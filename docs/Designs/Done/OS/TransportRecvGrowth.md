# A TCP transport's receive buffer grows on need

Status: built (reek), rung 1, 2026-11-01. Root GO 2026-11-01 under L-SCALE: "a transport reserves what it holds and grows on need up
to the 32 MiB message cap; grade with a many-connection arm". Source row: CORE-20 (`codex/foreword/core/core-backlog.md`).

## 1. The gap

`transport-new` (`codex/os/net/TcpTransport.codex`) advances the heap by `transport-recv-buf-cap`, 33,554,432 bytes, for
every connection before it receives a byte (33,554,496 measured with the record, 2026-11-01). N connections cost N x 32
MiB. On arm64 the heap and the stack share one 32 MB region, so one default transport filled it (`ProtocolStack.md`).
`recv-cap` is two things at once: the buffer's size and the largest message `transport-try-recv` accepts
(`msg-len > recv-cap - 4` drops the buffer).

## 2. The constraint that shapes the design

The buffer is raw memory at `recv-base`, reserved below every loop's heap mark because `transport-new` runs before the
loop starts. A buffer grown inside a loop is allocated at the heap top, which is ABOVE the loop's mark, and
`transport-compact` (`NetCompact.codex`) ends with `__heap-restore mark`: the next compaction would hand the grown
buffer back to the allocator while the transport still points into it (L-BELOWMARK, the WebMux and UOAIX trade
defects). There is no allocator outside the bump heap to take the region from instead.

Inside `codex/os/net` only the two compactors (`transport-compact`, `web-mux-compact`) restore the heap across a feed;
every other `__heap-restore` there brackets a poll that delivered no frame, a frame for another socket, or a DNS wait
(census 2026-11-01, 45 sites). A caller outside `codex/os/net` that restores across `transport-process-frame` would
reclaim a grown buffer the same way, and rung 1 censuses them.

## 3. Design

1. **Two fields where there was one.** `recv-cap` is the current buffer size; a new `recv-limit` is the largest message
   (`transport-try-recv` tests it). `transport-new` starts at `transport-recv-initial` (4,096 bytes) with
   `recv-limit` 33,554,432. `transport-new-with-cap sess cap` keeps today's meaning, a fixed buffer, with
   `recv-limit = recv-cap = cap`, so the web mux (64 KB) and the UOAIX transports (4,096) do not change.
2. **Growth on feed.** When a feed would pass `recv-cap` and the total stays within `recv-limit`, the transport takes a
   region of the next power of two that holds it, copies `recv-len` bytes, and points `recv-base` there. Past
   `recv-limit` the feed is refused as today.
3. **The floor.** The grown transport records `recv-floor`, the heap address just past its newest region.
   `transport-compact` restores to `max(mark, recv-floor)` and `transport-compact-past` measures from the same
   point, so a buffer grown inside a loop survives every later compaction. The leftovers between the old mark and the
   region (at most one compaction window plus one frame at the moment of growth) stay allocated; with doubling there
   are at most 13 growths from 4 KiB to 32 MiB.
4. **The outgrown region** is abandoned, as `list-push` abandons an outgrown list: the sum of the outgrown regions is
   under the final size. A size-class pool for them is a later rung if a measurement asks for it.

## 4. Proof

- `codex/test/net-recv-growth`: 64 `transport-new` connections each receiving one small framed message cost under
  64 x 8 KiB together (against 64 x 32 MiB); one connection receiving a 1 MiB message in 1,460-byte feeds grows and
  returns the message byte for byte (a ramp, not zeros); a message one byte past `recv-limit` is refused; a fixed
  `transport-new-with-cap` buffer refuses past its cap exactly as before.
- The L-BELOWMARK arm: a transport grows inside a compacting loop (`transport-arenas`, a mark, a feed that grows, a
  `transport-compact` to the mark, then allocations that would overwrite a reclaimed region), and the message still
  reads back byte for byte; the same arm with `transport-compact` restoring to `mark` alone goes red.
- Every existing citer through `build/cite-gate.ps1`, `net-recv-heap` and `net-compact-arena` among them.

## 5. Rungs

| rung | delivers | acceptance |
|---|---|---|
| 1 | `recv-limit`, growth on feed, `recv-floor` in compaction, `transport-new` at 4 KiB | section 4 |
