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

**How the plug lowers stores:** a store is a statement: an `act` statement, a
guarded one (`if c then <store> else 0`, lowered to `if (c) { store; }`), or
the body of a helper that returns 0, which is how `bk-linear-store16` makes 16.
A helper that reads a buffer is emitted once per kernel and bound to the first buffer a call passes it, so one helper called with two buffers reads the first for both, with no diagnostic: `bk-attn-q-at` and `bk-attn-k-at` exist for that. A buffer-reading helper shared by two kernels (`bk-sum`) is emitted once per
kernel as `<helper>__<kernel>`.

Open, unowned: `wgsl-ty-text` maps a helper's Boolean parameter to `i32`,
but a direct conditional use emits that integer as the condition of `select`.
WebGPU refuses the shader. MusicGen's normalization helper uses an Integer
flag compared with 1 to avoid the gap. Evidence (2026-09-30):
red `build-output/musicgen-stage3/lm-norm.wgsl`, `ml_stat_sum`, and the refused
`medium-norm` run; the explicit comparison in `lm-norm2.wgsl` runs successfully.

**`bk-linear` and `bk-conv2d` are register-tiled**: a 64 x 64 tile of y per
256-thread workgroup, 4 x 4 outputs a thread, so a caller launches
ceil (M / 64) x ceil (N / 64) workgroups (`bu-tiles`); `bk-linear-h` is the
same loop over packed f16 weights. `bk-linear` runs 2574 to 2800 GFLOPS at
2048^3 against 536 to 584 for the 16 x 16 form, the two alternated in one page
(2026-09-30, reference card). Each output accumulates over k in the same
order as before, so the page's image is unchanged (`txt2img.mjs`, mean 0.516
of 255 from `codex_image`'s render either way).

**The page keeps weights of rank 2 and up as packed f16** (`bt-half` in
`CheckpointLayout`: every one except CLIP's embedding tables), as the native
UNet does, read by `bk-linear-h` and `bk-conv2d-h`: SD1.5 takes 2.14 GB on the
device where f32 took 4.13 (2026-09-30). An f32 checkpoint's weights are
converted in the upload, rounding to nearest even (`gpu-bridge.mjs` grades
every f16 value and every midpoint). On an f16 checkpoint the image is
unchanged (`txt2img.mjs`, mean 0.516 of 255), 67.9 s from the click.

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
| 5 | Graded: SDXL dual CLIP, whole-prompt conditioning, UNet blocks, VAE and the first Euler/Karras image in a page | 1024 x 1024 native-render comparison under the same mean-RGB gate as stage 4; details below |

## SDXL text encoders

`BrowserClipLoad` binds both original SDXL text-encoder parts from a picked
checkpoint. It normalizes OpenCLIP-G names, splits combined Q/K/V weights and
biases into their file ranges, and uploads embedding tables as f32 and dense
matrices as packed f16. `BrowserClipKernels` transposes the packed projection
by writing one complete f16 pair per thread. `BrowserClip` supplies that
projection to the shared `ClipGraph`; L uses its penultimate state without
final normalization, while G also runs its last block and projects the
normalized EOS state. G's post-EOS padding remains the graph's id-0 rule.

After `gpu-open-then` succeeds, call
`browser-clip-load fileHandle transposeWgsl spec part ready refuse` with
`bc-xl-l-spec` or `bc-xl-g-spec` and the corresponding `PartBinding` from
`layout-bind header sdxl-layout`. Success returns `BrowserClipLoaded`, whose
`bcl-weights` works with
`clip-encode-with (browser-clip-ops baseWgsl) loaded.bcl-weights ids`, where
`ids` is one 77-ID sequence including BOS, EOS and EOS padding. The shared
graph replaces G's post-EOS padding with zero. Check `co-status == 0` before
using the returned buffers.
`fileHandle` is the opaque `name` returned by `pick-file-then`; outputs are
`co-cond` and, for G, `co-pooled`. The caller frees those handles and calls `browser-clip-close` once
for the loaded weights. The loader refuses incomplete parts and unsupported
dtypes and releases acquired handles on an upload/allocation refusal.

Build `codex/plugs/html/arms/ClipXlArm.codex` with the bundle/HTML commands and
`apps/diffusion/BrowserClipKernels.codex` with the WGSL plug. Run
`node codex/plugs/html/arms/clip-xl.mjs <arm.html> <BrowserKernels.wgsl>
<BrowserClipKernels.wgsl>`. It uses the original
`dreamshaperXL_lightningDPMSDE.safetensors` and existing `clip-sdxl-ref` cat/hero
fixtures. The checkpoint is under `build-output/diffusion-models/`; tokenizer
files are `vocab.json` and `merges.txt` under
`D:/AI/DiffusionForge/webui/backend/huggingface/stabilityai/stable-diffusion-xl-base-1.0/tokenizer`.
Reference regeneration uses `D:/AI/DiffusionForge/system/python/python.exe` with
`build/clip-encoder-oracle.py <output-directory>`; its header names the original
checkpoint location under the Forge installation and model configuration;
the browser harness uses the copy under this repository's build-output.
The browser tokenizes the actual prompts; expected tensors remain
outside the page. Every hidden-state and pooled-output element is checked
against `clip-sdxl-encode`'s unchanged Forge f16-deviation bounds, separately
over all rows and non-BOS rows. Wrong L activation, G activation and G padding
must fail the non-BOS bound with valid finite outputs. A packed transpose
pattern must be bit-exact with overdispatch leaving its sentinel tail intact.
The report records checkpoint, reference and shader hashes.

The final prompt-tokenizing run passes: maximum L non-BOS error 1.95504e-5,
G error 8.96454e-5 and pooled error 7.74861e-6 (2026-10-01). All 18 existing
SD1.5 CLIP arms also pass. The complete SDXL image grade is below.

Weight storage is proportional to the two encoders; projection transpose adds
one temporary packed matrix. Existing graph activations are freed after each
encode. The grade checks no live GPU handles after closing weights; pooled
storage and downloaded diagnostic arrays remain retained. Metadata expansion
and name/handle accumulation copy growing lists and can cost quadratic work
in tensor count; the inventory bounds that count. Projection transpose costs
linear work in matrix elements. Malformed-header and
allocation/device-loss failure paths and peak-memory limits remain ungraded.

`BrowserClipPrompt` conditions a whole SDXL prompt with the existing
`ClipPromptText` parser and emphasis arithmetic. After loading both encoders,
call `browser-clip-condition-then baseWgsl lWeights gWeights bpe prompt negative
ready refuse`. Success receives `ClipCond`: `cn-chunks`, `cn-cond-l`,
`cn-cond-g`, `cn-pooled`, and `cn-status` zero. Each chunk's L/G hidden states
are weighted separately; pooled G comes from the first chunk before emphasis.
An empty prompt with `negative=True` returns one zeroed chunk and zero pooled
output. `browser-clip-crossattn baseWgsl cond` returns each row's 768 L values
followed by 1280 G values, or zero on allocation/launch refusal. The caller
frees that handle and calls `browser-clip-cond-free cond`; encoder weights
remain caller-owned and must stay loaded until `ready` or `refuse` fires.
Conditioning refusal releases its acquired aggregate and
temporary GPU handles.

Build `ClipXlPromptArm.codex` through the same HTML commands, then run
`node codex/plugs/html/arms/clip-xl-prompt.mjs <arm.html> <BrowserKernels.wgsl>
<BrowserClipKernels.wgsl>` with the same checkpoint/tokenizer prerequisites.
Fixtures are `codex/test/apps/clip-sdxl-prompt-ref/{negative,emphasis,long}.ref`,
regenerated by Forge's Python with `build/clip-prompt-oracle.py <directory>`.
These preserve the native `clip-sdxl-prompt` bounds and exercise nested/weighted
emphasis and three chunks spanning comma backtracking and BREAK. The page
receives prompts and tokenizer bytes, not expected IDs, weights or outputs.

The grade passes (2026-10-01): every chunk ID and f32 weight bit matches;
every L/G hidden-state and pooled value meets its native bound. Maximum L
non-BOS error is 2.43187e-5, G non-BOS error 2.77520e-4, pooled error 1.23978e-5.
Cross-attention copies every actual L/G value exactly, including BOS rows,
and also meets the reference non-BOS bound. Empty-negative tensors and their
concatenation are all zero. Unweighted emphasis must exceed the reference
bound; disabled comma backtracking must produce valid different chunks.
All successful output and model handles are released.

Encoder work is linear in chunk count at the fixed 77-token shape. A numeric
scratch buffer is allocated once per conditioning job and reused across
readbacks; it remains in the Codex heap afterward. Parsing/readback lists and
aggregate tensors grow with chunk count, and GPU storage remains pooled.
Long-run heap bounds and allocation/device-loss cleanup remain ungraded.

## SDXL UNet block grade

`BrowserUNet` implements the shared graph's SDXL `attention` operation with
64-wide heads: packed-weight Q/K/V projection, f32 scores scaled by 1/8,
row softmax, value aggregation and output projection. The native graph remains
the owner of residual blocks, transformer depths, label/time embeddings and
skip connections. The operation releases its temporary Q/K/V, score and value
handles after use.

Build `codex/plugs/html/arms/UNetXlArm.codex` through the HTML commands, then
run `node codex/plugs/html/arms/unet-xl.mjs <arm.html> <BrowserKernels.wgsl>`.
The original checkpoint is
`build-output/diffusion-models/dreamshaperXL_lightningDPMSDE.safetensors`.
Reference data is `apps/diffusion/UNetReference.codex`, regenerated by Forge's
Python with `build/sdxl-unet-oracle.py <checkpoint> <output.codex>`. The page
receives the native test's formula-generated latent, label and context at
timestep 500, never expected block values. The diagnostic validates the
reference's canonical block order and sampling constants, all uploads, actual
returned block count, finite outputs and recorded GPU errors.

The 16 x 16 latent step passes all 20 blocks (2026-10-01): all 64 sampled values
per block are within 1% of reference RMS, and whole-tensor mean square is
within 2% of reference mean square. This is sampled-value plus aggregate-energy
parity, not elementwise parity. The wrong timestep at input 1 gives 2/64;
zero context at input 4 gives 50/64 even though its mean square remains near.
Both controls return valid finite outputs. The successful arm releases all
model, input, block and control handles. Rebuilding on depot seed D2C01E16
produces byte-identical HTML and BrowserKernels WGSL to the graded artifacts.
All 30 existing SD1.5 UNet arms also pass on that seed.

The existing `node codex/plugs/html/arms/vae-decode.mjs` already uses
dreamshaperXL's decoder and the native `diffusion-vae-decode` fixture. Its five
arms pass on D2C01E16: all 196608 output bytes are within 1 of Forge, reported
mean difference 8/1000; the channel-swap control fails. No VAE implementation change
is needed for this checkpoint. This grades a 256 x 256 decode, not the final
1024 x 1024 image or its memory use.

The direct attention path materializes `heads * query_rows * key_rows` scores;
self-attention work and storage are quadratic in spatial positions. The block
diagnostic retains all block outputs and their serialized readbacks. Freed GPU
storage remains pooled. This small-latent grade does not establish full-image
memory use, throughput, or failure-path cleanup.

## SDXL image composition

`BrowserSdxlLoad` opens an original SDXL checkpoint through
`browser-sdxl-open file size baseWgsl clipWgsl ready refuse`. Set `file` to the
picked-file callback's opaque `name` and `size` to its `size`; the callback's
`file` field is only the display filename. The two shader texts are BrowserKernels
and BrowserClipKernels. It validates the SDXL layout, loads both text encoders,
the UNet and decoder weights, and returns `BrowserSdxlModel`. Acquired weight
handles are released on load refusal.

`browser-sdxl-euler-then model bpe prompt negative settings progress ready
refuse` runs explicit Karras scheduling with Euler. `BrowserSdxlSettings`
contains `bs-width`, `bs-height`, `bs-steps`, `bs-seed` and `bs-cfg`. Dimensions
are 64-1344 in multiples of 32, with at most 1048576 pixels. Axis bounds are
checked before multiplying. Dimensions outside that alignment need the
graph's unsupported browser crop operation. Steps are 1-50 and CFG is finite
in 0-30. The tokenizer must be valid. Progress reports encoding, sampling
and decoding; a negative
return cancels at a progress boundary. Only one generation may use a model
at a time. `browser-sdxl-close` refuses while busy and is idempotent after
completion. Keep the model loaded until the success or refusal callback.

`browser-sdxl-sample-then model bpe prompt negative settings sampler progress
ready refuse` selects the sampler without changing `BrowserSdxlSettings`.
`browser-sdxl-euler-then` remains the sampler-0 compatibility entry. All routes
use explicit Karras scheduling; these are not the native Automatic schedule
defaults. DPM++ 3M SDE requests one extra sigma and drops the penultimate one,
as the native route does.

| ID | Sampler | Noise source |
|---|---|---|
| 0 | Euler | None after initial latent |
| 1 | DPM++ SDE | Brownian tree, midpoint and endpoint queries |
| 2 | DPM++ 2M | None after initial latent |
| 3 | DPM++ 2M SDE | Brownian tree, endpoint queries |
| 4 | DPM++ 2M SDE Heun | Brownian tree, endpoint queries |
| 5 | DPM++ 2S a | Seeded torch-compatible draws following the initial latent |
| 6 | DPM++ 3M SDE | Brownian tree, endpoint queries |

Unsupported sampler IDs refuse before changing the model's busy state.
Stochastic routes report a `noise` preparation phase. A fresh worker runs
the native noise arithmetic compiled to Wasm and returns a bank of raw f32
words. Preparation reports `(0, 1)` every 50 ms and `(1, 1)` when ready;
a negative progress result cancels preparation and terminates the worker.
Sampling callbacks still count
completed sampling steps, not denoiser evaluations. DPM++ SDE (1) and 2S ancestral (5)
perform an additional midpoint denoiser call on nonterminal steps.
Completion, refusal and cancellation release the sampler's GPU bank, draw
buffer and history buffers. The worker copies the bank out, resets its
Wasm heap and terminates before the callback. No Brownian tree is allocated
in the page byte heap. Physical process-memory reclamation is controlled by
the browser and is not measured by the ownership checks.

Success transfers a GPU handle containing 3 x height x width f32 pixels to
the caller, which may use `gpu-buf-show-then` and must free that handle. The
model remains loaded for reuse. Conditioning supplies separate positive and
negative contexts, their own token lengths, and pooled-plus-size/crop ADM
vectors. Sampling drains the device between steps. VAE input is divided by
0.13025 before decoding. GPU completion succeeds with an empty string;
nonempty completion text or a recorded GPU error causes refusal.

The browser VAE adapter now consumes the first activation passed to `add`,
matching the native adapter's ownership contract. Its separate result buffer
previously left residual convolution outputs live. The attention wrapper no
longer frees that consumed projection a second time, and failed activation
construction/transposition releases the allocated buffer. The seven VAE-op
arms, including ownership and refusal controls, and five existing decode arms
pass; pixel bounds remain unchanged. Native VAE code is unchanged.

Prepare the native witness with
`node apps/diffusion/build-sdxl-reference.mjs <new-output-directory> [width height sampler-id]`
(the VM sidecar requires an output path without whitespace). It specializes
`build/sdxl-browser-reference.codex` to `reference.codex` in that directory,
compiles it using the depot seed, mints the tokenizer
disk and runs native `Txt2Img` with explicit model/output roots. Require the
successful render text and newly produced PNG. Its evidence file records
checkpoint, compiler, source, CDX, tokenizer-disk/component and image hashes;
checkpoint, compiler and source hashes must remain unchanged across the run.
Keep `reference-evidence.json` beside `reference.cdx` and `reference.codex`,
which the image grader resolves relative to the evidence file.
This uses the native renderer directly, without a Cobblestone MCP server.

These commands use the checkpoint at
`build-output/diffusion-models/dreamshaperXL_lightningDPMSDE.safetensors` and
the tokenizer under
`D:/AI/DiffusionForge/webui/backend/huggingface/stabilityai/stable-diffusion-xl-base-1.0/tokenizer`.
The browser grader requires WebGPU-capable Edge at
`C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe` and uses
`nvidia-smi` for the recorded GPU samples.

From the repository root, create a scratch directory and build the arm and
both shader modules serially. Substitute that directory for `<scratch>`:

```powershell
pwsh codex/plugs/html/build.ps1
pwsh codex/plugs/wgsl/build.ps1
pwsh build/bundle-app.ps1 -Src codex/plugs/html/arms/SdxlArm.codex -Out <scratch>/arm.codex
pwsh codex/plugs/html/run.ps1 -Src <scratch>/arm.codex -Out <scratch>/arm.html -Compiler seed/Codex.cdx
pwsh codex/plugs/wgsl/run.ps1 -Src codex/foreword/gpu/BrowserKernels.codex -Out <scratch>/BrowserKernels.wgsl
pwsh codex/plugs/wgsl/run.ps1 -Src apps/diffusion/BrowserClipKernels.codex -Out <scratch>/BrowserClipKernels.wgsl
node apps/diffusion/build-noise-wasm.mjs <scratch>/noise.wasm
```

The plug builds create their required `build-output/*-plug.cdx` artifacts.
The noise builder requires `wat2wasm` on PATH and builds the Wasm plug from
the depot seed. Both page builders embed its bytes under `sdxl-noise`.
The grader defaults to `noise.wasm` beside `arm.html`. Then run
`node codex/plugs/html/arms/sdxl-image.mjs <arm.html> <BrowserKernels.wgsl>
<BrowserClipKernels.wgsl> <native.png> <browser.png> <reference-evidence.json>
[width height sampler-id noise.wasm]`.
Both commands default to 1024 x 1024 and also accept 1344 x 768 or
768 x 1344. Sampler defaults to 0; IDs 1-6 select the DPM++ routes above.
Pass the same dimensions and sampler to both commands. Euler writes
`euler-reference.png`; DPM++ writes `dpm-reference.png`. The generated source hash and PNG
request metadata bind the requested shape; the grade checks raw word
transport, finite f32 output, canvas dimensions and every RGB channel.
The default request is dreamshaperXL's storage prompt, negative prompt and seed
7201 from the native first-image fixture, with Euler, explicit Karras, six
steps, CFG 2 and 1024 x 1024 by default, or the selected preset dimensions.
It intentionally differs from that fixture's
DPM++ SDE sampler. The native witness and browser request match each other.

The image gate is the existing `txt2img.mjs` contract: mean absolute RGB-byte
difference below 4 of 255. The earlier SD1.5 result of 0.52 is a measurement,
not that gate's threshold. The grader verifies request metadata and native
input/artifact hashes, all output floats are finite, the image is non-flat,
six sampling steps arrive in order, busy/close guards work, and no active
handles remain after cleanup. Expected pixels enter the page only after
generation. Maximum pixel difference and the fraction within eight are
reported, not additional acceptance bounds. The full-image grade passes
(2026-10-01): mean difference 0.754852 of 255, maximum 169, and 99.52% of RGB
channels within eight. The maximum records localized differences despite the
passing mean. All 3145728 f32 output values
are finite, all six steps are observed in order, and no active handles remain.

The native and browser routes share graphs and supporting algorithms, so this
is backend/render parity rather than an independent Forge algorithm oracle.
The additional Euler/3M comparison below uses original Forge components.
Native reference rendering takes about 14.1 seconds in the recorded run,
excluding compile/hash preparation. The browser takes 55.7 seconds from click
through load/generation/readback on the RTX 4060 Ti 16 GB. Device-wide GPU
memory sampled every two seconds reaches 15590 MiB; retained buffer sizes
total 16373573432 bytes after handle release. Neither is an exact peak-VRAM
measurement. This full-resolution case uses most of the reference card's
memory. Pooling retains freed GPU storage, and the
grader retains readback arrays and canvases. Long-run memory across different
image sizes, other samplers, and the full accepted settings range are ungraded.

The engine's rectangular presets pass the same native mean-error grade
(main 33463, 2026-10-01):

| Size | Mean RGB error /255 | Maximum error /255 | Channels within 8 | Sampled GPU MiB | Click through readback |
|---|---|---|---|---|---|
| 1344 x 768 | 0.598940895 | 51 | 0.998990175 | 15690 | 56.18 s |
| 768 x 1344 | 0.871763845 | 141 | 0.993237046 | 15642 | 57.92 s |

Each run checks six dimension refusals, including oversized area, axis,
misalignment and the maximum signed Integer, then six ordered Euler steps,
finite output, busy/close guards and zero active handles. Both final grader
processes exit 0 with verified profile-only browser cleanup. Evidence is
`build-output/sdxl-dimensions/browser.out`, with native evidence directories,
PNGs and `.gpu.json` samples beside it. `memory.jsonl` records fresh RAM and
commit headroom before each GPU job; native evidence also records memory
immediately before its guest. The native renders take 14.12 and 14.73 s.

The dimension check has constant heap/time cost and retains the previous
maximum pixel count. Shape-dependent pools retain 18380487096 logical buffer
bytes after each rectangular run, above the square run's 16373573432 bytes.
These are not physical VRAM measurements; sampled device-wide GPU use above
is also not an exact peak. Mixed-size repeated generations remain ungraded.
This proves the engine and `SdxlArm`; the demo chooser still generates square
images. Spark's existing rectangular controls require a rebuild to include
the updated engine.

The sampler matrix passes at 1024 x 1024, six steps, CFG 2 and seed 7201
(main 33680, 2026-10-01). Each row uses its matching native render and the
unchanged mean RGB error limit below 4/255:

| Sampler | Mean error /255 | Maximum error /255 | Click through readback | Byte-heap watermark, bytes |
|---|---|---|---|---|
| Euler | 0.754852 | 169 | 54.68 s | 6334440 |
| DPM++ SDE | 1.076351 | 170 | 83.10 s | 6334440 |
| DPM++ 2M | 1.307890 | 208 | 54.49 s | 6334440 |
| DPM++ 2M SDE | 1.274557 | 248 | 56.16 s | 6334440 |
| DPM++ 2M SDE Heun | 1.254641 | 216 | 56.68 s | 6334440 |
| DPM++ 2S a | 1.644087 | 160 | 80.96 s | 6334440 |
| DPM++ 3M SDE | 0.989757 | 255 | 74.87 s | 8435720 |

The 3M row includes cancellation during noise preparation, cancellation
after two sampling steps and a full regeneration on the same model. Every
row ends with zero active GPU handles and noise workers, and a successful
process exit after profile-only browser cleanup.
The byte-heap values include other page allocations and the noise fixture;
they are not isolated sampler allocations or JavaScript heap measurements.
Sampled device-wide GPU use spans 15576-15660 MiB. Fresh RAM and commit
headroom are recorded before each GPU job.

The grade also checks the hashed Forge Brownian fixtures: 3840 values within
1e-5, all query coordinates within one f32 ULP, and a wrong-draw control.
Those use seed 20260929 and 256-value draws. They establish primitive parity;
the full images establish the end result for seed 7201 and image-sized draws.
Unsupported sampler IDs, six dimension refusals, ordered steps and busy/close
guards are checked. The native Brownian golden remains unchanged.
`diffusion-sqrt-fixed` compares result bits with the original 60-update
Newton loop. The fixed-point exit adds no allocation and preserves that
loop's result; it is not a claim of correctly rounded square roots.

Evidence is `build-output/sdxl-noise/matrix.out`, `matrix-summary.json`,
PNGs and GPU samples, using the existing `sdxl-dpm/native-*` references.
The matrix uses compiler `1C9168D510D4C60A`. All PNG hashes match the prior
sampler matrix. Native/Wasm/GPU noise evidence is `sdxl-noise/reference1/`
and `noise-browser-evidence.json`; the noise module rebuilt on the current
compiler is byte-identical to the module proved on `12BF5AD490E635F1`.
Both self-contained page builds pass and embed the proved noise module and
shader bytes (`packages2.out`, `package-evidence.json`). The demo and Studio
sampler controls are separate from this engine proof.

Noise preparation uses `BrowserNoiseWasm.codex`, compiled from the same
`BrownianTree` and `NoiseGen` arithmetic as native. `noise-bank` exports an
eight-byte header (little-endian u32 byte length and vector width), followed
by the native f32 words in draw order. The bridge transfers those bytes and
uses GPU buffer copies for each draw, preserving negative zero and NaN
payloads as well as ordinary values. No GPU transcendental approximation
replaces native noise arithmetic.

Run `node apps/diffusion/noise-bank.mjs <new-output-directory>` for the
native/Wasm proof. Its output directory must not already exist and must
contain no spaces. Then run
`node codex/plugs/html/arms/noise-bank.mjs <arm.html> <noise.wasm> <native-evidence-directory>`
reusing the first command's directory and module with the compiled SdxlArm
above. Evidence hashes bind
the compiler, bundled sources, native program, Wasm and every noise bank.
All 21 cases match every byte natively, in Wasm and after browser GPU
upload/download (2026-10-01). The cases cover the five stochastic IDs at
256/65536/64512 values, seeds 20260929/7201/0, and six steps; 256 values,
seed 1 and 50 steps; and SDE at 65536 values, seed 7201 and 50 steps together.
These are measured fixtures, not exhaustive input coverage.

The six-step SDE Wasm proof takes 4.18 s at 65536 values, including byte
comparison and hashing. The combined
maximum case takes 33.82 s in Node and 35.49 s through browser GPU readback,
with an 855638016-byte temporary arena and a 25690112-byte bank. Eight repeated
small jobs, prelaunch/active cancellation, worker errors, allocation/upload
refusals, malformed modules, timeout and throwing-callback controls pass.
The browser proof ends with 39 workers created and terminated, no live GPU
handles and an unchanged page byte heap of 16 bytes. It does not measure
physical browser-memory reclamation.

Brownian CPU values and work remain O(materialized nodes times latent
elements) within each worker. Bank storage, transfer and GPU storage add
O(draw count times latent elements); draw copies are O(latent elements).
The worker arena is job-local and released before callback, so repeated
jobs do not retain Brownian trees in the page. The 50 ms cancellation poll
adds bounded work while preparation is active. No full settings-range or
long-run physical-memory bound is claimed. Compiler heap/time is unchanged.

The diffusion demo chooser supports both families (main 33426).
`build-browser-page.mjs` embeds base and CLIP WGSL; tokenizer embedding and
`--no-tokenizer` folder loading retain their existing routes. SD1.5 uses
512 x 512 Euler/uniform, default 20 steps and CFG 7. SDXL uses 1024 x 1024
Euler/Karras, default 6 steps and CFG 2. The demo accepts 2-50 steps and
CFG 0-30. Switching closes the old model and awaits `gpu-trim-then` before
loading its replacement; unload releases active and pooled buffers.

`browser-page-arm.mjs <folder> <sd15-name> <sdxl-name> <invalid-name>
<page.html> <native.png>` grades header classification, partial-upload
failure/recovery, SD1.5 generation, SDXL generation with busy switch/unload
attempts, and another SD1.5 generation after switching back.
The folder must contain exactly three `.safetensors` files: the two real
checkpoints and an invalid fixture containing eight zero bytes. The native PNG
must have its builder's `reference-evidence.json` and `reference.cdx` beside
it; hashes bind the checkpoint, tokenizer, native source and image.
On 2026-10-01 the compiled chooser passed 24 checks, mean RGB error
0.754851659/255 and maximum 169 against the native reference, with final
active handles and pool empty. Evidence is
`build-output/diffusion-chooser/fixed.out`; the PNG and comparison JSON sit
beside `page-fixed.html`. The grader's profile cleanup was then made an
explicit check; source syntax and absence of surviving owned browsers were
verified. The 24-check run predates that additional cleanup assertion.
Tokenizer arena allocation still occurs per load; this is not a long-run
host-memory bound. Compiler heap/time behavior is unchanged.

## Independent Forge 3M comparison

The dark, saturated six-step 3M image also occurs in original Forge under
the same explicit Karras request. The 2026-10-01 grade (tools in main 34153) uses DreamShaper XL
Lightning, the longhouse prompt and negative from
`build/sdxl-browser-reference.codex`, seed 7201, CFG 2, 1024 x 1024, and no
extras. Native and browser use compiler `5AA9EBB7601DCB33`.

`build/sdxl-sampler-image-oracle.py` runs Forge's own CLIP processing,
UNet, prediction, Euler/3M samplers, Brownian tree and embedded VAE in f32.
TF32 and the SGM noise multiplier are off; initial noise is Forge's default
CUDA route on the RTX 4060 Ti. 3M requests a seven-step Karras schedule,
then drops its penultimate entry, leaving six transitions. PNG conversion truncates as Forge's processing path
does. This is a controlled component comparison, not arbitrary Forge UI
configuration or extension coverage.

| Sampler | Native/Forge mean /255 | Browser/native mean /255 | Browser/Forge mean /255 | Browser/Forge maximum /255 |
|---|---|---|---|---|
| Euler | 0.442322 | 0.754852 | 0.499235 | 2 |
| DPM++ 3M SDE | 0.956657 | 0.989757 | 0.281555 | 204 |

All pairs pass the existing mean-error gate below 4/255. Maximum channel
error is not that gate's per-pixel bound. Both fresh native PNGs and both
browser PNGs match the earlier sampler results. The 3M browser run also
passes its cancellation, dimension, finite-output, ordered-step and
zero-handle/worker checks. No sampler-engine change is indicated by this
request. Other checkpoints, schedules and step counts are not graded here.
Forge's Automatic 3M scheduler is Exponential; this comparison explicitly
selects Karras on every route.

The oracle binds native checkpoint, source, program, tokenizer and image
hashes, validates the complete generated request, and rechecks native
artifacts after inference. `--check-inputs` stops before GPU imports; an
altered clip-skip request with a refreshed source hash is refused.
`build/sdxl-forge-image-compare.mjs` checks PNG hashes and remeasures every
pair. Its limited RGB/RGBA PNG decoder must reproduce the independent
Pillow and browser-canvas mean/max measurements before grading the direct
browser/Forge pair. It is not a general PNG validator.

To reproduce, claim the GPU and run serially in a detached PowerShell
runner, checking fresh RAM/commit before each model run. Use a new proof
directory under `build-output`:

```powershell
$proof = 'build-output/forge-3m-check'
node apps/diffusion/build-sdxl-reference.mjs "$proof/native-0" 1024 1024 0
node apps/diffusion/build-sdxl-reference.mjs "$proof/native-6" 1024 1024 6
& 'D:/AI/DiffusionForge/system/python/python.exe' build/sdxl-sampler-image-oracle.py build-output/diffusion-models/dreamshaperXL_lightningDPMSDE.safetensors "$proof/forge1" $proof
node apps/diffusion/build-noise-wasm.mjs "$proof/noise.wasm"
pwsh -NoProfile -File build/bundle-app.ps1 -Src codex/plugs/html/arms/SdxlArm.codex -Out "$proof/arm.codex"
pwsh -NoProfile -File codex/plugs/html/run.ps1 -Src "$proof/arm.codex" -Out "$proof/arm.html" -Compiler seed/Codex.cdx
```

Require exit 0 at each step. For sampler IDs 0 and 6, run the existing
`sdxl-image.mjs` command in "SDXL image composition" with this arm/noise module and matching native
reference. Name the outputs `browser-0.png` and `browser-6.png`; collect
both successful command logs into `browser.out`, and write `0` to
`browser.exit` only after both commands succeed. Then run
`node build/sdxl-forge-image-compare.mjs $proof`.

Evidence is under red's `build-output/sdxl-3m-forge`: `forge1/evidence.json`,
`comparison.json`, `run.out`, `browser.out`, PNGs, native evidence folders
and `oracle-run1.py` (the exact executed oracle source). Post-run native
hashes were independently rechecked before accepting that run; the current
oracle makes those checks itself. Selected Forge source hashes and package
versions are recorded, not a complete executable dependency closure.

The Forge component pipeline took 39.08 s excluding input-hash passes;
PyTorch peak allocated/reserved bytes were 10826226176/11423186944, not
device-wide VRAM peaks. Noise recordings scale with draws times latent
elements; pixel comparisons use linear time and image-sized buffers.
Compiler heap/time behavior is unchanged.

## SD1.5 LoRA

The standard SD1.5 loader is on main 33714.

`browser-sd15-lora-load-then checkpoint file size weight code progress refuse ready`
loads one picked LoRA with an SD1.5 checkpoint. `file` is the opaque name
returned by `pick-file-then`, not its displayed basename. `size` is the
picked file's size, `weight` is a finite Real, and `code` is the WGSL emitted
from `apps/diffusion/BrowserLoraKernels.codex`. Both page builders embed that
shader as `page-data "lora"`. The callbacks follow `bl-load-then`: progress
receives loaded/total tensor counts, refusal receives text, and ready
receives a `BrowserModel`. The original no-LoRA entry remains available.

The loader accepts standard down/up/alpha LoRA tensors in F16, F32, BF16,
F8_E4M3 or F8_E5M2,
including flattened convolution weights and CLIP-L weights. Missing alpha
means alpha equals rank. It uses native `LoraNames` for checkpoint/module
mapping and applies `weight * alpha / rank * (up down)` to each resident
weight, widened from the loader's f16 representation and rounded back once.
The browser dot product is f32; the native dot product is f64. The image
grade below measures the resulting difference. LoCon mid tensors and
LyCORIS and DoRA use the merges below. This entry
accepts one LoRA per load.

`SparkModelFile.spark-lora-family` classifies the complete header before the
checkpoint loader runs. SDXL, Flux, ambiguous and unrecognized results
refuse. Existing GPU ownership survives a family refusal. Once a valid LoRA
is admitted, loading replaces the previous SD1.5 weights as `bl-load-then`
does; callers serialize loading and generation. Failed uploads or merges
free the partial model and scratch. A successful load awaits
`gpu-trim-then` before ready, keeping active model weights while destroying
pooled patch scratch. No production picker or weight-control UI is included
in this engine unit.

From the repository root, prepare a new native evidence directory without
spaces and an existing browser scratch directory:

```powershell
node apps/diffusion/browser-lora-reference.mjs <native-directory>
pwsh build/bundle-app.ps1 -Src codex/plugs/html/arms/LoraArm.codex -Out <scratch>/arm.codex
pwsh codex/plugs/html/run.ps1 -Src <scratch>/arm.codex -Out <scratch>/arm.html -Compiler seed/Codex.cdx
pwsh codex/plugs/wgsl/run.ps1 -Src apps/diffusion/BrowserLoraKernels.codex -Out <scratch>/lora.wgsl
node codex/plugs/html/arms/lora.mjs <scratch>/arm.html <BrowserKernels.wgsl> <scratch>/lora.wgsl <native-directory> <scratch>
```

Build the HTML/WGSL plugs and base shader using the recipe above. The proof
uses `realisticVisionV60B1_v20Novae.safetensors`, the repository's
`codex/test/gpu-files/lora-sd15.safetensors`, and the same CLIP tokenizer as
the SDXL grades. Native requests pass through `DiffusionDriver`'s `lora=`
parser, resident restore/apply and `dr-run`; the witness renames only the
resident entry point in its bundle. It does not exercise the driver's
request-serving loop. Hashes bind the bundled native source, CDX, model,
LoRA, tokenizer disk and PNGs. Pixel payloads must differ between weights.

The selected fixture patches CLIP layer 3's V projection, UNet input block
1's self-attention Q projection and output block 11's projection convolution.
At seed 7, six Euler/Automatic steps, CFG 7 and 512 x 512, the browser images
pass the existing mean RGB error limit below 4/255 (2026-10-01):

| LoRA weight | Mean error /255 | Maximum error /255 |
|---|---|---|
| 0 | 0.622757 | 16 |
| 0.75 | 0.536466 | 14 |
| -0.5 | 0.548116 | 11 |

The grader observes all three merge targets and differing browser PNGs.
It compares 253949 f16 roundtrip, midpoint-neighbor and overflow packing
values against .NET Half. Family refusals preserve existing GPU ownership;
post-allocation upload, merge, completion and trim refusals call back once
with no live handles. The plain loader must produce the zero-weight PNG,
and a missing checkpoint must refuse cleanly. Each finished case trims
pooled buffers and the owned browser profile is gone before process success.

Active merge scratch is one f32 target plus its up/down matrices. GPU work
is O(target elements times rank), and temporary storage is O(target elements
plus up/down elements). Header validation uses bounded arithmetic and tail
recursion. Pooled scratch is destroyed before ready. Page byte-heap growth
still includes checkpoint/tokenizer work per load; this proof establishes
GPU ownership cleanup, not a long-run host-memory bound. Compiler heap/time
behavior is unchanged.

Evidence is `build-output/browser-lora/native2/evidence.json`,
`browser-final3.out`, `browser-evidence.json` and `package-evidence.json`.
Both packaged pages build with the graded LoRA shader. The native and browser
proofs use compiler `1C9168D510D4C60A`; the independent contract reader
confirmed the source and measured claims.

## SDXL LoRA

The standard SDXL loader is on main 33764.

`browser-sdxl-lora-open checkpoint size code extra file lora-size weight lora-code ready refuse`
loads a standard LoRA with SDXL. Checkpoint and LoRA names/sizes come from
the picked-file records. `code` and `extra` are the base and CLIP shaders;
`lora-code` is `page-data "lora"`. Ready receives a `BrowserSdxlModel` after
patching and scratch trim. The no-LoRA `browser-sdxl-open` and
`browser-clip-load` signatures are unchanged.

The format and family-admission bounds are the same as SD1.5 above. UNet
and CLIP-L use the native module-name mapping. The browser's OpenCLIP-G
loader splits packed `attn.in_proj_weight` into contiguous Q/K/V row slices
and gives them HF-style names. `blo-slots` maps each split projection to
its native `te2` LoRA module. Other CLIP-G attention and MLP projections use
the same mapping. Failed loads unwind completed CLIP-L/CLIP-G owners and
the partial UNet or VAE; successful loads destroy pooled scratch before
returning the live model. This is load-time engine support, not a production
LoRA picker or weight-control UI.

The native builder also accepts `sdxl`:

```powershell
node apps/diffusion/browser-lora-reference.mjs <new-native-directory> sdxl
```

That directory must be inside the repository and contain no spaces. The
builder stages an identical copy of the real F16 LoRA
`D:/AI/DiffusionForge/webui/models/Lora/9HNHMWJZDSGD8WE7FFJCRVB8M0.safetensors`
under it for the native file root. It uses dreamshaperXL, seed 7201, six
Euler/Karras steps, CFG 2 and 1024 x 1024. Build `LoraArm` and run the same
browser command as above; the evidence selects SDXL and the real picked
file. The additional CLIP shader is read from
`build-output/sdxl-browser/clip-extra.wgsl`.

```powershell
pwsh codex/plugs/wgsl/run.ps1 -Src apps/diffusion/BrowserClipKernels.codex -Out build-output/sdxl-browser/clip-extra.wgsl
```

The file carries 2958 tensors and 986 modules. Browser loading patches 986
split targets; the native resident merge counts 922 checkpoint tensors
because each CLIP-G Q/K/V group shares one packed source tensor. The
zero-weight and 0.6 native pixel payloads differ. Observed mean RGB errors
are 0.589426/255 and 0.845582/255 respectively, below the same 4/255 gate;
maximum errors are 70 and 85 (2026-10-01). This is rendered-image parity,
not a claim of identical merged weight bits.

Full-size image cases run in separate browser processes. The first
same-device sequence passed both weighted images, then refused the third
load when available commit fell below 25 GiB. Active handles were zero and
the pool had been trimmed; those facts do not bound retained browser/driver
commit. The isolated grade establishes individual loading/rendering and
ownership cleanup. Same-device repeated full-size LoRA loading remains open.

Evidence is `build-output/browser-lora-xl/native1/evidence.json`,
`isolated.out` and `browser-evidence.json`. The controls include upload and
completion failure after CLIP-G starts, merge failure after UNet starts,
trim failure and a missing checkpoint. Each receives one refusal callback
and leaves no active handles; free-call multiplicity is not measured.
Cold HTML/WGSL builds on compiler `0406549EDD0C6740` emit the same page and
three shader byte sequences as the graded `1C9168D510D4C60A` build
(`head2-artifacts.json`). The SD1.5 regression also passes (`sd15.out`). Both current page packages embed those shader bytes
(`head/package-evidence.json`).

## LoRA BF16 and FP8

Both family entries accept BF16, F8_E4M3 and F8_E5M2 standard LoRA tensors,
including scalar alpha. Packed tensors upload as raw bytes into one temporary
GPU buffer; `blw-decode` widens every element to f32 using integer bit
operations. BF16 preserves its upper-word representation, E4M3 uses the
native finite-only format, and E5M2 uses the upper byte of IEEE binary16.
Conversion completion precedes release of the packed buffer. A merge that
uses alpha refuses nonfinite alpha. Checkpoint dtype admission remains F16/F32.

Unsupported dtypes have a named refusal message before checkpoint
loading. The Spark family classification still applies to every
admitted file. A failed conversion releases live handles; failed-load pool
storage requires the caller's trim, as for the existing loader.

The existing native helper accepts an optional dtype after the family:

```powershell
node apps/diffusion/browser-lora-reference.mjs <new-native-directory> sd15 BF16
node apps/diffusion/browser-lora-reference.mjs <new-native-directory> sdxl F8_E4M3
```

The new directory must be inside the repository and contain no spaces.
The helper converts the existing family's F16 LoRA, including alpha, and
hashes the converted file. Native `DiffusionDriver lora=` and the browser
picker consume the same converted bytes. Use `lora.mjs` with the generated
native directory as in the standard LoRA recipe; F8_E5M2 is the third dtype.
FP8 fixture conversion rounds to the nearest finite value, ties to even,
and saturates outside the finite range. BF16 rounds to nearest, ties to even.

The grader covers every 65536 BF16 encoding and every 256 encoding of each
FP8 format through file upload and GPU widening, plus a nonzero short tail
for each format. Finite output bits must agree; nonfinite output classes
must agree. The f16 packing, family mismatch and failed-load controls remain
part of the same grade.

All six dtype/family matrices pass (2026-10-01), including scalar alpha
decoding over the same encodings and nonfinite-alpha refusal. SD1.5 uses
weights 0, 0.75 and -0.5; SDXL uses 0 and 0.6. Every group also compares a
plain load with the zero-weight image and requires exact PNG equality.
Requests retain the family-specific seed, dimensions and Euler settings
from the standard LoRA recipes above. The gate remains mean error below
4/255 for every image.

| Family | Dtype | Largest image mean /255 | Largest channel error /255 |
|---|---|---|---|
| SD1.5 | BF16 | 0.622757 | 16 |
| SD1.5 | F8_E4M3 | 0.622757 | 16 |
| SD1.5 | F8_E5M2 | 0.622757 | 16 |
| SDXL | BF16 | 0.793711 | 71 |
| SDXL | F8_E4M3 | 0.802445 | 74 |
| SDXL | F8_E5M2 | 0.773916 | 70 |

Every image and injected failure leaves zero active GPU handles. The
caller trims pooled storage after each case; SDXL cases use isolated
browser processes, retaining the repeated-load limit below. Evidence lives
under `build-output/browser-lora-dtypes`: `native2-<family>-<dtype>/evidence.json`,
`browser-<family>-<dtype>/browser-evidence.json`, `matrix3.out` and
`matrix-summary.json`. Both producers and all native references use compiler
`CC3FC5222D726096`.
Demo and Studio packages embed the graded LoRA, base and CLIP shaders
(`package-evidence.json`). The engine and proof changes are on main at 33885.

Conversion time is linear in tensor elements; one packed input adds one or
two bytes per element, rounded to a four-byte boundary, to the existing
merge scratch. Merge work remains
output elements times rank. No compiler implementation changes are involved.

## LoCon mid tensors

The same SD1.5 and SDXL LoRA entries accept `.lora_mid.weight`. Before the
ordinary up/down merge, the GPU folds mid `[rank,rank,kh,kw]` and down's
`rank*cin` elements into an effective down `[rank,cin*kh*kw]`, matching
native `Lora.lora-mid-down`. Up has `rows*rank` elements. Mid rank dimensions and
the flattened checkpoint columns must agree; a mismatch refuses the load.
The standard no-mid path retains its existing calculation.

The fold keeps the uploaded down and mid until GPU completion, then frees
both and transfers effective-down ownership to the ordinary merge cleanup.
Allocation, upload, launch and completion failures release the fold scratch
and the partial model. The existing caller-trim and repeated-load limits
continue to apply. Folding work is O(rank squared times columns), with
additional GPU storage O(rank squared times kernel area plus rank times
columns). No compiler implementation changes are involved.

Use the standard LoRA recipe with the final native-helper argument `LoCon`:

```powershell
node apps/diffusion/browser-lora-reference.mjs <new-native-directory> sd15 LoCon
node apps/diffusion/browser-lora-reference.mjs <new-native-directory> sdxl LoCon
```

The helper copies the convolution's alpha/down/mid/up tensors from
`codex/test/gpu-files/lora-kinds.safetensors` and adds a zero-delta family
anchor (zero up/down, alpha 1). Only LoCon changes the image with strength; the anchor makes Spark's
classifier unambiguous. Native and browser consume the same hashed file.
Run the full `lora.mjs` matrix so the aggregate strength-distinctness and
plain/zero equality checks run, including the isolated SDXL cases.

The failure controls cover mid allocation/upload, fold launch/completion
and an incompatible mid rank with an unchanged byte count.

Both family matrices pass (2026-10-01). SD1.5 grades weights 0, 0.75 and
-0.5; SDXL grades 0 and 0.6. The nonzero images differ from zero, and each
family's plain image equals zero exactly. Every image remains below the
same mean-error gate of 4/255:

| Family | Largest image mean /255 | Largest channel error /255 |
|---|---|---|
| SD1.5 | 0.860442 | 28 |
| SDXL | 0.601847 | 70 |

All image and failure cases leave zero active handles, with one refusal
per injected failure. The standard-LoRA regression controls also pass.
Allocation mocks return Integer `0n` and require the specific
`LoCon scratch allocation failed` response. The archived shipped page also
passes that guard check in both families with no device error after trim
(`build-output/browser-lycoris/locon-alloc.out`).
Evidence lives under `build-output/browser-locon`: `native-<family>/evidence.json`,
`browser-<family>/browser-evidence.json`, `matrix.out`, `matrix-summary.json`
and `controls.out`. Both producers and native references use compiler
`CC3FC5222D726096`.
Both page packages embed the graded LoRA, base and CLIP shader bytes
(`package-evidence.json`). The engine and proof changes are on main at 33917.

## LyCORIS

The same loader entries accept LoHa, LoKr, GLoRA and difference/norm
tensors. Every slot uses native kind priority; a bias uses `.diff_b` before
`.b_norm`, independently of its weight's kind. The checkpoint is widened
to f32 for a merge and packed back once. Browser accumulation is f32;
native accumulation is f64. Image grading establishes the measured
agreement below, not exact tensor equality.

| Kind | Calculation and alpha rule |
|---|---|
| LoHa | Elementwise product of two low-rank products; alpha divided by the first down rank. Optional `hada_t1/t2` expand both Tucker factors first. |
| LoKr | Kronecker product of full or factored sides. Alpha uses the second side's rank when factored, otherwise the first side's rank; two full sides ignore alpha. `lokr_t2` expands the second side as a Tucker tensor and uses its rank. |
| GLoRA | Adds `b2*b1 + (W*a2)*a1`, with `W` captured before modification; alpha uses `a1`'s rank. |
| Difference/norm | Adds strength times `.diff`, `.diff_b`, `.w_norm` or `.b_norm`; alpha is unused. |

When alpha is used but absent, alpha defaults to the selected rank.

All shapes and counts must fit the checkpoint slot before merge work.
Inputs and intermediate factors remain owned until final completion.
Failure unwinds the current factor and every retained earlier factor;
the outer work buffer and partial model also unwind. Live-handle cleanup
does not assert immediate driver-memory reclamation; callers retain the
existing trim/serialization contract.

Difference and full Kronecker work are linear in output elements. Matrix
products multiply output work by rank; Tucker expansion multiplies by
rank squared. Device scratch scales with inputs and expanded outputs.
The browser retains both Tucker input groups until completion, unlike the
native helper's earlier release of each input group. Host control lists
hold a bounded number of handles per patch. No compiler change is involved.

The native helper isolates each format, using the same real checkpoints
and request settings as the standard LoRA grades:

```powershell
node apps/diffusion/browser-lora-reference.mjs <new-native-directory> sd15 LoHa
node apps/diffusion/browser-lora-reference.mjs <new-native-directory> sdxl LoKr
```

Run both families with each of `LoHa`, `LoKr`, `GLoRA` and `Diff`, then use
the full `lora.mjs` matrix. The helper preserves the fixture's F16, BF16
and F32 payloads and adds a zero-delta family anchor. SD1.5's CLIP bias uses
the first 768 coefficients at layer 3. LoKr adds full/full factors with
alpha 7 (ignored), and both-factored sides of ranks 2 and 4 with alpha 3
(divisor 4). These controls distinguish the native alpha rules.

Before images, the grader validates every emitted kernel layout with a
zero-element dispatch. The WGSL plug currently binds one helper buffer
mapping per kernel; Hada and GLoRA therefore use distinct dot helpers for
their two operand pairs ([codex/plugs/plugs-backlog.md](../../../../codex/plugs/plugs-backlog.md),
`WGSL-HELPER-BUFFERS`). The
grader records the LyCORIS target and phase for upload, merge, final pack,
completion and allocation failures. Second Tucker/factor failures exercise
cleanup after the first factor exists. Allocation mocks return Integer
`0n` and must reach the specific allocation guard. Every injected failure
must refuse once, leave zero handles and preserve a clear device-error state.

The main 34016 implementation passed the 2026-10-01 matrix: 28 images (including eight plain controls)
and 71 targeted failures. Each plain PNG equals its zero-strength PNG;
nonzero strengths change the image. Every completed case leaves zero live
handles and no device error. Mean error is graded below 4/255; maximum
channel error is a separate observation, not that mean's per-pixel bound.

| Family | Kind | Images | Failure controls | Worst mean /255 | Largest channel error /255 |
|---|---|---|---|---|---|
| SD1.5 | LoHa | 4 | 9 | 0.622757 | 16 |
| SDXL | LoHa | 3 | 9 | 0.829926 | 70 |
| SD1.5 | LoKr | 4 | 10 | 0.622757 | 16 |
| SDXL | LoKr | 3 | 10 | 0.736434 | 70 |
| SD1.5 | GLoRA | 4 | 8 | 0.622757 | 24 |
| SDXL | GLoRA | 3 | 8 | 0.883693 | 92 |
| SD1.5 | Difference/norm | 4 | 7 | 0.680278 | 46 |
| SDXL | Difference/norm | 3 | 10 | 0.589426 | 70 |

Evidence is under red's `build-output/browser-lycoris`: `matrix3.out`,
`matrix-summary.json`, and each `browser-<family>-<kind>/browser-evidence.json`
paired with `native-<family>-<kind>/evidence.json`. Demo and Studio package
shader bytes match the graded shaders (`package-evidence.json`); the two
whole pages are different. Head compiler 66AE634E reproduces the graded
arm, shader and both package files byte for byte (`head/artifact-equality.json`).

## Browser DoRA

The existing LoRA loaders (main 34127) accept `.dora_scale` alongside an underlying
weight format. Bias tensors retain their ordinary difference/norm path.
The browser matches native DoRA's input-channel grouping: with `rows`
output channels, flattened `cols`, and `chans` scale elements, `cols` must
divide into `chans` groups of `inner = cols / chans`. Each norm is the
square root of the sum of `(W + delta)^2` over every output row and every
element of that group.

The underlying format computes `delta` at strength 1 in a zeroed f32
buffer. A private raw-buffer path avoids widening or packing that buffer;
the outer operation owns it through normalization and completion. DoRA
then computes `(W + delta) * scale / norm`. Strength 1 uses that result
directly; other strengths blend `W + strength * (result - W)`. The model
weight is packed only after the complete operation. Browser arithmetic is
f32; native accumulation is f64.

DoRA adds two output-sized f32 buffers and two channel-sized f32 buffers
while a patch is active, plus the underlying format's factor scratch.
Norm and application work are linear in output elements. Raw inner merges
release their own inputs, and the outer operation releases the delta,
norms, scale and widened weight after completion or refusal. Zero live
handles does not establish immediate driver reclamation or a bound on
repeated page-heap growth.

The same reference command accepts `DoRA` for either family. Its staged
file contains the fixture's BF16 matrix DoRA and F16 LoCon convolution,
with BF16 convolution scale `[1,320,1,1]`, plus a zero-delta family anchor.
The standard fractional cases gain a strength-1 case. Images jointly
exercise matrix and convolution targets; they are not an independent
per-target tensor proof. The failure matrix stops at the first applicable
target, so it does not assert every injected failure for both shapes.
Callback failure injection tests unwind behavior, not a real device fault.

The SDXL grader uses a fresh browser process for each image and each
injected failure. Repeated full-model loads can cross the 25 GiB available
commit floor even after zero-handle cleanup and trim; the admission limit
is preserved. `--case controls --fault dora-norm` repeats one failure;
the full parent run collects every case and checks strength distinctness
and plain/zero equality. New case records include the native evidence
digest, and the parent refuses a digest change between children.

The 2026-10-01 primary grade passes both families. Each plain image equals
zero strength exactly, all requested strengths produce distinct images,
and every image and injected failure leaves zero live handles.

| Family | Image cases including plain | Injected failures | Worst mean /255 | Largest channel error /255 |
|---|---|---|---|---|
| SD1.5 | 5 | 12 | 0.626942 | 54 |
| SDXL | 4 | 12 | 1.134575 | 107 |

The gate is mean error below 4/255, not a bound on every channel. Evidence
is under red's `build-output/browser-dora`: `primary-summary.json`,
`matrix2.out`, `resume.out`, `native-<family>/evidence.json` and
`browser-<family>/browser-evidence.json`. The SDXL aggregate retains the
completed image cases and adds fresh-process failure records. Its audit
checks all four shader/page digests, the strength set, plain/zero equality,
target counts, one hit/refusal per fault and zero active handles.

The affected LoHa, LoKr, GLoRA, difference/norm and LoCon regressions also
pass both families: 35 images and 90 injected failures. All 35 PNG hashes
equal their pre-DoRA results (`regression-identity.json`). Combined with
DoRA, the audited matrix is 44 images and 114 failures (`summary.json`,
`regress.out`). Unsupported dtype and family guards, malformed shapes,
f16 packing and all BF16/FP8 encoding controls remain in the grader.

Compiler `0FD86AE43ACF3693` reproduces the graded arm and LoRA shader
byte for byte (`head/artifact-equality.json`). Demo and Studio packages
build with the same shader bytes (`package-evidence.json`); Studio includes
the writing-pane caller from main 34120. These package checks establish
compilation and shader embedding, not another Studio UI image grade.

## SD1.5 DPM++

`BrowserSd15.codex` exposes
`browser-sd15-sample-then model bpe prompt negative settings sampler progress ready refuse`.
`BrowserSd15Settings` carries `b15-width`, `b15-height`, `b15-steps`,
`b15-seed` and `b15-cfg`. Dimensions are multiples of 64 from 64 through 768;
steps are 1-50 and CFG is finite in 0-30. Callers serialize loading,
generation and model disposal. Ready transfers ownership of a planar f32
3 x height x width GPU image to the caller; refusal receives text. Every
completed, refused or cancelled job releases its contexts, latent, sampler
history and noise handles. The model weights remain loaded.

Sampler IDs match SDXL's table above. ID 0 is Euler with the native
Automatic/Uniform schedule. IDs 1-6 use explicit Karras, including 3M's
extra sigma and discarded penultimate sigma. They do not select the native
Automatic defaults for the SDE variants. The existing
`browser-txt2img-then` and `browser-txt2img-timed` Euler entries keep their
signatures and multi-step image path; their prompt-failure cleanup is fixed
as described below.
The new entry calls `unet15-denoise-with`, guides the positive and negative
contexts at their own token lengths, and uses the SD1.5 VAE scale 0.18215.
It shares the graded `BrowserDpm` equations and `sdxl-noise` Wasm bank.

Progress reports `encoding`, `noise`, `sampling` and `decoding`; a negative
result cancels the job. Noise preparation keeps the worker contract above.
Sampling reports completed steps, so SDE/2S midpoint denoiser evaluations
do not add progress steps. Cancellation does not unload the model.

The shared Uniform/Normal schedule now handles one step as timestep 999
followed by zero, avoiding division by `steps - 1`. Existing multi-step
sampler-table expectations remain unchanged. Failed prompt joins,
subsequent chunk encodes and emphasis transfers free the condition buffers
they own, including accumulated earlier chunks.

Prepare a new native directory without spaces and an existing scratch
directory. Build the HTML plug, base WGSL and noise module as above:

```powershell
node apps/diffusion/sd15-sampler-reference.mjs <native-directory>
pwsh build/bundle-app.ps1 -Src codex/plugs/html/arms/Sd15SamplerArm.codex -Out <scratch>/arm.codex
pwsh codex/plugs/html/run.ps1 -Src <scratch>/arm.codex -Out <scratch>/arm.html -Compiler seed/Codex.cdx
node codex/plugs/html/arms/sd15-samplers.mjs <scratch>/arm.html <BrowserKernels.wgsl> <noise.wasm> <native-directory> <scratch>
```

The native witness uses DiffusionDriver parsing/rendering with one resident
realisticVisionV60B1_v20Novae checkpoint, prompt `a photo of a cat`, empty
negative, seed 7, six steps, CFG 7 and 512 x 512. It also renders one-step
Euler. The browser keeps one model loaded for the matrix, requires ordered
steps and live-handle counts returning to that model's baseline, and grades
each PNG by the same mean error below 4/255. The 3M case cancels during an
active noise worker and after two sampling steps, then regenerates. Prompt
join, emphasis and later-chunk failure controls require the same GPU baseline.

The matrix passes (2026-10-01), with the following RGB-byte errors against
native. Euler's six-step PNG also equals the previously graded Euler PNG.

| Sampler | Steps | Mean error /255 | Maximum error /255 |
|---|---|---|---|
| Euler | 6 | 0.622757 | 16 |
| DPM++ SDE | 6 | 0.880494 | 20 |
| DPM++ 2M | 6 | 0.591273 | 16 |
| DPM++ 2M SDE | 6 | 0.667525 | 13 |
| DPM++ 2M SDE Heun | 6 | 0.640680 | 11 |
| DPM++ 2S a | 6 | 1.406086 | 50 |
| DPM++ 3M SDE | 6 | 0.568115 | 8 |
| Euler | 1 | 0.528085 | 6 |

Every case returns to the 1022 loaded-model handles with no noise workers.
The final close and trim leave zero handles, pooled buffers and workers.
The 3M grade observes cancellation while the worker registry is active;
sampling cancellation and regeneration follow on the same model. The
prompt-failure controls and existing `diffusion-sampler-table` golden pass.
Evidence is `build-output/sd15-dpm/native1/evidence.json`, `browser4.out`,
`browser/evidence.json` and `head-build.out`; the browser proof uses compiler
`CC3FC5222D726096`, with the recorded native reference on `0406549EDD0C6740`.
Both demo and Studio packages embed the graded base shader and noise module
bytes (`package-evidence.json`). The engine and proof sources are on main
at 33839.

History uses at most two latent buffers. Bank storage scales with latent
elements times stochastic draws; denoising work scales with steps at fixed
shape, with extra midpoint evaluations for SDE and 2S. Initial noise still
allocates `4 * latent elements` bytes in the page heap per job, and prompt
emphasis also uses that heap. GPU/worker cleanup does not establish a bounded
page heap. The one-step branch has constant cost; failed prompt cleanup is
linear in remaining chunks. Compiler implementation is unchanged.

## Open

### Browser follow-up work

The chooser, SDXL-DIMENSIONS, SDXL-DPM and SDXL-NOISE-COST are complete.

| ID | Capability | Remaining work |
|---|---|---|
| BROWSER-LORA-RELOAD | Repeated full-size LoRA loads on one device | Two SDXL LoRA image cases passed on one device, then the third was refused by the 25 GiB commit admission rule despite zero active handles and a trimmed pool. Measure and reduce retained browser/driver commit before claiming a repeated-load memory bound. |
| BROWSER-JOB-HEAP | Repeated generation page-heap growth | Initial `bt-noise` and prompt emphasis allocate page byte-heap storage per job without restoration. Worker-local Brownian trees are released, but the full page heap is not bounded across arbitrary repeated generations. |
| BROWSER-DRIVER-ENTRY | Reference chapters that compile alone | `BrowserLoraReference.codex` and `Sd15SamplerReference.codex` cite `DiffusionDriver`, which declares `opening`, so neither compiles as written (CDX3001, L-UNCITABLE); their runners rename the driver's entry in the bundle. Split `DiffusionDriver` into a citable chapter and a thin entry chapter, repoint the build scripts that compile it, and remove both rows from `build/app-sweep-baseline.txt`. |

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
  `SamplerOps` (`lincomb`, `free`; `sample-euler-with`). SDXL's DPM++ routes
  now use `BrowserDpm`; other native samplers remain outside the browser
  engine. **A page can cite a graph only in a
  chapter with no native cite**: `GpuBridge` and `WebGpu` both define
  `GpuGrid` and `GpuParam`, so a page citing `Sampler` is CDX3001. The graphs
  in citable chapters: `SamplerGraph` (Euler), `UNetGraph` and `UNet15Graph`
  (`UNetOps a`, generic in the weight handle, `UNetFile` natively),
  `ClipGraph` (`ClipOps a`, the weights read through accessor ops) and
  `VaeGraph` (`VaeOps a b`, weights and activation). The browser records:
  Euler's is `BrowserSampler` (`browser-sampler-ops`, graded exact by
  `codex/plugs/html/arms/sampler.mjs`); UNet15's is `BrowserUNet`
  (`browser-unet-ops`, the `UNetOps BuWeights` value; its SDXL `attention`
  field follows the block grade above); CLIP's is
  `BrowserClip` (`browser-clip-ops`, the `ClipOps BcWeights` value, CLIP-L's
  HF layout; the native casts to f16 are f32 copies, and the embedding gather
  is a copy of each token's row at its byte offset).
  **One SD1.5 UNet step runs in a page** (2026-09-30, `unet15.mjs` over
  `UNet15Arm.codex`): the checkpoint's 686 UNet tensors uploaded (rank 2 and up as packed f16), then
  `unet15-forward-with` on `sd15-unet-step`'s inputs, graded as that test
  grades: all 26 blocks 64/64 within 1% of rms against Forge's own step, rms
  near; input block 1 at t = 900 (3/64, rms far) and with a zero context
  (34/64) miss as natively. 19.4 s from the click to every block read back,
  the upload included, at a 16 x 16 latent. **CLIP-L encodes in a page**
  (2026-09-30, `clip.mjs` over `ClipArm.codex`): its 196 tensors (rank 2 and up as packed f16), then
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
  11, 99.998% of values within 8; 67.9 s from the click, the 2.1 GB upload
  included. The html bridge pools freed buffers by size (a page queues a whole
  generation) and `gpu-buf-show-then` draws an image buffer into a canvas.
  **The page**: `BrowserImagePage.codex`, built by `node
  apps/diffusion/build-browser-page.mjs [--tokenizer <dir>] [--out <html>]`
  into one file carrying BrowserKernels' WGSL and the tokenizer's two files
  as page-data: choose the models folder (`pick-dir-then`); every
  `.safetensors` is listed grey, then checked by its header alone against
  `sd15-layout` and shown red when any part is missing or misshapen, black and
  clickable when it is SD1.5 (6 files in 4 s on this box); a click loads that
  checkpoint (1022 tensors, 18 to 38 s; a second load frees the first's
  weights), then write a prompt and generate (2026-09-30, headless Edge on the reference card, idle:
  "a lighthouse on a cliff at sunset, oil painting", seed 7, drawn 94 s after
  the click; the tab is busy while the generation is queued).
  A page parses and weights a prompt as Forge does (`ClipPromptText`, split
  from `ClipPrompt`): `clip.mjs` chunks `clip-sd15-prompt`'s emphasis prompt
  into Forge's 77 ids and weights, and `bt-cond` applies `cp-emphasise` in the
  byte heap, within 1.8e-5 of Forge's cond. The site carries the page as
  `apps/landing/web/imagegen-browser.html` (`apps/landing/build.ps1` builds
  it with `--no-tokenizer`), linked from the landing's image card; that page
  reads vocab.json and merges.txt from the picked models folder
  (tokenizers/clip/, the setup page's step 5). A prompt of several chunks
  conditions on its chunks' conds end to end, natively and in the page, and
  the cond and the uncond each attend over their own (`UNetOps`' `ctx-len`):
  Forge's backend repeats the shorter context to a common length
  (`backend/condition.py`), which leaves attention unchanged. Graded against
  Forge: the conds at 1, 2 and 3 chunks (`clip-sd15-prompt` natively, within
  3.3e-5 in `clip.mjs`), one UNet step over a 154-token context
  (`sd15-unet-long`, all 26 blocks), and the page's image of a two-chunk
  prompt against the native render of it, mean 0.51 of 255, max 8
  (2026-09-30). SD1.5 also has the DPM++ entry and grades above;
  speed: `txt2img.mjs` draws its image 25.9 s from the click, sampling
  0.78 s a step at 512 x 512 (its printed stage log, 2026-09-30, reference card,
  GPU otherwise idle). The bridge records launches into one command encoder,
  submitted every 32 dispatches, and parses a kernel's `cx-kernel` line once;
  a submit and a header parse per launch cost 1.2 s of sampling's 20.9. The
  UNet's softmax is `bk-softmax-rows`, one launch in place, where three
  launches and a copy back through a temporary took 15% of GPU time. Where the
  GPU time goes, by timestamp queries on every pass of a 4-step generation
  (2026-09-30): `bk-conv2d-h` 57%, `bk-linear-h` 22%, `bk-attn-scores` 7%,
  `bk-attn-values` 5%, `bk-softmax-rows` 4%; the passes sum to 4.7 s of the
  run's 5.5 s of encode, sampling and decode, so the page is GPU-bound and the
  GEMM core that conv and linear share is the lever. A deeper register tile
  does not pull it on its own: `bk-linear-h` as 8 x 4 outputs a thread on
  128 x 64 tiles ran 17 to 57% faster replayed alone at the UNet's larger
  shapes (4096 x 2560 x 320, 4096 x 320 x 1280, 1024 x 640 x 640), and the
  same launches inside a generation took 385 ms, where the 4 x 4 form's run
  spent 376 on them (all linear passes 1030 ms against 1021, two timestamp
  profiles, 2026-09-30). A kernel's rate replayed alone is
  therefore not a forecast of its rate in the generation here; grade a kernel
  change by the profile. Keyed by launch shape, the profile shows the UNet's
  1280-channel convs at 8 x 8 and 16 x 16 filling 20 and 80 workgroups of the
  card's 34 multiprocessors (730 GFLOPS at 8 x 8, against 1950 at 64 x 64), so
  a conv of fewer than 136 tiles runs split in k (`bk-conv2d-hs`, up to 8
  slices of at least 256, summed by `bk-sum-splits`): conv GPU time 2687 to
  about 2311 ms over 4 steps, sampling 17.3 to 15.7 s (2026-09-30). The
  64 x 64 level's attention (8 heads, d 40) is `bk-attn-flash`, one launch
  that never writes the 8 x 4096 x 4096 scores, through `UNetOps`' `flash-at`
  and `flash` (the native record answers False, so the native graph runs as
  before: `sd15-unet-step` matches its `.expected`): peak GPU memory 7385 to
  6828 MiB and the tab's private commit 7.79 to 7.16 GB, sampling 15.92 to
  15.60 s against the same page with `flash-at` off (2026-09-30). It is
  17.2 ms a launch against 20.1 for the three it replaces, near 1.25 TFLOPS:
  every multiply-add costs about one workgroup-memory load, where the card
  issues four multiply-adds a load.
  Kernel rates, measured as 20 dispatches in one submit (one dispatch per
  submit carries a wait of about 3 ms that swamps a kernel this size), on the
  reference card: `bk-conv2d-h` at 320 channels, 64 x 64, 3 x 3 runs 2696
  GFLOPS, from 2408 before a thread staged one im2col pixel with its row and
  column divided out once; `bk-attn-values`, a per-head GEMM on the scores'
  tiles, runs 2165 GFLOPS at 8 heads of 4096 x 4096, d 40, from 888 one output
  a thread (2026-09-30).
  The row statistics and softmax rows are 256-thread workgroup reductions
  (`bk-wg-reduce`, the image from a mean 0.516 to 0.521 of 255) and the attention
  scores a per-head GEMM on `bk-linear-4x4`'s tiles (image unchanged);
  `Txt2Img`'s `ti-den` guides through `guided-with` when a request has no
  dynamic thresholding, SAG or PAG (`diffusion-first-image` writes the same
  PNG bytes either way, 2026-09-30); `ti-den3`, which also hands the sampler
  the uncond, keeps its own combine. A page reads a picked
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
  `VaeDecodeArm.codex`): dreamshaperXL's 140 decoder tensors uploaded (rank 2 and up as packed f16)
  from the picked file, then `vae-decode-with` over `browser-vae-ops` on
  `diffusion-vae-decode`'s latent, by that test's pixel rule against Forge's
  own f32 decode: max 1, all 196,608 channel values within 1 (native: max 2);
  the latent with channels 0 and 1 exchanged misses as natively (mean 60,150
  of 1000ths); two decodes 5.9 s from the click. The browser arms run by `node` (`node
  codex/plugs/html/arms/<arm>.mjs`), not through `bvt.ps1`. Every `BrowserKernels` kernel reads f32. Every kernel the SD1.5 path launches now has a browser form in
  `BrowserKernels` (72 arms): `lincomb3` there needs an output apart from its
  inputs, because WebGPU refuses one buffer bound twice as writable storage.
