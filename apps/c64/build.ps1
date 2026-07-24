param(
    [string]$Out = "build/output/c64.cdx",
    [string]$Log = "build/output/c64.log"
)
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '../..')

$outDir = Split-Path $Out -Parent
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }

# Compile the entry chapter directly and let compile.ps1 resolve every `cites`
# through the quire map. Hand-concatenating the chapters here double-included
# each one the C64 quire also resolves, which halts with CDX3001 (duplicate
# type) the moment the quire is registered -- the same dead-build trap circuits
# had. The entry chapter is the source of truth for the chapter set.
Write-Host "Compiling..."
pwsh build/compile.ps1 -Src apps/c64/opening.codex -Out $Out -Log $Log
