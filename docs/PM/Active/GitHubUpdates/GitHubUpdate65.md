# GitHub Update 65

**NOT PUSHED. The cycle after Update 64.** Record landed changes here after
the Update 64 publication boundary. Update 64 was pushed from a tree frozen
at main 29237 plus its release documents; main CLs after that head that are
not named in GitHubUpdate64.md publish here.

**DRAFT themes (red, 2026-09-29), for the release Damian calls.** Numbers in
parentheses are main changelists. The release proof is added by the gate.

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

## The compiler

- **Closures carry every value past six** (COMPILER-106, val 30603): a closure
  holding captures plus arguments past the six x86-64 argument registers (past
  eight on arm64 and riscv) dropped the surplus since the first closure codegen;
  five parser continuations at seven values passed only by frame layout.
  Graded over captures 0-16 by arity 1-12 on all three targets.
- **Large module-level integer tables compile** (COMPILER-107, val 30619): a
  `List Integer` literal past about 6,000 elements crashed the compiler; tables
  are now written straight to the data buffer, to 65,504 elements.

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

## Site republish with this release (Damian, DamianDecisions 1.1)

First, CobblestoneWeb; then, the ModBuilder page
(`cobblestoneproject.com/modbuilder/`). What the published site carries stale,
and the file that replaces it:

| page | stale on the site | replaced by |
|---|---|---|
| fishtank | `fishtank.wasm` before main 30043 | `apps/fishtank/web/fishtank.wasm` at head |
| ModBuilder | `unity-stdio.wasm` before MB-14 | the embed re-made at main 30111 |
| gpushow, 39 pages | no live-compile block | the pages `apps/gpushow/tools/build-pages.mjs` assembles at head |
