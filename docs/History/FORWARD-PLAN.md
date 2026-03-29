# Forward Plan

*Updated after nested-match fix session.*

This document captures what's done, what's next, and what the open questions are.
It replaces the iteration handoff docs as the single source of truth for project direction.

Detailed deliverable checklists live in [08-MILESTONES.md](08-MILESTONES.md).
Design choices live in [DECISIONS.md](DECISIONS.md).

---

## Where We Are

The Codex compiler is self-hosting. The full pipeline works:

```
Source (.codex) → Lex → Parse → Desugar → NameResolve → TypeCheck → Lower → Emit → dotnet/node/rustc/...
```

| Metric | Value |
|--------|-------|
| C# projects | 32 |
| Test count | 654 (all passing) |
| Codex source | ~2,600 lines across 21 .codex files |
| Bootstrap parity | ~286 records, ~310 functions in output.cs |
| Backends | C#, JavaScript, Rust, Python, C++, Go, Java, Ada, Babbage, Fortran, COBOL, **IL** (12 total) |
| LSP | Diagnostics, hover, completion, go-to-definition, symbols, semantic tokens |
| Repository | Content-addressed fact store with proposals/verdicts, import resolution |
| IDE support | TextMate grammar for VS 2022 + VS Code |
| Build system | Incremental builds, parallel front-end, parallel multi-target emission |
| Prose | Chapter/Section structure, prose templates (record & variant from bullet lists) |
| Parser | Error recovery: partial definitions, partial types, bad fields/constructors skipped |

---

## What's Done (Completed Milestones)

All of the following are ✅. See [08-MILESTONES.md](08-MILESTONES.md) for deliverable checklists.

| # | Milestone | Summary |
|---|-----------|---------|
| M0 | Foundation | Project structure, `Codex.Core`, content hashing, diagnostics |
| M1 | Hello Notation | Lexer, parser, CST/AST, desugaring |
| M2 | Type Checking | Bidirectional type checker, sum/record types, pattern matching |
| M3 | Execution via C# | IR, C# emitter, CLI (`codex check/build/run`) |
| M9 | LSP & Editor | Diagnostics, completion, hover, go-to-def, symbols, semantic tokens |
| M12 | JS & Rust backends | 3 backends total, 39 integration tests, TCO in all three |
| M13 | Self-hosting | Stage 0 → output.cs → Stage 1 → compiles Codex. C# generics in both stages. |
| M10 | Proofs | Refl, sym, trans, cong (bidirectional), induction with IH, lemma application |
| — | 8 more backends | Python, C++, Go, Java, Ada, Babbage, Fortran, COBOL. 165 integration tests. |
| — | IDE / syntax | TextMate grammar, VS 2022 `.pkgdef`, `codex.project.json`, `codex init` |
| — | Incremental builds | SHA256 content hashing, parallel front-end, parallel multi-target emission |

## What's Partially Done

| # | Milestone | What works | What's left |
|---|-----------|------------|-------------|
| M4 | Prose Integration | Chapter/Section parsing, prose templates (record/variant from bullets), prose-aware compilation | The Reader (`codex read`): formatted prose rendering to terminal |
| M5 | Effects | Effect rows, effect checking, effect polymorphism (row variables), Console/State, C# emission, **`run-state` effect handler** (`get-state`, `set-state`, `run-state` built-ins; type checker allows effects inside handler scope and eliminates them from the return type; C# emitter emits mutable-cell closure; `do` blocks as handler arguments). 12 effect handler tests. | User-defined effects, additional built-in handlers |
| M6 | Linear Types | Linearity annotations, `LinearityChecker` with usage counting (CDX2040/2041/2042), if/match branch merging, let-forward tracking, C# runtime checks. 6 tests. Wired in pipeline. | Richer integration: linear values through record fields, linear closures |
| M7 | Repository | Fact store, content hashing, CLI commands, `import` resolution from store via `IModuleLoader` | Views (single-user views, view consistency checking) |
| M8 | Dependent Types | Dependent function types, type-level arithmetic, proof obligations | Full `Vector` type with `append` end-to-end |
| M10 | Proofs | Induction, cong, lemma application, IH registration, 9 proofs in sample | Type-level function reduction (needed for non-trivial inductive steps), arithmetic induction with Peano encoding |
| M11 | Tests | Property-based tests, integration tests (689 total), corpus emission (165 per-sample-per-backend) | Fuzz testing, CI configuration |
| — | IL Emitter | `Codex.Emit.IL` project, `IAssemblyEmitter` interface, CLI wired (`--target il`). Emits working `.exe`/`.dll` via `System.Reflection.Metadata`. Handles: integer/text/boolean/number literals, static methods with parameters, if/else branching, let bindings, negation, binary ops, function application (including recursive/forward calls, curried multi-arg, nested composition), **records (IL classes with fields + constructors), sum types (abstract base + sealed subclasses), field access (`ldfld`), pattern matching (`isinst` branch chains with sub-pattern binding)**. 38 integration tests (emission + PE validation + runtime execution). Verified: hello→25, factorial→3628800, arithmetic→37, person→"Hello, Alice!", shapes with field extraction. | Generics, TCO (`tail.` prefix), full bootstrap (`codex build codex-src --target il`) |

---

## Recent Fixes (Bootstrap-Critical)

Three Stage 0 compiler bugs were fixed that caused nested `when`/match
expressions to emit broken C#. All three blocked self-compilation because
the Codex source uses nested matches heavily (e.g., `unify_structural`
pattern-matches a type, and each branch pattern-matches sub-structure).

### 1. `LowerMatch` infers type from branches when `expectedType` is `ErrorType`

**File:** `src/Codex.IR/Lowering.cs`

When a `let` binding's value is a `when`/match, the lowering passed
`ErrorType` as the expected type. The match then reported `ErrorType`
as its result type, causing `IRLet.NameType` to be `ErrorType`, which
emitted as `Func<object, …>`. Now `LowerMatch` resolves the type from
the first non-error branch body, matching how `LowerIf` already works.

### 2. Column-based `when` branch scoping in parser

**File:** `src/Codex.Syntax/Parser.Expressions.cs`

Nested `when` expressions (e.g., `when a if X -> when b if Y -> …`)
had the inner match greedily consuming branches that belonged to the
outer match. Now the parser records the column of the first `if`
keyword and only accepts subsequent `if` keywords at the same column
(or same line for inline matches). This cleanly distinguishes inner
vs outer branches.

### 3. Unique pattern binding names in C# emission

**File:** `src/Codex.Emit.CSharp/CSharpEmitter.Match.cs`

With nested matches now emitting all branches, C# pattern variable
names like `_mIntegerTy_` clashed between outer and inner matches in
the same expression scope. Added a `m_matchCounter` to generate unique
names (`_mIntegerTy0_`, `_mIntegerTy1_`, etc.).

---

## Bootstrap Status (Current)

Stage 0 → Stage 1 → Stage 1 output pipeline runs end-to-end.

| Metric | Stage 0 output | Stage 1 output |
|--------|---------------|----------------|
| Lines | 3,753 | 1,051 |
| Size | 192 KB | 111 KB |
| `object` references | 4 (all correct) | 328 |
| Unresolved generics (`T{id}`) | 0 | 158 |
| Functions with concrete types | ~310 | 92 |
| Unification errors | 0 | 1,863 |
| Type defs | ✅ correct | ✅ correct (fields, constructors) |
| Nested `when` branches | ✅ all emitted | ✅ all emitted |

**What works:** Type definitions (records, sum types) emit correctly with
fields. Nested `when`/match expressions emit all branches. The full pipeline
(lex → parse → desugar → check → lower → emit) completes without crashing.

**What's broken:** The self-hosted type checker produces 1,863 unification
errors, causing most function parameters and return types to remain as
`object` or unresolved `T{id}`. Root causes to investigate:

1. **Record field access** — `d.name`, `tok.text`, etc. require the type
   checker to know the record type of the scrutinee to resolve field types.
   The self-hosted checker may not have `ConstructorMap`/`TypeDefMap` wired.
2. **Curried function application** — `f(a)(b)` requires peeling `FunTy`
   layers. If the function's type isn't resolved, all downstream types fail.
3. **`deep-resolve` coverage** — May not be called on all type bindings
   before emission, leaving `TypeVar(id)` as `T{id}` in output.

**Next step for bootstrap:** Fix the self-hosted type checker's unification
to resolve concrete types. Compare `check-module` in `TypeChecker.codex`
against `TypeChecker.cs` to find the gaps.

---

## What's Next (Priority Order)

### Tier 1: Solidify What Exists

**1. Error recovery in parser** — ✅ Done.
Parser now recovers from errors at all levels: type definitions produce
`ErrorTypeBody` instead of backtracking, definitions with missing `=`
produce partial `DefinitionNode` with `ErrorExpressionNode`, record bodies
skip bad fields, variant bodies skip bad constructors. Multiple definitions
after errors are parsed correctly. 11 new tests. New diagnostic codes:
CDX1050 (bad type body), CDX1051 (bad record field), CDX1052 (bad variant ctor).

### Tier 2: Complete Partial Milestones

**1. Effect handlers (M5)**
`run-state` effect handler is implemented: `get-state`, `set-state`, `run-state`
built-ins with type checker support for effect elimination and C# mutable-cell
emission. 12 tests. Remaining: user-defined effects and additional built-in
handlers (e.g., `run-reader`, `run-writer`).
Estimated: medium.

**2. Views (M7)**
The repository stores facts and resolves imports, but there's no view layer.
A View maps names to definitions such that all definitions are mutually
consistent. Single-user views first, then multi-user consensus.
Estimated: medium.

**3. The Reader (M4)**
`codex read <file>` renders a prose-mode document to the terminal with
formatted prose, highlighted notation blocks, and structured layout.
`Codex.Narration` is the project for this — currently empty.
Estimated: small-medium.

**4. Type-level function reduction (M10)**
Proof steps that require unfolding function definitions currently use
`assume`. The proof checker needs to normalize type-level expressions
by inlining function bodies. Arithmetic induction with Peano encoding
also depends on this. Estimated: medium.

### Tier 3: New Capabilities

**5. Direct IL emission (`Codex.Emit.IL`) — native .exe without C# transpile**
The C# emitter produces text that must be fed to `dotnet build` to get an
executable. This means Codex always depends on the C# toolchain at build time.
A direct IL backend would emit a .NET PE assembly (`.exe` / `.dll`) from the
IR, skipping the C# intermediary entirely.

Pipeline today:
```
.codex → Lex → Parse → … → Lower → CSharpEmitter → .cs → dotnet build → .exe
```

Pipeline with IL emitter:
```
.codex → Lex → Parse → … → Lower → ILEmitter → .exe (directly)
```

**Approach:** Use `System.Reflection.Metadata` + `System.Reflection.PortableExecutable`
(both in-box in .NET 8) to write raw IL bytes and PE headers. This is the same
infrastructure Roslyn uses. No external dependencies.

**New project:** `src/Codex.Emit.IL/` implementing `IAssemblyEmitter`
(since `ICodeEmitter.Emit` returns `string`, and this returns `byte[]`).

**CLI integration:** `codex build . --target il` produces a runnable `.exe`
directly. The existing `--target cs` continues to work for transpile scenarios.

**Bootstrap significance:** Once the IL emitter can compile the full Codex
source, the compiler can produce its own `.exe` without any C# toolchain
dependency. That's the final step to true self-hosting — Codex compiles itself
to an executable with no external compiler involved. At that point,
`Codex.Codex` can build with a previously-built copy of itself rather than
round-tripping through C# and MSBuild.

**Incremental plan:**
1. ~~Scaffold `Codex.Emit.IL` project, `IAssemblyEmitter` interface, wire into CLI~~ ✅
2. ~~Emit a working `.exe` for a trivial `main = "Hello"` program~~ ✅
3. ~~Emit static methods (the module class pattern the C# emitter uses)~~ ✅
4. ~~Records and sum types (sealed record → IL class hierarchy)~~ ✅
5. ~~Pattern matching (the `switch` dispatch the C# emitter generates → IL `isinst` branch chains)~~ ✅
6. Generics (the C# emitter's generic function strategy → IL generic method defs)
7. Tail call optimization (IL `tail.` prefix or loop conversion)
8. Full bootstrap: `codex build codex-src --target il` produces `Codex.exe`

Steps 1–5 are done. The IL emitter handles literals, parameters, binary ops,
negation, if/else, let bindings, function calls (including recursive, forward,
curried multi-arg, and nested composition), `Console.WriteLine` for
main-value printing, record types (IL classes with fields and constructors),
sum types (abstract base class + sealed subclasses), field access (`ldfld`),
and pattern matching (`isinst` branch chains with sub-pattern variable binding).
38 tests verify emission, PE structure, and runtime output.

**What step 6 requires:**
- Generic method definitions with type parameters in metadata
- Type parameter encoding in signatures
- Generic instantiation for call sites

**Estimated remaining:** medium (step 6 is the bulk; step 7 is small;
step 8 is integration/debugging).

**6. Package manager / dependency resolution**
The repository stores facts but there's no transitive dependency resolution
across modules. `import` currently resolves one level deep — it needs to
resolve transitively, handle version conflicts, and support views.
Estimated: large.

**7. Full `Vector` type (M8)**
The dependent type infrastructure works. Wire it up end-to-end: `Vector n a`
with `append : Vector m a → Vector n a → Vector (m + n) a`, compile and run.
Estimated: medium.

---

## Resolved Questions

These were open questions. Damian answered them; decisions are recorded in
[DECISIONS.md](DECISIONS.md).

### Language Design

1. **Module system** → **Prose-style imports (long-term).** The vision
   ([NewRepository.txt](Vision/NewRepository.txt)) describes capability-based
   prose imports: `I need: access to the filesystem to write files.` For now,
   `import TypeName` works as the notation-mode syntax and resolves from the
   repository's fact store. The prose-import design is an open question.

2. **Mutable state** → **Named-purpose mutability.** No general `ref` types.
   Mutable values must declare their purpose by naming convention —
   `UnificationState`, not `MutableRef`. See [DECISIONS.md](DECISIONS.md):
   "Codex-Side Type Checker — Threaded UnificationState."

3. **Type classes / traits** → **No explicit type classes.** Polymorphism is
   the compiler's problem. Prose handles subtyping naturally. Design work needed.

4. **String interpolation** → **No.** `++` and named functions only.
   See [DECISIONS.md](DECISIONS.md): "No String Interpolation Syntax."

5. **Tail call optimization** → **✅ Done.** All 11 backends convert
   self-recursive tail calls to loops. See [DECISIONS.md](DECISIONS.md):
   "Tail Call Optimization via Loop Conversion."

### Tooling

6. **CI pipeline** → **Deferred.** Not until there are users or funding.

7. **VS Code extension publishing** → **Deferred.** Same.

8. **Documentation generation** → **Deferred.** Low priority.

### Architecture

9. **Incremental compilation** → **✅ Done.** SHA256 content hash + timestamp.
   Parallel front-end, parallel emission. See M11c in the completed list above.

10. **Test preservation** → **Policy enforced.** Sample `.codex` files go in
    `samples/`, integration tests call the compiler on them. Every sample is
    a permanent regression test. Currently 628 tests across 7 test projects.

---

## Open Questions (Remaining)

1. **Prose-import syntax** — What exactly does a prose import look like?
   `I need: access to the filesystem` implies capability-based imports rather
   than module-based. How does this interact with the effect system?
   (See [NewRepository.txt](Vision/NewRepository.txt) Chapter 4.)

2. **Auto-memoization** — Which functions get memoized? All recursive pure
   functions? Only those marked? Only those with primitive arguments? What's
   the eviction policy? This needs a design doc before implementation.
   (See the Named-purpose mutability decision.)

3. **Subtype-inference for prose polymorphism** — "Add this fish to the list
   of animals" implies structural subtyping or coercion. Codex currently has
   nominal sum types. How do we bridge the gap?

4. **Heap-allocated stack frames** — For non-tail recursive functions, should
   we offer CPS transform + trampoline? TCO covers the tail-call case;
   this would cover general deep recursion.

---

## Technical Debt

| Item | Location | Impact | Effort | Status |
|------|----------|--------|--------|--------|
| Rust backend doesn't `.clone()` for recursive calls | `RustEmitter.cs` | Generated Rust won't compile for recursive `String`/`Vec` args (mitigated by TCO) | Medium | Open |
| JS uses `BigInt` (`42n`) for integers | `JavaScriptEmitter.cs` | Can't mix with `number` in arithmetic without explicit conversion | Low | Intentional |
| `output.cs` is tracked in git | `codex-src/output.cs` | Large generated file in version control | Low | Convenient for now |
| Iteration handoff docs are stale | `docs/ITERATION-*-HANDOFF.md` | 10 docs from old iteration model, superseded by this plan | Low | Archive or delete |
| `Codex.Narration` project is empty | `src/Codex.Narration/` | No prose rendering capability | Low | Deferred — see "The Reader (M4)" above |
| `TypeVariable emits as object` decision is superseded | `DECISIONS.md` | Superseded by "C# Generics for Polymorphic Functions" | None | Marked in DECISIONS.md |

---

## Principles (Unchanged)

See [10-PRINCIPLES.md](10-PRINCIPLES.md). The core principles haven't changed:

1. Ship working software at every milestone
2. Correctness over performance
3. Immutability by default
4. No premature abstraction
5. The human holds the vision; the tools serve the human

---

*This document tracks direction. The milestones doc
([08-MILESTONES.md](08-MILESTONES.md)) tracks deliverable status. The decision
log ([DECISIONS.md](DECISIONS.md)) tracks design choices.*
