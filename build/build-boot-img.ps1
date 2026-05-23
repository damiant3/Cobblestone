$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Repo     = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Seed     = Join-Path $Repo 'seed\Codex.cdx'
$ImgOut   = Join-Path $Repo 'seed\Codex.img'

if (-not (Test-Path -PathType Leaf $Seed)) { Write-Host "FAIL: $Seed missing"; exit 1 }

$BuildOut = Join-Path $Repo 'build-output'
if (-not (Test-Path $BuildOut)) { New-Item -ItemType Directory -Force $BuildOut | Out-Null }

Write-Host "=== Build Boot IMG ==="

# Step 1: Compile boot source to CDX
$compileScript = Join-Path $PSScriptRoot 'compile.ps1'
$BootSource = Join-Path $Repo 'apps\works\UefiBoot.codex'
$BootCdx = Join-Path $BuildOut 'boot.cdx'
$BootLog = Join-Path $BuildOut 'img-compile.log'
Write-Host "  Compiling $BootSource -> CDX..."
& pwsh -NoProfile -File $compileScript -Src $BootSource -Out $BootCdx -Log $BootLog
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $BootCdx)) { Write-Host "FAIL: CDX compile failed"; exit 1 }

# Step 2: Convert CDX to PE via PE plug
$PePlug = Join-Path $Repo 'codex\plugs\pe\run.ps1'
$BootPe = Join-Path $BuildOut 'boot.efi'
Write-Host "  Converting CDX -> PE via plug..."
& pwsh -NoProfile -File $PePlug -CdxInput $BootCdx -Out $BootPe
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $BootPe)) { Write-Host "FAIL: PE plug failed"; exit 1 }

# Step 3: Build IMG via IMG plug (PE + seed CDX -> FAT16 GPT image)
$ImgPlug = Join-Path $Repo 'codex\plugs\img\run.ps1'
if (Test-Path -PathType Leaf $ImgPlug) {
    Write-Host "  Building IMG via plug..."
    & pwsh -NoProfile -File $ImgPlug -PeInput $BootPe -CdxInput $Seed -Out $ImgOut -Fat16
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $ImgOut)) { Write-Host "FAIL: IMG plug failed"; exit 1 }
} else {
    Write-Host "SKIP: IMG plug not yet available (codex/plugs/img/run.ps1 missing)"
    exit 0
}

$totalSize = (Get-Item $ImgOut).Length
Write-Host "Done: $ImgOut ($([math]::Round($totalSize / 1MB, 1)) MB)"
