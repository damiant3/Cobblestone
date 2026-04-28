# Backlog — Outstanding Work

## Active

Ordered by gating relationship.

| # | Item | Design Doc | Notes |
|---|------|-----------|-------|
| 1 | **Self-host bare-metal re-audit** | `docs/Active/Compiler/REF-LESSONS-FOR-SELFHOST.md` | C# target lifting complete (selfhost-cs sweep 75/54/20/11/3 post CL 209; 3 fails all target-semantic). Bare-metal target (`--compiler=selfhost`) still has gaps for MM4: IrRunState/IrGetState/IrSetState emit in X86_64, SSE enable on bare-metal entry, `record-set` → `__record-set` rename + Runtime cite, type-aware REPL print. Lessons doc tracks each. **Gates pingpong re-green.** |
| 2 | **Self-host parity re-audit** (post-MM4) | `docs/Active/Compiler/SELF-HOST-PARITY-AUDIT.md` (stale) | The existing matrix is tagged STALE-post-CL-128. Rewrite once MM4 is closed; anchor every row on the sample-battery gate rather than on code-shape pattern-matching. |
| 3 | **Second Bootstrap (MM4)** | `docs/Active/Compiler/SECOND-BOOTSTRAP.md` | Port x86-64 backend to Codex. 8 phases. Gated on #1 so pingpong is green first. |
| 4 | Escape copy bare-metal | `docs/Designs/Memory/CAMP-IIIA-ESCAPE-ANALYSIS.md` | Skip removed, tests passing. Rearchitect deferred till after MM4. |

## Needs Design Doc

| Item | Why | Notes |
|------|-----|-------|
| Crypto primitives | Ed25519 + SHA-256 on bare metal, constant-time | Unblocks trust lattice, identity, agent protocol, CDX verification |
| Identity & authentication | Key pairs, first-boot ceremony, trust bootstrap | Unblocks agent protocol, policy enforcement |
| The Verifier | Type-check untrusted code at install time | The hardest sub-problem. Unblocks Codex.OS security model |
| The Shell | Prose-as-command interface | Unblocks Codex.OS user interaction |
| Boot sequence / init | First-boot, capability root, fact store loading | Unblocks Codex.OS on real hardware |
| Process IPC | Inter-process communication, typed channels | Unblocks multi-process OS |
| Scheduler & quotas | RT scheduling, CPU/memory quotas, watchdog | Unblocks resource enforcement |
| Compiler concurrency story | DiagnosticBag lock; phase invariant barriers; thread-safety of ExprTypes table, FactStore, ChapterLoader caches; parallel chapter compilation vs sequential | Decide explicitly post-MM4. Currently self-host is single-threaded so the reference's `DiagnosticBag` lock is dead weight; either parallelize in earnest (bag races, invariant barriers, provenance+stable-mangling become load-bearing) or drop the locks. Section I of the diagnostics wishlist before it moved here. |

## Designed, Awaiting Implementation (after MM4)

| Feature | Design Doc | Effort |
|---------|-----------|--------|
| CDX binary format + loader | `docs/Designs/Codex.OS/CodexBinary.md` | Phase 2 of Second Bootstrap covers writer; loader is separate |
| Trust network | `docs/Designs/Codex.OS/TrustAndRuntime.md` | Medium-Large |
| Agent protocol (7 messages) | `docs/Designs/Codex.OS/TrustAndRuntime.md` §1 | Medium |
| Policy contract | `docs/Designs/Codex.OS/TrustAndRuntime.md` §2 | Medium |
| Forensics layer | `docs/Designs/Codex.OS/TrustAndRuntime.md` §3 | Medium |
| Capability refinement Steps 2-8 | `docs/Designs/Language/CAPABILITY-REFINEMENT.md` | Weeks |
| Structured concurrency runtime | `docs/Designs/Features/CAMP-IIIC-STRUCTURED-CONCURRENCY.md` | ~1 week |
| Stdlib expansion (Set, Queue, StringBuilder, TextSearch) | `docs/Designs/Features/STDLIB-AND-CONCURRENCY.md` | ~2 weeks |
| V2 Narration Phases 4-6 | `docs/Designs/Language/V2-NARRATION-LAYER.md` | Medium |
| V3 Repository federation | `docs/Designs/Features/V3-REPOSITORY-FEDERATION.md` | Large |

## Deferred Indefinitely

| Item | Reason |
|------|--------|
| ARM64 backend | Abandoned 2026-03-29 |
| RISC-V backend | Abandoned 2026-03-29 |
| Codex.UI substrate | No design doc, medium-term |
| Codex.OS on real hardware (WHPX) | After MM4 + basic OS stack |
| Floppy disk 1.44 MB target | 64 MB achieved, streaming optimizations later |
| Multi-language syntax | Large effort, no design doc |

## Language Evolution (small, designed)

| Item | Design Doc | Notes |
|------|-----------|-------|
| Legacy transpilation backends | — | Staying in C#, not being ported |

## Compiler Correctness (low priority, non-blocking)

| # | Item | Notes |
|---|------|-------|
| 3 | NetworkSync test failures | 4 tests need self-contained peer or integration-only marking |
| 5 | `text-to-double-bits` bare metal implementation | On x86-64 bare metal, `text-to-double-bits` falls through to `__text_to_int` (integer parser). Need a proper `__text_to_double` runtime helper that parses decimal text to IEEE 754 bits. Not blocking — the builtin is only called at compile time when the compiler runs as .NET, not at runtime on bare metal. |

## Sample validation

`tools/sweep.sh` is the canonical regression gate. Every sample either has a `.expected` snapshot (runtime output diffed), a `.failing` sidecar (compile must fail with a specific CDX code), or a `.skip` sidecar (documented reason).

Runs the bare-metal selfhost via `build-output/bare-metal/Codex.Codex.elf` (from `pingpong-self.sh`) under QEMU. `--jobs=N` for parallel runs.

Current state (CL 463): **72 verified + 22 expected-fail diagnostics + 11 skipped + 0 fail**, out of 105 samples.

Skipped samples are each documented in their `.skip` sidecar. Four are known-broken or not-yet-implemented features (effect handlers, fork/await on bare metal, linear types runtime); four are type-check-only tests with no `opening`; two are missing-dep stubs. They act as TODO markers in the battery — when the capability lands, delete the `.skip` and snapshot.

Diagnostic negative tests cover CDX1000 (parser-resync), CDX2001 (type mismatch), CDX2002 (unknown ctor), CDX2033 (let-effectful), CDX3001 (duplicate def/ctor — 2 tests), CDX3002 (unknown name), CDX3010 (unresolved cite). ~50 live diagnostic codes; 8 covered.

All real bugs surfaced during the test-battery build-out are resolved. Remaining skips are type-check-only samples with no `opening` entry point or missing-dep stubs; track each at its `.skip` sidecar.

Recently fixed REF bugs (in this CL):

| Sample | What it was | Fix |
|---|---|---|
| `samples/string-ops.codex` | `count-letters "hello world 123"` returned `1`; expected `10`. | `is-letter` / `is-digit` / `is-whitespace` emit in `X86_64CodeGen` was mutating the source register in place (`SubRI(rd, 13); Setcc(rd)`). When the source register happened to be a parameter register — which it is inside a tail-recursive function that reads a param directly into the builtin call — the parameter got clobbered, and the next TCO iteration saw the clobbered value. Same shape also affected `char-at` (destructive `AddRR(idx, strLoaded)` on the caller's index param). Both fixed by allocating a fresh temp, copying, and mutating that. |
| `samples/expr-calculator.codex` | Self-tests failed with wrong numeric values. | Not a compiler bug — the sample's digit-parsing logic used `char-code c - 48` assuming ASCII, but Codex Text is CCE-encoded where `'0'` is byte 3, not 48. Changed `- 48` to `- 3`. Recursive-descent parser now passes all 10 self-tests including operator precedence and parens. |
| `samples/polymorphism-coverage.codex` | `#PF` at `RIP=0x106a45` during `opening`. | Not a polymorphism bug — the sample is a type-checker sweep with no `opening` entry point. Bare-metal trampoline called the missing symbol and landed on garbage. Added a parallel `samples/poly-runtime.codex` that actually drives each polymorphic shape (id, const, opt-map, unbox, rebox, swap-pair, apply-fn) — every pattern produces the correct runtime value. Polymorphism is fine in REF. |
| `samples/state-demo.codex` | `run-state 0 (get; set(x+10); get; set(y*2); get)` returned `0`; expected `20`. | `X86_64CodeGen.EmitExpr` had no cases for `IRRunState` / `IRGetState` / `IRSetState` — they fell through to `EmitUnhandled` (returns 0). Same class of gap as the missing `IRLambda` case. Added dedicated emit methods: state lives in a single stack slot scoped dynamically around each `run-state`'s body; nested `run-state` saves/restores through a field. Integer-width state works end-to-end; wider state would need heap boxing (defer until a sample needs it). |

Previously broken, now fixed:

| Sample | Symptom | Fix |
|---|---|---|
| `samples/shapes.codex` | `#UD` at SSE `mulsd` instruction after `READY` | REF's bare-metal `__start` never enabled SSE. Added CR4.OSFXSR+OSXMMEXCPT and cleared CR0.EM / set CR0.MP in `X86_64CodeGen.cs` before entering long mode. Runtime fixed; separately, bare-metal does not yet format a Number result to text (minor feature gap, not a miscompile). |
| `samples/list-test.codex` | Runtime asserts `FAIL: length expected [5] got [0]`, then `#GP` | Two compounding bugs: (1) name collision — `foreword/List.codex`'s `list-length : ConsList a -> Integer` silently lost to the builtin `list-length : List a -> Integer`. Fixed by Step 4 cite-gating + emitter priority (user-defined function shadows same-named builtin). (2) Polymorphic return type — `head : ConsList a -> a` applied to `ConsList Integer` lowered with `a` stuck as a `TypeVariable` because `SubstituteTypeVarsFromArg` didn't walk into `ConstructedType` / `SumType` / `RecordType` arguments, and the IR didn't consult the type-checker's deeply-resolved per-expression types. Fixed by threading `TypeChecker.ExprTypes` into `Lowering` and preferring the resolved type in `LowerApply`. All 15 list-test checks now PASS. |
| `samples/closure-in-record.codex` | Megabytes of garbage bytes on serial, then fault | Three chained gaps in closure handling: (1) X86_64 emitter had no `IRLambda` case at all — lambdas fell through to `EmitUnhandled` which returns 0; the resulting "0 as text pointer" cascaded into `__str_concat` writing through `ptr=0+offset` until it hit unmapped memory. Fixed by a new `src/Codex.IR/LambdaLifting.cs` pass that runs after `LowerCitedDefs` and rewrites every `IRLambda` into a synthesized top-level `IRDefinition` + `IRApply` chain partially applying the free variables. (2) `EmitApply` flattened the whole `IRApply` chain into a single call regardless of the named function's arity, so `(make-adder 10) 5` tried to call `make-adder` with two arguments. Fixed by tracking each user function's arity in `m_userDefinedArity` and splitting the over-apply into `direct-call + indirect-call` phases. (3) Applying a closure obtained from a non-`IRName` expression (`IRFieldAccess`, `IRApply` result) emitted no call at all — `funcName == null` fell off the end of `EmitApply`. Fixed by evaluating such a function expression into a closure local and performing an indirect call against it. All closure probes (direct lambda, lambda as def value, capturing adder, closure stored in record field) produce the correct output. `closure-in-record.codex` prints `a:13` then `b:103` as designed. |


