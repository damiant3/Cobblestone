# GitHub Update 66

Pushed from main 34272 plus its release documents. The release seed is
`267B6C8360E7D2B4` (`TechnicalDetails.md` carries the full digests). Numbers in
parentheses are main changelists.

## Spark Studio and the in-browser LLM

- **A local Qwen3 model runs in a WebGPU tab and Studio can call the model as a provider**: the GGUF parser and BPE tokenizer are graded against llama.cpp (red 34235), Q4_K and Q6_K dequantization is bit-identical to gguf-py (red 34243), the matvec kernels are graded against gguf-py float64 (red 34245), and the one-token decoder yields 12 greedy tokens equal to llama.cpp with a log-probability bound (red 34251); the Studio local provider follows (red 34259).
- **Shared provider, writing assistant and new-project wizard**: the canonical Prism provider is packaged with namespaced key storage and abort guards, with no network call in the proof (blu 34103); the writing assistant has an explicit Apply and no external request (blu 34120); the wizard's browser arm passes 21/21 (root 34238).
- **File-origin controls**: cancel-safe pickers keep project and model state (blu 34175); disk control feedback, safe LoRA input validation and real preset choices are built, with inference, providers and file handles still fixtures and native dialogs unproved (blu 34207, 34215).
- **SDXL inside Studio**: Concept Art through the browser engine, a real 1024 image at mean 0.754852/255 against native (blu 33400); 1344 landscape and portrait presets at native mean 0.59894/255 (blu 33505, 33520); DPM++ 2M at mean 1.308/0.755 against the 4/255 gate (blu 33666); SDE, 2M SDE, Heun and 3M SDE choices, each with a real 1024 image under native mean 4/255 (blu 33709, 33719); LoRA for SD1.5 and SDXL, the real XL image at native mean 0.846 (blu 33739, 33740, 33811, 33816).
- **Studio step 1 and the CivitAI steps**: the request builder, prompt-file parser, catalog, document store, preferences, job queue, ratings, thumbnails and project page are each graded against the WPF original (reek 32535, 32539, 32557; blu 32604, 32622, 32639 to 32659 by twos, 32672, 32703, 32726); model catalogue, CivitAI search and trigger words by SHA256 are graded against the original (blu 32740, 32811, 32822, 32828, 32831).
- **Audio, video and 3D in Studio**: MusicGen Generate, cancel, WAVE save and playback, with 22 real-medium arms and 25 Studio arms (blu 33271, 33308, 33360); IEEE-f32 WAVE analysis (blu 33324); waveform, spectrum and playback synchronization (blu 33004, 33017); music analyzer rules (blu 32855, 32868, 32903, 32919, 32926, 32933); video composition at 17 arms (blu 33066); a textured 3D scene editor against a WebGL interior-pixel reference (blu 33093). Landing cards were refreshed (blu 33247, 33336).

## Diffusion in a browser tab (WebGPU)

- **SDXL runs end to end in the tab**: dual CLIP, weighted multi-chunk conditioning, the UNet block grade and the full pipeline at mean RGB error 0.755/255 under the limit of 4 (red 33276, 33294, 33328, 33365), with a chooser for SD1.5 and SDXL (red 33426) and landscape and portrait sizes (red 33463).
- **DPM++ samplers**: six SDXL samplers plus Euler meet native mean RGB error below 4/255 (red 33603); job-local noise banks leave PNGs unchanged (red 33680); SD1.5 DPM++ is graded on eight native images (red 33839).
- **LoRA in six formats**: standard SD1.5 (red 33714), SDXL with real-file native parity (red 33764), BF16 and FP8 at max mean 0.802445/255 (red 33885), LoCon at worst mean 0.860442/255 (red 33917), LyCORIS over 28 images and 71 faults (red 34016), and DoRA over 44 images and 114 failure controls with 35 old PNGs exact (red 34127).
- **Forge comparison closes the dark 3M image**: the original Forge reproduces the dark six-step Karras image, browser against Forge at mean 0.281555/255, Euler control 0.499235 (red 34153, 34154).
- **Faster**: batched launches, one-launch softmax, tiled attention, split-K convolution and flash attention at the 64x64 level (red 32680, 32700, 32714, 32723, 32750, 32776); multi-chunk prompts are graded against Forge at 1, 2 and 3 chunks (red 32852).

## MusicGen in a browser tab

- **Real checkpoint to audio, graded stage by stage**: tensor tables match torch.load (red 33009), RVQ, EnCodec convolution, LSTM and waveform match audiocraft on fixed-token cases (red 33035, 33040, 33051, 33086), the T5 tokenizer passes 13 exact cases (red 33097), the T5 encoder passes small and medium grades (red 33143), the LM matches dual-reference logits (red 33187), and cached whole-clip generation gives exact greedy and sampled tokens (red 33201, 33220, 33242). A TorchZip reader refuses hostile pickles by name (red 32908).

## GPU / GSP stack

- **SM 5.2 opcode encoder and plug**: control packer, four encoder batches and the first plug slice (reek 32667, 32675, 32691, 32696, 32710, 32729), graded on maxas; SASS accumulation is linear, heap 269779064 to 1310864 bytes for 8192 emits (reek 32900).
- **GSP stage 3 payloads graded against original tinygrad and nouveau writers**: QMD, MMU, GPFIFO, command queue, boot arguments, registry, RM allocations, channel parameters, RAMFC, ACR, WPR, FECS and GPCCS, GR init lists, ELF, booter and VBIOS/FWSEC parsers, each with a corruption control (reek 32743 to 32792, 32837, 32846, 32859, 32871, 32940, 32948, 32959, 32967, 32986, 32988, 32990); the prepared Ada boot sequence matches a tinygrad trace in 14 scenarios (reek 33046), and system-info allocation fell from 42085352 to 83096 bytes (reek 33057).
- **Stage 2 first slice**: SM89 control fields and F16 HMMA encodings graded against turingas and NAK, host-only (reek 34229).

## The compiler

- **Closed defects**: an undecidable proof type is refused with CDX4025 (COMPILER-110, red 32887); UEFI slices are initialized (COMPILER-111, reek 33130); timer selection rotates (COMPILER-112, fester 33111); a negative process-kill PID is refused instead of overwriting a sentinel (COMPILER-113, reek 33300); constructor return annotations are refused with CDX1080 (COMPILER-116, reek 33976); a retained empty fidelity list survives CHECK scratch rewind (COMPILER-117, reek 34132).
- **128-column layout** (COMPILER-109): width set to 128 (reek 33637) and the parser, scoper and resolver expressions rewritten token-identical (reek 33555, 33571, 33585, 33653, 33730, 33733, 33744, 33753, 33761, 33773, 33794, 34028, 34185); code layout stage 4 over the plugs, OS, boards and apps (blu 32519, 32571; reek 32491).

## Games

- **Magic nine-set pool fully modelled**: unmodelled cards fell from 110 to 0 of 404, each with a sabotage-red arm and eight demos byte-identical (val 32487, 32864 and the cards between); AI play replaced inert cards (val 32882, 32911, 32929, 32944, 32951, 32956, 32963, 32971, 32977, 32981), and a Channel resolution trap was fixed (val 32719).
- **CodexMagic interaction**: phase freezes, manual spell and payment decisions, Diamond amplification, Generals' loyalty, a disruption window, Pause now and queued orders (val 33148 to 33986, native counts rising to 260 native/150 API); four skill profiles, where Master ties Expert (val 33929); unique per-copy card identity (val 33352); starter decks corrected over 3000 seeds (blu 33578, 33631).
- **Arcade and boards**: Poker Variants, Crazy Eights (after pagat) and Spider arms (val 32574, 32594, 32596), the backgammon bear-off tray (red 32606) and the star map (red 32631).

## Desk and OS

- **Desktop**: Settings typed persistence and GUI (blu 33855, 33954), BMP wallpaper (blu 34004), stored high contrast (blu 34092), a visible focus ring (blu 34035), accessibility roles and Settings keyboard (blu 34032, 34069), a Windows-key menu and triple Ctrl-Alt-Delete reboot (fester 33944).
- **Process pool and workers**: a firmware-owned process pool (fester 34159) and a persistent render worker in 3D View and Aquarium (fester 34057), graded in the native and OVMF beds; physical acceptance stays open, except GUIOS image 90B8CEFA (fester 33405).
- **WORKS-81 Web Server admin, DHCP binding and logs** (fester 33787), **WORKS-80** (blu 33438) and **WORKS-77** (blu 33479) are fixed.

## Landing site and pages

- **Landing site** from disk with grouped Try It cards, 58 pages passing under file:// (root 34248); **C64** loads from a file URL, with a PRINT2 limitation recorded (val 34195).

## Build and test tooling

- **JSON string escaping** shared across WebRoute and CodexMagic (blu 33691); the HTML JSON control fix (blu 33780); SdExplorer through the shared encoder (red 34233).
- **Chain content-extent oracle** with mutation arms (reek 33995); a corrected focus oracle (blu 34143); a tool-catalog check trigger (root 33472).

## Release proof

The full gate ran every phase against the release seed. Its compiler stages
were green first time: the seed compiled from the release source is a hard
fixed point in one pass and equals the shipped seed byte for byte. Three reds
past those stages were fixed rather than waived, none of them a compiler
defect: `flux-step` and `flux-euler` stream the 11.07 GB Flux checkpoint from
the build box's hard disk and take 71 s with exact output, over the 60 s test
budget, and now carry `.slow` (34267); two diffusion reference chapters compile
only through their own runners and were baselined with the runner named, the
split recorded as BROWSER-DRIVER-ENTRY (34269); and
`check-empty-fidelity-lifetime` grades the compiler's serial phase trace, which
hosted wasm has no port for, and is declared bare-metal (34272). After the
fixes: every test chapter compiling and running against its `.expected` (the 17
other GPU subjects over budget with 16 guests on one GPU passed run one at a
time), 335 of 338 apps clean with the 3 baselined units, 1,144 hosted wasm
programs passing with 6 known reds, deck headroom (tightest margin 2.13), the
page builds and the browser checks. IR fidelity graded its controls and reported
no unexpected result.

The battery (`-Tier all`) on the release seed passed 2,165 of 2,239 tests with
62 declared skips; the vector and CCE oracles agreed with the host (130 vector,
1,485 CCE with 31 documented gaps). Its 12 failures were all GPU diffusion tests
over their wall budget with 13 guests sharing one GPU; run one at a time, all 12
passed. The poison battery, on a seed built from the release source with a 0xCD
fill, passed 2,166 with no uninitialized-field fault; its 11 failures were the
same GPU wall budget, and all 11 passed one at a time.

Roslyn built the freshly emitted C# compiler. Both DDC arms produced 3,823,306
bytes with zero differences from the release seed outside offsets 40..135. The
symbol map matched all 6,380 embedded MAP1 rows by name, address and size.

Both boot images were rebuilt on the release seed; `seed/Codex.img` reproduces
byte for byte across two builds. All 59 diagnostic rehearsal arms passed across
Codex VM and QEMU/OVMF with a 180-second minimum arm allowance, and the
shipping check confirmed the default configuration. The diagnostic image
SHA-256 is `368B40D327E6B16C3221285F498A43579DC8855C431CA691512B5D074D399311`.

The box sampler (`docs/Agents/box-release-2026-10-02-u66.csv`) recorded a
free-memory floor of 15.96 GiB at 03:41, in the battery's compile phase with the
DDC emit beside it, and the peak of 15 guests in the same sample, at about
0.87 GB each.
