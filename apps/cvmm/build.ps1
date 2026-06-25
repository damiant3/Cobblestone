# Build apps/cvmm/build-output/cvmm-server.cdx
# Bundles all CVMM chapters + foreword/OS dependencies, compiles to CDX.
[CmdletBinding()]
param([switch]$Force)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo      = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$AppDir    = (Resolve-Path $PSScriptRoot).Path
$OutDir    = Join-Path $AppDir 'build-output'
$OutFile   = Join-Path $OutDir 'cvmm-server.cdx'
$BundleSrc = Join-Path $OutDir 'cvmm-source.codex'
$LogFile   = Join-Path $OutDir 'build.log'

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# App chapters in dependency order (leaf deps first, entry point last)
$AppChapters = @(
    'CvmmTypes',
    'ResourceModel',
    'Command',
    'CvmmState',
    'CvmmTheme',
    'CvmmDashboard',
    'FileExplorer',
    'DriveManager',
    'UsbManager',
    'ProcessManager',
    'ServiceManager',
    'PortMonitor',
    'NetworkManager',
    'DisplayManager',
    'ServerManager',
    'FleetManager',
    'DeployManager',
    'CvmmDisplay',
    'LogViewer',
    'Terminal',
    'ProductivityDb',
    'Monitor',
    'Calendar',
    'Notes',
    'MeshBridge',
    'DataBinding',
    'Keybindings',
    'Serialize',
    'CvmmSettingsViews',
    'CvmmShell',
    'CvmmRoutes',
    'CvmmServer'
)

# App chapters render through Format-CiteChapters so their headers carry
# the Cvmm-- prefix; compile.ps1's embedded-chapter detection only matches
# that form, and plain headers make it re-pull every Cvmm chapter into its
# own prelude (duplicate-definition CDX3001s).
. (Join-Path $Repo 'build\quire-map.ps1')
$appOrdered = [System.Collections.Generic.List[hashtable]]::new()
foreach ($ch in $AppChapters) {
    $path = Join-Path $AppDir "$ch.codex"
    if (-not (Test-Path -PathType Leaf $path)) {
        [Console]::Error.WriteLine("MISSING: $path")
        exit 3
    }
    $appOrdered.Add(@{ Quire = 'Cvmm'; Name = $ch; Path = $path; Lines = [System.IO.File]::ReadAllLines($path) })
}
$lines = Format-CiteChapters -Ordered $appOrdered

# -- Resolve foreword/OS cites -------------------------------------------
# Cvmm intra-quire cites are already bundled above -- skip them
try {
    $ordered = Resolve-CiteOrder -RootLines $lines -Repo $Repo -ExcludeQuires @('Cvmm')
} catch {
    [Console]::Error.WriteLine("MISSING: $($_.Exception.Message)")
    exit 3
}
$preLines = Format-CiteChapters -Ordered $ordered

$body = (@($preLines) + @($lines) -join "`n") + "`n"
[System.IO.File]::WriteAllText($BundleSrc, $body, [System.Text.UTF8Encoding]::new($false))
Write-Host "[cvmm] bundled $($preLines.Count + $lines.Count) lines, $($body.Length) bytes"

# -- Compile via compile.ps1 ------------------------------------------
$compileScript = Join-Path $Repo 'build\compile.ps1'
& pwsh -NoProfile -File $compileScript -Src $BundleSrc -Out $OutFile -Log $LogFile
if ($LASTEXITCODE -ne 0) {
    [Console]::Error.WriteLine("FAIL: compile errors; see $LogFile")
    exit 5
}
Write-Host "[cvmm] OK: $OutFile ($((Get-Item $OutFile).Length) bytes)"
