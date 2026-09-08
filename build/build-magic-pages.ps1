# build-magic-pages.ps1 -- Build CodexMagic web pages from .codex sources through the HTML plug
# GENERATED FROM THE CODEX SHELL DSL. Do not edit by hand.
# A hand edit here must NOT be submitted. Change the generator under
# codex/build/, regenerate, and submit the generator and this file
# together. Until then build/check-generated-scripts.ps1 reports this
# file as drifted, and the next regeneration discards the edit.
[CmdletBinding()]
param(
    [string[]]$Pages = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$BundleScript = (Join-Path $Repo 'build\bundle-app.ps1')
$HtmlRunScript = (Join-Path $Repo 'codex\plugs\html\run.ps1')
$AppDir = (Join-Path $Repo 'apps\games\codexmagic')
$WebDir = (Join-Path $AppDir 'web')
$OutDir = (Join-Path $WebDir 'build-output')
New-Item -ItemType Directory -Force $OutDir | Out-Null


if (($Pages.Count -eq 0)) {
    $Pages = (Get-ChildItem $AppDir -Filter '*Page.codex' -File | ForEach-Object { $_.BaseName })
}


foreach ($page in $Pages) {
    $src = (Join-Path $AppDir ([string]$page + '.codex'))
    $bundled = (Join-Path $OutDir ([string]$page + '-bundle.codex'))
    $htmlOut = (Join-Path $WebDir ([string]($page -replace 'Page$', '').ToLower() + '.html'))

    if ((-not (Test-Path -PathType Leaf $src))) {
        Write-Host ([string]([string]'SKIP: ' + $src) + ' not found') -ForegroundColor Yellow
        continue
    }

    Write-Host ([string]([string]([string]'[' + $page) + '] ') + 'bundling...') -ForegroundColor Cyan
    & 'pwsh' -NoProfile -File $BundleScript -Src $src -Out $bundled
    if ((-not ($LASTEXITCODE -eq 0))) {
        Write-Host ([string]([string]([string]'[' + $page) + '] ') + 'FAIL: bundle') -ForegroundColor Red
        continue
    }

    Write-Host ([string]([string]([string]'[' + $page) + '] ') + 'compiling to HTML...') -ForegroundColor Cyan
    & 'pwsh' -NoProfile -File $HtmlRunScript -Src $bundled -Out $htmlOut
    if ((-not ($LASTEXITCODE -eq 0))) {
        Write-Host ([string]([string]([string]'[' + $page) + '] ') + 'FAIL: HTML generation') -ForegroundColor Red
        continue
    }


    $html = [System.IO.File]::ReadAllText($htmlOut)
    $inject = '<link rel="stylesheet" href="/magic.css">'
    $inject += '<script src="/magic.js"></script>'
    $inject += '<script src="/card-render.js"></script>'

    $html = ($html -replace '</head>', ([string]$inject + '</head>'))
    $html = ($html -replace '</body>', ([string]'<script>document.addEventListener("DOMContentLoaded",function(){if(typeof ensureAccount==="function")ensureAccount()});</script>' + '</body>'))
    [System.IO.File]::WriteAllText($htmlOut, $html, ([System.Text.Encoding]::UTF8))
    Write-Host ([string]([string]([string]([string]'[' + $page) + '] ') + 'OK: ') + $htmlOut) -ForegroundColor Green

}
