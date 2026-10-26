[CmdletBinding()]
param([Parameter(Mandatory)][string]$Artifact,[Parameter(Mandatory)][string]$World,
    [Parameter(Mandatory)][string]$CompressionSource,[Parameter(Mandatory)][string]$OutDir,
    [ValidateRange(1,26)][int]$Bots=6,[ValidateRange(1,1440)][int]$Minutes=120,[int]$Seed=1,
    [ValidateRange(30,900)][int]$StartupSeconds=300,[string]$Vm='',[string]$CoverLog='',[ValidateRange(1,100)][int]$ClockScale=1,
    [ValidateRange(0,3600)][int]$ErrandSeconds=900,[string[]]$Only=@(),[switch]$Open)
# -Open launches UOAIX TESTING OPEN (the economy open at boot, CompositeGame.md), so British logs in at once.
if($Open){$ErrandSeconds=0}
# Scripted-player soak (UOAIX-49 part B): N bot characters on N links drive every packet family
# against a COPY of a world disk through the real composite server, in testing mode, for -Minutes.
# Fatal: a server FAIL/!EXC/OUT OF MEMORY/"requires restart"/"SAVE FAILED" line or the guest exiting; "SAVE CHECK FAILED, saving anyway" is a logged check, not a refused save (a save never refuses, uoaix-init.md). Every bot
# disconnect, every server REFUSE and every unanswered request is counted and named in soak.log.
# The guest runs under the host sampling profiler; coverage is functions sampled over functions in the map.
# -ClockScale N runs every guest timer N times faster (codex-vm -clock-scale, UOAIX-82); the soak always runs the
# server in testing mode, which is the only mode a scaled clock is for. -Minutes stays wall-clock minutes.
# British's own pulses pause his gold errand, and the NPC economy opens only when it banks (UOAIX-97), so bot 1 (British)
# logs in once the server logs the errand finished; not finished within -ErrandSeconds of listen is fatal. 0 skips the wait.
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$script:poisoned=@{}
$script:widenStop=$false
$script:corWatch=$null
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
function Send-Plain($B,[byte[]]$Bytes){$o=$B.Plain.Count;$B.Plain.AddRange($Bytes);$x=Login-Xor $B.Plain.ToArray() $B.Key;$B.Stream.Write($x,$o,$Bytes.Length);$B.Sent++;$B.LastSent=[datetime]::UtcNow}
function Say([string]$Text){$t=[Text.Encoding]::ASCII.GetBytes($Text);$p=[byte[]]::new(9+$t.Length);$p[0]=3;Put16 $p 1 $p.Length;$p[3]=0;Put16 $p 4 0x3B2;Put16 $p 6 3;$t.CopyTo($p,8);return ,$p}
function Serial-Packet([int]$Op,[long]$Serial){$p=[byte[]]::new(5);$p[0]=$Op;Put32 $p 1 $Serial;return ,$p}
function Target-Ground($B,[int]$X,[int]$Y,[int]$Z){$p=[byte[]]::new(19);$p[0]=0x6C;$p[1]=1;Put32 $p 2 $B.Cursor;Put16 $p 11 $X;Put16 $p 13 $Y;$p[16]=$Z-band 255;return ,$p}
function Target-Object($B,[long]$Serial,[int]$X,[int]$Y,[int]$Z,[int]$Graphic){$p=[byte[]]::new(19);$p[0]=0x6C;$p[1]=0;Put32 $p 2 $B.Cursor;Put32 $p 7 $Serial;Put16 $p 11 $X;Put16 $p 13 $Y;$p[16]=$Z-band 255;Put16 $p 17 $Graphic;return ,$p}
$families=@('mint','walk','say','go','goto','vendor','bank','harvest','craft','carve','combat','moongate','inventory','click','lore','paperdoll','stow','magery','help','chop','skill','fighter','reap','sow',
    'camping','cartography','taste','forensics','spirit','poisoning','stealing','herding',
    'bard','tracking','healing','alchemy','inscription','arms','slay','cull','fallen','dungeons','smite','pick','pet','sell',
    'petname','mappins','book','decay','venom','speechbuy','trade','play','duel','eat','donate','gm','build','trinsic','cove','minoc','vesper','vesperwork','minocwork','covework','moonglowwork','yewwork','windwork','buccwork','bucc','jhelom','serpents','moonglow','yew','wind','skara','nujelm','ocllo','magincia','spells','wild',
    'eights','gofish','poker','blackjack','battleship','chess','checkers','backgammon','graded','castle','cheer','loom','fell','farm','reekgraded','festergraded','redgraded','load','widen','coroner')
# UOAIX-254: -Only load gives every bot, British too, a draw from this mix; bot names soakbot and a letter cap -Bots at 26.
$loadMix=@('walk','say','combat','craft','vendor','trade','bank','harvest')
# -Only runs only the named families (a scenario run); British's own draw is limited to them too.
if($Only.Count -gt 0){$Only=@($Only -join ',' -split ','|Where-Object{$_});$bad=@($Only|Where-Object{$families -notcontains $_});if($bad.Count){throw "Unknown families: $($bad -join ',')"};$families=$Only}
$fatalPattern='(?m)^.*(?:^FAIL|!EXC|OUT OF MEMORY|requires restart|SAVE FAILED).*$'
$tally=@{};foreach($f in $families+@('death','ghost','buy','target','menu')){$tally[$f]=0}
$recvOps=@{};$disconnects=[Collections.Generic.List[string]]::new();$gmDrops=[Collections.Generic.List[string]]::new()
function New-Bot([int]$Index){
    [pscustomobject]@{Index=$Index;Name=$(if($Index -eq 1){'British'}else{'soakbot'+[char](96+$Index)});Password=$(if($Index -eq 1){'Astronaut'}else{''});Flora=@{};Crops=@{};Tcp=$null;Stream=$null;Key=0L;Plain=$null;Up=$false;Serial=0L;X=0;Y=0;Z=0;
        Seq=0;Pending=-1;Pack=0L;Cursor=0L;CursorAt=[datetime]::MinValue;Dead=$false;DeathAsked=$false;Mobiles=@{};Items=@{};Corpses=@{};Places=[Collections.Generic.List[string]]::new();
        BuyFrom=0L;Shop=@{};Sale=$null;Bank=0L;Opened=$false;Fields=@{};FieldSeen=[Collections.Generic.HashSet[long]]::new();Said=[Collections.Generic.List[string]]::new();Menu=$null;Gump=$null;Decor=0L;Runes=@{};MapEdit=-1;Course=0;BookHead=$null;BookPages=$null;Dropped=@{};Ground=@{};Sites=@{};Built=$false;Blocked=@{};Held=@{};Sent=0;Received=0;NextAt=[datetime]::UtcNow;Last='login';Logins=0;Reconnect=[datetime]::UtcNow;Kicked=$null;
        Labels=@{};BuyNames=$null;SellNames=$null;Gate=@{};Winch=0L;LastSent=[datetime]::UtcNow;Swings=@{};Worn=@{};Fires=@{};Doors=@{};TradeBox=0L;Opens=@{};CursorType=-1;Cases=@{};Noto=@{};CraftReady=[datetime]::MinValue;Hits=100}
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
    $B.Up=$true;$B.Dead=$false;$B.DeathAsked=$false;$B.Seq=0;$B.Pending=-1;$B.Mobiles=@{};$B.Items=@{};$B.Corpses=@{};$B.Opened=$false;$B.Bank=0
}
function Drop-Bot($B,[string]$Why){
    if($B.Kicked -and ([datetime]::UtcNow - $B.Kicked).TotalSeconds -lt 60){$B.Kicked=$null;$gmDrops.Add("$($B.Name) after '$($B.Last)': $Why");Note "GM DISCONNECT $($B.Name) after '$($B.Last)': $Why"}
    else{$disconnects.Add("$($B.Name) after '$($B.Last)': $Why");Note "DISCONNECT $($B.Name) after '$($B.Last)': $Why"}
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
        0x78{$s=(U32 $P 3)-band 0x7FFFFFFF;$B.Mobiles[$s]=@((U16 $P 7),(U16 $P 9),(U16 $P 11),$(if($P[13] -gt 127){$P[13]-256}else{[int]$P[13]}));if($P.Length -gt 18 -and -not ((U32 $P 3) -band 0x80000000)){$B.Noto[$s]=$P[18]};$wn=0;$wa=19;while($wa+4 -le $P.Length){if((U32 $P $wa) -eq 0){break};$wn++;$wa+=7+$(if((U16 $P ($wa+4)) -band 0x8000){2}else{0})};$B.Worn[$s]=$wn
            if($s -eq $B.Serial){$at=19;while($at+4 -le $P.Length){$item=U32 $P $at;if($item -eq 0){break};$g=U16 $P ($at+4);$layer=$P[$at+6];if($layer -eq 0x15){$B.Pack=$item};if($layer -eq 1 -or $layer -eq 2){$B.Held[[int]$layer]=$item};$at+=7+$(if($g -band 0x8000){2}else{0})}}}
        0x77{$s=U32 $P 1;$B.Mobiles[$s]=@((U16 $P 5),(U16 $P 7),(U16 $P 9),$(if($P[11] -gt 127){$P[11]-256}else{[int]$P[11]}));if($P.Length -gt 16){$B.Noto[$s]=$P[16]}}
        0x2F{if($P.Length -ge 10){$B.Swings["$(U32 $P 2)>$(U32 $P 6)"]=1}}
        0x6F{if($P.Length -ge 8 -and $P[3] -eq 0){$B.TradeBox=U32 $P 4}}
        0x1D{$s=U32 $P 1;$B.Mobiles.Remove($s);$B.Items.Remove($s);$B.Corpses.Remove($s);$B.Fields.Remove($s);$B.Flora.Remove($s);$B.Crops.Remove($s);$B.Sites.Remove($s);if($B.Dropped.ContainsKey($s)){$B.Dropped.Remove($s);Count 'decayed'}}
        0xB0{$B.Gump=$P}
        0x56{if($P[5] -eq 7){$B.MapEdit=$P[6]}elseif($P[5] -eq 1){$B.Course++}}
        0x93{$B.BookHead=$P}
        0x66{$B.BookPages=$P}
        0x1A{$s=U32 $P 3;$g=U16 $P 7;$at=9;if($s -band 0x80000000){$at=11};$s=$s-band 0x7FFFFFFF;$x=(U16 $P $at)-band 0x7FFF;$y=(U16 $P ($at+2))-band 0x3FFF
            if($g -eq 0x2006){$B.Corpses[$s]=@($x,$y)};if($g -eq 0x0B90){$B.Decor=$s};if($g -eq 0x1F14){$B.Runes[$s]=1};if(@(0x0FA2,0x0FA3,0x14F1,0x0FA6,0x0E1C,0x0FAD,0x0FA7,0x1061,0x1062) -contains $g){$B.Ground[$s]=@($x,$y,$g)};if($s -ge 0x7C000001 -and $s -lt 0x7D000000){$B.Flora[$s]=@($x,$y,$g)};if($s -ge 0x7D000001 -and $s -lt 0x7E000000){$B.Crops[$s]=@($x,$y,$g)};if($s -ge 0x7B000001 -and $s -lt 0x7C000000){$B.Sites[$s]=@($x,$y,$g)};if($g -ge 0x0DE3 -and $g -le 0x0DE8){$B.Fires[$s]=@($x,$y,$g)};if(($g -ge 0x0675 -and $g -le 0x06F4) -or ($g -ge 0x0839 -and $g -le 0x0848)){$B.Doors[$s]=@($x,$y,$g)}
            if(@(0x0082,0x398C,0x3996,0x3915,0x3922,0x3967,0x3979,0x3946,0x3956,0x0F6C) -contains $g){$B.Fields[$s]=@($x,$y,$g);[void]$B.FieldSeen.Add($s)}
            if($g -ge 0x0A97 -and $g -le 0x0A9C){$B.Cases[$s]=@($x,$y,$g)}
            $zAt=$at+4+$(if((U16 $P $at) -band 0x8000){1}else{0});if($g -eq 0x06F6 -and $zAt -lt $P.Length){$B.Gate[$s]=S8 $P[$zAt]};if($g -eq 0x1EAC){$B.Winch=$s}}
        0x2E{if((U32 $P 9) -eq $B.Serial -and $P[8] -eq 0x15){$B.Pack=U32 $P 1};if((U32 $P 9) -eq $B.Serial -and $P[8] -eq 0x1D){$B.Bank=U32 $P 1};if($P[8] -eq 0x1A -or $P[8] -eq 0x1B){$B.Shop[(U32 $P 1)]=U32 $P 9}}
        0x3C{$count=U16 $P 3;for($i=0;$i -lt $count;$i++){$at=5+$i*19;if($at+19 -gt $P.Length){break};$B.Items[(U32 $P $at)]=@((U16 $P ($at+4)),(U32 $P ($at+13)),(U16 $P ($at+7)),(U16 $P ($at+9)),(U16 $P ($at+11)))}}
        0x25{$B.Items[(U32 $P 1)]=@((U16 $P 5),(U32 $P 14),(U16 $P 8),(U16 $P 10),(U16 $P 12))}
        0x6C{$B.Cursor=U32 $P 2;$B.CursorAt=[datetime]::UtcNow;$B.CursorType=$P[1]}
        0x24{$B.Opens[(U32 $P 1)]=U16 $P 5}
        0x2C{$B.Dead=$true;$B.DeathAsked=$true}
        0x74{$B.BuyFrom=U32 $P 3;$names=[Collections.Generic.List[object]]::new();$at=8;for($i=0;$i -lt $P[7] -and $at+5 -le $P.Length;$i++){$n=$P[$at+4];if($at+5+$n -gt $P.Length){break};$names.Add(@((U32 $P $at),[Text.Encoding]::ASCII.GetString($P,$at+5,$n).Trim([char]0)));$at+=5+$n};$B.BuyNames=$names}
        0x9E{$B.Sale=$P;$names=[Collections.Generic.List[object]]::new();$at=9;for($i=0;$i -lt (U16 $P 7) -and $at+14 -le $P.Length;$i++){$n=U16 $P ($at+12);if($at+14+$n -gt $P.Length){break};$names.Add(@((U16 $P ($at+10)),[Text.Encoding]::ASCII.GetString($P,$at+14,$n).Trim([char]0)));$at+=14+$n};$B.SellNames=$names}
        0x7C{$B.Menu=$P}
        0x11{if($P.Length -ge 41 -and (U32 $P 3) -eq $B.Serial){$B.Hits=U16 $P 37}}
        0xA1{if($P.Length -ge 9 -and (U32 $P 1) -eq $B.Serial){$B.Hits=U16 $P 7}}
        0x1C{if($P.Length -gt 44){$t=[Text.Encoding]::ASCII.GetString($P,44,$P.Length-44).Trim([char]0);if($script:corWatch -and $B -eq $script:corWatch){$who=U32 $P 3;if($who -ne 0 -and $who -ne 0xFFFFFFFFL -and @($fleet|ForEach-Object Serial) -notcontains $who){[void]$script:corHeard.Add($t)}};if($B.Said.Count -lt 32){$B.Said.Add($t)};$B.Labels[(U32 $P 3)]=$t
            if($t -match 'successfully steal'){Count 'stolen'}
            if($t -match 'walks where it was instructed|begins to follow you'){Count 'herded'}
            if($t -match '^Killed\.'){Count 'killed'}
            if($t -match 'You have been poisoned'){Count 'poisoned';$script:poisoned[$B.Serial]=1}
            if($t -match 'stableman|trained and loyal|standing room|creatures to claim'){Count ('claimsaid-' + (($t -replace '[^A-Za-z]+','_').Substring(0,[Math]::Min(40,($t -replace '[^A-Za-z]+','_').Length))))}
            if($t -match 'You cannot rename this'){Count 'refused-rename'}
            if($t -match 'That name is unacceptable'){Count 'unacceptable-rename'}
            if($t -match '^[A-Za-z ]+: (.+)$'){foreach($w in ($Matches[1] -split ' ')){if($w -and -not $B.Places.Contains($w)){$B.Places.Add($w)}}}}}
    }
}
function Pump-Bot($B,[int]$Max){
    $n=0
    while($n -lt $Max -and $B.Tcp.Available -gt 0){$p=Read-Packet $B.Stream;Handle $B $p;$n++}
    if($n -eq 0 -and $Max -eq 1){Start-Sleep -Milliseconds 20}
    # GameLinks closes a link whose client sends nothing for 30000 guest ticks (gl-serve, no log line): an idle bot pings (0x73).
    if($B.Up -and ([datetime]::UtcNow-$B.LastSent).TotalSeconds -gt 5){Send-Plain $B ([byte[]]@(0x73,0))}
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
# A craft tool answers with the craft gump (0xB0, CraftGump: layout text at byte 21, an item's reply button reads
# "{ button x y 2117 2118 1 0 id }") or, for a menu craft, an 0x7C menu. The bot presses a random item, then Make Last
# (9999) on the gump that follows; a menu gets its first entry, as Answer-Menu does. Clear Gump and Menu before the use.
function Answer-Craft($B,[string]$Family){
    $deadline=[datetime]::UtcNow.AddSeconds(2);while($null -eq $B.Gump -and $null -eq $B.Menu -and [datetime]::UtcNow -lt $deadline){Pump-Bot $B 1}
    if($null -ne $B.Menu){$m=$B.Menu;$B.Menu=$null;$tl=$m[9];$at=10+$tl;if($m[$at] -le 0){return}
        $a=[byte[]]::new(13);$a[0]=0x7D;Put32 $a 1 (U32 $m 3);Put16 $a 5 (U16 $m 7);Put16 $a 7 1;Put16 $a 9 (U16 $m ($at+1));Send-Plain $B $a;Count "menu-$Family";return}
    if($null -eq $B.Gump){Count $(if(@($B.Said|Where-Object{$_ -match 'Wait before crafting again'}).Count -gt 0){"wait-$Family"}else{"nogump-$Family"});return}
    for($round=0;$round -lt 2;$round++){
        $g=$B.Gump;$B.Gump=$null;if($null -eq $g -or $g.Length -lt 22){return}
        $layout=[Text.Encoding]::ASCII.GetString($g,21,$g.Length-21)
        $ids=@([regex]::Matches($layout,'\{ button \d+ \d+ \d+ \d+ 1 0 (\d+) \}')|ForEach-Object{[long]$_.Groups[1].Value}|Where-Object{$_ -ne 0 -and $_ -ne 9999})
        $button=if($round -eq 1 -and $layout -match ' 1 0 9999 \}'){9999}elseif($ids.Count -gt 0){$ids[$rng.Next($ids.Count)]}else{0}
        if($button -eq 0){Count "nobutton-$Family";return}
        while([datetime]::UtcNow -lt $B.CraftReady){Pump-Bot $B 1}
        $a=[byte[]]::new(23);$a[0]=0xB1;Put16 $a 1 23;Put32 $a 3 (U32 $g 3);Put32 $a 7 (U32 $g 7);Put32 $a 11 $button;Send-Plain $B $a
        Count $(if($button -eq 9999){"makelast-$Family"}else{"gump-$Family"})
        # A press crafts, and the server refuses the next craft for 3000 ms of guest time (GameCraft, gh-ticks 3000).
        $B.CraftReady=[datetime]::UtcNow.AddMilliseconds(3000/$ClockScale+500)
        $deadline=[datetime]::UtcNow.AddSeconds(2);while($null -eq $B.Gump -and [datetime]::UtcNow -lt $deadline){Pump-Bot $B 1};if($null -eq $B.Gump){return}}
}
# UOAIX-49 C: British's GM and admin speech (CompositePaging cp-route, cp-british, cp-target, the decorator, land and
# moderation commands, their menus and gumps), one step per draw in a fixed cycle so a short run reaches every step.
function Gm-Victim{$v=@($fleet|Where-Object{$_.Index -ne 1 -and $_.Up -and -not $_.Dead -and $_.Serial});if($v.Count -eq 0){Count 'notarget-gm';return $null};return $v[$rng.Next($v.Count)]}
function Gm-At($B,$O,[string]$Speech,[string]$Name){if($O -and $Name -in 'kick','unstuck','jail','ban'){$O.Kicked=[datetime]::UtcNow};if($O){Use-Then-Target $B 0 {Target-Object $B $O.Serial $O.X $O.Y $O.Z 400} "gm-$Name" -Speech $Speech}}
function Gm-Gump($B,[string]$Speech,[int]$Button,[string]$Name){$B.Gump=$null;Send-Plain $B (Say $Speech);$deadline=[datetime]::UtcNow.AddSeconds(2);while($null -eq $B.Gump -and [datetime]::UtcNow -lt $deadline){Pump-Bot $B 1}
    if($null -eq $B.Gump){Count "nogump-gm-$Name";return};$g=$B.Gump;$B.Gump=$null
    $a=[byte[]]::new(23);$a[0]=0xB1;Put16 $a 1 23;Put32 $a 3 (U32 $g 3);Put32 $a 7 (U32 $g 7);Put32 $a 11 $Button;Send-Plain $B $a;Count "gump-gm-$Name";Pump-Bot $B 32}
function Gm-Menu($B,[string]$Speech,[int]$Choice){$B.Menu=$null;Send-Plain $B (Say $Speech);$deadline=[datetime]::UtcNow.AddSeconds(2);while($null -eq $B.Menu -and [datetime]::UtcNow -lt $deadline){Pump-Bot $B 1}
    if($null -eq $B.Menu){Count 'nomenu-gm-decor';return};$m=$B.Menu;$B.Menu=$null
    $a=[byte[]]::new(13);$a[0]=0x7D;Put32 $a 1 (U32 $m 3);Put16 $a 5 (U16 $m 7);Put16 $a 7 $Choice;Send-Plain $B $a;Count "menu-gm-decor-$Choice";Pump-Bot $B 32}
# British [adds an item and hands it to bot $O by secure trade (dropped on the bot beside him, both accept), as the
# 'trade' family does: a drop at his feet can bounce on the ground's height. Answers the serial, or 0.
function Gm-Hand($B,$O,[string]$Hex,[int]$Graphic,[int]$Amount){$s=Ensure-Item $B $Hex $Graphic $Amount 'gm-hand';if(-not $s){return 0};$n=[Math]::Max(1,$B.Items[$s][2])
    Send-Plain $O (Say "[go $($B.X+1) $($B.Y)");Pump-Bot $O 32;Pump-Bot $B 16
    $l=[byte[]]::new(7);$l[0]=7;Put32 $l 1 $s;Put16 $l 5 $n;Send-Plain $B $l
    $d=[byte[]]::new(14);$d[0]=8;Put32 $d 1 $s;Put16 $d 5 0xFFFF;Put16 $d 7 0xFFFF;Put32 $d 10 $O.Serial;Send-Plain $B $d;Pump-Bot $B 16;Pump-Bot $O 16
    foreach($x in @($B,$O)){$a=[byte[]]::new(12);$a[0]=0x6F;Put16 $a 1 12;$a[3]=2;$a[11]=1;Send-Plain $x $a}
    $until=[datetime]::UtcNow.AddSeconds(2);while(-not ($O.Items.ContainsKey($s) -and (Owned $O $s)) -and [datetime]::UtcNow -lt $until){Pump-Bot $B 8;Pump-Bot $O 8}
    if($O.Items.ContainsKey($s) -and (Owned $O $s)){Count 'handed-gm';return $s};Count 'nohand-gm';return 0}
function Gm-Decor($B,[string]$Speech,[string]$Name){if(-not $B.Decor){Count "nodecor-gm-$Name";return};Use-Then-Target $B 0 {Target-Object $B $B.Decor $B.X $B.Y $B.Z 0x0B90} "gm-$Name" -Speech $Speech}
$gmSteps=[ordered]@{
    'help'={param($B) Send-Plain $B (Say '[help')}
    'where'={param($B) Send-Plain $B (Say '[where')}
    'invisible'={param($B) Send-Plain $B (Say '[invisible')}
    'inspect'={param($B) $t=Nearest $B {param($m) $true};$o=if($t){$m=$B.Mobiles[$t];@{Serial=$t;X=$m[1];Y=$m[2];Z=0}}else{$B};Gm-At $B $o '[inspect' 'inspect'}
    'cancel'={param($B) Use-Then-Target $B 0 {$p=[byte[]]::new(19);$p[0]=0x6C;Put32 $p 2 $B.Cursor;Put16 $p 11 0xFFFF;return ,$p} 'gm-cancel' -Speech '[inspect'}
    'move'={param($B) Pump-Bot $B 32;$t=Creature $B;if(-not $t){Count 'notarget-gm';return};$m=$B.Mobiles[$t];Use-Then-Target $B 0 {Target-Object $B $t $m[1] $m[2] 0 $m[0]} 'gm-move' -Speech '[move';Next-Target $B {Target-Ground $B ($B.X+1) $B.Y $B.Z} 'gm-move'}
    'summon'={param($B) Send-Plain $B (Say '[summon 33')}
    'remove'={param($B) Pump-Bot $B 32;$t=Creature $B;if(-not $t){Count 'notarget-gm';return};$m=$B.Mobiles[$t];Use-Then-Target $B 0 {Target-Object $B $t $m[1] $m[2] 0 $m[0]} 'gm-remove' -Speech '[remove'}
    'time'={param($B) Send-Plain $B (Say '[time 6')}
    'navy'={param($B) Send-Plain $B (Say '[navy')}
    'gumptest'={param($B) Gm-Gump $B '[gumptest' 1 'gumptest'}
    # Each overlay is shown, up to two of its runes (art 0x1F14) clicked and opened (gmo-click), and hidden again.
    'overlays'={param($B) foreach($w in @('[zones','[spawns','[landshow')){$B.Runes=@{};if($w -eq '[spawns'){Send-Plain $B (Say '[go 1385 1487');Pump-Bot $B 32};Send-Plain $B (Say $w);$deadline=[datetime]::UtcNow.AddSeconds(2);while($B.Runes.Count -eq 0 -and [datetime]::UtcNow -lt $deadline){Pump-Bot $B 4}
        # 1385,1487 is 4 tiles inside the cemetery spawn region's south-east corner (cgf-ground), so its edge is in view.
        Count $(if($B.Runes.Count -eq 0){"norune-gm-$($w.TrimStart('['))"}else{"runes-gm-$($w.TrimStart('['))"})
        foreach($r in @($B.Runes.Keys|Select-Object -First 2)){Send-Plain $B (Serial-Packet 9 $r);Send-Plain $B (Serial-Packet 6 $r);Count 'rune-gm'};Pump-Bot $B 16;Send-Plain $B (Say $w);Pump-Bot $B 16}}
    'kick'={param($B) Gm-At $B (Gm-Victim) '[kick' 'kick'}
    'mute'={param($B) $o=Gm-Victim;Gm-At $B $o '[mute' 'mute';Gm-At $B $o '[mute' 'unmute'}
    'unstuck'={param($B) Gm-At $B (Gm-Victim) '[unstuck' 'unstuck'}
    'jail'={param($B) $o=Gm-Victim;Gm-At $B $o '[jail 1' 'jail';Gm-At $B $o '[jail 1' 'release'}
    'ban'={param($B) $o=Gm-Victim;if($o){Gm-At $B $o '[ban' 'ban';Send-Plain $B (Say "[unban $($o.Name)")}}
    'decor-menu'={param($B) if(-not (Test-Path variable:script:gmChoice)){$script:gmChoice=0};Gm-Menu $B '[decor' (1+($script:gmChoice++) % 7)}
    'decor-place'={param($B) Send-Plain $B (Say '[decor place 0B90');Pump-Bot $B 32;Send-Plain $B (Say '[decor list');Send-Plain $B (Say '[decor markarea 3')}
    'decor-mark'={param($B) Gm-Decor $B '[decor mark' 'mark';Gm-Decor $B '[decor unmark' 'unmark';Gm-Decor $B '[decor nudge 1 0' 'nudge'}
    'decor-remove'={param($B) Gm-Decor $B '[decor remove' 'decor-remove';$B.Decor=0}
    'decor-build'={param($B) Use-Then-Target $B 0 {Target-Ground $B ($B.X+3) $B.Y $B.Z} 'gm-run' -Speech '[decor run 0080';Use-Then-Target $B 0 {Target-Ground $B ($B.X+1) ($B.Y+1) $B.Z} 'gm-fill' -Speech '[decor fill 0519'
        # A cut design (UoaixMultis.md, ids from 0x200) placed at British's tile, then a walk east across its site: main 45460 fixed a ring of walls every placed design raised.
        Send-Plain $B (Say '[decor multi 200');Pump-Bot $B 16;$blocked=$B.Blocked.Count;for($i=0;$i -lt 8;$i++){Walk $B 2;Pump-Bot $B 8};Count $(if($B.Blocked.Count -gt $blocked){'multi-walk-blocked'}else{'multi-walk-clear'})
        Send-Plain $B (Say '[decor multi 1')}
    'decor-unstatic'={param($B) Use-Then-Target $B 0 {$p=Target-Ground $B ($B.X+1) $B.Y $B.Z;Put16 $p 17 0x0080;return ,$p} 'gm-unstatic' -Speech '[decor unstatic'}
    'decor-more'={param($B) Gm-Gump $B '[decor gump' 7 'decor';foreach($w in @('[decor set none none','[decor baked 1 2','[decor bogus')){Send-Plain $B (Say $w);Pump-Bot $B 16}}
    'land'={param($B) foreach($w in @('[land grass','[land dirt','[land 0x0003','[land 4','[land nowhere','[raise 1','[flatten')){Use-Then-Target $B 0 {Target-Ground $B ($B.X+2) $B.Y $B.Z} 'gm-land' -Speech $w};Use-Then-Target $B 0 {Target-Ground $B ($B.X+2) $B.Y $B.Z} 'gm-land' -Speech '[landclear'
        # Two 17x17 strokes past the 18-tile view (no runes) leave fewer than two strokes of the 1024 rows free, so the
        # next round grows the table (gld-grow); clearing them sweeps, compacts and reindexes it.
        Send-Plain $B (Say '[brush 8');Pump-Bot $B 4
        foreach($w in @('[land sand','[landclear')){foreach($dx in @(40,60)){Use-Then-Target $B 0 {Target-Ground $B ($B.X+$dx) $B.Y $B.Z} 'gm-land-wide' -Speech $w}}
        Send-Plain $B (Say '[brush 0')}
    'land-gump'={param($B) Gm-Menu $B '[decor' 8;$deadline=[datetime]::UtcNow.AddSeconds(2);while($null -eq $B.Gump -and [datetime]::UtcNow -lt $deadline){Pump-Bot $B 1}
        if($null -eq $B.Gump){Count 'nogump-gm-land';return};$g=$B.Gump;$B.Gump=$null;$a=[byte[]]::new(23);$a[0]=0xB1;Put16 $a 1 23;Put32 $a 3 (U32 $g 3);Put32 $a 7 (U32 $g 7);Put32 $a 11 10;Send-Plain $B $a;Count 'gump-gm-land'
        Next-Target $B {Target-Ground $B ($B.X+2) $B.Y $B.Z} 'gm-land-gump'}
    'play'={param($B) foreach($w in @('[play show','[play select 1','[play sample')){Send-Plain $B (Say $w);Pump-Bot $B 16};Gm-Gump $B '[play gump' 10 'play'}
    'story'={param($B) Send-Plain $B (Say '[story');Gm-Gump $B '[story gump' 1 'story'}
    'skill'={param($B) $o=Gm-Victim;Gm-At $B $o '[skill magery 50 target' 'skill';Gm-At $B $o '[attr str 60 target' 'attr'}
    'usage'={param($B) foreach($w in @('[go','[add','[summon','[jail','[unban','[kick help','[decor help')){Send-Plain $B (Say $w);Pump-Bot $B 8}}
    'accounts'={param($B) Send-Plain $B (Say '[accounts');Send-Plain $B (Say '[pages')}
    'add-named'={param($B) Send-Plain $B (Say '[add dagger 2');Send-Plain $B (Say '[add nosuchthing')}
    # The sample play's story "bakery" has the scene (set) "market"; a placed decoration joins it (cp-decor-member).
    'story-set'={param($B) foreach($w in @('[play select 1','[play sample')){Send-Plain $B (Say $w);Pump-Bot $B 32}
        if(-not $B.Decor){Send-Plain $B (Say '[decor place 0B90');$deadline=[datetime]::UtcNow.AddSeconds(2);while(-not $B.Decor -and [datetime]::UtcNow -lt $deadline){Pump-Bot $B 1}};Gm-Decor $B '[decor set bakery market' 'decor-set'}
    # Only British can [add, so the other bots never hold a bow or shield: British hands one a bow, arrows and a shield
    # (dropped at his feet, packed by the bot), and the bot shoots a creature (cb-range, cb-shot, ammo-hook), then
    # fights it with the shield up (cb-parry). Every bot is pumped meanwhile.
    'armory'={param($B) $o=Gm-Victim;if(-not $o -or -not $o.Pack){return}
        $bow=Gm-Hand $B $o '13B2' 0x13B2 1;[void](Gm-Hand $B $o '0F3F' 0x0F3F 30);$shield=Gm-Hand $B $o '1B76' 0x1B76 1
        foreach($w in @($bow,$shield)){if(-not $w){continue};Equip $o $w 2;Send-Plain $o (Say '[goto britspawn');Pump-Bot $o 64;$t=Creature $o
            $until=[datetime]::UtcNow.AddSeconds(5);while(-not $t -and [datetime]::UtcNow -lt $until){Pump-Bot $o 8;$t=Creature $o}
            if(-not $t){Count 'notarget-gm-armory';continue};Send-Plain $o ([byte[]]@(114,1,0,50,0));Send-Plain $o (Serial-Packet 5 $t);Count 'fight-gm-armory'
            # A player swings only in its own pulse (cb-live), so the fighter keeps talking as a client does: a status request each half second.
            $until=[datetime]::UtcNow.AddSeconds(8);$s=[byte[]]::new(10);$s[0]=0x34;Put32 $s 1 0xEDEDEDEDL;$s[5]=4;Put32 $s 6 $o.Serial
            while([datetime]::UtcNow -lt $until){Send-Plain $o $s;foreach($x in $fleet){if($x.Up){Pump-Bot $x 32}};Start-Sleep -Milliseconds 500}
            Send-Plain $o ([byte[]]@(114,0,0,50,0))}}
    # 10 iron ore weigh 200 stones (TILEDATA 20 each) against a 30-strength bot's 145: British drops them at his feet, a
    # bot packs them and steps one way until the stamina drain refuses the step (cp-overload, cp-overweight), then drops them.
    'overweight'={param($B) $o=Gm-Victim;if(-not $o -or -not $o.Pack){return};$ore=Gm-Hand $B $o '19B9' 0x19B9 10;if(-not $ore){return}
        $dir=$rng.Next(8);for($i=0;$i -lt 10;$i++){Walk $o $dir;Pump-Bot $o 16};Count 'walked-gm-overweight'
        $l=[byte[]]::new(7);$l[0]=7;Put32 $l 1 $ore;Put16 $l 5 10;Send-Plain $o $l
        $d=[byte[]]::new(14);$d[0]=8;Put32 $d 1 $ore;Put16 $d 5 $o.X;Put16 $d 7 $o.Y;$d[9]=[byte]($o.Z -band 255);Put32 $d 10 0xFFFFFFFF;Send-Plain $o $d;Pump-Bot $o 16}
    'royal'={param($B) Send-Plain $B (Say '[go 1324 1619');Pump-Bot $B 64;Walk $B 6;Pump-Bot $B 64;Send-Plain $B (Say '[go 5360 78');Pump-Bot $B 64;Walk $B 4;Pump-Bot $B 64}
    'stable'={param($B) Send-Plain $B (Say '[go 1389 1662');Pump-Bot $B 64;Send-Plain $B (Say 'horse')}
    # A claimed creature is summoned (gws-summon), so [kill on it removes it without a corpse (cb-die, cb-vanish).
    'kill-pet'={param($B) $before=@($B.Mobiles.Keys);Send-Plain $B (Say 'Claim horse');$t=0;$until=[datetime]::UtcNow.AddSeconds(3)
        while(-not $t -and [datetime]::UtcNow -lt $until){Pump-Bot $B 8;foreach($k in @($B.Mobiles.Keys)){if($before -notcontains $k -and $B.Mobiles[$k][0] -lt 400){$t=$k;break}}}
        if(-not $t){Count 'noclaim-gm-kill';return};$m=$B.Mobiles[$t];Use-Then-Target $B 0 {Target-Object $B $t $m[1] $m[2] 0 $m[0]} 'gm-kill-pet' -Speech '[kill'}
}
# UOAIX-49 C: MageryActions paths the 1-64 sweep cannot reach (mga-trap, mga-reflects, the polymorph form menu, mga-wall-index,
# mga-gate-twin, every summon). One step per British draw in a fixed cycle; each cast notes the system lines it was answered
# with ("SPELL <tag> spell=<n> <aimed|nocursor|self> said: ..."). Gates last 30 s of game time: run at -ClockScale 2.
function Spell-Cast($C,[int]$N,$Aim,[string]$Tag){
    if($C.Index -eq 1){Send-Plain $C (Say '[attr mana 100');Pump-Bot $C 16};$C.Said.Clear()
    $scText=[Text.Encoding]::ASCII.GetBytes("$N");$scPacket=[byte[]]::new(5+$scText.Length);$scPacket[0]=0x12;Put16 $scPacket 1 $scPacket.Length;$scPacket[3]=0x56;$scText.CopyTo($scPacket,4)
    $scBefore=$C.CursorAt;Send-Plain $C $scPacket;$scHow='self'
    if($Aim){$scUntil=[datetime]::UtcNow.AddSeconds(8);while($C.CursorAt -eq $scBefore -and [datetime]::UtcNow -lt $scUntil){Pump-Bot $C 1}
        if($C.CursorAt -ne $scBefore){Send-Plain $C (& $Aim);$tally['target']++;$scHow='aimed'}else{$scHow='nocursor'}}
    $scUntil=[datetime]::UtcNow.AddSeconds(2);while([datetime]::UtcNow -lt $scUntil){Pump-Bot $C 4}
    Count "$scHow-spells-$Tag";Note "SPELL $Tag spell=$N $scHow said: $((@($C.Said)|Select-Object -Unique) -join ' / ')"
}
function Spell-Said($C,[string]$Tag,[int]$Seconds){$until=[datetime]::UtcNow.AddSeconds($Seconds);while([datetime]::UtcNow -lt $until){Pump-Bot $C 4};Note "SPELL $Tag at $($C.X),$($C.Y) said: $((@($C.Said)|Select-Object -Unique) -join ' / ')";$C.Said.Clear()}
# [go answers before the bot's position does: wait for the 0x20 that puts it there, or a target is aimed from the old spot.
function Go-Wait($C,[int]$X,[int]$Y){Send-Plain $C (Say "[go $X $Y");$until=[datetime]::UtcNow.AddSeconds(4)
    while(([Math]::Abs($C.X-$X) -gt 1 -or [Math]::Abs($C.Y-$Y) -gt 1) -and [datetime]::UtcNow -lt $until){Pump-Bot $C 4};if([Math]::Abs($C.X-$X) -gt 1 -or [Math]::Abs($C.Y-$Y) -gt 1){Count 'nogo-spells'}}
# A mobile that came into view near the caster since $Before (a summon), other bots excluded.
function New-Mobile($C,$Before,[int]$Seconds){$bots=@($fleet|ForEach-Object Serial);$until=[datetime]::UtcNow.AddSeconds($Seconds)
    while([datetime]::UtcNow -lt $until){Pump-Bot $C 8;foreach($k in @($C.Mobiles.Keys)){$m=$C.Mobiles[$k];if($Before -notcontains $k -and $bots -notcontains $k -and [Math]::Max([Math]::Abs($m[1]-$C.X),[Math]::Abs($m[2]-$C.Y)) -le 4){return $k}}};return 0}
function Field-At($C,[int]$X,[int]$Y,[int]$Graphic){foreach($k in @($C.Fields.Keys)){$f=$C.Fields[$k];if($f[0] -eq $X -and $f[1] -eq $Y -and ($Graphic -eq 0 -or $f[2] -eq $Graphic)){return $k}};return 0}
# British gives a bot magery, intelligence and mana by target (the bot's own casts take no [attr).
# [skill and [attr take only the speaker's own character (csk-apply, cat-apply), so the primed bot says them itself.
function Spell-Prime($C,$O){if($O){foreach($w in @('[skill magery 100','[attr int 100','[attr mana 100')){Send-Plain $O (Say $w)};Pump-Bot $O 32}}
$spellSteps=[ordered]@{
    # A bot's kit chest, which the pack scan made lockable (locked when empty): Unlock, Magic Trap, then opening it (mg-use)
    # and Telekinesis on it (mga-container) set the trap off; Magic Lock makes Telekinesis answer "locked"; Untrap clears it.
    'trap'={param($C) $v=@($fleet|Where-Object{$_.Index -ne 1 -and $_.Up -and -not $_.Dead -and $_.Serial});if($v.Count -eq 0){Count 'noplayers-spells-trap';return}
        $o=$v[0];Open-Pack $o;$g=0x0E43;$box=Item-Of $o 0x0E43;if(-not $box){$g=0x0E41;$box=Item-Of $o 0x0E41};if(-not $box){Count 'nochest-spells-trap';return}
        Spell-Prime $C $o;Pump-Bot $o 32;$aim={Target-Object $o $box 0 0 0 $g}
        Spell-Cast $o 23 $aim 'trap-unlock';Spell-Cast $o 13 $aim 'trap-set';$o.Said.Clear();Send-Plain $o (Serial-Packet 6 $box);Spell-Said $o 'trap-open' 2
        Spell-Cast $o 13 $aim 'trap-reset';Spell-Cast $o 21 $aim 'trap-telekinesis'
        Spell-Cast $o 19 $aim 'lock';Spell-Cast $o 21 $aim 'telekinesis-locked';Spell-Cast $o 23 $aim 'unlock'
        Spell-Cast $o 13 $aim 'trap-again';Spell-Cast $o 14 $aim 'untrap'}
    # Players cannot harm each other outside a duel (admit-hook), so two bots duel. Magic Reflection is charged before the
    # duel starts (a struck caster's spell fizzles), then the challenger's Magic Arrow returns to it (mga-reflects); the
    # second arrow, at the unreflected challenger, is the control.
    'reflect'={param($C) $v=@($fleet|Where-Object{$_.Index -ne 1 -and $_.Up -and -not $_.Dead -and $_.Serial});if($v.Count -lt 2){Count 'noplayers-spells-reflect';return}
        $a=$v[0];$b=$v[1];Go-Wait $C ($a.X+2) $a.Y;Go-Wait $b ($a.X+1) $a.Y;Pump-Bot $a 16
        foreach($o in @($a,$b)){Spell-Prime $C $o}
        Spell-Cast $b 36 $null 'reflect-charge'
        Send-Plain $a (Say 'I challenge thee to a duel');Pump-Bot $a 16;Send-Plain $b (Say 'I accept');Pump-Bot $b 16;Pump-Bot $a 16
        Spell-Cast $a 5 {Target-Object $a $b.Serial $b.X $b.Y $b.Z 400} 'reflect-arrow'
        Spell-Cast $b 5 {Target-Object $b $a.Serial $a.X $a.Y $a.Z 400} 'duel-arrow'}
    # Polymorph answers with the 0x7C form menu (#A020); the answer casts the chosen form (mg-poly-answer, mga-polymorph).
    'polymorph'={param($C) if(-not (Test-Path variable:script:polyNext)){$script:polyNext=0};$C.Menu=$null;Spell-Cast $C 56 $null 'polymorph-menu'
        if($null -eq $C.Menu){Count 'nomenu-spells-polymorph';return};$m=$C.Menu;$C.Menu=$null;$choice=1+($script:polyNext++ % 18)
        $a=[byte[]]::new(13);$a[0]=0x7D;Put32 $a 1 (U32 $m 3);Put16 $a 5 (U16 $m 7);Put16 $a 7 $choice;Send-Plain $C $a;Count 'answer-spells-polymorph';Spell-Said $C "polymorph-form-$choice" 4
        Spell-Cast $C 56 $null 'polymorph-again'}
    # Every wall and field, farthest first so a stone or energy wall never stands in the line of sight of the next, each then
    # dispelled by Dispel Field aimed at one of its tiles (mga-wall-index, mga-dispel-field).
    'walls'={param($C) Go-Wait $C 1420 1698
        foreach($w in @(@(24,6),@(28,5),@(39,4),@(47,3),@(50,2))){$before=@($C.Fields.Keys);$x=$C.X+$w[1];$y=$C.Y;$z=$C.Z
            Spell-Cast $C $w[0] {Target-Ground $C $x $y $z} "wall-$($w[0])"
            $f=0;$until=[datetime]::UtcNow.AddSeconds(2);while(-not $f -and [datetime]::UtcNow -lt $until){Pump-Bot $C 8;foreach($k in @($C.Fields.Keys)){if($before -notcontains $k){$f=$k;break}}}
            if(-not $f){Count "nofield-spells-$($w[0])";continue};$at=$C.Fields[$f]
            Spell-Cast $C 34 {Target-Object $C $f $at[0] $at[1] $z $at[2]} "dispel-field-$($w[0])"}}
    # Mark a rune at one street, Gate Travel from another: the caster steps off his gate and back on (mg-gate-walk,
    # mga-gate-twin) and arrives at the mark. There he dispels the gate he arrived on, walks back to the first gate and steps
    # onto it, which answers that the other gate no longer exists.
    'gate'={param($C) $rune=Ensure-Item $C '1F14' 0x1F14 1 'spells';if(-not $rune){return};$aim={Target-Object $C $rune 0 0 0 0x1F14}
        Go-Wait $C 1475 1645;$mx=$C.X;$my=$C.Y;Spell-Cast $C 45 $aim 'mark'
        Go-Wait $C 1437 1696;Spell-Cast $C 52 $aim 'gate-open';$hx=$C.X;$hy=$C.Y
        if(-not (Field-At $C $hx $hy 0x0F6C)){Count 'nogate-spells-gate';return}
        $off=2;$C.Said.Clear();Walk $C $off;Pump-Bot $C 32;if($C.X -eq $hx -and $C.Y -eq $hy){$off=4;Walk $C $off;Pump-Bot $C 32}
        Walk $C (($off+4) % 8);Spell-Said $C 'gate-step' 2
        if([Math]::Abs($C.X-$mx) -le 1 -and [Math]::Abs($C.Y-$my) -le 1){Count 'travelled-spells-gate'}else{Count 'stayed-spells-gate';return}
        $there=Field-At $C $C.X $C.Y 0x0F6C;if(-not $there){Count 'notwin-spells-gate';return}
        Spell-Cast $C 34 {Target-Object $C $there $C.X $C.Y $C.Z 0x0F6C} 'gate-dispel'
        Go-Wait $C ($hx+1) $hy;$C.Said.Clear();Walk $C 6;Spell-Said $C 'gate-orphan' 2}
    # Each summon (Blade Spirits and Energy Vortex at a ground point, the rest beside the caster), then Dispel at it
    # (mga-dispel) and [remove, which frees its followers (UOAIX-214); last, Mass Dispel over a summon.
    'summons'={param($C) Go-Wait $C 1420 1698
        foreach($n in @(33,58,40,60,61,62,63,64)){$before=@($C.Mobiles.Keys);$x=$C.X+2;$y=$C.Y;$z=$C.Z
            if($n -eq 33 -or $n -eq 58){Spell-Cast $C $n {Target-Ground $C $x $y $z} "summon-$n"}else{Spell-Cast $C $n $null "summon-$n"}
            $t=New-Mobile $C $before 3;if(-not $t){Count "nosummon-spells-$n";continue};Count "summoned-spells-$n";$m=$C.Mobiles[$t]
            Spell-Cast $C 41 {Target-Object $C $t $m[1] $m[2] 0 $m[0]} "dispel-$n"
            if($C.Mobiles.ContainsKey($t)){$m=$C.Mobiles[$t];Use-Then-Target $C 0 {Target-Object $C $t $m[1] $m[2] 0 $m[0]} 'spells-remove' -Speech '[remove'}}
        $before=@($C.Mobiles.Keys);Spell-Cast $C 40 $null 'summon-mass';$t=New-Mobile $C $before 3
        if($t){Spell-Cast $C 54 {Target-Ground $C $C.X $C.Y $C.Z} 'mass-dispel';if($C.Mobiles.ContainsKey($t)){$m=$C.Mobiles[$t];Use-Then-Target $C 0 {Target-Object $C $t $m[1] $m[2] 0 $m[0]} 'spells-remove' -Speech '[remove'}}}
}
# UOAIX-49 B: British's graded checks of Damian's live reports, one per draw in a fixed cycle; each counts pass-<row> or
# fail-<row> and notes what it saw, so a run of -Only graded grades the rows a soak can reach.
# A vendor label names its price in two adjacent coins, "<item> at 6g 5s" (UOAIX-225): never three coins, never gold with
# copper, a space between coins; the charge is the label at the soak's royal rates, 700 copper and 70 silver to the gold.
function Grade-Price([long]$Price,[string]$Name,[string]$List){
    if($Name -notmatch ' at (?:(\d+)g(?= \d|$))? ?(?:(\d+)s(?= \d|$))? ?(?:(\d+)c)?$' -or -not ($Matches[1] -or $Matches[2] -or $Matches[3])){Count "fail-225-label";Note "PRICE $List no coin label: '$Name' price=$Price";return}
    $g=[long]$(if($Matches[1]){$Matches[1]}else{0});$s=[long]$(if($Matches[2]){$Matches[2]}else{0});$c=[long]$(if($Matches[3]){$Matches[3]}else{0})
    if(($Matches[1] -and $Matches[2] -and $Matches[3]) -or ($Matches[1] -and $Matches[3])){Count 'fail-225-label';Note "PRICE $List label takes three coins or gold with copper: '$Name'"}else{Count 'pass-225-label'}
    if($g*700+$s*10+$c -eq $Price){Count 'pass-225-charge'}else{Count 'fail-225-charge';Note "PRICE $List label '$Name' is $($g*700+$s*10+$c) copper, charge $Price"}
}
# UOAIX-49 coverage of blu's rows: British-only families castle (208), cheer (171), loom (174), fell (47) and farm (58),
# each counting pass-<row>-* or fail-<row>-* with a Note naming what it saw.
function Blu-Wait($C,[int]$Seconds){$until=[datetime]::UtcNow.AddSeconds($Seconds);while([datetime]::UtcNow -lt $until){Pump-Bot $C 16}}
function Blu-Said($C){return (@($C.Said)|Select-Object -Unique) -join ' / '}
# The reply id of the craft gump row whose text matches $Pattern ({ button ... 1 0 id }{ text x y hue index }), or 0.
function Gump-Button([byte[]]$G,[string]$Pattern){$n=U16 $G 19;$layout=[Text.Encoding]::ASCII.GetString($G,21,[Math]::Min($n,$G.Length-21));$at=21+$n;if($at+2 -gt $G.Length){return 0}
    $count=U16 $G $at;$at+=2;$texts=[Collections.Generic.List[string]]::new()
    for($i=0;$i -lt $count -and $at+2 -le $G.Length;$i++){$l=U16 $G $at;$at+=2;if($at+2*$l -gt $G.Length){break};$texts.Add([Text.Encoding]::BigEndianUnicode.GetString($G,$at,2*$l));$at+=2*$l}
    foreach($m in [regex]::Matches($layout,'\{ button \d+ \d+ \d+ \d+ 1 0 (\d+) \}\{ text \d+ \d+ \d+ (\d+) \}')){$t=[int]$m.Groups[2].Value;if($t -lt $texts.Count -and $texts[$t] -match $Pattern){return [long]$m.Groups[1].Value}};return 0}
$bluSteps=[ordered]@{
    # UOAIX-208: a fruited castle tree (planting-view leaves at trunk art + 2, cpl-draw-map) picks to fruit, then draws bare (+ 1).
    'castle'={param($C) $trees=@(@(0x0D9C,1363,1584),@(0x0D9C,1363,1589),@(0x0D94,1363,1595),@(0x0D98,1363,1601),@(0x0DA0,1367,1589),@(0x0DA0,1367,1595))
        Go-Wait $C 1365 1592;Blu-Wait $C 3;$found=0
        foreach($t in $trees){$k=@($C.Crops.Keys|Where-Object{$C.Crops[$_][0] -eq $t[1] -and $C.Crops[$_][1] -eq $t[2]})|Select-Object -First 1;if(-not $k){continue};$found++
            if($C.Crops[$k][2] -ne $t[0]+2){continue}
            Count 'pass-208-fruited';Go-Wait $C ($t[1]+1) $t[2];$C.Said.Clear();Send-Plain $C (Serial-Packet 6 $k);Blu-Wait $C 2;$said=Blu-Said $C
            Count $(if($said -match 'You reap the '){'pass-208-pick'}else{'fail-208-pick'});$after=if($C.Crops.ContainsKey($k)){$C.Crops[$k][2]}else{0}
            Count $(if($after -eq $t[0]+1){'pass-208-bare'}else{'fail-208-bare'});Note "CASTLE tree $('{0:X4}' -f $t[0]) at $($t[1]),$($t[2]): said '$said', leaves now $('{0:X4}' -f $after)";return}
        if($found -eq 0){Count 'fail-208-none';Note "CASTLE no planting-view leaves at the castle trees by 1365,1592"}else{Count 'none-208-ripe';Note "CASTLE $found trees seen, none fruited"}}
    # UOAIX-171: a skeleton (body 0x32, ai 2) slain beside townsfolk draws one of cg-cheer-line's three lines. A witness
    # speaks at most once per cg-alarm-quiet (240 game seconds, cg-alarm-fresh), and a standing shop or bank keeper is not
    # always a town NPC, so a kill is graded only beside a walking human not near a kill within that window (wall seconds
    # 240 / ClockScale + 15); otherwise it counts quiet-171 and nothing is killed.
    'cheer'={param($C) if(-not (Test-Path variable:script:cheerSeen)){$script:cheerSeen=@{}};$quiet=240/$ClockScale+15
        foreach($spot in @(@(1475,1645),@(1495,1620),@(1452,1529))){Go-Wait $C $spot[0] $spot[1];Blu-Wait $C 1
            $bots=@($fleet|ForEach-Object Serial);$near={param($r) @($C.Mobiles.Keys|Where-Object{$m=$C.Mobiles[$_];$bots -notcontains $_ -and $m[0] -ge 400 -and $m[0] -le 401 -and [Math]::Max([Math]::Abs($m[1]-$C.X),[Math]::Abs($m[2]-$C.Y)) -le $r})}
            $was=@{};foreach($k in (& $near 6)){$was[$k]="$($C.Mobiles[$k][1]),$($C.Mobiles[$k][2])"};Blu-Wait $C 2
            $folk=@($was.Keys|Where-Object{$C.Mobiles.ContainsKey($_) -and "$($C.Mobiles[$_][1]),$($C.Mobiles[$_][2])" -ne $was[$_] -and (-not $script:cheerSeen.ContainsKey($_) -or ([datetime]::UtcNow-$script:cheerSeen[$_]).TotalSeconds -gt $quiet)})
            if($folk.Count -eq 0){Count 'quiet-171';continue};foreach($k in (& $near 8)){$script:cheerSeen[$k]=[datetime]::UtcNow}
            $before=@($C.Mobiles.Keys);Send-Plain $C (Say '[summon 32');$t=New-Mobile $C $before 3;if(-not $t){Count 'nosummon-171';return}
            $C.Labels.Clear();$m=$C.Mobiles[$t];Use-Then-Target $C 0 {Target-Object $C $t $m[1] $m[2] 0 $m[0]} 'blu-cheer' -Speech '[kill'
            $lines=@('Thank the Virtues, the beast is dead','Good riddance! Still, monsters','The guards must keep them from the gates');$heard=$null;$until=[datetime]::UtcNow.AddSeconds(12)
            while([datetime]::UtcNow -lt $until -and -not $heard){Pump-Bot $C 16;foreach($v in @($C.Labels.Values)){foreach($l in $lines){if($v.Contains($l)){$heard=$v}}}}
            Count $(if($heard){'pass-171-cheer'}else{'fail-171-cheer'});Note "CHEER at $($C.X),$($C.Y) with $($folk.Count) townsfolk within 6 tiles: $(if($heard){$heard}else{'none heard'})";return}
        Count 'quiet-171-all'}
    # UOAIX-174: at Carpentry 100 the hammer's gump makes a loom deed (0x14F0, the art the house deeds share, so the deed
    # is the 0x14F0 that was not in the pack before), and the deed double-clicked places LoomSouthAddon's two pieces
    # (0x1061, 0x1062). 1998 carpentry is a direct use (Craft1998), so no skill is refused "no chance".
    'loom'={param($C) Go-Wait $C 1430 1595;$h=Ensure-Item $C '102A' 0x102A 1 'blu-loom';if(-not $h){return}
        foreach($r in @(@('[skill 11 100','make'))){Send-Plain $C (Say $r[0]);Send-Plain $C (Say '[add 1BD7 85');Send-Plain $C (Say '[add 1766 25');Blu-Wait $C 2
            $deeds=@($C.Items.Keys|Where-Object{$C.Items[$_][0] -eq 0x14F0})
            $C.Gump=$null;$C.Said.Clear();Send-Plain $C (Serial-Packet 6 $h);$until=[datetime]::UtcNow.AddSeconds(3);while($null -eq $C.Gump -and [datetime]::UtcNow -lt $until){Pump-Bot $C 4}
            if($null -eq $C.Gump){Count "fail-174-gump";continue};$g=$C.Gump;$C.Gump=$null;$id=Gump-Button $g '(?i)loom'
            if(-not $id){Count 'fail-174-row';Note "LOOM no loom row in the hammer's gump: $(Gump-Texts $g)";continue}
            $a=[byte[]]::new(23);$a[0]=0xB1;Put16 $a 1 23;Put32 $a 3 (U32 $g 3);Put32 $a 7 (U32 $g 7);Put32 $a 11 $id;Send-Plain $C $a;Blu-Wait $C 6;$said=Blu-Said $C
            $deed=@($C.Items.Keys|Where-Object{$C.Items[$_][0] -eq 0x14F0 -and $deeds -notcontains $_ -and (Owned $C $_)})|Select-Object -First 1;if(-not $deed){Count 'fail-174-deed';Note "LOOM no deed in the pack after the craft: $said";return};Count 'pass-174-deed'
            if(-not (Test-Path variable:script:loomAt)){$script:loomAt=0};Go-Wait $C (1405+3*($script:loomAt++ % 6)) 1698;$before=@($C.Ground.Keys);$C.Said.Clear();Send-Plain $C (Serial-Packet 6 $deed);Blu-Wait $C 3
            $placed=@($C.Ground.Keys|Where-Object{$before -notcontains $_ -and @(0x1061,0x1062) -contains $C.Ground[$_][2]})
            Count $(if($placed.Count -ge 2){'pass-174-loom'}else{'fail-174-loom'});Note "LOOM placed $($placed.Count) of 2 pieces from $($C.X),$($C.Y): $(Blu-Said $C)"}}
    # UOAIX-47: a tree (art 0x0C9E-0x0CEA) felled with a hatchet draws a stump (0x0E56); revisited later, a sapling
    # (0x0CE9) and then its tree again (wf-site-sync). Up to three felled trees are tracked across the run.
    'fell'={param($C) Prime $C '44';$axe=Ensure-Item $C '0F43' 0x0F43 1 'blu-fell';if(-not $axe){return}
        if(-not (Test-Path variable:script:felled)){$script:felled=@{}}
        foreach($k in @($script:felled.Keys)){$f=$script:felled[$k];Go-Wait $C ($f[0]+1) $f[1];Blu-Wait $C 2;if(-not $C.Flora.ContainsKey($k)){continue};$g=$C.Flora[$k][2]
            if($g -eq 0x0CE9 -and $f[3] -eq 0x0E56){Count 'pass-47-sapling';$f[3]=$g;Note "FELL $($f[0]),$($f[1]) a sapling"}
            elseif($g -ne 0x0E56 -and $g -ne 0x0CE9){Count 'pass-47-regrown';Note "FELL $($f[0]),$($f[1]) drawn $('{0:X4}' -f $g) again";$script:felled.Remove($k)}}
        if($script:felled.Count -ge 3){return}
        Go-Wait $C 1420 1698;Blu-Wait $C 2;$t=0;$far=99
        foreach($k in $C.Flora.Keys){$f=$C.Flora[$k];if($f[2] -lt 0x0C9E -or $f[2] -gt 0x0CEA -or $f[2] -eq 0x0CE9 -or $script:felled.ContainsKey($k)){continue};$d=[Math]::Max([Math]::Abs($f[0]-$C.X),[Math]::Abs($f[1]-$C.Y));if($d -lt $far){$far=$d;$t=$k}}
        if(-not $t){Count 'notree-47';return};$f=$C.Flora[$t];$art=$f[2];Go-Wait $C ($f[0]+1) $f[1];$C.Said.Clear()
        for($i=0;$i -lt 20 -and $C.Flora.ContainsKey($t) -and $C.Flora[$t][2] -ne 0x0E56;$i++){Use-Then-Target $C $axe {Target-Object $C $t $f[0] $f[1] 0 $art} 'blu-fell';Blu-Wait $C 2}
        Go-Wait $C ($f[0]+10) $f[1];Blu-Wait $C 1;Go-Wait $C ($f[0]+1) $f[1];Blu-Wait $C 2
        $stump=@($C.Flora.Keys|Where-Object{$C.Flora[$_][0] -eq $f[0] -and $C.Flora[$_][1] -eq $f[1] -and $C.Flora[$_][2] -eq 0x0E56})|Select-Object -First 1
        $now=if($stump){0x0E56}elseif($C.Flora.ContainsKey($t)){$C.Flora[$t][2]}else{0}
        if($stump){$t=$stump}
        if($now -eq 0x0E56){Count 'pass-47-stump';$script:felled[$t]=@($f[0],$f[1],$art,0x0E56);Note "FELL tree $('{0:X4}' -f $art) at $($f[0]),$($f[1]) is a stump"}
        else{Count 'fail-47-stump';Note "FELL tree $('{0:X4}' -f $art) at $($f[0]),$($f[1]) drawn $('{0:X4}' -f $now) after the swings: $(Blu-Said $C)"}}
    # UOAIX-58: every Royal Test Farm bed (cpl-farm-beds; timber is fell's) is drawn and yields to a hand, a trowel, a
    # shovel or a pitchfork as its reply names (cpl-dig-with); a bed regrowing says so and is not a failure.
    'farm'={param($C) $beds=@(@(10,1211,1595),@(11,1215,1595),@(13,1219,1595),@(14,1223,1595),@(17,1227,1595),@(18,1231,1595),@(19,1235,1595),@(20,1211,1600),@(27,1215,1600),@(16,1223,1600),
            @(28,1227,1600),@(29,1231,1600),@(30,1235,1600),@(31,1211,1605),@(32,1215,1605),@(33,1219,1605),@(34,1223,1605),@(35,1227,1605),@(36,1231,1605),@(37,1235,1605),@(38,1211,1610),@(39,1225,1600),@(41,1215,1610))
        $tools=@{trowel=(Ensure-Item $C '10E7' 0x10E7 1 'blu-farm');shovel=(Ensure-Item $C '0F39' 0x0F39 1 'blu-farm');pitchfork=(Ensure-Item $C '0E87' 0x0E87 1 'blu-farm')}
        Go-Wait $C 1223 1598;Blu-Wait $C 3;$seen=0
        foreach($bed in $beds){$crop=@($C.Crops.Keys|Where-Object{$C.Crops[$_][0] -eq $bed[1] -and $C.Crops[$_][1] -eq $bed[2]})|Select-Object -First 1
            if(-not $crop){Count 'fail-58-nobed';Note "FARM no bed drawn for resource $($bed[0]) at $($bed[1]),$($bed[2])";continue};$seen++
            Go-Wait $C $bed[1] ($bed[2]-1);$C.Said.Clear();Send-Plain $C (Serial-Packet 6 $crop);Blu-Wait $C 1;$said=Blu-Said $C
            $use=@($tools.Keys|Where-Object{$said -match $_});if($use.Count -gt 0 -and $tools[$use[0]]){$C.Said.Clear();$g=$C.Crops[$crop][2];Use-Then-Target $C $tools[$use[0]] {Target-Object $C $crop $bed[1] $bed[2] 0 $g} 'blu-farm';Blu-Wait $C 1;$said=Blu-Said $C}
            if($said -match '(?i)you (reap|pick|dig|gather|put|harvest)'){Count 'pass-58-yield'}elseif($said -match 'no fruit|nothing to reap|needs no more care|cannot tend that now|You tend the|fail to tend'){Count 'wait-58-regrow'}
            else{Count 'fail-58-yield';Note "FARM resource $($bed[0]) at $($bed[1]),$($bed[2]): $said"}}
        Note "FARM $seen of $($beds.Count) beds drawn"}
}
$gradedSteps=[ordered]@{
    # UOAIX-162: a single click on a monster answers its name label (cg-creature-label).
    'labels'={param($C) Send-Plain $C (Say '[goto britspawn');$until=[datetime]::UtcNow.AddSeconds(4);while([datetime]::UtcNow -lt $until){Pump-Bot $C 16}
        $bots=@($fleet|ForEach-Object Serial);$near=@($C.Mobiles.Keys|Where-Object{$m=$C.Mobiles[$_];$bots -notcontains $_ -and ($m[0] -lt 400 -or $m[0] -gt 403) -and [Math]::Max([Math]::Abs($m[1]-$C.X),[Math]::Abs($m[2]-$C.Y)) -le 12}|Select-Object -First 5)
        if($near.Count -eq 0){Count 'none-162-label';return}
        foreach($t in $near){$C.Labels.Remove($t);Send-Plain $C (Serial-Packet 9 $t);$until=[datetime]::UtcNow.AddSeconds(2);while(-not $C.Labels.ContainsKey($t) -and [datetime]::UtcNow -lt $until){Pump-Bot $C 4}
            if($C.Labels.ContainsKey($t)){Count 'pass-162-label'}else{Count 'fail-162-label';Note "LABEL none for body $($C.Mobiles[$t][0]) at $($C.Mobiles[$t][1]),$($C.Mobiles[$t][2])"}}}
    # UOAIX-169 and 225: the buy and sell lists carry every line's price in coins, and the charge is the label. The Sweet
    # Dreams innkeeper and the healer trade at every hour (other shops close from hour 20 to 8); "vendor buy" reaches the
    # nearest vendor in range. The sell list needs goods the vendor buys, so its absence is no verdict.
    'prices'={param($C) $graded=$false
        foreach($at in @(@(1497,1618),@(1473,1615))){if($graded){break};Go-Wait $C $at[0] $at[1];Pump-Bot $C 64
            foreach($side in @('buy','sell')){if($side -eq 'buy'){$C.BuyNames=$null}else{$C.SellNames=$null}
                Send-Plain $C (Say "vendor $side");$until=[datetime]::UtcNow.AddSeconds(3)
                while([datetime]::UtcNow -lt $until -and $null -eq $(if($side -eq 'buy'){$C.BuyNames}else{$C.SellNames})){Pump-Bot $C 4}
                $list=@(@(if($side -eq 'buy'){$C.BuyNames}else{$C.SellNames}) | Where-Object {$null -ne $_});if($list.Count -eq 0){Count "nolist-169-$side";continue}
                $graded=$true;Count "pass-169-$side";foreach($e in $list){Grade-Price $e[0] $e[1] $side}}}}
    # UOAIX-160 and 207: British works the winch (reach is 3 tiles in x and y, any z); every viewer, British and a bot
    # stood at the gatehouse, sees all six portcullis pieces move, raised and lowered (gcv-item-update at any z).
    'portcullis'={param($C) $o=@($fleet|Where-Object{$_.Index -ne 1 -and $_.Up -and -not $_.Dead -and $_.Serial})|Select-Object -First 1
        Go-Wait $C 1400 1626;if($o){Send-Plain $o (Say '[go 1397 1627');Pump-Bot $o 64};$until=[datetime]::UtcNow.AddSeconds(3);while([datetime]::UtcNow -lt $until){Pump-Bot $C 16;if($o){Pump-Bot $o 16}}
        if(-not $C.Winch){Count 'nowinch-160';return}
        for($r=0;$r -lt 2;$r++){$viewers=@($C)+@($o|Where-Object{$_});$before=@{};foreach($w in $viewers){$before[$w.Index]=$w.Gate.Clone()}
            Send-Plain $C (Serial-Packet 6 $C.Winch);$until=[datetime]::UtcNow.AddSeconds(4);while([datetime]::UtcNow -lt $until){foreach($w in $viewers){Pump-Bot $w 16}}
            foreach($w in $viewers){$moved=@($w.Gate.Keys|Where-Object{-not $before[$w.Index].ContainsKey($_) -or $before[$w.Index][$_] -ne $w.Gate[$_]}).Count
                $ok=$moved -ge 6;Note "GATE round $r $($w.Name) at $($w.X),$($w.Y),$($w.Z): $moved of $($w.Gate.Count) pieces moved"
                if($w.Index -eq 1){Count $(if($ok){'pass-160-winch'}else{'fail-160-winch'})};Count $(if($ok){"pass-207-z$($w.Z)"}else{"fail-207-z$($w.Z)"})}}}
    # UOAIX-221: a summoned creature left 8 tiles behind answers "all follow me" and runs to within 3 tiles.
    'follow'={param($C) Go-Wait $C 1420 1698;$before=@($C.Mobiles.Keys);Spell-Cast $C 40 $null 'follow-summon';$t=New-Mobile $C $before 3
        if(-not $t){Count 'nosummon-221';return};Go-Wait $C ($C.X+8) $C.Y;Send-Plain $C (Say 'all follow me')
        $until=[datetime]::UtcNow.AddSeconds(10);$d=99;$track=[Collections.Generic.List[string]]::new()
        while([datetime]::UtcNow -lt $until -and $d -gt 3){Pump-Bot $C 16;if($C.Mobiles.ContainsKey($t)){$m=$C.Mobiles[$t];$d=[Math]::Max([Math]::Abs($m[1]-$C.X),[Math]::Abs($m[2]-$C.Y));$at="$($m[1]),$($m[2])";if($track.Count -eq 0 -or $track[$track.Count-1] -ne $at){$track.Add($at)}}}
        Count $(if($d -le 3){'pass-221-follow'}else{'fail-221-follow'});Note "FOLLOW summon $t ends $d tiles from British"
        # A stalled follower names what it stalled beside: its track, its body, and every mobile within 2 tiles of it.
        if($d -gt 3 -and $C.Mobiles.ContainsKey($t)){$m=$C.Mobiles[$t];$near=@($C.Mobiles.Keys|Where-Object{$_ -ne $t}|ForEach-Object{$o=$C.Mobiles[$_];if([Math]::Max([Math]::Abs($o[1]-$m[1]),[Math]::Abs($o[2]-$m[2])) -le 2){"$_ body $($o[0]) at $($o[1]),$($o[2])"}})
            Note "GRADE 221 fail summon $t body $($m[0]) British at $($C.X),$($C.Y) track $($track -join ' ') near: $($near -join '; ')"}
        if($C.Mobiles.ContainsKey($t)){$m=$C.Mobiles[$t];Use-Then-Target $C 0 {Target-Object $C $t $m[1] $m[2] 0 $m[0]} 'graded-remove' -Speech '[remove'}}
    # UOAIX-220: every field cast across a street lands the tiles that fit (Wall of Stone too) and never answers that the
    # spell requirements are no longer met. A field is graded on the tile's arrival (FieldSeen): at -ClockScale 30 a
    # 10 s stone wall stands 0.33 s of wall time and is gone before Spell-Cast returns.
    'fields'={param($C) Go-Wait $C 1420 1698
        foreach($w in @(@(24,6),@(28,5),@(39,4),@(47,3),@(50,2))){$before=@($C.FieldSeen);$x=$C.X+$w[1];$y=$C.Y;$z=$C.Z
            Spell-Cast $C $w[0] {Target-Ground $C $x $y $z} "graded-field-$($w[0])"
            $f=0;$until=[datetime]::UtcNow.AddSeconds(2);while(-not $f -and [datetime]::UtcNow -lt $until){Pump-Bot $C 8;foreach($k in @($C.FieldSeen)){if($before -notcontains $k){$f=$k;break}}}
            $refused=@($C.Said|Where-Object{$_ -match 'no longer met'}).Count -gt 0
            # A fizzle is the skill roll failing, which places nothing and refuses nothing: not graded either way.
            if(-not $f -and -not $refused -and (Said-Has $C '(?i)spell fizzles')){Count "fizzled-220-$($w[0])";continue}
            Count $(if($f -and -not $refused){"pass-220-$($w[0])"}else{"fail-220-$($w[0])"})
            if(-not $f -or $refused){Note "GRADE 220 fail spell=$($w[0]) field=$f said: $(Said-Text $C)"}
            if($f -and $C.Fields.ContainsKey($f)){$at=$C.Fields[$f];Spell-Cast $C 34 {Target-Object $C $f $at[0] $at[1] $z $at[2]} "graded-dispel-$($w[0])"}elseif($f){Count "expired-220-$($w[0])"}}}
    # UOAIX-230: in Go Fish both seats' gumps follow every move (Play-Gumps counts relay- and norelay-gofish).
    'cards'={param($C) Tavern-Game $C 'gofish'}
}
# UOAIX-49 B (reek): graded checks of reek's rows, one per British draw in a fixed cycle (-Only reekgraded). Each step ends
# in one line "GRADE <row> pass|fail|unreached <what it saw>" and counts <verdict>-<row>; unreached means the step never
# reached the condition it grades (L-VACUOUS), and is never read as a pass.
function Grade([string]$Row,[string]$Verdict,[string]$Seen){Count "$Verdict-$Row";Note "GRADE $Row $Verdict $Seen"}
function Reek-Bots{@($fleet|Where-Object{$_.Index -ne 1 -and $_.Up -and -not $_.Dead -and $_.Serial})}
function Pump-All([int]$Seconds){$until=[datetime]::UtcNow.AddSeconds($Seconds);while([datetime]::UtcNow -lt $until){foreach($x in $fleet){if($x.Up){Pump-Bot $x 16}};Start-Sleep -Milliseconds 20}}
function Said-Has($B,[string]$Pattern){@($B.Said|Where-Object{$_ -match $Pattern}).Count -gt 0}
function Said-Text($B){(@($B.Said)|Select-Object -Unique) -join ' / '}
function Owned-Amount($B,[int]$Graphic){$n=0;foreach($k in @($B.Items.Keys)){if($B.Items[$k][0] -eq $Graphic -and (Owned $B $k)){$n+=[Math]::Max(1,$B.Items[$k][2])}};$n}
function Bandage-Self($O,[long]$Band){$O.Said.Clear();Use-Then-Target $O $Band {Target-Object $O $O.Serial $O.X $O.Y $O.Z 400} 'reek-205';Pump-All 2;Said-Has $O 'But that is not injured'}
function In-Box($M,[int[]]$Box){$M[1] -ge $Box[0] -and $M[1] -le $Box[2] -and $M[2] -ge $Box[1] -and $M[2] -le $Box[3]}
$reekFishBox=@(1472,1743,1478,1750)
# The NPC within 14 tiles of x, y whose single-click label names Word, or 0.
function Reek-Keeper($C,[string]$Word,[int]$X,[int]$Y){Go-Wait $C $X $Y;Pump-All 2;$bots=@($fleet|ForEach-Object Serial)
    foreach($k in @($C.Mobiles.Keys)){$m=$C.Mobiles[$k];if($bots -contains $k -or $m[0] -lt 400 -or $m[0] -gt 401 -or [Math]::Max([Math]::Abs($m[1]-$C.X),[Math]::Abs($m[2]-$C.Y)) -gt 14){continue}
        $C.Labels.Remove($k);Send-Plain $C (Serial-Packet 9 $k);$until=[datetime]::UtcNow.AddSeconds(2);while(-not $C.Labels.ContainsKey($k) -and [datetime]::UtcNow -lt $until){Pump-Bot $C 4}
        if($C.Labels.ContainsKey($k) -and $C.Labels[$k] -match "(?i)$Word"){return $k}};return 0}
# Buys one unit of the first lot of art Graphic on Keeper's buy list, then waits for it in the pack; answers the serial or 0.
function Reek-Buy($C,[long]$Keeper,[int]$Graphic){$C.BuyFrom=0;Send-Plain $C (Serial-Packet 9 $Keeper);Send-Plain $C (Say 'vendor buy')
    $until=[datetime]::UtcNow.AddSeconds(3);while(-not $C.BuyFrom -and [datetime]::UtcNow -lt $until){Pump-Bot $C 4};if(-not $C.BuyFrom){return 0}
    $lot=@($C.Items.Keys|Where-Object{$C.Items[$_][1] -eq $C.BuyFrom -and $C.Items[$_][0] -eq $Graphic})|Select-Object -First 1;if(-not $lot){return 0}
    $had=Owned-Amount $C $Graphic;$p=[byte[]]::new(15);$p[0]=0x3B;Put16 $p 1 15;Put32 $p 3 $Keeper;$p[7]=2;$p[8]=0x1A;Put32 $p 9 $lot;Put16 $p 13 1;Send-Plain $C $p
    $until=[datetime]::UtcNow.AddSeconds(3);while((Owned-Amount $C $Graphic) -le $had -and [datetime]::UtcNow -lt $until){Pump-Bot $C 4}
    return (Item-Of $C $Graphic)}
# Tally readings before a step and the rise since, for grading an existing family by its own counters.
function Reek-Delta([string[]]$Keys){$h=@{};foreach($k in $Keys){$h[$k]=[int]$tally[$k]};$h}
function Reek-Rose($Before,[string]$Key){[int]$tally[$Key] -gt $Before[$Key]}
# A board game two bots play (Tavern-Game): unreached when no piece was dragged, else pass when a refereed move landed.
function Reek-Board($C,[string]$Row,[string]$Game){$keys=@("drag-$Game","moved-$Game","noplayers-$Game","notable-$Game","nocups-$Game","nopieces-$Game");$b=Reek-Delta $keys;Tavern-Game $C $Game
    $why=(@('noplayers','notable','nocups','nopieces')|ForEach-Object{"$_+$([int]$tally["$_-$Game"]-$b["$_-$Game"])"}) -join ' '
    if(-not (Reek-Rose $b "drag-$Game")){Grade $Row 'unreached' "$Game no piece was dragged: $why";return}
    Grade $Row $(if(Reek-Rose $b "moved-$Game"){'pass'}else{'fail'}) "$Game drags $([int]$tally["drag-$Game"]-$b["drag-$Game"]), moved $([int]$tally["moved-$Game"]-$b["moved-$Game"])"}
# A gump game (Play-Gumps): unreached when no move was made, else pass when every move reached the other seat in 3 s.
function Reek-Gump($C,[string]$Row,[string]$Game){$keys=@("move-$Game","relay-$Game","norelay-$Game","ended-$Game","noplayers-$Game","notable-$Game");$b=Reek-Delta $keys;Tavern-Game $C $Game
    $d=@{};foreach($k in $keys){$d[$k]=[int]$tally[$k]-$b[$k]}
    if($d["move-$Game"] -eq 0){Grade $Row 'unreached' "$Game no move made: noplayers+$($d["noplayers-$Game"]) notable+$($d["notable-$Game"])";return}
    Grade $Row $(if($d["norelay-$Game"] -eq 0){'pass'}else{'fail'}) "$Game moves $($d["move-$Game"]), relayed $($d["relay-$Game"]), not relayed $($d["norelay-$Game"]), ended $($d["ended-$Game"])"}
$reekSteps=[ordered]@{
    # UOAIX-205: British at full hits bandaging himself hears "But that is not injured!"; at 30 hits he is bandaged (control).
    '205'={param($C) Open-Pack $C;$band=Ensure-Item $C '0E21' 0x0E21 5 'reek-205';if(-not $band){Grade '205' 'unreached' 'no bandage in British''s pack';return}
        Send-Plain $C (Say '[attr hits 100');Pump-All 1;$full=Bandage-Self $C $band;$fullSaid=Said-Text $C
        Send-Plain $C (Say '[attr hits 30');Pump-All 1;$hurt=Bandage-Self $C $band;$hurtSaid=Said-Text $C;Send-Plain $C (Say '[attr hits 100')
        Grade '205' $(if($full -and -not $hurt){'pass'}else{'fail'}) "full refused=$full (said: $fullSaid); at 30 hits refused=$hurt (said: $hurtSaid)"}
    # UOAIX-226: "bank" said 12 tiles south of the Britain banker opens the box; 25 tiles south it does not (control).
    '226'={param($C) Go-Wait $C 1438 1695;Pump-All 1;$bots=@($fleet|ForEach-Object Serial);$banker=0
        foreach($k in @($C.Mobiles.Keys)){$m=$C.Mobiles[$k];if($bots -contains $k -or $m[0] -lt 400 -or $m[0] -gt 401 -or [Math]::Max([Math]::Abs($m[1]-$C.X),[Math]::Abs($m[2]-$C.Y)) -gt 8){continue}
            $C.Labels.Remove($k);Send-Plain $C (Serial-Packet 9 $k);$until=[datetime]::UtcNow.AddSeconds(2);while(-not $C.Labels.ContainsKey($k) -and [datetime]::UtcNow -lt $until){Pump-Bot $C 4}
            if($C.Labels.ContainsKey($k) -and $C.Labels[$k] -match '(?i)banker'){$banker=$k;break}}
        if(-not $banker){Grade '226' 'unreached' "no NPC labelled banker within 8 tiles of $($C.X),$($C.Y)";return};$b=$C.Mobiles[$banker];$open=@{}
        foreach($d in @(12,25)){Go-Wait $C $b[1] ($b[2]+$d);$C.Bank=0;Send-Plain $C (Say 'bank');$until=[datetime]::UtcNow.AddSeconds(3);while(-not $C.Bank -and [datetime]::UtcNow -lt $until){Pump-Bot $C 4};$open[$d]="$([bool]$C.Bank) at $($C.X),$($C.Y)"}
        Grade '226' $(if($open[12] -like 'True*' -and $open[25] -like 'False*'){'pass'}else{'fail'}) "banker at $($b[1]),$($b[2]); 12 tiles opened=$($open[12]); 25 tiles opened=$($open[25])"}
    # UOAIX-197: a scribe at 100 uses a pen on blank scrolls; the inscription gump (5394 bytes on 2026-10-09, over the old
    # 4096 budget) arrives and British stays connected.
    '197'={param($C) Prime $C 'inscription';Send-Plain $C (Say '[skill magery 100');Open-Pack $C;$C.Said.Clear()
        $pen=Ensure-Item $C '0FBF' 0x0FBF 1 'reek-197';$scroll=Ensure-Item $C '0E34' 0x0E34 5 'reek-197';if(-not $pen -or -not $scroll){Grade '197' 'unreached' "pen=$pen scroll=$scroll";return}
        $C.Gump=$null;$big=$null;Use-Then-Target $C $pen {Target-Object $C $scroll 0 0 0 0x0E34} 'reek-197'
        $until=[datetime]::UtcNow.AddSeconds(4);while([datetime]::UtcNow -lt $until -and $null -eq $big){Pump-Bot $C 8;if($C.Gump){$big=$C.Gump;$C.Gump=$null}}
        if($null -eq $big){Grade '197' 'unreached' "no gump after the pen (said: $(Said-Text $C))";return}
        try{Pump-All 2}catch{Grade '197' 'fail' "gump $($big.Length) bytes, then: $($_.Exception.Message)";throw}
        Grade '197' $(if($big.Length -gt 4096 -and $C.Up){'pass'}else{'fail'}) "gump $($big.Length) bytes, British connected=$($C.Up)"
        $a=[byte[]]::new(23);$a[0]=0xB1;Put16 $a 1 23;Put32 $a 3 (U32 $big 3);Put32 $a 7 (U32 $big 7);Send-Plain $C $a}
    # UOAIX-206: two pairs of bots open secure trades at once: four trade-open packets (0x6F to both sides of each) and no
    # offerer hears that someone is already trading; both trades are then cancelled.
    '206'={param($C) $v=@(Reek-Bots);if($v.Count -lt 4){Grade '206' 'unreached' "$($v.Count) bots up, four needed (-Bots 5)";return}
        $pairs=@(@($v[0],$v[1]),@($v[2],$v[3]));$items=@()
        foreach($p in $pairs){Open-Pack $p[0];$s=Any-Owned $p[0];if(-not $s){$s=Gm-Hand $C $p[0] '0E21' 0x0E21 1};$items+=$s;Send-Plain $p[1] (Say "[go $($p[0].X+1) $($p[0].Y)")}
        Pump-All 2;if(@($items|Where-Object{-not $_}).Count){Grade '206' 'unreached' 'an offerer holds nothing to offer';return}
        $before=[int]$recvOps[[byte]0x6F];foreach($p in $pairs){$p[0].Said.Clear();$p[1].Said.Clear()}
        for($k=0;$k -lt 2;$k++){$a=$pairs[$k][0];$o=$pairs[$k][1];$s=$items[$k]
            $l=[byte[]]::new(7);$l[0]=7;Put32 $l 1 $s;Put16 $l 5 ([Math]::Max(1,$a.Items[$s][2]));Send-Plain $a $l
            $d=[byte[]]::new(14);$d[0]=8;Put32 $d 1 $s;Put16 $d 5 0xFFFF;Put16 $d 7 0xFFFF;Put32 $d 10 $o.Serial;Send-Plain $a $d;Pump-All 1}
        Pump-All 1;$opened=[int]$recvOps[[byte]0x6F]-$before;$busy=@($pairs|Where-Object{(Said-Has $_[0] 'already trading') -or (Said-Has $_[1] 'already trading')}).Count
        Grade '206' $(if($opened -ge 4 -and $busy -eq 0){'pass'}else{'fail'}) "0x6F packets $opened (4 open two trades), pairs told already trading: $busy"
        foreach($p in $pairs){foreach($x in $p){if($x.TradeBox){$a=[byte[]]::new(12);$a[0]=0x6F;Put16 $a 1 12;$a[3]=1;Put32 $a 4 $x.TradeBox;Send-Plain $x $a;$x.TradeBox=0}}};Pump-All 1}
    # UOAIX-172: a wild creature a bot walks up to and attacks swings back at that bot (a 0x2F naming the creature as the
    # attacker and the bot as the defender) within 20 s.
    '172'={param($C) $o=@(Reek-Bots)|Select-Object -First 1;if(-not $o){Grade '172' 'unreached' 'no bot logged in';return}
        Send-Plain $o (Say '[goto britspawn');Pump-All 4;$t=Creature $o;$until=[datetime]::UtcNow.AddSeconds(6);while(-not $t -and [datetime]::UtcNow -lt $until){Pump-All 1;$t=Creature $o}
        if(-not $t){Grade '172' 'unreached' "no creature near the bot at $($o.X),$($o.Y)";return}
        $body=$o.Mobiles[$t][0];$o.Swings.Clear();Send-Plain $o ([byte[]]@(114,1,0,50,0));Send-Plain $o (Serial-Packet 5 $t)
        $until=[datetime]::UtcNow.AddSeconds(20);$back=$false;$near=99;$went=[datetime]::MinValue
        while([datetime]::UtcNow -lt $until -and -not $back -and $o.Up){Pump-All 1;if($o.Mobiles.ContainsKey($t)){$m=$o.Mobiles[$t];$dist=[Math]::Max([Math]::Abs($m[1]-$o.X),[Math]::Abs($m[2]-$o.Y));$near=[Math]::Min($near,$dist)
                if($dist -gt 1 -and ([datetime]::UtcNow-$went).TotalSeconds -gt 3){Send-Plain $o (Say "[go $($m[1]+1) $($m[2])");$went=[datetime]::UtcNow;Send-Plain $o (Serial-Packet 5 $t)}}
            $back=$o.Swings.ContainsKey("$t>$($o.Serial)")}
        if($o.Up){Send-Plain $o ([byte[]]@(114,0,0,50,0))}
        if(-not $back -and $near -gt 1){Grade '172' 'unreached' "the bot came no nearer than $near tiles to body $body";return}
        Grade '172' $(if($back){'pass'}else{'fail'}) "body $body attacked from $near tiles; swung back=$back"}
    # UOAIX-201: at GM Magery and Eval Int, Flame Strike (7th circle) kills an orc or a ratman in one cast (Damian: "should
    # basically kill orcs and ratmen in 1 shot"); with none near the spawn, British summons an orc (body 0x11) to strike.
    '201'={param($C) Send-Plain $C (Say '[skill magery 100');Send-Plain $C (Say '[skill 16 100');Send-Plain $C (Say '[goto britspawn');Pump-All 4
        $want={param($m) @(17,42,44,45) -contains $m[0]};$t=Nearest $C $want
        if(-not $t){$before=@($C.Mobiles.Keys);Send-Plain $C (Say '[summon 0011');$t=New-Mobile $C $before 3}
        if(-not $t){Grade '201' 'unreached' "no orc or ratman near British at $($C.X),$($C.Y), and none summoned";return};$m=$C.Mobiles[$t];$body=$m[0]
        if([Math]::Max([Math]::Abs($m[1]-$C.X),[Math]::Abs($m[2]-$C.Y)) -gt 8){Go-Wait $C ($m[1]+2) $m[2];if($C.Mobiles.ContainsKey($t)){$m=$C.Mobiles[$t]}}
        $aimed=[int]$tally['aimed-spells-reek-201'];Spell-Cast $C 51 {Target-Object $C $t $m[1] $m[2] 0 $body} 'reek-201';Pump-All 3
        if([int]$tally['aimed-spells-reek-201'] -eq $aimed){Grade '201' 'unreached' "Flame Strike took no aim (said: $(Said-Text $C))";return}
        $dead=-not $C.Mobiles.ContainsKey($t)
        Grade '201' $(if($dead){'pass'}else{'fail'}) "body $body after one Flame Strike: killed=$dead (said: $(Said-Text $C))"}
    # UOAIX-233: a keeper standing in the fishmonger's shop steps about it within 30 s and never leaves its box (shops close
    # from game hour 20 to 7, when a keeper stands still: the note carries the hour).
    '233'={param($C) $f=Reek-Keeper $C 'fishmonger' 1475 1746;$keepers=@($f|Where-Object{$_})
        if($keepers.Count -eq 0){Grade '233' 'unreached' 'no NPC labelled fishmonger within 14 tiles of 1475,1746';return}
        $start=@{};foreach($k in $keepers){$start[$k]="$($C.Mobiles[$k][1]),$($C.Mobiles[$k][2])"};$moved=@{};$out=@{}
        $until=[datetime]::UtcNow.AddSeconds(30);while([datetime]::UtcNow -lt $until){Pump-Bot $C 16;foreach($k in $keepers){if(-not $C.Mobiles.ContainsKey($k)){continue};$m=$C.Mobiles[$k];if("$($m[1]),$($m[2])" -ne $start[$k]){$moved[$k]=1};if(-not (In-Box $m $reekFishBox)){$out[$k]="$($m[1]),$($m[2])"}};Start-Sleep -Milliseconds 50}
        $hour=([regex]::Matches((Read-Log),'ECONOMY ROWS hour=(\d+)')|Select-Object -Last 1|ForEach-Object{$_.Groups[1].Value})
        Grade '233' $(if($moved.Count -gt 0 -and $out.Count -eq 0){'pass'}else{'fail'}) "$($keepers.Count) NPCs in the box at economy hour $hour, $($moved.Count) moved, $($out.Count) left it $(($out.Values) -join ' ')"}
    # UOAIX-167: the keeper in the eastern Oaken Oar sells fish (its buy list holds fish art 0x09CC-0x09CF).
    '167'={param($C) $v=Reek-Keeper $C 'fishmonger' 1475 1746
        if(-not $v){Grade '167' 'unreached' 'no NPC labelled fishmonger within 14 tiles of 1475,1746';return}
        $at=$C.Mobiles[$v];if(-not (In-Box $at @(1471,1735,1483,1751))){Grade '167' 'fail' "the fishmonger stands at $($at[1]),$($at[2]), outside the eastern Oaken Oar";return}
        $C.BuyFrom=0;Send-Plain $C (Serial-Packet 9 $v);Send-Plain $C (Say 'vendor buy');$until=[datetime]::UtcNow.AddSeconds(3);while(-not $C.BuyFrom -and [datetime]::UtcNow -lt $until){Pump-Bot $C 4}
        if(-not $C.BuyFrom){Grade '167' 'unreached' "no buy list from $(if($C.Labels.ContainsKey($v)){$C.Labels[$v]}else{$v})";return}
        $lines=@($C.Items.Keys|Where-Object{$C.Items[$_][1] -eq $C.BuyFrom});$fish=@($lines|Where-Object{$C.Items[$_][0] -ge 0x09CC -and $C.Items[$_][0] -le 0x09CF}).Count
        Grade '167' $(if($fish -gt 0){'pass'}else{'fail'}) "$(if($C.Labels.ContainsKey($v)){$C.Labels[$v]}else{$v}): buy list $($lines.Count) lines, $fish fish"
        $shut=[byte[]]::new(8);$shut[0]=0x3B;Put16 $shut 1 8;Put32 $shut 3 $v;Send-Plain $C $shut}
    # UOAIX-87: British sets kindling on the ground, lights it, and cooks a raw fish steak on the fire (0x097A to 0x097B).
    '87'={param($C) Open-Pack $C;$raw=Item-Of $C 0x097A;if(-not $raw){$f=Reek-Keeper $C 'fishmonger' 1475 1746;if($f){$raw=Reek-Buy $C $f 0x097A}}
        Go-Wait $C 1420 1698;Send-Plain $C (Say '[skill camping 100');Send-Plain $C (Say '[skill cooking 100');$C.Said.Clear()
        $k=Ensure-Item $C '0DE1' 0x0DE1 1 'reek-87';if(-not $k -or -not $raw){Grade '87' 'unreached' "kindling=$k bought raw steak=$raw (a cooked steak needs a bought lot)";return}
        $l=[byte[]]::new(7);$l[0]=7;Put32 $l 1 $k;Put16 $l 5 ([Math]::Max(1,$C.Items[$k][2]));Send-Plain $C $l
        $d=[byte[]]::new(14);$d[0]=8;Put32 $d 1 $k;Put16 $d 5 ($C.X+1);Put16 $d 7 $C.Y;$d[9]=[byte]($C.Z -band 255);Put32 $d 10 0xFFFFFFFF;Send-Plain $C $d;Pump-All 1
        $C.Fires.Clear();Send-Plain $C (Serial-Packet 6 $k);$until=[datetime]::UtcNow.AddSeconds(6);while($C.Fires.Count -eq 0 -and [datetime]::UtcNow -lt $until){Pump-Bot $C 4}
        if($C.Fires.Count -eq 0){Grade '87' 'unreached' "the kindling did not light (said: $(Said-Text $C))";return}
        $f=@($C.Fires.Keys)[0];$at=$C.Fires[$f];$had=Owned-Amount $C 0x097B;$now=$had;$C.Said.Clear()
        Use-Then-Target $C $raw {Target-Object $C $f $at[0] $at[1] $C.Z $at[2]} 'reek-87';$until=[datetime]::UtcNow.AddSeconds(8)
        while($now -le $had -and [datetime]::UtcNow -lt $until){Pump-Bot $C 4;$now=Owned-Amount $C 0x097B}
        Grade '87' $(if($now -gt $had){'pass'}else{'fail'}) "fire art $($at[2]) at $($at[0]),$($at[1]); cooked steaks $had -> $now (said: $(Said-Text $C))"}
    # UOAIX-118: two bots play the Blue Boar board; a refereed drag lands (moved-backgammon rises).
    '118'={param($C) $drag=[int]$tally['drag-backgammon'];$moved=[int]$tally['moved-backgammon'];$why=@('noplayers','notable','nocups')|ForEach-Object{@{k=$_;n=[int]$tally["$_-backgammon"]}};Tavern-Game $C 'backgammon'
        if([int]$tally['drag-backgammon'] -eq $drag){Grade '118' 'unreached' ("no checker was dragged: "+(($why|ForEach-Object{"$($_.k)+$([int]$tally["$($_.k)-backgammon"]-$_.n)"}) -join ' '));return}
        Grade '118' $(if([int]$tally['moved-backgammon'] -gt $moved){'pass'}else{'fail'}) "drags $([int]$tally['drag-backgammon']-$drag), checkers moved $([int]$tally['moved-backgammon']-$moved)"}
    # UOAIX-54: every NPC in view at the bank square wears at least two things (hair, clothes) in its 0x78.
    '54'={param($C) Go-Wait $C 1437 1695;Pump-All 3;$bots=@($fleet|ForEach-Object Serial)
        $npcs=@($C.Mobiles.Keys|Where-Object{$m=$C.Mobiles[$_];$bots -notcontains $_ -and $m[0] -ge 400 -and $m[0] -le 401 -and $C.Worn.ContainsKey($_)})
        if($npcs.Count -eq 0){Grade '54' 'unreached' 'no NPC 0x78 seen';return};$bare=@($npcs|Where-Object{$C.Worn[$_] -lt 2})
        Grade '54' $(if($bare.Count -eq 0){'pass'}else{'fail'}) "$($npcs.Count) NPCs seen, $($bare.Count) wearing under two items $(($bare|Select-Object -First 5|ForEach-Object{"$_ at $($C.Mobiles[$_][1]),$($C.Mobiles[$_][2])"}) -join ', ')"}
    # UOAIX-178: two bots take the Blue Boar backgammon set, a cup each side, and a refereed drag lands.
    '178'={param($C) Reek-Board $C '178' 'backgammon'}
    # UOAIX-189: two bots play each Britain card table (Blackjack: one bot against the house); every move reaches the other seat.
    '189'={param($C) foreach($g in @('eights','gofish','poker','blackjack')){Reek-Gump $C '189' $g}}
    # UOAIX-190: two bots take the shipwright's two plans and shoot; every shot reaches the other seat.
    '190'={param($C) Reek-Gump $C '190' 'battleship'}
    # UOAIX-176: British's drawn map, set editable, takes pins and sends its course back when reopened; a round that also
    # cleared the pins (a quarter of them) sends none back and is no verdict.
    '176'={param($C) $b=Reek-Delta @('plotted-mappins','course-mappins','cleared-mappins','nomap-mappins','nocourse-mappins','noedit-mappins');Act $C 'mappins'
        if(-not (Reek-Rose $b 'plotted-mappins')){Grade '176' 'unreached' ("no pins plotted: "+((@('nomap','nocourse','noedit')|ForEach-Object{"$_+$([int]$tally["$_-mappins"]-$b["$_-mappins"])"}) -join ' '));return}
        if(Reek-Rose $b 'cleared-mappins'){Grade '176' 'unreached' 'the pins were cleared this round';return}
        Grade '176' $(if(Reek-Rose $b 'course-mappins'){'pass'}else{'fail'}) "pins plotted; course sent back on reopen=$(Reek-Rose $b 'course-mappins')"}
    # UOAIX-177: a blank book opens (0x93 header, 0x66 pages), takes a written title and page, and reopened reads the title back.
    '177'={param($C) $b=Reek-Delta @('opened-book','readback-book','noheader-book');Act $C 'book'
        if(-not (Reek-Rose $b 'opened-book')){Grade '177' 'unreached' "the book did not open (noheader+$([int]$tally['noheader-book']-$b['noheader-book']))";return}
        Grade '177' $(if(Reek-Rose $b 'readback-book'){'pass'}else{'fail'}) "written title read back=$(Reek-Rose $b 'readback-book')"}
    # UOAIX-156: British saying "Claim horse" and "Claim dragon" gets each creature at once, unpaid; both are removed after.
    '156'={param($C) $got=@();foreach($n in @('horse','dragon')){$before=@($C.Mobiles.Keys);$C.Said.Clear();Send-Plain $C (Say "Claim $n");$t=New-Mobile $C $before 3
            $got+="$n=$(if($t){'body '+$C.Mobiles[$t][0]}else{'none'})";if($t){$m=$C.Mobiles[$t];Use-Then-Target $C 0 {Target-Object $C $t $m[1] $m[2] 0 $m[0]} 'reek-156' -Speech '[remove'}}
        Grade '156' $(if(@($got|Where-Object{$_ -like '*none'}).Count -eq 0){'pass'}else{'fail'}) (($got -join ', ')+" (said: $(Said-Text $C))")}
    # UOAIX-183: a claimed horse double-clicked beside British is ridden (its mobile leaves the view); British double-clicking
    # himself sets it down again (a horse mobile reappears), which is then removed.
    '183'={param($C) $before=@($C.Mobiles.Keys);Send-Plain $C (Say 'Claim horse');$t=New-Mobile $C $before 3
        if(-not $t){Grade '183' 'unreached' "no horse claimed (said: $(Said-Text $C))";return};$m=$C.Mobiles[$t];Go-Wait $C ($m[1]+1) $m[2];Pump-All 1
        Send-Plain $C (Serial-Packet 6 $t);$until=[datetime]::UtcNow.AddSeconds(3);while($C.Mobiles.ContainsKey($t) -and [datetime]::UtcNow -lt $until){Pump-Bot $C 4}
        $rode=-not $C.Mobiles.ContainsKey($t);$before=@($C.Mobiles.Keys);Send-Plain $C (Serial-Packet 6 $C.Serial);$down=New-Mobile $C $before 3
        Grade '183' $(if($rode -and $down){'pass'}else{'fail'}) "horse body $($m[0]) ridden=$rode, set down=$([bool]$down) (said: $(Said-Text $C))"
        if($down){$m=$C.Mobiles[$down];Use-Then-Target $C 0 {Target-Object $C $down $m[1] $m[2] 0 $m[0]} 'reek-183' -Speech '[remove'}}
}
# UOAIX-49 C (fester): graded checks of fester's client-graded rows, one per British draw in a fixed cycle (-Only
# festergraded), each ending in one Grade line; unreached means the step never reached the condition it grades.
function Target-Static($B,[int]$X,[int]$Y,[int]$Z,[int]$Graphic){$p=[byte[]]::new(19);$p[0]=0x6C;$p[1]=1;Put32 $p 2 $B.Cursor;Put16 $p 11 $X;Put16 $p 13 $Y;$p[16]=$Z-band 255;Put16 $p 17 $Graphic;return ,$p}
function Fester-Remove($C,[long]$T){if($T -and $C.Mobiles.ContainsKey($T)){$m=$C.Mobiles[$T];Use-Then-Target $C 0 {Target-Object $C $T $m[1] $m[2] 0 $m[0]} 'fester-remove' -Speech '[remove'}}
function Fester-Tally([string]$Key){if($tally.ContainsKey($Key)){$tally[$Key]}else{0}}
$festerSteps=[ordered]@{
    # UOAIX-211: Energy Vortex at a clear ground point summons a vortex (Wrestling 120.0 binds, cb-skill-ceiling).
    # Energy Vortex is 8th circle, difficulty 1000 (spelskil.m), so a GM mage fizzles about half its casts: up to four casts,
    # and only a vortex (body 13) counts, not a passer-by.
    '211'={param($C) Go-Wait $C 1420 1698;$x=$C.X+2;$y=$C.Y;$z=$C.Z;$t=0;$casts=0;$said=''
        while(-not $t -and $casts -lt 4){$before=@($C.Mobiles.Keys);$c0=$C.CursorAt;$C.Said.Clear()
            Spell-Cast $C 58 {Target-Ground $C $x $y $z} 'fester-211';$said=Said-Text $C
            if($C.CursorAt -eq $c0){Grade '211' 'unreached' "no cursor for Energy Vortex (said: $said)";return};$casts++
            $found=New-Mobile $C $before 3;if($found -and $C.Mobiles[$found][0] -eq 13){$t=$found}}
        Grade '211' $(if($t){'pass'}else{'fail'}) "vortex (body 13) at $x,$y after $casts casts: $(if($t){'summoned'}else{'none'}) (said: $said)";Fester-Remove $C $t}
    # UOAIX-210: Dispel Field sends a ground cursor (type 1), and a tile answer dispels the field piece on that tile.
    '210'={param($C) Go-Wait $C 1420 1698;$before=@($C.Fields.Keys);$x=$C.X+3;$y=$C.Y;$z=$C.Z
        Spell-Cast $C 28 {Target-Ground $C $x $y $z} 'fester-210-field';$f=0;$until=[datetime]::UtcNow.AddSeconds(2)
        while(-not $f -and [datetime]::UtcNow -lt $until){Pump-Bot $C 8;foreach($k in @($C.Fields.Keys)){if($before -notcontains $k){$f=$k;break}}}
        if(-not $f){Grade '210' 'unreached' "no fire field landed at $x,$y (said: $(Said-Text $C))";return}
        $at=$C.Fields[$f];$C.CursorType=-1;$c0=$C.CursorAt;Spell-Cast $C 34 {Target-Ground $C $at[0] $at[1] $z} 'fester-210-dispel'
        if($C.CursorAt -eq $c0){Grade '210' 'unreached' "no cursor for Dispel Field (said: $(Said-Text $C))";return}
        Blu-Wait $C 2;$gone=-not $C.Fields.ContainsKey($f)
        Grade '210' $(if($gone -and $C.CursorType -eq 1){'pass'}else{'fail'}) "cursor type $($C.CursorType) (1 is ground); piece at $($at[0]),$($at[1]) gone=$gone (said: $(Said-Text $C))"}
    # UOAIX-204: the Open Spellbook macro (0x12 type 0x43, empty text) opens the carried book.
    '204'={param($C) Open-Pack $C;Blu-Wait $C 1;Open-Pack $C;Blu-Wait $C 1;$book=Item-Of $C 0x0EFA
        if(-not $book){Grade '204' 'unreached' 'British carries no spellbook (0x0EFA) in his pack';return}
        $C.Opens.Clear();$C.Said.Clear();Send-Plain $C ([byte[]]@(0x12,0,5,0x43,0));Blu-Wait $C 3;$open=$C.Opens.ContainsKey($book)
        Grade '204' $(if($open){'pass'}else{'fail'}) "macro opened the book=$open (opens: $(@($C.Opens.Keys|ForEach-Object{'{0:X}={1:X}' -f $_,$C.Opens[$_]}) -join ' ')); said: $(Said-Text $C)"}
    # UOAIX-228: a decoration bookcase (0x0A97..0x0A9C) double-clicked opens the bookcase gump 0x4D (ServUO britain.cfg spots).
    '228'={param($C) foreach($s in @(@(1497,1656),@(1414,1601),@(1545,1736))){Go-Wait $C $s[0] ($s[1]+1);Blu-Wait $C 2
            $k=@($C.Cases.Keys|Where-Object{[Math]::Max([Math]::Abs($C.Cases[$_][0]-$s[0]),[Math]::Abs($C.Cases[$_][1]-$s[1])) -le 2})|Select-Object -First 1;if(-not $k){continue}
            $at=$C.Cases[$k];$C.Opens.Clear();$C.Said.Clear();Send-Plain $C (Serial-Packet 6 $k);Blu-Wait $C 3;$open=$C.Opens.ContainsKey($k);$books=@($C.Items.Keys|Where-Object{$C.Items[$_][1] -eq $k}).Count
            Grade '228' $(if($open -and $C.Opens[$k] -eq 0x4D){'pass'}else{'fail'}) "bookcase $('{0:X4}' -f $at[2]) at $($at[0]),$($at[1]) from $($C.X),$($C.Y): gump $(if($open){'0x{0:X}' -f $C.Opens[$k]}else{'none'}), $books items inside (said: $(Said-Text $C))";return}
        Grade '228' 'unreached' 'no decoration bookcase drawn near 1497,1656, 1414,1601 or 1545,1736'}
    # UOAIX-231: a character on an account that is not British's or a soak bot's starts with its skills' tools and no testing
    # kit; lightbot is created as alchemy, anatomy and animal lore, so a mortar and pestle (0x0E9B) and no kit chest, crook or bandages.
    '231'={param($C) $n=New-Bot 9;$n.Name='lightbot';try{Connect-Bot $n}catch{Grade '231' 'unreached' "lightbot login: $($_.Exception.Message)";return}
        try{Open-Pack $n;Pump-Bot $n 64;Start-Sleep -Milliseconds 500;Pump-Bot $n 64;Open-Pack $n;$until=[datetime]::UtcNow.AddSeconds(3);while([datetime]::UtcNow -lt $until){Pump-Bot $n 16}
            if(-not $n.Pack){Grade '231' 'unreached' 'no backpack seen on lightbot';return}
            $mine=@($n.Items.Keys|Where-Object{Owned $n $_});$kit=@($mine|Where-Object{@(0x0E43,0x0E41,0x0E81,0x0E21) -contains $n.Items[$_][0]}).Count;$mortar=Item-Of $n 0x0E9B
            Grade '231' $(if($mortar -and $kit -eq 0){'pass'}else{'fail'}) "lightbot pack holds $($mine.Count) items: mortar=$([bool]$mortar), kit pieces=$kit ($(@($mine|ForEach-Object{'{0:X4}' -f $n.Items[$_][0]}|Sort-Object -Unique) -join ' '))"}
        finally{try{$n.Tcp.Dispose()}catch{}}}
    # UOAIX-223: a hatchet swing at a yew tree (static 0x12BB at 406,937, Yew: the trunk; 0x12BC beside it is not impassable, so the walk map
    # holds no collider for it and the harvest refuses it, cpr-static) raises an orc (body 17) below Lumberjacking 100.0, none at 100.0.
    '223'={param($C) $axe=Ensure-Item $C '0F43' 0x0F43 1 'fester-223';if(-not $axe){Grade '223' 'unreached' 'no hatchet';return};$orcs=@{};$swung=@{}
        foreach($sk in @(50,100)){Send-Plain $C (Say "[skill 44 $sk");Go-Wait $C 408 936;Blu-Wait $C 2;$before=@($C.Mobiles.Keys);$C.Said.Clear();$c0=$C.CursorAt
            Use-Then-Target $C $axe {Target-Static $C 406 937 0 0x12BB} 'fester-223';$swung[$sk]=$C.CursorAt -ne $c0
            $t=New-Mobile $C $before 4;$orcs[$sk]=if($t -and $C.Mobiles[$t][0] -eq 17){$t}else{0};Note "YEW at Lumberjacking $sk swung=$($swung[$sk]) orc=$($orcs[$sk]) said: $(Said-Text $C)";Fester-Remove $C $orcs[$sk];Blu-Wait $C 3}
        if(-not $swung[50] -or -not $swung[100]){Grade '223' 'unreached' "no hatchet cursor (50: $($swung[50]), 100: $($swung[100]))";return}
        Grade '223' $(if($orcs[50] -and -not $orcs[100]){'pass'}else{'fail'}) "orc at Lumberjacking 50: $([bool]$orcs[50]); at 100: $([bool]$orcs[100])"}
    # UOAIX-77: a GM mage with GM Evaluating Intelligence casts Magic Arrow 8 times at a dueling bot with Resisting Spells
    # 100.0, then 8 at 0: at equal skills the resister resists about 55% of casts, with none it never resists.
    '77'={param($C) $v=@(Reek-Bots);if($v.Count -lt 2){Grade '77' 'unreached' "$($v.Count) bots up, two needed";return}
        $a=$v[0];$b=$v[1];Go-Wait $C ($a.X+2) $a.Y;Send-Plain $b (Say "[go $($a.X+1) $($a.Y)");Pump-All 2
        # [skill and [attr take only a character of the speaker's own account, so each bot sets its own (cat-apply, csk-apply).
        foreach($w in @('[skill 25 100','[skill 16 100','[attr int 100')){Send-Plain $a (Say $w)};Pump-All 1
        Send-Plain $a (Say 'I challenge thee to a duel');Pump-All 1;Send-Plain $b (Say 'I accept');Pump-All 1;$seen=@{}
        foreach($r in @(100,0)){Send-Plain $b (Say "[skill 26 $r");Pump-All 1;$n=0;$cast=0
            for($i=0;$i -lt 8;$i++){Send-Plain $a (Say '[attr mana 100');Send-Plain $b (Say '[attr hits 100');Pump-All 1;$c0=$a.CursorAt
                Spell-Cast $a 5 {Target-Object $a $b.Serial $b.X $b.Y $b.Z 400} "fester-77-$r";if($a.CursorAt -ne $c0){$cast++;if(Said-Has $a 'resisting magical energy'){$n++}}}
            $seen[$r]=@($n,$cast)}
        if($seen[100][1] -lt 6 -or $seen[0][1] -lt 6){Grade '77' 'unreached' "aimed casts: $($seen[100][1]) at resist 100, $($seen[0][1]) at 0";return}
        Grade '77' $(if($seen[100][0] -ge 1 -and $seen[0][0] -eq 0){'pass'}else{'fail'}) "resisted $($seen[100][0]) of $($seen[100][1]) at Resisting Spells 100, $($seen[0][0]) of $($seen[0][1]) at 0"}
    # UOAIX-68: the smith inside the Hammer And Anvil answers vendor buy from the shop floor, and not across the west wall
    # (x 1415, STATICS0) from 1414; another vendor answering outside makes the step unreached, never a pass.
    '68'={param($C) Go-Wait $C 1418 1547;Blu-Wait $C 2;$C.BuyNames=$null;$C.BuyFrom=0;Send-Plain $C (Say 'vendor buy');$until=[datetime]::UtcNow.AddSeconds(3);while($null -eq $C.BuyNames -and [datetime]::UtcNow -lt $until){Pump-Bot $C 4}
        # Shops close from hour 20 to 7 and the soak starts at night: an unanswered call moves the clock 12 game hours once and asks again.
        if(-not $C.BuyFrom){Send-Plain $C (Say '[time 12');Pump-Bot $C 32;Blu-Wait $C 2;$C.BuyNames=$null;$C.BuyFrom=0;Send-Plain $C (Say 'vendor buy');$until=[datetime]::UtcNow.AddSeconds(3);while($null -eq $C.BuyNames -and [datetime]::UtcNow -lt $until){Pump-Bot $C 4};Note "GRADE-NOTE 68 moved the clock 12 hours; answered after: $([bool]$C.BuyFrom)"}
        $inside=$C.BuyFrom;if(-not $inside){Grade '68' 'unreached' "no vendor answered on the smithy floor at $($C.X),$($C.Y), at either hour";return}
        $bots=@($fleet|ForEach-Object Serial);$k=@($C.Mobiles.Keys|Where-Object{$m=$C.Mobiles[$_];$bots -notcontains $_ -and $m[0] -ge 400 -and $m[0] -le 401 -and $m[1] -ge 1416 -and $m[1] -le 1421 -and $m[2] -ge 1543 -and $m[2] -le 1551})|Select-Object -First 1
        if(-not $k){Grade '68' 'unreached' 'no keeper drawn inside the smithy';return};$ky=$C.Mobiles[$k][2];$kx=$C.Mobiles[$k][1]
        if($kx-1414 -gt 4){Grade '68' 'unreached' "keeper at $kx,$ky, past the 4-tile call range from 1414";return}
        Go-Wait $C 1414 $ky;Blu-Wait $C 2;$C.BuyNames=$null;$C.BuyFrom=0;Send-Plain $C (Say 'vendor buy');$until=[datetime]::UtcNow.AddSeconds(3);while($null -eq $C.BuyNames -and [datetime]::UtcNow -lt $until){Pump-Bot $C 4}
        if($C.BuyFrom -and $C.BuyFrom -ne $inside){Grade '68' 'unreached' "another vendor answered at 1414,$ky";return}
        Grade '68' $(if(-not $C.BuyFrom){'pass'}else{'fail'}) "keeper at $kx,$ky; answered on the floor, through the wall from $($C.X),$($C.Y): $([bool]$C.BuyFrom)"
        $close=[byte[]]::new(8);$close[0]=0x3B;Put16 $close 1 8;Put32 $close 3 $inside;Send-Plain $C $close}
    # UOAIX-56: every NPC's click label is "<Name> the <profession>"; a player not yet known reads "a/an <title>", and
    # once it says its name the label is that name.
    '56'={param($C) Go-Wait $C 1437 1695;Blu-Wait $C 2;$bots=@($fleet|ForEach-Object Serial)
        $npcs=@(@($C.Mobiles.Keys|Where-Object{$m=$C.Mobiles[$_];$bots -notcontains $_ -and $m[0] -ge 400 -and $m[0] -le 401 -and [Math]::Max([Math]::Abs($m[1]-$C.X),[Math]::Abs($m[2]-$C.Y)) -le 8})|Select-Object -First 5)
        if($npcs.Count -eq 0){Grade '56' 'unreached' "no NPC within 8 tiles of $($C.X),$($C.Y)";return};$bad=@()
        foreach($t in $npcs){$C.Labels.Remove($t);Send-Plain $C (Serial-Packet 9 $t);$until=[datetime]::UtcNow.AddSeconds(2);while(-not $C.Labels.ContainsKey($t) -and [datetime]::UtcNow -lt $until){Pump-Bot $C 4}
            $l=if($C.Labels.ContainsKey($t)){$C.Labels[$t]}else{''};if($l -notmatch '^[A-Z][a-z]+ the \S'){$bad+="'$l'"}}
        $o=@(Reek-Bots)|Select-Object -First 1;$player='no bot up'
        if($o){Go-Wait $C ($o.X+1) $o.Y;$C.Labels.Remove($o.Serial);Send-Plain $C (Serial-Packet 9 $o.Serial);Blu-Wait $C 2;$first=if($C.Labels.ContainsKey($o.Serial)){$C.Labels[$o.Serial]}else{''}
            Send-Plain $o (Say "my name is $($o.Name)");Pump-All 2;$C.Labels.Remove($o.Serial);Send-Plain $C (Serial-Packet 9 $o.Serial);Blu-Wait $C 2;$then=if($C.Labels.ContainsKey($o.Serial)){$C.Labels[$o.Serial]}else{''}
            $player="'$first' then '$then'";if($then -notmatch [regex]::Escape($o.Name) -or ($first -notmatch '^an? ' -and $first -notmatch [regex]::Escape($o.Name))){$bad+="player $player"}}
        Grade '56' $(if($bad.Count -eq 0){'pass'}else{'fail'}) "$($npcs.Count) NPC labels, $($bad.Count) wrong: $($bad -join ', '); player $player"}
    # UOAIX-188: chess and checkers at their live boards: the server takes a legal drag (a piece seen at its target).
    # Finishing a game is still Damian's grade.
    '188'={param($C) foreach($fam in @('chess','checkers')){$d0=Fester-Tally "drag-$fam";$m0=Fester-Tally "moved-$fam";Tavern-Game $C $fam;$d=(Fester-Tally "drag-$fam")-$d0;$m=(Fester-Tally "moved-$fam")-$m0
            if($d -eq 0){Grade "188-$fam" 'unreached' "no drag (notable=$(Fester-Tally "notable-$fam"), nopieces=$(Fester-Tally "nopieces-$fam"), noplayers=$(Fester-Tally "noplayers-$fam"))";continue}
            Grade "188-$fam" $(if($m -gt 0){'pass'}else{'fail'}) "$m of $d drags moved a piece"}}
    # UOAIX-247: a bot attacks another bot it has no duel with (77 duels the first two): 247-fight is a swing within 10 s
    # that the defender's client sees too; once per run British then drops the defender to 1 hit point and 247-kill is
    # its death (0x2C) within 20 s. The attacker's notoriety British sees after is noted (grey waits on a witness report, Law.md).
    '247'={param($C) $v=@(Reek-Bots);if($v.Count -lt 3){Grade '247-fight' 'unreached' "$($v.Count) bots up, three needed (-Bots 4)";return}
        $a=$v[1];$d=$v[2];Go-Wait $C ($a.X+2) ($a.Y+1);Send-Plain $d (Say "[go $($a.X+1) $($a.Y)");Pump-All 2;$a.Swings.Clear();$d.Swings.Clear()
        Send-Plain $a ([byte[]]@(114,1,0,50,0));Send-Plain $a (Serial-Packet 5 $d.Serial);Pump-All 10
        $key="$($a.Serial)>$($d.Serial)";$hit=$a.Swings.ContainsKey($key);$seen=$d.Swings.ContainsKey($key)
        Grade '247-fight' $(if($hit -and $seen){'pass'}else{'fail'}) "$($a.Name) at $($a.X),$($a.Y) attacked $($d.Name) at $($d.X),$($d.Y): swung=$hit, defender saw it=$seen (said: $(Said-Text $a))"
        if(-not $hit -or (Test-Path variable:script:festerKilled)){Send-Plain $a ([byte[]]@(114,0,0,50,0));Pump-All 1;return};$script:festerKilled=1
        Send-Plain $d (Say '[attr hits 1');$until=[datetime]::UtcNow.AddSeconds(20);while(-not $d.Dead -and [datetime]::UtcNow -lt $until){Pump-All 1}
        Send-Plain $a ([byte[]]@(114,0,0,50,0));Grade '247-kill' $(if($d.Dead){'pass'}else{'fail'}) "$($d.Name) dead=$($d.Dead) after 20 s at 1 hit point"
        Go-Wait $C ($a.X+30) $a.Y;Blu-Wait $C 1;Go-Wait $C ($a.X+2) ($a.Y+1);Blu-Wait $C 2
        Note "GRADE-NOTE 247 $($a.Name) notoriety seen by British: $(if($C.Noto.ContainsKey($a.Serial)){$C.Noto[$a.Serial]}else{'none'}) (1 innocent, 4 criminal, 6 murderer)"
        # The defender is raised at once (as the healing family's British does), or it stays a ghost and every later run is unreached.
        if($d.Dead){$band=Ensure-Item $C '0E21' 0x0E21 10 'healing';if($band){Send-Plain $C (Say '[skill anatomy 100');Send-Plain $C (Say '[skill healing 100');Send-Plain $C (Say "[go $($d.X+1) $($d.Y)");Pump-Bot $C 32
            Use-Then-Target $C $band {Target-Object $C $d.Serial $d.X $d.Y $d.Z 400} 'fester-247-raise';$until=[datetime]::UtcNow.AddSeconds(8);while($d.Dead -and [datetime]::UtcNow -lt $until){Pump-All 1}}
            Note "GRADE-NOTE 247 $($d.Name) raised=$(-not $d.Dead)"}}
}
# UOAIX-49 (red): graded checks of red's rows, one per British draw in a fixed cycle (-Only redgraded), each ending in one
# Grade line; unreached means the step never reached the condition it grades (L-VACUOUS). Shops open from game hour 8 to
# 20 (bsh-open-hour, bsh-close-hour); 229 and 224 move the testing clock into their window first.
function Red-Label($C,[long]$K){$C.Labels.Remove($K);Send-Plain $C (Serial-Packet 9 $K);$until=[datetime]::UtcNow.AddSeconds(2);while(-not $C.Labels.ContainsKey($K) -and [datetime]::UtcNow -lt $until){Pump-Bot $C 4};if($C.Labels.ContainsKey($K)){$C.Labels[$K]}else{''}}
function Red-Hour{$h=[regex]::Matches((Read-Log),'ECONOMY ROWS hour=(\d+)')|Select-Object -Last 1;if($h){[int]$h.Groups[1].Value % 24}else{-1}}
function Red-Open([int]$H){$H -ge 8 -and $H -lt 20}
# Moves the town clock ("[time N", testing mode) so the economy hour lands at $Target, then waits for an ECONOMY ROWS
# line inside the wanted window (open or shut); answers that hour, or -1 when none arrived within 120 s.
function Red-To-Hour($C,[bool]$Open,[int]$Target){$h=Red-Hour;if($h -ge 0 -and (Red-Open $h) -eq $Open){return $h}
    $n=($Target-[Math]::Max(0,$h)+24)%24;if($n -eq 0){$n=24};Send-Plain $C (Say "[time $n");$until=[datetime]::UtcNow.AddSeconds(120)
    while([datetime]::UtcNow -lt $until){Pump-All 2;$h=Red-Hour;if($h -ge 0 -and (Red-Open $h) -eq $Open){return $h}};return -1}
function Red-Close-Gump($C){if($C.Gump){$a=[byte[]]::new(23);$a[0]=0xB1;Put16 $a 1 23;Put32 $a 3 (U32 $C.Gump 3);Put32 $a 7 (U32 $C.Gump 7);Send-Plain $C $a;$C.Gump=$null}}
$redTinkerBox=@(1422,1648,1429,1663)
$redTinkerTools=@(0x1EB8,0x1EBC)
$redSteps=[ordered]@{
    # UOAIX-229: the Tinkers Guild keeper sells tinker's tools with no "quote absent or expired" refusal, and the bought
    # tool used from the pack opens the craft menu (not "The tool must be on your person").
    '229'={param($C) $h=Red-To-Hour $C $true 10;if($h -lt 0){Grade '229' 'unreached' "the clock reached no open hour (8 to 19) within 120 s, last hour $(Red-Hour)";return}
        Go-Wait $C 1425 1655;Pump-All 2;$bots=@($fleet|ForEach-Object Serial);$v=0
        foreach($k in @($C.Mobiles.Keys)){$m=$C.Mobiles[$k];if($bots -contains $k -or $m[0] -lt 400 -or $m[0] -gt 401 -or -not (In-Box $m $redTinkerBox)){continue};if((Red-Label $C $k) -match '(?i)tinker'){$v=$k;break}}
        if(-not $v){Grade '229' 'unreached' "no NPC labelled tinker in the Tinkers Guild at game hour $h";return}
        $C.Said.Clear();$C.BuyFrom=0;Send-Plain $C (Serial-Packet 9 $v);Send-Plain $C (Say 'vendor buy');$until=[datetime]::UtcNow.AddSeconds(3);while(-not $C.BuyFrom -and [datetime]::UtcNow -lt $until){Pump-Bot $C 4}
        if(-not $C.BuyFrom){Grade '229' 'unreached' "no buy list from $($C.Labels[$v]) (said: $(Said-Text $C))";return}
        $lots=@($C.Items.Keys|Where-Object{$C.Items[$_][1] -eq $C.BuyFrom});$lot=@($lots|Where-Object{$redTinkerTools -contains $C.Items[$_][0]})|Select-Object -First 1
        $seller=if($C.Shop.ContainsKey($C.BuyFrom)){$C.Shop[$C.BuyFrom]}else{$v}
        if(-not $lot){Grade '229' 'fail' "$($C.Labels[$v]) lists $($lots.Count) lines and no tinker's tools";$shut=[byte[]]::new(8);$shut[0]=0x3B;Put16 $shut 1 8;Put32 $shut 3 $seller;Send-Plain $C $shut;return}
        $had=0;foreach($g in $redTinkerTools){$had+=Owned-Amount $C $g};$now=$had
        $p=[byte[]]::new(15);$p[0]=0x3B;Put16 $p 1 15;Put32 $p 3 $seller;$p[7]=2;$p[8]=0x1A;Put32 $p 9 $lot;Put16 $p 13 1;Send-Plain $C $p
        $until=[datetime]::UtcNow.AddSeconds(4);while($now -le $had -and [datetime]::UtcNow -lt $until){Pump-Bot $C 4;$now=0;foreach($g in $redTinkerTools){$now+=Owned-Amount $C $g}}
        $refused=Said-Has $C '(?i)quote absent|expired'
        if($now -le $had){Grade '229' 'fail' "bought no tinker's tools ($had -> $now); quote refused=$refused (said: $(Said-Text $C))";return}
        $tool=@($C.Items.Keys|Where-Object{$redTinkerTools -contains $C.Items[$_][0] -and (Owned $C $_)})|Select-Object -First 1
        $C.Said.Clear();$C.Gump=$null;$C.Menu=$null;Send-Plain $C (Serial-Packet 6 $tool);$until=[datetime]::UtcNow.AddSeconds(4);while($null -eq $C.Gump -and $null -eq $C.Menu -and [datetime]::UtcNow -lt $until){Pump-Bot $C 4}
        $menu=($null -ne $C.Gump -or $null -ne $C.Menu);$banked=Said-Has $C '(?i)must be on your person';Red-Close-Gump $C
        Grade '229' $(if(-not $refused -and $menu -and -not $banked){'pass'}else{'fail'}) "bought tinker's tools $had -> $now, quote refused=$refused; the tool from the pack opened a craft menu=$menu, told on-your-person=$banked (said: $(Said-Text $C))"}
    # UOAIX-224: at night a Tinkers Guild door answers when the shop opens ("... closed, till 8am (N minutes)").
    '224'={param($C) $h=Red-To-Hour $C $false 22;if($h -lt 0){Grade '224' 'unreached' "the clock reached no shut hour (20 to 7) within 120 s, last hour $(Red-Hour)";return}
        Go-Wait $C 1425 1655;Pump-All 2
        $doors=@($C.Doors.Keys|Where-Object{$d=$C.Doors[$_];$d[0] -ge $redTinkerBox[0]-1 -and $d[0] -le $redTinkerBox[2]+1 -and $d[1] -ge $redTinkerBox[1]-1 -and $d[1] -le $redTinkerBox[3]+1})
        if($doors.Count -eq 0){Grade '224' 'unreached' "no door item seen at the Tinkers Guild ($($C.Doors.Count) doors in view)";return}
        $C.Said.Clear();foreach($d in $doors){$at=$C.Doors[$d];Go-Wait $C $at[0] $at[1];Send-Plain $C (Serial-Packet 6 $d);Pump-All 1}
        $note=@($C.Said|Where-Object{$_ -match '(?i)closed' -and $_ -match '(?i)till'})
        Grade '224' $(if($note.Count -gt 0){'pass'}else{'fail'}) "game hour $h, $($doors.Count) doors used; said: $(Said-Text $C)"}
    # UOAIX-159: on Castle Britain's main floor at 1341,1612, a guard of the castle patrol comes within the 36-tile earshot
    # on British's floor within 90 s, so a call from there can be answered.
    '159'={param($C) Go-Wait $C 1341 1612;Pump-All 2;$z=$C.Z;$bots=@($fleet|ForEach-Object Serial);$named=@{};$near=99;$seen=''
        $until=[datetime]::UtcNow.AddSeconds(90)
        while([datetime]::UtcNow -lt $until -and $near -gt 36){Pump-All 1
            foreach($k in @($C.Mobiles.Keys)){$m=$C.Mobiles[$k];if($bots -contains $k -or $m[0] -lt 400 -or $m[0] -gt 401){continue};if(-not $named.ContainsKey($k)){$named[$k]=Red-Label $C $k}
                if($named[$k] -match '(?i)guard' -and [Math]::Abs($m[3]-$z) -le 10){$d=[Math]::Max([Math]::Abs($m[1]-$C.X),[Math]::Abs($m[2]-$C.Y));if($d -lt $near){$near=$d;$seen="$($named[$k]) at $($m[1]),$($m[2]),$($m[3])"}}}}
        if($z -lt 60){Grade '159' 'unreached' "British stood at $($C.X),$($C.Y) z $z, not the castle's main floor";return}
        Grade '159' $(if($near -le 36){'pass'}else{'fail'}) "British at $($C.X),$($C.Y) z $z; nearest guard on the floor $near tiles $seen"}
    # UOAIX-39: iron is mined from a mountainside land tile (ids 240-243) west of the Hammer And Anvil; a street tile is not
    # minable (control).
    '39'={param($C) Prime $C 'mining';Open-Pack $C;$pick=Ensure-Item $C '0E86' 0x0E86 1 'red-39';if(-not $pick){Grade '39' 'unreached' 'no pickaxe reached British';return}
        $said=@{};foreach($spot in @(@(1404,1534,1404,1533,12),@(1425,1655,1425,1656,0))){Go-Wait $C $spot[0] $spot[1];Pump-All 1;$C.Said.Clear()
            Use-Then-Target $C $pick {Target-Ground $C $spot[2] $spot[3] $spot[4]} 'red-39';$until=[datetime]::UtcNow.AddSeconds(6);while($C.Said.Count -eq 0 -and [datetime]::UtcNow -lt $until){Pump-Bot $C 4};Pump-All 1
            $said["$($spot[2]),$($spot[3])"]=Said-Text $C}
        $mountain=$said['1404,1533'];$street=$said['1425,1656']
        if(-not $mountain){Grade '39' 'unreached' 'the mountainside swing said nothing';return}
        $mined=$mountain -match '(?i)place some ore|sifted through the dirt|no metal to mine here';$refused=$mountain -match '(?i)cannot mine there';$control=$street -match '(?i)cannot mine there'
        Grade '39' $(if($mined -and -not $refused -and $control){'pass'}else{'fail'}) "mountainside 1404,1533 (land 242): $mountain; street 1425,1656 (control, refused=$control): $street"}
    # UOAIX-76: British eats a cooked fish steak from the pack: the 1998 eating line, and one steak fewer.
    '76'={param($C) Open-Pack $C;$s=Ensure-Item $C '097B' 0x097B 2 'red-76';if(-not $s){Grade '76' 'unreached' 'no cooked fish steak reached British';return}
        $had=Owned-Amount $C 0x097B;$C.Said.Clear();Send-Plain $C (Serial-Packet 6 $s);$until=[datetime]::UtcNow.AddSeconds(4);while($C.Said.Count -eq 0 -and [datetime]::UtcNow -lt $until){Pump-Bot $C 4};Pump-All 1
        $ate=Said-Has $C '(?i)eat the food|after eating the food|consuming the food|too full to eat';$now=Owned-Amount $C 0x097B
        Grade '76' $(if($ate -and ($now -lt $had -or (Said-Has $C '(?i)too full'))){'pass'}else{'fail'}) "steaks $had -> $now (said: $(Said-Text $C))"}
    # UOAIX-35: a dagger on a raw fish in British's own pack slices it into 4 raw fish steaks (UOX3 sword.js).
    '35'={param($C) Open-Pack $C;$blade=Ensure-Item $C '0F52' 0x0F52 1 'red-35';$fish=Ensure-Item $C '09CC' 0x09CC 1 'red-35'
        if(-not $blade -or -not $fish){Grade '35' 'unreached' "dagger=$blade fish=$fish";return}
        $had=Owned-Amount $C 0x097A;$C.Said.Clear();Use-Then-Target $C $blade {Target-Object $C $fish 0 0 0 0x09CC} 'red-35'
        $until=[datetime]::UtcNow.AddSeconds(5);$now=$had;while($now -le $had -and [datetime]::UtcNow -lt $until){Pump-Bot $C 4;$now=Owned-Amount $C 0x097A}
        Grade '35' $(if($now -ge $had+4){'pass'}else{'fail'}) "raw fish steaks $had -> $now from one fish (said: $(Said-Text $C))"}
    # UOAIX-40: ore from two successful swings at the mountainside merges into stacks of its grade: no more ore stacks
    # than grades (3), and more ore than stacks.
    '40'={param($C) Prime $C 'mining';Open-Pack $C;$pick=Ensure-Item $C '0E86' 0x0E86 1 'red-40';if(-not $pick){Grade '40' 'unreached' 'no pickaxe reached British';return}
        Go-Wait $C 1404 1534;$yields=0;for($k=0;$k -lt 8 -and $yields -lt 2;$k++){$C.Said.Clear();Use-Then-Target $C $pick {Target-Ground $C 1404 1533 12} 'red-40'
            $until=[datetime]::UtcNow.AddSeconds(6);while($C.Said.Count -eq 0 -and [datetime]::UtcNow -lt $until){Pump-Bot $C 4};Pump-All 1;if(Said-Has $C '(?i)place some ore'){$yields++}}
        if($yields -lt 2){Grade '40' 'unreached' "$yields ore yields in 8 swings";return}
        $ore=@($C.Items.Keys|Where-Object{$C.Items[$_][0] -ge 0x19B7 -and $C.Items[$_][0] -le 0x19BA -and (Owned $C $_)});$amount=0;foreach($k in $ore){$amount+=[Math]::Max(1,$C.Items[$k][2])}
        Grade '40' $(if($ore.Count -le 3 -and $amount -gt $ore.Count){'pass'}else{'fail'}) "$yields yields; $($ore.Count) ore stacks holding $amount ore"}
}
# A tavern table (UOAIX-178, 188, 189, 190): the world item nearest a spot with one of the graphics. The live table is
# a world item beside the scenery one; flora, crops and decoration (serials from 0x7C000000, WorldDecoration) are skipped.
function Table-At($B,[int[]]$Graphics,[int]$X,[int]$Y){$best=0;$far=99;foreach($k in $B.Ground.Keys){$t=$B.Ground[$k];if($Graphics -notcontains $t[2] -or $k -ge 0x7C000000){continue};$d=[Math]::Max([Math]::Abs($t[0]-$X),[Math]::Abs($t[1]-$Y));if($d -lt $far){$far=$d;$best=$k}};return $best}
# The texts after a gump's layout (count, then length and big-endian UCS-2 text each), joined.
function Gump-Texts([byte[]]$G){$n=U16 $G 19;$at=21+$n;if($at+2 -gt $G.Length){return ''};$count=U16 $G $at;$at+=2;$out=[Text.StringBuilder]::new()
    for($i=0;$i -lt $count -and $at+2 -le $G.Length;$i++){$l=U16 $G $at;$at+=2;if($at+2*$l -gt $G.Length){break};[void]$out.Append([Text.Encoding]::BigEndianUnicode.GetString($G,$at,2*$l)).Append(' | ');$at+=2*$l};return $out.ToString()}
# Two bots play a gump game (cards 0x43415244, battleship tag 0x4253) to its end or the deadline: each presses a random
# reply button on its newest gump of the game (the chooser's $Choice when offered); one with no gump re-opens its table.
# The game's end is a gump or line naming a win, a draw or a sunk fleet.
# Every bot logged in but not seated is read too while a table game runs, so a watcher's socket does not fill and drop it.
function Pump-Watchers($Players){foreach($x in $fleet){if($x.Up -and @($Players|Where-Object{$_.Index -eq $x.Index}).Count -eq 0){Pump-Bot $x 64}}}
# The seat's socket timing since the press it is owed a gump for: first bytes and packets read (UOAIX-230 late relays).
function Since-Text($Since,$P){if(-not $Since.ContainsKey($P.Index)){return ''};$s=$Since[$P.Index];"first bytes $(if($s.first -lt 0){'none'}else{"at $($s.first) ms"}), $($P.Received-$s.r0) packets read"}
function Play-Gumps($Players,[long[]]$Tables,[long]$Mask,[long]$Id,[string]$Family,[int]$Seconds,[long]$Choice){
    $deadline=[datetime]::UtcNow.AddSeconds($Seconds);$quiet=@{};$owed=@{};$late=@{};$since=@{};$last='';foreach($p in $Players){$quiet[$p.Index]=[datetime]::UtcNow}
    while([datetime]::UtcNow -lt $deadline){
        Pump-Watchers $Players
        for($k=0;$k -lt $Players.Count;$k++){$p=$Players[$k]
            # When an owed seat's socket first holds bytes after the press: early bytes and a late gump is the soak reading a backlog.
            if($since.ContainsKey($p.Index) -and $since[$p.Index].first -lt 0 -and $p.Tcp -and $p.Tcp.Available -gt 0){$since[$p.Index].first=[int]([datetime]::UtcNow-$since[$p.Index].at).TotalMilliseconds}
            Pump-Bot $p 32;$g=$p.Gump
            # UOAIX-230: a move owes the other seat a gump of the game within 10 s; a relayed gump once outgrew the event ring. The
            # server tells it in about 54 ms of wall time (trial-46810 CARDS TOLD median), and a relay over 3 s was the soak decoding a
            # backlog of 36 packets, so 3 to 10 s is counted slow-relay, not a miss.
            # A seat a GM command disconnected (kick, unstuck, jail, ban: Gm-At marks it Kicked) has nowhere to receive a gump.
            if($owed.ContainsKey($p.Index) -and (-not $p.Up -or ($p.Kicked -and ([datetime]::UtcNow-$p.Kicked).TotalSeconds -lt 60))){$owed.Remove($p.Index);Count "kicked-norelay-$Family"}
            if($owed.ContainsKey($p.Index) -and ([datetime]::UtcNow-$owed[$p.Index]).TotalSeconds -gt 10){$late[$p.Index]=$owed[$p.Index];$owed.Remove($p.Index);Count "norelay-$Family";Note "NORELAY $Family $($p.Name) got no gump 10 s after the other seat's press ($(Since-Text $since $p)) (last: $($last.Substring(0,[Math]::Min(120,$last.Length))))"}
            if($null -eq $g -or $g.Length -lt 21 -or ((U32 $g 7) -band $Mask) -ne $Id){if(([datetime]::UtcNow-$quiet[$p.Index]).TotalSeconds -gt 5){Send-Plain $p (Serial-Packet 6 $Tables[$k]);$quiet[$p.Index]=[datetime]::UtcNow;Count "reopen-$Family"};continue}
            # A press the server refused ("It is not your turn.", a stale gump) moved nothing, so it owes no relay.
            if($Players.Count -gt 1 -and (Said-Has $p '(?i)not your turn')){$o=$Players[1-$k].Index;if($owed.ContainsKey($o)){$owed.Remove($o);Count "stale-$Family"}}
            if($owed.ContainsKey($p.Index)){$waited=([datetime]::UtcNow-$owed[$p.Index]).TotalMilliseconds;$owed.Remove($p.Index);Count "relay-$Family";if($waited -gt 3000){Count "slow-relay-$Family";Note "SLOW RELAY $Family $($p.Name) $([int]$waited) ms after the press ($(Since-Text $since $p))"}}
            # A norelay whose gump arrives later is a slow relay, not a lost one: count it and say how late.
            if($late.ContainsKey($p.Index)){Count "late-relay-$Family";Note "LATE RELAY $Family $($p.Name) $([int]([datetime]::UtcNow-$late[$p.Index]).TotalMilliseconds) ms after the press ($(Since-Text $since $p))";$late.Remove($p.Index)}
            $p.Gump=$null;$quiet[$p.Index]=[datetime]::UtcNow
            $layout=[Text.Encoding]::ASCII.GetString($g,21,[Math]::Min((U16 $g 19),$g.Length-21));$texts=Gump-Texts $g;$last="$($p.Name): $texts"
            if($texts -match '(?i)\bwins?\b|\bwon\b|drawn|sunk the last|game over|busts?\b|push'){Count "ended-$Family";Note "GAME $Family $($p.Name): $($texts.Substring(0,[Math]::Min(160,$texts.Length)))";return}
            $ids=@([regex]::Matches($layout,'\{ button \d+ \d+ \d+ \d+ 1 0 (\d+) \}')|ForEach-Object{[long]$_.Groups[1].Value}|Where-Object{$_ -ne 0})
            if($ids.Count -eq 0){Count $(if($texts -match '(?i)opponent.s turn|waiting for'){"wait-$Family"}else{"nobutton-$Family"});continue}
            $button=if($ids -contains $Choice){$Choice}else{$ids[$rng.Next($ids.Count)]}
            $a=[byte[]]::new(23);$a[0]=0xB1;Put16 $a 1 23;Put32 $a 3 (U32 $g 3);Put32 $a 7 (U32 $g 7);Put32 $a 11 $button;$p.Said.Clear();Send-Plain $p $a;Count "move-$Family"
            # Choosing the game seats nobody opposite, so the chooser's press owes the other bot nothing.
            if($Players.Count -gt 1 -and $texts -notmatch '(?i)choose a game'){$o=$Players[1-$k];$owed[$o.Index]=[datetime]::UtcNow;$since[$o.Index]=@{at=[datetime]::UtcNow;r0=$o.Received;first=-1}}}}
    Count "timeout-$Family";Note "GAMEOPEN $Family last gump $($last.Substring(0,[Math]::Min(200,$last.Length)))"
}
# Two bots play a board game by dragging pieces (0x07 lift, 0x08 drop into the board's container at a place): a piece of
# either colour toward a square its kind can reach (the server refuses a wrong colour or an illegal move); backgammon
# rolls its cup (a double-click) before each drag. A piece seen at its target counts as moved.
function Play-Board($Players,[long]$Board,[long[]]$Cups,[string]$Family,[int]$Seconds){
    $deadline=[datetime]::UtcNow.AddSeconds($Seconds)
    foreach($p in $Players){Send-Plain $p (Serial-Packet 6 $Board);Pump-Bot $p 64}
    while([datetime]::UtcNow -lt $deadline){
        Pump-Watchers $Players
        for($k=0;$k -lt $Players.Count;$k++){$p=$Players[$k];Pump-Bot $p 32
            $pieces=@($p.Items.Keys|Where-Object{$p.Items[$_][1] -eq $Board -and $p.Items[$_].Count -ge 5})
            if($pieces.Count -eq 0){Send-Plain $p (Serial-Packet 6 $Board);Pump-Bot $p 64;Count "nopieces-$Family";continue}
            $s=$pieces[$rng.Next($pieces.Count)];$it=$p.Items[$s]
            if($Family -eq 'backgammon'){Send-Plain $p (Serial-Packet 6 $Cups[$k]);Pump-Bot $p 32;$tx=43+$rng.Next(198);$ty=if($rng.Next(2) -eq 0){5+$rng.Next(76)}else{130+$rng.Next(74)}}
            else{$cx=if($Family -eq 'chess'){42}else{45};$cy=if($Family -eq 'chess'){5}else{25};$u=[Math]::Round(($it[3]-$cx)/25);$v=[Math]::Round(($it[4]-$cy)/25)
                $steps=if($Family -eq 'checkers'){@(@(1,1),@(-1,1),@(1,-1),@(-1,-1),@(2,2),@(-2,2),@(2,-2),@(-2,-2))}else{@(@(0,1),@(0,-1),@(0,2),@(0,-2),@(1,2),@(2,1),@(-1,2),@(-2,1),@(1,-2),@(2,-1),@(-1,-2),@(-2,-1),@(1,1),@(-1,1),@(1,-1),@(-1,-1),@(1,0),@(-1,0))}
                $st=$steps[$rng.Next($steps.Count)];$nu=$u+$st[0];$nv=$v+$st[1];if($nu -lt 0 -or $nu -gt 7 -or $nv -lt 0 -or $nv -gt 7){continue};$tx=$cx+25*$nu;$ty=$cy+25*$nv}
            $l=[byte[]]::new(7);$l[0]=7;Put32 $l 1 $s;Put16 $l 5 1;Send-Plain $p $l
            $d=[byte[]]::new(14);$d[0]=8;Put32 $d 1 $s;Put16 $d 5 $tx;Put16 $d 7 $ty;Put32 $d 10 $Board;Send-Plain $p $d;Count "drag-$Family";Pump-Bot $p 32
            if($p.Items.ContainsKey($s) -and $p.Items[$s][1] -eq $Board -and [Math]::Abs($p.Items[$s][3]-$tx) -le 12 -and [Math]::Abs($p.Items[$s][4]-$ty) -le 12 -and ($p.Items[$s][3] -ne $it[3] -or $p.Items[$s][4] -ne $it[4])){Count "moved-$Family"}}}
    Count "timeout-$Family"
}
# British runs a tavern game: bots 2 and 3 play (Blackjack: bot 2 alone), British watches by double-clicking the same
# table once the seats are taken (a watcher's double-click must not sit). Each card game has its own Blue Boar or
# Britain table, so a table still held by an unfinished game does not block the next kind.
function Tavern-Game($B,[string]$Family){
    $v=@($fleet|Where-Object{$_.Index -ne 1 -and $_.Up -and -not $_.Dead -and $_.Serial});if($v.Count -lt 2){Count "noplayers-$Family";return}
    $two=@($v[0],$v[1]);$cards=@{eights=@(1492,1688,101);gofish=@(1497,1681,102);poker=@(1497,1696,103);blackjack=@(1435,1715,104)}
    if($cards.ContainsKey($Family)){$c=$cards[$Family];foreach($p in @($two)+@($B)){Send-Plain $p (Say "[go $($c[0]-1) $($c[1])");Pump-Bot $p 64}
        $deck=Table-At $two[0] @(0x0FA2,0x0FA3) $c[0] $c[1];if(-not $deck){Count "notable-$Family";return}
        $players=if($Family -eq 'blackjack'){@($two[0])}else{$two};foreach($p in $players){$p.Gump=$null}
        Send-Plain $players[0] (Serial-Packet 6 $deck);Pump-Bot $players[0] 32
        if($players.Count -gt 1){Send-Plain $players[1] (Serial-Packet 6 $deck)}
        Send-Plain $B (Serial-Packet 6 $deck);Count "watch-$Family"
        Play-Gumps $players @($deck,$deck) 0xFFFFFFFF 0x43415244 $Family 180 $c[2];return}
    if($Family -eq 'battleship'){foreach($p in @($two)+@($B)){Send-Plain $p (Say '[go 1416 1751');Pump-Bot $p 64}
        $navy=Table-At $two[0] @(0x14F1) 1415 1750;$pirate=Table-At $two[0] @(0x14F1) 1415 1752;if(-not $navy -or -not $pirate -or $navy -eq $pirate){Count 'notable-battleship';return}
        # The first two double-clicks on either plan take the seats (ShipwrightBattleship), so British watches only after the
        # game: a watch sent beside the players' clicks can take a seat a late bot click left open, and British never shoots.
        foreach($p in $two){$p.Gump=$null};Send-Plain $two[0] (Serial-Packet 6 $navy);Pump-Bot $two[0] 32;Send-Plain $two[1] (Serial-Packet 6 $pirate);Pump-Bot $two[1] 32
        Play-Gumps $two @($navy,$pirate) 0xFFFF0000 0x42530000 'battleship' 120 (-1)
        Send-Plain $B (Serial-Packet 6 $navy);Count 'watch-battleship';return}
    $spot=@{chess=@(1497,1693,0x0FA6);checkers=@(1435,1720,0x0FA6);backgammon=@(1497,1687,0x0E1C)}[$Family]
    foreach($p in @($two)+@($B)){Send-Plain $p (Say "[go $($spot[0]-1) $($spot[1])");Pump-Bot $p 64}
    $until=[datetime]::UtcNow.AddSeconds(4);$board=0;$seen=0
    while([datetime]::UtcNow -lt $until){Pump-Bot $two[0] 64;$board=Table-At $two[0] @($spot[2],0x0FAD) $spot[0] $spot[1];$seen=@($two[0].Ground.Keys|Where-Object{$two[0].Ground[$_][2] -eq 0x0FA7 -and [Math]::Max([Math]::Abs($two[0].Ground[$_][0]-$spot[0]),[Math]::Abs($two[0].Ground[$_][1]-$spot[1])) -le 3}).Count;if($board -and ($Family -ne 'backgammon' -or $seen -ge 2)){break}}
    if(-not $board){Count "notable-$Family";return}
    $cups=@();if($Family -eq 'backgammon'){$cups=@($two[0].Ground.Keys|Where-Object{$two[0].Ground[$_][2] -eq 0x0FA7 -and [Math]::Max([Math]::Abs($two[0].Ground[$_][0]-$spot[0]),[Math]::Abs($two[0].Ground[$_][1]-$spot[1])) -le 3}|Sort-Object{$two[0].Ground[$_][1]});if($cups.Count -lt 2){Count 'nocups-backgammon';return}}
    Play-Board $two $board $cups $Family 75
    Send-Plain $B (Serial-Packet 6 $board);Count "watch-$Family"
}
# UOAIX-275 (-Only coroner -Bots 4; British runs it once all four bots are up): bot c, set to 1 hit point and struck by
# British beside a miner working the Britain mine, is an innocent's death (a GM [kill refuses a player, crm-kill); the
# miner runs to the mayor, the mayor walks to the corpse and offers bot b the burial quest, and b accepts. Bot d killed
# the same way brings the mayor again, and b's second accept is refused (once per account); British stands off 12 tiles
# after each kill, so the only living player in reach is b. b then stands in the graveyard while British walks both its
# regions killing every creature he sees, and the coroners bury c and pay b. Each step ends in one Grade line,
# unreached when its scene never arose (L-VACUOUS). The run holds the main loop (no fatal-log or guest-exit check) for
# up to about two hours; run it alone, -Minutes 60 or more. The town purse's refusal is said at the mayor's post, out
# of b's hearing, so an unpaid burial grades 275-paid unreached, not fail.
$corMine=@(1445,1526,1457,1534)
# The mine's earth elementals killed bot b in trial-47148, so British kills every creature he sees around the mine
# (his post 12 tiles east of a kill sees it) before each kill and through each wait there.
$corPit=@(1430,1512,1470,1548)
function Cor-Pit($C){$bots=@($fleet|ForEach-Object Serial);foreach($k in @($C.Mobiles.Keys)){$m=$C.Mobiles[$k];if($bots -notcontains $k -and $m[0] -lt 400 -and (In-Box $m $corPit)){Use-Then-Target $C 0 {Target-Object $C $k $m[1] $m[2] 0 $m[0]} 'coroner-pit' -Speech '[kill'}}}
$corYards=@(@(1338,1445,1374,1509),@(1368,1444,1389,1491))
$corSweep=@(@(1356,1460),@(1356,1495),@(1378,1466))
function Cor-Heard([string]$Pattern){@($script:corHeard|Where-Object{$_ -match $Pattern}).Count -gt 0}
function Cor-Text{(@($script:corHeard)|Select-Object -Last 8) -join ' / '}
# A line heard by b grades a bot's death only once the server's INCIDENT line for that victim shows the mayor waiting
# there (stage 4), with an offer made where one is owed: another incident's corpse (a miner, a guard) can stand within b's hearing.
function Cor-Inc([long]$Victim){$m=@([regex]::Matches((Read-Log),"INCIDENT row=\d+ stage=(\d+) corpse=\d+ at \d+,\d+ victim=$Victim .*? holder=(\d+) .*? offers=(\d+)"));if($m.Count -eq 0){return $null};$g=$m[$m.Count-1].Groups;@{Stage=[int]$g[1].Value;Holder=[long]$g[2].Value;Offers=[int]$g[3].Value}}
# b waits 6 rows off a mine corpse, outside the mine region (y 1526-1534) where its creatures take no new prey and
# inside the mayor's 6-tile offer reach (trial-47739: b waiting in the region was killed by an earth elemental).
function Cor-Safe-Y([int]$Y){if($Y -ge 1529){$Y+6}else{$Y-6}}
function Cor-Await($H,[string]$Pattern,[long]$Victim,[int]$Offers,[int]$Seconds,$C){$until=[datetime]::UtcNow.AddSeconds($Seconds)
    while([datetime]::UtcNow -lt $until){if($H.Dead -or -not (Cor-Wait $H $Pattern ([int][Math]::Max(1,($until-[datetime]::UtcNow).TotalSeconds)) $C)){return $false}
        $seen=[datetime]::UtcNow.AddSeconds(180);while([datetime]::UtcNow -lt $seen -and -not $H.Dead){$i=Cor-Inc $Victim;if($i -and $i.Stage -eq 4 -and $i.Offers -ge $Offers){return $true};[void](Cor-Wait $H '(?!)' 15 $C)}
        Note "COR '$Pattern' heard, but victim $Victim's incident shows no waiting offer: another incident's";$script:corHeard.Clear()};return $false}
function Cor-Wait($Bot,[string]$Pattern,[int]$Seconds,$Clear){$until=[datetime]::UtcNow.AddSeconds($Seconds);if((Test-Path variable:script:end) -and $script:end -lt $until){$until=$script:end}
    while([datetime]::UtcNow -lt $until){Pump-All 2;if($Clear){if($script:corAtPit){Cor-Pit $Clear;$script:corHeal++;if((($script:corHeal % 2 -eq 0 -and $script:corWatch.Hits -lt 80) -or $script:corHeal % 30 -eq 0) -and $script:corWatch.Up -and -not $script:corWatch.Dead){Send-Plain $script:corWatch (Say '[attr hits 100')}}else{Cor-Clear $Clear}};if(Cor-Heard 'fetch the mayor'){$script:corFetched=$true};if(Cor-Heard $Pattern){return $true};if($Bot.Said.Count -ge 12){$Bot.Said.RemoveRange(0,$Bot.Said.Count-4)}};return $false}
function Cor-Miner($C){$bots=@($fleet|ForEach-Object Serial);foreach($k in @($C.Mobiles.Keys)){$m=$C.Mobiles[$k];if($bots -notcontains $k -and $m[0] -ge 400 -and $m[0] -le 401 -and (In-Box $m $corMine)){return $k}};return 0}
function Cor-Find-Miner($C,[int]$Seconds){Go-Wait $C 1451 1531;Pump-All 2;Cor-Pit $C;$until=[datetime]::UtcNow.AddSeconds($Seconds);$m=Cor-Miner $C;while(-not $m -and [datetime]::UtcNow -lt $until){Pump-All 2;$m=Cor-Miner $C};$m}
function Cor-Kill($C,$V,[int]$X,[int]$Y){Send-Plain $V (Say "[go $X $Y");Pump-All 2;Send-Plain $V (Say '[attr hits 1');Go-Wait $C ($X+1) $Y;Pump-All 2
    Send-Plain $C ([byte[]]@(114,1,0,50,0));Send-Plain $C (Serial-Packet 5 $V.Serial);$until=[datetime]::UtcNow.AddSeconds(20);while(-not $V.Dead -and [datetime]::UtcNow -lt $until){Pump-All 1}
    Send-Plain $C ([byte[]]@(114,0,0,50,0));Go-Wait $C ($X+12) $Y}
function Cor-Clear($C){if(-not (Test-Path variable:script:corTick)){$script:corTick=0};$script:corTick++
    if($script:corTick % 10 -eq 0){$p=$corSweep[[int][Math]::Floor($script:corTick/10) % $corSweep.Count];Go-Wait $C $p[0] $p[1]}
    $bots=@($fleet|ForEach-Object Serial);foreach($k in @($C.Mobiles.Keys)){$m=$C.Mobiles[$k];if($bots -notcontains $k -and $m[0] -lt 400 -and ((In-Box $m $corYards[0]) -or (In-Box $m $corYards[1]))){Use-Then-Target $C 0 {Target-Object $C $k $m[1] $m[2] 0 $m[0]} 'coroner-yard' -Speech '[kill'}}}
# Once graded, b and British leave the graveyard for the login spot in town: two live players idle among its spawn keep
# every creature there awake for the rest of the run (trial-47689, kind-16 wakes 2.8M), which is not the family's load.
function Cor-Park($C){if(-not ((Test-Path variable:script:corDone) -and $script:corDone)){return};$H=@($fleet|Where-Object{$_.Index -eq 2})|Select-Object -First 1;$script:corAtPit=$false;if($H -and $H.Up){Send-Plain $H (Say '[go 1420 1698')};Go-Wait $C 1422 1698}
function Cor-Run($C){
    $H=@($fleet|Where-Object{$_.Index -eq 2})|Select-Object -First 1;$V1=@($fleet|Where-Object{$_.Index -eq 3})|Select-Object -First 1;$V2=@($fleet|Where-Object{$_.Index -eq 4})|Select-Object -First 1
    if(-not $H -or -not $V1 -or -not $V2){$script:corDone=$true;Grade '275-offer' 'unreached' 'bots b, c and d are needed (-Bots 4)';return}
    if(-not ($H.Up -and $V1.Up -and $V2.Up)){Count 'coroner-waiting';return}
    $script:corDone=$true;$script:corFetched=$false;$script:corAtPit=$true;$script:corWatch=$H;$script:corHeard=[Collections.Generic.List[string]]::new();$script:corHeal=0;Send-Plain $H (Say '[attr hits 100')
    $hour=Red-To-Hour $C $true 10;$m=@(Cor-Find-Miner $C 300)|Select-Object -Last 1
    if(-not $m){Grade '275-offer' 'unreached' "no miner in the mine (1445-1457, 1526-1534) within 300 s at game hour $hour";return}
    $mm=$C.Mobiles[$m];if(-not $mm){Grade '275-offer' 'unreached' "miner $m left British's view before c's kill";return};Send-Plain $H (Say "[go $($mm[1]+2) $(Cor-Safe-Y $mm[2])");$H.Said.Clear();$script:corHeard.Clear()
    $cv=$V1.Serial;Cor-Kill $C $V1 ($mm[1]+2) $mm[2];if(-not $V1.Dead){Grade '275-offer' 'unreached' "bot c at 1 hit point was not killed by British within 20 s";return}
    $offered=Cor-Await $H 'Wilt thou clear the graveyard' $cv 1 1800 $C;if(-not $offered -and $H.Dead){Grade '275-offer' 'unreached' "bot b was killed before any offer (said: $(Cor-Text))";return}
    if(-not $offered){Grade '275-offer' $(if($script:corFetched){'fail'}else{'unreached'}) "no offer to bot b within 1800 s of c's death beside miner $m; a witness said 'fetch the mayor'=$($script:corFetched) (said: $(Cor-Text))";return}
    Grade '275-offer' 'pass' "the witness ran, the mayor came to c's corpse and offered the quest (said: $(Cor-Text))"
    $H.Said.Clear();$script:corHeard.Clear();Send-Plain $H (Say 'accept stone');$took=Cor-Wait $H 'Bless thee' 10 $null
    Grade '275-accept' $(if($took){'pass'}else{'fail'}) "b said 'accept stone' (said: $(Cor-Text))";if(-not $took){return}
    $m2=@(Cor-Find-Miner $C 300)|Select-Object -Last 1
    if(-not $m2){Grade '275-refuse' 'unreached' 'no miner in the mine for the second death within 300 s'}
    elseif(-not $C.Mobiles[$m2]){Grade '275-refuse' 'unreached' "miner $m2 left British's view before d's kill"}
    else{$mm=$C.Mobiles[$m2];$x=$mm[1]-2;$y=$mm[2]+2;Send-Plain $H (Say "[go $x $(Cor-Safe-Y $y)");$H.Said.Clear();$script:corHeard.Clear();$dv=$V2.Serial;Cor-Kill $C $V2 $x $y;$died=$V2.Dead
        $came=$died -and (Cor-Await $H 'A good soul fallen' $dv 0 1800 $C);$second=Cor-Heard 'Wilt thou clear the graveyard'
        if(-not $came){Grade '275-refuse' 'unreached' "d killed=$died; the mayor did not reach d's corpse within 1800 s (said: $(Cor-Text))"}
        else{$H.Said.Clear();$script:corHeard.Clear();Send-Plain $H (Say 'accept');$refused=Cor-Wait $H 'already laid one of ours' 10 $null
            Grade '275-refuse' $(if($refused -and -not $second){'pass'}else{'fail'}) "offered again=$second, refused=$refused (said: $(Cor-Text))"}}
    $script:corAtPit=$false;Send-Plain $H (Say '[go 1350 1495');Go-Wait $C 1356 1492;$H.Said.Clear();$script:corHeard.Clear()
    $buried=Cor-Wait $H 'Rest now' 2400 $C
    Grade '275-burial' $(if($buried){'pass'}else{'unreached'}) "b in the graveyard heard the coroners lay c to rest=$buried (said: $(Cor-Text))"
    if(-not $buried){return};$paid=Cor-Wait $H 'hath set a deed' 60 $C
    Grade '275-paid' $(if($paid){'pass'}else{'unreached'}) "b was told of a deed in the pack=$paid; unpaid means the town purse could not buy one (said at the mayor's post) (said: $(Cor-Text))"
}
function Creature($B){return Nearest $B {param($m) $m[0] -lt 400}}
# Bodies the Britain forest spawns (regions 5649, 5651, 5653, 5783) only on profiles with a taming value; no taming-0 profile shares one.
function Tameable($B){return Nearest $B {param($m) @(5,6,209,211,212,225,234,237,290) -contains $m[0]}}
# Wields an item (0x07 lift, 0x13 equip); whatever the bot held in hand goes back to its pack first (0x07 lift, 0x08 drop),
# so a bow is never refused beside a dagger.
function Equip($B,[long]$Item,[int]$Layer){
    foreach($k in @($B.Held.Keys)){$h=$B.Held[$k];if($h -ne $Item -and $B.Pack){$l=[byte[]]::new(7);$l[0]=7;Put32 $l 1 $h;Put16 $l 5 1;Send-Plain $B $l
        $d=[byte[]]::new(14);$d[0]=8;Put32 $d 1 $h;Put16 $d 5 60;Put16 $d 7 80;Put32 $d 10 $B.Pack;Send-Plain $B $d};$B.Held.Remove($k)}
    $l=[byte[]]::new(7);$l[0]=7;Put32 $l 1 $Item;Put16 $l 5 1;Send-Plain $B $l
    $e=[byte[]]::new(10);$e[0]=0x13;Put32 $e 1 $Item;$e[5]=$Layer;Put32 $e 6 $B.Serial;Send-Plain $B $e;$B.Held[$Layer]=$Item;Pump-Bot $B 32
}
# Opens the pack and the kit's bags in it (0x0E76), so the bot sees what it carries.
function Open-Pack($B){
    if(-not $B.Pack){Send-Plain $B (Serial-Packet 6 ($B.Serial-bor 0x80000000));return}
    Send-Plain $B (Serial-Packet 6 $B.Pack);Pump-Bot $B 64
    foreach($k in @($B.Items.Keys)){if($B.Items[$k][0] -eq 0x0E76 -and $B.Items[$k][1] -eq $B.Pack){Send-Plain $B (Serial-Packet 6 $k)}};Pump-Bot $B 64
}
function Act($B,[string]$Force=''){
    if($B.DeathAsked){$B.DeathAsked=$false;Send-Plain $B ([byte[]]@(44,1));$tally['death']++;$B.Last='death answer';return}
    if(-not $B.Opened -and $B.Pack){$B.Opened=$true;$B.Last='open pack';Open-Pack $B;Count 'open-pack';return}
    # UOAIX-82: British drives the timer-delayed save-breaker triggers on a third of its actions (a world-spawn kill,
    # a spell death on another bot's account, an orchard pick), so a short scaled soak reaches all three; taming and the
    # bard skills ride the same draw, because only British can summon a lute.
    $special=@(@('slay','fallen','dungeons','smite','pick','pet','bard','bard','venom','petname','eights','gofish','poker','blackjack','battleship','chess','checkers','backgammon')|Where-Object{$families -contains $_})
    $f=if($Force){$Force}elseif($B.Index -eq 1 -and $special.Count -gt 0 -and $rng.Next(3) -eq 0){$special[$rng.Next($special.Count)]}else{$families[$rng.Next($families.Count)]};$B.Last=$f;$tally[$f]++;if($f -eq 'load'){$f=$loadMix[$rng.Next($loadMix.Count)];$B.Last=$f;$tally[$f]++}
    if($B.Dead -and $f -ne 'walk' -and $f -ne 'say'){$tally['ghost']++}
    switch($f){
        # UOAIX-270 (-Only widen): British adds plain art 0x0A92 (no catalog item, so no lot) to his pack 8 a draw,
        # reading each answer before the next (a 24-command burst failed his link, widen-45917), growing the world until
        # the checkpoint bound (STORE ROOM bound=) passes three quarters of its room. The first refusal stops the family
        # for the run (L-FUTILITY); the other bots walk.
        'widen'{if($B.Index -ne 1 -or ($tally.ContainsKey('widen-added') -and $tally['widen-added'] -ge 6000)){Act $B 'walk';return};if($script:widenStop){Count 'stopped-widen';return}
            $B.Said.Clear();for($i=0;$i -lt 8;$i++){Send-Plain $B (Say '[add 0A92 1');Pump-Bot $B 8}
            $added=@($B.Said|Where-Object{$_ -match 'Added 1'}).Count;$tally['widen-added']=$(if($tally.ContainsKey('widen-added')){$tally['widen-added']}else{0})+$added
            if(Said-Has $B 'Cannot add|Usage|No backpack'){$script:widenStop=$true;Count 'refused-widen';Note "WIDEN stopped: $(Said-Text $B)"}}
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
        # The testing kit's bank holds gold and material stacks: a bot withdraws one (0x07 lift, 0x08 drop into the pack),
        # gold half the time, so it has coin to buy with and goods to sell.
        'bank'{Send-Plain $B (Say '[go 1438 1695');Pump-Bot $B 64;Send-Plain $B (Say 'bank')
            $deadline=[datetime]::UtcNow.AddSeconds(2);$stacks=@();while($stacks.Count -eq 0 -and [datetime]::UtcNow -lt $deadline){Pump-Bot $B 1;if($B.Bank){$stacks=@($B.Items.Keys|Where-Object{$B.Items[$_][1] -eq $B.Bank -and $B.Items[$_][2] -gt 0})}}
            if($stacks.Count -eq 0 -or -not $B.Pack){Count 'nobank-bank';return}
            $gold=@($stacks|Where-Object{$B.Items[$_][0] -eq 0x0EED});$s=if($gold.Count -gt 0 -and $rng.Next(2) -eq 0){$gold[0]}else{$stacks[$rng.Next($stacks.Count)]}
            $amount=[Math]::Max(1,[Math]::Min($B.Items[$s][2],$(if($B.Items[$s][0] -eq 0x0EED){200}else{20})))
            $l=[byte[]]::new(7);$l[0]=7;Put32 $l 1 $s;Put16 $l 5 $amount;Send-Plain $B $l
            # Half the time the units stack onto a pack stack of the same art, which merges a lot into a lot.
            $onto=@($B.Items.Keys|Where-Object{$_ -ne $s -and $B.Items[$_][0] -eq $B.Items[$s][0] -and $B.Items[$_][1] -eq $B.Pack})
            $dest=if($onto.Count -gt 0 -and $rng.Next(2) -eq 0){$onto[0]}else{$B.Pack}
            $d=[byte[]]::new(14);$d[0]=8;Put32 $d 1 $s;Put16 $d 5 60;Put16 $d 7 80;Put32 $d 10 $dest;Send-Plain $B $d;Count $(if($dest -eq $B.Pack){'withdraw-bank'}else{'withdraw-stack-bank'})}
        # After looking, a bot stacks one whole pack stack onto another of the same art.
        'inventory'{Open-Pack $B
            $pairs=@($B.Items.Keys|Where-Object{$B.Items[$_][1] -eq $B.Pack -and $B.Items[$_][2] -gt 0}|Group-Object{$B.Items[$_][0]}|Where-Object Count -ge 2)
            if($pairs.Count -gt 0){$g=$pairs[$rng.Next($pairs.Count)].Group;$s=$g[0];$t=$g[1]
                $l=[byte[]]::new(7);$l[0]=7;Put32 $l 1 $s;Put16 $l 5 $B.Items[$s][2];Send-Plain $B $l
                $d=[byte[]]::new(14);$d[0]=8;Put32 $d 1 $s;Put16 $d 5 60;Put16 $d 7 80;Put32 $d 10 $t;Send-Plain $B $d;Count 'merge-inventory'}}
        'harvest'{Send-Plain $B (Say '[go 1452 1529');Pump-Bot $B 64;Use-Then-Target $B (Item-Of $B 0x0E86) {Target-Ground $B 1451 1528 40} 'harvest'}
        # Every craft tool, the carpentry tools by kind (UOAIX-174: saws, planes, hammer, drawknife, froe, inshave), each
        # walked to a stand within 2 tiles of its station (WorkStations: anvil and forge, loom, saw bench, tinker bench,
        # oven); British carries every tool and material ([add), the others what they bought.
        'craft'{$kinds=@(@(0x13E3,7),@(0x0F9D,34),@(0x1034,11),@(0x1028,11),@(0x102C,11),@(0x1030,11),@(0x102A,11),@(0x10E4,11),@(0x10E5,11),@(0x10E6,11),@(0x1EB8,37),@(0x097F,13),@(0x1043,13),@(0x1022,8))
            $kind=$kinds[$rng.Next($kinds.Count)];$hex='{0:X4}' -f $kind[0]
            $h=if($B.Index -eq 1){Ensure-Item $B $hex $kind[0] 1 'craft'}else{Item-Of $B $kind[0]}
            if($B.Index -eq 1){foreach($m in @(@('1BF2',0x1BF2),@('1BD7',0x1BD7),@('1766',0x1766),@('097A',0x097A),@('1BDD',0x1BDD))){if(-not (Item-Of $B $m[1])){[void](Ensure-Item $B $m[0] $m[1] 20 'craft')}}}
            # Cooking opens its menu only after the tool targets heat (cpr-cook-menu): Good Eats' oven, 0x092C at 1448,1615,20 (WorkStations).
            $cook=$kind[0] -eq 0x097F -or $kind[0] -eq 0x1043
            $shop=if($kind[0] -eq 0x13E3){'1423 1557'}elseif($kind[0] -eq 0x0F9D){'1472 1686'}elseif($kind[0] -eq 0x1EB8){'1425 1651'}elseif($cook){'1449 1616'}else{'1430 1595'}
            if($h){Send-Plain $B (Say "[go $shop");Pump-Bot $B 64;Prime $B "$($kind[1])"
                while([datetime]::UtcNow -lt $B.CraftReady){Pump-Bot $B 1};$B.Gump=$null;$B.Menu=$null;$B.Said.Clear()
                if($cook){Use-Then-Target $B $h {Target-Object $B 0 1448 1615 20 0x092C} 'cook'}else{Send-Plain $B (Serial-Packet 6 $h)};Answer-Craft $B "craft-$hex"}
            else{Count "noitem-craft-$hex"}}
        'carve'{$c=0;foreach($k in $B.Corpses.Keys){$c=$k;break};if($c){$at=$B.Corpses[$c];Send-Plain $B (Say "[go $($at[0]) $($at[1])");Pump-Bot $B 64;Use-Then-Target $B (Item-Of $B 0x0EC4) {Target-Object $B $c $at[0] $at[1] 0 0x2006} 'carve'}}
        'combat'{$t=Nearest $B {param($m) $m[0] -lt 400 -or $m[0] -gt 403};if(-not $t){$t=Nearest $B {param($m) $true}}
            if($t){Send-Plain $B ([byte[]]@(114,1,0,50,0));Send-Plain $B (Serial-Packet 5 $t);$m=$B.Mobiles[$t]
                for($i=0;$i -lt 4;$i++){$dx=[Math]::Sign($m[1]-$B.X);$dy=[Math]::Sign($m[2]-$B.Y);if($dx -eq 0 -and $dy -eq 0){break};Walk $B ([Array]::IndexOf(@('0,-1','1,-1','1,0','1,1','0,1','-1,1','-1,0','-1,-1'),"$dx,$dy"))}}
            else{Send-Plain $B (Say '[go 1385 1487')}}
        'moongate'{Send-Plain $B (Say '[go 1337 1997');Pump-Bot $B 64;Walk $B 6;Walk $B 6}
        'magery'{if($B.Index -ne 1 -and $rng.Next(2) -eq 0){Prime $B 'magery';$n=1+$rng.Next(64);$txt=[Text.Encoding]::ASCII.GetBytes("$n");$p=[byte[]]::new(5+$txt.Length);$p[0]=0x12;Put16 $p 1 $p.Length;$p[3]=0x56;$txt.CopyTo($p,4);$before=$B.CursorAt;Send-Plain $B $p
            $deadline=[datetime]::UtcNow.AddSeconds(4);while($B.CursorAt -eq $before -and [datetime]::UtcNow -lt $deadline){Pump-Bot $B 1}
            if($B.CursorAt -ne $before){$t=Nearest $B {param($m) $true};$pick=$rng.Next(4);$i=Any-Owned $B
                if($pick -eq 0 -and $t){$m=$B.Mobiles[$t];Send-Plain $B (Target-Object $B $t $m[1] $m[2] 0 $m[0])}
                elseif($pick -eq 1){Send-Plain $B (Target-Ground $B ($B.X+2) $B.Y $B.Z);Count 'ground-magery'}
                elseif($pick -eq 2 -and $i){Send-Plain $B (Target-Object $B $i 0 0 0 $B.Items[$i][0]);Count 'item-magery'}
                else{Send-Plain $B (Target-Object $B $B.Serial $B.X $B.Y $B.Z 400)};$tally['target']++};return}
            # Otherwise British casts the next circle of a 1-64 sweep, each spell at the target class mga-aim admits (MageryData mgd-ground, mgd-self, mgd-beneficial).
            $C=$fleet[0];if(-not $C.Up -or $C.Dead -or -not $C.Serial){Count 'nocaster-magery';return}
            if(-not (Test-Path variable:script:spellNext)){$script:spellNext=0}
            $ground=@(22,24,25,26,28,33,39,46,47,48,49,50,54,55,58);$self=@(2,7,15,35,36,40,56,57,60,61,62,63,64);$box=@(13,14,19,21,23);$good=@(4,6,9,10,11,16,17,29,34,44)
            Prime $C 'magery';Send-Plain $C (Say '[attr int 100')
            for($k=0;$k -lt 8;$k++){$n=1+($script:spellNext % 64);$script:spellNext++;$aim=$null;Send-Plain $C (Say '[attr mana 100')
                if($n -eq 59){$g=@($fleet|Where-Object{$_.Index -ne 1 -and $_.Up -and $_.Dead -and $_.Serial});if($g.Count -eq 0){Count 'notarget-magery-59';continue};$o=$g[0];Send-Plain $C (Say "[go $($o.X+1) $($o.Y)");Pump-Bot $C 64;$aim={Target-Object $C $o.Serial $o.X $o.Y $o.Z 0}}
                elseif($ground -contains $n){$aim={Target-Ground $C ($C.X+2) $C.Y $C.Z}}
                elseif($box -contains $n){$g=0x0E43;$i=Item-Of $C 0x0E43;if(-not $i){$g=0x0E41;$i=Item-Of $C 0x0E41};if(-not $i){$g=0x0E43;$i=Ensure-Item $C '0E43' 0x0E43 1 'magery'};if(-not $i){continue};$aim={Target-Object $C $i 0 0 0 $g}}
                elseif($n -eq 41){$bots=@($fleet|ForEach-Object Serial);$t=0;foreach($k in $C.Mobiles.Keys){if($bots -notcontains $k){$m=$C.Mobiles[$k];if([Math]::Max([Math]::Abs($m[1]-$C.X),[Math]::Abs($m[2]-$C.Y)) -le 10){$t=$k;break}}}
                    if(-not $t){Count 'notarget-magery-41';continue};$m=$C.Mobiles[$t];$aim={Target-Object $C $t $m[1] $m[2] 0 $m[0]}}
                elseif($n -eq 32 -or $n -eq 45 -or $n -eq 52){$i=Ensure-Item $C '1F14' 0x1F14 1 'magery';if(-not $i){continue};$aim={Target-Object $C $i 0 0 0 0x1F14}}
                elseif($good -contains $n){$aim={Target-Object $C $C.Serial $C.X $C.Y $C.Z 400}}
                elseif($self -notcontains $n){$v=@($fleet|Where-Object{$_.Index -ne 1 -and $_.Up -and -not $_.Dead -and $_.Serial});if($v.Count -eq 0){Count 'notarget-magery';continue}
                    $o=$v[$rng.Next($v.Count)];Send-Plain $C (Say "[go $($o.X+1) $($o.Y)");Pump-Bot $C 64;$aim={Target-Object $C $o.Serial $o.X $o.Y $o.Z 400}}
                $txt=[Text.Encoding]::ASCII.GetBytes("$n");$p=[byte[]]::new(5+$txt.Length);$p[0]=0x12;Put16 $p 1 $p.Length;$p[3]=0x56;$txt.CopyTo($p,4);$before=$C.CursorAt;Send-Plain $C $p
                $deadline=[datetime]::UtcNow.AddSeconds($(if($aim){3}else{1}));while($C.CursorAt -eq $before -and [datetime]::UtcNow -lt $deadline){Pump-Bot $C 1}
                if(-not $aim){Count "sweep-magery-c$(1+[Math]::Floor(($n-1)/8))"}elseif($C.CursorAt -ne $before){Send-Plain $C (& $aim);$tally['target']++;Count "sweep-magery-c$(1+[Math]::Floor(($n-1)/8))"}else{Count "nocursor-magery-$n"}
                if($n -eq 56){Answer-Menu $C 'magery'};Pump-Bot $C 64}}
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
        'lore'{$k=@(1,2,3,4,16)[$rng.Next(5)];Prime $B "$k"
            Use-Then-Target $B 0 {if($k -eq 3 -or $k -eq 4){$i=Any-Owned $B;if($i){Target-Object $B $i 0 0 0 $B.Items[$i][0]}else{Target-Object $B $B.Serial $B.X $B.Y $B.Z 400}}else{$t=if($k -eq 2){Creature $B}else{Nearest $B {param($m) $m[0] -ge 400 -and $m[0] -le 401}};if($t){$m=$B.Mobiles[$t];Target-Object $B $t $m[1] $m[2] 0 $m[0]}else{Target-Object $B $B.Serial $B.X $B.Y $B.Z 400}}} "lore-$k" -Packet (Skill-Use $k)}
        'paperdoll'{Send-Plain $B (Serial-Packet 6 ($B.Serial -bor 0x80000000));$o=Nearest $B {param($m) $m[0] -ge 400 -and $m[0] -le 401};if($o){Send-Plain $B (Serial-Packet 6 ($o -bor 0x80000000))}
            $s=[byte[]]::new(10);$s[0]=0x34;Put32 $s 1 0xEDEDEDEDL;$s[5]=5;Put32 $s 6 $B.Serial;Send-Plain $B $s;Pump-Bot $B 32;Count 'paperdoll'}
        'stow'{$i=Any-Owned $B;$bags=@($B.Items.Keys|Where-Object{$B.Items[$_][0] -eq 0x0E76 -and $B.Items[$_][1] -eq $B.Pack -and $_ -ne $i})
            if($i -and $bags.Count){$bag=$bags[$rng.Next($bags.Count)];$l=[byte[]]::new(7);$l[0]=7;Put32 $l 1 $i;Put16 $l 5 1;Send-Plain $B $l;$d=[byte[]]::new(14);$d[0]=8;Put32 $d 1 $i;Put16 $d 5 40;Put16 $d 7 60;Put32 $d 10 $bag;Send-Plain $B $d;Pump-Bot $B 32;Count 'stow'}else{Count 'noitem-stow'}}
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
        'bard'{$lute=Item-Of $B 0x0EB2;if(-not $lute){$lute=Ensure-Item $B '0EB3' 0x0EB3 1 'bard'}
            if($lute){$skill=if($B.Index -eq 1){if(-not (Test-Path variable:script:bardNext)){$script:bardNext=0};@(22,9,22,15)[($script:bardNext++) % 4]}else{@(9,22,15,28)[$rng.Next(4)]};$name=@{9='peacemaking';22='provocation';15='enticement';28='snooping'}[$skill];Prime $B $name;Prime $B 'musicianship'
                if($skill -ne 28){Send-Plain $B (Say '[goto britspawn');Pump-Bot $B 64}
                $t=if($skill -eq 28){Nearest $B {param($m) $true}}else{Creature $B}
                # Every bard skill but snooping wants its first target within 10 tiles, and provocation a second creature.
                if($t -and $skill -ne 28){$m=$B.Mobiles[$t];Send-Plain $B (Say "[go $($m[1]+1) $($m[2])");Pump-Bot $B 64}
                if($t -and $B.Mobiles.ContainsKey($t)){$m=$B.Mobiles[$t];Use-Then-Target $B 0 {Target-Object $B $t $m[1] $m[2] 0 $m[0]} "bard-$name" -Packet (Skill-Use $skill)
                    if($skill -eq 22 -or $skill -eq 15){$u=0;$far=99;foreach($k in $B.Mobiles.Keys){if($k -eq $t -or $k -eq $B.Serial){continue};$o=$B.Mobiles[$k];$d=[Math]::Max([Math]::Abs($o[1]-$B.X),[Math]::Abs($o[2]-$B.Y));if($o[0] -lt 400 -and $d -lt $far){$far=$d;$u=$k}};Next-Target $B {if($skill -eq 15 -or -not $u -or -not $B.Mobiles.ContainsKey($u)){Target-Object $B $B.Serial $B.X $B.Y $B.Z 400}else{$o=$B.Mobiles[$u];Target-Object $B $u $o[1] $o[2] 0 $o[0]}} "bard-$name"}}
                else{Count 'notarget-bard'}}}
        'tracking'{Prime $B 'tracking';Send-Plain $B (Skill-Use 38);Answer-Menu $B 'tracking' 2}
        'healing'{$band=Ensure-Item $B '0E21' 0x0E21 10 'healing';if(-not $band){Count 'noitem-healing'}
            else{Prime $B 'healing';$p=$null
                # UOAIX-49 C: British bandages a ghost (cg-as-resurrect) or a poisoned bot (cg-as-cure) when the fleet has one;
                # a ghost needs Healing and Anatomy at 80, poison at 60; the bandage lands after its delay, so he waits beside the patient.
                if($B.Index -eq 1){$p=@($fleet|Where-Object{$_.Index -ne 1 -and $_.Up -and $_.Serial -and ($_.Dead -or $script:poisoned.ContainsKey($_.Serial))})|Select-Object -First 1}
                if($p){Send-Plain $B (Say '[skill anatomy 100');Send-Plain $B (Say "[go $($p.X+1) $($p.Y)");Pump-Bot $B 32;Count $(if($p.Dead){'ghost-healing'}else{'cure-healing'});$script:poisoned.Remove($p.Serial)
                    Use-Then-Target $B $band {Target-Object $B $p.Serial $p.X $p.Y $p.Z 400} 'healing';$until=[datetime]::UtcNow.AddSeconds(8);while([datetime]::UtcNow -lt $until){Pump-Bot $B 8;Pump-Bot $p 8};return}
                $t=Nearest $B {param($m) $true};Use-Then-Target $B $band {if($t -and $rng.Next(2) -eq 0){$o=$B.Mobiles[$t];Target-Object $B $t $o[1] $o[2] 0 $o[0]}else{Target-Object $B $B.Serial $B.X $B.Y $B.Z 400}} 'healing'}}
        'alchemy'{$mortar=Ensure-Item $B '0E9B' 0x0E9B 1 'alchemy';$bottle=Ensure-Item $B '0F0E' 0x0F0E 3 'alchemy'
            $reagent=@('0F85','0F84','0F7A')[$rng.Next(3)];$r=Ensure-Item $B $reagent ([Convert]::ToInt32($reagent,16)) 5 'alchemy'
            if($mortar -and $r){Prime $B 'alchemy';Use-Then-Target $B $mortar {Target-Object $B $r 0 0 0 $B.Items[$r][0]} 'alchemy'}
            $potion=@(@(0x0F0C,0x0F07,0x0F0B)|ForEach-Object{Item-Of $B $_}|Where-Object{$_})|Select-Object -First 1;if($potion){Send-Plain $B (Serial-Packet 6 $potion);Count 'drink-alchemy'}}
        'inscription'{$pen=Ensure-Item $B '0FBF' 0x0FBF 1 'inscription';$scroll=Item-Of $B 0x0EF3;if(-not $scroll){$scroll=Ensure-Item $B '0E34' 0x0E34 5 'inscription'};$book=Ensure-Item $B '0EFA' 0x0EFA 1 'inscription'
            if($pen -and $scroll){Prime $B 'inscription';Use-Then-Target $B $pen {Target-Object $B $scroll 0 0 0 $B.Items[$scroll][0]} 'inscription';Answer-Menu $B 'inscription' 2}}
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
        'cull'{if($B.Index -ne 1){return};Send-Plain $B (Say '[goto britinn');Pump-Bot $B 64;$bots=@($fleet|ForEach-Object Serial);$t=Nearest $B {param($m) $m[0] -ge 400 -and $m[0] -le 401}
            if($t -and $bots -notcontains $t){$m=$B.Mobiles[$t];Use-Then-Target $B 0 {Target-Object $B $t $m[1] $m[2] 0 $m[0]} 'cull' -Speech '[kill'}else{Count 'notarget-cull'}}
        # UOAIX-218: British stands in each dungeon in turn, at its largest WorldSpawnData region's centre (Hythloth also at
        # its two balron rooms, regions 3725 and 3735), and counts the non-human mobiles within 18 tiles after 40 game seconds:
        # gws-regions takes 16 of the 517 regions a round, a round a game second, so a region's turn comes about every 33.
        'dungeons'{if($B.Index -ne 1){return};if(-not (Test-Path variable:script:dngNext)){$script:dngNext=0}
            $stops=@(@('covetous',5471,1920),@('deceit',5146,602),@('despise',5447,575),@('destard',5267,819),@('hythloth',5927,194),@('hythloth-balron',6086,179),@('hythloth-balron',6103,39),@('shame',5440,64),@('wrong',5830,558),@('fire',5824,1352),@('ice',5774,199),@('orc',5328,1335))
            $s=$stops[($script:dngNext++) % $stops.Count];$reached=$false;foreach($o in @(@(0,0),@(-8,-8),@(8,-8),@(-8,8),@(8,8))){$tx=$s[1]+$o[0];$ty=$s[2]+$o[1];Send-Plain $B (Say "[go $tx $ty");$u=[datetime]::UtcNow.AddSeconds(4);while([Math]::Max([Math]::Abs($B.X-$tx),[Math]::Abs($B.Y-$ty)) -gt 3 -and [datetime]::UtcNow -lt $u){Pump-Bot $B 4};if([Math]::Max([Math]::Abs($B.X-$tx),[Math]::Abs($B.Y-$ty)) -le 3){$reached=$true;break}}
            if(-not $reached){Count "unreached-dng-$($s[0])";return};$until=[datetime]::UtcNow.AddSeconds([Math]::Max(3,40/$ClockScale));while([datetime]::UtcNow -lt $until){Pump-Bot $B 32}
            $n=0;foreach($k in @($B.Mobiles.Keys)){$m=$B.Mobiles[$k];if($k -eq $B.Serial -or $m[0] -eq 400 -or $m[0] -eq 401){continue};if([Math]::Max([Math]::Abs($m[1]-$B.X),[Math]::Abs($m[2]-$B.Y)) -le 18){$n++;if($m[0] -eq 10){Count 'balron'}}}
            Count $(if($n -gt 0){"seen-dng-$($s[0])"}else{"none-dng-$($s[0])"})}
        # The fighter at its hunting ground (cgf-ground), or the hunter 15 tiles off (cgh-ground), the only non-bot humans in
        # view there: the death settles at the blow (cgf-fall) and an idle hour later raises it at home (cgf-revive).
        'fallen'{if($B.Index -ne 1){return};Send-Plain $B (Say '[go 1385 1487');Pump-Bot $B 64;$bots=@($fleet|ForEach-Object Serial);$t=0;$far=99
            foreach($k in $B.Mobiles.Keys){$m=$B.Mobiles[$k];if($bots -contains $k -or $m[0] -lt 400 -or $m[0] -gt 401){continue};$d=[Math]::Max([Math]::Abs($m[1]-$B.X),[Math]::Abs($m[2]-$B.Y));if($d -lt $far){$far=$d;$t=$k}}
            if($t){$m=$B.Mobiles[$t];Use-Then-Target $B 0 {Target-Object $B $t $m[1] $m[2] 0 $m[0]} 'fallen' -Speech '[kill'}else{Count 'notarget-fallen'}}
        'pet'{if($B.Index -ne 1){return};Send-Plain $B (Say '[skill 35 100');Send-Plain $B (Say '[goto britspawn');Pump-Bot $B 64;$t=Tameable $B;$until=[datetime]::UtcNow.AddSeconds(5);while(-not $t -and [datetime]::UtcNow -lt $until){Pump-Bot $B 8;$t=Tameable $B}
            if($t){$m=$B.Mobiles[$t];Send-Plain $B (Say "[go $($m[1]+1) $($m[2])");Pump-Bot $B 32;Use-Then-Target $B 0 {Target-Object $B $t $m[1] $m[2] 0 $m[0]} 'pet' -Packet (Skill-Use 35);$until=[datetime]::UtcNow.AddSeconds(8);while([datetime]::UtcNow -lt $until){Pump-Bot $B 8};foreach($o in @('all follow me','all guard me','all stay')){Send-Plain $B (Say $o);Pump-Bot $B 8;Count "order-pet"}}else{Count 'notarget-pet'}}
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
        # UOAIX-175: British claims a horse (owned, following) and renames it by 0x75 (serial at 1, name at 5, 30 bytes),
        # then asks its status (0x34 type 4) to see the new name; any other bot renames the nearest creature and is refused.
        'petname'{$name=[Text.Encoding]::ASCII.GetBytes(@('Dobbin','Old Paint','Sir Trots')[$rng.Next(3)])
            if($B.Index -eq 1){$before=@($B.Mobiles.Keys);Send-Plain $B (Say 'Claim horse');$t=0;$until=[datetime]::UtcNow.AddSeconds(3)
                while(-not $t -and [datetime]::UtcNow -lt $until){Pump-Bot $B 8;foreach($k in @($B.Mobiles.Keys)){if($before -notcontains $k -and $B.Mobiles[$k][0] -lt 400){$t=$k;break}}}
                if(-not $t){Count 'noclaim-petname';return}}
            else{$t=Creature $B;if(-not $t){Count 'notarget-petname';return}}
            $p=[byte[]]::new(35);$p[0]=0x75;Put32 $p 1 $t;$name.CopyTo($p,5);Send-Plain $B $p;Count $(if($B.Index -eq 1){'rename-petname'}else{'stranger-petname'})
            $s=[byte[]]::new(10);$s[0]=0x34;Put32 $s 1 0xEDEDEDEDL;$s[5]=4;Put32 $s 6 $t;Send-Plain $B $s;Pump-Bot $B 32}
        # UOAIX-176: a drawn map's course (0x56: serial, command, pin number, x, y). The bot opens the map (MapDetails,
        # then the course), sets it editable (6, answered by 7), adds two pins, inserts one, moves one, removes one, now
        # and then clears, and reopens it to count the course pins it is sent.
        'mappins'{$map=Item-Of $B 0x14EB
            if(-not $map){Prime $B 'cartography';$blank=Ensure-Item $B '14EC' 0x14EC 2 'mappins';$pen=Ensure-Item $B '0FBF' 0x0FBF 1 'mappins'
                if($blank -and $pen){Send-Plain $B (Serial-Packet 6 $blank);Pump-Bot $B 64;$map=Item-Of $B 0x14EB};if(-not $map){Count 'nomap-mappins';return}}
            $pin={param($c,$n,$x,$y) $q=[byte[]]::new(11);$q[0]=0x56;Put32 $q 1 $map;$q[5]=$c;$q[6]=$n;Put16 $q 7 $x;Put16 $q 9 $y;return ,$q}
            # Opening the map ends its course with the editable flag (7); a map not editable is toggled (6) until the reply reads 1.
            $B.MapEdit=-1;Send-Plain $B (Serial-Packet 6 $map)
            $deadline=[datetime]::UtcNow.AddSeconds(2);while($B.MapEdit -lt 0 -and [datetime]::UtcNow -lt $deadline){Pump-Bot $B 1}
            if($B.MapEdit -lt 0){Count 'nocourse-mappins';return}
            for($k=0;$k -lt 2 -and $B.MapEdit -ne 1;$k++){$B.MapEdit=-1;Send-Plain $B (& $pin 6 0 0 0);$deadline=[datetime]::UtcNow.AddSeconds(2);while($B.MapEdit -lt 0 -and [datetime]::UtcNow -lt $deadline){Pump-Bot $B 1}}
            if($B.MapEdit -ne 1){Count 'noedit-mappins';return};Count 'editable-mappins'
            foreach($c in @(@(1,0,40,50),@(1,0,90,120),@(2,1,60,70),@(3,0,45,55),@(4,1,0,0))){Send-Plain $B (& $pin $c[0] $c[1] $c[2] $c[3])};Count 'plotted-mappins'
            if($rng.Next(4) -eq 0){Send-Plain $B (& $pin 5 0 0 0);Count 'cleared-mappins'}
            $B.Course=0;Send-Plain $B (Serial-Packet 6 $map);$deadline=[datetime]::UtcNow.AddSeconds(2);while([datetime]::UtcNow -lt $deadline){Pump-Bot $B 4}
            if($B.Course -gt 0){Count 'course-mappins'}}
        # UOAIX-177: a blank book (0x0FF2; the paper mill sells it, British [adds it). Opening it sends the 98-byte 0x93
        # header and every page in one 0x66; the carrier writes a title and author (0x93) and page 1 (0x66), then reopens it
        # and checks the title came back.
        'book'{$book=Ensure-Item $B '0FF2' 0x0FF2 1 'book';if(-not $book){return}
            $B.BookHead=$null;$B.BookPages=$null;Send-Plain $B (Serial-Packet 6 $book)
            $deadline=[datetime]::UtcNow.AddSeconds(2);while(($null -eq $B.BookHead -or $null -eq $B.BookPages) -and [datetime]::UtcNow -lt $deadline){Pump-Bot $B 1}
            if($null -eq $B.BookHead){Count 'noheader-book';return};Count 'opened-book';if($null -eq $B.BookPages){Count 'nopages-book'}
            $title="Soak $($B.Index)";$h=[byte[]]::new(98);$h[0]=0x93;Put32 $h 1 $book;$h[5]=1;Put16 $h 6 20;[Text.Encoding]::ASCII.GetBytes($title).CopyTo($h,8);[Text.Encoding]::ASCII.GetBytes($B.Name).CopyTo($h,68);Send-Plain $B $h
            $lines=[Text.Encoding]::ASCII.GetBytes("line one`0line two`0");$g=[byte[]]::new(13+$lines.Length);$g[0]=0x66;Put16 $g 1 $g.Length;Put32 $g 3 $book;Put16 $g 7 1;Put16 $g 9 1;Put16 $g 11 2;$lines.CopyTo($g,13);Send-Plain $B $g;Count 'wrote-book'
            $B.BookHead=$null;Send-Plain $B (Serial-Packet 6 $book);$deadline=[datetime]::UtcNow.AddSeconds(2);while($null -eq $B.BookHead -and [datetime]::UtcNow -lt $deadline){Pump-Bot $B 1}
            if($null -ne $B.BookHead -and $B.BookHead.Length -ge 98 -and [Text.Encoding]::ASCII.GetString($B.BookHead,8,$title.Length) -eq $title){Count 'readback-book'}else{Count 'noreadback-book'}}
        # UOAIX-180: a pack item dropped on the ground at the bot's feet decays an hour of game time later; its 0x1D, when
        # the bot still sees it, counts as decayed.
        'decay'{$i=@($B.Items.Keys|Where-Object{(Owned $B $_) -and $B.Items[$_][0] -ne 0x0E76 -and $B.Items[$_][0] -ne 0x0EED -and $B.Held.Values -notcontains $_})
            if($i.Count -eq 0){Count 'noitem-decay';return};$s=$i[$rng.Next($i.Count)]
            $l=[byte[]]::new(7);$l[0]=7;Put32 $l 1 $s;Put16 $l 5 1;Send-Plain $B $l
            $d=[byte[]]::new(14);$d[0]=8;Put32 $d 1 $s;Put16 $d 5 $B.X;Put16 $d 7 $B.Y;$d[9]=[byte]($B.Z -band 255);Put32 $d 10 0xFFFFFFFF;Send-Plain $B $d;$B.Dropped[$s]=1;Count 'drop-decay'}
        # UOAIX-182: British claims a snake (UOX3 POISONSTRENGTH 1); bots cannot fight British or each other (cb-select,
        # admit-hook), so another bot attacks the snake itself, and a landed blow back poisons it ("You have been poisoned!").
        # Player secure trade (UOAIX-121; CompositeGameRules cg-trade-*, never run by a soak before): British drops an owned item
        # on a bot standing beside him (0x07, 0x08 onto the bot's serial), the trade opens on both, and both accept (0x6F action 2).
        'trade'{if($B.Index -ne 1){return};$v=@($fleet|Where-Object{$_.Index -ne 1 -and $_.Up -and -not $_.Dead -and $_.Serial});if($v.Count -eq 0){Count 'notarget-trade';return}
            Open-Pack $B;$s=Any-Owned $B;if(-not $s){Count 'noitem-trade';return}
            $o=$v[$rng.Next($v.Count)];Send-Plain $o (Say "[go $($B.X+1) $($B.Y)");Pump-Bot $o 32;Pump-Bot $B 16
            $l=[byte[]]::new(7);$l[0]=7;Put32 $l 1 $s;Put16 $l 5 ([Math]::Max(1,$B.Items[$s][2]));Send-Plain $B $l
            $d=[byte[]]::new(14);$d[0]=8;Put32 $d 1 $s;Put16 $d 5 0xFFFF;Put16 $d 7 0xFFFF;Put32 $d 10 $o.Serial;Send-Plain $B $d;Count 'offer-trade'
            Pump-Bot $B 16;Pump-Bot $o 16
            foreach($x in @($B,$o)){$a=[byte[]]::new(12);$a[0]=0x6F;Put16 $a 1 12;$a[3]=2;$a[11]=1;Send-Plain $x $a};Count 'accept-trade'
            Pump-Bot $B 16;Pump-Bot $o 16}
        'venom'{if($B.Index -ne 1){return};$v=@($fleet|Where-Object{$_.Index -ne 1 -and $_.Up -and -not $_.Dead -and $_.Serial});if($v.Count -eq 0){Count 'notarget-venom';return}
            Send-Plain $B (Say '[go 1430 1700');Pump-Bot $B 64;$before=@($B.Mobiles.Keys);Send-Plain $B (Say 'Claim snake');$snake=0;$until=[datetime]::UtcNow.AddSeconds(6)
            while(-not $snake -and [datetime]::UtcNow -lt $until){Pump-Bot $B 8;foreach($k in @($B.Mobiles.Keys)){if($before -notcontains $k -and $B.Mobiles[$k][0] -eq 52){$snake=$k;break}}}
            if(-not $snake){Count 'noclaim-venom';return};Count 'claimed-venom';$m=$B.Mobiles[$snake]
            $o=$v[$rng.Next($v.Count)];Send-Plain $o (Say "[go $($m[1]+1) $($m[2])");Pump-Bot $o 32;Send-Plain $o ([byte[]]@(114,1,0,50,0));Send-Plain $o (Serial-Packet 5 $snake);Count 'attack-venom'
            $until=[datetime]::UtcNow.AddSeconds(10);while([datetime]::UtcNow -lt $until){Pump-Bot $B 8;Pump-Bot $o 8}
            Send-Plain $o ([byte[]]@(114,0,0,50,0))}
        # UOAIX-184: a spoken "buy" beside a town worker with no shopkeeper in reach opens the worker's sale (0x74); the
        # bot goes to the gatherer's home or the farm fields (the mine's elementals kill a soak bot), steps beside a
        # person who is not a bot, and says it.
        'speechbuy'{$spot=@(@(1475,1519),@(1232,1590))[$rng.Next(2)];Send-Plain $B (Say "[go $($spot[0]) $($spot[1])");Pump-Bot $B 64
            $bots=@($fleet|ForEach-Object Serial);$t=Nearest $B {param($m) $m[0] -ge 400 -and $m[0] -le 401};if(-not $t -or $bots -contains $t){Count 'notarget-speechbuy';return}
            $m=$B.Mobiles[$t];Send-Plain $B (Say "[go $($m[1]+1) $($m[2])");Pump-Bot $B 32;$B.BuyFrom=0;Send-Plain $B (Say 'buy')
            $deadline=[datetime]::UtcNow.AddSeconds(2);while($B.BuyFrom -eq 0 -and [datetime]::UtcNow -lt $deadline){Pump-Bot $B 1}
            if($B.BuyFrom){Count 'menu-speechbuy'}else{Count 'nomenu-speechbuy'}}
        # UOAIX-49 C: British authors and runs a play (CompositePlays; CompositeGameRules cg-play-*, never run by a soak before):
        # one cast member at his feet takes and gives a gold coin and a kit bandage (catalog 75) on the play's wheel, then he
        # drops a bandage on her for the given cue, says the heard word and resets. Each answer is an "ADMIN play" server line.
        'play'{if($B.Index -ne 1){return};Send-Plain $B (Say '[go 1495 1620');Pump-Bot $B 64;Open-Pack $B
            $s=Item-Of $B 0x0E21;if($s -and $B.Items[$s][1] -ne $B.Pack){$l=[byte[]]::new(7);$l[0]=7;Put32 $l 1 $s;Put16 $l 5 ([Math]::Max(1,$B.Items[$s][2]));Send-Plain $B $l
                $d=[byte[]]::new(14);$d[0]=8;Put32 $d 1 $s;Put16 $d 5 60;Put16 $d 7 80;Put32 $d 10 $B.Pack;Send-Plain $B $d;Pump-Bot $B 16}
            foreach($w in @('select 2','reset','new','mark spot','line play Soak Trade','line scene stall 1490 1615 1500 1625','line cast ann "Ann" "the trader" townsfolk female at spot',
                'line state open minor','line ann take gold 1','line ann take 75 1','line ann give 75 1','line ann give gold 1','line after 20 -> wait',
                'line state wait major','line hint 10 ann say "Hand me a bandage."','line given ann 75 1 -> done','line heard "enough" -> done','line state done end','line ann say "My thanks."')){Send-Plain $B (Say "[play $w");Pump-Bot $B 8}
            $before=@($B.Mobiles.Keys);Send-Plain $B (Say '[play start');Count 'start-play';$ann=0;$until=[datetime]::UtcNow.AddSeconds(8)
            while([datetime]::UtcNow -lt $until){Pump-Bot $B 8;if(-not $ann){foreach($k in @($B.Mobiles.Keys)){if($before -notcontains $k -and $B.Mobiles[$k][0] -ge 400 -and $B.Mobiles[$k][0] -le 401){$ann=$k;break}}}}
            $s=Item-Of $B 0x0E21;if(-not $ann){Count 'nocast-play'}elseif(-not $s){Count 'noitem-play'}
            else{$l=[byte[]]::new(7);$l[0]=7;Put32 $l 1 $s;Put16 $l 5 1;Send-Plain $B $l
                $d=[byte[]]::new(14);$d[0]=8;Put32 $d 1 $s;Put16 $d 5 0xFFFF;Put16 $d 7 0xFFFF;Put32 $d 10 $ann;Send-Plain $B $d;Count 'given-play';Pump-Bot $B 32}
            Send-Plain $B (Say 'enough');Pump-Bot $B 32;Send-Plain $B (Say '[play reset');Pump-Bot $B 16}
        # UOAIX-49 C: consensual duels (CompositeGameRules cg-duel-*, never run by a soak before). With another bot up, a bot
        # challenges aloud beside it, the other accepts, and the challenger fights until the subduing blow ends the duel.
        # Otherwise it challenges in the tavern: a resident it bought two rounds answers, with none near Silas does.
        'duel'{if($B.Index -eq 1){return};$v=@($fleet|Where-Object{$_.Index -ne 1 -and $_.Index -ne $B.Index -and $_.Up -and -not $_.Dead -and $_.Serial})
            if($v.Count -gt 0 -and $rng.Next(2) -eq 0){$o=$v[$rng.Next($v.Count)];Send-Plain $o (Say "[go $($B.X+1) $($B.Y)");Pump-Bot $o 32;Pump-Bot $B 16
                Send-Plain $B (Say 'I challenge thee to a duel');Pump-Bot $B 16;Send-Plain $o (Say 'I accept');Pump-Bot $o 16;Count 'challenge-duel'
                Send-Plain $B ([byte[]]@(114,1,0,50,0));Send-Plain $B (Serial-Packet 5 $o.Serial);$until=[datetime]::UtcNow.AddSeconds(10)
                while([datetime]::UtcNow -lt $until){Pump-Bot $B 8;Pump-Bot $o 8};Send-Plain $B ([byte[]]@(114,0,0,50,0));Count 'fought-duel'}
            else{Send-Plain $B (Say '[go 1497 1619');Pump-Bot $B 64;if($rng.Next(2) -eq 0){foreach($i in 1..2){Send-Plain $B (Say 'buy thee a drink');Pump-Bot $B 16}}
                Send-Plain $B (Say 'I challenge thee to a duel');Pump-Bot $B 32;Count 'tavern-duel';Send-Plain $B ([byte[]]@(114,0,0,50,0))}}
        # UOAIX-49 C: British eats (CompositeGameRules cg-eat, cg-eat-one; never run by a soak before): a loaf (0x103B) from
        # his pack, double-clicked; a full stomach is refused until hunger falls (1 every 5 minutes), then one is eaten.
        'eat'{if($B.Index -ne 1){return};Open-Pack $B;$s=Ensure-Item $B '103B' 0x103B 5 'eat';if($s){Send-Plain $B (Serial-Packet 6 $s);Count 'bite-eat';Pump-Bot $B 16}}
        # UOAIX-49 C: a donation to Britain (CompositeGameRules cg-gift-said, cg-town-gift, cg-gift-pay; never run by a soak
        # before): the mayor stands in the south patrol post (CivicLiveState cv-south), so the bot steps beside each person
        # there and says "donate N <metal>" (within 4 tiles of the mayor); "donate lots" is answered with how to give.
        'donate'{Send-Plain $B (Say '[go 1432 1696');Pump-Bot $B 64;$bots=@($fleet|ForEach-Object Serial)
            $folk=@($B.Mobiles.Keys|Where-Object{$bots -notcontains $_ -and $B.Mobiles[$_][0] -ge 400 -and $B.Mobiles[$_][0] -le 401 -and [Math]::Max([Math]::Abs($B.Mobiles[$_][1]-$B.X),[Math]::Abs($B.Mobiles[$_][2]-$B.Y)) -le 12}|Select-Object -First 3)
            if($folk.Count -eq 0){Count 'notarget-donate';return}
            foreach($t in $folk){$m=$B.Mobiles[$t];Send-Plain $B (Say "[go $($m[1]+1) $($m[2])");Pump-Bot $B 32;Send-Plain $B (Say @('donate 5 gold','donate 20 copper','donate 3 silver','donate lots')[$rng.Next(4)]);Count 'said-donate';Pump-Bot $B 16}}
        # Any other bot speaks a GM word (refused without a GM role) or asks the royal stable (His Majesty's alone).
        # Construction (houses come from deeds): a bot that has not built buys a house deed in the Saw Horse when it holds
        # none (7 gold), then places it once with its front step 4 tiles south of where it stands. A refusal that names the
        # nearest front step that will do is acted on once; any other refusal ends the bot's building (L-FUTILITY). A
        # standing house's door is used and its parts clicked once.
        'build'{if($B.Built){Count 'done-build';return}
            $B.Built=$true;$deed=Item-Of $B 0x14F0
            if(-not $deed){Go-Wait $B 1430 1596;$B.Said.Clear();Send-Plain $B (Say (@('buy a deed','buy a cottage deed','buy a stone house deed')[$rng.Next(3)]));Pump-Bot $B 32;$deed=Item-Of $B 0x14F0;if(-not $deed){Count 'nodeed-build';return};Count 'buy-build'}
            $B.Said.Clear();Use-Then-Target $B $deed {Target-Ground $B $B.X ($B.Y+4) $B.Z} 'build-place';Pump-Bot $B 32
            $near=@($B.Said|Where-Object{$_ -match 'The nearest front step that will do is (\d+), (\d+)\.'}|Select-Object -First 1)
            if($near.Count -gt 0){$null=$near[0] -match 'is (\d+), (\d+)\.';$fx=[int]$Matches[1];$fy=[int]$Matches[2];Go-Wait $B $fx ($fy+1);$deed=Item-Of $B 0x14F0
                if($deed){$B.Said.Clear();Use-Then-Target $B $deed {Target-Ground $B $fx $fy $B.Z} 'build-hint';Pump-Bot $B 32}}
            if(Said-Has $B 'stands\. Its key is in your pack'){Count 'placed-build'}else{Count 'refused-build';return}
            $parts=@($B.Sites.Keys|Where-Object{[Math]::Abs($B.Sites[$_][0]-$B.X) -le 8 -and [Math]::Abs($B.Sites[$_][1]-$B.Y) -le 8})
            foreach($p in $parts){Send-Plain $B (Serial-Packet 9 $p);Send-Plain $B (Serial-Packet 6 $p)}
            Pump-Bot $B 32;Count "parts-build-$([Math]::Min(3,$parts.Count))"}
        # UOAIX-217: a bot goes to a Trinsic keeper's region (Cities ct-shops-trinsic) and asks to buy, as 'vendor' does in Britain.
        'nujelm'{$spot=@(@(3554,1204),@(3762,1269),@(3731,1321),@(3546,1195),@(3547,1179))[$rng.Next(5)];Go-Wait $B $spot[0] $spot[1];Pump-Bot $B 32
            $near=@($B.Mobiles.Keys|Where-Object{$_ -ne $B.Serial -and $B.Mobiles[$_][0] -ge 400 -and $B.Mobiles[$_][0] -le 401 -and [Math]::Max([Math]::Abs($B.Mobiles[$_][1]-$B.X),[Math]::Abs($B.Mobiles[$_][2]-$B.Y)) -le 6});if($near.Count){Send-Plain $B (Serial-Packet 9 $near[0])}
            Send-Plain $B (Say 'vendor buy');Pump-Bot $B 32;Count "keepers-nujelm-$([Math]::Min(3,$near.Count))"}
        'bucc'{$spot=@(@(2736,2252),@(2635,2083),@(2707,2146),@(2715,2099),@(2626,2099),@(2707,2179))[$rng.Next(6)];Go-Wait $B $spot[0] $spot[1];Pump-Bot $B 32
            $near=@($B.Mobiles.Keys|Where-Object{$_ -ne $B.Serial -and $B.Mobiles[$_][0] -ge 400 -and $B.Mobiles[$_][0] -le 401 -and [Math]::Max([Math]::Abs($B.Mobiles[$_][1]-$B.X),[Math]::Abs($B.Mobiles[$_][2]-$B.Y)) -le 6});if($near.Count){Send-Plain $B (Serial-Packet 9 $near[0])}
            Send-Plain $B (Say 'vendor buy');Pump-Bot $B 32;Count "keepers-bucc-$([Math]::Min(3,$near.Count))"}
        'vesper'{$spot=@(@(2915,852),@(2777,966),@(2858,994),@(2844,866),@(2842,884),@(2866,850),@(2865,811),@(2898,787),@(2914,799),@(2995,762),@(2859,1001),@(2838,868),@(2996,842),@(3017,780))[$rng.Next(14)];Go-Wait $B $spot[0] $spot[1];Pump-Bot $B 32
            $near=@($B.Mobiles.Keys|Where-Object{$_ -ne $B.Serial -and $B.Mobiles[$_][0] -ge 400 -and $B.Mobiles[$_][0] -le 401 -and [Math]::Max([Math]::Abs($B.Mobiles[$_][1]-$B.X),[Math]::Abs($B.Mobiles[$_][2]-$B.Y)) -le 6});if($near.Count){Send-Plain $B (Serial-Packet 9 $near[0])}
            Send-Plain $B (Say 'vendor buy');Pump-Bot $B 32;Count "keepers-vesper-$([Math]::Min(3,$near.Count))"}
        'minoc'{$spot=@(@(2509,476),@(2460,452),@(2469,560),@(2572,589),@(2526,548),@(2518,524),@(2450,428),@(2530,551),@(2522,522),@(2518,342))[$rng.Next(10)];Go-Wait $B $spot[0] $spot[1];Pump-Bot $B 32
            $near=@($B.Mobiles.Keys|Where-Object{$_ -ne $B.Serial -and $B.Mobiles[$_][0] -ge 400 -and $B.Mobiles[$_][0] -le 401 -and [Math]::Max([Math]::Abs($B.Mobiles[$_][1]-$B.X),[Math]::Abs($B.Mobiles[$_][2]-$B.Y)) -le 6});if($near.Count){Send-Plain $B (Serial-Packet 9 $near[0])}
            Send-Plain $B (Say 'vendor buy');Pump-Bot $B 32;Count "keepers-minoc-$([Math]::Min(3,$near.Count))"}
        'cove'{$spot=@(@(2241,1228),@(2216,1168),@(2216,1188),@(2216,1188),@(2211,1167),@(2256,1188))[$rng.Next(6)];Go-Wait $B $spot[0] $spot[1];Pump-Bot $B 32
            $near=@($B.Mobiles.Keys|Where-Object{$_ -ne $B.Serial -and $B.Mobiles[$_][0] -ge 400 -and $B.Mobiles[$_][0] -le 401 -and [Math]::Max([Math]::Abs($B.Mobiles[$_][1]-$B.X),[Math]::Abs($B.Mobiles[$_][2]-$B.Y)) -le 6});if($near.Count){Send-Plain $B (Serial-Packet 9 $near[0])}
            Send-Plain $B (Say 'vendor buy');Pump-Bot $B 32;Count "keepers-cove-$([Math]::Min(3,$near.Count))"}
        'trinsic'{$spot=@(@(1847,2796),@(1886,2653),@(1987,2841),@(1880,2808),@(1920,2809),@(2020,2804),@(1843,2675),@(1854,2791),@(1840,2704),@(1989,2872))[$rng.Next(10)];Go-Wait $B $spot[0] $spot[1];Pump-Bot $B 32
            $near=@($B.Mobiles.Keys|Where-Object{$_ -ne $B.Serial -and $B.Mobiles[$_][0] -ge 400 -and $B.Mobiles[$_][0] -le 401 -and [Math]::Max([Math]::Abs($B.Mobiles[$_][1]-$B.X),[Math]::Abs($B.Mobiles[$_][2]-$B.Y)) -le 6});if($near.Count){Send-Plain $B (Serial-Packet 9 $near[0])}
            Send-Plain $B (Say 'vendor buy');Pump-Bot $B 32;Count "keepers-trinsic-$([Math]::Min(3,$near.Count))"}
        # UOAIX-217: Skara Brae's keepers (Cities ct-shops-skara): the bot goes to a keeper's region, clicks the nearest
        # person and asks to buy; keepers-skara-N counts the people within 6 tiles.
        'buccwork'{$spot=@(@(2734,2188),@(2717,2092),@(2635,2086))[$rng.Next(3)];Go-Wait $B $spot[0] $spot[1];Pump-Bot $B 64
            $near=@($B.Mobiles.Keys|Where-Object{$_ -ne $B.Serial -and $B.Mobiles[$_][0] -ge 400 -and $B.Mobiles[$_][0] -le 401 -and [Math]::Max([Math]::Abs($B.Mobiles[$_][1]-$B.X),[Math]::Abs($B.Mobiles[$_][2]-$B.Y)) -le 16})
            Count "workers-bucc-$([Math]::Min(3,$near.Count))"}
        'windwork'{$spot=@(@(5243,43),@(5259,43),@(5311,35))[$rng.Next(3)];Go-Wait $B $spot[0] $spot[1];Pump-Bot $B 64
            $near=@($B.Mobiles.Keys|Where-Object{$_ -ne $B.Serial -and $B.Mobiles[$_][0] -ge 400 -and $B.Mobiles[$_][0] -le 401 -and [Math]::Max([Math]::Abs($B.Mobiles[$_][1]-$B.X),[Math]::Abs($B.Mobiles[$_][2]-$B.Y)) -le 16})
            Count "workers-wind-$([Math]::Min(3,$near.Count))"}
        'yewwork'{$spot=@(@(567,1240),@(675,939),@(635,1211))[$rng.Next(3)];Go-Wait $B $spot[0] $spot[1];Pump-Bot $B 64
            $near=@($B.Mobiles.Keys|Where-Object{$_ -ne $B.Serial -and $B.Mobiles[$_][0] -ge 400 -and $B.Mobiles[$_][0] -le 401 -and [Math]::Max([Math]::Abs($B.Mobiles[$_][1]-$B.X),[Math]::Abs($B.Mobiles[$_][2]-$B.Y)) -le 16})
            Count "workers-yew-$([Math]::Min(3,$near.Count))"}
        'moonglowwork'{$spot=@(@(4451,1139),@(4443,1115),@(4402,1163))[$rng.Next(3)];Go-Wait $B $spot[0] $spot[1];Pump-Bot $B 64
            $near=@($B.Mobiles.Keys|Where-Object{$_ -ne $B.Serial -and $B.Mobiles[$_][0] -ge 400 -and $B.Mobiles[$_][0] -le 401 -and [Math]::Max([Math]::Abs($B.Mobiles[$_][1]-$B.X),[Math]::Abs($B.Mobiles[$_][2]-$B.Y)) -le 16})
            Count "workers-moonglow-$([Math]::Min(3,$near.Count))"}
        'covework'{$spot=@(@(2255,1161),@(2210,1150),@(2224,1150))[$rng.Next(3)];Go-Wait $B $spot[0] $spot[1];Pump-Bot $B 64
            $near=@($B.Mobiles.Keys|Where-Object{$_ -ne $B.Serial -and $B.Mobiles[$_][0] -ge 400 -and $B.Mobiles[$_][0] -le 401 -and [Math]::Max([Math]::Abs($B.Mobiles[$_][1]-$B.X),[Math]::Abs($B.Mobiles[$_][2]-$B.Y)) -le 16})
            Count "workers-cove-$([Math]::Min(3,$near.Count))"}
        'minocwork'{$spot=@(@(2440,368),@(2556,424),@(2469,562))[$rng.Next(3)];Go-Wait $B $spot[0] $spot[1];Pump-Bot $B 64
            $near=@($B.Mobiles.Keys|Where-Object{$_ -ne $B.Serial -and $B.Mobiles[$_][0] -ge 400 -and $B.Mobiles[$_][0] -le 401 -and [Math]::Max([Math]::Abs($B.Mobiles[$_][1]-$B.X),[Math]::Abs($B.Mobiles[$_][2]-$B.Y)) -le 16})
            Count "workers-minoc-$([Math]::Min(3,$near.Count))"}
        'vesperwork'{$spot=@(@(2896,770),@(2800,684),@(2772,620))[$rng.Next(3)];Go-Wait $B $spot[0] $spot[1];Pump-Bot $B 64
            $near=@($B.Mobiles.Keys|Where-Object{$_ -ne $B.Serial -and $B.Mobiles[$_][0] -ge 400 -and $B.Mobiles[$_][0] -le 401 -and [Math]::Max([Math]::Abs($B.Mobiles[$_][1]-$B.X),[Math]::Abs($B.Mobiles[$_][2]-$B.Y)) -le 16})
            Count "workers-vesper-$([Math]::Min(3,$near.Count))"}
        'skara'{$spot=@(@(579,2227),@(626,2193),@(650,2179),@(618,2217),@(601,2234),@(627,2164),@(667,2138),@(597,2203),@(588,2224),@(655,2236))[$rng.Next(10)];Go-Wait $B $spot[0] $spot[1];Pump-Bot $B 32
            $near=@($B.Mobiles.Keys|Where-Object{$_ -ne $B.Serial -and $B.Mobiles[$_][0] -ge 400 -and $B.Mobiles[$_][0] -le 401 -and [Math]::Max([Math]::Abs($B.Mobiles[$_][1]-$B.X),[Math]::Abs($B.Mobiles[$_][2]-$B.Y)) -le 6});if($near.Count){Send-Plain $B (Serial-Packet 9 $near[0])}
            Send-Plain $B (Say 'vendor buy');Pump-Bot $B 32;Count "keepers-skara-$([Math]::Min(3,$near.Count))"}
        # UOAIX-217: Ocllo's keepers (Cities ct-shops-ocllo), as the skara family does for Skara Brae.
        'ocllo'{$spot=@(@(3634,2570),@(3644,2601),@(3667,2587),@(3602,2572),@(3626,2605),@(3669,2618),@(3615,2473),@(3628,2537),@(3603,2610),@(3602,2602),@(3629,2572))[$rng.Next(11)];Go-Wait $B $spot[0] $spot[1];Pump-Bot $B 32
            $near=@($B.Mobiles.Keys|Where-Object{$_ -ne $B.Serial -and $B.Mobiles[$_][0] -ge 400 -and $B.Mobiles[$_][0] -le 401 -and [Math]::Max([Math]::Abs($B.Mobiles[$_][1]-$B.X),[Math]::Abs($B.Mobiles[$_][2]-$B.Y)) -le 6});if($near.Count){Send-Plain $B (Serial-Packet 9 $near[0])}
            Send-Plain $B (Say 'vendor buy');Pump-Bot $B 32;Count "keepers-ocllo-$([Math]::Min(3,$near.Count))"}
        # UOAIX-217: Magincia's keepers (Cities ct-shops-magincia), as the skara family does for Skara Brae.
        'magincia'{$spot=@(@(3686,2228),@(3732,2227),@(3682,2173),@(3754,2229),@(3668,2232),@(3714,2133),@(3708,2244),@(3668,2135),@(3682,2258))[$rng.Next(9)];Go-Wait $B $spot[0] $spot[1];Pump-Bot $B 32
            $near=@($B.Mobiles.Keys|Where-Object{$_ -ne $B.Serial -and $B.Mobiles[$_][0] -ge 400 -and $B.Mobiles[$_][0] -le 401 -and [Math]::Max([Math]::Abs($B.Mobiles[$_][1]-$B.X),[Math]::Abs($B.Mobiles[$_][2]-$B.Y)) -le 6});if($near.Count){Send-Plain $B (Serial-Packet 9 $near[0])}
            Send-Plain $B (Say 'vendor buy');Pump-Bot $B 32;Count "keepers-magincia-$([Math]::Min(3,$near.Count))"}
        'jhelom'{$spot=@(@(1443,3803),@(1420,3854),@(1363,3780),@(1355,3730),@(1412,3772),@(1371,3810),@(1404,3802),@(1435,3827),@(1427,3981),@(1438,3796))[$rng.Next(10)];Go-Wait $B $spot[0] $spot[1];Pump-Bot $B 32
            $near=@($B.Mobiles.Keys|Where-Object{$_ -ne $B.Serial -and $B.Mobiles[$_][0] -ge 400 -and $B.Mobiles[$_][0] -le 401 -and [Math]::Max([Math]::Abs($B.Mobiles[$_][1]-$B.X),[Math]::Abs($B.Mobiles[$_][2]-$B.Y)) -le 6});if($near.Count){Send-Plain $B (Serial-Packet 9 $near[0])}
            Send-Plain $B (Say 'vendor buy');Pump-Bot $B 32;Count "keepers-jhelom-$([Math]::Min(3,$near.Count))"}
        'serpents'{$spot=@(@(3005,3386),@(3003,3407),@(2875,3509),@(2974,3353),@(3001,3348),@(2963,3407),@(2932,3504),@(3011,3355),@(3051,3370))[$rng.Next(9)];Go-Wait $B $spot[0] $spot[1];Pump-Bot $B 32
            $near=@($B.Mobiles.Keys|Where-Object{$_ -ne $B.Serial -and $B.Mobiles[$_][0] -ge 400 -and $B.Mobiles[$_][0] -le 401 -and [Math]::Max([Math]::Abs($B.Mobiles[$_][1]-$B.X),[Math]::Abs($B.Mobiles[$_][2]-$B.Y)) -le 6});if($near.Count){Send-Plain $B (Serial-Packet 9 $near[0])}
            Send-Plain $B (Say 'vendor buy');Pump-Bot $B 32;Count "keepers-serpents-$([Math]::Min(3,$near.Count))"}
        'moonglow'{$spot=@(@(4411,1058),@(4387,1106),@(4439,1155),@(4458,1059),@(4387,1061),@(4388,1082),@(4401,1161),@(4480,1067),@(4470,1157),@(4410,1091),@(4411,1138),@(4414,1063))[$rng.Next(12)];Go-Wait $B $spot[0] $spot[1];Pump-Bot $B 32
            $near=@($B.Mobiles.Keys|Where-Object{$_ -ne $B.Serial -and $B.Mobiles[$_][0] -ge 400 -and $B.Mobiles[$_][0] -le 401 -and [Math]::Max([Math]::Abs($B.Mobiles[$_][1]-$B.X),[Math]::Abs($B.Mobiles[$_][2]-$B.Y)) -le 6});if($near.Count){Send-Plain $B (Serial-Packet 9 $near[0])}
            Send-Plain $B (Say 'vendor buy');Pump-Bot $B 32;Count "keepers-moonglow-$([Math]::Min(3,$near.Count))"}
        'yew'{$spot=@(@(553,986),@(551,825),@(532,970),@(644,1083),@(635,819),@(611,815),@(564,1010),@(643,851),@(627,851),@(626,848),@(562,963),@(620,1145),@(514,986),@(513,990))[$rng.Next(14)];Go-Wait $B $spot[0] $spot[1];Pump-Bot $B 32
            $near=@($B.Mobiles.Keys|Where-Object{$_ -ne $B.Serial -and $B.Mobiles[$_][0] -ge 400 -and $B.Mobiles[$_][0] -le 401 -and [Math]::Max([Math]::Abs($B.Mobiles[$_][1]-$B.X),[Math]::Abs($B.Mobiles[$_][2]-$B.Y)) -le 6});if($near.Count){Send-Plain $B (Serial-Packet 9 $near[0])}
            Send-Plain $B (Say 'vendor buy');Pump-Bot $B 32;Count "keepers-yew-$([Math]::Min(3,$near.Count))"}
        'wind'{$spot=@(@(5158,97),@(5202,90),@(5348,55),@(5262,130),@(5311,35),@(5221,176),@(5171,20),@(5214,117),@(5151,60))[$rng.Next(9)];Go-Wait $B $spot[0] $spot[1];Pump-Bot $B 32
            $near=@($B.Mobiles.Keys|Where-Object{$_ -ne $B.Serial -and $B.Mobiles[$_][0] -ge 400 -and $B.Mobiles[$_][0] -le 401 -and [Math]::Max([Math]::Abs($B.Mobiles[$_][1]-$B.X),[Math]::Abs($B.Mobiles[$_][2]-$B.Y)) -le 6});if($near.Count){Send-Plain $B (Serial-Packet 9 $near[0])}
            Send-Plain $B (Say 'vendor buy');Pump-Bot $B 32;Count "keepers-wind-$([Math]::Min(3,$near.Count))"}
        'gm'{if($B.Index -ne 1){if($rng.Next(2) -eq 0){& $gmSteps['stable'] $B;Count 'stable-gm'}else{Send-Plain $B (Say @('[kick','[decor','[zones','[jail 1','[ban')[$rng.Next(5)]);Count 'refused-gm'};return}
            if(-not (Test-Path variable:script:gmNext)){$script:gmNext=0};$name=@($gmSteps.Keys)[($script:gmNext++) % $gmSteps.Count];& $gmSteps[$name] $B;Count "gm-$name"}
        # UOAIX-218: British stands in each themed wild area (a player's pulse spawns its window) and counts the themed
        # bodies in view: lizardmen 0x21/0x23/0x24 and snakes 0x34/0x15 in the swamps, polar bears 0xD5, walruses 0xDD and
        # white wolves 0x25 on Dagger Isle, daemons 0x09/0x0A, dragons 0x0C/0x3B and drakes 0x3C/0x3D at Fire Isle Jungle 6.
        'wild'{if($B.Index -ne 1){return};if(-not (Test-Path variable:script:wildNext)){$script:wildNext=0}
            $p=@(@('swamp',2020,1020,@(0x21,0x23,0x24,0x34,0x15)),@('swamp',1180,2880,@(0x21,0x23,0x24,0x34,0x15)),@('ice',3960,400,@(0xD5,0xDD,0x25)),@('ice',4080,570,@(0xD5,0xDD,0x25)),@('fire',4290,3720,@(0x09,0x0A,0x0C,0x3B,0x3C,0x3D)))[($script:wildNext++) % 5]
            Send-Plain $B (Say "[go $($p[1]) $($p[2])");$until=[datetime]::UtcNow.AddSeconds(20);while([datetime]::UtcNow -lt $until){Pump-Bot $B 16}
            $near=@($B.Mobiles.Keys|Where-Object{$_ -ne $B.Serial -and $B.Mobiles[$_][0] -ne 400 -and $B.Mobiles[$_][0] -ne 401 -and [Math]::Max([Math]::Abs($B.Mobiles[$_][1]-$B.X),[Math]::Abs($B.Mobiles[$_][2]-$B.Y)) -le 18})
            $seen=@($near|Where-Object{$p[3] -contains $B.Mobiles[$_][0]})
            Count "visit-wild-$($p[0])";if($seen.Count -gt 0){Count "seen-wild-$($p[0])";Note "WILD $($p[0]) at $($B.X),$($B.Y): $($seen.Count) themed of $($near.Count) creatures within 18 tiles"}else{Count "none-wild-$($p[0])";Note "WILD $($p[0]) at $($B.X),$($B.Y): 0 themed of $($near.Count) creatures within 18 tiles"}}
        'spells'{if($B.Index -ne 1){return};Prime $B 'magery';Send-Plain $B (Say '[attr int 100')
            if(-not (Test-Path variable:script:spellStep)){$script:spellStep=0};$name=@($spellSteps.Keys)[($script:spellStep++) % $spellSteps.Count];& $spellSteps[$name] $B;Count "spells-$name"}
        {@('castle','cheer','loom','fell','farm') -contains $_}{if($B.Index -eq 1){& $bluSteps[$f] $B}}
        'graded'{if($B.Index -ne 1){return};Prime $B 'magery';Send-Plain $B (Say '[attr int 100')
            if(-not (Test-Path variable:script:gradedStep)){$script:gradedStep=0};$name=@($gradedSteps.Keys)[($script:gradedStep++) % $gradedSteps.Count];& $gradedSteps[$name] $B;Count "graded-$name"}
        'reekgraded'{if($B.Index -ne 1){return};if(-not (Test-Path variable:script:reekStep)){$script:reekStep=0};$name=@($reekSteps.Keys)[($script:reekStep++) % $reekSteps.Count];& $reekSteps[$name] $B;Count "reekgraded-$name"}
        'festergraded'{if($B.Index -ne 1){return};Prime $B 'magery';Send-Plain $B (Say '[attr int 100');if(-not (Test-Path variable:script:festerStep)){$script:festerStep=0};$name=@($festerSteps.Keys)[($script:festerStep++) % $festerSteps.Count];& $festerSteps[$name] $B;Count "festergraded-$name"}
        'coroner'{if($B.Index -ne 1 -or (Test-Path variable:script:corDone)){return};Cor-Run $B;Cor-Park $B;Count 'coroner-draw'}
        'redgraded'{if($B.Index -ne 1){return};if(-not (Test-Path variable:script:redStep)){$script:redStep=0};$name=@($redSteps.Keys)[($script:redStep++) % $redSteps.Count];& $redSteps[$name] $B;Count "redgraded-$name"}
        {@('eights','gofish','poker','blackjack','battleship','chess','checkers','backgammon') -contains $_}{if($B.Index -eq 1){Tavern-Game $B $f}}
        'pick'{Send-Plain $B (Say '[go 1230 1591');Pump-Bot $B 64;$c=@($B.Crops.Keys|Where-Object{$B.Crops[$_][0] -eq 1230 -and $B.Crops[$_][1] -eq 1590})
            if($c.Count -gt 0){Send-Plain $B (Serial-Packet 6 $c[0]);Count 'pick-orchard'}else{Count 'notarget-pick'}}
    }
}
$log=Join-Path $OutDir 'server.log';$err=Join-Path $OutDir 'guest.stderr';$option=Join-Path $OutDir 'launch.input';$prof=Join-Path $OutDir 'prof.txt'
# The owner drives the admin port too (UOAIX-49 B): keys as start-composite-game.ps1 -Admin provisions them (health,
# mind, keeper, human, epoch); the owner is the human role 4. Only read-only panel commands run, so the world is unchanged,
# except under the 'gm' family: every other turn then queues a panel action, which British's next pulse applies (cp-panel-apply).
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
        $lord=$fleet[0];if($families -contains 'gm' -and $admin.Turn % 2 -eq 1 -and $lord.Up -and $lord.Serial){
            $a=@('"action":"lord-british-enter"','"action":"lord-british-leave"','"action":"invisible"',"`"action`":`"inspect`",`"target`":$($lord.Serial)",'"action":"navy"',
                '"action":"summon-creature","graphic":51','"action":"teleport","x":1475,"y":1645,"z":0','"action":"summon-item","graphic":3821,"amount":5','"action":"unban","account":"soakbotb"')[($admin.Turn -shr 1) % 9];$admin.Turn++
            $text=Admin-Send ('{"command":"panel-action","session":'+$admin.Session+','+$a+'}');if($text -match '^\{"queued"'){Count 'admin-action'}else{Count 'adminerr-action'};return}
        $c=$adminCommands[$admin.Turn % $adminCommands.Count];$admin.Turn++
        $extra=if($c -eq 'gm-log'){',"before":0'}elseif($c -eq 'panel-log'){',"before":1'}else{''}
        $text=Admin-Send ('{"command":"'+$c+'","session":'+$admin.Session+$extra+'}')
        if($text -match '^\{"error"'){Count "adminerr-$c"}else{Count "admin-$c"}
    }catch{Count 'admin-refused';$admin.Session=0;$admin.Ceiling=0}
}
# TICKET: a relay ticket's 120 s (cl-ticket-default) is guest time, so a scaled clock gets 120 x ClockScale.
[IO.File]::WriteAllText($option,"$(if($ClockScale -gt 1){"TICKET $(120*$ClockScale)`n"})ADMIN $($adminKeys -join ' ')`nUOAIX TESTING$(if($Open){' OPEN'})`n",[Text.Encoding]::ASCII)
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
# UOAIX-254: each minute of a -Only load run grades the server log slice against the bots up: WORLD PULSE total-us (trace,
# sampled), TICK SLOW total (ms, from 250), ROUND SLOW total (ms), WORLD COMMITTED total-us and WATERMARK heap-used.
$script:loadRows=@{}
function Load-Minute([string]$Slice,[int]$Up){
    $pulses=@([regex]::Matches($Slice,'(?m)^WORLD PULSE .*? total-us=(\d+)')|ForEach-Object{[long]$_.Groups[1].Value})
    $ticks=@([regex]::Matches($Slice,'(?m)^TICK SLOW total=(\d+)')|ForEach-Object{[long]$_.Groups[1].Value})
    $rounds=@([regex]::Matches($Slice,'(?m)^ROUND SLOW .*? total=(\d+)')|ForEach-Object{[long]$_.Groups[1].Value})
    $commits=@([regex]::Matches($Slice,'(?m)^WORLD COMMITTED .*? total-us=(\d+)')|ForEach-Object{[long]$_.Groups[1].Value})
    $heap=@([regex]::Matches($Slice,'(?m)^WATERMARK heap-used=(\d+)MiB')|ForEach-Object{[long]$_.Groups[1].Value})
    $mean=if($pulses.Count){[long](($pulses|Measure-Object -Average).Average)}else{0};$pmax=if($pulses.Count){($pulses|Measure-Object -Maximum).Maximum}else{0}
    $tmax=if($ticks.Count){($ticks|Measure-Object -Maximum).Maximum}else{0};$rmax=if($rounds.Count){($rounds|Measure-Object -Maximum).Maximum}else{0}
    $cmax=if($commits.Count){($commits|Measure-Object -Maximum).Maximum}else{0};$hlast=if($heap.Count){$heap[-1]}else{0}
    Note "LOAD up=$Up pulses=$($pulses.Count) pulse-mean-us=$mean pulse-max-us=$pmax tick-slow=$($ticks.Count) tick-max-ms=$tmax round-slow=$($rounds.Count) round-max-ms=$rmax commit-max-us=$cmax heap-mib=$hlast"
    if(-not $script:loadRows.ContainsKey($Up)){$script:loadRows[$Up]=@(0,0,0,0,0,0,0,0)};$r=$script:loadRows[$Up]
    $r[0]++;$r[1]+=$pulses.Count;$r[2]+=($pulses|Measure-Object -Sum).Sum;$r[3]=[Math]::Max($r[3],$pmax);$r[4]+=$ticks.Count;$r[5]+=$rounds.Count;$r[6]=[Math]::Max($r[6],[Math]::Max($tmax,$rmax));$r[7]=[Math]::Max($r[7],$hlast)
}
function Load-Grade{
    foreach($up in @($script:loadRows.Keys|Sort-Object)){$r=$script:loadRows[$up];Note "LOAD-ROW up=$up minutes=$($r[0]) pulses=$($r[1]) pulse-mean-us=$(if($r[1]){[long]($r[2]/$r[1])}else{0}) pulse-max-us=$($r[3]) tick-slow=$($r[4]) round-slow=$($r[5]) slow-max-ms=$($r[6]) heap-max-mib=$($r[7])"}
    $top=if($script:loadRows.Count){[int](@($script:loadRows.Keys)|Measure-Object -Maximum).Maximum}else{0}
    if($top -lt [Math]::Ceiling($Bots*0.8)){Grade 'load' 'unreached' "at most $top of $Bots bots were up at once";return}
    $r=$script:loadRows[$top]
    Grade 'load' $(if($r[5] -eq 0 -and $r[4] -le $r[0]){'pass'}else{'fail'}) "$top bots for $($r[0]) minutes: $($r[5]) slow rounds, $($r[4]) slow ticks (pass: no slow round and at most one slow tick a minute), slowest $($r[6]) ms, heap $($r[7]) MiB"
}
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
    $elapsed=0;$end=[datetime]::UtcNow.AddMinutes($Minutes);$nextReport=[datetime]::UtcNow.AddMinutes(1);$seen=0;$nextCreate=[datetime]::MinValue
    $errandDone=($ErrandSeconds -eq 0);$errandDeadline=[datetime]::UtcNow.AddSeconds($ErrandSeconds);$errandNext=[datetime]::UtcNow
    while([datetime]::UtcNow -lt $end){
        if(-not $errandDone -and [datetime]::UtcNow -ge $errandNext){
            $errandNext=[datetime]::UtcNow.AddSeconds(2)
            if((Read-Log) -match '(?m)^BRITISH errand (leg 5 |done)'){$errandDone=$true;Note "ERRAND British's errand finished, the economy is open; British logs in"}
            elseif([datetime]::UtcNow -gt $errandDeadline){$fatal="British's errand did not finish within $ErrandSeconds s of listen";break}
        }
        foreach($b in $fleet){
            try{
                # A first login creates the account, and the server creates one a 10 s (ga-may-create): space them 11 s apart.
                if(-not $b.Up){if($b.Index -eq 1 -and -not $errandDone){continue};if($b.Logins -eq 0 -and [datetime]::UtcNow -lt $nextCreate){continue};if([datetime]::UtcNow -ge $b.Reconnect){if($b.Logins -eq 0){$nextCreate=[datetime]::UtcNow.AddSeconds(11)};Connect-Bot $b;Note "LOGIN $($b.Name) serial=$($b.Serial) at $($b.X),$($b.Y) (login $($b.Logins))"};continue}
                Pump-Bot $b 256
                if([datetime]::UtcNow -ge $b.NextAt){Act $b;$b.NextAt=[datetime]::UtcNow.AddMilliseconds(300+$rng.Next(1200))}
            # A step drives other bots too (a card table's players): a write that failed on one of their sockets drops that
            # bot, not the bot whose turn it is (trial-46700: British dropped for soakbotd's kicked socket).
            }catch{$why="$($_.Exception.Message) (soak-bots line $($_.InvocationInfo.ScriptLineNumber))";$broken=@($fleet|Where-Object{$_.Up -and $_.Tcp -and -not $_.Tcp.Client.Connected})
                if($broken.Count -gt 0 -and $broken -notcontains $b){foreach($x in $broken){Drop-Bot $x $why}}else{Drop-Bot $b $why}}
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
            if($Only -contains 'load'){Load-Minute $new $up}
            Note ("STATUS up=$up/$Bots sent=$(($fleet|Measure-Object Sent -Sum).Sum) recv=$(($fleet|Measure-Object Received -Sum).Sum) disconnects=$($disconnects.Count) gm-disconnects=$($gmDrops.Count) " +(($tally.Keys|Sort-Object|ForEach-Object{"$_=$($tally[$_])"}) -join ' '))
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
foreach($d in $gmDrops){Note "GM DISCONNECTED $d"}
if($Only -contains 'load'){Load-Grade}
$syms=[Collections.Generic.List[object]]::new()
foreach($line in [IO.File]::ReadLines($map)){if($line -match '^(0x[0-9a-fA-F]+)\s+(\d+)\s+(.+)$'){$syms.Add(@([Convert]::ToInt64($Matches[1],16),[int]$Matches[2],$Matches[3].Trim()))}}
$sorted=@($syms|Sort-Object {$_[0]});$starts=[long[]]@($sorted|ForEach-Object{$_[0]})
$hitNames=@{};$samples=0
if(Test-Path $prof){foreach($line in [IO.File]::ReadLines($prof)){if($line -match 'HPROF:([0-9a-fA-F]+)'){$samples++;$rip=[Convert]::ToInt64($Matches[1],16);$i=[Array]::BinarySearch($starts,$rip);if($i -lt 0){$i=(-bnot $i)-1};if($i -ge 0 -and $rip -lt $sorted[$i][0]+$sorted[$i][1]){$hitNames[$sorted[$i][2]]=1}}}}
Note "COVERAGE sampled functions hit $($hitNames.Count) of $($sorted.Count) in the map, from $samples host samples at about 18 Hz (a sampled floor, not a full coverage measure)"
if($CoverLog){$md=Join-Path $OutDir 'coverage.md';try{pwsh -NoProfile -File (Join-Path $repo 'build/coverage-report.ps1') -Log $CoverLog -Output $log -Markdown $md | Select-Object -First 3 | ForEach-Object{Note "COVER $_"}}catch{Note "COVER report failed: $($_.Exception.Message)"}}
if(-not $fatal){$hit=[regex]::Matches((Read-Log),$fatalPattern);if($hit.Count -gt 0){$fatal=($hit|Select-Object -First 3|ForEach-Object{$_.Value.Trim()}) -join ' | '}}
# A drop no GM command made is a failure (L-FALSIF): trial-46602's 88 timeouts printed SOAK PASS. GM kicks are $gmDrops.
if(-not $fatal -and $disconnects.Count -gt 0){$fatal="$($disconnects.Count) disconnects no GM made: "+(($disconnects|Select-Object -First 3) -join ' | ')}
if($fatal){Note "SOAK FAIL $fatal";exit 1}
Note "SOAK PASS $Minutes minutes, $Bots bots, $($disconnects.Count) disconnects, $($gmDrops.Count) gm disconnects"
