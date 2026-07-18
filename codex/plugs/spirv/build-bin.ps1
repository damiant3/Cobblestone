# Build the SPIR-V binary plug (SpirvBinary + SpirvEmitBin + SpirvBinPlug)
# into build-output/spirvbin-plug.cdx. Separate from the text plug (build.ps1)
# so neither disturbs the other.
param([string]$Survey = '')
. (Join-Path $PSScriptRoot '..' 'common' 'plug-build-lib.ps1')
Build-TranspilerPlug -PlugDir $PSScriptRoot -PlugName 'spirvbin' -Chapters @('SpirvBinary', 'SpirvEmitBin', 'SpirvBinPlug') -Survey $Survey
