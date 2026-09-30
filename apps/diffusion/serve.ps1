# serve.ps1 -- the codex_image MCP server: SDXL, SD1.5 and Flux schnell text to image on the Codex
# diffusion pipeline, over stdio, one JSON-RPC message per line.
#
#   pwsh apps/diffusion/serve.ps1 [-Models <dir>] [-Output <dir>] [-Inputs <dir>] [-Driver <cdx>] [-Disk <img>]
#        [-ClipTokenizer <dir>] [-T5Tokenizer <tokenizer.json>] [-IdleMinutes <m>]
#
# The authority is fixed at launch and is all the tool has (root, 2026-09-29:
# not in ACCP, whose pure-computation promise stays whole):
#   -Models  read-only: checkpoints under Stable-diffusion\, LoRAs under Lora\,
#            and Flux's T5 and CLIP-L under text_encoder\ and ae under VAE\
#   -Output  write-only: the one directory PNGs land in
#   -Inputs  read-only: pictures and masks for img2img and inpaint
# A caller names a model, LoRA, picture or mask by file name, checked against a
# listing of those folders, and never supplies a path. A picture or mask is
# decoded here to width x height RGBA bytes; the guest has no PNG decoder.
#
# One guest serves every call (see Start-Guest): its staging root holds a
# junction `models` to -Models and, per call n, request-<n>.txt (key=value
# lines: prompt, negative, model=models/Stable-diffusion/<file>,
# lora=models/Lora/<file>:<w> repeated, sampler, steps, cfg, seed, width,
# height, the img2img, inpaint and hires keys, path=<png name>) and
# image-<n>.rgba / mask-<n>.rgba; the guest reads them through codex-vm's
# -gpu-files, writes the PNG and done-<n>.txt through -gpu-out, and keeps the
# checkpoint loaded until a call names another, or until -IdleMinutes pass with
# no call, when the guest exits and frees the device; the next call loads again
# (root, 2026-09-29: the fleet's GPU runs need 7-9 GB of the 16). The driver reads CLIP's BPE
# tables from -Disk, build/mint-clip-bpe-disk.ps1's image, minted at start
# when absent (or when it lacks T5 and a T5 tokenizer is now found) from
# -ClipTokenizer (vocab.json and merges.txt) and -T5Tokenizer. Each defaults
# to the models folder, then to Forge's copy beside it:
#   <Models>\tokenizers\clip\vocab.json, merges.txt
#       https://huggingface.co/openai/clip-vit-large-patch14 (the same tables as
#       Forge's SDXL tokenizer; graded by codex/test/apps/clip-bpe-forge)
#   <Models>\tokenizers\t5\tokenizer.json
#       https://huggingface.co/google-t5/t5-large (FLUX.1's tokenizer_2 is gated;
#       this one has its vocab and charsmap; graded by t5-tokenizer-forge)
# Without a T5 tokenizer the server starts and the driver refuses Flux.
[CmdletBinding()]
param(
    [string]$Models = 'D:\AI\DiffusionForge\webui\models',
    [string]$Output = '',
    [string]$Inputs = '',
    [string]$Driver = '',
    [string]$Disk = '',
    [string]$ClipTokenizer = '',
    [string]$T5Tokenizer = '',
    [int]$TimeoutSec = 900,
    [double]$IdleMinutes = 10,
    [string]$Staging = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $Output) { $Output = Join-Path $repo 'build-output\diffusion-images' }
if (-not $Inputs) { $Inputs = Join-Path $repo 'build-output\diffusion-inputs' }
if (-not $Driver) { $Driver = Join-Path $repo 'build-output\diffusion\DiffusionDriver.cdx' }
if (-not $Disk) { $Disk = Join-Path $repo 'build-output\diffusion\clip-bpe-t5.img' }
$hf = Join-Path (Split-Path $Models) 'backend\huggingface'
if (-not $ClipTokenizer) { $ClipTokenizer = @((Join-Path $Models 'tokenizers\clip'), (Join-Path $hf 'stabilityai\stable-diffusion-xl-base-1.0\tokenizer')) | Where-Object { Test-Path -PathType Leaf (Join-Path $_ 'merges.txt') } | Select-Object -First 1 }
if (-not $T5Tokenizer) { $T5Tokenizer = @((Join-Path $Models 'tokenizers\t5\tokenizer.json'), (Join-Path $hf 'black-forest-labs\FLUX.1-schnell\tokenizer_2\tokenizer.json')) | Where-Object { Test-Path -PathType Leaf $_ } | Select-Object -First 1 }
$vm = Join-Path $repo 'tools\codex-vm.exe'
$staging = if ($Staging) { $Staging } else { Join-Path $repo 'build-output\diffusion-staging' }
if (-not (Test-Path -PathType Container (Join-Path $Models 'Stable-diffusion'))) { [Console]::Error.WriteLine("codex_image: no Stable-diffusion folder under $Models; pass -Models <the folder holding Stable-diffusion\, Lora\, text_encoder\, VAE\>"); exit 1 }
New-Item -ItemType Directory -Force $Output, $Inputs, $staging | Out-Null
$Models = (Resolve-Path $Models).Path
$Output = (Resolve-Path $Output).Path
$Inputs = (Resolve-Path $Inputs).Path
Add-Type -AssemblyName System.Drawing
$haveT5 = [bool]$T5Tokenizer -and (Test-Path -PathType Leaf $T5Tokenizer)
$stale = (Test-Path -PathType Leaf $Disk) -and $haveT5 -and ([IO.File]::ReadAllBytes($Disk)[8] -eq 0)
if (-not (Test-Path -PathType Leaf $Disk) -or $stale) {
    if (-not $ClipTokenizer -or -not (Test-Path -PathType Leaf (Join-Path $ClipTokenizer 'vocab.json'))) { [Console]::Error.WriteLine("codex_image: no CLIP tokenizer; put vocab.json and merges.txt from https://huggingface.co/openai/clip-vit-large-patch14 in $(Join-Path $Models 'tokenizers\clip') or pass -ClipTokenizer"); exit 1 }
    New-Item -ItemType Directory -Force (Split-Path $Disk) | Out-Null
    $extra = if ($haveT5) { @('-Extra', $T5Tokenizer) } else { @() }
    $mint = & pwsh -NoProfile -File (Join-Path $repo 'build\mint-clip-bpe-disk.ps1') -Out $Disk -Source $ClipTokenizer @extra 2>&1
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -PathType Leaf $Disk)) { [Console]::Error.WriteLine("codex_image: no CLIP BPE disk at ${Disk}: $mint"); exit 1 }
}
$Disk = (Resolve-Path $Disk).Path

# Forge's names for the model upscalers (modules/realesrgan_model.py; the SwinIR extension and modules/dat_model.py name a local file by its stem), each offered when its file is under -Models.
$hrUpscalers = [ordered]@{ 'Latent' = '' }
foreach ($u in @(@('R-ESRGAN 4x+', 'RealESRGAN/RealESRGAN_x4plus.pth'), @('R-ESRGAN 4x+ Anime6B', 'RealESRGAN/RealESRGAN_x4plus_anime_6B.pth'), @('SwinIR_4x', 'SwinIR/SwinIR_4x.pth'), @('DAT_x4', 'DAT/DAT_x4.pth'))) { if (Test-Path -PathType Leaf (Join-Path $Models $u[1])) { $hrUpscalers[$u[0]] = $u[1] } }
$samplers = @('Euler', 'DPM++ SDE', 'DPM++ 2M', 'DPM++ 2M SDE', 'DPM++ 2M SDE Heun', 'DPM++ 2S a', 'DPM++ 3M SDE', 'Euler a', 'LMS', 'Heun', 'DPM2', 'DPM2 a', 'DPM fast', 'DPM adaptive', 'Restart', 'HeunPP2', 'IPNDM', 'IPNDM_V', 'DEIS', 'DDIM', 'DDIM CFG++', 'PLMS', 'UniPC', 'LCM', 'DDPM')
$schedulers = @('Automatic', 'Uniform', 'Karras', 'Exponential', 'Polyexponential', 'SGM Uniform', 'KL Optimal', 'Align Your Steps', 'Simple', 'Normal', 'DDIM', 'Beta', 'Turbo', 'Align Your Steps GITS', 'Align Your Steps 11', 'Align Your Steps 32')
$dtModes = @('Constant', 'Linear Down', 'Cosine Down', 'Half Cosine Down', 'Linear Up', 'Cosine Up', 'Half Cosine Up', 'Power Up', 'Power Down', 'Linear Repeating', 'Cosine Repeating', 'Sawtooth')
$protocols = @('2025-06-18', '2025-03-26', '2024-11-05')

# A Flux checkpoint holds the first double block's attention; read from the safetensors header.
function Is-Flux([string]$Name) { Header-Has (Join-Path $Models "Stable-diffusion\$Name") '"double_blocks.0.img_attn.qkv.weight"' }

# Whether a safetensors header names a key. A module file is a VAE when it holds decoder.conv_in.weight (Forge's replace_state_dict, backend/loader.py:205).
function Header-Has([string]$Path, [string]$Key) {
    $fs = [IO.File]::OpenRead($Path)
    try {
        $len = [byte[]]::new(8); if ($fs.Read($len, 0, 8) -ne 8) { return $false }
        $n = [BitConverter]::ToInt64($len, 0); if ($n -le 0 -or $n -gt 100MB) { return $false }
        $head = [byte[]]::new($n); if ($fs.Read($head, 0, $n) -ne $n) { return $false }
        [Text.Encoding]::UTF8.GetString($head).Contains($Key)
    } finally { $fs.Dispose() }
}

function Names([string]$Sub) {
    $d = Join-Path $Models $Sub
    if (-not (Test-Path -PathType Container $d)) { return @() }
    @(Get-ChildItem -LiteralPath $d -File -Filter *.safetensors | ForEach-Object Name)
}

$schema = [ordered]@{
    type = 'object'
    properties = [ordered]@{
        prompt   = @{ type = 'string'; description = 'Positive prompt; Forge syntax for (word:1.2) emphasis and BREAK.' }
        negative = @{ type = 'string'; description = 'Negative prompt; empty is zero conditioning, as in Forge.' }
        model    = @{ type = 'string'; description = 'Checkpoint file name in the models folder''s Stable-diffusion directory.' }
        loras    = @{ type = 'array'; items = @{ type = 'object'; properties = @{ name = @{ type = 'string' }; weight = @{ type = 'number' } }; required = @('name', 'weight') } }
        sampler  = @{ type = 'string'; enum = $samplers }
        scheduler = @{ type = 'string'; enum = $schedulers; description = 'Forge''s schedule type; Automatic is the sampler''s own. LCM takes Automatic only; DDIM, DDIM CFG++, PLMS and UniPC take none and are text to image only.' }
        steps    = @{ type = 'integer'; minimum = 1; maximum = 150 }
        cfg      = @{ type = 'number'; minimum = 0; maximum = 30 }
        seed     = @{ type = 'integer'; minimum = 0; maximum = 4294967295 }
        clip_skip = @{ type = 'integer'; minimum = 1; maximum = 12; description = 'Clip skip (Forge''s CLIP_stop_at_last_layers), default 1; SDXL uses at least 2 whatever is asked, as Forge does. SDXL and SD1.5 only.' }
        subseed  = @{ type = 'integer'; minimum = 0; maximum = 4294967295; description = 'Variation seed; random when a strength is given without it.' }
        subseed_strength = @{ type = 'number'; minimum = 0; maximum = 1; description = 'Variation strength, default 0: the start noise slerped toward the variation seed''s (SDXL and SD1.5).' }
        seed_resize_from_w = @{ type = 'integer'; minimum = 0; maximum = 2048; description = 'Resize seed from width: the noise drawn at this size and pasted centred; needs seed_resize_from_h.' }
        seed_resize_from_h = @{ type = 'integer'; minimum = 0; maximum = 2048 }
        width    = @{ type = 'integer'; minimum = 256; maximum = 2048; multipleOf = 8; description = 'Width; with an image, defaults to the image''s and must equal it. Width x height at most 1024 x 1024; Flux takes multiples of 64.' }
        height   = @{ type = 'integer'; minimum = 256; maximum = 2048; multipleOf = 8 }
        image    = @{ type = 'string'; description = 'img2img: a PNG or JPEG file name in the inputs folder.' }
        mask     = @{ type = 'string'; description = 'Inpaint: a mask file name in the inputs folder, the image''s size; white (or opaque, when the mask has transparency) is repainted.' }
        denoise  = @{ type = 'number'; minimum = 0; maximum = 1; description = 'img2img denoising strength, default 0.75.' }
        resize_mode = @{ type = 'integer'; enum = @(0, 1, 2, 3); description = 'img2img when the image is not the request size: 0 just resize (default), 1 crop and resize, 2 resize and fill (Forge''s images.resize_image), 3 latent upscale (the image encoded at its own size, multiples of 8, and the latent resized; not with a mask).' }
        mask_blur = @{ type = 'integer'; minimum = 0; maximum = 64; description = 'Inpaint mask blur, default 4.' }
        inpaint_full_res = @{ type = 'boolean'; description = 'Inpaint area: false the whole picture (default), true only masked: the masked area and its padding are cropped, generated at width x height and pasted back; the PNG keeps the image''s size.' }
        inpaint_full_res_padding = @{ type = 'integer'; minimum = 0; maximum = 256; description = 'Only masked padding in pixels, default 32.' }
        inpainting_fill = @{ type = 'integer'; enum = @(0, 1, 2, 3); description = 'Inpaint masked content, as Forge numbers it: 0 fill, 1 original (default), 2 latent noise, 3 latent nothing.' }
        outpaint_pixels = @{ type = 'integer'; minimum = 8; maximum = 256; description = 'Outpaint (Forge''s poor man''s outpainting): grow the image by this many pixels on each chosen side, canvas rounded up to multiples of 64, inpainted in width x height tiles overlapping by this much; an image alone, the PNG the canvas''s size. inpainting_fill defaults to 0 (fill) here.' }
        outpaint_mode = @{ type = 'string'; enum = @('poor', 'mk2'); description = 'Outpaint script: poor (poor man''s outpainting, the default) or mk2 (outpainting mk2: each side filled with FFT-matched noise and inpainted in turn, masked content original; pictures and outpaint_pixels in multiples of 64).' }
        outpaint_noise_q = @{ type = 'number'; minimum = 0; maximum = 4; description = 'Outpainting mk2 fall-off exponent, default 1.' }
        outpaint_color_variation = @{ type = 'number'; minimum = 0; maximum = 1; description = 'Outpainting mk2 color variation, default 0.05.' }
        outpaint_mask_blur = @{ type = 'integer'; minimum = 0; maximum = 64; description = 'Outpaint mask blur, default 4 (the tiles blur by twice it).' }
        outpaint_direction = @{ type = 'array'; items = @{ type = 'string'; enum = @('left', 'right', 'up', 'down') }; description = 'Outpaint sides, default all four.' }
        soft_inpainting = @{ type = 'boolean'; description = 'Inpaint: Forge''s soft inpainting (the mask kept unrounded, magnitude-preserving blends, an adaptive overlay).' }
        soft_power = @{ type = 'number'; minimum = 0; maximum = 8; description = 'Soft inpainting schedule bias, default 1.' }
        soft_scale = @{ type = 'number'; minimum = 0; maximum = 8; description = 'Soft inpainting preservation strength, default 0.5.' }
        soft_detail = @{ type = 'number'; minimum = 1; maximum = 32; description = 'Soft inpainting transition contrast boost, default 4.' }
        soft_influence = @{ type = 'number'; minimum = 0; maximum = 1; description = 'Soft inpainting mask influence, default 0.' }
        soft_threshold = @{ type = 'number'; minimum = 0; maximum = 8; description = 'Soft inpainting difference threshold, default 0.5.' }
        soft_contrast = @{ type = 'number'; minimum = 0; maximum = 8; description = 'Soft inpainting difference contrast, default 2.' }
        hr_scale = @{ type = 'number'; minimum = 1; maximum = 4; description = 'Hires fix: upscale the latent by this and run a second pass (text to image only).' }
        hr_denoise = @{ type = 'number'; minimum = 0; maximum = 1; description = 'Hires fix denoising strength, default 0.7.' }
        hr_upscaler = @{ type = 'string'; enum = @($hrUpscalers.Keys); description = 'Hires fix upscaler: Latent (the default) or an installed ESRGAN-family, SwinIR or DAT model, as Forge names them.' }
        hr_steps = @{ type = 'integer'; minimum = 0; maximum = 150; description = 'Hires fix steps; 0 uses steps.' }
        hr_cfg   = @{ type = 'number'; minimum = 1; maximum = 30; description = 'Hires fix CFG, default cfg.' }
        hr_sampler = @{ type = 'string'; enum = $samplers; description = 'Hires fix sampler; default the first pass''s.' }
        hr_scheduler = @{ type = 'string'; enum = $schedulers; description = 'Hires fix schedule type; default the first pass''s.' }
        hr_prompt = @{ type = 'string'; description = 'Hires fix prompt; default the prompt.' }
        hr_negative = @{ type = 'string'; description = 'Hires fix negative prompt; default the negative.' }
        hr_checkpoint = @{ type = 'string'; description = 'Hires fix checkpoint: an SDXL or SD1.5 file name the hires pass runs on; default the model.' }
        hr_modules = @{ type = 'array'; items = @{ type = 'string' }; description = 'Hires fix VAE / text encoder: file names from the models folder''s VAE and text_encoder directories (Forge''s hr_additional_modules). The last VAE file encodes and decodes the hires pass; a text encoder file changes nothing on SDXL or SD1.5, as in Forge.' }
        refiner = @{ type = 'string'; description = 'Refiner: an SDXL checkpoint file name the sampling switches to at refiner_switch_at; with hires fix, in the hires pass only.' }
        refiner_switch_at = @{ type = 'number'; minimum = 0; maximum = 1; description = 'Refiner: the fraction of model calls after which the refiner takes over, default 0.8.' }
        freeu = @{ type = 'boolean'; description = 'Forge''s FreeU Integrated (SDXL and SD1.5, not Flux; with a refiner, until the switch, as Forge''s reloaded refiner is unpatched): the output blocks'' backbone and skip features rescaled.' }
        freeu_b1 = @{ type = 'number'; minimum = 0; maximum = 2; description = 'FreeU B1, default 1.01 (Forge''s default preset; its SDXL preset is 1.3, 1.4, 0.9, 0.2).' }
        freeu_b2 = @{ type = 'number'; minimum = 0; maximum = 2; description = 'FreeU B2, default 1.02.' }
        freeu_s1 = @{ type = 'number'; minimum = 0; maximum = 4; description = 'FreeU S1, default 0.99.' }
        freeu_s2 = @{ type = 'number'; minimum = 0; maximum = 4; description = 'FreeU S2, default 0.95.' }
        freeu_start = @{ type = 'number'; minimum = 0; maximum = 1; description = 'FreeU start, a fraction of the steps, default 0: FreeU runs where the sampler''s step / (steps - 1) is within start and end, as Forge gates it.' }
        freeu_end = @{ type = 'number'; minimum = 0; maximum = 1; description = 'FreeU end, a fraction of the steps, default 1.' }
        pag_scale = @{ type = 'number'; minimum = 0; maximum = 100; description = 'Forge''s PerturbedAttentionGuidance Integrated, on at this scale (Forge''s default 3); one more model call per step. Not with Flux; with a refiner, until the switch.' }
        sag = @{ type = 'boolean'; description = 'Forge''s SelfAttentionGuidance Integrated: needs cfg above 1 (Forge skips it otherwise); one more model call per step. Not with Flux; with a refiner, until the switch.' }
        sag_scale = @{ type = 'number'; minimum = -2; maximum = 5; description = 'SAG scale, default 0.5.' }
        sag_blur_sigma = @{ type = 'number'; minimum = 0; maximum = 10; description = 'SAG blur sigma, default 2; above 0.' }
        sag_threshold = @{ type = 'number'; minimum = 0; maximum = 4; description = 'SAG blur mask threshold, default 1.' }
        dynthres = @{ type = 'boolean'; description = 'Forge''s DynamicThresholding (CFG-Fix) Integrated: the guidance rescaled to the variability the mimic scale would give. Not with Flux; with a refiner, until the switch.' }
        dynthres_mimic_scale = @{ type = 'number'; minimum = 0; maximum = 100; description = 'Mimic scale, default 7.' }
        dynthres_threshold_percentile = @{ type = 'number'; minimum = 0; maximum = 1; description = 'Threshold percentile, default 1.' }
        dynthres_mimic_mode = @{ type = 'string'; enum = $dtModes; description = 'Mimic scale schedule, default Constant.' }
        dynthres_mimic_scale_min = @{ type = 'number'; minimum = 0; maximum = 100; description = 'Mimic scale minimum, default 0.' }
        dynthres_cfg_mode = @{ type = 'string'; enum = $dtModes; description = 'CFG scale schedule, default Constant.' }
        dynthres_cfg_scale_min = @{ type = 'number'; minimum = 0; maximum = 100; description = 'CFG scale minimum, default 0.' }
        dynthres_sched_val = @{ type = 'number'; minimum = 0; maximum = 100; description = 'Schedule value for the power, repeating and sawtooth schedules, default 1.' }
        dynthres_separate_feature_channels = @{ type = 'boolean'; description = 'Separate feature channels, default true.' }
        dynthres_scaling_startpoint = @{ type = 'string'; enum = @('MEAN', 'ZERO'); description = 'Scaling startpoint, default MEAN.' }
        dynthres_variability_measure = @{ type = 'string'; enum = @('AD', 'STD'); description = 'Variability measure, default AD.' }
        dynthres_interpolate_phi = @{ type = 'number'; minimum = 0; maximum = 1; description = 'Interpolate phi, default 1.' }
    }
    required = @('prompt')
}
$tool = [ordered]@{
    name = 'codex_image'
    description = 'Generate an SDXL, SD1.5 or Flux schnell image with the Codex diffusion pipeline on this machine''s GPU: text to image, hires fix, img2img and inpaint (Flux: text to image only, Euler, Simple, CFG 1, no negative, 4 steps by default). Returns the PNG path; the parameters are written into the PNG.'
    inputSchema = $schema
}

function Reply($Id, $Result) { [Console]::Out.WriteLine((@{ jsonrpc = '2.0'; id = $Id; result = $Result } | ConvertTo-Json -Depth 20 -Compress)); [Console]::Out.Flush() }
function Fail($Id, [int]$Code, [string]$Message) { [Console]::Out.WriteLine((@{ jsonrpc = '2.0'; id = $Id; error = @{ code = $Code; message = $Message } } | ConvertTo-Json -Compress)); [Console]::Out.Flush() }
function Refuse([string]$Why) { @{ content = @(@{ type = 'text'; text = "refused: $Why" }); isError = $true } }

function Arg($A, [string]$Name, $Default) { if ($null -ne $A -and $A.PSObject.Properties.Name -contains $Name -and $null -ne $A.$Name) { $A.$Name } else { $Default } }
function Line([string]$Text) { ($Text -replace '[\r\n]+', ' ') }
function IsNum($V) { $V -is [double] -or $V -is [long] -or $V -is [int] -or $V -is [decimal] }
function PyFloat($V) { $s = ([double]$V).ToString('R', [Globalization.CultureInfo]::InvariantCulture); if ($s -notmatch '[.eE]') { $s += '.0' }; $s }

# A picture in -Inputs by file name, as @{ W; H; Bytes } (RGBA, row by row), or a refusal text.
function Read-Picture($Name, [string]$What) {
    $names = @(Get-ChildItem -LiteralPath $Inputs -File | Where-Object { $_.Extension -in '.png', '.jpg', '.jpeg' } | ForEach-Object Name)
    if ($Name -isnot [string] -or $names -cnotcontains $Name) { return "refused: $What must be one of the inputs folder's pictures: $($names -join ', ')" }
    $src = [Drawing.Bitmap]::new((Join-Path $Inputs $Name))
    try {
        $rect = [Drawing.Rectangle]::new(0, 0, $src.Width, $src.Height)
        $bmp = $src.Clone($rect, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $bd = $bmp.LockBits($rect, [Drawing.Imaging.ImageLockMode]::ReadOnly, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $bytes = [byte[]]::new($src.Width * $src.Height * 4)
        for ($y = 0; $y -lt $src.Height; $y++) { [Runtime.InteropServices.Marshal]::Copy([IntPtr]::Add($bd.Scan0, $y * $bd.Stride), $bytes, $y * $src.Width * 4, $src.Width * 4) }
        $bmp.UnlockBits($bd); $bmp.Dispose()
        for ($i = 0; $i -lt $bytes.Length; $i += 4) { $b = $bytes[$i]; $bytes[$i] = $bytes[$i + 2]; $bytes[$i + 2] = $b }
        @{ W = $src.Width; H = $src.Height; Bytes = $bytes }
    } finally { $src.Dispose() }
}

# A validated request as @{ Lines; Stage } (Stage: staged file name to bytes), or a refusal text beginning "refused: ".
function Request-Lines($A) {
    $prompt = Arg $A 'prompt' $null
    if ($prompt -isnot [string] -or $prompt.Trim() -eq '') { return 'refused: prompt must be a non-empty string' }
    $negative = Arg $A 'negative' ''
    if ($negative -isnot [string]) { return 'refused: negative must be a string' }
    if ($prompt.Length + $negative.Length -gt 16384) { return 'refused: prompt and negative exceed 16384 characters' }
    $checkpoints = Names 'Stable-diffusion'
    $model = Arg $A 'model' 'dreamshaperXL_lightningDPMSDE.safetensors'
    if ($model -isnot [string] -or $checkpoints -cnotcontains $model) { return "refused: model must be one of: $($checkpoints -join ', ')" }
    $flux = Is-Flux $model
    $loraNames = Names 'Lora'
    $loras = @(Arg $A 'loras' @())
    if ($loras.Count -gt 8) { return 'refused: at most 8 LoRAs' }
    $lines = [Collections.Generic.List[string]]::new()
    foreach ($l in $loras) {
        $n = Arg $l 'name' $null; $w = Arg $l 'weight' $null
        if ($n -isnot [string] -or $loraNames -cnotcontains $n) { return "refused: LoRA must be one of: $($loraNames -join ', ')" }
        if ($w -isnot [double] -and $w -isnot [long] -and $w -isnot [int] -and $w -isnot [decimal]) { return 'refused: LoRA weight must be a number' }
        if ([double]$w -lt -4 -or [double]$w -gt 4) { return 'refused: LoRA weight must be within -4 and 4' }
        $lines.Add("lora=models/Lora/${n}:$([double]$w)")
    }
    $sampler = Arg $A 'sampler' $(if ($flux) { 'Euler' } else { 'DPM++ SDE' })
    if ($samplers -cnotcontains $sampler) { return "refused: sampler must be one of: $($samplers -join ', ')" }
    $scheduler = Arg $A 'scheduler' 'Automatic'
    if ($schedulers -cnotcontains $scheduler) { return "refused: scheduler must be one of: $($schedulers -join ', ')" }
    $stage = @{}
    $extra = [Collections.Generic.List[string]]::new()
    $imageName = Arg $A 'image' $null
    $maskName = Arg $A 'mask' $null
    $hr = Arg $A 'hr_scale' $null
    if ($null -ne $maskName -and $null -eq $imageName) { return 'refused: a mask needs an image' }
    if ($null -ne $hr -and $null -ne $imageName) { return 'refused: hires fix is for text to image, not with an image' }
    $pic = $null
    if ($null -ne $imageName) {
        $pic = Read-Picture $imageName 'image'
        if ($pic -is [string]) { return $pic }
    }
    $ints = [ordered]@{ steps = @($(if ($flux) { 4 } else { 6 }), 1, 150); seed = @((Get-Random -Minimum 0 -Maximum 2147483647), 0, 4294967295); width = @($(if ($pic) { $pic.W } else { 1024 }), 256, 2048); height = @($(if ($pic) { $pic.H } else { 1024 }), 256, 2048) }
    $vals = @{}
    foreach ($k in $ints.Keys) {
        $v = Arg $A $k $ints[$k][0]
        if ($v -isnot [long] -and $v -isnot [int]) { return "refused: $k must be an integer" }
        if ($v -lt $ints[$k][1] -or $v -gt $ints[$k][2]) { return "refused: $k must be within $($ints[$k][1]) and $($ints[$k][2])" }
        if (($k -eq 'width' -or $k -eq 'height') -and $v % $(if ($flux) { 64 } else { 8 }) -ne 0) { return "refused: $k must be a multiple of $(if ($flux) { 64 } else { 8 })" }
        $vals[$k] = [long]$v
    }
    if ($vals.width * $vals.height -gt 1048576) { return 'refused: width x height must be at most 1024 x 1024' }
    $csk = Arg $A 'clip_skip' 1
    if (($csk -isnot [long] -and $csk -isnot [int]) -or $csk -lt 1 -or $csk -gt 12) { return 'refused: clip_skip must be an integer within 1 and 12' }
    if ($csk -gt 1) { if ($flux) { return 'refused: clip skip changes nothing on Flux, whose CLIP-L gives only its pooled output' }; $extra.Add("clip_skip=$csk") }
    if ($pic) {
        $rm = Arg $A 'resize_mode' 0
        if (($rm -isnot [long] -and $rm -isnot [int]) -or $rm -lt 0 -or $rm -gt 3) { return 'refused: resize_mode must be 0 (just resize), 1 (crop and resize), 2 (resize and fill) or 3 (latent upscale)' }
        $only = Arg $A 'inpaint_full_res' $false
        if ($only -isnot [bool]) { return 'refused: inpaint_full_res must be true or false' }
        if ($only -and $null -eq $maskName) { return 'refused: inpaint_full_res needs a mask' }
        $soft = Arg $A 'soft_inpainting' $false
        if ($soft -isnot [bool]) { return 'refused: soft_inpainting must be true or false' }
        if ($soft -and $null -eq $maskName) { return 'refused: soft_inpainting needs a mask' }
        $softRanges = @{ soft_power = @(1, 0, 8); soft_scale = @(0.5, 0, 8); soft_detail = @(4, 1, 32); soft_influence = @(0, 0, 1); soft_threshold = @(0.5, 0, 8); soft_contrast = @(2, 0, 8) }
        if ($pic.W -ne $vals.width -or $pic.H -ne $vals.height) {
            if ($rm -eq 3 -and $null -ne $maskName -and -not $only) { return 'refused: latent upscale with a mask and a new size; Forge pastes its overlay at the picture''s size, into the corner' }
            if ($rm -eq 3 -and ($pic.W % 8 -ne 0 -or $pic.H % 8 -ne 0 -or $pic.W * $pic.H -gt 1048576)) { return "refused: latent upscale encodes the image at its own size, $($pic.W)x$($pic.H); it must be multiples of 8 and at most 1024x1024 pixels" }
            $extra.Add("image_w=$($pic.W)"); $extra.Add("image_h=$($pic.H)"); $extra.Add("resize_mode=$rm")
        }
        $stage['image.rgba'] = $pic.Bytes; $extra.Add('image=image.rgba')
        $op = Arg $A 'outpaint_pixels' $null
        if ($null -ne $op) {
            if ($null -ne $maskName -or $only -or $soft -or $rm -ne 0) { return 'refused: outpainting takes an image alone, with no mask, inpaint_full_res, soft_inpainting or resize_mode' }
            if (($op -isnot [long] -and $op -isnot [int]) -or $op -lt 8 -or $op -gt 256) { return 'refused: outpaint_pixels must be an integer within 8 and 256' }
            $mode = Arg $A 'outpaint_mode' 'poor'
            if ($mode -cnotin 'poor', 'mk2') { return 'refused: outpaint_mode must be poor or mk2' }
            $ob = Arg $A 'outpaint_mask_blur' $(if ($mode -eq 'mk2') { 8 } else { 4 })
            if (($ob -isnot [long] -and $ob -isnot [int]) -or $ob -lt 0 -or $ob -gt 64) { return 'refused: outpaint_mask_blur must be an integer within 0 and 64' }
            $dirs = @(Arg $A 'outpaint_direction' @('left', 'right', 'up', 'down'))
            $bits = 0
            foreach ($dname in $dirs) { $i = @('left', 'right', 'up', 'down').IndexOf([string]$dname); if ($i -lt 0) { return 'refused: outpaint_direction must list left, right, up or down' }; $bits = $bits -bor (1 -shl $i) }
            if ($bits -eq 0) { return 'refused: outpaint_direction must name at least one side' }
            $of = Arg $A 'inpainting_fill' 0
            if (($of -isnot [long] -and $of -isnot [int]) -or $of -lt 0 -or $of -gt 3) { return 'refused: inpainting_fill must be 0 (fill), 1 (original), 2 (latent noise) or 3 (latent nothing)' }
            $extra.Add("outpaint_pixels=$op"); $extra.Add("outpaint_mask_blur=$ob"); $extra.Add("outpaint_dirs=$bits"); $extra.Add("inpainting_fill=$of")
            if ($mode -eq 'mk2') {
                $nq = Arg $A 'outpaint_noise_q' 1; $ncv = Arg $A 'outpaint_color_variation' 0.05
                if (-not (IsNum $nq) -or [double]$nq -lt 0 -or [double]$nq -gt 4 -or -not (IsNum $ncv) -or [double]$ncv -lt 0 -or [double]$ncv -gt 1) { return 'refused: outpaint_noise_q must be within 0 and 4 and outpaint_color_variation within 0 and 1' }
                $extra.Add('outpaint_mode=mk2'); $extra.Add("outpaint_noise_q=$([double]$nq)"); $extra.Add("outpaint_color_variation=$([double]$ncv)")
            }
        }
        $d = Arg $A 'denoise' 0.75
        if (-not (IsNum $d) -or [double]$d -le 0 -or [double]$d -gt 1) { return 'refused: denoise must be above 0 and at most 1' }
        $extra.Add("denoise=$([double]$d)")
        if ($null -ne $maskName) {
            $m = Read-Picture $maskName 'mask'
            if ($m -is [string]) { return $m }
            if ($m.W -ne $pic.W -or $m.H -ne $pic.H) { return "refused: the mask is $($m.W)x$($m.H) and the image $($pic.W)x$($pic.H)" }
            $stage['mask.rgba'] = $m.Bytes; $extra.Add('mask=mask.rgba')
            $b = Arg $A 'mask_blur' 4
            if (($b -isnot [long] -and $b -isnot [int]) -or $b -lt 0 -or $b -gt 64) { return 'refused: mask_blur must be an integer within 0 and 64' }
            $extra.Add("mask_blur=$b")
            $fill = Arg $A 'inpainting_fill' 1
            if (($fill -isnot [long] -and $fill -isnot [int]) -or $fill -lt 0 -or $fill -gt 3) { return 'refused: inpainting_fill must be 0 (fill), 1 (original), 2 (latent noise) or 3 (latent nothing)' }
            if ($fill -ne 1) { $extra.Add("inpainting_fill=$fill") }
            if ($soft) {
                $extra.Add('soft_inpainting=1')
                foreach ($k in 'soft_power', 'soft_scale', 'soft_detail', 'soft_influence', 'soft_threshold', 'soft_contrast') {
                    $range = $softRanges[$k]
                    $v = Arg $A $k $range[0]
                    if (-not (IsNum $v) -or [double]$v -lt $range[1] -or [double]$v -gt $range[2]) { return "refused: $k must be within $($range[1]) and $($range[2])" }
                    $extra.Add("$k=$([double]$v)")
                }
            }
            if ($only) {
                $pad = Arg $A 'inpaint_full_res_padding' 32
                if (($pad -isnot [long] -and $pad -isnot [int]) -or $pad -lt 0 -or $pad -gt 256) { return 'refused: inpaint_full_res_padding must be an integer within 0 and 256' }
                $extra.Add('inpaint_full_res=1'); $extra.Add("inpaint_full_res_padding=$pad")
            }
        }
    }
    $cfg = Arg $A 'cfg' $(if ($flux) { 1.0 } else { 2.0 })
    if (-not (IsNum $cfg)) { return 'refused: cfg must be a number' }
    if ([double]$cfg -lt 0 -or [double]$cfg -gt 30) { return 'refused: cfg must be within 0 and 30' }
    $ss = Arg $A 'subseed_strength' 0
    if (-not (IsNum $ss) -or [double]$ss -lt 0 -or [double]$ss -gt 1) { return 'refused: subseed_strength must be within 0 and 1' }
    if ([double]$ss -gt 0) {
        $sub = Arg $A 'subseed' (Get-Random -Minimum 0 -Maximum 2147483647)
        if (($sub -isnot [long] -and $sub -isnot [int]) -or $sub -lt 0 -or $sub -gt 4294967295) { return 'refused: subseed must be an integer within 0 and 4294967295' }
        $extra.Add("subseed=$sub"); $extra.Add("subseed_strength=$([double]$ss)")
    }
    $rw = Arg $A 'seed_resize_from_w' 0; $rh = Arg $A 'seed_resize_from_h' 0
    foreach ($v in $rw, $rh) { if (($v -isnot [long] -and $v -isnot [int]) -or $v -lt 0 -or $v -gt 2048) { return 'refused: seed_resize_from_w and seed_resize_from_h must be integers within 0 and 2048' } }
    if ($rw -gt 0 -and $rh -gt 0) { $extra.Add("seed_resize_from_w=$rw"); $extra.Add("seed_resize_from_h=$rh") }
    if ($null -ne $hr) {
        if (-not (IsNum $hr) -or [double]$hr -lt 1 -or [double]$hr -gt 4) { return 'refused: hr_scale must be within 1 and 4' }
        $hw = [long][Math]::Truncate($vals.width * [double]$hr); $hh = [long][Math]::Truncate($vals.height * [double]$hr)
        if ($hw % 8 -ne 0 -or $hh % 8 -ne 0 -or $hw * $hh -gt 1048576) { return "refused: the hires size ${hw}x$hh must be multiples of 8 and at most 1024 x 1024" }
        $hd = Arg $A 'hr_denoise' 0.7
        if (-not (IsNum $hd) -or [double]$hd -le 0 -or [double]$hd -gt 1) { return 'refused: hr_denoise must be above 0 and at most 1' }
        $hs = Arg $A 'hr_steps' 0
        if (($hs -isnot [long] -and $hs -isnot [int]) -or $hs -lt 0 -or $hs -gt 150) { return 'refused: hr_steps must be an integer within 0 and 150' }
        $hc = Arg $A 'hr_cfg' $cfg
        if (-not (IsNum $hc) -or [double]$hc -lt 1 -or [double]$hc -gt 30) { return 'refused: hr_cfg must be within 1 and 30' }
        $extra.Add("hr_scale=$([double]$hr)"); $extra.Add("hr_denoise=$([double]$hd)"); $extra.Add("hr_steps=$hs"); $extra.Add("hr_cfg=$([double]$hc)")
        $hu = Arg $A 'hr_upscaler' 'Latent'
        if ($hu -isnot [string] -or -not $hrUpscalers.Contains($hu)) { return "refused: hr_upscaler must be one of: $(@($hrUpscalers.Keys) -join ', ')" }
        if ($hrUpscalers[$hu]) { $extra.Add("hr_upscaler=models/$($hrUpscalers[$hu])"); $extra.Add("hr_upscaler_name=$hu") }
        $hsm = Arg $A 'hr_sampler' ''
        if ($hsm -ne '') { if ($samplers -cnotcontains $hsm) { return "refused: hr_sampler must be one of: $($samplers -join ', ')" }; $extra.Add("hr_sampler=$hsm") }
        $hsc = Arg $A 'hr_scheduler' ''
        if ($hsc -ne '') { if ($schedulers -cnotcontains $hsc) { return "refused: hr_scheduler must be one of: $($schedulers -join ', ')" }; $extra.Add("hr_scheduler=$hsc") }
        $hpr = Arg $A 'hr_prompt' ''; $hng = Arg $A 'hr_negative' ''
        if ($hpr -isnot [string] -or $hng -isnot [string] -or $hpr.Length + $hng.Length -gt 16384) { return 'refused: hr_prompt and hr_negative must be strings within 16384 characters' }
        if ($hpr -ne '') { $extra.Add("hr_prompt=$(Line $hpr)") }; if ($hng -ne '') { $extra.Add("hr_negative=$(Line $hng)") }
        $hck = Arg $A 'hr_checkpoint' ''
        if ($hck -ne '') {
            if ($hck -isnot [string] -or $checkpoints -cnotcontains $hck -or (Is-Flux $hck)) { return "refused: hr_checkpoint must be a checkpoint other than Flux, one of: $(@($checkpoints | Where-Object { -not (Is-Flux $_) }) -join ', ')" }
            $extra.Add("hr_checkpoint=models/Stable-diffusion/$hck")
        }
        $hmo = @(Arg $A 'hr_modules' @())
        if ($hmo.Count -gt 0) {
            $mods = [ordered]@{}; foreach ($sub in 'VAE', 'text_encoder') { foreach ($f in Names $sub) { if (-not $mods.Contains($f)) { $mods[$f] = "$sub/$f" } } }
            $hv = ''
            foreach ($m in $hmo) {
                if ($m -isnot [string] -or -not $mods.Contains($m)) { return "refused: hr_modules names files in VAE or text_encoder, of: $(@($mods.Keys) -join ', ')" }
                if (Header-Has (Join-Path $Models $mods[$m]) '"decoder.conv_in.weight"') { $hv = $mods[$m] }
            }
            if ($hv) { $extra.Add("hr_vae=models/$hv") }
            $extra.Add("hr_modules=$(@($hmo | ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_) }) -join '|')")
        }
    }
    $ref = Arg $A 'refiner' ''
    if ($ref -ne '') {
        if ($ref -isnot [string] -or $checkpoints -cnotcontains $ref -or (Is-Flux $ref) -or $flux) { return "refused: refiner must be a checkpoint other than Flux, one of: $(@($checkpoints | Where-Object { -not (Is-Flux $_) }) -join ', ')" }
        $sw = Arg $A 'refiner_switch_at' 0.8
        if (-not (IsNum $sw) -or [double]$sw -le 0 -or [double]$sw -gt 1) { return 'refused: refiner_switch_at must be above 0 and at most 1' }
        $extra.Add("refiner=models/Stable-diffusion/$ref"); $extra.Add("refiner_switch_at=$([double]$sw)")
    }
    # Forge's extras; the values go into the infotext as Python writes floats.
    $fu = Arg $A 'freeu' $false; $sg = Arg $A 'sag' $false; $pg = Arg $A 'pag_scale' $null
    if ($fu -isnot [bool] -or $sg -isnot [bool]) { return 'refused: freeu and sag must be true or false' }
    if (($fu -or $sg -or $null -ne $pg) -and $flux) { return 'refused: FreeU, PAG and SAG are for SDXL and SD1.5, not Flux' }
    if ($fu) {
        $fr = [ordered]@{ freeu_b1 = @(1.01, 0, 2); freeu_b2 = @(1.02, 0, 2); freeu_s1 = @(0.99, 0, 4); freeu_s2 = @(0.95, 0, 4) }
        foreach ($k in $fr.Keys) {
            $v = Arg $A $k $fr[$k][0]
            if (-not (IsNum $v) -or [double]$v -lt $fr[$k][1] -or [double]$v -gt $fr[$k][2]) { return "refused: $k must be within $($fr[$k][1]) and $($fr[$k][2])" }
            $extra.Add("$k=$(PyFloat $v)")
        }
        foreach ($k in 'freeu_start', 'freeu_end') {
            $v = Arg $A $k $(if ($k -eq 'freeu_start') { 0 } else { 1 })
            if (-not (IsNum $v) -or [double]$v -lt 0 -or [double]$v -gt 1) { return "refused: $k must be within 0 and 1" }
            $extra.Add("$k=$(PyFloat $v)"); $extra.Add("${k}_bits=$([BitConverter]::DoubleToInt64Bits([double]$v))")
        }
    }
    if ($sg) {
        $sr = [ordered]@{ sag_scale = @(0.5, -2, 5); sag_blur_sigma = @(2, 0, 10); sag_threshold = @(1, 0, 4) }
        foreach ($k in $sr.Keys) {
            $v = Arg $A $k $sr[$k][0]
            if (-not (IsNum $v) -or [double]$v -lt $sr[$k][1] -or [double]$v -gt $sr[$k][2]) { return "refused: $k must be within $($sr[$k][1]) and $($sr[$k][2])" }
            if ($k -eq 'sag_blur_sigma' -and [double]$v -le 0) { return 'refused: sag_blur_sigma must be above 0' }
            $extra.Add("$k=$(PyFloat $v)")
        }
    }
    if ($null -ne $pg) {
        if (-not (IsNum $pg) -or [double]$pg -lt 0 -or [double]$pg -gt 100) { return 'refused: pag_scale must be within 0 and 100' }
        $extra.Add("pag_scale=$(PyFloat $pg)")
    }
    $dt = Arg $A 'dynthres' $false
    if ($dt -isnot [bool]) { return 'refused: dynthres must be true or false' }
    if ($dt) {
        if ($flux) { return 'refused: dynamic thresholding is for SDXL and SD1.5, not Flux' }
        $dn = [ordered]@{ dynthres_mimic_scale = @(7, 0, 100); dynthres_threshold_percentile = @(1, 0, 1) }
        foreach ($k in $dn.Keys) { $v = Arg $A $k $dn[$k][0]; if (-not (IsNum $v) -or [double]$v -lt $dn[$k][1] -or [double]$v -gt $dn[$k][2]) { return "refused: $k must be within $($dn[$k][1]) and $($dn[$k][2])" }; $extra.Add("$k=$(PyFloat $v)") }
        $mm = Arg $A 'dynthres_mimic_mode' 'Constant'; if ($dtModes -cnotcontains $mm) { return "refused: dynthres_mimic_mode must be one of: $($dtModes -join ', ')" }; $extra.Add("dynthres_mimic_mode=$mm")
        $v = Arg $A 'dynthres_mimic_scale_min' 0; if (-not (IsNum $v) -or [double]$v -lt 0 -or [double]$v -gt 100) { return 'refused: dynthres_mimic_scale_min must be within 0 and 100' }; $extra.Add("dynthres_mimic_scale_min=$(PyFloat $v)")
        $cm = Arg $A 'dynthres_cfg_mode' 'Constant'; if ($dtModes -cnotcontains $cm) { return "refused: dynthres_cfg_mode must be one of: $($dtModes -join ', ')" }; $extra.Add("dynthres_cfg_mode=$cm")
        foreach ($k in 'dynthres_cfg_scale_min', 'dynthres_sched_val') { $v = Arg $A $k $(if ($k -eq 'dynthres_sched_val') { 1 } else { 0 }); if (-not (IsNum $v) -or [double]$v -lt 0 -or [double]$v -gt 100) { return "refused: $k must be within 0 and 100" }; $extra.Add("$k=$(PyFloat $v)") }
        $sf = Arg $A 'dynthres_separate_feature_channels' $true; if ($sf -isnot [bool]) { return 'refused: dynthres_separate_feature_channels must be true or false' }; $extra.Add("dynthres_separate_feature_channels=$(if ($sf) { 'enable' } else { 'disable' })")
        $spt = Arg $A 'dynthres_scaling_startpoint' 'MEAN'; if ($spt -cnotin 'MEAN', 'ZERO') { return 'refused: dynthres_scaling_startpoint must be MEAN or ZERO' }; $extra.Add("dynthres_scaling_startpoint=$spt")
        $vm = Arg $A 'dynthres_variability_measure' 'AD'; if ($vm -cnotin 'AD', 'STD') { return 'refused: dynthres_variability_measure must be AD or STD' }; $extra.Add("dynthres_variability_measure=$vm")
        $v = Arg $A 'dynthres_interpolate_phi' 1; if (-not (IsNum $v) -or [double]$v -lt 0 -or [double]$v -gt 1) { return 'refused: dynthres_interpolate_phi must be within 0 and 1' }; $extra.Add("dynthres_interpolate_phi=$(PyFloat $v)")
    }
    $all = [Collections.Generic.List[string]]::new()
    $all.Add("prompt=$(Line $prompt)"); $all.Add("negative=$(Line $negative)"); $all.Add("model=models/Stable-diffusion/$model")
    foreach ($x in $lines) { $all.Add($x) }
    $all.Add("sampler=$sampler"); $all.Add("scheduler=$scheduler"); $all.Add("steps=$($vals.steps)"); $all.Add("cfg=$([double]$cfg)"); $all.Add("seed=$($vals.seed)")
    $all.Add("width=$($vals.width)"); $all.Add("height=$($vals.height)")
    foreach ($x in $extra) { $all.Add($x) }
    $all.Add("path=$((Get-Date).ToString('yyyyMMdd-HHmmss'))-$($vals.seed).png")
    @{ Lines = $all.ToArray(); Stage = $stage }
}

# The resident guest: one codex-vm for the server's life, started by the first
# call and again after a failure. It serves request-<n>.txt from its staging
# root in turn and answers each in done-<n>.txt under -Output; between calls
# the process is suspended, so an idle server costs no CPU while the weights
# stay on the device.
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class CodexImageProc { [DllImport("ntdll.dll")] public static extern int NtSuspendProcess(IntPtr h); [DllImport("ntdll.dll")] public static extern int NtResumeProcess(IntPtr h); }'
$script:guest = $null; $script:root = $null; $script:n = 0; $script:lastUse = [DateTime]::UtcNow

function Stop-Guest {
    if ($script:guest) {
        if (-not $script:guest.HasExited) { [void][CodexImageProc]::NtResumeProcess($script:guest.Handle); $script:guest.Kill(); [void]$script:guest.WaitForExit(15000) }
        $script:guest.Dispose()
    }
    $script:guest = $null
    if ($script:root -and (Test-Path $script:root)) {
        $link = Join-Path $script:root 'models'
        if (Test-Path $link) { [IO.Directory]::Delete($link) }
        foreach ($f in [IO.Directory]::GetFiles($script:root)) { [IO.File]::Delete($f) }
        [IO.Directory]::Delete($script:root)
    }
    $script:root = $null
}

function Start-Guest {
    $script:root = Join-Path $staging ([guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory $script:root | Out-Null
    New-Item -ItemType Junction -Path (Join-Path $script:root 'models') -Target $Models | Out-Null
    $script:n = 0
    foreach ($old in [IO.Directory]::GetFiles($Output, 'done-*.txt')) { [IO.File]::Delete($old) }
    $roots = @('-gpu-files', $script:root)
    foreach ($sub in 'text_encoder', 'VAE') { $d = Join-Path $Models $sub; if (Test-Path -PathType Container $d) { $roots += @('-gpu-files', $d) } }
    $script:guest = Start-Process -FilePath $vm -ArgumentList (@('-kernel', $Driver, '-disk', $Disk) + $roots + @('-gpu-out', $Output, '-mem', '3072', '-headless', '-output', (Join-Path $script:root 'serial.txt'))) -PassThru -WindowStyle Hidden
}

function Generate($A) {
    $rq = Request-Lines $A
    if ($rq -is [string]) { return @{ content = @(@{ type = 'text'; text = $rq }); isError = $true } }
    if (-not (Test-Path -PathType Leaf $Driver)) { return Refuse "the diffusion driver is not built ($Driver): pwsh build/compile.ps1 -Src apps/diffusion/DiffusionDriver.codex -Out $Driver -Log build-output/diffusion/driver.log -Kernel seed/Codex.cdx" }
    if (-not $script:guest -or $script:guest.HasExited) { Stop-Guest; Start-Guest }
    $n = ++$script:n
    $req = @($rq.Lines | ForEach-Object { $_ -replace '^(image|mask)=(image|mask)\.rgba$', "`$1=`$2-$n.rgba" })
    $name = ($req | Where-Object { $_.StartsWith('path=') }).Substring(5)
    $staged = @()
    try {
        foreach ($k in $rq.Stage.Keys) { $s = Join-Path $script:root ($k -replace '\.rgba$', "-$n.rgba"); [IO.File]::WriteAllBytes($s, $rq.Stage[$k]); $staged += $s }
        $tmp = Join-Path $script:root "request-$n.tmp"
        [IO.File]::WriteAllText($tmp, ($req -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
        [IO.File]::Move($tmp, (Join-Path $script:root "request-$n.txt"))
        $staged += Join-Path $script:root "request-$n.txt"
        [void][CodexImageProc]::NtResumeProcess($script:guest.Handle)
        $done = Join-Path $Output "done-$n.txt"
        $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSec)
        while (-not (Test-Path -PathType Leaf $done) -and -not $script:guest.HasExited -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
        if (-not (Test-Path -PathType Leaf $done)) {
            $why = if ($script:guest.HasExited) { "the driver stopped (exit $($script:guest.ExitCode))" } else { "no image within $TimeoutSec s" }
            Stop-Guest
            return Refuse $why
        }
        [void][CodexImageProc]::NtSuspendProcess($script:guest.Handle)
        $script:lastUse = [DateTime]::UtcNow
        Start-Sleep -Milliseconds 50
        $answer = [IO.File]::ReadAllText($done).Trim()
        [IO.File]::Delete($done)
        if (-not $answer.StartsWith('ok png')) { return Refuse "the driver answered: $answer" }
        $png = Join-Path $Output $name
        if (-not (Test-Path -PathType Leaf $png)) { return Refuse "the driver wrote no image ($answer)" }
        $head = [byte[]]::new(8); $fs = [IO.File]::OpenRead($png); [void]$fs.Read($head, 0, 8); $fs.Dispose()
        if ([Convert]::ToHexString($head) -ne '89504E470D0A1A0A') { return Refuse "the driver wrote $name, which is not a PNG" }
        return @{ content = @(@{ type = 'text'; text = $png }); structuredContent = @{ path = $png; request = $req }; isError = $false }
    } finally {
        foreach ($s in $staged) { if (Test-Path $s) { [IO.File]::Delete($s) } }
    }
}

[Console]::Error.WriteLine("codex_image ready; models $Models (read), output $Output (write), driver $Driver, disk $Disk, " + $(if ($haveT5) { "T5 tokenizer $T5Tokenizer" } else { "no T5 tokenizer, Flux refused: put tokenizer.json from https://huggingface.co/google-t5/t5-large in $(Join-Path $Models 'tokenizers\t5')" }))
$initialized = $false
# The next line from the client, or $null at its end; while waiting, a guest
# idle for -IdleMinutes is stopped.
$stdin = [IO.StreamReader]::new([Console]::OpenStandardInput(), [Text.UTF8Encoding]::new($false))
$idleCheck = [TimeSpan]::FromSeconds([Math]::Max(1, [Math]::Min(30, $IdleMinutes * 15)))
function Next-Line {
    $read = $stdin.ReadLineAsync()
    while (-not $read.Wait($idleCheck)) {
        if ($script:guest -and ([DateTime]::UtcNow - $script:lastUse).TotalMinutes -ge $IdleMinutes) { Stop-Guest; [Console]::Error.WriteLine("codex_image: idle $IdleMinutes min, guest stopped") }
    }
    $read.Result
}
while ($null -ne ($raw = Next-Line)) {
    if ($raw.Trim() -eq '') { continue }
    try { $m = $raw | ConvertFrom-Json } catch { Fail $null -32700 'parse error'; continue }
    $id = if ($m.PSObject.Properties.Name -contains 'id') { $m.id } else { $null }
    $method = if ($m.PSObject.Properties.Name -contains 'method') { $m.method } else { '' }
    $params = if ($m.PSObject.Properties.Name -contains 'params') { $m.params } else { $null }
    if ($null -eq $id) { continue }
    switch ($method) {
        'initialize' {
            $want = Arg $params 'protocolVersion' ''
            $v = if ($protocols -contains $want) { $want } else { $protocols[0] }
            $initialized = $true
            Reply $id @{ protocolVersion = $v; capabilities = @{ tools = @{ listChanged = $false } }; serverInfo = @{ name = 'codex-image'; version = '0.1.0' }; instructions = 'codex_image generates an SDXL or SD1.5 image on this machine and returns the PNG path. Models are named by file name; see the tool schema.' }
        }
        'ping' { Reply $id @{} }
        'tools/list' { Reply $id @{ tools = @($tool) } }
        'tools/call' {
            if (-not $initialized) { Fail $id -32002 'not initialized'; break }
            $name = Arg $params 'name' ''
            if ($name -ne 'codex_image') { Fail $id -32602 "unknown tool $name"; break }
            try { Reply $id (Generate (Arg $params 'arguments' $null)) } catch { Reply $id (Refuse "internal error: $($_.Exception.Message)") }
        }
        default { Fail $id -32601 "method not found: $method" }
    }
}
Stop-Guest
