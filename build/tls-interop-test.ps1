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

# A client that sends a real ClientHello and walks away, by FIN or by RST
# (SO_LINGER 0), then a real client one second later. The server serves one
# connection at a time, so the second client completes only if the first
# connection was released.
$abandon = @'
import socket, ssl, sys, json, time, struct
port, mode, cafile = int(sys.argv[1]), sys.argv[2], sys.argv[3]
ctx0 = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
ctx0.check_hostname = False
ctx0.verify_mode = ssl.CERT_NONE
inc, out = ssl.MemoryBIO(), ssl.MemoryBIO()
obj = ctx0.wrap_bio(inc, out)
try:
    obj.do_handshake()
except ssl.SSLWantReadError:
    pass
s = socket.create_connection(("127.0.0.1", port), timeout=10)
s.sendall(out.read())
if mode == "rst":
    s.setsockopt(socket.SOL_SOCKET, socket.SO_LINGER, struct.pack("ii", 1, 0))
s.close()
time.sleep(1)
res = {"mode": mode}
t0 = time.time()
ctx = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
ctx.minimum_version = ssl.TLSVersion.TLSv1_3
ctx.check_hostname = False
ctx.verify_mode = ssl.CERT_REQUIRED
ctx.load_verify_locations(cafile)
try:
    with socket.create_connection(("127.0.0.1", port), timeout=60) as c:
        with ctx.wrap_socket(c) as sc:
            sc.sendall(b"GET")
            res["echo"] = sc.recv(64).decode("latin1")
            res["ok"] = True
except Exception as e:
    res["ok"] = False
    res["error"] = "%s: %s" % (type(e).__name__, e)
res["seconds"] = round(time.time() - t0, 1)
print(json.dumps(res))
'@
$abandonPy = Join-Path $Out 'abandon.py'
Set-Content -Path $abandonPy -Value $abandon -Encoding utf8

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
        #  - This server handles one connection at a time, and a connection
        #    that stays open carrying no data parks it inside net-io-recv-raw
        #    for that function's whole fuel budget. A connection that CLOSES
        #    is released at once (case 6 grades that).
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

function Invoke-Abandon {
    param([string]$Mode)

    $vmOut = Join-Path $Out "abandon-$Mode.out"
    if (Test-Path $vmOut) { Remove-Item $vmOut -Force }
    $args = @('-kernel', $Kernel, '-headless', '-mem', '3072',
              '-output', $vmOut, '-portfwd', "$($Port):9443")
    $proc = Start-Process -FilePath $Vm -ArgumentList $args -PassThru -WindowStyle Hidden
    try {
        Start-Sleep -Seconds $BootSeconds
        if ($proc.HasExited) { return @{ ok = $false; error = 'guest exited during boot' } }
        $raw = & $Python $abandonPy $Port $Mode $caPem 2>&1
        try { return ($raw | Select-Object -Last 1 | ConvertFrom-Json) }
        catch { return @{ ok = $false; error = "abandon client produced no JSON: $raw" } }
    }
    finally {
        if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
        Start-Sleep -Milliseconds 500
    }
}

# s_client offers a key share for the FIRST group in -groups only, so
# P-256:X25519 is a client leading with a group we do not speak: the server
# must answer with a HelloRetryRequest (RFC 8446 4.1.4). -msg prints every
# handshake message, and two ClientHellos is the proof the retry happened.
function Invoke-SClient {
    param([string]$Label, [string]$Groups, [int]$Seconds = 60, [string]$Sigalgs = '')

    $vmOut = Join-Path $Out "$Label.out"
    if (Test-Path $vmOut) { Remove-Item $vmOut -Force }
    $args = @('-kernel', $Kernel, '-headless', '-mem', '3072',
              '-output', $vmOut, '-portfwd', "$($Port):9443")
    $proc = Start-Process -FilePath $Vm -ArgumentList $args -PassThru -WindowStyle Hidden
    try {
        Start-Sleep -Seconds $BootSeconds
        if ($proc.HasExited) { return @{ ok = $false; error = 'guest exited during boot'; hellos = 0 } }

        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = $OpenSsl
        $psi.Arguments = "s_client -connect 127.0.0.1:$Port -tls1_3 -groups $Groups -CAfile `"$caPem`" -verify_return_error -ign_eof -msg" + $(if ($Sigalgs) { " -sigalgs $Sigalgs" } else { '' })
        $psi.UseShellExecute = $false
        $psi.RedirectStandardInput = $true
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $sc = [System.Diagnostics.Process]::Start($psi)
        $stdout = $sc.StandardOutput.ReadToEndAsync()
        $stderr = $sc.StandardError.ReadToEndAsync()
        $sc.StandardInput.Write("GET")
        $sc.StandardInput.Close()
        $timedOut = -not $sc.WaitForExit($Seconds * 1000)
        if ($timedOut) { $sc.Kill() }
        $text = $stdout.Result + $stderr.Result
        Set-Content -Path (Join-Path $Out "$Label.sclient.txt") -Value $text -Encoding utf8
        $hellos = ([regex]::Matches($text, '>>> TLS 1\.3, Handshake \[length [0-9a-f]+\], ClientHello')).Count
        return @{
            ok = (-not $timedOut) -and ($text -match 'Verify return code: 0 \(ok\)') -and ($text -match 'New, TLSv1\.3, Cipher is')
            timedOut = $timedOut
            hellos = $hellos
            echo = ($text -match '(?m)^GET')
            peerAlert = [regex]::Match($text, '<<< TLS 1\.\d, Alert \[length [0-9a-f]+\], fatal (\w+)').Groups[1].Value
            peerSig = [regex]::Match($text, 'Peer signature type: (\S+)').Groups[1].Value
            error = if ($timedOut) { "s_client timed out after $Seconds s" } else { ($text -split "`n" | Select-String -Pattern 'error|alert' | Select-Object -First 1) -as [string] }
        }
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

Write-Host "tls-interop: case 3 -- openssl s_client leading with P-256, X25519 second (must retry and succeed)"
$hrr = Invoke-SClient -Label 'hrr' -Groups 'P-256:X25519'
$hrr | ConvertTo-Json -Compress | Write-Host

Write-Host "tls-interop: case 4 -- openssl s_client offering P-256 only (must FAIL)"
$p256 = Invoke-SClient -Label 'p256' -Groups 'P-256' -Seconds 30
$p256 | ConvertTo-Json -Compress | Write-Host

# A client offering no Ed25519 signature scheme: tls-serve answers with its
# P-256 leaf, and OpenSSL verifies our ECDSA CertificateVerify and the chain.
Write-Host "tls-interop: case 5 -- openssl s_client offering only ecdsa_secp256r1_sha256 (must succeed on the P-256 leaf)"
$ecdsa = Invoke-SClient -Label 'ecdsa' -Groups 'X25519' -Sigalgs 'ecdsa_secp256r1_sha256'
$ecdsa | ConvertTo-Json -Compress | Write-Host

Write-Host "tls-interop: case 6 -- a client abandons mid-handshake by FIN, then by RST; the next client must be served"
$abFin = Invoke-Abandon -Mode 'fin'
$abFin | ConvertTo-Json -Compress | Write-Host
$abRst = Invoke-Abandon -Mode 'rst'
$abRst | ConvertTo-Json -Compress | Write-Host

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

if (-not $hrr.ok)            { $problems += "P-256:X25519 client did not complete: $($hrr.error)" }
elseif ($hrr.hellos -ne 2)   { $problems += "P-256:X25519 client sent $($hrr.hellos) ClientHello(s), expected 2: no HelloRetryRequest happened" }
elseif (-not $hrr.echo)      { $problems += "P-256:X25519 client got no 'GET' echo" }
if ($p256.ok)                { $problems += "a P-256-only client COMPLETED a handshake with an X25519-only server" }
elseif ($p256.peerAlert -ne 'handshake_failure') { $problems += "P-256-only control failed for the wrong reason: alert '$($p256.peerAlert)', $($p256.error)" }
if (-not $ecdsa.ok)             { $problems += "ecdsa-only client did not complete: $($ecdsa.error)" }
elseif ($ecdsa.peerSig -ne 'ECDSA') { $problems += "ecdsa-only client saw peer signature type '$($ecdsa.peerSig)', expected ECDSA" }
elseif (-not $ecdsa.echo)       { $problems += "ecdsa-only client got no 'GET' echo" }
foreach ($ab in @($abFin, $abRst)) {
    if (-not $ab.ok)            { $problems += "after a client abandoned by $($ab.mode), the next client was not served: $($ab.error)" }
    elseif ($ab.echo -ne 'GET') { $problems += "after a client abandoned by $($ab.mode), the next client's echo was '$($ab.echo)'" }
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
Write-Host "  P-256:X25519 client retried ($($hrr.hellos) ClientHellos) and got its echo"
Write-Host "  P-256-only control refused with alert $($p256.peerAlert)"
Write-Host "  ecdsa-only client verified a $($ecdsa.peerSig) CertificateVerify and the chain, and got its echo"
Write-Host "  after an abandon by FIN the next client was served in $($abFin.seconds) s, after one by RST in $($abRst.seconds) s"
exit 0
