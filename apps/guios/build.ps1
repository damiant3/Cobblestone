# Build apps/guios/build-output/guios.cdx
# Bundles all GuiOS chapters + foreword/OS/data dependencies, compiles to CDX.
[CmdletBinding()]
param([switch]$Force)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo      = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$AppDir    = (Resolve-Path $PSScriptRoot).Path
$OutDir    = Join-Path $AppDir 'build-output'
$OutFile   = Join-Path $OutDir 'guios.cdx'
$BundleSrc = Join-Path $OutDir 'guios-source.codex'
$LogFile   = Join-Path $OutDir 'build.log'

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$AppChapters = @(
    'SystemFont',
    'GuiTimer',
    'GuiDisplay',
    'FontLoad',
    'FontAi',
    'GopRender',
    'TrackerDb',
    'TrackerApp',
    'CalcApp',
    'CalendarApp',
    'ProductivityApps',
    'MediaApps',
    'CreativeApps',
    'DiffusionApp',
    'CommsApps',
    'GuiShell'
)

. (Join-Path $Repo 'build\quire-map.ps1')
$appOrdered = [System.Collections.Generic.List[hashtable]]::new()
foreach ($ch in $AppChapters) {
    $path = Join-Path $AppDir "$ch.codex"
    if (-not (Test-Path -PathType Leaf $path)) {
        [Console]::Error.WriteLine("MISSING: $path")
        exit 3
    }
    $appOrdered.Add(@{ Quire = 'Guios'; Name = $ch; Path = $path; Lines = [System.IO.File]::ReadAllLines($path) })
}
$lines = Format-CiteChapters -Ordered $appOrdered

try {
    $ordered = Resolve-CiteOrder -RootLines $lines -Repo $Repo -ExcludeQuires @('Guios')
} catch {
    [Console]::Error.WriteLine("MISSING: $($_.Exception.Message)")
    exit 3
}
$preLines = Format-CiteChapters -Ordered $ordered

$body = (@($preLines) + @($lines) -join "`n") + "`n"
[System.IO.File]::WriteAllText($BundleSrc, $body, [System.Text.UTF8Encoding]::new($false))
Write-Host "[guios] bundled $($preLines.Count + $lines.Count) lines, $($body.Length) bytes"

$compileScript = Join-Path $Repo 'build\compile.ps1'
& pwsh -NoProfile -File $compileScript -Src $BundleSrc -Out $OutFile -Log $LogFile
if ($LASTEXITCODE -ne 0) {
    [Console]::Error.WriteLine("FAIL: compile errors; see $LogFile")
    exit 5
}
Write-Host "[guios] OK: $OutFile ($((Get-Item $OutFile).Length) bytes)"
