# Backlog — Outstanding Work

## Active

| # | Item | Design Doc | Notes |
|---|------|-----------|-------|
| 1 | **Second Bootstrap (MM4)** | `docs/Active/Compiler/SECOND-BOOTSTRAP.md` | Port x86-64 backend to Codex. 8 phases. The critical path. |
| 2 | Escape copy bare-metal | `docs/Designs/Memory/CAMP-IIIA-ESCAPE-ANALYSIS.md` | Skip removed, tests passing. Rearchitect deferred till after MM4. |
| 3 | **Self-host parity audit** | `docs/Active/Compiler/SELF-HOST-PARITY-AUDIT.md` | Living gap doc: reference vs self-host, per data structure / diagnostic / runtime behavior / primitive. Top open gaps: self-host adoption of foreword `Maybe`; polymorphism coverage audit; symmetric `(record-expr).field` chaining. |

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

## Performance — Quadratic hotspots in self-host (remaining)

| # | Location | Pattern | Impact |
|---|----------|---------|--------|
| P2 | `Codex.Codex/Types/TypeEnv.codex:37-45` `env-bind` | Uses `list-insert-at` (O(n) shift per insert) for sorted binding insert | Building N-binding env is O(n²). Partially mitigated by `list-insert-at` Path 1 in-place shift (`0f75f96`) and the HAMT-partial merge (`88e056a` / `1a90eeb`), but each call still O(n) bytes. Affects type-checking at module scope. |
| P3 | `Codex.Codex/Core/Set.codex:14-20` `set-insert` | Same `list-insert-at` pattern as P2 | O(n) per insert; O(n²) to build an n-element set. |
| P7 | `Codex.Codex/Types/TypeChecker.codex:295-312` `build-type-def-map` | `list-insert-at` sorted-insert per type def to keep `tdm` sorted | O(T²) where T = number of type defs. Typical T ≈ 50–100 → modest. Still O(n²) pattern worth flagging. |
| P9 | `Codex.Codex/Types/Unifier.codex:83-89` `resolve` | Walks the substitution chain without path compression | `TypeVar → TypeVar → …` chain of depth D resolves in O(D * log S). With classic union-find path compression this becomes amortized O(α(S)). Lookup-side 2×-bsearch already fused via `9a8a723` (P8); the insert-side `add-subst` still uses `list-insert-at`. |
| P13-tail | `Codex.Codex/Emit/CodexEmitter.codex:579,616-630` `replace-def` + `list-set` | During dominance analysis, each dominated def triggers `replace-def` (O(N) scan) + `list-set` (O(N) rebuild via snoc-loop) | O(N²) total for module with N defs. Only in Codex emitter (not bare-metal), so not a binary-mode hotspot. Head of P13 closed by `list-set-at` builtin (`466a08b`); the scan-side remains. |
| P14 | `Codex.Codex/Types/TypeChecker.codex:376-381` `lookup-record-field` | Linear scan over record fields per `.field` access | Record field count is small (typically ≤ 10), so O(F) per access is modest. But called on every record projection across the program → cumulative. Low priority; keep a linear probe since F is bounded. |


## Compiler Correctness (low priority, non-blocking, continued)

| # | Item | Notes |
|---|------|-------|
| 6 | **C# bootstrap emitter: `[]` empty-list literal loses element type in conditional branches** | When an empty list literal `[]` appears in a branch of an `if/then/else` whose other branch produces `List Integer`, the self-host C# emitter outputs `(List<long>)new List<object>()` — an invalid cast that fails `dotnet build` of `Codex.Bootstrap.csproj`. Workaround: define a named `no-bytes : List Integer = []` constant and reference it by name — the annotated type flows through. Hit on 2026-04-12 while writing `chunked-write-binary`. Fix lives in `Codex.Codex/Emit/CSharpEmitterExpressions.codex` list-literal emission path — should consult the expected type and emit `new List<long>()` when it's known. |
