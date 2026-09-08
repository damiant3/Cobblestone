# Build CodexMagic web pages from .codex source through the HTML plug.
# Usage: apps/games/codexmagic/web/build-pages.ps1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$WebDir  = (Resolve-Path $PSScriptRoot).Path
$Repo    = (Resolve-Path (Join-Path $WebDir '..\..\..\..')).Path
$PlugRun = Join-Path $Repo 'codex\plugs\html\run.ps1'
$PlugCdx = Join-Path $Repo 'codex\plugs\html\build-output\html-plug.cdx'

if (-not (Test-Path $PlugCdx)) {
    Write-Host "HTML plug CDX not built. Building..." -ForegroundColor Yellow
    & pwsh -NoProfile -File (Join-Path $Repo 'codex\plugs\html\build.ps1')
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAILED to build HTML plug." -ForegroundColor Red
        exit 1
    }
}

$pages = @(
    @{ Name = 'admin';       Codex = 'AdminApp.codex';       Theme = 'MagicTheme.codex' }
    @{ Name = 'marketplace'; Codex = 'MarketplaceApp.codex'; Theme = 'MagicTheme.codex' }
)

foreach ($page in $pages) {
    $themeFile = Join-Path $WebDir $page.Theme
    $appFile   = Join-Path $WebDir $page.Codex
    $outFile   = Join-Path $WebDir "$($page.Name).html"

    if (-not (Test-Path $appFile)) {
        Write-Host "SKIP $($page.Name): $($page.Codex) not found" -ForegroundColor Yellow
        continue
    }

    Write-Host "Building $($page.Name)..." -NoNewline -ForegroundColor Cyan

    $bundleFile = Join-Path $WebDir "build-output\$($page.Name)-bundle.codex"
    New-Item -ItemType Directory -Force (Join-Path $WebDir 'build-output') | Out-Null

    $lines = [System.Collections.Generic.List[string]]::new()
    if (Test-Path $themeFile) {
        $renamed = $false
        foreach ($l in [System.IO.File]::ReadAllLines($themeFile)) {
            if ((-not $renamed) -and $l -match '^Chapter:\s*(.+?)\s*$') {
                $lines.Add("Chapter: CodexMagic--$($matches[1])")
                $renamed = $true
            } else { $lines.Add($l) }
        }
        $lines.Add(''); $lines.Add('')
    }
    $renamed = $false
    foreach ($l in [System.IO.File]::ReadAllLines($appFile)) {
        if ((-not $renamed) -and $l -match '^Chapter:\s*(.+?)\s*$') {
            $lines.Add("Chapter: CodexMagic--$($matches[1])")
            $renamed = $true
        } else { $lines.Add($l) }
    }

    $body = ($lines -join "`n") + "`n"
    [System.IO.File]::WriteAllText($bundleFile, $body, [System.Text.UTF8Encoding]::new($false))

    & pwsh -NoProfile -File $PlugRun -Src $bundleFile -Out $outFile
    if ($LASTEXITCODE -ne 0) {
        Write-Host " FAILED" -ForegroundColor Red
    } else {
        $sz = if (Test-Path $outFile) { (Get-Item $outFile).Length } else { 0 }
        Write-Host " OK ($sz bytes)" -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "Pages built in $WebDir" -ForegroundColor Green
Get-ChildItem $WebDir -Filter '*.html' | ForEach-Object { "  $($_.Name) ($($_.Length) bytes)" }
