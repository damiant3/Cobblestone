# Mints the .disk sidecars for the foreword Gpt geometry arms
# (codex/test/apps/gpt-core-*). Each is codex/test/apps/gop-fat16.disk with the
# protective-MBR signature set (the foreword reader requires it; the Works one
# does not), AlternateLBA and LastUsableLBA set to the image's last LBA (the
# base leaves both 0, which its own Works reader never reads), and ONE header
# field changed, header CRC recomputed so the refusal can only come from the
# field under test:
#
#   gpt-core-read         unchanged geometry                positive control
#   gpt-core-size-256     SizeOfPartitionEntry 256, count 64 well-formed, must be ACCEPTED
#   gpt-core-size-guard   SizeOfPartitionEntry 64          refused: entries would be read past the sector
#   gpt-core-count-guard  NumberOfPartitionEntries 100000  refused: the array does not fit under FirstUsableLBA
#   gpt-core-lba-wide     PartitionEntryLBA 2^63-1         refused: an added array length would wrap
#   gpt-core-alt-wide     AlternateLBA 2^63-1              refused: the disk claims more sectors than the medium
#   gpt-core-part-wide    entry 0 EndingLBA 2^63-1         refused: the partition ends past the medium
#   gpt-core-part-low     entry 0 StartingLBA 1            refused: the partition starts before FirstUsableLBA
#   gpt-core-part-high    LastUsableLBA 126                refused: entry 0 ends at 127, past LastUsableLBA
#   gpt-core-last-high    LastUsableLBA 128                refused: LastUsableLBA past AlternateLBA
#   gpt-core-hdr-crc      a DiskGUID byte flipped AFTER the header CRC   refused: header CRC
#   gpt-core-array-crc    a byte of entry 5 flipped, array CRC left as it was   refused: array CRC
#
# The array CRC is recomputed for the arms whose array still fits the image
# and left as-is for count-guard (12.8 MB of array on a 64 KB image) and
# lba-wide; the geometry test runs before either CRC is read.
param([string]$OutDir = 'codex/test/apps', [string]$Source = 'codex/test/apps/gop-fat16.disk')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$src = if ([IO.Path]::IsPathRooted($Source)) { $Source } else { Join-Path $repo $Source }
$outRoot = if ([IO.Path]::IsPathRooted($OutDir)) { $OutDir } else { Join-Path $repo $OutDir }
if (-not (Test-Path -PathType Leaf $src) -and (Test-Path -PathType Leaf "$src-mint")) {
    $minted = @(& pwsh -NoProfile -File (Join-Path $repo 'build/mint-test-disk.ps1') -Recipe "$src-mint")
    if ($LASTEXITCODE -ne 0) { throw "cannot mint ${Source}: $($minted[-1])" }
    $src = $minted[-1]
}
$base = [IO.File]::ReadAllBytes($src)

$tbl = New-Object uint32[] 256
for ($i = 0; $i -lt 256; $i++) { $c = [uint32]$i; for ($j = 0; $j -lt 8; $j++) { if ($c -band 1) { $c = ($c -shr 1) -bxor 0xEDB88320 } else { $c = $c -shr 1 } }; $tbl[$i] = $c }
function Crc32([byte[]]$b, [int]$off, [int]$len) {
    $c = [uint32]::MaxValue
    for ($i = 0; $i -lt $len; $i++) { $c = [uint32]($tbl[($c -bxor $b[$off + $i]) -band 0xFF] -bxor ($c -shr 8)) }
    return [uint32]($c -bxor [uint32]::MaxValue)
}
function Mint([string]$name, [scriptblock]$patch, [bool]$arrayCrc, [scriptblock]$afterCrc = $null) {
    $b = [byte[]]$base.Clone()
    $b[510] = 0x55; $b[511] = 0xAA
    $lastLba = [uint64]($b.Length / 512 - 1)
    [BitConverter]::GetBytes($lastLba).CopyTo($b, 512 + 32)
    [BitConverter]::GetBytes($lastLba).CopyTo($b, 512 + 48)
    & $patch $b
    $h = 512
    if ($arrayCrc) {
        $lba = [BitConverter]::ToUInt64($b, $h + 72); $cnt = [BitConverter]::ToUInt32($b, $h + 80); $sz = [BitConverter]::ToUInt32($b, $h + 84)
        [BitConverter]::GetBytes([uint32](Crc32 $b ([int]($lba * 512)) ([int]($cnt * $sz)))).CopyTo($b, $h + 88)
    }
    [BitConverter]::GetBytes([uint32]0).CopyTo($b, $h + 16)
    [BitConverter]::GetBytes([uint32](Crc32 $b $h 92)).CopyTo($b, $h + 16)
    if ($afterCrc) { & $afterCrc $b }
    $out = Join-Path $outRoot "$name.disk"
    [IO.File]::WriteAllBytes($out, $b)
    Write-Host ("{0,-22} entlba={1} cnt={2} sz={3} hcrc={4:x8} acrc={5:x8}" -f $name, [BitConverter]::ToUInt64($b, $h + 72), [BitConverter]::ToUInt32($b, $h + 80), [BitConverter]::ToUInt32($b, $h + 84), [BitConverter]::ToUInt32($b, $h + 16), [BitConverter]::ToUInt32($b, $h + 88))
}
Mint 'gpt-core-read'        { param($b) } $true
Mint 'gpt-core-size-256'    { param($b) [BitConverter]::GetBytes([uint32]64).CopyTo($b, 512 + 80); [BitConverter]::GetBytes([uint32]256).CopyTo($b, 512 + 84) } $true
Mint 'gpt-core-size-guard'  { param($b) [BitConverter]::GetBytes([uint32]64).CopyTo($b, 512 + 84) } $true
Mint 'gpt-core-count-guard' { param($b) [BitConverter]::GetBytes([uint32]100000).CopyTo($b, 512 + 80) } $false
$wide = [BitConverter]::GetBytes([uint64]9223372036854775807)
Mint 'gpt-core-lba-wide'    { param($b) $wide.CopyTo($b, 512 + 72) } $false
Mint 'gpt-core-alt-wide'    { param($b) $wide.CopyTo($b, 512 + 32) } $true
Mint 'gpt-core-part-wide'   { param($b) $wide.CopyTo($b, [int]([BitConverter]::ToUInt64($b, 512 + 72) * 512) + 40) } $true
Mint 'gpt-core-part-low'    { param($b) [BitConverter]::GetBytes([uint64]1).CopyTo($b, [int]([BitConverter]::ToUInt64($b, 512 + 72) * 512) + 32) } $true
Mint 'gpt-core-part-high'   { param($b) [BitConverter]::GetBytes([uint64]126).CopyTo($b, 512 + 48) } $true
Mint 'gpt-core-last-high'   { param($b) [BitConverter]::GetBytes([uint64]128).CopyTo($b, 512 + 48) } $true
Mint 'gpt-core-hdr-crc'   { param($b) } $true { param($b) $b[512 + 56] = $b[512 + 56] -bxor 1 }
Mint 'gpt-core-array-crc'   { param($b) $e = [int]([BitConverter]::ToUInt64($b, 512 + 72) * 512) + 5 * [int][BitConverter]::ToUInt32($b, 512 + 84); $b[$e + 20] = $b[$e + 20] -bxor 1 } $false
