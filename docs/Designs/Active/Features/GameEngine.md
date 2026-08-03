# Codex Game Engine -- General-Purpose, Bare-Metal, Self-Hosted

## Status

**Phase 1 is complete.** All nine chapters ship in
`codex/foreword/engine/`: Mesh, Scene3D, Renderer3D, Material, Texture,
GameLoop, Input, AssetTable, Audio3D. The complete 3D pipeline from mesh
to framebuffer runs on bare metal with no OS, no libc, no external
dependencies. Every byte is Codex.

Four chapters from later phases landed early and are also in the tree:
`Culling`, `Skinning`, `PostProcess`, and `DebugDraw`.

**Next: Phase 2 -- shadow mapping.** The `DepthBuffer` type Phase 1 built
for the main pass is exactly what a light-space depth pass needs; the
work is a second render target rendered from the light's point of view
and a light-space compare in the shading stage. See "Shadow Mapping
(Phase 2)" below. SIMD math and skeletal animation are the other Phase 2
items; skinning is already in.

---

## Motivation

Codex already has 93 foreword files (~12,500 lines) implementing
individual game engine subsystems: ECS, 2D rasterizer, raytracer, Verlet
physics, collision detection, particle systems, audio DSP, sprite/tile
systems, a UI compositor, CORDIC trig, Mat4/Quaternion/LinearAlgebra,
procedural generation (cellular automata, diamond-square, Voronoi,
Perlin), pathfinding (A*, hex grids), rollback netcode, save/load, and
a hardware framebuffer driver (VBE 32bpp XRGB8888). The GPU backend
has PTX and SPIR-V plug emitters (CL 4432) for dual-target GPU
compilation, and the SIMD design (SIMD.md) specifies `Vector N T` types
with SSE2/AVX/NEON/SVE/RISC-V V targets.

What is missing is the *connective tissue*: the pipeline that wires
these subsystems into a coherent engine with a 3D scene graph, mesh
types, a depth-buffered rasterization pipeline, render passes, a
material system, asset management, a game loop, and unified input. This
design fills those gaps.

### What makes this different

1. **Bare metal.** No OS, no driver stack, no GPU API. The engine talks
   directly to hardware (VBE framebuffer, port I/O, MMIO) or dispatches
   GPU kernels through the serial-bridge protocol. There is no Vulkan,
   no DirectX, no OpenGL -- those are other people's abstractions. We own
   the metal.

2. **Self-hosted.** The engine, the compiler that compiles the engine,
   and the tool that runs the compiler are all Codex (except codex-vm.exe,
   which is the hypervisor). A game built with this engine ships as a
   single CDX binary, bootable.

3. **Fixed-point + Real.** Geometry and physics use fixed-point (scale
   1000) for deterministic cross-platform behavior. The `Real` type
   (f64/f32/f16 with safety modes from SIMD.md) is available for GPU
   shader code and SIMD hot paths. No floating-point surprise behavior.

4. **Dependent types carry semantics.** `Vector 4 (Real approximate)` is
   a type. `Integer between 0 and 255` is a type. The engine's vertex
   and pixel types carry their constraints through the type system, and
   the compiler proves bounds at compile time.

5. **Linear types for resources.** GPU buffers, textures, and render
   targets are `linear` -- the type system guarantees they are consumed
   exactly once (no leak, no use-after-free), with no GC.

6. **Effect system for I/O.** Drawing, input, audio, and network are
   effects. A pure function cannot accidentally touch the framebuffer.
   Systems that need hardware carry their effects in their types.

7. **Dual-target GPU.** The same Codex shader code compiles to PTX
   (NVIDIA) and SPIR-V (Vulkan) through the plug architecture, with
   target selection at load time.

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│  Game Code                                                  │
│  (your game, in Codex)                                      │
├─────────────────────────────────────────────────────────────┤
│  Engine Layer                                               │
│  ┌──────────┬──────────┬──────────┬──────────┬───────────┐  │
│  │ Scene3D  │ Renderer │ Assets   │ GameLoop │ Input     │  │
│  │ (graph,  │ (pipeline│ (mesh,   │ (fixed   │ (unified  │  │
│  │  xforms, │  passes, │  texture,│  dt, var │  keyboard │  │
│  │  cull)   │  depth,  │  audio,  │  dt,     │  mouse    │  │
│  │          │  light)  │  stream) │  phases) │  gamepad) │  │
│  └────┬─────┴────┬─────┴────┬─────┴────┬─────┴─────┬─────┘  │
│       │          │          │          │           │         │
├───────┼──────────┼──────────┼──────────┼───────────┼─────────┤
│  Foreword Libraries (existing)                              │
│  ┌──────────┬──────────┬──────────┬──────────┬───────────┐  │
│  │ Math     │ Game     │ Sim      │ Signal   │ UI        │  │
│  │ Mat4     │ ECS      │ Physics  │ Synth    │ Surface   │  │
│  │ Quat     │ Raster   │ Collide  │ FFT      │ Render    │  │
│  │ Geom     │ Scene2D  │ Particle │ Perlin   │ Event     │  │
│  │ Cordic   │ Sprite   │ Steer    │ Osc      │ Font      │  │
│  │ Bezier   │ Camera   │ Spatial  │ Env      │ Compose   │  │
│  │ LinAlg   │ TileMap  │ Kinem    │ Filter   │ Anim      │  │
│  └──────────┴──────────┴──────────┴──────────┴───────────┘  │
├─────────────────────────────────────────────────────────────┤
│  Hardware Abstraction                                       │
│  ┌──────────┬──────────┬──────────┬──────────┬───────────┐  │
│  │ VBE/VGA  │ GpuBridge│ HDA      │ Keyboard │ Mouse     │  │
│  │ framebuf │ COM3→GPU │ audio    │ PS/2,USB │ PS/2,USB  │  │
│  └──────────┴──────────┴──────────┴──────────┴───────────┘  │
├─────────────────────────────────────────────────────────────┤
│  Bare Metal (codex-vm or real hardware)                     │
│  x86-64, identity-mapped, bump allocator, no OS             │
└─────────────────────────────────────────────────────────────┘
```

---

## New Chapters

All new code lives under `codex/foreword/engine/` as quire `Engine`.

### 1. Mesh -- Vertex Buffers and Index Buffers

```
Chapter: Mesh
  cites Math chapter Matrix4
  cites Math chapter Quaternion
```

Types:

```
  Vertex = record {
    vx : Integer, vy : Integer, vz : Integer,
    nx : Integer, ny : Integer, nz : Integer,
    u : Integer, v : Integer,
    color : Integer
  }

  Mesh = record {
    mesh-verts : List Vertex,
    mesh-indices : List Integer,
    mesh-vert-count : Integer,
    mesh-index-count : Integer
  }

  MeshPrimitive =
    | MeshTriangles
    | MeshLines
    | MeshPoints
```

Vertex positions, normals, and UVs are fixed-point (scale 1000).
Color is packed XRGB8888 (same as framebuffer). Index buffer stores
triangle indices (3 per triangle). Mesh is immutable after construction
-- transform the scene node, not the mesh.

Primitive generators: `mesh-cube`, `mesh-sphere` (icosphere subdivision),
`mesh-plane`, `mesh-cylinder`, `mesh-cone`, `mesh-torus`. These are
the engine's built-in test meshes -- enough to prototype any game
without external assets.

### 2. Scene3D -- 3D Scene Graph

```
Chapter: Scene3D
  cites Engine chapter Mesh
  cites Engine chapter Material
  cites Math chapter Matrix4
  cites Math chapter Quaternion
  cites Game chapter ECS
```

Types:

```
  Transform3D = record {
    t3-pos : Vec3,
    t3-rot : Quat,
    t3-scale : Vec3
  }

  SceneNode3D = record {
    sn3-transform : Transform3D,
    sn3-mesh : Maybe Mesh,
    sn3-material : Maybe MaterialId,
    sn3-children : List Integer,
    sn3-parent : Integer,
    sn3-visible : Boolean,
    sn3-tag : Text
  }

  Scene3D = record {
    s3-nodes : List SceneNode3D,
    s3-count : Integer,
    s3-lights : List Light3D,
    s3-camera : Camera3D,
    s3-ambient : Integer
  }
```

Parent-child hierarchy with local transforms. World transform computed
by walking up the parent chain (recursive mat4-mul). Frustum culling
using AABB-against-frustum test (6-plane extraction from
view-projection matrix).

### 3. Renderer3D -- Software 3D Rasterization Pipeline

```
Chapter: Renderer3D
  cites Engine chapter Scene3D
  cites Engine chapter Mesh
  cites Engine chapter Material
  cites Game chapter Rasterizer
  cites Game chapter Color
  cites Math chapter Matrix4
```

The pipeline:

```
  1. CULL      -- frustum-cull scene nodes against camera
  2. TRANSFORM -- multiply each vertex by model × view × projection
  3. CLIP      -- clip triangles against the near plane (w > 0)
  4. PROJECT   -- perspective divide (x/w, y/w) → screen coords
  5. RASTERIZE -- scanline fill with per-pixel depth test
  6. SHADE     -- per-pixel lighting (Phong), material color
  7. WRITE     -- write pixel to framebuffer
```

Types:

```
  DepthBuffer = record {
    db-width : Integer,
    db-height : Integer,
    db-data : List Integer
  }

  RenderTarget = record {
    rt-fb : Framebuf,
    rt-depth : DepthBuffer
  }

  Camera3D = record {
    c3-eye : Vec3,
    c3-target : Vec3,
    c3-up : Vec3,
    c3-fov : Integer,
    c3-near : Integer,
    c3-far : Integer
  }

  Light3D =
    | DirectionalLight (Vec3) (Rgb) (Integer)
    | PointLight (Vec3) (Rgb) (Integer) (Integer)
    | SpotLight (Vec3) (Vec3) (Rgb) (Integer) (Integer) (Integer)
```

The depth buffer stores fixed-point depth values (scale 1000000 for
precision at distance). Depth test is `less-than` -- closer fragments
win. The rasterizer reuses the existing `fb-tri` scanline infrastructure
but adds per-pixel interpolation for depth, normal, UV, and color using
barycentric coordinates.

Lighting: directional, point, and spot lights. The shading model
extends the existing Raytracer's Phong implementation to work with
interpolated normals (Gouraud for speed, Phong for quality -- selectable
per material).

### 4. Material -- Surface Properties

```
Chapter: Material
  cites Game chapter Color
```

```
  ShadingModel =
    | Unlit
    | FlatShaded
    | GouraudShaded
    | PhongShaded

  Material = record {
    mat-id : Integer,
    mat-albedo : Rgb,
    mat-diffuse : Integer,
    mat-specular : Integer,
    mat-shininess : Integer,
    mat-shading : ShadingModel,
    mat-texture : Maybe TextureId,
    mat-wireframe : Boolean,
    mat-double-sided : Boolean
  }
```

Materials reference textures by ID (index into the asset table). The
texture sampler does nearest-neighbor (integer UV lookup) or bilinear
interpolation (4-tap with fixed-point weights). Texture coordinates
wrap (modulo) by default.

### 5. Texture -- Software Texture Sampling

```
Chapter: Texture
  cites Game chapter Color
  cites Game chapter Rasterizer
```

```
  Texture = record {
    tex-width : Integer,
    tex-height : Integer,
    tex-pixels : List Integer
  }

  SampleMode =
    | NearestNeighbor
    | Bilinear

  WrapMode =
    | WrapRepeat
    | WrapClamp
    | WrapMirror
```

Texture sampling: UV coordinates (0-1000 range) map to pixel
coordinates. Bilinear interpolation blends the four nearest texels
using fixed-point weights. This is the hot path of the renderer --
future SIMD (Vector 4 Integer for RGBA channels) will accelerate it.

### 6. GameLoop -- Fixed-Timestep Game Loop

```
Chapter: GameLoop
  cites Engine chapter Input
  cites Game chapter ECS
```

```
  FrameState = record {
    fs-tick : Integer,
    fs-dt-fixed : Integer,
    fs-dt-actual : Integer,
    fs-accumulator : Integer,
    fs-frame-count : Integer,
    fs-fps : Integer
  }

  GamePhase =
    | PhaseInput
    | PhaseUpdate
    | PhaseRender
    | PhasePresent

  GameConfig = record {
    gc-target-fps : Integer between 15 and 240,
    gc-fixed-dt : Integer,
    gc-max-frame-skip : Integer between 1 and 10,
    gc-width : Integer,
    gc-height : Integer
  }
```

Fixed-timestep update (default 60 Hz, configurable). Variable-timestep
render with interpolation. The accumulator pattern: physics and game
logic run at fixed dt regardless of frame rate. The render phase
interpolates between the previous and current state using the leftover
accumulator time. This gives deterministic simulation with smooth
visuals.

Frame timing uses the HPET tick counter (kernel metadata cell at
`tick-count-addr`, 28672). No OS timer API -- we read the hardware
directly.

### 7. Input -- Unified Input System

```
Chapter: Input
  cites Kernel chapter Keyboard
  cites Foreword chapter Maybe
```

```
  InputState = record {
    inp-keys-down : List Integer,
    inp-keys-pressed : List Integer,
    inp-keys-released : List Integer,
    inp-mouse-x : Integer,
    inp-mouse-y : Integer,
    inp-mouse-dx : Integer,
    inp-mouse-dy : Integer,
    inp-mouse-buttons : Integer,
    inp-mouse-scroll : Integer
  }

  KeyCode =
    | KeyW | KeyA | KeyS | KeyD
    | KeyUp | KeyDown | KeyLeft | KeyRight
    | KeySpace | KeyEscape | KeyEnter | KeyTab
    | KeyShift | KeyCtrl | KeyAlt
    | Key0 | Key1 | Key2 | Key3 | Key4
    | Key5 | Key6 | Key7 | Key8 | Key9
    | KeyF1 | KeyF2 | KeyF3 | KeyF4
    | KeyF5 | KeyF6 | KeyF7 | KeyF8
    | KeyF9 | KeyF10 | KeyF11 | KeyF12
    | KeyBackspace | KeyDelete | KeyHome | KeyEnd
    | KeyPageUp | KeyPageDown
    | KeyMinus | KeyEquals | KeyLeftBracket | KeyRightBracket

  InputAction =
    | ActionMoveForward
    | ActionMoveBack
    | ActionMoveLeft
    | ActionMoveRight
    | ActionJump
    | ActionAttack
    | ActionInteract
    | ActionPause

  InputBinding = record {
    ib-action : InputAction,
    ib-key : KeyCode
  }
```

Three-state key tracking: down (held), pressed (edge this frame),
released (edge this frame). Mouse delta computed from previous frame.
Action mapping layer: game code reads actions, not raw keys. Rebindable
at runtime. Reads from the kernel key buffer (metadata cell at
`key-buffer-addr`, 28680) and PS/2 mouse port.

### 8. AssetTable -- Format-Agnostic Asset Registry

```
Chapter: AssetTable
  cites Engine chapter Mesh
  cites Engine chapter Texture
  cites Engine chapter Material
  cites Foreword chapter Maybe
```

```
  AssetKind =
    | AssetMesh
    | AssetTexture
    | AssetMaterial
    | AssetSound
    | AssetScript

  AssetEntry = record {
    ae-id : Integer,
    ae-kind : AssetKind,
    ae-name : Text,
    ae-loaded : Boolean
  }

  AssetTable = record {
    at-entries : List AssetEntry,
    at-meshes : List Mesh,
    at-textures : List Texture,
    at-materials : List Material,
    at-count : Integer
  }
```

Assets are registered by name and loaded on demand. Meshes can be
constructed procedurally (built-in generators) or loaded from Codex
mesh literals (vertex/index lists in source). Textures similarly --
procedural (checkerboard, gradient, noise) or literal pixel data.

No external file formats (OBJ, FBX, PNG, etc.) in Phase 1 -- those are
other people's formats. Phase 2 adds a CDX asset container (facts in
the repository protocol) for binary mesh and texture data, with the
trust lattice guaranteeing asset integrity.

### 9. Audio3D -- Spatial Audio

```
Chapter: Audio3D
  cites Signal chapter Synth
  cites Signal chapter AudioEffect
  cites Signal chapter Oscillator
  cites Math chapter Quaternion
```

3D positional audio using the existing synthesizer and effects chain.
Distance attenuation (inverse square law, fixed-point). Stereo panning
based on listener-relative angle. Reverb via the existing convolution
module. Output to HDA audio (codex-vm Intel HDA with host waveOut).

---

## Render Pass Architecture

### Forward Rendering (Phase 1)

Single-pass forward renderer with per-pixel Phong lighting:

```
  1. Clear framebuffer + depth buffer
  2. For each visible node (front-to-back by distance):
     a. Compute model-view-projection matrix
     b. Transform vertices
     c. Clip and project
     d. Rasterize triangles with depth test
     e. Shade: ambient + directional + point lights
     f. Texture sample if material has texture
  3. UI overlay (2D compositor on top of 3D scene)
  4. Present (VBE framebuffer write)
```

### Shadow Mapping (Phase 2)

Render depth from light's perspective into a shadow depth buffer.
During the main pass, transform each fragment into light-space and
compare against the shadow depth. Shadowed fragments receive ambient
only.

### Deferred Rendering (Phase 3)

G-buffer pass writes position, normal, albedo, and material ID to
separate render targets. Lighting pass reads the G-buffer and
accumulates light contributions. Decouples geometry from lighting --
many lights at constant geometry cost.

### GPU-Accelerated Rendering (Phase 4)

Dispatch vertex transformation and rasterization to GPU via the PTX/
SPIR-V plugs. The compiler emits `[Device]` functions as GPU kernels
(GpuKernels.md K0-K2). The host sends vertex data over the GPU bridge,
the kernel transforms and rasterizes in parallel, and the result is
read back to the VBE framebuffer. This is where the dual-target GPU
compilation (DualTargetGpuCompilation.md) pays off -- the same Codex
shader code targets NVIDIA and Vulkan hardware.

---

## SIMD Math Integration (Phase 2)

When the SIMD design (SIMD.md Phase 1) lands:

- `Vector 4 (Real approximate)` for 4-component XYZW math (SSE2)
- Mat4 multiply becomes 4 dot products using `vec-reduce-add`
- Vertex transform processes 4 vertices simultaneously
- Pixel blend uses `Vector 4 (Integer between 0 and 255)` for RGBA
- Depth buffer clears use 128-bit stores (4 pixels per instruction)
- Texture sampling does 4-tap bilinear in one vector operation

Expected speedup: 3-4x on transform-bound scenes, 2-3x on
fill-rate-bound scenes. The fixed-point → SIMD path is clean because
PADDQ (packed 64-bit add) operates on the same integer representation
the engine already uses.

---

## AZDO Principles (Approaching Zero Driver Overhead)

Since we have no driver, the overhead is *already* zero. But the
principles still apply to our internal pipeline:

1. **Batch by material.** Sort draw calls by material to minimize
   state changes (shader/texture rebinding in GPU mode).

2. **Persistent mapped buffers.** In GPU mode, vertex data lives in
   a pre-allocated region the GPU kernel reads directly -- no
   copy-per-frame.

3. **Indirect draw.** The scene culler produces a draw list (node ID +
   triangle range). The renderer consumes it without per-object
   dispatch overhead.

4. **Instanced rendering.** Multiple instances of the same mesh share
   the vertex buffer. Each instance carries a transform matrix. On
   GPU, this is a kernel launch with instance ID indexing.

5. **Bindless textures.** In GPU mode, textures are referenced by
   address, not by binding slot. The material carries a texture
   pointer, not a texture unit index.

---

## Existing Subsystem Integration Map

| Engine Need | Existing Chapter | Gap |
|-------------|-----------------|-----|
| ECS | `Game/ECS` | None -- use directly |
| 2D rendering | `Game/Rasterizer`, `Game/Scene2D` | None -- 2D overlay on 3D |
| 3D math | `Math/Matrix4`, `Math/Quaternion`, `Math/Geometry` | None -- use directly |
| Trig | `Math/Cordic` | None -- CORDIC gives sin/cos |
| Color | `Game/Color` | None -- RGB/HSL/pack/unpack |
| Sprites | `Game/Sprite` | None -- 2D billboards |
| Physics | `Sim/Physics` (Verlet) | None -- integrate directly |
| Collision | `Sim/Collision` (AABB, sphere) | Add mesh-AABB generator |
| Particles | `Sim/ParticleSystem` | Add 3D particle emitter |
| Spatial | `Game/Quadtree`, `Game/Octree`, `Sim/SpatialHash` | None -- use Octree for 3D |
| Camera | `Game/GameCamera` | Extend to 3D (look-at, orbit) |
| Pathfinding | `Game/AStar`, `Game/Pathfinding` | None -- 3D navmesh future |
| Terrain | `Game/DiamondSquare`, `Game/CellularAutomata` | Heightmap → Mesh converter |
| Audio | `Signal/Synth`, `Signal/AudioEffect`, `Signal/FFT` | Add 3D spatial panning |
| Animation | `Game/Easing`, `Game/Tween` | Add skeletal animation |
| Netcode | `Game/Netcode` | None -- rollback works for any game |
| Save/Load | `Game/SaveSlot` | None -- serialize game state |
| UI | `UI/Surface`, `UI/Render`, `UI/Widget` | None -- overlay compositor |
| GPU | `Kernel/GpuBridge`, PTX/SPIR-V plugs | Wire kernel dispatch |
| Framebuffer | `Kernel/VgaGraphics` | None -- VBE linear FB |
| Font | `Kernel/BitmapFont` | None -- CBF glyph rendering |
| Input | `Kernel/Keyboard` | Add mouse, action mapping |
| State machine | `Game/StateMachine` | None -- game states |
| Tiles/Hex | `Game/TileMap`, `Game/HexMap` | None -- 2D map layers |
| Noise | `Signal/Perlin`, `Signal/Noise` | None -- procedural gen |
| Curves | `Math/Bezier`, `Math/Spline` | None -- animation paths |

**26 of 30 engine needs are already implemented.** The four gaps are:
Scene3D, Renderer3D, Mesh, and AssetTable. Everything else is wiring.

---

## Phasing

### Phase 1: Software 3D Pipeline -- COMPLETE

- `Engine/Mesh` -- vertex/index types, primitive generators
- `Engine/Scene3D` -- 3D scene graph with transforms
- `Engine/Renderer3D` -- transform → clip → project → rasterize → shade
- `Engine/Material` -- surface properties, shading model selection
- `Engine/Texture` -- software texture sampling
- `Engine/GameLoop` -- fixed-timestep loop with frame timing
- `Engine/Input` -- unified input with action mapping
- `Engine/AssetTable` -- asset registry
- `Engine/Audio3D` -- spatial audio

Deliverable: a spinning textured cube with Phong lighting, depth
buffer, camera control (WASD + mouse look), running as a bootable CDX.

### Phase 2: SIMD + Shadows + Skeletal Animation -- NEXT

- **Shadow mapping (directional light depth pass) -- the next item.**
  Reuse the existing `DepthBuffer`: render depth from the light, then
  compare in light-space during the main pass.
- SIMD math (Vector 4 Real approximate) for transforms -- waits on
  SIMD.md Phase 2
- Skeletal animation (bone hierarchy, skinning) -- `Engine/Skinning`
  already landed
- Heightmap terrain from DiamondSquare → Mesh
- SIMD-accelerated texture sampling and pixel blend

### Phase 3: GPU-Accelerated Rendering

- GPU vertex transform kernel (`[Device]` effect)
- GPU rasterizer kernel (tile-based, parallel fragments)
- GPU texture sampling (shared memory cache)
- Dual-target: PTX for NVIDIA, SPIR-V for Vulkan
- Draw-indirect dispatch from CPU cull results

### Phase 4: Deferred Rendering + Advanced Effects

- G-buffer (position, normal, albedo, material)
- Screen-space ambient occlusion (SSAO)
- Bloom (downsample + Gaussian blur + composite)
- Tone mapping (ACES filmic)
- Anti-aliasing (FXAA -- post-process edge detection)

---

## Memory and Time-Complexity Assessment

**Memory:** Phase 1 uses software rendering. At 1024x768x32bpp:

| Buffer | Size | Notes |
|--------|------|-------|
| Framebuffer | 3 MB | 1024 × 768 × 4 bytes |
| Depth buffer | 6 MB | 1024 × 768 × 8 bytes (i64 depth) |
| Mesh data | ~50 KB typical | Cube: 8 verts × 72 bytes + 36 indices × 8 |
| Scene graph | ~10 KB typical | 100 nodes × ~100 bytes |
| Textures | ~1 MB typical | 256×256 × 4 bytes × 4 textures |
| **Total** | **~10 MB** | Fits easily in 3 GB bare-metal |

At 640x480 (default): framebuffer 1.2 MB, depth 2.4 MB -- lighter still.

**Time:** Per-frame budget at 60 FPS = 16.67 ms.

| Stage | Cost (1000-triangle scene, 640x480) |
|-------|--------------------------------------|
| Cull | O(N nodes), ~50 μs for 100 nodes |
| Transform | O(V vertices), ~200 μs for 3000 verts |
| Clip + Project | O(T triangles), ~100 μs for 1000 tris |
| Rasterize + Shade | O(P pixels), ~5 ms for 307K pixels |
| Depth clear | O(W×H), ~300 μs |
| FB present | O(W×H), ~300 μs (VBE MMIO writes) |
| **Total** | **~6 ms** -- well within 16.67 ms |

The bottleneck is rasterization (fill rate). SIMD (Phase 2) and GPU
(Phase 3) directly attack this. The software renderer is the baseline --
it proves correctness before we optimize.

**Heap:** All per-frame allocations use bivy scratch (reclaimed every
frame). Persistent data (meshes, textures, scene graph) lives on the
deck. No allocation in the render hot path.

---

## Comparison: What the LinkedIn Expert Listed

| "Requirement" | Codex Status |
|---------------|-------------|
| Renderer pipeline | 7-stage software pipeline with depth buffer |
| SIMD math library | Designed (SIMD.md), 12 math chapters exist |
| ECS system | Done -- `Game/ECS`, BitSet masks, 256 entities |
| Asset manager | `Engine/AssetTable` -- meshes, textures, materials |
| Renderer abstraction | Software baseline, GPU via plugs (Phase 3) |
| Lighting | Phong shading with directional + point lights |
| Shaders | GPU shaders via PTX/SPIR-V plugs |
| Render passes | Forward (done), Shadow (P2), Deferred (P4) |
| CPU-GPU communication | `GpuBridge` (serial COM3→CUDA proxy) |
| AZDO principles | No driver = zero overhead by construction |
| Specific API | We are the API. No Vulkan, no DX, no GL. |
| Frustum culling | `Engine/Culling` -- 6-plane extraction, AABB test |
| Backface culling | Screen-space winding order test |
| Near-plane clipping | Sutherland-Hodgman in homogeneous coords |
| Skeletal animation | `Engine/Skinning` -- bones, keyframes, 4-weight LBS |
| Post-processing | `Engine/PostProcess` -- bloom, ACES tonemap, FXAA |
| Debug visualization | `Engine/DebugDraw` -- wireframe, AABB, grid, axes, stats |
| Texture sampling | `Engine/Texture` -- nearest/bilinear, wrap/clamp/mirror |
| Game loop | `Engine/GameLoop` -- fixed timestep, accumulator pattern |
| Input system | `Engine/Input` -- 3-state keys, mouse, action mapping |
| Multiplayer bridge | `Engine/HelmBridge` -- voice hierarchy, game events |
