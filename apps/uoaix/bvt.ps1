[CmdletBinding()]
param([string]$Kernel='seed/Codex.cdx',[string]$ClientRoot='C:/Users/Damian/uo1998-client',[string]$OutDir='',
    [string]$Cache=(Join-Path $env:LOCALAPPDATA 'uoaix-bvt'),[ValidateSet('TESTING','DEV')][string]$Mode='TESTING')
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Set-Location $repo
$Kernel=(Resolve-Path -LiteralPath $Kernel).Path
if(-not $OutDir){$OutDir=Join-Path $repo ('build-output/uoaix/bvt-'+[guid]::NewGuid().ToString('N'))}
$OutDir=[IO.Path]::GetFullPath($OutDir)
if(Test-Path -LiteralPath $OutDir){throw 'OutDir must be new'}
[void](New-Item -ItemType Directory -Path $OutDir)
# Decoration (27 s) and flora are built once per client into a cache outside the repo (ruling R1).
$decoration=Join-Path $Cache 'britannia.dwd';$flora=Join-Path $Cache 'flora/flora.flr'
if(-not (Test-Path -LiteralPath $decoration)){& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'import-decoration.ps1') -ClientRoot $ClientRoot -OutFile $decoration | Out-Null;if($LASTEXITCODE -ne 0){throw 'Decoration import failed'}}
if(-not (Test-Path -LiteralPath $flora)){& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'transform-flora.ps1') -ClientRoot $ClientRoot -OutDir (Join-Path $Cache 'flora') | Out-Null;if($LASTEXITCODE -ne 0){throw 'Flora transform failed'}}
$clock=[Diagnostics.Stopwatch]::StartNew()
$script:failures=0
$script:guests=@()
$script:stderrs=@()
function Check([string]$Name,[bool]$Ok){if($Ok){"PASS $Name"}else{$script:failures++;"FAIL $Name"}}
function Admit([int]$KiB){$free=(Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory;if($free -lt $KiB){throw "RAM admission: $free KiB free"}}
function FreePort{$l=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0);$l.Start();$p=$l.LocalEndpoint.Port;$l.Stop();$p}
function ReadShared([string]$Path){if(-not (Test-Path -LiteralPath $Path)){return ''};$fs=[IO.File]::Open($Path,'Open','Read','ReadWrite,Delete');$r=[IO.StreamReader]::new($fs);try{$r.ReadToEnd()}finally{$r.Dispose()}}

# Unit cases: every apps/uoaix/bvt chapter, inlined (the resolver maps Uoaix chapters to apps/uoaix only),
# under an entry generated from each `bvt-<name> : Integer -> [Console] Integer`.
. (Join-Path $repo 'build/quire-map.ps1')
$caseFiles=@(Get-ChildItem (Join-Path $PSScriptRoot 'bvt') -Filter '*.codex' | Sort-Object { if($_.BaseName -eq 'BvtCheck'){0}else{1} },Name)
$parts=[Collections.Generic.List[string]]::new();$seen=@{};$cites=@();$calls=@()
foreach($f in $caseFiles){
    $name=$f.BaseName
    $text=[IO.File]::ReadAllText($f.FullName)
    $parts.Add($text.Replace("Chapter: $name","Chapter: Uoaix--$name"))
    $seen["Uoaix::$name"]=$true
    $cites+="  cites Uoaix chapter $name"
    foreach($m in [regex]::Matches($text,'(?m)^  (bvt-[a-z0-9-]+) : Integer -> \[Console\] Integer\s*$')){$calls+=$m.Groups[1].Value}
}
if($calls.Count -lt 1){throw 'No BVT case functions found'}
$entry=@('Chapter: BvtRun')+$cites+@('','  opening : [Console] Integer = act')
for($i=0;$i -lt $calls.Count;$i++){$entry+="    f$i <- $($calls[$i]) 0"}
$sum=(0..($calls.Count-1)|ForEach-Object{"f$_"}) -join ' + '
$entry+=@("    print-line-uni (`"UOAIX BVT unit failures=`" & show ($sum) & `" functions=$($calls.Count)`")",'    0','  end')
$body=($parts -join "`r`n")+"`r`n"+($entry -join "`r`n")+"`r`n"
$ordered=Resolve-CiteOrder -RootLines ($body -split '\r?\n') -Repo $repo -SeedSeen $seen
$unit=Join-Path $OutDir 'bvt-unit.codex'
[IO.File]::WriteAllText($unit,((Format-CiteChapters -Ordered $ordered) -join "`r`n")+"`r`n"+$body)

Admit 1048576
$unitCdx=Join-Path $OutDir 'bvt-unit.cdx';$serverCdx=Join-Path $OutDir 'server.cdx'
$compiles=@(
    @{name='unit';p=(Start-Process pwsh -PassThru -WindowStyle Hidden -ArgumentList @('-NoProfile','-File','build/compile.ps1','-Src',('"'+$unit+'"'),'-Out',('"'+$unitCdx+'"'),'-Log',('"'+(Join-Path $OutDir 'unit.compile.log')+'"'),'-Kernel',('"'+$Kernel+'"')) -RedirectStandardOutput (Join-Path $OutDir 'unit.compile.out'))},
    @{name='server';p=(Start-Process pwsh -PassThru -WindowStyle Hidden -ArgumentList @('-NoProfile','-File','build/compile.ps1','-Src','apps/uoaix/CompositeGameServer.codex','-Out',('"'+$serverCdx+'"'),'-Log',('"'+(Join-Path $OutDir 'server.compile.log')+'"'),'-Kernel',('"'+$Kernel+'"')) -RedirectStandardOutput (Join-Path $OutDir 'server.compile.out'))})
$world=Join-Path $OutDir 'world.disk'
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'install-map-cache.ps1') -ClientRoot $ClientRoot -WorldDisk $world -DecorationFile $decoration -FloraFile $flora | Out-Null
$installed=$LASTEXITCODE -eq 0
# The restart chain boots its own copy of the fresh disk beside the walk guest; copied before any guest opens it (L-HOTCOPY).
$world2=Join-Path $OutDir 'world2.disk'
if($installed){Copy-Item -LiteralPath $world -Destination $world2}
foreach($c in $compiles){$c.p.WaitForExit();$c.exit=$c.p.ExitCode}
Write-Output "kernel: $Kernel [$((Get-FileHash $Kernel).Hash.Substring(0,16))]"
Check 'server compiles (CompositeGameServer, 0 errors)' ($compiles[1].exit -eq 0 -and (Test-Path $serverCdx))
Check 'unit cases compile' ($compiles[0].exit -eq 0 -and (Test-Path $unitCdx))
Check 'fresh world installs' $installed

function Launch([string]$Name,[string]$Disk,[string]$Record){
    Admit 1572864
    $port=FreePort
    if(Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue){throw "Port $port is owned"}
    $log=Join-Path $OutDir "$Name.log"
    $recordFile=Join-Path $OutDir "$Name.input"
    [IO.File]::WriteAllText($recordFile,"$Record`n",[Text.Encoding]::ASCII)
    # codex-vm writes stderr unbuffered; redirected under build-output a 0.2 s run took 35-38 s (2026-10-08), under TEMP 0.2 s.
    $err=Join-Path ([IO.Path]::GetTempPath()) ("uoaix-bvt-"+[guid]::NewGuid().ToString('N')+"-$Name.stderr")
    $script:stderrs+=@{from=$err;to=(Join-Path $OutDir "$Name.stderr")}
    $vmArgs=@('-kernel',('"'+$serverCdx+'"'),'-disk',('"'+$Disk+'"'),'-output',('"'+$log+'"'),'-headless','-mem','3072','-e1000-nat','-portfwd',"${port}:2593",'-input',('"'+$recordFile+'"'))
    $vm=Start-Process (Join-Path $repo 'tools/codex-vm.exe') -ArgumentList $vmArgs -WindowStyle Hidden -PassThru -RedirectStandardError $err
    $script:guests+=$vm
    Write-Host "$Name guest PID=$($vm.Id) port=$port log=$log"
    return @{vm=$vm;port=$port;log=$log;text=''}
}
function Await($G){
    $deadline=[datetime]::UtcNow.AddSeconds(90)
    do{
        $text=ReadShared $G.log
        if($text -match '(?m)^FAIL|!EXC|OUT OF MEMORY' -or $G.vm.HasExited){break}
        if($text -match '(?m)^LISTEN game-server 0'){break}
        Start-Sleep -Milliseconds 200
    }while([datetime]::UtcNow -lt $deadline)
    $G.text=ReadShared $G.log
    return $G
}
function Stop-Guest($Vm){if(-not $Vm.HasExited){Stop-Process -Id $Vm.Id -Force;[void]$Vm.WaitForExit(10000)}}

$codes=@{}
$book=[regex]::Match([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'GameHuffman.codex')),'(?s)gh-codebook : List Integer = \[(.*?)\]').Groups[1].Value
$pairs=@([regex]::Matches($book,'#([0-9A-Fa-f]+)')|ForEach-Object{[Convert]::ToInt32($_.Groups[1].Value,16)})
for($i=0;$i -le 256;$i++){$codes["$($pairs[$i*2]):$($pairs[$i*2+1])"]=$i}
function U16([byte[]]$B,[int]$At){[int]$B[$At]*256+$B[$At+1]}
function U32([byte[]]$B,[int]$At){[long](U16 $B $At)*65536+(U16 $B ($At+2))}
function Read-Exact($S,[int]$N){$b=[byte[]]::new($N);$at=0;while($at -lt $N){$n=$S.Read($b,$at,$N-$at);if($n -eq 0){throw 'socket closed'};$at+=$n};return ,$b}
function Read-Packet($S){$plain=[Collections.Generic.List[byte]]::new();$bits=0;$value=0
    for($r=0;$r -lt 6000;$r++){$byte=$S.ReadByte();if($byte -lt 0){throw 'socket closed in packet'}
        for($bit=7;$bit -ge 0;$bit--){$value=($value-shl 1)-bor (($byte-shr $bit)-band 1);$bits++
            if($codes.ContainsKey("${bits}:$value")){$sym=$codes["${bits}:$value"];if($sym -eq 256){return ,$plain.ToArray()};$plain.Add([byte]$sym);$bits=0;$value=0}elseif($bits -gt 11){throw 'bad Huffman prefix'}}}
    throw 'packet budget'}
function Login-Xor([byte[]]$Bytes,[uint64]$seed=0x01020304){
    [uint64]$mask=4294967295
    [uint64]$low=(((($seed-bxor $mask)-bxor 0x1357)-shl 16)-bor (($seed-bxor 0xFFFFAAAAUL)-band 65535))-band $mask
    [uint64]$high=(($seed-bxor 0x43210000)-shr 16)-bor ((($seed-bxor $mask)-bxor 0xABCDFFFFUL)-band 4294901760)
    $r=[byte[]]::new($Bytes.Length)
    for($i=0;$i -lt $Bytes.Length;$i++){$r[$i]=$Bytes[$i]-bxor ($low-band 255)
        $next=((($low-shr 1)-bor ($high-shl 31))-bxor 0x026950C6)-band $mask
        $high=((($high-shr 1)-bor ($low-shl 31))-bxor 0x389DE58C)-band $mask;$low=$next}
    return ,$r}
function Login([int]$Port){
    $name=[Text.Encoding]::ASCII.GetBytes('uoaixbvt')
    $plain=[byte[]]::new(214);$plain[0]=128;$plain[61]=100;$plain[62]=164;$plain[211]=160
    $name.CopyTo($plain,1);for($i=0;$i -lt $name.Length;$i++){$plain[31+$i]=$name[$i]-13}
    $enc=Login-Xor $plain
    $tcp=[Net.Sockets.TcpClient]::new('127.0.0.1',$Port)
    try{$st=$tcp.GetStream();$st.ReadTimeout=20000
        $st.Write([byte[]]@(1,2,3,4),0,4);$st.Write($enc,0,62)
        $list=Read-Exact $st 46;if($list[0] -ne 168){return "no shard list (0x$('{0:X2}' -f $list[0]))"}
        $st.Write($enc,62,152)
        $relay=Read-Exact $st 11;if($relay[0] -ne 140){return 'no relay'}
        $key=U32 $relay 7
    }finally{$tcp.Dispose()}
    $g=[byte[]]::new(65);$g[0]=145;$g[1]=($key-shr 24)-band 255;$g[2]=($key-shr 16)-band 255;$g[3]=($key-shr 8)-band 255;$g[4]=$key-band 255
    $name.CopyTo($g,5);for($i=0;$i -lt $name.Length;$i++){$g[35+$i]=$name[$i]-13}
    $tcp=[Net.Sockets.TcpClient]::new('127.0.0.1',$Port)
    try{$st=$tcp.GetStream();$st.ReadTimeout=20000
        $st.Write([byte[]]@($g[1],$g[2],$g[3],$g[4]),0,4);$st.Write((Login-Xor $g $key),0,65)
        $chars=Read-Packet $st
        if($chars[0] -ne 169){return "first game packet 0x$('{0:X2}' -f $chars[0]), not the character list"}
        return ''
    }finally{$tcp.Dispose()}
}

try{
    if((Test-Path $serverCdx) -and $installed){
        $first=Launch 'boot1' $world "UOAIX $Mode"
        $restart=Launch 'boot1b' $world2 "UOAIX $Mode"
        if($compiles[0].exit -eq 0){
            $unitOut=Join-Path $OutDir 'unit.out'
            & pwsh -NoProfile -File build/test-run.ps1 -Kernel $unitCdx -OutFile $unitOut | Out-Null
            $text=ReadShared $unitOut
            $text -split "`r?`n" | Where-Object { $_ -match '^(PASS|FAIL) ' }
            Check 'unit cases run clean' ($text.Contains('UOAIX BVT unit failures=0') -and $text -notmatch '(?m)^FAIL |!EXC|OUT OF MEMORY')
        }
        $first=Await $first
        $restart=Await $restart
        Check 'server boots a fresh world to LISTEN' ($first.text -match '(?m)^LISTEN game-server 0' -and $first.text -match '(?m)^WORLD (NEW|RESTORED)')
        Check 'a fresh world founds Britain''s mayor and guards (CIVIC founding)' ($first.text -match '(?m)^CIVIC founding')
        # Britain's shops, then every city shop row of each table ct-city-shops joins (bb-found-cities founds them at boot).
        $shops=([regex]::Matches([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'BritainCatalog.codex')),'BbShop \{ title =')).Count
        $citiesText=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Cities.codex'))
        $joined=[regex]::Match($citiesText,'(?m)^  ct-city-shops \(unused\) = (.*)$').Groups[1].Value
        foreach($table in [regex]::Matches($joined,'ct-shops-[a-z]+') | ForEach-Object Value){
            $rows=[regex]::Match($citiesText,"(?s)\n  $table \(unused\) = \[(.*?)\]\r?\n").Groups[1].Value
            $shops+=([regex]::Matches($rows,'BbShop \{ title =')).Count
        }
        Check "a fresh world stands one vendor per shop and one banker, no bank lineup (Damian, 2026-10-08)" ($first.text -match "(?m)^SPEECH on: \d+ listeners: $shops vendors, 1 bankers")
        Check 'a TESTING boot logs its economy closed until British strikes gold' ($first.text -match '(?m)^ECONOMY closed until British strikes gold')
        $why=if($first.text -match '(?m)^LISTEN game-server 0'){try{Login $first.port}catch{$_.Exception.Message}}else{'no listener'}
        Check "server admits a login to the character list$(if($why){': '+$why})" (-not $why)
        # The restart guest saves a logged-in fresh world, then restarts on it while the walk guest keeps running.
        $before=@([regex]::Matches((ReadShared $restart.log),'(?m)^WORLD COMMITTED')).Count
        $whyB=if($restart.text -match '(?m)^LISTEN game-server 0'){try{Login $restart.port}catch{$_.Exception.Message}}else{'no listener'}
        $deadline=[datetime]::UtcNow.AddSeconds(30)
        while(@([regex]::Matches((ReadShared $restart.log),'(?m)^WORLD COMMITTED')).Count -le $before -and [datetime]::UtcNow -lt $deadline -and -not $restart.vm.HasExited){Start-Sleep -Milliseconds 200}
        $committed=(-not $whyB) -and @([regex]::Matches((ReadShared $restart.log),'(?m)^WORLD COMMITTED')).Count -gt $before
        $restartSaved=ReadShared $restart.log
        Stop-Guest $restart.vm
        $second=Launch 'boot2' $world2 'UOAIX TESTING OPEN'
        $deadline=[datetime]::UtcNow.AddSeconds(15)
        while((ReadShared $first.log) -notmatch '(?m)^WORKER fisherman ' -and [datetime]::UtcNow -lt $deadline -and -not $first.vm.HasExited){Start-Sleep -Milliseconds 200}
        $workers=ReadShared $first.log
        Check 'a fresh world founds the fisherman and the lumberjack (main 40142, 40050)' ($workers -match '(?m)^WORKER fisherman ' -and $workers -match '(?m)^WORKER lumberjack ')
        Check 'a fresh world founds the sand gatherer on the shore (row 19)' ($workers -match '(?m)^WORKER sand-gatherer ')
        Check 'a fresh world founds the clay gatherer on the shore (row 20)' ($workers -match '(?m)^WORKER clay-gatherer ')
        Check 'a fresh world founds the quarrier (row 21)' ($workers -match '(?m)^WORKER quarrier ')
        $strip={param($m) $x=[int]$m.Groups[1].Value;$y=[int]$m.Groups[2].Value;$x -ge 1446 -and $x -le 1457 -and $y -ge 1530 -and $y -le 1534}
        $shore=@([regex]::Matches($workers,'(?m)^WORKER (?:fisherman|sand-gatherer) .* node (\d+),(\d+) '))
        Check 'the fisherman and the sand gatherer keep a shore node, never one on the ore strip (cg-node-reach)' ($shore.Count -ge 2 -and -not ($shore | Where-Object { & $strip $_ }))
        $herbs=@(,@('herb-farmer',1428,1629,1451,1637))
        $herbed=@($herbs | Where-Object { $h=$_;$m=[regex]::Match($workers,"(?m)^WORKER $($h[0]) .* node (\d+),(\d+) ");$m.Success -and [int]$m.Groups[1].Value -ge $h[1] -and [int]$m.Groups[1].Value -le $h[3] -and [int]$m.Groups[2].Value -ge $h[2] -and [int]$m.Groups[2].Value -le $h[4] })
        Check "a fresh world founds the herb farmer on its first reagent's plant cluster (ginseng, the garden) and the root and vegetable farmers ($($herbed.Count) of 1)" ($herbed.Count -eq 1 -and $workers -match '(?m)^WORKER root-farmer ' -and $workers -match '(?m)^WORKER vegetable-farmer ')
        $beach=[regex]::Match($workers,'(?m)^WORKER sand-gatherer .* node (\d+),(\d+) ')
        Check 'the sand gatherer digs beside Britain''s sand land, 1497-1503 by 1721-1727 (install-map-cache sand mark)' ($beach.Success -and [int]$beach.Groups[1].Value -ge 1496 -and [int]$beach.Groups[1].Value -le 1503 -and [int]$beach.Groups[2].Value -ge 1720 -and [int]$beach.Groups[2].Value -le 1727)
        Check 'no worker founding is refused and no worker is logged under a miner''s name past the fourth' ($workers -notmatch 'not founded' -and $workers -notmatch '(?m)^WORKER miner-([5-9]|\d\d) ')
        $deadline=[datetime]::UtcNow.AddSeconds(45)
        while((ReadShared $first.log) -notmatch '(?m)^WORKER lumberjack leg (1 at|12) .* node \d+,14[0-8]\d ' -and [datetime]::UtcNow -lt $deadline -and -not $first.vm.HasExited){Start-Sleep -Milliseconds 200}
        $walked=ReadShared $first.log
        Check 'the lumberjack leaves the inn through its door bound for a forest tree north of the city (leg 1 past the door, or 12; inn door dead end, UOAIX-234)' ($walked -match '(?m)^WORKER lumberjack leg (1 at|12) .* node \d+,14[0-8]\d ')
        Check 'no worker is blocked 32 times on a fresh world' ($walked -notmatch '(?m)^WORKER .*blocked=(3[2-9]|[4-9]\d|\d{3,})')
        Check 'a fresh world puts up the Castle Britain portcullis raised, six pieces and the winch (cgt-founded-open, UOAIX-157)' ($first.text -match '(?m)^CASTLE GATE open placed=7 ')
        Check 'a fresh world founded on one page grows at the round''s growth point (WORLD GROWN, UoaixDynamicTables.md step 5)' ((ReadShared $first.log) -match '(?m)^WORLD GROWN capacity=')
        Check 'a fresh world saves every section (no SAVE SECTION FAILED)' ((ReadShared $first.log) -notmatch 'SAVE SECTION FAILED' -and $restartSaved -notmatch 'SAVE SECTION FAILED')
        Stop-Guest $first.vm
        $second=Await $second
        Check 'a TESTING OPEN restart opens the economy at boot with no client (cg-seed-gold)' ($second.text -match '(?m)^ECONOMY opened by the TESTING OPEN launch')
        Check 'a restart restores the castle gate as it stood and places no second one' ($second.text -match '(?m)^CASTLE GATE open placed=0 ')
        Check "server restores its own save after a restart$(if($whyB){' (login before the save: '+$whyB+')'}elseif(-not $committed){' (boot1b logged no WORLD COMMITTED after its login)'})" ($committed -and $second.text -match '(?m)^WORLD RESTORED' -and $second.text -match '(?m)^LISTEN game-server 0')
        Check 'a second boot restores the civic state and founds no new mayor or guards' ($second.text -match '(?m)^CIVIC restored' -and $second.text -notmatch '(?m)^CIVIC founding')
        Check 'a restored world saves every section (no SAVE SECTION FAILED)' ((ReadShared $second.log) -notmatch 'SAVE SECTION FAILED')
        Stop-Guest $second.vm
    }
}finally{
    foreach($g in $script:guests){Stop-Guest $g}
    foreach($s in $script:stderrs){if(Test-Path -LiteralPath $s.from){Move-Item -LiteralPath $s.from -Destination $s.to -Force}}
}
$seconds=[int]$clock.Elapsed.TotalSeconds
Check "BVT within 120 s (took $seconds s)" ($seconds -le 120)
if($script:failures -gt 0){Write-Output "UOAIX BVT FAIL failures=$($script:failures) evidence=$OutDir";exit 1}
Write-Output "UOAIX BVT PASS seconds=$seconds evidence=$OutDir"
