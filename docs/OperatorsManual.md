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
  Open BOTH the `.c` and the `.exe`, and **after any merge-down that touched
  the `.c`, relink before running anything**: a resolve takes main's binary
  wholesale, so the exe on disk is no longer the one your source builds and
  every measurement from it is about somebody else's revision.
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

   **Its operator precedence table is a COPY of the compiler's and has
   to track it.** `Strip-RedundantParens` decides whether a paren is
   load-bearing by comparing precedences, so a table that disagrees with
   `operator-precedence` in `codex/compiler/Syntax/ParserCore.codex`
   makes the leg either reject correct output or accept wrong output.
   Measured 2026-08-11: the shipped table gave `&` and `|` the SAME
   precedence where the compiler gives Ampersand 3 and Pipe 2, so
   `(a | b) & c` normalized to `a | b & c` and the two compared EQUAL.
   The table now carries the compiler's own numbers. If you change
   `operator-precedence`, change this table in
   `codex/build/comparecodexsemanticScript.codex` in the same CL.

   **Be precise about what that cost, because it is narrower than it
   sounds.** The compiler parses these correctly and the text emitter
   preserves the parens, so no verdict this leg ever returned was wrong.
   The old table stripped the parens from the source side AND the stage1
   side equally, so the two still matched. What it produced was a BLIND
   SPOT: measured 2026-08-11 over the compiler's own source, four
   definitions normalize differently under the corrected table --
   `join-title-parts` and `scan-class-instance-defs`
   (`Syntax/Parser.codex:1245`, `:1522`), `normalize-list-insert-at` and
   `normalize-list-prim` (`Types/TypeChecker.codex:309`, `:250`). All
   four mix boolean `|` and `&`. For those four, the leg was comparing a
   form whose grouping had already been destroyed, so an emitter that
   reassociated `|` against `&` there would not have been caught.

   Two things it still does NOT know, both pre-existing and both
   unmeasured against real output: its tokenizer has no `|>` (it lexes
   as `|` then `>`), and no `===`, `~` or `~0`, all of which the
   compiler's table ranks. Nothing has yet shown these matter on the
   compiler's own source, which is the only corpus the leg runs on.

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
build/test.ps1 -Jobs 4                # Parallel test (4 batch slots; 4 is the standard since 2026-08-27, RAM-bounded)
build/test.ps1 -All                   # Include foreword + app tests
build/test.ps1 -Fatal                 # Include fatal (GPF/exception) tests
build/build.ps1                       # Full pipeline (all gates)
build/desk.ps1                        # The desktop, in a window, ~1.5s to paint
```

### The scripts under `build/` are generated, and the generators are stale

Most of `build/` is emitted by a Shell DSL: 49 generators under
`codex/build/*Script.codex`, each printing one script. `build/build.ps1`,
`build/test.ps1`, `build/compile.ps1` and `build/bvt.ps1` are all in that set.

**They have not been the source of truth for a long time. Do not regenerate
one to fix a bug in it.** Measured 2026-08-03 by compiling every generator
against the depot seed and diffing its output against what ships: **39 of the
40 generators with a live target have drifted, about 6,300 lines in total.**
Only `vm-config` still matches. Nine more generators emit scripts that no
longer exist anywhere. Nothing has ever regenerated or diffed any of them, so
the drift accumulated silently while the shipped scripts were maintained by
hand.

The shipped script is the maintained side in every case measured, and the
generator is the abandoned one, so a regeneration is a downgrade rather than a
refresh. Two examples, because the shape matters more than the count:

- **`compile.ps1`'s generator declares 3 parameters against the shipped
  script's 30** (measured 2026-08-03: it has `Src`, `Out` and `Log` and
  nothing else), and its `Stage0` is the hardcoded literal
  `build-output\bare-metal\Codex.cdx` with no `-Kernel` path at all. A
  regeneration would delete `-Kernel`, `-Text`, `-Measure` and `-Decks`,
  which `build/build.ps1`, `deck-floor-test.ps1` and most recipes in this
  document all pass. **The gate would go red on the first run and the cause
  would read as a compiler regression.** This is the sharpest instance and
  fester found it.
- **`test.ps1`'s generator does not emit `-ApprovedBy` at all.** Regenerating
  it removes the stop that keeps the full battery from being launched by an
  agent, and removes it silently. `stress-sweep.ps1` was the same until it
  was backported.
- **Every unconverted generator still defaults to `-Jobs 4`.** That number was
  overturned on 2026-08-02; the shipped scripts say 8.

So the generators encode at least two rulings that have since been reversed.
Until a generator is backported, **hand-editing the `.ps1` is the correct
thing to do** and regenerating it is not.

```powershell
build/check-generated-scripts.ps1              # drift table for all 49
build/check-generated-scripts.ps1 -Only test   # one, by emitted name
build/check-generated-scripts.ps1 -Diff test   # the actual drift, line by line
```

It exits 1 when anything has drifted and takes about 40 seconds for the whole
set (one VM boot compiles them all). It is deliberately **not** wired into
`build.ps1` or the battery, and it deliberately has **no** write mode: with the
generators in their current state, a bulk regenerate would destroy the working
scripts. Use `-Diff` and port the change back into the generator by hand.

When a generator is backported and `-Only <name>` reports `match`, that script
flips: the generator becomes the place to edit, and hand-editing the `.ps1`
becomes the thing that reintroduces drift. **`stress-sweep` and
`lint-unused-cites` have flipped**; `vm-config` reports `match` but is still
almost entirely `ScRaw`, so it is not yet a place to edit either. Everything
else is still hand-edited.

## The Desktop On The Dev Box (`build/desk.ps1`)

```powershell
build/desk.ps1                                  # interactive window at 1600x900
build/desk.ps1 -Width 1920 -Height 1080         # any mode codex-vm will give you
build/desk.ps1 -Force                           # recompile even if the CDX is current
build/desk.ps1 -Shot shot.bmp                   # headless, one frame, then exit
build/desk.ps1 -Keys '4000:4'                   # scancode timeline: 33 = f, 4 = 3, 1 = Esc
build/desk.ps1 -Rtc 2026-07-30T06:00:00         # freeze the taskbar clock
build/desk.ps1 -Disk seed/Codex.img             # give the Files pane a real ESP
```

**Two scales, and they step at different widths.** `ui-scale` drives the
hand-drawn surfaces (topbar, welcome window, clock, cursor) and doubles at
1024. `ui-wscale` drives the WIDGET layer and doubles at 1600, because the
widget tree is laid out in LOGICAL pixels at `w / ui-wscale`: doubling it
does not enlarge the chrome so much as halve the ROOM, and every app tree in
the depot is written against 1024x768. Measured 2026-08-11: at scale 2 a
1024-wide panel lays out in 512x384 and the calculator loses four of its
fifteen keys off the bottom, so the widget step stays at 1600 where half the
panel is still at least as much room as 1024x768 ever gave. Below 1600 the
widget layer is scale 1 and the desk is pixel-for-pixel what it always was.

**`-Disk` copies the image to `build-output/` and attaches the copy.**
codex-vm writes back and flushes to the host, so attaching a depot
artifact directly would let a guest edit it. With `seed/Codex.img` the
Files pane browses `ESP:/` and lists `EFI/`, `SOURCE.SRC` and
`CODEX.CDX`, which is the compiler reading its own boot medium. Without a
disk the pane says `no FAT ESP on the boot medium` and the font falls
back; both are correct and neither is a fault.

**An F12 shot from inside the desk takes 16 to 110 SECONDS in the bed, and a
short deadline renders that as "F12 does not fire".** `med-selected` is unset
under DeskVm, so every sector operation of the ~3 MB BMP write pays the
AHCI+NVMe probe chain before falling through to IDE. Any capture deadline
under two minutes reports a working key as a dead one -- twice filed as a
finding here and twice refuted. Give it `-screenshot-delay 120000` or more,
and verify by scanning the disk copy (`build-output/desk-Codex.img`) for the
`SH*.BMP` FAT directory entry (`S`, `H`, six digits, `B`, `M`, `P`) rather
than trusting the on-glass verdict. **The chaser that settles "did the key
arrive" without waiting:** inject `S` after F12; if shadows do not toggle the
loop is inside the shot, meaning the key landed and the arm is merely slow.

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

`tools/codex-vm.exe` -- a ~12,800-line C program (measured 2026-08-05) using Windows Hypervisor
Platform (WHP). Build with `tools/build-vm.ps1`.

**A guest CAN detect codex-vm through the HYPERVISOR bit since 2026-08-18.**
CPUID leaf 1 ECX carries bit 31 (the conventional "am I virtualised" answer)
and nothing else; leaves 8000_0002h..04h answer the brand string
`codex-vm virtual CPU (WHP)`. Until 2026-08-18 leaf 1 ECX read ZERO and the
brand leaves were absent, so a guest was told it was on bare silicon with no
name. Otherwise unchanged: the vendor string is `GenuineIntel`, the maximum
basic leaf is 1, and leaf 1 EDX carries a real feature set (FPU TSC MSR PAE
APIC CMOV MMX FXSR SSE SSE2). The corollary for anything decoding CPUID: a
feature line with nothing but the hypervisor bit on this host is the host,
not a broken decode. The diagnostic ladder's cpu stage reads `hypervisor`
here by design.

**Never kill codex-vm by process name.** Several agents run this box at once,
each booting VMs out of its own `D:\Projects\Cobblestone-<agent>\`, so
`Get-Process codex-vm | Stop-Process -Force` takes out **every agent's** VMs and
not just yours. A gate or a battery that dies for no reason someone can explain
is what that looks like from the other end. Filter on the executable's path:

```powershell
Get-CimInstance Win32_Process -Filter "Name='codex-vm.exe'" |
  Where-Object { $_.ExecutablePath -like 'D:\Projects\Cobblestone-<agent>\*' } |
  ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
```

The same caution applies to reading a stray VM as evidence: a codex-vm you did
not start is usually another agent working, not a leak of yours. Check
`ExecutablePath` before concluding anything about it, and before killing it.

Build note: the linker fails with `LNK1104` if a codex-vm is holding
`tools/codex-vm.exe` open, so stop **your own** VMs (as above) before
`tools/build-vm.ps1`.

#### codex-vm REFUSES AN UNRECOGNISED ARGUMENT (since 2026-08-27)

`codex-vm: unrecognised argument '-x'` on stderr, exit 2, on the first one it
meets. Before that it dropped them in silence, and that was the defect under
the one below: a typo, a renamed flag, a flag deleted from the emulator and a
flag that was never built all presented to the caller as a clean launch, so a
green boot was not evidence that a flag took effect. The parse loop ran 121
`else if` arms and ended with no final `else`; it has one now.

Only a leading `-` reaches the refusal, because every flag's value is consumed
by its own arm. `-run-list` and `-run-list-wall` are answered in an earlier
loop that returns before this one runs, so they are unaffected.

**If a command line that used to work now exits 2, the flag it names was never
doing anything.** That is what it found on its own first run: `-serial stdio
-timeout <n>` in `build/test-exception-handler.ps1`, neither parsed in any
revision, with `Wait-Process -Timeout` beside them doing the work `-timeout`
appeared to do. Both dropped; the harness still passes 5 of 5.

The instrument, when a flag's effect is in doubt, still works and no longer has
to be reached for first: the parse sites are all `strcmp(argv[i], "-...")`, so
grepping that pattern yields the complete set codex-vm accepts. It answered 120
flags on 2026-08-24 and 121 on 2026-08-27; re-measure rather than quoting
either (L-COUNT).

#### `-data-port` and `-ctrl-port` do not exist, and the usage banner used to say they did

**No revision of `tools/codex-vm.c` has ever parsed either flag** (#1 through
#110 checked 2026-08-24, and the flag table below is the full set it does
parse). They are out of the usage banner as of 2026-08-27 and were not
implemented: with the refusal above in place, advertising them would advertise
two flags the program answers with exit 2. Passing either now says so.

Until then, because unknown flags were dropped in silence, the command line
looked accepted: the guest booted, found nothing on the wire, and halted inside
500 ms.

That kills `Start-VmRun`'s codex-vm path in `build/vm-config.ps1`, which builds
exactly those two flags. `Start-CodexVmRun` reads the fast exit as a failed
launch, retries four times and returns `$null`, so a caller reports
`FAIL: VM did not start` and the real cause is a flag that was never there.
Wherever codex-vm is present -- every box -- these are unrunnable:
`codex/plugs/elf/extract-x86-output.ps1`, `build/test-disk-compile.ps1`,
`tools/sim-test.ps1`, `build/gdb-watchpoint.ps1`. The QEMU fallback branch does
carry a real serial wire, so the path works only where codex-vm is ABSENT,
which is nowhere.

`tools/test-codex-vm.ps1` passes the same two flags directly and also invokes
`codex.build\sample-compile-selfhost.ps1`, a path that no longer exists.

Whether this gets the serve mode built or the callers deleted is unclaimed.
#### CLI Flags

```
codex-vm -kernel file.cdx [options]
```

| Flag | Default | Description |
|------|---------|-------------|
| `-kernel <file>` | (required) | CDX or multiboot kernel to boot |
| `-mem <MB>` | 3072 | Guest RAM in megabytes. Binaries compiled by seeds older than CL 7209 require more than 2048 (their boot stack lands in the demand-paged range below 2 GB); current seeds boot at any size from ~128 MB up. **The size REPORTED to the guest is capped at 3040 MB**, so anything above that changes nothing without `-mem-nocap`: see "The guest heap ceiling is 3040 MB" |
| `-mem-nocap` | off | Report the real `-mem` to the guest instead of the 3040 MB cap. Only for a run that draws nothing: the heap may then grow through the GPU and GOP windows, which are at fixed GPAs |
| `-input <file>` | -- | Pre-load file into serial ring buffer (source input). **The image needs a TRAILING ZERO BYTE after the source** -- see below |
| `-output <file>` | -- | Capture serial output to file |
| `-disk <file>` | -- | Attach IDE disk image as the primary channel's MASTER (read/write, flushed to host) |
| `-disk2 <file>` | -- | Attach a second image as the primary channel's SLAVE, reached by `block-select 1`. A position with no image behind it answers the floating bus (`0xFF` on the signature registers, `0x00` on status) so the guest's own detect reports it absent. Before this existed there was one image behind every drive position, `dm-enumerate-drives` reported four drives on a machine with one disk, and "Install Codex to Drive" pointed at any of them repartitioned the boot disk. Status answers `0x00` rather than `0xFF` deliberately: `emit-ata-bring-up` runs a bounded BSY wait before it reads the signature, and a floating `0xFF` there costs a million port INs, which is a million VM exits, on every diskless boot |
| `-headless` | off | Suppress VGA/GOP display window |
| `-board-mmio` | off | Commit and map host RAM at the three board register windows that sit above the RAM ceiling: RP2040 SIO (0xD0000000), Cortex-M PPB/SCB (0xE0000000), BCM2711 peripherals (0xFE000000, 9 MB). A board driver's register access then reads back what it wrote -- the same fidelity the six sub-3GB boards get from falling inside guest RAM. **Opt-in, because it shadows the HDA and xHCI BARs**: audio and USB are dead while it is on, which a board test does not care about. Required by the pi4 / rp2040 / stm32l4 driver tests; `build/boards-test.ps1` passes it. |
| `-xhci-no-root-kbd` | off | Unplug the HID keyboard on xHCI root port 2, leaving the high-speed hub on root port 4 as the only route to a keyboard. The bus walk takes the first keyboard it finds and the root port comes first, so this is what lets a test drive the hub path with an unmodified guest binary. |
| `-xhci-hub-tiers <N>` | 1 | How many hubs to stack on xHCI root port 4. `1` is a high-speed hub with a full-speed keyboard below it. `2` inserts a full-speed hub in between, which is what tells apart a driver that reads the transaction translator off the immediate parent from one that carries the nearest high-speed ancestor's down the walk -- a full-speed hub has no translator of its own, so the keyboard below it is still served by the high-speed hub two tiers up. Route strings run to two nibbles (`0x11`). A monitor hub in front of a keyboard hub is this topology. |
| `-xhci-ports <N>` | 4 | Root ports the controller REPORTS (HCSPARAMS1 and the Supported Protocol capability), 4 to 32. The four modelled devices stay on ports 1-4; ports above them are empty and POWERED, so PORTSC reads non-zero there, which is what a real wide controller looks like. Use it on anything that INDEXES A TABLE BY PORT NUMBER: `xhci-diag-ports` wrote `20 + port` into a band that ends at 27 and laid PORTSC over the handback flag gating `kbd-pump`. Four ports here and eight in QEMU both sat on or under that boundary, so neither bed could show it; the ASUS reports 26. |
| `-usb-setcfg-fault-once <N>` | 0 | As `-usb-setcfg-fault`, but the fault applies to the FIRST SET_CONFIGURATION only and then clears. A TRANSIENT failure and a permanent one want opposite fixes in the host, so a bed that can only produce the permanent kind cannot show that a reading distinguishes them. Pair with the probe's `retry:` field: permanent gives `sc1=4 sc2=4`, transient gives `sc1=4 sc2=1`. |
| `-usb-bot-drop <N>` | 0 | Swallow the Nth transfer event on a BULK endpoint (EP0 enumeration untouched), counting from 1. The data still moves and the completion code is still success; only the EVENT goes missing, so the guest spins its `xhci-fuel` out and reads no completion. This is the worksflight 2026-08-09 signature exactly: a 32 KB data phase answering `msc-ce-no-event` with no stall and a volume left clean on all four FAT questions. Nothing else can reach a guest's timeout path, and everything behind it -- Stop Endpoint recovery, the latch drain, BOT Reset Recovery, the chunk retry -- was unexecuted code before this flag existed. Also implemented with it: Bulk-Only Mass Storage Reset (`bmRequestType 0x21`, `bRequest 0xFF`), which clears the model's in-flight CBW; without it a retried command arrived while `bot.active` was still set and was consumed as write data. |
| `-usb-bot-drops <K>` | 1 | How many CONSECUTIVE transfer events `-usb-bot-drop` swallows, starting at N. **One drop is always recovered** -- the driver resets, re-issues, and the write completes (`rty=3`), so with the default K no bed can produce a REFUSED write at all, and the shape the 2026-08-19 metal sitting showed had no bed reproduction. K=4 at index 500 reaches it on `diag.img`: `sink state=write-refused wr=0 cc=256 lba=3574 rty=2 ph=1 after=0`, the medium refusing a small write after the big one as well. The index decides which state you get and the states are distinct, so sweep it rather than assuming one value covers the path (measured 2026-08-19: 300 recovers, 500 refuses, 1000 writes and fails the readback). |
| `-usb-bot-drop-len <N>` | 0 | Swallow the transfer event for any bulk WRITE while the BOT command in flight carries N bytes or more. The size-keyed sibling of `-usb-bot-drop`, and the reason to prefer it: **an ordinal is not a property of the thing under test.** `-usb-bot-drop 500` names the 500th event since boot, so inserting a stage anywhere upstream moves the drop into a different phase silently. Measured 2026-08-20: adding the xhci stage moved the sink arm's drop out of the data phase into the MOUNT, and the arm reported `mount-fail`, a plausible word rather than a nonsense one. Keyed to `dCBWDataTransferLength` the same flag means the same thing whatever the guest does first, and it gives a bed a THRESHOLD for an instrument to reproduce: with `-usb-bot-drop-len 16384 -usb-bot-drop-len-max 16384` the sink ladder must answer `rung=32`, and at 8192 it must answer `rung=16`. Two arms whose answers move with the bed are what separate an instrument that measures from one that always says the same thing. **WRITES ONLY**, and that is load-bearing: the first version refused reads too, the mount reads more than 16 KB, and the stage under test reported `no-medium` instead of a threshold. |
| `-usb-bot-drop-len-max <M>` | 0 (no upper bound) | Bound `-usb-bot-drop-len` above, so the lever refuses a BAND of command sizes rather than everything from N upward. It exists because the diagnostic bank writes 32,768-byte commands of its own: without it, every threshold small enough to refuse a rung also refuses the bank, the bank dies before the stage runs, and the row reads `no-medium`. With `N == M` the lever refuses exactly one command size, which is what a control wants. **It cannot rescue an ordinal-keyed arm whose target is the same size as the bank's writes:** the 2.7 MB sink stage writes 32,768-byte commands and so does the bank, so no threshold separates them, and `diag-arm.ps1`'s `sink-drop` stays ordinal-keyed for that reason. |
| `-usb-bot-revive-on-reset` | off (the death is LATCHED) | Pairs with `-usb-bot-die-len` / `-usb-bot-die-lba`, which kill the BOT target at a write of a given size at or above a given LBA and leave it dead for the rest of the run. With this switch the target ANSWERS AGAIN after a Bulk-Only Mass Storage Reset, and the die trigger is SPENT rather than merely lifted. It keys on the BOT class reset and not a port reset because that is the one the driver issues: measured off `sink-dies`, the guest sends four Mass Storage Resets after the death and no port reset at all. Without the trigger being spent the driver retries the write that killed it, the length and LBA still match, and it dies again forever. This is what makes `msc-cell-retry` reach `msc-retry-ok` (3): the latched default can only ever reach `msc-retry-failed` (2), so the driver's recovery had a bed for its failure and none for its success |
| `-usb-bot-die-on-nic` | off | The BOT target stops answering FOREVER at the first bulk WRITE issued after the e1000 model sees the first observable of NIC bring-up. Latched like `-usb-bot-die-lba`, and spent by `-usb-bot-revive-on-reset` in the same way. **It injects a symptom, not a mechanism**, exactly as `-usb-setcfg-fault` does: nothing here claims to know why a part would behave this way, and no reading taken under it is evidence that it does. It exists because the other two medium-death levers key on a property of the WRITE (its length, its LBA) and so can only express "the medium dies at a certain kind of write"; the candidate sitting 11 raised is that the write is innocent and its TIMING is the whole of it, which no length and no LBA can separate. Arms on either the CTRL write that sets SLU or the RCTL write that sets EN, whichever comes first, and the stderr line NAMES the register rather than assuming one: measured 2026-08-21 on the diag ladder it is **RCTL.EN**, written by `nicinit`. Its arm is `nic-kills-msc` and its control is `b3-pass` |
| `-usb-no-unit-attention` | off (the condition is ON) | Restore the old always-ready storage target. **By default the device now presents the power-on UNIT ATTENTION every conforming SCSI target presents:** the first command after a controller reset answers CHECK CONDITION with sense key 0x06 / ASC 0x29, and the condition persists until REQUEST SENSE reads it. A host that skips that handshake sees its first real command fail on real hardware and used to pass here; `msc-wait-ready`'s retry loop had never executed. The condition is armed in the RESET path, not at init, because the guest issues HCRST during bring-up and would wipe it -- a sabotage arm that should have failed and did not is what found that. |
| `-usb-disk-port <N>` | 1 | Carry the mass-storage device to root port N; its old port goes dark rather than answering as well. Pair with `-xhci-ports` to reproduce a device sitting where no reader can see it. **No bed could put a connected device above root port 7** before this: the model had four ports, and QEMU refuses attachment above its eighth whatever HCSPARAMS1 claims. The ASUS answered with the boot stick on port 9, past the probe's eight PORTSC rows, so a count of connected ports named none of them. `-xhci-ports 26 -usb-disk-port 10` reproduces the board's `port=9 speed=4`. **Before believing a wide-port PASS, run the arm that must FAIL:** `-xhci-ports 8 -usb-disk-port 10` seats the disk past the declared port count, so the walk's own bound cannot reach it and the run has to report `connect=FAILED` (or `ok=0` on a `usb-attach` probe). Without that third arm a passing wide-port run is indistinguishable from a flag the emulator ignored. Better still, make the probe PRINT the port the walk settled on, so a vacuous run says `port=0` and convicts itself. |
| `-usb-cfgval <N>` | 1 | The mass-storage device numbers its configuration N and **refuses any other value** with a STALL (USB 2.0 9.4.7 makes a bad configuration value a request error, and a control pipe reports one by stalling). `bConfigurationValue` is not an index and is not obliged to be 1. Use it on any driver that sends SET_CONFIGURATION: `msc-open-endpoints` sent a hardcoded 1 while every sibling driver read descriptor byte 5, and no bed could refuse it -- this model reported 1, and QEMU's `usb-storage` reports 1 and then accepts anything. |
| `-usb-setcfg-fault <N>` | 0 | The mass-storage device answers SET_CONFIGURATION with completion code N whatever value is sent: 6 STALL, 4 USB Transaction Error. Reproduces the ASUS 2026-08-03 reading (`connect=FAILED`, rung 2) on the desk so the host's handling of a refusal can be built here. **It injects a symptom, not a cause.** Never use it to confirm a diagnosis -- a check fed a fault you selected will agree with you. |
| `-xhci-calibrate-periodic` | off | Skew the model's EXPECTED Interval and Max ESIT Payload by one, so a correct driver is reported as MISMATCH. The periodic value check runs always and reports once per slot and DCI on stderr; this is the arm that shows it can say no. Use it whenever you are about to believe a MATCH. |
| `-xhci-psi` | off | Declare Protocol Speed ID dwords in the Supported Protocol capability (PSIC = 4) and report NON-DEFAULT Port Speed values in PORTSC: full speed as 5, low as 6, high as 7, super as 8. Table 7-13's familiar 1/2/3/4 are only the defaults, and xHCI 7.2.2.1.1 makes them conditional on PSIC being zero. Use this on any driver that decides a speed CLASS from the PORTSC speed field. The model reports the true class and the slot context's claim side by side on stderr. |
| `-xhci-intel` | off | Present the xHCI as an Intel Lynx Point PCH (8086:8C31) with the USB2 ports still owned by the companion EHCI. XUSB2PRM reads `0xE` (ports 1-3 routable), XUSB2PR resets to 0, and an unrouted port reads PORTSC as all zero -- dark, not merely disconnected, which is what a port left with the companion looks like to the xHCI. The guest must route the ports before it sees any device on them. Writes to XUSB2PR are traced on stderr. |
| `-xhci-intel-lock` | off | As `-xhci-intel`, and additionally make XUSB2PR read-only, modelling a part whose ports firmware pinned to the companion. A correct driver then finds nothing, which is what proves the routing gate rather than something else is deciding. |
| `-xhci-csz` | off | Advertise CSZ=1 (HCCPARAMS1 bit 2) and hold every context to the 64-byte stride, the way Intel PCH silicon does. Every bed before this flag reported CSZ=0, so the driver's 64-byte context path shipped in every image and executed nowhere but on real Intel parts. A driver that hardcodes 32 writes its slot context inside the input control context and its ring pointers where the controller will not look. |
| `-xhci-scratch <N>` | 0 | Declare N scratchpad buffers in HCSPARAMS2 and REFUSE the first ENABLE_SLOT (completion code 9, stderr verdict) unless DCBAA[0] points at a 64-byte-aligned array of N page-aligned, in-RAM page pointers. QEMU and this model declared zero forever; Intel parts demand real pages, and a missing array is silent corruption on metal. |
| `-no-hpet` | off (the HPET answers) | The HPET window at 0xFED00000 is dead: every register reads zero and every write is dropped. What decides is the capability period at offset 04, which a guest divides into a second to get its tick rate, so a zero period is the box saying it has no clock to offer. `Hpet.codex` turns that into a rate of zero, and `E1000e` and `NicAsde` above it fall back to counting reads instead of clocking a deadline -- the path every network timeout on a box without an HPET actually takes, and one that had no bed arm at all until 2026-08-19. Four readers in the diagnostic ladder say so under it (`nicsit`, `nicinit` and `nicring` state `no-hpet`, and the scene stage's frame row reads `plain=no-clock`), which is the `nic-nohpet` arm. The xHCI MFINDEX is unaffected: that runs off host time inside the model rather than off a register the guest was offered. |
| `-hpet-frozen` | off (the HPET answers) | The HPET window is there and UNDECODED: every register reads all-ones and every write is dropped, which is the other dead clock and the one `-no-hpet` cannot express. A period of 0xFFFFFFFF derives a rate of 232830 Hz, bogus but nonzero, so every reader takes its CLOCKED wait path, and the counter at F0/F4 is 0xFFFFFFFF and never moves, so each of those waits is bounded only by its read fuel (`e1000-await-link-clocked` is 100000 batches of 4096 STATUS reads). The diagnostic ladder's `b3` stage reads the counter across 100000 reads at entry and answers `clock-stuck` before bring-up; that is the `b3-clockstuck` arm, whose cfg turns the three nic stages off so nothing ahead of b3 spends its fuel on the same clock first. (root, 2026-08-21, the shape blu asked for.) |
| `-no-smbios` | off (SMBIOS is published) | With `-uefi`: leave the SMBIOS entries out of the ConfigurationTable, so a guest that goes looking is told "none offered". The arm for a reader's no-table state; the diagnostic ladder's smbios stage answers `no-table` and its box row `unnamed` under it. |
| `-no-edid` | off (an EDID is offered) | With `-uefi`: LocateProtocol answers NOT_FOUND for the EDID protocols. The ladder's edid stage answers `absent`. |
| `-edid-bad` | off | With `-uefi`: the offered EDID has a wrong checksum byte. The ladder's edid stage answers `bad-checksum`, which is what shows the checksum is read rather than assumed. |
| `-uefi-conout-remode` | off | With `-uefi`: the first ConOut ClearScreen switches the GOP to 1024x768 stride 1024 and updates Mode->Info, modelling AMI Aptio V's GraphicsConsole activation. A stub that reads the GOP geometry BEFORE its first ConOut use hands its payload the splash mode's numbers for a scanout that has since changed, which is the ASUS display corruption of 2026-08-02 exactly. |
| `-xhci-evt-flood <N>` | 0 | Post N extra Port Status Change events at the Run transition, on top of the per-port connect and reset-completion PSC events the model now always posts. The event ring is 64 TRBs: N at or above it marches the producer through the ring WRAP and into the FULL condition (drop + one stderr report, per real silicon), the two paths a 4-port bed can never reach and a 26-port Intel reaches before enumeration begins. Both paths measured PASS on the shipping driver at N=30/50/100. |
| `-hid-nak` | off | The HID keyboard NAKs every interrupt IN forever: no DMA, no transfer event, the pending TD stays in progress and Stop Endpoint answers FSE code 26 (Stopped) with the residual. Reproduces the ASUS 2026-08-03 flight signature (EPINT=0, dq parked, est=1, f1=1a) on the desk; the arm every silent-keyboard hypothesis is tested against. |
| `-hid-idle-quirk` | off | The HID keyboard NAKs every interrupt IN after the guest sends SET_IDLE duration 0 (the over-honored "report only on change", HID 1.11 7.2.4 against the F.3 every-poll default). A driver that skips SET_IDLE never triggers it; one that sends it goes silent -- the arm that separates the v15 fix from the v14 behavior. |
| `-hid-root-silent` | off | The root-port keyboard completes every interrupt IN with SUCCESS and eight zero bytes (GET_REPORT answers zeros too) while the hub-attached keyboard carries the injected keys. The ASUS TUF 2026-08-04 desktop signature -- SUCCESS on the driver's own endpoint, buffer untouched, key held -- which `-hid-nak` cannot produce (it models no event at all). The arm the bind-every-keyboard fix is proven against (`usb-kbd-silent`). |
| `-hid-keys` | off | Scripted keys (`-keyt`/`-keys-file`) feed ONLY the USB HID held-key set: no PS/2 queue, no host-side key-cell write. Without it injected keys reach the guest through the PS/2 emulation whatever the USB stack does, so no bed run could ever prove a scancode travelled the interrupt-IN DMA path. The honest model of the ASUS, which has no PS/2 controller at all. |
| `-hid-combo` | off | The root HID device carries TWO interfaces: boot keyboard on EP1 IN and boot mouse on EP2 IN -- the wireless-dongle topology. Boot mouse reports are built as deltas from the `-mouse` timeline's absolute samples (clamped to the signed byte the protocol carries), so a scripted pointer drives the USB pipe exactly as scripted keys do under `-hid-keys`. The arm the per-interface classification is proven against (`usb-hid-combo`): a whole-device classifier hands the combo to the keyboard driver and the mouse half is unreachable. |
| `-hid-nak-unchanged` | **on** | HID interrupt IN endpoints NAK until they have news: a TRB completes only when its report would differ from the last one delivered on that endpoint (the combo mouse: only when a sample arrived since the last report); otherwise the TD stays pending and is re-rung when input state changes. **The default since 2026-08-06** -- the flag is still accepted so existing sidecars keep parsing, and passing it is now a no-op. The arm the per-endpoint event latch is proven against (`usb-hid-steal`). `CODEX_VM_HIDNAK_TRACE=1` restores the `HIDNAK:` narration, which used to appear whenever the flag was passed and would otherwise now be on every bed's stderr. |
| `-hid-instant-complete` | off | Restores the pre-2026-08-06 default: every interrupt IN TRB completes at doorbell time with whatever the input state is then. Completions become so plentiful that a defect fed by their SCARCITY -- one endpoint's waiter consuming a sibling's rare completion and starving it, the Unifying-receiver mouse failure of 2026-08-04 -- cannot be expressed, which is the reason to reach for it. The cost is that it drops any keystroke narrower than the guest's poll interval, silently; see the scripted-input section below. |
| `-uefi` | off | UEFI firmware mode (ConOut/ConIn, GOP, Block I/O, memory map, runtime services, auto-extract PE from GPT images) |
| `-gop` | off | Activate GOP framebuffer (default 640x480) |
| `-gop-width <N>` | 640 | GOP framebuffer width (implies `-gop`) |
| `-gop-height <N>` | 480 | GOP framebuffer height (implies `-gop`) |
| `-gop-max-mode <N>` | -- | Cap the GOP mode table's `MaxMode`. The UEFI GOP enumerates modes 0..2 (640x480, 800x600, 1024x768) plus, as mode 3, the `-gop-width x -gop-height` you set when it matches none of them, so a bed at 1600x900 is a firmware whose largest mode is what the display supports; `Mode->Mode` names the current one. `-gop-max-mode 1` is a firmware with nothing to enumerate, the fallback arm for a stub that picks the largest mode (`build/gop-mode-arm.ps1`). `QueryMode` answers into a scratch info block, not the current mode's; a mode past `MaxMode` is `EFI_INVALID_PARAMETER`; `SetMode` commits the framebuffer before it clears it (a runtime mode set from a headless boot faulted the HOST until it did). |
| `-smp [N]` | 1 | Enable multi-core: N virtual processors (1-16, default 4 if N omitted). Creates WHP VPs, LAPIC, MADT with per-core entries. Core count written to GPA 0xFF8; boot code reads it to decide whether to send INIT/SIPI. |
| `-portfwd [udp:]<host:guest>` | -- | Port forwarding from host to guest NIC (repeatable, max 8). TCP by default; `udp:` forwards datagrams instead, giving each host client a synthetic gateway source port so the guest's replies route back. Examples: `-portfwd 8080:80`, `-portfwd udp:15683:5683` |
| `-natmap <guestdest:hostport>` | -- | Remap an OUTBOUND destination port (repeatable, max 16, TCP only). The opposite direction to `-portfwd`: when the guest dials `guestdest`, the NAT connects to the host on `hostport` instead of the port the guest asked for. Exists because a plug's port is compiled into it from `build/plug-ports.ps1`, so N copies of one plug all needed the same host listener and could not run at once. With this, each worker owns a private host port while running the same unmodified plug binary -- which is what lets `codex/plugs/recheck/sweep-all.ps1` go N-wide. Unmapped ports are untouched, so every existing invocation means what it always did. Example: `-natmap 9134:9250` |
| `-debug` | off | Interactive debugger shell on breakpoints and single-step |
| `-break <name>` | -- | Patch INT3 at named function entry (implies `-debug`, repeatable) |
| `-map <file>` | auto | Symbol map file for address resolution. Auto-probed: `<kernel>.map`, then `seed/Codex.map` |
| `-watch <0xADDR>` | -- | Hardware watchpoint via page protection |
| `-watch-size <N>` | 8 | Watchpoint region size (max 64 bytes) |
| `-hbreak <fn>[:<reg>=<val>]` | -- | Conditional execution breakpoint through the debug registers (repeatable, max 4, one DR each, shared with `-wcet`; needs `-map`). The condition is evaluated at the host's `#DB` exit on the guest's own register file. Writes no guest byte, and an unmatched hit RESUMES instead of halting, so this is the way to reach the Nth call of a hot helper. Prints `HBREAK-OBS: <fn> hits=<n> matched=<m>` on exit -- **both counts, always**, because a run reporting only matches cannot be told from one whose breakpoint was never reached. **A conditional breakpoint on INT3 cannot work in this guest** and `-break`'s is not offered: vector 3 belongs to the guest, whose handler runs before the host sees the trap, so the condition would be read off the handler's registers (`rdi` is `0x33` every time) |
| `-hwwatch <0xADDR>` | -- | Data watchpoint through DR0, trapping the exact linear address regardless of guest paging, so it does not collide with the guest's own demand-paging `#PF` handler the way `-watch`'s page-protection exits do. `-hwwatch-rw` widens it to reads, `-hwwatch-len <N>` sets 1, 2, 4 or 8 bytes. **It did not fire host-side until 2026-07-28**: DR0 was armed correctly but nothing enabled `ExtendedVmExits.ExceptionExit`, so the `#DB` was delivered to the guest IDT, which dumped `!EXC=01` and halted on the first hit. Any conclusion drawn from an older `-hwwatch` run saw at most one write |
| `-hwwatch-log` | off | With `-hwwatch`, print one line per write and continue, instead of a full crash report. This is what names a corrupter: it is the SECOND write to a cell that matters, and the crash report is both too heavy and, in practice, terminal |
| `-wcet <name>` | -- | Observe a function's per-invocation dynamic instruction count (repeatable, max 4 -- one DR0-DR3 exec breakpoint each; needs `-map`). Prints `WCET-OBS: <fn> max=<n> calls=<k>` on exit. Observation only: no guest byte is modified. |
| `-mouse <script>` | -- | Scripted pointer: `t:x,y,btn` events separated by `;` (t = ms from boot, btn bit 0 left / 1 right / 2 middle). Injected straight into the guest, so no host cursor moves and no window takes focus. Works headless. **The `x,y` are NOT screen coordinates.** The host tracks a position starting at `0,0`, each event's `x,y` sets that tracked position, and what reaches the guest is the DELTA from the previous event, clamped to +-127 per sample (a HID boot mouse reports one signed byte per axis). Negative values are accepted and are how you move left or up. So a guest pointer that starts centred at 800,450 reaches 420,857 with four samples of about -95,+102 each, ending at `-380,407`, and a single `420,857` event moves it 127 right and 127 down instead of putting it there. Cost several boots to find; see the worked walk in `ModernDesk.md` stage 14. |
| `-mouse-file <file>` | -- | Same, read from a file (one event per line, `#` comments). Use for drags, which run to dozens of samples. |
| `-keys-file <file>` | -- | Timeline keyboard: `t:scancode` per line, on the same clock as `-mouse`. Lets a script interleave typing with clicks (the older `-keys` fires on a fixed start+interval). **The event separators are `;` and newline ONLY.** `inject_keyt_parse` skips leading `;`, newline, `\r`, space and tab, but after each event its trailing skip runs to the next `;` or newline, so **a comma-joined timeline silently injects its FIRST event and discards the rest of the line with no error printed.** Cost four boots to find. Re-derive any probe arm that rode a multi-key timeline before building on it. |
| `-rtc <stamp>` | host clock | Freeze the emulated CMOS RTC at `YYYY-MM-DDTHH:MM:SS` (the `T` may be a space). Day-of-week is computed from the date, not accepted. **This is what makes a guest that paints the time comparable against a recorded frame** -- without it the clock is host state the test cannot twist, which is why GuiOS was believed to be un-goldenable. It also turns the update-in-progress simulation OFF (a frozen clock cannot express UIP), so it is for frames and never for testing the RTC itself: anything asserting on clock behaviour must run without it. **It stops the HPET main counter as well**, pinned to `(minute*60 + second) * HPET_HZ` so the two clocks tell the same time and moving the stamp forward a second moves an HPET-driven animation forward a second. That was added when the desk's 3D orbit moved to the HPET: a guest animating off a hardware counter is exactly as uncomparable as one painting the wall clock, and freezing only the RTC would have left every existing capture recipe reproducing a different frame each run. Frozen-clock frames across that change were required to be byte-identical, and were. The counter therefore does not advance under this flag, so **anything that WAITS on the HPET must run without it** -- today that is `E1000e` and `NicAsde`, and no test sidecar passes `-rtc`. |
| `-screenshot <file>` | -- | Save GOP framebuffer as BMP on exit |
| `-screenshot-delay <ms>` | 0 | Delay before screenshot capture. **Measured 2026-08-15 on a `build-option-a` UEFI probe image: 0 and 2000 capture; 5000 and 9000 give `code=-1` and NO BMP. RE-MEASURED after the GOP rewrite at main 15469 and unchanged**, which is worth saying because that rewrite touched QueryMode, SetMode and the framebuffer commit and would reasonably be assumed to have invalidated this row. The 120000 recommended above is for the DeskVm BMP-write path and was not re-measured here. Verified with a control, because the first reading looked like a broken payload: the KNOWN-GOOD image fails the same way at the same delay. If a capture goes missing, bisect the delay before suspecting the guest. |
| `-args <string>` | -- | Boot arguments string (accessible to guest) |
| `-trace-file <file>` | -- | Write execution trace to file |

Environment: `CODEX_VM_NO_TIMER=1` disables PIT timer interrupts.

`CODEX_VM_DROP_SERIAL_AT=N` with `CODEX_VM_DROP_SERIAL_LEN=K` (default 1024)
discards K bytes of capture once N bytes have been taken, on whichever output
path is carrying them, counted and reported exactly as a real loss is. It is a
test instrument and it exists because the real drop paths fire only when the
HOST is out of memory: the failure they cause -- the battery's batch parser
filing whole binaries under the wrong test's name, every one at exit 0 -- could
not be produced on demand, so it could not be proven fixed. An environment
variable rather than a flag because the VM under test is launched by a harness
several levels down, which no flag of ours reaches. `ExaminersAssay.md`, "The
batch stream can lose bytes", has the worked reproduction. Never set in a run
whose output anyone intends to trust.

#### Batch mode: `-run-list`

```
codex-vm -run-list list.txt [-run-list-wall MS]
```

One line per run, each line carrying exactly the flags that run would take on
its own command line. Tokens are whitespace-separated and may be double-quoted,
so a path with a space survives; `#` comments and blank lines are ignored.
`-kernel` beside `-run-list` is REFUSED rather than ignored, so the two modes
never look combinable.

**It spawns a fresh `codex-vm` child per line. It does not reuse the process.**
That is the whole design and it is worth knowing before anyone tries to
"optimise" it: a batch is then byte-identical to N single runs by construction,
because each line gets a genuinely new process, not a partition delete and a
hopeful reset. Measured 2026-08-22, one kernel on a twelve-core box:

| path | per kernel |
|---|---|
| `pwsh` child running `build/test-run.ps1` | 574.8 ms |
| `codex-vm.exe` directly, fresh process each | 73.7 ms |
| bare `codex-vm.exe` process start alone | 12.6 ms |

The `pwsh` child is 501 of the 575, so what a batch mode has to remove is the
SCRIPT per test and not the exe. Reusing one process would buy the 12.6 ms and
require resetting 376 file-scope statics between runs: deleting a partition
does not reset them, because the device models are host state, and one missed
static makes a test's result depend on what preceded it in the batch.

Per line it writes `RUN-LIST BEGIN [i/N] <kernel>`, then that child's stderr
verbatim, then:

```
RUN-LIST END [i/N] <kernel> exit=<code|TIMEOUT|SPAWNFAIL> output=<bytes> dropped=<n> ms=<elapsed>
```

`output` is the child's own `Output: N bytes` line, or `-1` when it wrote none.
`dropped` is read from that child's `SERIAL: N guest serial byte(s) DROPPED`
lines, so L-SHORT's drop signal survives batching and is attributed to the
kernel that dropped it. It is keyed on that whole phrase and not on the word
DROPPED, which the GPU triangle-cap warning also prints.

**Do not read `exit` as a verdict.** A healthy test exits
`(debug_exit_code << 1) | 1`, so 1 is the normal answer and `build/test-run.ps1`
has always ignored it. The supervisor's OWN exit code is 0 when it ran the
list and 1 when a line timed out or could not be spawned.

The per-kernel wall budget is the same 60 s `test-run.ps1` keeps. A timeout
signals the child's own `Global\CodexVmShutdown_<pid>` event first and only
falls back to `TerminateProcess`, because killing a child mid-hypervisor-call
is what corrupts vid.sys. `-run-list-wall MS` overrides the budget: it exists
so the timeout path can be exercised at all, and `build/check-run-list.ps1`
uses it at 2000 ms.

Sidecar preparation stays with the caller. `test-run.ps1` copies a depot
`-disk` image to a writable temp because a synced sidecar is read-only; a list
line carries the FINAL paths, exactly as they would be passed to a single run.

`build/check-run-list.ps1` is the runner: byte-identity against single runs
(with an ablation proving that comparison can fail), a corrupt kernel mid-list
not taking its neighbours, the wall budget stopping one line alone, and the
drop count landing on its own line with the GPU line as a negative control.

**There is no `-timeout` flag, and an unknown flag is ignored in silence.**
Measured 2026-08-11: `-timeout` appears zero times in `tools/codex-vm.c`, so
`-timeout 20` parses as two arguments the loop above matches nothing against
and drops. The run is UNBOUNDED. Every recipe in this file that carries one --
including the disk-image reproduction below -- ended when its guest crashed,
which is why nobody noticed; point one at a guest that does not crash and it
runs until the host kills it. The wall budget lives in the harness instead:
`build/test-run.ps1` enforces `$wallBudgetMs`. For a bounded run by hand, use
`-screenshot <file> -screenshot-delay <ms>`, which exits after the capture and
gives a visual record as well. **This cost a browser soak an hour of wall clock
on 2026-08-11**, and the trap is the shape of it: the flag looks accepted, the
command works, and it is only wrong on the runs you most want bounded.

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

The vector field is the only thing an AP is told, here as on a physical
machine. `docs/ArchitectsSketchbook.md` has the three guest-side
requirements that follow from it.

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
processor alone, so each AP arms its own **LAPIC
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

**Proc 0 does not migrate.** Three guards forbid it: `__idle_dispatch` starts
each core's scan at its own id so an AP never reaches slot 0, and both
preemption scans skip slot 0 when the claiming core is not the boot
processor. Its affinity field says `0` to match.
`codex/test/smp-proc0-pinned.codex`
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
| 0x40B | OUT | Commit texture upload, and the value IS the wire format (0 or 1) |
| 0x40C | OUT | Asset load: guest address of a null-terminated host path |
| 0x40D | OUT | Asset load: guest destination address |
| 0x40E | OUT | Fade-clear framebuffer toward an XRGB color |
| 0x417 | OUT | Asset load: execute (reads the host file into guest RAM) |
| 0x40E | IN | Asset load: low 32 bits of bytes loaded |
| 0x40F | IN | Asset load: high 32 bits of bytes loaded |

The asset-load execute is 0x417 and **not** 0x40E, which the OUT chain matches
first as the fade-clear: an asset load fired at 0x40E silently faded the sky
and reported zero bytes read. 0x40E OUT (fade) and 0x40E IN (size) do not
collide, so the size read-back keeps its port.

Includes depth buffering, per-vertex normals, diffuse+specular
lighting, texture mapping with bilinear filtering, procedural Earth
texture generation, and atmospheric glow post-processing.

**The commit value at 0x40B declares the texture, not just the doorbell**, and
the two modes are not interchangeable. **0** is the original wire: three packed
RGB bytes per pixel, which is what `apps/globe/TerrainGen.codex` writes with
`poke-byte`, sampled bilinear with both axes inverted and shaded by the
earth-globe shader. **1** is one 32-bit `0x00RRGGBB` word per pixel, which is
what a Codex `EngineTexture` holds and what `gpu-mem-write` writes, sampled
nearest with both axes wrapping and shaded by a plain modulate of the
interpolated vertex colour, matching `Renderer3D`'s `r3d-tex-px`.

The globe shading is not a filter that can be left on. It reads the UVs as
SPHERICAL COORDINATES, fabricates a per-pixel sphere normal from them, lights
that, and adds a Fresnel atmosphere rim worth up to +140 blue. On a ground plane
it turns a correctly sampled tan texel (`6E5F4B`) into `405A9C`, a blue that
looks like some other object's albedo rather than like a shading bug, which cost
four wrong hypotheses before a probe was pointed at it.

**A viewport-confined frame is double buffered; a full-screen one is not.** A
frame arrives as a clear on one port and a draw on another, and the rasterizer
writes into the GOP framebuffer the display thread is reading, so between those
two writes the target holds nothing but the clear colour. Measured on the desk
pane at the instant between them: **1,123,200 of 1,123,200 pixels**, every
frame. That window is what a headless capture occasionally returns as a pane of
pure sky with the label still drawn beside it, and what reads on the glass as
the pane flickering while it animates.

So when a viewport is armed the frame is built in a host back buffer and
presented in one copy at the end of the rasterize; the same reading afterwards
is 333,663, which is the previous complete frame's real sky. **A program that
owns the whole screen has no viewport armed and still draws directly**, so it
keeps this window -- the globe demo will hand back a bare-background frame now
and then for exactly this reason, and that is the capture, not the renderer.

**`GPU_SHADOW_SLOPE` is 16 and the number was swept, twice.** The first sweep
used a CUBE FACE and settled on 6; flat geometry cannot show what a curved
surface does, and a sphere in the desk scene still carried visible acne at that
setting. Re-swept measuring acne as the sphere's difference from the same frame
with shadows off, against the ground shadow measured the same way: 6 gives 350
acne pixels and 132,287 of shadow, 16 gives 0 and 131,691, 24 gives 0 and
131,200, 40 gives 0 and 130,137. 16 is the knee rather than the largest value
that works. The software renderer reads 0 acne at every setting, so this was the
host path alone off parity.

**Single-pixel rasterizer probe.** Set `CODEX_GPU_PROBE=x,y` and codex-vm prints
one line per triangle that covers that screen pixel, whether it wins or loses the
depth test:

```
GPUPROBE f=8 tri=0 tex=1 uv=(0,0 1000,1000 1000,0) c=(C2C0B8 C2C0B8 C2C0B8)
         interp=(716,303) texel=6E5F4B mode=1 d=982321 pixel=534736
```

It answers "which triangle drew here, from what UVs, sampling what, and what did
it emit" in one run, and it costs nothing when the variable is unset. It is the
right first instrument for any "wrong colour in the pane" question: a census of
the frame tells you a colour is wrong, and only this tells you which triangle and
which stage produced it.

The same switch prints one line per rasterize carrying the triangle count, the
screen and depth extents of the whole submitted batch, and how much of the
VISIBLE framebuffer is currently the clear colour. The extents separate "the
guest culled everything" from "the guest submitted geometry that landed
somewhere unexpected" without a rebuild, and the last figure is what turns an
intermittent flicker into a deterministic reading.

**Serial I/O.** Ring buffer at GPA 0x500000 (1 MB). Source input is
pre-loaded from `-input` file. Output captured from guest UART writes
to `-output` file. Ports 0x3F8-0x3FD (COM1). Protocol: guest reads
input from ring buffer; writes output bytes; harness captures until
EOT or VM exit. The capture buffer starts at 16 MB and doubles; if a
doubling fails, every byte that does not fit is COUNTED, the first prints
`SERIAL: output buffer growth failed at N bytes ...` on stderr and the exit
dump adds `SERIAL: K guest serial byte(s) DROPPED ... <file> is SHORT`
(2026-08-18; before that the drop was silent and `-output` was simply
short with both ends reporting success). Exit status is unchanged by a
drop, so a harness that wants to refuse a short capture greps stderr for
`SERIAL:`. The BLIT path (bulk output) reports its own growth failure as
`BLIT: output buffer growth failed`.

**A `-input` image needs a TRAILING ZERO BYTE after the source**, so the
image is header, source, then one `0x00`. Without it the run produces exactly
one byte of output, `0x01` -- the leading marker with nothing behind it -- and
that reads as the compiler dying rather than as an empty read, which is a
diagnosis two steps away from the cause. `codex/plugs/wasm/build-page.ps1`
builds `header + src + 1` and is the worked example. The expensive half is
that **wasmtime does not care**, because fd_read's zero return is its own
terminator: the same malformed image runs clean on the wasm target and fails
on x86, so the two disagree in the direction that makes wasm look healthy and
the emulator look broken. (Distinct from the trailing `0x04` in the
two-compiler comparison recipe further down, which is a mode-line protocol for
that procedure and does not cover this.)

**The compile payload has three ways out, and the guest picks by probing
(root, 2026-08-21).** `__write_binary` and `__write_binary_buf` first read port
0x511: `0xB7` means codex-vm and the whole buffer leaves by the blit doorbell in
one exit. Otherwise they read port 0xE9: `0xE9` read back means QEMU's Bochs
debug console (`isa-debugcon`) is wired, and the whole buffer leaves as ONE
`rep outsb` to that port, no LSR poll and no 16-byte FIFO bound, because the
console is a byte sink with no status register. Anywhere else the port floats
`0xFF` (QEMU without the device, codex-vm itself, real hardware) and the COM1
loop runs as before: poll LSR once, burst up to 16 bytes. Only the QEMU compile
fallback in `vm-config.ps1` (`Invoke-VmCompileFallback`, generated from
`codex/build/vmconfigScript.codex`) wires the device, to a file chardev, and
it accepts the payload from EITHER channel: complete means the console holds
`SIZE:`'s byte count, or holds nothing and the serial wire does. A kernel older
than the probe (the gate's stage0 is the depot seed) still answers on serial and
is read unchanged; a console holding some bytes but not all is still streaming
whatever the serial count says, since a `MAP` trailer can outweigh a small
payload. The payload is spliced back where it sat on the wire, so `compile.ps1`
reads `OutputFile` the same way from either host, and the fallback prints
`qemu fallback: N payload bytes via the debug console` on stderr when that path
was taken. No other `Start-VmRun` consumer gets the device: each reads the
serial wire alone, and a device they did not ask for would move their payload
somewhere they do not look.

Measured 2026-08-21 on the landing, one unit (`codex/test/text-helper-spec`,
105 KB of output): the same kernel produces a byte-identical binary through
codex-vm's blit and through QEMU's debug console (one SHA-256 for both), which
is the claim that matters; the gate's `vm-differential` phase makes the same
comparison on every seed land. Wall clock under the QEMU fallback at that size
was 4.0/3.9 s with the pre-change kernel on the COM1 bursts and 3.6/4.0 s with
the new one on the console: inside noise, because the 16-byte bursts had
already taken the exits out of the serial path (blu, main 17213) and a compile
that size is boot and compile time, not output time. The path's own signature
is two `in al,dx; cmp rax,0xE9` sites in the seed, one per helper: 0 in the
pre-change seed, 0 in the pre-convergence Sut, 2 in the converged seed, the
4.3a shape exactly. A larger payload was attempted (`GopBoot`, 1.2 MB of
output) and the PRE-CHANGE kernel's run under the fallback did not complete:
36 minutes, 19 s of CPU, no log line, and the fallback's own deadline never
fired, so the QEMU path on a unit that size has a hang of its own that this
change neither causes nor measures. Not pursued; named here so the next
person who wants the big-payload number starts there rather than at the
console.

**NE2K NIC.** NE2000-compatible ISA NIC at I/O base 0x300. User-mode
NAT stack with IP 10.0.2.15, gateway 10.0.2.2, DNS 10.0.2.3. Handles
ARP (responds for gateway), DHCP (offers 10.0.2.15/24), DNS, TCP
forwarding, and UDP forwarding (see below). Port forwarding via
`-portfwd` for host-to-guest TCP connections.

**DHCP is answered.** The server answers DISCOVER with an OFFER and REQUEST
with an ACK carrying mask, router, DNS and a 3600-second lease.
`codex/test/dhcp-acquire` is the guest that asks, and it is what keeps this
row honest: `Dhcp.codex` builds the messages and parses the replies but
performs no I/O of its own, so without a caller neither the model nor this
document has anything testing it.

**`-dhcp-lease <seconds>` sets the lease the NAT offers; the default is
3600.** It exists because the default cannot be tested: a client renews at
half its lease, so nothing observes a renewal inside a run that lasts
seconds. `codex/test/dhcp-renew` asks for four, which puts the renewal two
seconds in, and it is the only reason that test can exist.

**An empty receive poll costs 540x more on the NE2000 than on the e1000, and
a guest that counts polls is counting two different things.** Measured
2026-08-14, one million empty polls through `net-driver-recv-frame`: **15.52 s
on the NE2000, 0.029 s on the e1000.** An NE2000 poll is a port IN and
therefore a VM exit; an e1000 poll reads a descriptor the model already wrote
into guest RAM, which is also what the real I219-V does. Anything in a guest
that uses a poll count as a stand-in for elapsed time is therefore calibrated
against whichever card it was written on, and `NetIO`'s tick was calibrated
against the NE2000 -- see `net-driver-poll-cell` in `ArchitectsSketchbook.md`.
This is a property of the two device models and not of any one guest, so
expect it to bite anything else that counts polls.

**The test for whether a spin count in this tree is secretly a duration, and
the audit that came out of it (2026-08-14).** The question is not how big the
number is, it is **what the loop body touches**, because that decides whether
the bed and the metal agree:

| the loop polls | cost per iteration | bed vs metal | verdict |
|---|---|---|---|
| a register, MMIO or port I/O | a VM exit in the bed, a bus read on metal | comparable, within a small factor | a count is a poor duration but it will not surprise you |
| **memory the device DMAd** (a descriptor status byte, a completion-queue phase bit, a flag another core sets) | a RAM read, no exit at all | **hundreds of times apart** | **the count is a hidden duration and it is probably wrong** |
| nothing external (a recursion or parse guard) | irrelevant | irrelevant | not a duration, leave it alone |

Measured on this box for the third row: **one million iterations of a bare
`peek-byte` status poll cost 605 microseconds.** That is the number to price
any RAM-polling budget against.

Swept 2026-08-14 over `codex/**` and `apps/**`: 36 named budgets of 1000 or
more. Almost all are honest give-up caps where the value never had to mean a
duration. The RAM-polling ones, which are the class above, are:

- **`e1000-tx-fuel` (`E1000e.codex`) WAS one and is FIXED.** At 1,000,000 it
  was a 605 microsecond budget against a 1500-byte frame that takes 1200
  microseconds on the wire at 10 Mb/s, and `e1000-send-frame` returns the
  result directly, so every send on a slow link would have reported failure
  while the frame went out anyway. Now bounded by HPET at 20 ms with the spin
  as the no-clock fallback; `codex/test/e1000-tx-deadline` pins it with the
  old path as its own control, in a band that cannot overlap.
- **`arm64-net-io-tick-interval` (`Arm64NetIO.codex`) is one and is NOT fixed**,
  because this lane has no counter read. What is missing is written out in
  that chapter's own Poll Clock section, including the derived
  `sysreg-cntvct-el0` value and the warning that half of it is seed-affecting.
- **`nvme-fuel` (`GopNvme.codex`)** polls a completion-queue phase bit, which
  is DMAd memory. Not measured, not mine, named here because it is the same
  shape.
- **`xhci-fuel` (`GopXhci.codex`)** is already open as WORKS-9, which states
  the problem exactly: it is a spin count nobody has converted to a duration
  on that box, and no bed can, because codex-vm completes every transfer
  before the guest spins once.
- **`smp-wait-fuel` (`X86_64Boot.codex`)** polls a RAM flag another core sets.
  Boot-time only and it has never failed, but it is the same shape.

`ahci-fuel`, `ide-poll-fuel`, `rtc-uip-fuel` and the other `e1000-*-fuel`
constants poll registers and sit in the first row, so they are not urgent.

**The guest-to-host byte census, and how to read it.** Any run in which the
guest sent TCP payload prints one line to stderr at exit:

```
NAT TX BYTES: seg=N queued=N sent=N drop-noconn=0 drop-badstate=0 drop-oom=0 drop-freed=0 (reap=0 exit=0)
```

`seg` is what the guest's data segments carried and the four `drop-*`
counters plus `sent` must add up to it. It exists because a short arrival
was invisible from both ends: the guest's own accounting says complete, and
`net-send-raw` answers the frame length unconditionally after kicking TXP
without ever reading the NE2000's transmit status, so **the guest cannot
report a transmit failure even in principle.** A nonzero `drop-` counter
names the site; `drop-freed` splits into `reap` (the half-closed reaper
collected a connection mid-drain) and `exit` (VM exit stopped waiting for a
reader, bounded by `NAT_EXIT_DRAIN_MS`).

**A slow reader does not provoke the loss this census was built for; a
STALLED one does.** The guest sends at roughly 120 KB/s, so a uniformly
throttled host reader is never the bottleneck and the socket never fills.
What fills it is a reader that stops near the end of a bulk transfer, so
the send buffer is full at the moment the guest's FIN arrives. Measured
2026-08-18: a one-shot 15 s stall at 16.0 MB of a 16 MB send lost half a
megabyte every time, where the unaided failure rate was about one run in
five.

**The IDE census, and how to read it.** Any run that touched the disk prints
one line to stderr at exit:

```
IDE CENSUS: pio-exits=N str-exits=N words=N reg-exits=N out-batch(hits=N words=N) in-batch(hits=N words=N) flush-entries=N flush-calls=N flush-bytes=N flush-ms=N refused(nopath=0 nodata=0 oob=0 openfail=0)
```

`pio-exits` counts exits on the data port at 0x1F0 and `words` counts the
words that crossed it, so their RATIO is the question the line exists to
answer: 1.0 is one exit per word and 1/256 is one exit per sector.
`reg-exits` counts the task-file registers at 0x1F1-0x1F7 and 0x3F6, which
is the driver's polling around the transfer rather than the transfer. The
`out-batch` and `in-batch` pairs are the string arms in `handle_io`
consuming a whole `REP OUTSW`/`REP INSW` in one exit.

**Read the `refused` counters before quoting a time from any disk run.** A
`Copy-Item` of a Perforce file leaves the copy READ-ONLY, every write-back
is then refused, the guest still reports the save complete, and the run
comes back about ten times faster. That silent loss is latched, counted and
reported here; clear the read-only bit rather than trusting the number.

Measured 2026-08-20 on `docs/Probes/fat-write-phases.codex` writing 2,896,050
bytes (5,657 sectors), a 16 MB image, 3 GB headless: 6,018 exits for 1,540,608
words, `out-batch` 5,865 hits over 1,501,440 words, whole probe 3.3 s wall. The
same probe against the compiler of the previous day took 1,540,098 exits for
the identical 1,540,608 words and 8.2 s, because the guest driver issued a
plain `OUT` per word and the host batch went unexercised.

**A retransmitted SYN is handled, and before 2026-08-14 it was not.** A SYN
for a connection the NAT already had fell through the new-connection branch,
opened a second host socket over the first and leaked it, and the guest's
payload then went to a socket no listener had accepted -- the guest saw its
data vanish and the peer saw a reset. It now answers a duplicate SYN with
silence while the host connect is still in flight (`nat_poll_connect` sends
the SYN+ACK when the socket is writable) and by resending the SYN+ACK once the
connection is up. Nothing in the tree had ever made a guest retransmit a SYN,
which is why it survived: it took running TCP over the e1000, where the poll
cost above puts the guest's first retransmit at 8.6 ms.

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

**A SECOND RECEIVER BRING-UP IN ONE BOOT USED TO STOP DELIVERY FOR GOOD, and
what that costs is a class of old bed reading rather than anything still
broken.** Fixed 2026-08-20 (blu, main 18011). `e1000_rx_cursor` is the head
both delivery paths use, and nothing reset it: there was no
`case E1000_REG_RDH` in `e1000_write` at all, so a write to RDH fell through
to the default store and the cursor only ever advanced. `e1000-setup-rx`
programs RDH=0 and RDT=15 on a 16-descriptor ring, and both paths declare
"ring full, leave it queued" when the cursor equals RDT, so a guest bringing
the receiver up a second time had **one chance in sixteen per bring-up** of
resuming with the cursor already on 15 -- and from there nothing was ever
delivered again. RNBC counted up, the host queue grew without bound, and the
guest polled a ring the model refused to fill. Measured on the diagnostic
ladder, which brings the receiver up four times in one boot: 138 ARP replies
sat in `rx_queue` while the stage reported `no-arp`. RDH is device state the
driver initialises, so writing it now moves the cursor, and reprogramming
RDBAL/RDBAH/RDLEN resets it.

**Which readings this voids, stated narrowly on purpose.** It was a BED
defect, so **metal readings are untouched** -- the sittings stand because
they are metal. It is also not "all bed evidence": the fault needs TWO
receiver bring-ups in ONE boot *and* the cursor landing on RDT, so a test
that boots, brings the receiver up once and exits could never reach it. All
22 e1000 and net tests pass identically before and after the fix, and no
published arm value changes. What IS void is one inference: **"the part did
not deliver", drawn from an empty receive row taken after a second bring-up
in the same boot.** In practice that is the diagnostic ladder's `nicring` and
`b3` rows and nothing else. A bed reading of arrived-but-invisible from that
shape corroborated nothing, which matters because arrived-but-invisible is
exactly what NIC-4 was chasing on metal, and a bed that can produce it for a
reason the metal does not have is worse than one that cannot.

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
| `-e1000-mdio-window` | MDIC answers nothing for 10 ms after CTRL.RST (I219 datasheet 9.2) |
| `-e1000-mdio-slow` | MDIO reads answer E until page 769 register 16 bit 10 is set (I219 9.2) |
| `-e1000-ctrl-ro` | CTRL is READ-ONLY: writes discarded whole, register keeps the firmware value |
| `-i219-ulp-armed` | PHY page 779 register 16 powers up with STICKY_ULP (bit 4) and EN_ULP_LANPHYPC (bit 10) SET, on top of the board's measured `0x0800` |

**779.16 is modelled and 779.17 is deliberately not.** ULP Configuration 1
(I219 datasheet 9.5.7.1) answers reads and writes under `-i219`; ULP
Configuration 2 at register 17 on the same page still answers `E`, which is
the control that shows the page dispatch is not blanket-answering everything
on page 779.

**The power-up value is the BOARD's, not the datasheet's.** Every field in
that register documents a default of `0b`, and Damian's I219 reads `0x0800`
(bit 11, inside the reserved field) on two independent flights, sittings 8
and 9. The model powers up at the measured value.

`-i219-ulp-armed` exists because both ULP-entry bits are CLEAR on that board,
so a driver's entry-disable write has nothing to change and no arm can see it
happen. Armed, 779.16 powers up at `0x0C10` and the write has something to
clear. Without the flag a reaching arm and a skipping arm are the same run.

### The xdiag cell registry

`xdiag-put`/`xdiag-get` (`GopXhci`) index one flat array of 32-bit cells at
`xhci-diag`. Every subsystem that wants a breadcrumb takes cells out of it,
**nothing arbitrates, and no compiler or gate ever looked**. Two owners of one
cell is green everywhere and surfaces as a wrong number on a photograph of a
boot screen.

| cells | owner |
|---|---|
| 0..19 | xHCI scalars (GopXhci's own map, literal offsets) |
| 20..27 | xHCI port cells, `20 + port`, `xhci-port-cells` = 8 |
| 28..46 | xHCI ownership, release and BAR scalars |
| 47 | `usb-hid-cell-count`, the HID bind counter |
| 48..63 | xHCI controller table, `48 + i*4`, four controllers |
| 64..69 | `xhci-ep-base` block, six words |
| 70..91 | MSC and FAT cells (`msc-cell-*`, `gfat-cell-*`; 88 is `gfat-cell-written`, the sectors the data run actually wrote; 89 is `gfat-cell-lastsize`, the size read back from the directory entry after the last bank write; 90 is `gfat-cell-banklost`, the stage index plus one of the first bank append that did not land, zero while every one has; 91 is `msc-cell-chunk`, the MSC transfer chunk in sectors for this boot, zero for the default of 64, set by `sink chunk=N` in DIAG.CFG) |
| 92..93 | diag `b3` bank-loss cells (`db3-cell-notes`, the stage's notes counted as the serial trail counts them, and `db3-cell-lost`, the ordinal of the first note whose write was refused with a bank open, zero while none was; both zeroed at `db3-run` entry because metal RAM is not, and the second is what `bank-lost-note=N` on `b3`'s first row reports) |
| 96..111 | `usb-hid-note` block, `usb-hid-cell-base + idx*4`, four devices |

`msc-cell-end` = 80 is an EXCLUSIVE bound for `xdiag-zero`, not a stored cell,
which is why it may equal another owner's first cell.

**The HID block was at 80 until 2026-08-13, four cells deep into storage
territory**, overlapping `gfat-cell-stage` 80, `msc-cell-lastcc` 81,
`msc-cell-phase` 82, `gfat-cell-write` 83, `xhci-cell-fuel-left` 84,
`msc-cell-fuel-lo` 85, `msc-cell-fail-lba` 86 and `msc-cell-retry` 87. It
stayed invisible because the writers run in order and neither reads the other:
`usb-attach` binds HID and fills 80..95, then the mount and any disk I/O
overwrite 80..87. So the storage diagnostics were correct and **DeskBoot's
first two HID table rows were showing storage state under device-descriptor
labels**. In the other direction a cell the storage path did not write on a
given boot kept its HID value, which is how an F12 mount failure came to
report `gfat-cell-write` as 1964712320 -- not a write stage, but HID device
0's report buffer address.

`build/check-xdiag-cells.ps1` is the runner. It fails on an overlap between
declared blocks, on two named constants claiming one cell, and on a named
constant outside every declared block. **A subsystem taking a RANGE must
declare it in that script's table**, because a computed base is invisible to a
constant scan. It deliberately does not parse the ~90 literal `xdiag-put 19`
calls in GopXhci; those are that chapter's private map.

| `-e1000-asde` | STATUS answers SPEED and ASDV, and CTRL.ASDE picks which source SPEED comes from (82583V 12349, 12590) |

`-e1000-phy-link` is the one worth knowing about. It is **off by default**,
so every run that predates it keeps the SLU-only link it was measured
against; with it on, a driver that never brings the PHY up gets no link,
which is the I219's real behaviour and a failure the default bed cannot
show. `codex/test/e1000-phy` runs under it and
`codex/test/e1000-phy-absent` runs under `-e1000-no-phy`.

`-e1000-mdio-window` is also off by default and is the arm for the settle
the I219 datasheet requires between a reset and the first MDIO access
(section 9.2). A closed window answers with neither R nor E, so it is
indistinguishable from `-e1000-no-phy` to the driver, on purpose.
`codex/test/e1000-mdio-window` runs under it together with
`-e1000-phy-link`, which is the pair that reproduces the metal symptom on
the desk: a link that never comes up while every MAC register reads
exactly as it should.

`-e1000-asde` is the fourth and also off by default, and it exists because
the fields it fills were dead. STATUS carried no SPEED and no ASDV, so
`na-line` printed both off a register nothing ever wrote and every arm ever
run reported 10 Mb/s. With the flag on, SPEED follows the PHY's negotiated
speed when CTRL.ASDE is clear and the MAC's own detection when it is set,
which is the divergence 82583V 12349 describes. The datasheet says the bit
"must be set to 0b" and does not say what happens when software disobeys, so
the model invents no failure for it: the MAC's detection resolves to 10 Mb/s
because this bed's MAC has nothing to sense. `codex/test/e1000-asde-speed`
runs under it with `-e1000-phy-link`. **It does not reproduce the metal wedge
and must not be read as evidence about it.**

`-e1000-ctrl-ro` is the newest and it is **measured on metal, not invented**.
Damian's Intel I219-V at `00:1f.6`, 2026-08-13: the driver cleared `CTRL.SLU`
and read `CTRL` straight back, four rows across two flights, and got
`0x180240` -- the firmware value, SLU still set -- every time. `CTRL.ASDE`
refused to set on the same flights. **MDIC writes work on that part**, so
this is `CTRL` specifically and not the CSR write path: `e1000-phy-write`
writes MDIC at `0x0020`, polls it ready, and succeeds on every arm.

Two consequences, and the second is the one that cost a day. The link comes
up **anyway**, over MDIO, because `na-phy-kick` reaches the PHY and the CTRL
write was never load-bearing. And with CTRL discarded, `e1000-reset` never
sets RST, so `e1000-await-reset` sees it clear on its first read and answers
`settled=1` -- **a reset that never happened, reporting exactly like one that
completed.** On 2026-08-13 that read as "the reset works on a warm part", and
a whole cold-versus-warm hypothesis got built on an event that never
occurred. `codex/test/e1000-ctrl-ro` pins all of it, including a row that
deliberately reads the same with the arm and without it, because no aggregate
can tell those two resets apart.

Nothing public in this tree states that CTRL is read-only to the host, so the
flag models the OBSERVATION and claims no mechanism. What the datasheet does
say is that the I219 is the PHY and the MAC lives in the PCH, whose link
configuration belongs to the integrated LAN controller and the ME
(I219 datasheet Table 5-1, and 9.2's note that the LAN controller configures
the LCD registers).

`-e1000-mdio-slow` is the third of these and also off by default. The PHY
model has pages now: registers 0-15 are the IEEE set and answer in every
page, registers 16-31 at page 0 keep the flat behaviour they always had,
and page 769 register 16 is modelled with the reset value its field table
gives. Any other page answers E rather than pretending to hold something.
With the flag on, ordinary MDIO reads answer E until slow mode is set, and
**the page register and 769.16 itself stay reachable so the bootstrap can
happen at all** -- that exemption is ours rather than the datasheet's,
because 9.2 read strictly forbids the very writes that would satisfy it.
A PHY reset clears the paged state, so a driver that sets slow mode before
resetting the PHY loses it; `codex/test/e1000-mdio-slow` catches that
ordering specifically.

**DNS is answered.** A query to port 53 is handled by `nat_handle_dns`: it
walks the QNAME, resolves it with `getaddrinfo` -- the **host's own resolver**, so
the hosts file, the search domain and whatever DNS the host actually
uses all apply, and no packet leaves the process -- and dresses the
answer as a DNS response the guest's resolver parses. Only QTYPE=A/IN
is answered; anything else returns NXDOMAIN rather than a lie.

**General UDP forwarding is implemented.** A
guest-originated datagram opens a **UDP flow** -- a host socket
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
  alternating 16 and 235 by bar. An isochronous TRB that sets IOC gets a
  transfer event (Short Packet when the frame is smaller than the buffer).
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

**Under `-hid-instant-complete` a keystroke narrower than the guest's poll
interval does not exist.** That model completes every interrupt IN TRB at
doorbell time, so the guest's armed TD is consumed with whatever the
held-key set held then and nothing stays armed for a later report to land
in. Measured on `usb-kbd-multi`, hold width the only variable, three runs
per cell:

| hold | `-hid-instant-complete` | default (NAK) |
|---|---|---|
| 1 ms | `got=0` 3/3 | `got=30` 3/3 |
| 2 ms | `got=0` 3/3 | `got=30` 3/3 |
| 10 ms | `got=30` 3/3 | `got=30` 3/3 |
| 600 ms (shipped) | `got=30` 3/3 | `got=30` 3/3 |

It fails silently: the VM logs the key event and the report reaching the
endpoint either way, so a bed reads as working right up to the width where
it stops being. That silence is why the default moved rather than the
guidance.

Re-ringing the endpoint on input change does NOT rescue instant-complete,
and was measured not to: with a trace on the re-ring it fires (twice, make
and break) and produces no report, because that model already drained the
ring at the guest's last doorbell. There is nothing to deliver into. Only
leaving the TD pending works, which is what the NAK model is.

Separator trap, same family: `-keys-file` and `-mouse` accept `;` and
newline ONLY. A comma-joined timeline silently injects its FIRST event and
prints no error, because the parser skips to the next `;` or newline and
discards the rest of the line.

**`HID: first key event sc=NN` prints NN in HEX, and nothing on the line
says so.** Read it as decimal and an injected 80 reads back as `sc=50`,
which looks like the wrong key arrived when it is the right one. On
2026-08-11 that cost a false backlog entry: four injected arrow keys
changed nothing on screen, the trace was read as decimal, and the
keyboard path was filed as the suspect. The keys were being delivered
perfectly and the page simply had nothing to scroll. When a scripted key
seems not to arrive, convert the trace before blaming the route, and
prefer a chord with an unmistakable effect -- `-keys 29,20,157` is
Ctrl+T, and a browser tab either appears or does not.

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

**SMBIOS.** The 2.1 entry point (`_SM_`) at 0xE0780 and the structure table
at 0xE0800: Type 0 (BIOS "Codex 1.0 05/23/2026"), Type 1 (System "Codex
Project" / "Codex VM" / "1.0"), Type 2 (baseboard "Codex Virtual Board"),
Type 17 (one memory device sized from `-mem`, added 2026-08-18) and Type 127.
Under `-uefi` a 3.0 entry point (`_SM3_`) at 0xF0B00 names the same table and
the ConfigurationTable publishes both under their GUIDs (entries 2 and 3;
`-no-smbios` leaves them out). Also under `-uefi`: EFI_EDID_ACTIVE and
EFI_EDID_DISCOVERED answer from LocateProtocol with a 128-byte EDID 1.4 block
at 0xF0B80 (manufacturer CDX, product 1, one 1920x1080 detailed timing,
monitor name "codex-vm dsp"); `-no-edid` removes them and `-edid-bad` breaks
the checksum. Measured 2026-08-18: the type 0 and type 1 string sets carried a
stray third NUL, which a spec walker reads as a zero-length structure and
stops on; fixed the same day, and the ladder's smbios stage is the arm.

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

Set `$env:CODEX_VM_HOST='qemu'` to force QEMU. It is the only switch that
does anything: `vm-config.ps1` decides the host with
`$script:UseCodexVm = (codex-vm.exe exists) -and ($env:CODEX_VM_HOST -ne
'qemu')`, and it reads that variable and no other.

**`USE_QEMU` is not a switch and never was.** This section told the reader
to set it until 2026-08-18, and `gdb-watchpoint.ps1` set it too, which is
how the GDB watchpoint workflow came to be silently dead on every box that
has `codex-vm.exe`: the host stayed codex-vm and `-gdb tcp::1234 -S` went
to a VM with no GDB stub. Measured by dot-sourcing `vm-config.ps1` both
ways, `USE_QEMU=1` leaves `UseCodexVm` **True**.

```powershell
$env:CODEX_VM_HOST = 'qemu'
build/test.ps1 -Jobs 4
```

`CODEX_ACCEL` overrides the accelerator (`whpx` on Windows, `tcg` on Linux).
`kernel-irqchip=off` required for bare-metal operation under QEMU.

**Where a QEMU compile's time goes, and it is mostly not the guest.**
Measured 2026-08-18 on `plug-oracle-arith` (103,645 bytes of output) under
WHPX, through `vm-config`'s own boot path: QEMU process start 565 ms, guest
boot to READY 404 ms, input send 260 ms, compile-and-output 1,419 ms,
teardown 17 ms. Two traps sit in that profile:

- **Do not time a QEMU compile with a bespoke `qemu-system-x86_64`
  invocation.** A hand-rolled one that does not honour the boot contract
  (the `0xfe8` RAM-size loader cell, the two chardevs) produces a few
  hundred bytes instead of the full binary because the guest never ran,
  and the run still looks like it finished. Drive it through `compile.ps1`
  with `CODEX_VM_HOST=qemu`.
- **`Invoke-VmCompileFallback` used to pay a flat 2 s per compile.** Once
  SIZE's declared payload was complete it set `ReadTimeout = 2000` and did
  one more blocking read to drain a trailer that in practice is never
  there. Now 250 ms. If a QEMU timing looks like it has a constant floor
  in it, this is the shape of thing to look for.

The output path itself is no longer per-byte: `__write_binary_buf` polls
LSR once and `rep outsb` a burst of up to 16 through the 16550 transmit
FIFO, so a burst is one VM exit rather than sixteen. codex-vm handles the
string I/O in one exit as well (`codex-vm.c`, the `StringOp` arm on 0x3F8);
without that arm a burst would write one byte out of RAX and skip the rest.

### ARM64 UEFI boot under QEMU (`build/boot-arm64.ps1`)

Source to booted guest in one command: IR, ARM64 codegen, PE through the plug,
GPT disk image, QEMU. `-NoBoot` stops after the image.

```powershell
build/boot-arm64.ps1 -Src codex\test\arm64-web-server.codex                  # interactive
build/boot-arm64.ps1 -Src codex\test\arm64-web-server.codex -NoBoot          # build only
build/boot-arm64.ps1 -Src codex\test\arm64-web-server.codex -TimeoutSec 45   # as an arm
```

**`-TimeoutSec` defaults to 0, which is the interactive console: foreground
`-nographic`, no deadline, Ctrl+C to stop.** Any positive value launches QEMU
monitored, sends the UART to `build/arm64-output/arm64-serial.log`, and kills it
at the deadline.

**A TIMEOUT EXITS 0, deliberately, and the printed line is what tells you
which run you had.** For a server payload the deadline IS the intended end, so a
kill is not a failure and a nonzero code would cry wolf on every normal run. The
cost is that the exit code alone cannot separate a healthy guest from a wedged
one: `QEMU exited on its own, code N` and `TIMEOUT at Ns: ... the run did NOT
finish` are the discriminator. **Do not script a verdict off the exit code
here** -- read the line, and read the serial log knowing it may be a partial.

**The host-forward port is derived per workspace in 18080..18279, and a held
port is a REFUSAL.** It was a fixed 8080, so two agents booting arm64 answered
each other's requests. Same hash shape as `build/boot/test-ovmf.ps1`'s monitor
port and for the same reason (L-SHARED). It refuses rather than hopping to the
next free number, because a run that quietly moves its port answers on an
address the caller was never told. `-HostPort N` overrides. The chosen port is
printed as `hostfwd tcp::N -> guest :80`.

To drive requests at the guest, `docs/Probes/arm64-two-requests.ps1` is the
caller: it banks guest serial, QEMU stderr and a per-request verdict, and
refuses to report request results unless the guest announced it was listening.

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

### TAILING A LIVE VM LOG WITH `Get-Content` CAN KILL THE VM

A harness that polls a running codex-vm's redirected stderr or stdout to
watch for progress must open those files **sharing write**. Plain
`Get-Content` does not, and the writer's next write into the locked file
fails, which takes the VM down.

**What it looks like is not a file error. It is a guest that dies partway
through, silently, always around the same place.** Measured 2026-08-10 on
`build/sink-arm.ps1`: its pass arm ran a 2.7 MB FAT write and died after the
same rung on three consecutive runs, with no fault line, no `EXC`, and
nothing in either stream. The identical image invoked by hand -- same flags,
same `-output`, no polling -- reached its last rung every time. Two earlier
runs of the same harness had passed, and the only thing that changed between
them was the addition of the poll.

The tell is that shape: **an arm that fails under the harness and passes by
hand is the harness perturbing the run**, not a flaky guest. `-output` was
the obvious suspect and was wrong -- adding it to the by-hand run changed
nothing, which is what ruled it out.

Read a live log like this instead:

```powershell
$fs = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read,
                      ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
$sr = New-Object IO.StreamReader($fs)
$text = $sr.ReadToEnd(); $sr.Close(); $fs.Close()
```

Reading the streams AFTER the process is gone, which is what
`build/ladder-arm.ps1` does, is unaffected -- there is no writer left to
lock out. This only bites a harness that watches progress while the VM runs,
and the reason to watch is real: without it, an arm that holds its colour by
repainting never exits and costs its whole deadline.

### READING A COLOUR OUT OF `-screenshot`: DECODE THE BMP BYTE BY BYTE

`-screenshot` works, and a payload's paint DOES show up in it. Verified
2026-08-11 across four payloads: a minimal paint probe and `blockladder.img`
both render `FFFFFF`, `sinkladder.img` renders `FF8000` mid-write, and
`a5flight2.img` renders `FFFFFF`.

**A ladder that reports `painted fb=1` against an apparently black BMP is
almost always the reader.** This expression is the usual cause:

```powershell
($b[$i+2] -shl 16) -bor ($b[$i+1] -shl 8) -bor $b[$i]     # WRONG
```

`$b[$i]` indexes a `byte[]`, and PowerShell shifts a `[byte]` at BYTE width, so
both shifted terms are 0 and the result is the blue channel alone. Orange
(`FF 80 00`) decodes as `000000` and reads as black; white decodes as `0000FF`
and reads as "not white".

Cast, or read the channels separately and do not combine them:

```powershell
$b = [IO.File]::ReadAllBytes($bmp)
$off = [BitConverter]::ToInt32($b,10); $w = [BitConverter]::ToInt32($b,18)
$h = [BitConverter]::ToInt32($b,22)
$stride = [int][math]::Floor(($w * 24 + 31) / 32) * 4
$i = $off + ($h - 1) * $stride          # BMP is bottom-up; this is the TOP row
'R={0:X2} G={1:X2} B={2:X2}' -f $b[$i+2], $b[$i+1], $b[$i]
```

The general shape is worth more than the recipe: **a decoder that renders two
different inputs as the same glyph is the `peek-32` sentinel trap again**, and
it cost a false section in this file plus a harness built around the belief
that the bed could not see a colour at all.

`-screenshot <file> -screenshot-delay <ms>` also EXITS after the capture, which
is the only bounded way to run a payload that holds its colour by repainting.

### THE AD-HOC CHECK IS THE LEAST RELIABLE INSTRUMENT IN THE LOOP

The section above is one instance of a general rule, and the rest of the
instances cost a session each. **When a check you wrote in the moment
disagrees with the product, distrust the check first**, and prefer the real
harness to a reimplementation of it. Three false readings in one session were
all the apparatus; the product was fine every time.

- **`[uint32]0xFFFFFFFF` THROWS, because PowerShell parses `0xFFFFFFFF` as
  Int32 minus one.** Measured 2026-08-15 writing a CRC32 helper to check GPT
  fixtures: the seed line `$crc = [uint32]0xFFFFFFFF` threw on every call, the
  catch-free loop reported every one of 26 fixtures as failing both CRCs, and
  that read exactly like a corpus-wide finding. Write the constant in decimal
  (`4294967295`, and `3988292384` for the polynomial). **Validate any checksum
  helper against a published vector before believing a single answer from it**:
  `CRC32("123456789")` is `0xCBF43926`, and one line asserting that turned the
  false corpus-wide finding into a real 21-verify / 4-stub split.
- **`[int]` ROUNDS in PowerShell, it does not truncate.** `[int](16895 / 512)`
  is 33, not 32, so the usual `(n + d - 1) / d` ceiling idiom comes out one
  high. Use `[Math]::Floor`. It landed a `FirstUsableLBA` one sector past where
  it belonged in four disk fixtures, which is harmless and wrong, and the same
  idiom in Codex is correct because Codex integer division truncates.
- **PowerShell's error formatting ECHOES THE SOURCE LINE before the message,
  and gutters the wrapped continuation with `|`.** So the FIRST occurrence of
  your own message text in captured output is the uninterpolated source, not
  the message: scoring refusal arms against `REFUSED: ([^.]+)` matched the
  literal `$why` from the `throw` statement and failed four arms that were
  working. Strip the gutters, collapse the wrap, and take the LAST match. And
  clamp a captured string for DISPLAY only -- scoring against the clamped copy
  failed the one arm whose phrase sat past column 74.
- **Array splatting binds POSITIONALLY, and against `test-run.ps1` it DESTROYS
  the file you name.** `$a = @('-Kernel', $k, '-OutFile', $o)` then
  `& test-run.ps1 @a` puts the literal string `-Kernel` in `$Kernel` and the
  KERNEL PATH in `$OutFile`; the no-output path then writes an empty file to
  `$OutFile`, so every run truncates the kernel to 0 bytes. It reads as a
  broken environment rather than a bad call, because the run "fails" and the
  next run fails differently now that the kernel is empty. Use a HASHTABLE
  splat (`@{ Kernel = ...; OutFile = ... }`), and when a test fails assert the
  kernel still has content BEFORE believing the result: a 0-byte kernel and a
  failing test are indistinguishable downstream.
- **`2>&1` does not capture `Write-Host`. Use `*>&1`.** Most build scripts here
  write progress that way, so `$t = & .\build.ps1 2>&1 | Out-String` leaves
  `$t` EMPTY while the output scrolls past on the console. A pass test of the
  form `if ($t -notmatch 'OK:')` then reports every arm failed; 44 plugs that
  had all printed OK came back as 44 failures. The dangerous direction is the
  mirror image: `-match 'FAIL'` over an empty string is False, so a real
  failure passes silently. Check the exit code, which is never empty, and if
  you must match text assert `$t.Length -gt 0` first.
- **`-match` and `-notmatch` are case-INSENSITIVE, and a cleaning filter is
  where that bites.** `-notmatch 'HEAP-'` also drops every `heap-` line, so a
  text cleaned for comparison silently lost four emitted `heap-*`/`stack-*`
  lines while the exact-match consumer downstream still expected them. The
  failure is quiet in the worst direction: the filter looks like it did its
  job and the diff that follows is against a text nobody wrote. Use
  `-cnotmatch` / `-cmatch` wherever a filter has to agree with a
  case-sensitive reader. Related, same family: `"$$n"` in a double-quoted
  string expands `$$` as an automatic variable and leaves `n` literal, so
  you get `n` rather than a literal `$` followed by `$n`. The specimen was
  written `"\$$n"` until 2026-08-27; a backslash escapes nothing in
  PowerShell, whose escape character is the backtick, so as written it was
  a different construct from the one described.
- **`Measure-Object -Line` silently DROPS EMPTY LINES, so it is the wrong
  instrument for a file size and it never says so.** Measured 2026-08-15 on
  `docs/Agents/PerforceProcess.md`: `(Get-Content $f).Count` = 500,
  `Measure-Object -Line` = 405, and the file has exactly 95 blank lines. The
  gap is the blanks, every time. It is dangerous precisely because 405 is a
  plausible number for that file, and the usual place it lands is a CL
  description quoting a size or a percentage cut, where nothing will ever
  re-measure it (L-COUNT). Count with `(Get-Content $f).Count`, or count the
  line-ending bytes; two instruments agreeing is the check.
- **A pipeline that greps a command's output discards that command's error
  message**, so a failed command reports as a clean result. `p4 diff -du -c
  <CL> 2>&1 | Select-String '^\+' | Measure-Object` returns 0 for a CL with
  164 added lines, because the `2>&1` folds `Invalid option: -c.` into the
  stream and the `^\+` filter eats it. The command was never silent; the
  pipeline silenced it. **`$LASTEXITCODE` survives the pipeline** and was 1
  the whole time, so test that rather than the filtered count. The Perforce
  instance is `P-DIFFC` in `docs/Agents/PerforceProcess.md`, but the shape is
  general and this tree is full of `2>&1 | Select-String`.
- **A one-element array unrolls to a scalar, and indexing a scalar STRING
  yields a character.** The loud instance reads `unresolved symbol 'm'` for
  `"main"`. The silent instance is the one to fear: a list of mnemonics
  derived each as its initial letter and printed a clean, plausible, entirely
  wrong opcode table. Force `@(...)` on the assignment or type the variable
  `[string[]]`. The registry harness section in `ExaminersAssay.md` has the
  same trap costing a green that could not have passed.
- **Name a helper carefully; a short name can shadow an alias.** A comparison
  helper called `H` bound to the `Get-History` alias, both hashes came back
  empty, and the check printed EQUAL. Assert the expected length before
  comparing -- two empty strings are equal, which is the exact shape of a
  check that passes because it never ran.
- **A substring match fails on wrapped prose.** Checking that a merge kept both
  sides, with a phrase that spans a line break in the file, answers False for
  both. Grep a short distinctive fragment, or the line number.
- **Do not filter a long run's output at the pipe.** `build.ps1 | Select-Object
  -Last 26` leaves a log holding 26 lines, and a later search of that whole
  file is not the wide check it feels like: the file is already the narrowed
  thing. It has twice produced a confident wrong answer -- once reporting that
  the gate prints no one-pass line when it prints it -- grep `hard fixed
  point in one pass`, cited as `build/build.ps1:620` when that was written
  and at `:662` now, having moved twice since --
  and again on 2026-08-27 when a filtered gate capture showed a
  `deck-headroom` FAIL with the offending unit's name and the entire
  derivation table absent, so the visible text named a cause the run had not
  established. Let the log land whole and filter at read time; when the
  question is "does X ever appear", search the SOURCE that would print X, not
  a capture of it. The truncation cannot be undone and costs a rerun to
  recover.

### A gate run as a tool-call child dies with the session, and it reads as host trouble

A gate launched as a child of an agent tool call is killed when that tool
call (or the whole session, on a restart) is killed, and what the next
reader sees is a run that died mid `test-compile` on a box that measures
clean -- no stray codex-vm, RAM fine -- which invites the "host
conditions" diagnosis and, from there, a fleet stand-down that clears
nothing. It happened exactly that way on 2026-08-28: one lane's gate 4
died on a clear box, a stand-down was called on the host-conditions
premise, and the premise was retracted the same hour.

The discriminating measurement (blu, same day): two `-Internal` runs
died that way as children of killed tool calls; the SAME gate on the
SAME box relaunched DETACHED via `Start-Process` (PID kept) then
survived a watcher being killed under it and ran to green. Nothing about
the box changed between the runs; only the process parentage did.
Fleet session restarts are not rare -- session addresses visibly roll
mid-conversation -- so the parent can vanish through no action of
yours.

The mitigation is the one the kill section below already wants for a
different reason: **launch the gate detached (`Start-Process`), keep
the PID, and poll or wait on that** rather than running the gate inline
in a tool call. Before diagnosing a died-mid-phase gate as host or
codegen trouble, ask first whether its parent process outlived it.

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

### Rebuild the transpiler plugs yourself; nothing else does

`codex/plugs/*/build-output/*-plug.cdx` is not a depot file --
`build-output/` is in `.p4ignore` -- so each workspace has its own, and
they are only as fresh as the last hand run of that plug's `build.ps1`.
A merge-down brings you new plug SOURCE and leaves your binaries where
they were.

```powershell
pwsh codex\plugs\rust\build.ps1                  # one plug, about 3 seconds
Get-ChildItem codex\plugs -Directory | ForEach-Object {
  $b = Join-Path $_.FullName 'build.ps1'
  if (Test-Path $b) { & pwsh -NoProfile -File $b *> $null }
}                                                # all of them, about 150 s
```

### Does every plug have an arm for the builtins that reach it

```powershell
pwsh build/check-plug-builtins.ps1            # OK, or the gaps + exit 1
pwsh build/check-plug-builtins.ps1 -Update    # rewrite the baseline
```

It compiles `codex/test/plug-oracle-arith` to IR under `-Passes 'text-plug'`,
which is the pipeline a source plug actually receives, takes every
`(name "X")` whose `X` is a declared builtin, and asks each wired plug's table
for an arm. A `FAIL` line reads `<plug> <builtin>`: that builtin arrives on
that plug's wire and the plug has no arm, so it emits a call to a function the
target never defines and only the target's own toolchain would ever notice.

**Deliberately not wired into `build.ps1`** (it costs a compiler run) and it
covers the WIRED plugs only. `pascal`, `fortran` and others register builtins
in a shape it does not model, so it refuses on a thin extraction rather than
listing phantom gaps against them. The compiler as a subject is not re-checked
here either: that case is the DDC witness, which already fails on a missing
builtin.

**Do this after any change under `codex/plugs/common/`**, which every plug
bundles. On 2026-08-11 a workspace held 44 plug binaries from 08-06 against
a shared `IRTextParser.codex` from 08-08, and 37 of 38 runnable plugs died
with an invalid opcode in `parse-type-record` the moment the IR contained a
record construction. The gate does not catch it: `plug-smoke` rebuilds a
plug only when its CDX is MISSING, and tests four of them on one
record-free program. Symptom to recognise: the plug VM exits `code=-1` and
the harness reports `plug produced no output` (exit 6).

Timestamps are the fast check -- compare
`Get-ChildItem codex\plugs\*\build-output\*-plug.cdx` against
`codex\plugs\common\*.codex`.

### `build/clean-zombies.ps1` is scoped to YOUR OWN workspace by default (2026-08-28)

It purges orphaned `qemu-system-x86_64`, `codex-vm` and `wsl` processes
after a SIGKILL-style abort (a `taskkill /F`, a harness OOM, a wedged WSL
VM) left the traps in the test, build and 3stage harnesses unable to run.

**A bare run kills only processes whose command line names the calling
workspace.** Until 2026-08-28 it was fleet-wide by name, the section above's
warning made deliberate -- and a bare run during another agent's gate killed
three gates in one day, each reading as a truncated-artifact failure with
nothing naming a kill. The old reasoning ("an orphan has no workspace left
to scope it to") was wrong: the workspace is in the dead launcher's COMMAND
LINE, which survives it. A foreign agent's VM, a bare WSL bridge and
`vmmemWSL` all survive a scoped run; your own GDB bridge names your repo
path, so it does not. `-All` restores the fleet-wide sweep (and is the only
mode that touches WSL); it is for a wedged BOX, not a wedged workspace, and
it still spares `-portfwd` servers. Proven by paired impostor arms: the
owned process dies and the foreign one survives a scoped run, and `-All`
takes the foreign one.

**Scoped still means YOUR LIVE GUESTS, not just your orphans -- the sweep
cannot tell them apart.** Run it BETWEEN your runs, never while anything of
yours is up: a sweep launched beside your own live gate kills that gate by
your own hand (nearly demonstrated 2026-08-28, caught by the lane it would
have hit).

```powershell
powershell -NoProfile -File build/clean-zombies.ps1        # your own zombies
powershell -NoProfile -File build/clean-zombies.ps1 -All   # the whole box
```

**It never calls `wsl --shutdown` unless `vmmemWSL` is already up, and
that fast path is not a micro-optimisation.** `wsl.exe` SPINS THE VM UP
IN ORDER TO SHUT IT DOWN, so the unconditional call costs minutes on a
box where WSL was simply idle. The same fast-path rule covers the other
two names: a process that is not running is skipped rather than asked
about.

This doctrine had no home but the script's own header comment until
2026-08-11, when adopting the generator dropped the header. Nothing else
in the tree described the tool.

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

**`True` ON STDOUT IS NOT THE VERDICT.** `compile.ps1` prints `True` on a
refused compile exactly as it does on a clean one; the verdict is the EXIT
CODE (4 on codegen errors) and the `-Log` file, which carries
`CODEGEN-ERRORS:<n>`, the diagnostics themselves, and `CODEGEN-HALTED: errors
in bag; no binary emitted`. A clean log holds only `info` lines. **The other
tell is that no `-Out` file appears**, which is easy to miss when a stale one
from an earlier run is still sitting there. Measured 2026-08-19: a refusal
read as a pass off that `True`, and the wrong reading was published before the
log was opened.

**IN `-Text` AND `-IrUni`, EXIT 4 IS THE SUCCESS PATH AND THE EXIT CODE MEANS
NOTHING. `-IrCce` IS NOT LIKE THEM.** The paragraph above holds for `CDX`,
where a clean compile exits 0. The modes do NOT agree with each other, and
assuming they do is how this entry got written wrong the first time. Measured
2026-08-27 against seed `0634584EF849D297`, same source and same invocation
shape:

| mode | exit | `-Out` | `-Log` |
|---|---|---|---|
| `-IrUni` | **4** | never written | the wire, bracketed by `IR-BEGIN` / `IR-END` |
| `-Text` | **4** | never written | the text, no `IR-BEGIN` |
| `-IrCce` | **0** | **written** (1,109 bytes here) | diagnostics only, no `IR-BEGIN` |

`-Text` and `-IrUni` emit no `SIZE:` line, so the scan that looks for one runs
off the end of the output and the script falls through to the bare `exit 4` at
the bottom of the compile loop, having written the whole guest output into
`-Log`. In those two a clean emit, a crash and a codegen refusal all carry the
SAME exit code, so the verdict is the `-Log` file: `IR-BEGIN` / `IR-END` bracket
the wire and `error CDX....` lines carry the diagnostics. `-IrCce` takes the
binary path instead and behaves like `CDX`, so there the exit code IS the
verdict.

The first version of this entry said all three behaved alike, on a reading of
`compile.ps1`'s control flow rather than a measurement of each mode, and a
naive-reader probe caught it the same day. Reading one branch of a dispatch and
generalising to its siblings is the same error as reading one tier and
concluding about three. The half that was right is worth keeping: a harness
gating on the exit code in `-IrUni` reports every run as a failure with the
correct IR sitting in the log, which is L-ACCEPTED pointed at a RESULT rather
than an argument.

### `build/ir-fidelity` -- did the IR carry what the checker knew?

When a plug emits a wrong type for a value and the program compiles clean, the
question is whether the compiler worked the type out and failed to put it on
the wire, or whether the program never constrained it. Those need different
fixes and they look identical from inside a plug, which erases or guesses
either way.

```powershell
build\ir-fidelity\ir-fidelity.ps1 -Grade                  # grade the arm, then run every case
build\ir-fidelity\ir-fidelity.ps1 -Only lambda-param-type # one case
build\ir-fidelity\ir-fidelity.ps1 -Passes                 # audit the OPTIMIZED program instead
```

A case is three programs and one wire position: `a` and `b` differ in one
respect and both compile clean, `knows` is a program the checker must REFUSE
with a named diagnostic, and `path` names the cell to compare. **CARRIED** means
the checker knew and the wire carried it, so a wrong type downstream is the
plug's. **DROPPED** means the checker knew and the wire did not, so it is
upstream. **UNCONSTRAINED** means the `knows` arm did not refuse, so the case
proved nothing and wants fixing before its cell is read. **UNSUPPORTED** means
the reader could not find the cell; expect it under `-Passes` where the
optimizer inlined the subject away. The last two are deliberately not passes.

The reader (`ir-wire.ps1`) has no plug opinion in it and shares no code with
`codex/plugs/common/IRTextParser.codex`. Run it from the depot root. Cost is
about 0.3 s per compile and three compiles per case. It is NOT wired into any
gate; that is Damian's ruling. `codex/plugs/plugs-backlog.md` 1.96 carries the
standing verdicts and what they mean.

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
- **The SIZE of a seed-to-seed difference carries no information, so do not
  read a change's reach off it in either direction.** Measured 2026-08-15
  over the last seven seeds on main, consecutive pairs differ by 97 bytes,
  2,354,783, 2,282,139, 1,211,880, 2,395,976, 364,032 and 2,536,593 -- four
  orders of magnitude, and the first difference sits at offset 8 every time
  because that is the content hash. A big diff is not evidence of a big
  change and a small one is not evidence of a safe change. The hash compare
  above answers whether a seed is needed; the gate answers whether the
  compiler still agrees with itself. Nothing else is a reading.

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

**But a codegen change can be VERIFIED without any of that.** A gate
followed by rebuilding whatever app you care about compiles that app with
the changed compiler, and no seed is written or submitted anywhere. The
seed rebuild above is how a codegen change LANDS, not how you find out
whether it works. Worth knowing before queueing for a seed cycle to
answer a question one gate already answers.

**Pass `-Kernel` when you do, because the default is not what you think.**
`build-output/bare-metal/Codex.cdx` is not the SUT, and it is not the seed
either.
`Build-Cdx` and `Build-Text` in `build.ps1` each copy their own kernel
over that path before running, so it ends up holding whichever kernel the
LAST compile phase used, not whichever artifact the run produced.
Measured after each of two green `build.ps1` runs on 2026-08-11, its
content hash equalled `seed/Codex.cdx` exactly (`AF4E14D9703985AC`),
because the phases after `plug-binary` compile against the seed.

That is the dangerous direction. Verifying a codegen change with the
default kernel boots the OLD compiler, the emitted wire is unchanged, and
the honest reading of that is "my fix did nothing" when the fix was never
in the binary under test. It has already produced a wrong answer: a
per-chapter sweep of `apps/works` reported roughly 80 of 84 chapters
compiling where the real figure against the depot seed was about 55,
because `build-output` held a kernel from before the `file-exists`
builtin was removed and every chapter calling it still resolved. The
sweep was measuring a compiler that no longer existed.

So `compile.ps1` prints the kernel and its digest on stderr on every run,
and warns when you did not ask for one and it is not the seed. Read the
line. If you are measuring anything at all, name the kernel:

```powershell
build/compile.ps1 -Src x.codex -Out x.cdx -Log x.log -Kernel build/output/Sut.cdx
```

`build/output/Sut.cdx` is what a gate just built; `seed/Codex.cdx` is the
shipped compiler. Compare the printed digest against the one you think
you are testing.

**And the default is resolved RELATIVE TO THE CURRENT DIRECTORY, so the same
command works from the repo root and reports a missing kernel anywhere else.**
`compile.ps1:58` sets `$Stage0 = Join-Path 'build-output' 'bare-metal'
'Codex.cdx'`, a relative path, and prints `MISSING: build-output\bare-metal\
Codex.cdx - run build.ps1 first`. Run from a subdirectory, or from a
background call that inherited a different location, that reads as the
COMPILE failing rather than as the path not resolving, and three subjects in
a row reported as failing to compile is convincing. `cd` to the repo root
before any compile, or pass `-Kernel` with an absolute path.

(The CDX fixed-point check compares content ignoring the signature bytes,
so a signed `Sut` and the unsigned `stage1` still register as one pass when
their code is identical. On copy-up, the gate is `Sut === seed` rebuilt on
the *target* workspace -- see `docs/Agents/PerforceProcess.md`.)

**Building the compiler by hand needs `-Repl`, and without it you get a
different binary that looks like a codegen difference.** `Invoke-BuildCdx`
in `build.ps1` compiles every stage with `compile.ps1 -Repl`, and nothing
outside that function does. Measured 2026-08-09 against the same source and
the same kernel: `-Repl` gives 2,753,304 bytes and is BYTE-IDENTICAL to the
gate's `build/output/stage1.cdx`, while omitting it gives 2,753,312. So a
hand-built compiler compared against `stage1.cdx` or the seed disagrees by
8 bytes for a reason that has nothing to do with the change under test.
Pass `-Repl` whenever the artifact is meant to BE the compiler; leave it off
when compiling an ordinary program. L-SAMEVER: confirm the two things you
are diffing were built the same way before concluding anything about either.

**That difference is real codegen and is meant to stay.** `X86_64Chapter`
ends the chapter on `st.exit-mode`: `Exit` emits the shutdown epilogue
(`li rax,0`, `li rdx,244`, `out dx,al`, `hlt`, `jmp self`) and anything else
emits a single `jmp repl-loop`. A REPL binary that shut the machine down
would be the defect. Do not try to make the two byte-identical.

**The quarter of a million differing bytes between the two modes are that
one stub and nothing else (COMPILER-3, closed 2026-08-16).** The item was
filed because a positional byte compare of the two arms reports ~9 per cent
of the file different, first difference at offset 8, spread the whole
length, which reads as an emitter that answers differently in the two
modes. It is an artifact of comparing at absolute offsets. Measured against
seed `55983566` on the 2,817,743-byte concatenation of `codex/compiler`,
`-Repl` at 2,798,023 bytes against 2,798,031 without it, 258,186 bytes
differing:

| region | size | differing | what it is |
|---|---|---|---|
| header | 276 | 37 | the content hash at bytes 8-39, plus 5 length and offset fields |
| code | 2,390,823 | 3,744 | 3,641 sites, every one a 32-bit absolute data address, all exactly +8; nothing unclassified |
| data | 254,626 | 126,893 | identical under a +8 shift but for 9,136 embedded 64-bit pointers, themselves +8 |
| MAP1 and tail | 152,298 | 127,512 | identical under a +8 shift but for ONE byte: `__start`'s size, 9545 against 9556 |

`__start` is the last function in the code section, so the 11 bytes the
`Exit` epilogue adds over `jmp repl-loop` push everything after it;
alignment padding absorbs 3 and the rest of the file relocates by 8. The
two embedded maps agree on all 5,194 names and offsets apart from that one
size. Every differing byte in the code section is an address that moved,
not an instruction that changed: **the emitter answers identically in both
modes.** Control, and run it before believing any number here: the same arm
compiled twice is byte-identical, so nothing in this is nondeterminism.

**What was NOT meant to stay was `map` riding on the same switch**, which
is why a compiler build emitted no symbol map. Fixed 2026-08-14; the two
flags are independent in `compile.ps1` now, as they always were in
`emit-cdx`.

### Two ways a hand-run probe serves the LAST run's answer

Both are silent, both look like a result, and both have produced a published
number that was not about the change under test.

**`build/compile.ps1` prints `True` on a FAILED compile.** The `True` is the
script's own success, not the compiler's verdict, so a probe that reads it
goes on to run whatever `.cdx` was already at that path. Delete the output
first, gate on the log, and check the file exists afterwards:

```powershell
Remove-Item probe.cdx -ErrorAction SilentlyContinue
build/compile.ps1 -Src probe.codex -Out probe.cdx -Log probe.log -Kernel seed/Codex.cdx
if (Select-String -Path probe.log -Pattern "error CDX" -Quiet) { throw "compile failed" }
```

Match the number form as well as the `CDX` form when a harness does this for
you: `error 3010: Unresolvable cite` carries no `CDX` prefix, and a checker
matching only `error CDX` served a four-day-old IR file as a fresh verdict
twice in one session. Comparing the output's `LastWriteTime` against the
clock is the habit that catches the whole class.

**`tools/codex-vm.exe -kernel X.cdx -headless` by hand prints NO program
output, and does not error.** Measured 2026-08-08: one probe read as a crash
(EXC=06) and a variant read as a clean silent exit, and a trivial
`print-line-uni (show 15)` control run the same way ALSO printed nothing --
so both readings were void, and the silence was the invocation rather than
the subject. Run programs through `build/test-run.ps1`, which supplies
`-output` and the comparison the battery uses. The control is what caught it,
and it cost two cycles before one was run (L-SIDECAR).

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

**All three further steps are now taken, and the result is byte-identical.**
Compiled in the mode the gate actually uses, the Roslyn arm reproduces
`build/output/NewSeed.cdx` (the gate's own unsigned stage1) with zero
differing bytes across the whole file. Against the shipped `seed/Codex.cdx`
the only difference is the 96-byte signature region at offsets 40..135,
which is zeros in an unsigned build and is stamped in place by the sign
phase rather than emitted by the compiler. Header bytes 0..39 and 136..223
agree and the entire body from 224 agrees. The `source -> IR` arm matches
too: both compilers run `IR-UNI` over the whole compiler source and emit
byte-identical IR. Nothing in `build/` invokes any of this on the gate; it
is run on demand.

**Five rules make the comparison mean anything. Each was learned by a false
result.**

- **ALWAYS compile the compiler with `CDX repl` when comparing against the
  seed.** `build.ps1` passes `-Repl` and `compile.ps1` turns that into the
  `CDX repl` mode line; without `-Repl` it appends `map` instead. Exit mode
  is a real codegen difference, so `CDX`, `CDX map` and `CDX repl` are three
  different binaries from one source. **Two content hashes disagreeing is
  not evidence of a defect; it is evidence that two different things were
  compiled.**
- **Run `emit-compiler.ps1` in FULL before any comparison.** `-SkipIr`
  reuses the previous `compiler.ir` and therefore the `Codex.codex` it was
  assembled from, and a merge-down moves `codex/compiler/` underneath you
  with no signal. That is how a stale bundle passed for a compiler defect
  through two sessions, surviving six individually sound eliminations that
  were all aimed at the transpiler while the untested assumption -- that
  both arms were the same program -- was the only wrong one (L-SAMEVER).
  **Compare `LastWriteTime` on `build-output/Codex.codex` against the newest
  file under `codex/compiler/` before believing any disagreement.** Reserve
  `-SkipIr` for iterating on the plug, where the compiler source is genuinely
  fixed.
- **Give both compilers the same input, and do NOT use `compile.ps1` to do
  it.** It assembles a unit (cites resolved, chapter lines rewritten) and
  that is not the file. Write the mode line, the source and a trailing
  `0x04` as UTF-8 without BOM, feed that to the .NET build's stdin and to
  `codex-vm -input`. The stderr line `all_consumed=1` confirms the guest read
  every byte. Both sides take the mode on stdin as one line, then the source.
- **The symbol map is the instrument for CDX, not the byte diff.** Pass
  `CDX map` and compare the `MAP:` block. One early size difference shifts
  every later absolute address, so a raw byte diff reports near-total
  disagreement and localises nothing; the map gives per-helper sizes at
  relative offsets.
- **`opening`'s Integer return does NOT surface as the codex-vm exit code.**
  A control returning 7 also reports 0, so reading an answer out of an exit
  code can "find" exactly the bug you are hunting. Print through the console
  and `-output`.

**One known structural limit, not a defect to chase.** The C# build prints
`warning CDX9003: pmap-walk self-test FAILED`. `pmap-self-test` builds
records and walks their raw memory; on .NET records are CLR objects rather
than bytes in the arena, so the walk sees nothing. It is a diagnostic and
does not alter emitted code, which is why the CDX still matches byte for
byte while the warning prints.

### Running the DDC end to end

This is a release gate (release skill, step 4).

**The pass has two conditions: the arm's output is the same LENGTH as
`seed/Codex.cdx`, and ZERO bytes differ outside the signature region at
offsets 40..135.** How many differ inside it is not a criterion and must
not be quoted as one -- 96 is the region's WIDTH, and two unrelated
signatures agree at a given byte about one time in 256, so a run differing
in 95 of the 96 is ordinary. Measured 2026-08-10 against seed `AF4E14D9`:
96. Measured 2026-08-12 against seed `527C2C75`: 95, on both arms
independently.

```powershell
$R = 'D:\Projects\Cobblestone-<agent>-main'

# 1. Emit. Do NOT pass -SkipIr on a release run.
& "$R\codex\plugs\csharp\emit-compiler.ps1" -Kernel "$R\seed\Codex.cdx" `
    -Out "$R\build-output\Codex.cs"

# 2. Build the arm with Roslyn. Scaffold once: a net9.0 Exe csproj beside
#    Codex.cs, Nullable disable, LangVersion latest.
dotnet build "$R\build-output\ddc-arm\CodexCs.csproj" -c Release

# 3 and 4 are build/ddc-witness.ps1. It builds ONE input file and feeds it to
#    BOTH arms, slices each at the CDX1 magic and applies the two conditions.
build/ddc-witness.ps1 -Repo $R -Source "$R\codex\plugs\csharp\build-output\Codex.codex"
```

**Steps 3 and 4 are scripted rather than prose**, because doing them by hand
re-derives the one proof that does not take the compiler's word for
anything. The part that is easy to get wrong is
reconstructing the exact bytes `compile.ps1` feeds the compiler (the mode
line is `CDX repl`, because the seed is built `-Repl`), and a wrong
reconstruction makes the C# arm disagree for a reason that has nothing to do
with trust. So the runner hands the SAME input to both arms and **treats the
Codex arm reproducing the shipped seed as the precondition for the C# arm's
answer meaning anything**; if it does not, the run is INCONCLUSIVE rather
than red.

**`build-output/` does not survive a gate run**, and both DDC prerequisites
live there: the `ddc-arm` csproj scaffold and `csharp-plug.cdx`. Expect to
rebuild the plug (`plugs/csharp/build.ps1 -Force`) and re-create the csproj
on any release where a gate has run since the last one. The plug's builder
pins `-Kernel` to `<repo>/seed/Codex.cdx`, resolved from the repo root rather
than from the process working directory (`plug-build-lib.ps1:149`), so the
seed under audit has to BE the one in `seed/`; staging a kernel anywhere else
does nothing.

**Four things that will give you a false answer, three of them measured
here on 2026-08-10.**

- **The plug must be built with the seed under audit, and `seed/Codex.cdx` is
  the only seed it will use.** `codex/plugs/common/plug-build-lib.ps1`:149
  passes `-Kernel <repo>/seed/Codex.cdx` explicitly; neither
  `plugs/<name>/build.ps1` nor `build/plug-build.ps1` takes a `-Kernel` to
  override it. Auditing any other seed means installing it at that path first.
  Read the `kernel:` digest the compile prints and check it against the seed
  you meant to audit. (This paragraph said the opposite until 2026-08-20,
  when the builder was measured rather than remembered.)
- **The C# arm writes diagnostics to stdout AHEAD of the binary.** The run
  above put 67,380 bytes of warning text before the CDX. Find the `CDX1`
  magic and slice from there; comparing the raw stream reports total
  disagreement and localises nothing.
- **Do not trust `plugs/csharp/build.ps1`'s exit code alone; check that the
  plug binary is actually newer than the bundle.** On 2026-08-10 it reported
  "FAIL: compile errors" twice on a bundle that compiles clean, naming a
  `build.log` that was not at the path it printed, then succeeded on an
  identical third invocation. The cause is NOT established and both earlier
  failures were in runs chained behind another codex-vm, so file contention
  is a suspect and nothing more. What is certain is that the emit step will
  happily use a stale plug and produce a confidently wrong answer.
- **A new compiler builtin needs a matching `BuiltinEmitter` in the plug**,
  or the emitted C# references a name that does not exist and Roslyn refuses
  it. Ordinary upkeep, the same as teaching any transpiler a new primitive:
  add the entry beside the others in
  `codex/plugs/csharp/CSharpEmitterExpressions.codex`; the stub is `"0L"`
  wherever a hosted C# build has no such device. Running the DDC on the
  release is what surfaces it if the entry was missed.

### The witness has a negative control: it has been made to fail

A DDC that only ever comes back green is the exact instrument L-FALSIF
warns about. It has been falsified deliberately (2026-08-10), and the
procedure is worth keeping because the result names the boundary.

Poison the compiler in one source site, build `A'` from the poisoned
source WITH THE CLEAN SEED, then REVERT the source before using `A'` -- so
`A'` is a compiler binary whose payload appears in no source in the tree,
which is Thompson's setup. Run `X = A'(clean source)` and the witness
chain (`A' -> IR -> C# -> stage1 -> stage2`), then compare stage2 against
both `X` and the clean seed.

Two sites were tried, each semantically inert so the compiler still works
and only the bytes move:

- **Code generator** (`X86_64Helpers.codex`, one extra `0x90` in a wait
  loop): stage2 differed from `X` by **2,232,990 bytes** and matched the
  clean seed to the signature. The payload never reached the IR (its size
  was byte-for-byte the clean IR's), so the C# arm built an honest stage1.
- **Lowering** (`fold-constants-in-chapter` stubbed to `ch`): stage2
  differed from `X` and again matched the clean seed. The payload DID reach
  the IR this time, but stage2 still reconstructed the honest compiler,
  because `A'` merely failed to fold -- it did not teach its output to fail
  to fold.
- **Frontend IR emission** (`X86_64Helpers.codex` `emit-read-line-helper`,
  the same one `[144]` nop as the code-generator arm, PLUS
  `IRTextEmitter.codex` `ir-emit-def` returning that function's trojaned IR
  def verbatim whenever it emits that function): stage2 == `X` byte-for-byte
  (SHA `EEDFECC9...`) and both differ from the clean seed by 2,104,373 bytes.
  **This one is NOT caught by the byte-comparison.** The hook makes `A'` write
  the trojaned `emit-read-line-helper` into the IR of clean source, so the C#
  arm builds a trojaned stage1 and stage2 carries the same payload as `X`. It
  is NOT self-reproducing: `ir-emit-def` substitutes a static def and stage1
  never re-emits IR. Its own control is the code-generator arm above -- the
  same nop with no hook, which IS caught -- so the hook is the only variable
  that flips the verdict from caught to survived. Measured 2026-08-11.

**The boundary is "reaches the readable intermediate" versus not, not
"self-reproducing" versus not (corrected 2026-08-11).** The frontend-IR arm
survives with no self-reproduction, so the earlier "the one payload that
would survive is a self-reproducing quine" was too narrow. What holds: a
payload living only in the binary is caught anywhere (arms one and two),
because the rebuild starts from clean source; a payload survives the
byte-comparison only if `A'` writes it into the IR that becomes the C#.

**And that is the neutralization, not a leak.** A survivor is, by
construction, sitting AS READABLE TEXT in the IR and in the emitted C#:
measured, the injected `st7z` / `[144]` was greppable in both. So the two
halves cover the whole space. The byte-comparison catches anything hidden
only in the binary (arms one and two, the classic Thompson shape). Anything
that slips past it had to reach the IR that becomes the C#, and is therefore
visible as text that can be grepped, diffed across re-emissions, and
content-addressed. Thompson's attack depends on the payload being invisible
because a binary is unreadable; here there is no invisible surviving form. **A
trojan hidden in the binary is caught by the rebuild; a trojan that survives
the rebuild is not hidden.** That readable-intermediate property, not the
byte-comparison alone, is what defends against the Thompson attack here. See
"The residual hole, stated plainly" below for the two upstream seed positions
this covers.

If you add a DDC sabotage arm to a harness, confirm the probe FIRES first
(stage2 must differ from a clean build) before trusting a red verdict --
an inert edit that never reaches the output is a vacuous arm.

**`jonquil: FAIL -- no IR-BEGIN/IR-END` usually means the guest DIED, not
that anything is wrong with the IR.** The phase compiles the whole compiler
to IR under the SUT and greps the result; when the compile dies the log is
near-empty and the grep reports the missing markers, which reads like a
finding about the quine check. Look at the size of
`build/output/quine-check/compiler.ir.log` before believing the message: a
real run is about 15 MB, and the failure that produced this note was 5 bytes.
Measured 2026-08-16 on a box with four agents gating at once; re-running the
phase alone against the same `Sut.cdx` scanned 4,709 defs and passed, with
nothing changed in between, and two later full gates were green. So it is a
contention flake, and the cheap repair when someone owns this script is for
the phase to distinguish an empty log from a real answer rather than
attributing it to the IR.

**Two source sites read as dead bindings and are NOT. Leave them alone.**
`X86_64Helpers.codex:1539` binds `st12c = emit-list-tail st12b` and the next
line continues from `st12b`; `X86_64Chapter.codex:450` binds
`st45j = patch-jcc-at st45i ...` and the next line continues from `st45i`.
Both are correct ONLY because `__record-set` mutates in place, and
`check-stale-reads` (`TypeChecker.codex:3114`) exists to warn about exactly
this shape. A tidy-up there is seed-affecting and would break the seed.

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
format's `*PROF:` lines into a hot-function histogram. **Compile it the
way the thing you are profiling was compiled and use the `<out>.map`
sidecar beside it.** That is now possible in both modes: until 2026-08-14
`compile.ps1` appended the `map` flag only when `-Repl` was ABSENT, so a
compiler build emitted no map at all and this line told you to mint one
NON-repl instead. It no longer does, and you no longer need to.

The coupling was in the driver, not the compiler. `emit-cdx` reads `repl`
and `map` as two independent flags, and `emit-binary-tail-with-map` writes
the same bytes as `emit-binary-tail` and then prints the `MAP:` lines --
**the map never enters the binary.** Measured 2026-08-14: `CDX repl` and
`CDX repl map` are byte-identical (84,607 both) and the second emits a
4,786-byte map. The symbol map is built into `EmitChapterResult`
regardless, so `map` only decides whether it is printed; it was being
computed and thrown away on every compiler build.

**How much a mismatched map actually costs, since the docs did not say.**
Measured over the same 158-symbol program: the non-repl and repl maps
differ on exactly ONE line, `__start`'s SIZE (9784 against 9773), and
every symbol ADDRESS is identical. So a mode-mismatched map is not the
scary case. **The stale case is** -- `seed/Codex.map` describing an older
binary is what put a crash in `sorted-builtin-names` when the faulting
function was `find-effect-op-addr` (2026-07-16, an hour). Now that a
`-Repl` build emits `<out>.map`, `codex-vm`'s `-map` auto-probe finds the
right file first and never reaches the `seed/Codex.map` fallback.

**Re-concatenate before you mint, or the fresh map describes stale
source.** `build/output/Codex.codex` is written by `build.ps1`'s source
phase, so it is only as current as the last full build in THAT workspace.
Compiling it gives a genuinely fresh map of whatever the tree looked like
then, which is the worst of both: it carries no staleness marker and every
symbol in it is confident. Run
`build/concat-codex-self.ps1 -OutFile <path>` first and compile that.
Measured 2026-08-04 in a workspace whose last build was five days old: the
checked-in `build/output/Codex.codex` was 3007248 bytes against 2993576
from a fresh concatenation of the same tree.

Two things a fresh map is NOT. It is not a replacement for
`seed/Codex.map`: that file must describe the binary that actually ships,
and installing a map minted from a compiler source ahead of the seed makes
every backtrace it resolves wrong in a way nothing announces. And the
symbol-count delta between the two is a measure of how far compiler source
has moved ahead of the seed, not of anything being broken -- source landing
without a seed cycle is normal.

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

**From a symbol to the emitted bytes.** `compile.ps1` writes a `<out>.map`
sidecar of `0xADDR <size> <name>` rows, and the file offset of that address
inside the CDX is `addr - 0x100000 + 224` (load address, then header). Dump
with `[IO.File]::ReadAllBytes` and decode. Reading the bytes is faster than
another round of ablation once the conditions are narrowed: ablation tells
you which conditions matter, the bytes tell you what happened. One decoding
trap worth naming, because it misreads as a different register: REX.R and
REX.B extend the modrm fields, so `4c 8b 4d 90` is `mov r9,[rbp-0x70]` and
not `rcx`.

**Resolve a fault with the map belonging to the binary that faulted.** The
`-map` auto-probe falls back to `seed/Codex.map` whatever was booted, so a
boot-PE backtrace resolved against it is confident nonsense. The MAP1 map
embedded in the CDX is authoritative, and its names are CCE -- which is why
an ASCII grep of a `.cdx` for a symbol answers False for symbols that are
certainly present.

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

### Exit code 49374 (0xC0DE) is a HOST crash, not a guest one

`crash_filter` in `tools/codex-vm.c` catches a fault in codex-vm ITSELF,
prints `HOST CRASH:` to **stderr**, and exits 0xC0DE. The distinction
matters because a harness that only captures `-output` sees an empty
serial log and a nonzero exit and reads it as a guest that produced
nothing. **Redirect stderr or the diagnosis is invisible.**

**FIXED 2026-08-18 (root). A failed demand-commit was reported as a guest
fault, and the crash reporter then read the page it was describing.**
`guest_mem` is committed in 2 MB chunks on the guest's first touch; when
`VirtualAlloc(MEM_COMMIT)` or `WHvMapGpaRange` refused (the box's commit
charge: several agents' VMs, each committing whatever its guest touches of a 3 GB reservation), the run loop fell through
to the `Unmapped MMIO GPA=...` crash report as though the guest had walked
off the map, and `dbg_dump_mem` read the uncommitted GPA to print "Memory at
fault address" and killed the process: `HOST CRASH ... at 0x...7C35`. That
is the intermittent brotli-interop crash of 2026-08-16 (`Address: 0x70a00000
Access: WRITE` then `HOST CRASH`), reproduced by refusing the commit past
`0x70000000` in an ablation build: same two lines, same low address bits.
Now the failure prints `HOST: cannot back guest RAM at GPA 0x...:
VirtualAlloc(MEM_COMMIT) failed, err=1455 (ERROR_COMMITMENT_LIMIT) -- the
host is out of commit charge, this is not a guest fault` (or the
`WHvMapGpaRange` hr), the report's reason line says `Guest RAM commit
failed`, and every host read the reporter makes (`dbg_dump_mem`,
`dbg_backtrace`, `dbg_dump_stack`, `dbg_disasm_at`) asks `VirtualQuery`
first and prints `host page not committed` instead of dying; the backtrace
had a second instance one frame past `__start` (RBP chain ending at
`0xbe000000`). Same CL: `Code at RIP` had printed EMPTY in every report on a
3 GB guest because `(int)(guest_mem_size - addr)` went negative for any RIP
under 1 GB; the clamp now happens before the narrowing. If you see the
`HOST:` line, the fix is on the box (free memory or fewer concurrent VMs),
not in the guest.

**FIXED 2026-08-10 (blu). Setting a VBE mode activated the framebuffer at
runtime without committing the guest region the emulator reads back.** The
same defect as the oversized-disk crash below, other direction: that one
wrote past the commit, this one read out of it.

The startup path commits `0xBE000000..GOP_FB_ADDR+fb` only when
`gop_active` is ALREADY set, and the failing run's own log line says it is
not (`gop=0`), so the region stayed `MEM_RESERVE`. The Bochs VBE handler
then sets `gop_active` and `vbe_active` at runtime and allocates only the
HOST buffer. `sync_shadow_buffers` bounds its memcpy against
`guest_mem_size` -- `0xBF000000 + fb` is inside 3 GB, so the bound passes --
and reads reserved uncommitted address space. `guest_commit_range` is the
existing remedy and the GPU texture and asset upload paths already call it
from this same I/O handler; the VBE handler did not.

**The read runs on an exit counter (`exits % 64`), and that is what makes
this expensive to probe.** A guest that sets the mode and returns exits
before the counter comes round and passes on a broken emulator: the first
probe written for this did exactly that, twice, and the second version
added 8 filler lines that bought 8 exits because serial is ring-buffered
rather than a port write per byte. `codex/test/vbe-mode-set` calls
`vbe-read` 200 times for the port pairs, and sums every value into its
answer so nothing can elide them. Measured both arms on one binary: depot
`codex-vm.exe#92` faults the host (exit 49374, no serial), the fixed build
prints `1 2 0 204800 1024` at 612 exits.

**This test is easy to disarm silently.** It once took its exits from
drawing, and when `gfx-put-pixel` stopped
banking (below), the exits fell from 1011 to 211, the pre-fix binary stopped
crashing, and the recorded output did not move a single byte -- a guard
reporting exactly what a working one reports. If you change what this test
draws, re-run the old binary before believing it still guards anything.

The original measurement, unchanged:

**Measured 2026-08-09 (blu): setting VBE mode crashes the
host.** `apps/browser/opening.codex` compiles clean (493057 bytes off seed
`A1EBA5A03016A128`) and dies immediately on boot:

```
VBE: mode set 1024x768 fb=0xfd000000
HOST CRASH: codex-vm faulted (code=0xC0000005) at 0x7FF7F24D5A0D reading memory 0x22715520000
```

Reproduced three times; the faulting code address is identical every run
and the host address it reads varies, which is what a per-run mapping base
does. One byte of guest serial appears before the fault, so the guest never
reaches its first `print-line`.

The log line above it is the lead: `RAM cap: guest_mem_size=0xc0000000
effective=0xbe000000 gop=0`, and the VBE framebuffer is handed out at
`0xfd000000`, which is above `effective`. **Raising `-mem` does not raise
`effective`** -- at `-mem 4096` the line reads `guest_mem_size=0x100000000
effective=0xbe000000` and the fault is unchanged, so the cap is not derived
from the requested size. That refutes "give it more RAM" as a fix and
points at the framebuffer mapping rather than at the guest.

Whatever the guest did, a guest cannot be allowed to fault the emulator:
the correct behaviour is a guest fault, not a host access violation.

`codex/test/apps/browser-keys` passes and is not a counter-example: it
never sets a video mode, so the whole display path is outside what the
browser's gate observes. That is why the browser reads green and does not
run.

**FIXED 2026-08-10 (blu), main 14494. A disk image larger than 31 MB
overran the pre-committed guest region.** Measured by reek, kept in full
below because the measurement is what made it findable in one sitting.

`guest_mem` is `MEM_RESERVE` with only the **first 32 MB committed** up
front (`codex-vm.c:8997`), and `load_kernel`'s raw path memcpys the WHOLE
file to `guest_mem + LOAD_ADDR` (`0x100000`) bounded against
`guest_mem_size` -- 3 GB -- rather than against what is committed. A 32 MB
image therefore writes 1 MB..33 MB and walks off the end of the commit into
reserved address space. `guest_commit_range` exists for exactly this and its
own comment describes the symptom ("a direct memcpy out of the same region
does crash, and takes the VM process with it"); `load_kernel` was not among
its callers. It now commits first.

**The threshold is where the mechanism says it is, which is how it was
pinned rather than argued.** `LOAD_ADDR + size` of exactly 32 MB
(32,505,856-byte image) boots; one 64 KB step past it faults. Both crashing
sizes boot after the fix and the 30 MB control is unchanged.

Two corrections to the notes below, both worth keeping. **The absence of
both GPT lines was not a clue about the directory walk** -- the fault is
upstream of the walk entirely, in a copy that runs before it, so neither
line could have printed and buffering was never involved. And the
unbounded `memcpy` at the extracted-PE copy is **real but is not this
crash**: it is bounded against the image and not against guest RAM, so it
would need a `BOOTX64.EFI` larger than guest RAM, while the crash needs
only a 32 MB disk. It is bounded and committed now as well.

The original measurement, unchanged:

**CLOSED. Re-verified 2026-08-10 (reek) against the fix above**: a 32 MB
image (33,554,432 bytes, 65536 sectors) built from the same source now boots
and runs to completion under `-uefi -disk`, with no host fault. The entry
below is the original measurement, kept because it is what made the cause
findable in one sitting. It said the cause was NOT pinned, and that was
true when written; it is pinned now and the account is above.

The `-timeout 60` below is a transcript of what was typed, not a recipe:
that flag does not exist and was silently ignored, exactly as the section
above records. **Do not copy this line.** To bound a run, use
`-screenshot` with `-screenshot-delay`, which exits after the capture.

```
tools/codex-vm.exe -kernel X.img -uefi -disk X.img -headless -timeout 60
IDE: X.img (33554432 bytes, 65536 sectors)
UEFI mode / ACPI / SMBIOS lines, then:
HOST CRASH: codex-vm faulted (code=0xC0000005) at 0x7FF62F685F9E writing memory 0x227397B0000
```

The control is the same payload and the same source in a **32768**-sector
image, built by the same `build/build-img.ps1` invocation with only
`-TotalSectors` changed: it boots, prints
`GPT: extracted BOOTX64.EFI (2591232 bytes, FAT16) from partition at LBA 2048`,
and compiles. So the variable is image size, not the payload.

Two facts to start from rather than re-measure. The failing run prints
neither the `GPT: extracted` line nor the `GPT: ESP ... has an unusable BPB`
line that is its else-branch, which either puts the fault inside the
directory walk or means those `fprintf(stderr, ...)` calls are lost to
buffering while the SEH handler's own writes survive -- **establish which
before reasoning from the absence**, because they imply different faults.
And the fault is a WRITE, while every access in the walk itself is a read.

Not on the A5 flight path: `a5flight2.img` and the 2.7 MB source arm are
both 32768 sectors. It bites whoever first needs an image above 16 MB,
which the drive installer will.

**One thing this fix does NOT change, recorded so nobody reads it as
covered.** A GPT disk image under `-uefi` is copied into guest RAM twice:
once whole by the raw path above, then again as the extracted PE. The first
copy is discarded and now commits the whole image, so a multi-gigabyte ESP
will commit multiple gigabytes of host RAM before booting. That is waste,
not a crash, and skipping the raw copy for a GPT image is a separate change
with its own fallback question.

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

### BOOTING `build/boot/diag.img` AS ITS OWN DISK REWRITES THE DEPOT ARTIFACT

`codex-vm -kernel build\boot\diag.img -disk build\boot\diag.img` looks like the
obvious way to try a diagnostic image by hand. It is not: the ladder BANKS, and
banking means the payload writes `DIAG.TXT` onto the ESP of whatever medium it
was given. Point `-disk` at the depot file and the depot file is what it edits.
Measured 2026-08-20 (blu): one probe boot moved `diag.img` from `A6A00B79` to
`0D69C8FC`, and the next `diag-arm.ps1` run duly reported the mutated image as
its subject.

**Boot a COPY.** That is what `diag-arm.ps1` does for every arm it runs
(`New-Copy` / `New-Variant`), and the reason is this and not tidiness. The
repair when it happens is `build/boot/build-diag.ps1`, which is deterministic:
the same sources give the same hash back, so a rebuild returns the artifact
rather than merely a new one.

Two ways this bites beyond the wasted rebuild. A CL that carries the mutated
image ships an artifact with somebody's test run baked into it, and the hash in
the rehearsal record will not be the hash of the thing anyone else builds. And
`build/check-shipping-images.ps1` will NOT catch it, because it refuses a
`DIAG.CFG` and this writes a `DIAG.TXT`.

### A guest diagnostic has two channels, and only ONE of them is ever read

| | port | emitter | who reads it |
|---|---|---|---|
| COM1 | 0x3F8 data, 0x3FD LSR | `emit-serial-wait-and-send` | `codex-vm -output`, so `test-run.ps1`, so every `.expected` |
| COM2 | 0x2F8 data, 0x2FD LSR | `emit-control-wait-and-send` | the compile protocol only. **NOT captured to `-output`** |

Both are in `codex/compiler/Emit/X86_64IO.codex`. **A diagnostic written to COM2
is emitted and discarded**, so the code under it looks like code that never
runs. `__out_of_memory` printed there for its whole life (fixed main 11837) and
a correct, firing stack guard read as "the guard never fires" for as long as it
did. Check which emitter a message goes through before concluding the path is
dead. `emit-control-wait-and-send` also carries a 10000-spin bail-out that the
serial one does not; the serial wait blocks until THR-empty.

### The halt pattern, and `cli` is not optional

Copy this (`emit-cpu-exception-dump`, `X86_64Boot.codex`); do not invent one:

```
emit-serial-drain-delay
cli
halt-pos = code-len
hlt
jmp (halt-pos - (code-len + 5))
```

**`hlt` with IF=1 is woken by the timer and the loop spins forever.** codex-vm
exits on `Guest halted with IF=0`, that never comes, and the harness kills the
run at its wall budget -- so a diagnostic that halted correctly is reported as a
hang, which is a different bug with a different owner. Any halt you add to a
guest path needs the `cli`.

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
Set-Location D:\Projects\Cobblestone-<agent>-main
[Environment]::CurrentDirectory = 'D:\Projects\Cobblestone-<agent>-main'
build/compile.ps1 -Src codex\test\apps\foo.codex -Out out.cdx -Log out.log -Kernel seed\Codex.cdx
```

**Detect:** hash the output. Two compiles of genuinely different source that
produce the same SHA-256 have not read your file. This is the cheap check and
it is the only one that fires before you start debugging the wrong program.

### `-IrUni` IS NOT THE WIRE. A plug consumes `-IrCce`, and since main 19558 they carry different IR

**Symptom:** you dump a program's IR with `-IrUni` because `-IrCce` is not
readable text, read the shape off it, and reason about what the plug does with
it. The reasoning is sound and the conclusion is wrong.

**Cause:** the two front doors run different pipelines.
`emit-ir-cce` (`codex/compiler/opening.codex:1707`) calls `lift-lambdas` at
`:1717` before pruning; `emit-ir-uni` (`:1683`) does not lift at all. That
second call landed at main 19558 (2026-08-25) to fix a wasm defect. So a
program with a lambda in it has TWO different IRs depending on which flag you
pass, and the plug only ever sees the lifted one: where `-IrUni` shows
`(lambda (params (param "x" ...)) ...)`, the wire carries
`(apply (name "__lam_0") (name <captured>))`, a partial application.

**Cost so far:** COMPILER-29's cause was recorded wrong twice, both times off
an `-IrUni` dump, and the second write-up named an innocent IR reader.

**Detect:** if a claim is about what a PLUG receives, do not settle it from
`-IrUni`. Force a marker constant into the emitted code from inside the arm you
believe runs, rebuild the plug, and read it off the target -- and put the same
marker in the arm you believe does NOT run, as the control. That pair is what
finally answered COMPILER-29; each round is a plug rebuild plus one filtered
`test-cross-batch.ps1` run, about ninety seconds.

### Run a plug's `build.ps1` FROM THE REPO ROOT, never from the plug directory

**Symptom:** `pwsh .\build.ps1` inside `codex\plugs\<p>\` exits 5 with
`FAIL: compile errors`, and prints a log full of `CDX3005` shadowing
warnings about `is-letter`, `is-digit` and `is-whitespace`. There are no
errors anywhere in it. The same bundle compiles clean by hand.

**Cause:** the message is wrong twice over. Something in the compile
resolves a path against the CURRENT WORKING DIRECTORY, so from anywhere but
the repo root the compile exits non-zero and nothing is compiled at all.
Re-measured 2026-08-20 from `C:\Users\Damian`: it is now the CITE that fails,
`error 3010: Unresolvable cite: Foreword chapter 'ListUtils' ... (expected
.\codex\foreword\core\ListUtils.codex)`, not the kernel -- the builder pins
`-Kernel` to `<repo>/seed/Codex.cdx` now. Same fix, different message, so do
not use the old text to decide whether this is your failure.
`Build-PlugCdx` (`codex\plugs\common\plug-build-lib.ps1`:148)
swallows that output with `| Out-Null`, sees a non-zero exit, reports it as
"compile errors", and then dumps the FIRST TEN LINES OF THE PREVIOUS RUN'S
`build.log`. Those warnings are stale and belong to a compile that succeeded.

**Fix:** run it from the repo root.

```powershell
cd D:\Projects\Cobblestone-<agent>
pwsh codex\plugs\csharp\build.ps1        # OK: ...\csharp-plug.cdx (371061 bytes)
```

**Detect:** the success line is `[<plug>-plug] OK: <path> (<n> bytes)`. If you
do not see it, nothing was written, whatever the log appears to say. Check the
log's timestamp against the run before believing a word of it.

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

**`-EscapeCheck` on the selfhost needs `-Decks 200`** or it dies in a silent
`#GP` in `copy-sx-pos`. The check allocates inside the walk, so it outgrows
the stock floors on a unit the size of the compiler. Not new, and it
reproduces on old seeds, so a fresh `#GP` there is not a regression you
introduced.

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

### The guest heap ceiling is 3040 MB, and `-mem` does not move it

Measured 2026-08-03. codex-vm caps the RAM size it reports to the guest at
`GPU_CMD_ADDR` (0xBE000000, 3040 MB) so the boot stack, which starts at the
reported size and grows down, cannot land in the GPU command buffer, the
depth buffer or the GOP framebuffer. Those three sit at fixed GPAs. The cap
binds unconditionally, so `-mem 8192` reports 3040 MB:

```
RAM cap: guest_mem_size=0x200000000 effective=0xbe000000 gop=0
```

**The consequence is that `compile.ps1`'s crash retry cannot recover
anything.** On an `!EXC` it prints `crash with 3072MB, retrying with 8192MB`
and runs a byte-identical machine. A whole-compiler `-IrCce` emit crashes
with the same RIP, the same interrupted RSP and the same heap frontier at
both sizes, four runs out of four. Do not read that retry as having ruled
memory out.

The failure looks like this, and the shape is worth knowing because the
crash site is innocent:

```
CRASH in __str_concat+0xF9 (general protection)
  callR 0xBDFFFA48   interrupted RSP
  R10   0xBE00367E   heap frontier, ABOVE the reported RAM top
  RBX   0x111C49161510180D   CCE text, restored from a clobbered stack slot
```

The heap frontier has passed the stack. Registers holding text rather than
pointers are the fingerprint: saved registers reloaded from stack slots the
heap has overwritten. Any `!EXC=0d` whose R10 is near 0xBE000000 is this and
not a codegen defect.

`-mem-nocap` reports the real size instead. It is opt-in, and it must stay
opt-in: the guest can turn GOP on after boot (UEFI GOP, VBE) by which time
the RAM size is long written, and an uncapped heap that grows past
0xBE000000 then overwrites the framebuffer it is about to scan out. Pass it
only for a run that draws nothing. `compile.ps1 -MemNoCap` forwards it, and
`-TimeoutSec` raises the 600 s VM kill for the one job that outruns it.

**Above ~3 GB nothing helps, because the address space is baked into the
binary.** `bare-metal-ram-size` in `codex/compiler/Emit/X86_64State.codex`
is the constant 3221225472, and the page tables the seed emits map exactly
that much RAM plus one device gigabyte above it. Raising the guest ceiling
past 3 GB is a change to that constant, which is seed-affecting.

### The UEFI identity map tracks `-mem`

codex-vm identity-maps one 2 MB-page PD per GB of guest RAM, minimum 4 GB and
maximum 64 GB, and CR3 points at it before the guest builds its own tables.
It tracks `-mem` because GetMemoryMap advertises conventional RAM at and
above 0x100000000 whenever `-mem` exceeds 4 GB, and `AllocateAnyPages`
allocates top-down from `guest_mem_size - 1 MB`: a guest that believes
either one touches memory the map has to cover.

**A UEFI fault of that shape is the bed, not the payload.** Earlier builds
pinned this at a fixed 4 GB, and before that 2 GB, so the emulator advertised
memory it did not map: at `-mem 8192` it returned 0x1f7f00000 from its own
allocator and then triple-faulted the guest on that address. An older account
calling a UEFI run above `-mem 4096` meaningless is describing that, and it
does not hold now.

Past 64 GB the map stops growing and says so on stderr, naming the address
above which memory is advertised and not mapped. It does not fault silently.
### A BOOT IMAGE RUNS IN 128 MB. THE BED GIVES IT ~3 GB.

**Any memory claim about a flight artifact measured in codex-vm at the
default `-mem` is measured against an arena roughly 24x too large.** The
two paths do not share a memory model:

| Path | Arena |
|---|---|
| `build/boot/build-option-a.ps1` (the boot image, what flies) | ONE region of `HeapPages` * 4096 bytes, heap at its base (`r10`) and stack at its top (RSP), every prologue checking `cmp rsp, r10`. `-AllocPages 32768` is the shipping value: **128 MB for heap and stack together** |
| `build/desk.ps1`, any `codex-vm -kernel *.cdx` | the bare-metal model above: heap 6 MB up to `-mem`, default 3072 |

`-AllocPages` is `cdx-to-pe.ps1`'s `-HeapPages` under another name; the
name is kept because probe commands in `docs/Hardware/HardwareSitting.md` pass it.

**A payload that hosts a VMX guest needs a bigger one, and 131072 (512 MB) is
measured.** `vm-prepare-guest` allocates the guest's region out of this same
arena, so 32768 cannot carry a 256 MB guest and `vm-guest-sizing` refuses every
time. Measured 2026-08-15 under OVMF: an Option A image built
`-AllocPages 131072 -Ebs -Uefi` boots on real edk2 firmware in a 2048 MB
machine and paints the desk. The control is the same image in a 256 MB
machine, which never reaches the paint at all -- one flat colour against five
-- so the bed can say no and the green means something. **What this does NOT
establish is the ASUS**: OVMF choosing to satisfy a 512 MB `AllocateMaxAddress`
below the aperture is exactly the class of spec freedom L-FREEDOM is about, and
AMI is free to answer differently. The stub raises `H` on a failed heap
allocation, so a board that refuses says so rather than wandering.

**`-AllocPages` above 524287 crashes `cdx-to-pe.ps1` rather than building.**
`$ebsBackoff = [Math]::Max(0, 0x10000000 - ($HeapPages * 4096)) + 0x100000`
picks the Int32 overload from the literal `0`, and past a 2 GB arena the
subtraction no longer fits: `Cannot convert argument "val2", with value:
"-2952790016"`. It fails loudly at build time and ships nothing wrong, and
`$HeapCeiling` is 3 GB anyway, so the reachable band is 512 MB to 2 GB. `0L`
would lift it if anyone ever needs more.

This is what hid the A6 F12 regression for a week. Six bed arms across two
agents could not express a 4.6 MB per-visit leak, and the post-mortem
concluded the remaining space was real hardware behavior the emulator does
not model. It was the heap.

**You cannot shrink the bed to match with `-mem`.** codex-vm's GOP
framebuffer sits at GPA 0xBF000000 (3056 MB), so a small `-mem` leaves it
outside RAM and the guest dies in `gop-rect-rows` (measured at 256 MB).
OVMF or metal are the only routes to the real arena.

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

Since 2026-08-05 the standing probe telemetry channel is GopShot's F12
screenshot-to-stick: probe-halt was replaced by shot-wait, and the frame
lands on the ESP as `SHhhmmss.BMP` behind the medium-select `CODEX.CDX`
lock. QR plus a photograph remains the honest fallback when the disk
stack is itself the subsystem under test, or below rung 6, where the
shot cannot land through a broken disk path. This section is that
fallback.

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

**When you add a probe that tests the disk stack or runs below rung 6,
render its findings as QR;** everywhere else, let it screenshot itself
via F12. A number that only a human can read off a monitor is a number
that arrives wrong, arrives late, or -- as happened -- dies with the
framebuffer at power-off, having cost a person a walk across the
building and a flash cycle to obtain.

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
build/test.ps1 -CodexCdx build/output/poison-seed.cdx -Tier all -Jobs 4 -ApprovedBy damian

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

## What each build COSTS, so a hang is visible before it is expensive

**Read this before you wait on anything.** The failure it exists to stop is
not slowness, it is mistaking a hang for slowness: twice on 2026-08-31
`build-page-modules.ps1` sat silent on one row for 25 minutes, and both times
the honest-sounding reading ("that is the slow row") was free, available
immediately, and wrong. A number to compare against is what makes the
difference between a diagnosis and a nap.

Measured 2026-08-31 on the 15.8 GiB box, on a codegen change implicating nine
regression phases. **Re-measure before quoting (L-COUNT); none of these has
ever gone down on its own.**

| step | cost | notes |
|---|---|---|
| `build/build.ps1 -Internal`, codegen change | **950 to 1,065 s** | three runs: 953.5, 1,016.2, 1,062.6 |
| `build/build.ps1 -Internal`, nothing implicated | ~186 s | the figure `CLAUDE.md` carries; not re-measured today |
| `codex/plugs/wasm/build-page.ps1` | **~13 min** | 01:35 to 01:47 measured; includes the compiler WAT and 69 examples |
| `build-page-modules.ps1`, all 51 rows, serial | ~11 min | the pre-parallel figure |
| `build-page-modules.ps1`, 3 rows at `-Jobs 4` | **117 s** | against ~235 s serial for the same three |
| `codex/plugs/wasm/build.ps1` (the wasm plug) | ~90 s | 5,704 lines bundled to a 388,861-byte CDX |
| the compiler as a hosted-windows `.exe`, self-compile | **21 s** | 3 MB in, `SIZE:3087504` out |

The gate's own phase breakdown, which is where the time actually goes and
which phase to suspect when a total moves:

| phase | s | | phase | s |
|---|---|---|---|---|
| `test-compile` (1,484 chapters) | **431.6** | | `plug-smoke` | 42.4 |
| `gen-scripts` | 113.0 | | `test-bvt` | 41.1 |
| `deck-headroom` | 76.8 | | `app-sweep` | 30.5 |
| `jonquil` | 68.2 | | `cdx-build` | 22.5 |
| `plug-binary` | 46.4 | | `cdx-stage1` | 21.5 |
| `check-errors` | 43.3 | | `cross-smoke` | 18.8 |

**A step past roughly twice its number here, printing nothing, is a hang until
proven otherwise, and one command settles it:**

```powershell
Get-Process codex-vm,wat2wasm,pwsh -ErrorAction SilentlyContinue |
    Select-Object Name,Id,@{n='CPU';e={[int]$_.CPU}},@{n='RAM(MB)';e={[int]($_.WorkingSet64/1MB)}},StartTime
```

A live build has a `codex-vm` or a `wat2wasm` burning CPU. **A step whose
owning shell has accumulated about one second of CPU over twenty-five minutes,
with no child process, is stopped, not working** -- and in PowerShell that is
also the signature of a headless parameter prompt, which is why `compile.ps1`
requires `-Log`. Check the artifact's timestamp too: an output file still
carrying its previous mtime while the step "runs" is the same tell.

**`-Jobs 4`, RE-RULED by Damian 2026-08-27, superseding the 2026-08-02
`-Jobs 8` standard, release runs included.** Not the dead XMP workaround
returning: a different condition, measured and named. The box holds 15.8 GiB
and 8 slots of 3072 MB guests overcommit it, killing guests with a moving
culprit that reads as codegen ("The compile batch asks for 12 GB of guest
RAM, and a short box reports it as a CODEGEN failure", below -- **and read
"asks for" literally: that number is REQUEST arithmetic, slots times the
`-mem 3072` ceiling, never a measured appetite.** `-mem` is a ceiling, not a
commitment; measured 2026-08-28, a self-compile guest peaks ~1.1 GB and a
compile-BATCH guest scales linearly with its batch, 234 MB at 60 chapters to
2,134 MB at a 416-chapter slot. On 2026-08-28 this sentence was quoted as
consumption and a day's regression hunt rode on it before the differential
came back FLAT; a request figure cited as an appetite is the false report
that hunt was chasing). The 2026-08-02
raise was right on its own evidence (977 s of compile phase at 4 slots was
the XMP-era workaround outliving its condition); this lowering carries its
condition with it for the same reason -- when the box grows RAM, re-measure
and re-raise. The crash-shaped contention classes are still caught by the
retry paths (`Invoke-StandaloneRetry` here, the sweep's no-diagnostic
re-runs), which is why 4 is enough width to keep.

**A Renode instance is a VM load under this ruling, and its peak is
invisible to a spot check.** Measured 2026-08-28 during the riscv cross bed:
four concurrent instances read 268 MB at their largest in a one-shot sample
while Damian watched the same processes BOUNCE TO ~1 GB EACH at peak, so a
Renode battery is transiently ~4 GB beside whatever gates are running --
four gate slots of 3072 MB plus four Renode peaks is the whole box, and
that day it read as four killed gates and a "qemu produced nothing" with a
different plausible culprit each time. Count running Renode instances
against the box before launching a gate, and run Renode beds at `-Jobs 4`
or serial like every other parallel harness.

**A bare `build/test.ps1` is the `lang` tier, not the full battery.** The
command above says `-Tier all` on purpose. A poison run over `lang` proves
`lang`, and the apps, forewords and lib tiers allocate shapes it never
reaches.

**Do not expect a specific pass count here.** The `lang` tier
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
2. **Refresh `seed/Codex.map`, and take it from the build.** The gate
   already produces the right one. `compile.ps1` appends `map` to every CDX
   mode, `-Repl` included (`compile.ps1`:139-141), and `Invoke-BuildCdx`
   carries the sidecar with the binary it describes, so a gate run leaves
   `build/output/Sut.map` beside `build/output/Sut.cdx`. Install that:

   ```powershell
   Copy-Item -Force build/output/Sut.map seed/Codex.map
   ```

   It is the shipping compiler's own map rather than a re-derivation.
   Measured 2026-08-15 against the seed's embedded MAP1: **5,192 of 5,192
   names, zero address differences, zero size differences.**

   **Nothing outside this step refreshes it, and the drift is total rather
   than partial.** The same measurement against the `seed/Codex.map` in the
   tree that day, minted at the previous release against seed `8D405FDF`:
   every name still resolved and **5,140 of 5,187 rows sat at the wrong
   address**. A stale map does not look stale. It looks like a map.

   So validate rather than assume, and validate the MAP and not the binary:
   read the seed's embedded MAP1 with `Get-Map1Symbols`
   (`build/vm-config.ps1`, whose `Addr` already carries the `0x100000` load
   base) and require every text-map row to match by name, address and size.
   That check fails loudly on the case that actually hurts, a map minted
   from compiler source that has moved ahead of the seed, which is
   confident nonsense that nothing else announces.

   The embedded MAP1 in each CDX is authoritative for crash reports
   regardless -- the text map only feeds `-Break` and `Resolve-Rip`.

## Host changes on the build box, 2026-09-01, with the undo for each

The box lost a DIMM (one 16 GB KSD516G72C34VTR at Controller1-DIMMB2,
15.8 GiB visible) and the replacement is weeks away, so Damian directed a
host-side purge of resident processes that were not the fleet's. Every
change below was made from an elevated shell he approved at the UAC prompt
(root, 2026-09-01, 09:06-09:16) unless marked per-user. Nothing here
touches the depot, the seed, or the build scripts; the build-side changes
of the same morning (`-Jobs 8`, the gate-RAM units) are in
`CoordinationProtocol.md` and `CurrentPlan.md`.

| # | what | how | measured effect | undo |
|---|---|---|---|---|
| 1 | NVIDIA App container service stopped and disabled | `Stop-Service NvContainerLocalSystem -Force; Set-Service NvContainerLocalSystem -StartupType Disabled` (elevated) | `NVIDIA Overlay` x5 (300 MB) and `nvcontainer` x4 (180 MB) gone and stay gone; the display driver service `NVDisplay.ContainerLocalSystem` untouched and running; CUDA and the driver unaffected. Lost: the NVIDIA App (overlay, recording, in-app driver updates) | `Set-Service NvContainerLocalSystem -StartupType Automatic; Start-Service NvContainerLocalSystem` |
| 2 | NVIDIA App self-update task disabled | `Disable-ScheduledTask -TaskName 'NVIDIA App SelfUpdate_{B2FE1952-0186-46C3-BAEC-A80AA35AC5B8}'` (elevated) | keeps 1 from being re-enabled by the updater | `Enable-ScheduledTask -TaskName 'NVIDIA App SelfUpdate_{B2FE1952-0186-46C3-BAEC-A80AA35AC5B8}'` |
| 3 | Start-menu web suggestions off by policy | `DisableSearchBoxSuggestions` = DWORD 1 under both `HKCU\Software\Policies\Microsoft\Windows\Explorer` and `HKLM\Software\Policies\Microsoft\Windows\Explorer` (elevated) | NONE on its own: `SearchHost` still spawned six `msedgewebview2` (351-366 MB) after `gpupdate /force` and two Explorer restarts. Kept because it is the documented switch and costs nothing | delete the value from both keys |
| 4 | Bing search and Cortana consent off (per-user) | `BingSearchEnabled` = 0, `CortanaConsent` = 0 under `HKCU\Software\Microsoft\Windows\CurrentVersion\Search` | none measured on the web views | delete both values |
| 5 | Search highlights and cloud search off (per-user) | `IsDynamicSearchBoxEnabled` = 0, `IsMSACloudSearchEnabled` = 0, `IsAADCloudSearchEnabled` = 0 under `HKCU\Software\Microsoft\Windows\CurrentVersion\SearchSettings` | none measured on the web views | delete the three values |
| 6 | The `SearchWebView2` feature flag (id 37926450) overridden off | key `HKLM\SYSTEM\CurrentControlSet\Control\FeatureManagement\Overrides\8\1694661260` with `EnabledState`=1, `EnabledStateOptions`=0, `Variant`=0, `VariantPayload`=0, `VariantPayloadKind`=0 (elevated). Reported working on 26200 builds, this box's build | WORKS. Measured 2026-09-01 10:33, four minutes after the reboot (booted 10:29:23), before any session had used the Start menu: `SearchHost` running (1 process), `msedgewebview2` count 0. The pre-reboot count under rows 3-5 alone was six. Re-count after a day's Start-menu use before calling the ~350 MB durable (L-GREEN): `(Get-Process msedgewebview2 -ErrorAction SilentlyContinue \| Measure-Object).Count` | delete the key `...\Overrides\8\1694661260` and reboot |
| 7 | Explorer restarted | Windows restarted it itself at 09:09:54 and 09:11:37 after `SearchHost` was killed; a deliberate `Stop-Process explorer` at ~09:14 | the repaint Damian saw; no memory effect | none needed |
| 8 | Logitech Options+ updater service stopped and disabled (second pass, 10:36-10:45, Damian's direction after the reboot) | `Stop-Service OptionsPlusUpdaterService -Force; Set-Service OptionsPlusUpdaterService -StartupType Disabled` (elevated) | `logioptionsplus_updater` (47 MB) and `logioptionsplus_appbroker` (16 MB) gone. The mouse is plain HID and keeps working; Options+ profiles do not apply while the agent is down, and the agent was not running before this either | `Set-Service OptionsPlusUpdaterService -StartupType Automatic; Start-Service OptionsPlusUpdaterService` |
| 9 | Logitech Download Assistant logon entry removed | `Remove-ItemProperty -Path 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'Logitech Download Assistant'` (elevated); the value was `C:\Windows\system32\rundll32.exe C:\Windows\System32\LogiLDA.dll,LogiFetch` | no logon-time driver fetch | `New-ItemProperty -Path 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'Logitech Download Assistant' -PropertyType String -Value 'C:\Windows\system32\rundll32.exe C:\Windows\System32\LogiLDA.dll,LogiFetch'` |
| 10 | Phone Link uninstalled (per-user) | `Get-AppxPackage Microsoft.YourPhone \| Remove-AppxPackage` (was 1.26071.164.0). It has no startup-approved entry to flip: it self-activates as a background COM server through CDP, so uninstall is the only durable stop | `PhoneExperienceHost` (162 MB) gone | Microsoft Store, `ms-windows-store://pdp/?productid=9NMPJ99VJBWV`; needs row 12 undone first, the Store downloads through DoSvc |
| 11 | Connected User Experiences and Telemetry disabled | `Stop-Service DiagTrack -Force; Set-Service DiagTrack -StartupType Disabled` (elevated) | 63 MB gone | `Set-Service DiagTrack -StartupType Automatic; Start-Service DiagTrack` |
| 12 | Delivery Optimization disabled | `Set-Service DoSvc -StartupType Disabled` is REFUSED under an elevated shell (`Access is denied`, the service's own ACL); the registry route works: `reg add HKLM\SYSTEM\CurrentControlSet\Services\DoSvc /v Start /t REG_DWORD /d 4 /f` (elevated), then `Stop-Service DoSvc` | 27 MB gone. SIDE EFFECT: DoSvc is the download engine for Windows Update and the Store, not only the peer-to-peer half, so Store installs and some updates fail while it is disabled; re-enable it for those, then disable again | `reg add HKLM\SYSTEM\CurrentControlSet\Services\DoSvc /v Start /t REG_DWORD /d 2 /f; Start-Service DoSvc` |
| 13 | Widgets removed (per-user) | The policy route FAILED: `reg add HKLM\SOFTWARE\Policies\Microsoft\Dsh /v AllowNewsAndInterests /t REG_DWORD /d 0 /f` answers `Access is denied` from an elevated shell although `Get-Acl` shows Administrators FullControl and no Deny ACE on the chain; cause not found. Instead: `Get-AppxPackage MicrosoftWindows.Client.WebExperience \| Remove-AppxPackage` (was 526.21100.40.0) and `TaskbarDa`=0 under `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` | `WidgetBoard` (37 MB) and `WidgetService` (34 MB) gone; `WidgetsPlatformRuntime` stays installed and idle | Microsoft Store, `ms-windows-store://pdp/?productid=9MSSGKG348SP` (Windows Web Experience Pack, needs row 12 undone first); `TaskbarDa`=1 |

| 14 | Edge startup boost and background mode off | `reg add HKLM\SOFTWARE\Policies\Microsoft\Edge /v StartupBoostEnabled /t REG_DWORD /d 0 /f` and the same for `BackgroundModeEnabled` (elevated); `Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'MicrosoftEdgeAutoLaunch_B3417D684B4D5DAA4B9966F05669343B'` (per-user; the value was `msedge.exe --no-startup-window --win-session-start`); the resident instance killed | the headless `msedge` x5 (344 MB) and `identity_helper` gone; Edge opens normally on click. Both policy toggles show as managed in edge://settings/system | `reg delete HKLM\SOFTWARE\Policies\Microsoft\Edge /v StartupBoostEnabled /f` and the same for `BackgroundModeEnabled`; Edge re-creates the Run entry itself once startup boost is back on |

Measured for rows 8-13: ~390 MB of working set retired, and `Win32_OperatingSystem.FreePhysicalMemory` did NOT move (10.65 GiB before, 10.58 after, one session and no guests). The freed pages are on the standby list, which that counter does not credit; the gain is real for a guest that asks, not visible on the free line. After row 14, 10.76 GiB. Not taken: `CrossDeviceResume` (68 MB), `CDPSvc` + `CDPUserSvc` (66 MB, the transport row 10 and the Resume feature rode on), the `NVDisplay.Container` pair (203 MB, the display driver's own container, left for the control panel), and the Explorer-owned shell hosts `SearchHost`, `TextInputHost`, `StartMenuExperienceHost`, `ShellExperienceHost` (~650 MB resting): Explorer relaunches those within two seconds of a kill (measured on `TextInputHost`, back at 188 MB), there is no uninstall short of a shell replacement, and the memory manager trims them when a guest asks.

Not done, and why: the Windows Search indexer (`WSearch`, `SearchIndexer.exe`)
was left running at 21 MB, not worth its side effects; Defender's exclusion
list could not be read without admin and was not changed; Edge (2.9 GB at
07:00, 360 MB by 09:00) and Signal were Damian's own and he closed them.

Measured before and after, no guests running: 08:32 four sessions at 8.2 GiB
free before any of this; 09:06 after rows 1-2, 9.6 GiB free.
