# Britain civic NPCs

`CivicLive` supplies the visible mayor and two foot patrols for the composite.
The shared `LawWorld` remains the authority for crimes, witnesses, guard
assignment and arrest offers. No player speech creates a deed or a witness.

## Installation and owner binding

```text
cv-populate townLive lawWorld cachedMap verifiedRoyalActor
  -> Result CivicLive Text
cv-handler civicState
cv-advance civicState cachedMap (Just decoration) gameSeconds budget
  -> Result GameReply Text
```

Install once on a private new-world candidate after Britain shops and
TownLive. The supplied law world must share the same indexed WorldTable and
have no registered guards. The initializer creates the mayor and two guards,
registers the guards through `lw-add-guard`, and produces all uniforms and
swords through the existing harvest/recipe engine. Production uses the
server-verified royal actor's pool; the equipment remains royal property in
that ledger, with exact physical serial/quality/wear/cost bindings in CVC1.
No purse grant, finished-item spawn or new recipe is added. A failed
initialization requires discarding the entire candidate.

The mayor starts at the south guard post near the client arrival area. This
is an explicit installation placement, not a claim of a historical mayor
spawn. Guard starting regions follow UOX3's Britain guards: south
1422..1442/1695..1698 and Guard Post North 1510..1516/1611..1616.
The source is `spawn_felucca_town_britain.dfn` at commit
`4560ae841bac898817143d7aa95ce59f47ab98e0`; attribution is retained in
`ports/UOX3-LICENSE.txt`. Four route waypoints per guard are installation
policy within the existing Britain cache. Real map reachability is part of
the complete-composite client grade.

Route `cv-handler` before `ns-handler`, since NS owns otherwise-unhandled
C03 speech. Existing C03 and fixed-five-byte C09 framing are reused.
Single-click names identify the mayor and guards. A nearby `mayor` line
gets the current number of known, unserved cases. This visible mayor
binding conveys no player or model authority over civic finance. Existing
TownGovernment appointment, treasury and payroll APIs retain their own
owner bindings; this adapter does not replace those records.

GameClientView renders these world mobiles and their GSI equipment. The
mayor serial is `state.mayor`; patrol serials are `state.guards[i].serial`.
The composite's combat owner may register those existing serials with its
normal NPC policy; no duplicate mobile should be spawned. The adapter
does not install invulnerability, instant killing or a combat damage rule.

## Patrol and crime response

Call `cv-advance` once per owner simulation update, using the same game
seconds as `tl-advance`, with budget 1 through 32. Movement takes 12 game
seconds per adjacent tile; failed paths retry after 60 game seconds.
The day-length setting therefore rescales patrols with the rest of the town.
The owner advances both components before checkpointing their shared clock.

Patrols share TownLive's bounded TlPath workspace under the serialized owner.
Each guard retains a 256-direction route hint and rechecks every actual step
against terrain, decoration doors and indexed living-mobile occupancy.
`lw-step` admits the adjacent horizontal movement; the same candidate then
applies the terrain-validated height. Neither intermediate state is published.
Unlocked doors use WorldDecoration; include WDS1 in the same commit. There
is no teleport when a route is blocked, a target is outside the cache, or
the path expansion budget is exhausted.

The authoritative theft/combat owner records deeds and observed witnesses
through `lw-deed` and `lw-observe` in the same transaction as the original
action and notoriety update. The source-event ID must be unique and the
witness decision must establish perception at that event. CVC1 retains
unwitnessed deeds and NPC criminal memory. Reading a player's accusation
does not establish either fact.

An authenticated living player's `guards` call searches only that player's
existing willing witness links. The selected idle guard must satisfy the
law core's twelve-tile/same-height hearing rule and a clear direct walk-map
line. This conservative hearing check refuses intervening walls/closed doors;
it does not search through a building for an alternative acoustic route.
Whispered calls use a one-tile hearing limit.
Only then does the actual call report the witnessed case and invoke
`lw-call`. Calls without evidence or an eligible audible guard change no
position and create no case. Emotes do not call guards.

A pursuing guard walks toward a clear tile beside the offender. On reaching
the core's arrest-offer range, `lw-capture` changes the case and the guard
says that a reported crime requires an answer. Arrest-offer and escort states
wait for the law/combat owner's next choice. Submission UI, prisoner escort,
jail containment and combat adjudication remain owner integration work,
as specified by `Law.md`; this unit does not claim those client grades.
Assigned assistance guards also approach on foot. A dead or removed guard
stops walking; no replacement is created and no evidence is erased. The
owner remains responsible for reassignment after a guard dies.

The returned movement/speech packets are output hints for the composite's
spatial delivery. Deliver only to relevant nearby viewers and only after
the encompassing commit. No function sends raw network data.

## Recovery and cost

```text
cvc-encode civicState cachedMap buffer capacity -> Result Integer Text
cvc-decode buffer size restoredTownLive cachedMap -> Result CivicLive Text
cvc-bytes -> Integer
```

CVC1 saves the mayor binding, two patrol routes/cursors/deadlines, equipment
provenance and an embedded LSC1 law checkpoint. Restore world, currency,
GSI, vendor, TLC1 and decoration bindings first. Decoding constructs a new
law world over the supplied shared WorldTable and reuses the restored
TownLive search workspace. Do not rerun population on recovery. Saved civic
time cannot exceed the supplied restored TownLive time. Expose the decoded
`state.law` to the other crime owners; retaining a second old LawWorld would
split authority. Historical dead NPC serials may remain in law evidence.

LSC1 also has standalone `lsc-encode law buffer capacity` and
`lsc-decode buffer size world` functions. All fixed cases, witnesses, guards,
jails and sentence terms are encoded; counts, source uniqueness, witness
relations, assignment bounds, Boolean encodings and unused zero rows are
validated. No deed is recreated through admission during load. This keeps
source-event replay refusal and sentence snapshots intact.

The caller supplies disjoint owned output storage and reclaims decode scratch
on failure. Clothing/world changes must keep the recorded royal material
attribution consistent in the encompassing transaction. Normal corpse
containment is allowed; erasing a bound equipment serial requires updating
that material/provenance state first.

Retained adapter state is two path buffers, eight waypoints, eleven equipment
rows and scalar bindings. The existing TlPath workspace is shared, not
duplicated. LSC1 allocates only the law core's bounded metadata on recovery.
Patrol work is capped by the caller's step budget; path cost is the existing
TlPath bound, with indexed occupancy and decoration rebuild costs. Law steps
scan bounded custody rows. Codec validation uses bounded quadratic uniqueness
checks and a case/witness cross-check, with no full world or economy clone.

`proofs/CivicLiveReplay.codex` covers produced outfits, bounded adjacent
patrol movement, mayor speech, evidence-only guard calls, on-foot pursuit,
arrest offers, CVC1/LSC1 recovery and forged unused-row refusal. The replay
uses a synthetic clear map. Root grades visible routes, mayor and guard
responses only in fester's complete composite.
