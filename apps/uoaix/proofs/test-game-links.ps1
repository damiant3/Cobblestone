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

function Open-Partial([long]$Key,[int]$Prefix){
    $tcp=[Net.Sockets.TcpClient]::new('127.0.0.1',$Port)
    $stream=$tcp.GetStream();$stream.ReadTimeout=15000
    $login=New-GameLogin $Key;$cipher=Login-Xor $login $Key
    $seed=[byte[]]::new(4);Put32 $seed 0 $Key
    $stream.Write($seed,0,4);$stream.Write($cipher,0,$Prefix)
    $plain=[Collections.Generic.List[byte]]::new();$plain.AddRange($login)
    [pscustomobject]@{Tcp=$tcp;Stream=$stream;Key=$Key;Plain=$plain;Login=$cipher;Prefix=$Prefix}
}
function Send-Plain($Client,[byte[]]$Bytes){
    $offset=$Client.Plain.Count;$Client.Plain.AddRange($Bytes)
    $cipher=Login-Xor $Client.Plain.ToArray() $Client.Key
    $Client.Stream.Write($cipher,$offset,$Bytes.Length)
}
function Finish-Login($Client){
    $Client.Stream.Write($Client.Login,$Client.Prefix,65-$Client.Prefix)
    $list=Read-Packet $Client.Stream
    Check 'independent game authentication' ($list[0] -eq 169 -and $list.Length -eq 372)
}
function Enter-New($Client,[int]$Slot,[string]$Name){
    $create=New-Create;Put32 $create 92 $Slot
    [Array]::Clear($create,10,30);[Text.Encoding]::ASCII.GetBytes($Name).CopyTo($create,10)
    Send-Plain $Client $create
    $entered=@();for($i=0;$i -lt 6;$i++){$entered+=,(Read-Packet $Client.Stream)}
    Check 'peer enters complete world sequence' ((@($entered|ForEach-Object{$_[0]})-join ',') -eq '27,32,46,46,79,85')
    $serial=U32 $entered[0] 1
    $pulse=Read-Packet $Client.Stream
    Check 'pulse reaches the correct peer actor' ($pulse[0] -eq 115 -and $pulse[1] -eq ($serial-band 255))
    return $serial
}
function Check-Ping($Client,[byte]$Value){Send-Plain $Client ([byte[]]@(115,$Value));$p=Read-Packet $Client.Stream;Check 'other peer remains responsive' ($p[0] -eq 115 -and $p[1] -eq $Value)}
$clients=[Collections.Generic.List[object]]::new()
try{
    $one=@(Open-Relay);$keyA=[long]$one[-1];$one|Select-Object -SkipLast 1
    $two=@(Open-Relay);$keyB=[long]$two[-1];$two|Select-Object -SkipLast 1
    Check 'two outstanding relay tickets are distinct' ($keyA -ne $keyB)
    $a=Open-Partial $keyA 20;$clients.Add($a)
    $b=Open-Partial $keyB 30;$clients.Add($b)
    Finish-Login $a
    $enteredA=@(Enter-New $a 0 'FirstPlayer');$serialA=[long]$enteredA[-1];$enteredA|Select-Object -SkipLast 1
    Check 'incomplete second login does not block first player' (-not $b.Stream.DataAvailable)
    Finish-Login $b
    $enteredB=@(Enter-New $b 1 'SecondPlayer');$serialB=[long]$enteredB[-1];$enteredB|Select-Object -SkipLast 1
    Check 'simultaneous players have different world serials' ($serialA -ne $serialB)
    for($sequence=0;$sequence -lt 2;$sequence++){
        Send-Plain $a ([byte[]]@(2,2,$sequence));Send-Plain $b ([byte[]]@(2,4,$sequence))
        $ma=Read-Packet $a.Stream;$mb=Read-Packet $b.Stream
        Check 'independent movement sequence and reply routing' ($ma[0] -eq 34 -and $mb[0] -eq 34 -and $ma[1] -eq $sequence -and $mb[1] -eq $sequence)
    }
    foreach($pair in @(@($a,$serialA),@($b,$serialB))){
        $status=[byte[]]::new(10);$status[0]=52;Put32 $status 1 0xEDEDEDEDL;$status[5]=4;Put32 $status 6 $pair[1]
        Send-Plain $pair[0] $status;$reply=Read-Packet $pair[0].Stream
        Check 'status belongs to its authenticated character' ($reply[0] -eq 17 -and (U32 $reply 3) -eq $pair[1])
    }
    $third=@(Open-Relay);$keyC=[long]$third[-1];$third|Select-Object -SkipLast 1
    $c=Open-Partial $keyC 65;$clients.Add($c);Finish-Login $c
    $select=[byte[]]::new(73);$select[0]=93;Put32 $select 1 0xEDEDEDEDL
    Send-Plain $c $select
    Check 'second controller of an active character is refused' ($c.Stream.ReadByte() -eq -1)
    $c.Tcp.Dispose()
    Check-Ping $a 91;Check-Ping $b 92
    $replay=Open-Partial $keyA 65;$clients.Add($replay)
    Check 'consumed relay cannot authenticate another game connection' ($replay.Stream.ReadByte() -eq -1)
    $replay.Tcp.Dispose()
    Send-Plain $a ([byte[]]@(1,255,255,255,255));Check 'first player logout EOF' ($a.Stream.ReadByte() -eq -1)
    $a.Tcp.Dispose();Check-Ping $b 93
    Send-Plain $b ([byte[]]@(1,255,255,255,255));Check 'second player logout EOF' ($b.Stream.ReadByte() -eq -1)
    $b.Tcp.Dispose()
    $file=[IO.File]::Open([IO.Path]::GetFullPath($ServerLog),'Open','Read',([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete));$reader=[IO.StreamReader]::new($file)
    try{$log=$reader.ReadToEnd()}finally{$reader.Dispose()}
    Check 'disconnect callbacks are once per connection' ($log -notmatch 'DISCONNECT \d+ calls=[2-9]' -and $log -match 'DISCONNECT')
    $script:packets|ConvertTo-Json -Depth 4|Set-Content (Join-Path $Evidence 'packets.json') -Encoding utf8
    "PASS concurrent game-links checks=$script:checks"
}finally{foreach($client in $clients){$client.Tcp.Dispose()}}
