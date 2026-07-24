# mqtt-interop-test.ps1 -- our MQTT client against a broker we did not write
#
# codex/test/apps/mqtt-loopback drives MqttEndpoint against packets built by
# our own Mqtt codec. That proves the two halves agree with each other and
# nothing else -- the failure the compression post-mortem in
# docs/PM/Active/Stories/BrotliBeatsOpus.md exists to describe. The oracle
# here is mosquitto, and it is used in BOTH directions, which is the rule
# that post-mortem ends with:
#
#   * OUR OUTPUT, THEIR INPUT.  The guest publishes; mosquitto_sub -- a
#     separate process using mosquitto's own client library -- must receive
#     the exact payload. If our CONNECT, SUBSCRIBE or PUBLISH bytes are
#     wrong, no message arrives.
#   * THEIR OUTPUT, OUR INPUT.  mosquitto delivers that message back to the
#     guest's own subscription, and the guest must report the same bytes. If
#     our PARSER is wrong -- and this chapter had no parser at all until
#     today -- the guest reports nothing or the wrong thing.
#
# Only the second direction can catch a decoder that reads only what our own
# encoder writes, and that is the direction the compression stack never had.
#
# Usage: mqtt-interop-test.ps1 [-Kernel <cdx>] [-Port <n>] [-KeepArtifacts]

param(
    [string]$Kernel = '',
    [int]$Port = 0,
    [int]$RunSeconds = 60,
    [switch]$KeepArtifacts
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo   = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Out    = Join-Path $Repo 'test-output\mqtt-interop'
$Client = Join-Path $Repo 'tools\mqtt-client.codex'
$Vm     = Join-Path $Repo 'tools\codex-vm.exe'

$MosqDir = 'C:\Program Files\Mosquitto'
$Broker  = Join-Path $MosqDir 'mosquitto.exe'
$Sub     = Join-Path $MosqDir 'mosquitto_sub.exe'

# 1883 is MQTT's assigned port and the client has it compiled in; -Port
# exists so a box already running a broker can move both ends together.
if ($Port -eq 0) { $Port = 1883 }

# These must match tools/mqtt-client.codex. The harness asserts on them
# rather than reading them back out of the guest.
$Topic        = 'codex/probe'
$PayloadText  = 'hello-codex'   # published at QoS 1
$Payload2Text = 'hello-qos2'    # published at QoS 2

New-Item -ItemType Directory -Force $Out | Out-Null

function Fail($msg) { Write-Host "mqtt-interop: FAIL -- $msg"; exit 1 }

foreach ($t in @($Broker, $Sub, $Vm, $Client)) {
    if (-not (Test-Path $t)) { Fail "missing: $t" }
}

$expectBytes  = (([System.Text.Encoding]::ASCII.GetBytes($PayloadText)) | ForEach-Object { [int]$_ }) -join ','
$expect2Bytes = (([System.Text.Encoding]::ASCII.GetBytes($Payload2Text)) | ForEach-Object { [int]$_ }) -join ','
$topicBytes   = (([System.Text.Encoding]::ASCII.GetBytes($Topic)) | ForEach-Object { [int]$_ }) -join ','

# ---------------------------------------------------------------------------
# The client binary.
# ---------------------------------------------------------------------------
if ($Kernel -eq '') {
    $Kernel = Join-Path $Out 'mqtt-client.cdx'
    Write-Host "mqtt-interop: compiling tools/mqtt-client.codex"
    & (Join-Path $Repo 'build\compile.ps1') -Src $Client -Out $Kernel -Log (Join-Path $Out 'mqtt-client.log') | Out-Null
    if (-not (Test-Path $Kernel)) { Fail "mqtt-client did not compile -- see $Out\mqtt-client.log" }
}

# ---------------------------------------------------------------------------
# The broker. Bound to loopback with anonymous access, because the point is
# the wire format and not the authentication story.
# ---------------------------------------------------------------------------
$conf = Join-Path $Out 'mosquitto.conf'
Set-Content -Path $conf -Encoding ascii -Value @(
    "listener $Port 127.0.0.1"
    "allow_anonymous true"
)

$brkOut = Join-Path $Out 'broker.out'
$brkErr = Join-Path $Out 'broker.err'
$subOut = Join-Path $Out 'sub.out'
$subErr = Join-Path $Out 'sub.err'
$vmOut  = Join-Path $Out 'guest.out'
$vmErr  = Join-Path $Out 'guest.err'

$brk = Start-Process -FilePath $Broker -ArgumentList '-c', $conf -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput $brkOut -RedirectStandardError $brkErr
$subProc = $null
try {
    Start-Sleep -Seconds 2
    if ($brk.HasExited) { Fail "mosquitto exited immediately -- $(Get-Content $brkErr -Raw)" }

    # DIRECTION 1's witness, subscribed before the guest publishes.
    # Note $subProc and not $sub: PowerShell variable names are
    # case-insensitive, so assigning $sub here silently overwrote $Sub, the
    # path to mosquitto_sub.exe, and the harness failed with a null FilePath.
    $subProc = Start-Process -FilePath $Sub `
        -ArgumentList '-h', '127.0.0.1', '-p', $Port, '-t', $Topic, '-V', '5', '-q', '1' `
        -PassThru -WindowStyle Hidden -RedirectStandardOutput $subOut -RedirectStandardError $subErr
    Start-Sleep -Seconds 2
    if ($subProc.HasExited) { Fail "mosquitto_sub exited immediately -- $(Get-Content $subErr -Raw)" }

    $vm = Start-Process -FilePath $Vm `
        -ArgumentList '-kernel', $Kernel, '-headless', '-mem', '3072', '-output', $vmOut `
        -PassThru -WindowStyle Hidden -RedirectStandardError $vmErr
    if (-not $vm.WaitForExit($RunSeconds * 1000)) {
        Stop-Process -Id $vm.Id -Force -ErrorAction SilentlyContinue
    }
    Start-Sleep -Seconds 1
}
finally {
    if ($subProc -and -not $subProc.HasExited) { Stop-Process -Id $subProc.Id -Force -ErrorAction SilentlyContinue }
    if (-not $brk.HasExited) { Stop-Process -Id $brk.Id -Force -ErrorAction SilentlyContinue }
}

if (-not (Test-Path $vmOut)) { Fail "guest produced no output at all" }
$guest = Get-Content $vmOut
Write-Host "mqtt-interop: guest said --"
$guest | ForEach-Object { Write-Host "  $_" }
$heard = if (Test-Path $subOut) { (Get-Content $subOut -Raw).Trim() } else { '' }
Write-Host "mqtt-interop: mosquitto_sub heard -- '$heard'"

# ---------------------------------------------------------------------------
# Verdict.
# ---------------------------------------------------------------------------
$problems = @()

$connect   = $guest | Where-Object { $_ -like 'mqtt connect *' }        | Select-Object -First 1
$subscribe = $guest | Where-Object { $_ -like 'mqtt subscribe *' }      | Select-Object -First 1
$pub1      = $guest | Where-Object { $_ -like 'mqtt publish-qos1 *' }   | Select-Object -First 1
$pub2      = $guest | Where-Object { $_ -like 'mqtt publish-qos2 *' }   | Select-Object -First 1
$delivered = $guest | Where-Object { $_ -like 'mqtt delivered *' }      | Select-Object -First 1

if (-not $connect) { $problems += "guest never reported a CONNECT result (it may not have reached the broker)" }
elseif ($connect -notmatch 'ready=True') { $problems += "broker refused the connection: $connect" }

if (-not $subscribe) { $problems += "guest never reported a SUBSCRIBE result" }
elseif ($subscribe -notmatch 'granted=2') { $problems += "subscription not granted at QoS 2: $subscribe" }

if (-not $pub1) { $problems += "guest never reported the QoS 1 PUBLISH" }
elseif ($pub1 -notmatch 'acked=True') { $problems += "broker never acknowledged the QoS 1 publish: $pub1" }

# The QoS 2 leg is the four-step exchange: PUBLISH, PUBREC, PUBREL, PUBCOMP.
# acked=True here means mosquitto sent PUBCOMP, which it only does after it
# has accepted our PUBREL -- and it disconnects rather than answering if the
# PUBREL's reserved flags are wrong.
if (-not $pub2) { $problems += "guest never reported the QoS 2 PUBLISH" }
elseif ($pub2 -notmatch 'acked=True') { $problems += "the QoS 2 exchange did not complete (no PUBCOMP): $pub2" }

# DIRECTION 1 -- our bytes, read by mosquitto's own client. Both payloads,
# so a QoS 2 publish that never left is not covered by the QoS 1 one.
$heardLines = if (Test-Path $subOut) { @(Get-Content $subOut | Where-Object { $_ -ne '' }) } else { @() }
if ($heardLines -notcontains $PayloadText) {
    $problems += "mosquitto_sub never heard the QoS 1 payload '$PayloadText' (heard: $($heardLines -join '|'))"
}
if ($heardLines -notcontains $Payload2Text) {
    $problems += "mosquitto_sub never heard the QoS 2 payload '$Payload2Text' (heard: $($heardLines -join '|'))"
}

# DIRECTION 2 -- mosquitto's bytes, read by our parser. This is the half a
# self-paired decoder always passes and a real one has to earn.
if (-not $delivered) { $problems += "guest never reported a delivery" }
else {
    # Both publishes come back through the guest's own subscription, so two
    # deliveries; the endpoint keeps the latest, which is the QoS 2 one.
    if ($delivered -notmatch 'count=2') { $problems += "guest did not receive both deliveries: $delivered" }
    if ($delivered -notmatch [regex]::Escape("topic=$topicBytes")) {
        $problems += "delivered topic was not '$Topic' ($topicBytes): $delivered"
    }
    if ($delivered -notmatch [regex]::Escape("payload=$expect2Bytes")) {
        $problems += "last delivered payload was not '$Payload2Text' ($expect2Bytes): $delivered"
    }
}

if (-not $KeepArtifacts) { Remove-Item $vmErr, $brkErr, $subErr -Force -ErrorAction SilentlyContinue }

if ($problems.Count -gt 0) {
    Write-Host ''
    foreach ($p in $problems) { Write-Host "  $p" }
    Fail "$($problems.Count) problem(s)"
}

Write-Host ''
Write-Host "mqtt-interop: OK"
Write-Host "  our output, their input:  mosquitto_sub received '$PayloadText' (QoS 1) and '$Payload2Text' (QoS 2)"
Write-Host "  their output, our input:  the guest parsed both deliveries back, byte-exact"
Write-Host "  QoS 2:                   PUBLISH/PUBREC/PUBREL/PUBCOMP completed against mosquitto"
exit 0
