[CmdletBinding()]
param([Parameter(Mandatory)][string]$Artifact,[Parameter(Mandatory)][string]$World,
    [Parameter(Mandatory)][string]$CompressionSource,[Parameter(Mandatory)][string]$OutDir,
    [ValidateRange(1,7)][int]$Bots=6,[ValidateRange(1,1440)][int]$Minutes=120,[int]$Seed=1,
    [ValidateRange(30,900)][int]$StartupSeconds=300,[string]$Vm='',[string]$CoverLog='',[ValidateRange(1,100)][int]$ClockScale=1,
    [ValidateRange(0,3600)][int]$ErrandSeconds=900)
# Scripted-player soak (UOAIX-49 part B): N bot characters on N links drive every packet family
# against a COPY of a world disk through the real composite server, in testing mode, for -Minutes.
# Fatal: a server FAIL/!EXC/OUT OF MEMORY/"requires restart"/"SAVE REFUSED" line or the guest exiting. Every bot
# disconnect, every server REFUSE and every unanswered request is counted and named in soak.log.
# The guest runs under the host sampling profiler; coverage is functions sampled over functions in the map.
# -ClockScale N runs every guest timer N times faster (codex-vm -clock-scale, UOAIX-82); the soak always runs the
# server in testing mode, which is the only mode a scaled clock is for. -Minutes stays wall-clock minutes.
# British's own pulses pause his gold errand, and the NPC economy opens only when it banks (UOAIX-97), so bot 1 (British)
# logs in once the server logs the errand finished; not finished within -ErrandSeconds of listen is fatal. 0 skips the wait.
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$Artifact=(Resolve-Path -LiteralPath $Artifact).Path;$World=(Resolve-Path -LiteralPath $World).Path
$map=[IO.Path]::ChangeExtension($Artifact,'.map');if(-not (Test-Path $map)){throw "No symbol map beside the artifact: $map"}
if((Get-FileHash $CompressionSource).Hash -ne '807165537C00C83F295DC98F229F53D31460F055D1CC7FCBA9616E5966523042'){throw 'Wrong Huffman oracle source'}
$hash=[Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($repo.ToLowerInvariant()))
$Port=20000+(([BitConverter]::ToUInt16($hash,0)+7) % 10000)
if(Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue){throw "Port $Port is held; refusing to share it"}
$AdminPort=$Port+1
if(Get-NetTCPConnection -State Listen -LocalPort $AdminPort -ErrorAction SilentlyContinue){throw "Port $AdminPort is held; refusing to share it"}
$OutDir=[IO.Path]::GetFullPath($OutDir);if(Test-Path $OutDir){throw 'OutDir must be new'};[void](New-Item -ItemType Directory $OutDir)
$disk=Join-Path $OutDir 'world.disk';Copy-Item -LiteralPath $World $disk
$source=[IO.File]::ReadAllText((Resolve-Path $CompressionSource))
$values=@([regex]::Matches([regex]::Match($source,'(?s)_huffmanTable = new int\[514\]\s*\{(.*?)\};').Groups[1].Value,'0x[0-9A-Fa-f]+')|ForEach-Object{[Convert]::ToInt32($_.Value.Substring(2),16)})
$codes=@{};for($i=0;$i -le 256;$i++){$codes["$($values[$i*2]):$($values[$i*2+1])"]=$i}
$rng=[Random]::new($Seed)
$soakLog=Join-Path $OutDir 'soak.log'
function Note([string]$Text){$line="$([datetime]::UtcNow.ToString('HH:mm:ss')) $Text";Add-Content -LiteralPath $soakLog $line;Write-Output $line}
function Put16([byte[]]$B,[int]$At,[long]$V){$B[$At]=($V-shr 8)-band 255;$B[$At+1]=$V-band 255}
function Put32([byte[]]$B,[int]$At,[long]$V){Put16 $B $At ($V-shr 16);Put16 $B ($At+2) $V}
function U16([byte[]]$B,[int]$At){[int]$B[$At]*256+$B[$At+1]}
function U32([byte[]]$B,[int]$At){[long](U16 $B $At)*65536+(U16 $B ($At+2))}
function S8([int]$V){if($V -gt 127){$V-256}else{$V}}
function Read-Exact($S,[int]$Count){$b=[byte[]]::new($Count);$at=0;while($at -lt $Count){$n=$S.Read($b,$at,$Count-$at);if($n -eq 0){throw 'premature socket EOF'};$at+=$n};return ,$b}
function Read-Packet($S){
    $plain=[Collections.Generic.List[byte]]::new();$bits=0;$value=0
    for($read=0;$read -lt 65536;$read++){
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
        $list=Read-Exact $s 46;if($list[0] -ne 168){throw "relay shard list opcode $($list[0])"}
        $s.Write($e,62,149);$s.Write($e,211,3)
        $relay=Read-Exact $s 11;if($relay[0] -ne 140){throw "relay ticket opcode $($relay[0])"}
        return (U32 $relay 7)
    }finally{$tcp.Dispose()}
}
function Send-Plain($B,[byte[]]$Bytes){$o=$B.Plain.Count;$B.Plain.AddRange($Bytes);$x=Login-Xor $B.Plain.ToArray() $B.Key;$B.Stream.Write($x,$o,$Bytes.Length);$B.Sent++}
function Say([string]$Text){$t=[Text.Encoding]::ASCII.GetBytes($Text);$p=[byte[]]::new(9+$t.Length);$p[0]=3;Put16 $p 1 $p.Length;$p[3]=0;Put16 $p 4 0x3B2;Put16 $p 6 3;$t.CopyTo($p,8);return ,$p}
function Serial-Packet([int]$Op,[long]$Serial){$p=[byte[]]::new(5);$p[0]=$Op;Put32 $p 1 $Serial;return ,$p}
function Target-Ground($B,[int]$X,[int]$Y,[int]$Z){$p=[byte[]]::new(19);$p[0]=0x6C;$p[1]=1;Put32 $p 2 $B.Cursor;Put16 $p 11 $X;Put16 $p 13 $Y;$p[16]=$Z-band 255;return ,$p}
function Target-Object($B,[long]$Serial,[int]$X,[int]$Y,[int]$Z,[int]$Graphic){$p=[byte[]]::new(19);$p[0]=0x6C;$p[1]=0;Put32 $p 2 $B.Cursor;Put32 $p 7 $Serial;Put16 $p 11 $X;Put16 $p 13 $Y;$p[16]=$Z-band 255;Put16 $p 17 $Graphic;return ,$p}
$families=@('mint','walk','say','go','goto','vendor','bank','harvest','craft','carve','combat','moongate','inventory','click','magery','help','chop','skill','fighter','reap','sow',
    'camping','cartography','taste','forensics','spirit','poisoning','stealing','herding',
    'bard','tracking','healing','alchemy','inscription','arms','slay','smite','pick','pet','sell')
$fatalPattern='(?m)^.*(?:^FAIL|!EXC|OUT OF MEMORY|requires restart|SAVE REFUSED).*$'
$tally=@{};foreach($f in $families+@('death','ghost','buy','target','menu')){$tally[$f]=0}
$recvOps=@{};$disconnects=[Collections.Generic.List[string]]::new()
function New-Bot([int]$Index){
    [pscustomobject]@{Index=$Index;Name=$(if($Index -eq 1){'British'}else{'soakbot'+[char](96+$Index)});Password=$(if($Index -eq 1){'Astronaut'}else{''});Flora=@{};Crops=@{};Tcp=$null;Stream=$null;Key=0L;Plain=$null;Up=$false;Serial=0L;X=0;Y=0;Z=0;
        Seq=0;Pending=-1;Pack=0L;Cursor=0L;CursorAt=[datetime]::MinValue;Dead=$false;DeathAsked=$false;Mobiles=@{};Items=@{};Corpses=@{};Places=[Collections.Generic.List[string]]::new();
        BuyFrom=0L;Shop=@{};Sale=$null;Menu=$null;Blocked=@{};Held=@{};Sent=0;Received=0;NextAt=[datetime]::UtcNow;Last='login';Logins=0;Reconnect=[datetime]::UtcNow}
}
function Connect-Bot($B){
    $B.Logins++;$key=Open-Relay $B.Name $B.Password
    $tcp=[Net.Sockets.TcpClient]::new('127.0.0.1',$Port);$s=$tcp.GetStream();$s.ReadTimeout=15000
    $p=[byte[]]::new(65);$p[0]=145;Put32 $p 1 $key
    $n=[Text.Encoding]::ASCII.GetBytes($B.Name);$n.CopyTo($p,5);for($i=0;$i -lt $n.Length;$i++){$p[35+$i]=$n[$i]-13}
    if($B.Password){$w=[byte[]]::new(30);[Text.Encoding]::ASCII.GetBytes($B.Password).CopyTo($w,0);$w.CopyTo($p,35)}
    $seed=[byte[]]::new(4);Put32 $seed 0 $key;$s.Write($seed,0,4);$s.Write((Login-Xor $p $key),0,65)
    $B.Tcp=$tcp;$B.Stream=$s;$B.Key=$key;$B.Plain=[Collections.Generic.List[byte]]::new();$B.Plain.AddRange($p)
    $list=Read-Packet $s;if($list[0] -ne 169){throw "game login opcode $($list[0])"}
    $existing=$list.Length -gt 4 -and $list[4] -ne 0
    if($existing){$c=[byte[]]::new(73);$c[0]=0x5D;Put32 $c 1 0xEDEDEDEDL;$n.CopyTo($c,5);Put32 $c 65 0;$c[69]=127;$c[72]=1}
    else{$c=[byte[]]::new(100);Put32 $c 1 0xEDEDEDEDL;Put32 $c 5 4294967295;$n.CopyTo($c,10);$c[71]=30;$c[72]=20;$c[73]=15;$c[75]=50;$c[76]=1;$c[77]=50;$c[78]=2;Put16 $c 80 1002}
    Send-Plain $B $c
    $deadline=[datetime]::UtcNow.AddSeconds(15)
    while($B.Serial -eq 0 -and [datetime]::UtcNow -lt $deadline){Pump-Bot $B 1}
    if($B.Serial -eq 0){throw 'no 0x1B login confirm'}
    $B.Up=$true;$B.Dead=$false;$B.DeathAsked=$false;$B.Seq=0;$B.Pending=-1;$B.Mobiles=@{};$B.Items=@{};$B.Corpses=@{}
}
function Drop-Bot($B,[string]$Why){
    $disconnects.Add("$($B.Name) after '$($B.Last)': $Why");Note "DISCONNECT $($B.Name) after '$($B.Last)': $Why"
    try{if($B.Tcp){$B.Tcp.Dispose()}}catch{}
    $B.Up=$false;$B.Serial=0;$B.Reconnect=[datetime]::UtcNow.AddSeconds(5)
}
function Handle($B,[byte[]]$P){
    $op=$P[0];$recvOps[$op]=1+$(if($recvOps.ContainsKey($op)){$recvOps[$op]}else{0});$B.Received++
    switch($op){
        0x1B{$B.Serial=U32 $P 1;$B.X=U16 $P 11;$B.Y=U16 $P 13;$B.Z=S8 $P[16]}
        0x20{if((U32 $P 1) -eq $B.Serial){$B.X=U16 $P 11;$B.Y=U16 $P 13;$B.Z=S8 $P[18]}}
        0x21{if($B.Pending -ge 0){$B.Blocked["$($B.X),$($B.Y),$($B.Pending)"]=1};$B.X=U16 $P 2;$B.Y=U16 $P 4;$B.Z=S8 $P[7];$B.Seq=0;$B.Pending=-1}
        0x22{if($B.Pending -ge 0){$d=$B.Pending;$dx=@(0,1,1,1,0,-1,-1,-1)[$d];$dy=@(-1,-1,0,1,1,1,0,-1)[$d];$B.X+=$dx;$B.Y+=$dy;$B.Pending=-1}}
        0x78{$s=(U32 $P 3)-band 0x7FFFFFFF;$B.Mobiles[$s]=@((U16 $P 7),(U16 $P 9),(U16 $P 11))
            if($s -eq $B.Serial){$at=19;while($at+4 -le $P.Length){$item=U32 $P $at;if($item -eq 0){break};$g=U16 $P ($at+4);$layer=$P[$at+6];if($layer -eq 0x15){$B.Pack=$item};$at+=7+$(if($g -band 0x8000){2}else{0})}}}
        0x77{$s=U32 $P 1;$B.Mobiles[$s]=@((U16 $P 5),(U16 $P 7),(U16 $P 9))}
        0x1D{$s=U32 $P 1;$B.Mobiles.Remove($s);$B.Items.Remove($s);$B.Corpses.Remove($s)}
        0x1A{$s=U32 $P 3;$g=U16 $P 7;$at=9;if($s -band 0x80000000){$at=11};$s=$s-band 0x7FFFFFFF;$x=(U16 $P $at)-band 0x7FFF;$y=(U16 $P ($at+2))-band 0x3FFF
            if($g -eq 0x2006){$B.Corpses[$s]=@($x,$y)};if($s -ge 0x7C000001 -and $s -lt 0x7D000000){$B.Flora[$s]=@($x,$y,$g)};if($s -ge 0x7D000001 -and $s -lt 0x7E000000){$B.Crops[$s]=@($x,$y,$g)}}
        0x2E{if((U32 $P 9) -eq $B.Serial -and $P[8] -eq 0x15){$B.Pack=U32 $P 1};if($P[8] -eq 0x1A -or $P[8] -eq 0x1B){$B.Shop[(U32 $P 1)]=U32 $P 9}}
        0x3C{$count=U16 $P 3;for($i=0;$i -lt $count;$i++){$at=5+$i*19;if($at+19 -gt $P.Length){break};$B.Items[(U32 $P $at)]=@((U16 $P ($at+4)),(U32 $P ($at+13)))}}
        0x25{$B.Items[(U32 $P 1)]=@((U16 $P 5),(U32 $P 14))}
        0x6C{$B.Cursor=U32 $P 2;$B.CursorAt=[datetime]::UtcNow}
        0x2C{$B.Dead=$true;$B.DeathAsked=$true}
        0x74{$B.BuyFrom=U32 $P 3}
        0x9E{$B.Sale=$P}
        0x7C{$B.Menu=$P}
        0x1C{if($P.Length -gt 44){$t=[Text.Encoding]::ASCII.GetString($P,44,$P.Length-44).Trim([char]0)
            if($t -match 'successfully steal'){Count 'stolen'}
            if($t -match 'walks where it was instructed|begins to follow you'){Count 'herded'}
            if($t -match '^Killed\.'){Count 'killed'}
            if($t -match '^[A-Za-z ]+: (.+)$'){foreach($w in ($Matches[1] -split ' ')){if($w -and -not $B.Places.Contains($w)){$B.Places.Add($w)}}}}}
    }
}
function Pump-Bot($B,[int]$Max){
    $n=0
    while($n -lt $Max -and $B.Tcp.Available -gt 0){$p=Read-Packet $B.Stream;Handle $B $p;$n++}
    if($n -eq 0 -and $Max -eq 1){Start-Sleep -Milliseconds 20}
}
# Only the bot's own items: the container chain must reach its backpack (vendor stock and other containers are seen too).
function Owned($B,$Serial){$c=$B.Items[$Serial][1];for($i=0;$i -lt 4;$i++){if($B.Pack -ne 0 -and $c -eq $B.Pack){return $true};if(-not $B.Items.ContainsKey($c)){return $false};$c=$B.Items[$c][1]};return $false}
function Item-Of($B,[int]$Graphic){$m=@($B.Items.Keys|Where-Object{$B.Items[$_][0] -eq $Graphic -and (Owned $B $_)});if($m.Count -eq 0){return 0};return $m[$rng.Next($m.Count)]}
function Nearest($B,[scriptblock]$Want){$best=0;$far=99;foreach($k in $B.Mobiles.Keys){if($k -eq $B.Serial){continue};$m=$B.Mobiles[$k];if(-not (& $Want $m)){continue};$d=[Math]::Max([Math]::Abs($m[1]-$B.X),[Math]::Abs($m[2]-$B.Y));if($d -lt $far){$far=$d;$best=$k}};return $best}
function Walk($B,[int]$Dir){$B.Pending=$Dir;$seq=$B.Seq;$B.Seq=if($B.Seq -eq 255){1}else{$B.Seq+1};Send-Plain $B ([byte[]]@(2,$Dir,$seq))}
function Skill-Use([int]$Skill){$t=[Text.Encoding]::ASCII.GetBytes("$Skill 0");$p=[byte[]]::new(5+$t.Length);$p[0]=0x12;Put16 $p 1 $p.Length;$p[3]=0x24;$t.CopyTo($p,4);return ,$p}
function Use-Then-Target($B,[long]$Tool,[scriptblock]$Target,[string]$Family,[string]$Speech='',[byte[]]$Packet=$null){
    if($Tool -eq 0 -and -not $Speech -and -not $Packet){return}
    $before=$B.CursorAt;Send-Plain $B $(if($Packet){$Packet}elseif($Speech){Say $Speech}else{Serial-Packet 6 $Tool})
    $deadline=[datetime]::UtcNow.AddSeconds(2);while($B.CursorAt -eq $before -and [datetime]::UtcNow -lt $deadline){Pump-Bot $B 1}
    if($B.CursorAt -ne $before){Send-Plain $B (& $Target);$tally['target']++;Count "cursor-$Family"}else{$k="nocursor-$Family";$tally[$k]=1+$(if($tally.ContainsKey($k)){$tally[$k]}else{0})}
}
function Count([string]$Key){$tally[$Key]=1+$(if($tally.ContainsKey($Key)){$tally[$Key]}else{0})}
function Any-Owned($B){$m=@($B.Items.Keys|Where-Object{$_ -ne $B.Pack -and (Owned $B $_)});if($m.Count -eq 0){return 0};return $m[$rng.Next($m.Count)]}
# A skill item the bot carries; British (the only [add caller) summons one when it has none.
function Ensure-Item($B,[string]$Hex,[int]$Graphic,[int]$Amount,[string]$Family){
    $s=Item-Of $B $Graphic;if($s){return $s}
    if($B.Index -eq 1){Send-Plain $B (Say "[add $Hex $Amount");$deadline=[datetime]::UtcNow.AddSeconds(2);while(-not $s -and [datetime]::UtcNow -lt $deadline){Pump-Bot $B 1;$s=Item-Of $B $Graphic}}
    if(-not $s){Count "noitem-$Family"};return $s
}
# The second cursor of a two-step skill (poison then weapon, animal then place).
function Next-Target($B,[scriptblock]$Target,[string]$Family){
    $before=$B.CursorAt;$deadline=[datetime]::UtcNow.AddSeconds(2);while($B.CursorAt -eq $before -and [datetime]::UtcNow -lt $deadline){Pump-Bot $B 1}
    if($B.CursorAt -ne $before){Send-Plain $B (& $Target);$tally['target']++;Count "cursor-$Family"}else{Count "nocursor2-$Family"}
}
# British sets the skill a scenario needs so its success branch is reachable (testing mode; others keep their own levels).
function Prime($B,[string]$Skill){if($B.Index -eq 1){Send-Plain $B (Say "[skill $Skill 100")}}
# Answers an 0x7C menu with its first entry; a nested menu gets one more answer.
function Answer-Menu($B,[string]$Family,[int]$Depth=2){
    for($d=0;$d -lt $Depth;$d++){$B.Menu=$null;$deadline=[datetime]::UtcNow.AddSeconds(2);while($null -eq $B.Menu -and [datetime]::UtcNow -lt $deadline){Pump-Bot $B 1}
        if($null -eq $B.Menu){if($d -eq 0){Count "nomenu-$Family"};return}
        $m=$B.Menu;$tl=$m[9];$at=10+$tl;$count=$m[$at];if($count -le 0){return}
        $g=U16 $m ($at+1);$a=[byte[]]::new(13);$a[0]=0x7D;Put32 $a 1 (U32 $m 3);Put16 $a 5 (U16 $m 7);Put16 $a 7 1;Put16 $a 9 $g;Send-Plain $B $a;Count "menu-$Family"}
}
function Creature($B){return Nearest $B {param($m) $m[0] -lt 400}}
# Wields an item (0x07 lift, 0x13 equip); whatever the bot held in hand goes back to its pack first (0x07 lift, 0x08 drop),
# so a bow is never refused beside a dagger.
function Equip($B,[long]$Item,[int]$Layer){
    foreach($k in @($B.Held.Keys)){$h=$B.Held[$k];if($h -ne $Item -and $B.Pack){$l=[byte[]]::new(7);$l[0]=7;Put32 $l 1 $h;Put16 $l 5 1;Send-Plain $B $l
        $d=[byte[]]::new(14);$d[0]=8;Put32 $d 1 $h;Put16 $d 5 60;Put16 $d 7 80;Put32 $d 10 $B.Pack;Send-Plain $B $d};$B.Held.Remove($k)}
    $l=[byte[]]::new(7);$l[0]=7;Put32 $l 1 $Item;Put16 $l 5 1;Send-Plain $B $l
    $e=[byte[]]::new(10);$e[0]=0x13;Put32 $e 1 $Item;$e[5]=$Layer;Put32 $e 6 $B.Serial;Send-Plain $B $e;$B.Held[$Layer]=$Item;Pump-Bot $B 32
}
function Act($B){
    if($B.DeathAsked){$B.DeathAsked=$false;Send-Plain $B ([byte[]]@(44,1));$tally['death']++;$B.Last='death answer';return}
    # UOAIX-82: British drives the timer-delayed save-breaker triggers on a third of its actions (a world-spawn kill,
    # a spell death on another bot's account, an orchard pick), so a short scaled soak reaches all three.
    $f=if($B.Index -eq 1 -and $rng.Next(3) -eq 0){@('slay','smite','pick','pet')[$rng.Next(4)]}else{$families[$rng.Next($families.Count)]};$B.Last=$f;$tally[$f]++
    if($B.Dead -and $f -ne 'walk' -and $f -ne 'say'){$tally['ghost']++}
    switch($f){
        'walk'{for($i=0;$i -lt 3;$i++){$open=@(0..7|Where-Object{-not $B.Blocked.ContainsKey("$($B.X),$($B.Y),$_")});if($open.Count -eq 0){break};Walk $B $open[$rng.Next($open.Count)];Pump-Bot $B 16}}
        'say'{Send-Plain $B (Say (@('hail','hello there','guards','what news','vendor buy','bank','I accept')[$rng.Next(7)]))}
        'go'{$spot=@(@(1475,1645),@(1437,1696),@(1452,1529),@(1385,1487),@(1340,1997),@(1420,1698),@(1495,1620))[$rng.Next(7)];Send-Plain $B (Say "[go $($spot[0]) $($spot[1])")}
        'goto'{if($B.Places.Count -eq 0 -or $rng.Next(4) -eq 0){Send-Plain $B (Say '[goto')}else{Send-Plain $B (Say "[goto $($B.Places[$rng.Next($B.Places.Count)])")}}
        'vendor'{Send-Plain $B (Say '[go 1437 1695');Pump-Bot $B 64
            $humans=@($B.Mobiles.Keys|Where-Object{$_ -ne $B.Serial -and $B.Mobiles[$_][0] -ge 400 -and $B.Mobiles[$_][0] -le 401 -and [Math]::Max([Math]::Abs($B.Mobiles[$_][1]-$B.X),[Math]::Abs($B.Mobiles[$_][2]-$B.Y)) -le 8});$v=if($humans.Count -gt 0){$humans[$rng.Next($humans.Count)]}else{0}
            if($v){Send-Plain $B (Serial-Packet 9 $v);Send-Plain $B (Serial-Packet 6 $v)};Send-Plain $B (Say 'vendor buy')
            $deadline=[datetime]::UtcNow.AddSeconds(2);$B.BuyFrom=0;while($B.BuyFrom -eq 0 -and [datetime]::UtcNow -lt $deadline){Pump-Bot $B 1}
            # The cart goes to the vendor whose menu opened: 0x74 names its stock container, and the menu's 0x2E ties that
            # container (layer 1A or 1B) to the vendor. Skill items the other scenarios need are preferred over anything else.
            $seller=if($B.Shop.ContainsKey($B.BuyFrom)){$B.Shop[$B.BuyFrom]}else{0}
            if($seller){$lots=@($B.Items.Keys|Where-Object{$c=$B.Items[$_][1];$B.Shop.ContainsKey($c) -and $B.Shop[$c] -eq $seller})
                $tools=@($lots|Where-Object{@(0x0EC4,0x13F6,0x13E3,0x0E86,0x0E21,0x0FBF,0x0E34,0x0E9B,0x0F0E,0x0F85,0x0F84,0x0F7A,0x0EB3,0x14EC,0x0E81,0x0F43,0x13B2,0x0F3F) -contains $B.Items[$_][0]})
                $pick=@(if($tools.Count -gt 0){$tools}else{$lots});$lot=if($pick.Count -gt 0){$pick[$rng.Next($pick.Count)]}else{0}
                if($lot){$p=[byte[]]::new(15);$p[0]=0x3B;Put16 $p 1 15;Put32 $p 3 $seller;$p[7]=2;$p[8]=0x1A;Put32 $p 9 $lot;Put16 $p 13 1;Send-Plain $B $p;$tally['buy']++}else{Count 'nolot-vendor'}}
            elseif($B.BuyFrom -ne 0){Count 'noseller-vendor'}}
        'bank'{Send-Plain $B (Say '[go 1438 1695');Send-Plain $B (Say 'bank')}
        'inventory'{if($B.Pack){Send-Plain $B (Serial-Packet 6 $B.Pack);Pump-Bot $B 64;foreach($k in @($B.Items.Keys)){if($B.Items[$k][0] -eq 0x0E76 -and $B.Items[$k][1] -eq $B.Pack){Send-Plain $B (Serial-Packet 6 $k)}}}else{Send-Plain $B (Serial-Packet 6 ($B.Serial-bor 0x80000000))}}
        'harvest'{Send-Plain $B (Say '[go 1452 1529');Pump-Bot $B 64;Use-Then-Target $B (Item-Of $B 0x0E86) {Target-Ground $B 1451 1528 40} 'harvest'}
        'craft'{$kinds=@(@(0x13E3,7),@(0x0F9D,34),@(0x1034,11),@(0x1EB8,37),@(0x097F,13),@(0x1043,13),@(0x1022,8));$kind=$kinds[$rng.Next($kinds.Count)];$h=Item-Of $B $kind[0]
            if($B.Index -eq 1){foreach($m in @(@('1BF2',0x1BF2),@('1BD7',0x1BD7),@('1766',0x1766),@('097A',0x097A),@('1BDD',0x1BDD))){if(-not (Item-Of $B $m[1])){[void](Ensure-Item $B $m[0] $m[1] 20 'craft')}}}
            if($h){if($kind[0] -eq 0x13E3){Send-Plain $B (Say '[go 1418 1547');Pump-Bot $B 64};Prime $B "$($kind[1])";Send-Plain $B (Serial-Packet 6 $h);Answer-Menu $B ('craft-{0:X4}' -f $kind[0]) 2}
            else{Count ('noitem-craft-{0:X4}' -f $kind[0])}}
        'carve'{$c=0;foreach($k in $B.Corpses.Keys){$c=$k;break};if($c){$at=$B.Corpses[$c];Send-Plain $B (Say "[go $($at[0]) $($at[1])");Pump-Bot $B 64;Use-Then-Target $B (Item-Of $B 0x0EC4) {Target-Object $B $c $at[0] $at[1] 0 0x2006} 'carve'}}
        'combat'{$t=Nearest $B {param($m) $m[0] -lt 400 -or $m[0] -gt 403};if(-not $t){$t=Nearest $B {param($m) $true}}
            if($t){Send-Plain $B ([byte[]]@(114,1,0,50,0));Send-Plain $B (Serial-Packet 5 $t);$m=$B.Mobiles[$t]
                for($i=0;$i -lt 4;$i++){$dx=[Math]::Sign($m[1]-$B.X);$dy=[Math]::Sign($m[2]-$B.Y);if($dx -eq 0 -and $dy -eq 0){break};Walk $B ([Array]::IndexOf(@('0,-1','1,-1','1,0','1,1','0,1','-1,1','-1,0','-1,-1'),"$dx,$dy"))}}
            else{Send-Plain $B (Say '[go 1385 1487')}}
        'moongate'{Send-Plain $B (Say '[go 1337 1997');Pump-Bot $B 64;Walk $B 6;Walk $B 6}
        'magery'{Prime $B 'magery';$n=1+$rng.Next(64);$txt=[Text.Encoding]::ASCII.GetBytes("$n");$p=[byte[]]::new(5+$txt.Length);$p[0]=0x12;Put16 $p 1 $p.Length;$p[3]=0x56;$txt.CopyTo($p,4);$before=$B.CursorAt;Send-Plain $B $p
            $deadline=[datetime]::UtcNow.AddSeconds(4);while($B.CursorAt -eq $before -and [datetime]::UtcNow -lt $deadline){Pump-Bot $B 1}
            if($B.CursorAt -ne $before){$t=Nearest $B {param($m) $true};$pick=$rng.Next(4);$i=Any-Owned $B
                if($pick -eq 0 -and $t){$m=$B.Mobiles[$t];Send-Plain $B (Target-Object $B $t $m[1] $m[2] 0 $m[0])}
                elseif($pick -eq 1){Send-Plain $B (Target-Ground $B ($B.X+2) $B.Y $B.Z);Count 'ground-magery'}
                elseif($pick -eq 2 -and $i){Send-Plain $B (Target-Object $B $i 0 0 0 $B.Items[$i][0]);Count 'item-magery'}
                else{Send-Plain $B (Target-Object $B $B.Serial $B.X $B.Y $B.Z 400)};$tally['target']++}}
        'mint'{Send-Plain $B (Say '[go 1333 1603');Pump-Bot $B 64;$m=@($B.Mobiles.Keys|Where-Object{$B.Mobiles[$_][1] -eq 1334 -and $B.Mobiles[$_][2] -eq 1603});$i=Ensure-Item $B '1BF2' 0x1BF2 3 'mint'
            if($m.Count -gt 0 -and $i){$l=[byte[]]::new(7);$l[0]=7;Put32 $l 1 $i;Put16 $l 5 3;Send-Plain $B $l;$d=[byte[]]::new(14);$d[0]=8;Put32 $d 1 $i;Put16 $d 5 0xFFFF;Put16 $d 7 0xFFFF;Put32 $d 10 $m[0];Send-Plain $B $d;Count 'drop-mint'}else{Count 'nominter-mint'}}
        'help'{$p=[byte[]]::new(258);$p[0]=0x9B;Send-Plain $B $p}
        'chop'{$t=0;$far=99;foreach($k in $B.Flora.Keys){$f=$B.Flora[$k];$d=[Math]::Max([Math]::Abs($f[0]-$B.X),[Math]::Abs($f[1]-$B.Y));if($d -lt $far){$far=$d;$t=$k}}
            if(-not $t){Send-Plain $B (Say '[go 1420 1698')}else{$f=$B.Flora[$t];Send-Plain $B (Say "[go $($f[0]+1) $($f[1])");Pump-Bot $B 64;Use-Then-Target $B (Item-Of $B 0x0F43) {Target-Object $B $t $f[0] $f[1] 0 $f[2]} 'chop'}}
        'skill'{$v="$($rng.Next(101)).$($rng.Next(10))"
            if($B.Index -ne 1){Send-Plain $B (Say "[skill all $v")}
            else{$names=@('alchemy','anatomy','magery','mining','tactics','swordsmanship','lumberjacking','healing','archery','tailoring');$form=$rng.Next(4)
                if($form -eq 0){Send-Plain $B (Say "[skill all $v")}elseif($form -eq 1){Send-Plain $B (Say "[skill $($names[$rng.Next($names.Count)]) $v")}elseif($form -eq 2){Send-Plain $B (Say "[skill $($rng.Next(46)) $v")}
                else{Use-Then-Target $B 0 {$t=Nearest $B {param($m) $true};if($t -and $rng.Next(2) -eq 0){$m=$B.Mobiles[$t];Target-Object $B $t $m[1] $m[2] 0 $m[0]}else{Target-Object $B $B.Serial $B.X $B.Y $B.Z 400}} 'skill' -Speech "[skill $($names[$rng.Next($names.Count)]) $v target"}}}
        'reap'{$keys=@($B.Crops.Keys);if($keys.Count -eq 0){Send-Plain $B (Say '[go 1229 1588')}
            else{$c=$keys[$rng.Next($keys.Count)];$at=$B.Crops[$c];Send-Plain $B (Say "[go $($at[0]) $($at[1]+1)");Pump-Bot $B 64;Send-Plain $B (Serial-Packet 6 $c)}}
        'sow'{$s=Item-Of $B 0x0C68;if(-not $s){$s=Item-Of $B 0x0CE9};if($s){$x=1222+$rng.Next(15);$y=@(1588,1589,1591,1592)[$rng.Next(4)];Send-Plain $B (Say "[go $x $($y-1)");Pump-Bot $B 64;Use-Then-Target $B $s {Target-Ground $B $x $y 0} 'sow'}else{$tally['nocursor-sow']=1+$(if($tally.ContainsKey('nocursor-sow')){$tally['nocursor-sow']}else{0})}}
        'fighter'{Send-Plain $B (Say '[go 1385 1487');Pump-Bot $B 64;Send-Plain $B ([byte[]]@(114,1,0,50,0))
            $bots=@($fleet|ForEach-Object Serial);$t=Nearest $B {param($m) $m[0] -ge 400 -and $m[0] -le 401};if($t -and $bots -notcontains $t){Send-Plain $B (Serial-Packet 5 $t)}}
        'click'{$t=Nearest $B {param($m) $true};if($t){Send-Plain $B (Serial-Packet 9 $t)};$s=[byte[]]::new(10);$s[0]=0x34;Put32 $s 1 0xEDEDEDEDL;$s[5]=4;Put32 $s 6 $B.Serial;Send-Plain $B $s}
        'camping'{$k=Item-Of $B 0x0DE1;$knife=Item-Of $B 0x0EC4;$tree=0;$far=99;foreach($t in $B.Flora.Keys){$f=$B.Flora[$t];$d=[Math]::Max([Math]::Abs($f[0]-$B.X),[Math]::Abs($f[1]-$B.Y));if($d -lt $far){$far=$d;$tree=$t}}
            if(-not $k -and $knife -and $tree){$f=$B.Flora[$tree];Send-Plain $B (Say "[go $($f[0]+1) $($f[1])");Pump-Bot $B 64;Use-Then-Target $B $knife {Target-Object $B $tree $f[0] $f[1] 0 $f[2]} 'kindling'}
            else{if(-not $k){$k=Ensure-Item $B '0DE1' 0x0DE1 3 'camping'};if($k){$l=[byte[]]::new(7);$l[0]=7;Put32 $l 1 $k;Send-Plain $B $l;$d=[byte[]]::new(14);$d[0]=8;Put32 $d 1 $k;Put16 $d 5 $B.X;Put16 $d 7 $B.Y;$d[9]=[byte]($B.Z -band 255);Put32 $d 10 0xFFFFFFFF;Send-Plain $B $d;Pump-Bot $B 16;Send-Plain $B (Serial-Packet 6 $k)}}}
        'cartography'{$drawn=Item-Of $B 0x14EB
            if($drawn -and $rng.Next(2) -eq 0){Send-Plain $B (Serial-Packet 6 $drawn)}
            else{Prime $B 'cartography';$blank=Ensure-Item $B '14EC' 0x14EC 2 'cartography';$pen=Ensure-Item $B '0FBF' 0x0FBF 1 'cartography'
                if($rng.Next(2) -eq 0){Send-Plain $B (Skill-Use 12);Count 'button-cartography'}elseif($blank -and $pen){Send-Plain $B (Serial-Packet 6 $blank)}}}
        'taste'{Use-Then-Target $B 0 {$i=Any-Owned $B;if($i){Target-Object $B $i 0 0 0 $B.Items[$i][0]}else{Target-Object $B $B.Serial $B.X $B.Y $B.Z 400}} 'taste' -Packet (Skill-Use 36)}
        'forensics'{$c=0;foreach($k in $B.Corpses.Keys){$c=$k;break}
            if($c){$at=$B.Corpses[$c];Send-Plain $B (Say "[go $($at[0]) $($at[1])");Pump-Bot $B 64;Use-Then-Target $B 0 {Target-Object $B $c $at[0] $at[1] 0 0x2006} 'forensics' -Packet (Skill-Use 19)}
            else{Use-Then-Target $B 0 {if($B.Pack){Target-Object $B $B.Pack 0 0 0 0x0E75}else{Target-Object $B $B.Serial $B.X $B.Y $B.Z 400}} 'forensics' -Packet (Skill-Use 19)}}
        'spirit'{Send-Plain $B (Skill-Use 32)}
        'poisoning'{$potion=Ensure-Item $B '0F0A' 0x0F0A 1 'poisoning'
            Use-Then-Target $B 0 {$i=if($potion){$potion}else{Any-Owned $B};if($i){Target-Object $B $i 0 0 0 $B.Items[$i][0]}else{Target-Object $B $B.Serial $B.X $B.Y $B.Z 400}} 'poisoning' -Packet (Skill-Use 30)
            Next-Target $B {$w=Any-Owned $B;if($w){Target-Object $B $w 0 0 0 $B.Items[$w][0]}else{Target-Object $B $B.Serial $B.X $B.Y $B.Z 400}} 'poisoning'}
        'stealing'{Prime $B 'stealing';$bots=@($fleet|ForEach-Object Serial)
            $people=@($B.Mobiles.Keys|Where-Object{$_ -ne $B.Serial -and $bots -notcontains $_ -and $B.Mobiles[$_][0] -ge 400 -and $B.Mobiles[$_][0] -le 401})
            if($people.Count -eq 0 -and $rng.Next(2) -eq 0){Send-Plain $B (Say "[go $(1490+$rng.Next(10)) $(1610+$rng.Next(10))");Pump-Bot $B 128;$people=@($B.Mobiles.Keys|Where-Object{$_ -ne $B.Serial -and $bots -notcontains $_ -and $B.Mobiles[$_][0] -ge 400 -and $B.Mobiles[$_][0] -le 401})}
            $t=if($people.Count -gt 0){$people[$rng.Next($people.Count)]}else{Creature $B}
            if(-not $t -or $bots -contains $t){Send-Plain $B (Say "[go $(1340+$rng.Next(30)) $(1450+$rng.Next(50))");Pump-Bot $B 128;$t=Creature $B}
            if($t -and $bots -notcontains $t){$m=$B.Mobiles[$t];Send-Plain $B (Say "[go $($m[1]+1) $($m[2])");Pump-Bot $B 64;$m=$B.Mobiles[$t];if($m){Use-Then-Target $B 0 {Target-Object $B $t $m[1] $m[2] 0 $m[0]} 'stealing' -Packet (Skill-Use 33)}}else{Count 'notarget-stealing'}}
        'herding'{$crook=Ensure-Item $B '0E81' 0x0E81 1 'herding'
            if($crook){Prime $B 'herding';Send-Plain $B (Say "[go $(1340+$rng.Next(100)) $(1512+$rng.Next(20))");Pump-Bot $B 128
                $near=@($B.Mobiles.Keys|Where-Object{$_ -ne $B.Serial -and $B.Mobiles[$_][0] -lt 400});$t=if($near.Count -gt 0){$near[$rng.Next($near.Count)]}else{0}
                if($t){$m=$B.Mobiles[$t];Send-Plain $B (Say "[go $($m[1]+1) $($m[2])");Pump-Bot $B 64}
                if($t -and $B.Mobiles.ContainsKey($t)){$m=$B.Mobiles[$t];Use-Then-Target $B $crook {Target-Object $B $t $m[1] $m[2] 0 $m[0]} 'herding';Next-Target $B {Target-Ground $B ($m[1]+3) $m[2] $B.Z} 'herding'}else{Count 'notarget-herding'}}}
        'bard'{$lute=Ensure-Item $B '0EB3' 0x0EB3 1 'bard'
            if($lute){$skill=@(9,22,15,28)[$rng.Next(4)];$name=@{9='peacemaking';22='provocation';15='enticement';28='snooping'}[$skill];Prime $B $name
                if($skill -ne 28){Send-Plain $B (Say '[goto britspawn');Pump-Bot $B 64}
                $t=if($skill -eq 28){Nearest $B {param($m) $true}}else{Creature $B}
                if($t){$m=$B.Mobiles[$t];Use-Then-Target $B 0 {Target-Object $B $t $m[1] $m[2] 0 $m[0]} "bard-$name" -Packet (Skill-Use $skill)
                    if($skill -eq 22 -or $skill -eq 15){$u=Nearest $B {param($m2) $m2[0] -lt 400};Next-Target $B {if($skill -eq 15 -or -not $u -or -not $B.Mobiles.ContainsKey($u)){Target-Object $B $B.Serial $B.X $B.Y $B.Z 400}else{$o=$B.Mobiles[$u];Target-Object $B $u $o[1] $o[2] 0 $o[0]}} "bard-$name"}}
                else{Count 'notarget-bard'}}}
        'tracking'{Prime $B 'tracking';Send-Plain $B (Skill-Use 38);Answer-Menu $B 'tracking' 2}
        'healing'{$band=Ensure-Item $B '0E21' 0x0E21 10 'healing';if(-not $band){Count 'noitem-healing'}
            else{Prime $B 'healing';$t=Nearest $B {param($m) $true};Use-Then-Target $B $band {if($t -and $rng.Next(2) -eq 0){$o=$B.Mobiles[$t];Target-Object $B $t $o[1] $o[2] 0 $o[0]}else{Target-Object $B $B.Serial $B.X $B.Y $B.Z 400}} 'healing'}}
        'alchemy'{$mortar=Ensure-Item $B '0E9B' 0x0E9B 1 'alchemy';$bottle=Ensure-Item $B '0F0E' 0x0F0E 3 'alchemy'
            $reagent=@('0F85','0F84','0F7A')[$rng.Next(3)];$r=Ensure-Item $B $reagent ([Convert]::ToInt32($reagent,16)) 5 'alchemy'
            if($mortar -and $r){Prime $B 'alchemy';Use-Then-Target $B $mortar {Target-Object $B $r 0 0 0 $B.Items[$r][0]} 'alchemy'}
            $potion=@(@(0x0F0C,0x0F07,0x0F0B)|ForEach-Object{Item-Of $B $_}|Where-Object{$_})|Select-Object -First 1;if($potion){Send-Plain $B (Serial-Packet 6 $potion);Count 'drink-alchemy'}}
        'inscription'{$pen=Ensure-Item $B '0FBF' 0x0FBF 1 'inscription';$scroll=Ensure-Item $B '0E34' 0x0E34 5 'inscription';$book=Ensure-Item $B '0EFA' 0x0EFA 1 'inscription'
            if($pen -and $scroll){Prime $B 'inscription';Use-Then-Target $B $pen {Target-Object $B $scroll 0 0 0 0x0E34} 'inscription';Answer-Menu $B 'inscription' 2}}
        # A bow and arrows (archery: range, ammunition, line of sight), a heater shield (Parrying when the prey strikes back)
        # or the kit dagger (a coated one poisons); then the bot fights the nearest creature.
        'arms'{$kind=$rng.Next(3);$w=0;$layer=1
            if($kind -eq 0){$w=Ensure-Item $B '13B2' 0x13B2 1 'arms-bow';[void](Ensure-Item $B '0F3F' 0x0F3F 30 'arms-arrows');$layer=2;Prime $B 'archery'}
            elseif($kind -eq 1){$w=Ensure-Item $B '1B76' 0x1B76 1 'arms-shield';$layer=2;Prime $B 'parrying'}
            else{$w=Item-Of $B 0x0F52;if(-not $w){Count 'noitem-arms-dagger'}}
            if($w){Equip $B $w $layer;Count "equip-arms-$kind";Send-Plain $B (Say '[goto britspawn');Pump-Bot $B 64;$t=Creature $B
                if($t){Send-Plain $B ([byte[]]@(114,1,0,50,0));Send-Plain $B (Serial-Packet 5 $t);Pump-Bot $B 64}else{Count 'notarget-arms'}}}
        'slay'{if($B.Index -ne 1){return};Send-Plain $B (Say '[goto britspawn');Pump-Bot $B 64;$t=Creature $B;$until=[datetime]::UtcNow.AddSeconds(5);while(-not $t -and [datetime]::UtcNow -lt $until){Pump-Bot $B 8;$t=Creature $B}
            if($t){$m=$B.Mobiles[$t];Use-Then-Target $B 0 {Target-Object $B $t $m[1] $m[2] 0 $m[0]} 'slay' -Speech '[kill'}else{Count 'notarget-slay'}}
        'pet'{if($B.Index -ne 1){return};Send-Plain $B (Say '[skill 35 100');Send-Plain $B (Say '[goto britspawn');Pump-Bot $B 64;$t=Creature $B;$until=[datetime]::UtcNow.AddSeconds(5);while(-not $t -and [datetime]::UtcNow -lt $until){Pump-Bot $B 8;$t=Creature $B}
            if($t){$m=$B.Mobiles[$t];Use-Then-Target $B 0 {Target-Object $B $t $m[1] $m[2] 0 $m[0]} 'pet' -Packet (Skill-Use 35);Pump-Bot $B 32;foreach($o in @('all follow me','all guard me','all stay')){Send-Plain $B (Say $o);Pump-Bot $B 8;Count "order-pet"}}else{Count 'notarget-pet'}}
        'smite'{if($B.Index -ne 1){return};$v=@($fleet|Where-Object{$_.Index -ne 1 -and $_.Up -and -not $_.Dead -and $_.Serial})
            if($v.Count -eq 0){Count 'notarget-smite';return};$o=$v[$rng.Next($v.Count)];Send-Plain $B (Say "[go $($o.X+1) $($o.Y)");Pump-Bot $B 64
            Prime $B 'magery';Send-Plain $B (Say '[attr int 100');Send-Plain $B (Say '[attr mana 100')
            $txt=[Text.Encoding]::ASCII.GetBytes('18');$p=[byte[]]::new(5+$txt.Length);$p[0]=0x12;Put16 $p 1 $p.Length;$p[3]=0x56;$txt.CopyTo($p,4)
            for($i=0;$i -lt 6 -and -not $o.Dead;$i++){$before=$B.CursorAt;Send-Plain $B $p;$deadline=[datetime]::UtcNow.AddSeconds(4);while($B.CursorAt -eq $before -and [datetime]::UtcNow -lt $deadline){Pump-Bot $B 1}
                if($B.CursorAt -eq $before){Count 'nocursor-smite';break};Send-Plain $B (Target-Object $B $o.Serial $o.X $o.Y $o.Z 400);Count 'cast-smite';Pump-Bot $B 32;Pump-Bot $o 64}}
        # Selling is how a bot other than British earns gold: 0x9E lists the pack items the vendor bids on (serial, graphic,
        # hue, quantity, price, a 2-byte name length, the name), and the 0x9F reply sells one line (serial, quantity).
        'sell'{$spot=@(@(1437,1695),@(1418,1547))[$rng.Next(2)];Send-Plain $B (Say "[go $($spot[0]) $($spot[1])");Pump-Bot $B 64
            $B.Sale=$null;Send-Plain $B (Say 'vendor sell');$deadline=[datetime]::UtcNow.AddSeconds(2);while($null -eq $B.Sale -and [datetime]::UtcNow -lt $deadline){Pump-Bot $B 1}
            $s=$B.Sale;if($null -eq $s -or $s.Length -lt 9 -or (U16 $s 7) -lt 1){Count 'nooffer-sell';return}
            $lines=[Collections.Generic.List[int]]::new();$at=9;for($i=0;$i -lt (U16 $s 7) -and $at+14 -le $s.Length;$i++){$lines.Add($at);$at+=14+(U16 $s ($at+12))}
            $at=$lines[$rng.Next($lines.Count)];$quantity=U16 $s ($at+8)
            $p=[byte[]]::new(15);$p[0]=0x9F;Put16 $p 1 15;Put32 $p 3 (U32 $s 3);Put16 $p 7 1;Put32 $p 9 (U32 $s $at);Put16 $p 13 (1+$rng.Next([Math]::Max(1,$quantity)));Send-Plain $B $p;Count 'sold-sell'}
        'pick'{Send-Plain $B (Say '[go 1230 1591');Pump-Bot $B 64;$c=@($B.Crops.Keys|Where-Object{$B.Crops[$_][0] -eq 1230 -and $B.Crops[$_][1] -eq 1590})
            if($c.Count -gt 0){Send-Plain $B (Serial-Packet 6 $c[0]);Count 'pick-orchard'}else{Count 'notarget-pick'}}
    }
}
$log=Join-Path $OutDir 'server.log';$err=Join-Path $OutDir 'guest.stderr';$option=Join-Path $OutDir 'launch.input';$prof=Join-Path $OutDir 'prof.txt'
# The owner drives the admin port too (UOAIX-49 B): keys as start-composite-game.ps1 -Admin provisions them (health,
# mind, keeper, human, epoch); the owner is the human role 4. Only read-only panel commands run, so the world is unchanged.
$adminKeys=@(1..5|ForEach-Object{[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLowerInvariant()})
$ownerKey=[Convert]::FromHexString($adminKeys[3])
$adminRequest=[Security.Cryptography.HMACSHA256]::HashData($ownerKey,[Text.Encoding]::UTF8.GetBytes("UOAIX1/request/4/$($adminKeys[4])"))
$adminResponse=[Security.Cryptography.HMACSHA256]::HashData($ownerKey,[Text.Encoding]::UTF8.GetBytes("UOAIX1/response/4/$($adminKeys[4])"))
$adminHttp=[Net.Http.HttpClient]::new();$adminHttp.Timeout=[TimeSpan]::FromSeconds(10);$adminHttp.DefaultRequestHeaders.ConnectionClose=$true
$admin=@{Sequence=0;Ceiling=0;Session=0;NextAt=[datetime]::MinValue;Turn=0}
$adminCommands=@('panel-data','treasury-view','panel-levels','gm-list','gm-log','panel-log','panel-cover')
function Admin-Post([string]$Path,[string]$Body){
    $request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post,"http://127.0.0.1:$AdminPort$Path")
    $request.Content=[Net.Http.StringContent]::new($Body,[Text.Encoding]::ASCII,'text/plain')
    try{$response=$adminHttp.Send($request);try{return @{status=[int]$response.StatusCode;body=$response.Content.ReadAsStringAsync().GetAwaiter().GetResult()}}finally{$response.Dispose()}}finally{$request.Dispose()}
}
function Admin-Tag([byte[]]$Secret,[string]$Text){[Convert]::ToHexString([Security.Cryptography.HMACSHA256]::HashData($Secret,[Text.Encoding]::UTF8.GetBytes($Text))).ToLowerInvariant()}
function Admin-Reserve{
    $challenge=[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLowerInvariant()
    $label="UOAIX1/sequence/4/$challenge"
    $probe=Admin-Post '/handshake' "4`n$challenge`n$(Admin-Tag $adminRequest $label)";$parts=$probe.body.Split("`n")
    if($probe.status -ne 200 -or $parts.Length -ne 2 -or $parts[1] -cne (Admin-Tag $adminResponse "$label/$($parts[0])")){throw "admin probe $($probe.status)"}
    $floor=[long]$parts[0];$label="UOAIX1/reserve/4/$challenge/$floor"
    $reserve=Admin-Post '/handshake' "4`n$challenge`n$floor`n$(Admin-Tag $adminRequest $label)";$parts=$reserve.body.Split("`n")
    if($reserve.status -ne 200 -or $parts.Length -ne 2 -or $parts[1] -cne (Admin-Tag $adminResponse $label)){throw "admin reserve $($reserve.status)"}
    $admin.Sequence=$floor;$admin.Ceiling=$floor+1024
}
function Admin-Send([string]$Json){
    if($admin.Sequence -ge $admin.Ceiling){Admin-Reserve}
    $admin.Sequence++;$seq=$admin.Sequence
    $nonce=[byte[]]::new(12);[BitConverter]::GetBytes([uint64]$seq).CopyTo($nonce,4)
    $plain=[Text.Encoding]::UTF8.GetBytes($Json);$cipher=[byte[]]::new($plain.Length);$tag=[byte[]]::new(16)
    $aes=[Security.Cryptography.AesGcm]::new($adminRequest,16);try{$aes.Encrypt($nonce,$plain,$cipher,$tag,[Text.Encoding]::UTF8.GetBytes("UOAIX1/request/4/$seq"))}finally{$aes.Dispose()}
    $reply=Admin-Post '/admin' "4`n$seq`n$([Convert]::ToHexString($cipher).ToLowerInvariant())`n$([Convert]::ToHexString($tag).ToLowerInvariant())"
    $parts=$reply.body.Split("`n");if($reply.status -ne 200 -or $parts.Length -ne 2){throw "admin $($reply.status)"}
    $cipher=[Convert]::FromHexString($parts[0]);$plain=[byte[]]::new($cipher.Length)
    $aes=[Security.Cryptography.AesGcm]::new($adminResponse,16);try{$aes.Decrypt($nonce,$cipher,[Convert]::FromHexString($parts[1]),$plain,[Text.Encoding]::UTF8.GetBytes("UOAIX1/response/4/$seq"))}finally{$aes.Dispose()}
    return [Text.Encoding]::UTF8.GetString($plain)
}
function Admin-Step{
    if([datetime]::UtcNow -lt $admin.NextAt){return};$admin.NextAt=[datetime]::UtcNow.AddSeconds(2)
    try{
        if($admin.Ceiling -eq 0){Admin-Reserve}
        if($admin.Session -eq 0){$admin.Session=[long]((Admin-Send '{"command":"panel-open"}')|ConvertFrom-Json).session;Count 'admin-panel-open';return}
        $c=$adminCommands[$admin.Turn % $adminCommands.Count];$admin.Turn++
        $extra=if($c -eq 'gm-log'){',"before":0'}elseif($c -eq 'panel-log'){',"before":1'}else{''}
        $text=Admin-Send ('{"command":"'+$c+'","session":'+$admin.Session+$extra+'}')
        if($text -match '^\{"error"'){Count "adminerr-$c"}else{Count "admin-$c"}
    }catch{Count 'admin-refused';$admin.Session=0;$admin.Ceiling=0}
}
[IO.File]::WriteAllText($option,"ADMIN $($adminKeys -join ' ')`nUOAIX TESTING`n",[Text.Encoding]::ASCII)
$vmPath=if($Vm){(Resolve-Path $Vm).Path}else{Join-Path $repo 'tools/codex-vm.exe'}
if((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory -lt 1572864){throw 'RAM admission'}
$fleet=@(1..$Bots|ForEach-Object{New-Bot $_})
$env:CODEX_VM_PROFILE=$prof
try{$guest=Start-Process $vmPath -ArgumentList @('-kernel',('"'+$Artifact+'"'),'-disk',('"'+$disk+'"'),'-output',('"'+$log+'"'),'-headless','-mem','3072','-e1000-nat','-portfwd',"${Port}:2593",'-portfwd',"${AdminPort}:2594",'-input',('"'+$option+'"'),'-clock-scale',"$ClockScale") -WindowStyle Hidden -PassThru -RedirectStandardError $err}
finally{$env:CODEX_VM_PROFILE=''}
@{pid=$guest.Id;guests=1;log=$log;port=$Port;bots=$Bots;minutes=$Minutes;world=$World}|ConvertTo-Json|Set-Content (Join-Path $OutDir 'run.json')
Note "SOAK guest PID=$($guest.Id) port=$Port bots=$Bots minutes=$Minutes clock-scale=$ClockScale world=$World"
$fatal=''
function Dump-Cover{$cb=@($fleet|Where-Object Up)|Select-Object -First 1;if($cb){try{Send-Plain $cb (Say '[cover');$until=[datetime]::UtcNow.AddSeconds(3);while([datetime]::UtcNow -lt $until){Pump-Bot $cb 1}}catch{}}}
function Read-Log{if(-not (Test-Path $log)){return ''};$f=[IO.File]::Open($log,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete));$r=[IO.StreamReader]::new($f);try{return $r.ReadToEnd()}finally{$r.Dispose()}}
try{
    $deadline=[datetime]::UtcNow.AddSeconds($StartupSeconds)
    while($true){
        $text=Read-Log
        if($text -match '(?m)^FAIL|!EXC|OUT OF MEMORY'){throw "startup failed: $text"}
        if($guest.HasExited){throw 'guest exited before listen'}
        if($text -match '(?m)^LISTEN game-server 0'){break}
        if([datetime]::UtcNow -gt $deadline){throw 'startup deadline'}
        Start-Sleep -Milliseconds 250
    }
    Note "SOAK server listening; logging in $Bots bots"
    $elapsed=0;$end=[datetime]::UtcNow.AddMinutes($Minutes);$nextReport=[datetime]::UtcNow.AddMinutes(1);$seen=0
    $errandDone=($ErrandSeconds -eq 0);$errandDeadline=[datetime]::UtcNow.AddSeconds($ErrandSeconds);$errandNext=[datetime]::UtcNow
    while([datetime]::UtcNow -lt $end){
        if(-not $errandDone -and [datetime]::UtcNow -ge $errandNext){
            $errandNext=[datetime]::UtcNow.AddSeconds(2)
            if((Read-Log) -match '(?m)^BRITISH errand (leg 5 |done)'){$errandDone=$true;Note "ERRAND British's errand finished, the economy is open; British logs in"}
            elseif([datetime]::UtcNow -gt $errandDeadline){$fatal="British's errand did not finish within $ErrandSeconds s of listen";break}
        }
        foreach($b in $fleet){
            try{
                if(-not $b.Up){if($b.Index -eq 1 -and -not $errandDone){continue};if([datetime]::UtcNow -ge $b.Reconnect){Connect-Bot $b;Note "LOGIN $($b.Name) serial=$($b.Serial) at $($b.X),$($b.Y) (login $($b.Logins))"};continue}
                Pump-Bot $b 256
                if([datetime]::UtcNow -ge $b.NextAt){Act $b;$b.NextAt=[datetime]::UtcNow.AddMilliseconds(300+$rng.Next(1200))}
            }catch{Drop-Bot $b $_.Exception.Message}
        }
        Admin-Step
        Start-Sleep -Milliseconds 10
        if([datetime]::UtcNow -ge $nextReport){
            $nextReport=[datetime]::UtcNow.AddMinutes(1);$text=Read-Log;$new=$text.Substring([Math]::Min($seen,$text.Length));$seen=$text.Length
            if($guest.HasExited){$fatal="guest exited code $($guest.ExitCode)";break}
            $hit=[regex]::Matches($new,$fatalPattern)
            if($hit.Count -gt 0){$fatal=($hit|Select-Object -First 3|ForEach-Object{$_.Value.Trim()}) -join ' | ';break}
            foreach($r in [regex]::Matches($new,'REFUSE[^\r\n]*reason=([^\r\n]*)')){Note "REFUSE $(($r.Groups[1].Value -replace ' raw-count=.*','').Trim())"}
            $elapsed++;if($elapsed % 10 -eq 0){Dump-Cover}
            $up=@($fleet|Where-Object Up).Count
            Note ("STATUS up=$up/$Bots sent=$(($fleet|Measure-Object Sent -Sum).Sum) recv=$(($fleet|Measure-Object Received -Sum).Sum) disconnects=$($disconnects.Count) " + (($tally.Keys|Sort-Object|ForEach-Object{"$_=$($tally[$_])"}) -join ' '))
        }
    }
}catch{$fatal="harness: $($_.Exception.Message)"}
finally{
    if(-not $guest.HasExited){Dump-Cover}
    foreach($b in $fleet){try{if($b.Up){Send-Plain $b ([byte[]]@(1,255,255,255,255))}}catch{};try{if($b.Tcp){$b.Tcp.Dispose()}}catch{}}
    Start-Sleep -Milliseconds 1500
    if(-not $guest.HasExited){
        try{$h=[Threading.EventWaitHandle]::OpenExisting("Global\CodexVmShutdown_$($guest.Id)");try{[void]$h.Set()}finally{$h.Dispose()};[void]$guest.WaitForExit(15000)}catch{}
        if(-not $guest.HasExited){Stop-Process -Id $guest.Id -Force;[void]$guest.WaitForExit(5000)}
    }
    if(Test-Path $option){[IO.File]::Delete($option)}
}
Note ("OPS received " + (($recvOps.Keys|Sort-Object|ForEach-Object{'{0:X2}={1}' -f $_,$recvOps[$_]}) -join ' '))
foreach($d in $disconnects){Note "DISCONNECTED $d"}
$syms=[Collections.Generic.List[object]]::new()
foreach($line in [IO.File]::ReadLines($map)){if($line -match '^(0x[0-9a-fA-F]+)\s+(\d+)\s+(.+)$'){$syms.Add(@([Convert]::ToInt64($Matches[1],16),[int]$Matches[2],$Matches[3].Trim()))}}
$sorted=@($syms|Sort-Object {$_[0]});$starts=[long[]]@($sorted|ForEach-Object{$_[0]})
$hitNames=@{};$samples=0
if(Test-Path $prof){foreach($line in [IO.File]::ReadLines($prof)){if($line -match 'HPROF:([0-9a-fA-F]+)'){$samples++;$rip=[Convert]::ToInt64($Matches[1],16);$i=[Array]::BinarySearch($starts,$rip);if($i -lt 0){$i=(-bnot $i)-1};if($i -ge 0 -and $rip -lt $sorted[$i][0]+$sorted[$i][1]){$hitNames[$sorted[$i][2]]=1}}}}
Note "COVERAGE sampled functions hit $($hitNames.Count) of $($sorted.Count) in the map, from $samples host samples at about 18 Hz (a sampled floor, not a full coverage measure)"
if($CoverLog){$md=Join-Path $OutDir 'coverage.md';try{pwsh -NoProfile -File (Join-Path $repo 'build/coverage-report.ps1') -Log $CoverLog -Output $log -Markdown $md | Select-Object -First 3 | ForEach-Object{Note "COVER $_"}}catch{Note "COVER report failed: $($_.Exception.Message)"}}
if(-not $fatal){$hit=[regex]::Matches((Read-Log),$fatalPattern);if($hit.Count -gt 0){$fatal=($hit|Select-Object -First 3|ForEach-Object{$_.Value.Trim()}) -join ' | '}}
if($fatal){Note "SOAK FAIL $fatal";exit 1}
Note "SOAK PASS $Minutes minutes, $Bots bots, $($disconnects.Count) disconnects"
