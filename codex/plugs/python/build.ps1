# Build plugs/python/build-output/python-plug.cdx
[CmdletBinding()]
param([switch]$Force)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..' 'common' 'plug-build-lib.ps1')

Build-TranspilerPlug -PlugDir $PSScriptRoot -PlugName 'python' -Chapters @('PythonEmitter', 'PythonPlug') -Survey 'lower-mul:120000'
