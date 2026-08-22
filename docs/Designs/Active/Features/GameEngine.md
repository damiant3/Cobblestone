# Codex Game Engine -- General-Purpose, Bare-Metal, Self-Hosted

## Status

**Ruling 2026-08-05 (Damian): Phase 2 rides A6.** The shadow-mapping /
SIMD / terrain remainder begins with whoever takes CurrentPlan's A6 (3D
on screen); this document is that owner's design.

**Phase 1 is complete, and the Phase 2 shadow-mapping deliverable is
complete** (red, A6 arc, dev CLs 13644/13663/13676/13695): near-plane
clipping (Sutherland-Hodgman in clip space), per-pixel lit shading
(Gouraud and Phong paths, SpotLight3D both), frustum and backface
culling wired into the render path, and shadow mapping
(`r3d-render-shadowed`: ortho light frustum from the scene's world
bounds, depth-only pass, per-vertex light-space coords interpolated per
pixel; bias `r3d-shadow-bias = 3000`). Per-pixel code is integer-only
and allocation-free, pinned by `engine-render-heap`; the calibrated
tests are `engine-near-clip`, `engine-shading`, `engine-culling-cost`,
`engine-shadow` (`engine-culling-cost` and `engine-texture-cost` were
`engine-culling` and `engine-texture` until 2026-08-22, when the stems
collided with the `forewords/` smoke tests of the same name in one
`test-output` directory and the battery reported the wrong one).

**The tree, measured 2026-08-06: 42 chapters, 7,916 lines in
`codex/foreword/engine/`.** The 9+4 chapter inventory this section used
to carry was two baselines stale; re-measure before quoting (L-COUNT).

**Remaining Phase 2 items, corrected 2026-08-18 (val): SIMD math and
skeletal animation, and nothing else from the A6 arc.** This paragraph
listed three more and **all three were already done**, checked against
the source rather than against the list:

- `Texture.codex` **is** wired into the render path. `r3d-ctx-with-tex`
  puts `mat.emat-texture` in the shade context and `r3d-tex-px` samples
  it per pixel through `etx-get` (`Renderer3D.codex`), and the desk's 3D
  pane renders a textured ground in every capture.
- The mesh generators **exist**: `mesh-sphere`, `mesh-cylinder`,
  `mesh-cone` and `mesh-torus` are all in `Mesh.codex`, beside cube,
  plane and pyramid. `gsc-scene` builds its ball from `mesh-sphere 700
  12 8` and its ring from `mesh-torus 650 220 12 8`.
- GopScene **does** call the shadowed entry, on both paths:
  `r3d-render-shadowed` for software and `gs-render-shadowed` for the
  host rasterizer, chosen by the `S` toggle (`GopScene.codex`).

The lesson is L-COUNT's, one level along: a stale ITEM list rots the
same way a stale count does, and this one was three for three.

### First shadow-cost numbers, 2026-08-18 (val)

**Metal, from the diagnostic stick** (`HardwareSitting.md`, i7-6700K,
software render of a 624x88 region): plain **12 ms** over 20 frames,
shadowed **24 ms** over 11.

**"Shadows double the frame" was my sentence and a second geometry has
already qualified it.** A 2026-08-19 sitting (red; banked at
`stick-archive\diag2-20260819\DIAG.TXT`, kernel `800A7683`, same board, fb
1920x1080) rendered **624x40** and read plain **5 ms** over 42 frames,
shadowed **12 ms** over 21. That is 2.4x, not 2.0x, and the direction is the
informative part:

| region | pixels | plain | shadowed | ratio | shadow increment |
|---|---:|---:|---:|---:|---:|
| 624x88 | 54,912 | 12 ms | 24 ms | 2.00 | 12 ms |
| 624x40 | 24,960 | 5 ms | 12 ms | 2.40 | 7 ms |

Area falls to 0.45 of the first. Plain falls to 0.42, near enough to say the
unshadowed frame is fill-bound. **The shadow INCREMENT falls only to 0.58**,
well short of the area ratio, so part of the shadow cost is not paid per
rendered pixel. The candidate is the depth pass, which is over the shadow MAP
(256x256, fixed) and not over the render region, and it is the same component
the bed's map-size arm already isolated: 16x less map area bought 26 ms of a
169 ms shadow cost, making the build about 28 ms and the main pass about 141.

**Do not read the split below as measured.** Solving `increment = F + P *
area` on these two points gives F about 2.8 ms fixed and P about 1.7e-4 ms per
pixel, and **two points fitting a two-parameter model is exact by
construction** -- that arithmetic cannot fail and is therefore not evidence
for the model, only a statement of what the model would have to be. A THIRD
geometry on the same board is what would test it, and the prediction to write
down first is that a 624x140 region reads plain about 19 ms and shadowed about
36 ms if the fixed component is real, against 38 ms if the whole increment
scales with area. The two answers differ by enough to call.

The practical consequence if it holds: caching the shadow map is worth MORE at
small regions, not less, because the fixed depth-pass build is a larger share
of a cheaper frame. Stage 1 found the per-pixel half is tap-bound rather than
arithmetic-bound; this is the other half of the same question.

**Bed, 1600x900 with a 1440x800 render region**, each arm sampled over a
full 60-second orbit period so the camera angle averages out: plain
**96 ms**, shadowed **265 ms**, shadowed with the map at 64 instead of
256 **239 ms**.

**What that settles, and it redirects the campaign.** Cutting the shadow
map sixteen-fold in area bought 26 ms of a 169 ms shadow cost, so the
depth-pass BUILD is roughly 28 ms and about 141 ms is the per-pixel work
in the main pass. Caching the map for a static scene -- the obvious first
move, and the one this campaign was expected to open with -- is worth at
most 15 per cent and only when the geometry does not move, which rules
out the Aquarium.

**And the per-pixel work is not arithmetic-bound.** Hoisting the
slope-scaled bias out of `r3d-shadow-lit`, which removed twelve minimums
and maximums and a division from every covered pixel, produced **no
measurable change**: interleaved arms on one seed read 148/149/138 ms
before and 164/141/128 ms after. What is left in that loop is the 3x3 PCF
kernel, nine `peek-32` taps into the shadow map per pixel, and the Phong
evaluation for lit pixels. **The next stage should attack the tap count,
not the arithmetic.**

**The bed cannot resolve better than about 10 per cent, and any stage
that claims less must say so.** Identical builds measured 262 to 686
frames in the same 60 seconds when the host was busy; the numbers above
are from a quiet host with the arms interleaved. Rebuilding between runs
is what makes it worst. The metal stick is a dedicated box and its
numbers are the trustworthy ones.

### Stage 2, 2026-08-20 (val): the taps are three quarters of it

Stage 1 concluded the per-pixel shadow cost is tap-bound **by elimination**,
having removed the arithmetic and seen no change. That is an argument, not a
measurement of the taps. This stage measured them.

`r3d-pcf-radius` is now the only place the kernel's tap count is written
down, and `r3d-pcf-taps` is derived from it, so radius 1 is the shipping 3x3
and radius 0 is one tap. **The arm was shown to take before it was trusted**:
`engine-shadow` at radius 0 differs on three lines, the edge stops being a
blend, the sample darkens, and the ambient pixel count goes 1691 to 1929.

Instrument: cube-on-plane, plain and shadowed measured in the SAME binary at
three render sizes, 20 frames each, HPET, so host noise moves both arms
together and the INCREMENT is the reading. Two binaries differing only in the
radius, run interleaved, three passes. Bed, not metal.

| area | plain | shadowed, 9 taps | shadowed, 1 tap | increment, 9 | increment, 1 |
|---:|---:|---:|---:|---:|---:|
| 76,800 | 2,003 us | 5,683 us | 3,719 us | 3,712 us | 1,684 us |
| 172,800 | 4,284 us | 10,960 us | 6,746 us | 6,673 us | 2,464 us |
| 307,200 | 7,724 us | 18,679 us | 11,237 us | 10,957 us | 3,511 us |

**Plain is the control and it holds.** It never reaches the PCF, so the two
arms must agree, and pooled across six runs they do to within a few per cent
at every size. A systematic difference there would have condemned the
harness rather than the kernel.

**Eight taps of the nine are what the difference buys**, so one tap over the
whole frame costs 253, 526 and 931 us at the three sizes, and the nine
together are **61, 71 and 76 per cent of the shadow increment**. My
prediction before the run was about 89 per cent, and it was too high.

The residue after the taps is 1,430, 1,938 and 2,578 us, and it does NOT
scale with area. **Fitting a fixed term plus a per-pixel term on the two
outer points predicts the middle to 1.5 per cent** (1,908 against 1,938),
which is a real test rather than the two-point tautology the 08-18 note
warned about: the fixed term is about 1,050 us, and that is the depth pass,
which is over the 256x256 map and not over the render region.

So at 640x480 on the bed a shadowed frame's extra cost is roughly **76 per
cent PCF taps, 14 per cent other per-pixel work, 10 per cent depth-pass
build.**

**The confound, which was written down before the run.** Radius 0 does not
only remove eight taps, it also abolishes the penumbra, and a fully shadowed
pixel skips Phong. The `engine-shadow` counts bound that at about 240 pixels
of 76,800, a third of a per cent of the frame, far too small to account for
a 55 to 68 per cent fall.

**What this opens.** With three quarters of the increment in the taps, a
cheaper kernel is worth about what it removes. **That sentence used to put a
number on it, "a four-tap kernel would take roughly 44 per cent off", and
stage 3 below withdraws the number while keeping the direction:** it was an
extrapolation on a per-tap rate that turns out not to be constant. Caching
the map remains worth at most the 10 per cent fixed term on a static scene.

### Stage 3, 2026-08-20 (val): the per-tap rate is not a constant, and the knob only goes down

Stage 2 got its per-tap figure by differencing nine taps against one. That is
a rate only if cost is LINEAR in the tap count, and stage 2 ASSUMED that while
carefully testing the equivalent assumption for area. Same trap, one level
along, in the same note that named it. Stage 3 added a third point.

All three arms re-measured on the converged seed **A7EDB7C6**, because the
seed change alters this unit's emitted bytes (175,062 to 175,329) and stage
2's binaries are no longer what the compiler produces. **Stage 2's numbers
were NOT carried forward** (L-COUNT); what follows replaces them. Three
passes, interleaved, 640x480, 20 frames per arm.

| taps | increment | marginal cost per tap |
|---:|---:|---:|
| 1 | 3,474 us | |
| 9 | 10,520 us | 881 us over the 1-to-9 range |
| 25 | 29,630 us | **1,194 us over the 9-to-25 range** |

**The prediction written down first was 25,855 us and the answer came in at
29,630, about 20 per cent above linear.** I had named the superlinear case as
the least likely of three and bet on the opposite one. The marginal cost of a
tap rises 36 per cent between the 3x3 and the 5x5 range, so **stage 2's 931 us
per tap is a LOCAL rate at the shipping radius and not a property of a tap.**

Stage 2's headline survives this intact, and it is worth saying why: the 61,
71 and 76 per cent figures were measured by differencing AT the shipping
radius, so they never depended on linearity. Only the extrapolation to a
cheaper kernel did, and that is the sentence withdrawn above.

**The obvious cause was tested and ruled out.** A wider kernel widens the
penumbra, and a partial pixel evaluates Phong and the blend where a fully
shadowed one returns ambient at once, so the excess could have been penumbra
rather than taps. `engine-shadow`'s ambient count says the penumbra does grow,
1,929 at one tap to 1,691 at nine to 1,470 at twenty-five. It is far too small
to matter: that is about 200 pixels at 320x240, some hundreds once scaled, and
the excess would need 5.6 us per pixel when a whole PLAIN frame of 307,200
pixels costs 7,500 us. Two orders of magnitude out. **The residue is real and
this document does not attribute it** -- the simple cache-line story predicts
the opposite sign, since rows of a 256x256 map are 1,024 bytes apart so the
line count grows as 3 to 5 while the taps grow as 9 to 25.

**The finding that matters is not the timing.** At radius 2 `engine-shadow`
reports `cube pixel unchanged : no`: the 5x5 kernel bleeds onto the caster
itself. **A wider kernel is not a quality setting, it is a defect**, so the
knob's only useful direction is DOWN, which stage 2 already priced. That
closes the campaign's measurement arc: what a cheaper kernel buys is bounded
below by stage 2's differencing and is not to be extrapolated from a rate.

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

**`r3d-pcf-radius` has a hard upper bound of 1, and it is a CORRECTNESS
bound rather than a performance one.** The percentage-closer kernel's
half-width is the engine's one shadow-quality parameter, and raising it is
the intuitive move for a softer edge. It is not available. At radius 2 the
kernel reaches map texels belonging to the CASTER and the caster begins
shadowing itself: `engine-shadow`'s `cube pixel unchanged` assertion holds at
radius 1 and fails at radius 2, measured 2026-08-20 on cube-on-plane. Radius
2 is where it was caught, not a proof that 1 is the exact edge; 3 and above
were not measured because 2 already refuses. **The only useful direction on
this constant is down**, trading a soft edge for speed, and stage 3 below has
what that costs.

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
