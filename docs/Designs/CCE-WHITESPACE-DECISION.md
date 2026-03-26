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

---

## What We Think (But Want Cam's Input On)

**Option A (boundary normalization) is the leading candidate.** The reasoning:

1. Codex is building its own world. Inside that world, the meaningful whitespace
   characters are newline (structure) and space (separation). Tab is "some spaces"
   — a rendering decision, not a semantic one.

2. External text is a boundary concern. The boundary translates. This is the
   same principle as CCE itself — Unicode exists at the I/O edges, not inside.

3. Evicting letters for machine codes (Option B) is philosophically backwards.

4. Multi-byte (Option C) is the right long-term answer for Tier 1 but is scope
   we don't need today.

5. Loud failure (Option D) should happen regardless of which option we pick.
   Silent NUL is a bug.

**But we haven't fully enumerated the tradeoffs.** Specifically:

- What other file formats break under lossy tab conversion? (CSV? TSV is
  obvious. What else?)
- Are there Codex programs today that use `"\t"` in string literals? (Search
  the samples and prelude.)
- Does the `show` builtin or any debug output use tabs for alignment?
- What does the Go emitter use for indentation? The Python emitter?
- If a Codex program is a code generator that emits Go/Python/YAML, how
  does it produce a tab when it needs one?
- Is `code-to-char 9` actually ergonomic enough for the "intentional tab"
  case, or does it need a named constant like `unicode-tab`?

---

## Immediate Bug (independent of this decision)

Unmapped characters producing NUL bytes is silent data corruption. This should
be fixed regardless of the TAB/CR decision:

1. Change `_Cce.FromUnicode` to map unmapped chars to a visible sentinel
   instead of `(char)0`.
2. Consider a compile-time diagnostic for string literals containing
   characters outside Tier 0.

---

## Action Items

- [ ] Cam: Read this, think about it, enumerate any tradeoffs we're missing
- [ ] Cam: Search codebase for `"\t"` and `"\r"` usage in .codex source
- [ ] Cam: Check Go and Python emitter indentation strategy
- [ ] All: Decide on Option A/B/C/D or a hybrid
- [ ] All: Fix silent NUL regardless of decision (Option D is orthogonal)
