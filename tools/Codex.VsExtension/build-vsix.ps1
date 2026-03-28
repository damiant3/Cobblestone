# Build and install the Codex VS extension (syntax highlighting VSIX)
# Usage: pwsh -File build-vsix.ps1
#
# Automatically uninstalls any previous version before installing.
# Close Visual Studio before running.
#
# For full project template + Build menu support, also run:
#   pwsh -File install-vs.ps1

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$outDir = Join-Path $scriptDir "out"

if (Test-Path $outDir) { Remove-Item -Recurse -Force $outDir }
New-Item -ItemType Directory -Path $outDir | Out-Null

$vsixPath = Join-Path $outDir "CodexLanguage.vsix"

# A VSIX is just a ZIP with a specific structure
$staging = Join-Path $outDir "staging"
New-Item -ItemType Directory -Path $staging | Out-Null

# Copy manifest
Copy-Item (Join-Path $scriptDir "extension.vsixmanifest") $staging
# Copy pkgdef
Copy-Item (Join-Path $scriptDir "CodexLanguage.pkgdef") $staging
# Copy TextMate grammar
Copy-Item (Join-Path $scriptDir "codex.tmLanguage.json") $staging

# Create [Content_Types].xml (required by VSIX/OPC format)
$contentTypes = @'
<?xml version="1.0" encoding="utf-8"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="vsixmanifest" ContentType="text/xml" />
  <Default Extension="pkgdef" ContentType="text/plain" />
  <Default Extension="json" ContentType="application/json" />
</Types>
'@
Set-Content -LiteralPath (Join-Path $staging "[Content_Types].xml") -Value $contentTypes

# Zip it up
if (Test-Path $vsixPath) { Remove-Item $vsixPath }
Compress-Archive -Path (Join-Path $staging "*") -DestinationPath $vsixPath

Write-Output "VSIX built: $vsixPath"

# --- Auto-install: uninstall old version, install new ---
$extensionId = "CodexLanguage.a1b2c3d4-e5f6-7890-abcd-ef1234567890"

# Find VSIXInstaller.exe (VS 2022)
$vsixInstaller = $null
$vsEditions = @("Community", "Professional", "Enterprise")
foreach ($edition in $vsEditions) {
    $candidate = "C:\Program Files\Microsoft Visual Studio\2022\$edition\Common7\IDE\VSIXInstaller.exe"
    if (Test-Path $candidate) { $vsixInstaller = $candidate; break }
}
# Fallback: vswhere
if (-not $vsixInstaller) {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $vsPath = & $vswhere -latest -property installationPath 2>$null
        if ($vsPath) {
            $candidate = Join-Path $vsPath "Common7\IDE\VSIXInstaller.exe"
            if (Test-Path $candidate) { $vsixInstaller = $candidate }
        }
    }
}

if ($vsixInstaller) {
    Write-Output ""
    Write-Output "Found VSIXInstaller: $vsixInstaller"

    # Uninstall old version (ignore errors if not installed)
    Write-Output "Uninstalling previous version..."
    $uninstall = Start-Process -FilePath $vsixInstaller `
        -ArgumentList "/u:$extensionId", "/quiet" `
        -Wait -PassThru -NoNewWindow 2>$null
    if ($uninstall.ExitCode -eq 0) {
        Write-Output "  Previous version uninstalled."
    } else {
        Write-Output "  No previous version found (or already uninstalled)."
    }

    # Install new version
    Write-Output "Installing new version..."
    $install = Start-Process -FilePath $vsixInstaller `
        -ArgumentList "`"$vsixPath`"", "/quiet" `
        -Wait -PassThru -NoNewWindow
    if ($install.ExitCode -eq 0) {
        Write-Output "  Installed successfully!"
    } else {
        Write-Output "  Install returned exit code $($install.ExitCode)."
        Write-Output "  Try closing Visual Studio first, then re-run."
    }
} else {
    Write-Output ""
    Write-Output "VSIXInstaller.exe not found — install manually:"
    Write-Output "  Double-click $vsixPath"
}

Write-Output ""
Write-Output "This VSIX provides syntax highlighting only."
Write-Output "For project templates + Build menu, also run:"
Write-Output "  pwsh -File install-vs.ps1"
