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
