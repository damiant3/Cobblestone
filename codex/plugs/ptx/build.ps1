param([string]$Survey = '')
. (Join-Path $PSScriptRoot '..' 'common' 'plug-build-lib.ps1')
Build-TranspilerPlug -PlugDir $PSScriptRoot -PlugName 'ptx' -Chapters @('PtxEmitter', 'PtxPlug') -Survey $Survey
