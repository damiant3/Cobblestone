# CCE Whitespace Decision — TAB and CR

**Date**: 2026-03-26
**Author**: Agent Linux + Damian (morning design session)
**Status**: Open decision — needs team input before implementation

---

## The Problem We Noticed

The CCE Tier 0 revision trimmed whitespace from 8 slots to 3: NUL (0), LF (1),
space (2). This dropped TAB (Unicode 9) and CR (Unicode 13) from the encoding.

**Consequence**: Any Codex string literal containing `"\t"` or `"\r"` silently
gets NUL bytes instead of the intended characters. The `UnicharToCce(9)` call
returns 0 (unmapped). This is silent data corruption.

The same applies at I/O boundaries: `read-file` on a TSV file or a Windows CRLF
file silently loses tabs and carriage returns.

---

## Characters We Removed

| Character | Unicode | Old CCE | New CCE | Used by |
|-----------|---------|---------|---------|---------|
| TAB       | 9       | 3       | unmapped | TSV files, Makefiles, Go/gofmt, YAML, legacy indentation |
| CR        | 13      | 2       | unmapped | Windows line endings (CRLF) |
| NBSP      | 160     | 5       | unmapped | Typography only |
| Thin space | 8201   | 6       | unmapped | Typography only |
| Narrow NBSP | 8239  | 7       | unmapped | Typography only |

NBSP, thin space, and narrow NBSP are clean cuts — no working code depends on
them. TAB and CR are the question.

---

## Options On The Table

### Option A: Boundary normalization (current leading candidate)

**TAB → two spaces** on input. **CR → stripped** on input.

The `_Cce.FromUnicode` boundary layer normalizes before encoding. Codex
programs never see tabs or CRs internally. On output, we don't reverse it —
two spaces stay as two spaces.

Pros:
- Zero encoding changes. No slots spent. No evictions.
- Encoding stays clean and principled.
- TAB is a rendering hint, not a character. Two spaces *is* the content.
- CR is half of a Windows line ending. Strip it. Nobody wants bare CRs.
- Programs that genuinely need to emit a tab byte for interop can use
  `code-to-char 9` through the Unicode boundary — intentional and explicit.

Cons:
- **Lossy.** Round-trip fidelity is broken for tab-containing files. Read a
  TSV, write it back — tabs are now spaces. The file is semantically different.
- Makefile generation requires explicit Unicode interop for the tab character.
- Any format that distinguishes tabs from spaces (TSV, Makefiles, some YAML)
  needs special handling at the boundary.
- The "two spaces" choice is arbitrary. Why not four? Why not preserve the
  column intent?

Open questions:
- Should the boundary emit a diagnostic when it encounters a tab? ("Note:
  TAB converted to spaces at line 47")
- Should `write-file` have an option to convert spaces back to tabs for
  specific formats?
- Is two spaces the right mapping, or should it be configurable?

### Option B: Evict two Cyrillic letters

Move the two least-common Cyrillic characters (п, у) to future Tier 1 and
put TAB and CR at positions 126-127.

Pros:
- Full fidelity for tabs and CRs. No silent conversion.
- Escape sequences `\t` and `\r` work as expected.
- No encoding shifts — everything else keeps its byte value.

Cons:
- **Evicts living letters in favor of dead machine instructions.** This is
  the ASCII mistake — prioritizing American teletype mechanics over the
  world's writing systems. The whole point of frequency-sorted encoding is
  that people's letters matter more than machines' habits.
- Sets a precedent: every future compat concern can evict another letter.
- п (pe) and у (u) are not rare — they're common Russian letters.
  п appears in practically every Russian sentence.
- The whitespace classification range breaks: `is-whitespace(b) = b <= 2`
  no longer catches TAB and CR at 126-127. Becomes a disjunction.

### Option C: Multi-byte Tier 1 encoding

TAB, CR, and everything else outside Tier 0 get a two-byte CCE representation
(e.g., `[0x80, byte]` for Tier 1 characters).

Pros:
- Architecturally correct. No evictions. Full fidelity. Extensible.
- CJK, extended Latin, emoji, and everything else gets a path forward.
- Tier 0 stays pristine.

Cons:
- Significant scope. Every string operation that assumes one-byte-per-character
  needs to handle variable width, or Tier 1 is boundary-only.
- The self-hosted compiler's string processing (lexer, parser, emitter) all
  assume `char-at s i` gives you one character. Multi-byte breaks this.
- Not needed today for the compiler's own operation — only for arbitrary
  text processing.

### Option D: Loud failure instead of silent NUL

Don't map unmapped characters to NUL. Map them to a visible sentinel (`?` or
a dedicated CCE "unmapped" glyph) and optionally emit a diagnostic.

Pros:
- Buys time. No encoding changes needed.
- The data loss is visible, not silent. Programmer sees `?` instead of
  mysterious NULs.
- Independent of the TAB/CR decision — this should probably happen regardless.

Cons:
- Still lossy. Doesn't solve the round-trip problem.
- `?` is already a valid character (CCE 68). Need a different sentinel or
  a side-channel diagnostic.

### Option E: Two compilation modes — forward-looking and backward-looking

Same compiler, same source language, one flag:

**`codex build --encoding unicode`** — backward-looking. Unicode on the inside,
no CCE conversion, no I/O boundaries, full fidelity for every character on earth.
`"\t"` is a tab. `read-file` returns the bytes as-is. `is-letter` calls
`char.IsLetter`. This is the mode for programs that live in the barbarian world:
data pipelines reading CSVs, code generators emitting Go/Python/YAML, anything
that processes external text.

**`codex build --encoding cce`** — forward-looking. CCE on the inside, boundaries
at I/O, frequency-sorted classification, the full vision. This is the mode for
programs that live in the Codex world: the self-hosted compiler, Codex.OS, agents,
anything that doesn't need to care about the outside.

The difference is entirely in the emitter layer:
- What the runtime preamble looks like (with or without `_Cce` class)
- How string literals are encoded (CCE-escaped or plain Unicode)
- How builtins like `is-letter`, `is-digit` are emitted (range checks or
  `char.IsLetter`)
- Whether I/O builtins wrap in conversion calls

The source language, parser, type checker, IR — all identical. The flag affects
code generation only.

Pros:
- **The TAB/CR question dissolves.** Unicode mode has them. CCE mode doesn't
  need them. No evictions. No silent corruption. No compromises.
- The user chooses based on their program's world, not the encoding's limits.
- The self-hosted compiler bootstraps in CCE mode (it IS a Codex-native program).
  A data processing tool compiles in unicode mode. Each gets the right tradeoffs.
- TAB and CR aren't a birth defect we carry forward OR a capability we lose.
  They're just irrelevant in one mode and available in the other.
- Relatively low maintenance cost — the emitter already has the CCE and non-CCE
  code paths from the migration. This is formalizing what was previously a
  before/after into a side-by-side.
- The compiler can default to one mode and evolve: maybe unicode today (safe),
  cce tomorrow (once the ecosystem is ready), cce-only eventually (when Codex.OS
  is the host and Unicode is the foreign encoding).

Cons:
- Two code paths in the emitter to maintain. Every new builtin needs both a CCE
  and Unicode emission. This is manageable but not free.
- Testing surface doubles for the emitter layer. Every sample needs to compile
  and run correctly in both modes.
- Risk of drift: one mode gets all the attention and the other rots. Need
  discipline to keep both green.
- "Which mode should I use?" is a question new users have to answer. Defaults
  matter.

Open questions:
- What's the default? Unicode (safe, backward-compatible) or CCE (forward-looking,
  the vision)?
- Does the flag live in `codex.project.json` per-project, or is it per-invocation?
- Can a CCE-mode program call into a unicode-mode library, or vice versa? (Probably
  not without boundary conversion — but is that a real use case?)
- How much of the emitter is actually duplicated? The CCE migration touched ~30
  builtin emission sites. That's the maintenance surface.

---

## What We Think (But Want Cam's Input On)

## Decision (2026-03-26)

**CCE-only. Forward path. No dual mode.**

After reviewing all five options and Cam's Janus reflection, the decision is:

1. **Option A (boundary normalization) for the immediate fix.** TAB → spaces,
   CR → stripped at the `read-file` / `read-line` boundary. The Lexer's `\t`
   and `\r` escape sequences produce spaces / nothing respectively, or are
   removed from the language.

2. **Option D (loud failure) as an independent fix.** Silent NUL for unmapped
   characters is a bug. Fix it with a visible sentinel and/or compile-time
   diagnostic for string literals containing characters outside Tier 0.

3. **Option E is rejected.** Dual encoding mode is structural debt that compounds
   forever. The 34% CCE overhead is temporary — it disappears when native backends
   become primary. Two encoding paths in the compiler would split the fixed point,
   double the emitter surface area, and carry forward indefinitely. The cost of
   the transition is paid once; the cost of dual encoding is paid every day.

4. **Option B is rejected.** Evicting Cyrillic letters for teletype commands is
   the ASCII mistake repeated. People's letters matter more than machines' habits.

5. **Option C (multi-byte Tier 1) remains on the roadmap** for CJK, extended
   Latin, and everything else outside Tier 0. Not blocking anything today.

6. **Backward compatibility with Unicode is someone else's rope.** A barbarian
   intelligence layer that bridges Unicode-native programs to Codex is legitimate
   future work — but it's not our work. We're building the AI intelligence layer,
   not the 1999 compatibility layer. The bridge gets built when someone needs it,
   by the person who needs it.

**The principle**: We are building for 2999, not 1999. The compiler thinks in
its own encoding. The transition costs are weather. The mountain doesn't move.

---

## Immediate Bug (independent of this decision)

Unmapped characters producing NUL bytes is silent data corruption. This should
be fixed regardless of the TAB/CR decision:

1. Change `_Cce.FromUnicode` to map unmapped chars to a visible sentinel
   instead of `(char)0`.
2. Consider a compile-time diagnostic for string literals containing
   characters outside Tier 0.

---

## Remaining Work

- [ ] Fix silent NUL: change `_Cce.FromUnicode` to produce a visible sentinel
      for unmapped characters, not `(char)0`
- [ ] Add compile-time diagnostic for string literals containing chars outside
      Tier 0 (catches `"\t"` and `"\r"` at compile time instead of silently
      producing garbage)
- [ ] Boundary normalization: TAB → spaces, CR → strip in `_Cce.FromUnicode`
- [ ] Remove or deprecate `\t` and `\r` escape sequences from the Lexer
- [ ] Track perf trend: median self-compile time per session, alarm if
      monotonically increasing
