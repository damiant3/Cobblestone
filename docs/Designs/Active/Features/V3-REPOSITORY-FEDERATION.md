# V3 — Repository Federation

**Date**: 2026-03-24
**Status**: **Wired, and NOT yet a defence.** Import-by-hash and the
in-compiler quotation gate shipped 2026-07-13 (val): a `quotes "sha256:…"
as Name` declaration is resolved by content hash on the compile path and
refused with a real diagnostic if it is missing, corrupt, unsigned, signed
by an unknown key, forged, or below the chapter's `trusting above N` floor.
That hole is **CLOSED (2026-07-16)**. The trust manifest was read from the
same untrusted blob the transport supplies, so a malicious transport minted
its own key and passed all four guards. Trusted keys are now **pinned in the
source** (`trusting <fp> <pub> <score>`), and a signer the author did not pin
is CDX3023 (`codex/test/errors/quotes-unknown-signer`). BACKLOG 6.3 is closed
and deleted — do not cite it.
Proposals, verdicts, the keyring with per-key trust, the wire codec,
supersession and proposal resolution were already there. The "V1/V2 done"
foundation this doc was written against was the C# tree, which is retired.
**Prior art**: `docs/Vision/NewRepository.txt`

---

## The Goal

A Codex repository can depend on definitions from other repositories.
Dependencies are identified by content hash, not by name or URL.
Trust flows through a lattice of vouches, not star counts.
Supply chain attacks are structurally impossible.

---

## The Foundation This Doc Was Written Against Is Gone

As written, this design rested on V1 and V2 — the `ViewDefinition` /
`FactStore.Views.cs` machinery of the C# reference compiler. That tree
is **retired**. It lives under `old/` as historical record, and CLAUDE.md
forbids editing, invoking, or rebuilding it. Every "extend
`ViewDefinition`" and "extend `ViewCompiler`" instruction in the
Implementation Plan below is therefore an instruction to modify code we
are not allowed to touch. Read those as intent, not as steps.

## What Actually Exists, In Codex

While this document sat still, most of it got built — under different
names, in `apps/works/RepoProtocol.codex`:

| This doc calls it | The code calls it |
|---|---|
| Proposal workflow (Phase 3) | `Proposal` + `Verdict` records, `create-verdict`, proposal resolution |
| Trust lattice (Phase 2) | The keyring: `keyring-add` carries a per-key `trust` value |
| Network sync (Phase 4) | The wire codec — frame encode/decode for proposals, verdicts, signed annotations |
| Fact identity | `fact-create`, content-addressed; `supersession` maps old hash → new hash |

Signed annotations, an annotation store, and a supersession chain are
all there too — none of which this document anticipated. The proposal /
verdict / keyring triad is the substance of Phases 2 through 4.

## What Was Missing, And What Closed It

Two things, and they were the two that mattered most. Both closed
2026-07-13.

1. **Import-by-hash.** `repo-import repo hash local-name threshold`
   resolves a fact from the local store by its content hash and walks
   four guards in order — present, hash-honest, signed by a key we
   hold, signed by someone we trust — returning `ImportOk` or one of
   `ImportMissing` / `ImportCorrupt` / `ImportUnsigned` /
   `ImportUnknownSigner` / `ImportForged` / `ImportUntrusted score`.
   Facts are now author-signed: `SignedFact` binds a signature over the
   content hash *and* the metadata the hash does not reach (kind,
   author, timestamp), so neither the body nor its attribution can be
   changed without breaking the signature.
2. **The trust gate.** The transitive vouch walk with decay already
   existed (`compute-trust` in `TrustLattice.codex`, depth-capped at
   5) and the inbound *annotation* path was already gated — but
   nothing had ever refused a *definition*. It does now. The pinned
   case: a fact signed by Bob, whose only trust is Alice's full vouch
   for him, scores 8000 after decay; it imports at threshold 5000 and
   is refused at 9000. Same fact, same key, two thresholds.

The claim this makes good on is "supply chain attacks are structurally
impossible" — and it makes good on it exactly as far as the gate
reaches, no further. See the honest limit below.

### The Honest Limit

**This section described a real hole for two days. It is closed, and the
history is worth keeping because of how it was found.**

`gate-import` *is* called from the compile path (inside
`resolve-quotations`), and its four guards — present, hash-honest, signed
by a known key, score at or above the chapter's floor — are real and pinned
by tests. What defeated them: **the trust manifest they checked against —
the fingerprints, the public keys and the scores — was built from the same
`%%QUOTED-WORKS%%` blob the transport supplies.** A malicious transport
generated its own Ed25519 keypair, signed the forged content, computed the
correct digest, and emitted `KEY <own-fp> <own-pub> 10000` beside it.
Present passed. Hash passed. The signer was "known" — the attacker put the
key there. The score cleared any floor. All four guards passed, and only the
content-hash pin constrained anything.

**The fix (2026-07-16): trusted keys are pinned in the source.** A chapter
writes `trusting <fingerprint> <pubkey> <score>` and the manifest is built
from those declarations, which the transport cannot author. A signer the
author did not pin is **CDX3023** regardless of what the blob claims about
it (`codex/test/errors/quotes-unknown-signer`). A chapter that quotes and
declares no floor is CDX3026 — with the keys pinned, an absent floor is a
weak default rather than a hole, but a default chosen by omission is not a
choice, so the gate refuses the chapter before resolving anything.

**How it was caught, because the shape matters more than the bug:** one
agent closed this and wrote that "the compiler refuses an untrusted
quotation" as a security claim. A second agent's adversarial review of that
work found the manifest hole. The first agent then retracted its own close
in writing — *"a correction I owe"* — and the entry was reopened before a
third change fixed it properly. The claim was too strong, the review caught
it, and the retraction is the part of the process that worked.

Still true and much less urgent: a quotation resolves from the **local fact
store only**. Peer and registry resolution do not exist (BACKLOG 6.2). Facts
can be exchanged by file copy, so that one is a reach, not a refusal gap.

### Why The Gate Lives In The Foreword

The quire dependency order is `codex.foreword → codex → codex.os →
apps`, and the compiler is `codex`. It therefore cannot cite
`TrustLattice` (`codex.os.trust`), `KeyManager` (`apps/works`), or
`RepoProtocol` (`apps/works`) — a check confirms the compiler cites
nothing outside `Foreword`. So a gate written in `apps/works` is
unreachable from the only place that could enforce it.

Moving the trust stack down was the obvious fix and the wrong one:
`TrustLattice` alone has ~60 citing chapters across `codex.os` and
`apps`, and the disk fact store (`DiskFacts`) is `codex.os.kernel`.
That is a demolition of the OS layer to serve a gate that needs none
of it.

The split that works is **computing trust vs. enforcing it**.
Computing is rich and expensive — a vouch graph, transitive scores,
decay — and stays in `codex.os.trust`, untouched. Enforcing is small
and cheap — hash, then signature, then score — and lives in
`codex/foreword/core/ImportGate.codex`, needing only `FactStore`,
`Sha256`, `Ed25519`, and `Hamt`.

The seam is `TrustManifest`: fingerprint → public key + score,
materialised by whoever did the expensive part. `RepoProtocol` builds
one from its lattice for the single signer an import names; the
compiler will read one from disk. `gate-import` cannot tell the
difference — which is the point, and is what lets the same refusal
serve both.

Everything below is the design as originally written, kept for context
and because the prose still describes the intended shape correctly even
where the C#-era implementation notes do not.

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

The two open items, in order. Both are Codex work in
`apps/works/RepoProtocol.codex` and the view/build path — nothing here
touches `old/`.

### Item 1: Import-by-Hash

An import specifies a content hash and a local name; the build resolves
it from the local fact store first (no networking required to be
useful — facts can be exchanged by file copy).

- A view carries imported facts: `(hash, local-name)` pairs
- The consistency check resolves imports before type-checking
- Resolution order: local store → known peer → registry
- On receipt: verify hash, type-check against declared type, verify any
  proof, cache
- Tests: import a fact by hash; build a view that uses it; reject a
  fact whose content does not match its hash

### Item 2: The Trust Gate

The keyring already records a trust weight per key. The gate is what
turns that record into a refusal.

- Walk the vouch graph; compute transitive trust with decay
- A view declares `trust-threshold W`
- Facts resolving below the threshold are **rejected at build time** —
  the compiler will not link untrusted code
- Tests: vouch chain, trust decay across two hops, threshold enforcement
  (the load-bearing test: a fact one notch below threshold fails the
  build)

### Already built (Phases 3 and 4 of the original plan)

Proposals, verdicts, supersession, and the wire codec exist in
`RepoProtocol.codex`. What they lack is not structure but reach: they
move proposals between parties, and cannot yet move a *dependency*
between repositories. That is Item 1.

Peer discovery, batch sync, and gossip remain unbuilt, but they are
optimizations of a protocol that already has a frame format — not the
blocker they were when this was written.

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

| Item | What | Effort | Status |
|-------|------|--------|--------|
| 1 | Import-by-hash (local resolution first) | Medium | **Shipped 2026-07-13** (`repo-import`) |
| 2 | Trust gate (threshold + transitive vouch walk) | Medium | **Shipped 2026-07-13** (`import-check-trust`) |
| 3a | Lift the gate into the foreword, where the compiler can reach it | Medium | **Shipped 2026-07-13** (`Foreword chapter ImportGate`) |
| 3b | `quotes "sha256:..." as Name` + `trusting above N` (lexer, parser) | Medium | **Shipped 2026-07-13**. Seed rebuilt. |
| 3c | Compiler loader resolves by hash + calls `gate-import` + CDX diagnostic | Medium | **Shipped 2026-07-13** (Stage 2b). `resolve-quotations` → `gate-import`, `opening.codex:1110`. The compiler refuses. |

### The Word Is `quotes`, Not `import`

`import` is a programming word in a language whose modules are chapters,
whose imports are citations, and whose entry point is an `opening`.

In scholarship you *cite* a work by name and you *quote* it verbatim —
and that is exactly the distinction the repository needs. A citation
resolves through its quire to whatever file is currently on disk under
that name. A quotation resolves through its digest to exactly one text:
the text that hashes to it, or nothing at all. That is the whole
difference between the two, and it is why a quotation can be trusted and
a citation cannot.

```
Chapter: PaymentGateway
  cites Foreword chapter Json
  quotes "sha256:a1b2c3d4..." as JsonParser
  trusting above 5000
```

`quotes`, `trusting` and `above` are real reserved keywords. `as` is
not, and deliberately: it is the idiomatic name for a list (`as`, `bs`,
`cs`) and is used as an ordinary identifier in the compiler itself, in
the foreword, and in the `reverse-reverse` proof. Inside a declaration
that `quotes` has already opened, it needs no protection.
| — | Proposal workflow | — | Built (`RepoProtocol.codex`) |
| — | Wire codec / fact exchange | — | Built (`RepoProtocol.codex`) |
| — | Peer / registry resolution, batch sync, gossip | Large | Open, not blocking — BACKLOG 6.2 |

Items 1 and 2 landed together: a trust gate with nothing to gate is
untestable end-to-end, and an import path that verifies a hash and a
signature but not a signer would have looked closed while leaving the
supply-chain claim open. Neither needed networking — local resolution
plus manual fact exchange exercises both.

Item 3 is what turns the gate from something a *program* can invoke
into something a *build* cannot escape.

---

## External Research (IRISA Harvest, 2026-06-23)

See `docs/Reference/IRISA_Research_Harvest.md` for full context.

### Squirrel Prover — Computational Protocol Verification

The Squirrel prover (SPICY team, IRISA D1) verifies cryptographic
protocols in the **computational model**, not just symbolic. Its
higher-order indistinguishability logic bridges symbolic reasoning
with computational soundness — verified properties hold under
computational hardness assumptions. Key: it handles mutable protocol
state (2022) and probabilistic reasoning for concrete security (2024).

**Applicability to Phase 2 (Trust Model):** Our trust lattice
currently uses symbolic proof terms (Refl, sym, trans, cong) to
reason about trust delegation. Squirrel's approach would let us
prove computational indistinguishability — that an attacker observing
the protocol transcript cannot distinguish between two trust states.
This is a stronger guarantee than symbolic equivalence and maps
directly to our append-only mutation log. Their probabilistic
reasoning could formalize trust decay (the `trust * trust` transitive
formula) under concrete security assumptions.

**Applicability to Phase 4 (Network Sync):** The sync protocol
exchanges signed facts between peers. Squirrel could verify that the
exchange protocol preserves trust transitivity under network
adversary models (man-in-the-middle, replay, selective forwarding).

### INZU — Delay-Tolerant Networking

The INZU team (IRISA D2) studies opportunistic networks:
infrastructure-free, intermittent connectivity, delay tolerance.

**Applicability to Phase 4:** When two Codex nodes connect via
TrustTransport after days of disconnection, they need to reconcile
divergent fact stores. INZU's delay-tolerant networking protocols
could inform the reconciliation strategy — our content-addressed
facts are inherently suited to this (identical changes have
identical hashes, so reconciliation is set-union with conflict
detection only on view composition).

### E4SE — Decentralized Edge Architecture

The E4SE team (IRISA D2) explicitly rejects centralized cloud
in favor of distributing intelligence to edge nodes.

**Applicability to overall design:** Validates the "no central
registry required" principle. Their patterns for data sovereignty
through local processing align with our view model (each node has
its own views, trust thresholds, and local fact stores; federation
is peer-to-peer, not hub-and-spoke).
