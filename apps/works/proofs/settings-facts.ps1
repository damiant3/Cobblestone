[CmdletBinding()]
param([string]$Kernel='seed/Codex.cdx',[string]$OutDir='')
$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Set-Location $repo
if(-not $OutDir){$OutDir=Join-Path $repo ('build-output/settings-facts-'+[Guid]::NewGuid().ToString('N'))}
if(-not [IO.Path]::IsPathRooted($OutDir)){$OutDir=Join-Path $repo $OutDir}
$OutDir=[IO.Path]::GetFullPath($OutDir)
if(-not $OutDir.StartsWith((Join-Path $repo 'build-output')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Output must be inside this repository build-output'}
if(Test-Path $OutDir){throw 'Choose a fresh output directory'}
New-Item -ItemType Directory -Path $OutDir|Out-Null
$disk=Join-Path $OutDir 'settings.img'
$bytes=[byte[]]::new(16777216)
function W16([int]$at,[uint16]$value){[BitConverter]::GetBytes($value).CopyTo($bytes,$at)}
function W32([int]$at,[uint32]$value){[BitConverter]::GetBytes($value).CopyTo($bytes,$at)}
function W64([int]$at,[uint64]$value){[BitConverter]::GetBytes($value).CopyTo($bytes,$at)}
function Ascii([int]$at,[string]$text){[Text.Encoding]::ASCII.GetBytes($text).CopyTo($bytes,$at)}
function Crc([byte[]]$data,[int]$at,[int]$count){
 [uint32]$crc=4294967295
 for($i=0;$i -lt $count;$i++){$crc=$crc -bxor [uint32]$data[$at+$i];for($j=0;$j -lt 8;$j++){if($crc -band 1){$crc=($crc -shr 1) -bxor [uint32]3988292384}else{$crc=$crc -shr 1}}}
 return [uint32]($crc -bxor [uint32]4294967295)
}
if((Crc ([Text.Encoding]::ASCII.GetBytes('123456789')) 0 9) -ne [uint32]3421780262){throw 'CRC control failed'}
# Independent GPT/FAT16 fixture: ESP 2048..18431; facts 18432..26623.
$bytes[446+4]=238;W32 454 1;W32 458 32767;W16 510 43605
[byte[]]$espGuid=40,115,42,193,31,248,210,17,186,75,0,160,201,62,201,59
[byte[]]$factsGuid=17,26,222,192,199,250,13,76,158,117,192,222,192,222,94,237
$espGuid.CopyTo($bytes,1024);$bytes[1040]=1;W64 1056 2048;W64 1064 18431
$factsGuid.CopyTo($bytes,1152);$bytes[1168]=2;W64 1184 18432;W64 1192 26623
$entryCrc=Crc $bytes 1024 16384
function Header([int]$sector,[int]$other,[int]$entries){
 $at=$sector*512;Ascii $at 'EFI PART';W32 ($at+8) 65536;W32 ($at+12) 92;W64 ($at+24) $sector;W64 ($at+32) $other;W64 ($at+40) 34;W64 ($at+48) 32734;$bytes[$at+56]=3;W64 ($at+72) $entries;W32 ($at+80) 128;W32 ($at+84) 128;W32 ($at+88) $entryCrc;W32 ($at+16) (Crc $bytes $at 92)
}
Header 1 32767 2
[Array]::Copy($bytes,1024,$bytes,32735*512,16384)
Header 32767 1 32735
$vol=2048*512;$bytes[$vol]=235;$bytes[$vol+1]=60;$bytes[$vol+2]=144;Ascii ($vol+3) 'CODEX   ';W16 ($vol+11) 512;$bytes[$vol+13]=1;W16 ($vol+14) 1;$bytes[$vol+16]=2;W16 ($vol+17) 512;W16 ($vol+19) 16384;$bytes[$vol+21]=248;W16 ($vol+22) 64;W16 ($vol+24) 63;W16 ($vol+26) 255;W32 ($vol+28) 2048;$bytes[$vol+36]=128;$bytes[$vol+38]=41;W32 ($vol+39) 1397052469;Ascii ($vol+43) 'SETTINGS   ';Ascii ($vol+54) 'FAT16   ';W16 ($vol+510) 43605
foreach($fat in @(($vol+512),($vol+65*512))){W16 $fat 65528;W16 ($fat+2) 65535;W16 ($fat+4) 65535}
$root=$vol+129*512;Ascii $root 'SETTINGSDAT';$bytes[$root+11]=32;W16 ($root+26) 2;W32 ($root+28) 56
$legacy=$vol+161*512;Ascii $legacy 'CST1';W16 ($legacy+4) 6
$records=@(@(1,2),@(2,9),@(3,1),@(4,2),@(5,17),@(99,42))
for($i=0;$i -lt $records.Count;$i++){W32 ($legacy+8+$i*8) $records[$i][0];W32 ($legacy+12+$i*8) ([uint32]([long]$records[$i][1]+2147483648))}
[IO.File]::WriteAllBytes($disk,$bytes)
function EspHash([byte[]]$image){$stream=[IO.MemoryStream]::new($image,$vol,16384*512,$false);try{return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream))}finally{$stream.Dispose()}}
$initial=(Get-FileHash $disk).Hash
$espInitial=EspHash $bytes
$factsInitial=(Get-FileHash apps/works/GopFacts.codex).Hash
$phaseEvidence=[Collections.Generic.List[object]]::new()
$fixture=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'settings-facts.codex'))
$phaseMarker='sfp-phase : Integer = 1'
if($fixture.IndexOf($phaseMarker) -ne $fixture.LastIndexOf($phaseMarker) -or $fixture.IndexOf($phaseMarker) -lt 0){throw 'Phase marker differs'}
function Admit {if((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory -le 1572864){throw 'RAM refused'}}
function Boot([int]$phase,[string[]]$expected){
 Admit
 $src=Join-Path $OutDir "phase$phase.codex";$bundle=Join-Path $OutDir "phase$phase-bundle.codex";$cdx=Join-Path $OutDir "phase$phase.cdx"
 [IO.File]::WriteAllText($src,$fixture.Replace($phaseMarker,"sfp-phase : Integer = $phase"))
 & pwsh -NoProfile -File build/bundle-app.ps1 -Src $src -Out $bundle
 if($LASTEXITCODE -ne 0){throw 'Bundle failed'}
 if($phase -eq 5){
  $text=[IO.File]::ReadAllText($bundle)
  $sig='  disk-write-into : Integer, Integer, Integer -> [Device.Port] Boolean'
  $body='  disk-write-into (lba) (count) (src) = act'
  if($text.IndexOf($sig) -lt 0 -or $text.IndexOf($sig) -ne $text.LastIndexOf($sig) -or $text.IndexOf($body) -ne $text.LastIndexOf($body)){throw 'Disk write injection site differs'}
  $wrapper="  disk-write-into : Integer, Integer, Integer -> [Device.Port] Boolean`n  disk-write-into (lba) (count) (src) = if lba == 18432 | lba == 18433 then False else sfp-real-disk-write-into lba count src`n`n"
  $text=$text.Replace($sig,$wrapper+$sig.Replace('disk-write-into','sfp-real-disk-write-into')).Replace($body,$body.Replace('disk-write-into','sfp-real-disk-write-into'))
  [IO.File]::WriteAllText($bundle,$text)
 }
 if($phase -in @(6,7,11,14)){
  $text=[IO.File]::ReadAllText($bundle)
  if($phase -ne 7){
   $name='disk-read-sector';$sig='  disk-read-sector : Integer -> [Device.Port] DiskRead';$body='  disk-read-sector (lba) = act'
   $failSector=if($phase -eq 14){18432}elseif($phase -eq 11){18435}else{18434}
   $wrapper="  disk-read-sector : Integer -> [Device.Port] DiskRead`n  disk-read-sector (lba) = if lba == $failSector then DiskRead { dr-ok = False, dr-buf = 0, dr-via = `"proof`" } else sfp-real-disk-read-sector lba`n`n"
  }else{
   $name='disk-read-into';$sig='  disk-read-into : Integer, Integer, Integer -> [Device.Port] Boolean';$body='  disk-read-into (lba) (count) (dest) = act'
   $wrapper="  disk-read-into : Integer, Integer, Integer -> [Device.Port] Boolean`n  disk-read-into (lba) (count) (dest) = if lba >= 18434 & lba < 26624 then False else sfp-real-disk-read-into lba count dest`n`n"
  }
  if($text.IndexOf($sig) -lt 0 -or $text.IndexOf($sig) -ne $text.LastIndexOf($sig) -or $text.IndexOf($body) -lt 0 -or $text.IndexOf($body) -ne $text.LastIndexOf($body)){throw 'Disk read injection site differs'}
  $text=$text.Replace($sig,$wrapper+$sig.Replace($name,'sfp-real-'+$name)).Replace($body,$body.Replace($name,'sfp-real-'+$name))
  [IO.File]::WriteAllText($bundle,$text)
 }
 & pwsh -NoProfile -File build/compile.ps1 -Src $bundle -Out $cdx -Log (Join-Path $OutDir "phase$phase-compile.log") -Kernel $Kernel
 if($LASTEXITCODE -ne 0){throw "Phase$phase compile failed"}
 Admit
 $output=Join-Path $OutDir "phase$phase.out";$err=Join-Path $OutDir "phase$phase.err"
 $guest=Start-Process (Join-Path $repo 'tools/codex-vm.exe') -WindowStyle Hidden -ArgumentList @('-kernel',('"'+$cdx+'"'),'-disk',('"'+$disk+'"'),'-output',('"'+$output+'"'),'-mem','3072','-headless','-smp','1') -RedirectStandardError $err -PassThru
 "phase$phase PID=$($guest.Id)"
 try {if(-not $guest.WaitForExit(90000)){throw "Phase$phase timeout"};$errors=[IO.File]::ReadAllText($err);if($guest.ExitCode -ne 1 -or $errors -notmatch 'FINAL: debug_exit_code=0 process_exit=1' -or $errors -match 'DROPPED|Triple fault|HOST CRASH'){throw "Phase$phase invalid completion"}}
 finally {if(-not $guest.HasExited){Stop-Process -Id $guest.Id -Force};$guest.WaitForExit();$guest.Dispose()}
 $body=([IO.File]::ReadAllText($output).Replace("`r",'') -split "`n"|Where-Object {$_ -and $_ -notmatch '^(HEAP|WD|STACK|PM):'}) -join "`n"
 $body
 if($body -cne ($expected -join "`n")){throw "Phase$phase output differs"}
 $phaseEvidence.Add(@{phase=$phase;bundleSha256=(Get-FileHash $bundle).Hash;cdxSha256=(Get-FileHash $cdx).Hash;outputSha256=(Get-FileHash $output).Hash})
}
Boot 5 @('unpublished migration retains legacy values: True','failed checkpoint reports no saved fact: True')
Boot 1 @('load state: migrated','one migration fact: True','legacy values preserved: True','out-of-range refused without mutation: True','new typed values saved: True','history reclaimed before return: True')
if((EspHash ([IO.File]::ReadAllBytes($disk))) -ne $espInitial){throw 'Migration modified the legacy FAT partition'}
# Change the still-existing legacy file after migration; facts must win.
$bytes=[IO.File]::ReadAllBytes($disk);W32 ($legacy+12) ([uint32]2147483651);[IO.File]::WriteAllBytes($disk,$bytes)
$espBefore=EspHash $bytes
$bytes[$legacy]=$bytes[$legacy] -bxor 1
if((EspHash $bytes) -eq $espBefore){throw 'ESP hash control failed'}
$bytes[$legacy]=$bytes[$legacy] -bxor 1
Boot 2 @('load state: loaded','migration not repeated: True','typed values survived reboot: True','explicit reset saved: True')
Boot 3 @('load state: loaded','defaults survived reboot: True','legacy file did not revive settings: True')
$bytes=[IO.File]::ReadAllBytes($disk);$espAfter=EspHash $bytes
if($espBefore -ne $espAfter){throw 'Reset changed the legacy FAT partition'}
Boot 8 @('desk candidate published and applied: True')
Boot 9 @('desk values survived cold boot and scratch overwrite: True','desk confirmed reset published: True')
Boot 10 @('desk reset survived next cold boot: True','desk boot did not repeat legacy migration: True')
if((EspHash ([IO.File]::ReadAllBytes($disk))) -ne $espBefore){throw 'Desk settings changed the legacy FAT partition'}
Boot 6 @('fact read failure refused: True','read failure did not append migration: True')
Boot 7 @('fact read failure refused: True','read failure did not append migration: True')
Boot 11 @('fact read failure refused: True','read failure did not append migration: True')
Boot 14 @('fact read failure refused: True','read failure did not append migration: True')
Boot 12 @('checked missing kind is empty success: True','checked scan matches legacy successful scan: True','checked empty content is a present record: True')
Boot 4 @('full store refused: True','refused write preserves published head: True')
if((Get-FileHash apps/works/GopFacts.codex).Hash -ne $factsInitial){throw 'GopFacts source changed during proof'}
@{phases=$phaseEvidence.ToArray();adapterSha256=(Get-FileHash apps/works/GopSettingsFacts.codex).Hash;gopFactsInitialSha256=$factsInitial;initialImageSha256=$initial;finalImageSha256=(Get-FileHash $disk).Hash;kernelSha256=(Get-FileHash $Kernel).Hash;sourceSha256=(Get-FileHash (Join-Path $PSScriptRoot 'settings-facts.codex')).Hash;gopFactsSha256=(Get-FileHash apps/works/GopFacts.codex).Hash;status='pass'}|ConvertTo-Json|Set-Content (Join-Path $OutDir 'evidence.json')
'PASS: cold boots, typed persistence, checkpoint refusal, once-only migration, explicit reset and unchanged legacy file'
