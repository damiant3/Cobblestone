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
| Text to image through Forge's HTTP API (`sdapi/v1/txt2img`), text to image only | `Spark/Services/ImageGenerator.cs:244-340` | The in-browser engine (`docs/Designs/Active/Apps/InBrowserDiffusion.md`, red) only (section 2). Forge leaves the path entirely | 1 |
| Prompt file (`SERIES` lines, `PROMPT NN` lines with a quoted title after the original's own separator character, a per-prompt `LORA: name:weight` line) | `Spark/PromptParser.cs:30-101` | The same text format, parsed by a Codex chapter, so an existing `ArtPrompts.txt` opens unchanged | 1 |
| Generate All: every prompt, RunsPerPrompt jobs each, in shuffled order, one job at a time | `Spark/MainViewModel.cs:572-652`; `GenerationService.cs:30-83` | A job queue in the page over the engine, one job at a time (one GPU), cancellable | 1 |
| Gallery: one card stack per prompt; a lightbox with zoom, pan and arrow keys | `Spark/PromptStack.cs`; `LightboxWindow.xaml.cs` | The same, as page components | 1 |
| Detail panel: rate 1-5, delete, regenerate, mutate, a refine preset, a prompt augment | `Spark/Controls/DetailPanel.xaml:21-64` | The same actions. A rating of 2 or less soft-deletes and requeues the prompt with a fresh seed, as in the original (`MainViewModel.cs:267-285`; the requeue at `:451` sets `Seed = -1`) | 1 |
| "Upscale" re-runs the seed at 1.5x the size with at least 25 steps; "Smaller" at 0.75x; both floor to multiples of 64, at least 512 (`ScaleBucket`) | `MainViewModel.cs:491-568, 504`; `ImageGenerator.cs:99-104` | Replaced by the engine's upscalers when it gains them (hires fix and R-ESRGAN 4x+, which `codex_image` has, `Diffusion.md`). The size variants stay | advanced mode, 1 |
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
masks, ControlNet, video and 3D. `codex_image` serves img2img,
inpaint, hires fix, the refiner and the built-in extras (`Diffusion.md`), and
advanced mode exposes each once the browser engine has it.

## 2. Where it runs

- **The page** is the product: a landing card ("touch it now", val reviews
  it) opens Spark Studio in the browser.
- **The engine** is the in-browser diffusion page's engine, owned by red.
  Its stages and measurements are in `InBrowserDiffusion.md`, which this
  design only consumes. The weights are the user's own files, picked in the
  page and never uploaded anywhere.
- **There is no native route.** Damian ruled "no page hand-off to a native
  helper" (CurrentPlan, release section), so the page never reaches
  `codex_image`. Every job runs on the browser engine, which runs the jobs
  it can (SD1.5 with Euler, SDXL with the supported Euler/DPM++ choices over Karras,
  `InBrowserDiffusion.md`);
  any other job is refused with the reason before it is queued. Advanced
  mode's controls appear as the engine gains each one.
- **Step 1's page** is a new page in `apps/spark` that cites the engine's
  chapters (sampler, graphs, kernels). The in-browser diffusion page
  (`apps/diffusion/BrowserImagePage.codex`) stays red's live demo. A piece
  the Spark page needs that is private to red's page is lifted by red into
  a citable chapter (root, 2026-09-30).
- **The browser is slower.** WebGPU exposes no tensor cores, and Damian has
  accepted that gap (2026-09-30). The page states the expected time per
  image for the engine in use.

## 3. The simple mode and the advanced mode

**Simple mode** is the default. It shows exactly these controls:

- prompt and negative prompt
- a model picker over the files the user has added
- size, from the model family's presets
- one quality choice, Draft or Good, which sets the steps
- seed, with a re-roll button
- Generate, and the gallery

| Family | Sizes | Draft / Good steps | Sampler, CFG |
|---|---|---|---|
| SD1.5 | 512 x 512, 768 x 512, 512 x 768 | 12 / 25 | Euler, 7 |
| SDXL | 1024 x 1024, 1024 x 768, 768 x 1024, 1344 x 768, 768 x 1344 | 12 / 25 | Euler default; DPM++ choices below, Karras, 5 |
| Flux schnell | 1024 x 1024, 1344 x 768, 768 x 1344 | 4 / 4 | Euler, 1 (schnell's own settings) |

SDXL dimensions follow the engine's 64-1344 axis bounds, multiples of 32,
and maximum area of 1048576 pixels. Requests outside those limits are
refused, never clamped. Preview canvases retain the image's aspect ratio.
Advanced mode selects Euler, DPM++ 2M, DPM++ SDE, DPM++ 2M SDE,
DPM++ 2M SDE Heun or DPM++ 3M SDE for SDXL. The SDE routes use the
job-local noise Wasm worker embedded by the page builder. SD1.5 remains
Euler-only, with that reason beside its disabled choices. Unsupported sampler
requests are refused without substitution. DPM++ 2S ancestral is not exposed
by Studio. Flux remains unavailable until its browser engine is exposed.

Checkpoint recommendations remain distinct from family defaults. For example,
dreamshaperXL lightning publishes DPM++ SDE, 6 steps and CFG 2
(`apps/modbuilder/page/generate-art.ps1`); Studio uses the SDXL family defaults
until the user changes the supported controls.

`SparkImageEngine.codex` exposes both image families.
Each running request freezes its dimensions, steps, CFG, sampler, scheduler, negative
prompt and model path until display and project save complete. Catalog
records keep the actual dimensions, seed, model family/file and negative
prompt. The settings tag and output path retain the actual sampler. Model
unloading and project changes refuse while an image job runs. Queued jobs
capture controls individually when each job starts.

`studio-sdxl.mjs` accepts optional width, height and a supported sampler name
after its four artifact paths. It grades a real SDXL image, PNG/catalog save,
settings changed during generation, exact reopened pixels/catalog identity,
noise worker termination and handle release. Non-Euler and rectangular
references require evidence binding the specialized native source. Its writable project
is an OPFS fixture supplied through the directory-picker callback; it does
not prove external-folder permission persistence.

Measured Studio image proofs on 2026-10-01, against matching native
renders (mean RGB difference must be below 4/255):

| Studio source | Sampler over Karras | Size | Mean difference / 255 | Sampled GPU peak |
|---|---|---|---|---|
| Main 33400 | Euler | 1024 x 1024 | 0.755 | 15,632 MiB |
| Main 33505 | Euler | 1344 x 768 | 0.599 | 15,676 MiB |
| Main 33666 | DPM++ 2M | 1024 x 1024 | 1.308 | 15,631 MiB |
| Main 33666 | Euler | 1024 x 1024 | 0.755 | 15,598 MiB |
| Main 33709 | DPM++ SDE | 1024 x 1024 | 1.076 | 15,609 MiB |
| Main 33709 | DPM++ 2M SDE | 1024 x 1024 | 1.275 | 15,616 MiB |
| Main 33709 | DPM++ 2M SDE Heun | 1024 x 1024 | 1.255 | 15,613 MiB |
| Main 33709 | DPM++ 3M SDE | 1024 x 1024 | 0.990 | 15,627 MiB |

Sampler-selection runs check the actual engine sampler ID, save/reopen,
and catalog tags after changing the live sampler control during generation.
The four SDE runs additionally compare exact reopened pixels and catalog
identity, and require one created/terminated noise worker with no live jobs.
SDE load/generate/save/reopen runs took 98.1, 70.4, 70.8 and 70.2 seconds in
the table's order. These Studio runs cover successful completion; cancellation
and noise-error cleanup remain covered by the
[engine's separate grades](../../docs/Designs/Active/Apps/InBrowserDiffusion.md#sdxl-image-composition).
The selector adds fixed page nodes and constant per-job state; sampling work
remains in the diffusion engine. The existing log retains one DOM row per
progress notification, including noise polls. Compiler heap/time behavior is unchanged.

The rectangular run also checks displayed aspect ratio, title/prompt/path
metadata, axis and area refusals, and both reopened image dimensions. The
768 x 1344 option is enabled; its actual rendering is graded at the engine
level in `InBrowserDiffusion.md`, not by this Studio run. These are
browser/native backend comparisons, not an independent foreign-engine
oracle. Main 33400 also passed `studio-generate.mjs` 10/10 SD1.5 checks and
`studio-page.mjs` 25/25 project/gallery checks. The LoRA caller proof below
also reloads the same SD1.5 checkpoint without LoRA. Switching between
different checkpoint families remains unproved in Studio.

**Advanced mode** adds, as the browser engine gains each, the controls Forge offers, grouped the
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
  - reads the safetensors header to classify the file
    (`SparkModelFile.codex`): a checkpoint by `codex_image`'s own test (Flux
    holds `double_blocks.0.img_attn.qkv.weight`, else the first of SDXL and
    SD1.5 whose whole layout binds); a LoRA by the family whose checkpoint
    weights `codex_image`'s merge (`LoraNames.codex`) finds a module for every
    tensor the file holds, "ambiguous" when more than one family does and
    "none" when no family does;
  - refuses a file whose family the engine cannot run, naming the family;
  - computes the SHA256 of the file and asks CivitAI's
    `api/v1/model-versions/by-hash/{hash}` for its trigger words
    (`trainedWords`), as the original does (`CivitAiClient.cs:21-30`). The
    request carries the hash and nothing else.
- **Search.** The catalogue also searches CivitAI's LoRAs by name, as the
  original's LoRA browser does (`CivitAiClient.cs:43-75`), and shows each
  result as a link.
- **Applying a LoRA.** Choose a classified SD1.5 or SDXL LoRA from the picked
  model list, enter its weight, then load a matching checkpoint. One standard
  F16/F32 down/up/alpha file is applied while the checkpoint loads. Changing
  the selection or weight requires reloading before Generate. Choose No LoRA
  and reload the checkpoint to restore unmodified weights. Loading waits for pooled GPU scratch
  to be trimmed after the previous model closes. Cross-family pairs, Flux,
  ambiguous and unrecognized classified LoRA families are refused by name
  before unloading the current model. The engine validates tensor formats
  when opening the selected file and reports unsupported dtype/layout then.
- **Trigger insertion.** Find trigger words by hash for the selected file,
  then Insert trigger words prepends them to the editable prompt augment,
  avoiding a duplicate case-insensitive substring, as the original's
  `ViewModels/DetailViewModel.cs:58-65` does. Each image request snapshots
  the augment and applied LoRA path/weight. The engine receives the augmented
  prompt; the catalog records `promptAugment` and `loraTag`. This selection
  is page-wide; it does not apply per-prompt `LORA:` directives automatically.
- **LoRA caller proof (2026-10-01).** `studio-lora.mjs` grades a picked SD1.5 file at
  weight 0.75 with a real 512 x 512 image: mean native difference 0.536/255,
  versus 34.825/255 from the zero-weight native image. Reloading without
  LoRA gives mean 0.623/255 and different browser pixels. PNG/canvas equality,
  catalog snapshots despite changed live controls, same-session project
  reopen and zero active handles pass. The current SD1.5 regression samples
  5,644 MiB peak GPU use. The SDXL arm applies weight 0.6 to a real
  1024 x 1024 image: mean native difference 0.846/255, versus 14.653/255
  from the zero-weight reference, with the same save/catalog/reopen and
  active-handle checks. Its sampled peak was 15,590 MiB. The SDXL arm does
  not reload a second model on the same device; retained browser/driver
  commit remains the engine's `BROWSER-LORA-RELOAD` gap in
  `docs/Designs/Active/Apps/InBrowserDiffusion.md`.
  The emitted-code CPU arm checks strict decimal weights, named family
  refusals, packaged JSON control escaping and trigger lookup,
  insertion, engine arguments and catalog propagation with hash/network/file
  I/O fixtures; it does not claim live CivitAI availability. Fixed control
  state and one button per listed non-checkpoint file add no sampling loop;
  trigger insertion scans and copies its text. Compiler heap/time behavior
  is unchanged. Engine patch allocation and tensor work remain in
  `BrowserLora.codex` and its separate native/browser grades.
- **Grading (step 2).** Every catalogue link answers 200. Every entry's
  declared family equals the header classification of the file it links,
  checked by downloading each file's header once (an HTTP Range request: the
  classification reads nothing else). A file of the wrong family is the
  sabotage. `apps/spark/model-family.mjs` grades the classification over the
  local Forge models and made files, one of each family, an ambiguous one and
  two of none.

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
- **A deviation (root, 2026-09-30).** The original names a new image by the
  count of its prompt's live stack and, when that file is already on disk,
  reports `Cached` and draws nothing (`ImageGenerator.cs:262-269`). A
  soft-deleted image keeps its file, so a regen after rating a stack's newest
  run 2 or less returns the deleted image. This page takes the next run index
  free on disk instead (`studio-generate.mjs`, the regen arm).

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
  - `SparkAudio.codex` holds the original's rules (graded by
    `codex/test/apps/spark-audio`); the page lists and plays project tracks
    and builds prompts. `SparkMusicModels.codex` supplies four local file
    pickers and MusicGen load/unload; the builder embeds the four shader
    modules. Red owns the engine contract in `docs/Designs/Active/Apps/MusicGen.md`,
    **Stage 5 engine API and grade**. `SparkMusicGenerate.codex` binds the
    engine to both tabs. Generation requires a loaded model and writable
    project, accepts 0.02 to 30 seconds rounded to 50 Hz frames, temperature
    0.1 to 2 and CFG 1 to 10. Seed -1 selects a random seed; explicit seeds
    range from 0 to 2147483646. SFX accepts 1 to 10 sequential variants with
    successive explicit seeds wrapping at 2147483647. Top-k is 250.
    A single sound uses the selected temperature. A multi-sound batch uses
    `temperature + i * 0.15 - count * 0.075`, with zero-based `i`, clamped
    to 0.2 through 1.9. Original prompt-category detection takes precedence
    over the selected category; an unmatched prompt uses the selection,
    with All meaning no category.
  - Cancel stops through the engine's progress callback. Completed files
    remain saved. Project changes and catalog edits are refused until audio
    generation and saving finish. Binary saves refuse existing filenames; a failed catalog save
    reports the already-written WAVE path without claiming the catalog saved.
    Each saved WAVE is analyzed before catalog insertion. Music records the
    original vibe description and BPM; SFX records its original attack,
    level and tone description. Analysis failure reports the saved file
    without claiming catalog insertion. Successful saves append the tab's
    catalog and expose the saved file for playback.
  - A rating of 1 or 2 removes the record, retaining its file, then generates
    one variant only after the catalog write succeeds. The variant uses the
    rated prompt, current duration/CFG, a fresh random seed, and the rated
    temperature plus jitter from -0.2 inclusive to 0.2 exclusive, clamped
    to 0.3 through 1.8. Track IDs increase throughout the open project to
    avoid reusing the removed track's filename. Without a loaded model,
    removal still succeeds and the variant refusal names the missing model.
  - Build with `node apps/spark/build-studio-page.mjs --out <output.html>`;
    grade with `node apps/spark/musicgen-studio.mjs <oracle.json> <output.html>`.
    Obtain the oracle through `MusicGen.md`, **Stage 5 engine API and grade**,
    using the actual medium-model inputs. On 2026-10-01 the packaged page
    passed all 46 integration arms: exact reference tokens and waveform
    bounds, unmocked random seeds, single/batch settings, category precedence,
    jitter/clamp boundaries, low-rating replacements, catalog-write refusal,
    cancellation on disk, playback/reopening and original WPF analysis
    metadata across generated files. The grade also checks float32 attack
    equality boundaries and retains old files when records are replaced.
    The grade uses an origin-private project and intercepted model pickers;
    audible output and human picker interaction are not established.
    `node apps/spark/studio-page.mjs` passed all 25 regression arms. The
    graded compiler was `D2C01E16DD7E342F`. Main 33360 records that audio
    implementation and package, including the HTML Integer random-seed fix;
    its compiled primitive arm passed 6 checks and all 21 HTML arm scripts
    exited 0. Main 33336 holds the val-approved card; both links returned
    200 and desktop/mobile fit and startup checks passed. Built only;
    publication remains Damian's.
  - The analysis (`MusicAnalyzer.cs`: waveform, RMS envelope, 64-band
    spectrogram, BPM by onset autocorrelation, spectral centroid, and the vibe
    tag built from them) is ported for PCM16 and finite IEEE-f32 WAVE,
    including generated MusicGen files (main 33324), and the waveform and spectrum views
    draw its output. Its two transforms are naive DFTs: the spectrogram of a
    30 s track at 32 kHz is about 234 frames x 1024 bins x 2048 samples, 490
    million sine and cosine pairs, and the centroid 34 million more. The port
    computes the same sums through an FFT. The original works in float32 and
    reads through NAudio's `AudioFileReader`, so the grade is a tolerance
    against the original run on the project's own WAVs (NAudio from NuGet in
    the oracle), with the vibe tag and the rounded BPM exact. Run
    `node apps/spark/audio-analyze.mjs` for the original PCM fixtures and add
    `--float32` for equivalent f32 samples with MusicGen's chunk layout.
    Both use the original WPF/NAudio analyzer as the oracle. The 2026-10-01
    grades passed 10 and 11 arms respectively over 14 tracks; float conversion
    changes analysis input, not cached player URLs.
- **Video (step 6):** first composition of generated stills and audio on a
  timeline (`VideoCompositor`, `Timeline`, `Keyframe`), then a generative
  video model. Which model is chosen when the step is opened.
  - The composition opens existing project stills, orders clips by frame
    duration, crossfades into the next still, and optionally plays a project
    audio file. Preview uses the suite's compositor at 320 x 180; transport
    follows elapsed time and skips preview frames when rendering is slower
    than the composition's frame rate. Saving writes the timeline, not an
    encoded movie. Generative video remains unavailable until an engine exists.
  - `Video/timeline.json` is a versioned project-relative file:
    `{"version":1,"fps":24,"prompt":"","audio":"Music/track.wav","clips":[{"file":"Concept/still.png","frames":72,"fade":12}]}`.
    The prompt holds composition notes for now; no model consumes the prompt.
    Empty audio means no soundtrack. A clip's fade occupies the final frames
    of that clip and blends toward the next still; the final clip has no
    outgoing fade. All frame values and the frame rate are whole numbers.
    Frame rate is 1 through 60, clip length 1 through 36000
    frames, fade 0 through clip length, and a timeline holds at most 128
    clips. Missing assets remain named; invalid or unsupported timeline files are
    refused without rewriting them. Read-only projects can preview but cannot
    save. `apps/spark/video-compose.mjs` grades composition against browser
    Canvas pixels, disables blending as a negative control, and checks
    soundtrack seeking, transport, ordering, removal and save/reopen. Run
    `node apps/spark/video-compose.mjs` with Node and headless Edge installed,
    after building the HTML plug with `pwsh codex/plugs/html/build.ps1`.
- **3D (step 7):** first the suite's scene, mesh and render chapters, shown
  in the page, with generated textures from the image engine; then a
  generative 3D model (`codex/foreword/ai/ImageTo3d.codex` is the
  placeholder chapter). Which model is chosen when the step is opened.
  - The scene editor adds cube, sphere and plane meshes, places objects,
    selects project images as textures, and orbits and zooms the camera.
    `SceneTexture` extends the suite's scene renderer with perspective-correct
    UV interpolation, near-plane clipping and a depth test. The preview is
    unlit, double-sided, 320 x 240, with 128 x 128 nearest-filtered textures;
    depth is quantized to millionths of normalized device depth. The browser
    only decodes images and presents the framebuffer; Codex renders the scene.
  - `Scene/scene.json` stores version 1, a camera and ordered objects:
    `{"version":1,"camera":{"yaw":25,"pitch":20,"distance":5},"objects":[{"shape":"cube","x":0,"y":0,"z":0,"texture":"Concept/still.png"}]}`.
    Camera angles are degrees, yaw -180 through 180 and pitch -75 through 75;
    distance is 2 through 30 scene units. Object coordinates are -10 through
    10 scene units. All stored numeric values are whole numbers. At most 16
    objects are accepted. An absent texture field or empty texture path
    selects a checkerboard; a non-text texture value is refused.
    Missing or unreadable textures are named in the object list and show the
    checkerboard without dropping the reference. Unsupported or invalid scene
    files are refused without rewriting them. A missing scene file opens a
    new scene; an existing zero-byte scene file is invalid. Read-only projects can view and
    edit a preview but cannot save. No 3D generation model is connected.
  - Run `node apps/spark/scene-view.mjs` with Node, PowerShell, headless Edge
    and the built HTML plug. The grader compares the rasterizer with WebGL on perspective
    interpolation, near clipping and depth order, comparing uniform-color
    interior pixels away from raster and texture boundaries. It disables
    texture sampling as a negative control, and checks texture selection, camera movement,
    object editing and save/reopen in the page.

## 8. The newcomer's first run

1. The page opens in simple mode and checks for WebGPU. With no WebGPU it
   says so and names the browsers that have it.
2. With no model added, the first screen is the catalogue's "start here"
   entry: one SD1.5 checkpoint (the smallest family the engine runs), its
   link, its size, and the one button that opens the file picker.
3. When the file is classified, the page generates one image at the
   family's defaults, with the time estimate shown before the job starts.
4. After that image, a single line points at the wizard ("make a project
   from a story") and at advanced mode.

## 9. The plan

Each step is its own landing, and each starts after the gated release
(Damian through root, 2026-09-30). red owns the engine and step 4; blu owns
the other steps (root, 2026-10-01), and val reviews the landing card. These steps replace the four stages CurrentPlan
listed before this design.

| Step | Delivers | Graded by |
|---|---|---|
| 0 | This design | a naive reader (R-NAIVE) before it lands |
| 1 | The visual uplift and simple mode over the in-browser engine. The prompt file, gallery, detail panel, job queue and project store. Advanced mode over the controls the engine has | the original's own project (`D:\Projects\Spark\Spark`, 17 records, 2026-09-30) opens with every record and prompt listed and every file found through the path rule of section 6; each advanced control is checked against `codex_image`'s infotext for the same request, as a reference run beside the page, not reached by it. The original's PNGs are no oracle, because its catalog records no checkpoint |
| 2 | The model and LoRA catalogue with links, header classification, trigger words by hash | section 4 |
| 3 | The shared Anthropic provider, explicit draft/apply prompt assistance and the five-step new-project wizard. The wizard keeps the original's defaults (My Game, 10 prompts, 1344x768, 20 steps, CFG 7, DPM++ 2M SDE) and, as the original does, writes its three template prompts when the prompts are left empty. A project's saved size and sampler apply when it opens. Local generation belongs to step 4. | Shared provider, context and browser arms cover canned responses, cancellation, refusal and stale drafts; `test-studio-wizard.mjs` (files, refusals, partial writes) and `studio-wizard-browser.mjs` (21 checks over file://: defaults, step refusals, folder cancel, six files written, project opened, existing folder refused). The packaged `apps/landing/web/sparkstudio.html` predates the wizard and is rebuilt by the next landing build. Acceptance still needs the deferred billed call on Damian's key (`DamianDecisions.md`). |
| 4 | The local LLM on WebGPU. Built: the bounded Qwen3 GGUF header and tensor-directory parser (`qwen-gguf.mjs`) and the GPT-2 byte-level BPE tokenizer (`qwen-tokenizer.mjs` over the wasm merge engine `QwenBpeWasm.codex`), and Q4_K/Q6_K dequantization and matrix-vector kernels (`QwenKernels.codex`, built by `codex/plugs/wgsl/run.ps1`; one thread per output row, not yet tuned), and the one-token Qwen3 decoder over them (`qwen-forward.mjs`: the page-side runtime holding GPU buffers and the dispatch plan per token; RoPE cos/sin from the host as ggml computes them). Measured on the RTX 4060 Ti in headless Edge (2026-10-01): 5.22 GB uploaded in 19 s, the 26-token prompt one position at a time in 15.5 s, 12 tokens in 6.0 s. The Studio panel's `local` provider is `createLocalQwenProvider` (page bundle `qwen-page-bundle.mjs`, installed by `build-studio-page.mjs`): it asks for the GGUF file on first use, keeps the model on the GPU, applies the Qwen3 chat template with thinking off, tokenizes system and user text without control tokens, streams text, and stops on an end-of-generation token or `maxTokens`. Open: batched prefill and kernel tuning; the packaged landing page (main 34215) predates the provider and is not rebuilt | token-for-token against llama.cpp on the same GGUF file and seed at temperature 0, with a sabotage arm. Built parts: `test-qwen-gguf.mjs` (399 tensors against llama.cpp `gguf-py` at 7fe450e, 8 refusal controls) and `test-qwen-tokenizer.mjs` (38 sequences against llama.cpp b11146 `/tokenize`, lossless decode, stable arena) and `test-qwen-dequant.mjs <QwenKernels.wgsl>` (128 superblocks from each of three Q4_K and two Q6_K tensors, f32 bit-identical to `gguf-py` at 7fe450e on WebGPU in headless Edge) and `test-qwen-matvec.mjs <QwenKernels.wgsl>` (two full Q4_K tensors and one full Q6_K tensor, up to 12,288 columns, within 1e-6 normalized of `gguf-py` dequantization multiplied in float64; compensated summation is load-bearing, as the uncompensated 12,288-term rows reach 9.6e-6); and `test-qwen-generate.mjs <QwenKernels.wgsl>` (the chat-template prompt, 12 greedy tokens equal to llama.cpp b11146 `llama-server` at temperature 0, and top-2 log-probabilities within 1.5 nats at each step; the control measures 0.72, a RoPE rotation-sign sabotage 6.19 with every token still equal, so the token comparison alone cannot see it; a GQA grouping sabotage changes the first token; fixture from `make-qwen-generation-reference.mjs`, whose emoji steps llama-server reports as one entry for two tokens). and `test-qwen-provider.mjs <QwenKernels.wgsl>` (the provider in a page over a disk-backed File: prompt tokens equal llama.cpp's 26, 11 streamed tokens equal llama.cpp ending `end_turn`, turn markers inside user content stay text, a cancelled request is refused before loading; a plain-text assistant header and control-token parsing of user content each go red). All need the local blob below and share the harness `qwen-gpu.mjs` |
| 5 | Audio | section 7 |
| 6 | Video | section 7 |
| 7 | 3D | section 7 |
| 8 | The landing card and packaged Studio | The unchanged SDXL and MusicGen card is main 33420. Val approved package main 34215, raw SHA256 `9AD7AE978C97C3D149A1AF02AB34F9886C6570F8084098C4187B16D90240757A`; MAIN bytes equal the tested staged file. Source compiler `267B6C8360E7D2B4` reproduces the page tested under `342D64BAC2A39ADA`. Desktop/narrow captures fit. Existing real image/audio engine evidence remains above. Built only; site publication remains Damian's. |

The Studio builder rebuilds its HTML plug before producing the standalone
page. `studio-pickers-browser.mjs` and `studio-controls-browser.mjs` accept
`--html apps/landing/web/sparkstudio.html` and navigate with `file://`.
The packaged-byte checks cover startup, cancellation/retry, retained model
selection, LoRA input validation and result/error rendering, changed request
fields, explicit provider draft application, gallery ratings/saving, muted
audio playback, waveform seeking/analysis, deletion, regeneration refusal,
and scene/timeline writes. Every enabled button and editable field in the
exercised states is inventoried. Evidence for main 34215 is in blu's
`build-output/spark-file/package-controls` and `package-pickers`.

These control checks use canned provider responses, inference refusal stubs
and in-memory File System Access handles. They do not establish native OS
picker permission behavior, live CivitAI availability, real inference or the
deferred billed call. The separate real-engine proofs remain necessary.

**Ruled (Damian, 2026-10-01):** "for spark llm, the local model approach
should be expanded to link to a cloud provided one as well. as for our
testing, i don't care which one, as long as it fits on the gpu here on this
box. there's a qwen that does".

- The panel offers both the local model and a cloud-provided one behind the
  one provider interface; the provider list starts with Anthropic (the
  interface Prism stage 4 already defines) and stays open to more.
- The local test model is Ollama's downloaded `qwen3:latest` GGUF blob,
  `D:\AI\OllamaModels\blobs\sha256-a3de86cd1c132c822487ededd47a324c50491393e6565cd14bafa40d0b8e686f`
  (4.87 GB), which fits the box's 16,380 MiB RTX 4060 Ti; `qwen3.6:latest`
  (22.29 GB) does not. Only the file is used: the runtime is ours (wasm and
  WebGPU), not Ollama.

**Open for Damian:** the billed-call acceptance for step 3 (Prism stage 4's
key, deferred 2026-09-08); step 3 lands with its arms until he lifts it.
