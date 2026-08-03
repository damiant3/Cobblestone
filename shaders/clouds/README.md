# Volumetric Clouds -- Wave 2

A runnable model of raymarched volumetric clouds. A ray goes in, a
scattered colour and a transmittance come out. This is the simulation a
cloud shader evaluates per pixel, written so it can be executed and
checked rather than only rendered.

Three chapters, the same split the ocean uses:

| Chapter | What it is |
|---|---|
| `CloudModel.codex` | the model, and nothing else. A library, no entry point |
| `CloudSamples.codex` | marches a few rays and prints what came back |
| `CloudRender.codex` | draws the sky to the GOP framebuffer as an image |

`CloudModel` cites `Water chapter OceanModel` for the Real math kernel and
`V3`. That chapter was made a library with no entry point precisely so the
next wave could reuse it, and what is reused is the general part:
range-reduced sine, Newton sqrt, saturate, the vector algebra. Nothing
about waves is used, and whole-program dead-code elimination drops it.

## Run it

```powershell
build/compile.ps1 -Src shaders/clouds/CloudSamples.codex -Out clouds.cdx -Log clouds.log
tools/codex-vm.exe -kernel clouds.cdx -headless -input NUL -output clouds.out -mem 3072
```

```powershell
build/compile.ps1 -Src shaders/clouds/CloudRender.codex -Out cloud-render.cdx -Log cloud-render.log
tools/codex-vm.exe -kernel cloud-render.cdx -headless -input NUL -output cloud-render.out `
  -mem 3072 -gop-width 640 -gop-height 400 -screenshot clouds.bmp -screenshot-delay 12000
```

The render takes about twelve seconds and the delay must land **after** the
draw and **before** the guest exits. Both ways of missing it are silent, so
they are worth knowing: too short and the capture lands mid-frame, too long
and no file is written at all. `CloudRender` clears the buffer to magenta
first so an unfinished frame is obvious rather than plausible -- a partial
capture reads as a horizon otherwise, which is exactly what happened here.
The hold after drawing is deliberately long so the delay does not have to be
guessed precisely.

## The readings

```
exp(0)    exp = 1.0
exp(1)    exp = 2.71828182442294
exp(-1)   exp = 0.36787944022894
exp(-10)  exp = 0.000045399929676
exp(5)    exp = 148.41315910183502
below slab  density = 0.0
above slab  density = 0.0
in a gap    density = 0.0
in a cloud  density = 0.341320350235813
same, base  density = 0.204567010482193
same, top   density = 0.0
up       rgb = 0.028977072262981 0.030220932910748 0.032708654206283  alpha = 0.083435539523837
sunward  rgb = 0.686991739305713 0.678179339106521 0.660554538708135  alpha = 0.648169075452627
shallow  rgb = 0.468827206368587 0.477750056950051 0.495595758112979  alpha = 0.960970098479428
down     rgb = 0.0 0.0 0.0  alpha = 0.0
```

The exponential is checked first because everything downstream is one and a
wrong one would be invisible in an image. It agrees with the true values to
about one part in a billion across the range.

The three density lines through one column are the height gradient doing its
job: 0.20 near the base where the cloud is still building, 0.34 in the body,
and 0.0 at the top where erosion has frayed it away. The gap and the points
outside the slab are all zero.

The rays read the way the physics says they should. Sunward is bright and
very slightly warm, because the direct term dominates there and it carries
the sun's colour. The shallow ray is dimmer but *cooler* despite being far
more opaque -- a long grazing path collects mostly the multiple-scattering
floor, which is near-neutral, rather than direct sun. Straight up is nearly
clear at this coverage. Straight down never reaches the slab at all.

## The model

### Value noise and the fractal sum

A lattice noise: hash the eight integer corners of a cell, interpolate with
the quintic fade whose first and second derivatives vanish at both ends.
The fractal sum doubles frequency and halves amplitude per octave and
normalises by the amplitude sum, so it stays in [0,1] at any octave count.

The hash folds its lattice coordinate to 31 bits **before** the mixing
multiply, which keeps the multiply inside a signed 64-bit Integer. Without
the fold a coordinate of a few thousand overflows and the noise seams
exactly where the wrap lands.

### The density field

Coverage remaps the base sum so that raising it lowers the floor and lets
more of the noise range survive. A height gradient ramps density up over the
bottom fifth of the slab and away across the top third, which is what makes
a cloud read as a cloud rather than as noise in a box: cumulus are dense and
rounded underneath where rising air is still saturated, frayed on top where
it has mixed with dry air. The detail sum then erodes the shape from below,
weighted toward the top, which carves the cauliflower edges.

### Beer-Powder

Beer's law alone makes a cloud look like smoke: it darkens monotonically
with depth, so the sunward face goes flat and grey exactly where a real one
is brightest. The powder term corrects it. Near a lit boundary the first
scattering events have not had room to happen yet, so a thin shell just
inside is *darker* than Beer predicts, and that contrast is what gives a
cloud its billowed look. `2 * beer * powder` is the combined form.

### Henyey-Greenstein

An analytic phase function whose single parameter slides from back- through
isotropic to forward-scattering. Water droplets scatter strongly forward,
which is why a cloud between you and the sun has a bright rim. The 1.5 power
is a square root of a cube, not a general power the kernel would have to
grow.

### Two marches

The view ray steps through the slab accumulating scattered light and
attenuating transmittance. At every step holding density, a shorter march
toward the sun measures how much light arrives. That inner march is the
expensive part and the reason cloud shaders cost what they do; it is
deliberately short, five steps over a lengthening stride.

## Three things that were wrong first, and what fixed them

These are kept because each is a mistake worth not repeating, and each was
found by looking at output rather than by reasoning.

**Everything rendered black.** The first extinction was 0.06 per metre. Over
an 831 m light march at typical density that is an optical depth near 50, so
the sunward transmittance was exp(-50) at every lit point. Extinction is now
*derived* rather than dialled: wanting the sun transmittance near exp(-0.7)
fixes it at 0.004.

**Everything then rendered mid-grey.** Single scattering has a ceiling well
below white -- the phase function peaks at 0.61 and Beer-Powder at about
0.75, so the direct term cannot exceed roughly 0.46 however lit the cloud
is. The missing light is real: it is the photons that scattered several
times before reaching the eye. The ambient floor and the sun gain restore it.
Its colour is **not** the sky colour, which was tried and is visibly wrong --
tinting the floor deep blue makes even the sunlit face come out
blue-dominant. Light that has bounced inside a cloud has been scattered by
near-neutral water droplets.

**The render took over five minutes.** The hash was doing three `int-mod`
operations, and `int-mod` is a hardware divide. That line is the hottest in
the model -- every density sample is two fractal sums, every octave is eight
corner hashes, every march step takes a density sample -- so at roughly
thirty cycles against one, those divides *were* the render. Masking with
`bit-and` instead, plus fewer octaves and steps, took it to twelve seconds.
Masking is not the same function as `int-mod` on negative input, but nothing
here needs it to be: a hash only has to be deterministic and well spread.

A fourth, smaller: the first jitter passed raw `x` and `y` to a hash that
combines its arguments linearly, so neighbouring pixels stayed correlated
and the bands came back as a cross-hatch. Scrambling both coordinates into
all three arguments is what actually decorrelates them.

## Parameters worth turning

| Name | Here | What it does |
|---|---|---|
| `cloud-coverage` | 0.50 | the most expressive dial. 0.35 is fair-weather scattered, 0.7 an overcast deck |
| `cloud-extinction` | 0.004 | optical depth per metre. Derived, not dialled -- see above |
| `cloud-erosion` | 0.25 | how hard the detail sum carves the base shape |
| `cloud-anisotropy` | 0.55 | Henyey-Greenstein g. Higher is a tighter forward rim |
| `cloud-ambient` | 0.40 | the multiple-scattering floor |
| `cloud-sun-intensity` | 1.8 | multiple-scattering gain along the sun path |
| `cloud-steps` | 48 | view march steps |
| `cloud-light-steps` | 5 | steps toward the sun per lit sample |
| `cr-forward` | pitch 0.50 | camera pitch. Matters more than anything else in the renderer |

## Why the renderer traces at half resolution

The ocean renderer evaluates its model once per grid vertex and fills the
triangles between them, so its cost is set by the grid rather than by the
screen. A raymarch has no such structure: every pixel walks the volume
itself. Halving each axis is a factor of four for a picture whose content is
low-frequency anyway, which is why real engines trace clouds at quarter
resolution and upscale.

The camera pitch is the other half of that. A camera looking *along* the
slab rather than up into it sends every ray through kilometres of cloud, and
a 2500 m deck seen edge-on saturates to opaque everywhere. The first framing
here used a pitch of 0.15 and produced a flat grey wall with no shape in it.
