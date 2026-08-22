# compile-arm64.ps1 -- Compile a Codex source file to an ARM64 ELF binary
# GENERATED FROM THE CODEX SHELL DSL. Do not edit by hand.
# A hand edit here must NOT be submitted. Change the generator under
# codex/build/, regenerate, and submit the generator and this file
# together. Until then build/check-generated-scripts.ps1 reports this
# file as drifted, and the next regeneration discards the edit.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$Src,
    [Parameter(Mandatory=$true)]
    [string]$Out,
    [int]$MemMB = 3072,
    # Where the intermediate IR, its log, and the wire bytes are written. The
    # default keeps them as build-output/last-compile.* because that is the
    # file you reach for when one compile misbehaves. They are the only shared
    # paths in this pipeline (compile.ps1 and run.ps1 use GetTempFileName
    # throughout), so a caller running several compiles at once must give each
    # its own directory or they overwrite each other's IR.
    [string]$WorkDir = '',
    # Which compiler compiles the source to IR. Defaults to the DEPOT SEED,
    # and that default is the point: compile.ps1's own default is
    # build-output/bare-metal/Codex.cdx, which holds whatever the last
    # build.ps1 left there. That made the cross bed's answer a property of
    # the workspace rather than of the depot, so two agents measured the
    # same source against different compilers and disagreed. Pass -Kernel
    # only to test a compiler that is not the seed, and say so in the CL.
    [string]$Kernel = '',
    # Emit the PSCI CPU_ON sequence in __start (multi-core programs only).
    # Undefined on boards without PSCI -- see run.ps1.
    [switch]$Smp
)

# Usage:
#   plugs/arm64/compile-arm64.ps1 -Src <source.codex> -Out <out.elf>
# 
# Pipeline:
#   source.codex -> compiler (IR mode) -> ARM64 codegen plug -> wire bytes
#   -> parse wire -> build ELF64 -> out.elf
# 
# The ARM64 plug must be built first: plugs/arm64/build.ps1


Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..' '..' '..' 'build' 'vm-config.ps1')

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
$PlugDir = (Resolve-Path $PSScriptRoot).Path
$OutDir = if ($WorkDir) { $WorkDir } else { Join-Path $PlugDir 'build-output' }
New-Item -ItemType Directory -Force $OutDir | Out-Null


# Phase 1: compile to IR
$IrFile = Join-Path $OutDir 'last-compile.ir'
$LogFile = Join-Path $OutDir 'compile-ir.log'
$compileScript = Join-Path $Repo 'build' 'compile.ps1'
$KernelCdx = if ($Kernel) { $Kernel } else { Join-Path $Repo 'seed' 'Codex.cdx' }
Write-Host "[arm64-compile] Compiling $Src to IR..."
& pwsh -NoProfile -File $compileScript -Src $Src -Out $IrFile -Log $LogFile -IrCce -MemMB $MemMB -Kernel $KernelCdx
if ((-not ($LASTEXITCODE -eq 0))) {
    [Console]::Error.WriteLine("FAIL: IR compile step exited $LASTEXITCODE; see $LogFile")
    exit 3
}


# Phase 2: codegen via ARM64 plug
$WireFile = Join-Path $OutDir 'last-compile.arm64.bin'
$RunScript = Join-Path $PlugDir 'run.ps1'
Write-Host "[arm64-compile] Running ARM64 codegen plug..."
$runArgs = @('-NoProfile','-File',$RunScript,'-IrInput',$IrFile,'-Out',$WireFile)
if ($Smp) {
    $runArgs += '-Smp'
}
& pwsh @runArgs
if ((-not ($LASTEXITCODE -eq 0))) {
    [Console]::Error.WriteLine("FAIL: ARM64 codegen plug exited $LASTEXITCODE")
    exit 4
}


# Phase 3: parse wire protocol and build ELF64
$rawWire = [System.IO.File]::ReadAllBytes($WireFile)
# Skip leading serial preamble (find the wire header: first byte where
# a plausible code-len int32 lives)
$wireOff = 0
for ($wi = 0; $wi -lt [Math]::Min(64, $rawWire.Length - 12); $wi++) {
    $cl = [BitConverter]::ToInt32($rawWire, $wi)
    $dl = [BitConverter]::ToInt32($rawWire, $wi + 4)
    $fc = [BitConverter]::ToInt32($rawWire, $wi + 8)
    if ($cl -gt 0 -and $cl -lt 16000000 -and $dl -ge 0 -and $dl -lt 1000000 -and $fc -gt 0 -and $fc -lt 10000 -and ($wi + 12 + $cl + $dl) -le $rawWire.Length + 64) {
        $wireOff = $wi
        break
    }
}
$wireBytes = New-Object byte[] ($rawWire.Length - $wireOff)
[Array]::Copy($rawWire, $wireOff, $wireBytes, 0, $wireBytes.Length)
$codeLen = [BitConverter]::ToInt32($wireBytes, 0)
$dataLen = [BitConverter]::ToInt32($wireBytes, 4)
$funcCount = [BitConverter]::ToInt32($wireBytes, 8)
Write-Host "[arm64-compile] Wire: code=$codeLen data=$dataLen funcs=$funcCount"


# Extract code and data
$codeStart = 12
$dataStart = $codeStart + $codeLen
$code = New-Object byte[] $codeLen
[Array]::Copy($wireBytes, $codeStart, $code, 0, $codeLen)
$data = New-Object byte[] $dataLen
[Array]::Copy($wireBytes, $dataStart, $data, 0, $dataLen)


# Parse function table: extract entry point and build symbol map.
$funcOff = $dataStart + $dataLen
$entryOffset = 0
$funcEntries = [System.Collections.Generic.List[object]]::new()
$pos = $funcOff
for ($fi = 0; $fi -lt $funcCount; $fi++) {
    if ($pos + 6 -gt $wireBytes.Length) {
        break
    }
    $nameLen = [BitConverter]::ToInt16($wireBytes, $pos)

    # nameLen is a BYTE count, not a character count. The offset arithmetic below
    # advances by it either way, but the name is a CCE stream, and reading it one
    # byte per character answers '?' for everything above tier 0.
    # The $nameLen -gt 0 guard is not defensive padding: PowerShell's range operator
    # counts DOWN when the end is below the start, so a zero-length name would slice
    # $wireBytes[n+2..n+1] and yield two bytes rather than none.
    $fname = if ($nameLen -gt 0) { ConvertFrom-CceBytes $wireBytes[($pos + 2)..($pos + 1 + $nameLen)] } else { '' }
    $foff = [BitConverter]::ToInt32($wireBytes, $pos + 2 + $nameLen)
    if ($fi -eq 0) {
        $entryOffset = $foff
    }
    [void]$funcEntries.Add(@{ Name = $fname; Offset = $foff })
    $pos = $pos + 2 + $nameLen + 4

}


# Build ELF64 (minimal: single LOAD segment)
$loadAddr = [uint64]0x40100000
$headerSize = 64
$phdrSize = 56
$phdrCount = 1
$headersEnd = $headerSize + $phdrSize * $phdrCount
$textStart = [int](($headersEnd + 15) -band 0xFFFFFFF0)
$textEnd = $textStart + $codeLen
$rodataStart = [int](($textEnd + 7) -band 0xFFFFFFF8)
[uint64]$entry = $loadAddr + [uint64]$textStart + [uint64]$entryOffset
$segFilesz = $rodataStart + $dataLen - $textStart
# The 240 MB heap reservation looks like the reason the run phase is slow --
# every renode.log says "Loading block of 251669168 bytes" -- and it is not.
# Measured 2026-07-21, same ELF, same board, only this field changed: three
# runs at 12.54/12.55/12.61s against three at 12.69/12.76/12.65s. The
# quarter-gigabyte zero-fill costs about 130 ms, one per cent, and dropping it
# does not make eight Renode slots stop flaking either. A test is 12.6s
# because RenoTimeout is a flat 10s sleep plus ~2.6s of Renode start and
# teardown; that sleep is the whole cost. Left as it was: this is a program
# header on every ELF for both architectures, a loader other than Renode may
# rightly rely on the zero-fill, and one per cent does not buy that risk.
$segMemsz = $segFilesz + 0x0F000000  # 240MB heap


$elf = [System.IO.MemoryStream]::new()
$bw = [System.IO.BinaryWriter]::new($elf)
# ELF64 header
$bw.Write([byte[]]@(0x7F, 0x45, 0x4C, 0x46))  # magic
$bw.Write([byte]2)    # class: 64-bit
$bw.Write([byte]1)    # data: LSB
$bw.Write([byte]1)    # version
$bw.Write([byte[]]::new(9))  # padding
$bw.Write([uint16]2)  # type: EXEC
$bw.Write([uint16]183) # machine: AArch64
$bw.Write([uint32]1)  # version
$bw.Write([uint64]$entry)  # entry
$bw.Write([uint64]$headerSize) # phoff

$bw.Write([uint64]0)  # shoff
$bw.Write([uint32]0)  # flags
$bw.Write([uint16]$headerSize) # ehsize
$bw.Write([uint16]$phdrSize) # phentsize
$bw.Write([uint16]$phdrCount) # phnum
$bw.Write([uint16]0)  # shentsize
$bw.Write([uint16]0)  # shnum
$bw.Write([uint16]0)  # shstrndx

# Program header (single RWX LOAD)
$bw.Write([uint32]1)  # PT_LOAD
$bw.Write([uint32]7)  # PF_RWX
$bw.Write([uint64]$textStart) # offset
$bw.Write([uint64]($loadAddr + $textStart)) # vaddr
$bw.Write([uint64]($loadAddr + $textStart)) # paddr
$bw.Write([uint64]$segFilesz) # filesz
$bw.Write([uint64]$segMemsz) # memsz
$bw.Write([uint64]0x1000) # align

# Padding to text start
$padding = $textStart - $headersEnd
if ($padding -gt 0) {
    $bw.Write([byte[]]::new($padding))
}
# Code
$bw.Write($code)
# Padding to rodata
$rodataPad = $rodataStart - $textEnd
if ($rodataPad -gt 0) {
    $bw.Write([byte[]]::new($rodataPad))
}
# Data
$bw.Write($data)
$bw.Flush()
[System.IO.File]::WriteAllBytes($Out, $elf.ToArray())
$bw.Close()



# Write symbol map
$mapFile = [System.IO.Path]::ChangeExtension($Out, '.map')
$mapLines = [System.Collections.Generic.List[string]]::new()
[void]$mapLines.Add('# ARM64 Symbol Map'); [void]$mapLines.Add('# Address         Size  Name')
for ($mi = 0; $mi -lt $funcEntries.Count; $mi++) {
    $fe = $funcEntries[$mi]
    [uint64]$addr = $loadAddr + [uint64]$textStart + [uint64]$fe.Offset
    $nextOff = if ($mi + 1 -lt $funcEntries.Count) { $funcEntries[$mi + 1].Offset } else { $codeLen }
    $fsize = $nextOff - $fe.Offset
    [void]$mapLines.Add("0x$($addr.ToString('X8').PadLeft(8,'0')) $fsize $($fe.Name)")
}

[System.IO.File]::WriteAllLines($mapFile, $mapLines)
$sz = (Get-Item $Out).Length
Write-Host "[arm64-compile] OK: $Out ($sz bytes, entry=0x$($entry.ToString('X')), map=$mapFile)"
