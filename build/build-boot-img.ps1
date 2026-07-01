$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Repo     = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Seed     = Join-Path $Repo 'seed\Codex.cdx'
$ImgOut   = Join-Path $Repo 'seed\Codex.img'

if (-not (Test-Path -PathType Leaf $Seed)) { Write-Host "FAIL: $Seed missing"; exit 1 }

$BuildOut = Join-Path $Repo 'build-output'
if (-not (Test-Path $BuildOut)) { New-Item -ItemType Directory -Force $BuildOut | Out-Null }

Write-Host "=== Build Boot IMG ==="

# Step 1: Bundle boot source with all dependencies
$bundleScript = Join-Path $PSScriptRoot 'bundle-app.ps1'
$compileScript = Join-Path $PSScriptRoot 'compile.ps1'
$BootSource = Join-Path $Repo 'apps\works\UefiBoot.codex'
$BundledSource = Join-Path $BuildOut 'boot-bundled.codex'
$BootCdx = Join-Path $BuildOut 'boot.cdx'
$BootLog = Join-Path $BuildOut 'img-compile.log'
Write-Host "  Bundling $BootSource..."
& pwsh -NoProfile -File $bundleScript -Src $BootSource -Out $BundledSource
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $BundledSource)) { Write-Host "FAIL: bundle failed"; exit 1 }

# Step 2: Compile bundled source to CDX
Write-Host "  Compiling bundled source -> CDX (UEFI mode)..."
& pwsh -NoProfile -File $compileScript -Src $BundledSource -Out $BootCdx -Log $BootLog -Uefi
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $BootCdx)) {
    Write-Host "FAIL: CDX compile failed"
    Get-Content $BootLog -ErrorAction SilentlyContinue | Select-Object -First 15 | ForEach-Object { Write-Host "  $_" }
    exit 1
}

# Step 3: Convert CDX to PE
$PeScript = Join-Path $PSScriptRoot 'cdx-to-pe.ps1'
$BootPe = Join-Path $BuildOut 'boot.efi'
Write-Host "  Converting CDX -> PE..."
& pwsh -NoProfile -File $PeScript -CdxInput $BootCdx -Out $BootPe -HeapPages 131072
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $BootPe)) { Write-Host "FAIL: PE conversion failed"; exit 1 }

# Step 4: Build GPT FAT16 disk image
$ImgScript = Join-Path $PSScriptRoot 'build-img.ps1'
$SourceFile = Join-Path $BuildOut 'Codex.codex'
Write-Host "  Building GPT disk image..."
if (Test-Path -PathType Leaf $SourceFile) {
    & pwsh -NoProfile -File $ImgScript -PeInput $BootPe -Out $ImgOut -Source $SourceFile
} else {
    & pwsh -NoProfile -File $ImgScript -PeInput $BootPe -Out $ImgOut
}
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $ImgOut)) { Write-Host "FAIL: IMG build failed"; exit 1 }

$totalSize = (Get-Item $ImgOut).Length
Write-Host "Done: $ImgOut ($([math]::Round($totalSize / 1MB, 1)) MB)"
