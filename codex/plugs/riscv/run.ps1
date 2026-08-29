# Run the RISC-V codegen plug: send IR text via serial, receive wire output.
#
# Usage:
#   plugs/riscv/run.ps1 -IrInput <file.ir> -Out <file.bin>
#
# The output is the binary wire protocol:
#   [4B code-len] [4B data-len] [4B func-count]
#   [code bytes] [data bytes]
#   [func entries: 2B name-len + name + 4B offset each]
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string]$IrInput,
    [Parameter(Mandatory=$true)] [string]$Out
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..' '..' '..' 'build' 'vm-config.ps1')

$PlugDir  = (Resolve-Path $PSScriptRoot).Path
$PlugCdx  = Join-Path $PlugDir 'build-output\riscv-plug.cdx'

if (-not (Test-Path -PathType Leaf $PlugCdx)) {
    [Console]::Error.WriteLine("MISSING: $PlugCdx -- run plugs/riscv/build.ps1 first")
    exit 2
}
if (-not (Test-Path -PathType Leaf $IrInput)) {
    [Console]::Error.WriteLine("MISSING: $IrInput")
    exit 2
}

$irBytes = [System.IO.File]::ReadAllBytes($IrInput)
Write-Host "[riscv-run] Input: $($irBytes.Length) bytes from $IrInput"

# Build input: CCE mode header + CCE IR + null terminator
$inputFile = [System.IO.Path]::GetTempFileName()
$hdrList = [System.Collections.Generic.List[byte]]::new()
foreach ($ch in "IR-CCE".ToCharArray()) {
    $u = [int]$ch
    if ($u -lt 256) { $hdrList.Add([byte]$script:UnicodeToCce[$u]) }
}
$hdrList.Add([byte]1)  # CCE newline
$modeHeader = $hdrList.ToArray()
$combined = New-Object byte[] ($modeHeader.Length + $irBytes.Length + 1)
[Buffer]::BlockCopy($modeHeader, 0, $combined, 0, $modeHeader.Length)
[Buffer]::BlockCopy($irBytes, 0, $combined, $modeHeader.Length, $irBytes.Length)
$combined[$combined.Length - 1] = 0  # null terminator for read-file
[System.IO.File]::WriteAllBytes($inputFile, $combined)

# Run plug CDX via serial I/O
$outFile = [System.IO.Path]::GetTempFileName()
$errFile = [System.IO.Path]::GetTempFileName()
$vmOk = Invoke-PlugVmFileSerial -Kernel $PlugCdx -InputFile $inputFile -OutputFile $outFile -StderrFile $errFile -MemMB 3072 -TimeoutSec 300

if (-not $vmOk) {
    [Console]::Error.WriteLine("FAIL: plug timed out")
    exit 5
}

# Read serial output (binary wire data)
$outputBytes = [System.IO.File]::ReadAllBytes($outFile)

[System.IO.File]::WriteAllBytes($Out, $outputBytes)

# A guest FAULT is not an emission. The plug prints a register dump beginning
# `!EXC=` and exits cleanly, so without this the fault is written to $Out, this
# script prints OK and exits 0, and the caller grades a 947-byte register dump
# as a binary. Two such dumps even DIFFER from each other -- the RIP moves with
# the plug build -- so a control-versus-fix byte comparison over them reads as
# "the change moved the output" when both arms crashed and neither emitted
# anything (reek, 2026-08-28, an hour spent before the bytes were read).
# Feeding IR-UNI where the plug wants IR-CCE is one way to land here.
# The dump may carry codex-vm's leading 0x01 marker, so the tag is FOUND in the
# first few bytes rather than compared at offset zero. An anchored compare reads
# \x01!EX and misses every fault that carries the marker, which is the shape
# that actually arrives here.
$leadAscii = [System.Text.Encoding]::ASCII.GetString($outputBytes, 0, [Math]::Min(8, $outputBytes.Length))
if ($leadAscii.Contains('!EXC')) {
    $dump = [System.Text.Encoding]::ASCII.GetString($outputBytes)
    [Console]::Error.WriteLine("FAIL: the plug FAULTED; $Out holds a register dump, not a wire.")
    [Console]::Error.WriteLine("  " + ($dump -split "`n")[0])
    Remove-Item -Force $inputFile, $outFile, $errFile -ErrorAction SilentlyContinue
    exit 7
}
if ($outputBytes.Length -eq 0) {
    [Console]::Error.WriteLine("FAIL: the plug produced no output.")
    Remove-Item -Force $inputFile, $outFile, $errFile -ErrorAction SilentlyContinue
    exit 7
}
Write-Host "[riscv-run] OK: $Out ($($outputBytes.Length) bytes)"

# Show any WARN/WCET/UNSUPPORTED lines from serial. Match anywhere in the
# line, not StartsWith: the first report line follows the binary wire with no
# newline between them, so an anchored test silently drops it. That is why the
# arm64 side already matches this way.
$serialText = ""
$unsupported = @()
try { $serialText = [System.Text.Encoding]::UTF8.GetString($outputBytes) } catch {}
foreach ($sl in ($serialText -split "`n")) {
    $m = [regex]::Match($sl.TrimEnd("`r"), '\[(WARN|WCET|UNSUPPORTED)\].*$')
    if ($m.Success) {
        Write-Host "[riscv-run] $($m.Value)" -ForegroundColor Yellow
        if ($m.Value.StartsWith('[UNSUPPORTED]')) { $unsupported += $m.Value }
    }
}

# An UNSUPPORTED report is a refusal, not a warning. See the arm64 run script.
if ($unsupported.Count -gt 0) {
    [Console]::Error.WriteLine("FAIL: $($unsupported.Count) call(s) this target cannot serve:")
    foreach ($u in $unsupported) { [Console]::Error.WriteLine("  $u") }
    Remove-Item -Force $inputFile, $outFile, $errFile -ErrorAction SilentlyContinue
    exit 6
}

Remove-Item -Force $inputFile -ErrorAction SilentlyContinue
Remove-Item -Force $outFile -ErrorAction SilentlyContinue
Remove-Item -Force $errFile -ErrorAction SilentlyContinue
