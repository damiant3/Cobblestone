# Build apps/cvmm/build-output/cvmm-dashboard.html
# Compiles CvmmApp.codex through the HTML plug to a self-contained page.
[CmdletBinding()]
param([switch]$Force)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo      = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$AppDir    = (Resolve-Path $PSScriptRoot).Path
$OutDir    = Join-Path $AppDir 'build-output'
$OutHtml   = Join-Path $OutDir 'cvmm-dashboard.html'
$BundleSrc = Join-Path $OutDir 'app-source.codex'
$LogFile   = Join-Path $OutDir 'app-build.log'
$PlugDir   = Join-Path $Repo 'codex\plugs\html'
$PlugCdx   = Join-Path $PlugDir 'build-output\html-plug.cdx'

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# Build HTML plug if needed
if (-not (Test-Path -PathType Leaf $PlugCdx)) {
    Write-Host "[cvmm-app] Building HTML plug first..." -ForegroundColor Yellow
    & pwsh -NoProfile -File (Join-Path $PlugDir 'build.ps1')
    if (-not (Test-Path -PathType Leaf $PlugCdx)) {
        [Console]::Error.WriteLine("FAIL: HTML plug build failed")
        exit 3
    }
}

$lines = [System.Collections.Generic.List[string]]::new()

function Add-Chapter {
    param([string]$Path)
    if (-not (Test-Path -PathType Leaf $Path)) {
        [Console]::Error.WriteLine("MISSING: $Path")
        exit 3
    }
    $renamed = $false
    foreach ($l in [System.IO.File]::ReadAllLines($Path)) {
        if ((-not $renamed) -and $l -match '^Chapter:\s*(.+?)\s*$') {
            $lines.Add("Chapter: Cvmm--$($matches[1])")
            $renamed = $true
        } else { $lines.Add($l) }
    }
    $lines.Add(''); $lines.Add('')
}

# App chapters -- only the ones needed for the HTML page
$AppChapters = @(
    'CvmmTypes',
    'ProductivityDb',
    'Notes',
    'Calendar',
    'CvmmTheme',
    'CvmmSettingsViews',
    'CvmmApp'
)

foreach ($ch in $AppChapters) {
    Add-Chapter -Path (Join-Path $AppDir "$ch.codex")
}

# Resolve foreword cites (Cvmm intra-quire chapters are bundled above)
. (Join-Path $Repo 'build\quire-map.ps1')
try {
    $ordered = Resolve-CiteOrder -RootLines $lines -Repo $Repo -ExcludeQuires @('Cvmm')
} catch {
    [Console]::Error.WriteLine("MISSING: $($_.Exception.Message)")
    exit 3
}
$preLines = Format-CiteChapters -Ordered $ordered

$body = (($preLines + $lines) -join "`n") + "`n"
[System.IO.File]::WriteAllText($BundleSrc, $body, [System.Text.UTF8Encoding]::new($false))
Write-Host "[cvmm-app] bundled $($preLines.Count + $lines.Count) lines, $($body.Length) bytes"

# Compile through HTML plug -- compile to IR, then run through plug
$compileScript = Join-Path $Repo 'build\compile.ps1'
$irFile = Join-Path $OutDir 'app-ir.txt'
& pwsh -NoProfile -File $compileScript -Src $BundleSrc -Out $irFile -Log $LogFile -IrUni
if ($LASTEXITCODE -ne 0) {
    [Console]::Error.WriteLine("FAIL: compile errors; see $LogFile")
    exit 5
}

# Run HTML plug on the IR
$plugRun = Join-Path $PlugDir 'run.ps1'
if (Test-Path -PathType Leaf $plugRun) {
    & pwsh -NoProfile -File $plugRun -Ir $irFile -Out $OutHtml
} else {
    Copy-Item $irFile $OutHtml
}

Write-Host "[cvmm-app] OK: $OutHtml"
