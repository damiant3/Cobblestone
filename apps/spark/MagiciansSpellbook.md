# The Magician's Spellbook -- Codex.Spark

A complete guide to the 82-module creative suite written in pure Codex.
No floating point. No OS. No GPU driver. Just integers, logic, and a
bare-metal framebuffer.

---

## Table of Contents

1. [What Is Spark](#what-is-spark)
2. [Architecture Overview](#architecture-overview)
3. [Coordinate System](#coordinate-system)
4. [3D Modeling](#3d-modeling)
5. [Scene Graph and Rendering](#scene-graph-and-rendering)
6. [Camera and Viewport](#camera-and-viewport)
7. [Lighting](#lighting)
8. [Materials](#materials)
9. [Textures and Procedural Noise](#textures-and-procedural-noise)
10. [Image Editor (Canvas)](#image-editor-canvas)
11. [Brush Engine](#brush-engine)
12. [Selection Tools](#selection-tools)
13. [Image Adjustments](#image-adjustments)
14. [Layer Effects](#layer-effects)
15. [Animation System](#animation-system)
16. [Skeletal Animation](#skeletal-animation)
17. [Audio and DAW](#audio-and-daw)
18. [DSP Effects](#dsp-effects)
19. [MIDI](#midi)
20. [Video Compositing](#video-compositing)
21. [Constructive Solid Geometry](#constructive-solid-geometry)
22. [Shape Builder](#shape-builder)
23. [Project Management](#project-management)
24. [Undo System](#undo-system)
25. [Input and Commands](#input-and-commands)
26. [Panel Layout](#panel-layout)
27. [Display Output (VGA)](#display-output-vga)
28. [Module Reference](#module-reference)

---

## What Is Spark

Codex.Spark is a creative suite written entirely in the Codex language.
It runs on bare metal -- no operating system, no libc, no floating point
unit. All math uses fixed-point integers with a scale factor of 1000
(so `1.0` is represented as `1000`, `0.5` as `500`, etc.).

Spark covers:

- **3D modeling**: meshes, CSG booleans, subdivision, extrusion, lathe
- **3D rendering**: software rasterizer, depth buffer, flat shading,
  wireframe, multi-pass pipeline
- **Image editing**: layered canvas, blend modes, brushes, filters,
  histogram, levels/curves, selections, layer effects
- **Animation**: keyframe tracks, bezier curves, timeline, skeletal
  rigging with IK, camera paths
- **Audio production**: multi-track mixer, synthesizer instruments,
  MIDI sequencer, DSP (filters, delay, compression, distortion),
  spectrum analysis, waveform visualization
- **Video compositing**: layer-based compositor with transitions
- **Application shell**: panel layout, command palette, property
  inspector, scene outliner, asset browser, undo/redo

Everything renders to a `Framebuf` -- a flat list of packed RGB integers.
That `Framebuf` can be blitted to VGA graphics hardware via the
`SparkDisplay` bridge module, or exported as raw pixel data.

---

## Architecture Overview

```
User Input                         Display Output
    |                                    ^
    v                                    |
InputManager ─> CommandSystem ──> UndoIntegration
    |                                    |
    v                                    |
PanelLayout ──> [Viewport|Properties|Timeline|Outliner|Assets]
    |                                    |
    v                                    v
Project ──────> SceneGraph ────> SceneRender ──> Framebuf ──> SparkDisplay
                    |                                              |
              [Mesh, Light,                                   VgaGraphics
               Material,                                    (gfx-put-pixel)
               Camera, ...]                                      |
                                                            Physical Screen
```

### Key Data Flow

1. **Input** arrives as `InputEvent` (key/mouse)
2. **CommandSystem** maps input to named actions via `KeybindingMap`
3. **Project** state updates (object added, moved, etc.)
4. **SceneGraph** holds the hierarchy; **SceneRender** rasterizes it
5. **Framebuf** holds the pixel result
6. **SparkDisplay** blits to VGA hardware via `gfx-put-pixel`

---

## Coordinate System

All spatial values use **fixed-point scale 1000**:

| Real Value | Codex Integer | Meaning |
|------------|---------------|---------|
| 1.0        | 1000          | One unit |
| 0.5        | 500           | Half unit |
| -2.5       | -2500         | Negative |
| 0.001      | 1             | Minimum precision |

**World axes**: X = right, Y = up, Z = toward viewer (right-handed).

**Colors** are packed as `R * 65536 + G * 256 + B` where each channel
is 0-255. So pure red = 16711680, pure green = 65280, pure blue = 255,
white = 16777215, black = 0.

**UV coordinates**: 0 = left/top, 1000 = right/bottom.

**Angles**: milliradians. Full circle = 6283 (2 * pi * 1000).

---

## 3D Modeling

### Mesh Data Structure

The core mesh type is `TriMesh` (in `Mesh.codex`):

```
TriMesh = record {
  tm-verts    : List Vec3,      -- vertex positions
  tm-uvs      : List Vec2,      -- texture coordinates
  tm-normals  : List Vec3,      -- vertex normals
  tm-faces    : List TriFace,   -- triangle index triples
  tm-vert-count  : Integer,
  tm-face-count  : Integer
}

TriFace = record { tf-v0 : Integer, tf-v1 : Integer, tf-v2 : Integer }
```

### Creating Meshes

**Primitives** (in `MeshPrimitives.codex`):

```
mesh-cube 1000              -- unit cube (size 1000 = 1.0)
mesh-sphere 600 8 12        -- sphere: radius, rings, sectors
mesh-plane 6000 6000        -- ground plane: width, depth
```

The sphere generates `rings * sectors * 2` triangles. Higher values
give smoother spheres but cost more memory and render time. Start with
`8 12` (192 faces) and go up from there.

**Manual construction**:

```
let m = tri-mesh-empty
in let m2 = tri-mesh-add-vert m (vec3-new 0 0 0) (vec2-new 0 0) (vec3-new 0 1000 0)
in let m3 = tri-mesh-add-vert m2 (vec3-new 1000 0 0) (vec2-new 1000 0) (vec3-new 0 1000 0)
in let m4 = tri-mesh-add-vert m3 (vec3-new 500 1000 0) (vec2-new 500 1000) (vec3-new 0 1000 0)
in tri-mesh-add-face m4 0 1 2
```

Each vertex needs position (Vec3), UV (Vec2), and normal (Vec3).

### Mesh Operations (MeshOps.codex)

```
mesh-subdivide cube           -- Catmull-Clark-style subdivision
mesh-compute-normals mesh     -- recompute face normals
```

### Mesh I/O

**OBJ export** (`ObjFormat.codex`):
```
obj-export cube "MyCube"      -- returns OBJ format text
```

**STL export** (`StlFormat.codex`):
```
stl-export cube "MyCube"      -- returns ASCII STL text
stl-info cube "MyCube"        -- "STL 'MyCube' 12 triangles"
```

### CSG Boolean Operations (MeshBoolean.codex)

```
mesh-union cube sphere        -- combine two meshes
mesh-intersect cube sphere    -- keep only overlapping volume
mesh-difference cube sphere   -- subtract sphere from cube
```

CSG works by ray-casting each face's centroid against the other mesh
to classify faces as inside or outside, then collecting the appropriate
faces based on the operation.

### Shape Builder (ShapeBuilder.codex)

Build meshes from 2D profiles:

```
-- Rectangle path
let rect = path-rect 0 0 1000 500

-- Circle approximation
let circle = path-circle-approx 0 0 500 16

-- Extrude a path into 3D
let box = shape-extrude rect extrude-default

-- Lathe revolve a profile
let vase-profile = path-add (path-add (path-add (path-add path-new 200 0) 300 500) 150 800) 200 1000
let vase = shape-lathe vase-profile lathe-default
```

`extrude-default` gives depth=1000, 1 segment, no taper, no twist.
`lathe-default` gives 12 segments, full 360-degree revolution.

---

## Scene Graph and Rendering

### Scene Graph (SceneGraph.codex)

The scene graph is a flat list of nodes with parent references:

```
SceneGraphNode = record {
  sgn-name     : Text,
  sgn-kind     : SceneNodeKind,    -- SnkEmpty | SnkMesh(idx) | SnkCamera | SnkLight(idx) | SnkGroup
  sgn-position : Vec3,
  sgn-rotation : Quat,
  sgn-scale    : Vec3,
  sgn-parent   : Integer,          -- parent node ID (0 = root)
  sgn-visible  : Boolean,
  sgn-id       : Integer
}
```

**Building a scene**:

```
let sg = scene-graph-new
in let sg2 = sg-add-mesh sg "Cube" 0 0         -- name, mesh-index, parent-id
in let sg3 = sg-set-position sg2 0 (vec3-new 0 500 0)
in let sg4 = sg-add-mesh sg3 "Sphere" 1 0
in let sg5 = sg-set-position sg4 1 (vec3-new 2000 600 0)
```

The mesh index refers to the position in the `SceneRenderCtx` mesh list.

### Render Context (SceneRender.codex)

```
let ctx = render-ctx-new cam lights
in let ctx2 = render-ctx-add-mesh ctx cube        -- index 0
in let ctx3 = render-ctx-add-mesh ctx2 sphere     -- index 1
in let ctx4 = render-ctx-add-mesh ctx3 plane      -- index 2
```

### Full Scene Render

```
let fb = scene-render sg ctx 640 480 2631720
```

Arguments: scene graph, render context, viewport width, viewport height,
background color. Returns a `Framebuf`.

### Wireframe Render

```
let fb = scene-render-wireframe sg ctx 640 480 0 16777215
```

Extra arguments: background color, wire color.

### Render Modes (RenderModes.codex)

```
let cfg = render-config-default                   -- starts in Solid mode
in let fb = rm-render-scene sg ctx cfg 640 480

-- Cycle through modes: Solid -> Wireframe -> Flat -> Textured -> X-Ray -> BBox
let cfg2 = render-config-cycle cfg
```

Available modes: `RmSolid`, `RmWireframe`, `RmFlatShade`, `RmTextured`,
`RmXray`, `RmBoundingBox`.

### Multi-Pass Rendering (RenderPass.codex)

```
let rp = render-pipeline-default 640 480 2631720
in let fb = rp-execute rp sg ctx
```

The default pipeline runs: Shadow -> Geometry -> Lighting -> PostProcess.
Individual passes can be enabled/disabled:

```
let rp2 = rp-disable-pass rp 0    -- disable shadow pass
```

---

## Camera and Viewport

### Camera (SceneCamera.codex)

```
-- Perspective camera (FOV = 1047 milliradians ~= 60 degrees)
let cam = spark-camera-new 640 480

-- Orthographic camera
let ortho = spark-camera-ortho 640 480 5000   -- extent = 5000 units

-- Get matrices
let view = spark-camera-view cam
let proj = spark-camera-proj cam
let vp   = spark-camera-vp cam                -- view * projection combined
```

### Camera Controls

```
let cam2 = spark-camera-orbit cam 100 50       -- yaw, pitch (milliradians)
let cam3 = spark-camera-pan cam2 200 0         -- dx, dy (world units)
let cam4 = spark-camera-zoom cam3 (-500)       -- negative = zoom in
```

### Viewport Navigation (ViewportNav.codex)

Full mouse-driven navigation state:

```
let nav = nav-default                          -- orbit at distance 5000
let nav2 = nav-begin-drag nav NavOrbit 100 200 -- start orbiting
let nav3 = nav-drag nav2 150 180               -- mouse moved
let nav4 = nav-end-drag nav3

-- Get results
let cam-pos = nav-camera-position nav4
let view-mat = nav-view-matrix nav4

-- Presets
let front = nav-front nav                      -- look from front
let top = nav-top nav                          -- look from top
let right-view = nav-right nav                 -- look from right
```

Modes: `NavOrbit` (rotate around pivot), `NavPan` (slide laterally),
`NavZoom` (dolly in/out), `NavFly` (free movement).

### Camera Paths (CameraPath.codex)

Animated camera for cinematics:

```
let cp = cam-path-new CamDolly
in let cp2 = cam-path-add cp (cam-waypoint (vec3-new 0 1000 5000) vec3-zero 785 0)
in let cp3 = cam-path-add cp2 (cam-waypoint (vec3-new 5000 2000 0) vec3-zero 785 60)
in let wp = cam-path-eval cp3 30               -- interpolate at frame 30
in let view = cam-path-view-matrix cp3 30      -- get view matrix at frame 30
```

Each waypoint stores: position, look-at target, FOV, and frame number.
Interpolation is linear between waypoints.

---

## Lighting

### Light Types (SceneLight.codex)

```
light-directional (vec3-new 577 577 577) 16777215 800
-- direction (normalized), color, intensity (0-1000)

light-point (vec3-new 0 2000 0) 5000 16777215 600
-- position, radius, color, intensity

light-ambient 16777215 200
-- color, intensity
```

Direction vectors should be normalized (length ~1000). The direction
`(577, 577, 577)` is roughly `(1,1,1)` normalized.

### Light Setup (LightEditor.codex)

```
let lts = light-preset-apply PresetStudio
-- Gives: key light + fill light + rim light

let lights = light-setup-to-list lts
-- Export to a List SparkLight for the render context
```

Available presets: `PresetSunlight`, `PresetStudio`, `PresetInterior`,
`PresetDramatic`, `PresetNight`.

---

## Materials

### Basic Materials (SceneMaterial.codex)

The render context uses a `MaterialLibrary` where each mesh index maps
to a `Material` with an albedo color.

### PBR Materials (MaterialEditor.codex)

```
PbrMaterial = record {
  pbr-name              : Text,
  pbr-albedo            : Integer,     -- base color (packed RGB)
  pbr-metallic          : Integer,     -- 0-1000
  pbr-roughness         : Integer,     -- 0-1000
  pbr-emission          : Integer,     -- emission color
  pbr-emission-strength : Integer,     -- 0-1000
  pbr-opacity           : Integer,     -- 0-1000
  pbr-albedo-tex        : Integer,     -- texture ID (-1 = none)
  pbr-normal-tex        : Integer      -- texture ID (-1 = none)
}
```

Built-in presets: `pbr-default`, `pbr-metal`, `pbr-glass`, `pbr-emissive`.

```
let mat = pbr-new "MyMetal" 11184810
in let mat2 = pbr-set-metallic mat 900
in let mat3 = pbr-set-roughness mat2 200

-- Shade a pixel
let color = pbr-shade mat3 800 700    -- light intensity, view-dot-normal
```

---

## Textures and Procedural Noise

### Textures (TextureMap.codex)

```
-- Solid color texture
let tex = texture-new 64 64 16711680

-- Checkerboard
let checker = texture-checkerboard 64 64 8 16777215 0

-- From framebuffer
let tex2 = texture-from-framebuf some-fb

-- Sample at UV coordinate (0-1000)
let color = texture-sample tex 500 250

-- Configure wrap and filter
let tex3 = texture-set-wrap tex WrapRepeat      -- WrapRepeat | WrapClamp | WrapMirror
let tex4 = texture-set-filter tex FilterBilinear -- FilterNearest | FilterBilinear
```

### Procedural Noise (NoiseTexture.codex)

```
let cfg = noise-config-default
-- NoiseGradient, seed=42, scale=100, 4 octaves, persistence=500, lacunarity=2000

let tex = noise-to-texture cfg 128 128

-- Configure
let cfg2 = noise-config-new NoiseCellular 99 200

-- Sample a single point
let val = noise-fbm 50 80 cfg    -- returns 0-1000
```

Noise types: `NoiseValue` (blocky), `NoiseGradient` (Perlin-style smooth),
`NoiseCellular` (Worley/Voronoi cells).

---

## Image Editor (Canvas)

### Canvas (Canvas.codex)

```
let cv = canvas-new 512 512 16777215           -- width, height, background

-- Add layers
let cv2 = canvas-add-layer cv "Paint"
let cv3 = canvas-add-layer cv2 "Effects"

-- Set active layer
let cv4 = canvas-set-active cv3 1

-- Pixel operations
let cv5 = canvas-set-pixel cv4 100 200 16711680
let color = canvas-get-pixel cv5 100 200

-- Layer visibility and opacity
let cv6 = canvas-set-layer-visible cv5 0 False
let cv7 = canvas-set-layer-opacity cv5 1 700

-- Flatten to single framebuf
let flat = canvas-flatten cv7
```

### Blend Modes

Each layer has a `BlendMode`: `BlendNormal`, `BlendMultiply`,
`BlendScreen`, `BlendOverlay`, `BlendAdditive`.

### Filters (ImageFilter.codex)

```
let cv2 = filter-grayscale cv
let cv3 = filter-invert cv
let cv4 = filter-blur cv
```

### Export (ExportImage.codex)

```
let ie = export-framebuf flat
let summary = export-summary ie    -- "512x512 786432 bytes"
```

---

## Brush Engine

### Basic Brush (Brush.codex)

```
let br = brush-new 6 16711680 1000   -- size, color, opacity

-- Stroke from (10,10) to (50,50)
let cv2 = brush-stroke cv br 10 10 50 50

-- Single stamp at (32,32)
let cv3 = brush-stamp cv br 32 32
```

### Advanced Brush (BrushEngine.codex)

```
let ab = advanced-brush-new 12 16711680 TipRound
-- Tip shapes: TipRound | TipSquare | TipDiamond | TipSoft

-- Configure dynamics
let dyn = BrushDynamics {
  bd-size-pressure = True,         -- pressure affects size
  bd-opacity-pressure = False,     -- pressure affects opacity
  bd-scatter = 50,                 -- random offset
  bd-spacing = 250,                -- distance between stamps (scale 1000)
  bd-rotation = 0,                 -- fixed rotation
  bd-rotation-jitter = 100         -- random rotation
}
let ab2 = advanced-brush-set-dynamics ab dyn

-- Paint a stroke
let points = [
  StrokePoint { stp-x = 10, stp-y = 10, stp-pressure = 500 },
  StrokePoint { stp-x = 30, stp-y = 25, stp-pressure = 800 },
  StrokePoint { stp-x = 50, stp-y = 20, stp-pressure = 1000 }
]
let cv2 = brush-engine-stroke cv ab2 points 3
```

The `TipSoft` tip applies a distance-based falloff for airbrush-like
painting.

---

## Selection Tools

### Rectangular Marquee

```
let sel = sel-rect 512 512 100 100 300 300
-- canvas width, height, x0, y0, x1, y1
```

### Elliptical Marquee

```
let sel = sel-ellipse 512 512 256 256 100 80
-- canvas w, h, center-x, center-y, radius-x, radius-y
```

### Lasso (Polygon)

```
let points = [
  LassoPoint { lp-x = 100, lp-y = 100 },
  LassoPoint { lp-x = 300, lp-y = 150 },
  LassoPoint { lp-x = 250, lp-y = 350 }
]
let sel = sel-lasso 512 512 points 3
```

### Magic Wand

```
let sel = sel-magic-wand flat 256 256 30
-- framebuf, seed-x, seed-y, color tolerance
```

### Combining Selections

```
let combined = sel-combine sel-a sel-b SelAdd
-- SelReplace | SelAdd | SelSubtract | SelIntersect
```

---

## Image Adjustments

### Histogram and Levels (ImageLevels.codex)

```
let hist = histogram-compute flat
let mean = hist-mean (hist.hist-lum) (hist.hist-pixel-count)

-- Auto levels
let lvl = levels-auto hist
let adjusted = levels-apply flat lvl

-- Manual levels
let lvl2 = LevelsConfig {
  lv-in-black = 20, lv-in-white = 230,
  lv-gamma = 1200,
  lv-out-black = 0, lv-out-white = 255
}
let adjusted2 = levels-apply flat lvl2

-- Curves
let crv = curves-add-point (curves-add-point curves-new 64 32) 192 224
let val = curves-eval crv 128
```

### Color Operations (ColorPicker.codex)

```
-- HSV color model (hue 0.0-360.0, sat 0.0-1.0, val 0.0-1.0)
let hsv = hsv-new 120.0 0.8 0.9
let rgb-packed = hsv-to-rgb hsv
let back = rgb-to-hsv rgb-packed

-- Color operations (amount is Real)
let lighter = color-lighten 16711680 0.2
let darker = color-darken 16711680 0.2
let saturated = color-saturate 16711680 0.3
let comp = color-complement 16711680     -- opposite on color wheel
```

### Gradients (Gradient.codex)

```
-- Linear gradient
let grd = gradient-linear 0 0 512 0              -- start-x, start-y, end-x, end-y
let grd2 = gradient-add-stop grd 0 16711680       -- position 0 = red
let grd3 = gradient-add-stop grd2 500 16776960    -- position 500 = yellow
let grd4 = gradient-add-stop grd3 1000 255        -- position 1000 = blue

-- Fill a framebuf with the gradient
let fb = gradient-fill (fb-new 512 1 0) grd4

-- Radial gradient
let radial = gradient-radial 256 256 200          -- center-x, center-y, radius
```

---

## Layer Effects

### Layer Effects (LayerEffects.codex)

Non-destructive effects applied during compositing:

```
-- Drop shadow
let shadow = fx-drop-shadow 0 3 2 2 500
-- color, blur-size, offset-x, offset-y, opacity

-- Outer glow
let glow = fx-outer-glow 16776960 4 300
-- color, size, opacity

-- Stroke
let stroke = fx-stroke 16777215 2 800
-- color, width, opacity

-- Build effect stack
let stack = effect-stack-add (effect-stack-add effect-stack-new shadow) glow

-- Apply to a layer
let fb = effect-stack-apply output-fb layer stack
```

---

## Animation System

### Keyframe Tracks (Keyframe.codex)

```
let track = keytrack-new "position-x"
in let track2 = keytrack-add-linear track 0 0        -- frame 0, value 0
in let track3 = keytrack-add-linear track2 30 5000    -- frame 30, value 5000
in let track4 = keytrack-add-linear track3 60 0       -- frame 60, value 0

let val = keytrack-eval track4 15                      -- interpolate at frame 15
```

### Bezier Curves (CurveEditor.codex)

```
let bz = bezier-track-new "bounce"
in let bz2 = bezier-track-add-preset bz 0 0 CurveEaseIn
in let bz3 = bezier-track-add-preset bz2 30 5000 CurveEaseOut
in let bz4 = bezier-track-add-preset bz3 60 0 CurveBounce

let val = bezier-track-eval bz4 15
```

Presets: `CurveLinear`, `CurveEaseIn`, `CurveEaseOut`, `CurveEaseInOut`,
`CurveBounce`.

### Animation Channels (AnimChannel.codex)

Channels bind tracks to object properties:

```
let channels = anim-channel-set-new
in let ch2 = anim-add-channel channels 0 PropPosX track-x
in let ch3 = anim-add-channel ch2 0 PropPosY track-y
```

Properties: `PropPosX`, `PropPosY`, `PropPosZ`, `PropRotX`, `PropRotY`,
`PropRotZ`, `PropScaleX`, `PropScaleY`, `PropScaleZ`, `PropOpacity`.

### Playback (AnimPlayer.codex)

```
let player = anim-player-new channels 30     -- channels, fps
in let player2 = anim-play player
in let player3 = anim-tick player2           -- advance one frame
```

### Timeline (Timeline.codex)

```
let tl = timeline-new player 120             -- player, total-frames
```

### Particle System (ParticleSystem.codex)

```
let particles = particle-pool-new emitter-fire 200 (vec3-new 0 0 0)
-- config, max-particles, origin

let particles2 = particle-pool-tick particles
let particles3 = particle-pool-tick particles2

-- Render particles to framebuf
let fb = particle-pool-render (fb-new 640 480 0) particles3 640 480
```

Presets: `emitter-fire` (yellow-to-red, cone), `emitter-snow` (white,
box spread, slow), `emitter-sparks` (bright, fast, sphere burst),
`emitter-config-default` (generic point emitter).

---

## Skeletal Animation

### Armature (Armature.codex)

```
-- Build from scratch
let arm = armature-new "Robot"
in let arm2 = armature-add-bone arm (bone-new "Root" (-1) vec3-zero quat-identity 200)
in let arm3 = armature-add-bone arm2 (bone-new "Spine" 0 (vec3-new 0 200 0) quat-identity 300)

-- Or use the humanoid preset (12 bones)
let human = armature-humanoid
```

### Posing

```
let arm2 = armature-set-pose arm 3 (vec3-new 0 200 0) (quat-rotate-x 500)
let tip = armature-bone-tip arm2 3     -- world-space endpoint of bone 3
```

### Inverse Kinematics (IkSolver.codex)

```
let chain = ik-chain-new [4, 5] 2 (vec3-new (-500) 500 0)
-- bone indices, chain length, target position

let result = ik-solve arm chain
-- result.ikr-armature   -- the solved armature
-- result.ikr-converged  -- True if target was reached
-- result.ikr-iterations -- how many CCD passes
-- result.ikr-error      -- remaining distance to target
```

### Weight Painting (WeightPaint.codex)

```
let wmap = weight-map-new (mesh.tm-vert-count) 4
in let wmap2 = wp-assign-uniform wmap 0 0      -- vert 0 fully weighted to bone 0
in let wmap3 = wp-assign-blend wmap2 5 0 700 1 300  -- vert 5: 70% bone 0, 30% bone 1

-- Paint brush
let brush = weight-brush-new 100 200 PaintAdd 0
-- radius, strength, mode, bone-index
-- Modes: PaintAdd | PaintSubtract | PaintSmooth | PaintReplace

-- Visualize weights as heat map
let fb = wp-visualize (fb-new 320 240 0) wmap mesh 0 320 240
```

---

## Audio and DAW

### Synthesizer (SynthInstrument.codex)

```
let tone = inst-generate-tone inst-piano 440 44100 44100
-- preset, frequency-Hz, duration-samples, sample-rate

-- Returns List Integer of PCM samples (-1000 to 1000)
```

Presets: `inst-piano` (sine, soft attack), `inst-bass` (square, punchy),
`inst-lead` (sawtooth), `inst-pad` (triangle, slow attack).

### Audio Tracks (Track.codex)

```
let clip = audio-clip-new tone 0 1000          -- samples, start-frame, length
let track = track-add-clip (audio-track-new "Piano") clip
```

### Mixer (Mixer.codex)

```
let mx = mixer-new 44100 120                   -- sample-rate, bpm
in let mx2 = mixer-add-track mx track

let mix = mixer-render mx2 44100               -- render N samples
-- Returns MixResult { mr-left, mr-right, mr-length }
```

### FX Chain (FxChain.codex)

```
let fx = fx-chain-new
in let fx2 = fx-chain-add fx fx-preset-reverb
in let fx3 = fx-chain-add fx2 fx-preset-delay
```

### Export (ExportAudio.codex)

```
let ae = export-mix mx2 44100
let peak = export-audio-peak ae
```

---

## DSP Effects

### Filters (AudioDsp.codex)

```
-- Low-pass filter (alpha = cutoff, 1-999)
let filtered = dsp-lowpass-buffer samples count 200

-- High-pass filter
let filtered2 = dsp-highpass-buffer samples count 800
```

### Delay

```
let dl = delay-new 4410 500 300
-- delay-samples, feedback (0-1000), wet-mix (0-1000)

let processed = delay-process-buffer dl samples count
```

### Compressor

```
let comp = compressor-new 20000 4000 100 200
-- threshold, ratio, attack, release
```

### Distortion

```
let distorted = dsp-distort-buffer samples count 2000
-- samples, count, drive amount
```

### Spectrum Analyzer (SpectrumAnalyzer.codex)

```
let spectrum = spectrum-analyze samples count 44100
-- Returns SpectrumData with 128 frequency bands

let fb = spectrum-render (fb-new 256 128 0) spectrum StyleBars 65280 0
-- Styles: StyleBars | StyleLine | StyleFilled
```

---

## MIDI

### MIDI Tracks (MidiTrack.codex)

```
let midi = midi-track-new "Lead" 0              -- name, channel
in let midi2 = midi-add-note midi 0 60 100 480  -- tick, note, velocity, duration
in let midi3 = midi-add-note midi2 480 64 90 480
in let midi4 = midi-add-note midi3 960 67 80 480
```

MIDI note 60 = middle C. Velocity 0-127. Duration in ticks (480 = quarter note at standard PPQ).

### Sequencer (Sequencer.codex)

```
let seq = sequencer-new 120                     -- BPM
in let seq2 = seq-add-midi seq midi-track
in let seq3 = seq-add-audio seq2 audio-track
```

---

## Video Compositing

### Compositor (VideoCompositor.codex)

```
let vc = compositor-new 640 480 24 120          -- width, height, fps, duration

let layer = video-layer-new "Background" 0 120  -- name, in-point, out-point
let layer2 = video-layer-add-frame layer frame-fb

let vc2 = compositor-add-layer vc layer

-- Render a single frame
let output = compositor-render-frame vc2 30     -- frame number
```

### Transitions

```
let trans = transition-crossfade 60 15          -- start-frame, duration
let blended = transition-apply frame-a frame-b trans 67
```

Types: `TransCut`, `TransCrossfade`, `TransWipeLeft`, `TransWipeDown`,
`TransFadeBlack`.

---

## Constructive Solid Geometry

See [3D Modeling](#3d-modeling) above. The three operations:

```
mesh-union a b        -- A + B
mesh-intersect a b    -- A * B
mesh-difference a b   -- A - B
```

Works on any `TriMesh`. Performance is O(faces-A * faces-B) for the
classification step.

---

## Shape Builder

See [3D Modeling](#3d-modeling) above. Key functions:

```
path-new                                -- empty path
path-add path x y                       -- add point
path-close path                         -- close the path
path-rect x y w h                       -- rectangle shortcut
path-circle-approx cx cy r segments     -- circle shortcut

shape-extrude path config               -- extrude into 3D mesh
shape-lathe path config                 -- revolve around Y axis

path-fill fb path color                 -- 2D scanline fill onto framebuf
```

---

## Project Management

### Project (Project.codex)

```
let proj = project-new 640 480                           -- viewport size
in let proj2 = project-add-object proj "Cube" cube 16711680
in let proj3 = project-add-object proj2 "Sphere" sphere 65280
in let proj4 = project-set-mode proj3 ModeAnimate
```

Modes: `ModeModel`, `ModeAnimate`, `ModeRender`, `ModeImage`, `ModeAudio`,
`ModeStage`.

### Serialization (ProjectFile.codex)

```
let text = project-serialize proj
-- "SPARK 1 Untitled 2\nOBJ 0 name=Cube verts=24 faces=12...\nEND\n"

let summary = project-summary proj
-- "Untitled: 2 objects, 33 verts, 14 faces"
```

### Transform Hierarchy (NodeTransform.codex)

```
let th = transform-hierarchy-new
in let th2 = th-add th (transform-new (vec3-new 0 500 0) quat-identity (vec3-new 1000 1000 1000))
in let th3 = th-add-child th2 (transform-new (vec3-new 2000 0 0) quat-identity (vec3-new 500 500 500)) 0

let world = th-world-matrix th3 1     -- child's world transform
let pos = th-world-position th3 1     -- child's world position
```

---

## Undo System

### Undo Manager (UndoIntegration.codex)

```
let um = undo-manager-new 100                  -- max entries

-- Push actions
let um2 = undo-push um (ActMoveObject 0 old-pos new-pos) "Move Cube"
let um3 = undo-push um2 (ActSetColor 0 old-color new-color) "Color Cube"

-- Undo / Redo
let um4 = undo-pop um3
let um5 = redo-pop um4

-- Query
let can-undo = undo-can-undo um3      -- True
let desc = undo-peek-desc um3         -- "Color Cube"
```

Action types: `ActAddObject`, `ActDeleteObject`, `ActMoveObject`,
`ActRotateObject`, `ActScaleObject`, `ActSetColor`, `ActCanvasPaint`,
`ActSetMode`.

---

## Input and Commands

### Input Manager (InputManager.codex)

```
let state = input-state-new

-- Process events
let state2 = input-process state (EvMouseDown 100 200 BtnLeft no-modifiers)
let state3 = input-process state2 (EvMouseMove 150 220 no-modifiers)
let state4 = input-process state3 (EvMouseUp 150 220 BtnLeft)

-- Query drag
let dx = input-drag-dx state3         -- 50
let dragging = input-is-dragging state3  -- True
```

### Keybindings

```
let km = keymap-default
-- G=grab, R=rotate, S=scale, X=delete, Ctrl+Z=undo, Ctrl+Shift+Z=redo

let action = keymap-lookup km 71 no-modifiers   -- "grab" (G key = ASCII 71)
```

### Command System (CommandSystem.codex)

```
let reg = cmd-registry-default                  -- 15 built-in commands

-- Search the command palette
let results = cmd-search reg "Scale"

-- Execute
let result = cmd-success "grab"
let result2 = cmd-failure "save" "No filesystem available"
```

---

## Panel Layout

### Layout (PanelLayout.codex)

```
-- Default layout: viewport + properties + timeline
let layout = layout-default 1024 768

-- With outliner
let layout2 = layout-with-outliner 1024 768

-- Hit test
let panel-idx = layout-panel-at layout2 500 400

-- Focus a panel
let layout3 = layout-focus-panel layout2 panel-idx

-- Toggle visibility
let layout4 = layout-toggle-panel layout3 2     -- hide/show panel 2
```

Panel types: `PanelViewport`, `PanelProperties`, `PanelTimeline`,
`PanelOutliner`, `PanelAssetBrowser`, `PanelConsole`.

### Scene Outliner (SceneOutliner.codex)

```
let ols = outliner-from-scene sg selection
let ols2 = outliner-select ols 2
let selected = outliner-selected-index ols2
```

### Property Inspector (PropertyInspector.codex)

```
let ins = inspector-from-object (project-get-object proj 0)
-- Generates property entries for Name, Visible, Position, Scale, Color, Vertices, Faces
```

### Asset Browser (AssetBrowser.codex)

```
let cat = asset-catalog-new
in let cat2 = asset-register cat "Cube" AkMesh 1024
in let cat3 = asset-register cat2 "Checker" AkTexture 4096
in let cat4 = asset-tag cat3 0 "primitive"

let filtered = asset-filtered (asset-set-filter cat4 AkMesh)
```

### Clipboard (Clipboard.codex)

```
let clip = clipboard-new
in let clip2 = clip-copy-mesh clip cube "viewport"
in let has = clip-has-mesh clip2               -- True
```

---

## Display Output (VGA)

### SparkDisplay Module

The bridge from Spark's `Framebuf` to physical pixels:

```
-- Initialize VGA
let mode = gfx-640x480                         -- 640x480x32 via Bochs VBE
let font = cbf-init                            -- bitmap font for HUD

-- Create display target
let dt = display-target-new mode font

-- Or centered (auto-offset small framebuf in larger screen)
let dt2 = display-target-centered mode font 320 240

-- Or scaled (fit framebuf to screen with aspect ratio)
let dt3 = display-target-scaled mode font 320 240

-- Clear screen
let w = display-clear dt 0

-- Blit a rendered scene to hardware
let w2 = display-blit dt scene-fb

-- HUD overlay
let w3 = display-hud dt [
  HudLine { hl-text = "Codex.Spark", hl-color = rgb-white },
  HudLine { hl-text = "FPS: 30", hl-color = rgb-green }
] 2
```

### Complete Graphics Demo (SparkGfxDemo.codex)

The `SparkGfxDemo` entry point demonstrates the full pipeline:

1. `gfx-640x480` -- set VGA to 640x480 32-bit
2. Build scene (cube, sphere, ground plane)
3. `scene-render` -- rasterize to Framebuf
4. `display-blit` -- copy pixels to VGA hardware
5. `particle-pool-render` -- overlay particle effects
6. `display-hud` -- draw status text

To run it, compile `SparkGfxDemo.codex` as a CDX or ELF target and
boot it in codex-vm. The VM's VGA display window shows the rendered
scene.

---

## Module Reference

### Phase 1-5: Core (40 modules)

| Module | Purpose |
|--------|---------|
| Mesh | Half-edge and triangle mesh data structures |
| MeshPrimitives | Cube, sphere, plane, cylinder, cone, torus |
| MeshOps | Subdivision, normal computation |
| DepthBuffer | Z-buffer for depth testing |
| SceneCamera | Perspective/orthographic camera, orbit/pan/zoom |
| SceneLight | Directional, point, ambient lights with Phong shading |
| SceneMaterial | Material library for mesh rendering |
| SceneNode | Scene graph node helpers |
| SceneGraph | Hierarchical scene tree with transforms |
| SceneRender | Full scene traversal and rendering |
| Pipeline3D | Vertex transform, backface cull, triangle rasterization |
| ObjFormat | Wavefront OBJ export |
| Canvas | Layered pixel canvas with blend modes |
| Brush | Basic brush with stroke and stamp |
| ImageTool | Canvas manipulation tools |
| ImageFilter | Grayscale, invert, blur filters |
| LayerStack | Layer management operations |
| ExportImage | Framebuf to raw pixel export |
| Keyframe | Linear keyframe tracks |
| AnimChannel | Property-to-track bindings |
| AnimPlayer | Playback engine with tick |
| Timeline | Timeline state with scrubbing |
| ExportVideo | Video frame export |
| Track | Audio track with clips |
| Mixer | Multi-track audio mixdown |
| FxChain | Audio effect chain |
| MidiTrack | MIDI note sequence |
| Sequencer | MIDI + audio sequencer |
| SynthInstrument | Instrument presets and tone generation |
| WaveformView | Audio waveform visualization |
| ExportAudio | Audio mixdown export |
| Project | Project state (objects, camera, mode) |
| Selection | Multi-item selection state |
| History | Undo stack |
| Toolbar | Editor toolbar state |
| StatusBar | Status bar information |
| Viewport | Viewport state |
| Workspace | Panel layout management |
| RenderQueue | Batch render queue |
| App | Text-mode entry point (exercises all modules) |

### Phase 6-7: Extended (20 modules)

| Module | Purpose |
|--------|---------|
| TextureMap | 2D textures, UV sampling, bilinear filter, procedural |
| ColorPicker | HSV/RGB conversion, color operations |
| Gradient | Linear/radial gradients with multi-stop interpolation |
| CurveEditor | Cubic bezier keyframes with presets |
| NodeTransform | Hierarchical transform composition |
| TransformGizmo | Visual axis handles for manipulation |
| GridSnap | Configurable grid with subdivision snapping |
| UvEditor | UV island management and wireframe display |
| RenderPass | Multi-pass render pipeline |
| ProjectFile | Text-based project serialization |
| ParticleSystem | Emitter/pool particle effects |
| TextRender | 5x7 bitmap font rendering |
| AudioDsp | Filters, delay, compressor, distortion |
| CameraPath | Dolly/orbit/spline camera animation |
| MaterialEditor | PBR materials with presets |
| UndoIntegration | Action-based undo/redo across editors |
| BrushEngine | Pressure-sensitive brush with tip shapes |
| MeshBoolean | CSG union/intersect/difference |
| Armature | Skeletal bone hierarchy with skinning |
| ShapeBuilder | Path extrusion, lathe, 2D scanline fill |

### Phase 8: Advanced (10 modules)

| Module | Purpose |
|--------|---------|
| IkSolver | CCD inverse kinematics |
| WeightPaint | Vertex weight painting with heat map viz |
| StlFormat | ASCII STL import/export |
| ImageLevels | Histogram, levels, curves adjustment |
| LayerEffects | Drop shadow, glow, stroke |
| VideoCompositor | Layer-based video compositing with transitions |
| NoiseTexture | Perlin, cellular, fBm procedural noise |
| SelectionTools | Marquee, ellipse, lasso, magic wand |
| SpectrumAnalyzer | FFT-based audio visualization |
| ViewportNav | Orbit/pan/zoom camera navigation |

### Phase 9: Application Shell (10 modules)

| Module | Purpose |
|--------|---------|
| InputManager | Keyboard/mouse events, drag state, keybindings |
| CommandSystem | Command registry, palette search, shortcuts |
| PanelLayout | Resizable panel regions with hit testing |
| PropertyInspector | Object property display and editing |
| SceneOutliner | Scene hierarchy tree view |
| Clipboard | Typed copy/paste (mesh, pixels, audio, text) |
| RenderModes | Solid/wireframe/x-ray mode switching |
| AssetBrowser | Asset catalog with search, tags, filtering |
| LightEditor | Interactive light setup with presets |
| MeasureTool | Distance, angle, area measurement with overlay |

### Display (2 modules)

| Module | Purpose |
|--------|---------|
| SparkDisplay | Framebuf-to-VGA bridge with scaling and HUD |
| SparkGfxDemo | Graphical demo entry point |

---

## Tips and Pitfalls

**Fixed-point overflow.** `1000 * 1000 = 1,000,000` is fine for 64-bit
integers, but chains like `a * b / 1000 * c / 1000` can overflow if
intermediate values exceed ~2^62. Break long chains into lets.

**Color packing.** Always use `R * 65536 + G * 256 + B` with each channel
0-255. The unpacking pattern is: `r = px / 65536`, `g = px / 256 - r * 256`,
`b = px - px / 256 * 256`.

**Vec3 is in Quaternion.** The `Vec3` type and `vec3-new`, `vec3-zero`,
`vec3-add`, etc. are defined in `Math chapter Quaternion`, not in a
separate vector module. Always `cites Math chapter Quaternion` for any
3D math.

**Vec2 is in Geometry.** The `Vec2` type and `vec2-new` are in
`Math chapter Geometry`.

**Mat4 is in Matrix4.** The 4x4 matrix type and all matrix operations
(`mat4-mul`, `mat4-translate`, `mat4-from-quat`, `mat4-perspective`,
`mat4-look-at`, `mat4-transform-vec4`) are in `Math chapter Matrix4`.

**Framebuf is in Rasterizer.** The `Framebuf` type and all drawing
primitives (`fb-new`, `fb-set`, `fb-get`, `fb-line`, `fb-rect`, `fb-tri`)
are in `Game chapter Rasterizer`.

**Rgb is in Color.** The `Rgb` record type (used by VGA functions) is in
`Game chapter Color`. Convert packed integers with `rgb-from-packed`.

**Negative literals need parens.** Write `vec3-new (-500) 0 0`, not
`vec3-new -500 0 0` (the latter parses as subtraction).

**No floating point.** Everything is integers. Scale by 1000 for
decimal-like precision. Use `isqrt` for square roots (Newton's method).
Trig uses Taylor series approximations (accurate within ~1% for small
angles, good enough for rendering).

---

*82 modules. Zero dependencies. Bare metal.*
