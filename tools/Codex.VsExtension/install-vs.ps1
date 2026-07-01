# Quick-install Codex language support into Visual Studio 2022
# Usage: pwsh -File install-vs.ps1
#
# Installs:
#   1. TextMate grammar -> syntax highlighting for .codex files
#   2. Project template via 'dotnet new' -> "File > New > Project" shows "Codex Project"
#
# Close Visual Studio before running. Restart it after.

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# --- 1. Syntax highlighting (TextMate grammar + pkgdef) ---
$vsDir = "$env:USERPROFILE\.vs\Extensions"
$codexDir = Join-Path $vsDir "CodexLanguage"

if (-not (Test-Path $vsDir)) {
    New-Item -ItemType Directory -Path $vsDir -Force | Out-Null
}
if (Test-Path $codexDir) {
    Remove-Item -Recurse -Force $codexDir
}
New-Item -ItemType Directory -Path $codexDir | Out-Null

Copy-Item (Join-Path $scriptDir "codex.tmLanguage.json") $codexDir
Copy-Item (Join-Path $scriptDir "CodexLanguage.pkgdef") $codexDir

Write-Output "[1/2] Syntax highlighting installed to: $codexDir"

# --- 2. Project template (dotnet new) ---
# Remove any previous installation
dotnet new uninstall (Join-Path $scriptDir "ProjectTemplate") 2>$null

# Remove stale .vstemplate zip if present from older installs
$oldZipLocations = @(
    (Join-Path ([Environment]::GetFolderPath("MyDocuments")) "Visual Studio 2022\Templates\ProjectTemplates\CSharp\CodexProject.zip"),
    (Join-Path ([Environment]::GetFolderPath("MyDocuments")) "Visual Studio 2022\Templates\ProjectTemplates\Codex\CodexProject.zip")
)
foreach ($z in $oldZipLocations) {
    if (Test-Path $z) { Remove-Item -Force $z }
}

# Install via dotnet new
dotnet new install (Join-Path $scriptDir "ProjectTemplate")

Write-Output "[2/2] Project template installed via 'dotnet new'."

Write-Output ""
Write-Output "Done! Close and reopen Visual Studio 2022, then:"
Write-Output "  - .codex files get syntax highlighting automatically"
Write-Output "  - File > New > Project > search 'Codex' > Codex Project"
Write-Output "  - Ctrl+Shift+B to compile (requires 'codex' on PATH)"
Write-Output ""
Write-Output "To create from the command line:"
Write-Output "  dotnet new codex -n MyProject"
Write-Output ""
Write-Output "To put 'codex' on PATH, build the CLI first:"
Write-Output "  dotnet build tools/Codex.Cli/Codex.Cli.csproj"
