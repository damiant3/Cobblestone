# Moved to tools/web/server.ps1 — forwarding.
param([int]$Port = 8080)
& (Join-Path $PSScriptRoot 'web\server.ps1') -Port $Port
