# Build plugs/elf/build-output/elf-plug.cdx
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
Add-PlugChapter -Lines $lines -Path (Join-Path $Repo 'codex\plugs\common\PlugChain.codex')
Add-PlugChapter -Lines $lines -Path (Join-Path $PlugDir 'ElfWriter.codex')
Add-PlugChapter -Lines $lines -Path (Join-Path $PlugDir 'DwarfWriter.codex')
Add-PlugChapter -Lines $lines -Path (Join-Path $PlugDir 'ElfPlug.codex')

$preLines = Resolve-PlugForewords $lines
$bundleSrc = Join-Path $OutDir 'plug-source.codex'
Bundle-PlugSource -PreLines $preLines -Lines $lines -BundleSrc $bundleSrc -PlugName 'elf-plug'
Build-PlugCdx -BundleSrc $bundleSrc -OutFile (Join-Path $OutDir 'elf-plug.cdx') -LogFile (Join-Path $OutDir 'build.log') -PlugName 'elf-plug'
