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
0x00008000 (32 KB)   24 KB      Runtime page tables (PML4 + PDPT + 4 PDs at
                                  3 GB: (2 + bare-metal-total-pd-count) pages)
0x00015000 (84 KB)    32 KB     IST stacks (ist-stacks-base, 16 x 2048)
0x0001D000 (118 KB)   12 KB     Free hole -- xhci-diag lives at its head
0x00020000 (128 KB)  256 KB     AP idle stacks (ap-stacks-base, 16 x 16 KB)
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
| 36240 | **devint-count-addr** | 8 | Interrupts taken on a vector that is neither the PIT's (32) nor the local timer's (48). `__interrupt_common` answers those two specially and returns from every other vector, so without this cell a delivered device interrupt and a dropped one are indistinguishable from inside the guest. Incremented with a locked add; the timer vectors branch away before reaching it, so the periodic tick cannot appear here |
| 36248 | **devint-last-vec-addr** | 8 | The vector of the most recent such interrupt. A test programs a line, then demands that line answered rather than merely that something arrived. Read with 36240 by `codex/test/hpet-interrupt.codex` |
| 36256 | **ap-id-next-addr** | 8 | The next core id to hand out. An application processor starts in real mode knowing nothing, with no id in any register, so the last act of its trampoline is a locked exchange-add here. The boot processor seeds it with 1 before the start-up IPI and keeps 0 for itself. A dense counter rather than the LAPIC id: the value indexes four arrays of `smp-max-cores` entries, and a LAPIC id is an identifier, not an index |
| 36264 | **net-driver-cb** | 56 | Which NIC the network seam is bound to, and its six addresses: card selector at +0 (0 = NE2000, 1 = e1000), then mmio, rx-ring, rx-bufs, ctrl-blk, tx-ring, tx-bufs at +8 through +48. Written once by `net-driver-bind-e1000` (`codex/os/net/NetDriver.codex`) with the selector LAST, so a half-written block is never live. Zero until something binds, which is why a guest that never probes PCI keeps serving off the NE2000. The seam takes no device argument, and a module-level record binding is a recipe rather than a cell that allocates again on every reference, so this is the only place the bound card can live |
| 36320 | **guard-page-base-addr** | 8 | The demand-paging guard page, published rather than recomputed: `emit-demand-unmap` (`codex/compiler/Emit/X86_64Boot.codex`) derives it once from the reported RAM size and stores it here, and `build` (`Core/PhaseAllocator.codex`) reads it to decide whether a deck reservation would leap the page. Two places deriving the same geometry is how they drift apart. **This row was missing from this table until 2026-08-14**, which is exactly the failure the warning below describes: the chapter that claims the cell says in its own prose that 36320 is the next free scalar above `net-driver-cb`, so a reader who trusted this table alone would have taken a cell the compiler already owns |
| 36328 | **net-driver-poll-cell** | 8 | Empty receive polls per NetIO tick, measured once by `net-driver-calibrate` (`codex/os/net/NetDriver.codex`) at bring-up and read by `net-io-tick-interval`. Zero until something brings a driver up, and a value below the floor reads as the 100000 fallback NetIO shipped until 2026-08-14, so a guest that never probes keeps the numbers it was tuned with. It exists because the cost of one poll belongs to the DRIVER: one million empty polls cost 15.52 s on the NE2000 model and 0.029 s on the e1000, which reads a descriptor from RAM where the NE2000 takes a VM exit, and every retransmit bound in `NetworkStack` is a count of ticks |

**Do not claim a cell in this band without grepping `tools/codex-vm.c` AND
`apps/works/**` AND `codex/compiler/Emit/**` AND `codex/foreword/**` first,
and prefer a hole whose neighbours are bounded by something the layout
already defends.** The host alone is half the question. All four are load
bearing: the host claims 36152, 36160 and 36168; `apps/works` claims
`xhci-diag`, `msc-cells` and `ptr-cells`; `Emit` claims the eight runtime
cells; and the foreword READS `ptr-cells` at 36736 without claiming it, so a
grep that skips it finds no writer and concludes the cell is free. Every runtime cell here carried a prose block naming
only `codex-vm.c`, all of them grepped exactly that, and the app-level
`xhci-diag` block sat on top of eight of them from 36200 to 36263 plus
`net-driver-cb` above that. Measured 2026-07-29 under OVMF and again
2026-07-30 under codex-vm: a USB bring-up wrote the diag magic into 36200,
the PCI vendor id into 36208, CAPLENGTH into 36216, xECP into 36232 and
maxports into 36256. `xhci-diag` now lives at 118784 (main 12283) and this
band is its own again.

**And the region a relocated block lands in has stack arrays in it, which is
the other half nobody greps.** `ist-stacks-base` is 86016 with
`smp-max-cores * ist-stack-size` = 32768 bytes above it, and
`ap-stacks-base` is **131072**, sixteen 16 KB idle stacks running to 393216.
So 0x20000 is not free space, it is core 0's idle stack, and the only hole
between the two arrays is 118784-131071. A single-core payload cannot tell
the difference: no application processor boots, nothing writes the idle
stacks, and a block placed on top of them reads back perfectly. Check the
stacks as well as the tables and the cells.

36152 is a permanent booby trap (a legacy codex-vm output-ring write
position), and 36160 / 36168 are codex-vm's blit cells -- the host writes
them (`BLIT_ADDR_CELL` / `BLIT_LEN_CELL`; the host does not define a third
at 36176). 36200 was the first free slot above them, and the band has
since grown upward through 36208 (uefi-systab), 36216 (ap-preempt-count),
36224 (spawn-affinity), 36232 (fs-elevated), 36240 / 36248 (the
device-interrupt evidence above) and 36256 (the AP core-id counter).

**The band is nearly out of room, and the ceiling is the PDPT at 36864.**
Above `net-driver-cb` sit `msc-cells` at 36480-36587 (`GopUsbMsc`) and
`ptr-cells` at 36736-36751 (the mouse mailbox, read by
`codex/foreword/ui/InputSource.codex`). What is left is 36588-36735 and
36752-36863. `kd-cell` at 37000 (`build/boot/diag/KbdDiagProbe.codex`) is
already PAST the ceiling, inside the PDPT page: harmless for a UEFI-booted
diag payload on firmware's own tables, not harmless for a bare-metal one.

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
System V or Windows ABI -- Codex owns the entire machine.

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
| **R8, R9** | Unused | -- | Not allocated by the register allocator |

**"Codex owns the entire machine" stops being true the moment a helper
calls firmware, and that boundary has produced the same bug three times.**
A UEFI application runs under the Microsoft x64 convention, where
RAX, RCX, RDX, R8, R9, R10 and R11 are volatile: firmware may destroy any
of them and does. Four of those (R10, RCX, RDX, R11) are load-bearing
here, and R10 is the bump allocator itself.

So any emitted sequence that calls through a UEFI protocol pointer must
save what this table calls callee-saved, plus R10, around that call. Two
of the three failures were R10 (the guest entered `opening` with a
firmware pointer in it and tripped the heap guard in its own prologue,
which then reported OUT OF MEMORY with the heap untouched); the third was
`uefi-call-conout` doing `mov rbx, rcx` with no `push rbx`, which handed
every caller of the five UEFI console builtins a corrupted RBX. In
`uefi-con-put-text` that was the live `Text`, and it came back holding the
ConOut pointer.

The symptom is never local to the cause. A destroyed register surfaces as
a wrong pointer dereferenced somewhere else entirely, so the register dump
is the instrument: a "length" of `0x56575441e5894855` is the byte pattern
`55 48 89 e5 41 54 57 56`, a function prologue, which says a load read
code and the pointer was garbage.

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

## The runtime list header, and why hand-rolled builders corrupt silently

Every runtime list is laid out `[capacity][count][slots...]`, and **the list
pointer points at COUNT**, so the capacity sits at `[list-8]`. The inlined
`list-at` reads the **SIGN** of `[list-8]`: negative means the grown/indirect
form, whose elements live behind the pointer at `[list+8]`.

**Any hand-rolled list builder in emitted code MUST write that header.**
`__list-with-capacity`, `__linked_list_to_list` and `__list_concat_many` are
the references to copy. A builder that writes slots and a count but no capacity
produces a list that reads correctly everywhere except through the one path
that consults the sign, so the corruption surfaces far from its cause.

**It is invisible on a fresh VM and deterministic in the REPL, by design.**
Zeros in never-touched memory make a missing capacity word look like a valid
small positive capacity. The REPL loop's between-units 0xCD poison sweep is
what turns the same defect into a reliable crash -- that sweep exists to
convert this class from luck into a repro, so a bug that only appears in a
batch session is evidence about the header before it is evidence about the
batch.

**A read-only instrument that allocates is a bug class here.** There is no
GC, and walks run inside `deck-record`, so a diagnostic that builds a list,
sorts, or formats a string is spending the deck it is standing on -- and it
reports a healthy number right up until the phase it is measuring runs short.
The diagnosis is one number: **print R10 across any suspect region.** R10
climbing over a walk that is supposed only to read is the whole finding, and
it costs nothing to look before writing the instrument a second time.

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
(`__heap-advance 0`) -- it exists as a semantic marker.

Deck regions survive phase compaction because `phase-compact` rewinds
R10 to `deck-pos` -- everything below that address (the deck) is
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

### After a compact, the deck and the bivy hand out the same bytes

Step 4 sets R10 to deck-pos, so on the far side of a compact the bivy
frontier and `deck-pos-addr` are **equal**. Until something reserves a new
deck, the next bivy allocation and the next `deck-record` are handed the
same address, and whichever runs second overwrites the first.

Measured 2026-07-28 in `compile-to-cdx-with-exit-mode`, between the
frontend's compact and `emit-build`'s reservation: deck-pos `0x0ceb0acf`
against a bivy frontier of `0x0ceb0adf`, with a live empty list at
`0x0ceb0ad7`. Raising one diagnostic there ran a deck extent that wrote a
91-character message across it; `__list_concat_many` then read CCE text as
a list length and marched R10 off the top of RAM. Nothing was miscompiled
-- the crash site was byte-identical to a working build.

The trap is that **a diagnostic is deck-bound by design**, so "just warn
here" is a deck allocation wherever you write it. Code added to a
post-compact window must either allocate nothing, or run after a new deck
is reserved. `phase-compact`'s own contract makes this reachable; it is
not a defect in the allocator.

The corollary is worth stating separately, because it is latent in
shipping code: **a bivy value held live across a `deck-record` in that
window is already exposed.** `proofs` in `compile-to-cdx-with-exit-mode`
is one, and it survives only because nothing else allocates there.

The same collision exists before a run's FIRST `build`, where `deck-pos`
still holds its power-on value below everything bump-allocated. Reserve
with `init-phase-allocator` then `build` before any deck-bound work;
`init-phase-allocator` alone page-faults, because the deck and the bivy
then grow from one address.

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
space, not memory -- physical consumption is what a phase actually
writes. Floors are flat rather than derived; see "Why the floors are flat"
below.
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

Measured 2026-07-21 on a **2.81 MB** selfhost source with
`build/compile.ps1 -Measure`, which prints one `DECK-<n>:phase=<NAME> ...`
line per phase, post CL 10026 (the cite-fallback allocation fix) and the
write-path guards (SCOPE/CHECK/LOWER floors up by their 8 MB guard bands).
Re-measure with that switch; do not quote this table.

**What the SCOPE deck actually holds** (deck-pos probes at each phase
step, 2026-07-21): `scope-adefs-ll` 0.3 MB, the cite bags 3.0 MB, and
`resolve-chapter` 53.3 MB -- resolve-expr's pattern path wraps its
per-pattern-variable results in `deck-record`, and the wrapper makes
every scope/seen skiplist insert deck-resident. That 53 MB is per-def
scratch retained to the phase compact; if SCOPE's floor ever tightens,
moving those PatResult wrappers off the deck is the lever.

| Phase | Floor | Used (2026-07-21) | x source | Headroom | Pattern |
|-------|-------|------------------:|---------:|---------:|---------|
| LEX | 96 MB | 27.4 MB | 10.2x | 3.51x | standard deck+compact |
| PARSE scratch | 384 MB | 12.0 MB | 4.5x | 31.9x | reclaimed at keep boundary |
| PARSE keep | 384 MB | 11.5 MB | 4.3x | 33.3x | reservation-copy |
| DESUGAR | 72 MB | 41.0 MB | 15.3x | 1.76x | reclaimed at frontend keep boundary |
| SCOPE | 104 MB | 56.4 MB | 21.0x | 1.85x | standard |
| CHECK | 648 MB | 200.2 MB | 74.6x | 3.24x | standard |
| CHECK keep | 96 MB | not reported | -- | -- | reservation-copy |
| Frontend keep | 192 MB | not reported | -- | -- | reservation-copy |
| LOWER | 328 MB | 158.6 MB | 58.9x | 2.07x | reservation-copy |
| RESOLVE | 200 MB (CDX only) | 19.4 MB | 7.2x | 10.3x | standard |
| LIFT | 104 MB (CDX only) | 37.0 MB | 13.8x | 2.8x | standard |
| EMIT | per-func | -- | -- | -- | streaming (CL 3793) |

**PARSE keep is 11.1 MB, and it reads as small because `copy-sx-text` carries
the `address-of t < b` guard its siblings `copy-sx-token` and `copy-sx-span`
have.** Without it that walk rematerializes every text unconditionally instead
of sharing anything below the reservation base, which costs 254 MB against
11.6 MB on the same input. A survivor-copy that expands 21x over the scratch
it reads is never plausible; the tell is a ratio that GROWS with input
size (0.09x on a 2.9 KB source, 21x on this one), which is duplication of
something shared, not structure.

**LOWER was the tightest deck at 1.20x and tightening with every source
byte, and the reason was a bug, not the workload.** 45 per cent of the
LOWER deck was `slug-has-suffix` allocating a substring per assignment
scanned in the bare-name cite fallback -- per citation whose composed key
misses, per slug transition, in every defs walk, and LOWER retains its
walk because `lower-chapter` is deck-wrapped (CL 2968). An intra-compiler
`cites Codex chapter X` always takes that fallback (the concat prefixes
chapters by directory, so `Codex--X` never matches), and 25 such cites
stood in the unit at ~5.4 MB each. Fixed in CL 10026 (in-place compare):
LOWER fell 266.7 to 158.1 MB and its cost is not superlinear in
citations. The tight rows are now DESUGAR (1.76x MEASURE, ~1.5x in CDX
mode, where it is the binding phase: `-Decks 65` refuses naming DESUGAR
and 70 compiles) and SCOPE (1.71x). Those are the rows to watch.

**Name the deck before quoting its ratio: PARSE has two and they differ by
20x.** The 95x deck is **PARSE keep**, the reservation the copy walk fills.
The scratch it is copied out of is **4.6x**, sitting under a 384 MB floor it
uses 3 per cent of. MEASURE emits phase names now; a positional list cannot
tell the two apart, and a floor sized from the wrong one is how that scratch
floor reached 384 MB.

**An under-reserved floor does not raise CDX9002** -- it dies in a `#GP` with
no diagnostic, so the phase closest to its floor is the one most worth a
pre-flight bound, whether or not it is the phase drawing attention.

**RESOLVE and LIFT** were
measured 2026-07-21 by an in-loop `__deck-pos` probe rather than through the
metrics list, which is a cheaper instrument than fixing the list-push problem
below and answers the question the floors actually pose. RESOLVE decomposes as
`build-type-def-map` 161,112 bytes, `sort-bindings` 180,504, and
`rewrite-ir-defs` 20,029,096 -- so 98.3 per cent of the phase is one walk, and
that is where its guard went. LIFT is `lift-lambdas` and nothing else.

The same probe settled something larger: **`__deck-pos` does not move inside a
phase-wide `deck-record` extent.** `__deck-enter` copies the cell into R10 and
only the outermost `__deck-exit` writes it back, so a guard loop inside such an
extent reads the deck's base on every iteration. The probe printed one constant
through 19.1 MB of writing. Guards in that position must read R10
(`deck-bound-short-of`); guards outside one, or in a phase that writes through
many small extents like SCOPE, read the cell (`deck-short-of`).

Settled 2026-07-21 for every guard in the compiler. Inside their extents and
reading R10: RESOLVE, LIFT, LOWER, and the PARSE-KEEP copy. Outside, reading the
cell: SCOPE (many small extents) and CHECK. **CHECK is the one you cannot infer
from the phase's shape** -- `check-chapter` issues a bare `__deck-exit` three
lines before `check-all-defs` and a `__deck-enter` after it, so the walk between
them is bivy-bound. Read the code for that pair before choosing a predicate.
Guessing from the phase name is wrong for exactly that one, and wrong there is a
compiler that type-checks one definition and emits nothing.

**Every phase that reserves a deck now stops on the write path.** LEX, PARSE
scratch, PARSE-KEEP, DESUGAR, the frontend keep copy, SCOPE, CHECK, LOWER,
RESOLVE and LIFT. Two shapes recur and both were learned by shipping the
wrong one first: a phase whose deck is written by SEVERAL walks needs all of
them guarded (the PARSE scratch takes three, the frontend keep copy four
lists plus a skip on every remaining field), and a saturating walk must
return an EMPTY result rather than a truncated or shared one, because the
wind-down that assembles a partial result allocates in proportion to what it
collected, onto the deck that just refused to grow.

**How to tell a guard that holds from a guard that does nothing.** Key the
phase's report on the post-hoc `ov` flag ALONE and re-run the starved compile:
if `CDX9002` still fires, the deck overflowed and the guard did not hold,
however cleanly it reported. Keyed the shipping way (`ov | sat`) the two print
the identical line, and so does an *unguarded* run at a mildly starved floor,
because the post-hoc check catches a survivable overrun. Three guards passed
that reading and held nothing. Starve until the unguarded run actually crashes
before trusting a negative control: above that floor the overrun survives, and
below it writers outside the guarded walk crash anyway, so only the band
between them tells you anything.

**A keep deck's usage is only visible where it is pushed into the metrics
list.** PARSE-KEEP is; DESUGAR-KEEP, CHECK-KEEP, the frontend keep, RESOLVE
and LIFT are not, so their rows read "not reported" rather than a number
nobody measured. Pushing them in is not a one-line change: `list-push`
writes in place under capacity and returns the same list, so pushing onto a
list the caller still holds corrupts the entries after it -- tried
2026-07-18, and it silently zeroed every phase after DESUGAR. Copy first.

A phase that exceeds its floor halts with CDX9002 (DeckOverflow, now
"deck floor exceeded") -- retained as a hard guard, though the selfhost
runs at 2-6x headroom under every floor.

### Why the floors are flat, not derived

Deriving each phase deck from a formula over the source length
(`survey-*-mul`) was tried and abandoned. The multipliers could not be sized
honestly: they were non-monotonic (20 worked, 25 did not, 40 silently
miscompiled a grown self-compile), and an under-reservation corrupted the
heap rather than raising a diagnostic.

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

### The per-function reclaim in emit-all-defs, and what it is worth

`emit-all-defs` brackets every definition in `__heap-save`/`__heap-restore`
(three such sites exist in the emitter, not one). It was an open question
whether that within-phase reclaim should give way to phase-boundary
discipline. **Measured 2026-07-18, and the answer is that it does not move
peak memory at all.** A compiler built with the restore removed
self-compiles fine -- 10.4 s, 2.42 MB output, no `CDX9002`, no fault -- and
the two binaries have the *same* minimum RAM to the resolution tested:

| `-mem` | with reclaim | without |
|--------|--------------|---------|
| 1536 | OK | OK |
| 1472 | OK | OK |
| 1408 | FAIL | FAIL |
| 1280 | FAIL | FAIL |

Peak is set by the phase decks -- CHECK 190 MB, PARSE-KEEP 241 MB, LOWER
253 MB -- not by per-function emit scratch, so removing the reclaim is
invisible against them.

**Keep it anyway, and know why.** It is a working-set optimisation that
costs nothing, and the `accum-at-capacity` guard is written against it: a
push past capacity reallocates into exactly the per-function bivy this loop
reclaims, which is what makes an over-capacity accumulator point at freed
memory. Code depends on the reclaim happening even though peak memory does
not. What the measurement settles is only that this bracketing is not what
the phase-boundary work is about; that work is the precise
escape roots for CHECK and LOWER.

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
-- re-measure it there; do not carry this number forward).

All accumulator lists are pre-allocated via `__list-with-capacity`.
`list-push` writes in-place with no allocation as long as the list
stays within capacity. The `accum-at-capacity` guard in
`codex/compiler/Emit/X86_64.codex` checks all 11 lists before each function
and halts with **CDX9002-band `CDX9005` (AccumOverflow)**.

**Exceeding capacity corrupts the table; it does not merely cost heap.**
A push past capacity doubles and reallocates like any other, and the new
backing lands in the per-function bivy that `emit-all-defs` reclaims with
`__heap-restore` -- so the accumulator is left pointing at reclaimed memory.
Measured, not assumed: built with `accum-capacity` at 16, the compiler
emits a factorial whose call-patch target is the empty string, and the
only complaint is `CDX2040: Unresolved call to ''`. This is why the
accumulators are sized once on the deck and why the guard exists.

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
`spawn-pool-base + N * spawn-slot-region-size` -- 1 GB base, 32 MB per
slot, 16 slots spanning [1 GB, 1.5 GB) of demand-paged address space.
`__spawn_pool_carve` (X86_64ProcessHelpers) reads the claimed slot
index from R12 and the region size from RDX, returning the heap base
in RDI and the stack top in RSI -- five instructions, no memory cell,
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
takes a caller-chosen heap size, bounds-checked at the call site --
a request that cannot fit inside one slot region (heap + 1 MB stack
> 32 MB) is refused with -1, never silently overlapped. The parent
pre-touches the child's stack pages before the child first runs
(`emit-spawn-stack-pretouch`) because a stack must never point into
a not-present page. Spawn-capable programs need the demand-range top
above 1.5 GB (any `-mem` from ~1664 MB; the default is 3072).

**Do not carve a spawn region from the spawner's own R10 frontier.** That
is correct only for proc 0, which owns the whole heap; a spawned child
owns a fixed region, so a child spawning a grandchild hands out memory
overlapping its own stack. The slot table is the allocator -- use it.

**Guest cell 36152 must never be claimed for metadata** -- legacy codex-vm
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

The check is a software compare on every function entry, and it watches
one direction only: RSP falling below R10.

### The Guard Page (2026-08-04)

The opposite direction has its own protection. One 2 MB page is left
unmapped at index `ram-pages - demand-stack-reserve-pages - 1`, directly
below the boot stack's 64 MB reserve -- at the shipping 3040 MB reported
size that is index 1487, `[2974 MB, 2976 MB)`. Any heap bump that lands
in it faults on first touch, whichever of the 58 inline `add r10` sites
did it, including sites added later. The cost on the allocation path is
nothing: no compare, no branch, no load.

`emit-demand-unmap` clears its PDE at boot (unconditionally, so it is
gone whether or not it falls inside the demand range), and
`emit-pagefault-handler` tests the faulting page against it BEFORE the
lo/hi range tests -- after them, the 3 GB case would already have been
routed to the exception dump. A hit routes to `__out_of_memory`, which
reloads RSP from `ram-size-addr` before printing and therefore survives
having had its stack written over.

**Two honest limits.** A single allocation LARGER than 2 MB steps
clean over the hole, demonstrated with a probe parked above the page.
`build` reserves a phase deck with one `__heap-advance` of the entire
deck height, so a reservation is exactly that shape -- but the
compiler's reservations are taken from a low frontier and land below
the page, and `build` carries its own ceiling test as belt-and-braces
regardless (`deck-reservation-guard`, `Core/PhaseAllocator.codex`).

**The guard page does catch the whole-compiler `-IrCce` overrun.**
Ablated against `seed/Codex.cdx#586` (crash) and `#587` (OUT OF MEMORY,
no ceiling test present), the page alone is what fixes it. Measuring this
takes care: the SUT's boot code is emitted by the SEED, so an emitter
change reaches stage1 and not the SUT, and a run against the SUT is
measuring a binary with no guard page in it. And a STACK that
grows down into it cannot take a #PF at all (the CPU cannot deliver the
frame onto the faulting stack), so that arm arrives as a double fault on
IST1 and an `!EXC` dump rather than an OUT OF MEMORY line.

`build/guard-page-test.ps1` is the runner. `build/build.ps1` cannot see
this: the compiler peaks around 1245 MB against a guard at 2974 MB, so
the gate is green whether the page exists or not.

---

## Vector / SIMD Register Allocation

Vector registers (XMM0-XMM15 on x86-64) are a separate allocation pool
from integer registers. The two domains never compete for the same
physical register.

| Role | Registers | Notes |
|------|-----------|-------|
| Vector temps | XMM0-XMM7 | Rotation scheme, like integer `alloc-temp` |
| Vector locals | XMM8-XMM15 | Callee-saved in our convention |
| Scalar float | XMM0/XMM1 | Pre-SIMD usage for `Real` arithmetic |

SSE2 packed instructions use 128-bit XMM registers. `Vector 2 Real`
(2 × f64 = 128 bits) fills one XMM register. `Vector 4 (Real approximate)`
(4 × f32 = 128 bits) also fits in one XMM.

### Alignment

Vector values carry natural alignment: `N * sizeof(T)` rounded up to
the next power of two, minimum 16 bytes. The bump allocator (`__alloc`)
rounds R10 up before allocation. Stack spill slots for vectors must also
respect alignment -- the prologue already aligns RSP to 16 bytes (SSE2
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

`emit-fill-device-pd` maps `[3 GB, 4 GB)` identity -- present, read-write,
NX (nothing up there is code). It costs one 4 KB page directory and
nothing at runtime. `bare-metal-device-pd-index`, `-device-page-start`,
`-device-page-end` and `-total-pd-count` are in `X86_64State.codex`.

This is where everything x86 puts above RAM lives: the LAPIC at
0xFEE00000, the IOAPIC at 0xFEC00000, the HPET at 0xFED00000, and every
PCI BAR codex-vm advertises. The tables once stopped at
`bare-metal-ram-size` with all of this unmapped, which is why codex-vm's
device model reaches almost everything through port I/O rather than MMIO.
**That ceiling is gone.** Reach for MMIO first when adding a device.

The `#PF` handler and `emit-demand-unmap` both address the PDs as one
flat array of 8-byte entries based at 0xA000; the device PD extends that
array contiguously (page 1536's entry lands at 0xD000, which is exactly
where PD 3 begins), so their arithmetic is unchanged. The demand range
only ever spans pages 3..1024, so it never reaches the device PD.

The consequence to know: **a stray pointer above 3 GB reaches the
bus instead of faulting.** That is what it would do on real hardware,
where those addresses are decoded by devices rather than by RAM -- but it
does mean the page tables do not catch a wild high pointer for you.

### Demand Paging (2026-07-07, hardened 2026-07-06 val CLs 7207-7210)

Before the CR3 switch, boot clears the PD entries covering heap pages
[6 MB, top) -- the demand range. The top is computed from the actual
RAM size (GPA 0xFE8): `min(1024, ram_pages - 32)` in 2 MB pages, so
the top 64 MB of RAM always stays present for the boot stack and any
`-mem` from ~128 MB boots. At 3 GB the top equals the 2 GB cap and
the stack/GOP region [2 GB, 3 GB) is present from boot.

The first touch of each 2 MB page raises #PF (vector 14). The
vector-14 stub preserves the CPU error code; the handler grows the
heap only for not-present faults (error-code P=0) inside the range --
it writes the identity PDE (`(CR2 & ~0x1FFFFF) | 0x83 | NX`),
increments the touched-page counter (cell 30688, the honest physical
metric -- the R10 HWM reports floor reservations), invlpg, iretq.
Protection or reserved-bit faults (P=1), out-of-range faults, and
every other vector fall through to the exception dump. NX matters:
everything above the code boundary is non-executable in the boot
mapping, and demand pages match it.

Invariant: a stack must never point into a not-present page -- the CPU
cannot deliver a #PF frame onto the faulting stack -- so spawn helpers
pre-touch every 2 MB page of the stacks they carve from the heap
(`emit-spawn-stack-pretouch`, unrolled at emit time from
`proc-spawn-stack-size`). When the invariant is violated anyway, the
double fault is delivered on the TSS IST1 emergency stack (TSS at
0x13000, GDT at 0x12800, 2 KB stack below 0x14800) and produces the
standard `!EXC` dump instead of a silent triple fault. BSP only --
an AP double fault is still fatal (per-core TSS is future work).

## SMP Memory Model

When codex-vm runs with `-smp N` (N > 1), the guest boots with
multiple virtual processors. The core count is written to GPA 0xFF8
before boot; the boot code reads it to decide whether to send
INIT/SIPI to start application processors.

**Bring-up.** `emit-smp-init` (`X86_64Boot.codex`) copies a real-mode
trampoline into the page at GPA 0x1000, seeds the core-id counter (cell
36256) with 1, publishes the stack table at GPA 0xF00, then writes the
LAPIC ICR: an INIT IPI, then two start-up IPIs, destination shorthand
"all excluding self", **start-up vector 1**. The ICR write is what starts
the cores. Each AP comes up in real mode at 0x1000, climbs to long mode,
takes a core id with a locked exchange-add on cell 36256, takes its stack
from the table by that index, loads the runtime GDT and IDT and its own
task register, adds one to the ready count (cell 4080) with a locked add,
and then goes to `__idle_dispatch` to look for work. The BSP spins on
that count -- on `pause`, not `hlt`: nothing sends the BSP an interrupt
when an AP checks in, so a halted BSP would never wake. The spin is fuel
capped, so a core that never answers costs a delay and not the boot.

**The start-up IPI's vector field is a page number, and it is the only
channel there is.** On silicon that field names the 4 KB page an AP
begins executing in, which caps the entry below 1 MB and requires a
real-mode trampoline. codex-vm
starts an AP at `vector<<12` in real mode with reset control registers and
nothing in RDI, and `ap-tramp-blob` is 177 bytes of 16-bit, 32-bit and
64-bit code that carries a core the rest of the way. Every SMP test
exercises it.

Three things in that blob are load-bearing and each was found by breaking
it. **Its GDT is ordered null, 64-bit code, data, 32-bit code**, so the
selectors a core still holds when it leaves mean the same things in the
runtime GDT it then loads: put the 32-bit descriptor at selector 8, as
reading order suggests, and the core runs until its first timer tick and
then general-protection-faults on the way back out, because selector 24 in
the runtime table is a TSS and a TSS is not a code segment. **CR4 must
carry OSFXSR**, or the first packed instruction in the first process the
core resumes is an invalid opcode. And **the AP entry must `lidt`**:
without it the first tick after `sti` dispatches through the
real-mode interrupt vector table. That last one presents as cores that
check in and then never claim a process, which looks nothing like a
missing IDT.

The core id is a dense counter and deliberately not the local APIC id. It
indexes the AP stack table, the per-core idle stacks, the per-core TSS
descriptors and the IST stacks, all arrays of `smp-max-cores` entries; a
LAPIC id is an identifier, not an index, and on a machine that numbers its
cores sparsely it would run off the end of every one of them. Nothing in
the tree needs a core's hardware identity, only a distinct small number,
so the trampoline reads no MMIO at all.

**Per-core TSS and emergency stacks.** A double fault is delivered on the
stack named by IST1 in the TSS the task register points at. The task
register is per-core and a TSS cannot be shared -- two cores would fight
over its busy bit and be handed the same emergency stack -- so the GDT
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
in always-present low memory -- 16 KB each at
`AP[i] stack = 0x20000 + i * 0x4000` ([0x20000, 0x60000), below the
EBDA) -- never in the demand-paged heap range, because an AP takes its
first interrupt on this stack and the CPU cannot deliver a fault
frame onto a not-present page. The guest writes these addresses to
the stack table at GPA 0xF00 before SIPI; codex-vm falls back to
`0xC0000000 - i * 0x10000` only for table entries left zero. Real
work on an AP runs on scheduler-provided stacks.

**Scheduling on an AP.** An application processor is not a special case.
It goes to **`__idle_dispatch`** -- the same routine the boot processor
goes to when it runs out of work -- walks the 16-slot process table,
claims a READY slot with a `LOCK CMPXCHG` on the state word
(READY → RUNNING), takes the time slice its priority is due, and resumes
it. From that instant the core is running a real process, on that
process's own stack, with that process's own R10.

Core 0 may claim slot 0; an AP may not. Slot 0 is the program the machine
booted and it owns the boot stack and the main heap.

**A core that parks must leave the process's stack first.** This is the
whole reason `__idle_dispatch` exists as a routine rather than a loop
inlined at each site. A core with no work is still standing on the stack
of the process it was last running -- and `process-wait` marks itself
BLOCKED, so the wake loop is about to mark it READY, another core will
claim it, and resume it *on that stack*. Two cores, one stack; the parked
core's next interrupt pushes a frame straight through the other core's
process. So the parked core switches RSP to its own idle stack **before**
it scans. `process-exit`, `process-wait` and the channel block path all
end in `jmp __idle_dispatch` for exactly this reason.

**Per-core identity: a core asks the process it is standing in.** There is
no MSR, no LAPIC read and no GS base involved. `proc-core-offset` (process
entry offset 8) records the core that claimed the slot, stamped by
whichever core won the CMPXCHG; an AP seeds its own id from the counter
its trampoline drew from at bring-up. A core recovers its identity by reading that field out of the
process it is currently running, and from the id it computes its idle
stack: `ap-stacks-base + (core + 1) * ap-stack-size`. AP idle stacks are
handed out from index 1, so region slot 0 was free and is the BSP's.

**Per-core heap: there isn't one, and none is needed.** `CoreHeap`
(`codex/os/sched/CoreHeap.codex`) is **effectively a model**: every function in
it (`compute-heap-layout`, `build-arenas`, the `arena-*` and `layout-*` family)
has no caller outside `codex/test/apps/core-heap-test.codex`, and no AP has ever
set R10 from it. Only the constant `single-core-heap-base` is consumed, by
`core-activate` in `OsScheduler.codex`, so "nothing calls it" is very slightly
too strong. It is not
needed on the critical path either: a spawned process carries its own
slot-indexed heap region *and its own R10* in its saved context, so a core
running one gets the right allocator by resuming it. Whether the
*compiler's* bivy should be split per core is a separate and open
question.

**Every core has a clock.** The PIT's IRQ reaches the boot processor
alone, so each AP arms its **own local APIC timer**
at bring-up (`emit-ap-timer-init`, `X86_64Boot.codex`): it enables its
LAPIC, programs the LVT timer periodic on **vector 48**, sets the initial
count, and only then raises IF. A process on an application processor is
preempted exactly as one on the BSP is.

Two clocks therefore arrive at `__interrupt_common`: **vector 32** (the
PIT, on the BSP) and **vector 48** (an AP's local timer). They run the
same scheduling path -- it was always per-core-safe, deriving the running
process from the interrupted RSP and claiming a replacement with a
CMPXCHG -- and differ only in **which chip is told the interrupt is over**:
the 8259 for the PIT, the local APIC for the LAPIC timer
(`emit-timer-eoi`). Send the wrong one and the raising chip believes the
interrupt is still in service and never delivers another, which reads as a
core that was preempted exactly once and then stopped.

The tick count is incremented with a **locked** add, because more than one
core increments it now; a plain load-add-store loses ticks.

Evidence lives at cell **36216** (`ap-preempt-count-addr`): every timer
interrupt taken on a core whose id is not zero bumps it, and the BSP's id
is always zero. `codex/test/smp-preempt.codex` reads it.

**Halting, affinity and work stealing are all built.** An idle core **halts**
(`st-append-code s15 hlt` in `__idle_dispatch`, `X86_64ProcessHelpers.codex`);
the timer lands on its idle stack and the handler drops ticks whose SP is in
that band, which is what makes halting safe. **Affinity** is real and on the
bare-metal path: `proc-affinity-offset` (process entry offset 16) is compared
against the core id in `__idle_dispatch`, `-1` meaning any core, set at spawn
and honoured on the yield path. **Work stealing** exists in the OS scheduler
model (`core-steal` / `core-longest-other`, `codex/os/sched/CoreState.codex`),
though the bare-metal dispatcher scans a shared process table rather than
per-core queues, so there it is not-applicable rather than missing. Tests:
`codex/test/smp-halt.codex`, `codex/test/smp-affinity.codex`.

**Proc 0 does not migrate.** The scheduler forbids it in three separate places.
`__idle_dispatch` starts each core's scan at its own id and wraps to 1, so an
application processor never reaches slot 0; and both preemption scans skip slot
0 outright when the claiming core is not the boot processor, each with its own
written account of the corruption that guard prevents. Slot 0 owns the boot
stack and the main heap. Its affinity field reads `0` to match: on the boot
processor the affinity test compares 0 against core 0 and passes, and on
every other core slot 0 is unreachable anyway.

`codex/test/smp-proc0-pinned.codex` pins it. It reads slot 0's core stamp after
a four-core run and requires it to still be the boot processor, with three
further readings establishing that the machine was genuinely busy while it was:
an application processor claimed and ran a process (cell 36200), one was
preempted (cell 36216), and the largest core stamp across the other fifteen
slots is above zero -- the same field, proven able to hold a value other than
the one slot 0 is required to hold. Without those three, zero is also the
initial value and the test would pass on one core.

**Atomics.** Six builtins: `atomic-load`, `atomic-store`,
`atomic-cas`, `atomic-add`, `atomic-exchange`, `memory-fence`.
x86-64 codegen: LOCK CMPXCHG, LOCK XADD, LOCK XCHG, MFENCE.

**IPI.** Inter-processor interrupts via LAPIC ICR writes. Used for
cross-core wake and TLB shootdown. Lock-free MPSC channels for
message passing between cores.

## Known Platform Constraints

### No exception record exists anywhere in the address map

No handler stores a faulting vector, RIP or RSP. There is no cell to read
"the last exception" from, and a debugger view that offers one is reading
something else: three dev-console views did exactly that for months by
reading `stdin-eof-settled-addr` and two scheduler cells, fixed in main
11344.

The only cells that ever hold a RIP are the watchdog's stall ring at
`ii-wd-ring-buf-addr`: four 32-byte slots of RIP, RSP and heap pointer,
written by the timer ISR when neither the heap pointer nor the saved RIP
moved since the last tick. That is a STALL sample, not a trap frame, and
anything reporting it must say so.

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
collision check (`cmp rsp, r10`) is the protection against the STACK
growing into the heap; it is tested on every function entry and it is
still the only thing watching that direction.

The other direction -- the HEAP frontier growing into the stack -- is
caught by the guard page (below), since 2026-08-04. The two are not
the same failure and the prologue check never saw the second one: it
compares only at function entry, and the 58 inline `add r10` bumps
that cross the boundary do so between calls.

## Codegen Quality vs C and the JITs

Function-body x86-64 instruction counts for the benchmarks in `bench/`
(build + compare with `bench/compare.ps1`). The Codex column is measured
2026-07-17 on the shipping seed C0B74DBE with the LIR selector live; the
C and JIT reference columns (cl.exe, the .NET JITs) were measured
2026-06-12 and do not move. The four primordial benches carry the full
reference set; the elaborate benches have no in-tree x86 C/JIT reference,
so only the Codex count is shown. Full optimization history and per-CL
breakdown: `docs/Designs/Done/Compiler/CodegenAnalysis.md`.

| Bench    | Codex | C /Od | C /O2 | C# JIT | F# JIT |
|----------|------:|------:|------:|-------:|-------:|
| fib      | 22    | 19    | 20    | 21     | 21     |
| fact     | 13    | 16    | 15    | 16     | 15     |
| gcd      | 10    | 18    | 14    | 11     | 9      |
| sum      | 7     | 20    | 23    | 9      | 4      |
| ack      | 23    | --    | --    | --     | --     |
| tak      | 37    | --    | --    | --     | --     |
| collatz  | 13*   | --    | --    | --     | --     |
| locals   | 18    | --    | --    | --     | --     |
| regright | 14    | --    | --    | --     | --     |

\* `collatz` moved when the bounded-division fix landed and the count above predates
it -- **re-measure before quoting.** Its `n` is an unbounded `Integer`, so
`n / 2` and `int-mod n 2` cannot take the one-instruction
shift/mask: those are correct only for a dividend proven non-negative,
and for any other they lower to `idiv`, which is what truncation
actually is. The binary grew 16 bytes; the other eight benches are
byte-identical. The shortcut is recoverable at the source rather than in
the emitter -- declaring `n : Integer between 1 and ...` proves the
bound and buys the shift back, which is the type system doing the job it
exists for.

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
named bindings. The LIR selector carries a Wimmer linear-scan allocator and is
live in the default pipeline. Measured 2026-07-19
(`docs/Designs/Done/Compiler/LIR.md`), it takes **all nine** `bench/codex`
functions and against the tree emitter is **neutral on seven and one instruction
ahead on `ack` and `collatz`**. It declines none of them.

**Beating the tree by more than a margin of one is not available, and that
was measured rather than assumed.** `docs/Designs/Done/Compiler/LIR.md`
section 12 is the closing note.
The short version is two independent negatives. The spills that remain are the
register file rather than the allocator -- one program (`bench/codex/regstress`)
under three register-file descriptors and one unchanged allocator spills 13, 6
and 0 slots at 2, 4 and 10 callee-saved registers, and its peak simultaneous
call-crossers is 6 against x86-64's pool of 4, so at least two values must live
in memory whatever the allocator does. And there are no live-range holes to
sharpen, because v1's LIR is a loop-free DAG with single-def-per-path vregs, so
every interval is contiguous by construction; holes appear only with v2 TCO
back-edges, where the one built and measured made `gcd` worse (158 to 159).
What stays open is coverage and verification, not quality: the prologue's
callee-saved pushes, stack guard and frame adjust are emitted outside the
LIR so no verifier sees them; `list-map` stopped lowering and the cause is
not established; and both verifiers' rejection paths run under no harness.

Two apparent gaps in the table above are **not** codegen gaps, and LIR.md is the
place that settles it: `fib`'s +2 over C /O2 is a source-shape difference, and
`tak`'s spill is genuinely required, because adding R15 to the pool is unsound
(`X86_64Builtins.codex` writes it unsaved at closure-call sites).

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
