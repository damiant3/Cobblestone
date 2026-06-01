# Run the C# plug over a Codex source file via TCP.
#
#   <Codex source.codex>
#     |
#     v  build/compile.ps1 -IrCce
#   <IR text (CCE)>
#     |
#     v  TCP to csharp-plug.cdx (booted in codex-vm)
#   <C# source via TCP>
#
# The host listens on a TCP port. The plug CDX connects to it
# (port passed via -args). IR is sent as a length-prefixed message
# (tag=1), C# source comes back as tag=2.
#
# Usage:
#   plugs/csharp/run.ps1 -Src <source.codex> -Out <out.cs>
[CmdletBinding()]
param(
    [string]$Src,
    [Parameter(Mandatory=$true)] [string]$Out,
    [string]$Ir,
    [int]$MemMB = 4096
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..' '..' '..' 'build' 'vm-config.ps1')

$Repo     = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
$PlugDir  = (Resolve-Path $PSScriptRoot).Path
$PlugCdx  = Join-Path $PlugDir 'build-output\csharp-plug.cdx'
$IrDir    = Join-Path $PlugDir 'build-output'
$IrFile   = Join-Path $IrDir 'last-run.ir'
$LogFile  = Join-Path $IrDir 'run.log'

if (-not (Test-Path -PathType Leaf $PlugCdx)) {
    [Console]::Error.WriteLine("MISSING: $PlugCdx -- run plugs/csharp/build.ps1 first")
    exit 2
}

# -- Phase 1: obtain IR text -----------------------------------------
# Either consume a pre-built IR file (-Ir) or compile -Src to IR here.
if ($Ir) {
    if (-not (Test-Path -PathType Leaf $Ir)) {
        [Console]::Error.WriteLine("MISSING: -Ir $Ir")
        exit 3
    }
    $IrFile = (Resolve-Path $Ir).Path
} elseif ($Src) {
    $compileScript = Join-Path $Repo 'build\compile.ps1'
    & pwsh -File $compileScript -Src $Src -Out $IrFile -Log $LogFile -IrCce -MemMB $MemMB
    if ($LASTEXITCODE -ne 0) {
        [Console]::Error.WriteLine("FAIL: IR emit step exited $LASTEXITCODE; see $LogFile")
        exit 3
    }
} else {
    [Console]::Error.WriteLine("FAIL: provide -Src <source.codex> or -Ir <prebuilt.ir>")
    exit 1
}
$irBytes = [System.IO.File]::ReadAllBytes($IrFile)
Write-Host "[csharp-run] IR: $($irBytes.Length) bytes"

# -- Phase 2: Start TCP listener -------------------------------------
$plugPort = 9100
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $plugPort)
$listener.Start()
Write-Host "[csharp-run] Listening on port $plugPort"

# -- Phase 3: Boot plug CDX ------------------------------------------
$stderrFile = [System.IO.Path]::GetTempFileName()
$csOutFile = Join-Path $IrDir 'plug-cs.out'
Remove-Item -Force $csOutFile -ErrorAction SilentlyContinue
$proc = $null
try {
    # The plug streams C# to its output ring (not TCP); codex-vm dumps the
    # ring to -output on exit. TCP is used only to deliver the IR.
    $proc = Start-Process -FilePath $script:CodexVmBin -ArgumentList @('-kernel', $PlugCdx, '-mem', "$MemMB", '-headless', '-output', $csOutFile) `
        -PassThru -WindowStyle Hidden -RedirectStandardError $stderrFile
# Accept TCP connection from plug
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while (-not $listener.Pending()) {
        if ([DateTime]::UtcNow -gt $deadline) {
            [Console]::Error.WriteLine("FAIL: plug did not connect within 30s")
            exit 5
        }
        Start-Sleep -Milliseconds 50
    }
    $tcpClient = $listener.AcceptTcpClient()
    $tcpStream = $tcpClient.GetStream()
    $listener.Stop()
    Write-Host "[csharp-run] Plug connected"

    # -- Phase 4: Send IR as framed message (tag=1) ------------------
    # Frame format: [4-byte LE length][1-byte tag][payload]
    $msgLen = $irBytes.Length + 1  # payload = tag + ir bytes
    $header = [BitConverter]::GetBytes([int]$msgLen)
    $tcpClient.NoDelay = $true
    $tcpStream.Write($header, 0, 4)
    $tcpStream.WriteByte(1)  # tag = 1 (IR)
    # Throttle: blasting all 9.7MB at once overruns the emulated NE2000 RX
    # ring (256 frames); the guest falls behind, drops the gateway ARP, and
    # its ACKs go to broadcast — TCP wedges partway through. Feed it in small
    # chunks so the guest's recv loop always keeps pace with the ring.
    $chunk = 16384
    $off = 0
    while ($off -lt $irBytes.Length) {
        $len = [Math]::Min($chunk, $irBytes.Length - $off)
        $tcpStream.Write($irBytes, $off, $len)
        $tcpStream.Flush()
        $off += $len
        Start-Sleep -Milliseconds 3
    }
    Write-Host "[csharp-run] Sent IR ($($irBytes.Length) bytes, throttled)"

    # -- Phase 5: wait for the plug to finish; read C# from -output -----
    # The plug emits C# to its output ring def-by-def; codex-vm dumps the
    # ring to $csOutFile when the guest halts. We don't read TCP back.
    $tcpClient.Close()
    if (-not $proc.WaitForExit(1800000)) {
        [Console]::Error.WriteLine("FAIL: plug did not finish within 1800s")
        exit 4
    }
    # output_buf carries serial bytes (any boot/runtime diagnostics) ahead of
    # the drained C# ring; skip leading control bytes so the file starts at the
    # first real C# character.
    $csText = if (Test-Path $csOutFile) {
        $raw = [System.IO.File]::ReadAllBytes($csOutFile)
        $start = 0
        while ($start -lt $raw.Length -and $raw[$start] -lt 0x20 -and $raw[$start] -ne 9 -and $raw[$start] -ne 10 -and $raw[$start] -ne 13) { $start++ }
        [System.Text.Encoding]::UTF8.GetString($raw, $start, $raw.Length - $start)
    } else { "" }
    [System.IO.File]::WriteAllText($Out, $csText, [System.Text.UTF8Encoding]::new($false))
    Write-Host "[csharp-run] OK: $Out ($($csText.Length) chars)"

} finally {
    # -- Phase 5b: stop the VM, clean up temp files -------------------
    if ($proc -and -not $proc.HasExited) {
        try { Stop-Process -Id $proc.Id -Force -ErrorAction Stop } catch {}
    }
    try { Copy-Item -Force $stderrFile (Join-Path $IrDir 'vm-stderr.log') -ErrorAction Stop } catch {}
    Remove-Item -Force $stderrFile -ErrorAction SilentlyContinue
}
