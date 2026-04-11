# Backlog — Outstanding Work

## Active

| # | Item | Design Doc | Notes |
|---|------|-----------|-------|
| 1 | **Second Bootstrap (MM4)** | `docs/Active/Compiler/SECOND-BOOTSTRAP.md` | Port x86-64 backend to Codex. 8 phases. The critical path. |
| 2 | Escape copy bare-metal | `docs/Designs/Memory/CAMP-IIIA-ESCAPE-ANALYSIS.md` | Skip removed, tests passing. Rearchitect deferred till after MM4. |

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
| IL emitter enhancements | Low priority, .NET dependency being retired |
| Legacy transpilation backends | Staying in C#, not being ported |

## Compiler Correctness (low priority, non-blocking)

| # | Item | Notes |
|---|------|-------|
| 1 | ~~Support negative number literals~~ | **DONE** — negative literals work end-to-end, all `0 - 1` idioms replaced. |
| 2 | Verify IL backend CCE assumptions | Document decision: .NET Char.Is* vs CCE ranges |
| 3 | NetworkSync test failures | 4 tests need self-contained peer or integration-only marking |
| 4 | Reference compiler: `if ... then do ... else` parse failure | The C# reference compiler rejects `if X then do { multi-line } else Y` — reports `Expected an expression, found ElseKeyword`. The self-hosted compiler parses this correctly (verified via bootstrap). Workaround: extract multi-line `then` bodies into helper functions. Not blocking — workaround exists. |
