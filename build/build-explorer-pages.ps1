# build-explorer-pages.ps1 -- Build explorer HTML pages from compiled CDX binaries
# Generated from Codex Shell DSL. Do not edit by hand.
[CmdletBinding()]
param(
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$OutDir = 'D:\Projects\CodexMagic\explorer\pages'
New-Item -ItemType Directory -Force $OutDir | Out-Null

. (Join-Path $Repo 'build\vm-config.ps1')


$pages = @(@{ Name = 'card'; Cdx = 'build-output\carddesigner.cdx' }, @{ Name = 'item'; Cdx = 'build-output\item-designer.cdx' }, @{ Name = 'character'; Cdx = 'build-output\characterdesigner.cdx' }, @{ Name = 'setting'; Cdx = 'build-output\settingdesigner.cdx' }, @{ Name = 'voice'; Cdx = 'build-output\voicestudio.cdx' })


foreach ($page in $pages) {
    $cdx = (Join-Path $Repo $page.Cdx)
    $htmlOut = (Join-Path $OutDir ([string]$page.Name + '.html'))
    if ((-not (Test-Path -PathType Leaf $cdx))) {
        Write-Host ([string]([string]([string]'SKIP ' + $page.Name) + ': CDX not found at ') + $cdx) -ForegroundColor Yellow
        continue
    }
    Write-Host ([string]([string]'Running ' + $page.Name) + '...') -NoNewline -ForegroundColor Cyan
    $tmpOut = (Join-Path $env:TEMP ([string]([string]([string]([string]'explorer-' + $page.Name) + '-') + $PID) + '.txt'))
    & 'pwsh' -NoProfile -File (Join-Path $Repo 'build\test-run.ps1') -Kernel $cdx -OutFile $tmpOut 2>$null

    if ((Test-Path -PathType Leaf $tmpOut)) {
        $content = (Get-Content $tmpOut -Raw -ErrorAction SilentlyContinue)
        if (($content -and ($content.Length -gt 100))) {
            Set-Content -Path $htmlOut -Value $content -Encoding UTF8
            Write-Host ([string]([string]' OK (' + $content.Length) + ' bytes)') -ForegroundColor Green
        } else {
            Write-Host ' EMPTY output' -ForegroundColor Yellow
        }
        Remove-Item -Force -ErrorAction SilentlyContinue $tmpOut
    } else {
        Write-Host ' NO output file' -ForegroundColor Red
    }
}


Write-Host ''
Write-Host ([string]'Pages built in ' + $OutDir) -ForegroundColor Green
Get-ChildItem $OutDir -Filter '*.html' -File | ForEach-Object {
    Write-Host ([string]([string]([string]([string]'  ' + $_.Name) + ' (') + $_.Length) + ' bytes)')
}
