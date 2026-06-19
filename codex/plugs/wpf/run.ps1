# Run the WPF plug over a Codex source file via TCP.
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
$PlugCdx  = Join-Path $PlugDir 'build-output\wpf-plug.cdx'
$IrDir    = Join-Path $PlugDir 'build-output'
$IrFile   = Join-Path $IrDir 'last-run.ir'
$LogFile  = Join-Path $IrDir 'run.log'

if (-not (Test-Path -PathType Leaf $PlugCdx)) {
    [Console]::Error.WriteLine("MISSING: $PlugCdx -- run plugs/wpf/build.ps1 first")
    exit 2
}

# -- Phase 1: obtain IR text -----------------------------------------
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
Write-Host "[wpf-run] IR: $($irBytes.Length) bytes"

# -- Phase 2: Start TCP listener -------------------------------------
$plugPort = 9100
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $plugPort)
$listener.Start()
Write-Host "[wpf-run] Listening on port $plugPort"

# -- Phase 3: Boot plug CDX ------------------------------------------
$stderrFile = [System.IO.Path]::GetTempFileName()
$wpfOutFile = Join-Path $IrDir 'plug-wpf.out'
Remove-Item -Force $wpfOutFile -ErrorAction SilentlyContinue
$proc = $null
try {
    $proc = Start-Process -FilePath $script:CodexVmBin -ArgumentList @('-kernel', $PlugCdx, '-mem', "$MemMB", '-headless', '-output', $wpfOutFile) `
        -PassThru -WindowStyle Hidden -RedirectStandardError $stderrFile
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
    Write-Host "[wpf-run] Plug connected"

    # -- Phase 4: Send IR as framed message (tag=1) ------------------
    $msgLen = $irBytes.Length + 1
    $header = [BitConverter]::GetBytes([int]$msgLen)
    $tcpClient.NoDelay = $true
    $tcpStream.Write($header, 0, 4)
    $tcpStream.WriteByte(1)
    $chunk = 16384
    $off = 0
    while ($off -lt $irBytes.Length) {
        $len = [Math]::Min($chunk, $irBytes.Length - $off)
        $tcpStream.Write($irBytes, $off, $len)
        $tcpStream.Flush()
        $off += $len
        Start-Sleep -Milliseconds 3
    }
    $tcpClient.Client.Shutdown([System.Net.Sockets.SocketShutdown]::Send)
    Write-Host "[wpf-run] Sent IR ($($irBytes.Length) bytes)"

    # -- Phase 5: read WPF output from TCP -----
    $resp = [System.Collections.Generic.List[byte]]::new()
    $buf = New-Object byte[] 65536
    $deadline2 = [DateTime]::UtcNow.AddSeconds(120)
    while ([DateTime]::UtcNow -lt $deadline2) {
        if ($tcpStream.DataAvailable) {
            try {
                $n = $tcpStream.Read($buf, 0, $buf.Length)
                if ($n -le 0) { break }
                for ($j = 0; $j -lt $n; $j++) { $resp.Add($buf[$j]) }
            } catch { break }
        } elseif ($proc.HasExited) {
            Start-Sleep -Milliseconds 100
            if ($tcpStream.DataAvailable) { continue }
            break
        } else {
            Start-Sleep -Milliseconds 50
        }
    }
    $tcpClient.Close()
    $wpfText = [System.Text.Encoding]::UTF8.GetString($resp.ToArray())
    Write-Host "[wpf-run] Received $($resp.Count) bytes from plug"

    # -- Phase 6: Split multi-file output into directory -----
    New-Item -ItemType Directory -Force -Path $Out | Out-Null
    $files = $wpfText -split '<<<FILE:([^>]+)>>>\r?\n'
    for ($i = 1; $i -lt $files.Count; $i += 2) {
        $fname = $files[$i]
        $content = $files[$i + 1]
        $fpath = Join-Path $Out $fname
        [System.IO.File]::WriteAllText($fpath, $content, [System.Text.UTF8Encoding]::new($false))
        Write-Host "[wpf-run] Wrote: $fname ($($content.Length) chars)"
    }
    Write-Host "[wpf-run] OK: $Out"

} finally {
    if ($proc -and -not $proc.HasExited) {
        try { Stop-Process -Id $proc.Id -Force -ErrorAction Stop } catch {}
    }
    try { Copy-Item -Force $stderrFile (Join-Path $IrDir 'vm-stderr.log') -ErrorAction Stop } catch {}
    Remove-Item -Force $stderrFile -ErrorAction SilentlyContinue
}
