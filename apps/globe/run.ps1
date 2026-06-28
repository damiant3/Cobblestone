param(
    [string]$OutDir = "$PSScriptRoot\out"
)
$ErrorActionPreference = 'Stop'

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory $OutDir -Force | Out-Null }

$csc = (Get-ChildItem "C:\Windows\Microsoft.NET\Framework64\v4*\csc.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1)?.FullName

if (-not $csc) {
    $csc = (Get-Command csc -ErrorAction SilentlyContinue)?.Source
}
if (-not $csc) {
    $sdkDir = "C:\Program Files\dotnet\sdk"
    $roslyn = Get-ChildItem $sdkDir -Recurse -Filter "csc.dll" -ErrorAction SilentlyContinue |
              Sort-Object FullName -Descending | Select-Object -First 1
    if ($roslyn) {
        $csc = "dotnet"
        $cscArgs = @($roslyn.FullName)
    }
}

$src = "$PSScriptRoot\GlobeApp.cs"
$exe = "$OutDir\GlobeApp.exe"

foreach ($tf in @("earth-texture-ice.raw", "earth-texture.raw", "earth-texture-8k.raw")) {
    $texSrc = "$PSScriptRoot\$tf"
    if (Test-Path $texSrc) {
        Copy-Item $texSrc "$OutDir\$tf" -Force
        Write-Host "Copied $tf ($([math]::Round((Get-Item $texSrc).Length / 1MB, 1)) MB)"
    }
}

Write-Host "Compiling GlobeApp.cs..."

# Try dotnet-based compilation first
$csproj = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <ImplicitUsings>disable</ImplicitUsings>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="GlobeApp.cs" />
  </ItemGroup>
</Project>
"@

$projDir = "$OutDir\globe-build"
if (-not (Test-Path $projDir)) { New-Item -ItemType Directory $projDir -Force | Out-Null }
$csproj | Set-Content "$projDir\GlobeApp.csproj" -Encoding UTF8
Copy-Item $src "$projDir\GlobeApp.cs" -Force

Push-Location $projDir
try {
    dotnet build -c Release -o "$OutDir" --nologo -v q 2>&1 | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) { throw "Build failed" }
} finally { Pop-Location }

if (Test-Path $texSrc) {
    Copy-Item $texSrc "$OutDir\earth-texture.raw" -Force
}

Write-Host "Running globe..."
& "$OutDir\GlobeApp.exe"
