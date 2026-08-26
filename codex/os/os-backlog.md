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

**A text-key hash is duplicated in two net chapters and wants lifting into the
foreword.** `pool-key-fold` in `LoadBalancer.codex` and `tp-key-fold` in
`MessageQueue.codex` are the same four-line djb2 fold, written twice because
both needed to turn a `Text` key into an `Integer` before
`chr-hash-key : Integer -> Integer` would take it. The right home is
`chr-hash-text : Text -> Integer` beside `chr-hash-key` in
`codex/foreword/core/ConsistentHash.codex`, which both chapters already cite;
each call site then loses its local fold. It was not done at the time because
the foreword is seed-affecting and the fixes were unblocking another lane,
which is a reason to defer it and not a reason to leave it unwritten.

**`Arm64NetIO` is the only net chapter whose compile depends on a chapter
outside this quire**, `codex/os/kernel/VirtioPci.codex`. That is not itself a
defect, but it means a kernel change can redden a net chapter, and nothing
says so anywhere the kernel lane would look.
