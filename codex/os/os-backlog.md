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

**The four web-mux arms are RED at head, and no gate compiles them**
(blu, 2026-09-08, controlled against the depot state). `web-mux-feed`
takes `(route) (obs) (m) (frame) (port)`, five arguments, and every call
site in `codex/test/web-mux-lifecycle`, `web-mux-concurrent`,
`web-mux-idle-reap` and `web-mux-reassembly` passes four, binding the
`WebMux` where the observer belongs. Each fails `CDX2001: Type mismatch:
Fun vs Rec:WebMux` at its first feed and `Rec:WebMux vs List` at the rest,
emits a 0-byte binary, and exits 4: three, four, six and six errors
respectively.

The control was run rather than reasoned: the change in flight was
shelved, the workspace reverted to head, and `web-mux-lifecycle` compiled
to the identical four errors, so the arity is at head and not in anyone's
edit.

These four are the ONLY end-to-end evidence the mux has, so while they are
red no change to the accept, sweep or drain paths can be proven
behaviourally. The repair is mechanical, the observer argument at each
site, and it wants doing before the transport pool lands.

**The web mux never gives a connection's heap back, and a connection costs
66,346 bytes** (measured 2026-09-08, `codex/test/web-mux-heap`).
`web-mux-accept` builds a fresh listen transport per accepted connection
and keeps one per live connection, and nothing restores the heap across
them. The measurement is per-connection and steady: 66,392 at one
connection, 66,344 at four, 66,346 at sixteen, so the `list-push` doubling
of `conns` is amortised and the figure is essentially the buffer.
`serve-recv-buf-cap` is 65,536 of it and the remaining 810 bytes are the
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
| retained (the three landed counts) | 66,346 |
| DISCARDED, no reference kept | **66,328** |
| the same construction inside a `__heap-save` / `__heap-restore` window | **0** |

The 18 bytes between retained and discarded are the list slot. The third
row is the control, and it is what makes the second row mean anything: the
instrument CAN read a reclaimed heap and reports zero when the bytes come
back (L-FALSIF). So a service that accepts and closes one connection at a
time still exhausts a 1 MiB slot after 15 of them.

A fixed pool of transports reused across connections is the shape, and it
is the only one of the two the memory model allows: a reap that restores
to a mark taken before the accept cannot work, because the accepted
connection's own state is allocated after that mark and a restore would
take it too (the same single-bump-pointer argument as the os/net
receive-loop leak). Sized to the connection ceiling, a pool converts a
per-accept cost into a per-slot cost paid once.

The repair is contained: `WebServer.codex`, the five `web-mux-*` arms and
`apps/works/GopWeb.codex:152`. The arm goes red when the repair lands,
which is when to record both numbers. Unowned; the net side.

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
shared memory are not the variable; the servicer round-trip from a spawned
process is. `scope-runtime-spawn` passes because its child writes blocks
DIRECTLY (`fat16-write-file`, `Device.Block`) with no servicer. Probable
age: as old as the servicer path itself; nothing in the Update 50 cycle
touches it, and no instrument has ever measured it before today. The
rewritten test (`process-wait`, verdict prose corrected) is in the tree
under a `.skip` citing this row; the `.expected` records 10, the value
that un-skipping should produce once this is fixed. First question for
whoever takes it: what does the child fault ON -- the capability lookup,
the servicer channel, or the elevation -- via the same two-stage probe
with the poke moved inside a narrower window. Unowned; blu's neighborhood.

