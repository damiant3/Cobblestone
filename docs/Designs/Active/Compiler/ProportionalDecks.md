# Proportional Phase Decks

*Opened 2026-08-03 by fester, at Damian's direction: "can we get apps to only
need the room they need?"*

## The state today, measured

Thirteen phase decks are fixed floors sized for the largest program that
exists, which is the compiler. Every compile reserves all of them.

**CORRECTED 2026-08-03, same day, before anyone acted on it. An earlier
revision of this section said the floors STACK, summing to 3168 MB against a
3040 MB ceiling and exceeding it by 128 MB. That is wrong.** The floors do
sum to 3168 MB, but they are not held at once: each phase's deck is released
and the space reused. Measured with `-Measure`, deck origins fall as often as
they rise -- on the compiler's own source LEX opens at 247 MB, PARSE at
746 MB, then PARSE-KEEP at 277 MB, SCOPE at **86 MB** and LOWER at 170 MB.
The `__heap-save` calls I read as unreleased are paired with restores; a
green gate could not have existed otherwise, which is the check I should have
run against my own claim first.

**Peak frontier is the number that matters, and it is:**

| | peak | deck bytes actually used |
|---|---|---|
| compiler's own source (2,989,272 B) | **1245 MB** | 553 MB across seven decks |
| a 1082-byte app | **1046 MB** | about 1 MB across seven decks |

A thousand-byte app peaks within 200 MB of the whole compiler, and uses one
megabyte to do it. **That is the waste, and it is a peak problem rather than
a sum problem.** The cause is unchanged: one phase's floor is enormous
(`check` 648 MB, `parse-scratch` 392 MB) and the bivy sits above whichever
floor is open, so the high-water mark is set by the largest single
reservation regardless of how little the input needs.

What a 1082-byte app actually uses, measured with `compile.ps1 -Measure`
against `seed/Codex.cdx` on 2026-08-03:

| phase | floor | used |
|---|---|---|
| LEX | 96 MB | 71,368 |
| PARSE (scratch) | 392 MB | 278,280 |
| PARSE-KEEP | 384 MB | 34,112 |
| DESUGAR | 72 MB | 88,168 |
| SCOPE | 104 MB | 73,512 |
| CHECK | 648 MB | 364,520 |
| LOWER | 328 MB | 113,280 |

**About 1 MB used against about 2 GB reserved for those seven.**

`-Measure` runs no IR pipeline, so its LOWER is smaller than the one that
ships. The other rows are the shipping path.

## Why this is safe to change now, and was not on 2026-07-07

`BuildSettings.codex` says the fixed floors "replaced the survey multiplier
system, which sized decks from source length and **corrupted silently when it
under-reserved**". That was the case against proportional sizing.

**Both legs of that case have since expired.**

- *Under-reservation corrupts silently.* Fixed on 2026-07-20 and 07-21, two
  weeks AFTER the multiplier was deleted for lacking it. LEX, PARSE-KEEP,
  DESUGAR, SCOPE, CHECK, LOWER, RESOLVE and LIFT each got a write-path guard
  that stops short of the ceiling and raises CDX9002. Confirmed by running
  `build/deck-floor-test.ps1` on 2026-08-03: `-Decks 5` and `20` raise
  CDX9002 naming LEX, `-Decks 40` names DESUGAR, none crash, and the
  positive control at 100 compiles. **Verified, not read off the prose.**
- *Address space is the only cost.* True while the floors fit the address
  space. They no longer do, by 128 MB.

## The binding constraint is the guard band, not the data

`scaled-floor (flags.deck-scale)` is applied to every floor. **The guard
bands are used raw at every site**, with no scaling. So as the scale falls,
the fixed band consumes the shrinking floor and the guard fires on a deck
holding almost nothing.

Measured on the 1082-byte app, bisected:

| `-Decks` | scaled DESUGAR floor | result |
|---|---|---|
| 11 | 7.92 MB | CDX9002 from DESUGAR |
| 12 | 8.64 MB | compiles |

`demand-desugar-guard-band` is 8 MB. The flip is the band exactly, while the
phase's real usage is 88 KB. DESUGAR is binding because it has the worst
band-to-floor ratio of the phases a small unit reaches.

**So the sum of the bands, 129 MB, is the irreducible minimum under any
scaling scheme that leaves the bands alone.** That is the honest ceiling on
this work: 3168 MB to about 129 MB, a 25x reduction, not the 1000x the usage
table alone suggests.

Do not shrink the bands to chase the rest. `BuildSettings.codex` argues the
asymmetry deliberately: a band too big costs a spurious CDX9002 that says
exactly what to do about it, and a band too small costs silent corruption.

## Where the peak actually comes from, and it is a working set of three

Re-derived after the stacking correction, because "The change" below was first
written believing all thirteen floors were co-resident.

The 1082-byte app's marks: `h0-start` 216 MB, `h1-tokenize` 216 MB, then
`h2-scan` **1096 MB**. The 880 MB step is PARSE opening its keep and scratch
decks while LEX's deck is still held:

```
frontend-keep 200 + lex 96 + parse-keep 384 + parse-scratch 392 = 1072 MB
```

against a measured peak of 1046 MB. **So the peak is not one floor and it is
not thirteen: it is the FOUR that are co-resident at the widest moment, which
is the parse keep-copy** -- keep and scratch must both be open there because
the copy reads one and writes the other. That is by design and is not the
stacking bug I wrongly reported.

`h0-start` is 216 MB before a token is read because `compile-checked` reserves
`demand-frontend-keep-floor` and only then takes the mark
(`opening.codex:575-577`). That floor is 209,715,200, so 200 of the 216 MB is
the frontend-keep deck and the rest is what precedes it. An earlier revision of
this section flagged the 216 MB as unexplained and told the reader to find it
before tuning; it is found, and it is the first member of the working set
above.

Two consequences for the change below. Scaling helps because it scales every
member of that working set at once, so the app's peak falls to roughly the sum
of the three bands plus base rather than the sum of three compiler-sized
floors. And the floors worth scaling hardest are the ones in the widest
working set -- `parse-keep` and `parse-scratch` -- rather than the largest
single floor, `check` at 648 MB, which is open when far less else is.

**The four to scale are therefore `frontend-keep`, `lex`, `parse-keep` and
`parse-scratch`, totalling 1072 MB.** Their four guard bands are 8 + 1 + 64 +
8 = 81 MB, so scaling them alone takes a small app's peak from about 1046 MB
to roughly 100 MB. `check` at 648 MB is the largest single floor and is worth
the least here, because it is open when the others are not.

## The change

Split each floor into the band and the workspace, and scale only the
workspace:

```
floor(scale) = band + max(min-workspace, (base-floor - band) * scale / 100)
```

At `scale = 100` this reproduces today's floors exactly, so the shipping
configuration is unchanged and the seed's own compile is unaffected. Below
100 it converges on the band and never crosses it, so the guard cannot fire
on an empty deck the way `-Decks 11` does today.

The scale then derives from the assembled unit length rather than defaulting
to 100, anchored so the compiler's own unit (2,993,576 bytes, re-measured
2026-08-04; this section said 2,989,272 the day before) yields 100. `deck-scale-of` already reads an explicit `decks=` from the mode
header; that stays as the override, and it needs to start answering 0 rather
than 100 when absent so "explicit 100" and "derive it" stop being the same
answer. `parse-mode-flags` has only the mode string, but `emit-from-disk`
holds the assembled source beside the flags it just built
(`opening.codex:1546-1548`), so the derivation belongs at the call sites that
have both.

**`build/deck-floor-test.ps1` passes unchanged, and I predicted it would not.**
The prediction was that protecting the band would move where each phase
starves, so the pinned `-Decks 5`, `20` and `40` legs would need re-deriving.
Measured against the new compiler: all four legs pass as written, LEX and
DESUGAR still name themselves and still do not crash, and the shipping floors
still compile. The reason is that the band only binds when it dominates the
scaled floor, which happens on small inputs; the compiler's own source at 5 to
40 per cent is still limited by the workspace exactly as before. Running it
was the cheapest proof the change did not quietly disable the guards, and it
is the one prediction in this doc that survived contact by being wrong in the
harmless direction.

### Shipped 2026-08-03: the formula. Derivation is the remaining half

`scaled-floor` now takes the band and scales only the workspace, and all 14
call sites pass their phase's band (`check-keep`, `quote-keep` and
`quote-scratch` have no band constant and pass 0). `deck-min-workspace` is
2 MB.

| | before | after |
|---|---|---|
| lowest `-Decks` that compiles a 1082-byte app | 12 | **5** |
| that app's peak frontier at its lowest setting | 1046 MB | **195 MB** |

Every compiled binary from `-Decks` 5, 8, 11 and 12 was run and matched its
`.expected`, so this is not merely "did not refuse". At 1 and 3 it still
refuses cleanly, which is the guard doing its job.

Reservations at the shipping setting are byte-identical: the same source
measured under the seed and under the new compiler gives the same deck origins
across all seven decks. `build/build.ps1` green, `deck-floor-test` green.

### Shipped 2026-08-04: the derivation. This half is now done

`parse-mode-flags` takes the assembled unit length and `effective-deck-scale`
uses an explicit `decks=` when there is one and `derive-deck-scale` otherwise.
`deck-scale-of` answers 0 rather than 100 when the flag is absent, so "explicit
100" and "derive it" are finally different answers, and `compile.ps1 -Decks`
now defaults to 0 and emits the flag whenever it is non-zero -- while the
default was 100 and 100 was the omitted value, an explicit `-Decks 100` could
not override a derivation, which `deck-floor-test`'s last leg needs.

| | peak frontier, derived | at `-Decks 100` | |
|---|---|---|---|
| unit 8.9 KB | 437 MB | 1047 MB | 2.4x |
| unit 43 KB | 439 MB | 1048 MB | 2.4x |
| unit 77 KB | 442 MB | 1051 MB | 2.4x |
| unit 681 KB | 596 MB | 1089 MB | 1.8x |

Measured with `-Measure` against the SUT. Every `used` figure is byte-identical
between the two arms, which is the point: the same work, less room.

**The prediction in the section above was wrong, and the corpus said so.**

*"A unit LARGER than the compiler gets a scale above 100."* One exists --
`codex/test/apps/foreword-all-compile`, 3,125,731 bytes against the compiler's
2,993,576 -- and letting it derive 105 would move a currently-passing test in
the one direction nothing has measured. The derivation clamps at 100 instead.
It already carries a `.flags` sidecar, so it was never going to derive anything
anyway. **The sidecar says `decks=200`, not the 150 written here, and the
COMPILER-6 section below measures why: that unit requires at least 131 and the
derivation's ceiling is 100, so the sidecar is the only reason it compiles.**

*"If real per-phase usage is superlinear in unit size, a linear scale
over-reserves for small inputs. That is the safe direction."* **It is not
superlinear, it is not linear, and it is not monotonic.** CHECK usage per unit
byte was measured between 21x and 167x, and `Cleanup` (77 KB unit) uses 11.7 MB
of CHECK while `GopUsbMsc` (135 KB, nearly double) uses 2.9 MB. A bare linear
estimate is a line through points that do not lie on one, so the safe direction
had to be bought explicitly with `deck-scale-margin`.

For units above 150 KB the picture is better and DESUGAR binds: across 29 of
them the ratio of the linear estimate to the scale actually required ran 1.24
to 1.71, so the bare estimate clears the requirement by as little as a quarter.
`deck-scale-margin` of 2 puts the tightest observed case at 2.5x.

**The floor is the number this change mostly IS.** 1628 of 1674 compilable
entry points derive below it. It is set from the two worst-DENSITY units in the
tree rather than from a small app, and both are pathological on purpose:
`list-literal-too-large` (one 131,038-character line holding a 65,536-element
literal) refuses at 12 and compiles at 16, and `shell-build-keep` refuses at 24
and compiles at 28 on a 26,164-byte unit, needing 28 times what its length
predicts. `deck-scale-min` was 32 when this was written and is **64** in the
source; the COMPILER-6 section below re-measures what the floor has to clear,
and it is neither of these two units any more.

Validated by sweeping **all 1674 entry points** at the derived default: zero
CDX9002, zero crashes. An earlier setting of 12 was swept the same way and
found exactly those two, which is what set the floor. `deck-floor-test` passes
with its starved legs still naming LEX and DESUGAR, so the guards are not
quietly disabled, and `build/build.ps1` is green.

**The gate cannot see this change.** The compiler's own unit derives to 100, so
every gate path reserves exactly what it did before and a green gate would look
identical if the derivation did nothing at all. The corpus sweep and the
paired `-Measure` arms above are the instrument; do not read the gate as
evidence for this feature.

### COMPILER-6 answered 2026-08-09: it tracks, over a range almost nothing occupies

The open question was whether deck derivation tracks unit growth or whether 64
is just a value that works. Measured over every quire against seed
`A66E54F57CBAEBFD`, with `build/deck-headroom.ps1`, which is the runner this
validation never had.

**The linear term is consulted for fifteen units.** It only beats the floor
above `deck-scale-min * anchor / (100 * margin)` = 957,944 bytes, and it only
falls under the clamp below `anchor / margin` = 1,496,788. That window is a
1.56x range of unit length and the corpus puts 15 entry points in it. Of 2814
measurable entry points, **2798 derive the floor, 15 derive from the linear
term, and 1 derives the clamp** -- and the one at the clamp is the compiler's
own unit. So the floor is not a fallback under the formula. It IS the
derivation.

**The clamp is already too low for a unit that exists, and this doc talked
itself out of noticing.** The section above dismissed
`codex/test/apps/foreword-all-compile` with "it already carries `decks=150` in
a `.flags` sidecar, so it was never going to derive anything anyway". The
sidecar says `decks=200`, not 150, and it is load-bearing rather than
incidental. Measured 2026-08-09: at `decks=200` its LOWER deck uses 437,990,744
bytes, which requires a scale of **131** -- and `-Measure` runs no IR pipeline,
so the shipping LOWER is larger still, which is what the other 69 points of the
sidecar are for. **The derivation cannot hand any unit more than 100.** At the
100 it would derive, the shipping path refuses with `CDX9002: Deck overflow in
LOWER`, so nothing is silently wrong; the unit compiles only because the
sidecar overrides the derivation.

That is also why this unit is absent from the sweep's 2814. At its derived
scale the `-Measure` path does not refuse, it **crashes in `bag-add+0x21` with
a general protection fault**, on both 3072 MB and 8192 MB. That is C2's open
item -- a starved LOWER faulting instead of raising on the non-CDX path -- now
reproduced on a real corpus unit at its own default rather than on a probe at a
hand-picked floor. The CDX path at the identical scale raises correctly, which
is the asymmetry C2 records.

**Where the linear term does run, it is well behaved.** Across those 15 the
required scale runs 22 to 37 against a derived 64 to 96, a margin of 2.58x to
2.91x, and DESUGAR binds on every one. Requirement is close to linear in unit
length over that range, which is the one place the estimate was ever asked to
be.

**The two tightest units in the tree are the two the derivation cannot reach,
and they are tighter than anything it governs:**

| | binding | derived | required | margin |
|---|---|---|---|---|
| `codex/build/vmconfigScript`, worst of 46 | CHECK-RESOLVE | 64, the floor | **43** | **1.49** |
| the compiler's own unit | DESUGAR | 100, the clamp | **67** | **1.49** |
| the rest of the Shell quire | CHECK-RESOLVE | 64, the floor | 34-42 | 1.52-1.88 |
| `apps/works/GopBoot` | DESUGAR | 96, linear | 37 | 2.59 |
| the other 14 in the band | DESUGAR | 67-82, linear | 22-31 | 2.6-2.9 |

Outside those 47 units nothing in the tree is closer than 2.13x, and 2543 of
2818 need a scale of 7 or less.

The Shell quire sits below the band's lower edge at 70 to 102 KB and takes the
floor; the compiler sits above its upper edge and takes the clamp. **Everything
the formula actually governs has twice the margin of either.** That is the
answer: the derivation tracks growth, accurately, over exactly the units that
did not need it.

**The floor's margin is 1.49x, not the 4x the app corpus suggests, and the
binding case has moved.** 64 was chosen as double the 32 that broke, from
`list-literal-too-large` (16) and `shell-build-keep` (28). Today
`shell-build-keep` needs 30 and the Shell quire needs 34 to 43. Nothing in the
app corpus comes close: outside `codex/build` and the compiler the worst is
`GopBoot` at 37 with 2.59x.

**It is moving, and the compiler is not what is moving it.** `checkappsScript`
is recorded above as byte-identical at 33 on 2026-08-05; bisected 2026-08-09 it
refuses at 34 and compiles at 36. Its CHECK deck usage is byte-identical
(209,427,632) under seeds `AEB5ED2B5043C7C1`, `065D92E60292492D` and
`A66E54F57CBAEBFD`, which span COMPILER-4 and both halves of COMPILER-5, so the
requirement grew with the units and the floor is a constant that does not
follow them. The gate leg below watches the 47 units this reaches; outside
them the next 20 per cent arrives as a CDX9002 wherever it lands first.

**The guards are intact.** 48 units bisected by output equality: 184 compiles
over the Shell quire at 44 through 56, plus the compiler at 40 through 95 and
`shell-build-keep` at 24 through 32. Every starved arm answered CDX9002 naming
its own phase, zero crashes, zero binaries that compiled but differed, and the
knob was monotonic on all 48. The silent arm this doc records for `checkappsScript` at 32
does not reproduce -- it is a clean refusal now.

**Corrections this measurement forced**, all three now fixed in
`opening.codex`: the anchor is not the length that maps to 100, it maps to 200
and clamps, so the derivation stops tracking at HALF the anchor; anchor
staleness is not safe in both directions, and the compiler shrank 247,317 bytes
below it, which is the unsafe direction; and `deck-scale-min` is 64, not the 32
this doc said at the section above.

### CHECK is two constraints on one deck, and reading one of them was wrong by up to 29 per cent

The first version of the runner was exact where DESUGAR bound and 7 to 29 per
cent LOW on every CHECK-bound unit, which is every one of the tightest rows
above. The cause is not the check-keep deck, which is what the section above
first guessed and published as untested.

`compile-type-check` measures the CHECK deck at `check-metrics` and then keeps
allocating on that same deck: `resolved-env`, `sorted-all0` and `sorted-et0`
are all deck-recorded afterwards, and they are what `post-ov-bag` exists to
catch. So the reported `used` was the deck partway through the phase.

**The two guards are also different, and that difference is load-bearing.** The
check body stops a guard band short of the ceiling
(`deck-short-of check-ceiling demand-check-guard-band`) while the resolve tail
is compared against the CEILING ITSELF (`__deck-pos > check-ceiling`). So the
tail may spend the band and the body may not, and the requirement is

```
required = max( ceil(CHECK / perPoint), ceil((CHECK-RESOLVE - band) / perPoint) )
```

`compile-type-check` now measures the deck a second time as `CHECK-RESOLVE`,
which is two lines and one PhaseMetrics record per compile. Both measurements
are kept because `memo-graph` sizes the memo table from the first one, so
moving it later would make that sizing depend on itself.

**The model was written from the source and then predicted two answers before
they were measured.** It reproduced the two flips already bisected (30 and 36)
and predicted 39 and 43 where bisection had only bracketed them to (38,40] and
(42,44]; both held, with clean CDX9002 from CHECK at 38 and 42. Two exact
points would have been a calibration. Two predictions are a model.

| unit | binding | model | bisected |
|---|---|---|---|
| `build/output/Codex.codex` | DESUGAR | 67 | 67 |
| `codex/test/shell-build-keep` | CHECK-RESOLVE | 30 | 30 |
| `codex/build/checkappsScript` | CHECK-RESOLVE | 36 | 36 |
| `codex/build/lintunusedcitesScript` | CHECK-RESOLVE | 39 | **39, predicted** |
| `codex/build/vmconfigScript` | CHECK-RESOLVE | 43 | **43, predicted** |

**What the runner still cannot see.** `-Measure` runs no IR pipeline, so its
LOWER is understated. No LOWER-bound unit is closer than 8x, so it changes
nothing today, and a LOWER-bound row is the one kind that still wants the
expensive instrument. The runner also warns and refuses to be quoted if the
kernel it was given reports no `CHECK-RESOLVE` deck, because that kernel
predates this fix and every CHECK-bound row under it is low.

### The gate leg, 2026-08-09

`build/build.ps1` runs `deck-headroom.ps1 -Quire codex\build -WithSelf
-MinMargin 1.25 -Fresh` between `gen-scripts` and `app-sweep`. 23 s over 47
units at `-Jobs 8`, in a 493 s gate. **The quire is 57 units as of 2026-08-14**
and grows every time a script is lifted to the Shell DSL, so re-measure the
count rather than quoting it; the 23 s and 493 s are from the 47-unit run and
have not been re-taken.

The assertion is derived/required, not a point count, because the two tight
cases have different denominators: `vmconfigScript` needs 46 against a floor of
64 (re-measured 2026-08-14 at seed `8D405FDF`; this said 43), the compiler's own
unit 67 against a clamp of 100. A point-count threshold passes the clamped unit
at any size. 1.25 trips the quire at 52 and the compiler at 80.

### CHECK-RESOLVE is bound by the resolved ENVIRONMENT, not by unit size

**Measured 2026-08-14, and the anti-correlation is the whole point:**
`checkdoccountsScript` at 101,042 bytes needed 52 of 64, where
`vmconfigScript` at 116,795 bytes needs 46. **The bigger unit needs less.** An
author who reads a tight CHECK-RESOLVE row as "my chapter is too long" will cut
the wrong thing and it will not move.

CHECK-RESOLVE is the resolve tail -- `resolved-env`, `sorted-all0`,
`sorted-et0` -- so what it tracks is the size of the resolved environment. Top
level definition count against required scale, over the tightest units in
`codex/build`:

| top-level defs | required | unit |
|---|---|---|
| 96 | 56 | `ablatedoctrineScript` (before) |
| 93 | 52 | `checkdoccountsScript` (before) |
| 46 | 42 | `checkxdiagcellsScript` |
| 45 | 41 | `checkplugtypesScript` |
| 32 | 39 | `checkconstantsScript` |
| 24 | 46 | `vmconfigScript` (47,716 bytes of source) |

Size is a real but second-order term -- `vmconfigScript` reaches 46 on 24
definitions because its expressions are enormous -- and the first-order term is
how many names the chapter binds at top level.

**So the author-side remedy is to inline single-use bindings, and it is
cheap.** Two chapters landed red on 2026-08-14 at 1.14 and 1.23 because they
were written giving nearly every sub-expression its own name; inlining took
them to 25 and 52 names, 45 required each, margin 1.42. Nothing else about
either chapter changed.

**Raising `deck-scale-min` is the wrong lever for this.** It is a whole-corpus
constant costing 6,710,886 bytes of reservation per point per unit, and the
condition is one chapter's naming style. Reach for it when the corpus moves,
not when one unit does.

**Re-measured 2026-08-15 (fester) as a controlled sweep, which the table above
is not.** The rows above compare DIFFERENT chapters, so naming style and
content vary together. `cdxtopeScript` was instead regenerated at four section
granularities over byte-identical body text, leaving the binding count as the
only variable:

| top-level defs | required | margin | unit length |
|---|---|---|---|
| 24 | 51 | 1.25 | 145,555 |
| 14 | 48 | 1.33 | 144,995 |
| 8 | 46 | 1.39 | 144,659 |
| 5 | 45 | 1.42 | 144,491 |

**Six points of required scale while unit length moves 0.7 per cent.** The
anti-correlation holds on a chapter 45 per cent larger than any in the table
above, and the effect is monotone rather than a threshold, so a chapter can be
tuned to a target margin instead of only rescued from a red.

**Diminishing returns are real and the knee is early.** Halving the bindings
from 24 to 14 buys 3 points; going from 8 to 5 buys 1 and costs a 72,466
character line. The cheap points are the first ones.

**For a `codex/build` generator chapter the restructure is provably safe**, and
that matters because the emitted `.ps1` is a checked-in artifact:
`build/check-generated-scripts.ps1` recompiles the generator and diffs the
result against the shipped file, so match with 0 drift after the change IS
proof the output is byte-identical. Inline freely, then check. There is a floor
on how far to take it: fully inlined, `ablatedoctrineScript` reached 43 with a
single 25,800-character line. One binding per section costs 2 points and brings
the longest line to 6,173, which is `vmconfigScript`'s order. Take the 2 points.

**A `codex/build/*Script.codex` is a unit this leg measures**, seed-affecting or
not. Both red chapters were verified with `check-generated-scripts` and per
script control arms and never with `build/build.ps1`, on the reasoning that a
generator is not seed-affecting. True, and irrelevant: proving the OUTPUT is
right says nothing about whether the chapter COMPILES within its deck.

Raising `deck-scale-min` is the floor-side remedy and is not free:
`(demand-check-floor - band) / 100` is 6,710,886 bytes per scale point, so 64
to 80 is +107 MB of reservation per compile on every unit that takes the floor.
The clamp side has no such lever; `derive-deck-scale` cannot exceed 100 and a
`.flags` override is the only route.

Arms: `-MinMargin 1.25` exits 0; `1.55` exits 1 naming both ends; a
pre-`CHECK-RESOLVE` kernel (seed `#221`) exits 1 refusing to report, since such
a kernel understates every CHECK-bound row by 7 to 29 per cent. `-Fresh` is
required or the script serves cached logs.

**Corpus, re-measured against seed `D4DC1FE059613C2F`.** 2818 measurable units;
derivation source 2802 floor, 15 linear, 1 clamp. Every unit under 2.0x margin
is one of the 47 watched. Tightest outside them is 2.13x (`shell-build-keep`,
`ShellBuild`, CHECK-RESOLVE at 30 of 64), next rung 2.61x. None needs more than
its derived scale. Binding phase: 2125 under the 2 MB minimum workspace, 490
DESUGAR, 119 CHECK-RESOLVE, 65 CHECK, 17 LOWER, 2 SCOPE. **The 2.13x pair is in
`codex/foreword`, which this leg does not cover**; add its quire if it moves.
Full corpus is `pwsh build/deck-headroom.ps1`, about 25 minutes, manual.

**`foreword-all-compile` needs 137 and ships at 200 (1.46x), bisected by output
equality against the same seed.** 100/120/131/136 refuse with `CDX9002` in
LOWER and emit nothing; 137 emits 118,710 bytes; 138 through 200 are
byte-identical to 137. It is absent from the 2818 because it crashes on the
`-Measure` path at its derived scale (`bag-add+0x21`, C2's item), so the cheap
instrument cannot reach it; the "at least 131" this doc used to carry came from
that path and is 4.4 per cent low. Not watched by the leg: it needs a real
compile at 27.5 s, and its remedy is one number in one sidecar.

## What this does and does not fix

**Does:** apps stop reserving floors sized for the compiler. A small app goes
from a 1046 MB peak to roughly 130 MB, set by the guard bands. About 8x on
peak, not the 25x an earlier revision claimed from the discredited sum.

**Does not:** the compiler compiling itself. There the unit really is the
whole 2.99 MB and the scale comes out at 100, so its 1245 MB frontend peak is
unchanged. It also does not unblock C1, which is not a deck problem at all.

## There is no companion fix, and C1 is blocked on something else

**An earlier revision of this doc proposed "release scratch decks between
phases" as the fix for the compiler's own headroom and for C1. That work is
already done** -- it is what the non-monotonic origins above are. Do not
implement it. The peak is already the MAX of the floors rather than their
sum.

Which relocates the C1 blocker. The frontend of the compiler's own source
peaks at 1245 MB and finishes; the `-IrCce` run dies at 3034 MB in
`__str_concat`, which is **after** the frontend. So roughly 1.8 GB is
consumed by the IR text emission itself, on top of a frontend that had
already peaked and come back down.

`IRTextEmitter.codex` is 779 lines of functions that each return `Text` and
are concatenated by their callers, with no GC.

### The emitter is LINEAR in time. Its memory cost is not yet measured, and here is why

Measured 2026-08-03, `-IrCce` against `seed/Codex.cdx`:

| input | IR bytes | secs | KB/s |
|---|---|---|---|
| `act-tco-loop` | 2,022 | 0.7 | -- |
| `ecdsa-cert` | 1,807,890 | 15.4 | 117 |
| `brotli-interop` | 2,280,337 | 18.7 | 122 |
| `db-full-test` | 14,084,686 | 112.9 | 125 |

**Constant throughput across a 7x range, so the superlinear-concatenation
hypothesis is refuted for TIME.** Note also that source size does not predict
IR size: brotli's 68 KB source yields 2.3 MB of IR and db-full-test's 31 KB
yields 14 MB, because the cited foreword dominates. Use IR bytes as the
independent variable, never source bytes.

**The memory question is still open, and host working set is the wrong
instrument for it.** Peak working set during those runs was 544 MB
(`ecdsa-cert`), 235 MB (`brotli-interop`) and 995 MB (`db-full-test`) -- and
brotli has MORE IR than ecdsa-cert while using less than half the memory, so
peak is not monotonic in IR size. A two-point fit over the other two gave
"38 MB of heap per MB of IR"; the third point predicted 562 MB and measured
235. **Do not quote that ratio, it was a line through two points.**

The control says why. Running the same three with `-Measure`, which runs the
frontend and no IR pipeline, gives a peak of **243 MB for all three**,
identical, because touched pages are dominated by the fixed floors rather
than by the input. Working set measures pages touched, and demand paging plus
floor-touching confound it; the quantity that actually matters is the
frontier, R10.

### The instrument already existed and the harness was throwing it away

`emit-ir-cce` takes `__heap-save` either side of the emit and prints the marks
after the payload (`opening.codex:1445`). `compile.ps1` stops scanning at
`SIZE:` and then uses that line's `WD:` prefix only as a terminator for the
payload-end scan, so the frontier across the emit has been measured and
transmitted on every run and discarded on arrival. That is why a question
about what the emitter costs appeared to have no instrument. Now logged;
the change is host-side and not seed-affecting.

### With it, the emitter is exonerated. It STREAMS

| input | IR produced | frontier moved by the emit |
|---|---|---|
| `ecdsa-cert` | 1,807,890 | 638,960 (0.35x) |
| `db-full-test` | 14,084,686 | 1,575,216 (0.11x) |

`emit-ir-cce` makes two passes -- `ir-defs-wire-size` for the total, then
`ir-print-defs` through `print-text` -- so it never builds the IR as one
`Text`. The chapter's own prose says so. **The no-GC concatenation hypothesis
is refuted for memory as well as for time, and the 779 lines of
`Text`-returning functions were a red herring throughout.**

### So the cost is the IR PIPELINE, which is what `-Measure` does not run

The frontier at pre-emit is 26 MB on `db-full-test` while its frontend peaked
at 1066 MB and released all of it. `-Measure` calls `compile-frontend`;
`-IrCce` calls `compile-frontend-ir`, which additionally runs lowering,
resolve, lift and the IR passes. `BuildSettings.codex` already records the
consequence from the other side, that `-Measure` "runs no IR pipeline and
therefore measures a smaller LOWER than the one that ships".

**That is where the compiler's missing 1.8 GB is, and it is the next thing to
measure.** Instrument the frontier per IR pass rather than per frontend phase.

### A defect found on the way: the last two heap marks are mislabelled

`fe.heap-marks` carries **8** entries -- parse contributes 5, scope pushes one
at `opening.codex:444`, check pushes one at 482, and lowering adds none. Every
emit site then prints `fe.heap-marks & [h-pre-emit, h-post-emit]`, so the two
appended marks land on label indices 8 and 9, and `phase-labels` has
`"h8-check"` and `"h9-lower"` there. The dedicated `"h-pre-emit"` and
`"h-post-emit"` labels sit at indices 11 and 12 and are never reached.

**So every `PHASE-h8-check` and `PHASE-h9-lower` this project has ever logged
is the emit bracket, not that phase.** Anyone who has sized the CHECK or LOWER
floor from those two lines was reading the wrong number. The numbers in the
table above are correct precisely because they are read as the emit brackets
they actually are.

**Fixed.** `format-emit-marks` gives the two brackets their own labels from
`emit-mark-labels`, and the four call sites now format the frontend marks and
the emit marks separately instead of concatenating and letting one label
table number both. That was chosen over pushing three more frontend marks
because it corrects the lie without guessing which marks CHECK, LOWER and
CTORS were meant to record -- those three are still missing and `phase-labels`
still names them, which is a smaller open question left deliberately open.

Verified by the values not moving: `h-pre-emit` and `h-post-emit` now print
16,327,376 and 16,966,336 on `ecdsa-cert`, the same two numbers that
previously appeared as `h8-check` and `h9-lower`. Gate green at 188.2 s.

## Prerequisite: `__alloc` has no ceiling test

`emit-alloc-helper` (`X86_64Helpers.codex:2830`) is `mov rax, r10` /
`add r10, rdi` / `rep stosb` / `ret`. There is no bounds check anywhere in
the tree. The deck guards protect a phase against overflowing ITS OWN floor;
nothing protects the frontier against running off the end of RAM, which is a
different failure and the one that produced the 2026-08-03 crash: the
zero-fill wrote over the boot stack and the program died later in
`__str_concat` with CCE text in its callee-saved registers.

Land the guard first. Every step of this design risks under-reservation, and
each one should fail loudly.

### Attempted 2026-08-03, and the control refused it. Read this before retrying.

The guard itself is right and cheap. `emit-minimal-guard` already emits
`cmp rsp, r10` / `jb __out_of_memory` and records the jump in
`stack-overflow-checks` for finalize to patch, so the fix in
`emit-alloc-helper` is one line placed between the bump and the `rep stosb`:

```
   in let st2 = st-append-code st1 (mov-rr reg-rax reg-r10 & add-rr reg-r10 reg-rdi)
   in let st2g = emit-minimal-guard st2
```

That built and gated green (211 s, exit 0, `cdx-fixedpoint` a real 12.0 s
stage-2 rather than a short-circuit). Reading the emitted bytes of a test
binary confirms it: two `4C 39 D4 0F 82` sequences, both patched to the same
target, so the check is present and wired.

**It does not fire, and the reason is the whole problem. `__alloc` is not the
chokepoint.** Run against a probe that parks the frontier with
`__heap-advance` and then concatenates text, the guarded and unguarded arms
crash IDENTICALLY, with `!EXC=0d` at RIP 0x100223 and 0x10022c -- nine bytes
apart, exactly the size of the inserted `cmp` plus `jb`. At the fault
R10=0xBE000D30 against an interrupted RSP of 0xBDFFFF88, so the frontier had
crossed the stack and nothing stopped it. The crash is inside `__str_concat`,
which **bumps `r10` inline and never calls `__alloc`**, and `__str_concat` is
where the 2026-08-03 compiler failure was read too.

So the frontier is advanced from several emitted helpers, each with its own
inline sequence: `__str_concat` and `__heap-advance` are confirmed,
`__list-with-capacity` is strongly suspected (an 8,000,000-slot request
overran to GPA 0xc22652d0, past the end of RAM, with the guard in place).

### The survey is in, and it says do not build a chokepoint at all

Counted 2026-08-03 across `codex/compiler/Emit/*.codex`, code lines only,
column-2 prose skipped:

| form | count | |
|---|---|---|
| `add-ri` / `add-rr reg-r10` | **58** | allocating bumps, each able to cross the stack |
| `mov-rr reg-r10` | 7 | restores |
| `mov-load reg-r10` | 7 | reloads |
| `li reg-r10` | 2 | inits |

The 58 bumps sit in eight files: `X86_64Helpers` 19, `X86_64ListHelpers` 14,
`X86_64TextHelpers` 11, `X86_64Builtins` 8, `X86_64` 3, and one each in
`X86_64Compound`, `X86_64Chapter` and `X86_64IPCHelpers`.

**Routing all 58 through one bump helper is a refactor of the hottest code in
the system, and each site is inline precisely because a call would cost more
than the allocation it performs.** Two instructions of guard at 58 sites is
also not free, and it is 58 chances to place one wrong.

### Use a guard page instead. One change, catches all 58, zero hot-path cost

**SHIPPED 2026-08-04.** Built as described below. What the build added to the
sketch: the handler's guard test must come BEFORE the lo/hi range tests, not
after, because at the shipping 3040 MB the guard index is 1487 and
`demand-heap-hi-page` is 1024, so the range test would route it to the
exception dump and the guard arm would never execute. Placed after, it works
only on configurations below about 2 GB, which is the configuration nobody
runs -- and it would have passed a casual "does it fault" check on one of them.

`emit-demand-unmap` clears the PDE unconditionally, after the loop and after
the empty-range branch rejoins, so the page is unmapped whether or not it falls
inside the demand range (at 3040 MB it does not; at 2048 MB it does).

Placement is `ram-pages - demand-stack-reserve-pages - 1`, immediately below the
64 MB the boot stack already reserves. That reserve is not a new number: on any
machine at or below 2 GB it is ALREADY what bounds the stack, because a stack
growing below it hits a not-present page and double-faults. `ArchitectsSketchbook`
puts typical self-compilation at about 1 MB of stack, so the margin is 64x.

**Verified by `build/guard-page-test.ps1`, which fails against the seed.** The
FIRE arm parks the frontier just below the page and walks up in 40-byte steps:
OUT OF MEMORY under the new compiler, `SURVIVED` under the depot seed after
burning 3.5 MB straight over the boot stack. The CONTROL arm parks far below,
burns the same amount and survives under both, so the harness cannot pass by
answering OOM to everything. Running it against the seed is what proves it
tests the fix rather than testing itself.

Cost: the maximum usable heap drops by the guard page plus nothing else, about
2 MB, and the frontier now stops at 2974 MB instead of running to 3034 MB and
into the stack. Gate green, 218.2s, with a real 12.8 s `cdx-fixedpoint` stage 2
rather than the one-pass short-circuit, which is the expected shape when
emitted code changes.

**The limit to know: a single allocation larger than 2 MB steps over the hole.**
The page catches incremental growth. It does NOT catch a single large bump.

### RETRACTED 2026-08-04, same day, before anyone built on it

**An earlier revision of this section said the guard page does not catch the C1
overrun, and that a deck reservation leaps it. That was measured against a
binary with no guard page in it, and it is wrong.**

The kernel under test was `build-output/bare-metal/Codex.cdx`. **The SUT's boot
code is emitted by the SEED**, so a change to `emit-demand-unmap` or
`emit-pagefault-handler` appears in stage1 and beyond, never in the SUT the same
build produces. The guard page was in the source and in the probe binaries the
SUT compiled -- which is why `guard-page-test` passed -- and was absent from the
SUT's own boot code, which is the code that runs when the SUT does a compile.
The instrument could not have fired. This is the project's own "the instrument
has to be able to see the thing", and it produced a confident published finding
in the opposite direction.

Settled by ablation once a seed cycle put the page in the shipping seed:

| kernel | guard page | `build` ceiling test | whole-compiler `-IrCce` |
|---|---|---|---|
| `seed/Codex.cdx#586` | no | no | `!EXC=0d` in `__str_concat+0xF9`, R10 0xBDFFFAF8 |
| `seed/Codex.cdx#587` | **yes** | no | **OUT OF MEMORY**, 29 s |
| SUT | yes | yes | OUT OF MEMORY, 29 s |

**The guard page alone converts the C1 crash into a clean refusal.** The middle
row is the whole argument: it has the page and not the ceiling test, and it is
already fixed.

The step-over mechanism described below is REAL -- a probe parked above the page
writes over the stack untouched -- but it is not what the compiler does. Deck
reservations start from a low frontier and land below the page; the frontier
then walks into it incrementally, which is the case the page is good at.

### The mechanism that was described, kept because it is true and bounds the design

`build` really does advance in one jump:

```
  build (size) =
   let p = __heap-save
   in let deck-init = __deck-set p
   in let advanced = __heap-advance size      <-- one bump, the whole deck height
   in p
```

A deck reservation is a single advance of hundreds of megabytes. Confirmed
directly rather than inferred: the probe arm re-run with a park value that lands
the frontier ABOVE the guard page in one advance prints `SURVIVED` and writes
happily over the boot stack.

So a reservation CAN leap the page. What the retraction above establishes is
that the compiler's do not: they are taken from a low frontier and land below
it, and the walk into the page afterwards is incremental.

**The ceiling test in `build` shipped anyway, and its honest status is
belt-and-braces, not a fix.** `deck-reservation-guard` reads the published
guard-page base and writes one byte into the page when `p + size` would cross
it, so an oversized reservation is refused at reservation time rather than
relying on a later bump landing inside a 2 MB window. It costs one compare per
deck, thirteen per compile, against one per allocation at the inline sites --
which is why `build` was worth guarding and the 58 sites were not.

**No measurement distinguishes it.** The ablation table above has the page-only
row and the page-plus-check row reporting the same OUT OF MEMORY in the same
29 s. It covers a real mechanism that no workload has been observed to use. Do
not cite it as the thing that fixed C1; the page did that.

Leave one 2 MB page unmapped between the top of the usable heap and the
bottom of the boot stack. Any bump that crosses it faults on first touch,
whichever of the 58 sites did it, including sites added later. The cost on
the allocation path is nothing at all: no compare, no branch, no load.

The machinery is nearly all present. `emit-demand-fault-handler` in
`X86_64Boot.codex` already classifies a fault and already has both a serve
path and a dump path (`j-prot`, `j-lo`, `j-hi` to `fix-pos`, and a separate
`dump-pos` for a non-page-fault). A fault inside the guard page is exactly
the case that must NOT be served, and it should route to `__out_of_memory`,
which already exists and already resets RSP from `ram-size-addr` before
printing -- which is what makes it survivable when the stack is the thing
that was hit.

What to confirm before building it: that the boot mapping can leave a hole
that high without disturbing the demand range top (`min(1024, ram_pages-32)`
2 MB pages), and that the fault handler can distinguish the guard page from
an ordinary not-present demand page cheaply, since it serves those constantly.

This supersedes the `__alloc` guard in CL 12759. That change is correct as
far as it goes and covers only allocations that route through `__alloc`,
which the measurement above shows is not the ones that matter.

**Do not retry by adding the guard to each site one at a time.** That is the
same shape as the deck guards, which needed eight separate phases fixed over
two days and still left this direction uncovered. Find every place that
writes `r10` first -- grep the emitters for `reg-r10` as a destination -- and
decide whether they can share one bump helper. A single chokepoint is what
makes one guard sufficient, and its absence is why this failure class has
outlived every attempt to catch it.

The probe that produced the result above is worth rebuilding: park the
frontier just under the stack with `__heap-advance 3180000000`, then
`grow 20000 "seed"` concatenating a 32-byte literal. It was not committed as
a battery test because `__out_of_memory` ends in `cli; hlt; jmp -6`, so a
test that legitimately trips it hangs rather than exits, and the harness
would record a timeout. Deciding what a passing OOM test looks like is part
of the work.

## Order

1. ~~The guard page.~~ **Done 2026-08-04**, runner `build/guard-page-test.ps1`.
2. Measure the IR PIPELINE, not the emitter. The emitter is exonerated above.
   Instrument the frontier per IR pass; the compiler's missing 1.8 GB is in
   lowering, resolve, lift or the passes, all of which `-Measure` skips. This
   is the C1 blocker.
2a. Fix the mislabelled heap marks first, or the instrument in step 2 will be
   read through the same off-by-three that has been mislabelling CHECK and
   LOWER all along.
3. ~~Proportional workspace scaling.~~ **Done 2026-08-04.** Formula shipped
   2026-08-03, derivation shipped 2026-08-04, validated by the 1674-unit sweep
   and `deck-floor-test`. Damian's request is delivered.

The validation prescribed here -- compile the app corpus at the new default,
confirm zero CDX9002, re-run `deck-floor-test.ps1` -- is what found the two
density outliers that set the floor. It was worth every second of the sweep and
should be re-run by anyone who changes `deck-scale-min`, `deck-scale-margin` or
`deck-scale-anchor`. **It is automated as of 2026-08-09 --
`build/deck-headroom.ps1`, about 25 minutes over the whole corpus at
`-Jobs 8`** -- and it still does not gate.
