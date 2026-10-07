param(
    [string]$ReferenceRoot = 'D:/Projects/uo-reference/UOX3',
    [string]$AnimIndex = 'D:/Projects/uoaix-client/ANIM.IDX',
    [string]$BodyDef = 'D:/OldProjects/Projects/UO/UOCli_Official/Body.def',
    [string]$Output = "$PSScriptRoot/WorldSpawnData.codex",
    [string]$SoundOutput = "$PSScriptRoot/CreatureSoundData.codex",
    [string]$CarveOutput = "$PSScriptRoot/CreatureCarveData.codex",
    [string]$Manifest = "$PSScriptRoot/../../build-output/uoaix/world-spawns/import.json"
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$sections = @{}
$sourceHashes = [ordered]@{}
$issues = [Collections.Generic.List[string]]::new()
function Read-Sections([string]$path) {
    $sourceHashes[$path] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    $raw = [IO.File]::ReadAllText($path)
    foreach($match in [regex]::Matches($raw, '(?ms)^\s*\[([^\]\r\n]+)\]\s*\{(.*?)^\s*\}')) {
        $lines = [Collections.Generic.List[string]]::new()
        foreach($line in ($match.Groups[2].Value -split '\r?\n')) {
            $value = $line.Trim()
            if($value -match '^NAME\s*=\s*#\s*//\s*(.+)$') { $value = 'NAME=' + $Matches[1] }
            else { $value = ($value -split '//',2)[0].Trim() }
            if($value) { $lines.Add($value) }
        }
        [pscustomobject]@{ Name=$match.Groups[1].Value.Trim(); Lines=$lines.ToArray(); File=$path }
    }
}
foreach($file in (Get-ChildItem -LiteralPath "$ReferenceRoot/data/dfndata/npc" -Recurse -Filter '*.dfn' | Sort-Object FullName)) {
    foreach($section in (Read-Sections $file.FullName)) {
        if($sections.ContainsKey($section.Name)) { $issues.Add("Duplicate NPC section: $($section.Name); first definition retained") }
        else { $sections[$section.Name] = $section }
    }
}
function RegionLines($section,[string[]]$chain=@()) {
    if($chain.Count -ge 32 -or $chain -contains $section.Name){throw "Region inheritance cycle: $($section.Name)"}
    $lines=[Collections.Generic.List[string]]::new()
    foreach($line in $section.Lines){
        if($line -match '^GET\s*=\s*(.+)$'){
            $parent='REGIONSPAWN '+$Matches[1].Trim()
            if(-not $regionSections.ContainsKey($parent)){throw "Missing region parent: $parent"}
            for($i=$lines.Count-1;$i -ge 0;$i--){if($lines[$i] -match '^(NPC|NPCLIST|ITEM|ITEMLIST)='){$lines.RemoveAt($i)}}
            foreach($inherited in (RegionLines $regionSections[$parent] ($chain+$section.Name))){
                if($inherited -notmatch '^(NPC|NPCLIST|ITEM|ITEMLIST|ERAS)='){$lines.Add($inherited)}
            }
        } else {$lines.Add($line)}
    }
    $lines.ToArray()
}
function Number([string]$text) {
    $text=$text.Trim()
    if($text -match '^0x[0-9a-f]+$') { return [Convert]::ToInt64($text.Substring(2),16) }
    if($text -match '^-?[0-9]+$') { return [long]::Parse($text,[Globalization.CultureInfo]::InvariantCulture) }
    throw "Non-numeric spawn field: $text"
}
function NpcFields($section,[string[]]$chain=@()) {
    if($chain.Count -ge 32 -or $chain -contains $section.Name){throw "NPC inheritance cycle: $($section.Name)"}
    $states=[Collections.Generic.List[hashtable]]::new()
    $states.Add(@{_denominator=1L})
    foreach($line in $section.Lines) {
        if($line -notmatch '^([^=]+)=(.*)$'){continue}
        $tag=$Matches[1].Trim();$value=$Matches[2].Trim()
        if($tag -eq 'GET' -or $tag -eq 'GETUO') {
            $parents=@($value -split '[, ]+' | Where-Object {$_})
            $expanded=[Collections.Generic.List[hashtable]]::new()
            foreach($state in $states){foreach($parent in $parents){
                $inherited=if($sections.ContainsKey($parent)){@(NpcFields $sections[$parent] ($chain+$section.Name))}
                    else {$issues.Add("Missing GET $parent in $($section.Name)");@(@{_denominator=1L})}
                foreach($variant in $inherited){
                    $copy=$state.Clone()
                    foreach($key in $variant.Keys){if($key -ne '_denominator'){$copy[$key]=$variant[$key]}}
                    $copy._denominator=$state._denominator*$parents.Count*$variant._denominator
                    if($copy._denominator -gt 65535 -or $expanded.Count -ge 256){throw 'NPC variant budget'}
                    $expanded.Add($copy)
                }
            }}
            $states=$expanded
        } elseif($tag -match '^GET(T2A|UOR|TD|LBR|AOS|SE|ML|SA|HS|TOL)$') { continue }
        elseif($tag -eq 'ID') {
            $bodies=@($value -split '[, ]+' | Where-Object {$_})
            $expanded=[Collections.Generic.List[hashtable]]::new()
            foreach($state in $states){foreach($body in $bodies){
                $copy=$state.Clone();$copy.ID=$body;$copy._denominator=$state._denominator*$bodies.Count
                if($copy._denominator -gt 65535 -or $expanded.Count -ge 256){throw 'NPC body variant budget'}
                $expanded.Add($copy)
            }}
            $states=$expanded
        } else {foreach($state in $states){$state[$tag]=$value}}
    }
    $states.ToArray()
}
function Range($fields,[string]$key,[long]$fallback) {
    if(-not $fields.ContainsKey($key)) { return @($fallback,$fallback) }
    $parts=@($fields[$key] -split '[, ]+' | Where-Object { $_ })
    $one=Number $parts[0]
    $two=if($parts.Count -gt 1){Number $parts[1]}else{$one}
    if($two -lt $one) {
        if($key -eq 'DAMAGE'){$two=$one}
        else {$swap=$one;$one=$two;$two=$swap}
    }
    return @($one,$two)
}
function CdText([string]$text,[int]$limit=96) {
    $text=[regex]::Replace($text,'[^A-Za-z0-9 ''(),._-]',' ').Trim()
    if($text.Length -gt $limit){$text=$text.Substring(0,$limit)}
    return '"' + $text + '"'
}
# UOX3 writes NAME=5123//a bone axeman: an all-digit NAME is a key into the client message dictionary.
$dictionaryPath="$ReferenceRoot/data/dictionaries/dictionary.ENG"
$sourceHashes[$dictionaryPath]=(Get-FileHash -LiteralPath $dictionaryPath -Algorithm SHA256).Hash
$dictionary=@{}
foreach($line in [IO.File]::ReadAllLines($dictionaryPath)){
    if($line -match '^\s*([0-9]+)\s*=(.*)$' -and -not $dictionary.ContainsKey($Matches[1])){$dictionary[$Matches[1]]=$Matches[2].Trim()}
}
# ANIM.IDX holds 12-byte rows (offset, length, extra); a body's first row is
# at body*110 below 200, 22000+(body-200)*65 below 400, else 35000+(body-400)*175.
# An offset of -1 means the client has no animation and draws nothing.
$sourceHashes[$AnimIndex]=(Get-FileHash -LiteralPath $AnimIndex -Algorithm SHA256).Hash
$anim=[IO.File]::ReadAllBytes($AnimIndex)
function Animated([long]$body){
    $row=if($body -lt 200){$body*110}elseif($body -lt 400){22000+($body-200)*65}else{35000+($body-400)*175}
    if($body -lt 0 -or ($row+1)*12 -gt $anim.Length){return $false}
    [BitConverter]::ToInt32($anim,[int]($row*12)) -ne -1 -and [BitConverter]::ToInt32($anim,[int]($row*12+4)) -gt 0
}
# Body.def is the client's own substitution table: <body> {<substitute>, ...} <hue>.
$sourceHashes[$BodyDef]=(Get-FileHash -LiteralPath $BodyDef -Algorithm SHA256).Hash
$substitutes=@{}
foreach($line in [IO.File]::ReadAllLines($BodyDef)){
    if($line -match '^\s*([0-9]+)\s*\{([0-9, ]+)\}\s*([0-9]+)' -and -not $substitutes.ContainsKey([long]$Matches[1])){
        $substitutes[[long]$Matches[1]]=@{bodies=@($Matches[2] -split '[, ]+' | Where-Object {$_} | ForEach-Object {[long]$_});hue=[long]$Matches[3]}
    }
}
function Shown([long]$body,[int]$depth){
    if(Animated $body){return @($body,0L)}
    if($depth -ge 4 -or -not $substitutes.ContainsKey($body)){return @(-1L,0L)}
    foreach($next in $substitutes[$body].bodies){
        $found=Shown $next ($depth+1)
        if($found[0] -ge 0){return @($found[0],$(if($found[1] -gt 0){$found[1]}else{$substitutes[$body].hue}))}
    }
    return @(-1L,0L)
}
$remapped=[Collections.Generic.List[string]]::new()
$creatureMovement=@{}
$creatureVoice=@{}
foreach($creature in (Read-Sections "$ReferenceRoot/data/dfndata/creatures/creatures.dfn")){
    if($creature.Name -notmatch '^CREATURE (.+)$'){continue}
    $body=Number $Matches[1];$attack=0L;$defend=0L;$start=0L
    foreach($line in $creature.Lines){
        if($line -match '^MOVEMENT=(.+)$'){$creatureMovement[$body]=$Matches[1].Trim()}
        elseif($line -match '^SOUND_ATTACK=(.+)$'){$attack=Number $Matches[1]}
        elseif($line -match '^SOUND_DEFEND=(.+)$'){$defend=Number $Matches[1]}
        elseif($line -match '^SOUND_STARTATTACK=(.+)$'){$start=Number $Matches[1]}
    }
    if($attack -lt 0 -or $attack -gt 65535 -or $defend -lt 0 -or $defend -gt 65535 -or $start -lt 0 -or $start -gt 65535){throw "Creature $body sound outside 16 bits"}
    if($attack -gt 0 -or $defend -gt 0 -or $start -gt 0){$creatureVoice[$body]=@($attack,$defend,$start)}
}
$voices=@{}
# carve.dfn ADDITEM=art[,amount]: raw ribs, raw bird, leg of lamb and chicken leg are meat; hides and the
# spined, barbed and horned hides (art 0x1078) are hides. Feathers, scales, fish steaks and body parts are not imported.
$carveMeat=@(0x09F1,0x09B9,0x1609,0x1607);$carveHides=@(0x1078,0xC000,0xC001,0xC002)
$carveTables=@{}
foreach($table in (Read-Sections "$ReferenceRoot/data/dfndata/carve/carve.dfn")){
    if($table.Name -notmatch '^CARVE ([0-9]+)$'){continue}
    $id=[long]$Matches[1];$meat=0L;$hides=0L
    foreach($line in $table.Lines){if($line -match '^ADDITEM=(0x[0-9A-Fa-f]+|[0-9]+)(?:,(.+))?$'){$art=Number $Matches[1];$amount=if($Matches[2]){Number $Matches[2]}else{1L};if($carveMeat -contains $art){$meat+=$amount}elseif($carveHides -contains $art){$hides+=$amount}}}
    $carveTables[$id]=@($meat,$hides)
}
$carves=@{}
$profiles=[Collections.Generic.List[string]]::new()
$profileIds=@{}
function ProfileVariant([string]$key,$f) {
    if(-not $f.ContainsKey('ID')){$issues.Add("NPC without body: $key");return -1}
    try {
        $body=Number $f['ID']; $strength=Range $f 'STR' 10; $dex=Range $f 'DEX' 20
        $hits=if($f.ContainsKey('HPMAX')){Range $f 'HPMAX' 10}else{$strength}
        $damage=Range $f 'DAMAGE' 1; $gold=Range $f 'GOLD' 0; $taming=if($f.ContainsKey('TOTAME')){Number $f['TOTAME']}else{0L}; $skill=Range $f 'WRESTLING' 0; $tactics=Range $f 'TACTICS' 0
        $ai=if($f.ContainsKey('NPCAI')){Number $f['NPCAI']}else{0}
        $skin=Range $f 'SKIN' 0;$hue=$skin[0];$hueHigh=$skin[1]
        $water=if($creatureMovement[$body] -eq 'WATER'){'True'}else{'False'}
        $shown=Shown $body 0
        if($shown[0] -lt 0){throw "body $body has no animation in the 1.25 client and no Body.def substitute with one"}
        if($shown[0] -ne $body){
            $remapped.Add("$key body $body to $($shown[0])")
            if($hue -eq 0 -and $hueHigh -eq 0 -and $shown[1] -gt 0){$hue=$shown[1];$hueHigh=$shown[1]}
        }
        if($body -lt 0 -or $body -gt 16383 -or $strength[0] -lt 0 -or $strength[1] -gt 65535 -or $hits[1] -gt 65535 -or $dex[1] -gt 1000 -or $damage[1] -gt 1000 -or $skill[0] -lt 0 -or $skill[1] -gt 1000 -or $tactics[0] -lt 0 -or $tactics[1] -gt 1000 -or $hue -lt 0 -or $hue -gt 65535 -or $gold[0] -lt 0 -or $gold[1] -gt 65535 -or $taming -lt 0 -or $taming -gt 65535){throw 'profile exceeds current combat budget'}
        $name=if($f.ContainsKey('NAME') -and $f['NAME'] -ne '#'){$f['NAME']}else{$key}
        if($name -match '^[0-9]+$'){
            if(-not $dictionary.ContainsKey($name)){throw "NAME $name is not in $dictionaryPath"}
            $name=$dictionary[$name]
        }
        if($damage[0] -lt 0 -or $hits[0] -lt 0 -or $dex[0] -lt 0){throw 'negative combat stat'}
        $id=$profiles.Count
        if($hueHigh -gt 65535){throw 'hue range exceeds budget'}
        $voice=if($creatureVoice.ContainsKey($body)){$creatureVoice[$body]}elseif($creatureVoice.ContainsKey($shown[0])){$creatureVoice[$shown[0]]}else{$null}
        if($null -ne $voice){
            if(-not $voices.ContainsKey($shown[0])){$voices[$shown[0]]=$voice}
            elseif($voices[$shown[0]][0] -ne $voice[0] -or $voices[$shown[0]][1] -ne $voice[1] -or $voices[$shown[0]][2] -ne $voice[2]){$issues.Add("Sound conflict on drawn body $($shown[0]): $key keeps the first profile's sounds")}
        }
        if($f.ContainsKey('CARVE') -and $f['CARVE'] -notmatch '^(0x[0-9A-Fa-f]+|[0-9]+)$'){$issues.Add("Non-numeric CARVE for $key")}
        elseif($f.ContainsKey('CARVE')){
            $carve=Number $f['CARVE']
            if(-not $carveTables.ContainsKey($carve)){$issues.Add("Missing CARVE $carve for $key")}
            elseif($carveTables[$carve][0] -gt 0 -or $carveTables[$carve][1] -gt 0){
                $parts=$carveTables[$carve]
                if($parts[0] -gt 65535 -or $parts[1] -gt 65535){throw "CARVE $carve exceeds 16 bits"}
                if(-not $carves.ContainsKey($shown[0])){$carves[$shown[0]]=$parts}
                elseif($carves[$shown[0]][0] -ne $parts[0] -or $carves[$shown[0]][1] -ne $parts[1]){$issues.Add("Carve conflict on drawn body $($shown[0]): $key keeps the first profile's parts")}
            }
        }
        $profiles.Add("WsProfile { key = $(CdText $key), name = $(CdText $name 30), body = $($shown[0]), hue = $hue, hue-high = $hueHigh, water = $water, strength-low = $($strength[0]), strength-high = $($strength[1]), dex-low = $($dex[0]), dex-high = $($dex[1]), hits-low = $($hits[0]), hits-high = $($hits[1]), damage-low = $($damage[0]), damage-high = $($damage[1]), skill-low = $($skill[0]), skill-high = $($skill[1]), tactics-low = $($tactics[0]), tactics-high = $($tactics[1]), ai = $ai, gold-low = $($gold[0]), gold-high = $($gold[1]), taming = $taming }")
        return $id
    } catch {$issues.Add("Unsupported NPC $key : $_");return -1}
}
$groups=[Collections.Generic.List[string]]::new()
$groupIds=@{}
function Profile([string]$key) {
    if($profileIds.ContainsKey($key)){return $profileIds[$key]}
    $reference=[pscustomobject]@{Profile=-1;Group=-1}
    if(-not $sections.ContainsKey($key)){$issues.Add("Missing NPC: $key");$profileIds[$key]=$reference;return $reference}
    $variants=@(NpcFields $sections[$key])
    if($variants.Count -eq 1){$reference.Profile=ProfileVariant $key $variants[0]}
    elseif($variants.Count -gt 1){
        $scale=1L
        foreach($variant in $variants){
            $a=$scale;$b=[long]$variant._denominator
            while($b -ne 0){$remainder=$a%$b;$a=$b;$b=$remainder}
            $scale=[long]($scale/$a)*$variant._denominator
            if($scale -gt 65535){throw "NPC variant probability budget: $key"}
        }
        $choices=[Collections.Generic.List[string]]::new()
        for($i=0;$i -lt $variants.Count;$i++){
            $profile=ProfileVariant "$key-$i" $variants[$i]
            $weight=[long]($scale/$variants[$i]._denominator)
            $choices.Add("WsChoice { key = $(CdText "$key-$i"), profile = $profile, group = -1, weight = $weight }")
        }
        $reference.Group=$groups.Count
        $groups.Add("WsGroup { key = $(CdText "template-$key"), choices = [$($choices -join ', ')] }")
    }
    $profileIds[$key]=$reference
    return $reference
}
function Entries([string[]]$lines,[string[]]$chain) {
    if($chain.Count -gt 32){throw 'NPC list nesting exceeds budget'}
    foreach($line in $lines) {
        if($line -match '^NPCLIST\s*=\s*(.+)$') {
            $name=$Matches[1].Trim();$section="NPCLIST $name"
            if($chain -contains $section){throw "NPC list cycle: $section"}
            if($sections.ContainsKey($section)){Entries $sections[$section].Lines ($chain + $section)}
            else {$issues.Add("Missing NPC list: $name")}
            continue
        }
        $weight=1L;$entry=$line
        if($line -match '^([0-9]+)\|(.*)$'){$weight=Number $Matches[1];$entry=$Matches[2].Trim()}
        if($weight -lt 1 -or $weight -gt 65535){throw "NPC choice weight outside budget: $line"}
        if($entry -match '^NPCLIST\s*=\s*(.+)$') {
            $group=Resolve-SpawnGroup $Matches[1].Trim() $chain
            "WsChoice { key = $(CdText $entry), profile = -1, group = $group, weight = $weight }"
        } else {
            $reference=Profile $entry
            "WsChoice { key = $(CdText $entry), profile = $($reference.Profile), group = $($reference.Group), weight = $weight }"
        }
    }
}
function Resolve-SpawnGroup([string]$name,[string[]]$chain=@()) {
    $key="NPCLIST $name"
    if($chain -contains $key){throw "Weighted NPC list cycle: $key"}
    if($groupIds.ContainsKey($name)){return $groupIds[$name]}
    $id=$groups.Count;$groupIds[$name]=$id;$groups.Add('')
    $rows=if($sections.ContainsKey($key)){@(Entries $sections[$key].Lines ($chain+$key))}else{$issues.Add("Missing NPC list: $name");@()}
    $groups[$id]="WsGroup { key = $(CdText $name), choices = [$($rows -join ', ')] }"
    return $id
}
$regions=[Collections.Generic.List[string]]::new()
# Damian, 2026-10-06 (UOAIX-25): the Britain Cemetery north of the forge spawns low-end undead only.
# location_25_light without its lich, source weights kept.
$lowUndead=@('3|skeleton','4|zombie','2|boneaxeman','2|ghoul')
$regionOverrides=@{5505L=$lowUndead;5506L=$lowUndead}
function FieldNumber($fields,[string]$key,[long]$fallback){if($fields.ContainsKey($key)){Number $fields[$key]}else{$fallback}}
$regionInputs=[Collections.Generic.List[object]]::new()
$regionSections=@{}
foreach($file in (Get-ChildItem -LiteralPath "$ReferenceRoot/data/dfndata/spawn/felucca" -Filter '*.dfn' | Where-Object {$_.Name -match '^spawn_felucca_dungeon_|^spawn_felucca_world_(lands|lostlands|forts|graveyards)\.dfn$'} | Sort-Object Name)) {
    foreach($section in (Read-Sections $file.FullName)) {
        if($regionSections.ContainsKey($section.Name)){$issues.Add("Duplicate spawn region: $($section.Name); first definition retained");continue}
        $regionSections[$section.Name]=$section;$regionInputs.Add($section)
    }
}
foreach($section in $regionInputs){
        if($section.Name -notmatch '^REGIONSPAWN ([0-9]+)$'){continue};$id=Number $Matches[1]
        $lines=@(RegionLines $section);$f=@{}
        foreach($line in $lines){if($line -match '^([^=]+)=(.*)$'){$f[$Matches[1].Trim()]=$Matches[2].Trim()}}
        if($f.ContainsKey('ERAS') -and @($f.ERAS -split ',' | ForEach-Object {$_.Trim()}) -notcontains 'UO'){continue}
        if((FieldNumber $f 'WORLD' 0) -ne 0 -or (FieldNumber $f 'MAXNPCS' 0) -le 0){continue}
        $x1=FieldNumber $f 'X1' 0;$x2=FieldNumber $f 'X2' 0;$y1=FieldNumber $f 'Y1' 0;$y2=FieldNumber $f 'Y2' 0
        if($x1 -lt 0 -or $y1 -lt 0 -or $x2 -ge 6144 -or $y2 -ge 4096 -or $x2 -lt $x1 -or $y2 -lt $y1){$issues.Add("Invalid region rectangle: $id");continue}
        $entries=[Collections.Generic.List[string]]::new();$exclusions=[Collections.Generic.List[string]]::new()
        foreach($line in $lines) {
            if($line -match '^NPC=(.+)$'){$entries.Add($Matches[1].Trim())}
            elseif($line -match '^NPCLIST=(.+)$'){$entries.Add($line)}
            elseif($line -match '^EXCLUDEAREA=(.+)$') {
                $coords=@($Matches[1] -split ',' | ForEach-Object {Number $_})
                if($coords.Count -ne 4){throw "Invalid exclusion in region $id"}
                $exclusions.Add("WsRect { x1 = $($coords[0]), y1 = $($coords[1]), x2 = $($coords[2]), y2 = $($coords[3]) }")
            }
        }
        if($regionOverrides.ContainsKey($id)){$entries.Clear();$entries.AddRange([string[]]$regionOverrides[$id])}
        $choices=@(Entries $entries.ToArray() @())
        $group=$groups.Count;$groupName=CdText ("region-"+$id)
        $groups.Add("WsGroup { key = $groupName, choices = [$($choices -join ', ')] }")
        $name=if($f.ContainsKey('NAME')){$f['NAME']}else{"region-$id"}
        $minimum=FieldNumber $f 'MINTIME' 0;$maximum=FieldNumber $f 'MAXTIME' 0
        if($minimum -lt 0 -or $maximum -lt $minimum -or $maximum -gt 1440){$issues.Add("Invalid region timer: $id");continue}
        $outside=if((FieldNumber $f 'ONLYOUTSIDE' 0) -ne 0){'True'}else{'False'}
        $regions.Add("WsRegion { id = $id, name = $(CdText $name), area = WsRect { x1 = $x1, y1 = $y1, x2 = $x2, y2 = $y2 }, maximum = $(FieldNumber $f 'MAXNPCS' 0), batch = $(FieldNumber $f 'CALL' 1), minimum-minutes = $minimum, maximum-minutes = $maximum, group = $group, preferred-height = $(FieldNumber $f 'PREFZ' 18), fixed-height = $(FieldNumber $f 'DEFZ' -129), outside = $outside, exclusions = [$($exclusions -join ', ')] }")
}
foreach($row in $profiles){
    $body=[long][regex]::Match($row,' body = ([0-9]+),').Groups[1].Value
    if(-not (Animated $body)){throw "Spawn body $body has no animation in $AnimIndex; the client would draw nothing"}
}
$text=[Text.StringBuilder]::new()
[void]$text.AppendLine("Chapter: WorldSpawnData")
[void]$text.AppendLine("  cites Uoaix chapter WorldSpawnTypes")
[void]$text.AppendLine("")
[void]$text.AppendLine(" Modified UOX3 Felucca region/NPC data, GPL-2.0-or-later.")
[void]$text.AppendLine(" Copyright 1997, 98 Marcus Rating (Cironian); see the retained UOX3 license.")
[void]$text.AppendLine(" Source and limitations: WorldSpawns.md. License: ports/UOX3-LICENSE.txt.")
function Emit([string]$prefix,[string]$type,$rows) {
    $chunks=[Math]::Max(1,[int][Math]::Ceiling($rows.Count/24.0))
    for($part=0;$part -lt $chunks;$part++){
        [void]$text.AppendLine("  $prefix-$part : List $type -> List $type")
        [void]$text.AppendLine("  $prefix-$part (out) =")
        $last='out'
        for($j=$part*24;$j -lt [Math]::Min($rows.Count,($part+1)*24);$j++){
            $lead=if($j -eq $part*24){'    let'}else{'    in let'}
            [void]$text.AppendLine("$lead r$j = list-push $last ($($rows[$j]))");$last="r$j"
        }
        $lead=if($last -eq 'out'){'    '}else{'    in '}
        $tail=if($part+1 -lt $chunks){"$prefix-$($part+1) $last"}else{$last}
        [void]$text.AppendLine("$lead$tail")
    }
}
Emit 'wsd-profiles' 'WsProfile' $profiles
Emit 'wsd-groups' 'WsGroup' $groups
Emit 'wsd-regions' 'WsRegion' $regions
$revision=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($text.ToString()))).Substring(0,15)
[void]$text.AppendLine("  wsd-catalog : Integer -> WsCatalog")
[void]$text.AppendLine("  wsd-catalog (unused) = WsCatalog { revision = #$revision, profiles = wsd-profiles-0 (__list-with-capacity $($profiles.Count)),")
[void]$text.AppendLine("    groups = wsd-groups-0 (__list-with-capacity $($groups.Count)), regions = wsd-regions-0 (__list-with-capacity $($regions.Count)) }")
[void]$text.AppendLine("");[void]$text.AppendLine("Page 1")
foreach($path in $sourceHashes.Keys){if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $sourceHashes[$path]){throw "Source changed during import: $path"}}
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Output))) | Out-Null
[IO.File]::WriteAllText([IO.Path]::GetFullPath($Output),($text.ToString() -replace '\r?\n',"`r`n"),[Text.UTF8Encoding]::new($false))
# A drawn body's row is 32 bits at body*4, two 16-bit values (kind 0 low, kind 1 high); 0 is none.
function BodyTable([string]$Chapter,[string]$Data,[string]$P,[string]$Read,$Map,[string]$Target){
    if(@($sourceHashes.Keys | ForEach-Object {[IO.Path]::GetFullPath($_)}) -contains [IO.Path]::GetFullPath($Target)){throw "Output $Target is an import source"}
    $bodies=@($Map.Keys | Sort-Object)
    $limit=if($bodies.Count -gt 0){$bodies[-1]+1}else{1}
    $wide=$bodies.Count -gt 0 -and @($Map[$bodies[0]]).Count -gt 2
    $row=if($wide){8}else{4};$poke=if($wide){'poke-qword'}else{'poke-32'}
    $out=[Text.StringBuilder]::new()
    [void]$out.AppendLine("Chapter: $Chapter")
    [void]$out.AppendLine("")
    [void]$out.AppendLine(" Modified UOX3 creature $Data data, GPL-2.0-or-later.")
    [void]$out.AppendLine(" Copyright 1997, 98 Marcus Rating (Cironian); see the retained UOX3 license.")
    [void]$out.AppendLine(" Source and limitations: WorldSpawns.md. License: ports/UOX3-LICENSE.txt.")
    [void]$out.AppendLine("  $P-limit : Integer = $limit")
    [void]$out.AppendLine("  $P-$Read : Integer, Integer, Integer -> Integer")
    [void]$out.AppendLine("  $P-$Read (table) (body) (kind) = if body < 0 | body >= $P-limit then 0 else peek-16 table (body * $row + kind * 2)")
    [void]$out.AppendLine("  $P-clear : Integer, Integer -> Integer")
    [void]$out.AppendLine("  $P-clear (table) (i) = if i == $P-limit then 0 else let z = $poke table (i * $row) 0 in $P-clear table (i + 1)")
    $chunks=[Math]::Max(1,[int][Math]::Ceiling($bodies.Count/24.0))
    for($part=0;$part -lt $chunks;$part++){
        [void]$out.AppendLine("  $P-fill-$part : Integer -> Integer")
        [void]$out.AppendLine("  $P-fill-$part (table) =")
        $lead='    let'
        for($j=$part*24;$j -lt [Math]::Min($bodies.Count,($part+1)*24);$j++){
            $b=$bodies[$j]
            $value=[long]$Map[$b][0] + [long]$Map[$b][1]*65536 + $(if($wide){[long]$Map[$b][2]*4294967296}else{0L})
            [void]$out.AppendLine("$lead r$j = $poke table $($b*$row) $value");$lead='    in let'
        }
        $tail=if($part+1 -lt $chunks){"$P-fill-$($part+1) table"}else{'0'}
        [void]$out.AppendLine($(if($lead -eq '    let'){"    $tail"}else{"    in $tail"}))
    }
    [void]$out.AppendLine("  $P-table : Integer -> Integer")
    [void]$out.AppendLine("  $P-table (unused) = let table = alloc-bytes ($P-limit * $row)")
    [void]$out.AppendLine("    in let cleared = $P-clear table 0")
    [void]$out.AppendLine("    in let filled = $P-fill-0 table in table")
    [void]$out.AppendLine("");[void]$out.AppendLine("Page 1")
    foreach($source in $sourceHashes.Keys){if((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $sourceHashes[$source]){throw "Source changed during import: $source"}}
    [IO.File]::WriteAllText([IO.Path]::GetFullPath($Target),($out.ToString() -replace '\r?\n',"`r`n"),[Text.UTF8Encoding]::new($false))
}
BodyTable 'CreatureSoundData' 'SOUND_ATTACK, SOUND_DEFEND and SOUND_STARTATTACK' 'csd' 'sound' $voices $SoundOutput
BodyTable 'CreatureCarveData' 'CARVE meat (kind 0) and hides (kind 1)' 'cvd' 'carve' $carves $CarveOutput
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Manifest))) | Out-Null
[ordered]@{source=$ReferenceRoot;profiles=$profiles.Count;groups=$groups.Count;regions=$regions.Count;voices=$voices.Count;carves=$carves.Count;remapped=$remapped.ToArray();issues=$issues.ToArray();hashes=$sourceHashes} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $Manifest -Encoding utf8
Write-Output "Wrote $Output; diagnostics in $Manifest"
