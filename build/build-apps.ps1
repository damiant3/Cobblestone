# build-apps.ps1 -- Build all standalone app pages through the HTML plug
# GENERATED FROM THE CODEX SHELL DSL. Do not edit by hand.
# A hand edit here must NOT be submitted. Change the generator under
# codex/build/, regenerate, and submit the generator and this file
# together. Until then build/check-generated-scripts.ps1 reports this
# file as drifted, and the next regeneration discards the edit.
[CmdletBinding()]
param(
    [string[]]$Only = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$BundleScript = Join-Path $Repo 'build\bundle-app.ps1'
$HtmlRunScript = Join-Path $Repo 'codex\plugs\html\run.ps1'
$PlugCdx = Join-Path $Repo 'codex\plugs\html\build-output\html-plug.cdx'


# Known-bad apps excluded from the build
$Skip = @{}


$apps = @()
foreach ($html in Get-ChildItem (Join-Path $Repo 'apps\*\web\*.html')) {
    $base = $html.BaseName
    $appDir = $html.Directory.Parent
    $pages = @(Get-ChildItem (Join-Path $appDir.FullName '*Page.codex') | Where-Object { ($_.BaseName -replace 'WebPage$|Page$','').ToLower() -eq $base })
    if ($pages.Count -gt 1) {
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


if ($Only.Count -gt 0) {
    $apps = @($apps | Where-Object { $Only -contains $_.Name })
}
if ($apps.Count -eq 0) {
    Write-Error 'no apps discovered'; exit 2
}
Write-Host "discovered $($apps.Count) apps: $(($apps | ForEach-Object { $_.Name }) -join ', ')"


if ((-not (Test-Path -PathType Leaf $PlugCdx))) {
    Write-Host '[build-apps] html plug missing; rebuilding...' -ForegroundColor Yellow
    & pwsh -NoProfile -File (Join-Path $Repo 'codex\plugs\html\build.ps1')
    if (($LASTEXITCODE -ne 0 -or (-not (Test-Path -PathType Leaf $PlugCdx)))) {
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
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[$($app.Name)] FAIL: bundle" -ForegroundColor Red
        $failed++
        continue
    }

    Write-Host "[$($app.Name)] compiling..." -ForegroundColor Cyan
    & pwsh -NoProfile -File $HtmlRunScript -Src $bundled -Out $rendered
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[$($app.Name)] FAIL: compile" -ForegroundColor Red
        $failed++
        continue
    }

    $new = [System.IO.File]::ReadAllText($rendered) -replace "`r`n","`n" -replace "`n","`r`n"
    $cur = if (Test-Path $app.Out) { [System.IO.File]::ReadAllText($app.Out) } else { '' }
    if (($new -eq $cur)) {
        Write-Host "[$($app.Name)] unchanged" -ForegroundColor DarkGray
        continue
    }
    if ((Get-Item $app.Out).IsReadOnly) {
        & p4 edit $app.Out | Out-Null
        if ((Get-Item $app.Out).IsReadOnly) {
            Write-Host "[$($app.Name)] FAIL: p4 edit (still read-only)" -ForegroundColor Red
            $failed++
            continue
        }
    }
    [System.IO.File]::WriteAllText($app.Out, $new, [System.Text.UTF8Encoding]::new($false))
    Write-Host "[$($app.Name)] OK: $($app.Out)" -ForegroundColor Green
}


Write-Host ''
if ($failed -gt 0) {
    Write-Host "$failed app(s) failed" -ForegroundColor Red
    exit 1
} else {
    Write-Host "All $($apps.Count) apps built" -ForegroundColor Green
}
