# Run the JavaScript plug over a Codex source file via TCP.
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
$PlugCdx  = Join-Path $PlugDir 'build-output\javascript-plug.cdx'
$IrDir    = Join-Path $PlugDir 'build-output'
$IrFile   = Join-Path $IrDir 'last-run.ir'
$LogFile  = Join-Path $IrDir 'run.log'

if (-not (Test-Path -PathType Leaf $PlugCdx)) {
    [Console]::Error.WriteLine("MISSING: $PlugCdx -- run plugs/javascript/build.ps1 first")
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
Write-Host "[js-run] IR: $($irBytes.Length) bytes"

# -- Phase 2: Start TCP listener -------------------------------------
$plugPort = 9100
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $plugPort)
$listener.Start()
Write-Host "[js-run] Listening on port $plugPort"

# -- Phase 3: Boot plug CDX ------------------------------------------
$stderrFile = [System.IO.Path]::GetTempFileName()
    $proc = Start-Process -FilePath $script:CodexVmBin -ArgumentList @('-kernel', $PlugCdx, '-mem', '3072', '-headless') `
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
    Write-Host "[js-run] Plug connected"

    # -- Phase 4: Send IR as framed message (tag=1) ------------------
    $msgLen = $irBytes.Length + 1
    $header = [BitConverter]::GetBytes([int]$msgLen)
    $tcpStream.Write($header, 0, 4)
    $tcpStream.WriteByte(1)
    $chunkSize = 4096
    $off = 0
    while ($off -lt $irBytes.Length) {
        $n = [Math]::Min($chunkSize, $irBytes.Length - $off)
        $tcpStream.Write($irBytes, $off, $n)
        $tcpStream.Flush()
        $off += $n
        if ($off -lt $irBytes.Length) { Start-Sleep -Milliseconds 20 }
    }
    Write-Host "[js-run] Sent IR ($($irBytes.Length) bytes, chunked)"

    # -- Phase 5: Receive output until plug sends FIN ----------------
    $tcpStream.ReadTimeout = 120000
    $allBytes = [System.Collections.Generic.List[byte]]::new(65536)
    $readBuf = [byte[]]::new(8192)
    try {
        while ($true) {
            $n = $tcpStream.Read($readBuf, 0, $readBuf.Length)
            if ($n -le 0) { break }
            for ($bi = 0; $bi -lt $n; $bi++) { $allBytes.Add($readBuf[$bi]) }
        }
    } catch {}
    $outText = [System.Text.Encoding]::UTF8.GetString($allBytes.ToArray())
    [System.IO.File]::WriteAllText($Out, $outText, [System.Text.UTF8Encoding]::new($false))
    Write-Host "[js-run] OK: $Out ($($outText.Length) chars)"

    $tcpClient.Close()

    # -- Phase 5b: Drain serial for diagnostic output -----------------
    if ($proc -and -not $proc.HasExited) {
        try { Stop-Process -Id $proc.Id -Force -ErrorAction Stop } catch {}
    }
    Remove-Item -Force $stderrFile -ErrorAction SilentlyContinue
