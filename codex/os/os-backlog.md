# OS quire -- open capabilities

Quire-domain backlog for `codex/os/**` (kernel, net, observe). The shape and
priority order for the platform live in `docs/PM/CurrentPlan.md`; anything
that is this quire's own behaviour lives here.

The rules are the standing ones: an entry says what is still missing and
nothing else, a closed entry is DELETED rather than annotated, and a gap that
is still real is never quietly dropped.

Opened 2026-08-19 (blu). There was no register for this quire before, which is
part of why the entries below went unrecorded for as long as they did.

## Open

**`verify-cdx-full` takes a `List Integer`, so verifying an image costs eight
bytes of heap per byte of image.** Every caller has to read the artifact out
of wherever it lives and into a list first, which is the memory limit
`OTAFirmwareUpdate.md` names as its gap 2: an OTA candidate is staged in a
flash bank and then read back out to be checked, and the documented `List
Integer` element cost is 8 bytes, so a 400 KB image asks for 3.2 MB it does
not have. A buffer-taking verifier is the fix, and it reaches further than
OTA: `evaluate-load` in `codex/os/verify/VerifiedLoader.codex` is the entry
point, it delegates the real work to `verify-cdx-full` and then does its own
`read-le64` and `decode-capabilities` over the same list, and there are SEVEN
call sites at head -- `ProgramRegistry.codex:48` in production, and the arms
`ota-lwm2m-loopback`, `loader-scope-test`, `loader-network-scope-test`,
`process-caps-test`, `verified-loader-test`, `verified-spawn-test`.

The precedent is in the tree and is worth reading first: `OtaBoot.codex`'s
`boot-verify-candidate` and `boot-read-bytes` already hash a staged image
straight from an ADDRESS, so Gate A does from flash what Gate B cannot. The
question a design has to answer is what the verifier reads through -- an
address plus a length, or a buffer handle -- and whether the capability
decode can share it, since that half also indexes the list.

Additive first, if that is the shape chosen: a buffer-taking entry point
beside the list one leaves all seven call sites alone and lets the OTA path
move on its own. Whoever takes it should say which, because a signature
change to `evaluate-load` is a change to `codex/os/core` and every arm above.
Unowned; the verify side, campaign-sized rather than a CL (root's ruling,
2026-09-08).

**No build script names the four web-mux arms**, so `web-mux-lifecycle`,
`web-mux-concurrent`, `web-mux-idle-reap` and `web-mux-reassembly` are
reached only by the release battery, which discovers `codex/test` by
directory (L-NOGATE). The arity defect that made all four red is repaired
and verified at head: every `web-mux-feed` call site passes five
arguments. These four remain the only end-to-end evidence the mux has, so
a change to the accept, sweep or drain paths is proven behaviourally only
by running them by hand.

**The web mux never gives a connection's heap back, and a connection costs
66,322 bytes** (measured 2026-09-08, `codex/test/web-mux-heap`).
`web-mux-accept` builds a fresh listen transport per accepted connection
and keeps one per live connection, and nothing restores the heap across
them. The measurement is per-connection and steady: 66,368 at one
connection, 66,320 at four, 66,322 at sixteen, so the `list-push` doubling
of `conns` is amortised and the figure is essentially the buffer.
`serve-recv-buf-cap` is 65,536 of it and the remaining 786 bytes are the
session, the transport record and the list slot.

That divides into the two beds the entry used to estimate: a spawned
service on the default 1 MiB heap holds **15** connections, and proc 0 on
a 128 MB boot image holds about **2,000**. The estimates it replaces
("on the order of a dozen", "a few thousand") were the right order, which
is worth knowing before trusting the next one.

**Those two ceilings are per connection EVER ACCEPTED, not per concurrent
connection** (blu, 2026-09-08, measured both ways). The three counts above
all RETAIN their transports, so none of them could say whether a closed
connection gives its bytes back. It does not: `transport-new-with-cap`
ends in `__heap-advance cap`, `web-mux-remove` rebuilds a list and calls
no `__heap-restore`, and the chapter's only restore is `web-mux-loop`'s
empty-poll window, which its own prose says the sweep runs outside of.
The arm now carries the two readings that settle it, at n=16:

| arm | bytes per connection |
|---|---|
| retained (the three landed counts) | 66,322 |
| DISCARDED, no reference kept | **66,304** |
| the same construction inside a `__heap-save` / `__heap-restore` window | **0** |

The 18 bytes between retained and discarded are the list slot. The third
row is the control, and it is what makes the second row mean anything: the
instrument CAN read a reclaimed heap and reports zero when the bytes come
back (L-FALSIF). So a service that accepts and closes one connection at a
time still exhausts a 1 MiB slot after 15 of them.

**REPAIRED (blu, 2026-09-08): the transport pool, and the per-accept cost
is 768 bytes.** `WebMux` gained a `free` list, a closed connection's
transport is pushed onto it at all three close sites, and `web-mux-accept`
rebinds one instead of minting when the list is not empty.
`rebind-listen-transport` is `fresh-listen-transport` with `recv-base` and
`recv-cap` carried over and `recv-len` zeroed, so the 65,536-byte
`__heap-advance` does not happen and what remains is the session and the
record. Measured by `per-conn-recycled` at n=16: **768 bytes against
66,304**, and 66,304 minus 768 is exactly 65,536, the buffer.

A reap was the other option the entry offered and the memory model forbids
it: a restore to a mark taken before the accept would take the accepted
connection's own state with it, which is the same single-bump-pointer
argument as the os/net receive-loop leak.

The first three counts in the arm are UNCHANGED at 66,368 / 66,320 /
66,322, and that is not a defect: they call `fresh-listen-transport`
directly, which is what the FIRST connection still costs and what every
connection costs once the pool is empty. The pool converts a per-accept
cost into a per-slot cost paid once, so a server's ceiling is now its
concurrent connections rather than the connections it has ever accepted.

**A CAP ON `free` IS REFUSED, and the measurement is `drop-delta` in
`codex/test/web-mux-heap` (blu, 2026-09-08): dropping one transport from a
16-entry pool moves the heap by +144, never back.** A cap can only drop
transports, and a dropped transport's buffer came from `__heap-advance`
inside `transport-new-with-cap`; the heap is a single bump pointer whose one
reclamation is `__heap-restore` to a mark, and the mark preceding a pooled
transport also precedes every live connection. So a cap returns nothing, pays
144 bytes for the rebuilt list, and guarantees a 66,304-byte re-mint at every
accept the cap turned away: a capped pool is strictly worse than the unbounded
one at every cap below the peak burst. The instrument is not agreeing with
itself, because the same two marks read 0 on the `restored` line, where the
bytes do come back (L-FALSIF).

The pool is therefore unbounded by design, and the ceiling it leaves is real
and belongs to the memory model rather than to `WebMux`: a burst of N
concurrent connections holds N buffers for the life of the process, 66,304
bytes each. Bounding that needs a heap that can free one buffer, which is the
same change the os/net receive-loop leak needs.

**A spawned child exercising an inherited `FileSystem.Write` capability
CRASHES inside `write-file`, and the test guarding it never once observed
its subject.** Found at the Update 50 release battery (red, 2026-08-25,
Damian's ruling: document, skip, carry on). The chain, each step measured
at seed `C45E5825`: `fs-spawn-inherits` peeked byte 28000 after ONE
`process-yield`, which a servicer round-trip outlives, so every green it
ever recorded was the BOOT CONTENT of that byte -- an instrument that
cannot fail (L-FALSIF); the value was 1 for months by layout accident and
today's 9 KB seed growth moved it to 2, which is the only reason anything
surfaced. Rewritten with `process-wait`, the test answers 237 (child never
poked); a two-stage probe pins it exactly: the child pokes a 77 sentinel,
then dies inside `write-file` before the verdict poke -- 77 on the glass,
no clean False, a fault. `spawn-memo-table` (pure child, same
poke/wait/peek shape) is green in the same environment, so scheduling and
shared memory are not the variable; the WRITE servicer from a spawned
process is (narrowed from "the servicer round-trip" by the trail below,
which shows the read servicer completing in the same child). `scope-runtime-spawn` passes because its child writes blocks
DIRECTLY (`fat16-write-file`, `Device.Block`) with no servicer. Probable
age: as old as the servicer path itself; nothing in the Update 50 cycle
touches it, and no instrument has ever measured it before today. The
test is in the tree under a `.skip` citing this row.

**THE FAULT IS IN THE WRITE PATH ALONE, and none of the three candidates
this row used to name is the site** (blu, 2026-09-08). The test is now a
TRAIL: the child writes a sentinel at each stage it reaches, and one run
under the arm's own disk fixture reports

| state | value | what it eliminates |
|---|---|---|
| `parent-write` | 1 | the control: proc 0's write path and the fixture are good |
| `alive` | 71 | the child ran |
| `write-cap` | 1 | the `FileSystem.Write` bit DID travel to the child's process-table word, so the lookup is not the site, and its deny path returns 0 rather than faulting |
| `block-cap` | 0 | the child holds no `cap-block-device`, so any disk the child reaches, it reaches through the elevation cell and nothing else |
| `read` | 72 | the READ servicer completed and returned content, so the capability lookup, the elevation store to cell 36232 and the servicer call all work in a spawned process |
| `verdict` | 0 | the child never reached the verdict poke |

The site is therefore downstream of everything the read path shares, inside
the write half. The next narrowing is inside `fat16-write-file` from a
spawned process rather than in the servicer machinery, and the first
candidate the trail does not separate is the child's own heap: a spawned
process runs on the default 1 MiB slot, and a FAT and directory update
allocates sector buffers at 8 bytes a byte, which the read path does not do.
The block syscalls are NOT the difference: all four (10 read, 11 write,
12 count, 13 select) go through the same `emit-block-elev-gate`, verified at
head. The test stays under its `.skip` and its `.expected` records the
post-fix trail. Unowned; blu's neighborhood.

