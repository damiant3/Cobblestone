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
$IrDir     = Join-Path $BuildDir 'ir'
$AppCdx    = Join-Path $BuildDir 'prism.cdx'
$LogFile   = Join-Path $BuildDir 'compile.log'

if (-not (Test-Path $BuildDir)) { New-Item -ItemType Directory $BuildDir | Out-Null }
if (-not (Test-Path $IrDir))    { New-Item -ItemType Directory $IrDir    | Out-Null }

$compileScript = Join-Path $Repo 'build\compile.ps1'

# -- Phase 1: Pre-compile demo files to IR ------------------------------
$demoFiles = @(
    'codex/test/punctual-smoke.codex',
    'codex/test/examples/missile-warning.codex',
    'codex/foreword/core/ListUtils.codex',
    'codex/foreword/core/StringUtils.codex',
    'apps/prism/Prism.codex'
)

foreach ($src in $demoFiles) {
    $name = [System.IO.Path]::GetFileNameWithoutExtension($src)
    $irFile = Join-Path $IrDir "$name.ir"
    $irLog  = Join-Path $IrDir "$name.log"

    Write-Host "[prism] Compiling $src to IR..."
    & pwsh -File $compileScript -Src (Join-Path $Repo $src) -Out $irFile -Log $irLog -IrUni 2>$null

    # IR-UNI writes to the log between IR-BEGIN and IR-END markers
    if (Test-Path $irLog) {
        $lines = Get-Content $irLog
        $capturing = $false
        $irLines = @()
        foreach ($l in $lines) {
            if ($l -eq 'IR-BEGIN') { $capturing = $true; continue }
            if ($l -eq 'IR-END')   { $capturing = $false; continue }
            if ($capturing) { $irLines += $l }
        }
        if ($irLines.Count -gt 0) {
            [System.IO.File]::WriteAllText($irFile, ($irLines -join "`n"), [System.Text.UTF8Encoding]::new($false))
            Write-Host "[prism]   -> $irFile ($($irLines.Count) lines)"
        } else {
            Write-Host "[prism]   -> no IR output (check $irLog)"
        }
    }
}

# -- Phase 2: Compile Prism app to CDX ----------------------------------
Write-Host "[prism] Compiling Prism.codex..."
& pwsh -File $compileScript -Src (Join-Path $AppDir 'Prism.codex') -Out $AppCdx -Log $LogFile
if ($LASTEXITCODE -ne 0) {
    [Console]::Error.WriteLine("FAIL: compile exited $LASTEXITCODE; see $LogFile")
    Get-Content $LogFile | Select-Object -Last 20
    exit 3
}
Write-Host "[prism] Compiled OK ($([math]::Round((Get-Item $AppCdx).Length / 1024)) KB)"

# -- Phase 3: Boot Prism in codex-vm with networking --------------------
Write-Host "[prism] Starting web server on http://localhost:$Port ..."
Write-Host "[prism] Press Ctrl+C to stop."

& $script:CodexVmBin -kernel $AppCdx -mem 2048 -headless -net -hostfwd "tcp::${Port}-:9200"
