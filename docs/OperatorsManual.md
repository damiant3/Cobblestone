# Operator's Manual

This document covers the runtime memory model, allocator architecture,
build process, and known platform constraints for the Codex bare-metal
compiler.

## Memory Layout

The bare-metal system occupies a single flat physical address space.
All addresses are identity-mapped (virtual = physical). The single
governing constant is `bare-metal-ram-size` in
`codex/Emit/X86_64State.codex`. Every other memory value derives from
it.

```
Address           Region
──────────────    ──────────────────────────────────
0x0000            Boot / real-mode area
0x5000            Process table (16 entries × 256 bytes)
0x6000            IDT (Interrupt Descriptor Table)
0x7000            Kernel metadata (addresses below)
0x8000            Runtime page tables (PML4 + PDPT + PDs)
0x100000  (1 MB)  Code text segment (bare-metal-load-addr)
0x500000  (5 MB)  Serial ring buffer (1 MB)
0x600000  (6 MB)  Heap base (bare-metal-heap-base, R10 init)
  ...             Heap grows UP via R10
  ...             Stack grows DOWN via RSP
ram-size          Stack top (bare-metal-stack-top = bare-metal-ram-size)
```

### Kernel Metadata (0x7000 region)

Fixed addresses for runtime state, defined in `codex/Emit/X86_64Boot.codex`:

| Address | Name | Purpose |
|---------|------|---------|
| 28688 | current-proc-addr | Running process index |
| 28696 | arena-base-addr | Phase allocator root |
| 28704 | serial-write-pos-addr | Serial output ring position |
| 28712 | serial-read-pos-addr | Serial input ring position |
| 28720 | deck-pos-addr | Deck allocator position |
| 28728 | heap-hwm-addr | Heap high-water mark |
| 28736 | stack-min-rsp-addr | Lowest RSP observed |
| 28912 | bivy-save-addr | Bivy allocator save point |
| 28920 | stdin-eof-flag-addr | EOF flag for serial input |
| 28944 | sched-ready-head-addr | Scheduler ready queue |
| 28968 | handler-table-base-addr | Effect handler dispatch (512 bytes) |

### Derived Constants

Defined in `codex/Emit/X86_64State.codex`, derived from `bare-metal-ram-size`:

| Constant | Formula | Purpose |
|----------|---------|---------|
| bare-metal-stack-top | bare-metal-ram-size | RSP initial value |
| bare-metal-pd-count | ceil(ram-size / 1 GB) | Page directories needed |
| bare-metal-page-count | ceil(ram-size / 2 MB) | Total 2 MB pages |

The CDX header heap field and ELF segment memsz are both computed as
`bare-metal-ram-size - bare-metal-heap-base` in
`codex/Emit/X86_64Chapter.codex`.

## Heap and Stack

Heap and stack share the arena between `bare-metal-heap-base` (4 MB) and
`bare-metal-stack-top` (= ram-size). The heap grows upward via register
R10; the stack grows downward via RSP.

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
heap position and initializes a deck region with `__deck-set`.
`seal(start)` finalizes the region. Deck regions survive phase
compaction; bivy regions do not.

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

- **text-buf**: Machine code (x86-64 instructions). Capacity set by
  `text-buf-size` in `codex/Emit/X86_64Chapter.codex` (currently 2 MB).
- **rodata-buf**: Read-only data (string literals, CCE tables). Capacity
  set by `rodata-buf-size` (currently 512 KB).

`init-emit-workspace(text-cap, rodata-cap)` allocates both buffers from
the heap and returns an `EmitWorkspace` record with base addresses and
capacities. The code generator writes into these buffers via
`st-append-text` and `__buf-write-bytes`, tracking `text-len` and
`rodata-len` in `CodegenState`.

After code generation, the text and rodata buffers are assembled into
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

3. **CDX build**: Boot the seed (`seed/Codex.cdx`) in QEMU, feed it the
   concatenated source over serial, receive the compiled CDX binary.
   This produces the SUT (System Under Test).

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
   expected errors, `.skip` for skipped). Runs 4 parallel QEMU
   instances.

### QEMU Configuration

All scripts use `qemu-config.ps1` for shared QEMU setup:
- Accelerator: WHPX (Windows Hypervisor Platform)
- Memory: 2048 MB (configurable via MemMB parameter)
- Serial: dual chardev sockets (data on ch0, control on ch1)
- Network: NE2K ISA NIC
- `kernel-irqchip=off` required for bare-metal operation

### Self-Host Compilation Protocol

`sample-compile-selfhost.ps1` boots the compiler kernel in QEMU and
communicates over serial:

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
address space) are reserved for PCI MMIO on x86 platforms. QEMU
respects this: `-m 4096` provides 4 GB of RAM, but physical addresses
in the MMIO window are not usable as RAM. RAM above 4 GB is relocated
to physical addresses starting at 0x100000000.

Codex does not currently support non-contiguous physical memory or
addresses above 4 GB. The practical ceiling for `bare-metal-ram-size`
is approximately 3 GB (0xC0000000). Setting it to 2 GB (0x80000000)
avoids the issue entirely with ample margin.

### Text Buffer Ceiling

The compiler's own text segment is approximately 2 MB. The text emit
buffer (`text-buf-size`) is 2 MB with roughly 50 KB headroom. Adding
significant code to the compiler may require increasing this buffer.
The serial ring buffer at 0x500000 (5 MB) sits between the code and
heap, providing a hard upper bound on code size at the current layout
(4 MB for the binary, starting at 0x100000).

### Stack Size

The stack starts at `bare-metal-stack-top` and grows downward. Typical
self-compilation uses approximately 1 MB of stack. The prologue
collision check (`cmp rsp, r10`) is the only protection against stack
overflow. There is no guard page.
