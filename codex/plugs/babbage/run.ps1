# Run the Babbage plug over a Codex source file via TCP.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string]$Src,
    [Parameter(Mandatory=$true)] [string]$Out
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..' '..' '..' 'build' 'vm-config.ps1')

$Repo     = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
$PlugDir  = (Resolve-Path $PSScriptRoot).Path
$PlugCdx  = Join-Path $PlugDir 'build-output\babbage-plug.cdx'
$IrDir    = Join-Path $PlugDir 'build-output'
$IrFile   = Join-Path $IrDir 'last-run.ir'
$LogFile  = Join-Path $IrDir 'run.log'

if (-not (Test-Path -PathType Leaf $PlugCdx)) {
    [Console]::Error.WriteLine("MISSING: $PlugCdx -- run plugs/babbage/build.ps1 first")
    exit 2
}

# ── Phase 1: Codex source -> IR text ────────────────────────────────
$compileScript = Join-Path $Repo 'build\compile.ps1'
& pwsh -File $compileScript -Src $Src -Out $IrFile -Log $LogFile -IrCce
if ($LASTEXITCODE -ne 0) {
    [Console]::Error.WriteLine("FAIL: IR emit step exited $LASTEXITCODE; see $LogFile")
    exit 3
}
$irBytes = [System.IO.File]::ReadAllBytes($IrFile)
Write-Host "[babbage-run] IR: $($irBytes.Length) bytes"

# ── Phase 2: Start TCP listener ─────────────────────────────────────
$plugPort = 9100
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $plugPort)
$listener.Start()
Write-Host "[babbage-run] Listening on port $plugPort"

# ── Phase 3: Boot plug CDX ──────────────────────────────────────────
$run = Start-VmRun -Kernel $PlugCdx -ConnectTimeoutSec 30 -MemMB 4096
if (-not $run) {
    $listener.Stop()
    [Console]::Error.WriteLine("FAIL: VM did not start")
    exit 4
}

try {
    $conn = $run.Conn
    if (-not (Read-VmReady -Conn $conn -TimeoutSec 30)) {
        [Console]::Error.WriteLine("READY not received within 30s")
        exit 4
    }

    # Accept TCP connection from plug
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while (-not $listener.Pending()) {
        if ([DateTime]::UtcNow -gt $deadline) {
            [Console]::Error.WriteLine("FAIL: plug did not connect within 30s")
            exit 5
        }
        Start-Sleep -Milliseconds 50
    }
    $tcpClient = $listener.AcceptTcpClient()
    $tcpStream = $tcpClient.GetStream()
    $listener.Stop()
    Write-Host "[babbage-run] Plug connected"

    # ── Phase 4: Send IR as framed message (tag=1) ──────────────────
    $msgLen = $irBytes.Length + 1
    $header = [BitConverter]::GetBytes([int]$msgLen)
    $tcpStream.Write($header, 0, 4)
    $tcpStream.WriteByte(1)
    $tcpStream.Write($irBytes, 0, $irBytes.Length)
    $tcpStream.Flush()
    Write-Host "[babbage-run] Sent IR ($($irBytes.Length) bytes)"

    # ── Phase 5: Receive output until plug sends FIN ────────────────
    $tcpStream.ReadTimeout = 120000
    $allBytes = [System.Collections.Generic.List[byte]]::new(65536)
    $readBuf = [byte[]]::new(8192)
    try {
        while ($true) {
            $n = $tcpStream.Read($readBuf, 0, $readBuf.Length)
            if ($n -le 0) { break }
            for ($bi = 0; $bi -lt $n; $bi++) { $allBytes.Add($readBuf[$bi]) }
        }
    } catch {}
    $outText = [System.Text.Encoding]::UTF8.GetString($allBytes.ToArray())
    [System.IO.File]::WriteAllText($Out, $outText, [System.Text.UTF8Encoding]::new($false))
    Write-Host "[babbage-run] OK: $Out ($($outText.Length) chars)"

    $tcpClient.Close()

    # ── Phase 5b: Drain serial for diagnostic output ─────────────────
    $serialDrain = ''
    $dataStream = $conn.Data.GetStream()
    $dataStream.ReadTimeout = 5000
    $sBuf = [byte[]]::new(4096)
    try {
        while ($true) {
            $sn = $dataStream.Read($sBuf, 0, $sBuf.Length)
            if ($sn -le 0) { break }
            $serialDrain += [System.Text.Encoding]::UTF8.GetString($sBuf, 0, $sn)
        }
    } catch {}
    if ($serialDrain.Length -gt 0) { Write-Host "[babbage-run] Serial: $serialDrain" }
    exit 0
} finally {
    if ($listener.Server.IsBound) { try { $listener.Stop() } catch {} }
    if ($run) {
        Close-Vm -Conn $run.Conn -Process $run.Process
        if ($run.StderrFile -and (Test-Path $run.StderrFile)) {
            for ($retry = 0; $retry -lt 10; $retry++) { try { $stderr = [System.IO.File]::ReadAllText($run.StderrFile).Trim(); break } catch { Start-Sleep -Milliseconds 200 } }
            if (-not $stderr) { $stderr = '' }
            if ($stderr.Length -gt 0) {
                [Console]::Error.WriteLine("[babbage-run] VM stderr:")
                [Console]::Error.WriteLine($stderr)
                if ($stderr -match 'RIP=0x([0-9a-fA-F]+)') {
                    $crashRip = [Convert]::ToInt64($matches[1], 16)
                    $mapPath = Join-Path $PlugDir 'build-output\babbage-plug.map'
                    if (Test-Path $mapPath) {
                        foreach ($ml in Get-Content $mapPath) {
                            if ($ml -match '^(0x[0-9a-fA-F]+)\s+(\d+)\s+(.+)$') {
                                $addr = [Convert]::ToInt64($matches[1], 16)
                                $size = [int]$matches[2]
                                if ($crashRip -ge $addr -and $crashRip -lt ($addr + $size)) {
                                    $offset = $crashRip - $addr
                                    [Console]::Error.WriteLine("CRASH: $($matches[3]) at $($matches[1]) (offset +$offset)")
                                    break
                                }
                            }
                        }
                    }
                }
            }
        }
        Remove-Item -Force $run.StdoutFile, $run.StderrFile -ErrorAction SilentlyContinue
    }
}
