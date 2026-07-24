# Prose Buildout — Feature-Flagged CPL Integration

**Started**: 2026-05-06
**Agent**: Cam
**CL series**: 1026+

## Goal

Integrate the Codex Prose Language (CPL) grammar into the self-hosted
compiler as a feature-flagged pipeline extension. When the flag is off,
the compiler behaves identically to today — prose lines are skipped, no
new tokens emitted, no new AST nodes. When on, prose lines become
load-bearing: lexed, parsed into CPL sentence forms, and preserved
through the AST into IR as structured metadata.

Annotations (`@kind target body`) become a built-in language construct
gated by the same flag, replacing the sidecar-only design.

## Design: Feature Flag

A `CompileFlags` record threads through the pipeline alongside
`VmProfile`. The mode protocol extends to `CMD PROFILE FLAGS`:

```
TEXT                  → flags = default (prose off)
TEXT prose            → flags = { prose = True }
CDX prose             → flags = { prose = True }
ELF QEMU-11.0.0 prose → flags = { prose = True }
```

```
CompileFlags = record {
  prose : Boolean
}

compile-flags-default : CompileFlags
compile-flags-default = CompileFlags { prose = False }
```

The flag is checked at two points:

1. **Lexer**: When `flags.prose` is True, column-2 lines emit
   `ProseText` tokens instead of being skippable. The `@` character
   at column 2 emits an `AtSign` token for annotations.
2. **Parser**: When `flags.prose` is True, `is-prose-line` dispatches
   to `parse-prose-block` instead of `skip-prose-lines`. When False,
   behavior is identical to today.

No downstream changes (desugar, scope, check, lower, emit) until the
parser produces structured prose nodes that need to survive.

## Baseline HWM (2026-05-06, CL head, seed at ~1.8 MB)

Source: 1,019,839 bytes (selfhost concatenated)

### Phase Heap Marks

| Mark | Address | Delta |
|------|---------|-------|
| h0-start | 8,273,640 | — |
| h1-tokenize | 38,753,440 | 29.1 MB |
| h2-scan | 70,394,554 | 30.2 MB |
| h3-assignments | 70,394,554 | 0 |
| h4-parse | 70,394,554 | 0 |
| h5-desugar | 81,764,104 | 10.8 MB |
| h6-scope | 118,503,973 | 35.0 MB |
| h7-resolve | 189,318,677 | 67.5 MB |

### Phase Deck Usage

| Phase | Deck | Bivy | Origin |
|-------|------|------|--------|
| lex | 29.07 MB | 0.00 MB | 8,273,640 |
| parse | 25.92 MB | 0.00 MB | 38,753,440 |
| desugar | 15.10 MB | 0.00 MB | 65,932,160 |
| scope | 28.63 MB | 0.00 MB | 81,764,104 |
| check | 54.93 MB | 0.00 MB | 111,788,768 |

**Total deck: 153.65 MB** (lex+parse: 54.99 MB)

Text output: 20,606 lines / 956,679 bytes

## Step Plan

Each step is a separate CL. Each must pass sweep + pingpong before
the next begins. Steps 1-4 are the lexer/parser foundation. Steps 5+
are the CPL sentence forms.

### Step 1: CompileFlags record + mode parsing

Add `CompileFlags` to `opening.codex`. Extend `parse-mode-cmd` /
`parse-mode-profile` to also extract flags. Thread the record through
`compile-lex` → `compile-parse` → downstream. Flag defaults to False.
No behavioral change.

**Memory risk**: One 8-byte Boolean per compile. Zero.
**Files**: `codex/opening.codex`

### Step 2: Lexer — ProseText token emission (flag-gated)

When `flags.prose` is True, the lexer emits `ProseText` tokens for
column-2 content instead of producing tokens that the parser later
skips. Add `AtSign` token kind for `@` at prose position.

Current behavior: lexer tokenizes everything uniformly (identifiers,
keywords, operators) regardless of column. The parser checks column
and skips. New behavior (flag on): lexer produces a single `ProseText`
token per prose line, plus `AtSign` tokens for annotation lines.

**Memory risk**: ProseText tokens are one Token record per prose line.
Selfhost source has ~4,800 prose lines × 56 bytes/token = ~262 KB.
Negligible against 29 MB lex deck.
**Files**: `codex/Syntax/Lexer.codex`, `codex/Syntax/Token.codex`

### Step 3: Parser — prose block collection (flag-gated)

When `flags.prose` is True, `parse-top-level` calls `parse-prose-block`
instead of `skip-prose-lines`. Prose blocks are collected as raw text
on the Document and Def nodes (existing `prose` field on AChapter and
IRChapter already exists but is empty string today).

**Memory risk**: Prose text is already in the source string. Collecting
it as substrings (offset+length into existing source) costs ~4,800 ×
16 bytes = ~75 KB. Negligible.
**Files**: `codex/Syntax/Parser.codex`, `codex/Syntax/SyntaxNodes.codex`

### Step 4: Parser — CPL sentence recognition

Parse prose blocks into CPL sentence forms per the ProseGrammarProposal:
- Type Declaration (`A X is a record containing:`)
- Function Declaration (`To V ... gives ...`)
- Constraint (`such that ...`, `where ...`)
- Proof Assertion (`claim:`, `therefore,`)
- Procedure Step (`first,` / `then,` / `finally,`)
- Quantified Statement (`for every ...`)

Non-matching prose is classified as commentary (not load-bearing).
Prose inside `We say:` blocks is parsed strictly; outside is lenient.

**Memory risk**: Parse nodes for ~4,800 lines. Structured nodes are
larger than raw text — estimate 3× overhead = ~225 KB. Still negligible.
**Files**: `codex/Syntax/Parser.codex`, `codex/Syntax/SyntaxNodes.codex`

### Step 5: Annotation syntax (`@kind target body`)

When `flags.prose` is True, `@` at prose position introduces an
inline annotation. The parser produces `AnnotationNode` AST nodes
that carry kind, target, and body. These attach to the nearest
definition above them.

**Memory risk**: Selfhost currently has 0 annotations in source.
Cost is proportional to annotation count, which the author controls.
**Files**: `codex/Syntax/Parser.codex`, `codex/Syntax/SyntaxNodes.codex`,
`codex/Ast/AstNodes.codex`

**Where the records are declared changed in CL 10176.** `ProseBlock`,
`AnnotationNode`, `ProseTransition` and `ProseTemplate` moved from
`Syntax/SyntaxNodes.codex` to `IR/IRChapter.codex`, beside the `IRTextMeta`
that carries them to emission. The parser that BUILDS them is unmoved; the
compiler is one compilation unit, so no citation changed. The reason is the
plug bundle: it takes `IRChapter`'s declarations and deliberately not
`SyntaxNodes`', so a plug could not name `ProseBlock` and therefore could not
read the `pblocks` form off the IR text wire. Both forms now
round-trip.

### Step 6: Prose-notation consistency checking

Post-parse validation: function template names match definition names,
record template fields match notation fields, etc. Diagnostic codes
CDX1101-CDX1104 per V2-NARRATION-LAYER.md.

**Memory risk**: Single pass over existing AST. O(n) in definitions.
**Files**: `codex/Syntax/Parser.codex`, `codex/Core/Diagnostic.codex`

### Step 7: AST/IR prose preservation

Prose nodes survive desugaring and lowering. The IR carries structured
prose metadata so the text emitter can round-trip prose faithfully.

**Memory risk**: Proportional to prose volume. Selfhost prose is ~15%
of source by line count. Already measured in source string.
**Files**: `codex/Ast/Desugarer.codex`, `codex/IR/Lowering.codex`,
`codex/IR/IRChapter.codex`

### Step 8: Banned-word enforcement

Lexical prohibition of CPL banned words (it, this, they, some, many,
few, etc., so, since, while, may, might, should) within `We say:`
blocks. Diagnostic with suggested substitute.

**Memory risk**: Lookup table of ~15 words. O(1) per word.
**Files**: `codex/Syntax/Lexer.codex`

### Step 9: Sample + sweep integration

Write `codex.test/prose-basic.codex` exercising all CPL sentence forms
and annotation syntax. Add `.expected` snapshot. Add to sweep.
Compile with `prose` flag. Verify round-trip.

**Files**: `codex.test/prose-basic.codex`, `codex.test/prose-basic.expected`

## HWM Tracking

| CL | Step | Lex (MB) | Parse (MB) | Desugar (MB) | Scope (MB) | Check (MB) | Total (MB) | Delta |
|----|------|----------|------------|--------------|------------|------------|------------|-------|
| head | baseline | 29.07 | 25.92 | 15.10 | 28.63 | 54.93 | 153.65 | — |
| 1026 | step 1 | 29.10 | 25.95 | 15.11 | 28.66 | 54.94 | 153.77 | +0.12 |
| 1029 | step 2 | 29.14 | 25.99 | 15.13 | 28.69 | 54.99 | 153.95 | +0.30 |
| 1031 | step 3 | 29.18 | 26.02 | 15.15 | 28.71 | 55.01 | 154.06 | +0.41 |
| 1035 | step 4a | 29.23 | 26.06 | 15.18 | 28.76 | 55.06 | 154.29 | +0.64 |
| 1038 | step 4b | 29.26 | 26.09 | 15.21 | 28.77 | 55.09 | 154.42 | +0.77 |
| 1041 | step 5 | 29.29 | 26.12 | 15.22 | 28.80 | 55.11 | 154.54 | +0.89 |
| 1044 | step 6 | 29.36 | 26.18 | 15.27 | 28.87 | 55.22 | 154.90 | +1.25 |
| 1047 | step 8 | 29.49 | 26.33 | 15.36 | 28.97 | 55.44 | 155.60 | +1.95* |
| | step 3 | | | | | | | |
| | step 4 | | | | | | | |
| | step 5 | | | | | | | |

## Gate Log

| CL | Step | Pingpong | Sweep | CDX size | Source size |
|----|------|----------|-------|----------|-------------|
| 1026 | step 1 | FIXED POINT (1,822,992 B) | 152/0/18 | +1,760 B | 1,020,988 B |
| 1029 | step 2 | FIXED POINT (1,825,848 B) | 152/0/18 | +4,616 B | 1,022,429 B |
| 1031 | step 3 | FIXED POINT (1,827,448 B) | 152/0/18 | +6,216 B | 1,023,602 B |
| 1035 | step 4a | FIXED POINT (1,830,632 B) | 152/0/18 | +9,400 B | 1,025,854 B |
| 1038 | step 4b | FIXED POINT (1,833,728 B) | 152/0/18 | +12,496 B | 1,027,564 B |
| 1041 | step 5 | FIXED POINT (1,835,784 B) | 152/0/18 | +14,552 B | 1,028,815 B |
| 1044 | step 6 | FIXED POINT (1,840,216 B) | 152/0/18 | +18,984 B | 1,031,645 B |
| 1047 | step 8 | FIXED POINT (1,848,440 B) | 152/0/18 | — | 1,037,173 B |

## Decision Log

- **2026-05-06 (step 8)**: HWM delta marked * — new seed (CL 1045)
  includes Nib's UEFI Console emit, so absolute numbers reflect both
  prose buildout and UEFI code growth.
- **2026-05-06**: Feature flag approach chosen over always-on. Rationale:
  prose parsing adds new token kinds and parse paths that could surface
  corner-case bugs in the ~21K-line selfhost. Toggle lets us iterate on
  prose without blocking codegen/OS work. Flag is a Boolean on a record
  threaded through the pipeline, matching the VmProfile precedent.
