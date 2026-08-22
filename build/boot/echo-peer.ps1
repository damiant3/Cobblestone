# The echo peer a diagnostic sitting dials, and the register of what dialled it.
#
# DiagB3's conversation is RAW TCP and not the repository wire, so a cdx-serve
# peer answers it nothing and the row reads `no-reply` -- which is
# indistinguishable from a real failure (blu, 2026-08-20). This is the peer
# that answers: it echoes every byte back unchanged, which is what lets the
# stage compare `sent=` against `rx=` and say the conversation completed
# rather than merely started.
#
# IT IS ALSO THE REGISTER. The stick has no way to tell us what address it
# ended up with -- our stack does not answer a ping (`icmp-parse` has no
# production caller anywhere outside tests, and there is no
# `net-process-icmp` at all; Decision 1, deliberate), so pinging the box
# proves nothing about the network in either direction. The CONNECTION is the
# proof: when the stick dials in, this records the source address it dialled
# from, and that is how we learn what the box is running as. A ping would only
# have tested ICMP, which we do not implement; this tests link, ARP, IP, TCP
# and a payload returned byte for byte.
#
# Port 7 is the well-known echo port (RFC 862). It is deliberately not an
# invented number: a flight card that says `peer=<ip>:7` explains itself.
#
# Usage:
#   build/boot/echo-peer.ps1                      # listen on 0.0.0.0:7
#   build/boot/echo-peer.ps1 -Port 10007          # somewhere else
#   build/boot/echo-peer.ps1 -Seconds 900         # stop by itself
#
# Windows Firewall blocks inbound on a fresh port. This script does NOT touch
# the firewall -- opening a port is a change to the machine and belongs to
# whoever owns it. It PRINTS the rule to add and whether one already exists.
[CmdletBinding()]
param(
    [int]$Port = 7,
    [int]$Seconds = 0,
    [string]$Log = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $Log) {
    $repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $Log = Join-Path $repo 'build-output\echo-peer.log'
}
$logDir = Split-Path $Log -Parent
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Force $logDir | Out-Null }

function Say([string]$m) {
    $line = "{0}  {1}" -f (Get-Date -Format 'HH:mm:ss'), $m
    Write-Host $line
    Add-Content -Path $Log -Value $line
}

$rule = Get-NetFirewallRule -DisplayName "Codex diag echo $Port" -ErrorAction SilentlyContinue
if (-not $rule) {
    Write-Host "NOTE: no firewall rule named 'Codex diag echo $Port' exists."
    Write-Host "      Inbound connections will be BLOCKED until one does. To add it, elevated:"
    Write-Host "      New-NetFirewallRule -DisplayName 'Codex diag echo $Port' -Direction Inbound ``"
    Write-Host "          -Protocol TCP -LocalPort $Port -Action Allow -Profile Private"
    Write-Host ""
}

$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Any, $Port)
try { $listener.Start() }
catch { Write-Host "FAIL: cannot listen on $Port -- $($_.Exception.Message)"; exit 1 }

Say "echo peer listening on 0.0.0.0:$Port  (log: $Log)"
Say "waiting for the stick to dial in; its SOURCE ADDRESS is the registration"

$deadline = if ($Seconds -gt 0) { (Get-Date).AddSeconds($Seconds) } else { [DateTime]::MaxValue }
$conns = 0
try {
    while ((Get-Date) -lt $deadline) {
        if (-not $listener.Pending()) { Start-Sleep -Milliseconds 200; continue }
        $client = $listener.AcceptTcpClient()
        $conns++
        $remote = $client.Client.RemoteEndPoint
        Say "CONNECTION $conns from $remote   <-- the box is at $($remote.Address)"
        try {
            $stream = $client.GetStream()
            $stream.ReadTimeout = 5000
            $buf = New-Object byte[] 4096
            $total = 0
            while ($true) {
                $n = 0
                try { $n = $stream.Read($buf, 0, $buf.Length) } catch { break }
                if ($n -le 0) { break }
                $total += $n
                $stream.Write($buf, 0, $n)
                $stream.Flush()
                $txt = ([Text.Encoding]::ASCII.GetString($buf, 0, $n)) -replace '[^\x20-\x7e]', '.'
                Say "  echoed $n bytes (total $total): [$txt]"
            }
            Say "  closed, $total bytes echoed back unchanged"
        } finally { $client.Close() }
    }
} finally {
    $listener.Stop()
    Say "echo peer stopped after $conns connection(s)"
}
