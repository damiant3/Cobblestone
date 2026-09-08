# Build CodexMagic Mobile: bundle chapters, compile through MAUI plug.
[CmdletBinding()]
param([switch]$Build)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$AppDir   = (Resolve-Path $PSScriptRoot).Path
$Repo     = (Resolve-Path (Join-Path $AppDir '..' '..')).Path
$OutDir   = Join-Path $AppDir 'build-output'
$BundleSrc = Join-Path $OutDir 'mobile-bundle.codex'

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$lines = [System.Collections.Generic.List[string]]::new()

function Add-Chapter {
    param([string]$Path, [string[]]$StripCites = @())
    if (-not (Test-Path $Path)) {
        [Console]::Error.WriteLine("MISSING: $Path")
        exit 3
    }
    foreach ($l in [System.IO.File]::ReadAllLines($Path)) {
        $skip = $false
        foreach ($sc in $StripCites) { if ($l -match "cites.*$sc") { $skip = $true } }
        if (-not $skip) { $lines.Add($l) }
    }
    $lines.Add(''); $lines.Add('')
}

# Bundle app chapters -- order matters (dependencies first)
Add-Chapter (Join-Path $AppDir 'MobileTheme.codex') -StripCites @('MobileApp')
Add-Chapter (Join-Path $AppDir 'LoginPage.codex') -StripCites @('MobileApp')
Add-Chapter (Join-Path $AppDir 'HomePage.codex') -StripCites @('MobileApp')
Add-Chapter (Join-Path $AppDir 'CreationsPage.codex') -StripCites @('MobileApp')
Add-Chapter (Join-Path $AppDir 'GalleryPage.codex') -StripCites @('MobileApp')
Add-Chapter (Join-Path $AppDir 'ClanPage.codex') -StripCites @('MobileApp')
Add-Chapter (Join-Path $AppDir 'ProfilePage.codex') -StripCites @('MobileApp')
Add-Chapter (Join-Path $AppDir 'MobileApp.codex') -StripCites @('MobileApp')

$body = ($lines -join "`n") + "`n"
[System.IO.File]::WriteAllText($BundleSrc, $body, [System.Text.UTF8Encoding]::new($false))
Write-Host "[mobile] bundled $($lines.Count) lines, $($body.Length) bytes"

# Run through MAUI plug
$plugRun = Join-Path $Repo 'codex' 'plugs' 'maui' 'run.ps1'
$projDir = Join-Path $OutDir 'CodexApp'
$buildFlag = if ($Build) { '-Build' } else { '' }

$runArgs = @('-NoProfile', '-File', $plugRun, '-Src', $BundleSrc, '-ProjectDir', $projDir)
if ($Build) { $runArgs += '-Build' }
& pwsh @runArgs

if ($LASTEXITCODE -ne 0) {
    [Console]::Error.WriteLine("FAIL: MAUI plug pipeline failed")
    exit 5
}

Write-Host "[mobile] Project ready at: $projDir"
