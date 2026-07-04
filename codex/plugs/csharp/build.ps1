# Build plugs/csharp/build-output/csharp-plug.cdx
[CmdletBinding()]
param([switch]$Force)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..' 'common' 'plug-build-lib.ps1')

Build-TranspilerPlug -PlugDir $PSScriptRoot -PlugName 'csharp' -Chapters @('CsAst', 'CSharpEmitter', 'CSharpEmitterExpressions', 'CSharpPlug')
