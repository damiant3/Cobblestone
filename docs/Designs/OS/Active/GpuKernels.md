# GPU Kernels — Builder-Facing Compute via Codex-Native PTX

**Author**: Pip
**Date**: 2026-05-08
**Status**: Proposal — for review
**Companion**: [GpuCompute.md](GpuCompute.md) (transport / proxy / firmware constraints)
**Provoked by**: NVlabs/cuda-oxide, dropped 2026-05-08

## Executive Summary

cuda-oxide is NVIDIA Research's new (alpha, 2026-05-08) custom rustc backend
that compiles `#[kernel]`-marked Rust functions to CUDA PTX, with a
matching device-side intrinsic library (`thread`, `warp`, `shared`,
`barrier`, `atomic`, `tma`, `cluster`, `tcgen05`, `wgmma`) and a
host-side RAII runtime. It's a serious effort — 868 TFLOPS GEMM on B200,
58% of cuBLAS SoL — and it answers a question Codex hasn't yet
answered: **how do builders write GPU kernels in their language?**

The implementation cannot be ported. It depends on rustc, Pliron, LLVM 21,
clang, bindgen, Rust nightly, and Linux. Every one of these violates a
Codex rule.

The *ideas*, however, are exactly what Codex needs to expose GPU
compute to builders, and they slot cleanly into work we have already
done (capability refinement, the typed IR, the multi-backend codegen
pattern, GpuProxy/GpuBridge transport, the existing effect-row
machinery).

This doc proposes a **Codex-first GPU kernel programming surface** in
which kernel-ness is carried by the type signature — specifically the
`[Device]` effect — with no inline annotations. New: `[Device]` and
`[Gpu]` effects, a `cap-gpu` capability, an 8th emit mode (PTX), a
`codex.foreword.gpu` quire of intrinsics, and a phased integration path
that augments — not replaces — the existing GpuCompute.md plan.

The name "Pliron" does not appear below this line. We are not adding an
MLIR layer.

---

## 1. Where Codex Is On GPU Compute Today

| Asset | What it does | Lines |
|---|---|---|
| `docs/Designs/Codex.OS/GpuCompute.md` | Transport plan (Nib + Damian, 2026-05-05): proxy / virtio / UEFI GOP / direct-init phases | 181 |
| `codex.foreword.ai/GpuProxy.codex` | Guest-side fixed-op command serialization (matmul, matvec, relu, softmax, conv1d) | 183 |
| `codex.kernel/GpuBridge.codex` | COM3 serial transport for command/response with the host proxy | 136 |
| `codex.build/gpu-dispatch.cu` | Host-side cuBLAS / CUDA dispatcher; 17 fixed ops (matmul through clamp) | 744 |
| `codex.kernel/Pci.codex` (Phase 0 ✅) | PCIe enumeration | — |
| `codex.test/apps/gpu-{bridge,proxy}-test.codex` | End-to-end test samples | — |

**The hard constraint from GpuCompute.md still applies and does not
change here**: NVIDIA GPUs since Turing require signed firmware blobs
(GSP-RM, ACR, SEC2, PMU) to bring up the compute engine. There is no
known way to dispatch CUDA from a bare-metal Codex.OS guest without a
host-side helper holding the firmware-loaded driver. Phase 4 of
GpuCompute.md (direct init) is blocked at the same place it always was.

What this design adds is **what runs once you've crossed the proxy**.
The current proxy carries a closed enum of operations; this design
makes the proxy carry arbitrary Codex-authored kernels compiled to PTX.

## 2. Why Now

User's stated current direction (2026-05-08): integrate the frontend
code into the UEFI console-driven app, and expose **VMs and the GPU**
to end-users and builders.

- **End-users** see the GPU as a capability granted by the trust
  lattice — they run apps that need it.
- **Builders** see the GPU as a *programming surface*. Today there
  is none — they can only call into the closed enum of ops in
  `GpuProxy`. To "expose the GPU to builders" we need a language-level
  way to write kernels in Codex.

cuda-oxide arrived the same day. Its ideas are the right ones; we need
to extract them now while we're designing the builder surface, not
after it's been frozen as a fixed-op shape.

## 3. cuda-oxide Deep Dive

### 3.1 Architecture

```
┌─ rustc frontend ────────────────────────┐
│  parsing → HIR → typecheck → MIR        │   (rustc nightly, plus
│  fully monomorphized, MIR passes done   │    rustc-codegen-cuda backend
└────────┬────────────────────────────────┘    that hooks codegen_crate)
         │
         ▼
┌─ rustc-codegen-cuda backend ────────────┐
│  1. detect kernels: scan CGUs for       │
│     functions in cuda_oxide_kernel_*    │   (#[kernel] attribute renames
│     namespace                           │    to a reserved-prefix symbol)
│  2. collector: walk MIR call graph from │
│     entry points; pull in cuda_device,  │
│     core (iter, Option, …); filter out  │
│     fmt/panic/intrinsic stubs           │
│  3. SPLIT:                              │
│     ├─ DEVICE PATH ─→ Pliron dialect-mir│
│     │                  → mem2reg etc.   │
│     │                  → dialect-llvm   │
│     │                  → LLVM IR (.ll)  │
│     │                  → llc-21 NVPTX   │
│     │                  → .ptx           │
│     │                  → JIT in driver  │
│     └─ HOST PATH ────→ standard rustc   │
│                         LLVM backend    │
└────────┬────────────────────────────────┘
         │
         ▼
┌─ host runtime (cuda-core) ──────────────┐
│  CudaContext (Arc, RAII)                │   uses cuda-bindings (bindgen
│  CudaModule  (cuModuleLoadData ptx_src) │    over cuda.h, NVIDIA license)
│  CudaStream                             │
│  DeviceBuffer<T>  (alloc, htod, dtoh)   │
│  cuda_launch! macro: kernel + config +  │
│    args → packed Vec<*mut c_void> →     │
│    cuLaunchKernel                       │
└─────────────────────────────────────────┘
```

### 3.2 Surface Catalog

**Device intrinsic shelves** (`crates/cuda-device/src/`, ~5500 LOC):

| File | LOC | What |
|---|---:|---|
| `thread.rs` | 449 | `index_1d()`, `index_2d()`, `threadIdx_x/y/z`, `blockIdx_*`, `blockDim_*`, `gridDim_*`; `ThreadIndex` newtype to gate `DisjointSlice` |
| `warp.rs` | 493 | `lane_id()`, `shuffle_xor`, `shuffle_up/down`, `ballot`, `all/any`, butterfly reduction |
| `shared.rs` | 454 | `SharedArray<T,N>`, `DynamicSharedArray<T>` |
| `barrier.rs` | (large) | `mbarrier_init/arrive/wait`, typestate-managed barriers, `TmaBarrier` |
| `atomic.rs` | (large) | scope × ordering matrix: `Device/Block/SystemAtomicU32/I32/U64/I64/F32/F64` × `Relaxed/Acquire/Release/AcqRel/SeqCst` |
| `cluster.rs` | (large) | Hopper thread-block clusters + DSMEM ring exchange |
| `tma.rs` | 585 | TMA descriptors, async bulk tensor copy |
| `tcgen05.rs` | 2285 | Blackwell tensor cores: TMEM, MMA, cta_group::2 |
| `wgmma.rs` | 329 | Hopper warpgroup MMA |
| `fence.rs` | 54 | acq/rel/sc fences |
| `cusimd.rs`, `clc.rs`, `cooperative_groups.rs`, `disjoint.rs`, `debug.rs`, `grid.rs` | — | misc + the `DisjointSlice` safety wrapper |

**Host runtime** (`crates/cuda-core/src/`, ~1700 LOC):

| File | What |
|---|---|
| `context.rs` | `CudaContext` (Arc<>, owns device, primary context) |
| `module.rs` | `CudaModule::load_module_from_ptx_src(&str) -> CUmodule` (driver JIT) |
| `device_buffer.rs` | `DeviceBuffer<T>` — typed device allocation + htod/dtoh |
| `stream.rs` | `CudaStream` — async dispatch queue |
| `event.rs`, `peer.rs`, `vmm.rs` | events, peer access, virtual memory mgmt |
| `launch.rs` | `LaunchConfig { grid_dim, block_dim, shared_mem_bytes }` |

**Launch site** (`crates/cuda-host/src/launch.rs`):

```rust
cuda_launch! {
    kernel: map::<f32, _>,
    stream: stream,
    module: module,
    config: LaunchConfig::for_num_elems(1024),
    args: [move |x: f32| x * factor, slice(input), slice_mut(output)]
}
```

The `cuda_launch!` macro looks up the kernel's PTX entry point name
(via the `CudaKernel` trait the `#[kernel]` macro generates), packs
arguments into `Vec<*mut c_void>`, and calls `cuLaunchKernel`. The
async layer (`cuda-async`) wraps this in `DeviceOperation` /
`DeviceFuture` for composable streams.

**LTOIR pipeline** (`crates/cuda-host/src/ltoir.rs`, 355 LOC): when a
kernel uses transcendentals (`sin`, `cos`, `exp`, `pow`), cuda-oxide
emits NVVM IR (.ll) instead of PTX. Then `libNVVM` + `libdevice.10.bc`
inlines the `__nv_*` math symbols, `nvJitLink` produces a cubin, and
the driver loads the cubin. Both libNVVM and nvJitLink are loaded via
`dlopen` (`libnvvm.so`, `libnvJitLink.so`) at runtime.

### 3.3 The Genuinely Good Ideas

1. **Single-source kernel definitions.** Host and device code in the
   same file, distinguished by `#[kernel]`. The compiler does the split.
   Programmer doesn't manage two source trees.
2. **Kernel name as PTX entry point.** `#[kernel] fn vecadd(...)` lands
   as a `vecadd` symbol in the PTX. Generics get a stable hash suffix
   (`scale_a1b2c3d4` for `scale::<f32>`).
3. **Type-gated parallel write safety.** `DisjointSlice<T>` only accepts
   `ThreadIndex`; `ThreadIndex` is constructible only by `index_1d()` /
   `index_2d()`; those derive from hardware special registers. The
   safety property is enforced at the type level rather than in
   review. (cuda-oxide flags `index_2d` as currently unsound — a
   genuine open problem they're solving with witness types.)
4. **Atomics with explicit scope and ordering in the type.**
   `DeviceAtomicU32` vs `BlockAtomicU32` vs `SystemAtomicU32` is a
   *type distinction*, not a runtime parameter. Same for ordering.
   Maps directly to PTX qualifiers.
5. **Effect-typed launch.** `cuda_launch! { kernel:, config:, args: }`
   is a single-site invocation that bundles the config, the args, the
   PTX entry, and the stream. Fits an algebraic-effect handler model
   one-to-one.
6. **Device → host via libdevice + nvJitLink.** Math intrinsics resolve
   through a well-known library. The mechanism — link-time-optimization
   IR (LTOIR) — is the right primitive: kernel × library → cubin.
7. **Generic monomorphization at the codegen boundary.** The host is a
   single binary; the device gets one PTX entry per concrete instantiation.
   The naming convention is hashed and stable.
8. **PTX as the target ISA**, not SASS. PTX is NVIDIA's blessed virtual
   ISA, JIT-compiled by the driver to per-architecture SASS. Targeting
   PTX means one backend across Turing through Blackwell.

### 3.4 What We Cannot Carry Over

| cuda-oxide piece | Why it doesn't port |
|---|---|
| rustc backend (codegen_crate hook) | rustc isn't here |
| Pliron / dialect-mir / dialect-llvm | An MLIR-style framework solves the "extensible IR" problem; Codex owns its IR end-to-end and does not need it |
| LLVM 21 / NVPTX backend | "If we didn't build it, we don't trust it"; CLAUDE.md rule 5 (no foreign toolchains) |
| `cargo-oxide` build tooling | `codex.works/VmCompile/etc.` is the Codex-native equivalent |
| `cuda-bindings` (bindgen-generated) | NVIDIA Software License on those bindings; we'd write our own |
| Linux-first toolchain (rust-toolchain.toml, ubuntu deps) | Codex is Windows + bare-metal; CLAUDE.md rule 5 again |
| HMM (Heterogeneous Memory Management) | Linux-kernel-6.1+ feature; not available on Windows |
| Pinned Rust nightly + clippy CI plumbing | irrelevant |

The dependency stack cuda-oxide installs (Rust nightly, rustc-dev,
rust-src, CUDA Toolkit 12.x, LLVM 21, clang-21, libclang-common-21-dev,
nvcc, nvJitLink, libNVVM, libdevice) is the *opposite* of the Codex
ethos. We need none of it on the guest. We will need a small subset
on the host for the proxy: just the CUDA driver (for JIT), and
`libdevice.10.bc` if we want transcendentals (and we do).

## 4. Codex-First Design

### 4.1 The Surface Builders See

**No `@kernel` annotation.** Kernel-ness is borne by the type
signature. The effect row IS the contract, and the contract is what
the compiler reads. Anything that has to also be told about behavior
out-of-band is a bug in the design. Adopting an inline annotation to
mark kernels would let a kernel hide as a function and a function
hide as a kernel — exactly the opposite of what we want.

**Two effects**, both declared in `codex.foreword.gpu/GpuEffect.codex`:

- `[Device]` — the body runs *on* the GPU. Functions with `[Device]`
  in their effect row are **kernels** (when the row also has no host
  effects), or **device functions** (callable from kernels). The
  compiler emits PTX for any `cites`-closure reachable from a
  `[Device]`-effected definition. The available intrinsics inside a
  `[Device]` body are exactly those declared in the
  `codex.foreword.gpu/{Thread,Warp,Block,Grid,Shared,Atomic,Barrier,
  Cluster,Tma,Tcgen05,Wgmma}.codex` chapters — each of those carries
  `[Device]` in its operation signatures.
- `[Gpu]` — the *caller-side* effect of launching kernels and moving
  buffers. `kernel-launch`, `device-buffer-from-host`,
  `device-buffer-zeroed`, `device-buffer-to-host` all carry `[Gpu]`.
  A function with `[Gpu]` in its effect row is host code talking to
  the GPU, not running on it.

A program needing to launch kernels has `[Gpu]` in `opening`'s
effect row. A kernel definition has `[Device]` in its return type
row. The two never appear in the same effect row on the same
function — host functions are not kernels, kernels are not host
functions. The launch operator is the bridge: it accepts a
`[Device]`-typed function and discharges into `[Gpu]`.

`[Device]` is also the marker the verifier and PTX backend read to
do the host/device source split. **No annotation is harvested for
this purpose. The type system carries it.**

**Capability**: `cap-gpu` (new bit, new entry in the capability
table). Granted at process spawn time; revocable via
`process-restrict-cap`. The 5-phase verifier (Phase 3) refuses to
load a CDX that uses `[Gpu]` without `cap-gpu` declared. `[Device]`
does not need a capability check at the same level — it's never
exercised host-side; reaching `[Device]` code from outside the
kernel boundary is a type error.

**Intrinsic shelf**: `codex.foreword.gpu/`, a new quire — chapters
mirror cuda-oxide's catalog where the hardware semantics are universal:

| Chapter | Maps to cuda-oxide | Notes |
|---|---|---|
| `Thread.codex` | `thread.rs` | `index-1d`, `index-2d`, `thread-idx-x/y/z`, `block-idx-*`, `block-dim-*`, `grid-dim-*`, `sync-threads` |
| `Warp.codex` | `warp.rs` | `lane-id`, `shuffle-xor`, `shuffle-up/down`, `ballot`, `all`, `any` |
| `Block.codex` | (split out of thread+barrier) | block-scope sync + dim queries |
| `Grid.codex` | `grid.rs` | grid-scope queries + cooperative groups primitives |
| `Shared.codex` | `shared.rs` | `SharedArray T n`, `DynamicSharedArray T` |
| `Atomic.codex` | `atomic.rs` | three records `DeviceAtomic`, `BlockAtomic`, `SystemAtomic`; ordering as a sum type `Relaxed \| Acquire \| Release \| AcqRel \| SeqCst`. Type carries scope; ordering is an arg |
| `Barrier.codex` | `barrier.rs` | `mbarrier-init/arrive/wait`, typestate transitions encoded in linear types |
| `Cluster.codex` | `cluster.rs` | Hopper+ only; emits stubs for older arches |
| `Tma.codex` | `tma.rs` | Hopper+ only |
| `Tcgen05.codex` | `tcgen05.rs` | Blackwell sm_100a+ only |
| `Wgmma.codex` | `wgmma.rs` | Hopper sm_90a+ only |
| `DisjointSlice.codex` | `disjoint.rs` | The witness-typed parallel-write surface |
| `LaunchConfig.codex` | `cuda-core/launch.rs` | `LaunchConfig { grid : (Integer, Integer, Integer), block : (Integer, Integer, Integer), shared-bytes : Integer }` plus `for-num-elems n` helper |
| `GpuEffect.codex` | (new) | `effect Gpu where ...`; `kernel-launch` op |

**Worked example** — vecadd as a builder writes it:

```codex
Chapter: VecAdd
  cites Foreword chapter Tensor
  cites Gpu chapter Thread
  cites Gpu chapter DisjointSlice
  cites Gpu chapter LaunchConfig

 Section: Kernel

  vecadd : List F32 -> List F32 -> DisjointSlice F32 -> [Device] Nothing
  vecadd (a) (b) (c) = act
    let idx = index-1d
    in when disjoint-get-mut c idx
      is None -> ()
      is Just (slot) -> *slot <- list-at a (thread-index-get idx)
                              + list-at b (thread-index-get idx)
  end

 Section: Host

  opening : [Console, Gpu] Nothing = act
    let n = 1024
    in let a = list-range 0 n
    in let b = list-range 0 n
    in let dev-a = device-buffer-from-host a
    in let dev-b = device-buffer-from-host b
    in let dev-c = device-buffer-zeroed n
    in kernel-launch
         vecadd
         (launch-config-for-num-elems n)
         [arg-buf dev-a, arg-buf dev-b, arg-buf-mut dev-c]
    let result = device-buffer-to-host dev-c
    in print-line (show (list-at result 0))
  end
```

The `[Device]` effect on `vecadd`'s return type is what marks it as
a kernel — there is nothing else marking it. The type checker enforces
the contract: `vecadd`'s body may only use operations whose effect
rows are subsets of `[Device]` (so `index-1d`, `disjoint-get-mut`,
list-at on a CDX-resident input are fine; `print-line` is rejected
because `[Console]` is not `[Device]`). `opening` carries
`[Console, Gpu]` because it both prints and launches a kernel; the
loader checks the binding against `cap-gpu` (and `cap-console`) at
load time. The `kernel-launch` operator is what bridges: it accepts
a function whose effect row is `[Device]` and discharges into
`[Gpu]` on the caller side.

The compiler's host/device split is mechanical: walk the
`cites`/call graph from every `[Device]`-effected definition, collect
the transitive closure, emit those to PTX. Everything else compiles
to host CDX. No source-level annotation is consulted because none
exists.

### 4.2 The Backend

**8th emit mode: PTX.**

```
Source (.codex)
    → Lexer / Parser / Desugarer / ChapterScoper / NameResolver / TypeChecker / Lowering
    → Codex Typed IR
    → Emitter
        ├─ TEXT  (round-trip)
        ├─ CDX   (canonical seed)
        ├─ ELF   (x86-64 bare metal)
        ├─ EFI   (PE32+ UEFI)
        ├─ IMG   (GPT disk)
        ├─ DISK  (ATA)
        ├─ MEASURE
        └─ PTX   (NEW — device kernels only)
```

A single source file produces:

1. A **CDX** carrying the host-side code (every definition outside the
   device-reachable closure) plus a manifest table of kernel descriptors:
   `(name, ptx-bytes, sm-target, shared-mem-budget, effect-row,
   capability-set)`.
2. Zero or more **PTX modules**, one per `[Device]`-effected definition
   in the device-reachable closure. Generic kernels emit one PTX per
   type instantiation, named with a stable hash suffix (cuda-oxide
   pattern: `scale_a1b2c3d4`).

**No LLVM. No Pliron. No NVVM IR.** Codex IR lowers directly to PTX
text, using the same direct-emission discipline that the
x86-64/ARM64/RISC-V backends already use. PTX is a typed assembly with
a small, stable surface; emitting it is a job the existing emitter
generalizes to. Three PTX-specific pieces:

- `codex/Emit/PtxRegisterAllocator.codex` — virtual-register style
  (PTX uses %rd<N>, %f<N> etc., effectively unbounded virtual regs;
  the driver does the real allocation at JIT time).
- `codex/Emit/PtxOpEncoding.codex` — instruction encoding (PTX is
  text, so this is text formatting, not bit-packing).
- `codex/Emit/PtxIntrinsicLowering.codex` — maps the
  `codex.foreword.gpu` chapter calls to the corresponding PTX
  instruction sequences (e.g. `index-1d` → load `%tid.x`, `%ctaid.x`,
  `%ntid.x`, multiply-add).

**Kernel detection** lands in TypeChecker, not in NameResolver and
not in lowering: it falls out of effect inference. After
type-checking, walk every definition whose effect row contains
`[Device]`. Each is a device root. From each root, transitively
collect every `cites`-reachable definition whose effect row is also
a subset of `[Device]`. That set is the device-reachable closure;
emit it to PTX. Everything outside that closure is host code; emit
it to CDX. The walker is a small TypeChecker post-pass — no separate
annotation collector is needed. (cuda-oxide does this with a MIR
collector starting from `#[kernel]`-marked functions; we do it
straight from the effect row.)

**Subset rule.** A `[Device]` definition is rejected if any
operation it transitively calls has an effect outside `[Device]` —
e.g., a kernel that calls `print-line` is a type error before it
ever reaches the PTX backend. This is normal effect-row checking,
not new machinery.

**Generic monomorphization** for `[Device]`-effected definitions
happens during lowering. Same pattern as cuda-oxide: monomorphize,
hash the type-instantiation, emit the suffixed PTX entry.

**Math intrinsics** (`sin`, `cos`, `exp`, `log`, `pow`, …): two paths.

- *Codex pure path*: implement transcendentals in pure Codex (the
  `codex.foreword.math/` quire already has CORDIC, Quaternion, Matrix4,
  Bezier, Catmull-Rom Spline, Geodesic, Complex). Lower the foreword
  calls to PTX directly. No host dependency.
- *libdevice path* (optional, opt-in): emit calls to `__nv_sin` etc.,
  then the host links libdevice via libNVVM at module-load time.
  Faster, but adds a host dependency (libdevice.10.bc from the CUDA
  Toolkit). Behind a build flag — default off, default is "all-Codex."

We start with the Codex pure path. CORDIC is already there. Adding
libdevice is a future option, not the entry point.

### 4.3 The Runtime

**Near-term (firmware constraint stays)**: extend the existing proxy
model to carry PTX modules.

`GpuProxy.codex` and `gpu-dispatch.cu` today carry a closed enum of
operations. The change is to add a new op kind:

```
gpu-op-launch-ptx : Integer = 32   (new)
```

The command record carries: kernel name, sm target, grid/block dims,
shared-mem bytes, argument byte-blob, plus the PTX bytes themselves
(or a content-addressed hash if the host has cached the module).

`gpu-dispatch.cu` adds a code path: receive PTX, call
`cuModuleLoadData(ptx_src)`, find the kernel by name, call
`cuLaunchKernel` with the marshaled args, copy output back. Existing
fixed-op paths stay — they're cheaper and tested — until the PTX
path is benchmarked competitive.

**Long-term**: the `direct-init` Phase 4 of GpuCompute.md is still
gated on the firmware problem. If/when that opens, the same PTX
modules drop straight into a Codex-native CUDA driver binding —
nothing in the kernel-language layer changes.

### 4.4 Trust and Verification Integration

The 5-phase verifier already has a Phase 4 (effect metadata) and
Phase 3 (capabilities). Both extend cleanly:

- **Phase 3 (capability check)**: a CDX carrying `[Gpu]` in any
  function's effect row must declare `cap-gpu` in its capability
  table. The loader rejects unverified CDXs trying to use `[Gpu]`.
- **Phase 4 (effect/capability consistency)**: each `[Device]`-effected
  definition's effect row is recorded in the CDX kernel manifest; the
  verifier checks that the recorded row matches the kernel's actual
  call graph.
- **Phase 5 (proof verification)**: kernels can carry proofs about
  their behavior (e.g. "this kernel does not write outside the
  DisjointSlice it is given"). The verifier's fact-store lookup
  applies the same way.

Trust lattice: the GPU itself, treated as an identity, can be vouched
for by the device-seed Ed25519 key. Untrusted kernels (signed by an
identity below the trust threshold) are not loaded. This is just the
existing trust pattern applied to a new resource.

PTX modules are signed using the same Ed25519 mechanism the seed CDX
uses (CL 751). The signing key is whichever identity built the host
binary — typically the same author that signed the CDX.

### 4.5 Memory Model and Heap Discipline

PTX emission lives inside the existing emit phase. Per
`PHASE-ARCHITECTURE.md`, the emitter has its own deck (CL 644 emitter
wall). The PTX backend allocates its own per-kernel `emit-build`,
emits the PTX text into the emitter deck, and `seal`s it before the
next kernel emits.

No retention of device-IR across phases — we are explicitly avoiding
the Pliron-shaped temptation to keep an extensible IR around. Each
kernel goes IR → PTX text → CDX manifest entry, then its IR is
struck.

The host CDX carries the PTX as inline byte arrays (length-prefixed),
so the *runtime* memory cost is just whatever PTX modules are loaded;
the *compile-time* cost is bounded by the largest single kernel's
emit deck. cuda-oxide's bigger kernels (gemm_sol) produce ~25 KB of
PTX; that's well below any phase-discipline threshold.

---

## 5. Integration With GpuCompute.md

GpuCompute.md is **augmented**, not replaced. Its phases:

| Phase | Status | Effect of this design |
|---|---|---|
| **Phase 0** — PCIe enumeration foreword | ✅ done | Unchanged |
| **Phase 1** — Shared-memory GPU proxy (closed enum of ops) | In progress | **Augmented**: the existing 17-op enum stays; a new `gpu-op-launch-ptx` op is added. Builders gain a PTX path; existing tensor ops remain the fast path for fixed shapes |
| **Phase 2** — Virtio GPU compute device | Not started | **Augmented**: virtqueue command format gets a `launch-ptx` variant; the rest is unchanged |
| **Phase 3** — UEFI GOP framebuffer | Not started | Orthogonal — display, not compute. Untouched |
| **Phase 4** — Direct GPU init | Blocked (firmware) | Untouched. If it ever opens, the same PTX modules slot in |

This design **adds** a parallel track of language-and-codegen phases
that interleave with the transport phases above. They live entirely
in the compiler and the foreword; they touch the runtime only at the
proxy-extension point.

What this design **subsumes**: the assumption baked into
`GpuProxy.codex` that the set of GPU operations is fixed at compile
time of the host dispatcher. Builders no longer wait for someone to
add an op to `gpu-dispatch.cu`. They author kernels in Codex.

What this design **diverges from**: nothing in GpuCompute.md is
reversed. The fixed-op path stays as the small-shape fast path
(CPU < tiny-GPU < cuBLAS, per Nib's perf budget table — those
crossover points are unchanged for the existing ops). The PTX path
opens the long tail.

---

## 6. Phasing — CL-Sized Chunks

Numbered K0–K9. Each is a single thing per CLAUDE.md rule 3.

| CL | What | Gates |
|---|---|---|
| **K0** | `codex.foreword.gpu/` skeleton: chapter files with type signatures only, declaring `[Device]` and `[Gpu]` effects, no codegen lowering. `cites` graph valid. Pure types — no bodies that need PTX | sweep |
| **K1** | Effect-system support for `[Device]` and `[Gpu]`: register both effects, teach the type checker the subset rule (a `[Device]` body cannot call a non-`[Device]` operation), wire negative tests for the subset violation | pingpong + sweep |
| **K2** | TypeChecker post-pass: walk effect rows, collect every definition with `[Device]` ∈ row, transitively close over `cites`. Partition the IR into device-reachable and host-only sets. Output both partitions ready for emit | pingpong + sweep |
| **K3** | PTX emit mode (smallest viable subset): integer arith, simple memory access, function calls. Compile a hand-written `vecadd` Codex kernel, dump PTX text, eyeball-verify against cuda-oxide's `vecadd.ptx` reference | pingpong + sweep |
| **K4** | Proxy extension: `gpu-op-launch-ptx` in `GpuProxy.codex`; `gpu-dispatch.cu` learns to `cuModuleLoadData`. End-to-end identity matmul through the new path | sweep + a new gpu-launch-ptx-test sample |
| **K5** | Working `vecadd` example end-to-end: pure Codex source → Codex compiler → CDX (host) + PTX (device) → host runs through proxy → RTX 4060 Ti executes → result returns | sweep |
| **K6** | `Warp.codex`, `Shared.codex`, `Atomic.codex` lowerings: the bread-and-butter intrinsics. Adds the Atomic scope/ordering type-encoding and warp shuffle PTX. Demonstrates with a warp-reduction sample | pingpong + sweep |
| **K7** | Effect + capability integration: `[Gpu]` is a recognized effect, `cap-gpu` is a recognized capability, the verifier's Phase 3/4 checks fire. Negative tests for unverified `[Gpu]` | pingpong + sweep |
| **K8** | Math intrinsic lowering via the existing `codex.foreword.math` quire: trig, exp, log, pow lowered as PTX call-and-inline of pure-Codex CORDIC (no libdevice yet) | sweep |
| **K9** | Optional: libdevice path behind a build flag for users who want the perf. cuJitLink load via dlopen on host. Don't enter this CL until K0–K8 are stable | sweep |

A `Hopper+` block (TMA, cluster, wgmma, tcgen05) and an `Async` block
(`DeviceOperation`-equivalent with structured-concurrency integration)
sit beyond K9 and don't need to be designed yet. The 4060 Ti is Ada
Lovelace; many of those Hopper+ intrinsics literally cannot run on
the dev hardware.

Estimated effort across K0–K8: comparable to a new native ISA
backend. The x86-64 backend (per `THE-ASCENT.md`) was 21 commits and
20 bugs in one evening; PTX is simpler than x86-64 (typed virtual
registers, no encoding, no relocations) but the intrinsic surface is
larger. Expect somewhere in the same order of magnitude.

---

## 7. Dependencies and Assumptions

### Hard dependencies

- **Host-side CUDA driver**. We rely on the driver's PTX JIT
  (`cuModuleLoadData`). This is the same dependency GpuCompute.md
  already accepts — it's already loaded by the proxy.
- **NVIDIA hardware**. PTX is NVIDIA-only. AMD/Intel/Apple GPUs need
  their own backend (HIP/HSA, Level Zero, Metal) — a future
  conversation, parallel to this one.
- **Effect system**. `[Device]` and `[Gpu]` are new effects on the
  existing effect-row machinery — additive, no structural change. The
  TypeChecker subset-rule and post-pass are new but small. *No*
  dependency on the inline annotation syntax.
- **Phase 4/5 verifier (CLs 775–786)**. Effect-row and capability
  checks reuse existing machinery.

### Soft dependencies

- **libdevice.10.bc** for transcendentals via the libdevice path.
  *Optional.* Default is the pure-Codex CORDIC path. libdevice is
  shipped with the CUDA Toolkit; loaded via libNVVM dlopen at host
  runtime.
- **nvJitLink** (`libnvJitLink.so` / `nvJitLink.dll`) for LTOIR
  linking. *Optional and post-K9.*

### Assumptions

- The existing Codex IR is expressive enough to lower to PTX without
  a separate device IR. We bet yes — PTX maps cleanly to typed-IR
  three-address operations.
- Codex's bounded-integer subtypes (`Integer between L and H`) map to
  PTX register types (`.u8` / `.u16` / `.u32` / `.u64` / `.s8` / …).
  The bounds determine the storage; PTX accepts the matching operand
  widths.
- `kebab-case` Codex names sanitize to PTX-legal identifiers
  (`a-b` → `a_b`). This is a one-line transform; cuda-oxide uses the
  same approach for Rust generic mangling.
- Generic instantiation in Codex (closure capture + monomorphization)
  is at parity with what `[Device]`-effected definitions require. The
  CAMP-IIIC concurrency work already exercised closure capture across
  function boundaries on x86-64; the same machinery applies.
- The 5-phase verifier's effect machinery accepts a new `[Gpu]` row.
  This is an additive change.

## 8. Risks

### Strategic

- **cuda-oxide is alpha**. Their API will churn — they explicitly
  promise breakage. We lift *vocabulary* and *taxonomy*, not code or
  binary compatibility. Our intrinsic surface should track CUDA C++
  semantics, not cuda-oxide's Rust traits.
- **NVIDIA-only.** PTX commits us to NVIDIA hardware. The user has
  RTX 4060 Ti. AMD/Intel/Apple GPUs are out of scope here. An
  abstract `[Gpu]` effect leaves room for HIP/Metal backends later
  without breaking source compatibility.
- **The firmware constraint is unchanged.** This design does not
  enable bare-metal Codex.OS GPU dispatch. It opens a builder
  surface that runs through the host proxy. The illusion that
  Phase 4 is now reachable should be actively dispelled.

### Technical

- **Kernel divergence across CUDA versions**. PTX evolves (sm_75 →
  sm_120). The CDX manifest must record the target sm so the
  runtime can refuse stale modules. Pinning `sm_89` (Ada Lovelace,
  4060 Ti) for the dev box; lower-bounding at sm_75 (Turing) for
  general use.
- **Generic kernel name stability**. cuda-oxide uses
  `std::any::type_name::<T>()` hashed to a stable suffix. Codex
  needs an analogous stable scheme; today the type system has stable
  names but not necessarily stable hashes across compiler versions.
  This is a small new module (`PtxNameMangler.codex`), not a big
  problem, but it must be stable across pingpong runs.
- **Heap pressure during emit**. Holding the IR for all `[Device]`
  bodies plus their reachable closure of device functions could push
  the emit-deck high-water mark. CORDIC math intrinsics in pure
  Codex are pull-in by reachability and could blow heap.
  *Mitigation*: emit each kernel in isolation (per-kernel
  `emit-build` / `seal`); strike intermediate IR aggressively;
  measure pingpong heap HWM at K3 and K6.
- **`index_2d` soundness gap.** cuda-oxide flagged this as currently
  unsound. We can do better at K3 by encoding stride into the
  witness type (`ThreadIndex 'stride`) from day one. Adopt the fix
  cuda-oxide is planning, not the broken version.
- **PTX text size in CDX**. Each kernel adds ~few-KB to the CDX.
  Current seed is 1.99 MB; 50 kernels at 5 KB is 250 KB — meaningful
  but not catastrophic. Compress with the existing
  `codex.foreword.compress` quire (LZ77/Huffman) if it becomes a
  problem.
- **Async/structured-concurrency interaction**. CAMP-IIIC's
  fork/par-map/par-reduce are CPU-side. Adding GPU async (cuda-async
  in their world) must integrate with the same scheduler primitives,
  not parallel-evolve them. *Defer to post-K9*; design then.

### Security

- **GPU memory residue.** A kernel can leave data in GPU memory that
  the next process's kernel reads. The verifier should refuse to
  load a kernel that doesn't either zero its outputs or declare
  `[Gpu, Residue]`.
- **PTX is not executed by the verifier**. The 5-phase verifier
  inspects effects and capabilities at the Codex level. PTX itself
  is opaque assembly. We are trusting the codegen to be honest. This
  is the same trust we extend to the existing x86-64 backend, but
  it's worth naming.
- **JIT timing channels.** PTX JIT compilation time can vary with
  kernel size and complexity, exposing kernel structure to a
  co-tenant on the same GPU. Out of scope for this design but worth
  flagging.

### Process

- **Scope creep from cuda-oxide**. They have 17 crates, including
  `cuda-async` (composable streams), `cuda-bindings` (full driver
  API), `mathdx_ffi_test` (cuFFTDx/cuBLASDx). Resist the urge to
  chase parity. We want the minimal kernel programming surface plus
  a launch path. Everything else is K9+.

## 9. Open Questions for Damian

1. **Quire naming.** `codex.foreword.gpu/` (new top-level quire) vs.
   extending `codex.foreword.ai/` (where Tensor/NeuralNet/GGUF live)?
   GPU is a transport, AI is a domain — they're not the same axis. I
   lean toward a new `gpu` quire but you may have stronger naming
   intuitions.
2. ~~**Annotation vs effect.**~~ **Resolved 2026-05-08 (Damian)**:
   effects are always part of the contract; the ergonomic is in
   knowing and type-checking the behavior, not hiding it. There is
   no `@kernel` annotation. Kernel-ness is borne by the `[Device]`
   effect row. (See section 4.1 above.) A separate broader question
   remains for sidecar annotations as a kind — those would live
   outside the source, reference targets by section/function name,
   and never pepper the code itself. This design does not depend on
   sidecar annotations either way.
3. **HMM / unified memory.** cuda-oxide leans into HMM (Linux 6.1+
   only). Windows + WHPX gives us no HMM. Should we plan around
   explicit `device-buffer` copy semantics permanently, or keep a
   placeholder for unified memory if NVIDIA ever ships it on Windows?
4. **Backend co-location.** Does the PTX backend live in
   `codex/Emit/` next to x86-64 / ARM64 / EFI, or in a separate
   `codex.backend.gpu/` quire that's only loaded when GPU compilation
   is requested? Co-location is cheaper; quire-isolation is cleaner.
5. **Trust boundary on PTX.** Are PTX modules content-addressed and
   trust-graded the same way CDX modules are, or do they ride on the
   trust of the parent CDX? I lean on parent-trust (kernel
   inseparable from author) but this affects how a future module
   marketplace would work.
6. **K9 priority.** libdevice + LTOIR is the path to cuBLASDx /
   cuFFTDx interop and matters for serious GEMM perf. Is that ever
   on the table, or do we stay all-Codex-math indefinitely?
7. **Hardware test plan.** How do we run pingpong/sweep against
   `[Device]`-effected samples? `gpu-dispatch.exe` already exists
   for the fixed-op path; the K4 launch-ptx path is its successor.
   Sweep currently boots samples in QEMU/codex-vm — kernels need a
   real GPU on the host. Either skip samples that touch `[Gpu]` in
   default sweep and run them under a separate `sweep -Gpu` flag, or
   always route through gpu-dispatch.exe.

## 10. References

- **NVlabs/cuda-oxide** — `https://github.com/NVlabs/cuda-oxide`. Apache-2.0 + NVIDIA license on bindings. Alpha as of 2026-05-08. Book: `https://nvlabs.github.io/cuda-oxide/`.
- **GpuCompute.md** — Nib + Damian, 2026-05-05. The transport / firmware reality companion to this doc.
- **`codex.foreword.ai/GpuProxy.codex`**, **`codex.kernel/GpuBridge.codex`**, **`codex.build/gpu-dispatch.cu`** — the existing closed-enum proxy that this design extends.
- **`docs/Active/Compiler/PHASE-ARCHITECTURE.md`** — phase discipline + emit deck rules; the PTX backend lives inside the emitter wall.
- **`docs/Designs/Codex.OS/Verifier.md`** — 5-phase verifier; effect + capability checks for `[Gpu]` ride here.
- **`docs/Designs/Codex.OS/TrustAndRuntime.md`** — capability admission, trust lattice; `cap-gpu` joins the existing capability bits.
- **`docs/Designs/Language/CAPABILITY-REFINEMENT.md`** — direction + scope on capabilities; relevant when GPU access is delegated to a child process.
- **CLs 775–786** — verifier Phases 4 and 5; `[Gpu]` and `[Device]` extend this surface.

## Appendix A — cuda-oxide Vocabulary, Mapped

| cuda-oxide term | Codex equivalent | Notes |
|---|---|---|
| `#[kernel]` attribute | `[Device]` effect on the return-type row | the type signature *is* the marker; nothing inline. Compiler harvests kernels by walking effect rows |
| `#[device]` attribute (callable from device) | `[Device]` effect on a non-entry function | identical mechanism — the subset rule lets device-reachable helpers compose |
| `cuda_launch! { ... }` macro | `kernel-launch` builtin | with the same field surface (kernel, config, args); `[Gpu]` effect on caller |
| `LaunchConfig` | `LaunchConfig` record in `codex.foreword.gpu/LaunchConfig.codex` | identical shape |
| `ThreadIndex` newtype | `ThreadIndex` opaque record | constructible only by `index-1d` / `index-2d`; same safety story |
| `DisjointSlice<T>` | `DisjointSlice T` record | same `get-mut` API; `get-unchecked-mut` is unsafe-discharged via a capability |
| `DeviceAtomicU32`, `BlockAtomic*`, `SystemAtomic*` | three records in `Atomic.codex` | type carries scope; ordering is a `MemOrdering` sum-type argument |
| `AtomicOrdering::Relaxed/Acquire/...` | `MemOrdering = Relaxed \| Acquire \| Release \| AcqRel \| SeqCst` | direct PTX-qualifier mapping |
| `SharedArray<T, N>` / `DynamicSharedArray<T>` | `SharedArray T n` (dependent type) / `DynamicSharedArray T` | the dependent shape lets us bound-check at compile time when N is known |
| `Barrier` typestate (`Uninit` → `Ready` → `Invalidated`) | linear-typed state machine in `Barrier.codex` | linearity is already a Codex feature |
| `mbarrier_init/arrive/wait` | `mbarrier-init/arrive/wait` (kebab) | direct rename |
| `cuda-async::DeviceOperation` | TBD | post-K9; integrates with CAMP-IIIC structured concurrency |
| `LTOIR` build pipeline | optional K9 path | host runtime detail; not exposed to builders |
| `cargo oxide doctor` | `codex.works/GpuDoctor.codex` | a small chapter that probes the host proxy and reports CUDA version, libdevice presence, sm target |
| `cuda-bindings` (bindgen FFI) | `codex.kernel/CudaDriverBindings.codex` | thin Codex bindings to the CUDA Driver API; written by hand; not redistributed under NVIDIA license |

## Appendix B — What This Doc Does NOT Propose

- Does not propose a Codex MLIR / extensible IR layer.
- Does not propose adopting Pliron, LLVM, or any external Rust/C++ toolchain.
- Does not propose retargeting AMD or Intel GPUs.
- Does not propose breaking the existing GpuProxy fixed-op path.
- Does not propose unblocking Phase 4 (direct-init) — the firmware
  constraint stands.
- Does not propose committing to Hopper+ intrinsics (TMA, cluster,
  wgmma, tcgen05). Those are post-K9 if ever.
- Does not propose async GPU compute. CAMP-IIIC integration is post-K9.
- Does not propose changing the verifier's phase count or structure.
  We extend Phase 3 and Phase 4; we do not add Phase 6.
- **Does not propose any inline annotation** — no `@kernel`, no
  `@device`, no `@gpu`. Kernel-ness is borne by the type signature.
  Effects are the contract; the type system is the carrier; the
  compiler reads what it sees. A separate broader question about
  sidecar annotations (existing across-the-codebase, not specific to
  this design) is unresolved — but this design does not depend on
  any annotation form.
