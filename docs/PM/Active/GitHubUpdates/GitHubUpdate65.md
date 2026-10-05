# GitHub Update 65

Pushed from main 32453 plus its release documents. The release seed is
`B3256BF8B4CC8327` (`TechnicalDetails.md` carries the full digests). Numbers in
parentheses are main changelists.

## Diffusion: our own image pipeline on the GPU

- **The first image is made end to end in Codex**: dreamshaperXL at
  1024 x 1024 from a prompt, through a CLIP BPE tokenizer built from the real
  vocabulary, both SDXL text encoders, the UNet, the Euler and DPM++ SDE
  samplers and the VAE decoder, every layer a PTX kernel the Codex PTX plug
  emits and codex-vm launches on the RTX card. The weights never enter guest
  memory: the guest reads a checkpoint's header and uploads each tensor from
  its file range straight to a device buffer.
- **Graded against Forge, not against itself**: the tokenizer's ids, the text
  encoders within the deviation of the same model in f16, every one of the
  UNet's 20 blocks within 1% of its rms against Forge's own UNet in f32, the
  samplers within 5e-7 of k-diffusion fed the same draws, and the VAE decode
  within 2 on every byte of Forge's f32 decode, each with sabotage arms.
- **img2img, inpaint, hires fix and the SD1.5 UNet** are built and graded
  against Forge's own code paths.
- **From 354 s to about 29 s for a 1024 px image**, by resident weights, one
  launch for all attention heads, fast exp on the device, pooled temporaries
  and asynchronous launches.
- **`codex_image`**, a tool server with its authority fixed at launch (models
  read-only, one output directory, no caller path), keeps one guest resident
  across calls. `docs/Designs/Active/Apps/Diffusion.md` carries the stages
  and every measurement.
- **Flux runs end to end**: its transformer, 16-channel VAE, T5 encoder with
  Forge's prompt chunking and emphasis, CLIP-L pooled conditioning and flow
  sampler (reek 30859, 30879, 30917, 30950; blu 30841, 30868, 30889, 30943),
  served through `codex_image` (reek 31057), with LoRA merged onto the fp8
  transformer bit-exact against torch (reek 31677, 31686, 31701).
- **Forge's sampler and option surface**: all 25 samplers and 16 schedule types
  under Forge's labels (blu 30808, val 30758), torchsde's Brownian tree for the
  SDE family (blu 31146, 31238), seeds from torch's CUDA and CPU generators
  (blu 31127, 31251), hires fix with its own prompt, sampler, checkpoint and
  refiner plus the ESRGAN, SwinIR and DAT upscalers (blu 31343 to 31807),
  img2img resize and inpaint mask modes (reek 31502 to 31671), soft inpainting
  and outpainting (blu 31763 to 31787), variation seeds (reek 31471, 31494),
  clip skip (blu 31992), and FreeU, SAG, PAG and dynamic thresholding on the GPU
  (reek 31721 to 32089).

## Diffusion in a browser tab (WebGPU)

- **The first SD1.5 image made in a browser tab** (red 31951), within a mean of
  0.52 of 255 of `codex_image`'s render: the tokenizer, CLIP-L, the UNet and
  the VAE compiled from Codex to WebGPU kernels, each graded against Forge
  (red 31063 to 31892).
- **36.5 s from click to image** after register-tiled linear and convolution
  kernels, workgroup reductions and tiled attention (red 32302, 32309, 32368,
  32387), with the weights packed f16, 2.14 GB instead of 4.13 (red 32327). The
  demo page has a checkpoint list, a live console and job history (red 32103
  to 32226, root 32185).

## The compiler

- **Closures carry every value past six** (COMPILER-106, val 30603): a closure
  holding captures plus arguments past the six x86-64 argument registers (past
  eight on arm64 and riscv) dropped the surplus since the first closure codegen;
  five parser continuations at seven values passed only by frame layout.
  Graded over captures 0-16 by arity 1-12 on all three targets.
- **Large module-level integer tables compile** (COMPILER-107, val 30619): a
  `List Integer` literal past about 6,000 elements crashed the compiler; tables
  are now written straight to the data buffer, to 65,504 elements.
- **A record-field call with seven or more arguments keeps its first**
  (COMPILER-108, red 31437).

## Games

- **Every game's rules are written from a canonical source and graded by
  board-state class**: sudoku through hexwar (red 30846 to 31015), Mahjong,
  Battleship, Liar's Dice, Yahtzee, Monopoly and Risk (blu 30923 to 31086), and
  the card games to pagat's rules (val 30778 to 31108).
- **Magic** gains Commander (val 31511 to 31590), the draft and gauntlet, layered
  power and toughness (val 31611 to 31705), and 110 of 404 cards modelled in its
  first nine sets (val 32306).

## Desk and ModBuilder

- **DeskScheduler stages 1 and 2**: bounded input collection, render
  checkpoints and phased presentation, graded in both beds (fester 31623 to
  32370). Physical acceptance is not claimed.
- **MB-7**: a forwarding WinHTTP DLL proven by an export oracle, and the Codex
  Mono loader with SDK-free packaging (fester 31004, 31079).

## The on-disk test runner (Build.md Phase B)

- **A booted guest grades the whole test corpus** (red 30543): each test is
  bundled with its cited chapters on the host, compiled in process on the
  guest, and every diagnostic code the `.failing` sidecar records is checked at
  its line and column. 984 of 984 over `codex/test` and `codex/test/errors`.

## Pages

- **gpushow's live-compile block publishes** (GPUSHOW-3, Damian's ruling,
  30196): every one of the 39 pages compiles its own kernel with the
  in-browser compiler, lowers it to WGSL, and compares against the shipped
  shader, editable with Revert.
- **Fishtank ships its head module** (reek 30043), and the **ModBuilder page's
  Unity plug decodes UTF-8 IR** (MB-14, reek 30086, root 30111).

## Release proof

The full gate ran every phase against the release seed, and five of its reds
were fixed on the way rather than waived: the gate's clean step deleted five
of the six model-root links the GPU tests read (32407), a rename missed three
callers in the Explorer app (32429), a test reaching the GPU bridge through a
cited chapter was declared bare-metal for the wasm arm (32432, plugs 2.109),
the wasm end-to-end harness decoded wasmtime's output as ibm437 when launched
in the background (32438), and the test chapters' run phase met the GPU
timing below. After the fixes: 244 declared refusals, every test chapter
compiling and running against its `.expected`, cross-architecture smoke, 56
generators with no drift, deck headroom (tightest margin 2.13), 318 of 319
apps compiling clean with the one baseline failure, 1,121 hosted wasm programs
passing with 6 known reds, 31 of 31 wasm end-to-end subjects, the compile page
and the browser checks. IR fidelity graded its controls and reported no
unexpected result.

The battery (`-Tier all`) on the release seed passed 2,098 of 2,167 tests
with 58 declared skips; the three oracles agreed with the host (2,013 scalar,
130 vector, 1,485 CCE with 31 documented gaps). Its 11 failures were all GPU
diffusion tests over their 60-second wall budget with 14 guests sharing one
GPU; run one at a time, 10 passed, and `flux-first-image`, which takes 27 to
74 seconds alone and prints the right answer every time, now carries a `.slow`
sidecar (32453). The poison battery, on a seed built from the release source
with a 0xCD fill, passed 2,093 with no uninitialized-field fault; its 15
failures were the same GPU wall budget, 14 passed one at a time, and
`flux-euler` printed its expected output by hand in 183 seconds against 33
unpoisoned.

Roslyn built the freshly emitted C# compiler. Both DDC arms produced
3,813,351 bytes with zero differences from the release seed outside offsets
40..135. The symbol map matched all 6,368 embedded MAP1 rows by name, address
and size.

Both boot images were rebuilt on the release seed. All 59 diagnostic
rehearsal arms passed across Codex VM and QEMU/OVMF with a 180-second minimum
arm allowance, and the shipping check confirmed the default configuration. The
diagnostic image SHA-256 is
`8AC817F307B8DDEAFAFB0F21AD3CFDCB665EAC59FCF3461507261CCB85F86448`.

The box sampler (`docs/Agents/box-release-2026-09-30-u65.csv` and `-u65b.csv`)
recorded a free-memory floor of 16.2 GiB at 08:52, in the gate's test-chapter
run with 11 guests at about 1.7 GB each, and a peak of 17 guests at 09:37, in
the battery, at about 0.8 GB each.

## Site republish with this release (Damian, DamianDecisions 1.1)

First, CobblestoneWeb; then, the ModBuilder page
(`cobblestoneproject.com/modbuilder/`). What the published site carries stale,
and the file that replaces it:

| page | stale on the site | replaced by |
|---|---|---|
| fishtank | `fishtank.wasm` before main 30043 | `apps/fishtank/web/fishtank.wasm` at head |
| ModBuilder | `unity-stdio.wasm` before MB-14 | the embed re-made at main 30111 |
| gpushow, 39 pages | no live-compile block | the pages `apps/gpushow/tools/build-pages.mjs` assembles at head |
