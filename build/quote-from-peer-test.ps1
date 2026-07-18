# BACKLOG 6.2 closed end to end: a quotation resolves from a PEER.
#
# The store half (6.1) put the work on a disk this machine could reach. This
# puts it on a machine this one can only talk to:
#
#   1. codex/test/apps/store-quoted-work writes one signed, content-addressed
#      work onto a blank disk -- the same work, digest, signer and signature
#      codex/test/quotes-gate proves through the blob, so the only new thing
#      under test is the transport.
#   2. tools/cdx-serve boots on that disk and listens. The disk is the PEER's;
#      the compile in step 3 never sees it.
#   3. codex/test/quote-from-peer QUOTES that work by digest with NO blob and NO
#      store attached, compiled with `compile.ps1 -Peer`. The host asks the peer
#      for the digest, prepends the answer to the %%QUOTED-WORKS%% blob, and the
#      compiler resolves it through the same four guards.
#   4. The binary is run. sort-ascending comes only from the quoted work, so
#      sorted=105 means the definition crossed a socket to get here.
#
# THE SENSITIVITY RUN IS NOT OPTIONAL AND IT IS STEP 5. Steps 1-4 pass if the
# work reaches the compiler by ANY route, and the whole claim is that it reached
# it by this one. So the last step compiles the same source against a peer that
# holds nothing, and it MUST fail. A test that cannot fail proves nothing, and
# "the quotation resolved" is exactly the shape that looks identical whether the
# fetch worked or the work was lying around somewhere else.
#
# Usage:
#   pwsh build/quote-from-peer-test.ps1
#   pwsh build/quote-from-peer-test.ps1 -Kernel build/output/Sut.cdx
#   pwsh build/quote-from-peer-test.ps1 -KeepArtifacts

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

if ($HostPort -eq 0) { $HostPort = 19700 + (Get-Random -Min 0 -Max 200) }
$out = 'test-output/quote-from-peer'
New-Item -ItemType Directory -Force $out | Out-Null

$fail = 0
function Check($what, $got, $want) {
    if ($got -eq $want) { Write-Host "  PASS  $what" }
    else { Write-Host "  FAIL  $what`n          got:  $got`n          want: $want"; $script:fail++ }
}

$writerCdx = "$out/store-quoted-work.cdx"
$serveCdx  = "$out/cdx-serve.cdx"
$quoteCdx  = "$out/quote-from-peer.cdx"
$disk      = "$out/peer-store.img"
$emptyDisk = "$out/empty-store.img"

$procs = @()
function Start-Peer([string]$Image, [int]$Port, [string]$Tag) {
    $p = Start-Process -FilePath $script:CodexVmBin -PassThru -WindowStyle Hidden `
        -ArgumentList @('-kernel', $serveCdx, '-disk', $Image, '-output', "$out/$Tag.out",
                        '-portfwd', "${Port}:9300", '-mem', '3072', '-headless') `
        -RedirectStandardError "$out/$Tag.err"
    $script:procs += $p
    return $p
}

# A peer takes ~20s to boot and index before it listens, and codex-vm's port
# forward accepts the host connection long before the guest is behind it -- so
# an early ask does not fail fast, it blocks on a socket nobody is reading and
# burns the whole read timeout. Wait for the peer to answer something before
# asking it for anything that matters. A hash nobody stored is the cheapest
# question: any answer at all means the server is up, and an empty payload is
# what "not here" looks like rather than an error.
#
# Do not read $Tag.out to decide this. codex-vm flushes -output when the VM
# exits, so a running server's output file is empty however healthy it is; that
# read said "the server never booted" about a server that was serving fine.
function Wait-Peer([int]$Port) {
    Write-Host '  waiting for the peer to index and listen'
    $ready = Invoke-WorkAsk -HostName '127.0.0.1' -Port $Port -Hash ('0' * 64) -TimeoutSec 180
    if ($null -eq $ready) { Write-Host "  FAIL  the peer on :$Port never came up"; exit 1 }
    Write-Host '  the peer is listening'
}

try {
    Write-Host "--- 1. write one signed work onto the peer's disk ---"
    & (Join-Path $PSScriptRoot 'compile.ps1') -Src 'codex/test/apps/store-quoted-work.codex' `
        -Out $writerCdx -Log "$out/store-quoted-work.log" -Kernel $Kernel | Out-Null
    if (-not (Test-Path $writerCdx)) { Write-Host "  FAIL  the store writer did not compile"; exit 1 }
    # codex-vm is attached to this image DIRECTLY: its IDE writes flush durably
    # to the file, so the work is still here when the server boots on it.
    # test-run.ps1 copies a disk to a throwaway temp and cannot be used to write
    # a store that has to survive the boot.
    [System.IO.File]::WriteAllBytes((Join-Path (Get-Location) $disk), (New-Object byte[] 1048576))
    $w = Start-Process -FilePath $script:CodexVmBin -PassThru -WindowStyle Hidden `
        -ArgumentList @('-kernel', $writerCdx, '-output', "$out/writer.out", '-disk', $disk, '-mem', '3072', '-headless')
    $w.WaitForExit(120000) | Out-Null
    if (-not $w.HasExited) { try { $w.Kill() } catch {}; Write-Host '  FAIL  the store writer VM did not exit'; exit 1 }
    if ((Get-Content "$out/writer.out" -Raw -ErrorAction SilentlyContinue) -notmatch 'stored') {
        Write-Host '  FAIL  the store writer did not report it stored the work'; exit 1
    }
    Write-Host '  a signed work is on the peer disk'

    Write-Host "--- 2. boot the peer on it (host :$HostPort -> guest :9300) ---"
    & (Join-Path $PSScriptRoot 'compile.ps1') -Src 'tools/cdx-serve.codex' `
        -Out $serveCdx -Log "$out/cdx-serve.log" -Kernel $Kernel | Out-Null
    if (-not (Test-Path $serveCdx)) { Write-Host '  FAIL  tools/cdx-serve.codex did not compile'; exit 1 }
    Start-Peer -Image $disk -Port $HostPort -Tag 'serve' | Out-Null
    Wait-Peer -Port $HostPort

    Write-Host '--- 3. compile a source that quotes it, with no blob and no store ---'
    & (Join-Path $PSScriptRoot 'compile.ps1') -Src 'codex/test/quote-from-peer.codex' `
        -Out $quoteCdx -Log "$out/quote-from-peer.log" -Kernel $Kernel -Peer "127.0.0.1:$HostPort" | Out-Null
    if (-not (Test-Path $quoteCdx)) {
        Write-Host "  FAIL  it did not compile; the quotation was not resolved from the peer. See $out/quote-from-peer.log"
        exit 1
    }
    Write-Host '  it compiled -- the quotation resolved from the peer'

    Write-Host '--- 4. run it ---'
    $runOut = "$out/quote-from-peer.out"
    & (Join-Path $PSScriptRoot 'test-run.ps1') -Kernel $quoteCdx -OutFile $runOut 2>$null | Out-Null
    $actual = ((Get-Content $runOut -Raw -ErrorAction SilentlyContinue) -replace "`r", '').Trim()
    Check 'the program computes with the quoted function' $actual 'sorted=105'

    Write-Host '--- 5. sensitivity: the same source against a peer holding nothing ---'
    [System.IO.File]::WriteAllBytes((Join-Path (Get-Location) $emptyDisk), (New-Object byte[] 1048576))
    $emptyPort = $HostPort + 1
    Start-Peer -Image $emptyDisk -Port $emptyPort -Tag 'serve-empty' | Out-Null
    Wait-Peer -Port $emptyPort
    $missCdx = "$out/quote-from-peer-miss.cdx"
    Remove-Item -Force $missCdx -ErrorAction SilentlyContinue
    & (Join-Path $PSScriptRoot 'compile.ps1') -Src 'codex/test/quote-from-peer.codex' `
        -Out $missCdx -Log "$out/quote-from-peer-miss.log" -Kernel $Kernel -Peer "127.0.0.1:$emptyPort" 2>$null | Out-Null
    Check 'a peer that does not hold the work cannot supply it' (Test-Path $missCdx) $false
} finally {
    foreach ($p in $procs) {
        if ($p -and -not $p.HasExited) { Stop-VmGraceful -ProcessId $p.Id }
    }
    if (-not $KeepArtifacts) {
        Remove-Item -Force $disk, $emptyDisk -ErrorAction SilentlyContinue
    }
}

Write-Host ''
if ($fail -gt 0) { Write-Host "quote-from-peer-test: $fail FAILED" -ForegroundColor Red; exit 1 }
Write-Host 'PASS: a quotation was resolved from a peer.' -ForegroundColor Green
exit 0
