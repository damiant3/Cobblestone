# PhysicalCostCodegen -- Feeding Type-Level Facts to the Machine

**Status:** COMPLETE (NoAliasCodegen campaign, blu, 2026-07-07; WI-1
stages 0-3 + WI-2 VecArray shipped, WI-3 deferred with pickup
triggers -- see the per-stage as-built sections).
Stage 0 shipped (CL 7243): `IRDef.unique-params` carries declared-linear
param names from the checker into the emitter; `CodegenState.noalias-slots`
binds them to local slots; CDX4011 (NoAliasProven, info) fires at field
reads/stores/`__record-set` through a unique owner. Wire untouched -- no
plug rebuilds. Stage 1 shipped (CL 7247 + seed): the checker now roots
minted linear locals (let and act binds from linear-returning calls) as
tracked owners, closing the LinearOwnership residual that made the
param fact airtight; `-> [E] linear T` bare returns sanctioned. Stage 2
(this doc's WI-1 first consumer) is the alias-free field-load cache in
the x86-64 emitter -- see the as-built notes at the end.

**Memory-model ruling (Damian, 2026-07-07, required by stage 2):**
unsynchronized cross-core mutation of plain record fields is outside
the Codex memory model. Cross-core data sharing goes through channels
and atomics (both are calls, which flush the emitter's field-load
cache); a plain field held in a register across a call-free
straight-line sequence may not be legitimately mutated by another core
in that window. This is consistent with the SMP design (per-core
CoreHeap arenas, lock-free MPSC channels, explicit atomics) and is the
license for any register caching of memory-resident values.

**WI-1 findings that reshaped the work (research 2026-07-07):** the
x86-64 emitter had NO alias-guarded conservatism to relax -- its
redundancy was unconditional recomputation (receiver re-emission per
field read, base reloads per store-loop iteration), all register-
liveness conservatism, not aliasing. So "reorder or coalesce loads/
stores" below is retired language: there is no scheduler, and building
one is a backend rewrite. The implementable WI-1 is: build a field-load
cache (sound with zero linearity input -- the measurable win), then let
the linearity fact narrow its invalidation (the type-fact-to-speed
mechanism proven end-to-end). Mutable-record uniqueness is EXCLUDED
permanently: pass-on plus free reads is legal aliasing by design
(CDX2062 polices consumption count, not read aliasing), so no
optimization may rely on it. The linear discipline itself limits the
cache's firing rate on linear bases (every mention is a use -- code
cannot read the same linear base twice), so the honest payoff order is:
alias-free cache first, linearity narrowing as mechanism proof,
fresh-allocation no-alias (an `IrRecord` pointer cannot alias anything
until it escapes) as the highest-value future alias fact.

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
that actually set cost -- data dependencies, latent parallelism, and
locality -- so the CPU reconstructs them every cycle. That reconstruction
*is* most of the "hard work":

- **Register renaming** -- undoing false dependencies forced by a small
  register file.
- **Memory dependency analysis** -- "do these two computed addresses
  alias?" is conservative at best and may not be computable, so the core
  serializes defensively and leaves optimization on the table.
- **Speculation + rollback** -- guess the future, prepare to undo it
  (and inherit Spectre/Meltdown).
- **Cache coherence** -- the flat-memory fiction, paid for with coherence
  traffic that crosses many times the real inter-core distance,
  invisibly.

The remedy the article proposes: stop hiding it. Move the
software/hardware boundary so the information is *explicit* -- compiler
control-flow hints (BasicBlocker), GPU-style explicit spatial
parallelism, formal verification to make the automation trustworthy.

## 2. Why Codex is already on the right side of this

Codex makes explicit, in types, most of what the article says is hidden.
This is not aspirational -- it ships today:

- **Linear / `mutable` uniqueness** answers the uncomputable aliasing
  question directly (CDX2061/2062). Where the type system proves two
  references cannot alias, no runtime disambiguation is ever needed.
- **Effect types** make "what touches the world" explicit; a `[]`-pure
  function is reorderable and parallelizable by construction.
- **`Vector N T` + the `[Device]` effect** are explicit parallelism, not
  latent -- the GPU path (PTX/SPIR-V/WGSL plugs) is the article's
  "express it in the model so nobody has to extract it."
- **SMP the article's way:** `CoreHeap` gives each core its own arena
  slice (no R10 contention); cross-core work goes through explicit
  atomics + lock-free MPSC channels, not an implicit coherence fabric.
- **`punctual`** is a BasicBlocker cousin: no recursion, bounded control
  flow, bounded instruction count -- a shape that needs little to no
  speculation.
- **Arena/deck locality:** phase-scoped bump allocation keeps a phase's
  data spatially together -- locality as a structural property.
- **The demand-paged arena is this exact move on the residency axis.**
  Shipped 2026-07 (`DemandPagedArena` / `DemandPagingVictory`), it retired
  the survey system -- a formula that *predicted* each phase's physical need
  and paid for mispredictions with a silent, non-monotonic cliff (20 works,
  25 does not, 40 works, "a dial nobody understands"). That is precisely
  the article's failure: a physical cost hidden behind a predictive
  software abstraction, extracted wrong at runtime. The replacement is
  commit-on-touch paging plus a touched-page counter as the honest
  physical-consumption metric -- physical cost *measured*, not modelled.
  Where WI-1/WI-2 below attack the communication-distance cost, demand
  paging already did it for residency, and it did it by *deleting* a
  predictor rather than tuning one (Virtue 13, "less is more").

The gap is that the strongest of these facts (the linear-types alias
proof) currently dies at the checker instead of reaching codegen.

## 3. Work items

### WI-1 -- Propagate linearity into the emitter as alias facts (highest value)

**What:** When the checker has proven a `linear` or uniquely-owned
`mutable` value cannot alias another, carry that as metadata into the IR
and let the x86-64 / ARM64 / RISC-V emitters exploit it: reorder or
coalesce loads/stores that are currently serialized defensively, and drop
conservative ordering between provably-disjoint accesses.

**Why:** This is the article's central lever -- the emitter is doing (or
forgoing) the exact conservative memory-dependency reasoning the CPU also
does, while the answer already sits proven in the checker. It needs no
hardware and it is entirely our own codegen.

**Sketch / seams:** thread a "no-alias" bit (or an ownership token id)
from `LinearOwnership` ownership tracking through LOWER into the IR
memory ops; consume it in the reorder/scheduling points of
`codex/Emit/X86_64*.codex` (and the ARM64/RISC-V plugs). Start read-only:
prove it changes nothing incorrectly (fixed point holds) before enabling
any reorder that changes emitted bytes.

**Risk:** fixed-point-critical -- any reorder is a codegen change (two-pass
seed, full battery, pingpong). Gate hard. Memory/time verdict: metadata
is O(1) per value; reordering is local to a basic block, no heap growth.

**Depends on:** `LinearOwnership` ownership-move implementation (ruled but
NOT STARTED as of 2026-07-03) -- the move semantics are the source of the
no-alias fact. This WI is downstream of that landing.

### WI-2 -- Structure-of-arrays layout for `Vector N T` and hot records

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

### Stage 2 as-built: the field-load cache (x86-64)

`FieldCacheEntry { fc-slot, fc-offset, fc-reg }` on `CodegenState`.
Populated and consulted ONLY in `emit-field-access` for `IrName`
receivers resolving to a local slot; a hit replaces receiver emission +
memory narrow-load with one `mov rd, cached`. Default-flush: survival
is the audited act. Evictions: `alloc-temp` (reg reuse), `alloc-local`
(slot reuse -- `emit-let` rewinds `next-local`), `store-local` (slot
write), idiv arms (RAX/RDX/R11 fixed-reg clobber; `imul-rr` is
two-operand and safe; shift builtins flush via the apply rule). Full
flushes: `emit-apply` exit (every call/builtin/closure -- composes
through nesting because inner applies flush at their own exits),
`emit-call-to`/`emit-jmp-to` (runtime helpers), `patch-jcc-at`/
`patch-jmp-at` (the universal branch-label choke points: every
if-variant's else label and every match-arm boundary patch through
these two; prologue-guard patches flush harmlessly), `emit-field-store`
and `emit-record-set-builtin` (memory stores -- stage 3 narrows these
to same-base invalidation for noalias slots), and dispatch entries of
`IrHandle`/`IrTry`/`IrFork`/`IrAwait`/`IrWithTimeout`. Cache bounded by
the temp pool (every entry holds a distinct temp reg, max 6). Survival
domain: pure straight-line expression trees -- `x.a + x.a`, repeated
`fot.offset`-style reads. R8/R9/R15 staging and register-locals are
disjoint from the temp pool and never cached.

THE SOUNDNESS INVARIANT (learned the hard way -- the first pingpong
emitted a compiler that crashed at scale): any code path that writes a
register OUTSIDE alloc-temp must fc-evict it first. The emitter's
in-place binary-op folds (emit-reg-right-inplace, the reg-left
commutative fold, the both-complex pop+op shortcut) write an operand's
result register directly; when that operand was a field access, the
cache had just recorded that register -- the fold made the entry stale
and a later same-field read consumed the folded sum. Same class:
destination-driven emit-to-local writes (arg registers in direct
calls) and the raw RAX/R11 handler-table load. All carry evictions
now, plus exit barriers on handle/try/fork/await/timeout. The
field-cache-shape test pins each class with a wrong-value-if-stale
probe, and the 3-pass pingpong (seed -> Sut -> stage1 -> stage2, the
last two cache-emitted) is the gate that catches any future miss --
stage1 == stage2 byte-identical is required, and stage1 must itself
survive the self-compile.

### Stage 3 as-built: fresh-allocation narrowing (and why linearity narrowing is empty)

The planned "linearity narrows store invalidation" is STRUCTURALLY
EMPTY, by the discipline's own rules: every mention of a linear param
is a use (a second cached read is CDX2061), field-assign on immutable
records is CDX2060, `__record-set` through a linear param is CDX2065,
and temps are caller-saved so cached registers die at every call
regardless of aliasing. No legal program contains a shape the
narrowing would improve. Mutable params would allow the shapes (reads
free, stores legal, in-def uniqueness via moves) but caller-side
mutable-LOCAL minting is untracked -- two mutable params can legally
alias -- so consuming that fact awaits the mutable consolidation
campaign. This is the honest stage-3 finding: for THIS cache, the
declared-linear fact's value is exhausted at stage 0's instrumentation.

The alias fact with no checker dependency and a real trigger is
FRESHNESS: a just-constructed record/list is virgin bump-allocator
memory, provably disjoint from every earlier cached base (no GC, no
reuse until function return). `fresh-slots` on CodegenState: SET when
a let binds an IrRecord/IrList construction; CLEARED when the slot's
value is read as a value (`emit-name` -- the single choke point
through which a pointer can escape into an alias; direct-register
fold reads cannot alias record pointers), when the slot is re-stored
or re-allocated, and wholesale in fc-flush (freshness lives and dies
with the cache). CONSUMED at emit-field-store and __record-set: a
store through a still-fresh base evicts only that base's entries
(fc-store-barrier) instead of flushing, with a CDX4011 info. The
freshness is NOT re-added after the store: `__record-set`'s result
aliases its base, so re-freshening could preserve entries cached
through the result name -- first store narrows, later ones flush.
Probes: codex/test/fresh-alloc-narrow -- a narrowing shape plus an
adversarial alias bind (`let y = b`) whose output shifts if freshness
wrongly survives alias creation.

### Stage 4 as-built: VecArray (WI-2)

Two new x86-64 builtins -- `vec-load-at : Integer -> Vector 2 T` and
`vec-store-at : Integer, Vector 2 T -> Integer` (one polymorphic
signature each; movupd is type-agnostic over 16 bytes and carries no
alignment assumption -- never movapd) -- plus the foreword chapter
`codex/foreword/math/VecArray.codex`: va-alloc / va-get / va-set /
va-map2 / va-sum over one flat 16 x N buffer from `alloc-bytes`.
`va-alloc` takes a fill value because `alloc-bytes` is a raw bump
allocation; `va-set` writes in place and returns the SAME array,
exactly the `list-set-at` contract. x86-64 only; the ARM64/RISC-V
plugs gain the builtins in a small follow-up.

Measured under codex-vm (n = 100k, PIT ticks ~18.2/s, bit-exact
output match between paths): read-reduce (2000 passes) boxed 14
ticks vs flat 27 -- the flat side pays a va-get user call plus a
fresh 16-byte result cell per element while boxed `list-at` is
inline; fill (500 passes) boxed 6 vs flat 7 -- parity, because the
`vec-splat` input cell dominates both sides. The honest stage-4
claim is therefore structural, not wall-clock: one 16N allocation
replaces N cells plus the 8N pointer array, stores allocate
nothing, and the addressing mechanism (typed 16-byte lane moves at
computed addresses) now exists end-to-end. The wall-clock win needs
the follow-up increment: emitters that keep the value in XMM across
a combine (inline va-get/va-set or a fused map2) instead of
round-tripping every element through a heap cell. Pickup trigger: a
consumer with a measured vector hot loop.

The stage's bit-exact test found a LATENT emitter bug (pre-dating
the campaign): `emit-vec-extract-builtin` held a call receiver's
result -- which `emit-direct-call` returns RAW in RAX -- across the
index operand's `emit-expr`; when the temp rotation wrapped
(`next-temp mod 6 == 0` at the site), `alloc-temp` handed RAX to
the index, zeroing the receiver, and the extract loaded from
address 0 -- which demand paging serves as zeros, so the wrong value
was a silent 0.0, not a crash. Deterministic per binary, but any
unrelated edit shifts the rotation position and moves the failure --
the classic heisenbug-by-recompile. Same class in the same
function: both extract emitters mutated the index register in
place, corrupting a register-resident local index for every later
read. Fixed in `vec-extract` and `vec4-extract` by the established
local-spill shape: spill the receiver to a local across the index
emission, copy the index into an `alloc-temp-avoiding` destination,
mutate only that. Probes: `codex/test/vec-extract-hazards.codex`
(call receivers at six rotation offsets + index reuse after
extract), plus `vec-array.codex` get3. The generalized invariant,
extending the stage-2 rule: an emitter must not hold a bare
emit-expr result register across another emit-expr (spill to a
local first), and must never mutate an operand's result register --
only alloc-temp'd destinations. Other emitters were NOT audited for
this class in this CL; a dedicated sweep is future work.

### WI-3 -- Emit control-flow hints from facts we already hold (BasicBlocker analog)

**What:** At emit time we already know effect purity, `punctual` shape,
and linear liveness -- enough to describe a function's basic-block
dependency graph. Emit that as sidecar metadata, and (the interesting
part) teach `codex-vm` to consume it.

**Why:** `codex-vm` is a machine model we fully own. It is the one place
Codex can actually do the article's *joint* hardware/software co-design --
prototype an explicit-dependency / explicit-parallelism execution model --
rather than only emitting a scalar stream for someone else's silicon.
Purely exploratory; no production-hardware payoff, but directly on the
article's thesis and cheap to try in the VM.

**Risk:** research, isolated to codex-vm + a metadata section; no seed or
fixed-point exposure until/unless it changes emitted code.

**DEFERRED (2026-07-07, Damian-approved scoping):** not implementable
as written -- codex-vm executes guest code natively on host silicon via
WHP (`WHvRunVirtualProcessor`); there is no interpreter loop that could
consume dependency hints, and it does not even parse the CDX header
(skips 224 bytes flat). The co-design experiment would first require an
instruction-level emulator inside the WHP host -- a separate project
whose output is a slow model, not a machine.

**RULING (Damian, 2026-07-07 second session):** the WCET-validation
slice is approved and SHIPPED (below); the rest of WI-3 stays deferred,
and the VM will not gain an interpreting or code-rewriting mode -- the
VM is an observer, never a rewriter. The remaining pickup trigger is a
second consumer for a shared IR-metadata channel.

### WI-3 slice as-built: empirical WCET validation

No new metadata was needed: CDX6010 already reports the per-function
static count and budget, and the map carries address ranges. codex-vm
gained `-wcet <fn>` (up to 4 per run): a hardware execution breakpoint
(DR0-DR3) at the function entry -- no guest byte is written -- then TF
single-stepping to the return, counting only instructions whose RIP is
inside the function's own range (callee bodies excluded, matching the
static count's per-body semantics; DR6.B0-B3 = entry, DR6.BS = step, RF
resumes past the entry fault). `WCET-OBS: <fn> max=<n> calls=<k>` lines
on exit. `build/wcet-validate.ps1` compiles a program, parses the
CDX6010 claims, batches the observation runs, and gates on
**observed <= budget** -- the declared contract, machine-verified per
invocation. Driver: `codex/test/wcet-probe.codex`.

Two findings from the instrument's first run:

1. **CDX6010 under-counts (DRIFT):** `st-append-code` bumps
   `insn-count` by +1 per append CALL, not per instruction, so any
   emitter that batches several instructions into one append (min/max,
   bit ops, most compound emitters) under-reports. Measured: clamp-add
   claimed 28, machine executed 34; a bit-op/min body claimed 26,
   executed 34. Branch-only code shows the expected direction (claimed
   17, worst path 12). Budgets still hold empirically; the harness
   reports observed > static as DRIFT, not failure. FOLLOW-UP: make
   the static count a true instruction count (per-append counts or an
   emitted-subset length decoder), then tighten the harness gate to
   observed <= static.
2. **The ExceptionExitBitmap was inert:** codex-vm set the bitmap but
   never enabled `ExtendedVmExits.ExceptionExit`, so every exception --
   including #DB and #BP -- was delivered to the guest IDT (the !EXC
   dump path); host-side "interception" never fired. Crash reports
   work because they parse the guest's serial !EXC line, not exception
   exits. wcet mode now enables ExceptionExit with the bitmap narrowed
   to #DB only (host-intercepting #PF would break guest demand
   paging); non-wcet runs are byte-for-byte unchanged. INT3 remains
   guest-owned -- vector 3 belongs to the guest's !EXC protocol.

Also observed: the inliner can consume every call site of a small
punctual function (its standalone body then never executes; the
harness reports WARN, a coverage gap, not a violation).

Remaining pickup trigger for the deferred remainder: a second consumer
for a shared IR-metadata channel, so the channel is designed once,
jointly. (An analyzing/interpreting VM mode was ruled out 2026-07-07.)

## 4. The honest limit

Codex still emits a scalar instruction stream that runs on the same
out-of-order, speculating, cache-coherent silicon. Being a clean,
type-first language does not exempt the *emitted* code from the CPU's
reconstruction tax. What the type facts buy is: (a) a *compiler* that
makes better decisions before the stream is handed over (WI-1, WI-2), and
(b) the `[Device]` / SIMD / SMP paths that sidestep the tax where the
work is genuinely parallel. The article's deepest version -- a co-designed
ISA -- is out of scope for real hardware; `codex-vm` (WI-3) is the only
sandbox where that experiment could live.

Net: the article is less a to-do list than a vindication of the
type-first design, with one under-exploited asset -- the linear-types
alias proof -- that codegen is currently leaving on the floor. WI-1 is the
one that turns a safety proof into speed; the rest are locality and
research.
