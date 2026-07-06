# Boot the packaged USA 250 fireworks scene (frozen v1 snapshot).
# F1 in-app toggles between the classic pixel-art mode and the photoreal mode.
# Press q to quit. Requires -mem 3072 (framebuffer + buffers live near 3GB).
[CmdletBinding()]
param([string]$Cdx = (Join-Path $PSScriptRoot 'fireworks-usa250.cdx'))
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
Get-Process codex-vm -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300
& (Join-Path $repo 'tools\codex-vm.exe') -kernel $Cdx -mem 3072 -gop-width 1024 -gop-height 768
