# cdx-serve-test.ps1 -- the first test that drives a Codex TCP server.
#
# Nothing in the tree tested a guest server before this. IdeaServer, WebServer and
# ExplorerServer are all driven by demo scripts and by nothing else, which is how
# WebServer came to have six undeclared-effect-row errors that no battery could
# see. A server that is only ever run by hand is a server nobody is checking.
#
# This boots the real ingest tool and the real server and asks the real question:
#
#   tools/cdx-store  stores one work on a blank disk and PRINTS its hash
#   tools/cdx-serve  boots on that same disk and listens on guest port 9300
#   this script      connects over -portfwd and asks for that hash by wire
#
# The hash is never hardcoded. It is read back out of cdx-store's own output, so
# a change to the addressing rule moves both ends together and this test keeps
# asking the right question instead of asserting a stale constant.
#
# THE WIRE IS CCE, AND THAT IS THE TRAP. frame-encode-text calls char-code, which
# gives the CCE code point and not the ASCII one, so the hash's own hex digits go
# out as CCE bytes. Sending ASCII here does not error -- the server simply never
# finds the work and answers "not here", which reads exactly like a working
# server with an empty store. Encode through UnicodeToCce (vm-config.ps1).
#
# Usage:
#   pwsh build/cdx-serve-test.ps1
#   pwsh build/cdx-serve-test.ps1 -Kernel seed/Codex.cdx   # compile with a chosen seed

[CmdletBinding()]
param(
    [string]$Kernel = 'seed/Codex.cdx',
    [int]$HostPort = 0,
    [switch]$KeepArtifacts,
    # Extra codex-vm flags, for running this same conversation over a
    # different card. Empty is the NE2000 and every historical run.
    [string[]]$VmArgs = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
. (Join-Path $PSScriptRoot 'vm-config.ps1')
# The wire this test spoke privately now has one home, because compile.ps1 asks
# the same question for the quotation client half, and a second hand-rolled copy
# is how a transport ends up with 41 of them.
. (Join-Path $PSScriptRoot 'work-wire.ps1')

if ($HostPort -eq 0) { $HostPort = 19300 + (Get-Random -Min 0 -Max 400) }
$out = 'build-output'
New-Item -ItemType Directory -Force $out | Out-Null

$fail = 0
function Check($what, $got, $want) {
    if ($got -eq $want) { Write-Host "  PASS  $what" }
    else { Write-Host "  FAIL  $what`n          got:  $got`n          want: $want"; $script:fail++ }
}

# The CCE helpers, the frame codec and the request builder live in
# build/work-wire.ps1, dot-sourced above.

# --- 1. Compile the server under test ----------------------------------------
Write-Host "cdx-serve-test: compiling with $Kernel"
& (Join-Path $PSScriptRoot 'compile.ps1') -Src 'tools/cdx-serve.codex' -Out "$out/cs-serve.cdx" -Log "$out/cs-serve.cdx.log" -Kernel $Kernel | Out-Null
if (-not (Test-Path "$out/cs-serve.cdx")) { Write-Host "  FAIL  tools/cdx-serve.codex did not compile"; exit 1 }

# --- 2. Seed the store with the REAL ingest tool ------------------------------
# store-source.ps1 drives cdx-store and knows the wire: header line, source, then
# a NUL, because read-serial-cce stops on the NUL and blocks forever without one.
# Reusing it is the point -- this test stores a work the way a person stores one.
$disk = "$out/cs-store.img"
Remove-Item -Force $disk -ErrorAction SilentlyContinue
$work = "$out/cs-served.codex"
[System.IO.File]::WriteAllText((Join-Path (Get-Location) $work),
    "Chapter: ServedWork`n`n We say:`n`nSection: Entry`n`n  answer : Integer`n  answer = 42`n",
    [System.Text.UTF8Encoding]::new($false))

$storeOut = & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'store-source.ps1') `
    -Src $work -Disk $disk -Path 'served.codex' -Quire 'Test' -Chapter 'ServedWork' -Kernel $Kernel 2>&1 | Out-String
Write-Host "cdx-store said: $($storeOut.Trim())"
$hash = $null
if ($storeOut -match 'stored\s+([0-9a-fA-F]{64})') { $hash = $matches[1] }
if (-not $hash) { Write-Host "  FAIL  cdx-store did not report a hash; nothing to ask for"; exit 1 }

# --- 3. Boot the server on that disk -----------------------------------------
Write-Host "cdx-serve-test: booting server (host :$HostPort -> guest :9300)"
$srvOut = "$out/cs-serve.out"
$proc = Start-Process -FilePath $script:CodexVmBin -PassThru -WindowStyle Hidden `
    -ArgumentList (@('-kernel', "$out/cs-serve.cdx", '-disk', $disk, '-output', $srvOut,
                     '-portfwd', "${HostPort}:9300", '-mem', '3072', '-headless') + $VmArgs) `
    -RedirectStandardError "$out/cs-serve.err"

function Ask([string]$h, [int]$timeoutSec = 60) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        $c = $null
        try {
            $c = [System.Net.Sockets.TcpClient]::new()
            $c.Connect('127.0.0.1', $HostPort)
            $s = $c.GetStream()
            $s.ReadTimeout = 30000
            $req = New-WorkRequestFrame $h
            $s.Write($req, 0, $req.Length)
            $s.Flush()
            $hdr = New-Object byte[] 5
            $n = 0
            while ($n -lt 5) { $r = $s.Read($hdr, $n, 5 - $n); if ($r -le 0) { throw 'short header' }; $n += $r }
            $total = Read-Le32 $hdr 0
            $tag = $hdr[4]
            $rest = New-Object byte[] ($total - 1)
            $n = 0
            while ($n -lt $rest.Length) { $r = $s.Read($rest, $n, $rest.Length - $n); if ($r -le 0) { throw 'short body' }; $n += $r }
            $c.Dispose()
            return @{ Tag = $tag; Body = $rest }
        } catch {
            if ($c) { $c.Dispose() }
            Start-Sleep -Milliseconds 700
        }
    }
    return $null
}

try {
    Write-Host "`n--- a peer asks for a work it was told the digest of ---"
    $r = Ask $hash
    if (-not $r) { Write-Host "  FAIL  the server never answered"; exit 1 }

    Check "reply carries tag-work-reply (18)" $r.Tag 18

    # decode-work-reply-body: bytes(pubkey) ++ text(hash) ++ text(payload).
    # Decoded here by hand rather than through work-wire's Invoke-WorkAsk: this
    # test's job is to pin the wire, and a test that reads the bytes through the
    # same client the shipping code uses cannot see the two disagree.
    $o = 0
    $pkLen = Read-Le32 $r.Body $o; $o += 4 + $pkLen
    $hLen = Read-Le32 $r.Body $o; $o += 4
    $rHash = ConvertFrom-CceBytes $r.Body[$o..($o + $hLen - 1)]; $o += $hLen
    $pLen = Read-Le32 $r.Body $o; $o += 4
    $payload = if ($pLen -gt 0) { ConvertFrom-CceBytes $r.Body[$o..($o + $pLen - 1)] } else { '' }

    Check "reply answers the hash that was asked" $rHash $hash
    Check "reply is not empty" ($pLen -gt 0) $true
    if ($VerbosePreference -ne 'SilentlyContinue' -or $KeepArtifacts) {
        Write-Host "  [payload $pLen chars, frame body $($r.Body.Length) bytes]"
        [System.IO.File]::WriteAllText((Join-Path (Get-Location) "$out/cs-payload.txt"), $payload)
        Write-Host "  [wrote $out/cs-payload.txt]"
    }
    Check "the served work carries the stored source" ($payload -match 'answer = 42') $true
    Check "the served work names its path" ($payload -match 'served\.codex') $true

    Write-Host "`n--- a peer asks for a hash nobody stored ---"
    $miss = '0' * 64
    $r2 = Ask $miss
    if (-not $r2) { Write-Host "  FAIL  the server never answered the miss"; exit 1 }
    $o = 0
    $pkLen = Read-Le32 $r2.Body $o; $o += 4 + $pkLen
    $hLen = Read-Le32 $r2.Body $o; $o += 4 + $hLen
    $pLen = Read-Le32 $r2.Body $o
    Check "a miss is an empty payload, not an error" $pLen 0
} finally {
    if ($proc -and -not $proc.HasExited) { Stop-VmGraceful -ProcessId $proc.Id }
    if (-not $KeepArtifacts) {
        Remove-Item -Force "$out/cs-served.codex" -ErrorAction SilentlyContinue
    }
}

Write-Host ""
if ($fail -gt 0) { Write-Host "cdx-serve-test: $fail FAILED"; exit 1 }
Write-Host "cdx-serve-test: all checks passed"
exit 0
