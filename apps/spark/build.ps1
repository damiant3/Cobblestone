# Build a Spark entry point to CDX.
#
# No chapter list and no concat: Spark chapters cite each other by name
# (`cites Spark chapter Mesh`), the quire map points Spark at apps/spark, and
# compile.ps1 resolves the transitive closure itself and prepends it in
# dependency order. Concatenating the closure by hand duplicates exactly what
# compile.ps1 already pulls in, which surfaces as a wall of CDX3001 duplicate
# type definitions.
#
#   apps/spark/build.ps1                       # SparkApp -> build/output/spark.cdx
#   apps/spark/build.ps1 -Entry SparkGfxDemo
param(
    [string]$Entry = "SparkApp",
    [string]$Out   = "build/output/spark.cdx",
    [string]$Log   = "build/output/spark.log"
)

$ErrorActionPreference = 'Stop'

$src = "apps/spark/$Entry.codex"
if (-not (Test-Path $src)) { throw "no such chapter: $src" }

$outDir = Split-Path $Out -Parent
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }

Write-Host "Compiling $src ..."
pwsh build/compile.ps1 -Src $src -Out $Out -Log $Log
