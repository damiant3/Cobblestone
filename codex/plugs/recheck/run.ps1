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
    [string]$Kernel = ''
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
& pwsh @compileArgs 2>&1 | Out-Null

# compile.ps1 reports success on a failed compile, so the log is the gate.
if (Select-String -Path $LogFile -Pattern 'error CDX' -Quiet) {
    [Console]::Error.WriteLine("FAIL: source did not compile; see $LogFile")
    Select-String -Path $LogFile -Pattern 'error CDX' | Select-Object -First 5 |
        ForEach-Object { [Console]::Error.WriteLine("  $($_.Line)") }
    exit 4
}
if (-not (Test-Path $IrFile)) {
    [Console]::Error.WriteLine("FAIL: no IR produced; see $LogFile")
    exit 4
}

# -- Phase 2: IR text -> report via plug ------------------------------
$stderrFile = [System.IO.Path]::GetTempFileName()
try {
    $proc = Start-Process -FilePath $script:CodexVmBin `
        -ArgumentList @('-kernel', $PlugCdx, '-mem', '3072', '-headless') `
        -PassThru -WindowStyle Hidden -RedirectStandardError $stderrFile
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 9100)
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
    $ns.ReadTimeout = 300000
    $resp = [System.Collections.Generic.List[byte]]::new()
    $buf = New-Object byte[] 65536
    while ($true) {
        try { $n = $ns.Read($buf, 0, $buf.Length) } catch { break }
        if ($n -le 0) { break }
        for ($i = 0; $i -lt $n; $i++) { $resp.Add($buf[$i]) }
    }
    $client.Close()
    if ($resp.Count -eq 0) { [Console]::Error.WriteLine("FAIL: empty response from plug"); exit 8 }
    $text = [System.Text.Encoding]::ASCII.GetString($resp.ToArray())
    Write-Host "[recheck] $Src  (passes=$(if ($Passes) { $Passes } else { 'default' }))"
    Write-Host $text
    if ($Out) { [System.IO.File]::WriteAllText($Out, $text) }
} finally {
    if ($proc -and -not $proc.HasExited) {
        try { Stop-Process -Id $proc.Id -Force -ErrorAction Stop } catch {}
    }
    Remove-Item -Force $stderrFile -ErrorAction SilentlyContinue
}
