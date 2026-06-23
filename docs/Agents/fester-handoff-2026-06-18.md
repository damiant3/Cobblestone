# Fester Handoff -- 2026-06-18

## Session Summary

GpuKernels.md implementation: K0 through K4 of the GPU kernel
programming surface. New `codex.foreword.gpu` quire with 10 modules,
compiler capability registration, PTX plug GPU intrinsic lowering,
GpuProxy PTX launch extension, gpu-dispatch.cu driver API integration.

## What Shipped

### K0 — GPU Foreword Quire (10 chapters)

New `codex/foreword/gpu/` quire with:
- **DeviceEffect.codex** — `effect Device where` with 13 operations
  (thread/block/grid index queries + sync-threads). Flat `[Device]`
  effect per Damian's direction (no sub-effects).
- **GpuEffect.codex** — `effect Gpu where` with 5 host-side operations
  (kernel-launch, device-buffer-alloc/from-host/to-host/free). Uses
  `Gpu.Compute` and `Gpu.Memory` sub-effects.
- **DeviceBuffer.codex** — `DeviceBuffer` record, `DeviceArg` variant
  (ArgBuf/ArgBufMut/ArgScalar), `KernelDescriptor` record.
- **LaunchConfig.codex** — `LaunchConfig` record (grid/block dims +
  shared bytes), `launch-config-1d` and `launch-config-for-num-elems`
  constructors.
- **Thread.codex** — `ThreadIndex` witness type, `make-thread-index`,
  `thread-index-get`.
- **Warp.codex** — `WarpMask` record, `warp-full-mask`.
- **Shared.codex** — `SharedArray`, `DynamicSharedArray` records,
  pure constructors.
- **Atomic.codex** — `MemOrdering` variant (5 orderings), `DeviceAtomic`,
  `BlockAtomic`, `SystemAtomic` scope-encoded records.
- **Barrier.codex** — `MBarrierState` variant, `MBarrier` record.
- **DisjointSlice.codex** — `DisjointSlice` record, `disjoint-from-buffer`,
  `disjoint-length`.

11 smoke tests, all compile-verified individually and in battery.

Key discovery: foreword chapters cannot use `[Device]` effect annotations
in function signatures. Effects appear only in `effect ... where` blocks.
Foreword utility functions are pure; the effect is carried by the
declarations in DeviceEffect.codex.

### K1 — Effect + Capability Registration

- **TypeChecker.codex** line 1252: Added `"Device", "Gpu", "Gpu.Compute",
  "Gpu.Memory"` to `granted-capabilities`.
- **X86_64Boot.codex** line 241-243: Added `cap-device = 16`,
  `cap-gpu-compute = 17`, `cap-gpu-memory = 18` capability bits.

Both changes purely additive. No logic changes.

### K2 — PTX Plug GPU Intrinsic Lowering

- **PtxEmitter.codex**: Added GPU Intrinsic Recognition section:
  - `ptx-gpu-special-reg`: Maps 12 Device effect operations to PTX
    special registers (`%tid.x`, `%ctaid.x`, `%ntid.x`, etc.)
  - `ptx-is-gpu-intrinsic`: Recognizes Device effect ops + sync-threads
  - `ptx-emit-gpu-intrinsic`: Emits `mov.u32` from special registers
    with `cvt.s64.u32` widening, or `bar.sync 0` for sync-threads
  - Wired into both `IrName` and `ptx-emit-apply` dispatch
  - `.entry` directive for kernel functions (name prefix `kernel-`)
  - `.reg .u32 %ru<16>` declaration for special register reads

### K3 — GpuProxy PTX Launch Extension

- **GpuProxy.codex**: Added `gpu-op-launch-ptx = 32` opcode,
  `GpuPtxCommand` record (kernel name, sm target, grid/block dims,
  shared bytes, arg count), `gpu-cmd-launch-ptx` constructor.
- **gpu-dispatch.cu**: Added `#include <cuda.h>`, `OP_LAUNCH_PTX = 32`,
  `do_launch_ptx()` function using CUDA Driver API (`cuModuleLoadData`,
  `cuModuleGetFunction`, `cuLaunchKernel`), wired into dispatch switch.

### K4 — End-to-End Test

- **gpu-launch-ptx-test.codex**: Tests PTX command construction,
  LaunchConfig, and DeviceBuffer across all three GPU quire areas.
  Pure logic test (no GPU hardware needed). Compile-verified.

## Build Infrastructure

- **quire-map.ps1**: Added `'Gpu' = 'codex\foreword\gpu'`
- **DevelopersRulebook.md**: Added codex.foreword.gpu quire entry

## Battery

Pre-change baseline: 178 total, 168 pass, 10 skip
Post-change: 188+ total (10 new foreword + 1 app test), 0 failures

## What's Open

### Not Started (K5-K9)
- K5: End-to-end vecadd on actual GPU hardware
- K6: Warp shuffle, shared memory, atomic PTX lowering in plug
- K7: Verifier Phase 3/4 integration for [Gpu] effect
- K8: Math intrinsic lowering via CORDIC -> PTX
- K9: libdevice path (optional, post-K8)

### Design Observation
Foreword chapters cannot use `[Device]` in function type annotations.
The parser/type-checker rejects it outside `effect ... where` blocks.
This means GPU utility functions must be pure, with `[Device]` carried
only by the effect declaration. This is consistent with how Console and
FileSystem work but may limit expressiveness for K6 intrinsic wrappers.
Future compiler work may be needed to allow effect annotations in
foreword function signatures.
