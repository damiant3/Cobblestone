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
    [Parameter(Mandatory=$true)] [string]$Src,
    [Parameter(Mandatory=$true)] [string]$Out
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

# -- Phase 1: Codex source -> IR text --------------------------------
$compileScript = Join-Path $Repo 'build\compile.ps1'
& pwsh -File $compileScript -Src $Src -Out $IrFile -Log $LogFile -IrCce
if ($LASTEXITCODE -ne 0) {
    [Console]::Error.WriteLine("FAIL: IR emit step exited $LASTEXITCODE; see $LogFile")
    exit 3
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
    $proc = Start-Process -FilePath $script:CodexVmBin -ArgumentList @('-kernel', $PlugCdx, '-mem', '4096', '-headless') `
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
    $tcpStream.Write($header, 0, 4)
    $tcpStream.WriteByte(1)  # tag = 1 (IR)
    $tcpStream.Write($irBytes, 0, $irBytes.Length)
    $tcpStream.Flush()
    Write-Host "[csharp-run] Sent IR ($($irBytes.Length) bytes)"

    # -- Phase 5: Receive C# output until plug sends FIN ----------------
    # The plug sends all C# data then calls net-io-close (FIN). We read
    # until Read returns 0 (EOF = FIN received). Then we close our side,
    # which signals the plug to exit. No timeouts needed for the data
    # path — only a deadline for the overall operation.
    $tcpStream.ReadTimeout = 120000  # 2 minutes for large programs
    $allBytes = [System.Collections.Generic.List[byte]]::new(65536)
    $readBuf = [byte[]]::new(8192)
    try {
        while ($true) {
            $n = $tcpStream.Read($readBuf, 0, $readBuf.Length)
            if ($n -le 0) { break }
            for ($bi = 0; $bi -lt $n; $bi++) { $allBytes.Add($readBuf[$bi]) }
        }
    } catch {}
    $csText = [System.Text.Encoding]::UTF8.GetString($allBytes.ToArray())
    [System.IO.File]::WriteAllText($Out, $csText, [System.Text.UTF8Encoding]::new($false))
    Write-Host "[csharp-run] OK: $Out ($($csText.Length) chars)"

    # Close our side — the plug's drain-wait sees this and exits cleanly
    $tcpClient.Close()

    # -- Phase 5b: Drain serial for diagnostic output -----------------
    if ($proc -and -not $proc.HasExited) {
        try { Stop-Process -Id $proc.Id -Force -ErrorAction Stop } catch {}
    }
    Remove-Item -Force $stderrFile -ErrorAction SilentlyContinue
}
}
