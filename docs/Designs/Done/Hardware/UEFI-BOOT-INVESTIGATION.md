# UEFI Boot Investigation -- Why the Stick Boots in the VM but Rolls Dice on Real Hardware

**Created**: 2026-07-07 (fester, Fable 5 first pass over the boot chain)
**Status**: Active -- investigation complete, fix not yet applied
**Companion**: `REAL-HARDWARE-BRINGUP.md` (the older BIOS/UEFI checklist)

## The Symptom (Damian's words, paraphrased)

> The same code, built and flashed, gets different behavior on real
> hardware. We got it to sort-of work a few times -- the menu cycled,
> `info` ran -- but iteration was painfully slow and we never got a
> repeatable process even when the code didn't change.

That is not flakiness in the usual sense. It is a **precise signature**:
the boot stub gambles on specific physical addresses being free, and
whether it wins the gamble depends on the firmware's memory map -- which
varies board-to-board **and boot-to-boot**. This document explains the
mechanism, names every place the theory is confirmed in the code, and
proposes the fix.

## TL;DR Root Cause

The UEFI boot stub (`build/cdx-to-pe.ps1`) does three things that are
**illegal-ish before `ExitBootServices`** and that only work because
`codex-vm`'s fake firmware is permissive:

1. **Raw-writes the SystemTable pointer to physical `0x8000`** with no
   `AllocatePages` -- a bare `mov [0x8000], r15` into firmware-owned low
   memory.
2. **Copies the whole compiler to absolute `0x100000`** and jumps there,
   *ignoring* the address `AllocatePages(AllocateAnyPages)` actually
   returned.
3. **Demands `AllocatePages(AllocateAddress, 0x1000000, 512 MB)`** for a
   fixed heap+stack, then sets `RSP` to `~0x9000000`.

It never calls `ExitBootServices`. It runs the entire OS as a UEFI
*application* with firmware alive, on firmware's page tables, having
scribbled into three physical regions it never owned.

`codex-vm` grants all of this unconditionally (see "Why It Works in the
VM"). Real firmware honors its memory map: when any of `0x8000`,
`0x100000`, or `[16 MB, 528 MB)` overlaps a reserved / ACPI / runtime /
MMIO region, the allocation fails (stub `hlt`s) or the write faults. UEFI
memory maps are genuinely non-deterministic across boots, so the same
stick lands differently each time. **That is the observed symptom,
exactly.**

This contradicts our own reference doc, `docs/Reference/UEFI_Spec_Summary.md`
§"Why UEFI Apps Fail on Real Hardware", items #1, #10, which we wrote and
then did not follow.

---

## The Boot Chain, End to End

```
seed/Codex.cdx  (the signed compiler)
  │
  │  build/build-boot-img.ps1
  ├─(1) bundle-app.ps1     apps/works/UefiBoot.codex + deps  → boot-bundled.codex
  ├─(2) compile.ps1 -Uefi  boot-bundled.codex               → boot.cdx
  ├─(3) cdx-to-pe.ps1      boot.cdx  (+ UEFI app stub)       → boot.efi   ← THE STUB
  └─(4) build-img.ps1      boot.efi (+ build-output/Codex.codex source) → seed/Codex.img
  │
  │  tools/write-usb.ps1  (raw write to \\.\PhysicalDriveN)
  ▼
USB stick: GPT → FAT16 ESP → EFI/BOOT/BOOTX64.EFI (=boot.efi), SOURCE.SRC
  │
  ▼
Firmware loads BOOTX64.EFI → stub runs → copies Codex to 0x100000 → jumps to `opening`
  │
  ▼
apps/works/UefiBoot.codex `opening`:
   sys-table = peek-qword 0x8000 0     ← reads the pointer the stub raw-wrote
   con  = uefi-console-init sys-table
   dev-console-loop (Codex Dev Console)
```

### Confirmed facts from the code

**The stub (`build/cdx-to-pe.ps1`, the one actually on the boot path):**

- L124–132: `AllocatePages(AllocateAddress=2, EfiLoaderData=2, HeapPages, [rsp+0x38]=0x1000000)`.
  With `build-boot-img.ps1` passing `-HeapPages 131072`, that is
  **131072 × 4096 = 512 MB demanded at fixed physical `0x1000000`**.
- L114–120: `AllocatePages(AllocateAnyPages)` for code+rodata -- but the
  returned address in `[rsp+0x30]` is **never read again**.
- L135–136: `mov [0x8000], r15` -- raw store of SystemTable to physical
  `0x8000`, **no allocation**.
- L176–195: `rep movsb` copies text to **absolute `0x100000`** and rodata
  to `0x100000 + align8(textSz)`, sources RIP-relative (good), dest
  absolute (fatal on relocation-happy firmware).
- L142–147: `RSP`/`RBP` = `0x1000000 + HeapPages*4096` ≈ `0x9000000`.
- L205–210: `mov rax, 0x100000+openingOff; call rax; hlt`. **No
  `ExitBootServices`.** Interrupts still enabled, firmware timer live,
  running on firmware's page tables with `RSP` in the 144 MB region.

**The entry point (`apps/works/UefiBoot.codex`):**

- L64–70: `opening` reads `peek-qword 32768 0` (`0x8000`) for the
  SystemTable, inits the UEFI console, and drops straight into
  `dev-console-loop`. **It never calls `FirstBoot`** -- see "The FirstBoot
  Wizard Is Written But Not Wired" below.

**The reference we ignored (`docs/Reference/UEFI_Spec_Summary.md`):**

- #1 Relocation: "Real firmware loads the image at an arbitrary address…
  absolute addresses are WRONG after relocation."
- #10 Absolute addresses to low memory: "Physical addresses like 0x7000,
  0x8000, 0x100000 are NOT guaranteed available before ExitBootServices."
- The doc's own recommendation: kernel path must call `ExitBootServices`
  *before* touching absolute low memory, or the app path must run
  in-place and never copy.

---

## Why It Works in the VM (and hides the bug)

`tools/codex-vm.c`, the WHP fake-firmware path:

- **`AllocatePages` (L1926–1958):** for `AllocateAddress` the handler
  comment is literally `/* AllocateAddress: caller set *R9 to exact
  address. Just succeed. */` -- it returns `EFI_SUCCESS` for **any**
  address, occupied or not. Real firmware returns `EFI_NOT_FOUND` when
  the range isn't free.
- **`GetMemoryMap` (L1968–1988):** hardcodes an "ASUS TUF (AMI Aptio V)
  compatible" map that marks `[0x100000, guest_top)` as **type 7
  (EfiConventionalMemory) = all free**. A real Aptio V map is riddled
  with reserved / ACPI-NVS / runtime / MMIO holes below 512 MB.
- **SystemTable at `0x8000` (L148, L1712):** the VM itself parks the
  SystemTable at `0x8000` and nothing else uses that page, so the stub's
  raw `mov [0x8000]` is harmless *here only*.
- **Load base (L4460–4464):** the VM loads the PE at 16 MB and *expects*
  the stub to copy `.text` to `0x100000`, which is always free in-VM.

So every one of the stub's three gambles is a guaranteed win under
`codex-vm` and a coin-flip on metal. The emulator is not modeling the one
firmware behavior that matters for this bug: **honoring the memory map.**

---

## Secondary Findings (real, but not the core bug)

### F1 -- Three divergent, drifting image/PE builders

| Artifact | PowerShell (on the boot path) | Codex plug (canonical per CLAUDE.md, NOT on the boot path) |
|---|---|---|
| CDX→PE | `build/cdx-to-pe.ps1` | `codex/plugs/pe/PeWriter.codex` |
| PE→IMG | `build/build-img.ps1` | `codex/plugs/img/*.codex` |

`build-boot-img.ps1` calls the **PowerShell** pair. The **Codex plugs**
are what CLAUDE.md says produces container formats ("Container formats …
are produced by plug CDX binaries") -- but they are not exercised by the
boot build, so they have silently diverged:

- Entry point: `cdx-to-pe.ps1` jumps to **`opening`** (via debug-map
  lookup); `PeWriter.codex`'s `pe-build-uefi-app-stub` jumps to
  **`__start`** (`entry-off`).
- Low memory: `cdx-to-pe.ps1` raw-writes `0x8000`; `PeWriter.codex`
  `AllocatePages(AllocateAddress)` at `0x7000` **and** `0x8000` (still
  fixed-address, still fragile, but at least asks firmware).
- Heap: `cdx-to-pe.ps1` fixes heap at `0x1000000`; `PeWriter.codex` uses
  `AllocateAnyPages` for heap and stores the base at `0x7580`.

Two implementations of the most safety-critical code in the project, only
one tested, and they disagree on the entry point. This alone makes "the
code didn't change" untrustworthy -- *which* code?

### F2 -- Flash script drift (`flash-usb.ps1` does not exist)

`docs/UsersHandbook.md` and this session's onboarding both tell you to use
`build/flash-usb.ps1` ("more reliable, skips `Clear-Disk`, uses
`Flush($true)`"). **That file does not exist in the repo.** The only
flasher is `tools/write-usb.ps1`, which is the *other* one the Handbook
warns about: it calls `Clear-Disk -RemoveData -RemoveOEM` (the Handbook's
named race), verifies **only the first 4 KB**, and uses `$fs.Flush()`
(not `Flush($true)`) -- though it does open the stream `WriteThrough`,
which mitigates. Net: the recommended-reliable path was never committed,
so every flash used the path the docs call unreliable. This is a *second,
independent* source of non-determinism stacked on top of the boot-stub
gamble.

### F3 -- The FirstBoot wizard is written but not wired

`apps/works/FirstBoot.codex` implements exactly the flow Damian wants --
`PhaseWelcome → PhaseIdentity → PhaseAgentSelect → PhaseUpstream →
PhaseModeSelect → PhaseSaveConfig`, Ed25519 keygen, passphrase-encrypted
key in DiskFacts, boot-mode persistence. But `UefiBoot.codex`'s `opening`
goes straight to `dev-console-loop` and **never calls
`first-boot-entry`**. The "pick the model, set your key, save to the
stick, then boot the full OS" experience isn't missing -- it's
**disconnected**. Wiring it in is cheap *once boot is reliable*; doing it
before is polishing a floor that keeps falling through.

### F4 -- Stale artifacts

`seed/Codex.img` and `seed/Codex.elf` are dated **2026-07-01**; the seed
`Codex.cdx` is **2026-07-07** (post map-mode-flag, post NoAliasCodegen
merge-down). The committed `.img` was built from a seed that no longer
exists. Any "it worked once" memory may be against a binary we can't
reproduce.

### F5 -- DevConsole / VGA / GOP maturity

- **DevConsole (UEFI text):** the most-complete path; this is what
  "sort-of worked" (menu cycled, `info` ran). It depends only on ConOut /
  ConIn, which survive without `ExitBootServices`.
- **VGA text:** never worked on the UEFI path and can't -- there is no VGA
  text mode under pure UEFI/GOP without CSM. Dead end by design.
- **GOP framebuffer (the desired first-boot UI):** unreached. It needs a
  reliable boot first, then `LocateProtocol(GOP)` + `QueryMode/SetMode` +
  Blt or direct framebuffer writes. The `UefiGopMode`/`UefiPixel` types in
  `UefiConsole.codex` exist; the wiring does not.

---

## The Fix -- Two Viable Architectures

The current stub is a **hybrid**: it acts like a kernel loader (copies to
absolute addresses, moves the stack, `hlt`s -- never returns) while
*keeping firmware alive* (no `ExitBootServices`). That hybrid is the worst
of both worlds and is the direct cause of the address gamble. Pick one
lane and commit.

### Option A -- Proper kernel path (recommended for the OS goal)

Do what a real bootloader does, in this order, **inside the stub**:

1. `AllocatePages(AllocateAnyPages)` for code+rodata+heap; **keep the
   returned addresses**. Do not hardcode `0x100000`/`0x1000000`.
2. Build our own identity-mapped page tables in *allocated* pages.
3. `GetMemoryMap` → `ExitBootServices(ImageHandle, mapKey)` (retry once on
   stale key). After this, firmware is gone and low memory is ours.
4. *Now* copy to `0x100000` / park state at `0x8000` if we still want
   those fixed addresses -- legal post-EBS -- or better, make the compiled
   image position-independent and skip the copy.
5. `cli`, load our IDT, set `RSP`, jump to `opening`.

Cost: real work in the stub (page tables, EBS, memory-map buffer). But it
is the *only* path that makes the address assumptions true instead of
lucky, and it's the path `UEFI_Spec_Summary.md` §"Kernel stub" already
specifies. Trade-off: ConOut dies at EBS, so first-boot UI must be GOP
(framebuffer) or serial -- which is the direction we want anyway.

### Option B -- Honest UEFI application (fastest to "reliable menu")

Keep firmware alive, but stop lying about addresses:

1. Run the compiled image **in place** at firmware's load address (needs a
   position-independent build, or `AllocateAnyPages` + `.reloc` fixups).
2. Pass the SystemTable pointer **in a register / on the stack** to
   `opening`, not through physical `0x8000`.
3. Allocate the heap with `AllocateAnyPages`; thread the base to the
   runtime instead of hardcoding `0x1000000`.
4. Never move `RSP` into an unmapped region; use firmware's stack or an
   allocated one.
5. Return `EFI_SUCCESS` or call `Exit()`; don't `hlt`.

Cost: the compiled Codex program must accept its base addresses as inputs
rather than baking them in (a codegen/runtime change:
`peek-qword 0x8000` → a passed pointer; R10/heap init from an argument).
Keeps ConOut alive, so the text DevConsole keeps working while we get GOP
going. Good for iteration; weaker as a final OS story.

### Cross-cutting, do regardless of A/B

- **Collapse the two builder families.** Make the boot build use the
  Codex plugs (`pe`, `img`) *or* delete the plugs and bless the
  PowerShell scripts -- one source of truth for the stub. Today they
  disagree on the entry point.
- **Teach `codex-vm` to model a hostile memory map** behind a flag
  (`-uefi-strict`): reserve realistic holes, fail `AllocateAddress` on
  occupied ranges. Without this, the VM will keep green-lighting code that
  bricks on metal. This is the highest-leverage single change for
  iteration speed -- it turns a hardware-only bug into a VM-reproducible
  one.
- **Commit the reliable flasher.** Add `build/flash-usb.ps1` (direct
  write, `Flush($true)`, full-image verify, no `Clear-Disk`) that the docs
  already promise, or fix the docs to match `write-usb.ps1`.
- **Wire `FirstBoot` into `UefiBoot.opening`** -- but only after a boot is
  repeatable.

## Progress -- `-uefi-strict` shipped, bug reproduced in-VM (2026-07-07)

Step 1 of the sequence is done. `codex-vm` now has a `-uefi-strict` flag
(`tools/codex-vm.c`) that models the two things the permissive fake
firmware faked:

1. **`AllocateAddress` honors the memory map.** A fixed-address request
   overlapping the first 2 MB (firmware-owned) or the running image's own
   load region returns `EFI_NOT_FOUND`, like real firmware.
2. **Firmware owns low memory before `ExitBootServices`.** The first 2 MB
   is split to 4 KB pages and marked not-present, except the structures
   the CPU/firmware legitimately expose (GDT/TSS/IDT at `0xA000`-`0xBFFF`,
   the SystemTable/BootServices tables + HLT trap page at
   `0xF0000`-`0xF1FFF`). A write to a fixed low address the app never
   allocated now faults, and strict mode prints a crash report naming the
   RIP and CR2 and exits (instead of spinning).

Booting the **existing** `seed/Codex.img`:

| Mode | Result |
|---|---|
| `-uefi` (permissive) | Boots -- compiler runs at `0x100000`, menu path reached. Every fixed-address gamble is granted. |
| `-uefi-strict` | `AllocateAddress(0x1000000, 131072 pages) -> EFI_NOT_FOUND`, then `CRASH: fault at RIP=0x1001082 accessing CR2=0x8000 -- the UEFI app touched firmware-owned low memory it never allocated`. |

`CR2=0x8000` is precisely the stub's `mov [0x8000], r15` SystemTable
write. The hardware-only "same code, different behavior" bug is now a
deterministic, one-command VM failure. Iteration on the fix happens here,
not on the stick.

**Known strict-mode v2 item:** flip the firmware-owned low pages to
present when the guest calls `ExitBootServices`, so a *correct* Option-A
stub (allocate → EBS → then own low memory) passes strict mode. Until
then, strict mode is a "does the stub illegally touch firmware memory
before EBS" detector, which is exactly what we need to validate the
rewrite's early stages.

## Recommended Sequence

1. **`-uefi-strict` in `codex-vm`** -- make the bug reproducible in the VM.
   Nothing else is worth doing blind. (Est: medium; pure C in `codex-vm.c`
   `AllocatePages` + `GetMemoryMap`.)
2. **Pick Option A or B** and rewrite the *single* blessed stub against
   the strict VM until it boots there deterministically.
3. **Commit `flash-usb.ps1`** and rebuild `seed/Codex.img` from the
   current seed.
4. **One** real-hardware flash on the ASUS TUF -- expect it to work,
   because the VM now models the failure. Iterate in the VM, not on the
   stick.
5. **Wire FirstBoot + GOP** on the now-stable base.

## DECISION: Option A (full kernel path) -- 2026-07-07

Damian chose **Option A**. The rewritten stub does ExitBootServices and
the compiled program renders via the GOP framebuffer; the UEFI text
ConOut path (UefiConsole/DevConsole) is retired for boot because ConOut
dies at EBS.

### The strict-clean instruction ordering (the crux)

The failure mode of *every* current stub is doing fixed-address work
(write 0x8000, copy to 0x100000) while on firmware's / codex-vm's map,
before owning that memory. The fix is ordering: **acquire everything
through boot services, exit, install our own identity map, and only
then touch fixed low addresses.** Precise sequence:

1. Prolog; save RCX=ImageHandle, RDX=SystemTable.
2. **Acquire GOP while boot services live**: `LocateProtocol(GOP)` →
   read `Mode->FrameBufferBase`, `HorizontalResolution`,
   `VerticalResolution`, `PixelsPerScanLine`, `PixelFormat`. Stash in
   callee-saved regs / a scratch struct. (The framebuffer is a physical
   region that *survives* EBS; the pointer must be obtained before.)
3. `AllocateAnyPages` for code+rodata, heap, and page-table area. **Keep
   every returned address.** Check each status; on failure, print via
   ConOut (still alive) and halt with a clear code.
4. `GetMemoryMap` → `ExitBootServices(ImageHandle, mapKey)`; retry once
   on stale key. Firmware is now gone; we own all conventional memory.
5. Build an identity page table in the allocated page-table area
   (PML4→PDPT→PDs, 2 MB pages, enough to cover RAM + the GOP FB).
6. `mov cr3` to our tables. **Now** low memory (0x100000, etc.) is
   present in *our* map -- no dependency on firmware or codex-vm's map.
7. Copy `.text` to its link address (0x100000) and `.rodata` to
   data-vaddr; set up heap base, RSP (real stack in allocated memory),
   IDT; pass the GOP framebuffer base/dims + heap base to the program.
8. Jump to `opening`.

Because every fixed-address write (step 7) happens *after* step 6, the
stub touches only memory it owns. Under `-uefi-strict` the not-present
firmware pages are irrelevant once our CR3 is live, so a correct Option A
stub passes strict **without** needing the strict-v2 EBS page-flip. (The
current broken stub still faults, because it never reaches EBS -- good:
strict stays a valid detector.)

### Milestones (each ends in a demo)

- **A1 -- Boot & paint. [DONE, CL 7280]** Stub-only (`build/boot/a1_boot_paint.asm`):
  acquire GOP, EBS, own page tables, paint 8 color bars. Strict-clean;
  screenshot `build/boot/a1.png`. Fixed two codex-vm gaps (4 GB UEFI map;
  GOP shadow sync for direct-FB writes).
- **A2 -- GOP text. [DONE, CL 7283 handoff + 7284 text]** The Option A stub
  (`build/boot/option_a_stub.asm`, wrapped by `build-option-a.ps1`) wraps a
  real CDX and, in strict-clean order, hands the framebuffer to
  `apps/works/GopBoot.codex` via cells at 0x8000. GopBoot clears the FB and
  draws text with the CBF bitmap font. Strict-clean; text confirmed
  (`build/boot/optiona.png`). Fixed a third codex-vm gap: AllocatePages/Pool
  now commit the host pages they hand out (else GetMemoryMap's host memcpy
  access-violated the VM).
- **A3 -- Menu over GOP. [DONE, CL 7286]** `apps/works/GopBoot.codex` draws
  an interactive menu (4 items, highlight bar) with the CBF font and reads
  Set-1 scancodes from the kernel key buffer (cell 28680): Up/Down move,
  Enter confirms. Screenshot confirmed via a new codex-vm `-keys` scancode
  injector (down,down,enter selects "Serial REPL"). Fixed a fourth codex-vm
  issue: pending scancodes now flush to 28680 every main-loop iteration, not
  every 64 exits (~3.5 s) -- real keyboard on a compute-bound GOP guest was
  unusably laggy.
- **A4 -- FirstBoot + key ceremony.** Wire `first-boot-entry` and the
  WakeCeremony over the real seed read from the FAT partition.

  Still pending before a real flash: port the proven `option_a_stub.asm`
  sequence into the self-hosted blessed builder (`build/cdx-to-pe.ps1`),
  collapse the two builder families, then rebuild `seed/Codex.img` and
  commit `build/flash-usb.ps1`.

### Single blessed builder

Iterate the stub in **one** place. `build/cdx-to-pe.ps1` is on the boot
path and fastest to iterate against the strict VM, so it becomes the
source of truth; the Codex `pe`/`img` plugs get reconciled to match (or
retired) once A1 is green. Do not leave two stubs that disagree again.

## Open Questions for Damian (remaining)

- **Which lane** -- full kernel (A, ExitBootServices, GOP/serial only) or
  honest app (B, ConOut stays, needs PIC-ish runtime)? A matches the OS
  vision; B is faster to a working menu.
- Is making the compiled image accept **base addresses as arguments**
  (instead of hardcoded `0x8000`/`0x100000`/`0x1000000`) an acceptable
  codegen/runtime change? It's the crux of getting off fixed addresses in
  either lane.
- Do we keep the **Codex plugs** as the canonical builders (and route the
  boot build through them), or retire them in favor of the PowerShell
  scripts?

---

## Appendix: Fixed-Address Inventory (everything the stub assumes)

| Address | What | Set by | Read by | Legal pre-EBS on metal? |
|---|---|---|---|---|
| `0x8000` | SystemTable ptr | stub `mov [0x8000]` | `UefiBoot.opening` `peek-qword 0x8000` | **No** -- firmware-owned |
| `0x100000` | Codex `.text` copy dest | stub `rep movsb` | CPU (`call`) | **No** -- may be reserved/relocated |
| `0x100000+align8(text)` | Codex `.rodata` copy dest | stub `rep movsb` | Codex code | **No** |
| `0x1000000` | heap base (fixed) | `AllocateAddress` | R10 init | **No** -- 512 MB fixed region |
| `~0x9000000` | stack top | `mov rsp` | CPU | **No** -- inside the fixed region |
| `0x7030` (deck-pos) etc. | kernel metadata cells | stub direct writes | runtime | **No** -- low memory, firmware-owned |

Every row is a place the code says "this address is mine" without asking
firmware. Under `codex-vm` every claim is granted. On metal, each is an
independent coin-flip, and the boot succeeds only if **all** come up
heads -- which is why it booted "a few times" and never repeatably.
