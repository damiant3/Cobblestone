# Developer's Rulebook

## Foreword Quire Catalog

A foreword quire is a library package marked `"foreword": true` in its
`codex.project.json`. Foreword modules compile before user code.

**They are not automatically in scope.** A foreword chapter contributes its names only when
it is cited into the compilation unit; `cites Foreword chapter X` is
what puts X's definitions within reach, exactly as for any other quire.
What "foreword" buys is that the quire resolves without a path and its
chapters compile first, not that every name in it is visible everywhere.

The difference is invisible until a definition MOVES between foreword
chapters, and then it is a `CDX3002: Undefined name` in every caller
that cited the old home. Two tests failed that way during the 2.32 text
cluster against the belief this sentence encouraged. When
you relocate a foreword definition, budget a `cites` line for each
caller of the old chapter.

### codex.foreword (129 modules, measured 2026-08-16) -- Core

The standard library. Core types, collections, cryptography, text,
data structures, networking, and system utilities.

| Category | Modules |
|----------|---------|
| Core types | List, Maybe, Result, Either, Pair, State |
| Collections | Set, Deque, Queue, PriorityQueue, RingBuffer, CircularBuffer, Hamt, BPlusTree, SkipListText, Trie, IntervalTree, Graph, UnionFind, ElasticHash, FunnelHash, ElasticBloom |
| Text | StringBuilder, StringUtils, TextScan, TextSearch, TextWrap, TabComplete, Format, Parse, Pattern, Regex, EditDistance |
| Crypto | Aes, Aes256, AesGcm, ChaCha20, Poly1305, ChaCha20Poly1305, Sha256, Sha512, Hmac, Hkdf, Pbkdf, Cmac, Ed25519, DiffieHellman, ProofOfWork, CryptoBig, Rsa |
| Math/Stats | MathLib, Decimal, NumberTheory, Probability, Statistics |
| Time | DateTime, Time, Schedule, Scheduler, TimingWheel |
| Encoding | CCE, Unicode, Locale |
| IO/System | Console, FileSystem, Path, Fat16, Fat32, Gpt, Network, Channel, Concurrent, EventBus, Pipeline, SerialLine |
| Probabilistic | BloomFilter, BitSet, CountMinSketch, ConsistentHash |
| Identity/Trust | Identity, FactStore, History, KvStore |
| Misc | Audio, Camera, Display, Fuel, Iterate, Location, LocationStub, Logger, LruCache, Microphone, Random, RateLimiter, Rope, Sensors, SensorsStub, Sort, Tls |

`Camera`, `Location`, `Microphone` and `Sensors` are effect declarations
and nothing else: an `effect X where` marker with no operations. The
operations live elsewhere, because an operation declared with no handler
is not neutral -- it type-checks and then dies at emit with CDX2040, so a
program that merely MENTIONS it cannot be built. `LocationStub` and
`SensorsStub` answer their effects with named constants and publish
`location-is-fixed` / `sensors-are-fixed`, both False, so a caller can
always tell a placeholder from a reading. `Camera`'s only answering
chapter is `Works chapter CamCapture`, retired along with the camera rig
(2026-08-05); the effect declaration remains for a future revival.

### codex.foreword.ai (43 modules) -- Machine Learning

Neural networks, tensors, transformers, sampling, and model utilities.

Tensor, Embedding, Attention, Transformer, NeuralNet, Loss, Optimizer,
Sampling, KNearestNeighbor, DecisionTree, GeneticAlgorithm, Gguf,
GpuProxy, Tokenizer, Reservoir, KvCache, DiffusionScheduler,
Activation, SparseLattice

### codex.foreword.compress (8 modules, measured 2026-07-27) -- Compression

**Four of the seven came back.** `Brotli`, `BrotliDict`, `BrotliDictIndex`
and `Deflate` were rebuilt from the spec by val and landed on main 10560,
and the decoder now reads other implementations' streams -- the thing the
original never did. `docs/Agents/val-workplan.md` is the live account; this
catalog is not, so do not quote the paragraphs below as current status.
Still gone: `Fse`, `Gzip` and `Zstd`.

The history stands, and it is why the rebuild was held to a foreign-stream
oracle. **The compression stack was DELETED on 2026-07-19.** All seven were
removed, with their tests, their generators and their format notes, leaving
Codex with no general-purpose compressor and no standard container format.

The reason is in `docs/PM/Active/Stories/BrotliBeatsOpus.md` and it is worth reading
before rebuilding any of it. The short version: the encoders were real and an
independent decoder said so, but the DECODERS only ever read what our own
encoders wrote, and that was reported as working for three days across five
sessions. The harness asked whether .NET could read our output and never whether
we could read .NET's. Handed four streams from a real encoder, Brotli returned
zero bytes for all four.

**If this is rebuilt, the oracle test comes FIRST and in both directions.** A
round-trip through our own halves cannot tell a compressor from a pipe, and it
cannot tell a decoder from a decoder that only reads itself.

What survived the deletion untouched are the four primitives, which stand alone
and never depended on the deleted set.

| Module | Compresses? | What it is |
|---|---|---|
| Lz4 | **yes** | LZ4 block format; hash-table match finding. |
| Lz77 | **yes** | Match/literal tokens. **A hash chain**: positions indexed by a 3-byte hash, candidates walked newest-first, window **32768** and matches to **258** (RFC 1951's limits). Bounded by a 128-candidate chain limit and a strictly-backwards check, both safe because every candidate is verified against the bytes -- the chain is a hint, so a wrong one costs ratio and never correctness. Lazy matching, and a literal-price estimate (from the distinct-byte count of the surrounding 4096 bytes) that declines matches costing more than the literals they replace. Own token format, not a standard. Deflate, Gzip and Brotli used to share it; all three are gone and it is now used only by its own tests. |
| Huffman | **yes** | Frequency-built optimal prefix codes. Own format. |
| Rle | **yes**, weakly | Run-length only. |

### codex.foreword.encode (75 modules, measured 2026-07-26) -- Encoding and Codecs

Data formats, image codecs, audio codecs, video codecs, protocols.

| Category | Modules |
|----------|---------|
| Data | Base64, Hex, Json, Yaml, Toml, Csv, Ini, Markdown, Protobuf, MessagePack, Cbor, Bencode, Uri, Uuid, Jwt, GrayCode, Crc32 |
| Image | Bmp, Png, Jpeg, Gif, Tiff, Qoi |
| Audio | Wav, Flac, Mp3, Ogg, Midi |
| Video | Mp4, Avi, VideoCodec |
| 3D/Font | Gltf, TrueType, TrueTypeWriter, FontGen |
| Web/mail | WebSocket, Smtp |
| Transport security | Dtls (record layer), DtlsHandshake (flights, retransmission, cookie), DtlsMessage (framing, transcript, Finished, ACK), DtlsHello (hello bodies, cookie ext) - RFC 9147 |
| Certificates | X509 (parse), X509Chain (path validation and peer identity), TlsEndpoint, TrustAnchors (the five roots Codex ships: DigiCert and Let's Encrypt) |
| IoT / MQTT | Mqtt (encode **and decode**), MqttEndpoint (client session: CONNACK, SUBACK, QoS 1 with DUP retransmit, inbound delivery), MqttSn, Coap, CoapEndpoint (RFC 7252 client), Lwm2m, Sparkplug, Sntp |
| Industrial bus | Modbus, Dnp3, Bacnet, Knx, J1939, Canopen, Mbus, OpcUa, Iec104, Enip, S7comm, Melsec, Fins, Goose, Hart |
| Wireless / mesh | Lorawan, Zigbee, Ieee802154, Sixlowpan, BleAtt |

### codex.foreword.game (26 modules) -- Game Development

Rendering, spatial structures, procedural generation, ECS, input.

TileMap, Sprite, Quadtree, Octree, Scene2D, HexMap, Pathfinding,
AStar, FloodFill, Raytracer, Rasterizer, ECS, CardDeck, Klondike,
Inventory, SaveSlot, CellularAutomata, DiamondSquare, Bresenham,
Voronoi, StateMachine, Tween, Easing, GameCamera, Color

### codex.foreword.math (14 modules) -- Mathematics

LinearAlgebra, Geometry, Matrix3, Matrix4, Quaternion, Complex,
Numeric, Optimize, Bezier, Cordic, Spline, Geodesic, VecArray,
Interval

### codex.foreword.signal (14 modules) -- Signal Processing

Fft, AudioAnalysis, AudioEffect, Synth, Oscillator, Filter, Envelope,
MusicTheory, Noise, Perlin, Pitch, Resample, Convolution, Wavelet

### codex.foreword.sim (7 modules) -- Simulation

Physics, Kinematics, Collision, Constraint, ParticleSystem,
SpatialHash, Steering

### codex.foreword.punctual (8 modules) -- Real-Time Primitives

IntOps, BitOps, Saturate, FastMath, Trig, ColorOps, Kinematic, Endian

Every function is `punctual`: no heap, no recursion, a worst-case
execution budget in instructions. Safe to call from real-time, embedded,
and interrupt contexts.

`punctual` is the TIME promise and forbids the heap outright. Its sibling
`bounded <class>` is the ALLOCATION promise for code that must allocate:
it declares a ceiling in `none < fixed < linear < growing`, the compiler
infers the class from the body, and CDX6101 refuses transitively when a
callee exceeds it. Both are enforced properties, not documentation.
`DevelopersGuide.md` under Bounded Functions has the syntax and the
proven/abstains table.

### codex.foreword.engine (42 modules) -- 3D Game Engine

AbilitySystem, AnimBlend, AssetTable, Audio3D, AudioBus, Biome,
ClothSim, Collision3D, Culling, Cutscene, DamageSystem, DebugDraw,
EdgeMesh, FacialAnim, Fog, FractalPlant, GameLoop, GameplayTags,
HairSim, HelmBridge, Input, LOD, Material, Mesh, Musculature,
NavMesh, ParticleRenderer, PhysicsJoint, PostProcess, Renderer3D,
Scene3D, Signal, Skinning, SkinShader, SoftBody, SplinePath,
Terrain, Texture, TimeOfDay, Water, WorldGen, WorldHUD

3D rendering pipeline, scene management, materials, LOD/culling,
post-processing, spatial audio, input handling, gameplay systems,
physics, procedural generation, and edge mesh networking.

### codex.foreword.ui (50 modules) -- User Interface

Accessibility, Animation, AppRunner, Binding, BoxModel, Canvas, Charts,
Clipboard, CommandPalette, Cursor, DataTable, DetailPane, Dialog, Drag,
Dropdown, Editor, Event, FilterableList, Focus, Font, FontAtlas,
GlyphRasterizer, GpuRender, Icon, InputSource, Layout, Markdown,
Orchestrator, Overlay, Render, RichText, Scroll, SearchBar, Selection,
SettingsPanel, Sound, StatusBadge, Surface, TextField, Theme, Touch,
TreeView, TrueTypeFont, Validation, Vector, Widget, Window

### codex.foreword.gpu (11 modules) -- GPU Kernel Programming

DeviceEffect, GpuEffect, DeviceBuffer, DeviceMath, LaunchConfig, Thread,
Warp, Shared, Atomic, Barrier, DisjointSlice

GPU kernel programming surface. `[Device]` effect marks code that runs
on the GPU; `[Gpu]` effect marks host-side kernel launch and buffer
management. Type-safe thread indexing via `ThreadIndex` witness type,
scope-encoded atomics, warp shuffles, shared memory, and
`DisjointSlice` for provably-disjoint parallel writes.

### codex.foreword.shell (5 modules) -- Shell Script Emission

BashEmit, KshEmit, PowerShellEmit, ShellBuild, ShellTypes

Typed shell-script construction and emission. `ShellTypes` and
`ShellBuild` model commands, pipelines, and redirections; the three
Emit chapters render that model to Bash, Ksh, or PowerShell.

## Non-Foreword Quires

These are not auto-loaded. User code must `cites` them explicitly.

### codex (64 modules) -- The Compiler

The self-hosted compiler, in `codex/compiler/`. Subdirectories: Ast,
Core, Emit, IR, Semantics, Syntax, Types. Do not modify without reading
the code first and passing both gates (sample battery + pingpong).

### codex.os (160 modules) -- Operating System

Split across sub-quires. Re-measured 2026-07-29, when adding one kernel
chapter turned `check-doc-counts.ps1` red and showed the table had
drifted well beyond that one row: the header said 147 against a measured
156, and `codex.os.dev` said 28 against 36. The sub-rows must sum to the
header, so both were re-measured together rather than leaving the table
internally inconsistent.

| Sub-quire | Modules | Purpose |
|-----------|---------|---------|
| codex.os.core | 4 | Core OS abstractions |
| codex.os.dev | 37 | Device management |
| codex.os.kernel | 36 | Hardware drivers (PCI, xHCI, NE2K, e1000e, VGA, IDE, HDA, USB HID, and Hpet, the monotonic clock) |
| codex.os.net | 39 | Networking stack (incl. HttpFetch -- the Network effect -- DtlsEndpoint, UdpIO, the datagram send/poll pair, and DhcpIO, which acquires an address) |
| codex.os.observe | 8 | Observability |
| codex.os.replay | 3 | Deterministic replay |
| codex.os.sched | 10 | Scheduling |
| codex.os.trust | 16 | Trust lattice |
| codex.os.verify | 7 | Verification |

### codex.plugs (55 plugs, all building clean) -- Transpiler Plugs

48 language and UI transpilers (Ada to Zig, 14 UI frameworks, GPU PTX +
SPIR-V + WGSL), 6 native backends (ARM64, RISC-V, T3ISA, ELF, PE, IMG),
and `recheck`, the independent rechecker. Each plug receives IR or CDX
over TCP and produces the target format. A plug is a directory under
`codex/plugs/` with a `build.ps1`; `common/` and `test-input/` are not
plugs, and `t3isa/spec/` is a subdirectory of one rather than another.

Re-measured 2026-08-09, when landing `t3isa` moved the count and showed
the header had already drifted: it said 53 against a measured 54, because
`recheck` arrived without the row being touched. Both were re-measured
together rather than fixing only the one that moved.

## Library Rules

1. **Foreword modules must be self-contained.** A foreword module may
   depend on other foreword modules in the same quire or a parent
   quire. It must not depend on codex.os or on any app quire.

2. **No circular dependencies between quires.** The dependency order is:
   codex.foreword → codex → codex.os → app quires (`apps/`).
   A quire may depend on anything to its left. Never to its right.

3. **One module, one concern.** Each `.codex` file is a chapter. Each
   chapter handles one domain. If a module grows beyond its concern,
   split it.

4. **Naming follows the book metaphor.** Chapters are PascalCase.
   Functions are kebab-case. Types are PascalCase. Hyphens, not
   underscores. `__double-underscore` is reserved for compiler
   intrinsics.

5. **All internal text is CCE.** Unicode conversion happens only at
   I/O boundaries. Foreword modules that handle text must operate on
   CCE internally.

6. **No GC, no magic.** Bare-metal has no garbage collector. Every
   allocation is permanent until the producing function returns.
   Library authors must document allocation behavior for hot-path
   functions.

7. **A seed rebuild is decided by REACHABILITY, and a new foreword
   module is not automatically reachable.** Adding a module to a foreword
   quire does not by itself change what the compiler bakes in. Measured
   2026-07-26: `CryptoBig` and `Rsa` were added to
   `codex/foreword/core` in one changelist and `build/output/Sut.cdx`
   came out byte-identical to `seed/Codex.cdx`.

   The reason is mechanical rather than lucky. `concat-codex-self.ps1`
   assembles the compiler's unit by walking `cites` transitively from
   `codex/compiler`, so a foreword chapter nobody cites is never in the
   unit at all -- it is not dead code the emitter prunes, it is source the
   compiler never sees. A new module changes the seed when something in
   that closure cites it, and not before.

   Removing or renaming a module IS different, because something already
   cites it. So is adding a `cites` line anywhere in the closure.

   **But a cite is not a dependency cost, and treating it as one declines
   correct fixes.** A cite governs name visibility and unit assembly, not
   what is emitted: unreached code is still eliminated, so citing a large
   chapter to reach one definition does not drag the chapter in. Measured,
   adding a cite moved two CDX artifacts by **3 bytes each**, which was the
   longer name. Weigh a cite by whether the name belongs in scope, never by
   the size of what sits behind it.

   The rule underneath all of it is the same one as for a new definition
   in an existing chapter: reachability, not directory. Compare
   `build/output/Sut.cdx` against `seed/Codex.cdx` after the gate rather
   than reasoning about it, every time, in both directions. Predicting
   this has now been wrong in both directions on the record -- CL 9432
   predicted a seed and did not need one, and this rule predicted one for
   every new foreword module since it was written.

   **When you need the answer BEFORE paying for a gate, ask the concat,
   not the cite lines.** Grepping `codex/compiler/` for
   `cites Foreword chapter X` under-reports, because the compiler unit is
   assembled by `build/concat-codex-self.ps1` and a chapter can arrive
   without the citation that would name it (L-SUBSET). Build the unit and
   look in it:

   ```powershell
   pwsh build/concat-codex-self.ps1 -OutFile $scratch\unit.codex
   Select-String -Path $scratch\unit.codex -Pattern "^Chapter: .*Fat16"
   ```

   **The chapters are QUIRE-PREFIXED in there** -- `Foreword--Fat16`, not
   `Fat16` -- so a grep for `Chapter: Fat16` answers "absent" for a chapter
   that is present, and answers it for every chapter you ask about, which
   reads as a confident all-clear. Measured 2026-08-16: `Foreword--Fat16`
   present, `Fat32`, `Gguf` and `ShellTypes` absent, which is why a Fat16
   change moves `Sut` and a Fat32 change does not. The gate comparison
   above is still the authority; this is how to predict it without one,
   and how to tell whether someone else's merge-down has invalidated a
   seed you already built.

8. **Prose is exactly one space of indent, and a word is a keyword the
   moment it is not.** `scan-token` in `codex/compiler/Syntax/Lexer.codex`
   decides on `s.column == 2`: a line starting there is prose and its
   contents are never tokenized. One more space and the same line is code,
   where `cites`, `grounds`, `quotes` and `trusting` are reserved words
   (`classify-word`, same file).

   The way this bites is reflowing a paragraph. Re-wrapping can push a
   sentence's leading word onto a fresh line and give the line an extra
   space, and if that word happens to be `cites`, the sentence becomes a
   cite directive and the compiler reports a chapter name made of English:
   `error 3010: Unresolvable cite: Foreword chapter 'LocationStub and gets
   fixed constants that say'`. Nothing about the message points at the
   indentation, which is the whole cost of it.

   The fix is to not start a prose line with one of those words -- move it
   mid-line and the sentence reads the same. After editing prose in a
   chapter, `^\s+(cites|grounds|quotes|trusting)\s` over the files you
   touched finds every one of these in a second, and the directives it
   legitimately matches are all in the header block where you can see them.
