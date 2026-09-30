[CmdletBinding()]
param([string]$Kernel = '')
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$out=Join-Path $repo 'build-output/native-deploy'
[void](New-Item -ItemType Directory -Force -Path $out)
$stage=Join-Path $out ('loader-'+[guid]::NewGuid().ToString('N'))
$placeholder=Join-Path $out 'empty-payload.bin'
[IO.File]::WriteAllBytes($placeholder,[byte[]]@(0))
if(-not $Kernel){$Kernel=Join-Path $repo 'seed/Codex.cdx'}
& pwsh -NoProfile -File (Join-Path $repo 'codex/plugs/unity/package.ps1') -ManagedLibrary $placeholder -OutDirectory $stage -Kernel $Kernel
if($LASTEXITCODE -ne 0){throw 'Publisher loader build failed'}
Copy-Item -LiteralPath (Join-Path $stage 'winhttp.dll') -Destination (Join-Path $out 'winhttp-template.dll') -Force
Write-Host "Loader template: $(Join-Path $out 'winhttp-template.dll')"
