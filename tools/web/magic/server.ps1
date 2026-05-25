# server.ps1 — CodexMagic web server
# Boots the codexmagic CDX in a VM, serves web pages, bridges API calls.
# Usage: tools/web/magic/server.ps1 [-Port 8090]
[CmdletBinding()]
param([int]$Port = 8090)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$WebDir   = $PSScriptRoot
$Repo     = (Resolve-Path (Join-Path $WebDir '..\..\..')).Path
$CdxPath  = Join-Path $Repo 'build-output\codexmagic.cdx'

. (Join-Path $Repo 'build\vm-config.ps1')

# ── VM Management ──────────────────────────────────────────────────

$script:GameVm = $null
$script:GameStream = $null

function Start-GameVm {
    if (-not (Test-Path -PathType Leaf $CdxPath)) {
        Write-Host "CDX not found: $CdxPath" -ForegroundColor Red
        Write-Host "Run: pwsh build/compile.ps1 -Src apps/games/codexmagic/opening.codex -Out build-output/codexmagic.cdx -Log build-output/codexmagic.log" -ForegroundColor Yellow
        return
    }
    Write-Host "Booting CodexMagic VM..." -ForegroundColor Cyan
    $port = Get-VmPort
    $ctrlPort = Get-VmPort
    $args = @(
        '-m', '2048',
        '-kernel', $CdxPath,
        '-chardev', (Get-VmChardevData $port),
        '-chardev', (Get-VmChardevCtrl $ctrlPort),
        '-device', 'isa-serial,chardev=ch0',
        '-device', 'isa-serial,chardev=ch1',
        '-display', 'none'
    )
    if ($script:UseCodexVm) {
        $proc = Start-Process -FilePath $script:CodexVmBin -ArgumentList $args -PassThru -WindowStyle Hidden
    } else {
        $proc = Start-Process -FilePath $script:FallbackVmBin -ArgumentList $args -PassThru -WindowStyle Hidden
    }
    Start-Sleep -Milliseconds 1500
    try {
        $conn = Connect-Vm -Port $port -TimeoutMs 15000
        $script:GameVm = @{ Process = $proc; Conn = $conn; Port = $port }
        $script:GameStream = $conn.Stream
        $ready = Read-VmReady -Stream $script:GameStream -TimeoutMs 10000
        if ($ready) {
            Write-Host "CodexMagic VM ready." -ForegroundColor Green
        } else {
            Write-Host "VM did not signal READY." -ForegroundColor Yellow
        }
    } catch {
        Write-Host "Failed to connect to VM: $_" -ForegroundColor Red
        $script:GameVm = $null
        $script:GameStream = $null
    }
}

function Send-GameRequest {
    param([string]$RequestLine)
    if (-not $script:GameStream) { return $null }
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($RequestLine + "`n")
        $script:GameStream.Write($bytes, 0, $bytes.Length)
        $script:GameStream.Flush()
        $line = Read-StreamLine -Stream $script:GameStream -TimeoutMs 10000
        return $line
    } catch {
        Write-Host "VM communication error: $_" -ForegroundColor Red
        $script:GameStream = $null
        return $null
    }
}

# ── Static File Serving ────────────────────────────────────────────

$MimeTypes = @{
    '.html' = 'text/html'; '.css' = 'text/css'; '.js' = 'application/javascript';
    '.json' = 'application/json'; '.png' = 'image/png'; '.jpg' = 'image/jpeg';
    '.svg' = 'image/svg+xml'; '.wav' = 'audio/wav'; '.mp3' = 'audio/mpeg';
}

function Send-StaticFile {
    param($Response, [string]$FilePath)
    if (Test-Path -PathType Leaf $FilePath) {
        $ext = [System.IO.Path]::GetExtension($FilePath).ToLower()
        $mime = if ($MimeTypes.ContainsKey($ext)) { $MimeTypes[$ext] } else { 'application/octet-stream' }
        $buf = [System.IO.File]::ReadAllBytes($FilePath)
        $Response.ContentType = "$mime; charset=utf-8"
        $Response.ContentLength64 = $buf.Length
        $Response.StatusCode = 200
        $Response.OutputStream.Write($buf, 0, $buf.Length)
    } else {
        $Response.StatusCode = 404
        $buf = [System.Text.Encoding]::UTF8.GetBytes('Not found')
        $Response.ContentType = 'text/plain; charset=utf-8'
        $Response.ContentLength64 = $buf.Length
        $Response.OutputStream.Write($buf, 0, $buf.Length)
    }
}

# ── HTTP Server ────────────────────────────────────────────────────

$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add("http://localhost:$Port/")
$listener.Start()
Write-Host ""
Write-Host "  CodexMagic server running at http://localhost:$Port/" -ForegroundColor Green
Write-Host "  Game:       http://localhost:$Port/web/magic/game.html" -ForegroundColor Cyan
Write-Host "  Collection: http://localhost:$Port/web/magic/collection.html" -ForegroundColor Cyan
Write-Host "  Store:      http://localhost:$Port/web/magic/store.html" -ForegroundColor Cyan
Write-Host "  Press Ctrl+C to stop." -ForegroundColor DarkGray
Write-Host ""

Start-GameVm

try {
    while ($listener.IsListening) {
        $ctx = $listener.GetContext()
        $resp = $ctx.Response
        $path = $ctx.Request.Url.AbsolutePath

        try {
            if ($path -eq '/' -or $path -eq '/index') {
                $resp.Redirect('/web/magic/game.html')
                $resp.StatusCode = 302
            }
            elseif ($path -like '/web/*') {
                $relFile = $path.Substring(5) -replace '/', '\'
                Send-StaticFile -Response $resp -FilePath (Join-Path (Split-Path $WebDir -Parent) $relFile)
            }
            elseif ($path -like '/api/magic/*') {
                if (-not $script:GameStream) {
                    Write-Host "  VM down -- restarting..." -ForegroundColor Yellow
                    if ($script:GameVm) {
                        try { Close-Vm -Conn $script:GameVm.Conn -Process $script:GameVm.Process } catch {}
                    }
                    Start-GameVm
                }
                if (-not $script:GameStream) {
                    $resp.StatusCode = 503
                    $buf = [System.Text.Encoding]::UTF8.GetBytes('{"error":"game server not running"}')
                    $resp.ContentType = 'application/json; charset=utf-8'
                    $resp.ContentLength64 = $buf.Length
                    $resp.OutputStream.Write($buf, 0, $buf.Length)
                } else {
                    $reqLine = "$($ctx.Request.HttpMethod) $($ctx.Request.Url.PathAndQuery)"
                    $respLine = Send-GameRequest -RequestLine $reqLine
                    if ($respLine) {
                        $firstSpace = $respLine.IndexOf(' ')
                        $secondSpace = if ($firstSpace -ge 0) { $respLine.IndexOf(' ', $firstSpace + 1) } else { -1 }
                        if ($firstSpace -gt 0 -and $secondSpace -gt 0) {
                            $statusCode = [int]$respLine.Substring(0, $firstSpace)
                            $contentType = $respLine.Substring($firstSpace + 1, $secondSpace - $firstSpace - 1)
                            $body = $respLine.Substring($secondSpace + 1)
                            $buf = [System.Text.Encoding]::UTF8.GetBytes($body)
                            $resp.StatusCode = $statusCode
                            $resp.ContentType = "$contentType; charset=utf-8"
                            $resp.Headers.Add('Access-Control-Allow-Origin', '*')
                            $resp.ContentLength64 = $buf.Length
                            $resp.OutputStream.Write($buf, 0, $buf.Length)
                        } else {
                            $resp.StatusCode = 502
                            $buf = [System.Text.Encoding]::UTF8.GetBytes('{"error":"bad response from game VM"}')
                            $resp.ContentType = 'application/json; charset=utf-8'
                            $resp.ContentLength64 = $buf.Length
                            $resp.OutputStream.Write($buf, 0, $buf.Length)
                        }
                    } else {
                        Write-Host "  VM timeout -- restarting..." -ForegroundColor Yellow
                        if ($script:GameVm) {
                            try { Close-Vm -Conn $script:GameVm.Conn -Process $script:GameVm.Process } catch {}
                        }
                        $script:GameVm = $null; $script:GameStream = $null
                        Start-GameVm
                        $resp.StatusCode = 504
                        $buf = [System.Text.Encoding]::UTF8.GetBytes('{"error":"restarting, try again"}')
                        $resp.ContentType = 'application/json; charset=utf-8'
                        $resp.ContentLength64 = $buf.Length
                        $resp.OutputStream.Write($buf, 0, $buf.Length)
                    }
                }
            }
            else {
                $resp.StatusCode = 404
                $buf = [System.Text.Encoding]::UTF8.GetBytes('<html><body style="background:#0d1117;color:#8b949e;font-family:monospace;padding:48px;text-align:center"><h1>404</h1><p>Not found</p><a href="/" style="color:#58a6ff">Back</a></body></html>')
                $resp.ContentType = 'text/html; charset=utf-8'
                $resp.ContentLength64 = $buf.Length
                $resp.OutputStream.Write($buf, 0, $buf.Length)
            }
        } catch {
            $resp.StatusCode = 500
        } finally {
            $resp.Close()
        }
    }
} finally {
    $listener.Stop()
    $listener.Close()
    if ($script:GameVm) {
        try { Close-Vm -Conn $script:GameVm.Conn -Process $script:GameVm.Process } catch {}
    }
    Write-Host "Server stopped." -ForegroundColor Yellow
}
