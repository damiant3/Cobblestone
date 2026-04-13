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
| P6 | `Codex.Codex/Semantics/NameResolver.codex:70-79` `collect-top-level-names` | `list-insert-at` (O(n) shift) per top-level name to maintain sorted set for dup-check | Called once per top-level def. N≈500 → N² = 250K shifts. Each shift in-place via `list-insert-at` Path 1 is still O(n). Swap to HAMT / `TextSet` with hash-based insert. |
| P7 | `Codex.Codex/Types/TypeChecker.codex:295-312` `build-type-def-map` | `list-insert-at` sorted-insert per type def to keep `tdm` sorted | O(T²) where T = number of type defs. Typical T ≈ 50–100 → modest. Still O(n²) pattern worth flagging. |
| P8 | `Codex.Codex/Types/Unifier.codex:98-100` `add-subst` | `list-insert-at` sorted-insert of substitution entries | Every unification that resolves a var adds a subst. For a large compile, S grows to thousands. O(S²) insertion cost. Also lookup is 2× bsearch via `has-subst` + `subst-lookup` (should fuse). |
| P9 | `Codex.Codex/Types/Unifier.codex:83-89` `resolve` | Walks the substitution chain without path compression | `TypeVar → TypeVar → …` chain of depth D resolves in O(D * log S). With classic union-find path compression this becomes amortized O(α(S)). |
| P10 | `Codex.Codex/Syntax/Lexer.codex:293-302` `process-escapes` | `acc ++ char-to-text c` per character in text literals | Between every concat, `char-to-text` bumps the heap, so `acc` is no longer heap-top → `__str_concat` takes slow path (O(len(acc))). Processing N-char literal is O(N²). Negligible in typical source but unbounded in large text literals. |
| P11 | `Codex.Codex/Semantics/ChapterScoper.codex:32-55,468` `slugify` / `strip-hyphens` | Same per-char `acc ++ char-to-text c` pattern as P10 | Applied to every chapter title and def name. O(L²) per name where L = name length. Small per name, cumulative across the module. |
| P12 | `Codex.Codex/IR/LoweringTypes.codex:7-16` `lookup-type` | **Linear scan** over full type/ctor/def binding list, called per name reference during lowering | `ctx.types` = `ctor-types ++ env.env.bindings` (unsorted concat of two sorted lists). Lowering calls this at Lowering.codex:55, 79, 241, 308, 399 — once per name/ctor/record-ctor in the program. For a 10K-expression program with 1K bindings, that's 10M ops in the lowering pass alone. **Likely a top-3 compile-time cost.** Fix: sort the concat, or feed the HAMT from P1 in once it exists. |
| P13 | `Codex.Codex/Emit/CodexEmitter.codex:579,616-630` `replace-def` + `list-set` | During dominance analysis, each dominated def triggers `replace-def` (O(N) scan) + `list-set` (O(N) rebuild via snoc-loop) | O(N²) total for module with N defs. Only in Codex emitter (not bare-metal), so not a binary-mode hotspot — but same root cause as P1 (no `list-set` builtin). |
| P14 | `Codex.Codex/Types/TypeChecker.codex:376-381` `lookup-record-field` | Linear scan over record fields per `.field` access | Record field count is small (typically ≤ 10), so O(F) per access is modest. But called on every record projection across the program → cumulative. Low priority; keep a linear probe since F is bounded. |
| P15 | `Codex.Codex/Emit/X86_64Helpers.codex:667-682` `emit-list-cons-alloc` | Mirror of P4: self-host `__list_cons` also uses `capacity = newLen` | When P4 is fixed in C#, this must be mirrored byte-identically in the codex emitter. |
| P16 | `Codex.Codex/IR/Lowering.codex:125-129,140-144,162-166,233` `LowerCtx.types` extension | Every `let`, lambda param, and pattern binder prepends via `[{...}] ++ ctx.types` | Pathological for the `++` fix — `a=[x]` has `spare=0`, so Path 1 never applies; Path 2 geometric-alloc copies the full N-entry context every time. In a program with L bindings and global ctx size G ≈ 1000, total cost is O(L × G). Self-host compiler has ~10K lets + params → ~10M word copies just to extend the environment. **Likely P12's twin top-3 cost.** Fix options: (a) represent `types` as a stack of delta-lists (O(1) cons-front), with lookup probing each layer; (b) swap to HAMT from P1 with persistent insert; (c) teach `++` a reverse fast path (`small ++ big` when big is heap-top, in-place prepend by shifting big). Option (a) aligns with P12's cure. |
| P17 | `Codex.Codex/Semantics/ChapterScoper.codex:26-42` `find-collisions-loop` + `appears-in-different-chapter` | Outer loop over N assignments; inner `appears-in-different-chapter` re-scans all N → O(N²) | N ≈ 500 defs → 250K ops. Modest but classic "nested linear scan" smell; one sorted-by-name pass would collapse it to O(N log N). |


## Compiler Correctness (low priority, non-blocking, continued)

| # | Item | Notes |
|---|------|-------|
| 6 | **C# bootstrap emitter: `[]` empty-list literal loses element type in conditional branches** | When an empty list literal `[]` appears in a branch of an `if/then/else` whose other branch produces `List Integer`, the self-host C# emitter outputs `(List<long>)new List<object>()` — an invalid cast that fails `dotnet build` of `Codex.Bootstrap.csproj`. Workaround: define a named `no-bytes : List Integer = []` constant and reference it by name — the annotated type flows through. Hit on 2026-04-12 while writing `chunked-write-binary`. Fix lives in `Codex.Codex/Emit/CSharpEmitterExpressions.codex` list-literal emission path — should consult the expected type and emit `new List<long>()` when it's known. |
