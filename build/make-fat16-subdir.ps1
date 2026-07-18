# Add a real subdirectory to a FAT16 image, host-side, written from the FAT16
# spec rather than from Fat16.codex. That independence is the whole point: a
# fixture built by the code under test proves only that the code agrees with
# itself, which is how a CCE short name would have round-tripped forever while
# being unreadable to every other FAT driver on earth.
#
# Creates /SUB (a directory) in the root, with its . and .. entries.
param(
    [Parameter(Mandatory=$true)][string]$In,
    [Parameter(Mandatory=$true)][string]$Out,
    [string]$DirName = 'SUB'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$b = [System.IO.File]::ReadAllBytes($In)
$p = 2048 * 512    # fat16-boot-partition-start, in bytes

function U16([int]$off) { [BitConverter]::ToUInt16($b, $p + $off) }
function U32([int]$off) { [BitConverter]::ToUInt32($b, $p + $off) }
function PutU16([int]$abs, [int]$v) {
    $b[$abs]     = [byte]($v -band 0xFF)
    $b[$abs + 1] = [byte](($v -shr 8) -band 0xFF)
}

$bps      = U16 11
$spc      = $b[$p + 13]
$reserved = U16 14
$nfats    = $b[$p + 16]
$rootEnts = U16 17
$fatSecs  = U16 22

$fatStart  = $reserved
$rootStart = $reserved + ($nfats * $fatSecs)
# [Math]::Floor, NOT [int]: PowerShell's [int] cast ROUNDS (16895/512 -> 33),
# which puts dataStart one sector past where every cluster actually lives.
$rootSecs  = [Math]::Floor(($rootEnts * 32 + $bps - 1) / $bps)
$dataStart = $rootStart + $rootSecs
$totalSec  = U32 32
$clusters  = [Math]::Floor(($totalSec - $dataStart) / $spc)

Write-Host "geometry: bps=$bps spc=$spc fat@$fatStart root@$rootStart data@$dataStart clusters=$clusters"

# --- 1. find a free cluster (FAT entry == 0), starting at 2 -------------
# Scan the whole volume, not an arbitrary prefix: this image's first free
# cluster is ~4891, so a scan capped at 4096 reports a 16 MB disk as full.
$free = -1
$maxEnt = [Math]::Min($clusters + 2, [Math]::Floor(($fatSecs * $bps) / 2))
for ($c = 2; $c -lt $maxEnt; $c++) {
    $e = [BitConverter]::ToUInt16($b, $p + ($fatStart * $bps) + ($c * 2))
    if ($e -eq 0) { $free = $c; break }
}
if ($free -lt 0) { throw "no free cluster in 2..$maxEnt" }
Write-Host "free cluster: $free"

# --- 2. mark it end-of-chain in EVERY FAT copy -------------------------
for ($f = 0; $f -lt $nfats; $f++) {
    $abs = $p + (($fatStart + ($f * $fatSecs)) * $bps) + ($free * 2)
    PutU16 $abs 0xFFFF
}

# --- 3. zero the directory's cluster -----------------------------------
$clusSec = $dataStart + (($free - 2) * $spc)
$clusAbs = $p + ($clusSec * $bps)
for ($i = 0; $i -lt ($spc * $bps); $i++) { $b[$clusAbs + $i] = 0 }

# --- 4. write the . and .. entries -------------------------------------
# 8.3 is space-padded ASCII, 11 bytes. attr 0x10 = ATTR_DIRECTORY.
# ".." in the root has cluster 0 by the spec, not the root's sector.
function PutEntry([int]$abs, [string]$name83, [int]$attr, [int]$cluster, [int]$size) {
    $n = $name83.PadRight(11).Substring(0, 11)
    $bytes = [System.Text.Encoding]::ASCII.GetBytes($n)
    [Array]::Copy($bytes, 0, $b, $abs, 11)
    $b[$abs + 11] = [byte]$attr
    for ($i = 12; $i -lt 26; $i++) { $b[$abs + $i] = 0 }
    PutU16 ($abs + 26) $cluster
    PutU16 ($abs + 28) ($size -band 0xFFFF)
    PutU16 ($abs + 30) (($size -shr 16) -band 0xFFFF)
}
PutEntry $clusAbs          '.'  0x10 $free 0
PutEntry ($clusAbs + 32)   '..' 0x10 0     0

# --- 5. add the directory's entry to the root --------------------------
$slot = -1
for ($i = 0; $i -lt $rootEnts; $i++) {
    $abs = $p + ($rootStart * $bps) + ($i * 32)
    if ($b[$abs] -eq 0 -or $b[$abs] -eq 0xE5) { $slot = $abs; break }
}
if ($slot -lt 0) { throw 'root full' }
PutEntry $slot $DirName 0x10 $free 0
Write-Host "wrote /$DirName -> cluster $free, root slot @$slot"

[System.IO.File]::WriteAllBytes($Out, $b)

# --- verify by re-reading, independently of how we wrote it ------------
$v = [System.IO.File]::ReadAllBytes($Out)
$nm = [System.Text.Encoding]::ASCII.GetString($v, $slot, 11)
$at = $v[$slot + 11]
$cl = [BitConverter]::ToUInt16($v, $slot + 26)
Write-Host ("verify root entry : name='{0}' attr=0x{1:X2} cluster={2}" -f $nm, $at, $cl)
$d1 = [System.Text.Encoding]::ASCII.GetString($v, $clusAbs, 11)
$d2 = [System.Text.Encoding]::ASCII.GetString($v, $clusAbs + 32, 11)
Write-Host ("verify dot/dotdot : '{0}' / '{1}'" -f $d1, $d2)
if ($at -ne 0x10) { throw 'attr is not ATTR_DIRECTORY' }
