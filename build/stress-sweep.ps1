# Stress loop: re-run test.ps1 -Jobs N until killed or test fails.
# Logs each iteration (start, duration, pass/fail counts) to a stress log.
[CmdletBinding()]
param(
    [int]$Jobs = 7,
    [switch]$Pin,
    [switch]$ContinueOnFailure,
    [string]$LogPath = 'build-output\stress-sweep.log'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Continue'
Set-Location (Join-Path $PSScriptRoot '..')
[Environment]::CurrentDirectory = (Get-Location).Path

$sweepScript = Join-Path $PSScriptRoot 'test.ps1'
$null = New-Item -ItemType Directory -Force -Path (Split-Path $LogPath)
Set-Content -Path $LogPath -Value "=== stress-sweep start jobs=$Jobs at $((Get-Date).ToString('yyyy-MM-dd HH:mm:ss')) ===`n" -Encoding UTF8

$iter = 0
while ($true) {
    $iter++
    $start = Get-Date
    Add-Content -Path $LogPath -Value "[iter $iter] start $($start.ToString('HH:mm:ss'))" -Encoding UTF8

    $tmp = [System.IO.Path]::GetTempFileName()
    try {
        $sweepArgs = @('-NoProfile','-File',$sweepScript,'-Jobs',$Jobs)
        if ($Pin) { $sweepArgs += '-Pin' }
        & pwsh @sweepArgs *>&1 | Tee-Object -FilePath $tmp | Out-Null
        $exit = $LASTEXITCODE
        $tail = (Get-Content -Path $tmp -Tail 6) -join ' | '
    } catch {
        $exit = -1
        $tail = "exception: $_"
    } finally {
        Remove-Item -Force $tmp -ErrorAction SilentlyContinue
    }

    $dur = [int]((Get-Date) - $start).TotalSeconds
    Add-Content -Path $LogPath -Value "[iter $iter] end exit=$exit dur=${dur}s tail=$tail" -Encoding UTF8

    if ($exit -ne 0) {
        if ($ContinueOnFailure) {
            Add-Content -Path $LogPath -Value "[iter $iter] FAILED exit=$exit -- continuing (-ContinueOnFailure)" -Encoding UTF8
        } else {
            Add-Content -Path $LogPath -Value "[iter $iter] FAILED exit=$exit -- stopping" -Encoding UTF8
            exit 1
        }
    }
}
