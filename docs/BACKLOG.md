# Backlog — Outstanding Work

## Active

| # | Item | Design Doc | Notes |
|---|------|-----------|-------|
| 1 | **Second Bootstrap (MM4)** | `docs/Active/Compiler/SECOND-BOOTSTRAP.md` | Port x86-64 backend to Codex. 8 phases. The critical path. |
| 2 | Escape copy bare-metal | `docs/Designs/Memory/CAMP-IIIA-ESCAPE-ANALYSIS.md` | Skip removed, tests passing. Rearchitect deferred till after MM4. |
| 3 | **Self-host parity audit** | `docs/Active/Compiler/SELF-HOST-PARITY-AUDIT.md` | Systematic gap analysis: reference vs self-host, per data structure / diagnostic / runtime behavior / primitive. Recent fragility (silent binary crashes, missing Maybe/Set/xor, no parser diagnostics, no GPF handler) traces back to uncatalogued gaps. Priority: diagnostics and data structures first. |
| 4 | **Diagnostics, error reporting, staged compilation** | `docs/Active/Compiler/DIAGNOSTICS-AND-STAGING.md` | Self-host diagnostics lack source location; CDX codes scattered as string literals across 17 files with no registry; pipeline stages don't gate on prior failures so errors cascade. Plan: (1) docs-only CDX registry, (2) SourceSpan in self-host Diagnostic, (3) DiagnosticBag in self-host, (4) CDX codes as constants, (5) phase gating with PhaseResult, (6) organized output. Sequenced smallest-first, each shippable independently. |

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

## Designed, Awaiting Implementation (after MM4)

| Feature | Design Doc | Effort |
|---------|-----------|--------|
| CDX binary format + loader | `docs/Designs/Codex.OS/CodexBinary.md` | Phase 2 of Second Bootstrap covers writer; loader is separate |
| Trust network | `docs/Designs/Codex.OS/TrustAndRuntime.md` | Medium-Large |
| Agent protocol (7 messages) | `docs/Designs/Codex.OS/TrustAndRuntime.md` §1 | Medium |
| Policy contract | `docs/Designs/Codex.OS/TrustAndRuntime.md` §2 | Medium |
| Forensics layer | `docs/Designs/Codex.OS/TrustAndRuntime.md` §3 | Medium |
| Capability refinement Steps 2-8 | `docs/Designs/Features/CAPABILITY-REFINEMENT.md` | Weeks |
| Structured concurrency runtime | `docs/Designs/Features/CAMP-IIIC-STRUCTURED-CONCURRENCY.md` | ~1 week |
| Stdlib expansion (Set, Queue, StringBuilder, TextSearch) | `docs/Designs/Features/STDLIB-AND-CONCURRENCY.md` | ~2 weeks |
| V2 Narration Phases 4-6 | `docs/Designs/Features/V2-NARRATION-LAYER.md` | Medium |
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
| Inline const syntax (`name : T = v`) | `docs/Designs/Language/INLINE-CONST-SYNTAX.md` | Collapse two-line zero-param defs into one line. Small grammar add in both parsers. Land on `hex/inline-const-syntax` branch after CDX registry work. |
| Builtin dispatch table (HOF) | `docs/Designs/Language/BUILTIN-DISPATCH-TABLE.md` | Replace `emit-builtin` wall-of-else with a sorted list of `(name, emitter-fn)` records + bsearch. Blocked on verifying bootstrap2 handles closures-in-records. Own branch: `hex/builtin-dispatch-table`. |
| `when`-arm syntax: `is` / `otherwise` | `docs/Designs/Language/WHEN-ARM-SYNTAX.md` | Replace `if Pattern -> Result` with `is Pattern -> Result` inside `when`, and `_` wildcard with `otherwise`. Frees `if` to mean only "boolean conditional" and kills the cryptic underscore. Two-phase rollout, own branch: `hex/when-arm-syntax`. |
| IL emitter enhancements | Low priority, .NET dependency being retired |
| Legacy transpilation backends | Staying in C#, not being ported |

## Compiler Correctness (low priority, non-blocking)

| # | Item | Notes |
|---|------|-------|
| 1 | ~~Support negative number literals~~ | **DONE** — negative literals work end-to-end, all `0 - 1` idioms replaced. |
| 2 | Verify IL backend CCE assumptions | Document decision: .NET Char.Is* vs CCE ranges |
| 3 | NetworkSync test failures | 4 tests need self-contained peer or integration-only marking |
| 4 | ~~Reference compiler: `if ... then do ... else` parse failure~~ | **RESOLVED (2026-04-13)** — fixed in `785ff64` (ref compiler: `ElseKeyword`/`InKeyword` added to do-block stop-set in `Parser.Expressions.cs`) and `9cc73e6` (self-host parser: same stop-set in `ParserCore.codex`/`ParserExpressions.codex`). Multi-line `if X then do { ... } else Y` and `let P = do ... in Y` both parse in the ref compiler now. |
| 5 | `text-to-double-bits` bare metal implementation | On x86-64 bare metal, `text-to-double-bits` falls through to `__text_to_int` (integer parser). Need a proper `__text_to_double` runtime helper that parses decimal text to IEEE 754 bits. Not blocking — the builtin is only called at compile time when the compiler runs as .NET, not at runtime on bare metal. |

## Compiler Correctness — TOP PRIORITY (blocking bare-metal MM4)

| # | Item | Notes |
|---|------|-------|
| T1 | ~~**Bare-metal x86-64: nested record-field access silently reads offset 0**~~ | **RESOLVED (2026-04-13)** — fixed in `f71d8d7` (self-host) and `785ff64` (ref compiler backport). Five coordinated fixes: (1) `deep-resolve` now recurses into `EffectfulTy` so wrapped `TypeVars` resolve; (2) Lowering field access strips `EffectfulTy`/`ForAllTy` wrappers before the `RecordTy`/`ConstructedTy` resolve match; (3) `resolve-constructed-ty` unwraps `ForAllTy` (was only unwrapping `EffectfulTy`); (4) `emit-field-access` emits a diagnostic when type resolution fails instead of silently defaulting to field-idx 0; (5) same diagnostic added to `emit-record-set-builtin`. Text pingpong green at 537,984 bytes fixed point. |
| T2 | **do-blocks returning typed values emit `Func<object>`** — COULD NOT REPRODUCE | Original report (line 2 of this file, and echoed in the MM4 session summary as "when inner do-block produces a value, C# emitter generates Func<object> instead of Func<EmitChapterResult>") drove the `bin-finalize` / `emit-binary` pure-let refactors as workarounds. On 2026-04-12 spent ~20 min trying to reproduce on current master and **could not**: every typed-do pattern (`outer : [Console] Result = do { ...; Result {...} }`, `<-` bind with typed RHS, inner-do-as-last-statement of typed outer) emits correct `Func<TypedResult>` with or without the proposed emitter fix. Byte-identical output. The remaining candidate triggers (`let P = do ... in Y` and `if C then do-typed else do-typed`) hit the **ref-compiler parse bug (item #4 below)** before reaching `emit-do`, so they cannot be tested via the Codex.Bootstrap driver which uses the ref compiler to parse. **Proposed fix on branch `hex/t2-emit-do-effectful-strip`** (commit `59a2e9f`): strip `EffectfulTy` from `ty` before `emit-do`'s `when`-dispatch so the `VoidTy`/`NothingTy`/`ErrorTy` branches match for effect-wrapped forms. Bootstrap fixed-point, 539/5/0 tests, and pingpong all pass — but the fix does not demonstrably change emitted C# for any input I could construct. **Theory**: the original report may have misattributed a symptom — the actual trigger might be a type-inference issue (wrong `ty` on the `IrDo` node) or an earlier lowering bug, not `emit-do` itself. **Unblocker**: fix parse bug #4 (multi-line `if/then do ... else do ...` in the ref compiler) so the test cases that currently can't be parsed can actually run through the self-host emit-do. Without that, we can't distinguish "no bug in emit-do" from "bug in emit-do but I can't trigger it." Don't merge the fix branch until a concrete failing case exists. |

## Compiler Correctness (low priority, non-blocking, continued)

| # | Item | Notes |
|---|------|-------|
| 6 | **C# bootstrap emitter: `[]` empty-list literal loses element type in conditional branches** | When an empty list literal `[]` appears in a branch of an `if/then/else` whose other branch produces `List Integer`, the self-host C# emitter outputs `(List<long>)new List<object>()` — an invalid cast that fails `dotnet build` of `Codex.Bootstrap.csproj`. Workaround: define a named `no-bytes : List Integer = []` constant and reference it by name — the annotated type flows through. Hit on 2026-04-12 while writing `chunked-write-binary`. Fix lives in `Codex.Codex/Emit/CSharpEmitterExpressions.codex` list-literal emission path — should consult the expected type and emit `new List<long>()` when it's known. |
