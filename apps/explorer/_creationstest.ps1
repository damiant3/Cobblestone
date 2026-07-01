# Per-user creations probe for ExplorerServer (auth-serve + disk-backed
# creations). Boots explorer-server.cdx WITH -disk, exercises
# register/login/save/mine + user isolation, then RESTARTS the VM on the
# same disk image to prove durability. Framed [len:u32 LE][tag:u8][body].
param(
  [string]$Cdx  = "build-output\explorer-server.cdx",
  [string]$Disk = "build-output\explorer.db.img",
  [int]$Port = 9100, [int]$MemMB = 2048)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Repo  = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$vmBin = Join-Path $Repo 'tools\codex-vm.exe'
$CdxA  = Join-Path $Repo $Cdx
$DiskSrc = Join-Path $Repo $Disk
# Work on a fresh copy so the canonical image stays pristine and reruns start clean.
$DiskA = Join-Path $Repo "build-output\explorer-creations-test.img"
Copy-Item -Force $DiskSrc $DiskA

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

# Boot the VM, wait for the guest's outbound connection, return (proc, stream, client).
function Boot-Connect {
  $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
  $listener.Start()
  $errFile = [System.IO.Path]::GetTempFileName()
  $proc = Start-Process -FilePath $vmBin -ArgumentList @('-kernel',$CdxA,'-disk',$DiskA,'-mem',$MemMB,'-headless') -PassThru -WindowStyle Hidden -RedirectStandardError $errFile
  $deadline = [DateTime]::UtcNow.AddSeconds(45)
  while (-not $listener.Pending()) {
    if ($proc.HasExited) { Write-Host "[cr] VM exited early"; Get-Content $errFile -Raw | Out-Host; $listener.Stop(); exit 4 }
    if ([DateTime]::UtcNow -gt $deadline) { Write-Host "[cr] timeout"; $listener.Stop(); exit 5 }
    Start-Sleep -Milliseconds 100
  }
  $client = $listener.AcceptTcpClient(); $client.NoDelay = $true; $client.ReceiveTimeout = 60000
  $st = $client.GetStream(); $listener.Stop()
  @{ proc = $proc; stream = $st; client = $client; err = $errFile }
}
function Shutdown($ctx) {
  try { $ctx.client.Close() } catch {}
  if (-not $ctx.proc.HasExited) { Stop-Process -Id $ctx.proc.Id -Force -ErrorAction SilentlyContinue }
  $ctx.proc.WaitForExit(5000) | Out-Null
  Remove-Item -Force $ctx.err -ErrorAction SilentlyContinue
}

Write-Host "=== BOOT 1 (register, save, isolation) ==="
$c = Boot-Connect; $st = $c.stream
Write-Host "register bob    -> $(Req $st '/api/auth/register?u=bob&d=Bob&p=pw1')"
$lb = Req $st '/api/auth/login?u=bob&p=pw1'; Write-Host "login bob       -> $lb"
$tb = [regex]::Match($lb, '"token":"([0-9a-f]+)"').Groups[1].Value
Write-Host "bob mine (empty)-> $(Req $st "/api/mine?t=$tb")"
Write-Host "bob save A      -> $(Req $st "/api/save?t=$tb&kind=setting&name=Forest1&data=biome_forest_seed42")"
Write-Host "bob save B      -> $(Req $st "/api/save?t=$tb&kind=item&name=Sword9&data=blade_mythic_s7")"
Write-Host "bob mine (2)    -> $(Req $st "/api/mine?t=$tb")"
Write-Host "register alice  -> $(Req $st '/api/auth/register?u=alice&d=Alice&p=pw2')"
$la = Req $st '/api/auth/login?u=alice&p=pw2'; $ta = [regex]::Match($la, '"token":"([0-9a-f]+)"').Groups[1].Value
Write-Host "alice mine(0)   -> $(Req $st "/api/mine?t=$ta")"
Write-Host "alice save      -> $(Req $st "/api/save?t=$ta&kind=character&name=Mage3&data=elf_wizard_lvl5")"
Write-Host "alice mine(1)   -> $(Req $st "/api/mine?t=$ta")"
Write-Host "public catalog  -> $(($(Req $st '/api/biomes')).Substring(0,60))..."
Shutdown $c

Write-Host "`n=== BOOT 2 (same disk: durability) ==="
$c2 = Boot-Connect; $st2 = $c2.stream
$lb2 = Req $st2 '/api/auth/login?u=bob&p=pw1'; Write-Host "login bob again -> $lb2"
$tb2 = [regex]::Match($lb2, '"token":"([0-9a-f]+)"').Groups[1].Value
Write-Host "bob mine (2!)   -> $(Req $st2 "/api/mine?t=$tb2")"
$la2 = Req $st2 '/api/auth/login?u=alice&p=pw2'; $ta2 = [regex]::Match($la2, '"token":"([0-9a-f]+)"').Groups[1].Value
Write-Host "alice mine (1!) -> $(Req $st2 "/api/mine?t=$ta2")"
Shutdown $c2
Write-Host "`n[cr] done"
