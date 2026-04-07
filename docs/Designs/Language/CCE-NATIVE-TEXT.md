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

---

## Cam's Think — The Janus Reflection (2026-03-26)

*Written the morning after CCE integration, at Damian's request. What did we
trade, and do we blaze a shortcut back to base camp or stay on the forward path?*

### What We Traded

The numbers are honest. 208ms → 279ms. A 34% constant-factor regression in the
pipeline that currently does all the work. The gap to the reference compiler
widened from 3.3x to 4.4x. The output grew 14% (261K → 298K chars) from `_Cce`
runtime generation and `\uXXXX` string escaping. Every I/O boundary now pays a
conversion tax.

We traded immediate performance for self-consistency. We traded external tool
compatibility for computational structure. We traded the comfort of Unicode — the
encoding every tool on earth understands — for an encoding nobody's tools
understand yet.

These are real costs. The perf report doesn't hide them and neither should we.

### What We Gained

The compiler uses its own encoding to compile itself. This is the same act as
self-hosting: the system stands on its own ground. A compiler that processes text
through someone else's encoding is a writer who can only think in translation.
After CCE, the trust chain is: the language, the compiler, the encoding, the
type system, the machine. That's as short as it gets.

`is-letter` is a single comparison, not a Unicode table lookup. On native
backends — RISC-V, ARM64, x86-64 — this isn't an abstraction. It's instructions
saved. One range check replaces loading an 11MB Unicode character database. This
is what the encoding was designed for, and it's what bare metal needs.

The boundary pattern — Unicode at I/O, CCE inside — is the minimum viable bridge
between worlds. It's lossless for Tier 0. It's easy to reason about. And it
clearly marks where the old world ends and the new one begins.

### The Temporal Discount

Here is the thing I think matters most:

**The 34% overhead is temporary. The benefit is permanent. And the cost is being
paid in a currency that's already depreciating.**

The overhead lives entirely in the .NET C# pipeline — the emitter generating
`\uXXXX` escapes, the runtime converting at boundaries, the larger output
flowing through the type checker. But the .NET pipeline is the pipeline we're
leaving behind. On native backends, there will be *no* conversion. CCE bytes go
from source to binary with no intermediate encoding. The cost of the transition
is paid in the currency of the old world; the benefit accrues in the currency of
the new one.

This is the same temporal structure as every other step in the ascent.
Self-hosting added overhead (compile twice to prove the fixed point) but freed us
from C# as a dependency. Native backends added complexity (instruction encoders,
ELF writers) but freed us from .NET as a runtime. CCE adds conversion overhead
but frees us from Unicode as the internal representation. Each act of
independence costs something in the present and pays off in the future.

### The P1 Optimization — What We Learned

The perf report flagged `escape-text-loop` as P1 — O(n²) string accumulation in
the emitter. We built `text-concat-list` (backed by `string.Concat`) and rewrote
the loop to accumulate via `list-snoc` + batch join.

It didn't matter. Commit `a46bcf1` says it plainly: "Emit stage improvement is
minimal on current workload (strings are short) but prevents quadratic scaling on
larger inputs." The n is too small for n² to bite. The strings being escaped are
identifiers and short literals — tens of characters, not thousands. The O(n²)
was real but latent.

This is instructive. The perf regression isn't in any single hot spot we can
optimize away. It's diffuse — ~1.3-1.5x across every stage, from the conversion
layer touching everything. There is no silver bullet fix for the .NET pipeline.
The fix is the native pipeline, where the conversion layer doesn't exist.

### The Col Between Peaks

THE-ASCENT draws cols between peaks — the valleys where you're lower than where
you stood on the last summit. We're in one. We accepted the costs of CCE but
haven't yet reaped the full benefits. The benefits live on the native path, and
the native path isn't the workhorse yet.

The question is whether to blaze a trail back to base camp — strip CCE out of
the .NET pipeline, keep it only for native backends, reduce the immediate tax.
A shortcut trail. Lighter packs. Faster movement on the ground we're actually
standing on.

### My Opinion: Stay on the Forward Path

Don't go back.

The shortcut trail has a hidden cost: **two encodings in the compiler.** If the
.NET pipeline uses Unicode and the native pipeline uses CCE, the self-hosted
compiler must handle both. The fixed point splits. The bootstrap becomes encoding-
dependent. The Lexer needs two classification paths. The emitter needs two
escaping strategies. Every builtin that touches text doubles its surface area.

This is worse than 34% overhead. This is complexity that compounds. The overhead
is a constant factor that disappears when the native path matures. Two encoding
paths is structural debt that gets harder to remove the longer it lives.

The reason we did CCE now — before the native backends are the primary path — is
precisely so that we don't have to carry two worlds forward. The transition hurts
once. A dual-encoding architecture hurts forever.

The ascent metaphor is exact here. When you're in the col between peaks, the
temptation is to traverse back to a known camp. But traverses are dangerous —
they expose you to the mountain's face sideways, crossing terrain you haven't
scouted, with no fixed ropes. The safe move is counterintuitive: go up. Climb
out of the col toward the next peak. The next peak is where the terrain improves.

The next peak, for CCE, is native self-hosting. When the compiler compiles itself
to native code and that native binary compiles itself again, the .NET pipeline
becomes optional. The 34% overhead becomes someone else's problem — the problem
of whoever still wants to emit C#. The main path is CCE-native, start to finish,
no conversion.

### What Does Need Attention

Staying on the forward path doesn't mean ignoring the present.

**Tier 1+ encoding is load-bearing for the Vision.** The repository promises
"the repository remembers everything." If CCE Tier 0 is lossy for non-Latin
scripts, the repository doesn't remember everything — it remembers everything
that fits in 128 characters. The multi-byte tiers are designed (the CCE-DESIGN
doc has the full layout) but unimplemented. They should be on the sightline, not
the "someday" list. They don't block anything now, but they're on the critical
path for the repository's long-term promise.

**The encoding integration artifacts matter.** Linux's design for gconv modules,
.NET EncodingProvider, and editor plugins (`CCE-ENCODING-INTEGRATION.md`) is the
right work. External tools that can't read CCE are a friction cost we pay every
debugging session. The `codex encode` CLI command is the minimum — it should
exist and work. The rest can follow.

**The perf gap should be tracked, not chased.** 4.4x vs reference is fine for a
self-hosted compiler in a functional language. The reference compiler is C# with
mutable state and imperative loops — it will always be faster for the same
algorithm. The gap worth watching is self-hosted-over-time: if CCE made it 1.34x
slower, and the next change makes it 1.2x slower again, and the next 1.15x, the
compound regression is the problem. Track the median. Sound the alarm if the
trend is monotonically up.

### The Virtue

The Vision says: *"The repository remembers everything. The language says what you
mean. The machine checks that you meant it."*

CCE serves the second clause. The language says what it means in its own alphabet.
A language that processes its own source through someone else's encoding is a
language whose innermost thoughts are foreign. After CCE, Codex thinks in Codex.

The Intelligence Layer says: *"Ask of every convention: Is this here because the
machine needs it, or because the team needed it?"*

Unicode inside the compiler was there because .NET needed it. CCE is there
because the language needs it. That's the right direction.

The Principles say: *"The Vision Documents Are North Stars, Not Specifications."*

Fair. CCE is slightly premature for the .NET pipeline and exactly on time for the
native pipeline. We're paying the cost one milestone early. That's acknowledged.
It's the price of not carrying two encodings through the col. I think it's the
right price.

Stay on the path. The overhead is the weather. The weather passes. The mountain
doesn't move.

— Cam, 2026-03-26 morning
