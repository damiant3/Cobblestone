# Rescued from reek's session scratchpad by red at the 2026-08-17 AgentGrid
# relaunch (would otherwise have been lost). $Repo below is reek's workspace;
# set it to yours. Account: docs/Designs/Active/OS/OracleCloudArm64.md,
# section "ARM RUN 2026-08-17 (reek)".# Drive TWO HTTP requests at the ARM64 web server in one boot and bank every
# channel. boot-arm64.ps1 runs QEMU in the foreground with no timeout, so it
# cannot be an arm; this builds with -NoBoot and launches QEMU itself.
#
# Three channels, because the guest crashing and the client timing out look
# identical from the client alone:
#   serial   -- the guest's own loop lines and any exception dump
#   stderr   -- QEMU's account (used/avail index, RX in-buffers)
#   client   -- what each request actually returned
#
# The port is derived, not fixed: another agent booting arm64 on 8080 would
# otherwise be answering my requests (L-SHARED).
param(
  [string]$Src = 'codex\test\arm64-web-server.codex',
  [int]$BootWaitSec = 60,
  [int]$RequestTimeoutSec = 15,
  [int]$Requests = 2
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Repo = 'D:\Projects\NewRepository-reek'
Set-Location $Repo
$out = $PSScriptRoot
$serialFile = Join-Path $out 'arm64-serial.txt'
$errFile    = Join-Path $out 'arm64-qemu-err.txt'
Remove-Item $serialFile, $errFile -Force -ErrorAction SilentlyContinue

Write-Host '=== PHASE 1: build the image (also runs the new DMA floor assertion) ==='
& pwsh -NoProfile -File (Join-Path $Repo 'build\boot-arm64.ps1') -Src $Src -NoBoot 2>&1 |
    Select-String 'DMA floor|PE:|OK:|FAIL|Wire:' | ForEach-Object { "  $($_.Line.Trim())" }
if ($LASTEXITCODE -ne 0) { Write-Host "FAIL: build exited $LASTEXITCODE"; exit 2 }
$img = Join-Path $Repo 'build\arm64-output\codex-arm64.img'
if (-not (Test-Path $img)) { Write-Host "FAIL: no image at $img"; exit 2 }
Write-Host "  image: $((Get-Item $img).Length) bytes, written $((Get-Item $img).LastWriteTime)"

# A free port, so this cannot answer or be answered by another agent's guest.
$port = 0
foreach ($p in 18080..18120) {
  $l = $null
  try { $l = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $p); $l.Start(); $port = $p; $l.Stop(); break }
  catch { if ($l) { try { $l.Stop() } catch {} } }
}
if ($port -eq 0) { Write-Host 'FAIL: no free port in 18080-18120'; exit 2 }
Write-Host "=== PHASE 2: boot QEMU, hostfwd on $port ==="

$qemu = 'D:\Program Files\qemu\qemu-system-aarch64.exe'
$fwSrc = 'D:\Program Files\qemu\share\edk2-aarch64-code.fd'
$vars = Join-Path $Repo 'build\arm64-output\efi-varstore.img'
foreach ($f in @($qemu, $fwSrc, $vars)) { if (-not (Test-Path $f)) { Write-Host "FAIL: missing $f"; exit 2 } }
# Start-Process does NOT re-quote a space inside a single argument token, so
# `-drive if=pflash,...,file=D:\Program Files\...` reaches QEMU split at the
# space and it dies on 'Could not open D:\Program'. boot-arm64.ps1 gets away
# with the same path because `& $exe @args` quotes per element. Copy the
# firmware somewhere without a space rather than fight the quoting; the image,
# varstore and serial paths already have none.
$fw = Join-Path $out 'edk2-aarch64-code.fd'
if ((-not (Test-Path $fw)) -or (Get-Item $fw).Length -ne (Get-Item $fwSrc).Length) {
  Copy-Item -Force $fwSrc $fw
  Write-Host "  copied firmware to a space-free path: $((Get-Item $fw).Length) bytes"
}

$qargs = @(
  '-machine','virt,gic-version=3','-cpu','cortex-a72','-m','1024',
  '-drive',"if=pflash,format=raw,file=$fw,readonly=on",
  '-drive',"if=pflash,format=raw,file=$vars",
  '-drive',"file=$img,format=raw,if=virtio",
  '-device','virtio-net-pci,netdev=net0,mac=52:54:00:12:34:56',
  '-netdev',"user,id=net0,hostfwd=tcp::$port-:80",
  '-display','none','-serial',"file:$serialFile"
)
$proc = Start-Process -FilePath $qemu -ArgumentList $qargs -PassThru -WindowStyle Hidden -RedirectStandardError $errFile
Write-Host "  qemu pid $($proc.Id)"

try {
  # Wait for the guest to say it is listening. A known-good line proves the
  # instrument is live before any silence is read as evidence.
  $ready = $false
  $deadline = $BootWaitSec * 4
  for ($i = 0; $i -lt $deadline; $i++) {
    Start-Sleep -Milliseconds 250
    if ($proc.HasExited) { Write-Host "  qemu exited early, code $($proc.ExitCode)"; break }
    if (Test-Path $serialFile) {
      $s = Get-Content $serialFile -Raw -ErrorAction SilentlyContinue
      if ($s -and $s -match 'Listening on port 80') { $ready = $true; break }
    }
  }
  Write-Host "  guest reported listening: $ready"

  # An unfinished run is not a verdict. If QEMU never came up, requests can
  # only fail and their failure says nothing about the guest, so refuse to
  # report them as if it did.
  if (-not $ready) {
    Write-Host ''
    Write-Host 'HARNESS FAILURE: the guest never reported listening, so no request result below'
    Write-Host '                 would be evidence about the guest. QEMU stderr:'
    if ((Test-Path $errFile) -and (Get-Item $errFile).Length -gt 0) {
      Get-Content $errFile | Select-Object -First 10 | ForEach-Object { "  $_" }
    } else { Write-Host '  (stderr empty)' }
    if (Test-Path $serialFile) {
      Write-Host '                 serial tail:'
      Get-Content $serialFile | Select-Object -Last 10 | ForEach-Object { "  $_" }
    } else { Write-Host '                 (no serial file at all)' }
    if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
    exit 3
  }

  Write-Host "=== PHASE 3: $Requests requests, same boot ==="
  $paths = @('/api/health', '/api/health', '/', '/api/status')
  for ($n = 0; $n -lt $Requests; $n++) {
    $path = $paths[$n % $paths.Count]
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $verdict = ''
    try {
      $r = Invoke-WebRequest -Uri "http://127.0.0.1:$port$path" -TimeoutSec $RequestTimeoutSec -UseBasicParsing
      $verdict = "HTTP $($r.StatusCode), $($r.RawContentLength) bytes"
    } catch { $verdict = "FAILED: $($_.Exception.Message)" }
    $sw.Stop()
    Write-Host ("  request {0} {1,-14} {2,6:N1}s  {3}" -f $n, $path, $sw.Elapsed.TotalSeconds, $verdict)
  }
} finally {
  Start-Sleep -Milliseconds 500
  if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
}

Write-Host '=== SERIAL (the guest own account) ==='
if (Test-Path $serialFile) {
  Get-Content $serialFile | Where-Object { $_ -match 'loop |Listening|PCI|feat|rxq|Exception|avail=' } | ForEach-Object { "  $_" }
} else { Write-Host '  (no serial file)' }
Write-Host '=== QEMU STDERR (the device own account) ==='
if ((Test-Path $errFile) -and (Get-Item $errFile).Length -gt 0) {
  Get-Content $errFile | Select-Object -First 20 | ForEach-Object { "  $_" }
} else { Write-Host '  (empty)' }
Write-Host "=== files: $serialFile  $errFile ==="
