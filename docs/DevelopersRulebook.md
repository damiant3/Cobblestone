# Developer's Rulebook

## Foreword Quire Catalog

A foreword quire is a library package marked `"foreword": true` in its
`codex.project.json`. Foreword modules compile before user code.

**They are not automatically in scope, and this line said they were
until 2026-07-19.** A foreword chapter contributes its names only when
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

### codex.foreword (119 modules) — Core

The standard library. Core types, collections, cryptography, text,
data structures, networking, and system utilities.

| Category | Modules |
|----------|---------|
| Core types | List, Maybe, Result, Either, Pair, State |
| Collections | Set, Deque, Queue, PriorityQueue, RingBuffer, CircularBuffer, Hamt, BPlusTree, SkipListText, Trie, IntervalTree, Graph, UnionFind, ElasticHash, FunnelHash, ElasticBloom |
| Text | StringBuilder, StringUtils, TextScan, TextSearch, TextWrap, TabComplete, Format, Parse, Pattern, Regex, EditDistance |
| Crypto | Aes, Aes256, AesGcm, ChaCha20, Poly1305, ChaCha20Poly1305, Sha256, Sha512, Hmac, Hkdf, Pbkdf, Cmac, Ed25519, DiffieHellman, ProofOfWork |
| Math/Stats | MathLib, Decimal, NumberTheory, Probability, Statistics |
| Time | DateTime, Time, Schedule, Scheduler, TimingWheel |
| Encoding | CCE, Unicode, Locale |
| IO/System | Console, FileSystem, Path, Fat16, Fat32, Gpt, Network, Channel, Concurrent, EventBus, Pipeline, SerialLine |
| Probabilistic | BloomFilter, BitSet, CountMinSketch, ConsistentHash |
| Identity/Trust | Identity, FactStore, History, KvStore |
| Misc | Audio, Camera, Display, Fuel, Iterate, Location, Logger, LruCache, Microphone, Random, RateLimiter, Rope, Sensors, Sort, Tls |

### codex.foreword.ai (43 modules) — Machine Learning

Neural networks, tensors, transformers, sampling, and model utilities.

Tensor, Embedding, Attention, Transformer, NeuralNet, Loss, Optimizer,
Sampling, KNearestNeighbor, DecisionTree, GeneticAlgorithm, Gguf,
GpuProxy, Tokenizer, Reservoir, KvCache, DiffusionScheduler,
Activation, SparseLattice

### codex.foreword.compress (4 modules) -- Compression

**The compression stack was DELETED on 2026-07-19.** `Brotli`, `BrotliDict`,
`BrotliDictIndex`, `Deflate`, `Fse`, `Gzip` and `Zstd` are gone, with their
tests, their generators and their format notes. Codex has no general-purpose
compressor and no standard container format.

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

What survives are the four primitives, which stand alone and never depended on
the deleted set.

| Module | Compresses? | What it is |
|---|---|---|
| Lz4 | **yes** | LZ4 block format; hash-table match finding. |
| Lz77 | **yes** | Match/literal tokens. **A hash chain**: positions indexed by a 3-byte hash, candidates walked newest-first, window **32768** and matches to **258** (RFC 1951's limits). Bounded by a 128-candidate chain limit and a strictly-backwards check, both safe because every candidate is verified against the bytes -- the chain is a hint, so a wrong one costs ratio and never correctness. Lazy matching, and a literal-price estimate (from the distinct-byte count of the surrounding 4096 bytes) that declines matches costing more than the literals they replace. Own token format, not a standard. Deflate, Gzip and Brotli used to share it; all three are gone and it is now used only by its own tests. |
| Huffman | **yes** | Frequency-built optimal prefix codes. Own format. |
| Rle | **yes**, weakly | Run-length only. |

### codex.foreword.encode (74 modules, measured 2026-07-23) — Encoding and Codecs

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
| IoT / MQTT | Mqtt (encode **and decode**), MqttEndpoint (client session: CONNACK, SUBACK, QoS 1 with DUP retransmit, inbound delivery), MqttSn, Coap, CoapEndpoint (RFC 7252 client), Lwm2m, Sparkplug, Sntp |
| Industrial bus | Modbus, Dnp3, Bacnet, Knx, J1939, Canopen, Mbus, OpcUa, Iec104, Enip, S7comm, Melsec, Fins, Goose, Hart |
| Wireless / mesh | Lorawan, Zigbee, Ieee802154, Sixlowpan, BleAtt |

### codex.foreword.game (26 modules) — Game Development

Rendering, spatial structures, procedural generation, ECS, input.

TileMap, Sprite, Quadtree, Octree, Scene2D, HexMap, Pathfinding,
AStar, FloodFill, Raytracer, Rasterizer, ECS, CardDeck, Klondike,
Inventory, SaveSlot, CellularAutomata, DiamondSquare, Bresenham,
Voronoi, StateMachine, Tween, Easing, GameCamera, Color

### codex.foreword.math (14 modules) — Mathematics

LinearAlgebra, Geometry, Matrix3, Matrix4, Quaternion, Complex,
Numeric, Optimize, Bezier, Cordic, Spline, Geodesic, VecArray,
Interval

### codex.foreword.signal (14 modules) — Signal Processing

Fft, AudioAnalysis, AudioEffect, Synth, Oscillator, Filter, Envelope,
MusicTheory, Noise, Perlin, Pitch, Resample, Convolution, Wavelet

### codex.foreword.sim (7 modules) — Simulation

Physics, Kinematics, Collision, Constraint, ParticleSystem,
SpatialHash, Steering

### codex.foreword.punctual (8 modules) — Real-Time Primitives

IntOps, BitOps, Saturate, FastMath, Trig, ColorOps, Kinematic, Endian

Every function is `punctual`: no heap, no recursion, bounded
instruction count. Safe to call from real-time, embedded, and
interrupt contexts.

### codex.foreword.engine (42 modules) — 3D Game Engine

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

### codex.foreword.ui (47 modules) — User Interface

Accessibility, Animation, AppRunner, Binding, BoxModel, Canvas, Charts,
Clipboard, CommandPalette, Cursor, DataTable, DetailPane, Dialog, Drag,
Dropdown, Editor, Event, FilterableList, Focus, Font, FontAtlas,
GlyphRasterizer, GpuRender, Icon, InputSource, Layout, Markdown,
Orchestrator, Overlay, Render, RichText, Scroll, SearchBar, Selection,
SettingsPanel, Sound, StatusBadge, Surface, TextField, Theme, Touch,
TreeView, TrueTypeFont, Validation, Vector, Widget, Window

### codex.foreword.gpu (11 modules) — GPU Kernel Programming

DeviceEffect, GpuEffect, DeviceBuffer, DeviceMath, LaunchConfig, Thread,
Warp, Shared, Atomic, Barrier, DisjointSlice

GPU kernel programming surface. `[Device]` effect marks code that runs
on the GPU; `[Gpu]` effect marks host-side kernel launch and buffer
management. Type-safe thread indexing via `ThreadIndex` witness type,
scope-encoded atomics, warp shuffles, shared memory, and
`DisjointSlice` for provably-disjoint parallel writes.

### codex.foreword.shell (5 modules) — Shell Script Emission

BashEmit, KshEmit, PowerShellEmit, ShellBuild, ShellTypes

Typed shell-script construction and emission. `ShellTypes` and
`ShellBuild` model commands, pipelines, and redirections; the three
Emit chapters render that model to Bash, Ksh, or PowerShell.

## Non-Foreword Quires

These are not auto-loaded. User code must `cites` them explicitly.

### codex (60 modules) — The Compiler

The self-hosted compiler, in `codex/compiler/`. Subdirectories: Ast,
Core, Emit, IR, Semantics, Syntax, Types. Do not modify without reading
the code first and passing both gates (sample battery + pingpong).

### codex.os (147 modules) — Operating System

Split across sub-quires. Re-measured 2026-07-23; the previous total
(143) was stale, and the row that read `codex.os | 4` was really
`codex.os.core`.

| Sub-quire | Modules | Purpose |
|-----------|---------|---------|
| codex.os.core | 4 | Core OS abstractions |
| codex.os.dev | 28 | Device management |
| codex.os.kernel | 33 | Hardware drivers (PCI, xHCI, NE2K, VGA, IDE, HDA, USB HID, etc.) |
| codex.os.net | 37 | Networking stack (incl. HttpFetch — the Network effect — DtlsEndpoint, and UdpIO, the datagram send/poll pair) |
| codex.os.observe | 8 | Observability |
| codex.os.replay | 3 | Deterministic replay |
| codex.os.sched | 10 | Scheduling |
| codex.os.trust | 16 | Trust lattice |
| codex.os.verify | 7 | Verification |

### codex.plugs (53 plugs, all building clean) — Transpiler Plugs

48 transpiler plugs (Ada to Zig, 14 UI frameworks, GPU PTX + SPIR-V +
WGSL) plus 5 native backends (ARM64, RISC-V, ELF, PE, IMG). Each plug
receives IR or CDX over TCP and produces the target format. A plug is a
directory under `codex/plugs/` with a `build.ps1`; `common/` and
`test-input/` are not plugs.

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

7. **New foreword modules require a seed rebuild.** Adding or removing
   a module from a foreword quire changes what the compiler bakes in.
   Follow the seed rebuild procedure in the Developer's Guide.

   Adding a *definition* to a module that already exists is a different
   question, and the answer is reachability: whole-program dead-code
   elimination drops what the compiler never calls, so a new function in a
   cited chapter may leave the binary byte-identical. Measured both ways in
   the Developer's Guide. Compare `build/output/Sut.cdx` against
   `seed/Codex.cdx` after the gate rather than reasoning about it.
