# Ordered economy inputs and recovery

`EconomyInput` records the implemented money, production and plant operations
beside a [UEC1 checkpoint](EconomyStateCodec.md). UEI1 frames contain trusted
server decisions and expected outcomes. They are not a player request format.
Authentication, actor control, map admission and royal authority are decided
before constructing a record. Stored authority flags do not authenticate an
untrusted caller or make a checksum an authorization mechanism.

`ei-new sequence tick operation` constructs an entry with eight zero arguments.
Populate the arguments and `expected`, then call `ei-encode entry buffer
capacity`. Unused arguments must remain zero. Operation-specific booleans are
exactly 0 or 1. The supported operations are:

| Code | Operation | Arguments in order |
|---:|---|---|
| 1 | Open empty purse | kind, owner |
| 2 | Admit empty actor | purse, place |
| 3 | Admit owned workplace | actor, station kind, place |
| 4 | Admit wild source | resource, place |
| 5 | Admit prepared ground | actor, place, ground kind |
| 6 | Record verified arrival | actor, place |
| 7 | Configure mint | authority, operator actor, coin per ingot |
| 8 | Strike coin | authority, actor, station, ingots |
| 9 | Set tax | authority, basis points |
| 10 | Lend | lender purse, borrower purse, principal, interest, due game hour |
| 11 | Repay | borrower purse, loan, gross amount |
| 12 | Pay | payer purse, payee purse, gross amount |
| 13 | Grant to crown business | authority, business purse, amount |
| 14 | Reclaim removed coin | payer purse, amount |
| 15 | Harvest | actor, node |
| 16 | Craft | actor, recipe, station |
| 17 | Buy | buyer actor, seller actor, item type, quantity |
| 18 | Eat | actor, food item |
| 19 | Plant | actor, plot, resource |
| 20 | Care for plant | actor, plot, water flag |
| 21 | Plant eaten | node |
| 22 | Advance game hour | none |

The result is the underlying operation's integer result. No raw issuance,
stock grant, skill grant, arbitrary field assignment or catalog-edit opcode
exists. Catalog changes require an explicitly versioned admission path.
Clock rates and plant deadlines are game time; real-time day-length conversion
belongs to the server clock owner.

## Apply and replay ownership

`ei-apply checkpoint entry` requires the next sequence and a nondecreasing
outer tick, executes the operation and checks the recorded result. A valid
recorded refusal still advances the input position. Skill failure -2 is a
recorded outcome that changes practice and tool wear; admission refusal -1
has the underlying operation's refusal semantics.

**An outcome mismatch can follow mutation. Discard that private recovery
candidate on Err. Do not publish, continue or retry that candidate.** The
primitive does not provide live transactional rollback. `ei-execute` is an
internal dispatcher for validated entries, not an authorization endpoint.

`ei-replay source buffer bytes` clones the source checkpoint once, applies
contiguous UEI1 frames to the private copy, and validates the completed state.
Success returns detached state; failure leaves the supplied source unchanged.
It accepts up to 4096 frames (524288 bytes) and rejects a partial tail. Every
entry contains only scalars, so per-frame decode/result scratch is reclaimed
without retaining input pointers. On failure, report the error and reclaim
the caller's entire replay scratch. The clone buffer remains in the returned
allocation region because this runtime has no moving collector.

The batch buffer is concatenated UEI1 payloads, not a WorldDisk image or its
commit sectors. Use `ei-decode-record buffer length outerTick outerSequence`
for individual store payloads; outer and embedded metadata must agree.

## UEI1 wire format

Each frame is exactly 128 bytes, little-endian cells. The first eight cells
are magic `0x31494555`, version 1, length 128, operation, tick, sequence,
FNV32 and expected result. The eight argument cells begin at byte 64. FNV32
skips its own eight bytes at offset 48. Unknown operations, noncanonical
argument padding and invalid Boolean fields refuse admission after checksum
validation. FNV32 detects corruption and does not authenticate the record.
Outer tick and sequence are bounded to the economy's 1000000000000 limit;
input sequence must be positive and exhaustion is explicit.

## Store recovery and proof

`EconomyStore.economy-store-restore store` selects the latest kind-1 UEC1
checkpoint and follows kind-2 UEI1 records to the committed head. A blank
store refuses; initialization is explicit. The returned `EconomyRecovery`
holds the detached checkpoint and suffix count. Final tick/sequence must
match the store, and the full economy invariant check must pass. Checkpoint
before exceeding the 4096-input suffix budget.

Keep device, DMA buffers and store below recovery marks, following
[WorldDisk.md](WorldDisk.md). Keep the store single-owned and unchanged during
recovery. Publish only Ok state. The helper is read-only and retains one UEC1
buffer plus one UEI1 buffer alongside the decoded state. It performs no
whole-state copy per input.

```powershell
pwsh -NoProfile -File apps/uoaix/proofs/test-economy-restart.ps1 -Kernel seed/Codex.cdx
```

The harness creates a fresh synthetic 4 MiB disk. A writer VM commits the
empty checkpoint, 162 inputs, and a later checkpoint at input 48. A separate
reader VM restores that checkpoint and its 114-input suffix. The scenario
starts with zero coin, mines/smelts/mints, funds a vendor loan, buys a worn
tool and water, plants and tends cotton, defaults and partially repays the
loan, reclaims coin, and harvests the grown plant. Expected state includes
exact purse balances, loan terms/status, charges, mint and material counters,
post-checkpoint admissions and plant deadline. The input fixture computes
admitted operation results in a separate simulation before the writer replays
and records them; it is not a live commit protocol.

Exact writer/reader oracles, kernel/artifact hashes and disk hashes are saved.
Both guests must exit normally and the reader must leave the disk hash
unchanged. Blank recovery and cached-head disagreement refuse. The test
establishes economy recovery on codex-vm IDE; it does not establish interrupted
commit, QEMU/virtio, physical power-loss or complete live-shard recovery.

`proofs/EconomyInputProof.codex` also grades byte equality with the executed
source, source isolation, rehashed sequence/outcome/time/argument corruption,
metadata and length bounds, and 4096 actual clock advances; replay retains the
same heap for 2 and for 4096 clock inputs. Frame codec work is fixed-size; replay costs one checkpoint
clone plus the operations and final bounded validation. `EconomyInput` has
five fields (40 native bytes) plus its bounded argument list; results and
frames are caller scratch. Compile normal and poisoned input proofs and
compare complete output to `EconomyInputProof.expected`.

This standalone store is an integration witness, not a separately committed
economy database for the live shard. A live action must enclose world serial
changes, character/economy state and every ledger/trade record in one durable
record, as [WorldAction.md](WorldAction.md) requires. UGC1/TSC1 and the economy
sections still need explicit composition and serial-preserving world binding.
The current storage path remains until the database passes equivalent restart
acceptance under [Database.md](Database.md).
