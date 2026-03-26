# CCE-Native Text — Design Plan

**Date**: 2026-03-25
**Author**: Cam (Claude Code CLI)
**Status**: Proposal — awaiting review

---

## The Decision

Char is CCE. Text is CCE. Unicode dies inside Codex. The boundary is file I/O
and .NET interop — the barbarian translator. Inside the compiler, inside the
language, inside the OS, everything is Codex Character Encoding.

## What CCE Gives Us

From `docs/Designs/CCE-DESIGN.md`:

- 128 characters in Tier 0, frequency-sorted
- Single-comparison classification: `is-whitespace(b) = b <= 7`
- Single-range letter check: `is-letter(b) = b >= 18 && b <= 69`
- Bitmask case conversion: `to-lower(b) = b | 0x1A` (future, needs bitwise ops)
- Digits at positions 8-17: `digit-value(b) = b - 8`

With CCE-native text, the Lexer's `is-letter-code` becomes one range check instead
of two (uppercase + lowercase). `is-digit-code` stays one range check but at a
different offset. Classification is faster, not slower.

## Architecture

### The Representation

C# `string` stays as the backing store. Each `char` position holds a CCE byte
value (0-127) cast to `char`. This means:

- `text-length` → `s.Length` (unchanged)
- `substring` → `s.Substring(i, len)` (unchanged)
- `char-at s i` → `(long)s[(int)i]` (unchanged implementation, CCE value)
- `text-compare` → `string.CompareOrdinal` (unchanged, compares CCE bytes)
- `==` on Text → `string.Equals` (unchanged, compares CCE bytes)

No new data structure. No byte arrays. No reimplementation of string operations.
The strings just contain different values.

### The Boundaries

Unicode exists at exactly four points:

| Boundary | Direction | Function |
|----------|-----------|----------|
| File read | Unicode → CCE | `read-file`, `read-line`, source loading |
| File write | CCE → Unicode | `write-file` |
| Console output | CCE → Unicode | `print-line`, `show` |
| String literals | Compile-time | Emitter encodes literal text as CCE |

Everything between these boundaries is pure CCE. No conversion, no lookup tables,
no per-character overhead.

### The Conversion Functions

Two core functions, already in the prelude:

- `unicode-to-cce : Integer -> Integer` — 300-line lookup, covers Tier 0 (128 chars)
- `cce-to-unicode : Integer -> Integer` — 180-line lookup, covers Tier 0

For the boundary layer, we need string-level versions:

```
text-to-cce : UnicodeText -> Text       -- convert each char via unicode-to-cce
text-to-unicode : Text -> UnicodeText   -- convert each char via cce-to-unicode
```

In the C# emitter, these would be emitted as helper methods in the runtime
preamble, called at I/O boundaries. They loop through the string once, converting
each character.

### What `UnicodeText` Means

We do NOT add a `UnicodeText` type to the language. At the Codex level, there is
only `Text` (CCE). The boundary conversion happens in the builtin implementations:

```csharp
// read-file becomes:
var unicode = File.ReadAllText(path);
return ConvertToCce(unicode);

// write-file becomes:
File.WriteAllText(path, ConvertToUnicode(cceText));

// print-line becomes:
Console.WriteLine(ConvertToUnicode(cceText));
```

The programmer never sees Unicode. They call `read-file` and get CCE text back.

## Execution Plan

### Phase 1: Runtime Helpers (reference compiler)

Add to the C# emitter's runtime preamble:

```csharp
static string _CceFromUnicode(string s) { ... }
static string _UnicodeFromCce(string s) { ... }
```

These are lookup-table-driven, O(n) per string. Generated from the CCE prelude
mappings.

Wire into builtins:
- `read-file` → `_CceFromUnicode(File.ReadAllText(path))`
- `write-file` → `File.WriteAllText(path, _UnicodeFromCce(cceText))`
- `print-line` → `Console.WriteLine(_UnicodeFromCce(cceText))`
- `read-line` → `_CceFromUnicode(Console.ReadLine())`
- `show` → stays CCE (it's internal representation)
- `run-process` → convert args and result at boundary

### Phase 2: String Literal Encoding (reference compiler)

The emitter currently emits `"hello"` as a C# string literal. Change to emit
CCE-encoded values:

```csharp
// Before: "hello"
// After:  "\u0019\u0012\u001c\u001c\u0015"
```

Each character in the source literal is converted through `unicode-to-cce` at
compile time. The emitter has the lookup table built in.

### Phase 3: Character Constants and Builtins

Update `is-letter`, `is-digit`, `is-whitespace` to use CCE classification:

```csharp
// is-letter: CCE 18-69 (was char.IsLetter)
(c >= 18L && c <= 69L)

// is-digit: CCE 8-17 (was char.IsDigit)
(c >= 8L && c <= 17L)

// is-whitespace: CCE 0-7 (was char.IsWhiteSpace)
(c <= 7L)
```

`char-code` stays as identity (Char value = CCE byte = long).
`code-to-char` stays as identity.
`char-at` stays as indexing (but now returns CCE byte).
`char-code-at` returns the same as `char-at` followed by `char-code` — CCE byte.

Char literals: `'A'` → the lexer converts Unicode 65 → CCE 46, emits that value.

### Phase 4: Self-Hosted Compiler Migration

The self-hosted compiler's Lexer currently uses Unicode character constants:

```
cc-space = 32        -- Unicode space
cc-equals = 61       -- Unicode '='
cc-lower-a = 97      -- Unicode 'a'
```

These all change to CCE values:

```
cc-space = 4         -- CCE whitespace: space
cc-equals = 85       -- CCE punct: '='
cc-lower-a = 20      -- CCE lower: 'a' (actually 'a' is CCE 20)
```

But wait — with char literals working, these become:

```
cc-space = char-code ' '
cc-equals = char-code '='
```

And the compiler resolves them to CCE values automatically. This is where char
literals pay off — the Lexer becomes encoding-agnostic. You write `'='` and the
compiler knows that means CCE 85.

### Phase 5: Bootstrap

This is the critical phase. The bootstrap has a transitional step:

1. **Reference compiler (Unicode)** compiles self-hosted source → Stage 1
   - Stage 1 contains CCE conversion helpers
   - Stage 1 string literals are CCE-encoded by the reference compiler
   - Stage 1 reads source files and converts Unicode → CCE

2. **Stage 1** compiles self-hosted source → Stage 2
   - Source is read as CCE (converted on load)
   - All processing is in CCE
   - Output string literals are CCE-encoded

3. **Stage 2** compiles self-hosted source → Stage 3
   - Same as Stage 2 — fixed point here

Fixed point is Stage 2 = Stage 3. Stage 1 is the bridge.

The bootstrap tool needs to be updated to do the Unicode → CCE conversion when
loading source for the benchmark, since it feeds source text directly to the
compiled self-hosted compiler.

### Phase 6: Cleanup

- Remove `cc-newline = 10`, `cc-space = 32`, etc. — replace with `char-code '\n'`,
  `char-code ' '`
- CCE prelude functions (`is-cce-letter` etc.) become the standard `is-letter` etc.
- Remove the `is-cce-` prefix — they're not CCE-specific anymore, they're just
  character classification
- `text-compare` now compares CCE bytes — sort order is frequency-based, not
  alphabetical. Binary search still works (consistent ordering) but sorted output
  won't be alphabetical. Consider whether `text-compare` should do CCE-to-Unicode
  for user-facing sorting, or if CCE ordering is fine internally.

## Risks

### CCE sort order ≠ alphabetical order

CCE is frequency-sorted: e=18, t=19, a=20, o=21, i=22. Alphabetical order is
a=20, b=37, c=29, d=27, e=18. Our binary search in TypeEnv, Scope, etc. uses
`text-compare` which would now compare CCE bytes. The sorted order changes, but
binary search still works — it just finds things in CCE order instead of
alphabetical order.

If this matters for user-facing output (e.g., error messages listing names), we
can sort by converting to Unicode first. Internally, CCE order is fine.

### Characters outside Tier 0

CCE Tier 0 covers 128 characters. What about emoji, CJK, extended Latin? The
current `unicode-to-cce` returns 0 (null) for unmapped characters. We need a
strategy:

- **Option A**: Unmapped characters become CCE 0 (lossy). Simple, fast. Bad for
  source files containing comments in non-Tier-0 scripts.
- **Option B**: Multi-byte CCE encoding for Tier 1+. The design doc mentions this
  but it's not implemented.
- **Option C**: Escape sequences. Unmapped characters stored as `\u{XXXX}` in CCE
  text. Preserves information but breaks fixed-width assumptions.

Recommendation: Start with Option A. Codex source is ASCII + Tier 0 punctuation.
Comments in other scripts lose fidelity but the compiler still works. Tier 1+
encoding is future work.

### Performance

The conversion cost is O(n) per string at the boundary. For `read-file` loading
a 174K source file, that's one pass through 174K characters — negligible compared
to the 208ms compile time. For `print-line` on short strings, also negligible.

String literals are converted at compile time, zero runtime cost.

The Lexer gets faster because CCE classification is cheaper than Unicode
classification (one range check vs. two).

### The `++` operator on Text

String concatenation (`++`) stays as `string.Concat` or `+` in C#. Since both
operands are CCE-encoded, the result is CCE-encoded. No issue.

## Session Plan

I'd execute this over 2-3 sessions:

**Session A** (next session):
- Phase 1 + 2: Runtime helpers and string literal encoding in reference compiler
- Phase 3: Character builtins
- Build, test — existing tests will need updates (string comparisons change)

**Session B**:
- Phase 4: Self-hosted compiler Lexer migration to CCE constants / char literals
- Phase 5: Bootstrap and fixed-point verification

**Session C** (cleanup):
- Phase 6: Remove Unicode vestiges, rename CCE functions
- Update docs, syntax reference

## Encoding Evolution

CCE Tier 0 is frequency-sorted based on today's corpus data. In 100 years the
world's writing might shift — more Chinese, more Arabic, a script that doesn't
exist yet. The encoding must be a parameter, not a constant.

### What makes this possible

The encoding is defined in exactly two artifacts:

1. **`prelude/CCE.codex`** — the lookup tables (`cce-to-unicode`, `unicode-to-cce`)
2. **The emitter's compile-time table** — used to encode string literals

The compiler doesn't know or care that CCE byte 18 is 'e'. It just indexes into
strings and compares bytes. If the tables change, the compiler recompiles itself
with the new encoding and everything shifts consistently.

### The process

**Regeneration trigger**: When the global character frequency distribution shifts
enough that the current encoding wastes more than N% of Tier 0 slots on
low-frequency characters. Measured against a representative corpus (web text,
source code, published books, messaging).

**Regeneration steps**:

1. Analyze corpus → produce frequency-ranked character list
2. Generate new `CCE.codex` prelude with updated lookup tables
3. Assign a version number (CCE v1, v2, ...)
4. Recompile the self-hosted compiler with the new tables
5. Fixed-point verification passes → the encoding is self-consistent

**Migration path for existing content**:

CCE-encoded files need a version tag. Options:

- **Magic byte prefix**: First byte of a CCE file is the encoding version.
  Version 1 = current. Reader checks the version and applies the right
  decode table.
- **Filesystem metadata**: The OS tracks encoding version per file. Codex.OS
  has capability-enforced metadata — this is natural.
- **Self-describing**: Each CCE version's prelude contains the previous version's
  tables, so `read-file` can detect and auto-convert.

The key property: **old content is never unreadable.** A CCE v2 system can read
CCE v1 files because v1's tables ship with v2. Content migrates forward lazily —
read in v1, write back in v2.

### What stays fixed across versions

- Tier 0 is always 128 bytes (single-byte characters)
- Classification ranges are always contiguous (whitespace, digits, lower, upper,
  punct, accented, extended)
- The structural property holds: `is-letter(b) = b >= LETTER_START && b <= LETTER_END`
- The compiler's character classification is always two comparisons or fewer

What changes: which characters land in which slots, and what the slot boundaries
are. The classification functions become parameterized by the version's range
boundaries — but since ranges are contiguous, it's still just constants.

### Frequency data source

The initial CCE Tier 0 is sorted by frequency across English, with extensions for
accented Latin and Cyrillic. A future version might:

- Weight CJK ideographs into Tier 0 if Chinese becomes dominant
- Promote Arabic/Devanagari characters if South Asian writing grows
- Demote rarely-used punctuation to Tier 1

The decision is data-driven: run the corpus analysis, look at the numbers, decide
if the current encoding is still optimal. If the top 128 characters cover 99.5%
of real-world text, the encoding is good. If coverage drops below 95%, it's time
to re-sort.

This is a governance decision, not a compiler decision. The compiler just needs
the tables. The tables come from the data. The data comes from the world.

## Resolved Questions

- **Sort order**: CCE order everywhere. Collation is a user-level concern, not a
  compiler concern. If an agent wants alphabetical sorting, it handles that.
- **`text-compare`**: Compares CCE bytes. Fast, consistent, done.
- **Console**: Exists as a debugging crutch, not a design commitment. `print-line`
  converts to Unicode because the host terminal expects it. When Codex.OS is the
  host, there is no console — there's an agent.

## Decisions (2026-03-25)

1. **Tier 0 only for the compiler.** The compiler is Tier 0. Period. Anything
   beyond Tier 0 is userspace — the Clarifier translates any language to the
   canonical Tier 0 form. We don't sacrifice compiler performance because someone
   wrote their source in Elvish. (And yes, Codex in Tolkien's Elvish is a real
   thing we're doing — via the multi-language syntax layer, translated from
   English and back.)

2. **Encoding version tag**: Filesystem metadata. Files are a transitional concept
   — Codex.OS will move past them. While we still have files, the OS's
   capability-enforced metadata tracks the encoding version. BOM as fallback for
   foreign filesystems.

3. **Test updates**: Brute force. It's mechanical work, not design work.

4. **Collation/sort order**: CCE order everywhere. User-facing sorting is a
   userspace concern handled by the agent.

5. **Console**: A debugging crutch for the transition. Not a design commitment.
