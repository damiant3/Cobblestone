# stress-sweep.ps1 -- Stress loop: re-run test.ps1 -Jobs N until killed or test fails
# GENERATED FROM THE CODEX SHELL DSL. Do not edit by hand.
# A hand edit here must NOT be submitted. Change the generator under
# codex/build/, regenerate, and submit the generator and this file
# together. Until then build/check-generated-scripts.ps1 reports this
# file as drifted, and the next regeneration discards the edit.
[CmdletBinding()]
param(
    [int]$Jobs = 8,
    [switch]$Pin,
    [switch]$ContinueOnFailure,
    [string]$LogPath = 'build-output\stress-sweep.log',
    [string]$ApprovedBy
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Continue'
Set-Location (Join-Path $PSScriptRoot '..')
[Environment]::CurrentDirectory = (Get-Location).Path


$sweepScript = (Join-Path $PSScriptRoot 'test.ps1')
New-Item -ItemType Directory -Force (Split-Path $LogPath) | Out-Null
Set-Content -Path $LogPath -Value ([string]([string]'=== stress-sweep start jobs=' + $Jobs) + ([string]' at ' + ([string](Get-Date).ToString('yyyy-MM-dd HH:mm:ss') + ' ==='))) -Encoding UTF8


$iter = 0
:stress_loop while ($true) {
    $iter++
    $start = (Get-Date)
    Add-Content -Path $LogPath -Value ([string]([string]([string]'[iter ' + $iter) + '] ') + ([string]'start ' + $start.ToString('HH:mm:ss'))) -Encoding UTF8


    $tmp = [System.IO.Path]::GetTempFileName()
    try {
        $sweepArgs = @('-NoProfile', '-File', $sweepScript, '-Jobs', $Jobs, '-ApprovedBy', $ApprovedBy)
        if ($Pin) {
            $sweepArgs += '-Pin'
        }
        & 'pwsh' @sweepArgs *>&1 | Tee-Object -FilePath $tmp | Out-Null
        $exit = $LASTEXITCODE
        $tail = (Get-Content -Path $tmp -Tail 6) -join ' | '
    } catch {
        $exit = -1
        $tail = ([string]'exception: ' + $_)
    } finally {
        Remove-Item -Force -ErrorAction SilentlyContinue $tmp
    }


    $dur = [int]((Get-Date) - $start).TotalSeconds
    Add-Content -Path $LogPath -Value ([string]([string]([string]'[iter ' + $iter) + '] ') + ([string]([string]'end exit=' + $exit) + ([string]([string]' dur=' + $dur) + ([string]'s tail=' + $tail)))) -Encoding UTF8


    if ((-not ($exit -eq 0))) {
        if ($ContinueOnFailure) {
            Add-Content -Path $LogPath -Value ([string]([string]([string]'[iter ' + $iter) + '] ') + ([string]([string]'FAILED exit=' + $exit) + ' -- continuing (-ContinueOnFailure)')) -Encoding UTF8
        } else {
            Add-Content -Path $LogPath -Value ([string]([string]([string]'[iter ' + $iter) + '] ') + ([string]([string]'FAILED exit=' + $exit) + ' -- stopping')) -Encoding UTF8
            exit 1
        }
    }

}
