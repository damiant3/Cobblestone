# Operator's Manual

This document covers the runtime memory model, allocator architecture,
build process, and known platform constraints for the Codex bare-metal
compiler.

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

### Register Convention

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

### Deck-Bound Mode (R10 Swap)

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

## Compilation Phase Map

The compiler runs in phases. Each phase allocates a **deck** (durable
output) and a **bivy** (scratch). At phase end, `phase-compact`
restores R10 to the deck-pos, reclaiming all bivy scratch. Deck data
persists as the base for subsequent phases.

All phase work runs inside `deck-record(...)`, so R10 points at the
deck during phase execution. Bivy usage is near-zero (only the 16-byte
`PhaseStart` record per phase).

### Phase Deck Layout (selfhost, ~1.15 MB source)

Deck heights are computed from source length (S bytes) using survey
multipliers in `codex/opening.codex`. Values below are for the
selfhost compiler (S ≈ 1,174,009 bytes, ~500 defs).

```
Heap
base     Phase decks (sealed, read-only)         Bivy
(R10) ──►┌──────────────────────────────────────┐  (reclaimed
0x600000  │  init-phase-allocator (mountain base)│   after each
          ├──────────────────────────────────────┤   phase)
          │  LEX deck                            │
          │  Survey: S × 10 + 1 MB ≈ 12.7 MB    │
          │  Tokens, offset table                │
          ├──────────────────────────────────────┤
          │  PARSE deck                          │
          │  Survey: S × 5 + 1 MB ≈ 6.9 MB      │
          │  AST nodes, chapter index, def list  │
          ├──────────────────────────────────────┤
          │  DESUGAR deck                        │
          │  Survey: S × 18 + 1 MB ≈ 22.1 MB    │
          │  Desugared AST                       │
          ├──────────────────────────────────────┤
          │  SCOPE deck                          │
          │  Survey: S × 35 + 1 MB ≈ 42.1 MB    │
          │  Name bindings, slug-mangled names   │
          ├──────────────────────────────────────┤
          │  CHECK deck                          │
          │  Survey: S × 75 + 1 MB ≈ 89.1 MB    │
          │  Type environment, resolved types    │
          ├──────────────────────────────────────┤
          │  LOWER deck                          │
          │  Survey: S × 300 + 1 MB ≈ 353.2 MB  │
          │  IR defs, IR expressions, lambda list│
          ├──────────────────────────────────────┤
          │  EMIT deck (CL 1563: deck-everything)│
          │  Survey: defs × 64 KB + 16 MB        │
          │  ≈ 48 MB for ~500 defs               │
          │  Contains:                           │
          │    Sorted TypeBindings (sort-by)     │
          │    EmitWorkspace (code + data bufs)  │
          │    Accumulator lists (11 × 32K cap)  │
          │    Rewritten IR (deck-record nodes)  │
          │    Lambda-lifted defs                │
          │    User arities                      │
          │    Per-function CodegenState (deck-   │
          │     record in emit-all-defs)         │
          ├──────────────────────────────────────┤
          │  (bivy: per-function scratch)        │
          │  Reclaimed by __heap-save/restore    │
          │  in emit-all-defs loop               │
          └──────────────────────────────────────┘
                              ▲
                              │ gap (~1.4 GB for 2 GB RAM)
                              ▼
          ┌──────────────────────────────────────┐
0x80000000│  Stack top (grows downward)          │
          │  ~1 MB typical usage for selfhost    │
          └──────────────────────────────────────┘
```

### Emit Phase Detail

After `emit-build` creates the emit deck and `__deck-enter` swaps R10
to it, the emit init allocates in this order (all on the deck):

```
Emit deck
──────────────────────────────────────────────────────────────
1. bare-metal-trampoline        ~100 bytes   Boot stub
2. init-emit-workspace          4.5 MB       Code buf (4 MB) + data buf (512 KB)
3. sort-type-bindings           ~10 KB       Sorted TypeBinding list (577+ entries)
4. CodegenState                 ~300 bytes   With 11 pre-allocated accumulator lists:
     fo-names, fo-offsets          2 × 256 KB   Function offset table
     cp-offsets, cp-targets        2 × 1 MB     Call patch table (4× capacity)
     fa-offsets, fa-targets        2 × 256 KB   Far-address patch table
     rf-poffsets, rf-roffsets      2 × 512 KB   Rodata fix-up table (2× capacity)
     da-poffsets, da-offsets       2 × 256 KB   Data-address patch table
     stack-overflow-checks         1 × 256 KB   Stack overflow check positions
                                ─────────
                                ~5 MB total accumulator backing buffers
5. rewrite-ir-defs              variable     IR with ConstructedTy resolved (deck-record)
6. lift-lambdas                 variable     Lambda-lifted IR defs
7. build-x86-arities            ~10 KB       Sorted arity table
8. emit-runtime-helpers          (writes to code buffer, no deck alloc)
──────────────────────────────────────────────────────────────
   __deck-exit (R10 back to bivy)

9. emit-all-defs loop:
     Per function:
       __heap-save h
       emit-function → writes machine code to code buffer,
                        static data to data buffer,
                        pushes to accumulator lists (in-place, no alloc)
       __heap-restore h (reclaims per-function scratch: locals, temps, spills)
       deck-record(codegen-carry-forward) → new CodegenState on deck (~300 bytes)

10. x86-64-finalize-* → patches, ELF/CDX header, output
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
| code-buffer | 4 MB (`code-buffer-size`) | x86-64 machine code; written sequentially via `st-append-code` |
| data-buffer | 512 KB (`data-buffer-size`) | String literals, CCE tables, static data |

Current selfhost binary: ~2.1 MB code, ~100 KB data. The 4 MB code
buffer has ~1.9 MB headroom.

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
the final CDX or ELF binary by the format writers (`CdxWriter.codex`,
`ElfWriter.codex`).

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

## Build Process

The build script (`codex.build/build.ps1`) runs the full verification
pipeline. Each phase must pass before the next begins.

### Phases

1. **Clean**: Remove `build-output/` and temporary files.

2. **Source concat**: `concat-codex-self.ps1` scans `codex/` for
   `.codex` files, resolves foreword dependencies transitively, and
   emits a single concatenated source file. Foreword chapters are
   prefixed with `Foreword--`. Compiler chapters in subdirectories are
   prefixed with the directory name (e.g., `Emit--`).

3. **CDX build**: Boot the seed (`seed/Codex.cdx`) in the VM
   (codex-vm by default), feed it the concatenated source over serial,
   receive the compiled CDX binary. This produces the SUT (System Under
   Test).

4. **Sign**: If `D:\Projects\signing.key` exists, compile and run an
   inline Ed25519 signing program to embed a signature in the CDX
   header (bytes 40–135).

5. **Canary**: Compile `codex.test/hello.codex` with the SUT and verify
   runtime output matches `hello.expected`. This confirms the SUT can
   compile and run a simple program.

6. **Semantic equivalence**: The SUT emits the source in TEXT mode
   (stage1.codex). `compare-codex-semantic.ps1` parses both source and
   stage1, normalizes whitespace/parens/operator aliases, and compares
   every definition body. Mismatches indicate the emitter lost
   information.

7. **Text fixed point**: The SUT emits stage1.codex, then emits
   stage1.codex again to produce stage2.codex. SHA-256 of stage1 must
   equal SHA-256 of stage2. This proves the text emitter is idempotent.

8. **CDX fixed point**: The SUT compiles source → stage1.cdx. Then
   stage1.cdx compiles source → stage2.cdx. SHA-256 of stage1.cdx must
   equal SHA-256 of stage2.cdx. This proves the compiler is a fixed
   point of itself — the binary it produces is identical to itself.

9. **Test battery**: `test.ps1` runs all samples in `codex.test/`.
   Each sample has a sidecar (`.expected` for success, `.failing` for
   expected errors, `.skip` for skipped). Runs 4 parallel VM
   instances.

### VM Configuration

All scripts use `vm-config.ps1` for shared VM setup (codex-vm default, QEMU via `$env:USE_QEMU=1`):
- Accelerator: WHPX (Windows Hypervisor Platform)
- Memory: 2048 MB (configurable via MemMB parameter)
- Serial: dual chardev sockets (data on ch0, control on ch1)
- Network: NE2K ISA NIC
- `kernel-irqchip=off` required for bare-metal operation

### Self-Host Compilation Protocol

`test-compile.ps1` boots the compiler kernel in the VM (codex-vm by
default, QEMU via `$env:USE_QEMU=1`) and communicates over serial:

1. Wait for `READY` on control channel (ch1).
2. Send mode header (`CDX`, `ELF`, `TEXT`, `IR`, etc.) on data channel.
3. Send foreword library bytes (transitively resolved).
4. Send source bytes.
5. Send EOT (0x04).
6. Read diagnostic lines until `SIZE:<n>` (success) or
   `CODEGEN-HALTED` (failure).
7. On success, read `n` raw bytes of binary output.

## Known Platform Constraints

### 4 GB Barrier

Physical addresses 0xC0000000–0xFFFFFFFF (the top 1 GB of the 32-bit
address space) are reserved for PCI MMIO on x86 platforms. Both
codex-vm and QEMU respect this: `-m 4096` provides 4 GB of RAM, but physical addresses
in the MMIO window are not usable as RAM. RAM above 4 GB is relocated
to physical addresses starting at 0x100000000.

Codex does not currently support non-contiguous physical memory or
addresses above 4 GB. The practical ceiling for `bare-metal-ram-size`
is approximately 3 GB (0xC0000000). Setting it to 2 GB (0x80000000)
avoids the issue entirely with ample margin.

### Code Buffer Ceiling

The compiler's own code segment is approximately 2.1 MB. The code
buffer (`code-buffer-size`) is 4 MB with roughly 1.9 MB headroom. The
serial ring buffer at 0x500000 (5 MB) sits between the code and heap,
providing a hard upper bound on code size at the current layout (4 MB
for the binary, starting at 0x100000).

### Stack Size

The stack starts at `bare-metal-stack-top` and grows downward. Typical
self-compilation uses approximately 1 MB of stack. The prologue
collision check (`cmp rsp, r10`) is the only protection against stack
overflow. There is no guard page.

## Status Server

`tools/status-server.ps1` is a lightweight HTTP dashboard for the Codex
project. It serves a single-page status view at `http://localhost:8080/`
with live data pulled from the workspace and Perforce.

```powershell
pwsh tools/status-server.ps1            # default port 8080
pwsh tools/status-server.ps1 -Port 9090 # custom port
```

The page displays:

- **Seed CDX** — size, SHA-256 prefix, last-modified timestamp
- **Test Battery** — pass/fail/skip counts from the most recent
  `test-output/_results`, last-run timestamp
- **At a Glance** — total modules, quires, compiler source lines,
  test file count
- **Modules by Quire** — per-quire breakdown with visual bar chart
- **Recent Changelists** — last 20 CLs from `p4 changes`

The page auto-refreshes every 30 seconds. The styling matches the
dark theme defined in `codex.works/Http.codex` (`html-page`).

## Debugging with GDB and QEMU

For memory corruption hunting, GDB under WSL with QEMU is the primary
tool. Rule 5 permits Unix tools for this purpose. The PowerShell script
`codex.build/gdb-watchpoint.ps1` wraps this workflow; the manual
procedure is documented below.

### Workflow: Trace First, Probe Second

1. **Trace** — run Codex.cdx under QEMU **TCG** (no KVM) with
   `-d in_asm` to capture every translated block. Use this to find
   which addresses are actually executed.
2. **Probe** — run Codex.cdx under QEMU **KVM** with gdbstub, set a
   hardware breakpoint at the target address, inspect registers when hit.

Never set a gdb `hbreak` at an address you have not first confirmed
is in the trace. A breakpoint at an unreached address looks like "gdb
is broken" — it is not.

### Step 1 — Find a Candidate Address

Use crash logs (`!EXC=0e RIP=0x273cef`) or the compiler's IR text
output to identify the address to probe.

### Step 2 — Verify the Address Is Executed

Run under QEMU TCG with `-d in_asm` (inside WSL):

```bash
CDX=seed/Codex.cdx
SAMPLE=build-output/bare-metal/bs3-mini.codex
TRACE=/tmp/bs3-qemu-trace.log
SERIAL=/tmp/bs3-trace-serial.raw
PIPE=/tmp/bs3trace-$$
rm -f "$PIPE" "$SERIAL" "$TRACE"
mkfifo "$PIPE"

(
    while ! grep -qa READY "$SERIAL" 2>/dev/null; do sleep 0.3; done
    printf 'CDX\n'; cat "$SAMPLE"; printf '\x04'
) > "$PIPE" &

/usr/bin/qemu-system-x86_64 \
    -kernel "$CDX" \
    -serial stdio \
    -device isa-debug-exit,iobase=0xf4,iosize=0x04 \
    -display none -no-reboot -m 1024 \
    -d in_asm -D "$TRACE" \
    < "$PIPE" > "$SERIAL" 2>/dev/null &
QEMU=$!

for i in $(seq 120); do
    kill -0 $QEMU 2>/dev/null || break
    grep -qa 'CODEGEN-HALTED\|CODEGEN-ERRORS\|CODEGEN-EMITTED' "$SERIAL" 2>/dev/null && { sleep 1; kill $QEMU 2>/dev/null; break; }
    sleep 1
done
kill $QEMU 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"

grep -c '^0x001e0534:' "$TRACE"
```

Non-zero means executed — proceed to Step 3. Zero means dead code;
the bug is upstream.

### Step 3 — Probe with GDB

Run under QEMU KVM with gdbstub (inside WSL):

```bash
ADDR=0x1e0534
CDX=seed/Codex.cdx
SAMPLE=build-output/bare-metal/bs3-mini.codex
SERIAL=/tmp/bs3-gdb-serial.raw
PIPE=/tmp/bs3gdb-$$
rm -f "$PIPE" "$SERIAL"
mkfifo "$PIPE"

(
    while ! grep -qa READY "$SERIAL" 2>/dev/null; do sleep 0.3; done
    printf 'CDX\n'; cat "$SAMPLE"; printf '\x04'
) > "$PIPE" &

/usr/bin/qemu-system-x86_64 \
    -enable-kvm -kernel "$CDX" \
    -serial stdio \
    -device isa-debug-exit,iobase=0xf4,iosize=0x04 \
    -display none -no-reboot -m 1024 \
    -gdb tcp::1234 -S \
    < "$PIPE" > "$SERIAL" 2>/dev/null &
QEMU=$!
sleep 1

cat > /tmp/bs3gdb.gdb << GEOF
set architecture i386:x86-64
target remote :1234
set pagination off
set confirm off
hbreak *$ADDR
continue
printf "HIT $ADDR: rip=%#lx rdi=%#lx rsi=%#lx rdx=%#lx rcx=%#lx r8=%#lx r9=%#lx\n", \$rip, \$rdi, \$rsi, \$rdx, \$rcx, \$r8, \$r9
kill
quit
GEOF

timeout 30 /usr/bin/gdb -batch -nx -x /tmp/bs3gdb.gdb 2>&1

kill $QEMU 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE" /tmp/bs3gdb.gdb
```

### Step 4 — Interpret Registers

Codex does not use the System V ABI — see the Register Convention
table above. First argument goes in RDI, temporaries cycle through
RAX/RCX/RDX/RSI/RDI/R11. Callee-saved locals are RBX/R12/R13/R14.
R10 is the bump allocator pointer. R15 is the closure environment.

### GDB Script Skeleton

```
set architecture i386:x86-64
target remote :1234
set pagination off
set confirm off

hbreak *0xADDRESS
continue
printf "HIT rip=%#lx rdi=%#lx rsi=%#lx\n", $rip, $rdi, $rsi

kill
quit
```

Must set architecture BEFORE `target remote` — default is i386, gdb
rejects the x86-64 binary otherwise.

### Known GDB/QEMU Quirks

1. **HW breakpoint requires exact instruction boundary.** An `hbreak`
   mid-instruction silently never fires. Addresses from crash dumps are
   safe; arbitrary `+N` offsets may not be.

2. **Only 4 HW breakpoints (DR0-DR3).** A 5th `hbreak` fails silently.
   Use software `break` (INT3) for overflow — QEMU's gdbstub intercepts
   INT3 before the guest IDT.

3. **One continue per session.** After a HW bp hits, a second `continue`
   in the same batch session fails with "target is running." Set all
   breakpoints before the first `continue`. Alternatively, use the gdb
   Python API with a `stop` event listener.

4. **TCG is slow.** `-d in_asm` forces TCG. Compiling the mini sample
   takes ~20-60s under TCG vs. ~2s under KVM. Use TCG only for tracing.

### QEMU Debug Flags

| Flag | Purpose |
|------|---------|
| `-kernel Codex.cdx` | Multiboot boot of CDX |
| `-serial stdio` | Kernel's `CDX\n<src>\x04` input, binary output |
| `-device isa-debug-exit,iobase=0xf4,iosize=0x04` | `out 0xf4, 0` exits QEMU cleanly |
| `-gdb tcp::1234 -S` | GDB stub on port 1234, start halted |
| `-enable-kvm` | 10x+ faster — use for all iterative debug runs |
| `-d in_asm -D file.log` | Record every translated block — TCG only, no KVM |
| `-display none -no-reboot -m 1024` | Headless, 1 GB |
