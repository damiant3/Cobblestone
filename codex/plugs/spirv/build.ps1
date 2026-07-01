param([string]$Survey = '')
. (Join-Path $PSScriptRoot '..' 'common' 'plug-build-lib.ps1')
Build-TranspilerPlug -PlugDir $PSScriptRoot -PlugName 'spirv' -Chapters @('SpirvEmitter', 'SpirvPlug') -Survey $Survey
