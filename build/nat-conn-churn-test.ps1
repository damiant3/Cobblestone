# nat-conn-churn-test.ps1 -- does the NAT give a closed connection's slot back?
#
# codex-vm's connection table holds 64 entries. A connection the guest closed
# went to state 3 and stopped there: nat_poll_rx skipped that state, so the
# socket was never read again, never closed, and the slot was never freed.
# nat_alloc only reuses a slot whose `active` flag is clear, and state 3 leaves
# it set -- so 64 ordinary closes exhausted the table and every connection
# after that was dropped in silence.
#
# This is the instrument that failure was waiting for, and it is why 4.16 sat
# open: cdx-serve-test makes ONE connection, so it passes whether or not the
# slot comes back. The question here is not "does a request work" but "does
# the sixty-fifth request work", and only a churn well past the table size can
# ask it.
#
# The count is deliberately above 64 and not a round number near it: 80 leaves
# no doubt that the table wrapped, and the failure it is looking for is sharp
# -- before the fix every request from the 65th on times out.
#
# Usage:
#   pwsh build/nat-conn-churn-test.ps1
#   pwsh build/nat-conn-churn-test.ps1 -Connections 80
#   pwsh build/nat-conn-churn-test.ps1 -Vm build-output/codex-vm-prechange.exe   # negative control

[CmdletBinding()]
param(
    [string]$Kernel = 'seed/Codex.cdx',
    [int]$HostPort = 0,
    [int]$Connections = 80,
    [string]$Vm = '',
    [switch]$KeepArtifacts
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
. (Join-Path $PSScriptRoot 'vm-config.ps1')
. (Join-Path $PSScriptRoot 'work-wire.ps1')

if ($HostPort -eq 0) { $HostPort = 19800 + (Get-Random -Min 0 -Max 150) }
$out = 'build-output'
New-Item -ItemType Directory -Force $out | Out-Null
$vmBin = if ($Vm) { $Vm } else { $script:CodexVmBin }

Write-Host "nat-conn-churn: compiling cdx-serve with $Kernel"
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'compile.ps1') `
    -Src 'tools/cdx-serve.codex' -Out "$out/nc-serve.cdx" -Log "$out/nc-serve.log" -Kernel $Kernel *>$null
if (-not (Test-Path "$out/nc-serve.cdx")) { Write-Host "  FAIL  cdx-serve did not compile"; exit 1 }

$disk = "$out/nc-store.img"
Remove-Item -Force $disk -ErrorAction SilentlyContinue
$work = "$out/nc-served.codex"
[System.IO.File]::WriteAllText((Join-Path (Get-Location) $work),
    "Chapter: ChurnWork`n`n We say:`n`nSection: Entry`n`n  answer : Integer`n  answer = 7`n",
    [System.Text.UTF8Encoding]::new($false))

$storeOut = & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'store-source.ps1') `
    -Src $work -Disk $disk -Path 'churn.codex' -Quire 'Test' -Chapter 'ChurnWork' -Kernel $Kernel 2>&1 | Out-String
$hash = $null
if ($storeOut -match 'stored\s+([0-9a-fA-F]{64})') { $hash = $matches[1] }
if (-not $hash) { Write-Host "  FAIL  cdx-store did not report a hash"; exit 1 }

Write-Host "nat-conn-churn: booting server on $vmBin (host :$HostPort -> guest :9300)"
$proc = Start-Process -FilePath $vmBin -PassThru -WindowStyle Hidden `
    -ArgumentList @('-kernel', "$out/nc-serve.cdx", '-disk', $disk, '-output', "$out/nc-serve.out",
                    '-portfwd', "${HostPort}:9300", '-mem', '3072', '-headless') `
    -RedirectStandardError "$out/nc-serve.err"

# One request on one connection, closed afterwards. Returns $true on a
# well-formed reply. The short per-attempt timeout is the point: once the table
# is exhausted the guest never sees the connection at all, so the failure is a
# timeout rather than a refusal.
function Ask-Once([string]$h, [int]$timeoutMs) {
    $c = $null
    try {
        $c = [System.Net.Sockets.TcpClient]::new()
        $iar = $c.BeginConnect('127.0.0.1', $HostPort, $null, $null)
        if (-not $iar.AsyncWaitHandle.WaitOne($timeoutMs)) { return $false }
        $c.EndConnect($iar)
        $s = $c.GetStream()
        $s.ReadTimeout = $timeoutMs
        $req = New-WorkRequestFrame $h
        $s.Write($req, 0, $req.Length); $s.Flush()
        $hdr = New-Object byte[] 5
        $n = 0
        while ($n -lt 5) { $r = $s.Read($hdr, $n, 5 - $n); if ($r -le 0) { return $false }; $n += $r }
        $total = Read-Le32 $hdr 0
        $rest = New-Object byte[] ($total - 1)
        $n = 0
        while ($n -lt $rest.Length) { $r = $s.Read($rest, $n, $rest.Length - $n); if ($r -le 0) { return $false }; $n += $r }
        return ($hdr[4] -eq 18)
    } catch { return $false }
    finally { if ($c) { $c.Dispose() } }
}

try {
    # The peer takes ~20 s to boot and index, and the port forward accepts the
    # host connection long before the guest is behind it -- so an early ask
    # blocks on a socket nobody is reading. Wait for a real answer first.
    Write-Host "nat-conn-churn: waiting for the server to come up"
    $ready = $false
    $deadline = (Get-Date).AddSeconds(90)
    while ((Get-Date) -lt $deadline) {
        if (Ask-Once $hash 5000) { $ready = $true; break }
        Start-Sleep -Milliseconds 700
    }
    if (-not $ready) { Write-Host "  FAIL  the server never answered a first request"; exit 1 }

    Write-Host "nat-conn-churn: $Connections sequential connections, each closed"
    $ok = 0
    $firstFail = 0
    for ($i = 1; $i -le $Connections; $i++) {
        if (Ask-Once $hash 8000) { $ok++ }
        elseif ($firstFail -eq 0) { $firstFail = $i }
    }

    Write-Host ""
    Write-Host "  answered: $ok / $Connections"
    if ($firstFail -gt 0) { Write-Host "  first failure at connection $firstFail" }
    if ($ok -eq $Connections) {
        Write-Host "  PASS  the table gives closed slots back"
        exit 0
    } else {
        Write-Host "  FAIL  the table stopped answering -- closed connections are not being freed"
        exit 1
    }
}
finally {
    if ($proc -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
    if (-not $KeepArtifacts) {
        Remove-Item -Force "$out/nc-store.img","$out/nc-served.codex" -ErrorAction SilentlyContinue
    }
}
