[CmdletBinding()]
param([Parameter(Mandatory)][string]$Artifact,[Parameter(Mandatory)][string]$World,
    [Parameter(Mandatory)][string]$CompressionSource,[Parameter(Mandatory)][string]$OutDir,
    [ValidateRange(1,7)][int]$Bots=6,[ValidateRange(1,1440)][int]$Minutes=120,[int]$Seed=1,
    [ValidateRange(30,900)][int]$StartupSeconds=300,[string]$Vm='',[string]$CoverLog='')
# Scripted-player soak (UOAIX-49 part B): N bot characters on N links drive every packet family
# against a COPY of a world disk through the real composite server, in testing mode, for -Minutes.
# Fatal: a server FAIL/!EXC/OUT OF MEMORY/"requires restart"/"SAVE REFUSED" line or the guest exiting. Every bot
# disconnect, every server REFUSE and every unanswered request is counted and named in soak.log.
# The guest runs under the host sampling profiler; coverage is functions sampled over functions in the map.
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$Artifact=(Resolve-Path -LiteralPath $Artifact).Path;$World=(Resolve-Path -LiteralPath $World).Path
$map=[IO.Path]::ChangeExtension($Artifact,'.map');if(-not (Test-Path $map)){throw "No symbol map beside the artifact: $map"}
if((Get-FileHash $CompressionSource).Hash -ne '807165537C00C83F295DC98F229F53D31460F055D1CC7FCBA9616E5966523042'){throw 'Wrong Huffman oracle source'}
$hash=[Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($repo.ToLowerInvariant()))
$Port=20000+(([BitConverter]::ToUInt16($hash,0)+7) % 10000)
if(Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue){throw "Port $Port is held; refusing to share it"}
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
$families=@('walk','say','go','goto','vendor','bank','harvest','craft','carve','combat','moongate','inventory','click','magery','help','chop','skill','fighter','reap','sow')
$fatalPattern='(?m)^.*(?:^FAIL|!EXC|OUT OF MEMORY|requires restart|SAVE REFUSED).*$'
$tally=@{};foreach($f in $families+@('death','ghost','buy','target','menu')){$tally[$f]=0}
$recvOps=@{};$disconnects=[Collections.Generic.List[string]]::new()
function New-Bot([int]$Index){
    [pscustomobject]@{Index=$Index;Name=$(if($Index -eq 1){'British'}else{'soakbot'+[char](96+$Index)});Password=$(if($Index -eq 1){'Astronaut'}else{''});Flora=@{};Crops=@{};Tcp=$null;Stream=$null;Key=0L;Plain=$null;Up=$false;Serial=0L;X=0;Y=0;Z=0;
        Seq=0;Pending=-1;Pack=0L;Cursor=0L;CursorAt=[datetime]::MinValue;Dead=$false;DeathAsked=$false;Mobiles=@{};Items=@{};Corpses=@{};Places=[Collections.Generic.List[string]]::new();
        BuyFrom=0L;Menu=$null;Blocked=@{};Sent=0;Received=0;NextAt=[datetime]::UtcNow;Last='login';Logins=0;Reconnect=[datetime]::UtcNow}
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
        0x2E{if((U32 $P 9) -eq $B.Serial -and $P[8] -eq 0x15){$B.Pack=U32 $P 1}}
        0x3C{$count=U16 $P 3;for($i=0;$i -lt $count;$i++){$at=5+$i*19;if($at+19 -gt $P.Length){break};$B.Items[(U32 $P $at)]=@((U16 $P ($at+4)),(U32 $P ($at+13)))}}
        0x25{$B.Items[(U32 $P 1)]=@((U16 $P 5),(U32 $P 14))}
        0x6C{$B.Cursor=U32 $P 2;$B.CursorAt=[datetime]::UtcNow}
        0x2C{$B.Dead=$true;$B.DeathAsked=$true}
        0x74{$B.BuyFrom=U32 $P 3}
        0x7C{$B.Menu=$P}
        0x1C{if($P.Length -gt 44){$t=[Text.Encoding]::ASCII.GetString($P,44,$P.Length-44).Trim([char]0)
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
function Use-Then-Target($B,[long]$Tool,[scriptblock]$Target,[string]$Family,[string]$Speech=''){
    if($Tool -eq 0 -and -not $Speech){return}
    $before=$B.CursorAt;Send-Plain $B $(if($Speech){Say $Speech}else{Serial-Packet 6 $Tool})
    $deadline=[datetime]::UtcNow.AddSeconds(2);while($B.CursorAt -eq $before -and [datetime]::UtcNow -lt $deadline){Pump-Bot $B 1}
    if($B.CursorAt -ne $before){Send-Plain $B (& $Target);$tally['target']++}else{$k="nocursor-$Family";$tally[$k]=1+$(if($tally.ContainsKey($k)){$tally[$k]}else{0})}
}
function Act($B){
    if($B.DeathAsked){$B.DeathAsked=$false;Send-Plain $B ([byte[]]@(44,1));$tally['death']++;$B.Last='death answer';return}
    $f=$families[$rng.Next($families.Count)];$B.Last=$f;$tally[$f]++
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
            if($B.BuyFrom -ne 0 -and $v){$lots=@($B.Items.Keys|Where-Object{$B.Items[$_][1] -eq $B.BuyFrom});$tools=@($lots|Where-Object{@(0x0EC4,0x13F6,0x13E3,0x0E86) -contains $B.Items[$_][0]});$pick=@(if($tools.Count -gt 0){$tools}else{$lots});$lot=if($pick.Count -gt 0){$pick[$rng.Next($pick.Count)]}else{0}
                if($lot){$p=[byte[]]::new(15);$p[0]=0x3B;Put16 $p 1 15;Put32 $p 3 $v;$p[7]=2;$p[8]=0x1A;Put32 $p 9 $lot;Put16 $p 13 1;Send-Plain $B $p;$tally['buy']++}}}
        'bank'{Send-Plain $B (Say '[go 1438 1695');Send-Plain $B (Say 'bank')}
        'inventory'{if($B.Pack){Send-Plain $B (Serial-Packet 6 $B.Pack);Pump-Bot $B 64;foreach($k in @($B.Items.Keys)){if($B.Items[$k][0] -eq 0x0E76 -and $B.Items[$k][1] -eq $B.Pack){Send-Plain $B (Serial-Packet 6 $k)}}}else{Send-Plain $B (Serial-Packet 6 ($B.Serial-bor 0x80000000))}}
        'harvest'{Send-Plain $B (Say '[go 1452 1529');Pump-Bot $B 64;Use-Then-Target $B (Item-Of $B 0x0E86) {Target-Ground $B 1451 1528 40} 'harvest'}
        'craft'{$h=Item-Of $B 0x13E3;if($h){Send-Plain $B (Say '[go 1418 1547');Pump-Bot $B 64;$B.Menu=$null;Send-Plain $B (Serial-Packet 6 $h);$deadline=[datetime]::UtcNow.AddSeconds(2);while($null -eq $B.Menu -and [datetime]::UtcNow -lt $deadline){Pump-Bot $B 1}
            if($B.Menu){$m=$B.Menu;$tl=$m[9];$at=10+$tl;$count=$m[$at];if($count -gt 0){$g=U16 $m ($at+1);$a=[byte[]]::new(13);$a[0]=0x7D;Put32 $a 1 (U32 $m 3);Put16 $a 5 (U16 $m 7);Put16 $a 7 1;Put16 $a 9 $g;Send-Plain $B $a;$tally['menu']++}}else{$tally['nomenu-craft']=1+$(if($tally.ContainsKey('nomenu-craft')){$tally['nomenu-craft']}else{0})}}}
        'carve'{$c=0;foreach($k in $B.Corpses.Keys){$c=$k;break};if($c){$at=$B.Corpses[$c];Send-Plain $B (Say "[go $($at[0]) $($at[1])");Pump-Bot $B 64;Use-Then-Target $B (Item-Of $B 0x0EC4) {Target-Object $B $c $at[0] $at[1] 0 0x2006} 'carve'}}
        'combat'{$t=Nearest $B {param($m) $m[0] -lt 400 -or $m[0] -gt 403};if(-not $t){$t=Nearest $B {param($m) $true}}
            if($t){Send-Plain $B ([byte[]]@(114,1,0,50,0));Send-Plain $B (Serial-Packet 5 $t);$m=$B.Mobiles[$t]
                for($i=0;$i -lt 4;$i++){$dx=[Math]::Sign($m[1]-$B.X);$dy=[Math]::Sign($m[2]-$B.Y);if($dx -eq 0 -and $dy -eq 0){break};Walk $B ([Array]::IndexOf(@('0,-1','1,-1','1,0','1,1','0,1','-1,1','-1,0','-1,-1'),"$dx,$dy"))}}
            else{Send-Plain $B (Say '[go 1385 1487')}}
        'moongate'{Send-Plain $B (Say '[go 1337 1997');Pump-Bot $B 64;Walk $B 6;Walk $B 6}
        'magery'{$n=1+$rng.Next(16);$txt=[Text.Encoding]::ASCII.GetBytes("$n");$p=[byte[]]::new(5+$txt.Length);$p[0]=0x12;Put16 $p 1 $p.Length;$p[3]=0x56;$txt.CopyTo($p,4);$before=$B.CursorAt;Send-Plain $B $p
            $deadline=[datetime]::UtcNow.AddSeconds(4);while($B.CursorAt -eq $before -and [datetime]::UtcNow -lt $deadline){Pump-Bot $B 1}
            if($B.CursorAt -ne $before){$t=Nearest $B {param($m) $true};if($t -and $rng.Next(2) -eq 0){$m=$B.Mobiles[$t];Send-Plain $B (Target-Object $B $t $m[1] $m[2] 0 $m[0])}else{Send-Plain $B (Target-Object $B $B.Serial $B.X $B.Y $B.Z 400)};$tally['target']++}}
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
    }
}
$log=Join-Path $OutDir 'server.log';$err=Join-Path $OutDir 'guest.stderr';$option=Join-Path $OutDir 'launch.input';$prof=Join-Path $OutDir 'prof.txt'
[IO.File]::WriteAllText($option,"UOAIX TESTING`n",[Text.Encoding]::ASCII)
$vmPath=if($Vm){(Resolve-Path $Vm).Path}else{Join-Path $repo 'tools/codex-vm.exe'}
if((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory -lt 1572864){throw 'RAM admission'}
$fleet=@(1..$Bots|ForEach-Object{New-Bot $_})
$env:CODEX_VM_PROFILE=$prof
try{$guest=Start-Process $vmPath -ArgumentList @('-kernel',('"'+$Artifact+'"'),'-disk',('"'+$disk+'"'),'-output',('"'+$log+'"'),'-headless','-mem','3072','-e1000-nat','-portfwd',"${Port}:2593",'-input',('"'+$option+'"')) -WindowStyle Hidden -PassThru -RedirectStandardError $err}
finally{$env:CODEX_VM_PROFILE=''}
@{pid=$guest.Id;guests=1;log=$log;port=$Port;bots=$Bots;minutes=$Minutes;world=$World}|ConvertTo-Json|Set-Content (Join-Path $OutDir 'run.json')
Note "SOAK guest PID=$($guest.Id) port=$Port bots=$Bots minutes=$Minutes world=$World"
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
    while([datetime]::UtcNow -lt $end){
        foreach($b in $fleet){
            try{
                if(-not $b.Up){if([datetime]::UtcNow -ge $b.Reconnect){Connect-Bot $b;Note "LOGIN $($b.Name) serial=$($b.Serial) at $($b.X),$($b.Y) (login $($b.Logins))"};continue}
                Pump-Bot $b 256
                if([datetime]::UtcNow -ge $b.NextAt){Act $b;$b.NextAt=[datetime]::UtcNow.AddMilliseconds(300+$rng.Next(1200))}
            }catch{Drop-Bot $b $_.Exception.Message}
        }
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
