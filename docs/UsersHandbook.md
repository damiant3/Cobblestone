# Codex User's Handbook

## VS Code Setup

Syntax highlighting, error squiggles, hover types, go-to-definition,
and completion for `.codex` files in Visual Studio Code.

### Prerequisites

1. **.NET 8 SDK** — `dotnet --version` should show `8.x.x` or higher.
2. **Node.js (LTS)** — `node --version` and `npm --version` should work.
3. **Visual Studio Code**.

### Build the extension (one-time)

```powershell
cd tools\vscode
npm install
npm run compile
```

### Install into VS Code

**Option A — Development mode (quickest)**

1. `Ctrl+Shift+P` → **"Developer: Install Extension from Location..."**
2. Browse to `tools\vscode` inside the repo root → **Select Folder**.
3. Reload when prompted.

**Option B — Package and install**

```powershell
cd tools\vscode
npm install -g @vscode/vsce
vsce package
```

Then `Ctrl+Shift+P` → **"Extensions: Install from VSIX..."** → select the `.vsix`.

### What you get

| Feature | Trigger |
|---------|---------|
| Syntax highlighting | Automatic on `.codex` files |
| Error squiggles | On save or change |
| Hover types | Mouse over any name |
| Go to definition | `F12` |
| Peek definition | `Alt+F12` |
| Completion | `Ctrl+Space` |
| Document outline | Explorer sidebar → Outline |

### Using a pre-built server (faster startup)

```powershell
dotnet publish src\Codex.Lsp\Codex.Lsp.csproj -c Release -r win-x64 --self-contained -o out\lsp
```

Set `codex.serverPath` in VS Code settings to the produced executable path.

### Troubleshooting

- **No highlighting** — check file extension is `.codex`, language mode shows "Codex" in status bar.
- **No squiggles/hover** — `View → Output → Codex Language Server` for errors. Run `dotnet build Codex.sln`.
- **"spawn dotnet ENOENT"** — .NET not on PATH. Restart VS Code or set `codex.serverPath`.
- **Missing project file** — open the repo root folder, not a subfolder.

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

```powershell
Copy-Item seed\Codex.img build-output\boot-test.img -Force
Set-ItemProperty build-output\boot-test.img -Name IsReadOnly -Value $false
& 'D:\Program Files\qemu\qemu-system-x86_64.exe' `
  -accel tcg `
  -drive 'if=pflash,format=raw,readonly=on,file=D:\Program Files\qemu\share\edk2-x86_64-code.fd' `
  -drive 'format=raw,file=D:\Projects\NewRepository-reek\build-output\boot-test.img' `
  -m 2048
```

Use `-accel tcg` (software emulation). WHPX adds guest pressure
to the Windows hypervisor and can cause host instability when
combined with codex-vm instances.
