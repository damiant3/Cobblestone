# Build explorer.db.img: ALL explorer content from ExplorerData.codex written
# as a multi-table ExplorerStore .db (directory + per-table page-blocks), raw,
# 1 MB padded, for the bare-metal server to read via block-read-sector.
# Single source of truth = ExplorerData.codex.
param([string]$Out = "build-output\explorer.db.img")
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$src = Get-Content (Join-Path $Repo 'apps\explorer\ExplorerData.codex') -Raw

function Get-Block([string]$listName) {
  $i = $src.IndexOf("$listName = [")
  if ($i -lt 0) { throw "list not found: $listName" }
  $seg = $src.Substring($i)
  $e = $seg.IndexOf("`n  ]")
  if ($e -lt 0) { throw "list close not found: $listName" }
  $seg.Substring(0, $e)
}
function Rows-2str([string]$b) { ([regex]'\{ name = "([^"]*)", [\w-]+ = "([^"]*)"').Matches($b) | ForEach-Object { ,@($_.Groups[1].Value, $_.Groups[2].Value) } }
function Rows-rar([string]$b)  { ([regex]'\{ name = "([^"]*)", color = "([^"]*)", prompt = "([^"]*)" \}').Matches($b) | ForEach-Object { ,@($_.Groups[1].Value, $_.Groups[2].Value, $_.Groups[3].Value) } }
function Rows-grp([string]$b)  { ([regex]'\{ name = "([^"]*)", from = (\d+), to = (\d+) \}').Matches($b) | ForEach-Object { ,@($_.Groups[1].Value, $_.Groups[2].Value, $_.Groups[3].Value) } }

# (db-table-name, ExplorerData list var, kind)
$spec = @(
  @('biomes','biome-defs','2'), @('genres','biome-genres','g'),
  @('times','time-defs','2'), @('weathers','weather-defs','2'), @('moods','mood-defs','2'), @('scales','scale-defs','2'),
  @('races','all-races','2'), @('racegroups','race-groups','g'),
  @('classes','class-defs','2'), @('genders','gender-defs','2'), @('personalities','personality-defs','2'), @('portraits','portrait-modes','2'),
  @('items','item-categories','2'), @('materials','material-types','2'), @('rarities','rarity-tiers','r'),
  @('conditions','condition-defs','2'), @('enchants','enchant-defs','2'), @('aligns','align-defs','2'), @('sizes','size-defs','2'), @('colors','color-defs','2'),
  @('mod-pommel','sword-pommel-opts','2'), @('mod-guard','sword-guard-opts','2'), @('mod-blade','sword-blade-opts','2'),
  @('mod-phead','polearm-head-opts','2'), @('mod-phaft','polearm-haft-opts','2'),
  @('mod-bottle','potion-bottle-opts','2'), @('mod-liquid','potion-liquid-opts','2'),
  @('mod-style','shield-style-opts','2'), @('mod-emblem','shield-emblem-opts','2'), @('mod-visor','helm-visor-opts','2'), @('mod-gem','ring-gem-opts','2')
)

function W16($list,[int]$v){ $list.Add([byte]($v -band 0xFF)); $list.Add([byte](($v -shr 8) -band 0xFF)) }
function Build-Block($rows) {
  $b = [System.Collections.Generic.List[byte]]::new()
  W16 $b @($rows).Count
  foreach ($r in $rows) {
    $b.Add([byte]$r.Count)
    foreach ($f in $r) { $fb = [System.Text.Encoding]::ASCII.GetBytes([string]$f); W16 $b $fb.Length; foreach ($x in $fb) { $b.Add($x) } }
  }
  ,$b.ToArray()
}

$tables = foreach ($s in $spec) {
  $block = Get-Block $s[1]
  $rows = switch ($s[2]) { '2' { Rows-2str $block } 'r' { Rows-rar $block } 'g' { Rows-grp $block } }
  @{ name = $s[0]; bytes = (Build-Block $rows); count = @($rows).Count }
}

# directory: [u16 tableCount][per table: u8 nameLen, name, u32 dataOff]
$dirLen = 2; foreach ($t in $tables) { $dirLen += 1 + $t.name.Length + 4 }
$off = $dirLen
$dir = [System.Collections.Generic.List[byte]]::new()
W16 $dir @($tables).Count
$blocks = [System.Collections.Generic.List[byte]]::new()
foreach ($t in $tables) {
  $nb = [System.Text.Encoding]::ASCII.GetBytes($t.name)
  $dir.Add([byte]$nb.Length); foreach ($x in $nb) { $dir.Add($x) }
  foreach ($k in 0..3) { $dir.Add([byte](($off -shr (8*$k)) -band 0xFF)) }   # u32 LE dataOff
  foreach ($x in $t.bytes) { $blocks.Add($x) }
  $off += $t.bytes.Length
}
$payload = $dir.ToArray() + $blocks.ToArray()
Write-Host "[build-db] $(@($tables).Count) tables, payload $($payload.Length) bytes"
$tables | ForEach-Object { "  {0,-14} {1,3} rows" -f $_.name, $_.count } | Write-Host

$img = New-Object byte[] 1048576
[Array]::Copy($payload, $img, $payload.Length)
$outPath = Join-Path $Repo $Out
[System.IO.File]::WriteAllBytes($outPath, $img)
Write-Host "[build-db] wrote $outPath ($($img.Length) bytes)"
