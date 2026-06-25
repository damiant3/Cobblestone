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

## SMP Status

The boot stub already handles INIT/SIPI and AP startup. GuiShell
reads core count from GPA 4088 and ready count from GPA 4080;
Monitor (F2) and Hardware (F3) views display these values.

**Known issue:** Booting with `-smp N` causes `frames=0` (black
screen). The AP halt loop or SMP init sequence interferes with the
BSP event loop. This affects both old and new CDX builds — not a
regression. Root cause investigation needed before M2 SMP work.

---

## M2 Plan

### SMP Integration

1. Diagnose and fix the `-smp` black-screen issue
2. BSP runs GUI render loop, APs run background tasks
3. Per-core CPU gauges in Monitor view
4. Work-stealing visualization

### Interactive Desktop

1. Window drag via title bar
2. Start menu popup
3. File browser (IDE disk directory listing)
4. Real app implementations beyond wireframe views
