# hosted-http-arm.ps1 -- PRISM-10: a hosted target serves HTTP over the host's
# TCP, on both hosted targets, answering the SAME route function the bare-metal
# server takes.
#
# WSL is the verification bed by Damian's ruling of 2026-08-28. Verification
# only: nothing here is on the build path.
#
# WHAT THE ARM IS ACTUALLY FOR. Sockets were proved by hosted-socket-arm.ps1;
# echoing bytes is not serving HTTP. Two claims are under test here and neither
# is visible from an echo:
#
#   /hello       the subject's OWN route function answered, so http-parse-request
#                and http-encode work end to end over host-socket.
#   /api/health  a route the subject never wrote answered, so the standard
#                endpoints in Works chapter WebRoute are reached from the hosted
#                server. That is the whole point of splitting WebRoute out of
#                WebServer: one route function serves both targets, and a route
#                answering /api/health on one target and 404 on the other is the
#                per-target rewrite PRISM-10 exists to prevent.
#
# THE CONTROLS. A client that cannot connect looks exactly like a server that
# never listened (L-VACUOUS), so:
#   positive: the same client function against a .NET HttpListener must pass.
#   negative: the same client against a dead port must fail.
[CmdletBinding()]
param(
    [string]$Kernel = '',
    [switch]$ControlsOnly,
    [ValidateSet('linux','windows','both')][string]$Target = 'both',
    [switch]$ReuseBinaries,
    [string]$WorkDir = '',
    # The subject binds $PortBase + hosted-kind: linux base+1, windows base+2.
    # The same base is in codex/test/hosted-http.codex. The control ports sit
    # clear of both.
    [int]$PortBase = 34570,
    [int]$ControlPort = 34610,
    [int]$DeadPort = 34611,
    # The subject and what to ask it. Parameterised so a second subject reuses
    # THESE controls rather than carrying a second copy of them: two arms with
    # two copies of one client is the pair that drifts, and a control that has
    # drifted is worse than no control.
    #   -Probe 'path=text' entries. Entries sharing a path are grouped into ONE
    #   request and every pattern is checked against that one answer, because a
    #   subject with a request budget counts CONNECTIONS and not assertions.
    [string]$Subject = 'codex\test\hosted-http.codex',
    [string]$Stem = 'hosted-http',
    [string[]]$Probe = @('/hello=200', '/hello="hello":"hosted"', '/api/health="status":"ok"'),
    # What the subject prints when it has finished. Empty means do not check.
    [string]$DoneLine = 'served 2'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
if (-not $WorkDir) { $WorkDir = Join-Path $env:TEMP ('hosted-http-' + [Guid]::NewGuid().ToString('n').Substring(0,8)) }
New-Item -ItemType Directory -Force $WorkDir | Out-Null
if (-not $ControlsOnly -and -not $Kernel) { throw 'pass -Kernel, or -ControlsOnly to prove the instrument alone' }
if ($Kernel -and -not (Test-Path $Kernel)) { throw "kernel not found: $Kernel" }

$failures = @()
function Fail($m) { $script:failures += $m }
function Get-Port([string]$t) { if ($t -eq 'windows') { $PortBase + 2 } else { $PortBase + 1 } }

# One client, used by the arm and by both controls. Raw HTTP/1.0 on a socket
# rather than Invoke-WebRequest, because the subject answers one request per
# connection and closes; a client that pools or retries would hide that.
# Returns the whole response text, or $null when nothing was readable.
function Ask-Http([int]$p, [string]$path, [int]$timeoutMs = 4000) {
    $c = [System.Net.Sockets.TcpClient]::new()
    try {
        $iar = $c.BeginConnect('127.0.0.1', $p, $null, $null)
        if (-not $iar.AsyncWaitHandle.WaitOne($timeoutMs)) { return $null }
        $c.EndConnect($iar)
        $c.SendTimeout = $timeoutMs
        $c.ReceiveTimeout = $timeoutMs
        $s = $c.GetStream()
        $req = "GET $path HTTP/1.0`r`nHost: 127.0.0.1`r`n`r`n"
        $b = [Text.Encoding]::ASCII.GetBytes($req)
        $s.Write($b, 0, $b.Length); $s.Flush()
        $sb = [Text.StringBuilder]::new()
        $buf = [byte[]]::new(4096)
        while ($true) {
            $n = $s.Read($buf, 0, $buf.Length)
            if ($n -le 0) { break }
            [void]$sb.Append([Text.Encoding]::ASCII.GetString($buf, 0, $n))
        }
        if ($sb.Length -eq 0) { return $null }
        return $sb.ToString()
    } catch { return $null } finally { $c.Close() }
}

# Readiness off the subject's own stdout. A probe that dials the port is
# answered by accept and consumed as a served request, which would leave the
# arm one request short and read as a broken server (measured on the socket
# arm, 2026-09-08).
function Wait-Listening([string]$outPath, [int]$seconds = 20) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $t = Get-Content $outPath -Raw -ErrorAction SilentlyContinue
        if ($t -and $t -match 'listen 0') { return $true }
        Start-Sleep -Milliseconds 200
    }
    return $false
}

# ---- positive control -------------------------------------------------------
$http = [System.Net.HttpListener]::new()
$http.Prefixes.Add("http://127.0.0.1:$ControlPort/")
$http.Start()
$ps = [PowerShell]::Create()
$null = $ps.AddScript({
    param($l)
    $ctx = $l.GetContext()
    $body = [Text.Encoding]::ASCII.GetBytes('{"hello":"control"}')
    $ctx.Response.ContentType = 'application/json'
    $ctx.Response.OutputStream.Write($body, 0, $body.Length)
    $ctx.Response.Close()
}).AddArgument($http)
$handle = $ps.BeginInvoke()
$ctlText = Ask-Http $ControlPort '/hello'
$null = $handle.AsyncWaitHandle.WaitOne(5000)
try { $ps.EndInvoke($handle) } catch { }
$ps.Dispose()
$http.Stop()
if ($null -eq $ctlText -or $ctlText -notmatch 'control') {
    Fail "POSITIVE CONTROL: a .NET HttpListener on $ControlPort answered '$ctlText', so the client function is wrong and every result below means nothing"
}

# ---- negative control -------------------------------------------------------
$deadText = Ask-Http $DeadPort '/hello' 1500
if ($null -ne $deadText) { Fail "NEGATIVE CONTROL: port $DeadPort answered '$deadText' with nothing listening, so a pass is not evidence" }

if ($ControlsOnly) {
    if ($failures.Count -gt 0) {
        Write-Host "hosted-http-arm [controls only]: $($failures.Count) FAILURE(S)"
        foreach ($f in $failures) { Write-Host "  $f" }
        exit 1
    }
    Write-Host 'hosted-http-arm [controls only]: the client reads a real HTTP server and does not read a dead port, so the instrument reads'
    exit 0
}

# ---- the arm ----------------------------------------------------------------
$src = Join-Path $Repo $Subject

function Invoke-Target([string]$tgt) {
    $cdx = Join-Path $WorkDir "$Stem.$tgt.cdx"
    $out = Join-Path $WorkDir "$Stem.$tgt.out"
    $bin = if ($tgt -eq 'windows') { Join-Path $WorkDir "$Stem.exe" } else { Join-Path $WorkDir "$Stem.elf" }
    if (-not ($ReuseBinaries -and (Test-Path $bin))) {
        $flag = if ($tgt -eq 'windows') { 'hosted-windows' } else { 'hosted' }
        & (Join-Path $Repo 'build\compile.ps1') -Src $src -Out $cdx -Log (Join-Path $WorkDir "$Stem.$tgt.log") -Kernel $Kernel -RawFlags $flag *> $null
        if (-not (Test-Path $cdx)) { Fail "$tgt : compile produced no CDX; see $WorkDir\hosted-http.$tgt.log"; return $null }
        try {
            if ($tgt -eq 'windows') { & (Join-Path $Repo 'codex\plugs\pe\cdx-to-pe-console.ps1') -CdxInput $cdx -Out $bin *> $null }
            else { & (Join-Path $PSScriptRoot 'cdx-to-elf.ps1') -CdxInput $cdx -Out $bin *> $null }
        } catch { Fail "$tgt : wrapping the CDX failed: $_"; return $null }
    }

    if ($tgt -eq 'windows') {
        $proc = Start-Process -FilePath $bin -PassThru -NoNewWindow -RedirectStandardOutput $out -RedirectStandardError (Join-Path $WorkDir "$Stem.$tgt.err")
    } else {
        $lp = wsl -e wslpath -a $bin
        $proc = Start-Process -FilePath 'wsl' -ArgumentList '-e', $lp -PassThru -NoNewWindow -RedirectStandardOutput $out -RedirectStandardError (Join-Path $WorkDir "$Stem.$tgt.err")
    }

    $port = Get-Port $tgt
    if (-not (Wait-Listening $out)) {
        Fail "$tgt : the subject did not report 'listen 0' within 20s; it printed: $((Get-Content $out -Raw -ErrorAction SilentlyContinue) -replace '\r?\n', ' | ')"
    } else {
        # PROBES ARE GROUPED BY PATH, one request per DISTINCT path, because a
        # subject with a request budget counts connections and not assertions.
        # Measured: three probe entries over two paths made three connections
        # against a subject serving two, and the third found the server already
        # gone, which reported a working server as answering nothing.
        $byPath = [ordered]@{}
        foreach ($spec in $Probe) {
            $eq = $spec.IndexOf('=')
            $path = $spec.Substring(0, $eq)
            $want = $spec.Substring($eq + 1)
            if (-not $byPath.Contains($path)) { $byPath[$path] = @() }
            $byPath[$path] += $want
        }
        foreach ($path in $byPath.Keys) {
            $got = Ask-Http $port $path
            if ($null -eq $got) { Fail "$tgt : $path answered nothing"; continue }
            foreach ($want in $byPath[$path]) {
                if ($got -notmatch [regex]::Escape($want)) { Fail "$tgt : $path did not carry '$want'; it answered: $($got -replace '\r?\n', ' | ')" }
            }
        }
    }

    # A subject with a request BUDGET exits on its own and a hang is a failure.
    # A subject serving unbounded never exits, and killing it is the ordinary
    # end of the arm rather than a fault. -DoneLine tells the two apart: a
    # budgeted subject prints a finishing line, an unbounded one cannot.
    if (-not $proc.WaitForExit(10000)) {
        $proc.Kill()
        if ($DoneLine) { Fail "$tgt : the subject did not exit after its probes" }
    }
    $stdout = (Get-Content $out -Raw -ErrorAction SilentlyContinue)
    if ($null -eq $stdout) { $stdout = '' }
    if ($DoneLine -and $stdout -notmatch [regex]::Escape($DoneLine)) { Fail "$tgt : the subject did not print '$DoneLine'; it printed: $($stdout -replace '\r?\n', ' | ')" }
    return ($stdout -replace '\r\n', "`n")
}

$targets = if ($Target -eq 'both') { @('linux','windows') } else { @($Target) }
if ($targets -contains 'linux' -and -not (Get-Command wsl -ErrorAction SilentlyContinue)) {
    Fail 'linux arm has no bed: wsl is not on this box'
    $targets = @($targets | Where-Object { $_ -ne 'linux' })
}
$said = @{}
foreach ($t in $targets) { $said[$t] = Invoke-Target $t }

if ($targets -contains 'linux' -and $targets -contains 'windows') {
    if ($null -eq $said['linux'] -or $null -eq $said['windows']) { Fail 'PARITY: one target produced no output, so the two cannot be compared' }
    elseif ($said['linux'] -ne $said['windows']) { Fail "PARITY: the targets disagree. linux: $($said['linux'] -replace '\n', ' | ') windows: $($said['windows'] -replace '\n', ' | ')" }
}

if ($failures.Count -gt 0) {
    Write-Host "hosted-http-arm: $($failures.Count) FAILURE(S)"
    foreach ($f in $failures) { Write-Host "  $f" }
    Write-Host "artifacts: $WorkDir"
    exit 1
}
$parity = if ($targets.Count -gt 1) { ', and the two targets printed byte-identical output' } else { '' }
Write-Host "hosted-http-arm: ALL ARMS OK over $($targets -join '+'); control answered, dead port refused, the hosted Codex server answered every probed path ($($Probe.Count) assertion(s))$parity"
exit 0
