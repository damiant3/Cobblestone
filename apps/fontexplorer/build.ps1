# Build Font Explorer
#
# Two modes:
#   -Native (default): Build from FontExplorerApp.cs directly
#   -Transpile:        Codex source -> IR -> WinForms plug -> C# -> .exe
#
# Usage: pwsh apps/fontexplorer/build.ps1
#        pwsh apps/fontexplorer/build.ps1 -Transpile
[CmdletBinding()]
param(
    [switch]$Transpile,
    [switch]$SkipPlugBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$AppDir = (Resolve-Path $PSScriptRoot).Path
$OutDir = Join-Path $AppDir 'build-output'
New-Item -ItemType Directory -Force $OutDir | Out-Null

$csprojDir = Join-Path $OutDir 'dotnet'
$csproj = Join-Path $csprojDir 'FontExplorer.csproj'

New-Item -ItemType Directory -Force $csprojDir | Out-Null
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <RootNamespace>CodexFontExplorer</RootNamespace>
    <AssemblyName>FontExplorer</AssemblyName>
  </PropertyGroup>
</Project>
"@ | Set-Content $csproj

$nativeCs = Join-Path $AppDir 'FontExplorerApp.cs'

if (-not $Transpile -and (Test-Path $nativeCs)) {
    Write-Host '[fontexplorer] Building from native C# source...' -ForegroundColor Cyan
    Get-ChildItem $csprojDir -Filter '*.cs' | Remove-Item -Force
    Copy-Item -Force $nativeCs (Join-Path $csprojDir 'FontExplorerApp.cs')
} else {
    Write-Host '[fontexplorer] Building via Codex transpilation pipeline...' -ForegroundColor Cyan
    $WfRunScript = Join-Path $Repo 'codex' 'plugs' 'winforms' 'run.ps1'
    $WfBuildScript = Join-Path $Repo 'codex' 'plugs' 'winforms' 'build.ps1'
    $WfPlugCdx = Join-Path $Repo 'codex' 'plugs' 'winforms' 'build-output' 'winforms-plug.cdx'
    $BundleScript = Join-Path $Repo 'build' 'bundle-app.ps1'

    if (-not $SkipPlugBuild -and -not (Test-Path -PathType Leaf $WfPlugCdx)) {
        Write-Host '[fontexplorer] Building WinForms plug...' -ForegroundColor Yellow
        & pwsh -NoProfile -File $WfBuildScript
        if ($LASTEXITCODE -ne 0) { Write-Error 'WinForms plug build failed'; exit 1 }
    }

    $bundled = Join-Path $OutDir 'fontexplorer-bundle.codex'
    & pwsh -NoProfile -File $BundleScript -Src (Join-Path $AppDir 'opening.codex') -Out $bundled
    if ($LASTEXITCODE -ne 0) { Write-Error 'Bundle failed'; exit 2 }

    $csFile = Join-Path $OutDir 'FontExplorer.cs'
    & pwsh -NoProfile -File $WfRunScript -Src $bundled -Out $csFile
    if ($LASTEXITCODE -ne 0) { Write-Error 'WinForms plug failed'; exit 3 }

    Get-ChildItem $csprojDir -Filter '*.cs' | Remove-Item -Force
    Copy-Item -Force $csFile (Join-Path $csprojDir 'FontExplorer.cs')
}

Write-Host '[fontexplorer] Building .NET executable...' -ForegroundColor Cyan
$dotnetResult = & dotnet build $csproj -c Release 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host '[fontexplorer] dotnet build FAILED:' -ForegroundColor Red
    Write-Host ($dotnetResult | Out-String)
    exit 4
}

$binDir = Get-ChildItem (Join-Path $csprojDir 'bin' 'Release') -Directory | Select-Object -First 1
if ($binDir) {
    $runDir = Join-Path $OutDir 'app'
    New-Item -ItemType Directory -Force $runDir | Out-Null
    Copy-Item "$($binDir.FullName)\*" $runDir -Recurse -Force
    $ptxFile = Join-Path $Repo 'apps' 'fontai' 'kernels' 'mlp.ptx'
    if (Test-Path $ptxFile) {
        Copy-Item $ptxFile $runDir -Force
        Write-Host "[fontexplorer] Bundled mlp.ptx for GPU training" -ForegroundColor Cyan
    }
    Write-Host "[fontexplorer] SUCCESS: $runDir\FontExplorer.exe" -ForegroundColor Green
} else {
    Write-Host '[fontexplorer] Built but output dir not found' -ForegroundColor Yellow
}
