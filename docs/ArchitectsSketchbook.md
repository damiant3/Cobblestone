# Architect's Sketchbook

Runtime memory model, allocator architecture, register conventions,
and platform constraints for the Codex bare-metal compiler.

## Memory Layout

The bare-metal system occupies a single flat physical address space.
All addresses are identity-mapped (virtual = physical). The single
governing constant is `bare-metal-ram-size` (2 GB) in
`codex/Emit/X86_64State.codex`. Every other memory value derives from
it.

### Static Layout (boot time)

```
Address              Size       Region
───────────────────  ─────────  ──────────────────────────────────────────
0x00000000           20 KB      Boot / real-mode area
0x00005000 (20 KB)    4 KB      Process table (16 entries × 256 bytes)
0x00006000 (24 KB)    4 KB      IDT (Interrupt Descriptor Table)
0x00007000 (28 KB)    4 KB      Kernel metadata cells (see table below)
0x00008000 (32 KB)   32 KB      Runtime page tables (PML4 + PDPT + PDs)
0x00100000 (1 MB)     4 MB      Binary code segment (bare-metal-load-addr)
                                  Current seed: ~2.1 MB of 4 MB headroom
0x00500000 (5 MB)     1 MB      Serial ring buffer (serial-ring-buf-addr)
0x00600000 (6 MB)     ────      Heap base (bare-metal-heap-base, R10 init)
     │                           Heap grows UP ──►
     │                           (phase decks + bivy, ~150-200 MB for selfhost)
     │
     │                           ◄── Stack grows DOWN
0x80000000 (2 GB)     ────      Stack top (bare-metal-stack-top = ram-size)
```

### Kernel Metadata Cells (0x7000 region)

Fixed addresses for runtime state. Defined in
`codex/Emit/X86_64Boot.codex`, starting at byte 28672 (0x7000).

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

### Derived Constants

Defined in `codex/Emit/X86_64State.codex`:

| Constant | Value | Purpose |
|----------|-------|---------|
| bare-metal-load-addr | 0x100000 (1 MB) | Binary load address |
| bare-metal-heap-base | 0x600000 (6 MB) | R10 initial value |
| bare-metal-ram-size | 0x80000000 (2 GB) | Total physical memory |
| bare-metal-stack-top | 0x80000000 (2 GB) | RSP initial value |

The CDX header heap field and ELF segment memsz are both computed as
`bare-metal-ram-size - bare-metal-heap-base` in
`codex/Emit/X86_64Chapter.codex`.

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

Defined in `codex/Core/PhaseAllocator.codex`. The compiler runs in
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
as `heap-marks` in the compile pipeline (`codex/opening.codex`).

## Compilation Phase Map

The compiler runs in phases. Each phase allocates a **deck** (durable
output) and a **bivy** (scratch). At phase end, `phase-compact`
restores R10 to the deck-pos, reclaiming all bivy scratch. Deck data
persists as the base for subsequent phases.

All phase work runs inside `deck-record(...)`, so R10 points at the
deck during phase execution. Bivy usage is near-zero (only the 16-byte
`PhaseStart` record per phase).

### Phase Deck Layout (selfhost, ~1.17 MB source)

Deck heights are computed from source length (S bytes) using survey
multipliers in `codex/opening.codex`. The pipeline has 6 TEXT-mode
frontend phases plus emit. CDX mode adds RESOLVE and LIFT between
LOWER and EMIT (CL 2429 split the frontend so TEXT skips those).
ConstructedTy resolution and lambda lifting were moved out of the
emitter into their own phases (CLs 2135, 2169), each with an
independent `phase-compact` cycle so bivy scratch is reclaimed
between passes.

Measured deck and bivy usage (CL 2454, TEXT-mode selfhost,
S = 1,174,861 bytes):

| Phase | Deck Usage | Bivy HWM | Survey |
|-------|-----------|----------|--------|
| LEX | 13.2 MB | 21.9 MB | S × 40 + 1 MB |
| PARSE | 5.4 MB | 25.4 MB | tokens × 265 + 1 MB |
| DESUGAR | 19.2 MB | 0.04 MB | S × 21 + 1 MB |
| SCOPE | 11.6 MB | 31.7 MB | S × 52 + 1 MB |
| CHECK | 66.1 MB | ~0 | S × 400 + 1 MB |
| LOWER | 92.5 MB | ~0 | S × 300 + 1 MB |
| RESOLVE | — | — | S × 200 + 1 MB (CDX only) |
| LIFT | — | — | S × 200 + 1 MB (CDX only) |
| EMIT | ~48 MB | per-func | defs × 64 KB + 16 MB |

Cumulative deck (TEXT): ~208 MB. Gap to stack: ~1,834 MB.

### CHECK Deck Overflow (CL 2574/2596)

The CHECK phase survey originally used `S × 95 + 1 MB`. This was
sufficient for normal compiler source (~2-3 type definitions per
file) but failed for plug source where PlugTypes.codex defines
~40 types in 368 lines (~15x normal type density). The deck
overflowed silently (CDX9002 was a warning), corrupting heap data.
Subsequent type-checker reads hit corrupted `ParamEntry` records
containing non-canonical addresses, triggering GPF.

Three fixes: (1) `survey-headroom` (120%) now applies to CHECK,
(2) `survey-check-mul` raised from 95 → 200 → 400, (3) CDX9002
promoted from warning to error (halts cleanly on overflow).

The survey formula with headroom:
`deck_height = source_len × survey-check-mul × 120 / 100 + 1 MB`

At `survey-check-mul = 400`: a 76 KB plug source gets
`76339 × 480 + 1 MB ≈ 36.6 MB` for CHECK. This is sufficient
for type-dense source.

Prior measurement (CL 2169, S ≈ 1,158,497 bytes) for comparison:

| Phase | Deck (2169) | Deck (2454) | Bivy (2169) | Bivy (2454) |
|-------|------------|------------|------------|------------|
| LEX | 11.2 MB | 13.2 MB | 21.7 MB | 21.9 MB |
| PARSE | 5.3 MB | 5.4 MB | 25.1 MB | 25.4 MB |
| DESUGAR | 19.0 MB | 19.2 MB | 0.04 MB | 0.04 MB |
| SCOPE | 11.5 MB | 11.6 MB | 31.3 MB | 31.7 MB |
| CHECK | 65.9 MB | 66.1 MB | ~0 | ~0 |
| LOWER | 90.9 MB | 92.5 MB | 457 MB | ~0 |

LOWER bivy dropped from 457 MB to ~0 because the TEXT path (CL 2429)
runs LOWER on deck directly; the 457 MB bivy was the CDX path running
LOWER on bivy before RESOLVE copies to deck. All deck values track
within ~2 MB of the CL 2169 baseline — proportional to source growth
(1,174 KB vs 1,158 KB).

```
Heap
base     Phase decks (sealed, read-only)         Bivy
(R10) ──►┌──────────────────────────────────────┐  (reclaimed
0x600000  │  init-phase-allocator (mountain base)│   after each
          ├──────────────────────────────────────┤   phase)
          │  LEX deck                  13.2 MB   │
          │  Survey: S × 40 + 1 MB               │
          │  Tokens, offset table                │
          ├──────────────────────────────────────┤
          │  PARSE deck                 5.4 MB   │
          │  Survey: tokens × 265 + 1 MB         │
          │  AST nodes, chapter index, def list  │
          ├──────────────────────────────────────┤
          │  DESUGAR deck              19.2 MB   │
          │  Survey: S × 21 + 1 MB               │
          │  Desugared AST                       │
          ├──────────────────────────────────────┤
          │  SCOPE deck                11.6 MB   │
          │  Survey: S × 52 + 1 MB               │
          │  Name bindings, slug-mangled names   │
          ├──────────────────────────────────────┤
          │  CHECK deck                66.1 MB   │
          │  Survey: S × 95 + 1 MB               │
          │  Type environment, resolved types    │
          ├──────────────────────────────────────┤
          │  LOWER deck                92.5 MB   │
          │  Survey: S × 300 + 1 MB              │
          │  IR defs, IR expressions             │
          ├──────────────────────────────────────┤
          │  RESOLVE deck        (CDX only)      │
          │  Survey: S × 200 + 1 MB              │
          │  ConstructedTy → RecordTy/SumTy,     │
          │  sorted type bindings                │
          ├──────────────────────────────────────┤
          │  LIFT deck           (CDX only)      │
          │  Survey: S × 200 + 1 MB              │
          │  Lambda-lifted defs                  │
          ├──────────────────────────────────────┤
          │  EMIT deck                ~48 MB     │
          │  Survey: defs × 64 KB + 16 MB        │
          │  EmitWorkspace (code + data bufs)    │
          │  Accumulator lists (11 × 32K cap)    │
          │  User arities                        │
          │  Per-function CodegenState            │
          ├──────────────────────────────────────┤
          │  (bivy: per-function scratch)        │
          │  Reclaimed by __heap-save/restore    │
          │  in emit-all-defs loop               │
          └──────────────────────────────────────┘
                              ▲
                              │ gap (~1.8 GB for 2 GB RAM)
                              ▼
          ┌──────────────────────────────────────┐
0x80000000│  Stack top (grows downward)          │
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
2. init-emit-workspace          4.5 MB       Code buf (4 MB) + data buf (512 KB)
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

`accum-capacity` = 32768 (defined in `codex/Core/BuildSettings.codex`).

All accumulator lists are pre-allocated via `__list-with-capacity`.
`list-push` writes in-place with no allocation as long as the list
stays within capacity. The `accum-at-capacity` guard in
`codex/Emit/X86_64.codex` checks all 11 lists before each function.

### Emit Output Buffers

| Buffer | Size | Contents |
|--------|------|----------|
| code-buffer | 8 MB (`code-buffer-size`) | x86-64 machine code; written sequentially via `st-append-code` |
| data-buffer | 512 KB (`data-buffer-size`) | String literals, CCE tables, static data |

Current selfhost binary: ~2.1 MB code, ~100 KB data. The 4 MB code
buffer has ~1.9 MB headroom.

## Emit Allocator

Defined in `codex/Emit/EmitAllocator.codex`. The code generator needs
two large contiguous buffers for the output binary:

- **code-buffer**: Machine code (x86-64 instructions). Capacity set by
  `code-buffer-size` in `codex/Core/BuildSettings.codex` (currently 4 MB).
- **data-buffer**: Static data (string literals, CCE tables). Capacity
  set by `data-buffer-size` in `codex/Core/BuildSettings.codex`
  (currently 512 KB).

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
`bare-metal-stack-top` (= ram-size, 2 GB). The heap grows upward via
register R10; the stack grows downward via RSP. See the Register
Convention table above for the full register map.

### Collision Detection

Every function prologue (`emit-prologue` in `codex/Emit/X86_64.codex`)
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

### Heap High-Water Mark

`heap-hwm-addr` stores the highest value R10 has reached. Updated by
`emit-update-heap-hwm` after each compilation phase. Reported over the
control serial channel as `HEAP:<value>` at the end of each compile run.

## Page Tables

### Trampoline (Boot)

The multiboot trampoline (`codex/Emit/X86_64IO.codex`) contains
hardcoded 32-bit machine code that runs before long mode is active.
It identity-maps 4 GB of physical memory using 2 MB pages:

- First PD (at 0x3000): 512 entries mapping 0x00000000–0x3FFFFFFF (1 GB)
- Three PDs (at 0x10000–0x12000): 1536 entries mapping
  0x40000000–0xFFFFFFFF (3 GB)

This mapping is temporary. It exists only long enough for the 64-bit
`__start` code to run and install the runtime page tables.

### Runtime

`emit-build-process-page-tables` in `codex/Emit/X86_64Boot.codex`
builds identity-mapping page tables sized to `bare-metal-ram-size`:

- PML4 at pml4-addr (one entry pointing to PDPT)
- PDPT at pml4-addr + 4096 (bare-metal-pd-count entries, one per GB)
- PD pages at pml4-addr + 8192 onward (512 entries each, 2 MB pages)

The runtime tables replace the trampoline tables via `mov cr3, rax`
during `emit-process-setup`. After this point, only memory up to
`bare-metal-ram-size` is mapped. Accessing addresses above this will
page-fault.

## Known Platform Constraints

### 4 GB Barrier

Physical addresses 0xC0000000–0xFFFFFFFF (the top 1 GB of the 32-bit
address space) are reserved for PCI MMIO on x86 platforms. Both
codex-vm and QEMU respect this: `-m 4096` provides 4 GB of RAM, but
physical addresses in the MMIO window are not usable as RAM. RAM above
4 GB is relocated to physical addresses starting at 0x100000000.

Codex does not currently support non-contiguous physical memory or
addresses above 4 GB. The practical ceiling for `bare-metal-ram-size`
is approximately 3 GB (0xC0000000). Setting it to 2 GB (0x80000000)
avoids the issue entirely with ample margin.

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
