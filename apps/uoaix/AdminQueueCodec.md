# Durable admin report and mind-job queues

`AdminQueueCodec` encodes AdminWorld's queue metadata, counters, report ring
and mind-job text in UAC1. The fixed envelope is 70096 bytes. The caller
checkpoints the corresponding townsfolk/mind state in the same enclosing
commit. UAC1 does not serialize those referenced records, protocol keys,
boot epochs, authenticated sessions, panel audit or the volatile GM stand-in.

`ac-encode admin tick sequence buffer capacity` returns the written length
or a diagnostic. Supply stable module-owned AdminWorld state and disjoint
output storage. Metadata must be nonnegative. The output buffer must not
overlap any source record/list/text storage; the caller owns that condition.

`ac-decode buffer length townsfolk mind` returns detached queue storage
bound to the supplied townsfolk/mind references. Supply the already validated
state recovered from the same enclosing snapshot. The decoder validates
pending-job NPC/persona references against that state. `ac-decode-record`
also takes outer tick and sequence before the two state arguments and
requires matching metadata. A queue-only payload is not a complete shard
checkpoint and cannot be passed directly to TownStore or GameStore.

The input buffer can be reused after successful decoding. Keep the returned
AdminWorld and supplied state below subsequent request scratch marks. On
failure, consume the diagnostic then reclaim the caller's recovery scratch;
do not publish a partial candidate. Encoding/validation also allocate bounded
scratch that the caller reclaims after consuming the Result.

## UAC1 layout and checks

| Offset | Content |
|---:|---|
| 0..63 | Magic `0x31434155`, version 1, size, reserved zero, tick, sequence, FNV32, reserved zero |
| 64..183 | Fifteen integer cells in `ac-scalars` order |
| 184 | Reserved zero cell |
| 192 | Thirty-two report text slots |
| 65984 | Two mind-job text slots |

Each 2056-byte text slot holds an eight-byte length, at most 2047 CCE bytes
and zero padding through its final byte. Native unused text cells are not
semantic state and are normalized to zero. Integer cells are little-endian.
FNV32 skips its cell at offset 48 and detects corruption, not forgery.

Validation bounds list shapes, scalar values, ring positions, counts,
sequences, text extents and padding. Every active report must contain its
expected ordered JSON id. Pending/completed jobs require an admitted persona
and valid request context; completed result JSON must match the job id.
The decoder retains the job phase, so a result awaiting delivery remains
pending acknowledgement. Delivery and durable acknowledgement ordering are
still the shared owner's responsibility.

Wire work is linear in the fixed envelope and bounded report JSON. Report
validation reclaims per-report scratch. Native decode reserves the existing
AdminWorld queue arrays, then copies each logical text byte; no input-buffer
pointers are retained. Source townsfolk and mind are neither copied nor
mutated by the codec.

`proofs/AdminQueueProof.codex` has exact normal/poison oracles for an advanced
report ring, quoted Unicode, pending/completed jobs, counters, buffer bounds,
input reuse, detached queue mutation and corrupted/rehashed malformed frames.
Disk restart, panel audit persistence and live server admission remain
integration work. [AdminPort.md](AdminPort.md) owns authentication and job
delivery semantics; the codec adds no new network authority.
