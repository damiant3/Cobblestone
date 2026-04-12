# Diagnostics, Error Reporting, and Staged Compilation

## Why this exists

The compiler's error handling works but is fragile and uneven. Three concrete
problems, each of which has cost us hours of debugging:

1. **Self-host diagnostics are impoverished.** Reference diagnostics have
   `SourceSpan` (file, line, column, related spans), four severities (Hint,
   Info, Warning, Error), a `DiagnosticBag` with locking and a 20-error
   suppression cap. Self-host diagnostics have `code`, `message`, three
   severities, no source location, no bag, no suppression. When the
   self-host reports an error, you get "CDX3002: Undefined name 'foo'" —
   no file, no line, no context. On bare metal, that error appears over
   serial with no way to correlate back to source.

2. **CDX codes are scattered string literals.** 83 distinct `CDX\d{4}` codes
   in the reference, across 17 files, as ad-hoc string literals. No central
   registry. No tooling knows what codes exist, what they mean, or whether
   two sites emit the same code for different reasons. Adding a new error
   means picking a number and hoping.

3. **Compiler stages don't gate on earlier failure.** The pipeline is
   tokenize → parse → scan → desugar → resolve → typecheck → lower → emit.
   Each stage assumes the previous one produced well-formed input. In
   practice: if parsing emits errors but produces partial output, later
   stages walk that partial output and emit their OWN cascading errors,
   often nonsensical, confusing the user about the real cause. The
   self-host has almost no inter-stage error gating.

This plan addresses all three.

## Scope

Not in scope:
- The broader self-host parity audit (covered by
  `SELF-HOST-PARITY-AUDIT.md` — diagnostics/staging is one row there).
- Error *recovery* inside a single stage (separate work, e.g. parser
  resuming at the next top-level def after a syntax error).
- IDE integration or LSP (downstream consumer of the diagnostics design).

In scope:
- Self-host `Diagnostic` type reaches parity with reference.
- Central CDX code registry, both compilers reference it.
- Staged pipeline with explicit error gates between phases.
- One canonical list-of-diagnostics for a whole compilation, queryable by
  phase, severity, code, or source location.

## Work breakdown

### Phase 1 — Self-host Diagnostic parity

Bring `Codex.Codex/Core/Diagnostic.codex` up to the reference shape:

- Add `SourceSpan` type (file path, start line/col, end line/col).
- Add `related-spans : List SourceSpan` field to `Diagnostic`.
- Add `Hint` to `DiagnosticSeverity`.
- Every existing call site that creates a diagnostic must now supply a
  `SourceSpan`. This touches parser, name resolver, type checker, emitter
  code — every file using `make-error` today. The call sites all need a
  span available; for parser / lexer that's trivial (they have token
  positions). For later phases the AST / IR nodes need to carry their
  source span.

- Decision point: do AST and IR nodes carry `SourceSpan`? In the reference
  they do. In the self-host, confirm and fix gaps. Each IR node should be
  able to answer "where in the source did this come from?"

### Phase 2 — `DiagnosticBag` in the self-host

Reference has a `DiagnosticBag` with:
- Max error cap (20) with overflow suppression + a `CDX0001` sentinel
- `HasErrors`, `Count`, severity-specific add methods
- Locking (concurrent-safe)

Port this to the self-host. Concurrency is less pressing there (pipeline
is sequential for now) but the rest of the API matters. One bag per
compilation, threaded through all phases.

### Phase 3 — CDX code registry

Create `docs/CDX-Codes.md` (or `src/Codex.Core/CdxCodes.cs` — see below)
as the single source of truth. Columns: code, phase, severity, short
description, when to emit, example message format.

Two options:
- (a) Pure documentation (`docs/CDX-Codes.md`). Human reference only;
  code still uses string literals.
- (b) Codified (`static class CdxCodes` with `public const string
  UnknownName = "CDX3002";`). Compiler code references constants instead
  of literals. Grep + rename actually work.

Recommend (b). The reference and self-host should both consume the same
registry — reference from a C# constants file, self-host from a
`Codex.Codex/Core/CdxCodes.codex` with matching constants. Audit tooling
(simple script) verifies both files agree.

Additionally:
- Every code appears exactly once in the registry.
- Duplicate-code check: grep for `CDX\d{4}` across source, verify each
  code appears in the registry and nowhere else as a bare literal.
- Unused-code check: find codes in the registry not referenced anywhere.

### Phase 4 — Staged compilation with error gates

Today the pipeline is:

```
tokenize → parse → scan → desugar → resolve → typecheck → lower → emit
```

Each stage takes the previous stage's output directly. If parse produces
a partially-valid AST with errors, resolve walks it and piles on.

The fix: gate. After each phase, check `bag.HasErrors` (for errors
originated *in this phase*, not earlier). If the phase produced errors
severe enough that downstream phases can't proceed sensibly, stop the
pipeline and return the collected diagnostics.

Concretely, define a `PhaseResult a`:

```
PhaseResult a = record {
  value : a,
  phase-errors : List Diagnostic,
  fatal : Boolean
}
```

Each phase returns a `PhaseResult`. The driver:
- Collects all phase diagnostics into the single bag.
- If `fatal`, short-circuit; run no further phases.
- Render diagnostics at the end, sorted by source position.

Decide per phase what is fatal:
- Lexer: malformed tokens → fatal (nothing to parse).
- Parser: any syntax error → fatal *unless* we have parser error
  recovery later (see separate backlog item).
- Name resolver: unknown names → fatal (typecheck cannot proceed with
  unknown references).
- Type checker: type errors → non-fatal; continue and emit diagnostics
  from later phases to surface multiple errors at once (reference does
  this already in some cases).
- Lowering / emit: errors here are bugs in earlier phases and *should*
  have been prevented by gates; if they happen, fatal.

### Phase 5 — Organized error output

Reference currently dumps diagnostics as a flat list ordered by insertion.
Add a single canonical presentation:

- Group by source file, then sort by source position.
- Within a position, order: errors before warnings before info before hints.
- Each entry: `<file>:<line>:<col>: <severity> <code>: <message>`
- Related spans rendered as indented sub-entries: `  note: related at <file>:<line>:<col>`
- Summary footer: total counts per severity, total compilation time.

Also expose the structured list programmatically:
- `List Diagnostic` for iteration
- Map by code → list of occurrences (for "why does this code keep firing?")
- Map by phase → list of occurrences (for "where is the pipeline breaking?")

Give both compilers a flag to emit diagnostics as JSON for tooling:
`codex check file.codex --diagnostics-format=json`.

## File impact estimate

Self-host files that will need updating once per change:
- `Codex.Codex/Core/Diagnostic.codex` (expand type)
- `Codex.Codex/Core/SourceText.codex` (already exists — verify `SourceSpan`)
- `Codex.Codex/Syntax/*.codex` (every site that reports a lex/parse error)
- `Codex.Codex/Semantics/NameResolver.codex`
- `Codex.Codex/Types/TypeChecker*.codex`
- `Codex.Codex/IR/Lowering*.codex`
- `Codex.Codex/Emit/*.codex`
- `Codex.Codex/main.codex` (pipeline driver, adds gates)

Reference files:
- `src/Codex.Core/Diagnostics.cs` (already good, minor additions)
- `src/Codex.Core/CdxCodes.cs` (new registry)
- `src/Codex.Cli/Program.*.cs` (rendering)
- Each phase's caller (already gates in some places; audit for gaps).

Docs:
- `docs/CDX-Codes.md` (new human-readable registry).

## Sequencing

Do in this order, smallest-first, each shippable independently:

1. **CDX code registry (Phase 3)** as pure docs. Just enumerate what
   exists today. Reference only. Zero code change. Immediately useful.
2. **`SourceSpan` in self-host `Diagnostic` (Phase 1)**. One type
   change + mechanical fix-up of every `make-error` call site.
3. **`DiagnosticBag` in self-host (Phase 2)**. Introduce the bag, thread
   through pipeline. Old `List Diagnostic` gradually replaced.
4. **CDX codes as constants (Phase 3 part b)**. Replace string literals
   with named constants, reference and self-host.
5. **Phase gating (Phase 4)**. Wrap each phase with a `PhaseResult`
   check. Define fatality per phase. Short-circuit the driver.
6. **Organized output (Phase 5)**. One presentation layer. JSON flag.

## Success criteria

- Every error reported to a user includes file, line, column.
- Syntax errors no longer cascade: a missing paren produces one
  diagnostic, not twenty.
- `docs/CDX-Codes.md` exists and matches reality: every `CDX\d{4}` in
  source is documented there, and every documented code is emitted by
  at least one site.
- `codex check` exits nonzero on any error-severity diagnostic and zero
  otherwise.
- Running a broken sample through the bare-metal ELF produces a
  diagnostic summary over serial before `SIZE:` or crashes with a code
  explaining what went wrong.

## Questions to resolve before starting

- Does the self-host AST / IR already carry `SourceSpan`, or do we need
  to add it? (Needs quick audit.)
- What's the policy on warning-as-error? Reference has info/warning
  today but no aggregation rule.
- Bare-metal rendering budget: how much diagnostic output can we afford
  to send over serial before the 360s binary-pingpong timeout? May need
  to summarize aggressively when errors > N.
