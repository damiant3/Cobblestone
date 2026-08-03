# Foreword Signal -- open capabilities

Quire-domain backlog. The shape and priority order for the platform live in
`docs/PM/CurrentPlan.md`; there is no platform-wide register any more.
Anything that is this quire's own behaviour lives here.

The rules are the standing ones: an entry says what is still missing and
nothing else, a closed entry is DELETED rather than annotated, and a gap that
is still real is never quietly dropped.

## The shared mixer

`Foreword chapter Random` supplies `mix-bits` and `rand-in-range` (CL 10493),
and its prose carries the whole account: why an unfolded multiply-add has no
usable low bits, why that is invisible to a balance check, and -- the part
that matters most -- **why it is only a defect where the consumer reads low
bits.** A remainder by a power of two or a bit-and is degenerate; a remainder
by a large non-power-of-two is often fine. Steering was recorded here as
broken on the strength of its expression, measured, and found fine.

**Read that prose before migrating anything, and measure the consumer rather
than grading the mixer.** `codex/test/mix-bits.codex` is the worked example
and keeps the old mixer as a live negative control.

**Do not touch `ElasticHash`, `FunnelHash`, `Lz4`, `Steering`, `ImageTensor`,
`noise-hash-pair` or `perlin-hash`.** The first two already run a full
Murmur-style finalizer, Lz4 takes the HIGH bits, Steering's modulus saves it,
and ImageTensor's mask is its LCG modulus while the output comes from a
division that reads the high bits. All measured. They are named so nobody
re-audits them.

**SIG-2 and SIG-3 were the last two rows here and both are closed as measured
fine.** Value noise samples the lattice at consecutive integer coordinates, so
that is what was fed to it: 1000 consecutive x through `noise-hash-val`, which
is the consumer and which calls `perlin-hash`, bucketed ten ways where 100 each
is uniform. The answer was 109 104 90 93 90 93 103 90 121 107. `noise-hash-pair`,
which is SIG-2's own fold-then-multiply-then-mask shape, answered 101 111 81 113
102 95 103 92 110 92. Neither shows the concentration the rows predicted.

**`kernel-box-3` has a gain of 999 and not 1000, and that is a live choice
rather than an oversight.** It is `333 333 333`, so a smoothing filter that
should preserve a constant signal attenuates it by a part in a thousand on
every pass, compounding if applied repeatedly. Measured 2026-07-27 and pinned
by `codex/test/convolution-identity`, which records the number without
blessing it. One third is not representable at scale 1000; `334 333 333` would
be exact for a constant at the cost of a lopsided kernel. `kernel-box-5` has
the same shape and was not measured. Left as it is, on the record, because
changing a kernel constant moves output for every caller.

**What was measured is the marginal distribution and not autocorrelation.** A
mixer can spread its values evenly and still correlate adjacent ones. If noise
ever looks patterned rather than merely uneven, that is the measurement to take
next, and these rows do not rule it out.

**The migration was declined for the remaining chapters, deliberately.**
`ENG-1` (WorldGen) and `FGAME-2` (DiamondSquare) each said in their own text
that the body ends on a fold and is not broken, which makes them duplication
rather than defect. Migrating changes terrain and world output and forces every
expectation downstream to be re-recorded, and buys one shared definition in
place of a private copy. Damian declined that trade on 2026-07-27. Their consumers
were NOT measured, so if either is ever suspected on other grounds, it is an open
question again and this paragraph is not evidence about it.