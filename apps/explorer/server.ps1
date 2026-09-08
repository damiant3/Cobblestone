# server.ps1 -- Explorer web server
# Boots the ExplorerServer CDX in a VM, bridges HTTP requests to
# the CDX over TCP (via NE2K NIC NAT) or serial (fallback).
# Usage: apps/explorer/server.ps1 [-Port 8889] [-Mode tcp]
[CmdletBinding()]
param(
    [int]$Port = 8889,
    [int]$MagicPort = 8090,
    [int]$SdPort = 7860,
    [int]$TcpBridgePort = 9100,
    [ValidateSet('tcp','serial')]
    [string]$Mode = 'tcp'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$WebDir    = $PSScriptRoot
$Repo      = (Resolve-Path (Join-Path $WebDir '..\..')).Path
$CacheDir  = 'D:\Projects\CodexMagic\explorer\cache'
$PagesDir  = 'D:\Projects\CodexMagic\explorer\pages'
$CdxPath   = Join-Path $Repo 'build-output\explorer-server.cdx'
$SdApi     = "http://localhost:$SdPort/sdapi/v1"

New-Item -ItemType Directory -Force $CacheDir | Out-Null

. (Join-Path $Repo 'build\vm-config.ps1')

# ── SD API helpers ────────────────────────────────────────────

function Invoke-SdApi {
    param([string]$Path, [string]$Method = 'GET', $Body = $null)
    $uri = "$SdApi/$Path"
    $params = @{ Uri = $uri; Method = $Method; ContentType = 'application/json'; TimeoutSec = 300 }
    if ($Body) { $params.Body = ($Body | ConvertTo-Json -Depth 10 -Compress) }
    try { Invoke-RestMethod @params } catch { $null }
}

function Get-PromptHash {
    param([string]$Prompt)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Prompt)
    $hash = $sha.ComputeHash($bytes)
    ($hash | ForEach-Object { $_.ToString('x2') }) -join '' | ForEach-Object { $_.Substring(0, 12) }
}

# ── VM Management ─────────────────────────────────────────────

$script:ExplorerVm = $null
$script:ExplorerStream = $null
$script:VmBuf = New-Object byte[] 65536

function Start-ExplorerVm {
    if (-not (Test-Path -PathType Leaf $CdxPath)) {
        Write-Host "CDX not found: $CdxPath" -ForegroundColor Red
        Write-Host "Compiling ExplorerServer.codex..." -ForegroundColor Yellow
        $src = Join-Path $Repo 'apps\explorer\ExplorerServer.codex'
        $log = Join-Path $Repo 'build-output\explorer-server.log'
        New-Item -ItemType Directory -Force (Split-Path $CdxPath) | Out-Null
        & pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') -Src $src -Out $CdxPath -Log $log 2>&1 | Out-Null
        if (-not (Test-Path -PathType Leaf $CdxPath)) {
            Write-Host "  Compilation failed." -ForegroundColor Red
            return
        }
        Write-Host "  Compiled: $((Get-Item $CdxPath).Length) bytes" -ForegroundColor Green
    }

    Write-Host "Booting Explorer VM (codex-vm, NE2K NIC)..." -ForegroundColor Cyan
    $stderrFile = [System.IO.Path]::GetTempFileName()
    $vmArgs = @('-kernel', $CdxPath, '-mem', '2048', '-headless')
    $script:VmProcess = Start-Process -FilePath $script:CodexVmBin -ArgumentList $vmArgs -PassThru -WindowStyle Hidden -RedirectStandardError $stderrFile
    $script:VmStderrFile = $stderrFile

    if ($script:VmProcess.HasExited) {
        Write-Host "  VM exited immediately." -ForegroundColor Red
        return
    }
    Write-Host "  VM PID: $($script:VmProcess.Id). Waiting for CDX to connect on port $TcpBridgePort..." -ForegroundColor Cyan

    if (-not (Wait-TcpConnection -TimeoutSec 30)) {
        Write-Host "  CDX did not connect via TCP." -ForegroundColor Red
        try { Stop-Process -Id $script:VmProcess.Id -Force -ErrorAction Stop } catch {}
        Remove-Item -Force $stderrFile -ErrorAction SilentlyContinue
        return
    }
    Write-Host "  CDX connected." -ForegroundColor Green
}

function Feed-SdConfig {
    # Query SD API and feed config lines to the CDX
    $models = Invoke-SdApi 'sd-models'
    $samplers = Invoke-SdApi 'samplers'
    $loras = Invoke-SdApi 'loras'
    $upscalers = Invoke-SdApi 'upscalers'
    $opts = Invoke-SdApi 'options'

    $skip = @('uberRealistic', '0.5(')
    foreach ($m in $models) {
        $dominated = $false
        foreach ($s in $skip) { if ($m.model_name -like "*$s*" -or $m.title -like "*$s*") { $dominated = $true } }
        if (-not $dominated) {
            Send-VmLine "CONFIG model $($m.title)`t$($m.model_name)"
        }
    }
    $goodSamplers = @('DPM++ SDE','DPM++ 2M SDE','DPM++ 2S a','Euler a','Euler','DDIM','UniPC','Heun','DPM++ 3M SDE')
    foreach ($s in $samplers) {
        if ($s.name -in $goodSamplers) {
            Send-VmLine "CONFIG sampler $($s.name)"
        }
    }
    foreach ($l in $loras) {
        Send-VmLine "CONFIG lora $($l.name)"
    }
    foreach ($u in $upscalers) {
        if ($u.name -ne 'None') {
            Send-VmLine "CONFIG upscaler $($u.name)"
        }
    }
    if ($opts) {
        Send-VmLine "CONFIG current $($opts.sd_model_checkpoint)"
    }
    Send-VmLine 'CONFIG-END'
    # Drain any response
    Start-Sleep -Milliseconds 500
    try { $null = $script:ExplorerStream.Read($script:VmBuf, 0, $script:VmBuf.Length) } catch {}
}

function Send-VmLine {
    param([string]$Line)
    if (-not $script:ExplorerStream) { return }
    $bytes = [System.Text.Encoding]::UTF8.GetBytes("$Line`n")
    $script:ExplorerStream.Write($bytes, 0, $bytes.Length)
    $script:ExplorerStream.Flush()
}

function Send-VmRequest {
    param([string]$RequestLine)
    if (-not $script:ExplorerStream) { return $null }
    try {
        Send-VmLine $RequestLine
        $acc = ''
        $deadline = (Get-Date).AddSeconds(10)
        while ((Get-Date) -lt $deadline) {
            try {
                $n = $script:ExplorerStream.Read($script:VmBuf, 0, $script:VmBuf.Length)
                if ($n -gt 0) {
                    $acc += [System.Text.Encoding]::UTF8.GetString($script:VmBuf, 0, $n)
                    if ($acc.Contains("`n")) { return ($acc -split "`n")[0].TrimEnd("`r") }
                }
            } catch { break }
        }
        if ($acc.Length -gt 0) { return $acc.TrimEnd("`r", "`n") }
        return $null
    } catch { return $null }
}

# ── TCP Bridge ────────────────────────────────────────────────

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
    Write-Host "  TCP connection timeout" -ForegroundColor Red
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

function Stop-TcpBridge {
    if ($script:TcpStream) { try { $script:TcpStream.Close() } catch {} }
    if ($script:TcpClient) { try { $script:TcpClient.Close() } catch {} }
    if ($script:TcpListener) { try { $script:TcpListener.Stop() } catch {} }
}

function Send-CdxRequest {
    param([string]$RequestLine)
    if ($script:UseTcp -and $script:TcpStream) {
        return Send-TcpRequest $RequestLine
    } else {
        return Send-VmRequest $RequestLine
    }
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

# ── Static file serving ───────────────────────────────────────

$MimeTypes = @{
    '.html' = 'text/html'; '.css' = 'text/css'; '.js' = 'application/javascript'
    '.json' = 'application/json'; '.png' = 'image/png'; '.jpg' = 'image/jpeg'
    '.svg' = 'image/svg+xml'; '.ico' = 'image/x-icon'
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

# ── SD Generation ─────────────────────────────────────────────

$script:CurrentModel = ''

function Switch-SdModel {
    param([string]$Title)
    if ($script:CurrentModel -eq $Title) { return $true }
    Write-Host "  Switching SD model to $($Title.Split('.')[0])..." -ForegroundColor Yellow
    try {
        Invoke-SdApi 'options' -Method 'POST' -Body @{ sd_model_checkpoint = $Title }
        $deadline = (Get-Date).AddSeconds(120)
        while ((Get-Date) -lt $deadline) {
            $opts = Invoke-SdApi 'options'
            if ($opts -and $opts.sd_model_checkpoint -like "*$Title*") {
                $script:CurrentModel = $Title
                Write-Host "  Model loaded." -ForegroundColor Green
                return $true
            }
            Start-Sleep -Seconds 2
        }
    } catch {}
    Write-Host "  Model switch failed." -ForegroundColor Red
    return $false
}

function Invoke-SdGenerate {
    param($Body)
    $prompt = $Body.prompt
    $neg = $Body.negative_prompt
    $modelTitle = $Body.model_title
    $modelShort = $Body.model_short
    $sampler = $Body.sampler
    $steps = $Body.steps
    $cfg = $Body.cfg
    $lora = $Body.lora
    $loraShort = if ($Body.lora_short) { $Body.lora_short } else { 'none' }
    $seed = if ($Body.seed) { $Body.seed } else { 424242 }
    $width = if ($Body.width) { $Body.width } else { 768 }
    $height = if ($Body.height) { $Body.height } else { 1024 }

    $promptHash = Get-PromptHash $prompt
    $promptDir = Join-Path $CacheDir $promptHash
    New-Item -ItemType Directory -Force $promptDir | Out-Null

    # Save prompt text
    $promptFile = Join-Path $promptDir '_prompt.txt'
    if (-not (Test-Path $promptFile)) { $prompt | Set-Content -Path $promptFile -Encoding UTF8 }

    # Build filename (must match CDX make-cache-key)
    $sClean = ($sampler -replace ' ','_' -replace '\+','p')
    $mClean = ($modelShort -replace '[^a-zA-Z0-9]','_')
    if ($mClean.Length -gt 30) { $mClean = $mClean.Substring(0, 30) }
    $lClean = if ($loraShort -eq '' -or $loraShort -eq $null) { 'none' } else { $loraShort -replace '[^a-zA-Z0-9]','_' }
    if ($lClean.Length -gt 25) { $lClean = $lClean.Substring(0, 25) }
    $fname = "${mClean}_${sClean}_s${steps}_c${cfg}_lora_${lClean}_seed${seed}.png"
    $fpath = Join-Path $promptDir $fname
    $cacheUrl = "/cache/$promptHash/$fname"

    # Return cached
    if (Test-Path $fpath) {
        return @{ status = 'cached'; url = $cacheUrl; file = $fname }
    }

    # Switch model if needed
    if ($modelTitle -and $modelTitle -ne $script:CurrentModel) {
        if (-not (Switch-SdModel $modelTitle)) {
            return @{ status = 'error'; error = 'Model switch failed' }
        }
    }

    # Build SD prompt
    $fullPrompt = $prompt
    if ($lora -and $lora -ne '') { $fullPrompt = "<lora:${lora}:0.8>, $prompt" }

    # Call SD API
    $sdBody = @{
        prompt = $fullPrompt
        negative_prompt = $neg
        steps = [int]$steps
        sampler_name = $sampler
        cfg_scale = [double]$cfg
        width = [int]$width
        height = [int]$height
        seed = [int]$seed
    }
    $result = Invoke-SdApi 'txt2img' -Method 'POST' -Body $sdBody
    if (-not $result -or -not $result.images) {
        return @{ status = 'error'; error = 'SD API returned no images' }
    }

    $imgBytes = [Convert]::FromBase64String($result.images[0])
    [System.IO.File]::WriteAllBytes($fpath, $imgBytes)
    Write-Host "  Generated $fname ($([math]::Round($imgBytes.Length/1024))KB)" -ForegroundColor DarkGreen

    return @{ status = 'ok'; url = $cacheUrl; file = $fname; size_kb = [math]::Round($imgBytes.Length/1024) }
}

# ── Boot ──────────────────────────────────────────────────────

# Detect current SD model
try {
    $opts = Invoke-SdApi 'options'
    if ($opts) { $script:CurrentModel = $opts.sd_model_checkpoint }
    Write-Host "SD model: $($script:CurrentModel)" -ForegroundColor Gray
} catch {
    Write-Host "Warning: SD API not reachable on port $SdPort" -ForegroundColor Yellow
}

# ── CodexMagic proxy (forward /api/magic/* and /api/clan/* to MagicPort) ──

$script:MagicHttp = [System.Net.Http.HttpClient]::new()
$script:MagicHttp.Timeout = [TimeSpan]::FromSeconds(15)
$script:MagicBase = "http://localhost:$MagicPort"

function Proxy-ToMagic {
    param($Context, $Response)
    $url = "$($script:MagicBase)$($Context.Request.Url.PathAndQuery)"
    try {
        $result = $script:MagicHttp.GetAsync($url).GetAwaiter().GetResult()
        $body = $result.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
        $Response.StatusCode = [int]$result.StatusCode
        $ct = $result.Content.Headers.ContentType
        $Response.ContentType = if ($ct) { $ct.ToString() } else { 'application/json; charset=utf-8' }
        $Response.Headers.Add('Access-Control-Allow-Origin', '*')
        $Response.ContentLength64 = $body.Length
        $Response.OutputStream.Write($body, 0, $body.Length)
    } catch {
        Send-Json -Response $Response -Json '{"error":"codexmagic server unavailable"}' -Status 502
    }
}

$script:UseTcp = ($Mode -eq 'tcp')

function Cleanup-Resources {
    if ($script:TcpStream)   { try { $script:TcpStream.Close() }   catch {}; $script:TcpStream = $null }
    if ($script:TcpClient)   { try { $script:TcpClient.Close() }   catch {}; $script:TcpClient = $null }
    if ($script:TcpListener) { try { $script:TcpListener.Stop() }  catch {}; $script:TcpListener = $null }
    if ($script:VmProcess -and -not $script:VmProcess.HasExited) {
        try { Stop-Process -Id $script:VmProcess.Id -Force } catch {}
        $script:VmProcess = $null
    }
    if ($listener) { try { $listener.Stop(); $listener.Close() } catch {} }
    if ($script:MagicHttp) { try { $script:MagicHttp.Dispose() } catch {} }
}

try { [Console]::CancelKeyPress.Add({ Cleanup-Resources }) } catch {}

# ── HTTP Server ───────────────────────────────────────────────

$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add("http://localhost:$Port/")
$listener.Start()
Write-Host ""
Write-Host "  Explorer running at http://localhost:$Port/" -ForegroundColor Green
Write-Host "  Mode: $Mode" -ForegroundColor Gray
Write-Host "  Press Ctrl+C to stop." -ForegroundColor DarkGray
Write-Host ""

try {
    if ($script:UseTcp) { Start-TcpBridge }
    try { Start-ExplorerVm } catch { Write-Host "  CDX VM not available: $_" -ForegroundColor Yellow }
    while ($listener.IsListening) {
        $ctx = $listener.GetContext()
        $resp = $ctx.Response

        try {
            $path = $ctx.Request.Url.AbsolutePath
            $query = $ctx.Request.Url.PathAndQuery
            $method = $ctx.Request.HttpMethod

            if ($path -eq '/' -or $path -eq '/index.html' -or $path -eq '/item') {
                Send-StaticFile -Response $resp -FilePath (Join-Path $PagesDir 'item.html')
            }
            elseif ($path -eq '/character') {
                Send-StaticFile -Response $resp -FilePath (Join-Path $PagesDir 'character.html')
            }
            elseif ($path -eq '/setting') {
                Send-StaticFile -Response $resp -FilePath (Join-Path $PagesDir 'setting.html')
            }
            elseif ($path -eq '/card') {
                Send-StaticFile -Response $resp -FilePath (Join-Path $PagesDir 'card.html')
            }
            elseif ($path -match '^\/([\w-]+\.(js|css))$') {
                Send-StaticFile -Response $resp -FilePath (Join-Path $PagesDir $matches[1])
            }
            elseif ($path -like '/cache/*') {
                # Serve cached images
                $relPath = $path.Substring(7) -replace '/', '\'
                Send-StaticFile -Response $resp -FilePath (Join-Path $CacheDir $relPath)
            }
            elseif ($path -like '/api/magic/*' -or $path -like '/api/clan/*') {
                Proxy-ToMagic -Context $ctx -Response $resp
            }
            elseif ($path -like '/api/*') {
                $cdxResp = Send-CdxRequest "GET $query"
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
    Cleanup-Resources
    Write-Host "Server stopped." -ForegroundColor Yellow
}
