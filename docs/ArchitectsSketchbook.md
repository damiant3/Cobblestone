# Architect's Sketchbook

Runtime memory model, allocator architecture, register conventions,
and platform constraints for the Codex bare-metal compiler.

## Memory Layout

The bare-metal system occupies a single flat physical address space.
All addresses are identity-mapped (virtual = physical). The single
governing constant is `bare-metal-ram-size` (3 GB) in
`codex/compiler/Emit/X86_64State.codex`. Every other memory value derives from
it.

### Static Layout (boot time)

```
Address              Size       Region
───────────────────  ─────────  ──────────────────────────────────────────
0x00000000           20 KB      Boot / real-mode area
0x00005000 (20 KB)    4 KB      Process table (16 entries × 256 bytes)
0x00006000 (24 KB)    4 KB      IDT (Interrupt Descriptor Table)
0x00007000 (28 KB)    4 KB      Kernel metadata cells (see table below)
0x00008000 (32 KB)   40 KB      Runtime page tables (PML4 + PDPT + 8 PDs)
0x00100000 (1 MB)     4 MB      Binary code segment (bare-metal-load-addr)
                                  Current seed: ~2.3 MB of 4 MB headroom
0x00500000 (5 MB)     1 MB      Serial ring buffer (serial-ring-buf-addr)
0x00600000 (6 MB)     ────      Heap base (bare-metal-heap-base, R10 init)
     │                           Heap grows UP ──►
     │                           (phase decks + bivy, ~150-200 MB for selfhost)
     │
     │                           ◄── Stack grows DOWN
0x0C0000000 (3 GB)    ────      Stack top (bare-metal-stack-top = ram-size)
```

### Kernel Metadata Cells (0x7000 region)

Fixed addresses for runtime state. Defined in
`codex/compiler/Emit/X86_64Boot.codex`, starting at byte 28672 (0x7000).

| Address | Name | Width | Purpose |
|---------|------|-------|---------|
| 28672 | tick-count-addr | 8 | Timer tick counter |
| 28680 | key-buffer-addr | 8 | Keyboard input buffer |
| 28688 | current-proc-addr | 8 | Running process index |
| 28696 | arena-base-addr | 8 | Phase allocator mountain base |
| 28704 | serial-write-pos-addr | 8 | Serial output ring write position |
| 28712 | serial-read-pos-addr | 8 | Serial input ring read position |
| 28720 | **deck-pos-addr** | 8 | Deck allocator position (R10 when deck-bound) |
| 28728 | heap-hwm-addr | 8 | Heap high-water mark |
| 28736 | stack-min-rsp-addr | 8 | Lowest RSP observed |
| 28744 | wd-stale-tick-addr | 8 | Watchdog: last-progress tick |
| 28752 | wd-last-heap-addr | 8 | Watchdog: last-observed R10 |
| 28760 | wd-last-rip-addr | 8 | Watchdog: last-observed RIP |
| 28768 | wd-ring-buf-addr | 128 | Watchdog ring buffer (16 entries × 8) |
| 28896 | wd-ring-head-addr | 8 | Watchdog ring write position |
| 28904 | **deck-bound-counter-addr** | 8 | Deck-bound nesting depth (0 = bivy mode) |
| 28912 | **bivy-save-addr** | 8 | Saved R10 (bivy) when deck-bound > 0 |
| 28920 | stdin-eof-flag-addr | 8 | EOF flag for serial input |
| 28928 | stdin-eof-settled-addr | 8 | EOF settled flag |
| 28936 | cap-expiry-addr | 8 | Capability expiry counter |
| 28944 | sched-ready-head-addr | 8 | Scheduler ready queue head |
| 28952 | sched-current-task-addr | 8 | Current task pointer |
| 28960 | sched-yield-flag-addr | 8 | Cooperative yield flag |
| 28968 | handler-table-base-addr | 512 | Effect handler dispatch table |
| 29480 | fork-pool-cursor-addr | 8 | Fork pool allocation cursor |
| 29488 | ata-identify-buf-addr | 512 | ATA IDENTIFY data buffer |
| 30000 | ata-sector-count-addr | 8 | ATA detected sector count |
| 30008 | slice-table-addr | 32 | Scheduler time-slice table |
| 30056 | boot-factstore-addr | 8 | Boot fact store pointer |
| 30720 | chan-table-base | 2304 | Channel table (16 × 128 + overhead) |
| 33024 | nic-present-addr | 8 | NIC detected flag |
| 33056 | nic-rx-buf-addr | 1536 | NIC receive buffer |
| 34592 | nic-tx-buf-addr | 1536 | NIC transmit buffer |
| 36128 | try-fail-flag-addr | 8 | Try/fail exception flag |
| 36200 | **ap-dispatch-count-addr** | 8 | Processes claimed by a core whose id is not zero. Only `__idle_dispatch` writes it, and the BSP's id is always zero, so a value above zero is evidence an application processor took a process out of the table and ran it. Read by `codex/test/smp-dispatch.codex` |

**Do not claim a cell in this band without grepping `tools/codex-vm.c`
first.** 36152 is a permanent booby trap (a legacy codex-vm output-ring
write position), and 36160 / 36168 / 36176 are codex-vm's blit cells —
the host writes them. 36200 was the first free slot above them.

### Derived Constants

Defined in `codex/compiler/Emit/X86_64State.codex`:

| Constant | Value | Purpose |
|----------|-------|---------|
| bare-metal-load-addr | 0x100000 (1 MB) | Binary load address |
| bare-metal-heap-base | 0x600000 (6 MB) | R10 initial value |
| bare-metal-ram-size | 0x0C0000000 (3 GB) | Total physical memory |
| bare-metal-stack-top | 0x0C0000000 (3 GB) | RSP initial value (dynamic via GPA 0xFE8) |

The CDX header heap field and ELF segment memsz are both computed as
`bare-metal-ram-size - bare-metal-heap-base` (~3 GB minus 6 MB) in
`codex/compiler/Emit/X86_64Chapter.codex`.

## Register Convention

Codex uses a fixed register assignment on x86-64. There is no
System V or Windows ABI — Codex owns the entire machine.

| Register | Role | Lifetime | Notes |
|----------|------|----------|-------|
| **R10** | **Bump allocator pointer** | Global | All heap allocation goes through R10. Points to bivy when deck-bound-counter = 0; points to deck when deck-bound-counter > 0. Never appears in compiled user expressions. |
| **R14** | Local variable 4 (callee-saved) | Per-function | Pushed in every prologue, popped in every epilogue. Also used temporarily as deck-pos in `__deck_list_snoc` and `__list_concat_many` (saved/restored via push/pop). |
| **RAX** | Return value / temp 0 | Caller-saved | |
| **RCX** | Temp 1 | Caller-saved | |
| **RDX** | Temp 2 | Caller-saved | |
| **RSI** | Temp 3 | Caller-saved | |
| **RDI** | Temp 4 / first argument | Caller-saved | First argument in function calls |
| **R11** | Temp 5 | Caller-saved | Used extensively for address loading |
| **RBX** | Local variable 1 (callee-saved) | Per-function | |
| **R12** | Local variable 2 (callee-saved) | Per-function | |
| **R13** | Local variable 3 (callee-saved) | Per-function | |
| **R15** | Closure environment pointer | Per-function | Callee-saved; loaded from caller's closure env at call sites |
| **RBP** | Frame pointer | Per-function | Points to base of current stack frame; locals accessed as `[RBP - offset]` |
| **RSP** | Stack pointer | Global | Grows downward; collision-checked against R10 in every prologue |
| **R8, R9** | Unused | — | Not allocated by the register allocator |

Temp registers cycle: `alloc-temp` rotates through [RAX, RCX, RDX,
RSI, RDI, R11] (mod 6). Local registers are assigned in order: [RBX,
R12, R13, R14]; if all four are used, additional locals spill to the
stack at `[RBP - (32 + 8*n)]`.

## Deck-Bound Mode (R10 Swap)

When `__deck-enter` is called (directly or via `deck-record`):

1. Increment `deck-bound-counter-addr`.
2. If counter was 0 (first entry): save R10 → `bivy-save-addr`;
   load `deck-pos-addr` → R10.
3. All subsequent R10-based allocations (records, lists,
   `__list-with-capacity`, `sort-by` intermediates) go to the deck.

When `__deck-exit` is called:

1. Decrement `deck-bound-counter-addr`.
2. If counter reaches 0 (last exit): save R10 → `deck-pos-addr`;
   load `bivy-save-addr` → R10.

Nesting is supported: inner `deck-record` calls increment/decrement
the counter without swapping R10.

## Phase Allocator

Defined in `codex/compiler/Core/PhaseAllocator.codex`. The compiler runs in
phases (lex, parse, scope, check, lower, lift, emit). Each phase
allocates temporary data that can be discarded before the next phase.
The phase allocator provides this via two mechanisms:

### Bivy

A bump allocator. `pitch(size)` saves the current heap pointer and
advances R10 by `size` bytes, returning the old pointer. `strike(start)`
restores R10 to a previously saved position, effectively freeing
everything allocated since that `pitch`.

Bivy allocations are cheap (one add instruction) but cannot be
selectively freed. They are arena-scoped: everything allocated in a
phase is freed together when the phase ends.

### Deck

A structured allocator built on top of bivy. `build(size)` saves the
heap position, sets `deck-pos-addr` to that position via `__deck-set`,
and advances R10 past the reserved region. `seal(start)` is a no-op
(`__heap-advance 0`) — it exists as a semantic marker.

Deck regions survive phase compaction because `phase-compact` rewinds
R10 to `deck-pos` — everything below that address (the deck) is
preserved; everything above it (bivy scratch) is reclaimed. The deck
position cascades between phases: each phase's `build` starts its deck
at the current R10, which is past the previous phase's sealed deck.

The deck is used for data that must persist across phase boundaries
(e.g., the AST produced by parsing must survive into type checking).
Bivy is used for scratch data within a phase.

### Phase Lifecycle

1. `phase-start`: Records `bivy-origin` (R10) and `deck-origin`
   (`__deck-pos`) at the start of a phase.
2. The phase runs, allocating via bivy (scratch) and deck (persistent).
3. `phase-measure`: Captures `bivy-hwm` for diagnostics.
4. `phase-compact`: Restores R10 to the current deck position,
   reclaiming all bivy scratch from the phase.

Phase boundaries are recorded in `PhaseMetrics` records and reported
as `heap-marks` in the compile pipeline (`codex/compiler/opening.codex`).

## Compilation Phase Map

The compiler runs in phases. Each phase allocates a **deck** (durable
output) and a **bivy** (scratch). At phase end, `phase-compact`
restores R10 to the deck-pos, reclaiming all bivy scratch. Deck data
persists as the base for subsequent phases.

All phase work runs inside `deck-record(...)`, so R10 points at the
deck during phase execution. Bivy usage is near-zero (only the 16-byte
`PhaseStart` record per phase).

### Phase Deck Layout (selfhost, ~1.39 MB source)

Deck heights are fixed generous floors (`codex/compiler/Core/
BuildSettings.codex`, Demand Decks section). The heap range
[6 MB, 2 GB) boots with its PD entries not-present and a #PF handler
commits identity 2 MB pages on first touch, so a floor costs address
space, not memory — physical consumption is what a phase actually
writes. The survey-multiplier system that previously sized decks from
source length was deleted 2026-07-07.
The pipeline has 6 TEXT-mode frontend phases plus emit. CDX mode adds
RESOLVE, LIFT, and INLINE between LOWER and EMIT (CL 2429 split the
frontend so TEXT skips those). ConstructedTy resolution and lambda
lifting run in their own phases (CLs 2135, 2169), each with an
independent `phase-compact` cycle so bivy scratch is reclaimed.

Three phases use the **reservation-copy pattern** (CLs 3805, 3849,
3894): a keep deck is reserved FIRST, a bounded scratch deck is built
above it, and a deep-copy walk moves survivors into the reservation.
One `phase-compact` then reclaims the scratch, bivy, and unused
reservation tail. This means the heap does NOT monotonically stack --
dead decks are reclaimed at phase boundaries:

- **Frontend keep deck** (CL 3894): reserved below everything in
  `compile-checked` (S x 40 + 2 MB). At the desugar boundary,
  `copy-as-*` copies the AChapter, assignments, colliding, bags,
  heap-marks, and phase-metrics into the reservation. One compact
  reclaims the LEX deck, PARSE keep deck, and DESUGAR deck together.
  SCOPE starts at ~57 MB instead of ~146 MB.

- **PARSE keep deck** (CL 3849): reserved below the parse scratch
  (tokens x 160 + 1 MB). `copy-sx-*` copies the Document and
  assignments into the reservation; one compact reclaims the scratch.

- **LOWER scratch reclamation** (CL 3805): RESOLVE deck reserved
  first, bounded LOWER scratch above it, `rewrite-ir-chapter`
  deep-copies survivors into the reservation.

| Phase | Floor | Selfhost used (2026-07-07) | Pattern |
|-------|-------|---------------------------|---------|
| LEX | 96 MB | 21.4 MB | standard deck+compact |
| PARSE keep | 64 MB | 9.1 MB | reservation-copy |
| PARSE scratch | 192 MB | 32.6 MB | reclaimed at keep boundary |
| DESUGAR | 64 MB | 16.3 MB | reclaimed at frontend keep boundary |
| SCOPE | 96 MB | — | standard |
| CHECK | 640 MB | 156.2 MB | standard |
| CHECK keep | 96 MB | — | reservation-copy |
| Frontend keep | 192 MB | — | reservation-copy |
| LOWER | 320 MB | 115.5 MB | reservation-copy |
| RESOLVE | 192 MB (CDX only) | — | standard |
| LIFT | 96 MB (CDX only) | — | standard |
| EMIT | per-func | — | streaming (CL 3793) |

A phase that exceeds its floor halts with CDX9002 (DeckOverflow, now
"deck floor exceeded") — retained as a hard guard, though the selfhost
runs at 2-6x headroom under every floor.

### Why the floors are flat, not derived

The compiler used to size each phase deck from a formula over the source
length (`survey-*-mul`). **That system is deleted.** The multipliers could
not be sized honestly — they were non-monotonic (20 worked, 25 did not,
40 silently miscompiled a grown self-compile), and an under-reservation
corrupted the heap rather than raising a diagnostic.

The fix was structural, not numeric: flat generous floors over
demand-paged address space. Type-dense plug source and the selfhost draw
from the same reservation and pay only for the pages they touch. A floor
costs address space, not memory.

If you find `survey-*-mul`, `SurveyConfig`, `-Survey`, or
reservation-by-formula referenced anywhere, it is stale.

```
Heap (after desugar boundary compact, CDX selfhost)

base     Reservation-copy pattern means dead decks are reclaimed.
(R10) ──►┌──────────────────────────────────────┐
0x600000  │  init-phase-allocator (mountain base)│
          ├──────────────────────────────────────┤
          │  Frontend keep deck      ~25 MB      │  Copied survivors:
          │  Floor: 192 MB                       │  AChapter, assignments,
          │  (LEX, PARSE-keep, DESUGAR decks     │  colliding, bags,
          │   were above -- RECLAIMED at the     │  heap-marks, metrics
          │   desugar boundary by phase-compact) │
          ├──────────────────────────────────────┤  deck-origin ~57 MB
          │  SCOPE deck                12.3 MB   │
          │  Floor: 96 MB                        │
          │  Name bindings, slug-mangled names   │
          ├──────────────────────────────────────┤
          │  CHECK deck                69.4 MB   │
          │  Floor: 640 MB                       │
          │  Type environment, resolved types    │
          ├──────────────────────────────────────┤
          │  LOWER survivors (reservation-copy)  │
          │  Floor: 320 MB                       │
          │  (LOWER scratch RECLAIMED at the     │
          │   RESOLVE reservation boundary)      │
          ├──────────────────────────────────────┤
          │  RESOLVE deck        (CDX only)      │
          │  Floor: 192 MB                       │
          ├──────────────────────────────────────┤
          │  LIFT deck           (CDX only)      │
          │  Floor: 96 MB                        │
          ├──────────────────────────────────────┤
          │  EMIT (streaming, CL 3793)           │
          │  EmitWorkspace (code 8 MB + data 2M) │
          │  Accumulator lists (11 × 32K cap)    │
          │  Per-function CodegenState            │
          ├──────────────────────────────────────┤
          │  (bivy: per-function scratch)        │
          │  Reclaimed by __heap-save/restore    │
          │  in emit-all-defs loop               │
          └──────────────────────────────────────┘
                              ▲
                              │ gap (~2.9 GB for 3 GB RAM)
                              ▼
          ┌──────────────────────────────────────┐
0x0C0000000│ Stack top (grows downward)          │
          │  ~1 MB typical usage for selfhost    │
          └──────────────────────────────────────┘
```

### Emit Phase Detail

ConstructedTy resolution and lambda lifting run in their own phases
before emit (RESOLVE and LIFT). The emitter receives pre-resolved,
pre-lifted IR and a sorted `List TypeBinding`. After `emit-build`
creates the emit deck and `__deck-enter` swaps R10, the emit init
allocates in this order (all on the deck):

```
Emit deck
──────────────────────────────────────────────────────────────
1. bare-metal-trampoline        ~100 bytes   Boot stub
2. init-emit-workspace          10 MB        Code buf (8 MB) + data buf (2 MB)
3. CodegenState                 ~300 bytes   With 11 pre-allocated accumulator lists:
     fo-names, fo-offsets          2 × 256 KB   Function offset table
     cp-offsets, cp-targets        2 × 1 MB     Call patch table (4× capacity)
     fa-offsets, fa-targets        2 × 256 KB   Far-address patch table
     rf-poffsets, rf-roffsets      2 × 512 KB   Rodata fix-up table (2× capacity)
     da-poffsets, da-offsets       2 × 256 KB   Data-address patch table
     stack-overflow-checks         1 × 256 KB   Stack overflow check positions
                                ─────────
                                ~5 MB total accumulator backing buffers
4. build-x86-arities            ~10 KB       Sorted arity table
5. emit-runtime-helpers          (writes to code buffer, no deck alloc)
──────────────────────────────────────────────────────────────
   __deck-exit (R10 back to bivy)

6. emit-all-defs loop:
     Per function:
       __heap-save h
       emit-function → writes machine code to code buffer,
                        static data to data buffer,
                        pushes to accumulator lists (in-place, no alloc)
       __heap-restore h (reclaims per-function scratch: locals, temps, spills)
       deck-record(codegen-carry-forward) → new CodegenState on deck (~300 bytes)

7. x86-64-finalize-* → patches, CDX header, output
```

### Accumulator Capacity

`accum-capacity` = **65536** (defined in `codex/compiler/Core/BuildSettings.codex`
— this line said 32768 until it was re-measured; do not carry it forward).

All accumulator lists are pre-allocated via `__list-with-capacity`.
`list-push` writes in-place with no allocation as long as the list
stays within capacity. The `accum-at-capacity` guard in
`codex/compiler/Emit/X86_64.codex` checks all 11 lists before each function
and halts with **CDX9002-band `CDX9005` (AccumOverflow)**.

**Exceeding capacity corrupts the table; it does not merely cost heap.**
A push past capacity doubles and reallocates like any other, and the new
backing lands in the per-function bivy that `emit-all-defs` reclaims with
`__heap-restore` — so the accumulator is left pointing at reclaimed memory.
Measured, not assumed: built with `accum-capacity` at 16, the compiler
emits a factorial whose call-patch target is the empty string, and the
only complaint is `CDX2040: Unresolved call to ''`. This is why the
accumulators are sized once on the deck and why the guard exists.

**That guard was written and never called** until 2026-07-16 — this
paragraph asserted it ran for as long as it did not. A guard defined and
left unwired is worth exactly what no guard is worth.

### Emit Output Buffers

| Buffer | Size | Contents |
|--------|------|----------|
| code-buffer | 8 MB (`code-buffer-size`) | x86-64 machine code; written sequentially via `st-append-code` |
| data-buffer | 2 MB (`data-buffer-size`) | String literals, CCE tables, static data |

Current selfhost binary: ~2.3 MB code, ~100 KB data.

## Emit Allocator

Defined in `codex/compiler/Emit/EmitAllocator.codex`. The code generator needs
two large contiguous buffers for the output binary:

- **code-buffer**: Machine code (x86-64 instructions). Capacity set by
  `code-buffer-size` in `codex/compiler/Core/BuildSettings.codex` (currently 8 MB).
- **data-buffer**: Static data (string literals, CCE tables). Capacity
  set by `data-buffer-size` in `codex/compiler/Core/BuildSettings.codex`
  (currently 2 MB).

`init-emit-workspace(text-cap, data-cap)` allocates both buffers from
the heap and returns an `EmitWorkspace` record with base addresses and
capacities. The code generator writes into these buffers via
`st-append-code` and `__buf-write-bytes`, tracking `code-len` and
`data-len` in `CodegenState`.

After code generation, the code and data buffers are assembled into
the final CDX binary by `CdxWriter.codex`. Container formats (ELF,
PE, GPT/FAT images) are produced by plug CDX binaries in `codex/plugs/`.

## Heap and Stack

Heap and stack share the arena between `bare-metal-heap-base` (6 MB) and
`bare-metal-stack-top` (= ram-size, 3 GB). The heap grows upward via
register R10; the stack grows downward via RSP. See the Register
Convention table above for the full register map.

### Spawn Regions (Slot-Indexed)

Spawned-process regions are slot-indexed, never carved from the
spawner's R10: process slot N owns the fixed region
`spawn-pool-base + N * spawn-slot-region-size` — 1 GB base, 32 MB per
slot, 16 slots spanning [1 GB, 1.5 GB) of demand-paged address space.
`__spawn_pool_carve` (X86_64ProcessHelpers) reads the claimed slot
index from R12 and the region size from RDX, returning the heap base
in RDI and the stack top in RSI — five instructions, no memory cell,
no cursor.

**The process table is the allocator.** A slot freed by exit or kill
frees its region; the next spawn into that slot reuses the same
addresses. A long-running spawn loop therefore plateaus at its
working set instead of consuming fresh address space per spawn
(physical pages commit on first touch and stay committed, so reuse
bounds physical consumption at the per-slot high-water mark; `__alloc`
zero-fills, so a reused region's stale bytes are never visible
through allocations). Pinned by `codex/test/spawn-reuse.codex`.

Plain `process-spawn` regions hold `proc-spawn-heap-size` +
`proc-spawn-stack-size` (1 MB + 1 MB); `process-spawn-with-heap`
takes a caller-chosen heap size, bounds-checked at the call site —
a request that cannot fit inside one slot region (heap + 1 MB stack
> 32 MB) is refused with -1, never silently overlapped. The parent
pre-touches the child's stack pages before the child first runs
(`emit-spawn-stack-pretouch`) because a stack must never point into
a not-present page. Spawn-capable programs need the demand-range top
above 1.5 GB (any `-mem` from ~1664 MB; the default is 3072).

**Do not carve a spawn region from the spawner's own R10 frontier.** That
is correct only for proc 0, which owns the whole heap; a spawned child
owns a fixed region, so a child spawning a grandchild hands out memory
overlapping its own stack. The slot table is the allocator — use it.

**Guest cell 36152 must never be claimed for metadata** — legacy codex-vm
builds read it as the retired 0x700000 output-ring write position.

### Collision Detection

Every function prologue (`emit-prologue` in `codex/compiler/Emit/X86_64.codex`)
performs two checks:

1. **Stack tracking**: Compare RSP against the stored minimum
   (`stack-min-rsp-addr`). If RSP is lower, update the minimum. This
   tracks the stack high-water mark.

2. **Collision check**: `cmp rsp, r10`. If RSP < R10, the stack has
   grown into the heap. The prologue jumps to `__out_of_memory`, which
   resets RSP to `bare-metal-stack-top` and prints a diagnostic over
   serial before halting.

There is no guard page or MMU-based protection. The check is a software
compare on every function entry.

---

## Vector / SIMD Register Allocation

Vector registers (XMM0–XMM15 on x86-64) are a separate allocation pool
from integer registers. The two domains never compete for the same
physical register.

| Role | Registers | Notes |
|------|-----------|-------|
| Vector temps | XMM0–XMM7 | Rotation scheme, like integer `alloc-temp` |
| Vector locals | XMM8–XMM15 | Callee-saved in our convention |
| Scalar float | XMM0/XMM1 | Pre-SIMD usage for `Real` arithmetic |

SSE2 packed instructions use 128-bit XMM registers. `Vector 2 Real`
(2 × f64 = 128 bits) fills one XMM register. `Vector 4 (Real approximate)`
(4 × f32 = 128 bits) also fits in one XMM.

### Alignment

Vector values carry natural alignment: `N * sizeof(T)` rounded up to
the next power of two, minimum 16 bytes. The bump allocator (`__alloc`)
rounds R10 up before allocation. Stack spill slots for vectors must also
respect alignment — the prologue already aligns RSP to 16 bytes (SSE2
minimum).

Future AVX/AVX2 (YMM, 256-bit) requires 32-byte alignment. AVX-512
(ZMM, 512-bit) requires 64-byte alignment. These are Phase 2/3 concerns.

### Heap High-Water Mark

`heap-hwm-addr` stores the highest value R10 has reached. Updated by
`emit-update-heap-hwm` after each compilation phase. Reported over the
control serial channel as `HEAP:<value>` at the end of each compile run.

## Page Tables

### Trampoline (Boot)

The multiboot trampoline (`codex/compiler/Emit/X86_64IO.codex`) contains
hardcoded 32-bit machine code that runs before long mode is active.
It identity-maps 4 GB of physical memory using 2 MB pages:

- First PD (at 0x3000): 512 entries mapping 0x00000000–0x3FFFFFFF (1 GB)
- Three PDs (at 0x10000–0x12000): 1536 entries mapping
  0x40000000–0xFFFFFFFF (3 GB)

This mapping is temporary. It exists only long enough for the 64-bit
`__start` code to run and install the runtime page tables.

### Runtime

`emit-build-process-page-tables` in `codex/compiler/Emit/X86_64Boot.codex`
builds identity-mapping page tables covering RAM plus the device
gigabyte above it:

- PML4 at pml4-addr (one entry pointing to PDPT)
- PDPT at pml4-addr + 4096 (`bare-metal-total-pd-count` entries, one per GB)
- PD pages at pml4-addr + 8192 onward (512 entries each, 2 MB pages)

With 3 GB RAM: 3 RAM PDs + 1 device PD = (2 + 4) * 4096 = 24 KB total,
from 0x8000 to 0xE000. The GDT (0x12800) and TSS (0x13000) sit above it.

The runtime tables replace the trampoline tables via `mov cr3, rax`
during `emit-process-setup`.

#### The device gigabyte

`emit-fill-device-pd` maps `[3 GB, 4 GB)` identity — present, read-write,
NX (nothing up there is code). It costs one 4 KB page directory and
nothing at runtime. `bare-metal-device-pd-index`, `-device-page-start`,
`-device-page-end` and `-total-pd-count` are in `X86_64State.codex`.

This is where everything x86 puts above RAM lives: the LAPIC at
0xFEE00000, the IOAPIC at 0xFEC00000, the HPET at 0xFED00000, and every
PCI BAR codex-vm advertises. Until 2026-07-13 the tables stopped at
`bare-metal-ram-size` and all of it was unmapped, which is why codex-vm's
device model reaches almost everything through port I/O rather than MMIO —
**the device model grew around a ceiling that is now gone.** Reach for
MMIO first when adding a device.

The `#PF` handler and `emit-demand-unmap` both address the PDs as one
flat array of 8-byte entries based at 0xA000; the device PD extends that
array contiguously (page 1536's entry lands at 0xD000, which is exactly
where PD 3 begins), so their arithmetic is unchanged. The demand range
only ever spans pages 3..1024, so it never reaches the device PD.

The consequence to know: **a stray pointer above 3 GB now reaches the
bus instead of faulting.** That is what it would do on real hardware,
where those addresses are decoded by devices rather than by RAM — but it
does mean the page tables no longer catch a wild high pointer for you.

### Demand Paging (2026-07-07, hardened 2026-07-06 val CLs 7207-7210)

Before the CR3 switch, boot clears the PD entries covering heap pages
[6 MB, top) — the demand range. The top is computed from the actual
RAM size (GPA 0xFE8): `min(1024, ram_pages - 32)` in 2 MB pages, so
the top 64 MB of RAM always stays present for the boot stack and any
`-mem` from ~128 MB boots. At 3 GB the top equals the 2 GB cap and
the stack/GOP region [2 GB, 3 GB) is present from boot.

The first touch of each 2 MB page raises #PF (vector 14). The
vector-14 stub preserves the CPU error code; the handler grows the
heap only for not-present faults (error-code P=0) inside the range —
it writes the identity PDE (`(CR2 & ~0x1FFFFF) | 0x83 | NX`),
increments the touched-page counter (cell 30688, the honest physical
metric — the R10 HWM reports floor reservations), invlpg, iretq.
Protection or reserved-bit faults (P=1), out-of-range faults, and
every other vector fall through to the exception dump. NX matters:
everything above the code boundary is non-executable in the boot
mapping, and demand pages match it.

Invariant: a stack must never point into a not-present page — the CPU
cannot deliver a #PF frame onto the faulting stack — so spawn helpers
pre-touch every 2 MB page of the stacks they carve from the heap
(`emit-spawn-stack-pretouch`, unrolled at emit time from
`proc-spawn-stack-size`). When the invariant is violated anyway, the
double fault is delivered on the TSS IST1 emergency stack (TSS at
0x13000, GDT at 0x12800, 2 KB stack below 0x14800) and produces the
standard `!EXC` dump instead of a silent triple fault. BSP only —
an AP double fault is still fatal (per-core TSS is future work).

## SMP Memory Model

When codex-vm runs with `-smp N` (N > 1), the guest boots with
multiple virtual processors. The core count is written to GPA 0xFF8
before boot; the boot code reads it to decide whether to send
INIT/SIPI to start application processors.

**Bring-up.** `emit-smp-init` (`X86_64Boot.codex`) publishes the AP entry
point at GPA 0x1000 and the stack table at GPA 0xF00, then writes the
LAPIC ICR: an INIT IPI, then two start-up IPIs, destination shorthand
"all excluding self". The ICR write is what starts the cores. Each AP
takes its stack from the table by core index, adds one to the ready
count (cell 4080) with a locked add, and then goes to `__idle_dispatch`
to look for work. The BSP spins on that count — on `pause`, not `hlt`:
nothing sends the BSP an interrupt when an AP checks in, so a halted BSP
would never wake. The spin is fuel capped, so a core that never answers
costs a delay and not the boot.

The start-up IPI's vector field is zero. On real silicon that field names
the 4 KB page an AP begins executing in, which caps the entry below 1 MB
and requires a real-mode trampoline; codex-vm takes the full 64-bit entry
from GPA 0x1000 instead. Physical multi-core needs that trampoline
written (BACKLOG 4.2).

**Per-core TSS and emergency stacks.** A double fault is delivered on the
stack named by IST1 in the TSS the task register points at. The task
register is per-core and a TSS cannot be shared — two cores would fight
over its busy bit and be handed the same emergency stack — so the GDT
carries one TSS descriptor per core at selector `24 + core * 16`, the TSS
array holds one 128-byte-strided entry per core at `0x13000`, and each
core's IST1 points at its own 2 KB stack in `[0x15000, 0x1D000)`, below
the AP idle stacks. Each AP loads the runtime GDT (the hypervisor starts
it on the boot GDT, which carries no TSS) and then its own task register,
and reports back what the CPU accepted at `0x13800 + core * 8`.

Two things must be true for a fault on an AP to be *seen*: the core needs
its own emergency stack, **and** codex-vm has to serve COM1 on the AP
thread. Miss the second and the guest writes a perfectly good dump that
the host throws away.

**Per-core stacks.** Each AP gets an independent idle stack. The BSP
stack starts at the actual RAM top (GPA 0xFE8). AP idle stacks live
in always-present low memory — 16 KB each at
`AP[i] stack = 0x20000 + i * 0x4000` ([0x20000, 0x60000), below the
EBDA) — never in the demand-paged heap range, because an AP takes its
first interrupt on this stack and the CPU cannot deliver a fault
frame onto a not-present page. The guest writes these addresses to
the stack table at GPA 0xF00 before SIPI; codex-vm falls back to
`0xC0000000 - i * 0x10000` only for table entries left zero. Real
work on an AP runs on scheduler-provided stacks.

**Scheduling on an AP.** An application processor is not a special case.
It goes to **`__idle_dispatch`** — the same routine the boot processor
goes to when it runs out of work — walks the 16-slot process table,
claims a READY slot with a `LOCK CMPXCHG` on the state word
(READY → RUNNING), takes the time slice its priority is due, and resumes
it. From that instant the core is running a real process, on that
process's own stack, with that process's own R10.

Core 0 may claim slot 0; an AP may not. Slot 0 is the program the machine
booted and it owns the boot stack and the main heap.

**A core that parks must leave the process's stack first.** This is the
whole reason `__idle_dispatch` exists as a routine rather than a loop
inlined at each site. A core with no work is still standing on the stack
of the process it was last running — and `process-wait` marks itself
BLOCKED, so the wake loop is about to mark it READY, another core will
claim it, and resume it *on that stack*. Two cores, one stack; the parked
core's next interrupt pushes a frame straight through the other core's
process. So the parked core switches RSP to its own idle stack **before**
it scans. `process-exit`, `process-wait` and the channel block path all
end in `jmp __idle_dispatch` for exactly this reason.

**Per-core identity: a core asks the process it is standing in.** There is
no MSR, no LAPIC read and no GS base involved. `proc-core-offset` (process
entry offset 8) records the core that claimed the slot, stamped by
whichever core won the CMPXCHG; an AP seeds its own id from R15 at
bring-up. A core recovers its identity by reading that field out of the
process it is currently running, and from the id it computes its idle
stack: `ap-stacks-base + (core + 1) * ap-stack-size`. AP idle stacks are
handed out from index 1, so region slot 0 was free and is the BSP's.

**Per-core heap: there isn't one, and none is needed.** `CoreHeap`
(`codex/os/sched/CoreHeap.codex`) is a **pure model — nothing calls it**,
and no AP has ever set R10 from it. It is not
needed on the critical path either: a spawned process carries its own
slot-indexed heap region *and its own R10* in its saved context, so a core
running one gets the right allocator by resuming it. Whether the
*compiler's* bivy should be split per core is a separate and open
question — see `docs/PM/BACKLOG.md` 4.11.

**Every core has a clock.** The PIT's IRQ reaches the boot processor
alone, so an AP used to run whatever it was given until that process
yielded, blocked or exited. Each AP now arms its **own local APIC timer**
at bring-up (`emit-ap-timer-init`, `X86_64Boot.codex`): it enables its
LAPIC, programs the LVT timer periodic on **vector 48**, sets the initial
count, and only then raises IF. A process on an application processor is
preempted exactly as one on the BSP is.

Two clocks therefore arrive at `__interrupt_common`: **vector 32** (the
PIT, on the BSP) and **vector 48** (an AP's local timer). They run the
same scheduling path — it was always per-core-safe, deriving the running
process from the interrupted RSP and claiming a replacement with a
CMPXCHG — and differ only in **which chip is told the interrupt is over**:
the 8259 for the PIT, the local APIC for the LAPIC timer
(`emit-timer-eoi`). Send the wrong one and the raising chip believes the
interrupt is still in service and never delivers another, which reads as a
core that was preempted exactly once and then stopped.

The tick count is incremented with a **locked** add, because more than one
core increments it now; a plain load-add-store loses ticks.

Evidence lives at cell **36216** (`ap-preempt-count-addr`): every timer
interrupt taken on a core whose id is not zero bumps it, and the BSP's id
is always zero. `codex/test/smp-preempt.codex` reads it. What is still
open is BACKLOG 4.11: no work stealing, no affinity, an idle core
pause-spins rather than halting, and proc-0 migration is unproven.

**Atomics.** Six builtins: `atomic-load`, `atomic-store`,
`atomic-cas`, `atomic-add`, `atomic-exchange`, `memory-fence`.
x86-64 codegen: LOCK CMPXCHG, LOCK XADD, LOCK XCHG, MFENCE.

**IPI.** Inter-processor interrupts via LAPIC ICR writes. Used for
cross-core wake and TLB shootdown. Lock-free MPSC channels for
message passing between cores.

## Known Platform Constraints

### 4 GB Barrier and MMIO Hole

Physical addresses 0xC0000000–0xFFFFFFFF (the top 1 GB of the 32-bit
address space) are reserved for PCI MMIO on x86 platforms.

As of 2026-06-20, `bare-metal-ram-size` is 3 GB (0xC0000000). The
stack top sits at the RAM boundary. The seed reads the actual VM
memory size from GPA 0xFE8 (written by codex-vm before boot) and
sets RSP dynamically, so the same seed binary works with any `-mem`
value. The default VM memory is 3072 MB (`-mem 3072`), reduced from
8 GB to prevent host memory exhaustion when running concurrent VMs.

### Code Buffer Ceiling

The compiler's own code segment is approximately 2.1 MB. The code
buffer (`code-buffer-size`) is 8 MB with roughly 5.9 MB headroom. The
serial ring buffer at 0x500000 (5 MB) sits between the code and heap,
providing a hard upper bound on code size at the current layout (4 MB
for the binary, starting at 0x100000).

### Stack Size

The stack starts at `bare-metal-stack-top` and grows downward. Typical
self-compilation uses approximately 1 MB of stack. The prologue
collision check (`cmp rsp, r10`) is the only protection against stack
overflow. There is no guard page.

## Codegen Quality vs C and the JITs

Function-body x86-64 instruction counts for the benchmarks in `bench/`
(build + compare with `bench/compare.ps1`). The Codex column is measured
2026-07-17 on the shipping seed C0B74DBE with the LIR selector live; the
C and JIT reference columns (cl.exe, the .NET JITs) were measured
2026-06-12 and do not move. The four primordial benches carry the full
reference set; the elaborate benches have no in-tree x86 C/JIT reference,
so only the Codex count is shown. Full optimization history and per-CL
breakdown: `docs/Reference/CodegenAnalysis.md`.

| Bench    | Codex | C /Od | C /O2 | C# JIT | F# JIT |
|----------|------:|------:|------:|-------:|-------:|
| fib      | 22    | 19    | 20    | 21     | 21     |
| fact     | 13    | 16    | 15    | 16     | 15     |
| gcd      | 10    | 18    | 14    | 11     | 9      |
| sum      | 7     | 20    | 23    | 9      | 4      |
| ack      | 23    | --    | --    | --     | --     |
| tak      | 37    | --    | --    | --     | --     |
| collatz  | 13    | --    | --    | --     | --     |
| locals   | 18    | --    | --    | --     | --     |
| regright | 14    | --    | --    | --     | --     |

Codex now beats C /O2 on fact (13 vs 15), gcd (10 vs 14), and sum (7 vs
23, a 70 percent reduction); fib is +2 over /O2. Against the JITs, gcd
(10) beats the C# JIT (11), and sum (7) beats it (9); the F# JIT still
wins gcd (9) and sum (4) by tight margins.

Campaign start (CL 3091): fib 107, fact 79, gcd 79, sum 82. The
structural changes that closed the gap, in order: destination-driven
emission, immediate operands, TCO parallel-move arg shuffle (sum loop
at F# JIT density: add/lea/jmp), R8/R9-staged binary operands (binary
expressions consume zero locals), minimal and near-leaf frame elision
(pure leaves skip the frame pointer AND the stack guard -- a call-free
function cannot grow the stack; near-leaves keep the guard because the
guard chain through recursive calls is the heap-collision detector),
IrRemInt with the leaf inliner (math-mod sites become inline
idiv/RDX), and commutative both-complex shortcut (pop+op replaces
mov+pop+mov+op for tree-recursive add/mul).

sum-to-N beats C at both optimization levels. The remaining gap to the
JITs is the registers they win through full linear-scan allocation of
named bindings. The LIR selector now carries a Wimmer linear-scan
allocator and is live in the default pipeline (BACKLOG 3.8), but it is
instruction-neutral against the tree emitter today -- so beating the tree
on named-binding allocation, and widening the class of functions the
selector handles, is the next frontier.

### RISC-V RV64 Codegen Quality (CL 6287)

Cross-compiled via the RISC-V plug pipeline. Compared against
`riscv64-linux-gnu-gcc` 13.3.0 cross-compiler.

| Bench   | Codex RV64 | GCC -O0 | GCC -O2 | GCC -Os |
|---------|----------:|--------:|--------:|--------:|
| fib     |        20 |      34 |   241*  |      22 |
| fact    |        14 |      27 |      14 |       9 |
| gcd     |         7 |      26 |       8 |       6 |
| sum     |         7 |      27 |      11 |       9 |
| ack     |        24 |      33 |     103 |      22 |
| tak     |        39 |      36 |      33 |      34 |
| collatz |        15 |      29 |      20 |      13 |
| locals  |        15 |      52 |      25 |      19 |

*GCC -O2 fib transforms tree recursion into a 241-instruction
iterative loop (O(n) runtime, larger code). GCC -Os keeps the
recursive form; that is the fair codegen comparison.

Aggregate: Codex 141 vs GCC -Os 134 (+5%). Four benchmarks beat
GCC -Os (fib, sum, locals by 21%, collatz within 15%). fact gap
(+56%) is structural: GCC transforms recursion to iteration, a
compiler-level optimization not available to the plug.

Two optimization campaigns. Phase 1 (CLs 6147-6172, 8 CLs):
deferred save-reg, destination-driven emission, frameless TCO,
inline builtins, compact prologue. Average reduction 66%. Phase 2
(CLs 6261-6287, 17 CLs, 135 insns eliminated): pow2 strength
reduction, NOP compaction, direct TCO with dependency analysis,
expanded frameless TCO with temp-only locals, direct N-arg emission,
reordered mixed-TCO, last-arg skip in TCO shuffle.

## ARM64 Boot Sequence

The ARM64 kernel binary starts with a 2 KB exception vector table
(16 entries × 32 instructions × 4 bytes), followed by `__start`.

### `__start` Runtime Detection (CL 5010)

`__start` detects whether it was entered from a UEFI stub or bare
metal by checking X28 (the heap register):

```
vector_table:     2048 bytes of WFI+B.self entries
__start:
  CBNZ X28, uefi    ; UEFI stub already set X28 → skip init
  ADR  X9, vector_table
  MSR  VBAR_EL1, X9  ; install exception vectors
  MOVZ X9, #0x4800, LSL #16
  MOV  SP, X9         ; SP = 0x48000000
  MOVZ X28, #0x4010, LSL #16
                       ; X28 = 0x40100000 (heap = code base for bare metal)
uefi:
  BL   opening
  WFI
```

On UEFI boot, the PE stub (`Arm64PeWriter.codex`) calls
`AllocatePages` for the kernel, copies code+rodata, sets
SP = 0x7F000000, X28 = heap_base (past code+rodata), and jumps to
`__start`. X28 is non-zero → the CBNZ skips bare-metal init →
`opening` runs with UEFI's memory layout.

On bare-metal boot (Renode), X28 is 0 on cold start → `__start`
installs VBAR, sets SP and X28, then calls `opening`.

### Memory Layout (ARM64 UEFI)

```
0x40100000    Kernel code (copied from PE .text)
  + align8(code)  Rodata (string literals)
  + align4K(code+rodata)  Heap base (X28)
0x7F000000    Stack top (SP, grows down)
```

### Memory Layout (ARM64 Bare Metal / Renode)

```
0x40100000    Kernel code (loaded by bootloader)
  = X28 init   Heap base (shares code region)
0x48000000    Stack top (SP, grows down)
```