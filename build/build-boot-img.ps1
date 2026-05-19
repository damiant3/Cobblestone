$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Repo     = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Seed     = Join-Path $Repo 'seed\Codex.cdx'
$ImgOut   = Join-Path $Repo 'seed\Codex.img'

if (-not (Test-Path -PathType Leaf $Seed)) { Write-Host "FAIL: $Seed missing"; exit 1 }

$BuildOut = Join-Path $Repo 'build-output'
if (-not (Test-Path $BuildOut)) { New-Item -ItemType Directory -Force $BuildOut | Out-Null }

Write-Host "=== Build Boot IMG ==="
$compileScript = Join-Path $PSScriptRoot 'test-compile.ps1'
$BootSource = Join-Path $Repo 'apps\works\UefiBoot.codex'
& pwsh -NoProfile -File $compileScript -Src $BootSource -Out $ImgOut -Log (Join-Path $BuildOut 'img-build.log') -Img -Fat16 -Uefi
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $ImgOut)) { Write-Host "FAIL: IMG compile failed"; exit 1 }

$totalSize = (Get-Item $ImgOut).Length
Write-Host "Done: $ImgOut ($($totalSize / 1MB) MB)"
