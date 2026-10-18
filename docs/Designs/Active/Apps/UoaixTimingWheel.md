# UOAIX: every timed behaviour on the timing wheel

Owner: blu. Ruled by Damian 2026-10-08 ("pick an agent to work on this structure"), on root's recommendation.

## The rule

A behaviour that happens later is a `(deadline, kind, key)` entry on the wheel (`apps/uoaix/WorldTimers.codex`).
The world tick does three things: the network, the wheel drain (fire every entry now due), and the save. No system
scans its actors to ask whether it is time; the wheel's `now` is the only clock, and the town clock, the economy hour
and the clock scale derive from it.

## Why

1. A test advances `now` to the next due entry and skips idle time, so a game day of every worker runs in memory in
   milliseconds. This replaces the 4.5-minute world trial as the switch gate (root 2026-10-08; 19 workers graded by
   reading WORKER lines).
2. A tick costs the entries due, not the actor count (R-COST).
3. A failed plan arms a back-off entry instead of re-stepping at the fixed step: on main 41037 at 30x for one game day
   the lumberjack blocked 20,992 times and the quarrier 20,640 (`build-output\uoaix\trial-41037-open\server.log`).

## On the wheel

Drained by `cg-drain` -> `cg-timers`, from `cg-world-tick`, `cg-tick` and `cg-pulse-base`:

| kind | what fires |
|---|---|
| `cgm-kind` | every worker in `owner.miners`, re-armed by `cg-worker-step`'s delay: one step (`cgf-step-ticks`) after progress, doubling to 32 steps while idle or blocked |
| `cgw-kind` (12) | the economy gatherer (key 0) and the farmer (key 1), paced as the miners |
| `cgs-kind` (13) | the shop stations (keys 0 to 6) and shop trades (7, restock, stone, supply, cooking) each second; the townsfolk walk with its labour hours (8) every 100 ms |
| `cgr-kind` (14) | spawns (key 0) and the NPC combat round (1), each second |
| `cgd-kind` (15) | the save (key 0, 60 s) and the world-store reports (1, 1 s), raised as bits in `store-due` for `cs-timer` and `cs-reports` |

The pool grows at `cs-grow` once fewer than a quarter of its entries are free (`wtm-tight`, ceiling 1048576), and
`wtm-find` reaches an entry through a per-(kind, key) bucket index. Every entry is re-armed at load; the wheel is not
saved. `apps/uoaix/bvt/BvtWorkerDay.codex`, `BvtWheelGrow.codex` and `BvtWheelStages.codex` grade the workers, the growth and the stage 2 to 4 kinds.

## Open

Stage 5: the hand-written clock checks (about 45 in 25 chapters, regex count, a lower bound, 2026-10-08), each
converted when its chapter is touched for another reason.