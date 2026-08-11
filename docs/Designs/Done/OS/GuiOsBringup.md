# GUI OS Bringup

**Date**: 2026-06-21
**Status**: M1 complete, M2 open
**Stream**: MutableRecords

---

## RULED 2026-08-03 (Damian): there is one desktop, and it is GopDesk

*"right hand didn't know left hand doings. we only need 1."*

Everything below this section describes `apps/guios/GuiShell.codex`, which runs
under codex-vm and **has never rendered on metal**. The desktop that boots the
ASUS is `apps/works/GopDesk.codex`, and it sits in the metal stack beside
`GopXhci`, `GopUsbKbd`, `GopUsbMsc`, `GopBoot` and `GopFat16`.

They were never fully independent. **`apps/works/GopFont.codex` cites `Guios
chapter FontLoad` and `Guios chapter GuiDisplay`** -- the metal desktop has been
reusing this app's font pipeline verbatim the whole time, and both stacks
already share the UI foreword (`Widget`, `Theme`, `Layout`). The real
duplication is the shell and the app views.

**Consequences:**

- **`GopDesk` is the product.** M2's "the desktop renders on metal" is a
  GopDesk row now, and it is unblocked: GOP is METAL and input became METAL on
  2026-08-03 (both the firmware path and USB HID).
- **`FontLoad`, `FontAi` and `GuiDisplay` stay** as the font library they
  already are. Nothing to port.
- **`GuiShell`, `GuiTimer` and the app-view chapters retire.** They are
  wireframes by this document's own M2 list, so porting them is a rewrite
  either way. Retire a chapter when a GopDesk pane replaces what it did, and
  delete it in that changelist. **Do not open a merge campaign.**

The rest of this document is kept as the record of what the guios shell does,
because the font pipeline and the MutWheel memory model are still live and
still cited.

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

**There is no longer one.** `GuiShell` was the app's only entry point and it
is deleted, so `apps/guios/build.ps1` and `start.ps1` went with it: there is
nothing left in this directory with an `opening`. The desktop is
`build/desk.ps1` (GopDesk under codex-vm) or an Option A boot image.

### What `apps/guios` is now

**A font library, and that is the whole of it.** The five chapters below are
load-bearing for the SHIPPING desktop and are not retiring: `apps/works/GopFont.codex`
cites `Guios chapter FontLoad` and `Guios chapter GuiDisplay`, `FontLoad` cites
`GuiTimer` and `GuiDisplay`, and `GuiDisplay` cites `SystemFont`. The ruling at
the head of this document said "FontLoad, FontAi and GuiDisplay stay as the font
library they already are"; measured 2026-08-03, that is right and it extends to
`GuiTimer` and `SystemFont` through the same chain.

| File | Purpose |
|------|---------|
| ~~`apps/guios/GuiShell.codex`~~ | **DELETED 2026-08-03.** Entry point, event loop, 25 views. Cited by nothing when it went -- the two test files naming it did so only in prose. Its two GUI goldens, `apps/guios/tests/desktop-chrome` and `dashboard`, were deleted with it |
| `apps/guios/GuiDisplay.codex` | GopBuf pixel buffer, GOP presentation, cursor, CBF + TrueType text. **Stays** -- `GopFont` cites it |
| `apps/guios/GuiTimer.codex` | MutWheel (mutable timing wheel, zero-alloc state metadata). **Stays** -- `FontLoad` and `GuiDisplay` cite it |
| `apps/guios/FontLoad.codex` | TrueType font loading from FAT32 IDE disk, glyph rasterization. **Stays** -- `GopFont` cites it |
| `apps/guios/SystemFont.codex` | Pre-rasterized CMU Typewriter bitmap font (95 glyphs, 9x16). **Stays** -- `GuiDisplay` cites it |
| ~~`apps/guios/CalcApp.codex`~~ | **MOVED 2026-08-03 to `apps/works/GopCalc.codex`** and wired to the Calc pane (`c`). State moved off MutWheel into a cell block; behaviour unchanged |
| ~~`apps/guios/CalendarApp.codex`~~ | **MOVED 2026-08-03 to `apps/works/GopCal.codex`** and wired to the Calendar pane (`l`). Date arithmetic unchanged; the month is a widget grid now instead of hand-placed pixels, and today is a button rather than a hand-filled rectangle |
| `apps/guios/DiffusionApp.codex` | **UNFINISHED, and kept as such** (Damian, 2026-08-03: "diffusion app is unfinished and has a button that does nothing. that's fine"). The screen composes real foreword widgets; the Generate button dispatches to nothing and `df-draw-generating` fills a progress bar from a counter to 20 with no model behind it. Finishing it is `apps/works/works-backlog.md` **WORKS-3**. **It still cites `GuiDisplay`, `GuiTimer` and `GopRender`, so porting it into `apps/works` gates the deletion of those three** -- the port is separate from, and much cheaper than, finishing the app |
| ~~`apps/guios/TrackerApp.codex`~~ | **List view MOVED 2026-08-03 to `apps/works/GopTrack.codex`**, wired to the Issues pane (`i`). The kanban board, sprint cycle, detail overlay, command palette and focus ring did NOT come across: each was a `(GopBuf, ShellUiState)` painter needing shell-owned UI state. The chapter stays in depot history for them |
| ~~`apps/guios/TrackerDb.codex`~~ | **MOVED 2026-08-03 to `apps/works/GopTrackDb.codex`**, verbatim. A relational layer over `DbServer` with no UI in it, which is why it survived the retirement intact |

**DELETED 2026-08-03: `ProductivityApps`, `MediaApps`, `CreativeApps`,
`CommsApps`** (Damian: "remove the menuitems permanently"). Every definition in
those four was a `draw-view-*` painter taking `(GopBuf, ShellUiState)` -- the
menu-item layer and nothing else. They were cited by nothing, absent from
`apps/guios/build.ps1`'s chapter list, and **not called by GuiShell**, which has
no `draw-view-` reference in it: they were compiled only by
`build/sweep-app-classes.ps1` and never run. The fourteen views they name
(Tasks, Pomodoro, Publisher, Boombox, Piano, Photos, Podcasts, Capture,
ImgTools, Diagram, Designer, Mail, Chat, Collab, Helm) were never reachable.

### Features

**The list below describes GuiShell, which has never rendered on metal.
Measured 2026-08-03: the 19 "app views" are wireframes GuiShell builds itself,
with widget ids like `calc-*` that never call `CalcApp`.** Read it as the
sketch it is, not as shipped capability.

- **5 system views** (F1-F5): Dashboard, Monitor, Hardware, About, Apps catalog
- **App sidebar**: 19 entries with selection highlight and Tab/Enter navigation
- **Taskbar**: Codex start button, hotbar with active view indicator, RTC clock
- **TimingWheel event loop**: clock (18 ticks ~1s), heartbeat (90 ticks ~5s)
- **Double-buffered rendering**: GopBuf backbuffer -> GOP hardware FB
- **TrueType fonts**: 3 roles (serif, sans, mono) loaded from FAT32 disk
- **Mouse cursor**: 8x10 arrow with pixel save/restore
- ~~**Piano interaction**~~ -- deleted with `MediaApps` 2026-08-03
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
