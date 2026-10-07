# Registered-title checkpoint

`TitleRegistryCodec` preserves the complete title table and committed history
in TRC1. `trc-encode registry tick sequence buffer capacity` returns the encoded
length; `trc-decode buffer length` returns `TitleCheckpoint { registry, tick,
sequence }`. Use `trc-decode-record` with the enclosing record's tick/sequence.
The outer sequence is distinct from the registry's history sequence.

The caller supplies disjoint owned output storage and stable registry state.
Prepared transactions cannot be encoded. An encoding error leaves output
unusable; callers must check the Result. Decode returns detached numeric
records and retains no pointer into the input. Report a decode error, then
reclaim the caller's scratch without publishing its partial candidate.

## TRC1

The fixed length is 475232 bytes. Every cell is a little-endian integer.

| Offset | Content |
|---:|---|
| 0 | Magic `0x31435254` |
| 8 | Version 1 |
| 16 | Total bytes |
| 24 | Configured town-registrar bound |
| 32 | Outer tick |
| 40 | Outer input sequence |
| 48 | FNV32, skipping its own cell |
| 56 | Reserved zero |
| 64 | Used title count |
| 72 | History count |
| 80 | Registry sequence, equal to committed history count |
| 88 | Last registry game hour |
| 96 | 128 title slots, 128 bytes each |
| 16480 | 4096 history slots, 112 bytes each |

Field order is explicit in the codec. No native pointer or authorization
credential enters the format. Unused title/history slots must be zero. Busy
state and undo handles are never persisted. FNV32 detects corruption, not
forgery or authenticated provenance.

## Reconstruction and ownership

Decoding validates dimensions and checksum before allocation/indexing. It
then replays each stored history row through the registry rules into fresh
preallocated state. The emitted row must match the supplied row exactly,
including sequence, parties, mark, revision and references. The resulting
title table and counters must match the stored snapshot. Thus a changed owner
without matching history, a broken consent chain, a changed parent link or
nonzero spare data refuses even after rehashing.

Reconstruction checks internal historical consistency. It does not reauthenticate
old human consent or independently prove the original physical/world events.
The enclosing trusted commit supplies that authority. The shard owner must
also validate current item and legal-party references against the corresponding
world/identity state. Retired title history remains queryable after an item is
gone; a registry payload by itself is not a complete shard checkpoint.

Keep the recovered registry below later request scratch marks. Per-history-row
command/evidence temporaries are reclaimed; persistent titles and history copy
only scalar values. Encoder validation performs the same bounded reconstruction
in scratch and reclaims it after checking the result.

## Cost and evidence

Wire copy/hash work is linear in the fixed payload. Reconstruction costs
O(history * titles), with maxima 4096 and 128. It does not copy the registry
for each row or retain decoded command objects; decode retention does not grow
with the history count, and the proof caps it below 1 MiB.

`proofs/TitleRegistryProof.codex` checks canonical encode/decode equality,
detached ownership, prepared-state refusal, exact outer metadata, truncation,
spare fields and rehashed owner/lineage/consent/sequence corruptions. Normal
and poisoned builds must match the complete `.expected` file. This establishes
native codec recovery, not disk commit, physical power-loss behavior or a live
registration endpoint. [TitleRegistry.md](TitleRegistry.md) owns transaction
and authority boundaries; database migration remains under [Database.md](Database.md).
