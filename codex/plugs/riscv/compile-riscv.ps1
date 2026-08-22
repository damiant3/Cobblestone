# compile-riscv.ps1 -- Compile a Codex source file to a RISC-V ELF binary
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
    [string]$Kernel = ''
)

# Usage:
#   plugs/riscv/compile-riscv.ps1 -Src <source.codex> -Out <out.elf>
# 
# Pipeline:
#   source.codex -> compiler (IR mode) -> RISC-V codegen plug -> wire bytes
#   -> parse wire -> build ELF64 -> out.elf
# 
# The RISC-V plug must be built first: plugs/riscv/build.ps1


Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..' '..' '..' 'build' 'vm-config.ps1')

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
$PlugDir = (Resolve-Path $PSScriptRoot).Path
$OutDir = if ($WorkDir) { $WorkDir } else { Join-Path $PlugDir 'build-output' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null


# Phase 1: compile to IR
$IrFile = Join-Path $OutDir 'last-compile.ir'
$LogFile = Join-Path $OutDir 'compile-ir.log'
$compileScript = Join-Path $Repo 'build' 'compile.ps1'
$KernelCdx = if ($Kernel) { $Kernel } else { Join-Path $Repo 'seed' 'Codex.cdx' }
Write-Host "[riscv-compile] Compiling $Src to IR..."
& pwsh -NoProfile -File $compileScript -Src $Src -Out $IrFile -Log $LogFile -IrCce -MemMB $MemMB -Kernel $KernelCdx
if ($LASTEXITCODE -ne 0) {
    [Console]::Error.WriteLine("FAIL: IR compile step exited $LASTEXITCODE; see $LogFile")
    exit 3
}


# Phase 2: codegen via RISC-V plug
$WireFile = Join-Path $OutDir 'last-compile.riscv.bin'
$RunScript = Join-Path $PlugDir 'run.ps1'
Write-Host "[riscv-compile] Running RISC-V codegen plug..."
& pwsh -NoProfile -File $RunScript -IrInput $IrFile -Out $WireFile
if ($LASTEXITCODE -ne 0) {
    [Console]::Error.WriteLine("FAIL: RISC-V codegen plug exited $LASTEXITCODE")
    exit 4
}


# Phase 3: parse wire protocol and build ELF64
$rawWire = [System.IO.File]::ReadAllBytes($WireFile)
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
Write-Host "[riscv-compile] Wire: code=$codeLen data=$dataLen funcs=$funcCount"


# Extract code and data
$codeStart = 12
$dataStart = $codeStart + $codeLen
$code = New-Object byte[] $codeLen
[Array]::Copy($wireBytes, $codeStart, $code, 0, $codeLen)
$data = New-Object byte[] $dataLen
[Array]::Copy($wireBytes, $dataStart, $data, 0, $dataLen)


# Parse function table (entry point + symbol map).
$funcOff = $dataStart + $dataLen
$entryOffset = 0
$funcEntries = [System.Collections.Generic.List[PSObject]]::new()
for ($fi = 0; $fi -lt $funcCount -and $funcOff + 2 -lt $wireBytes.Length; $fi++) {
    $nameLen = ([BitConverter]::ToInt16($wireBytes, $funcOff))
    # nameLen is a BYTE count, not a character count. The offset arithmetic below
    # advances by it either way, but the name is a CCE stream, and reading it one
    # byte per character answers '?' for everything above tier 0.
    # The $nameLen -gt 0 guard is not defensive padding: PowerShell's range operator
    # counts DOWN when the end is below the start, so a zero-length name would slice
    # $wireBytes[n+2..n+1] and yield two bytes rather than none.
    $fname = if ($nameLen -gt 0) { ConvertFrom-CceBytes $wireBytes[($funcOff + 2)..($funcOff + 1 + $nameLen)] } else { '' }
    $foff = ([BitConverter]::ToInt32($wireBytes, $funcOff + 2 + $nameLen))
    $funcEntries.Add([PSCustomObject]@{ Name = $fname; Offset = $foff })
    $funcOff = $funcOff + 2 + $nameLen + 4
}

# The plug appends a Unicode name manifest (FUNCMAP-BEGIN .. FUNCMAP-END,
# rows "<byte-offset> <name>") after the binary wire. Prefer those names --
# they are correct Unicode and need no CCE decoding. Match by byte offset.
$manifestText = [System.Text.Encoding]::UTF8.GetString($rawWire)
$nameByOffset = @{}
$inFuncMap = $false
foreach ($mline in ($manifestText -split "`n")) {
    $mline = $mline.TrimEnd("`r")
    if ($mline -match 'FUNCMAP-BEGIN') { $inFuncMap = $true; continue }
    if ($mline -match 'FUNCMAP-END') { break }
    if ($inFuncMap -and $mline -match '^(\d+)\s+(.+)$') { $nameByOffset[[int]$matches[1]] = $matches[2] }
}
if ($nameByOffset.Count -gt 0) {
    foreach ($fe in $funcEntries) { if ($nameByOffset.ContainsKey([int]$fe.Offset)) { $fe.Name = $nameByOffset[[int]$fe.Offset] } }
}

if ($funcEntries.Count -gt 0) {
    $entryOffset = $funcEntries[0].Offset
}


# Build ELF64 for RISC-V
$loadAddr = 2147483648  # 0x80000000 -- QEMU virt RISC-V convention
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
# Measured on the ARM64 twin of this line, 2026-07-21: the quarter-gigabyte
# zero-fill costs about 130 ms per test, one per cent, and dropping it does
# not make eight Renode slots stop flaking either. A test is 12.6s because
# RenoTimeout is a flat 10s sleep plus ~2.6s of Renode start and teardown.
$segMemsz = $segFilesz + 0x0F000000


$elf = [System.IO.MemoryStream]::new()
$bw = [System.IO.BinaryWriter]::new($elf)

# ELF64 header
$bw.Write([byte[]]@(0x7F, 0x45, 0x4C, 0x46))
$bw.Write([byte]2)     # 64-bit
$bw.Write([byte]1)     # LSB
$bw.Write([byte]1)     # version
$bw.Write([byte[]]::new(9))
$bw.Write([uint16]2)   # EXEC
$bw.Write([uint16]243) # EM_RISCV
$bw.Write([uint32]1)
$bw.Write([uint64]$entry)
$bw.Write([uint64]$headerSize)
$bw.Write([uint64]0)


$bw.Write([uint32]0)   # flags
$bw.Write([uint16]$headerSize)
$bw.Write([uint16]$phdrSize)
$bw.Write([uint16]$phdrCount)
$bw.Write([uint16]0)
$bw.Write([uint16]0)
$bw.Write([uint16]0)

# Program header (single RWX LOAD)
$bw.Write([uint32]1)   # PT_LOAD
$bw.Write([uint32]7)   # PF_RWX
$bw.Write([uint64]$textStart)
$bw.Write([uint64]($loadAddr + [uint64]$textStart))
$bw.Write([uint64]($loadAddr + [uint64]$textStart))


$bw.Write([uint64]$segFilesz)
$bw.Write([uint64]$segMemsz)
$bw.Write([uint64]0x1000)

$padding = $textStart - $headersEnd
if ($padding -gt 0) {
    $bw.Write([byte[]]::new($padding))
}
$bw.Write($code)
$rodataPad = $rodataStart - $textEnd
if ($rodataPad -gt 0) {
    $bw.Write([byte[]]::new($rodataPad))
}
$bw.Write($data)

$bw.Flush()
[System.IO.File]::WriteAllBytes($Out, $elf.ToArray())
$bw.Close()


# Also produce a flat binary for -bios none (QEMU jumps to 0x80000000 regardless of ELF entry)
$flatOut = [System.IO.Path]::ChangeExtension($Out, '.bin')
$flatData = New-Object byte[] ($codeLen + ($rodataStart - $textEnd) + $dataLen)
[Array]::Copy($code, 0, $flatData, 0, $codeLen)
if ($dataLen -gt 0) {
    $rodataPadFlat = $rodataStart - $textEnd
    [Array]::Copy($data, 0, $flatData, $codeLen + $rodataPadFlat, $dataLen)
}
[System.IO.File]::WriteAllBytes($flatOut, $flatData)


# Write symbol map
$mapFile = [System.IO.Path]::ChangeExtension($Out, '.map')
$mapLines = [System.Collections.Generic.List[string]]::new()
[void]$mapLines.Add('# RISC-V Symbol Map')
[void]$mapLines.Add('# Address         Size  Name')
for ($mi = 0; $mi -lt $funcEntries.Count; $mi++) {
    $fe = $funcEntries[$mi]
    [uint64]$addr = $loadAddr + [uint64]$textStart + [uint64]$fe.Offset
    $nextOff = if ($mi + 1 -lt $funcEntries.Count) { $funcEntries[$mi + 1].Offset } else { $codeLen }
    $fsize = $nextOff - $fe.Offset
    [void]$mapLines.Add("0x$($addr.ToString('X8').PadLeft(8,'0')) $fsize $($fe.Name)")

}
[System.IO.File]::WriteAllLines($mapFile, $mapLines)


# The remap window. rv-remap-addr-insns adds 0x80000000 to any address whose
# high bits are clear, so a test written against the x86-64 low map reads its
# cells out of RAM. That window lands at [0x80000000, 0x80000000 + threshold),
# which is exactly where this image is loaded, so an image that grows past the
# threshold overlaps the addresses the remap hands out and a low peek starts
# reading the guest's own code.
# 
# The threshold is READ from rv-remap-addr-insns rather than restated: it is
# the shift the remap tests, so 24 means 16 MB. An unmatched pattern is a
# FAILURE, not a skip -- a check whose regex stopped matching has quietly
# stopped asking (build/build-arm64-img.ps1 states the same rule for the DMA
# floor, and check-doc-counts.ps1 before it).
$rvRuntime = Join-Path $PSScriptRoot 'RiscVRuntime.codex'
if (-not (Test-Path -PathType Leaf $rvRuntime)) {
    Write-Host "FAIL: remap window check cannot read $rvRuntime"
    exit 8
}
$rvShiftM = [regex]::Match([System.IO.File]::ReadAllText($rvRuntime), 'rv-srli rv-t0 addr-reg (\d+)')
if (-not $rvShiftM.Success) {
    Write-Host "FAIL: the remap shift in $rvRuntime no longer matches this check's pattern."
    Write-Host '      A check whose regex stopped matching has quietly stopped asking. Fix the pattern.'
    exit 8
}
$rvRemapWindow = [uint64]1 -shl [int]$rvShiftM.Groups[1].Value
if ([uint64]$flatData.Length -ge $rvRemapWindow) {
    Write-Host ''
    Write-Host 'FAIL: the RISC-V image has grown into the address-remap window.'
    Write-Host ("  image      {0} bytes, loaded at 0x{1:X}" -f $flatData.Length, $loadAddr)
    Write-Host ("  remap window 0x{0:X} bytes (rv-remap-addr-insns shifts by {1})" -f $rvRemapWindow, $rvShiftM.Groups[1].Value)
    Write-Host ("  overrun    {0} bytes" -f ([uint64]$flatData.Length - $rvRemapWindow))
    Write-Host ''
    Write-Host '  Every address below the window is remapped to 0x80000000 + addr, which is'
    Write-Host '  now inside this image: a low peek reads the guest own code instead of the'
    Write-Host '  cell it asked for, silently. Raise the shift in rv-remap-addr-insns and the'
    Write-Host '  matching rule in docs/Designs/Done/Compiler/RiscVProcessKernel.md, or shrink'
    Write-Host '  the image.'
    exit 8
}
Write-Host ("[riscv-compile] Remap window ok: image {0} bytes, window 0x{1:X}, {2} bytes clear" -f $flatData.Length, $rvRemapWindow, ($rvRemapWindow - [uint64]$flatData.Length))


$sz = (Get-Item $Out).Length
Write-Host "[riscv-compile] OK: $Out ($sz bytes, entry=0x$($entry.ToString('X')))"
Write-Host "[riscv-compile] Flat: $flatOut ($($flatData.Length) bytes)"
Write-Host "[riscv-compile] Map: $mapFile ($($funcEntries.Count) functions)"
