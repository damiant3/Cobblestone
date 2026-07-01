# generate-assets.ps1 — Generate fish tank textures via DiffusionForge.
# Usage: apps/fishtank/generate-assets.ps1 [-Force]
# Requires Forge running at http://127.0.0.1:7860 with --api flag
[CmdletBinding()]
param([switch]$Force)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$OutDir = Join-Path $PSScriptRoot 'assets'
New-Item -ItemType Directory -Force $OutDir | Out-Null

$ForgeUrl = 'http://127.0.0.1:7860/sdapi/v1/txt2img'

$FishStyle = 'underwater photograph, side view, studio lighting, ultra detailed, 4k, '
$CoralStyle = 'underwater macro photography, detailed, 4k, '
$FishNeg = 'blurry, low quality, multiple fish, text, watermark, deformed, cartoon, drawing, surface, above water'
$CoralNeg = 'blurry, low quality, text, watermark, fish, cartoon, drawing'

$Assets = @(
    # Fish — side-view, on solid green/blue for easy background removal
    @{ Id='fish-clownfish'; W=512; H=320; Steps=20; CFG=5
       Prompt=$FishStyle + 'clownfish swimming, orange white black stripes, amphiprion ocellaris, isolated on solid dark blue background, clear profile view'
       Neg=$FishNeg }
    @{ Id='fish-angelfish'; W=512; H=512; Steps=20; CFG=5
       Prompt=$FishStyle + 'freshwater angelfish swimming, silver body with bold black vertical stripes, long flowing fins, pterophyllum scalare, isolated on solid dark blue background'
       Neg=$FishNeg }
    @{ Id='fish-neontetra'; W=384; H=256; Steps=20; CFG=5
       Prompt=$FishStyle + 'neon tetra fish, tiny iridescent tropical fish with bright blue stripe and red tail, paracheirodon innesi, isolated on solid dark blue background, macro'
       Neg=$FishNeg }
    @{ Id='fish-tang'; W=512; H=384; Steps=20; CFG=5
       Prompt=$FishStyle + 'blue tang fish swimming, vivid royal blue body with bright yellow tail fin, paracanthurus hepatus, isolated on solid dark blue background'
       Neg=$FishNeg + ', cartoon character, finding dory' }
    @{ Id='fish-discus'; W=512; H=512; Steps=20; CFG=5
       Prompt=$FishStyle + 'discus fish, perfectly round flat body, vibrant red turquoise pattern, symphysodon, isolated on solid dark blue background'
       Neg=$FishNeg }
    @{ Id='fish-guppy'; W=384; H=256; Steps=20; CFG=5
       Prompt=$FishStyle + 'male guppy fish, tiny colorful fantail, iridescent rainbow colors purple green orange, poecilia reticulata, isolated on solid dark blue background, macro'
       Neg=$FishNeg }

    # Corals and plants — on black background (additive blend in renderer)
    @{ Id='coral-brain'; W=512; H=512; Steps=6; CFG=2
       Prompt=$CoralStyle + 'brain coral underwater, spherical with maze-like ridges, warm orange and green bioluminescence, isolated on solid black background'
       Neg=$CoralNeg }
    @{ Id='coral-seafan'; W=512; H=640; Steps=6; CFG=2
       Prompt=$CoralStyle + 'purple sea fan coral, delicate branching gorgonian fan shape, pink and purple, isolated on solid black background'
       Neg=$CoralNeg }
    @{ Id='coral-anemone'; W=512; H=512; Steps=6; CFG=2
       Prompt=$CoralStyle + 'magnificent sea anemone, flowing tentacles with bright green fluorescent tips, heteractis magnifica, isolated on solid black background'
       Neg=$CoralNeg + ', clownfish' }
    @{ Id='plant-kelp'; W=256; H=768; Steps=6; CFG=2
       Prompt=$CoralStyle + 'tall kelp seaweed fronds, translucent green waving leaves, underwater macro, isolated on solid black background'
       Neg=$CoralNeg }
    @{ Id='plant-moss'; W=512; H=320; Steps=6; CFG=2
       Prompt=$CoralStyle + 'java moss aquarium plant, dense lush green carpet of tiny leaves, vesicularia dubyana, isolated on solid black background'
       Neg=$CoralNeg }

    # Rocks
    @{ Id='rock-01'; W=512; H=384; Steps=6; CFG=2
       Prompt='smooth natural aquarium stone, grey and warm brown, rounded river rock, isolated on solid black background, studio lighting, detailed texture, 4k'
       Neg='blurry, text, plants, fish, cartoon' }
    @{ Id='rock-02'; W=512; H=384; Steps=6; CFG=2
       Prompt='rough dark lava rock for aquarium, porous volcanic basalt stone with holes, isolated on solid black background, studio lighting, 4k'
       Neg='blurry, text, smooth, polished, cartoon' }

    # Sand floor (tileable)
    @{ Id='floor-sand'; W=512; H=512; Steps=6; CFG=2
       Prompt='seamless tileable aquarium sand texture, fine golden sand grains underwater, even soft lighting, top-down overhead view, 4k texture map'
       Neg='fish, plants, shadows, objects, text, rocks, side view' }

    # Background
    @{ Id='bg-ocean'; W=1920; H=1080; Steps=20; CFG=5
       Prompt='deep ocean underwater background, smooth blue gradient from light aqua cyan at top to deep dark navy at bottom, distant blurred coral reef silhouette, volumetric light rays from above, atmospheric underwater haze, no fish, cinematic, 4k'
       Neg='fish, text, watermark, surface, sky, cartoon, bright, overexposed' }
)

Write-Host "Codex Fish Tank Asset Generator" -ForegroundColor Cyan
Write-Host "Output: $OutDir" -ForegroundColor Gray
Write-Host "Checking Forge at $ForgeUrl..." -ForegroundColor Gray

try {
    Invoke-WebRequest -Uri 'http://127.0.0.1:7860/sdapi/v1/options' -UseBasicParsing -TimeoutSec 5 | Out-Null
    Write-Host "  Forge is online." -ForegroundColor Green
} catch {
    Write-Host "  Forge is not running. Start DiffusionForge and try again." -ForegroundColor Red
    exit 1
}

$generated = 0; $skipped = 0; $failed = 0

foreach ($asset in $Assets) {
    $outFile = Join-Path $OutDir "$($asset.Id).png"
    if ((Test-Path $outFile) -and -not $Force) {
        Write-Host "  SKIP $($asset.Id) (exists)" -ForegroundColor Gray
        $skipped++
        continue
    }

    Write-Host "  Generating $($asset.Id) ($($asset.W)x$($asset.H), $($asset.Steps) steps)..." -ForegroundColor Yellow -NoNewline

    $body = @{
        prompt = $asset.Prompt
        negative_prompt = $asset.Neg
        width = $asset.W
        height = $asset.H
        steps = $asset.Steps
        cfg_scale = $asset.CFG
        sampler_name = 'DPM++ SDE'
        scheduler = 'karras'
        seed = -1
        batch_size = 1
        n_iter = 1
        save_images = $false
        send_images = $true
    } | ConvertTo-Json

    try {
        $resp = Invoke-WebRequest -Uri $ForgeUrl -Method Post -ContentType 'application/json' -Body $body -TimeoutSec 300
        $result = $resp.Content | ConvertFrom-Json
        $imgBytes = [Convert]::FromBase64String($result.images[0])
        [System.IO.File]::WriteAllBytes($outFile, $imgBytes)
        $size = [math]::Round((Get-Item $outFile).Length / 1024, 1)
        Write-Host " OK (${size}KB)" -ForegroundColor Green
        $generated++
    } catch {
        Write-Host " FAIL: $_" -ForegroundColor Red
        $failed++
    }
}

Write-Host ""
Write-Host "Done: $generated generated, $skipped skipped, $failed failed." -ForegroundColor Cyan
Write-Host "Assets in: $OutDir" -ForegroundColor Gray
