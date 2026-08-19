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

