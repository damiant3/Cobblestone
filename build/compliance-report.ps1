# Generates a compliance evidence report as a build artifact.
# Compiles a tiny program that calls generate-evidence-report from the
# ComplianceEvidence foreword, runs it in the VM, and captures the output.
#
# Usage: pwsh build/compliance-report.ps1
# Output: build/output/compliance-evidence.txt
[CmdletBinding()]
param(
    [int]$PCore = 1
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$compile = Join-Path $PSScriptRoot 'compile.ps1'
$run     = Join-Path $PSScriptRoot 'test-run.ps1'
$src     = Join-Path $PSScriptRoot 'compliance-report.codex'
$outDir  = Join-Path $PSScriptRoot 'output'
$cdx     = Join-Path $outDir 'compliance-report.cdx'
$log     = Join-Path $outDir 'compliance-report.log'
$report  = Join-Path $outDir 'compliance-evidence.txt'

if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Force $outDir | Out-Null }

Write-Host "Compiling compliance report generator..."
& $compile -Src $src -Out $cdx -Log $log -PCore $PCore
if ($LASTEXITCODE -ne 0) {
    Write-Host "FAIL: compliance report compilation failed"
    if (Test-Path $log) { Get-Content $log | Write-Host }
    exit 1
}

Write-Host "Running compliance report..."
& $run -Kernel $cdx -OutFile $report -PCore $PCore
if ($LASTEXITCODE -ne 0) {
    Write-Host "FAIL: compliance report execution failed"
    exit 1
}

$lines = (Get-Content $report | Measure-Object -Line).Lines
Write-Host "Compliance evidence report generated: $report ($lines lines)"
Write-Host ""
Get-Content $report
