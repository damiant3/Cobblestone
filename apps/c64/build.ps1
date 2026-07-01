param(
    [string]$Out = "build/output/c64.cdx",
    [string]$Log = "build/output/c64.log"
)
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '../..')

$outDir = Split-Path $Out -Parent
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }

$chapters = @(
    'apps/c64/RomData.codex',
    'apps/c64/Memory.codex',
    'apps/c64/Cia.codex',
    'apps/c64/Cpu6502.codex',
    'apps/c64/opening.codex'
)

$srcPath = "build/output/c64.codex"
$sb = [System.Text.StringBuilder]::new()
foreach ($ch in $chapters) {
    $lines = [System.IO.File]::ReadAllLines($ch)
    foreach ($l in $lines) { [void]$sb.AppendLine($l) }
}
[System.IO.File]::WriteAllText($srcPath, $sb.ToString(), [System.Text.UTF8Encoding]::new($false))
Write-Host "Concatenated c64 source: $($sb.Length) bytes"

Write-Host "Compiling..."
pwsh build/compile.ps1 -Src $srcPath -Out $Out -Log $Log
