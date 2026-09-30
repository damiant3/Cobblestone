# Spark Studio: the design and the plan

Spark Studio is Damian's WPF Spark (`D:\Projects\Spark`, outside the depot)
rebuilt on Cobblestone: an AI creative workbench for game and story art,
served from the cobblestoneproject landing as a page a newcomer can touch and
use, with a simple mode and an advanced mode. The register entry is
CurrentPlan, "Spark, the image studio"; this file is its stage 0.

**Rulings this design carries (Damian through root, 2026-09-30):**

- The LLM is BOTH a local model running in our own wasm and WebGPU, and a
  provider LLM on the user's own account.
- Full scope: image, audio, video and 3D, one step at a time, each step its
  own landing.
- This file is docs only. Implementation starts after the gated release.

The rest of `apps/spark` (the 3D, canvas, audio and video chapters listed in
`README.md`) is a separate suite. Spark Studio does not replace it. The later
audio, video and 3D steps draw on its chapters (the table below says which).

## 1. The original, feature by feature

This inventory was measured from the source on 2026-09-30. The original has
82 files and 12,738 lines in `Spark`, plus 1,242 lines in `Common.Core` and
`Common.Wpf`. Paths are relative to `D:\Projects\Spark`.

| Original feature | Evidence | In Spark Studio | Step |
|---|---|---|---|
| Text to image through Forge's HTTP API (`sdapi/v1/txt2img`), text to image only | `Spark/Services/ImageGenerator.cs:244-340` | The in-browser engine (`docs/Designs/Active/Apps/InBrowserDiffusion.md`, red), and `codex_image` (`apps/diffusion/serve.ps1`) when the page runs beside a native server. Forge leaves the path entirely | 1 |
| Prompt file (`SERIES` lines, `PROMPT NN` lines with a quoted title after the original's own separator character, a per-prompt `LORA: name:weight` line) | `Spark/PromptParser.cs:30-101` | The same text format, parsed by a Codex chapter, so an existing `ArtPrompts.txt` opens unchanged | 1 |
| Generate All: every prompt, RunsPerPrompt jobs each, in shuffled order, one job at a time | `Spark/MainViewModel.cs:572-652`; `GenerationService.cs:30-83` | A job queue in the page over the engine, one job at a time (one GPU), cancellable | 1 |
| Gallery: one card stack per prompt; a lightbox with zoom, pan and arrow keys | `Spark/PromptStack.cs`; `LightboxWindow.xaml.cs` | The same, as page components | 1 |
| Detail panel: rate 1-5, delete, regenerate, mutate, a refine preset, a prompt augment | `Spark/Controls/DetailPanel.xaml:21-64` | The same actions. A rating of 2 or less soft-deletes and requeues the prompt with a fresh seed, as in the original (`MainViewModel.cs:267-285`; the requeue at `:451` sets `Seed = -1`) | 1 |
| "Upscale" re-runs the seed at 1.5x the size with at least 25 steps; "Smaller" at 0.75x; both floor to multiples of 64, at least 512 (`ScaleBucket`) | `MainViewModel.cs:491-568, 504`; `ImageGenerator.cs:99-104` | Replaced by the real upscalers `codex_image` already has (hires fix and R-ESRGAN 4x+, `Diffusion.md`). The size variants stay | advanced mode, 1 |
| Refine presets, creative pools, art directions (heuristic prompt shaping, no LLM) | `ImageGenerator.cs:28-78`; `CreativeEngine.cs:47-64`; `MainViewModel.cs:352-396` | Ported as data (the original's JSON configs). The original ignores the project's own copies and reads the ones beside the exe (`SparkProject.cs:42-45` against `ArtDirections.cs:39`). Here the project's copies are the ones read | 1 |
| Preference tracking: style words counted over rated images, added to or removed from later prompts | `PreferenceTracker.cs:106-149` | Ported | 1 |
| Document store: story snippets matched by keyword overlap and placed in front of the prompt | `DocumentStore.cs:168-218` | Ported. The LLM panel can use the same snippets as context | 1 |
| Installed LoRA list; trigger words (`trainedWords`) looked up on CivitAI by the file's SHA256; a CivitAI LoRA search | `ImageGenerator.cs:173-210`; `LoraService.cs:149-183`; `Services/CivitAiClient.cs:21-30, 43-75` | The catalogue (section 4). The hash is computed in the page over the user's own file | 2 |
| LoRA download into a hard-coded Forge folder | `ImageGenerator.cs:218` | Links only, never a hosted copy. The user downloads and then picks the file | 2 |
| Model family (SD1.5, SDXL, Flux) guessed from the checkpoint's name | `GenerationSettingsViewModel.cs:124-157` | Classified from the file's safetensors header, as `codex_image` already does (`img2img-model` binds the SDXL, then the SD1.5 layout; a Flux key) | 2 |
| New Project wizard: Setup, Story and Setting, Art Style, Art Prompts, Generation Settings. Ollama writes the synopsis and the prompts | `Wizard/WizardViewModel.cs:32-48, 228-343` | The wizard, with the LLM panel (section 5) in Ollama's place | 3 |
| Service probes and launchers (Ollama, Forge, MusicGen, every 10 s); a setup guide that repairs a Python install | `LocalServiceManager.cs:91-256`; `Presenters/SetupGuidePresenter.cs:109-194` | Dropped. The page needs no service. The one probe left is whether the browser exposes WebGPU, plus whether a native `codex_image` server answers | 1 |
| Music Director and Sound FX: tag-built prompts to a MusicGen Gradio server (`facebook/musicgen-medium`); play, rate, variant; SFX in batches; BPM by FFT | `Services/MusicGenClient.cs:39-162`; `Presenters/SfxPresenter.cs`; `Services/MusicAnalyzer.cs:54-108` | The audio step (section 7). Playback, the waveform and the spectrum come from this suite's `AudioDsp`, `WaveformView` and `SpectrumAnalyzer` | 5 |
| Project folder: `spark_project.json`, `ArtPrompts.txt`, `Concept/<settings tag>/*.png`, `catalog.json`, `preferences.json`, music and SFX catalogs | `SparkProject.cs:74-100`; `ImageCatalog.cs`; `JsonStore.cs` | The same layout, so an original project opens here (section 6). Every PNG also carries Forge's infotext (`PngMetadata`), which the original never wrote | 1 |
| Dark and light themes, switchable | `DarkTheme.xaml`; `LightTheme.xaml`; `Services/ThemeManager.cs` | The visual uplift (step 1), both themes | 1 |

Absent from the original, and not invented for step 1: img2img, inpaint,
masks, ControlNet, video and 3D. `codex_image` already serves img2img,
inpaint, hires fix, the refiner and the built-in extras (`Diffusion.md`), and
advanced mode exposes them from step 1.

## 2. Where it runs

- **The page** is the product: a landing card ("touch it now", val reviews
  it) opens Spark Studio in the browser.
- **The engine** is the in-browser diffusion page's engine, owned by red.
  Its stages and measurements are in `InBrowserDiffusion.md`, which this
  design only consumes. The weights are the user's own files, picked in the
  page and never uploaded anywhere.
- **The native route** is `codex_image`, which the page reaches when a
  local server answers. It is faster, NVIDIA only, and has every Forge
  feature listed in `Diffusion.md`: all 25 samplers, LoRAs, SDXL, SD1.5
  and Flux. When a server answers it runs every job. Otherwise the browser
  engine runs the jobs it can (SD1.5 with Euler today,
  `InBrowserDiffusion.md`), and any other job is refused with the reason
  before it is queued. The page shows which engine will run a job before
  the job starts.
- **Step 1's page** is the in-browser diffusion page
  (`apps/diffusion/BrowserImagePage.codex`, built by
  `apps/diffusion/build-browser-page.mjs`). Spark Studio grows out of it,
  and red owns both.
- **The browser is slower.** WebGPU exposes no tensor cores, and Damian has
  accepted that gap (2026-09-30). The page states the expected time per
  image for the engine in use.

## 3. The simple mode and the advanced mode

**Simple mode** is the default. It shows exactly these controls:

- prompt and negative prompt
- a model picker over the files the user has added
- size, from three presets per model family
- one quality choice, Draft or Good, which sets the steps
- seed, with a re-roll button
- Generate, and the gallery

| Family | Sizes | Draft / Good steps | Sampler, CFG |
|---|---|---|---|
| SD1.5 | 512 x 512, 768 x 512, 512 x 768 | 12 / 25 | Euler, 7 |
| SDXL | 1024 x 1024, 1344 x 768, 768 x 1344 | 12 / 25 | DPM++ 2M Karras, 5 |
| Flux schnell | 1024 x 1024, 1344 x 768, 768 x 1344 | 4 / 4 | Euler, 1 (schnell's own settings) |

A checkpoint with published settings of its own overrides its row. For
example, dreamshaperXL lightning is DPM++ SDE, 6 steps, CFG 2
(`apps/modbuilder/page/generate-art.ps1`). The catalogue entry carries those
settings.

**Advanced mode** adds every other control `codex_image` takes, grouped the
way Forge groups them:

| Group | Controls |
|---|---|
| Sampling | sampler, scheduler, steps, CFG |
| Seeds | variation seed and strength, seed resize |
| Networks | LoRAs and their weights |
| Encoders | clip skip |
| Hires fix | scale, denoise, steps, CFG, upscaler, sampler, checkpoint, prompt |
| Refiner | checkpoint, switch-at |
| Image to image | img2img, inpaint (mask, blur, soft inpainting, only-masked padding), outpaint |
| Built-in extras | FreeU with its step window, SAG, PAG, dynamic thresholding |
| Workbench | refine presets, creative pools, art directions |

The mode is a view over one request. Switching mode never changes a value
that simple mode hides.

## 4. Models and LoRAs: links, never copies

- **Links.** The catalogue lists the popular checkpoints and LoRAs per model
  family. Each entry links to its page on CivitAI or Hugging Face, with the
  file name, size and licence. The page hosts no model file and proxies no
  download.
- **Adding a file.** The user downloads the file and picks it in the page.
  The page then:
  - reads the safetensors header to classify the file: checkpoint family,
    or LoRA kind and base family, with the same rules `codex_image` uses;
  - refuses a file whose family the engine cannot run, naming the family;
  - computes the SHA256 of the file and asks CivitAI's
    `api/v1/model-versions/by-hash/{hash}` for its trigger words
    (`trainedWords`), as the original does (`CivitAiClient.cs:21-30`). The
    request carries the hash and nothing else.
- **Search.** The catalogue also searches CivitAI's LoRAs by name, as the
  original's LoRA browser does (`CivitAiClient.cs:43-75`), and shows each
  result as a link.
- **Grading (step 2).** Every catalogue link answers 200. Every entry's
  declared family equals the header classification of the file it links,
  checked by downloading each file once. A file of the wrong family is the
  sabotage.

## 5. The LLM prompt panel

The panel has one provider interface: messages in, streamed text and tool
calls out. This is the interface Prism stage 4 defines
(`apps/prism/design/Active/PrismDevEnvironment.md`, "Agent mode"). Two
providers implement it.

- **The provider LLM.** The user's own Anthropic key, held only in that
  browser's `localStorage`. The key never enters a project file, an export
  or a built artifact, and the panel is inert until a key exists (Prism's
  rule). The request shape is Prism's pinned one, every row of it
  (`PrismDevEnvironment.md`, "The request shape, pinned"): the endpoint
  and the browser-access header, the model id, adaptive thinking with
  `output_config.effort`, summarized display, no prefill, the refusal stop
  reason and `max_tokens`. Copy it from that design; do not write it from
  memory.
- **The local LLM.** Our own transformer decoder, running on WebGPU from
  wasm, over a GGUF file the user picks (links as in section 4).
  `codex/foreword/ai` has `Gguf.codex` (graded by `gguf-hostile`),
  `Transformer.codex` and `KvCache.codex` as CPU reference chapters. The
  kernels follow the path `InBrowserDiffusion.md` measures for WGSL. This
  is a model runtime of its own: step 3 ships with the provider first, and
  the local model is step 4.

**What the panel does:**

- writes the wizard's synopsis and art prompts, as the original's Ollama
  step did;
- expands or rewrites one prompt in the detail panel, beside the augment
  box;
- explains a refusal the engine gave.

The document store's snippets are the context. The panel never starts a
generation without the user pressing Generate.

## 6. The project store

The project is a folder the user picks: the File System Access API where the
browser has it, the origin-private file system otherwise.

- The layout is the original's (section 1), so a project made by the WPF
  Spark opens here and one made here opens there.
- The original's `catalog.json` stores each image's absolute Windows path
  (`filePath`). On opening, each path is taken relative to the project
  folder from its `Concept\` component onward. A record whose file is not
  under the project folder is listed as missing, never dropped. This page
  writes relative paths.
- The catalogue is written as the original's `JsonStore` writes it, one
  whole file per change. A project holds thousands of records, not
  millions, so the cost is a rewrite of a small file.
- Soft-deleted images are purged 24 hours later, as in the original
  (`ImageCatalog.cs:61-79`).

## 7. Audio, video and 3D, one step at a time

Each of these is a step of its own, after the image studio is done. Each is
graded against a foreign reference, the way `Diffusion.md` grades against
Forge.

- **Audio (step 5):** the Music Director and Sound FX tabs.
  - Generation is MusicGen (`facebook/musicgen-medium`, the original's
    model) on the same engine: a transformer decoder over EnCodec tokens.
    It shares the local LLM's decoder work.
  - Graded against the reference implementation's tokens and waveform for
    fixed seeds.
  - Playback, mixing, waveform and spectrum come from this suite's audio
    chapters.
- **Video (step 6):** first composition of generated stills and audio on a
  timeline (`VideoCompositor`, `Timeline`, `Keyframe`), then a generative
  video model. Which model is chosen when the step is opened.
- **3D (step 7):** first the suite's scene, mesh and render chapters, shown
  in the page, with generated textures from the image engine; then a
  generative 3D model (`codex/foreword/ai/ImageTo3d.codex` is the
  placeholder chapter). Which model is chosen when the step is opened.

## 8. The newcomer's first run

1. The page opens in simple mode and checks for WebGPU. With no WebGPU it
   says so, and offers the native route with its setup link.
2. With no model added, the first screen is the catalogue's "start here"
   entry: one SD1.5 checkpoint (the smallest family the engine runs), its
   link, its size, and the one button that opens the file picker.
3. When the file is classified, the page generates one image at the
   family's defaults, with the time estimate shown before the job starts.
4. After that image, a single line points at the wizard ("make a project
   from a story") and at advanced mode.

## 9. The plan

Each step is its own landing, and each starts after the gated release
(Damian through root, 2026-09-30). The owner is red, per CurrentPlan. val
reviews the landing card. These steps replace the four stages CurrentPlan
listed before this design.

| Step | Delivers | Graded by |
|---|---|---|
| 0 | This design | a naive reader (R-NAIVE) before it lands |
| 1 | The visual uplift and simple mode over the in-browser engine. The prompt file, gallery, detail panel, job queue and project store. Advanced mode over `codex_image`'s request | the original's own project (`D:\Projects\Spark\Spark`, 17 records, 2026-09-30) opens with every record and prompt listed and every file found through the path rule of section 6; the same request made through both engines, where both can run it, gives the same picture within the engines' measured distance; each advanced control is checked against `codex_image`'s infotext for that request. The original's PNGs are no oracle, because its catalog records no checkpoint |
| 2 | The model and LoRA catalogue with links, header classification, trigger words by hash | section 4 |
| 3 | The LLM panel with the provider LLM: wizard, prompt expansion | Prism stage 4's arms over the shared interface. The acceptance also needs one billed call on Damian's key, which he deferred for Prism (2026-09-08, `DamianDecisions.md`); until he lifts that, step 3 lands with its arms and the billed call stays open |
| 4 | The local LLM on WebGPU | token-for-token against llama.cpp on the same GGUF file and seed at temperature 0, with a sabotage arm |
| 5 | Audio | section 7 |
| 6 | Video | section 7 |
| 7 | 3D | section 7 |
| 8 | The landing card | val's review; the card's links answer 200 |

**Open for Damian:**

- whether the provider list starts at Anthropic alone;
- which GGUF model the local LLM targets first, by size against the card
  a newcomer is likely to have.
