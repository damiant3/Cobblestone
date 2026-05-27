# Build explorer pages: .codex -> JS (via plug) + .codex -> HTML (via CDX).
# Usage: build/build-explorer.ps1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$OutDir = Join-Path $Repo 'build-output\explorer'
$PagesDir = 'D:\Projects\CodexMagic\explorer\pages'
New-Item -ItemType Directory -Force $OutDir | Out-Null
New-Item -ItemType Directory -Force $PagesDir | Out-Null

$compileScript = Join-Path $Repo 'build\compile.ps1'
$jsRunScript = Join-Path $Repo 'codex\plugs\javascript\run.ps1'
$testRunScript = Join-Path $Repo 'build\test-run.ps1'

$PlugCdx = Join-Path $Repo 'codex\plugs\javascript\build-output\javascript-plug.cdx'
if (-not (Test-Path -PathType Leaf $PlugCdx)) {
    Write-Host "JS plug not built. Building..." -ForegroundColor Yellow
    & pwsh -NoProfile -File (Join-Path $Repo 'codex\plugs\javascript\build.ps1')
    if ($LASTEXITCODE -ne 0) { Write-Host "FAIL: plug build"; exit 1 }
}

# ── Item Designer ────────────────────────────────────────────────────

Write-Host ""
Write-Host "=== Item Designer ===" -ForegroundColor Cyan

# Step 1: Compile ItemDesignerApp.codex -> item-app.js (via JS plug)
Write-Host "  [1/3] App -> JS..." -NoNewline
$appSrc = Join-Path $Repo 'apps\works\explorer\ItemDesignerApp.codex'
$appJs = Join-Path $OutDir 'item-app.js'
& pwsh -NoProfile -File $jsRunScript -Src $appSrc -Out $appJs
if ($LASTEXITCODE -ne 0) {
    Write-Host " FAIL" -ForegroundColor Red; exit 2
}
Write-Host " OK ($((Get-Item $appJs).Length) bytes)" -ForegroundColor Green

# Step 2: Compile ItemDesignerPage.codex -> CDX -> HTML
Write-Host "  [2/3] Page -> HTML..." -NoNewline
$pageSrc = Join-Path $Repo 'apps\works\explorer\ItemDesignerPage.codex'
$pageCdx = Join-Path $OutDir 'item-page.cdx'
$pageLog = Join-Path $OutDir 'item-page.log'
& pwsh -NoProfile -File $compileScript -Src $pageSrc -Out $pageCdx -Log $pageLog
if ($LASTEXITCODE -ne 0) {
    Write-Host " FAIL (compile)" -ForegroundColor Red
    if (Test-Path $pageLog) { Get-Content $pageLog | Select-Object -First 5 }
    exit 3
}
$pageHtml = Join-Path $OutDir 'item.html'
& pwsh -NoProfile -File $testRunScript -Kernel $pageCdx -OutFile $pageHtml
if (-not (Test-Path $pageHtml) -or (Get-Item $pageHtml).Length -lt 100) {
    Write-Host " FAIL (run)" -ForegroundColor Red; exit 4
}
Write-Host " OK ($((Get-Item $pageHtml).Length) bytes)" -ForegroundColor Green

# Step 3: Copy to pages directory
Write-Host "  [3/3] Deploy..." -NoNewline
Copy-Item $appJs (Join-Path $PagesDir 'item-app.js') -Force
Copy-Item $pageHtml (Join-Path $PagesDir 'item.html') -Force
Copy-Item (Join-Path $Repo 'tools\web\explorer\item-ui.js') (Join-Path $PagesDir 'item-ui.js') -Force
Write-Host " OK" -ForegroundColor Green

# ── Card Designer ─────────────────────────────────────────────────────

Write-Host ""
Write-Host "=== Card Designer ===" -ForegroundColor Cyan

Write-Host "  [1/3] App -> JS..." -NoNewline
$cardAppSrc = Join-Path $Repo 'apps\works\explorer\CardDesignerApp.codex'
$cardAppJs = Join-Path $OutDir 'card-app.js'
& pwsh -NoProfile -File $jsRunScript -Src $cardAppSrc -Out $cardAppJs
if ($LASTEXITCODE -ne 0) {
    Write-Host " FAIL" -ForegroundColor Red; exit 2
}
Write-Host " OK ($((Get-Item $cardAppJs).Length) bytes)" -ForegroundColor Green

Write-Host "  [2/3] Page -> HTML..." -NoNewline
$cardPageSrc = Join-Path $Repo 'apps\works\explorer\CardDesignerPage.codex'
$cardPageCdx = Join-Path $OutDir 'card-page.cdx'
$cardPageLog = Join-Path $OutDir 'card-page.log'
& pwsh -NoProfile -File $compileScript -Src $cardPageSrc -Out $cardPageCdx -Log $cardPageLog
if ($LASTEXITCODE -ne 0) {
    Write-Host " FAIL (compile)" -ForegroundColor Red
    if (Test-Path $cardPageLog) { Get-Content $cardPageLog | Select-Object -First 5 }
    exit 3
}
$cardPageHtml = Join-Path $OutDir 'card.html'
& pwsh -NoProfile -File $testRunScript -Kernel $cardPageCdx -OutFile $cardPageHtml
if (-not (Test-Path $cardPageHtml) -or (Get-Item $cardPageHtml).Length -lt 100) {
    Write-Host " FAIL (run)" -ForegroundColor Red; exit 4
}
Write-Host " OK ($((Get-Item $cardPageHtml).Length) bytes)" -ForegroundColor Green

Write-Host "  [3/3] Deploy..." -NoNewline
Copy-Item $cardAppJs (Join-Path $PagesDir 'card-app.js') -Force
Copy-Item $cardPageHtml (Join-Path $PagesDir 'card.html') -Force
Copy-Item (Join-Path $Repo 'tools\web\explorer\card-ui.js') (Join-Path $PagesDir 'card-ui.js') -Force
Write-Host " OK" -ForegroundColor Green

# ── Summary ──────────────────────────────────────────────────────────
Write-Host ""
Write-Host "Built:" -ForegroundColor Green
Get-ChildItem $PagesDir | ForEach-Object { "  $($_.Name) ($($_.Length) bytes)" }
