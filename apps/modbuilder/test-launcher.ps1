[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$launcherCapture = @{ helper = $null; url = '' }
function Start-Process {
    [CmdletBinding()]
    param([string]$FilePath, [string[]]$ArgumentList, [switch]$PassThru, [string]$WindowStyle)
    if ([IO.Path]::GetFileName($FilePath) -in @('msedge.exe','chrome.exe')) {
        $launcherCapture.url = $ArgumentList[0].Trim('"')
        return
    }
    $launcherCapture.helper = Microsoft.PowerShell.Management\Start-Process @PSBoundParameters
    return $launcherCapture.helper
}
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
$listener.Start(); $testPort = $listener.LocalEndpoint.Port; $listener.Stop()
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('modbuilder-launcher-' + [guid]::NewGuid().ToString('N') + '.json')
try {
    [IO.File]::WriteAllText($fixture,'{"version":1,"id":"launcher-test","kind":"unity"}',[Text.UTF8Encoding]::new($false))
    & (Join-Path $PSScriptRoot 'run.ps1') -Workspace -Preset hud -Port $testPort -Profile $fixture
    $url = [uri]$launcherCapture.url
    if ($url.Scheme -ne 'file' -or -not $url.LocalPath.EndsWith('workspace.html')) { throw 'Launcher did not open the disk workspace' }
    $parts = @{}
    foreach ($pair in $url.Fragment.TrimStart('#').Split('&')) { $kv=$pair.Split('=',2); $parts[$kv[0]]=[uri]::UnescapeDataString($kv[1]) }
    if ($parts.preset -ne 'hud' -or ($parts.profile | ConvertFrom-Json).id -ne 'launcher-test') { throw 'Launcher preset/profile handoff differs' }
    if ($parts.bridgeUrl -ne "http://127.0.0.1:$testPort") { throw 'Launcher discarded the selected helper port' }
    $health = Invoke-RestMethod -Uri ($parts.bridgeUrl + '/health') -Headers @{ 'X-Bridge-Token' = $parts.bridge } -TimeoutSec 2
    if (-not $health.tokenOk) { throw 'The real helper rejected the launcher token' }
    'PASS workspace launcher, preset/profile handoff, selected port and authenticated real helper'
} finally {
    if ($launcherCapture.helper -and -not $launcherCapture.helper.HasExited) { Stop-Process -Id $launcherCapture.helper.Id -ErrorAction SilentlyContinue }
    Remove-Item -LiteralPath $fixture -Force -ErrorAction SilentlyContinue
}
