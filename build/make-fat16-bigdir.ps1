# Fill directories of a FAT16 image with zero-size files, host-side, written
# from the FAT16 spec rather than from Fat16.codex (the same independence
# make-fat16-subdir.ps1 keeps). A listing that pages through a directory needs
# a fixture whose root spans more than one sector and whose subdirectory spans
# more than one cluster, which a hand-made image with two entries never does.
#
# Adds -RootCount files named R00..Rnn to the root and -SubCount files named
# S00..Snn to the first-level directory -SubDir, extending that directory's
# cluster chain in EVERY FAT copy when its clusters fill. The partition is
# found through the GPT when the image has one, else at LBA 2048.
param(
    [Parameter(Mandatory=$true)][string]$In,
    [Parameter(Mandatory=$true)][string]$Out,
    [int]$RootCount = 0,
    [string]$SubDir = 'EFI',
    [int]$SubCount = 0
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$b = [System.IO.File]::ReadAllBytes($In)
$gpt = [System.Text.Encoding]::ASCII.GetString($b, 512, 8) -eq 'EFI PART'
$entryLba = if ($gpt) { [BitConverter]::ToUInt64($b, 512 + 72) } else { 0 }
$p = if ($gpt) { [int]([BitConverter]::ToUInt64($b, [int]$entryLba * 512 + 32)) * 512 } else { 2048 * 512 }

function U16([int]$abs) { [BitConverter]::ToUInt16($b, $abs) }
function PutU16([int]$abs, [int]$v) {
    $b[$abs]     = [byte]($v -band 0xFF)
    $b[$abs + 1] = [byte](($v -shr 8) -band 0xFF)
}

$bps      = U16 ($p + 11)
$spc      = $b[$p + 13]
$reserved = U16 ($p + 14)
$nfats    = $b[$p + 16]
$rootEnts = U16 ($p + 17)
$fatSecs  = U16 ($p + 22)
$tot16    = U16 ($p + 19)
$totalSec = if ($tot16 -ne 0) { $tot16 } else { [BitConverter]::ToUInt32($b, $p + 32) }

$fatStart  = $reserved
$rootStart = $reserved + ($nfats * $fatSecs)
$rootSecs  = [Math]::Floor(($rootEnts * 32 + $bps - 1) / $bps)
$dataStart = $rootStart + $rootSecs
$clusters  = [Math]::Floor(($totalSec - $dataStart) / $spc)
$perClus   = ($spc * $bps) / 32
Write-Host "geometry: part@$($p / 512) bps=$bps spc=$spc fat@$fatStart root@$rootStart data@$dataStart clusters=$clusters"

function FatGet([int]$c) { U16 ($p + ($fatStart * $bps) + ($c * 2)) }
function FatSet([int]$c, [int]$v) {
    for ($f = 0; $f -lt $nfats; $f++) { PutU16 ($p + (($fatStart + ($f * $fatSecs)) * $bps) + ($c * 2)) $v }
}
function ClusAbs([int]$c) { $p + (($dataStart + (($c - 2) * $spc)) * $bps) }
function FreeCluster {
    for ($c = 2; $c -lt $clusters + 2; $c++) { if ((FatGet $c) -eq 0) { return $c } }
    throw 'no free cluster'
}
function PutEntry([int]$abs, [string]$name83, [int]$attr) {
    $bytes = [System.Text.Encoding]::ASCII.GetBytes($name83.PadRight(11).Substring(0, 11))
    [Array]::Copy($bytes, 0, $b, $abs, 11)
    $b[$abs + 11] = [byte]$attr
    for ($i = 12; $i -lt 32; $i++) { $b[$abs + $i] = 0 }
}

# The slot after the last used one: a first byte of 0 ends a directory, so a
# freed (0xE5) slot is not reused and the new names follow the old ones.
$rootAbs = $p + ($rootStart * $bps)
$slot = 0
while ($slot -lt $rootEnts -and $b[$rootAbs + $slot * 32] -ne 0) { $slot++ }
if ($slot + $RootCount -gt $rootEnts) { throw "root holds $rootEnts entries, $slot used" }
for ($i = 0; $i -lt $RootCount; $i++) { PutEntry ($rootAbs + ($slot + $i) * 32) ('R{0:D2}     TXT' -f $i) 0x20 }
Write-Host "root: $RootCount files from slot $slot, $($slot + $RootCount) of $rootEnts used"

if ($SubCount -gt 0) {
    $sub = -1
    for ($i = 0; $i -lt $rootEnts; $i++) {
        $a = $rootAbs + $i * 32
        if ([System.Text.Encoding]::ASCII.GetString($b, $a, 11) -eq $SubDir.PadRight(11) -and ($b[$a + 11] -band 0x10)) { $sub = U16 ($a + 26); break }
    }
    if ($sub -lt 2) { throw "no directory $SubDir in the root" }
    $c = $sub
    $chain = @($c)
    while ((FatGet $c) -lt 0xFFF8) { $c = FatGet $c; $chain += $c }
    $k = 0
    while ($k -lt $perClus -and $b[(ClusAbs $c) + $k * 32] -ne 0) { $k++ }
    for ($i = 0; $i -lt $SubCount; $i++) {
        if ($k -ge $perClus) {
            $n = FreeCluster
            FatSet $c $n
            FatSet $n 0xFFFF
            $za = ClusAbs $n
            for ($z = 0; $z -lt $spc * $bps; $z++) { $b[$za + $z] = 0 }
            $c = $n; $chain += $n; $k = 0
        }
        PutEntry ((ClusAbs $c) + $k * 32) ('S{0:D2}     TXT' -f $i) 0x20
        $k++
    }
    Write-Host "/$SubDir : $SubCount files, chain $($chain -join ' -> ')"
}

[System.IO.File]::WriteAllBytes($Out, $b)
