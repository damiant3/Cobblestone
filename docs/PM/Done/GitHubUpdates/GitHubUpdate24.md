# GitHub Update 24 -- 2026-06-16

Covers main CLs 4074-4483 (since Update 23 at CL 4073, 2026-06-13).
Three days, 88 copy-ups from four agent streams (val, reek, fester, blu).

## SIMD and Vector types

Codex now has first-class SIMD. `Vector N T` is a type-level construct
where N is a compile-time lane count and T is a numeric element type.
The type checker enforces matching widths -- `Vector 2 Real` is not
`Vector 4 Real`, and mixing them is a type error, not a silent bug.

What shipped:

- `VectorTy(N, T)` and `VectorMaskTy(N)` type constructors
- Arithmetic operators (`+`, `-`, `*`, `/`) overloaded for vectors,
  element-wise, with SSE2 packed codegen (ADDPD, SUBPD, MULPD, DIVPD)
- Comparison operators (`<`, `>`, `<=`, `>=`) produce `VectorMask N`
- `vec-splat`, `vec-extract`, `vec-reduce-add`, `vec-select` builtins
- Vector operator overloading -- `v1 + v2` emits ADDPD directly
- Bounded integers in vector lanes: `Vector 16 (Integer between 0 and 255)`
  is a valid type with per-lane range guarantees

The design (`docs/Designs/Features/Active/SIMD.md`) covers the full
roadmap: Phase 1 (SSE2/NEON, shipped), Phase 2 (AVX/AVX2 via VEX
encoding), Phase 3 (AVX-512/SVE/RISC-V V), Phase 4 (crypto intrinsics).
The portable API is architecture-independent; `suggested-vector-width`
selects the right lane count per target at compile time.

## Real type and approximate equality

`Number` is renamed to `Real` across the entire compiler, all 53 plugs,
and all tests. The qualifier communicates confidence level:

- `Real` -- f64, ~15 significant digits
- `Real approximate` -- f32, ~7 digits (doubles SIMD lane density)
- `Real guess` -- f16, ~3 digits (future, ML inference)

The `~` operator replaces `==` for floating-point comparison:

- `x ~ y` -- approximately equal (4 ULP tolerance, covers typical
  accumulated rounding)
- `x ~0 y` -- bitwise exact (zero tolerance, for hash keys and
  serialization round-trips)

`==` and `/=` on Real types are now compile errors (CDX2085). The
diagnostic explains why and shows the `~` alternative. This eliminates
the entire class of `0.1 + 0.2 == 0.3` bugs by making the programmer
choose between "close enough" and "intentionally exact."

Safety modes compose with precision: `Real trapping` (trap on NaN/Inf),
`Real saturating` (clamp to +-MAX), `Real checked` (return Result).

## GPU plugs -- dual-target compilation

The compiler's plug architecture extends to GPUs. Two new plugs:

- **ptx-plug** -- translates device IR to NVIDIA PTX (target: sm_89,
  Ada Lovelace)
- **spirv-plug** -- translates device IR to Vulkan/OpenCL SPIR-V
  (target: Vulkan 1.2+)

Both plugs are built and compiling as standalone CDX binaries (157KB
and 152KB). They share the same device IR input -- the compiler's
type-checker post-pass partitions host and device code, and the
IRTextEmitter serializes the device-reachable closure to S-expressions
that the plugs parse and translate.

The dual-target approach means a single Codex source file can produce
firmware for NVIDIA (via PTX), ARM Mali, Qualcomm Adreno, and Intel
GPUs (via SPIR-V) -- same signed CDX, same trust chain. For IoT edge
deployments where the GPU vendor varies, SPIR-V reaches hardware that
PTX cannot.

Design: `docs/Designs/Backends/Active/DualTargetGpuCompilation.md`.

## Game engine foreword (21 chapters)

New `codex.foreword.engine` quire -- a 3D game engine library:

Renderer3D, Scene3D, Material, Texture, Mesh, Skinning, LOD, Culling,
PostProcess, Audio3D, AudioBus, Input, GameLoop, GameplayTags,
AbilitySystem, Signal, DebugDraw, TimeOfDay, AssetTable, EdgeMesh,
HelmBridge.

Covers the core game loop, scene graph, PBR materials, skeletal
animation, LOD/culling, post-processing, spatial audio, input handling,
gameplay ability systems, and edge mesh networking for multiplayer.
Each chapter has a smoke test.

## Punctual foreword (8 chapters)

New `codex.foreword.punctual` quire -- a library where every function
is `punctual` (no heap, no recursion, bounded instruction count):

IntOps, BitOps, Saturate, FastMath, Trig, ColorOps, Kinematic, Endian.

These are the building blocks for real-time code: saturating arithmetic,
CORDIC trig, byte-swap, color blending, kinematic interpolation -- all
proven bounded at compile time. Safe for interrupt handlers, sensor
drivers, and hard real-time control loops.

## Cross-architecture GCC parity

ARM64 and RISC-V backends now meet or beat GCC -O0 on all four
micro-benchmarks (fib, fact, gcd, sum). Twenty-four optimization CLs
from agent reek, all emitter-level -- no optimizer pass:

- Destination-driven emission (result goes directly to target register)
- Selective prologue/epilogue (frameless for leaf functions)
- Fused compare-and-branch (cmp + beq in one instruction)
- Frameless TCO (tail calls without frame overhead)
- Identity-return base-chain optimization
- Two-argument direct call convention
- Mixed TCO (some paths tail-call, others return normally)

On RISC-V, sum beats GCC -O0 by 33%. On ARM64, fact beats GCC by 24%.
No optimizer -- the code generator emits these sequences directly.

## Poisoned compact

New `__memset` builtin and per-phase poison bytes. When a compiler phase
reclaims memory, the freed region is overwritten with a poison pattern.
Any stale-pointer read hits the poison immediately instead of silently
reading whatever garbage was left behind. This turns use-after-free
in the compiler itself from "silent corruption three phases later" into
"crash at the exact point of misuse."

## CCE Tier 1

Codex Character Encoding gains 2048 two-byte codepoints covering every
character needed for all 27 EU member state languages. The encoding
uses UTF-8-compatible framing (`110xxxxx 10xxxxxx`). A 16-entry
block-offset table (~48 bytes rodata) maps CCE codepoints to Unicode.
Required for IoT deployment under the Cyber Resilience Act, where
literate source must be readable by non-English regulatory reviewers.

## Prism IDE

Agent fester built Prism, a web-based IDE with compiler integration:
dynamic compile, plug facade, syntax highlighting, error extraction
and display. Wraps the CDX compiler as a backend service for
browser-based development.

## VS Code extension v0.2.0

Agent blu shipped an updated VS Code extension with `.codex` language
support.

## Compiler internals

- **IrRemInt** -- remainder via rdx-direct (avoids extra mov)
- **Compact TCO temps** -- temp registers freed before tail calls
- **Temp-free single-arg** -- single-argument functions skip temp alloc
- **Desugar bivy fix** -- fixes a parser/desugar interaction that
  corrupted bivy scratch on certain patterns
- **LOWER scratch reclamation revert** -- CL 3805's LOWER reclamation
  caused a seed crash (stale pointer after compact); reverted in
  CL 4454, root-caused, documented

## By the numbers

| Metric | Update 23 | Update 24 | Delta |
|--------|----------:|----------:|------:|
| Library modules | 348 | 377 | +29 |
| Foreword quires | 24 | 26 | +2 |
| Transpiler plugs | 48 | 53 | +5 |
| Seed size | 2.16 MB | 2.20 MB | +40 KB |
| Seed digest | `0579CAF4` | `24FEA310` | -- |
| Tests | ~150 | 543 | +393 |
| Copy-ups | 78 | 88 | -- |
| Days | 3 | 3 | -- |
| Agent streams | 4 | 4 | -- |

## What's next

The SIMD story continues: AVX/AVX2 (VEX encoding, 256-bit vectors),
gather/scatter, architecture-specific intrinsic modules. The GPU plugs
need the K0-K2 compiler changes from GpuKernels.md (`[Device]`/`[Gpu]`
effects, type-checker post-pass, IR partition) before end-to-end kernel
compilation works. The game engine quire needs wiring into the Spark
creative suite's rendering pipeline.

The documentation has been comprehensively updated: README, all core
docs, Milestones, CurrentPlan, DevelopersGuide (Vector types, `~`
operator, `for` expressions), DevelopersRulebook (engine + punctual
quires), ArchitectsSketchbook (SIMD register allocation). A new
`docs/KingsAndCourts.md` consolidates the hard real-time, EU
compliance, and IoT regulatory story into a single document.
