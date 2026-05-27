# Build explorer HTML pages from compiled CDX binaries.
# Each CDX emits HTML to serial when run. We capture that output.
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$OutDir = 'D:\Projects\CodexMagic\explorer\pages'
New-Item -ItemType Directory -Force $OutDir | Out-Null

. (Join-Path $Repo 'build\vm-config.ps1')

$pages = @(
    @{ Name = 'card';      Cdx = 'build-output\item-designer.cdx' }  # placeholder, will fix
    @{ Name = 'item';      Cdx = 'build-output\item-designer.cdx' }
    @{ Name = 'character'; Cdx = 'build-output\characterdesigner.cdx' }
    @{ Name = 'setting';   Cdx = 'build-output\settingdesigner.cdx' }
    @{ Name = 'voice';     Cdx = 'build-output\voicestudio.cdx' }
)

# Fix: use correct CDX names
$pages[0].Cdx = 'build-output\carddesigner.cdx'

foreach ($page in $pages) {
    $cdx = Join-Path $Repo $page.Cdx
    $htmlOut = Join-Path $OutDir "$($page.Name).html"

    if (-not (Test-Path $cdx)) {
        Write-Host "SKIP $($page.Name): CDX not found at $cdx" -ForegroundColor Yellow
        continue
    }

    Write-Host "Running $($page.Name)..." -NoNewline -ForegroundColor Cyan

    $tmpOut = Join-Path $env:TEMP "explorer-$($page.Name)-$PID.txt"
    & pwsh -NoProfile -File (Join-Path $Repo 'build\test-run.ps1') `
        -Kernel $cdx -OutFile $tmpOut 2>$null

    if (Test-Path $tmpOut) {
        $content = Get-Content -Raw $tmpOut -ErrorAction SilentlyContinue
        if ($content -and $content.Length -gt 100) {
            $content | Set-Content -Path $htmlOut -Encoding UTF8 -NoNewline
            Write-Host " OK ($($content.Length) bytes)" -ForegroundColor Green
        } else {
            Write-Host " EMPTY output" -ForegroundColor Yellow
        }
        Remove-Item $tmpOut -Force -ErrorAction SilentlyContinue
    } else {
        Write-Host " NO output file" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "Pages built in $OutDir" -ForegroundColor Green
Get-ChildItem $OutDir -Filter '*.html' | ForEach-Object { "  $($_.Name) ($($_.Length) bytes)" }
