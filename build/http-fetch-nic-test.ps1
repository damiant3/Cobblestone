# http-fetch-nic-test.ps1 -- HttpFetch's plain `fetch` over the guest NIC against an HTTP server we did not write in Codex
# (CORE-22, L-UNCALLED). No test in codex/test reaches http-recv-rx: it needs a host server, which test-run.ps1 does not
# start.
#
# The guest probe (written below) calls `fetch "http://10.0.2.2:8080/<path>"`; codex-vm's NAT opens 10.0.2.2 as the
# host and `-natmap 8080:<port>` sends it to this script's listener on a port derived from the workspace, which refuses
# to start when anything else holds it (L-SHARED). Each body is ASCII; the guest prints its length, its first and last
# 16 characters and a position-weighted sum of its code points, and the script computes the same from the bytes it
# served. /big spans dozens of frames, so it grades the receive buffer's growth and the header scan across frames.
#
# Usage: http-fetch-nic-test.ps1 [-Kernel <compiler cdx>] [-RunSeconds <n>]

param(
    [string]$Kernel = '',
    [int]$RunSeconds = 180
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Out  = Join-Path $Repo 'test-output\http-fetch-nic'
$Vm   = Join-Path $Repo 'tools\codex-vm.exe'
if ($Kernel -eq '') { $Kernel = Join-Path $Repo 'seed\Codex.cdx' }
$sum = 0; foreach ($ch in $Repo.ToCharArray()) { $sum = ($sum * 31 + [int]$ch) % 400 }
$Port = 19500 + $sum

function Fail($msg) { Write-Host "http-fetch-nic: FAIL -- $msg"; exit 1 }

foreach ($t in @($Vm, $Kernel)) { if (-not (Test-Path $t)) { Fail "missing: $t" } }
if (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) {
    Fail "port $Port is already listening on this box; this run would grade another server"
}
New-Item -ItemType Directory -Force $Out | Out-Null

function Body([int]$n) { $sb = [System.Text.StringBuilder]::new($n); for ($i = 0; $i -lt $n; $i++) { [void]$sb.Append([char](97 + (($i * 7) % 26))) }; $sb.ToString() }
function Digest([string]$b) {
    $s = [long]0; for ($i = 0; $i -lt $b.Length; $i++) { $s = ($s + ([long]$i + 1) * [int]$b[$i]) % 1000000007 }
    $head = $b.Substring(0, [Math]::Min(16, $b.Length)); $tail = $b.Substring([Math]::Max(0, $b.Length - 16))
    "len=$($b.Length) head=$head tail=$tail sum=$s"
}
$pages = [ordered]@{ '/small' = (Body 2000); '/big' = (Body 60000) }

$probe = @'
Chapter: HttpFetchNicProbe
  cites Net chapter HttpFetch
  cites Foreword chapter Console
  cites Foreword chapter CCE

Section: Probe

  digest-loop : Text, Integer, Integer -> Integer
  digest-loop (t) (i) (acc) =
    if i >= text-length t then acc
    else digest-loop t (i + 1) ((acc + (i + 1) * to-unicode (char-code-at t i)) - ((acc + (i + 1) * to-unicode (char-code-at t i)) / 1000000007) * 1000000007)

  ends : Text -> Text
  ends (t) =
    let n = text-length t
    in "len=" & show n & " head=" & substring t 0 (if n < 16 then n else 16) & " tail=" & substring t (if n < 16 then 0 else n - 16) (if n < 16 then n else 16) & " sum=" & show (digest-loop t 0 0)

  report : Text, Result Text Text -> Text
  report (name) (r) = when r
    is Ok (body) -> "fetch " & name & " " & ends body
    is Err (e) -> "fetch " & name & " error " & e

  opening : [Console, Network.Read, Network.Write] Nothing = act
    small <- fetch "http://10.0.2.2:8080/small"
    print-line-uni (report "small" small)
    big <- fetch "http://10.0.2.2:8080/big"
    print-line-uni (report "big" big)
  end

Page 1
'@
$probeSrc = Join-Path $Out 'http-fetch-nic-probe.codex'
[IO.File]::WriteAllText($probeSrc, $probe.Replace("`r`n", "`n").Replace("`n", "`r`n"), [Text.UTF8Encoding]::new($false))
$probeCdx = Join-Path $Out 'http-fetch-nic-probe.cdx'
if (Test-Path $probeCdx) { Remove-Item $probeCdx }
Write-Host "http-fetch-nic: compiling the probe with $Kernel"
& (Join-Path $Repo 'build\compile.ps1') -Src $probeSrc -Out $probeCdx -Log (Join-Path $Out 'probe.log') -Kernel $Kernel | Out-Null
if (-not (Test-Path $probeCdx)) { Fail "the probe did not compile -- see $Out\probe.log" }

$reqLog = Join-Path $Out 'requests.txt'
Set-Content -Path $reqLog -Value @() -Encoding ascii
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
$listener.Start()
$server = Start-ThreadJob -ArgumentList $listener, $pages, $reqLog -ScriptBlock {
    param($listener, $pages, $reqLog)
    while ($true) {
        try { $c = $listener.AcceptTcpClient() } catch { break }
        try {
            $s = $c.GetStream(); $s.ReadTimeout = 10000
            $buf = [System.Collections.Generic.List[byte]]::new(); $one = New-Object byte[] 1
            while ($true) {
                $n = $s.Read($one, 0, 1); if ($n -le 0) { break }
                $buf.Add($one[0]); $k = $buf.Count
                if ($k -ge 4 -and $buf[$k-4] -eq 13 -and $buf[$k-3] -eq 10 -and $buf[$k-2] -eq 13 -and $buf[$k-1] -eq 10) { break }
            }
            $req = [System.Text.Encoding]::ASCII.GetString($buf.ToArray())
            Add-Content -Path $reqLog -Value (($req -split "`r`n")[0]) -Encoding ascii
            $path = (($req -split "`r`n")[0] -split ' ')[1]
            $text = if ($pages.Contains($path)) { $pages[$path] } else { 'not found' }
            $body = [System.Text.Encoding]::ASCII.GetBytes($text)
            $status = if ($pages.Contains($path)) { '200 OK' } else { '404 Not Found' }
            $hb = [System.Text.Encoding]::ASCII.GetBytes("HTTP/1.1 $status`r`nContent-Type: text/plain`r`nContent-Length: $($body.Length)`r`nConnection: close`r`n`r`n")
            $s.Write($hb, 0, $hb.Length); $s.Write($body, 0, $body.Length); $s.Flush()
        } catch { } finally { $c.Close() }
    }
}

$vmOut = Join-Path $Out 'guest.out'
$vmErr = Join-Path $Out 'guest.err'
try {
    $vm = Start-Process -FilePath $Vm -ArgumentList '-kernel', $probeCdx, '-headless', '-mem', '3072', '-output', $vmOut, '-natmap', "8080:$Port" `
        -PassThru -WindowStyle Hidden -RedirectStandardError $vmErr
    if (-not $vm.WaitForExit($RunSeconds * 1000)) { Stop-Process -Id $vm.Id -Force -ErrorAction SilentlyContinue }
}
finally {
    $listener.Stop()
    Stop-Job $server -ErrorAction SilentlyContinue
    Remove-Job $server -Force -ErrorAction SilentlyContinue
}

if (-not (Test-Path $vmOut)) { Fail "guest produced no output at all" }
$guest = @(Get-Content $vmOut | Where-Object { $_ -like 'fetch *' })
Write-Host "http-fetch-nic: guest said --"
$guest | ForEach-Object { Write-Host "  $_" }
$requests = @(Get-Content $reqLog | Where-Object { $_ -match '^GET ' })
Write-Host "http-fetch-nic: server saw $($requests.Count) request(s): $(($requests | ForEach-Object { ($_ -split ' ')[1] }) -join ' ')"

$problems = @()
foreach ($name in @('small', 'big')) {
    $want = "fetch $name " + (Digest $pages["/$name"])
    $got = @($guest | Where-Object { $_ -like "fetch $name *" })
    if ($got.Count -ne 1) { $problems += "no single line for $name (got $($got.Count))" }
    elseif ($got[0] -cne $want) { $problems += "$name differs: got '$($got[0])', want '$want'" }
}
if ($requests.Count -ne 2) { $problems += "the server saw $($requests.Count) requests, expected 2" }
if ($problems.Count -gt 0) { foreach ($p in $problems) { Write-Host "  $p" }; Fail "$($problems.Count) problem(s)" }
Write-Host "http-fetch-nic: OK -- both bodies arrived byte for byte (2,000 and 60,000 bytes) through http-recv-rx over the NIC"
exit 0
