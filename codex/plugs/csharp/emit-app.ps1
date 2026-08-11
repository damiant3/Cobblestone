# Transpile a Codex module through the csharp plug and package the
# result as a runnable .NET executable:
#
#   <source.codex>
#     |
#     v  run.ps1 (compile.ps1 -IrCce, then plug over its own TCP port, see build/plug-ports.ps1)
#   <OutDir>\<name>.cs
#     |
#     v  generated <name>.csproj (the only barbarian-authored artifact,
#     |  per the Lens platform seam -- and it is generated too)
#     v  dotnet build -c Release
#   <OutDir>\bin\Release\net9.0\<name>.exe
#
# -SqlClient adds the Microsoft.Data.SqlClient package reference for
# modules that bind the lens-sql-exec platform seam: the emitted
# _LensSql preamble references the client by full name and reads its
# connection string from LENS_SQL_CONN at run time.
#
# Usage:
#   plugs/csharp/emit-app.ps1 -Src <source.codex> -OutDir <dir> [-SqlClient]
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string]$Src,
    [Parameter(Mandatory=$true)] [string]$OutDir,
    [switch]$SqlClient,
    [int]$MemMB = 3072
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$PlugDir = (Resolve-Path $PSScriptRoot).Path
$Repo    = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
$SrcPath = (Resolve-Path $Src).Path
$Name    = [System.IO.Path]::GetFileNameWithoutExtension($SrcPath) -replace '[^A-Za-z0-9_]', '_'

New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir  = (Resolve-Path $OutDir).Path
$CsFile  = Join-Path $OutDir "$Name.cs"

# Transpile from the repo root: run.ps1 and compile.ps1 resolve cites
# and the stage-0 kernel relative to the current directory.
Push-Location $Repo
try {
    & pwsh -File (Join-Path $PlugDir 'run.ps1') -Src $SrcPath -Out $CsFile -MemMB $MemMB
    if ($LASTEXITCODE -ne 0) {
        [Console]::Error.WriteLine("FAIL: transpile exited $LASTEXITCODE")
        exit 2
    }
} finally { Pop-Location }

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('<Project Sdk="Microsoft.NET.Sdk">')
$lines.Add('  <PropertyGroup>')
$lines.Add('    <OutputType>Exe</OutputType>')
$lines.Add('    <TargetFramework>net9.0</TargetFramework>')
$lines.Add('    <Nullable>disable</Nullable>')
$lines.Add('    <ImplicitUsings>disable</ImplicitUsings>')
$lines.Add('    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>')
$lines.Add("    <AssemblyName>$Name</AssemblyName>")
$lines.Add('  </PropertyGroup>')
$lines.Add('  <ItemGroup>')
$lines.Add("    <Compile Include=`"$Name.cs`" />")
$lines.Add('  </ItemGroup>')
if ($SqlClient) {
    $lines.Add('  <ItemGroup>')
    $lines.Add('    <PackageReference Include="Microsoft.Data.SqlClient" Version="5.2.2" />')
    $lines.Add('  </ItemGroup>')
}
$lines.Add('</Project>')
$CsprojFile = Join-Path $OutDir "$Name.csproj"
[System.IO.File]::WriteAllLines($CsprojFile, $lines, [System.Text.UTF8Encoding]::new($false))

& dotnet build $CsprojFile -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) {
    [Console]::Error.WriteLine("FAIL: dotnet build exited $LASTEXITCODE")
    exit 3
}

$Exe = Join-Path $OutDir "bin\Release\net9.0\$Name.exe"
if (-not (Test-Path -PathType Leaf $Exe)) {
    [Console]::Error.WriteLine("FAIL: expected exe not found: $Exe")
    exit 4
}
Write-Host "[emit-app] OK: $Exe"
