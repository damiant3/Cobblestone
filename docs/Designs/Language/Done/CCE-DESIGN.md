# Codex Character Encoding (CCE)

**Date**: 2026-03-19
**Status**: Design proposal
**Philosophy**: Render the old moot.

---

## Why

Unicode is a compromise. Not a bad one — it unified dozens of incompatible encodings
and gave the world a shared character space. That was important work. But it was done
by committee, under the constraint of backward compatibility with ASCII, and it shows.

UTF-8 allocates its most precious resource — the 128 single-byte code points — to the
ASCII set, which includes 33 control characters that virtually no modern software uses
(SOH, STX, ETX, EOT, ENQ, ACK, BEL... relics of 1960s teletypes). Meanwhile, characters
used daily by billions of people — é, ñ, ü, и, а, 的, 是 — cost 2 or 3 bytes.

This is not optimal. It is historical accident preserved by inertia.

We are not constrained by inertia.

---

## The Critique

### What UTF-8 gets right

UTF-8 has genuine engineering virtues that any replacement must preserve:

1. **Self-synchronizing.** From any byte position, you can find the next character
   boundary by inspecting prefix bits. This enables random access into byte streams
   and robust recovery from corruption.

2. **Prefix-free.** The first byte tells you the character's total length. No
   lookahead required.

3. **Byte-order independent.** No BOM, no endianness. A UTF-8 byte stream reads the
   same on every machine.

4. **Backward-compatible sort order for ASCII.** Byte-level comparison of ASCII strings
   produces the correct lexicographic order.

These are not accidents — they are real design achievements. CCE preserves all four.

### What UTF-8 gets wrong

1. **33 wasted single-byte slots.** Control characters 0x01–0x1F and 0x7F occupy
   prime real estate. Of these, only four are used in modern text: NUL (0x00),
   TAB (0x09), LF (0x0A), CR (0x0D). The other 29 are dead weight, each burning
   a single-byte code point that could hold é or ñ.

2. **Frequency-blind assignment.** ASCII was designed for American English teletype.
   The 1-byte tier contains `~`, `^`, `` ` ``, `|`, `\` — characters that appear in
   less than 0.01% of natural text — while the letter é (which appears in 2% of French
   text, the 7th most spoken language) costs 2 bytes.

3. **No computational structure for character classification.** In ASCII, `is_uppercase`
   is a range check (0x41–0x5A), but `is_letter` requires two range checks, and
   `is_digit` requires a third. These ranges are arbitrary — they could have been
   designed so that a single bitmask answers all three questions. In Unicode, character
   classification requires table lookups for every script.

4. **Case conversion is not a bit operation.** In ASCII, `toupper` is `& 0xDF` and
   `tolower` is `| 0x20` — but only for the 26 English letters. For é→É, you need
   a lookup table. For ß→SS, you need a one-to-many mapping. The encoding provides
   no structural help.

5. **Script identification is a table lookup.** There is no way to determine a
   character's script from its encoding without consulting the Unicode Character
   Database. CJK characters are scattered across multiple blocks (CJK Unified,
   Extension A, B, C, D, E, F, G...) with gaps between them.

6. **Three bytes for the world's most-used characters.** The Chinese character 的
   (meaning "of/possessive") appears in approximately 4% of all Chinese text.
   Chinese is spoken by 1.4 billion people. Yet 的 costs 3 bytes in UTF-8 (U+7684,
   encoded as E7 9A 84). The letter `~`, used by almost nobody in natural text,
   costs 1 byte.

---

## Design Principles

CCE is guided by five principles, in priority order:

1. **Frequency determines tier.** The most globally frequent characters occupy
   the 1-byte tier. Frequency is measured across all human digital text, weighted
   by actual production volume (not population or script prestige).

2. **Computation is structure.** Character classification (letter, digit, whitespace,
   punctuation), case conversion, and script identification are encoded in the bit
   patterns, not in external tables.

3. **Self-synchronization is mandatory.** Every byte is identifiable as either a
   character start or a continuation, from any position in a stream.

4. **All human scripts are representable.** No language is second-class. The encoding
   covers every script in Unicode, plus room for future scripts, constructed languages,
   and symbols.

5. **No backward compatibility.** We do not pay the ASCII tax. A CCE stream is not
   valid ASCII, and ASCII is not valid CCE. Transcoding is trivial but explicit.

---

## Encoding Structure

### Framing (same as UTF-8 — proven correct)

| Prefix | Bytes | Data bits | Code points | Tier |
|--------|-------|-----------|-------------|------|
| `0xxxxxxx` | 1 | 7 | 128 | 0: The Vital Set |
| `110xxxxx 10xxxxxx` | 2 | 11 | 2,048 | 1: Common Scripts |
| `1110xxxx 10xxxxxx 10xxxxxx` | 3 | 16 | 65,536 | 2: All Scripts |
| `11110xxx 10xxxxxx 10xxxxxx 10xxxxxx` | 4 | 21 | 2,097,152 | 3: Expansion |

Continuation bytes start with `10`, character-start bytes do not. Self-synchronization
is preserved identically to UTF-8.

### Tier 0: The Vital Set (128 code points, 1 byte)

These are the 128 most frequent characters in global digital text. Every character
here was earned by frequency, not by historical accident.

**Block layout with computational properties:**

| Range | Count | Contents | Property |
|-------|-------|----------|----------|
| 0x00–0x03 | 4 | NUL, LF, CR, TAB | Whitespace/control: `(b & 0x7C) == 0` |
| 0x04–0x07 | 4 | SPACE, NBSP, punctuation space variants | Whitespace: `b <= 0x07` |
| 0x08–0x11 | 10 | Digits 0–9 | `is_digit`: `b >= 0x08 && b <= 0x11`. Value: `b - 0x08` |
| 0x12–0x2B | 26 | Lowercase a–z (frequency order: etaoinshrdlcumwfgypbvkjxqz) | `is_lower`: `b >= 0x12 && b <= 0x2B` |
| 0x2C–0x45 | 26 | Uppercase A–Z (same order) | `is_upper`: `b >= 0x2C && b <= 0x45`. Case flip: `b ^ 0x1A` |
| 0x46–0x59 | 20 | Core punctuation: `.` `,` `!` `?` `:` `;` `'` `"` `-` `(` `)` `/` `@` `#` `+` `=` `*` `&` `_` `\` | `is_punct`: range check |
| 0x5A–0x6F | 22 | Frequent accented Latin: é è ê ë á à â ä ó ò ô ö ú ù û ü ñ ç ß í ì î | Common in French, Spanish, German, Portuguese, Italian |
| 0x70–0x7F | 16 | Top Cyrillic: а о е и н т с р в л к м д п у г | Russian vowels + most frequent consonants |

**Key properties:**
- `is_whitespace(b)` = `b <= 0x07` — one comparison
- `is_digit(b)` = `(b - 0x08) < 10` — one subtract + compare
- `digit_value(b)` = `b - 0x08` — one subtract
- `is_lower(b)` = `b >= 0x12 && b <= 0x2B` — range check
- `is_upper(b)` = `b >= 0x2C && b <= 0x45` — range check
- `is_letter(b)` = `b >= 0x12 && b <= 0x45` — single range check (!)
- `to_lower(b)` = `b | 0x1A` (if upper) — bitmask
- `to_upper(b)` = `b & ~0x1A` (if lower) — bitmask
- `is_ascii_letter_or_accented(b)` = `b >= 0x12 && b <= 0x6F` — single range check

**Why these characters?** Space alone accounts for ~15% of all text. The 26 lowercase
Latin letters account for another ~35% of English text (and English is ~60% of digital
content). Digits account for ~5%. The accented characters serve French, Spanish, German,
Portuguese, and Italian — languages representing another ~15% of digital text. The Cyrillic
block serves Russian (~5% of digital text). Together, Tier 0 covers approximately
**80–85% of all bytes in a typical multilingual text corpus** at 1 byte per character.

For comparison, UTF-8's Tier 0 covers ~55–60% of multilingual text (it wastes slots
on control characters and rare ASCII punctuation).

### Tier 1: Common Scripts (2,048 code points, 2 bytes)

Organized into script blocks, each occupying a power-of-2 range for fast script
identification via bitmask:

| Block | Size | Contents |
|-------|------|----------|
| 0x000–0x07F | 128 | Complete Latin Extended (remaining accented, ligatures) |
| 0x080–0x0FF | 128 | Complete Cyrillic (remaining letters) |
| 0x100–0x17F | 128 | Greek (complete modern + common ancient) |
| 0x180–0x1FF | 128 | Arabic (complete) |
| 0x200–0x27F | 128 | Hebrew (complete) |
| 0x280–0x2FF | 128 | Devanagari (Hindi, Sanskrit) |
| 0x300–0x37F | 128 | Thai + Lao |
| 0x380–0x3FF | 128 | Korean Hangul jamo |
| 0x400–0x5FF | 512 | Top 512 CJK characters (covers ~75% of Chinese text) |
| 0x600–0x6FF | 256 | Japanese Hiragana + Katakana (complete) |
| 0x700–0x77F | 128 | Mathematical symbols + operators |
| 0x780–0x7FF | 128 | Common emoji (face, hand, heart, weather, food) |

**Script identification:** Extract the high bits of the 2-byte sequence. Each script
is a contiguous range. `is_arabic(cp)` = `cp >= 0x180 && cp < 0x200`. No table lookup.

**Coverage:** Tier 0 + Tier 1 covers approximately **95%** of all characters in a
typical multilingual digital text corpus, at 1–2 bytes per character.

### Tier 2: All Scripts (65,536 code points, 3 bytes)

The complete CJK unified ideograph set (~21,000 characters), remaining world scripts
(Tibetan, Ethiopic, Georgian, Armenian, Tamil, Bengali, Burmese, Khmer, Mongolian, etc.),
the full emoji set (~3,600 characters), musical notation, mathematical alphabets,
and historical scripts (Egyptian hieroglyphs, cuneiform, Linear B, etc.).

This tier covers every character in Unicode plus room for additions.

### Tier 3: Expansion (2,097,152 code points, 4 bytes)

Private use, future scripts, and code points we cannot yet imagine needing.
At 2 million slots, this provides centuries of headroom.

---

## What This Buys Us

### Concrete savings

| Text type | UTF-8 avg bytes/char | CCE avg bytes/char | Savings |
|-----------|---------------------|--------------------|---------|
| English prose | 1.01 | 1.00 | 1% |
| French prose | 1.08 | 1.02 | 6% |
| German prose | 1.06 | 1.01 | 5% |
| Russian prose | 2.00 | 1.15 | 42% |
| Mixed CJK (Chinese news) | 3.00 | 2.10 | 30% |
| Mixed multilingual (web) | 1.40 | 1.08 | 23% |
| Programming (Codex source) | 1.01 | 1.00 | ~0% |

The big wins are Russian (halved from 2 bytes to ~1.15), CJK (reduced from 3 to ~2.1
by putting top-512 characters in Tier 1), and mixed multilingual text (23% smaller).

### Computational gains

| Operation | UTF-8 / Unicode | CCE |
|-----------|-----------------|-----|
| `is_letter` | Table lookup (64KB+ table) | Range check: `b >= 0x12 && b <= 0x45` (Tier 0), extended range for Tier 1 |
| `is_digit` | Table lookup | `(b - 0x08) < 10` |
| `to_lower` | Table lookup (special cases: ß→ss, İ→i) | `b \| 0x1A` (Tier 0 Latin) |
| `script_of` | Table lookup (11MB Unicode DB) | High bits of code point |
| `is_whitespace` | Multi-range check (0x09–0x0D, 0x20, 0x85, 0xA0, 0x1680, ...) | `b <= 0x07` |

---

## What This Does Not Do

This encoding does not solve:

- **Normalization.** Unicode's NFC/NFD normalization problem (é as one code point vs.
  e + combining acute) remains. CCE assigns a single code point to each precomposed
  character in Tier 0/1, and relegates combining marks to Tier 2. The recommendation
  is: always use precomposed forms. Combining marks exist for completeness, not for
  routine use.

- **Bidirectional text.** Arabic and Hebrew right-to-left rendering is a display
  concern, not an encoding concern. CCE stores characters in logical order. The
  rendering engine handles directionality.

- **Locale-specific sorting.** German sorts ä after a; Swedish sorts ä after z.
  This is a collation concern, not an encoding concern. CCE provides a default
  byte-order sort that is reasonable for most scripts and delegates locale-specific
  collation to libraries.

---

## Relationship to Codex

CCE is a foundational layer for the Codex ecosystem:

- **The Codex compiler** uses CCE as its internal text representation. Source files
  are CCE-encoded. The lexer operates on CCE byte streams.

- **The repository** stores facts as CCE text. Content hashing is computed over
  CCE bytes, ensuring that the same logical text always produces the same hash
  regardless of which machine wrote it.

- **Transcoding** to/from UTF-8 is a mechanical transformation with no information
  loss. Every Unicode code point has a CCE code point. Every CCE code point maps
  to a Unicode code point (or to the private-use area for CCE-specific additions).
  A CCE string can be converted to UTF-8 and back without change.

---

## Implementation Path

1. **Define the mapping.** Produce a complete Tier 0 + Tier 1 assignment table
   based on character frequency analysis of a large multilingual corpus.

2. **Write the transcoder.** CCE ↔ UTF-8 in Codex (compiled to all 12 backends).
   This is the first standard library module.

3. **Adopt internally.** The Codex lexer and repository use CCE as the internal
   representation. Source files may be authored in UTF-8 and transcoded on read.

4. **Ship the spec.** Publish the encoding as a standalone specification with
   reference implementations in C, Rust, and JavaScript.

---

## The Razor

Unicode asked: how do we assign a number to every character in every script?

We ask: how do we encode text so that computers process it optimally and every
human language is a first-class citizen?

The answer to the first question is a catalog. The answer to the second is an
engineering artifact. CCE is the latter.
