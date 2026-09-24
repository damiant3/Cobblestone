[CmdletBinding()]
param([string]$Kernel = '')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
if (-not $Kernel) { $Kernel = Join-Path $repo 'seed/Codex.cdx' }
& pwsh -NoProfile -File (Join-Path $repo 'codex/plugs/wasm/build-page-modules.ps1') -Only unity -Jobs 1 -Kernel $Kernel
exit $LASTEXITCODE
