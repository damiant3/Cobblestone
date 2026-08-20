# GitHub Update 49

**Scope: main CLs after the Update 48 release push commit, opened 2026-08-20.**
Update 48 covered main 17237 to the release head plus the release's own map,
img, diag, README and report CLs. Accumulate this cycle's themes here as they
land; every number in the final report gets re-measured at the release head,
not carried forward (L-COUNT).

## Open from Update 48

- **The batch stream can lose bytes, and the harness cannot tell that from a
  miscompile.** Seen again during the Update 48 poison run: seven subjects
  red with truncated artifacts (`ideas-test` 204 bytes of 2,199,
  `repo-tombstone` 0). blu has taken it (`codex-vm.c` is his claim) and is
  building a per-layer byte census across guest serial, the host `-output`
  writer and the parser's marker walk, to name which layer loses them. The
  standing hazard until then: **a short artifact and a wrong artifact are the
  same colour on the verdict line.** Check the artifact's LENGTH against its
  `.expected` before believing a red is codegen.
- **plugs 1.34, the ARM64 MMIO boundary** (effect row on the device window, or
  EL0): rulings queue 10.
- **B4 step 6** waits on the B3 metal sitting.
- **NIC-4, NIC-5, WORKS-9, the native GOP metal half and A8's metal arm** all
  ride the next grouped sitting (Track A; red composes, Damian sits once).
- **Rulings still queued for Damian:** 1 (answer a ping), 2 (ARP trust), 3 (the
  rechecker fork), 4 (the 16 MB stick image's depot slot), 7 (vm-differential
  retry), 8 (stale-check dropped-add), 10 (above), 16 (ProductBuilder stage 6
  host), 17 (bounded/budgeted class), 19 (ThreatModel's four questions).
- **`flash-open-bank` Board threading** (root's follow-on; reek and blu own its
  callers).

## Landed this cycle

*(nothing yet)*
