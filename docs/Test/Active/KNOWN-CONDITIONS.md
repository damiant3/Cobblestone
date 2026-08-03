# Known Conditions

**Live conditions only.** This file exists to stop agents
re-investigating things that are already understood. It records what is
*currently true and surprising* -- not history.

Three rules for this file:

- **A resolved condition leaves.** Its record is the changelist that
  closed it. A fixed bug documented at length here is not "context"; it
  is a trap that makes the live entries harder to find.
- **An open capability does not live here.** This file says "do not
  chase this", which is a different claim from "someone should build
  this". Where a condition below implies work, the work is not tracked
  by this file and, since 2026-07-23, is not tracked by a
  platform-wide register either.
- **Verify before trusting.** Every entry carries the date it was last
  checked against the tree.

Last full audit: 2026-07-13.

---

## Apps

### CVMM "type checker bug" (2026-06-22) -- it was never a compiler bug

App builds are two-stage: the app's own `build.ps1` bundles chapters,
then `build/compile.ps1` **re-resolves all `cites` and silently prepends
anything the bundle missed**. If the bundle's chapter list is
incomplete, the compiler sees a LARGER source than the bundle file on
disk -- so errors reference "phantom" code and look exactly like
type-environment corruption.

It is a build-list gap, not corruption. The 14 original errors were
genuine type errors in silently-prepended chapters. `compile.ps1` prints
the extra chapters it prepends -- **read that output before suspecting
the type checker.** The sort/bsearch/text-compare machinery was audited
and is correct.

## Codegen

### The tree emitter's small-arg TCO miscompiles a compound accumulator (2026-07-17)

Found by LIR differential fuzzing. The **tree** emitter's optimized
small-argument tail-call path writes the decremented loop parameter
*before* it computes a later tail-call argument that reads that
parameter, so the accumulator sees the already-updated value.

Repro (`probe (n) (acc) = if n <= 0 then acc else probe (n - 1) (acc - n + 2)`):
`probe 1 7` is `7 - 1 + 2 = 8`, but the tree answers `9 = 7 - 0 + 2` (n
read as its decremented value); `probe 5 (-2)` answers `-2` where the
correct value is `-7`. Reproduces on the depot seed (the tree is what
compiles it there).

**Scope is narrow, and shrinking -- this is why the note is here and not
filed as work.**

- The tree's **general multi-arg TCO path is correct.** A 7-parameter
  version of the same shape compiles and runs correctly; only the
  small-arg optimized path has the bug.
- Every function that actually hits that path AND is selector-eligible
  (non-punctual, <= 6 plain params, non-bounded return, lowerable body,
  no effect) is now routed to the **LIR selector**, whose topological
  back-edge argument ordering compiles it correctly. So promoting the
  selector *fixes* the reachable cases rather than leaving them.
- The residual is a small-arg tail accumulator declined by the selector
  for a *non-param* reason. Most such shapes cannot even be written: a
  bounded-return accumulator does not compile (`CDX2051` without an
  explicit `__narrow`), and a `punctual` function cannot self-recurse
  (`CDX6005`). The only realistic residual is an effect-carrying tail
  accumulator whose accumulator update reads the loop counter -- an
  unusual shape.

**What to do:** nothing, unless the residual bites. The discovery is
kept so it is not re-investigated from scratch. The correct-ordering
behaviour is already pinned as a **selector regression** by `s-tail` in
`codex/test/lir-selector-smoke.codex` (in the BVT): if a future change
dropped the selector's topological ordering, that test answers `9` for
`8` and fails. Fixing the tree path itself (so the fallback is also
correct) is possible -- mirror the selector's `@8449` ordering in the
tree TCO -- but it is low value while the selector covers the reachable
cases, and may never be worth doing.

### `ConOut->ClearScreen` and the Asus/Dell UEFI heap corruption -- cause UNIDENTIFIED

CL 1223 removed `uefi-clear-screen` from the dev-console redraw path
after observing heap corruption on Asus/Dell UEFI boards.

The standing suspect -- the heap base pointer at `0x7580` -- is
**EXONERATED (2026-07-15)**. `uefi-heap-base-addr` (30080 = `0x7580`) was
read in exactly one place, `emit-uefi-start`, which had **no caller
anywhere** in the tree, and was **written nowhere**. The live UEFI stub
(`build/cdx-to-pe.ps1`) sets R10 = `0x1000000` directly, stores it to
`deck-pos` / `heap-hwm`, and calls `opening` directly -- it never enters
`__start` and never touches `0x7580`. The prior "STILL LIVE" note had only
re-checked that the constant still equalled 30080, not that any live path
read it. The dead `emit-uefi-start` and the `uefi-heap-base-addr` constant
have been deleted.

So `0x7580` cannot be the cause, and **the actual corruption cause on
those boards is unidentified.** The EDK2 reference
`GraphicsConsoleConOutClearScreen` touches only the framebuffer and
protocol-internal cursor state -- no heap writes -- and R10 is saved across
the call by `uefi-call-conout` (`X86_64Helpers.codex:607`), so neither
ClearScreen nor R10-clobber is an obvious culprit either.

**What to do:** default to row-fill (`uefi-con-fill-row` +
`uefi-con-blank-rows`) for screen clearing -- it is safe regardless of the
unknown cause. Do not reintroduce ClearScreen without a clean repro probe
(allocate, pattern-fill, ClearScreen, verify the pattern survives) on the
suspect hardware. The corruption cause is open work, untracked,
reproducible only on OVMF or the suspect hardware. The UEFI boot surface
is fester's arena (`BootRoadmap.md`).

## Type System -- linearity / mutable-aliasing checker

The checker in `Types/TypeChecker.codex` (`lin-of` for `linear`,
`consume-of` for `mutable`) is sound for current code. What follows is
one deliberate design constraint that **must be preserved**, and one
accepted imprecision.

### The field walk stays sum/list-blind -- do not "fix" it

`apply-threads` decides a call consumes its bare mutable argument by
asking whether the callee's return type hands the record onward.
`return-mentions-mut` recurses through *parametric* structure --
`ListTy`/`LinkedListTy` elements and a `SumTy`'s instantiated type
arguments -- and delegates named types to `type-mentions-mut`, the
record-FIELD walk. So `-> List MutRec`, `-> Maybe MutRec`, and
`-> (T, MutRec)` all count as consumption. Pinned by
`errors/mutable-launder-{sum,tuple,list}-return`.

**Do NOT add `SumTy` ctor-payload or `ListTy` arms to
`type-mentions-mut` itself.** This was tried in CL 2710 and reverted. A
return type like `Token`, whose field chain reaches the mutable record
only through a list or sum field, merely *reads* the state -- completing
that transitive chain rejects legitimate borrow-twice callers
(`make-token`'s shape). The split is structural:

- parametric wrapping at return position = **threading** (consumption)
- nominal field chains through sum/list boundaries = **borrowing**

Pinned by `codex/test/mut-borrow-transitive.codex` -- a positive battery
test that CL 2710's approach would reject. If you are about to "complete
the transitive chain," that test is why you should not.

### Remaining accepted imprecision

A callee that returns a *fresh* wrapped record while only borrowing its
argument is still counted as threading -- type-level analysis cannot see
the body. This is the same imprecision the direct-return `CheckResult`
pattern has always had, and no current code trips it.

If it ever bites, the direction is a callee-body escape analysis ("does
param i flow into the return value"), not a smarter type walk.
