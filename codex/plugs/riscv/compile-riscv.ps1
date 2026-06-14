# Compile a Codex source file to a RISC-V ELF binary.
#
# Usage:
#   plugs/riscv/compile-riscv.ps1 -Src <source.codex> -Out <out.elf>
#
# Pipeline:
#   source.codex -> compiler (IR mode) -> RISC-V codegen plug -> wire bytes
#   -> parse wire -> build ELF64 -> out.elf
#
# The RISC-V plug must be built first: plugs/riscv/build.ps1
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string]$Src,
    [Parameter(Mandatory=$true)] [string]$Out,
    [int]$MemMB = 4096
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..' '..' '..' 'build' 'vm-config.ps1')

$Repo     = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
$PlugDir  = (Resolve-Path $PSScriptRoot).Path
$OutDir   = Join-Path $PlugDir 'build-output'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# Phase 1: compile to IR
$IrFile  = Join-Path $OutDir 'last-compile.ir'
$LogFile = Join-Path $OutDir 'compile-ir.log'
$compileScript = Join-Path $Repo 'build' 'compile.ps1'
Write-Host "[riscv-compile] Compiling $Src to IR..."
& pwsh -NoProfile -File $compileScript -Src $Src -Out $IrFile -Log $LogFile -IrCce -MemMB $MemMB
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
$wireBytes = [System.IO.File]::ReadAllBytes($WireFile)
$codeLen = [BitConverter]::ToInt32($wireBytes, 0)
$dataLen = [BitConverter]::ToInt32($wireBytes, 4)
$funcCount = [BitConverter]::ToInt32($wireBytes, 8)
Write-Host "[riscv-compile] Wire: code=$codeLen data=$dataLen funcs=$funcCount"

$codeStart = 12
$dataStart = $codeStart + $codeLen
$code = New-Object byte[] $codeLen
[Array]::Copy($wireBytes, $codeStart, $code, 0, $codeLen)
$data = New-Object byte[] $dataLen
[Array]::Copy($wireBytes, $dataStart, $data, 0, $dataLen)

# Parse function table to find entry point
$funcOff = $dataStart + $dataLen
$entryOffset = 0
$off = $funcOff
for ($fi = 0; $fi -lt $funcCount; $fi++) {
    $nameLen = [BitConverter]::ToInt16($wireBytes, $off)
    $nameChars = [char[]]::new($nameLen)
    for ($ci = 0; $ci -lt $nameLen; $ci++) {
        $cce = $wireBytes[$off + 2 + $ci]
        $nameChars[$ci] = if ($cce -lt $script:CceToUnicode.Length) { [char]$script:CceToUnicode[$cce] } else { [char]63 }
    }
    $name = [string]::new($nameChars)
    $funcOffset = [BitConverter]::ToInt32($wireBytes, $off + 2 + $nameLen)
    if ($name -eq '__start') { $entryOffset = $funcOffset }
    $off += 2 + $nameLen + 4
}

# Build ELF64 for RISC-V
$loadAddr = 2147483648  # 0x80000000 — QEMU virt RISC-V convention
$headerSize = 64
$phdrSize = 56
$phdrCount = 1
$headersEnd = $headerSize + $phdrSize * $phdrCount
$textStart = [int](($headersEnd + 15) -band 0xFFFFFFF0)
$textEnd = $textStart + $codeLen
$rodataStart = [int](($textEnd + 7) -band 0xFFFFFFF8)
[uint64]$entry = $loadAddr + [uint64]$textStart + [uint64]$entryOffset
$segFilesz = $rodataStart + $dataLen - $textStart
$segMemsz = $segFilesz + 0x1000000

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
if ($padding -gt 0) { $bw.Write([byte[]]::new($padding)) }
$bw.Write($code)
$rodataPad = $rodataStart - $textEnd
if ($rodataPad -gt 0) { $bw.Write([byte[]]::new($rodataPad)) }
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

$sz = (Get-Item $Out).Length
Write-Host "[riscv-compile] OK: $Out ($sz bytes, entry=0x$($entry.ToString('X')))"
Write-Host "[riscv-compile] Flat: $flatOut ($($flatData.Length) bytes)"
