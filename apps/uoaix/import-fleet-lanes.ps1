param(
    [string]$Map = 'D:/Games/UOAIX-Reference/MAP0.MUL',
    [string]$Output = "$PSScriptRoot/MerchantFleetLanes.codex"
)
# UOAIX-237 F2: the merchant fleet's docks and sea routes from the 1.25 MAP0.MUL. A block is 8 x 8 tiles (MAP0 stores
# 768 x 512 blocks column-major, 4 + 64 * 3 bytes each); it is sea when all 64 land tiles are water (0xA8..0xAB,
# 0x136..0x137). A route is an 8-connected breadth-first path over sea blocks, kept only at its turns, so every straight
# run between two waypoints crosses sea blocks alone. The open sea is the largest connected set of sea blocks; a port's
# dock is the open-sea block nearest its city rectangle's centre, except Britain's, the harbour the fishing boat uses. Minoc lies on a lake no sea reaches,
# so no lane calls there.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -TypeDefinition @'
using System; using System.IO; using System.Collections.Generic;
public static class FleetSea {
  public const int W = 768, H = 512;
  public static bool[] Sea; public static int[] Comp;
  public static void Load(string path){ var b = File.ReadAllBytes(path); Sea = new bool[W*H];
    for (int bx = 0; bx < W; bx++) for (int by = 0; by < H; by++) { int o = (bx*512+by)*196+4; bool all = true;
      for (int c = 0; c < 64 && all; c++) { int t = BitConverter.ToUInt16(b, o+c*3); all = (t >= 0xA8 && t <= 0xAB) || t == 0x136 || t == 0x137; }
      Sea[bx*H+by] = all; }
    Comp = new int[W*H]; for (int i = 0; i < Comp.Length; i++) Comp[i] = -1; int n = 0;
    for (int i = 0; i < Comp.Length; i++) if (Sea[i] && Comp[i] < 0) { var q = new Queue<int>(); q.Enqueue(i); Comp[i] = n;
      while (q.Count > 0) { int c = q.Dequeue(); int x = c / H, y = c % H;
        for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) { int nx = x+dx, ny = y+dy; if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
          int k = nx*H+ny; if (Sea[k] && Comp[k] < 0) { Comp[k] = n; q.Enqueue(k); } } } n++; } }
  public static int Largest(){ var count = new Dictionary<int,int>(); foreach (int c in Comp) if (c >= 0) count[c] = (count.ContainsKey(c) ? count[c] : 0) + 1;
    int best = -1, bn = 0; foreach (var kv in count) if (kv.Value > bn) { bn = kv.Value; best = kv.Key; } return best; }
  public static int Nearest(int tx, int ty, int comp){ int best = -1; long bd = long.MaxValue;
    for (int i = 0; i < Comp.Length; i++) if (Comp[i] == comp) { long dx = (i/H)*8+4-tx, dy = (i%H)*8+4-ty; long d = dx*dx+dy*dy; if (d < bd) { bd = d; best = i; } } return best; }
  public static int[] Path(int from, int to){ var prev = new int[W*H]; for (int i = 0; i < prev.Length; i++) prev[i] = -2;
    var q = new Queue<int>(); q.Enqueue(from); prev[from] = -1;
    while (q.Count > 0) { int c = q.Dequeue(); if (c == to) break; int x = c / H, y = c % H;
      for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) { if (dx == 0 && dy == 0) continue; int nx = x+dx, ny = y+dy;
        if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue; int k = nx*H+ny;
        if (!Sea[k] || prev[k] != -2) continue; if (dx != 0 && dy != 0 && (!Sea[nx*H+y] || !Sea[x*H+ny])) continue;
        prev[k] = c; q.Enqueue(k); } }
    if (prev[to] == -2) return null; var path = new List<int>(); for (int c = to; c != -1; c = prev[c]) path.Add(c); path.Reverse(); return path.ToArray(); }
}
'@
[FleetSea]::Load($Map)
$open = [FleetSea]::Largest()
$cities = @{0 = @(1460, 1766); 1 = @(1811, 2643, 2075, 2912); 2 = @(4374, 1041, 4529, 1176); 7 = @(3657, 2122, 3766, 2269); 8 = @(3544, 1166, 3773, 1400); 9 = @(1305, 3672, 1534, 4061); 12 = @(2738, 618, 3040, 1003); 14 = @(2208, 1112, 2286, 1234)}
$lanes = @(@(0, 7, 2, 8), @(0, 1, 9), @(0, 12, 14))
$docks = [ordered]@{}
foreach ($c in ($cities.Keys | Sort-Object)) {
    $r = $cities[$c]
    $tx = if ($r.Count -eq 2) { $r[0] } else { [int](($r[0] + $r[2]) / 2) }
    $ty = if ($r.Count -eq 2) { $r[1] } else { [int](($r[1] + $r[3]) / 2) }
    $b = [FleetSea]::Nearest($tx, $ty, $open)
    $docks[[string]$c] = $b
}
$routes = [Collections.Generic.List[string]]::new()
foreach ($lane in $lanes) {
    for ($i = 0; $i -lt $lane.Count; $i++) {
        $from = $lane[$i]; $to = $lane[($i + 1) % $lane.Count]
        $path = [FleetSea]::Path($docks[[string]$from], $docks[[string]$to])
        if ($null -eq $path) { throw "No sea route from city $from to city $to" }
        $points = [Collections.Generic.List[int]]::new()
        for ($k = 0; $k -lt $path.Count; $k++) {
            $keep = $k -eq 0 -or $k -eq $path.Count - 1
            if (-not $keep) {
                $a = $path[$k - 1]; $b = $path[$k]; $n = $path[$k + 1]
                $dx1 = [Math]::Floor($b / 512) - [Math]::Floor($a / 512); $dy1 = ($b % 512) - ($a % 512)
                $dx2 = [Math]::Floor($n / 512) - [Math]::Floor($b / 512); $dy2 = ($n % 512) - ($b % 512)
                $keep = $dx1 -ne $dx2 -or $dy1 -ne $dy2
            }
            if ($keep) { $points.Add([int]([Math]::Floor($path[$k] / 512) * 8 + 4)); $points.Add([int](($path[$k] % 512) * 8 + 4)) }
        }
        $routes.Add("if from == $from & to == $to then [$($points -join ', ')]")
    }
}
$text = [Text.StringBuilder]::new()
[void]$text.AppendLine('Chapter: MerchantFleetLanes')
[void]$text.AppendLine('')
[void]$text.AppendLine(' Generated by import-fleet-lanes.ps1 from the 1.25 MAP0.MUL; regenerate, never edit. A route is the docks and turns')
[void]$text.AppendLine(' of a sea path from one port city of ct-table to the next on its lane, as tile x, y pairs.')
foreach ($k in @('x', 'y')) {
    $chain = '0'
    foreach ($c in ($docks.Keys | Sort-Object { [int]$_ } -Descending)) { $b = $docks[$c]; $v = if ($k -eq 'x') { [Math]::Floor($b / 512) * 8 + 4 } else { ($b % 512) * 8 + 4 }; $chain = "if city == $c then $v else $chain" }
    [void]$text.AppendLine("  mfl-dock-$k : Integer -> Integer")
    [void]$text.AppendLine("  mfl-dock-$k (city) = $chain")
}
[void]$text.AppendLine('  mfl-route : Integer, Integer -> List Integer')
[void]$text.AppendLine("  mfl-route (from) (to) = $(($routes -join ' else ')) else []")
[void]$text.AppendLine(''); [void]$text.AppendLine('Page 1')
[IO.File]::WriteAllText([IO.Path]::GetFullPath($Output), ($text.ToString() -replace '\r?\n', "`r`n"), [Text.UTF8Encoding]::new($false))
foreach ($c in $docks.Keys) { $b = $docks[$c]; "dock city $c at $([Math]::Floor($b / 512) * 8 + 4),$(($b % 512) * 8 + 4)" }
"routes $($routes.Count); wrote $Output"
