[CmdletBinding()]
param([int]$Port = 8790, [string]$Root = '')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $Root) { $Root = Join-Path $PSScriptRoot 'web' }
$Root = (Resolve-Path -LiteralPath $Root).Path
foreach ($name in @('index.html', 'workspace.html', 'modbuilder.html')) {
    if (-not (Test-Path -LiteralPath (Join-Path $Root $name) -PathType Leaf)) { throw "Missing $name. Run apps/modbuilder/build-site.ps1 first." }
}
$listener = [Net.HttpListener]::new()
$listener.Prefixes.Add("http://localhost:$Port/")
try {
    $listener.Start()
    Write-Host "ModBuilder: http://localhost:$Port/ (Ctrl+C stops the server)"
    while ($listener.IsListening) {
        $context = $listener.GetContext()
        try {
            $name = $context.Request.Url.AbsolutePath.TrimStart('/')
            if (-not $name) { $name = 'index.html' }
            if ($context.Request.HttpMethod -notin @('GET', 'HEAD')) { $context.Response.StatusCode = 405; continue }
            if ($name -notin @('index.html', 'workspace.html', 'modbuilder.html')) { $context.Response.StatusCode = 404; continue }
            $bytes = [IO.File]::ReadAllBytes((Join-Path $Root $name))
            $context.Response.ContentType = 'text/html; charset=utf-8'
            $context.Response.Headers['Cache-Control'] = 'no-cache'
            $context.Response.ContentLength64 = $bytes.Length
            if ($context.Request.HttpMethod -eq 'GET') { $context.Response.OutputStream.Write($bytes, 0, $bytes.Length) }
        } finally { $context.Response.Close() }
    }
} finally { $listener.Close() }
