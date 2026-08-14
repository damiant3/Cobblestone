# GitHub Update 42

**Scope: main CLs 14991 to 15084, released 2026-08-14.** Update 41 covered
14771 to 14990. Every number below was re-measured at the release head
(seed `8D405FDF`, 2,793,222 bytes), not carried forward.

## The headline: the compiler compiled itself on the metal

**A5 SHIPPED** (reek, main 15041). The compiler read its own source off the
volume on the ASUS and wrote `OUT.CDX` byte-identical to the host compile of
the same source with the same kernel. The blocker was a UD2 above a 4 GB
heap: `PhaseAllocator` and `EmitAllocator` are widened and the stub zeroes
the guard cell. The seed cycled and converged.

## Trust: the rechecker now raises ONE finding against the whole compiler

**C2's last four abstentions are closed** (val, main 15023). They were the
`lower-lambda` defect one level down, in the BRANCHING NODE rather than the
lambda -- and the register's account of why was wrong, which is the part
worth keeping. It said the branch bodies had no concrete type to substitute;
they were concrete all along. `lower-match` already had `infer-match-type`,
which derives the type from the arms, and consulted it only `when ty is
ErrorTy`. A bare type variable is not `ErrorTy`, so it took the other arm and
discarded what it had just computed. The `if` is the same shape through
`merge-ty`.

Whole compiler, `-Passes none`: **AGREE 4864, DISAGREE 0, UNSUPPORTED 0,
IMPROVED 0, SINGLE-WITNESS 0**, against IMPROVED 4 before. Kill-rate 27/27
with a passing control. The only finding the rechecker now raises against
the entire compiler is one `bounds-underived` abstention in
`compile-type-check`.

**`types-equal` is total** (val, main 15052). Ten of the 26 `CodexType`
variants had no arm and fell to `otherwise -> False`, so it answered "not
equal" for two identical sum types. Never a soundness gap -- its only caller
is a fast-path short-circuit whose fall-through unifies those forms
correctly -- but it is a trap for the next caller, and it caught this lane:
a working fix measured as no change at all. Fixed rather than documented,
on the principle that a trap cheaper to remove than to describe should be
removed. **No measurable speedup** from the recovered fast path: 11.8 /
11.96 / 12.41 s against 11.8 / 12.38 / 12.30 s, noise floor about 0.4 s.

## The network moved from "a tick" to a duration

**The poll-count-as-duration class** (blu, main 15013 and 15028). A NetIO
tick was 100,000 polls, which is 1.55 s on the NE2000 and 2.9 ms on the
e1000 -- the same constant meaning two different durations on two devices.
The e1000 transmit wait was a 605 us budget against a 1200 us frame at
10 Mb/s. Calibrated against the driver, the ARM64 twin partly fixed with the
remainder specified, and the test for the class is published in the
Operator's Manual. First TCP conversation over the e1000 in the same cycle,
with codex-vm's NAT taught to survive a retransmitted SYN.

## The desk pointer saturates its input

**WORKS-26 closed** (val, main 15001). A pane's pointer was delivered at
17.8 reports/s against the desktop's 102.2, and the recorded cause (repaint)
was wrong: `desk-cal-loop` repaints nothing. The desktop was smooth only
because `desk-clock` reads the CMOS every trip and a port read is a VM exit;
a pane that never exits never got serviced. HID service moved off the exit
path onto its own thread, which is what a controller polling a periodic
endpoint actually does. Every pane now reads **102.2 reports/s**, against an
unmoved 102.2 desktop control.

## The build scripts are generated from Codex

**The Shell DSL campaign closed** (fester, main 15006), and the lift
continued through the cycle: `check-sidecars`, `check-facts-guid`,
`check-effect-vocab` (15020), `check-plug-ports` (15037), `check-plug-types`
and `check-cdx-registry` (15045), `check-doc-counts` (15057) and
`ablate-doctrine` (15063) are now emitted from generators under
`codex/build/`. `check-generated-scripts` reports any build script with no
generator.

The lift also produced the cycle's one red gate and its fix (15068, 15071):
two generated units came in under the 1.25 deck-headroom floor at 1.14 and
1.23. Fixed by cutting the resolved-environment size, then restoring the
section structure the first fix had inlined away -- one binding per section
costs 2 points of deck and buys the file back. Quire tightest is 1.39.

## Release proofs, all four green at the release head

| Proof | Result |
|---|---|
| Battery (depth) | 1,454 tests, **1,427 pass, 0 fail**, 27 skip |
| App sweep (breadth) | **264 clean, 0 regressions** over 269 units |
| Poison build (memory hygiene) | **0 fail, 0 newly red** |
| DDC (trusting trust) | **WITNESS HOLDS**, 0 bytes differing outside the signature |

Oracles: scalar 2013/2013, vector 130/130, CCE 1485/1516 with 31 in
documented gaps and 0 unexplained. Gate: `SUT === stage1` hard fixed point
in one pass, and `SUT === seed` byte-identical, so the seed that ships is
provably the fixed point of the source that ships.

The DDC surfaced one blocker exactly as designed: `BootPaint`'s `bp_cmos`
calls `port-in-byte`, the C# plug had only the write direction, and Roslyn
refused the emitted name. Two stub entries added (main 15084).

## Open, carried into Update 43

- **`COMPILER-3`: `-Repl` and non-repl compiles of the same source differ in
  255,683 bytes.** Same function names at the same offsets, different bytes
  inside the bodies, and no gate can currently see it. First experiment is
  in `codex/compiler/compiler-backlog.md`.

- **`COMPILER-5`: a hex literal past i64-max breaks the text round-trip.**
  Filed by reek during the A5 work.

- **27 skipped tests**, catalogued by `build/audit-skips.ps1`: 7 REAL (they
  still differ), 6 TRIVIAL stubs that assert nothing, 13 with no `.expected`
  at all, and 1 STALE that now passes. The stubs are the ones that read as
  coverage and are not; the audit names each and why.

- **The 0xFE8 RAM-size cell is a private ABI between the harness and the
  guest.** Steve Howell named it as a compromise during PR 63 and he is
  right: nothing in the compiler writes that cell, and the multiboot header
  does not set MEMORY_INFO, so the guest never asks the loader the question
  whose answer it is handed. Retiring the cheat is open work.

**Closed from Update 41's open list:** `codex/test/engine-shadow` is no
longer skipped. It was unskipped at 14796/14805 when the shadow regression
was root-caused -- second-depth shadow mapping is wrong for anything
standing on the surface it shadows -- so the battery's 0 fail no longer
depends on that skip.
