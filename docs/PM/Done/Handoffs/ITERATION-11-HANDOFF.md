**Date**: 2026-03-15
**Branch**: `master`
**Remote**: https://github.com/damiant3/NewRepository

---

## What Was Done

### Milestone Status

| Milestone | Status | Notes |
|-----------|--------|-------|
| M0–M10 | ✅ Complete | See ITERATION-10-HANDOFF.md |
| **M11: Collaboration** | **✅ Complete** | Proposals, verdicts, trust, sync, stakeholder management |

### M11 Deliverables

| Deliverable | Status |
|-------------|--------|
| Proposals and Verdicts in the repository | ✅ |
| Stakeholder management | ✅ |
| CLI: `codex propose`, `codex verdict`, `codex proposals` | ✅ |
| Fact synchronization between stores | ✅ |
| Trust facts: vouching | ✅ |

---

## Implementation Details

### New Fact Kinds

| Kind | Purpose |
|------|---------|
| `Proposal` | Suggested change — bundles a definition hash, stakeholder list, optional supersession |
| `Verdict` | Response to a proposal — Accept, Reject, Amend, or Abstain with reasoning |
| `Trust` | Vouching — author vouches for a fact at a degree (Reviewed, Tested, Verified, Critical) |

### New Enums

| Enum | Values |
|------|--------|
| `VerdictDecision` | `Accept`, `Reject`, `Amend`, `Abstain` |
| `TrustDegree` | `Reviewed`, `Tested`, `Verified`, `Critical` |
| `SyncResult` | `readonly record struct` with `Sent` and `Received` counts |

### New Fact Factory Methods

| Method | Purpose |
|--------|---------|
| `Fact.CreateProposal(definition, author, justification, stakeholders, supersedes?)` | Create a proposal fact |
| `Fact.CreateVerdict(proposal, decision, author, reasoning, amendment?)` | Create a verdict fact |
| `Fact.CreateTrust(target, degree, author, reasoning)` | Create a trust/vouching fact |

### New FactStore Methods

| Method | Purpose |
|--------|---------|
| `GetFactsByKind(kind)` | Query all facts of a given kind |
| `GetProposals()` | List all proposal facts |
| `GetVerdicts(proposalHash)` | Get all verdicts for a specific proposal |
| `CheckConsensus(proposalHash)` | Check if all stakeholders have accepted/abstained |
| `AcceptProposal(proposalHash, viewName)` | Apply accepted proposal to the view |
| `GetTrustFacts(targetHash)` | Get all trust/vouch facts for a target |
| `Sync(other)` | Bidirectional fact synchronization between two stores |
| `CollectAllHashes()` | Internal — enumerate all stored fact hashes |

### Static Parsing Helpers

| Method | Purpose |
|--------|---------|
| `FactStore.ParseStakeholders(proposal)` | Extract stakeholder list from proposal content |
| `FactStore.ParseDefinitionHash(proposal)` | Extract definition hash from proposal content |
| `FactStore.ParseVerdictDecision(verdict)` | Extract decision from verdict content |
| `FactStore.ParseTrustDegree(trust)` | Extract trust degree from trust content |

### CLI Commands

| Command | Usage |
|---------|-------|
| `codex propose <file> [--stakeholder <name>]...` | Create a proposal with optional stakeholders |
| `codex verdict <hash> <accept\|reject\|amend\|abstain> [reasoning]` | Issue a verdict on a proposal |
| `codex proposals` | List all proposals with their status |
| `codex vouch <hash> <reviewed\|tested\|verified\|critical> [reasoning]` | Vouch for a fact |
| `codex sync <remote-repo-path>` | Sync facts with another local repository |

### Consensus Protocol

- A proposal with **no stakeholders** is auto-accepted
- A proposal is accepted when **all required stakeholders** have issued `Accept` or `Abstain` verdicts
- A **single `Reject`** from any stakeholder blocks consensus
- `AcceptProposal` applies the proposal's definition to the named view only after consensus

### Sync Protocol

- Bidirectional gossip-based: "I have these hashes, what do you have that I don't?"
- Each store enumerates its fact hashes, then transfers missing facts in both directions
- Sync is idempotent — running it twice transfers nothing the second time
- Works between any two local `FactStore` instances (filesystem paths)

### Architecture Decision: Partial Class Pattern

Added a copilot instruction rule for large file editing: when `Program.cs` or other files exceed ~300 lines and need multiple new methods, use a `partial class` file (e.g., `Program.Collaboration.cs`) to keep edits fast and the UI responsive. Merge back when stable.

- `tools/Codex.Cli/Program.cs` — main dispatch, existing commands
- `tools/Codex.Cli/Program.Collaboration.cs` — the 5 new collaboration commands

---

## Test Count

**229 tests, all passing** (16 Core + 11 Ast + 63 Syntax + 10 Semantics + 92 Types + 14 LSP + 23 Repository)

New tests: **23** (8 Fact unit tests + 11 FactStore integration tests + 4 Sync tests)

---

## Demo

### Create a proposal with stakeholders
```
codex propose sort.codex --stakeholder bob --stakeholder carol
✓ Proposed sort
  proposal: a1b2c3d4e5f6g7h8
  definition: 9f8e7d6c5b4a3210
  stakeholders: bob, carol
```

### Issue verdicts
```
codex verdict a1b2c3d4e5f6g7h8 accept "looks correct"
✓ Verdict: Accept on proposal a1b2c3d4e5f6g7h8
  by bob
  "looks correct"

codex verdict a1b2c3d4e5f6g7h8 accept "verified proofs"
✓ Verdict: Accept on proposal a1b2c3d4e5f6g7h8
  by carol
  "verified proofs"
  ★ Consensus reached — proposal can be accepted into the view.
```

### List proposals
```
codex proposals
Proposals (1):

  a1b2c3d4e5f6g7h8 by alice
    "add merge sort"
    stakeholders: bob, carol
    status: ✓ consensus
```

### Vouch for a fact
```
codex vouch 9f8e7d6c5b4a3210 verified "checked all proofs"
✓ Vouched (Verified) for 9f8e7d6c5b4a32
  by carol
  "checked all proofs"
```

### Sync two repositories
```
codex sync /path/to/other/repo
✓ Sync complete
  sent: 3 fact(s)
  received: 1 fact(s)
```

---

## Key Code Locations

| Task | File |
|------|------|
| Fact kinds, factory methods | `src/Codex.Repository/FactStore.cs` (top) |
| Consensus checking | `src/Codex.Repository/FactStore.cs` (`CheckConsensus`) |
| Sync protocol | `src/Codex.Repository/FactStore.cs` (`Sync`) |
| Trust/vouching | `src/Codex.Repository/FactStore.cs` (`GetTrustFacts`, `CreateTrust`) |
| CLI collaboration commands | `tools/Codex.Cli/Program.Collaboration.cs` |
| Tests | `tests/Codex.Repository.Tests/CollaborationTests.cs` |
| Copilot rule update | `.github/copilot-instructions.md` (Large File Editing Strategy) |

---

## Known Limitations

- **Sync is local-only**: works between two filesystem paths; no network protocol yet
- **No amendment workflow**: `Amend` verdict is stored but not wired to a follow-up proposal
- **No view merge on sync**: syncing transfers facts but doesn't reconcile views
- **No cryptographic signatures**: facts are attributed by username, not signed
- **Stakeholder identity is string-based**: no public key infrastructure yet
- **No exhaustive search index**: `GetFactsByKind` scans all files each time

---

## What's Next

| Milestone | What | Realistic Effort |
|-----------|------|-----------------|
| **M12: Additional Backends** | JavaScript + Rust emitters | 1-2 sessions |
| M13: Self-Hosting | The compiler in Codex, compiling itself | 3-5 sessions |
