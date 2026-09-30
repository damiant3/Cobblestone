# The ModBuilder page's illustrations, from the Codex diffusion pipeline through
# the codex_image server (apps/diffusion/serve.ps1), one resident server for the
# run. Forge's batch of two becomes two calls, seeds seed and seed + 1, as Forge
# seeds a batch.
#   pwsh apps/modbuilder/page/generate-art.ps1                 # every image, two candidates each
#   pwsh apps/modbuilder/page/generate-art.ps1 -Only hero -Pick 1
# Candidates land in build-output/modbuilder-art; -Pick N copies candidate N of
# each named image into page/img as the JPEG the page embeds.
[CmdletBinding()]
param([string[]]$Only = @(), [int]$Pick = 0)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$cand = Join-Path $repo 'build-output/modbuilder-art'
$img = Join-Path $PSScriptRoot 'img'
[void](New-Item -ItemType Directory -Force -Path $cand, $img)

# dreamshaperXL lightning wants few steps and a low CFG; these are its published settings.
$model = 'dreamshaperXL_lightningDPMSDE.safetensors'
$style = 'painterly fantasy concept art, norse viking theme, warm firelight against cold blue night, cinematic lighting, rich detail, game key art, '
$negative = 'text, letters, words, watermark, signature, logo, ui, blurry, deformed, ugly, lowres, jpeg artifacts, photo, photograph, modern'

$art = @(
    @{ id = 'hero';    w = 1536; h = 640;  out = 1536; seed = 7101; prompt = 'a viking smithy at night where glowing runes are hammered into an amulet on an anvil, sparks flying, open book of runes on the bench, snowy mountains through an arched window' }
    @{ id = 'storage'; w = 1024; h = 1024; out = 640;  seed = 7201; prompt = 'four iron-bound wooden chests around a carpenter workbench, joined by glowing golden rune threads, inside a torch-lit longhouse' }
    @{ id = 'meadows'; w = 1024; h = 1024; out = 640;  seed = 7301; prompt = 'a sunlit green meadow with wildflowers and a clear stream, small harmless lizard creatures basking on rocks, birch forest, soft morning light' }
    @{ id = 'arc';     w = 1024; h = 1024; out = 640;  seed = 7401; prompt = 'a vivid rainbow-coloured circumhorizontal arc across wispy cirrus clouds high above a viking longhouse on a green hill, bright midday sky' }
    @{ id = 'hover';   w = 1024; h = 1024; out = 640;  seed = 7511; prompt = 'a wild boar animal on four legs resting on straw beside a stone cooking fire, a small glowing golden hourglass floating in the air above the fire, cozy barn interior, no people' }
    @{ id = 'hud';     w = 1024; h = 1024; out = 640;  seed = 7601; prompt = 'three steaming bowls of stew, bread and roasted meat on a carved wooden shelf beside a glowing red health potion vial, warm hearth light' }
    @{ id = 'rows';    w = 1024; h = 1024; out = 640;  seed = 7701; prompt = 'perfectly straight rows of carrot and turnip seedlings in dark tilled soil beside a viking farmhouse, a wooden hoe, golden hour' }
    @{ id = 'forge';   w = 1536; h = 640;  out = 1536; seed = 7801; prompt = 'a blazing forge fire and anvil in a dark stone smithy, a glowing rune-carved amulet cooling in a quench trough, embers drifting' }
)
$Only = @($Only | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
if ($Only) { $art = @($art | Where-Object { $Only -contains $_.id }) }

function Save-Jpeg([byte[]]$Png, [string]$Path, [int]$Width) {
    $ms = [IO.MemoryStream]::new($Png); $src = [Drawing.Image]::FromStream($ms)
    try {
        $h = [int][math]::Round($src.Height * $Width / $src.Width)
        $bmp = [Drawing.Bitmap]::new($Width, $h)
        $g = [Drawing.Graphics]::FromImage($bmp); $g.InterpolationMode = 'HighQualityBicubic'; $g.DrawImage($src, 0, 0, $Width, $h); $g.Dispose()
        $enc = [Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object MimeType -eq 'image/jpeg'
        $ps = [Drawing.Imaging.EncoderParameters]::new(1); $ps.Param[0] = [Drawing.Imaging.EncoderParameter]::new([Drawing.Imaging.Encoder]::Quality, [long]82)
        $bmp.Save($Path, $enc, $ps); $bmp.Dispose()
    } finally { $src.Dispose(); $ms.Dispose() }
}

if ($Pick -gt 0) {
    foreach ($a in $art) {
        $png = Join-Path $cand ("{0}-{1}.png" -f $a.id, $Pick)
        if (-not (Test-Path -LiteralPath $png)) { throw "No candidate $png" }
        Save-Jpeg ([IO.File]::ReadAllBytes($png)) (Join-Path $img ($a.id + '.jpg')) $a.out
        Write-Host "picked $($a.id) candidate $Pick"
    }
    exit 0
}

$psi = [Diagnostics.ProcessStartInfo]::new((Get-Process -Id $PID).Path)
foreach ($x in @('-NoProfile', '-File', (Join-Path $repo 'apps/diffusion/serve.ps1'), '-Output', $cand)) { $psi.ArgumentList.Add($x) }
$psi.RedirectStandardInput = $true; $psi.RedirectStandardOutput = $true; $psi.UseShellExecute = $false
$server = [Diagnostics.Process]::Start($psi)
function Call($Id, $Method, $Params) {
    $server.StandardInput.WriteLine((@{ jsonrpc = '2.0'; id = $Id; method = $Method; params = $Params } | ConvertTo-Json -Depth 10 -Compress)); $server.StandardInput.Flush()
    $server.StandardOutput.ReadLine() | ConvertFrom-Json
}
try {
    $null = Call 1 'initialize' @{ protocolVersion = '2025-06-18'; capabilities = @{}; clientInfo = @{ name = 'generate-art'; version = '1' } }
    $id = 2
    foreach ($a in $art) {
        for ($i = 0; $i -lt 2; $i++) {
            $r = Call ($id++) 'tools/call' @{ name = 'codex_image'; arguments = @{ prompt = $style + $a.prompt; negative = $negative; model = $model; width = $a.w; height = $a.h; steps = 6; cfg = 2.0; sampler = 'DPM++ SDE'; seed = $a.seed + $i } }
            if ($r.result.isError) { throw "$($a.id) candidate $($i + 1): $($r.result.content[0].text)" }
            Move-Item -Force -LiteralPath $r.result.structuredContent.path -Destination (Join-Path $cand ("{0}-{1}.png" -f $a.id, ($i + 1)))
        }
        Write-Host "generated $($a.id) (2 candidates)"
    }
} finally { $server.StandardInput.Close(); [void]$server.WaitForExit(60000) }
