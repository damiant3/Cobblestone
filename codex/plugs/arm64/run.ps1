# Run the ARM64 codegen plug: send IR text via serial, receive wire output.
#
# Usage:
#   plugs/arm64/run.ps1 -IrInput <file.ir> -Out <file.bin>
#
# The output is the binary wire protocol:
#   [4B code-len] [4B data-len] [4B func-count]
#   [code bytes] [data bytes]
#   [func entries: 2B name-len + name + 4B offset each]
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string]$IrInput,
    [Parameter(Mandatory=$true)] [string]$Out,
    # Emit the PSCI CPU_ON sequence in __start. Only pass this for a program
    # that wants a secondary core: the conduit is HVC, which is undefined on
    # boards without PSCI (the committed Renode board), where it traps and
    # parks the guest before `opening` runs. See CL 8221.
    [switch]$Smp
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..' '..' '..' 'build' 'vm-config.ps1')

$Repo     = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
$PlugDir  = (Resolve-Path $PSScriptRoot).Path
$PlugCdx  = Join-Path $PlugDir 'build-output\arm64-plug.cdx'

if (-not (Test-Path -PathType Leaf $PlugCdx)) {
    [Console]::Error.WriteLine("MISSING: $PlugCdx -- run plugs/arm64/build.ps1 first")
    exit 2
}
if (-not (Test-Path -PathType Leaf $IrInput)) {
    [Console]::Error.WriteLine("MISSING: $IrInput")
    exit 2
}

$irBytes = [System.IO.File]::ReadAllBytes($IrInput)
Write-Host "[arm64-run] Input: $($irBytes.Length) bytes from $IrInput"

# Build input: CCE mode header + CCE IR + null terminator
$inputFile = [System.IO.Path]::GetTempFileName()
$hdrList = [System.Collections.Generic.List[byte]]::new()
$modeText = if ($Smp) { "IR-CCE smp" } else { "IR-CCE" }
foreach ($ch in $modeText.ToCharArray()) {
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
Write-Host "[arm64-run] OK: $Out ($($outputBytes.Length) bytes)"

# Show any WARN/WCET lines from serial. The first report line follows the
# binary wire with no newline between, so match anywhere in the line.
$serialText = ""
try { $serialText = [System.Text.Encoding]::UTF8.GetString($outputBytes) } catch {}
$unsupported = @()
foreach ($sl in ($serialText -split "`n")) {
    $m = [regex]::Match($sl.TrimEnd("`r"), '\[(WARN|WCET|UNSUPPORTED)\].*$')
    if ($m.Success) {
        Write-Host "[arm64-run] $($m.Value)" -ForegroundColor Yellow
        if ($m.Value.StartsWith('[UNSUPPORTED]')) { $unsupported += $m.Value }
    }
}

# An UNSUPPORTED report is a refusal, not a warning. The plug emitted a
# placeholder for a call it cannot serve, and shipping that binary is how a
# read of a file came back as "" and was mistaken for an empty file. Fail the
# build here so the refusal reaches whoever ran it.
if ($unsupported.Count -gt 0) {
    [Console]::Error.WriteLine("FAIL: $($unsupported.Count) call(s) this target cannot serve:")
    foreach ($u in $unsupported) { [Console]::Error.WriteLine("  $u") }
    Remove-Item -Force $inputFile, $outFile, $errFile -ErrorAction SilentlyContinue
    exit 6
}

Remove-Item -Force $inputFile -ErrorAction SilentlyContinue
Remove-Item -Force $outFile -ErrorAction SilentlyContinue
Remove-Item -Force $errFile -ErrorAction SilentlyContinue
