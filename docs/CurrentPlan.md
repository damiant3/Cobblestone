# Current Plan

**Updated**: 2026-04-25

## REF COMPILER IS LOCKED. MAKE NO CHANGES THERE.

The reference compiler under `src/` is **frozen**. Do not edit it. BS1 and
BS1.1 are **legacy** — they ran the .NET self-host through C# / Codex-text
emit and are kept only for historical reference. New work goes through the
self-host (`Codex.Codex/`), and the gates that must remain green are:

- **BS2 (pingpong)** — bare-metal ELF emits Codex text, stage 1 === stage 2
  byte-identical under the self-built SUT (semantic equivalence under SUT
  follows from byte-identity), sample battery green on the same compiler.
  `wsl bash tools/pingpong-self.sh`. The legacy `tools/pingpong.sh` (REF
  builds the ELF, REF runs sem-equiv) is preserved untouched for
  comparison.
- **BS3** — bare-metal ELF emits ELF (stage-1 self-emits machine code).
  `wsl bash tools/bootstrap3.sh`.

If a change requires an edit to `src/`, stop and raise it — the freeze is
the rule, not a guideline.

## Process: Perforce is primary

As of 2026-04-17, the source of truth for in-flight work is the **local
Perforce server** (port 1666, depot `//Codex/main`). Changelists are reviewed
there — shelved for pre-submit review, then submitted. GitHub remains as a
public mirror but receives only **ad-hoc pushes** when there is a meaningful
milestone to share with the world. Do not create long-lived GitHub feature
branches; route work through Perforce instead.

## The CL 128 Reset (2026-04-20)

Prior pingpong, BS1, and BS3 greens were **ceremonial**. They proved that the
self-host and the reference agreed — not that either was correct. CL 128
exposed multiple silent REF correctness holes (missing `IRLambda` / `IRRunState`
/ `IRGetState` / `IRSetState` emit cases, destructive register mutation in
predicate/char builtins, `SubstituteTypeVarsFromArg` not walking `ConstructedType`
/ `SumType` / `RecordType`, `ExprTypes` not threaded to lowering, name collisions
between user-defined and built-in functions, SSE never enabled on bare-metal
entry). Each of these could produce a green pingpong while the compiled
output was wrong; several of them did. A self-host that mirrored the same
holes would agree with the REF, and the test said "green."

CL 128 replaced the implicit "stages agree" gate with an explicit sample
battery (`tools/sweep.sh` over 72 samples with `.expected` / `.failing` /
`.skip` sidecars). That battery is now the correctness anchor. Byte-identity
between stages remains a self-compilation gate, but agreement alone never
again counts as a milestone.

Live state as of 2026-04-22:

- **REF correctness baseline**: 59 verified + 21 expected-fail diagnostics
  + 5 unverified-compile + 11 skipped + 1 fail (out of 97 samples). Battery
  grew from 72 to 97 since CL 128.
- **Self-host C# target — LIFTING COMPLETE**. `tools/sweep.sh
  --compiler=selfhost-cs` reports 83 pass / 57 verified / 21 expected-fail /
  5 unverified / 11 skip / 3 fail of 97. All 3 fails (`effectful-hello`,
  `shapes`, `w3`) are target-semantic — REF bare-metal vs selfhost-cs differ
  on stdin EOF, Number-to-text formatting, and record ToString — not
  compiler bugs.
- **BS1 / BS1.1**: both green. BS1 = 1,155,461 chars (Stage 1 === Stage 3).
  BS1.1 = 687,653 chars (Stage 1 === Stage 2). Prior sizes stale;
  silent BS1 regression since CL 239 (multi-name `cites` introduced
  in opening.codex:12) went unnoticed because the live-state numbers
  were not re-measured. Fixed 2026-04-23 in CL 286
  (parse-selected-names accepts TypeIdentifier + consumes `)`).
- **Self-host bare-metal target (Row 11) — GREEN**. `tools/sweep.sh
  --compiler=selfhost --jobs=8` (bare-metal via QEMU, 8-way parallel) reports
  82 pass / 61 verified / 21 expected-fail / 8 skip / 0 fail of 90 in ~96s
  wall. Closed 2026-04-22: type-aware REPL print (CL 240), SSE enable in
  bootstrap trampoline (CL 246), state-effect emit + predicate register
  clobber (CL 247), arity-aware over-apply (CL 249), lambda lifting +
  AbsorbOuterLambdas + get-apply-root (CL 252), match-lit-wild
  compare+branch (CL 256). Closed 2026-04-23: sweep grep tolerance for
  CDX-prefixed diagnostic codes (CL 274, flipped 20 "diag-parity"
  artifacts), Foreword preload in bare-metal sample harness (CL 278,
  flipped cite-fn-call / list-test / missing-cite), text-split emit
  aliasing fix (CL 282, flipped text-ops — scan-loop offset was read
  after in-place mutation by scan-head emit, causing jmps to land at
  emit-seg and spin the heap ptr into the stack region). Row 11 closure
  unblocks BS2 (pingpong) work on the `__`-prefix L12 + diag-parity
  drift tracked in `docs/Active/Compiler/REF-LESSONS-FOR-SELFHOST.md`.
- **.net selfhost bare-metal target (Row 9) — GREEN**: `tools/sweep.sh
  --compiler=selfhost-netbin` reports 82 pass / 61 verified / 21 expected-fail /
  8 skip / 0 fail of 90 raw after MM4-Deferred moves (fork-basic,
  prose-banking, handler-basic). Applied denominator 82: all valid tests
  pass; 8 invalid excluded (MathLib, hamt-test, linear-basic,
  polymorphism-coverage, proofs, test-run-process, use-math-lib,
  watchdog-panic-probe). Up from 43/89 pre-audit. Runnable-fail cases
  closed 2026-04-22: abs/int-mod/max/min builtins (CL 257), Foreword
  cite resolution + CDX3010 + user-arity-beats-builtin dispatch +
  tail-pos reset sweep for sum-ctor / record / list / negate / binary /
  field-access / act-stmt arg eval (CL 259), match-lit-wild compare+branch
  (CL 256).
- **REF IL backend (Row 18)**: `tools/sweep.sh --compiler=il` reports
  57 pass / 93 total after parity-lift CLs 273 / 279 / 284. Remaining 28
  gaps: 21 are the pre-existing diag-harness sample set shared with
  `--compiler=ref`; 3 are target-semantic divergence (same 3 that fail
  selfhost-cs); 4 are multi-step partial-application (cascading closures)
  — design in `docs/Active/Backends/IL-PARITY.md`.
- **BS3 (Row 13)**: stage-1 ELF boots — the RIP=0x0B crash cleared with
  CL 252. Remaining bug: stage-1 miscompiles self-host's own typechecker
  on trivial samples (`greeting "World"` → `Text vs Fun`; `square 5` →
  `Integer vs Fun`). This is a codegen-emit bug in self-host's own
  `Codex.Codex/Emit/X86_64*.codex` source: stage-0 (REF-emitted) compiles
  the same samples correctly at row 11, but stage-0's compilation of the
  self-host source produces a stage-1 binary that mis-types fully-applied
  function calls. Size evidence: stage-1 is 1,081,344 bytes / 1,874
  function prologues vs stage-0's 1,411,096 bytes / 1,945 prologues — 71
  functions missing in stage-1.

## MM4: The Second Bootstrap (NOW)

**Goal**: A Codex compiler compiled entirely by Codex, producing bare-metal
x86-64 binaries, achieving fixed-point self-compilation. No C# in the chain.

**Design doc**: `docs/Active/Compiler/SECOND-BOOTSTRAP.md`

**Current milestone-path**:

| Step | What | Status |
|------|------|--------|
| 0 | REF correctness baseline (sample battery green) | ✅ since CL 128 — 59 verified + 21 diag + 5 unverified + 11 skip + 1 fail / 97 |
| 1 | Self-host C# target green against REF | ✅ since CL 209 — selfhost-cs 83/57/21/5/11/3; BS1 1,155,461 green (post-CL-286) |
| 2 | Self-host bare-metal target green | ✅ 2026-04-23 — Row 11 sweep 82/82 (CL 274/278/282). Unblocks BS2 (pingpong). |
| 3 | BS2 (pingpong) re-green | **Next** — Row 11 closed; next is sem-equiv + stage1===stage2 fixed-point. |
| 4 | BS3 re-green; MM4 fixed-point cut | Gates on step 3 |

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

## Deferred (revisit after MM4)

| Item | Why deferred |
|------|-------------|
| ARM64 backend | x86-64 is the critical path; revisit if hardware demands it |
| RISC-V backend | x86-64 is the critical path; revisit if hardware demands it |
| Perf automation (--bench-check CI) | Low priority vs cutting the cord |
| Codex.UI substrate | Medium-term — no design doc yet |
| Codex.OS on real hardware (WHPX) | After MM4 and basic OS stack |
| Floppy disk (1.44 MB target) | 64 MB achieved; streaming optimizations deferred |
| Repository federation | Trust lattice + networking needed first |
| Standard library expansion | Set, Queue, StringBuilder, TextSearch — when needed |
| V2 Narration layer (Phases 4-6) | Phases 1-3 done; remaining phases after MM4 |
| Structured concurrency runtime | Design exists; implementation after MM4 |

## No Dates

Every estimate has been wrong by orders of magnitude, in both directions.
We don't put dates on mountains. The critical path is ordered. That's all
we need to know.

## The Compiler Matrix

Status legend:
- ✅ = numerator == denominator (every applicable test passes).
- 🟡 = 0 < numerator < denominator (some pass, known gaps).
- 🔴 = numerator == 0 (nothing works).
- ❌ = not exercised (no test path wired up).

**denominator − numerator = work remaining.** Denominator is valid tests
only: a sample whose pass/fail is a legitimate assertion about the
compiler. Excluded: stubs, libraries with no `opening`, samples with
broken cites/setup, intentional infinite loops, tests whose `.expected`
matches a different target's semantics. Included: every feature gap
whose test could pass if the compiler were finished.

Tests targeting features explicitly out of MM4 scope live under
`samples/MM4-Deferred/` and are not iterated by the sweep at all (nor
counted in any denominator). Currently: `prose-banking`, `fork-basic`,
`handler-basic` (user-defined effect handlers — effect rows are used
everywhere for capability tagging, but no real code uses a `with E body
op = ...` handler expression; the sample is a grammar-only probe).

Ordinals are a single global range across all sub-matrices. Sweep
numbers are serial `--jobs=1` measurements. Notes below the tables.

### Microsoft C#

| #   | Input    | Backend | Output       | Status         |
|-----|----------|---------|--------------|----------------|
| 1   | src/*.cs | .NET    | REF compiler | ✅ every build |

### REF (C# self-host)

| #   | Input           | Backend     | Output              | Status                |
|-----|-----------------|-------------|---------------------|-----------------------|
| 2   | sample programs | x86-64-bare | sample ELFs         | 🟡 84/93              |
| 3   | selfhost source | C#          | → .net selfhost     | ✅ BS1 1,163,057      |
| 4   | selfhost source | Codex text  | (re-emitted source) | ✅ BS1.1 692,570      |
| 5   | selfhost source | bare-metal  | bare-metal selfhost | ✅ 1,436,480 bytes    |
| 18  | sample programs | IL (.NET)   | sample .dlls        | 🟡 79/93              |

### .net selfhost

| #   | Input           | Backend    | Output                       | Status                  |
|-----|-----------------|------------|------------------------------|-------------------------|
| 6   | sample programs | C#         | .cs files                    | 🟡 82/93                |
| 7   | selfhost source | C#         | (stage 2/3)                  | ✅ BS1 fixed-point      |
| 8   | selfhost source | Codex text | (stage 2)                    | ✅ BS1.1 fixed-point    |
| 9   | sample programs | bare-metal | sample binaries              | ✅ 85/93                |
| 10  | selfhost source | bare-metal | would be bare-metal selfhost | ❌ gap — not exercised  |

### bare-metal selfhost

| #   | Input           | Backend    | Output                   | Status                |
|-----|-----------------|------------|--------------------------|-----------------------|
| 11  | sample programs | bare-metal | sample binaries          | ✅ 85/93              |
| 12  | selfhost source | Codex text | (stage 2)                | ✅ BS2 692,612        |
| 13  | selfhost source | bare-metal | self-sustaining compiler | 🟡 BS3 stage 1        |
| 14  | sample programs | C#         | —                        | ❌ gap                |
| 15  | selfhost source | C#         | —                        | ❌ gap                |

### self-sustaining

| #   | Input           | Backend    | Output                        | Status       |
|-----|-----------------|------------|-------------------------------|--------------|
| 16  | anything        | anything   | —                             | 🔴 no boot   |
| 17  | selfhost source | bare-metal | byte-identical copy of itself | 🔴 MM4 target |

### Notes

- **Sweep totals**: 93 samples = applied + 8 skip. Denominators above
  subtract the 8 skip (pass/total shown is verified-pass / applied).
- **Parallel flakes**: `--jobs=N > 1` is not reliable for bare-metal
  (QEMU-via-WSL races) — use `--jobs=1` for ground truth.
- **Stage-0 freshness**: Row 11 requires `codex build Codex.Codex
  --target x86-64-bare` output to be current against `Codex.Codex/`
  source; a stale ELF silently fails dozens of samples.
- **Sidecar integrity**: `shapes` / `w3` / `effectful-hello` `.expected`
  files encode a REF bare-metal bug (no `NumberType`/sum-type print
  on entry; no deterministic stdin-EOF handling), not ground truth.
  Fix either in REF or by splitting sidecars per backend. Decision
  pending.
- **Row 2 real fails (post sidecar fix)**: `over-apply-partial` emits
  the .text base pointer instead of the computed value.
- **Row 18 real fails (post sidecar fix)**: `list-test` / `poly-runtime`
  (polymorphic partial-app not yet handled), `expr-calculator` (unknown
  parser bug).