# Codex User's Handbook

## VS Code Setup

The `tools\vscode` extension gives `.codex` files syntax highlighting,
bracket matching, and indentation rules in Visual Studio Code. It needs
no .NET, no compiler build, and no language server.

Rich editor features — error squiggles, hover types, go-to-definition,
completion — are **not available today**. See "What is not available"
below before you go looking for them.

### Prerequisites

**Visual Studio Code.** That is all.

Node.js is optional and only needed to re-compile the extension's
TypeScript or to package a `.vsix`. The compiled `out\extension.js` is
already in the depot, and the parts that do the work (the TextMate
grammar and the language configuration) are declarative JSON that VS
Code reads directly — they never run any code.

**Do not install .NET for this.** Nothing in the working extension uses it.

### Install into VS Code

**Option A — Development mode (quickest)**

1. `Ctrl+Shift+P` → **"Developer: Install Extension from Location..."**
2. Browse to `tools\vscode` inside the repo root → **Select Folder**.
3. Reload when prompted.

Open any `.codex` file. The status bar should read **Codex** and the
file should be colored.

**Option B — Package and install**

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
| — chapter/section/foreword headers | `Chapter:`, `Section:`, `Foreword:` |
| — prose declarations (`We say:`, `To …`) | Automatic |
| — keywords, types, effect rows (`[Identity]`) | Automatic |
| — strings (incl. `"""` triple), chars, hex (`#FF`) numbers | Automatic |
| — annotations (`@name`) | Automatic |
| — arrow/unicode operators (`->`, `→`, `⊢`, `∀`) | Automatic |
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
reference compiler. That compiler is **permanently retired** — it lives
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
contributions are declarative — VS Code applies them whether or not the
extension's code activates — so highlighting, brackets, and indentation
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

- **No highlighting** — check the file extension is `.codex` and the
  status bar language mode reads "Codex". If it reads "Plain Text",
  the extension is not installed or VS Code was not reloaded.
- **"Couldn't start client Codex Language Server" / "spawn dotnet ENOENT"**
  — expected. There is no language server. Dismiss the notification;
  highlighting is unaffected. Do not install .NET, and do not try to
  build anything under `old/`.
- **No squiggles/hover/F12** — not a fault. See "What is not available".

## UEFI Dev Console USB Boot

### Overview

`seed/Codex.img` is an 8 MB GPT disk image containing a FAT16 ESP
partition with `EFI/BOOT/BOOTX64.EFI` (the dev console), the CDX
seed, and concatenated source text (`SOURCE.SRC`). Flash it to a USB
stick and boot from UEFI.

### Building the image

```powershell
build/build-boot-img.ps1
```

Compiles `apps/works/UefiBoot.codex` in IMG mode using the current
seed. Output: `seed/Codex.img` (8 MB).

### Flashing

Two scripts exist. Use `flash-usb.ps1` — it is more reliable.

```powershell
# Recommended — direct write, no Clear-Disk
Start-Process pwsh -Verb RunAs -ArgumentList '-NoProfile','-Command',
  'pwsh build/flash-usb.ps1 -DiskNumber N -Force; Read-Host "Done"'

# Alternative — zeros first, Clear-Disk, verify
Start-Process pwsh -Verb RunAs -ArgumentList '-NoProfile','-Command',
  'pwsh tools/write-usb.ps1 -Image seed/Codex.img -DiskNumber N; Read-Host "Done"'
```

Both require admin (UAC elevation). Find the disk number with
`Get-Disk | Where-Object { $_.BusType -eq 'USB' }`.

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
| QEMU + OVMF (edk2-x86_64-code.fd) | current | Works (use `-accel tcg` to avoid WHPX guest pressure) |

**AllocatePages failure (CL 2019 fix).** The PE stub calls
`AllocatePages(AllocateMaxAddress, EfiLoaderCode, N, &0x100000)`
to allocate pages for the code segment. On some boards the
allocation fails silently — the status is non-zero but the stub
continued and copied code to unallocated memory, corrupting
firmware. CL 2019 added `test rax, rax; jz ok; hlt` after each
`AllocatePages` call. If the stub halts immediately on boot
(no output, no console), the allocation is failing.

**Larger binaries may fail on constrained boards.** As the UEFI
app grows (more menus, features, source), the PE binary gets
larger. Each section-alignment boundary crossing (4096 bytes)
changes the PE layout. On boards with tight memory below 1 MB,
larger binaries may fail to allocate even with the status checks.

### Source browsing

`UefiBoot.codex` reads `SOURCE.SRC` from the FAT16 partition at
boot via `fat16-read-text`. This uses the UEFI Block I/O protocol
via `LocateProtocol`, which finds the **first** Block I/O
instance — not necessarily the boot disk.

| Platform | Source loading |
|----------|--------------|
| ASUS TUF (real hardware) | Untested — may work if USB is first Block I/O |
| QEMU + OVMF | Fails — first Block I/O is NVRAM flash, not boot disk |

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
