# Build plugs/julia/build-output/julia-plug.cdx
[CmdletBinding()]
param([switch]$Force)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..' 'common' 'plug-build-lib.ps1')

Build-TranspilerPlug -PlugDir $PSScriptRoot -PlugName 'julia' -Chapters @('JuliaEmitter', 'JuliaPlug') -Survey 'lower-mul:120000'