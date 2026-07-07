# Demand Paging Hardening — closing the review findings

**Status:** Fix series SHIPPED on //Codex/val 2026-07-06 — CL A = 7207
(#PF hardening), CL B = 7208 (TSS/IST1 + comparator hex literals),
CL C = 7209 (dynamic demand top; -mem 512/1024/2048/3072 all verified
booting), CL D = 7210 (AP idle stacks to [0x20000,0x60000) + unrolled
spawn pre-touch; spawn/fork tests green), CL E = 7211 (codex-vm
default -mem 3072). Each gated: two-pass seed convergence then
one-pass hard fixed point, BVT, targeted probes. Series close (full
battery, plug rebuild, copy-up) and the optimization backlog remain.
**Origin:** val's adversarial review of the demand-paged arena
(main CL 7202, seed DDAB0BD2). Companion to
`docs/Designs/Compiler/Done/DemandPagedArena.md` (the design) and
`DemandPagingVictory.md` (the ship story). This document records what
the gates could not see, the fixes, and the optimization backlog that
builds on the same machinery.

---

## 1. Review findings (all verified, 2026-07-06)

The gates all run at `-mem 3072` on a single core. Every finding lives
at an edge the gates never touch.

| # | Finding | Evidence |
|---|---------|----------|
| F1 | `-mem <= 2048` silent triple-fault: RSP = actual RAM (GPA 0xFE8); at 2048 the boot stack sits at the 2 GB demand boundary; a #PF frame cannot be delivered onto a not-present stack; no IST -> triple fault. codex-vm default IS 2048. | Empirical: probe hangs at 2048, runs at 2056/3072 |
| F2 | Stack overflow diagnostic dead: the 2 GB demand wall precedes the prologue `cmp rsp,r10` check; overflow = silent hang, not `__out_of_memory`. | Empirical: 2M-deep non-tail recursion at `-mem 2056` completes on the pre-demand seed, hangs on the new one |
| F3 | NX/W^X regression: heap PDEs were NX (bit 63; trampoline sets EFER 0x900 = LME\|NXE); the #PF handler maps `0x83` with bit 63 clear -> every demand-mapped page in [6 MB, 2 GB) is executable after first touch. | Static: `emit-pd-entries` nx path vs `emit-pagefault-handler` |
| F4 | Handler maps protection faults: the ISR stub discards the #PF error code, so the design's P-bit check (DemandPagedArena §5.3.4) is absent; any vector-14 with CR2 in range is remapped `0x83` and retried. | Static: `emit-isr-stub` add rsp,8; handler has no EC/PDE check |
| F5 | SMP unvalidated + AP stacks misplaced: no harness passes `-smp`; no battery test executes code on an AP. `ap-stacks-base = 6 MB` puts AP idle stacks inside the demand range AND overlapping the BSP heap base; they are present only because the BSP faults page 3 in first. ArchitectsSketchbook describes codex-vm's fallback (stack-top based, table 0x1000), not the code (6 MB based, table 0xF00). | Static + `-smp 4` boot stderr (`stack=0x620000`) |
| F6 | `seed/constants.hash` stale since CL 4949 -> spurious "seed rebuild required" every build. Root cause: build.ps1 auto-updates the tracked file without `p4 edit`, so it never rides a copy-up. | **FIXED: val CL 7204 -> main CL 7205** |
| F7 | The "+86 KB growth pingpong" (DemandPagingVictory §5) is a one-off, not a committed regression test. | No ballast/growth script exists in build/ |
| F8 | Doc debt: PHASE-ARCHITECTURE.md still teaches survey-before-allocate; OperatorsManual `-mem` default row + `-mem 2048` debugger example are boot-killers; ExaminersAssay run example has no `-mem`; no doc states the RAM floor. | Read |

Cleared during review (checked, no action): handler frame math and pop
order, PDE linear indexing across PD pages, range boundaries (pages 3
and 1024), concurrent same-PDE writes (idempotent atomic stores),
rep-stosb page straddling (restartable), device DMA (host-side writes
bypass guest page tables), REPL monotonic mapping accumulation, plug
and cross-arch runtimes (no MMU use), spawn pre-touch correct for the
current 1 MB stack.

---

## 2. Fix series (this campaign)

Each CL is a single concern, gated independently. Boot-emitter changes
are codegen changes: build twice, install NewSeed, verify one-pass.

### CL A — #PF handler hardening

Only not-present faults grow the heap; the mapping restores NX.

- `emit-isr-stub`: vector 14 KEEPS its error code (nop4 path) instead
  of discarding it. All other error-code vectors unchanged.
- `emit-pagefault-handler`: read the error code at [rsp+40]; if P=1
  (protection/reserved violation) normalize the frame (shift the five
  saved registers over the error code) and fall through to the dump.
  P=0 in-range faults map `identity | 0x83 | (1<<63)` — NX restored.
  Exit path drops the error code (`add rsp,8`) before IRETQ.
- P=0 + PDE-already-present (SMP race: another core mapped it between
  fault and handler) is benign: the store rewrites the same value.
- Touched-page counter: increment cell 30688 per demand map — the
  honest physical-consumption metric (R10 HWM is floor-inflated).
  Folded here because it is two instructions in the same edit and
  saves a later seed cycle.

### CL B — TSS + IST1: double faults become loud

A 64-bit TSS with IST1 gives the CPU a known-good emergency stack for
#DF, converting every silent triple-fault (F1, F2, and any future
stack-invariant violation) into the standard `!EXC` dump.

- GDT64 at 0x12800: null, code, data (same layout/selectors as the
  trampoline GDT), TSS descriptor (base 0x13000, type 0x89) at 0x18.
- TSS at 0x13000 (104 bytes, zeroed); IST1 = 0x14800 (2 KB emergency
  stack at [0x14000, 0x14800), always-present low memory).
- Boot: lgdt, ltr 0x18, then patch IDT entry 8 byte +4 = 1 (IST1).
- BSP only. APs keep no TSS (their #DF still triple-faults) — per-core
  TSS is in the optimization backlog.
- #PF stays OFF the IST deliberately: putting it on IST would make
  stacks demand-grow, changing stack semantics and requiring per-core
  TSS for SMP correctness. Not this campaign.

### CL C — demand top derived from actual RAM

`emit-demand-unmap` computes the clear range at boot instead of the
constant 1024: `hi = min(1024, ram_pages - 32)` where ram_pages =
[0xFE8] >> 21. The top 64 MB of RAM stays present for the stack. This
restores every `-mem` down to ~128 MB. The handler's constant range
check stays: only cleared pages ever fault. At `-mem >= 3072 + 64 MB`
behavior is byte-equivalent to today.

### CL D — AP idle stacks out of the demand range + pre-touch loop

- AP idle stacks move from `6 MB + i*64 KB` (demand range, heap
  overlap) to fixed low always-present memory: base 0x20000, 16 KB per
  core, [0x20000, 0x60000) for 16 cores. Idle APs only ever take an
  interrupt frame plus the wake path on this stack; real work runs on
  scheduler-provided stacks. Kills the F5 timing hazard and the heap
  overlap with compile-time constants.
- Spawn stack pre-touch becomes an emit-time unrolled loop over
  `ceil(size / 2 MB) + 1` touch points, so raising
  `proc-spawn-stack-size` past 2 MB cannot silently skip middle pages.

### CL E — codex-vm default `-mem` 2048 -> 3072

Belt and braces: aligns the default with the harness, and binaries
compiled by pre-CL-C seeds remain bootable without flags. codex-vm.c +
rebuilt exe (matched pair).

### CL F — doc sweep

OperatorsManual (`-mem` default row, debugger example, demand-paging
constraints note), ExaminersAssay (run example), ArchitectsSketchbook
(AP stack scheme, handler hardening, TSS/IST, dynamic demand top),
PHASE-ARCHITECTURE.md (retire survey-era prose), future-dated
2026-07-07 stamps.

### CL G — growth-pingpong regression script

`build/test-growth.ps1`: append generated ballast definitions (~86 KB)
to the concatenated compiler source, self-compile twice, assert the
two stages byte-identical. Makes DemandPagingVictory's "the old killer
is now a regression test" true in the depot. Standalone script (not in
the default gate; it costs two self-compiles).

### Series close

Full battery + `test-plugs.ps1 -BuildFirst` on val, then one copy-up
to main carrying the final seed.

---

## 3. Optimization backlog (after the fixes)

Ranked. Each builds on the Stage-1 machinery; none blocks the fixes.

### MEASURED, 2026-07-07 (host sampler — see below)

Before chasing the backlog I profiled the self-compile. **The
`__alloc` zero-fill is NOT the bottleneck it looked like.** Two
sampler results:

- Guest sampler (interrupt-frame RIP, injection-skewed): `__alloc`
  57%. This is the skew — WHP delivers the injected timer at the next
  instruction boundary, which biases toward the tight `rep stosb` in
  `__alloc`. An A/B seed with the zero-fill deleted entirely changed
  the self-compile median by **0.1s of 21.9s** (21.9 -> 21.8, inside
  noise), and the guest histogram was nearly unchanged (still ~59%
  "in __alloc") — proof the 57% is where the timer lands, not where
  the time goes.
- Host sampler (VP-cancel RIP, bias-free): `__write_binary` ~78%,
  `emit-map-lines` ~5%, `__alloc` ~5%. The real cost is **serial
  output of the 2 MB binary**, one byte per port write through the
  data channel, plus the symbol-map emission — I/O, not compute.

So the ranked backlog is re-prioritized: the `__alloc` zero-elision
(item 1b below) is a **non-win** on current evidence — keep the
zero-fill (it is the poison safety net) unless a later measurement
under a fixed output path says otherwise. The output path is the
lever.

0. **Batch the binary output path** (NEW TOP ITEM). `__write_binary`
   dominates because it writes the ~2 MB CDX one byte at a time to a
   serial port, each a VM exit / MMIO-ish store. Options: widen the
   output ring writes (qword or block copy into the 0x700000 ring
   the host already drains), or have the guest write the whole buffer
   to a known GPA range and signal length once. Measure against the
   host sampler. Expected to move the ~78% far more than anything in
   the compute path.

1. **Stage 3: frame pool + decommit** (DemandPagedArena §4b, deferred
   at ship). The physical-reduction prize (not a compile-speed one).
   - `phase-compact` and REPL reset decommit -> physical RAM returns
     to the pool; the REPL re-arms to fresh demand-zero pages.
   - (1b) `__alloc` zero-fill elision: measured a non-win above; only
     revisit if the output path is fixed first and a fresh profile
     shows compute-bound allocation.
2. **Per-core TSS/IST** (16 x 104-byte TSS array + per-core ltr in the
   AP entry path) so AP double faults dump instead of triple-faulting.
   Prerequisite for taking SMP seriously under demand paging.
3. **AP-executes-code battery test**: a `.smp` sidecar (like `.disk`)
   that makes the phase-2 runner pass `-smp N`, plus a test that
   dispatches real work to an AP (CoreHeap alloc on core 1, atomics
   handshake back). Closes the F5 coverage hole permanently.
4. **Demand range above 2 GB**: with the frame pool, extend the
   virtual arena past physical RAM (the original §4b promise) and
   stop capping the heap at 2 GB; fixes the db-test class (heap-scan
   overflows 2 GB).
5. **Handler observability**: the touched-page counter (CL A) feeds a
   `PAGES:<n>` line next to `HEAP:` on the control channel; phase
   metrics report touched-per-phase by sampling it at phase-measure.
6. **invlpg elision**: architecturally unnecessary for P=0 -> P=1
   transitions (x86 does not cache not-present entries). Two
   instructions per fault, ~500 faults per compile — negligible; do it
   only if the handler is edited anyway.

Rejected: 1 GB pages (kills touched-page accounting granularity and
the future frame pool for two saved faults); #PF on IST (changes stack
semantics, needs per-core TSS first); a per-prologue stack ceiling
check (per-call cost the demand design exists to avoid — IST + #DF
dump covers the diagnostic need for free).

---

## 4. Invariants (the contract this campaign enforces)

1. A stack must never point into a not-present page (existing, now
   with a loud failure mode via IST).
2. Only a not-present fault (error code P=0) inside
   [6 MB, demand-top) may be grown; everything else dumps.
3. Demand-mapped pages carry the same protection bits the boot mapping
   would have given them (NX above the code boundary).
4. The demand top always leaves the top-of-RAM stack region present.
5. Idle AP stacks live in always-present memory.
