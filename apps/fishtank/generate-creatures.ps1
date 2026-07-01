# generate-creatures.ps1 — Generate additional sea creatures and decorations.
[CmdletBinding()]
param([switch]$Force)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$OutDir = Join-Path $PSScriptRoot 'assets'
New-Item -ItemType Directory -Force $OutDir | Out-Null
$ForgeUrl = 'http://127.0.0.1:7860/sdapi/v1/txt2img'

$Assets = @(
    @{ Id='creature-crab'; W=512; H=384; Steps=20; CFG=5
       Prompt='hermit crab on sea floor, tiny orange crab with shell, underwater macro photography, side view, isolated on solid dark blue background, detailed, 4k'
       Neg='blurry, low quality, text, watermark, cartoon, above water' }
    @{ Id='creature-starfish'; W=512; H=512; Steps=20; CFG=5
       Prompt='orange starfish on ocean floor, five-armed sea star, overhead view, underwater photography, isolated on solid dark blue background, detailed, 4k'
       Neg='blurry, low quality, text, watermark, cartoon' }
    @{ Id='creature-urchin'; W=512; H=512; Steps=20; CFG=5
       Prompt='purple sea urchin underwater, spiny round echinoid, macro photography, isolated on solid dark blue background, detailed spines, 4k'
       Neg='blurry, low quality, text, watermark, cartoon' }
    @{ Id='creature-seahorse'; W=384; H=512; Steps=20; CFG=5
       Prompt='yellow seahorse underwater, hippocampus, side profile view, delicate curved body, isolated on solid dark blue background, macro, 4k'
       Neg='blurry, low quality, text, watermark, cartoon, multiple' }
    @{ Id='creature-shrimp'; W=512; H=320; Steps=20; CFG=5
       Prompt='cleaner shrimp underwater, red and white striped peppermint shrimp, side view, isolated on solid dark blue background, macro photography, 4k'
       Neg='blurry, low quality, text, watermark, cartoon' }
    @{ Id='creature-snail'; W=384; H=384; Steps=20; CFG=5
       Prompt='turbo snail on aquarium glass, spiral shell, underwater macro, isolated on solid dark blue background, detailed, 4k'
       Neg='blurry, low quality, text, watermark, land snail' }
    @{ Id='coral-tube'; W=512; H=640; Steps=6; CFG=2
       Prompt='tube coral underwater, orange sun coral with polyps extended, cylindrical colonies, isolated on solid black background, macro photography, 4k'
       Neg='blurry, text, watermark, fish' }
    @{ Id='coral-mushroom'; W=512; H=384; Steps=6; CFG=2
       Prompt='mushroom coral underwater, flat disc-shaped coral with radiating ridges, green fluorescent, isolated on solid black background, macro, 4k'
       Neg='blurry, text, watermark, fish' }
    @{ Id='plant-seagrass'; W=384; H=640; Steps=6; CFG=2
       Prompt='seagrass blades underwater, tall green ribbon-like leaves waving, aquatic eelgrass, isolated on solid black background, underwater photography, 4k'
       Neg='blurry, text, watermark, fish, land grass' }
    @{ Id='decor-shell'; W=512; H=384; Steps=6; CFG=2
       Prompt='conch seashell on ocean floor, large spiral shell with pink interior, isolated on solid black background, studio lighting, 4k'
       Neg='blurry, text, watermark, cartoon' }
    @{ Id='decor-treasure'; W=512; H=384; Steps=6; CFG=2
       Prompt='small treasure chest underwater on sandy ocean floor, open with gold coins spilling out, covered in barnacles and algae, isolated on solid black background, 4k'
       Neg='blurry, text, watermark, cartoon, pirate, person' }
    @{ Id='decor-anchor'; W=384; H=512; Steps=6; CFG=2
       Prompt='old rusty anchor on ocean floor underwater, covered in barnacles and coral growth, isolated on solid black background, atmospheric, 4k'
       Neg='blurry, text, watermark, cartoon, ship' }
)

Write-Host "Generating additional sea creatures and decorations..." -ForegroundColor Cyan

try { Invoke-WebRequest -Uri 'http://127.0.0.1:7860/sdapi/v1/options' -UseBasicParsing -TimeoutSec 5 | Out-Null }
catch { Write-Host "Forge not running!" -ForegroundColor Red; exit 1 }

$gen = 0; $skip = 0; $fail = 0
foreach ($a in $Assets) {
    $out = Join-Path $OutDir "$($a.Id).png"
    if ((Test-Path $out) -and -not $Force) { Write-Host "  SKIP $($a.Id)" -ForegroundColor Gray; $skip++; continue }
    Write-Host "  $($a.Id) ($($a.W)x$($a.H))..." -ForegroundColor Yellow -NoNewline
    $body = @{ prompt=$a.Prompt; negative_prompt=$a.Neg; width=$a.W; height=$a.H; steps=$a.Steps; cfg_scale=$a.CFG; sampler_name='DPM++ SDE'; scheduler='karras'; seed=-1; batch_size=1; n_iter=1; save_images=$false; send_images=$true } | ConvertTo-Json
    try {
        $resp = Invoke-WebRequest -Uri $ForgeUrl -Method Post -ContentType 'application/json' -Body $body -TimeoutSec 300
        $result = $resp.Content | ConvertFrom-Json
        [System.IO.File]::WriteAllBytes($out, [Convert]::FromBase64String($result.images[0]))
        $sz = [math]::Round((Get-Item $out).Length/1024, 1)
        Write-Host " OK (${sz}KB)" -ForegroundColor Green
        $gen++
    } catch { Write-Host " FAIL: $_" -ForegroundColor Red; $fail++ }
}
Write-Host "`nDone: $gen generated, $skip skipped, $fail failed" -ForegroundColor Cyan
