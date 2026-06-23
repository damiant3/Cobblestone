# AssetForge: Local AI Model Runner and Asset Generation Pipeline

**Author:** Val + Damian
**Date:** 2026-06-21, updated 2026-06-22
**Status:** Active

## Problem

We depend on ComfyUI, Stable Diffusion WebUI (A1111/Forge), and
various other Python-based model runners for image generation and
3D reconstruction. These are external dependencies with their own
dependency trees (PyTorch, transformers, diffusers, etc.). AssetForge
replaces all of them with a single Codex-native model runner that
loads SafeTensors weights and runs inference through the GPU proxy.

Zero external dependencies. Zero Python. Zero subscriptions.
The proven pipeline is: text prompt -> 2D image -> 3D mesh, with each
step handled by a purpose-built model. Codex already has 70% of the
infrastructure (tensors, GGUF, diffusion scheduler, GPU proxy, image
codecs, 3D mesh types). This design fills the remaining gaps.

## Pipeline

```
Card concept (text)
    |
    v
[SafeTensors loader] -- weights from HuggingFace
    |
    v
[UNet + CLIP encoder] -- text-conditioned image diffusion
    |                     (FLUX.2 or Stable Diffusion architecture)
    v
2D image (PNG via Png.codex)
    |
    v
[Image-to-3D model] -- multi-view reconstruction
    |                   (TRELLIS-2 or Hunyuan3D architecture)
    v
3D mesh (GLB export via new GltfWriter.codex)
    |
    v
CodexMagic card renderer
```

## Architecture

### Layer 1: Data (foreword libraries)

New modules in `codex/foreword/ai/` and `codex/foreword/encode/`:

| Module | Purpose | Dependencies |
|--------|---------|-------------|
| `SafeTensors.codex` | Parse SafeTensors format (header + mmap-style weight access) | Tensor, Json |
| `Conv2d.codex` | 2D convolution, transpose conv, depthwise separable | Tensor, GpuProxy |
| `Normalization.codex` | GroupNorm, LayerNorm, RMSNorm for UNet/transformer blocks | Tensor |
| `UNet.codex` | UNet architecture: down/mid/up blocks with skip connections | Conv2d, Normalization, Attention, Activation |
| `ImageTensor.codex` | Image <-> tensor conversion (NCHW layout, normalize/denormalize) | Tensor, Png |
| `GltfWriter.codex` | glTF 2.0 binary (.glb) export for 3D meshes | Mesh, Json |

### Layer 2: Process (model runners)

New modules in `codex/foreword/ai/`:

| Module | Purpose | Dependencies |
|--------|---------|-------------|
| `TextEncoder.codex` | CLIP-style text encoder (tokenize + transformer) | Tokenizer, Transformer, Embedding |
| `DiffusionPipeline.codex` | Full text-to-image pipeline (encode -> denoise loop -> decode) | TextEncoder, UNet, DiffusionScheduler, ImageTensor |
| `ImageTo3d.codex` | Image-to-3D reconstruction (multi-view + mesh extraction) | DiffusionPipeline, Mesh |

### Layer 3: Application

App in `apps/assetforge/` or integrated into CodexMagic:

| Module | Purpose |
|--------|---------|
| `AssetForge.codex` | CLI/UI for card art generation workflow |
| `CardArtPipeline.codex` | CodexMagic-specific: prompt templates, style guide, batch generation |

## SafeTensors Format

SafeTensors is the standard weight format for diffusion models (FLUX,
SD, TRELLIS, Hunyuan3D). Simpler than GGUF -- designed for direct
memory mapping.

```
[8 bytes: header length N, little-endian u64]
[N bytes: JSON header with tensor metadata]
[remaining: raw tensor data, contiguous]
```

JSON header maps tensor names to `{dtype, shape, data_offsets: [start, end]}`.
Supported dtypes: F32, F16, BF16, I32, I8. Data offsets are relative to
the end of the header.

The loader parses the JSON header, then provides random access to
individual tensors by name. Dequantization from F16/BF16 to fixed-point
reuses the same half-float conversion pattern as Gguf.codex.

## Conv2d Design

2D convolution is the core operation for image-domain neural networks.
The UNet in diffusion models chains hundreds of conv layers.

```
conv2d : Tensor, Tensor, Integer, Integer -> Tensor
  input:  [batch, in_channels, height, width]   (flattened to 1D)
  kernel: [out_channels, in_channels, kH, kW]   (flattened to 1D)
  stride, padding
  output: [batch, out_channels, out_h, out_w]
```

CPU path: direct sliding-window convolution (no im2col to avoid
memory blowup on bare metal). GPU path: dispatch via GpuProxy op 12
(conv2d already in gpu-dispatch.cu).

Transpose convolution (for upsampling in UNet decoder):

```
conv2d-transpose : Tensor, Tensor, Integer, Integer -> Tensor
```

Implemented as insert-zeros + conv2d, or direct output-indexed loop.

## UNet Architecture

The UNet for diffusion models has three parts:

```
Input (noisy image + timestep embedding)
  |
  v
[DownBlock x N] -- conv + attention + downsample
  |  (skip connections saved)
  v
[MidBlock] -- conv + self-attention + conv
  |
  v
[UpBlock x N] -- concat skip + conv + attention + upsample
  |
  v
Output (predicted noise or velocity)
```

Each block contains:
- ResNet block: conv -> norm -> silu -> conv -> residual add
- Attention block: self-attention or cross-attention (text conditioning)
- Down/upsample: strided conv or transpose conv

Timestep conditioning: sinusoidal embedding -> MLP -> added to each
ResNet block.

Cross-attention for text conditioning: Q from image features, K/V from
text encoder output.

## GPU Dispatch Strategy

The GPU proxy already supports the critical operations:

| UNet Operation | GPU Proxy Op | Status |
|---------------|-------------|--------|
| matmul (attention) | OP_MATMUL (0) | Ready |
| relu/activation | OP_RELU (2), OP_GELU (10), OP_SILU (14) | Ready |
| softmax (attention) | OP_SOFTMAX (3) | Ready |
| conv2d | OP_CONV2D (12) | Ready |
| group norm | OP_GROUP_NORM (13) | Ready |
| upsample 2x | OP_UPSAMPLE2X (15) | Ready |
| layer norm | OP_LAYER_NORM (8) | Ready |
| element-wise add | OP_ELEMWISE_ADD (5) | Ready |
| scale | OP_SCALE (11) | Ready |
| clamp | OP_CLAMP (16) | Ready |

All 10 operations needed for UNet inference are already implemented
in gpu-dispatch.cu. The Codex side needs command construction helpers
for the new ops (currently only matmul/relu/softmax have helpers in
GpuProxy.codex).

## Model Formats and Sizes

| Model | Format | Size | VRAM | Use |
|-------|--------|------|------|-----|
| FLUX.2 schnell | SafeTensors | ~12 GB (F16) | 16 GB | Text-to-image |
| SD 1.5 | SafeTensors | ~2 GB (F16) | 4 GB | Text-to-image (lighter) |
| TRELLIS-2 | SafeTensors | ~4 GB | 16 GB | Image-to-3D |
| Hunyuan3D-2.1 | SafeTensors | ~3 GB | 6 GB | Image-to-3D |

For initial development: start with SD 1.5 (smallest, best documented
architecture, fits in 4 GB VRAM).

## Feature Scope (ComfyUI/Forge Replacement)

### Tier 1: Non-Negotiable

| Feature | Module | Status |
|---------|--------|--------|
| Text-to-image (txt2img) | DiffusionPipeline | Done (CL 5539) |
| Model loading (SafeTensors) | SafeTensors | Done (CL 5537) |
| Negative prompts + CFG | DiffusionPipeline | Done (CL 5539) |
| Prompt weighting `(text:1.5)` | PromptParser | Done (CL 5543) |
| Samplers: Euler, DPM++, DDIM | Sampler | Done (CL 5543) |
| SD 1.5 + SDXL architecture | UNet, UNetXL, TextEncoderXL | Done (CL 5555, 5556) |
| LoRA loading + stacking | LoraLoader | Done (CL 5543) |
| Seed reproducibility | DiffusionPipeline | Done (CL 5539) |
| Model registry (scan dir) | ModelRegistry | Done (CL 5543) |

### Tier 2: High-Impact

| Feature | Module | Status |
|---------|--------|--------|
| Image-to-image (img2img) | DiffusionPipeline | Done (CL 5555) |
| Inpainting | Inpainting | Done (CL 5556) |
| Hires fix / multi-pass upscale | HiresFix | Done (CL 5556) |
| Separate VAE swap | VaeDecoder | Done (CL 5555) |
| PNG metadata embedding | PngMetadata | Done (CL 5556) |

### Tier 3: Future

| Feature | Module | Status |
|---------|--------|--------|
| ControlNet | ControlNet | Done (CL 5561) |
| FLUX architecture | FluxPipeline | Done (CL 5561) |
| Outpainting | Inpainting (edge masks) | Supported |
| VAE tiling (large images) | VaeTiling | Done (CL 5562) |
| CLIP interrogator (reverse prompt) | ClipInterrogator | Done (CL 5562) |
| ESRGAN upscaling (2x/4x) | Upscaler | Done (CL 5562) |
| Face restoration | FaceRestore | Done (CL 5562) |

### Phase 5: SDXL + Quality Features (CL 5556)

19. `UNetXL.codex` -- SDXL UNet (640/1280/1280, micro-conditioning)
20. `TextEncoderXL.codex` -- dual CLIP encoder (768+1280 -> 2048)
21. `PngMetadata.codex` -- A1111-compatible generation params in PNG
22. `Inpainting.codex` -- naive inpainting (mask, blur, blend)
23. `HiresFix.codex` -- multi-pass upscale (latent nearest/bilinear)

### Phase 6: Tier 3 Features (CL 5561)

24. `ControlNet.codex` -- spatial conditioning (zero-conv, Canny preprocessor)
25. `FluxPipeline.codex` -- FLUX DiT architecture (rectified flow, RoPE, joint attention)

### Phase 7: Final 20% (CL 5562)

26. `VaeTiling.codex` -- tiled VAE decode (overlap blending, linear/cosine)
27. `ClipInterrogator.codex` -- ViT image encoder, cosine similarity search
28. `Upscaler.codex` -- ESRGAN RRDB, pixel shuffle, Lanczos, bilinear
29. `FaceRestore.codex` -- face detection, crop/restore/blend pipeline

## Phasing

### Phase 0: Data Layer (CL 5537)

1. `SafeTensors.codex` -- parse header, random-access tensor loading
2. `Conv2d.codex` -- conv2d, conv2d-transpose, depthwise conv
3. `Normalization.codex` -- GroupNorm, RMSNorm
4. `ImageTensor.codex` -- image/tensor conversion, NCHW layout
5. `Gltf.codex` -- glTF 2.0 binary export
6. Extend `GpuProxy.codex` -- command helpers for all 17 ops

### Phase 1: UNet + Text Encoder (CL 5539)

7. `UNet.codex` -- ResNet blocks, attention blocks, down/mid/up
8. `TextEncoder.codex` -- CLIP text encoder
9. `DiffusionPipeline.codex` -- end-to-end text-to-image

### Phase 2: Image-to-3D (CL 5540)

10. `ImageTo3d.codex` -- triplane features, marching cubes, GLB export

### Phase 3: Application (CL 5542)

11. `AssetForge.codex` -- batch generation, config presets
12. `CardArtPipeline.codex` -- CodexMagic card art prompt engineering

### Phase 4: General-Purpose Model Runner (CL 5543)

13. `Sampler.codex` -- Euler, Euler A, DPM++ 2M, DPM++ SDE, DDIM
14. `PromptParser.codex` -- weighted tokens, negative prompts, BREAK
15. `LoraLoader.codex` -- LoRA weight merging with alpha scaling
16. `ModelRegistry.codex` -- scan directories, identify model types
17. `VaeDecoder.codex` -- full VAE decoder (not simplified)
18. Extend `DiffusionPipeline.codex` -- img2img mode

## Memory and Time Assessment

**SafeTensors loader:** O(1) memory for header parse (JSON is small).
Tensor loading is O(n) in tensor element count -- each tensor loaded
individually, not the whole file at once. Fixed-point conversion
is per-element, no intermediate buffers.

**Conv2d CPU path:** O(out_c * in_c * kH * kW * out_h * out_w) time.
For a 3x3 conv on 64x64 with 512 channels: ~300M multiply-adds.
Slow on CPU, fine on GPU via proxy.

**Conv2d GPU path:** Single GpuProxy dispatch, host CUDA handles it.
Time dominated by data transfer (guest RAM -> shared memory -> GPU).

**UNet inference:** ~50 conv layers, ~10 attention layers for SD 1.5.
On GPU: ~2-5 seconds per denoising step, 20-50 steps total.
On CPU: minutes per step -- GPU proxy is mandatory for usability.

**glTF export:** O(vertices + indices) -- linear scan, no allocation
pressure. A 10K-vertex mesh produces ~200 KB GLB.

## Non-Goals

- Training (inference only -- load pre-trained weights)
- ONNX runtime (SafeTensors + native Codex inference)
- Node graph UI (ComfyUI's visual programming -- use code instead)
- Python interop (no PyTorch, no transformers library)

## Open Questions

1. **F16 vs fixed-point precision.** The GPU dispatcher works in F32.
   The Codex side uses fixed-point scale-1000. For diffusion models,
   the GPU does the heavy lifting (F32), and Codex orchestrates. The
   fixed-point path is for CPU fallback and small models only. Large
   models go through GpuProxy exclusively.

2. **Disk I/O for large models.** A 12 GB SafeTensors file cannot be
   loaded into guest RAM all at once. The loader must stream tensors
   individually via IDE disk or serial. Phase 0 targets small models
   (SD 1.5, ~2 GB) that fit in the 3 GB guest RAM.

3. **Multi-view consistency for image-to-3D.** TRELLIS-2 uses a
   multi-view diffusion approach. This requires generating multiple
   consistent views of the same object, which is a harder problem
   than single-image generation. Defer to Phase 2.
