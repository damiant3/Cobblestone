# Screen-Space GGX Reflections -- Wave 4

A runnable model of screen-space reflection, and a lake among the terrain
of wave 3 to point it at.

| Chapter | What it is |
|---|---|
| `ReflectModel.codex` | the G-buffer, the projection, the march. A library, no entry point |
| `ReflectSamples.codex` | round-trips every piece that fails silently |
| `ReflectRender.codex` | the two-pass scene: terrain, lake, reflections |

It cites `OceanModel` for the Real kernel, `V3`, and the GGX and Fresnel
terms, and `TerrainModel` for the scene. Wave 4 adds the G-buffer, the
projection, and the screen-space march.

## What SSR actually is

Screen-space reflection is not a way of computing reflections. It is a way
of **reusing a frame that has already been drawn**. The reflected ray is
marched against the depth buffer of the image on screen, and where it
crosses a surface, the colour already sitting in the framebuffer at that
pixel is the answer. That is the whole trick, and it is why it is cheap
enough to be everywhere in real-time rendering.

It is also why it is wrong in specific, recognisable ways, and modelling it
honestly means modelling the failures:

- A ray that leaves the screen has nothing to sample. Faded out toward the
  border, which is why an SSR reflection dissolves at the edges.
- A ray passing *behind* a surface is indistinguishable from one hitting
  it, because the depth buffer stores one distance per pixel and knows
  nothing about what is behind it. Bounded by a thickness.
- Anything not drawn this frame cannot appear in a reflection, ever.
- A reflective surface cannot reflect itself; that is a second-order
  reflection which is not in the frame. Excluded explicitly.

## Run it

```powershell
build/compile.ps1 -Src shaders/reflect/ReflectSamples.codex -Out reflect.cdx -Log reflect.log
tools/codex-vm.exe -kernel reflect.cdx -headless -input NUL -output reflect.out `
  -mem 3072 -gop-width 640 -gop-height 400
```

```powershell
build/compile.ps1 -Src shaders/reflect/ReflectRender.codex -Out reflect-render.cdx -Log reflect-render.log
tools/codex-vm.exe -kernel reflect-render.cdx -headless -input NUL -output reflect-render.out `
  -mem 3072 -gop-width 640 -gop-height 400 -screenshot reflect.bmp -screenshot-delay 15000
```

`-mem 3072` is required, not conventional: the G-buffer lives at
0xA0000000 and a smaller machine does not have that address.

## The readings

```
depth 1234.5 -> 1234.5  exact = yes
depth 0.125 -> 0.125  exact = yes
up       err = 0.0  flag = 1  len = 1.0
tilted   err = 0.002928951808505  flag = 1  len = 1.0
pixel 160,100 -> 160.5,100.5  view-z 499.99800001199992 want 499.99800001199992 agree yes
pixel 40,30 -> 40.500000000000042,30.500000000000028  view-z 1050.142500879038152 want 1050.142500879038152 agree yes
pixel 300,180 -> 300.499999999999943,180.5  view-z 67.145669012268569 want 67.145669012268569 agree yes
down onto flat  -> 0.0 1.0 0.0
oblique onto flat -> 0.6 0.8 0.0
head-on  n.v = 1.0 -> 0.02
45 deg   n.v = 0.7071 -> 0.022112627526333
grazing  n.v = 0.05 -> 0.778305318749999
centre 1.0
edge   0.153846153846153
corner 0.005917159763313
```

Three of these are round-trips, and they are here because each is a place
where a silent error still produces a picture that looks like a reflection.
A depth buffer that quantises, a normal that returns unnormalised, a
projection that disagrees with the ray it came from -- none announce
themselves, they just put the reflection in the wrong place.

Depth is exact because it is stored as an f32 bit pattern rather than
fixed-point. The normal costs 0.003 of angular error for a byte per axis
and comes back unit length. The projection returns the originating pixel,
and its depth agrees with an independently computed `dist * dot(dir,
forward)` -- the check matters because that depth is along the **view
axis**, not along the ray, and equals the ray distance only down the centre
of the screen.

Fresnel runs 0.02 head-on to 0.78 at grazing, which is the number that
decides where this technique is worth anything at all.

## Four things that were wrong, in order

**A world-space march.** The first version stepped through the world and
projected each sample. That works looking down at water and falls apart
looking across it, which is the view worth rendering: at grazing incidence
the reflected ray runs nearly parallel to the surface, so a fixed
world-space step crawls across the screen in places and leaps whole
mountains in others. 15% of water pixels found a hit and the hits were
scattered. Stepping in **screen** space at a fixed pixel stride fixes it by
construction -- no pixel on the ray's path is skipped or visited twice,
whatever the angle. Depth comes along by interpolating its reciprocal,
which is linear in screen space for a straight world ray, and *that* is why
the buffer stores view-axis depth.

**A depth window instead of a crossing.** Requiring one sample to land
within a thickness of the stored surface cannot work near the horizon,
where a sub-two-pixel step spans an enormous change in depth. The ray is in
front of a ridge at one sample and far behind it at the next, never inside
any reasonable window. What decides it is the **sign change** between
consecutive samples: in front, then behind, means it crossed. Thickness
survives, demoted to a guard against a step so coarse the crossing means
nothing.

**A pass that wrote the buffer it read.** Marking each pixel's hit/miss
outcome back into the G-buffer's flag byte looked like a tidy way to count
them. But pass 2 reads that byte: every water pixel already processed
carried its outcome instead of its material, so the self-reflection reject
could never match and the buffer the march depends on was being rewritten
underneath it, row by row. Counting by accumulation instead. **A pass that
consumes a buffer must not also write it.**

**Too few steps for a grazing ray.** The stride rule guarantees no pixel is
skipped; it says nothing about depth resolution. A near-horizontal
reflected ray projects both endpoints close together near the horizon, so a
short pixel span asks for a handful of steps while covering kilometres of
world distance, and the crossing falls between samples. A floor on the step
count is a separate criterion from the stride and both are needed.

There was also a framing error worth naming: the first camera sat 80 m
above the lake looking down, and produced water that was accurately and
uselessly dark. Fresnel for water is about 0.02 head-on. **A reflection
shader has to be pointed at a grazing view or there is nothing to see**,
and that is physics rather than composition.

## Parameters worth turning

| Name | Here | What it does |
|---|---|---|
| `rf-stride` | 1.6 | pixels per march step. Below 1 wastes samples |
| `rf-min-steps` | 72 | depth resolution floor. Separate criterion from the stride |
| `rf-steps` | 190 | ceiling on step count |
| `rf-length` | 4200.0 | how far the reflected ray is followed, in metres |
| `rf-thickness` | 260.0 | rejects a crossing too coarse to trust |
| `rf-bias` | 0.4 | keeps a surface from striking itself at step one |
| `rf-edge-margin` | 26.0 | width of the border fade, in pixels |
| `rr-lake-y` | 108.0 | water line. The field runs 33 to 207 |
| `rr-ripple-amp` | 0.0075 | a slope of a turns the ray by 2a. Small on purpose |
| `rr-water-f0` | 0.02 | Fresnel at normal incidence for water |

## What the counters are for

The renderer prints `water pixels`, `ssr hit` and `ssr miss`. A reflection
pass that runs over zero water pixels and one that runs over thousands and
misses every time produce the same image, and neither announces itself. On
this scene it is about 33000 water pixels with roughly one in eight finding
a hit -- low, and correctly so: most reflected rays at this angle leave the
screen, which is the technique's defining limitation rather than a bug in
this implementation.
