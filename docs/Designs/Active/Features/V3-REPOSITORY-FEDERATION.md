# V3 — Repository Federation

**Date**: 2026-03-24
**Status**: Design
**Depends on**: V1 (views, consistency, composition — done), V2 (narration — done)
**Prior art**: `docs/Vision/NewRepository.txt`

---

## The Goal

A Codex repository can depend on definitions from other repositories.
Dependencies are identified by content hash, not by name or URL.
Trust flows through a lattice of vouches, not star counts.
Supply chain attacks are structurally impossible.

---

## What Already Exists

1. **Content-addressed fact store**: Every definition is a fact with a hash.
2. **Named views**: A view selects a subset of facts. `codex build --view X`
   compiles exactly what the view contains.
3. **View composition**: Override, Merge (with conflict detection), Filter.
4. **View consistency**: Type-check all definitions in a view together.
5. **69 ViewTests** covering CRUD, composition, and consistency.

What's missing: cross-repo references, the trust lattice, the proposal
workflow, and the sync protocol.

---

## Design

### Cross-Repo Dependencies

A view can reference facts from other repositories by hash:

```codex
view my-app =
  include local.core
  import "sha256:a1b2c3..." as json-parser   -- external fact, by hash
  import "sha256:d4e5f6..." as http-client
```

The `import` directive says: "this view includes a fact identified by its
content hash." The hash uniquely identifies the definition — its source,
its type, its dependencies. If the hash matches, the fact is the same
regardless of which repository it came from.

### Resolution Protocol

When building a view with external imports:

1. **Local cache check**: Is the fact (by hash) already in the local store?
2. **Peer query**: Ask known peers for the fact. Peers are repositories
   the local repo has synced with before.
3. **Discovery**: If no peer has it, query a registry (a well-known
   repository that indexes facts by hash and type signature).
4. **Verification**: On receipt, verify the hash. Type-check the fact
   against its declared type. If it carries a proof, verify the proof.
5. **Cache**: Store the fact locally for future builds.

This is content-addressable networking, like IPFS or Git's object store,
but for typed, verified program facts.

### The Trust Lattice

Not every fact is equally trustworthy. A fact from your own repository
is fully trusted. A fact from a colleague's repo is mostly trusted.
A fact from an unknown author on the internet is untrusted until vouched.

The trust model:

```
Trust(fact) = max(
  direct_vouch(me, fact),
  max(trust(voucher) * vouch_weight for voucher in vouchers(fact))
)
```

- **Direct vouch**: "I reviewed this fact and trust it." Weight = 1.0.
- **Transitive vouch**: "Alice vouches for this, and I trust Alice at 0.8."
  Effective trust = 0.8 * Alice's vouch weight.
- **Decay**: Trust decreases with distance. Two hops = trust * trust.

Views can set a trust threshold:

```codex
view production-app =
  trust-threshold 0.5   -- only include facts trusted above 0.5
  include ...
  import "sha256:..." as ...
```

Facts below the threshold are rejected at build time. The compiler
won't link untrusted code.

### The Proposal Workflow

Proposals replace pull requests. A proposal is a view diff: "here are the
facts I want to add, modify, or remove."

```
proposal add-json-support =
  add json-parse : Text -> [Parse] JsonValue
  add json-emit  : JsonValue -> Text
  modify config-loader : uses json-parse instead of manual parsing
```

Reviewing a proposal means type-checking the new view (with the proposed
changes applied) and running the test suite. If consistency holds and
tests pass, the proposal is accepted by merging the view.

No branches. No merge conflicts (facts are content-addressed — two
identical changes have the same hash). No "rebase hell."

---

## Implementation Plan

### Phase 1: Cross-Repo References

Add `import` to the view DSL. An import specifies a content hash and
a local name. The build pipeline resolves imports from the local fact
store (no networking yet — facts must be manually copied).

- Extend `ViewDefinition` with `ImportedFact(hash, localName)`
- Extend `ViewCompiler` to resolve imported facts during consistency check
- Tests: import a fact by hash, build a view that uses it

### Phase 2: The Trust Model

Add vouch metadata to facts. A vouch is a signed statement:
"author X trusts fact Y at weight W."

- `Vouch = record { author: PublicKey, fact: Hash, weight: Number, signature: Bytes }`
- Trust computation: walk the vouch graph, compute transitive trust
- View trust threshold: reject facts below threshold during build
- Tests: vouch chain, trust decay, threshold enforcement

### Phase 3: Proposal Workflow

Proposals as first-class objects in the repository.

- `Proposal = record { name: Text, additions: List Fact, removals: List Hash }`
- `apply-proposal : View -> Proposal -> View`
- Consistency check on the resulting view
- Accept/reject based on type-checking + tests

### Phase 4: Network Sync

The actual peer-to-peer protocol for exchanging facts.

- Simple HTTP/REST: `GET /fact/{hash}` returns the fact
- Peer discovery: each repo maintains a list of known peers
- Batch sync: "give me all facts matching these hashes"
- Eventually: gossip protocol for discovery without a central registry

---

## What NOT to Build

- **Git compatibility**: Codex repos are NOT git repos. No branches,
  no commits, no trees. The fact store is the only data model.
- **Package manager**: Facts ARE the packages. There is no separate
  packaging step — `import` by hash IS the dependency declaration.
- **Central registry (required)**: The registry is a convenience, not
  a requirement. Two repos can sync directly. The registry accelerates
  discovery but is not a trust authority.
- **Semantic versioning**: Versions are meaningless when dependencies
  are content-addressed. A "newer" version is just a different fact
  with a different hash. Trust determines which fact you use, not version
  numbers.

---

## Sequencing

| Phase | What | Effort | Blocks |
|-------|------|--------|--------|
| 1 | Cross-repo references (local only) | Medium | V1 (done) |
| 2 | Trust model + vouches | Medium | Phase 1 |
| 3 | Proposal workflow | Medium | Phase 1 |
| 4 | Network sync | Large | Phase 2+3 |

Phases 2 and 3 are independent after Phase 1. Phase 4 is the big lift
(networking, protocol design, security). Phases 1-3 deliver value
without any networking — manual fact exchange via file copy works.
