[CmdletBinding()]
param([string]$Kernel='seed/Codex.cdx',[Parameter(Mandatory)][string]$OutDir)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Set-Location $repo
$OutDir=[IO.Path]::GetFullPath((Join-Path $repo $OutDir))
if(-not $OutDir.StartsWith((Join-Path $repo 'build-output')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Output must be inside build-output'}
if(Test-Path $OutDir){throw 'Choose a fresh output directory'}
New-Item -ItemType Directory -Path $OutDir|Out-Null
function Admit {if((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory -le 1572864){throw 'RAM refused'}}
function Compile([string]$src,[string]$stem){
 Admit
 & pwsh -NoProfile -File build/compile.ps1 -Src $src -Out "$stem.cdx" -Log "$stem-compile.log" -Kernel $Kernel
 if($LASTEXITCODE -ne 0){throw "Compile failed: $src"}
}
function Guest([string]$cdx,[string]$disk,[string]$stem,[int]$delay=0){
 Admit
 $argv=@('-kernel',('"'+$cdx+'"'),'-disk',('"'+$disk+'"'),'-output',('"'+$stem+'.out"'),'-headless','-mem','3072')
 if($delay){$argv+=@('-gop-width','1024','-gop-height','768','-hid-combo','-hwwatch','0x0fa00000','-hwwatch-log','-screenshot',('"'+$stem+'.bmp"'),'-screenshot-delay',"$delay")}
 $p=Start-Process tools/codex-vm.exe -WindowStyle Hidden -ArgumentList $argv -RedirectStandardError "$stem.err" -PassThru
 "guest PID=$($p.Id) $stem"
 try{if(-not $p.WaitForExit(90000)){throw 'Guest timeout'}; $trace=[IO.File]::ReadAllText("$stem.err"); if($p.ExitCode -ne 1 -or $trace -notmatch 'FINAL: debug_exit_code=0 process_exit=1' -or $trace -match 'CRASH|!EXC=|500 hits'){throw "Guest failed: $stem"}}
 finally{if(-not $p.HasExited){Stop-Process -Id $p.Id -Force};$p.WaitForExit();$p.Dispose()}
}
function Grade([string]$stem,[string[]]$expected){
 $lines=([IO.File]::ReadAllText("$stem.out").Replace("`r",'') -split "`n"|Where-Object {$_ -and $_ -notmatch '^(HEAP|WD|STACK|PM):'}) -join "`n"
 $lines|Write-Host
 if($lines -cne ($expected -join "`n")){throw "Output mismatch: $stem"}
}
$bytes=[IO.File]::ReadAllBytes((Join-Path $repo 'seed/Codex.img'))
$inputHash=(Get-FileHash seed/Codex.img).Hash
$esp=0;$factAt=0;$factBytes=0
for($i=0;$i -lt 4;$i++){
 $o=1024+$i*128;$guid=[Convert]::ToHexString($bytes[$o..($o+15)])
 $first=[long][BitConverter]::ToUInt64($bytes,$o+32);$last=[long][BitConverter]::ToUInt64($bytes,$o+40)
 if($guid -eq '28732AC11FF8D211BA4B00A0C93EC93B'){$esp=$first*512}
 if($guid -eq '111ADEC0C7FA0D4C9E75C0DEC0DE5EED'){$factAt=$first*512;$factBytes=($last-$first+1)*512}
}
if($esp -lt 512 -or $factAt -le $esp -or $factBytes -le 0 -or $factAt+$factBytes -gt $bytes.Length){throw 'Fixture partition bounds invalid'}
[Array]::Clear($bytes,$factAt,$factBytes)
$bps=[int][BitConverter]::ToUInt16($bytes,$esp+11);$spc=[int]$bytes[$esp+13]
$reserved=[int][BitConverter]::ToUInt16($bytes,$esp+14);$nfats=[int]$bytes[$esp+16]
$rootEntries=[int][BitConverter]::ToUInt16($bytes,$esp+17);$fatSectors=[int][BitConverter]::ToUInt16($bytes,$esp+22)
$rootAt=$esp+($reserved+$nfats*$fatSectors)*$bps
$rootSectors=[int][Math]::Ceiling($rootEntries*32.0/$bps)
$dataAt=$rootAt+$rootSectors*$bps
$total=[int][BitConverter]::ToUInt16($bytes,$esp+19);if(-not $total){$total=[int][BitConverter]::ToUInt32($bytes,$esp+32)}
$clusterCount=[int][Math]::Floor(($esp+$total*$bps-$dataAt)/($spc*$bps))
if($bps -ne 512 -or $spc -lt 1 -or $fatSectors -lt 1 -or $rootEntries -lt 1){throw 'Fixture requires FAT16'}
function PutFile([string]$short,[byte[]]$data){
 $slot=-1
 for($i=0;$i -lt $rootEntries;$i++){$at=$rootAt+$i*32;if($bytes[$at] -in @(0,229)){$slot=$at;break}}
 if($slot -lt 0){throw 'No root directory slot'}
 $need=[int][Math]::Ceiling($data.Length/[double]($spc*$bps));$chain=[Collections.Generic.List[int]]::new()
 $limit=[Math]::Min($clusterCount+2,[int]($fatSectors*$bps/2))
 for($c=2;$c -lt $limit -and $chain.Count -lt $need;$c++){if([BitConverter]::ToUInt16($bytes,$esp+$reserved*$bps+$c*2) -eq 0){$chain.Add($c)}}
 if($chain.Count -ne $need){throw 'Insufficient fixture space'}
 for($j=0;$j -lt $chain.Count;$j++){
  $next=if($j+1 -lt $chain.Count){$chain[$j+1]}else{65535}
  for($f=0;$f -lt $nfats;$f++){[BitConverter]::GetBytes([uint16]$next).CopyTo($bytes,$esp+($reserved+$f*$fatSectors)*$bps+$chain[$j]*2)}
  $at=$dataAt+($chain[$j]-2)*$spc*$bps;[Array]::Clear($bytes,$at,$spc*$bps)
  $n=[Math]::Min($spc*$bps,$data.Length-$j*$spc*$bps);[Array]::Copy($data,$j*$spc*$bps,$bytes,$at,$n)
 }
 [Array]::Clear($bytes,$slot,32);[Text.Encoding]::ASCII.GetBytes($short).CopyTo($bytes,$slot)
 $bytes[$slot+11]=32;[BitConverter]::GetBytes([uint16]$chain[0]).CopyTo($bytes,$slot+26);[BitConverter]::GetBytes([uint32]$data.Length).CopyTo($bytes,$slot+28)
}
function Bmp([int]$w,[int]$h,[int]$bits,[bool]$top){
 $stride=[int][Math]::Ceiling($w*($bits/8.0)/4)*4;$b=[byte[]]::new(54+$stride*$h)
 $b[0]=66;$b[1]=77;[BitConverter]::GetBytes([uint32]$b.Length).CopyTo($b,2);[BitConverter]::GetBytes([uint32]54).CopyTo($b,10)
 [BitConverter]::GetBytes([uint32]40).CopyTo($b,14);[BitConverter]::GetBytes([uint32]$w).CopyTo($b,18)
 $height=if($top){[uint32](4294967296L-$h)}else{[uint32]$h};[BitConverter]::GetBytes($height).CopyTo($b,22)
 [BitConverter]::GetBytes([uint16]1).CopyTo($b,26);[BitConverter]::GetBytes([uint16]$bits).CopyTo($b,28)
 if($bits -eq 24){$upper=[byte[]](@(0,0,255)*($w/2)+@(0,255,0)*($w/2));$lower=[byte[]](@(255,0,0)*($w/2)+@(255,255,255)*($w/2));for($y=0;$y -lt $h;$y++){$row=if($y -lt $h/2){$lower}else{$upper};[Array]::Copy($row,0,$b,54+$y*$stride,$row.Length)}}
 else{$row=[byte[]](@(51,34,17,128)*$w);for($y=0;$y -lt $h;$y++){[Array]::Copy($row,0,$b,54+$y*$stride,$row.Length)}}
 return ,$b
}
PutFile 'PHOTO   BMP' (Bmp 512 384 24 $false)
PutFile 'OTHER   BMP' (Bmp 128 128 32 $true)
PutFile 'BAD     BMP' ([byte[]]::new(70))
$disk=Join-Path $OutDir 'wallpaper.img';[IO.File]::WriteAllBytes($disk,$bytes)
Copy-Item $disk (Join-Path $OutDir 'plain.img')
$template=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'wallpaper.codex'))
for($phase=1;$phase -le 4;$phase++){
 $stem=Join-Path $OutDir "phase$phase";$src="$stem.codex"
 [IO.File]::WriteAllText($src,$template.Replace('wpf-phase : Integer = 1',"wpf-phase : Integer = $phase"))
 Compile $src $stem
 Guest "$stem.cdx" $disk $stem
 switch($phase){
  1{Grade $stem @('filename keyboard input: True','photo applied and cached: True','wallpaper filename persisted: True')}
  2{Grade $stem @('wallpaper survived reboot and scratch overwrite: True','bad file preserves choice, cache and fact count: True','top-down BGRX32 file applied: True','confirmed reset removes wallpaper: True')}
  3{Grade $stem @('wallpaper reset survived reboot: True','photo restored for idle proof: True');Copy-Item $disk (Join-Path $OutDir 'selected.img')}
  4{Grade $stem @('failed publication preserves wallpaper cache: True')}
 }
}
$bundle=Join-Path $OutDir 'desk.codex'
& pwsh -NoProfile -File build/bundle-app.ps1 -Src apps/works/DeskVm.codex -Out $bundle
if($LASTEXITCODE -ne 0){throw 'Desk bundle failed'}
$source=[IO.File]::ReadAllText($bundle)
$anchor='      ap <- dk-settings-boot ds'
if($source.IndexOf($anchor) -lt 0 -or $source.IndexOf($anchor) -ne $source.LastIndexOf($anchor)){throw 'Watch anchor changed'}
$armed=$source.Replace($anchor,$anchor+"`n      watchLo <- port-out-32 1041 (bit-and (base + (383 * stride + 510) * 4) 4294967295)`n      watchHi <- port-out-32 1042 (bit-shru (base + (383 * stride + 510) * 4) 32)`n      watchArm <- port-out-32 1045 0")
$watched=Join-Path $OutDir 'watched.codex';[IO.File]::WriteAllText($watched,$armed);Compile $watched (Join-Path $OutDir 'watched')
$counts=@{}
foreach($mode in @('plain','selected')){
 $seen=@()
 foreach($ms in @(8000,16000)){
  $stem=Join-Path $OutDir "$mode-$ms";$copy="$stem.img";Copy-Item (Join-Path $OutDir "$mode.img") $copy
  Guest (Join-Path $OutDir 'watched.cdx') $copy $stem $ms
  $trace=[IO.File]::ReadAllText("$stem.err");$hits=[regex]::Matches($trace,'HWWATCH #').Count
  if($trace -notmatch 'GUEST-ARM HWWATCH' -or $hits -lt 1){throw 'Probe missed initial paint'}
  $seen+=$hits;"$mode $ms ms: $hits watched background writes"
 }
 if($seen[0] -ne $seen[1]){throw "$mode background repaint grows while idle"}
 $counts[$mode]=$seen
}
$startAnchor='      let tki = dk-task-init ds tf w'
$loopAnchor='      let rr = dk-rate-tick ds'
foreach($needle in @($startAnchor,$loopAnchor)){if($armed.IndexOf($needle) -lt 0 -or $armed.IndexOf($needle) -ne $armed.LastIndexOf($needle)){throw 'Late-write control anchor changed'}}
$negative=$armed.Replace($startAnchor,"      let watchStart = poke-qword (peek-32 ds dk-sty-cell) 1040 hpet-ticks`n      in let tki = dk-task-init ds tf w")
$injection='      let late = if hpet-ticks-per-second > 0 & peek-qword (peek-32 ds dk-sty-cell) 1048 == 0 & hpet-ticks - peek-qword (peek-32 ds dk-sty-cell) 1040 >= hpet-ticks-per-second * 10 then let changed = poke-qword (base + (383 * stride + 510) * 4) 0 1 in poke-qword (peek-32 ds dk-sty-cell) 1048 1 else 0'
$negative=$negative.Replace($loopAnchor,$injection+"`n      in let rr = dk-rate-tick ds")
$negativePath=Join-Path $OutDir 'late-write.codex';[IO.File]::WriteAllText($negativePath,$negative);Compile $negativePath (Join-Path $OutDir 'late-write')
$negativeDisk=Join-Path $OutDir 'late-write.img';Copy-Item (Join-Path $OutDir 'selected.img') $negativeDisk
Guest (Join-Path $OutDir 'late-write.cdx') $negativeDisk (Join-Path $OutDir 'late-write') 16000
$negativeTrace=[IO.File]::ReadAllText((Join-Path $OutDir 'late-write.err'))
$negativeHits=[regex]::Matches($negativeTrace,'HWWATCH #').Count
if($negativeHits -le $counts['selected'][1] -or $negativeTrace -notmatch 'now=0x1\b'){throw 'Idle budget probe did not detect the injected late write'}
$counts['late-write']=$negativeHits
'late-write control rejected by idle budget'
if((Get-FileHash seed/Codex.img).Hash -ne $inputHash){throw 'Seed image changed'}
@{status='pass';kernel=(Get-FileHash $Kernel).Hash;source=(Get-FileHash (Join-Path $PSScriptRoot 'wallpaper.codex')).Hash;desk=(Get-FileHash apps/works/GopDesk.codex).Hash;wallpaper=(Get-FileHash apps/works/GopWallpaper.codex).Hash;watchedCdx=(Get-FileHash (Join-Path $OutDir 'watched.cdx')).Hash;inputImage=$inputHash;counts=$counts}|ConvertTo-Json -Depth 5|Set-Content (Join-Path $OutDir 'evidence.json')
'PASS: file decode, persisted choice, cold reset, refusal cache preservation and sampled idle budget'
