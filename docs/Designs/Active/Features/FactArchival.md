# Fact Archival — Ancient History Is Tiered, Not Forgotten

**Date**: 2026-07-16
**Status**: **BUILT, and BACKLOG 6.5 is closed and deleted.** `Works chapter
FactArchive`, pinned by `codex/test/apps/repo-archive` (the base and its three
refusals) and `codex/test/apps/repo-reclaim` (the reclaim and its invariant).
val 8612/main 8613 (base) -> val 8662/main 8663 (reclaim) -> val 8665/main 8667
(read a work back out).

What works: an archival chooses a safe cut from the verified index, copies the
prefix out as a signed compressed artifact, shifts the rest of the log down over
it, and records the base. Measured — log head 10 to 7, two superseded editions
moved out, tree unchanged; the superseded edition comes back out of the base by
hash, byte-exact, and still verifies under `def-verify`. Nothing triggers it: it
is a tool, as asked.

**Residuals, small and deliberate** (none of them a register entry): the base is
Lz4 rather than gzip — a choice now that `Deflate` compresses, and re-basing is a
new base rather than a migration; a store holding a base cannot say "never
stored" without membership evidence (see the lookup section); and nothing walks
the `br-prev-base` chain across more than one base.

The requirement is Damian's
(2026-07-16): *"there should be a provision for archiving/compacting ancient
history. In 2626, I would hope they can, somehow, get back to the code we are
writing today, but I can't imagine it would be useful. We provide the ability,
but not use it until the action is somehow helpful."* And on the artifact:
*"the archive artifact should be some kind of compressed data file with a
signature we record in the active repository as the base."* And on storage:
*"where it lives is a matter of deployment concerns not relevant to the code."*
**No longer blocked on 5.13**: `Deflate` compresses as of val 8646, so a real
gzip base is available whenever the base is re-cut. The first cut shipped on
Lz4; re-basing to gzip is a new base, not a migration.
**Register entry**: BACKLOG 6.5
**Prior art**: `docs/PM/Stories/Vision/NewRepository.txt` (the repository
remembers everything), `docs/Designs/Active/Features/V3-REPOSITORY-FEDERATION.md`

---

## The Requirement, And Why It Is Not A Contradiction

The founding document is absolute: *"If you published a definition, it exists
forever."* A store that reclaims space by dropping old editions breaks that,
and every previous attempt to reclaim space in this tree has been a bug.

Archiving is not deleting, and that is the whole design. A fact that has been
archived is still addressable by its hash, still verifies against it, and is
still retrievable — it simply is not in the live log any more. The live log
stops carrying it and starts carrying **a signed record of where it went**. The
difference between a tier and an amnesia is that a tiered store can still answer
*"that hash exists, and here is which archive holds it"*, where a forgetful one
answers *"not found"*.

That is what makes 2626 reachable. Not that the bytes are in the live log — they
will not be — but that the store never stops naming what it had.

**The ability is provided and not used.** No boot path, no checkpoint, no
write path invokes archival. This is already the established shape here:
`disk-compact` has existed for a long time and **is called by nothing but its
own test** (`codex/test/apps/disk-facts-compact`). Archival follows it. A
capability nobody invokes costs a tool and a test; a capability wired into a
trigger nobody asked for costs history.

---

## What Is Actually True Today (measured 2026-07-16, val)

BACKLOG 6.5 claimed that the absence of a supersession record was load-bearing
for three things. Two of them are false and are corrected here, because the
entry was steering the fix in the wrong direction.

**"There is no current version of a file" — FALSE.** `WorkIndex.wi-current`
maps a path to the hash of its current content, and
`apps/works/RepoProtocolPersist.codex:276` is explicit that this never needed
supersession at all: *"Neither of those needs a supersession record to fix, and
I spent a while believing they did. What they need is a KEY."* Last-write-wins
over the path key is the semantics, the way refs are for git and levels are for
an LSM tree.

**"Nothing can be deleted" — FALSE.** Signed kind-39 tombstones ship, with
`index-remove`, verification in `repo-index-verified`, and timestamp-ordered
replay resistance. Three tests pin them (`repo-tombstone`,
`repo-tombstone-signed`, `repo-tombstone-replay`). The prose at
`RepoProtocolPersist.codex:294` still reads *"What is still missing is REMOVAL
… BACKLOG 6.5"* and the implementation is twenty lines below it. **That line is
stale and should be cut when this work lands.**

**"Compaction cannot reclaim a superseded singleton" — TRUE.** This is the only
surviving claim. `compact-identity` is `kind:hash`
(`codex/os/kernel/AppPersist.codex:236`), so two entries collapse only when they
are the same kind and the same bytes. Nothing else is ever dropped, by design
and correctly — the compactor cannot prove two facts are the same, so it keeps
both.

**The entry's cross-references have rotted.** It cites "6.1b" and "6.1c" for the
two false claims; 6.1's letters were renumbered and now read "no post-submit
hook" and "single-byte UTF-8 only".

**A supersession map exists and is not this.** `RepoProtocol.codex:464` has a
`supersession` HAMT, but it is in-memory, annotation-scoped (D.5), and never
persisted. The disk layer records nothing about replacement.

---

## The Layout, And A Trap In It

The fact log entry header is 78 bytes (`codex/foreword/core/FactLog.codex`):

| Offset | Width | Field |
|---|---|---|
| 0 | 32 | content hash |
| 32 | 2 | kind |
| **34** | **32** | **gap — written by nobody, read by nobody** |
| 66 | 8 | timestamp |
| 74 | 4 | content length |
| 78 | — | content |

**Do not put `replaces` in the gap.** It is real — `pack-fact-entry` writes the
hash, kind, timestamp and length and nothing else, the buffer comes from
`alloc-bytes` which zero-fills, and no reader touches 34–65 — and it is exactly
a hash wide, so it looks free and correct. It is neither.

**The header is outside the signature envelope.** A definition's signature lives
in its content and binds the content hash, the author and the timestamp. The
content hash covers the content, not the header. So a `replaces` field in the
header is authenticated by nothing: anyone who can write a sector could set it
and cause a compactor to drop the fact it names. That is precisely the hole
Authenticated Removal closed for tombstones, in its own words:

> A tombstone was not [signed]: its content was the bare path it retired, so
> anyone who could write a sector could retire any path, and the tree honored
> it. Removal is an assertion like any other, and an unauthenticated assertion
> is exactly the hole the store exists to close.

An archival is an assertion like any other. It is signed, and it is signed the
way tombstones are — in the content, binding what it asserts together with the
author and the timestamp.

The gap stays empty. It is worth a line in `FactLog.codex` saying why, so the
next reader does not rediscover it as an opportunity.

---

## The Design

### The base record — a new signed kind

The active repository records the archive's signature **as its base** (Damian,
2026-07-16). That is the whole relationship: the live log is not the history, it
is the *delta on top of a base*, and the base is one signed, compressed artifact
named from inside the active store. It is the same move a git packfile makes and
the same move an LSM tree makes with its lower levels.

Kind 41, `persist-kind-base`, beside the existing 39 (tombstone) and 40 (index
snapshot). Its content is the wire form of:

```
BaseRecord = record {
  br-artifact-hash : Text,      -- hash of the COMPRESSED artifact bytes
  br-plain-hash    : Text,      -- hash of the decompressed fact log
  br-codec         : Text,      -- the compression format, named
  br-count         : Integer,   -- how many facts the base holds
  br-covers-to     : Integer,   -- log position/timestamp the base subsumes
  br-prev-base     : Text,      -- artifact hash of the base this supersedes ("" if first)
  br-signer        : Text,
  br-signature     : List Integer,
  br-author        : Text,
  br-timestamp     : Integer
}
```

Signing content is `artifact-hash | plain-hash | codec | count | covers-to |
prev-base | author | timestamp`, mirroring `tombstone-signing-content`. An
unsigned or untrusted base record is dropped by the verified reader exactly as
an unsigned tombstone is, and the signed timestamp is what makes replay of an
old base record inert.

**Both hashes are signed, and that is not belt-and-braces.** `br-artifact-hash`
is what you check on the bytes you were handed, before you decompress them —
which is the only order that lets you refuse a decompression bomb, because you
verify before you expand. `br-plain-hash` is what you check on what came out,
which catches a decoder that is subtly wrong rather than absent. A codec bug
that silently produced different bytes would otherwise be indistinguishable from
history.

**The base is singular, and its lineage is a chain.** Each archival produces a
new base subsuming the previous one, and `br-prev-base` names what it replaced.
The active store's base is the newest verified kind-41. The chain means custody
of one artifact is enough to read the history, while the lineage of every base
that ever existed is still auditable from the live log — the records are facts,
and facts are not deleted.

### The archive artifact — a compressed fact log

The artifact decompresses **to a fact log**: the same superblock, the same
78-byte headers, the same entries. One log format, not two. What compression
adds is one decode step in front of it, and the thing on the other side is what
`FactDisk` already reads.

**Where the artifact lives is a deployment concern and not the code's business**
(Damian, 2026-07-16). The store names it by hash and signature; whether those
bytes sit on a second disk, a tape, an object store, or a peer answering 6.2's
by-hash fetch is somebody else's decision, and the design must not encode one.
The code's obligation stops at: name it, sign it, verify it when handed it.

### The codec — and the trap in the compression quire

**Measured 2026-07-16: four of the eight chapters in `codex.foreword.compress`
did not compress**, three of them wearing a standard name while being
pass-through framing — a caller reads the catalog, picks the familiar name, and
ships something larger than its input.

| Chapter | What it does |
|---|---|
| `Deflate` | **Real since val 8646** — fixed-Huffman blocks (BTYPE=01). Was stored-only (BTYPE=00), which added 5 bytes per 65535-byte chunk. `deflate-compress-stored` keeps the old encoder for incompressible data. No BTYPE=02 yet. |
| `Gzip` | **Real since val 8646** — RFC 1952 framing over that Deflate. 600 repetitive bytes: 623 before, 34 after. |
| `Zstd` | *"Zstandard framing (RFC 8878) — raw stored blocks."* **Still compresses nothing.** BACKLOG 5.13. |
| `Brotli` | Stored meta-block framing, honest about it: *"NOT interoperable with RFC 7932 decoders."* **Still compresses nothing.** BACKLOG 5.13. |
| `Lz77` | **Real.** Match/literal tokens over a sliding window. Deflate's matcher. |
| `Huffman` | **Real.** Frequency-built optimal prefix codes. |
| `Lz4` | **Real.** Hash-table match finding, 4-byte minimum match, LZ4 block format. Tested twice. What the first cut of this design ships. |
| `Rle` | Real, and weak — run-length only. |

**The codec should be gzip, and since val 8646 it can be.** `Deflate` emits
fixed-Huffman blocks now, so `gzip-compress` is a real compressor: 600
repetitive bytes go to 34, where the stored-block version produced 623. Interop
is proven both ways against .NET's `GZipStream`.

**A correction worth keeping, because it was mine and it was wrong.** This
section used to argue that a real gzip was needed so a 2626 reader could open
the base *without our code*, implying the stored-block gzip lacked that
property. **It did not.** Measured 2026-07-16: the stored-block output was
already valid RFC 1952/1951 and .NET read it fine at 623 bytes — the framing
was correct all along, it simply never compressed. So the 2626 recovery
argument was already satisfied, and **5.13 was only ever about ratio.** The
conclusion (use gzip) survived; the reason for it did not. Measure your own
claims, not only the inherited ones.

What gzip buys, stated accurately: the most widely implemented format in the
history of computing, so the artifact opens under any zlib without our code,
our compiler, or our disk format — *and* now a real ratio.

**`Lz4` is what the first cut shipped** and remains a legitimate choice: real,
tested, and a better ratio than fixed-Huffman deflate on some inputs. It costs
the universal-decoder property. `br-codec` names the format in the record, so
the choice is recorded rather than assumed and moving to gzip is a new base
rather than a migration.

### What may be archived

**Only facts that are not current, and only below a chosen cut.** A fact is
archivable when both hold:

1. It sits below the cut point (a sector, or a signed timestamp).
2. **No path in `wi-current` points at its hash**, and it is not a tombstone or
   definition that the verified index still needs to resolve a live path.

The second condition is the one that matters and the one that makes archival
index-aware rather than purely temporal. Archiving the current edition of a path
would break the tree, and age says nothing about currency: a file written once in
2026 and never touched again is ancient *and* current.

For collection kinds with no path key — 24 (secret entries), 25 (FileShare
manifests), 26 (revocation records) — there is no notion of currency at all, so
**nothing of those kinds is archivable under this design.** Their members are all
live. Reclaiming them needs a per-kind owner to say what its collection means,
and no owner has been asked. That is out of scope here and should stay out until
someone has a reason.

### Retrieval — the part that is the whole point

`disk-read-fact` today walks the live log and answers `Maybe Fact`. After
archival it must distinguish two kinds of miss:

- **Not found** — no fact with that hash was ever stored here.
- **Archived** — a fact with that hash was stored, and archive record `A` covers
  the range it lived in.

That means the live store keeps enough to answer the second: the archive records
stay in the live log forever (they are small — one per archival, not one per
fact), and a hash miss consults them. Whether a lookup can *resolve* the bytes
depends on whether the archive artifact is attached; whether it can *name* them
does not.

This is also where archival meets 6.2. An archived hash that the local box
cannot resolve is exactly a hash to fetch from a peer, and by-hash fetch is what
6.2 is for. The two compose: the archive record says what you are missing, and
peer resolution goes and gets it. Neither depends on the other being built
first.

### Rebuild

`repo-index-from-disk` and `repo-index-verified` fold definitions and tombstones
in log order. A kind-41 record is neither, so an existing tail walk steps over it
untouched, the same way it steps over the kind-40 snapshot. **The index does not
change.** That falls out of only archiving non-current facts: everything the
index needed is still live, so a rebuild over the archived log produces the same
tree it produced before.

That is the invariant worth testing hardest, and it is the one this design is
built to make testable: **archive, rebuild, and the tree is byte-identical.**

---

## What This Does NOT Do

- **It does not run.** No trigger, no policy, no boot path, no checkpoint.
  `disk-archive` is a tool. Provided, not used.
- **It does not reclaim a collection kind.** 24, 25, 26 have no currency notion;
  their members are all live.
- **It does not make the browser's history blob smaller by itself.** A singleton
  kind still has no key that says which of its facts is current. If that turns
  out to be the thing anyone actually wants, the answer is a per-kind singleton
  declaration (default collection, explicit opt-in) — the same "what it needs is
  a KEY" insight the index used — and it is a *separate, smaller* piece of work
  than this. It is not folded in here because nobody has asked for it.
- **It does not delete anything, ever.** If the archive artifact is lost, that is
  a custody failure, not a store operation. The store's promise is that it never
  stops naming what it held.

---

## Settled

- **Where the artifact lives** — a deployment concern, not the code's (Damian,
  2026-07-16). The store names and verifies; custody is somebody else's problem.
- **One archive or many** — one **base**, superseded by a new base that subsumes
  it, with `br-prev-base` recording the lineage. Custody of one artifact suffices.

## Open Questions

1. **Who signs a base?** Same blocker as 6.1(d): the box identity cannot sign
   (there is no `key-sign` intrinsic — only `key-load`, `key-zero`,
   `key-status`), so this signs with a tool key as `cdx-store` does, and
   **inherits that gap rather than closing it**. Worth stating plainly: a base
   signed by a tool key is only as good as custody of that key.
2. **Does 5.13 gate this, or does it ship on Lz4 and re-base later?** Re-basing
   to a new codec is a normal operation here — a new base, a new record, a new
   signature — so shipping on Lz4 is not a trap the design cannot back out of.
   It is a sequencing call, not an architectural one.

---

## Sequencing

Nothing here blocks anything, and nothing blocks it. Suggested order, each its
own CL with its own test:

**All of it is done** (val 8612, 8662, 8665). The sequence is kept as the record
of how it was built, and because each step's test is still the thing that pins
it. What actually happened against the plan: step 0 (5.13) turned out not to
gate anything, because gzip already interoperated; and the eligibility rule in
step 2 was the whole design rather than a stage of it.

0. ~~**BACKLOG 5.13**~~ — **done for deflate/gzip** (val 8646): fixed-Huffman
   blocks, interop proven both ways against .NET, tests assert the output is
   smaller. Re-basing this design's artifact from Lz4 to gzip is now a choice
   rather than a blocker.
1. `BaseRecord` + wire form + signing/verification, mirroring `Tombstone`.
   Test: sign, verify, reject forged, reject replayed, reject a base whose
   artifact hash does not match the bytes.
2. Archivability: given a verified index and a cut, which facts are eligible.
   Test: the current edition of an ancient path is never eligible.
3. `disk-archive`: write the compressed artifact, write the base record, rewrite
   the live log. Test: **archive, rebuild, tree is byte-identical**; archived
   content still verifies out of the artifact; `br-plain-hash` catches a
   corrupted decode.
4. Retrieval: a hash miss distinguishes not-found from in-the-base.
   Test: an archived hash names its base rather than answering None.

Memory and time: the archive walk is one pass over the log with an index
already in hand, allocating one `CompactEntry`-shaped record per fact, the same
shape `compact-scan` already has. No new hot path.

---

## A Test Of This Cannot Skip Its Disk

`codex/test/apps/repo-archive` carries a 1 MB blank `.disk` sidecar, and it is
load-bearing in a way worth stating here rather than only in the test.

Written without one, the test **passed**. codex-vm drops every write when no
disk is attached and reads return zeros, so it reported a real-looking
compression ratio (3072 zero bytes compress wonderfully) and an exact round-trip
(zeros round-trip perfectly). The archive machinery agreed with itself about
nothing at all. The only assertion that broke was the one that had to read a
fact back off the disk.

This is the same shape as BACKLOG 5.13 — a round-trip test cannot tell a
compressor from a pipe — and as `file-exists` returning True for everything.
**A function that always agrees with itself looks exactly like one that works.**
Any future test here asserts against bytes that had to survive a real write.
