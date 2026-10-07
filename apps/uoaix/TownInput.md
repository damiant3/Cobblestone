# Ordered simulation inputs and replay

`TownInput`, `TownInputCodec` and `TownReplay` serialize and replay the
currently implemented world/townsfolk/mind operations beside the combined
[checkpoint codec](TownStateCodec.md). The input format is a trusted server
record, not a player command language. Authentication and game admission
remain the server's responsibility; player text cannot select an operation.
The database migration in [Database.md](Database.md) will replace this path
only after the database passes equivalent restart acceptance.

## Operations

Construct `ti-new sequence tick operation expectedResult`, populate its eight
integer argument slots and relevant text/proposal/world-event fields, and
encode with `ti-encode entry buffer capacity`. Unused slots and fields must
remain zero/empty. Success returns exactly `ti-frame-bytes`, 1280 bytes.

| Operation | Arguments from slot 0 | Text / nested data | Recorded result |
|---:|---|---|---|
| 1 world mutation | none | `WorldEvent`, with matching sequence/tick | allocated/updated/deleted serial, or -100 for a world refusal |
| 2 add town | market, tavern, temple | `text`: name | town serial or admission refusal |
| 3 add person | town, home, workplace, job, ageDays | `text`: name | NPC serial or admission refusal |
| 4 marriage | one, two | none | layer 1 return code |
| 5 expect birth | parent, dueDay | none | layer 1 return code |
| 6 purchase | buyer, vendor, item, quantity | none | layer 1 return code |
| 7 clock hour | none | none | layer 1 return code |
| 8 acknowledge events | none | none | 0 |
| 9 configure persona | NPC, callsPerHour, tokenBudget, giftLimit | `text`: description; `extra`: fallback | configuration return code |
| 10 host response | NPC, eventKind, requestId, providerStatus, tokensUsed | `text`: quoted-data input; `proposal`: all proposal fields | `TmReply.outcome` |
| 11 unavailable stub | NPC, eventKind, requestId | `text`: player/event input | `TmReply.outcome` |
| 12 acknowledge audit | none | none | 0 |
| 13 bind mobile | NPC, mobile serial, or zero to unbind | none | 0 or -1 |

World mutations use the existing UWJ1 operation and expected-serial rules.
Deleting a mobile still bound to a living NPC refuses with -100; unbind
before deletion. Binding requires an existing mobile and refuses duplicate
living-NPC bindings. A world refusal retains its diagnostic in the returned
`TownOutcome.detail`; the durable result is the explicit -100 code.

Location arguments are bounded to signed 31-bit values; invalid nonpositive
locations still reach layer 1's recorded refusal. Text capacities are 512 for
`text`, 128 for `extra` and 256 for proposal speech. The underlying admission
and proposal validators retain their tighter limits. The world-event field is
used only by operation 1; all other operations leave the zero placeholder from
`ti-new`. There is no arbitrary field setter or model-call operation. World
mutations remain trusted server records; this codec does not enforce economy
or discipline authorization.
Future economy/government state and operations require an explicit schema
extension; version 1 does not claim to persist unimplemented state.

## Applying and replaying

`ti-decode buffer actualLength` returns a detached `TownInput`; use
`ti-decode-record buffer actualLength outerTick outerSequence` after
`WorldDisk.us-read`. A committed frame must be complete. The decoder rejects
wrong lengths, unknown versions/operations, checksums, reserved data, text
bounds and embedded world metadata disagreement. FNV32 detects corruption,
not forgery or authorization.

`ti-apply candidate entry` is the single-writer mutation primitive. It checks
the next sequence and nondecreasing tick, applies the operation and compares
the actual result with the recorded result. Recorded refusals are valid inputs:
for example, an unavailable mind or rejected purchase still advances the
committed input position. Sequence exhaustion is explicit.

**An outcome mismatch can occur after mutation. Discard that candidate on
Err; never publish or continue it.** Use this primitive only on an owned
candidate during recovery, or within a server commit/admission procedure that
handles ambiguous outcomes by recovery. `ti-apply` does not implement live
transaction rollback or decide whether an untrusted request is authorized.

`ti-replay source buffer byteLength` clones the complete source once and applies
the contiguous frames to that private candidate. Success returns detached
state; failure leaves the original source unchanged. The batch accepts at most
4096 frames (5242880 bytes), and refuses a partial trailing frame. Each frame
fits separately inside WorldDisk's 4 MiB payload limit. The batch buffer limit
is not a limit on the store's total record count.
The batch buffer contains concatenated TSI1 payloads, not the disk image or
UOS1 commit sectors and padding. A WorldDisk caller extracts those payloads
through `us-read` after the selected checkpoint.

Text in successful town/person admissions and persona configuration becomes
part of persistent state. Retain the decoded input's allocation for those
operations; `ti-retains-text entry outcome.code` names the cases. Other inputs
keep no decoded pointers and their scratch mark can be restored after the
outcome and next-record offset have been captured. The batch replay implements
that rule. On failure, its allocated candidate/error remains in the caller's
scratch lifetime until the caller reports and abandons the attempt.

For streaming recovery, start from the store's newest kind-1 checkpoint, check
its outer metadata with `ts-decode-record`, then follow each returned `next`
offset. Require kind 2, decode with outer metadata, apply in order, and preserve
successful admission strings. Do not publish the candidate until the suffix
has completed and `ts-state-valid` passes. Preserve storage/DMA allocations
below recovery marks as [WorldDisk.md](WorldDisk.md) requires.

## TSI1 wire format

| Offset | Content |
|---:|---|
| 0..63 | Eight little-endian integer cells: magic `0x31495354`, version 1, length 1280, operation, tick, sequence, FNV32, expected result |
| 64 | Eight argument cells |
| 128 | UWJ1 frame for operation 1; otherwise 128 zero bytes |
| 256 | Proposal actor, kind, target, item, quantity, destination |
| 304 | CCE text: length cell plus capacity 512 |
| 824 | CCE extra text: length cell plus capacity 128 |
| 960 | CCE proposal speech: length cell plus capacity 256 |
| 1224 | 56 reserved zero bytes |

Text padding is zero. The checksum covers the complete frame except its own
cell at 48; UWJ1 retains its own inner checksum. Sequence and tick in UWJ1 must
match the outer TSI1 values. Frames contain no native pointers. `TownInput`
has nine native fields (72 bytes), `TownOutcome` two (16 bytes), plus the
bounded argument, text, proposal and world-event allocations.

Frame encoding/decoding is bounded by 1280 bytes. Replay costs one combined
checkpoint copy plus each operation's normal cost. Binding scans at most 128
NPCs; world mutation costs remain those in WorldRecords.md. There is no
per-input copy of the whole state. Only successful admission/configuration
strings accumulate; their counts are bounded by town/NPC/persona capacity.
The empty-world clock proof retains the same heap after 2 and 4096 applied
inputs; failed replay allocations require caller reclamation.

## Proofs and limits

`proofs/TownInputProof.codex` covers wire cells, quoted/multiline text,
proposals, mixed operations, detached source state, replay gaps, reversal,
outcome mismatch, and rehashed malformed data. Its maximum batch advances
4096 actual clock hours and checks equal retained heap against two hours.
Run normal and poisoned builds and compare the complete output with
`proofs/TownInputProof.expected`.

```powershell
pwsh -NoProfile -File apps/uoaix/proofs/test-town-restart.ps1 -Kernel seed/Codex.cdx
```

The restart harness creates a new synthetic 4 MiB image, writes initial and
later combined checkpoints plus 46 ordered inputs, exits the writer VM, and
opens that image in a separate reader VM. The reader restores checkpoint 9
and its 37-input suffix, including names/personas admitted after that checkpoint.
Exact oracles cover clocks, family/birth data, world records, counters and
pending audit. Artifact/disk hashes and normal guest-exit evidence are retained;
the reader must leave the disk hash unchanged. No client data enters the test.

The proof establishes combined recovery under codex-vm's IDE backend. Fester's
WorldDisk proofs cover the storage layer and virtio backend separately. This
unit does not establish the combined sequence under QEMU/virtio, physical power
loss, authenticated game-server mutation admission, log rotation or a deployed
shard. The writer/reader entries are integration examples, not a running server.
