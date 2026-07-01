# Backlog — Outstanding Work

**Updated**: 2026-06-18

## Active — Ongoing

### USB Install (Gap 4)

| # | Item | Notes |
|---|------|-------|
| 1 | **End-to-end USB validation** | All driver/integration layers done (MSC, DriveManager, DevConsole, XHCI). Needs physical USB stick test on Asus + Dell. RAM now 8GB with MMIO-hole split; real hardware needs page-fault skip for the hole (deferred). |

### Memory

| # | Item | Notes |
|---|------|-------|
| 1 | ~~**Non-contiguous physical memory (the real 8GB+)**~~ | DONE. `bare-metal-ram-size` = 8GB, page tables extended, build scripts updated to `-mem 8192`. VM memory map splits around MMIO hole. Real hardware unmapped-hole page-fault skip deferred. |

### Compiler

| # | Item | Notes |
|---|------|-------|
| 1 | **Phase discipline — remaining items** | `docs/Designs/Active/Compiler/PHASE-ARCHITECTURE.md`. Per-phase build/measure/compact and the RESOLVE/LIFT split are done. Open: deck-record toggle ratchet, escape-invariant enforcement, TCO-reset removal, per-phase survey tightening (lex 40x done CL 2306). |

### Apps — Compile Health (2026-06-17 sweep)

Fester swept all app tests 2026-06-17. Key findings:

- **`cites Codex chapter General` removed** (CL 4602/4612 apps,
  CL 4706 codex/test): 85 app tests + 58 core tests cited a chapter
  that never existed. Removing it recovered 100+ test passes.
- **All 53 remaining "failures" are batch heap exhaustion**, not code
  bugs. Every failing test compiles individually. Lightest-first batch
  sorting (CL 4609/4612) mitigates but does not eliminate the issue.
- **27/27 web apps compile clean** through the HTML plug (build-apps.ps1,
  CL 5737). 5 app bugs fixed: tasks (text-to-int), fitness (type mismatch),
  notes (clipboard-write), piano (% -> int-mod), bridge (VoiceHierarchy API).
- **360 foreword modules** (up from 305 at CL 4612).

| # | Item | Notes |
|---|------|-------|
| 1 | ~~**Batch heap exhaustion**~~ | DONE. 8GB RAM landed; batch VM memory increased. Previously-failing tests now pass. |
| 2 | ~~**Bare `list-map` callers**~~ | DONE (CL 4848). `list-map` IS in foreword (ListUtils.codex). 22 files used it via transitive cites only — added explicit `cites Foreword chapter ListUtils` to all 22. |

### SMP

| # | Item | Notes |
|---|------|-------|
| 1 | **Phase 1 -- Atomic primitives** | DONE (CL 4626). 6 builtins: atomic-load, atomic-store, atomic-cas, atomic-add, atomic-exchange, memory-fence. x86-64 LOCK CMPXCHG/XADD/XCHG/MFENCE codegen. ARM64 and RISC-V backends not yet done. |
| 2 | **Phase 2 -- Per-core bootstrap** | DONE. Opt-in via `-smp N` (default single-core). Boot reads core count from GPA 0xFF8; if <= 1, SMP init skipped. AP trampoline, INIT/STARTUP IPI, per-core stacks wired into boot sequence. VM creates N vCPUs + MADT entries. |
| 3 | **Phase 3 -- Per-core scheduler** | DONE. CoreState module: per-core run queues, priority dequeue, work stealing from longest queue, balanced enqueue, idle tracking, schedule-step. AP entry reads LAPIC ID, sets per-core stack, enables LAPIC, signals ready. BSP spin-waits for all APs. |
| 4 | **Phase 4 -- Per-core heap** | DONE. CoreHeap module: per-core arena splitting. Single-core: full 6MB-3GB heap. Multi-core: arenas start above AP stacks (7MB), page-aligned equal split, last core gets remainder. HWM tracking per arena. |
| 5 | **Phase 5 -- IPI + lock-free channels** | DONE. IPI module: typed messages (SchedulerWake, TlbShootdown, PanicHalt), per-core mailboxes, targeted/broadcast/all-but-self delivery, convenience senders. LockFreeChannel module: MPSC circular buffer with head/tail indices, send-from-any-core, recv-on-owner, close, stats. All 5 SMP phases complete for x86-64. |

### Library Gap Closure

| # | Item | Notes |
|---|------|-------|
| 1 | ~~**BigInt**~~ | DONE (fester, 2026-06-18). `codex.foreword.core.BigInt` — sign + base-10000 limbs. add/sub/mul/divmod/compare/pow/factorial/gcd/mod, to-text/from-integer/to-integer. |

### Encoding

| # | Item | Notes |
|---|------|-------|
| 1 | ~~**CCE Tiers 2+ (CJK, rare scripts, emoji)**~~ | DONE (fester, 2026-06-18). Tier 2 block tables: CJK Unified (20992), CJK Extension A (6592), Hangul Syllables (11172), Hiragana (96), Katakana (96), CJK Symbols (64), Thai (256), Misc Symbols (512), Emoji (1024), Dingbats (256). Bidirectional to-unicode/from-unicode, 3-byte encode/decode (framing was already in place), classification (cce-is-cjk/hangul/kana/emoji), letter recognition extended. |

### GPU Compute

| # | Item | Notes |
|---|------|-------|
| 1 | **Dual-target GPU compilation (PTX + SPIR-V)** | Design complete (CL 4424). **K0-K8 done** (fester, 2026-06-18): foreword.gpu quire (10 modules), Device/Gpu effects + capabilities, PTX plug with GPU intrinsics (special regs, warp shuffle, shared mem, atomics, barriers, math), verifier Phase 3/4 integration (CdxBinary + CdxVerifier), GpuProxy launch-ptx, gpu-dispatch.cu CUDA Driver API, vecadd E2E test (.skip -- needs GPU hardware). Only K9 (libdevice path) remains, deferred by design. |

### Compiler — Phase Discipline

| # | Item | Notes |
|---|------|-------|
| 1 | **LOWER deck survey accounts for IR depth** | The LOWER deck formula sizes budget by `def_count * multiplier`. Programs with few defs but deeply nested IR (large literal AST construction) overflow. Workaround: split into many small defs. Root fix: survey formula could factor in IR node count or tree depth, not just def count. See `docs/Test/KNOWN-CONDITIONS.md` and CL 5800. Low priority — workaround is simple. |

### Tooling — Host Stability

| # | Item | Notes |
|---|------|-------|
| 1 | ~~**build.ps1 -mem matches bare-metal-ram-size**~~ | RESOLVED. `bare-metal-ram-size` is now 8GB. Build scripts updated to `-mem 8192`. |
