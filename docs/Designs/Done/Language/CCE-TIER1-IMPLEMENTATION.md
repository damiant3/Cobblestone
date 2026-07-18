# CCE Tier 1 Implementation Plan

**Date**: 2026-06-14
**Status**: Phases 1-4 implemented
**Motivation**: IoT deployment in Europe requires multilingual prose in
literate source. Tier 0's 16 accented + 15 Cyrillic slots cover English
and basic French/German/Russian. European markets need complete Latin
Extended, full Cyrillic, Greek, and Arabic at minimum.

---

## Current State

Tier 0 is fully implemented: 128 single-byte codes, range-check
classification, bitmask case conversion, 256-byte rodata tables, I/O
boundary converters. The compiler and runtime operate entirely in CCE
Tier 0 internally.

The CCE-DESIGN.md spec (Done/) defines the full multi-tier framing
(identical to UTF-8: `0xxxxxxx` / `110xxxxx 10xxxxxx` / etc.) and the
Tier 1 block assignments. No production code implements Tier 1+.

---

## What Changes

### 1. CCE.codex -- encoding and decoding multi-byte sequences

Add functions to the foreword:

```
cce-encode-length : Integer -> Integer
```
Given a CCE code point, returns the byte count (1-4).

```
cce-encode : Integer, List Integer -> List Integer
```
Encode a CCE code point into bytes, appending to the accumulator.

```
cce-decode : List Integer, Integer -> (Integer, Integer)
```
Decode a CCE code point starting at byte offset. Returns (code-point,
next-offset). First byte determines length via prefix bits.

```
cce-is-continuation : Integer -> Boolean
```
True when `(byte & 0xC0) == 0x80`. Used for self-synchronization --
scanning forward to the next character boundary.

```
cce-char-start : Integer -> Boolean
```
True when byte is NOT a continuation. Every character starts here.

These are pure functions over byte values. They do not depend on tables.

### 2. CCE.codex -- Tier 1 classification

Tier 1 code points (128-2175) occupy 2-byte sequences. Classification
needs to work on decoded code points, not raw bytes:

```
cce-is-letter-ext : Integer -> Boolean
```
True for code points in the Latin Extended, Cyrillic Extended, Greek,
Arabic, Hebrew, and Devanagari blocks (0x000-0x2FF in Tier 1 space).

```
cce-script : Integer -> Integer
```
Returns the script block index from the high bits of a Tier 1 code
point. `(cp - 128) >> 7` gives the block. No table lookup.

The existing Tier 0 functions (`is-letter`, `is-digit`, etc.) remain
unchanged -- they operate on single-byte code points and are the fast
path. A new `is-letter-any` function checks Tier 0 first, then Tier 1.

### 3. CCE.codex -- expanded conversion tables

The current `cce-to-unicode-table` has 128 entries. Tier 1 needs up to
2048 more. Two approaches:

**Option A: Full table (2176 entries).** Simple. ~8KB rodata. Every
code point has a direct Unicode mapping. `to-unicode` becomes
`list-at table cp` with a bounds check. Inverse table is a 128KB
sparse array (Unicode max 0x10FFFF mapped to CCE) -- too large.
Inverse uses a sorted list + binary search.

**Option B: Block-based tables.** Each Tier 1 script block stores an
offset into Unicode space. `to-unicode cp = block-base[block(cp)] +
(cp - block-start)`. Most blocks are contiguous in Unicode, so this
is a small table (~24 entries) plus an offset. Inverse is the same
offset math in reverse with a script-identification step.

**Recommendation: Option B.** The block structure is already in the
design doc. The tables stay small (~200 bytes). The math is two
operations. Inverse conversion identifies the script from the Unicode
range, computes the block offset, and produces the CCE code point.

### 4. Lexer.codex -- multi-byte character support

The lexer currently reads one byte at a time via `char-at source offset`.
For Tier 1+, the lexer needs to:

a. **Detect multi-byte sequences.** When the current byte has bit 7
   set (`>= 128`), decode the full CCE sequence to get the code point.

b. **Classify the code point.** Use `cce-is-letter-ext` to determine
   if it's a valid identifier character. Tier 1 letters are valid in
   identifiers and prose.

c. **Advance by the correct byte count.** A 2-byte character advances
   offset by 2, not 1.

The key change is in `scan-ident-end` and the main `lex-next` dispatch.
Currently:

```
if is-letter-code c then scan-ident-end ...
```

Becomes:

```
if is-letter-code c then scan-ident-end ...
else if c >= 128 then
  let (cp, next) = cce-decode source offset
  in if cce-is-letter-ext cp then scan-ident-ext ...
  else error ...
```

Prose sections (which are already free-form text between `We say:` and
section markers) naturally support multi-byte characters because the
parser treats them as opaque text. Only identifiers and keywords need
the lexer change.

### 5. Text representation -- no change needed

The internal `Text` type is a byte sequence with a length. It is already
encoding-agnostic -- it stores whatever bytes the source contains. CCE
multi-byte sequences are just byte sequences. `text-length` returns byte
count (not character count), which is consistent with how the compiler
uses it (offset arithmetic, not character counting).

A `text-char-count` function can be added to the foreword for user code
that needs logical character count (walk the bytes, skip continuations).

### 6. I/O boundary converters -- expand Unicode mapping

The x86-64 emitter has `__cce_to_unicode` and `__unicode_to_cce` helpers
that currently operate on single-byte lookups from 256-byte rodata tables.

For Tier 1+, these become:

a. **`__cce_to_unicode`**: Decode the multi-byte CCE sequence, then
   apply the block-based offset to get the Unicode code point.

b. **`__unicode_to_cce`**: Identify the Unicode range, map to CCE
   script block, compute the CCE code point, encode as multi-byte
   sequence.

The helpers move from table lookups to computed mappings. The rodata
footprint stays small (block-offset table, ~200 bytes).

### 7. Rodata and constants

Current constants in X86_64State.codex:
- `cce-to-unicode-rodata-offset : Integer = 0` (128 bytes)
- `unicode-to-cce-rodata-offset : Integer = 128` (128 bytes)

After Tier 1: add a block-offset table (~48 bytes for 12 blocks x
4 bytes each). Total rodata increase: ~48 bytes. Negligible.

---

## Implementation Order

### Phase 1: Foreword functions (no compiler change)

Add to CCE.codex:
- Multi-byte encode/decode
- Tier 1 classification
- Block-based Unicode conversion
- `text-char-count`

This is testable in isolation -- write test programs that encode/decode
Tier 1 characters and verify round-trip through Unicode conversion.

### Phase 2: Lexer (compiler change, requires seed rebuild)

Modify Lexer.codex to handle multi-byte sequences in identifiers. This
is the gate: once the lexer accepts Tier 1 characters, source files can
use them.

This changes the compiler binary. Requires:
1. Edit Lexer.codex
2. Build with current seed (Tier 0 only) -- the lexer change is in Codex
   source which the seed can compile
3. Prove fixed point -- the new compiler compiles itself identically
4. Rebuild seed

The new seed accepts Tier 1 in source but the source itself still uses
only Tier 0 (the compiler's own source has no non-ASCII identifiers).
So the fixed-point property is preserved.

### Phase 3: I/O converters (compiler change, requires seed rebuild)

Update the x86-64 emitter's `__cce_to_unicode` and `__unicode_to_cce`
helpers to handle multi-byte sequences. This lets runtime I/O correctly
convert Tier 1 characters.

### Phase 4: Tier 1 block assignment table

Produce the definitive Tier 1 assignment. The design doc specifies
block ranges but not the exact character-to-code-point mapping within
each block. For Latin Extended, this means deciding which 128 characters
fill the block and in what order (frequency-ranked within script, per
the design principles).

This is a data task, not a code task. The output is a table that feeds
into CCE.codex and the rodata initializer.

---

## Scope Boundaries

### In scope
- Tier 1 (2-byte, 2048 code points): Latin Extended, Cyrillic Extended,
  Greek, Arabic, Hebrew, Devanagari, Thai/Lao, Korean jamo, top-512 CJK,
  Japanese kana, math symbols, common emoji
- Lexer support for Tier 1 identifiers
- I/O conversion for Tier 1
- European language coverage (French, German, Spanish, Portuguese,
  Italian, Dutch, Polish, Czech, Swedish, Norwegian, Danish, Finnish,
  Greek, Russian, Ukrainian, Turkish, Arabic)

### Out of scope (Tier 2/3)
- Full CJK (21,000+ characters) -- Tier 2
- Rare scripts -- Tier 2
- Private use / expansion -- Tier 3
- Bidirectional text rendering
- Locale-specific collation
- Normalization (NFC/NFD) beyond precomposed forms

### Not changing
- Tier 0 layout (frozen, load-bearing)
- Text type representation (byte sequence)
- Compiler's own source encoding (stays Tier 0)
- Fixed-point property
- Build pipeline

---

## Risk Assessment

**Memory**: Rodata grows ~48 bytes. Tier 1 conversion tables are
computed, not stored. No heap impact.

**Time complexity**: Lexer adds one branch per byte (check bit 7).
Fast path (Tier 0, bit 7 clear) is unchanged -- one extra comparison.
Slow path (Tier 1) decodes 2 bytes and does a range check. No loops,
no table lookups.

**Fixed point**: The compiler's own source uses only Tier 0. The new
lexer accepts but does not require Tier 1. Self-compilation produces
identical output because the input is identical. Fixed point preserved.

**Compatibility**: Existing Codex source files are valid. New source
files with Tier 1 characters require the new compiler. Old compilers
reject them with a lexer error (unknown byte >= 128).

---

## Definitive Block Assignment (Phase 4)

The rodata uses a 16-entry slice table indexed by `(offset >> 7)`.
Each entry is the Unicode code point at the start of that 128-char
slice. Lookup: `unicode = slice_base[offset >> 7] + (offset & 127)`.

| Slice | CCE Range | Unicode Range | Script | Coverage |
|-------|-----------|---------------|--------|----------|
| 0 | 128-255 | U+00C0-U+013F | Latin Extended (lower) | FR DE ES PT IT IS SE NO DK |
| 1 | 256-383 | U+0140-U+01BF | Latin Extended (upper) | PL CZ SK HU HR SI TR RO |
| 2 | 384-511 | U+0400-U+047F | Cyrillic | RU UK BG SR MK |
| 3 | 512-639 | U+0370-U+03EF | Greek | EL |
| 4 | 640-767 | U+0600-U+067F | Arabic | AR FA UR |
| 5 | 768-895 | U+0590-U+060F | Hebrew | HE YI |
| 6 | 896-1023 | U+0900-U+097F | Devanagari | HI SA MR NE |
| 7 | 1024-1151 | U+0E00-U+0E7F | Thai + Lao | TH LA |
| 8 | 1152-1279 | U+1100-U+117F | Korean Hangul jamo | KO |
| 9-12 | 1280-1791 | U+4E00-U+4FFF | Top CJK | ZH (75% coverage) |
| 13-14 | 1792-2047 | U+3040-U+30FF | Japanese Hiragana+Katakana | JA |
| 15 | 2048-2175 | U+2200-U+227F | Math symbols + operators | Technical |

Latin Extended occupies 256 slots (2 slices) to cover both Latin-1
Supplement (0xC0-0xFF: uppercase accented) and Latin Extended-A
(0x100-0x1BF: Polish, Czech, Hungarian, Turkish, Romanian, etc.).
Emoji moved to Tier 2 (3 bytes) -- not needed in identifiers or prose.

European language coverage is complete: every character needed for
all 27 EU member state languages is in Tier 1 at 2 bytes per character.
