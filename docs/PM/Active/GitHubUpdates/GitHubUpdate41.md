# GitHub Update 41

**Scope: main CLs 14771 onward, opened 2026-08-12.** Update 40 covered
14534 to 14770. Accumulate this cycle's themes here as they land; every
number in the final report gets re-measured at the release head, not
carried forward.

## Carried in from Update 40

- **`codex/test/engine-shadow` is skipped, not passing.** val's, from the
  shadow work at 14721/14732. The sidecar states its retirement condition:
  val confirms the measured output is what the 3x3 filtered compare is
  meant to produce, re-mints the `.expected`, and deletes the skip. Until
  then `build/audit-skips.ps1` reports it REAL. **The battery's 0 fail
  depends on this skip**, so retiring it is the first thing that should
  happen this cycle.
- **A5 has no owner** and both sticks are rebuilt and bed-verified.
- **The self-reproducing quine** the DDC witness cannot catch is still
  unbuilt.
- **192 bare `print-line` sites** still need judging rather than sweeping.
