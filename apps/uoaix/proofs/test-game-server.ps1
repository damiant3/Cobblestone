[CmdletBinding()]
param(
    [int]$Port=2594,
    [Parameter(Mandatory)][string]$CompressionSource,
    [Parameter(Mandatory)][string]$Evidence,
    [Parameter(Mandatory)][string]$ServerLog,
    [switch]$Hooks,
    [ValidateRange(0,120)][int]$IdleLoginSeconds=0
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$Evidence=[IO.Path]::GetFullPath($Evidence)
New-Item -ItemType Directory -Force $Evidence|Out-Null
if((Get-FileHash $CompressionSource).Hash -ne '807165537C00C83F295DC98F229F53D31460F055D1CC7FCBA9616E5966523042'){throw 'Wrong Huffman oracle source'}
$source=[IO.File]::ReadAllText((Resolve-Path $CompressionSource))
$table=[regex]::Match($source,'(?s)_huffmanTable = new int\[514\]\s*\{(.*?)\};').Groups[1].Value
$values=@([regex]::Matches($table,'0x[0-9A-Fa-f]+')|ForEach-Object{[Convert]::ToInt32($_.Value.Substring(2),16)})
if($values.Count -ne 514){throw 'Wrong Huffman table extent'}
$codes=@{}
for($i=0;$i -le 256;$i++){$codes["$($values[$i*2]):$($values[$i*2+1])"]=$i}
$script:checks=0
$script:idleDelayUsed=$false
$script:packets=[Collections.Generic.List[object]]::new()
$script:refusals=[Collections.Generic.List[object]]::new()
function Check([string]$Name,[bool]$Condition){if(-not $Condition){throw "FAIL $Name"};$script:checks++;"PASS $Name"}
function Put16([byte[]]$Bytes,[int]$At,[long]$Value){$Bytes[$At]=($Value-shr 8)-band 255;$Bytes[$At+1]=$Value-band 255}
function Put32([byte[]]$Bytes,[int]$At,[long]$Value){Put16 $Bytes $At ($Value-shr 16);Put16 $Bytes ($At+2) $Value}
function U16([byte[]]$Bytes,[int]$At){[int]$Bytes[$At]*256+$Bytes[$At+1]}
function U32([byte[]]$Bytes,[int]$At){[long](U16 $Bytes $At)*65536+(U16 $Bytes ($At+2))}
function Read-Exact($Stream,[int]$Count){
    $bytes=[byte[]]::new($Count);$at=0
    while($at -lt $Count){$n=$Stream.Read($bytes,$at,$Count-$at);if($n -eq 0){throw 'premature socket EOF'};$at+=$n}
    return ,$bytes
}
function Read-Packet($Stream){
    $plain=[Collections.Generic.List[byte]]::new();$bits=0;$value=0
    for($read=0;$read -lt 6000;$read++){
        $byte=$Stream.ReadByte();if($byte -lt 0){throw 'EOF inside Huffman packet'}
        for($bit=7;$bit -ge 0;$bit--){
            $value=($value-shl 1)-bor (($byte-shr $bit)-band 1);$bits++
            $key="${bits}:$value"
            if($codes.ContainsKey($key)){
                $symbol=$codes[$key]
                if($symbol -eq 256){
                    if(($byte-band ((1-shl $bit)-1)) -ne 0){throw 'Nonzero Huffman padding'}
                    $result=$plain.ToArray();$script:packets.Add(@{kind='synthetic replay response';hex=[Convert]::ToHexString($result)})
                    return ,$result
                }
                $plain.Add([byte]$symbol);if($plain.Count -gt 4096){throw 'Huffman plaintext budget'}
                $bits=0;$value=0
            }elseif($bits -gt 11){throw 'Invalid Huffman prefix'}
        }
    }
    throw 'Huffman encoded budget'
}
function Login-Xor([byte[]]$Bytes,[uint64]$seed=0x01020304){
    [uint64]$mask=4294967295
    [uint64]$low=(((($seed-bxor $mask)-bxor 0x1357)-shl 16)-bor (($seed-bxor 0xFFFFAAAAUL)-band 65535))-band $mask
    [uint64]$high=(($seed-bxor 0x43210000)-shr 16)-bor ((($seed-bxor $mask)-bxor 0xABCDFFFFUL)-band 4294901760)
    $result=[byte[]]::new($Bytes.Length)
    for($i=0;$i -lt $Bytes.Length;$i++){
        $result[$i]=$Bytes[$i]-bxor ($low-band 255)
        $next=((($low-shr 1)-bor ($high-shl 31))-bxor 0x026950C6)-band $mask
        $high=((($high-shr 1)-bor ($low-shl 31))-bxor 0x389DE58C)-band $mask;$low=$next
    }
    return ,$result
}
function Open-Relay {
    $plain=[byte[]]::new(214);$plain[0]=128;$plain[61]=100;$plain[62]=164;$plain[211]=160
    $name=[Text.Encoding]::ASCII.GetBytes('uoaixtest');$name.CopyTo($plain,1)
    for($i=0;$i -lt $name.Length;$i++){$plain[31+$i]=$name[$i]-13}
    $encrypted=Login-Xor $plain
    $tcp=[Net.Sockets.TcpClient]::new('127.0.0.1',$Port)
    try{
        $stream=$tcp.GetStream();$stream.ReadTimeout=15000
        $stream.Write([byte[]]@(1,2,3,4),0,4);$stream.Write($encrypted,0,62)
        $list=Read-Exact $stream 46
        Check 'shard list' ($list[0] -eq 168 -and (U16 $list 1) -eq 46 -and $list[5] -eq 1)
        $stream.Write($encrypted,62,149)
        if($IdleLoginSeconds -gt 0 -and -not $script:idleDelayUsed){
            $script:idleDelayUsed=$true
            "Waiting $IdleLoginSeconds seconds after hardware info before shard selection"
            Start-Sleep -Seconds $IdleLoginSeconds
        }
        $stream.Write($encrypted,211,3)
        $relay=Read-Exact $stream 11
        Check 'relay endpoint' ($relay[0] -eq 140 -and (U16 $relay 5) -eq $Port)
        return (U32 $relay 7)
    }finally{$tcp.Dispose()}
}
function New-GameLogin([long]$Key){
    $p=[byte[]]::new(65);$p[0]=145;Put32 $p 1 $Key
    $name=[Text.Encoding]::ASCII.GetBytes('uoaixtest');$name.CopyTo($p,5)
    for($i=0;$i -lt $name.Length;$i++){$p[35+$i]=$name[$i]-13}
    return ,$p
}
function New-Create {
    $p=[byte[]]::new(100);Put32 $p 1 0xEDEDEDEDL;Put32 $p 5 4294967295
    [Text.Encoding]::ASCII.GetBytes('ReplayAvatar').CopyTo($p,10)
    $p[71]=30;$p[72]=20;$p[73]=15;$p[74]=0;$p[75]=50;$p[76]=1;$p[77]=50;$p[78]=2
    Put16 $p 80 1002
    return ,$p
}
function Protect-Game([byte[]]$Plain,[string]$Name,[long]$Key){
    $inFile=Join-Path $Evidence ($Name+'.plain');$outFile=Join-Path $Evidence ($Name+'.cipher')
    [IO.File]::WriteAllBytes($inFile,$Plain)
    [IO.File]::WriteAllBytes($outFile,(Login-Xor $Plain $Key))
    return ,[IO.File]::ReadAllBytes($outFile)
}
$relayOutput=@(Open-Relay);$key=[long]$relayOutput[-1];$relayOutput|Select-Object -SkipLast 1
$game=New-GameLogin $key;$create=New-Create
$status=[byte[]]::new(10);$status[0]=52;Put32 $status 1 0xEDEDEDEDL;$status[5]=4;Put32 $status 6 1
$requests=[Collections.Generic.List[byte]]::new();$requests.AddRange($game)
$createAt=$requests.Count;$requests.AddRange($create)
$tipAt=$requests.Count;$requests.AddRange([byte[]]@(167,255,255,0))
$statusAt=$requests.Count;$requests.AddRange($status)
$movesAt=$requests.Count
for($i=0;$i -lt 20;$i++){$requests.AddRange([byte[]]@(2,2,$i))}
$speech=[byte[]]([byte[]]@(3,0,17,0,3,178,0,3)+[Text.Encoding]::ASCII.GetBytes('I accept')+[byte[]]@(0))
$maximum=[byte[]]::new(137);$maximum[0]=3;Put16 $maximum 1 137;$maximum[3]=9;Put16 $maximum 4 946;Put16 $maximum 6 3
for($i=8;$i -lt 136;$i++){$maximum[$i]=97}
$speechAt=$requests.Count;$requests.AddRange($speech)
$maximumAt=$requests.Count;$requests.AddRange($maximum);$requests.AddRange([byte[]]@(115,42))
$hooksAt=$requests.Count
if($Hooks){$requests.AddRange([byte[]]@(5,0,0,0,1,18,0,5,65,0,114,1,0,0,0))}
$logoutAt=$requests.Count
$requests.AddRange([byte[]]@(1,255,255,255,255))
$encrypted=Protect-Game $requests.ToArray() 'create-walk' $key
$tcp=[Net.Sockets.TcpClient]::new('127.0.0.1',$Port)
try{
    $stream=$tcp.GetStream();$stream.ReadTimeout=15000
    $seed=[byte[]]::new(4);Put32 $seed 0 $key;$stream.Write($seed,0,4);$stream.Write($encrypted,0,65)
    $list=Read-Packet $stream
    Check 'empty character list' ($list.Length -eq 372 -and (U16 $list 1) -eq 372 -and $list[0] -eq 169 -and $list[4] -eq 0 -and (U32 $list 368) -eq 0)
    $stream.Write($encrypted,$createAt,$create.Length)
    $entered=@();for($i=0;$i -lt 6;$i++){$entered+=,(Read-Packet $stream)}
    Check 'entry packet order' ((@($entered|ForEach-Object{$_[0]})-join ',') -eq '27,32,46,46,79,85')
    Check 'entry position' ((U32 $entered[0] 1) -eq 1 -and (U16 $entered[0] 11) -eq 1420 -and (U16 $entered[0] 13) -eq 1698)
    Check 'player body' ($entered[1].Length -eq 19 -and (U16 $entered[1] 5) -eq 400)
    Check 'equipment owner and colors' ((U32 $entered[2] 9) -eq 1 -and (U16 $entered[2] 13) -eq 0 -and (U16 $entered[3] 13) -eq 0)
    $stream.Write($encrypted,$tipAt,4+$status.Length);$stats=Read-Packet $stream
    Check 'optional tip preserves following status packet' ($stats[0] -eq 17)
    Check 'initial stats' ($stats.Length -eq 66 -and $stats[0] -eq 17 -and (U16 $stats 44) -eq 30 -and (U16 $stats 46) -eq 20 -and (U16 $stats 48) -eq 15)
    $accepted=0;$denied=0
    for($i=0;$i -lt 20;$i++){
        $stream.Write($encrypted,$movesAt+$i*3,3);$reply=Read-Packet $stream
        Check "movement response $i" (($reply[0] -eq 34 -and $reply.Length -eq 3) -or ($reply[0] -eq 33 -and $reply.Length -eq 8))
        Check "movement sequence $i" ($reply[1] -eq $i)
        if($reply[0] -eq 34){$accepted++}else{$denied++}
    }
    Check 'movement reached' ($accepted -ge 2)
    for($i=0;$i -lt $speech.Length;$i++){$stream.Write($encrypted,$speechAt+$i,1)}
    $said=Read-Packet $stream
    Check 'fragmented speech length and speaker' ($said.Length -eq 53 -and $said[0] -eq 28 -and (U16 $said 1) -eq 53 -and (U32 $said 3) -eq 1)
    Check 'speech server name and exact text' ([Text.Encoding]::ASCII.GetString($said,14,12) -eq 'ReplayAvatar' -and [Text.Encoding]::ASCII.GetString($said,44,8) -eq 'I accept' -and $said[52] -eq 0)
    $stream.Write($encrypted,$maximumAt,$maximum.Length+2)
    $said=Read-Packet $stream;$ping=Read-Packet $stream
    Check 'maximum speech followed by coalesced ping' ($said.Length -eq 173 -and $said[9] -eq 9 -and $said[172] -eq 0 -and [Text.Encoding]::ASCII.GetString($said,44,128) -eq ('a'*128) -and $ping.Length -eq 2 -and $ping[0] -eq 115 -and $ping[1] -eq 42)
    if($Hooks){
        $stream.Write($encrypted,$hooksAt,5)
        $fixed=Read-Packet $stream;$pulse=Read-Packet $stream
        Check 'registered fixed handler and pulse replies' ([Convert]::ToHexString($fixed) -eq '735A' -and [Convert]::ToHexString($pulse) -eq '735C')
        for($i=0;$i -lt 5;$i++){$stream.Write($encrypted,$hooksAt+5+$i,1)}
        $variable=Read-Packet $stream
        Check 'registered variable handler reply' ([Convert]::ToHexString($variable) -eq '735B')
        $stream.Write($encrypted,$hooksAt+10,5);$war=Read-Packet $stream
        Check 'registered war-mode reply' ([Convert]::ToHexString($war) -eq '7201000000')
    }
    $stream.Write($encrypted,$logoutAt,5)
    Check 'logout EOF' ($stream.ReadByte() -eq -1)
    "movement accepted=$accepted denied=$denied"
}finally{$tcp.Dispose()}
$relayOutput=@(Open-Relay);$nextKey=[long]$relayOutput[-1];$relayOutput|Select-Object -SkipLast 1
Check 'fresh relay key' ($nextKey -ne $key)
$play=[byte[]]::new(73);$play[0]=93;Put32 $play 1 0xEDEDEDEDL
$plain=[byte[]]((New-GameLogin $nextKey)+$play+[byte[]]@(1,255,255,255,255))
$encrypted=Protect-Game $plain 'select-existing' $nextKey
$tcp=[Net.Sockets.TcpClient]::new('127.0.0.1',$Port)
try{
    $stream=$tcp.GetStream();$stream.ReadTimeout=15000
    $seed=[byte[]]::new(4);Put32 $seed 0 $nextKey;$stream.Write($seed,0,4);$stream.Write($encrypted,0,65)
    $list=Read-Packet $stream
    Check 'character survives reconnect' ($list.Length -eq 372 -and (U16 $list 1) -eq 372 -and (U32 $list 368) -eq 0 -and [Text.Encoding]::ASCII.GetString($list,4,12) -eq 'ReplayAvatar')
    $stream.Write($encrypted,65,73)
    $entered=@();for($i=0;$i -lt 6;$i++){$entered+=,(Read-Packet $stream)}
    Check 'existing character selected' ($entered[0][0] -eq 27 -and (U32 $entered[0] 1) -eq 1)
    $stream.Write($encrypted,138,5)
    Check 'second logout EOF' ($stream.ReadByte() -eq -1)
}finally{$tcp.Dispose()}
for($case=0;$case -lt 7;$case++){
    $relayOutput=@(Open-Relay);$caseKey=[long]$relayOutput[-1];$relayOutput|Select-Object -SkipLast 1
    $login=New-GameLogin $caseKey
    $tail=[byte[]]@()
    if($case -eq 0){$login[35]=33}
    elseif($case -eq 1){$tail=New-Create;Put16 $tail 94 1;$tail[71]=46}
    elseif($case -eq 2){$tail=[byte[]]@(255)}
    elseif($case -eq 3){$tail=[byte[]]@(3,0,9)}
    elseif($case -eq 4){$tail=[byte[]]@(3,1,1)}
    elseif($case -eq 5){$tail=[byte[]]@(3,0,17,0)}
    else{$tail=[byte[]]::new(299);$tail[0]=164;$tail[149]=164;$tail[298]=255}
    $encrypted=Protect-Game ([byte[]]($login+$tail)) "refusal-$case" $caseKey
    $tcp=[Net.Sockets.TcpClient]::new('127.0.0.1',$Port)
    try{
        $stream=$tcp.GetStream();$stream.ReadTimeout=15000
        $seed=[byte[]]::new(4);Put32 $seed 0 $caseKey;$stream.Write($seed,0,4);$stream.Write($encrypted,0,65)
        if($case -ne 0){
            $list=Read-Packet $stream
            Check "refusal state preserves slots $case" ($list[4] -eq 82 -and $list[64] -eq 0)
            $stream.Write($encrypted,65,$tail.Length)
            if($case -eq 5){$tcp.Client.Shutdown([Net.Sockets.SocketShutdown]::Send)}
        }
        Check "malformed request closes $case" ($stream.ReadByte() -eq -1)
        $wire=[byte[]]($seed+$encrypted)
        $retained=[Math]::Min(256,$wire.Length)
        $hex=[Convert]::ToHexString($wire,$wire.Length-$retained,$retained)
        $opcode=if($case -eq 0){145}elseif($case -eq 1){0}elseif($case -eq 2 -or $case -eq 6){255}else{3}
        $script:refusals.Add(@{connection=5+2*$case;opcode=$opcode;rawCount=$retained;raw=$hex;partial=($case -eq 5)})
    }finally{$tcp.Dispose()}
}
$relayOutput=@(Open-Relay);$survivalKey=[long]$relayOutput[-1];$relayOutput|Select-Object -SkipLast 1
Check 'accepts login after final malformed connection' ($survivalKey -gt 0)
$log=''
for($attempt=0;$attempt -lt 20;$attempt++){
    $file=[IO.File]::Open([IO.Path]::GetFullPath($ServerLog),[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
    $reader=[IO.StreamReader]::new($file)
    try{$log=$reader.ReadToEnd()}finally{$reader.Dispose()}
    if($log -match 'REFUSE connection=17 '){break}
    Start-Sleep -Milliseconds 100
}
foreach($expected in $script:refusals){
    $pattern='(?m)^REFUSE connection='+$expected.connection+' mode=game-xor opcode='+$expected.opcode+' reason=(.*?) raw-count='+$expected.rawCount+' raw='+$expected.raw+'\r?$'
    $match=[regex]::Match($log,$pattern)
    Check "bounded refusal diagnostic $($expected.connection)" $match.Success
    if($expected.partial){Check 'truncated EOF names incomplete packet' ($match.Groups[1].Value -eq 'incomplete packet at EOF')}
}
Check 'optional tip is logged without disconnect' ($log -match 'PACKET 167 optional tip/notice ignored id=65535 type=0 connection=1')
if($Hooks){Check 'opcode startup and pulse evidence' ($log -match 'PASS opcode startup checks' -and $log -match 'PULSE hook pulse tick=')}
$script:refusals|ConvertTo-Json -Depth 4|Set-Content (Join-Path $Evidence 'refusals.json') -Encoding utf8
$script:packets|ConvertTo-Json -Depth 4|Set-Content (Join-Path $Evidence 'packets.json') -Encoding utf8
"PASS game-server replay checks=$script:checks; unmodified client acceptance not claimed"
