# World timers

`WorldTimers.codex` is the composite's single fixed-capacity timer wheel.
`wtm-new capacity slots now` allocates the entry pool (40 bytes per entry)
and the one-tick buckets once; no wheel operation allocates after that. The
composite owns a wheel of 256 entries over 256 PIT-tick buckets (about 2.56 s
per revolution) and drains it in every pulse with the pulse tick.

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

The wheel is not persisted. A door open at a restart stays open until used.

## Timed state not on the wheel (surveyed 2026-10-05)

| family | where the time lives | clock |
|---|---|---|
| composite save | `cs-timer` in `CompositeStore`, `last-save`, 60 s | HPET |
| monsters | `GameMonster` slot `due`; persisted as remaining time by `GameMonsterCodec` | combat tick (PIT) |
| combat swings | `GameCombat` actor `due` | combat tick (PIT) |
| magery | `GameMagery`, `MageryTick`, `MageryEffects` actor `due`, `recovery`, `poison-due`, `regen`, `night`, `protected-until`; wall `until` | magery tick (PIT) |
| vendor quotes | `GameVendor` player `expires`, checked when a cart arrives; fires nothing | PIT |
| world spawns | `GameWorldSpawn` `deadlines` buffer and slot `due`; not composed into the composite | combat tick (PIT) |

Not world events: TCP retransmit and close timers (`codex/os/net/NetIdle.codex`,
transport state; its 100 ms HPET wake drives the pulse) and the `ShardServer`
link tick (a separate entry).

## Proof

`proofs/WorldTimersProof.codex` grades exact firing, replace and clear by key,
pool exhaustion and reuse, a drain across many revolutions, past deadlines,
adds during a drain, and 64 self-rescheduling timers over 38000 ticks firing
the computed count with zero heap growth. Compare its complete output with
`WorldTimersProof.expected`. `proofs/DecorationReplay.codex` grades door
autoclose, the occupied-doorway deferral and cancellation by hand.
