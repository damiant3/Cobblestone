# GitHub Update 25 -- 2026-06-18

Covers main CLs 4484-4849 (since Update 24 at CL 4483, 2026-06-16).
Two days, 96 copy-ups from four agent streams (val, reek, fester, blu).

## SMP -- all 5 phases complete

Symmetric multiprocessing for x86-64, from zero to done in one day:

- **Phase 1 -- Atomic primitives.** 6 builtins: `atomic-load`,
  `atomic-store`, `atomic-cas`, `atomic-add`, `atomic-exchange`,
  `memory-fence`. x86-64 codegen: LOCK CMPXCHG, LOCK XADD,
  LOCK XCHG, MFENCE.

- **Phase 2 -- Per-core bootstrap.** AP boot via INIT/STARTUP IPI,
  LAPIC emulation in codex-vm (`-smp N` flag), per-core stacks,
  BSP spin-wait until all APs report. Boot guard prevents
  accidental multi-core on single-core builds.

- **Phase 3 -- Work-stealing scheduler.** Per-core run queues,
  lock-free work stealing, core affinity, `CoreState` module
  with LAPIC ID + per-core stack.

- **Phase 4 -- Per-core heap.** `CoreHeap` module: bivy arena split
  equally among cores (8 GB / N cores). Each core gets an
  independent bump allocator — no contention on R10.

- **Phase 5 -- IPI + lock-free channels.** Inter-processor
  interrupts for cross-core wake, TLB shootdown. Lock-free MPSC
  channels for message passing.

Design: `docs/Designs/OS/Active/SMP.md` (moved to Done after
Phase 5 completion).

## 8 GB RAM -- 3 GB ceiling broken

`bare-metal-ram-size` raised from 3 GB to 8 GB. Page tables
now identity-map 8 GB via 8 page directories. Stack at 8 GB,
heap at 6 MB, full ~8 GB arena. codex-vm UEFI memory map
updated to split around the PCI MMIO hole (0xC0000000-0xFFFFFFFF)
for guests >3 GB. Seed rebuilt, all gates green. Build scripts
default to `-mem 8192`.

## GPU compute -- K5-K8 complete

Completes the GPU kernel programming surface:

- **K5 -- Warp intrinsics.** `__shfl_sync`, `__ballot_sync`,
  `__activemask` lowered to PTX `shfl.sync`, `vote.ballot.b32`.
- **K6 -- Shared memory.** `__shared__` qualifier, `__syncthreads`
  lowered to PTX `bar.sync`.
- **K7 -- Atomic operations.** `atomicAdd`, `atomicCAS` lowered
  to PTX `atom.global.add`, `atom.global.cas`.
- **K8 -- Math intrinsics + verifier.** `__expf`, `__logf`,
  `__rsqrtf` lowered to PTX `ex2.approx`, `lg2.approx`,
  `rsqrt.approx`. Verifier integration checks `[Device]`/`[Gpu]`
  capabilities.

New `codex.foreword.gpu` quire (10 modules): Thread, Warp,
Barrier, Shared, Atomic, LaunchConfig, DeviceBuffer, DeviceEffect,
GpuEffect, DisjointSlice. `GpuProxy.gpu-op-launch-ptx` dispatches
kernels via CUDA Driver API (`gpu-dispatch.cu`).

End-to-end test: `gpu-vecadd-e2e` compiles a vector-add kernel
through PTX plug and dispatches to GPU hardware.

## CCE Tier 2 -- CJK, Hangul, Kana, Emoji

Codex Character Encoding gains 3-byte codepoints covering CJK
unified ideographs, Hangul syllables, Katakana, Hiragana, and
emoji. Extends the encoding to global reach beyond EU languages.

## Browser -- network fetch + page compiler

All three browser phases complete:

- **Phase A** (bare-metal integration): keyboard, framebuffer,
  CCE -- all wired.
- **Phase B** (network fetch): TCP fetch via NE2K NIC, HTTP
  request/response, content hash + signature verification.
- **Phase C** (in-browser compilation): `PageCompiler.codex` --
  lightweight tokenizer + parser + evaluator for the slim Codex
  profile (widget construction, data binding, event handling).
  15 native widget functions, curried application, let bindings.

Browser now at 5,162 lines across 19 modules + 4 sample pages.

## Type system advances

- **Higher-kinded types.** `TypeCon` + `TypeApply` constructors,
  symmetric unification, deep-resolve reduction. Enables
  `Functor`, `Monad`, and other type classes.
- **Type class dictionary forwarding.** Constrained functions can
  call other constrained functions -- polymorphic dictionary
  propagation.
- **GADTs.** Constructor return-type annotations, per-branch
  unification state forking, pattern match type refinement.
- **Length-indexed vectors.** `Vector N a` with compile-time
  length checking.
- **Session types.** Typed communication channels with
  send/receive protocol enforcement.

## Trust protocol

- **LeaseManager** -- time-bounded capability leases with
  automatic expiry.
- **PeerDiscovery** -- trust lattice peer discovery protocol.
- **Forensics** -- escalation detection and response.

## Library gap analysis -- 50/50 complete

All 50 gaps from the original CL 1199 analysis are filled:
- Float = `Real approximate` (f32 arithmetic)
- X25519 = `DiffieHellman` (already existed under different name)
- BigInt = new `codex.foreword.core.BigInt` module

LibraryGapAnalysis.md moved to Done.

## DiskFacts log compaction

`disk-compact` in `AppPersist.codex` reclaims space from the
append-only fact log. Scans for latest entry per kind, rewrites
the log, updates superblock. Example: browser re-saves history
on every navigation -- after 100 navigations, compaction reduces
500 sectors to 5.

## Explicit list-map cites

22 app files used `list-map` via transitive cites only. Added
explicit `cites Foreword chapter ListUtils` to all 22, making
dependencies robust against refactoring.

## Globe and 3D demos

- GPU-accelerated globe with NASA Earth texture, icosphere mesh,
  per-pixel lighting, atmospheric glow, mouse orbit
- OpenRelativity port: Lorentz contraction, Doppler shift,
  aberration, Terrell rotation, time dilation
- Black hole general-relativistic ray marcher (scalar + SIMD)
- Procedural planet generator with progressive LOD, rivers, biomes

## Poison-compact stale pointer fix

Root cause of the poison-compact stale pointer bug (active blocker
since CL 3805): `copy-sx-text` had an address-of fast-path that
returned a pointer into the about-to-be-compacted region. Fix:
remove the fast-path, always copy. New seed.

## Test and quality

- 99 blank app test stubs filled with proper test bodies
- 85 app tests + 58 core tests fixed by removing phantom
  `cites Codex chapter General` (chapter never existed)
- 305/305 foreword modules have compile-smoke tests
- Lightest-first batch sorting for test battery
- REPL heap poison for debugging
- `.flags` test sidecar for per-test configuration

## By the numbers

| Metric | Update 24 | Update 25 | Delta |
|--------|----------:|----------:|------:|
| Library modules | 377 | 425 | +48 |
| Foreword quires | 26 | 28 | +2 (gpu, boards) |
| Transpiler plugs | 53 | 53 | -- |
| Seed size | 2.20 MB | 2.30 MB | +100 KB |
| Seed digest | `24FEA310` | `6F75DBC7` | -- |
| Tests | 543 | 742 | +199 |
| Copy-ups | 88 | 96 | -- |
| Days | 3 | 2 | -- |
| Agent streams | 4 | 4 | -- |
| RAM ceiling | 3 GB | 8 GB | +5 GB |
| SMP phases | 0/5 | 5/5 | all |
| GPU kernel phases | K0-K2 | K0-K8 | +6 |
| Browser phases | 0/3 | 3/3 | all |

## What's next

SMP is done for x86-64 -- ARM64/RISC-V backends need the atomic
instruction encoding. GPU compute needs real hardware validation
(K5 end-to-end on NVIDIA). The browser's slim-profile compiler
covers widget construction; data channel binding and event
handling are the next expansion points. Non-contiguous RAM on real
hardware needs page-fault-based MMIO hole skip (codex-vm is done).
