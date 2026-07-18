param([string]$Survey = '')
. (Join-Path $PSScriptRoot '..' 'common' 'plug-build-lib.ps1')
Build-TranspilerPlug -PlugDir $PSScriptRoot -PlugName 'spirv' -Chapters @('SpirvBinary', 'SpirvEmitBin', 'SpirvPlug') -Survey $Survey
