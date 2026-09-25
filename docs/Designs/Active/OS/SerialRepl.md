# Serial REPL from the boot menu

**Status: PROPOSAL** (val, 2026-09-24). Owner: val. Register row: WORKS-5,
`apps/works/works-backlog.md`. Root ruled 2026-09-24: not DeskBuildLoop's
`vm-compile-cdx` (it needs VT-x and no bed can run it), and no second loader
if one exists. None does: no GopBoot row executes a CDX; `seed-line` and the
wake ceremony read `CODEX.CDX` off the stick and verify it, and nothing jumps
into it.

## The capability

A row on GopBoot's boot menu, "Serial REPL", hands the machine to the
compiler on the stick (`CODEX.CDX`, built `-Repl`), which then serves its
REPL on COM1: source in over serial, output back. The payload does not
return; Restart is the way out.

## What a CDX assumes when it starts (read 2026-09-24)

- **It is linked at one address.** Every CDX's code carries 32-bit absolute
  addresses against `ImageBase` 0x100000 (`build/cdx-to-pe.ps1`,
  `$ImageBase`; OperatorsManual, COMPILER-3's table: 3,641 absolute sites in
  the compiler). GopBoot itself runs at 0x100000, so the compiler can only be
  placed there by code that is NOT at 0x100000.
- **`__start` builds its own world** (`emit-start`, `X86_64Chapter.codex`):
  `cli`, RSP loaded from cell 4072 (`ram-size-addr`), the heap at the
  compiled constant `bare-metal-heap-base`, its own PML4 at 0x8000, IDT, TSS,
  the syscall MSRs, and COM1 initialised at 0x3F8. It reads the UEFI system
  table cell only if nonzero.
- **stdin can be pre-loaded**: `__start` skips resetting the serial ring when
  `serial-primed-magic-addr` holds `serial-primed-magic`, which is how
  `cdx-to-pe.ps1 -Stdin` feeds a board with no serial port.

## The mechanism

1. **Read and refuse.** The row reads `CODEX.CDX` with `gfat-read-file-bulk`
   (the wake ceremony's path) into GopBoot's heap, then checks it the way
   `cdx-to-pe.ps1` does before trusting any offset: magic `CDX1`, sections
   tiling the content region, the content hash at bytes 8..39 matching
   `sha256` of the content. On top of that, only an image the wake ceremony
   calls sound is chained: `cdx-to-pe.ps1` accepts an unsigned CDX as a
   build artifact, but handing the machine to a compiler is a trust decision,
   and the stick's seed is signed. A refused image leaves the menu on screen
   with the reason; nothing has been overwritten yet.
2. **Stage.** The seed is about 3.4 MB of text and rodata (3,166,464 +
   288,464 bytes, seed of 2026-09-24), so it stages in GopBoot's own heap,
   where the read already put it. Only the trampoline goes low: a small
   position-independent copy loop on a page in [0x60000, 0x9FC00), below the
   EBDA and clear of the boot cells, the PML4 page and the AP stacks, but NOT
   empty: `X86_64Boot.codex` puts the profiler buffer at 0x60000 and the
   allocation trace from 0x70000, both written only when enabled, so the page
   goes above the trace buffer's end. The copy's target [0x100000, ~0x44B8D0)
   ends below the serial ring (0x500000) and the compiler's heap base.
3. **Quiesce, then jump to the trampoline.** Before the jump GopBoot halts
   every controller it started that masters memory (the xHCI's Run/Stop, at
   least), because GopBoot's heap is about to be reused by nobody who knows
   the rings are there. The trampoline, with interrupts off and on a stack of
   its own inside its page: copies the staged image over 0x100000; writes
   cell 4072 with the compiler's stack top (see the open question); ZEROES
   the system-table cells at 0x8000 and `uefi-systab-addr` (30704) and the
   image-handle cell, because `__start` copies 0x8000 into 30704 whenever
   30704 is zero (`X86_64Chapter.codex`, `emit-start`), so zeroing one is not
   enough; and jumps to 0x100000 + the header's `__start` offset (bytes
   200..207). `__start` pushes five registers before its own `cli`, which is
   why the trampoline's stack must already be valid.
4. The compiler's `__start` rebuilds page tables and the heap and falls into
   `repl-loop`. The stick's `CODEX.CDX` is the seed, built `-Repl`; a
   non-REPL build would run once and halt.

## Open questions, each settled by a measurement before code

- **Cell 4072.** GopBoot's value is the top of its firmware-allocated heap,
  which on AMI boards can sit above what the compiler's page tables map (RAM
  below `bare-metal-ram-size`, 3 GB, plus one device window). The compiler's
  heap grows up from its fixed base toward that stack. Measure where OVMF and
  the flown board put GopBoot's heap (`xp/2gx 0xfe8` in the bed), then write a
  stack top below 3 GB and above the compiler's working set, not GopBoot's.
- **Boot services.** GopBoot runs after ExitBootServices on every image
  (`build-boot-img.ps1` and `build-option-a.ps1 -Ebs` alike), so the chain
  hands the compiler a machine with no live firmware.
- **The menu.** A fifth row overlaps the status line (`msg-y` is 132 px below
  the first row; five rows need 140), and GopBoot.codex:69 still says three
  rows. The layout moves with the row.
- **The screen.** The compiler never draws; the screen keeps GopBoot's last
  frame, so "Serial REPL on COM1" is drawn before the jump.
- **COM1 on a real machine.** Most laptops have no UART at 0x3F8. The bed
  (codex-vm, OVMF) has one. Metal is a sitting question for Damian, not a
  blocker for the bed.
## Proof

1. A codex-vm arm, no GOP: a test payload that reads a small CDX (a REPL
   echo program, built `-Repl`) from a `.disk`, chains into it by the
   mechanism above, and the chained program's serial output is the
   `.expected`. CONTROL: a CDX with one content byte flipped is refused with
   the hash reason, and the payload's own next line still prints.
2. OVMF: GopBoot's row chains into the stick's `CODEX.CDX`; `test-ovmf.ps1`
   pre-loads nothing, so the serial log must show the REPL's banner, then one
   compile fed through the QEMU serial (a `-serial` pipe arm, to be added to
   `test-ovmf.ps1` only if its current `file:` serial cannot carry input).
