# https-interop-test.ps1 -- fetch-tls against servers we did not write.
#
# build/mqtts-interop-test.ps1 was the first test where our TLS CLIENT met a
# foreign server, but it re-implements the record framing in
# tools/mqtts-client.codex. So Net chapter HttpFetch's own https path --
# tls-recv-record, https-drive, https-request, https-recv-app -- had still
# never seen bytes it did not produce. This runs it.
#
# Three OpenSSL servers, three certificate stories, one guest:
#
#   4443  ECDSA P-256 chain    -> CertificateVerify is ecdsa_secp256r1_sha256
#   4444  RSA-2048 chain       -> CertificateVerify is rsa_pss_rsae_sha256
#   4445  self-signed P-256    -> anchored by nobody, and must be REFUSED
#
# The rogue is the control. Two servers accepted proves only that we accept.
#
# Usage: https-interop-test.ps1 [-Kernel <cdx>] [-RunSeconds n] [-KeepArtifacts]

param(
    [string]$Kernel = '',
    [int]$RunSeconds = 600,
    [switch]$KeepArtifacts
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo    = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Out     = Join-Path $Repo 'test-output\https-interop'
$Fix     = Join-Path $Repo 'codex\test\fixtures\https'
$Guest   = Join-Path $Repo 'tools\https-client.codex'
$Vm      = Join-Path $Repo 'tools\codex-vm.exe'
$OpenSsl = 'C:\Program Files\Git\usr\bin\openssl.exe'

# Must match tools/https-client.codex.
$Body = 'codex-https-interop-ok'
$Ports = @{ ec = 4443; rsa = 4444; rogue = 4445 }

function Fail($msg) { Write-Host "https-interop: FAIL -- $msg"; exit 1 }

foreach ($t in @($OpenSsl, $Vm, $Guest)) { if (-not (Test-Path $t)) { Fail "missing: $t" } }
foreach ($f in @('ec-ca.pem','ec-leaf.pem','ec-leaf.key','rsa-ca.pem','rsa-leaf.pem','rsa-leaf.key','rogue-leaf.pem','rogue-leaf.key')) {
    if (-not (Test-Path (Join-Path $Fix $f))) { Fail "missing fixture $f -- run build/mint-https-fixtures.ps1 -Patch" }
}

New-Item -ItemType Directory -Force $Out | Out-Null

# ---------------------------------------------------------------------------
# The anchors the guest carries must be the CAs the servers' leaves chain to.
# The guest holds its own copy of the DER; drift between the two would fail
# this test for a reason that has nothing to do with the protocol, so it is
# checked rather than hoped for. Same guard as mqtts-interop-test.ps1.
# ---------------------------------------------------------------------------
function Get-ByteList([string]$path, [string]$name) {
    $src = [System.IO.File]::ReadAllText($path)
    $m = [regex]::Match($src, "$name\s*:\s*List Integer\s*=\s*\[(?<body>[^\]]*)\]")
    if (-not $m.Success) { Fail "could not read $name out of $path" }
    return [byte[]]@($m.Groups['body'].Value -split '[,\s]+' |
        Where-Object { $_ -match '^\d+$' } | ForEach-Object { [byte][int]$_ })
}

foreach ($k in @('ec','rsa')) {
    $der = Join-Path $Out "$k-ca.der"
    & $OpenSsl x509 -in (Join-Path $Fix "$k-ca.pem") -outform DER -out $der 2>&1 | Out-Null
    if (-not (Test-Path $der)) { Fail "openssl could not read the $k CA" }
    $fromPem = [System.IO.File]::ReadAllBytes($der)
    $inGuest = Get-ByteList $Guest "https-$k-ca"
    if (@(Compare-Object $fromPem $inGuest -SyncWindow 0).Count -ne 0) {
        Fail "tools/https-client.codex's https-$k-ca has drifted from codex/test/fixtures/https/$k-ca.pem -- re-run build/mint-https-fixtures.ps1 -Patch"
    }
}
Write-Host "https-interop: guest anchors agree with the fixture CAs"

# ---------------------------------------------------------------------------
# The document. Written with no trailing newline, because the guest compares
# the body for equality and a stray byte is a failure it should report.
# ---------------------------------------------------------------------------
$Docs = Join-Path $Out 'docroot'
New-Item -ItemType Directory -Force $Docs | Out-Null
[System.IO.File]::WriteAllText((Join-Path $Docs 'probe.txt'), $Body)

# ---------------------------------------------------------------------------
# The guest binary.
# ---------------------------------------------------------------------------
if ($Kernel -eq '') {
    $Kernel = Join-Path $Out 'https-client.cdx'
    Write-Host "https-interop: compiling tools/https-client.codex"
    & (Join-Path $Repo 'build\compile.ps1') -Src $Guest -Out $Kernel -Log (Join-Path $Out 'https-client.log') | Out-Null
    if (-not (Test-Path $Kernel)) { Fail "https-client did not compile -- see $Out\https-client.log" }
}

# ---------------------------------------------------------------------------
# The servers. -WWW serves files from the working directory, so the response
# body is ours to pin; -naccept 1 makes each one exit after the single
# connection the guest opens, which is also how a hung guest shows up as a
# server that never exited.
# ---------------------------------------------------------------------------
$servers = @()
function Start-Server([string]$name, [string]$cert, [string]$key, [int]$port) {
    $o = Join-Path $Out "$name-server.out"
    $e = Join-Path $Out "$name-server.err"
    $a = @('s_server','-WWW','-accept',"$port",'-naccept','1',
           '-cert',$cert,'-key',$key,'-tls1_3','-quiet')
    return Start-Process -FilePath $OpenSsl -ArgumentList $a -PassThru -WindowStyle Hidden `
        -WorkingDirectory $Docs -RedirectStandardOutput $o -RedirectStandardError $e
}

$vmOut = Join-Path $Out 'guest.out'
$vmErr = Join-Path $Out 'guest.err'
Remove-Item $vmOut -Force -ErrorAction SilentlyContinue

try {
    $servers += Start-Server 'ec'    (Join-Path $Fix 'ec-leaf.pem')    (Join-Path $Fix 'ec-leaf.key')    $Ports.ec
    $servers += Start-Server 'rsa'   (Join-Path $Fix 'rsa-leaf.pem')   (Join-Path $Fix 'rsa-leaf.key')   $Ports.rsa
    $servers += Start-Server 'rogue' (Join-Path $Fix 'rogue-leaf.pem') (Join-Path $Fix 'rogue-leaf.key') $Ports.rogue
    Start-Sleep -Seconds 2
    foreach ($s in $servers) { if ($s.HasExited) { Fail "an s_server exited immediately -- see $Out\*-server.err" } }
    Write-Host "https-interop: three s_server instances up on $($Ports.ec), $($Ports.rsa), $($Ports.rogue)"

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $vm = Start-Process -FilePath $Vm `
        -ArgumentList '-kernel', $Kernel, '-headless', '-mem', '3072', '-output', $vmOut `
        -PassThru -WindowStyle Hidden -RedirectStandardError $vmErr
    if (-not $vm.WaitForExit($RunSeconds * 1000)) {
        Stop-Process -Id $vm.Id -Force -ErrorAction SilentlyContinue
        Write-Host "https-interop: guest did not exit within $RunSeconds s -- reporting what it managed to say"
    }
    $sw.Stop()
    Write-Host "https-interop: guest ran for $([int]$sw.Elapsed.TotalSeconds) s"
    Start-Sleep -Seconds 1
}
finally {
    foreach ($s in $servers) { if (-not $s.HasExited) { Stop-Process -Id $s.Id -Force -ErrorAction SilentlyContinue } }
}

if (-not (Test-Path $vmOut)) { Fail "guest produced no output at all" }
$guest = Get-Content $vmOut
Write-Host "https-interop: guest said --"
$guest | ForEach-Object { Write-Host "  $_" }

# ---------------------------------------------------------------------------
# Verdict.
# ---------------------------------------------------------------------------
$problems = @()
function Line([string]$prefix) { return ($guest | Where-Object { $_ -like "$prefix*" } | Select-Object -First 1) }

foreach ($k in @('ec','rsa')) {
    $l = Line "https $k "
    if (-not $l) { $problems += "guest never reported a $k result" }
    else {
        if ($l -notmatch 'ok=True')            { $problems += "the $k fetch failed: $l" }
        if ($l -notmatch 'body-matches=True')  { $problems += "the $k fetch returned the wrong body: $l" }
    }
}

$r = Line 'https rogue '
if (-not $r) { $problems += "guest never reported a rogue result" }
elseif ($r -notmatch 'ok=False') { $problems += "THE UNANCHORED SELF-SIGNED SERVER WAS ACCEPTED: $r" }

if (-not (Line 'https done')) { $problems += "guest did not run to the end" }

if (-not $KeepArtifacts) {
    Remove-Item (Join-Path $Out 'ec-ca.der'), (Join-Path $Out 'rsa-ca.der'), $vmErr -Force -ErrorAction SilentlyContinue
}

if ($problems.Count -gt 0) {
    Write-Host ''
    foreach ($p in $problems) { Write-Host "  $p" }
    Fail "$($problems.Count) problem(s)"
}

Write-Host ''
Write-Host "https-interop: OK"
Write-Host "  fetch-tls completed an HTTPS exchange with OpenSSL as the server, twice"
Write-Host "  an ECDSA P-256 chain and an RSA-2048 chain, both authenticated to our anchors"
Write-Host "  both bodies matched the served file byte for byte"
Write-Host "  the unanchored self-signed server was refused"
exit 0
