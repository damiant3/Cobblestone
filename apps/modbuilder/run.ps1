[CmdletBinding()]
param(
    [string]$Profile = '',
    [int]$Port = 8787,
    [switch]$Workspace,
    [string]$Preset = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$root = Join-Path $repo 'build-output/prism-targets'
if (-not $Profile) { $Profile = Join-Path $root 'valheim-local.prism-target.json' }
if ($Preset) {
    $catalogue = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'mods/valheim/features.json') -Raw | ConvertFrom-Json
    if ($Preset -notin $catalogue.features.id) { throw "Unknown Valheim preset: $Preset" }
    $Workspace = $true
}
$page = Join-Path $PSScriptRoot $(if ($Workspace) { 'web/workspace.html' } else { 'web/modbuilder.html' })
if (-not (Test-Path -LiteralPath $page -PathType Leaf)) { throw "The page is not built: $page" }
[void](New-Item -ItemType Directory -Force -Path $root)

$probe = [Net.Sockets.TcpClient]::new()
try { $busy = $probe.ConnectAsync('127.0.0.1', $Port).Wait(500) } catch { $busy = $false } finally { $probe.Dispose() }
if ($busy) { throw "Something already listens on 127.0.0.1:$Port (a bridge left open?). Close it, or pass -Port." }

$b64url = { param([byte[]]$b) [Convert]::ToBase64String($b).TrimEnd('=').Replace('+', '-').Replace('/', '_') }
$token = & $b64url ([Security.Cryptography.RandomNumberGenerator]::GetBytes(18))
$fragment = '#bridge=' + $token
if (Test-Path -LiteralPath $Profile -PathType Leaf) {
    $json = Get-Content -LiteralPath $Profile -Raw | ConvertFrom-Json | ConvertTo-Json -Compress
    $fragment += '&profile=' + [uri]::EscapeDataString($json)
} else {
    Write-Host "No profile at $Profile; fill the Game profile section on the page."
}
if ($Workspace) {
    $fragment += '&bridgeUrl=' + [uri]::EscapeDataString("http://127.0.0.1:$Port")
    if ($Preset) { $fragment += '&preset=' + [uri]::EscapeDataString($Preset) }
}

$env:PRISM_BRIDGE_TOKEN = $token
try {
    $bridge = Start-Process -FilePath (Get-Command pwsh).Source -WindowStyle Hidden -PassThru -ArgumentList @('-NoProfile', '-File', ('"' + (Join-Path $repo 'apps/prism/build-bridge.ps1') + '"'), '-Port', $Port, '-Root', ('"' + $root + '"'))
} finally { Remove-Item Env:PRISM_BRIDGE_TOKEN -ErrorAction SilentlyContinue }

try {
$up = $false
for ($i = 0; $i -lt 40 -and -not $up; $i++) {
    Start-Sleep -Milliseconds 250
    if ($bridge.HasExited) { throw "The bridge exited with code $($bridge.ExitCode)" }
    try { $up = (Invoke-RestMethod -Uri "http://127.0.0.1:$Port/health" -Headers @{ 'X-Bridge-Token' = $token } -TimeoutSec 1).tokenOk -eq $true } catch { }
}
if (-not $up) { throw 'The bridge did not answer with the launcher token within 10 s' }

$url = 'file:///' + ($page -replace '\\', '/') + $fragment
$browser = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Google\Chrome\Application\chrome.exe") | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $browser) { throw 'No Edge or Chrome found to open ModBuilder.' }
Start-Process -FilePath $browser -ArgumentList ('"' + $url + '"')
Write-Host "ModBuilder is connected to its local helper (PID $($bridge.Id)). Stop the helper with: Stop-Process -Id $($bridge.Id)"
} catch {
    if (-not $bridge.HasExited) { Stop-Process -Id $bridge.Id -ErrorAction SilentlyContinue }
    throw
}
