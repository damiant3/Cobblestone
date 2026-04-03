# Current Plan

**Date**: 2026-04-01

---

## MM3 IS PROVEN

The self-hosted Codex compiler compiled **itself** on bare metal x86-64 under
QEMU. Pingpong is green — Codex in, Codex out, fixed point holds at 73 MB HWM
(128 MB budget after Phase 3 memory increase).

## ARM64 & RISC-V — ABANDONED

All work on ARM64 and RISC-V backends is forbidden. Existing code remains
for reference. Agent time is x86-64 only.

---

## The Critical Path

One thing at a time. Linear. Each item unblocks the next.

### MM4: The Second Bootstrap (NOW)

**Goal**: A Codex compiler compiled entirely by Codex, producing bare-metal
x86-64 binaries, achieving fixed-point self-compilation. No C# in the chain.

**Design doc**: `docs/Compiler/SECOND-BOOTSTRAP.md`

| Phase | What | Status |
|-------|------|--------|
| ~~1~~ | ~~x86-64 instruction encoder in Codex~~ | ~~Done~~ |
| ~~2~~ | ~~ELF writer + CDX binary format writer in Codex~~ | ~~Done~~ |
| ~~3~~ | ~~Core codegen (M3.1–M3.9: int, let, if, calls, records, match, lists, TCO, closures)~~ | ~~Done — 1,400 lines, all 9 milestones QEMU-proven~~ |
| ~~4~~ | ~~Runtime helpers (16 of 22: string, list, math)~~ | ~~Done — 1,300 lines, pingpong green at 548KB ELF~~ |
| ~~5~~ | ~~Builtins (30 pure-CCE operations)~~ | ~~Done — 264 lines in X86_64.codex, pingpong green~~ |
| 6 | Escape copy & regions | **Next** — will shrink 113MB HWM; requires rodata fixup infra |
| 7 | Boot + I/O boundary (CCE tables, print-line, read/write, 5 deferred helpers) | Waiting on 5-6 |
| 8 | Self-compilation fixed point | **MM4: cord is cut** |

**Work style**: Single agent (Cam), one phase at a time. Other agents review
completed phases, not concurrent feature work. Minimize coordination overhead.

**Pingpong remains the acceptance test.** Every phase that touches codegen
must pass pingpong before moving on.

### BLOCKING: Bootstrap2 Stage0 != Stage1

Semantic equivalence checker (`codex sem-equiv`) measures progress.
Current: **98% body match** (1123/1144 matched, 18 diffs remain).

Test: `dotnet run --project tools/Codex.Cli -- sem-equiv docs/TestResults/stage0.codex docs/TestResults/stage1.codex`

Progress as of 2026-04-03:

1. ~~**Long ++ chain truncation**~~ — **FIXED**.
2. ~~**Caret character mis-emission**~~ — **FIXED**.
3. ~~**Definition dropping / collision**~~ — **FIXED**. All 1144 stage0
   defs matched in stage1 (0 drops). 42 colliding names correctly scoped.
4. ~~**Precedence paren dropping**~~ — **FIXED**. `wrap-binary-right` uses
   `<=` with associativity check. `a - (b + c)` preserved correctly.
5. **CCE string content bugs** — **18 defs remain**. The bare-metal text
   emitter produces wrong characters inside multi-character string literals.
   `\n` emits as wrong CCE character, `\"` emits as `\I`. Concentrated
   in csharp-emitter and csharp-emitter-expressions modules. Plus 5
   redundant-paren normalizer issues and 2 cosmetic diffs.
6. **Whole-module mangling** — **DESIGNED, STASHED**. Policy: if any name
   in a module collides, mangle all defs. C# reference proven (bootstrap1
   green). Bare-metal `find-colliding-modules` produces wrong results.
   Changes stashed in git (`git stash list`). Separate debug session.

Until item 5 is resolved, bootstrap2 cannot achieve stage0 == stage1.

### After MM4: The OS Stack

Once the compiler is self-sustaining, these items become buildable.
Ordered by dependency, not priority.

| # | Item | Design doc | Depends on |
|---|------|-----------|------------|
| 1 | Crypto primitives (Ed25519, SHA-256) | `docs/Codex.OS/CryptoPrimitives.md` | MM4 (must run on bare metal) + bitwise builtins |
| 2 | CDX binary loader + verification | `docs/Codex.OS/CodexBinary.md` | Crypto (#1) |
| 3 | Identity & authentication | None yet | Crypto (#1) |
| 4 | Trust lattice (runtime) | `docs/Designs/Features/CAPABILITY-REFINEMENT.md` | Identity (#3) |
| 5 | Capability refinement Steps 2-8 | `docs/Designs/Features/CAPABILITY-REFINEMENT.md` | MM4 |
| 6 | Agent protocol (7 message types) | `docs/Codex.OS/RuntimeTrust.txt` | Trust lattice (#4), Crypto (#1) |
| 7 | Trust network layer | `docs/Codex.OS/TrustNetwork.md` | Agent protocol (#6) |
| 8 | Policy contract (prose→capabilities) | `docs/Codex.OS/RuntimeTrust.txt` | Capability refinement (#5) |
| 9 | Forensics layer | `docs/Codex.OS/RuntimeTrust.txt` | Agent protocol (#6) |
| 10 | Verifier | `docs/Milestones/THE-LAST-PEAK.md` (Face 2) — needs design doc | Capability refinement (#5) |
| 11 | Filesystem (facts on disk) | None yet | MM4 |
| 12 | Networking stack (TCP transport) | `docs/Codex.OS/TrustNetwork.md` | MM4 |
| 13 | Shell (prose command interface) | `docs/Milestones/THE-LAST-PEAK.md` (Face 3) — needs design doc | Policy (#8), Verifier (#10) |
| 14 | Clarifier (policy feedback loop) | `docs/ForFun/Clarifier.txt` — promote to Designs/ | Policy (#8) |

### Design Docs Needed (no code, can be written anytime)

| Topic | Why | Blocking |
|-------|-----|----------|
| Identity & authentication | Key generation, biometrics, trust bootstrap, first-boot ceremony | OS stack #3 |
| The Verifier | Decidable subset, fuel limits, soundness argument, minimal trusted core | OS stack #10 |
| The Shell | Prose-as-command, capability integration, tab completion | OS stack #13 |
| Boot sequence / init | What starts first, initial capability distribution, root of trust | OS stack broadly |
| Process IPC | Inter-process communication, typed channels, supervisor model | OS stack broadly |
| Scheduler | RT scheduling for [HardRealtime], quotas, priority, watchdog | OS stack broadly |

---

## Deferred (revisit after MM4)

| Item | Why deferred |
|------|-------------|
| Escape copy rearchitect | Needs coarser grains; revisit when codegen is in Codex |
| Perf automation (--bench-check CI) | Low priority vs cutting the cord |
| Codex.UI substrate | Medium-term — no design doc yet |
| Codex.OS on real hardware (WHPX) | After MM4 and basic OS stack |
| Floppy disk (1.44 MB target) | 64 MB achieved; streaming optimizations deferred |
| Repository federation | Trust lattice + networking needed first |
| Standard library expansion | Set, Queue, StringBuilder, TextSearch — when needed |
| V2 Narration layer (Phases 4-6) | Phases 1-3 done; remaining phases after MM4 |
| Structured concurrency runtime | Design exists; implementation after MM4 |

---

## Existing Design Docs (complete, awaiting implementation)

| Design | Doc | Status |
|--------|-----|--------|
| Second Bootstrap | `docs/Compiler/SECOND-BOOTSTRAP.md` | **Active — Phase 6 (escape copy) next** |
| Phase 3 Core Codegen | `docs/Compiler/PHASE3-CORE-CODEGEN.md` | ~~Complete — all 9 milestones proven~~ |
| CDX binary format | `docs/Codex.OS/CodexBinary.md` | ~~Complete, implemented in Phase 2~~ |
| Crypto primitives | `docs/Codex.OS/CryptoPrimitives.md` | Design complete, implements after MM4 |
| Language bitwise builtins | `docs/Codex.OS/LanguageUpdates.md` | Design complete, implements after MM4 |
| Encoder updates (crypto) | `docs/Codex.OS/EncoderUpdates.md` | Design complete, implements after MM4 |
| Module namespaces | `src/Codex.Semantics/ModuleScoper.cs` | ~~Implemented and merged~~ |
| Trust network | `docs/Codex.OS/TrustNetwork.md` | Complete, implements after MM4 |
| Agent protocol | `docs/Codex.OS/RuntimeTrust.txt` §1 | Complete |
| Policy contract | `docs/Codex.OS/RuntimeTrust.txt` §2 | Complete |
| Forensics layer | `docs/Codex.OS/RuntimeTrust.txt` §3 | Complete |
| Capability refinement | `docs/Designs/Features/CAPABILITY-REFINEMENT.md` | Complete |
| Structured concurrency | `docs/Designs/Features/CAMP-IIIC-STRUCTURED-CONCURRENCY.md` | Complete |
| Stdlib & concurrency | `docs/Designs/Features/STDLIB-AND-CONCURRENCY.md` | Complete |
| V2 Narration layer | `docs/Designs/Features/V2-NARRATION-LAYER.md` | Phases 1-3 done |
| V3 Repository federation | `docs/Designs/Features/V3-REPOSITORY-FEDERATION.md` | Complete |
| Safe mutation | `docs/Designs/Features/SAFE-MUTATION.md` | Principle established |
| Distributed Agent OS | `docs/Codex.OS/DistributedAgentOS.txt` | Vision complete |

---

## No Dates

Every estimate has been wrong by orders of magnitude, in both directions.
We don't put dates on mountains. The critical path is ordered. The next
hold is Phase 4 (runtime helpers). That's all we need to know.
