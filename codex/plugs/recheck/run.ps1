# Re-check one Codex source file: compile it to IR text, hand the IR to the
# recheck plug, print the plug's report.
#
# -Passes decides WHICH PROGRAM is re-checked and the answer is not the same
# either way. The IR text is emitted after the optimizer, so the default
# re-checks the program that will actually run, inlining and all; `-Passes
# none` re-checks the definitions as written. A run that does not say which
# it did is not interpretable, so the report carries it.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string]$Src,
    [string]$Out = '',
    [string]$Passes = 'none',
    [string]$Kernel = '',
    [int]$Mem = 3072
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..' '..' '..' 'build' 'vm-config.ps1')

$Repo    = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
$PlugDir = (Resolve-Path $PSScriptRoot).Path
$PlugCdx = Join-Path $PlugDir 'build-output\recheck-plug.cdx'
$IrDir   = Join-Path $PlugDir 'build-output'
$IrFile  = Join-Path $IrDir 'last-run.ir'
$LogFile = Join-Path $IrDir 'run.log'

if (-not (Test-Path -PathType Leaf $PlugCdx)) {
    [Console]::Error.WriteLine("MISSING: $PlugCdx -- run codex/plugs/recheck/build.ps1 first")
    exit 2
}

# -- Phase 1: Codex source -> IR text --------------------------------
$compileScript = Join-Path $Repo 'build' 'compile.ps1'
$compileArgs = @('-NoProfile', '-File', $compileScript, '-Src', $Src, '-Out', $IrFile,
                 '-Log', $LogFile, '-IrCce', '-Kernel', $(if ($Kernel) { $Kernel } else { Join-Path $Repo 'seed\Codex.cdx' }))
if ($Passes) { $compileArgs += @('-Passes', $Passes) }
# A failed compile must not be able to serve the PREVIOUS run's IR. Twice on
# 2026-08-14 it did: the compile died, both checks below passed anyway, and the
# plug rechecked a four-day-old artifact while reporting a plausible verdict.
# Deleting the file first means a silent failure is an absent IR, not a stale
# one, which is the only failure mode that cannot be misread as a measurement.
Remove-Item -Force $IrFile -ErrorAction SilentlyContinue

& pwsh @compileArgs 2>&1 | Out-Null

# compile.ps1 reports success on a failed compile, so the log is the gate.
# NOT every compiler error is spelled `error CDX`: an unresolvable cite is
# `error 3010:` with no prefix, and matching only the CDX form let that class
# through. Match the number form too.
$errPat = 'error (CDX|\d{4}:)'
if (Select-String -Path $LogFile -Pattern $errPat -Quiet) {
    [Console]::Error.WriteLine("FAIL: source did not compile; see $LogFile")
    Select-String -Path $LogFile -Pattern $errPat | Select-Object -First 5 |
        ForEach-Object { [Console]::Error.WriteLine("  $($_.Line)") }
    exit 4
}
if (-not (Test-Path $IrFile)) {
    [Console]::Error.WriteLine("FAIL: no IR produced; see $LogFile")
    exit 4
}

# -- Phase 2: IR text -> report via plug ------------------------------
$stderrFile = [System.IO.Path]::GetTempFileName()
$consoleFile = [System.IO.Path]::GetTempFileName()
try {
    $proc = Start-PlugVm -Kernel $PlugCdx -ConsoleFile $consoleFile -StderrFile $stderrFile -MemMB "$Mem"
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 9134)
    $listener.Start()
    $deadline = (Get-Date).AddSeconds(30)
    while (-not $listener.Pending() -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 100 }
    if (-not $listener.Pending()) { [Console]::Error.WriteLine("FAIL: plug did not connect"); exit 7 }
    $client = $listener.AcceptTcpClient()
    $listener.Stop()
    $ns = $client.GetStream()
    $irData = [System.IO.File]::ReadAllBytes($IrFile)
    $msgLen = $irData.Length + 1
    $ns.Write([BitConverter]::GetBytes([int]$msgLen), 0, 4)
    $ns.WriteByte(1)
    $off = 0
    while ($off -lt $irData.Length) {
        $n = [Math]::Min(4096, $irData.Length - $off)
        $ns.Write($irData, $off, $n)
        $ns.Flush()
        $off += $n
        if ($off -lt $irData.Length) { Start-Sleep -Milliseconds 20 }
    }
    # A read that FAILS is not a response that ENDED, and conflating the two
    # cost three wrong diagnoses on 2026-08-09: the reader caught the timeout,
    # broke, and handed back the first 11 KB of a 16 MB report as though the
    # plug had finished. A partial report reads as a small clean one -- the
    # summary line is simply absent -- so it must be a loud failure here.
    #
    # The accumulator is a MemoryStream because the old one added ONE BYTE AT
    # A TIME to a List; at 16 MB that is slow enough for the socket to fall
    # behind and produce the very timeout being mistaken for the end.
    $ns.ReadTimeout = 300000
    $resp = [System.IO.MemoryStream]::new()
    $buf = New-Object byte[] 262144
    $truncated = $false
    while ($true) {
        try { $n = $ns.Read($buf, 0, $buf.Length) } catch { $truncated = $true; break }
        if ($n -le 0) { break }
        $resp.Write($buf, 0, $n)
    }
    $client.Close()
    if ($resp.Length -eq 0) { [Console]::Error.WriteLine("FAIL: empty response from plug"); exit 8 }
    if ($truncated) {
        [Console]::Error.WriteLine("FAIL: response truncated after $($resp.Length) bytes -- the plug was still sending")
        exit 9
    }
    # The check above sees an ABRUPT disconnect only. A send the plug refuses
    # cleanly closes the socket normally, so the guest says TRUNCATED sent= on
    # its console and nothing here would hear it without -output. codex-vm
    # dumps that ring ON EXIT, so the wait is load-bearing, not politeness:
    # measured 2026-08-17 the file holds 1 byte before it and the line after.
    if ($proc -and -not $proc.HasExited) { $proc.WaitForExit(20000) }
    $truncHit = @()
    if (Test-Path $consoleFile) { $truncHit = @(Select-String -Path $consoleFile -Pattern 'TRUNCATED sent=') }
    if ($truncHit.Count -gt 0) {
        [Console]::Error.WriteLine("FAIL: the plug could not send its whole report -- $($truncHit[0].Line.Trim())")
        exit 9
    }
    $text = [System.Text.Encoding]::ASCII.GetString($resp.ToArray())
    Write-Host "[recheck] $Src  (passes=$(if ($Passes) { $Passes } else { 'default' }))"
    Write-Host $text
    if ($Out) { [System.IO.File]::WriteAllText($Out, $text) }
} finally {
    if ($proc -and -not $proc.HasExited) {
        try { Stop-Process -Id $proc.Id -Force -ErrorAction Stop } catch {}
    }
    Remove-Item -Force $stderrFile -ErrorAction SilentlyContinue
    Remove-Item -Force $consoleFile -ErrorAction SilentlyContinue
}
