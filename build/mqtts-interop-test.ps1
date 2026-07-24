# mqtts-interop-test.ps1 -- MQTT over TLS 1.3 against mosquitto's TLS listener
#
# This is the first test in the tree where OUR TLS CLIENT talks to a foreign
# SERVER. build/tls-interop-test.ps1 drives our server with openssl's client,
# so the client half was only ever exercised by our own server in a loopback
# -- the one-directional shape docs/PM/Active/Stories/BrotliBeatsOpus.md is
# about, pointed at TLS instead of compression.
#
# The broker is given the SAME Ed25519 leaf and CA the loopback fixture uses,
# converted to PEM here rather than copied, so there is one source of truth
# for those bytes. The guest carries the CA as its only anchor.
#
# The control is the certificate chain itself: the guest reports
# authenticated=True only if the leaf mosquitto presented walked to the anchor
# the guest was built with. A run that connected but did not authenticate is a
# failure, not a pass.
#
# Usage: mqtts-interop-test.ps1 [-Kernel <cdx>] [-Port <n>] [-KeepArtifacts]

param(
    [string]$Kernel = '',
    [int]$Port = 0,
    [int]$RunSeconds = 90,
    [switch]$KeepArtifacts
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo    = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Out     = Join-Path $Repo 'test-output\mqtts-interop'
$Client  = Join-Path $Repo 'tools\mqtts-client.codex'
$Fixture = Join-Path $Repo 'codex\test\apps\tls-noauth-loopback.codex'
$Serve   = Join-Path $Repo 'tools\tls-serve.codex'
$Vm      = Join-Path $Repo 'tools\codex-vm.exe'

$MosqDir = 'C:\Program Files\Mosquitto'
$Broker  = Join-Path $MosqDir 'mosquitto.exe'
$Sub     = Join-Path $MosqDir 'mosquitto_sub.exe'
$OpenSsl = 'C:\Program Files\Git\usr\bin\openssl.exe'

if ($Port -eq 0) { $Port = 8883 }

# Must match tools/mqtts-client.codex.
$Topic       = 'codex/secure'
$PayloadText = 'secret-codex'

New-Item -ItemType Directory -Force $Out | Out-Null

function Fail($msg) { Write-Host "mqtts-interop: FAIL -- $msg"; exit 1 }

foreach ($t in @($Broker, $Sub, $OpenSsl, $Vm, $Client, $Fixture, $Serve)) {
    if (-not (Test-Path $t)) { Fail "missing: $t" }
}

function Get-ByteList([string]$path, [string]$name) {
    $src = [System.IO.File]::ReadAllText($path)
    $m = [regex]::Match($src, "$name\s*:\s*List Integer\s*=\s*\[(?<body>[^\]]*)\]")
    if (-not $m.Success) { Fail "could not read $name out of $path" }
    return [byte[]]@($m.Groups['body'].Value -split '[,\s]+' |
        Where-Object { $_ -match '^\d+$' } | ForEach-Object { [byte][int]$_ })
}

# ---------------------------------------------------------------------------
# The CA the guest anchors on must be the CA the broker's leaf chains to.
# The guest carries its own copy of the literal; drift between the two would
# make this test fail for a reason that has nothing to do with the protocol,
# so it is checked rather than hoped for.
# ---------------------------------------------------------------------------
$caBytes     = Get-ByteList $Fixture 'ca-cert'
$guestCa     = Get-ByteList $Client  'mqtts-ca'
if (@(Compare-Object $caBytes $guestCa -SyncWindow 0).Count -ne 0) {
    Fail "tools/mqtts-client.codex's mqtts-ca has drifted from the fixture ca-cert -- re-copy the literal"
}
$leafBytes = Get-ByteList $Serve 'leaf-cert'
$leafPriv  = Get-ByteList $Serve 'leaf-priv'
if ($leafPriv.Count -ne 32) { Fail "leaf-priv parsed as $($leafPriv.Count) bytes, expected 32" }

$caDer  = Join-Path $Out 'ca.der';   [System.IO.File]::WriteAllBytes($caDer, $caBytes)
$leafDer = Join-Path $Out 'leaf.der'; [System.IO.File]::WriteAllBytes($leafDer, $leafBytes)
$caPem   = Join-Path $Out 'ca.pem'
$leafPem = Join-Path $Out 'leaf.pem'
& $OpenSsl x509 -inform DER -in $caDer   -out $caPem   2>&1 | Out-Null
& $OpenSsl x509 -inform DER -in $leafDer -out $leafPem 2>&1 | Out-Null
if (-not (Test-Path $caPem) -or -not (Test-Path $leafPem)) { Fail "openssl could not read the fixture certificates" }

# An Ed25519 private key in PKCS#8 is a fixed 16-byte prologue and the seed.
# There is no openssl invocation that takes a raw seed, so the wrapper is
# written out here: SEQUENCE, version 0, AlgorithmIdentifier 1.3.101.112,
# OCTET STRING wrapping an OCTET STRING of 32 bytes.
$pkcs8 = [byte[]]@(0x30,0x2e,0x02,0x01,0x00,0x30,0x05,0x06,0x03,0x2b,0x65,0x70,0x04,0x22,0x04,0x20) + $leafPriv
$keyPem = Join-Path $Out 'leaf.key'
Set-Content -Path $keyPem -Encoding ascii -Value (@('-----BEGIN PRIVATE KEY-----') +
    ([System.Convert]::ToBase64String($pkcs8) -split '(.{64})' | Where-Object { $_ }) +
    @('-----END PRIVATE KEY-----'))

# Prove the pair matches before blaming the handshake for a mismatched key.
$certPub = (& $OpenSsl x509 -in $leafPem -noout -pubkey 2>&1) -join "`n"
$keyPub  = (& $OpenSsl pkey -in $keyPem -pubout 2>&1) -join "`n"
if ($certPub.Trim() -ne $keyPub.Trim()) { Fail "the fixture leaf key does not match the leaf certificate" }
Write-Host "mqtts-interop: fixture leaf and key agree; CA is $($caBytes.Count) bytes"

# ---------------------------------------------------------------------------
# The client binary.
# ---------------------------------------------------------------------------
if ($Kernel -eq '') {
    $Kernel = Join-Path $Out 'mqtts-client.cdx'
    Write-Host "mqtts-interop: compiling tools/mqtts-client.codex"
    & (Join-Path $Repo 'build\compile.ps1') -Src $Client -Out $Kernel -Log (Join-Path $Out 'mqtts-client.log') | Out-Null
    if (-not (Test-Path $Kernel)) { Fail "mqtts-client did not compile -- see $Out\mqtts-client.log" }
}

# ---------------------------------------------------------------------------
# The broker, TLS only.
# ---------------------------------------------------------------------------
$conf = Join-Path $Out 'mosquitto.conf'
Set-Content -Path $conf -Encoding ascii -Value @(
    "listener $Port 127.0.0.1"
    "allow_anonymous true"
    "cafile $caPem"
    "certfile $leafPem"
    "keyfile $keyPem"
    "tls_version tlsv1.3"
    "require_certificate false"
)

$brkOut = Join-Path $Out 'broker.out'
$brkErr = Join-Path $Out 'broker.err'
$vmOut  = Join-Path $Out 'guest.out'
$vmErr  = Join-Path $Out 'guest.err'

# -v, because a TLS handshake that fails silently from the guest's side is
# diagnosed almost entirely from what the broker says it saw.
$brk = Start-Process -FilePath $Broker -ArgumentList '-c', $conf, '-v' -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput $brkOut -RedirectStandardError $brkErr
try {
    Start-Sleep -Seconds 2
    if ($brk.HasExited) { Fail "mosquitto exited immediately -- $(Get-Content $brkErr -Raw)" }

    $vm = Start-Process -FilePath $Vm `
        -ArgumentList '-kernel', $Kernel, '-headless', '-mem', '3072', '-output', $vmOut `
        -PassThru -WindowStyle Hidden -RedirectStandardError $vmErr
    if (-not $vm.WaitForExit($RunSeconds * 1000)) {
        Stop-Process -Id $vm.Id -Force -ErrorAction SilentlyContinue
    }
    Start-Sleep -Seconds 1
}
finally {
    if (-not $brk.HasExited) { Stop-Process -Id $brk.Id -Force -ErrorAction SilentlyContinue }
}

if (-not (Test-Path $vmOut)) { Fail "guest produced no output at all" }
$guest = Get-Content $vmOut
Write-Host "mqtts-interop: guest said --"
$guest | ForEach-Object { Write-Host "  $_" }

# ---------------------------------------------------------------------------
# Verdict.
# ---------------------------------------------------------------------------
$problems = @()

$tls     = $guest | Where-Object { $_ -like 'mqtts tls *' }       | Select-Object -First 1
$connect = $guest | Where-Object { $_ -like 'mqtts connect *' }   | Select-Object -First 1
$publish = $guest | Where-Object { $_ -like 'mqtts publish *' }   | Select-Object -First 1

if (-not $tls) { $problems += "guest never reported a TLS result" }
else {
    if ($tls -notmatch 'done=True') { $problems += "the TLS handshake did not complete against mosquitto: $tls" }
    if ($tls -notmatch 'authenticated=True') { $problems += "mosquitto's certificate did not chain to the fixture CA: $tls" }
}

if (-not $connect) { $problems += "guest never reported a CONNECT result" }
elseif ($connect -notmatch 'ready=True') { $problems += "broker refused the MQTT connection inside TLS: $connect" }

if (-not $publish) { $problems += "guest never reported a PUBLISH result" }
elseif ($publish -notmatch 'acked=True') { $problems += "broker never acknowledged the publish: $publish" }

if (-not $KeepArtifacts) { Remove-Item $caDer, $leafDer, $vmErr, $brkErr -Force -ErrorAction SilentlyContinue }

if ($problems.Count -gt 0) {
    Write-Host ''
    foreach ($p in $problems) { Write-Host "  $p" }
    Fail "$($problems.Count) problem(s)"
}

Write-Host ''
Write-Host "mqtts-interop: OK"
Write-Host "  our TLS 1.3 CLIENT completed a handshake with OpenSSL as the server"
Write-Host "  mosquitto's Ed25519 leaf chained to the fixture CA the guest anchors on"
Write-Host "  MQTT ran inside the TLS session: CONNECT accepted, publish acknowledged"
exit 0
