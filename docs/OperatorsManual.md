# Operator's Manual

How to build, test, install, and debug the Codex compiler. For the
runtime memory model, register conventions, and allocator internals,
see `docs/ArchitectsSketchbook.md`.

## Build Process

The build script (`build/build.ps1`) runs the full verification
pipeline. Each phase must pass before the next begins.

Traps in the surrounding tooling, every one of which presents as something
other than what it is:

- **`tools/codex-vm.exe` is a versioned Perforce binary.** `build-vm.ps1`
  cannot relink it until `p4 edit tools/codex-vm.exe`; without that you get
  `LNK1104`, which reads like a missing file rather than a read-only one.
- **`build/run-plug.ps1` takes `-InFile`, not `-Input`** (`$Input` is a
  PowerShell automatic variable and shadows it). The request it sends is
  framed and **the reply is not**, so strip a header only when the length
  field proves one is there.
- **PowerShell's `-band 0xFFFFFFFF` is not a mask.** `0xFFFFFFFF` parses as
  Int32 `-1`, so the AND is the identity and the value reaches the next
  `[int]` cast intact and overflows. In hand-assembly like `cdx-to-pe.ps1`,
  take the bytes from `[BitConverter]::GetBytes` instead.
- **Variable names are CASE-INSENSITIVE, so `$em` and `$EM` are ONE
  variable.** A scan holding its needle in `$EM` and its per-file counter in
  `$em` searches for whatever the counter last held. Cost an hour on
  2026-07-28: the counter reset to 0 each file, so from the second file
  onward the scan looked for `[char]0`, every real hit vanished, and binaries
  decoded as UTF-8 matched their NUL bytes and reported a uniform 10. **The
  uniform count across unrelated files was the tell** -- a real measurement
  does not come out identical on a `.glb`, a `.wasm` and an `.elf`. Never
  distinguish a constant from a counter by case, and note that `-replace` is
  case-insensitive too, so renaming `$EM` also renames every `$em`.
- **`Measure-Object -Line` counts only NON-BLANK lines**, and nothing at the
  call site says so. Measured 2026-07-28 it reported the compiler at 48,456
  lines against a true 57,440 from `ReadAllLines`, a 16 per cent undercount,
  heading into a public report. **Any line count wants two methods before it
  is published**; the disagreement is the instrument, not an annoyance.
- **`-match` / `-notmatch` against an ARRAY are filters, not tests.** They
  return the matching elements, and a non-empty result is truthy, so
  `if ($lines -notmatch 'x')` is true whenever ANY line lacks `x` -- nearly
  always. Join to one string before testing. This silently disabled a whole
  guard in `merge-down-all.ps1`.

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
   point of itself -- the binary it produces is identical to itself.

9. **Test battery**: `build/test.ps1` runs all samples in `codex/test/`.
   Each sample has a sidecar (`.expected` for success, `.failing` for
   expected errors, `.skip` for skipped). Runs parallel VM instances.

10. **Plug gates**: the five native backends (riscv, arm64, elf, pe,
    img) must rebuild clean with the just-proven compiler
    (`plug-binary`), and a representative transpiler subset
    (typescript, python, rust, ptx) must run end-to-end -- SUT IR over
    the framed TCP wire through the plug VM to non-empty target text
    (`plug-smoke`). Missing plug CDX builds once and caches; a failing
    smoke run gets one rebuild-and-retry before failing the build.
    The full 53-plug matrix (`codex/plugs/test-plugs.ps1`) remains a
    manual sweep.

### Quick Commands

```powershell
build/test.ps1                        # Sample battery (~2-5s per sample)
build/test.ps1 -Jobs 8                # Parallel test (8 batch slots; 8 is the standard)
build/test.ps1 -All                   # Include foreword + app tests
build/test.ps1 -Fatal                 # Include fatal (GPF/exception) tests
build/build.ps1                       # Full pipeline (all gates)
build/desk.ps1                        # The desktop, in a window, ~1.5s to paint
```

## The Desktop On The Dev Box (`build/desk.ps1`)

```powershell
build/desk.ps1                                  # interactive window at 1280x800
build/desk.ps1 -Width 1920 -Height 1080         # any mode codex-vm will give you
build/desk.ps1 -Force                           # recompile even if the CDX is current
build/desk.ps1 -Shot shot.bmp                   # headless, one frame, then exit
build/desk.ps1 -Keys '4000:4'                   # scancode timeline: 33 = f, 4 = 3, 1 = Esc
build/desk.ps1 -Rtc 2026-07-30T06:00:00         # freeze the taskbar clock
build/desk.ps1 -Disk seed/Codex.img             # give the Files pane a real ESP
```

**`-Disk` copies the image to `build-output/` and attaches the copy.**
codex-vm writes back and flushes to the host, so attaching a depot
artifact directly would let a guest edit it. With `seed/Codex.img` the
Files pane browses `ESP:/` and lists `EFI/`, `SOURCE.SRC` and
`CODEX.CDX`, which is the compiler reading its own boot medium. Without a
disk the pane says `no FAT ESP on the boot medium` and the font falls
back; both are correct and neither is a fault.

GopDesk normally reaches the glass through an Option A boot image, which
means flashing a stick or driving OVMF. `apps/works/DeskVm.codex` is the
dev-box entry point instead: it reads codex-vm's own GOP cells (0x7C4,
0x7C8, 0x7E0, framebuffer at 0xBF000000) rather than the boot stub's cells
at 0x8000, so the desktop comes up from a plain CDX with no image, no ESP
and no firmware in front of it. **Measured 2026-07-30: the desk paints
1.5 seconds after launch** (nothing at 1.0s, chrome at 1.5s), so a key
pressed in the first second is gone.

`f` opens Files, `3` opens the 3D View, `Esc` leaves a view for the desk.
Editor, Terminal, Monitor and Settings are inert chrome. There is no disk
attached, so `desk-font` falls back to the CBF bitmap face and the Welcome
window says so; that is the fallback working, not a fault.

**Nothing leaves the desktop by key.** `desk-loop` does not return, so Esc
at the desk does nothing and the guest cannot be ended by a stray keypress
(fester, main 12350). **Shutdown**, bottom-left, is the way out, and it is
a deliberate click. Closing the window or `Stop-Process -Id` also works.

The launcher hands back the codex-vm PID and the `Stop-Process -Id` line
to close it. Use it: several agents run guests on this box and killing
`codex-vm` by name takes down all of them (see the section below).

## Test Harness

### Two-Phase Architecture

**Phase 1 -- Batch compile.** One VM per job slot, REPL loop reuse.
Each slot boots `seed/Codex.cdx` once, then compiles multiple test
sources sequentially over the persistent serial connection. The
compiler resets its heap between compilations automatically.

**Phase 2 -- Run.** Each compiled CDX with a `.expected` sidecar is
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
| `foo.flags` | First line appended to the compile mode line: `prose`, `passes=+name`, `decks=N`. Read by the batch harness only. See `docs/ExaminersAssay.md` |
| `foo.stdin` | Pumped to VM serial after boot (runtime input) |
| `foo.keys` | Scancode timeline (`t:scancode` per line, t = ms since boot) passed as `-keys-file`. This is the **keyboard**; `.stdin` is the **serial ring**. A keyboard read (`uefi-read-key` / `poll-key`) reads the PS/2 key cell and no `.stdin` reaches it -- pick by what the code reads. See `docs/ExaminersAssay.md` |
| `foo.disk` | Attached as IDE disk image (primary master) |
| `foo.disk2` | A second image, attached as the primary slave and reached by `block-select 1`. A test whose subject is WHICH drive it reached needs two: with one image behind every position, a working drive-select and a missing one produce identical output. `codex/test/block-select-drives` is the worked example. Copied to a writable temp like `.disk`, which matters more here because the case it exists for is one drive writing to another |
| `foo.smp` | Core count; the test boots with `-smp N` |
| `foo.vmargs` | Extra codex-vm flags (whitespace-separated, `#` comments). For tests whose subject is the machine: a bus topology, an absent device. See `docs/ExaminersAssay.md` |

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

`tools/codex-vm.exe` -- a ~8000-line C program using Windows Hypervisor
Platform (WHP). Build with `tools/build-vm.ps1`.

**A guest cannot detect codex-vm through the HYPERVISOR bit.** CPUID leaf 1
ECX reads ZERO here, and bit 31 of that register is the conventional "am I
virtualised" answer, so it says no. Measured on a booted kernel: the vendor
string is `GenuineIntel`, the maximum leaf is 1, and leaf 1 EDX carries a
real feature set (FPU TSC MSR PAE APIC CMOV MMX FXSR SSE SSE2) while leaf 1
ECX is entirely empty. The corollary for anything decoding CPUID: an empty
feature line on this host is the host, not a broken decode.

**Never kill codex-vm by process name.** Several agents run this box at once,
each booting VMs out of its own `D:\Projects\NewRepository-<agent>\`, so
`Get-Process codex-vm | Stop-Process -Force` takes out **every agent's** VMs and
not just yours. A gate or a battery that dies for no reason someone can explain
is what that looks like from the other end. Filter on the executable's path:

```powershell
Get-CimInstance Win32_Process -Filter "Name='codex-vm.exe'" |
  Where-Object { $_.ExecutablePath -like 'D:\Projects\NewRepository-<agent>\*' } |
  ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
```

The same caution applies to reading a stray VM as evidence: a codex-vm you did
not start is usually another agent working, not a leak of yours. Check
`ExecutablePath` before concluding anything about it, and before killing it.

Build note: the linker fails with `LNK1104` if a codex-vm is holding
`tools/codex-vm.exe` open, so stop **your own** VMs (as above) before
`tools/build-vm.ps1`.

#### CLI Flags

```
codex-vm -kernel file.cdx [options]
```

| Flag | Default | Description |
|------|---------|-------------|
| `-kernel <file>` | (required) | CDX or multiboot kernel to boot |
| `-mem <MB>` | 3072 | Guest RAM in megabytes. Binaries compiled by seeds older than CL 7209 require more than 2048 (their boot stack lands in the demand-paged range below 2 GB); current seeds boot at any size from ~128 MB up |
| `-input <file>` | -- | Pre-load file into serial ring buffer (source input) |
| `-output <file>` | -- | Capture serial output to file |
| `-disk <file>` | -- | Attach IDE disk image as the primary channel's MASTER (read/write, flushed to host) |
| `-disk2 <file>` | -- | Attach a second image as the primary channel's SLAVE, reached by `block-select 1`. A position with no image behind it answers the floating bus (`0xFF` on the signature registers, `0x00` on status) so the guest's own detect reports it absent. Before this existed there was one image behind every drive position, `dm-enumerate-drives` reported four drives on a machine with one disk, and "Install Codex to Drive" pointed at any of them repartitioned the boot disk. Status answers `0x00` rather than `0xFF` deliberately: `emit-ata-bring-up` runs a bounded BSY wait before it reads the signature, and a floating `0xFF` there costs a million port INs, which is a million VM exits, on every diskless boot |
| `-headless` | off | Suppress VGA/GOP display window |
| `-board-mmio` | off | Commit and map host RAM at the three board register windows that sit above the RAM ceiling: RP2040 SIO (0xD0000000), Cortex-M PPB/SCB (0xE0000000), BCM2711 peripherals (0xFE000000, 9 MB). A board driver's register access then reads back what it wrote -- the same fidelity the six sub-3GB boards get from falling inside guest RAM. **Opt-in, because it shadows the HDA and xHCI BARs**: audio and USB are dead while it is on, which a board test does not care about. Required by the pi4 / rp2040 / stm32l4 driver tests; `build/boards-test.ps1` passes it. |
| `-xhci-no-root-kbd` | off | Unplug the HID keyboard on xHCI root port 2, leaving the high-speed hub on root port 4 as the only route to a keyboard. The bus walk takes the first keyboard it finds and the root port comes first, so this is what lets a test drive the hub path with an unmodified guest binary. |
| `-xhci-hub-tiers <N>` | 1 | How many hubs to stack on xHCI root port 4. `1` is a high-speed hub with a full-speed keyboard below it. `2` inserts a full-speed hub in between, which is what tells apart a driver that reads the transaction translator off the immediate parent from one that carries the nearest high-speed ancestor's down the walk -- a full-speed hub has no translator of its own, so the keyboard below it is still served by the high-speed hub two tiers up. Route strings run to two nibbles (`0x11`). A monitor hub in front of a keyboard hub is this topology. |
| `-xhci-calibrate-periodic` | off | Skew the model's EXPECTED Interval and Max ESIT Payload by one, so a correct driver is reported as MISMATCH. The periodic value check runs always and reports once per slot and DCI on stderr; this is the arm that shows it can say no. Use it whenever you are about to believe a MATCH. |
| `-xhci-psi` | off | Declare Protocol Speed ID dwords in the Supported Protocol capability (PSIC = 4) and report NON-DEFAULT Port Speed values in PORTSC: full speed as 5, low as 6, high as 7, super as 8. Table 7-13's familiar 1/2/3/4 are only the defaults, and xHCI 7.2.2.1.1 makes them conditional on PSIC being zero. Use this on any driver that decides a speed CLASS from the PORTSC speed field. The model reports the true class and the slot context's claim side by side on stderr. |
| `-xhci-intel` | off | Present the xHCI as an Intel Lynx Point PCH (8086:8C31) with the USB2 ports still owned by the companion EHCI. XUSB2PRM reads `0xE` (ports 1-3 routable), XUSB2PR resets to 0, and an unrouted port reads PORTSC as all zero -- dark, not merely disconnected, which is what a port left with the companion looks like to the xHCI. The guest must route the ports before it sees any device on them. Writes to XUSB2PR are traced on stderr. |
| `-xhci-intel-lock` | off | As `-xhci-intel`, and additionally make XUSB2PR read-only, modelling a part whose ports firmware pinned to the companion. A correct driver then finds nothing, which is what proves the routing gate rather than something else is deciding. |
| `-xhci-csz` | off | Advertise CSZ=1 (HCCPARAMS1 bit 2) and hold every context to the 64-byte stride, the way Intel PCH silicon does. Every bed before this flag reported CSZ=0, so the driver's 64-byte context path shipped in every image and executed nowhere but on real Intel parts. A driver that hardcodes 32 writes its slot context inside the input control context and its ring pointers where the controller will not look. |
| `-xhci-scratch <N>` | 0 | Declare N scratchpad buffers in HCSPARAMS2 and REFUSE the first ENABLE_SLOT (completion code 9, stderr verdict) unless DCBAA[0] points at a 64-byte-aligned array of N page-aligned, in-RAM page pointers. QEMU and this model declared zero forever; Intel parts demand real pages, and a missing array is silent corruption on metal. |
| `-uefi-conout-remode` | off | With `-uefi`: the first ConOut ClearScreen switches the GOP to 1024x768 stride 1024 and updates Mode->Info, modelling AMI Aptio V's GraphicsConsole activation. A stub that reads the GOP geometry BEFORE its first ConOut use hands its payload the splash mode's numbers for a scanout that has since changed, which is the ASUS display corruption of 2026-08-02 exactly. |
| `-xhci-evt-flood <N>` | 0 | Post N extra Port Status Change events at the Run transition, on top of the per-port connect and reset-completion PSC events the model now always posts. The event ring is 64 TRBs: N at or above it marches the producer through the ring WRAP and into the FULL condition (drop + one stderr report, per real silicon), the two paths a 4-port bed can never reach and a 26-port Intel reaches before enumeration begins. Both paths measured PASS on the shipping driver at N=30/50/100. |
| `-hid-nak` | off | The HID keyboard NAKs every interrupt IN forever: no DMA, no transfer event, the pending TD stays in progress and Stop Endpoint answers FSE code 26 (Stopped) with the residual. Reproduces the ASUS 2026-08-03 flight signature (EPINT=0, dq parked, est=1, f1=1a) on the desk; the arm every silent-keyboard hypothesis is tested against. |
| `-hid-idle-quirk` | off | The HID keyboard NAKs every interrupt IN after the guest sends SET_IDLE duration 0 (the over-honored "report only on change", HID 1.11 7.2.4 against the F.3 every-poll default). A driver that skips SET_IDLE never triggers it; one that sends it goes silent -- the arm that separates the v15 fix from the v14 behavior. |
| `-uefi` | off | UEFI firmware mode (ConOut/ConIn, GOP, Block I/O, memory map, runtime services, auto-extract PE from GPT images) |
| `-gop` | off | Activate GOP framebuffer (default 640x480) |
| `-gop-width <N>` | 640 | GOP framebuffer width (implies `-gop`) |
| `-gop-height <N>` | 480 | GOP framebuffer height (implies `-gop`) |
| `-smp [N]` | 1 | Enable multi-core: N virtual processors (1-16, default 4 if N omitted). Creates WHP VPs, LAPIC, MADT with per-core entries. Core count written to GPA 0xFF8; boot code reads it to decide whether to send INIT/SIPI. |
| `-portfwd [udp:]<host:guest>` | -- | Port forwarding from host to guest NIC (repeatable, max 8). TCP by default; `udp:` forwards datagrams instead, giving each host client a synthetic gateway source port so the guest's replies route back. Examples: `-portfwd 8080:80`, `-portfwd udp:15683:5683` |
| `-debug` | off | Interactive debugger shell on breakpoints and single-step |
| `-break <name>` | -- | Patch INT3 at named function entry (implies `-debug`, repeatable) |
| `-map <file>` | auto | Symbol map file for address resolution. Auto-probed: `<kernel>.map`, then `seed/Codex.map` |
| `-watch <0xADDR>` | -- | Hardware watchpoint via page protection |
| `-watch-size <N>` | 8 | Watchpoint region size (max 64 bytes) |
| `-hbreak <fn>[:<reg>=<val>]` | -- | Conditional execution breakpoint through the debug registers (repeatable, max 4, one DR each, shared with `-wcet`; needs `-map`). The condition is evaluated at the host's `#DB` exit on the guest's own register file. Writes no guest byte, and an unmatched hit RESUMES instead of halting, so this is the way to reach the Nth call of a hot helper. Prints `HBREAK-OBS: <fn> hits=<n> matched=<m>` on exit -- **both counts, always**, because a run reporting only matches cannot be told from one whose breakpoint was never reached. **A conditional breakpoint on INT3 cannot work in this guest** and `-break`'s is not offered: vector 3 belongs to the guest, whose handler runs before the host sees the trap, so the condition would be read off the handler's registers (`rdi` is `0x33` every time) |
| `-hwwatch <0xADDR>` | -- | Data watchpoint through DR0, trapping the exact linear address regardless of guest paging, so it does not collide with the guest's own demand-paging `#PF` handler the way `-watch`'s page-protection exits do. `-hwwatch-rw` widens it to reads, `-hwwatch-len <N>` sets 1, 2, 4 or 8 bytes. **It did not fire host-side until 2026-07-28**: DR0 was armed correctly but nothing enabled `ExtendedVmExits.ExceptionExit`, so the `#DB` was delivered to the guest IDT, which dumped `!EXC=01` and halted on the first hit. Any conclusion drawn from an older `-hwwatch` run saw at most one write |
| `-hwwatch-log` | off | With `-hwwatch`, print one line per write and continue, instead of a full crash report. This is what names a corrupter: it is the SECOND write to a cell that matters, and the crash report is both too heavy and, in practice, terminal |
| `-wcet <name>` | -- | Observe a function's per-invocation dynamic instruction count (repeatable, max 4 -- one DR0-DR3 exec breakpoint each; needs `-map`). Prints `WCET-OBS: <fn> max=<n> calls=<k>` on exit. Observation only: no guest byte is modified. |
| `-mouse <script>` | -- | Scripted pointer: `t:x,y,btn` events separated by `;` (t = ms from boot, btn bit 0 left / 1 right / 2 middle). Injected straight into the guest, so no host cursor moves and no window takes focus. Works headless. |
| `-mouse-file <file>` | -- | Same, read from a file (one event per line, `#` comments). Use for drags, which run to dozens of samples. |
| `-keys-file <file>` | -- | Timeline keyboard: `t:scancode` per line, on the same clock as `-mouse`. Lets a script interleave typing with clicks (the older `-keys` fires on a fixed start+interval). |
| `-rtc <stamp>` | host clock | Freeze the emulated CMOS RTC at `YYYY-MM-DDTHH:MM:SS` (the `T` may be a space). Day-of-week is computed from the date, not accepted. **This is what makes a guest that paints the time comparable against a recorded frame** -- without it the clock is host state the test cannot twist, which is why GuiOS was believed to be un-goldenable. It also turns the update-in-progress simulation OFF (a frozen clock cannot express UIP), so it is for frames and never for testing the RTC itself: anything asserting on clock behaviour must run without it. |
| `-screenshot <file>` | -- | Save GOP framebuffer as BMP on exit |
| `-screenshot-delay <ms>` | 0 | Delay before screenshot capture |
| `-args <string>` | -- | Boot arguments string (accessible to guest) |
| `-trace-file <file>` | -- | Write execution trace to file |

Environment: `CODEX_VM_NO_TIMER=1` disables PIT timer interrupts.

#### Emulated Hardware

**CPU and SMP.** WHP-accelerated x86-64 (long mode, full hardware
virtualization). Shadow register file works around WHP GPR corruption.
Multi-core via `-smp N`: each AP gets its own WHP virtual processor
and host thread. INIT/SIPI startup sequence: the guest copies a real-mode
trampoline into the page at GPA 0x1000, seeds the core-id counter at cell
36256, writes the per-core stack addresses to a stack table at GPA 0xF00,
then writes the LAPIC ICR -- an INIT IPI followed by two start-up IPIs
carrying **vector 1**, delivered to all cores excluding self. That ICR
write is what launches the APs.

**An AP starts the way silicon starts one**, at `vector<<12` in real mode
with reset control registers, an empty GDT and nothing in RDI. Everything
after that is the guest's trampoline: protected mode, PAE, CR3, EFER, long
mode, a core id taken with a locked exchange-add on cell 36256, a stack
claimed from the table by that index, the runtime GDT and IDT, its own task
register, and one added to the ready count at cell 4080. The BSP spins on
that count (bounded -- it gives up and continues single-core rather than
hanging) before carrying on.

This used to be a shortcut, and the shortcut hid the whole problem: codex-vm
read a 64-bit entry address out of GPA 0x1000 and configured each AP's CR3,
CR4, EFER, GDTR, IDTR and core id from the host. SMP worked here and could
not have worked on a physical machine, where the vector field is the only
thing an AP is told. Nothing changed about the guest when this was fixed
except that it now does the work; three real defects in the guest surfaced
the moment it had to (see `docs/ArchitectsSketchbook.md`).

**An AP runs processes.** It goes to `__idle_dispatch`, the same routine
the boot processor goes to when it runs out of work, claims a READY slot
out of the process table with a `LOCK CMPXCHG`, and resumes it -- on that
process's own stack, with that process's own R10. Six children spawned at
`-smp 4` execute across four cores. `codex/test/smp-dispatch.codex` pins
it, and pins the right thing: every claim by a core whose id is *not*
zero bumps cell 36200, and the boot processor's id is always zero, so a
count above zero is evidence a core other than the BSP took a process out
of the table and executed it. Six children *finishing* would prove
nothing -- one core does that.

**A process on an AP is preempted.** The PIT's IRQ reaches the boot
processor alone, so an AP used to run whatever it was given until that
process yielded, blocked or exited. Each AP now arms its own **LAPIC
timer** at bring-up -- periodic, on vector 48 -- and raises IF, so a
scheduling tick arrives on every core. codex-vm emulates the timer
per-core (`lapic_timers[]`) and injects the vector from each AP's own
thread; the kicker cancels the AP's VP every PIT period, because a
compute-bound core never leaves `WHvRunVirtualProcessor` on its own and a
tick that is never injected is not a clock.

`codex/test/smp-preempt.codex` pins it, and pins the right thing: every
timer interrupt taken on a core whose id is not zero bumps cell 36216, and
the BSP's id is always zero. Six children *finishing* proves nothing here
either -- and neither does six children finishing on six cores, because an
AP that runs its child to completion undisturbed has still never been
preempted. The claim is about who can be interrupted, so the evidence is
about who *was* interrupted.

An idle core **halts** rather than spinning on `pause`, and **affinity** is
honoured: `__idle_dispatch` skips a process whose affinity field names a
different core, `-1` meaning any. `codex/test/smp-halt.codex` and
`codex/test/smp-affinity.codex` pin the two. Work stealing exists in the OS
scheduler model but not in the bare-metal dispatcher, which scans a shared
process table rather than per-core queues.

**Proc 0 does not migrate**, and this line used to say it was permitted by
design and merely unproven. Three guards forbid it: `__idle_dispatch` starts
each core's scan at its own id so an AP never reaches slot 0, and both
preemption scans skip slot 0 when the claiming core is not the boot
processor. Its affinity field says `0` to match; it said `-1`, "any core",
which is what the old claim was read off. `codex/test/smp-proc0-pinned.codex`
pins it, and pins it in the only way that means anything: slot 0's core stamp
must still be the boot processor's after a run in which an AP demonstrably
claimed a process, an AP was demonstrably preempted, and some other slot's
stamp is demonstrably above zero.

**An SMP test must assert that an AP executed guest code, not that the
work finished** -- one core can finish the work. `smp-cores.codex` reads
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
lengths at various points, and both resume mid-instruction -- the
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
| 0 | Bochs VGA (1234:1111) | Display | 0xFD000000 | -- |
| 1 | xHCI USB (1033:0194) | USB 3.0 | 0xFE800000 | 10 |
| 2 | Intel HDA (8086:2668) | Audio | 0xFE000000 | 11 |

Config space read/write via ports 0xCF8/0xCFC.

**Display.** Three modes:
- **VGA text** (80x25) via port 0x3D4/0x3D5, text buffer at 0xB8000
- **Bochs VBE** via ports 0x1CE/0x1CF (index/data), guest-initiated mode switch
- **GOP framebuffer** at GPA 0xBF000000 (in RAM -- fast writes, no MMIO trap). Guest writes 32-bit XRGB pixels directly. The VM renders from a shadow copy to a Win32 window on a separate thread. Three preset modes: 640x480 (0), 800x600 (1), 1024x768 (2). Custom sizes via `-gop-width`/`-gop-height`. Window title changes to "Codex Spark" in GOP mode.

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
ARP (responds for gateway), DHCP (offers 10.0.2.15/24), DNS, TCP
forwarding, and UDP forwarding (see below). Port forwarding via
`-portfwd` for host-to-guest TCP connections.

**DHCP is answered, and until 2026-07-30 it was not** -- the same shape as
the DNS correction below, in the same entry, found the same way. This line
has claimed the NAT offers 10.0.2.15/24 by DHCP for as long as it has
existed. There was no server: a DISCOVER on port 67 fell through to
`nat_handle_udp_tx` and out to a host socket that nothing answers, so the
guest waited out its fuel and configured nothing. Nothing noticed because
nothing in the tree had ever sent a DISCOVER -- `Dhcp.codex` builds the
messages and parses the replies and says of itself "pure logic, no I/O",
and there was no caller. The server now answers DISCOVER with an OFFER and
REQUEST with an ACK carrying mask, router, DNS and a 3600-second lease, and
`codex/test/dhcp-acquire` is the guest that asks. **A model nobody drives
and a document nobody checks fail in the same direction, which is the
reassuring one.**

**`-dhcp-lease <seconds>` sets the lease the NAT offers; the default is
3600.** It exists because the default cannot be tested: a client renews at
half its lease, so nothing observes a renewal inside a run that lasts
seconds. `codex/test/dhcp-renew` asks for four, which puts the renewal two
seconds in, and it is the only reason that test can exist.

**The NAT is one wire, and `-e1000-nat` moves it to the Intel card.** The
e1000 model is absent unless a flag selects it, and by default its transmit
path sums the frame rather than sending it. With `-e1000-nat` the frame goes
to the NAT and replies are delivered into the e1000 receive ring instead of
the NE2000's, so a guest that binds the Intel part can hold a real TCP
conversation. Both cards drain one queue, so the NE2000 injection is turned
off while the flag is set; a guest that stays on the NE2000 under this flag
transmits and never hears back. `-e1000-strict-filter` makes the model refuse
promiscuous mode, so a frame addressed to neither the station address nor
broadcast is dropped and named on stderr -- the metal failure a stack
sourcing the wrong MAC produces, which our driver's own RCTL.UPE hides.

**The PHY, reached through MDIC at 0x0020.** The model answers MDIC with a
32-register PHY file at address 1; a read of any other address returns the
error bit rather than zero, because that is what a bus with nothing on it
does and a driver must be able to tell the two apart. BMCR reset and
auto-negotiation restart are self-clearing, and a device reset drops the
negotiated state.

| Flag | Effect |
|---|---|
| `-e1000-no-phy` | MDIC never reports ready: a PHY that is not answering |
| `-e1000-phy-err` | MDIC reports the error bit on every transaction |
| `-e1000-phy-link` | STATUS.LU requires auto-negotiation complete, not merely CTRL.SLU |

`-e1000-phy-link` is the one worth knowing about. It is **off by default**,
so every run that predates it keeps the SLU-only link it was measured
against; with it on, a driver that never brings the PHY up gets no link,
which is the I219's real behaviour and the failure that was previously
invisible here. `codex/test/e1000-phy` runs under it and
`codex/test/e1000-phy-absent` runs under `-e1000-no-phy`.

**DNS is answered, and until 2026-07-14 it was not.** This entry used
to say the NAT handled "DNS (forwards to host)" and "TCP/UDP
forwarding". Neither was true of UDP: the UDP branch of
`nat_handle_tx` was an empty block whose entire body was the comment
`/* UDP -- minimal DNS forwarding could go here */`, so every DNS query
any guest ever sent was silently dropped and every lookup timed out.
Nothing noticed, because nothing in the tree could make a network call.

A query to port 53 is now answered by `nat_handle_dns`: it walks the
QNAME, resolves it with `getaddrinfo` -- the **host's own resolver**, so
the hosts file, the search domain and whatever DNS the host actually
uses all apply, and no packet leaves the process -- and dresses the
answer as a DNS response the guest's resolver parses. Only QTYPE=A/IN
is answered; anything else returns NXDOMAIN rather than a lie.

**General UDP forwarding is implemented** (2026-07-23). This entry said
the opposite until then: every UDP datagram to a port other than 53 was
dropped, which is why CoAP could not reach a server, NTP could not reach
a time source, and no datagram protocol in the tree was testable. A
guest-originated datagram now opens a **UDP flow** -- a host socket
remembered by (guest port, destination port, destination address) -- and
its replies are framed back to the port the guest sent from. A flow is
not a connection: there is no handshake and nothing to retransmit, so it
is reaped on 30 seconds of idleness rather than on any close, and 32 of
them are live at once.

**The gateway address is the host.** `10.0.2.2` is translated to
`127.0.0.1` when a host socket is opened, for TCP as well as UDP. This is
the convention every user-mode NAT uses and the one this emulator already
advertised in DHCP and answered ARP for -- but nothing translated it, so
a guest addressing the gateway had its packet handed to the host stack as
a literal destination and it went nowhere. A service running on the box
codex-vm runs on is reached at `10.0.2.2` from inside the guest.
`build/mqtt-interop-test.ps1` and `build/coap-interop-test.ps1` are the
worked examples, against mosquitto and aiocoap.

**Inbound UDP is forwarded too**, via `-portfwd udp:<host>:<guest>` (the
prefix is optional and the default stays TCP, so every existing invocation
means what it always did). A datagram from a host client is put in front of
the guest from a **synthetic gateway port** that names which client it came
from; the guest answers that port as it would any peer, and the reply is
routed back by it. Without the synthetic port there is nothing in the
guest's reply that says which of several host clients it is for.
`build/coap-serve-test.ps1` is the worked example: aiocoap's client against
a CoAP server running in the guest.

**IDE Disk.** PIO-mode IDE controller at ports 0x1F0-0x1F7, 0x3F6.
Supports IDENTIFY, READ SECTORS, WRITE SECTORS, and FLUSH CACHE.
Writes are flushed to the host image file (durable disk writes).

**xHCI USB 3.x Controller.** Full command ring, event ring, and
transfer ring processing. Three device slots:
- Slot 1: Mass storage (bulk IN/OUT, SCSI READ/WRITE to RAM disk)
- Slot 2: HID keyboard (interrupt IN, generates scan codes)
- Slot 3: UVC camera (isochronous transfers). Delivers a 160x120 YUYV
  colour-bar test pattern: eight vertical bars 20 px wide, luma
  alternating 16 and 235 by bar. An isochronous TRB that sets IOC now
  gets a transfer event (Short Packet when the frame is smaller than the
  buffer); it used to copy the data and post nothing, so a driver waiting
  on the completion waited forever while its buffer filled.
- Root port 4: a **high-speed hub** with a **full-speed keyboard** behind
  its one downstream port (hub class descriptor, GET_PORT_STATUS,
  SET/CLEAR_PORT_FEATURE). Device personality keys off the root port AND
  the route string, because with a hub on the bus two devices share a root
  port and only the route tells them apart. Under `-xhci-hub-tiers 2` a
  **full-speed hub** is inserted between the two, so the keyboard sits at
  route `0x11` and is served by a translator that its immediate parent does
  not own.

**A full- or low-speed device below a high-speed hub is serviced only if
its slot context names a TRANSACTION TRANSLATOR.** Real silicon reaches
such a device through the hub's TT, and is told which one by the TT Hub
Slot ID / TT Port Number in the slot context; leave them zero and there is
no split schedule to place the endpoint in. codex-vm refuses to service
that endpoint and says so, rather than delivering anyway. The failure this
models is asymmetric and worth recognising: **control transfers to endpoint
zero keep working, because the hub handles those itself, while the periodic
endpoint is silent forever** -- a keyboard that enumerates perfectly and
then never sends a keystroke. That is the reported shape of the failure on
real Intel xHCI, and it is reproducible here
with no hardware.

**Which** translator is the part that is easy to get wrong, and
`-xhci-hub-tiers 2` is what makes the difference observable. A translator
lives in a high-speed hub; a full-speed hub has none, so everything below a
full-speed hub is served by whichever high-speed hub sits further up. A
walk that reads the translator off the device's immediate parent is right
for one tier and writes zero for two. `codex/test/apps/usb-kbd-hub` and
`usb-kbd-hub2` are the two cases, and they differ by nothing but the
topology the emulator presents.

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
Trap-page dispatch at GPA 0xF1000 (HLT opcodes -- guest calls trap,
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
build/test.ps1 -Jobs 8
```

`kernel-irqchip=off` required for bare-metal operation under QEMU.

### Renode (cross-architecture board testing)

Renode v1.16.1 provides cycle-accurate simulation for ARM64 and
RISC-V 64 targets. Install the runtime **once per box** -- extract the
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

### Killing codex-vm by NAME kills the whole fleet's

`Stop-Process -Name codex-vm -Force`, and every spelling of it
(`Get-Process codex-vm | Stop-Process`), is **machine-wide, not
workspace-scoped**. Several agents share this box, and every one of their
gates runs the compiler inside codex-vm, so a name-based kill reaches
into other workspaces and terminates compiles that have nothing to do
with you.

**What it looks like on the receiving end is not a dead VM. It is a
compiler that produced nothing, in a different phase every time.**
Measured 2026-07-28, four gate runs on one unchanged tree while a peer
was sweeping name-based kills between its own compiles:

| run | symptom |
|---|---|
| 1 | `FAIL: text round-trip` -- stage2 wrote **0 bytes**, hashing to the sha of the empty string |
| 2 | green |
| 3 | `FAIL: BVT` -- four unrelated tests failing at `(compile)` |
| 4 | `FAIL: semantic equivalence -- stage1 does not match source` |

Three different failures, none reproducible, none real. There is no crash
and no diagnostic: the phase simply yields a truncated or empty artifact.
So the agent on the receiving end reverts to a clean tree, gets one
green, and concludes the fault is in their own change. That happened
here, and the clean-tree green was itself a one-sample coincidence that
made the wrong conclusion look confirmed.

**Kill by PID, or filter on the command line.** `Start-Process` hands the
process back; keep it and stop that one. If you must sweep, scope it to
your own workspace path:

```powershell
# Yours only -- the path is what makes it yours
Get-CimInstance Win32_Process -Filter "Name='codex-vm.exe'" |
  Where-Object { $_.CommandLine -match [regex]::Escape($PWD.Path) } |
  ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
```

The same applies to `pwsh`, which carries every agent's session as well
as every build worker: a name-based kill there takes out other agents'
Claude sessions, not just their builds.

**If a gate fails in a compile-heavy phase and will not reproduce,
re-run before diagnosing.** `text-stage1`, `text-stage2`, `sem-equiv`
and the BVT's compile steps are the long codex-vm runs and therefore the
wide targets. A failure that vanishes on re-run was never in your tree,
and the expensive mistake is bisecting your own change to find it.

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
inverts it -- and it does not need to. It hashes the content, checks the
signature, checks the key the source pinned and checks the trust floor itself,
which is exactly what lets the messenger be untrusted. The host carries works;
it never carries trust, and `work-wire.ps1` emits `WORK` lines and never a `KEY`
line. A peer that answers with the wrong work is refused by arithmetic.

The consequence to know: **the resolution order lives in the host, so this works
only when a host drives the build.** A bare-metal stick has no fetcher.

A miss is not an error -- a peer that does not hold a work simply does not, and
the compile then fails at the gate if nothing else supplies it. An unreachable
peer is an error.

## Seed Management

The canonical seed is `seed/Codex.cdx` -- the signed, self-sustaining
CDX binary, bootable via codex-vm or QEMU multiboot.

### Seed Rebuild Procedure

**Pre-conditions:**
- All source changes are submitted
- The change justifies a rebuild. **The test is whether the compiler REACHES
  the code you changed, not which chapter you edited** -- whole-program DCE
  prunes definitions the compiler never calls, so a new function in a cited
  chapter can cost the binary nothing while a new `cites` line, a live
  registry arm, or the body of a definition the compiler calls all change it.
  Do not predict which case you are in: compare the hash of
  `build/output/Sut.cdx` against `seed/Codex.cdx` after the gate. Equal means
  no seed is needed. `build.ps1` proves the SUT is a fixed point of itself and
  never checks it against the depot seed. Full table and the two measured
  cases: `docs/DevelopersGuide.md`, Seed Rebuild Procedure.

**Steps:**
1. Run the full build: `build/build.ps1`. All phases must PASS.
2. Install new seed: `Copy-Item build/output/Sut.cdx seed\Codex.cdx -Force`
   Use `build/output/Sut.cdx` -- the signed SUT. Do NOT use
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
`(SUT === stage1 -- hard fixed point in one pass)`. A change to code
generation (e.g. `emit-prologue`) built from a pre-change seed is
**two-pass**: the stage0 seed lacks the new codegen, so its `Sut` carries
the change only in its emit logic, not yet in its own function bodies --
`Sut != stage1`, and the real fixed point is `stage1` (`NewSeed.cdx`, which
is unsigned). Installing `Sut.cdx` there leaves a seed that is one pass
short: it compiles correctly but is not byte-identical to itself
(`seed != seed-compiles-seed`), so it must not be trusted or copied up. The
fix is simply to **rebuild again** from that once-built seed -- the second
build converges one-pass with the change baked into the seed's own
prologues, and `Sut.cdx` is then the signed fixed point. Always rebuild
until the build reports one pass before submitting a codegen seed.

**But a codegen change can be VERIFIED without any of that.** `build.ps1`
leaves the SUT at `build-output/bare-metal/Codex.cdx`, which is the
compiler `compile.ps1` boots by default, so a gate followed by rebuilding
whatever app you care about compiles that app with the changed compiler
and no seed is written or submitted anywhere. The seed rebuild above is
how a codegen change LANDS, not how you find out whether it works. Worth
knowing before queueing for a seed cycle to answer a question one gate
already answers.

(The CDX fixed-point check compares content ignoring the signature bytes,
so a signed `Sut` and the unsigned `stage1` still register as one pass when
their code is identical. On copy-up, the gate is `Sut === seed` rebuilt on
the *target* workspace -- see `docs/Agents/PerforceProcess.md`.)

## Diverse Double-Compiling (and why the C# plug is maintained)

The fixed point proves the seed is a stable fixed point of itself. It does
not prove the seed is honest. A compiler carrying a Thompson trojan is a
perfectly stable fixed point too: it recognises its own source, re-injects
the payload into every compiler it emits, and the payload never appears in
any source you can read. Self-consistency cannot detect this, and neither
can reproducible builds, because a poisoned compiler is deterministic.

The answer is Wheeler's diverse double-compiling. Take the compiler under
suspicion `A` with source `sA`, and an **unrelated** compiler `B`:

```
X      = A(sA)          the official build -- our seed/Codex.cdx
stage1 = B(sA)          rebuild A's source with the unrelated compiler
stage2 = stage1(sA)     compile A's source once more with that rebuild
compare stage2 against X
```

Honest `A` gives `stage2 == X` byte for byte, because `A` and `stage1` are
the same program built two ways. A poisoned `A` injects into `X` but not
into `stage2`, and the comparison goes red.

### What does NOT count as a diverse compiler

**A different target architecture adds nothing.** Emitting the compiler to
RISC-V and compiling back to x86 looks like an independent path and is not
one, because the RISC-V binary is `A(sA, target=riscv)` -- the seed built
it, and the seed had its chance to inject on the way out. The same holds
for a different host, a different board, or a different emulator. Only a
different **implementation of the source-to-binary function** buys
independence. Cross-lane runs are worth having as differential codegen
tests; they are not a trusting-trust defense and must not be described as
one.

### `B` is the C# plug, and that is why it is maintained

`codex/plugs/csharp/` renders Codex IR into a single C# file. Built by
Roslyn, whose lineage has no relationship to ours, it is the only `B`
available that stays current as the compiler evolves. The retired reference
compiler under `old/` was a genuine hand-written second implementation and
is the formally cleaner witness, but it is frozen at an April language
version and can only ever check a seed nobody ships. **Do not delete the C#
plug as unused. Its job is this.**

`codex/plugs/csharp/emit-compiler.ps1` already runs most of the pipeline:

```
codex/compiler/*.codex --concat--> Codex.codex
                       --seed---->  compiler.ir
                       --csharp-plug--> Codex.cs
```

The script stops at writing the `.cs`. Its stated success criterion is that
`Codex.cs` compiles under `dotnet build`, and even that is a manual step the
script does not take. To turn it into a witness, carry it three steps
further: run `dotnet build`, run the result against `Codex.codex`, and diff
the x86 CDX it emits against `seed/Codex.cdx`. Nothing in `build/` invokes
any of this today.

### The residual hole, stated plainly

The seed sits upstream of this pipeline in **two** places, not one.

1. The `source -> IR` step is `compile.ps1`, which is the seed. A poisoned
   seed could inject into the IR that becomes the C#.
2. **`csharp-plug.cdx` is itself a Codex program the seed compiled.** The
   emitter that renders IR into C# is `codex/plugs/csharp/*.codex`, built by
   the same seed under suspicion, so `A` also controls the translation into
   `B`'s input language. This is easy to miss because Roslyn's independence
   is real and draws the eye to the last step.

**As written, the pipeline is not independent.** Two things narrow it:

1. Have the transpiled compiler do its own `source -> IR` step. `Codex.cs`
   is the whole compiler, parser included, so once Roslyn has built it, it
   should read `Codex.codex` itself rather than consuming IR the seed
   produced. That leaves `A` responsible only for generating the C# text.
2. What remains must survive rendering into **readable C#**. Thompson's
   attack works because binaries are unreadable; a payload forced through
   ~300 KB of generated source has to exist as text that can be grepped and
   diffed across re-emissions.

That is narrower than real DDC and should be reported as narrower. The full
claim requires a `B` the seed never touched.

## Sampling Profiler

Two independent sampling profilers, both driven by the PIT tick (~18 Hz).
codex-vm now delivers timer interrupts to a compute-bound guest (a
55 ms kicker thread cancels the VP each period), so a self-compile is
sampled -- before 2026-07-07 the timer only fired while the guest was
halted, and long compute ran blind.

**Host sampler (recommended -- bias-free).** Set `CODEX_VM_PROFILE=<file>`
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
by compiling the compiler source NON-repl (the `<out>.map` sidecar) --
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
(function never entered -- the inliner consumed every call site),
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
(the `!EXC` dump protocol) -- INT3/vector 3 always belongs to the guest.

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
compiler's own functions -- not compiled test programs. Test CDX
binaries emit their own MAP block in the build log (visible between
`MAP:` and `MAP-END` lines).

## Native Debugging Toolkit

All debugging uses codex-vm and the PowerShell harness. No GDB, no
WSL, no external tools. The compiler embeds a binary MAP1 symbol map
in every CDX (2600+ functions, ~79KB), and the harness resolves
addresses automatically.

**Resolve a fault with the map belonging to the binary that faulted.**
codex-vm's backtrace loads `seed/Codex.map` whatever it actually booted, so
every symbol in a boot-PE or app fault is confident nonsense. Re-resolve
against that binary's own map (`build-output/boot.map` for the boot PE).

**`MMIO: cannot size instruction at RIP=...` is not a demand-paging
message.** It fires only when the faulting instruction cannot be SIZED. To
tell a livelock from a runaway allocation, group the reported GPAs: repeats
mean a livelock, unique and ascending means the guest is allocating.

**A UEFI PE stub is not `__start` and gets none of its side effects.** The
stub calls `opening` directly, so everything `emit-start` establishes -- the
SystemTable at `uefi-systab-addr`, the process table, capability grants, the
IDT, the PIC -- is simply absent unless the stub does it too. The failures
are all action-at-a-distance: a null SystemTable presents as a call to
address 0 from whichever `uefi-*` helper happens to lack a null guard.

### Crash Reports

When a crash occurs during batch compilation, the harness prints a
resolved crash report:

```
CRASH in peek-qword+0x58 (page fault, CR2=0x000100000000)
  RIP   0x00107DFE  peek-qword+0x58
  callR 0xBDFFFFC8
  R10   0x00600000  (heap @ 0 MB)
  Stack trace (heuristic):
    S[58] 0x0010AB54  opening+0x2C
    S[68] 0x001116A3  __start+0xDE2
```

The `!EXC` line from the guest's exception handler includes RIP, all
callee-saved registers, CR2, callR, and 16 stack qwords.
`Format-CrashReport` (vm-config.ps1) resolves every value in the code
range (0x100000-0x400000) to a function name.

**The dump prints the syscall MSRs**, read back off the virtual processor
rather than out of codex-vm's own shadows:

```
  EFER=0000000000000d01 STAR=0000000800000000 LSTAR=0000000000147098 SFMASK=0000000000000200
```

Without them, a guest that jumped somewhere absurd through an unprogrammed
`LSTAR` and one that got there any other way produce the identical picture, and
the fix for the first is invisible when it works. That ambiguity is exactly what
made the UEFI boot path look unfixed after it had been fixed.

**A guest `rdmsr` is answered from a host-side shadow, not from the
processor.** `handle_msr` keeps `msr_efer`, `msr_star`, `msr_lstar`,
`msr_sfmask`, `msr_cstar`, `msr_kernel_gs_base` and `msr_apic_base`, and
anything not in that list reads zero. So any place codex-vm sets one of those
registers on the VP directly has to set the shadow with it: a guest doing the
ordinary `rdmsr; or rax,1; wrmsr` to add SCE would otherwise read 0, write 1,
and clear LME and LMA in the middle of long mode. The UEFI entry path and the
UEFI trampoline fixup both set `EFER=0xD01`, and both now set `msr_efer` too.

**`callR` is the interrupted RSP, not a return address**, whatever its
name suggests. The dump reads it from offset 64 of the handler's own
stack, which is the RSP slot of the CPU's interrupt frame. It is
normally a stack address near the top of RAM and resolves to no
symbol; if it ever does resolve, that is a coincidence of the value
landing inside a function's byte range, not a caller.

Only part of the `S[]` dump is the interrupted program's stack. The
guest dumps sixteen qwords starting at the handler's RSP, and the
first eleven are the five registers the handler pushed (RDI, RSI, RDX,
RCX, RAX) followed by the five-word interrupt frame (RIP, CS, RFLAGS,
RSP, SS). Real stack begins at `S[0x50]`. This is why the trace is
labelled heuristic: the harness cannot tell a return address from a
saved register or a stale slot, so it prints whatever resolves to a
symbol and drops the rest.

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
| `b <fn\|0xaddr> [if reg=val]` | Set breakpoint. **The `if` condition does not discriminate** -- see below; use `-hbreak` |
| `w <addr> [size]` | Set memory watchpoint |
| `sym <name>` | Look up symbol address |
| `q` / `quit` | Exit VM |

**The shell's `if reg=val` condition does NOT discriminate. Use `-hbreak`
instead.** The form parses and arms, and it breaks whatever the register
holds -- proven with a condition that can never match, which broke anyway.

There are two reasons and the second is fatal to the whole approach. The
check in `dbg_command_loop` runs only for `vec == 1`, while an INT3
breakpoint arrives as `vec == 3`, so it is never consulted. And even
wired to vector 3 it could not work: **vector 3 belongs to the guest.**
Its handler runs before the host sees the trap, so the condition would be
evaluated against the handler's registers rather than the callee's --
measured, `rdi` reads `0x33` every time.

An unconditional breakpoint is also **terminal** here: the guest's
handler dumps `!EXC` and halts, so a run reports exactly one break
however many times the address is executed. Counting breaks does not
count calls.

`-hbreak` exists because of all three. It arms a debug register instead,
so the `#DB` is intercepted by the host and never reaches the guest IDT,
the condition sees the real register file, and an unmatched hit resumes.

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

The debugger compiles into the same binary -- `-debug` activates it.
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
PH:LEX
PH:PARSE
PH:DESUGAR
PH:SCOPE
PH:CHECK
PH:LOWER
PH:FRONTEND
DBG:emit defs=412
SIZE:2176384
```

**A `PH:` line means that phase COMPLETED**, so a crash names the phase
after the last line printed. This is the only thing that identifies the
phase when a starved deck corrupts the heap: the crash site is a shared
runtime helper (`__linked_list_to_list`, `copy-sx-text`) that half the
frontend calls, and four `-Decks` values that all die in DESUGAR name
three different helpers between them. Reach for this before theorising
about which phase ran out of room.

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

### `OUT OF MEMORY` is the STACK guard, and the two numbers that diagnose it are already in registers

`__out_of_memory` is reached from `cmp rsp, r10; jb __out_of_memory` in every
non-leaf prologue, so it fires on a stack/heap COLLISION, not on a heap
allocation failure. `codex/test/exc-stack-heap` is the deliberate collision and
its whole expected output is the one line. The message names the wrong resource
and has repeatedly been read as heap exhaustion.

The handler opens by saving both sides of the comparison and then prints
neither:

```
mov rbx, rsp      ; RBX = the faulting RSP
mov r12, r10      ; R12 = the deck pointer at the trip
```

It ends `cli; hlt; jmp -6`, so it spins with both intact and any register dump
taken after the message reads them:

```powershell
# under OVMF
pwsh build/boot/test-ovmf.ps1 -Img <img> -Out <png> -Seconds 20 -MonCmds 'info registers'
# then, to see the recursion cycle, dump the stack at RBX and resolve the
# return addresses against the .map that compile.ps1 wrote for THAT binary
pwsh build/boot/test-ovmf.ps1 -Img <img> -Out <png> -Seconds 20 -MonCmds 'xp /32gx <RBX>'
```

Subtract the heap base the loader installed (`cdx-to-pe.ps1` writes it to
`deck-pos-addr`, 0x7030) and the split says which side ran away. Measured on
`seed/Codex.img` 2026-07-30: stack 489.1 MB, heap 22.9 MB, meeting with a
32-byte gap inside a 512 MB region. A repeating frame carrying the SAME pointer
every time is mutual recursion; the map resolved it to
`dev-console-poll` / `dev-console-serial-poll` / `dev-console-idle`.

**Do not read `deck-pos-addr` or `heap-hwm-addr` post-mortem to decide this.**
Both are the wrong instrument on a UEFI boot and both answer plausibly:

- `heap-hwm-addr` (0x7038) CANNOT MOVE there. Its only updater is
  `emit-update-heap-hwm`, whose one call site is inside `emit-start` after
  `opening` returns, and the UEFI stub calls `opening` directly so `__start`
  never runs. It holds whatever the loader wrote, so "hwm equals the deck
  position" is a tautology on that path rather than evidence.
- `deck-pos-addr` (0x7030) is stale, because the live deck pointer is R10 and
  the cell is not written on every allocation. It read as the base while R10
  was 22.9 MB above it.

### compile.ps1 Debug Flags

| Flag | Purpose |
|------|---------|
| `-Break "name"` | INT3 at function entry; use with codex-vm `-debug` for interactive shell |
| `-DebugMode` | Phase markers (`DBG:frontend`, `DBG:emit`) |
| `-Poison` | 0xCD fill in `__alloc` (catches uninitialized fields) |
| `-Repl` | REPL loop (for batch compilation) |
| `-Decks <N>` | Scale every phase deck floor to N% of the `BuildSettings` defaults (100 = defaults). Sends `decks=N` on the mode line. |

### Running a compile from a second workspace: set `[Environment]::CurrentDirectory`

**Symptom:** you edit a file, compile, and the output binary is
**byte-identical** to the one before your edit. It reads exactly like "my
change had no effect", which is the most expensive possible false reading.

**Cause:** `compile.ps1` resolves cites with `-Repo '.'`, and .NET file APIs
resolve `.` against `[Environment]::CurrentDirectory` -- which PowerShell's
`Set-Location` does **not** update. So in any workspace other than the shell's
default, a relative `-Src` **and every cited quire** silently resolve to the
*other* workspace. An absolute `-Src` fixes the source and leaves the quires
still pointing at the wrong tree, which is worse: the compile succeeds, most
of the program is yours, and one cited chapter is somebody else's copy.

**Fix:** set both, every time:

```powershell
Set-Location D:\Projects\NewRepository-<agent>-main
[Environment]::CurrentDirectory = 'D:\Projects\NewRepository-<agent>-main'
build/compile.ps1 -Src codex\test\apps\foo.codex -Out out.cdx -Log out.log -Kernel seed\Codex.cdx
```

**Detect:** hash the output. Two compiles of genuinely different source that
produce the same SHA-256 have not read your file. This is the cheap check and
it is the only one that fires before you start debugging the wrong program.

### The deck knob, and the deadlock it exists to break

Phase deck floors live in `BuildSettings.codex` and are **compiled into
the compiler**. `build/check-constants.ps1` hashes them and warns when the
source and the seed disagree -- but it is only a warning, and `build.ps1`
refreshes the hash at the end of every build.

That creates a deadlock. If a source grows past the floor, the compile
fails with `CDX9002` -- and raising the constant does not help, because
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
raise `CDX9002` -- the parse keep-deck copy writes past the floor into
the scratch it is still reading and the compile dies in a `#GP`
(`!EXC=0d`) with no diagnostic, because the post-copy overflow check
never runs. That is a pre-existing property of under-reserved floors,
documented in `BuildSettings.codex`; the knob simply makes it reachable
on purpose. `decks=5` on the compiler's own source demonstrates it.

The survey-multiplier system (and its `-Survey` override) was deleted
2026-07-07: phase decks are fixed generous floors and the heap range
[6 MB, 2 GB) is demand-paged -- physical memory commits on first touch.
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
`docs/PM/Active/Stories/TheSilentKeyboard.md`, and its first recommendation is
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
is written dereferences `0xCDCDCDCDCDCDCDCD` -- a non-canonical address,
and therefore an immediate page fault instead of a plausible zero.

**This is a release gate, not a routine one.** Run it before publishing
the seed to the public mirrors. If the battery passes against a poison
seed, the compiler has no uninitialized-field dependencies -- the zero
fill is a safety net and not a patch holding something together.

Why it matters: the REPL loop resets R10, deck-pos and heap-hwm between
compilations but does **not** zero the freed memory. Without the calloc,
the second compile in a batch allocates records on top of the first
compile's live pointers and type tags. The first boot looks clean because
hardware zeroed the heap; the second one does not.

### How to Run a Poison Build

```powershell
# 0. Save the kernel you are about to overwrite. Step 3 replaces it.
Copy-Item -Force build-output/bare-metal/Codex.cdx build-output/kernel-backup.cdx

# 1. Concat compiler source
build/concat-codex-self.ps1 -CodexDir codex/compiler -OutFile build/output/Codex.codex

# 2. Compile a poison seed (0xCD fill instead of zero)
build/compile.ps1 -Src build/output/Codex.codex `
    -Out build/output/poison-seed.cdx `
    -Log build/output/poison-build.log -Repl -Poison

# 3. Run the battery against the poison seed. Name the tiers you want:
#    a bare invocation is the `lang` tier only, which is NOT the full battery.
build/test.ps1 -CodexCdx build/output/poison-seed.cdx -Tier all -Jobs 8 -ApprovedBy damian

# 4. PUT YOUR KERNEL BACK. Step 3 does not.
Copy-Item -Force build-output/kernel-backup.cdx build-output/bare-metal/Codex.cdx

# Zero failures. Any failure means an uninitialized field was read during
# compilation. The crash CR2 will be 0xCDCDCDCDCDCDCDCD -- look up RIP in
# the symbol map to find the function that dereferenced the bad pointer.
```

**Steps 0 and 4 are not tidiness.** `-CodexCdx` does not point the battery at
another compiler, it **copies that compiler over
`build-output/bare-metal/Codex.cdx`** (`test.ps1`:170-173) and never restores
it. So the poison seed stays installed as the kernel every later
`compile.ps1` boots by default, and the next thing you compile is compiled by
the poison compiler while you believe otherwise. Measured 2026-07-28: after a
poison battery the kernel was still `3AF5763C`, the poison seed, and nothing
said so. `compile.ps1` prints the kernel and its digest on every run for
exactly this class of mistake -- read that line.

**`-Jobs 8`, and a release run is not an exception.** This recipe said `-Jobs 4`
until 2026-08-02 and the 4 was the dead XMP workaround described under "The
parallelism default" in `ExaminersAssay.md`: the box's DDR5 was running a
profile it was not stable at, that was fixed 2026-07-22, and the harness default
went back to 8 while the release recipes kept the halved number. Measured
2026-08-02 on the poison run that carried it: **977 s in the compile phase at 4
slots on a 12-core box.** Damian's ruling is that 8 is the standard everywhere,
including release proofs. The contention classes that motivated the caution are
crash-shaped (`FAIL_RUNTIME` with no uart, exit 4 with zero diagnostics) and
both retry paths already catch them -- `Invoke-StandaloneRetry` here, and the
sweep re-runs its own no-diagnostic units alone.

**A bare `build/test.ps1` is the `lang` tier, not the full battery.** The
command above says `-Tier all` on purpose. A poison run over `lang` proves
`lang`, and the apps, forewords and lib tiers allocate shapes it never
reaches.

**Do not expect a specific pass count here.** This text used to say "Expected:
105 pass, 0 fail" against a battery that no longer exists; the `lang` tier
alone measured 674 total / 649 pass / 25 skip on 2026-07-28. The number to
check is **fail=0**, and the rollup's own run-over-run delta (`newly red`) is
a better instrument than any count written down in a document.

### The two poison modes are different instruments

`-Poison` fills every `__alloc` block with `0xCD`, so a field read before it
is written faults instead of reading a plausible zero.

`-PoisonCompact` is the other axis: it poisons each phase's scratch **after
that phase compacts**, with a distinct byte per phase -- 161 lex, 162 parse,
163 desugar, 164 scope, 165 check, 166 lower, 167 resolve, 168 lift, 169
frontend (`codex/compiler/opening.codex`). A use-after-compact therefore
faults with a byte that **names the phase whose memory was reused**, which
`-Poison` cannot tell you. Run both; a clean `-Poison` says nothing about
phase discipline.

## Release-to-Public Gate

These steps run ONLY when publishing the seed to the public mirrors
(GitHub, GitLab) -- never on routine seed rebuilds or copy-to-main. The
day-to-day gates (text + CDX fixed point, sample battery) already prove
correctness; the items here are public-facing polish and are not usually
needed internally.

1. **Poison build passes** (above) -- the seed has no uninitialized-field
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
   regardless -- the text map only feeds `-Break` and `Resolve-Rip`.
