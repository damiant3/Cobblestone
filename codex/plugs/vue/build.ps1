# Build plugs/vue/build-output/vue-plug.cdx
[CmdletBinding()]
param([switch]$Force)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..' 'common' 'plug-build-lib.ps1')

Build-TranspilerPlug -PlugDir $PSScriptRoot -PlugName 'vue' -Chapters @('VueEmitter', 'VuePlug')