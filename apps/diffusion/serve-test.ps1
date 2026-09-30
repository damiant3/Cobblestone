# serve-test.ps1 -- the codex_image server's protocol and authority, without
# generating an image: every refusal the tool promises, a valid call against a
# missing driver, and a valid call against a stand-in driver that writes no
# image (it stops at once), after which the staging root must be empty and the models folder
# unchanged. Prints PASS or FAIL per line and exits 1 on any FAIL.
#
#   pwsh apps/diffusion/serve-test.ps1 -StandIn <any cdx that writes no PNG>
[CmdletBinding()]
param([Parameter(Mandatory)][string]$StandIn)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$serve = Join-Path $PSScriptRoot 'serve.ps1'
$models = 'D:\AI\DiffusionForge\webui\models'
$out = Join-Path $repo 'build-output\diffusion-images-test'
$staging = Join-Path $repo 'build-output\diffusion-staging-test'
$inputs = Join-Path $repo 'build-output\diffusion-inputs-test'
$script:fail = 0

function Check([string]$Name, [bool]$Ok) { if ($Ok) { "PASS $Name" } else { "FAIL $Name"; $script:fail++ } }

function Start-Server([string]$Driver) {
    $psi = [Diagnostics.ProcessStartInfo]::new((Get-Process -Id $PID).Path)
    foreach ($a in @('-NoProfile', '-File', $serve, '-Output', $out, '-Inputs', $inputs, '-Driver', $Driver, '-TimeoutSec', '120', '-Staging', $staging)) { $psi.ArgumentList.Add($a) }
    $psi.RedirectStandardInput = $true; $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true; $psi.UseShellExecute = $false
    [Diagnostics.Process]::Start($psi)
}
function Call($P, $Id, [string]$Method, $Params) {
    $P.StandardInput.WriteLine((@{ jsonrpc = '2.0'; id = $Id; method = $Method; params = $Params } | ConvertTo-Json -Depth 10 -Compress))
    $P.StandardInput.Flush()
    $P.StandardOutput.ReadLine() | ConvertFrom-Json
}
function Refusal($R) { if ($R.result.isError) { $R.result.content[0].text } else { '' } }

function Write-Png([string]$Name, [int]$W, [int]$H) {
    $b = [Drawing.Bitmap]::new($W, $H)
    for ($y = 0; $y -lt $H; $y += 8) { for ($x = 0; $x -lt $W; $x += 8) { $b.SetPixel($x, $y, [Drawing.Color]::FromArgb(255, ($x * 3) % 256, ($y * 5) % 256, 128)) } }
    $b.Save((Join-Path $inputs $Name), [Drawing.Imaging.ImageFormat]::Png); $b.Dispose()
}
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $inputs | Out-Null
foreach ($old in @(Get-ChildItem -LiteralPath $inputs -File)) { [IO.File]::Delete($old.FullName) }
Write-Png 'pic.png' 256 256; Write-Png 'mask.png' 256 256; Write-Png 'small.png' 128 128; Write-Png 'odd.png' 250 250

$modelsBefore = (Get-ChildItem -LiteralPath $models -Recurse -File | Measure-Object -Property Length -Sum).Sum
$p = Start-Server (Join-Path $repo 'build-output\diffusion\no-such-driver.cdx')
$r = Call $p 1 'tools/call' @{ name = 'codex_image'; arguments = @{ prompt = 'a cat' } }
Check 'call before initialize is an error' ($r.error.code -eq -32002)
$r = Call $p 2 'initialize' @{ protocolVersion = '2025-06-18'; capabilities = @{}; clientInfo = @{ name = 'serve-test'; version = '0' } }
Check 'initialize echoes the protocol' ($r.result.protocolVersion -eq '2025-06-18')
$r = Call $p 3 'tools/list' @{}
Check 'one tool, codex_image' ($r.result.tools.Count -eq 1 -and $r.result.tools[0].name -eq 'codex_image')
$bad = [ordered]@{
    'empty prompt'                   = @{ prompt = ' ' }
    'a path for model'               = @{ prompt = 'a cat'; model = '..\Stable-diffusion\dreamshaperXL_lightningDPMSDE.safetensors' }
    'an unknown model'               = @{ prompt = 'a cat'; model = 'nothing.safetensors' }
    'a path for a LoRA'              = @{ prompt = 'a cat'; loras = @(@{ name = '../x.safetensors'; weight = 1 }) }
    'a LoRA weight past 4'           = @{ prompt = 'a cat'; loras = @(@{ name = (Get-ChildItem "$models\Lora" -Filter *.safetensors | Select-Object -First 1).Name; weight = 9 }) }
    'an unknown sampler'             = @{ prompt = 'a cat'; sampler = 'Heun++' }
    'an unknown scheduler'           = @{ prompt = 'a cat'; scheduler = 'Cosine' }
    'width not a multiple of 8'       = @{ prompt = 'a cat'; width = 1004 }
    'steps of 0'                     = @{ prompt = 'a cat'; steps = 0 }
    'a text cfg'                     = @{ prompt = 'a cat'; cfg = 'high' }
    'an image not in the inputs'     = @{ prompt = 'a cat'; image = 'nothing.png' }
    'a path for an image'            = @{ prompt = 'a cat'; image = '..\pic.png' }
    'a mask without an image'        = @{ prompt = 'a cat'; mask = 'mask.png' }
    'hires fix with an image'        = @{ prompt = 'a cat'; image = 'pic.png'; hr_scale = 2 }
    'a resize mode not built'        = @{ prompt = 'a cat'; image = 'pic.png'; width = 512; height = 512; resize_mode = 4 }
    'latent upscale with a mask'     = @{ prompt = 'a cat'; image = 'pic.png'; mask = 'mask.png'; width = 512; height = 512; resize_mode = 3 }
    'only masked without a mask'     = @{ prompt = 'a cat'; image = 'pic.png'; inpaint_full_res = $true }
    'a padding past 256'             = @{ prompt = 'a cat'; image = 'pic.png'; mask = 'mask.png'; inpaint_full_res = $true; inpaint_full_res_padding = 300 }
    'latent upscale off 8'         = @{ prompt = 'a cat'; image = 'odd.png'; width = 512; height = 512; resize_mode = 3 }
    'a mask of another size'         = @{ prompt = 'a cat'; image = 'pic.png'; mask = 'small.png' }
    'a denoise of 0'                 = @{ prompt = 'a cat'; image = 'pic.png'; denoise = 0 }
    'a masked content past 3'        = @{ prompt = 'a cat'; image = 'pic.png'; mask = 'mask.png'; inpainting_fill = 4 }
    'a hires size off 8'             = @{ prompt = 'a cat'; width = 512; height = 512; hr_scale = 1.3 }
    'a variation strength past 1'    = @{ prompt = 'a cat'; subseed = 5; subseed_strength = 1.5 }
    'a seed resize past 2048'        = @{ prompt = 'a cat'; seed_resize_from_w = 4096; seed_resize_from_h = 512 }
    'an unknown hires upscaler'      = @{ prompt = 'a cat'; width = 512; height = 512; hr_scale = 2; hr_upscaler = 'LDSR' }
    'an unknown hires checkpoint'    = @{ prompt = 'a cat'; width = 512; height = 512; hr_scale = 2; hr_checkpoint = 'nothing.safetensors' }
    'an unknown hires module'        = @{ prompt = 'a cat'; width = 512; height = 512; hr_scale = 2; hr_modules = @('nothing.safetensors') }
    'a clip skip past 12'            = @{ prompt = 'a cat'; clip_skip = 13 }
    'soft inpainting without a mask' = @{ prompt = 'a cat'; image = 'pic.png'; soft_inpainting = $true }
    'outpainting with a mask'        = @{ prompt = 'a cat'; image = 'pic.png'; mask = 'mask.png'; outpaint_pixels = 64 }
    'an unknown outpaint mode'       = @{ prompt = 'a cat'; image = 'pic.png'; outpaint_pixels = 64; outpaint_mode = 'mk3' }
    'an unknown outpaint side'       = @{ prompt = 'a cat'; image = 'pic.png'; outpaint_pixels = 64; outpaint_direction = @('north') }
    'a soft detail below 1'          = @{ prompt = 'a cat'; image = 'pic.png'; mask = 'mask.png'; soft_inpainting = $true; soft_detail = 0.5 }
    'FreeU with Flux'                = @{ prompt = 'a cat'; model = 'flux1-schnell-fp8-e4m3fn.safetensors'; freeu = $true }
    'PAG with a refiner'             = @{ prompt = 'a cat'; refiner = 'dreamshaperXL_lightningDPMSDE.safetensors'; pag_scale = 3 }
    'a FreeU end past 1'             = @{ prompt = 'a cat'; freeu = $true; freeu_end = 1.2 }
    'a SAG blur sigma of 0'          = @{ prompt = 'a cat'; sag = $true; sag_blur_sigma = 0 }
    'dynthres with Flux'             = @{ prompt = 'a cat'; model = 'flux1-schnell-fp8-e4m3fn.safetensors'; dynthres = $true }
    'an unknown dynthres mode'       = @{ prompt = 'a cat'; dynthres = $true; dynthres_mimic_mode = 'Wobble' }
}
$i = 10
foreach ($k in $bad.Keys) { $r = Call $p ($i++) 'tools/call' @{ name = 'codex_image'; arguments = $bad[$k] }; Check "refuses $k" ((Refusal $r) -like 'refused: *' -and (Refusal $r) -notlike '*driver*') }
$r = Call $p 30 'tools/call' @{ name = 'codex_image'; arguments = @{ prompt = 'a cat'; negative = 'blurry'; steps = 6; cfg = 2; seed = 7; width = 1024; height = 1024; sampler = 'Euler' } }
Check 'a valid call reaches the missing driver' ((Refusal $r) -like '*driver is not built*')
$r = Call $p 32 'tools/call' @{ name = 'codex_image'; arguments = @{ prompt = 'a cat'; image = 'pic.png'; mask = 'mask.png'; denoise = 0.5 } }
Check 'a valid inpaint call reaches the missing driver' ((Refusal $r) -like '*driver is not built*')
$r = Call $p 33 'tools/call' @{ name = 'codex_image'; arguments = @{ prompt = 'a cat'; width = 512; height = 512; hr_scale = 1.5 } }
Check 'a valid hires call reaches the missing driver' ((Refusal $r) -like '*driver is not built*')
$r = Call $p 34 'tools/call' @{ name = 'codex_image'; arguments = @{ prompt = 'a cat'; width = 512; height = 512; hr_scale = 2; hr_upscaler = 'R-ESRGAN 4x+' } }
Check 'a valid hires call through R-ESRGAN 4x+ reaches the missing driver' ((Refusal $r) -like '*driver is not built*')
$r = Call $p 40 'tools/call' @{ name = 'codex_image'; arguments = @{ prompt = 'a cat'; width = 512; height = 512; hr_scale = 2; hr_checkpoint = 'dreamshaperXL_lightningDPMSDE.safetensors' } }
Check 'a valid hires checkpoint call reaches the missing driver' ((Refusal $r) -like '*driver is not built*')
$r = Call $p 41 'tools/call' @{ name = 'codex_image'; arguments = @{ prompt = 'a cat'; width = 512; height = 512; hr_scale = 2; refiner = 'dreamshaperXL_lightningDPMSDE.safetensors'; refiner_switch_at = 0.5 } }
Check 'a valid hires call with a refiner reaches the missing driver' ((Refusal $r) -like '*driver is not built*')
$r = Call $p 42 'tools/call' @{ name = 'codex_image'; arguments = @{ prompt = 'a cat'; image = 'pic.png'; mask = 'mask.png'; soft_inpainting = $true; soft_threshold = 1.5 } }
Check 'a valid soft inpainting call reaches the missing driver' ((Refusal $r) -like '*driver is not built*')
$r = Call $p 43 'tools/call' @{ name = 'codex_image'; arguments = @{ prompt = 'a cat'; image = 'pic.png'; outpaint_pixels = 64; outpaint_direction = @('left', 'down') } }
Check 'a valid outpaint call reaches the missing driver' ((Refusal $r) -like '*driver is not built*')
$r = Call $p 44 'tools/call' @{ name = 'codex_image'; arguments = @{ prompt = 'a cat'; image = 'pic.png'; outpaint_pixels = 64; outpaint_mode = 'mk2'; outpaint_noise_q = 1.5 } }
Check 'a valid outpainting mk2 call reaches the missing driver' ((Refusal $r) -like '*driver is not built*')
$r = Call $p 45 'tools/call' @{ name = 'codex_image'; arguments = @{ prompt = 'a cat'; cfg = 5; freeu = $true; freeu_b1 = 1.3; freeu_start = 0.2; freeu_end = 0.8; pag_scale = 3; sag = $true } }
Check 'a valid FreeU, PAG and SAG call reaches the missing driver' ((Refusal $r) -like '*driver is not built*')
$r = Call $p 46 'tools/call' @{ name = 'codex_image'; arguments = @{ prompt = 'a cat'; cfg = 5; dynthres = $true; dynthres_threshold_percentile = 0.95; dynthres_mimic_mode = 'Cosine Down' } }
Check 'a valid dynamic thresholding call reaches the missing driver' ((Refusal $r) -like '*driver is not built*')
$r = Call $p 47 'tools/call' @{ name = 'codex_image'; arguments = @{ prompt = 'a cat'; cfg = 5; sampler = 'DDIM CFG++'; sag = $true; pag_scale = 3; dynthres = $true } }
Check 'a valid DDIM CFG++ call with SAG, PAG and dynamic thresholding reaches the missing driver' ((Refusal $r) -like '*driver is not built*')
$r = Call $p 35 'tools/call' @{ name = 'codex_image'; arguments = @{ prompt = 'a cat'; image = 'pic.png'; width = 512; height = 256; resize_mode = 2 } }
Check 'a valid resize-and-fill call reaches the missing driver' ((Refusal $r) -like '*driver is not built*')
$r = Call $p 36 'tools/call' @{ name = 'codex_image'; arguments = @{ prompt = 'a cat'; image = 'pic.png'; mask = 'mask.png'; inpainting_fill = 2 } }
Check 'a valid latent-noise inpaint call reaches the missing driver' ((Refusal $r) -like '*driver is not built*')
$r = Call $p 37 'tools/call' @{ name = 'codex_image'; arguments = @{ prompt = 'a cat'; image = 'pic.png'; mask = 'mask.png'; inpainting_fill = 0 } }
Check 'a valid fill inpaint call reaches the missing driver' ((Refusal $r) -like '*driver is not built*')
$r = Call $p 38 'tools/call' @{ name = 'codex_image'; arguments = @{ prompt = 'a cat'; image = 'pic.png'; mask = 'mask.png'; width = 512; height = 384; resize_mode = 2 } }
Check 'a valid inpaint call with a resized image and mask reaches the missing driver' ((Refusal $r) -like '*driver is not built*')
$r = Call $p 39 'tools/call' @{ name = 'codex_image'; arguments = @{ prompt = 'a cat'; image = 'pic.png'; width = 512; height = 384; resize_mode = 3 } }
Check 'a valid latent upscale call reaches the missing driver' ((Refusal $r) -like '*driver is not built*')
$r = Call $p 40 'tools/call' @{ name = 'codex_image'; arguments = @{ prompt = 'a cat'; image = 'pic.png'; mask = 'mask.png'; width = 512; height = 384; inpaint_full_res = $true; inpaint_full_res_padding = 16 } }
Check 'a valid only-masked inpaint call reaches the missing driver' ((Refusal $r) -like '*driver is not built*')
$r = Call $p 31 'nothing/here' @{}
Check 'an unknown method is -32601' ($r.error.code -eq -32601)
$p.StandardInput.Close(); [void]$p.WaitForExit(10000)

$p = Start-Server (Resolve-Path $StandIn).Path
$null = Call $p 1 'initialize' @{ protocolVersion = '2024-11-05'; capabilities = @{}; clientInfo = @{ name = 'serve-test'; version = '0' } }
$r = Call $p 2 'tools/call' @{ name = 'codex_image'; arguments = @{ prompt = 'a cat'; seed = 7 } }
Check 'a driver that stops is reported' ((Refusal $r) -like '*driver stopped*')
$r = Call $p 3 'tools/call' @{ name = 'codex_image'; arguments = @{ prompt = 'a cat'; seed = 7; image = 'pic.png'; mask = 'mask.png' } }
Check 'an inpaint call with that driver is reported' ((Refusal $r) -like '*driver stopped*')
$p.StandardInput.Close(); [void]$p.WaitForExit(10000)
Check 'staging is empty afterwards' (@(Get-ChildItem -LiteralPath $staging -Force).Count -eq 0)
Check 'the models folder is unchanged' ((Get-ChildItem -LiteralPath $models -Recurse -File | Measure-Object -Property Length -Sum).Sum -eq $modelsBefore)
Check 'the inputs folder is unchanged' (@(Get-ChildItem -LiteralPath $inputs -File).Count -eq 4)
Check 'no image was written' (@(Get-ChildItem -LiteralPath $out -File -ErrorAction SilentlyContinue).Count -eq 0)

$bare = Join-Path $repo 'build-output\serve-test-models'
New-Item -ItemType Directory -Force $bare | Out-Null
if (-not (Test-Path (Join-Path $bare 'Stable-diffusion'))) { New-Item -ItemType Junction -Path (Join-Path $bare 'Stable-diffusion') -Target (Join-Path $models 'Stable-diffusion') | Out-Null }
$said = & (Get-Process -Id $PID).Path -NoProfile -File $serve -Models $bare -Disk (Join-Path $bare 'no-disk.img') -Output $out 2>&1
$code = $LASTEXITCODE
[IO.Directory]::Delete((Join-Path $bare 'Stable-diffusion')); [IO.Directory]::Delete($bare)
Check 'a models folder without a CLIP tokenizer refuses to start, naming the download' ($code -eq 1 -and "$said" -like '*no CLIP tokenizer*huggingface.co/openai/clip-vit-large-patch14*')
if ($script:fail) { exit 1 }
exit 0
