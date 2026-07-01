# Launch the GuiOS desktop.
# Usage: pwsh apps/guios/start.ps1 [-Build] [-Width 1024] [-Height 768]
[CmdletBinding()]
param(
    [switch]$Build,
    [int]$Width  = 1024,
    [int]$Height = 768,
    [int]$MemMB  = 3072
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo    = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$AppDir  = (Resolve-Path $PSScriptRoot).Path
$CdxFile = Join-Path $AppDir 'build-output' 'guios.cdx'
$FontDisk = Join-Path $Repo 'fonts' 'font-disk.img'
$VmBin   = Join-Path $Repo 'tools' 'codex-vm.exe'

if ($Build -or -not (Test-Path $CdxFile)) {
    Write-Host "[guios] Building..."
    & pwsh (Join-Path $AppDir 'build.ps1')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if (-not (Test-Path $CdxFile)) {
    Write-Error "CDX not found: $CdxFile"
    exit 1
}

$vmArgs = @(
    '-kernel', $CdxFile,
    '-gop-width', "$Width",
    '-gop-height', "$Height",
    '-mem', "$MemMB"
)

if (Test-Path $FontDisk) {
    $vmArgs += @('-disk', $FontDisk)
}

Write-Host "[guios] Launching: $Width x $Height, ${MemMB}MB RAM"
& $VmBin @vmArgs
