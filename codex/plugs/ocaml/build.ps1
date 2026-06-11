# Build plugs/ocaml/build-output/ocaml-plug.cdx
[CmdletBinding()]
param([switch]$Force)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..' 'common' 'plug-build-lib.ps1')

Build-TranspilerPlug -PlugDir $PSScriptRoot -PlugName 'ocaml' -Chapters @('OCamlEmitter', 'OCamlPlug')