# Build plugs/riscv/build-output/riscv-plug.cdx
[CmdletBinding()]
param([switch]$Force)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..' 'common' 'plug-build-lib.ps1')

Build-TranspilerPlug -PlugDir $PSScriptRoot -PlugName 'riscv' -Chapters @('RiscVRuntime', 'RiscVCodeGen', 'RiscVCodeGen2', 'RiscVLir', 'RiscVCodeGen3', 'RiscVDisasm', 'RiscVPlug') -Survey 'lower-mul:120000' -WithLir -CommonChapters @('PlugManifest')
