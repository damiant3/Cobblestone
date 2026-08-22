# GitHub Update 50

**Scope: main CLs after the Update 49 release push commit, opened 2026-08-21.**
Update 49 covered main 17858 to the release head plus the release's own map,
img, diag, README and report CLs. Accumulate this cycle's themes here as they
land; every number in the final report gets re-measured at the release head,
not carried forward (L-COUNT).

## Open from Update 49

- **Sitting 12 (red composes, Damian sits once):** root's `banked=n` paint at
  the step (19021), the six-part `rings-link` split (19029) and `pchk1`
  listening after the K1 write (18948), reek's `died`/`recovered` sink words
  (18932, 18966) and the K1 control (18874) all ride it. Questions it asks:
  which of swflag or the CTRL|SLU write kills the medium; whether the K1 write
  took; whether the sitting-10 reset hang recurs.
- **The sitting-10 reset hang is open and unexplained**: identical code hung
  at sitting 10 and ran at sitting 11; state-dependent or intermittent.
- **The SWFLAG acquire is a full-register RMW that writes MNG bit 7 and the
  ext-config fields back 2,000 times with no delay** (blu, registered in
  `I219IsNotAnE1000.md`); fix after sitting 12 names the line.
- **B4 step 6** (the repository wire on the part) is open now that B3 flew.
- **NIC-5 and A8's metal arm** still ride a flight.
- **HAL hardware crypto dispatch steps 2 and 3** are blocked on a board
  crypto manual the tree does not hold.
- **CostModel `fixed` rung** stays unshipped until the registry's unknown
  rows are measured.
- **WORKS-25, per-controller USB attachment in codex-vm**: deferred (red,
  2026-08-21) with the size measured in the catalog's prerequisite row.
- **CDX4022's message text is false** (says induction checking is
  unimplemented; it is): seed-affecting one-liner, val's lane.
- **PR 76 closes with the Update 49 push commit named.**
- **Ruling 16 (ProductBuilder stage 6 host)** is customer-gated and the only
  ruling left.

## Landed this cycle

*(nothing yet)*
