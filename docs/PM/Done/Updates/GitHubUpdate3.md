# GitHub Update 3 — CL 619 to CL 678 (2026-05-01 to 2026-05-02)

Previous update: CL 618 (commit `2b38729`).
This update: CL 678.

## CAMP-IIIC: Structured Concurrency on Bare Metal

The structured concurrency runtime shipped end-to-end across 13 CLs
(621–651). The compiler now emits cooperative green threads with
fork/await, effect handlers, and parallel combinators — all running
on bare metal under QEMU with no OS, no libc, no runtime library.

| Feature | CL | What |
|---------|-----|------|
| Sequential fork/await | 623–624 | `fork` allocates a 64 KB stack, copies closure + args, runs body; `await` joins |
| Cooperative green threads | 639 | Round-robin scheduler over 16 process slots; `process-yield` switches context |
| Effect handlers | 643–648 | `with E body op = ...` syntax; handler-basic, handler-args, handler-nested, handler-concurrent all green |
| Cross-function handlers | 649 | Handler clauses can call other functions that perform the handled effect |
| Nested fork allocator | 651 | Fix: inner forks no longer corrupt outer fork's stack frame |
| Par-map / par-reduce | 642 | Foreword combinators using real fork; par-map sample, par-nested sample |
| Process yield V0 | 668 | Bare-metal `process-yield` builtin; round-robin scan across 16 slots |
| Process spawn | 672 | `process-spawn` takes closure, allocates 2 MB per child (1 MB heap + 1 MB stack), `__proc_entry` calls closure then `process-exit` switches back |

Two-process context switch proven: parent spawns child, child writes
to memory and yields, parent resumes and reads the written value.

## ATA PIO Block I/O + On-Disk Fact Store

Bare-metal disk I/O landed from the ground up:

| CL | What |
|----|------|
| 655 | `block-read` / `block-write` syscalls — ATA PIO via INT 13h-style syscall dispatch |
| 658–664 | Bring-up tests, sector-level I/O buffer, `block-read-sector` / `block-write-sector` helpers |
| 665 | **DiskFacts V0** — `foreword/DiskFacts.codex` with superblock, fact-log entries, pack/unpack u8–u64, disk-init/write/checkpoint/load. 5 samples + `tools/test-disk-persistence.ps1` harness with session-level retries for WHPX IDE flakiness |

Two QEMU IDE PIO bugs documented during bring-up (same-session
write-then-read stale cache; cross-session boot-time read failure
~40% of boots). Workarounds: in-memory buffer for same-session reads;
session-level retry for cross-session.

## Phase Architecture

All 6 frontend phases now have per-phase `build` / `phase-measure` /
`phase-compact` (CLs 500, 552, 632–644). The emitter has its own deck
via `emit-build` (CL 644), enforcing a wall between frontend and
emitter allocations.

Per-phase deck breakdown (stage 1, selfhost ~927 KB source):

| Phase | Deck usage |
|-------|------------|
| lex | 27.9 MB |
| parse | 24.8 MB |
| desugar | 14.5 MB |
| scope | 29.0 MB |
| check | 58.8 MB |
| **total** | **155 MB** |

Sub-step localization (CL 663 diagnostic, shelved): `check-chapter`
alone accounts for 56 MB; `resolve-chapter` 18 MB; `parse-document`
15.7 MB; `scan-document` 8 MB.

Bivy (within-phase scratch reclaimed at phase-compact) measured at 8
bytes per phase after right-sizing PhaseStart fields to u32 (CL 676).

## Integer Right-Sizing

Two batches of record-field refinements from `Integer` (8 bytes,
unbounded) to `Integer between 0 and 4294967295` (4 bytes, u32):

| CL | What | Fields |
|----|------|--------|
| 675 | HelpResult1..4 patch positions | 10 |
| 676 | PhaseStart, PhaseMetrics, EmitWorkspace, EffectOpAddr, IsrStubResult | 12 |

PhaseStart record shrank from 16 to 8 bytes (two u64 fields → two
u32 fields), halving bivy-usage per phase.

## Compiler Source Cleanup

- **Const collapse (CL 671).** 110 verbose 3-line constant
  definitions collapsed to single-line `name : Type = value` form
  across 10 files. No semantic change.
- **`[]` sugar for LinkedList (CL 661).** Parser, Lowering,
  TypeCheckerInference, Unifier converted from verbose
  `__linked-list-empty 0` to `[]` syntax with seed refresh.
- **Prose grooming (CL 676).** Removed commit-message-style
  narratives, "Mirrors REF's …" cross-references to retired src/,
  naming-convention apologetics from 8 files.

## OS Stack Design Docs

Three design documents completing the OS stack's design phase:

| CL | Document | Covers |
|----|----------|--------|
| 626 | Verifier (`docs/Designs/Codex.OS/Verifier.md`) | 5-phase verification (integrity, author, capabilities, types, proofs), fuel-limited normalization, CDX binary verification entry point, self-verification bootstrap |
| 627 | Identity (`docs/Designs/Codex.OS/Identity.md`) | Key pairs, first-boot ceremony, trust bootstrap |
| 628 | Kernel (`docs/Designs/Codex.OS/Kernel.md`) | Boot sequence, scheduler, IPC, filesystem (facts on disk), shell |

All design-doc gaps in the OS stack are now closed.

## Reference Library

New `docs/Reference/` directory (CL 670) for archiving external
research papers that post-date the agents' training cutoffs. First
entry:

- Cesario, Zakhour, Weisenburger, Salvaneschi — *Versioned E-Graphs*
  (PLDI 2026). Cited from `Verifier.md` as candidate data structure
  for branch-search proof checking.

## Tooling

- **`tools/test-disk-persistence.ps1`** (CL 665). Multi-phase disk
  persistence test with session-level retries (`-MaxRetries N`) for
  WHPX IDE flakiness.
- **`tools/run-with-disk.ps1`** updated with 3-second pre-kill sleep
  to ensure write flush to backing file.
- Bug documentation consolidated: `docs/Bugs.md` deleted; all bugs
  now in `docs/Bugs/` (CL 674). Two new QEMU IDE PIO bugs documented.

## Seed

```
seed/Codex.Codex.elf    1,813,048 bytes    hard fixed point
```

Self-host: 52 `.codex` files, ~20,900 lines. 39 foreword modules.

## Gate Status

| Gate | Status |
|------|--------|
| BS2 (pingpong) | PASS — stage1 === stage2 byte-identical (881,527 B) |
| BS3 (bootstrap3) | PASS — ELF === ELF byte-identical |
| Sweep | 139 pass, 0 fail, 13 skip of 152 |

## Known Issues

- **WHPX host BSOD.** 7 incidents on 2026-05-02 alone (3 nib, 2 cam
  during sweep, 2 cam during single-guest pingpong). First confirmed
  single-guest BSOD ��� prior incidents all required parallel jobs.
  `kernel-irqchip=off` and `hyperv=off` do not eliminate the bug.
  Upstream: gitlab.com/qemu-project/qemu/-/work_items/3460.
- **QEMU IDE PIO bugs.** Same-session write-then-read returns stale
  data (controller read cache not invalidated). Cross-session reads
  unreliable (~40% of boots return zeros). Workarounds in place.
