# Demand-Paged Arena — retiring the survey

**Status:** Design, ready for a fresh implementation session.
**Author:** blu, 2026-07-05.
**Supersedes (on success):** the survey multipliers, `check-deck-overflow`
/ `CDX9002`, DynamicSurvey retry, the reservation-copy dance, AND the
two stop-gaps discussed but NOT built this session (the prologue
ceiling-check and the MMU guard page). Read those in context below.

---

## 1. The problem this kills

Today the bare-metal compiler bump-allocates over a **flat, fully
identity-mapped arena** `[6 MB .. ram-size]`. Every phase (LEX, PARSE,
DESUGAR, SCOPE, CHECK, LOWER, RESOLVE, LIFT, INLINE, EMIT) pre-reserves
a *fixed-size deck* whose size is guessed by a **survey formula**
(`source-len * survey-<phase>-mul + ... ) * headroom` in
`BuildSettings.codex`, consumed in `opening.codex`). That single design
choice generates every memory pathology we have:

- **Over-reservation waste.** CHECK reserves `S * check-mul * 1.2`.
  Measured 2026-07-05: at `check-mul=200` the CHECK bivy-hwm peaks at
  **978 MB while CHECK actually touches a flat 156 MB** — a ~6x
  over-commit that sets the whole compile's peak. Every phase does this
  to some degree.
- **The overflow minefield.** Reservations are *advisory*, not walls.
  `__heap-advance` bumps R10 with zero bound checks; the only runtime
  guard is the prologue `cmp rsp, r10` (stack collision at ~3 GB,
  useless for deck-vs-scratch). Overflow detection (`CDX9002`) is
  **post-hoc** at `phase-measure`, i.e. AFTER the deck already wrote
  past its reservation into adjacent live data. Result: lowering a
  multiplier is non-monotonic Russian roulette. Measured: `check-mul`
  40 clean, 25 **hard-faults** (silent over-run corrupts, then a live
  read faults), 15 cleanly overflows→retries, 10 corrupts to spurious
  type errors. The fat default hides a latent corruption band.
- **Retained "dead middle."** RESOLVE/LIFT copy the IR out of the LOWER
  deck and sever it, but nothing rewinds R10 below them, so the dead
  LOWER deck stays resident under the live ones. The whole
  retained-heap campaign (429→287 MB etc.) is fighting this by hand
  with reservation-copy + hash-consing + memo copiers.

**Root cause:** we pre-reserve fixed regions from a guessed size, on a
substrate that maps everything up front. The modern answer is to stop
doing both: **reserve address space, commit physical pages on demand.**

---

## 2. The idea (industry-standard, see §9 references)

Reserve a large virtual address range; leave it **not-present**. Bump
the allocation pointer freely. When code first *touches* a page, the
CPU raises **#PF (vector 14)**; the handler maps a frame there and
resumes the faulting instruction. Consequences:

- **The survey disappears.** No `check-mul`, no `lower-mul`, no
  estimates, no `headroom`. You cannot under-reserve something you do
  not reserve. Decks grow by being written.
- **Overflow becomes impossible.** There is no ceiling to cross. The
  arena grows until *physical frames* run out — a single, honest OOM,
  detected cleanly at the one place a frame is handed out.
- **Peak = touched, not reserved.** CHECK commits ~156 MB, not 978 MB.
  The survey over-commit stops costing memory entirely.
- **Compact returns frames.** `phase-compact` decommits the reclaimed
  range (unmap + free frames), which frees *physical* RAM instead of
  only lowering an address. The "dead middle" cannot exist.

This is `VirtualAlloc(MEM_RESERVE)` + lazy commit / Linux demand-zero /
Fleury's reserve-big-commit-on-touch arena, applied to a bare-metal
identity-ish substrate.

---

## 3. Current system — the exact parts we touch

Grounding for the impl session (all verified 2026-07-05):

- **Page tables** (`X86_64Boot.codex`): `emit-build-process-page-tables`
  → `emit-fill-pds` fills PD entries as **2 MB pages**, each
  `phys_start | 0x83` (present|write|PS), NX variant sets bit 63. PML4
  at `pml4-addr` (0x8000), PDPT at +4096, PDs at +8192.
  `bare-metal-pd-count` PDs cover `ram-size`. cr3 loaded in
  `emit-process-setup`. **This is where we stop pre-filling the heap
  PDs present.**
- **IDT / exceptions** (`X86_64Boot.codex`): `emit-common-interrupt-handler`
  (`__interrupt_common`) reads the vector into rax (`movzx`), `cmp 32`,
  and `jb` → CPU-exception path (`emit-cpu-exception-dump`, which prints
  `!EXC`/CR2/regs and halts). **Vector 14 = #PF. We intercept it here:
  read CR2, decide grow-vs-real, map+IRETQ or fall through to the
  dump.** CR2 is already read/printed in the dump (`0f 20 d0` = `mov
  rax,cr2`); the CPU pushes a #PF error code we must account for on the
  stack frame.
- **Allocator** (`PhaseAllocator.codex`): `build(size)` = save R10,
  `__deck-set` R10, `__heap-advance size`. `pitch/strike` = bivy
  save/restore. `phase-compact` = `__heap-restore(__deck-pos)`.
  Deck-bound mode swaps R10 between bivy and deck via
  `deck-bound-counter` / `bivy-save` / `deck-pos` cells (see
  `ArchitectsSketchbook.md`). `__alloc` (`X86_64Helpers.codex`
  `emit-alloc-helper`) = `mov rax,r10; add r10,rdi; <zero-fill via rep
  stosb>; ret`. Lists bump R10 inline (`X86_64Builtins.codex`); records
  via `__alloc`. **No single alloc choke point** — which is why the
  fault (hardware, catches every write) is the right layer, not
  instrumenting alloc sites.
- **REPL heap reset** (`X86_64Chapter.codex`): between batch compiles,
  resets R10/deck-pos/heap-hwm to arena base WITHOUT zeroing (calloc in
  `__alloc` covers reuse). Demand paging must re-arm here (decommit or
  keep-committed-and-reuse — see §5.4).
- **Kernel metadata cells** at `0x7000+`; free slots at 30688/30696
  (confirmed unused) if cells are needed.

---

## 4. The core design decision: identity vs. frame pool

Two variants; they differ in whether virtual == physical.

### 4a. Identity demand-paging (Stage 1 — safety + survey death, no
physical reduction)

Keep VA == PA. Leave heap-range PDs **not-present** at boot. On a #PF
in the heap range, set that 2 MB PD entry = `(CR2 & ~0x1FFFFF) | 0x83`
(the identity phys) and IRETQ. No frame allocator — the frame *is* the
address.

- **Gains:** overflow impossible; survey retired; HWM = touched pages;
  the whole `check-mul` minefield gone; near-zero new machinery (one PD
  poke + IRETQ in the #PF path, and don't pre-fill heap PDs).
- **Does NOT gain:** physical RAM reduction. With VA==PA a 3 GB address
  space still needs 3 GB of RAM present *to be touchable*; we just
  touch less. Good for correctness + measurement, not for running in a
  small footprint.

This is the low-risk first landing and it already deletes the survey.

### 4b. Frame-pool demand-paging (Stage 2 — physical reduction, run in
less RAM than the address space)

Decouple VA from PA. Reserve a large **virtual** arena (can exceed
physical RAM). A **physical frame allocator** hands out 2 MB frames
from actual RAM on fault; the #PF handler maps `CR2_page -> frame`.
`phase-compact` returns the frames to the pool. Now:

- **Peak physical RAM = max simultaneously-live pages** (~200-300 MB
  measured), not the address-space high-water. The compiler runs on a
  small device / many concurrent VMs fit.
- Requires: a frame allocator (§5.2), non-identity PD/PT population,
  and decommit-on-compact.

**Recommendation:** build 4a first (it is mostly "delete the survey +
a 6-instruction #PF case"), prove it, then layer 4b's frame pool. 4a's
#PF hook is a strict subset of 4b's.

---

## 5. Mechanism detail

### 5.1 Boot: reserve, don't commit

- Identity-map the **low fixed region** as today, present: code
  `[1 MB..~5 MB]`, serial ring (5 MB), the page tables themselves, the
  0x7000 metadata, VGA/GOP/MMIO holes, LAPIC/IOAPIC/HPET, device DMA
  buffers (NIC rx/tx, xHCI, etc.). **These MUST stay identity+present**
  — DMA and MMIO cannot fault-in. Enumerate them from `X86_64Boot.codex`
  cell/addr table and the MMIO map in `OperatorsManual.md`.
- The **heap arena** `[heap-base .. ram-size]` (or a higher virtual
  top in 4b): PDs **not-present** (or present-but-reserved sentinel).
- Keep 2 MB granularity for TLB (see §6). A 3 GB heap = 1536 PD
  entries; leaving them not-present is a memset-0 + fill only the
  device/code PDs.

### 5.2 Frame allocator (4b only)

Simple + fast beats clever. Candidates:
- **2 MB free-list stack:** a contiguous array of free 2 MB frame
  numbers; alloc = pop, free = push. O(1), tiny. Initialize with all
  heap frames. This is the recommended default.
- Bitmap if we want fragmentation stats; buddy only if we later need
  mixed page sizes. Not now.
- Frames are 2 MB; the pool for 3 GB = 1536 entries. Trivial.
- OOM = pop from empty pool → clean halt (`CDX-OOM` diagnostic + serial
  dump), the *only* legitimate memory failure.

### 5.3 The #PF handler (the load-bearing code)

In `__interrupt_common`, in the vector<32 branch, add a vector==14 case
BEFORE the generic dump:

1. `mov rax, cr2` (fault address). Account for the CPU-pushed #PF error
   code on the stack frame (the ISR stub / frame layout must match —
   #PF and other error-code exceptions push an extra qword; verify the
   stub in `emit-isr-stub` handles or normalizes this).
2. Range-check CR2 against `[heap-base, arena-top)`. If OUTSIDE →
   genuine fault → fall through to `emit-cpu-exception-dump` (unchanged
   behavior; real bugs still crash loudly).
3. If inside:
   - 4a: `pd_entry_addr = pd_base + ((cr2 >> 21) * 8)` within the right
     PD page; store `(cr2 & ~0x1FFFFF) | 0x83`.
   - 4b: `frame = frame_pool_pop()`; if none → OOM halt; else zero the
     2 MB frame (or rely on write-zeroed frames / defer — see §6
     zeroing), store `frame | 0x83` at the PD entry.
   - `invlpg [cr2]` (or reload cr3) for the mapped page.
   - IRETQ → the faulting instruction re-executes and now succeeds.
4. Error-code check: a #PF with the "present" bit SET in the error code
   is a protection fault (write to RO, etc.), NOT a demand fault →
   treat as real fault → dump. Only not-present (P=0) faults grow.

Keep this handler **allocation-free and re-entrant-safe** (it runs with
interrupts off in the exception frame; it must not itself fault — touch
only present memory: the pool array and page tables must be
identity+present).

### 5.4 Phase allocator on top

- `build(size)` → just `__deck-set R10` + advance R10 by size **without
  touching the pages**. No survey. `size` can be a generous virtual
  reservation (address space is cheap) or dropped entirely for a pure
  grow-on-write bump. Simplest: keep the deck/bivy pointer discipline,
  delete the survey inputs, reserve address space liberally.
- `phase-compact` → lower R10/deck-pos as today AND **decommit** the
  reclaimed `[new_top, old_top)` range: for each now-dead 2 MB page,
  clear its PD entry (4a) or clear + `frame_pool_push` (4b), `invlpg`.
  THIS is what makes reclaim free physical RAM and kills the dead
  middle. Batch the invlpg or reload cr3 once per compact.
- The **reservation-copy pattern becomes unnecessary** for reclaim: you
  no longer need keep-below/scratch-above to compact scratch, because
  decommit reclaims arbitrary dead ranges directly. (Keep it only if
  it still helps locality; likely delete it — a `Less Is More` win.)
- **REPL reset:** decommit the whole arena back to base between batch
  compiles (4b returns all frames; 4a clears all heap PDs). One cr3
  reload. Removes the stale-data concern entirely (fresh faults zero).

### 5.5 What `__alloc`'s zero-fill becomes

`__alloc` currently `rep stosb`-zeroes every block (calloc semantics,
CL 1927) to prevent stale-data bugs. With demand-zero pages (§6), a
freshly committed 2 MB page is already zero, so **per-alloc zeroing is
redundant for first-touch allocations** and could be dropped — a real
hot-path win (the poison-alloc infrastructure proves all fields are
initialized, so zero-fill is a safety net, not a dependency). CAUTION:
reused pages (post-compact re-commit, REPL) must still be zeroed on
commit, OR keep a lightweight per-alloc zero. Decide during impl;
measure. This is a *bonus* the design unlocks, not a requirement.

---

## 6. Performance — perf matters (safety first, but not at any cost)

- **TLB:** keep **2 MB pages**. A compile touches hundreds of MB; 2 MB
  entries cover it in ~hundreds of TLB slots, 4 KB would thrash (512x
  more entries than the TLB holds). Do NOT globally switch to 4 KB.
- **Fault cost:** a 2 MB demand fault happens once per 2 MB of growth.
  For a ~1 GB peak that is ~500 faults per compile — negligible vs.
  millions of allocations. Minor faults are ~hundreds of cycles; ~500
  of them is invisible. (Contrast: the prologue check would have paid 3
  instructions on *every call*, forever — this pays ~500 faults total.)
- **Zeroing cost:** committing a 2 MB page zeroes 2 MB. ~500 pages = ~1
  GB zeroed once — but we ALREADY zero every alloc in `__alloc`, so net
  zeroing likely *drops* (see §5.5). If zeroing on fault is too spiky,
  zero lazily / use non-temporal stores / rely on `__alloc`'s existing
  zero for sub-page and demand-zero only reused frames.
- **A/D bits, huge-page split:** not needed initially. If a future
  feature needs 4 KB protection (e.g. W^X on the code buffer), split
  that one 2 MB PD into a PT on demand — localized, doesn't affect the
  heap's 2 MB mapping.
- **SMP:** `CoreHeap` splits the arena per core today. Demand paging is
  per-core-friendly: each core faults its own range; the frame pool
  needs a lock or per-core sub-pools (per-core is simplest, matches the
  existing arena split). Design the pool per-core from the start.

Net expectation: **lower peak RAM, equal-or-better speed** (fewer
zeroing bytes, ~500 faults, better locality from not pre-touching
huge reservations).

---

## 7. Safety analysis (this is the whole point)

- **Overflow:** structurally impossible. There is no reservation to
  exceed; growth is commit-on-touch until frames exhaust.
- **OOM:** exactly one detection site (frame pool empty), clean halt
  with a diagnostic — not a corruption, not a silent over-run.
- **Real bugs still crash loudly:** faults OUTSIDE the arena, or
  protection faults (P=1 error code), fall through to the existing
  `!EXC` dump unchanged. We do not weaken genuine fault detection.
- **The #PF handler is the new TCB.** It must be allocation-free,
  touch only present memory, correctly handle the error-code stack
  frame, and never grow a protection fault. A bug here bricks boot with
  no diagnostic (double-fault). Treat it like the interrupt code it is:
  minimal, audited, tested first in isolation (see §8).
- **DMA/MMIO must stay identity+present** — a device writing to a
  not-present page faults in a context that cannot be resumed. Enumerate
  and pin every device buffer.

---

## 8. Staged implementation plan (for the fresh session)

Each stage is independently gateable (build + fixed point + battery +
poison + escape + old/new corpus binary-differential — the rigor that
vetted the LOWER cut).

1. **Stage 0 — instrument & prove the hook.** Add the vector-14
   intercept that, for now, just *logs* CR2 and falls through (no
   mapping). Confirm the error-code frame layout is right and we can
   read CR2 in the handler without disturbing the existing dump. Boot,
   run battery. (No behavior change yet.)
2. **Stage 1 — identity demand paging (4a).** Stop pre-filling heap
   PDs; make the vector-14 case map the identity 2 MB PD entry +
   resume. Delete NOTHING yet (survey still reserves, but reservations
   are now just address bumps over demand-mapped space). Verify: the
   `check-mul` minefield is gone (25/15/10 all just work — faults grow
   the deck). Full gate. This alone makes low `check-mul` safe and lets
   us delete the guard-page / prologue-check ideas.
3. **Stage 2 — retire the survey.** Remove `survey-*-mul` inputs from
   `build`/phases; reserve address space liberally (or grow purely on
   write). Delete `check-deck-overflow`/`CDX9002`, DynamicSurvey retry
   in `compile.ps1`, the plug `check-mul:200` offset, and the
   `check-mul=40` change (CL 7130) — all obsolete. Full gate.
4. **Stage 3 — frame pool (4b) + decommit.** Add the per-core 2 MB
   frame allocator; `phase-compact` and REPL reset decommit + return
   frames. Measure peak *physical* RAM (should drop to touched-peak).
   Consider dropping `__alloc` zero-fill for first-touch (§5.5),
   measured. Full gate + a real "compile in `-mem 512`" test.
5. **Stage 4 — simplify.** Delete the reservation-copy pattern where it
   only existed for reclaim; re-evaluate hash-consing/memo copiers (they
   were fighting retention that decommit now handles — some may become
   unnecessary; keep the ones that also cut *touched* pages). `Less Is
   More`.

Stop after any stage with a shippable win; each is a milestone.

---

## 8b. Cross-architecture & IoT — build it arch-parameterized

This design must NOT ship as "x86 demand paging." It is one instance of
a general principle: **commit-on-fault where the hardware has an MMU;
guard-region or bounded where it doesn't.** The phase-allocator
INTERFACE (`build`/bump/`compact`) stays arch-neutral; only the BACKING
is per-target.

**Scope now:** the compiler runs bare-metal only on x86-64 (codex-vm).
The plug pipeline that cross-compiles FOR arm64/riscv/IoT (x86 seed ->
IR -> plug -> ELF) is UNAFFECTED — this changes the compiler's own
runtime, not how it emits target code. So near-term impact on
ARM/RISC-V/IoT targets is zero.

**MMU-class targets (x86-64; ARM64 A-profile - Pi/gateways; RISC-V
S-mode/RV64):** the design PORTS, same concept, different mechanics.
- ARM64: translation tables (TTBR0/1, 2 MB block descriptors), fault =
  synchronous data abort via VBAR_EL1, addr in FAR_EL1, translation
  fault in ESR_EL1; resume tlbi/dsb/isb/eret.
- RISC-V: Sv39/48 via satp, fault = load/store page-fault trap
  (scause 13/15), addr in stval; resume sfence.vma/sret.
- PRECONDITION: verify whether the current arm64/riscv plug runtimes
  even enable the MMU (they may run flat/physical; the Renode
  Cortex-A53 / RV64GC boards have MMUs but our runtime may not use
  them). If MMU-off, "turn the MMU on with identity tables" is step 0
  there. This is needed only when the compiler SELF-HOSTS on those
  (IoT Phase 4). The frame-pool stage (4b) shrinking the compiler's
  footprint is what makes on-device/edge self-hosting on a Pi-class
  board viable - a real IoT-gateway win.

**No-MMU MCUs (Cortex-M, RV32 microcontrollers - the IoT deployment
tier):** demand paging is PHYSICALLY IMPOSSIBLE (no paging hardware).
It is also unnecessary: IoT app code is small/bounded and `punctual`
hard-real-time code has NO heap (CDX6002). The 978 MB survey blow-up is
a COMPILER problem, not an MCU-app problem. Where overrun protection IS
wanted on an MCU, the two stop-gaps we set aside for x86 find their
home as the no-MMU backings:
- MPU (ARM Cortex-M) / PMP (RISC-V) present: a no-access guard region
  above the arena traps overruns (the "guard page" idea, MPU-flavored).
- Nothing at all: the software prologue/alloc bounds check, or just
  bounded static allocation.

So the allocator backing selector is: {MMU: demand-paged} / {MPU|PMP:
guard region} / {none: software check or bounded}. Design the interface
so a target picks its backing; do not hard-wire the x86 #PF path as the
only implementation.

## 9. References (modern practice, confirmed 2026-07-05)

- Ryan Fleury, "Untangling Lifetimes: The Arena Allocator" — reserve
  large, commit on demand; the canonical modern arena design.
  https://www.dgtlgrove.com/p/untangling-lifetimes-the-arena-allocator
- MS Win32 `VirtualAlloc` — `MEM_RESERVE` vs `MEM_COMMIT`; physical
  backing deferred until touch.
  https://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-virtualalloc
- "Virtual Memory Tricks" (gamedeveloper.com) — reserve-commit arena
  idioms. https://www.gamedeveloper.com/programming/virtual-memory-tricks
- Demand paging / #PF mechanics + minor-vs-major faults:
  https://kindatechnical.com/operating-systems/lesson-59-demand-paging.html
  https://offlinemark.com/demand-paging/

---

## 10. What this retires (the satisfying part)

If Stages 1-3 land: `survey-headroom`, `survey-check-mul`,
`survey-check-unit-mul`, `survey-lower-mul`, `survey-resolve-mul`,
`survey-lift-mul`, `survey-inline-mul`, all the `-base`/`-keep-mul`
constants, `SurveyConfig`, `check-deck-overflow`, `CDX9002`, the
`compile.ps1` DynamicSurvey retry loop + `$surveyDefaultMul` mirror, the
plug `check-mul:200` offset, and the reservation-copy machinery — all
deleted. The "survey before you allocate" virtue
(`VisionAndVirtues.md` #12) gets rewritten: **don't survey; commit on
demand.** The compiler stops guessing how much memory it needs and
simply uses what it touches.

---

## Appendix — session context (2026-07-05, blu)

This design fell out of the IR-bloat campaign (`ir-bloat-campaign.md`):
- The LOWER transient cut (main CL 7127) landed the 66 MB retained win.
- Probing the 978 MB CHECK peak showed the survey is ~6x over-committed
  for self-compile and that lowering it hits a geometry-sensitive
  corruption band (the overflow detector is post-hoc). `check-mul` was
  dropped 200→40 as source-only (CL 7130, seed NOT rebuilt).
- The discussion ladder was: prologue ceiling-check (software, 3
  instr/call) → MMU guard page (hardware, 2 MB, #PF surgery, catches
  the error) → **demand paging (makes the error not exist)**. Damian:
  "way better if we have that ability instead of this dumb allocation
  stuff." Build the real thing.
