# Build all standalone app pages through the HTML plug.
#
# Apps are discovered by convention, not listed: every artifact
# apps/<dir>/web/<name>.html pairs with the chapter
# apps/<dir>/<Name>Page.codex (or <Name>WebPage.codex) whose name,
# stripped of the Page/WebPage suffix and lowercased, equals <name>.
# Adding an app = adding its Page chapter and an (initially empty)
# web/<name>.html under p4; no script edit.
#
# Regeneration hygiene handled here so no step is manual:
#   - rebuilds the HTML plug if build-output is clean
#   - p4-edits the output artifact before writing
#   - normalizes LF -> CRLF (artifacts are stored CRLF)
#
# Usage: build/build-apps.ps1 [-Only mail,notes]
[CmdletBinding()]
param([string[]]$Only = @())
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$BundleScript = Join-Path $Repo 'build\bundle-app.ps1'
$HtmlRunScript = Join-Path $Repo 'codex\plugs\html\run.ps1'
$PlugCdx = Join-Path $Repo 'codex\plugs\html\build-output\html-plug.cdx'

# Known-bad apps excluded from the build
$Skip = @{}

# -- Discover apps from web artifacts ---------------------------------
$apps = @()
foreach ($html in Get-ChildItem (Join-Path $Repo 'apps\*\web\*.html')) {
    $base = $html.BaseName
    $appDir = $html.Directory.Parent
    $pages = @(Get-ChildItem (Join-Path $appDir.FullName '*Page.codex') | Where-Object {
        ($_.BaseName -replace 'WebPage$|Page$','').ToLower() -eq $base
    })
    if ($pages.Count -gt 1) {
        # <Name>WebPage is the browser build of an app whose <Name>Page is a component
        $pages = @($pages | Where-Object { $_.BaseName -match 'WebPage$' })
    }
    if ($pages.Count -ne 1) {
        Write-Warning "skip $($html.FullName): no unique Page chapter"
        continue
    }
    if ($Skip.ContainsKey($base)) {
        Write-Warning "skip ${base}: $($Skip[$base])"
        continue
    }
    $apps += @{ Name = $base; Src = $pages[0].FullName; Out = $html.FullName }
}
if ($Only.Count -gt 0) { $apps = @($apps | Where-Object { $Only -contains $_.Name }) }
if ($apps.Count -eq 0) { Write-Error 'no apps discovered'; exit 2 }
Write-Host "discovered $($apps.Count) apps: $(($apps | ForEach-Object { $_.Name }) -join ', ')"

# -- Ensure the plug exists -------------------------------------------
if (-not (Test-Path -PathType Leaf $PlugCdx)) {
    Write-Host '[build-apps] html plug missing; rebuilding...' -ForegroundColor Yellow
    & pwsh -NoProfile -File (Join-Path $Repo 'codex\plugs\html\build.ps1')
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -PathType Leaf $PlugCdx)) {
        Write-Error 'html plug rebuild failed'; exit 3
    }
}

$failed = 0
foreach ($app in $apps) {
    $bundleDir = Join-Path $Repo "build-output\apps\$($app.Name)"
    New-Item -ItemType Directory -Force $bundleDir | Out-Null
    $bundled = Join-Path $bundleDir "$($app.Name)-bundle.codex"
    $rendered = Join-Path $bundleDir "$($app.Name)-rendered.html"

    Write-Host "[$($app.Name)] bundling..." -ForegroundColor Cyan
    & pwsh -NoProfile -File $BundleScript -Src $app.Src -Out $bundled
    if ($LASTEXITCODE -ne 0) { Write-Host "[$($app.Name)] FAIL: bundle" -ForegroundColor Red; $failed++; continue }

    Write-Host "[$($app.Name)] compiling..." -ForegroundColor Cyan
    & pwsh -NoProfile -File $HtmlRunScript -Src $bundled -Out $rendered
    if ($LASTEXITCODE -ne 0) { Write-Host "[$($app.Name)] FAIL: compile" -ForegroundColor Red; $failed++; continue }

    $new = [System.IO.File]::ReadAllText($rendered) -replace "`r`n","`n" -replace "`n","`r`n"
    $cur = if (Test-Path $app.Out) { [System.IO.File]::ReadAllText($app.Out) } else { '' }
    if ($new -eq $cur) {
        Write-Host "[$($app.Name)] unchanged" -ForegroundColor DarkGray
        continue
    }
    if ((Get-Item $app.Out).IsReadOnly) {
        & p4 edit $app.Out | Out-Null
        if ((Get-Item $app.Out).IsReadOnly) {
            Write-Host "[$($app.Name)] FAIL: p4 edit (still read-only)" -ForegroundColor Red; $failed++; continue
        }
    }
    [System.IO.File]::WriteAllText($app.Out, $new, [System.Text.UTF8Encoding]::new($false))
    Write-Host "[$($app.Name)] OK: $($app.Out)" -ForegroundColor Green
}

Write-Host ''
if ($failed -gt 0) { Write-Host "$failed app(s) failed" -ForegroundColor Red; exit 1 }
else { Write-Host "All $($apps.Count) apps built" -ForegroundColor Green }
