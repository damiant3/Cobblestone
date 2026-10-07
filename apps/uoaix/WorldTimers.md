# World timers

`WorldTimers.codex` is the composite's single fixed-capacity timer wheel.
`wtm-new capacity slots now` allocates the entry pool (40 bytes per entry)
and the one-tick buckets once; no wheel operation allocates after that. The
composite owns a wheel of 256 entries over 256 PIT-tick buckets (about 2.56 s
per revolution) and drains it in every pulse with the pulse tick and in the
server's world tick once per second (`gl-tick`, `cp-tick`), clients or none.
`fired` counts the entries handed out since boot.

- `wtm-add deadline kind key` answers the entry, or -1 when the pool is full.
  A deadline at or before the last drained tick fires at the next tick, never
  inside the drain under way.
- `wtm-set` replaces the pending timer for a (kind, key) and `wtm-clear`
  removes it. Both scan the pool, O(capacity), and run on player actions only.
- `wtm-next tick` unlinks one due entry, leaves its kind and key in the
  wheel's `kind` and `key` fields and answers the entry, or -1 when nothing is
  due. A drain visits at most `slots` buckets; an entry more than one
  revolution out is passed over once per revolution.
- Kind 0 marks a free entry and is refused. The composite refuses a kind it
  does not dispatch (`world timer kind`).

| kind | owner | key | rule |
|---|---|---|---|
| 1 | `WorldDecoration` door autoclose | decoration row | ServUO BaseDoor: close 20 s after opening; an occupied doorway defers another 20 s; a door closed by hand cancels |
| 2 | `CompositeLabor` resident shift | resident index | every game hour (`clb-hour-ticks`): pay 1 copper when at work, direct to work in a scheduled work hour, re-add; re-added at boot |
| 3 | `GameCombat` hit-point regeneration (`cb-regen-fire`) | player serial | Source-X: one hit point per 40 s, the living only. One entry per player (`wtm-set`); the player's pulse arms it when unarmed. A fire for a dead player, or for a player who is not the active session, disarms and keeps the overdue tick, so that player's next pulse regenerates at once, as the per-pulse path did. A combat state with no wheel (`wheel = None`, every server but the composite) keeps the per-pulse `cb-regen`. |
| 6 | `Magery` actor (`mgs-arm`, `mgs-fired`) | actor world slot | one entry per tracked actor at its earliest deadline (stat modifier expiry, night, protection, mana regen, poison tick, cast or target timeout); `mgs-track` arms it, a visit re-arms it, untracking clears it, and `mgs-attach` arms every actor restored before the wheel was bound. A fire queues the slot and the next magery pulse visits it. The pulsing player's own actor is visited every pulse, so a cast fizzles on movement and death clears effects at once. A full pool leaves the actor unarmed and the next pulse visits every unarmed actor. A magery state with no wheel keeps the per-pulse walk over every tracked actor. |
| 7 | `CompositeGameRules` logout linger (`cg-depart`, `cg-departed`) | character serial | Source-X ClientLinger, 300 s in its shipped sphere.ini: a disconnect or logout arms the entry from the wheel's last drained tick; its fire marks the body departed (concealed byte 2) so every view drops it; world entry (`cg-return`) clears the entry and restores the body. A boot marks every account character departed until its player enters. `proofs/CompositeLingerProof` grades the linger, the removal, re-entry, a cancelled linger and the boot state; removing the cancel turns it red. |
| 4 | `CompositeFighter` errand | 0 | one errand step (`cgf-step`), re-added 400 ms out on an errand and one game hour out at home (`cgf-delay`); re-added by the first world tick after boot (`cg-errand-arm`) |
| 5 | `CompositeBritishErrand` Lord British's errand | 0 | one errand step (`cbe-step`), re-added 400 ms out while walking, 3 s out while mining and 1 s out at home or while his own session drives him (`cbe-delay`); not re-added once the errand ends; re-added by the first world tick after boot (`cg-british-arm`) |

The wheel is not persisted. A door open at a restart stays open until used.

## Everything timed goes on the wheel (Damian, 2026-10-06)

"lets also be getting more things on the wheel. i think that is key to any
scalability."

Root's reading, binding until Damian corrects it: a pulse's cost follows the
number of DUE events, never the number of objects. Every new timed behaviour
(regeneration, cooldowns, NPC errands, spawns, decay) schedules a wheel entry
instead of storing a `due` that a pulse scans, and the families below move onto
the wheel (UOAIX-29). A family that persists its timers saves remaining time and
re-adds wheel entries at boot.

## Timed state not on the wheel (surveyed 2026-10-05)

| family | where the time lives | clock |
|---|---|---|
| composite save | `cs-timer` in `CompositeStore`, `last-save`, 60 s; one O(1) compare per pulse, stays (root, 2026-10-06) | HPET |
| monsters | `GameMonster` slot `due`; persisted as remaining time by `GameMonsterCodec` | combat tick (PIT) |
| combat swings | `GameCombat` actor `due` | combat tick (PIT) |
| magery walls | `MageryTick` `mgt-walls`, wall `until` over 32 fixed slots per pulse (actor timers are kind 6) | magery tick (PIT) |
| vendor quotes | `GameVendor` player `expires`, checked when a cart arrives; expiry, fires nothing | PIT |
| greeting cooldowns | `GameNpcSpeech` row `last`, checked when a player approaches; expiry, fires nothing (persisted in NSC1). The approach scan over the speech rows runs only when the viewer's place or life changes (`ns-place`); `NpcSpeechReplay` grades an unmoved pulse doing no scan | town game seconds |
| world spawns | `GameWorldSpawn` `deadlines` buffer and slot `due`; a viewer's pulse (`gws-view`) visits the slots that viewer has drawn, the slots filed in the 64-tile sectors its view touches, and the slots at war; the spawn pass (once a second) sweeps every other slot once (`gws-sweep`), so an idle spawn far from every viewer acts at most once a second (UOAIX-42) | combat tick (PIT) |

Not world events: TCP retransmit and close timers (`codex/os/net/NetIdle.codex`,
transport state; its 100 ms HPET wake drives the pulse) and the `ShardServer`
link tick (a separate entry).

## Proof

`proofs/RegenWheelProof.codex` runs the per-pulse and wheel regeneration over
the same 4401-pulse timeline (wounds, death, resurrection, an inactive gap) and
requires the same firing count and an identical hit-point trace, then requires
pulses with nothing due to do no regeneration work; removing the inactive-player
disarm turns it red.
`proofs/MageryWheelProof.codex` runs magery per pulse and on the wheel over the same 3000-pulse timeline (a pulsing hero regenerating mana, a non-pulsing NPC with poison, a stat bonus, protection and night) and requires an identical trace, then requires the wheel to visit one actor fewer per pulse between the NPC's deadlines; removing the re-arm after a visit turns it red.
`proofs/WorldTimersProof.codex` grades exact firing, replace and clear by key,
pool exhaustion and reuse, a drain across many revolutions, past deadlines,
adds during a drain, and 64 self-rescheduling timers over 38000 ticks firing
the computed count with zero heap growth. Compare its complete output with
`WorldTimersProof.expected`. `proofs/DecorationReplay.codex` grades door
autoclose, the occupied-doorway deferral and cancellation by hand.
