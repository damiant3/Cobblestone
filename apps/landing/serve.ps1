# Serve the whole landing site as ONE origin: the static page, the packaged
# self-compile surface, and the two live backends behind it.
#
# Why a proxy rather than three links to three ports. Prism and the REPL are
# servers, not pages; they cannot be copied into the site as files. But they
# also never deploy standalone (Damian, 2026-08-26), so the bundle is what
# matters, not their on-disk shape. One listener owns the origin and forwards:
#
#   /               -> web/            static, includes compile/
#   /prism/         -> 127.0.0.1:8080  apps/prism/run.ps1, compiles on the fly
#   /repl/          -> 127.0.0.1:9100  Steve Howell's essay-repl-server
#
# so the landing page's links are plain relative paths that work in the
# deployed bundle, and a visitor never sees a port.
#
# A backend that is not up does NOT produce a dead link: its path serves a
# page saying exactly what is missing and the command that fixes it. A dead
# button is the one outcome worth engineering against here.
[CmdletBinding()]
param(
    [int]$Port = 8088,
    [int]$PrismPort = 8080,
    [int]$ReplPort = 9100,
    [switch]$NoPrism,
    [switch]$NoRepl
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$AppDir = (Resolve-Path $PSScriptRoot).Path
$Repo   = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$Web    = Join-Path $AppDir 'web'
$ReplRepo = 'D:\Projects\essay-repl-server-main'
$ReplVenv = Join-Path $AppDir 'build-output\repl-venv'

if (-not (Test-Path (Join-Path $Web 'landing.html'))) {
    Write-Host "REFUSE: no web/landing.html. Run apps/landing/build.ps1 first."; exit 2
}

function Test-Listening([int]$p) {
    $null -ne (Get-NetTCPConnection -LocalPort $p -State Listen -ErrorAction SilentlyContinue)
}

# --- backends ---------------------------------------------------------

$prismUp = Test-Listening $PrismPort
if (-not $NoPrism -and -not $prismUp) {
    Write-Host "[serve] starting Prism on $PrismPort ..."
    Start-Process pwsh -ArgumentList @('-NoProfile','-File',(Join-Path $Repo 'apps\prism\run.ps1'),'-Port',$PrismPort) -WindowStyle Minimized
}

$replUp = Test-Listening $ReplPort
$replWhy = ''
if (-not $NoRepl -and -not $replUp) {
    $venvPy = Join-Path $ReplVenv 'Scripts\python.exe'
    if (-not (Test-Path $ReplRepo)) {
        $replWhy = "The REPL source is not on this box. Expected it at $ReplRepo (clone https://github.com/showell/essay-repl-server)."
    } elseif (-not (Test-Path $venvPy)) {
        $replWhy = "The REPL's Python environment is not built yet. Run: apps/landing/build.ps1 -Repl"
    } else {
        # Authoritative rather than a platform guess: ask this very
        # interpreter whether it can import what repl.py imports.
        & $venvPy -c 'import resource' 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            $replWhy = 'The REPL is Unix-only. repl.py imports the resource module to bound each run with RLIMIT, and that module does not exist on Windows; the venv and Flask install fine, the import does not. It runs where it is deployed, on the Linux droplet, and this box is not that.'
        } else {
            Write-Host "[serve] starting the REPL on $ReplPort ..."
            Start-Process $venvPy -ArgumentList @((Join-Path $ReplRepo 'app.py')) -WorkingDirectory $ReplRepo -WindowStyle Minimized
        }
    }
}
# --- the listener -----------------------------------------------------

$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add("http://localhost:$Port/")
$listener.Start()
Write-Host ''
Write-Host "[serve] http://localhost:$Port/landing.html"
Write-Host "[serve] Ctrl+C to stop."
Write-Host ''

$mime = @{
    '.html'='text/html; charset=utf-8'; '.css'='text/css'; '.js'='text/javascript'
    '.jpg'='image/jpeg'; '.png'='image/png'; '.svg'='image/svg+xml'
    '.wasm'='application/wasm'; '.json'='application/json'
    '.codex'='text/plain; charset=utf-8'; '.txt'='text/plain; charset=utf-8'
}

function Send-Text($ctx, [int]$code, [string]$body, [string]$type = 'text/html; charset=utf-8') {
    $b = [Text.Encoding]::UTF8.GetBytes($body)
    $ctx.Response.StatusCode = $code
    $ctx.Response.ContentType = $type
    $ctx.Response.ContentLength64 = $b.Length
    $ctx.Response.OutputStream.Write($b, 0, $b.Length)
}

function Send-Unavailable($ctx, [string]$name, [string]$why, [string]$link) {
    $html = @"
<!doctype html><meta charset=utf-8><title>$name is not running</title>
<style>body{background:#0a0a12;color:#e0e0e8;font-family:system-ui,sans-serif;
display:flex;min-height:100vh;align-items:center;justify-content:center;margin:0}
div{max-width:34em;padding:2em;line-height:1.7}h1{color:#d4a017;font-size:1.4em}
code{background:#1e1e2e;padding:2px 6px;border-radius:4px}a{color:#d4a017}</style>
<div><h1>$name is not running</h1><p>$why</p>
<p>It is a live server, not a page, which is why it cannot be copied into the
site as files. The rest of the site works without it.</p>
<p><a href="/landing.html">Back to Cobblestone</a>$link</p></div>
"@
    Send-Text $ctx 503 $html
}

function Invoke-Proxy($ctx, [int]$port, [string]$prefix) {
    $tail = $ctx.Request.Url.PathAndQuery.Substring($prefix.Length)
    if (-not $tail.StartsWith('/')) { $tail = '/' + $tail }
    $target = "http://127.0.0.1:$port$tail"
    $req = [System.Net.HttpWebRequest]::Create($target)
    $req.Method = $ctx.Request.HttpMethod
    $req.AllowAutoRedirect = $false
    if ($ctx.Request.HasEntityBody) {
        $req.ContentType = $ctx.Request.ContentType
        $in = New-Object IO.MemoryStream
        $ctx.Request.InputStream.CopyTo($in)
        $bytes = $in.ToArray()
        $req.ContentLength = $bytes.Length
        $rs = $req.GetRequestStream(); $rs.Write($bytes,0,$bytes.Length); $rs.Close()
    }
    $resp = $req.GetResponse()
    $ctx.Response.StatusCode = [int]$resp.StatusCode
    $ctx.Response.ContentType = $resp.ContentType
    $resp.GetResponseStream().CopyTo($ctx.Response.OutputStream)
    $resp.Close()
}

try {
    while ($listener.IsListening) {
        $ctx = $listener.GetContext()
        try {
            $p = $ctx.Request.Url.AbsolutePath
            if ($p -eq '/') { $p = '/landing.html' }

            if ($p.StartsWith('/prism')) {
                if (Test-Listening $PrismPort) { Invoke-Proxy $ctx $PrismPort '/prism' }
                else { Send-Unavailable $ctx 'Prism' 'Prism did not come up on this box. It boots prism.cdx inside codex-vm and compiles on the fly, so it needs the repo and a working VM.' '' }
            }
            elseif ($p.StartsWith('/repl')) {
                if (Test-Listening $ReplPort) { Invoke-Proxy $ctx $ReplPort '/repl' }
                else {
                    $why = if ($replWhy) { $replWhy } else { 'The REPL did not come up on this box.' }
                    Send-Unavailable $ctx 'The online REPL' $why ' &middot; <a href="https://github.com/showell/essay-repl-server">source</a>'
                }
            }
            else {
                $rel = $p.TrimStart('/') -replace '/', '\'
                $file = Join-Path $Web $rel
                $full = [IO.Path]::GetFullPath($file)
                if (-not $full.StartsWith([IO.Path]::GetFullPath($Web))) { Send-Text $ctx 403 'no' 'text/plain' }
                elseif (Test-Path -PathType Leaf $full) {
                    $bytes = [IO.File]::ReadAllBytes($full)
                    $ext = [IO.Path]::GetExtension($full).ToLower()
                    $ctx.Response.ContentType = $(if ($mime.ContainsKey($ext)) { $mime[$ext] } else { 'application/octet-stream' })
                    $ctx.Response.ContentLength64 = $bytes.Length
                    $ctx.Response.OutputStream.Write($bytes, 0, $bytes.Length)
                }
                else { Send-Text $ctx 404 '<p style="font:16px system-ui">Not found. <a href="/landing.html">Back</a></p>' }
            }
        } catch {
            try { Send-Text $ctx 502 ('<pre>' + $_.Exception.Message + '</pre>') } catch {}
        } finally {
            try { $ctx.Response.OutputStream.Close() } catch {}
        }
    }
} finally {
    $listener.Stop()
}
