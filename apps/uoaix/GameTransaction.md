# Game action preparation and undo

`gt-prepare game input map hostPort packet` wraps the current stage-1
`gn-default-handler`. It returns `GamePrepared { undo, reply }` after applying
the action under single ownership. The reply is still private. Persist the
complete action before publishing it, call `gt-accept undo` only after commit,
or call `gt-rollback undo` on a failed commit. Both finalizers are single-use.
This module does not write a journal, flush a device or acknowledge a client.

The caller supplies an already validated world and character table. Keep
them, the GameInput/session and WalkMap below the request scratch mark.
The undo and reply live in request scratch until finalized and consumed.
No other action, observer or asynchronous callback may inspect or mutate the
prepared state before commit/rollback. On ambiguous storage failure, latch
the authoritative store fault and stop admission until recovery; restoring
memory does not decide whether bytes committed on the device.

## Current mutation footprint

| Action | Captured persistent records |
|---|---|
| Character creation | World header and up to five slots from the free-list prefix: one mobile and up to four equipment records |
| Walking/turning | The selected mobile's slot |
| Relay allocation | Next relay-key counter |
| Other currently admitted packets | No world-slot writes |

Every capture also keeps the fixed world header, all five fixed character
slots and next relay counter. Volatile undo keeps pending relay key, active
mobile, login-listing flag and the five GameSession fields. Packet framing,
XOR state and raw diagnostic bytes remain consumed; the failed transaction's
connection must close rather than replay those bytes.

The packet profile is explicit and refuses unsupported opcodes, lengths or
non-byte data before calling the handler. Changes to GameSession/GameNet
admission or mutation behavior must update this footprint and its proof before
the changed handler is used behind GameTransaction. In particular, item,
trade, economy, skills, NPC memory and admin actions are not covered by this
stage-1 wrapper. Do not treat its fixed footprint as a general mutation tracker.

`gt-changed undo` compares the captured persistent bytes with the prepared
state. Session-only changes, such as a turn without a position change, can
return False. The store owner still checks its fault/ownership state before
admission. A durable action format must include all persistent differences,
the ordered commit metadata and replay preconditions; this predicate alone
does not supply that format.

When the domain handler returns Err, preparation restores persistent bytes
and returns that error. It deliberately preserves the handler's volatile
refusal behavior: a bad game login consumes its pending relay key. Explicit
rollback of a successfully prepared action restores both persistent and
volatile captured state, including the pending key. No prepared response may
have escaped before that rollback. Failed partial creation restores even
unpublished serial generations and free-list links, not just the live count.

The free-list prefix check is bounded and rejects observed cycles, live slots
and invalid indices. It is not a replacement for full recovered-world
validation. It never scans or clones the whole world. Character copying and
comparison use the fixed 800-byte account table; slot capture/restore is
bounded by five 80-byte records. The remaining work is fixed metadata plus
the existing GameSession operation. Creation refusal can still invoke the
existing world-delete child scan. The native proof confirms equal capture
allocation for small and maximum-capacity worlds, below its 4096-byte bound.

`proofs/GameTransactionProof.codex` checks full creation undo, committed-undo
refusal, movement/session rollback, partial equipment failure, consumed bad
login keys, relay/listing rollback, malformed profile/free-prefix refusal and
capacity-independent capture allocation. Normal and poisoned output must
match its exact oracle. Durable commit and restart remain separate work.
