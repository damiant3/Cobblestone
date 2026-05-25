# Run the Elixir plug over a Codex source file via TCP.
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
$PlugCdx  = Join-Path $PlugDir 'build-output\elixir-plug.cdx'
$IrDir    = Join-Path $PlugDir 'build-output'
$IrFile   = Join-Path $IrDir 'last-run.ir'
$LogFile  = Join-Path $IrDir 'run.log'

if (-not (Test-Path -PathType Leaf $PlugCdx)) {
    [Console]::Error.WriteLine("MISSING: $PlugCdx -- run plugs/elixir/build.ps1 first")
    exit 2
}

# ── Phase 1: Codex source -> IR text ────────────────────────────────
$irRun = Start-VmRun -Kernel (Join-Path $Repo 'seed\Codex.cdx') -ConnectTimeoutSec 30 -MemMB 2048
if (-not $irRun) { [Console]::Error.WriteLine("FAIL: seed VM did not start"); exit 3 }
try {
    if (-not (Read-VmReady -Conn $irRun.Conn -TimeoutSec 60)) {
        [Console]::Error.WriteLine("FAIL: seed not ready"); exit 3
    }
    $stream = $irRun.Conn.Data.GetStream()
    $stream.Write([System.Text.Encoding]::UTF8.GetBytes("IR-CCE`n"), 0, 7)
    $srcBytes = [System.IO.File]::ReadAllBytes($Src)
    $stream.Write($srcBytes, 0, $srcBytes.Length)
    $stream.WriteByte(4); $stream.Flush()
    Set-Content -Path $LogFile -Value '' -Encoding UTF8
    $irSize = 0
    while ($true) {
        $line = Read-StreamLine -Stream $stream -TimeoutSec 120
        if ($null -eq $line) { break }
        Add-Content -Path $LogFile -Value $line -Encoding UTF8
        if ($line.StartsWith('SIZE:')) { if ($line.Substring(5) -match '^\d+') { $irSize = [int]$matches[0] }; break }
        if ($line.StartsWith('CODEGEN-HALTED') -or $line.StartsWith('CODEGEN-ERRORS')) {
            while ($true) { $l2 = Read-StreamLine -Stream $stream -TimeoutSec 5; if ($null -eq $l2) { break }; Add-Content -Path $LogFile -Value $l2 -Encoding UTF8 }
            [Console]::Error.WriteLine("FAIL: IR compile failed; see $LogFile"); exit 4
        }
    }
    if ($irSize -le 0) { [Console]::Error.WriteLine("FAIL: no IR SIZE; see $LogFile"); exit 5 }
    $irBytes = Read-StreamBytes -Stream $stream -Count ($irSize + 1) -TimeoutSec 60
    if ($null -eq $irBytes) { [Console]::Error.WriteLine("FAIL: could not read IR bytes"); exit 5 }
    [System.IO.File]::WriteAllBytes($IrFile, $irBytes[0..($irSize - 1)])
} finally {
    Close-Vm -Conn $irRun.Conn -Process $irRun.Process
    Remove-Item -Force $irRun.StdoutFile, $irRun.StderrFile -ErrorAction SilentlyContinue
}

# ── Phase 2: IR text -> Elixir via plug ───────────────────────────────
$plugRun = Start-VmRun -Kernel $PlugCdx -ConnectTimeoutSec 30 -MemMB 2048
if (-not $plugRun) { [Console]::Error.WriteLine("FAIL: plug VM did not start"); exit 6 }
try {
    if (-not (Read-VmReady -Conn $plugRun.Conn -TimeoutSec 60)) {
        [Console]::Error.WriteLine("FAIL: plug not ready"); exit 6
    }
    $plugStream = $plugRun.Conn.Data.GetStream()
    # Send IR over TCP to plug
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 9100)
    $listener.Start()
    $deadline = (Get-Date).AddSeconds(30)
    while (-not $listener.Pending() -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 100 }
    if (-not $listener.Pending()) { [Console]::Error.WriteLine("FAIL: plug did not connect"); exit 7 }
    $client = $listener.AcceptTcpClient()
    $listener.Stop()
    $ns = $client.GetStream()
    $irData = [System.IO.File]::ReadAllBytes($IrFile)
    $msgLen = $irData.Length + 1
    $hdr = [BitConverter]::GetBytes([int]$msgLen)
    $ns.Write($hdr, 0, 4)
    $ns.WriteByte(1)
    $chunkSize = 4096
    $off = 0
    while ($off -lt $irData.Length) {
        $n = [Math]::Min($chunkSize, $irData.Length - $off)
        $ns.Write($irData, $off, $n)
        $ns.Flush()
        $off += $n
        if ($off -lt $irData.Length) { Start-Sleep -Milliseconds 50 }
    }
    # Read response
    $ns.ReadTimeout = 120000
    $resp = [System.Collections.Generic.List[byte]]::new()
    $buf = New-Object byte[] 65536
    try {
        while ($true) {
            $n = $ns.Read($buf, 0, $buf.Length)
            if ($n -le 0) { break }
            for ($bi = 0; $bi -lt $n; $bi++) { $resp.Add($buf[$bi]) }
        }
    } catch {}
    $client.Close()
    if ($resp.Count -eq 0) { [Console]::Error.WriteLine("FAIL: empty response from plug"); exit 8 }
    [System.IO.File]::WriteAllBytes($Out, $resp.ToArray())
    Write-Host "[elixir-plug] OK: $Out ($($resp.Count) bytes)"
} finally {
    Close-Vm -Conn $plugRun.Conn -Process $plugRun.Process
    Remove-Item -Force $plugRun.StdoutFile, $plugRun.StderrFile -ErrorAction SilentlyContinue
}
