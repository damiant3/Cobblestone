# Operator's Manual

How to build, test, install, and debug the Codex compiler. For the
runtime memory model, register conventions, and allocator internals,
see `docs/ArchitectsSketchbook.md`.

## Build Process

The build script (`build/build.ps1`) runs the full verification
pipeline. Each phase must pass before the next begins.

### Phases

1. **Clean**: Remove `build-output/` and temporary files.

2. **Source concat**: `build/concat-codex-self.ps1` scans `codex/compiler/`
   for `.codex` files, resolves foreword dependencies transitively, and
   emits a single concatenated source file. Foreword chapters are
   prefixed with `Foreword--`. Compiler chapters in subdirectories are
   prefixed with the directory name (e.g., `Emit--`).

3. **CDX build**: Boot the seed (`seed/Codex.cdx`) in the VM
   (codex-vm by default), feed it the concatenated source over serial,
   receive the compiled CDX binary. This produces the SUT (System Under
   Test).

4. **Sign**: Compile and run an inline Ed25519 signing program to
   embed a signature in the CDX header (bytes 40-135).

5. **Canary**: Compile `codex.test/hello.codex` with the SUT and verify
   runtime output matches `hello.expected`. This confirms the SUT can
   compile and run a simple program.

6. **Semantic equivalence**: The SUT emits the source in TEXT mode
   (stage1.codex). `build/compare-codex-semantic.ps1` parses both
   source and stage1, normalizes whitespace/parens/operator aliases,
   and compares every definition body. Mismatches indicate the emitter
   lost information.

7. **Text fixed point**: The SUT emits stage1.codex, then emits
   stage1.codex again to produce stage2.codex. SHA-256 of stage1 must
   equal SHA-256 of stage2. This proves the text emitter is idempotent.

8. **CDX fixed point**: The SUT compiles source → stage1.cdx. Then
   stage1.cdx compiles source → stage2.cdx. SHA-256 of stage1.cdx must
   equal SHA-256 of stage2.cdx. This proves the compiler is a fixed
   point of itself — the binary it produces is identical to itself.

9. **Test battery**: `build/test.ps1` runs all samples in `codex/test/`.
   Each sample has a sidecar (`.expected` for success, `.failing` for
   expected errors, `.skip` for skipped). Runs parallel VM instances.

### Quick Commands

```powershell
build/test.ps1                        # Sample battery (~2-5s per sample)
build/test.ps1 -Jobs 4                # Parallel test (4 batch slots)
build/test.ps1 -All                   # Include foreword + app tests
build/test.ps1 -Fatal                 # Include fatal (GPF/exception) tests
build/build.ps1                       # Full pipeline (all gates)
```

## Test Harness

### Two-Phase Architecture

**Phase 1 — Batch compile.** One VM per job slot, REPL loop reuse.
Each slot boots `seed/Codex.cdx` once, then compiles multiple test
sources sequentially over the persistent serial connection. The
compiler resets its heap between compilations automatically.

**Phase 2 — Run.** Each compiled CDX with a `.expected` sidecar is
booted in its own VM. Serial output is captured and compared
byte-for-byte against the expected output.

### Sidecars

| File | Meaning |
|------|---------|
| `foo.expected` | Compile must succeed; runtime output must match |
| `foo.failing` | Compile must fail with listed CDX error codes |
| `foo.diag` | Compile must succeed and emit each listed CDX code at any severity (warning/info/error); one per line. Tests warnings/infos. |
| `foo.skip` | Skipped entirely (first line = reason) |
| `foo.slow` | Skipped unless `-Slow` (first line = reason) |
| `foo.fatal` | Skipped unless `-Fatal` (kills VM at runtime) |
| `foo.stdin` | Pumped to VM serial after boot (runtime input) |
| `foo.disk` | Attached as IDE disk image |

### Test Results

Results are written to `test-output/_results/`, one file per test.
Each file contains a tab-separated line: `STATUS\tNAME\tDETAIL`.

| Status | Meaning |
|--------|---------|
| PASS_EXPECTED | Compiled, ran, output matched |
| PASS_FAILING | Compiled failed with expected error codes |
| PASS_UNVERIFIED | Compiled but no `.expected` sidecar |
| FAIL_COMPILE | Compile failed unexpectedly |
| FAIL_OUTPUT | Ran but output didn't match |
| FAIL_RUNTIME | VM crashed or timed out |
| FAIL_EXPECTED_BUT_COMPILED | Expected failure but compile succeeded |
| FAIL_WRONG_DIAGNOSTIC | Failed but wrong error codes |
| SKIPPED | Skipped (with reason) |

## VM Configuration

All scripts use `build/vm-config.ps1` for shared VM setup.

### codex-vm (default)

`tools/codex-vm.exe` — a ~6000-line C program using Windows Hypervisor
Platform (WHP). Build with `tools/build-vm.ps1`.

#### CLI Flags

```
codex-vm -kernel file.cdx [options]
```

| Flag | Default | Description |
|------|---------|-------------|
| `-kernel <file>` | (required) | CDX or multiboot kernel to boot |
| `-mem <MB>` | 2048 | Guest RAM in megabytes |
| `-input <file>` | — | Pre-load file into serial ring buffer (source input) |
| `-output <file>` | — | Capture serial output to file |
| `-disk <file>` | — | Attach IDE disk image (read/write, flushed to host) |
| `-headless` | off | Suppress VGA/GOP display window |
| `-uefi` | off | UEFI firmware mode (ConOut/ConIn, GOP, Block I/O, memory map, runtime services, auto-extract PE from GPT images) |
| `-gop` | off | Activate GOP framebuffer (default 640x480) |
| `-gop-width <N>` | 640 | GOP framebuffer width (implies `-gop`) |
| `-gop-height <N>` | 480 | GOP framebuffer height (implies `-gop`) |
| `-smp [N]` | 1 | Enable multi-core: N virtual processors (1-16, default 4 if N omitted). Creates WHP VPs, LAPIC, MADT with per-core entries. Core count written to GPA 0xFF8; boot code reads it to decide whether to send INIT/SIPI. |
| `-portfwd <host:guest>` | — | TCP port forwarding from host to guest NIC (repeatable, max 16). Example: `-portfwd 8080:80` |
| `-debug` | off | Interactive debugger shell on breakpoints and single-step |
| `-break <name>` | — | Patch INT3 at named function entry (implies `-debug`, repeatable) |
| `-map <file>` | auto | Symbol map file for address resolution. Auto-probed: `<kernel>.map`, then `seed/Codex.map` |
| `-watch <0xADDR>` | — | Hardware watchpoint via page protection |
| `-watch-size <N>` | 8 | Watchpoint region size (max 64 bytes) |
| `-screenshot <file>` | — | Save GOP framebuffer as BMP on exit |
| `-screenshot-delay <ms>` | 0 | Delay before screenshot capture |
| `-args <string>` | — | Boot arguments string (accessible to guest) |
| `-trace-file <file>` | — | Write execution trace to file |

Environment: `CODEX_VM_NO_TIMER=1` disables PIT timer interrupts.

#### Emulated Hardware

**CPU and SMP.** WHP-accelerated x86-64 (long mode, full hardware
virtualization). Shadow register file works around WHP GPR corruption.
Multi-core via `-smp N`: each AP gets its own WHP virtual processor
and host thread. INIT/SIPI startup sequence: guest writes AP entry
address to GPA 0x1000 and per-core stack addresses to a stack table;
the LAPIC ICR write triggers AP launch. APs start in 64-bit long mode
with their LAPIC ID in R15.

**LAPIC** (0xFEE00000). Per-core local APIC: ID register, SIVR, EOI,
ICR (lo/hi). SIPI delivery creates AP threads. Used for SMP boot and
inter-processor interrupts.

**IOAPIC** (0xFEC00000). 24 redirection entries. IRQ routing from
PCI devices and ISA sources to the BSP.

**PCI Bus.** 3 devices on bus 0:

| Slot | Device | Class | BAR | IRQ |
|------|--------|-------|-----|-----|
| 0 | Bochs VGA (1234:1111) | Display | 0xFD000000 | — |
| 1 | xHCI USB (1033:0194) | USB 3.0 | 0xFE800000 | 10 |
| 2 | Intel HDA (8086:2668) | Audio | 0xFE000000 | 11 |

Config space read/write via ports 0xCF8/0xCFC.

**Display.** Three modes:
- **VGA text** (80x25) via port 0x3D4/0x3D5, text buffer at 0xB8000
- **Bochs VBE** via ports 0x1CE/0x1CF (index/data), guest-initiated mode switch
- **GOP framebuffer** at GPA 0xBF000000 (in RAM — fast writes, no MMIO trap). Guest writes 32-bit XRGB pixels directly. The VM renders from a shadow copy to a Win32 window on a separate thread. Three preset modes: 640x480 (0), 800x600 (1), 1024x768 (2). Custom sizes via `-gop-width`/`-gop-height`. Window title changes to "Codex Spark" in GOP mode.

**GPU Triangle Rasterizer.** Host-side native-speed rasterizer
accessed via I/O ports 0x400-0x40F:

| Port | Direction | Function |
|------|-----------|----------|
| 0x400 | OUT | Rasterize N triangles from guest command buffer |
| 0x401 | OUT | Clear framebuffer to XRGB color |
| 0x402 | OUT | Clear depth buffer |
| 0x403-0x405 | OUT | Set light direction (x/y/z, fixed-point /1000) |
| 0x406 | OUT | Set eye direction X (Y/Z copied from light) |
| 0x407 | OUT | Set texture guest address |
| 0x408-0x409 | OUT | Set texture width/height |
| 0x40A | OUT | Commit texture upload (copy from guest RAM) |

Includes depth buffering, per-vertex normals, diffuse+specular
lighting, texture mapping with bilinear filtering, procedural Earth
texture generation, and atmospheric glow post-processing.

**Serial I/O.** Ring buffer at GPA 0x500000 (1 MB). Source input is
pre-loaded from `-input` file. Output captured from guest UART writes
to `-output` file. Ports 0x3F8-0x3FD (COM1). Protocol: guest reads
input from ring buffer; writes output bytes; harness captures until
EOT or VM exit.

**NE2K NIC.** NE2000-compatible ISA NIC at I/O base 0x300. User-mode
NAT stack with IP 10.0.2.15, gateway 10.0.2.2, DNS 10.0.2.3. Handles
ARP (responds for gateway), DHCP (offers 10.0.2.15/24), DNS (forwards
to host), and TCP/UDP forwarding. Port forwarding via `-portfwd` for
host-to-guest TCP connections.

**IDE Disk.** PIO-mode IDE controller at ports 0x1F0-0x1F7, 0x3F6.
Supports IDENTIFY, READ SECTORS, WRITE SECTORS, and FLUSH CACHE.
Writes are flushed to the host image file (durable disk writes).

**xHCI USB 3.x Controller.** Full command ring, event ring, and
transfer ring processing. Three device slots:
- Slot 1: Mass storage (bulk IN/OUT, SCSI READ/WRITE to RAM disk)
- Slot 2: HID keyboard (interrupt IN, generates scan codes)
- Slot 3: UVC camera (isochronous transfers, MJPEG frames)

**Intel HDA Audio.** CORB/RIRB command interface, output stream DMA.
48 kHz 16-bit stereo PCM. Host-side playback via Windows waveOut API.

**HPET.** High Precision Event Timer at 0xFED00000. Main counter
driven by QueryPerformanceCounter. Comparator 0 with periodic mode
and interrupt generation.

**Timers and Interrupts.** Dual 8259 PIC (master 0x20, slave 0xA0).
PIT channel 0 at port 0x40 (host-driven periodic tick). CMOS RTC at
port 0x70/0x71 (real host time). PC speaker via Windows Beep().

**PS/2 Keyboard and Mouse.** Keyboard at port 0x60/0x64 with scan
code queue. Mouse data written to GPA 28684 (kernel metadata cell
`key-buffer-addr + 4`) as 3-byte packet (buttons, dx, dy).

**ACPI.** RSDP at 0xE0000, RSDT, FADT (SCI on port 0x2000, PM timer
at 0x2004), MADT (LAPIC entries per core + IOAPIC), DSDT stub.

**SMBIOS.** Entry point at 0xF0000. Type 0 (BIOS), Type 1 (System),
Type 127 (end-of-table). Vendor/product: "Codex"/"codex-vm".

**UEFI Firmware Emulation** (when `-uefi` or booting a GPT image).
Trap-page dispatch at GPA 0xF1000 (HLT opcodes — guest calls trap,
VM intercepts the HLT). Supported protocols:

| Protocol | Functions |
|----------|-----------|
| ConIn | Reset, ReadKeyStroke, ReadKeyStrokeEx |
| ConOut | Reset, OutputString, TestString, QueryMode, SetMode, SetAttribute, ClearScreen, SetCursorPosition, EnableCursor |
| Boot Services | AllocatePages, FreePages, GetMemoryMap, AllocatePool, FreePool, ExitBootServices, Stall, SetWatchdogTimer, HandleProtocol, LocateHandle, LocateProtocol |
| Runtime Services | GetTime (host RTC) |
| GOP | QueryMode, SetMode, Blt |
| Block I/O | Reset, ReadBlocks, WriteBlocks, Flush |
| Simple File System | OpenVolume, Open, Close, Read, GetInfo, SetPosition |

Auto-extracts PE from GPT images: scans for EFI System Partition,
locates `EFI/BOOT/BOOTX64.EFI`, loads the PE into memory.

**Guest-to-Host Communication.** Guest reads host-provided values
from fixed GPAs:

| GPA | Width | Content |
|-----|-------|---------|
| 0xFE8 | 8 bytes | Guest RAM size in bytes (for dynamic RSP) |
| 0xFF0 | 8 bytes | Boot arguments string pointer |
| 0xFF8 | 4 bytes | SMP core count (0 or 1 = single-core) |

#### Interactive Debugger

Run with `-debug` (and optionally `-break <fn>` and `-map <file>`)
for a command shell on breakpoints. See the Native Debugging Toolkit
section for commands and workflows.

### QEMU (fallback)

Set `$env:USE_QEMU=1` to force QEMU. Required for GDB watchpoints
and TCG tracing.

```powershell
$env:USE_QEMU = 1
build/test.ps1 -Jobs 4
```

`kernel-irqchip=off` required for bare-metal operation under QEMU.

### Renode (cross-architecture board testing)

Renode v1.16.1 provides cycle-accurate simulation for ARM64 and
RISC-V 64 targets. Install the runtime **once per box** — extract the
`renode-1.16.1.windows-portable-dotnet.zip` to `C:\Renode` (so
`C:\Renode\renode.exe` exists), or set `$env:CODEX_RENODE_HOME` to any
Renode install dir. Every workspace finds it via `build/renode-config.ps1`
(`Get-RenodeExe`), which probes `CODEX_RENODE_HOME`, then `C:\Renode`,
then `%LOCALAPPDATA%\Renode`, then a legacy `tools/renode/` copy. Only the
board `.repl` files under `tools/renode/codex/` are tracked per-workspace;
the ~120 MB runtime is not.

**Board definitions** (in `tools/renode/codex/`):

| Board | CPU | UART | RAM |
|---|---|---|---|
| `codex-arm64.repl` | Cortex-A53 (GICv3) | PL011 @ 0x09000000 | 1GB @ 0x40000000 |
| `codex-riscv64.repl` | RV64GC (PLIC/CLINT) | NS16550 @ 0x10000000 | 1GB @ 0x80000000 |

**Quick test** (requires plugs built first):

```powershell
build/test-boards.ps1                    # Both boards
build/test-boards.ps1 -Arch arm64        # ARM64 only
build/test-boards.ps1 -Arch riscv64      # RISC-V only
```

**Pipeline**: source → compile.ps1 (IR) → arm64/riscv plug → wire
protocol → compile-arm64/riscv.ps1 (ELF) → Renode (UART capture).

**Setup from scratch**:
1. Download the Renode v1.16.1 `windows-portable-dotnet` zip from GitHub releases
2. Extract box-wide to `C:\Renode` (or set `$env:CODEX_RENODE_HOME`)
3. The `.repl` board files under `tools/renode/codex/` are already tracked
4. Build plugs: `codex/plugs/arm64/build.ps1`, `codex/plugs/riscv/build.ps1`
5. Run `build/test-boards.ps1`

**Key fixes for Renode compatibility** (vs QEMU):
- ARM64: exception vector table required (VBAR_EL1/EL3), PL011
  needs explicit UARTLCR_H + UARTCR init, 32-bit UART writes
- ARM64: all runtime helpers must use `a64-emit-block` (not `a64-emit`)
- RISC-V: heap register must be S1/x9 (not t3/x28 which collides
  with temp allocator)

## Self-Host Compilation Protocol

`build/compile.ps1` boots the compiler kernel in a VM and
communicates over serial:

1. Wait for `READY` on control channel (ch1).
2. Send mode header (`CDX`, `TEXT`, `IR`, etc.) on data channel.
3. Send foreword library bytes (transitively resolved).
4. Send source bytes.
5. Send EOT (0x04).
6. Read diagnostic lines until `SIZE:<n>` (success) or
   `CODEGEN-HALTED` (failure).
7. On success, read `n` raw bytes of binary output.

### Compile Modes

| Mode | Output |
|------|--------|
| `CDX` | CDX binary |
| `CDX repl` | CDX binary, REPL loop enabled |
| `TEXT` | Codex source text |
| `IR` | IR text dump |
| `MEASURE` | Phase metrics |

Append flags: `TEXT prose`, `CDX repl`, `CDX poison`

Container formats (ELF, PE, IMG) are produced by post-compile
plugs in `codex/plugs/`. See `docs/Designs/Active/Compiler/EmitterExodus.md`.

## Seed Management

The canonical seed is `seed/Codex.cdx` — the signed, self-sustaining
CDX binary, bootable via codex-vm or QEMU multiboot.

### Seed Rebuild Procedure

**Pre-conditions:**
- All source changes are submitted
- The change justifies a rebuild (codegen change, new builtin, foreword
  change that affects compilation)

**Steps:**
1. Run the full build: `build/build.ps1`. All phases must PASS.
2. Install new seed: `Copy-Item build/output/Sut.cdx seed\Codex.cdx -Force`
   Use `build/output/Sut.cdx` — the signed SUT. Do NOT use
   `build-output/bare-metal/Codex.cdx` (unsigned boot kernel).
3. Self-verify: `build/test-self-verify.ps1`. Must print
   "THE SEED VERIFIES ITSELF".
4. Capture digest: `Get-FileHash -Algorithm SHA256 seed\Codex.cdx`
5. Submit to Perforce.

**Rules:**
- Never skip the full build. Never skip self-verify.
- One seed per CL. CDX is primary.
- The bootable image (`seed/Codex.img`) is a separate distribution
  artifact built by `build/build-boot-img.ps1`. It is NOT part of
  the seed rebuild.
- Signing is automatic.
- Never `git add -A`. Never force-push.

**Codegen changes need a one-pass fixed point.** Step 2's "install
`Sut.cdx`" is correct ONLY when the build prints
`(SUT === stage1 — hard fixed point in one pass)`. A change to code
generation (e.g. `emit-prologue`) built from a pre-change seed is
**two-pass**: the stage0 seed lacks the new codegen, so its `Sut` carries
the change only in its emit logic, not yet in its own function bodies —
`Sut != stage1`, and the real fixed point is `stage1` (`NewSeed.cdx`, which
is unsigned). Installing `Sut.cdx` there leaves a seed that is one pass
short: it compiles correctly but is not byte-identical to itself
(`seed != seed-compiles-seed`), so it must not be trusted or copied up. The
fix is simply to **rebuild again** from that once-built seed — the second
build converges one-pass with the change baked into the seed's own
prologues, and `Sut.cdx` is then the signed fixed point. Always rebuild
until the build reports one pass before submitting a codegen seed.

(The CDX fixed-point check compares content ignoring the signature bytes,
so a signed `Sut` and the unsigned `stage1` still register as one pass when
their code is identical. On copy-up, the gate is `Sut === seed` rebuilt on
the *target* workspace — see `docs/Agents/PerforceProcess.md`.)

## Status Server

`tools/status-server.ps1` serves a single-page dashboard at
`http://localhost:8080/` with live data from the workspace and Perforce.

```powershell
pwsh tools/status-server.ps1            # default port 8080
pwsh tools/status-server.ps1 -Port 9090 # custom port
```

Displays: seed CDX info, test battery results, module counts by quire,
recent changelists. Auto-refreshes every 30 seconds.

## Symbol Map

`seed/Codex.map` is a text file mapping code addresses to function
names, emitted during each seed build. Format:

```
# Codex Symbol Map
# Address         Size  Name
0x00100114 7 __alloc
0x0010011B 251 __str_concat
...
0x002F56FB 614 lookup-expr-type
```

Each line: hex address, decimal byte size, function name. Use it to
identify crash locations from `!EXC` diagnostic output:

```powershell
# Look up a crash RIP in the map
$rip = 0x2f588b
$lines = Get-Content seed\Codex.map | Where-Object { $_ -match '^0x' }
foreach ($line in $lines) {
    if ($line -match '^(0x[0-9a-fA-F]+)\s+(\d+)\s+(.+)$') {
        $addr = [Convert]::ToInt64($matches[1], 16)
        if ($addr -le $rip -and ($addr + [int]$matches[2]) -gt $rip) {
            "$($matches[3]) at $($matches[1]) (offset +$($rip - $addr))"
        }
    }
}
```

The map is rebuilt whenever the seed is rebuilt. It tracks the
compiler's own functions — not compiled test programs. Test CDX
binaries emit their own MAP block in the build log (visible between
`MAP:` and `MAP-END` lines).

## Native Debugging Toolkit

All debugging uses codex-vm and the PowerShell harness. No GDB, no
WSL, no external tools. The compiler embeds a binary MAP1 symbol map
in every CDX (2600+ functions, ~79KB), and the harness resolves
addresses automatically.

### Crash Reports

When a crash occurs during batch compilation, the harness prints a
resolved crash report:

```
CRASH in lookup-expr-type+0x42 (page fault, CR2=0x2eeef7000000)
  RIP   0x0027484a  lookup-expr-type+0x42
  callR 0x00274200  check-chapter+0x1a0
  R10   0x01a71cb0  (heap @ 21.4 MB)
  Stack trace (heuristic):
    S[0] 0x00274200  check-chapter+0x1a0
    S[3] 0x00261f10  compile-type-check+0x48
```

The `!EXC` line from the guest's exception handler includes RIP, all
callee-saved registers, CR2, callR (return address), and 16 stack
qwords. `Format-CrashReport` (vm-config.ps1) resolves every value in
the code range (0x100000-0x400000) to a function name.

### Manual Address Lookup

```powershell
build/resolve-rip.ps1 0x2748af                # single address
build/resolve-rip.ps1 0x100114 0x200000        # multiple
```

Or from any script that sources vm-config.ps1:

```powershell
Resolve-Rip -Rip 0x2748af                     # -> "function+0xNN"
Resolve-Name -Name "lookup-expr-type"          # -> 0x2F56FB (address)
```

### Breakpoints by Function Name

Patch INT3 at a function's entry point. The guest exception handler
fires `!EXC=03` (vector 3) and dumps register state.

```powershell
build/compile.ps1 -Src foo.codex -Out foo.cdx -Log foo.log `
    -Break "lookup-expr-type"
```

Output:
```
BREAK: patched INT3 at lookup-expr-type+0x0 (0x2F56FB, orig=0x4C)
  CRASH in lookup-expr-type+0x1 (breakpoint)
    RIP   0x002F56FC  lookup-expr-type+0x1
    R10   0x00c00000  (heap @ 6 MB)
    RDI   0x00000010
```

Exit code 5 = breakpoint hit (vs 4 = real crash).

### Interactive Debugger

Run codex-vm directly with `-debug` (and optionally `-map <file>.map`)
to get an interactive debug shell on breakpoints and single-steps.

```
tools/codex-vm.exe -kernel build-output/bare-metal/Codex.cdx ^
    -input input.tmp -output out.tmp -mem 2048 -headless ^
    -debug -map build/output/reg.map
```

When a breakpoint fires (`-Break` patch or runtime `b` command), the
debugger prints registers with symbol resolution and waits for commands:

```
--- Break at 0x12f1de <desugar-def+0> ---
  RIP=0x12f1de <desugar-def+0>
  RSP=00000000007ffa08 RBP=00000000007ffa30 ...
  R13=00000000006ff325
dbg> _
```

**Commands:**

| Command | Description |
|---------|-------------|
| `s` / `step` | Single-step one instruction (sets TF) |
| `c` / `continue` | Resume until next breakpoint |
| `r` / `regs` | Dump all registers with symbol lookup |
| `m <addr> [len]` | Hex+ASCII memory dump (default 64 bytes) |
| `x <addr>` | Read one qword at address |
| `bt` / `backtrace` | Walk RBP chain (resolved frames) |
| `stack` | Dump 16 stack slots with symbol lookup |
| `b <fn\|0xaddr> [if reg=val]` | Set breakpoint (optional condition) |
| `w <addr> [size]` | Set memory watchpoint |
| `sym <name>` | Look up symbol address |
| `q` / `quit` | Exit VM |

**Conditional breakpoints** let you skip high-frequency call sites and
only break when a register matches:

```
dbg> b desugar-def if rdi=0x705320
  conditional: rdi == 0x705320
  breakpoint 0 at 0x12f1de <desugar-def+0>
```

The debugger skips the breakpoint when `rdi != 0x705320` by restoring
the original byte, single-stepping past, and re-patching.

**Workflow: tracing a field corruption**

1. Break at the function that builds the record (`-Break "desugar-def"`)
2. `regs` to see the input parameter (rdi = Def pointer)
3. `x <rdi+0>` to read the Def's first field (ann in CCE order)
4. `step` through the function, watching how field values flow
5. After the record constructor, `x <r10_before+16>` to verify the
   declared-type field was stored correctly
6. Or use `w <addr>` to set a memory watchpoint on the field slot

**Build codex-vm with debugger:**

```powershell
tools/build-vm.ps1   # standard build includes debugger
```

The debugger compiles into the same binary — `-debug` activates it.
Without `-debug`, behavior is unchanged (breakpoints print and continue
as before).

### Debug Compile Mode

Emit phase markers during compilation. When a crash occurs, the
last `DBG:` line in the log identifies which phase was active.

```powershell
build/compile.ps1 -Src foo.codex -Out foo.cdx -Log foo.log `
    -DebugMode
```

Log output:
```
DBG:frontend src=1204413
DBG:emit defs=412
SIZE:2176384
```

### Workflow: Investigating a Crash

1. **Read the crash report.** The harness prints resolved function
   names. Start with the RIP function and the heuristic stack trace.

2. **Read the code.** `resolve-rip.ps1` gives you the function.
   Read that function in the compiler source. Form a theory.

3. **Set a breakpoint.** Use `-Break "suspect-function"` to confirm
   the function is reached and inspect register state at entry.

4. **Use debug mode.** `-DebugMode` shows phase progression. If the
   crash is in emit, the last `DBG:emit` line narrows the window.

5. **Run the poison build.** If you suspect uninitialized memory,
   build a poison seed (see Poison-Alloc section below) and run
   the tests. `CR2=0xCDCDCDCDCDCDCDCD` = uninitialized field.

### compile.ps1 Debug Flags

| Flag | Purpose |
|------|---------|
| `-Break "name"` | INT3 at function entry; use with codex-vm `-debug` for interactive shell |
| `-DebugMode` | Phase markers (`DBG:frontend`, `DBG:emit`) |
| `-Poison` | 0xCD fill in `__alloc` (catches uninitialized fields) |
| `-Repl` | REPL loop (for batch compilation) |
| `-Survey "f:n,..."` | Override per-phase survey multipliers at runtime (no seed rebuild). Fields: `lex-mul`, `parse-mul`, `desugar-mul`, `scope-mul`, `check-mul`, `lower-mul`, `resolve-mul`, `lift-mul`, `headroom`. Appends `survey=...` to the mode line; defaults are byte-identical to `BuildSettings`. Raising a multiplier reserves more deck (safe); lowering too far under-reserves and currently faults rather than bailing cleanly. |

### GDB (Legacy Fallback)

The interactive debugger (`-debug`) covers breakpoints, single-step,
register/memory inspection, conditional breaks, and backtraces. GDB
under WSL with QEMU TCG is a last resort for hardware watchpoints
(DR0-DR3) on specific memory addresses. Rule 6 permits Unix tools
for this purpose only. See `build/gdb-watchpoint.ps1`.

## Poison-Alloc Diagnostic Build

### Background: REPL Batch Stale Data

The test harness compiles multiple tests on a single VM instance via
the REPL loop to avoid per-test VM startup overhead. Between
compilations the REPL loop (X86_64Chapter.codex) resets R10 (bump
allocator), deck-pos, and heap-hwm back to the arena base. It does
NOT zero the freed memory. The `__alloc` helper was three
instructions — `mov rax, r10; add r10, rdi; ret` — returning
uninitialized memory.

This meant the second compilation allocated records on top of the
first compilation's stale data. Any field not explicitly written
after allocation would silently inherit the previous compilation's
value. On first boot the heap was zeroed by hardware, masking the
problem. On second REPL iteration, uninitialized fields contained
live pointers, type tags, or text references from the prior compile.

### Incident Timeline

| CL | Date | Event |
|----|------|-------|
| — | pre-1845 | Intermittent GPFs in REPL batch compilation. Plug compiler crashes under WHPX but not TCG. Crash at `text-compare` called from `bsearch-text-pos` during type lookup, with CR2 pointing into the seed's code section (partial-application trampolines). Six investigation sessions across three agents (see `docs/Test/PLUG-CRASH-INVESTIGATION.md`). |
| 1845 | 2026-05-19 | **Root cause found.** `lookup-expr-type` in Unifier.codex used non-short-circuit `&` to guard a `list-at` access after binary search: `if pos < len & (list-at entries pos).key == k`. When the key was not found (`pos == len`), the right operand executed anyway, reading one element past the list into stale heap. The OOB value — a seed return address shifted 3 bytes — propagated through the type environment and caused a GPF when later dereferenced. Fix: split into nested `if` so the access only executes when `pos < len`. |
| 1885 | 2026-05-20 | **Class fix.** `IrAnd`/`IrOr` now emit conditional jumps instead of bitwise AND/OR. The right operand is only evaluated when the left operand doesn't short-circuit. Eliminates the entire class of non-short-circuit guard bugs. |
| 1927 | 2026-05-21 | **Calloc + REPL hardening.** `__alloc` now zeroes its returned block via `rep stosb` (calloc semantics). REPL loop resets `stdin-eof-flag`, `stdin-eof-settled`, `try-fail-flag`, and `deck-bound-counter` between iterations. `codegen-carry-forward` now carries `vm-profile` (was silently dropped — latent uninitialized field). |

### Audit Results (CL 1927)

**Binary search call sites.** All 8 distinct `bsearch-*` functions
(~20 call sites) across Collections, TypeEnv, TypeChecker, Unifier,
ChapterScoper, LambdaLifting, X86_64Builtins, X86_64Compound were
audited. Every consumer follows the pattern
`if pos < len then if element.key == searchkey then HIT else DEFAULT else DEFAULT`.
No remaining OOB-after-miss vulnerabilities.

**Record construction.** `emit-store-record-fields-by-type`
(X86_64Compound.codex:612) iterates type-definition fields and
matches them against provided constructor fields via
`find-field-local-slot`. If a field name doesn't match (slot = -1),
the field's memory is not written. In a well-typed program every
field is provided, so this path is unreachable — but it would be
the mechanism if a name mismatch existed. The calloc ensures zeros
rather than stale data if this path ever fires.

**`codegen-carry-forward` fix.** This function creates a fresh
CodegenState preserving accumulated code/data but resetting locals.
It was not copying `vm-profile` — the field was uninitialized after
carry-forward. Fixed to carry both `vm-profile` and the new
`poison-alloc` flag.

### Poison Build: 105/105 Pass

On 2026-05-21, the compiler was built with `poison-alloc = True`,
producing a seed where `__alloc` fills every allocation with `0xCD`
instead of zeroing. The full test battery (105 tests, 4 batch REPL
slots) was run against this poison seed.

**Result: 105 pass, 0 fail.**

Every dereference of `0xCDCDCDCDCDCDCDCD` (non-canonical x86-64
address) would be an immediate page fault. Zero failures means every
heap-allocated record in the compiler is fully initialized before
any field is read. There are no latent uninitialized-field
dependencies hiding behind the calloc's zero fill.

### Conclusions

1. **CL 1845 was the real bug.** The non-short-circuit `&` caused
   an OOB read that copied a stale code-section address into the
   type environment. CL 1885 eliminated the entire class.
2. **The calloc is a safety net, not a patch.** The poison build
   proves the compiler initializes all its fields. The zero fill
   prevents future regressions from producing stale-data corruption
   — they'd produce zero-value bugs instead, which are detectable
   but not catastrophic.
3. **The REPL kernel state resets close the remaining exposure.**
   `stdin-eof`, `try-fail-flag`, and `deck-bound-counter` are now
   zeroed between iterations, preventing I/O state leakage.
4. **The poison build is a release gate.** Before any public build,
   run the test battery against a poison seed. If all tests pass,
   the compiler has no uninitialized-field dependencies.

### How to Run a Poison Build

```powershell
# 1. Concat compiler source
build/concat-codex-self.ps1 -CodexDir codex/compiler -OutFile build/output/Codex.codex

# 2. Compile a poison seed (0xCD fill instead of zero)
build/compile.ps1 -Src build/output/Codex.codex `
    -Out build/output/poison-seed.cdx `
    -Log build/output/poison-build.log -Repl -Poison

# 3. Run the full test battery against the poison seed
build/test.ps1 -CodexCdx build/output/poison-seed.cdx -Jobs 4

# Expected: 105 pass, 0 fail.
# Any failure means an uninitialized field was read during compilation.
# The crash CR2 will be 0xCDCDCDCDCDCDCDCD — look up RIP in the
# symbol map to find the function that dereferenced the bad pointer.
```

## Release-to-Public Gate

These steps run ONLY when publishing the seed to the public mirrors
(GitHub, GitLab) — never on routine seed rebuilds or copy-to-main. The
day-to-day gates (text + CDX fixed point, sample battery) already prove
correctness; the items here are public-facing polish and are not usually
needed internally.

1. **Poison build passes** (above) — the seed has no uninitialized-field
   dependencies.
2. **Refresh `seed/Codex.map`.** This is the one artifact that silently
   drifts: the seed is built `-Repl`, and `-Repl` mode does not emit the
   text `MAP:` block that `compile.ps1` captures into `<out>.map`, so
   neither the seed rebuild nor copy-to-main ever refreshes it.
   Regenerate by compiling the compiler source NON-repl with the
   published seed and copying the emitted map:

   ```powershell
   Copy-Item -Force seed/Codex.cdx build-output/bare-metal/Codex.cdx
   build/concat-codex-self.ps1 -CodexDir codex/compiler -OutFile build/output/Codex.codex
   build/compile.ps1 -Src build/output/Codex.codex -Out build/output/SutMap.cdx -Log build/output/map.log
   Copy-Item -Force build/output/SutMap.map seed/Codex.map
   ```

   The non-repl binary differs from the `-Repl` seed only in unnamed
   padding; every named function offset is byte-identical (cross-check
   against the seed's embedded MAP1 if unsure), so the text map is exact.
   The embedded MAP1 in each CDX is authoritative for crash reports
   regardless — the text map only feeds `-Break` and `resolve-rip.ps1`.
