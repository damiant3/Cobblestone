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

### Interim mirror push, 2026-08-22, seed `A01C1547` (unchanged since Update 49)

Not a release: a mirror update carrying main 19069 to 19104, all of it
build scripts, tests and docs, the seed untouched. No release proof was
re-run for it and none was owed: the four proofs certify a seed, and this
seed carried them on 2026-08-21. What it carries is **the battery
choreography** (Damian's direction 2026-08-22, red coordinating, fester on
item 2): **the full battery went from about 10.5 minutes to 123 s** on a
quiet box (phase 1 ~8 min to 58 s, phase 2 151 s to 53 s), measured three
times that day, the first two on a box something else was loading.

- **The batch compiler's parser was quadratic** (red, main 19081).
  `$raw = if (...) { ReadAllBytes } else { ... }` ran the `byte[]` through
  the pipeline and landed an `Object[]` of boxed bytes, so every `GetString`
  and `Array.Copy` re-converted the whole capture: 96 tests 132 s to 0.8 s,
  and the release batches of 193 had spent 417 to 456 s parsing against 20
  to 62 s compiling. One direct assignment in the generator.
- **Batches are dealt by size** (red, 19086): the last run's `.src-bytes`,
  heaviest first onto the lightest batch; round-robin had put 11.0 to 17.4 MB
  per batch, size-dealt 14.2 MB each.
- **`codex-vm -run-list`** (fester, 19092): a supervisor that spawns a FRESH
  `codex-vm` child per line and reports `exit`, `output`, `dropped` and `ms`
  per line, so a batch is byte-identical to N single runs by construction.
  Measured: the `pwsh` child per test was 501 of the 575 ms a test cost, the
  exe's own start 12.6 ms; reusing a process would have bought 2 per cent for
  376 host globals to reset. `build/check-run-list.ps1`, five arms.
- **Both harnesses run their phase 2 through it** (red, 19095 and 19098),
  one supervisor per `-Jobs` slot, proven byte-identical to `test-run.ps1`
  on every sidecar kind. On the way: **a `-RedirectStandardError` file
  anywhere on D: costs ~7.5 ms per stderr line** (2.6 s against 12.3 s for
  the same eight supervisors), now a standing bed fact in the Assay; both
  harnesses capture on the system temp and move the file afterwards.
- **Two root tests renamed** (red, 19100): `engine-culling-cost` and
  `engine-texture-cost` had shared stems with the `forewords/` smoke tests
  and the battery keyed verdicts by stem.
- **`test-run.ps1` releases its writable disk copies** (red, 19102): 9,340
  leaked temp images, 15.7 GB, since 2026-06-15.
- **Open, for the `tools/codex-vm.c` claim holder:** SMP teardown. One SMP
  test per battery run went red and each a different one: `smp-affinity`
  hung at exit for 60 s with its complete output on the wire, `smp-halt`'s
  child faulted on the host (`0xC0000005`) at teardown after its complete
  output. Each green standalone three of three; the harness passes the case
  as `test-run.ps1` always did and the register records it.
