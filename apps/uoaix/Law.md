# Native law admission

`Law.lw-new worldTable` binds the retained authoritative `WorldRecords`
table. It allocates bounded native law metadata; it creates no mobile and
copies no world. All mutations require one serialized owner. The caller
retains the world and law records below request scratch and commits each
complete action under [Database.md](Database.md) before publication.
The Codex DB backend, live session binding, combat and pathfinding remain
integration work. No `GameNet` changes or live-client acceptance are included.
For an action spanning several calls, use a private candidate or complete
caller-owned undo: refusal of a later call does not roll back earlier calls.

## Deeds, witnesses and memory

`lw-deed w verified sourceEvent offender kind town` records an admitted
theft (1), assault (2) or murder (3). The offender must be a living world
mobile; towns use stable ids 1..16. The server supplies a unique positive
source-event id, at most 1000000000000. Replays refuse, including after a
sentence ends. The verified decision establishes the actual deed, offender,
jurisdiction and crime classification, never a player's or model's claim.

Every admitted deed has a retained row, including unwitnessed deeds. The
classic notoriety adapter must consume each source event exactly once in the
same transaction, independently of reporting. This core does not invent a
replacement notoriety scale, name colour or title. Its deed rows are the
native input contract for that missing adapter. The stage L phrase
"unwitnessed theft raises nothing" means no known crime or guard response;
section 5's explicit later ruling still requires the notoriety effect.

`lw-observe` adds a witness to a deed. The server supplies the witnessed
event decision, identity, NPC/player classification and willingness to tell.
That decision must establish presence and perception at the original event,
not proximity measured later. The stored deed coordinates aid the adapter;
they do not prove line of sight. A witness cannot be the offender or be
recorded twice for the same deed. The retained NPC witness/deed relation is
its criminal memory, queried by `lw-remembers`; it survives silence, arrest,
sentence completion and a criminal winning combat. The live townsfolk memory
adapter must expose this relation to service, warning and testimony choices.
It is not yet inserted into `TfPerson.memories` or model context.

Observation alone never makes a crime known. `lw-report` requires a living,
willing witness and a verified act of telling. `lw-willing` records a later
verified willingness choice; withdrawal does not erase an earlier report.
Player testimony can make a crime known but is not presented as NPC memory.

## Guards and movement

The keeper provisions living guard serials and jurisdiction with
`lw-add-guard`. `lw-call` requires a verified audible call by a living,
reporting, currently willing witness. It chooses the nearest idle living
guard of that town within twelve tiles of the caller. No eligible guard
returns zero without changing any position or assigning a response. Ties use
guard registration order. The offender is never selected as its own guard.

Twelve-tile hearing is a simulation default, not a claim
about the historical client. Native distance is squared Euclidean distance
in x/y, with equal z required. The live adapter must additionally validate
acoustics, floor connectivity, current perception and path reachability.

A successful call assigns pursuit. `lw-capture` makes an arrest offer only
when the assigned living guard is within three tiles of the living offender.
No movement occurs during the call, capture or reinforcement selection.
`lw-step` accepts exactly one adjacent tile step, including diagonals, with
unchanged z and caller-validated path admission. It rejects a blocked step,
teleport, dead mobile or jailed offender. The owner supplies collision,
corner-cutting, movement timing, stairs and authorization; this primitive is
not a pathfinder or a client movement endpoint.

## Submit, fight and jail

Case status is open 0, arrest offer 1, combat 2, escort 3, jailed 4 or served 5.
Guard state is idle 0, pursuit 1, arrest offer 2, combat/assistance 3 or escort 4.
All choices recheck the living arresting guard and three-tile range.

`lw-submit` takes a verified choice by the offender and requires a configured
town jail. It assigns escort, without teleporting either mobile. The owner
walks the guard and prisoner using admitted steps, enforcing escort control.
`lw-arrive` requires the prisoner at the configured jail x/y and the guard
still within three tiles on the same z. Arrival starts the sentence and frees
the guard. Jail containment, floor/door policy and controlled escort routes
are the live owner's obligations; the native jailed flag blocks `lw-step`.

`lw-jail-set` and `lw-sentence-set` require verified keeper authority. The
initial terms are one game hour for theft, six for assault and 24 for murder.
Those are simulation defaults. A sentence is snapshotted when the deed is
recorded; editing the table cannot rewrite an existing term. Valid terms are
1..8760 game hours. `lw-hour` advances monotonically up to 1000000000000.
`lw-release` changes jailed to served only at or after the due hour.

`lw-fight` takes a verified offender choice, marks the arresting guard in
combat, and assigns the three nearest other idle living guards in the same
town. When fewer than three are available, it assigns all available guards.
Distance is from each guard to the offender, with the same deterministic tie
rule. It returns the assistant count, excludes occupied guards and other
towns, and changes no position or health. Reinforcements approach on foot
through the live pathing owner. They do not get an instant attack.

`lw-won-fight` accepts the verified combat owner's result, clears guard
assignments and reopens pursuit eligibility. The crime stays known and every
witness record survives. It does not resurrect guards or grant immunity.
Normal combat, defeat/surrender outcomes, consensual duels, subdual damage,
drunkenness and classic notoriety presentation are not implemented here.
Guard death, abandonment or disconnected arrest choices need a live owner
recovery policy before production use; no automatic timeout is invented.

## Storage, cost and proof

The native capacity is 128 lifetime deeds, 256 lifetime witness links,
32 guards and 16 jail locations. Full storage refuses before effects and
never overwrites evidence. Logical rows contain respectively 11, five, four
and three scalar fields; the sentence table has three integer cells.
Codex DB migration must retain source-event uniqueness, historical witness
links and snapshot sentence terms in the same transactions as world effects.
No standalone disk codec or second durable store is added.

Deed admission scans at most 128 source ids; witness insertion/memory scans
at most 256 links. A call scans 32 guards and combat selection makes at most
three such scans. Steps scan 128 custody rows, then use constant-time mobile
updates without changing containment. World reads allocate bounded temporary
records; reclaim request scratch after consuming results. No operation grows
a retained list or history beyond the initial allocation. Compiler heap/time
behavior is unchanged.
On 2026-10-04, normal and poison proofs measured 26,104 retained bytes for
law construction, excluding the bound world table. The scripted walk retained
zero additional bytes with its per-step scratch boundary.

`proofs/LawProof.codex` grades unwitnessed and unwilling cases, NPC memory,
player reports, no guard in earshot, hearing and capture boundaries, rejected
teleport/blocked steps, escort and sentence timing, nearest-three assistance,
known crime after a won fight, keeper authority, replay and full deed storage.
An additional arm grades distance ties, dead/occupied guards, different z
and fewer than three available assistants.
Normal and poisoned allocation runs must print `UOAIX LAW failures=0`.
