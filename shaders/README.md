# Shader Library

Shading and simulation techniques, modeled in Codex so they actually
**run** -- a point goes in, a lit sample comes out -- instead of living as
GPU source we cannot execute or verify here. Each entry reads like a
book: the math is the code, the code is the explanation.

| Wave | Name | Form | Techniques |
|------|------|------|------------|
| 1 | [Ocean Water](water/) | Codex model + sampler + renderer | Sum-of-sines (Gerstner) displacement, analytic normals, Jacobian foam, GGX specular, subsurface scattering, frustum grid with wavelength LOD |
| 2 | [Volumetric Clouds](clouds/) | Codex model + sampler + renderer | Raymarched density field, value-noise fractal sum, height gradient and detail erosion, Beer-Powder, Henyey-Greenstein, nested light march, jittered steps |
| 3 | [Triplanar Terrain](terrain/) | Codex model + sampler + renderer | Ridged fractal sum, analytic normals by central difference, triplanar projection, height-blended material splat, raymarched height field with bisection refine, aerial perspective |
| 4 | [Screen-Space Reflections](reflect/) | Codex model + sampler + renderer | G-buffer in guest RAM, perspective-correct screen-space march, crossing detection, thickness and edge fade, Schlick Fresnel weighting, two-pass render over the wave 3 terrain |

Wave 2 cites wave 1. `OceanModel` carries the Real math kernel and `V3`,
and was made a library with no entry point so exactly this could happen;
`CloudModel` adds only what clouds need that water did not, which is an
exponential, because Beer's law is an exponential and nothing else.

## Why model the sim, not export a shader

A GPU shader is an artifact you trust and hope. Modeling the simulation
in Codex means the wave field, the foam criterion, and the BRDF are all
evaluated by a program you can compile, run, and check against the
physics -- normals come out unit-length, foam appears exactly where the
surface folds, energy stays bounded. Once the model is right, lowering it
to HLSL/GLSL/WGSL is a transcription, not a guess.

## Conventions

- Each folder is self-contained and carries its own `README.md` with the
  math, the parameter guide, and how to run it.
- Real math the foreword lacks (range-reduced trig, etc.) is built in the
  model's own kernel and documented -- near-zero Taylor approximations are
  not enough for a wave field whose phase grows without bound.
- `show` on `Real` prints readable values; the driver uses
  `print-line-uni` so output is Unicode at the boundary (raw `print-line`
  emits CCE).

## Roadmap (future waves)

- Foliage translucency + wind
