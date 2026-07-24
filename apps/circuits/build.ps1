param(
    [string]$Out = "build/output/circuits.cdx",
    [string]$Log = "build/output/circuits.log"
)

$ErrorActionPreference = 'Stop'

$outDir = Split-Path $Out -Parent
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }

# The circuits app is a single entry unit rooted at opening.codex; compile.ps1
# resolves its CircuitsApp / UI / Foreword cite closure. This replaces the old
# hand-written concat of every file: once the CircuitsApp quire was registered
# (the class-G fix), compile.ps1 resolved the same chapters through cites while
# the concat also pasted them in, so every shared type (CanvasItem, PaletteEntry,
# EditHistory, ...) was defined twice -- CDX3001, no binary. Resolving from the
# entry unit is what the app-class sweep already does, and it compiles clean.
pwsh build/compile.ps1 -Src apps/circuits/opening.codex -Out $Out -Log $Log
exit $LASTEXITCODE
