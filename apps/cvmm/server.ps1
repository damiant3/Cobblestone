# server.ps1 -- CVMM web server
# Boots the CvmmServer CDX in a VM, bridges HTTP requests to the CDX
# over framed TCP (NE2K NIC NAT). Serves the dashboard HTML page and
# proxies /api/* to the bare-metal server.
# Usage: apps/cvmm/server.ps1 [-Port 2682] [-Mode tcp]
# Port 2682: IANA removed (2002-04-30), unassigned. Claimed for CVMM.
[CmdletBinding()]
param(
    [int]$Port = 2682,
    [int]$TcpBridgePort = 9100,
    [ValidateSet('tcp','serial')]
    [string]$Mode = 'tcp'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$AppDir    = $PSScriptRoot
$Repo      = (Resolve-Path (Join-Path $AppDir '..\..')).Path
$CdxPath   = Join-Path $AppDir 'build-output\cvmm-server.cdx'

. (Join-Path $Repo 'build\vm-config.ps1')

# -- VM Management ---------------------------------------------------------

$script:CvmmVm = $null
$script:CvmmStream = $null
$script:VmBuf = New-Object byte[] 65536

function Start-CvmmVm {
    if (-not (Test-Path -PathType Leaf $CdxPath)) {
        Write-Host "CDX not found: $CdxPath" -ForegroundColor Red
        Write-Host "Building CVMM server..." -ForegroundColor Yellow
        & pwsh -NoProfile -File (Join-Path $AppDir 'build.ps1')
        if (-not (Test-Path -PathType Leaf $CdxPath)) {
            Write-Host "  Build failed." -ForegroundColor Red
            return
        }
        Write-Host "  Built: $((Get-Item $CdxPath).Length) bytes" -ForegroundColor Green
    }

    Write-Host "Booting CVMM VM..." -ForegroundColor Cyan
    $run = Start-VmRun -Kernel $CdxPath -ConnectTimeoutSec 30 -MemMB 2048
    if (-not $run) {
        Write-Host "  VM failed to start." -ForegroundColor Red
        return
    }
    $script:CvmmVm = $run
    $script:CvmmStream = $run.Conn.Data.GetStream()
    $script:CvmmStream.ReadTimeout = 15000

    $ready = ''
    $deadline = (Get-Date).AddSeconds(15)
    while ((Get-Date) -lt $deadline) {
        try {
            $n = $script:CvmmStream.Read($script:VmBuf, 0, $script:VmBuf.Length)
            if ($n -gt 0) {
                $ready += [System.Text.Encoding]::UTF8.GetString($script:VmBuf, 0, $n)
                if ($ready -match 'READY') { break }
            }
        } catch { break }
    }

    if ($ready -notmatch 'READY') {
        Write-Host "  VM did not signal READY." -ForegroundColor Red
        Close-Vm -Conn $run.Conn -Process $run.Process
        $script:CvmmVm = $null; $script:CvmmStream = $null
        return
    }
    Write-Host "  VM ready." -ForegroundColor Green

    if ($script:UseTcp) {
        Send-VmLine 'TCP'
        Write-Host "  Sent TCP mode. Waiting for CDX connection..." -ForegroundColor Cyan
        if (-not (Wait-TcpConnection -TimeoutSec 30)) {
            Write-Host "  CDX did not connect via TCP." -ForegroundColor Red
            Close-Vm -Conn $run.Conn -Process $run.Process
            $script:CvmmVm = $null; $script:CvmmStream = $null
            return
        }
    } else {
        Send-VmLine 'SERIAL'
        Write-Host "  Serial mode active." -ForegroundColor Green
    }
}

function Send-VmLine {
    param([string]$Line)
    if (-not $script:CvmmStream) { return }
    $bytes = [System.Text.Encoding]::UTF8.GetBytes("$Line`n")
    $script:CvmmStream.Write($bytes, 0, $bytes.Length)
    $script:CvmmStream.Flush()
}

# -- TCP Bridge ------------------------------------------------------------

$script:TcpListener = $null
$script:TcpClient = $null
$script:TcpStream = $null

function Start-TcpBridge {
    $script:TcpListener = [System.Net.Sockets.TcpListener]::new(
        [System.Net.IPAddress]::Loopback, $TcpBridgePort)
    $script:TcpListener.Start()
    Write-Host "  TCP bridge listening on port $TcpBridgePort" -ForegroundColor Cyan
}

function Wait-TcpConnection {
    param([int]$TimeoutSec = 30)
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        if ($script:TcpListener.Pending()) {
            $script:TcpClient = $script:TcpListener.AcceptTcpClient()
            $script:TcpClient.NoDelay = $true
            $script:TcpClient.ReceiveTimeout = 15000
            $script:TcpStream = $script:TcpClient.GetStream()
            Write-Host "  CDX connected via TCP" -ForegroundColor Green
            return $true
        }
        Start-Sleep -Milliseconds 100
    }
    return $false
}

function Send-FramedMessage {
    param([int]$Tag, [byte[]]$Body)
    if (-not $script:TcpStream) { return }
    $totalLen = 1 + $Body.Length
    $header = [BitConverter]::GetBytes([int]$totalLen)
    $script:TcpStream.Write($header, 0, 4)
    $script:TcpStream.WriteByte([byte]$Tag)
    if ($Body.Length -gt 0) { $script:TcpStream.Write($Body, 0, $Body.Length) }
    $script:TcpStream.Flush()
}

function Recv-FramedMessage {
    param([int]$TimeoutMs = 10000)
    if (-not $script:TcpStream) { return $null }
    $old = $script:TcpStream.ReadTimeout
    $script:TcpStream.ReadTimeout = $TimeoutMs
    try {
        $hdr = New-Object byte[] 4
        $read = 0
        while ($read -lt 4) {
            $n = $script:TcpStream.Read($hdr, $read, 4 - $read)
            if ($n -le 0) { return $null }
            $read += $n
        }
        $msgLen = [BitConverter]::ToInt32($hdr, 0)
        if ($msgLen -lt 1 -or $msgLen -gt 1048576) { return $null }
        $payload = New-Object byte[] $msgLen
        $read = 0
        while ($read -lt $msgLen) {
            $n = $script:TcpStream.Read($payload, $read, $msgLen - $read)
            if ($n -le 0) { return $null }
            $read += $n
        }
        $tag = $payload[0]
        $body = if ($msgLen -gt 1) { $payload[1..($msgLen - 1)] } else { @() }
        return @{ Tag = $tag; Body = $body; Text = [System.Text.Encoding]::UTF8.GetString($body) }
    } catch { return $null }
    finally { $script:TcpStream.ReadTimeout = $old }
}

function Send-TcpRequest {
    param([string]$RequestLine)
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($RequestLine)
    Send-FramedMessage -Tag 1 -Body $bytes
    $resp = Recv-FramedMessage -TimeoutMs 10000
    if ($resp) { return $resp.Text }
    return $null
}

function Send-CdxRequest {
    param([string]$RequestLine)
    if ($script:UseTcp -and $script:TcpStream) {
        return Send-TcpRequest $RequestLine
    }
    return $null
}

function Parse-CdxResponse {
    param([string]$Raw)
    if (-not $Raw) { return $null }
    $firstSpace = $Raw.IndexOf(' ')
    if ($firstSpace -lt 0) { return $null }
    $secondSpace = $Raw.IndexOf(' ', $firstSpace + 1)
    if ($secondSpace -lt 0) { return $null }
    $status = $Raw.Substring(0, $firstSpace)
    $ctype = $Raw.Substring($firstSpace + 1, $secondSpace - $firstSpace - 1)
    $body = $Raw.Substring($secondSpace + 1)
    return @{ Status = [int]$status; ContentType = $ctype; Body = $body }
}

function Stop-TcpBridge {
    if ($script:TcpStream) { try { $script:TcpStream.Close() } catch {} }
    if ($script:TcpClient) { try { $script:TcpClient.Close() } catch {} }
    if ($script:TcpListener) { try { $script:TcpListener.Stop() } catch {} }
}

# -- Static + JSON helpers -------------------------------------------------

$MimeTypes = @{
    '.html' = 'text/html'; '.css' = 'text/css'; '.js' = 'application/javascript'
    '.json' = 'application/json'; '.png' = 'image/png'; '.svg' = 'image/svg+xml'
    '.wasm' = 'application/wasm'; '.ico' = 'image/x-icon'
}

function Send-StaticFile {
    param($Response, [string]$FilePath)
    if (Test-Path -PathType Leaf $FilePath) {
        $ext = [System.IO.Path]::GetExtension($FilePath).ToLower()
        $mime = if ($MimeTypes.ContainsKey($ext)) { $MimeTypes[$ext] } else { 'application/octet-stream' }
        $buf = [System.IO.File]::ReadAllBytes($FilePath)
        $Response.ContentType = $mime
        $Response.ContentLength64 = $buf.Length
        $Response.StatusCode = 200
        $Response.OutputStream.Write($buf, 0, $buf.Length)
    } else {
        $Response.StatusCode = 404
        $buf = [System.Text.Encoding]::UTF8.GetBytes('Not found')
        $Response.ContentType = 'text/plain'
        $Response.ContentLength64 = $buf.Length
        $Response.OutputStream.Write($buf, 0, $buf.Length)
    }
}

function Send-Json {
    param($Response, [string]$Json, [int]$Status = 200)
    $buf = [System.Text.Encoding]::UTF8.GetBytes($Json)
    $Response.StatusCode = $Status
    $Response.ContentType = 'application/json; charset=utf-8'
    $Response.Headers.Add('Access-Control-Allow-Origin', '*')
    $Response.ContentLength64 = $buf.Length
    $Response.OutputStream.Write($buf, 0, $buf.Length)
}

# -- Boot ------------------------------------------------------------------

$script:UseTcp = ($Mode -eq 'tcp')
if ($script:UseTcp) { Start-TcpBridge }
try { Start-CvmmVm } catch {
    Write-Host "  CDX VM not available: $_" -ForegroundColor Yellow
}

# -- HTTP Server -----------------------------------------------------------

$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add("http://localhost:$Port/")
$listener.Start()
Write-Host ""
Write-Host "  CVMM running at http://localhost:$Port/" -ForegroundColor Green
Write-Host "  Mode: $Mode" -ForegroundColor Gray
Write-Host "  Press Ctrl+C to stop." -ForegroundColor DarkGray
Write-Host ""

$WebDir = Join-Path $AppDir 'web'

try {
    while ($listener.IsListening) {
        $ctx = $listener.GetContext()
        $resp = $ctx.Response

        try {
            $path = $ctx.Request.Url.AbsolutePath
            $method = $ctx.Request.HttpMethod

            if ($path -eq '/' -or $path -eq '/index.html') {
                Send-StaticFile -Response $resp -FilePath (Join-Path $WebDir 'index.html')
            }
            elseif ($path -match '^\/([\w.-]+\.(js|css|html|svg|png|wasm|ico))$') {
                Send-StaticFile -Response $resp -FilePath (Join-Path $WebDir $matches[1])
            }
            elseif ($path -like '/api/*') {
                $cdxResp = Send-CdxRequest "$method $path"
                $parsed = Parse-CdxResponse $cdxResp
                if ($parsed) {
                    Send-Json -Response $resp -Json $parsed.Body -Status $parsed.Status
                } else {
                    Send-Json -Response $resp -Json '{"error":"CDX unavailable"}' -Status 502
                }
            }
            else {
                $resp.StatusCode = 404
                $buf = [System.Text.Encoding]::UTF8.GetBytes('Not found')
                $resp.ContentType = 'text/plain'
                $resp.ContentLength64 = $buf.Length
                $resp.OutputStream.Write($buf, 0, $buf.Length)
            }
        } catch {
            Write-Host "  Error: $_" -ForegroundColor Red
            $resp.StatusCode = 500
        } finally {
            $resp.Close()
        }
    }
} finally {
    $listener.Stop()
    $listener.Close()
    Stop-TcpBridge
    if ($script:CvmmVm) {
        Write-Host "Shutting down CVMM VM..." -ForegroundColor Gray
        Close-Vm -Conn $script:CvmmVm.Conn -Process $script:CvmmVm.Process
    }
    Write-Host "Server stopped." -ForegroundColor Yellow
}
