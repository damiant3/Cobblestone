# Walk a FAT16 volume inside a raw disk image and answer the four questions a
# returned flight asks: what files are there, do their chains overlap, are any
# clusters allocated to nothing, and do the two FAT copies agree.
#
# Reads a FILE, never a device. Dump the device first (build/dump-usb.ps1) so
# the evidence is frozen and the analysis is repeatable: a mounted FAT volume
# gets System Volume Information written into it by Windows before anyone has
# read a byte, and those writes allocate clusters, which is the exact evidence
# question 3 asks about.
#
# RUN IT ON THE PRE-FLIGHT IMAGE FIRST. Both defects found while writing it
# were caught that way and neither was visible on the returned stick alone: a
# root-sector count that read every cluster one sector late, and a walk that
# never descended into subdirectories and so called all 1336 clusters of
# EFI/BOOT/BOOTX64.EFI orphans. A build-img image answers "none" to question 3.
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$Image,
    # Partition start in sectors. The ESP of a build-img image is at 2048.
    [int]$PartLba = 2048
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$img = [IO.File]::ReadAllBytes((Resolve-Path $Image).Path)
$base = $PartLba * 512
function U16([int]$o) { [BitConverter]::ToUInt16($img, $o) }
function U32([int]$o) { [BitConverter]::ToUInt32($img, $o) }
# PowerShell's [int] cast ROUNDS, so [int](($n + 511) / 512) is not a ceiling:
# it answers 33 root sectors where the answer is 32, and every cluster then
# reads one sector late (reek, measured 2026-08-09).
function CeilDiv([long]$n, [long]$d) { $q = [long]$n + $d - 1; [int](($q - ($q % $d)) / $d) }

$bps      = U16 ($base + 11)
$spc      = $img[$base + 13]
$reserved = U16 ($base + 14)
$numFats  = $img[$base + 16]
$rootEnts = U16 ($base + 17)
$totS16   = U16 ($base + 19)
$fatSz    = U16 ($base + 22)
$totS32   = U32 ($base + 32)
$totS     = if ($totS16 -ne 0) { $totS16 } else { $totS32 }

$rootSectors = CeilDiv ($rootEnts * 32) $bps
$fat0Lba     = $PartLba + $reserved
$rootLba     = $fat0Lba + $numFats * $fatSz
$dataLba     = $rootLba + $rootSectors
$maxCluster  = [int](($totS - ($reserved + $numFats * $fatSz + $rootSectors)) / $spc) + 1

Write-Host "BPB: bps=$bps spc=$spc reserved=$reserved fats=$numFats rootEnts=$rootEnts fatSz=$fatSz totalSectors=$totS"
Write-Host "LBA: fat0=$fat0Lba fat1=$($fat0Lba + $fatSz) root=$rootLba data=$dataLba  maxCluster=$maxCluster"
Write-Host ""

# A boxed UInt16 2 and a boxed Int32 2 are DIFFERENT hashtable keys, which
# reported every owned cluster as an orphan on the first run of this script.
function FatEntry([int]$copy, [int]$cl) { [int](U16 (($fat0Lba + $copy * $fatSz) * 512 + $cl * 2)) }
function ClusterLba([int]$cl) { $dataLba + ($cl - 2) * $spc }

$fatMismatch = 0
for ($cl = 0; $cl -le $maxCluster; $cl++) { if ((FatEntry 0 $cl) -ne (FatEntry 1 $cl)) { $fatMismatch++ } }
Write-Host "FAT copies: $(if ($fatMismatch -eq 0) { 'identical' } else { "$fatMismatch entries DIFFER" })"

$script:owner    = @{}
$script:files    = [System.Collections.Generic.List[object]]::new()
$script:overlaps = [System.Collections.Generic.List[string]]::new()

function Get-Chain([int]$first, [string]$who) {
    $chain = [System.Collections.Generic.List[int]]::new()
    $cl = [int]$first
    while ($cl -ge 2 -and $cl -lt 0xFFF8 -and $chain.Count -lt 70000) {
        $chain.Add($cl)
        if ($script:owner.ContainsKey($cl)) {
            $script:overlaps.Add("cluster $cl claimed by BOTH '$($script:owner[$cl])' and '$who'")
        } else { $script:owner[$cl] = $who }
        $cl = FatEntry 0 $cl
    }
    ,$chain
}

function Walk-Dir([int]$firstCluster, [string]$path) {
    # The root directory is a fixed run of sectors; a subdirectory is a chain.
    if ($firstCluster -eq 0) {
        $sectors = @(); for ($i = 0; $i -lt $rootSectors; $i++) { $sectors += ($rootLba + $i) }
    } else {
        $chain = Get-Chain $firstCluster $path
        $sectors = @(); foreach ($c in $chain) { for ($i = 0; $i -lt $spc; $i++) { $sectors += ((ClusterLba $c) + $i) } }
    }
    foreach ($sec in $sectors) {
        for ($e = 0; $e -lt ($bps / 32); $e++) {
            $o = $sec * 512 + $e * 32
            $b0 = $img[$o]
            if ($b0 -eq 0) { return }
            if ($b0 -eq 0xE5) { continue }
            $attr = $img[$o + 11]
            if (($attr -band 0x0F) -eq 0x0F) { continue }
            $raw = [System.Text.Encoding]::ASCII.GetString($img, $o, 11)
            # Test the RAW field, not the formatted name: ".." formats to ".."
            # + "." and TrimEnd('.') then eats every dot, leaving an empty
            # string that no '..' guard matches, so the walk recursed into the
            # parent until the call stack gave out.
            if ($raw -eq '.          ' -or $raw -eq '..         ') { continue }
            $name = ($raw.Substring(0, 8).Trim() + '.' + $raw.Substring(8, 3).Trim()).TrimEnd('.')
            $full = if ($path -eq '') { $name } else { "$path/$name" }
            $first = [int](U16 ($o + 26))
            $size  = [int](U32 ($o + 28))
            if (($attr -band 0x10) -ne 0) { Walk-Dir $first $full; continue }
            $chain = Get-Chain $first $full
            $script:files.Add([pscustomobject]@{
                Path = $full; Size = $size; First = $first
                Clusters = $chain.Count; Needed = (CeilDiv $size ($bps * $spc))
                FirstLba = if ($chain.Count) { ClusterLba $first } else { 0 }
            })
        }
    }
}

Walk-Dir 0 ''

Write-Host ""
Write-Host "Files:"
$script:files | Format-Table -AutoSize | Out-String | Write-Host

$bad = 0
foreach ($f in $script:files) {
    if ($f.Clusters -ne $f.Needed) {
        Write-Host "  !! '$($f.Path)': chain is $($f.Clusters) clusters, size needs $($f.Needed)" -ForegroundColor Red
        $bad++
    }
}
if ($bad -eq 0) { Write-Host "Chain lengths: every file matches its size." }

if ($script:overlaps.Count -eq 0) { Write-Host "Overlaps: none." }
else { Write-Host "Overlaps: $($script:overlaps.Count)" -ForegroundColor Red; $script:overlaps | ForEach-Object { Write-Host "  !! $_" -ForegroundColor Red } }

$orphans = [System.Collections.Generic.List[int]]::new()
for ($cl = 2; $cl -le $maxCluster; $cl++) {
    if ((FatEntry 0 $cl) -ne 0 -and (-not $script:owner.ContainsKey($cl))) { $orphans.Add($cl) }
}
if ($orphans.Count -eq 0) { Write-Host "Allocated to nothing: none." }
else {
    Write-Host "Allocated to nothing: $($orphans.Count) clusters" -ForegroundColor Red
    Write-Host "  first 40: $(($orphans[0..([math]::Min(39, $orphans.Count - 1))]) -join ', ')"
}
