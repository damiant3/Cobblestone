# Build plugs/arm64/build-output/arm64-plug.cdx
[CmdletBinding()]
param([switch]$Force)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..' 'common' 'plug-build-lib.ps1')

Build-TranspilerPlug -PlugDir $PSScriptRoot -PlugName 'arm64' -Chapters @('Arm64Runtime', 'Arm64CodeGen', 'Arm64Plug') -Survey 'lower-mul:60000'
