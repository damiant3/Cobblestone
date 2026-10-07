# Live stage N: the frozen town network

One shared frozen TownNetwork drives Nell, Jorin and Mira's live destinations,
buying/selling and affect, with bounded per-NPC work and every action through
the layer1 validator. Val reviews and owns the final contract; the exact-lot
and tender boundary is `gv-checkout`, with current offers and encompassing
candidate/undo.

## In the composite

A fresh Britain world binds the network at creation (`tnv-new`, then
`gla-market` posts merchant policy); a version6 or older townsfolk world
binds a fresh one at load; `gla-market` posts again as each economy day begins (`cg-town-step`), so stock made since, the baker's bread, is offered. Each pulse first runs `eu-hour` until the economy
hour equals the town game hour (at most 24 per pulse), then `tnv-advance`
with 3 decisions and 8 steps. A refusal, or an economy hour that cannot catch
up, falls back to `tl-advance` and names the cause in the pulse note. TNL1 is
UCC1's version7 section (length qword, then at most
`tnlc-offers-at + 256*72 + 1024*tnlc-event-bytes` bytes).

Survival guard (root, 2026-10-06): after each inference, a resident whose
hunger is at least 30, who holds no food and for whom a food offer passes
`tnv-offer` (live, priced at the current rates, affordable) gets one action,
buy food, in place of the network's queue (`tnv-guard`). The trained model
stops proposing a buy once hunger passes about 75, out of the range it was
trained in. Each override is logged at debug ("TOWN survival guard resident=
hunger= coin= overrides="); the count says whether a retrain is worth it. A
buy that meets a work shift still answers -19 and times out at the shift's end
(-18); the guard queues it again at the next inference.

The audit book holds 1024 events. Measured 2026-10-05 in
`proofs/CompositeBritainWorld.codex`: 18.1 events per game hour at three
residents (1230 events in 68 game hours), so an unrotated book fills in about
57 game hours. Past 960 events the composite keeps the newest 512
(`cg-rotate`). The currency log (`em-rows`, 8192 rows) grew 1.3 rows per game
hour and has no rotation; at that rate it fills in about 6200 game hours.

## Draft choices requiring review

- GameLiveActions borrows TownLive and owns a fixed Ea offer/audit book plus
  captured currency-rate arrays. Merchant policy posts real stock/funded
  offers independently of the requesting network. A changed price revokes
  and reposts consent rather than silently raising a counterparty's limit.
  Legacy EconomyActions still refuses denominated trade with-17.
- Trades call unchanged `gv-checkout`; selected physical lots, EuCurrency
  payment, tax and undo remain authoritative. Eating isolates one selected
  lot unit and recombines the untouched pool. Quotes invalidate on lot changes.
- GameVendorState gains bounded controlled-NPC owner bindings so purchased
  lots survive codec validation without pretending NPCs are player sessions
  or vendors. GVS1 carries an owner table from version2 onward and
  accepts version1. The composite's outer length admission must be updated too.
- TownLive retains a validated control target until expiry and uses its
  existing pathfinder/step budget. Targets are home/work/tavern or an approach
  to a registered vendor. Illness still refuses non-rest movement. The new
  control fields are persisted by TNL1, not TLC1 alone.
- TownNetworkLive keeps the frozen weights, three separate neural states,
  recurrent happiness/alarm, role inputs and four-town residence counters.
  Wealth uses current gold-equivalent value. Baker/tailor use the frozen
  model's "other" role; no untrained raid inputs are invented.
- Queues wait for audit room or travel completion; a new inference must not
  overwrite pending actions. Travel timeout is an explicit recorded refusal.
  TNL1 carries model identity, neural/control state, consent and audit and is
  intended to validate all bytes before changing borrowed route state.

Work currently means travel to the assigned workplace, not a new production
or wage executor. Residents start with zero coin; legitimate income/funding
and full economic behaviour need review rather than fixture grants. The
composite rotates the audit book; offer exhaustion (256 offers) remains an
explicit fixed-budget refusal with no renewal. Review NPC-owner death/retirement,
partial initialization and late-error rollback before accepting restart claims.

