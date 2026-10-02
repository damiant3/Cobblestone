# MusicGen on WebGPU: the engine behind Spark's audio tabs

Root, 2026-09-30: red owns the engine; blu's Music Director and Sound FX tabs
(`apps/spark/SparkAudio.codex`, `apps/spark/SparkStudio.md` section 7) refuse
Generate with their reason until it exists. The model is the original Spark's,
`facebook/musicgen-medium`. The engine runs in the page on the image engine's
kernels (`codex/foreword/gpu/BrowserKernels.codex`; that engine's design is
`docs/Designs/Active/Apps/InBrowserDiffusion.md`), and every stage is graded against the reference implementation, the way the
image engine is graded against Forge.

## What is on this box (2026-09-30)

| thing | where |
|---|---|
| musicgen-medium weights | `%USERPROFILE%\.cache\huggingface\hub\models--facebook--musicgen-medium\snapshots\<hash>\`: `state_dict.bin` (3,677,668,599 bytes, the language model), `compression_state_dict.bin` (236,001,935 bytes, EnCodec 32 kHz) |
| musicgen-small (same format, `state_dict.bin` 840,843,863 bytes) | beside it; the fast grading model for every stage before the last |
| the reference implementation | `D:\AI\audiocraft-main` with its own `venv\Scripts\python.exe` (audiocraft, `encodec` 0.1.1) |
| a second reference | HF transformers 4.46.1 in `D:\AI\DiffusionForge\system\python` (`MusicgenForConditionalGeneration`) |

Measured over all four files the engine reads (2026-09-30, Python's
`pickletools`): every zip entry is stored, none compressed, so `TorchZip`
needs no inflate; one `data.pkl` per file under a hash directory, the storages
beside it as `data/<key>`. The pickles use 21 opcodes (`PROTO`, `STOP`,
`GLOBAL`, `REDUCE`, `BINPERSID`, `MARK`, `EMPTY_DICT`, `SETITEMS`,
`EMPTY_TUPLE`, `TUPLE`, `TUPLE1`, `TUPLE2`, `TUPLE3`, `BININT`, `BININT1`,
`BININT2`, `BINUNICODE`, `NEWFALSE`, `BINPUT`, `LONG_BINPUT`, `BINGET`) and 4
GLOBALs (`collections.OrderedDict`, `torch._utils._rebuild_tensor_v2`,
`torch.HalfStorage` in the language models, `torch.FloatStorage` in
EnCodec): that is the allowlist ruling 1 closes.

Both `.bin` files are PyTorch checkpoints (a zip holding a pickle and raw
tensor storages), not safetensors.

## The model

A T5 encoder (t5-base) conditions a decoder-only transformer by
cross-attention. The decoder predicts 4 EnCodec codebooks per 50 Hz frame,
interleaved by the delay pattern (codebook k lags by k steps); the 4
embeddings are summed on input and 4 linear heads read the output. Sampling is
audiocraft's default: top-k 250, temperature 1.0, classifier-free guidance 3.0
(the cond and the null-text uncond in one batch). EnCodec's decoder turns the 4
codebooks back into 32 kHz audio: residual-VQ dequantize, then the SEANet
decoder (transposed conv1d upsampling and residual blocks, with a 2-layer
LSTM).

## Stages

Each stage lands graded, in this order.

| stage | capability | graded by |
|---|---|---|
| 0 | Graded: `TorchZip` reads flat state dictionaries and MusicGen's `best_state` wrapper into the records `CheckpointLayout` binds, natively and in the page | `build/torchzip-oracle.py --out <oracle.json> <four checkpoint paths>` runs in audiocraft's venv and checks tensor byte ranges against `torch.load`. `node build/torchzip-checkpoints.mjs <oracle.json> <native.cdx> <page.html>` compares every name, dtype, shape and absolute byte range. Compile `codex/test/apps/torchzip-checkpoints.codex` and `codex/plugs/html/arms/TorchZipArm.codex` for those artifacts. All four files match exactly (2026-09-30, main 33009); duplicate inputs and a shifted offset fail. `codex/test/torchzip-machine` grades the flat form and hostile cases, including an absent storage |
| 1 | Graded: EnCodec 32 kHz decodes fixed tokens from the real checkpoint to audio in the page: RVQ, SEANet and LSTM | `apps/spark/encodec-page.mjs` compares every audio sample with audiocraft's decode under that case's measured f16/f32 deviation. Ten zero/spread cases at 1, 2, 7, 8 and 17 frames pass; worst absolute error `9.05246e-7`. Negative tokens refuse and the residual-sign control fails (2026-09-30, main 33086) |
| 2 | Graded: t5-base encodes a prompt and applies the small or medium checkpoint's conditioning projection in the page | Fourteen singleton prompts against audiocraft; exact IDs, masks and relative-position buckets, all blocks and final outputs under the metrics below |
| 3 | Graded: original small and medium LM checkpoints, fixed codes and text conditioning to four heads' logits | audiocraft's nonstreaming `lm.forward`, every block and logit under the stage-3 contract below |
| 4 | Graded: cached decoding, CFG, top-k sampling and delayed codebooks for complete clips | Small and medium greedy/sampled clips at 8, 17, 50 and 129 frames match audiocraft exactly, including observed step/RNG counters; wrong seed differs |
| 5 | Spark's Generate calls the engine | `apps/spark` arms (blu's), one clip per tab |

For stage 0, create a scratch output directory and use the current depot seed.
The page build requires `codex/plugs/html/build-output/html-plug.cdx` from
`codex/plugs/html/build.ps1`. With that plug present, the artifacts are built by:

```powershell
pwsh build/compile.ps1 -Src codex/test/apps/torchzip-checkpoints.codex -Out <native.cdx> -Log <native.log> -Kernel seed/Codex.cdx
pwsh build/bundle-app.ps1 -Src codex/plugs/html/arms/TorchZipArm.codex -Out <page.codex>
pwsh codex/plugs/html/run.ps1 -Src <page.codex> -Out <page.html> -Compiler seed/Codex.cdx
```

## Stage 1 grade

The small checkpoint's RVQ step is graded on WebGPU (main 33035): zero and
spread tokens at 1, 2, 7, 8 and 17 frames match audiocraft's f32 latent values
exactly. Threads beyond the output leave sentinels unchanged; changing the
fourth codebook's selected index fails. The kernel uses constant work per
latent element and no per-element allocation.

`build/encodec-oracle.py <compression_state_dict.bin> --out <directory>
--device cuda`, run with audiocraft's Python, exports the codebooks, latent
values and audio. CPU mode omits the precision comparison. CUDA mode measures
the same reference model in f32 and f16 with TF32 disabled. The ten small-model
cases measured maximum absolute audio deviations from 0.000278153 to
0.001227379 (normalized amplitude, 2026-09-30). Those measurements concern
the specified cases and device. The assembled browser decoder is within each
case's bound. Small and medium compression checkpoints are byte-identical:
SHA256 `37D256B525D4117F8BBF790AB448A8E9F4746CD401900FE1AE72E154B1513A30`.

Build `apps/spark/EncodecKernels.codex` with `codex/plugs/wgsl/run.ps1 -Src
<source> -Out <kernels.wgsl>` using an existing WGSL plug, then run
`node apps/spark/encodec.mjs <reference-directory> <kernels.wgsl>`.
The installed EnCodec is non-causal: reflection padding and transposed-conv
trimming are split between left and right. Weight normalization, convolution
and ELU are graded primitives (main 33040): every decoder convolution's real
weights at input lengths 1, 3 and 9, including short reflection padding.
The error metric is `abs(actual-reference)/(1+abs(reference))`: at most `1e-6`
for normalized weights and ELU, `1e-5` for convolution. A compensated sum of
squares meets the normalization threshold. Missing convolution cases are
refused; shifted transpose trimming fails. Those primitive tolerances are
separate from the waveform precision measurement. The two-layer LSTM primitive
is graded at `1e-5` normalized error (main 33051): zero and nonzero initial
hidden/cell states at lengths 1, 3 and 9; first-layer first-step gates, both
layers' final states and every second-layer output. Overdispatch leaves the
checked gate, final-state and second-layer-output tails unchanged; erasing the
initial cell state fails. The assembled graph is `BrowserEncodec`, with the
TorchZip upload and normalization path in `EncodecLoad`. Required tensor shapes
and codebook ID bounds are checked before decoding; activation handles are
released as consumed. Original and normalized weights coexist until model close.

Build `codex/plugs/html/arms/EncodecArm.codex` through the same bundle and HTML
commands as stage 0, then run `node apps/spark/encodec-page.mjs <reference-directory>
<EncodecArm.html> <kernels.wgsl>`. The page picks the original checkpoint named
in the reference, decodes all cases and reads the audio back. Long-clip throughput
and allocation-failure paths are not graded by those short fixed-token cases.

`apps/spark/EncodecKernels.codex` supplies RVQ, weight normalization, conv1d,
transposed conv1d, ELU and LSTM primitives. Stages 3 and 4 below own the
language-model layout and sampling contracts.

## Stage 2 grade

`MusicT5Tokenizer` reuses the existing tokenizer tables and Unigram segmentation
with legacy T5 added-token splitting, terminal EOS deduplication and longest
charsmap matching across combining sequences. The latter follows
[SentencePiece NormalizePrefix](https://github.com/google/sentencepiece/blob/v0.1.99/src/normalizer.cc).
Thirteen browser cases match Transformers 4.39.3 `T5Tokenizer` IDs and singleton
masks exactly (main 33097), including empty versus whitespace-only text,
Unicode, special sentinels and decomposed accents. The shared Flux tokenizer is
unchanged. Missing cases and a changed tokenizer JSON hash are refused.

Generate the reference with audiocraft's Python:
`build/musicgen-t5-oracle.py <t5-base snapshot directory> --out <oracle.json>`.
Build `codex/plugs/html/arms/MusicT5TokenizerArm.codex` through the stage-0 HTML
commands, then run `node apps/spark/musicgen-t5-tokenizer.mjs <oracle.json>
<MusicT5TokenizerArm.html>`. The reference records library version and artifact
hashes.

`BrowserMusicT5` runs the twelve T5-base blocks with unscaled attention,
relative-position bias, RMS normalization and ReLU feed-forward layers.
`MusicT5Load` reads the encoder from the original t5-base safetensors and the
learned projection from the original MusicGen TorchZip checkpoint. Small
(1024 output channels) and medium (1536) are graded on fourteen singleton
prompts, including empty text and a 193-token case (2026-09-30).

For encoder fixtures, add `--lm-checkpoint <state_dict.bin>` to the reference
command, using a separate output directory for each checkpoint because tensor
fixture filenames are shared. Build `MusicT5EncoderArm.codex` through the HTML commands, and compile
`apps/spark/MusicT5Kernels.codex` and `codex/foreword/gpu/BrowserKernels.codex`
through the WGSL plug. Run `node apps/spark/musicgen-t5-encoder.mjs <oracle.json>
<arm.html> <t5.wgsl> <base.wgsl>` separately for the two checkpoints.

Raw block tensors use `abs(actual-reference)/max(1,reference-row-RMS)` at
`1e-4`; the RMS includes the model's `1e-6` epsilon. Final hidden and projected
conditioning tensors use `abs(actual-reference)/(1+abs(reference))` at `1e-4`.
The grader also reports raw component-relative error. Its original raw
component-relative threshold failed; row scaling accounts for large raw
activation magnitudes without relaxing the final-output threshold. Optional
`--precision` measures sensitivity to wider reference parameters and matrix
products; masks, variance and softmax remain f32. It is not a full-f64 oracle.
Reversing relative-position direction fails the condition-output check.
Inspection and production lifetime paths produce bit-identical conditions.

Activations are freed as consumed; inspection retains block outputs for grading.
Power-of-two capacities let different prompt lengths reuse buffer pools, with
padding overhead. Attention work and storage remain quadratic in token count.
The browser bridge retains freed GPU storage and grows its handle metadata;
these grades establish neither bounded long-run host memory nor peak VRAM.
Mixed-length batch padding remains ungraded; callers encode each prompt
separately. Allocation-failure behavior and long-prompt throughput remain open.

## Stage 3 grade

`BrowserMusicLM` sums four codebook embeddings, adds sinusoidal positions,
runs causal self-attention and text cross-attention, then projects the final
normalized state through four vocabulary heads. `MusicLMLoad` reads original
TorchZip weights, retains matrices in f16 storage and uploads normalization
vectors as f32. Dense matrix products and normalization statistics use compensated
f32 sums; attention, affine normalization and GELU use BrowserKernels.

Run audiocraft's Python with `build/musicgen-lm-oracle.py <state_dict.bin>
<stage-2 oracle.json> --out <directory> --precision`, in separate directories
for small and medium. The stage-2 reference must belong to the same checkpoint.
Build `MusicLMArm.codex` through the HTML commands and `MusicLMKernels.codex`
through the WGSL plug, then run `node apps/spark/musicgen-lm.mjs <oracle.json>
<arm.html> <lm.wgsl> <BrowserKernels.wgsl>`.

Six cases per model combine text or null conditioning with 1, 3 or 7 code
positions. Every raw block uses `abs(actual-f32)/(1+abs(f32)) <= 1e-4`.
For logits, the reference also evaluates the same quantized weights and fixed
f32 conditioning in f64. Let `d` be that case's maximum
`abs(f64-f32)/(1+abs(f32))`. Every GPU logit must be within
`max(1e-4,2*d)` of both references, using `1+abs(f32)` for both denominators.
The grader recomputes `d` from the saved arrays and checks the recorded value.
This is an empirical precision allowance for these inputs, not a mathematical
error bound. Original fixed-`1e-4` logit failures remain in the grade report.
CPU precision deviation reaches `4.28618e-4` for medium; the original fixed
bound does not describe the accepted medium proof (2026-09-30).

Both models pass this contract. The grader checks exact fixture shapes,
byte lengths, case inventory and fixed code patterns. Inspection and production
lifetime paths give bit-identical logits; reversing the causal mask fails
the head-output check under the same bounds. The page receives fixed codes
and conditioning, never expected block outputs or logits.

Dense work is proportional to sequence length times matrix size; self-attention
work and storage are quadratic in code positions. Activations are released
as consumed, with inspection retaining block states. These short cases do not
grade cache offsets, streaming, sampling, long-run memory or the assembled
tokenizer-to-audio path. Stages 4 and 5 own those generation checks.

## Stage 4 grade

`MusicSampling` implements the one-sample CUDA multinomial path for four
2048-way probability rows: Philox exponentials followed by f32 probability
division and argmax, following [PyTorch 2.1](https://github.com/pytorch/pytorch/blob/v2.1.0/aten/src/ATen/native/Distributions.cpp#L559-L590).
It reuses `NoiseGen`'s Philox. The graded profile is Torch 2.1.0+cu121 on the
RTX4060Ti with 34 SMs: 8192 threads, one counter per draw. The explicit draw
index corresponds to CUDA generator offsets `4*draw` before and `4*(draw+1)`
after. Other tensor shapes and device launch profiles are not claimed.

With audiocraft's Python, run `build/musicgen-sampler-oracle.py --out <directory>`.
Build `MusicSamplingArm.codex` through the HTML commands, then run
`node apps/spark/musicgen-sampler.mjs <oracle.json> <arm.html>`. Three probability
distributions, three seeds including high seed bits, and three consecutive draws give
exact token IDs against Torch. Every exponential value is within
`abs(actual-reference)/(1+abs(reference)) <= 1e-6`; changing the seed yields
different valid tokens. The oracle checks Torch's multinomial result against
its independent exponential draw and verifies identical generator state.
Fixture hashes, offsets and finite nonnegative probability rows are checked.

The primitive assumes positive row sums and valid probabilities; it does not
implement Torch's invalid-input API. Approximate exponentials do not promise
identical winners for arbitrary near-ties. Work is linear in the fixed shape,
with reusable numeric buffers; the diagnostic's balanced serialization costs
O(n log n).

`MusicLMStream` retains each layer's self-attention keys/values and precomputes
the fixed cross-attention keys/values. `MusicGeneration` keeps separate text
and null caches, applies CFG, temperature and top-k filtering, and advances
the Philox draw counter before masking delayed positions. Codebook k's frame t
is written at sequence position `t+k+1`, after the initial special tokens.

`build/musicgen-generation-oracle.py <checkpoint> <stage-2 oracle.json> --out
<directory>` calls actual audiocraft `LM.generate` in f32 on CUDA with TF32
disabled. The conditioner supplies the independently encoded text fixture and
zero null conditioning; this is not a fresh text-encoder run. Use a separate
directory per checkpoint. Build `MusicGenerationArm.codex` through the HTML
commands and the current `MusicLMKernels.codex` through the WGSL plug, then run
`node apps/spark/musicgen-generation.mjs <oracle.json> <arm.html> <lm.wgsl>
<BrowserKernels.wgsl>`.

Eight cases per model combine greedy or sampled generation with 8, 17, 50 and
129 frames (50 Hz). The longest crosses the 64- and 128-position attention
boundaries. Complete codebooks match exactly at seed 1234, CFG 3, temperature 1
and sampled top-k 250. The grader checks actual browser step order/count and
final RNG offset, all fixture hashes and token ranges. The wrong-seed control
must produce valid different codebooks. Saved guided logits diagnose a token
divergence; they have no independent numerical acceptance bound in this grade.

Generation accepts 1-1500 frames from a fresh start. A progress callback can
cancel by returning a negative value. GPU caches and step outputs are released
at completion or refusal; the loaded model remains caller-owned. Cancellation
is checked by a focused medium arm: after one sampled step and
the arm's model cleanup, no live GPU handles remain. Cache storage
is linear in frames and model width; attention work over a clip is quadratic
in frames, while dense projection work is linear in frames. Numeric host
scratch is allocated once per generation, not per step, and remains in the
Codex heap. Long-duration throughput, long-run memory, audio-prompt continuation
and other prompt/settings combinations remain ungraded. Stage 5 composes text
encoding and waveform decoding with the page's save/play workflow.

## Stage 5 engine API and grade

`Spark.MusicGenEngine` is the page-callable text-to-wave entry. The page supplies
four `MusicGenFile` records. Map `pick-file-then` callback JSON `name` to
`mgf-name` and `size` to `mgf-size`; `file` is the display filename, not the
opaque handle. The inputs are t5-base `tokenizer.json`, its `model.safetensors`, MusicGen
`state_dict.bin`, and `compression_state_dict.bin`. Put them in `MusicGenSources`
as `mgs-tokenizer`, `mgs-text`, `mgs-model`, and `mgs-audio`. `MusicGenShaders`
contains WGSL text in `mgh-base` (BrowserKernels), `mgh-text` (MusicT5Kernels),
`mgh-model` (MusicLMKernels), and `mgh-audio` (EncodecKernels).

The entry signatures are:

```text
musicgen-open : MusicGenSources, MusicGenShaders,
  (MusicGenEngine -> Integer), (Text -> Integer) -> Integer
musicgen-generate : MusicGenEngine, Text, MusicGenSettings,
  (Text, Integer, Integer -> Integer),
  (MusicGenAudio -> Integer), (Text -> Integer) -> Integer
musicgen-close : MusicGenEngine -> Boolean
```

Opening loads all four sources and returns an engine to the success callback;
the final callback receives a refusal reason. Generation's progress callback
receives phase (`encoding`, `generating`, or `decoding`), current and total;
return a negative integer to cancel. `MusicGenSettings` supplies `mg-frames`
(50 Hz, 1-1500), `mg-seed`, `mg-sampling`, `mg-top-k` (0-2048),
`mg-temperature` (0.1-2), and `mg-guidance` (1-10). Prompts may contain at most
512 tokenizer IDs. The maximum duration is 30 seconds from a fresh start;
the original application's 60-second continuation mode is not implemented.

Success returns `MusicGenAudio`: `mga-codes` holds codebook-major tokens and
`mga-wave` holds `mw-address`, `mw-bytes`, `mw-samples`, `mw-rate` and `mw-valid`.
The WAVE contains mono 32 kHz IEEE f32 samples without amplitude normalization,
an 18-byte format chunk and a fact sample count. Save it with
`file-save-bytes-then relativePath wave.mw-address wave.mw-bytes callback` after
`dir-open-then` successfully chooses the project output directory. The input
folder picker `pick-dir-then` does not establish that writable directory.
The save callback receives JSON containing
`ok:true`, `name`, `path` and `size`, or `error`; `name` works with `file-url`
and `file-read-then`. The page owns catalog updates and playback.

Binary save snapshots bytes and the chosen directory at invocation. Calls are
serialized within the page and refuse an existing destination, invalid relative
path or byte range. This does not provide atomic exclusion against another tab
or external writer. Repeated and concurrent `gpu-open-then` calls share the
device; a failed open permits retry. Engine close refuses while busy, is
idempotent afterward, and releases its GPU handles while preserving other
callers' handles. A completed or cancelled generation leaves loaded weights
available for reuse. Recorded GPU errors cause refusal, including pre-existing
errors on the shared device; the engine does not clear another caller's error.

With audiocraft's Python, run `build/musicgen-audio-oracle.py <state_dict.bin>
<compression_state_dict.bin> <t5-base snapshot> --out <directory>`.
Build `MusicGenEngineArm.codex` through the HTML commands, then run
`node apps/spark/musicgen-audio.mjs <oracle.json> <arm.html> <base.wgsl>
<text.wgsl> <model.wgsl> <audio.wgsl>`. The reference runs actual T5 conditioning,
LM generation and EnCodec decoding on CUDA f32 with TF32 disabled. The browser
receives prompts, settings and original files, not reference tokens or audio.

The medium grade covers one-second music and SFX prompts at seed 1234,
temperature 1 and CFG 3, plus an eight-frame music case at seed 42,
temperature 0.8 and CFG 4, all top-k 250. Every token must match exactly;
every finite sample must differ by at most 1e-4 absolute. The final run's
largest difference was 9.53675e-7 (2026-09-30). The diagnostic checks input/audio
hashes and case inventory, independently parses saved WAVE chunks and decodes
the files with the browser audio decoder. It also checks cancellation at the
first generation step followed by retry, busy/closed guards, invalid settings,
an injected recorded GPU error, and preservation of a pre-existing GPU buffer.
The final engine grade passes this contract.

`node codex/plugs/html/arms/audio-io.mjs` separately checks exact binary saves,
byte/directory snapshots, same-path queued refusal, invalid paths/ranges,
missing directory, open retry and concurrent device reuse. Both diagnostics
use origin-private filesystem handles; user picker interaction, audible
playback and Spark Studio controls remain the page integration's proof.
Malformed checkpoint failures, allocation/device loss, other cancellation
phases and long-duration throughput remain ungraded.

The wrapper adds linear tokenizer copying and waveform serialization. Its
tokenizer storage, generation scratch and returned WAVE bytes remain in the
Codex heap; close releases GPU handles, not host storage or pooled GPU buffers.
Long-run bounded memory is not established. Compiler heap/time behavior is
unchanged. Blu's `SparkStudioPage` generation/save/catalog/playback,
IEEE-f32 analysis and original rating/batch/category policies are graded
separately in `apps/spark/SparkStudio.md` section 7. Main 33360 includes the
current packaged page and the HTML Integer random-seed correction. The page
integration grade passes 46 arms (2026-10-01), including unmocked random
seeds and original-analyzer catalog metadata. The engine grade alone does
not establish those page behaviors.

## Rulings (root, 2026-09-30)

1. **The weights are read by `TorchZip`, in Codex.** The zip central directory,
   then the pickle under a closed allowlist: only the opcodes `torch.save`
   writes for a state dict, and only the GLOBALs that rebuild a tensor
   (`torch._utils._rebuild_tensor_v2`, `collections.OrderedDict`) and name a
   storage type (`torch.FloatStorage`, `torch.HalfStorage`, `torch.BFloat16Storage`
   and the rest of that family). Any other opcode or GLOBAL is refused by name.
   A hostile-pickle arm (a GLOBAL outside the list, a REDUCE of anything but a
   rebuild, a persistent id naming no storage) must be refused, and stage 0 is
   not done without it. It also opens Forge's older `.bin` and `.ckpt` files.
2. **Port Torch's multinomial:** sampled clips must match audiocraft's draw
   for draw for a fixed seed, as the image noise matches torch's.
