# Ocean Water -- Wave 1

A runnable model of a real-time ocean surface, in `OceanModel.codex`. It
samples the surface at chosen points and reports displacement, normal,
foam coverage, and shaded colour. This is the simulation a water shader
would run per-vertex/per-pixel, expressed so it can be executed and
checked rather than only rendered.

Three chapters:

| Chapter | What it is |
|---|---|
| `OceanModel.codex` | the model, and nothing else. A library, no entry point |
| `OceanSamples.codex` | the four-sample driver. Prints the readings below |
| `OceanRender.codex` | draws the model to the GOP framebuffer as an image |

`OceanModel` carried the sampling driver until `OceanRender` needed to cite
it, and a chapter with an `opening` cannot be cited by a program that has
its own. The driver moved out unchanged; the readings are identical.

## Run it

```powershell
build/compile.ps1 -Src shaders/water/OceanSamples.codex -Out ocean.cdx -Log ocean.log
tools/codex-vm.exe -kernel ocean.cdx -headless -output ocean.out
Get-Content ocean.out
```

Sample output (`time = 3.0`):

```
sample (0, 0):
  pos=(-0.809, -0.893, 0.041)  n=(0.497, 0.867, -0.047)  foam=0.0
  rgb=(0.041, 0.208, 0.289)
sample (20, -8):
  pos=(19.453, -1.170, -7.873)  n=(0.412, 0.867, -0.279)  foam=0.0
  rgb=(0.044, 0.184, 0.268)
```

Both points are ordinary open water: normal near straight up, no foam,
deep blue-green, a little darker at `(20,-8)` where the surface is in a
trough and the view angle is shallower.

**These numbers changed on 2026-07-27 and the old ones are worth knowing
about.** This block used to read `pos=(19.379, 4.431, -7.740)`,
`n=(0.440, -0.809, -0.389)`, `foam=1.0` at `(20,-8)`, and the paragraph
under it explained at length that the surface had risen 4.43, folded past
vertical, driven its horizontal-fold Jacobian negative and thrown foam --
"that crest-pinch to foam link is the whole point of the Jacobian
criterion". None of it happened. The compiler was emitting an integer
compare for Real `<` and `>`, which reverses when both operands are
negative, so the range reduction in this chapter's own math kernel
returned garbage for some phases and `w-sin` answered outside `[-1, 1]`.
The fold was a miscompile wearing a physical explanation. The criterion
is real and it does fire -- `OceanRender` reports 97 foam pixels on the
crests it draws -- but not at any of these four points.

## Rendering it

`OceanRender.codex` evaluates the model over a frustum-shaped grid, projects
the displaced points through a look-at camera, and scanline-fills the facets
straight into the GOP framebuffer, back-to-front by depth. It carries
wavelength level of detail, because a grid that holds its density constant on
screen samples the far water below the Nyquist rate of the shortest wave and
turns it all to foam.

```powershell
build/compile.ps1 -Src shaders/water/OceanRender.codex -Out ocean-render.cdx -Log ocean-render.log
tools/codex-vm.exe -kernel ocean-render.cdx -headless -input NUL -output ocean-render.out `
  -mem 3072 -gop-width 640 -gop-height 400 -screenshot ocean.bmp -screenshot-delay 2500
```

The capture is a timer that fires while the guest is still running, so the
driver holds the frame on the PIT tick after drawing and the capture ends the
run partway through that hold. The `held =` line is therefore absent from a
run that took a screenshot, and present in one that did not.

**This is what found the Real comparison miscompile.** The first renders came
back as screen-spanning slivers and then as a 99-per-cent-white field, and the
cause was not in this directory: Real `<`, `>`, `<=` and `>=` were emitted as
an integer compare of the IEEE-754 bit patterns, which reverses when both
operands are negative. That broke the range reduction in this chapter's own
math kernel -- `w-floor` compares two negatives, `w-fold-quadrant` compares
against `-pi/2` -- so `w-sin` returned 225.6 instead of a value in `[-1, 1]`
and the surface reported 95-unit waves from amplitudes summing to 2.57.

Fixed the same day; `codex/test/real-compare-negative.codex` is the pin.
Sampling four points had missed it for the life of the model, and drawing a
few thousand did not.

## The four techniques

### 1. Sum-of-sines (Gerstner) displacement

Each wave `i` has a unit direction `d`, amplitude `A`, wavelength `L`
(wave number `k = 2π/L`), crest speed, and steepness `Q`. The phase at a
horizontal position `p = (x,z)` and time `t` is

```
phase = k · (d·p) + t · speed · k
```

The surface is displaced by the sum over all waves:

```
disp.x += Q·A·d.x·cos(phase)     -- crests pull forward (sharpens peaks)
disp.z += Q·A·d.y·cos(phase)
disp.y += A·sin(phase)           -- and rise
```

Pure sines give round swells; the `Q·cos` horizontal term is what makes
Gerstner waves peak and trough like real water. Push `Q` too high and the
surface loops over itself -- that overhang is visible at `(20,-8)`.

### 2. Analytic normal

No finite differences. The gradient of the wave sum gives the normal in
closed form:

```
grad.x += d.x · (k·A) · cos(phase)
grad.y += Q  · (k·A) · sin(phase)
grad.z += d.y · (k·A) · cos(phase)

normal = normalize( -grad.x, 1 - grad.y, -grad.z )
```

Exact, cheap, and stable at any tessellation.

### 3. Jacobian foam

Foam forms where the horizontal displacement folds -- where crests pinch
and the surface compresses. That is measured by the Jacobian of the
`(x,z)` displacement:

```
Jxx += Q·(k·A)·d.x·d.x·sin(phase)
Jzz += Q·(k·A)·d.y·d.y·sin(phase)
Jxz += Q·(k·A)·d.x·d.y·sin(phase)

J    = (1 - Jxx)(1 - Jzz) - Jxz²
foam = saturate( (foamThreshold - J) · foamSharpness )
```

When `J` drops toward zero (or negative), the surface is folding →
foam. In a full engine this feeds a compute shader that **accumulates**
foam into a texture with per-frame decay, so foam lingers as trails; the
model computes the instantaneous criterion that compute shader would
inject each frame.

### 4. GGX specular + Fresnel + subsurface scattering

The sun highlight is Cook-Torrance GGX: distribution `D`, Smith geometry
`G`, Schlick Fresnel `F`. Water is a dielectric, so `F0 ≈ 0.02` -- most of
its grazing-angle brightness is Fresnel reflection toward the sky colour,
not the specular lobe. Subsurface scattering adds the green glow of light
passing through a lit crest toward the eye:

```
sssDir = normalize( -L + N·distort )
sss    = saturate( V · -sssDir ) ^ power · scale
```

Final colour: deep↔shallow by view angle, blended to sky by Fresnel, plus
SSS tint, plus sun specular, then lerped to foam-white by foam coverage.

## Parameter guide

Waves are in `ocean-waves`; everything else is a named constant in the
**Scene Configuration** section.

| Constant | Meaning | Try |
|----------|---------|-----|
| `Wave.amp` / `wavelen` / `speed` | per-wave size, spacing, motion | geometric falloff across the set |
| `Wave.steep` (`Q`) | crest sharpness | keep `Σ Q·k·A ≤ 1` or the surface loops |
| `ocean-roughness` | GGX lobe width | `0.02`-`0.15` for calm→choppy |
| `ocean-f0` | water reflectance at normal incidence | `0.02` (dielectric) |
| `ocean-foam-threshold` / `-sharpness` | when/how hard foam appears | raise threshold for more foam |
| `ocean-sss-distort/-scale/-power` | subsurface look | `power` is an integer exponent |
| `ocean-*-color` | palette (RGB in 0..1) | deep, shallow, sky, sun, sss, foam |

## Notes on the port to a GPU

- The wave loop and the Jacobian are identical in a vertex/compute
  shader; only `sin`/`cos` swap to hardware intrinsics (no range
  reduction needed -- the GPU handles large phases).
- Foam wants a persistent texture: a compute kernel evaluates the
  Jacobian per texel over the water plane and does `foam = max(prev·decay,
  inject)` so trails fade. The model evaluates the per-sample `inject`.
- Real math kernel (range-reduced `sin`/`cos`, Newton `sqrt`) exists only
  because this runs on the CPU model; it is not needed on the GPU.
