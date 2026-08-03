# Build apps/guios/build-output/guios.cdx
# Bundles all GuiOS chapters + foreword/OS/data dependencies, compiles to CDX.
[CmdletBinding()]
param(
    [switch]$Force,
    # The compiler that builds guios.cdx. Empty means whatever build.ps1 last
    # left in build-output. build-output/ is NOT in the depot, so an artifact
    # here has no provenance unless this pins it: a guios.cdx built 2026-07-21
    # by a seed that emitted SIPI vector 0 triple-faulted under -smp for a week
    # after the compiler stopped emitting that, and read as a live GuiOS bug.
    [string]$Kernel = ''
)

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
$compileArgs = @('-Src', $BundleSrc, '-Out', $OutFile, '-Log', $LogFile)
if ($Kernel -ne '') {
    if (-not (Test-Path $Kernel)) { throw "-Kernel not found: $Kernel" }
    $compileArgs += @('-Kernel', (Resolve-Path $Kernel).Path)
}
& pwsh -NoProfile -File $compileScript @compileArgs
if ($LASTEXITCODE -ne 0) {
    [Console]::Error.WriteLine("FAIL: compile errors; see $LogFile")
    exit 5
}
Write-Host "[guios] OK: $OutFile ($((Get-Item $OutFile).Length) bytes)"
