param(
    [string]$ReferenceRoot = 'D:/Projects/uo-reference/UOX3',
    [string]$AnimIndex = 'D:/Projects/uoaix-client/ANIM.IDX',
    [string]$BodyDef = 'D:/OldProjects/Projects/UO/UOCli_Official/Body.def',
    [string]$Output = "$PSScriptRoot/WorldSpawnData.codex",
    [string]$SoundOutput = "$PSScriptRoot/CreatureSoundData.codex",
    [string]$CarveOutput = "$PSScriptRoot/CreatureCarveData.codex",
    [string]$IntOutput = "$PSScriptRoot/WorldSpawnIntData.codex",
    [string]$OutfitOutput = "$PSScriptRoot/WorldSpawnOutfitData.codex",
    [string]$TileData = 'D:/Projects/uoaix-client/TILEDATA.MUL',
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
# RANDOMCOLOR n is a hue list; 0x8000 marks a partial hue and is not part of the HUES.MUL index (1..3000 in 1.25).
$colorLists=@{}
foreach($list in (Read-Sections "$ReferenceRoot/data/dfndata/colors/colors.dfn")){
    if($list.Name -notmatch '^RANDOMCOLOR ([0-9]+)$'){continue}
    $listId=[long]$Matches[1]
    $colorLists[$listId]=@($list.Lines | Where-Object {$_ -match '^(0x[0-9A-Fa-f]+|[0-9]+)$'} | ForEach-Object {(Number $_) -band 0x7FFF})
}
function HueList([string]$text,[string]$key){
    $n=Number $text
    if(-not $colorLists.ContainsKey($n) -or $colorLists[$n].Count -eq 0){$issues.Add("Missing RANDOMCOLOR $n for $key");return $null}
    $bad=@($colorLists[$n] | Where-Object {$_ -lt 1 -or $_ -gt 3000})
    if($bad.Count -gt 0){$issues.Add("RANDOMCOLOR $n for $key holds hues outside the 1.25 HUES.MUL");return $null}
    return ,$colorLists[$n]
}
# ITEMLIST n rows are [weight|]art or blank; the text after a section's opening brace is its title.
$itemLists=@{}
foreach($list in (Read-Sections "$ReferenceRoot/data/dfndata/items/itemlists/itemlists.dfn")){
    if($list.Name -notmatch '^ITEMLIST ([0-9]+)$'){continue}
    $listId=[long]$Matches[1]
    $rows=[Collections.Generic.List[object]]::new()
    for($i=0;$i -lt $list.Lines.Count;$i++){
        if($list.Lines[$i] -match '^(?:([0-9]+)\|)?([A-Za-z0-9_-]+)$'){$rows.Add(@($(if($Matches[1]){[long]$Matches[1]}else{1L}),$Matches[2]))}
        elseif($i -gt 0){$issues.Add("Unparsed ITEMLIST $listId row: $($list.Lines[$i])")}
    }
    $itemLists[$listId]=$rows
}
# An item section names its art by ID, or inherits it from its first GET parent; TILEDATA.MUL item entries (37 bytes,
# after 512 land blocks of 4 + 32 x 26) carry the wearable layer at byte 5.
$itemSections=@{}
foreach($file in (Get-ChildItem -LiteralPath "$ReferenceRoot/data/dfndata/items" -Recurse -Filter '*.dfn' | Sort-Object FullName)){
    foreach($item in (Read-Sections $file.FullName)){
        $name=$item.Name.ToLowerInvariant()
        if(-not $itemSections.ContainsKey($name)){$itemSections[$name]=$item}
    }
}
$sourceHashes[$TileData]=(Get-FileHash -LiteralPath $TileData -Algorithm SHA256).Hash
$tiles=[IO.File]::ReadAllBytes($TileData)
function WornLayer([long]$art){
    $at=512*(4+32*26)+[Math]::Floor($art/32)*(4+32*37)+4+($art%32)*37
    if($art -le 0 -or $at+37 -gt $tiles.Length){return 0}
    return [int]$tiles[$at+5]
}
function ArtOf([string]$token,[int]$depth=0){
    $lower=$token.ToLowerInvariant()
    if($depth -lt 8 -and $itemSections.ContainsKey($lower)){
        $parent=$null
        foreach($line in $itemSections[$lower].Lines){
            if($line -match '^(?i:ID)\s*=\s*(0x[0-9A-Fa-f]+|[0-9]+)$'){return Number $Matches[1]}
            if($null -eq $parent -and $line -match '^(?i:GET)\s*=\s*([^, ]+)'){$parent=$Matches[1]}
        }
        if($null -ne $parent){return ArtOf $parent ($depth+1)}
    }
    if($token -match '^(0x[0-9A-Fa-f]+|[0-9]+)$'){return Number $token}
    return 0L
}
# EQUIPITEM lines in section order, a GET parent's first; COLORLIST and HAIRCOLOR dye the item before them and
# COLORMATCHHAIR gives it the hair's hue.
function OutfitLines($section,[string[]]$chain=@()){
    if($chain.Count -ge 32 -or $chain -contains $section.Name){throw "NPC inheritance cycle: $($section.Name)"}
    foreach($line in $section.Lines){
        if($line -match '^GET\s*=\s*(.+)$'){foreach($parent in @($Matches[1] -split '[, ]+' | Where-Object {$_})){if($sections.ContainsKey($parent)){OutfitLines $sections[$parent] ($chain+$section.Name)}}}
        elseif($line -match '^(EQUIPITEM|COLORLIST|HAIRCOLOR)\s*=\s*(.+)$' -or $line -match '^(COLORMATCHHAIR)$'){$line}
    }
}
function OutfitSlots([string]$key,$section){
    $slots=[Collections.Generic.List[hashtable]]::new();$hair=-1
    foreach($line in @(OutfitLines $section)){
        if($line -match '^EQUIPITEM\s*=\s*(.+)$'){
            $token=$Matches[1].Trim();$choices=[Collections.Generic.List[object]]::new()
            $rows=[Collections.Generic.List[object]]::new()
            if($token -notmatch '^listobject([0-9]+)$'){$rows.Add(@(1L,$token))}
            elseif($itemLists.ContainsKey([long]$Matches[1])){$rows.AddRange($itemLists[[long]$Matches[1]])}
            else{$issues.Add("Missing ITEMLIST for $key : $token")}
            foreach($row in $rows){
                if($row[1] -eq 'blank'){$choices.Add(@($row[0],0L,0));continue}
                $art=ArtOf $row[1];$layer=if($art -gt 0){WornLayer $art}else{0}
                if($layer -lt 1 -or $layer -gt 25 -or $layer -eq 21){$issues.Add("Outfit art $($row[1]) of $key wears on no layer in $TileData");$choices.Add(@($row[0],0L,0))}
                else {$choices.Add(@($row[0],$art,$layer))}
            }
            if($choices.Count -gt 0){$slots.Add(@{choices=$choices.ToArray();hues=$null;match=-1})}
        } elseif($slots.Count -gt 0 -and $line -match '^(COLORLIST|HAIRCOLOR)\s*=\s*(.+)$'){
            $slots[$slots.Count-1].hues=HueList $Matches[2] $key
            if($Matches[1] -eq 'HAIRCOLOR'){$hair=$slots.Count-1}
        } elseif($slots.Count -gt 0 -and $line -eq 'COLORMATCHHAIR' -and $hair -ge 0){$slots[$slots.Count-1].match=$hair}
    }
    return ,$slots.ToArray()
}
$outfits=[ordered]@{}
$notorieties=[Collections.Generic.List[long]]::new()
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
$ints=[Collections.Generic.List[long]]::new()
$profileIds=@{}
function ProfileVariant([string]$key,$f,$section) {
    if(-not $f.ContainsKey('ID')){$issues.Add("NPC without body: $key");return -1}
    try {
        $body=Number $f['ID']; $strength=Range $f 'STR' 10; $dex=Range $f 'DEX' 20
        $hits=if($f.ContainsKey('HPMAX')){Range $f 'HPMAX' 10}else{$strength}
        $damage=Range $f 'DAMAGE' 1; $taming=if($f.ContainsKey('TOTAME')){Number $f['TOTAME']}else{0L}; $skill=Range $f 'WRESTLING' 0; $tactics=Range $f 'TACTICS' 0
        $ai=if($f.ContainsKey('NPCAI')){Number $f['NPCAI']}else{0}
        $skin=Range $f 'SKIN' 0;$hue=$skin[0];$hueHigh=$skin[1]
        if(-not $f.ContainsKey('SKIN') -and $f.ContainsKey('SKINLIST') -and ($body -eq 400 -or $body -eq 401)){
            $skins=HueList $f['SKINLIST'] $key
            if($null -ne $skins){$hue=($skins | Measure-Object -Minimum).Minimum;$hueHigh=($skins | Measure-Object -Maximum).Maximum}
        }
        # A UOX3 or ServUO hue with bit 0x4000 (spectre 0x6677, ServUO 0x4001) is the translucent class: the server draws
        # it with the 0x80 status flag over the body's own colours (cbp-hue), since the 1.25 HUES.MUL holds no such index.
        if((($hue -bor $hueHigh) -band 0x4000) -ne 0){$hue=0x4000;$hueHigh=0x4000}
        $water=if($creatureMovement[$body] -eq 'WATER'){'True'}else{'False'}
        $shown=Shown $body 0
        if($shown[0] -lt 0){throw "body $body has no animation in the 1.25 client and no Body.def substitute with one"}
        if($shown[0] -ne $body){
            $remapped.Add("$key body $body to $($shown[0])")
            if($hue -eq 0 -and $hueHigh -eq 0 -and $shown[1] -gt 0){$hue=$shown[1];$hueHigh=$shown[1]}
        }
        if($body -lt 0 -or $body -gt 16383 -or $strength[0] -lt 0 -or $strength[1] -gt 65535 -or $hits[1] -gt 65535 -or $dex[1] -gt 1000 -or $damage[1] -gt 1000 -or $skill[0] -lt 0 -or $skill[1] -gt 1000 -or $tactics[0] -lt 0 -or $tactics[1] -gt 1000 -or $hue -lt 0 -or $hue -gt 65535 -or $taming -lt 0 -or $taming -gt 65535){throw 'profile exceeds current combat budget'}
        # A human drawn from a NAMELIST keeps only its TITLE here ("the Brigand"); the composite prefixes a first name
        # by serial when it dresses the spawn (cg-dress-spawns).
        $name=if($f.ContainsKey('NAME') -and $f['NAME'] -ne '#'){$f['NAME']}
            elseif($f.ContainsKey('NAMELIST') -and $f.ContainsKey('TITLE')){$f['TITLE']}
            elseif($f.ContainsKey('NAMELIST') -and ($body -eq 400 -or $body -eq 401)){''}
            else{$key}
        if($name -match '^[0-9]+$'){
            if(-not $dictionary.ContainsKey($name)){throw "NAME $name is not in $dictionaryPath"}
            $name=$dictionary[$name]
        }
        if($f.ContainsKey('NAMELIST') -and -not $f.ContainsKey('NAME') -and ($body -eq 400 -or $body -eq 401) -and $name -ne '' -and $name -notmatch '^the '){$issues.Add("NAMELIST profile $key has a title not beginning 'the ': $name")}
        $flag=if($f.ContainsKey('FLAG')){$f['FLAG'].ToUpperInvariant()}else{'NEUTRAL'}
        $notoriety=if($flag -eq 'EVIL'){6L}elseif($flag -eq 'INNOCENT' -or $flag -eq 'GOOD'){1L}elseif($flag -eq 'NEUTRAL'){3L}else{throw "FLAG $flag"}
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
        $wit=Range $f 'INT' 10;$ints.Add([long][Math]::Floor(($wit[0]+$wit[1])/2));$notorieties.Add($notoriety)
        if($shown[0] -eq 400 -or $shown[0] -eq 401){
            try {$slots=OutfitSlots $key $section;if($slots.Count -gt 0){$outfits[[string]$id]=$slots}} catch {$issues.Add("Outfit of $key not imported: $_")}
        }
        $profiles.Add("WsProfile { key = $(CdText $key), name = $(CdText $name 30), body = $($shown[0]), hue = $hue, hue-high = $hueHigh, water = $water, strength-low = $($strength[0]), strength-high = $($strength[1]), dex-low = $($dex[0]), dex-high = $($dex[1]), hits-low = $($hits[0]), hits-high = $($hits[1]), damage-low = $($damage[0]), damage-high = $($damage[1]), skill-low = $($skill[0]), skill-high = $($skill[1]), tactics-low = $($tactics[0]), tactics-high = $($tactics[1]), ai = $ai, taming = $taming }")
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
    if($variants.Count -eq 1){$reference.Profile=ProfileVariant $key $variants[0] $sections[$key]}
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
            $profile=ProfileVariant "$key-$i" $variants[$i] $sections[$key]
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
# Root, 2026-10-09 (UOAIX-218): nothing spawns that the 1.25 client cannot draw, so Wrong's two levels hold undead
# prisoners instead of the source's juka (LBR, body 764-766, drawn by the client only as a man).
$prisoners=@('4|skeleton','4|zombie','2|ghoul','1|lich')
$regionOverrides=@{5505L=$lowUndead;5506L=$lowUndead;3850L=$prisoners;3851L=$prisoners}
# Damian, 2026-10-09 (UOAIX-218): "the swamps on the mainland have lizardman and snakes, the ice island has polar bears
# and walrus and arctic wolves, fire island should have demon and dragon spawn on the temple". Added to the source's own
# entries (the existing data is the base). UOX3 has no arctic wolf; its white wolf is the snow wolf. No source names a
# Fire Island temple; the Shrine of Humility (4270,3693) lies in Fire Isle Jungle 6.
$swamp=@('4|lizardman','2|spearlizardman','2|macelizardman','3|snake','1|giantserpent')
$ice=@('25|polarbear','25|walrus','25|whitewolf')
$fireTemple=@('15|daemon','8|dragon','15|drake')
# Root, 2026-10-09 (UOAIX-218): a themed region is as dense as the densest wild regions, so a player crossing it meets the
# theme every few screens: 2.4 creatures a thousand tiles, the 90th percentile of the source's regions of 5000 tiles or
# more (dungeon levels lead; the Oasis holds 3.7). The source's own maximum stands where it is higher.
$themeDensity=2.4
# Damian, 2026-10-10: "we need orc lords and orc mages at the nearby orc camp" (the palisade at 622..646, 1473..1497 is
# UOX3's Location 8 Medium orc fort).
$orcCamp=@('20|orclord','20|orcmage')
$regionThemes=@{5660L=$swamp;5850L=$swamp;5690L=$ice;5791L=$ice;5792L=$ice;5793L=$ice;5794L=$ice;5795L=$ice;5796L=$ice;5797L=$ice;5798L=$ice;5873L=$fireTemple;5511L=$orcCamp}
function FieldNumber($fields,[string]$key,[long]$fallback){if($fields.ContainsKey($key)){Number $fields[$key]}else{$fallback}}
$regionInputs=[Collections.Generic.List[object]]::new()
$regionSections=@{}
foreach($file in (Get-ChildItem -LiteralPath "$ReferenceRoot/data/dfndata/spawn/felucca" -Filter '*.dfn' | Where-Object {$_.Name -match '^spawn_felucca_dungeon_|^spawn_felucca_world_(lands|lostlands|forts|graveyards)\.dfn$'} | Sort-Object Name)) {
    foreach($section in (Read-Sections $file.FullName)) {
        if($regionSections.ContainsKey($section.Name)){$issues.Add("Duplicate spawn region: $($section.Name); first definition retained");continue}
        $regionSections[$section.Name]=$section;$regionInputs.Add($section)
    }
}
# UOAIX-217 (root, 2026-10-09): a city's town spawns that are not shops (the shops come from the city's shop table):
# Moonglow's thief guildmaster and farmhouses; Yew's sheep pens, fishers, farms and prison. Wind's town file is shops only.
$townRegions=@{'spawn_felucca_town_moonglow.dfn'=@(616,621,622,624,625,626,627);'spawn_felucca_town_yew.dfn'=@(1308,1311,1312,1313,1315,1326,1327,1328,1331,1332,1333,1334,1335,1338,1339)}
foreach($town in $townRegions.Keys | Sort-Object){
    $wanted=$townRegions[$town]
    foreach($section in (Read-Sections "$ReferenceRoot/data/dfndata/spawn/felucca/$town")) {
        if($section.Name -notmatch '^REGIONSPAWN ([0-9]+)$' -or $wanted -notcontains [int]$Matches[1]){continue}
        if($regionSections.ContainsKey($section.Name)){$issues.Add("Duplicate spawn region: $($section.Name); first definition retained");continue}
        $regionSections[$section.Name]=$section;$regionInputs.Add($section)
    }
}
# Regions no reference holds, authored in UOX3's own region form (Damian, 2026-10-10, standing at each). Bounds come from
# the 1.25 MAP0 and STATICS0: the peninsula's land east of Trinsic (11071 land tiles, 8 a thousand for "very dense"),
# the ruin's walls, the labyrinth's outer walls (two homes of one daemon each), the gravestones of the three graveyards
# no reference spawns (the low undead of Britain's), and Nujel'm's island under the open sky.
$authored=@(
    @(9401,'Trinsic Peninsula - Deer and Sheep',2060,2624,2172,2858,89,16,1,2,@('1|hart','1|hind','2|sheep'),@('2060,2742,2087,2843','2060,2844,2107,2858'),0),
    @(9402,'Lich Ruin',842,1536,860,1551,3,3,30,40,@('lich'),@(),0),
    @(9403,'Labyrinth West - Daemon',1033,2157,1145,2299,1,1,30,40,@('daemon'),@(),0),
    @(9404,'Labyrinth East - Daemon',1146,2157,1258,2299,1,1,30,40,@('daemon'),@(),0),
    @(9405,'Nujelm Cemetery',3517,1142,3527,1162,4,2,18,22,$lowUndead,@(),0),
    @(9406,'Trinsic West Cemetery',1467,2501,1478,2514,4,2,18,22,$lowUndead,@(),0),
    @(9407,'Trinsic North Cemetery',1820,2411,1839,2427,6,3,18,22,$lowUndead,@(),0),
    @(9408,'Nujelm Island - Scorpions',3480,1040,3832,1428,24,4,5,10,@('scorpion'),@(),1))
foreach($a in $authored){
    $lines=[Collections.Generic.List[string]]::new()
    $lines.AddRange([string[]]@("NAME=$($a[1])","X1=$($a[2])","Y1=$($a[3])","X2=$($a[4])","Y2=$($a[5])","MAXNPCS=$($a[6])","CALL=$($a[7])","MINTIME=$($a[8])","MAXTIME=$($a[9])","WORLD=0","ONLYOUTSIDE=$($a[12])"))
    foreach($n in $a[10]){$lines.Add("NPC=$n")}
    foreach($e in $a[11]){$lines.Add("EXCLUDEAREA=$e")}
    $name="REGIONSPAWN $($a[0])"
    if($regionSections.ContainsKey($name)){throw "Authored region $($a[0]) collides with a reference region"}
    $section=[pscustomobject]@{Name=$name;Lines=$lines.ToArray();File='authored'}
    $regionSections[$name]=$section;$regionInputs.Add($section)
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
        if($regionThemes.ContainsKey($id)){$entries.AddRange([string[]]$regionThemes[$id])}
        $choices=@(Entries $entries.ToArray() @())
        $group=$groups.Count;$groupName=CdText ("region-"+$id)
        $groups.Add("WsGroup { key = $groupName, choices = [$($choices -join ', ')] }")
        $name=if($f.ContainsKey('NAME')){$f['NAME']}else{"region-$id"}
        $minimum=FieldNumber $f 'MINTIME' 0;$maximum=FieldNumber $f 'MAXTIME' 0
        if($minimum -lt 0 -or $maximum -lt $minimum -or $maximum -gt 1440){$issues.Add("Invalid region timer: $id");continue}
        $outside=if((FieldNumber $f 'ONLYOUTSIDE' 0) -ne 0){'True'}else{'False'}
        $most=FieldNumber $f 'MAXNPCS' 0
        if($regionThemes.ContainsKey($id)){$most=[Math]::Max($most,[long][Math]::Ceiling(($x2-$x1+1)*($y2-$y1+1)*$themeDensity/1000))}
        $regions.Add("WsRegion { id = $id, name = $(CdText $name), area = WsRect { x1 = $x1, y1 = $y1, x2 = $x2, y2 = $y2 }, maximum = $most, batch = $(FieldNumber $f 'CALL' 1), minimum-minutes = $minimum, maximum-minutes = $maximum, group = $group, preferred-height = $(FieldNumber $f 'PREFZ' 18), fixed-height = $(FieldNumber $f 'DEFZ' -129), outside = $outside, exclusions = [$($exclusions -join ', ')] }")
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
    if($P -eq 'cvd'){[void]$out.AppendLine(" UOX3 carves by the NPC's section, which shearing (0xCF to 0xDF, sheepshearing.js) does not change.")}
    [void]$out.AppendLine("  $P-$Read : Integer, Integer, Integer -> Integer")
    if($P -eq 'cvd'){
        [void]$out.AppendLine("  $P-$Read (table) (body) (kind) = if body == #00DF then $P-$Read table #00CF kind")
        [void]$out.AppendLine("    else if body < 0 | body >= $P-limit then 0 else peek-16 table (body * $row + kind * 2)")
    } else {[void]$out.AppendLine("  $P-$Read (table) (body) (kind) = if body < 0 | body >= $P-limit then 0 else peek-16 table (body * $row + kind * 2)")}
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
# A profile's INT (the midpoint of UOX3's range, 10 when the section names none) by catalog profile index, as a
# balanced tree of comparisons so a lookup allocates nothing; kept out of WorldSpawnData so the catalog revision holds.
function IntTree($values,[int]$lo,[int]$hi){
    if($lo -eq $hi){return "$($values[$lo])"}
    $mid=[int][Math]::Floor(($lo+$hi)/2)
    return "(if i <= $mid then $(IntTree $values $lo $mid) else $(IntTree $values ($mid+1) $hi))"
}
$io=[Text.StringBuilder]::new()
[void]$io.AppendLine('Chapter: WorldSpawnIntData');[void]$io.AppendLine('')
[void]$io.AppendLine(' Modified UOX3 creature INT data, GPL-2.0-or-later.')
[void]$io.AppendLine(' Copyright 1997, 98 Marcus Rating (Cironian); see the retained UOX3 license.')
[void]$io.AppendLine(' Source and limitations: WorldSpawns.md. License: ports/UOX3-LICENSE.txt.')
[void]$io.AppendLine("  wsi-count : Integer = $($ints.Count)")
[void]$io.AppendLine('  wsi-int : Integer -> Integer')
[void]$io.AppendLine("  wsi-int (i) = if i < 0 | i >= wsi-count then 10 else $(if($ints.Count -gt 0){IntTree $ints 0 ($ints.Count-1)}else{'10'})")
[void]$io.AppendLine(' A profile''s 0x78 notoriety from its UOX3 FLAG: Evil 6 (murderer), Innocent and Good 1, Neutral or none 3.')
[void]$io.AppendLine('  wsi-notoriety : Integer -> Integer')
[void]$io.AppendLine("  wsi-notoriety (i) = if i < 0 | i >= wsi-count then 3 else $(if($notorieties.Count -gt 0){IntTree $notorieties 0 ($notorieties.Count-1)}else{'3'})")
[void]$io.AppendLine('');[void]$io.AppendLine('Page 1')
[IO.File]::WriteAllText([IO.Path]::GetFullPath($IntOutput),($io.ToString() -replace '\r?\n',"`r`n"),[Text.UTF8Encoding]::new($false))
# A human profile's outfit, one row per EQUIPITEM: an ITEMLIST choice drawn by weight and a dye from its colour list,
# both fixed by the spawn's serial (cst-pick, salts 100 + slot and 200 + slot); a blank choice wears nothing.
function HueExpr($slots,[int]$k){
    $slot=$slots[$k]
    if($slot.match -ge 0){$slot=$slots[$slot.match];$k=[int]$slots[$k].match}
    $hues=$slot.hues
    if($null -eq $hues){return '0'}
    if($hues.Count -eq 1){return "$($hues[0])"}
    $low=($hues | Measure-Object -Minimum).Minimum;$high=($hues | Measure-Object -Maximum).Maximum
    if($high-$low+1 -eq $hues.Count){return "$low + cst-pick serial $(200+$k) $($hues.Count)"}
    return "list-at [$($hues -join ', ')] (cst-pick serial $(200+$k) $($hues.Count))"
}
$wo=[Text.StringBuilder]::new()
[void]$wo.AppendLine('Chapter: WorldSpawnOutfitData')
[void]$wo.AppendLine('  cites Uoaix chapter CharacterStylist');[void]$wo.AppendLine('')
[void]$wo.AppendLine(' Modified UOX3 NPC outfit data, GPL-2.0-or-later.')
[void]$wo.AppendLine(' Copyright 1997, 98 Marcus Rating (Cironian); see the retained UOX3 license.')
[void]$wo.AppendLine(' Source and limitations: WorldSpawns.md. License: ports/UOX3-LICENSE.txt.')
[void]$wo.AppendLine('  wso-keep : List CstRow, Integer, List CstRow -> List CstRow')
[void]$wo.AppendLine('  wso-keep (rows) (i) (out) = if i == list-length rows then out')
[void]$wo.AppendLine('    else let row = list-at rows i in wso-keep rows (i + 1) (if row.graphic == 0 then out else list-push out row)')
$dispatch=[Collections.Generic.List[string]]::new()
foreach($id in $outfits.Keys){
    $slots=$outfits[$id];$calls=[Collections.Generic.List[string]]::new()
    for($k=0;$k -lt $slots.Count;$k++){
        $choices=$slots[$k].choices;$total=0L;foreach($c in $choices){$total+=$c[0]}
        if($total -lt 1 -or $total -gt 65535){throw "Outfit weight outside budget: profile $id slot $k"}
        $body="cst-row 0 0 0";$at=$total
        for($j=$choices.Count-1;$j -ge 0;$j--){
            $at-=$choices[$j][0];$row=if($choices[$j][1] -eq 0){'cst-row 0 0 0'}else{"cst-row $($choices[$j][1]) hue $($choices[$j][2])"}
            $body=if($j -eq $choices.Count-1){$row}else{"if c < $($at+$choices[$j][0]) then $row else $body"}
        }
        [void]$wo.AppendLine("  wso-p$id-$k : Integer -> CstRow")
        [void]$wo.AppendLine("  wso-p$id-$k (serial) = let c = cst-pick serial $(100+$k) $total in let hue = $(HueExpr $slots $k) in $body")
        $calls.Add("wso-p$id-$k serial")
    }
    [void]$wo.AppendLine("  wso-p$id : Integer -> List CstRow")
    [void]$wo.AppendLine("  wso-p$id (serial) = wso-keep [$($calls -join ', ')] 0 []")
    $dispatch.Add($id)
}
[void]$wo.AppendLine('  wso-rows : Integer, Integer -> List CstRow')
$chain='[]';for($d=$dispatch.Count-1;$d -ge 0;$d--){$chain="if profile == $($dispatch[$d]) then wso-p$($dispatch[$d]) serial else $chain"}
[void]$wo.AppendLine("  wso-rows (profile) (serial) = $chain")
[void]$wo.AppendLine('');[void]$wo.AppendLine('Page 1')
[IO.File]::WriteAllText([IO.Path]::GetFullPath($OutfitOutput),($wo.ToString() -replace '\r?\n',"`r`n"),[Text.UTF8Encoding]::new($false))
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Manifest))) | Out-Null
[ordered]@{source=$ReferenceRoot;profiles=$profiles.Count;groups=$groups.Count;regions=$regions.Count;voices=$voices.Count;carves=$carves.Count;remapped=$remapped.ToArray();issues=$issues.ToArray();hashes=$sourceHashes} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $Manifest -Encoding utf8
Write-Output "Wrote $Output; diagnostics in $Manifest"
