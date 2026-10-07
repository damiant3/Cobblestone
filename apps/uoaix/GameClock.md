# Live game-day length

`GameClock.codex` converts the runtime's 100 Hz monotonic ticks into due game
hours. The default is 7200 real seconds per game day. The owner panel accepts
`panel-day-set` with `seconds` and the live owner `session`; allowed values
are 24 through 604800 seconds. Personal GMs and service roles cannot change
the rate. The panel logs actor, session, tick, old seconds and new seconds
before changing the clock. A full action log refuses the change without
advancing or rescaling the clock. `panel-data.day_seconds` reports the rate.

`AdminPanel.clock` is the shared clock. The serialized game-loop owner calls:

```text
panel-clock-poll panel currentRuntimeTick hourBudget applyOneGameHour
```

This convenience callback is pure (`Integer -> Integer`), takes the integer
1 and returns zero only after applying one in-memory game hour. It cannot
perform `Device.Block` operations. The clock consumes the hour after success.
Return values from polling are the number consumed, or -1 on admission or
callback failure. A failed poll may have committed a successful prefix;
retry only the remaining `clock.due`, never the whole elapsed interval.
The callback must refuse before effects or roll back on failure. The proof
uses `tf-step` and acknowledges its event batch after a successful hour.
For durable execution, use an outer loop with the required storage effects,
not the pure callback. Call `gc-accrue panel.clock currentRuntimeTick`; stop
without processing hours if accrual refuses. After success, process at most
the chosen budget of due hours. For each hour, atomically
commit the candidate world and ordered input together with the prospective
clock state, then call `gc-commit-hour panel.clock` on success. The transaction stores the resulting
world hour together with the prospective clock state: unchanged rate, phase
and last tick, but `due - 1`. The in-memory decrement follows successful
commit. Do not checkpoint the pre-consumption `due` as the
committed state. Recovery after a crash between commit and the in-memory
decrement restores that prospective state, so the committed hour cannot run
twice. The effectful outer loop reads the shared clock to construct that transaction.

The hour budget is 1 through 24 per poll. Longer pauses accrue a backlog
without dropping hours, while the caller retains control of per-loop work.
Tick rollback, elapsed intervals above 2147483647 ticks, or a backlog above
8760000 game hours refuse without accrual. Rate changes first accrue elapsed
time at the old rate, preserve all whole due hours and rescale the fractional
hour to the new rate. Integer division loses less than one fractional credit
unit on a rate change; polling remains limited by the runtime tick resolution.
Schedules, ages and resource regrowth stay expressed in game hours, with no
separate wall-clock multipliers.

Construct the clock and reusable pure callback below the persistent heap mark.
Do not allocate a new captured callback on every idle poll. The four-word record has
constant size; accrual and rate changes allocate no retained state. A poll is
O(hourBudget * callback cost), with a maximum of 24 callback invocations.
The native proof checks no retained heap growth across idle polls. Compiler
heap/time behavior is unchanged.

The admin-only listener does not own a game simulation loop. Image composition
must drive the shared clock through the applicable pure or durable path while
idle as well as while receiving packets, using the same clock for every scheduler.
The panel proof establishes live
setting changes and the callback contract, not a fully composed image clock.
Durable storage must checkpoint the configured rate, phase and due backlog
with the world. On restart, rebase `last-tick` to the new monotonic origin;
do not interpret the previous process's tick value as elapsed wall time.
Offline progression is a separate policy, not inferred by this clock.

`proofs/GameClockProof.codex` checks the two-hour default, fractional rate
changes, old-rate backlog, bounded catch-up, callback refusal, clock rollback,
owner audit admission, and shared townsfolk scheduling/aging/regrowth.
`test-admin-panel.ps1` includes that proof and browser controls for a live
owner change and a direct GM refusal. Use `-Poison` for poisoned allocation.
