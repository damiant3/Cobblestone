# Build plugs/babbage/build-output/babbage-plug.cdx
[CmdletBinding()]
param([switch]$Force)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..' 'common' 'plug-build-lib.ps1')

Build-TranspilerPlug -PlugDir $PSScriptRoot -PlugName 'babbage' -Chapters @('BabbageEmitter', 'BabbagePlug')