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

Split into 1a (API shape) and 1b (AST/IR span threading).

**1a — API parity (done).** Self-host `Diagnostic` now matches reference:

- `Diagnostic { code, message, severity, span, related-spans }`.
- `make-error / make-warning / make-info / make-hint` take a `SourceSpan`.
- `make-error-related` for the multi-span variant.
- Helpers in `SourceText.codex`: `synthetic-span`, `is-synthetic-span`,
  `span-at line col offset len`, `span-display`.
- 5 existing call sites updated. Parser site (`ParserCore.codex:326`)
  passes a real span built from its token; the other four
  (`Unifier.codex` unify errors, `NameResolver.codex` duplicate-def
  and undefined-name, `X86_64.codex` IR-error passthrough) use
  `synthetic-span` because the AST/IR nodes they operate on don't
  yet carry source spans.
- `diagnostic-display` renders `file:line:col: sev CDXnnnn: message`
  when the span is real; prefix omitted for synthetic spans.

**1b — AST/IR span threading (pending, own branch).** Retrofit
`SourceSpan` onto every AST and IR node type so diagnostics reported
by name resolver, type checker, lowering, and emit can cite the user's
original source. Touches:

- `Ast/AstNodes.codex` — every variant constructor and record gets a
  `span : SourceSpan` field.
- `Ast/Desugarer.codex` — carry span from CST `Token` into each new AST
  node via `span-at`.
- `IR/IRChapter.codex` — every IR node type gets a `span`.
- `IR/Lowering.codex`, `Lowering*.codex` — carry AST span into IR node.
- `Emit/*.codex` — any site that constructs IR or emits diagnostics
  uses the node's span.

This is a mechanical but large edit, and it will churn almost every
constructor call in the compiler. Land on its own branch
(`hex/ast-ir-spans` suggested) with golden-output testing per phase so
regressions are caught at the node level rather than downstream.

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

#### Phase 4 follow-up: streaming binary path diagnostics

The `compile-with-citations` driver now gates per-phase with a running
accumulator bag. The **streaming binary path** (`compile-to-binary` /
`emit-defs-binary-gated`) went through a partial port:

- Tier 1 landed: per-def parse bags are captured and merged into the
  compilation bag (previously the parser's per-def state was dropped
  via `unwrap-body`, silently losing syntax errors on bare metal).
- Chapter gating landed: `CitesDecl` / `ACitesDecl` now carry a
  `citing-chapter : Text` stamped by the scanner. If any def in a
  chapter has parse errors, the chapter is marked bad; any chapter
  citing a bad chapter is transitively bad. Bad chapters skip codegen
  but their errors still surface in the final bag.

- **Tier 3 deferred: per-def resolve + type-check on the binary
  path.** `compile-with-citations` already runs the resolver and type
  checker on the whole chapter, but `compile-to-binary` does not — so
  on bare metal we still cannot surface undefined-name / type-mismatch
  diagnostics until codegen fails with a generic IrError. Running
  `resolve-expr` and `check-def` per def inside
  `emit-defs-binary-gated`, seeding bag results the same way parse
  bags are seeded, would close this gap. Blocker hit first attempt:
  `cites NameResolution (builtin-names, resolve-expr, ...)` from
  `main.codex` caused unrelated type inference errors at line 18
  (`build-all-assignments`), suggesting a chapter-scoping edge case
  that wants investigation before threading NameResolver functions
  into the binary path. Likely one of: (a) the specific name set in
  the cite list collides with something in main.codex's existing
  scope, (b) CodexEmitter's `collect-ctor-names` already overloaded
  and adding another cite confuses disambiguation, or (c) the cite
  ordering matters and citing NameResolution before CodexEmitter
  changes what wins. A branch dedicated to just that threading can
  sort it out; the streaming binary path is now at least visible for
  syntax errors, which was the largest-blind-spot class.

### Phase 5 — Organized error output (out of scope for the compiler)

Originally this section proposed grouping by file, sorting by source
position, formatting related spans as indented notes, and rendering
summary footers. That's moved out of the compiler's job.

The compiler's responsibility ends at producing a faithful, ordered
`List Diagnostic` with real spans and stable CDX codes. That's done
in Phases 1–4. Grouping, sorting, pretty-printing, JSON serialization
for editors, summary tables, and any other presentation concerns
belong to a **post-compile tool** — `codex report` or an LSP server
or an editor plugin — that consumes the structured diagnostic list.

Why: a compiler that's also a report formatter accumulates policy
decisions (sort order, group headers, severity emoji, color, unicode
vs. ascii box drawing) that every consumer wants to override. Keeping
the compiler's output as a flat well-spanned list of records means
downstream tools can render however they need without fighting the
compiler.

Minimum the compiler needs to provide for external tooling:

- Stable `Diagnostic { code, message, severity, span, related-spans,
  provenance }` record — done.
- Way to emit the bag as a machine-readable stream. JSON-over-stdout
  when a flag is set is a reasonable shape. This is a small surface
  and can live in the CLI driver, not the compiler proper.

The richer presentation pipeline (grouping, sorting, caret rendering,
summary stats) lives in a separate tool and is tracked outside this
plan.

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
- Bare-metal rendering budget: how much diagnostic output can we afford
  to send over serial before the 360s binary-pingpong timeout? May need
  to summarize aggressively when errors > N.

## Related compiler-infrastructure work

The scope above (diagnostics, CDX registry, phase gating) is the
immediate priority. But several adjacent pieces of internal compiler
infrastructure belong in the same conversation. A professional compiler
has most of these; Codex has partial or none. Each is its own body of
work — listed here so the diagnostic plan doesn't accidentally imply
"that's everything that's missing."

The framing below is "internal concerns" — things the compiler itself
uses to stay correct and debuggable. External policy decisions
(warning-as-error, which warnings to surface, IDE integration format,
etc.) are out of scope. An error is an error; it means we cannot produce
meaningful complete output. A warning is "are you sure you meant this?"
and what downstream tooling does with it is their choice.

### A. Inspection & introspection

Cheap to build if the IR is already well-structured; huge debugging
multiplier when something goes wrong mid-pipeline.

- **Per-phase dumps**: `--dump=parsed`, `--dump=resolved`, `--dump=typed`,
  `--dump=ir`, `--dump=codegen`. Each phase knows how to pretty-print its
  output. When something downstream breaks, you diff the dumps between a
  working and broken input and find the exact phase where things diverged.
  The Codex text emitter already exists for one phase; generalize the
  pattern.
- **DWARF (or Codex-equivalent) debug info for bare-metal**: lets GDB
  step through a bare-metal ELF at the source level. Today a `__start`
  crash at address 0x100384 is a 4-hour scavenger hunt; with debug info
  it's `main.codex:42:8 in hamt-lookup-offset`. This also serves
  production Codex.OS — users running Codex programs will want to debug
  them.
- **Reverse mapping**: given an IR node, find the source span it came
  from. Given an emitted instruction, find the IR node that produced it.
  Right now the arrows only go forward through the pipeline. Bidirectional
  mapping is what makes step-through debugging, "why did this code get
  emitted," and source-level profiling possible.
- **Find-all-references / go-to-definition**: if the symbol table keeps
  backlinks from definitions to every use, both operations are O(1). The
  data has to be captured during name resolution; bolting it on later is
  awkward. Cheap to design in now, expensive to add later. Important for
  future IDE integration but also useful to the compiler itself for
  refactoring passes and unused-def detection.

### B. Invariant enforcement (verifier passes)

Catches bugs at the phase where they're introduced, not three phases
later when the symptom is far from the cause. This is the thing that
would have saved four hours on the HAMT-crash debugging session.

Every phase produces output that must satisfy invariants for the *next*
phase to operate correctly. Write those invariants down as a pass that
runs between phases in debug builds.

- **After name resolution**: every `IrName` resolves to exactly one def
  or parameter. No undefined references.
- **After type checking**: no `ErrorTy` reaches later phases (if it does,
  there's an error count mismatch — a type error got swallowed somewhere).
  Every AST/IR node has a non-null type assignment.
- **After lowering**: no unresolved type variables, no unbound names,
  every record construction has the correct arity for its declared type,
  every match is total or has an explicit catch-all.
- **After codegen**: every call target resolves to an emitted function;
  every fixup references a valid offset; no dangling patches; the text
  buffer has the expected function count in func-offsets.

Run these in debug builds; skip in release if too slow. The point is to
*fail fast* in the phase that broke the invariant, with a specific
pointer to what invariant was violated. A bug that slips past three
phases is much harder to diagnose than one caught at the source.

### C. Fuel / termination budgets

Every recursive compiler operation needs a fuel counter.

Concrete risks we have today:
- Type inference can loop if the unifier hits a cycle (occurs-check
  failure that was bypassed or handled incorrectly).
- Lowering can loop if desugaring produces a cycle (e.g., a rewrite that
  re-applies to its own output).
- Name resolution can loop on recursive or circular imports.
- Recursion can blow the stack on deeply nested or adversarial input
  (deeply nested parens, deeply nested record literals, etc.).

Rule: every recursive descent has a maximum depth or fuel count.
Exceeding it becomes a compiler error: `CDX9001: compiler resource
exhausted in <phase> at <source-loc>; likely a compiler bug or
pathological input`. The compiler **never hangs** and **never crashes
silently** on input — those are always failures of this rule.

Fuel also protects against denial-of-service as Codex moves into the OS
role. A user running another user's Codex program shouldn't be able to
brick the system by handing it a program that makes the compiler loop.

### D. Elaborated AST (typed AST as a distinct stage)

Today the pipeline has: AST (pre-typecheck) → type-check → IR
(post-lowering). The gap: "what's the inferred type of this
subexpression in the original AST?" can only be answered by re-running
inference, or by chasing through IR and reverse-mapping.

Professional compilers produce a **typed AST**: same shape as the
untyped AST, but every node carries its resolved type. Later phases
consume the typed AST, not the raw one. Diagnostics, tooling, and
lowering all benefit.

Benefits:
- The invariant checks in (B) are trivial to write: every typed-AST node
  has a type, assert it's not ErrorTy.
- Tooling ("hover to see type") is a direct query instead of a
  recomputation.
- Roundtrip tests are precise: pretty-print a typed AST, re-parse,
  re-typecheck, compare — the type annotations should survive.
- Separates the concern of "build AST" from "assign types" — the
  untyped AST is simpler, the typed AST is richer; both are valuable.

Cost: one more IR intermediate structure. Usually worth it.

### E. Stable name mangling

Mangled names today are a function of content hashes. The consequence:
adding a small function shifts the hash of its containing chapter, which
cascades into every mangled name that references or is referenced by
that chapter. A one-line change can rename hundreds of symbols.

Fix: mangle as a deterministic function of the **canonical signature**
— the name, type, containing chapter (in a stable addressing scheme
like a path or ordered-chapter-index). Only resort to disambiguation
when there's a genuine naming conflict in source (two defs with the
same name, collisions between imported chapters, etc.).

Properties the mangling should have:
- **Locality**: a change in chapter A shouldn't rename anything in
  chapter B.
- **Determinism**: the same source, built twice, produces the same
  mangled names. Already partially true; make it fully true.
- **Reversibility** (optional): given a mangled name, recover the
  canonical signature. Useful for debug info, error messages,
  reflection.
- **Test**: add a dead function to a large chapter; the emitted binary
  should grow by exactly that function and nothing else should move.

### F. Provenance tracking

Every IR node should record two things:
1. The source span it ultimately came from.
2. What transformation produced it (parse, desugar, lowering,
   partial-app expansion, etc.) and optionally a chain back to the
   original source construct.

Concrete example: when desugaring turns `do { x <- e; body }` into
`e >>= (\x -> body)`, the generated lambda has:
- Source span = the original `do`-block's span.
- Provenance = `desugared from do-block`, `original construct: do at
  line N`.

Why this matters: errors and crashes in lowered code should blame the
user's original construct, not the intermediate IR. If lowering produces
buggy IR and we crash at codegen, without provenance we only see "bad
IR"; with provenance we see "bad IR that came from the do-block at
line N," which is usually enough to diagnose.

This also makes "why is there a call to `>>=` here?" a direct query,
which helps with compiler bug triage.

### G. Self-verification / roundtrip tests

Beyond pingpong (which is semantic equivalence of compiler output),
cheap CI tests that catch whole classes of regressions:

- **Parse → pretty-print → parse**: the result should be the same AST
  both times (idempotence). Catches: pretty-printer losing information,
  parser accepting text the printer can't produce.
- **Typecheck → emit → re-typecheck emitted IR**: the emitter shouldn't
  produce IR that its own typechecker rejects. Catches: emitter bugs,
  typechecker holes.
- **Lowering determinism**: same input, same output, byte-for-byte. Run
  twice in CI, diff results. Catches: accidental dependency on hash
  ordering, pointer identity, allocation order.
- **Codex text → binary → Codex text**: compiling to binary and back
  (when we add disassembly) should recover the original semantics.

These are quick to implement once the inspection infrastructure from
(A) is in place.

### H. Testing infrastructure

Professional compilers have heavy testing harnesses; ours is thin.
Items, rough order of value:

- **Golden tests per phase**: `samples/expected/arithmetic.parsed.txt`,
  `.resolved.txt`, `.typed.txt`, etc. CI diffs against expected.
  Regenerate when intentional; CI failure when accidental. This is how
  you notice "my innocuous refactor changed the parser's output in some
  way I didn't predict."
- **Property tests** (quickcheck-style): for any well-formed AST,
  `parse(print(ast)) == ast`. For any program, `compile(program)` is
  deterministic. These probe edge cases that manual tests miss.
- **Fuzzing**: feed the parser random byte sequences; it should always
  terminate (fuel) and never crash (invariants). Fuzz mutation of valid
  programs ("delete this token," "insert this token," "rename this
  identifier") to find invariant-breaking inputs.
- **Differential testing**: reference and self-host compile the same
  input; outputs should agree modulo known differences. Already
  implicit in pingpong; make it a first-class test harness.

### I. Concurrency story

Not urgent, but worth deciding on. The reference `DiagnosticBag` has a
lock because phases may run concurrently (per-def codegen, per-chapter
type-checking). The self-host is single-threaded today.

If we ever parallelize:
- The diagnostic bag needs concurrency-safe access.
- Phase invariant checks need to be safe to run on partial state or
  have a barrier after each phase.
- Provenance and stable mangling both become more important (different
  threads shouldn't see different name orderings).

If we stay sequential forever, we can drop the locks in the bag and
save the overhead. Decide explicitly — don't let it drift.

### J. Hash-consing / canonical forms

Codex has `sem-equiv` for comparing two compiler outputs. Internally, a
related concern: making two structurally-identical IR subtrees share
memory ("hash-consing"). Benefits:
- Equality checks become pointer equality.
- Caching becomes trivial: two calls with the same argument graph hit
  the cache regardless of how they were constructed.
- Memory savings on repetitive IR (lots of defs reusing similar types).

Cost: extra allocation-time work and a cons-table. Only worth it if
profiling shows the overhead of equality checks or allocation is
material. Flag it for later; don't do it now.

### K. Plugin points / phase composition

Longer term. Professional compilers often treat phases as first-class
values — orderable, replaceable, instrumentable. This enables:
- Experimental phases inserted between standard ones without forking the
  compiler.
- External tools that consume intermediate state (LSP, documentation
  generators, linters).
- Research work (alternative type systems, alternative codegen) without
  disturbing the production pipeline.

This is a big refactor and not urgent. Listed for completeness and so
future design work doesn't treat the current fixed pipeline as
immutable.

---

The concrete work above is additive to the six-phase plan. Each item
(A) through (K) is its own body of work. As with the main plan,
sequence smallest-first: (A) per-phase dumps and (B) invariant passes
are small and unlock a lot; (C) fuel is small and prevents catastrophes;
(D) typed AST is medium and unlocks the rest; (E)-(K) are larger or
speculative.
