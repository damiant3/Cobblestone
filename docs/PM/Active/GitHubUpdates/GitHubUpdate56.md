# GitHub Update 56

**Scope: main CLs after the Update 55 release push commit.** Update 55
covers the cycle from the Update 54 push through its release head (seed
`BBB9907CBE21CB16`, 2026-09-02: trapping integer arithmetic, memory stage 3,
the apps on the landing page, safari-codex, the box and main rule).
Accumulate this cycle's themes here as they land; every number in the final
report gets re-measured at the release head, not carried forward (L-COUNT).

## Open from Update 55

- **COMPILER-42, the rewrite pass** (blu): the ownership analysis and the
  copying helpers are on main with the pass OFF; applied to the compiler
  itself it crashes in `mcopy-labels` (22254), and the fix named on the
  row is that the chunk functions in `opening.codex` must return the list
  they build.
- **safari on the site** (val): the port runs and grades; the page and
  card are staged.
- **plugs 2.26**: 48 plug runners still write their IR to one fixed scratch
  path (L-SHARED); wgsl is fixed. Unowned.
- **The seed install runner** (fester, `build/sign-seed.ps1`): two releases
  in a row found the seed on main was not the fixed point of its source.
- **The arm64 and riscv ban's scope**: whether emission-only compiler work
  on those backends is covered (blu's CL4, red's 2.21 halves, plugs 2.06).
  Damian's.

## Landed this cycle

*(accumulates)*
