# GUI OS Bringup

**Date**: 2026-06-21
**Status**: M1 complete, M2 open
**Stream**: MutableRecords

---

## Goal

Boot Codex.OS under codex-vm with a visible GUI rendered on the GOP
framebuffer, keyboard/mouse input, and a TimingWheel-driven event
loop. Prove the system can run indefinitely with zero memory leakage.

---

## Current State

The GUI desktop boots and runs on bare metal under codex-vm. 19 apps
in the sidebar, each with its own view. TrueType font rendering via
IDE disk. Tab/Enter keyboard navigation across sidebar apps. Mouse
click hit-testing on sidebar and hotbar. Zero-leak render loop.

### Launch Command

```powershell
tools/codex-vm.exe -kernel build-output/guios.cdx -gop -mem 3072 -disk fonts/font-disk.img
```

### Components

| File | Purpose |
|------|---------|
| `apps/guios/GuiShell.codex` | Entry point, event loop, 25 views, taskbar, sidebar, key dispatch |
| `apps/guios/GuiDisplay.codex` | GopBuf pixel buffer, GOP presentation, cursor, CBF + TrueType text |
| `apps/guios/GuiTimer.codex` | MutWheel (mutable timing wheel, zero-alloc state metadata) |
| `apps/guios/FontLoad.codex` | TrueType font loading from FAT32 IDE disk, glyph rasterization |
| `apps/guios/SystemFont.codex` | Pre-rasterized CMU Typewriter bitmap font (95 glyphs, 9x16) |
| `apps/guios/CalcApp.codex` | Calculator with scancode input |
| `apps/guios/CalendarApp.codex` | Calendar with RTC date |
| `apps/guios/ProductivityApps.codex` | Tasks (kanban), Pomodoro (timer), Publisher (editor) |
| `apps/guios/MediaApps.codex` | Boombox (player), Piano (keyboard), Photos (gallery), Podcasts |
| `apps/guios/CreativeApps.codex` | Capture, ImgTools, Diagram, Designer |
| `apps/guios/CommsApps.codex` | Mail, Chat, Collab, Helm |

### Features

- **5 system views** (F1-F5): Dashboard, Monitor, Hardware, About, Apps catalog
- **19 app views**: Notes (notepad), Calendar, Calculator, Settings, Tasks,
  Pomodoro, Publisher, Boombox, Piano, Photos, Podcasts, Capture, ImgTools,
  Diagram, Designer, Mail, Chat, Collab, Helm
- **App sidebar**: 19 entries with selection highlight and Tab/Enter navigation
- **Taskbar**: Codex start button, hotbar with active view indicator, RTC clock
- **TimingWheel event loop**: clock (18 ticks ~1s), heartbeat (90 ticks ~5s)
- **Double-buffered rendering**: GopBuf backbuffer -> GOP hardware FB
- **TrueType fonts**: 3 roles (serif, sans, mono) loaded from FAT32 disk
- **Mouse cursor**: 8x10 arrow with pixel save/restore
- **Piano interaction**: A-; keys highlight notes on a two-octave keyboard
- **Zero-leak render**: `__heap-save`/`__heap-restore` wraps all drawing

### Memory Model

**Pre-allocated buffers.** GopBuf (1.2 MB), cursor save (320 B),
keyboard table (84 B), font cache, MutWheel metadata (256 B),
notepad buffer (2 KB). No per-frame allocation after init.

**MutWheel state.** 256-byte metadata buffer with 32-bit slots for
all mutable UI state: view ID, mouse position, keyboard modifiers,
app selection, calculator state, font settings, tab cursor, and 5
shared app-state slots (offsets 220-240) reused per active app.

### Architecture

```
 opening -> init buffers -> load fonts from disk -> gui-idle loop
   |
   +-- kb-has-key? ------> gui-on-key -> view-specific key handler -> gui-render
   +-- mouse-port-avail? -> gui-on-mouse-port -> sidebar/hotbar click -> gui-render
   +-- count >= 5000? ----> port-out-byte 224 (VM exit) -> tw-tick -> gui-render
   +-- else: gui-idle st (count + 1)
```

---

## M2 Plan

### SMP: the black screen was a stale artifact (closed 2026-07-28)

GuiShell is single-core today. It does not read a core count, does not
display one, and does nothing with the APs.

**There is no black-screen bug. This item is closed and nothing on the
SMP side is blocked behind it.** Measured on a rebuilt `guios.cdx`:
`-smp 4` starts every AP at `0100:0000 (0x1000)`, renders `frames=15`
against 16 single-core, exits clean, and paints the full desktop
(sidebar, panels, clock, status bar), pixel-identical to the
single-core capture on distinct-colour and non-black-sample counts.

What this section used to say was that `-smp N` produces `frames=0`,
that the AP halt loop or the SMP init sequence interferes with the BSP
event loop, and that it **affects both old and new CDX builds and is
therefore not a regression.** Every part of that is wrong, and the last
part is what made it expensive: it told anyone who read it that
rebuilding was pointless.

The real failure was a **triple fault**, not a stalled renderer, and it
belonged to one stale local binary:

- `RDI=0xfee00300` (the LAPIC ICR) with `RAX=0x000C4600`. The low byte
  of a SIPI is its vector, and that vector is **0**, so the APs began
  executing at physical `0000:0000`, ran through low memory, and died
  at `0x990`. The BSP then triple-faulted writing `0x6080`, and the
  bytes at that address differed run to run -- the fingerprint of the
  wild APs scribbling, not a fixed corruption.
- `codex/compiler/Emit/X86_64Boot.codex` declares `ap-sipi-vector = 1`,
  giving ICR `0xC4601` and a trampoline at `0x1000`. Compiled against
  the current seed, `codex/test/smp-cores` starts all three APs at
  `0x1000`, checks them all in, and exits clean.

So the emitted boot code has been correct; the binary under test was
built 2026-07-21 by a seed that emitted vector 0.
**`apps/guios/build-output/` is not in the depot**, so this was one
workspace's leftover, which is also why it could look like it survived
a rebuild to anyone who never did one.

The lesson is cheaper than the debugging: an artifact that is not in
the depot has no provenance unless the build pins it.
`apps/guios/build.ps1` now takes `-Kernel` for that reason. When a
bare-metal symptom smells like boot code, **rebuild before diagnosing**
(L-SELF, L-OUTPUT).

Everything else on the SMP side (BSP renders while APs run background
tasks, per-core gauges in the Monitor view, work-stealing
visualization) is gated only by the kernel-loop wiring tracked in
`docs/Designs/Active/OS/SMP.md`.

### Interactive Desktop

1. Window drag via title bar
2. Start menu popup
3. File browser (IDE disk directory listing)
4. Real app implementations beyond wireframe views
