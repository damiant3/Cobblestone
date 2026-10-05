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
Ties use registry order. An addressed NPC outside range does not redirect
the command to a different nearby NPC. `hello`, `hi`, `hail` and `greetings`
without a name select the closest admitted NPC. Names use the authoritative
TownLive persona or Britain shop registry, not player text. Bankers share
the role name Banker and choose the nearest registered banker.

Named townsfolk lines use TownMind's deterministic fallback with the full
player line as quoted context, subject to existing call/audit budgets.
No speech proposal can move inventory. Banker and vendor greetings are short
role-specific canned lines. The vendor prompt says "Say vendor buy or vendor
sell". Saying buy/sell to a named townsfolk resident
does not redirect the request to a shop.

The bounded distance policy is implemented here. Geometry occlusion is not
tested by this chapter; the four-tile policy is not a claim of full acoustic
or line-of-sight simulation.

## Greetings and recovery

Call `ns-pulse` alongside `tl-pulse` after `tl-advance`. NS owns banker/vendor
approach greetings; TownLive continues to own residents' approach greetings.
The callbacks emit no duplicate mobile draws. Approaching within three tiles
speaks once until departure/re-entry, with a ten-game-minute cooldown per
NPC/player slot. Reconnect preserves cooldowns. Time comes from TownLive's
owner-supplied game seconds and follows the live day-length setting.

```text
nsc-encode speechState buffer capacity -> Result Integer Text
nsc-decode speechState buffer size -> Result Integer Text
nsc-bytes -> Integer
```

NSC1 stores the registered NPC serials, greeting times and player-slot
identities in fixed-size rows. Restore the world, economy, GSI, vendor,
banker registry and TLC1 first; call `ns-bind`, then `nsc-decode`.
The codec requires identical NPC registration order and refuses changed
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
support the chosen classic policy; the 1.25.32 client result is graded in
fester's complete composite by root.

`proofs/NpcSpeechReplay.codex` covers four/five-tile boundaries, upper-case
and name-addressed requests, whisper/emote refusal, word boundaries,
malformed packets, banker access, logged townsfolk replies and NSC1 recovery.
The existing vendor replay covers cart settlement and out-of-range refusal.

Retained state is twenty-four fixed NPC rows and five viewer records, with
five timestamps per row. No per-message data is retained. Matching costs
O(R*N*M) comparisons for R registered NPCs, N speech characters and M name
characters, all bounded by these tables and the C03 packet limit. Nearest
selection and greetings scan only the registered NPC rows using indexed
serial lookup. NSC1 work is linear in the fixed row count. Menu construction
and TownMind keep their existing cost bounds; no economy clone is added.
