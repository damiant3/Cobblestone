# OnePipeline — Text vs Binary Pipeline Drift (Forensic Analysis)

## TL;DR

The design intent was: **one compiler pipeline, the emitter is the only fork.**

The code is not that. Since `2026-04-11`, the binary path has quietly forked into an entire parallel set of functions (`bin-tokenize`, `bin-scope`, `bin-build-env`, `bin-analyze`, `bin-emit-codegen`, `bin-finalize`, `emit-defs-binary-gated`) with **different scoping, no type checking, and different error handling**. The text pipeline kept the original semantics. This wedge is the root cause of tonight's C7 crash — a 2-arg call site linked into a 1-arg function because the binary path's IR-level rename missed a coverage case that the text path's AST-level rename handled.

## The wedge — exact commit

**`4029a82c95471690fde02b24bc99d4baa7a579bb`** — 2026-04-11 07:37:25 -0700

> `feat: streaming binary pipeline with per-def heap-restore`

This commit removed `scope-achapter`, `check-chapter`, and `lower-chapter` from `compile-to-binary` and replaced them with a per-def streaming loop (`emit-defs-binary`) that performs IR-level renaming instead of AST-level renaming. From this point on, the binary path and the text path are no longer the same pipeline with different emitters — they are two different pipelines.

Before this commit, `compile-to-binary` was literally:

```codex
compile-to-binary (source) (chapter-name) =
 let tokens = tokenize source
 in let st = make-parse-state tokens
 in let scan = scan-document st
 in let assignments = build-all-assignments (scan.def-headers) 0 []
 in let colliding = find-colliding-names assignments
 in let doc = parse-document (make-parse-state tokens)
 in let ast = desugar-document doc chapter-name
 in let scoped = scope-achapter ast colliding assignments
 in let check-result = check-chapter scoped
 in let ir = lower-chapter scoped (check-result.types) (check-result.state)
 in x86-64-emit-chapter ir
```

— identical to `compile` (text path) except the final `x86-64-emit-chapter` vs `emit-full-chapter`. **This is the target state we must return to.**

## Commit timeline — every step that deepened the wedge

| Date | Commit | What it did | Wedge impact |
|------|--------|-------------|--------------|
| 2026-04-08 | `70f77235` | `feat: Phase 8 — compile-to-binary path + write-binary builtin` | **Clean baseline.** `compile-to-binary` is text-identical minus the emitter. No wedge. |
| 2026-04-11 | `4029a82c` | `feat: streaming binary pipeline with per-def heap-restore` | **THE WEDGE.** Removed `scope-achapter`, `check-chapter`, `lower-chapter` from `compile-to-binary`. Introduced `emit-defs-binary` streaming loop with per-def `scope-def-name` + `rename-ir-expr`. No type-check, no AST scope, no `apply-cite-overrides`. Motivation was memory (heap-restore per def). |
| 2026-04-11 | `f2909b5` | `Revert "feat: streaming binary pipeline with per-def heap-restore"` | Tried to back out. |
| 2026-04-11 | `5d064a1` | `Reapply "feat: streaming binary pipeline..."` | Reapplied. Wedge permanent from here. |
| 2026-04-11 | `f8a8fdd` | `fix: heap-save IrName dispatch, O(n²) ELF concatenation, runtime helper cleanup` | Patched symptoms inside the streaming path. |
| 2026-04-12 | `1484914c` | `feat(diagnostics): streaming binary — parse-bag capture + chapter gating` | Split the streaming loop into **two passes**: `parse-all-bodies` then `emit-defs-binary-gated`. Added `close-bad-chapters-under-citations` — error handling unique to binary path. Text path has nothing equivalent. |
| 2026-04-12 | `266be6b6` | `fix(compile-to-binary): scope def headers before type registration` | **Band-aid for a bug caused by `4029a82`.** Since `scope-achapter` was gone, `register-def-headers` was called with raw names. Added `scope-def-headers` (headers-only scope). Commit message reads: *"This mirrors what scope-achapter already does on the non-streaming paths"* — i.e. it acknowledges duplication in text, and still duplicates. |
| 2026-04-12 | `02b71e95` | `feat(diagnostics): BINARY-DIAG mode with per-stage phase markers` | Refactored the now-diverged pipeline into 6 named stages (`bin-tokenize`/`bin-scope`/`bin-build-env`/`bin-analyze`/`bin-emit-codegen`/`bin-finalize`) with dedicated records (`BinaryScan`, `BinaryScope`, `BinaryEnv`, `BinaryAnalysis`). **This crystallized the wedge into an architecture** — each stage is now independently callable and named, making the duplication look deliberate. |
| 2026-04-13 | `67a7a615` | `emitdefs` (short message — added `emit-defs-diag-loop`) | Added a second streaming loop duplicate (`emit-defs-diag-loop`) — briefly three copies of the per-def loop coexisted. |
| 2026-04-13 | `ae69afbd` | `fix(emit-binary-diag): use production emit-defs-binary-gated, not emit-defs-diag-loop` | Collapsed the duplicate loop back to one. |
| 2026-04-13 | `a29e572d` | `fix(x86-64): preserve in-tail-pos across builtin/ctor/partial calls (CDX-C3)` | Patched a TCO bug inside binary path. |
| 2026-04-14 | `2e91955a` | `fix(self-host bare-metal): mirror C6 fixes into codex codegen` | Most recent C6 fix; unaware of the scoping wedge underneath. |

## Every current difference between the two pipelines

### Text pipeline (`compile` in `Codex.Codex/main.codex:13-24`)

```
compile source chapter-name =
  tokenize(source, 1)
  → make-parse-state
  → scan-document
  → build-all-assignments (from scan.def-headers)
  → find-colliding-names
  → parse-document (full body parse into AST)
  → desugar-document
  → scope-achapter ast colliding assignments       ← AST-level rename (names + ALL bodies, full-coverage)
  → check-chapter scoped                            ← TYPE CHECK bodies
  → lower-chapter scoped (types, state)             ← lower entire chapter at once
  → emit-full-chapter ir (scoped.type-defs)         ← emitter (C# or CIL)
```

### Binary pipeline (`compile-to-binary` in `Codex.Codex/main.codex:392-399`)

```
compile-to-binary source chapter-name =
  bin-tokenize source             -- Codex.Codex/main.codex:320
  → bin-scope                     -- Codex.Codex/main.codex:332
  → bin-build-env                 -- Codex.Codex/main.codex:344
  → bin-analyze                   -- Codex.Codex/main.codex:358
  → bin-emit-codegen              -- Codex.Codex/main.codex:374
  → bin-finalize                  -- Codex.Codex/main.codex:387
```

Unrolled:

```
bin-tokenize        = tokenize + make-parse-state + scan-document + (desugar-type-def on type-defs eagerly)
bin-scope           = build-all-assignments + find-colliding-names + scope-def-headers   ← HEADERS ONLY
bin-build-env       = build-type-def-map + register-type-defs + register-def-headers(scoped-headers) + collect-ctor-bindings
bin-analyze         = parse-all-bodies + group-parse-bags + collect-bad-chapters + close-bad-chapters-under-citations + merge-all-parse-bags
bin-emit-codegen    = x86-64-init-codegen-streaming + emit-runtime-helpers + bare-metal-trampoline + emit-defs-binary-gated
  per-def loop (main.codex:516-552):
     build-chapter-rename-map colliding assignments slug   ← NO apply-cite-overrides (scope-adefs has it)
     desugar-def
     scope-def-name (mangle def name)
     lower-def adef all-types ust
     rename-ir-expr rn ir-def.body                         ← IR-level rename (rename-aexpr-on-AST replacement)
     emit-function scoped
bin-finalize        = x86-64-finalize
```

### Delta table

| Concern | Text pipeline | Binary pipeline | Consequence |
|---------|---------------|------------------|-------------|
| AST-level scoping | `scope-achapter` → `scope-adefs` → `rename-aexpr` on every body node | **Missing.** Only `scope-def-headers` (mangles def names) + per-def `rename-ir-expr` | Renamer coverage differs. Any node type that `rename-aexpr` handles but `rename-ir-expr` doesn't (or where lowering inserts a name AFTER rename, etc.) leaks unrenamed names into codegen. |
| `apply-cite-overrides` | Called from `scope-adefs` in `ChapterScoper.codex:435` | **Not called** in `emit-defs-binary-gated` (`main.codex:523` uses plain `build-chapter-rename-map`) | Cite-based overrides for colliding names don't apply in binary mode. Matters for any chapter that uses `cites X (name)` when `name` is a colliding import. |
| Type checking | `check-chapter scoped` runs full checker over bodies | **Not run.** Only `lower-def` per-def, which does type lookups but not unification/inference | Binary path cannot surface type errors. Also means parameter/return types threaded through `lower-def` may differ from what `check-chapter` would have inferred. |
| Lowering unit | `lower-chapter` — whole AST at once | `lower-def` — one def at a time | `lower-def` runs in a per-def context that doesn't see cross-def state. Any lowering step that relied on full-chapter context silently behaves differently. |
| Parse layout | `parse-document` → one AST for whole chapter | `parse-all-bodies` → re-parses each body independently from `body-pos` | Two parsers walking same tokens. A parser bug that only fires on per-body parsing, or a parse-state invariant that doesn't hold when starting mid-stream, surfaces here. |
| Error surfacing | `check-chapter` diagnostic bag + parser errors | `bad-chapters` gating via `close-bad-chapters-under-citations` — a chapter with a parse error gets all its defs SKIPPED, AND every chapter citing it gets skipped | Binary path's "fail quietly by skipping chapters" is a different model. Text path reports errors up; binary path elides silently. |
| Rename timing vs lowering | Rename BEFORE lower | Rename AFTER lower | Any name used during lowering (e.g. when `lower-def` queries type bindings by name) sees the unrenamed name. `register-def-headers` was given mangled headers to work around this — but the fix is incomplete. |
| State records | Implicit (just threaded args) | Explicit records: `BinaryScan`, `BinaryScope`, `BinaryEnv`, `BinaryAnalysis`, `ParsedDef` | Not a correctness issue, but more surface area to maintain. Every change to the pipeline needs to update these records. |

### The specific failure mode we hit tonight (C7)

1. `resolve-type-expr` exists in two chapters with different signatures:
   - `Codex.Codex/Types/TypeChecker.codex:6-13` — 2-arg `List TypeBinding -> ATypeExpr -> CodexType`
   - `Codex.Codex/IR/LoweringTypes.codex:180-198` — 1-arg `ATypeExpr -> CodexType`
2. They are a real collision and SHOULD be mangled to `type-checker_resolve-type-expr` and `lowering_resolve-type-expr`.
3. On the text path via `scope-achapter` → `rename-aexpr`, mangling propagates through every AST reference. `pingpong.sh` passes because the text path is whole.
4. On the binary path via `emit-defs-binary-gated`, mangling propagates through `rename-ir-expr` on each IR body. **Something in this path misses the rename** — the 2-arg caller ended up with an unrenamed `resolve-type-expr` in its IR, linked to whichever def was registered under that name. Result: a 2-arg call site (`push tdm; push texpr; call resolve-type-expr`) linked into a 1-arg function (which expects only the texpr in RDI, ignores RSI). RDI then held the `tdm` list pointer. The 1-arg body treated the list as an `ATypeExpr`, read `[list+8]` (past an empty list), got 0, dereferenced 0 (BIOS IVT area), `#GP`.
5. Renaming the 1-arg `resolve-type-expr` to `resolve-type-expr-1arg` removed the collision entirely, so no mangling was needed, and the bug disappeared.

**Exact coverage gap is still to be pinpointed** (candidates: something in `lower-def` emitting a fresh `IrName` after `rename-ir-expr` would have run; or `rename-ir-expr` missing a node type; or the param-type bindings returned by `lookup-type-bsearch` holding unrenamed references). The collapse fix subsumes that by removing the duplicate scoper entirely.

## What must change — collapse plan

Return to the `70f77235` model: **one pipeline, emitter is the only fork.**

Concrete steps:

1. **Delete** from `Codex.Codex/main.codex`:
   - `BinaryScan`, `BinaryScope`, `BinaryEnv`, `BinaryAnalysis` records
   - `bin-tokenize`, `bin-scope`, `bin-build-env`, `bin-analyze`, `bin-emit-codegen`, `bin-finalize`
   - `emit-defs-binary-gated`, `emit-defs-diag-loop` (if still present)
   - `scope-def-headers` (only caller was `bin-scope`)
   - `parse-all-bodies`, `group-parse-bags`, `collect-bad-chapters`, `close-bad-chapters-under-citations`, `merge-all-parse-bags`, `has-slug`, and anything else unique to the binary path
   - Per-def `rename-ir-expr` call site
2. **Rewrite** `compile-to-binary` to be the text `compile` with `emit-full-chapter` replaced by `x86-64-emit-chapter`. Exactly one-line difference.
3. **Keep streaming** only at the emit step if needed for memory: iterate `lower-chapter`'s output defs one at a time into `x86-64-emit-chapter`. Streaming is an emit-side concern; it does NOT justify a separate scoping/type-checking path.
4. **Verify**: `pingpong.sh` stays green (text). Binary pingpong stage 1 still passes. Binary pingpong stage 2 is the acceptance test for C7.
5. **Re-open CDX-C7** after collapse — if the real coverage gap in `rename-ir-expr` vs `rename-aexpr` was the underlying bug, the collapse fixes it. If any other binary-only bug remains, it's now exposed without the scoping confound.

Post-collapse invariant: **searching `grep -c "bin-" Codex.Codex/main.codex` must return 0 (or only uses like "binary-pingpong" strings).** The word "binary" in function names is a code smell; the pipeline doesn't need to know its output format beyond the last call.

## Rules going forward

- No new `bin-*` functions. If you need per-output-format behavior, pass the emitter as a parameter.
- Any change that adds a step to one pipeline must add it to the other (or prove it's emitter-specific).
- Diagnostic instrumentation (phase markers, stage records) wraps the pipeline — it does not replace the pipeline.
- The narrow parity invariant in `CLAUDE.md` rule 8 ("anything that affects the compilation output must mirror precisely") applies inside the compiler itself: text and binary compilers must not mirror each other via two sets of code — they must BE one set of code.
