# Build plugs/recheck/build-output/recheck-plug.cdx
[CmdletBinding()]
param([switch]$Force)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# The builtin name list is generated from codex/compiler/Types/Builtins.codex.
# A stale list would report a real builtin as an unbound name, so the drift
# check runs before the compile rather than after.
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'gen-builtins.ps1') -Check
if ($LASTEXITCODE -ne 0) { exit 1 }

. (Join-Path $PSScriptRoot '..' 'common' 'plug-build-lib.ps1')

Build-TranspilerPlug -PlugDir $PSScriptRoot -PlugName 'recheck' `
    -Chapters @('RecheckBuiltins', 'RecheckCore', 'RecheckWellFormed', 'RecheckBounds', 'RecheckEffects', 'RecheckPlug')
