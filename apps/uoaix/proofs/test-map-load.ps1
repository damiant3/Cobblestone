[CmdletBinding()]
param([Parameter(Mandatory)][string]$Actual,[Parameter(Mandatory)][string]$ClientRoot)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$text=[IO.File]::ReadAllText((Resolve-Path -LiteralPath $Actual)).Replace("`r",'')
if($text -match 'FAIL|REFUSE|!EXC'){throw 'Guest reported failure'}
$samples=@($text -split "`n"|Where-Object{$_ -match '^LAND '})
if($samples.Count -ne 20 -or $text -notmatch '(?m)^map samples complete$'){throw 'Incomplete height sample manifest'}
$map=[IO.File]::Open((Join-Path (Resolve-Path -LiteralPath $ClientRoot) 'MAP0.MUL'),[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
function Read-Z([int]$X,[int]$Y){
    $block=[long][Math]::Floor($X/8)*512+[long][Math]::Floor($Y/8)
    $offset=$block*196+4+(($Y%8)*8+$X%8)*3+2
    [void]$map.Seek($offset,[IO.SeekOrigin]::Begin)
    $value=$map.ReadByte()
    if($value -lt 0){throw 'Short original map read'}
    if($value -ge 128){$value-256}else{$value}
}
try{
    for($i=0;$i -lt 20;$i++){
        $x=1408+$i%10;$y=1680+[int][Math]::Floor($i/10)
        $a=Read-Z $x $y;$b=Read-Z $x ($y+1);$c=Read-Z ($x+1) $y;$d=Read-Z ($x+1) ($y+1)
        $z=if([Math]::Abs($a-$d) -gt [Math]::Abs($b-$c)){($b+$c)-shr 1}else{($a+$d)-shr 1}
        if($samples[$i] -cne "LAND $x $y $z"){throw "Height mismatch: $($samples[$i]) versus LAND $x $y $z"}
    }
}finally{$map.Dispose()}
'PASS 20 guest terrain samples agree with original map; client walking is a separate grade'
