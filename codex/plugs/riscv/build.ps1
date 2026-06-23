# Build plugs/riscv/build-output/riscv-plug.cdx
[CmdletBinding()]
param([switch]$Force)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..' 'common' 'plug-build-lib.ps1')

Build-TranspilerPlug -PlugDir $PSScriptRoot -PlugName 'riscv' -Chapters @('RiscVRuntime', 'RiscVCodeGen', 'RiscVPlug') -Survey 'lower-mul:80000'
