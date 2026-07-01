# Install the Codex SDK (MSBuild props/targets) for local development.
# This enables Codex projects to build using 'dotnet build' and VS Build menu.
#
# Usage: pwsh -File install-sdk.ps1
# Optional: pwsh -File install-sdk.ps1 -CliPath "C:\path\to\Codex.Cli.dll"

param(
    [string]$CliPath = ""
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# MSBuild extensions directory (user-level)
$msbuildExtDir = Join-Path $env:USERPROFILE ".msbuild"
$codexSdkDir = Join-Path $msbuildExtDir "Codex"

if (-not (Test-Path $msbuildExtDir)) {
    New-Item -ItemType Directory -Path $msbuildExtDir -Force | Out-Null
}
if (Test-Path $codexSdkDir) {
    Remove-Item -Recurse -Force $codexSdkDir
}
New-Item -ItemType Directory -Path $codexSdkDir | Out-Null

# Copy SDK files
Copy-Item (Join-Path $scriptDir "Sdk\Codex.Sdk.props") $codexSdkDir
Copy-Item (Join-Path $scriptDir "Sdk\Codex.Sdk.targets") $codexSdkDir

# If a CLI path was given, patch the props file
if ($CliPath -ne "") {
    $propsPath = Join-Path $codexSdkDir "Codex.Sdk.props"
    $content = Get-Content $propsPath -Raw
    $content = $content -replace 'CodexCliPath Condition.*?</CodexCliPath>', "CodexCliPath>$CliPath</CodexCliPath>"
    Set-Content -LiteralPath $propsPath -Value $content
}

Write-Output "Codex SDK installed to: $codexSdkDir"
Write-Output ""
Write-Output "The Codex project template uses a .csproj with inline build targets,"
Write-Output "so the SDK is optional. Use install-vs.ps1 instead for full VS integration."
