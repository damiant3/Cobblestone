# Per-user creations browser demo. Boots ExplorerServer (reads explorer.db.img
# off the attached disk), bridges HTTP :8888 -> framed TCP :9100, serves
# creations.html at / and proxies /api/* (auth, save, mine, delete, catalog)
# to the guest. No serial. Build the server first:
#   pwsh build\compile.ps1 -Src apps\explorer\ExplorerServer.codex -Out build-output\explorer-server.cdx -Log build-output\exsrv.log
#   pwsh apps\explorer\build-explorer-db.ps1
param([int]$HttpPort = 8888, [int]$MemMB = 2048,
      [string]$Cdx = "build-output\explorer-server.cdx",
      [string]$Disk = "build-output\explorer.db.img",
      [switch]$FreshDisk)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$TcpPort = 9100
$Repo  = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$vmBin = Join-Path $Repo 'tools\codex-vm.exe'
$CdxPath = Join-Path $Repo $Cdx
$Page = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'creations.html'))

$DiskPath = Join-Path $Repo $Disk
if ($FreshDisk) {
  $work = Join-Path $Repo 'build-output\creations-demo.img'
  Copy-Item -Force $DiskPath $work; $DiskPath = $work
  Write-Host "[demo] fresh disk copy: $work"
}

function Send-Frame($stream, [int]$tag, [byte[]]$body) {
  $hdr = [BitConverter]::GetBytes([int](1 + $body.Length))
  $stream.Write($hdr, 0, 4); $stream.WriteByte([byte]$tag)
  if ($body.Length -gt 0) { $stream.Write($body, 0, $body.Length) }; $stream.Flush()
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

$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $TcpPort)
$listener.Start()
Write-Host "[demo] TCP bridge on $TcpPort; booting server CDX (disk: $DiskPath)..."
$errFile = [System.IO.Path]::GetTempFileName()
$proc = Start-Process -FilePath $vmBin -ArgumentList @('-kernel',$CdxPath,'-disk',$DiskPath,'-mem',$MemMB,'-headless') -PassThru -WindowStyle Hidden -RedirectStandardError $errFile
$deadline = [DateTime]::UtcNow.AddSeconds(45)
while (-not $listener.Pending()) {
  if ($proc.HasExited) { Write-Host "[demo] VM exited early"; Get-Content $errFile -Raw | Out-Host; exit 4 }
  if ([DateTime]::UtcNow -gt $deadline) { Write-Host "[demo] guest connect timeout"; exit 5 }
  Start-Sleep -Milliseconds 100
}
$client = $listener.AcceptTcpClient(); $client.NoDelay = $true; $client.ReceiveTimeout = 30000
$guest = $client.GetStream(); $listener.Stop()
Write-Host "[demo] guest connected."

$http = [System.Net.HttpListener]::new()
$http.Prefixes.Add("http://localhost:$HttpPort/")
$http.Start()
Write-Host "[demo] OPEN  http://localhost:$HttpPort/   (Ctrl+C to stop)"
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
        $b = [System.Text.Encoding]::UTF8.GetBytes($Page)
        $resp.StatusCode = 200; $resp.ContentType = 'text/html; charset=utf-8'; $resp.ContentLength64 = $b.Length
        $resp.OutputStream.Write($b, 0, $b.Length)
      }
    } catch { $resp.StatusCode = 500 } finally { $resp.Close() }
  }
} finally {
  if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
  try { $http.Stop() } catch {}; Remove-Item -Force $errFile -ErrorAction SilentlyContinue
}
