# Regenerate index.html from ModBuilderPage.codex through the html plug.
# index.html is build output: edit the chapter, then run this.
[CmdletBinding()]
param([string]$Kernel)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Site = (Resolve-Path $PSScriptRoot).Path
$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
if (-not $Kernel) { $Kernel = Join-Path $Repo 'seed\Codex.cdx' }
if (-not (Test-Path -PathType Leaf $Kernel)) { Write-Host "REFUSE: no kernel at $Kernel"; exit 2 }

& pwsh -NoProfile -File (Join-Path $Repo 'codex\plugs\html\run.ps1') `
    -Src (Join-Path $Site 'ModBuilderPage.codex') `
    -Out (Join-Path $Site 'index.html') `
    -Compiler $Kernel
if ($LASTEXITCODE -ne 0) { Write-Host '[modbuilder-site] FAIL: page generation'; exit 3 }
