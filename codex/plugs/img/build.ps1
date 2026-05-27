# Build plugs/img/build-output/img-plug.cdx
# Bundles: ByteHelpers + PlugChain + GptWriter + Fat32Writer + Fat16Writer + ImgPlug
[CmdletBinding()]
param([switch]$Force)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..' 'common' 'plug-build-lib.ps1')

$Repo      = $script:PlugBuildRepo
$PlugDir   = (Resolve-Path $PSScriptRoot).Path
$OutDir    = Join-Path $PlugDir 'build-output'
$OutFile   = Join-Path $OutDir 'img-plug.cdx'
$BundleSrc = Join-Path $OutDir 'plug-source.codex'
$LogFile   = Join-Path $OutDir 'build.log'

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$lines = [System.Collections.Generic.List[string]]::new()

function Add-Chapter {
    param([string]$Path)
    foreach ($l in [System.IO.File]::ReadAllLines($Path)) { $lines.Add($l) }
    $lines.Add(''); $lines.Add('')
}

Add-Chapter -Path (Join-Path $Repo 'codex\plugs\common\ByteHelpers.codex')
Add-Chapter -Path (Join-Path $Repo 'codex\plugs\common\PlugChain.codex')
Add-Chapter -Path (Join-Path $PlugDir 'GptWriter.codex')
Add-Chapter -Path (Join-Path $PlugDir 'Fat32Writer.codex')
Add-Chapter -Path (Join-Path $PlugDir 'Fat16Writer.codex')
Add-Chapter -Path (Join-Path $PlugDir 'ImgPlug.codex')

$preLines = Resolve-PlugForewords $lines
$body = (($preLines + $lines) -join "`n") + "`n"
[System.IO.File]::WriteAllText($BundleSrc, $body, [System.Text.UTF8Encoding]::new($false))
Write-Host "[img-plug] bundled $($preLines.Count + $lines.Count) lines, $($body.Length) bytes"

Build-PlugCdx -BundleSrc $BundleSrc -OutFile $OutFile -LogFile $LogFile -PlugName 'img-plug'
