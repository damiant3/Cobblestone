# Current Plan

**Updated**: 2026-04-15

---

## MM3 IS PROVEN

The self-hosted Codex compiler compiled **itself** on bare metal x86-64 under
QEMU. Pingpong is green — Codex in, Codex out, fixed point holds. Text-mode
pingpong is byte-identical at ~545 KB, ~35 s per stage, sem-equiv PASS.

## MM4: THE CORD IS NEARLY CUT

Binary pingpong is running end-to-end: the self-host kernel runs on bare
metal, accepts source over serial, compiles, and streams a ~4 MB ELF back.
What's left is isolated byte-level corruption in the streamed output; once
that class of bug is cleared, MM4 fixed-point is cuttable. See `BACKLOG.md`
for the live list of remaining CDX-C items.

## Recently Landed

- **`do` → `act ... end` (2026-04-15)**: `do` is out of the language.
  Statement sequencing uses explicit `act ... end`. `act`/`end`/`qed`
  are contextual keywords (only keywordish in record-scoped positions).
  Phases A + B merged, tests migrated. Rationale archived in
  `docs/Done/Compiler/DO-TO-ACT.md`.
- **Multi-line function application in parens (2026-04-15)**: paren-depth
  tracking in both parsers; `make-error (…) (span-at …)` no longer has
  to be squeezed onto one line. Self-host + reference both pass.
- **Self-host adopts `Maybe`**: parser sentinel-pair sums replaced with
  `Maybe` records; C# emitter carries type args through ctor sites,
  record fields, and patterns; bootstrap loads cited foreword chapters.
- **Multi-arg lambda C# emission**: `\a b c -> body` now emits as the
  curried nested form `(a) => (b) => (c) => body`, matching the curried
  delegate type. Self-host and reference both fixed.
- **Contextual keyword parse-record recovery**: `Synchronize` now advances
  past `end`/`qed` tokens; 3 infinite-loop bugs collapsed into one review
  commit.
- **Heuristic audit closed**: H-001..H-009 all resolved on master. Doc
  preserved as a stub in `docs/Done/HEURISTIC-AUDIT.md`.
- **Diagnostics infrastructure Phases 1–4 shipped**; Phase 5 (presentation)
  explicitly pushed out of the compiler. Tier 3 (per-def resolve +
  type-check on the streaming binary path) is the remaining gap.
- **Bare-metal heap 2 MB → ~1 GB**.

---

## The Critical Path

One thing at a time. Linear. Each item unblocks the next.

### MM4: The Second Bootstrap (NOW)

**Goal**: A Codex compiler compiled entirely by Codex, producing bare-metal
x86-64 binaries, achieving fixed-point self-compilation. No C# in the chain.

**Design doc**: `docs/Active/Compiler/SECOND-BOOTSTRAP.md`

| Phase | What | Status |
|-------|------|--------|
| 1–5, 7 | Encoder, ELF writer, core codegen, helpers, builtins, boot | Shipped |
| 6 | Escape copy & regions | **Deferred to post-MM4** — attempted twice, needs architectural rethink |
| 8 | Self-compilation fixed point | **In flight** — binary pingpong runs end-to-end; isolated byte-level corruption remaining |

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
