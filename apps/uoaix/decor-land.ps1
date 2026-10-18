[CmdletBinding()]
param([Parameter(Mandatory)][string]$ClientRoot,[Parameter(Mandatory)][string]$OutDir)
# The land bake's client half (UoaixDecorator.md item 6): writes the client's MAP0.MUL with land.cfg's edits. MAP0 is
# 393216 blocks of 196 bytes in x-major block order (block (x/8)*512 + y/8): a 4-byte header, then 64 cells of land art
# (2 bytes) and z (signed byte), row-major within the block. The input is the client's own MAP0.MUL, so the result
# depends only on land.cfg; an edit leaves the client at the next run after it leaves land.cfg.
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $ClientRoot).Path
$OutDir=[IO.Path]::GetFullPath($OutDir)
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if($OutDir.StartsWith($repo+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or $OutDir -eq $repo){throw 'Client files stay outside tracked source, shipped images and published artifacts (ruling R1)'}
$source=Join-Path $root 'MAP0.MUL'
$map=[IO.File]::ReadAllBytes($source)
if($map.Length -ne 77070336){throw 'Legacy MAP0.MUL extent required'}
$edits=0;$lineNo=0
foreach($line in [IO.File]::ReadLines((Join-Path $PSScriptRoot 'land.cfg'))){
    $lineNo++;$text=$line.Trim()
    if($text.Length -eq 0 -or $text.StartsWith('#')){continue}
    if($text -notmatch '^(\d+)\s+(\d+)\s+(?:0x([0-9A-Fa-f]+)|keep)\s+(-?\d+)(?:\s|$)'){throw "land.cfg:$lineNo unparsed"}
    $x=[int]$Matches[1];$y=[int]$Matches[2];$art=if($Matches[3]){[Convert]::ToInt32($Matches[3],16)}else{-1};$z=[int]$Matches[4]
    if($x -ge 6144 -or $y -ge 4096 -or $art -ge 16384 -or $z -lt -128 -or $z -gt 127){throw "land.cfg:$lineNo outside the legacy map"}
    $at=([math]::Floor($x/8)*512+[math]::Floor($y/8))*196+4+(($y%8)*8+$x%8)*3
    if($art -ge 0){$map[$at]=[byte]($art -band 0xFF);$map[$at+1]=[byte]($art -shr 8)};$map[$at+2]=[byte]($z -band 0xFF);$edits++
}
New-Item -ItemType Directory -Force -Path $OutDir|Out-Null
$out=Join-Path $OutDir 'MAP0.MUL'
[IO.File]::WriteAllBytes($out,$map)
$receipt=[ordered]@{clientRoot=$root;outDir=$OutDir;landEdits=$edits;sourceSha256=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash;landCfgSha256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'land.cfg') -Algorithm SHA256).Hash;mapSha256=(Get-FileHash -LiteralPath $out -Algorithm SHA256).Hash;localOnly=$true}
$receipt|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $OutDir 'decor-land.json')
Write-Output "LAND edits=$edits $OutDir"
