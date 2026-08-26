# GitHub Update 51

**Scope: main CLs after the Update 50 release push commit.** Update 50
covers the cycle from main 19106 through its release head. Accumulate this
cycle's themes here as they land; every number in the final report gets
re-measured at the release head, not carried forward (L-COUNT).

## Open from Update 50

- **De-recurse or trampoline the compiler's emit spine** (`codex-emit-expr`)
  so the crazy-boss page runs in a browser WORKER's 1 MB stack instead of
  the main-thread fallback (plugs 1.14's durable browser close, register
  1.83). Seed-affecting, token, fester's lane per red.
- **The Cobblestone rename campaign continues**: web surfaces and live docs
  (reek or blu), prism/REPL surfaces (fester), the home page (Damian + red).
- **COMPILER-23 A/B oracle routing** queued behind blu's relaunch;
  COMPILER-28 (decorative bounds on non-Integer bases) carries the fix that
  unblocks 23's bounded-domain half.
- **sem-equiv trigger widening** was held for this release and is now
  released; proposer reek, unowned after that.
- **SMP teardown** (one SMP test red per battery run, each a different
  one) stays with the `tools/codex-vm.c` claim holder.
- **B4 step 6, NIC-5, A8's metal arm, WORKS-9** still ride a sitting or
  flight; **HAL crypto dispatch 2 and 3** blocked on a board manual;
  **CostModel `fixed` rung** unshipped; **CDX4022's message text** is
  val's seed-affecting one-liner.

## Landed this cycle

*(accumulates)*
