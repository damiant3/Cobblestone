# Forwarding to apps/games/server.ps1.
param([int]$Port = 8080)
& (Join-Path $PSScriptRoot '..\apps\games\server.ps1') -Port $Port
