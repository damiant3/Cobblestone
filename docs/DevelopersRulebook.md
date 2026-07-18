# Developer's Rulebook

## Foreword Quire Catalog

A foreword quire is a library package marked `"foreword": true` in its
`codex.project.json`. Foreword modules compile before user code and
make their types and functions automatically available.

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

### codex.foreword.compress (8 modules) — Compression

**All eight compress now, but two only shrink runs.** `Zstd` and `Brotli` were
pass-through framing that returned MORE bytes than they were given until val 8844
gave each RLE blocks: a run of four or more identical bytes costs one header plus
one byte. They compress run-heavy input and are pinned by a size assertion
(`codex/test/lib/zstd-test`, `brotli-test`), but neither has entropy coding yet,
so general non-run data is stored and does not shrink. (`Deflate` and `Gzip` had
the same pass-through problem until val 8646.) The remaining ratio work -- Zstd
FSE/Huffman blocks, real Brotli, Deflate dynamic Huffman -- is BACKLOG 5.13.

| Module | Compresses? | What it is |
|---|---|---|
| Deflate | **yes** | RFC 1951. `deflate-compress` emits fixed-Huffman (BTYPE=01); `deflate-compress-dynamic` emits data-derived dynamic Huffman (BTYPE=02, val 8853); `deflate-compress-stored` is the stored encoder. `deflate-compress` does not yet auto-pick the smallest block per input. |
| Gzip | **yes** | RFC 1952 container over Deflate. Interoperates both ways with zlib. |
| Lz4 | **yes** | LZ4 block format; hash-table match finding. |
| Lz77 | **yes** | Match/literal tokens; Deflate's matcher. Own token format, not a standard. |
| Huffman | **yes** | Frequency-built optimal prefix codes. Own format. |
| Rle | **yes**, weakly | Run-length only. |
| **Zstd** | **yes**, runs | RFC 8878 frame; RLE blocks (Block_Type 01) for runs, raw blocks otherwise. No FSE/Huffman compressed block yet. |
| **Brotli** | **yes**, runs | RLE + stored meta-blocks. Real ratio on runs; still NOT RFC 7932 interoperable (internal format). |

### codex.foreword.encode (71 modules) — Encoding and Codecs

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
| IoT / MQTT | Mqtt, MqttSn, Coap, Lwm2m, Sparkplug, Sntp |
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

### codex.os (143 modules) — Operating System

Split across sub-quires. Re-measured 2026-07-14; the previous total
(140) was stale, and the row that read `codex.os | 4` was really
`codex.os.core`.

| Sub-quire | Modules | Purpose |
|-----------|---------|---------|
| codex.os.core | 4 | Core OS abstractions |
| codex.os.dev | 28 | Device management |
| codex.os.kernel | 33 | Hardware drivers (PCI, xHCI, NE2K, VGA, IDE, HDA, USB HID, etc.) |
| codex.os.net | 34 | Networking stack (incl. HttpFetch — the Network effect — and DtlsEndpoint) |
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
