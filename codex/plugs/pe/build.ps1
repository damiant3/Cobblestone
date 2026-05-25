# Build plugs/pe/build-output/pe-plug.cdx
# Bundles: ByteHelpers + PeWriter + PePlug
[CmdletBinding()]
param([switch]$Force)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..' 'common' 'plug-build-lib.ps1')

$Repo      = $script:PlugBuildRepo
$PlugDir   = (Resolve-Path $PSScriptRoot).Path
$OutDir    = Join-Path $PlugDir 'build-output'
$OutFile   = Join-Path $OutDir 'pe-plug.cdx'
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
Add-Chapter -Path (Join-Path $PlugDir 'PeWriter.codex')
Add-Chapter -Path (Join-Path $PlugDir 'PePlug.codex')

$preLines = Resolve-PlugForewords $lines
$body = (($preLines + $lines) -join "`n") + "`n"
[System.IO.File]::WriteAllText($BundleSrc, $body, [System.Text.UTF8Encoding]::new($false))
Write-Host "[pe-plug] bundled $($preLines.Count + $lines.Count) lines, $($body.Length) bytes"

Build-PlugCdx -BundleSrc $BundleSrc -OutFile $OutFile -LogFile $LogFile -PlugName 'pe-plug'
