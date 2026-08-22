# mmio-gate-arm.ps1 -- the ARM64 MMIO window gate, both arms.
#
# HAND-WRITTEN. Registered in build/handwritten-scripts.txt.
#
# The gate lives in the emitted code (a64-rt-guarded-access), not in the type
# checker, so no compile-time check can observe it and a green battery cannot
# tell a live gate from a dead one. Two programs settle it: the same peek-32
# of the same device register, differing only in whether the opening declares
# Device.Mmio.
#
#   open arm   declares Device.Mmio   -> reaches the PL011, prints its value
#   shut arm   declares nothing       -> refused, prints -1
#
# The assertion that matters is that the two DISAGREE. If the gate were
# removed both would print the device value; if it refused everything both
# would print -1. Either failure makes the arms equal, and equal is red.
#
#   pwsh build/mmio-gate-arm.ps1
#
# Exit status: 0 on pass, 1 on failure.
[CmdletBinding()]
param([int]$TimeoutSec = 10)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
[Environment]::CurrentDirectory = (Get-Location).Path
$Repo = (Get-Location).Path

. (Join-Path $PSScriptRoot 'renode-config.ps1')
$RenodeExe = Get-RenodeExe -Repo $Repo
if (-not $RenodeExe) { Write-RenodeSkip; exit 0 }

$plugCdx = Join-Path $Repo 'codex\plugs\arm64\build-output\arm64-plug.cdx'
if (-not (Test-Path -PathType Leaf $plugCdx)) {
    Write-Host "SKIP: arm64 plug not built ($plugCdx)" -ForegroundColor Yellow
    Write-Host '  Build with: codex\plugs\arm64\build.ps1'
    exit 0
}
$boardRepl = Join-Path $Repo 'tools\renode\codex\codex-arm64.repl'
if (-not (Test-Path -PathType Leaf $boardRepl)) {
    Write-Host "SKIP: board definition missing ($boardRepl)" -ForegroundColor Yellow
    exit 0
}
$compileScript = Join-Path $Repo 'codex\plugs\arm64\compile-arm64.ps1'

$SeedCdx = Join-Path $Repo 'seed\Codex.cdx'
$Stage0 = Join-Path $Repo 'build-output\bare-metal\Codex.cdx'
New-Item -ItemType Directory -Force (Split-Path $Stage0) | Out-Null
if (-not (Test-Path -PathType Leaf $Stage0)) { Copy-Item -Force $SeedCdx $Stage0 }

$OutDir = Join-Path $Repo 'test-output-cross\mmio-gate'
New-Item -ItemType Directory -Force $OutDir | Out-Null

function Invoke-Arm {
    param([string]$Name)

    $src = Join-Path $Repo "build\mmio-gate-$Name.codex"
    $armDir = Join-Path $OutDir $Name
    New-Item -ItemType Directory -Force $armDir | Out-Null
    $elfOut = Join-Path $armDir "$Name.elf"
    $compileLog = Join-Path $armDir 'compile.log'

    Write-Host -NoNewline "  $Name compile ... "
    $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    & pwsh -NoProfile -File $compileScript -Src $src -Out $elfOut 2>&1 | Out-File -FilePath $compileLog -Encoding UTF8
    $compileExit = $LASTEXITCODE
    $ErrorActionPreference = $prev
    if ($compileExit -ne 0 -or -not (Test-Path -PathType Leaf $elfOut)) {
        Write-Host 'FAIL' -ForegroundColor Red
        Get-Content $compileLog | Select-String 'error CDX|CODEGEN-ERRORS' | Select-Object -First 3 | ForEach-Object { Write-Host "    $($_.Line.Trim())" }
        return $null
    }
    Write-Host 'OK'

    $elfPath = (Resolve-Path $elfOut).Path -replace '\\','/'
    $boardPath = (Resolve-Path $boardRepl).Path -replace '\\','/'
    $uartLog = (Join-Path $armDir 'uart.log') -replace '\\','/'
    if (Test-Path -PathType Leaf $uartLog) { Remove-Item -Force -ErrorAction SilentlyContinue $uartLog }

    $rescContent = @(
        'mach create "codex"'
        "machine LoadPlatformDescription @$boardPath"
        "sysbus LoadELF @$elfPath"
        "uart0 CreateFileBackend @$uartLog true"
        'start'
        "sleep $TimeoutSec"
        'quit'
    ) -join "`n"
    $rescFile = Join-Path $armDir 'run.resc'
    [System.IO.File]::WriteAllText($rescFile, $rescContent)
    $rescPath = $rescFile -replace '\\','/'

    Write-Host -NoNewline "  $Name run ... "
    $prev2 = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    & $RenodeExe --disable-xwt --console -e "include @$rescPath" 2>&1 | Out-Null
    $ErrorActionPreference = $prev2
    Start-Sleep -Milliseconds 300

    $uartLogWin = $uartLog -replace '/','\'
    if (-not (Test-Path $uartLogWin)) { Write-Host 'FAIL (no uart output)' -ForegroundColor Red; return $null }
    $raw = [System.IO.File]::ReadAllText($uartLogWin) -replace "`r",''
    $line = @($raw -split "`n" | Where-Object { $_.StartsWith('mmio: ') }) | Select-Object -First 1
    if (-not $line) { Write-Host 'FAIL (no mmio line)' -ForegroundColor Red; return $null }
    Write-Host $line
    return $line.Substring(6).Trim()
}

Write-Host '=== ARM64 MMIO window gate ===' -ForegroundColor Cyan

$open = Invoke-Arm -Name 'open'
$shut = Invoke-Arm -Name 'shut'

if ($null -eq $open -or $null -eq $shut) {
    Write-Host 'mmio-gate-arm: FAIL (an arm did not report)' -ForegroundColor Red
    exit 1
}

$bad = @()
if ($shut -ne '-1') { $bad += "shut arm read the device ($shut); the gate did not refuse" }
if ($open -eq '-1') { $bad += 'open arm was refused; a declared Device.Mmio program cannot reach the device' }
if ($open -eq $shut) { $bad += "both arms answered $open; the declaration made no difference" }

if ($bad.Count -gt 0) {
    foreach ($b in $bad) { Write-Host "  $b" -ForegroundColor Red }
    Write-Host 'mmio-gate-arm: FAIL' -ForegroundColor Red
    exit 1
}

Write-Host "mmio-gate-arm: OK (declared $open, undeclared $shut)" -ForegroundColor Green
exit 0
