# `do` → `act ... end`: Replacing the Monadic Block Keyword

**Status:** Planned
**Owners:** Hex-hex (Phase A, Phase B parser), coordinate with Cam for pingpong/migration timing.

---

## Why

### Problem 1: `do` lies to imperative readers.

`do` is borrowed from Haskell, which borrowed it from English imperative prose ("do this, then that"). Haskell's bet was that imperative programmers would read `do` as "do the following steps."

For anyone with actual imperative muscle memory — FORTRAN, BASIC, Pascal, C, Ruby, shell — `do` means **loop**. `DO i = 1, 10`, `do { ... } while`, `for x in list do`, `do...loop`. It's the wrong word for "sequence of statements."

The camouflage fails exactly when it needs to succeed. A reader new to Codex sees `do` and gets the wrong mental model on first glance. They have to be taught that here, `do` means sequencing, not iteration. That's a needless cognitive tax.

### Problem 2: layout-based `do`-blocks make multi-line calls load-bearing.

The parser (`parse-app-loop` in self-host, `ParseApplication` in the reference) does not skip newlines between arguments. Every existing multi-argument call in the compiler is squeezed onto one long line because of this — not for style. See: every `make-error` / `span-at` / `bag-add` call in the codebase.

This has surfaced as a real bug at least three times (most recently in `check-cite-names` for H-007 duplicate-cite detection). The error manifests far from the actual site because parse recovery drifts. Debugging is painful.

Two prior attempts to fix this without changing the keyword:

1. **Paren-depth counter** (`hex-hex/parser-multiline-app`, 2026-04-15): "inside `()`, newlines are whitespace." Worked for plain multi-line calls. Broke `let x = (do ...)` because the do-block inside parens had its statement separators eaten — next do-stmt's identifier became an argument continuation of the previous stmt's RHS. Round-trip failed. Branch never merged.

2. **Indent-aware continuation**: "swallow newlines only if next token is indented past function atom's column." Consistent with how do-blocks already end (column ≤ min-col). Feasible but adds complexity to `parse-app-loop` that scales with every call site.

Both attempts contort the parser to work around the fact that **newlines serve two different purposes**: separating do-statements, and being whitespace. As long as that ambiguity exists, the parser has to disambiguate it, and every disambiguation rule has edge cases.

### Problem 3: layout is writer-optimized, not reader-optimized.

When you type fresh code, indentation feels natural. When you read someone else's compiler — or your own code six months later — explicit delimiters are faster to parse visually. You `grep` for `act` and find every effect boundary. With layout you're scanning for indentation patterns and counting columns.

Layout also lies during code transformations: cut a do-block from one context and paste it into another, and the indentation silently breaks. Explicit delimiters travel with the code.

### Why `act`?

Rejected alternatives and why:

- **`do` kept, fix parser**: preserves the misleading keyword. The reader problem remains.
- **`do { ; }`**: adds braces and semicolons, both disliked aesthetically. Also parser complexity for comparable semantic benefit.
- **`begin ... end`**: verbose and vacuous. "begin what? end what?" SQL's stored procs are uglier for it.
- **`seq`**: short, declarative, unambiguous, but compsci-flavored. Doesn't fit Codex's natural-language/book vocabulary.
- **`run`**: evocative but overloaded (BASIC's `RUN`, shell, tons of imperative baggage).
- **`flow`, `steps`, `perform`**: workable but weaker fit.

**`act`** wins because:

- **Fits Codex's vocabulary.** Chapters, cites, prose, claim, proof, quire, notation — Codex already reads like a book. "Acts" belong in that register. A chapter contains acts.
- **Declarative, not verby.** "An act" names the thing (a scripted sequence of events), not an instruction to perform.
- **Short.** 3 letters, one more than `do`.
- **Universally understood.** Every reader, programmer or not, knows an act is a bounded sequence of scripted events. Theater Act 1, Act 2.
- **No loop connotation.** Nobody confuses an act with iteration.
- **`end` earns its keep once paired with `act`.** "End Act 1" is vernacular theater language. `end` after `act` is not empty ceremony — the opener named what's being ended. `begin ... end` was vacuous; `act ... end` is not.

### The payoff: one coherent rule

With `act ... end` as explicit block delimiters:

- The parser knows unambiguously when it's in statement context (inside an unclosed `act`).
- Newlines become pure whitespace everywhere except top-of-act.
- Multi-line calls work everywhere. No more single-line-call convention.
- Column tracking for do-block termination is deleted.
- The cognitive model for readers is consistent: explicit keywords delimit explicit regions.

The keyword change, the parser simplification, and the parser bug fix all land together as one coherent story instead of three separate workarounds.

---

## Design

### Syntax

```codex
f (x) =
 let y = act
  a <- foo x
  b <- bar a
  return b
 end
 in y + 1
```

**Rules:**

- `act` opens a statement-sequence block.
- `end` closes the nearest unclosed `act`.
- Inside an `act` block, newlines at the top level separate statements.
- Inside nested delimiters (`()`, `[]`, `{}`, inner `act ... end`), newlines are whitespace.
- Outside `act`, newlines are whitespace everywhere (subject to top-level definition separation, which is unchanged).

**Statement forms inside `act`:**

- `name <- expr` — bind the result of an effectful expression to a name.
- `expr` — evaluate for effect, discard the result.

Unchanged from current `do`-block semantics.

### Grammar (informal)

```
act-block    ::= "act" newline? act-stmts "end"
act-stmts    ::= act-stmt (stmt-sep act-stmt)*
act-stmt     ::= identifier "<-" expr
               | expr
stmt-sep     ::= newline (at top level of act-block only)
```

### What doesn't change

- `let ... in ...` — multi-binding `let` with layout continues to work as today. Single-binding is already the norm; multi-binding is rare and not affected by the motivating problems.
- Top-level definitions — still separated by newline-then-identifier-at-col-0.
- Record literals `{ field = v, ... }`, list literals `[ a, b, c ]`, function application grouping `(expr)` — unchanged.
- All effectful-type inference, CDX2033 enforcement, etc. — unchanged. This is purely a syntactic surface change.

---

## Plan

Two phases so each lands independently and `pingpong.sh` validates each.

### Phase A — Additive (coexistence)

**Goal:** accept `act ... end` as alternative syntax. `do`-layout continues to work. No existing code breaks.

**Branch:** `hex-hex/act-phase-a`

**Changes:**

1. **Lexer** (`Codex.Codex/Syntax/Lexer.codex`, `src/Codex.Syntax/Lexer.cs`):
   - Add `ActKeyword` token.
   - Verify `EndKeyword` exists (used by proof grammar); add if missing.

2. **Parser self-host** (`Codex.Codex/Syntax/ParserExpressions.codex`):
   - `parse-atom` recognizes `ActKeyword` as a compound-expression starter.
   - `parse-act-block`: consume `act`, optional newline, stmts, `end`.
   - Inside act-block, track "act-depth" in parser state. When act-depth > 0 and no inner `()`/`[]`/`{}`/inner-act is open, newlines = statement separators. Otherwise newlines = whitespace.
   - The existing `parse-do-block` stays intact.

3. **Parser reference** (`src/Codex.Syntax/Parser.Expressions.cs`):
   - Matching logic, idiomatic C#.

4. **Tests** (`tests/Codex.Syntax.Tests/ParserTests.cs`):
   - Empty act: `act end`.
   - Single-stmt act.
   - Multi-stmt act with newline separation.
   - Multi-line call inside act (via parens).
   - Nested `act` inside `act`.
   - `act` inside parens (the case that broke paren-depth: `let x = (act ... end)`).
   - Round-trip (parse + print) preserves shape.

**Acceptance:**
- Build green, new tests green, full test suite green.
- Pingpong still green (Cam confirms).
- No existing `.codex` file needs to change.

### Phase B — Migration + removal

**Goal:** rewrite every `do` in the tree to `act ... end`; delete `do`-layout support and the column-tracking machinery that exists only to serve it.

**Branch:** `hex-hex/act-phase-b`

**Changes:**

1. **Migration tool** (`tools/migrate-do-to-act.sh`):
   - Walk every `.codex` file.
   - For each `do` keyword: find its layout extent (next line with column ≤ the do-block's min-col is outside the block). Replace `do` with `act`; insert `end` at the matching outer indent.
   - One-shot. Commit output as a single migration commit so history is bisectable.

2. **Parser cleanup:**
   - Delete `parse-do-block` from self-host; same in reference.
   - Emit CDX for `DoKeyword` use: "Use `act ... end` instead."
   - Delete layout/column-tracking code that existed only for do-block termination.

3. **Doc + memory cleanup:**
   - Update `docs/Active/Compiler/SELF-HOST-PARITY-AUDIT.md` if it references `do`-block parsing.
   - Retire `feedback_selfhost_parser_limits.md` memory — single-line rule is no longer load-bearing.
   - Remove `do` references from other docs; add a note to `CLAUDE.md` under syntax.

4. **Tests:**
   - Existing do-block tests ported to act/end form, or renamed and rewritten.
   - Multi-line call inside an `act` without parens (should now parse correctly if we also ship the newlines-are-whitespace-inside-delimiters rule fully).

**Acceptance:**
- Build + tests + pingpong green.
- Sem-equiv stays at 100% body match between stage0 and stage1.
- `git grep "^\s*do\b" -- '*.codex'` returns nothing.

---

## Risks

- **Sem-equiv regression inside self-host.** The self-host parser rewrite must produce byte-identical output in stage1 vs stage0 for the migrated code. Phase A is additive first precisely so stage0 can still emit `do` while stage1 learns `act`, then migration happens in one atomic commit where both parsers switch over.

- **In-flight branches conflict with migration.** Any branch touching `.codex` files during Phase B will conflict with the migration commit. Coordinate with Cam before starting Phase B — ideally no other compiler-branch work in flight when the migration lands.

- **`do` embedded in proof/prose/cites.** Should be none (those use their own grammars), but `git grep` before Phase B confirms.

- **Migration tool correctness.** The tool has to get indentation right for the emitted `end`. Wrong column = post-migration compile error. Mitigation: run migration tool, full build, full test, pingpong — if all green, the migration is correct by construction.

- **Aesthetic regret.** If `act` turns out to read worse than expected in practice, reversing is mechanical (mirror migration tool). But the worse outcome is ambivalent keeping of both forms.

---

## Open questions

- Do we want `act ... end` or `act ... end act` (explicit closer naming)? The latter is more self-documenting but adds noise. Default: bare `end`, rely on editor matching.
- Should `end` be scoped (e.g., `act` + bare `end` only, vs. `end` as general closer)? Default: bare `end` only closes the nearest unclosed `act`. Other constructs don't need closers.
- Migration tool: shell script, C# tool under `tools/`, or one-shot Codex program? Default: C# tool under `tools/` for parity with other migration tooling.
