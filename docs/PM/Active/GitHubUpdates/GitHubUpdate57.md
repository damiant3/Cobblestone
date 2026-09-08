# GitHub Update 57

**Scope: main CLs after the Update 56 release push commit.** Update 56
covers the cycle from the Update 55 push through its release head
(2026-09-02 evening to 2026-09-08: generic equality in the compiler, the
IoT stack on the wire, chess in the arcade, the plug fleet's integer
types, the desk, preemptive scheduling on metal, the register audit, and
the `-Repl` seeds). Accumulate this cycle's themes here as they land; every
number in the final report gets re-measured at the release head, not
carried forward (L-COUNT).

## Open from Update 56

- **COMPILER-66's larger half, the comparator** (red, CL 23818): proven and
  shelved behind the push; rebuilds on the first seed to land after it.
- **COMPILER-68** (unowned): the BVT never batches, so an Exit-mode seed
  passes it; one arm that compiles a 2-member batch through the candidate.
- **COMPILER-69** (unowned): a `let` bound to an unapplied partial
  application and never used compiles clean and does nothing.
- **COMPILER-42, the rewrite pass** (blu): still OFF; the chunk functions in
  `opening.codex` must return the list they build.
- **plugs 2.26** (unowned): 48 plug runners still write their IR to one
  fixed scratch path (L-SHARED).
- **safari on the site** (val): the port runs and grades; the page and card
  are not on the landing page yet.
- **The arm64 and riscv ban's scope**: Damian's.

## Landed this cycle

