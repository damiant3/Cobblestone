# The hosted plug: the ring plug's chapter set, a different body.
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'bundle_ringplug.ps1') `
    -Body 'ZigEmitHosted.codex' `
    -OutName 'zigemit-source.codex' `
    -PlugName 'zigemit'
