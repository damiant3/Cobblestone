param(
    [string]$ReferenceRoot = 'D:/Projects/uo-reference/UOX3',
    [string]$Output = "$PSScriptRoot/GotoPlaceData.codex"
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# A town's spawn row is the nearest monster region within 200 tiles of its first place; an island with none gets none.
# Damian, 2026-10-06 (UOAIX-23): "[goto britbank" style names; the first place of a kind is unnumbered,
# later ones are ordinal from 2 (britbank, britbank2).
$towns = @(
    @{ town='Trinsic'; prefix='trin'; file='trinsic'; location='Trinsic'; gate=5 },
    @{ town='Minoc'; prefix='minoc'; file='minoc'; location='Minoc'; gate=4 },
    @{ town='Vesper'; prefix='vesper'; file='vesper'; location='Vesper'; gate=-1 },
    @{ town='Yew'; prefix='yew'; file='yew'; location='Yew'; gate=3 },
    @{ town='Moonglow'; prefix='moonglow'; file='moonglow'; location='Moonglow'; gate=0 },
    @{ town='Magincia'; prefix='magincia'; file='magincia'; location='Magincia (original)'; gate=7 },
    @{ town='Jhelom'; prefix='jhelom'; file='jhelom'; location='Jhelom'; gate=2 },
    @{ town='Skara Brae'; prefix='skara'; file='skara_brae'; location='Skara Brae'; gate=6 },
    @{ town='Cove'; prefix='cove'; file='cove'; location='Cove'; gate=-1 },
    @{ town='Buccaneer''s Den'; prefix='buc'; file='buc_den'; location='Buccaneer''s Den'; gate=-1 },
    @{ town='Nujel''m'; prefix='nujelm'; file='nujelm'; location='Nujel''m'; gate=-1 },
    @{ town='Ocllo'; prefix='ocllo'; file='ocllo'; location='Ocllo'; gate=-1 },
    @{ town='Serpent''s Hold'; prefix='serp'; file='serpents_hold'; location='Serpent''s Hold'; gate=-1 },
    @{ town='Papua'; prefix='papua'; file='papua'; location='Papua'; gate=-1 },
    @{ town='Delucia'; prefix='delucia'; file='delucia'; location='Delucia'; gate=-1 },
    @{ town='Wind'; prefix='wind'; file='wind'; location='Wind'; gate=-1 }
)
# Britain's rows are hand-placed (CompositePaging history): location.dfn 27 and 28, BritainCatalog shop centres,
# GameMoongates' gate, the stand south of CompositeLineup's row, the throne (cpr-throne-x/y) and the Royal Minter's tile.
$britain = @(
    @('britbank',1436,1693), @('britbank2',1656,1614), @('britmoon',1336,1997), @('britlineup',1437,1696),
    @('brithealer',1473,1611), @('britsmith',1418,1547), @('britinn',1497,1616), @('britfarm',1229,1575),
    @('throne',1326,1624), @('mint',1334,1603)
)

$sourceHashes = [ordered]@{}
function Read-Text([string]$path) { $sourceHashes[$path] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash; [IO.File]::ReadAllText($path) }

$locationText = Read-Text "$ReferenceRoot/data/dfndata/location/location.dfn"
$locations = foreach ($m in [regex]::Matches($locationText, '(?ms)^\[LOCATION (\d+)\]\s*\{\s*//\s*([^\r\n]*)\s*X=(\d+)\s*Y=(\d+)')) {
    [pscustomobject]@{ Id=[int]$m.Groups[1].Value; Name=$m.Groups[2].Value.Trim(); X=[int]$m.Groups[3].Value; Y=[int]$m.Groups[4].Value }
}
if (@($locations).Count -lt 300) { throw "location.dfn parsed only $(@($locations).Count) rows" }

$gateText = Read-Text "$PSScriptRoot/GameMoongates.codex"
$gates = @([regex]::Matches($gateText, 'GmgGate \{ serial = 0, x = (\d+), y = (\d+)') | ForEach-Object { ,@([int]$_.Groups[1].Value, [int]$_.Groups[2].Value) })
if ($gates.Count -ne 8) { throw "GameMoongates gate table has $($gates.Count) rows, expected 8" }

$spawnText = Read-Text "$PSScriptRoot/WorldSpawnData.codex"
$regions = foreach ($m in [regex]::Matches($spawnText, 'WsRegion \{ id = (\d+), name = "([^"]*)", area = WsRect \{ x1 = (\d+), y1 = (\d+), x2 = (\d+), y2 = (\d+)')) {
    [pscustomobject]@{ Id=[int]$m.Groups[1].Value; Name=$m.Groups[2].Value; X1=[int]$m.Groups[3].Value; Y1=[int]$m.Groups[4].Value; X2=[int]$m.Groups[5].Value; Y2=[int]$m.Groups[6].Value }
}
if (@($regions).Count -lt 400) { throw "WorldSpawnData parsed only $(@($regions).Count) regions" }
$monsterRegions = @($regions | Where-Object { $_.Name -notmatch 'Animals|Wandering Healer|Town' })

function Npc-Sites([string]$text, [string]$pattern) {
    $sites = [Collections.Generic.List[object]]::new()
    foreach ($m in [regex]::Matches($text, '(?ms)^\[REGIONSPAWN (\d+)\]\s*\{(.*?)^\}')) {
        $body = $m.Groups[2].Value
        if ($body -notmatch "(?m)^NPC=($pattern)\s*$") { continue }
        if ($body -match '(?m)^WORLD=(\d+)' -and [int]$Matches[1] -ne 0) { continue }
        $c = @('X1','Y1','X2','Y2') | ForEach-Object { if ($body -match "(?m)^$_=(\d+)") { [int]$Matches[1] } else { -1 } }
        if ($c -contains -1) { continue }
        $x = [int][Math]::Floor(($c[0] + $c[2]) / 2); $y = [int][Math]::Floor(($c[1] + $c[3]) / 2)
        $near = $false
        foreach ($s in $sites) { if ([Math]::Max([Math]::Abs($s[0] - $x), [Math]::Abs($s[1] - $y)) -le 8) { $near = $true } }
        if (-not $near) { $sites.Add(@($x, $y)) }
    }
    return ,$sites
}

$rows = [Collections.Generic.List[string]]::new()
$names = @{}
function Add-Row([string]$town, [string]$name, [int]$x, [int]$y) {
    if ($names.ContainsKey($name)) { throw "Duplicate goto name $name" }
    if ($x -lt 0 -or $x -ge 6144 -or $y -lt 0 -or $y -ge 4096) { throw "Goto $name outside the map: $x,$y" }
    $names[$name] = 1
    $rows.Add("CpPlace { town = ""$town"", name = ""$name"", x = $x, y = $y }")
}
function Add-Kind([string]$town, [string]$base, $sites) {
    for ($i = 0; $i -lt $sites.Count; $i++) { Add-Row $town ($base + $(if ($i -eq 0) { '' } else { [string]($i + 1) })) $sites[$i][0] $sites[$i][1] }
}
function Spawn-Near([int]$x, [int]$y) {
    $lost = $x -ge 5120
    $best = $null; $bestDistance = [int]::MaxValue
    foreach ($r in $monsterRegions) {
        if (($r.X1 -ge 5120) -ne $lost) { continue }
        $cx = [Math]::Min([Math]::Max($x, $r.X1), $r.X2); $cy = [Math]::Min([Math]::Max($y, $r.Y1), $r.Y2)
        $d = [Math]::Max([Math]::Abs($cx - $x), [Math]::Abs($cy - $y))
        if ($d -gt 0 -and $d -le 200 -and $d -lt $bestDistance) { $best = @($cx, $cy, $r.Id, $d); $bestDistance = $d }
    }
    if (-not $best) { return ,@() }
    return ,$best
}

foreach ($b in $britain) { Add-Row 'Britain' $b[0] $b[1] $b[2] }
$spawns = [ordered]@{}
$s = Spawn-Near 1436 1693; Add-Row 'Britain' 'britspawn' $s[0] $s[1]; $spawns['britspawn'] = "$($s[2]) at $($s[3]) tiles"

foreach ($t in $towns) {
    $placed = $rows.Count
    $text = Read-Text "$ReferenceRoot/data/dfndata/spawn/felucca/spawn_felucca_town_$($t.file).dfn"
    $banks = [Collections.Generic.List[object]]::new()
    foreach ($l in $locations) { if ($l.Name -match ('^' + [regex]::Escape($t.location) + ' - Bank')) { $banks.Add(@($l.X, $l.Y)) } }
    if ($banks.Count -eq 0) { $banks = Npc-Sites $text 'banker' }
    Add-Kind $t.town ($t.prefix + 'bank') $banks
    if ($t.gate -ge 0) { Add-Row $t.town ($t.prefix + 'moon') $gates[$t.gate][0] $gates[$t.gate][1] }
    Add-Kind $t.town ($t.prefix + 'healer') (Npc-Sites $text 'healer')
    Add-Kind $t.town ($t.prefix + 'smith') (Npc-Sites $text 'blacksmith|weaponsmith|armourer|armorer')
    Add-Kind $t.town ($t.prefix + 'inn') (Npc-Sites $text 'innkeeper')
    $first = $rows.Count - $placed
    if ($first -le 0) { throw "$($t.town) has no place in location.dfn or its spawn file" }
    $anchor = [regex]::Match($rows[$rows.Count - $first], 'x = (\d+), y = (\d+)')
    $s = Spawn-Near ([int]$anchor.Groups[1].Value) ([int]$anchor.Groups[2].Value)
    if ($s.Count -eq 0) { $spawns[$t.prefix + 'spawn'] = 'none within 200 tiles' }
    else { Add-Row $t.town ($t.prefix + 'spawn') $s[0] $s[1]; $spawns[$t.prefix + 'spawn'] = "$($s[2]) at $($s[3]) tiles" }
}

$text = [Text.StringBuilder]::new()
[void]$text.AppendLine('Chapter: GotoPlaceData')
[void]$text.AppendLine('')
[void]$text.AppendLine(' Modified UOX3 location.dfn and Felucca town spawn data, GPL-2.0-or-later.')
[void]$text.AppendLine(' Copyright 1997, 98 Marcus Rating (Cironian); see the retained UOX3 license.')
[void]$text.AppendLine(' Generated by import-goto-places.ps1. License: ports/UOX3-LICENSE.txt.')
[void]$text.AppendLine('  CpPlace = record { town : Text, name : Text, x : Integer, y : Integer }')
$chunks = [Math]::Ceiling($rows.Count / 24.0)
for ($part = 0; $part -lt $chunks; $part++) {
    [void]$text.AppendLine("  gpd-places-$part : List CpPlace -> List CpPlace")
    [void]$text.AppendLine("  gpd-places-$part (out) =")
    $last = 'out'
    for ($j = $part * 24; $j -lt [Math]::Min($rows.Count, ($part + 1) * 24); $j++) {
        $lead = if ($j -eq $part * 24) { '    let' } else { '    in let' }
        [void]$text.AppendLine("$lead r$j = list-push $last ($($rows[$j]))"); $last = "r$j"
    }
    $tail = if ($part + 1 -lt $chunks) { "gpd-places-$($part + 1) $last" } else { $last }
    [void]$text.AppendLine("    in $tail")
}
[void]$text.AppendLine('  gpd-places : Integer -> List CpPlace')
[void]$text.AppendLine("  gpd-places (unused) = gpd-places-0 (__list-with-capacity $($rows.Count))")
[void]$text.AppendLine('')
[void]$text.AppendLine('Page 1')
foreach ($path in $sourceHashes.Keys) { if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $sourceHashes[$path]) { throw "Source changed during import: $path" } }
[IO.File]::WriteAllText([IO.Path]::GetFullPath($Output), ($text.ToString() -replace '\r?\n', "`r`n"), [Text.UTF8Encoding]::new($false))
Write-Output "Wrote $Output with $($rows.Count) places"
foreach ($k in $spawns.Keys) { Write-Output "$k region $($spawns[$k])" }
