# Cinematic Engine -- CodexMagic Scene Renderer

## Status

**Not started.** Nothing in this document has been built.

There is no scene renderer, no layer stack, no camera rail, no narration
system. There is no `Scene` or `Camera` chapter in `codex.foreword.ui`.
The CSS parallax stand-in described below as "what we build now" was
never built either: `apps/games/codexmagic/web/welcome.html` exists, but
it contains no parallax, no canvas, no `translateZ`, and no
`perspective` -- only the Ashenvale lore text. The design is a design.

The WASM renderer remains blocked on the Spark WASM backend.

## Vision

A WASM-compiled scene renderer that produces layered parallax
landscapes with simple animations and narration overlays. Used for:

- **Landing page** -- first thing a visitor sees before login
- **Cutscenes** -- story beats between game events
- **Card reveals** -- dramatic pack-opening sequences
- **World building** -- environment art that establishes tone

The renderer is built in Codex, compiled through the WASM backend
(Spark project), and embedded in the web client via a canvas element.
It replaces static HTML/CSS parallax with a real-time composited scene
that can animate, respond to scroll/mouse, and play narrated sequences.

## Architecture

### Scene Graph

A scene is a stack of **layers**, back to front. Each layer has:

```
Layer = record {
  depth : Integer,          -- parallax depth (higher = farther)
  image : Text,             -- asset path or procedural generator ID
  offsetX : Integer,        -- horizontal offset (pixels)
  offsetY : Integer,        -- vertical offset (pixels)
  scrollFactor : Decimal,   -- 0.0 = fixed, 1.0 = full scroll speed
  animation : Maybe Animation,
  opacity : Decimal         -- 0.0 to 1.0
}
```

### Parallax Math

Camera position `(cx, cy)` drives layer offsets:

```
  layer-screen-x = layer.offsetX - cx * layer.scrollFactor
  layer-screen-y = layer.offsetY - cy * layer.scrollFactor
```

Deeper layers (higher depth) get lower scrollFactor. A mountain at
depth 100 with scrollFactor 0.1 barely moves as the camera pans. A
foreground tree at depth 10 with scrollFactor 0.9 moves almost 1:1.

### Camera

The camera follows a **rail** -- a sequence of keyframes:

```
CameraKeyframe = record {
  time : Decimal,      -- seconds from scene start
  x : Integer,
  y : Integer,
  zoom : Decimal,
  easing : Text        -- 'linear', 'ease-in', 'ease-out', 'ease-in-out'
}
```

The opening scene: camera starts above clouds (y high, zoom wide),
descends through cloud layer (cloud layers at depth 50-60 with high
opacity that fades as camera passes through), reveals mountain (depth
80-90, scrollFactor 0.15), continues descending and panning right to
reveal harbor town (depth 95, scrollFactor 0.08).

### Animation Types

```
Animation =
  | Drift (dx : Decimal) (dy : Decimal)   -- constant velocity
  | Bobble (amplitude : Integer) (period : Decimal)  -- sine wave
  | Fade (fromOpacity : Decimal) (toOpacity : Decimal) (duration : Decimal)
  | Sprite (frames : List Text) (fps : Integer)
```

Clouds drift. Water shimmers (sprite animation). Birds bobble.
Mountain is static. Town has chimney smoke (sprite + drift).

### Narration

Text overlays timed to camera keyframes:

```
NarrationCue = record {
  time : Decimal,
  text : Text,
  position : Text,     -- 'top', 'center', 'bottom'
  duration : Decimal,
  fadeIn : Decimal,
  fadeOut : Decimal
}
```

### Spark Integration

The Spark WASM backend compiles Codex to WebAssembly. The scene
renderer would be a Codex library (`codex.foreword.ui.Scene` or
similar) that:

1. Receives a scene description (layers, camera rail, narration cues)
2. Runs a requestAnimationFrame loop
3. Composites layers onto a canvas using 2D context
4. Interpolates camera position along the rail
5. Applies parallax math per layer
6. Renders narration text with fade timing

Until Spark WASM is ready, the landing page uses CSS parallax as a
stand-in -- same visual language, implemented with `transform:
translateZ()` and `perspective` in pure CSS/JS. The scene description
format is the same; only the renderer differs.

## Opening Scene -- "Descent to Ashenvale Harbor"

### Layers (back to front)

| Depth | Asset | ScrollFactor | Description |
|-------|-------|-------------|-------------|
| 100 | sky-gradient | 0.0 | Fixed dark-to-light blue gradient |
| 95 | distant-sea | 0.05 | Ocean horizon, subtle wave animation |
| 90 | harbor-town | 0.08 | Fishing village, ~1000 souls, chimney smoke sprites |
| 80 | mountain | 0.15 | Snow-capped peak, left of frame |
| 70 | mid-hills | 0.25 | Rolling green foothills |
| 60 | cloud-layer-far | 0.30 | Thin cirrus clouds, drift right |
| 50 | cloud-layer-near | 0.45 | Thick cumulus, camera descends through these |
| 30 | pine-treeline | 0.65 | Dark evergreen silhouettes |
| 15 | foreground-branch | 0.85 | Close pine branch, partially obscuring view |
| 5 | bird-silhouette | 0.95 | Flying bird, bobble animation |

### Camera Rail

| Time | Position | Zoom | Notes |
|------|----------|------|-------|
| 0s | (0, -200) | 0.8 | Above clouds, wide shot |
| 3s | (0, 0) | 1.0 | Descending through cloud layer |
| 6s | (100, 100) | 1.0 | Mountain revealed on left |
| 10s | (300, 150) | 1.1 | Pan right, harbor comes into view |
| 14s | (400, 120) | 1.2 | Settle on harbor, slight zoom |

### Narration

| Time | Text | Position |
|------|------|----------|
| 1s | "Above the world, the wind carries whispers of power..." | top |
| 5s | "From the peaks of the Ashen Range..." | center |
| 9s | "...to the harbor where mages gather." | center |
| 13s | "Your story begins here." | bottom |

## Interim CSS Parallax (the proposed first step -- NOT built)

The intent was: the landing page (`welcome.html`) uses CSS `perspective`
and `translateZ` on nested divs to simulate the same parallax effect with
gradient/CSS-painted layers. Each layer is a full-viewport div with
`position: absolute`, `transform: translateZ(-Ndpx) scale(S)` where
S compensates for perspective shrinkage. Scroll drives the camera.

None of that is in `welcome.html` today. This remains the cheapest way
to start -- it needs no compiler work and no Spark -- and it is the
recommended first move if this design is picked up.

When WASM Spark is ready, the CSS layers are replaced with a single
`<canvas>` element running the compiled scene renderer. The scene
description doesn't change.

## Dependencies

- **Spark WASM backend** -- the design doc for this does not exist; Gap 9
  in CurrentPlan is the live reference.
- **Codex UI library** -- would need new `Scene` and `Camera` chapters in
  `codex.foreword.ui` (only `Animation.codex` exists there today).
- **Asset pipeline** -- SVG or procedural generation for layer images.
