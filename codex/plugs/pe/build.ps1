# Build plugs/pe/build-output/pe-plug.cdx
[CmdletBinding()]
param([switch]$Force)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..' 'common' 'plug-build-lib.ps1')

$Repo    = $script:PlugBuildRepo
$PlugDir = $PSScriptRoot
$OutDir  = Join-Path $PlugDir 'build-output'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$lines = [System.Collections.Generic.List[string]]::new()
Add-PlugChapter -Lines $lines -Path (Join-Path $Repo 'codex\plugs\common\ByteHelpers.codex')
Add-PlugChapter -Lines $lines -Path (Join-Path $PlugDir 'PeWriter.codex')
Add-PlugChapter -Lines $lines -Path (Join-Path $PlugDir 'PePlug.codex')

$preLines = Resolve-PlugForewords $lines
$bundleSrc = Join-Path $OutDir 'plug-source.codex'
Bundle-PlugSource -PreLines $preLines -Lines $lines -BundleSrc $bundleSrc -PlugName 'pe-plug'
Build-PlugCdx -BundleSrc $bundleSrc -OutFile (Join-Path $OutDir 'pe-plug.cdx') -LogFile (Join-Path $OutDir 'build.log') -PlugName 'pe-plug'
