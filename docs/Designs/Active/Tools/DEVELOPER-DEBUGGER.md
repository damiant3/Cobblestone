# Codex Developer Quire -- Bare-Metal Hardware Debugger

## Status

**Seven inspectors are live**, in `codex/os/dev/`:

| Shipped | |
|---|---|
| `HexFormat.codex` | `MemInspector.codex` |
| `IoInspector.codex` | `AtaDebugger.codex` |
| `PerfMonitor.codex` | `DiskInspector.codex` |
| `IdtInspector.codex` | |

The `dbg` command dispatch is live too -- in `apps/works/DevDebugger.codex`,
not the `ShellDebug.codex` this document names.

**One named module does not exist**: `ShellDebug.codex`, and it is
superseded rather than missing -- `apps/works/DevDebugger.codex` is where
the dispatch actually lives. **Every other module in this design is
built.** Phases 1, 2 and 3 are closed as of 2026-07-28, and Phase 4's
port trace ring landed the same day, which leaves three rows in that
phase and the two hardest of them behind compiler work.

**The Port I/O view had never had an input.** `watch-ports` was
initialised empty and no command anywhere added to it, so the view
rendered a heading over nothing on every repaint from the day it was
written until `io.watch` landed 2026-07-28. It is the same shape as
`Address Lookup` having no dispatch arm and `block-select` writing three
cells nothing read: a menu item that draws, answers, and means nothing.

**`CpuInspector.codex` is built and wired as of 2026-07-28**, on the six
builtins that landed with a seed the same day. `cpu.cr` decodes CR0 and
CR3, `cpu.cpuid` takes a leaf expression against its own cursor, and
`cpu.feat` names the feature bits this project actually depends on.

Two things it reports that are worth knowing before reading its output.
**Leaf 1 ECX is zero under codex-vm**, so the feature line for it is
empty on the machine the battery runs on and renders as `(none)` rather
than as a blank that reads like a broken decode. And the **HYPERVISOR bit
is therefore NOT set under codex-vm**: bit 31 of leaf 1 ECX is the usual
way to ask "am I virtualised", and on this host it answers no. Do not use
it to detect codex-vm.

**`Macro.codex` is built and wired as of 2026-07-28.** `macro.def` takes a
name then a body of commands separated by `;`, `macro.run` executes one,
`macro.list` and `macro.clear` manage the table, and `repeat` takes a
count and a command on one line. Sixteen macros, sixteen commands each, a
240-character body and a repeat count of 64: every cap REFUSES rather than
truncating, because a macro silently cut short at its eighth command runs
in front of the operator with no error and looks like it worked.

**The recursion bound is a refusal, not a depth limit.** A body that runs
a macro can reach itself, and with no collector and no stack guard that is
a hang rather than a diagnostic. `macro.run` is refused outright while a
macro is already running: one flag to read, against a limit that has to be
got right and then trusted, and nothing a debugger macro is for needs
nesting. The macro's summary carries the last command's status so that an
in-macro refusal is visible on screen, not just on the serial mirror.

**`SerialBridge.codex` is built and wired as of 2026-07-28.** The console
mirrors each command and its answer to COM1 under `serial.on`, and reads
commands from COM1 under `serial.remote`, with `serial.off` and
`serial.local` clearing each flag. The two flags are INDEPENDENT rather
than one mode with two positions, because they govern opposite
directions and wanting to watch a console you are driving by keyboard is
the ordinary case. **Remote input never disables the keyboard**: the
serial check sits on the path where `uefi-con-poll-key` already answered
nothing, so no arrangement of the flags can strand an operator in front
of a console that has stopped listening (L-FALLBACK). That is a
deliberate divergence from the `serial.local` row below, which reads as a
switch back from a mode this does not enter.

This said six until 2026-07-28, then five;
`DiskInspector.codex` and `IdtInspector.codex` were both built that day,
so `dbg disk.*` and `dbg idt.*` are behaviour now rather than
specification. The three CPU-state builtins (`cpu-read-cr0`,
`cpu-read-cr3`, `cpu-cpuid`) still do not exist -- so the entire
`dbg cpu.*` command family below is unimplemented and `CpuInspector.codex`
is not startable, and the command tables that depend on a module that is
still missing (`dbg macro.*`, `dbg serial.*`) remain specification.

**There is no exception record on this machine, and this document assumed
one.** `dbg idt.fault` was specified as a watchdog ring of RIPs, which is
real and is what it now reads. But the Registers, Stack and Backtrace
views were reading an "exception record" at address 28928 with a vector,
RIP and RSP at its first three offsets, and no such record exists
anywhere in the address map: 28928 is `stdin-eof-settled-addr`, and the
next two cells are `cap-expiry-addr` and `sched-ready-head-addr`. Three
menu items were rendering the standard-input EOF flag as a fault vector
and walking a stack from the scheduler's ready-list head. Nothing looked
blank, so it read as working. Fixed 2026-07-28: all three now read the
watchdog ring, which is the only place on this machine a RIP is ever
stored, and none of them reports a vector, because none is recorded.

**`ExprEval.codex` landed 2026-07-28.** Hex and decimal literals, the
named constants, `$`, and `+ - * / & | ^ << >>` over two precedence
levels; every failure is an `ExprErr` naming the token and its position,
because an evaluator that answers 0 for input it did not understand is
how you dump the wrong page and believe it. It is wired into the
debugger's dispatch: a line in ModeDebug that is not a menu key is an
address expression, where it used to be `else state` and was swallowed
without a word.

Two tests, because it is two things. `codex/test/apps/expr-eval` is the
pure evaluator across 25 cases, roughly half of which require a specific
error. `codex/test/apps/debug-expr` drives the wiring through
`handle-console-input`, which is the only way to prove the arm is reached
at all -- calling `expr-eval` directly would pass against the old
swallowing arm.

**`DevState.codex` landed 2026-07-28, and watches work.** A bounded watch
table -- 32 entries, 64 KiB per region -- over FNV-1a 32-bit region
checksums, wired into the debugger as Watch Here / Check Watches / Clear
Watches. The whole chapter is pure, because `peek-byte` carries no effect
row, so the watch logic is tested without a machine
(`codex/test/apps/dev-watch`) and the console wiring is tested through
`handle-console-input` (`codex/test/apps/debug-expr`).

A check **re-snapshots** every watch it reports, so each report answers
"since the last check" rather than "ever". That is the one behaviour worth
protecting: without it a single change makes a watch report as changed for
the rest of the session, and an operator watching two regions cannot tell
which one moved. The test ablates exactly that.

**Three things came out differently from the specification above, and the
differences are deliberate rather than partial.**

- **The snapshot lives in `DevState`, not in `MemInspector`.** The module
  table used to mark MemInspector built for "hex dump, search, compare,
  watch"; the chapter really has a dump, four reads and one write, and it
  still does. Putting the checksum beside the table that holds it keeps
  the pure half in one chapter. **MemInspector's search and compare are
  still not built** and are still specification.
- **`last-result` is not in the record.** The debugger's `last-address` is
  already what an evaluated expression sets and what `$` reads. A second
  field holding the same number is two places that must agree with nothing
  checking they do.
- **`macros`, `serial-mirror` and `serial-remote` are not in the record**
  either, and once both modules landed on 2026-07-28 they still did not
  move in. `Macro.codex` owns its own `MacroTable`, because a table with
  its own bounds, refusals and splitter can be tested as a unit -- the
  same argument that put the watch table in `DevState` rather than in
  `MemInspector`. The serial flags live in the console's record because
  they are not debugger data: they govern how the console does I/O, and
  nothing pure has an opinion about them. The design's one unified record
  is three chapters, each holding what it can test.

**The two commands are menu items, not typed lines**, and `watch` arms at
the CURSOR rather than taking an address of its own: the length is the
dump length, so the region watched is the region just dumped. ExprEval
supplies the address (`heap-base + 0x100` moves the cursor, then `watch`
watches it), which is what makes the pairing work without a second
argument parser. The command table below still writes them as
`dbg watch <addr> [len]` and `dbg watch.check`; read those rows as the
capability, not the syntax.

**A checksum cannot say WHAT changed**, only that something did. Storing
the bytes would allow an old-against-new diff at the cost of the region's
length per watch; a checksum costs one integer. `dbg mem <addr>` is one
command away, so the pairing is: the watch says it moved, the dump says
what it is now. If a future reader wants the diff, the honest change is
storing bytes under a small per-watch cap, not widening this.

## Quire Placement

New quire: **`codex.os.dev`**, cited as **`Dev`**.

```
Foreword  <--  Kernel  <--  OS  <--  Dev  <--  Works
```

Dev depends on Foreword (CCE, Maybe, Hamt, StringBuilder) and Kernel (Vga, Console, DiskFacts, Keyboard). It does NOT depend on Trust, Verify, or Net -- the debugger must work when those subsystems are broken.

Works gains `ShellDebug.codex` that wires Dev commands into the shell dispatch.

## Core Modules

| File | Cite | Purpose | Built? |
|------|------|---------|--------|
| `codex.os.dev/HexFormat.codex` | `Dev chapter HexFormat` | Hex formatting: byte-to-hex, addr-to-hex, dump lines, ASCII sidebar | **yes** |
| `codex.os.dev/DevState.codex` | `Dev chapter DevState` | The memory watch table: 32 entries, 64 KiB each, FNV-1a 32-bit checksums, re-armed on every check. **Not** the "unified state record" this document specified -- `last-result`, `macros` and the two serial flags are deliberately absent (see Status) | **yes** |
| `codex.os.dev/ExprEval.codex` | `Dev chapter ExprEval` | Inline arithmetic: hex literals, +, -, *, /, &, \|, ^, <<, >> | **yes** |
| `codex.os.dev/MemInspector.codex` | `Dev chapter MemInspector` | Hex dump, byte/u16/u32/u64 read, byte write. **Search, compare and watch are NOT built** (checked 2026-07-28; the chapter has `mem-dump`, four `mem-read-*` and `mem-write-byte`, and nothing else) | partly |
| `codex.os.dev/IoInspector.codex` | `Dev chapter IoInspector` | Port read/write, ATA register decode, PCI config | **yes** |
| `codex.os.dev/AtaDebugger.codex` | `Dev chapter AtaDebugger` | Step-through ATA PIO protocol with full status decode | **yes** |
| `codex.os.dev/IdtInspector.codex` | `Dev chapter IdtInspector` | Gate decode at a vector, tick and keyboard counters, watchdog stall ring, PIC IRR/ISR/IMR. Also the source of the RIP and RSP the Registers and Backtrace views show, there being no exception record | **yes** |
| `codex.os.dev/CpuInspector.codex` | `Dev chapter CpuInspector` | CR0 and CR3 decode, raw CPUID leaf, vendor string, feature-flag names. **CR4 is not read** -- no builtin for it, and nothing asked for one | **yes** |
| `codex.os.dev/DiskInspector.codex` | `Dev chapter DiskInspector` | Raw sector dump, superblock viewer, fact log browser, sector compare, sector count | **yes** |
| `codex.os.dev/PerfMonitor.codex` | `Dev chapter PerfMonitor` | Tick delta, heap HWM, stack depth | **yes** |
| `codex.os.dev/Macro.codex` | `Dev chapter Macro` | Named command sequences: bounded table, `;` splitter, repeat parsing. All caps refuse rather than truncate. The recursion bound is a flat refusal of nested `macro.run`, enforced in the console where the flag lives | **yes** |
| `codex.os.dev/SerialBridge.codex` | `Dev chapter SerialBridge` | COM1 primitives and line assembly: fuel-capped write with a drop count, CCE-to-ASCII outbound and ASCII-to-CCE inbound, non-blocking read, EOL classification. Wired into the console as `serial.on/off/remote/local` (2026-07-28) | **yes** |
| `codex.os.dev/PortTrace.codex` | `Dev chapter PortTrace` | A 64-entry ring of port accesses over `Foreword chapter RingBuffer`, one packed Integer per entry (byte, port, direction). `pt-read` and `pt-write` are the traced pair and the only writers; the header names the total as well as the count, so a wrapped ring is distinguishable from one that never filled | **yes** |
| `codex.works/ShellDebug.codex` | `Works chapter ShellDebug` | Command parser and dispatch for all `dbg *` commands | superseded by `apps/works/DevDebugger.codex` |

## Shell Commands

All prefixed with `dbg` to avoid collision with existing shell.

### Memory Inspector

| Command | Syntax | Description |
|---------|--------|-------------|
| `dbg mem` | `dbg mem <addr> [len]` | Hex dump (default 256 bytes). 16/line, addr + hex + ASCII sidebar |
| `dbg mem.b` | `dbg mem.b <addr>` | Read single byte |
| `dbg mem.w` | `dbg mem.w <addr>` | Read 16-bit LE |
| `dbg mem.d` | `dbg mem.d <addr>` | Read 32-bit LE |
| `dbg mem.q` | `dbg mem.q <addr>` | Read 64-bit LE |
| `dbg mem.s` | `dbg mem.s <addr> <bytes>` | Search for byte pattern |
| `dbg mem.cmp` | `dbg mem.cmp <a1> <a2> <len>` | Compare two regions |
| `dbg mem.w!` | `dbg mem.w! <addr> <byte>` | Write byte (shows old + new) |
| `dbg mem.fill` | `dbg mem.fill <addr> <len> <byte>` | Fill region |
| `dbg watch` | `dbg watch <addr> [len]` | Snapshot for change detection |
| `dbg watch.check` | `dbg watch.check` | Report changed watches |

### I/O Port Inspector

| Command | Syntax | Description |
|---------|--------|-------------|
| `dbg io.in` | `dbg io.in <port>` | Read byte from port |
| `dbg io.out` | `dbg io.out <port> <byte>` | Write byte to port |
| `dbg io.ata` | `dbg io.ata` | Decode all ATA primary registers (0x1F0-0x1F7 + 0x3F6) |
| `dbg io.com1` | `dbg io.com1` | Decode COM1 registers |
| `dbg io.pic` | `dbg io.pic` | PIC ISR/IRR state |
| `dbg io.pit` | `dbg io.pit` | PIT counter values |
| `io.r` | `io.r` then a port | Read a port and record the access. The port is an expression seeded with zero, so `0x3fd` works and `$` means nothing in particular -- a port is not a space anything steps through, so it gets no cursor. Out of 0..65535 is refused |
| `io.watch` | `io.watch` then a port | Add a port to the Port I/O view's list, capped at 16. The list is re-read on every repaint, so its length is a per-frame I/O cost |
| `io.trace` | `io.trace` / `io.trace.off` | Arm and disarm recording. Disarmed, `io.r` still reads and records nothing |
| `io.trace.show` | `io.trace.show` | The ring, oldest first, over a header naming kept against total |
| `io.trace.clear` | `io.trace.clear` | Empty the ring and zero the total, leaving it armed as it was |

### ATA Debugger (the motivating feature)

| Command | Syntax | Description |
|---------|--------|-------------|
| `dbg ata.id` | `dbg ata.id` | IDENTIFY DEVICE: model, serial, firmware, total sectors |
| `dbg ata.read` | `dbg ata.read <lba>` | Traced PIO read with status at each phase |
| `dbg ata.step` | `dbg ata.step <lba>` | Interactive step-through: pause after each PIO phase, show registers, wait for keypress |
| `dbg ata.verify` | `dbg ata.verify <lba>` | Write 0x55AA pattern, read back, compare byte-by-byte |
| `dbg ata.stress` | `dbg ata.stress <lba> <count>` | Multi-sector write+verify |
| `dbg ata.reset` | `dbg ata.reset` | ATA soft reset with trace |

**How `dbg ata.step` diagnoses the off-by-one bug:**

```
codex> dbg ata.step 0
  [1/6] WAIT-READY
    Status: BSY=0 DRDY=1 DRQ=0 ERR=0  (0x50)
  [press any key]

  [2/6] SET-LBA
    Drive/Head: 0xE0  (LBA mode, drive 0)
    LBA: 0x000000  Sector Count: 1
  [press any key]

  [3/6] SEND-COMMAND  (READ SECTORS = 0x20)
  [press any key]

  [4/6] WAIT-DRQ
    Status: BSY=0 DRQ=1  (0x58)
  [press any key]

  [5/6] PRE-TRANSFER PROBE (manual word reads from port 0x1F0)
    Word 0: 0x4F43  -> bytes: 43 4F  ('C' 'O')
    Word 1: 0x4544  -> bytes: 44 45  ('D' 'E')
  [press any key]

  [6/6] BULK TRANSFER (REP INSW, 256 words)
    Buffer first 32 bytes:
    002A0000  43 4F 44 45 58 46 53 31  00 00 00 00 00 00 00 00
    Manual probe matches buffer: CONSISTENT (no off-by-one)
```

If there's an off-by-one, step 5 shows `43 4F` but step 6 shows the buffer starting at `4F 44` -- the mismatch is immediately visible. Minutes instead of hours.

### Interrupt / IDT Inspector

| Command | Syntax | Description |
|---------|--------|-------------|
| `dbg idt` | `dbg idt` then a vector expression | Display IDT entry: offset, selector, type, DPL, IST, present. The vector is an expression seeded with the vector cursor, so `$ + 1` steps to the next gate; anything outside 0..255 is refused and the cursor does not move |
| `dbg idt.counts` | `dbg idt.counts` | Tick counter and last keyboard scancode. Both cells are zeroed before `sti`, so zero is reported as "the ISR has not run" rather than as a count |
| `dbg idt.fault` | `dbg idt.fault` | The watchdog's four-slot ring, newest first: RIP, RSP and heap pointer per sample. The timer ISR writes a slot when neither the heap pointer nor the saved RIP moved since the last tick, so a slot is a STALL sample and not a trap frame |
| `dbg idt.irq` | `dbg idt.irq` | PIC IRR, ISR and IMR for both controllers, named by line. The mask is a third register the design did not name, and it is the one that answers why a line that should be firing is silent |

Measured 2026-07-28 on a booted test kernel, and worth recording because
two of the three are not what a reading of the design would predict. Every
one of the 256 gates is present, type 14, DPL 0, selector 8, with the
stubs twelve bytes apart; vector 8 is the only gate with a non-zero IST,
which is the double-fault emergency stack. And the master PIC's mask is
`#ec` against the slave's `#ff` -- the timer, keyboard and COM1 are
unmasked, and **IRQ 2 is masked**, so the cascade is closed and the second
controller cannot deliver anything at all. Whether that is intended is
not this document's question; that it is the state is.

### CPU State

| Command | Syntax | Description |
|---------|--------|-------------|
| `dbg cpu.cr` | `dbg cpu.cr` | CR0, CR3 values |
| `dbg cpu.cpuid` | `dbg cpu.cpuid <leaf>` | CPUID leaf: EAX/EBX/ECX/EDX |
| `dbg cpu.feat` | `dbg cpu.feat` | Feature flag decode: SSE, RDRAND, long mode, etc |

Requires 3 new builtins (Phase 2): `cpu-read-cr0`, `cpu-read-cr3`, `cpu-cpuid`.

### Disk Inspector

| Command | Syntax | Description |
|---------|--------|-------------|
| `dbg disk` | `disk`, then an LBA expression | Hex dump one sector |
| `dbg disk.sb` | `disk.sb` | Superblock decode **at the fact partition's base**, not at sectors 0+1 |
| `dbg disk.facts` | `disk.facts` | Walk fact log entry headers |
| `dbg disk.sc` | `disk.sc` | ATA sector count |
| `dbg disk.cmp` | `disk.cmp`, then an LBA expression | Compare the disk cursor against one other sector |

**BUILT 2026-07-28, and the syntax column above is what shipped rather
than what was specified.** The console dispatches one line at a time, so
each command takes at most one argument the same way the memory commands
do: an expression supplies the sector, the command acts there, and the
disk cursor (`last-lba`) is what `$` means inside these prompts. Seeding
the evaluator with the memory cursor instead would answer a sector number
six orders of magnitude off the medium.

`disk.sb` reading sectors 0 and 1 was correct before the fact store had a
partition and is not correct now; the row is corrected above.
`codex/test/apps/disk-inspect` pins it against a fixture whose store is at
LBA 128, and the ablation was fired: pointing the read at LBA 0 moves
exactly two lines of the expected output and leaves the other eight
byte-identical.

The multi-line reports land in `disk-lines` on the debugger state and the
view renders that, rather than the view producing them. A view that read
the sector would perform a disk transaction and leak 512 bytes of heap on
every repaint, because `block-read-sector` bumps the allocator and nothing
returns it.

### Performance

| Command | Syntax | Description |
|---------|--------|-------------|
| `dbg perf` | `dbg perf` | Snapshot: ticks, heap HWM, stack depth, process count |
| `dbg perf.heap` | `dbg perf.heap` | Heap used + HWM |
| `dbg perf.stack` | `dbg perf.stack` | Stack min-RSP |
| `dbg perf.ticks` | `dbg perf.ticks` | Stopwatch (first call starts, second shows delta) |
| `dbg perf.proc` | `dbg perf.proc` | Process table summary |

### Expression Evaluator

All address arguments accept expressions:
- Decimal: `12345`
- Hex: `0x1F7`, `0xB8000`
- Named constants: `vga-base`, `ata-status`, `heap-hwm`, `tick-count`
- Arithmetic: `+`, `-`, `*`, `/`, `&`, `|`, `^`, `<<`, `>>`
- `$` = last result

```
codex> dbg mem.q heap-hwm
codex> dbg mem vga-base+0xA0 32
codex> dbg io.in ata-status
```

### Macros

| Command | Syntax | Description |
|---------|--------|-------------|
| `dbg macro.def` | `dbg macro.def <name> <cmd1> ; <cmd2>` | Define macro |
| `dbg macro.run` | `dbg macro.run <name>` | Execute macro |
| `dbg repeat` | `dbg repeat <n> <cmd>` | Repeat command N times |

### Serial Bridge

| Command | Syntax | Description |
|---------|--------|-------------|
| `dbg serial.on` | `dbg serial.on` | Mirror all output to COM1 |
| `dbg serial.remote` | `dbg serial.remote` | Read commands from COM1 (remote debugging) |
| `dbg serial.local` | `dbg serial.local` | Return to keyboard input |

## Hex Dump Format

```
AAAAAAAA  HH HH HH HH HH HH HH HH  HH HH HH HH HH HH HH HH  |ASCII_SIDEBAR...|
```

78 chars/line, fits 80-column VGA. Hex bytes cyan (0x0B), addresses light-gray (0x07), ASCII sidebar dark-gray (0x08), changed bytes highlighted red (0x0C).

## ATA Register Decode

Status register (0x1F7):

| Bit | Name | Meaning |
|-----|------|---------|
| 7 | BSY | Drive busy |
| 6 | DRDY | Drive ready |
| 5 | DF | Drive fault |
| 4 | DSC | Seek complete |
| 3 | DRQ | Data transfer requested |
| 2 | CORR | Corrected data |
| 1 | IDX | Index mark |
| 0 | ERR | Error (check error register) |

## Debugger State

```
DebuggerState = record {
  watches : List WatchEntry,
  macros : HamtMap MacroEntry,
  serial-mirror : Boolean,
  serial-remote : Boolean,
  last-result : Integer,
  ticks-start : Integer
}
```

Bounded: max 32 watches (~10KB), max 64 macros (~16KB).

## New Builtins (Phase 2)

| Builtin | Signature | Implementation |
|---------|-----------|----------------|
| `cpu-read-cr0` | `() -> Int` | `mov rax, cr0; ret` |
| `cpu-read-cr3` | `() -> Int` | `mov rax, cr3; ret` |
| `cpu-cpuid` | `Int -> Int` | `mov eax, edi; cpuid; shl rbx, 32; or rax, rbx; ret` |

**Built 2026-07-28, and the CPUID row above is wrong twice.** What landed
is six builtins, not three: `cpu-read-cr0`, `cpu-read-cr3`, and
`cpu-cpuid-eax` / `-ebx` / `-ecx` / `-edx`, each taking the leaf and
answering one register.

**The packed `eax | ebx << 32` cannot serve the command table three rows
above it.** `dbg cpu.cpuid` promises EAX/EBX/ECX/EDX and `dbg cpu.feat`
decodes feature flags, which live in **ECX and EDX** of leaf 1 and leaf
0x80000001. A builtin that returns only EAX and EBX cannot answer either
command. The design specified a packing that drops exactly the halves its
own consumer needs.

**And the sequence corrupts the caller.** `shl rbx, 32; or rax, rbx`
clobbers RBX, and RBX is CALLEE-SAVED in this ABI -- the function
prologue in `Emit/X86_64.codex` pushes `rbx, r12, r13, r14` and the
epilogue pops them, so a helper that writes RBX without restoring it
corrupts whatever the caller was holding there. Every CPUID helper now
brackets the instruction with `push rbx` / `pop rbx`. This is not a
theoretical objection: the helpers are called like ordinary functions, so
the caller has every right to expect RBX back.

**ECX is zeroed before every CPUID**, because ECX is the SUBLEAF for
several leaves. Without it the subleaf is whatever the register happened
to hold, so the same leaf can answer differently between two runs of the
same command, which is the least useful property a debugger can have.

Four straight-line helpers were chosen over one with a selector argument
because a selector needs branches, and these are hand-emitted bytes in a
seed-carrying change: a straight-line sequence can be verified by reading
it, a patched jump chain has to be trusted. The cost is one CPUID per
register, which is nothing on a command driven by a keypress.

**The cross lanes refuse all six by name.** `arm64` and `riscv` enumerate
the builtins they cannot do and refuse them explicitly; there is no
catch-all, so a builtin with no arm falls through to a call to a symbol
those runtimes have never heard of. That is precisely how the list
intrinsics failed silently until main 11280, and adding six unhandled
names would have reopened it. Control registers and CPUID are x86
architecture rather than devices -- AArch64 has system registers via MRS
and RISC-V has CSRs, both answering different questions -- so there is no
honest translation and the refusal is the correct answer rather than a
gap.

## Implementation Phases

### Phase 1: Foundation -- mostly done, two gaps

Core that would have caught the ATA off-by-one in minutes:

1. `HexFormat.codex` -- formatting utilities -- **done**
2. `DevState.codex` -- the watch table -- **done 2026-07-28**
3. `ExprEval.codex` -- hex literal + arithmetic -- **done 2026-07-28**
4. `MemInspector.codex` -- `dbg mem`, `dbg mem.b/w/d/q`, `dbg mem.w!` -- **done**
5. `IoInspector.codex` -- `dbg io.in/out`, `dbg io.ata` -- **done**
6. `AtaDebugger.codex` -- `dbg ata.read`, `dbg ata.verify`, `dbg ata.step` -- **done**
7. Dispatch + integration -- **done**, as `apps/works/DevDebugger.codex`

**Phase 1 is closed.** Items 2 and 3 both landed 2026-07-28, so the
named-constant / arithmetic / `$` syntax works where the debugger takes an
address, and the watch table has a home.

**Memory read and write work at the cursor, as of 2026-07-28, and the
honest description of what was wrong is worse than "they passed a bare
literal".** `read` and `write` set a status saying "enter address" and
**nothing anywhere consumed the next line**: it fell through to the
address evaluator, moved the cursor, and no read or write ever happened.
There was no pending-input state at all, and no `debug-mem-read` or
`debug-mem-write` existed. Two menu items the console could not perform.

They act at the CURSOR now, like `watch`, which keeps one idiom instead of
two: an expression chooses the address, the command acts there. `read`
answers all four widths immediately. `write` is the only command that
needs a second word, so it is the only one that arms `pending`, and the
flag is cleared **before** the value is evaluated on both paths -- a
refusal that stayed armed would write the operator's next address
expression into memory.

A value outside a byte is refused and nothing is written.
`codex/test/apps/dev-mem-cmd` drives all of it through
`handle-console-input` over a buffer it allocates and zeroes, because the
cursor starts at 0x100000 and a test that wrote a byte there would corrupt
the running program to prove a command works. Both refusals are checked by
reading the byte back rather than by trusting the status line, and both
ablations fired: leaving `pending` set makes the following `$ + 1` be
swallowed as a value, and dropping the range check stores 300 as `#2c`
while the status claims `#fc` -- the report and the store disagreeing,
which is the sharper reason the guard is there.

**`sym` and `bp-add` take the name they prompt for, as of 2026-07-28.**
These were the last two prompt-and-consume-nothing commands, and here the
"it is only wiring" reading really was true: `debug-sym-lookup` and
`bp-add-symbolic` were both already written, correct, and **called from
nowhere**, while the menu advertised both. The line each prompted for went
to the address evaluator and moved the cursor.

`DebugPending` has two more cases, and the reason it is a variant rather
than a Boolean is that **the three pending cases do not read their line
the same way**. A write value is an expression and goes through
`ExprEval`; a symbol name and a function name are text and must not, since
`main` handed to an evaluator answers "unknown name" for something that
was never one. The flag records which reader gets the line, not merely
that one is expected.

`codex/test/apps/dev-name-cmd` drives both through
`handle-console-input`. It seeds `index-full-dev-surface` because
`dev-console-init` starts with an empty index, against which a hit and a
miss are the same line; with it, `parse` finds five definitions with
chapter and line and `zzz-no-such-symbol` does not. Ablation fired:
routing the names to the evaluator turns all three name lines into
`expr: unknown name ...`.

**One limit, stated because the line looks stronger than it is.** The
breakpoint case answers `No debug map found`. That string is produced by
`bp-add-symbolic` and by nothing else, so it proves the line was ROUTED
there rather than to the evaluator, which is what the change claims.

**The reason it answered that was three defects in `map1-find`, not a
missing map, and this document said otherwise until 2026-07-28.** It read
"`map1-find` finds no MAP1 in the test binary", and a test binary carries
one: 40 of 40 binaries under `test-output/` hold a valid MAP1, as does
every binary `compile.ps1` produces. What was broken was the reader.

| defect | measured |
|---|---|
| The magic constant was `826361677`, which is `0x3141474D`, or "MGA1". The emitter writes `[77, 65, 80, 49]` = "MAP1" (`map1-magic`, `X86_64Chapter.codex`), little-endian `827343181` | no emitted binary has ever held the bytes the reader searched for |
| The scan stepped by 4 from a 4-aligned base. The emitter appends MAP1 wherever the preceding section ends | of those same 40 binaries, **35 sit at an address congruent to 3 mod 4** and only 3 at 0, so even with the right constant a step-4 scan finds a map in about 7 per cent of binaries |
| `map1-scan` returned `map1-validate`'s result directly, so a COINCIDENTAL "MAP1" that failed validation ended the scan instead of continuing | one probe binary carried a bogus "MAP1" at offset 44178, ahead of the real map at 83455 |

Fixed 2026-07-28 and pinned by `codex/test/apps/map1-lookup`, which
exercises `map1-find`, `map1-read-entry`, `map1-read-name`, the bsearch
and `map1-find-by-name` against the map the test itself carries. Both
ablations fire: restoring either the old constant or the step of 4 turns
the whole report into `map1: NOT FOUND`.

So `sym`, `addr`, the backtrace naming and symbolic breakpoints had never
resolved a symbol in any binary since they were written. **What remains
genuinely untestable is only the WRITE half**: a successful symbolic
breakpoint puts `0xCC` over the first byte of the named function, so a
test that set one would patch its own running code. That still needs a
target binary that is not the test itself.

**Search, compare and Address Lookup landed 2026-07-28, and Phase 1's
command surface is complete.** Every item in the debugger menu now either
does what it says or reports honestly why it cannot.

**The argument count was the whole of the difficulty, and it dissolved.**
This document writes them as `dbg mem.s <addr> <bytes>` and
`dbg mem.cmp <a1> <a2> <len>` -- two and three arguments against a console
that dispatches one line -- which read as a reason to build a
multi-argument parser. The console's own idiom settles it instead: an
expression moves the cursor, a command acts there, and the window is the
dump length, which is the rule `watch` already used. A search is then a
pattern at the cursor and a compare is the cursor against one other
address. One argument each, and no parser.

The two arguments are read differently, which is why `DebugPending` is a
variant: a search pattern is TEXT parsed as hex digit pairs, a compare
address is an EXPRESSION. The pattern parser reuses `hex-char-val` from
`Dev chapter HexFormat` rather than becoming a fourth spelling of that
table -- it is already CCE-correct, with a contiguous digit arm and a
six-way letter comparison, which is the form ExprEval had to be repaired
into after `0xff` read as 391.

A bad pattern is refused rather than truncated: an odd digit count and a
non-hex character are both typos, and either one silently dropped would
search for something the operator did not ask for and report an address
they would then trust. A compare reports the FIRST difference with both
bytes, not a count.

**`Address Lookup` was worse than unbuilt** -- it was in the menu with no
dispatch arm at all, so the key fell through to the evaluator and answered
`expr: unknown name addr`, while `resolve-addr` had existed all along to
answer it. It takes no argument: it asks what the cursor points at.

`codex/test/apps/dev-mem-find` drives all three through
`handle-console-input` over zeroed buffers it allocates. The needle sits
at +64 behind a decoy at +16 that shares its first byte, so a search
matching one byte finds the wrong address; ablation fired, and that single
line moves from +64 to +16. Both refusals and the cancelled compare are
asserted, and the compare's first-difference reading lands on the decoy
rather than the needle, which is what pins "first" rather than "any".

`addr` answered `<no debug info>` here, and this document explained that
as "there is no MAP1 in a test binary". **That explanation was wrong**:
the map was there and `map1-find` could not find it, for the three
reasons tabulated under Phase 1 above. What the reading separated at the
time was "the command exists" from "the key fell through", which is what
that change claimed and all it claimed. Symbol resolution itself is
exercised as of 2026-07-28 by `codex/test/apps/map1-lookup`.

### Phase 2: Extended Inspectors

**Three constraints on `DiskInspector`, read off the code on 2026-07-28
rather than off this document. The command table above predates the fact
store getting its own partition, and one of its rows is now wrong.**

- **`dbg disk.sb` cannot read sectors 0 and 1.** The table says
  "Superblock decode (sectors 0+1)", which was true when the store began
  at LBA 0. It does not any more: the store lives in a partition of type
  `C0DE1A11-FAC7-4C0D-9E75-C0DEC0DE5EED` and addresses every sector
  relative to that partition's base. On the shipped 16 MB image the base
  is 28639, so sectors 0 and 1 are the protective MBR and the GPT header.
  Read `fact-store-region` for the base, then `read-superblock base`.
  Decoding LBA 0 would answer "no superblock" on a disk that has one.

- **`fact-store-region` must be resolved ONCE and threaded.** It takes no
  arguments, so every mention re-runs it, and it performs a GPT read each
  time. A per-sector call in a fact-log walk is a disk read and an
  allocation per sector -- the heap blow-up that rule 8 exists for. This
  is the same rule the store itself follows: `disk-init` / `disk-load`
  resolve the base once and carry it in the `DiskFactStore`.

- **`dbg disk.facts` must walk HEADERS, not entries.** The obvious
  primitive is `scan-fact-at`, and it is the wrong one for a listing: it
  calls `disk-read-entry-content`, which allocates `nsec * sector-size`
  and materialises the whole content of every fact it passes. A listing
  that only shows kind, length and timestamp should read the one sector
  and take `fl-entry-kind`, `fl-entry-timestamp` and
  `fl-entry-content-len` off the header, which is bounded by one sector
  buffer per entry instead of by the length of the log.

Also worth knowing before scoping the ATA rows: **`codex/os/dev/AtaDebugger.codex`
already exists** and carries `ata-step-read`, `ata-traced-read` and
`ata-verify-sector`. It is cited only by `vga-terminal-demo`, which is
skipped for needing a display and a keyboard, so it has no live caller.
That is the defined-with-no-caller shape, not a missing module -- read it
before writing one.

1. `DiskInspector.codex` -- superblock, fact log -- **done 2026-07-28**
2. `IdtInspector.codex` -- IDT viewer, interrupt counts -- **done 2026-07-28**
   (`codex/test/apps/idt-inspect`)
3. `PerfMonitor.codex` -- heap/stack/tick metrics -- **done** (landed early)
4. New builtins -- **done 2026-07-28**, as six rather than three
   (`codex/test/apps/cpu-builtins`); carried a seed
5. `CpuInspector.codex` -- CR, CPUID, feature decode -- **done 2026-07-28**
   (`codex/test/apps/cpu-inspect`)

**Phase 2 is closed.**

### Phase 3: Automation

1. `Macro.codex` -- macro definition + execution -- **done 2026-07-28**
   (`codex/test/apps/macro-table`, `codex/test/apps/macro-run`)
2. `SerialBridge.codex` -- mirror + remote mode -- **done 2026-07-28**
   (`codex/test/apps/serial-bridge`, `codex/test/apps/serial-mirror`)
3. `dbg ata.stress` -- multi-sector stress test
4. `dbg mem.s/cmp` -- search and compare

### Phase 4: Advanced

1. Hardware watchpoints (DR0-DR3 debug registers)
2. Port access trace ring buffer -- **done 2026-07-28**
   (`codex/test/apps/port-trace`, console half in `codex/test/apps/macro-run`)
3. VGA split-screen (live register view)
4. I/O bitmap breakpoints (TSS modification) -- **NOT ACHIEVABLE as
   specified, measured 2026-07-28.** The processor consults the I/O
   permission bitmap only when CPL is greater than IOPL; at CPL less
   than or equal to IOPL it permits the access and never reads the
   bitmap. Everything in this system runs at CPL 0, so the bitmap is
   never consulted no matter what the TSS holds. Both GDTs carry DPL0
   code and data and NO ring-3 descriptor -- the one codex-vm installs
   at 0xA000 and the runtime one `X86_64Boot` builds at `gdt64-base` --
   and both TSSes have limit 103, a 104-byte TSS with no bitmap in it.
   This row is not unbuilt, it is unreachable: it needs a ring 3 to trap
   FROM, and that is a user mode this OS does not have. Reclassify it as
   an OS item or cut it.

**What the trace ring records, and what it cannot.** `PortTrace.codex`
holds sixty-four entries in the foreword's `RingBuffer`, one Integer per
entry, and `pt-read` / `pt-write` are the only things that write to it.
An access made anywhere else in the system -- a driver, an ISR, the ATA
code -- does not appear and cannot, because nothing traps it. So the
report is a record of what the OPERATOR did, which is a smaller claim
than the row above reads, and worth stating so nobody diagnoses a silent
driver from an empty ring.

**This paragraph said item 4 would change that, and it was wrong.** It
named the I/O permission bitmap as the mechanism that turns another
module's `in` or `out` into something the debugger can observe, and on
this machine the bitmap never runs: the processor consults it only when
CPL is greater than IOPL, and everything here is CPL 0. Item 4's row
above carries the measurement. The correction matters because the
original sentence made the ring's limit read as temporary, which would
send whoever wants system-wide port capture at a TSS bitmap that cannot
deliver it.

Item 1 is blocked on compiler work in the same way item 5 of Phase 2
was: reading or writing DR0-DR3 needs `mov dr` builtins, which need a
seed cycle, and delivering the `#DB` needs a vector-1 handler that
records something a guest can read. Neither exists.
