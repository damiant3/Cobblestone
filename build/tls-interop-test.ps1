# tls-interop-test.ps1 -- our TLS 1.3 server against a client we did not write
#
# Every other TLS test in this tree is a loopback: our client against our
# server, shuttling records between two endpoints in one process. That proves
# the two halves agree with each other. It cannot tell a correct
# implementation from two consistently wrong ones, which is the failure the
# compression post-mortem in docs/PM/Active/Stories/BrotliBeatsOpus.md exists
# to describe.
#
# The oracle here is Python's ssl module over OpenSSL. It is NOT .NET:
# SslStream goes through SChannel, which does not support Ed25519 in TLS, and
# our X.509 stack is Ed25519-only, so a .NET client would fail to negotiate
# for a reason that has nothing to do with our code.
#
# The CA bytes are READ OUT OF the test fixture rather than copied, so the
# anchor openssl trusts and the leaf our server presents have one source of
# truth. If someone re-mints the fixture, this harness follows.
#
# Usage: tls-interop-test.ps1 [-Kernel <cdx>] [-Port <n>] [-KeepArtifacts]

param(
    [string]$Kernel = '',
    [int]$Port = 0,
    [int]$BootSeconds = 25,
    [switch]$KeepArtifacts
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo    = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
# Artifacts go under test-output, not build-output: build.ps1's clean phase
# removes build-output, so a gate run between the failure and the diagnosis
# deletes exactly the guest log you need.
$Out     = Join-Path $Repo 'test-output\tls-interop'
$Fixture = Join-Path $Repo 'codex\test\apps\tls-noauth-loopback.codex'
$Serve   = Join-Path $Repo 'tools\tls-serve.codex'
$Vm      = Join-Path $Repo 'tools\codex-vm.exe'

$Python  = 'D:\Python311\python.exe'
$OpenSsl = 'C:\Program Files\Git\usr\bin\openssl.exe'

if ($Port -eq 0) { $Port = 19443 }
New-Item -ItemType Directory -Force $Out | Out-Null

function Fail($msg) { Write-Host "tls-interop: FAIL -- $msg"; exit 1 }

foreach ($t in @($Python, $OpenSsl, $Vm)) {
    if (-not (Test-Path $t)) { Fail "missing tool: $t" }
}

# ---------------------------------------------------------------------------
# The anchor, taken from the fixture the server's leaf was minted against.
# ---------------------------------------------------------------------------
$src = [System.IO.File]::ReadAllText($Fixture)
$m = [regex]::Match($src, 'ca-cert\s*:\s*List Integer\s*=\s*\[(?<body>[^\]]*)\]')
if (-not $m.Success) { Fail "could not read ca-cert out of $Fixture" }
$caBytes = [byte[]]@($m.Groups['body'].Value -split '[,\s]+' |
    Where-Object { $_ -match '^\d+$' } | ForEach-Object { [byte][int]$_ })
if ($caBytes.Count -lt 64) { Fail "ca-cert parsed as only $($caBytes.Count) bytes" }

$caDer = Join-Path $Out 'ca.der'
$caPem = Join-Path $Out 'ca.pem'
[System.IO.File]::WriteAllBytes($caDer, $caBytes)
& $OpenSsl x509 -inform DER -in $caDer -out $caPem 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $caPem)) { Fail "openssl could not read the fixture CA as a certificate" }
Write-Host "tls-interop: anchor = $($caBytes.Count) bytes from the fixture, openssl parsed it"

# A DIFFERENT CA, for the negative control. Same algorithm, unrelated key.
$badKey = Join-Path $Out 'bad.key'
$badPem = Join-Path $Out 'bad.pem'
& $OpenSsl genpkey -algorithm ED25519 -out $badKey 2>&1 | Out-Null
& $OpenSsl req -new -x509 -key $badKey -out $badPem -days 3650 -subj '/CN=Not The Codex CA' 2>&1 | Out-Null
if (-not (Test-Path $badPem)) { Fail "could not mint the control CA" }

# ---------------------------------------------------------------------------
# The server binary.
# ---------------------------------------------------------------------------
if ($Kernel -eq '') {
    $Kernel = Join-Path $Out 'tls-serve.cdx'
    Write-Host "tls-interop: compiling tools/tls-serve.codex"
    & (Join-Path $Repo 'build\compile.ps1') -Src $Serve -Out $Kernel -Log (Join-Path $Out 'tls-serve.log') | Out-Null
    if (-not (Test-Path $Kernel)) { Fail "tls-serve did not compile -- see $Out\tls-serve.log" }
}

# ---------------------------------------------------------------------------
# The client. check_hostname is OFF and the reason is recorded rather than
# hidden: the fixture leaf carries its name in the CN only, and OpenSSL has
# required subjectAltName for hostname matching since it stopped trusting CN.
# The CHAIN is still verified against the fixture CA (CERT_REQUIRED), which is
# the property under test; hostname binding is a separate gap.
# ---------------------------------------------------------------------------
$client = @'
import socket, ssl, sys, json
port, cafile, expect_ok = int(sys.argv[1]), sys.argv[2], sys.argv[3] == "ok"
ctx = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
ctx.minimum_version = ssl.TLSVersion.TLSv1_3
ctx.maximum_version = ssl.TLSVersion.TLSv1_3
ctx.check_hostname = False
ctx.verify_mode = ssl.CERT_REQUIRED
ctx.load_verify_locations(cafile)
res = {"openssl": ssl.OPENSSL_VERSION}
try:
    with socket.create_connection(("127.0.0.1", port), timeout=90) as s:
        with ctx.wrap_socket(s) as ss:
            res["version"] = ss.version()
            res["cipher"] = ss.cipher()[0]
            peer = ss.getpeercert(binary_form=True)
            res["peercert_bytes"] = len(peer) if peer else 0
            ss.sendall(b"GET")
            data = ss.recv(64)
            res["echo"] = data.decode("latin1")
            res["ok"] = True
except Exception as e:
    res["ok"] = False
    res["error"] = "%s: %s" % (type(e).__name__, e)
print(json.dumps(res))
'@
$clientPy = Join-Path $Out 'client.py'
Set-Content -Path $clientPy -Value $client -Encoding utf8

# ---------------------------------------------------------------------------
# Boot the guest and ask it a question.
# ---------------------------------------------------------------------------
function Invoke-Case {
    param([string]$Label, [string]$CaFile, [string]$Expect)

    $vmOut = Join-Path $Out "$Label.out"
    if (Test-Path $vmOut) { Remove-Item $vmOut -Force }
    $args = @('-kernel', $Kernel, '-headless', '-mem', '3072',
              '-output', $vmOut, '-portfwd', "$($Port):9443")
    $proc = Start-Process -FilePath $Vm -ArgumentList $args -PassThru -WindowStyle Hidden

    try {
        # DO NOT PROBE THE PORT TO CHECK READINESS. Two reasons, and the
        # second one cost a run after the first was "fixed":
        #
        #  - codex-vm's port forward accepts the host connection long before
        #    the guest is behind it, so a successful connect proves nothing.
        #  - This server handles one connection at a time and a connection
        #    carrying no data parks it inside net-io-recv-raw for that
        #    function's whole fuel budget. A probe that connects and closes
        #    therefore wedges the server for far longer than the client is
        #    willing to wait. Making the server LOOP did not fix this; the
        #    loop only helps once the current connection finishes.
        #
        # So: wait for the boot, then let the real client be the probe.
        Start-Sleep -Seconds $BootSeconds
        if ($proc.HasExited) { return @{ ok = $false; error = 'guest exited during boot' } }

        $raw = & $Python $clientPy $Port $CaFile $Expect 2>&1
        try { return ($raw | Select-Object -Last 1 | ConvertFrom-Json) }
        catch { return @{ ok = $false; error = "client produced no JSON: $raw" } }
    }
    finally {
        if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
        Start-Sleep -Milliseconds 500
    }
}

Write-Host "tls-interop: case 1 -- python/OpenSSL client, fixture CA (must succeed)"
$good = Invoke-Case -Label 'good' -CaFile $caPem -Expect 'ok'
$good | ConvertTo-Json -Compress | Write-Host

Write-Host "tls-interop: case 2 -- same server, unrelated CA (must FAIL)"
$bad = Invoke-Case -Label 'bad' -CaFile $badPem -Expect 'fail'
$bad | ConvertTo-Json -Compress | Write-Host

# ---------------------------------------------------------------------------
# Verdict.
# ---------------------------------------------------------------------------
$problems = @()
if (-not $good.ok)                      { $problems += "handshake failed against the fixture CA: $($good.error)" }
elseif ($good.version -ne 'TLSv1.3')    { $problems += "negotiated $($good.version), expected TLSv1.3" }
elseif ($good.echo -ne 'GET')           { $problems += "echo was '$($good.echo)', expected 'GET'" }

# The negative must fail, and it must fail for the RIGHT reason. A control
# that fails because the guest never booted proves nothing.
if ($bad.ok) { $problems += "handshake SUCCEEDED against an unrelated CA -- the chain is not being verified" }
elseif ($bad.error -notmatch 'certificate|CERTIFICATE|verify') {
    $problems += "control failed for the wrong reason: $($bad.error)"
}

if (-not $KeepArtifacts) { Remove-Item $caDer, $badKey -Force -ErrorAction SilentlyContinue }

if ($problems.Count -gt 0) {
    Write-Host ''
    foreach ($p in $problems) { Write-Host "  $p" }
    Fail "$($problems.Count) problem(s)"
}

Write-Host ''
Write-Host "tls-interop: OK"
Write-Host "  $($good.version) / $($good.cipher) against $($good.openssl)"
Write-Host "  server certificate accepted: $($good.peercert_bytes) bytes, chain walked to the fixture CA"
Write-Host "  application data echoed: '$($good.echo)'"
Write-Host "  control refused an unrelated CA: $($bad.error)"
exit 0
