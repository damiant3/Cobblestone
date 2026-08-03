# Dual-Target GPU Compilation -- PTX + SPIR-V via Plugs

**Author**: Blu + Damian
**Date**: 2026-06-15
**Status**: Design
**Companions**: [GpuCompute.md](../../OS/Active/GpuCompute.md) (transport/proxy),
[GpuKernels.md](../../OS/Active/GpuKernels.md) (Codex-native kernel language surface)
**Provoked by**: [299bytes.com/Helix](https://299bytes.com) dual-target x86-64 + PTX compilation

---

## Executive Summary

Pip's GpuKernels.md proposes an 8th emit mode (PTX) for GPU kernels
authored via `[Device]` / `[Gpu]` effects. That design is NVIDIA-only
and explicitly punts on AMD/Intel/Apple.

This design closes that gap by applying the same pattern the project
already uses for container formats: **the compiler emits IR; plugs
produce the final format.** A `ptx-plug` translates device IR to
NVIDIA PTX. A `spirv-plug` translates the same IR to Vulkan/OpenCL
SPIR-V. Both consume identical input. Adding a third target (e.g.
Metal IR for Apple, HIP for AMD ROCm) is another plug, not a compiler
change.

The insight: PTX and SPIR-V are both typed virtual ISAs with
unbounded virtual registers, structured control flow, and explicit
memory spaces. The same device IR serves both. The compiler doesn't
need to know which GPU vendor the binary targets -- that's the plug's
job.

---

## 1. What Already Exists

| Asset | Role |
|---|---|
| `docs/Designs/OS/Active/GpuKernels.md` | `[Device]` / `[Gpu]` effects, capability, type-checker post-pass, kernel detection, intrinsic shelf -- the full language surface |
| `docs/Designs/OS/Active/GpuCompute.md` | Transport: shared-memory proxy, virtio, UEFI GOP, firmware constraints |
| `codex/plugs/` (53 plugs) | Proven plug architecture: receive IR over TCP, parse S-expressions, emit target format |
| `codex/plugs/common/IRTextParser.codex` | S-expression IR parser shared by all plugs |
| `codex/plugs/common/PlugChain.codex` | Binary wire protocol for x86 codegen → container writers |
| `codex/compiler/Emit/IRTextEmitter.codex` | S-expression IR serializer |

**This design changes nothing in GpuKernels.md's language surface.**
Effects, capabilities, type-checker post-pass, kernel detection,
intrinsic shelf -- all unchanged. What changes is the backend: instead
of a monolithic PTX emitter in `codex/compiler/Emit/`, the compiler
emits a device IR that plugs consume.

---

## 2. Architecture

```
Source (.codex)
    → Frontend (lex/parse/desugar/scope/resolve/typecheck/lower)
    → Codex Typed IR
    → Type-checker post-pass: partition into host-reachable / device-reachable
    → Host IR  → existing CDX/TEXT/x86 emit path (unchanged)
    → Device IR → IRTextEmitter → TCP to plug
                                    ├─ ptx-plug   → PTX text   → proxy → NVIDIA GPU
                                    ├─ spirv-plug → SPIR-V binary → Vulkan/OpenCL runtime
                                    └─ (future)   → Metal IR, HIP, ...
```

### 2.1 The Device IR

The device IR is **not a new IR**. It is the existing Codex typed IR,
filtered to the device-reachable closure (exactly as GpuKernels.md
§4.2 specifies), serialized as S-expression text via IRTextEmitter,
and sent to the plug over TCP on the standard plug port.

The device IR carries:
- Every `[Device]`-effected definition and its transitive closure
- Type annotations on every node (the IR is fully typed)
- Effect rows (so the plug can validate the subset rule)
- Intrinsic references (`index-1d`, `shuffle-xor`, `mbarrier-init`, etc.)
  as named atoms -- the plug maps these to target instructions

What the device IR does **not** carry:
- Host-side code (filtered out by the post-pass)
- Target-specific instruction encodings (that's the plug's job)
- Register allocation decisions (both PTX and SPIR-V use virtual registers)
- Memory layout decisions beyond what the type system provides

### 2.2 PTX Plug

```
codex/plugs/ptx/
  PtxPlug.codex       -- entry: connect TCP, receive device IR, dispatch
  PtxEmitter.codex    -- IR walk → PTX text emission
  PtxIntrinsics.codex -- intrinsic name → PTX instruction sequence mapping
  PtxRegisters.codex  -- virtual register allocation (%rd0, %f0, etc.)
  PtxTypes.codex      -- Codex type → PTX type mapping (.u32, .f32, .pred, etc.)
  build.ps1
  run.ps1
```

The PTX plug:
1. Receives device IR as S-expression text over TCP
2. Parses it with the shared `IRTextParser`
3. Walks each `IRDef`, emitting PTX 7.x text
4. Maps intrinsic atoms to PTX instruction sequences:
   - `index-1d` → `mov.u32 %r0, %tid.x; mov.u32 %r1, %ctaid.x; mov.u32 %r2, %ntid.x; mad.lo.u32 %r3, %r1, %r2, %r0`
   - `shuffle-xor` → `shfl.sync.bfly.b32`
   - `barrier-sync` → `bar.sync 0`
   - `shared-load` → `ld.shared`
   - Atomics: scope/ordering from type → PTX qualifier (`.gpu` / `.cta` / `.sys` × `.relaxed` / `.acquire` / `.release`)
5. Emits one `.ptx` module per kernel entry point
6. Returns PTX text over TCP (or writes to file)

**Target SM**: `sm_89` (Ada Lovelace, RTX 4060 Ti) as default;
configurable via build flag. Lower bound `sm_75` (Turing) for
general use.

### 2.3 SPIR-V Plug

```
codex/plugs/spirv/
  SpirvPlug.codex       -- entry: connect TCP, receive device IR, dispatch
  SpirvEmitter.codex    -- IR walk → SPIR-V binary emission
  SpirvIntrinsics.codex -- intrinsic name → SPIR-V instruction mapping
  SpirvRegisters.codex  -- SSA ID allocation
  SpirvTypes.codex      -- Codex type → SPIR-V type mapping
  SpirvBinary.codex     -- SPIR-V binary format writer (magic, headers, sections)
  build.ps1
  run.ps1
```

The SPIR-V plug:
1. Same input: device IR as S-expression text over TCP
2. Parses with the shared `IRTextParser`
3. Walks each `IRDef`, emitting SPIR-V instructions in SSA form
4. Maps intrinsic atoms to SPIR-V built-in decorations and instructions:
   - `index-1d` → `OpLoad` on `GlobalInvocationId` built-in variable
   - `shuffle-xor` → `OpGroupNonUniformShuffleXor` (Vulkan subgroup ops)
   - `barrier-sync` → `OpControlBarrier`
   - `shared-load` → `OpLoad` with `Workgroup` storage class
   - Atomics: `OpAtomicLoad` / `OpAtomicStore` / `OpAtomicExchange` with
     scope (`Device` / `Workgroup`) and semantics (`Relaxed` / `Acquire` / `Release`)
5. Emits binary SPIR-V (not text) -- the format is a binary word stream:
   magic `0x07230203`, version, generator ID, bound, schema, then
   instruction words
6. Returns SPIR-V binary over TCP

**Target**: SPIR-V 1.5+ (Vulkan 1.2 baseline). Vulkan compute shaders
for GPU compute; OpenCL SPIR-V for compute-only runtimes.

### 2.4 Intrinsic Mapping Table

The GPU intrinsic shelf (`codex.foreword.gpu/`) defines operations in
terms of Codex semantics. Each plug maps these to its target:

| Codex Intrinsic | PTX | SPIR-V |
|---|---|---|
| `index-1d` | `%tid.x + %ctaid.x * %ntid.x` | `GlobalInvocationId.x` |
| `thread-idx-x` | `%tid.x` | `LocalInvocationId.x` |
| `block-idx-x` | `%ctaid.x` | `WorkgroupId.x` |
| `block-dim-x` | `%ntid.x` | `WorkgroupSize.x` (spec constant) |
| `sync-threads` | `bar.sync 0` | `OpControlBarrier Workgroup` |
| `shuffle-xor v mask` | `shfl.sync.bfly.b32 v, mask, 0x1f, 0xffffffff` | `OpGroupNonUniformShuffleXor Subgroup v mask` |
| `shared-array T n` | `.shared .align 8 .b8 smem[n*sizeof(T)]` | `OpVariable Workgroup ArrayType` |
| `atomic-add scope val` | `atom.{scope}.add.u32` | `OpAtomicIAdd {scope}` |
| `ballot mask` | `vote.sync.ballot.b32 p, mask` | `OpGroupNonUniformBallot Subgroup mask` |
| `lane-id` | `mov.u32 %r, %laneid` | `SubgroupLocalInvocationId` |

The intrinsic mapping is the **only** thing that differs between plugs.
IR parsing, type validation, control flow lowering, and register
allocation strategy are structurally identical (both targets use
virtual registers / SSA IDs with the driver/runtime doing final
allocation).

---

## 3. What the Compiler Changes

### 3.1 Device IR Emission (new, small)

The compiler already has IRTextEmitter for serializing IR to
S-expressions for the existing 53 plugs. For GPU, it:

1. Runs the type-checker post-pass (GpuKernels.md §4.2) to partition
   host/device
2. Serializes the device-reachable closure via IRTextEmitter
3. Sends to whichever plug(s) the build requests

This is a small addition to `CodexEmitter.codex` -- a new dispatch
case alongside the existing TEXT/CDX/ELF/EFI/IMG/DISK/MEASURE modes.

### 3.2 What Does NOT Change in the Compiler

- No PTX instruction encoding in the compiler
- No SPIR-V binary encoding in the compiler
- No GPU-specific register allocator in the compiler
- No target-specific intrinsic lowering in the compiler
- The `[Device]` / `[Gpu]` effect system (GpuKernels.md) is unchanged
- The type-checker post-pass (GpuKernels.md §4.2) is unchanged
- The intrinsic shelf (`codex.foreword.gpu/`) is unchanged

The compiler knows about GPU kernels at the type level. It does not
know about PTX or SPIR-V. That's the plug boundary.

---

## 4. Why Plugs, Not a Compiler Backend

GpuKernels.md §4.2 proposes three new files in `codex/compiler/Emit/`:
`PtxRegisterAllocator.codex`, `PtxOpEncoding.codex`,
`PtxIntrinsicLowering.codex`. That works for one target. For N
targets it means N × 3 files in the compiler, all touching the
emit phase, all affecting the fixed point.

The plug approach:
1. **The compiler's fixed point is unaffected.** GPU plugs are
   separate CDX binaries compiled against the plug seed. They do not
   participate in the self-compile gate.
2. **Adding a target is adding a plug.** Metal IR, HIP, Intel
   oneAPI -- each is a new directory under `codex/plugs/`, not a
   change to the compiler.
3. **The pattern is proven.** 53 plugs already work this way. The
   WASM plug, the ARM64 plug, the RISC-V plug, the ELF plug -- all
   receive IR and emit a target format.
4. **Independent development.** The PTX plug and SPIR-V plug can be
   built and tested independently, by different agents, without
   merge conflicts in the compiler.

### 4.1 Deviation from GpuKernels.md

This design **supersedes** GpuKernels.md §4.2 (the backend section
only). Specifically:

- `PtxRegisterAllocator.codex` moves to `codex/plugs/ptx/PtxRegisters.codex`
- `PtxOpEncoding.codex` moves to `codex/plugs/ptx/PtxEmitter.codex`
- `PtxIntrinsicLowering.codex` moves to `codex/plugs/ptx/PtxIntrinsics.codex`

Everything else in GpuKernels.md is unchanged: effects, capabilities,
type-checker post-pass, intrinsic shelf, trust integration, memory
model, proxy extension. The language surface is identical. Only the
backend location moves from compiler-internal to plug-external.

---

## 5. The Build Flow

### Single-target (NVIDIA only, simplest)

```powershell
build/compile.ps1 -Src app.codex -Out app.cdx -Gpu ptx -Log build.log
```

The compiler:
1. Compiles host code → `app.cdx`
2. Extracts device IR → sends to `ptx-plug` on TCP 9101
3. `ptx-plug` returns PTX text
4. Compiler embeds PTX as byte array in CDX kernel manifest

### Dual-target (NVIDIA + Vulkan)

```powershell
build/compile.ps1 -Src app.codex -Out app.cdx -Gpu ptx,spirv -Log build.log
```

The compiler:
1. Compiles host code → `app.cdx`
2. Extracts device IR → sends to `ptx-plug` on TCP 9101 AND
   `spirv-plug` on TCP 9102 (parallel)
3. Both plugs return their output
4. Compiler embeds both in CDX kernel manifest:
   ```
   kernel-manifest:
     vecadd:
       ptx:   <bytes>   sm_89
       spirv: <bytes>   vulkan-1.2
   ```

### Runtime target selection

The host CDX carries both PTX and SPIR-V for each kernel. At load
time, the runtime probes available GPU APIs:
1. CUDA driver available → use PTX (via `cuModuleLoadData`)
2. Vulkan compute available → use SPIR-V (via `vkCreateShaderModule`)
3. Neither → fall back to CPU path (existing fixed-op proxy or pure Codex)

This is the same adaptive pattern cloud runtimes use (CUDA on NVIDIA,
ROCm on AMD, OpenCL as fallback). The difference: both representations
ship in a single signed CDX, and the trust lattice covers both.

---

## 6. SPIR-V: Why It Matters for IoT

PTX is the right target for the dev box (RTX 4060 Ti) and NVIDIA
data center GPUs. SPIR-V is the right target for the IoT edge:

- **ARM Mali GPUs** (prevalent in IoT gateways, Raspberry Pi) support
  Vulkan compute via SPIR-V, not PTX
- **Qualcomm Adreno** (mobile/edge SoCs) -- Vulkan compute via SPIR-V
- **Intel integrated GPUs** (industrial PCs, edge servers) -- Vulkan
  compute or OpenCL SPIR-V via oneAPI/Level Zero
- **Imagination PowerVR** (automotive, embedded) -- Vulkan compute
- **Samsung Xclipse** (Exynos, RDNA2-based) -- Vulkan compute

For the European IoT deployment, many edge devices will have ARM Mali
or Qualcomm Adreno GPUs, not NVIDIA. SPIR-V reaches them. PTX does
not.

The dual-target approach means a single Codex source file produces
firmware that runs GPU compute on whatever hardware is available --
NVIDIA in the data center, ARM Mali on the gateway, CPU on the
sensor node. Same signed CDX, same trust chain, same effect-typed
safety guarantees.

---

## 7. Integration with GpuCompute.md Transport

The proxy extension from GpuKernels.md §4.3 (`gpu-op-launch-ptx`)
generalizes to:

```
gpu-op-launch-ptx   : Integer = 32   (existing proposal)
gpu-op-launch-spirv : Integer = 33   (new)
```

`gpu-dispatch.cu` (host-side) adds:
- PTX path: `cuModuleLoadData` → `cuLaunchKernel` (as GpuKernels.md proposes)
- SPIR-V path: `vkCreateShaderModule` → `vkCmdDispatch` (new)

The guest-side `GpuProxy.codex` sends whichever format the CDX
manifest contains for the target kernel + the detected host
capability. If both are available, prefer the native path (PTX on
NVIDIA, SPIR-V on Vulkan-only hardware).

---

## 8. Phasing

This design layers on top of GpuKernels.md's K0-K9 phasing. The
kernel CLs (K0-K2: effects, type-checker, IR partition) are
prerequisites. The plug work starts at K3:

| CL | GpuKernels.md | This design |
|---|---|---|
| K0-K2 | Effects, type-checker post-pass, IR partition | Unchanged -- these are compiler-side, not plug-side |
| **K3** | PTX emit (in compiler) | **Replaced**: PTX emit moves to `ptx-plug`. Device IR emission added to compiler. PTX plug built and tested against hand-written vecadd |
| K4 | Proxy extension (`gpu-op-launch-ptx`) | **Extended**: add `gpu-op-launch-spirv` alongside |
| K5 | End-to-end vecadd | **Extended**: vecadd runs on both PTX and SPIR-V paths |
| **K5.1** | (new) | SPIR-V plug: binary emitter, intrinsic mapping, vecadd through Vulkan compute |
| K6-K8 | Warp/shared/atomic intrinsics, capabilities, math | Unchanged in scope; intrinsic lowering lives in plugs not compiler |
| **K6.1** | (new) | SPIR-V intrinsic coverage: subgroup ops, shared memory, atomics |
| K9 | Optional libdevice | Unchanged -- PTX-only optimization |

### New plug CLs (interleaved with K-series)

| CL | What | Gates |
|---|---|---|
| **P1** | `ptx-plug` skeleton: PtxPlug.codex + PtxEmitter.codex, emits PTX for integer arith + simple memory | plug builds, PTX text validates against `ptxas -v` |
| **P2** | `spirv-plug` skeleton: SpirvPlug.codex + SpirvEmitter.codex + SpirvBinary.codex, emits SPIR-V for integer arith + simple memory | plug builds, SPIR-V validates against `spirv-val` |
| **P3** | PTX intrinsic coverage: thread/warp/block/shared/atomic mappings | vecadd + warp-reduce samples produce correct PTX |
| **P4** | SPIR-V intrinsic coverage: same surface as P3 | vecadd + warp-reduce samples produce correct SPIR-V |
| **P5** | Dual-target build integration: `-Gpu ptx,spirv` flag, parallel plug dispatch, CDX manifest with both representations | compile one source, get both outputs, proxy dispatches to correct one |

---

## 9. Trust and Verification

Both PTX and SPIR-V modules are signed with the same Ed25519
mechanism as CDX binaries (GpuKernels.md §4.4). The CDX kernel
manifest entry carries:

```
kernel-entry:
  name         : Text
  effect-row   : EffectRow
  capabilities : CapabilitySet
  targets      : List KernelTarget

kernel-target:
  format       : PtxTarget | SpirvTarget
  version      : Text          -- "sm_89" or "vulkan-1.2"
  bytes        : List Integer  -- the compiled module
  content-hash : Hash          -- SHA-256 of bytes
  signature    : Signature     -- Ed25519 over content-hash
```

The 5-phase verifier checks:
- Phase 3: `cap-gpu` declared for any `[Gpu]` usage
- Phase 4: effect rows match between manifest and call graph
- Phase 5: signatures valid on all kernel targets

A kernel target that fails signature verification is not loaded,
even if other targets for the same kernel are valid. The trust
chain is per-target, not per-kernel.

---

## 10. Future Targets

The plug architecture makes adding GPU targets mechanical:

| Target | Plug | Hardware | Priority |
|---|---|---|---|
| PTX | `ptx-plug` | NVIDIA (Turing through Blackwell) | **Now** -- dev box is RTX 4060 Ti |
| SPIR-V | `spirv-plug` | ARM Mali, Qualcomm Adreno, Intel, Samsung, Imagination | **Now** -- IoT edge GPUs |
| Metal IR | `metal-plug` | Apple M1/M2/M3/M4 | Later -- if Apple hardware enters scope |
| HIP | `hip-plug` | AMD RDNA/CDNA | Later -- if AMD hardware enters scope |
| DXIL | `dxil-plug` | Windows DirectX 12 compute | Later -- if Windows desktop GPU compute needed |

Each plug is ~500–1500 lines of Codex (comparable to existing
language plugs). The intrinsic mapping table (§2.4) is the
substantive work; the rest is IR parsing (shared) and format
encoding (mechanical).

---

## 11. Open Questions

1. **Plug port allocation.** Existing plugs use port 9100. GPU plugs
   need their own ports (9101 for PTX, 9102 for SPIR-V?) or a
   multiplexed protocol on 9100. The simpler path is dedicated ports.

2. **SPIR-V validation tooling.** `spirv-val` from the Vulkan SDK
   validates SPIR-V binaries. Do we vendor it, or treat it as an
   optional external check like `ptxas`? Recommendation: optional
   external check, same as ptxas. The plug emits correct SPIR-V by
   construction; the validator is a belt-and-suspenders.

3. **Vulkan compute runtime on the host.** The PTX path uses the
   existing `gpu-dispatch.cu` with CUDA driver API. The SPIR-V path
   needs a Vulkan compute dispatcher on the host. This is ~300–500
   lines of C (create instance, find compute queue, create pipeline,
   dispatch, read back). Should it be a separate executable
   (`gpu-dispatch-vk.c`) or integrated into `gpu-dispatch.cu`?
   Recommendation: separate executable -- keeps CUDA and Vulkan
   dependencies isolated.

4. **Fallback ordering.** When the CDX carries both PTX and SPIR-V,
   and the host has both CUDA and Vulkan: which wins? Recommendation:
   CUDA (lower dispatch latency on NVIDIA hardware), but configurable
   via a runtime preference.

---

## References

- [GpuKernels.md](../../OS/Active/GpuKernels.md) -- Codex-native kernel language surface (Pip, 2026-05-08)
- [GpuCompute.md](../../OS/Active/GpuCompute.md) -- transport/proxy/firmware (Nib + Damian, 2026-05-05)
- [299bytes.com/Helix](https://299bytes.com) -- dual-target x86-64 + PTX compilation, provoked this design
- SPIR-V spec: Khronos SPIR-V 1.6 (2022-02-18)
- Vulkan compute: Khronos Vulkan 1.3 §14 (Compute Shaders)
- PTX ISA: NVIDIA PTX ISA 8.5 (CUDA 12.6)
