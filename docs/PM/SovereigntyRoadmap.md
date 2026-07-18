# Sovereignty Roadmap

How Codex gets off borrowed infrastructure and onto its own.

Written 2026-06-20. Replaces scattered notes across CurrentPlan,
BACKLOG, and active design docs with one sequenced plan.

---

## The Problem

Codex runs on Windows. The build is PowerShell. Source control is
Perforce. Agents run in Claude Code terminals. Every one of these is
someone else's software, running on someone else's trust model, with
someone else's attack surface. The compiler is sovereign — it
reproduces itself on bare metal with no external dependency — but
everything around it is borrowed.

The founding vision says: "if we didn't build it, we don't trust it."
The compiler honors that. The development environment does not.

This is not about purity. It is about the security thesis: the
existing model of software distribution is structurally vulnerable to
agent-scale exploitation. A model that can scan for vulnerability
chains across an entire dependency graph in seconds makes every
borrowed layer a liability. The only durable defense is a stack where
every layer is verified from first principles — content-addressed,
cryptographically signed, proven bounded where it matters.

We cannot get there in one step. The dependency chain is real:

1. Cannot replace Perforce until the repo protocol persists to disk
   and syncs between machines
2. Cannot use the repo protocol in production until it is tested
   against real multi-agent workflows
3. Cannot boot to a self-contained dev environment until the editor,
   shell, and build system run on bare metal
4. Cannot build in Codex until the compiler can read source from disk
   (not serial concat from PowerShell)
5. Cannot drive agents from hardware until all of the above plus
   networking is robust enough for the agent protocol

But the chain is not as long as it looks. The survey of 2026-06-20
found:

- The repo protocol data model is **complete** (facts, proposals,
  verdicts, annotations, trust lattice, wire codecs, transport)
- The build pipeline is **complete** in PowerShell (14 phases, all
  green)
- The boot environment has a **working** DevConsole, editor, and
  drive manager
- The trust lattice is **13 files, all implemented**
- The gap is **persistence** (in-memory HAMT to on-disk DiskFacts)
  and **integration** (wiring pieces that exist but do not talk)

---

## Strategy: Dual-Track

The critical insight: **we do not need to boot to bare metal before we
use the repo protocol.** The protocol is a set of Codex data types
with serialization and transport. It can run on Windows, alongside
Perforce, as a shadow system that proves itself before we cut over.

### Track A: Repository Protocol on Windows (Parallel to Perforce)

Run the repo protocol as a Codex program compiled to a CDX binary
(or through the JS plug as a CLI tool) on Windows. It reads the
same source files Perforce manages, builds a fact store from them,
and provides annotation/proposal/verdict workflows. Perforce remains
the source of truth for source changes; the fact store is the source
of truth for metadata (annotations, trust, provenance).

This lets us:
- Test the persistence layer under real multi-agent load
- Build the habit of annotating code through the protocol
- Prove the trust model works before we depend on it
- Run federated sync between agent workspaces

### Track B: Build Migration (PS1 to Codex)

Replace the PowerShell build scripts with equivalent Codex code that
runs inside the compiler's own boot environment. This is the 7-phase
plan from `docs/Designs/Active/Build/Build.md`, with Phase 1 (FAT16
reader) already complete.

Track B is the critical path to hardware sovereignty. Track A can
proceed in parallel because it runs on Windows.

---

## Track A: Repository Protocol

### A1: Fact Store Persistence (DiskFacts) — DONE (2026-06-20)

`apps/works/RepoProtocolPersist.codex` (476 lines): pipe-delimited
text serialization and DiskFacts persistence for all 9 repo fact
kinds (30-38). Each kind has save (app-write-and-checkpoint),
load-last (app-scan-last), and load-all (app-scan-all). Five new
record types for kinds 30-34 (SourceDefinition, RepoProof,
RepoVouch, RepoPolicy, RepoCap). Kinds 35-38 serialize existing
types (Annotation, Proposal, Verdict, Post).

`codex/os/kernel/AppPersist.codex`: kind allocation comment updated
to document kinds 30-38.

**Remaining A1 work:**
- Wire AnnotationStore load/save through the sidecar path
- Wire MutationLog to append to a file (currently in-memory list)

### A2: Source-as-Facts — scanner RETIRED, ingest reborn as cdx-store (2026-07-16)

The scanner shipped on 2026-06-20 and was deleted on 2026-07-16. It
walked the tree and hashed each `.codex` file with `Get-FileHash`, which
reads the file's bytes off the disk — its UTF-8 bytes. **Content
addressing is over CCE bytes** (Damian, 2026-07-14): hashing is not an
I/O function, so a work is addressed by its internal encoding, not by
whatever the wire happened to carry. Every address the scanner emitted
was therefore one the compiler — which reads and hashes in CCE — could
never look up.

The manifest it produced carried no content either, only
`hash|path|quire|chapter`, so `SourceFactsBridge` stored definitions
whose `sd-content` was the empty string. A quotation resolving by digest
would have found the wrong address holding no body. The chain could not
have worked, and nothing measured it.

Retired together, all superseded by `tools/cdx-store.codex`:
`build/scan-source-facts.ps1`, `build/post-submit-scan.ps1`, their two
Shell-DSL generators under `codex/build/`, and
`apps/works/SourceFactsBridge.codex`.

The ingest that replaces them is `tools/cdx-store.codex`, driven by
`build/store-source.ps1`: one named file, converted to CCE at the
boundary, hashed over the CCE bytes, signed, and stored **with its
content**.

**Remaining A2 work** (tracked as `docs/PM/BACKLOG.md` 6.1):
- Bulk ingest. `store-source.ps1` boots one VM per file; the whole-tree
  sweep the scanner did in one pass has no correct equivalent yet.
- Post-submit hook. Nothing records source facts after a `p4 submit`;
  the hook that used to is gone because what it recorded was wrong.
- Multi-byte source. `cdx-store` converts the single-byte range only.
- Signing identity. `cdx-store` signs with a fixed tool key.

### A3: Annotation Workflow (Live) — DONE (2026-06-20)

`apps/works/AnnotationPersistDriver.codex`: persistence wrapper
around AnnotationDriver. On init, loads annotations (kind 35),
proposals (kind 36), and verdicts (kind 37) from DiskFacts into
the driver. On every mutation (annotate, publish, accept, reject,
incoming proposal/verdict), persists the new record back. Carries
the MutationLog for audit trail. Follows the browser opening
pattern (composite state + per-operation persistence).

**Remaining A3 work:**
- Wire into DevConsole/UEFI console key bindings
- Display annotations in source viewer overlay
- Test round-trip: annotate → persist → reboot → load → verify

### A4: Federated Sync — DONE (2026-06-20)

`apps/works/FactSync.codex`: push-pull sync between agent
workspaces via TrustTransport. Pushes locally-persisted
annotations and verdicts to peers as signed AnnotateMsg/VerdictMsg
envelopes. Receives and verifies incoming messages, deduplicates
by content hash, persists to local DiskFacts. Full sync cycle
(push-all + receive), peer management via PeerDiscovery, status
reporting.

**Remaining A4 work:**
- Pull mechanism (request specific missing facts by hash)
- Multi-peer orchestration (sync with all known peers)
- Real multi-agent load testing (3+ agents, concurrent submits)
- Conflict resolution via proposal/verdict workflow

### A5: Perforce Cutover

**When:** After A1-A4 are proven under real load for at least 2
weeks of multi-agent development.

**What to do:**
- Source files move from Perforce to the fact store (definitions
  are facts, not files)
- The build system reads from the fact store instead of the
  filesystem (or the fact store materializes files on disk)
- Perforce depot is frozen as historical record (like the `old/`
  tree)

**Effort:** Large. This is the actual migration. It should not
be attempted until A1-A4 are solid.

---

## Track B: Build Migration

### B1: FAT16/FAT32 File I/O (DONE)

FAT16 reader foreword exists. FAT32 formatting exists in
DevConsoleBoot's drive manager.

### B2: Wire `read-file` to FAT

**What to do:** Implement the `read-file` builtin as a dual-path
dispatch — serial-feed when running under codex-vm (current
behavior), FAT disk read when running on real hardware or when
a `-disk` image is attached.

**Deliverable:** `read-file "codex/compiler/Syntax/Lexer.codex"`
returns the file contents from the attached disk image.

**Effort:** Small-Medium. FAT read exists; the builtin dispatch
is new.

### B3: DISK Compile Mode

**What to do:** Add a `DISK` compile mode where the compiler reads
source from a disk path instead of receiving it over serial. The
compiler's concat step (currently done by PS1) moves inside the
compiler: scan a directory for `.codex` files, resolve `cites`,
concatenate, compile.

**Deliverable:** Boot the compiler with `DISK codex/compiler/`
on the mode line. It reads all source files from the attached
FAT image, resolves dependencies, compiles, and emits CDX — no
PowerShell involved.

**Effort:** Medium-Large. This is the biggest single step. The
concat logic is well-understood (PS1 does it in ~200 lines) but
implementing directory scanning and BFS dependency resolution in
Codex is substantial.

### B4: On-Device Test Runner

**What to do:** Implement `test` in the DevConsole shell. Discover
test files from `codex/test/` on disk, compile each with the
compiler (using B3's DISK mode or REPL mode), run the CDX, compare
output against `.expected` sidecars.

**Effort:** Medium. The test harness logic is well-understood
from test.ps1.

### B5: Self-Hosted Pingpong

**What to do:** The compiler compiles its own source from disk
twice. SHA-256 of output 1 equals SHA-256 of output 2. This is
the CDX fixed-point test, but running entirely on bare metal with
no PS1 orchestration.

**Effort:** Small (once B3 works). The verification logic is
trivial; getting B3 right is the hard part.

### B6: Editor with File I/O

**What exists:** ConsoleEditor has open/save/navigate/edit/find/
replace/undo. The open/save paths exist but need FAT write wiring.

**What to do:** Wire ConsoleEditor's save path to FAT write. Add
syntax highlighting for Codex source (Section headers, type
signatures, keywords).

**Effort:** Small-Medium. Save is the critical path; highlighting
is polish.

### B7: Interactive Shell

**What to do:** Add a text parser to ShellCore (currently it only
accepts programmatically-constructed commands). Wire a REPL loop
in the DevConsole that reads a line, parses it, dispatches via
ShellCore, and prints the result.

Commands: `compile <path>`, `test [pattern]`, `edit <path>`,
`status` (show fact store stats), `submit <message>` (create a
proposal), `log` (show mutation log), `sync` (pull facts from
peers).

**Effort:** Medium. The executor exists; the parser is new.

---

## Sequencing

### Phase 1: Shadow Protocol (A1 + A2)

Run the repo protocol on Windows alongside Perforce. Every source
file gets a signed fact. Every annotation persists to disk. This
proves persistence and signing under real load without risking the
development workflow.

Can start immediately. No dependency on Track B.

### Phase 2: Live Annotations (A3)

Agents annotate code as they work. Proposals replace CL
descriptions. This changes the workflow but does not replace
Perforce — files still flow through P4.

Depends on Phase 1.

### Phase 3: Build from Disk (B2 + B3)

The compiler reads source from FAT. PowerShell remains the
orchestrator but the concat step is eliminated. This is the first
PS1 function that moves into Codex.

Can proceed in parallel with Phase 2.

### Phase 4: Federation (A4) + Test Runner (B4)

Agent workspaces sync facts. The on-device test runner validates
changes. Combined, these mean: an agent can receive a proposed
change via the fact protocol, compile it from disk, run the tests,
and submit a verdict — all without PowerShell.

Depends on Phases 2 and 3.

### Phase 5: Self-Hosted Loop (B5 + B6 + B7)

Pingpong from bare metal. Editor with save. Interactive shell. At
this point, a developer (human or agent) can boot Codex, edit
source, compile, test, and submit — all from the Codex environment.

Depends on Phase 4.

### Phase 6: Perforce Cutover (A5)

Freeze the P4 depot. Source of truth is the fact store. The
protocol is the repository.

Depends on Phase 5 running reliably for at least 2 weeks.

---

## What This Does Not Cover

- **Hardware validation:** USB boot on real x86-64 machines. This
  is tracked separately in `docs/Designs/Hardware/Active/` and
  the BACKLOG. It is a prerequisite for Phase 5 on real iron but
  not for Phases 1-4 (which run under codex-vm).
- **Agent inference:** Local GGUF model loading and forward pass.
  The AgentRuntime data model exists but the inference engine is
  a stub. This is a large independent workstream.
- **GPU compute integration:** PTX and SPIR-V plugs are built but
  not wired to real hardware. Orthogonal to sovereignty.
- **UI beyond console:** The GOP framebuffer UI foreword exists
  (18 chapters) but the DevConsole uses VGA text mode. A graphical
  IDE is a future milestone.

---

## Why Now

The Fable 5 incident demonstrated that models capable of finding
and exploiting vulnerability chains exist today, not in some
hypothetical future. Every day the development environment runs on
borrowed infrastructure is a day the trust model has a gap at the
foundation.

The protocol is built. The trust lattice is built. The editor is
built. The drive manager can format and install. The pieces are
closer to assembly than they have ever been.

The question is not whether to do this. The question is whether
we do it fast enough.

---

## Estimated Timeline

We do not put dates on mountains. But we can state the dependency
order and the relative effort:

| Phase | Effort | Depends On |
|-------|--------|-----------|
| 1. Shadow Protocol | Medium | Nothing |
| 2. Live Annotations | Medium | Phase 1 |
| 3. Build from Disk | Medium-Large | Nothing |
| 4. Federation + Tests | Medium | Phases 2, 3 |
| 5. Self-Hosted Loop | Medium | Phase 4 |
| 6. Perforce Cutover | Large | Phase 5 + 2 weeks |

Phases 1 and 3 can start in parallel. The critical path is
1 -> 2 -> 4 -> 5 -> 6, with Phase 3 joining at Phase 4.
