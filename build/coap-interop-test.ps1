# coap-interop-test.ps1 -- our CoAP client against a server we did not write
#
# codex/test/apps/coap-loopback drives CoapEndpoint against datagrams built
# by our own Coap codec. That proves the two halves agree with each other and
# nothing else -- the failure the compression post-mortem in
# docs/PM/Active/Stories/BrotliBeatsOpus.md exists to describe. The oracle
# here is aiocoap, an independent RFC 7252 implementation.
#
# It found a real bug on its first run: coap-text-to-bytes converted a
# Uri-Path segment with char-code, which answers the CCE code point rather
# than the ASCII one, so every path Codex had ever built named a resource no
# server could match. The loopback could not see it because both halves used
# the same mapping.
#
# TWO CASES, and the second is the point:
#   1. GET a resource that exists   -> 2.05 Content (69) with the exact bytes
#   2. GET a resource that does not -> 4.04 Not Found (132), no payload
# Without the second, "the response code is parsed" is a claim nothing
# checks: a client that always reported success would pass case 1.
#
# Usage: coap-interop-test.ps1 [-Kernel <cdx>] [-Port <n>] [-KeepArtifacts]

param(
    [string]$Kernel = '',
    [int]$Port = 0,
    [int]$RunSeconds = 40,
    [switch]$KeepArtifacts
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo   = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
# test-output, not build-output: build.ps1's clean phase removes
# build-output, so a gate run between the failure and the diagnosis would
# delete exactly the guest log needed to read it.
$Out    = Join-Path $Repo 'test-output\coap-interop'
$Client = Join-Path $Repo 'tools\coap-client.codex'
$Server = Join-Path $Repo 'build\coap-server.py'
$Vm     = Join-Path $Repo 'tools\codex-vm.exe'
$Python = 'D:\Python311\python.exe'

# The guest asks the gateway, and codex-vm maps the gateway to the host's
# loopback. The port is fixed at 5683 in the client because that is CoAP's
# assigned port; -Port exists so a busy box can move both ends together.
if ($Port -eq 0) { $Port = 5683 }

# The harness owns both of these. The client does not know the payload and
# the server does not know what the client will check.
$ResourceName = 'codex'
$PayloadText  = 'temp=21.5C'

New-Item -ItemType Directory -Force $Out | Out-Null

function Fail($msg) { Write-Host "coap-interop: FAIL -- $msg"; exit 1 }

foreach ($t in @($Python, $Vm, $Server, $Client)) {
    if (-not (Test-Path $t)) { Fail "missing: $t" }
}

& $Python -c "import aiocoap" 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { Fail "aiocoap is not installed for $Python (pip install aiocoap)" }

# ---------------------------------------------------------------------------
# The expectation, computed here from the string the server was told to
# serve. Never read back out of the guest.
# ---------------------------------------------------------------------------
$expectBytes = (([System.Text.Encoding]::ASCII.GetBytes($PayloadText)) | ForEach-Object { [int]$_ }) -join ','

# ---------------------------------------------------------------------------
# The client binary.
# ---------------------------------------------------------------------------
if ($Kernel -eq '') {
    $Kernel = Join-Path $Out 'coap-client.cdx'
    Write-Host "coap-interop: compiling tools/coap-client.codex"
    & (Join-Path $Repo 'build\compile.ps1') -Src $Client -Out $Kernel -Log (Join-Path $Out 'coap-client.log') | Out-Null
    if (-not (Test-Path $Kernel)) { Fail "coap-client did not compile -- see $Out\coap-client.log" }
}

# ---------------------------------------------------------------------------
# Boot the oracle, then the guest.
# ---------------------------------------------------------------------------
$srvOut = Join-Path $Out 'server.out'
$srvErr = Join-Path $Out 'server.err'
$srv = Start-Process -FilePath $Python `
    -ArgumentList $Server, $ResourceName, $PayloadText, $Port `
    -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput $srvOut -RedirectStandardError $srvErr

$vmOut = Join-Path $Out 'guest.out'
$vmErr = Join-Path $Out 'guest.err'
try {
    Start-Sleep -Seconds 3
    if ($srv.HasExited) { Fail "aiocoap server exited immediately -- $(Get-Content $srvErr -Raw)" }

    $vm = Start-Process -FilePath $Vm `
        -ArgumentList '-kernel', $Kernel, '-headless', '-mem', '3072', '-output', $vmOut `
        -PassThru -WindowStyle Hidden -RedirectStandardError $vmErr
    if (-not $vm.WaitForExit($RunSeconds * 1000)) {
        Stop-Process -Id $vm.Id -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 500
    }
}
finally {
    if (-not $srv.HasExited) { Stop-Process -Id $srv.Id -Force -ErrorAction SilentlyContinue }
}

if (-not (Test-Path $vmOut)) { Fail "guest produced no output at all" }
$guest = Get-Content $vmOut
Write-Host "coap-interop: guest said --"
$guest | ForEach-Object { Write-Host "  $_" }
Write-Host "coap-interop: oracle said -- $(Get-Content $srvOut -Raw)".TrimEnd()

# ---------------------------------------------------------------------------
# Verdict.
# ---------------------------------------------------------------------------
$problems = @()

$hit = $guest | Where-Object { $_ -like 'coap hit *' } | Select-Object -First 1
$miss = $guest | Where-Object { $_ -like 'coap miss *' } | Select-Object -First 1

if (-not $hit) { $problems += "guest never reported the 'hit' exchange" }
else {
    if ($hit -notmatch 'done=True')  { $problems += "hit did not complete: $hit" }
    if ($hit -notmatch 'code=69')    { $problems += "hit code was not 2.05 Content (69): $hit" }
    if ($hit -notmatch [regex]::Escape("payload=$expectBytes")) {
        $problems += "hit payload was not the served bytes ($expectBytes): $hit"
    }
}

# The control must fail, and it must fail as a 4.04 rather than by timing
# out -- a control that fails because nothing answered proves nothing.
if (-not $miss) { $problems += "guest never reported the 'miss' exchange" }
else {
    if ($miss -notmatch 'done=True') { $problems += "miss never got a response at all (server silent, not 4.04): $miss" }
    if ($miss -notmatch 'code=132')  { $problems += "miss code was not 4.04 Not Found (132): $miss" }
}

if (-not $KeepArtifacts) { Remove-Item $vmErr, $srvErr -Force -ErrorAction SilentlyContinue }

if ($problems.Count -gt 0) {
    Write-Host ''
    foreach ($p in $problems) { Write-Host "  $p" }
    Fail "$($problems.Count) problem(s)"
}

Write-Host ''
Write-Host "coap-interop: OK"
Write-Host "  2.05 Content from aiocoap, payload byte-exact ($expectBytes)"
Write-Host "  control: 4.04 Not Found for a resource the server does not have"
exit 0
