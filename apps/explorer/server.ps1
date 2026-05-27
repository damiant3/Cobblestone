# server.ps1 — SD Explorer web server
# Boots the SdExplorer CDX in a VM, feeds it SD config over serial,
# bridges HTTP requests, proxies SD API calls, and caches images.
# Usage: tools/web/explorer/server.ps1 [-Port 8888] [-SdPort 7860]
[CmdletBinding()]
param(
    [int]$Port = 8888,
    [int]$SdPort = 7860
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$WebDir    = $PSScriptRoot
$Repo      = (Resolve-Path (Join-Path $WebDir '..\..\..')).Path
$CacheDir  = 'D:\Projects\CodexMagic\explorer\cache'
$PagesDir  = 'D:\Projects\CodexMagic\explorer\pages'
$CdxPath   = Join-Path $Repo 'build-output\sd-explorer.cdx'
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
        Write-Host "Compiling SdExplorer.codex..." -ForegroundColor Yellow
        $src = Join-Path $Repo 'apps\works\SdExplorer.codex'
        $log = Join-Path $Repo 'build-output\sd-explorer.log'
        New-Item -ItemType Directory -Force (Split-Path $CdxPath) | Out-Null
        & pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') -Src $src -Out $CdxPath -Log $log 2>&1 | Out-Null
        if (-not (Test-Path -PathType Leaf $CdxPath)) {
            Write-Host "  Compilation failed." -ForegroundColor Red
            return
        }
        Write-Host "  Compiled: $((Get-Item $CdxPath).Length) bytes" -ForegroundColor Green
    }

    Write-Host "Booting SD Explorer VM..." -ForegroundColor Cyan
    $run = Start-VmRun -Kernel $CdxPath -ConnectTimeoutSec 30 -MemMB 2048
    if (-not $run) {
        Write-Host "  VM failed to start." -ForegroundColor Red
        return
    }
    $script:ExplorerVm = $run
    $script:ExplorerStream = $run.Conn.Data.GetStream()
    $script:ExplorerStream.ReadTimeout = 15000

    # Wait for READY
    $ready = ''
    $deadline = (Get-Date).AddSeconds(15)
    while ((Get-Date) -lt $deadline) {
        try {
            $n = $script:ExplorerStream.Read($script:VmBuf, 0, $script:VmBuf.Length)
            if ($n -gt 0) {
                $ready += [System.Text.Encoding]::UTF8.GetString($script:VmBuf, 0, $n)
                if ($ready -match 'READY') { break }
            }
        } catch { break }
    }

    if ($ready -notmatch 'READY') {
        Write-Host "  VM did not signal READY." -ForegroundColor Red
        Close-Vm -Conn $run.Conn -Process $run.Process
        $script:ExplorerVm = $null; $script:ExplorerStream = $null
        return
    }
    Write-Host "  VM ready. Feeding SD config..." -ForegroundColor Green

    # Feed SD configuration to the CDX
    Feed-SdConfig
    Write-Host "  Config loaded." -ForegroundColor Green
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

# CDX VM disabled for now — pages are pre-built, SD proxy works directly
# To enable: uncomment Start-ExplorerVm below
# try { Start-ExplorerVm } catch {
#     Write-Host "  CDX VM not available." -ForegroundColor Yellow
# }
Write-Host "  CDX VM: skipped (pages pre-built)" -ForegroundColor DarkGray

# ── HTTP Server ───────────────────────────────────────────────

$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add("http://localhost:$Port/")
$listener.Start()
Write-Host ""
Write-Host "  SD Explorer running at http://localhost:$Port/" -ForegroundColor Green
Write-Host "  SD API: http://localhost:$SdPort/" -ForegroundColor Gray
Write-Host "  Cache: $CacheDir" -ForegroundColor Gray
Write-Host "  Press Ctrl+C to stop." -ForegroundColor DarkGray
Write-Host ""

try {
    while ($listener.IsListening) {
        $ctx = $listener.GetContext()
        $resp = $ctx.Response

        try {
            $path = $ctx.Request.Url.AbsolutePath
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
            elseif ($path -eq '/api/config' -and $method -eq 'GET') {
                # Forward to CDX
                $vmResp = Send-VmRequest "GET /api/config"
                if ($vmResp) {
                    $firstSpace = $vmResp.IndexOf(' ')
                    $secondSpace = if ($firstSpace -ge 0) { $vmResp.IndexOf(' ', $firstSpace + 1) } else { -1 }
                    if ($secondSpace -gt 0) {
                        $body = $vmResp.Substring($secondSpace + 1)
                        Send-Json -Response $resp -Json $body
                    } else {
                        Send-Json -Response $resp -Json '{"error":"bad CDX response"}' -Status 502
                    }
                } else {
                    # Fallback: build config directly from SD API
                    $cfg = @{
                        models = @(); samplers = @(); loras = @(); upscalers = @()
                        current_model = $script:CurrentModel
                        steps_options = @(4,8,12,15,20,25,30,40)
                        cfg_options = @(1,2,3,4,5,7,9,12)
                    }
                    $sdModels = Invoke-SdApi 'sd-models'
                    $skip = @('uberRealistic','0.5(')
                    foreach ($m in $sdModels) {
                        $bad = $false
                        foreach ($s in $skip) { if ($m.model_name -like "*$s*") { $bad = $true } }
                        if (-not $bad) { $cfg.models += @{ title = $m.title; name = $m.model_name } }
                    }
                    $sdSamplers = Invoke-SdApi 'samplers'
                    $good = @('DPM++ SDE','DPM++ 2M SDE','DPM++ 2S a','Euler a','Euler','DDIM','UniPC','Heun','DPM++ 3M SDE')
                    foreach ($s in $sdSamplers) { if ($s.name -in $good) { $cfg.samplers += $s.name } }
                    $sdLoras = Invoke-SdApi 'loras'
                    foreach ($l in $sdLoras) { $cfg.loras += @{ name = $l.name } }
                    $sdUp = Invoke-SdApi 'upscalers'
                    foreach ($u in $sdUp) { if ($u.name -ne 'None') { $cfg.upscalers += $u.name } }
                    Send-Json -Response $resp -Json ($cfg | ConvertTo-Json -Depth 5 -Compress)
                }
            }
            elseif ($path -eq '/api/generate' -and $method -eq 'POST') {
                $reader = New-Object System.IO.StreamReader($ctx.Request.InputStream)
                $body = $reader.ReadToEnd() | ConvertFrom-Json
                $result = Invoke-SdGenerate $body
                Send-Json -Response $resp -Json ($result | ConvertTo-Json -Compress)
            }
            elseif ($path -eq '/api/cached' -and $method -eq 'POST') {
                $reader = New-Object System.IO.StreamReader($ctx.Request.InputStream)
                $body = $reader.ReadToEnd() | ConvertFrom-Json
                $hash = Get-PromptHash $body.prompt
                $dir = Join-Path $CacheDir $hash
                $files = @()
                if (Test-Path $dir) {
                    $files = (Get-ChildItem -File $dir -Filter '*.png' |
                              Where-Object { $_.Name -notlike '_*' }).Name
                }
                Send-Json -Response $resp -Json (@{ hash = $hash; files = $files; count = $files.Count } | ConvertTo-Json -Compress)
            }
            elseif ($path -eq '/api/status' -and $method -eq 'GET') {
                $vmResp = Send-VmRequest "GET /api/status"
                if ($vmResp -and $vmResp.IndexOf(' ') -gt 0) {
                    $body = $vmResp.Substring($vmResp.IndexOf(' ', $vmResp.IndexOf(' ') + 1) + 1)
                    Send-Json -Response $resp -Json $body
                } else {
                    Send-Json -Response $resp -Json '{"status":"vm unavailable"}'
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
    if ($script:ExplorerVm) {
        Write-Host "Shutting down Explorer VM..." -ForegroundColor Gray
        Close-Vm -Conn $script:ExplorerVm.Conn -Process $script:ExplorerVm.Process
    }
    Write-Host "Server stopped." -ForegroundColor Yellow
}