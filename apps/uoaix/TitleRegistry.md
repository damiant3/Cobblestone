# Town and crown title registry

`TitleRegistry.codex` keeps registered title truth independently of an item's
visible mark. Registration is sparse: an initially unmarked item cannot be
registered. Removing a registered item's mark does not erase its title or
history. Every title is keyed by the stable `WorldObject.serial`,
not item type, graphic, inventory position or a reusable slot number. Committed
history is append-only; retired serials and old owners remain queryable.

The registry does not implement visible/hidden marks, engraving quality,
Forgery or Forensic Evaluation. Those are item-metadata/world operations.
A successful physical forgery does not change registry ownership. An
unregistered item's registry lookup returns no provenance; this component
does not infer its earlier owner or expose hidden marks.

## Identities and evidence

`TrParty` is a legal-party identity: player character 1, NPC 2, household/family
3, town 4, crown 5. IDs are positive through 1000000000000 except the crown,
whose ID is zero. The 0/0 pair denotes no registered owner after release.
These codes are not purse kinds, session IDs or connection IDs. Integrators
must supply immutable, non-reused identities and resolve display names from
identity history, including retired characters.
Family, town and crown actors require validated representation of that legal
party; knowledge of its ID conveys no authority.

`TrEvidence` contains serial, certified presence, marked status, authoritative
title owner, apparent mark owner and physical mark revision. The true owner
and visible claim are distinct. The item adapter derives these facts from
validated current/planned world and item metadata, never client flags. Every
physical re-mark must advance its server-owned revision; clients cannot choose
or reuse that value. Item-serial shape is checked locally, but actual existence,
ownership, quantities and mark operations are the adapter's responsibility.

`tr-new townLimit` admits registrar 0 for the crown and 1..townLimit for towns,
with a configuration limit of 1024. Each mutation's `verified` flag represents
authenticated actor/registrar authority and the operation-specific world
admission described below. The flag is not authentication by itself. A town
registrar's current authority must be checked by the server. The registry
performs no world reads, allowing evidence to be prepared before a WorldAction
lease. No player request may dispatch these functions directly.

## Registration and bilateral transfer

- `tr-register r verified registrar actor evidence gameHour` requires a marked,
  admitted item whose true owner and visible owner both match the authenticated
  actor. The serial must be new to the registry. It returns a title index.
- `tr-offer r verified actor recipient evidence gameHour` requires the current
  registered owner and matching true item owner. It records a distinct recipient
  and returns a positive offer nonce. Ownership does not move yet.
- `tr-cancel r verified actor serial nonce gameHour` clears only the current
  owner's matching offer. A later offer has a different committed nonce.
- `tr-accept r verified recipient nonce evidence gameHour` requires that
  recipient, the active offer nonce and unchanged title revision. It records
  the ownership transfer, clears the offer and marks a lawful re-mark pending.
- `tr-remark r verified actor evidence gameHour` requires the current owner,
  a pending lawful ownership/acquisition change, a matching new visible claim
  and a newer physical mark revision. It certifies that mark exactly once.

Each successful mutation appends history and advances the registry sequence.
Game hours cannot reverse. Offers bind a title revision and recipient; price,
quantity and other trade terms belong to the enclosing trade protocol and
must also be covered by both parties' authenticated consent. A title nonce
alone does not authorize a payment or change the terms of a sale.

`tr-check-mark r evidence` returns 0 without registration, 1 for consistency,
2 for an unchanged certified mark awaiting lawful re-mark after transfer,
3 for a registered mismatch/forgery, 4 for a released title still carrying its
old certified mark, and 5 for retired/absent items. It returns -1 during a
prepared transaction. A mismatch is evidence for game law, not an account
penalty. Comparison never rewrites the registered truth.

## Legal succession and serial lineage

`tr-release r verified actor evidence worldEvent gameHour` records the current
owner's verified discard and releases title. `tr-claim r verified actor evidence
worldEvent gameHour` admits a verified rightful acquisition of that released
item, not a claim over another active owner. Both preserve earlier history.

`tr-succeed r verified oldOwner heir evidence cause worldEvent gameHour` admits
cause 1 character deletion only for a player owner, or cause 2 NPC death only
for an NPC owner. There is no ordinary player-death cause: a ghost retains
title. The world adapter verifies deletion/death and the lawful beneficiary:
named heir then crown for character deletion; family, town, then crown for NPC
death. This module does not choose heirs or alter accounts. Verified cause IDs
must advance per title; reusing an older cause cannot repeat succession.

`tr-derive r verified parentSerial childEvidence gameHour` registers an admitted
split child with the parent's registrar, owner, certified mark, pending-re-mark
state and a parent-provenance link. The adapter must prove the actual split,
quantity conservation and copied item attributes. A fresh serial is required.
`tr-retire r verified serial worldEvent gameHour` records verified destruction
or serial retirement, preserving the last owner and history. Merge quantity,
destination and metadata admission remains with ItemActions/WorldAction; its
immutable world-event reference supplies the retirement evidence.

## Transaction participation

`TrCommand` packages one of the eleven operation kinds, verified admission,
registrar, actor/recipient, evidence, nonce, world event, parent and game hour.
`tr-prepare r command` applies one operation privately and returns `TrUndo`,
including its integer result. The owner must call `tr-rollback undo` on failure,
or `tr-accept-undo undo` only after the encompassing durable commit succeeds.
Both close the handle and refuse a second close.

`tr-prepare-batch r commands` supports 1..64 commands at one game hour, including
repeated changes to a title. A late refusal rolls back the accepted prefix.
Success returns `TrBatchUndo`; use `tr-rollback-batch` or `tr-accept-batch`.
When an offer and acceptance share a batch, the owner derives the offer nonce
from the stable pre-batch registry sequence and its command position.

Successful preparation holds the registry busy. Owner/history/mark queries,
competing mutations and checkpoint encoding refuse until close. The batch's
internal loop temporarily releases its per-command gate; the entire call is
single-owner and must not interleave another reader/writer. Do not inspect raw
records, publish results, nest preparation, mutate commands/handles or restore
request scratch before close. Use only returned module-owned handles; never
construct an undo manually or close individual units inside a batch handle.

The registry, world, item metadata, economy and trade log must participate in
one encompassing transaction. Registry acceptance alone is not a WAL flush or
a complete transfer. Keep registry allocations below request scratch and keep
commands/undo live until close. No whole-registry copy is made per action;
rollback restores touched title before-images and only uncommitted history.

## Public queries and cost

`tr-owner r serial` returns a detached party, retaining the last party for a
retired title. `tr-status r serial` returns unknown 0, active 1, released 2 or
retired 3, and -1 while prepared. Check status when presenting provenance.
`tr-history r serial before limit` returns
copied history rows, newest first, with an exclusive sequence cursor (`before=0`
for newest) and limit 1..16. Anyone may query provenance; caller mutation of a
returned row cannot alter the registry. Query allocations are request scratch.
Parent links can be followed to query a split item's earlier lineage.

History kinds are registration 1, offer 2, cancellation 3, transfer 4,
certified re-mark 5, release 6, reacquisition 7, split derivation 8, retirement 9,
character-deletion succession 10 and NPC-death succession 11. Rows retain
registrar, old/new party IDs, certified mark/revision, title revision, game hour
and operation reference. Physical marks, locations, display names and prices
are supplied by their owning records rather than duplicated here.

Budgets are 128 lifetime titles and 4096 committed history rows. Exhaustion
refuses before effects; it never overwrites history. Native title records have
16 fields (128 bytes), history rows 14 (112 bytes), and registry state 8
(64 bytes), plus lists. Direct mutations allocate no per-event storage after
construction. Title lookup is O(titles), paging O(history) plus at most 16
copies, and a batch O(commands * titles). The proof bounds a 64-command undo
batch below 32 KiB.

[TitleRegistryCodec.md](TitleRegistryCodec.md) owns checkpoint encoding.
`proofs/TitleRegistryProof.codex` grades consent, stale nonces, apparent
forgery, succession, serial reuse, detached queries, single/batch rollback,
late failure, scratch lifetime, capacity and checkpoint reconstruction.
Run normal and poisoned builds against the complete `.expected` oracle.
Authentication, physical marks/forensics, live registration UI, full shard
transaction admission and disk-reboot integration remain external work.
