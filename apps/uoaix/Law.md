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

## In the composite

The composite's law world is the civic one (`CivicLive.law`, CVC1). A player's
theft attempt (`theft-hook`, every attempt that reaches the roll), landed blow
(`swing-hook`) or kill (`death-hook`) against an innocent is a deed of
Britain, town 1 (`cg-crime`). An innocent is a player or a town NPC (a
shopkeeper, resident, banker, the mayor or a guard) who is not marked. A theft
or an assault opens one case at a time per offender; every murder is a deed.
Every living mobile of those kinds, and every player, standing within 12 tiles
with a clear line to the offender witnesses the deed (`cg-witnesses`). An NPC
witness three rounds drunk is unwilling: it remembers the deed (refusal, warning,
testimony) but neither tells nor calls. Any other NPC witness tells at once and calls the nearest guard of Britain within 12 tiles
of itself, who walks to the offender and makes the arrest call. A player
witness calls by saying `guards`.

Notoriety is the client's classic byte, overriding what the emitter sent
(`GameClientView.noto-hook`, `cg-notoriety`): 6 (red, murderer) at five
murder deeds, witnessed or not; 4 (grey, criminal) while a known case is not
jailed or served, and for every deed, witnessed or not, through the deed's
game hour and the next (R3: every crime moves notoriety); otherwise the
emitter's value. A case's `due` holds the deed's game hour until `lw-arrive`
replaces it with the release hour. Attacking or robbing a marked
mobile is no deed. The 128 case rows are shared by the whole world; a full
table records nothing more until a case retires (`lw-retire`, below), and the
composite numbers each deed's source past the highest live one
(`lw-next-source`).

Britain's jail cell is an indoor standing tile of Guard Post North (one under a roof
collider, `cg-indoor`; an outdoor cell moves at boot unless someone is jailed), set once per world
(`cg-jail`). Offered arrest, the offender says "submit" or "fight", or strikes
the arresting guard (`cg-arrest-answer`, `cg-struck-guard`). On submit the
guard walks to a tile beside the cell (`cv-escort`) and asks the prisoner to
step in; the prisoner walks there and stepping onto the cell starts the term
(`lw-arrive`). The term ends at its due game hour (`lw-release`, each town
step). The cell's walls are Guard Post North's: while anyone is jailed, every
closed door within 4 tiles of the post is barred (`wd-bar`), and the bars lift
when nobody is (`cg-jail-walls`). A barred door can be picked. A prisoner more
than 3 tiles from the cell has broken out: the case reopens, known, and the
offender is grey again. On fight, the arresting guard
and every other free guard of Britain, up to three, attack (`cb-select`) and
keep attacking each town step; when the offender or every assigned guard
falls, the fight ends (`lw-won-fight`) and the crime stays known. The town has
two guards.

A shop refuses to trade with a player whom its keeper, or a resident working
there, witnessed committing a deed (`cg-shun` through `GvWorld.shun`, on the
click and the spoken buy/sell path): "I saw what thou didst. I will not trade
with thee." A shopkeeper or resident who witnessed the approaching player's
deed greets with a warning instead (`ns-greet`, and `TownLive.warn-hook` bound
to `cg-warns`). The memory is the witness row and never fades.

Consensual duels (R3): a player saying "I challenge thee ... to a duel"
challenges the nearest other player within 8 tiles (`cg-duel-said`), who
accepts by saying "I accept". Between duellists no blow or kill is a deed.
A bare-handed blow subdues (`GameCombat.subdue-hook`, `cg-subdue`): it stops at
1 hit point, knocks the loser down, stops both fighters' attacks and ends the
duel. Armed blows follow the normal rules, and a death ends the duel. With no
player near, Silas the gambler answers a challenge within 8 tiles: after two
rounds of ale he accepts and swings first, sober he refuses
(`cg-duel-silas`). Before Silas, the nearest resident within 8 tiles in sight
answers (`cg-duel-folk`): after two rounds the resident accepts and swings first,
sober the resident refuses. A player who says "buy thee a drink" (or "a round")
beside a resident in sight pays the tavern Silas's price and the resident drinks one
round, at most 4, one wearing off each game hour (`cg-round-said`); its name reads
"(merry)" from one round and "(drunk)" from three. A resident three
rounds drunk challenges a player within 3 tiles in sight aloud and spends a round on
the boast (`cg-folk-dare`); "I accept" starts the fight with the resident swinging
first, and the challenge lapses past 8 tiles. Duels and rounds are not saved.

Testimony: a player who says "witness", or asks what an NPC saw, to a
shopkeeper, resident, the mayor or a guard within 3 tiles hears the deeds that
NPC witnessed, the two latest ("I saw <name> commit thieving."), or "I have
seen no crime." (`cg-testify-said`).

## Deeds, witnesses and memory

`lw-deed w verified sourceEvent offender kind town` records an admitted
theft (1), assault (2) or murder (3). The offender must be a living world
mobile; towns use stable ids 1..16. The server supplies a unique positive
source-event id, at most 1000000000000. Replays refuse, including after a
sentence ends. The verified decision establishes the actual deed, offender,
jurisdiction and crime classification, never a player's or model's claim.

`lw-confine w verified offender town hours` records a GM's jail order, kind 4:
a case jailed at once at the town's cell for 1..8760 game hours, not known,
with no witness and no guard, and refused for a dead or already jailed
offender. `lw-free w offender` serves every jailed case of the offender now.
LSC1 version 2 admits kind 4; version 1 saves still load.

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

**Guards are ordinary NPCs (Damian, 2026-10-08: "guards are not immune, do not have granted perfect skills. they are npc like all"; "they do not appear, they run like a normal npc"; "and only if in earshot of the call", "which is about 2 screens worth of radius").** A guard is mortal and fights with the skills it holds, like every NPC; no guard is made immune, and no fight grants a guard a skill. A called guard runs to the call on foot like any NPC, and only a guard within hearing of the call answers it: 36 tiles, root's reading of two screens as twice the 1.25 client's 18-tile view range; no guard appears beside a creature. A guard that arrives engages the monster and fights it to the death, its own or the monster's, never surrendering or retreating (Damian, the same day: "when the guards arrive to kill a monster, they engage the monster and try to kill it, fighting till death and not surrendering or retreating"). Guards coordinate: every guard in the fight attacks the one creature the first guard to land a blow chose, until that creature dies, where they can reach it (Damian, the same day: "guards are smart and coordinate their attacks on a single creature if possible until it dies. that choice is made by the first to land a blow"). A guard joining the focus says so aloud, naming the first guard and the creature, then switches targets (Damian: "`"Nice hit Carl, let me help you kill that skeleton`" and they would change targets and engage the same skeleton"). Guards hold no granted gear, but each guard buys armor and weapons from Britain's shops with its own wages and wears and wields what it bought (Damian: "guards can, however purchase and use armor and weapons they can purchase with their wages").

The keeper provisions living guard serials and jurisdiction with
`lw-add-guard`. `lw-call` requires a verified audible call by a living,
reporting, currently willing witness. It chooses the nearest idle living
guard of that town within 36 tiles of the caller. No eligible guard
returns zero without changing any position or assigning a response. Ties use
guard registration order. The offender is never selected as its own guard.

The 36-tile hearing is Damian's ruling above. Native distance is squared Euclidean distance
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

The native capacity is 128 deeds, 256 witness links, 32 guards and 16 jail
locations. Full storage refuses before effects. `lw-retire` removes a served
case (status 5), and a deed nobody reported (status 0, not known, no guard),
40 game hours after its due hour, with its witness rows, compacting both
tables in place and renumbering surviving case ids in witness and guard rows;
it never touches a case that is open, known, in custody or jailed. Murder
counts therefore decay as classic UO's long-term count does. The composite
runs it from the town step while 96 or more cases or 192 or more witness rows
are in use; LSC1's layout is unchanged. Logical rows contain respectively 11, five, four
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
behavior is unchanged. The scripted walk retains no additional bytes across
its per-step scratch boundary.

`proofs/LawProof.codex` grades unwitnessed and unwilling cases, NPC memory,
player reports, no guard in earshot, hearing and capture boundaries, rejected
teleport/blocked steps, escort and sentence timing, nearest-three assistance,
known crime after a won fight, keeper authority, replay and full deed storage.
An additional arm grades distance ties, dead/occupied guards, different z
and fewer than three available assistants.
Normal and poisoned allocation runs must print `UOAIX LAW failures=0`.
