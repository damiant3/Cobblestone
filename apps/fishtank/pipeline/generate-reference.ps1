# Generate reference images for a creature via Stable Diffusion Forge API.
# Produces: side profile, top-down, front view on white background.
#
# Usage: pwsh apps/fishtank/pipeline/generate-reference.ps1 -Species "Clownfish"
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Species,
    [string]$ForgeUrl = 'http://127.0.0.1:7860',
    [int]$Width = 1024,
    [int]$Height = 768
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'creature-db.ps1')

$rec = Load-Creature $Species
if (-not $rec) {
    Write-Error "No creature record for '$Species'. Run plan-creature.ps1 first."
    exit 1
}

$outDir = Join-Path $PSScriptRoot '..' 'creature-db' (Get-CreatureKey $Species) 'reference'
New-Item -ItemType Directory -Force $outDir | Out-Null

$views = @(
    @{ suffix='side';  prompt="side profile view, full body, fins fully spread" },
    @{ suffix='top';   prompt="top-down view, dorsal visible, looking straight down" },
    @{ suffix='front'; prompt="front view, facing camera, symmetrical" }
)

foreach ($view in $views) {
    $outPath = Join-Path $outDir "$($view.suffix).png"
    if (Test-Path $outPath) {
        Write-Host "[$Species] $($view.suffix) exists, skipping"
        continue
    }

    $fullPrompt = if ($view.suffix -eq 'side') { $rec.refPrompt }
        else { "$Species, $($view.prompt), white background, product photography, centered, clean, isolated specimen, no environment" }

    $body = @{
        prompt = $fullPrompt
        negative_prompt = $rec.refNegative
        steps = 30
        cfg_scale = 9
        width = $Width
        height = $Height
        sampler_name = "Euler a"
        batch_size = 1
        n_iter = 1
    } | ConvertTo-Json -Depth 5

    Write-Host "[$Species] generating $($view.suffix) view..."
    try {
        $resp = Invoke-RestMethod -Uri "$ForgeUrl/sdapi/v1/txt2img" -Method Post -Body $body -ContentType 'application/json' -TimeoutSec 300
        $imgBytes = [Convert]::FromBase64String($resp.images[0])
        [IO.File]::WriteAllBytes($outPath, $imgBytes)
        Write-Host "[$Species] $($view.suffix): $outPath ($($imgBytes.Length) bytes)"
    } catch {
        Write-Warning "[$Species] $($view.suffix) FAILED: $_"
    }
}

Update-CreatureStatus $Species 'reference-done'
Write-Host "[$Species] reference images complete"
