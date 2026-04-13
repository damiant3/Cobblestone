# Backlog — Outstanding Work

## Active

| # | Item | Design Doc | Notes |
|---|------|-----------|-------|
| 1 | **Second Bootstrap (MM4)** | `docs/Active/Compiler/SECOND-BOOTSTRAP.md` | Port x86-64 backend to Codex. 8 phases. The critical path. |
| 2 | Escape copy bare-metal | `docs/Designs/Memory/CAMP-IIIA-ESCAPE-ANALYSIS.md` | Skip removed, tests passing. Rearchitect deferred till after MM4. |
| 3 | **Self-host parity audit** | `docs/Active/Compiler/SELF-HOST-PARITY-AUDIT.md` | Systematic gap analysis: reference vs self-host, per data structure / diagnostic / runtime behavior / primitive. Recent fragility (silent binary crashes, missing Maybe/Set/xor, no parser diagnostics, no GPF handler) traces back to uncatalogued gaps. Priority: diagnostics and data structures first. |

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
| 2 | Verify IL backend CCE assumptions | Document decision: .NET Char.Is* vs CCE ranges |
| 3 | NetworkSync test failures | 4 tests need self-contained peer or integration-only marking |
| 5 | `text-to-double-bits` bare metal implementation | On x86-64 bare metal, `text-to-double-bits` falls through to `__text_to_int` (integer parser). Need a proper `__text_to_double` runtime helper that parses decimal text to IEEE 754 bits. Not blocking — the builtin is only called at compile time when the compiler runs as .NET, not at runtime on bare metal. |

## Compiler Correctness — TOP PRIORITY (blocking bare-metal MM4)

| # | Item | Notes |
|---|------|-------|

## Performance — Quadratic hotspots in self-host (slowing binary mode)

Hunt started 2026-04-13 after `++` runtime helper fix (commit `582562e`). Self-host still superlinear — `__list_append` was fixed but per-call-site patterns remain.

| # | Location | Pattern | Impact |
|---|----------|---------|--------|
| P1 | `Codex.Codex/Core/Hamt.codex:78-96` `hamt-text-replace-at` / `hamt-int-replace-at` | Rebuilds the ENTIRE 8192-slot list (keys + values) by snoc-loop on every insert just to replace one element | Every `hamt-set` = 16384 copies + 16384 heap words wasted. Called by `build-offset-map` F times → O(F * 16K). For F=500 funcs: 8M ops + ~128MB heap churn. **Fix:** add `list-set` builtin that does O(1) in-place `MovStore`, replace the loop. Hex-Hex is investigating a HAMT/full-source issue — coordinate before changing. |
| P2 | `Codex.Codex/Types/TypeEnv.codex:37-45` `env-bind` | Uses `list-insert-at` (O(n) shift per insert) for sorted binding insert | Building N-binding env is O(n²). Partially mitigated by `list-insert-at` Path 1 in-place shift, but each call still O(n) bytes. Affects type-checking at module scope. |
| P3 | `Codex.Codex/Core/Set.codex:14-20` `set-insert` | Same `list-insert-at` pattern as P2 | O(n) per insert; O(n²) to build an n-element set. |
| P4 | `src/Codex.Emit.X86_64/X86_64CodeGen.cs:5066` `EmitListConsHelper` | Allocates with `capacity = newLen` (no geometric growth) and copies oldLen elements | O(n) per `x :: xs`. Self-host only uses `::` in 2 places, so low impact today — but any future cons-accumulation would be quadratic. Mirror of the `++` fix (geometric cap) is straightforward. |
| P5 | `Codex.Codex/Emit/X86_64.codex:366-372` `collect-call-patches` + `lookup-func-offset` | Linear scan over `func-offsets` (O(F)) for every call patch (P) → O(P*F) at link time | Final pass only; defined `hamt-lookup-offset` already exists but unused. Swap to HAMT lookup after P1 is fixed. |


## Compiler Correctness (low priority, non-blocking, continued)

| # | Item | Notes |
|---|------|-------|
| 6 | **C# bootstrap emitter: `[]` empty-list literal loses element type in conditional branches** | When an empty list literal `[]` appears in a branch of an `if/then/else` whose other branch produces `List Integer`, the self-host C# emitter outputs `(List<long>)new List<object>()` — an invalid cast that fails `dotnet build` of `Codex.Bootstrap.csproj`. Workaround: define a named `no-bytes : List Integer = []` constant and reference it by name — the annotated type flows through. Hit on 2026-04-12 while writing `chunked-write-binary`. Fix lives in `Codex.Codex/Emit/CSharpEmitterExpressions.codex` list-literal emission path — should consult the expected type and emit `new List<long>()` when it's known. |
