param(
    [string]$ClientRoot = 'D:/Projects/uoaix-client',
    [string]$ReferenceRoot = 'D:/Projects/uo-reference',
    [string]$Output = "$PSScriptRoot/HouseData.codex",
    [string]$SignFile = "$PSScriptRoot/houses.cfg",
    [string]$Report = '' 
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# UOAIX-236: every scenery house of the land, for player housing. A house is the roofed floor behind an unlocked UOX3 door
# (felucca_doors.jsdata type 12) on the 1.25 statics, reached from the door without crossing a wall (0x10) or impassable
# (0x40) static at the door's height. A floor that touches a UOX3 or premises.cfg sign within 4 tiles of a door, a shop,
# inn or worker-home box of the server's own tables, a public building, or the Lost Lands (x 5120 and up) is not for sale;
# nor is a floor of fewer than 12 or more than 600 tiles, or with one room past 300 (a cell, a closet, a castle or a
# covered street), or one whose
# doors all open indoors.
$tiles = [IO.File]::ReadAllBytes((Join-Path $ClientRoot 'TILEDATA.MUL'))
if ($tiles.Length -ne 1036288) { throw 'Requires legacy 32-bit TILEDATA.MUL' }
$idx = [IO.File]::ReadAllBytes((Join-Path $ClientRoot 'STAIDX0.MUL'))
$st = [IO.File]::ReadAllBytes((Join-Path $ClientRoot 'STATICS0.MUL'))
$templates = Join-Path $ReferenceRoot 'UOX3/data/js/jsdata/worldtemplates'

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
public static class UoaixHouses {
 const uint Wall=0x10,Impassable=0x40,Surface=0x200,Roof=0x10000000;
 static byte[] Tiles,Idx,St;
 public static void Load(byte[] tiles,byte[] idx,byte[] st){Tiles=tiles;Idx=idx;St=st;}
 static uint Flags(int id){int at=428032+(id/32)*1188+4+(id%32)*37;return BitConverter.ToUInt32(Tiles,at);}
 static int Height(int id){int at=428032+(id/32)*1188+4+(id%32)*37;return Tiles[at+16];}
 // Every static on x, y as (art, z) pairs.
 static List<int> At(int x,int y){var r=new List<int>();if(x<0||y<0||x>=6144||y>=4096)return r;int b=(x/8)*512+y/8;
  uint off=BitConverter.ToUInt32(Idx,b*12),len=BitConverter.ToUInt32(Idx,b*12+4);if(off==0xFFFFFFFF||len==0xFFFFFFFF)return r;
  for(long a=off;a<off+len;a+=7){int id=BitConverter.ToUInt16(St,(int)a);if(St[a+2]==x%8&&St[a+3]==y%8){r.Add(id&0x3FFF);r.Add((sbyte)St[a+4]);}}return r;}
 public static bool Covered(int x,int y,int z){var s=At(x,y);for(int i=0;i<s.Count;i+=2){uint f=Flags(s[i]);int sz=s[i+1];
  if((f&Roof)!=0&&sz>z)return true;if((f&Surface)!=0&&sz>=z+15)return true;}return false;}
 public static bool Blocked(int x,int y,int z){var s=At(x,y);for(int i=0;i<s.Count;i+=2){uint f=Flags(s[i]);int sz=s[i+1];
  if((f&(Wall|Impassable))!=0&&sz<z+16&&sz+Height(s[i])>z+2)return true;}return false;}
 public static HashSet<int> Doors=new HashSet<int>();
 public static bool Open(int x,int y,int z){return !Doors.Contains(x*4096+y)&&Covered(x,y,z)&&!Blocked(x,y,z);}
 // A door's wall runs through it: a side of the door is the tile across the wall line, never along it.
 public static bool Side(int x,int y,int z){return Doors.Contains(x*4096+y)||Blocked(x,y,z);}
 // The open floor reached from a seed at height z, as x * 4096 + y; stops past limit tiles.
 public static List<int> Fill(int seed,int z,int limit){var seen=new HashSet<int>();var q=new Queue<int>();var r=new List<int>();
  seen.Add(seed);q.Enqueue(seed);int[] dx={1,-1,0,0},dy={0,0,1,-1};
  while(q.Count>0&&r.Count<=limit){int t=q.Dequeue();r.Add(t);int x=t/4096,y=t%4096;
   for(int d=0;d<4;d++){int nx=x+dx[d],ny=y+dy[d];int k=nx*4096+ny;if(nx<0||ny<0||nx>=6144||ny>=4096||seen.Contains(k))continue;if(Open(nx,ny,z)){seen.Add(k);q.Enqueue(k);}}}
  return r;}
 // A front door faces the street: the roofed tiles outside it (a porch, the eaves) reach an open sky tile without passing a
 // door. A room behind a door, an inn room's hall among them, reaches none.
 public static bool Street(int x,int y,int z){var seen=new HashSet<int>();var q=new Queue<int>();int[] dx={1,-1,0,0},dy={0,0,1,-1};
  if(Doors.Contains(x*4096+y)||Blocked(x,y,z))return false;if(!Covered(x,y,z))return true;seen.Add(x*4096+y);q.Enqueue(x*4096+y);
  while(q.Count>0&&seen.Count<400){int t=q.Dequeue();int tx=t/4096,ty=t%4096;
   for(int d=0;d<4;d++){int nx=tx+dx[d],ny=ty+dy[d];int k=nx*4096+ny;if(nx<0||ny<0||nx>=6144||ny>=4096||seen.Contains(k)||Doors.Contains(k)||Blocked(nx,ny,z))continue;if(!Covered(nx,ny,z))return true;seen.Add(k);q.Enqueue(k);}}
  return false;}
}
'@
[UoaixHouses]::Load($tiles, $idx, $st)

function Read-Template([string]$Name) {
    $rows = [Collections.Generic.List[object]]::new()
    foreach ($line in [IO.File]::ReadLines((Join-Path $templates $Name))) {
        $p = $line.Split('|')
        if ($p.Count -lt 7) { continue }
        $rows.Add([pscustomobject]@{ art = [int]$p[0]; name = $p[1]; type = [int]$p[3]; x = [int]$p[4]; y = [int]$p[5]; z = [int]$p[6] })
    }
    $rows
}
$signs = [Collections.Generic.List[object]]::new()
# A signpost or a street's name marks no premises.
foreach ($s in (Read-Template 'felucca_signs.jsdata')) { if ($s.name -notmatch '^wooden signpost$| (Road|Way|Street|Lane|Avenue|Bridge|Path)$') { $signs.Add($s) } }
$cfgSign = $false
foreach ($line in [IO.File]::ReadLines((Join-Path $PSScriptRoot 'premises.cfg'))) {
    if ($line -match '^Sign\s') { $cfgSign = $true; continue }
    if ($cfgSign -and $line -match '^(\d+)\s+(\d+)\s+(-?\d+)') { $signs.Add([pscustomobject]@{ x = [int]$Matches[1]; y = [int]$Matches[2]; z = [int]$Matches[3] }); continue }
    $cfgSign = $false
}
# The server's own premises: every shop, inn and worker-home box, and the castle kitchen and fish hall quarters.
$boxes = [Collections.Generic.List[int[]]]::new()
$outdoor = @('Ore', 'Woods', 'Field', 'Pasture', 'Shore', 'Wild plants', 'Rock')
foreach ($file in @('Cities.codex', 'CityWorkers.codex', 'CompositeGatherer.codex', 'BritainCatalog.codex', 'TownLiveState.codex', 'KeeperLeisure.codex', 'CivicLiveState.codex')) {
    $text = [IO.File]::ReadAllText((Join-Path $PSScriptRoot $file))
    foreach ($m in [regex]::Matches($text, 'title = "([^"]*)", premises = "[^"]*", x1 = (-?\d+), y1 = (-?\d+), x2 = (-?\d+), y2 = (-?\d+)')) {
        if ($outdoor -contains $m.Groups[1].Value) { continue }
        $boxes.Add(@([int]$m.Groups[2].Value, [int]$m.Groups[3].Value, [int]$m.Groups[4].Value, [int]$m.Groups[5].Value))
    }
    foreach ($m in [regex]::Matches($text, 'cw-house (\d+) (\d+) (\d+) (\d+) (\d+) (\d+)')) {
        $boxes.Add(@([int]$m.Groups[3].Value, [int]$m.Groups[4].Value, [int]$m.Groups[5].Value, [int]$m.Groups[6].Value))
    }
    foreach ($m in [regex]::Matches($text, 'cw-box "Inn" (\d+) (\d+) (\d+) (\d+)')) {
        $boxes.Add(@([int]$m.Groups[1].Value, [int]$m.Groups[2].Value, [int]$m.Groups[3].Value, [int]$m.Groups[4].Value))
    }
}
if ($boxes.Count -lt 100) { throw "only $($boxes.Count) premises boxes read from the server's tables" }
# ServUO Data/Regions.xml: no house is sold in a Felucca dungeon, jail or Mondain region, or a named castle. Its no-housing
# regions forbid placing a new house, not selling a standing one.
$regions = [Collections.Generic.List[object]]::new()
$xml = [xml](Get-Content -LiteralPath (Join-Path $ReferenceRoot 'ServUO/Data/Regions.xml') -Raw)
function Read-Regions($node) {
    if (-not $node.PSObject.Properties['region']) { return }
    foreach ($r in @($node.region)) {
        $type = $r.GetAttribute('type'); $name = $r.GetAttribute('name')
        if ($type -in @('DungeonRegion', 'Jail', 'MondainRegion') -or $name -match 'Castle') {
            foreach ($rect in @($r.SelectNodes('rect'))) {
                if ($rect.HasAttribute('x1')) {
                    $x = [int]$rect.GetAttribute('x1'); $y = [int]$rect.GetAttribute('y1')
                    $w = [int]$rect.GetAttribute('x2') - $x; $h = [int]$rect.GetAttribute('y2') - $y
                } else {
                    $x = [int]$rect.GetAttribute('x'); $y = [int]$rect.GetAttribute('y')
                    $w = [int]$rect.GetAttribute('width'); $h = [int]$rect.GetAttribute('height')
                }
                $regions.Add([pscustomobject]@{ name = $name; x1 = $x; y1 = $y; x2 = $x + $w - 1; y2 = $y + $h - 1 })
            }
        }
        Read-Regions $r
    }
}
Read-Regions ($xml.ServerRegions.Facet | Where-Object { $_.name -eq 'Felucca' })
if ($regions.Count -lt 10) { throw "only $($regions.Count) no-housing rectangles read from Regions.xml" }
# Public buildings of the 1998 map that ServUO's regions do not name: Castle British, the Lycaeum, and the labyrinth that
# holds the daemon spawn homes at 1141,2236.
foreach ($p in @(@('Castle British', 1295, 1550, 1415, 1700), @('the Lycaeum', 4500, 900, 4600, 1000), @('the labyrinth', 1110, 2200, 1160, 2260))) {
    $regions.Add([pscustomobject]@{ name = $p[0]; x1 = $p[1]; y1 = $p[2]; x2 = $p[3]; y2 = $p[4] })
}

$all = @(Read-Template 'felucca_doors.jsdata')
foreach ($d in $all) { [void][UoaixHouses]::Doors.Add($d.x * 4096 + $d.y) }
$doors = @($all | Where-Object { $_.type -eq 12 -and $_.x -lt 5120 })
$owner = @{}
$houses = [Collections.Generic.List[object]]::new()
function Merge($a, $b) {
    if ([object]::ReferenceEquals($a, $b)) { return $a }
    foreach ($d in $b.doors) { $a.doors.Add($d) }
    foreach ($t in $b.floor) { $a.floor.Add($t); $owner[$t] = $a }
    if ($null -eq $a.out) { $a.out = $b.out; $a.front = $b.front }
    if ($b.capped) { $a.capped = $true }
    $b.gone = $true
    $a
}
# A door's two sides lie across its wall line: the side that reaches the sky is the street, the roofed side that does not is a room.
foreach ($d in $doors) {
    $ew = [UoaixHouses]::Side($d.x - 1, $d.y, $d.z) -or [UoaixHouses]::Side($d.x + 1, $d.y, $d.z)
    $ns = [UoaixHouses]::Side($d.x, $d.y - 1, $d.z) -or [UoaixHouses]::Side($d.x, $d.y + 1, $d.z)
    if ($ew -eq $ns) { continue }
    $sides = if ($ew) { @(@($d.x, ($d.y - 1)), @($d.x, ($d.y + 1))) } else { @(@(($d.x - 1), $d.y), @(($d.x + 1), $d.y)) }
    $rooms = [Collections.Generic.List[object]]::new()
    $out = $null
    foreach ($s in $sides) {
        $k = $s[0] * 4096 + $s[1]
        if ($owner.ContainsKey($k)) { $rooms.Add($owner[$k]); continue }
        if ([UoaixHouses]::Street($s[0], $s[1], $d.z)) { $out = $s; continue }
        if (-not [UoaixHouses]::Open($s[0], $s[1], $d.z)) { continue }
        $h = [pscustomobject]@{ doors = [Collections.Generic.List[object]]::new(); floor = [Collections.Generic.List[int]]::new(); out = $null; front = $null; gone = $false; capped = $false }
        $room = [UoaixHouses]::Fill($k, $d.z, 300)
        if ($room.Count -gt 300) { $h.capped = $true }
        foreach ($t in $room) { $h.floor.Add($t); $owner[$t] = $h }
        $houses.Add($h); $rooms.Add($h)
    }
    if ($rooms.Count -eq 0) { continue }
    $h = $rooms[0]
    for ($i = 1; $i -lt $rooms.Count; $i++) { $h = Merge $h $rooms[$i] }
    $h.doors.Add($d)
    if ($null -ne $out -and $null -eq $h.out) { $h.out = $out; $h.front = $d }
}
$houses = @($houses | Where-Object { -not $_.gone })
function Near-Sign($h) {
    foreach ($d in $h.doors) { foreach ($s in $signs) { if ([Math]::Abs($s.x - $d.x) -le 4 -and [Math]::Abs($s.y - $d.y) -le 4) { return $true } } }
    $false
}
$kept = [Collections.Generic.List[object]]::new()
$why = @{ size = 0; front = 0; sign = 0; premises = 0; region = 0 }
$verdicts = [Collections.Generic.List[string]]::new()
function Verdict($h, [string]$v) { foreach ($d in $h.doors) { $verdicts.Add("$($d.x),$($d.y),$($h.floor.Count),$v") } }
foreach ($h in $houses) {
    if ($h.capped -or $h.floor.Count -lt 12 -or $h.floor.Count -gt 600) { $why.size++; Verdict $h size; continue }
    if ($null -eq $h.out) { $why.front++; Verdict $h front; continue }
    if (Near-Sign $h) { $why.sign++; Verdict $h sign; continue }
    $xs = @($h.floor | ForEach-Object { [int][Math]::Floor($_ / 4096) }); $ys = @($h.floor | ForEach-Object { $_ % 4096 })
    $x1 = ($xs | Measure-Object -Minimum).Minimum; $x2 = ($xs | Measure-Object -Maximum).Maximum
    $y1 = ($ys | Measure-Object -Minimum).Minimum; $y2 = ($ys | Measure-Object -Maximum).Maximum
    $used = $false
    foreach ($b in $boxes) { if ($b[0] -le $x2 -and $b[2] -ge $x1 -and $b[1] -le $y2 -and $b[3] -ge $y1) { $used = $true; break } }
    if ($used) { $why.premises++; Verdict $h premises; continue }
    $public = @($regions | Where-Object { $_.x1 -le $x2 -and $_.x2 -ge $x1 -and $_.y1 -le $y2 -and $_.y2 -ge $y1 })
    if ($public.Count -gt 0) { $why.region++; Verdict $h $public[0].name; continue }
    Verdict $h house
    $kept.Add([pscustomobject]@{ sx = $h.out[0]; sy = $h.out[1]; sz = $h.front.z; dx = $h.front.x; dy = $h.front.y; x1 = $x1; y1 = $y1; x2 = $x2; y2 = $y2; tiles = $h.floor.Count })
}
$sorted = @($kept | Sort-Object { $_.sx * 4096 + $_.sy })
if ($sorted.Count -lt 1) { throw 'no houses' }

# The sign hangs on the tile outside the front door: ServUO HouseSign 0x0BD2 faces a door in a north-south wall, 0x0BD1 one
# in an east-west wall (TILEDATA names both "sign").
$cfg = [Collections.Generic.List[string]]::new()
$cfg.Add('# Generated by import-houses.ps1 (UOAIX-236); do not edit by hand. One house sign outside the front door of every')
$cfg.Add('# scenery house that is for sale (HouseData.codex), read by import-decoration.ps1 after premises.cfg.')
foreach ($art in @(0x0BD1, 0x0BD2)) {
    $rows = @($sorted | Where-Object { $(if ($_.sx -ne $_.dx) { 0x0BD2 } else { 0x0BD1 }) -eq $art })
    if ($rows.Count -eq 0) { continue }
    $cfg.Add(('Sign 0x{0:X4} (Name=a house for sale)' -f $art))
    foreach ($r in $rows) { $cfg.Add("$($r.sx) $($r.sy) $($r.sz)") }
}
[IO.File]::WriteAllText($SignFile, (($cfg -join "`r`n") + "`r`n"))

$out = [Collections.Generic.List[string]]::new()
$out.Add('Chapter: HouseData')
$out.Add('')
$out.Add(' Generated by import-houses.ps1 from UOX3 felucca_doors.jsdata and felucca_signs.jsdata (GPL-2.0-or-later) over the')
$out.Add(' 1.25 statics; do not edit by hand. Eleven integers a house: the sign x, y, z, the front door x, y, z, the floor box x1,')
$out.Add(' y1, x2, y2 and its tile count; sorted by the sign''s x * 4096 + y.')
$out.Add("  hd-count : Integer = $($sorted.Count)")
$out.Add('  hd-row-cells : Integer = 11')
$out.Add('  hd-rows : Integer -> List Integer')
$cells = [Collections.Generic.List[string]]::new()
foreach ($r in $sorted) { $cells.Add("$($r.sx), $($r.sy), $($r.sz), $($r.dx), $($r.dy), $($r.sz), $($r.x1), $($r.y1), $($r.x2), $($r.y2), $($r.tiles)") }
$body = [Collections.Generic.List[string]]::new()
for ($i = 0; $i -lt $cells.Count; $i += 4) {
    $end = [Math]::Min($i + 4, $cells.Count) - 1
    $body.Add('    ' + (($cells[$i..$end]) -join ', ') + $(if ($end -lt $cells.Count - 1) { ',' } else { ']' }))
}
$body[0] = '  hd-rows (unused) = [' + $body[0].TrimStart()
foreach ($b in $body) { $out.Add($b) }
[IO.File]::WriteAllText($Output, (($out -join "`r`n") + "`r`n"))
"doors=$($doors.Count) buildings=$($houses.Count) houses=$($sorted.Count) dropped: size=$($why.size) no-front=$($why.front) signed=$($why.sign) premises=$($why.premises) region=$($why.region) -> $Output, $SignFile"
if ($Report) { [IO.File]::WriteAllLines($Report, $verdicts) }
