# Quartermaster's Map

**Archived from `docs/` root to `docs/Reference/` 2026-07-17.** The fleet
delegates through `docs/PM/CurrentPlan.md` and the per-agent workplans, not
this map. It is kept as a record. It still holds open digs (ARM64 register
allocator, Spark WebGPU wat2wasm, GamesDemo idle-hang, GPU Globe bundle,
RealBitcast cross-arch parity, PatchEntry.value bound, and more), and
**none of them is tracked anywhere else.** Do not treat a dig here as
tracked just because it is written down.

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

**Status: PARTIAL (blu, 2026-07-09).** The foreword slice is done
(fester, 2026-07-03; 28 sites, 14 modules) and the `-Apps` slice is
done (blu, CL 7415; 24 sites, 13 files -- see Claimed). What remains
here is the `-FW` test surface.

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

**Pointers:** `docs/ExaminersAssay.md`
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

**Pointers:** `docs/Agents/blu-workplan.md` (follow-up 3).

### 6. Known-defect tests (two standalone digs)

From `docs/ExaminersAssay.md` Known Defects — each independent.
(6a duplicate-`Event` and 6b historian-test are RESOLVED — see Claimed.)

- **6c. db-full-test — mystery SOLVED, test repaired, un-skip
  blocked on BulkLoader + expected adjudication (blu, 2026-07-09).**
  The "CDX1000 token
  mismatch in Server.codex only when concatenated" note was a
  misattribution from stale line-mapping: the real errors were (a)
  CDX1070 multi-line applications in the TEST itself (query args on
  following lines — pre-dates the newline-application rule), (b)
  btree-insert/btree-range-scan/page-insert API rot (test used old
  record-result fields .bt-tree/.page/.rs-count; current APIs return
  tuples — rewrote with the depot's `let (x, _) =` idiom), (c)
  sha256-hex name rot (never existed in the Sha256 foreword; added a
  Text->Text helper to Security composing
  sha256-to-hex/sha256/text-to-bytes, same composition Backup line 94
  already uses), (d) CDX2000 chained generic-field access in
  Backup.codex backup-count-pages `((e.value).cat-heap)` — fixed by
  binding first, the file's own idiom. Also converted 48 print-line
  calls to print-line-uni (output was raw CCE). Test now compiles and
  runs all 48 sections; after the entry-16 miscompile fix (Claimed)
  the plan-cache line matches too, so 39/48 lines match `.expected`.
  Adjudication of the 9 remaining divergences: the `.expected` was
  HAND-AUTHORED (test never compiled, so never ran) — several
  "divergences" are correct current behavior (nl-join 12 = 4 Eng
  emps x 3 depts; left-join is by-design first-match-per-left-row;
  bp evictions=0 because bp-fetch pins and the test never unpins so
  no victim exists). Remaining blockers before un-skip: bulk-import
  loading 0 rows + bulk-insert clobbering bi-ok=True over inner
  failure (BulkLoader.codex bulk-insert always returns bi-ok=True —
  real defect), and final `.expected` refresh after that fix plus a
  per-line semantics verdict on joins/mvcc/colstore/backup.

**Crew notes:** 6c's residuals are a small BulkLoader fix +
expected adjudication/refresh.

### 16b. emit-pattern silent tag-0 fallback (hardening, residual of 16)

**What:** `emit-pattern`'s IrCtorPat still silently defaults
expected-tag to 0 when neither the scrut-ty nor the pattern ty
resolves to a SumTy. The class-1 miscompile that exposed this is
FIXED at its root in lowering (see the Claimed entry 16 for the full
story) -- but the emitter fallback remains a latent trap: any future
unresolved shape reaching it would misdispatch silently rather than
fault.

**The dig:** resolve the sum BY CONSTRUCTOR NAME from `st.type-defs`
(or emit an error) so no future unresolved shape can misdispatch
silently. Small, compiler codegen, seed change -- full gates.

**Crew notes:** bind-the-field-access-to-a-let is a complete
workaround for any residual shape meanwhile. Minimized repro
(deterministic, seed B4EAC215), also pinned as battery test
`codex/test/when-generic-field`:

    ProbeOp = | OpScan (Text) | OpFilter (Text) (Integer)
    lookup-like : Integer -> Pair Integer (Maybe ProbeOp)
    lookup-like (k) =
      if k == 1 then make-pair 10 (Just (OpScan "emp"))
      else make-pair 20 None
    -- in opening:
    let r2 = lookup-like 2
    in let b2 = r2.snd
    in act
      print-line-uni (when b2 is Just (p) -> "b2-hit" is None -> "b2-miss")        -- prints b2-miss (OK)
      print-line-uni (when (r2.snd) is Just (p) -> "i2-hit" is None -> "i2-miss")  -- prints GARBAGE/empty
    end

### 7. RealBitcast parity on ARM64 and RISC-V

**Status: OVERTAKEN (audit blu, 2026-07-09).** val shipped the ARM64 +
RISC-V bitcast mirrors the same week the map was drawn (main CL 6728,
2026-07-03: "Both plug CDX build clean. Runtime cross-verification
pending Renode"). `real-bitcast.codex` + `.expected` exist in the
battery tree, `rv-fmv-x-d` / FMOV forms are live in both codegens.
Residual dig: run the ARM64 + RISC-V cross batteries and confirm
real-bitcast passes on Renode — that closes the entry for good.

**What (historical):** IEEE-754 bit-pattern intrinsics for Real landed
on x86-64 (acceptance 5/5 f64, 5/5 f32). The ARM64 and RISC-V plugs do
not emit them yet.

**The win:** Real serialization (Wav/Flac/GGUF/network float
encoding) works cross-arch; the cross battery can un-skip anything
float-serialization-shaped.

**The dig:** Mirror the x86-64 emission in each plug (FMOV
general<->vector on ARM64; fmv.x.d / fmv.d.x on RISC-V), port the
acceptance test to the cross harness, run the cross battery. Plug
rebuild only — no seed.

**Crew notes:** Medium; ideal for an agent already fluent in the
plug codegen files (the val/reek pattern).

**Pointers:** `codex/plugs/arm64/`, `codex/plugs/riscv/`.

### 8. ARM64 register allocator — local recycling

**Status: OVERTAKEN (audit blu, 2026-07-09).** Arm64CodeGen already
carries the pressure-site recycling shape: `peak-local` +
`saved-next-local` fields, peak tracking on every local alloc,
`a64-compute-save-pairs` sized from peak, and next-local recycle at
the user-call staging site (~line 1116). CAUTION for any future work
here: the RISC-V PER-EXPRESSION recycling variant was reverted as
unsound (val 6875/6909/6928, main 6939 — TCO frameless-mode leak
clobbered caller locals); the pressure-site variant is the standing
design. Residual dig: a regstress-style bench + ARM64 cross battery
run to pin the behavior, if anyone wants the receipt.

**What (historical):** The ARM64 plug has the same monotonic
`next-local` exhaustion the RISC-V plug had; the design doc exists
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

**Pointers:** `docs/Designs/Active/Compiler/Arm64RegisterAllocator.md`,
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
fluency. Not tracked anywhere else.

**Pointers:** x86 reference in
`codex/os/sched/` + `docs/ArchitectsSketchbook.md` (SMP Memory
Model).

### 10. DynamicSurvey Phase 1 — auto-retry on deck overflow

**Status: CLOSED — OBSOLETE (blu, 2026-07-07).** The entire survey
system (multipliers, SurveyConfig, the `-Survey` knob, the Phase-1
retry) was deleted when the demand-paged arena shipped (blu 7190-7198,
seed DDAB0BD2...). Decks are fixed generous floors; physical memory
commits on touch; there is nothing left to retry. Phases 2 and 3 will
never be built.

**FLEET NOTE (merge-down, all agents):** the new seed means every
binary you compile demand-boots. The full battery, all 52 plugs,
ARM64/RISC-V boards, and an adversarial +86KB growth pingpong are
green on it. If your stream carries a diverged seed, REBUILD from
merged source per PerforceProcess (do not resolve -at a seed). New
invariant if you write spawn/stack code: a stack must never point
into a not-present page — pre-touch in-heap stack carves (see the
Page Fault Handler prose in X86_64Boot).

Original entry (historical):
The harness catches CDX9002 and retries with raised `-Survey`
multipliers. What remained was Phase 2 (sidecar measure-and-feed-back)
and Phase 3 (compiler-internal proportional survey).

**What:** Survey multipliers are static; unusually dense source
overflows a phase deck (CDX9002, now a clean error halt). Phase 1 —
catch CDX9002 in the harness and retry with raised multipliers via
the existing `-Survey` override — is designed and unbuilt. Related
annoyance: `MEASURE`/measure-survey overflows
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

**Pointers:** `build/compile.ps1` `-Survey` flag, CDX9002 in
`docs/ArchitectsSketchbook.md` (CHECK Deck Overflow).

### 11. GPU Globe bundle — PTX ABI fix and scene completion

**RE-SCOPED (audit blu, 2026-07-09):** shelved CL 6166 no longer
exists (the shelf is gone; the app files must be re-created or
recovered from whoever holds them). The PTX plug was reworked since
the entry was drawn — Real-is-f64 (blu 7319, main 7321), entry-drop
index fix, `kernel-`/`gpu-` prefixes now emit `.entry`. Whether the
`%lv_` cross-call corruption survives that rework is UNKNOWN: the
first step of this dig is now to re-run the minimal two-function
repro before touching anything. The three follow-ups may be
partially overtaken (entry handling changed).

**What (historical):** The Codex -> PTX -> CUDA pipeline renders a
textured earth (proven 2026-06-27). The single blocker for UV-mapped
earth is the PTX function-call ABI: `%lv_` registers corrupt across
`.func` calls. Behind it, three scoped follow-ups: dead-code
elimination of `kernel-` prefixed defs in IR mode, stub emission for
recognized intrinsics, and the black-hole scene entry shim (same
treatment gpu_earth got). App files are shelved in CL 6166 on the
blu stream.

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

**Pointers:** `codex/plugs/` PTX emitter, shelved
CL 6166, `apps/globe/run.ps1`.

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

### 15. GamesDemo runtime idle-hang bisect

**What:** `apps/games/classic/GamesDemo.codex` compiles again as of
blu 7188 (it was dark-ship rot: `opening : [Console] None` failed
CDX2001, so it had NO compile coverage and had never been RUN). At
runtime it never prints: the VM goes idle (300s wall, ~6s CPU - a
hlt/wait, not a compute grind) somewhere in the 20 let-bound game
runners before the act block. The TicTacToe leg is exonerated
(run-ttt-perfect completes in under a second standalone and is
pinned by codex/test/ttt-perfect.expected).

**The win:** 20 game engines get their first real runtime coverage;
whatever waits forever is a latent bug in a shipped games engine.

**The dig:** Bisect by commenting the let list down (compile is ~2
min per cycle with the 18-chapter chain; binary search, ~5 cycles).
When the waiter is named, read its runner for the wait (RNG? channel?
unbounded sim loop?), fix or file, and pin GamesDemo with an
.expected so the rot cannot return. Watch for a second waiter behind
the first.

**Crew notes:** aliasing trap relevant to ALL game engines here:
`list-set-at` MUTATES IN PLACE (pinned by
codex/test/wavelet-sort-aliasing.codex). Any runner that treats it as
a functional update and re-reads a stale list is corrupt; TicTacToe
had exactly this (fixed 7188, ttt-move now copies squares first). If
a game's sim loop misbehaves during the bisect, check its list
updates before its logic.

**Pointers:** blu 7188 (compile fix + the ttt precedent),
`apps/games/classic/GamesDemo.codex`, quire map entry `Games` in
`build/quire-map.ps1`.

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
  defaulting): dig when a real program needs them.
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
  polarity threading) — a compiler item; touches the unifier's
  core.
- **Vision-check adversarial probe DESIGN** ("fulfill
  the vision check") — designing probes that try to break the
  by-construction claims is senior work; executing a designed probe
  catalog is delegatable and will be mapped when the designs exist.
  Status: the LINEAR leg's stage 0 is done (blu CL 6819, nine open
  routes); capabilities and punctual legs remain. The LinearOwnership
  FIX campaign stays held back until its design is ruled.
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
- **1 (-Apps slice). CDX2051 sweep of the -Apps test surface** -- blu, DONE
  2026-07-09 (CL 7415). Cleared all 24 CDX2051 sites across 13 files reachable
  from the `-Apps` battery: apps/data (Row col-count, Protocol frame-type x2,
  Schema col-count x2, Page page-type + slot-offset/slot-length, BufferPool
  capacity), os/net (Udp read-be16 src/dst ports -- mirrors the Tcp.codex:88
  precedent, DnsResolver ttl), os/kernel (Vga attr, Console backspace col),
  os/sched (LockFreeChannel owner-core, Watchdog action-count), test/apps
  (vga-test cursor attr), apps/webapp (WebTheme bdr/eu/exy/ws builders). All
  fixed with the blessed `__narrow`-at-store idiom -- values in range by
  construction (list lengths, decoded wire fields, VGA attrs, small counters);
  never trap; no bound deleted. All 16 affected tests recompile 0 errors and
  RUN PASS against `.expected`; BVT green; fresh full `-Apps -Jobs 8` census:
  597 total / 496 pass / 9 fail / 92 skip, and the 9 are pre-existing
  non-CDX2051 classes (see entry 12 closure for the catalogue). Console.codex
  `row` store deliberately left un-narrowed: the checker types binary exprs by
  the LEFT operand, so `bounded - 1` passes silently (known lint gap, recorded
  in memory). NO seed change (none of the 13 files are compiler-cited).
  Mem/time: O(1) per narrow, no allocation, no loops added.
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
- **6d. db-test heap overflow** -- blu, RESOLVED 2026-07-09. The 2 GB heap-scan
  overflow is GONE under the demand-paged arena — compiles, runs in
  the standard 3072 MB VM, and is UN-SKIPPED in the default battery
  (new baseline 341/327/0/14). The old `.expected` was captured from
  a buggy run that silently dropped row 2 ("Bob") — select-all showed
  2 of 3 rows and group sums matched the loss; current output has all
  3 rows with correct arithmetic; `.expected` refreshed to the
  verified-correct output. No code change — sidecar-only.
- **12. Batch REPL scalability (-Apps timeouts)** -- blu, CLOSED-OBSOLETE
  2026-07-09. The ~118-timeout class
  no longer exists. A fresh full `-Apps -Jobs 8` sweep (post CL 7415,
  clean test-output) completes with ZERO timeouts: total=597 pass=496
  fail=9 skip=92. The census predated the batched output path (blu
  7301-7311) and the plain-CDX batch protocol fix (2026-07-07); those
  fixes dissolved the class. The 9 residual failures are real per-test
  defects, not harness scalability: boot-stage-test + nic-ping
  (CDX2031/2033 effect rot in test source), erp-server-test (CDX3001),
  quaternion-test (CDX2001 x12), spark-shapes-test (CDX2085 == on
  Real), spark-mesh-test + wave3-test (output mismatch),
  xhci-discover-test (runtime), historian-test-full
  (expected-error-but-compiled). Each is a separate scoped follow-up.
  Harness hygiene note for whoever picks those up: `build/test.ps1`
  leaves stale per-test `build.log`s from prior runs -- clear
  `test-output/` (or check log timestamps) before trusting error lines,
  or recompile the failure individually via `build/compile.ps1`.
- **16. when-on-inline-generic-field miscompile (class-1)** -- blu, RESOLVED
  2026-07-09. Root cause was in LOWERING,
  not the emitter: `AFieldAccess` typed the IR node with the record
  DEFINITION's declared field type. For a generic record the receiver
  name's span-recorded type is an unresolved `RecordTy (tvar)(tvar)`
  shape (the unifier never binds those tvars), so the field type
  stayed a bare tvar; match lowering copied that tvar onto every
  `IrCtorPat`, and the emitter -- unable to resolve a SumTy from
  either -- silently defaulted every arm's expected tag to 0
  (`emit-pattern` IrCtorPat fallback). Just (tag 0) matched by
  coincidence; None (tag 1) matched NO arm and the match result local
  was never written -- garbage text returned. Fix, two parts in
  `IR/Lowering.codex` (annotation-only -- record LAYOUT typing is
  untouched, construction/access offsets cannot diverge):
  `refine-receiver-ty` (an IrName receiver whose type still has tvars
  takes its overlay/base binding type when that one is tvar-free --
  lower-let stores the instantiated type) + `fa-pair-args`/
  `fa-apply-subst` (pair the resolved record's declared args with the
  receiver's instantiation args POSITIONALLY, keyed by BOTH nullary
  param name and TypeVar id -- the env scheme's RecordTy args are
  TypeVars, which is why name-only substitution missed). Pinned as
  battery test `codex/test/when-generic-field` (4-line expected).
  All gates green, fixed point holds, battery 342/328/0/14, full
  -Apps swept. db-full-test's plan-cache line now matches expected
  (q2=miss after-stale=miss). RESIDUAL HARDENING (open, small):
  `emit-pattern`'s IrCtorPat still silently defaults expected-tag to 0
  when neither scrut-ty nor pattern ty resolves to a SumTy -- resolve
  the sum BY CONSTRUCTOR NAME from st.type-defs, or error, so no
  future unresolved shape can misdispatch silently. Workaround for any
  residual shape meanwhile: bind the field access to a let before the
  when.
