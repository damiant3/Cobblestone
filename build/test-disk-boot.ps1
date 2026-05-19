[CmdletBinding()]
param(
    [string]$Img = (Join-Path $PSScriptRoot '..\build-output\Codex.img'),
    [int]$TimeoutSec = 60
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'vm-config.ps1')

if (-not (Test-Path $Img)) { throw "Image not found: $Img" }

Write-Host "Disk boot test" -ForegroundColor Cyan

if ($script:UseCodexVm) {
    $run = Start-CodexVmRun -Kernel $Img -ConnectTimeoutSec 30 -MemMB 2048
    if (-not $run) { Write-Host "FAIL: codex-vm did not start" -ForegroundColor Red; exit 1 }
    $conn = $run.Conn; $proc = $run.Process
    $stdoutFile = $run.StdoutFile; $stderrFile = $run.StderrFile
} else {
    $dataPort = Get-QemuPort
    $ctrlPort = $dataPort + 1
    Write-Host "  QEMU mode: ports $dataPort/$ctrlPort" -ForegroundColor Gray
    $qemuArgs = @(
        '-accel', 'tcg', '-cpu', 'max',
        '-machine', 'kernel-irqchip=off',
        '-drive', "file=$Img,format=raw,if=ide",
        '-chardev', (Get-QemuChardevData -Port $dataPort),
        '-chardev', (Get-QemuChardevCtrl -Port $ctrlPort),
        '-serial', 'chardev:ch0',
        '-serial', 'chardev:ch1',
        '-device', 'isa-debug-exit,iobase=0xf4,iosize=0x04',
        '-netdev', 'user,id=net0',
        '-device', 'ne2k_isa,netdev=net0,irq=9,iobase=0x300,mac=52:54:00:12:34:56',
        '-display', 'none',
        '-no-reboot',
        '-m', '2048'
    )
    $stdoutFile = [System.IO.Path]::GetTempFileName()
    $stderrFile = [System.IO.Path]::GetTempFileName()
    $proc = Start-Process -FilePath $script:QemuBin -ArgumentList $qemuArgs -PassThru -WindowStyle Hidden -RedirectStandardOutput $stdoutFile -RedirectStandardError $stderrFile
    Start-Sleep -Milliseconds 2000
    if ($proc.HasExited) {
        Write-Host "QEMU exited immediately (code $($proc.ExitCode))" -ForegroundColor Red
        Get-Content $stderrFile | Write-Host -ForegroundColor Red
        exit 1
    }
    $conn = Connect-Qemu -DataPort $dataPort -CtrlPort $ctrlPort -TimeoutSec 30
    if (-not $conn) {
        Write-Host "FAIL: could not connect to QEMU serial ports" -ForegroundColor Red
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        exit 1
    }
}

Write-Host "PID=$($proc.Id), waiting for READY..." -ForegroundColor Gray
$stream = $conn.Ctrl.GetStream()
$ready = $false
for ($i = 0; $i -lt 20; $i++) {
    $line = Read-StreamLine -Stream $stream -TimeoutSec $TimeoutSec
    if ($null -eq $line) { break }
    Write-Host "  CTRL: $line" -ForegroundColor Gray
    if ($line.StartsWith('READY')) { $ready = $true; break }
}

if ($ready) {
    Write-Host "SUCCESS: Kernel booted from disk image!" -ForegroundColor Green
} else {
    Write-Host "FAIL: no READY received" -ForegroundColor Red
    Close-Qemu -Conn $conn -Process $proc
    Remove-Item -Force $stdoutFile, $stderrFile -ErrorAction SilentlyContinue
    exit 1
}

Close-Qemu -Conn $conn -Process $proc
Remove-Item -Force $stdoutFile, $stderrFile -ErrorAction SilentlyContinue
