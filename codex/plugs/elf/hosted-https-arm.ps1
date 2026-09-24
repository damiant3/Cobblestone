# hosted-https-arm.ps1 -- a hosted target serves HTTPS, TLS 1.3 over
# the host's TCP, on both hosted targets, to a client that is not ours.
#
# WSL is the verification bed for the linux binary by Damian's ruling of
# 2026-08-28. Verification only: nothing here is on the build path.
#
# THE CLIENT IS OPENSSL (Git for Windows), not our own TLS client. Our client and
# our server agree with each other by construction; the claim under test is that
# the server speaks TLS 1.3 to somebody else. Windows' own stack (Schannel) is
# not used because it does not do Ed25519.
#
# The subject prints its certificate, minted at start from a key drawn from
# RDRAND, and the arm pins exactly that certificate. Three connections, in order:
#   wrong pin    the client trusts a DIFFERENT self-signed certificate for the
#                same names; verification must fail and no HTTP may come back.
#   /hello       pinned, hostname localhost: the subject's own route.
#   /api/health  pinned, address 127.0.0.1: the WebRoute standard endpoint.
#
# THE CONTROLS. A client that cannot connect looks exactly like a server that
# never listened (L-VACUOUS), so:
#   positive: the same client function against `openssl s_server` holding the
#             control certificate must answer.
#   negative: the same client against a dead port must not.
[CmdletBinding()]
param(
    [string]$Kernel = '',
    [switch]$ControlsOnly,
    [ValidateSet('linux','windows','both')][string]$Target = 'both',
    [string]$WorkDir = '',
    # The subject binds $PortBase + hosted-kind: linux base+1, windows base+2.
    # The same base is in codex/test/hosted-https.codex.
    [int]$PortBase = 34580,
    [int]$ControlPort = 34620,
    [int]$DeadPort = 34621
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$Ssl = 'C:\Program Files\Git\usr\bin\openssl.exe'
if (-not (Test-Path $Ssl)) { throw "openssl not found at $Ssl" }
# ONE FIXED PATH for the binaries, in this workspace. Windows Firewall asks
# about every NEW executable that listens on more than loopback, and the subject
# listens through hosted-listen, which binds INADDR_ANY (host-socket's bind
# takes the address as its third argument; hosted-listen passes 0), so a fresh
# directory per run was a fresh prompt per run. Cancelling the prompt still leaves loopback connecting.
if (-not $WorkDir) { $WorkDir = Join-Path $Repo 'build-output\hosted-https-arm' }
New-Item -ItemType Directory -Force $WorkDir | Out-Null
if (-not $ControlsOnly -and -not $Kernel) { throw 'pass -Kernel, or -ControlsOnly to prove the instrument alone' }
if ($Kernel -and -not (Test-Path $Kernel)) { throw "kernel not found: $Kernel" }

$failures = @()
function Fail($m) { $script:failures += $m }
function Get-Port([string]$t) { if ($t -eq 'windows') { $PortBase + 2 } else { $PortBase + 1 } }

# openssl with a hard timeout. Returns @{ Exit; Out; Err }, Exit -1 on timeout.
function Invoke-Ssl([string[]]$argv, [string]$stdin = '', [int]$ms = 15000) {
    $psi = [Diagnostics.ProcessStartInfo]::new($Ssl)
    foreach ($a in $argv) { $psi.ArgumentList.Add($a) }
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $pr = [Diagnostics.Process]::Start($psi)
    if ($stdin) { $pr.StandardInput.Write($stdin) }
    $pr.StandardInput.Flush()
    $o = $pr.StandardOutput.ReadToEndAsync()
    $e = $pr.StandardError.ReadToEndAsync()
    if (-not $pr.WaitForExit($ms)) { $pr.Kill(); $null = $pr.WaitForExit(2000); return @{ Exit = -1; Out = $o.Result; Err = $e.Result } }
    return @{ Exit = $pr.ExitCode; Out = $o.Result; Err = $e.Result }
}

# One client, used by the arm and by both controls: TLS 1.3 only, the pin as
# the only trust anchor, a verification failure is fatal, and the peer is
# checked by name or by address.
function Ask-Https([int]$p, [string]$path, [string]$pinPem, [string]$name = 'localhost', [string]$ip = '') {
    $req = "GET $path HTTP/1.0`r`nHost: localhost`r`n`r`n"
    $idArgs = if ($ip) { @('-verify_ip', $ip) } else { @('-verify_hostname', $name) }
    $argv = @('s_client', '-connect', "127.0.0.1:$p", '-tls1_3', '-CAfile', $pinPem, '-partial_chain', '-verify_return_error') + $idArgs + @('-quiet')
    return Invoke-Ssl $argv $req
}

# The control certificate: a second self-signed Ed25519 leaf for the same names,
# minted by openssl. It is the wrong pin in the arm and the server's own
# certificate in the positive control.
$ctlKey = Join-Path $WorkDir 'control.key'
$ctlPem = Join-Path $WorkDir 'control.pem'
$mk = Invoke-Ssl @('req', '-x509', '-newkey', 'ed25519', '-nodes', '-keyout', $ctlKey, '-out', $ctlPem, '-days', '30',
    '-subj', '/O=Codex DEV-ONLY self-signed/CN=codex-dev', '-addext', 'subjectAltName=DNS:localhost,IP:127.0.0.1', '-addext', 'basicConstraints=critical,CA:FALSE')
if ($mk.Exit -ne 0 -or -not (Test-Path $ctlPem)) { throw "could not mint the control certificate: $($mk.Err)" }

# ---- positive control -------------------------------------------------------
$srvOut = Join-Path $WorkDir 'control-server.out'
$srv = Start-Process -FilePath $Ssl -ArgumentList 's_server', '-accept', "127.0.0.1:$ControlPort", '-key', $ctlKey, '-cert', $ctlPem, '-tls1_3', '-www', '-naccept', '1' -PassThru -NoNewWindow -RedirectStandardOutput $srvOut -RedirectStandardError (Join-Path $WorkDir 'control-server.err')
try {
    [Threading.Thread]::Sleep(1500)
    $ctl = Ask-Https $ControlPort '/' $ctlPem
    if ($ctl.Exit -ne 0 -or $ctl.Out -notmatch 'HTTP/1\.0 200') {
        Fail "POSITIVE CONTROL: openssl s_server on $ControlPort answered exit $($ctl.Exit), so the client function is wrong and every result below means nothing: $($ctl.Err -replace '\r?\n', ' | ')"
    }
} finally {
    if (-not $srv.HasExited) { $srv.Kill() }
}

# ---- negative control -------------------------------------------------------
$dead = Ask-Https $DeadPort '/hello' $ctlPem
if ($dead.Exit -eq 0 -or $dead.Out -match 'HTTP/') { Fail "NEGATIVE CONTROL: port $DeadPort answered with nothing listening, so a pass is not evidence" }

if ($ControlsOnly) {
    if ($failures.Count -gt 0) {
        Write-Host "hosted-https-arm [controls only]: $($failures.Count) FAILURE(S)"
        foreach ($f in $failures) { Write-Host "  $f" }
        exit 1
    }
    Write-Host 'hosted-https-arm [controls only]: the client reads a real TLS 1.3 server and does not read a dead port, so the instrument reads'
    exit 0
}

# ---- the arm ----------------------------------------------------------------
$src = Join-Path $Repo 'codex\test\hosted-https.codex'

function Wait-Listening([string]$outPath, [int]$seconds = 30) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $t = Get-Content $outPath -Raw -ErrorAction SilentlyContinue
        if ($t -and $t -match 'listen 0') { return $true }
        Start-Sleep -Milliseconds 200
    }
    return $false
}

function Invoke-Target([string]$tgt) {
    $cdx = Join-Path $WorkDir "hosted-https.$tgt.cdx"
    $out = Join-Path $WorkDir "hosted-https.$tgt.out"
    $bin = if ($tgt -eq 'windows') { Join-Path $WorkDir 'hosted-https.exe' } else { Join-Path $WorkDir 'hosted-https.elf' }
    $flag = if ($tgt -eq 'windows') { 'hosted-windows' } else { 'hosted' }
    & (Join-Path $Repo 'build\compile.ps1') -Src $src -Out $cdx -Log (Join-Path $WorkDir "hosted-https.$tgt.log") -Kernel $Kernel -RawFlags $flag *> $null
    if (-not (Test-Path $cdx)) { Fail "$tgt : compile produced no CDX; see $WorkDir\hosted-https.$tgt.log"; return $null }
    try {
        if ($tgt -eq 'windows') { & (Join-Path $Repo 'codex\plugs\pe\cdx-to-pe-console.ps1') -CdxInput $cdx -Out $bin *> $null }
        else { & (Join-Path $PSScriptRoot 'cdx-to-elf.ps1') -CdxInput $cdx -Out $bin *> $null }
    } catch { Fail "$tgt : wrapping the CDX failed: $_"; return $null }

    if ($tgt -eq 'windows') {
        $proc = Start-Process -FilePath $bin -PassThru -NoNewWindow -RedirectStandardOutput $out -RedirectStandardError (Join-Path $WorkDir "hosted-https.$tgt.err")
    } else {
        $lp = wsl -e wslpath -a $bin
        $proc = Start-Process -FilePath 'wsl' -ArgumentList '-e', $lp -PassThru -NoNewWindow -RedirectStandardOutput $out -RedirectStandardError (Join-Path $WorkDir "hosted-https.$tgt.err")
    }
    try {
        $port = Get-Port $tgt
        if (-not (Wait-Listening $out)) {
            Fail "$tgt : the subject did not report 'listen 0' within 30s; it printed: $((Get-Content $out -Raw -ErrorAction SilentlyContinue) -replace '\r?\n', ' | ')"
            return $null
        }
        $certLine = @((Get-Content $out) | Where-Object { $_ -like 'cert *' })[0]
        $der = [byte[]](($certLine.Substring(5).Trim() -split ' +') | ForEach-Object { [int]$_ })
        $derPath = Join-Path $WorkDir "subject.$tgt.der"
        $pin = Join-Path $WorkDir "subject.$tgt.pem"
        [IO.File]::WriteAllBytes($derPath, $der)
        $cv = Invoke-Ssl @('x509', '-inform', 'der', '-in', $derPath, '-out', $pin)
        if ($cv.Exit -ne 0) { Fail "$tgt : openssl cannot read the certificate the subject printed: $($cv.Err)"; return $null }

        $wrong = Ask-Https $port '/hello' $ctlPem
        if ($wrong.Exit -eq 0 -or $wrong.Out -match 'HTTP/') { Fail "$tgt : a client pinned to a different certificate was served: exit $($wrong.Exit)" }
        elseif ($wrong.Err -notmatch 'verify') { Fail "$tgt : the wrong-pin client failed, but not on verification: $($wrong.Err -replace '\r?\n', ' | ')" }

        $hello = Ask-Https $port '/hello' $pin
        if ($hello.Exit -ne 0 -or $hello.Out -notmatch 'HTTP/1\.0 200' -or $hello.Out -notmatch [regex]::Escape('"hello":"hosted-tls"')) {
            Fail "$tgt : /hello pinned by name: exit $($hello.Exit), answered: $($hello.Out -replace '\r?\n', ' | ') $($hello.Err -replace '\r?\n', ' | ')"
        }
        $health = Ask-Https $port '/api/health' $pin -ip '127.0.0.1'
        if ($health.Exit -ne 0 -or $health.Out -notmatch [regex]::Escape('"status":"ok"')) {
            Fail "$tgt : /api/health pinned by address: exit $($health.Exit), answered: $($health.Out -replace '\r?\n', ' | ') $($health.Err -replace '\r?\n', ' | ')"
        }

        if (-not $proc.WaitForExit(15000)) { Fail "$tgt : the subject did not exit after its three connections" }
        $stdout = (Get-Content $out -Raw -ErrorAction SilentlyContinue)
        if ($null -eq $stdout) { $stdout = '' }
        if ($stdout -notmatch 'served 2') { Fail "$tgt : the subject did not print 'served 2'; it printed: $(($stdout -replace '(?m)^cert .*$', 'cert ...') -replace '\r?\n', ' | ')" }
        if ($stdout -notmatch 'retained under 1 MiB: True') { Fail "$tgt : the server kept its connections' heap; it printed: $(($stdout -replace '(?m)^cert .*$', 'cert ...') -replace '\r?\n', ' | ')" }
        # The certificate line differs by construction (a fresh key per run).
        return (($stdout -replace '\r\n', "`n") -replace '(?m)^cert .*\n', '')
    } finally {
        if (-not $proc.HasExited) { $proc.Kill() }
    }
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
    Write-Host "hosted-https-arm: $($failures.Count) FAILURE(S)"
    foreach ($f in $failures) { Write-Host "  $f" }
    Write-Host "artifacts: $WorkDir"
    exit 1
}
$parity = if ($targets.Count -gt 1) { ', and the two targets printed identical output apart from their certificates' } else { '' }
Write-Host "hosted-https-arm: ALL ARMS OK over $($targets -join '+'); openssl s_server control answered, dead port refused, a wrong pin was refused on verification, /hello (by name) and /api/health (by address) answered over TLS 1.3$parity"
exit 0
