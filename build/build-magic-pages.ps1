# Build CodexMagic web pages from .codex sources through the HTML plug.
# Each page .codex file is bundled with its dependencies, compiled to IR,
# then run through the HTML plug to produce a standalone HTML file.
[CmdletBinding()]
param([string[]]$Pages = @())

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$BundleScript = Join-Path $Repo 'build\bundle-app.ps1'
$HtmlRunScript = Join-Path $Repo 'codex\plugs\html\run.ps1'
$AppDir = Join-Path $Repo 'apps\games\codexmagic'
$WebDir = Join-Path $AppDir 'web'
$OutDir = Join-Path $WebDir 'build-output'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

if ($Pages.Count -eq 0) {
    $Pages = @(Get-ChildItem -Path $AppDir -Filter '*Page.codex' | ForEach-Object { $_.BaseName })
}

foreach ($page in $Pages) {
    $src = Join-Path $AppDir "$page.codex"
    if (-not (Test-Path $src)) { Write-Host "SKIP: $src not found" -ForegroundColor Yellow; continue }
    $bundled = Join-Path $OutDir "$page-bundle.codex"
    $htmlOut = Join-Path $WebDir (($page -replace 'Page$','').ToLower() + '.html')

    Write-Host "[$page] bundling..." -ForegroundColor Cyan
    & pwsh -NoProfile -File $BundleScript -Src $src -Out $bundled
    if ($LASTEXITCODE -ne 0) { Write-Host "[$page] FAIL: bundle" -ForegroundColor Red; continue }

    Write-Host "[$page] compiling to HTML..." -ForegroundColor Cyan
    & pwsh -NoProfile -File $HtmlRunScript -Src $bundled -Out $htmlOut
    if ($LASTEXITCODE -ne 0) { Write-Host "[$page] FAIL: HTML generation" -ForegroundColor Red; continue }

    # Post-process: inject external scripts (auth, card rendering, shared CSS)
    $html = [System.IO.File]::ReadAllText($htmlOut)
    $inject = '<link rel="stylesheet" href="/magic.css">'
    $inject += '<script src="/magic.js"></script>'
    $inject += '<script src="/card-render.js"></script>'
    $html = $html -replace '</head>', "$inject</head>"
    # Add auth bootstrap after the compiled script
    $authBoot = '<script>document.addEventListener("DOMContentLoaded",function(){if(typeof ensureAccount==="function")ensureAccount()});</script>'
    $html = $html -replace '</body>', "$authBoot</body>"
    [System.IO.File]::WriteAllText($htmlOut, $html, [System.Text.Encoding]::UTF8)

    Write-Host "[$page] OK: $htmlOut" -ForegroundColor Green
}
