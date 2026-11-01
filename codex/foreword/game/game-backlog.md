# Foreword Game -- open capabilities

Quire-domain backlog. The shape and priority order for the platform live in
`docs/PM/CurrentPlan.md`; there is no platform-wide register any more.
Anything that is this quire's own behaviour lives here.

The rules are the standing ones: an entry says what is still missing and
nothing else, a closed entry is DELETED rather than annotated, and a gap that
is still real is never quietly dropped.

## The shared mixer

`Foreword chapter Random` supplies `mix-bits` and `rand-in-range`,
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

**Every remaining grade below is a HYPOTHESIS until its consumer is run.**
Four chapters were graded broken from their expressions; two of them
(Steering, ImageTensor) measured fine. Measure first, then migrate.

Migrating a chapter CHANGES ITS OUTPUT, so each is its own changelist with
its own re-recorded expectations.
| # | Capability | State of the gap |
|---|---|---|

**FGAME-1 was here and it is CLOSED, not open.** The row described `fy-hash` as
an unfolded LCG feeding a modulo and a Fisher-Yates shuffle, and called it the
highest-visibility instance in the tree. That was true when written and is not
true now: `fy-next-random` advances through `mix-bits`, the account of the old
behaviour is in `CardDeck.codex`'s own prose, and `codex/test/carddeck-shuffle`
is the distribution check the row asked for. Re-measured 2026-07-27 by compiling
and running it: card 0 lands 39 30 32 31 28 39 26 31 across the eight positions
of an eight-card deck against an expected 32, where before the fix it landed
56 16 37 18 33 25 39 32; the two-card stripe that reversed on 255 of 255
consecutive seeds is gone, at 127 alternations.

**FGAME-2 was here and the migration is declined.** DiamondSquare's hash folds
once before the consumer, so its own row said weaker than a finalizer but not
degenerate: duplication rather than defect. Migrating changes terrain output and
forces every expectation downstream to be re-recorded, and buys one shared
definition in place of a private copy. Damian declined that trade on 2026-07-27.
Its consumer was NOT measured, so if terrain is ever suspected on other grounds
this is an open question again and this paragraph is not evidence about it.

Both of this quire's mixer rows are now closed, one by measurement and one by a
declined trade, so nothing in the migration remains here.

## Inventory loses items and miscounts equipment

Four defects in `Game chapter Inventory`, each pinned by a line of
`codex/test/forewords/game-inventory` (2026-10-31). `inv-add-to-slot` caps a stack at
its maximum and drops the rest (30 then 20 of max 40 leaves 40); an add into an empty
slot does not cap (50 of max 40 stores 50); `inv-remove` takes only from the first
matching slot (removing 42 from stacks of 40 and 5 leaves 5). And `inv-equip` sets
`inv-equip-count` to `list-length cleaned + 1` after `list-push cleaned entry` has
grown `cleaned` in place (L-ALIAS), so the second distinct slot equipped counts 3
for 2 entries, and `inv-unequip` then walks past the list and traps (`!EXC=06`,
measured). **Open:** overflow into a new stack, cap the first stack, remove across
stacks, and count the list after building it.
