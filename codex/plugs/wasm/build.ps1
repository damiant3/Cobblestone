# Build plugs/wasm/build-output/wasm-plug.cdx
[CmdletBinding()]
param([switch]$Force)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..' 'common' 'plug-build-lib.ps1')

Build-TranspilerPlug -PlugDir $PSScriptRoot -PlugName 'wasm' -Chapters @('WasmEmitter', 'WasmPlug') -Survey 'lower-mul:120000'
