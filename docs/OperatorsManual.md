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

5. **Canary**: Compile `codex/test/factorial.codex` with the SUT and
   verify runtime output matches `codex/test/factorial.expected`. This
   confirms the SUT can compile and run a simple program.

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

10. **Plug gates**: the five native backends (riscv, arm64, elf, pe,
    img) must rebuild clean with the just-proven compiler
    (`plug-binary`), and a representative transpiler subset
    (typescript, python, rust, ptx) must run end-to-end — SUT IR over
    the framed TCP wire through the plug VM to non-empty target text
    (`plug-smoke`). Missing plug CDX builds once and caches; a failing
    smoke run gets one rebuild-and-retry before failing the build.
    The full 53-plug matrix (`codex/plugs/test-plugs.ps1`) remains a
    manual sweep.

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
| `foo.keys` | Scancode timeline (`t:scancode` per line, t = ms since boot) passed as `-keys-file`. This is the **keyboard**; `.stdin` is the **serial ring**. A keyboard read (`uefi-read-key` / `poll-key`) reads the PS/2 key cell and no `.stdin` reaches it — pick by what the code reads. See `docs/ExaminersAssay.md` |
| `foo.disk` | Attached as IDE disk image |
| `foo.smp` | Core count; the test boots with `-smp N` |

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

`tools/codex-vm.exe` — a ~8000-line C program using Windows Hypervisor
Platform (WHP). Build with `tools/build-vm.ps1`.

#### CLI Flags

```
codex-vm -kernel file.cdx [options]
```

| Flag | Default | Description |
|------|---------|-------------|
| `-kernel <file>` | (required) | CDX or multiboot kernel to boot |
| `-mem <MB>` | 3072 | Guest RAM in megabytes. Binaries compiled by seeds older than CL 7209 require more than 2048 (their boot stack lands in the demand-paged range below 2 GB); current seeds boot at any size from ~128 MB up |
| `-input <file>` | — | Pre-load file into serial ring buffer (source input) |
| `-output <file>` | — | Capture serial output to file |
| `-disk <file>` | — | Attach IDE disk image (read/write, flushed to host) |
| `-headless` | off | Suppress VGA/GOP display window |
| `-board-mmio` | off | Commit and map host RAM at the three board register windows that sit above the RAM ceiling: RP2040 SIO (0xD0000000), Cortex-M PPB/SCB (0xE0000000), BCM2711 peripherals (0xFE000000, 9 MB). A board driver's register access then reads back what it wrote — the same fidelity the six sub-3GB boards get from falling inside guest RAM. **Opt-in, because it shadows the HDA and xHCI BARs**: audio and USB are dead while it is on, which a board test does not care about. Required by the pi4 / rp2040 / stm32l4 driver tests; `build/boards-test.ps1` passes it. |
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
| `-wcet <name>` | — | Observe a function's per-invocation dynamic instruction count (repeatable, max 4 — one DR0-DR3 exec breakpoint each; needs `-map`). Prints `WCET-OBS: <fn> max=<n> calls=<k>` on exit. Observation only: no guest byte is modified. |
| `-mouse <script>` | — | Scripted pointer: `t:x,y,btn` events separated by `;` (t = ms from boot, btn bit 0 left / 1 right / 2 middle). Injected straight into the guest, so no host cursor moves and no window takes focus. Works headless. |
| `-mouse-file <file>` | — | Same, read from a file (one event per line, `#` comments). Use for drags, which run to dozens of samples. |
| `-keys-file <file>` | — | Timeline keyboard: `t:scancode` per line, on the same clock as `-mouse`. Lets a script interleave typing with clicks (the older `-keys` fires on a fixed start+interval). |
| `-screenshot <file>` | — | Save GOP framebuffer as BMP on exit |
| `-screenshot-delay <ms>` | 0 | Delay before screenshot capture |
| `-args <string>` | — | Boot arguments string (accessible to guest) |
| `-trace-file <file>` | — | Write execution trace to file |

Environment: `CODEX_VM_NO_TIMER=1` disables PIT timer interrupts.

#### Emulated Hardware

**CPU and SMP.** WHP-accelerated x86-64 (long mode, full hardware
virtualization). Shadow register file works around WHP GPR corruption.
Multi-core via `-smp N`: each AP gets its own WHP virtual processor
and host thread. INIT/SIPI startup sequence: the guest writes the AP
entry address to GPA 0x1000 and the per-core stack addresses to a stack
table at GPA 0xF00, then writes the LAPIC ICR — an INIT IPI followed by
two start-up IPIs, delivered to all cores excluding self. That ICR write
is what launches the APs. They start in 64-bit long mode with their
LAPIC ID in R15, claim a stack by core index, add themselves to the
ready count at cell 4080 with a locked add, and then go looking for
work; the BSP spins on that count (bounded — it gives up and continues
single-core rather than hanging) before carrying on.

**An AP runs processes.** It goes to `__idle_dispatch`, the same routine
the boot processor goes to when it runs out of work, claims a READY slot
out of the process table with a `LOCK CMPXCHG`, and resumes it — on that
process's own stack, with that process's own R10. Six children spawned at
`-smp 4` execute across four cores. `codex/test/smp-dispatch.codex` pins
it, and pins the right thing: every claim by a core whose id is *not*
zero bumps cell 36200, and the boot processor's id is always zero, so a
count above zero is evidence a core other than the BSP took a process out
of the table and executed it. Six children *finishing* would prove
nothing — one core does that.

**A process on an AP is preempted.** The PIT's IRQ reaches the boot
processor alone, so an AP used to run whatever it was given until that
process yielded, blocked or exited. Each AP now arms its own **LAPIC
timer** at bring-up — periodic, on vector 48 — and raises IF, so a
scheduling tick arrives on every core. codex-vm emulates the timer
per-core (`lapic_timers[]`) and injects the vector from each AP's own
thread; the kicker cancels the AP's VP every PIT period, because a
compute-bound core never leaves `WHvRunVirtualProcessor` on its own and a
tick that is never injected is not a clock.

`codex/test/smp-preempt.codex` pins it, and pins the right thing: every
timer interrupt taken on a core whose id is not zero bumps cell 36216, and
the BSP's id is always zero. Six children *finishing* proves nothing here
either — and neither does six children finishing on six cores, because an
AP that runs its child to completion undisturbed has still never been
preempted. The claim is about who can be interrupted, so the evidence is
about who *was* interrupted.

An idle core still spins on `pause` rather than halting, and there is no
work stealing and no affinity (BACKLOG 4.11).

**An SMP test must assert that an AP executed guest code, not that the
work finished** — one core can finish the work. `smp-cores.codex` reads
back the ready count, which only an AP ever increments;
`smp-dispatch.codex` reads cell 36200, which only a core whose id is not
zero ever bumps. Both are evidence a *different* core ran. Before those
tests existed, codex-vm was launching the APs from the host without ever
creating a thread to run them and incrementing the guest's ready count on
their behalf: no AP had executed a single guest instruction, and nothing
in the battery could tell.

**An AP can be heard.** Each application processor has its own TSS and
IST1 emergency stack, so a fault on one produces the standard `!EXC` dump
(tagged with its core id in R15) instead of a silent triple fault. codex-vm
serves **COM1 only** on AP threads: `handle_io()` drives stateful devices
through VP 0's shadow register file and is not safe to re-enter from
another thread. An AP has no business touching the IDE controller; it has
business reporting that it died.

**Do not step RIP past an MMIO instruction by a guessed length.** WHP
does not hand you the length: `VpContext.InstructionLength` reads 0 on
these exits and `MemoryAccess.InstructionByteCount` is the size of the
16-byte instruction *buffer*, not the instruction. Both were taken as
lengths at various points, and both resume mid-instruction — the
signature is a `!EXC=06` (invalid opcode) at an address a byte or two
inside a real instruction. `mmio_insn_len()` decodes the bytes WHP
provides, and returns 0 rather than guessing when it cannot.

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
| 0x403 | IN | GPU rasterizer present (returns 1) |
| 0x404-0x406 | OUT | Set light direction (x/y/z, fixed-point /1000) |
| 0x407 | OUT | Set eye direction X (Y/Z copied from light) |
| 0x408 | OUT | Set texture guest address |
| 0x409-0x40A | OUT | Set texture width/height |
| 0x40B | OUT | Commit texture upload (copy from guest RAM) |
| 0x40C | OUT | Asset load: guest address of a null-terminated host path |
| 0x40D | OUT | Asset load: guest destination address |
| 0x40E | OUT | Fade-clear framebuffer toward an XRGB color |
| 0x417 | OUT | Asset load: execute (reads the host file into guest RAM) |
| 0x40E | IN | Asset load: low 32 bits of bytes loaded |
| 0x40F | IN | Asset load: high 32 bits of bytes loaded |

The light/eye/texture rows above were off by one until 2026-07-13. The
asset-load execute is 0x417 and **not** 0x40E, which the OUT chain matches
first as the fade-clear: an asset load fired at 0x40E silently faded the sky
and reported zero bytes read. 0x40E OUT (fade) and 0x40E IN (size) do not
collide, so the size read-back keeps its port.

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
ARP (responds for gateway), DHCP (offers 10.0.2.15/24), DNS, and TCP
forwarding. Port forwarding via `-portfwd` for host-to-guest TCP
connections.

**DNS is answered, and until 2026-07-14 it was not.** This entry used
to say the NAT handled "DNS (forwards to host)" and "TCP/UDP
forwarding". Neither was true of UDP: the UDP branch of
`nat_handle_tx` was an empty block whose entire body was the comment
`/* UDP — minimal DNS forwarding could go here */`, so every DNS query
any guest ever sent was silently dropped and every lookup timed out.
Nothing noticed, because nothing in the tree could make a network call
(`docs/PM/BACKLOG.md` 1.7).

A query to port 53 is now answered by `nat_handle_dns`: it walks the
QNAME, resolves it with `getaddrinfo` — the **host's own resolver**, so
the hosts file, the search domain and whatever DNS the host actually
uses all apply, and no packet leaves the process — and dresses the
answer as a DNS response the guest's resolver parses. Only QTYPE=A/IN
is answered; anything else returns NXDOMAIN rather than a lie.

**UDP to any port other than 53 is still dropped**, and now logs
`NAT UDP: dropped (only port 53 is served)` instead of failing in
silence. General UDP forwarding is not implemented.

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

**PS/2 Keyboard.** Port 0x60/0x64 with a scan code queue.

Scripted input (`-mouse`, `-mouse-file`, `-keys-file`) writes the same guest
state the window proc writes -- press latch included -- so a guest cannot
distinguish it from a hand on the mouse. This is what `build/test-gui.ps1`
drives GOP applications with; see `docs/ExaminersAssay.md`.

**Mouse (absolute, I/O ports 0xE1-0xE4).** The guest reads the mouse
through four ports rather than shared memory (which WHP does not keep
coherent between the window thread and the vCPU).

| Port | Meaning |
|------|---------|
| 0xE1 | Button mask (bit 0 left, 1 right, 2 middle). The live **level**, OR the presses latched since the last read. Reading it consumes the latch. |
| 0xE2 | Absolute x (16-bit, client coords) |
| 0xE3 | Absolute y (16-bit). Reading it consumes 0xE4. |
| 0xE4 | An **edge**: a new mouse event has arrived since 0xE3 was last read |

0xE1 is a level and stays valid between events; 0xE4 gates only the
position reads. **Never gate the button read on 0xE4** -- that makes a
held button legible for exactly one poll per host mouse message and
reads as released on every other poll, so no drag can ever be seen.
(This was the circuits mouse bug; see `codex/foreword/ui/InputSource.codex`
for the correct poll sequence.) The press latch on 0xE1 means a click
whose down and up both land between two guest polls is still delivered,
exactly once, so a slow-rendering guest does not drop clicks.

The 3-byte PS/2 packet buffer at GPA 28684 (read by
`codex/os/kernel/Mouse.codex`) is inert: codex-vm flushes it but never
populates it, so it always reads as 'no packet'.

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
plugs in `codex/plugs/`.

### Resolving A Quotation From A Peer (`-Peer`)

A source that `quotes` a work by digest normally has to carry it in a
`%%QUOTED-WORKS%%` blob or find it in an attached store (`-DiskFile`).
`-Peer <host>:<port>` fetches it instead:

```powershell
build/compile.ps1 -Src x.codex -Out x.cdx -Log x.log -Peer 127.0.0.1:9300
```

`compile.ps1` scans the source for every digest it quotes and does not already
carry, asks the peer for each over TCP, and prepends the answers to the blob
before the compiler is booted. The peer is `tools/cdx-serve`, listening on guest
port 9300; the client is `build/work-wire.ps1`.

**The compiler is not involved and never will be.** Library Rule 2 fixes
`codex.foreword → codex → codex.os → apps`, so the compiler citing the net stack
inverts it — and it does not need to. It hashes the content, checks the
signature, checks the key the source pinned and checks the trust floor itself,
which is exactly what lets the messenger be untrusted. The host carries works;
it never carries trust, and `work-wire.ps1` emits `WORK` lines and never a `KEY`
line. A peer that answers with the wrong work is refused by arithmetic.

The consequence to know: **the resolution order lives in the host, so this works
only when a host drives the build.** A bare-metal stick has no fetcher.

A miss is not an error — a peer that does not hold a work simply does not, and
the compile then fails at the gate if nothing else supplies it. An unreachable
peer is an error.

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

## Sampling Profiler

Two independent sampling profilers, both driven by the PIT tick (~18 Hz).
codex-vm now delivers timer interrupts to a compute-bound guest (a
55 ms kicker thread cancels the VP each period), so a self-compile is
sampled — before 2026-07-07 the timer only fired while the guest was
halted, and long compute ran blind.

**Host sampler (recommended — bias-free).** Set `CODEX_VM_PROFILE=<file>`
before booting; on each timer kick codex-vm records the guest RIP at the
point of cancellation and writes one `HPROF:<hex-rip>` line per sample on
exit. No guest cooperation, no injection-delivery skew.

```powershell
$env:CODEX_VM_PROFILE = "prof.txt"
build/compile.ps1 -Src build/output/Codex.codex -Out out.cdx -Log c.log
$env:CODEX_VM_PROFILE = ""
build/prof-report.ps1 -Log prof.txt -Map seed/Codex.map -Top 25
```

**Guest sampler.** `prof-start`/`prof-dump` builtins bracket a region; the
timer ISR records the interrupt-frame RIP into a ring at cell 0x60000.
`compile.ps1 -Profile` wraps the CDX pipeline in these (`CDX profile`
mode). It emits `PROF:<count>` then one `PROF:<rip>` per sample. Skews
toward hot call targets (WHP delivers the injected interrupt at the next
instruction boundary), so prefer the host sampler for accurate weights.

`build/prof-report.ps1 -Log <capture> -Map <symbol.map>` resolves either
format's `*PROF:` lines into a hot-function histogram. Mint a fresh map
by compiling the compiler source NON-repl (the `<out>.map` sidecar) —
the seed map drifts (see the Release-to-Public Gate note).

The sample buffer lives at 0x60000 (profiler) / 0x70000 (alloc trace),
in the free low-memory band above the AP stacks; earlier it sat inside
the page tables and enabling it destroyed them after ~88 samples.

## WCET Validation

`build/wcet-validate.ps1` empirically validates punctual WCET claims:
it compiles a program, parses the CDX6010 static counts and budgets
from the compile log, then runs the binary under `codex-vm -wcet`
(hardware execution breakpoint at each function entry + TF
single-step; callee instructions excluded, matching the static count's
per-body semantics) and gates on **observed <= budget** per
invocation. `CODEX_VM_NO_TIMER=1` is set for the observation runs.

```powershell
build/wcet-validate.ps1                          # default driver (codex/test/wcet-probe.codex)
build/wcet-validate.ps1 -Src path/to/prog.codex  # any punctual program
```

Verdicts: PASS (observed within budget and static claim), WARN
(function never entered — the inliner consumed every call site),
FAIL (observed > budget, observed > static, or instrumentation
failure). Exit 0 unless FAIL. The static CDX6010 count is a decode
of the function's finished bytes (`X86_64InsnCount.codex`, taken
after NOP compaction), so it is exact: punctual code cannot loop,
every dynamic path is a subset of the body, and an observation above
the static count means the accounting is broken. An encoding outside
the decode vocabulary raises CDX6012 (count unavailable) instead of
reporting a partial number.

Note: `-wcet` is the only mode that host-intercepts exceptions
(`ExtendedVmExits.ExceptionExit` + bitmap narrowed to #DB). In every
other mode the bitmap is inert and all exceptions go to the guest IDT
(the `!EXC` dump protocol) — INT3/vector 3 always belongs to the guest.

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

`Resolve-Rip` and `Resolve-Name` are functions in `build/vm-config.ps1`.
Dot-source it, then call them:

```powershell
. build/vm-config.ps1
Resolve-Rip -Rip 0x2748af                     # -> "function+0xNN"
Resolve-Name -Name "lookup-expr-type"          # -> 0x2F56FB (address)
```

Both read `seed\Codex.map` by default; pass `-MapFile` to point at
another map.

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
    -input input.tmp -output out.tmp -mem 3072 -headless ^
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

2. **Read the code.** `Resolve-Rip` (vm-config.ps1) gives you the function.
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
| `-Decks <N>` | Scale every phase deck floor to N% of the `BuildSettings` defaults (100 = defaults). Sends `decks=N` on the mode line. |

### The deck knob, and the deadlock it exists to break

Phase deck floors live in `BuildSettings.codex` and are **compiled into
the compiler**. `build/check-constants.ps1` hashes them and warns when the
source and the seed disagree — but it is only a warning, and `build.ps1`
refreshes the hash at the end of every build.

That creates a deadlock. If a source grows past the floor, the compile
fails with `CDX9002` — and raising the constant does not help, because
the **seed doing that compile enforces its own baked-in floor**. You
would need the new seed in order to build the new seed.

`-Decks` is the way out:

```powershell
# The source outgrew the floors the current seed was built with.
build/compile.ps1 -Src big.codex -Out big.cdx -Log big.log -Decks 200
```

Turn the floors up for the one compile that needs it, build the seed
that carries the higher default, then turn the knob back off. Without
it, the only escape is a two-stage bootstrap through an intermediate
seed.

**Turning the knob DOWN is sharp.** An under-reserved floor does not
raise `CDX9002` — the parse keep-deck copy writes past the floor into
the scratch it is still reading and the compile dies in a `#GP`
(`!EXC=0d`) with no diagnostic, because the post-copy overflow check
never runs. That is a pre-existing property of under-reserved floors,
documented in `BuildSettings.codex`; the knob simply makes it reachable
on purpose. `decks=5` on the compiler's own source demonstrates it.

The survey-multiplier system (and its `-Survey` override) was deleted
2026-07-07: phase decks are fixed generous floors and the heap range
[6 MB, 2 GB) is demand-paged — physical memory commits on first touch.
`compile.ps1` still accepts `-Survey` and ignores it for one transition
cycle.

Hardened 2026-07-06 (val CLs 7207-7211): only
not-present faults grow the heap (protection faults dump), demand
mappings carry NX, the demand-range top derives from actual RAM (any
`-mem` from ~128 MB boots; the top 64 MB stays present for the stack),
a TSS/IST1 emergency stack turns stack-overflow triple-faults into
`!EXC` dumps, and the touched-page count lives at cell 30688.

### GDB (Legacy Fallback)

The interactive debugger (`-debug`) covers breakpoints, single-step,
register/memory inspection, conditional breaks, and backtraces. GDB
under WSL with QEMU TCG is a last resort for hardware watchpoints
(DR0-DR3) on specific memory addresses. Rule 6 permits Unix tools
for this purpose only. See `build/gdb-watchpoint.ps1`.

## Reading Telemetry Off the Glass (QR)

On real hardware there is no serial port. Consumer boards of this era
expose no UART header, and the no-borrowed-substrate doctrine means there
is no dmesg, no lsusb, and no kernel log -- nothing between the
framebuffer and silence. Worse, the obvious workaround (write the findings
to a file on the boot stick) routes the telemetry through USB mass
storage, which on the machines this matters for is *itself* one of the
broken subsystems. That mistake cost a week; the accident report is
`docs/PM/Done/Stories/TheSilentKeyboard.md`, and its first recommendation is
that no hardware campaign may launch without an output channel that does
not depend on the subsystem under test.

This is that channel, and it is the standing one.

**The guest paints QR codes.** `apps/works/GopQr.codex` is a QR encoder in
Codex -- version 5, error level L, mask 0, 106 bytes per code, chunked
`i/n;` across as many codes as the report needs. It draws them straight to
the GOP framebuffer. It needs no disk, no serial, no network, and no
working USB. If the machine can paint pixels, it can report.

**The human photographs the screen.** A phone, hand-held, is fine.

**The host reads the photograph.**

```powershell
pwsh tools/qr-read.ps1 -Path D:\20260713_131201.jpg
pwsh tools/qr-read.ps1 -Path shot.jpg -Save findings.txt   # keep the bytes
pwsh tools/qr-read.ps1 -Path shot.jpg -ShowDebug           # when it will not read
```

It prints the exact bytes the machine emitted, reassembling the `i/n;`
chunks in order and warning loudly if a chunk is missing rather than
quietly handing back a truncated report. Reed-Solomon correction is real:
a photograph of a lit screen loses modules to glare and blur, and up to 13
corrupt codewords per code are recovered.

`qr-read.ps1` is the decoder half of `GopQr` and is written as its exact
inverse -- same reserved-module map, same zigzag, same mask, same
generator (roots from alpha^0). Only the image decode is borrowed, from
Windows itself; the QR decode is ours. Do not reach for a Python library
or a QR app on your phone: the phone app will silently give you a
*rendering* of the bytes, and this pipeline is the one that gives you the
bytes.

**Reading the output.** `-ShowDebug` prints the timing patterns of every
symbol it sampled. Timing must read `101010...` and report `21/21`. That
number is the honest test of whether the geometry was found:

- `21/21` and no decode -- the grid is right and the payload is damaged.
  Re-shoot with less glare.
- anything less -- the grid is wrong, and no amount of error correction
  will save it. Re-shoot straighter and fill the frame with the codes.

**When you add a probe, render its findings as QR.** A number that only a
human can read off a monitor is a number that arrives wrong, arrives late,
or -- as happened -- dies with the framebuffer at power-off, having cost a
person a walk across the building and a flash cycle to obtain.

## Poison-Alloc Diagnostic Build

`__alloc` zeroes every block it returns (calloc semantics). A **poison
build** replaces that zero fill with `0xCD`, so any field read before it
is written dereferences `0xCDCDCDCDCDCDCDCD` — a non-canonical address,
and therefore an immediate page fault instead of a plausible zero.

**This is a release gate, not a routine one.** Run it before publishing
the seed to the public mirrors. If the battery passes against a poison
seed, the compiler has no uninitialized-field dependencies — the zero
fill is a safety net and not a patch holding something together.

Why it matters: the REPL loop resets R10, deck-pos and heap-hwm between
compilations but does **not** zero the freed memory. Without the calloc,
the second compile in a batch allocates records on top of the first
compile's live pointers and type tags. The first boot looks clean because
hardware zeroed the heap; the second one does not.

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
   regardless — the text map only feeds `-Break` and `Resolve-Rip`.
