# Idea Forge -- boot the webservice and serve the browser UI.
#
# Usage: apps/ideas/run.ps1            (build everything, boot, serve)
#        apps/ideas/run.ps1 -SkipBuild (reuse existing build-output artifacts)
#        apps/ideas/run.ps1 -NoVm      (bridge only; VM already running)
# Then open http://localhost:8080 in a browser.
#
# The bridge serves the plug-built page at / and forwards every other
# GET to the codex-vm guest via the -portfwd mapping, so the page and
# the API share one origin and no CORS is involved.
[CmdletBinding()]
param(
    [int]$Port = 8080,
    [int]$VmPort = 9201,
    [switch]$SkipBuild,
    [switch]$NoVm
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo    = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$OutDir  = Join-Path $Repo 'build-output'
$AppCdx  = Join-Path $OutDir 'idea-forge.cdx'
$PageOut = Join-Path $OutDir 'idea-forge-page.html'

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory $OutDir | Out-Null }

if (-not $SkipBuild) {
    Write-Host '[forge] Compiling IdeaServer.codex...'
    & pwsh -File (Join-Path $Repo 'build\compile.ps1') -Src (Join-Path $Repo 'apps\ideas\IdeaServer.codex') -Out $AppCdx -Log (Join-Path $OutDir 'idea-forge.log')
    if ($LASTEXITCODE -ne 0) { throw "compile failed; see build-output/idea-forge.log" }
    Write-Host '[forge] Building IdeaPage through the HTML plug...'
    & pwsh -File (Join-Path $Repo 'codex\plugs\html\run.ps1') -Src (Join-Path $Repo 'apps\ideas\IdeaPage.codex') -Out $PageOut
    if ($LASTEXITCODE -ne 0) { throw 'HTML plug build failed' }
}

if (-not (Test-Path $AppCdx))  { throw "missing $AppCdx (run without -SkipBuild)" }
if (-not (Test-Path $PageOut)) { throw "missing $PageOut (run without -SkipBuild)" }

$script:vmProc = $null
$script:vmBoots = 0
function Start-ForgeVm {
    $script:vmBoots++
    Write-Host "[forge] Booting idea-forge on codex-vm (host :$VmPort -> guest :9200, boot #$script:vmBoots)..."
    $script:vmProc = Start-Process -FilePath (Join-Path $Repo 'tools\codex-vm.exe') -ArgumentList @(
        '-kernel', $AppCdx, '-headless', '-portfwd', "${VmPort}:9200",
        '-output', (Join-Path $OutDir 'idea-forge-serve.out')
    ) -RedirectStandardError (Join-Path $OutDir "idea-forge-vm$script:vmBoots.err") `
      -RedirectStandardOutput (Join-Path $OutDir "idea-forge-vm$script:vmBoots.out") -PassThru
    Start-Sleep -Seconds 3
}
if (-not $NoVm) { Start-ForgeVm }

$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add("http://localhost:$Port/")
$listener.Start()
Write-Host "[forge] The house is open: http://localhost:$Port  (Ctrl+C to close)"

try {
    while ($listener.IsListening) {
        $ctx  = $listener.GetContext()
        $path = $ctx.Request.Url.AbsolutePath
        Write-Host "[forge] $($ctx.Request.HttpMethod) $($ctx.Request.Url.PathAndQuery)"
        try {
            if ($path -eq '/' -or $path -eq '/index.html') {
                $bytes = [System.IO.File]::ReadAllBytes($PageOut)
                $ctx.Response.ContentType = 'text/html'
                $ctx.Response.OutputStream.Write($bytes, 0, $bytes.Length)
            } else {
                $upstream = "http://localhost:$VmPort$($ctx.Request.Url.PathAndQuery)"
                $r = $null
                try {
                    $r = Invoke-WebRequest -Uri $upstream -TimeoutSec 30 -UseBasicParsing -SkipHttpErrorCheck
                } catch {
                    if ((-not $NoVm) -and $script:vmProc -and $script:vmProc.HasExited) {
                        Write-Host '[forge] the house collapsed; rebuilding it...'
                        Start-ForgeVm
                        $r = Invoke-WebRequest -Uri $upstream -TimeoutSec 30 -UseBasicParsing -SkipHttpErrorCheck
                    } else { throw }
                }
                $ctx.Response.StatusCode = [int]$r.StatusCode
                $ctx.Response.ContentType = "$($r.Headers['Content-Type'])"
                $bytes = [System.Text.Encoding]::UTF8.GetBytes("$($r.Content)")
                $ctx.Response.OutputStream.Write($bytes, 0, $bytes.Length)
            }
        } catch {
            Write-Host "[forge] proxy error for $($ctx.Request.Url.PathAndQuery): $($_.Exception.Message)"
            $ctx.Response.StatusCode = 502
            $bytes = [System.Text.Encoding]::UTF8.GetBytes('{"error":"the house is not answering"}')
            $ctx.Response.OutputStream.Write($bytes, 0, $bytes.Length)
        }
        $ctx.Response.Close()
    }
} finally {
    $listener.Stop()
    if ($script:vmProc -and -not $script:vmProc.HasExited) { $script:vmProc.Kill() }
}
