# PhysicalCostCodegen — Feeding Type-Level Facts to the Machine

**Status:** WORK ITEMS / not started. This is a backlog of codegen and
layout opportunities, each derived from one observation: Codex already
proves, in its type system, several facts that a modern CPU spends
transistors reconstructing at runtime. Today most of those proofs serve
*safety* and stop at the checker. The work below is about propagating
them into the emitter and the data layout, where they become speed.

**Provenance:** applying Adam Chlipala's "Why Your CPU Works So Hard"
(his *Structure and Guarantees* Substack,
https://stng.substack.com/p/why-your-cpu-works-so-hard) to Codex.
Chlipala is the formal-verification voice already cited in
VisionAndVirtues virtue 10 (CPDT). fester, 2026-07-07.

---

## 1. The argument, compressed

Computation is physical: signals move at light speed, so cost is
dominated by *communication distance*, not arithmetic. Sequential
languages over a flat shared-memory abstraction hide the three things
that actually set cost — data dependencies, latent parallelism, and
locality — so the CPU reconstructs them every cycle. That reconstruction
*is* most of the "hard work":

- **Register renaming** — undoing false dependencies forced by a small
  register file.
- **Memory dependency analysis** — "do these two computed addresses
  alias?" is conservative at best and may not be computable, so the core
  serializes defensively and leaves optimization on the table.
- **Speculation + rollback** — guess the future, prepare to undo it
  (and inherit Spectre/Meltdown).
- **Cache coherence** — the flat-memory fiction, paid for with coherence
  traffic that crosses many times the real inter-core distance,
  invisibly.

The remedy the article proposes: stop hiding it. Move the
software/hardware boundary so the information is *explicit* — compiler
control-flow hints (BasicBlocker), GPU-style explicit spatial
parallelism, formal verification to make the automation trustworthy.

## 2. Why Codex is already on the right side of this

Codex makes explicit, in types, most of what the article says is hidden.
This is not aspirational — it ships today:

- **Linear / `mutable` uniqueness** answers the uncomputable aliasing
  question directly (CDX2061/2062). Where the type system proves two
  references cannot alias, no runtime disambiguation is ever needed.
- **Effect types** make "what touches the world" explicit; a `[]`-pure
  function is reorderable and parallelizable by construction.
- **`Vector N T` + the `[Device]` effect** are explicit parallelism, not
  latent — the GPU path (PTX/SPIR-V/WGSL plugs) is the article's
  "express it in the model so nobody has to extract it."
- **SMP the article's way:** `CoreHeap` gives each core its own arena
  slice (no R10 contention); cross-core work goes through explicit
  atomics + lock-free MPSC channels, not an implicit coherence fabric.
- **`punctual`** is a BasicBlocker cousin: no recursion, bounded control
  flow, bounded instruction count — a shape that needs little to no
  speculation.
- **Arena/deck locality:** phase-scoped bump allocation keeps a phase's
  data spatially together — locality as a structural property.
- **The demand-paged arena is this exact move on the residency axis.**
  Shipped 2026-07 (`DemandPagedArena` / `DemandPagingVictory`), it retired
  the survey system — a formula that *predicted* each phase's physical need
  and paid for mispredictions with a silent, non-monotonic cliff (20 works,
  25 does not, 40 works, "a dial nobody understands"). That is precisely
  the article's failure: a physical cost hidden behind a predictive
  software abstraction, extracted wrong at runtime. The replacement is
  commit-on-touch paging plus a touched-page counter as the honest
  physical-consumption metric — physical cost *measured*, not modelled.
  Where WI-1/WI-2 below attack the communication-distance cost, demand
  paging already did it for residency, and it did it by *deleting* a
  predictor rather than tuning one (Virtue 13, "less is more").

The gap is that the strongest of these facts (the linear-types alias
proof) currently dies at the checker instead of reaching codegen.

## 3. Work items

### WI-1 — Propagate linearity into the emitter as alias facts (highest value)

**What:** When the checker has proven a `linear` or uniquely-owned
`mutable` value cannot alias another, carry that as metadata into the IR
and let the x86-64 / ARM64 / RISC-V emitters exploit it: reorder or
coalesce loads/stores that are currently serialized defensively, and drop
conservative ordering between provably-disjoint accesses.

**Why:** This is the article's central lever — the emitter is doing (or
forgoing) the exact conservative memory-dependency reasoning the CPU also
does, while the answer already sits proven in the checker. It needs no
hardware and it is entirely our own codegen.

**Sketch / seams:** thread a "no-alias" bit (or an ownership token id)
from `LinearOwnership` ownership tracking through LOWER into the IR
memory ops; consume it in the reorder/scheduling points of
`codex/Emit/X86_64*.codex` (and the ARM64/RISC-V plugs). Start read-only:
prove it changes nothing incorrectly (fixed point holds) before enabling
any reorder that changes emitted bytes.

**Risk:** fixed-point-critical — any reorder is a codegen change (two-pass
seed, full battery, pingpong). Gate hard. Memory/time verdict: metadata
is O(1) per value; reordering is local to a basic block, no heap growth.

**Depends on:** `LinearOwnership` ownership-move implementation (ruled but
NOT STARTED as of 2026-07-03) — the move semantics are the source of the
no-alias fact. This WI is downstream of that landing.

### WI-2 — Structure-of-arrays layout for `Vector N T` and hot records

**What:** Give SIMD-lane data and hot-loop records a structure-of-arrays
option so lanes/fields are contiguous, turning latent gather/scatter into
contiguous streams.

**Why:** The article is a locality argument first. Contiguous access is
the cheapest communication pattern; SoA is how you get it for
vectorized/streamed data.

**Sketch:** a layout attribute or a lowering choice for `Vector N T`
backing storage and for records only touched in tight loops; the vector
codegen already lives in the SSE2/packed path (see ArchitectsSketchbook
"Vector / SIMD Register Allocation"). Measure against a strided baseline.

**Risk:** additive layout choice; no correctness exposure if opt-in.
Memory/time verdict: neutral-to-positive on both (fewer cache misses, no
extra allocation).

### WI-3 — Emit control-flow hints from facts we already hold (BasicBlocker analog)

**What:** At emit time we already know effect purity, `punctual` shape,
and linear liveness — enough to describe a function's basic-block
dependency graph. Emit that as sidecar metadata, and (the interesting
part) teach `codex-vm` to consume it.

**Why:** `codex-vm` is a machine model we fully own. It is the one place
Codex can actually do the article's *joint* hardware/software co-design —
prototype an explicit-dependency / explicit-parallelism execution model —
rather than only emitting a scalar stream for someone else's silicon.
Purely exploratory; no production-hardware payoff, but directly on the
article's thesis and cheap to try in the VM.

**Risk:** research, isolated to codex-vm + a metadata section; no seed or
fixed-point exposure until/unless it changes emitted code.

## 4. The honest limit

Codex still emits a scalar instruction stream that runs on the same
out-of-order, speculating, cache-coherent silicon. Being a clean,
type-first language does not exempt the *emitted* code from the CPU's
reconstruction tax. What the type facts buy is: (a) a *compiler* that
makes better decisions before the stream is handed over (WI-1, WI-2), and
(b) the `[Device]` / SIMD / SMP paths that sidestep the tax where the
work is genuinely parallel. The article's deepest version — a co-designed
ISA — is out of scope for real hardware; `codex-vm` (WI-3) is the only
sandbox where that experiment could live.

Net: the article is less a to-do list than a vindication of the
type-first design, with one under-exploited asset — the linear-types
alias proof — that codegen is currently leaving on the floor. WI-1 is the
one that turns a safety proof into speed; the rest are locality and
research.
