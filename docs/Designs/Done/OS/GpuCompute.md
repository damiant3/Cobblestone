# GPU Compute Design

**Author**: Nib + Damian
**Date**: 2026-05-05
**Status**: Design

## Goal

Enable GPU-accelerated tensor operations (matmul, convolution, activation)
from Codex.OS bare-metal code. Primary target: NVIDIA RTX 4060 Ti (Ada
Lovelace, AD106). The AI shelf's Tensor/NeuralNet forewords provide the
CPU path; this design adds the GPU path.

## Hardware Context

- **Host**: Windows 11, RTX 4060 Ti (8GB VRAM), CUDA toolkit available
- **Guest**: Codex.OS bare-metal x86-64 in QEMU with WHPX acceleration
- **Connection**: QEMU chardev serial (COM1/COM2) for compiler I/O

## The Bare-Metal Problem

NVIDIA GPUs since Turing (2018) require **signed firmware blobs** (GSP-RM,
ACR, SEC2, PMU falcon images) to bring up the graphics/compute engine
(PGRAPH). Without these NVIDIA-signed images:

- PCIe enumeration works (vendor 0x10DE, find BARs)
- BAR0 MMIO control registers are accessible
- PMC reset can be deasserted
- BUT VRAM memory training requires VBIOS tables
- BUT PGRAPH (shader engine) is locked behind signed firmware
- No shader dispatch, no compute, no CUDA

**This is a hard block.** Nouveau on Linux works around it by loading
NVIDIA's proprietary GSP-RM blob. There is no known way to bring up
compute on Ada Lovelace without NVIDIA's firmware.

### What IS achievable bare-metal

- **UEFI GOP framebuffer**: GPU VBIOS includes a UEFI GOP driver. If
  booting via UEFI, call GOP for a linear framebuffer (address + stride +
  pixel format) before ExitBootServices. The framebuffer persists -- write
  pixels directly. No acceleration, but a working display. Replaces VGA
  text mode with high-res graphics.

- **PCIe device enumeration**: Standard PCI CAM/ECAM scan. Useful as an
  OS primitive even without GPU compute.

## The Practical Path: Virtio GPU Compute

Instead of bare-metal GPU access, route compute through a **custom QEMU
virtio device** that dispatches to host-side CUDA.

### Architecture

```
┌──────────────────────────────┐
│  Codex.OS (bare-metal guest) │
│                              │
│  Tensor foreword             │
│    │                         │
│  gpu-matmul builtin          │
│    │                         │
│  virtio-gpu-compute driver   │
│    │ (MMIO write to device)  │
└────┼─────────────────────────┘
     │ virtio descriptor
┌────┼─────────────────────────┐
│  QEMU host                   │
│    │                         │
│  virtio-gpu-compute backend  │
│    │                         │
│  cuBLAS / CUDA kernels       │
│    │                         │
│  RTX 4060 Ti                 │
└──────────────────────────────┘
```

### Guest Side

1. **virtio-gpu-compute driver** (new Codex kernel module):
   - Probe virtio-pci device at boot (vendor 0x1AF4, device TBD)
   - Set up virtqueues for command/response
   - Expose builtins: `gpu-matmul`, `gpu-conv2d`, `gpu-relu`

2. **Command format** (in shared memory):
   ```
   op      : u32   (0=matmul, 1=conv2d, 2=relu, 3=softmax)
   rows-a  : u32
   cols-a  : u32
   cols-b  : u32
   data-a  : offset into shared buffer
   data-b  : offset into shared buffer
   data-out: offset for result
   ```

3. **Data format**: Fixed-point integers (scale 1000), same as Tensor
   foreword. The host backend converts to float for CUDA, converts back.

### Host Side

1. **QEMU virtio backend** (~500-1000 lines C):
   - Register as a virtio-pci device
   - On descriptor arrival: read command, copy data from guest memory
   - Dispatch to cuBLAS: `cublasSgemm` for matmul
   - Write result back to guest memory, signal completion

2. **Alternative (simpler)**: Shared memory + host process:
   - QEMU `-object memory-backend-file,share=on`
   - Host-side process polls shared region for commands
   - Dispatches to CUDA, writes results
   - Guest polls for completion
   - No QEMU modification required

### Performance Budget

| Path | Latency | Throughput |
|------|---------|------------|
| CPU tensor (current) | ~0 overhead | Limited by integer multiply throughput |
| Virtio GPU (shared mem) | ~10-100us per dispatch | cuBLAS speeds on 4060 Ti |
| Virtio GPU (virtqueue) | ~5-50us per dispatch | Same, lower overhead |

For large matmuls (1024x1024+), GPU dispatch wins despite the round-trip.
For small operations (< 256 elements), CPU is faster.

## Implementation Phases

### Phase 0: PCIe Enumeration (Foreword) -- DONE
- `codex.kernel/Pci.codex`: CAM address encoding, bus scan, vendor/device/class/BAR reads
- Pure logic foreword -- useful regardless of GPU path

### Phase 1: Shared Memory GPU Proxy -- IN PROGRESS
- `codex.build/gpu-proxy.ps1`: host-side dispatch (CPU reference + CUDA auto-detect)
- `codex.build/gpu-dispatch.cu`: cuBLAS/CUDA backend (matmul, relu, softmax)
- `codex.build/build-gpu-dispatch.ps1`: nvcc build script
- `codex.build/test-gpu-proxy.ps1`: end-to-end tests (identity matmul, 3x3, relu, softmax)
- `codex.foreword.ai/GpuProxy.codex`: guest-side command protocol (serialize/deserialize)
- CPU reference passes all tests; CUDA backend awaiting toolkit install
- **Next**: build gpu-dispatch.exe, run GPU vs CPU comparison, guest-side shared-mem I/O

### Phase 2: Virtio GPU Compute Device
- Custom QEMU virtio-pci backend
- Proper virtqueue-based command submission
- Interrupt-driven completion (no polling)
- Guest-side virtio driver as kernel module

### Phase 3: UEFI GOP Framebuffer
- Boot Codex.OS via UEFI (currently uses multiboot/BIOS)
- Acquire GOP framebuffer before ExitBootServices
- High-res pixel framebuffer replaces VGA 80x25 text
- Independent of compute path -- display only

### Phase 4: Direct GPU Init (Long-Term)
- Requires either:
  - NVIDIA cooperation (signed firmware access)
  - Targeting older GPU (pre-Turing, no signed firmware)
  - Or: loading GSP-RM blob from disk (like nouveau does)
- Full PGRAPH init, pushbuffer command submission
- Native SASS shader dispatch
- **Blocked until firmware path is resolved**

## Open Questions

1. Should Phase 1 use a PowerShell wrapper around CUDA, or a native C++
   host process? PowerShell is consistent with the build system but adds
   latency.

2. For Phase 3, do we switch the whole boot path to UEFI, or maintain
   dual-boot (multiboot for QEMU, UEFI for real hardware)?

3. Should the Tensor foreword auto-detect GPU availability and dispatch
   transparently, or should the caller explicitly choose `tensor-matmul`
   (CPU) vs `gpu-matmul` (GPU)?

## References

- NVIDIA GSP-RM firmware: loaded by nouveau since kernel 6.x
- envytools register docs: envytools.readthedocs.io
- QEMU virtio spec: docs.oasis-open.org/virtio/virtio/v1.2
- Damian's GgufReader.cs: `D:\Projects\SpatialDb\SparseLattice\Gguf\`
- Codex AI shelf: `codex.foreword.ai/` (Tensor, NeuralNet, Gguf, etc.)
