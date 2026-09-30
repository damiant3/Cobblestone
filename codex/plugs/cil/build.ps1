[CmdletBinding()]
param([string]$Kernel = '')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
if (-not $Kernel) { $Kernel = Join-Path $repo 'seed/Codex.cdx' }
& pwsh -NoProfile -File (Join-Path $repo 'codex/plugs/common/build-plug-wasm.ps1') -Plug cil -Transport irbytes -Chapters 'ByteHelpers,CilMetadata,CilPe,CilTypes,CilFunctions,CilUnity,CilEmitter,CilStdio' -Kernel $Kernel
exit $LASTEXITCODE
