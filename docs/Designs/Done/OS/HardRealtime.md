# Punctual -- Bounded Time, Bounded Space, Proven Deadlines

## The Problem

Codex has deterministic memory (no GC, bounded allocation via TCO +
regions) and a priority-based task scheduler with watchdog monitoring.
But it cannot today make the guarantee that matters for safety-critical
and IoT deployments: **this code will finish within N microseconds, or
the system will take a defined recovery action**.

The IoT plan (CodexIoTPlan.md Section 2.3) identifies the gap directly:

> Real-time guarantees: TCO + regions give bounded memory and bounded
> stack, but there is no hard-realtime scheduler with priority
> inversion prevention, deadline monotonic scheduling, or interrupt
> latency guarantees.

IEC 62443 Foundational Requirement 6 demands "timely response to
events." EU CRA Article 6 requires resilience under failure. Medical
device guidance (IEC 62304) demands bounded response to alarms. None
of these can be satisfied by "it's probably fast enough."

## The Vision

`punctual` is a compile-time contract. A function marked `punctual`
guarantees:

1. **Bounded time** -- the compiler can compute a static upper bound on
   execution time (WCET) from the function's structure alone.
2. **Bounded space** -- no heap allocation; all data lives in regions,
   the stack, or pre-allocated pools.
3. **No blocking** -- no unbounded waits, no locks that could invert
   priority, no I/O without a timeout.
4. **Preemptibility** -- the scheduler can always preempt this task in
   bounded time to service a higher-priority deadline.

If the compiler cannot prove all four properties, it rejects the
function with a diagnostic. There is no "trust me" escape hatch.

## Language Integration

### The Keyword

`punctual` is a reserved keyword placed before a function's type
signature, like `mutable` for records:

```
  punctual sensor-read : SensorHandle -> [Gpio, Clock] SensorReading
  sensor-read (h) =
   let raw = gpio-read-pin (h.pin)
   in convert-reading (h.calibration) raw
```

Two lines, no name repetition. The word reads naturally -- "this
function is punctual" means it shows up on time, every time.

The compiler tracks `punctual` through the call graph. A `punctual`
function may only call other `punctual` functions or compiler-blessed
builtins with known WCET. Calling an unbounded function from a
`punctual` context is a type error:

```
CDX6001: punctual function 'sensor-read' calls 'format-log-message'
         which is not punctual
```

### Restrictions Inside Punctual Functions

A `punctual` function is subject to these restrictions,
enforced at compile time:

| Allowed | Forbidden | Why |
|---------|-----------|-----|
| Tail recursion (TCO) | General recursion | Unbounded stack |
| Bounded loops (`for x in 0 to N`) | Unbounded loops | WCET uncomputable |
| Region allocation | Heap allocation (bivy) | No deallocation guarantee |
| Stack locals | `list-push`, `list-concat` | Dynamic growth |
| `with-timeout` I/O | Bare I/O effects | Could block forever |
| Pattern match on known variants | `otherwise` catch-all on open types | Exhaustiveness required |
| Integer arithmetic | Text concatenation (`&`) | Unbounded allocation |
| Direct function calls | Higher-order calls (closures) | Dynamic dispatch |
| Pre-allocated buffers | `__alloc` | Heap bump |

The compiler verifies these structurally. No user annotation is needed
beyond the top-level `punctual` -- the restrictions propagate
through the call graph automatically.

### Effect Integration

`punctual` is an effect-system constraint, not a separate type.
It refines the existing effect row:

```
sensor-read : SensorHandle -> [Gpio, Clock | HardRealtime] SensorReading
```

The `HardRealtime` effect is a *negative* effect -- it restricts what
other effects may appear in the row. An effect handler for
`HardRealtime` is the scheduler's deadline enforcement mechanism (see
Scheduling below). The type system ensures:

- `punctual` functions cannot perform `[Heap]` effects
- `punctual` functions cannot perform `[Network]` without
  `with-timeout`
- `punctual` functions cannot perform `[FileSystem]` at all
  (disk I/O is inherently unbounded)
- `punctual` functions CAN perform `[Gpio]`, `[Clock]`,
  `[Serial]` -- hardware I/O with bounded latency

### Timed Budget Tracking

The effect row carries a time budget that the compiler threads through
the call graph:

```
  punctual control-loop : MotorState -> [Gpio, Clock | Timed 100us] MotorState
  control-loop (m) =
   let reading = adc-read (m.channel)
   in let command = pid-compute m reading
   in pwm-write (m.output) command
```

In Phase 2, the `Timed 100us` effect-row entry carries the budget.
The compiler threads it through calls and rejects over-budget
functions.

When a `Timed N` function calls a `Timed M` function, the compiler
verifies `M <= remaining-budget` and decrements. If the budget would
go negative, the call is rejected:

```
CDX5003: Time budget exceeded in punctual function 'control-loop'.
         Budget: 100us. Consumed: 105us at call to 'format-reading'
         (estimated 25us) on line 8.
```

This is inspired by Automatic Amortized Resource Analysis (Hofmann
et al.) which derives worst-case resource bounds at compile time via
resource-annotated types. Where AARA tracks heap cells, Codex tracks
cycles. A 2025 extension of AARA to algebraic effects (polynomial
resource bounds for effectful programs) provides the theoretical
bridge.

The budget is architectural -- it names a target platform (Phase 2):

```
  punctual control-loop : MotorState -> [Gpio, Clock | Timed 100us cortex-m4-168mhz] MotorState
```

The same function might pass on a 168 MHz Cortex-M4 and fail on a
20 MHz ESP32-C6 LP core. The compiler checks against the declared
target's architecture profile.

### Fuel and Loop Bounds

Every loop and recursion inside `punctual` must have a
compile-time-visible bound:

```
  punctual sum-array : Array Integer, Integer -> Integer
  sum-array (arr) (n) =
   sum-array-loop arr 0 0 n

  punctual sum-array-loop : Array Integer, Integer, Integer, Integer -> Integer
  sum-array-loop (arr) (acc) (i) (n) =
   if i >= n then acc
   else sum-array-loop arr (acc + array-at arr i) (i + 1) n
```

The compiler derives the bound from the `i >= n` guard and the `i + 1`
increment -- this is a counted loop with exactly `n` iterations. `n` is
a parameter, so the WCET formula is `O(n)` with a constant factor
determined by the loop body's instruction count.

For recursive functions that don't follow the counted-loop pattern,
an explicit fuel parameter is required:

```
  punctual search-tree : Tree, Key, Integer -> Maybe Value
  search-tree (t) (k) (fuel) =
   if fuel == 0 then None
   else when t
   is Leaf -> None
   is Node (left) (right) (nk) (nv) ->
    if k == nk then Just nv
    else if k < nk then search-tree left k (fuel - 1)
    else search-tree right k (fuel - 1)
```

The `fuel` parameter bounds recursion depth. The compiler uses it to
compute WCET = `fuel * body-cost`.

## WCET Analysis

### What Codex Can (and Cannot) Prove

No system today provides a formally verified end-to-end proof that
compiled code meets absolute timing bounds on modern hardware:

- **CompCert** proves functional equivalence but explicitly excludes
  timing properties.
- **SPARK/Ada** delegates WCET to external tools (aiT, RapiTime) that
  are not formally linked to correctness proofs.
- **seL4** achieved complete WCET on ARM11 with ~4x pessimism, but ARM
  discontinued publishing worst-case instruction latencies for ARMv7+.

Codex takes a pragmatic, layered approach:

**Layer 1 -- Structural bounds (compile time, sound).** The compiler
proves that every `punctual` function terminates, uses bounded
stack, performs no allocation, and has no unbounded blocking. This is
the type-system guarantee. It does not give an absolute time in
microseconds, but it proves the WCET is *finite and computable*.

**Layer 2 -- Instruction-count bounds (compile time, architecture-aware).**
The emitter counts instructions for each `punctual` function body
on the target architecture. Since Codex controls the entire compilation
pipeline (no external assembler, no linker), instruction counts are
exact. The compiler emits a WCET report:

```
WCET: sensor-read = 47 instructions (ARM Cortex-M4 @ 168 MHz)
      worst-case: 47 * 3 cycles = 141 cycles = 0.84 us
      deadline: 500 us -- PASS (0.17% utilization)
```

The per-instruction cycle count is a platform constant (defined in the
backend's architecture profile). For in-order cores (Cortex-M4, RV32IMC),
the worst-case is the sum of per-instruction maxima. For out-of-order
cores (Cortex-A72), the estimate is conservative (no pipeline modeling).

**Layer 3 -- Measured calibration (runtime, informational).** On first
boot, the runtime can optionally execute a calibration sequence that
times known instruction patterns and adjusts the cycle-count constants.
This does not affect the compile-time proof -- it provides runtime
diagnostics if the platform deviates from the assumed profile.

### Architecture Profiles

Each backend defines an architecture profile with worst-case cycle
costs:

```
 ArchProfile = record {
  name : Text,
  clock-hz : Integer,
  cycles-alu : Integer,        -- ADD, SUB, MOV, CMP
  cycles-mul : Integer,        -- MUL, SMULL
  cycles-div : Integer,        -- UDIV, SDIV
  cycles-load : Integer,       -- LDR (cache hit)
  cycles-store : Integer,      -- STR
  cycles-branch : Integer,     -- B, BL (taken, pipeline flush)
  cycles-branch-not : Integer  -- B (not taken)
 }
```

For Cortex-M4 (in-order, no cache): `alu=1, mul=1, div=2-12,
load=2, store=2, branch=3, branch-not=1`. These are from the ARM
Cortex-M4 Technical Reference Manual.

For RV32IMC (ESP32-C6 HP core): `alu=1, mul=1-5, div=6-35,
load=2, store=1, branch=3, branch-not=1`. From the RISC-V
unprivileged spec + Espressif datasheet.

## Scheduling

### Deadline-Aware Scheduler

The existing priority scheduler (TaskQueue.codex) uses four static
priority levels. `punctual` extends this with deadline-aware
scheduling:

**Earliest Deadline First (EDF)** for punctual tasks. Among tasks
at `TaskCritical` priority with `punctual` annotations, the
scheduler dispatches the one whose absolute deadline is nearest. EDF
is optimal for uniprocessor preemptive scheduling -- if any algorithm
can meet all deadlines, EDF can.

**Static priority** for non-realtime tasks. Tasks without
`punctual` annotations use the existing priority queue. Realtime
tasks always preempt non-realtime tasks.

### Priority Inversion Prevention

Priority inversion occurs when a high-priority task waits for a
resource held by a low-priority task, while a medium-priority task
preempts the low-priority one -- the high-priority task is blocked
indefinitely.

Codex prevents this through **two complementary mechanisms**:

1. **Priority ceiling protocol.** Each shared resource (region, channel,
   hardware peripheral) is assigned a priority ceiling equal to the
   highest priority of any task that accesses it. When a task acquires
   a resource, its effective priority is raised to the ceiling. This
   prevents any medium-priority task from preempting while the resource
   is held. The ceiling is computed at compile time from the
   `punctual` call graph.

2. **No blocking in punctual code.** The stronger guarantee:
   `punctual` functions cannot acquire locks or wait on channels.
   All shared state is either read-only, accessed via lock-free
   single-writer patterns, or copied into the task's region at
   dispatch time. Priority inversion is impossible when there is
   nothing to wait for.

### Interrupt Model

On Cortex-M (NVIC) and RISC-V (PLIC/CLIC), hardware interrupts
preempt the running task with bounded latency:

- **Cortex-M4**: 12 cycles from interrupt assertion to first handler
  instruction (zero wait-state memory). Tail-chaining reduces
  back-to-back interrupt overhead to 6 cycles.
- **RV32IMC**: Implementation-dependent; ESP32-C6 specifies 4-cycle
  minimum interrupt latency in vectored mode.

`punctual` interrupt handlers are annotated like any other
function:

```
  @interrupt vector 25
  punctual timer-tick-handler : -> [Gpio, Clock] ()
  timer-tick-handler () =
   let now = clock-read-ticks
   in gpio-toggle-pin led-pin
```

The compiler verifies the handler body meets the deadline, accounts
for the platform's interrupt entry overhead, and emits the appropriate
vector table entry. Nested interrupts are supported where the hardware
supports priority-based nesting (NVIC priority groups, CLIC levels).

### Watchdog Integration

The existing watchdog (Watchdog.codex) monitors service health. For
`punctual` tasks, the watchdog additionally monitors **deadline
misses**:

- Each dispatched `punctual` task registers its absolute deadline
  with the watchdog.
- The tick handler checks for overdue tasks.
- A deadline miss triggers the task's defined recovery action:
  `restart`, `fallback`, or `halt`.
- Deadline misses are recorded in the fact store as auditable events
  (IEC 62443 FR6, EU CRA vulnerability reporting).

## Memory Model

### Regions Only

`punctual` code uses a restricted memory model:

- **Stack** for local variables (bounded by function frame size,
  known at compile time).
- **Pre-allocated regions** for structured data. Regions are allocated
  before the `punctual` section begins and freed after it
  completes. No allocation occurs during the bounded execution window.
- **Hardware-mapped memory** for peripheral registers (GPIO, timers,
  serial). Accessed via typed pointers with no allocation.

The heap bump allocator (R10/bivy) is not available inside
`punctual` code. Any attempt to call `__alloc` is a compile
error.

### Pre-Allocated Buffers

For tasks that need working memory (sensor data buffers, protocol
frame buffers), the pattern is pre-allocation:

```
  punctual process-sensor-batch : Region, SensorArray -> BatchResult
  process-sensor-batch (r) (sensors) =
   let buf = region-slice r 0 256
   in read-sensors-into buf sensors
```

The `Region` is allocated by the caller (outside the `punctual`
boundary). The `punctual` function uses the pre-allocated space
without growing it.

## Safe-Mode Integration

The TrustAndRuntime design defines safe mode as the fallback when an
agent's confidence drops below threshold or an anomaly is detected.
`punctual` is the implementation mechanism for safe mode:

1. **Anomaly detected** -- agent enters safe mode.
2. **Capability lease restricted** -- agent reverts to compiled-in safe
   capabilities (no network, no filesystem, minimal GPIO).
3. **All code paths are `punctual`** -- pre-compiled safe
   responses execute with proven bounded time and space.
4. **Escalation** -- the agent sends a distress signal to its supervisor
   over a pre-authorized channel. The signal itself is
   `punctual` (bounded-size, fixed-format, no allocation).

The pre-authorized interrupt fast-path (noted as a design gap in
TrustAndRuntime.md review note 3) is resolved by `punctual`
interrupt handlers: the authority was verified at compile time and
baked into the binary as a capability bit, so no runtime vouch-chain
walk is needed during an emergency.

## IoT Integration

### Protocol Deadlines

IoT protocols impose soft and hard timing requirements. `punctual`
enables the compiler to verify the critical ones:

| Protocol | Timing Requirement | Deadline |
|----------|-------------------|----------|
| CoAP | ACK retransmission | 2s (ACK_TIMEOUT) |
| CoAP | Exchange lifetime | 247s |
| MQTT | Keep-alive ping | configurable (typ. 30-60s) |
| LwM2M | Registration update | server-defined lifetime |
| LwM2M | Firmware update state transition | application-defined |
| Thread | MAC frame timing | 4ms (aMaxFrameResponseTime) |
| Motor control | PWM update | 50us-1ms |
| Sensor sampling | ADC read + process | 100us-10ms |

The first four are soft deadlines (protocol retransmits on miss). The
last three are hard deadlines (miss = physical system damage or data
loss). `punctual` targets the hard deadlines; soft deadlines are
handled by the existing `with-timeout` mechanism.

### IoT Deadline Tiers

Real-world IoT and industrial systems span four orders of magnitude
in timing requirements:

| Tier | Deadline Range | Examples | Standard |
|------|---------------|----------|----------|
| Ultra-hard | 1-100 us | Motor current loop (50us), EtherCAT I/O (30us), PROFINET IRT (31.25us) | IEC 61784 Class C |
| Hard | 100 us - 10 ms | PLC machine control (1-20ms), factory automation (1-2ms), 3GPP motion control (1-10ms) | IEC 61784 Class B, 3GPP TS 22.104 |
| Firm | 10 ms - 1 s | 5G URLLC (1ms target), Thread mesh (<200ms), MQTT QoS 0 (~11ms) | IEEE 802.15.4, MQTT |
| Soft | 1 s - minutes | CoAP ACK (2-3s), environmental monitoring, MQTT keepalive (60s) | RFC 7252 |

Codex Phase 1 hardware targets and their sweet spots:

- **STM32F4 (Cortex-M4, 168 MHz)**: ultra-hard and hard tiers.
  Motor control, industrial fieldbus endpoints, PLC replacement.
- **ESP32-C6 (RV32IMC, 160 MHz)**: hard and firm tiers.
  Sensor networks, Thread/Matter endpoints, telemetry.
- **Raspberry Pi 4/5 (Cortex-A72/A76)**: firm and soft tiers.
  Edge gateways, protocol bridges, local AI inference.

Jitter budgets at the ultra-hard tier are < 10 us (EtherCAT: < 1 us).
The WCET analysis must be tight enough that proven-bound minus
observed-execution leaves room for the jitter budget.

### Compliance Mapping

| Regulation | Requirement | Punctual Mechanism |
|------------|-------------|-------------------------|
| IEC 62443 FR6 | Timely response to events | Proven deadline bounds |
| IEC 62443 FR7 | Resource availability | Bounded memory, no OOM |
| EU CRA Art. 6 | Resilience under failure | Safe-mode fallback |
| EU CRA Annex I.2 | Secure by design | Compile-time enforcement |
| ETSI 303 645 5.9 | System resilience to outages | Watchdog + recovery |
| NISTIR 8259 Cat. 6 | Security state awareness | Deadline miss logging |

### Power Management

On battery-powered IoT devices, `punctual` interacts with power
management:

```
  punctual sensor-duty-cycle : SensorHandle -> [Gpio, Clock, Power] SensorReading
  sensor-duty-cycle (h) =
   let reading = sensor-read h
   in power-enter-stop-mode 950ms
```

The compiler verifies that `sensor-read` fits within 50ms, and that the
`power-enter-stop-mode` call is the last operation (tail position). The
scheduler knows the task's next activation is 950ms later and does not
attempt to preempt during the stop window.

## Implementation Plan

### Phase 1 -- Structural Verification (with IoT Phase 1)

- `punctual` annotation parsing and representation in AST/IR
- Compile-time restriction checker: no heap, no unbounded loops, no
  blocking, no general recursion, no closures
- Diagnostics: CDX5001-CDX5010 for each violation class
- Effect row integration: `HardRealtime` as a negative effect
- Call-graph analysis: transitivity of restrictions

### Phase 2 -- WCET Computation (with IoT Phase 2)

- Architecture profiles for Cortex-M4, RV32IMC, Cortex-A72
- Instruction-count accumulator in the emitter (per-function)
- Deadline check: instruction-count * worst-case-cycles vs declared
  deadline
- WCET report emission (text mode diagnostic)
- Loop-bound inference from counted-loop patterns

### Phase 3 -- Scheduler Integration (with IoT Phase 3)

- EDF scheduling for `punctual` tasks
- Priority ceiling computation from call graph
- Deadline miss detection in watchdog
- Fact store logging of deadline events
- Interrupt handler support (vector table emission, entry overhead
  accounting)

### Phase 4 -- Calibration and Hardening (with IoT Phase 4)

- Runtime calibration sequence for cycle-count adjustment
- Multi-core considerations (Cortex-A72 quad-core, ESP32-C6 dual-core)
- Cache modeling for Cortex-A (conservative, informational)
- Power-state integration (stop/standby timing)

## Prior Art Survey (June 2026)

The design space for compile-time bounded-execution enforcement has
five tiers. No production language occupies the space `punctual` fills.

### Tier 1: Production Languages with Real-Time Restrictions

**Ada / Ravenscar / SPARK.** Ravenscar (`pragma Profile(Ravenscar)`)
is compiler-enforced -- compile errors, not warnings. It restricts
the tasking model: no dynamic task creation, no `select`, no `abort`,
no relative `delay`, one entry per protected object. But Ravenscar
restricts CONCURRENCY, not COMPUTATION. It does not reject unbounded
loops, does not check recursion, and has no WCET awareness.

`pragma Restrictions(No_Allocators)` is a hard compile error -- the
compiler rejects `new`. `pragma Restrictions(No_Recursion)` is NOT a
compile error -- the Ada RM defines it as "erroneous execution"
(undefined behavior). The compiler is not required to detect it. GNAT
may catch direct recursion but mutual recursion through dispatching
escapes. SPARK's `Loop_Variant` proves termination, not bounded time.

WCET analysis is ALWAYS external tooling: aiT (AbsInt, ~$50K+),
RapiTime (Rapita), Bound-T. No Ada compiler computes timing. The
compiler provides structural guarantees that make external WCET
analysis feasible, but does not perform it.

**Rust embedded.** rustc enforces nothing real-time-specific.
`#[no_std]` removes the standard library (heap, OS, threads) but does
not prevent recursion, unbounded loops, or non-deterministic patterns.
RTIC and Embassy are frameworks (proc macros), not compiler features.
No `#[bounded]` or `#[realtime]` attribute exists in any RFC.
Clang's `[[clang::nonblocking]]` (Clang 18+) verifies no heap/locks
but does NOT check loops or recursion.

**MISRA C/C++.** NOT enforced by any standard compiler. Checked by
external tools (Polyspace, PC-lint, LDRA, cppcheck). Rule 17.2 bans
recursion, Rule 21.3 bans malloc -- but these are linting rules, not
compiler features. IAR and Green Hills have partial built-in MISRA
modes; GCC/Clang have none. No C/C++ compiler has a "bounded-time
function" annotation.

### Tier 2: Synchronous Languages (Bounded by Construction)

**Esterel.** Bounded execution per tick is COMPILER-CHECKED, not
structural. The language CAN express instantaneous loops -- the
compiler rejects them via static analysis (every loop path must
contain a `pause`). Constructive causality analysis rejects cyclic
signal dependencies. No recursion, no dynamic allocation. Each tick
compiles to a finite automaton.

**Lustre / SCADE.** Bounded execution is STRUCTURAL -- the language has
no loop construct and no recursion. Programs are dataflow equations
evaluated once per tick. SCADE's KCG code generator produces C with no
loops, no recursion, no malloc, no function pointers. Qualified
DO-178C TQL-1, ISO 26262 ASIL D, IEC 61508 SIL 3. SCADE is the gold
standard for compile-time bounded execution in production avionics
(Airbus fly-by-wire).

**Key limitation:** These are whole-language restrictions. You cannot
write unrestricted code in the same program. `punctual` is per-function
opt-in -- the rest of the program is unrestricted.

### Tier 3: Per-Function Totality / Restriction Annotations

**Idris 2 `total`.** Per-function keyword. The compiler rejects
non-terminating recursion via structural decrease analysis. Proves
the function HALTS, not how long it takes. A function doing O(2^n)
work on a structurally decreasing argument passes. Agda, Coq, Lean 4,
and Dafny have similar totality checking.

**D language `pure`, `@nogc`, `nothrow`, `@safe`.** Four per-function
compiler-enforced annotations. `@nogc` rejects GC allocation,
`pure` rejects side effects, `nothrow` rejects exceptions. Transitive
to callees. The mechanism (keyword on function, compiler rejects
violations) matches `punctual` exactly -- but applied to effects, not
bounded execution.

**Rust `const fn`.** Restricts heap allocation and I/O but permits
unbounded loops and recursion (subject to an evaluation step limit
that is a practical safeguard, not a semantic guarantee).

### Tier 4: Academic Complexity Inference

**TiML (Wang, Wang, Chlipala, OOPSLA 2017).** Function types carry
time-complexity indices (e.g., `using n+1`). The type checker uses SMT
to verify declared bounds. Supports Big-O via Master Theorem. Academic
prototype, not production. Closest to a per-function time-bound
annotation with compiler enforcement.

**RAML (Hoffmann et al., CAV 2012, CMU).** Fully automatic polynomial
resource bound inference via amortized analysis. No programmer
annotations needed. Reports bounds like "heap <= 3n^2 + 5n + 2".
More mature than TiML (web demo, thesis extensions through 2023) but
still academic. Handles an OCaml subset.

**Bounded Affine Types (Ghica & Smith, 2013).** Actual cycle counts
in types with rejection on failure. Closest to `punctual`'s approach.
But recursion-free, no effects, no architecture profiles, not designed
for bare-metal.

### Tier 5: External WCET Tools

| Tool | Vendor | Approach |
|------|--------|----------|
| aiT | AbsInt | Abstract interpretation on binaries, cache/pipeline modeling |
| RapiTime | Rapita Systems | Hybrid measurement + static analysis |
| Bound-T | Tidorum | Static analysis on binaries |
| OTAWA | Univ. Toulouse | Open-source static WCET framework |

No WCET tool is integrated into any compiler. All operate on compiled
binaries as a separate step with a separate trusted computing base.

### Theoretical Foundations

**Interrupts as algebraic effects.** Ahman and Pretnar (POPL 2021,
"Asynchronous Effects") prove that hardware interrupts can be modeled
as asynchronous algebraic effects with type safety (verified in Agda).

**Linear effects and resource safety.** Munch-Maccagnoni (2025, arXiv
2510.23517) proves combining algebraic effects with linear types
guarantees resource cleanup even during exceptions. Foundation for
`punctual`'s region-only memory model.

**Cost as effect.** Calf (Niu, Sterling, Harper, POPL 2022) models
cost as a computational effect. Manual proofs in Agda, not a compiler.
RAST (Das & Pfenning, 2022) encodes work+span via session types.

### Where punctual Sits

| Property | Ada Ravenscar | Esterel/SCADE | Idris `total` | D `@nogc` | TiML | Codex `punctual` |
|----------|--------------|---------------|---------------|-----------|------|-----------------|
| Per-function | No (global) | No (whole language) | Yes | Yes | Yes | **Yes** |
| Rejects heap alloc | Yes (global) | Yes (structural) | No | Yes (`@nogc`) | No | **Yes (CDX6002)** |
| Rejects recursion | Erroneous execution | Yes (structural) | No (proves termination) | No | No | **Yes (CDX6005)** |
| Rejects unsafe calls | No | N/A (no FFI) | No | No | No | **Yes (CDX6001)** |
| Rejects bare I/O | No | N/A | No | No | No | **Yes (CDX6004)** |
| Instruction/cycle count | No | No | No | No | Asymptotic only | **Yes (CDX6010)** |
| Budget with violation warning | No | No | No | No | Type error | **Yes (CDX6011)** |
| Shipping | Yes | Yes (SCADE) | Yes | Yes | No (academic) | **Yes** |

No production language has all seven rows checked. `punctual` is the
first per-function bounded-execution keyword in a production systems
language with compiler-enforced structural restrictions and
instruction-count reporting.

A side-by-side example (missile warning receiver) lives in
`codex/test/examples/missile-warning.codex` (Codex) and
`codex/test/examples/missile-warning-ada.txt` (Ada).

## Practical Comparison: Ada Ravenscar vs Codex punctual

A side-by-side example (missile warning receiver) lives in
`codex/test/examples/missile-warning.codex` (Codex) and
`codex/test/examples/missile-warning-ada.txt` (Ada). Key differences:

| Aspect | Ada + Ravenscar | Codex + punctual |
|--------|----------------|-----------------|
| Scope | Global (whole program restricted) | Per-function (unrestricted code coexists) |
| Heap check | `pragma Restrictions(No_Allocators)` -- global | CDX6002 -- per punctual function |
| Recursion check | `No_Recursion` -- erroneous execution, not always detected | CDX6005 -- hard compile error |
| WCET | External tool (aiT, $50K+) | Compiler reports instruction count at CDX6010 |
| Budget | None -- compiler has no timing awareness | `punctual 128 name` sets instruction budget |
| Violation | External tool finds it post-compilation | CDX6011 warning at compile time |
| I/O check | None -- you can do I/O in any function | CDX6004 -- punctual cannot do bare I/O |
| Unsafe calls | Not checked -- any function can call any function | CDX6001 -- punctual can only call punctual or safe builtins |

No production language has all of: per-function granularity, single keyword,
compiler-enforced structural restrictions, and instruction-count reporting.
Ada Ravenscar is the closest but requires external WCET tools and applies
restrictions globally. Clang's `[[clang::nonblocking]]` checks allocation
and side effects but not loops or recursion.

## Open Questions

1. **Division WCET.** Integer division has highly variable cycle count
   (2-35 cycles depending on operand size). Should `punctual`
   ban division, use the worst case, or require the programmer to
   declare operand bounds?

2. **Interrupt stacking depth.** On Cortex-M with multiple priority
   levels, how many nested interrupts should the WCET analysis
   account for? All levels, or only those declared `punctual`?

3. **Cross-core interference.** On multi-core targets, memory bus
   contention from other cores affects timing. Should the analysis
   assume worst-case contention, or model core isolation?

4. **Floating point.** Cortex-M4 has an optional FPU with variable-
   latency operations. Should `punctual` restrict to integer-
   only, or model FP WCET?

5. **WCET composability.** When `punctual` function A calls
   `punctual` function B, is A's WCET simply A-body + B-WCET?
   Or should the compiler account for calling convention overhead,
   pipeline effects, and cache state transitions?
