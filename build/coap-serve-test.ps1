# coap-serve-test.ps1 -- our CoAP SERVER, asked by a client we did not write
#
# build/coap-interop-test.ps1 proves Codex can ask a CoAP server for
# something. This proves it can BE one, which is the role an IoT device
# actually plays: an LwM2M client is a CoAP server, and a sensor node
# answers rather than asks.
#
# It also exercises the other new half of codex-vm's NAT. Outbound UDP made
# the guest a datagram client; `-portfwd udp:<host>:<guest>` makes it a
# server, giving each host client a synthetic gateway port so the guest's
# reply can be routed back to the right one.
#
# TWO REQUESTS, and the second is the control:
#   1. GET the resource the server serves -> 2.05 Content with its payload
#   2. GET anything else                  -> 4.04 Not Found
# Without the second, a server that answered every request identically
# would pass.
#
# Usage: coap-serve-test.ps1 [-Kernel <cdx>] [-Port <n>] [-KeepArtifacts]

param(
    [string]$Kernel = '',
    [int]$Port = 0,
    [int]$BootSeconds = 22,
    [switch]$KeepArtifacts
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo   = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Out    = Join-Path $Repo 'test-output\coap-serve'
$Server = Join-Path $Repo 'tools\coap-server.codex'
$Vm     = Join-Path $Repo 'tools\codex-vm.exe'
$Python = 'D:\Python311\python.exe'

if ($Port -eq 0) { $Port = 15683 }

# These must match tools/coap-server.codex.
$ResourceName = 'codex'
$PayloadText  = 'on-device'

New-Item -ItemType Directory -Force $Out | Out-Null

function Fail($msg) { Write-Host "coap-serve: FAIL -- $msg"; exit 1 }

foreach ($t in @($Python, $Vm, $Server)) { if (-not (Test-Path $t)) { Fail "missing: $t" } }

& $Python -c "import aiocoap" 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { Fail "aiocoap is not installed for $Python (pip install aiocoap)" }

if ($Kernel -eq '') {
    $Kernel = Join-Path $Out 'coap-server.cdx'
    Write-Host "coap-serve: compiling tools/coap-server.codex"
    & (Join-Path $Repo 'build\compile.ps1') -Src $Server -Out $Kernel -Log (Join-Path $Out 'coap-server.log') | Out-Null
    if (-not (Test-Path $Kernel)) { Fail "coap-server did not compile -- see $Out\coap-server.log" }
}

# ---------------------------------------------------------------------------
# The client. aiocoap, asking twice, reporting what came back as JSON so the
# verdict below reads codes and payloads rather than prose.
# ---------------------------------------------------------------------------
$client = @'
import asyncio, json, sys
from aiocoap import Context, Message, GET

async def ask(ctx, uri):
    try:
        resp = await asyncio.wait_for(ctx.request(Message(code=GET, uri=uri)).response, timeout=20)
        return {"code": str(resp.code), "payload": resp.payload.decode("ascii", "replace")}
    except Exception as e:
        return {"error": "%s: %s" % (type(e).__name__, e)}

async def main():
    port, name = int(sys.argv[1]), sys.argv[2]
    ctx = await Context.create_client_context()
    hit = await ask(ctx, "coap://127.0.0.1:%d/%s" % (port, name))
    miss = await ask(ctx, "coap://127.0.0.1:%d/nosuch" % port)
    await ctx.shutdown()
    print(json.dumps({"hit": hit, "miss": miss}))

asyncio.run(main())
'@
$clientPy = Join-Path $Out 'client.py'
Set-Content -Path $clientPy -Value $client -Encoding utf8

$vmOut = Join-Path $Out 'guest.out'
$vmErr = Join-Path $Out 'guest.err'

$vm = Start-Process -FilePath $Vm `
    -ArgumentList '-kernel', $Kernel, '-headless', '-mem', '3072', '-output', $vmOut, `
                  '-portfwd', "udp:$($Port):5683" `
    -PassThru -WindowStyle Hidden -RedirectStandardError $vmErr
try {
    # Do not probe the port to check readiness: the forward accepts host
    # datagrams long before the guest is behind it, and a probe datagram
    # would be consumed by the server's bounded request count.
    Start-Sleep -Seconds $BootSeconds
    if ($vm.HasExited) { Fail "guest exited during boot -- see $vmOut" }

    $raw = & $Python $clientPy $Port $ResourceName 2>&1
    try { $res = ($raw | Select-Object -Last 1 | ConvertFrom-Json) }
    catch { Fail "aiocoap client produced no JSON: $raw" }
}
finally {
    Start-Sleep -Seconds 2
    if (-not $vm.HasExited) { Stop-Process -Id $vm.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 500
}

Write-Host "coap-serve: aiocoap said -- $($res | ConvertTo-Json -Compress)"
if (Test-Path $vmOut) {
    Write-Host "coap-serve: guest said --"
    Get-Content $vmOut | ForEach-Object { Write-Host "  $_" }
}

# ---------------------------------------------------------------------------
# Verdict.
# ---------------------------------------------------------------------------
$problems = @()

if (-not $res.hit) { $problems += "no result for the resource that exists" }
elseif ($res.hit.PSObject.Properties.Name -contains 'error') {
    $problems += "aiocoap could not reach our server: $($res.hit.error)"
} else {
    if ($res.hit.code -notmatch '2\.05') { $problems += "hit code was $($res.hit.code), expected 2.05 Content" }
    if ($res.hit.payload -ne $PayloadText) { $problems += "hit payload was '$($res.hit.payload)', expected '$PayloadText'" }
}

# The control must come back 4.04 rather than time out: a control that fails
# because nothing answered proves nothing about the server.
if (-not $res.miss) { $problems += "no result for the control request" }
elseif ($res.miss.PSObject.Properties.Name -contains 'error') {
    $problems += "control did not get an answer at all (server silent, not 4.04): $($res.miss.error)"
} elseif ($res.miss.code -notmatch '4\.04') {
    $problems += "control code was $($res.miss.code), expected 4.04 Not Found"
}

if (-not $KeepArtifacts) { Remove-Item $vmErr -Force -ErrorAction SilentlyContinue }

if ($problems.Count -gt 0) {
    Write-Host ''
    foreach ($p in $problems) { Write-Host "  $p" }
    Fail "$($problems.Count) problem(s)"
}

Write-Host ''
Write-Host "coap-serve: OK"
Write-Host "  aiocoap fetched /$ResourceName from the guest: 2.05, '$PayloadText'"
Write-Host "  control: the guest answered 4.04 for a resource it does not serve"
exit 0
