# Library Gap Analysis — UI and Foreword Layers

**Author**: Pip
**Date**: 2026-05-08 (depot HEAD CL 1199)
**Status**: Survey + proposal — for review
**Scope**: `codex.foreword.ui/` (18 modules) plus the foreword quires
proper (`codex.foreword/` + `codex.foreword.{game,ai,signal,compress,
encode,math,sim}/`) — 295 modules across 9 quires, ~25 KLOC of Codex
prose and code

## Methodology

For each quire I checked module roster, per-module size, the high-level
shape of representative chapters, what cites what, and whether the
existing body of work covers the natural scope of that domain or
leaves obvious holes. I am explicitly *not* doing a line-by-line
implementation review — that's a different document. This is about
shape, completeness, and cross-quire connective tissue.

"Gap" here means one of three things:

- **Missing chapter** — a concept that should exist as a Codex
  Chapter and doesn't.
- **Thin chapter** — a Chapter that exists but doesn't cover the
  domain it claims (e.g., a 100-line `NeuralNet.codex` that names
  itself for a much bigger problem).
- **Missing bridge** — two existing chapters that should `cite` each
  other but don't, leaving the user to reinvent the connection.

Counts and sizes are from depot HEAD (CL 1199).

## Strengths Worth Naming First

Before listing gaps it's worth being explicit about where this layer
is *strong*, because the design should preserve those properties.

- **Data structures are dense and idiomatic.** `codex.foreword/`
  carries Hamt, BPlusTree, Trie, IntervalTree, ConsistentHash,
  BloomFilter, CountMinSketch, RingBuffer, Rope, Deque, Set, KvStore,
  EventBus, LruCache, RateLimiter, TimingWheel, UnionFind, Graph,
  PriorityQueue, Queue, Pair — the persistent-functional vocabulary
  is well-stocked.
- **Crypto is real.** Ed25519, SHA-256/512, ChaCha20, AES, HMAC,
  DiffieHellman, Hmac, Tls, ProofOfWork. The "if we didn't build it
  we don't trust it" rule is honored.
- **Networking is full TCP/IP.** Already documented in CurrentPlan
  but worth restating: Ethernet/ARP/IPv4 + TCP 9-state +
  UDP/ICMP/DNS/DHCP/NTP/Syslog/TFTP/HTTP. Not a toy.
- **Encode quire is broad.** 28 modules covering JSON, Base64, Hex,
  URI, UUID, CSV, CRC32, Bencode, Protobuf, plus the entire media
  zoo: Avi, Bmp, Flac, Gif, Jpeg, Markdown, Midi, Mp3, Mp4, Ogg,
  Png, Qoi, Smtp, Tiff, VideoCodec, Wav, WebSocket. This is a real
  bet.
- **Game quire is comprehensive.** Pathfinding (A*/Dijkstra/FloodFill),
  spatial trees (Quadtree, Octree), terrain (DiamondSquare, HexMap,
  TileMap, Voronoi), CA, ECS, state machines, cards, graphics
  primitives — everything you'd reach for in a 2D engine.
- **UI substrate has the right pieces named.** Theme, BoxModel,
  Layout (flex), Widget, Surface, Render, Event, Binding, Animation,
  Icon, Font, Cursor, Scroll, Focus, Dialog, Overlay, Sound,
  Orchestrator. The architecture (themed-via-data, central
  orchestrator loop, dirty-binding propagation) is clean.

The per-quire gaps below should be read against this baseline: the
foundations are in place, what follows is what's missing or shallow.

---

## UI Quire (`codex.foreword.ui/`)

Eighteen modules, 4318 LOC. The orchestrator + binding + dirty-flag
shape is in place; what's missing are the *forms* that surround
that loop.

### Missing chapters

- **`TextField.codex`** — there is no text-input widget. `Widget.codex`
  is 295 LOC; `Event.codex` knows about input events; but actually
  letting the user *type* into a field, with caret, selection,
  insert/delete, IME composition state, is not present.
- **`Selection.codex`** — selection model: range, multi-range,
  click-and-drag, shift-click extension, double-click-to-select-word.
  Currently there is nothing.
- **`Clipboard.codex`** — clipboard read/write effect (`[Clipboard]`).
  Required by anything that copies/pastes; absent.
- **`Drag.codex`** — drag-and-drop event semantics + drop zones.
  Absent.
- **`Accessibility.codex`** — semantic roles, alt text, focus-order
  hints, live-region announcements. The trust lattice could
  eventually verify accessibility coverage; today nothing.
- **`Locale.codex`** — locale, RTL/LTR direction, number/date
  formatting per locale, plural rules. Codex is meant for satellites
  and surgical robots — but the UI substrate as currently shaped is
  English-only LTR.
- **`Window.codex`** — multi-window / multi-pane / tab management.
  `Surface` is one display surface; nothing manages a window
  hierarchy across them.
- **`Touch.codex`** — touch gestures (pinch/swipe/multi-touch) above
  the raw event level. The phone target needs this.
- **`RichText.codex`** — styled text spans, hyperlinks within text,
  inline icons. `Font.codex` does monospace bitmap glyphs; rich
  text is a different layer.
- **`Vector.codex`** — vector graphics primitives (paths, beziers,
  fills, strokes). `Render.codex` is pixel-based; vector is missing.
  `codex.foreword.math/Bezier.codex` exists but doesn't bridge to
  rendering.
- **`Charts.codex` / data-viz** — bar/line/scatter/heatmap. Builders
  building admin UIs will need it. (Or this could live in
  `codex.foreword.game/` as a sibling to Rasterizer — the game
  graphics primitives apply.)
- **`Shadow.codex` / `Effect.codex`** — drop shadow, blur, blend
  modes, glow. `Surface.codex` does compositing; effects are absent.

### Thin chapters worth deepening

- **`Widget.codex` (295 LOC)** — names `Widget` but the *catalog* of
  concrete widgets (Button, Checkbox, RadioButton, Dropdown, Slider,
  TextField, NumberField, DatePicker, ColorPicker, ListView,
  TreeView, TableView, ProgressBar, Toolbar, Menu, ContextMenu,
  Tabs, Accordion) is not enumerated. This is the *content* of
  the UI surface; right now it's mostly the framework around an
  empty room.
- **`Layout.codex` (111 LOC)** — Flex direction is implemented;
  Grid, Stack, ScrollView-as-layout, Splitter, AspectRatio,
  Positioned (absolute), Aligned are not. Flex alone is not
  sufficient for production UI.
- **`Dialog.codex` (84 LOC)** — modal frame; the dialog *kinds*
  (alert, confirm, prompt, file-open/save, color-picker, font-picker,
  print) are absent.
- **`Sound.codex` (142 LOC)** — UI sound queue; the *audio rendering*
  side belongs in `codex.foreword.signal/Synth.codex`, but
  there's no `cite` between them today (see bridge gaps below).

### Missing bridges

- `Render` → `Encode` — to draw a JPEG or PNG you need to decode
  it. `codex.foreword.encode/{Jpeg,Png,Bmp,Gif,Qoi}.codex` exists;
  `Render.codex` has no `cite` to any of them. Without that bridge,
  every app rolls its own image-loading path.
- `Render` → `math/Bezier` — vector strokes need beziers. Bezier
  exists; Render doesn't reach for it.
- `Sound` → `signal/Synth` and `signal/AudioEffect` — UI sound
  effects could cheaply use the synth quire. Today they are
  presumably PCM blobs from Wav.
- `Animation` → `math/Spline` — animation tweens via splines are a
  natural fit; not connected.

---

## Foreword Proper (`codex.foreword/`)

Seventy modules, ~14 KLOC. The bones of the language. Strong on
data structures and crypto; gaps cluster in three places: numerics,
text/i18n, and modern-async.

### Missing chapters

- **Numerics**
  - `Decimal.codex` — fixed-point decimal arithmetic for currency
    and audit-precision contexts. Bounded `Integer between L and H`
    helps but doesn't replace fractional accuracy.
  - `Float.codex` — Codex internally uses fixed-point scale-1000
    in many places. There is no first-class IEEE-754 chapter for
    code that genuinely needs floats (signal processing, graphics).
    This is partial — `codex.foreword.math` modules use float-like
    semantics without naming them.
  - `BigInt.codex` — promotable arbitrary-precision integers. The
    bignum behavior currently embedded in `Ed25519.codex` (per
    `KNOWN-CONDITIONS.md`) is value-dependent-time and tied to that
    one chapter. A first-class BigInt would let crypto and numerics
    share a hardened core.
- **Text / i18n**
  - `Locale.codex` — locale identity, locale-aware text comparison.
  - `Format.codex` — printf-style formatter on top of `show` /
    `integer-to-text`. Currently every consumer rolls their own.
  - `Unicode.codex` — Unicode normalization (NFC/NFD/NFKC/NFKD),
    casefolding, grapheme-cluster iteration. CCE is internal; *at
    boundaries*, callers need this.
  - `Plural.codex` — locale-aware plural rules.
- **Crypto**
  - `Argon2.codex` / `Scrypt.codex` / `Bcrypt.codex` — password
    hashing. Password-equivalent secrets exist (identity seeds),
    but a memory-hard KDF for human-chosen secrets is absent.
  - `Hkdf.codex` — HMAC-based key derivation. Required by TLS 1.3
    and most modern key-schedule constructions; not present
    despite `Hmac.codex` existing.
  - `X25519.codex` — Curve25519 ECDH. We have `DiffieHellman.codex`
    (likely classic finite-field) and `Ed25519.codex` (signing), but
    not the modern key-exchange counterpart.
  - `AesGcm.codex` — authenticated encryption. `Aes.codex` exists;
    a GCM mode wrapper does not.
- **Concurrency**
  - `Future.codex` / `Promise.codex` — composable lazy values. The
    CAMP-IIIC fork/await primitives are at a lower level; there's
    no value-level Future abstraction in the foreword.
  - `Mutex.codex` / `Semaphore.codex` — strictly a kernel-OS
    concept already in `codex.os.sched`, but a foreword-level
    abstraction (built on Channel) is sometimes simpler.
- **System**
  - `Path.codex` — filesystem path manipulation (join, split,
    normalize, extension). `FileSystem.codex` exists; path math
    is folded into it inconsistently.
  - `Process.codex` — process control surface at the foreword
    level. Exists at the kernel level (`codex.os.sched`); a
    foreword-level ergonomic wrapper is missing.
  - `Env.codex` — environment variables / config dictionary.

### Thin chapters

- **`Logger.codex`** (size unknown without reading; likely thin) —
  structured logging with levels + sinks. Would benefit from a
  level-policy + redaction story.
- **`Random.codex`** — needs a seeded-RNG vs system-RNG distinction
  (the latter being effectful via RDRAND); the policy on which is
  default is worth designing explicitly.

### Missing bridges

- `Tls.codex` → `Hkdf` / `X25519` / `AesGcm` — modern TLS uses all
  three. Without them, `Tls.codex` is restricted to older suites.
- `FileSystem.codex` ↔ `Path.codex` — the missing chapter above
  would untangle this.

---

## AI Quire (`codex.foreword.ai/`)

Twelve modules, 1498 LOC. The smallest "named-domain" quire and
also the one furthest from completeness for its own scope.

### Missing chapters

- **`Attention.codex`** — scaled-dot-product attention; multi-head.
  The single biggest gap given the agent-runtime ambitions.
- **`Transformer.codex`** — encoder/decoder layer (LayerNorm + MHA
  + FFN). Without this, "AI runtime" is a shell.
- **`Embedding.codex`** — token-id → vector lookup; positional encoding.
- **`Optimizer.codex`** — SGD, Adam, AdamW, Lion. NeuralNet.codex is
  101 LOC; there's no place where a real optimizer lives.
- **`Loss.codex`** — CrossEntropy, MSE, Hinge, KL.
- **`Sampling.codex`** — top-k, top-p, temperature, repetition
  penalty. Required by any text-gen agent runtime.
- **`KvCache.codex`** — KV cache for autoregressive inference.
  Without this, an LLM is unusable at any reasonable speed.
- **`Quantize.codex`** — int8/int4 quantize/dequantize. Gguf.codex
  *reads* quantized weights; there is no companion module for the
  arithmetic that operates on them.
- **`BPE.codex` / `WordPiece.codex` / `SentencePiece.codex`** —
  Tokenizer.codex is 86 LOC and likely covers byte-level only. The
  algorithms each agent model wants live somewhere else today —
  i.e. nowhere.

### Thin chapters

- **`NeuralNet.codex` (101 LOC)** — names itself for a problem 100×
  bigger than 101 LOC of Codex. This is currently a forward-pass
  stub; backprop, batching, LR scheduling are absent.
- **`Tensor.codex` (133 LOC)** — fixed-shape multidim tensor with
  scalar ops; broadcast, matmul, reductions, slicing, view/reshape
  are partial. The companion `GpuKernels.md` design doc is the
  right place for these to gain teeth (GPU execution); the CPU
  reference path here is shallow.
- **`Tokenizer.codex` (86 LOC)** — see above.

### Missing bridges

- `NeuralNet`/`Tensor` → `GpuProxy` / future `GpuKernels` — Tensor
  ops should be transparently dispatchable to GPU when available,
  CPU when not. This is one of the explicit open questions in
  `GpuCompute.md`. The bridge needs a name.
- `Gguf` → `Quantize` (proposed) — Gguf reads quantized weights
  but the quant arithmetic lives nowhere.

---

## Math Quire (`codex.foreword.math/`)

Nine modules, 1171 LOC. Geometry is the heavy chapter (426 LOC);
the rest are small focused chapters.

### Missing chapters

- **`LinearAlgebra.codex`** — Gaussian elimination, LU decomposition,
  QR, SVD, eigenvalues. Matrix3/Matrix4 are size-fixed; general
  N×N solvers are absent.
- **`Numeric.codex`** — root-finding (bisection, Newton), numerical
  integration (Simpson, Gauss-Legendre), ODE solvers (RK4).
- **`Optimize.codex`** — gradient descent, simplex, BFGS. Different
  from `ai/Optimizer` — that one is for ML training, this is for
  math problems.
- **`Probability.codex`** — distributions (Normal, Poisson, Binomial,
  Beta, Gamma). `codex.foreword/Statistics.codex` exists but is
  presumably summary statistics, not distributions.
- **`NumberTheory.codex`** — GCD/LCM (likely already in Cordic or
  somewhere), modular exponentiation, prime sieve, factor.

### Thin chapters

- **`Spline.codex` (61 LOC)** — splines beyond what's there
  (B-spline, NURBS, Hermite) for graphics + animation.

---

## Sim Quire (`codex.foreword.sim/`)

Four modules, 459 LOC. The thinnest specialized quire.

### Missing chapters

- **`SpatialHash.codex`** — broadphase collision via spatial
  hashing. Octree + Quadtree are in `game`; a hash-grid alternative
  for dense particle systems is missing.
- **`Constraint.codex`** — distance constraints, springs, joints.
  Required by any soft-body or articulated simulation.
- **`Cloth.codex`** / **`Rope.codex` (sim)** — distinct from the
  foreword `Rope.codex` which is the data structure; sim/Rope
  would be a 1D Verlet chain.
- **`Kinematics.codex`** — IK chains, FK helpers.

### Thin chapters

- **`Physics.codex` (96 LOC)** — Verlet integration is mentioned
  in README but the chapter is small. Rigid body dynamics, restitution,
  friction are absent or shallow.
- **`Steering.codex` (131 LOC)** — Reynolds steering behaviors are
  named; flock + crowd dynamics on top would add real capability.

---

## Compress Quire (`codex.foreword.compress/`)

Three modules, ~thin. The smallest quire by far.

### Missing chapters

- **`Deflate.codex`** — LZ77 + Huffman composed, the de-facto
  standard. Both pieces exist; the composition does not.
- **`Gzip.codex`** — Deflate with the gzip wrapper. Required for
  almost any web content negotiation.
- **`Zstd.codex`** — modern general-purpose compressor.
- **`Lz4.codex`** — speed-tier compressor for hot paths.
- **`Brotli.codex`** — web-tier compressor (HTTP content
  negotiation expects it).
- **`Bzip2.codex`** — Burrows-Wheeler tier; less critical but
  occasionally needed.

The 3-module surface is fine if the compressed-output ambitions are
small. If Codex.OS is going to ship over the network at any
scale, gzip and zstd at minimum are not optional.

---

## Encode Quire (`codex.foreword.encode/`)

Twenty-eight modules. The most comprehensive quire by count. The
gaps here are configuration-format-modern and one or two newer
media formats.

### Missing chapters

- **`Yaml.codex`** — Csv/Ini/Json present, Yaml is not. (Some
  argue intentionally.)
- **`Toml.codex`** — modern config; absent.
- **`MessagePack.codex`** / **`Cbor.codex`** — binary
  schema-less serialization. Protobuf is schemaful; both binary
  schemaless options are missing.
- **`Plist.codex`** — Apple property list (binary + XML).
- **`Heic.codex` / `Avif.codex`** — modern image formats. Jpeg/
  Png are covered; HEIC and AVIF are increasingly common.
- **`Opus.codex`** — modern low-latency audio codec; Mp3 is
  covered.

### Thin or worth-revisiting

- **`Markdown.codex`** — rendering vs parsing depth not surveyed
  here; markdown-as-output for the UEFI dev console may be a
  future need.

---

## Game Quire (`codex.foreword.game/`)

Twenty-two modules, 2988 LOC. Most complete domain quire. Few
named gaps.

### Missing chapters

- **`Camera.codex` (game)** — game-camera (orthographic, perspective,
  follow, shake). Distinct from `codex.foreword/Camera.codex` which
  is a hardware camera capability.
- **`Inventory.codex`** — inventory + equipment slots; common
  enough across genres to deserve a chapter.
- **`SaveSlot.codex`** — save/load with versioning. `Gguf.codex`
  has serialization patterns to crib from; game saves want their
  own shape.
- **`Netcode.codex`** — rollback netcode, client-side prediction.
  Networking exists but game-specific netcode does not.

(These are nice-to-haves more than load-bearing gaps. The Game
quire is in healthier shape than every other domain quire.)

---

## Signal Quire (`codex.foreword.signal/`)

Ten modules, 1343 LOC. Reasonable for audio synthesis + analysis.

### Missing chapters

- **`Filter.codex`** — IIR/FIR digital filters (low-pass,
  high-pass, band-pass). Convolution exists but generic IIR is
  missing.
- **`Wavelet.codex`** — discrete wavelet transform.
- **`Pitch.codex`** — pitch detection (autocorrelation, YIN).
- **`Resample.codex`** — sample-rate conversion (sinc + windowing).

### Thin chapters

- **`Envelope.codex` (64 LOC)** — ADSR is the table-stakes envelope;
  AHDSR, multi-stage, looping, modulation routing are absent.

---

## Cross-Cutting (Bridges Between Quires)

The single most expensive class of gap is *connective tissue* —
two quires that should `cite` each other and don't. A short list
of the highest-impact missing bridges:

| From | To | Why missing today bites |
|---|---|---|
| `ui/Render` | `encode/{Png,Jpeg,Bmp,Gif,Qoi}` | Loading an image is each app's problem. |
| `ui/Render` | `math/Bezier` + `math/Spline` | No vector path rendering. |
| `ui/Sound` | `signal/Synth` + `signal/AudioEffect` | UI sounds are PCM blobs; no procedural option. |
| `ui/Animation` | `math/Spline` | Tween curves stuck on linear/quadratic. |
| `ai/Tensor` + `ai/NeuralNet` | `ai/GpuProxy` (today) / `GpuKernels` (proposed) | The transparent-dispatch question is a real open in `GpuCompute.md` open Q3 |
| `Tls` | proposed `crypto/Hkdf` + `crypto/X25519` + `crypto/AesGcm` | TLS 1.3 cipher suites. |
| `FileSystem` | proposed `Path` | Path manipulation reinvented at every callsite. |
| `Logger` | `os.observe/*` | Structured-logging sinks belong here. |

A useful follow-up sweep would `cites`-graph the entire 295-module
surface and produce a connectedness audit; modules with zero
incoming `cites` are either isolated tools or unused.

---

## Prioritization

Eyeballed against the user's stated direction (UEFI console-driven
app, exposing VMs+GPU to end-users and builders) and the active
work in `CurrentSubPlan.md`:

### Tier 1 — load-bearing for current direction

These are needed now or soon:

1. **UI form widgets** — `TextField.codex`, plus the catalog inside
   `Widget.codex` (Button/Checkbox/Dropdown/Slider/etc.). Without
   these the UEFI console-driven app has nothing to put on screen.
2. **`Selection.codex`** + **`Clipboard.codex`** — basic editing.
   The dev console already has a text editor (Phase 3 of
   `GpuCompute.md` framebuffer note is unrelated; the editor is in
   `codex.works/ConsoleEditor.codex`).
3. **`ui/Render` → `encode/{Png,Jpeg,Bmp}` bridge** — exposing the
   GPU and VMs to end-users implies displaying images. The decoders
   exist; the wire-up doesn't.
4. **`ai/Attention` + `ai/Transformer` + `ai/KvCache` + `ai/Sampling`**
   — agent lifecycle (CL 1018) ships an AgentRuntime that loads GGUF
   models; the actual inference layer to run a transformer is the
   biggest hole in the quire pile.

### Tier 2 — round out the substrate

These complete obvious arcs but aren't blocking the current direction:

5. **`Decimal` / `Float` / `BigInt`** in `codex.foreword/` —
   numerics is going to come up; might as well design now.
6. **`Path` / `Format` / `Locale`** in `codex.foreword/` —
   ergonomic gaps that show up in every app.
7. **`Deflate` / `Gzip` / `Zstd`** in `codex.foreword.compress/` —
   Codex over network needs these.
8. **`Hkdf` / `X25519` / `AesGcm`** in crypto — modern TLS 1.3.
9. **UI `Vector.codex` / `Charts.codex`** — admin UIs need them.

### Tier 3 — nice-to-have, defer

Everything in `Sim`, `Math`-advanced, modern config formats
(Yaml/Toml/CBOR), additional compressors, game netcode. These are
real gaps but unlikely to bite within the current quarter.

---

## Naming Observations

A few inconsistencies worth flagging:

- `KNearestNeighbor.codex` vs. shorter peers like `Hamt`, `Trie`.
  Either rename to `Knn.codex` or accept full names everywhere.
- `codex.foreword/Camera.codex` (hardware camera) vs. an eventual
  `codex.foreword.game/Camera.codex` (game camera) — the collision
  is real. Use `HwCamera` / `GameCamera` or place hw camera under
  `codex.kernel`.
- Quire `cite` references in chapters mix `Foreword chapter X`,
  `Game chapter Y`, `UI chapter Z`. Consistent capitalization
  (always quire-name-PascalCase) is already in place; worth
  spot-checking it stays that way.

## Open Questions for Damian

1. **Does the UI quire grow to be production-app-grade**, or stay
   at "just enough for the dev console"? The answer to this
   determines whether form-widget catalog (Tier 1) is months of
   work or one push.
2. **Is the AI quire pursued seriously**, or is the GGUF + agent
   coordinator the floor and inference happens elsewhere? If
   serious, Tier 1 #4 is its own multi-CL project; if not, the
   quire is well-scoped at its current 12 modules.
3. **Float story**: is the project committing to first-class
   IEEE-754 in the foreword, or does fixed-point scale-1000 stay
   the dialect for everyone? Today it's mixed and ad-hoc.
4. **Compression scope**: do we need Codex artifacts to ride over
   `Content-Encoding: gzip` / `Content-Encoding: zstd` headers?
   That single decision picks Tier 2 #7 in or out.
5. **Connectedness audit**: should I run a second pass that diffs
   the `cites` graph against expected bridges and surfaces
   isolated chapters? Lower-priority but worth it once a chapter
   count this size is in the tree.

## What This Doc Does NOT Propose

- Does not propose deleting any existing chapter.
- Does not propose changing the quire structure (the 19-quire layout
  is healthy; the names are fine).
- Does not propose specific implementations — only naming, scope,
  and prioritization.
- Does not include `codex.kernel/`, `codex.os.*/`, or `codex.works/`
  surveys; those are different layers and warrant their own gap
  doc.
- Does not commit to any of the named modules being the right
  *next* CL — Tier 1 is a recommendation against the user's stated
  direction, not a roadmap.

## References

- `codex.foreword*/*.codex` — surveyed at depot HEAD CL 1199.
- `docs/Designs/Features/STDLIB-AND-CONCURRENCY.md` — older
  (2026-03-19) framing of the stdlib question; still load-bearing
  on the "small core, deep foundations" principle.
- `docs/Designs/Language/QUIRES.md` — quire taxonomy.
- `docs/Designs/Codex.OS/GpuCompute.md` and
  `docs/Designs/Codex.OS/GpuKernels.md` — Tier 1 #4 (AI inference)
  and bridge to `GpuKernels` are interlocked.
- `docs/00-OVERVIEW.md` — current OS Stack table; library quires
  shown there at quire granularity.
