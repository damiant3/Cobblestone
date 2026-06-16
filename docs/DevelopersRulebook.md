# Developer's Rulebook

## Foreword Quire Catalog

A foreword quire is a library package marked `"foreword": true` in its
`codex.project.json`. Foreword modules compile before user code and
make their types and functions automatically available.

### codex.foreword (106 modules) — Core

The standard library. Core types, collections, cryptography, text,
data structures, networking, and system utilities.

| Category | Modules |
|----------|---------|
| Core types | List, Maybe, Result, Either, Pair, State |
| Collections | Set, Deque, Queue, PriorityQueue, RingBuffer, CircularBuffer, Hamt, BPlusTree, SkipListText, Trie, IntervalTree, Graph, UnionFind, ElasticHash, FunnelHash, ElasticBloom |
| Text | StringBuilder, StringUtils, TextScan, TextSearch, TextWrap, TabComplete, Format, Parse, Pattern, Regex, EditDistance |
| Crypto | Aes, AesGcm, ChaCha20, Sha256, Sha512, Hmac, Hkdf, Pbkdf, Ed25519, DiffieHellman, ProofOfWork |
| Math/Stats | MathLib, Decimal, NumberTheory, Probability, Statistics |
| Time | DateTime, Time, Schedule, Scheduler, TimingWheel |
| Encoding | CCE, Unicode, Locale |
| IO/System | Console, FileSystem, Path, Fat16, Network, Channel, Concurrent, EventBus, Pipeline |
| Probabilistic | BloomFilter, BitSet, CountMinSketch, ConsistentHash |
| Identity/Trust | Identity, FactStore, History, KvStore |
| Misc | Camera, Display, Fuel, Iterate, Location, Logger, LruCache, Microphone, Random, RateLimiter, Rope, Sensors, Sort, Tls |

### codex.foreword.ai (19 modules) — Machine Learning

Neural networks, tensors, transformers, sampling, and model utilities.

Tensor, Embedding, Attention, Transformer, NeuralNet, Loss, Optimizer,
Sampling, KNearestNeighbor, DecisionTree, GeneticAlgorithm, Gguf,
GpuProxy, Tokenizer, Reservoir, KvCache, DiffusionScheduler,
Activation, SparseLattice

### codex.foreword.compress (8 modules) — Compression

Deflate, Gzip, Brotli, Zstd, Lz4, Lz77, Huffman, Rle

### codex.foreword.encode (35 modules) — Encoding and Codecs

Data formats, image codecs, audio codecs, video codecs, protocols.

| Category | Modules |
|----------|---------|
| Data | Base64, Hex, Json, Yaml, Toml, Csv, Ini, Markdown, Protobuf, MessagePack, Cbor, Bencode, Uri, Uuid, GrayCode, Crc32 |
| Image | Bmp, Png, Jpeg, Gif, Tiff, Qoi |
| Audio | Wav, Flac, Mp3, Ogg, Midi |
| Video | Mp4, Avi, VideoCodec |
| Protocol | WebSocket, Smtp |

### codex.foreword.game (26 modules) — Game Development

Rendering, spatial structures, procedural generation, ECS, input.

TileMap, Sprite, Quadtree, Octree, Scene2D, HexMap, Pathfinding,
AStar, FloodFill, Raytracer, Rasterizer, ECS, CardDeck, Klondike,
Inventory, SaveSlot, CellularAutomata, DiamondSquare, Bresenham,
Voronoi, StateMachine, Tween, Easing, GameCamera, Color

### codex.foreword.math (12 modules) — Mathematics

LinearAlgebra, Geometry, Matrix3, Matrix4, Quaternion, Complex,
Numeric, Optimize, Bezier, Cordic, Spline, Geodesic

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

### codex.foreword.engine (21 modules) — 3D Game Engine

Renderer3D, Scene3D, Material, Texture, Mesh, Skinning, LOD, Culling,
PostProcess, Audio3D, AudioBus, Input, GameLoop, GameplayTags,
AbilitySystem, Signal, DebugDraw, TimeOfDay, AssetTable, EdgeMesh,
HelmBridge

3D rendering pipeline, scene management, materials, LOD/culling,
post-processing, spatial audio, input handling, gameplay systems,
and edge mesh networking for multiplayer.

### codex.foreword.ui (28 modules) — User Interface

Widget, Window, Surface, Theme, Font, Icon, Event, Layout, Animation,
Binding, TextField, RichText, Dialog, Overlay, Orchestrator, Render,
Accessibility, Focus, Selection, Cursor, Drag, Touch, Scroll, BoxModel,
Charts, Sound, Clipboard, Vector

## Non-Foreword Quires

These are not auto-loaded. User code must `cites` them explicitly.

### codex (54 modules) — The Compiler

The self-hosted compiler. Subdirectories: Ast, Core, Emit, IR,
Semantics, Syntax, Types. Do not modify without reading the code first
and passing both gates (sample battery + pingpong).

### codex.kernel (24 modules) — Hardware Drivers

Bare-metal hardware interfaces: BitmapFont, Console, DiagnosticShell,
DiskFacts, DriveManager, GpuBridge, IdentityManager, Ivshmem,
Keyboard, Mouse, Ne2k, Pci, SystemDb, Usb, UsbAudio, UsbMassStorage,
UsbVideo, Vga, VgaGraphics, VmIde, VmSerial, Xhci

### codex.os (81 modules) — Operating System

Split across sub-quires:

| Sub-quire | Modules | Purpose |
|-----------|---------|---------|
| codex.os | 4 | Core OS abstractions |
| codex.os.dev | 5 | Device management |
| codex.os.kernel | 24 | Hardware drivers (PCI, xHCI, NE2K, VGA, IDE, HDA, etc.) |
| codex.os.net | 18 | Networking stack |
| codex.os.observe | 7 | Observability |
| codex.os.replay | 3 | Deterministic replay |
| codex.os.sched | 6 | Scheduling |
| codex.os.trust | 11 | Trust lattice |
| codex.os.verify | 5 | Verification |

### codex.plugs (133 modules) — Transpiler Plugs

53 transpiler plugs (Ada to Zig, 14 UI frameworks, GPU PTX + SPIR-V,
4 binary formats) plus WASM and HTML backends. Each plug receives IR
or CDX over TCP and produces the target format.

## Library Rules

1. **Foreword modules must be self-contained.** A foreword module may
   depend on other foreword modules in the same quire or a parent
   quire. It must not depend on codex.kernel, codex.os, or codex.works.

2. **No circular dependencies between quires.** The dependency order is:
   codex.foreword → codex → codex.kernel → codex.os → codex.works.
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
