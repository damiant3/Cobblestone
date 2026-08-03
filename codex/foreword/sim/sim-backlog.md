# Foreword Sim -- open capabilities

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

**Do not touch `ElasticHash`, `FunnelHash`, `Lz4`, `Steering` or
`ImageTensor`.** The first two already run a full Murmur-style finalizer, Lz4
takes the HIGH bits, Steering's modulus saves it, and ImageTensor's mask is
its LCG modulus while the output comes from a division that reads the high
bits. All measured. They are named so nobody re-audits them.

**Every remaining grade is a HYPOTHESIS until its consumer is run.** Four
chapters were graded broken from their expressions; two of them (Steering,
ImageTensor) measured fine. Measure first, then migrate.
Migrating a chapter CHANGES ITS OUTPUT, so each is its own changelist with
its own re-recorded expectations.
| # | Capability | State of the gap |
|---|---|---|

**SIM-2 was here and it is fixed, but not for the reason the row gave.** The
row said to read `psys-noise` and decide whether the remainder was degenerate.
It is a remainder by `spread * 2`, so a power-of-two spread would indeed read
low bits of an unfolded hash. That was the smaller half of the problem and it
was not what was actually breaking.

The emitter drew all THREE velocity components from one hash, at `s`,
`s + 1000` and `s + 2000`. A remainder turns an added constant into an added
constant, so every particle received the same y minus x offset, and at a
spread of 100 that offset was exactly zero: vy equalled vx on every particle
emitted. Measured through `psys-emit` over 200 particles, 200 of 200 shared
the modal offset at spread 100 and 168 of 200 at spread 64. Particles fanned
along a line rather than a cone. Each component now draws its own hash, and
`psys-hash` goes through `mix-bits`; the same measurement gives 2 of 200 and
1 of 200. Pinned by `codex/test/particle-spread`.

**A marginal histogram could not have found this.** The spread of vx alone was
even before and after, at 24 24 24 24 26 25 27 26 across eight buckets. The
defect lived in the joint distribution of the three components. Every other
mixer row in this campaign was graded on a marginal, which is worth knowing
before the next one is closed on the strength of an even histogram.
