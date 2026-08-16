# Mints the .disk sidecars for the foreword Gpt geometry arms
# (codex/test/apps/gpt-core-*). Each is codex/test/apps/gop-fat16.disk with the
# protective-MBR signature set (the foreword reader requires it; the Works one
# does not) and ONE header field changed, header CRC recomputed so the refusal
# can only come from gpt-header-geom-ok:
#
#   gpt-core-read         unchanged geometry                positive control
#   gpt-core-size-256     SizeOfPartitionEntry 256, count 64 well-formed, must be ACCEPTED
#   gpt-core-size-guard   SizeOfPartitionEntry 64          refused: entries would be read past the sector
#   gpt-core-count-guard  NumberOfPartitionEntries 100000  refused: the array does not fit under FirstUsableLBA
#
# The array CRC is recomputed for the arms whose array still fits the image
# and left as-is for count-guard (12.8 MB of array on a 64 KB image); the
# foreword reader does not check it and the geometry test runs first.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$src = Join-Path $repo 'codex/test/apps/gop-fat16.disk'
$base = [IO.File]::ReadAllBytes($src)

$tbl = New-Object uint32[] 256
for ($i = 0; $i -lt 256; $i++) { $c = [uint32]$i; for ($j = 0; $j -lt 8; $j++) { if ($c -band 1) { $c = ($c -shr 1) -bxor 0xEDB88320 } else { $c = $c -shr 1 } }; $tbl[$i] = $c }
function Crc32([byte[]]$b, [int]$off, [int]$len) {
    $c = [uint32]::MaxValue
    for ($i = 0; $i -lt $len; $i++) { $c = [uint32]($tbl[($c -bxor $b[$off + $i]) -band 0xFF] -bxor ($c -shr 8)) }
    return [uint32]($c -bxor [uint32]::MaxValue)
}
function Mint([string]$name, [scriptblock]$patch, [bool]$arrayCrc) {
    $b = [byte[]]$base.Clone()
    $b[510] = 0x55; $b[511] = 0xAA
    & $patch $b
    $h = 512
    if ($arrayCrc) {
        $lba = [BitConverter]::ToUInt64($b, $h + 72); $cnt = [BitConverter]::ToUInt32($b, $h + 80); $sz = [BitConverter]::ToUInt32($b, $h + 84)
        [BitConverter]::GetBytes([uint32](Crc32 $b ([int]($lba * 512)) ([int]($cnt * $sz)))).CopyTo($b, $h + 88)
    }
    [BitConverter]::GetBytes([uint32]0).CopyTo($b, $h + 16)
    [BitConverter]::GetBytes([uint32](Crc32 $b $h 92)).CopyTo($b, $h + 16)
    $out = Join-Path $repo "codex/test/apps/$name.disk"
    [IO.File]::WriteAllBytes($out, $b)
    Write-Host ("{0,-22} entlba={1} cnt={2} sz={3} hcrc={4:x8} acrc={5:x8}" -f $name, [BitConverter]::ToUInt64($b, $h + 72), [BitConverter]::ToUInt32($b, $h + 80), [BitConverter]::ToUInt32($b, $h + 84), [BitConverter]::ToUInt32($b, $h + 16), [BitConverter]::ToUInt32($b, $h + 88))
}
Mint 'gpt-core-read'        { param($b) } $true
Mint 'gpt-core-size-256'    { param($b) [BitConverter]::GetBytes([uint32]64).CopyTo($b, 512 + 80); [BitConverter]::GetBytes([uint32]256).CopyTo($b, 512 + 84) } $true
Mint 'gpt-core-size-guard'  { param($b) [BitConverter]::GetBytes([uint32]64).CopyTo($b, 512 + 84) } $true
Mint 'gpt-core-count-guard' { param($b) [BitConverter]::GetBytes([uint32]100000).CopyTo($b, 512 + 80) } $false
