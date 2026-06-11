# Build plugs/winforms/build-output/winforms-plug.cdx
[CmdletBinding()]
param([switch]$Force)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..' 'common' 'plug-build-lib.ps1')

Build-TranspilerPlug -PlugDir $PSScriptRoot -PlugName 'winforms' -Chapters @('WinFormsEmitter', 'WinFormsPlug')