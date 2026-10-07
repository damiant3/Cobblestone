# Classic client view

`GameClientView.codex` is a transient presentation adapter, applied after the
composite owner's successful action/commit and after each pulse, including a
pulse with no family reply. It does not change world, item or combat state.

```text
gcv-new capacity radius seasonEnabled season light -> Result GameClientView Text
gcv-environment view season light -> Result Integer Text
gcv-apply view combat items game input selfStatus reply -> Result GameReply Text
```

Allocate one view per sequential listener below its connection/request heap
marks. Use radius18 for the existing shard. Connection or active-character
changes reset the view. `selfStatus` must be the current classic 66-byte 11
packet for this actor. The composite should supply `gv-status-packet` so coin
value remains authoritative; use `gp-status` only for worlds without economy.
Call only after the family has completed its state change. On an empty pulse,
pass `gs-reply [] "client view"`; return None only if the resulting packet list
is empty. Preserve any existing reply's close flag and note.

This state is not a shard codec component: it describes what a connection has
seen, not durable world facts. Reconstruct it empty after recovery. Persist
season/light policy in the owner's world configuration if it changes durably.
Do not serialize its observed serials, status copy or connection identifier.

## Reference behavior and bounds

Port/adaptation of SphereServer Source-X commit
`dd28a0ad53258adb55b548b1bd4df989065a1fe4`, Apache-2.0, with attribution in source
and [license](Sphere-LICENSE.txt): `CClientMsg.cpp` addPlayerSee, addSeason and
addLight; `network/send.cpp` PacketRemoveObject, PacketHealthUpdate,
PacketManaUpdate, PacketStaminaUpdate, PacketSeason and PacketGlobalLight.
The local reference checkout is `D:/Projects/uo-reference/Source-X`.

| Family | Existing shard | Adapter |
|---|---|---|
| Status 11 and A1 hits | Combat query and hit paths exist | Current self status on entry/change/query; own A1/A2/A3 use actual/max fields; other mobiles use percentage hits |
| Mobile 78/77 and remove1D | Combat scene drawn once; monsters have their own range tracking | Inclusive square range, equipped entry draw, movement/body/flags changes, actual deletion removal, re-entry and reconnect refresh |
| Light4F | Fixed bright packet at entry | Owner-selected0..30, changed-value delivery, bright ghost view |
| SeasonBC | Absent | Owner-selected0..4, desolate ghost view, season before forced light refresh |

The adapter consumes family mobile draw/move packets to remember direction
and notoriety, suppresses out-of-range draws, and reconciles all current
mobiles. The 1.25 client culls mobiles outside the sight range: departures
clear the adapter cache without sending1D. Family removal packets for mobiles
still present in the world are suppressed. Actual deletions retain1D, and
re-entry produces a fresh draw. It replaces self status and A1 updates with the supplied authoritative
state. Other requested status packets, combat animation/death, items, speech
and unrelated replies remain family-owned. Ground items and house-interior
visibility are not inferred by this mobile-only adapter. It does not invent
weather, daylight progression, mana consumption or stamina regeneration.

Source-X documents the BC wire layout but does not establish support in every
historical client. The retained Sphere0.52 table calls BC unknown. Therefore
`seasonEnabled` is explicit; root must grade BC on the exact1.25.32 composite
before claiming that client supports it. False suppresses BC while retaining
light updates. The status/light/mobile layouts preserve the established
legacy profile. No standalone client run substitutes for composite acceptance.

## Verification and cost

`proofs/GameClientViewReplay.codex` checks equipped entry, exact fields,
unchanged idle, repeated status requests, scaled health updates, range edge,
silent range departure, suppressed premature family removal and stale family
draw, actual deletion, re-entry, season/light order, ghost
environment and reconnect. It uses a16384-slot indexed world with16000 distant
items and checks unavailable-index refusal. Native packet checks do not prove
client rendering.

Retained memory is O(world capacity): preallocated observed-mobile records,
an eight-byte slot per possible visible mobile, and one66-byte status buffer.
No packet list or world-read record escapes request scratch; fields are copied
into preallocated records. Departure checks walk the previous visible set;
discovery walks world-tile-first/next within the sight square. Each reconcile
costs O(reply packets + previous visible mobiles + sight tiles + local tile
occupants), independent of distant world capacity. Indexed GameMobileBank
equipment serialization uses its fixed visible-layer table and bounded sort.
Idle emits no packets. Live views refuse an unavailable world index rather
than falling back to a capacity scan. The receiver owns scratch reclamation.
Compiler heap/time behavior is unchanged.
