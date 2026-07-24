# val -- workplan

*Status, not journal. Per-CL history is in Perforce. Durable process truths are in
`docs/Agents/PerforceProcess.md` and `CLAUDE.md`, not here. This file is the current
picture and the next moves only. Keep it under ~80 lines.*

## Status -- 2026-07-22 (session end)

**No red gate, nothing shelved or pending, tree clean both clients, no stray VMs,
token released. Everything of mine is integrated through main 10406.** Depot seed
unchanged all session at
`B180343660EBB6CF67C7B7319DB4980EE501E9FF7C31CA5949C8741699D0EAA8` (2604983
bytes) -- **no CL carried a seed**, checked after every gate, because neither the
compute bridge nor the foreword quires are cited by the compiler.

### Shipped this session (main CL), all no-seed

- **BACKLOG 4.17 CLOSED.** Compute-bridge matmul auto-runs on the RTX 4060 Ti
  above ~400^3 flops (10296 conv1d/max-pool/clamp, 10314 conv2d, 10338/10339).
- **BACKLOG 7.20 CLOSED (10376, 10381, 10394).** `foreword-all-compile` cites
  **415 chapters, up from 177** -- every chapter in every foreword quire, and it
  compiles and runs with **zero CDX3006**. Thirteen broken AI chapters fixed
  (10376) and given per-chapter tests; four cross-chapter collisions fixed
  (10381): `Pressure`, `LoadResult`, `MdBlock`+5 ctors, and `md-parse` /
  `rgb-lerp` / `rgb-brightness`.
- **BACKLOG 7.30 CLOSED (10394).** The two `Lwm2m` chapters merged into one
  (Encode survives, Foreword's deleted). Its TLV encoder was the wrong one and
  the test only asserted `length > 0`; `lwm2m-encode` now checks bytes.
- **BACKLOG 7.28 CLOSED (10406) WITHOUT building what it asked for.** See below.

### Gate truth

`build/build.ps1` run **four times, GREEN every time**, hard fixed point ONE PASS,
`constants.hash` 262 unchanged throughout. **No seed installed or submitted, so
`test-self-verify.ps1` is NOT APPLICABLE and was NOT RUN. The full battery was NOT
RUN** (Damian's tool) -- except one `-FW -NoErrors` run he approved in-session:
**719 total, 684 pass, 11 fail, 24 skip.**

**8 of those 11 look real and are NOT mine** -- `induction-assoc`,
`induction-list`, `induction-param`, `induction-parse`, `proof-smoke` all fail
`CDX4020 not emitted`; `prose-anchor` fails `CDX1101 not emitted`;
`annotation-under-header` and `interval-exhaustive` fail on output. All are root
battery, on depot source and the depot seed, and none of these classes is retried
by the harness. The other three (`effect-widen-scope`, `field-guard-refine`,
`fresh-alloc-narrow`) were `FAIL_COMPILE` under load and **compile clean
standalone** -- contention. **Reported to Damian; nobody has picked them up.**

### 7.28: closed by proving the work was wrong to do

It asked for a verifier over the tree emitter's tail-call shuffle. Not built.
Re-measured with `-Passes lir-dump` on the compiler's source: **1,529 selected /
3,879 declined = 28.3 per cent**, against the entry's own 1,507 / 3,825 = **also
28.3 per cent**. Four LirRetarget steps (4f-4i) landed between the two and the
tree path's share did not move; the counts grew only because the source did. So
the tree emitter is not being retired and the entry's hedge was false -- which
argues FOR building it, and is why it had to be measured. It still should not be
built: the gap is the whole tree emitter (its prologue is 7.19), and `LIR.md` s8
already funds the answer.

**The real defect was two lines.** `codex/test/tco-shuffle-spill` and
`tco-direct-arg-reads` are the repros of the two miscompiles, added by the CLs
that fixed them, and **neither was in the BVT**. Both are in it now (58 tests,
~13s). **`build/build.ps1` runs `build/bvt.ps1`, NOT the default battery** -- a
test in `codex/test/` root is pinned but NOT gated unless `bvt.ps1` names it.

### OTHER AGENTS

- **fester duplicated my 7.20 work** and landed 208 cites at main 10379 while I
  was extending to 416. Caught at the mandatory merge-down; my list was a strict
  superset so nothing was lost. Second duplicate-work collision in a month (7.29
  was the first). **After init, say the pick out loud before building.**
- **A copy-up can silently drop files.** `p4 copy --from //Codex/val` at main
  10381 carried 10 of 13 -- it left the three that had entered the CL as
  *integrate* records from a merge-down. Main kept the old content while I
  reported the item closed. **Count the copy's files against the CL's, and
  `p4 print` off MAIN before saying anything landed.** Submit the merge-down as
  its OWN CL rather than folding integrate records into the work CL.
- **The compute bridge:** `GpuBridge` (`codex/os/kernel`), doorbell ports
  0x420-0x423, guest buffers **cmd #BD000000 (6 MB), reply #BD600000**. All 17
  ops have entry points. The COM3 slab is committed at boot in codex-vm -- do not
  move a bridge buffer without checking the commit covers it, or the host memcpy
  hits reserved memory (0xC0000005). A box with no NVIDIA GPU refuses a device
  launch rather than falling back; do not soften that.

**Source no longer cites `BACKLOG <n>.<m>` or a design-doc filename.** The
register renumbers and design docs move, so a number in source rots into pointing
at the WRONG entry. Write the gap in prose.

### Next in the val lane

Nothing blocked, nothing to resume, nothing shelved.

- **The 8 red root-battery tests above** are the most concrete open thing, and
  they are unowned. Five are one class (`CDX4020 not emitted` across the proof
  tests).
- **6.1** store cutover is **not available work** (Damian, 2026-07-22): waits on
  infrastructure and a tree not moving under codegen/syntax. Do not pick it up.
- **Grounds migration** (CurrentPlan gap 1, `GroundsBoundary.md`): migrate
  `codex/os` off the blanket effect-exemption onto explicit `grounds`. Touches
  the compiler's exemption list, so word to reek first.
- **Damian, 2026-07-22, on how I work:** *"you agents don't finish out stuff,
  spend time documenting nits and nags and hangnails, you run full builds and
  gated pushes for one tiny feature at a time, instead of batching up work."*
  Batch several items into ONE gate and ONE token hold; finish an item to
  landed-and-verified-on-main before writing prose about it; skip doc hygiene
  unless the change makes an existing doc false.
