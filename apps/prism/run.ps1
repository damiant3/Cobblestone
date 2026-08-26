# Prism -- Codex Through Every Lens
# Compile demo files to IR, build and boot the Prism web server.
#
# Usage: apps/prism/run.ps1
# Then open http://localhost:8080 in your browser.
[CmdletBinding()]
param(
    [int]$Port = 8080
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..' '..' 'build' 'vm-config.ps1')

$Repo      = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$AppDir    = (Resolve-Path $PSScriptRoot).Path
$BuildDir  = Join-Path $AppDir 'build-output'
$AppCdx    = Join-Path $BuildDir 'prism.cdx'
$LogFile   = Join-Path $BuildDir 'compile.log'

if (-not (Test-Path $BuildDir)) { New-Item -ItemType Directory $BuildDir | Out-Null }

$compileScript = Join-Path $Repo 'build\compile.ps1'

# There is no IR pre-bake here any more. It compiled a hard-coded list of five
# demo files to IR on the host before the app booted, which is the canned IR
# Damian's ruling of 2026-08-24 removes: "we definitely need compile/transpile
# on the fly for the prism. the canned IR is not the correct design." Baked IR
# could only ever answer for files that were already in the repo, and a REPL is
# defined by compiling text that is not.

# -- Compile Prism app to CDX -------------------------------------------
Write-Host "[prism] Compiling Prism.codex..."
& pwsh -File $compileScript -Src (Join-Path $AppDir 'Prism.codex') -Out $AppCdx -Log $LogFile
if ($LASTEXITCODE -ne 0) {
    [Console]::Error.WriteLine("FAIL: compile exited $LASTEXITCODE; see $LogFile")
    Get-Content $LogFile | Select-Object -Last 20
    exit 3
}
Write-Host "[prism] Compiled OK ($([math]::Round((Get-Item $AppCdx).Length / 1024)) KB)"

# -- Boot Prism in codex-vm with networking -----------------------------
Write-Host "[prism] Starting web server on http://localhost:$Port ..."
Write-Host "[prism] Press Ctrl+C to stop."

& $script:CodexVmBin -kernel $AppCdx -mem 2048 -headless -net -hostfwd "tcp::${Port}-:9200"
