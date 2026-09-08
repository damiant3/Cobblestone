# DB-backed designer demo. Boots ExplorerServer (reads explorer.db.img off the
# attached disk), bridges HTTP :8888 -> framed TCP :9100, and serves the three
# COMPILED designer pages by route: / and /setting -> setting.html, /character,
# /item. Each page fetches /api/d/<table> over AJAX; the bridge proxies /api/* to
# the guest. No serial. Build the pages first with codex\plugs\html\run.ps1.
param([int]$HttpPort = 8888, [int]$MemMB = 2048,
      [string]$Cdx = "build-output\explorer-server.cdx",
      [string]$Disk = "build-output\explorer.db.img")
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$TcpPort = 9100   # the server connects OUT to host:9100 (hardcoded in WebServer); do not change
$Repo  = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$vmBin = Join-Path $Repo 'tools\codex-vm.exe'
$CdxPath = Join-Path $Repo $Cdx

$pageFiles = @{ 'setting' = 'build-output\setting.html'; 'character' = 'build-output\character.html'; 'item' = 'build-output\item.html'; 'excalibur' = 'build-output\excalibur.html'; 'mine' = 'build-output\creations.html' }
$pageSrc = @{ 'setting' = 'SettingDesignerApp'; 'character' = 'CharDesignerApp'; 'item' = 'ItemDesignerApp'; 'excalibur' = 'ExcaliburSlice'; 'mine' = 'CreationsApp' }
$pages = @{}
foreach ($k in $pageFiles.Keys) {
  $p = Join-Path $Repo $pageFiles[$k]
  if (-not (Test-Path $p)) {
    Write-Host "[demo] skip $k (not built): pwsh codex\plugs\html\run.ps1 -Src apps\explorer\$($pageSrc[$k]).codex -Out $($pageFiles[$k])"
    continue
  }
  $pages[$k] = [System.IO.File]::ReadAllText($p)
}
if ($pages.Count -eq 0) { Write-Host "[demo] no pages built; nothing to serve"; exit 2 }
Write-Host "[demo] serving pages: $($pages.Keys -join ', ')"

# The My-Creations SPA is now compiled Codex (CreationsApp.codex -> creations.html).
# It loads from build-output alongside the designer pages; the hand-JS creations.html
# is retired. AuthClient handles auth natively; the bridge injection bar is kept for
# the designer pages only (they don't cite AuthClient).

# Inject an account-aware "Save to My Creations" bar into each designer page,
# at the bridge layer (no plug/seed change). It reads the shared token, grabs the
# current selection from #hero-meta, and posts it to /api/save.
$inject = @'
<div id="cxbar" style="position:fixed;top:10px;right:12px;z-index:99999;font:13px system-ui;background:#16161bcc;border:1px solid #33333d;border-radius:8px;padding:6px 10px;backdrop-filter:blur(4px)">
  <span id="cxwho" style="color:#9a8c66;margin-right:8px"></span>
  <button id="cxsaveBtn" style="font:inherit;font-weight:600;background:#e9c46a;color:#1a1a1a;border:0;border-radius:6px;padding:5px 12px;cursor:pointer">Save to My Creations</button>
  <a href="/mine" style="color:#e9c46a;margin-left:8px;text-decoration:none">My Creations &rarr;</a>
</div>
<script>(function(){
  var tk=function(){return localStorage.getItem('cx_tok')||'';};
  var who=document.getElementById('cxwho');
  fetch('/api/auth/me?t='+encodeURIComponent(tk())).then(function(r){return r.json();}).then(function(m){who.textContent=(m&&m.handle)?('@'+m.handle):'not signed in';}).catch(function(){});
  // Reopen a saved creation: /<kind>?prompt=<data> pre-fills the prompt box.
  try{var sp=new URLSearchParams(location.search).get('prompt');if(sp){var fill=function(){var pt=document.getElementById('prompt');if(pt){pt.value=sp;pt.focus();}else setTimeout(fill,150);};fill();}}catch(e){}
  document.getElementById('cxsaveBtn').onclick=function(){
    if(!tk()){alert('Log in on the My Creations page first ( /mine )');return;}
    var hm=document.getElementById('hero-meta');var meta=hm?(hm.textContent||'').trim():'';
    var kind=(location.pathname.indexOf('character')>=0)?'character':(location.pathname.indexOf('item')>=0)?'item':'setting';
    var name=((meta.split(' - ')[0]||'').trim()||kind).slice(0,60);
    var data=(meta||'(no selection)').slice(0,180);
    fetch('/api/save?t='+encodeURIComponent(tk())+'&kind='+kind+'&name='+encodeURIComponent(name)+'&data='+encodeURIComponent(data)).then(function(r){return r.json();}).then(function(j){alert(j.ok?('Saved "'+name+'"'):('Save failed: '+(j.error||'?')));}).catch(function(e){alert('Save error');});
  };
})();</script>
'@
foreach ($k in @($pages.Keys)) { if ($k -ne 'mine') { $pages[$k] = $pages[$k] -replace '</body>', ($inject + '</body>') } }

function Send-Frame($stream, [int]$tag, [byte[]]$body) {
  $hdr = [BitConverter]::GetBytes([int](1 + $body.Length))
  $stream.Write($hdr, 0, 4); $stream.WriteByte([byte]$tag)
  if ($body.Length -gt 0) { $stream.Write($body, 0, $body.Length) }
  $stream.Flush()
}
function Recv-Frame($stream) {
  $hdr = New-Object byte[] 4; $r = 0
  while ($r -lt 4) { $n = $stream.Read($hdr, $r, 4 - $r); if ($n -le 0) { return $null }; $r += $n }
  $len = [BitConverter]::ToInt32($hdr, 0)
  if ($len -lt 1 -or $len -gt 8388608) { return $null }
  $buf = New-Object byte[] $len; $r = 0
  while ($r -lt $len) { $n = $stream.Read($buf, $r, $len - $r); if ($n -le 0) { break }; $r += $n }
  [System.Text.Encoding]::ASCII.GetString($buf, 1, $len - 1)
}
function Query-Guest($stream, [string]$path) {
  Send-Frame $stream 1 ([System.Text.Encoding]::ASCII.GetBytes("GET $path"))
  $resp = Recv-Frame $stream
  if (-not $resp) { return @{ status = 502; body = '{"error":"no response"}' } }
  $i = $resp.IndexOf(' '); $j = $resp.IndexOf(' ', $i + 1)
  if ($i -lt 0 -or $j -lt 0) { return @{ status = 200; body = $resp } }
  return @{ status = [int]$resp.Substring(0, $i); body = $resp.Substring($j + 1) }
}

# --- boot the guest ---
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $TcpPort)
$listener.Start()
Write-Host "[demo] TCP bridge listening on $TcpPort; booting server CDX..."
$errFile = [System.IO.Path]::GetTempFileName()
$vmArgs = @('-kernel', $CdxPath, '-mem', $MemMB, '-headless')
if ($Disk -ne "") { $dp = Join-Path $Repo $Disk; if (Test-Path $dp) { $vmArgs += @('-disk', $dp); Write-Host "[demo] disk: $dp" } }
$proc = Start-Process -FilePath $vmBin -ArgumentList $vmArgs -PassThru -WindowStyle Hidden -RedirectStandardError $errFile
$deadline = [DateTime]::UtcNow.AddSeconds(45)
while (-not $listener.Pending()) {
  if ($proc.HasExited) { Write-Host "[demo] VM exited early"; Get-Content $errFile -Raw | Out-Host; exit 4 }
  if ([DateTime]::UtcNow -gt $deadline) { Write-Host "[demo] guest connect timeout"; exit 5 }
  Start-Sleep -Milliseconds 100
}
$client = $listener.AcceptTcpClient(); $client.NoDelay = $true; $client.ReceiveTimeout = 30000
$guest = $client.GetStream(); $listener.Stop()
Write-Host "[demo] guest connected."

# --- HTTP front ---
$http = [System.Net.HttpListener]::new()
$http.Prefixes.Add("http://localhost:$HttpPort/")
$http.Start()
Write-Host "[demo] OPEN  http://localhost:$HttpPort/   (setting | /character | /item | /excalibur | /mine ; Ctrl+C to stop)"
try {
  while ($http.IsListening) {
    $ctx = $http.GetContext(); $resp = $ctx.Response
    try {
      $path = $ctx.Request.Url.PathAndQuery
      if ($path -like '/api/*') {
        $q = Query-Guest $guest $path
        $b = [System.Text.Encoding]::UTF8.GetBytes($q.body)
        $resp.StatusCode = $q.status; $resp.ContentType = 'application/json; charset=utf-8'
        $resp.Headers.Add('Access-Control-Allow-Origin','*'); $resp.ContentLength64 = $b.Length
        $resp.OutputStream.Write($b, 0, $b.Length)
      } else {
        $key = switch -Wildcard ($path) { '/mine*' { 'mine' } '/character*' { 'character' } '/item*' { 'item' } '/excalibur*' { 'excalibur' } default { 'setting' } }
        $html = if ($pages.ContainsKey($key)) { $pages[$key] } else { "<!doctype html><meta charset=utf-8><body style='font:14px system-ui;background:#0a0a0a;color:#ccc;padding:40px'>Page '<b>$key</b>' is not built. Run: <code>pwsh codex\plugs\html\run.ps1 -Src apps\explorer\$($pageSrc[$key]).codex -Out $($pageFiles[$key])</code></body>" }
        $b = [System.Text.Encoding]::UTF8.GetBytes($html)
        $resp.StatusCode = 200; $resp.ContentType = 'text/html; charset=utf-8'; $resp.ContentLength64 = $b.Length
        $resp.OutputStream.Write($b, 0, $b.Length)
      }
    } catch { $resp.StatusCode = 500 } finally { $resp.Close() }
  }
} finally {
  if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
  try { $http.Stop() } catch {}; Remove-Item -Force $errFile -ErrorAction SilentlyContinue
}
