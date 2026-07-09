# Blu Agent Workplan — 2026-07-08

## LATEST: capability stage 7 — follow-ups + stage-4 regression fix

Closed every remaining capability follow-up AND fixed a stage-4
regression the follow-ups uncovered. THE REGRESSION: stage 4 made
boot grants manifest-derived, but the gated kernel builtins
(process-spawn, chan-kern-*, identity-set/-set-proc,
process-restrict-cap) still had empty effect rows — so no program
could declare the effect, earn the grant, or pass the in-kernel cap
check. Nine -Apps identity tests silently went pass->fail across
stages 4-6 (not in the default battery, so unnoticed). Fixed with
honest rows: spawn family + chan-kern-* = [Concurrent]; identity-set
= [Identity]; process-restrict-cap = new [Capability]; identity-set-proc
= [Identity, Capability]. Spawn callback is effect-POLYMORPHIC
(ForAllEff over the callback row — the child's effects are not the
caller's). All nine tests act-converted and passing.
Other follow-ups: read-key (syscall 2) -> cap-console-read; Gpu +
Capability loader arms in VerifiedLoader; granted-capabilities renamed
capability-vocabulary (it was never a grant — that misnomer hid the
regression); opening-as-value manifest hole confirmed CLOSED + pinned
(errors/opening-value-effect-undeclared).
KNOWN BUG, deferred: two-level spawn (child spawns grandchild) hangs
on post-stage-4 seeds — latent heap-budget bug in the spawn allocator,
exposed because caps work is the first thing that can build a working
nested spawn. Scheduler/emit-spawn owner. See CapabilityProbe.md.

## EARLIER: capability stage 6 — identity key syscalls gated on a real Identity effect

key-load / key-zero / key-status (syscalls 15/16/18) are now real
intrinsics carrying [Identity], and the syscall handlers consult the
capability word (bit 15, cap-identity) with the stage-5 deny
pattern. Identity is wired through the full vocabulary: manifest cap
id 7, boot grant, granted-capabilities, verifier cap-name, loader
kern-bit arm. The reshaping discovery: IdentityManager had NEVER
compiled — its wrappers called a `__syscall` intrinsic that does not
exist (CDX3002), so the pinned-key path was completely dark; its
dead wrappers are deleted (the chapter now uses the builtins;
`__syscall 3 0` -> get-ticks). IdentityManager still does not
compile — its console layer calls `print` and `read-key`, which
exist nowhere (pre-existing; a keystroke API is firstboot-ceremony
surface, fester's arena). Probes: cap-identity-denied (runtime
grant/strip/deny) + errors/cap-launder-pure-key (2031+2033). Details
and remaining follow-ups in CapabilityProbe.md Status.

## EARLIER: capability stage 5 — block syscalls consult the cap word

All four block syscalls (10 read, 11 write, 12 sector count, 13
select) now run emit-check-capability against cap-block-device
before touching the device; a process without the bit falls through
the dispatch chain and gets -1 with the device untouched. The same
CL fixed a latent emit-check-capability bug: the entry-address
multiply was hardcoded `imul rcx, rbx` while the current-proc index
lives in R11 — the console-write gate only worked because RBX
happened to be zero at its call sites; the block-read helper parks
the SECTOR NUMBER in RBX, so the widened surface would have read
garbage cap words without the fix. VerifiedLoader gained the missing
Device arm in cdx-cap-to-kern-bits (manifest Device previously
mapped to zero kernel bits on the loader path). The probe's first
run then caught a THIRD latent bug: manifest-base-loop
(X86_64Chapter) and effect-base-loop (CdxVerifier) split dotted
effect names on ASCII 46, which in CCE is the letter H (same bug
find-dot fixed in CL 6509, two more copies) — so Device.Block never
resolved to Device, the caps section dropped the entry, and the
boot grant carried Console bits only. Both fixed with the
`char-code (char-at "." 0)` idiom. Runtime probe
codex/test/cap-block-denied (granted 0, strip own grant, denied -1)
joined the default battery; the five -Apps disk tests that reach
block syscalls now declare [Device.Block] on opening so their
manifests grant the bit (their openings are VALUE bindings, which
escape the row-subset check and carried empty manifests — recorded
as an open checker follow-up in CapabilityProbe.md). Codegen change:
two-pass converged, seed rebuilt. Remaining cap follow-ups in
CapabilityProbe.md section Status (key/identity syscalls need an
Identity effect source; read-key could gate on cap-console-read;
granted-capabilities rename; opening-value manifest hole).

## EARLIER: capability stage 4 COMPLETE (CL 7325/7326, on main 7327) — vision-check capability leg CLOSED

Boot now grants proc 0 exactly its own manifest's capability mask
(emit-grant-cap-mask from manifest-cap-names; empty manifest = zero
grants — secure-by-default is literal). The syscall capability check
is real for the first time: it previously tested bit argument-mod-64
and branched on a flag `bt` does not set; now `bt r64, imm8` on the
required cap bit, branch on CF. New ProcessCaps chapter
(codex/os/verify) wires LoadDecision grants into the real
process-table capability word — runtime-proven by
codex/test/apps/process-caps-test. ClaimsCalibration: "rejected at
load time" is EARNED for the wired path. Seed 25AC86EF29634165
(blu == main, depot verified), battery 329/314/0/15 zero failures.
Follow-ups (polish): widen the syscall surface that consults the cap
word (block syscalls do not check yet); rename/narrow the type
checker's static granted-capabilities vocabulary list.

## EARLIER (same day): PTX Real-is-f64 campaign COMPLETE (CL 7319) — reek-fontexplorer UNBLOCKED

The dead-learning-signal blocker had three layers, all fixed:
1. The PTX plug's float path was f32 mislabeled as f64 (RealTy ->
   .f32/%fs, literals down-converted, the -f64 intrinsics emitting
   .f32 into %rd integer registers). Real is f64: the float path is
   now single-width .f64/%fd throughout (arithmetic, comparisons,
   literals, params, returns, locals, TCO, loads/stores, math
   intrinsics). Real approximate widens to f64 (documented; second
   f32 width awaits a measured need); trig cvt-sandwiches through the
   f32 approx units until libdevice (K9).
2. DeviceEffect: device-load-f64 returns Real, device-store-f64 takes
   a Real value (were Integer — Real kernels could not type-check).
3. ptx-emit-defs conflated loop index with per-def register base:
   kernel ENTRIES were silently dropped whenever any plain function
   preceded them (the stale checked-in mlp.ptx shows it — zero entry
   points). Fixed (i, base-slot split); ptx-is-kernel-entry also
   recognizes fontexplorer's kernel- prefix — all 10 MlpKernels now
   emit .visible .entry.

**FOR REEK-FONTEXPLORER:** merge down, rebuild the ptx plug
(codex/plugs/ptx/build.ps1), then your side: MlpKernels Integer ->
Real, host double[] transfer (BitConverter bits). Regression check:
codex/plugs/ptx/test-f64.ps1 (PASS). Battery 329/314/0/15. No seed
change. SPIR-V plug has the same f32 story — flag it if you need
Vulkan parity, it is a follow-up.

## EARLIER (same day) — WCET plug audit + batch output path, all on main

Two campaigns completed and copied up as CL 7313 (seed-carrying, target
proof run on the main workspace, depot digest verified). blu == main,
seed F1F9DF8524A7A35E, battery baseline 329/314/0/15, workspace clean —
nothing pending, nothing shelved.

1. **WCET plug audit** (CL 7300): RISC-V [WCET] reports were formatted
   before module-level rv-compact-nops and over-counted NOP'd prologue
   saves (probe: 36 reported vs 28 actual). Fixed by deferring
   RvWcetPending ranges past compaction; wire byte-identical. ARM64
   audited HONEST (per-function placeholder-aware compaction runs
   before the delta read) — no change; its run.ps1 now echoes
   [WCET]/[WARN] lines (regex match; the first line glues to the wire
   bytes with no newline).
2. **Batch output path** (CLs 7301/7304/7305/7307 CDX leg, 7311 TEXT
   leg, docs 7308/7312): codex-vm bulk blit (doorbell 0x510 cmd 3,
   cells 36160/36168, capability probe IN 0x511 == 0xB7 cached at boot
   in flag cell 36176) + write-binary-buf builtin + content/MAP1 blit
   + staged print sink (__serial_put / __print_begin / __print_flush
   over R10 heap-top scratch, cursor cell 36192). Measured: self-compile
   25.9s -> 7.8s, gate build 188.5s -> 96.7s, text stages halved.
   Untouched on purpose: raw prints, COM2 control (HEAP: OUT-time
   detection), UEFI prints, !EXC dumps.

**OTHER-AGENTS notes:** merge down to inherit the ~2x gate speedup; the
new codex-vm.exe is backward-compatible with older seeds (blit is
probe-gated). Do NOT adopt the VM's old 0x700000 output ring — that GPA
is live heap in current guests. New-builtin bootstrap rule (relearned
this session): compiler source may only USE a new builtin one seed
AFTER the seed that registers it — land machinery first, use second.
Cells 36160-36199 are now claimed (blit addr/len, flag, print base/
cursor); next free metadata cell is 36200, PDPT starts at 36864.

**Next session:** no in-flight work — pick a fresh stream with Damian.
Candidates, roughly by value: plugs' own wire output (the last
per-byte serial path, serial-write-byte loops in all 52 plugs' output
stage); GPU Real f64 device intrinsics (unblocks reek-fontexplorer's
MLP training); capability stage-4 OS wiring (ClaimsCalibration's last
open BY-CONSTRUCTION gap); __alloc/emit-def compute profiling (new
top of the profile at 22%, and the residual ~16s per TEXT stage).
One host BSOD occurred this session under multi-VM load (known DDR5
XMP trap) — crashed runs just re-run.

## Earlier today: emit-mutation class-2 audit sweep (COMPLETE) + fix
Full sweep of X86_64.codex / X86_64Builtins.codex / X86_64Compound.codex
for the two standing emit invariants. Verdict:
- Class-2 (mutating an operand's result register): ZERO violations
  beyond the stage-4-fixed vec extracts. The two deliberate in-place
  sites (both-complex pop+op shortcut, reg-left commutative fold)
  mutate only guaranteed-non-name complex results, with fc-evict and
  a next-temp retention bump — sound, prose-documented.
- Class-1 (holding a bare emit-expr result across another emit-expr):
  ONE real bug — emit-vec-arith-core (backs the vec-add/sub/mul/div
  builtins) emitted lhs, then rhs, and spilled both only afterwards;
  any call in rhs clobbers every caller-saved reg, so f op g computed
  g op g. Reproduced deterministically (add: 20.5 for 11.75, sub: 0.0
  for 15.25, mul: 16 for 12, div: 1 for 3.75) with
  codex/test/vec-arith-hazards.codex (multi-branch producers keep the
  operands out of the inliner). Fix: spill lhs before emitting rhs
  (the house pattern every neighbor already follows).
- Why the bounded holds elsewhere are safe (audit rationale, do not
  rediscover): the three register pools are disjoint (rotation temps
  RAX/RCX/RDX/RSI/RDI/R11; locals RBX/R12-R14; load-local scratch
  R8/R9/R15 on a 3-toggle), rotation positions advance monotonically
  so a held rotation temp survives <= 5 subsequent alloc-temps, and a
  plain alloc-temp collision with a held raw-RAX call result is
  benign when the new temp's first write is the self-copy (mov-rr
  returns [] for rd == rs).
- Two fragile-but-sound invariants to keep in mind: (a) IrDivInt/
  IrRemInt's save-to-R11 would clobber l-reg if l-reg were R11 while
  r-reg is RDX/RAX — unreachable today because every call site stages
  or folds so l-reg is R8, a local, or a non-R11 temp; (b) vec
  emitters hold XMM0/XMM1 across emit-bivy-alloc's __alloc call —
  sound because __alloc is integer-only (mov/add/rep stosb), a fact
  any future __alloc change must preserve.

## Shipped earlier today: WCET true instruction accounting (Damian: "punctual spot
## on" is the first task; then low-level — language, codegen, binary
## plugs, GPU. UEFI boot is fester's arena, stay out).
CDX6010 now decodes the function's finished bytes instead of counting
append calls: new chapter `codex/compiler/Emit/X86_64InsnCount.codex`
(closed-vocabulary x86-64 length decoder — prefixes 66/F0/F2/F3, REX,
1-byte + 0F maps, ModRM/SIB/disp, immediates; unknown opcode returns
-(offset+1), surfaced as new warning CDX6012 RtWcetOpaque, never a
partial number). emit-all-defs decodes [start, code-len) after
emit-function returns, so NOP compaction (nop-sub-rsp = 2 insns where
1 was appended, nop-unused-pushes merges 2 pushes into 1) is included;
deferred imm patches don't move boundaries. The insn-count field is
DELETED from CodegenState — st-append-code sheds one __record-set per
append (hot-path allocation win). wcet-validate.ps1: DRIFT verdict
removed, observed > static is now FAIL (punctual code cannot loop, so
any dynamic path is a subset of the decoded body). Gates in flight.

## Status addendum 2026-07-07: WI-3 WCET-validation slice SHIPPED (same day).
Damian ruling: empirical WCET validation approved; the rest of WI-3
stays deferred and the VM never gains an interpreting/rewriting mode.
Shipped: codex-vm `-wcet <fn>` (DR0-DR3 exec breakpoints + TF
stepping, pure observation), `build/wcet-validate.ps1` (gate:
observed <= budget per invocation), `codex/test/wcet-probe.codex`.
Two findings on first run: (1) CDX6010 UNDER-COUNTS — st-append-code
counts +1 per append call, not per instruction (clamp-add claimed 28,
machine executed 34) — FOLLOW-UP work item: true instruction
accounting, then tighten the gate to observed <= static; (2) codex-vm's
ExceptionExitBitmap was inert (ExtendedVmExits.ExceptionExit never
enabled) — all exceptions have always gone to the guest IDT; wcet mode
enables #DB-only interception, all other modes byte-for-byte
unchanged. No compiler source touched; no seed.

## Status: NoAliasCodegen campaign COMPLETE (stages 0-4).
Stages 0-3 + /handoff skill ON MAIN (copy-ups 7267 + 7271, main seed
A791B8D9 verified). Stage 4 (WI-2 VecArray) is the final stage:
vec-load-at/vec-store-at builtins + Math chapter VecArray + the
vec-extract/vec4-extract latent-bug fix (call receiver clobbered by
temp-rotation wrap onto raw RAX; in-place index mutation corrupting
register-resident locals — probes in codex/test/
vec-extract-hazards.codex). Measured n=100k: read-reduce flat 27 vs
boxed 14 ticks (va-get call + per-read cell), fill parity 7 vs 6,
bit-exact match; the stage-4 claim is structural (one 16N allocation
replaces N cells + 8N pointers; stores allocate nothing) — wall-clock
wins await inline/fused emitters (pickup: a measured vector hot
loop). PhysicalCostCodegen.md status COMPLETE with per-stage as-built
sections; WI-3 stays deferred. NEXT: batched copy-up of stage 4
(merge down first, check main cadence, p4 edit seed, verify depot
digest).

OTHER AGENTS — the emitter now carries a field-load cache (stage 2).
STANDING INVARIANT for any x86-64 emit change: a register write that
does not go through alloc-temp must fc-evict-reg first (in-place
folds, dest-driven writes, raw fixed-reg sequences). The branch-label
flush choke points are patch-jcc-at/patch-jmp-at. Violating this
emits compilers that miscompile only at self-compile scale — the
battery catches it as wrong-value output tests, and the 3-pass
pingpong (Sut -> stage1 -> stage2, stage1==stage2 AND stage1 must
survive its own self-compile) is the gate that proves it. CDX4011 is
the campaign's info diagnostic (no-alias instrumentation +
narrowing). Earlier "input-alignment crash" panic in this file was a
generation confound — retracted; seeds were always healthy.

## Prior status 2026-07-07 (superseded): stages 0+1 shipped, stage 2
shelved on what was then thought a latent input-alignment crash.

Campaign (PhysicalCostCodegen.md WI-1, plan approved by Damian):
- Stage 0 (CL 7243): IRDef.unique-params carrier + CDX4011 no-alias
  instrumentation. Gates green, one-pass, battery 320/305/0/15.
- Stage 1 (CL 7247 + seed 91B2927D): checker tracks minted linear
  locals (let + act binds from linear-returning calls); effectful
  linear returns sanctioned; LinearOwnership residual closed. Gates
  green, one-pass, battery 323/308/0/15, self-verify, depot digest
  verified. Zero depot fallout (surveyed).
- Stage 2 (CL 7248, SHELVED): alias-free field-load cache in the
  x86-64 emitter (FieldCacheEntry on CodegenState; consult/populate in
  emit-field-access; default-flush at apply-exit/calls/patch-labels/
  stores; evictions at alloc-temp/alloc-local/store-local/idiv).
  Includes the memory-model ruling + WI-3 deferral in
  PhysicalCostCodegen.md and codex/test/field-cache-shape probes.
  The stage-2 code is PROVEN INNOCENT of the blocker below (green on
  prev seed AND on 91B2 at a different input size).

## BLOCKER RESOLVED (same day) — it was the stage-2 cache, root-caused
The "latent input-size crash" hypothesis was WRONG — a generation
confound (the DemandPagingVictory lesson repeating). The only crashing
binary was build #1's stage1.cdx: the FIRST compiler ever EMITTED by
the stage-2 field-load-cache codegen. Every input-size correlation
dissolved once the kernel provenance was pinned by timestamp+archive
forensics (build.ps1 archives build/output per run — the archives
identified everything). Seeds 91B2927D and 917711F3 are HEALTHY; all
lineage compilers produce byte-identical outputs (FC72... from three
generations).

ROOT CAUSE: the emitter's in-place binary-op folds write an operand's
RESULT REGISTER without going through alloc-temp — emit-reg-right-
inplace / emit-binary-reg-left commutative fold (`inplace-int-op` into
l-reg / r.reg) and the both-complex pop+op shortcut (`pop & add/imul
into r.reg`). When that operand was a field access, the field cache
had just recorded that register as holding `x.f`; after `x.f + y` the
register holds the sum but the cache still said `x.f` — a later read
of `x.f` hit the stale register. Small tests rarely trigger the fold
(needs a register-local operand against a complex field-access
operand); the 28k-line self-compile triggers it constantly — hence
"only crashes at scale". The pre-fix battery caught 4 wrong-output
repros (av-codec/circbuf/color/final-batch), confirming the class.
FIXES (all in the stage-2 CL): fc-evict-reg at the four fold sites;
fc-evict at emit-to-local entry (destination-driven writes to arg
registers bypassed alloc-temp — same class); RAX/R11 eviction in
emit-load-from-handler-table; exit barriers (emit-fc-barrier) on
handle/try/fork/await/timeout dispatch. INVARIANT for future emit
work: any code path that WRITES a register outside alloc-temp must
fc-evict it first; the cache's soundness is exactly this discipline.
Verified: 3-pass pingpong converges (stage1C == stage2C byte-identical)
and the field-cache-shape semantic probes pass on the cache-emitted
compiler.

## Prior status 2026-07-07: DEMAND-PAGED ARENA SHIPPED, SURVEY SYSTEM DELETED
(blu 7190-7200, LOCAL — copy-up pending, seed-carrying, Damian's call).
Seed DDAB0BD288C93AAB: #PF handler + [6MB,2GB) demand range + fixed
generous deck floors; SurveyConfig/multipliers/-Survey/DynamicSurvey
deleted. Gates: one-pass fixed point, battery 319/304/0/15, self-verify,
all 52 plugs, ARM64+RISC-V boards, +86KB growth pingpong, spawn-stack
fix (pre-touch invariant). ROOT CAUSE of the week-long "demand bug":
generation confounded with configuration — check-mul 40 miscompiled
pass-2 silently; demand paging was innocent. Full story:
docs/Designs/Compiler/Done/DemandPagingVictory.md. Also same day: the
tic-tac-toe honesty fix (7188/7189, list-set-at aliasing + first games
battery coverage). OTHER AGENTS: see QuartermastersMap dig 10 fleet
note before merging down the new seed.

## Prior status 2026-07-05 (late): TWO WINS SHIPPED to main (copy-up CL 7162).
(1) CL 7130 - survey-check-mul default 200->40 (CHECK reservation peak
978->~620 MB; source-only, no seed - reservation size does not affect
emitted output). (2) CL 7161 - codex-vm.c watchpoint feature: `-hwwatch`
DR0/DR7 hardware watchpoint + demand-aware value-filtered `-watch`
(`-watch-val`, and watch_init now pre-commits+maps the watched 2 MB chunk
so the host demand-commit can't clobber the RO page). Sample battery
303/0, seed untouched (7CE0E867). blu == main on all 5 files.

## ACTIVE THREAD: demand-paged arena (blu 7142, SHELVED, NOT on main).
The #PF demand mechanism is proven (byte-identical self-compile on
codex-vm+QEMU) but running the compiler under demand paging
deterministically garbles diagnostic Text. Session 3 built the DR
watchpoint (shipped) + a guest-side #DB recorder (in 7142) and caught the
mechanism: a SINGLE heap store of the LEFT-operand pointer into the concat
result. rsp is a NORMAL stack => NOT a stack-heap collision (that theory is
dead). R8=R9=operand-ptr => the "R8/R9-staged binary operands" codegen opt
is the prime suspect. NEXT (no VM roulette): read that emit path
statically. Full writeup: docs/Designs/Compiler/Active/DemandPagedArena.md
"Session 3". Resume recipe + tooling addresses in memory ir-bloat-campaign
top block. OTHER AGENTS: do NOT touch demand paging or codex-vm watchpoints
without syncing with blu; nothing of this is on main except 7130+7161.

---

## (history 2026-07-04) FOUR RESULTS SHIPPED TODAY. (1) Circular proofs reject CDX4023. (2) Escape instrument repaired + 2 real dangling pointers fixed. (3) Deck-liveness map shipped (one strand pins ~106 MB). (4) POISON-COMPACT WAS RED ON MAIN — pre-existing, all seeds; diagnosed to the root (scope output + checker tables in scratch), sorted-et spine fix shipped, campaign staged in PHASE-ARCHITECTURE.md.

**MAKE-IT-GREEN SESSION (late evening).** Piecewise with full gates
per piece (the lesson applied). Piece A (copy-bindings-deep at
sorted-all): GATE-GREEN, one-pass, SHIPPED - binding graphs severed
from check scratch; poison counter unmoved (21 - the failing sites
read expr-type ANNOTATIONS, not bindings). Piece B
(copy-expr-types-deep): deterministic GPF on the SUT's own
self-compile - misaligned garbage pointer INSIDE walked graphs
while check scratch is FULLY ALIVE => **the expr-types table
contains invalid type nodes at check tail in NORMAL compiles** (the
table lowering uses to type IR - a live latent-miscompilation
vector; validity guards just move the crash to the next stage's
layout). THE NEXT PROBE: validate ty at record-expr-type time
(tag+alignment+span diagnostic) to catch the producer red-handed;
fix producer; land piece B (prototype evidence: clears 13/21
poison sites); pre-resolve annotations; green. ALSO LEARNED: the
scratch-range map segments are CONFOUNDED (later decks legitimately
recycle earlier scratch addresses) - scope-achapter output is
actually deck-resident (precise walk 0); poison + addr/tag
forensics are the only scratch ground truth. Scope
reservation-copy stage CANCELLED as unnecessary.

**POISON-COMPACT SESSION NOTES (evening).** Damian: "we have phase
specific poison to catch any breakages, be sure you test clean
there." Baseline was ALREADY RED (21 CDX2000 unresolved-type at
emit, identical on seeds 8CA1E63B/09DF0CE4/current) - the second
existed-but-unwatched detector today. Forensic method that worked:
instrument the emit error with addr= + tag= (peek-qword of the
annotation) - a repeated poison byte names the guilty phase (0xA5 =
CHECK). Findings: (a) sort-expr-types was the ONE un-deck-wrapped
allocation in the check tail (spine of the table lowering reads) -
FIXED this CL; (b) binding/expr-type graphs live in check scratch
(check-all-defs runs deck-exited; deep-resolve SHARES typevar-free
subtrees) - a prototype deep copier took poison 21->8 but FAILED
the TEXT-mode gate on its first full pipeline run (lesson: the
poison iteration loop never normal-mode self-compiled the
intermediate binaries - always run the full pipeline before
trusting a compiler change); (c) the residual 8 = TypeVar
annotations resolved at LOWER through poisoned substitutions;
wholesale substitution copy/pre-resolve GPFs (CR2=0 = non-canonical
poison ptr, NOT a page fault) because the substitution graphs
ALREADY dangle into SCOPE-poisoned scratch - scope-achapter runs
outside deck-record, its output survives by overwrite-luck.
CAMPAIGN (PHASE-ARCHITECTURE.md): (1) scope output
reservation-copy, (2) check-tail deep copies + pre-resolve
(resolve-all-expr-types is dead code awaiting this), (3) poison
green + byte-identical = the gate that also unlocks sever/reclaim.
Traps: for/list-map is loop-based (not a stack risk); substitutions
slots are valid self-TypeVar boxes (fresh-var), nulls come from
calloc-zero fields; GPF leaves CR2 stale - CR2=0 with a halt at
__interrupt_common usually means a poison-byte dereference.

**PHASE-MEMORY ESCAPE SESSION (2026-07-04 afternoon).** Damian asked
for a swing at "the phase memory stuff... a nut nobody can crack."
Root cause of the un-crackability: the escape-invariant instrument
itself was broken and reporting green. `pmap-walk self-test FAILED:
got 0 (expect 3)` fired on every -EscapeCheck run; the const
self-type table's HARDCODED tags predated a CodexType reorder (every
tag from TextTy up wrong -> descriptors decoded as wrong ctors ->
walk fell through to 0), plus the known width-packing NOTE. Fixes:
live-type-tag (tags read off live values at emit -> tag drift
impossible by construction), width-ordered mixed-width slots,
pmap-selftest-bag True (always-on tripwire, warning not error - a
gen-1 bootstrap binary must still compile gen 2), locator params
threaded through the walk (CDX9003-AT: Type.field, no per-visit
allocation - Rule 8). First real measurement: PARSE/Document clean;
SCOPE/AChapter had 2 REAL dangling pointers -> copy-as-chapter
shallow-copied rt-budgets + conversions (fields added after the
copier; emitter reads rt-budgets for CDX6010 budgets = latent
corruption). Deep-copy fix; post-fix precise count 0. Two-pass
bootstrap (rodata table change), converged one-pass on pass 2.
REMAINING (next stages, delegatable candidates): precise roots for
CHECK/LOWER outputs (conservative scan shows 121k/197k
false-positive-dominated hits, untriaged), then the copying
compactor the walker was built to drive; survey tightening;
TCO-reset removal.

**PROOF TOTALITY PROBED AND FIXED (blu, 2026-07-04, stages 0+1 in
one day).** The Chlipala audit leg: proof CONTENT checks were sound
(re-verified) but totality was unchecked — six routes each "proved"
a FALSE proposition silently (`bad = bad`, mutual defs, recursive
prop-returning helper, qed-sugar self-ref, induction step citing the
claim under proof as a lemma, mutual lemma citation). Damian's
ruling: fix the code to match the goal, no README hedging. Stage 1
shipped: `check-proof-cycles` in TypeChecker (Section: Proof
Acyclicity) — relevance from DEEP-RESOLVED checked types
(result.types, index-aligned with mod.defs; catches undeclared
intermediaries), exhaustive fuel-capped type walk incl. SumTy ctor
payloads/RecordTy fields, edges via collect-rt-mentions with
self="" (self-mentions ARE edges here), rt-reaches reused verbatim;
NEW AInductionExpr arm in collect-rt-mentions (was missing — the
punctual under-coverage lesson; without it the two lemma routes slip
the check). CDX4023 CircularProof (CdxCodes). is-proof-def
(X86_64.codex) generalized to any arity via is-proof-return (no
EffectfulTy arm — erasing an effectful prop-returning def would
delete its effects). Gates: one-pass hard fixed point FIRST build
(Sut === stage1, 0 non-sig byte diffs; selfhost has no proof defs so
the check early-outs), BVT 46/0, full battery + self-verify + seed
in the CL. Residual edges logged (ProofTotalityProbe.md 6.2):
cross-chapter DefMap (not constructible today), Option B grammar,
stale CDX4022 registry description (val's lane). Prior legs
spot-verified green same session. Capability stage 4 (OS wiring)
remains the one open Vision Check item — delegatable, kernel agent.

**CAPABILITY STAGE 3 SHIPPED (blu CL 6994 + seed FC795D76...,
docs 6996, 2026-07-04).** The emitter derives the capability manifest
from opening's registered type: effects section (wire format from the
verify quire) + covering-capability entries, appended after rodata
INSIDE the hashed+signed content (text stays at 224 for codex-vm's
fixed header skip; verifier reads through header offsets). Verifier
fixes in the same CL: content-hash range now [224 .. furthest section
end] (the old [cap-off..EOF] range included the unsigned MAP1 tail —
pre-existing mismatch self-verify never noticed); effect-has-capability
gains dotted-prefix coverage. Seed manifest decodes as caps [Console
rw, FileSystem rw] + effects [Console, FileSystem] — declaration-only
for the exempt TCB, checker-complete for enforced programs (the
threat surface). cap-launder-manifest RENAMED cap-manifest-derived
(positive guard). Two-pass -> one-pass converged; self-verify green
with the manifest under the signature. REMAINING: stage 4 only — OS
wiring (VerifiedLoader cdx-caps-to-bitmask already consumes the
manifest; replace grant-all, wire loader bitmask into the proc
table). Kernel-side agent work; specs CapabilityProbe.md sec 7.5.

**PUNCTUAL FIX SHIPPED (blu CL 6970 + seed, docs 6972, 2026-07-04).**
The three body walks (calls/no-alloc/no-lambda) now cover every
expression shape the cycle collector covers PLUS match-arm guards and
handler clause bodies (a gap even the cycle checker had — fixed in
the same CL; a punctual fn can discharge internal effects through a
handler, so clauses are reachable punctual code). Computed application
heads are rejected outright (CDX6001 — a punctual call must name its
callee). CDX6004 is an any-effect-row judgment (is-rt-unsafe-effect
deleted). Allowlist tightened: show/integer-to-text (allocate),
text-starts-with/text-compare (O(n)) dropped. PROBE CORRECTION:
list-length/list-at are emitter-proven O(1) (lists are
length-prefixed contiguous vectors) — they STAY; the stage-0 doc's
O(n) claim was wrong and is corrected. All four launder probes
flipped to errors/.failing; blast radius zero (punctual foreword +
Vga use only arithmetic/bit builtins). One-pass fixed point on first
build (checker-only), battery 308/293/0/15, self-verify green.

**CAPABILITY STAGE 2 SHIPPED (blu CL 6958 + docs 6963, 2026-07-04).**
Foreword device-effect annotation done: Fat32 + Gpt act-converted
with [Device.Block] on their closures; GpuRender + InputSource +
AppRunner declare [Device.Port]. Scope corrections vs the probe
estimate: KeyboardLayout is heap-only (pure by ruling, untouched);
InputSource was MISSING from the probe list and joined the scope
(mouse/keyboard ports), pulling AppRunner transitively. New trap
recorded in CapabilityProbe.md sec 7.5: CDX1070 — an act statement
may not start with a literal or `(` after an application statement
(only name/keyword-headed lines start statements); trailing `True`
needs the `<chapter>-done (last-call ...)` idiom. New gating test
foreword-apprunner (ui device closure had zero compile coverage). NO
seed change (modules outside the seed compile set). Remaining
capability stages: 3 (manifest derivation at emit) + 4 (OS wiring) —
OS-ish, delegatable per the ruling. blu moves to the PUNCTUAL FIX
campaign (PunctualProbe.md: unify the 4 rt-safety walks onto the
cycle-checker's exhaustive node coverage, any-effect-row CDX6004,
tighten the safe-builtin allowlist).

**CAPABILITY STAGE 1b SHIPPED (blu CL 6932 + seed, 2026-07-04).**
All six block-* intrinsics carry Device.Block (block-sector-count as
an EffectfulTy value — a bare mention performs); new read-mmio /
poke-mmio intrinsics carry Device.Mmio (TypeEnv + NameResolver +
X86_64Helpers byte stubs = the complete new-intrinsic wiring). KEY
AS-BUILT LESSON: enforced-module "annotation" is ACT-CONVERSION —
CDX2033 rejects an effectful let outside an act-bind regardless of
the declared row (the declaration only satisfies CDX2031); the legal
non-act positions are tail, when-scrutinee, and argument (all
probe-verified). Fat16 took 6 act conversions + 6 step helpers + 3
de-let rewrites; the act form is legal under both the old (pure
intrinsics) and new typing, which keeps the two-pass bootstrap green.
Two-pass -> rebuild -> one-pass; seed CF0FF221...; self-verify green.
Probes: errors/cap-launder-pure-block, errors/cap-launder-pure-mmio
(2031+2033); cap-launder-pure-poke RENAMED cap-heap-poke-pure
(positive ruled guard); cap-device-declared extended.
STAGE 2 REVISED: Fat32 (~74 sites) / Gpt (~52) are act-conversion
campaigns, not signature sweeps. NEXT: stage 2 (foreword
annotation), or punctual FIX campaign, per Damian's pick.
Cross-battery note (val): the x86 seed emits read-mmio/poke-mmio as
named runtime helpers; the RISC-V/ARM64 plugs map no port-*/mmio
intrinsics, so cap-device-declared may need a plug mapping or a
cross-skip if the cross battery compiles it.

**CAPABILITY STAGE 1a SHIPPED (blu CL 6923 + seed, 2026-07-03).**
The port-* intrinsics carry Device.Port; the owned hardware stack is
quire-exempt (chapter-slug prefix list in TypeChecker: slug-quire /
quire-effect-exempt / def-effect-exempt). Key as-built lesson: an
effectful intrinsic trips BOTH the row-subset check (CDX2031, def
boundary) and the effectful-let check (CDX2033, inside infer-expr on
`let w = port-out-byte ...`), so exemption needed a state carrier -
an `effect-exempt : Boolean` field on UnificationState, set per-def in
check-def-normal, read by both. cap-launder-pure-io flipped to
errors/.failing (2031+2033); cap-device-declared positive guard added.
One-pass fixed point, battery 306/291/0/15, self-verify green. NEXT
(stage 1b): Device.Block + Fat16 annotation (Fat16 is cited by the
compiler, so it is ENFORCED and must be annotated in the SAME CL or
the seed breaks) + poke-mmio/read-mmio intrinsics (Device.Mmio) +
reclassify cap-launder-pure-poke (heap poke stays pure). Then stage 2
(foreword annotation), 3 (manifest derivation - OS-ish), 4 (OS
wiring). Full spec: CapabilityProbe.md sec 7.
SCAR this session: hit the shelve-before-revert trap (plain `p4 shelve`
no-ops on an already-shelved CL - use -f, or skip the dance when the
workspace is already clean and build from disk). Recovered from context.

**PUNCTUAL + CAPABILITIES STAGE-0 PROBED (2026-07-03, docs CL 6895).**
Seven probes, all compile clean, all pinned as passing `.expected`
(`codex/test/punctual-launder-*`, `cap-launder-*`). Punctual: four of
the five checks (calls/heap/closures/bare-io) are AST walks over only
{Apply,If,Let,Binary,Match} - unary, act blocks, and computed call
heads launder; CDX6004 blocklists Console/FileSystem/Network by name
(a custom effect passes); the safe-builtin allowlist admits `show`
(allocs) and `list-length` (O(n)). The mutual-recursion cycle check is
the exhaustive-coverage exception the other four should copy. Findings:
PunctualProbe.md. Capabilities: the manifest is hardcoded empty
(`build-cdx` cap-sz=0), the raw I/O/memory/block intrinsics are typed
with an EMPTY effect row (a pure-signature function does unmediated
port/poke/block I/O), and boot grants process 0 all caps - the
verifier's cap/effect phases are vacuous on every binary it produces.
Findings: CapabilityProbe.md. Highest-value fix (capability leg):
effect rows on the intrinsics in TypeEnv.codex:243-250 - a compiler-
only change that closes both pure-I/O probes.

**Vision-check scorecard:** effects (EffectRows, done), bounded ints
(BoundedSignatures, done), linear (LinearOwnership stages 0-4, DONE -
all nine routes enforced), capabilities (probed, gaps documented),
punctual (probed, gaps documented). Every by-construction claim now
has an adversarial probe; ClaimsCalibration carries the honest scope
for the two un-enforced legs.

**STAGE 4 SHIPPED (CL 6883 + seed 6886, digest 47CABCEA...,
2026-07-03) — the campaign is done, all nine routes enforced.** One retain rule serves the last
three: a let-bind whose value retains the owner (capturing lambda,
partial application by registered arity, container literal stashing
the bare name) moves ownership into the binding, held to
exactly-once — closures are call-once (CDX2061 on the second call),
containers are consume-once (`list-at xs 0` on a linear-holding
list is CDX2065 through the plain boundary; `b.h + b.h` is
CDX2061). New CDX2067 LinearCapture for run-many contexts: handler
clauses (the ruled rule, enforced) and closures escaping as
arguments; a capturing lambda in tail position takes the return
obligation (CDX2066 unless the return is declared linear). Guards
added: linear-capture-once (positive), errors/linear-capture-arg,
errors/linear-capture-clause. Battery 290/275/0/15 (287 + 3 new).
Residual documented edges in LinearOwnership.md stage 4 notes:
call-minted locals, container literals in arg/tail position,
effectful linear returns, move-site spans.

**STAGE 3 SHIPPED (CL 6868 + seed 6871, digest F5A68CBD...,
2026-07-03):** boundaries. A
linear value moves into a callee only through a parameter declared
linear, read off the REGISTERED signature (the wrapper stage 1 left
there) — freeze is the sanctioned door by its own `linear a` param,
no call-site special case; violations are CDX2065 LinearEscape
(names the callee). A bare linear return requires a linear-declared
return type — CDX2066 LinearReturn; the freeze identity is the one
exempt door; ownership chains keep the tail obligation (`let h = n
in h` under a plain return still fires). Closed AND flipped:
boundary (2065), return (2066), and partial (2065 — the probe's
partial application hands the linear to a plain first param, so the
capture route runs through the boundary). Three routes remain:
closure, list, record (stage 4 capture/containers, where the ruled
handler-clause-capture error also lands). One-pass fixed point on
first build, battery 287/272/0/15, self-verify green.

**STAGE 2 SHIPPED (CL 6856 + seed 6860, digest 351FF9CE...,
2026-07-03):** local ownership
flow. A let-bind whose value is a bare mention of a tracked owner
(declared linear param or mutable-record param) is a MOVE: the walk
re-roots on the new name, which inherits the exactly-once
obligation; every residual mention of the old name is dead
(CDX2061/CDX2062 "moved to a new owner ... the original name is
dead"). The mutable consume walk converted from Integer to the
shared LinResult record — one ownership walk now serves both
disciplines (the ruling's verdict-4 unification). Closes and flips
local-leak (CDX2063), local-dup (CDX2061), mutable-launder-alias
(CDX2062) to errors/.failing. Gates: one-pass hard fixed point on
first build, battery 287/272/0/15 (three probes now green in the
failing direction), self-verify green. Six routes remain open
(closure, partial, list, record, boundary, return) — stages 3-4.
Session also: merge-down 6850 (val RISC-V plug fix, verified via
plug rebuild + Renode vector-basic run) and CL 6851 (five __narrow
at CDX2051 stores in plugs/common/PlugTypes.codex + RiscVCodeGen —
the plug surface was never swept; blocked all plug rebuilds under
the CDX2051-error seed).

NEXT: the remaining vision-check legs — capabilities or punctual
stage-0 (adversarial probes, EffectRows pattern) — or the residual
linear edges if Damian wants totality before moving on.

## Previous: Linear leg stages 0-1.

Damian's directive (2026-07-03): delegatable work goes on
`docs/QuartermastersMap.md` (shipped, main 6817 — 14 scoped digs for
the crew); blu works the held-back, high-risk features. Pick: the
BACKLOG's highest-priority item, adversarial verification of the
by-construction claims, starting with linear types.

**Linear stage 0 (CL 6819):** nine probes, ALL open — linearity
enforcement is param-mention-only; let-alias, closure, partial app,
list/record stash, call boundary, plain return, and mutable alias
all launder silently. `linear` is ERASED at type resolution
(resolve-type-expr unwraps ALinearType), so boundaries cannot
enforce it. Probes pin the permissive behavior as .expected tests
(flip to .failing per closed route — catalog green BOTH directions
is the ship gate). Findings + candidate 4-stage campaign (represent
-> local flow -> boundaries -> capture/containers):
`docs/Designs/Compiler/Active/LinearOwnership.md`. DevelopersGuide
carries the honest scope note; ClaimsCalibration registers the fix.
RULED (Damian, 2026-07-03): ownership-move semantics, Rust-like but
kept linear (drop stays an error). Decision rule was "multiplicity
if better, Rust-like if same or better"; the literature verdict
(doc section 6) found multiplicity fails the existing uniqueness
promise (Linear.codex's freeze-without-copy / in-place-update
prose), so moves win — multiplicity-polymorphic HOFs and borrows
deferred with explicit revisit triggers.

**STAGE 1 SHIPPED (CL 6840 + seed 6841, digest 4A4B42DB...):**
LinearTy survives resolution, inert by construction — unify strips
at entry, bind-def-params binds params stripped, IR wire unchanged
(no plug rebuilds). One-pass hard fixed point on the first build,
battery 287/272/0/15 (+linear-poly-freeze guard), self-verify
green. As-built deviation: the mutable flag deferred to stage 2
(compiler source itself declares mutable records — flagging now
would forfeit inertness; no reader exists yet). Full as-built notes
in LinearOwnership.md section 5.

(Stage 2 shipped — see the top section.)

## Previous: BoundedSignatures campaign + follow-ups COMPLETE and on main.

blu == main (copy-up 6797, merge-downs current through fester's
BACnet Write at 6800/6801 — verified clean under the promoted seed).
Workspace clean, nothing shelved. Seed digest E1E4A10B85F8A3B8...
Next stream is Damian's pick: the per-chapter constructor-contract
sweeps (rule below), the BACKLOG vision-check probes, the GPU Globe
PTX ABI blocker, or emit-side propagation. Standing step for every
future merge-down: verify each incoming chapter compiles under the
promoted seed (CDX2051 is an error now; fester's CoAP needed one
__narrow at 6759, everything since has arrived clean).

All stages shipped and on main. Session CL chain: 6682 (checker
arithmetic arms + __heap-save fact), 6691 (19 length/position
signatures + 5 __narrow), 6693 (seed), 6697 (copy-up), 6711
(register/slot family), 6721 (diagnostics chain + TypeVar narrows),
6732 (TEXT emitter printed bounded signatures - real bug found by
the promotion gate), 6745 (promotion + ~70-site library sweep across
21 foreword/kernel files + test migration), 6747 (CdxBinary verify
tail), 6748 (final seed, digest D8E87D8A...). Battery 261/246/0/15;
self-verify green. Full as-built record in BoundedSignatures.md.

**Follow-ups: 1-2 DONE, 3 PILOTED (same day, CLs 6783/6787/6790,
seed 6791 digest E1E4A10B...):**
1. DONE - Checker prover reach (CL 6783): declared-return consult
   (sound behind stage-B postcondition guards), type-derived local
   ranges (locals join the arithmetic arms), text-length structural
   fact. Test return-narrow-proven pins all three doors. The
   field-access arm was NOT built: the AST does not carry receiver
   types and re-inferring in the lint would mutate checker state -
   the emit prover (which has typed IR) remains the field-heavy
   prover.
2. DONE - EmitResult.reg Location remodel (CL 6787): reg declares
   0..65535 matching slot/ptr-loc. Cascade measured at ZERO (the
   ~140 .reg-read constructions type-fit, constants prove, allocator
   sites already narrowed); two family-2 narrows removed
   (emit-bivy-alloc, emit-eval-record-fields). Field storage 4 -> 2
   bytes, internal and name-addressed.
3. PILOTED - constructor param contracts (CL 6790): adsr-new
   declares sustain 0..1000, __narrow off the store, all callers
   prove. CAMPAIGN RULE established: convert a constructor only when
   its ENTIRE caller set (apps included) proves or is swept in the
   same CL. The 30-site app survey (GopRender 14, CvmmTheme 6, ...)
   shows compositor-new/widget-custom-class constructors receive
   computed values - those chapters need their app sweep in the same
   CL. Remaining chapters queued as an on-demand stream.
4. NEXT-STREAM CANDIDATES: BACKLOG vision-check probe streams; GPU
   Globe PTX ABI blocker; per-chapter constructor sweeps (rule
   above).

Damian's directive: build bounded integers at the function boundary
"the way that would survive the most rigorous doctoral dissertation
panel." Design doc (theory + metatheory + literature + staged plan):
`docs/Designs/Compiler/Active/BoundedSignatures.md`. This is the
successor to the bounds-prover reach campaign (slices 1-3 below, which
took selfhost CDX2051 66 -> 45 lint-side and established that the
remaining plurality is parameter pass-through no lint could discharge).

KEY REFRAME (verified empirically): bounded ints ALREADY parse in
signatures (the "standing trap" was stale — EffectRows' FunTy rework
fixed it). The real gap: the param/return boundary was COSMETIC —
`inc-byte 300` (300 into `Integer between 0 and 255`) compiled clean,
returned 301, no trap/warning, while the same literal into a bounded
FIELD is CDX2050 + trap. The feature = extend the existing hybrid
static/dynamic refinement enforcement to the one position (the
boundary) that was skipped. Uniform with fields, not special-cased.

**Stage A — static (blu 6644 + seed 6645, main 6646, digest
D1BBC2EB):** the narrowing lint runs at arg->param
(lint-arg-narrowing in infer-application) and body->return
(lint-return-narrowing in check-def-normal). CDX2050 error on
out-of-range literal, CDX2051 advisory, CDX2053 proven. Type-checker
only, one-pass. Tests bounded-sig-static, errors/bounded-param-literal.

**Stage B — runtime (blu 6653 + seed 6656, main 6657, digest
17538A18):** Eiffel/DbC callee-side guards (Damian's pick over
call-site). emit-param-guards (prologue, both function paths, reads
arg-regs before bind-params) + emit-return-guard (before epilogue,
elided when value-fits-field proves the body), reusing
emit-error-bound-trap. Out-of-range arg/result trap UD2 (verified
EXC=06). GOTCHA fixed: the leaf INLINER bypassed callee-side guards
(inc-byte inlined into caller); has-bounded-boundary now excludes
bounded-boundary functions from inline candidates. One-pass (guards
inert for self-compile), battery 254/239/0/15. Tests bounded-sig-
runtime (.expected), bounded-param-trap + bounded-return-trap (.fatal).

**Stage C1 — propagation + first adoption (blu 6663 + seed 6665, main
6666, digest 13493455):** bind-def-params records bounded param ranges
into local-ranges (sound behind B's guards); mk-cdx adopts code
0..65535 / severity 0..3 / phase 0..15 (registry-only, no cascade).
Selfhost CDX2051 -> 39. Two defects fixed: (1) ctor/param lint DEDUP
(merged lint-ctor-narrowing + lint-param-narrowing into one
lint-arg-narrowing), (2) local-ranges per-def LEAK (check-all-defs
reset only locals; false CDX2053 across same-named params; also closed
a latent slice-3 leak). One-pass, battery 254/239/0/15.

### C2 family 1 (length/position) — DONE (blu 6682 + 6691, seed 6693)

Two gated CLs. CL 6682 (checker-only, one-pass): aexpr-proven-range
ABinaryExpr arm (interval add/sub/mul/div, emit-prover guards
mirrored) + __heap-save builtin fact; test arith-narrow-proven pins
all four arms both directions; 42 -> 39. CL 6691 (adoption,
one-pass — source-level, the seed already emits stage-B guards): 19
signatures bounded (span chain, lexer fid/length/offset chain,
skip-newlines-pos, codegen-carry-forward, init-emit-workspace,
cumsum-widths, accumulate-offset-width-sort, assign-effect-op-addrs,
pitch, gen-unique-name-loop) + 5 __narrow assertion sites
(make-i32-patch, emit-isr-stubs, build-x86-arities, emit-record,
install-new-node); 39 -> 16. Battery 255/240/0/15 (the +1 is the new
test). Timing within noise. Full as-built notes in
BoundedSignatures.md C2 section, including the left-operand type
rule and let-local type flow that make cascade analysis cheap.

### C2 families 2-3 — NEXT (then promote)

Residual 16 CDX2051. Adopt inward-out, one family per gated CL:

1. **Register family** (`0..31`, biggest residual: 9 sites — reg
   fields at alloc-temp/alloc-local/load-local/emit-reg-right-inplace,
   slot at add-local/emit-eval-record-fields, ptr-loc at
   emit-bivy-alloc) — HIGHER RISK: must verify EVERY value flowing
   into a `reg`/`slot` param genuinely fits (or the callee-entry
   guard traps mid-emit). Read the allocator before bounding. NOTE:
   these are the hottest functions in the emitter; the stage-B
   inliner exclusion applies — measure cdx-build.
2. **Diagnostics chain** (3 sites: make-diagnostic code+severity,
   make-error-related code) — CASCADE: bound make-error/warning/info
   code param (0..65535) AND make-diagnostic together, inward-out.
3. **TypeVar payloads** (4 sites: normalize-type x2,
   parameterize-walk x2) — tvar-map-lookup returns -1 on miss, so a
   0..2^32-1 return bound is dishonest; the sites are structurally
   non-miss. __narrow at the sites, or split lookup into a miss-free
   shape. Decide when reached.
4. **Endgame**: at CDX2051 == 0, promote CDX2051 warning -> error
   (like CDX9002 got), so a future unbounded->bounded flow fails the
   build.

WATCH: the Stage B inliner exclusion means bounded hot signatures do
NOT inline — measure self-compile time (cdx-build phase) as hot paths
get bounded; a real regression motivates the deferred CALL-SITE
precondition elision (design doc Stage B, currently callee-entry).
Emit-side propagation (ir-expr-proven-range trusting param bounds to
elide the downstream field runtime check) is a separate deferred
zero-cost optimization. Verbosity: each mk-cdx registry call now emits
~3 CDX2053 "proven" infos (~200/compile) — correct but noisy; consider
suppressing the trivial constant-fits info.

PROCESS SCARS: (a) ALWAYS `p4 shelve` BEFORE `p4 revert` — reverting
first discards the on-disk edit and unshelve restores the pre-fix
version (lost the C1 leak fix once this way; caught via Sut hash
mismatch). (b) Codegen changes here are ONE-PASS because no compiler
signature is bounded yet, so the guard code is inert for self-compile;
this changes the moment a hot signature IS bounded and the compiler
starts guarding its own calls — re-verify one-pass at each C2 family.

---

## Historical: Bounds-Prover Reach campaign (slices 1-3, on main)

**Slice 1 SHIPPED (blu 6611 + seed 6612, main 6615, digest
076335E0...):** literal-defined constants prove their ranges at
bounded stores. The narrowing lint gained `aexpr-proven-range`
(checker-side twin of the emit prover's `ir-expr-proven-range`),
consulted only on the would-warn path: literals, constant name refs
via a `TypeEnv.const-ranges` side table filled at registration,
if-union, let-body. Proven stores report new info CDX2053
(NarrowingProven). Selfhost 66 -> 53 (the reg-rax/reg-rdx/sev-error
family). Test const-narrow-proven.

**Slice 2 SHIPPED (blu 6625 + seed 6626, main 6628, digest
073D0D54...):** builtin return ranges. `aexpr-proven-range` gained an
AApplyExpr arm: `__narrow x` passes its argument's range through
(mirrors the emit prover), and a head name resolving to a
structurally bounded builtin proves its return - `list-length` and
`__deck-pos` are 0..2^32-1 (heap-backed in the 3 GB-RAM design),
guarded by env-is-local against shadowed heads. Selfhost 53 -> 48
(four `code-len/data-len = list-length` sites + `deck-origin =
__deck-pos`). Test builtin-narrow-proven pins both directions. Gates:
one-pass fixed point, full battery 248/235/0/13, seed self-verify
green. Note: one battery flake seen (effect-map-effctx output
capture) - the binary runs correct output individually; a clean
re-run confirmed 248/235/0/13.

**Key architecture fact (do not re-litigate):** registering constants
AS `IntegerTy n n` is unsound — infer-arithmetic/infer-comparison
unify operand types and list literals unify elements, so two distinct
singleton constants would be a disjoint-range CDX2001. The range
table beside the types, read only by the lint, is the ruled shape.

### Next step (needs Damian's decision)

**Bounded integers in function signatures.** The dominant remaining
bucket (~24 of 45) is a parameter bounded lo..hi in reality but typed
plain Integer because `Integer between L and H` does not parse in a
param/return position today (standing trap). Letting it parse there
clears the whole bucket AND is a genuine language capability (bounded
sensor params for the IoT/safety story). Open design questions to
settle first: (1) does a bounded param get a runtime narrowing check
on entry, or is it a contract the caller must satisfy? (2) how does a
bounded return interact with the callee's body range? (3) codegen:
params are flat 8-byte slots today, so this is contract-level at
first (like the TypeVar-id 32-bit bounding was) unless entry checks
are emitted. This is a parser + resolver + type-checker change,
design-worthy, not a mechanical slice.

Alternatives if the parser feature is deferred: interprocedural
argument-range join (whole-program, heavier), or list-element bounds
(`arity = list-length (d.params)` needs list-length proven <= 255,
which it structurally is not - a covariance/element-bound feature).

**Deferred old slice numbering (superseded):**
- Slice 4 (field source-bounding) is BLOCKED on parameter bounds, see
  status above - do not attempt before them.
- Slice 5 (parameter bounds) is now THE next step, above.
   bounded ints in function signatures (parser feature) or
   interprocedural argument join. Decide shape with Damian.

## Other Streams

- **BACKLOG adversarial-probe streams** (Fulfill the Vision Check):
  linear types, bounded integers, capabilities, punctual — the
  EffectRows stage-0 probe pattern applied to the other headline
  claims.
- **GPU Globe** (background, paused 2026-06-27): PTX function-call
  ABI fix is the single blocker for UV-mapped earth; shelved CL 6166
  has the app files.

---

## Historical: EffectRows stream (COMPLETE, on main via 6574 + 6598)

EffectRows stages 0-4 (design rigor, probes, representation, row
unification, the ruled ambient-row architecture, migration +
enforcement, cleanup), the dense row-substitution table, three
treasure-map claims (8: EffectfulTy parameterization, 9: for-sugar
parens in the printer, 11: variant-construction narrowing lint), two
review passes over fester's packing/widening work, and four
merge-downs + two copy-ups. Battery baseline at stream close: 246
total / 233 pass / 0 fail / 13 skip.

EffectRows deferred odds (none blocking, land when a real program
needs them): argument-boundary dotted widening, handler dotted
discharge, CDX2093 row-escapes-to-codegen assertion, unsolved-tail
defaulting pass. PatchEntry.value could carry its truthful
0..4294967295 bound (nit from the de-widening review, fester's call).

Design doc (authoritative plan + as-built notes):
`docs/Designs/Compiler/Active/EffectRows.md`. Deferred finds:
`docs/FabledTreasureMap.md`. All work on //Codex/blu, all submitted,
zero open files, all gates green at every CL.

### Completed 2026-07-02 (CLs 6507-6517)

- **CL 6507** — EffectRows.md rigor revision: ACI1 set-row theory with
  principality lemma, subsumption via Koka-style open instantiation +
  directed row-le (polarity-threaded unify REJECTED), handler typing
  rule, IR wire-format constraint, probe catalog, Rule-8 verdict.
- **CL 6508** — checker fix: act-bind strips EffectfulTy through
  resolution (`r <- effectful-app` then use of r falsely tripped
  CDX2031).
- **CL 6509** — checker fix: find-dot compared ASCII 46 ('H' in CCE);
  the dotted sub-effect lattice (Console.Write under Console) had
  never worked. Now expressed as `char-code (char-at "." 0)`.
- **CL 6510** — seed rebuild for the two fixes (digest 7928F8FD…).
- **CL 6511** — stage 0: 12 adversarial probes. OPEN laundering routes
  confirmed at runtime (list-map, record field, lazy, handler clause;
  fork at type level) land as passing tests that flip to .failing at
  stage 3; policed routes (direct/partial CDX2031, let CDX2033,
  instance body, dotted deny) land in errors/. Depot sweep for
  lowercase-in-bracket effect names: zero hits.
- **CL 6512** — stage 1a: `FunTy (CodexType) (EffectRow) (CodexType)`,
  EffectRow = canonical label set + tail-name, interned empty-row.
  199 sites hand-edited across 15 files (regex for candidate
  selection ONLY — standing Damian rule). One-pass fixed point on the
  first build.
- **CL 6513** — stage 1b: `[e]` / `[Console, e]` row-variable syntax,
  CDX1120/1121 diagnostics, printer merges tail into the result
  bracket, TEXT round-trip byte-stable. tail-id field REMOVED per
  Damian review (write-only in stage 1; stage 2 owns ids). IR wire
  format unchanged for existing programs — no plug rebuilds.
- **CL 6514** — seed rebuild (digest 2F7B68C5EEABBF8D).
- **CL 6515** — stage-1b tests. Battery: 241 total / 228 pass / 0 fail.
- **CL 6516** — TypeVar/ForAllTy payloads bounded 0..2^32-1 (Damian
  ruling: type ids are 32-bit). Contract-level today — sum-ctor
  fields are flat 8-byte slots (`emit-sum-ctor`), so no layout change
  until ctor field packing lands (treasure map #1). Cleared the
  var-id CDX2051 silent-truncation warnings.
- **CL 6517** — `docs/FabledTreasureMap.md` seeded with 7 deferred
  wins found this session.

### Completed 2026-07-02 (stage 2, CL 6532 + seed CL 6533)

All four worklist items landed as one gated CL. Gates: hard fixed
point in one pass (SUT === stage1), text round-trip byte-stable,
semantic equivalence, BVT, full battery 241/228/0 — identical to the
CL 6515 baseline. The -FW forewords sweep shows 34 failures that are
byte-identical under the baseline seed (same count, same names;
engine-terrain's CDX2001s verified three ways: new SUT, seed, and
seed + depot ListUtils — all identical, so pre-existing on this
stream, not stage 2's; cce-tier2's "3-byte FAIL" likewise pre-exists
and is fester's tier-2 CCE stream). As predicted for an inert stage,
zero behavior change — every solved row is the empty row until
stage 3.

1. **Row-variable ids** — `parameterize-row` in the parameterize
   walk assigns ids to tail-names via the shared 32-bit counter;
   `ParamEntry` gained an `is-row` kind flag (one lowercase letter
   can be a type var AND a row tail in one signature — kinds are
   matched, not just names). `ForAllEff` binder added — APPENDED as
   the last CodexType ctor (ordinals are load-bearing in emission;
   the ctor list is append-only). `instantiate-type` freshens it via
   `subst-row-var`. EffectRow regained `tail-id` (now read).
2. **unify-row** — Unifier.codex, per §5 exactly: canonical
   make-row, one-merge labels-minus, four tail cases, tail-chain
   occurs (CDX2090/2091 minted). `unify-fun` unifies rows between
   params and results. Rows solve in `row-substitutions` (assoc
   list) on UnificationState; GADT snapshot/restore covers BOTH
   tables (SubstSnapshot).
3. **HOF retyping** — TypeEnv map/par/race/fork (fork charges the
   spawner); compiler Collections map/fold family (`for` desugars to
   map-list, so every for loop in the selfhost exercises row
   instantiation + unify-row — the fixed point is the proof);
   foreword ListUtils map/fold/filter family. Iterate/Pipeline/Sort
   deferred to stage 3.
4. **Migration DEFERRED to stage 3** (as-built note in doc §12): all
   effect readers (extract-effects, effect-le, act-union) read
   EffectfulTy; migrating concretes before the readers are row-aware
   would drain the point checks and regress the stage-0 policed
   probes. The §7 handler rule moves with it — under the split a
   body's row is empty, so the rule would enforce nothing.

New traps learned in stage 2:

- Bounded integers do NOT parse in function signatures (params,
  returns, tuple payloads) — record fields and ctor payloads only.
  CDX1000/1072 at the `between`.
- A negative literal in record-field position is a negate
  APPLICATION at AST level, not ALitExpr — `tail-id = -1` trips the
  CDX2051 lint (full-range value type); `__narrow (-1)` is the
  suppression idiom (is-narrow-call, lint-narrowing-check).
- build.ps1 archives build/output by RENAME — any open handle into
  that directory (a tee'd log) makes the whole build die with
  Access denied. Log outside build/output.
- Two treasure-map finds: parameterize-walk-children has no
  EffectfulTy arm (type vars under effectful VALUES never
  parameterize — latent monomorphization trap), and
  emit-const-codextype still boxes FunTy as two words (pre-row
  shape; consumer unknown). FabledTreasureMap.md #7/#8.

### Next: Stage 3 (open instantiation + directed row-le — the hard one)

**RULED (Damian, 2026-07-02): option (a) — ambient row threading.
CheckResult gains an effect-row field.** The full spec — row-union
with tail-equating (Koka shared-ε), application capture via fresh
row variable, lambda/lazy arrows carrying the body ambient, the
CL 3a (inert calculus) / CL 3b (migration + enforcement) split with
the inertness gate argument — is in EffectRows.md §12 stage-3
as-ruled note.

**CL 3a SHIPPED (CL 6550 + seed CL 6551, digest 968E099D…).** The
judgment is Γ ⊢ e : τ | ε end to end: effect-row on CheckResult,
row-union, per-arm unions in every inference arm, application
capture ({t} on the synthesized arrow, t joins the ambient), lambda
and lazy arrows carry the body ambient (the lazy laundering probe
dies by construction at 3b), undeclared defs carry a fresh
performing-arrow row unified with the body ambient (last-arrow-row
in check-def-normal — callers see an undeclared def's effects
through resolution in any check order). LetBindResult stayed a
2-field pair for the registration walkers; let-inference got its own
LetInferResult with binds-row. Gates: one-pass hard fixed point,
battery 243/230/0 == baseline, cdx-build 21.1s vs 20.9s baseline
(~1% for a row var per application — the dense table earns its
keep). Handler arm passes the body ambient through unchanged — the
§7 subtraction is 3b's, so 3a over-approximates there, the sound
direction.

**CL 3b SHIPPED — the KingsAndCourts claim is now enforced.**
Concretes migrated onto arrow rows (resolve-type-expr, TypeEnv
print/process builtins; read-line-family values keep EffectfulTy),
infer-name opens referenced spine rows (§6a) and surfaces
effectful-VALUE effects into the ambient, lambda/lazy rows open
when closed, CDX2033 reads ambient labels, the def boundary is
check-effect-row-subset (CDX2031, lattice via effect-covered-by),
effect-le DELETED (row unification does its job — CDX2090),
unify-at stripping arms deleted (EffectfulTy unifies structurally,
set-equal names), §7 handler discharge via unification against
{E | t'} with clause-body inference (CDX2092 minted), punctual
CDX6004 + opening capabilities read rows via collect-effect-names,
Iterate retyped [e] (Sort/Pipeline deliberately strict — effectful
comparators under reordering are refused by design). Probes: the
five open laundering routes flipped to .failing (map/lazy 2031,
record/fork 2090, handler-clause 2031+2033); launder-hof/partial
sidecars moved 2031→2090 (mechanism now unification). Gates: hard
fixed point one pass, battery 243/230/0, catalog green BOTH
directions. ZERO under-declarations in compiler source — the
budgeted triage was unnecessary. Two printer findings: the
CodexType printer now renders row labels (emit-row-result knew only
tails), and for-expressions lose parens in operand position
(treasure map #9; sidestepped with an explicit loop).

**Stage 4 SHIPPED (CL 6560 + seed): the stream is COMPLETE.** All
transitional flat-effect forms deleted (act name-union machinery,
handler env/type stripping, act-bind EffectfulTy arm,
extract-effects/extract-scopes); CDX2094 rejects an effect bracket
wrapping a function type in a value signature. Gates + battery
green.

**Post-stream follow-ups shipped (CLs 6581/6582, 6586/6587):**
treasure map #8 (EffectfulTy parameterization hole closed, test
effect-value-poly), #9 (for-sugar parenthesized in operand position,
re-proven by the fixed point via collect-effect-names), #11 (variant
construction narrowing lint, CDX2050/2051 at ctor-headed
applications — the ruling's direction: more checking, bounds are
contracts). Battery baseline now 246/233/0. Copy-up of these HELD
for Damian's merge signal while fester restores the widened bounds.

**EffectRows: DONE.** The remaining deferred odds, none blocking,
land when a real program needs them: argument-boundary dotted
widening (unification is exact; the lattice lives at the def
boundary), dotted discharge in handlers (exact-name membership,
matching the retired filter-effect's exactness), CDX2093
row-escapes-to-codegen assertion (nothing reads rows at codegen),
unsolved-tail defaulting pass (row-le treats unbound tails as
empty). Next stream decision is Damian's: copy-up to main, then
either the BACKLOG's remaining adversarial-probe streams (linear
types, bounded integers, capabilities, punctual) or back to the
GPU Globe PTX ABI blocker.

Also settled this session in prep: the row-substitution table is now
dense with a dedicated counter (CL 6538) — per-application row vars
will not go quadratic.

Per EffectRows.md §12 stage 3 + the deferred stage-2 items:

1. §6(a) opening in `instantiate-type` (never at binding positions;
   mutable fields invariant), fresh tails for lambda literals,
   unsolved tails default to {} at the def boundary.
2. §6(b) directed `row-le` at the three boundaries; REMOVE
   effect-le's type-variable leniency; delete the unify-at
   EffectfulTy stripping arms.
3. EffectfulTy→row migration of concretes (resolve-type-expr AFunType
   arm, TypeEnv print-line et al.) + make every effect reader
   row-aware (extract-effects, act union, def boundary as row-le).
4. §7 handler rule (body row unified against {E, t'}; clause rows
   unioned; CDX2092).
5. Retype Iterate/Pipeline/Sort comparators; 16-label
   assert-and-log; flip the stage-0 laundering probes from .expected
   to .failing (CDX2031/2090) — the catalog green in BOTH directions
   is the ship gate.
6. Remember: application sites synthesize `FunTy arg {} ret` with an
   EMPTY row (infer-application) — stage 3 must mint a fresh open
   row there or every application closes its callee's row; same for
   the def-boundary tie and build-undeclared-fun-type.

### Standing traps (hard-won, do not rediscover)

- **effect-le's HOF leniency is an artifact of shared mutated
  substitution state**: infer-application's unify runs before
  effect-le but add-subst mutates the shared substs store in place,
  so effect-le sees post-unify bindings and compares the effectful
  type against itself. Stage 3's row-le must capture the declared
  param row BEFORE the application unify solves it.
- compile.ps1's kernel `build-output/bare-metal/Codex.cdx` goes STALE
  after syncs and test.ps1 -CodexCdx runs overwrite it; copy
  seed/Codex.cdx over it before probing or results lie.
- `effect X where` blocks swallow following defs unless a `Section:`
  header terminates them. Def-level handlers parse via
  `let r = with E body <clauses> in r`. Act-block `let` needs act-bind.
- parse-effect-names returns a 4-tuple; ParserExpressions'
  with-timeout parser is the easy-to-miss second caller.

## Background Stream: GPU Globe App (paused, unchanged since 2026-06-27)

Pipeline proven: Codex → PTX plug → CUDA JIT → RTX 4060 Ti renders
textured earth. Shelved CL 6166 has the app files. Next steps in
priority order: PTX function-call ABI fix (%lv_ registers corrupted
across .func calls — the only blocker for UV-mapped earth), dead-code
elimination of kernel- prefixed defs in IR mode, stub emission for
recognized intrinsics, black hole scene, Sosaria texture. PTX plug
f64 type system is complete (CLs 6152/6156/6163).
