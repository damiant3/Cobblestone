# Triplanar Terrain -- Wave 3

A runnable model of triplanar-mapped terrain with height-blended material
layers. A world position goes in, a lit surface colour comes out.

Three chapters, the split the library uses:

| Chapter | What it is |
|---|---|
| `TerrainModel.codex` | the model, and nothing else. A library, no entry point |
| `TerrainSamples.codex` | reports the field, the projection weights, and the blend |
| `TerrainRender.codex` | raymarches the height field into an image |

It cites both earlier waves. `OceanModel` carries the Real math kernel and
`V3`; `CloudModel` carries the lattice noise, the fractal sum, and
`c-remap`. Terrain adds a ridged sum, the triplanar projection, and the
height blend. Dead-code elimination drops the rest.

## Run it

```powershell
build/compile.ps1 -Src shaders/terrain/TerrainSamples.codex -Out terrain.cdx -Log terrain.log
tools/codex-vm.exe -kernel terrain.cdx -headless -input NUL -output terrain.out -mem 3072
```

```powershell
build/compile.ps1 -Src shaders/terrain/TerrainRender.codex -Out terrain-render.cdx -Log terrain-render.log
tools/codex-vm.exe -kernel terrain-render.cdx -headless -input NUL -output terrain-render.out `
  -mem 3072 -gop-width 640 -gop-height 400 -screenshot terrain.bmp -screenshot-delay 20000
```

About twenty seconds. The delay has to land after the draw and before the
guest exits; the renderer clears to magenta first so a capture that lands
early is obvious rather than plausible.

## The readings

```
valley   h = 136.741559969215472  n = -0.002337248836018 0.999996664911973 -0.00109883247567
ridge    h = 73.927843594607509  n = 0.002073145434686 0.999928856896803 -0.011746625603088
slope    h = 81.682525823406848  n = -0.326896569756599 0.924068549240607 -0.198080662826355
flat     w = 0.0 1.0 0.0  sum = 1.0
wall-x   w = 1.0 0.0 0.0  sum = 1.0
wall-z   w = 0.0 0.0 1.0  sum = 1.0
diagonal w = 0.5 0.5 0.0  sum = 1.0
near  wa=0.4 ha=0.55 hb=0.45 -> r=0.441624365482233 b=0.558375634517766
near  wa=0.5 ha=0.55 hb=0.45 -> r=0.541871921182266 b=0.458128078817734
near  wa=0.6 ha=0.55 hb=0.45 -> r=0.674846625766871 b=0.325153374233128
far   wa=0.5 ha=0.8 hb=0.2 -> r=0.932203389830508 b=0.067796610169491
far   wa=0.5 ha=0.2 hb=0.8 -> r=0.067796610169491 b=0.932203389830508
```

The projection weights are printed on their own because they are the one
quantity that is easy to get wrong and impossible to check in an image: a
weight set that does not sum to one, or that favours the wrong axis on a
wall, still produces a picture that looks textured. Flat ground is pure top
projection, each wall is pure on its own axis, the diagonal splits evenly,
and every set sums to exactly 1.0.

The blend lines are the technique's whole character. At **equal** weights
and close heights it mixes, and the mix tips with the weight. At equal
weights and **divergent** heights it does not mix -- the taller material
takes 93% of the result, and the split reverses exactly when the heights
reverse. That asymmetry is the point: a lerp cannot do it, and it is what
puts grass into the hollows of the rock instead of drawing a soft grey band
along a contour.

## The model

### Ridged noise

Mountains are not the same signal as hills. A plain fractal sum is
symmetric about its mean and gives rolling dunes; folding it about its
midpoint and squaring puts a crease at every ridge line. Both are summed,
because a landscape that is all ridges is as wrong as one that is all
dunes.

### Triplanar projection

A height field has no texture coordinates. Projecting a pattern straight
down works until the ground tilts, and on a cliff a downward projection
smears into vertical streaks. Triplanar projects three times, once down
each axis, and blends by how much the surface faces that axis -- so
whichever projection would stretch is exactly the one whose weight is near
zero.

The sharpness exponent is what makes this work rather than merely average.
At sharpness 1 all three contribute nearly everywhere and the result is a
mush of three misaligned copies; raising it concentrates weight on the
dominant axis so only genuinely diagonal ground blends.

### Height blending

A material carries a colour and a height, and wins where
`weight + depth * height` is larger. At equal weights the taller material
takes the ground, so the boundary follows the shape of the height textures
instead of the shape of the weight field.

## The terracing, and four wrong answers

The first render came out in stair-step contours across every slope. This
is recorded at length because the mistakes are more instructive than the
fix, and because one of them is the expensive kind.

**Wrong answer 1: the march.** The obvious suspect was the ray march. Its
step grows, and bisection is only correct if the ray crosses the surface
once inside the bracket it is given; an uncapped step reaches ~180 m where
the finest terrain octave is ~39 m, so a bracket can hold several
crossings and the bisection converges confidently to the wrong one. That
argument is *correct* and it is *not what was happening*. The cap was
added and the terraces did not move. A mechanism that plausibly explains
the wrong artifact is worse than no explanation, because it stops the
search.

**What actually located it** were two experiments, each removing one
suspect completely rather than adjusting it:

- Render with a **constant albedo**. The surface came out smooth. That
  clears the height field, the normals, and the march in one image.
- Render with the blends intact but **flat material colours**. The
  terraces stayed. That clears the colour textures.

What was left was the blend, and then it is arithmetic. The boundary lies
where `(wa - wb) + depth * (ha - hb)` crosses zero. Slope and altitude vary
smoothly, so their crossing is a smooth contour of the terrain unless the
height term is big enough to push it around. A 2-octave normalised sum sits
in about [0.35, 0.65], so at depth 0.5 it moved the boundary by ~±0.15
against weights spanning [-1, 1]. Nowhere near enough.

**Wrong answer 2: make the height texture very fine.** Two-metre features
break the contour and then alias into per-pixel speckle, because a pixel
two kilometres out covers about eight metres of ground.

**Wrong answer 3: make the depth large.** At 1.4 the height term overrides
the weight outright and snow appears in patches on low ground, having beaten
the altitude that was supposed to decide the matter.

**Wrong answer 4: leave the ramps wide.** With the original slope and
altitude ranges most of the landscape sat permanently in the contested
zone, so the blend mottled everything like camouflage instead of acting at
a boundary.

The settled values keep the feature size above the pixel footprint, keep
the perturbation comparable to rather than larger than the weight it
perturbs, and narrow the ramps so most ground is decisively one material
and the blend has a thin band to work in.

## Parameters worth turning

| Name | Here | What it does |
|---|---|---|
| `terrain-freq` | 0.0016 | horizontal scale of the field, per metre |
| `terrain-amplitude` | 260.0 | vertical scale, metres |
| `terrain-ridge-weight` | 0.72 | ridged against rolling. All ridges is as wrong as none |
| `terrain-tri-sharpness` | 4 | projection sharpness. 1 is a three-way mush |
| `terrain-splat-scale` | 0.05 | height texture the blend competes on. Finer aliases |
| `terrain-splat-depth` | 0.85 | how hard height counts against weight. Larger ignores the weight |
| `terrain-splat-width` | 0.55 | width of the contested zone. Wider tends to a lerp |
| `terrain-slope-lo/hi` | 0.76 / 0.88 | where rock takes over from grass |
| `terrain-snow-lo/hi` | 178 / 208 | where snow takes over, in metres |
| `tr-max-step` | 24.0 | march step cap. Must stay under the finest terrain feature |

## Why the renderer marches instead of rasterising

The ocean renderer builds a grid and fills triangles because it needs the
surface at controlled positions. Here the surface is a function and can be
asked about anywhere, so marching gives an exact silhouette along every
ridge line rather than one quantised to a grid. The march steps grow with
distance, for the same reason the cloud model's light march does, and is
capped for the reason above.
