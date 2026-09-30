# In-browser diffusion: SD1.5, then SDXL, on WebGPU

Pool work (Damian, 2026-09-29, CurrentPlan "The local image generator page"):
the image pipeline that `codex_image` runs natively (`Diffusion.md`), run in a
browser tab on WebGPU, SD1.5 first. The native route is NVIDIA only (PTX,
`sm_89`); this route is any GPU a browser exposes.

## The shape

The native pipeline keeps its split: Codex orchestrates, every layer is a GPU
kernel, the weights never enter the orchestrator's memory. In the browser the
three parts move as follows.

| part | native | browser |
|---|---|---|
| kernels | `codex/foreword/gpu/LayerKernels.codex`, `GemmKernels.codex`, through the PTX plug | the same chapters through the WGSL plug (`codex/plugs/wgsl`) |
| device bridge | `GpuBridge.codex`, codex-vm ops 40-48 over `nvcuda.dll` | the same `gpu-buf-*` / `gpu-launch` names as html-plug runtime primitives over WebGPU |
| weights | op 44 uploads a file range to a device buffer | the page slices a user-picked `.safetensors` (`File.slice`) straight into a `GPUBuffer` |
| orchestration | `apps/diffusion` chapters compiled to CDX | the same chapters compiled for the page |

## Measured

**The WGSL plug lowers none of the layer kernels** (2026-09-29,
`pwsh codex/plugs/wgsl/run.ps1 -Src codex/foreword/gpu/LayerKernels.codex -Out <file>`,
seed kernel): 33 refusals, 20 `float_storage_buffer_access` (the chapter's 12
`device-load-f64` and 8 `device-store-f64`; WGSL has no f64), 7 `list_literal`
and 6 `when_expression`. The plug's device operations are `device-load`,
`device-store` and the two f64 forms it refuses (`wgsl-is-device-load`,
`wgsl-is-device-store`); the chapter's 48 `device-load-f32`, 21
`device-store-f32` and 2 `device-store-f16` are not device operations to it
and fall through as ordinary calls.

**Nor the GEMM chapter** (same command over `GemmKernels.codex`): the same 7
`list_literal` and 6 `when_expression` refusals, at the same emitted lines
(`list_tail_loop`), therefore from a helper chapter both cite and not from a
kernel; and its 392 `wmma-*` occurrences are not refused but not recognised either.
WGSL has no matrix-core operation, so the browser's GEMM and conv2d are new
f32 kernels, not lowerings of these.

A WebGPU f32 GEMM (1024^3, 16 x 16 tiles) runs at 668 to 695 GFLOPS on the RTX
4060 Ti in headless Edge (`apps/landing/test-imagegen.mjs`, three runs,
2026-09-29); the native PTX WMMA route runs 45 TFLOPS f16 on the same card.

**f32 storage lowers** (2026-09-29): `device-load-f32` and `device-store-f32`
cross the `array<i32>` bindings by `bitcast`, graded on WebGPU by
`codex/plugs/wgsl/arms/f32-storage.mjs` over `F32StorageArm.codex` (4,096
inputs: a copy bit for bit, an axpy within one ulp of f32 arithmetic; a
conversion in place of the bitcast is caught), and six shipped gpushow kernels
re-emit byte-identical. `device-store-f16` and `device-load-f16` are refused by
name (`f16_storage_buffer_access`): an f16 element is half a 32-bit word, and a
store races the thread holding the other half. **f16 storage is read through
`device-load-f16-approx`** (packed pairs, element i the low half of word i / 2
when even), lowered to `unpack2x16float`, which is core WGSL: no `enable f16`
and no shader-f16 feature. `bk-linear-h` (w in f16) equals `bk-linear`'s output
bit for bit on weights exact in f16, and halves weight memory; it does not move
speed: 453 GFLOPS with f32 weights and 449 with f16 in the same run at 1024^3
(2026-09-29), so this GEMM is not bound by weight bandwidth, and the gap to the
probe and to the card is the tiling (one output per thread). f16 stores stay
refused; the browser keeps activations in f32.

**The layer kernels are in the PTX thread convention**: `kernel-silu` and its
siblings read `block-idx-x`, `block-dim-x` and `thread-idx-x` inside an `act`
block, while the WGSL plug takes a kernel as a function whose last parameter
is `gid`. The browser kernels are written in the gid convention, in
`codex/foreword/gpu/BrowserKernels.codex`, beside the PTX ones.

**The first browser kernel runs** (2026-09-29): `bk-silu`, f32 in and out, its
exponential through `real-exp2-approx`, which the WGSL plug now lowers to
`exp2`. `codex/plugs/wgsl/arms/browser-kernels.mjs` runs it on WebGPU: 2,000
inputs (multiples of 1/64 in [-16, 16)) within `gpu-layer-kernels`'
1e-6 (1 + |want|), the worst 9.9e-8; the 48 threads past `n` write nothing;
e in place of 2 as the base is caught on 1,903. `BrowserKernels` is in no
compiler closure: the self-compile unit does not contain it and stage 2 is
content-identical to the seed. Next: the rest of the `LayerKernels` set in
this form, then GEMM and conv2d. Since then `bk-gelu` (erf by A&S 7.1.26, worst 8.1e-8) and `bk-add` (exact) pass the same arms, and GroupNorm (`bk-row-mean`, `bk-row-rstd`, `bk-group-affine`) passes on 4 rows of 128 and 512 elements, and LayerNorm (the same statistics, `bk-layer-affine`) on 8 rows of 64, and row softmax (`bk-softmax`, out of place, one thread per element, worst 4.6e-8) on the same rows, and `bk-concat` and `bk-upsample` (nearest, x2) bit-exact. `bk-timestep` is graded against Forge's own fp32 `timestep_embedding` (Forge computes it in f32, which is the contract; root 2026-09-29), within 1e-6 (1 + |want|) plus 2^-21 |arg|: over t in {1, 50, 500, 999} at dim 64 the worst is 0.49 of that bound, and the gap to f64 is 1.6e-4 at t = 999, which is f32 arithmetic (the argument's ulp there is about 6e-5) and is Forge's gap too. `bk-linear` (y = x w^T + b) is the first tiled kernel, in `Real approximate`: 16 x 16 tiles of y per 256-thread workgroup through shared memory, exact over mm 37, nn 45, kk 70 (every edge a partial tile); 402 GFLOPS at 1024^3 on the reference card against the page probe's 668, reported by the arm and not graded. The WGSL plug lowers the PTX block model for it: `thread-idx-x`, `block-idx-x`, `block-dim-x`, `sync-threads`, `shared-base`, `shared-load/store-u32` and `-approx` onto one workgroup of 256 over `var<workgroup> array<i32, 4096>`; the PTX lowering of `shared-load/store-approx` is reek's. `bk-conv2d` is the implicit-GEMM conv2d on the same tiles (`kernel-conv2d-wmma`'s contract with a cout bias), exact at stride 1 and 2 with pad 1. 42 arms in all; with this stage 1's kernel set runs on WebGPU. The WGSL plug has no workgroup memory, barrier, local or workgroup index (measured 2026-09-29: none in `WgslEmitter.codex`), so the first form is one thread per row or group looping over it, as the native norms began, which needs no plug change; workgroup memory in the plug is the speed step after.

**A plug limit the kernels work around:** the WGSL plug lowers a store only as
a statement, so a store used as a value (sequencing two stores through an
`if`) is emitted as a call to an undefined `device_store_f32` and the module
fails. A kernel therefore makes one store. A buffer-reading helper shared by
two kernels (`bk-sum`) is emitted once per kernel as `<helper>__<kernel>`.

**The browser bridge runs** (stage 2, 2026-09-29): `apps/webapp/WebGpu.codex`
declares `GpuGrid`, `GpuParam`, `gpu-arg-buf` and `gpu-arg-u32` as
`GpuBridge.codex` does, and the HTML plug binds `gpu-buf-alloc`,
`gpu-buf-free` and `gpu-launch` with the native signatures, `gpu-launch` taking
the WGSL module text where the native one takes PTX. What WebGPU answers only
later takes a callback: `gpu-open-then` (the device), `gpu-buf-download-then`
(the words as JSON), `gpu-buf-upload-then` (the native upload's arguments, the
path being a name `pick-file-then` handed back; f32 to f32 and f16 to f32), and
`gpu-buf-write-words` writes from a list. A file chooser opens only on a user
gesture, so a page picks from a click handler. `gpu-launch` binds by position
through the WGSL plug's `// cx-kernel <entry> wg=<n> <param>=bK|uK ...` line,
refuses a block size other than the kernel's, and answers 0 once queued;
`gpu-error` reads the last error WebGPU reported. The runtime is emitted only
into a page that calls it. Graded by `codex/plugs/html/arms/gpu-bridge.mjs`
over the compiled page `GpuBridgeArm.codex`: 256 words round-trip exactly,
four ranges of a picked file upload, `bk_linear` through the bridge equals
x w^T + b exactly over f32 weights and over the same weights uploaded as f16,
and swapping two bindings in the `cx-kernel` line is caught (10 arms).

## Stages

| stage | capability | graded by |
|---|---|---|
| 1 | The WGSL plug lowers f32 and f16 storage access (`array<f32>`, `array<f16>` under `enable f16`) and the 13 helper refusals are gone; the kernel set has an f64-free form: every kernel `sd15` needs (GroupNorm, LayerNorm, SiLU, GELU, softmax attention, add, concat, upsample and timestep embedding from `LayerKernels`; GEMM and conv2d as new tiled f32 kernels, the page probe's 16 x 16 GEMM the starting point) lowers | each kernel against the f32 CPU reference and tolerance `codex/test/gpu-layer-kernels` already uses, run on WebGPU by a headless-Edge harness on the pattern of `apps/landing/test-imagegen.mjs`, with that test's sabotages |
| 2 | The browser bridge: `gpu-buf-alloc`, `-free`, `-upload`, `-download`, `gpu-launch` as html-plug primitives with the `GpuBridge` signatures, and the file-range upload from a picked file | a buffer round trip byte-exact; one GEMM launched through the bridge against the stage 1 result |
| 3 | The orchestration compiled for the page: `CheckpointLayout` binding a picked SD1.5 file's header, CLIP-L tokenizer and encoder, `UNet15`, the VAE decoder, one sampler | the native arms' oracles (`clip-sd15-prompt`, `sd15-unet-step`, `diffusion-vae-decode`) within the same tolerances |
| 4 | **The first SD1.5 image in a tab**: 512 x 512, the settings `Diffusion.md` measured natively | side by side with `codex_image`'s PNG for the same request and seed |
| 5 | SDXL: OpenCLIP-G, the SDXL UNet, `maxBufferSize` per tensor | as stage 4 at 1024 x 1024 |

## Open

- Stage 1 is plug work in `codex/plugs/wgsl` (granted to red for stage 1 by root, 2026-09-29; reek's lane otherwise) and
  kernel work in `codex/foreword/gpu`, which is foreword and therefore lands
  under the build token; the f64 forms exist for accuracy on the native route
  (`Diffusion.md`, "The layer kernels are correct, not yet fast"), so the
  browser forms are f32 kernels beside them, not edits to them.
- One f32 source for both routes (reek and red, 2026-09-29): BrowserKernels
  declare `Real approximate` (RwF32, which the IR carries), and the PTX plug
  lowers RwF32 to f32 while plain `Real` stays f64. Not a declaration alone:
  `Real approximate` does not unify with `Real` (CDX2001), and
  `device-load-f32`/`device-store-f32` are typed `Real` (`DeviceEffect.codex:25`),
  so the kernels need approximate load/store intrinsics in both plugs and
  approximate constants first; the plan is reek's row (main 31190).
- Where the orchestration runs (stage 3). The native SD1.5 path cannot be
  compiled for a page as it stands (surveyed 2026-09-29): the bridge rings x86
  ports (`GpuBridge.codex:77-84`); scratch lives at fixed guest addresses read
  by peek and poke (#BD700000: `CheckpointFile.codex:16`, `UNet.codex:19`,
  `Noise.codex:68`); weights upload by host path (`gpu-buf-upload`, and
  `ClipEncoder.codex:406` reads 77 token rows from the file per encode); the
  BPE vocabulary loads from a block device (`DiffusionDriver.codex:275`). And
  the model graph is not separable from its GPU ops: `UNet.codex`,
  `ClipEncoder.codex` and `VaeDecoder.codex` each hold their own op wrappers
  (`un-*`, `ce-*`, `vd-*`) over `gm-launch` with PTX kernel names and f64
  scalars. **Ruled (root, 2026-09-29): split the graph from the ops, one op
  set per route, the graph once.** The page's orchestration is Codex compiled
  by the html plug over `WebGpu`. Codex has no per-build choice between two
  chapters (a quire holds one `Chapter: X`, `QUIRES.md`), so the op set is a
  VALUE: a record of functions (`UNetOps`, then `ClipOps`, `VaeOps`, and the
  sampler's `lincomb3`) with the op wrappers' signatures, passed as the first
  argument of every graph function and called as `ops.conv u x ...`, the form
  `ZigBuiltinEmitter`'s `emit` field already compiles
  (`ZigEmitter.codex:1185`). The native record is built from today's
  wrappers, unchanged, so a native run calls the same code in the same order.
  Proof per slice: native outputs byte-identical before and after
  (`diffusion-first-image`, `sdxl-unet-step`, `sd15-euler`, the img2img parts).
  Split: `UNet`, `UNet15`, `ClipEncoder`, `VaeDecoder`, and Euler over
  `SamplerOps` (`lincomb`, `free`; `sample-euler-with`). The other samplers
  stay native until the page offers them. **A page can cite a graph only in a
  chapter with no native cite**: `GpuBridge` and `WebGpu` both define
  `GpuGrid` and `GpuParam`, so a page citing `Sampler` is CDX3001. The graphs
  in citable chapters: `SamplerGraph` (Euler), `UNetGraph` and `UNet15Graph`
  (`UNetOps a`, generic in the weight handle, `UNetFile` natively),
  `ClipGraph` (`ClipOps a`, the weights read through accessor ops) and
  `VaeGraph` (`VaeOps a b`, weights and activation). The browser records:
  Euler's is `BrowserSampler` (`browser-sampler-ops`, graded exact by
  `codex/plugs/html/arms/sampler.mjs`); UNet15's is `BrowserUNet`
  (`browser-unet-ops`, the `UNetOps BuWeights` value; its `attention` field,
  which only the SDXL graph calls, answers 0 until stage 5); CLIP's is
  `BrowserClip` (`browser-clip-ops`, the `ClipOps BcWeights` value, CLIP-L's
  HF layout; the native casts to f16 are f32 copies, and the embedding gather
  is a copy of each token's row at its byte offset).
  **One SD1.5 UNet step runs in a page** (2026-09-30, `unet15.mjs` over
  `UNet15Arm.codex`): the checkpoint's 686 UNet tensors uploaded as f32, then
  `unet15-forward-with` on `sd15-unet-step`'s inputs, graded as that test
  grades: all 26 blocks 64/64 within 1% of rms against Forge's own step, rms
  near; input block 1 at t = 900 (3/64, rms far) and with a zero context
  (34/64) miss as natively. 19.4 s from the click to every block read back,
  the upload included, at a 16 x 16 latent. **CLIP-L encodes in a page**
  (2026-09-30, `clip.mjs` over `ClipArm.codex`): its 196 tensors as f32, then
  `clip-encode-with` on the ids Forge chose for `clip-sd15-prompt`'s "a photo
  of a cat" and empty prompt (all weights 1, where Forge's emphasis is the
  identity): max 3.0e-5 from Forge's f32 cond over all rows (the f16 model's
  own bound is 0.0305); clip skip 2 without the final LayerNorm misses by 6.4.
  **The tokenizer runs in a page** (2026-09-30, `tokenizer.mjs` over
  `TokenizerArm.codex`): `ClipBpe` unchanged, over the html plug's byte heap
  (`alloc-bytes`, `peek`/`poke` byte and 32, `__memset`), loaded from
  vocab.json and merges.txt passed as page-data: Forge's ids for the cat and
  empty prompts, and the long prompt's 70-token first chunk as a prefix;
  merges cut after 1000 misses. **The first SD1.5 image in a tab** (stage 4,
  2026-09-30, `txt2img.mjs` over `Txt2ImgArm.codex`): `BrowserTxt2Img`'s
  `browser-txt2img` (one 77-token chunk tokenized as written, torch GPU noise
  from `NoiseGen`, Euler at the uniform schedule from `SamplerSchedule`,
  `guided-with` over `unet15-denoise-with` from `DenoiserGraph`, the VAE at
  1 / 0.18215) for "a photo of a cat", seed 0, 20 steps, CFG 7, 512 x 512:
  the noise is torch's within 1e-5 on all 16384 draws, and the image against
  `codex_image`'s PNG of the same request differs by a mean 0.52 of 255, max
  11, 99.998% of values within 8; 126 s from the click, the 4.1 GB f32 upload
  included. The html bridge pools freed buffers by size (a page queues a whole
  generation) and `gpu-buf-show-then` draws an image buffer into a canvas.
  **The page**: `BrowserImagePage.codex`, built by `node
  apps/diffusion/build-browser-page.mjs [--tokenizer <dir>] [--out <html>]`
  into one file carrying BrowserKernels' WGSL and the tokenizer's two files
  as page-data: pick an SD1.5 checkpoint (1022 tensors, 18 s), write a
  prompt, generate (2026-09-30, headless Edge on the reference card, idle:
  "a lighthouse on a cliff at sunset, oil painting", seed 7, drawn 94 s after
  the click; the tab is busy while the generation is queued).
  A page parses and weights a prompt as Forge does (`ClipPromptText`, split
  from `ClipPrompt`): `clip.mjs` chunks `clip-sd15-prompt`'s emphasis prompt
  into Forge's 77 ids and weights, and `bt-cond` applies `cp-emphasise` in the
  byte heap, within 1.8e-5 of Forge's cond. Open: a prompt over one chunk is
  refused, as the native SD1.5 route refuses it; a page has no source for
  vocab.json and merges.txt but page-data; Euler is the only sampler;
  `guided-with`'s native twin is
  `Txt2Img`'s `ti-den`/`ti-den3`, not yet moved onto it. A page reads a picked
  file's bytes through `file-read-then` and parses a checkpoint's header with
  `SafeTensors`' own `st-parse-file`: the real SDXL header's 2,516 tensors
  agree with JSON.parse (`codex/plugs/html/arms/cce.mjs`), and
  `CheckpointLayout` binds it in the page exactly as natively
  (`codex/plugs/html/arms/layout.mjs` against `diffusion-layout.expected`,
  SDXL and SD1.5 with their controls), and a chain of
  `gpu-buf-upload-then` puts a bound part on the device in its own dtypes: SD1.5's
  VAE, 248 tensors and 167,307,726 bytes, reads back as the file's bytes
  (`codex/plugs/html/arms/upload.mjs`). `BrowserVae` holds the VAE's browser
  operations and `browser-vae-ops`, the `VaeOps BvWeights BvAct` value: conv,
  GroupNorm with SiLU, add, upsample and attention (`vae-ops.mjs`, against f64).
  **The VAE decodes in a page** (2026-09-30, `vae-decode.mjs` over
  `VaeDecodeArm.codex`): dreamshaperXL's 140 decoder tensors uploaded as f32
  from the picked file, then `vae-decode-with` over `browser-vae-ops` on
  `diffusion-vae-decode`'s latent, by that test's pixel rule against Forge's
  own f32 decode: max 1, all 196,608 channel values within 1 (native: max 2);
  the latent with channels 0 and 1 exchanged misses as natively (mean 60,150
  of 1000ths); two decodes 5.9 s from the click. The browser arms run by `node` (`node
  codex/plugs/html/arms/<arm>.mjs`), not through `bvt.ps1`. Every `BrowserKernels` kernel reads f32. Every kernel the SD1.5 path launches now has a browser form in
  `BrowserKernels` (72 arms): `lincomb3` there needs an output apart from its
  inputs, because WebGPU refuses one buffer bound twice as writable storage.
