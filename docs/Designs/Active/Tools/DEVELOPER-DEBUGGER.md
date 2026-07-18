# Codex Developer Quire — Bare-Metal Hardware Debugger

## Status

**Five inspectors are live**, in `codex/os/dev/`:

| Shipped | |
|---|---|
| `HexFormat.codex` | `MemInspector.codex` |
| `IoInspector.codex` | `AtaDebugger.codex` |
| `PerfMonitor.codex` | |

The `dbg` command dispatch is live too — in `apps/works/DevDebugger.codex`,
not the `ShellDebug.codex` this document names.

**Eight named modules do not exist**: `DevState.codex`, `ExprEval.codex`,
`IdtInspector.codex`, `CpuInspector.codex`, `DiskInspector.codex`,
`Macro.codex`, `SerialBridge.codex`, and `ShellDebug.codex`. The three
CPU-state builtins (`cpu-read-cr0`, `cpu-read-cr3`, `cpu-cpuid`) do not
exist either — so the entire `dbg cpu.*` command family below is
unimplemented, and every command table that depends on a missing module
(`dbg idt.*`, `dbg disk.*`, `dbg macro.*`, `dbg serial.*`) is
specification, not behaviour.

**Next: `ExprEval.codex` and `DevState.codex`.** They are the two Phase-1
modules that never landed, and they are the ones the shipped inspectors
are poorer for lacking — every command below promises expression
arguments (`dbg mem vga-base+0xA0 32`, `$` for last result), and without
ExprEval that promise is unmet across the whole tool. DevState is what
gives watches, macros, and `last-result` somewhere to live. Build those
two before adding a sixth inspector.

## Quire Placement

New quire: **`codex.os.dev`**, cited as **`Dev`**.

```
Foreword  <--  Kernel  <--  OS  <--  Dev  <--  Works
```

Dev depends on Foreword (CCE, Maybe, Hamt, StringBuilder) and Kernel (Vga, Console, DiskFacts, Keyboard). It does NOT depend on Trust, Verify, or Net — the debugger must work when those subsystems are broken.

Works gains `ShellDebug.codex` that wires Dev commands into the shell dispatch.

## Core Modules

| File | Cite | Purpose | Built? |
|------|------|---------|--------|
| `codex.os.dev/HexFormat.codex` | `Dev chapter HexFormat` | Hex formatting: byte-to-hex, addr-to-hex, dump lines, ASCII sidebar | **yes** |
| `codex.os.dev/DevState.codex` | `Dev chapter DevState` | Unified debugger state record | no |
| `codex.os.dev/ExprEval.codex` | `Dev chapter ExprEval` | Inline arithmetic: hex literals, +, -, *, /, &, |, ^, <<, >> | no |
| `codex.os.dev/MemInspector.codex` | `Dev chapter MemInspector` | Hex dump, search, compare, watch | **yes** |
| `codex.os.dev/IoInspector.codex` | `Dev chapter IoInspector` | Port read/write, ATA register decode, PCI config | **yes** |
| `codex.os.dev/AtaDebugger.codex` | `Dev chapter AtaDebugger` | Step-through ATA PIO protocol with full status decode | **yes** |
| `codex.os.dev/IdtInspector.codex` | `Dev chapter IdtInspector` | IDT entry viewer, interrupt counts, fault info | no |
| `codex.os.dev/CpuInspector.codex` | `Dev chapter CpuInspector` | CR0/CR3/CR4, CPUID leaf dump | no |
| `codex.os.dev/DiskInspector.codex` | `Dev chapter DiskInspector` | Raw sector dump, superblock viewer, fact log browser | no |
| `codex.os.dev/PerfMonitor.codex` | `Dev chapter PerfMonitor` | Tick delta, heap HWM, stack depth | **yes** |
| `codex.os.dev/Macro.codex` | `Dev chapter Macro` | Named command sequences | no |
| `codex.os.dev/SerialBridge.codex` | `Dev chapter SerialBridge` | Mirror debugger I/O to COM1 for remote debugging | no |
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

If there's an off-by-one, step 5 shows `43 4F` but step 6 shows the buffer starting at `4F 44` — the mismatch is immediately visible. Minutes instead of hours.

### Interrupt / IDT Inspector

| Command | Syntax | Description |
|---------|--------|-------------|
| `dbg idt` | `dbg idt [vec]` | Display IDT entry: offset, selector, type, DPL, present |
| `dbg idt.counts` | `dbg idt.counts` | Tick counter + keyboard buffer state |
| `dbg idt.fault` | `dbg idt.fault` | Watchdog ring buffer: last N RIPs when watchdog fired |
| `dbg idt.irq` | `dbg idt.irq` | PIC ISR/IRR: pending/active IRQ lines |

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
| `dbg disk` | `dbg disk <lba>` | Hex dump one sector |
| `dbg disk.sb` | `dbg disk.sb` | Superblock decode (sectors 0+1) |
| `dbg disk.facts` | `dbg disk.facts [start] [count]` | Walk fact log entries |
| `dbg disk.sc` | `dbg disk.sc` | ATA sector count |
| `dbg disk.cmp` | `dbg disk.cmp <lba1> <lba2>` | Compare two sectors |

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

## Implementation Phases

### Phase 1: Foundation — mostly done, two gaps

Core that would have caught the ATA off-by-one in minutes:

1. `HexFormat.codex` — formatting utilities — **done**
2. `DevState.codex` — debugger state — **NOT DONE (next)**
3. `ExprEval.codex` — hex literal + arithmetic — **NOT DONE (next)**
4. `MemInspector.codex` — `dbg mem`, `dbg mem.b/w/d/q`, `dbg mem.w!` — **done**
5. `IoInspector.codex` — `dbg io.in/out`, `dbg io.ata` — **done**
6. `AtaDebugger.codex` — `dbg ata.read`, `dbg ata.verify`, `dbg ata.step` — **done**
7. Dispatch + integration — **done**, as `apps/works/DevDebugger.codex`

Items 2 and 3 are the next work on this design. Until ExprEval lands,
every address argument is a bare literal and the named-constant /
arithmetic / `$` syntax specified above does not work.

### Phase 2: Extended Inspectors

1. `DiskInspector.codex` — superblock, fact log
2. `IdtInspector.codex` — IDT viewer, interrupt counts
3. `PerfMonitor.codex` — heap/stack/tick metrics — **done** (landed early)
4. New builtins: `cpu-read-cr0`, `cpu-read-cr3`, `cpu-cpuid` — none exist
5. `CpuInspector.codex` — CR, CPUID, feature decode — blocked on 4

### Phase 3: Automation

1. `Macro.codex` — macro definition + execution
2. `SerialBridge.codex` — mirror + remote mode
3. `dbg ata.stress` — multi-sector stress test
4. `dbg mem.s/cmp` — search and compare

### Phase 4: Advanced

1. Hardware watchpoints (DR0-DR3 debug registers)
2. Port access trace ring buffer
3. VGA split-screen (live register view)
4. I/O bitmap breakpoints (TSS modification)
