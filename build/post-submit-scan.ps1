# Post-submit hook: scan source tree and record facts after p4 submit.
# Run manually or wire into agent workflow as a post-submit step.
#
# Usage:
#   build/post-submit-scan.ps1                    # scan and save manifest
#   build/post-submit-scan.ps1 -Cl 4982           # tag manifest with CL number
#   build/post-submit-scan.ps1 -Submit            # submit manifest to Perforce
[CmdletBinding()]
param(
    [int]$Cl = 0,
    [switch]$Submit
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$OutDir = Join-Path $Repo 'build' 'output'
$ManifestPath = Join-Path $OutDir 'source-facts.manifest'

Write-Host "=== Post-Submit Source Scan ==="

if ($Cl -gt 0) {
    Write-Host "  CL: $Cl"
}

$scanStart = Get-Date
& (Join-Path $PSScriptRoot 'scan-source-facts.ps1') -Root $Repo -OutFile $ManifestPath
$scanEnd = Get-Date
$elapsed = ($scanEnd - $scanStart).TotalSeconds

$lineCount = (Get-Content $ManifestPath | Measure-Object -Line).Lines
$hash = (Get-FileHash -Algorithm SHA256 $ManifestPath).Hash.ToLower()

Write-Host "  Manifest: $ManifestPath"
Write-Host "  Files: $lineCount"
Write-Host "  SHA-256: $hash"
Write-Host "  Elapsed: $([math]::Round($elapsed, 1))s"

if ($Cl -gt 0) {
    $tagFile = Join-Path $OutDir "source-facts-cl$Cl.manifest"
    Copy-Item $ManifestPath $tagFile -Force
    Write-Host "  Tagged: $tagFile"
}

if ($Submit) {
    $depotManifest = Join-Path $Repo 'seed' 'source-facts.manifest'
    Copy-Item $ManifestPath $depotManifest -Force
    $opened = & p4 opened $depotManifest 2>&1
    if ($opened -match 'not opened') {
        & p4 edit $depotManifest 2>&1 | Out-Null
    }
    Write-Host "  Depot: seed/source-facts.manifest ready for submit"
}

Write-Host "=== Scan Complete ==="
