# The local build bridge: the run half of the compile page's native-build
# configs.
#
# The page can resolve the exact build and run commands for what a lens just
# emitted, and it cannot execute them: a static page has no way to reach a
# toolchain. This is the optional other end. Start it and the page's Config
# panel gains working Build and Run buttons; do not start it and the page is
# exactly what it was, which is the shape the design asked for (an optional
# local bridge the page fully works without).
#
# THE SECURITY SHAPE, STATED PLAINLY BECAUSE THIS RUNS COMMANDS.
# Any page open in your browser can POST to 127.0.0.1. A bridge that executed
# whatever arrived would hand every site you visit a shell on this machine, so
# the guards here are not decoration:
#
#   - It binds 127.0.0.1 only, never a routable address, so nothing off this
#     machine can reach it at all.
#   - /run REQUIRES a token, printed once at startup and pasted into the page
#     by hand. A site that does not have it gets 403 no matter what it sends.
#     The token is per-run and lives nowhere on disk.
#   - It runs in ONE directory, -Root, defaulting to where you started it.
#   - It is off unless you start it, and it says what it is while it runs.
#
# /health answers without a token on purpose: the page needs to tell "no bridge
# running" from "bridge running, token wrong", and refusing to admit existence
# would collapse those two into one unhelpful state. It reveals only that a
# bridge is up.
#
#   apps/prism/build-bridge.ps1
#   apps/prism/build-bridge.ps1 -Port 8787 -Root D:\work\myproject
[CmdletBinding()]
param(
    [int]$Port = 8787,
    [string]$Root = (Get-Location).Path,
    # The page is served from a file:// origin when opened locally and from the
    # site when deployed; both are echoed back rather than '*', because '*' and
    # a credentialed request are not both allowed and the narrower answer costs
    # nothing here.
    [string[]]$Origin = @(),
    [int]$TimeoutSec = 300
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -PathType Container $Root)) {
    Write-Host "REFUSE: -Root is not a directory: $Root"; exit 2
}
$Root = (Resolve-Path $Root).Path

# A token the caller cannot guess. New every run: a bridge you restarted is a
# bridge whose old token stops working, which is the behaviour you want if you
# ever pasted it somewhere you should not have.
$token = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(18)).TrimEnd('=').Replace('+','-').Replace('/','_')

$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add("http://127.0.0.1:$Port/")
try { $listener.Start() }
catch {
    Write-Host "REFUSE: cannot listen on 127.0.0.1:$Port -- $($_.Exception.Message)"
    Write-Host "  Another bridge may already be running, or the port is taken."
    exit 3
}

Write-Host ''
Write-Host '  Prism local build bridge' -ForegroundColor Magenta
Write-Host "  listening   http://127.0.0.1:$Port  (this machine only)"
Write-Host "  working dir $Root"
Write-Host "  token       $token" -ForegroundColor Yellow
Write-Host '  Paste that token into the compile page: Configs -> Bridge.'
Write-Host '  It RUNS COMMANDS in the directory above. Ctrl+C stops it.'
Write-Host ''

function Write-Json($ctx, [int]$status, $obj) {
    $res = $ctx.Response
    $res.StatusCode = $status
    $res.ContentType = 'application/json'
    # Echo the caller's origin rather than '*': the page may be file:// (which
    # arrives as the literal "null") or the deployed site, and both are fine
    # here because the TOKEN is what authorises, not the origin.
    $o = $ctx.Request.Headers['Origin']
    if ($o) {
        if ($Origin.Count -eq 0 -or $Origin -contains $o) { $res.Headers.Add('Access-Control-Allow-Origin', $o) }
    }
    $res.Headers.Add('Vary', 'Origin')
    $bytes = [Text.Encoding]::UTF8.GetBytes(($obj | ConvertTo-Json -Depth 6 -Compress))
    $res.ContentLength64 = $bytes.Length
    $res.OutputStream.Write($bytes, 0, $bytes.Length)
    $res.OutputStream.Close()
}

# The elapsed time is measured HERE rather than in the page, because the page's
# clock would include the request, the JSON and the browser's own scheduling.
# Bench wants the command's own duration; anything else makes a fast build look
# slow on a busy machine and is not comparable between runs.
function Invoke-Bridged([string]$cmd, [string]$cwd) {
    # Commands are shell lines the user wrote into their own config, so they go
    # to a shell. stdout and stderr are captured separately: a build that fails
    # says why on stderr, and folding them together loses which was which.
    $out = [IO.Path]::GetTempFileName(); $err = [IO.Path]::GetTempFileName()
    $sw = [Diagnostics.Stopwatch]::StartNew()
    try {
        $p = Start-Process -FilePath 'pwsh' -ArgumentList @('-NoProfile', '-NonInteractive', '-Command', $cmd) `
             -WorkingDirectory $cwd -NoNewWindow -PassThru `
             -RedirectStandardOutput $out -RedirectStandardError $err
        if (-not $p.WaitForExit($TimeoutSec * 1000)) {
            try { $p.Kill() } catch {}
            $sw.Stop()
            return @{ ok = $false; code = -1; out = ''; ms = [int]$sw.ElapsedMilliseconds; err = "the bridge stopped this after $TimeoutSec s" }
        }
        $sw.Stop()
        return @{ ok = ($p.ExitCode -eq 0); code = $p.ExitCode; ms = [int]$sw.ElapsedMilliseconds
                  out = (Get-Content $out -Raw -ErrorAction SilentlyContinue)
                  err = (Get-Content $err -Raw -ErrorAction SilentlyContinue) }
    } finally {
        Remove-Item $out, $err -Force -ErrorAction SilentlyContinue
    }
}

try {
    while ($listener.IsListening) {
        $ctx = $listener.GetContext()
        $req = $ctx.Request
        $path = $req.Url.AbsolutePath.TrimEnd('/')
        if ($path -eq '') { $path = '/' }

        # A POST carrying a custom header is preflighted, so OPTIONS has to be
        # answered before any /run ever arrives.
        if ($req.HttpMethod -eq 'OPTIONS') {
            $ctx.Response.Headers.Add('Access-Control-Allow-Methods', 'GET, POST, OPTIONS')
            $ctx.Response.Headers.Add('Access-Control-Allow-Headers', 'Content-Type, X-Bridge-Token')
            $ctx.Response.Headers.Add('Access-Control-Max-Age', '600')
            Write-Json $ctx 204 @{ ok = $true }
            continue
        }

        if ($path -eq '/health') {
            # Tokenless, so the page can tell "no bridge" from "bridge up".
            # But if a token IS offered, say whether it is the right one:
            # otherwise the page's probe reports "answering" on a wrong token
            # and the user finds out only when a build 403s, which is the
            # failure arriving at the least useful moment. This confirms what
            # the caller already sent and reveals nothing it did not.
            $given = $req.Headers['X-Bridge-Token']
            $h = @{ ok = $true; bridge = 'prism'; version = 1; root = $Root }
            if ($given) { $h.tokenOk = ($given -eq $token) }
            Write-Json $ctx 200 $h
            continue
        }

        if ($path -eq '/write' -and $req.HttpMethod -eq 'POST') {
            # A build command needs a file to build, and the page is holding the
            # only copy of what the lens emitted. Without this the whole feature
            # resolves commands against a filename that exists nowhere, which is
            # what a first real press found.
            $body = ''
            $reader = [IO.StreamReader]::new($req.InputStream, $req.ContentEncoding)
            try { $body = $reader.ReadToEnd() } finally { $reader.Dispose() }
            $sent = $null
            try { $sent = $body | ConvertFrom-Json } catch { Write-Json $ctx 400 @{ ok = $false; err = 'body is not JSON' }; continue }
            $given = $req.Headers['X-Bridge-Token']
            if (-not $given -and $sent -and ($sent.PSObject.Properties.Name -contains 'token')) { $given = $sent.token }
            if ($given -ne $token) { Write-Json $ctx 403 @{ ok = $false; err = 'bad or missing token' }; continue }
            $name = if ($sent.PSObject.Properties.Name -contains 'name') { [string]$sent.name } else { '' }
            # A LEAF NAME ONLY. The page sends what a lens produced, but this
            # endpoint accepts bytes from a browser, so it must not be able to
            # write outside -Root however the name is spelled: take the leaf,
            # then refuse anything that still is not a plain filename.
            $leaf = Split-Path $name -Leaf
            if (-not $leaf -or $leaf -ne $name -or $leaf -match '[\\/:*?"<>|]' -or $leaf -in @('.', '..')) {
                Write-Json $ctx 400 @{ ok = $false; err = "not a plain filename: $name" }; continue
            }
            $text = if ($sent.PSObject.Properties.Name -contains 'text') { [string]$sent.text } else { '' }
            $dest = Join-Path $Root $leaf
            # Belt and braces: the resolved path must still be inside -Root.
            $full = [IO.Path]::GetFullPath($dest)
            if (-not $full.StartsWith(([IO.Path]::GetFullPath($Root) + [IO.Path]::DirectorySeparatorChar), [StringComparison]::OrdinalIgnoreCase)) {
                Write-Json $ctx 400 @{ ok = $false; err = 'refused: outside the bridge root' }; continue
            }
            [IO.File]::WriteAllText($full, $text, [Text.UTF8Encoding]::new($false))
            Write-Host "  wrote: $leaf ($($text.Length) chars)"
            Write-Json $ctx 200 @{ ok = $true; path = $full; bytes = $text.Length }
            continue
        }

        if ($path -eq '/run' -and $req.HttpMethod -eq 'POST') {
            $body = ''
            $reader = [IO.StreamReader]::new($req.InputStream, $req.ContentEncoding)
            try { $body = $reader.ReadToEnd() } finally { $reader.Dispose() }
            $sent = $null
            try { $sent = $body | ConvertFrom-Json } catch {
                Write-Json $ctx 400 @{ ok = $false; err = 'body is not JSON' }; continue
            }
            $given = $req.Headers['X-Bridge-Token']
            if (-not $given -and $sent -and ($sent.PSObject.Properties.Name -contains 'token')) { $given = $sent.token }
            if ($given -ne $token) {
                Write-Host "  refused a /run with a bad token" -ForegroundColor DarkYellow
                Write-Json $ctx 403 @{ ok = $false; err = 'bad or missing token' }; continue
            }
            $cmd = if ($sent.PSObject.Properties.Name -contains 'cmd') { [string]$sent.cmd } else { '' }
            if (-not $cmd.Trim()) { Write-Json $ctx 400 @{ ok = $false; err = 'no command' }; continue }
            Write-Host "  run: $cmd"
            $r = Invoke-Bridged $cmd $Root
            Write-Host ("     exit $($r.code) in $($r.ms) ms")
            Write-Json $ctx 200 @{ ok = $r.ok; code = $r.code; out = $r.out; err = $r.err; ms = $r.ms }
            continue
        }

        Write-Json $ctx 404 @{ ok = $false; err = "no such endpoint: $path" }
    }
} finally {
    if ($listener.IsListening) { $listener.Stop() }
    $listener.Close()
    Write-Host 'bridge stopped.'
}
