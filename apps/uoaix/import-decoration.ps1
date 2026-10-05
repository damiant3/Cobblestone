# GPL-2.0. Port of ServUO Decorate.cs and BaseDoor/Doors/SecretDoors.
# Data remains in the supplied reference checkout. See WorldDecoration.md.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ClientRoot,
    [Parameter(Mandatory)][string]$OutFile,
    [string]$ReferenceRoot = 'D:/Projects/uo-reference',
    [ValidateRange(0,6143)][int]$X = 0,
    [ValidateRange(0,4095)][int]$Y = 0,
    [ValidateRange(2,6144)][int]$Width = 6144,
    [ValidateRange(2,4096)][int]$Height = 4096
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($X+$Width -gt 6144 -or $Y+$Height -gt 4096) { throw 'Decoration window outside Britannia' }
$tilePath = Join-Path $ClientRoot 'TILEDATA.MUL'
$tiles = [IO.File]::ReadAllBytes($tilePath)
if ($tiles.Length -ne 1036288) { throw 'Requires legacy 32-bit TILEDATA.MUL' }
$tileCache = @{}
function Read-Tile([int]$Id) {
    if ($Id -lt 0 -or $Id -ge 16384) { return $null }
    if ($tileCache.ContainsKey($Id)) { return $tileCache[$Id] }
    $at = 428032 + [int][Math]::Floor($Id / 32) * 1188 + 4 + ($Id % 32) * 37
    $flags = [BitConverter]::ToUInt32($tiles,$at)
    $name = [Text.Encoding]::ASCII.GetString($tiles,$at+17,20).Trim([char]0).Trim()
    $value = if ($flags -eq 0 -and $name.Length -eq 0 -and $tiles[$at+16] -eq 0) { $null } else {
        [pscustomobject]@{Flags=[long]$flags;Height=[int]$tiles[$at+16];Name=$name}
    }
    $tileCache[$Id]=$value
    return $value
}
$facing = @('WestCW','EastCCW','WestCCW','EastCW','SouthCW','NorthCCW','SouthCCW','NorthCW')
$dx = @(-1,1,-1,1,1,1,0,0)
$dy = @(1,1,0,-1,1,-1,0,-1)
$doors=@{}
$inputs=[Collections.Generic.List[string]]::new()
foreach($name in @('Doors.cs','SecretDoors.cs')) {
    $path=Join-Path $ReferenceRoot "ServUO/Scripts/Items/Functional/$name"
    $inputs.Add($path)
    $class=''
    foreach($line in [IO.File]::ReadLines($path)) {
        if($line -match 'public class (\w+) : BaseDoor') {$class=$Matches[1]}
        if($line -match ': base\((0x[0-9A-Fa-f]+) \+.*?, (0x[0-9A-Fa-f]+) \+') {
            $doors[$class]=@([Convert]::ToInt32($Matches[1],16),[Convert]::ToInt32($Matches[2],16))
        }
    }
}
$lights=@{}
$itemSource=Join-Path $ReferenceRoot 'ServUO/Server/Item.cs'
$inputs.Add($itemSource)
$inLight=$false
foreach($line in [IO.File]::ReadLines($itemSource)) {
    if($line -match 'enum LightType') {$inLight=$true;continue}
    if($inLight -and $line -match '^\s*}') {break}
    if($inLight -and $line -match '^\s*(\w+)\s*=\s*(\d+)') {$lights[$Matches[1]]=[int]$Matches[2]}
}
$rows=@{}
$doorPositions=@{}
$skipped=[Collections.Generic.List[object]]::new()
$addons=@{}
function Add-Placement([string]$Type,[int]$Art,[hashtable]$Props,[int]$AtX,[int]$AtY,[int]$AtZ,[string]$Name,[string]$Source) {
    if($AtX -lt $X-16 -or $AtY -lt $Y-16 -or $AtX -ge $X+$Width+16 -or $AtY -ge $Y+$Height+16) {return}
    if($Type -notmatch 'Addon$') {Add-Decoration $Type $Art $Props $AtX $AtY $AtZ $Name $Source;return}
    if(-not $addons.ContainsKey($Type)) {
        $path=Join-Path $ReferenceRoot "ServUO/Scripts/Items/Addons/$Type.cs"
        if(-not (Test-Path -LiteralPath $path)) {
            $found=@(Get-ChildItem (Join-Path $ReferenceRoot 'ServUO/Scripts/Items/Addons') -Filter '*.cs' -File | Where-Object {[IO.File]::ReadAllText($_.FullName) -match "class\s+$Type\s*:"})
            if($found.Count -ne 1) {throw "Missing or ambiguous addon source $Type at $Source"}
            $path=$found[0].FullName
        }
        $inputs.Add($path)
        $sourceText=[IO.File]::ReadAllText($path)
        $constructor=[regex]::Match($sourceText,"(?is)public\s+$Type\s*\(\s*\)\s*\{([^}]+)\}")
        if(-not $constructor.Success) {throw "Unported addon constructor $Type at $Source"}
        $body=$constructor.Groups[1].Value
        $parts=[regex]::Matches($body,'AddComponent\(new\s+\w+\((0x[0-9A-Fa-f]+|\d+|\w+\+\+|\+\+\w+)\),\s*([+-]?\d+),\s*([+-]?\d+),\s*([+-]?\d+)\)')
        if($parts.Count -eq 0 -or $parts.Count -ne [regex]::Matches($body,'AddComponent\(').Count -or $body -match '\b(for|while|if|switch)\s*\(') {throw "Unported addon components $Type at $Source"}
        $counters=@{}
        foreach($counter in [regex]::Matches($body,'int\s+(\w+)\s*=\s*(0x[0-9A-Fa-f]+|\d+)')) {
            $counters[$counter.Groups[1].Value]=[Convert]::ToInt32($counter.Groups[2].Value,$(if($counter.Groups[2].Value.StartsWith('0x')){16}else{10}))
        }
        $expanded=[Collections.Generic.List[object]]::new()
        foreach($part in $parts) {
            $expression=$part.Groups[1].Value
            if($expression.Contains('++')) {
                $counter=$expression.Replace('++','')
                if(-not $counters.ContainsKey($counter)) {throw "Unknown addon counter $Type at $Source"}
                if($expression.StartsWith('++')) {$counters[$counter]++}
                $partArt=$counters[$counter]
                if($expression.EndsWith('++')) {$counters[$counter]++}
            } else {$partArt=[Convert]::ToInt32($expression,$(if($expression.StartsWith('0x')){16}else{10}))}
            $expanded.Add([pscustomobject]@{Art=$partArt;X=[int]$part.Groups[2].Value;Y=[int]$part.Groups[3].Value;Z=[int]$part.Groups[4].Value})
        }
        $addons[$Type]=$expanded
    }
    foreach($part in $addons[$Type]) {Add-Decoration 'Static' $part.Art $Props ($AtX+$part.X) ($AtY+$part.Y) ($AtZ+$part.Z) $Name $Source}
}
function Add-Decoration([string]$Type,[int]$Art,[hashtable]$Props,[int]$AtX,[int]$AtY,[int]$AtZ,[string]$Name,[string]$Source) {
    if($AtX -lt $X -or $AtY -lt $Y -or $AtX -ge $X+$Width -or $AtY -ge $Y+$Height) {return}
    if($AtZ -lt -128 -or $AtZ -gt 127) {throw "Decoration z outside legacy packet: $Source"}
    $tile=Read-Tile $Art
    if($null -eq $tile) {$skipped.Add([pscustomobject]@{Source=$Source;Art=$Art;Reason='absent legacy tiledata'});return}
    $kind=0;$open=$Art;$ox=0;$oy=0;$light=-1;$openTile=$tile
    if($doors.ContainsKey($Type)) {
        $face=0
        if($Props.ContainsKey('Facing')) {
            $face=-1
            for($direction=0;$direction -lt $facing.Length;$direction++) {if($facing[$direction] -eq $Props.Facing) {$face=$direction;break}}
        }
        if($face -lt 0) {throw "Unknown door facing at $Source"}
        $kind=1;$open=$doors[$Type][1]+2*$face;$ox=$dx[$face];$oy=$dy[$face]
        $openTile=Read-Tile $open
        if($null -eq $openTile) {$skipped.Add([pscustomobject]@{Source=$Source;Art=$open;Reason='absent open-door tiledata'});return}
        if($AtX+$ox -lt $X -or $AtY+$oy -lt $Y -or $AtX+$ox -ge $X+$Width -or $AtY+$oy -ge $Y+$Height) {
            $skipped.Add([pscustomobject]@{Source=$Source;Art=$Art;Reason='door swing outside loaded window'});return
        }
        if($Props.ContainsKey('Locked') -and $Props.Locked -ne 'false') {$kind=4}
    } elseif($Type -match 'Door|Gate' -and $Props.ContainsKey('Facing')) {throw "Unported door type $Type at $Source"}
    elseif($Type -match 'Sign') {$kind=2}
    if(($tile.Flags -band 0x800000) -ne 0 -or $Props.ContainsKey('Light') -or $Type -match 'Lamp|Lantern|Torch|Candle|Brazier') {
        $light=1
        if($Props.ContainsKey('Light')) {
            if(-not $lights.ContainsKey($Props.Light)) {throw "Unknown light pattern at $Source"}
            $light=$lights[$Props.Light]
        }
        if($kind -eq 0) {$kind=3}
    }
    $hue=if($Props.ContainsKey('Hue')) {[Convert]::ToInt32($Props.Hue, $(if($Props.Hue.StartsWith('0x')){16}else{10}))}else{0}
    if($hue -lt 0 -or $hue -gt 65535) {throw "Invalid hue at $Source"}
    if($Props.ContainsKey('Name')) {$Name=$Props.Name}
    if([string]::IsNullOrWhiteSpace($Name)) {$Name=$tile.Name}
    $Name=[Text.Encoding]::ASCII.GetString([Text.Encoding]::ASCII.GetBytes($Name))
    if($Name.Length -gt 63) {$Name=$Name.Substring(0,63)}
    $key="$AtX,$AtY,$AtZ,$Art,$hue"
    $rows[$key]=[pscustomobject]@{X=$AtX;Y=$AtY;Z=$AtZ;Art=$Art;Hue=$hue;Flags=$tile.Flags;Height=$tile.Height;Kind=$kind;Open=$open;DX=$ox;DY=$oy;Light=$light;OpenFlags=$openTile.Flags;OpenHeight=$openTile.Height;Name=$Name;Source=$Source}
    if($kind -eq 1 -or $kind -eq 4) {$doorPositions["$AtX,$AtY,$AtZ"]=$true}
}
foreach($folder in @('ServUO/Data/Decoration/Old/Britannia','ServUO/Data/Decoration/Britannia')) {
    foreach($file in (Get-ChildItem (Join-Path $ReferenceRoot $folder) -Filter '*.cfg' -File | Sort-Object Name)) {
        $inputs.Add($file.FullName)
        $type='';$art=0;$props=@{};$comment='';$lineNo=0
        foreach($line in [IO.File]::ReadLines($file.FullName)) {
            $lineNo++;$line=$line.Trim()
            if($line.Length -eq 0) {continue}
            if($line.StartsWith('#')) {$comment=$line.TrimStart('#').Trim();continue}
            if($line -match '^(\w+)\s+(0x[0-9a-fA-F]+|\d+)(?:\s*\((.*)\))?$') {
                $type=$Matches[1];$art=[Convert]::ToInt32($Matches[2],$(if($Matches[2].StartsWith('0x')){16}else{10}));$props=@{}
                if($Matches.ContainsKey(3)) {foreach($property in $Matches[3].Split(';')) {
                    $pair=$property.Trim().Split('=',2);if($pair[0]) {$props[$pair[0].Trim()]=if($pair.Length -eq 2){$pair[1].Trim()}else{'true'}}
                }}
            } elseif($line -match '^(-?\d+)\s+(-?\d+)\s+(-?\d+)(?:\s+.*)?$') {
                if(-not $type) {throw "Placement without definition at $($file.Name):$lineNo"}
                Add-Placement $type $art $props ([int]$Matches[1]) ([int]$Matches[2]) ([int]$Matches[3]) $comment "$folder/$($file.Name):$lineNo"
            } elseif($line -match '^-?\d+\s+-?\d+\s+\S+') {
                $skipped.Add([pscustomobject]@{Source="$folder/$($file.Name):$lineNo";Art=$art;Reason='malformed placement';Text=$line})
            } else {throw "Unparsed decoration at $($file.Name):$lineNo"}
        }
    }
}
# ServUO's CFGs omit doors installed by DoorGenerator. UOX3's literal Felucca
# door template supplies those placements; typed CFG doors take precedence.
$doorTemplate=Join-Path $ReferenceRoot 'UOX3/data/js/jsdata/worldtemplates/felucca_doors.jsdata'
$inputs.Add($doorTemplate)
$doorArt=@{}
foreach($typeName in ($doors.Keys | Sort-Object)) {
    for($face=0;$face -lt $facing.Length;$face++) {
        $closed=$doors[$typeName][0]+2*$face
        if(-not $doorArt.ContainsKey($closed)) {$doorArt[$closed]=@($typeName,$face)}
    }
}
$lineNo=0;$templateVersion=$false;$templateDoors=0
foreach($line in [IO.File]::ReadLines($doorTemplate)) {
    $lineNo++
    if([string]::IsNullOrWhiteSpace($line)) {continue}
    if(-not $templateVersion) {if($line.Trim() -ne '3') {throw 'Requires UOX3 door template version3'};$templateVersion=$true;continue}
    $parts=$line.Split('@',2)[0].Split('|')
    $source="UOX3/felucca_doors.jsdata:$lineNo"
    if($parts.Length -lt 24) {throw "Malformed door template row at $source"}
    $art=[int]$parts[0];$atX=[int]$parts[4];$atY=[int]$parts[5];$atZ=[int]$parts[6]
    if([int]$parts[7] -ne 0 -or [int]$parts[8] -ne 0 -or $atX -lt $X -or $atY -lt $Y -or $atX -ge $X+$Width -or $atY -ge $Y+$Height) {continue}
    if([int]$parts[3] -notin 12,13) {throw "Non-door type at $source"}
    if($doorPositions.ContainsKey("$atX,$atY,$atZ")) {continue}
    if(-not $doorArt.ContainsKey($art)) {$skipped.Add([pscustomobject]@{Source=$source;Art=$art;Reason='unmapped legacy door art'});continue}
    $definition=$doorArt[$art]
    $props=@{Facing=$facing[$definition[1]];Hue=$parts[2];Locked=if([int]$parts[3] -eq 13){'true'}else{'false'}}
    $name=if($parts[1] -eq '#'){''}else{$parts[1]}
    Add-Decoration $definition[0] $art $props $atX $atY $atZ $name $source
    if($doorPositions.ContainsKey("$atX,$atY,$atZ")) {$templateDoors++}
}
# UOX3 supplies literal names where ServUO uses newer client-localized labels.
$signs=Join-Path $ReferenceRoot 'UOX3/data/js/jsdata/worldtemplates/felucca_signs.jsdata'
$inputs.Add($signs)
foreach($line in [IO.File]::ReadLines($signs)) {
    $parts=$line.Split('|')
    if($parts.Length -lt 7 -or $parts[1] -eq '#') {continue}
    Add-Decoration 'Sign' ([int]$parts[0]) @{} ([int]$parts[4]) ([int]$parts[5]) ([int]$parts[6]) $parts[1] 'UOX3/felucca_signs.jsdata'
}
$ordered=@($rows.Values | Sort-Object X,Y,Z,Art,Hue)
if($ordered.Count -gt 65536) {throw 'Decoration region exceeds 65536 rows; reduce region'}
$bytes=[byte[]]::new(64+$ordered.Count*176)
function Put-Qword([int]$At,[long]$Value) {[BitConverter]::GetBytes($Value).CopyTo($bytes,$At)}
Put-Qword 0 0x31445744;Put-Qword 8 $ordered.Count;Put-Qword 16 $X;Put-Qword 24 $Y;Put-Qword 32 $Width;Put-Qword 40 $Height
for($i=0;$i -lt $ordered.Count;$i++) {
    $row=$ordered[$i];$at=64+$i*176
    $values=@($row.X,$row.Y,$row.Z,$row.Art,$row.Hue,$row.Flags,$row.Height,$row.Kind,$row.Open,$row.DX,$row.DY,$row.Light,$row.OpenFlags,$row.OpenHeight)
    for($j=0;$j -lt $values.Length;$j++) {Put-Qword ($at+$j*8) $values[$j]}
    [Text.Encoding]::ASCII.GetBytes($row.Name).CopyTo($bytes,$at+112)
}
$digest=[Security.Cryptography.SHA256]::HashData($bytes)
$identity=[BitConverter]::ToInt64($digest,0) -band [long]::MaxValue
Put-Qword 48 $identity
$output=[IO.Path]::GetFullPath($OutFile)
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($output))
[IO.File]::WriteAllBytes($output,$bytes)
$manifest=[ordered]@{format='DWD1';window=@($X,$Y,$Width,$Height);rows=$ordered.Count;doors=@($ordered|Where-Object {$_.Kind -in 1,4}).Count;templateDoors=$templateDoors;identity=$identity;tiledataSha256=(Get-FileHash $tilePath).Hash;outputSha256=(Get-FileHash $output).Hash;skipped=$skipped;inputs=@($inputs|ForEach-Object{@{path=$_;sha256=(Get-FileHash -LiteralPath $_).Hash}})}
$manifest|ConvertTo-Json -Depth 6|Set-Content -LiteralPath "$output.json" -Encoding utf8
Write-Output "DWD1 rows=$($ordered.Count) skipped=$($skipped.Count) output=$output"
