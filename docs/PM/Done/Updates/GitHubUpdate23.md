# GitHub Update 23 -- 2026-06-13

Covers main CLs 3760-4073 (since Update 22 at CL 3758, 2026-06-10).
Three days, ~78 copy-ups from four agent streams (val, reek, fester, blu).

## punctual: compiler-enforced bounded execution

The `punctual` keyword landed end-to-end. A function marked `punctual`
is proven bounded at compile time -- no external WCET tool needed.

The compiler enforces five structural restrictions per punctual function:

| CDX Code | What it rejects |
|----------|----------------|
| CDX6001 | Calls to non-punctual or non-safe-builtin functions |
| CDX6002 | Heap allocation (list-push, list-concat, etc.) |
| CDX6003 | Closures or lambdas |
| CDX6004 | Bare I/O effects (Console, FileSystem, Network) |
| CDX6005 | Self-recursion (unbounded control flow) |

The emitter counts instructions emitted per punctual function and
reports them at CDX6010. Optional instruction budgets
(`punctual 128 sensor-read`) warn at CDX6011 when exceeded. Default
budget is 256 instructions.

**This is novel.** No production language has all of: per-function
granularity, single keyword, compiler-enforced structural restrictions,
and instruction-count reporting. Ada Ravenscar applies restrictions
globally and needs external WCET tools ($50K+). Rust has nothing. MISRA-C
is external linters. Esterel/SCADE guarantee bounded time but restrict
the entire language. See the comprehensive prior art survey in
`docs/Designs/OS/Active/HardRealtime.md`.

A missile warning receiver example (`codex/test/examples/missile-warning.codex`)
shows the feature in action alongside an equivalent Ada/Ravenscar version.
The Codex compiler tells you `classify-threat: 115 instructions (44% of budget)`.
The Ada compiler tells you nothing -- you need aiT.

## Unit types: compile-time domain safety

`Second = unit Integer` declares a distinct type wrapping Integer with
zero runtime overhead. The compiler erases the wrapper at codegen --
the value is stored and operated on as a plain integer. Type safety is
compile-time only.

- `Second 42 + Second 8 = 50` -- same-unit arithmetic
- `Second 42 * 3 = 126` -- scalar multiplication preserves unit
- `Second 42 > Second 8 = True` -- comparisons work
- `Second + Meter = ERROR` -- cross-unit is a type error
- `Second between 0 and 3600` -- bounded + unit composition
- `1 Minute = 60 Second` -- conversion declarations parsed and stored
- Manual conversion functions: `minute-to-second (m) = Second (m * 60)`

Unit types are transparent to their inner type at assignment boundaries
(a `Second` can be passed where `Integer` is expected) but distinct
from other unit types (`Second` and `Meter` don't mix).

## ARM64 and RISC-V backends

Agent reek landed cross-architecture backend infrastructure:

| What | Status |
|------|--------|
| ARM64 QEMU virt | "Hello, World!" + cross-arch test 10/10 lines (8/10 content) |
| RISC-V QEMU virt | cross-arch test 10/10 perfect |
| Instruction encoders | 6 forewords (AArch64, RV64, RV32C, Thumb-2, plus existing x86-64) |
| Codegen plugs | 2 (ARM64, RISC-V) with 29 runtime functions each |
| Board HAL | Board.codex abstraction + STM32F4 + ESP32-C6 + Pi4 + QEMU virt |

- **ARM64:** AArch64 encoder, ELF64 writer, runtime helpers (itoa,
  print-line, heap-alloc, stack-overflow guard), QEMU virt board,
  Thumb-2 encoder for Cortex-M. Integer output working.
- **RISC-V:** RV64 + RV32IMC + RV32C (compressed) encoders, ELF writer,
  runtime helpers, QEMU virt board, callee-save register allocation.
  Cross-arch test produces 10/10 correct output lines.
- Cross-architecture test inputs (`codex/plugs/test-input/`).

## IoT protocol stack

- **MQTT v5.0** client library: CONNECT, PUBLISH, SUBSCRIBE, PING,
  DISCONNECT with QoS 0-2 and property encoding.
- **CoAP** client library: GET, POST, ACK with option encoding
  (RFC 7252, confirmable/non-confirmable).
- **Board HAL** abstraction: `Board.codex` trait with concrete
  implementations for STM32F4, ESP32-C6, Raspberry Pi 4, and QEMU virt.

## Build and test improvements

- **BVT mode** (agent fester): `build.ps1` runs a 10-test BVT subset
  instead of the full battery during development. Total build time
  dropped from ~520s to ~113s.
- **Test consolidation** (agent val): 99 individual test files merged
  into 11 smoke bundles. Test count 232 -> 137. Battery time 424s -> 217s.
  Smoke bundles: unit-smoke, rt-smoke, try-smoke, prose-smoke,
  linear-smoke, linear-errors, mutable-smoke, typeclass-smoke,
  handler-smoke, record-smoke, lang-smoke, bs3-smoke.

## Other

- **Mutable field assignment** (agent blu, CL 3880): landed then reverted
  (CL 3883) due to seed crash at scale. Root cause: the seed couldn't
  compile the source with mutable assignment patterns at full program
  size. Reverted pending investigation.
- **Codegen optimizations** (agent val): emit-binary both-complex
  shortcut (CL 3846), tails-direct shuffle improvements (CL 3840).
- **Parser improvements** (agent fester): keyword-as-pattern-var error
  test, parser resync improvements, whitespace handling.
- **Foreword libraries** (agent fester): ElasticHash, FunnelHash,
  ElasticBloom probabilistic data structures.
- **IoT docs** (agents reek, val): STM32/ESP32 hardware references,
  IEC-62443 compliance mapping, HardRealtime design integration.
- **Dead-phase reclamation** design (agent fester).
