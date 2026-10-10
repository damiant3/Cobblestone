# Classic NPC speech

`GameNpcSpeech` supplies the composite's C03 speech route for Britain's five
shops, three TownLive residents and up to sixteen registered bankers.

```text
ns-bind townLive britainShops bankState -> Result NpcSpeech Text
ns-handler speechState
ns-pulse speechState
```

Allocate the binding below request scratch after population or shared-state
restore. Route `ns-handler` before `gmb-handle`, `bb-handler`, `tl-handler`
and the core speech fallback. The handler owns every validated authenticated
C03 packet, including ordinary chat; the handler emits the player's normal
speech echo and optional NPC replies. No new opcode is needed. Malformed
length, ASCII body, mode, font or color refuses before quote or bank changes.
Other opcodes return None and retain the existing routing order.

## Listener and keywords

Normal speech and yelling reach NPCs within four tiles, using Chebyshev
distance and a sixteen-unit height limit. Whispered NPC commands reach one
tile. Emotes do not trigger NPC actions. Both speaker and listener must be
living mobiles. The four-tile vendor range also applies to the existing
cart checkout, so an offered trade can complete from the same position.
Bank opening still requires the existing two-tile banker access policy.

Matching ignores ASCII case and uses word boundaries: `BUY`, `vendor buy`,
`please, vendor sell!`, and `Britains Premier Provisioner, buy` work;
`buying` and `seller` are not buy/sell commands. A line containing both
buy and sell opens neither menu. No client-supplied keyword IDs select goods.

Resident names and full shop names take precedence over nearest-listener
selection. Shop role aliases are provisioner, blacksmith, tailor, baker and
healer. `vendor` selects a nearby shop; `banker` selects a nearby banker.
A name or alias shared by several rows (the composite lineup copies every shopkeeper) picks the nearest; ties use registry order. An addressed NPC outside range does not redirect
the command to a different nearby NPC. `hello`, `hi`, `hail` and `greetings`
without a name select the closest admitted NPC. Names use the authoritative
TownLive persona or Britain shop registry, not player text. Bankers share
the role name Banker and choose the nearest registered banker.

## The canned library

Every NPC line comes from `NpcLines.codex`, the canned library (UOAIX-66,
ruling R2: no relay, no provider key, no live call). A row is keyed by kind
(shopkeeper, banker, townsfolk), town, role, topic (greeting, trade, work,
news, chat), opinion (sour, even, glad) and mood (glum, even, cheerful,
alarmed); an empty town or role and the value 9 are wildcards. The pick is
the matching row with the most specific fields, and equally specific rows
rotate by a variety number (the persona's request count for townsfolk, the
speaker and clock for shopkeepers and bankers).

- **Topic** is the first of trade, work, news and greeting whose keywords the
  player's line contains (`ns-topic`), otherwise chat. An approach greeting is
  topic 0.
- **Townsfolk keys** use the persona's town and job, mood from `TfPerson.mood`
  in thirds, alarm 600 and over as alarmed, and opinion from the townsfolk
  network: trade talk reads opinion axis 0, work talk axis 1, other topics
  their mean, with 250 either side of zero as a held opinion. The network
  writes each resident's two axes and alarm into `TownLive.feel` on every
  think (`tnv-feel`), and TNL1 restore rewrites them.
- **Shopkeepers and bankers** key on kind, Britain and the shop role; their
  opinion and mood are unknown, so only wildcard rows match them.
- **`ns-bind` refuses an invalid library**: every shopkeeper greeting must say
  both "vendor buy" and "vendor sell", every banker greeting "say bank", and
  each kind must hold a fully wildcard row for every topic, so a valid key
  never misses.

A townsfolk reply still passes TownMind's budgets and is audited as a
model-unavailable fallback; the reply note is `townsfolk canned line` on a
library pick and `townsfolk named fallback` when the library misses and the
persona's configured fallback is spoken. A shopkeeper or banker miss speaks the
row's fixed greeting. `NlLibrary` counts hits and misses. No speech proposal
can move inventory. Saying buy/sell to a named townsfolk resident does not
redirect the request to a shop.

The library ships in the image. Moving its rows into a database table waits
for the database backend (`Database.md`).

The bounded distance policy is implemented here. Walls block an NPC's line
of sight, except a banker's (Damian, 2026-10-06: "line of sight for npcs should block through walls, except bankers."):
a shopkeeper, resident, miner or farmer behind a wall neither hears, answers
its name nor greets the player, and a resident's work show and overhead
lines reach only a player it can see (UOAIX-68). The line is `tl-sight`: the
ranged-combat ray (`cb-ray`, tiledata Window and NoShoot block it) cast over
the Britain map the residents walk (`cg-sight-map`). A banker (`ns-heard`,
kind 2) hears and greets by distance alone.

## Greetings and recovery

Call `ns-pulse` alongside `tl-pulse` after `tl-advance`. NS owns banker/vendor
approach greetings; TownLive continues to own residents' approach greetings.
The callbacks emit no duplicate mobile draws. Approaching within three tiles
speaks once until departure/re-entry, with a ten-game-minute cooldown per
NPC/player slot. Reconnect preserves cooldowns. Time comes from TownLive's
owner-supplied game seconds and follows the live day-length setting.

**Ruling (Damian, 2026-10-08):** "make them say greetings only if you are in or near their shop with them, but not randomly on the road or at home. the convo should be appropriate for time of day, shop opening or closing state, etc." An approach greeting fires only when the player and the NPC are both inside or at the door of the NPC's own workplace; an NPC on the road or at home does not greet. The greeting row is chosen by time of day and by the shop's open or closed state.

**Ruling (Damian, 2026-10-08):** "an npc should "announce" its state only once per player. i don't want to see the same guard saying repeating the same assignment over and over." An NPC announces a given state (an assignment, a duty, a shop state) once, whoever hears it, and again only after the state changes; no per-player record is kept (Damian, the same day: "it also can be once per npc.. that is we don't need to maintain a list of npcs->pcs who have announced and not. if the npc announces and one player sees it, that is enough.").


**Ruling (Damian, 2026-10-08):** "npcs should comment on their economic activities occasionally. "oh nice pile of ore" or "that gorget is one of my best!" or "those ingots were expensive. i guess I'll have to raise prices at the smithy"". An NPC now and then says a canned line about the economic event it just took part in (a harvest, an item it made, a purchase and its price), naming the goods.


```text
nsc-encode speechState buffer capacity -> Result Integer Text
nsc-decode speechState buffer size -> Result Integer Text
nsc-bytes -> Integer
```

NSC1 stores up to 40 registered NPC rows, keyed by serial and kind (a composite shopkeeper is also registered as a banker), with greeting times and player-slot
identities in fixed-size rows. Restore the world, economy, GSI, vendor,
banker registry and TLC1 first; call `ns-bind`, then `nsc-decode`.
Rows match by key, not position, because the composite re-registers bankers in a different order after a restore. Banker registration is a set: the codec compares distinct stored keys with distinct bound keys, and a duplicated key restores to its first row. The codec refuses a missing or extra key, changed
serial bindings, bad checksums and future saved clocks. Connection/inside
masks reset. A new character occupying a slot clears the old character's
cooldowns. Names and roles come from the authoritative restored registries.
Include NSC1 in the same transaction as TLC1, world and vendor state.
No speech or menu reply publishes before the encompassing owner commit.

## Sources, cost and grade

The name/title, nearest-listener and first-contact shape follows Source-X
commit `dd28a0ad53258adb55b548b1bd4df989065a1fe4`,
`CCharNPCStatus.cpp::NPC_OnHearName`,
`CClientEvent.cpp::Event_Talk_Common` and
`CCharNPCAct.cpp::NPC_OnHear`. Apache-2.0 attribution is retained in
`Sphere-LICENSE.txt`. The four-tile pre-AOS range is corroborated by
ServUO `Scripts/Mobiles/AI/VendorAI.cs::OnSpeech`'s pre-AOS branch; that
numeric policy is used without copying the implementation. The existing
ServUO license is retained in `ports/ServUO-LICENSE.txt`. These references
support the chosen classic policy; the 1.25.32 client result is graded on
the complete composite.

`proofs/NpcSpeechReplay.codex` covers four/five-tile boundaries, upper-case
and name-addressed requests, whisper/emote refusal, word boundaries,
malformed packets, banker access, logged townsfolk replies and NSC1 recovery,
line of sight (a wall silences Nell and not the banker),
and the library: validity and its refusals, most-specific selection, town
culture, mood and alarm buckets, rotation, a counted miss, topic
classification, and the network outlook and alarm choosing a resident's line.
A build that ignores the outlook fails exactly the outlook arm.
`proofs/TownNetworkLiveProof.codex` grades `TownLive.feel` against the
network after live rounds and after TNL1 restore.
The existing vendor replay covers cart settlement and out-of-range refusal.

Retained state is forty fixed NPC rows and five viewer records, with
five timestamps per row. No per-message data is retained. Matching costs
O(R*N*M) comparisons for R registered NPCs, N speech characters and M name
characters, all bounded by these tables and the C03 packet limit. Nearest
selection and greetings scan only the registered NPC rows using indexed
serial lookup. NSC1 work is linear in the fixed row count. Menu construction
and TownMind keep their existing cost bounds; no economy clone is added.

The library is built once per `TownLive`; a pick is two linear scans of the
rows and retains nothing. Topic classification calls
`ns-has` for at most 42 keywords, and each `ns-char` goes through `to-unicode`
(about a kilobyte), so `ns-topic` runs under a heap mark and keeps only its
Integer.
