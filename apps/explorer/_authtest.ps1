# Auth flow probe for the Accounts std-include (AuthDemo server).
# Boots auth-demo.cdx (guest connects OUT to host:9100), then exercises
# register -> login -> me(token) -> app route. Framed [len:u32 LE][tag:u8][body].
param([string]$Cdx = "build-output\auth-demo.cdx", [int]$Port = 9100, [int]$MemMB = 2048)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$vmBin = Join-Path $Repo 'tools\codex-vm.exe'

function Send-Frame($stream, [int]$tag, [string]$body) {
  $b = [System.Text.Encoding]::ASCII.GetBytes($body)
  $hdr = [BitConverter]::GetBytes([int](1 + $b.Length))
  $stream.Write($hdr, 0, 4); $stream.WriteByte([byte]$tag)
  if ($b.Length -gt 0) { $stream.Write($b, 0, $b.Length) }; $stream.Flush()
}
function Recv-Frame($stream) {
  $hdr = New-Object byte[] 4; $r = 0
  while ($r -lt 4) { $n = $stream.Read($hdr, $r, 4 - $r); if ($n -le 0) { return $null }; $r += $n }
  $len = [BitConverter]::ToInt32($hdr, 0)
  if ($len -lt 1 -or $len -gt 1048576) { return "BADLEN" }
  $buf = New-Object byte[] $len; $r = 0
  while ($r -lt $len) { $n = $stream.Read($buf, $r, $len - $r); if ($n -le 0) { break }; $r += $n }
  [System.Text.Encoding]::ASCII.GetString($buf, 1, $len - 1)
}
function Req($stream, $path) { Send-Frame $stream 1 "GET $path"; Recv-Frame $stream }

$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
$listener.Start(); Write-Host "[auth] listening on $Port; booting AuthDemo..."
$errFile = [System.IO.Path]::GetTempFileName()
$proc = Start-Process -FilePath $vmBin -ArgumentList @('-kernel',$Cdx,'-mem',$MemMB,'-headless') -PassThru -WindowStyle Hidden -RedirectStandardError $errFile
try {
  $deadline = [DateTime]::UtcNow.AddSeconds(45)
  while (-not $listener.Pending()) {
    if ($proc.HasExited) { Write-Host "[auth] VM exited early"; Get-Content $errFile -Raw | Out-Host; exit 4 }
    if ([DateTime]::UtcNow -gt $deadline) { Write-Host "[auth] timeout"; exit 5 }
    Start-Sleep -Milliseconds 100
  }
  $client = $listener.AcceptTcpClient(); $client.NoDelay = $true; $client.ReceiveTimeout = 60000
  $st = $client.GetStream(); $listener.Stop(); Write-Host "[auth] guest connected`n"
  Write-Host "anon /          -> $(Req $st '/')"
  Write-Host "register alice  -> $(Req $st '/api/auth/register?u=alice&d=Alice&p=secret')"
  Write-Host "register dup    -> $(Req $st '/api/auth/register?u=alice&d=A&p=x')"
  $login = Req $st '/api/auth/login?u=alice&p=secret'
  Write-Host "login alice     -> $login"
  $tok = [regex]::Match($login, '"token":"([0-9a-f]+)"').Groups[1].Value
  Write-Host "login badpass   -> $(Req $st '/api/auth/login?u=alice&p=wrong')"
  Write-Host "me (token)      -> $(Req $st "/api/auth/me?t=$tok")"
  Write-Host "me (no token)   -> $(Req $st '/api/auth/me?t=')"
  Write-Host "app route(auth) -> $(Req $st "/?t=$tok")"
  Write-Host "logout          -> $(Req $st "/api/auth/logout?t=$tok")"
  Write-Host "me after logout -> $(Req $st "/api/auth/me?t=$tok")"
  $client.Close()
} finally {
  if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
  try { $listener.Stop() } catch {}; Remove-Item -Force $errFile -ErrorAction SilentlyContinue
}
