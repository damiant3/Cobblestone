# GitHub Update 55

**Scope: main CLs after the Update 54 release push commit.** Update 54
covers the cycle from the Update 53 push through its release head (the
compiler memory campaign, the sem-equiv release blocker, Steve Howell's
queue, the hosted x86-64 lift, wasm parity, the games arcade, Prism stage
2, and the one-DIMM gate work). Accumulate this cycle's themes here as
they land; every number in the final report gets re-measured at the
release head, not carried forward (L-COUNT).

## Open from Update 54

- **The compiler memory campaign, stage 2** (red): the LIFT deck (200 MB),
  the CHECK-RESOLVE tail (152 MB deck, 49 MB bivy) and DESUGAR, measured
  at the Update 54 head; the row is CurrentPlan's red row.
- **Two collectors owed from the sem-equiv blocker** (red): `$tSemantic`
  in `build/build.ps1` should fire on `IR/Lowering.codex`, and
  `compare-codex-semantic.ps1` should name an empty source body instead
  of reporting a mismatch.
- **COMPILER-41** (blu): `show` on a Real corrupts a digit once the
  integer part passes sixteen; the printer, not the parser.
- **COMPILER-36** (reek, blocked on a ruling): the trapping integer
  default cannot be built as ruled; options in `compiler-backlog.md`.
- **The L-NOGATE candidate runner**: `-Internal` RUNS, not merely
  compiles, the `codex/test` chapters whose source cites a changed
  chapter. Unowned.

## Landed this cycle

*(accumulates)*
