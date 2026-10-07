# Panel audit checkpoint

`PanelAuditCodec` stores the owner panel audit and owner session counter in
UPA1. `pac-encode panel tick sequence buffer capacity` returns the fixed
133216-byte payload or a diagnostic. The caller supplies stable state and
disjoint output storage. Encoding refuses an action while its callback is
in flight; checkpoint only at the serialized owner's operation boundary.

`pac-decode buffer length adminWorld trustedActor gmBackend clock` restores copied
audit storage bound to caller-supplied state and identity. Use validated
AdminWorld recovered from the same enclosing commit. The trusted actor and
GM backend come from boot provisioning, not from the checkpoint. Supply the
shared validated GameClock recovered from the encompassing snapshot with
its last-tick rebased to the new monotonic origin. UPA1 does not serialize
the clock or silently replace its rate, phase or due backlog. With
`pac-decode-record`, pass outer tick and sequence before those bindings.
The envelope metadata must match the enclosing record.

Restore clears owner/GM sessions, connection listings and pending action
fields. The one queued action's retained audit outcome becomes -2
(cancelled); its original intent text remains. The owner session counter
survives, so the next login advances it. A restored queue cannot apply an
old callback. No effect-in-flight snapshot is accepted. Fresh authentication
epochs and keys remain the boot owner's responsibility and are never part
of UPA1. Deployed GM mutations remain disabled until the database is
authoritative, per the stage-D ruling; this codec does not enable them.

After success, the input buffer can be reused. Keep the returned panel and
supplied bindings below request scratch marks. Failed decode may allocate
a private partial candidate: report its error then reclaim recovery scratch.
The codec does not mutate the supplied AdminWorld, GM backend or clock. Current
principal text is supplied by trusted boot configuration; historical audit
rows retain their original actor text.

## UPA1 layout

| Offset | Content |
|---:|---|
| 0..63 | Magic `0x31415055`, version 1, size, reserved zero, tick, sequence, FNV32, reserved zero |
| 64 | Owner session counter |
| 72 | Audit count, 0 through 128 |
| 80 | Queued audit id, or zero |
| 88 | Pending flag, zero or one |
| 96 | 128 rows of 1040 bytes |

A row contains length(8), at most 1020 CCE text bytes, zero padding through
offset 1031, then an eight-byte outcome. Integer cells are little-endian.
FNV32 skips its cell at 48 and detects corruption, not authorization.
Row ids must match their positions and row session values cannot exceed
the retained counter. Only the identified pending row may have outcome zero.
Unused rows and text padding must be zero. Completed/refused/cancelled rows
retain their outcomes. The existing bounded audit capacity remains unchanged;
the shared owner must handle admission backpressure and eventual log draining.
Near-full retained history can refuse a new panel login after reboot. The
codec supplies no archive/drain mechanism and does not bypass that refusal.

Work is linear in the fixed envelope and bounded row JSON. Per-row parse
scratch is reclaimed. Decode allocates the existing panel audit/connection
arrays and wrappers once; no input pointer is retained. No GM backend or
world copy is introduced by the codec.

`proofs/PanelAuditProof.codex` checks normal/poison round trips, guard bytes,
copied history, cancellation without callback, fresh session state and
principal binding, monotonic next login, input reuse, corruption, malformed
queued references/outcomes and refusal during an in-flight action.
Disk ordering, composite recovery and live game/admin integration remain
separate stage-D work. [AdminPanel.md](AdminPanel.md) owns log-before-effect
semantics; UPA1 is a component, not a new commit path.
