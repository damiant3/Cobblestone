param(
    [string]$Out = "build/output/guios.cdx",
    [string]$Log = "build/output/guios.log"
)

$ErrorActionPreference = 'Stop'

$outDir = Split-Path $Out -Parent
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }

Write-Host "Compiling GuiOS bare-metal shell..."
pwsh build/compile.ps1 -Src apps/cvmm/GuiOpening.codex -Out $Out -Log $Log
