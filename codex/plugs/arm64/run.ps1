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
    [Parameter(Mandatory=$true)] [string]$Out
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
$proc = Start-Process -FilePath $script:CodexVmBin -ArgumentList @('-kernel',$PlugCdx,'-input',$inputFile,'-output',$outFile,'-mem', '3072','-headless') -PassThru -WindowStyle Hidden -RedirectStandardError $errFile
$proc.WaitForExit(300000)

if (-not $proc.HasExited) {
    [Console]::Error.WriteLine("FAIL: plug timed out")
    try { Stop-Process -Id $proc.Id -Force } catch {}
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
foreach ($sl in ($serialText -split "`n")) {
    $m = [regex]::Match($sl.TrimEnd("`r"), '\[(WARN|WCET)\].*$')
    if ($m.Success) {
        Write-Host "[arm64-run] $($m.Value)" -ForegroundColor Yellow
    }
}

Remove-Item -Force $inputFile -ErrorAction SilentlyContinue
Remove-Item -Force $outFile -ErrorAction SilentlyContinue
Remove-Item -Force $errFile -ErrorAction SilentlyContinue
