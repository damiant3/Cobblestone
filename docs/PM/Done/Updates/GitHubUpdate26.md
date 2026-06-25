# GitHub Update 26 -- 2026-06-22

Covers main CLs 4850-5737 (since Update 25 at CL 4849, 2026-06-18).
Five days, 198 copy-ups from four agent streams (val, reek, fester, blu).

## TrueType Font Rasterizer

A complete TrueType font engine, written from scratch in Codex.
No FreeType, no HarfBuzz, no borrowed code.

- **TTF parser** (`codex.foreword.encode.TrueType`) -- reads the 7
  required tables: head, maxp, hhea, hmtx, cmap (format 4), loca
  (short/long), glyf. Handles simple glyphs (contour points with
  flag expansion, coordinate deltas) and compound glyph stubs.

- **Glyph rasterizer** (`codex.foreword.ui.GlyphRasterizer`) --
  FUnit-to-pixel scaling in 26.6 fixed-point, implicit midpoint
  expansion for off-curve points, adaptive quadratic Bezier
  flattening, scanline fill with non-zero winding rule, Y-flip for
  TrueType coordinates. 4x4 supersampled anti-aliasing with
  downsampling (17 gray levels).

- **Font atlas** (`codex.foreword.ui.FontAtlas`) -- cached glyph
  storage indexed by codepoint, O(1) lookup, ASCII pre-population,
  text width measurement.

- **BitmapFont bridge** (`codex.foreword.ui.TrueTypeFont`) --
  converts rasterized glyphs to the existing BitmapFont system so
  `font-draw-text` works with TrueType fonts on bare metal.

- **14 CC0/public domain TTF fonts** checked into `fonts/cc0/`:
  Computer Modern (serif/sans/mono, regular+bold+italic, 7 fonts)
  plus 7 CC0-licensed display, pixel, and decorative fonts from
  FontSpace. All validated through the parser (684-1324 glyphs each).

- **Tools**: `gen-test-ttf.ps1` (synthetic TTF generator),
  `ttf-to-codex.ps1` (TTF-to-byte-literal converter),
  `validate-fonts.ps1` (header validator), `test-font-render.ps1`
  (end-to-end PowerShell pipeline test).

Design: `docs/Designs/Features/Active/TrueTypeFont.md`

## International Keyboard Layouts

27 keyboard layouts covering all EU languages, Cyrillic, and Greek:

| Script | Layouts |
|--------|---------|
| Latin QWERTY | US, UK, PT, IT, TR, RO, PL, ET, LV, LT |
| Latin QWERTZ | DE, CH, CZ, SK, HU, HR/SI |
| Latin AZERTY | FR |
| Scandinavian | SE/FI, NO/DA |
| Spanish | ES |
| Cyrillic | RU, UA, BG |
| Greek | EL |

- **Dead key composition** for 6 accent types: acute, grave,
  circumflex, diaeresis, tilde, cedilla. Two-stroke sequence: dead
  key sets pending accent, next letter combines.

- **Kernel integration** -- `Keyboard.codex` rewritten to use the
  layout system. `KbState` carries a `KbLayout` and `DeadState`.
  Shift handling uses the layout's shift table with fallback to
  case conversion. Caps lock extended for Latin, Cyrillic, and Greek
  scripts.

- **16-bit layout tables** -- widened from byte arrays to support
  CCE Tier 1 codepoints (Cyrillic, Greek, extended Latin).

- **Runtime layout switching** via `kb-state-for-layout`.

Module: `codex.foreword.core.KeyboardLayout`

## CCE Extended -- Latin-1 Supplement

Tier 1 block 0 extended from Unicode 192-447 to Unicode 128-447.
One-line change (block base 192->128) adds 64 previously unmapped
characters including pound, yen, section, copyright, registered,
degree, plus-minus, micro, inverted question mark, and the rest of
the Latin-1 Supplement (U+0080-U+00BF). All existing CCE mappings
preserved.

## GUI OS -- Milestone 1 + SMP

A graphical operating system shell running on bare metal via GOP
framebuffer, with multi-core support:

- **GuiShell** -- main compositor with sidebar app launcher,
  status bar, RTC clock display, per-app content area.
- **GuiDisplay** -- GOP framebuffer abstraction with resolution
  support for 640x480, 800x600, and 1024x768 (32-bit XRGB8888).
  Multi-monitor management: extended desktop, mirror, and single
  modes with per-monitor scale, rotation, and virtual workspace
  switching. Default fallback 1920x1080.
- **SystemFont** -- Computer Modern Mono (CC0) embedded as system
  font with font settings applet.
- **Working apps**: Calculator (button grid, expression eval),
  Calendar (month view, date navigation), Notepad (text editing),
  Timer (countdown), Diffusion (image generation interface),
  Tracker (issue tracking with sprint engine), Piano (2-octave
  keyboard with ADSR and metronome).
- **MutWheel scheduler** -- mutable state management for GUI apps
  via peek/poke intrinsics. No GC, no closures -- raw memory slots
  for frame counters, tick state, widget IDs, and input buffers.
  Used by Timer, Tracker, Piano, and MediaApps.
- **SMP integration** -- work-stealing scheduler, per-core heap
  isolation, IPI wake signals for idle cores, lock-free cross-core
  channels. codex-vm `-smp N` for 1-16 cores.
- **Mouse support** -- absolute coordinates via I/O port protocol,
  WHP coherency fix, key debounce.
- **App catalog** with sidebar selection and highlight.

## ARM64 Backend -- 62 Renode Tests Passing

Massive push from ~17 to 62 passing Renode tests:

- 17 codegen bug fixes (timeout root cause, local register
  recycling, epilogue restore, trivial-arg, list name mismatch)
- Per-branch local recycling, register allocator design doc
- PE stub ExitBootServices wiring, VirtIO 64-bit BAR
- TCO result unification, nullary ctor with ConstructedTy
- Negative number itoa fix, 16MB code buffer
- Nested effect handler S-register save/restore
- FunTy return-type unification for closures
- Fork/await heap-wrap implementation
- Oversaturated function call cache invalidation
- Integer-return operation for effect handlers

## RISC-V Backend -- 44 Renode Tests Passing

From hello-world to 44 passing tests:

- **Encoder fixes**: rv-li-64 register clobber, rv-li-hi20 floor
  division for negative values, SD operand order
- **Spill safety**: 5 sites fixed (bind-ctor-fields, store-record-
  fields, eval-record-fields, store-to-dest, function return) --
  spill slot numbers (64+) truncated to x0 via 5-bit masking
- **Fork/Await**: unwrap IrLambda, evaluate body, allocate 8-byte
  heap Task record. Non-lambda thunks called with dummy arg.
- **2-arg TCO swap clobber**: optimized path overwrote params
  sequentially; fixed by routing through general save-then-copy
- **Effect handlers**: direct JALR dispatch, resume identity
  trampoline, handler push/pop via locals (not SP), unique handler
  names per scope
- **Memo table**: zero-param non-effectful defs cached in s0-based
  64-slot table, preventing heap exhaustion from global list
  reconstruction
- **Full-range clamp skip**: eliminates useless i64-min/i64-max
  bounds checks that generated massive LI sequences

Both plugs: 7 runtime functions, lambda prologue/epilogue, bounded
integer clamping, ConstructedTy field resolution, show CharTy,
is-char-class, inline dispatches, Real and Real approximate
floating-point support, text equality, indirect closure calls, sum
type structural equality.

Renode board tests (`test-boards.ps1`, `test-cross.ps1`) for
cycle-accurate ARM64 Cortex-A53 and RISC-V RV64GC simulation.

## All 52 Plugs Build Clean

47 transpiler plugs were broken by IR type extensions (new
`RealApproxTy`, `RealTrappingTy`, `RealSaturatingTy`, `UnitTy`
types and `IrVecPat` pattern). All 47 fixed in a single parallel-
agent batch. Every plug -- 5 native (arm64, riscv, elf, pe, img)
plus 47 transpiler (ada through zig) -- now compiles cleanly.

## 27/27 Web Apps Build Clean

57 apps in the repository (up from 46 at Update 25). All 27 web
apps build through the HTML plug without errors:

| App | Fix applied |
|-----|-------------|
| tasks | `text-to-int` alias added to Parse foreword |
| fitness | Type mismatch: else branch returns `""` not `0` |
| notes | `clipboard-write` stub added to WebRuntime |
| piano | `%` operator replaced with `int-mod` |
| bridge | Updated to new VoiceHierarchy API |

## App Lifting -- All Apps at 75%+

Systematic quality campaign across the entire app portfolio:

- 4 rounds of lifting (25% -> 55% -> 70% -> 75-80%)
- Theme dedup, HTML/CSS extraction, JS-to-Codex migration
- 48 features added across 10 apps in final round
- All apps at Flagship tier (75%+)

New apps: CVMM (virtual machine manager with settings, deploy,
fleet, USB, productivity apps), Diffusion, Tracker (issue/sprint),
Workflow (process automation), AssetForge, shared UI components
(CommandPalette, DetailPane, FilterableList, SearchBar,
SettingsPanel, StatusBadge).

## Mutable Records System

Merged from `//Codex/MutableRecords` (15+ CLs):

- Exhaustive match analysis for mutable record fields
- Guard clause refinements for mutable state
- Scope analysis for local mutable bindings
- Dynamic dispatch for mutable record methods

## OS Kernel and Drivers

147 modules (up from 108). 39 new modules including: AppLauncher,
AudioControl, BackupRestore, Bluetooth, DateTimeSettings,
DefaultApps, GamepadManager, GlobalSearch, KeyboardRgb,
MultiMonitor, NetworkDiag, Notification, PowerManager,
PrintManager, PrivacySettings, QmkProtocol, ScreenRecorder,
ScreenSaver, Taskbar, TouchpadSettings, VpnFirewall, Wallpaper,
WindowSnap, ImapClient, OAuthClient, OAuthProvider,
ExternalAuthBridge, UsbHid, NotificationLog.

## Real Number System

- **Polymorphic negate** -- works on both Integer and Real.
- **Conversion builtins** -- `real-from-int`, `real-to-int`,
  `real-approx` wired in seed.
- **Safety modes**: RealApproxTy (default), RealTrappingTy
  (overflow traps), RealSaturatingTy (clamps to bounds).
- **`show` dispatch** for all Real types.
- **Suggested-vector-width** builtin.

## Sovereignty -- Repository Protocol Phase 1

- **Persistence layer** (kinds 30-38): source-unit, annotation,
  review-comment, build-log, coverage-map, dep-lock, migration,
  test-result, doc-chunk. All stored via DiskFacts append-only log.
- **Source scanner** -- indexes `.codex` files into the fact store.
- **Annotation workflow** -- attaches review/doc annotations.
- **Federated sync** -- exchanges facts with remote peer over TCP.
- **DevConsole integration** -- shell commands for scan, annotate,
  status, sync.
- **Roadmap** -- dual-track plan, 6 phases to Perforce cutover.

## Type System

- **Superclass constraints** with polymorphic method dispatch.
- **Sum type structural equality** -- tag comparison.

## codex-vm Improvements

- **Demand-paged memory** -- guest RAM committed on first access.
  Fixes host BSOD on 8 GB VMs.
- **Graceful shutdown** on guest triple-fault.
- **Dynamic RSP** from VM RAM size.
- **Mouse tracking** -- absolute coordinates, Y inversion fix.
- **3 GB default** for concurrent VM safety.

## Seed

Rebuilt with `real-from-int`/`real-to-int` builtins, polymorphic
negate, `text-to-int`/`text-to-integer` parsing aliases, 3 GB RAM.
Hard fixed point in one pass.

## By the numbers

| Metric | Update 25 | Update 26 | Delta |
|--------|----------:|----------:|------:|
| Foreword modules | 322 | 360 | +38 |
| Foreword quires | 12 | 12 | -- |
| OS/Kernel modules | 108 | 147 | +39 |
| Transpiler plugs | 134 | 134 | -- |
| Plug build health | 5/52 | 52/52 | +47 fixed |
| App count | 46 | 57 | +11 |
| App build health | -- | 27/27 | all pass |
| Seed size | 2.30 MB | 2.31 MB | +10 KB |
| Seed digest | `6F75DBC7` | `EF14F3A5` | -- |
| Tests (compilable) | 742 | 748 | +6 |
| Battery pass rate | 167/179 | 182/182 | +15, 0 fail |
| ARM64 Renode tests | ~17 | 62 | +45 |
| RISC-V Renode tests | 0 | 44 | +44 |
| Keyboard layouts | 0 | 27 | +27 |
| TTF fonts | 0 | 14 | +14 |
| CCE coverage | U+00C0+ | U+0080+ | +64 codepoints |
| Apps at 75%+ | 0 | 57 | +57 |
| Copy-ups | 96 | 198 | -- |
| Days | 2 | 5 | -- |
| Agent streams | 4 | 4 | -- |

## What's next

Lambda captures in RISC-V plug (~10 tests blocked). HAMT hash-path
bug. CJK input methods (IME composition). GUI OS milestone 2 (window
management, drag, resize). Sovereignty Phase 2 (build migration).
Font hinting (auto-hinter for crisp small sizes).
