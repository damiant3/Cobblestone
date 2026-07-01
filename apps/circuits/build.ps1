param(
    [string]$Out = "build/output/circuits.cdx",
    [string]$Log = "build/output/circuits.log"
)

$ErrorActionPreference = 'Stop'

$sb = [System.Text.StringBuilder]::new()

# Core modules (dependency order)
$coreOrder = @(
    'CircuitUnits','NetlistModel','DesignRules','StackUp','ComponentModel',
    'DesignVariants','ProjectModel','CircuitSerializer','CrossProbe',
    'KicadImporter','PcbCalculator','SignedPackage'
)
foreach ($ch in $coreOrder) {
    [void]$sb.AppendLine((Get-Content "apps/circuits/Core/$ch.codex" -Raw))
}

# Subsystem directories
foreach ($d in @('SymbolEditor','SchematicEditor','FootprintEditor',
                 'Simulator','PcbEditor','BoardViewer','Manufacturing')) {
    foreach ($f in Get-ChildItem "apps/circuits/$d/*.codex" | Sort-Object Name) {
        [void]$sb.AppendLine((Get-Content $f.FullName -Raw))
    }
}

# UI infrastructure (dependency order: no intra-app deps first, then dependents)
$uiOrder = @(
    'CircuitsDisplay','CircuitsTheme',
    'CircuitsEngine','CanvasModel','BitmapFont','CircuitsUI','opening'
)
foreach ($ch in $uiOrder) {
    [void]$sb.AppendLine((Get-Content "apps/circuits/$ch.codex" -Raw))
}

$outDir = Split-Path $Out -Parent
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }

$srcPath = "build/output/circuits.codex"
[System.IO.File]::WriteAllText($srcPath, $sb.ToString(), [System.Text.UTF8Encoding]::new($false))

Write-Host "Concatenated circuits source: $((Get-Item $srcPath).Length) bytes"
Write-Host "Compiling..."

pwsh build/compile.ps1 -Src $srcPath -Out $Out -Log $Log
