# Diagnostics, Error Reporting, and Staged Compilation

## Status

Diagnostics Phases 1a, 1b, 2, 3, and 4 are shipped on the hosted pipeline:
self-host `Diagnostic` has `SourceSpan` + severity + related-spans;
`DiagnosticBag` threads through every phase with the CDX0001 overflow
sentinel at 20 errors; the 83+ CDX codes live in a single registry that
both compilers consume (`7b00b46` and follow-ups); the pipeline gates
per-phase with a running accumulator bag (`0d1239d`).

Phase 5 (presentation — grouping, sorting, pretty-printing) was explicitly
pushed out of the compiler (`8c5d693`). The compiler's job ends at a
well-spanned `List Diagnostic`; downstream tools (LSP, `codex report`,
editor plugins) render it.

## Open — streaming binary path Tier 3

`compile-with-citations` (hosted path) already runs resolver + type checker
with full bag threading. `compile-to-binary` / `emit-defs-binary-gated`
went through a partial port:

- Tier 1 (shipped): per-def parse bags captured and merged into the
  compilation bag. Previously `unwrap-body` silently dropped parser state
  on bare metal.
- Chapter gating (shipped, `1484914`): `CitesDecl` / `ACitesDecl` carry a
  `citing-chapter` field stamped by the scanner. Chapters with parse
  errors are marked bad; citing chapters transitively bad. Bad chapters
  skip codegen but their errors still surface.
- **Tier 3 deferred**: per-def resolve + type-check on the binary path.
  Bare metal still cannot surface undefined-name / type-mismatch
  diagnostics — codegen fails with a generic IrError instead. Running
  `resolve-expr` and `check-def` per def inside `emit-defs-binary-gated`
  would close this. First attempt hit a chapter-scoping edge case when
  citing `NameResolution` from `main.codex`; needs its own branch.

## Success criteria

- Every error reported to a user includes file, line, column.
- Syntax errors no longer cascade: a missing paren produces one
  diagnostic, not twenty.
- `codex check` exits nonzero on any error-severity diagnostic.
- Running a broken sample through the bare-metal ELF produces a
  diagnostic summary over serial before `SIZE:` or crashes with a code
  explaining what went wrong.

---

## Related compiler-infrastructure work

Adjacent pieces of internal compiler infrastructure that a professional
compiler would have and Codex currently has partially or not at all.
Each is its own body of work. Listed here so the diagnostics plan doesn't
imply "that's everything that's missing."

### A. Inspection & introspection

- **Per-phase dumps**: `--dump=parsed`, `--dump=resolved`, `--dump=typed`,
  `--dump=ir`, `--dump=codegen`. Diff dumps between a working and broken
  input to find the exact phase where things diverged. The Codex text
  emitter already does this for one phase; generalize.
- **DWARF (or Codex-equivalent) debug info for bare-metal**: lets GDB
  step through a bare-metal ELF at source level. Today a `__start` crash
  at 0x100384 is a 4-hour scavenger hunt. Also serves production
  Codex.OS — users will want to debug their Codex programs.
- **Reverse mapping**: IR node → source span; emitted instruction → IR
  node. Today arrows only go forward. Bidirectional mapping is what
  makes step-through debugging, "why did this code get emitted," and
  source-level profiling possible.
- **Find-all-references / go-to-definition**: symbol table with
  backlinks. Cheap to design in now, expensive to add later.

### B. Invariant enforcement (verifier passes)

Catches bugs at the phase where they're introduced, not three phases
later when the symptom is far from the cause. Every phase produces
output that must satisfy invariants for the *next* phase. Write them
down as a pass between phases in debug builds.

- After name resolution: every `IrName` resolves to exactly one def or
  parameter.
- After type checking: no `ErrorTy` reaches later phases; every AST/IR
  node has a non-null type assignment.
- After lowering: no unresolved type variables, no unbound names, every
  record construction has correct arity for its declared type, every
  match is total or has an explicit catch-all.
- After codegen: every call target resolves to an emitted function;
  every fixup references a valid offset; no dangling patches.

Fail fast in the phase that broke the invariant, with a specific pointer
to what invariant was violated.

### C. Fuel / termination budgets

Every recursive compiler operation needs a fuel counter. Concrete risks:

- Type inference can loop if the unifier hits a cycle (occurs-check
  failure that was bypassed or handled incorrectly).
- Lowering can loop if desugaring produces a cycle.
- Name resolution can loop on recursive or circular imports.
- Recursion can blow the stack on deeply nested adversarial input.

Rule: every recursive descent has a maximum depth. Exceeding it becomes
`CDX9001: compiler resource exhausted in <phase> at <source-loc>`. The
compiler **never hangs** and **never crashes silently** on input. Fuel
also protects against DoS as Codex moves into the OS role.

### D. Elaborated AST (typed AST as a distinct stage)

Today: AST (pre-typecheck) → type-check → IR (post-lowering). The gap:
"what's the inferred type of this subexpression in the original AST?"
can only be answered by re-running inference or chasing through IR.

Professional compilers produce a **typed AST** — same shape, every node
carries its resolved type. Benefits: trivial invariant checks (every
node has a type, assert not ErrorTy); direct hover-type queries instead
of recomputation; precise roundtrip tests. Cost: one more IR.

### E. Stable name mangling

Mangled names today are a function of content hashes, so a small change
shifts the hash of its containing chapter and cascades. One-line changes
rename hundreds of symbols.

Fix: mangle as a deterministic function of the **canonical signature** —
name, type, containing chapter path. Disambiguate only on genuine
conflict. Properties: locality (change in chapter A doesn't rename
chapter B), determinism, optional reversibility. Test: add a dead
function to a large chapter; the emitted binary should grow by exactly
that function, nothing else should move.

### F. Provenance tracking

Every IR node records (1) the source span it came from, (2) what
transformation produced it. Concrete example: when desugaring turns
`x <- e; body` into `e >>= (\x -> body)`, the generated lambda's
provenance is "desugared from act-bind at line N". Crashes in lowered
code blame the user's original construct, not the intermediate IR.

### G. Self-verification / roundtrip tests

Beyond pingpong:

- **Parse → pretty-print → parse**: result is the same AST.
- **Typecheck → emit → re-typecheck emitted IR**: the emitter shouldn't
  produce IR its own typechecker rejects.
- **Lowering determinism**: same input, same output, byte-for-byte.
  Catches accidental dependency on hash ordering or allocation order.
- **Codex text → binary → Codex text** (once disassembly exists).

### H. Testing infrastructure

- **Golden tests per phase**: `.parsed.txt`, `.resolved.txt`,
  `.typed.txt` etc. CI diffs; regenerate when intentional.
- **Property tests** (quickcheck-style): `parse(print(ast)) == ast`;
  `compile(program)` is deterministic.
- **Fuzzing**: random byte sequences → parser terminates (fuel) and
  never crashes (invariants). Mutation-based fuzzing of valid programs.
- **Differential testing**: reference and self-host on the same input.
  Already implicit in pingpong; make it a first-class harness.

### I. Concurrency story

Reference `DiagnosticBag` has a lock. Self-host is single-threaded. If
we parallelize: bag needs concurrency-safe access, phase invariant
checks need barriers, provenance and stable mangling become more
important. If we stay sequential, drop the locks. Decide explicitly.

### J. Hash-consing / canonical forms

Structurally-identical IR subtrees share memory. Benefits: equality
becomes pointer-equality; caching by argument graph. Cost: a cons-table
per allocation. Only worth it if profiling shows overhead.

### K. Plugin points / phase composition

Phases as first-class values — orderable, replaceable, instrumentable.
Enables experimental phases, external consumers (LSP, doc generators),
and research work without forking. Big refactor; not urgent.

---

Sequence smallest-first: (A) per-phase dumps and (B) invariant passes
are small and unlock a lot; (C) fuel is small and prevents catastrophes;
(D) typed AST is medium and unlocks the rest; (E)–(K) are larger or
speculative.
