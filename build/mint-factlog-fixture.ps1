# Mint the fact-log disk fixture that codex/test/apps/factdisk-read.codex reads.
#
# The image is authored from the SPECIFICATION -- Foreword chapter FactLog for
# the sector layout and Foreword chapter SourceDefWire for the record format --
# and NOT by running DiskFacts. That is the whole point: a fixture produced by
# the writer under test would only prove the reader agrees with its own writer,
# which is the failure docs/PM/Active/Stories/BrotliBeatsOpus.md is about.
#
# The tree carried no kind-30 fixture at all. The only two fact-log disks
# (disk-facts-read.disk, disk-facts-load.disk) hold a single kind-0 "hello"
# entry, so the source-definition decode path had never been exercised.
#
# Text is stored one CCE code point per byte, which is what DiskFacts.pack-text
# does (poke-byte of char-code-at) and what FactLog.fl-text reads back. CCE is
# NOT ASCII: it orders letters by English frequency, so e is 13 and a is 15.
# The table below was measured against the seed, not assumed.

param([string]$Out = "codex/test/apps/factdisk-read.disk")

$ErrorActionPreference = 'Stop'

$cce = @{}
'0123456789'.ToCharArray()      | ForEach-Object -Begin { $i = 3 }  -Process { $cce[$_] = $i; $i++ }
$lower = 15,32,24,22,13,28,29,20,17,35,34,23,26,18,16,31,37,21,19,14,25,33,27,36,30,38
'abcdefghijklmnopqrstuvwxyz'.ToCharArray() | ForEach-Object -Begin { $i = 0 } -Process { $cce[$_] = $lower[$i]; $i++ }
$upper = 41,58,50,48,39,54,55,46,43,61,60,49,52,44,42,57,63,47,45,40,51,59,53,62,56,64
'ABCDEFGHIJKLMNOPQRSTUVWXYZ'.ToCharArray() | ForEach-Object -Begin { $i = 0 } -Process { $cce[$_] = $upper[$i]; $i++ }
$cce[[char]' '] = 2; $cce[[char]'|'] = 87; $cce[[char]'/'] = 81
$cce[[char]'.'] = 65; $cce[[char]'-'] = 73; $cce[[char]'_'] = 85; $cce[[char]':'] = 69

function Get-CceBytes([string]$s) {
  $out = New-Object byte[] $s.Length
  for ($i = 0; $i -lt $s.Length; $i++) {
    $c = $s[$i]
    if (-not $cce.ContainsKey($c)) { throw "no CCE code recorded for '$c'; measure it before using it" }
    $out[$i] = [byte]$cce[$c]
  }
  return ,$out
}

$SECTOR = 512
$HDR    = 78          # fl-header-size
$OFFK   = 32          # fl-off-kind
$OFFTS  = 66          # fl-off-timestamp
$OFFLEN = 74          # fl-off-content-len
$LOGSTART = 2         # fl-fact-log-start
$KINDDEF  = 30        # fl-kind-definition

$img = New-Object byte[] 1048576

# Entry 1 is NOT a source definition. It must be STRIDDEN PAST rather than
# decoded, and it is first so that a walk which stops at the first non-30
# entry would find nothing at all.
#
# Entry 2 is an unsigned source definition: sig-hex is empty, so
# sdw-signature decodes to an empty list and fd-admit-work OFFERS it.
#
# Entry 3 is signed: sig-hex "aabb" decodes to two bytes, so it is PUBLISHED
# and lands in the bundle's signed map as well as its fact store.
$entries = @(
  @{ kind = 0;        ts = 11; text = "hello" },
  @{ kind = $KINDDEF; ts = 22; text = "abc123|src|Foreword|Demo|||val|77|12|demo content" },
  @{ kind = $KINDDEF; ts = 33; text = "def456|src|Foreword|Signed|fp01|aabb|val|88|11|signed body" }
)

$s = $LOGSTART
foreach ($e in $entries) {
  $base  = $s * $SECTOR
  $bytes = Get-CceBytes $e.text
  [BitConverter]::GetBytes([uint16]$e.kind).CopyTo($img, $base + $OFFK)
  [BitConverter]::GetBytes([int64]$e.ts).CopyTo($img, $base + $OFFTS)
  [BitConverter]::GetBytes([uint32]$bytes.Length).CopyTo($img, $base + $OFFLEN)
  $bytes.CopyTo($img, $base + $HDR)
  $s = $s + [int][Math]::Ceiling(($HDR + $bytes.Length) / $SECTOR)
}

$logHead = $s

# Superblock in sector 0. Sector 1 is left zeroed: its magic is absent, so
# fd-gen answers -1 for it and it can never win the generation comparison.
(Get-CceBytes "CODEXFS1").CopyTo($img, 0)
[BitConverter]::GetBytes([int64]$logHead).CopyTo($img, 8)    # fl-off-sb-log-head
[BitConverter]::GetBytes([int64]2).CopyTo($img, 24)          # fl-off-sb-index-gen

[System.IO.File]::WriteAllBytes((Join-Path (Get-Location) $Out), $img)
"wrote $Out : log-head=$logHead, entries at sectors 2 (kind 0), 3 (kind 30 unsigned), 4 (kind 30 signed)"
