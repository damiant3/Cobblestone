# Current Test Results

**Date:** 2026-04-16
**Commit:** master @ `e84f674`
**Reproduction:** `rm -rf build-output …; dotnet build Codex.sln; wsl bash tools/pingpong.sh`

See `docs/CodexBootstrap.png` for the bootstrap diagram and
`BOOTSTRAP-REPORT.md` for the long-form description.

---

## Summary

| # | Bootstrap | Runtime | Emitter | Verdict |
|---|-----------|---------|---------|---------|
| 1 | Bootstrap 1 | .NET | C# | ✅ PASS |
| 1.1 | Bootstrap 1.1 | .NET | Codex text | ✅ PASS |
| 2 | Bootstrap 2 (pingpong) | bare-metal ELF + QEMU | Codex text | ❌ **FAIL** (sem-equiv) |
| 3 | Bootstrap 3 | bare-metal ELF + QEMU | x86-64 binary | not green by design |

Bootstrap 1 and 1.1 being green does **not** imply bootstrap 2
(pingpong) is green. They run under `dotnet`; pingpong runs on bare
metal.

---

## Bootstrap 1 — .NET, C# output

| Stage | Source | Size | Time |
|-------|--------|------|------|
| Stage 0 | ref compiler emits C# from source | 1,141,723 chars | ~450ms |
| Stage 1 | stage 0 self-host emits C# under `dotnet` | 946,826 chars | ~4.3s |
| Stage 3 | stage 1 self-host emits C# under `dotnet` | 946,826 chars | ~6.3s |

Fixed point: **stage 1 === stage 3** (946,826 chars byte-identical).

Artefacts: `bootstrap1-stage0.cs`, `bootstrap1-stage1.cs`,
`bootstrap1-stage3.cs`.

---

## Bootstrap 1.1 — .NET, Codex-text output

| Stage | Source | Size | Time |
|-------|--------|------|------|
| Stage 1 | stage 0 self-host emits Codex text under `dotnet` | 549,881 chars | ~2.4s |
| Stage 2 | stage 1 self-host emits Codex text under `dotnet` | 549,881 chars | ~2.4s |

Fixed point: **stage 1 === stage 2** (549,881 chars byte-identical).

Artefacts: `bootstrap1.1-stage1.codex`, `bootstrap1.1-stage2.codex`.

---

## Bootstrap 2 (pingpong) — bare-metal ELF, Codex-text output

The reference compiler cross-compiles the self-host to `x86-64-bare`,
producing a 1,040,752-byte ELF (`Codex.Codex.elf`). The ELF runs in
QEMU under KVM with no OS, no libc, no dotnet. Source is fed over
serial; Codex text is captured back.

### Stage 1 — ELF compiles source

| Metric | Value |
|--------|-------|
| Input | source.codex (600,341 bytes) |
| Output | stage 1 (562,740 bytes) |
| Wall time | 39s |
| Stack HWM | 2,497,152 B |
| Heap HWM | 1,011,859,392 B (≈965 MB / ≈1 GB available) |

### sem-equiv(source, stage 1)

| Check | Value |
|-------|-------|
| Verdict | **FAIL** |
| Sigs match | 1,531 / 1,536 |
| Bodies match | 1,536 / 1,536 |
| Dropped | 1 (`foreword--maybe: Maybe (a) (line 1)`) |
| Extra | 2 (`?: Maybe :` orphan; `emit-pattern` pre-existing) |
| Sig mismatches | 5 (all `foreword--maybe`: `from-maybe`, `is-just`, `is-none`, `maybe-map`, `maybe-bind`) |

**Root cause:** `Codex.Codex/Emit/CodexEmitter.codex:11-17`
(`emit-type-def`) destructures `tparams` but never emits them — so
`Maybe (a) =` round-trips to `Maybe =`. The same loss happens in type
references: `Maybe a ->` round-trips to `Maybe ->`. Introduced at
`4420b92` (Merge hex-cam/self-host-maybe, 2026-04-15 22:05); the Maybe
merge added parametric types to self-host but did not update the
Codex-text emitter.

### Stage 2

Gated on sem-equiv. Not run. `bootstrap2-stage2.codex` does not exist.

---

## Bootstrap 3 — bare-metal binary

Not exercised by `pingpong.sh`. Tracked as MM4 Phase 8
(`docs/Active/Compiler/SECOND-BOOTSTRAP.md`). Self-host compiled as
`x86-64-bare` target emitting raw machine code. Fixed point requires
the self-compiled binary to byte-identically reproduce the
reference-compiled binary. Not yet achieved.

---

## Heap HWM delta vs. previous report (2026-04-13)

| Metric | 2026-04-13 | 2026-04-16 | Δ |
|--------|-----------|-------------------|---|
| Bootstrap 2 (pingpong) stage 1 output | 537,984 B | 562,740 B | +5% |
| Bootstrap 2 (pingpong) stage 1 time | 35s | 39s | +11% |
| Bootstrap 2 (pingpong) stage 1 stack HWM | 2,449,040 B | 2,497,152 B | ~same |
| Bootstrap 2 (pingpong) stage 1 heap HWM | 248,474,328 B | 1,011,859,392 B | **4.07×** |

Heap HWM quadrupled because `CDX-C6` (`e9b4ee6`, 2026-04-14) disabled
scalar-reclaim on bare-metal — heap never shrinks during a compile.
That was a correctness fix (prior scheme corrupted records holding
pointers into their own region), not a regression. The ≈1 GB bare-metal
heap absorbs the growth.

---

## Reference Artefacts

| File | Size (B) |
|------|----------|
| `source.codex` | 600,341 |
| `bootstrap1-stage0.cs` | 1,141,723 |
| `bootstrap1-stage1.cs` | 946,826 |
| `bootstrap1-stage3.cs` | 946,826 |
| `bootstrap1.1-stage1.codex` | 549,881 |
| `bootstrap1.1-stage2.codex` | 549,881 |
| `bootstrap2-stage1.codex` | 562,701 |
| `bootstrap2-stage2.codex` | 36 (placeholder: `could not produce, sem-equiv failed`) |

All artefacts regenerated from a clean build (no incremental state).
The placeholder content of `bootstrap2-stage2.codex` is evidence that
bootstrap 2 (pingpong) is red.
