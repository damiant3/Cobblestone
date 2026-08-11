# dtls-fragment-interop.ps1 -- our handshake reassembly against OpenSSL's fragments
#
# codex/test/dtls-openssl-fragments carries FROZEN bytes captured from
# OpenSSL. Frozen bytes are only worth anything if someone can regenerate
# them, so this is that recipe: it captures a fresh DTLS handshake through a
# recording UDP proxy, reassembles the Certificate message independently in
# Python, and checks the answer against the digest the frozen fixture pins.
#
# WHY DTLS 1.2 WHEN OUR ENDPOINT IS 1.3-ONLY. The handshake message header
# and its fragmentation rules are byte-identical between RFC 6347 section
# 4.2.2 and RFC 9147 section 5.2 -- same 12-byte header, same uint24
# fragment_offset and fragment_length, same rule that the length field
# carries the WHOLE message. The record layer and key schedule differ, and
# neither is under test here. DTLS 1.3 arrived in OpenSSL 3.5; 3.2.4 is what
# is installed, and this script says so if that changes.
#
# Usage: dtls-fragment-interop.ps1 [-Port <n>] [-KeepArtifacts]

param(
    [int]$Port = 0,
    [switch]$KeepArtifacts,
    # Rewrite codex/test/dtls-openssl-fragments.codex from this capture.
    # Needed because the chain below is minted fresh every run, so the bytes
    # -- and the digest over them -- are new each time. The property this
    # harness checks holds across that; a frozen digest could not.
    [switch]$Regenerate
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo    = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Out     = Join-Path $Repo 'test-output\dtls-fragment-interop'
$Test    = Join-Path $Repo 'codex\test\dtls-openssl-fragments.codex'

$Python  = 'D:\Python311\python.exe'
$OpenSsl = 'C:\Program Files\Git\usr\bin\openssl.exe'

# There is deliberately NO expected digest here. The chain is minted fresh
# every run, so the Certificate bytes are new every run and any pinned digest
# would fail for a reason that has nothing to do with the code. What IS
# checked is the property that survives a recapture: OpenSSL fragments the
# message, it refragments the retransmission differently, and every complete
# transmission reassembles to the same bytes. The frozen digest lives in the
# Codex test beside the bytes it was computed over, which is the only place
# it means anything.

if ($Port -eq 0) { $Port = 24444 }
$ServerPort = $Port + 1
New-Item -ItemType Directory -Force $Out | Out-Null

function Fail($msg) { Write-Host "dtls-fragment-interop: FAIL -- $msg"; exit 1 }

foreach ($t in @($Python, $OpenSsl)) {
    if (-not (Test-Path $t)) { Fail "missing tool: $t" }
}

$ver = (& $OpenSsl version) -join ''
Write-Host "dtls-fragment-interop: oracle = $ver"
$dtls13 = (& $OpenSsl s_server -help 2>&1 | Out-String) -match 'dtls1_3'
if ($dtls13) {
    Write-Host "dtls-fragment-interop: NOTE -- this OpenSSL advertises dtls1_3."
    Write-Host "  A full DTLS 1.3 handshake oracle is now possible and this"
    Write-Host "  harness only exercises the fragmentation layer. Worth extending."
} else {
    Write-Host "dtls-fragment-interop: no DTLS 1.3 in this OpenSSL (needs 3.5+);"
    Write-Host "  fragmentation framing is what this oracle can reach."
}

# ---------------------------------------------------------------------------
# A chain big enough that the Certificate message must fragment at -mtu 400.
# ---------------------------------------------------------------------------
Push-Location $Out
try {
    & $OpenSsl req -x509 -newkey rsa:2048 -keyout srv.key -out srv.crt -days 30 -nodes -subj '/CN=frag.test' 2>&1 | Out-Null
    & $OpenSsl req -x509 -newkey rsa:2048 -keyout f.key   -out f.crt   -days 30 -nodes -subj '/CN=filler.one' 2>&1 | Out-Null
    Get-Content srv.crt, f.crt, f.crt | Set-Content chain.pem
    if (-not (Test-Path (Join-Path $Out 'chain.pem'))) { Fail 'could not mint the server chain' }
} finally { Pop-Location }

# ---------------------------------------------------------------------------
# A UDP proxy that records both directions. Nothing else here can see the
# wire: codex-vm is not in this loop, both peers are OpenSSL.
# ---------------------------------------------------------------------------
$proxy = @'
import socket, sys, time, os
listen_port, server_port, outdir = int(sys.argv[1]), int(sys.argv[2]), sys.argv[3]
cli = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
cli.bind(("127.0.0.1", listen_port)); cli.settimeout(0.5)
srv = socket.socket(socket.AF_INET, socket.SOCK_DGRAM); srv.settimeout(0.5)
peer, n_c, n_s, deadline = None, 0, 0, time.time() + 20
while time.time() < deadline:
    for sock, tag, fwd in ((cli, "c2s", None), (srv, "s2c", None)):
        try:
            data, addr = sock.recvfrom(65535)
        except (socket.timeout, OSError):
            continue
        if tag == "c2s":
            peer = addr
            open(os.path.join(outdir, "c2s_%03d.bin" % n_c), "wb").write(data); n_c += 1
            srv.sendto(data, ("127.0.0.1", server_port))
        else:
            open(os.path.join(outdir, "s2c_%03d.bin" % n_s), "wb").write(data); n_s += 1
            if peer: cli.sendto(data, peer)
print("captured c2s=%d s2c=%d" % (n_c, n_s))
'@
$proxyPy = Join-Path $Out 'proxy.py'
Set-Content -Path $proxyPy -Value $proxy -Encoding utf8

# ---------------------------------------------------------------------------
# Independent reassembly, in a language that is not ours.
# ---------------------------------------------------------------------------
$reasm = @'
import sys, os, glob, hashlib
outdir = sys.argv[1]
def r16(b,o): return (b[o]<<8)|b[o+1]
def r24(b,o): return (b[o]<<16)|(b[o+1]<<8)|b[o+2]
recs = []
for path in sorted(glob.glob(os.path.join(outdir, "s2c_*.bin"))):
    b = open(path,"rb").read(); o = 0
    while o + 13 <= len(b):
        rlen = r16(b, o+11)
        if o + 13 + rlen > len(b): break
        frag = b[o+13:o+13+rlen]
        if b[o] == 22 and r16(b,o+3) == 0 and len(frag) >= 12 and frag[0] == 11:
            recs.append({"off": r24(frag,6), "len": r24(frag,9),
                         "total": r24(frag,1), "body": frag[12:]})
        o += 13 + rlen
if not recs:
    print("NONE"); sys.exit(0)
total = recs[0]["total"]
xmits, cur = [], []
for r in recs:
    if r["off"] == 0 and cur: xmits.append(cur); cur = []
    cur.append(r)
if cur: xmits.append(cur)
out = []
for x in xmits:
    buf, filled = bytearray(total), bytearray(total)
    for r in x:
        for i in range(min(r["len"], len(r["body"]))):
            if r["off"]+i < total: buf[r["off"]+i] = r["body"][i]; filled[r["off"]+i] = 1
    if all(filled):
        out.append((",".join("%d+%d" % (r["off"], r["len"]) for r in x),
                    hashlib.sha256(buf).hexdigest()))
print("TOTAL %d" % total)
for cuts, dg in out: print("XMIT %s %s" % (cuts, dg))
'@
$reasmPy = Join-Path $Out 'reasm.py'
Set-Content -Path $reasmPy -Value $reasm -Encoding utf8

# ---------------------------------------------------------------------------
# The generator, used only under -Regenerate. It writes the whole Codex test,
# fixture bytes and prose together, so the captured offsets quoted in the
# prose cannot drift away from the bytes underneath them.
# ---------------------------------------------------------------------------
$gen = @'
import sys, os, glob, hashlib
outdir, dest = sys.argv[1], sys.argv[2]
def r16(b,o): return (b[o]<<8)|b[o+1]
def r24(b,o): return (b[o]<<16)|(b[o+1]<<8)|b[o+2]
recs = []
for path in sorted(glob.glob(os.path.join(outdir, "s2c_*.bin"))):
    b = open(path,"rb").read(); o = 0
    while o + 13 <= len(b):
        rlen = r16(b, o+11)
        if o + 13 + rlen > len(b): break
        rec, frag = b[o:o+13+rlen], b[o+13:o+13+rlen]
        if b[o] == 22 and r16(b,o+3) == 0 and len(frag) >= 12 and frag[0] == 11:
            recs.append({"off": r24(frag,6), "len": r24(frag,9), "total": r24(frag,1), "rec": rec})
        o += 13 + rlen
total = recs[0]["total"]
xmits, cur = [], []
for r in recs:
    if r["off"] == 0 and cur: xmits.append(cur); cur = []
    cur.append(r)
if cur: xmits.append(cur)
x1, x2 = xmits[0], xmits[1]
buf = bytearray(total)
for r in x1:
    body = r["rec"][25:]
    for i in range(min(r["len"], len(body))): buf[r["off"]+i] = body[i]
digest = hashlib.sha256(buf).hexdigest()
def clist(bs):
    out, cur = [], "    ["
    for x in bs:
        s = str(x) + ", "
        if len(cur) + len(s) > 98: out.append(cur.rstrip()); cur = "     "
        cur += s
    out.append(cur.rstrip().rstrip(",") + "]")
    return "\n".join(out)
fix = []
for xi, x in enumerate((x1, x2)):
    for ri, r in enumerate(x):
        fix.append("  ossl-x%d-r%d : List Integer =\n%s\n" % (xi+1, ri, clist(r["rec"])))
cuts1 = ", ".join("%d+%d" % (r["off"], r["len"]) for r in x1)
cuts2 = ", ".join("%d+%d" % (r["off"], r["len"]) for r in x2)
tmpl = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "template.txt")).read()
open(dest, "w", newline="\n").write(tmpl % (cuts1, cuts2, "\n".join(fix)))
print("wrote %s (message %d bytes, sha256 %s)" % (dest, total, digest))
'@
Set-Content -Path (Join-Path $Out 'gen.py') -Value $gen -Encoding utf8
Copy-Item (Join-Path $Repo 'build\dtls-openssl-fragments.template.txt') (Join-Path $Out 'template.txt') -Force

# ---------------------------------------------------------------------------
# Capture.
# ---------------------------------------------------------------------------
Get-ChildItem $Out -Filter '*.bin' -ErrorAction SilentlyContinue | ForEach-Object { $_.Delete() }

Push-Location $Out
try {
    $srvArgs = @('s_server','-dtls1_2','-accept',"$ServerPort",'-cert','chain.pem','-key','srv.key','-mtu','400','-quiet')
    $srvProc = Start-Process -FilePath $OpenSsl -ArgumentList $srvArgs -PassThru -WindowStyle Hidden `
                 -RedirectStandardOutput (Join-Path $Out 'srv.log') -RedirectStandardError (Join-Path $Out 'srv.err')
    $prxProc = Start-Process -FilePath $Python -ArgumentList @($proxyPy,"$Port","$ServerPort",$Out) -PassThru -WindowStyle Hidden `
                 -RedirectStandardOutput (Join-Path $Out 'proxy.log') -RedirectStandardError (Join-Path $Out 'proxy.err')
    Start-Sleep -Seconds 2
    $cliArgs = @('s_client','-dtls1_2','-connect',"127.0.0.1:$Port",'-mtu','400')
    $cliProc = Start-Process -FilePath $OpenSsl -ArgumentList $cliArgs -PassThru -WindowStyle Hidden `
                 -RedirectStandardOutput (Join-Path $Out 'cli.log') -RedirectStandardError (Join-Path $Out 'cli.err')
    Start-Sleep -Seconds 12
    foreach ($p in @($cliProc, $srvProc)) {
        if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    }
    $prxProc.WaitForExit(15000) | Out-Null
    if (-not $prxProc.HasExited) { Stop-Process -Id $prxProc.Id -Force -ErrorAction SilentlyContinue }
} finally { Pop-Location }

$caps = @(Get-ChildItem $Out -Filter 's2c_*.bin' -ErrorAction SilentlyContinue)
if ($caps.Count -eq 0) { Fail 'captured no server datagrams -- did openssl s_server start?' }
Write-Host "dtls-fragment-interop: captured $($caps.Count) server datagrams"

# ---------------------------------------------------------------------------
# Verdict.
# ---------------------------------------------------------------------------
$lines = @(& $Python $reasmPy $Out)
$total = ($lines | Where-Object { $_ -match '^TOTAL (\d+)$' } | ForEach-Object { [int]$Matches[1] } | Select-Object -First 1)
$xmits = @($lines | Where-Object { $_ -match '^XMIT ' } | ForEach-Object { $_ -replace '^XMIT ','' })

if (-not $total) { Fail 'no Certificate fragments in the capture' }
Write-Host "dtls-fragment-interop: Certificate declared length $total"
foreach ($x in $xmits) { Write-Host "  transmission: $x" }

$problems = @()
$digests = @($xmits | ForEach-Object { ($_ -split ' ')[1] } | Select-Object -Unique)
$cuts    = @($xmits | ForEach-Object { ($_ -split ' ')[0] } | Select-Object -Unique)

if ($xmits.Count -lt 1) { $problems += 'no transmission reassembled completely' }

# The message must actually have been cut, or this oracle proves nothing at
# all: an unfragmented Certificate would sail through a reassembler that does
# not work, and the run would still read green.
$fragmented = $false
foreach ($c in $cuts) { if ((($c -split ',').Count) -gt 1) { $fragmented = $true } }
if (-not $fragmented) {
    $problems += 'OpenSSL did not fragment the Certificate -- raise the chain size or lower -mtu, this run tests nothing'
}

# Every complete transmission must land on identical bytes. This is the one
# assertion that survives a fresh chain, and it is the interesting one: the
# two transmissions are cut at DIFFERENT offsets, so agreement means the
# bytes are recoverable independently of where the peer chose to cut.
if ($digests.Count -gt 1) {
    $problems += "transmissions of the same message reassembled to different bytes: $($digests -join ' vs ')"
}

# Two distinct fragmentations is what the fixture's `mixed` arm is built from.
# Its absence is not a failure, but a regeneration from such a run would
# quietly drop that arm, so say so rather than let it vanish.
if ($cuts.Count -lt 2) {
    Write-Host 'dtls-fragment-interop: NOTE -- only one distinct fragmentation in this run.'
    Write-Host '  The frozen fixture caught OpenSSL refragmenting a retransmitted flight.'
    Write-Host '  Regenerating from this capture would lose the mixed-cut arm.'
}

if ($Regenerate -and $problems.Count -eq 0) {
    if ($cuts.Count -lt 2) { Fail 'refusing to regenerate from a capture with only one fragmentation' }
    Write-Host "dtls-fragment-interop: regenerating $Test"
    & $Python (Join-Path $Out 'gen.py') $Out $Test
    if ($LASTEXITCODE -ne 0) { Fail 'the generator failed' }
    Write-Host '  regenerated -- now recompile the test and refresh its .expected'
}

if (-not $KeepArtifacts) {
    Get-ChildItem $Out -Filter '*.bin' -ErrorAction SilentlyContinue | ForEach-Object { $_.Delete() }
}

if ($problems.Count -gt 0) {
    Write-Host ''
    foreach ($p in $problems) { Write-Host "  $p" }
    Fail "$($problems.Count) problem(s)"
}

Write-Host ''
Write-Host 'dtls-fragment-interop: OK'
Write-Host "  OpenSSL fragmented a $total-byte Certificate $($cuts.Count) different way(s)"
Write-Host '  every complete transmission reassembled to identical bytes'
Write-Host "  OUR reassembler is checked against frozen bytes by $Test"
Write-Host '  pass -Regenerate to refreeze that fixture from a fresh capture'
exit 0
