# 05 — Repository Model

## Overview

The Codex Repository replaces Git, GitHub, package managers, and artifact registries with a single unified system. It is an **append-only, content-addressed fact store** with a formal social protocol for collaboration.

This is not Git with a new UI. This is a fundamentally different model of how code is stored, versioned, discovered, and trusted.

---

## Core Concepts

### Facts

The fundamental unit of the repository is a **Fact**. A fact is:

- **Immutable**: once published, it never changes
- **Content-addressed**: its identity is the SHA-256 hash of its content
- **Attributed**: it records who published it, when, and why
- **Typed**: it has a fact kind (Definition, Proposal, Verdict, etc.)
- **Connected**: it references other facts by their content hashes

```
Fact
├── Hash        : ContentHash (SHA-256 of canonical serialization)
├── Kind        : FactKind
├── Content     : byte[] (the payload, interpretation depends on Kind)
├── Author      : Identity (cryptographic identity of the publisher)
├── Timestamp   : UTC datetime
├── Justification : Text (why this fact was published)
├── References  : ContentHash[] (facts this fact depends on or relates to)
└── Signature   : byte[] (cryptographic signature by Author)
```

### Fact Kinds

| Kind | Payload | Purpose |
|------|---------|---------|
| `Definition` | Codex source (a function, type, or module definition) | The actual code |
| `Proposal` | A Definition + justification + stakeholder list | Suggested change |
| `Verdict` | Accept / Reject / Amend / Abstain + reasoning | Response to a Proposal |
| `Test` | Test source + results | Empirical verification of a Definition |
| `Benchmark` | Benchmark source + measured results | Performance data |
| `Proof` | Formal proof that a property holds | Mathematical verification |
| `Discussion` | Prose attached to any other fact | Discourse |
| `Supersession` | New Definition + reference to superseded Definition | Versioning |
| `Deprecation` | Reference to a Definition + reason | Soft removal |
| `Trust` | Author A vouches for Fact F to degree D | Reputation |

### Content Addressing

Every fact's identity is the hash of its content. This means:
- Two identical facts are the same fact (deduplication)
- A fact's identity never changes (immutability)
- References between facts are tamper-evident (a hash chain)
- Facts can be stored anywhere — the hash verifies integrity

This is the same principle as Git's object store and IPFS, but applied to semantically meaningful units (definitions, not files).

---

## Views

### What Is a View?

A **View** is a consistent selection of facts from the repository. It maps names to definitions such that all definitions in the view are mutually consistent (no type errors, no unresolved references).

```
View = Name → Definition
  such that type-check(view) succeeds
```

Views replace branches. Instead of "I'm on the feature-x branch," you say "I'm looking at the view where `sort` is defined as merge-sort instead of quicksort."

### Canonical View

The **canonical view** is the view that all stakeholders have agreed to. It is the "main branch" — but it is not a branch, it is a set of facts with full consensus.

### Personal Views

Each developer has a **personal view** that may diverge from the canonical view. When you are working on a new definition, your personal view includes your draft. When the draft becomes a proposal, and the proposal is accepted, the canonical view is updated.

### View Composition

Views can be composed:
- `base-view + override-definition` = a new view where one definition is replaced
- `view-a ∪ view-b` = merge two views (fails if they conflict)
- `view | filter` = a view restricted to certain modules

### View Consistency

A view must be **consistent**: every definition in the view must type-check against every other definition in the view. The repository enforces this. You cannot publish a view that doesn't type-check.

This eliminates the "merge broke the build" problem. If two changes are incompatible, the view that includes both will fail consistency checking, and the conflict must be resolved before the view is published.

---

## Versioning Without Mutation

### Supersession

When you improve a definition, you publish a **Supersession** fact:

```
Supersession
├── new-definition  : ContentHash  (the improved version)
├── supersedes      : ContentHash  (the old version)
├── justification   : Text         (why the new version is better)
└── compatibility   : Compatibility
      | BackwardCompatible    -- old callers work unchanged
      | BreakingChange        -- old callers need to update
      | BugFix                -- same interface, corrected behavior
```

The old definition still exists. It is still addressable by its hash. But the repository knows that a newer version exists and can guide users to it.

### No Deletion

Facts are never deleted. This is a hard rule. If a definition has a vulnerability, you publish a supersession and a deprecation. The old fact remains — marked deprecated, with a pointer to the fix.

This prevents the "left-pad problem" (an author deletes a package and breaks the ecosystem). It prevents the "rewriting history" problem (someone force-pushes and loses work). It prevents the "bit-rot" problem (a URL stops working because the hosting service shut down).

### Version Chains

The supersession graph forms a chain for each logical definition:

```
sort v1 ──superseded-by──► sort v2 ──superseded-by──► sort v3 (current)
```

The Historian (in the environment) shows this chain. You can see every version that ever existed, who wrote it, why it changed.

---

## Proposals and Verdicts

### The Proposal Protocol

Instead of pull requests, Codex uses **Proposals**:

```
Proposal
├── definition     : ContentHash  (the proposed new/changed definition)
├── justification  : Text         (why this change should be made)
├── supersedes     : ContentHash? (the definition it replaces, if any)
├── tests          : ContentHash[] (test facts that the proposal passes)
├── proof          : ContentHash? (formal proof, if applicable)
├── stakeholders   : Identity[]   (whose verdicts are required)
└── comparison     : Diff         (structured diff against the superseded definition)
```

A proposal is a first-class fact. It is immutable. It is content-addressed. It is attributed.

### Verdicts

Stakeholders respond with **Verdict** facts:

```
Verdict
├── proposal   : ContentHash (the proposal being responded to)
├── decision   : Accept | Reject | Amend | Abstain
├── reasoning  : Text (why this decision)
└── amendment  : ContentHash? (if Amend, the suggested modification)
```

A proposal is accepted when all required stakeholders have issued `Accept` or `Abstain` verdicts. This is a formal consensus protocol.

### No Silent Merges

There is no "merge" button. The proposal process is the only way definitions enter the canonical view. This means every change to the canonical codebase is justified, attributed, and agreed-upon.

---

## Discovery and Trust

### Capability-Based Search

Users search the repository by **capability**, not by keyword:

```
search: (List a) → List a
  where sorted result
  where O(n log n) worst case
  where verified sortedness
```

The repository returns definitions that:
1. Match the requested type signature
2. Have proofs covering the requested properties
3. Meet the requested performance characteristics

This is type-directed search (like Hoogle for Haskell) extended with proof and benchmark data.

### Trust Lattice

Every definition has a trust profile:

```
TrustProfile
├── proof-coverage    : Percentage  (what fraction of properties are formally proven)
├── test-coverage     : Percentage  (what fraction of behaviors are tested)
├── benchmark-data    : BenchmarkRecord[] (measured performance)
├── vouchers          : (Identity, Degree)[] (who vouches for this, how strongly)
├── dependents        : Integer    (how many definitions depend on this)
├── age               : Duration   (how long has this existed without supersession)
└── vulnerability-record : VulnerabilityFact[] (known issues)
```

Trust is not a single number. It is a multi-dimensional profile. Different consumers weigh different dimensions differently.

### Vouching

A **Trust** fact says "Author A vouches for Fact F to degree D." Degrees:

| Degree | Meaning |
|--------|---------|
| `Reviewed` | I have read this and it looks correct |
| `Tested` | I have used this in my own code and it works |
| `Verified` | I have checked the proofs and they are sound |
| `Critical` | I depend on this for production systems |

Vouching is transitive within limits: if I trust Alice, and Alice vouches for a definition, I have indirect trust in that definition. The trust lattice computes transitive trust with decay.

---

## Storage Architecture

### Local Store

Each developer has a local fact store — a directory of content-addressed blobs. The store is a simple key-value mapping from `ContentHash → byte[]`.

```
~/.codex/store/
  ab/cd/ef/1234567890...   → blob
  12/34/56/abcdef0123...   → blob
  ...
```

### Synchronization

Facts synchronize between stores using a simple protocol:
1. "I have these hashes. What do you have that I don't?"
2. "Here are the facts I'm missing."

This is gossip-based replication. No central server is required (though one can exist for convenience). Facts are self-verifying (content hash + cryptographic signature), so you can accept facts from any source.

### Index

The local store also maintains an **index** — a queryable database of fact metadata:
- By kind (all Definitions, all Proposals, etc.)
- By author
- By type signature (for capability search)
- By dependency graph (what depends on what)
- By supersession chain
- By trust profile

The index is derived from facts and can be rebuilt from scratch. It is not authoritative — the facts are.

---

## Implementation Strategy

### Phase 1: Local Only
- Content-addressed fact store (local filesystem)
- Facts: Definition only
- Views: single implicit view
- No proposals, no verdicts, no trust
- Enough to store and retrieve compiled Codex definitions

### Phase 2: Versioning
- Supersession facts
- Version chains
- Multiple views
- The Historian can show definition history

### Phase 3: Collaboration
- Proposals and Verdicts
- Stakeholder management
- The formal change protocol

### Phase 4: Distribution
- Fact synchronization between stores
- Gossip protocol
- Optional central relay server

### Phase 5: Trust & Discovery
- Trust lattice
- Vouching
- Capability-based search
- Trust-ranked results

### Phase 6: Federation
- Multiple independent repositories that share facts
- Cross-repository trust
- Global namespace management

---

## Relationship to Existing Systems

| Git Concept | Codex Equivalent |
|-------------|------------------|
| Repository | Fact Store |
| Commit | Fact (immutable, content-addressed) |
| Branch | View |
| Merge | View composition + consistency check |
| Pull Request | Proposal + Verdicts |
| Tag | Named View snapshot |
| `.gitignore` | N/A (no files, only typed facts) |
| Conflict | View inconsistency (detected by type checker) |
| Force push | Impossible (append-only) |
| `git blame` | Fact attribution (always present, unforgeable) |

---

## Open Questions

1. **Namespace governance** — who decides the canonical name for a definition in a shared repository? First-come-first-served? Democratic vote? Some other mechanism?

2. **Storage scalability** — an append-only store grows forever. What garbage collection (if any) is appropriate? Archival tiers? Content deduplication?

3. **Identity system** — how are author identities managed? Public key infrastructure? Decentralized identity? Integration with existing identity providers?

4. **Offline experience** — how much of the repository experience works offline? All of it (local store), with sync when connected? Or is online access required for some operations?

5. **Migration from Git** — can existing Git repositories be imported into the Codex fact store? What is lost in translation? This matters for adoption.

6. Damian asks: what if a required stakeholder is unable to vote possibly ever again? Do we allow timeouts? Proxy voting? Or is the proposal resubmitted?
