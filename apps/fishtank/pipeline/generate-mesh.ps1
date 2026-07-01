# Generate 3D mesh from reference image via TripoSR.
#
# Usage: pwsh apps/fishtank/pipeline/generate-mesh.ps1 -Species "Clownfish"
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Species,
    [string]$TripoDir = 'D:\AI\TripoSR'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'creature-db.ps1')

$rec = Load-Creature $Species
if (-not $rec) { Write-Error "No creature record for '$Species'"; exit 1 }

$key = Get-CreatureKey $Species
$refImage = Join-Path $PSScriptRoot '..' 'creature-db' $key 'reference' 'side.png'
if (-not (Test-Path $refImage)) {
    Write-Error "No reference image at $refImage. Run generate-reference.ps1 first."
    exit 2
}

$meshDir = Join-Path $PSScriptRoot '..' 'creature-db' $key 'mesh'
New-Item -ItemType Directory -Force $meshDir | Out-Null
$outGlb = Join-Path $meshDir "$key.glb"

if (Test-Path $outGlb) {
    Write-Host "[$Species] mesh exists at $outGlb, skipping"
} else {
    Write-Host "[$Species] running TripoSR..."
    $tripoScript = Join-Path $TripoDir 'run.py'
    $tripoPython = Join-Path $TripoDir 'venv\Scripts\python.exe'
    if (-not (Test-Path $tripoPython)) { $tripoPython = 'python' }

    Push-Location $TripoDir
    try {
        & $tripoPython $tripoScript $refImage --output-dir $meshDir --model-save-format glb 2>&1 | ForEach-Object { Write-Host "  $_" }
        if ($LASTEXITCODE -ne 0) { Write-Error "TripoSR failed"; exit 3 }
    } finally { Pop-Location }

    $generated = Get-ChildItem $meshDir -Filter '*.glb' -Recurse | Select-Object -First 1
    if ($generated -and $generated.FullName -ne $outGlb) {
        Move-Item $generated.FullName $outGlb -Force
    }
}

if (Test-Path $outGlb) {
    $sz = (Get-Item $outGlb).Length
    Write-Host "[$Species] mesh: $outGlb ($sz bytes)"
    $rec.meshPath = $outGlb
    $rec.status = 'mesh-done'
    Save-Creature $rec | Out-Null
} else {
    Write-Error "[$Species] no mesh produced"
    exit 4
}
