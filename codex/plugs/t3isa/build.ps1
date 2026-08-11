param([string]$Survey = '')
. (Join-Path $PSScriptRoot '..' 'common' 'plug-build-lib.ps1')
Build-TranspilerPlug -PlugDir $PSScriptRoot -PlugName 't3isa' -Chapters @('T3IsaEncode', 'T3IsaEmitter', 'T3IsaPlug')
