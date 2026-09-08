# hosted-socket-arm.ps1 -- Prism stage 5c, acceptance arm 1: a Codex program
# that listens, accepts one connection, echoes a line and closes, compiled for
# the HOSTED Linux target and asked by a host client.
#
# WSL is the verification bed by Damian's ruling of 2026-08-28. Verification
# only: nothing here is on the build path.
#
# THE TWO CONTROLS ARE THE POINT. A client that cannot connect looks exactly
# like a server that never listened, so this probe's silence would otherwise
# agree with "the sockets work" and "the probe is wrong" equally (L-VACUOUS):
#
#   positive: the SAME client function against a .NET TcpListener echo must
#             pass, or the probe is broken and the arm's result means nothing.
#   negative: the same client against a port nobody listens on must FAIL, or a
#             pass proves nothing about anybody listening.
#
#   pwsh codex/plugs/elf/hosted-socket-arm.ps1 -Kernel <candidate .cdx>
[CmdletBinding()]
param(
    [string]$Kernel = '',
    # Run the two controls and stop. The controls need no guest and no kernel,
    # so the instrument can be proved before there is a subject to point it at.
    [switch]$ControlsOnly,
    # An ELF this script already built, to re-run the bed without paying for a
    # compile guest. A harness defect is fixed against the same subject bytes.
    [string]$Elf = '',
    # Both targets by default: they are each other's control, and one passing
    # alone has historically meant a path that lowered somewhere else entirely.
    [ValidateSet('linux','windows','both')][string]$Target = 'both',
    # Re-run binaries already in -WorkDir instead of compiling. No guest.
    [switch]$ReuseBinaries,
    [string]$WorkDir = '',
    # The subject binds $PortBase + hosted-kind, so linux takes base+1 and
    # windows base+2. The same base is in codex/test/hosted-echo.codex and this
    # script does not read it out of there, because a regex over a source file
    # is a second thing to get wrong. The control ports sit clear of both.
    [int]$PortBase = 34567,
    [int]$ControlPort = 34600,
    [int]$DeadPort = 34601
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
if (-not $WorkDir) { $WorkDir = Join-Path $env:TEMP ('hosted-sock-' + [Guid]::NewGuid().ToString('n').Substring(0,8)) }
New-Item -ItemType Directory -Force $WorkDir | Out-Null
if (-not $ControlsOnly) {
    if (-not $Kernel -and -not $Elf) { throw 'pass -Kernel, or -Elf to re-run a built subject, or -ControlsOnly to prove the instrument alone' }
    if ($Kernel -and -not (Test-Path $Kernel)) { throw "kernel not found: $Kernel" }
    if ($Elf -and -not (Test-Path $Elf)) { throw "elf not found: $Elf" }
    # Re-running a built ELF cannot also produce a Windows binary, so that
    # combination narrows to the target it can actually run rather than
    # reporting a compile failure the caller did not ask for.
    if ($Elf -and -not $Kernel -and $Target -eq 'both') { $Target = 'linux' }
}

$failures = @()
function Fail($m) { $script:failures += $m }

# One client, used by the arm and by both controls. Returns the echoed text, or
# $null when it could not connect or the read timed out.
function Ask-Echo([int]$p, [string]$line, [int]$timeoutMs = 3000) {
    $c = [System.Net.Sockets.TcpClient]::new()
    try {
        $iar = $c.BeginConnect('127.0.0.1', $p, $null, $null)
        if (-not $iar.AsyncWaitHandle.WaitOne($timeoutMs)) { return $null }
        $c.EndConnect($iar)
        $c.SendTimeout = $timeoutMs
        $c.ReceiveTimeout = $timeoutMs
        $s = $c.GetStream()
        $b = [Text.Encoding]::ASCII.GetBytes($line)
        $s.Write($b, 0, $b.Length); $s.Flush()
        $buf = [byte[]]::new(4096)
        $n = $s.Read($buf, 0, $buf.Length)
        if ($n -le 0) { return $null }
        return [Text.Encoding]::ASCII.GetString($buf, 0, $n)
    } catch { return $null } finally { $c.Close() }
}

# Wait until the subject says it is listening, so the arm is not a race against
# process start-up. WAITING MUST NOT COST A CONNECTION: the subject accepts
# exactly ONE, and a readiness probe that dials and hangs up is the connection
# it accepts. It then reads EOF, echoes nothing and exits before the real client
# arrives, which reads as a broken recv (measured 2026-09-08: "echoed 0" with
# socket, reuseaddr, bind and listen all correct). So readiness is taken off the
# subject's own stdout instead, and a timeout here is its own named failure
# rather than a wrong answer downstream.
function Wait-Listening([string]$outPath, [int]$seconds = 20) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $t = Get-Content $outPath -Raw -ErrorAction SilentlyContinue
        if ($t -and $t -match 'listen 0') { return $true }
        Start-Sleep -Milliseconds 200
    }
    return $false
}

# THE TWO TARGETS MUST NOT SHARE A PORT, AND WAITING IS NOT ENOUGH. WSL2
# forwards a listener inside WSL onto the Windows host and does not release it
# when the subject exits: measured 2026-09-08, the port was still unbindable
# 30 seconds later with NOTHING owning it in either namespace, invisible to
# Get-NetTCPConnection, below the ephemeral range and unexcluded. Run back to
# back on one port the Windows subject answered "bind -1" while the same binary
# standalone answered "bind 0", so the harness was calling a working target
# broken. The subject now derives its port from `hosted-kind`, so each target
# has its own by construction; this remains only to catch a leftover listener
# of ours, and says so distinctly rather than as a bind failure.
function Get-Port([string]$t) { if ($t -eq 'windows') { $PortBase + 2 } else { $PortBase + 1 } }
function Wait-PortFree([int]$p, [int]$seconds = 30) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $l = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Any, $p)
            $l.Start(); $l.Stop()
            return $true
        } catch { Start-Sleep -Milliseconds 250 }
    }
    return $false
}

# ---- positive control -------------------------------------------------------
# The echo side runs in its own runspace so the client below is the SAME
# blocking function the arm uses. Driving it from this thread would need a
# different client and the control would then prove a different instrument.
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $ControlPort)
$listener.Start()
$ps = [PowerShell]::Create()
$null = $ps.AddScript({
    param($l)
    $cl = $l.AcceptTcpClient()
    $st = $cl.GetStream()
    $bb = [byte[]]::new(4096)
    $nn = $st.Read($bb, 0, $bb.Length)
    if ($nn -gt 0) { $st.Write($bb, 0, $nn); $st.Flush() }
    $cl.Close()
}).AddArgument($listener)
$handle = $ps.BeginInvoke()
$ctlText = Ask-Echo $ControlPort "control line`n"
$null = $handle.AsyncWaitHandle.WaitOne(5000)
try { $ps.EndInvoke($handle) } catch { }
$ps.Dispose()
$listener.Stop()
if ($ctlText -ne "control line`n") { Fail "POSITIVE CONTROL: a .NET echo on $ControlPort answered '$ctlText', so the client function is wrong and the arm below proves nothing" }

# ---- negative control -------------------------------------------------------
$deadText = Ask-Echo $DeadPort "nobody home`n" 1500
if ($null -ne $deadText) { Fail "NEGATIVE CONTROL: port $DeadPort answered '$deadText' with nothing listening, so a pass on the arm is not evidence" }

# ---- the arm ----------------------------------------------------------------
if ($ControlsOnly) {
    if ($failures.Count -gt 0) {
        Write-Host "hosted-socket-arm [controls only]: $($failures.Count) FAILURE(S)"
        foreach ($f in $failures) { Write-Host "  $f" }
        exit 1
    }
    Write-Host 'hosted-socket-arm [controls only]: the client answers a real listener and does not answer a dead port, so the instrument reads'
    exit 0
}

$src = Join-Path $Repo 'codex\test\hosted-echo.codex'

# One target's run, so the two targets are the SAME procedure asked twice. That
# is what makes them each other's control: a hosted path that quietly lowered to
# the bare-metal serial road has passed on one target alone before, which is the
# reason ReadFile exists.
function Invoke-Target([string]$tgt) {
    $cdx = Join-Path $WorkDir "hosted-echo.$tgt.cdx"
    $out = Join-Path $WorkDir "hosted-echo.$tgt.out"
    $bin = if ($tgt -eq 'windows') { Join-Path $WorkDir 'hosted-echo.exe' }
           elseif ($Elf) { $Elf } else { Join-Path $WorkDir 'hosted-echo.elf' }
    # A harness fix is proved against the SAME subject bytes, so a built binary
    # is re-run rather than recompiled: a rebuild would change two things at once.
    $reuse = ($tgt -eq 'linux' -and $Elf) -or ($ReuseBinaries -and (Test-Path $bin))
    if (-not $reuse) {
        $flag = if ($tgt -eq 'windows') { 'hosted-windows' } else { 'hosted' }
        & (Join-Path $Repo 'build\compile.ps1') -Src $src -Out $cdx -Log (Join-Path $WorkDir "hosted-echo.$tgt.log") -Kernel $Kernel -RawFlags $flag *> $null
        if (-not (Test-Path $cdx)) { Fail "$tgt : compile produced no CDX; see $WorkDir\hosted-echo.$tgt.log"; return $null }
        try {
            if ($tgt -eq 'windows') { & (Join-Path $Repo 'codex\plugs\pe\cdx-to-pe-console.ps1') -CdxInput $cdx -Out $bin *> $null }
            else { & (Join-Path $PSScriptRoot 'cdx-to-elf.ps1') -CdxInput $cdx -Out $bin *> $null }
        } catch { Fail "$tgt : wrapping the CDX failed: $_"; return $null }
    }

    if (-not (Wait-PortFree (Get-Port $tgt))) {
        Fail "$tgt : port $(Get-Port $tgt) was still held 30s before this target started"
        return $null
    }

    if ($tgt -eq 'windows') {
        $proc = Start-Process -FilePath $bin -PassThru -NoNewWindow `
                              -RedirectStandardOutput $out -RedirectStandardError (Join-Path $WorkDir "hosted-echo.$tgt.err")
    } else {
        $lp = wsl -e wslpath -a $bin
        $proc = Start-Process -FilePath 'wsl' -ArgumentList '-e', $lp -PassThru -NoNewWindow `
                              -RedirectStandardOutput $out -RedirectStandardError (Join-Path $WorkDir "hosted-echo.$tgt.err")
    }

    $answer = $null
    if (-not (Wait-Listening $out)) {
        Fail "$tgt : the subject did not report 'listen 0' within 20s; it printed: $((Get-Content $out -Raw -ErrorAction SilentlyContinue) -replace '\r?\n', ' | ')"
    } else {
        $want = "hello from the arm`n"
        $answer = Ask-Echo (Get-Port $tgt) $want
        if ($answer -ne $want) { Fail "$tgt : sent '$want', got '$answer'" }
    }
    if (-not $proc.WaitForExit(10000)) { $proc.Kill(); Fail "$tgt : the subject did not exit after the exchange" }

    $stdout = (Get-Content $out -Raw -ErrorAction SilentlyContinue)
    if ($null -eq $stdout) { $stdout = '' }
    # The subject prints each call's answer, so a -1 anywhere names WHICH call
    # refused rather than leaving one silent failure to be guessed at.
    foreach ($line in @('reuseaddr 0', 'bind 0', 'listen 0')) {
        if ($stdout -notmatch [regex]::Escape($line)) { Fail "$tgt : the subject did not print '$line'; it printed: $($stdout -replace '\r?\n', ' | ')" }
    }
    if ($stdout -notmatch 'socket ok') { Fail "$tgt : socket() did not answer a usable descriptor; the subject printed: $($stdout -replace '\r?\n', ' | ')" }
    if ($stdout -notmatch 'echoed 19') { Fail "$tgt : the subject did not report sending the 19 bytes it was asked; it printed: $($stdout -replace '\r?\n', ' | ')" }
    return ($stdout -replace '\r\n', "`n")
}

$targets = if ($Target -eq 'both') { @('linux','windows') } else { @($Target) }
if ($targets -contains 'linux' -and -not (Get-Command wsl -ErrorAction SilentlyContinue)) {
    Fail 'linux arm has no bed: wsl is not on this box'
    $targets = @($targets | Where-Object { $_ -ne 'linux' })
}
$said = @{}
foreach ($t in $targets) { $said[$t] = Invoke-Target $t }

# ARM 2: the two targets must say the SAME thing. Compared only when both ran,
# because "one target was skipped" and "the two agree" must not read alike.
if ($targets -contains 'linux' -and $targets -contains 'windows') {
    if ($null -eq $said['linux'] -or $null -eq $said['windows']) {
        Fail 'PARITY: one target produced no output, so the two cannot be compared'
    } elseif ($said['linux'] -ne $said['windows']) {
        Fail "PARITY: the targets disagree. linux: $($said['linux'] -replace '\n', ' | ') windows: $($said['windows'] -replace '\n', ' | ')"
    }
}

if ($failures.Count -gt 0) {
    Write-Host "hosted-socket-arm: $($failures.Count) FAILURE(S)"
    foreach ($f in $failures) { Write-Host "  $f" }
    Write-Host "artifacts: $WorkDir"
    exit 1
}
$parity = if ($targets.Count -gt 1) { ', and the two targets printed byte-identical output' } else { '' }
Write-Host "hosted-socket-arm: ALL ARMS OK over $($targets -join '+'); positive control answered, dead port refused, the hosted Codex server bound its port, accepted, echoed 19 bytes and exited$parity"
exit 0
