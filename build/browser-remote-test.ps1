# browser-remote-test.ps1 -- the browser's wire path against an HTTP server we
# did not write in Codex (BROWSER-4).
#
# tools/browser-remote-probe.codex calls resolve-and-load-remote for four
# codex://pages.test/ paths. PageFetcher sends every named host to 10.0.2.2:80,
# which codex-vm opens as 127.0.0.1:80 on this box, so this script serves port
# 80 and refuses to start when anything else holds it (L-SHARED).
#
#   hello             200, Content-Type application/codex: must load
#   body-publisher    200, the BODY carries an X-Codex-Publisher line and the
#                     headers carry none: must load, a body is not a header.
#                     Served with CRLF line ends, which a real server sends
#   header-publisher  200, the HEADERS name a publisher and carry no signature:
#                     must be refused by the signature check
#   missing           404: must answer not found
#
# Usage: browser-remote-test.ps1 [-Kernel <compiler cdx>] [-RunSeconds <n>] [-KeepArtifacts]

param(
    [string]$Kernel = '',
    [int]$RunSeconds = 120,
    [switch]$KeepArtifacts
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo  = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Out   = Join-Path $Repo 'test-output\browser-remote'
$Probe = Join-Path $Repo 'tools\browser-remote-probe.codex'
$Vm    = Join-Path $Repo 'tools\codex-vm.exe'
$Port  = 80
if ($Kernel -eq '') { $Kernel = Join-Path $Repo 'seed\Codex.cdx' }

function Fail($msg) { Write-Host "browser-remote: FAIL -- $msg"; exit 1 }

foreach ($t in @($Vm, $Probe, $Kernel)) { if (-not (Test-Path $t)) { Fail "missing: $t" } }
if (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) {
    Fail "port $Port is already listening on this box; this run would grade another server"
}

New-Item -ItemType Directory -Force $Out | Out-Null
$probeCdx = Join-Path $Out 'browser-remote-probe.cdx'
if (Test-Path $probeCdx) { Remove-Item $probeCdx }
Write-Host "browser-remote: compiling tools/browser-remote-probe.codex with $Kernel"
& (Join-Path $Repo 'build\compile.ps1') -Src $Probe -Out $probeCdx -Log (Join-Path $Out 'probe.log') -Kernel $Kernel | Out-Null
if (-not (Test-Path $probeCdx)) { Fail "the probe did not compile -- see $Out\probe.log" }

$lf = "`n"
$pages = @{
    '/hello'            = @{ Status = '200 OK'; Headers = @('Content-Type: application/codex')
                             Body = "Chapter: Remote Hello$lf  page : [Display] Widget$lf  page (vw) (vh) = widget-label `"title`" `"hello over the wire`"$lf" }
    '/body-publisher'   = @{ Status = '200 OK'; Headers = @('Content-Type: application/codex')
                             Body = "X-Codex-Publisher: ed25519:AAAAAAAA`r`nChapter: Body Publisher`r`n  page : [Display] Widget`r`n  page (vw) (vh) = widget-label `"title`" `"body`"`r`n" }
    '/header-publisher' = @{ Status = '200 OK'; Headers = @('Content-Type: application/codex', 'X-Codex-Publisher: ed25519:AAAAAAAA')
                             Body = "Chapter: Header Publisher$lf  page : [Display] Widget$lf  page (vw) (vh) = widget-label `"title`" `"header`"$lf" }
}

$reqLog = Join-Path $Out 'requests.txt'
Set-Content -Path $reqLog -Value @() -Encoding ascii
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
$listener.Start()
$server = Start-ThreadJob -ArgumentList $listener, $pages, $reqLog -ScriptBlock {
    param($listener, $pages, $reqLog)
    while ($true) {
        try { $c = $listener.AcceptTcpClient() } catch { break }
        try {
            $s = $c.GetStream()
            $s.ReadTimeout = 10000
            $buf = [System.Collections.Generic.List[byte]]::new()
            $one = New-Object byte[] 1
            while ($true) {
                $n = $s.Read($one, 0, 1)
                if ($n -le 0) { break }
                $buf.Add($one[0])
                $k = $buf.Count
                if ($k -ge 4 -and $buf[$k-4] -eq 13 -and $buf[$k-3] -eq 10 -and $buf[$k-2] -eq 13 -and $buf[$k-1] -eq 10) { break }
            }
            $req = [System.Text.Encoding]::ASCII.GetString($buf.ToArray())
            Add-Content -Path $reqLog -Value ($req -replace "`r", '') -Encoding ascii
            $path = (($req -split "`r`n")[0] -split ' ')[1]
            $page = $pages[$path]
            if ($page) {
                $body = [System.Text.Encoding]::ASCII.GetBytes($page.Body)
                $head = "HTTP/1.1 $($page.Status)`r`n" + (($page.Headers + @("Content-Length: $($body.Length)", 'Connection: close')) -join "`r`n") + "`r`n`r`n"
            } else {
                $body = [System.Text.Encoding]::ASCII.GetBytes('not found')
                $head = "HTTP/1.1 404 Not Found`r`nContent-Length: $($body.Length)`r`nConnection: close`r`n`r`n"
            }
            $hb = [System.Text.Encoding]::ASCII.GetBytes($head)
            $s.Write($hb, 0, $hb.Length)
            $s.Write($body, 0, $body.Length)
            $s.Flush()
        } catch { } finally { $c.Close() }
    }
}

$vmOut = Join-Path $Out 'guest.out'
$vmErr = Join-Path $Out 'guest.err'
try {
    $vm = Start-Process -FilePath $Vm -ArgumentList '-kernel', $probeCdx, '-headless', '-mem', '3072', '-output', $vmOut `
        -PassThru -WindowStyle Hidden -RedirectStandardError $vmErr
    if (-not $vm.WaitForExit($RunSeconds * 1000)) { Stop-Process -Id $vm.Id -Force -ErrorAction SilentlyContinue }
}
finally {
    $listener.Stop()
    Stop-Job $server -ErrorAction SilentlyContinue
    Remove-Job $server -Force -ErrorAction SilentlyContinue
}

if (-not (Test-Path $vmOut)) { Fail "guest produced no output at all" }
$guest = @(Get-Content $vmOut | Where-Object { $_ -like 'remote *' })
Write-Host "browser-remote: guest said --"
$guest | ForEach-Object { Write-Host "  $_" }
$requests = @(Get-Content $reqLog | Where-Object { $_ -match '^GET ' })
Write-Host "browser-remote: server saw $($requests.Count) request(s): $(($requests | ForEach-Object { ($_ -split ' ')[1] }) -join ' ')"

$expected = @(
    'remote hello code=1 title=Remote Hello',
    'remote body-publisher code=1 title=Body Publisher',
    'remote header-publisher code=3 title=Publisher signature verification failed',
    'remote missing code=3 title=Page not found: pages.test/missing',
    'remote done'
)
$problems = @()
for ($i = 0; $i -lt $expected.Count; $i++) {
    $got = if ($i -lt $guest.Count) { $guest[$i] } else { '(nothing)' }
    if ($got -ne $expected[$i]) { $problems += "line $($i + 1): expected '$($expected[$i])', got '$got'" }
}
$hosts = @(Get-Content $reqLog | Where-Object { $_ -match '^Host: ' } | Select-Object -Unique)
if ($hosts.Count -ne 1 -or $hosts[0] -ne 'Host: pages.test') { $problems += "the requests did not name the host pages.test: $($hosts -join ' | ')" }

if (-not $KeepArtifacts) { Remove-Item $vmErr -Force -ErrorAction SilentlyContinue }
if ($problems.Count -gt 0) {
    Write-Host ''
    foreach ($p in $problems) { Write-Host "  $p" }
    Fail "$($problems.Count) problem(s)"
}
Write-Host ''
Write-Host 'browser-remote: OK -- a named codex:// host loads over the wire, headers are read from the headers, 404 is not found'
exit 0
