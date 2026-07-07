# Run the ObjC plug over a Codex source file via TCP.
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
$PlugCdx  = Join-Path $PlugDir 'build-output\objc-plug.cdx'
$IrDir    = Join-Path $PlugDir 'build-output'
$IrFile   = Join-Path $IrDir 'last-run.ir'
$LogFile  = Join-Path $IrDir 'run.log'

if (-not (Test-Path -PathType Leaf $PlugCdx)) {
    [Console]::Error.WriteLine("MISSING: $PlugCdx -- run plugs/objc/build.ps1 first")
    exit 2
}

# -- Phase 1: Codex source -> IR text --------------------------------
$compileScript = Join-Path $Repo 'build' 'compile.ps1'
& pwsh -NoProfile -File $compileScript -Src $Src -Out $IrFile -Log $LogFile -IrCce 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $IrFile)) {
    [Console]::Error.WriteLine("FAIL: IR compile failed; see $LogFile")
    exit 4
}
# -- Phase 2: IR text -> ObjC via plug -------------------------------
$stderrFile = [System.IO.Path]::GetTempFileName()
try {
    $proc = Start-Process -FilePath $script:CodexVmBin -ArgumentList @('-kernel', $PlugCdx, '-mem', '3072', '-headless') `
        -PassThru -WindowStyle Hidden -RedirectStandardError $stderrFile
    # Send IR over TCP to plug
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
    $hdr = [BitConverter]::GetBytes([int]$msgLen)
    $ns.Write($hdr, 0, 4)
    $ns.WriteByte(1)
    $chunkSize = 4096
    $off = 0
    while ($off -lt $irData.Length) {
        $n = [Math]::Min($chunkSize, $irData.Length - $off)
        $ns.Write($irData, $off, $n)
        $ns.Flush()
        $off += $n
        if ($off -lt $irData.Length) { Start-Sleep -Milliseconds 50 }
    }
    # Read response
    $ns.ReadTimeout = 120000
    $resp = [System.Collections.Generic.List[byte]]::new()
    $buf = New-Object byte[] 65536
    try {
        while ($true) {
            $n = $ns.Read($buf, 0, $buf.Length)
            if ($n -le 0) { break }
            for ($bi = 0; $bi -lt $n; $bi++) { $resp.Add($buf[$bi]) }
        }
    } catch {}
    $client.Close()
    if ($resp.Count -eq 0) { [Console]::Error.WriteLine("FAIL: empty response from plug"); exit 8 }
    [System.IO.File]::WriteAllBytes($Out, $resp.ToArray())
    Write-Host "[objc-plug] OK: $Out ($($resp.Count) bytes)"
} finally {
    if ($proc -and -not $proc.HasExited) {
        try { Stop-Process -Id $proc.Id -Force -ErrorAction Stop } catch {}
    }
    Remove-Item -Force $stderrFile -ErrorAction SilentlyContinue
}
