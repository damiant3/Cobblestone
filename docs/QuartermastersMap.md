# Quartermaster's Map

The successor to `docs/FabledTreasureMap.md`, drawn for a different
purpose. The old map recorded treasure found in passing — deferred
wins an agent tripped over while digging for something else. This map
is deliberate: work that is **scoped well enough to delegate**, so
senior agents stay on the high-level, high-risk features. The
quartermaster divides the work; the crew digs.

Every entry is a dig another agent can execute end-to-end: the
coordinates are precise, the acceptance gates are named, and the
standing policies that govern it are linked. If an entry needs a
ruling mid-dig, stop and ask Damian — do not improvise past a policy.

Entry format: what / the win / the dig / crew notes / pointers.

## How to claim a dig

1. Announce it: move the entry to Claimed with your agent name and CL
   before you start, so two agents don't dig the same hole.
2. Follow the gates. Anything touching `codex/` runs the full dance:
   one-pass fixed point, battery, self-verify if a seed ships
   (`build/test-self-verify.ps1` — the verify chain is NOT in the
   battery surface). Plug work rebuilds the plug and runs the
   cross-arch tests. Doc/harness work still gets a review.
3. Standing policies bind every dig — do not re-litigate:
   - Bounds are contracts. Never delete or widen a bound to silence a
     diagnostic; prove the range or assert it with `__narrow`.
   - No regex search-replace. Grep selects candidate lines; every
     edit is made by hand with the surrounding code read first.
   - Every CL states a memory + time-complexity verdict (Rule 8).
   - One dig, one CL chain. No "while I'm here."
   - ASCII only in CL descriptions.
4. When done, record the outcome in the entry (CL numbers, what
   actually happened, surprises). The map is also a record.

---

## Buried

### 1. CDX2051 sweep of the -Apps and -FW surfaces

**Status: PARTIAL (fester, 2026-07-03).** The foreword slice is done --
28 sites across 14 core/game/ai modules cleared via foreword-all-compile
(see Claimed). What remains here is the `-Apps` and `-FW` TEST surfaces.

**What:** CDX2051 (silent narrowing) was promoted from warning to
error at the end of the BoundedSignatures campaign. The default and
BVT batteries are clean, but the `-Apps` and `-FW` test surfaces were
never swept under the promoted seed. Any stray unproven flow into a
bounded position in an app or foreword test now fails that compile.

**The win:** The whole depot compiles under the promotion, not just
the battery surface. Until this is done, `-Apps`/`-FW` are landmined
for whoever runs them next.

**The dig:** Run `build/test.ps1 -Apps -Jobs 8`, then `-FW`. For each
CDX2051 hit: read the site, then either prove the range (bound the
source, use a provable expression) or assert it with `__narrow` at
the store. Never delete the bound. Expect mostly-mechanical fixes;
the CoAP precedent needed exactly one `__narrow` (CL 6759). Note the
batch REPL timeout class (entry 12) — a timeout is not a CDX2051
failure; compile stubborn tests individually via `build/compile.ps1`.

**Crew notes:** Junior-friendly, parallelizable by app/foreword
family. No seed change expected (source-level fixes only).

**Pointers:** `docs/Designs/Compiler/Active/BoundedSignatures.md`
(the promotion, the __narrow idiom), `docs/ExaminersAssay.md`
(battery layout, sidecars).

### 2. Constructor param contract sweeps (per-chapter campaign)

**What:** The adsr-new pilot (CL 6790) established the pattern for
giving constructor parameters real bounds: declare the bound on the
ctor param, drop the `__narrow` off the store, and make every caller
prove. The CAMPAIGN RULE is binding: **convert a constructor only
when its ENTIRE caller set — apps included — proves or is swept in
the same CL.** A 30-site app survey exists (GopRender 14, CvmmTheme
6, ...) showing which chapters receive computed values and therefore
need their app sweep bundled.

**The win:** Constructor boundaries stop being cosmetic — the same
stage-A/stage-B enforcement functions get at fields and function
boundaries, extended to construction. Each converted chapter is a
permanent contract.

**The dig:** Per chapter: read the ctor, enumerate the full caller
set (`Grep` for the ctor name, apps included), classify each call
site (literal/provable/computed), bound the params, sweep the
computed sites in the same CL, gate. One chapter per CL. Chapters
whose callers all pass literals are quick wins; compositor-new /
widget-custom-class need their app sweeps bundled.

**Crew notes:** Mechanical once the rule is internalized, but the
caller-set enumeration must be exhaustive — a missed caller is a
runtime trap (stage-B guards are callee-side). Parallelizable by
chapter with claimed-chapter coordination. Compiler-source chapters
change the seed: full gates + self-verify.

**Pointers:** `docs/Designs/Compiler/Active/BoundedSignatures.md`
(pilot as-built, survey), `docs/Agents/blu-workplan.md` (follow-up 3).

### 6. Known-defect tests (two standalone digs)

From `docs/ExaminersAssay.md` Known Defects — each independent.
(6a duplicate-`Event` and 6b historian-test are RESOLVED — see Claimed.)

- **6c. db-full-test — CDX1000 in ~9304-line concat.** Token
  mismatch in Server.codex only when concatenated. Suspect
  concat-boundary or scale-dependent lexer state. Medium
  investigation; a minimized repro is the deliverable even if the
  fix is deferred.
- **6d. db-test — heap-scan overflows 2 GB at runtime.** Compiles
  clean, blows the heap. This is a Rule-8 dig: survey what the scan
  retains, apply heap-save/restore or deck discipline. Medium;
  requires allocator literacy (`docs/ArchitectsSketchbook.md`).

**Crew notes:** 6c/6d need investigation patience. Both have zero
seed risk until a compiler fix is identified (then full gates).

### 7. RealBitcast parity on ARM64 and RISC-V

**What:** IEEE-754 bit-pattern intrinsics for Real landed on x86-64
(acceptance 5/5 f64, 5/5 f32). The ARM64 and RISC-V plugs do not
emit them yet.

**The win:** Real serialization (Wav/Flac/GGUF/network float
encoding) works cross-arch; the cross battery can un-skip anything
float-serialization-shaped.

**The dig:** Mirror the x86-64 emission in each plug (FMOV
general<->vector on ARM64; fmv.x.d / fmv.d.x on RISC-V), port the
acceptance test to the cross harness, run the cross battery. Plug
rebuild only — no seed.

**Crew notes:** Medium; ideal for an agent already fluent in the
plug codegen files (the val/reek pattern).

**Pointers:** `docs/Designs/Compiler/Active/RealBitcast.md`,
`codex/plugs/arm64/`, `codex/plugs/riscv/`.

### 8. ARM64 register allocator — local recycling

**What:** The ARM64 plug has the same monotonic `next-local`
exhaustion the RISC-V plug had; the design doc exists
(`Arm64RegisterAllocator.md`) and the RISC-V twin shipped as the
template (peak-local tracking + recycling at the binary/user-call
pressure sites, CL 6401, main 6409).

**The win:** Same as RISC-V: call-valued-argument staging stops
spilling past the callee-saved set — a real app-code win even
though the 8 micro-benches won't move (they didn't on RISC-V;
that's expected, not failure).

**The dig:** Port the RISC-V recycling shape: `peak-local` field,
recycle `next-local` at the two wrapper sites, peak drives the save
count, restore peak at nested-body sites (lambda, handler clause).
Prove with a regstress-style bench; run the ARM64 cross battery.

**Crew notes:** Medium; the template CL makes this a translation,
not a design. Read the RISC-V CL diff first.

**Pointers:** `docs/Designs/Compiler/Active/Arm64RegisterAllocator.md`,
RISC-V template in `codex/plugs/riscv/RiscVCodeGen.codex` (CL 6401),
`bench/codex/regstress.codex`.

### 9. SMP atomics + boot on ARM64 / RISC-V

**What:** The full SMP stack (atomics, per-core bootstrap, scheduler,
per-core heap, IPI, lock-free channels) is complete on x86-64. The
six atomic builtins and the per-core boot path are not ported to the
ARM64/RISC-V backends.

**The win:** Multi-core Codex on every architecture we emit; unblocks
cross-arch parity for the scheduler/channel test family.

**The dig:** Two phases per arch: (1) atomic builtins — LDAXR/STLXR
or LSE on ARM64, LR/SC + AMO on RISC-V, mapped from the same six
builtin facts; (2) AP boot — PSCI or spin-table on ARM64, HART start
via SBI/CLINT on RISC-V, mirroring the x86 INIT/SIPI + stack-table
shape. Gate on Renode/QEMU SMP boots plus the channel tests.

**Crew notes:** Large — the biggest dig on the map; split by arch,
or atomics-first (useful alone) then boot. Needs plug + OS-layer
fluency. BACKLOG-tracked.

**Pointers:** `docs/PM/BACKLOG.md` (SMP), x86 reference in
`codex/os/sched/` + `docs/ArchitectsSketchbook.md` (SMP Memory
Model).

### 10. DynamicSurvey Phase 1 — auto-retry on deck overflow

**Status: Phase 1 SHIPPED (fester, 2026-07-03 — see Claimed).** The
harness now catches CDX9002 and retries with raised `-Survey`
multipliers. What remains here is Phase 2 (sidecar measure-and-feed-back)
and Phase 3 (compiler-internal proportional survey).

**What:** Survey multipliers are static; unusually dense source
overflows a phase deck (CDX9002, now a clean error halt). Phase 1
of `DynamicSurvey.md` — catch CDX9002 in the harness and retry with
raised multipliers via the existing `-Survey` override — is designed
and unbuilt. Related annoyance: `MEASURE`/measure-survey overflows
in TEXT mode on the full self-source, which blocked measuring the
ctor-packing win (old map #1).

**The win:** Type-dense source stops needing hand-tuned multipliers;
measure-survey works on the selfhost again, restoring the ability to
quantify heap wins.

**The dig:** Harness-side: detect CDX9002 in the compile log, retry
with a raised `-Survey` field for the failing phase (the diagnostic
names it), cap retries, report the multiplier that worked. Then
apply the same override path to the measure-survey TEXT run.

**Crew notes:** Small-medium; PowerShell + BuildSettings literacy,
no seed change for Phase 1.

**Pointers:** `docs/Designs/Build/Active/DynamicSurvey.md`,
`build/compile.ps1` `-Survey` flag, CDX9002 in
`docs/ArchitectsSketchbook.md` (CHECK Deck Overflow).

### 11. GPU Globe bundle — PTX ABI fix and scene completion

**What:** The Codex -> PTX -> CUDA pipeline renders a textured earth
(proven 2026-06-27). The single blocker for UV-mapped earth is the
PTX function-call ABI: `%lv_` registers corrupt across `.func`
calls. Behind it, three scoped follow-ups: dead-code elimination of
`kernel-` prefixed defs in IR mode, stub emission for recognized
intrinsics, and the black-hole scene entry shim (same treatment
gpu_earth got). App files are shelved in CL 6166 on the blu stream.

**The win:** The GPU compute story gets its demo app, and the PTX
plug's function-call ABI becomes trustworthy for every future
kernel, not just this one.

**The dig:** Reproduce the `%lv_` corruption with a minimal two-
function kernel; read the PTX plug's call emission (caller-save vs
callee-save of `%lv_` locals across `call.uni`); fix the save/
restore or renumber into caller-owned registers; re-run the globe.
Then the three follow-ups in order.

**Crew notes:** Medium-high; natural fit for the agent already in
the GPU/PTX files (reek's FontExplorer stream uses the same plug).
Coordinate before unshelving 6166 — it is blu's shelf.

**Pointers:** `codex/plugs/` PTX emitter,
`docs/Designs/Backends/Active/DualTargetGpuCompilation.md`, shelved
CL 6166, `apps/globe/run.ps1`.

### 12. Batch REPL scalability (-Apps timeouts)

**What:** ~118 large-dependency-chain tests time out in `-Apps`
batch mode. They compile fine in individual VMs — the REPL batch VM
accumulates foreword bytes across a session and late-batch tests
exhaust the budget. A harness scalability defect, not a code defect.

**The win:** `-Apps` becomes a true full-surface gate instead of
"green modulo 118 timeouts", which currently forces entry-1-style
sweeps to fall back to one-at-a-time compiles.

**The dig:** Instrument the batch REPL path (bytes sent per slot,
heap after N compiles), then pick the cheapest fix: recycle the VM
every K compiles, presort tests by dependency-chain size so heavy
tests get fresh VMs, or raise slots for heavy bins. Measure before
choosing (Rule: read/measure first).

**Crew notes:** Medium; harness + VM protocol literacy
(`docs/OperatorsManual.md`, Self-Host Compilation Protocol).

**Pointers:** `build/test.ps1` batch phase,
`docs/ExaminersAssay.md` (Batch Compile Architecture).

### 13. Spark WebGPU Studio — wat2wasm blocker

**What:** CurrentPlan gap 8: Spark WebGPU Studio is blocked on
`wat2wasm` reporting an undefined function `$AbsorbedDose`. A unit-
family extractor name leaking into the WASM emission path as an
unresolved import is the likely shape, but nobody has looked.

**The win:** Unblocks the Spark WebGPU demo; likely fixes a WASM-
plug symbol-resolution class affecting any app using unit families.

**The dig:** Reproduce the wat2wasm failure, find who references
`$AbsorbedDose` (a `Duration`/unit-family extractor from
`Units.codex`), determine why the definition was dropped (DCE? not
in the emitted module? cite gap?), fix at the WASM emission layer.

**Crew notes:** Small-medium investigation, medium fix. App/plug
layer only.

**Pointers:** `docs/PM/CurrentPlan.md` (gap 8), WASM plug in
`codex/plugs/`, `codex/foreword/core/Units.codex`.

### 14. PatchEntry.value truthful bound

**What:** De-widening review nit: `PatchEntry.value` could carry its
truthful `0..4294967295` bound (a patch IS a 32-bit little-endian
write). Deliberately left as fester's call.

**The win:** One more honest contract; consistency with the ruling
that bounds are documentation + store checks.

**The dig:** Tiny — bound the field, prove/`__narrow` the writers,
gates. Belongs to fester by prior claim; anyone else coordinate
first.

**Pointers:** old map entry #2 restoration notes
(`docs/FabledTreasureMap.md`), `docs/Agents/blu-workplan.md`
(EffectRows deferred odds).

---

## Conditional digs (armed, not urgent)

These have explicit trigger conditions from prior verdicts — do not
dig early:

- **Scoped-effect TEXT printing + round-trip probe harness** (old
  map #4): dig when the first scoped effect `[Name "scope"]` appears
  in the depot.
- **Const-box width packing + FunTy third word** (old map #10): dig
  when `-EscapeCheck` is revived as a supported path.
- **EffectRows deferred odds** (argument-boundary dotted widening,
  handler dotted discharge, CDX2093 codegen assertion, unsolved-tail
  defaulting): dig when a real program needs them —
  `docs/Designs/Compiler/Active/EffectRows.md` owns the coordinates.
- **Unaligned variant heap objects** (old map #1 follow-up):
  dropping the round-up-to-8 to realize the TypeVar 16->12 win needs
  a cross-arch alignment-safety verdict first. Riskier than it
  looks; treat as design-first.

## Held back (not for the crew)

Deliberately NOT on this map — high-risk, fixed-point-critical, or
awaiting a Damian ruling. Listed so nobody claims them by accident:

- **Emit-side range propagation** (`ir-expr-proven-range` trusting
  bounded params to elide downstream checks) — fixed-point-critical
  emitter surgery; BoundedSignatures design section 7.
- **C2 register family bounding** (reg/slot/ptr-loc, the hottest
  emitter functions; inliner-exclusion perf interaction) — flagged
  HIGHER RISK in the workplan.
- **Full effect-row subtyping** (effect variables in unification,
  polarity threading) — BACKLOG compiler item; touches the unifier's
  core.
- **Vision-check adversarial probe DESIGN** (the BACKLOG's "fulfill
  the vision check") — designing probes that try to break the
  by-construction claims is senior work; executing a designed probe
  catalog is delegatable and will be mapped when the designs exist.
  Status: the LINEAR leg's stage 0 is done (blu CL 6819, nine open
  routes, `docs/Designs/Compiler/Active/LinearOwnership.md`);
  capabilities and punctual legs remain. The LinearOwnership FIX
  campaign stays held back until its design is ruled.
- **CDX2051-class promotion decisions** and any change to what the
  bounds-contract policy means — rulings, not digs.

---

## Claimed

- **4. Encode chapters missing from the bundle list** -- fester, RESOLVED
  2026-07-03. Stale-noise warning; gated on `$seedSeen.Count > 0` in
  `build/compile.ps1`. See the entry for the as-built.
- **5. Cross-arch batch harness -Parallel aggregation bug** -- fester, RESOLVED
  2026-07-03. Root cause: in the `-UseQemu` run branch, `$proc.WaitForExit($timeoutMs)`
  (which returns a bool) was not captured, so the bool leaked into the parallel
  pipeline output; `$runResults` then interleaved stray `$true`/`$false` with the
  result hashtables, and the merge loop's `$compiled[$rr.Name]` threw
  "property 'Name' cannot be found" on a Boolean under StrictMode Latest. The
  compile phase (line 72) captures the same call correctly; the parallel branch
  did not. Fix: `[void]$proc.WaitForExit($timeoutMs)`. Verified: reproduced the
  exact StrictMode throw on a stray Boolean; post-fix the `-UseQemu` batch
  completes phase 2 and tallies per-test results instead of crashing in the merge
  loop. Renode branch never had the leak (uses `& $renodeExe ... | Out-Null`, no
  WaitForExit) and is unchanged. Note: `-UseQemu` mode emits no UART in this
  environment (empty logs even at 3s) -- a separate pre-existing QEMU-runtime gap,
  not aggregation; the validated path remains Renode. Memory + time: O(1) guard,
  no allocation. Also surfaced (separate CL): both cross-arch plugs failed to
  build under the CDX2051 promotion (`make-position`/`make-span` in PlugTypes +
  register-index binds in the RISC-V/ARM64 codegens); fixed with the blessed
  `__narrow`-at-store idiom so the cross battery can run at all.
- **Bezier CDX2001 (foreword-all-compile blocker)** -- fester, RESOLVED
  2026-07-03. `math/Bezier.codex` is designed integer fixed-point (scale 1000)
  but `cites Math chapter Quaternion` and used its `Real` `Vec3`, so every
  `a * p0.vx` was Integer*Real (CDX2001). Its live per-chapter test `math-bezier`
  had been silently failing (a -FW test, rarely run). Fix: dropped the Quaternion
  cite and gave Bezier its own integer `BezVec` record (vx/vy/vz : Integer);
  math unchanged, so `math-bezier.expected` still matches. `math-bezier` now
  passes. No consumer breakage (EdgeRouter/DiagramRenderer cite Bezier but never
  call its functions or name Vec3). foreword-all-compile advances to the NEXT
  Real/Integer chain (Matrix4 mat4-look-at, ~concat 19310). No seed change.
- **1 (foreword slice). CDX2051 sweep of the foreword surface** -- fester,
  PARTIAL 2026-07-03. Using foreword-all-compile as the driver, cleared ALL 28
  CDX2051 silent-narrowing sites across 14 core/game/ai modules: Deque, RingBuffer,
  LruCache, BloomFilter (core), Reservoir, SparseLattice, NeuralNet (ai), Huffman
  (compress), AStar, CellularAutomata, FloodFill, TileMap, Octree, Quadtree (game).
  All were unbounded params or math-mod/ring-wrap results stored into bounded
  grid/capacity/depth fields; fixed with the blessed `__narrow`-at-store idiom
  (values are in range by construction -- never trap). None are compiler-cited,
  so NO seed change; the `__narrow` is transparent (foreword-all-compile now
  type-checks past all 14). foreword-all-compile still can't un-skip: it now hits
  ~21 CDX2001 Integer-vs-Real mismatches in one game foreword (concat ~18729) --
  a separate defect (next dig). The -Apps/-FW test-surface CDX2051 sweep (the rest
  of dig 1) remains. Mem/time: O(1) per narrow, no allocation.
- **6a. foreword-all-compile duplicate Event** -- fester, RESOLVED 2026-07-03.
  Two chapter-scope `Event` record types collided when all forewords compile
  together: `ui/Event.codex` (the widely-used UI event) and `core/EventBus.codex`.
  Renamed the EventBus one to `BusEvent` (self-contained: its external consumers
  call only `event-*` functions and never name the `Event` type, so no caller
  sweep). CDX3001 is gone; `eventbus-test` still passes (rename is transparent).
  foreword-all-compile is NOT un-skipped -- compiling all forewords together now
  surfaces many pre-existing CDX2051 landmines (activation, rs-capacity,
  num-bits, dq-cap, ...) that belong to the depot-wide CDX2051 sweep (dig 1);
  its `.skip` reason was updated to say so. No seed change (EventBus is not in
  the compiler cite graph). Mem/time: O(1) rename, no allocation.
- **6b. historian-test parse error** -- fester, RESOLVED 2026-07-03. The old
  CDX1000 `is`-token parse error was already gone (fixed upstream). The live
  blocker was 6 CDX2051 silent-narrowing errors in historian-test's dependency
  chain: `TrustLattice.codex` (`TrustEntry.direct-score` / `Vouch.score`, both
  0..65535, fed the plain-Integer `score` param at 3 sites) and
  `RepoProtocol.codex` (`VerdictKindDecodeResult.next-offset` 0..4294967295, fed
  the unbounded `off + 1` at 3 sites in `decode-verdict-kind`). Fixed with the
  blessed `__narrow`-at-store idiom (trust scores are 0-10000, offsets are small
  buffer positions -- never trap). historian-test now compiles, runs, and is
  un-skipped with a fresh `.expected` (`initial=abc123 steps=0 term=abc123`).
  repo-protocol-test still passes. NOTE: `trust-lattice-test` has a PRE-EXISTING
  stale `.expected` (line 3 `7200` vs actual `5760`, a decay-computation value)
  that already failed on main -- unrelated to the `__narrow` (identity for
  in-range values); left for a separate -Apps `.expected` refresh (dig 1).
  No seed/plug change (os/trust + apps/works are outside the compiler cite graph).
  Mem/time: O(1), 6 narrow-at-store guards, no allocation.
- **10. DynamicSurvey Phase 1 -- auto-retry on deck overflow** -- fester, RESOLVED
  2026-07-03 (Phase 1 only). `compile.ps1` now watches the failure output for the
  compiler's `Deck overflow in <PHASE>` diagnostic (CDX9002), maps the phase to
  its survey field (LEX->lex-mul, PARSE->parse-mul, DESUGAR->desugar-mul,
  SCOPE->scope-mul, CHECK->check-mul, LOWER->lower-mul, RESOLVE->resolve-mul,
  LIFT->lift-mul; the *-KEEP phases -> headroom), doubles that field from its
  BuildSettings default (max with any existing override), rewrites only the
  mode-line survey= suffix, and retries -- up to 5 escalations. The normal path
  is byte-identical (body built once, survey applied per attempt). Verified:
  normal compiles unchanged; the phase-parse + escalation logic checks out for
  every phase (CHECK 400->800->1600, LEX 40->80->160, LOWER 42200->84400, etc.).
  SCOPE NOTE: Phase 1 handles the CLEAN CDX9002 case (moderate overflow, phase
  completes and the post-compact check reports). SEVERE under-reservation still
  hard-faults with no output BEFORE emitting CDX9002 (the OperatorsManual's
  "lowering too far faults rather than bailing cleanly") -- that is out of Phase 1
  scope and unchanged; a clean-bail on fault belongs to a later phase. Because the
  default budgets are generous (and unit-heavy for CHECK), an artificial `-Survey`
  starvation could not manufacture a clean CDX9002 (it jumps straight from fine to
  fault); the real trigger is genuinely type-dense/oversized source at defaults.
  Memory + time: O(1) per retry decision; adds at most 5 bounded re-compiles only
  when a deck actually overflows. Phases 2 (sidecar measure-and-feed-back) and 3
  (compiler-internal proportional) remain open. No seed/plug change; PowerShell only.
- **3. CDX2053 "proven" info verbosity** -- fester, RESOLVED 2026-07-04. Measured
  a self-compile: 367 CDX2053 NarrowingProven infos, 354 of them single-point
  (`proven within N..N` -- a literal or named constant that folds to one value,
  the trivial "constant fits" case) and only 13 a real interval (`lo..hi` from
  arithmetic/flow/builtin-return, the prover doing actual work). Fix in
  `TypeCheckerInference.codex` `lint-narrowing-prove`: gate the CDX2053 info on
  `p-lo < p-hi` -- single-point proofs elide SILENTLY, real-range proofs still
  report. The bounds-check elision itself is unchanged in both cases; only the
  log line is gated. Self-compile CDX2053 count 367 -> 14. New `.diag` test
  `cdx2053-range` pins the survivor (`list-length xs` proves 0..4294967295 into
  a bounded field) so over-suppression regresses loudly; the 6 existing
  BoundedSignatures category tests (arith/builtin/const/local/return/bounded-sig)
  still pass -- each retains at least one non-trivial CDX2053 (e.g.
  const-narrow-proven's `if flag then 41 else 202` proves the 41..202 union).
  Diagnostic-only change, so ONE-PASS hard fixed point (SUT === stage1); seed
  rebuilt + self-verifies; full default battery 294/0/15. Memory + time: O(1)
  added comparison, no allocation.
