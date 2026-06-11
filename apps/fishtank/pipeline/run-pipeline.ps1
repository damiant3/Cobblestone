# Run the full creature asset pipeline for one species.
# Orchestrates: plan -> reference -> mesh -> textures
# Stops at the first failure and reports status.
#
# Usage: pwsh apps/fishtank/pipeline/run-pipeline.ps1 -Species "Clownfish" -Family "Pomacentridae"
#        pwsh apps/fishtank/pipeline/run-pipeline.ps1 -Species "Brain Coral" -Kind coral -Family "Mussidae"
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Species,
    [string]$Kind = 'fish',
    [string]$Family = '',
    [string]$Scientific = '',
    [string]$Description = '',
    [string]$ForgeUrl = 'http://127.0.0.1:7860',
    [string]$TripoDir = 'D:\AI\TripoSR',
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$pipeDir = $PSScriptRoot

Write-Host "=== Creature Pipeline: $Species ===" -ForegroundColor Cyan

# Stage 1: Plan
Write-Host "`n[1/4] Planning..." -ForegroundColor Yellow
& pwsh -NoProfile -File (Join-Path $pipeDir 'plan-creature.ps1') -Species $Species -Kind $Kind -Family $Family -Scientific $Scientific -Description $Description
if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne $null) { Write-Error "Plan failed"; exit 1 }

# Stage 2: Reference images
Write-Host "`n[2/4] Generating reference images..." -ForegroundColor Yellow
& pwsh -NoProfile -File (Join-Path $pipeDir 'generate-reference.ps1') -Species $Species -ForgeUrl $ForgeUrl
if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne $null) { Write-Error "Reference generation failed"; exit 2 }

# Stage 3: 3D mesh
if ($Kind -in @('fish', 'invertebrate', 'coral', 'rock')) {
    Write-Host "`n[3/4] Generating 3D mesh..." -ForegroundColor Yellow
    & pwsh -NoProfile -File (Join-Path $pipeDir 'generate-mesh.ps1') -Species $Species -TripoDir $TripoDir
    if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne $null) { Write-Warning "Mesh generation failed (continuing to textures)" }
}

# Stage 4: Textures
Write-Host "`n[4/4] Generating textures..." -ForegroundColor Yellow
& pwsh -NoProfile -File (Join-Path $pipeDir 'generate-textures.ps1') -Species $Species -ForgeUrl $ForgeUrl
if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne $null) { Write-Warning "Texture generation had failures" }

# Summary
. (Join-Path $pipeDir 'creature-db.ps1')
$rec = Load-Creature $Species
Write-Host "`n=== Pipeline Complete ===" -ForegroundColor Green
Write-Host "Species: $($rec.name)"
Write-Host "Status:  $($rec.status)"
Write-Host "Mesh:    $(if ($rec.meshPath) { $rec.meshPath } else { '(none)' })"
$texCount = 0; $texDone = 0
foreach ($su in $rec.subUnits) { foreach ($t in $su.textures) { $texCount++; if ($t.status -eq 'done') { $texDone++ } } }
Write-Host "Textures: $texDone/$texCount"
Write-Host "DB path: $(Get-CreaturePath $Species)"
