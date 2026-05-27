# Run a single plug CDX. Boots the plug in a VM with NE2K NIC,
# listens on TCP 9100, sends the input, receives the output.
# Usage: build/run-plug.ps1 -Plug <plug.cdx> -Input <file> -Output <file>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string]$Plug,
    [Parameter(Mandatory=$true)] [string]$Input,
    [Parameter(Mandatory=$true)] [string]$Output,
    [int]$MemMB = 4096,
    [int]$TimeoutSec = 120,
    [int]$Port = 9100
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

if (-not (Test-Path -PathType Leaf $Plug)) {
    [Console]::Error.WriteLine("MISSING plug: $Plug"); exit 2
}
if (-not (Test-Path -PathType Leaf $Input)) {
    [Console]::Error.WriteLine("MISSING input: $Input"); exit 2
}

$inputBytes = [System.IO.File]::ReadAllBytes($Input)
Write-Host "[plug] Input: $($inputBytes.Length) bytes"
Write-Host "[plug] Plug: $Plug"

# Start TCP listener on port 9100
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
$listener.Start()
Write-Host "[plug] Listening on TCP $Port"

# Boot the plug VM with NIC enabled
$vmBin = Join-Path $Repo 'tools\codex-vm.exe'
$stderrFile = [System.IO.Path]::GetTempFileName()
$vmArgs = @('-kernel', $Plug, '-mem', $MemMB, '-headless')
$proc = Start-Process -FilePath $vmBin -ArgumentList $vmArgs -PassThru -WindowStyle Hidden -RedirectStandardError $stderrFile
Write-Host "[plug] VM PID $($proc.Id)"

try {
    # Wait for TCP connection from plug
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    while (-not $listener.Pending()) {
        if ($proc.HasExited) {
            $stderr = if (Test-Path $stderrFile) { Get-Content $stderrFile -Raw } else { "" }
            [Console]::Error.WriteLine("FAIL: VM exited before connecting. stderr: $stderr")
            exit 4
        }
        if ([DateTime]::UtcNow -gt $deadline) {
            [Console]::Error.WriteLine("FAIL: plug did not connect within 60s")
            exit 5
        }
        Start-Sleep -Milliseconds 100
    }
    $client = $listener.AcceptTcpClient()
    $stream = $client.GetStream()
    $listener.Stop()
    Write-Host "[plug] Connected"

    # Send input (raw bytes, no framing — plug reads until connection closes)
    $stream.Write($inputBytes, 0, $inputBytes.Length)
    $stream.Flush()
    # Half-close: signal we're done sending
    $client.Client.Shutdown([System.Net.Sockets.SocketShutdown]::Send)
    Write-Host "[plug] Sent $($inputBytes.Length) bytes, waiting for output..."

    # Receive output
    $stream.ReadTimeout = $TimeoutSec * 1000
    $allBytes = [System.Collections.Generic.List[byte]]::new(65536)
    $readBuf = [byte[]]::new(8192)
    try {
        while ($true) {
            $n = $stream.Read($readBuf, 0, $readBuf.Length)
            if ($n -le 0) { break }
            for ($bi = 0; $bi -lt $n; $bi++) { $allBytes.Add($readBuf[$bi]) }
        }
    } catch [System.IO.IOException] {
        # Read timeout or connection reset — normal end
    }

    $client.Close()

    if ($allBytes.Count -eq 0) {
        # Check serial output for diagnostics
        if (-not $proc.HasExited) { $proc.WaitForExit(5000) }
        $stderr = if (Test-Path $stderrFile) { Get-Content $stderrFile -Raw } else { "" }
        [Console]::Error.WriteLine("FAIL: plug produced no output. stderr: $stderr")
        exit 6
    }

    [System.IO.File]::WriteAllBytes($Output, $allBytes.ToArray())
    Write-Host "[plug] OK: $Output ($($allBytes.Count) bytes)"

} finally {
    if (-not $proc.HasExited) {
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    }
    Remove-Item -Force $stderrFile -ErrorAction SilentlyContinue
    try { $listener.Stop() } catch {}
}
