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
| 4 | Reference compiler: `if ... then do ... else` parse failure | The C# reference compiler rejects `if X then do { multi-line } else Y` — reports `Expected an expression, found ElseKeyword`. The self-hosted compiler parses this correctly (verified via bootstrap). Workaround: extract multi-line `then` bodies into helper functions. Not blocking — workaround exists. |
| 5 | `text-to-double-bits` bare metal implementation | On x86-64 bare metal, `text-to-double-bits` falls through to `__text_to_int` (integer parser). Need a proper `__text_to_double` runtime helper that parses decimal text to IEEE 754 bits. Not blocking — the builtin is only called at compile time when the compiler runs as .NET, not at runtime on bare metal. |

## Compiler Correctness — TOP PRIORITY (blocking bare-metal MM4)

| # | Item | Notes |
|---|------|-------|
| T1 | **Bare-metal x86-64: nested record-field access silently reads offset 0** | Pattern `(record-expr.field1).field2` — a field access whose receiver is itself a field access, whether written inline with parens, as the chained form `record.f1.f2`, or split via an intermediate `let s = rec.f1 in s.f2` — mis-compiles on the bare-metal x86-64 backend. The *outer* `field-idx` is computed as `0` instead of the real offset, so the generated `mov` reads a garbage qword. Manifests as a silent page fault (no "OUT OF MEMORY" printed, execution just stops; QEMU serial output truncates mid-line). **Hypothesis**: in `emit-field-access` (`Codex.Codex/Emit/X86_64.codex:1950`), `resolve-constructed-ty` returns something other than `RecordTy` for the inner receiver's type — probably the IR type is `ConstructedTy "CodegenState"` (or similar) and `lookup-type-binding` on `st.type-defs` misses, returning `ErrorTy`, so the `when rec-ty` hits `if _ -> 0`. Needs instrumentation in the emitter to confirm which branch the inner case takes. **Known sites in `.codex` source** (each must be re-audited once the emitter is fixed): `Emit/X86_64.codex:2815` `layout-text-buf = (e.st).text-buf-addr`, `:2818` `layout-rodata-buf = (e.st).rodata-buf-addr`, `:2839` `layout-bag = (e.st).bag`, `:2864` `bag = (e.st).bag` inside `fin-extract-bytes`, `Emit/X86_64Helpers.codex:1286` `((cmp.cg).text-len)`. **To find more**: `rg -n "\(\w+\.[\w-]+\)\.\w" Codex.Codex` for the paren-inline form, plus `rg -n "^\s*let \w+ = \w+\.[\w-]+\s*$" -A2 Codex.Codex` for the split-via-let form (check whether the next line accesses a field on the let-bound name). Also audit every `fin-*` / `layout-*` helper that takes a record and the places they're called. **Workaround that works**: helpers may only return *scalars* (Integer, Text, Boolean, etc.), never records that callers then field-access. If the caller needs a nested field, define a single helper that returns the final scalar directly (`layout-text-buf (e) = (e.st).text-buf-addr` itself is *still* affected because the bug is inside the helper body — so the helper must be written differently, e.g., by making `.st` return something other than a record, or by inlining the CodegenState fields into ElfLayout). **Repro**: on `hex/finalize-phase-markers`, `bin/emit-binary` → `fin-write-text` → `(e.st).text-buf-addr` as first arg of `buf-read-bytes` — the enclosing function silently no-ops (a `print-line` placed as the first statement of the enclosing helper never fires). Localized via PH-marker subdivision during the chunking work. |
| T2 | **do-blocks returning typed values emit `Func<object>`** (already filed at line 2) | Same root cause family as T1 — both are emitter / type-info-propagation bugs. Top priority alongside T1. |

## Compiler Correctness (low priority, non-blocking, continued)

| # | Item | Notes |
|---|------|-------|
| 6 | **C# bootstrap emitter: `[]` empty-list literal loses element type in conditional branches** | When an empty list literal `[]` appears in a branch of an `if/then/else` whose other branch produces `List Integer`, the self-host C# emitter outputs `(List<long>)new List<object>()` — an invalid cast that fails `dotnet build` of `Codex.Bootstrap.csproj`. Workaround: define a named `no-bytes : List Integer = []` constant and reference it by name — the annotated type flows through. Hit on 2026-04-12 while writing `chunked-write-binary`. Fix lives in `Codex.Codex/Emit/CSharpEmitterExpressions.codex` list-literal emission path — should consult the expected type and emit `new List<long>()` when it's known. |
