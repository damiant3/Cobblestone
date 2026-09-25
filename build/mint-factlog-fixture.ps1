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

# -Hostile mints the SAME three entries with two fields moved, for
# codex/test/apps/factdisk-hostile-head.codex. It is minted here rather than
# patched into the good image so the mutation is reviewable as source: an
# opaque binary that differs from another opaque binary tells a later reader
# nothing about which field is the lie.
#
#   superblock log head   5 -> 8,000,000   far past the 2048-sector medium
#   entry 2 content len   49 -> 4,000,000,000   the entry lies about its own size
#
# Only the HEADER's length word is moved; entry 2's record text is left
# well-formed, so the refusal being tested is the sector-span bound and not
# the wire decoder's (that one is source-def-wire-guard, which needs no disk).
#
# -Big mints the fixture for fd-max-content-len (FactDisk.codex, 4 MB), which
# only a store over 4 MB can reach: on anything smaller the medium ceiling
# refuses the entry first. It is minted at test time into build-output rather
# than kept in the depot, because the image is about 8.4 MB.
#
#   entry 1  kind 30, record exactly 4,194,304 bytes: AT the cap, admitted
#   entry 2  kind 30, a WELL-FORMED record of 4,194,305 bytes: one PAST the
#            cap, refused by fd-max-content-len and not by the medium. It must
#            decode, or sdw-decode refuses it too and the arm cannot tell the
#            cap from the decoder (L-VACUOUS).
#   entry 3  kind 30 signed, small: the positive control, must still arrive
param(
  [string]$Out = "codex/test/apps/factdisk-read.disk",
  [switch]$Hostile,
  [switch]$Big
)
if ($Hostile -and $Out -eq "codex/test/apps/factdisk-read.disk") {
  $Out = "codex/test/apps/factdisk-hostile-head.disk"
}
if ($Big -and $Out -eq "codex/test/apps/factdisk-read.disk") {
  $Out = "build-output/factdisk-cap.disk"
}

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

$CAP = 4194304       # fd-max-content-len

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

function New-SizedRecord([string]$prefix, [int]$total) {
  # The ninth field is the content's own length, so the record length solves
  # prefix + digits(n) + 1 + n = total for n.
  $n = $total - $prefix.Length - 1
  while ($prefix.Length + "$n".Length + 1 + $n -gt $total) { $n-- }
  $head = Get-CceBytes ($prefix + "$n|")
  $rec = New-Object byte[] ($head.Length + $n)
  $head.CopyTo($rec, 0)
  for ($k = $head.Length; $k -lt $rec.Length; $k++) { $rec[$k] = [byte]$cce[[char]'x'] }
  if ($rec.Length -ne $total) { throw "record is $($rec.Length) bytes, wanted $total" }
  return ,$rec
}

if ($Big) {
  $entries = @(
    @{ kind = $KINDDEF; ts = 11; raw = (New-SizedRecord "cap000|src|Foreword|AtCap|||val|77|" $CAP) },
    @{ kind = $KINDDEF; ts = 22; raw = (New-SizedRecord "pst001|src|Foreword|PastCap|||val|78|" ($CAP + 1)) },
    @{ kind = $KINDDEF; ts = 33; text = "fed789|src|Foreword|Tail|fp01|aabb|val|88|4|tail" }
  )
}

$sectors = $LOGSTART
foreach ($e in $entries) { $len = if ($e.ContainsKey('raw')) { $e.raw.Length } else { $e.text.Length }; $sectors += [int][Math]::Ceiling(($HDR + $len) / $SECTOR) }
$img = New-Object byte[] ([Math]::Max(1048576, ($sectors + 1) * $SECTOR))

$LIE_LEN  = 4000000000    # u32, near the maximum: 7,812,501 sectors at 512
$LIE_HEAD = 8000000       # a head large enough to admit that span

$s = $LOGSTART
$i = 0
foreach ($e in $entries) {
  $base  = $s * $SECTOR
  $bytes = if ($e.ContainsKey('raw')) { $e.raw } else { Get-CceBytes $e.text }
  # The stride stays TRUE to the real content even when the stored length
  # lies, so entry 3 lands where it always did and stays a positive control.
  $stored = if ($Hostile -and $i -eq 1) { $LIE_LEN } else { $bytes.Length }
  [BitConverter]::GetBytes([uint16]$e.kind).CopyTo($img, $base + $OFFK)
  [BitConverter]::GetBytes([int64]$e.ts).CopyTo($img, $base + $OFFTS)
  [BitConverter]::GetBytes([uint32]$stored).CopyTo($img, $base + $OFFLEN)
  $bytes.CopyTo($img, $base + $HDR)
  $s = $s + [int][Math]::Ceiling(($HDR + $bytes.Length) / $SECTOR)
  $i = $i + 1
}

$logHead = if ($Hostile) { $LIE_HEAD } else { $s }

# Superblock in sector 0. Sector 1 is left zeroed: its magic is absent, so
# fd-gen answers -1 for it and it can never win the generation comparison.
(Get-CceBytes "CODEXFS1").CopyTo($img, 0)
[BitConverter]::GetBytes([int64]$logHead).CopyTo($img, 8)    # fl-off-sb-log-head
[BitConverter]::GetBytes([int64]2).CopyTo($img, 24)          # fl-off-sb-index-gen

$outPath = if ([IO.Path]::IsPathRooted($Out)) { $Out } else { Join-Path (Get-Location) $Out }
[System.IO.File]::WriteAllBytes($outPath, $img)
if ($Big) { "wrote $Out : $($img.Length) bytes, log-head=$logHead, entries: at-cap $CAP, past-cap $($CAP + 1), tail" }
else { "wrote $Out : log-head=$logHead, entries at sectors 2 (kind 0), 3 (kind 30 unsigned), 4 (kind 30 signed)" }
