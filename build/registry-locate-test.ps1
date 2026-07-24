# A peer is DISCOVERED rather than named.
#
# quote-from-peer-test.ps1 proves a quotation resolves from a peer the compile
# was TOLD about (`-Peer host:port`). This proves it resolves from a peer the
# compile was never told about: the only address it is given is a registry's.
#
#   1. codex/test/apps/store-quoted-work writes one signed work onto a disk.
#   2. tools/cdx-registry boots. It takes no configuration and knows nobody.
#   3. tools/cdx-announce boots on that disk, reads the index, and tells the
#      registry that a given address holds those digests. Then it exits.
#      It runs BEFORE the server and while nothing else holds the disk --
#      codex-vm attaches an image read-write, so two guests on one image is not
#      a thing to attempt.
#   4. tools/cdx-serve boots on the disk and listens. Nothing in step 6 is ever
#      told this port.
#   5. The registry is asked to locate the digest and must name the peer.
#   6. codex/test/quote-from-peer is compiled with `-Registry` naming only the
#      registry, then run. sort-ascending exists only in the quoted work.
#
# THE CONTROLS ARE STEPS 7 AND 8 AND THEY ARE THE POINT. Steps 1-6 pass if the
# work reaches the compiler by ANY route, and the claim is that it reached it by
# discovery.
#
#   7. A SECOND registry, never announced to, must name nobody and the compile
#      against it must FAIL. This is the one that proves the announcement is
#      what carries the address: if that compile succeeded, the work was
#      arriving from somewhere else and the whole harness measures nothing.
#   8. A registry told about a peer that holds NOTHING must still name it -- an
#      announcement is a rumour and the registry does not verify it -- and the
#      compile must still fail, at the fetch. That is what keeps "the registry
#      names it" from being mistaken for "the work is trustworthy".
#
# Usage:
#   pwsh build/registry-locate-test.ps1
#   pwsh build/registry-locate-test.ps1 -Kernel build/output/Sut.cdx
#   pwsh build/registry-locate-test.ps1 -KeepArtifacts

[CmdletBinding()]
param(
    [string]$Kernel = 'seed/Codex.cdx',
    [int]$HostPort = 0,
    [switch]$KeepArtifacts
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
. (Join-Path $PSScriptRoot 'vm-config.ps1')
. (Join-Path $PSScriptRoot 'work-wire.ps1')

if ($HostPort -eq 0) { $HostPort = 19700 + (Get-Random -Min 0 -Max 150) }
$peerPort      = $HostPort        # the holder
$emptyPeerPort = $HostPort + 1    # a peer with an empty store
$regPort       = $HostPort + 2    # the registry that gets told
$regBarePort   = $HostPort + 3    # a registry nobody ever tells
$regLiarPort   = $HostPort + 4    # told about the peer that holds nothing

$out = 'test-output/registry-locate'
New-Item -ItemType Directory -Force $out | Out-Null

$fail = 0
function Check($what, $got, $want) {
    if ($got -eq $want) { Write-Host "  PASS  $what" }
    else { Write-Host "  FAIL  $what`n          got:  $got`n          want: $want"; $script:fail++ }
}

# A locate that got no answer is a FAILED CHECK, not a crashed script. Under
# StrictMode, reading .Count off the $null that means "no answer" throws and
# takes the whole harness down at the point of the first symptom, losing every
# later check and the cleanup that stops the VMs.
function LocateCount($r) { if ($null -eq $r) { return -1 } return $r.Count }

$writerCdx = "$out/store-quoted-work.cdx"
$serveCdx  = "$out/cdx-serve.cdx"
$regCdx    = "$out/cdx-registry.cdx"
$annCdx    = "$out/cdx-announce.cdx"
$disk      = "$out/peer-store.img"
$emptyDisk = "$out/empty-store.img"

$procs = @()

# DELETE BEFORE COMPILING. `Test-Path` on the output cannot tell a fresh binary
# from last run's: a compile that fails leaves the previous one in place and the
# check passes. That is not hypothetical -- it silently ran this harness against
# a stale registry binary and reported the guest never reaching its serve loop,
# when the diagnostics under test had never been compiled in. An artifact nobody
# deleted is not evidence.
function Compile-Server([string]$Src, [string]$OutCdx, [string]$LogName) {
    Remove-Item -Force $OutCdx -ErrorAction SilentlyContinue
    & (Join-Path $PSScriptRoot 'compile.ps1') -Src $Src `
        -Out $OutCdx -Log "$out/$LogName.log" -Kernel $Kernel | Out-Null
    if (-not (Test-Path $OutCdx)) {
        Write-Host "  FAIL  $Src did not compile"
        Select-String -Path "$out/$LogName.log" -Pattern ': error|CODEGEN-HALTED' |
            ForEach-Object { Write-Host "          $($_.Line)" }
        exit 1
    }
}

function Start-Serve([string]$Image, [int]$Port, [string]$Tag) {
    $p = Start-Process -FilePath $script:CodexVmBin -PassThru -WindowStyle Hidden `
        -ArgumentList @('-kernel', $serveCdx, '-disk', $Image, '-output', "$out/$Tag.out",
                        '-portfwd', "${Port}:9300", '-mem', '3072', '-headless') `
        -RedirectStandardError "$out/$Tag.err"
    $script:procs += $p
    return $p
}

# The registry takes NO configuration. It learns its peers by being told.
#
# A GUEST'S -output IS FLUSHED ON EXIT, so a killed VM's file holds only part of
# what the guest printed. Do not read a short output file as the guest having
# stopped there: that misreading, plus PowerShell unrolling an empty array on
# return, is what recorded two defects that did not exist.
function Start-Registry([int]$Port, [string]$Tag) {
    $p = Start-Process -FilePath $script:CodexVmBin -PassThru -WindowStyle Hidden `
        -ArgumentList @('-kernel', $regCdx, '-output', "$out/$Tag.out",
                        '-portfwd', "${Port}:9301", '-mem', '3072', '-headless') `
        -RedirectStandardError "$out/$Tag.err"
    $script:procs += $p
    return $p
}

# One shot: reads the disk, tells the registry, exits. Runs while nothing else
# holds the image.
function Invoke-Announce([string]$Image, [int]$RegPort, [int]$AdvertisePort, [string]$Tag) {
    $inFile = "$out/$Tag.in"
    [System.IO.File]::WriteAllText((Join-Path (Get-Location) $inFile),
        "127.0.0.1:$RegPort 127.0.0.1:$AdvertisePort`n")
    $p = Start-Process -FilePath $script:CodexVmBin -PassThru -WindowStyle Hidden `
        -ArgumentList @('-kernel', $annCdx, '-disk', $Image, '-input', $inFile,
                        '-output', "$out/$Tag.out", '-mem', '3072', '-headless') `
        -RedirectStandardError "$out/$Tag.err"
    $p.WaitForExit(180000) | Out-Null
    if (-not $p.HasExited) { try { $p.Kill() } catch {} ; Write-Host '  FAIL  cdx-announce did not exit'; exit 1 }
    $said = (Get-Content "$out/$Tag.out" -Raw -ErrorAction SilentlyContinue)
    Write-Host "  announce: $(($said -replace "`r", '').Trim() -split "`n" | Select-Object -Last 1)"
    return $said
}

# A guest takes ~20s to boot before it listens, and codex-vm's port forward
# accepts the host connection long before the guest is behind it -- so an early
# ask blocks on a socket nobody is reading. Ask for something nobody has: any
# answer at all means it is up.
#
# Do not read the .out file to decide this. codex-vm flushes -output on exit, so
# a running server's output file is empty however healthy it is.
# GIVE THE GUEST ITS BOOT BEFORE KNOCKING. A guest takes ~20 s to reach its
# listener, and every ask made before then is a connection codex-vm's port
# forward accepts, allocates a NAT slot for, and never gets to hand over.
# Abandoned slots stay live, so knocking through the boot window exhausted the
# 64-slot table and every later frame was dropped -- a self-inflicted failure
# that looks exactly like a broken server.
$script:GuestBootSeconds = 22

function Wait-Serve([int]$Port) {
    Start-Sleep -Seconds $script:GuestBootSeconds
    if ($null -eq (Invoke-WorkAsk -HostName '127.0.0.1' -Port $Port -Hash ('0' * 64) -TimeoutSec 180)) {
        Write-Host "  FAIL  the peer on :$Port never came up"; exit 1
    }
}

# Readiness probe, restored 2026-07-20. It was deleted because it MANUFACTURED
# the failure it was meant to avoid: a fresh listener has remote-port 0 and so
# accepts ANY SYN, including one left over from an abandoned probe, and having
# bound to that dead peer it then correctly filters the live client's data out by
# source port. Symptom: the first locate after boot always worked and the second
# never did.
#
# Two fixes since have removed the mechanism, both on main:
#   * 9710 (tcp-listen-reclaim) -- a listener that served one caller no longer
#     carries the old remote-port forward into its next LISTEN, so it cannot
#     refuse the next caller for naming a different source port.
#   * 9864 (codex-vm proper TCP) -- a forwarded connection whose host client has
#     gone away is reaped on the MSG_PEEK==0 orderly close instead of sitting in
#     the NAT table with a pending SYN for the guest to accept later.
#
# It is deliberately ONE ask, matching Wait-Serve, not a retry loop: a single
# connection is what keeps this from re-creating the stale-connection hazard, and
# the long timeout is what absorbs a slow boot. The blind sleep it replaces is
# what let step 3 fire a locate at a registry that was not accepting yet, and be
# answered on a connection the host had already timed out and closed
# (`PORTFWD recv: n=0 err=0 state=2`).
#
# If the probe is ever suspected of breaking the request after it again, step 3
# is the detector: it asks the same question the probe does and demands 0.
function Wait-Registry([int]$Port) {
    Start-Sleep -Seconds $script:GuestBootSeconds
    if ($null -eq (Invoke-WorkLocate -HostName '127.0.0.1' -Port $Port -Hash ('0' * 64) -TimeoutSec 180)) {
        Write-Host "  FAIL  the registry on :$Port never came up"; exit 1
    }
}

function Try-Compile([string]$Reg, [string]$OutCdx, [string]$LogName) {
    Remove-Item -Force $OutCdx -ErrorAction SilentlyContinue
    & (Join-Path $PSScriptRoot 'compile.ps1') -Src 'codex/test/quote-from-peer.codex' `
        -Out $OutCdx -Log "$out/$LogName.log" -Kernel $Kernel -Registry $Reg 2>$null | Out-Null
    return (Test-Path $OutCdx)
}

try {
    Write-Host '--- 1. write one signed work onto the peer disk ---'
    Compile-Server 'codex/test/apps/store-quoted-work.codex' $writerCdx 'store-quoted-work'
    [System.IO.File]::WriteAllBytes((Join-Path (Get-Location) $disk), (New-Object byte[] 1048576))
    $w = Start-Process -FilePath $script:CodexVmBin -PassThru -WindowStyle Hidden `
        -ArgumentList @('-kernel', $writerCdx, '-output', "$out/writer.out", '-disk', $disk, '-mem', '3072', '-headless')
    $w.WaitForExit(120000) | Out-Null
    if (-not $w.HasExited) { try { $w.Kill() } catch {}; Write-Host '  FAIL  the store writer VM did not exit'; exit 1 }
    if ((Get-Content "$out/writer.out" -Raw -ErrorAction SilentlyContinue) -notmatch 'stored') {
        Write-Host '  FAIL  the store writer did not report it stored the work'; exit 1
    }
    [System.IO.File]::WriteAllBytes((Join-Path (Get-Location) $emptyDisk), (New-Object byte[] 1048576))
    Write-Host '  a signed work is on the peer disk'

    Write-Host '--- 2. compile the three tools ---'
    Compile-Server 'tools/cdx-serve.codex'    $serveCdx 'cdx-serve'
    Compile-Server 'tools/cdx-registry.codex' $regCdx   'cdx-registry'
    Compile-Server 'tools/cdx-announce.codex' $annCdx   'cdx-announce'

    Write-Host "--- 3. boot the registry (:$regPort), which knows nobody ---"
    Start-Registry -Port $regPort -Tag 'registry' | Out-Null
    Wait-Registry -Port $regPort
    # @() OR THIS ASKS FOR THE DIGIT 7. Get-QuotedHashes builds an array and
    # `return $found` unrolls a single element to a bare String, so `[0]`
    # indexes the STRING and yields its first character. The registry was then
    # announced the real digest and asked for "7", which no announcement can
    # ever match -- step 5 could not pass however well the transport worked.
    # Same unrolling trap as the one Invoke-WorkLocate already carries a note
    # about below; it is worth assuming for every function in work-wire.ps1
    # that returns a list.
    $hash = @(Get-QuotedHashes (Get-Content 'codex/test/quote-from-peer.codex'))[0]
    # ASSIGN, THEN COUNT. Invoke-WorkLocate returns `,@()` so that an empty
    # result survives as an empty array rather than collapsing to $null, but
    # that makes `@(Invoke-WorkLocate ...)` collect ONE object -- the empty
    # array itself -- and report Count 1. Through a variable it is 0. Every
    # count in this file goes through a variable for that reason.
    $none = Invoke-WorkLocate -HostName '127.0.0.1' -Port $regPort -Hash $hash -TimeoutSec 180
    Check 'a registry nobody has told knows nobody' (LocateCount $none) 0

    Write-Host '--- 4. announce the store to it ---'
    Invoke-Announce -Image $disk -RegPort $regPort -AdvertisePort $peerPort -Tag 'announce' | Out-Null

    Write-Host '--- 5. the registry now names the holder ---'
    $who = Invoke-WorkLocate -HostName '127.0.0.1' -Port $regPort -Hash $hash -TimeoutSec 180
    Check 'it names exactly one holder' (LocateCount $who) 1
    Check 'and it is the address that was announced' ($who.Peers[0]) "127.0.0.1:$peerPort"

    Write-Host "--- 6. boot the holder (:$peerPort), compile against the registry alone, run it ---"
    Start-Serve -Image $disk -Port $peerPort -Tag 'serve' | Out-Null
    Wait-Serve -Port $peerPort
    $quoteCdx = "$out/quote-from-registry.cdx"
    $built = Try-Compile "127.0.0.1:$regPort" $quoteCdx 'quote-from-registry'
    Check 'a quotation resolves without the holder being named' $built $true
    if ($built) {
        $runOut = "$out/quote-from-registry.out"
        & (Join-Path $PSScriptRoot 'test-run.ps1') -Kernel $quoteCdx -OutFile $runOut 2>$null | Out-Null
        $actual = ((Get-Content $runOut -Raw -ErrorAction SilentlyContinue) -replace "`r", '').Trim()
        Check 'the program computes with the discovered function' $actual 'sorted=105'
    }

    Write-Host '--- 7. control: a registry nobody ever told ---'
    Start-Registry -Port $regBarePort -Tag 'registry-bare' | Out-Null
    Wait-Registry -Port $regBarePort
    $bare = Invoke-WorkLocate -HostName '127.0.0.1' -Port $regBarePort -Hash $hash -TimeoutSec 180
    Check 'it names nobody' (LocateCount $bare) 0
    Check 'and the compile fails' (Try-Compile "127.0.0.1:$regBarePort" "$out/miss-bare.cdx" 'miss-bare') $false

    Write-Host '--- 8. control: told about a peer that holds nothing ---'
    Start-Registry -Port $regLiarPort -Tag 'registry-liar' | Out-Null
    Wait-Registry -Port $regLiarPort
    # Announce the EMPTY store's (absent) contents under the empty peer's
    # address, then hand the registry the real digest by announcing it from the
    # real disk under the WRONG address. The registry does not verify either.
    Invoke-Announce -Image $disk -RegPort $regLiarPort -AdvertisePort $emptyPeerPort -Tag 'announce-liar' | Out-Null
    Start-Serve -Image $emptyDisk -Port $emptyPeerPort -Tag 'serve-empty' | Out-Null
    Wait-Serve -Port $emptyPeerPort
    $liar = Invoke-WorkLocate -HostName '127.0.0.1' -Port $regLiarPort -Hash $hash -TimeoutSec 180
    Check 'the registry repeats the rumour it was told' (LocateCount $liar) 1
    Check 'and the compile still fails, at the fetch' (Try-Compile "127.0.0.1:$regLiarPort" "$out/miss-liar.cdx" 'miss-liar') $false
} finally {
    foreach ($p in $procs) {
        if ($p -and -not $p.HasExited) { Stop-VmGraceful -ProcessId $p.Id }
    }
    if (-not $KeepArtifacts) {
        Remove-Item -Force $disk -ErrorAction SilentlyContinue
        Remove-Item -Force $emptyDisk -ErrorAction SilentlyContinue
    }
}

Write-Host ''
if ($fail -gt 0) { Write-Host "registry-locate-test: $fail FAILED" -ForegroundColor Red; exit 1 }
Write-Host 'PASS: a quotation was resolved from a peer nobody named.' -ForegroundColor Green
exit 0
