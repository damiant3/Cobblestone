# Build a bootable diag/probe image: [stub][CDX .text][CDX .rodata] wrapped in
# a GPT/FAT16 disk image.
#
# The stub comes from cdx-to-pe.ps1, which emits it from PowerShell. It used to
# come from ml64 assembling option_a_stub.asm; that stub is RETIRED and no MSVC
# is involved in any boot artifact this script builds (B5.4 step 4). The .asm
# and its ml64 builder build-option-a-legacy.ps1 were DELETED 2026-08-03: they
# had been kept as the reference for the ASUS display defect, that defect turned
# out to be the ConOut re-mode (cured in cdx-to-pe.ps1, gated by
# build/boot/test-conout-remode.ps1), and the legacy stub rendered on that panel
# only because it never called ConOut at all. The switch also moves the
# framebuffer handoff to the magic-gated block at 0x1F000, which is why every
# probe reads it through GopHandoff's boot-fb-* rather than off 0x8000.
#
#   pwsh build/boot/build-option-a.ps1 -Src apps/works/GopBoot.codex
[CmdletBinding()]
param(
    [string]$Src = 'apps/works/GopBoot.codex',
    [string]$Out = 'build/boot/optiona.img',
    [int]$AllocPages = 32768,  # 128 MB: page tables + memmap + Codex heap + stack
    # Embed the CDX seed on the ESP as CODEX.CDX so the booted payload can read
    # it back with its own drivers. '' skips it (a menu-only proof image).
    [string]$Seed = 'seed/Codex.cdx',
    # A TrueType font shipped on the ESP as CMUNSS.TTF for the desktop's
    # text (H4c). '' skips it; the desktop falls back to the CBF font.
    [string]$Font = 'fonts/cc0/cmunss.ttf',
    # Codex source shipped on the ESP as SOURCE.SRC. The desktop's file
    # manager draws a .SRC file as highlighted source, so this is what makes
    # that screen reachable. '' skips it.
    [string]$Source = 'build-output/Codex.codex',
    [int]$TotalSectors = 32768,  # 16 MB: PE + a 2.1 MB seed with room to spare
    # Format the ESP as FAT32 (vendor-stick layout). Needs -TotalSectors >=
    # ~70000: a real FAT32 volume carries at least 65525 clusters.
    [switch]$Fat32,
    # The compiler that builds the payload. Empty means whatever build.ps1 last
    # left in build-output, which is fine for a dev loop and wrong for anything
    # anyone flashes: the image then has no provenance at all. Pass the seed for
    # an artifact that goes near a stick.
    [string]$Kernel = '',
    # ExitBootServices before handing off (cdx-to-pe -ExitBootServices). Set it
    # for every driver-truth probe: with boot services alive the firmware's own
    # xHCI driver keeps driving the controller under test. Leave it off only
    # for payloads that call ConIn/ConOut (KeyProof, the dev console).
    [switch]$Ebs
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
trap { Write-Host "ERROR line $($_.InvocationInfo.ScriptLineNumber): $_"; throw $_ }

$here = $PSScriptRoot
$repo = (Resolve-Path (Join-Path $here '..' '..')).Path
$bo   = Join-Path $repo 'build-output'
if (-not (Test-Path $bo)) { New-Item -ItemType Directory -Force $bo | Out-Null }

# ---- 1. Bundle + compile the Codex source to CDX ----
$bundle  = Join-Path $repo 'build/bundle-app.ps1'
$compile = Join-Path $repo 'build/compile.ps1'
$bundled = Join-Path $bo 'optiona-bundled.codex'
$cdxOut  = Join-Path $bo 'optiona.cdx'
$log     = Join-Path $bo 'optiona-compile.log'
Write-Host "[opt-a] bundling $Src"
& pwsh -NoProfile -File $bundle -Src (Join-Path $repo $Src) -Out $bundled
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $bundled)) { throw "bundle failed" }
Write-Host "[opt-a] compiling -> CDX (pet: interactive poll loop, no heap progress)"
$compileArgs = @('-Src', $bundled, '-Out', $cdxOut, '-Log', $log, '-Pet')
if ($Kernel -ne '') {
    if (-not (Test-Path $Kernel)) { throw "-Kernel not found: $Kernel" }
    $compileArgs += @('-Kernel', (Resolve-Path $Kernel).Path)
}
& pwsh -NoProfile -File $compile @compileArgs
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $cdxOut)) {
    Get-Content $log -ErrorAction SilentlyContinue | Select-Object -First 20 | ForEach-Object { Write-Host "  $_" }
    throw "CDX compile failed"
}

# ---- 2. CDX -> PE, through the one surviving stub ----
#
# This was ml64 assembling option_a_stub.asm, extracting its .text and patching
# five placeholder immediates. That stub is gone (B5.4 step 4) and cdx-to-pe.ps1
# emits the equivalent bytes from PowerShell, so no MSVC is involved in any boot
# artifact. The two emitted the same PE shape already -- same 512-byte headers,
# 4096 section alignment, .text plus .reloc -- and they differed only in where
# the stub bytes came from. -AllocPages keeps its name here because four probe
# commands in HardwareSitting.md pass it; it is cdx-to-pe's -HeapPages.
$peOutFile = Join-Path $bo 'optiona.efi'
$peArgs = @('-CdxInput', $cdxOut, '-Out', $peOutFile, '-HeapPages', $AllocPages)
if ($Ebs) { $peArgs += '-ExitBootServices' }
& pwsh -NoProfile -File (Join-Path $repo 'build/cdx-to-pe.ps1') @peArgs
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $peOutFile)) { throw "PE conversion failed" }

# ---- 5. Wrap into GPT/FAT16 image ----
$outAbs = if ([System.IO.Path]::IsPathRooted($Out)) { $Out } else { Join-Path $repo $Out }
$imgArgs = @('-PeInput', $peOutFile, '-Out', $outAbs, '-TotalSectors', $TotalSectors)
if ($Fat32) { $imgArgs += '-Fat32' }
if ($Seed) {
    $seedAbs = if ([System.IO.Path]::IsPathRooted($Seed)) { $Seed } else { Join-Path $repo $Seed }
    if (Test-Path $seedAbs) { $imgArgs += @('-Seed', $seedAbs) }
    else { Write-Host "[opt-a] WARN: seed not found at $seedAbs; image will carry no CODEX.CDX" }
}
if ($Font) {
    $fontAbs = if ([System.IO.Path]::IsPathRooted($Font)) { $Font } else { Join-Path $repo $Font }
    if (Test-Path $fontAbs) { $imgArgs += @('-Font', $fontAbs) }
    else { Write-Host "[opt-a] WARN: font not found at $fontAbs; image will carry no CMUNSS.TTF" }
}
# The desktop's file manager renders a .SRC file as highlighted Codex source
# rather than as hex, so an image with no source on it cannot exercise that
# screen at all. Default to the compiler concat when one has been built.
if ($Source) {
    $srcAbs = if ([System.IO.Path]::IsPathRooted($Source)) { $Source } else { Join-Path $repo $Source }
    if (Test-Path $srcAbs) { $imgArgs += @('-Source', $srcAbs) }
    else { Write-Host "[opt-a] WARN: source not found at $srcAbs; image will carry no SOURCE.SRC" }
}
& pwsh -NoProfile -File (Join-Path $repo 'build/build-img.ps1') @imgArgs
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $outAbs)) { throw "build-img failed" }
Write-Host "Done: $outAbs"
Write-Host "  Visual:  tools/codex-vm.exe -kernel $outAbs -uefi -gop -screenshot build/boot/optiona.bmp"
Write-Host "  Strict:  tools/codex-vm.exe -kernel $outAbs -uefi-strict -headless"
