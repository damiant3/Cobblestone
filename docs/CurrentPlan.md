# Current Plan

**Updated**: 2026-04-14

---

## MM3 IS PROVEN

The self-hosted Codex compiler compiled **itself** on bare metal x86-64 under
QEMU. Pingpong is green — Codex in, Codex out, fixed point holds.
Latest text-mode pingpong (2026-04-14): stage1 === stage2 byte-identical
at **544,780 bytes**, sem-equiv PASS, ~35s per stage.

## MM4: THE CORD IS NEARLY CUT

Binary pingpong progression:
- **Pre-C5**: self-host crashed in `__list_snoc` inside `build-offset-table-loop`
  before even reaching `SIZE:`. Compile pipeline never finished.
- **Post-C5** (`f221359`): self-host kernel runs on bare metal, accepts
  `BINARY\n<source>\x04` over serial, completes compile, prints
  `SIZE:4218032`, and streams 4.2 MB of ELF bytes. The compile pipeline
  **runs to completion end-to-end in bare metal**.
- **Remaining**: first 5 bytes of the streamed binary are corrupted
  (`07 07 0c 2d 0b` instead of `7f 45 4c 46 01`). Bytes 5+ match the
  reference-built ELF exactly. Filed as CDX-C6 in BACKLOG with probe
  suggestions. Once C6 is fixed, binary pingpong's stage1 output should
  boot cleanly and the MM4 fixed-point proof is cuttable.

## Recently Landed (MM4 path)

- **T1 fix (`f71d8d7` + `785ff64` ref backport)**: bare-metal nested
  record-field access. Defensive `EffectfulTy`/`ForAllTy` unwrapping
  across `deep-resolve`, field-access lowering, `resolve-constructed-ty`;
  `emit-field-access`/`emit-record-set-builtin` now diagnose instead of
  silently defaulting to field-idx 0. BACKLOG T1 closed.
- **Parse bug #4 fixed (`785ff64` + `9cc73e6`)**: `ElseKeyword`/`InKeyword`
  added to do-block stop-set in both ref and self-host parsers. Multi-line
  `if X then do { ... } else Y` and `let P = do ... in Y` now parse
  correctly through the ref compiler. BACKLOG #4 closed.
- **Do-block type fix (`96b24db`)**: `lower-do` computes type from the
  last statement instead of trusting `expectedType`.
- **T2 (`8475298`)**: strip `EffectfulTy` in `emit-do` before type
  matching; could not reproduce the original C# emitter symptom but the
  fix is harmless and defensively correct.
- **Diagnostics infrastructure (Phases 1-4) shipped**: CDX registry
  (`7b00b46`), SourceSpan file-id + AST/IR span threading
  (`dacc14c`/`7601cd8`/`d190cb6`/`8cda64d`/`792158c`), `DiagnosticBag`
  threading (`2004592`), staged compilation with error gates
  (`0d1239d`), streaming-binary parse-bag + chapter gating (`1484914`).
  Phase 5 (presentation) explicitly pushed out of the compiler
  (`8c5d693`). See `docs/Active/Compiler/DIAGNOSTICS-AND-STAGING.md`.
- **Bare-metal heap (`a725ac7`)**: 2 MB → ~1 GB.
- **Effect annotations (`2c8b520`)**: parser now parses `[E]` effect
  annotations instead of discarding them.

---

## The Critical Path

One thing at a time. Linear. Each item unblocks the next.

### MM4: The Second Bootstrap (NOW)

**Goal**: A Codex compiler compiled entirely by Codex, producing bare-metal
x86-64 binaries, achieving fixed-point self-compilation. No C# in the chain.

**Design doc**: `docs/Active/Compiler/SECOND-BOOTSTRAP.md`

| Phase | What | Status |
|-------|------|--------|
| 6 | Escape copy & regions | **Deferred to post-MM4** — attempted twice, needs architectural rethink |
| 8 | Self-compilation fixed point | **MM4: cord is cut** |

**Work style**: Single agent (Hex), one phase at a time. Other agents review
completed phases, not concurrent feature work. Minimize coordination overhead.

### After MM4: The OS Stack

Once the compiler is self-sustaining, these items become buildable.
Ordered by dependency, not priority.

| # | Item | Design doc | Depends on |
|---|------|-----------|------------|
| 1 | Crypto primitives (Ed25519, SHA-256) | `docs/Designs/Codex.OS/CryptoPrimitives.md` | MM4 (must run on bare metal) + bitwise builtins |
| 2 | CDX binary loader + verification | `docs/Designs/Codex.OS/CodexBinary.md` | Crypto (#1) |
| 3 | Identity & authentication | None yet | Crypto (#1) |
| 4 | Trust lattice (runtime) | `docs/Designs/Language/CAPABILITY-REFINEMENT.md` | Identity (#3) |
| 5 | Capability refinement Steps 2-8 | `docs/Designs/Language/CAPABILITY-REFINEMENT.md` | MM4 |
| 6 | Agent protocol (7 message types) | `docs/Designs/Codex.OS/TrustAndRuntime.md` | Trust lattice (#4), Crypto (#1) |
| 7 | Trust network layer | `docs/Designs/Codex.OS/TrustAndRuntime.md` | Agent protocol (#6) |
| 8 | Policy contract (prose→capabilities) | `docs/Designs/Codex.OS/TrustAndRuntime.md` | Capability refinement (#5) |
| 9 | Forensics layer | `docs/Designs/Codex.OS/TrustAndRuntime.md` | Agent protocol (#6) |
| 10 | Verifier | `docs/Stories/THE-LAST-PEAK.md` (Face 2) — needs design doc | Capability refinement (#5) |
| 11 | Filesystem (facts on disk) | None yet | MM4 |
| 12 | Networking stack (TCP transport) | `docs/Designs/Codex.OS/TrustAndRuntime.md` | MM4 |
| 13 | Shell (prose command interface) | `docs/Stories/THE-LAST-PEAK.md` (Face 3) — needs design doc | Policy (#8), Verifier (#10) |
| 14 | Clarifier (policy feedback loop) | `docs/Designs/Language/Clarifier.md` | Policy (#8) |

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
| ARM64 backend | x86-64 is the critical path; revisit if hardware demands it |
| RISC-V backend | x86-64 is the critical path; revisit if hardware demands it |
| Escape copy & regions (Phase 6) | Two attempts failed; needs coarser architectural change, not incremental fix |
| Perf automation (--bench-check CI) | Low priority vs cutting the cord |
| Codex.UI substrate | Medium-term — no design doc yet |
| Codex.OS on real hardware (WHPX) | After MM4 and basic OS stack |
| Floppy disk (1.44 MB target) | 64 MB achieved; streaming optimizations deferred |
| Repository federation | Trust lattice + networking needed first |
| Standard library expansion | Set, Queue, StringBuilder, TextSearch — when needed |
| V2 Narration layer (Phases 4-6) | Phases 1-3 done; remaining phases after MM4 |
| Structured concurrency runtime | Design exists; implementation after MM4 |

---

## No Dates

Every estimate has been wrong by orders of magnitude, in both directions.
We don't put dates on mountains. The critical path is ordered. That's all
we need to know.
