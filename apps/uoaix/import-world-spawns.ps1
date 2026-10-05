param(
    [string]$ReferenceRoot = 'D:/Projects/uo-reference/UOX3',
    [string]$Output = "$PSScriptRoot/WorldSpawnData.codex",
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
$creatureMovement=@{}
foreach($creature in (Read-Sections "$ReferenceRoot/data/dfndata/creatures/creatures.dfn")){
    if($creature.Name -notmatch '^CREATURE (.+)$'){continue}
    $body=Number $Matches[1]
    foreach($line in $creature.Lines){if($line -match '^MOVEMENT=(.+)$'){$creatureMovement[$body]=$Matches[1].Trim()}}
}
$profiles=[Collections.Generic.List[string]]::new()
$profileIds=@{}
function ProfileVariant([string]$key,$f) {
    if(-not $f.ContainsKey('ID')){$issues.Add("NPC without body: $key");return -1}
    try {
        $body=Number $f['ID']; $strength=Range $f 'STR' 10; $dex=Range $f 'DEX' 20
        $hits=if($f.ContainsKey('HPMAX')){Range $f 'HPMAX' 10}else{$strength}
        $damage=Range $f 'DAMAGE' 1; $skill=Range $f 'WRESTLING' 0; $tactics=Range $f 'TACTICS' 0
        $ai=if($f.ContainsKey('NPCAI')){Number $f['NPCAI']}else{0}
        $skin=Range $f 'SKIN' 0;$hue=$skin[0];$hueHigh=$skin[1]
        $water=if($creatureMovement[$body] -eq 'WATER'){'True'}else{'False'}
        if($body -lt 0 -or $body -gt 16383 -or $strength[0] -lt 0 -or $strength[1] -gt 65535 -or $hits[1] -gt 65535 -or $dex[1] -gt 1000 -or $damage[1] -gt 1000 -or $skill[0] -lt 0 -or $skill[1] -gt 1000 -or $tactics[0] -lt 0 -or $tactics[1] -gt 1000 -or $hue -lt 0 -or $hue -gt 65535){throw 'profile exceeds current combat budget'}
        $name=if($f.ContainsKey('NAME') -and $f['NAME'] -ne '#'){$f['NAME']}else{$key}
        if($damage[0] -lt 0 -or $hits[0] -lt 0 -or $dex[0] -lt 0){throw 'negative combat stat'}
        $id=$profiles.Count
        if($hueHigh -gt 65535){throw 'hue range exceeds budget'}
        $profiles.Add("WsProfile { key = $(CdText $key), name = $(CdText $name 30), body = $body, hue = $hue, hue-high = $hueHigh, water = $water, strength-low = $($strength[0]), strength-high = $($strength[1]), dex-low = $($dex[0]), dex-high = $($dex[1]), hits-low = $($hits[0]), hits-high = $($hits[1]), damage-low = $($damage[0]), damage-high = $($damage[1]), skill-low = $($skill[0]), skill-high = $($skill[1]), tactics-low = $($tactics[0]), tactics-high = $($tactics[1]), ai = $ai }")
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
        $choices=@(Entries $entries.ToArray() @())
        $group=$groups.Count;$groupName=CdText ("region-"+$id)
        $groups.Add("WsGroup { key = $groupName, choices = [$($choices -join ', ')] }")
        $name=if($f.ContainsKey('NAME')){$f['NAME']}else{"region-$id"}
        $minimum=FieldNumber $f 'MINTIME' 0;$maximum=FieldNumber $f 'MAXTIME' 0
        if($minimum -lt 0 -or $maximum -lt $minimum -or $maximum -gt 1440){$issues.Add("Invalid region timer: $id");continue}
        $outside=if((FieldNumber $f 'ONLYOUTSIDE' 0) -ne 0){'True'}else{'False'}
        $regions.Add("WsRegion { id = $id, name = $(CdText $name), area = WsRect { x1 = $x1, y1 = $y1, x2 = $x2, y2 = $y2 }, maximum = $(FieldNumber $f 'MAXNPCS' 0), batch = $(FieldNumber $f 'CALL' 1), minimum-minutes = $minimum, maximum-minutes = $maximum, group = $group, preferred-height = $(FieldNumber $f 'PREFZ' 18), fixed-height = $(FieldNumber $f 'DEFZ' -129), outside = $outside, exclusions = [$($exclusions -join ', ')] }")
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
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Manifest))) | Out-Null
[ordered]@{source=$ReferenceRoot;profiles=$profiles.Count;groups=$groups.Count;regions=$regions.Count;issues=$issues.ToArray();hashes=$sourceHashes} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $Manifest -Encoding utf8
Write-Output "Wrote $Output; diagnostics in $Manifest"
