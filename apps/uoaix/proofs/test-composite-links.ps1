[CmdletBinding()]
param([Parameter(Mandatory)][string]$Artifact,[Parameter(Mandatory)][string]$StateFile,
    [Parameter(Mandatory)][string]$CompressionSource,[Parameter(Mandatory)][string]$OutDir,
    [ValidateRange(30,600)][int]$StartupSeconds=240,[string]$Vm='',[switch]$Gate,[string]$Corpus='')
# Two simultaneous composite players on one guest. The host port is derived from the workspace
# (never 2593, which a live shard holds) and a held port is refused rather than shared.
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$Artifact=(Resolve-Path -LiteralPath $Artifact).Path;$StateFile=(Resolve-Path -LiteralPath $StateFile).Path
if((Get-FileHash $CompressionSource).Hash -ne '807165537C00C83F295DC98F229F53D31460F055D1CC7FCBA9616E5966523042'){throw 'Wrong Huffman oracle source'}
$hash=[Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($repo.ToLowerInvariant()))
$Port=20000+([BitConverter]::ToUInt16($hash,0) % 10000)
if(Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue){throw "Port $Port is held; refusing to share it"}
$OutDir=[IO.Path]::GetFullPath($OutDir);if(Test-Path $OutDir){throw 'OutDir must be new'};[void](New-Item -ItemType Directory $OutDir)
$source=[IO.File]::ReadAllText((Resolve-Path $CompressionSource))
$values=@([regex]::Matches([regex]::Match($source,'(?s)_huffmanTable = new int\[514\]\s*\{(.*?)\};').Groups[1].Value,'0x[0-9A-Fa-f]+')|ForEach-Object{[Convert]::ToInt32($_.Value.Substring(2),16)})
$codes=@{};for($i=0;$i -le 256;$i++){$codes["$($values[$i*2]):$($values[$i*2+1])"]=$i}
$script:checks=0
function Check([string]$Name,[bool]$Condition){if(-not $Condition){throw "FAIL $Name"};$script:checks++;Write-Host "PASS $Name"}
function Put16([byte[]]$B,[int]$At,[long]$V){$B[$At]=($V-shr 8)-band 255;$B[$At+1]=$V-band 255}
function Put32([byte[]]$B,[int]$At,[long]$V){Put16 $B $At ($V-shr 16);Put16 $B ($At+2) $V}
function U16([byte[]]$B,[int]$At){[int]$B[$At]*256+$B[$At+1]}
function U32([byte[]]$B,[int]$At){[long](U16 $B $At)*65536+(U16 $B ($At+2))}
function Read-Exact($S,[int]$Count){$b=[byte[]]::new($Count);$at=0;while($at -lt $Count){$n=$S.Read($b,$at,$Count-$at);if($n -eq 0){throw 'premature socket EOF'};$at+=$n};return ,$b}
function Read-Packet($S){
    $plain=[Collections.Generic.List[byte]]::new();$bits=0;$value=0
    for($read=0;$read -lt 6000;$read++){
        $byte=$S.ReadByte();if($byte -lt 0){throw 'EOF inside Huffman packet'}
        for($bit=7;$bit -ge 0;$bit--){
            $value=($value-shl 1)-bor (($byte-shr $bit)-band 1);$bits++
            if($codes.ContainsKey("${bits}:$value")){
                $symbol=$codes["${bits}:$value"]
                if($symbol -eq 256){return ,$plain.ToArray()}
                $plain.Add([byte]$symbol);$bits=0;$value=0
            }elseif($bits -gt 11){throw 'Invalid Huffman prefix'}
        }
    }
    throw 'Huffman encoded budget'
}
function Login-Xor([byte[]]$Bytes,[uint64]$seed=0x01020304){
    [uint64]$mask=4294967295
    [uint64]$low=(((($seed-bxor $mask)-bxor 0x1357)-shl 16)-bor (($seed-bxor 0xFFFFAAAAUL)-band 65535))-band $mask
    [uint64]$high=(($seed-bxor 0x43210000)-shr 16)-bor ((($seed-bxor $mask)-bxor 0xABCDFFFFUL)-band 4294901760)
    $r=[byte[]]::new($Bytes.Length)
    for($i=0;$i -lt $Bytes.Length;$i++){
        $r[$i]=$Bytes[$i]-bxor ($low-band 255)
        $next=((($low-shr 1)-bor ($high-shl 31))-bxor 0x026950C6)-band $mask
        $high=((($high-shr 1)-bor ($low-shl 31))-bxor 0x389DE58C)-band $mask;$low=$next
    }
    return ,$r
}
function Open-Relay([string]$Name,[string]$Password=''){
    $plain=[byte[]]::new(214);$plain[0]=128;$plain[61]=100;$plain[62]=164;$plain[211]=160
    $n=[Text.Encoding]::ASCII.GetBytes($Name);$n.CopyTo($plain,1);for($i=0;$i -lt $n.Length;$i++){$plain[31+$i]=$n[$i]-13}
    if($Password){$w=[byte[]]::new(30);[Text.Encoding]::ASCII.GetBytes($Password).CopyTo($w,0);$w.CopyTo($plain,31)}
    $e=Login-Xor $plain
    $tcp=[Net.Sockets.TcpClient]::new('127.0.0.1',$Port)
    try{
        $s=$tcp.GetStream();$s.ReadTimeout=15000
        $s.Write([byte[]]@(1,2,3,4),0,4);$s.Write($e,0,62)
        $list=Read-Exact $s 46;Check "$Name shard list" ($list[0] -eq 168)
        $s.Write($e,62,149);$s.Write($e,211,3)
        $relay=Read-Exact $s 11;Check "$Name relay ticket" ($relay[0] -eq 140)
        return (U32 $relay 7)
    }finally{$tcp.Dispose()}
}
function Open-Game([string]$Name,[long]$Key,[string]$Password=''){
    $tcp=[Net.Sockets.TcpClient]::new('127.0.0.1',$Port);$s=$tcp.GetStream();$s.ReadTimeout=15000
    $p=[byte[]]::new(65);$p[0]=145;Put32 $p 1 $Key
    $n=[Text.Encoding]::ASCII.GetBytes($Name);$n.CopyTo($p,5);for($i=0;$i -lt $n.Length;$i++){$p[35+$i]=$n[$i]-13}
    if($Password){$w=[byte[]]::new(30);[Text.Encoding]::ASCII.GetBytes($Password).CopyTo($w,0);$w.CopyTo($p,35)}
    $seed=[byte[]]::new(4);Put32 $seed 0 $Key;$s.Write($seed,0,4);$s.Write((Login-Xor $p $Key),0,65)
    $plain=[Collections.Generic.List[byte]]::new();$plain.AddRange($p)
    $c=[pscustomobject]@{Name=$Name;Tcp=$tcp;Stream=$s;Key=$Key;Plain=$plain;Seen=[Collections.Generic.List[object]]::new();List=$null}
    $list=Read-Packet $s;$c.List=$list;Check "$Name game login lists characters" ($list[0] -eq 169)
    return $c
}
function Send-Plain($C,[byte[]]$Bytes){$o=$C.Plain.Count;$C.Plain.AddRange($Bytes);$x=Login-Xor $C.Plain.ToArray() $C.Key;$C.Stream.Write($x,$o,$Bytes.Length)}
function Drain($C,[int]$Ms){
    $C.Stream.ReadTimeout=$Ms;$got=[Collections.Generic.List[object]]::new();$until=[datetime]::UtcNow.AddMilliseconds($Ms)
    while([datetime]::UtcNow -lt $until){ try{ $p=Read-Packet $C.Stream;$got.Add($p);$C.Seen.Add($p) }catch [IO.IOException]{ break } }
    $C.Stream.ReadTimeout=15000;return ,$got.ToArray()
}
function Has($Packets,[int]$Op,[long]$Serial,[int]$At){ foreach($p in $Packets){ if($p.Length -gt $At+3 -and $p[0] -eq $Op -and ((U32 $p $At)-band 0x7FFFFFFF) -eq $Serial){return $true} };return $false }
function Enter($C,[string]$Avatar,[byte[]]$Packet=$null){
    $p=[byte[]]::new(100);Put32 $p 1 0xEDEDEDEDL;Put32 $p 5 4294967295
    [Text.Encoding]::ASCII.GetBytes($Avatar).CopyTo($p,10);$p[71]=30;$p[72]=20;$p[73]=15;$p[75]=50;$p[76]=1;$p[77]=50;$p[78]=2;Put16 $p 80 1002
    if($Packet){$p=$Packet}
    Send-Plain $C $p;$entered=Drain $C 4000
    Check "$($C.Name) enters the world" ($entered.Count -gt 0 -and $entered[0][0] -eq 27)
    return (U32 $entered[0] 1)
}
function Say([string]$Text){$t=[Text.Encoding]::ASCII.GetBytes($Text);$p=[byte[]]::new(9+$t.Length);$p[0]=3;Put16 $p 1 $p.Length;$p[3]=0;Put16 $p 4 0x3B2;Put16 $p 6 3;$t.CopyTo($p,8);return ,$p}
function Hex([string]$H){$b=[byte[]]::new($H.Length/2);for($i=0;$i -lt $b.Length;$i++){$b[$i]=[Convert]::ToByte($H.Substring($i*2,2),16)};return ,$b}
function Corpus-Entry([string]$Line){
    $name,$stream,$account,$password,$hex=$Line.Split('|')
    try{
        if($stream -eq 'login'){
            $tcp=[Net.Sockets.TcpClient]::new('127.0.0.1',$Port)
            try{$s=$tcp.GetStream();$s.ReadTimeout=3000;$s.Write([byte[]]@(1,2,3,4),0,4);$e=Login-Xor (Hex $hex);$s.Write($e,0,$e.Length)
                $got=0;try{$buf=[byte[]]::new(512);while(($r=$s.Read($buf,0,512)) -gt 0){$got+=$r}}catch [IO.IOException]{}
                return "$name answered $got bytes"}finally{$tcp.Dispose()}
        }
        $c=Open-Game $account (Open-Relay $account $password) $password
        try{if($hex){Send-Plain $c (Hex $hex)};$got=Drain $c 3000;return "$name answered $($got.Count) packets"}finally{$c.Tcp.Dispose()}
    }catch{return "$name refused: $($_.Exception.Message)"}
}$log=Join-Path $OutDir 'server.log';$err=Join-Path $OutDir 'guest.stderr';$option=Join-Path $OutDir 'launch.input'
[IO.File]::WriteAllText($option,$(if($Gate){"UOAIX TESTING`n"}else{"UOAIX DEV`n"}),[Text.Encoding]::ASCII)
$vm=if($Vm){(Resolve-Path $Vm).Path}else{Join-Path $repo 'tools/codex-vm.exe'}
if((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory -lt 1572864){throw 'RAM admission'}
$guest=Start-Process $vm -ArgumentList @('-kernel',('"'+$Artifact+'"'),'-disk',('"'+$StateFile+'"'),'-output',('"'+$log+'"'),'-headless','-mem','3072','-e1000-nat','-portfwd',"${Port}:2593",'-input',('"'+$option+'"')) -WindowStyle Hidden -PassThru -RedirectStandardError $err
@{pid=$guest.Id;guests=1;log=$log;port=$Port}|ConvertTo-Json|Set-Content (Join-Path $OutDir 'run.json')
"guest PID=$($guest.Id) port=$Port log=$log"
$clients=@()
try{
    $deadline=[datetime]::UtcNow.AddSeconds($StartupSeconds)
    while($true){
        $text=if(Test-Path $log){[IO.File]::ReadAllText($log)}else{''}
        if($text -match '(?m)^FAIL|!EXC|OUT OF MEMORY'){throw "startup failed: $text"}
        if($guest.HasExited){throw 'guest exited before listen'}
        if($text -match '(?m)^LISTEN game-server 0'){break}
        if([datetime]::UtcNow -gt $deadline){throw 'startup deadline'}
        Start-Sleep -Milliseconds 200
    }
    $a=Open-Game 'alphalink' (Open-Relay 'alphalink');$clients+=$a
    $b=Open-Game 'betalink' (Open-Relay 'betalink');$clients+=$b
    $sa=Enter $a 'Alphalink';$sb=Enter $b 'Betalink'
    Check 'two players hold different characters' ($sa -ne $sb)
    $da=Drain $a 3000
    Check 'alpha draws beta' (Has $a.Seen 0x78 $sb 3)
    Check 'beta draws alpha' (Has $b.Seen 0x78 $sa 3)
    $quiet=Drain $a 1500;$repeats=@($quiet|Group-Object {[BitConverter]::ToString($_)}|Where-Object Count -gt 1)
    "QUIET alpha received $($quiet.Count) packets in 1500 ms; repeated $($repeats.Count)"
    foreach($r in $repeats){"REPEATED x$($r.Count) $(($r.Name -split '-')[0..11] -join ' ')"}
    Check 'alpha is sent no packet twice while neither player acts' ($repeats.Count -eq 0)
    Send-Plain $b ([byte[]]@(2,2,0));$turn=Drain $b 1500;Send-Plain $b ([byte[]]@(2,2,1));$walk=Drain $b 1500;$moved=Drain $a 3000
    Check 'alpha sees beta move' ((Has $moved 0x77 $sb 1) -or (Has $moved 0x78 $sb 3))
    Send-Plain $a (Say 'hello beta');$echo=Drain $a 1500;$heard=Drain $b 3000
    Check 'beta hears alpha speak' (Has $heard 0x1C $sa 3)
    Send-Plain $a ([byte[]]@(114,1,0,50,0));$war=Drain $a 1500
    $attack=[byte[]]::new(5);$attack[0]=5;Put32 $attack 1 $sb;Send-Plain $a $attack;$fight=Drain $a 3000;$struck=Drain $b 3000
    "COMBAT alpha received opcodes " + (($fight|ForEach-Object{'{0:X2}' -f $_[0]}) -join ',') + "; beta received " + (($struck|ForEach-Object{'{0:X2}' -f $_[0]}) -join ',')
    Check 'beta sees alpha enter war mode' (Has $struck 0x77 $sa 1)
    if($Corpus){
        foreach($line in @(Get-Content -LiteralPath $Corpus | Where-Object {$_ -and -not $_.StartsWith('#')})){
            "CORPUS " + (Corpus-Entry $line)
            Send-Plain $a ([byte[]]@(0x73,0));$pong=Drain $a 1500
            Check "after corpus entry $(($line -split '\|')[0]) the shard still answers alpha" ((@($pong|Where-Object{$_[0] -eq 0x73}).Count -gt 0) -and -not $guest.HasExited)
        }
    }    if($Gate){
        # British summons a ghoul beside the entry tile and fights it; beta, in range, must be sent British's swings.
        $k=Open-Game 'British' (Open-Relay 'British' 'Astronaut') 'Astronaut';$clients+=$k
        $kslot=-1;for($i=0;$i -lt $k.List[3];$i++){if($kslot -lt 0 -and [Text.Encoding]::ASCII.GetString($k.List,4+$i*60,30).TrimEnd([char]0)){$kslot=$i}}
        $kname=[Text.Encoding]::ASCII.GetString($k.List,4+$kslot*60,30).TrimEnd([char]0)
        $play=[byte[]]::new(73);$play[0]=0x5D;Put32 $play 1 0xEDEDEDEDL;[Text.Encoding]::ASCII.GetBytes($kname).CopyTo($play,5);Put32 $play 65 $kslot;$play[69]=127;$play[72]=1
        $sk=Enter $k $kname $play
        Send-Plain $k (Say '[go 1421 1699');$moved=Drain $k 2500;$seen=Drain $b 1500
        Send-Plain $k (Say '[summon 1A');$summoned=Drain $k 3000
        $ghoul=0;$gat='';foreach($q in $summoned){if($q[0] -eq 0x78 -and (U16 $q 7) -eq 0x1A){$ghoul=(U32 $q 3)-band 0x7FFFFFFF;$gat="$(U16 $q 9),$(U16 $q 11),$([sbyte]$q[13])"}}
        Check 'British summons a ghoul beside the entry tile' ($ghoul -gt 0)
        $null=Drain $b 1500
        Send-Plain $k ([byte[]]@(114,1,0,50,0));$kwar=Drain $k 1000
        $hit=[byte[]]::new(5);$hit[0]=5;Put32 $hit 1 $ghoul;Send-Plain $k $hit
        $kfight=Drain $k 8000;$watched=Drain $b 3000
        $text=[IO.File]::ReadAllText($log)
        "FIGHT British=$sk ghoul=$ghoul at $gat beta received " + (($watched|ForEach-Object{'{0:X2}' -f $_[0]}) -join ',') + "; British sent own swing " + (Has $kfight 0x2F $sk 2) + "; British received " + (($kfight|ForEach-Object{'{0:X2}' -f $_[0]}) -join ',')
        Check 'beta, in range, is sent British''s swing at the ghoul over sockets' (Has $watched 0x2F $sk 2)
        Send-Plain $k ([byte[]]@(114,0,0,50,0));$kpeace=Drain $k 1000;Send-Plain $k (Say '[go 2704 692');$kgone=Drain $k 2500
        # The 0x00 a live client sent when creation was refused with "land not loaded" (play-hotfix2, ms 1224737,
        # 2026-10-06), decrypted; its trailing client address replaced by 127.0.0.1. Alpha stands beside the
        # Vesper gate with beta beside it, so no pulse pages the walk window back to the entry tile 1420,1698.
        Send-Plain $a (Say '[go 2702 692');$far=Drain $a 2000;Send-Plain $b (Say '[go 2703 692');$away=Drain $b 2000;$settled=Drain $a 1500
        $root=[byte[]]@(0,237,237,237,237,255,255,255,255,0,82,111,111,116,0,115,104,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,82,111,111,116,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,16,14,35,23,33,25,34,26,33,3,234,32,59,4,78,0,0,4,78,0,0,0,0,0,0,127,0,0,1)
        $g=Open-Game 'gammalink' (Open-Relay 'gammalink');$clients+=$g
        $sg=Enter $g 'Root' $root
        Check 'a new character is created while every other player stands far from the entry tile' ($sg -gt 0)
        # Testing mode's [go puts alpha east of the Britain gate (1336,1997); a turn and a step west enter it.
        Send-Plain $a (Say '[go 1337 1997');$went=Drain $a 2000
        Send-Plain $a ([byte[]]@(2,6,0));$turned=Drain $a 1500;Send-Plain $a ([byte[]]@(2,6,1));$gone=Drain $a 3000
        $text=[IO.File]::ReadAllText($log)
        Check 'alpha steps onto the Britain moongate and travels' ($text -match 'moongate destination=[0-9]')
    }
    foreach($c in $clients){Send-Plain $c ([byte[]]@(1,255,255,255,255))}
    Start-Sleep -Milliseconds 1500
    $text=[IO.File]::ReadAllText($log)
    Check 'server stayed up and logged no fault' (-not $guest.HasExited -and $text -notmatch '(?m)^FAIL|requires restart')
    "PASS all $script:checks checks"
}finally{
    foreach($c in $clients){$c.Tcp.Dispose()}
    if(-not $guest.HasExited){
        try{$h=[Threading.EventWaitHandle]::OpenExisting("Global\CodexVmShutdown_$($guest.Id)");try{[void]$h.Set()}finally{$h.Dispose()};[void]$guest.WaitForExit(8000)}catch{}
        if(-not $guest.HasExited){Stop-Process -Id $guest.Id -Force;[void]$guest.WaitForExit(5000)}
    }
    if(Test-Path $option){[IO.File]::Delete($option)}
}
