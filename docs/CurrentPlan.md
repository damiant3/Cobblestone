# Current Plan

**Date**: 2026-03-31

---

## MM3 IS PROVEN

The self-hosted Codex compiler compiled **itself** on bare metal x86-64 under
QEMU. Pingpong is green — Codex in, Codex out, fixed point holds at 55 MB HWM.

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

| Phase | What | Unblocks |
|-------|------|----------|
| 1 | x86-64 instruction encoder in Codex | Everything below |
| 2 | ELF writer + CDX binary format writer in Codex | Phase 3 |
| 3 | Core codegen (expressions, records, lists, match, calls, TCO) | M3 milestone: `main = 42` boots |
| 4 | Runtime helpers (string, list, math, I/O) | Parallel with 5, 6 |
| 5 | Builtins (50+ operations) | Parallel with 4, 6 |
| 6 | Escape copy & regions | Parallel with 4, 5 |
| 7 | Bare-metal boot sequence | Phase 8 |
| 8 | Self-compilation fixed point | **MM4: cord is cut** |

**Work style**: Single agent (Cam), one phase at a time. Other agents review
completed phases, not concurrent feature work. Minimize coordination overhead.

**Pingpong remains the acceptance test.** Every phase that touches codegen
must pass pingpong before moving on.

### After MM4: The OS Stack

Once the compiler is self-sustaining, these items become buildable.
Ordered by dependency, not priority.

| # | Item | Design doc | Depends on |
|---|------|-----------|------------|
| 1 | Crypto primitives (Ed25519, SHA-256) | None yet — write during or after MM4 | MM4 (must run on bare metal) |
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
| Crypto primitives | Ed25519 + SHA-256 on bare metal, constant-time, in Codex | OS stack #1 |
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
| Second Bootstrap | `docs/Compiler/SECOND-BOOTSTRAP.md` | **Active — Phase 1 tonight** |
| CDX binary format | `docs/Codex.OS/CodexBinary.md` | Complete, implements in Phase 2 |
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
hold is Phase 1. That's all we need to know.
