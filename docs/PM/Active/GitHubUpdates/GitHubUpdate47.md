# GitHub Update 47

**Scope: main CLs after the Release 46 push commit, opened 2026-08-17.** Update 46
covered 16149 to 16558 plus the release's own map, img, README and report CLs.
Accumulate this cycle's themes here as they land; every number in
the final report gets re-measured at the release head, not carried forward.

## Open from Update 46

- **The battery harness can lose bytes from a batch stream.** Carried from
  Update 44; not seen since.
- **Nothing exercises the guard page under a genuine allocation walk.**
- **Neither virtio driver derives its DMA regions from the stub's
  allocation**; the build-time floor assertion (16234) is the guard rail.
- **plugs 1.34, the ARM64 MMIO boundary** (effect row on the device window, or
  EL0): ruling with Damian.
- **plugs 1.33, no DECK on arm64/riscv** (blu, staged in the row).
- **B4 step 2b**: cdx-registry/cdx-announce never call net-driver-bring-up
  (root 16526, recorded for blu); B4 step 6 waits on the B3 metal sitting.
- **Steve Howell's PR 67**: the zig ladder moves to its own repository
  (codex-zig-ladder) with a pointer contrib/README.md to land here.

## Landed this cycle
