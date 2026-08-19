# GitHub Update 48

**Scope: main CLs after the Release 47 push commit, opened 2026-08-18.** Update 47
covered 16559 to 17236 plus the release's own map, img, README and report CLs.
Accumulate this cycle's themes here as they land; every number in
the final report gets re-measured at the release head, not carried forward.

## Open from Update 47

- **The battery harness can lose bytes from a batch stream.** Carried from
  Update 44; not seen since.
- **Nothing exercises the guard page under a genuine allocation walk.**
- **plugs 1.34, the ARM64 MMIO boundary** (effect row on the device window, or
  EL0): rulings queue 10.
- **B4 step 6** waits on the B3 metal sitting.
- **blu's CDX2064 sibling-argument extension (CL 17122)** was held out of
  Update 47 so the proofs ran once; it lands at MAIN OPEN and moves the seed.
- **rulings queue 15** (HAL Power: linear Board for `sleep-deep`) blocks root's
  last HAL step.
- **The `vm-differential` gate arm has no retry** and reds the whole gate on a
  QEMU timeout (rulings queue 7); it did so once during the Update 47 proofs.
- **My preview gate reported `Sut === stage1` on a pre-convergence stage** (blu
  measured compile(A) != A that night); the gate's fixed-point claim wants a
  look (red, 2026-08-18).
- **Steve Howell's issue 70 and PR 67**: comment with the public commit at the
  Update 47 push (PublicPush.md).

## Landed this cycle

### Interim mirror push, 2026-08-19 (github a061c173, gitlab the same), seed `800A7683`

Not a release: a routine mirror update so Steve Howell could rebase the
zig ladder on everything of his that had been absorbed. It carried main
17243 to 17256: blu's CDX2064 sibling-argument extension and its test (the
seed moved `90646EEB` to `800A7683`), and the three items below. The README
seed digest was re-measured for the seed in the commit (`check-doc-counts`
61/61); `Codex.img` was NOT rebuilt and still embeds `90646EEB`, which the
README's img row states correctly as that file's own hash. Rebuild it at the
Update 48 release as the release skill already says.

- **PR 69 (Steve Howell), main 17251:** `Resolve-PlugForewords` asks presence
  before the registry, so a chapter both hand-listed and cite-resolvable is
  bundled once. Generator and regenerated script together; all 55 bundles
  byte-identical; control bundle with Sort hand-listed drops from 8800 lines
  (Sort twice) to 8742.
- **PR 71 (Steve Howell), main 17254:** the zig prelude allocates through one
  `ArenaAllocator` instead of `page_allocator` per object; his ladder
  measurement 3.0 GB OOM-killed to 238 MB, output byte-identical. Re-applied
  by hand; the `cx_heap` unification he designed is left for his follow-up.
- **Issue 72 (Steve Howell), main 17254:** the zig plug dropped match guards.
  A guarded match is now an if-chain in a labeled block; guardless matches
  byte-identical; the oracle subject carries nine guard rows; zig PASS 49/49.
  The same rows show python, wasm, csharp and typescript drop guards too, and
  typescript mis-emits its first variant type (`never` with no separator):
  `plugs-backlog` 1.46.
- **PR 69's aside, not taken:** `build/test-compile-batch.ps1` passes
  `Start-Process -WindowStyle Hidden` unconditionally, which PowerShell on
  Linux refuses, so `check-generated-scripts.ps1` cannot run there. He offered
  it as its own PR and was told yes.

