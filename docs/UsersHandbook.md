# Codex User's Handbook

## VS Code Setup

The `tools\vscode` extension gives `.codex` files syntax highlighting,
bracket matching, and indentation rules in Visual Studio Code. It needs
no .NET, no compiler build, and no language server.

Rich editor features -- error squiggles, hover types, go-to-definition,
completion -- are **not available today**. See "What is not available"
below before you go looking for them.

### Prerequisites

**Visual Studio Code.** That is all.

Node.js is optional and only needed to re-compile the extension's
TypeScript or to package a `.vsix`. The compiled `out\extension.js` is
already in the depot, and the parts that do the work (the TextMate
grammar and the language configuration) are declarative JSON that VS
Code reads directly -- they never run any code.

**Do not install .NET for this.** Nothing in the working extension uses it.

### Install into VS Code

**Option A -- Development mode (quickest)**

1. `Ctrl+Shift+P` → **"Developer: Install Extension from Location..."**
2. Browse to `tools\vscode` inside the repo root → **Select Folder**.
3. Reload when prompted.

Open any `.codex` file. The status bar should read **Codex** and the
file should be colored.

**Option B -- Package and install**

Needs Node.js (LTS).

```powershell
cd tools\vscode
npm install
npm run compile
npm install -g @vscode/vsce
vsce package
```

Then `Ctrl+Shift+P` → **"Extensions: Install from VSIX..."** → select the `.vsix`.

### What works today

All of the following come from `syntaxes\codex.tmLanguage.json` and
`language-configuration.json`. No process is started; nothing can fail.

| Feature | Trigger |
|---------|---------|
| Syntax highlighting | Automatic on `.codex` files |
| -- chapter/section/foreword headers | `Chapter:`, `Section:`, `Foreword:` |
| -- prose declarations (`We say:`, `To …`) | Automatic |
| -- keywords, types, effect rows (`[Identity]`) | Automatic |
| -- strings (incl. `"""` triple), chars, hex (`#FF`) numbers | Automatic |
| -- annotations (`@name`) | Automatic |
| -- arrow/unicode operators (`->`, `→`, `⊢`, `∀`) | Automatic |
| Bracket matching and auto-close | Typing `(`, `[`, `{`, `"` |
| Surround-with-bracket on selection | Select, then type a bracket |
| Auto-indent after `act` / `then` / `else` / `in` / `for` / `=` | Newline |
| Codex word boundaries (kebab-case names stay whole) | Double-click, `Ctrl+←/→` |
| Comment/fold/select by indentation | VS Code defaults |

### What is not available

| Feature | Status |
|---------|--------|
| Error squiggles | **NOT AVAILABLE** |
| Hover types | **NOT AVAILABLE** |
| Go to definition / peek | **NOT AVAILABLE** |
| Completion | **NOT AVAILABLE** |
| Document outline | **NOT AVAILABLE** |

**Why.** Every one of these is a language-server feature. The only
Codex language server ever written was `src\Codex.Lsp`, part of the C#
reference compiler. That compiler is **permanently retired** -- it lives
under `old/` as historical record and is never built, invoked, or
edited. Its LSP went with it. A **Codex-native language server has not
been written yet**: there is no `.codex` implementation of the LSP
protocol anywhere in the tree.

The extension still carries the client half of that arrangement.
`src\extension.ts` unconditionally starts a `LanguageClient` on
activation, pointed at a `src\Codex.Lsp\Codex.Lsp.csproj` that no
longer exists at the repo root. Activation therefore fails and VS Code
may show a one-time error notification. **This is expected and
harmless.** Dismiss it. Grammar and language-configuration
contributions are declarative -- VS Code applies them whether or not the
extension's code activates -- so highlighting, brackets, and indentation
work regardless. The `codex.serverPath` setting is likewise vestigial:
there is no server for it to point at. Leave it empty.

**This gap is known and owned.** Rich editor support is tracked as open
work: `docs/PM/CurrentPlan.md` gap 7, "Editor and debugger maturity"
(syntax highlighting in GUI mode, F5 compile-and-run, watch
expressions, real debug info in the CDX). A Codex-native language
server is the missing piece for the VS Code half. New work items land
beside the work it belongs to. The capability is wanted; it has not been
rebuilt yet.

### Troubleshooting

- **No highlighting** -- check the file extension is `.codex` and the
  status bar language mode reads "Codex". If it reads "Plain Text",
  the extension is not installed or VS Code was not reloaded.
- **"Couldn't start client Codex Language Server" / "spawn dotnet ENOENT"**
  -- expected. There is no language server. Dismiss the notification;
  highlighting is unaffected. Do not install .NET, and do not try to
  build anything under `old/`.
- **No squiggles/hover/F12** -- not a fault. See "What is not available".

## UEFI Dev Console USB Boot

### Overview

`seed/Codex.img` is a 16 MB GPT disk image with **two** partitions:

| Partition | LBA | Size | Contents |
|---|---|---|---|
| EFI System (FAT16) | 2048..28638 | 13 MB | `EFI/BOOT/BOOTX64.EFI` (the dev console), `CODEX.CDX` (the seed), `SOURCE.SRC` (the concatenated source) |
| Codex Facts | 28639..32734 | 2 MB | the fact store |

Flash it to a USB stick and boot from UEFI.

**Taking it to a real machine?** Follow `docs/Hardware/HardwareSitting.md`. It is the
run sheet for release row R6: what to build, what to flash, the order the
boots go in, what a pass looks like at each rung, and what to bring home. A
sitting costs a human body, which is the scarcest device on the bus, and the
sheet exists so it is spent once.

The second partition is what lets a booted stick remember anything, and it
carries a type GUID nobody else uses,
`C0DE1A11-FAC7-4C0D-9E75-C0DEC0DE5EED`. The fact store addresses its sectors
relative to that partition and refuses to write to a disk that carries a
partition table without one -- because writing where it used to write ate
the partition table. A stick built before this carried one partition, so its
dev console reported `0 disk facts` and could never report anything else.

The sizing is an eighth of the medium off the top, capped at 128 MB and
floored at 1 MB, applied by `build/build-img.ps1` and by
`codex/plugs/img/GptWriter.codex` alike.

**Both inputs are unconditional, and the concat is generated when it is
missing.** That matters because the failure it replaces was silent:
`build-boot-img.ps1` once never passed `-Seed`, so `CODEX.CDX` reached no
image at all, and it passed `-Source` only when `build-output/Codex.codex`
happened to be left over from a previous build in that workspace. Whether a
stick carried its own source depended on scratch-directory state rather than
on the commit, and two runs of the same script at the same revision could
produce different images with neither saying so. A stick that cannot verify or rebuild
itself is not what is being promised here, so the claim belongs where
the artifact can be checked against it.

### First boot: the ceremony

The current bootable-stick payload is GopBoot, built by
`build/boot/build-option-a.ps1 -Src apps/works/GopBoot.codex -Kernel
seed/Codex.cdx -Ebs`. First boot runs the identity ceremony: it asks for
a passphrase, gathers entropy, and saves an Ed25519 identity to the
stick as `IDENTITY.DAT`; later boots unlock that identity instead of
minting a new one. The full screen-by-screen walkthrough with
screenshots is `docs/TailorsFitting.md`. It flew green on real hardware
2026-08-05.

### Building the image

```powershell
build/build-boot-img.ps1
```

Compiles `apps/works/UefiBoot.codex`, converts it to a PE, and hands the PE,
the seed and the source concat to `build/build-img.ps1`. Output:
`seed/Codex.img` (16 MB). Pass `-Out` to write somewhere else and leave the
depot artifact alone.

The run prints the layout it chose, so a build says what it made:

```
[build-img] ESP LBA 2048..28638 (13 MB)  facts LBA 28639..32734 (2 MB)
```

### Flashing

Two scripts exist. Use `flash-usb.ps1` -- it is more reliable, and its
`-Log` transcript is how you (or an agent session) read the elevated
window's result afterwards. Do not write wrapper scripts around it.

```powershell
# Recommended -- direct write, no Clear-Disk, logged
Start-Process pwsh -Verb RunAs -PassThru -ArgumentList '-NoProfile','-File',
  'D:\...\build\flash-usb.ps1','-Image','<full path to img>',
  '-DiskNumber','N','-SpecFit','-Force','-Log','D:\...\build-output\flash.log'

# Alternative -- zeros first, Clear-Disk, verify
Start-Process pwsh -Verb RunAs -ArgumentList '-NoProfile','-Command',
  'pwsh tools/write-usb.ps1 -Image seed/Codex.img -DiskNumber N; Read-Host "Done"'
```

If your problem is a DEVICE that will not work from a Codex boot (a
keyboard that types in BIOS but not here, a stick the firmware will
not list), start from
`docs/Designs/Active/Tools/HardwareBringUpPlaybook.md` -- the
diagnostic image, the method, and the worked example that got a dead
USB keyboard delivering.

Both require admin (UAC elevation). Find the disk number with
`Get-Disk | Where-Object { $_.BusType -eq 'USB' }`.

`-Image` is mandatory. Omit it and the script stops on PowerShell's
parameter prompt inside the elevated window, where you cannot see it.

`-SpecFit` refits the GPT to the stick actually in your hand: the
protective MBR spans the reported disk, the primary header's
`AlternateLBA`/`LastUsable` point at that disk's last sectors, and the
backup header and entry array are written there. Two things follow, and
both bite without it. Firmware that validates the backup GPT's position
(Dell) does not list the stick at all; and Windows GPT auto-repair
rewrites any disk whose `AlternateLBA` is not the last sector **on every
insertion**, so the stick is modified behind you between flash and boot.
Every patched sector is verified by readback. Prefer it over the older
`-FixupDir`, which needs Python.

### Known flashing issues

**Flashing is not reliably reproducible.** The same image, same
script, same stick, same procedure sometimes boots and sometimes
does not. Known contributing factors:

1. **`$stream.Flush()` vs `$stream.Flush($true)`.** The .NET
   `FileStream.Flush()` without `$true` only flushes the .NET
   buffer, not the OS write cache. Data may never reach the
   physical flash media. `Flush($true)` calls `FlushFileBuffers`
   and forces a sync to the device. Always use `Flush($true)`.

2. **`Clear-Disk` interference.** `write-usb.ps1` calls
   `Clear-Disk -RemoveData -RemoveOEM` before writing. This
   modifies the partition table and may trigger Windows disk
   management activity that races with the subsequent raw write.
   `flash-usb.ps1` skips this step and writes directly.

3. **`Set-Disk -IsOffline` with `FileShare.None`.** Taking the
   disk offline then opening with exclusive access causes
   "Access denied" on some configurations. Do not use this
   pattern. Use `FileShare.ReadWrite` (the default in both
   scripts).

4. **USB stick wear.** Repeated clear+write cycles on older
   sticks (e.g. Ativa 1GB) may degrade sectors. If flashing
   repeatedly fails on one stick, try a different one.

5. **Verify coverage.** The depot `write-usb.ps1` only verifies
   the first 4 KB of the written image. A full 8 MB verify is
   recommended to confirm the write landed.

**Workaround:** If a flash does not boot, pull the stick,
reinsert, and flash again with `flash-usb.ps1`. Two consecutive
flashes usually succeeds where one fails.

### UEFI firmware compatibility

Tested on:

| Board | Era | Status |
|-------|-----|--------|
| ASUS TUF (2015, Fallout 4 vintage) | 2015 | Works with AllocatePages status checks (CL 2019+) |
| QEMU + OVMF (edk2-x86_64-code.fd) | current | Reboot-loop FIXED 2026-07-29; now halts with OUT OF MEMORY -- see below |

**The reboot loop is fixed (2026-07-29). What it was.** The firmware
loaded and started the image, the payload came straight back, and BDS
retried forever: 21 triple faults in 40 seconds under a QEMU interrupt
log, nothing painted. Two defects in `build/cdx-to-pe.ps1`, both now
repaired:

1. **`AllocatePages` was asked for the wrong things and its status was
   discarded twice.** The code+rodata request was `AllocateAnyPages` and
   the returned buffer was *thrown away*, while the copy went to a
   hardcoded `0x100000` the app did not own. The heap request was
   `AllocateAddress` at a fixed `0x1000000`, **which edk2 refuses** -- it
   fails at 64 MB exactly as it fails at 512 MB, so it is the address
   that is unavailable, not the size. Now: `AllocateAddress` at
   `0x100000` for the code (the guest's text is linked absolute at
   `bare-metal-load-addr`, so no other address can work),
   `AllocateAnyPages` for the heap with the returned base actually used,
   and a halt-with-diagnostic on either failure.

2. **`ram-size-addr` (cell 4072) was never initialised, and that is what
   made the failure invisible.** Every panic path begins by relocating
   the stack to `[4072]`: `__out_of_memory`, `__watchdog_panic` and
   `emit-cpu-exception-dump` all do `mov rsp, [4072]`. Nothing in the
   compiler fills it -- `__start` only loads RSP from it
   (`X86_64Chapter.codex:376-377`) -- so on bare metal the harness writes
   it before the guest runs (`codex-vm.c:13576`, or `-device
   loader,addr=0xfe8` under QEMU). This stub did not, so the cell held zero, RSP
   became 0, and the handler's first push took `#PF` at `CR2 = -8`, then
   `#DF`, then a triple fault. **The guest was correctly detecting a
   stack/heap collision and calling the handler that exists to say so;
   the handler could not run.**

**Capture BOTH serial ports.** This is why the symptom was recorded as
"no output of ours on COM1". The firmware logs on COM1 (`0x3F8`), and
until main 11837 the guest's panic printer emitted on **COM2 (`0x2F8`)**
-- `__out_of_memory` used `emit-control-wait-and-send`, the compile
protocol channel, rather than `emit-serial-wait-and-send`, the program
channel. red fixed that at main and the panic now prints on COM1.

Attach both anyway. It costs a second `-serial file:...` and it is the
difference between a diagnosis and a dead machine: with one port
attached, a guest halted in a panic handler looks exactly like one that
never started. The general form, which is red's and is worth more than
the particular port: **if you add a diagnostic, check it reaches the
captured output, not merely that the code emits it.**

The control that localised it: the **Option A** payloads
(`build/boot/build-option-a.ps1`) boot ONCE on the same firmware and
paint. So the box, the firmware and the disk plumbing were never at
fault.

3. **`r10` was loaded before a firmware call that is allowed to destroy
   it.** `r10` is the deck pointer for all generated Codex -- every
   non-leaf prologue opens `cmp rsp, r10; jb __out_of_memory` -- and it
   is caller-saved in the Microsoft x64 ABI. The stub set it before
   calling `ClearScreen`, and OVMF clobbers it, so `opening` was entered
   with a firmware pointer in `r10` and tripped the guard in its own
   prologue. It is now reloaded from `deck-pos-addr` after the last
   firmware call.

   **The `OUT OF MEMORY` this produced was a false report.** The heap was
   never touched. What identified it was the stack depth: RSP at the trip
   was exactly 48 bytes below the top -- one return address and five
   callee-saved pushes -- so nothing had run and nothing had allocated.
   A panic message is evidence about the panic path, not about the
   condition it names.

4. **`SYSCALL` was given selectors the firmware GDT defines backwards.**
   `SYSCALL` reads no descriptor: it sets `CS = STAR[47:32]` and
   `SS = STAR[47:32] + 8` and forces flat 64-bit CPL-0 attributes onto
   the hidden halves. With `STAR = (8 << 32)` the guest got `CS = 0x08`
   and `SS = 0x10`. Dumped from a running OVMF guest, that GDT is:

   | sel | type | what it actually is |
   |---|---|---|
   | 0x08 | 0x2 | **DATA**, 32-bit |
   | 0x10 | 0xf | **CODE**, 32-bit |
   | 0x30 | 0x3 | DATA, flat |
   | 0x38 | 0xa, L=1 | CODE, 64-bit |

   So CS was a data selector and SS a code selector, exactly inverted.
   Execution continued because the hidden halves are forced, and the
   selector values persisted after the handler returned; the first
   firmware `IRET` afterwards validated CS and refused `0x08`. That is
   the `#GP` with `ExceptionData = 0x0008` OVMF reported from `CpuDxe`.
   **There is no consecutive (code64, data) pair anywhere in that GDT**,
   so no value of `STAR` fixes it. The stub now copies the firmware's GDT
   into a page it owns, appends the *current* CS and SS descriptors as a
   consecutive pair at `0x40`/`0x48`, loads it, and points `STAR` there.
   Every firmware selector keeps its meaning because the copy is
   byte-identical, and the appended pair are the firmware's own
   descriptors rather than invented ones.

**With all four fixes the dev console runs on real UEFI firmware.** It
boots once, indexes, and paints a live header:

```
Codex Dev Console
Indexed 95 definitions, 0 disk facts | Codex Dev Console | 2026-07-29 ...
```

**The ConOut gap is still real for DevConsoleBoot as an alternate
payload** (WORKS-5 in `apps/works/works-backlog.md` records the black
screen and OUT OF MEMORY under OVMF), **but it no longer blocks any
sitting:** the 2026-08-05 flights flew GopBoot painting GOP directly,
and Dev Console left the interface menu the same day. The dev console's
output goes to COM1 and nowhere else.
`uefi-con-put-text` sets attribute and cursor through real UEFI traps and
then writes the text with `print-uni`, which lowers to `__serial_put` --
staged into codex-vm's blit cell, or `out` to COM1. **There is no ConOut
path**, so on real firmware the characters never reach the UEFI text
buffer and a person in front of the machine sees nothing. codex-vm
renders the console itself, which is why this never showed there.

Second thing to fix while in there: the console repaints continuously.
The run above emitted **29 MB of serial in 60 seconds**.

It booted "correctly" under codex-vm `-uefi` throughout, so **codex-vm's
default UEFI emulation is more permissive than edk2**; a green codex-vm
boot is not evidence about firmware. Use **`-uefi-strict`**, which models
the memory map and reproduces the fixed-address failure locally without
QEMU:

```
tools/codex-vm.exe -kernel <image.img> -uefi-strict -headless -mem 2048
```

On the pre-fix stub it prints
`UEFI-strict: AllocateAddress(0x1000000, 131072 pages) -> EFI_NOT_FOUND`
and then names the crash "the fixed-address boot bug".

**Attach a VARSTORE or you will misread this.** With only the code half,
the firmware has nowhere to keep a boot entry, never attempts the image,
and sits on its own splash -- which looks like "never left firmware"
rather than a loop:

```powershell
# both paths must be space-free: Start-Process splits ArgumentList on spaces
-drive if=pflash,format=raw,unit=0,readonly=on,file=<edk2-x86_64-code.fd copy>
-drive if=pflash,format=raw,unit=1,file=<edk2-i386-vars.fd copy, writable>
-drive format=raw,file=<disk copy>  -m 2048 -display none
-serial file:com1.log  -qmp tcp:127.0.0.1:PORT,server=on,wait=off
```

`com1.log` is the instrument that named this, not the screen.

**A non-black pixel count is not a screenshot, and around 2784 it is
OVMF's own TianoCore splash.** That number was once read as our console
rendering and once as our console failing, and on both occasions the guest
had not reached our code at all. Convert the frame and look at it
(`build/boot/ppm2png.ps1`) before believing any count. Two further traps
from the same instrument: identical counts across a series can mean the
screen is frozen rather than stable, so **hash the frames and require them
to DIFFER** when something on screen should be moving (a clock is ideal);
and a lit screen says nothing about input, which needs a `send-key` over
QMP and a frame that changes in response.

**AllocatePages failure.** The PE stub calls
`AllocatePages(AllocateAddress, EfiLoaderCode, N, &0x100000)` for the
code segment. On some boards the allocation fails, and if the stub
continues it copies code to unallocated memory and corrupts firmware.
CL 2019 added `test rax, rax; jz ok; hlt` after each `AllocatePages`
call for exactly that reason.

**Those checks were not present between then and 2026-07-29**, and this
section asserted they were. The stub in the tree had drifted to
`AllocateAnyPages` with the returned buffer discarded and no status test
on either call, which is how the reboot loop above happened. They are
back, and they now emit a byte before halting so a halt can be told from
a hang: `C` for the code allocation, `H` for the heap, on **both** COM1
and COM2. If the stub halts immediately on boot, capture serial and read
which letter came out.

**Larger binaries may fail on constrained boards.** As the UEFI
app grows (more menus, features, source), the PE binary gets
larger. Each section-alignment boundary crossing (4096 bytes)
changes the PE layout. On boards with tight memory below 1 MB,
larger binaries may fail to allocate even with the status checks.

### Source browsing

`UefiBoot.codex` reads `SOURCE.SRC` from the FAT16 partition at
boot via `fat16-read-text`. This uses the UEFI Block I/O protocol
via `LocateProtocol`, which finds the **first** Block I/O
instance -- not necessarily the boot disk.

| Platform | Source loading |
|----------|--------------|
| ASUS TUF (real hardware) | Untested -- may work if USB is first Block I/O |
| QEMU + OVMF | Fails -- first Block I/O is NVRAM flash, not boot disk |

The dev console shows "Indexed 0 defs, 0 chapters" when source
loading fails. The menu, system info, clock, and navigation still
work. Browse Source shows an empty list.

**Fix needed:** Use UEFI Simple File System Protocol or Loaded
Image Protocol to find the correct boot device, instead of
`LocateProtocol` with Block I/O which returns the wrong handle.

### QEMU testing

Run from the repo root. QEMU needs an absolute path for `-drive`, so
resolve it rather than hardcoding a workspace.

```powershell
Copy-Item seed\Codex.img build-output\boot-test.img -Force
Set-ItemProperty build-output\boot-test.img -Name IsReadOnly -Value $false
$img = (Resolve-Path build-output\boot-test.img).Path
& 'D:\Program Files\qemu\qemu-system-x86_64.exe' `
  -accel tcg `
  -drive 'if=pflash,format=raw,readonly=on,file=D:\Program Files\qemu\share\edk2-x86_64-code.fd' `
  -drive "format=raw,file=$img" `
  -m 2048
```

Use `-accel tcg` (software emulation). WHPX adds guest pressure
to the Windows hypervisor and can cause host instability when
combined with codex-vm instances.
