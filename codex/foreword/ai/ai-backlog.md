# Foreword AI -- open capabilities

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
No open entries.
