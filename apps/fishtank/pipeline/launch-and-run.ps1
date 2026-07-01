# Launch Forge + ComfyUI if not already running, wait for APIs, run pipeline.
#
# Usage: pwsh apps/fishtank/pipeline/launch-and-run.ps1 -Species "Clownfish"
#        pwsh apps/fishtank/pipeline/launch-and-run.ps1 -Batch
[CmdletBinding()]
param(
    [string]$Species = '',
    [switch]$Batch,
    [string]$ForgeUrl = 'http://127.0.0.1:7860',
    [int]$ComfyPort = 8188,
    [string]$TripoDir = 'D:\AI\TripoSR'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ForgeDir = 'D:\AI\DiffusionForge'
$ComfyExe = 'C:\Users\Damian\AppData\Local\Programs\ComfyUI\ComfyUI.exe'

function Test-Api([string]$Url) {
    try { Invoke-RestMethod -Uri $Url -TimeoutSec 3 -ErrorAction Stop | Out-Null; return $true }
    catch { return $false }
}

function Wait-Api([string]$Url, [string]$Name, [int]$TimeoutSec = 120) {
    Write-Host "Waiting for $Name at $Url..." -NoNewline
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSec)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (Test-Api $Url) { Write-Host " ready" -ForegroundColor Green; return $true }
        Write-Host "." -NoNewline
        Start-Sleep -Seconds 3
    }
    Write-Host " TIMEOUT" -ForegroundColor Red
    return $false
}

# Launch Forge if not running
$forgeAlive = Test-Api "$ForgeUrl/sdapi/v1/sd-models"
if (-not $forgeAlive) {
    Write-Host "Starting Diffusion Forge..." -ForegroundColor Cyan
    $forgePython = Join-Path $ForgeDir 'system\python\python.exe'
    if (-not (Test-Path $forgePython)) { Write-Error "Forge Python not found at $forgePython"; exit 1 }
    $forgeLaunch = Join-Path $ForgeDir 'webui\launch.py'
    $forgeEnv = @{
        PATH = "$ForgeDir\system\git\bin;$ForgeDir\system\python;$ForgeDir\system\python\Scripts;$env:PATH"
        SKIP_VENV = '1'
        HF_HOME = "$ForgeDir\system\transformers-cache"
    }
    foreach ($k in $forgeEnv.Keys) { [Environment]::SetEnvironmentVariable($k, $forgeEnv[$k], 'Process') }
    Start-Process -FilePath $forgePython -ArgumentList "$forgeLaunch --cuda-malloc --api" -WorkingDirectory (Join-Path $ForgeDir 'webui')
    if (-not (Wait-Api "$ForgeUrl/sdapi/v1/sd-models" "Forge" 300)) {
        Write-Error "Forge did not start within 300 seconds"
        exit 2
    }
} else {
    Write-Host "Forge already running" -ForegroundColor Green
}

# Launch ComfyUI if not running
$comfyUrl = "http://127.0.0.1:$ComfyPort"
$comfyAlive = Test-Api "$comfyUrl/system_stats"
if (-not $comfyAlive) {
    if (Test-Path $ComfyExe) {
        Write-Host "Starting ComfyUI..." -ForegroundColor Cyan
        Start-Process -FilePath $ComfyExe
        if (-not (Wait-Api "$comfyUrl/system_stats" "ComfyUI" 120)) {
            Write-Warning "ComfyUI did not start (pipeline will use Forge only)"
        }
    } else {
        Write-Warning "ComfyUI not found at $ComfyExe (pipeline will use Forge only)"
    }
} else {
    Write-Host "ComfyUI already running" -ForegroundColor Green
}

# Run pipeline
$pipeDir = $PSScriptRoot
if ($Batch) {
    & pwsh -NoProfile -File (Join-Path $pipeDir 'batch-aquarium.ps1') -ForgeUrl $ForgeUrl -TripoDir $TripoDir
} elseif ($Species) {
    & pwsh -NoProfile -File (Join-Path $pipeDir 'run-pipeline.ps1') -Species $Species -ForgeUrl $ForgeUrl -TripoDir $TripoDir
} else {
    Write-Host "Usage: launch-and-run.ps1 -Species 'Clownfish' or -Batch" -ForegroundColor Yellow
}
