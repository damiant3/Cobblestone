# Diffusion: replacing Diffusion Forge

Damian, 2026-09-29: our diffusion app replaces Diffusion Forge. This document
holds the target: everything the installed Forge supports, which is what the
replacement must match or deliberately decline.

## The plan (root, 2026-09-29)

**What exists.** `codex/foreword/ai` is layer code in fixed-point integers over
byte lists on the CPU; no pipeline runs end to end (the SDXL loader leaves every
block empty, the VAE is never called, the tokenizer is a toy, the text encoders
have no Q/K/V projections, and no test runs a forward pass). The layers are a
reference for shapes and names, not an engine.

**The route is PTX on the RTX 4060 Ti (16 GB)**, measured by reek at 4096^3:
PTX WMMA f16 45 TFLOPS (cuBLAS ceiling 84), f32 SIMT 7.2; WGSL through Edge
9.5 f16 with no tensor cores and 2 GiB per buffer; CUDA allocates 15.2 GB.
`tools/codex-vm.c` loads `nvcuda.dll` and serves device buffers to the guest (ops 40-45,
`GpuBridge.codex` `gpu-buf-*` and `gpu-launch`), bounded by device memory only.
**RULED (Damian, 2026-09-29): the NVIDIA driver (`nvcuda.dll`) is accepted,
"we can't avoid nvidia dll and we want the features therein".** The
"what we did not build we do not trust" rule does not block this route.

**The shape.** Codex orchestrates; the weights never enter guest memory. The
guest reads a checkpoint's header, then asks the host to upload a tensor
straight from its file range to a device buffer; every layer is a PTX kernel
launched on device buffers; only the final image comes back.

| stage | capability | graded by |
|---|---|---|
| 1 | **Done** (`codex/test/gpu-buffers`). Device buffers in the codex-vm GPU bridge: allocate, free, upload a file range with a dtype (f16, bf16, f32, fp8), launch PTX on buffer handles, download; the 524,288 cap gone | a 4096^3 f16 GEMM against a CPU reference on a sampled tile, and a file-range upload read back byte-exact |
| 2 | The kernel set, emitted by the Codex PTX plug and launched: GEMM (WMMA), conv2d as implicit GEMM, GroupNorm, LayerNorm, SiLU, GELU, softmax attention, add, concat, nearest upsample, timestep embedding. **Layer set done** (`codex/foreword/gpu/LayerKernels.codex`, baked by `codex/plugs/ptx/run.ps1 -Chapter` into `LayerKernelsPtx.codex`; quick GELU and causal softmax for CLIP included); GEMM and conv2d are reek's (`GemmKernels.codex`) | each kernel against an f32 CPU reference on small shapes, with a sabotage arm: `codex/test/gpu-layer-kernels`, 14 arms |
| 3 | **Done** (`codex/test/apps/diffusion-layout`). SafeTensors over a FILE: `Diffusion chapter CheckpointFile` reads the header through op 44 and uploads a bound tensor from its file offset; `Diffusion chapter CheckpointLayout` generates the SDXL and SD1.5 layouts (original LDM key names) from their configurations and binds one to a header | the real dreamshaperXL and realisticVision v6.0 headers, each bound complete against its own layout and incomplete against the other's, every count, byte total and offset equal to a host-side parse; the header itself by `build/safetensors-foreign-test.ps1` |
| 4 | **Done** (`codex/test/apps/clip-bpe-forge`, `codex/test/apps/clip-sdxl-encode`). CLIP BPE tokenizer from the real vocab and merges (`AI chapter ClipBpe`); CLIP-L and OpenCLIP-G text encoders on the GPU (`Diffusion chapter ClipEncoder`: `clip-load`, `clip-encode` giving cond at clip skip 2 and G's pooled) | token ids against Forge's CLIPTokenizer; cond_l, cond_g and pooled for two prompts against `build/clip-encoder-oracle.py`'s f32 reference, each within the deviation of the same model in f16 over all rows and over rows 1-76 (measured 2026-09-29 on "a photo of a cat", rows 1-76: cond_l 0.0050 of 0.0139, cond_g 0.0051 of 0.0550; pooled 0.0010 of 0.0067), with wrong-activation sabotages on both encoders and EOS padding on G |
| 5 | SDXL UNet, VAE decode, Euler and DPM++ SDE samplers: **the first image**, dreamshaperXL lightning at 1024x1024, the prompts in `apps/modbuilder/page/generate-art.ps1`. **The first image is made** (`Diffusion chapter Txt2Img`, `codex/test/apps/diffusion-first-image`, 2026-09-29): the "storage" prompt of generate-art.ps1 at its published settings, 1024x1024, in 354 s, written as `build-output/diffusion-first-image.png`; the Karras schedule, Euler and DPM++ SDE match Forge's k-diffusion within 5e-7 fed the same draws (`diffusion-sampler`); noise is torch's CUDA generator (Guidance and seeds). **VAE decode done** (`Diffusion chapter VaeDecoder`, `codex/test/apps/diffusion-vae-decode`): Forge's latent of a fixed 256x256 picture decodes on the GPU from the checkpoint's own weights to within 2 of Forge's f32 decode on every byte (mean 0.023; Forge's own f16 decode: max 2, mean 0.047), in 3 s for two decodes | Damian's eye, side by side with Forge at the same prompt, and a latent statistics check per step. The UNet alone first (fester): one step at a 16 x 16 latent, t = 500, context and label from formulas, per block against `apps/diffusion/UNetReference.codex`, which `build/sdxl-unet-oracle.py` produces from Forge's own UNet in float32 (20 blocks: shape, mean, rms, 64 samples). `apps/diffusion/UNet.codex` passes the whole step, `unet-forward`, every one of the 20 blocks (input 0-8, middle, output 0-8, `out`) in `codex/test/apps/sdxl-unet-step`, 64/64 within 1% of the block rms, through the shared model root `build-output/diffusion-models` (OperatorsManual, `-gpu-files`). Sabotage: t = 900 at input 1 is caught at 2/64; a zero context at input 4 is caught at only 50/64 with the rms still near, because cross-attention over the formula context moves that block about 1% of its rms, so a cross-attention defect shows in the samples and not in the rms. A step at a 64 x 64 latent (a 512 px image) took 9.5 s with every weight uploaded from the file at its use, 4.3 s with `unet-load` keeping all 1,680 UNet tensors resident (5.1 GB, loaded once in about 3.7 s), and 3.6 s with reek's `kernel_linear_fast` and `kernel_conv2d_fast` besides (2026-09-29; wall time of 1, 2 and 4 steps). The GEMMs are no longer the cost. The codex-vm launch census attributes one step (3.43 s wall, 3,006 launches, 2026-09-29): 1.53 s is the guest's own time, matching the 318 MB of PTX it re-sends each step through the command buffer; 1.79 s is device time, of which `kernel_geglu` (f64 erf series) is 583 ms, `kernel_attn_scores` with `kernel_attn_values` 518 ms (a serial f64 dot per output), `kernel_softmax_rows` 175 ms and `kernel_row_stats` 170 ms, against 175 ms for every `kernel_linear_fast`. Modules now load once and launch by handle (codex-vm ops 47 and 48, `gpu-module-load` and `gpu-launch-module` in `GpuBridge.codex`; op 45 with text is unchanged): the PTX sent per step is 0 bytes, and the step measured 2.67 s once on an idle card (2026-09-29); later runs while other lanes used the card put the device time alone between 2.5 and 6.4 s per step, so step timings need the card to themselves. GELU and GEGLU use Abramowitz and Stegun 7.1.26 for erf (1.5e-7), taking GEGLU from 583 to 63 ms per step and the device total to 1.42 s (census, one step, idle card, 2026-09-29). `un-attention` runs QK^T and PV per head through the linear GEMM on [heads, rows, 64] f16 copies (v as [heads, 64, sk]): one step at a 128 x 128 latent (a 1024 px image) went from 16.3 s wall and 11.6 s device to 10.5 s and 5.0 s, attention's two products from 7.6 s to 1.3 s; at 64 x 64 the device total went only from 1.44 to 1.29 s, because the per-head launches are small (9,199 launches per step against 3,019; census, idle card, 2026-09-29). The UNet's softmax and row statistics run a block per row (`kernel-softmax-rows-wide`, `kernel-row-stats-wide`): at 128 x 128, softmax 1.84 to 1.01 s and row stats 0.69 to 0.09 s, the device total 4.95 to 3.59 s and the wall 10.45 to 9.18 s (census, idle card, 2026-09-29). The wide softmax takes its exp from DeviceMath's `real-exp2-approx`, which the PTX plug lowers to `ex2.approx.f32`: softmax 1.02 to 0.37 s at 128 x 128 (0.34 s with the exp removed altogether), the device total 3.56 to 2.99 s and the wall 9.18 to 8.68 s. Attention writes its f16 operands directly (`kernel-softmax-rows-wide-f16` for P, `kernel-split-heads-f16` for the heads): the casts 0.48 to 0.17 s, the device total 2.99 to 2.72 s and the wall 8.68 to 7.93 s. GEGLU's erf takes its exp from `real-exp2-approx` too (CLIP's GELU and every SiLU keep the f64 exp): GEGLU 225 to 138 ms, the device total 2.72 to 2.63 s. Attention's two products run all heads in one launch (`kernel-linear-fast-batched` and `kernel-linear-wmma-batched`, one head per block-idx-z): 8,639 launches per step to 3,719, the device total 2.63 to 2.23 s. Attention is now one launch per call, `kernel-attention-fused` (`GemmKernels.codex`, graded by `codex/test/gpu-attention-fused`): a two-pass FlashAttention over 64-key tiles, no score matrix in memory, writing the merged heads; the 1024 px image went from 28.5 to 21.2 s wall and 22.65 to 15.48 s of device time, attention from 8.4 to 1.95 s (same tree, idle card, 2026-09-29), `sdxl-unet-step` unchanged at 64/64 per block. A linear whose 128 x 128 grid is under 136 blocks (two waves on 34 SMs) runs `kernel-linear-fast-n64`, a 128 x 64 tile with the same output bit for bit: the 1024-row linears and the context K/V, device 15.41 to 15.18 s and wall 21.1 to 20.9 s per 1024 px image, the image byte-identical (same tree, idle card, 2026-09-29). Not taken (root, 2026-09-29): the 77-token context K/V once per image instead of per pass, at most 0.21 s of 20.9; a cache inside `UNet.codex` goes stale on a reused buffer handle, a model switch or an in-place LoRA merge, and the safe form changes `unet-forward`'s callers. The UNet's operation-local temporaries (f16 casts, head copies, scores, softmax partials, norm statistics, the GEGLU product) return to a pool keyed by size instead of to the host, where each free was four VM exits and a device synchronisation (`un-temp`, `un-tfree`, 2 GiB held at most). One more step at 128 x 128 then costs 2.24 s of wall against 3.76 (one against three steps, 2026-09-29): 1.71 s on the device, 0.25 s of launch overhead and 0.28 s in the guest, about 26,000 VM exits against 47,000; the 1024 px first image ran in 64 s wall. Every diffusion launch goes by module handle (`Diffusion chapter GpuModules`, one slot per PTX module): the text of a module crossed the bridge a character at a time with every launch, 308 MB per 1024 px image, now 0; and the GPU bridge writes its command and reply addresses once instead of per operation (codex-vm keeps them), halving VM exits (690,643 to 378,036 per image). The image went from 52.9 to 50.3 s wall on an idle card (2026-09-29), so neither the text nor the exits was the large cost: of 50 s, about 32 s was device time and about 6 s launch overhead on the host. With codex-vm's asynchronous launches (reek, main 30659) the frees were the remaining waits, each cuMemFree draining the queue: `GpuBridge` now keeps freed buffers in a pool keyed by exact size (sizes recorded at allocation, so no stale entry; 2 GiB held at most; a refused allocation drains the pool and retries; `gpu-buf-pool-drain` for a resident server's idle release, `gpu-buf-pool-held` for the bytes held). The 1024 px image went from 37.4 to 28.8 s wall on the same tree, device time about 22 s in both (2026-09-29). By phase, from 1-step against 6-step runs alternated on an idle box (10.1 and 9.9 s against 28.7 and 32.7 s): each sampling step (two UNet passes) costs 3.7 s of wall and 3.7 s of device time, so a step is now device-bound; loading, CLIP, VAE decode and PNG together cost about 6.4 s per image. The remaining fixes in order: the next largest line of the census; `sdxl-unet-step` grades the resident, fast path and `sdxl-euler` the upload path. The UNet as k-diffusion's DiscreteEpsDDPMDenoiser (`apps/diffusion/UNetDenoiser.codex`: 1/sqrt(sigma^2+1) scaling, the nearest discrete timestep as Forge's `Prediction.timestep`, x - eps sigma) under val's `sample-euler` matches k-diffusion's `sample_euler` through Forge's UNet for 3 Karras steps, 64/64 within 1% of rms each (`codex/test/apps/sdxl-euler`, `apps/diffusion/SamplerReference.codex`). Next: classifier-free guidance, then weights resident on the device and reek's fast GEMM pair (`kernel_linear_fast`, `kernel_conv2d_fast`, about 10x the pair `UNet.codex` launches). The VAE by `build/vae-decode-oracle.py`, Forge's `IntegratedAutoencoderKL` |
| 6 | Forge parity, in the order Damian uses it: LoRA (kohya names, alpha scaling, the 7 kinds below), SD1.5, the samplers and schedulers below, hires fix, img2img, inpaint; then Flux (fp8, T5). **img2img, inpaint and hires fix built** (`Diffusion chapter Img2Img`, `img2img`, reached through `codex_image`'s `image`, `mask`, `denoise`, `mask_blur`, `hr_scale`, `hr_denoise`, `hr_steps`, `hr_cfg`): Forge's img2img schedule tail, the init latent as the posterior's sample (`vae-encode`, then mean + exp (logvar / 2) eps, times 0.13025), inpaint's binary, blurred, overlay and latent masks with the blend at every model call, the final blend and the pasted-back picture, and hires fix's bilinear latent resize and second pass. Declined here: resize (the picture must be the request's size), inpaint "only masked" and masked content other than "original", hires with an image; the guest decodes no PNG, therefore `serve.ps1` decodes the picture (GDI+, equal to PIL on every byte of a 1024 x 1024 PNG). **SD1.5 built** (realisticVisionV60B1 v20Novae): the UNet, `Diffusion chapter UNet15` (`unet15-embedding`, `unet15-forward`, 8 heads of c/8, 1x1-conv projections, over `UNet`'s operations), and the CLIP-L path, `clip-l15-spec` (clip skip 1 with the final LayerNorm, `sd15.py:36-47`) under `clip-condition-l` (no pooled; the empty negative encoded, not zeroed). Flux schnell is built (the Flux section below) and served through `codex_image` (stage 7). **SD1.5 served:** `img2img-model` takes a checkpoint as SDXL when every SDXL tensor binds, else as SD1.5 when every SD1.5 tensor binds, else refuses; SD1.5 conditions on `clip-condition-l` with no ADM vector, denoises through `unet15-denoise`, and scales the latent by 0.18215; txt2img, img2img, inpaint and hires fix all take either family. `Txt2Img`'s own `txt2img` (the first-image arm's entry) stays SDXL only | per feature, against its row in this document. The SD1.5 denoiser by `codex/test/apps/sd15-euler` against `build/sd15-unet-oracle.py`'s third output (k-diffusion's `sample_euler` through Forge's SD1.5 UNet in f32, 3 Karras steps): 64/64 at every step, step 0 within 4% of rms and the rest within 1%, because step 0 multiplies the eps error by 13.3 and Forge's own UNet in f16 deviates from its f32 there by up to 3.7% (44/64 within 1%); the sabotage, step 0 straight to sigma 2, 0/64. End to end through `codex_image` (2026-09-29, realisticVision v6.0 v20Novae, DPM++ SDE, 20 steps, CFG 7, 512 x 512): text to image, then img2img of that picture at denoise 0.5, each a correct picture by eye, and dreamshaperXL in the same server after them. The decoded image to bytes by `codex/test/apps/diffusion-pixel-forge` against `build/pixel-oracle.py` (Forge's own f32 clamp, times 255 and truncation, `processing.py:1013,1038-1039`): 2,077 f32 inputs at every byte boundary, the special values and 256 draws, all equal; the rounding `ImageOut` used before agrees on 1,177. img2img, inpaint and hires fix by `codex/test/apps/diffusion-img2img-parts` against `build/img2img-oracle.py` (Forge's own `setup_img2img_steps`, `create_binary_mask`, `apply_overlay` and `DiagonalGaussianDistribution`, and its init and hires lines over cv2, PIL and torch): 8 schedules within 1e-4 (the reference is f32), the posterior sample and the latent resize within 1e-6, every mask and the pasted-back picture byte-exact but the cv2 blur (248 of 3,072 bytes differ, by 1; cv2 blurs 8-bit in fixed point), a stand-in model sampled masked and not within 1e-5; the blur at sigma 3, the hires schedule read as img2img's, the inverted paste and the mask on the model's output only are each caught. End to end through `codex_image` (2026-09-29, dreamshaperXL, 6 steps DPM++ SDE, CFG 2, 1024 x 1024): img2img at denoise 0.5 in 72 s, inpaint of a masked ellipse at 0.9 in 109 s with every pixel outside the blurred mask kept, hires 512 to 1024 at 0.55 in 146 s. SD1.5 by `codex/test/apps/sd15-unet-step` (all 26 blocks 64/64 within 1% of rms against `build/sd15-unet-oracle.py`, Forge's UNet in f32; sabotage t = 900 at input 1 caught at 3/64, a zero context at input 1 at 34/64) and `codex/test/apps/clip-sd15-prompt` (4 prompts against `build/clip-sd15-oracle.py`, Forge's engine, within the f16 deviation; clip skip 2 and weights at 1 both caught). The encoder by `codex/test/apps/diffusion-vae-encode`: mean error 0.042 and max 3.30 against Forge's f32 mean, the same as Forge's own encoder with f16 conv operands (0.043, 3.46, measured 2026-09-29), which is the precision the conv kernel runs at; the f16 and bf16 encoders Forge would otherwise use deviate by 3.6 and 9.1 |
| 7 | **The tool call**: `codex_image` on its own MCP server, `apps/diffusion/serve.ps1` (root, 2026-09-29: not ACCP, whose pure-computation promise stays whole): prompt, negative, model and LoRAs by file name, sampler (Euler, DPM++ SDE), steps, CFG, seed, size; returns the PNG path with the parameters written into its metadata. Authority fixed at launch: the models folder read-only, an inputs folder read-only (img2img pictures and inpaint masks, decoded by the server and staged as `image.rgba` and `mask.rgba`), one output directory write-only, no caller-supplied path; sizes are multiples of 8 (Flux: 64), at most 1024 x 1024. **Sizes off multiples of 64** run as Forge's: the UNet halves by ceil and upsamples nearest to the skip's size (`unet-half`, `un-crop`; graded block by block against Forge's UNet at 15 x 11 by `codex/test/apps/sd15-unet-odd`, 75 x 57 checked the same way with each block read into a heap buffer: `un-download` reads through `un-scratch` at `#BD700000`, about 9 MB below the stack top, so a block larger than that (640 x 4275 f32 at 75 x 57) overwrites the stack and faults), and the VAE's attention takes the WMMA kernel when h w is off a multiple of 8 (`vd-linear`; `codex/test/apps/diffusion-vae-odd`, where the fast kernel is max 170 of 255 off Forge's decode and the WMMA one max 5). Against Forge (2026-09-30, the storage prompt, seed 7201, DPM++ SDE, Karras, 6 steps, CFG 2): SD1.5 600 x 456 0.27 of 255 where 576 x 448 is 0.33; SDXL 1000 x 600 1.84 where 1024 x 576 is 2.76 and CFG 2 to 2.02 moves Forge's own picture 8.07. **Resident:** one codex-vm guest serves every call, started by the first; its staging root holds a junction `models` to the models folder and, per call n, `request-<n>.txt` (key=value lines) and the staged pictures, which the driver CDX reads through `-gpu-files`; it writes the PNG and `done-<n>.txt` through `-gpu-out` and keeps the checkpoint (both text encoders, the UNet, the VAE encoder and decoder) on the device until a call names another, reclaiming each call's guest heap by a mark taken after the load; the server suspends the process between calls. Measured 2026-09-29, one server, three consecutive 1024 x 1024 calls: 55.3 s cold (boot and load included), 49.3 s and 36.2 s (img2img) resident; the device held 9,599 MiB throughout, the same after each call; the suspended guest used 0 ms of CPU in 10 s; the first call's PNG is byte-identical to `Txt2Img`'s for the same request on the same code. After `-IdleMinutes` (default 10) with no call the guest exits and the device is free again (root, 2026-09-29: the fleet's GPU runs need 7-9 GB); the next call loads again. Measured with `-IdleMinutes 0.5`: 9,599 MiB resident, 486 MiB after 50 s idle with no guest left, and the next call 54.8 s, cold. **Built:** the server, the driver and `apps/diffusion/serve-test.ps1` (protocol, every refusal, staging cleanup); `apps/modbuilder/page/generate-art.ps1` generates through one resident server instead of Forge's HTTP API (same prompts, seeds, sizes and model; Forge's batch of two as seeds seed and seed + 1), and Forge retires for the ModBuilder art: hero (1536 x 640) and storage, two candidates each, in 97 s on 2026-09-29. **Flux schnell served:** a checkpoint holding `double_blocks.0.img_attn.qkv.weight` is Flux; the driver frees the resident SDXL or SD1.5 model and both temporary pools (`gpu-buf-pool-drain`, `un-pool-drain`), runs `flux-txt2img` per call (text encoders, transformer and ae loaded and freed each time) and takes schnell's settings only (text to image, Euler, Simple or Automatic, CFG 1, no negative, no LoRA; anything else is refused); `serve.ps1` defaults a Flux model to Euler, 4 steps and CFG 1 and adds the models folder's `text_encoder` and `VAE` as `-gpu-files` roots. Measured 2026-09-29, one server: Flux 1024 x 1024 in 32.0 s with the files in the OS cache and about 130 s cold (16 GB of transformer and T5 read from disk), then dreamshaperXL, then Flux at 768 x 512; the device back at its idle 0.87 GB after each Flux call. Tokenizers without Forge: `<models>\tokenizers\clip\` holds openai/clip-vit-large-patch14's `vocab.json` and `merges.txt`, `<models>\tokenizers\t5\tokenizer.json` google-t5/t5-large's (FLUX.1's own repo is gated; the two share the vocab and charsmap); graded by `clip-bpe-forge` and `t5-tokenizer-forge` on disks minted from them, each with a one-entry sabotage that goes red, and the Flux PNG from them is byte-identical to the one from Forge's. The driver CDX is built by hand: `build/compile.ps1 -Src apps/diffusion/DiffusionDriver.codex -Out build-output/diffusion/DiffusionDriver.cdx`. **Open:** Damian registers the server; `apps/diffusion/serve-test.ps1` failed 1 of 44 checks in one run on 2026-09-29 (blu; the failing line was not captured) and passed 44 of 44 in the five runs after it, so one check is intermittent and unidentified. One cause is measured and removed: `serve-test.ps1` failed "staging is empty afterwards" whenever another `codex_image` server ran on the shared default staging root, and two servers there would share `request-<n>.txt`; `serve.ps1` takes `-Staging` and `serve-test.ps1` runs on `build-output\diffusion-staging-test`, 58 of 58 beside a live server (blu, 2026-09-29). Whether that was the unidentified failure is not known | `generate-art.ps1` regenerates the ModBuilder art through the tool |
**The layer kernels are correct, not yet fast, and stage 5 owns the difference.**
They read and write f32 but compute in f64, which Ada runs at 1/64 of f32; the
norms and softmax reduce one row per thread; and `kernel-attn-scores` and
`kernel-attn-values` compute one output per thread as a serial dot product,
which suits CLIP's 77 tokens and not the VAE's 16,384 or the UNet's, where
QK^T and PV go through the GEMM.

**Stage 4's tokenizer is not `codex/foreword/ai/Tokenizer.codex` extended.**
That chapter applies every merge in list order over the raw text; CLIP
lowercases and cleans whitespace, splits with `'s|'t|'re|'ve|'m|'ll|'d|
letters+|one digit|other non-space runs`, maps each word's UTF-8 bytes through
GPT-2's bytes-to-unicode table, marks the last symbol with `</w>`, and then
repeatedly merges the adjacent pair of LOWEST rank until none is ranked. The
inputs are `vocab.json` (49,408 ids) and `merges.txt` (48,894 merges after the
version line), 2026-09-29, identical in every family folder, e.g.
`webui/backend/huggingface/black-forest-labs/FLUX.1-dev/tokenizer/`. Their
symbols are bytes-to-unicode characters, several outside CCE, so under R-CCE the
tables are keyed by byte sequences decoded at load, never by Text. BOS 49406,
EOS 49407, pad 49407 to 77 (`TextEncoder.codex:55-57` already holds them).
**Stage 4a is the tokenizer alone** (root, 2026-09-29), graded by `a photo of
a cat` giving `49406 320 1125 539 320 2368 49407` before padding (the four
word ids are measured in `vocab.json`) and by the prompts in
`apps/modbuilder/page/generate-art.ps1`, each against the ids the Forge
tokenizer produces for it. **Built:** `AI chapter ClipBpe` loads the two
files unmodified and `codex/test/apps/clip-bpe-forge` grades it against
`build/clip-bpe-oracle.py`, which runs Forge's own CLIPTokenizer. Forge fills
a chunk with `id_end` (`classic_engine.py:172`) and then re-pads every
position after the first EOS with the tokenizer's own pad id where that
differs (`:300-303`): SDXL's tokenizer_2 pads with `!`, id 0, so OpenCLIP-G
reads zeros where CLIP-L reads EOS. **Built:** `Diffusion chapter ClipPrompt`'s
`clip-condition` takes a whole prompt as Forge's ClassicTextProcessingEngine
does (emphasis parsing, 75-token chunks with the comma backtrack and BREAK,
"Original" emphasis, the zeroed empty negative) and `clip-crossattn` joins
cond_l and cond_g into the UNet's 77 n x 2048 context; graded by
`codex/test/apps/clip-sdxl-prompt` against `build/clip-prompt-oracle.py`,
which runs Forge's own engine. `clip-prompt-nets` strips Forge's `<kind:args>` tags as
`extra_networks.parse_prompt` does and returns each lora's name, te and unet
weights as the lora extension reads them (`codex/test/apps/clip-lora-tags`,
against `build/lora-tag-oracle.py`); the caller strips the positive prompt
only, as `processing.py:511` does. **Clip skip built** (`codex_image`'s `clip_skip`, Forge's `CLIP_stop_at_last_layers`; `ce-skip`, `i2-skip`): the hidden state at -max(clip skip, the engine's minimum), 1 for SD1.5 and 2 for SDXL, with each engine's own final LayerNorm, and "Clip skip: N" above 1; Flux refuses it, as its CLIP-L gives only the pooled output. Against Forge (2026-09-30, the storage prompt, seed 7201, DPM++ SDE, Karras, 6 steps, CFG 2): SD1.5 576 x 448 at clip skip 2, 0.42 of 255, the change moving Forge's picture 10.83 and ours 10.84; SDXL 1024 x 576 at 3, 3.33 (2.76 at 2), moving Forge's 31.89 and ours 31.55. Not built: the rest of the webui prompt
syntax (`modules/prompt_parser.py`): step schedules `[a:b:0.5]`,
alternation `[a|b]` and `AND`, which today reach the tokenizer as literal
text; whitespace other than space and newline (a no-break space, say) around
a `:w)` weight or a BREAK, which Forge's regex takes as `\s` and ClipPrompt
does not; textual inversion; and Forge's tiling of the
shorter of cond and uncond to a common chunk count (`condition.py:59-72`),
which the sampler needs before `clip-crossattn` output longer than 77 rows
can reach the UNet.
The open tokenizer gaps above ASCII are `codex/foreword/ai/ai-backlog.md`.


## The SDXL checkpoint layout (stage 3)

Measured 2026-09-29 by a host-side census of the header of
`D:\AI\DiffusionForge\webui\models\Stable-diffusion\dreamshaperXL_lightningDPMSDE.safetensors`
(original LDM key names, no `__metadata__`, all 2,516 tensors F16), graded
against the released SDXL base configuration (`sd_xl_base.yaml`: 320 model
channels, multipliers 1/2/4, 2 residual blocks, transformer depth 0/2/10,
middle 10, context 2048, ADM 2816; CLIP ViT-L/14 12 x 768; OpenCLIP ViT-bigG/14
32 x 1280; VAE 128 x 1/2/4/4, 2 residual blocks, 4 latent channels). Every
figure below agrees with it. The transformer depth of the first level is 0,
not 1: `input_blocks.1` and `.2` hold no `transformer_blocks`.

| part | key prefix | tensors |
|---|---|---:|
| UNet | `model.diffusion_model.` | 1,680 |
| CLIP-L | `conditioner.embedders.0.transformer.text_model.` | 198 |
| OpenCLIP-G | `conditioner.embedders.1.model.` | 390 |
| VAE | `first_stage_model.` | 248 |

UNet, channels from each block's `0.out_layers.3.weight` and depth from its
`1.transformer_blocks.*`:

| block | channels | transformer depth |
|---|---|---|
| `input_blocks.0` | `conv_in` 4 -> 320 | -- |
| `input_blocks.1`, `.2` | 320 | 0 |
| `input_blocks.3` | downsample 320 | -- |
| `input_blocks.4`, `.5` | 640 | 2 |
| `input_blocks.6` | downsample 640 | -- |
| `input_blocks.7`, `.8` | 1280 | 10 |
| `middle_block` | 1280 | 10 |
| `output_blocks.0`, `.1`, `.2` (upsample in `.2`) | 1280 | 10 |
| `output_blocks.3`, `.4`, `.5` (upsample in `.5`) | 640 | 2 |
| `output_blocks.6`, `.7`, `.8` | 320 | 0 |
| `time_embed` 1280 x 320; `label_emb` input 2816; `out` 4 x 320 x 3 x 3; cross-attention `to_k` input 2048 | | |

Text encoders: CLIP-L has 12 `encoder.layers`, token embedding 49408 x 768,
positions 77 x 768, MLP 3072; OpenCLIP-G has 32 `transformer.resblocks`, token
embedding 49408 x 1280, positions 77 x 1280, MLP 5120, `text_projection`
1280 x 1280. VAE: encoder `down.0..3` at 128/256/512/512 with 2 residual
blocks each, decoder `up.0..3` at 128/256/512/512 with 3 each, `quant_conv`
8 -> 8 (mean and log-variance of 4 latent channels), `post_quant_conv` 4 -> 4.

Optional in SDXL CLIP-L, by checkpoint: `position_ids`, `logit_scale`,
`text_projection` (cyberrealisticXL v4 carries the first only, dreamshaperXL
the other two); SDXL reads none of them.

## The SD1.5 checkpoint layout

Measured 2026-09-29 on `realisticVisionV60B1_v20Novae` (1,143 tensors, F16) and
`_v51HyperVAE` (1,131, F32), against `v1-inference.yaml`. UNet
`model.diffusion_model.` 686: 320 channels, multipliers 1/2/4/4, transformer
depth 1 at the first three levels and 0 at the fourth, middle 1, context 768,
`proj_in`/`proj_out` 1x1 convolutions where SDXL's are linear, no `label_emb`.
CLIP-L under `cond_stage_model.` with the SDXL CLIP-L's names, 196 plus
`position_ids` (I64 in the HyperVAE file, a dtype SafeTensors reads as
unknown). VAE `first_stage_model.` 248, identical to SDXL's. The no-VAE file
also carries the 12 DDPM schedule buffers (`betas`, `alphas_cumprod` and ten
more, 1,000 each), which the layout lists as optional.
## Flux (stage 6, scoped 2026-09-29, nothing built)

Measured from the installed files' headers. `flux1-schnell-fp8-e4m3fn.safetensors`
(11,891,329,784 bytes) is the transformer alone: 776 tensors, every one
F8_E4M3 (biases and norm scales included), keys with no prefix. The merge
`0.5(flux1-schnell-fp8-e4m3fn) + 0.5(uberRealisticPornMerge_v23Final)` has the
same 776 tensors and dtype. The text encoders and the VAE are separate files
Forge pairs with it: `models/text_encoder/t5xxl_fp8_e4m3fn.safetensors`
(4.89 GB, 220 tensors, all F8_E4M3) or `t5xxl_fp16.safetensors` (9.79 GB),
`models/VAE/clip_l.safetensors` (246 MB, F16, `text_model.` keys, SDXL's CLIP-L
without the prefix) and `models/VAE/ae.safetensors` (335 MB, 244 tensors, F32).

| part | layout |
|---|---|
| input | the 16 x h/8 x w/8 latent cut into 2 x 2 patches: h/16 x w/16 tokens of 64 (`img_in` 64 -> 3072); T5's 256 tokens of 4096 (`txt_in` 4096 -> 3072) |
| conditioning vector | `time_in` (256 -> 3072 -> 3072, SiLU) of the timestep embedding (cos then sin, the timestep scaled by 1000) plus `vector_in` (768 -> 3072 -> 3072) of CLIP-L's pooled output; schnell has no `guidance_in` |
| 19 `double_blocks` | per stream (img, txt): `mod.lin` 3072 -> 18432 (shift, scale, gate for attention and for MLP, from SiLU of the vector), LayerNorm without affine at eps 1e-6, `attn.qkv` 3072 -> 9216 with bias, `attn.norm` RMSNorm of q and k per head (scale 128), `attn.proj` 3072 -> 3072, `mlp` 3072 -> 12288 -> 3072 with tanh GELU; attention joint over txt then img tokens, 24 heads of 128, scaled 1/sqrt(128), RoPE on q and k |
| 38 `single_blocks` | one stream of txt then img tokens: `modulation.lin` 3072 -> 9216, `linear1` 3072 -> 21504 (qkv 9216 and MLP 12288), RMSNorm q and k, attention, `linear2` 15360 -> 3072 (attention and GELU(MLP) side by side) |
| `final_layer` | `adaLN_modulation.1` 3072 -> 6144 (shift, scale), `linear` 3072 -> 64, then the patches back to 16 x h/8 x w/8 |
| RoPE | ids (0, row, col) per image token, 0 for text; axes 16, 56, 56 of the 128 head width, theta 10000 (`backend/nn/flux.py` `rope`, `EmbedND`) |
| T5-XXL encoder | 24 blocks of 4096: 64 heads of 64 with no 1/sqrt(d) scale, a relative position bias (32 buckets, bidirectional) from block 0 shared by all, RMS layer norm at eps 1e-6 with a scale and no bias, gated FFN `wi_0` / `wi_1` 4096 -> 10240 with tanh GELU, `wo` 10240 -> 4096, final norm; tokens from `tokenizer_2/tokenizer.json`, a SentencePiece Unigram model of 32,100 pieces (the embedding has 32,128 rows) behind a precompiled normalizer and the metaspace pre-tokenizer, then EOS 1, padded with 0 to 256 (`t5_engine.py`) |
| CLIP-L | Forge's SD1.5 settings (clip skip 1, final LayerNorm), pooled output only (`diffusion_engine/flux.py:56-68`) |
| VAE `ae` | the LDM encoder and decoder at 16 latent channels with no `quant_conv` or `post_quant_conv` (encoder `conv_out` 32 = mean and log variance); decode takes latent / 0.3611 + 0.1159 (`latent.py`, Flux) |
| sampling | flow matching: prediction `const`, noise scaling sigma noise + (1 - sigma) latent, denoised x - sigma v, the model's timestep is sigma; schnell's schedule shifts by mu = 1, sigma = e / (e + 1/t - 1) (`k_prediction.py` PredictionFlux, `diffusion_engine/flux.py:35-38`); schnell's published settings are 4 steps at CFG 1, one model call per step |

**Fitting 16 GB** (15.2 GB allocatable, stage 1's measurement). Forge runs fp8
weights by casting each layer's weight to the compute dtype (weight-only fp8):
activations are never fp8. Every e4m3 value is exact in f16, therefore the
kernel that matches Forge dequantizes a weight tile to f16 in shared memory and
runs the existing WMMA, and does not use Ada's fp8-by-fp8 tensor cores, which
quantize the activations too. Converting the transformer to f16 at upload would
double it to 23.8 GB.

| on the device | GB |
|---|---:|
| transformer, fp8, resident | 11.89 |
| VAE `ae`, f32 | 0.34 |
| CLIP-L, f16 | 0.25 |
| T5 streamed one block at a time from the fp8 file (193 M parameters a block) | 0.19 |
| activations at 1024 x 1024 (4,352 tokens), computed not measured: `linear1`'s output 0.37, three hidden states of 0.05, one head's attention scores 0.08 | about 0.6 |
| total | about 13.3 |

T5 whole (4.89 GB) beside the transformer is 17.4 GB and does not fit, and
neither does all 24 heads' scores at once (1.8 GB); attention goes a head at a
time, or tiled with a running softmax. The SDXL checkpoint (9.6 GB resident
under `codex_image`) and Flux are never resident together: a request naming the
other model frees the first (stage 7). Arithmetic, not measured: one step at
1024 x 1024 is about 1.0e14 FLOP in the linears (11.9 G weights, 2 FLOP each,
4,096 to 4,352 tokens), about 3 s at the 35 TFLOPS reek measured for
`kernel_linear_fast`.

**New work, in the order a first image needs it:**

1. fp8 weight GEMM, built: `kernel-linear-fp8` (`codex/foreword/gpu/GemmKernels.codex`,
   graded by `codex/test/gpu-linear-fp8` over `double_blocks.0.img_attn.qkv`). `y = x W^T + b` with W e4m3 on the device and x f16,
   dequantized per tile to f16 for WMMA; biases and norm scales (a few MB)
   converted to f32 at upload, which the bridge already does.
2. T5 tokenizer: **built**, `AI chapter T5Tokenizer` (`t5-tok-load`, `t5-tokenize`): the precompiled charsmap (darts-clone trie), the right strip and space-run replacement, Metaspace, Unigram Viterbi with fused `<unk>`, EOS; graded by `codex/test/apps/t5-tokenizer-forge` against `build/t5-tokenizer-oracle.py` (transformers' T5TokenizerFast over the same tokenizer.json): 15 prompts id for id, the generate-art prompts, the negative, the empty prompt, space runs, ASCII punctuation, accented Latin, full-width forms with a ligature and a circled digit, CJK, emoji, tab and newline; without the charsmap the full-width prompt differs. It reads tokenizer.json itself (`t5-json-tables`: JSON strings with every escape, base64, each score the f64 nearest its decimal, ties to even), and the same arm grades the tables it builds equal to the oracle's vocab.bin and charsmap.bin byte for byte, all 32,100 scores included, with a one-digit-changed control that differs. Open: each character is its own grapheme. The T5 engine is built: `Diffusion chapter T5Prompt` (`t5-prompt-chunks`) parses as CLIP does, tokenizes each segment without special tokens into one chunk that only BREAK closes, ends each chunk in </s> and pads it with <pad> to 256 and then to the longest chunk, graded by `codex/test/apps/t5-prompt-forge` against `build/t5-prompt-oracle.py` (Forge's own `tokenize_line`): 8 prompts id for id and weight for weight (a generate-art prompt, every weight form, BREAK, a lone BREAK, the empty prompt, 401 tokens in one chunk, BREAK inside an unclosed parenthesis, unclosed brackets), with a control that takes every weight as 1.0 and differs. The weights scale the encoder's output, which item 3 builds.
3. T5 encoder: **built**, `Diffusion chapter T5Encoder` (`t5-open`, `t5-encode`): each block's e4m3 weights uploaded at use and freed after, the residual stream f32, GEMM inputs f16 through `kernel_linear_fp8`, attention through `kernel_attn_scores` at scale 1 plus the bias, the gated FFN's product through LoRA's `kernel_lora_prod`. Graded by `codex/test/apps/t5-encoder-step` against `build/t5-encoder-oracle.py` (transformers' T5EncoderModel over the fp8 file cast to f32, on the CPU): the embedding, all 24 blocks and the final norm 64/64 each within the larger of 1% of rms and twice the model's own deviation with f16 GEMM inputs (at most 0.48% in the blocks, 0.75% at the final norm), all 511 buckets equal to transformers'; the final output against block 23's reference gives 13/64, and block 0 with the bias zeroed 11/64. One 256-token chunk encodes in 4 s wall (2026-09-29). A prompt's conditioning is `t5-condition`: T5Prompt's chunks encoded, Original emphasis on each (rows by their weights, then by the mean before over the mean after), stacked; graded in the same arm against Forge's own engine and `EmphasisOriginal` over a weighted prompt, 64/64, where the output without emphasis gives 62/64 (12 of the 256 rows carry a weight, and 2 samples fall on them). Why f32: measured 2026-09-29 on `t5xxl_fp8_e4m3fn`, each weight cast to f32
   at use as Forge casts it, transformers' own T5EncoderModel in f32, the 8
   generate-art.ps1 prompts, its negative and the empty prompt: the hidden
   state passes 65,504 from block 10 (200,241) to block 23 (252,116), and
   block 10's `wo` writes 139,725; the largest value entering any Linear, the
   f16 operand, is 5,152 (block 10's `wo`), 12.7 times under the f16 limit.
   The encoder's output after the final norm stays within 7.7.
4. Kernels: **built** in `LayerKernels.codex` section Flux, graded by
   `codex/test/gpu-flux-kernels` against f64 references: `kernel-gelu-tanh`,
   `kernel-rms-stats-wide` with `kernel-rms-norm` (a scale), `kernel-layer-norm-mod`
   (LayerNorm without affine, then x (1 + scale) + shift, over
   `kernel-row-stats-wide`'s statistics), `kernel-gated-add` (x + gate y),
   `kernel-rope` (in place at a column offset from a cos / sin table the host
   builds once per size, in f64 as Forge does), `kernel-patchify` and
   `kernel-unpatchify`. Attention over 4,352 tokens runs through the batched
   GEMM and `kernel-softmax-rows-wide-f16`: `kernel-attention-fused` is written
   for 64-wide heads, and 128-wide ones need more than its eight fragment slots.
   **The transformer is built**: `Diffusion chapter Flux` (`flux-load`, all 776
   tensors resident, matrices e4m3 through `kernel-linear-fp8`, biases and norm
   scales f32; `flux-run`), graded by `codex/test/apps/flux-step` against
   `apps/diffusion/FluxReference.codex`, which `build/flux-oracle.py` makes from
   Forge's own `IntegratedFluxTransformer2DModel` in f32, one module's weights
   on the device at a time: a 16 x 16 latent, 32 text tokens, sigma 0.75, all 82
   module outputs (img_in, time_in, vector_in, txt_in, the 19 double blocks' img
   and txt, the 38 single blocks, final, out) 64/64 within 1% of the rms with the
   rms near; sigma 0.25 leaves the output at 2/64 and a zero context at 3/64. The
   call takes 9 s with the 11.9 GB load (2026-09-29).
Conditioning, **built**: `Diffusion chapter FluxCondition` (`flux-condition`): crossattn is `t5-condition`'s output and vector CLIP-L's pooled output of the first chunk, final-normed at the first EOS and unprojected (`clip-l-flux-spec` over `clip_l.safetensors`, `flux-clip-l-layout`; `ClipSpec`'s `cs-pool` asks for the pooled output, projected only for OpenCLIP). Graded by `codex/test/apps/flux-condition` against `build/flux-clip-oracle.py` (transformers' CLIPTextModel under Flux's config): the first chunk's ids equal Forge's, all 768 pooled values within 1% of rms (the model's own f16 deviation is 0.51%), the T5 context 256 rows; the empty prompt's pooled output gives 2/768.
5. The 16-channel VAE: **built**. `vae-decode` skips `post_quant_conv` and
   `vae-encode` skips `quant_conv` where the checkpoint has none, and the
   encoder's `conv_out` is then 32 wide (16 means, 16 log variances). Graded by
   `codex/test/apps/flux-vae` over Forge's `models/VAE/ae.safetensors` against
   `build/flux-vae-oracle.py` (Forge's `IntegratedAutoencoderKL` at 16 channels
   in f32): the decode of Forge's latent within 1 of Forge's f32 decode on every
   byte (mean 0.012; Forge's own f16 decode is max 1 from it), the encoded mean
   within 0.5 on 16,322 of 16,384 values, the round trip max 8, mean 0.063. The
   latent map latent / 0.3611 + 0.1159 is `flux-latent-to-vae` (Forge's
   process_out) and `flux-vae-part` binds the bare file: Forge's mean put through
   process_in and mapped back decodes at max 1, the unmapped latent at max 251.
6. The flow sampler: **built** in `Diffusion chapter Flux` (`flux-sigma-at`,
   `flux-simple-sigmas`, `flux-denoise` for `sample-euler`), graded by
   `codex/test/apps/flux-euler` against `build/flux-oracle.py`'s third output,
   k-diffusion's `sample_euler` over Forge's own `PredictionFlux` (mu = 1) and
   `simple_scheduler`: the four sigmas equal Forge's (1, 0.89077, 0.73106,
   0.47537, 0), and each step's latent 64/64 within 2% of its rms, the worst
   0.5, 0.7, 0.8 and 1.2% (f16 GEMM operands against Forge's f32); the unshifted
   first sigma lands at 58.6%.
   A 1024 x 1024 rehearsal on formula conditioning (256 text tokens), seed
   7201, 4 steps, decoded by ae: it fits the card with the 11.9 GB transformer
   resident and made a coherent picture in 93 s wall, 86 s of device time, of
   which `kernel_linear_fp8` is 68 s (about 6 TFLOPS, against about 30 for
   `kernel_linear_fast`), softmax 8.5 s and the head GEMMs 3.2 s (idle card,
   2026-09-29). **The first Flux image is made** (`Diffusion chapter FluxTxt2Img`,
   `codex/test/apps/flux-first-image`): blu's `flux-condition` (T5 and CLIP-L's
   unprojected pooled), `flux-sample`, the latent map and ae, generate-art.ps1's
   "storage" prompt, seed 7201, 1024 x 1024, 4 steps, written as
   `build-output/flux-first-image.png`, in 37.5 s wall with the text encoders and
   the 11.9 GB load; 28.1 s of device time, of which the linears on
   `kernel-linear-fast-fp8` are 8.7 s (68 s on `kernel-linear-fp8`), softmax 8.5 s
   and the head GEMMs 3.2 s (idle card, 2026-09-29). Attention is
   `kernel-attention-fused-128` (`codex/test/gpu-attention-fused-128`): one pass
   over 32-key tiles with a running max and sum per row, the output rescaled
   only when a row's max rises by more than 8, each block one 64-column half of
   a head, 49152 bytes of shared memory, the most that keeps two blocks on an
   SM (a 64-key layout at 70656 bytes held one and ran 71.3 ms per launch
   against 46.2). 26 to 28 ms per launch at the Flux shape against 46.2 ms for
   the two-pass kernel; the first image 32.3 s wall against 35.6 s (same
   session, idle card, 2026-09-29); `flux-euler` worst 0.3, 0.4, 0.6, 0.7% of
   rms per step. The fp8 linears take their row tiles first (`kernel-linear-fast-fp8-mfirst`) whenever x is at most 40 MB, so W is read from DRAM once rather than once per row band: 4352 x 21504 x 3072 in 19.0 ms against 29.2, and `kernel-gelu-tanh` computes in f32 (worst 9.6e-7 relative to f64 over 53 M activations, 2.2 ms per launch against 8.6 in f64): the first image's device time 18.9 s and its wall 29.0 s with the files cached (2026-09-29). Forge's image is not the
   reference: the noise is SplitMix64, not torch's generator.

Graded as SDXL was: each block against Forge's own module in f32 on formula
inputs (as `build/sdxl-unet-oracle.py`), T5 and the tokenizer against Forge's
engine, the sampler against Forge's PredictionFlux.
## The installed Forge

`D:\AI\DiffusionForge\webui` is Stable Diffusion WebUI Forge v2.0.1v1.10.1
(`modules_forge/forge_version.py`), git HEAD `f5330788` on `main`, 2024-12-22.
Every row below cites that tree (paths relative to `webui`). Rows are what the
source accepts, measured by reading it on 2026-09-29; none was run.

## Checkpoint families

A checkpoint is typed by `huggingface_guess.guess` (`backend/loader.py:240`)
and must then match one of exactly four engines (`backend/loader.py:26`), or
loading refuses with "Failed to recognize model type!" (`loader.py:333-337`).

| Family | Engine | Text encoders | VAE |
|---|---|---|---|
| SD 1.x, with SD1.5 inpaint and instruct-pix2pix | `backend/diffusion_engine/sd15.py:14` | CLIP-L (`sd15.py:21`) | SD VAE |
| SD 2.x, with 2.x inpaint, v-prediction, unCLIP | `sd20.py:14` | CLIP-H (`sd20.py:20`) | SD VAE |
| SDXL, with SSD-1B, Segmind Vega, KOALA 700M/1B, SDXL ip2p, SDXL inpaint | `sdxl.py:15` | CLIP-L + CLIP-G (`sdxl.py:22-23`) | SDXL VAE |
| Flux dev and Flux schnell (schnell by repo name) | `flux.py:16,34-47` | CLIP-L + T5-XXL (`flux.py:24-25,70`) | 16-channel ae (`flux.py:32`, `backend/nn/vae.py:180`) |

Typed but not loadable, because no engine accepts them: SDXL Refiner, SD3,
Stable Cascade, HunyuanDiT, AuraFlow, SVD, Kolors
(`repositories/huggingface_guess/huggingface_guess/model_list.py`). Chroma
and Z-Image appear nowhere in `backend`, `modules` or `modules_forge`.

## Weight formats

| Format | Evidence |
|---|---|
| `.safetensors`, `.ckpt`, `.gguf` files | `modules/sd_models.py:171`; `backend/utils.py:25-28` |
| GGUF Q2_K, Q3_K, Q4_0, Q4_1, Q4_K, Q5_0, Q5_1, Q5_K, Q6_K, Q8_0 | `backend/operations_gguf.py:5-15` |
| bitsandbytes nf4 and fp4 | `backend/operations_bnb.py:7,65`; `loader.py:88-100,130-144` |
| fp8 e4m3fn and e5m2 storage, each with an "(fp16 LoRA)" variant | `modules_forge/main_entry.py:28-37` |
| Text encoders stored in fp8, nf4 or GGUF | `loader.py:82-100` |

## LoRA and other networks

All loading goes through the ComfyUI collection: the webui collection's
`packages_3rdparty/webui_lora_collection/lora.py` is a two-line stub.

| Kind | Evidence |
|---|---|
| LoRA and LoCon, with mid / Tucker weights | `packages_3rdparty/comfyui_lora_collection/lora.py:81`; `backend/patcher/lora.py:132` |
| LoHa | `comfyui_lora_collection/lora.py:102`; `backend/patcher/lora.py:201` |
| LoKr | `comfyui_lora_collection/lora.py:154`; `backend/patcher/lora.py:154` |
| GLoRA | `comfyui_lora_collection/lora.py:162`; `backend/patcher/lora.py:239` |
| DoRA (`dora_scale` on each kind above) | `backend/patcher/lora.py:52,147` |
| Full diff, diff_b, norm (w_norm, b_norm) | `comfyui_lora_collection/lora.py:168-189` |
| Flux LoRA key mapping | `comfyui_lora_collection/lora.py:321` |

Not supported: IA3, OFT and BOFT (no patch type in `backend/patcher/lora.py`).

`apps/diffusion/Lora.codex` merges LoRA and LoCon (with its `lora_mid`), LoHa (with
`hada_t1`/`t2`), LoKr (either factor full or low-rank, with `lokr_t2`), GLoRA, a full
`diff`, the bias patches (`diff_b`, `w_norm`, `b_norm`) and DoRA (`dora_scale`) into an SDXL or SD1.5 checkpoint's UNet and text-encoder tensors (SD1.5's CLIP-L under `cond_stage_model.`): UNet modules under both
kohya namings, LDM (`lora_unet_input_blocks_...`) and diffusers (`lora_unet_down_blocks_...`);
`lora_te1_` (and `lora_te_`, `text_encoder.`) onto CLIP-L and `lora_te2_` (and `text_encoder_2.`) onto OpenCLIP-G, q/k/v as row slices of the fused
`attn.in_proj_weight`. Alpha and a per-file strength apply in f32 with one cast to f16, as
Forge does; graded bit-exact against `build/lora-merge-oracle.py` by
`codex/test/apps/diffusion-lora`, the kinds no installed file uses on the synthetic
`codex/test/gpu-files/lora-kinds.safetensors`. Every installed SDXL LoRA is plain LoRA
(2026-09-29). **Served:** `codex_image`'s `loras` reach the resident SDXL or SD1.5 model through `Diffusion chapter LoraResident` (`lora-resident-apply`): each tensor a LoRA names is re-read as f32, merged as above and written over its resident buffer (an f16 weight cast back, an f32 bias kept, OpenCLIP-G's fused `in_proj` split into its three thirds); a change of LoRA set on the same checkpoint puts back every tensor the old set patched (`lora-resident-restore`, from the file with the loader's conversion) and merges the new set without a reload, each merge starting from the resident copy widened on the device: 12.8 s for a 512 x 512 call that adds pixel-art-xl and 15.5 s for one that switches to myststyle, against 13.5 s for a no-LoRA call (2026-09-29); the PNG's parameters carry Forge's `<lora:name:weight>` tags. Graded by `codex/test/apps/lora-resident`: every resident buffer byte-equal to `lora-merged-weight`'s merge (UNet, CLIP-L, the three G thirds), the f32 biases within one f16 rounding of it and changed from the file, an unnamed tensor untouched; wrong thirds and a skipped f32 write each go red on their own lines. End to end: weight 0 gives the same pixels as no LoRA, and dropping the LoRA again restores them byte for byte on the same resident guest. Flux takes no LoRA yet. Open: `lora_te2_text_projection` (OpenCLIP stores the transpose), and Forge's rarer UNet forms (`lora_prior_unet_`, bare LDM names, diffusers `unet.` / peft names).

## VAE and text-encoder files

| Item | Evidence |
|---|---|
| A separate VAE from `models/VAE` or a `*.vae.safetensors` beside the checkpoint | `modules/sd_vae.py:79-105` |
| One "VAE / Text Encoder" selector taking VAE and text-encoder files together, from `models/VAE` and `models/text_encoder` (ckpt, pt, bin, safetensors, gguf) | `modules_forge/main_entry.py:79,146,152,160-162` |
| Full VAE or TAESD, separately for encode and decode | `modules/shared_options.py:213-214`; `modules/sd_vae_taesd.py` |
| Approximate latent preview | `modules/sd_vae_approx.py` |
| Tiled VAE, only as an automatic out-of-memory fallback | `backend/patcher/vae.py:143-145,180-182` |

## Samplers

25 (2026-09-29), merged at `modules/sd_samplers.py:11-15`:

| Source | Samplers |
|---|---|
| k-diffusion, `modules/sd_samplers_kdiffusion.py:15-33` | DPM++ 2M, DPM++ SDE, DPM++ 2M SDE, DPM++ 2M SDE Heun, DPM++ 2S a, DPM++ 3M SDE, Euler a, Euler, LMS, Heun, DPM2, DPM2 a, DPM fast, DPM adaptive, Restart, HeunPP2, IPNDM, IPNDM_V, DEIS |
| timestep, `modules/sd_samplers_timesteps.py:14-17` | DDIM, DDIM CFG++, PLMS, UniPC |
| other | LCM (`modules/sd_samplers_lcm.py:101`), DDPM (`modules_forge/alter_samplers.py:21`) |

**Built** (`Diffusion chapter Sampler`): Euler, Heun, DPM2, DPM2 a, Euler a, LMS (7.2e-7; an Euler control against its fixture is off by 0.15), DPM++ SDE, DPM++ 2S a, DPM++ 2M and
DPM++ 2M SDE (midpoint and Heun), DPM++ 3M SDE, IPNDM, IPNDM_V, HeunPP2 and DEIS (tab, order 3), each within 1.7e-6 of Forge's own k-diffusion sampler
on 6 Karras steps of an analytic denoiser fed the same draws, and DDIM at eta 0,
UniPC (bh1, order 3, Forge's defaults) and PLMS within 3.6e-6 of Forge's timestep
`ddim`, `unipc` and `plms` over the 6-step timesteps through the classic eps estimation (`codex/test/apps/diffusion-sampler` against
`build/sampler-oracle.py`, with shifted-draw sabotages for the draw-taking rows and a neighbouring sampler's fixture as the control for the others). Also built: LCM
(within 2.4e-7, and its Automatic schedule, the LCM wrapper's `get_sigmas`,
within 6.7e-8 relative), DDPM (2.3e-6), Restart at 20 and 36 steps (1.7e-6;
below 20 steps it is Heun), DPM fast and DPM adaptive at Forge's eta 1
(2.8e-5 and 7.5e-6, where Forge's own f32 run differs from its f64 run by
3.4e-5 and 3.1e-6; adaptive takes Forge's 11 accepted steps), and DDIM CFG++
at CFG 3 (2.1e-5 at a largest value of 16.7, Forge's own f32-to-f64 spread
2.0e-5). Two facts of the installed Forge decide those rows: `ddim_cfgpp` sets
`cond_scale_miltiplier` and nothing reads it, so CFG++ runs at the full CFG
scale; and `sampling_function` calls the UNet directly, so
`LCMCompVisDenoiser`'s c_skip and c_out never apply. Every sampler in the
table is built. DDIM CFG++ also needs the unconditional D beside the guided one, which its denoiser takes as a third argument.

**Served** (`Diffusion chapter Txt2Img` `ti-run`, `ti-sigmas`; `codex_image`'s `sampler` and `scheduler`, Forge's labels): all 25 samplers and all 16 schedule types, Automatic resolving as Forge's `get_sigmas` does (the sampler's own option, else Uniform, and LCM's own schedule), DPM2, DPM2 a and DPM++ 3M SDE taking one more step and dropping the next-to-last sigma, DPM fast and DPM adaptive over the model's sigma range in text to image and the schedule tail's in img2img. Refused: LCM under a named schedule, and the four timestep samplers (DDIM, DDIM CFG++, PLMS, UniPC) with an image or hires fix, which the built timestep samplers cannot start part-way. The table is graded by `codex/test/apps/diffusion-sampler-table` against `build/sampler-table-oracle.py`, which reads Forge's sampler and scheduler tables from its source: 25 of 25 rows agree, and a dispatch that discards no sigma differs on 3. End to end through `codex_image` (2026-09-29, realisticVision, 512 x 512, 12 steps, CFG 7, one seed): DPM++ 2M, Euler a, DPM++ 3M SDE, DDIM, UniPC, DDIM CFG++, Restart, DDPM, Euler under Align Your Steps, Euler and DPM2 under Beta, and img2img with DPM++ 2M SDE each gave a correct picture by eye. DPM2 a and DPM fast match Forge's samplers within 1.7e-6 on the analytic denoiser and the Beta schedule matches Forge's to 1e-6. Against Forge at those settings (2026-09-30, the storage prompt, seed 7201, Automatic): DPM fast 3.78 of 255 from Forge's picture, which is as noisy as ours, where CFG 7 to 7.07 moves Forge's own 10.06; Euler a 4.71 (10.75); DPM2 a 49.83, two coherent pictures of different composition, where CFG 7 to 7.07 moves Forge's own 48.10, so DPM2 a is chaotic there, not wrong.

## Schedulers

16 (2026-09-29), `modules/sd_schedulers.py:212-227`: Automatic, Uniform,
Karras, Exponential, Polyexponential, SGM Uniform, KL Optimal, Align Your
Steps, Simple, Normal, DDIM, Beta, Turbo, Align Your Steps GITS, Align Your
Steps 11, Align Your Steps 32. **Built:** every one but Automatic, each within 6.4e-7 relative of Forge's `modules/sd_schedulers.py`
(the same test and oracle; Align Your Steps on SDXL's and SD1.5's tables, at 6
steps, at 8, which interpolates, and at the table's own 11).

## Guidance and seeds

| Item | Evidence |
|---|---|
| CFG, and Distilled CFG for Flux | `modules/ui.py:322`; `flux.py:47` |
| Seed, variation seed and strength, resize seed from width/height | `modules/processing_scripts/seed.py:30-48` |
| Refiner (checkpoint, "Switch at"); only SDXL-base-typed checkpoints load | `modules/processing_scripts/refiner.py:23-28` |

**The refiner, built for SDXL** (`Img2Img` `img2img-refined`; `codex_image`'s `refiner` and `refiner_switch_at`, default 0.8): text to image, img2img and inpaint, hires fix (in the hires pass only, Forge's default `hires_fix_refiner_pass`), and LoRAs, merged into the refiner at the switch before it conditions (`sd_samplers_common.py:201-204`); the PNG carries `Refiner` and `Refiner switch at` when the switch happened. With a hires checkpoint it runs in the hires pass over that checkpoint and reloads only when it is neither the hires checkpoint nor the base, as Forge's checkpoint_change compares with the configured (base) checkpoint: a refiner that is the base writes its keys and changes nothing, 4.62 of 255 from Forge's picture, the same as without it (2026-09-29; with two SDXL checkpoints installed, the reloading case has no Forge comparison). Against Forge (2026-09-29, dreamshaperXL to cyberrealisticXL at 0.5, the storage prompt, seed 7201, DPM++ SDE, 6 steps, CFG 2): hires 512 to 1024 px (latent, denoise 0.5) 4.81 of 255 where ours without the refiner is 4.55 and the refiner moves Forge's picture 10.65, through R-ESRGAN 4x+ 7.66 (7.35; 10.51); img2img of a 1024 px picture at denoise 0.5 3.04 (2.93; 10.35); 512 px with pixel-art-xl at 1 2.90 (2.70; 12.74). Against Forge (2026-09-29, dreamshaperXL to cyberrealisticXL at 0.5, DPM++ 2M, 12 steps, CFG 2, 1024 px, the storage prompt, seed 7201): ours is 1.38 of 255 from Forge's picture with the refiner and 1.42 without it, while the refiner moves Forge's own picture by 15.00; 94 s cold. Forge switches inside the sampler, not between steps (`modules/sd_samplers_common.py:163-207`, `apply_refiner`, called at the top of every CFGDenoiser forward, `sd_samplers_cfg_denoiser.py:171`): with step the count of model calls so far in this pass (0 at the first, `:54`, `+= 1` after each, `:222`) and total_steps the pass's steps, doubled for the samplers the table marks second_order (DPM++ SDE, DPM++ 2S a, Heun, DPM2, DPM2 a, Restart; `sd_samplers_common.py:17-21`), the first call with step / total_steps >= switch_at loads the refiner checkpoint in place of the base, recomputes the conditioning with its text encoders (`p.setup_conds`) and re-activates the prompt's LoRAs on it, and every later call of the pass runs on the refiner; a multistep or second-order sampler keeps its state across the switch. With hires fix on, the refiner applies to the second pass only by default (`hires_fix_refiner_pass`, "second pass"). Decoding runs on the model loaded at the end, the refiner. Two SDXL checkpoints do not fit the card together, so a refiner request runs as Flux's do: it frees the resident model, loads the base, and at the switch frees it and loads the refiner; the denoiser holds its call count and the current model configuration in one-element lists.

**Built** (`Diffusion chapter Noise`, `noise-torch`): Forge's default noise, torch.randn on the CUDA device from a generator seeded with the seed (`modules/rng.py`, randn_source GPU): Philox4x32-10, curand's Box-Muller and torch 2.3's grid-stride layout over this card's 204 blocks of 256 threads, for the start of text to image, of img2img and of the hires pass, and every later draw in call order, as `ImageRNG.next` gives them. Graded by `codex/test/apps/diffusion-torch-noise` against `build/torch-noise-oracle.py` (torch on the RTX 4060 Ti, 2026-09-29): 512 px, 1024 px, 1536 x 640 and Flux's 16-channel 1024 px latents, draws 0 to 2, every value within 1e-5; a draw read one late and Forge's own `rng_philox` (the NV source, one value per thread) both go red, the latter on exactly the 13,312 values of a 1024 px latent past the card's 52,224 threads. **Built** (`Diffusion chapter BrownianTree`): DPM++ SDE, 2M SDE, 2M SDE Heun and 3M SDE (Forge's `brownian_noise` samplers) take their noise from torchsde 0.2's Brownian tree as Forge builds it (`modules/sd_samplers_common.py:340-348`, k-diffusion's `BrownianTreeNoiseSampler`; `system/python/Lib/site-packages/torchsde/_brownian/brownian_interval.py`, `derived.py:122-165`): one tree per image over the whole schedule (for img2img and the hires pass too, not the tail they sample), tol 1e-6, pool 24, halfway splits, every time rounded as Python's `round(x, 6)` rounds the exact binary value, the seeds from numpy's `SeedSequence(seed, spawn_key = (key, depth), pool_size = 24)` (`Diffusion chapter SeedSequence`, graded by `codex/test/apps/diffusion-seedsequence`), each normal `noise-torch`'s draw 0, and every child W in f32 as the tensors are. DPM++ SDE asks at sigma_fn (t_fn sigma) and at its log-sigma midpoint, the multistep samplers at the plain sigmas. Graded by `codex/test/apps/diffusion-brownian` against `build/brownian-oracle.py` (the calls real `sample_dpmpp_sde` and `sample_dpmpp_2m_sde` runs make on the RTX 4060 Ti, 2026-09-29): over Forge's own f32 sigmas all 15 draws within 1e-5, the 5 DPM++ 2M SDE query times bit for bit, 3 of 10 DPM++ SDE times bit for bit and 7 one f32 ulp off (CUDA's logf and expf are not correctly rounded), which round to the same 6-decimal times here; a draw checked against another call's values is 256 of 256 off. Not taken (root, 2026-09-29): our Karras and Exponential sigmas are Forge's to 1 to 5 f32 ulps, not bit for bit. Forge computes Karras on the CPU through torch's vectorized f32 pow (Sleef) and Exponential on CUDA through libdevice expf, so bit-exact sigmas would emulate both libraries' rounding; the difference moves the tree's 6-decimal times, over our schedules 834 of 3840 values by more than 1e-3 and none by more than 1e-2 (the same test, 2026-09-29), below DPM++ SDE's 1.30 picture gap. **Built** (`Diffusion chapter Noise`, `noise-torch-cpu-into`; `Diffusion chapter Img2Img`): img2img's posterior eps is torch.randn on the CPU from the global generator (`backend/nn/vae.py:27-29`), which nothing seeds before the encode, so its seed is the one `modules/rng.py:21-23` last gave `torch.manual_seed`, (seed + 100000) % 65536: a request's own from its second run on in one Forge process, the first run's depending on what the process drew before. torch's CPU normal is MT19937 with `normal_fill`'s blocks of 16 (the last 16 drawn again when n is not a multiple of 16), graded by `codex/test/apps/diffusion-torch-cpu-noise` against `build/torch-cpu-noise-oracle.py` (512 px and 1024 px latents, 100 values, 256 values: every value within 1e-5, a seed one off is every value off, 2026-09-29). Inpaint's blend draws are `torch.randn_like` of the init latent from the global CUDA generator, the same seed, one draw per model call (`modules/sd_samplers_cfg_denoiser.py:178-181`), which is `noise-torch`'s draw k. **Compared with Forge** (2026-09-29, Forge f2.0.1v1.10.1-previous-635 on this box through its API, dreamshaperXL lightning, the first image's storage prompt, seed 7201, CFG 2, 1024 px; per channel of 255, the mean absolute difference, the share of pixels within 8, and the mean after a 32 px blur): DPM++ SDE, Karras, 6 steps 1.30, 97.0%, 0.35 (before the Brownian tree 31.6 and 10.1%); img2img at denoise 0.5 from Forge's picture 1.87, 92.8%, 0.24, and inpaint (a 448 px square, blur 4) 0.27, 99.3%, 0.04, both against Forge's second run in its process, which repeats its first byte for byte; Euler a, 12 steps 2.72, 88.8%, 0.43; DPM fast, 12 steps 4.06, 78.9%, 0.62; DPM2 a, 12 steps 9.24, 58.8%, 1.21, the same composition. DPM2 a's larger gap is the sampler's own sensitivity, not a defect: Forge's DPM2 a moves 19.13 (49.8%, 6.86) between CFG 2 and CFG 2.02 at the same seed, where its Euler a moves 4.10 and its DPM2 3.80, and ours moves 19.14; against Forge at CFG 2 ours is 2.28 (Euler a), 2.19 (DPM2) and 9.72 (DPM2 a), about 0.55 of each sampler's own move in all three (both sides on this box, 2026-09-29, the settings above, 12 steps, Automatic). A gap against Forge is read against Forge's own move under a small input change before it is chased. The UNet runs at the nearest discrete timestep, as Forge's KModel does (`backend/modules/k_model.py:35`, `k_prediction.py:148-151`), not k-diffusion's interpolated one; the change moved DPM++ SDE from 1.63 to 1.30, DPM2 a from 10.66 to 9.24 and Euler a from 2.13 to 2.72, so one seed does not rank small differences. The side-by-sides are in `build-output/diffusion-forge-compare/`.

## Hires fix

| Option | Evidence (`modules/ui.py`) |
|---|---|
| Upscaler: six latent modes and every installed upscaler | `:337`; `modules/shared.py:56-62` |
| Hires steps 0 to 150; denoising 0 to 1, default 0.7 | `:338-339` |
| Upscale by 1 to 4, or resize to a width and height | `:342-343` |
| Hires CFG and Hires Distilled CFG | `:347-348` |
| Hires checkpoint and Hires VAE / text encoder | `:351,370`; **checkpoint built** (`codex_image`'s `hr_checkpoint`; `Img2Img` `img2img-hr-swapped`, as `processing.py:1400-1428`: the first pass on the base, decoded through the base's VAE for an upscaler; the base freed, the hires checkpoint loaded, the hires prompt conditioned on its text encoders, the hires pass, the encode and the final decode on it; the latent passed between them unscaled; the request's LoRAs merged into each checkpoint as it loads). Against Forge (2026-09-29, dreamshaperXL to cyberrealisticXL, the storage prompt, seed 7201, 512 to 1024 px, DPM++ SDE, Karras, 6 steps, CFG 2, hires denoise 0.5 and CFG 2): latent 4.62 of 255 where the checkpoint moves Forge's own picture 15.25 and ours without it is 4.55 away; R-ESRGAN 4x+ 5.80 where the checkpoint moves Forge's 9.15 and ours without it is 7.35 away; latent with pixel-art-xl at 1, 2.46 where the LoRA moves Forge's picture 24.14. **Hires VAE / text encoder built** (`codex_image`'s `hr_modules`, file names from `models/VAE` and `models/text_encoder`; `Img2Img` `i2-with-vae`): as `backend/loader.py:205-224` does, a file holding `decoder.conv_in.weight` replaces the hires model's VAE, which the request then reloads (`img2img-hr-swapped`, the base as the hires checkpoint when none is named), and the last such file wins; a CLIP-L or T5 file changes nothing on SDXL or SD1.5, whose clip keys it lands beside are filtered out, and the pixels are ours without it byte for byte; a VAE that is not a 4-channel SD one (Flux's `ae`) is refused; the PNG carries "Hires Module N". Against Forge (2026-09-30, cyberrealisticXL's VAE as `models/VAE/cyberrealisticXL_v4-vae.safetensors` over dreamshaperXL, the hires checkpoint row's settings): 4.87 of 255 where ours without it is 4.55 away, and the module moves Forge's picture 10.29 and ours 10.20. The base pass's "VAE / Text Encoder" selector is not built |
| Hires sampler and scheduler | `:375-376`; **built** (`codex_image`'s `hr_sampler`, `hr_scheduler`; `i2-hr-base`): ours against Forge 3.91 of 255 with Euler a on Karras over a DPM++ SDE first pass (512 to 1024 px, latent, denoise 0.5), 4.55 with the first pass's, where the option moves Forge's own picture 12.20 (2026-09-29) |
| Hires prompt and negative prompt | `:380`; **built** (`codex_image`'s `hr_prompt`, `hr_negative`; the hires infotext keys in Forge's order and form, `DiffusionDriver` `dr-params-hires`): with ", heavy snowfall" added for the hires pass, ours is 4.40 of 255 from Forge's picture where our picture without it is 7.09 away, and the hires prompt moves ours 6.01 and Forge's 5.96 (2026-09-29, the hires sampler row's settings) |

**Model upscalers (blu).** Built: `Diffusion chapter PthFile` reads a torch.save checkpoint (ZIP central directory, a pickle interpreter for the opcodes and the three globals a state dict uses, each tensor's file offset), graded by `codex/test/apps/diffusion-pth` against `build/pth-oracle.py`: every tensor of both installed RealESRGAN checkpoints (702 and 192) by name, shape and first and last elements, bit for bit (2026-09-29); and `Diffusion chapter Esrgan`, RRDBNet on the GPU (f16 conv weights through VaeDecoder's convs, each dense block's channels in one buffer, `kernel-lrelu-into` in `UpscaleKernels`), graded by `codex/test/apps/diffusion-esrgan` against `build/esrgan-oracle.py`: spandrel's x4plus on a 3 x 32 x 32 tile, all 49,152 values within 2^-8 of Forge's f32 and of its f16 output (the anime 6B's weights: 741; 2026-09-29); and `esr-upscale`, Forge's `upscale_with_model` around it (`split_grid`, BGR tiles of byte / 255, the output rounded through f16 to bytes, `combine_grid`'s masks and PIL's blend), graded by the same test against Forge's own Extras upscale of a 256 x 192 picture (`build/esrgan-picture-oracle.py`, tile 192, overlap 8): 2,252,056 of 2,359,296 bytes equal and every byte within 1 (without the overlap's blend the largest difference is 188; 2026-09-29); and `Diffusion chapter Resize`, Pillow 9.5's LANCZOS resize (`Resample.c`: 22-bit coefficients, the horizontal pass first over the rows the vertical pass reads), graded by `codex/test/apps/diffusion-resize` against `build/resize-oracle.py`: up by 1.5, down, height only and width only, every byte Pillow's (2026-09-29). **Hires with an upscaler built** (`i2-hires-upscaled` in `Diffusion chapter Img2Img`; `codex_image`'s `hr_upscaler`: Latent, R-ESRGAN 4x+, R-ESRGAN 4x+ Anime6B, SwinIR_4x and DAT_x4 when installed), as `modules/processing.py`'s `sample_hr_pass` takes the non-latent branch: the first pass decoded and `255 * clamp ((x + 1) / 2)` truncated to bytes; `resize_image`, for a scale above 1 `Upscaler.upscale` (`modules/upscaler.py`: the model once, then LANCZOS to (`int (W scale) // 8 * 8`, the same for H) when that differs), then LANCZOS to the hires size when that differs; the picture encoded, the posterior draw as img2img's, the seed's draw 0 at the hires size, the hires schedule. Against Forge (2026-09-29, the storage prompt, seed 7201, 512 px, DPM++ SDE, Karras, 6 steps, CFG 2, hires 2x to 1024 px with R-ESRGAN 4x+ at denoise 0.5 and hires CFG 1, the API's default): a mean absolute difference of 6.92 of 255, 68.1% of pixels within 8, 1.35 after a 32 px blur, the same picture (`build-output/diffusion-forge-compare/hires-esrgan-codex-left-forge-right.png`). **SwinIR built** (`Diffusion chapter SwinIR`; `SwinIR_4x`, Forge's name for a local file, through `hr_upscaler`): spandrel's SwinIR large (9 groups of 6 blocks, dim 240, 8 heads, window 8, 3conv, nearest+conv x4), which Forge runs in f32 (spandrel reports no half support) through `upscale_2` and `tiled_upscale_2` (`extensions-builtin/SwinIR`, `SWIN_tile` 192, `SWIN_tile_overlap` 8: patches summed and divided by their count in f32, not blended in PIL); here the linears and convs take f16 operands with f32 accumulation, and the window attention (relative position bias, the rolled windows' mask, recomputed for any size) is one f32 kernel, `kernel-window-attention` in `UpscaleKernels`, which DAT shares. Rounding every linear and conv operand of spandrel's own network to f16 moves a 64 px tile's bytes by at most 3 (92.1% equal, 99.95% within 1; a CPU probe, 2026-09-29). Graded by `codex/test/apps/diffusion-swinir` against `build/swinir-oracle.py` (spandrel on the CUDA device, and Forge's own `upscale_2` compiled from `modules/upscaler_utils.py`'s text): a 3 x 40 x 32 tile, 61,435 of 61,440 values within 2^-8 of Forge's f32 and all within 2^-6 (no shifted windows: 5,157); a 256 x 192 picture, 2,290,563 of 2,359,296 bytes equal to Forge's, all but 2 within 1, the largest difference 2 (at tile 96: 989,186, the largest 133). End to end (2026-09-29, dreamshaperXL, 512 px, DPM++ SDE, Karras, 6 steps, CFG 2, hires 2x through SwinIR_4x at denoise 0.5): a correct 1024 px picture by eye in 56 s cold. The test's model root is `build-output/diffusion-swinir`, a junction to Forge's `models\SwinIR`. **DAT built** (`Diffusion chapter Dat`; `DAT_x4`, Forge's name for a local file, through `hr_upscaler`): spandrel's DAT (6 groups of 6 blocks, dim 180, 6 heads, windows 8 x 32 and 32 x 8, ffn 720, pixelshuffle x4), which Forge runs in f32 through `upscale_with_model` at `DAT_tile` 192 and `DAT_tile_overlap` 8, the same PIL tiling as the ESRGAN models (`esr-upscale-by`, the output bytes without the f16 step); qkv, proj, fc1, fc2 and the full convolutions take f16 operands, everything else (the dynamic position bias, the depthwise convolutions, BatchNorm, the interaction maps, the channel attention's Gram matrix) runs in f32 or f64 in `UpscaleKernels`. Rounding every linear and conv operand of spandrel's network to f16 moves a 64 px tile's bytes by at most 1 (a CPU probe, 2026-09-29). Graded by `codex/test/apps/diffusion-dat` against `build/dat-oracle.py` (spandrel on the CUDA device, and Forge's own `upscale_with_model`, `split_grid` and `combine_grid` compiled from their text): a 3 x 64 x 32 tile, all 98,304 values within 2^-8 of Forge's f32 (the window sides swapped: 51,231); a 256 x 192 picture, 2,286,409 of 2,359,296 bytes equal to Forge's and every byte within 1 (at tile 96: the largest difference 75). End to end (the SwinIR settings above): a correct 1024 px picture by eye in 81 s cold. The test's model root is `build-output/diffusion-dat`, a junction to Forge's `models\DAT`. The GPU bridge holds at most 4,096 buffers (codex-vm's `GPU_BUF_MAX`, and the guest pool's tables in `GpuBridge`), of which a resident SDXL model holds most, so SwinIR and DAT keep every f32 tensor but a convolution's bias in one packed buffer (`SwWeights`, `sw-arg`); DAT uploaded tensor by tensor ran in its test and refused inside the resident driver. Not built: HAT, ScuNET and SD upscale, none installed.

## Img2img and inpaint

| Item | Evidence |
|---|---|
| Modes: img2img, Sketch, Inpaint, Inpaint sketch, Inpaint upload, Batch (upload or directory) | `modules/ui.py:587-612` |
| Resize: just resize, crop and resize, resize and fill, just resize (latent upscale) | `ui.py:655` |
| Mask mode: inpaint masked or not masked | `ui.py:749` |
| Masked content: fill, original, latent noise, latent nothing | `ui.py:752` |
| Inpaint area: whole picture or only masked, with padding | `ui.py:756-759` |
| Soft inpainting | `extensions-builtin/soft-inpainting/scripts/soft_inpainting.py:502`; **built** (`Diffusion chapter SoftInpaint`; `codex_image`'s `soft_inpainting` and its six settings, `soft_power` to `soft_contrast`; with `inpaint_full_res` the overlay mask is uncropped into the picture, black outside the box): the unrounded f16 mask, the blend before and after each model call, no final blend, the adaptive overlay mask. Graded by `codex/test/apps/diffusion-soft-inpaint` against `build/soft-inpaint-oracle.py` (Forge's own functions): the modified mask and `latent_blend` within 1e-6, both histogram filter passes within 1e-6, and both settings' overlay masks byte-exact to Forge's at 128 x 96; a blur at radius 3 and the second filter pass at the first pass's percentiles each go red. Against Forge (2026-09-29, dreamshaperXL, a 1024 px picture, an ellipse mask, blur 16, denoise 0.75, DPM++ SDE, 6 steps, CFG 2, Forge's second run): the defaults 0.53 of 255 and a second setting 0.51, where soft inpainting moves Forge's picture 3.17 and 3.28 and our plain inpaint is 0.55 away; only masked (512 px, padding 32) 0.70, where soft moves Forge's 2.86 and our plain only-masked is 0.62 away |
| Fooocus inpaint (patch and inpaint head) | `extensions-builtin/sd_forge_fooocus_inpaint/scripts/forge_fooocus_inpaint.py:28,67` |
| Outpainting | `scripts/outpainting_mk_2.py`, `scripts/poor_mans_outpainting.py`; **poor man's built** (`Img2Img` `i2-outpaint`; `codex_image`'s `outpaint_pixels`, `outpaint_mask_blur`, `outpaint_direction`, `inpainting_fill` defaulting to fill; an image alone; the PNG the canvas's size): the canvas, the two rectangle masks, `split_grid` tiles at the request size, each border tile inpainted at seed plus its index with the previous tile's posterior seed, `combine_grid`. Against Forge (2026-09-29, a 512 px picture, 512 px tiles, 128 px, blur 4, fill, denoise 0.8, DPM++ SDE, 6 steps, CFG 2, Forge's second run): all four sides, 768 x 768, 2.00 of 255; right only, 640 x 512, 1.21; Forge's own first and second runs differ by 0.19 and 0.18; the same composition by eye. Outpainting mk2: its matched noise is built (`Diffusion chapter MatchedNoise` over `Diffusion chapter Pcg64`, numpy's `default_rng`; graded by `codex/test/apps/diffusion-matched-noise` against `build/matched-noise-oracle.py`, Forge's own `get_matched_noise`: two cases within 1e-6 and every byte equal, a noise_q sabotage 9,320 bytes off, 2026-09-29), and the flow is built (`Img2Img` `i2-mk2`; `codex_image`'s `outpaint_mode` mk2, `outpaint_noise_q`, `outpaint_color_variation`, mask blur default 8; pictures and pixels in multiples of 64, each pass at most 1024 x 1024): the sides in turn, each canvas filled by the matched noise and its edge region inpainted at its own size with the mask blurred 4 blur per axis, masked content original, the same seed. Against Forge (the poor man's settings above, blur 8, noise_q 1, color variation 0.05, Forge's second run): right only, one pass, 0.79 of 255; left, up and down alone 1.30, 0.69 and 0.52; all four sides, four passes, 6.27, where Forge's own first and second runs differ by 3.73 through the first pass's posterior draw alone, therefore the four-pass gap is each pass's small difference carried through the chain, the same composition by eye |

## Conditioning and post-processing

| Item | Evidence |
|---|---|
| ControlNet preprocessors: canny, depth (midas, leres, zoe, depth_anything v1 and v2, marigold), hed, pidinet, lineart and lineart anime, manga_line, mlsd, openpose, densepose, normalbae, oneformer and uniformer segmentation, shuffle, teed, tile (with colorfix), inpaint / inpaint_only / lama, reference, recolor, revision | `extensions-builtin/forge_legacy_preprocessors/annotator/`; `extensions-builtin/forge_preprocessor_*/scripts/` |
| ControlLLLite | `extensions-builtin/sd_forge_controlllite` |
| IP-Adapter, FaceID (InsightFace), InstantID | `extensions-builtin/sd_forge_ipadapter/scripts/forge_ipadapter.py:56,88-106` |
| Upscalers: ESRGAN, RealESRGAN, DAT, HAT, SwinIR, ScuNET, SD upscale | `modules/esrgan_model.py`, `realesrgan_model.py`, `dat_model.py`, `hat_model.py`; `extensions-builtin/SwinIR`, `ScuNET`; `scripts/sd_upscale.py` |
| Built-in extras present as folders and not read: dynamic thresholding, Kohya HRFix, MultiDiffusion, StyleAlign, latent modifier, NeverOOM. FreeU, SAG and PAG: "Built-in extras" below | `extensions-builtin/sd_forge_*` |

Control models load through `modules_forge/shared.py:52-61`, which asks each
registered patcher to build from the state dict:

| Accepted | Evidence |
|---|---|
| ControlNet, UNet-shaped only: SD1.5, SD2 and SDXL, whichever `detect_unet_config` types; LDM keys, `control_model.`-prefixed `.pth` keys, or diffusers keys converted to LDM; `_shuffle` files get global average pooling | `modules_forge/supported_controlnet.py:41-143`; `backend/nn/cnets/cldm.py:7`; `repositories/huggingface_guess/huggingface_guess/detection.py:277-290` |
| Control-LoRA (`lora_controlnet` key) | `supported_controlnet.py:42-43`; `backend/patcher/controlnet.py:420` |
| T2I-Adapter: full (`conv_in`, XL when 256 or 768 input channels) and light (`body.0.in_conv`), original or diffusers keys | `backend/patcher/controlnet.py:548-580` |
| ControlLLLite, IP-Adapter, Fooocus inpaint, each a registered patcher | `extensions-builtin/sd_forge_controlllite/scripts/forge_controllllite.py:38`; `sd_forge_ipadapter/scripts/forge_ipadapter.py:163`; `sd_forge_fooocus_inpaint/scripts/forge_fooocus_inpaint.py:132` |

Flux ControlNets are not accepted: they are transformer-shaped, no control
file names Flux or its blocks, and a state dict matching none of the paths
above returns None from every builder. `models/ControlNet` is empty
(2026-09-29).

## Built-in extras

FreeU, SAG, PAG and dynamic thresholding run on SDXL and SD1.5 through `codex_image` (`freeu`, `freeu_b1`..`freeu_s2`; `sag`, `sag_scale`, `sag_blur_sigma`, `sag_threshold`; `pag_scale`; `dynthres` and its eleven `dynthres_*` settings) and write Forge's infotext keys, in its script order (dynamic thresholding, FreeU, SAG, PAG). Dynamic thresholding replaces the CFG combine (`ti-combine`, Forge's `sampler_cfg_function`, `DynThresh.codex` over `ExtrasKernels`), graded case by case by `diffusion-dynthres` against mcmonkey's own `dynthres_core.py`. FreeU's and SAG's work runs as kernels in `ExtrasKernels`. The UNet takes them through `UNetOps` hooks (`out-patch`, `self-v`, `probs-at` / `probs`); SAG and PAG run after guidance in `ti-post` (`Txt2Img.codex`). Graded: `sdxl-unet-freeu`, `sdxl-unet-pag`, `sdxl-unet-sag`, `diffusion-sag-blur` against the extensions' own code in Forge's UNet, and `sdxl-guidance-extras`, one guided call through Forge's `sampling_function_inner`.

**FreeU's `freeu_start` / `freeu_end` window is Forge's `denoiser_callback`:** FreeU runs on a model call where `sampling_step / (steps - 1)` lies within the window, and `sampling_step` is the `i` of the sampler's last callback before that call (`modules/sd_samplers_common.py:268`), reset to 0 per `launch_sampling`, whose `steps` is `t_enc + 1` under img2img and the hires pass. `ti-seen-step` (`Txt2Img.codex`) is that step per sampler as a closed form in the call index, and `ti-launch` / `ti-tick` carry `shared.state` per request in `tx-step`. Graded by `diffusion-freeu-steps` against `build/freeu-step-oracle.py`, which runs Forge's 25 samplers and the extension's own callback: 123 of 123 sampler and step-count lines, with DPM adaptive on the recorded draws; and by `sdxl-guidance-extras`, whose gated-off call matches no extras and gated-on call matches FreeU. The window values travel as IEEE bits (`dr-bits`), because `dr-real`'s digit sum reads `0.3` as `0.30000000000000004` and flips the gate at step 3 of 10. With the default window DPM adaptive turns FreeU off once its attempts pass `steps - 1`, as Forge does. At one step Forge's division raises and FreeU keeps its last state; here it runs.

| Refused | Why |
|---|---|
| Any of the four with Flux | FreeU checks `model_channels` and skips Flux; SAG and PAG patch UNet blocks a transformer lacks |
| Any with a refiner | Forge's refiner swaps the patched UNet for an unpatched one; not mirrored |
| `sag_blur_sigma` 0 | the Gaussian divides by sigma |

**Forge's PAG writes its patch into the sampler's model options, and it persists.** `forge_perturbed_attention.py:39` calls `set_model_options_patch_replace(args["model_options"], ...)`, which assigns the new transformer options back into the dict it is given (`backend/patcher/base.py:33`), and `sampling_function` passes the patcher's own `model_options` (`backend/sampling/sampling_function.py:334`). After the first PAG call every later cond and uncond call runs with PAG's middle attention, so PAG's own term is 0 from step 2 on. Measured 2026-09-30 by running the lifted post-CFG function once: the options held `patches_replace attn1 (middle, 0)` afterwards. Root's ruling (2026-09-30): PAG runs as intended here; step 1 is graded against Forge (`sdxl-guidance-extras`), three Euler steps against the paper's and diffusers' per-step form (`sdxl-pag-euler`, which also shows the result 27% away from Forge's persisted PAG).

Forge's SAG key `("middle", 0, 0)` is looked up before PAG's `("middle", 0)` (`backend/nn/unet.py:218-225`), so with both on, the middle block's first self-attention is SAG's (normal on a cond call) rather than PAG's.

## What is installed

`webui/models`, 2026-09-29. What the replacement must load first:

| Folder | Files |
|---|---|
| Stable-diffusion | Flux schnell fp8 e4m3fn (11.3 GB); a 50/50 merge of that with an SD checkpoint (11.3 GB, not known to load); cyberrealisticXL v4 and dreamshaperXL lightning DPMSDE (SDXL, 6.6 GB each); realisticVision v6.0 B1 v2.0 no-VAE (2.0 GB) and v5.1 hyper VAE (4.1 GB) (SD1.5) |
| VAE | Flux `ae.safetensors` (320 MB); `clip_l.safetensors` (235 MB, a text encoder the combined selector accepts) |
| text_encoder | T5-XXL fp16 (9.3 GB) and fp8 e4m3fn (4.7 GB) |
| Lora | 9 files, 81 MB to 870 MB, mostly SDXL; one is Z-Image, which no engine here loads |
| Upscalers | DAT x4, RealESRGAN x4plus and x4plus anime 6B, SwinIR 4x |
| VAE-approx | SD and SDXL approximate decoders |
| Empty | ControlNet, ControlNetPreprocessor, Codeformer, GFPGAN, deepbooru, diffusers, hypernetworks, svd, z123 |

No ControlNet or IP-Adapter model is installed, and `extensions/` holds no
user extension.
