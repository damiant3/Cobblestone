# Backlog — Outstanding Work

## Active

| # | Item | Design Doc | Notes |
|---|------|-----------|-------|
| 1 | **Second Bootstrap (MM4)** | `docs/Active/Compiler/SECOND-BOOTSTRAP.md` | Port x86-64 backend to Codex. 8 phases. The critical path. |
| 2 | Escape copy bare-metal | `docs/Designs/Memory/CAMP-IIIA-ESCAPE-ANALYSIS.md` | Skip removed, tests passing. Rearchitect deferred till after MM4. |
| 3 | **Self-host parity audit** | `docs/Active/Compiler/SELF-HOST-PARITY-AUDIT.md` | Living gap doc: reference vs self-host, per data structure / diagnostic / runtime behavior / primitive. Top open gap: polymorphism coverage audit. |

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

## Performance — Quadratic hotspots in self-host (remaining)

Re-profiled 2026-04-16 on 589K-char self-host source. Typecheck = 1419ms,
emit = 1136ms (see `docs/Test/PERF-HOTSPOTS-2026-04-16.md`).
Ordered by measured cost.

| # | Location | Pattern | Measured impact |
|---|----------|---------|--------|
| P9 | `Codex.Codex/Types/Unifier.codex:96-108` `add-subst` | Sorted-list substitution table; every call rebuilds the whole list (`List<SubstEntry>` copy + `Insert` at bsearch pos) | **Biggest typecheck hotspot.** 20,394 calls, max N = 20,393, total work ≈ 208M copy ops ≈ **~400ms = 28% of typecheck**. `var_id` is sequentially allocated — fix by switching to dense `var_id`-indexed storage (O(1) insert/lookup). Note: `resolve`-side "path compression" previously listed here is a non-issue — max chain depth is 2, avg 0 hops. |
| P2 | `Codex.Codex/Types/TypeEnv.codex:37-45` `env-bind` | Sorted `List<TypeBinding>` with per-call list-copy + `Insert` (open-coded `list-insert-at`) | 10,493 calls, max N = 1,877, total work ≈ 17M ops ≈ **~35ms = ~2% of typecheck**. Real but small on current workload. Landed speedup `85cfa4d` (sorted bsearch + `list-snoc`) still mostly holds. HAMT (`88e056a`) was tried and **reverted** (`f85d031`) for being slower. |
| P13-tail | `Codex.Codex/Emit/CodexEmitter.codex:579,616-630` `replace-def` + `list-set` | O(N) scan + O(N) rebuild per dominated def | **Codex-to-Codex emitter only** — 0 calls on pingpong bench (C# emit path). Only hit if we rebuild the Codex emitter as part of the toolchain. |
| P14 | `Codex.Codex/Types/TypeChecker.codex:376-381` `lookup-record-field` | Linear scan over record fields per `.field` access | Unprofiled; F is bounded (≤ ~10) so cost is linear-small. Keep the probe. Low priority. |
| EMIT-TBD | `Codex.Codex/Emit/**` (needs profiling) | Unknown — emit phase is 1136ms (40% of total compile) with no hotspot named in the backlog | Needs a profiling pass similar to the one that produced the rows above. Next perf target after P9. |


## Compiler Correctness (low priority, non-blocking, continued)

| # | Item | Notes |
|---|------|-------|
| 6 | **C# bootstrap emitter: `[]` empty-list literal loses element type in conditional branches** | When an empty list literal `[]` appears in a branch of an `if/then/else` whose other branch produces `List Integer`, the self-host C# emitter outputs `(List<long>)new List<object>()` — an invalid cast that fails `dotnet build` of `Codex.Bootstrap.csproj`. Workaround: define a named `no-bytes : List Integer = []` constant and reference it by name — the annotated type flows through. Hit on 2026-04-12 while writing `chunked-write-binary`. Fix lives in `Codex.Codex/Emit/CSharpEmitterExpressions.codex` list-literal emission path — should consult the expected type and emit `new List<long>()` when it's known. |
