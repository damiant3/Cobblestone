# compliance-report.ps1 -- Generate compliance evidence report
# GENERATED FROM THE CODEX SHELL DSL. Do not edit by hand.
# A hand edit here must NOT be submitted. Change the generator under
# codex/build/, regenerate, and submit the generator and this file
# together. Until then build/check-generated-scripts.ps1 reports this
# file as drifted, and the next regeneration discards the edit.
[CmdletBinding()]
param(
    [int]$PCore = 1
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$compile = (Join-Path $PSScriptRoot 'compile.ps1')
$run = (Join-Path $PSScriptRoot 'test-run.ps1')
$src = (Join-Path $PSScriptRoot 'compliance-report.codex')
$outDir = (Join-Path $PSScriptRoot 'output')
$cdx = (Join-Path $outDir 'compliance-report.cdx')
$log = (Join-Path $outDir 'compliance-report.log')
$report = (Join-Path $outDir 'compliance-evidence.txt')


if ((-not (Test-Path -PathType Container $outDir))) {
    New-Item -ItemType Directory -Force $outDir | Out-Null
}


Write-Host 'Compiling compliance report generator...'
& $compile -Src $src -Out $cdx -Log $log -PCore $PCore
if ((-not ($LASTEXITCODE -eq 0))) {
    Write-Host 'FAIL: compliance report compilation failed'
    if ((Test-Path -PathType Leaf $log)) {
        Get-Content $log
    }
    exit 1
}


Write-Host 'Running compliance report...'
& $run -Kernel $cdx -OutFile $report -PCore $PCore
if ((-not ($LASTEXITCODE -eq 0))) {
    Write-Host 'FAIL: compliance report execution failed'
    exit 1
}


$lines = (Get-Content $report | Measure-Object -Line).Lines
Write-Host ([string]([string]([string]'Compliance evidence report generated: ' + $report) + ' (') + ([string]$lines + ' lines)'))
Write-Host ''
Get-Content $report
