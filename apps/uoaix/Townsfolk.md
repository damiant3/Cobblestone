# UOAIX layer 1 townsfolk

`Townsfolk.codex` implements the deterministic layer 1 simulation.
`TownClock.codex` is the separate acceptance entry point. The simulation has
no model, socket, account or disciplinary operations.

## Integration

Cite `Uoaix chapter Townsfolk` from a server chapter. The Windows resolver
registers Uoaix at `apps/uoaix`. The clock runner bundles the simulation as
`Chapter: Uoaix--Townsfolk` so its scratch mutations are the compiled subject.
Keep the test entry point out of a server unit, which supplies its own `opening`.

The caller owns one `TfWorld` from `tf-new 0`. The API mutates that world
and its contained records in place. Do not treat an alias as a snapshot or
call the mutators concurrently. `tf-person` and `tf-town` require admitted
positive serials. Serial zero means no relationship; serials are never reused,
including after death. Dead records retain their inventory and family links.
`world-serial` is the separate world mobile serial, zero until the world
adapter binds a mobile. Persona serials index the simulation, not WorldRecords.
Home, workplace and destination identifiers are supplied by the world adapter.

`tf-add-town world name market tavern temple` returns a town serial.
`tf-add-person world name town home workplace job ageDays` returns a person
serial. Names contain 1 through 64 CCE characters; location identifiers are
positive, supplied by the world layer. Job identifiers are 0 (no trade),
1 (farmer), 2 (baker), 3 (smith), 4 (vendor). A worker must be at least 6570
days old. A new resident has no money or goods. Population and initial stock
are explicit world setup, never a purchase or a model proposal.

Each `tf-step world` advances exactly one game hour. The caller maps wall
ticks to game hours; repeated wall ticks must not each advance an hour.
The simulation selects a destination identifier at the schedule boundary;
walking, map collision and visible mobile movement belong to the world adapter
(`TownLive.md`).
The step processes residents, market transfers, midnight aging, deaths,
births, and crop growth in that order. Births join the next hour's schedule.
Residents are visited in serial order. That order determines who receives
scarce stock. Replay requires the same initial state and external calls.

Before another step, consume `event-count` events from `events`, then call
`tf-ack-events world`. Each event occupies four integers: kind, actor serial,
other serial, game hour. Resolve the actor's town from the retained person.
Kinds are 1 marriage, 2 birth (other is the child), 3 death, 4 illness onset,
5 birth refused at capacity, 6 pregnancy cancelled after loss of a spouse.
For event index `i` (zero based), the sequence is
`world.sequence - world.event-count + i + 1`. Compute that value before
acknowledgement and persist the sequence with the event. A refused append
does not increment `sequence`. The monotonically increasing sequence identifies events in each person's
16-entry memory ring. `memory-next` names the next overwrite position;
`memory-count` saturates at 16. The ring intentionally retains recent indexes,
not an unbounded history. The consumer must persist events before acknowledging
them if crash recovery is required.

Persistence is TSC1 ([TownStateCodec.md](TownStateCodec.md)), which serializes
every field value, dead record, unused slot and the pending event prefix, plus
ordered inputs ([TownInput.md](TownInput.md)): admissions, family commands,
purchases, trusted stock changes and clock advances. Lifecycle notifications
alone cannot replay those inputs. Commit a snapshot with its input-log position
and acknowledged event sequence together; on restart, restore that snapshot and
replay later inputs in order. Deduplicate delivered notifications by sequence.

Return codes are 0 success, -1 invalid request, -2 capacity or clock budget,
-3 unconsumed/full event storage, -4 insufficient stock, -5 insufficient
buyer coins or exhausted vendor coin capacity. Admissions return a positive
serial on success. A refused admission, marriage, pregnancy or purchase
leaves state unchanged. A capacity-refused birth retains its due date and
emits an event each day; the clock still advances.

## Rules

Schedules contain 24 activity identifiers: 0 sleep/rest, 1 work, 2 meal,
3 market, 4 tavern, 5 worship, 6 family, 7 home leisure. Working residents
work at hours 7 through 10 and 13 through 17. Meals are at 6, 12 and 19;
markets at 11 and 18; tavern, family and worship at 20, 21 and 22. Residents
without a trade use home leisure instead of work, family at 20, worship at
21, and sleep at 22. Homes and workplaces remain distinct stored identifiers.

A work hour harvests two crop units into two grain units, bakes two grain
units into four bread units, or forges one ingot into one good. Daily crop
growth supplies 32 units up to a field capacity of 64. Ingots are finite.
Missing inputs or full output stock increment the town's shortage count;
no product appears. Stock and per-person coin/inventory fields are bounded
at one million. The initial world provider must supply values within those
bounds. Direct record writes are trusted setup, not a player-facing API.

`tf-buy world buyer vendor item quantity` accepts bread (item 1, one coin)
or goods (item 2, three coins), quantity 1 through 1000. Buyer and vendor
must be alive, in the same town and at the vendor's workplace. The vendor
must be healthy and in a work or market activity. The vendor workplace must
equal the town market at admission. The town shop owns stock; the vendor receives the buyer's
coins. Market schedules buy bread from the town's registered vendor.
The vendor takes bread from the same shop for personal meals. Children can
receive bread from a living parent at meals. Transfers conserve inventory;
meals consume one bread. Births create neither goods nor coins.

Hunger rises by three per hour, meals reduce hunger by 30. Rest reduces
fatigue by 12; other activities raise fatigue by four. Needs and mood stay
between zero and 100. High hunger or fatigue at midnight causes illness;
ill residents rest except for meals; recovery requires needs below the
illness threshold. Starvation with three
recorded illness days causes death. Age rises by one at midnight; the default
death age is 29200 days. These are explicit simulation parameters, not
claims about historical UO mechanics or biological realism.

`tf-marry world one two` links living unmarried adults in the same town;
parent-child and sibling marriages are refused. `tf-expect-birth world parent
dueDay` schedules a child for a married couple within the next 280 game days.
The initial scenario or world controller schedules family events; the
simulation does not choose couples or generate pregnancies autonomously.
Births retain both parents, inherit the requesting parent's home and town,
and increment each parent's child count. A spouse's death removes both
spouse links. The newborn's display name is `Newborn`, with a unique serial;
persona naming belongs to population setup or the keeper.

## Budgets and acceptance

The world preallocates four town slots, 128 lifetime person slots, 24 schedule
entries and 16 memory indexes per person, and 1024 event slots. A full event
buffer refuses an external event. An hourly step starts with no pending
events and can emit at most two lifecycle events per existing resident.
The game clock refuses after 8760000 hours. Increase bounds only with a new
population and allocation proof. No garbage collector or heap rewind is
required by the simulation.

An hour is O(N + T), including midnight; relationships use direct serial
lookups. Construction is O(capacity). No per-hour record, list or text
allocation is intended. The acceptance run measures construction and every
day's retained heap, including births. The clock driver allocates report text
outside the measured step interval, and clock advancement, births included,
retains nothing per day.

The person record stores 28 machine-word fields (224 bytes) plus its schedule
and memory arrays, the town record 24 fields, and the world wrapper eight
fields (64 bytes). Caller-owned name text is additional when dynamically allocated;
names must remain valid for the world's lifetime and are capped at 64 CCE
characters by admission. Name storage is not copied by admission.

Run from the repository root:

```powershell
pwsh -NoProfile -File apps/uoaix/test-clock.ps1 -Kernel seed/Codex.cdx
```

The runner creates a new evidence directory, compiles with the named kernel,
and runs one serial guest. `result.json` retains kernel, source and artifact
hashes, exits, memory admission readings and the verdict. The output contains
daily Britain and Minoc population, births, deaths, production, sales and
schedule-adherence census over 30 days. The guest checks location, needs,
family links, food and money conservation, and refusals. A complete passing
run requires both town rows for every day and zero retained step heap.

`-Poison` grades initialization with poisoned allocations. `-Sabotage schedule`,
`work`, `birth`, or `death` changes one operation in the scratch unit only;
each must fail acceptance. A compiler error or guest failure is not proof
that an acceptance assertion detects a mutation. These checks are standalone
stage acceptance, not additions to the full battery.
