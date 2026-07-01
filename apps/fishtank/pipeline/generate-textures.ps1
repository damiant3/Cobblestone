# Generate textures for each sub-unit of a creature via Forge API.
# Produces diffuse, normal, specular, alpha maps per sub-unit.
#
# Usage: pwsh apps/fishtank/pipeline/generate-textures.ps1 -Species "Clownfish"
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Species,
    [string]$ForgeUrl = 'http://127.0.0.1:7860'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'creature-db.ps1')

$rec = Load-Creature $Species
if (-not $rec) { Write-Error "No creature record for '$Species'"; exit 1 }

$key = Get-CreatureKey $Species
$texDir = Join-Path $PSScriptRoot '..' 'creature-db' $key 'textures'
New-Item -ItemType Directory -Force $texDir | Out-Null

$total = 0; $done = 0
foreach ($su in $rec.subUnits) {
    foreach ($tex in $su.textures) {
        $total++
        $fileName = "$($su.kind)-$($tex.slot).png"
        $outPath = Join-Path $texDir $fileName

        if (Test-Path $outPath) {
            Write-Host "[$Species] $fileName exists, skipping"
            $tex.path = $outPath
            $tex.status = 'done'
            $done++
            continue
        }

        $body = @{
            prompt = $tex.prompt
            negative_prompt = "blurry, watermark, text, cartoon, low quality"
            steps = 25
            cfg_scale = 7
            width = $tex.width
            height = $tex.height
            sampler_name = "DPM++ 2M"
            scheduler = "Karras"
            batch_size = 1
            n_iter = 1
            override_settings = @{
                sd_model_checkpoint = $tex.model
            }
        } | ConvertTo-Json -Depth 5

        Write-Host "[$Species] generating $fileName ($($tex.width)x$($tex.height))..."
        try {
            $resp = Invoke-RestMethod -Uri "$ForgeUrl/sdapi/v1/txt2img" -Method Post -Body $body -ContentType 'application/json' -TimeoutSec 300
            $imgBytes = [Convert]::FromBase64String($resp.images[0])
            [IO.File]::WriteAllBytes($outPath, $imgBytes)
            Write-Host "[$Species] ${fileName}: $($imgBytes.Length) bytes"
            $tex.path = $outPath
            $tex.status = 'done'
            $done++
        } catch {
            Write-Warning "[$Species] $fileName FAILED: $_"
            $tex.status = 'failed'
        }
    }
}

$rec.status = if ($done -eq $total) { 'textures-done' } else { 'textures-partial' }
Save-Creature $rec | Out-Null
Write-Host "[$Species] textures: $done/$total complete"
