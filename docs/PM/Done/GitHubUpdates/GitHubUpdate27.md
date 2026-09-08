# GitHub Update 27 -- 2026-06-22

Covers main CLs 5244-5737 (since Update 26 at CL 5243, 2026-06-21).
Two days, 99 copy-ups from four agent streams (blu, fester, reek, val).

## RISC-V Backend -- 44 Renode Tests Passing

The RISC-V plug went from 25 to 44 passing Renode tests in a single
session. Nine codegen bugs fixed:

- **Fork/Await**: `IrFork`/`IrAwait` were identity passthroughs.
  Fork now unwraps `IrLambda`, evaluates the body, allocates an
  8-byte heap Task record, and stores the result. Await loads from
  `[ptr+0]`. Non-lambda thunks (named function references) are
  called with a dummy argument.

- **Spill safety (3 sites)**: `rv-bind-ctor-fields`, `rv-store-
  record-fields`, and `rv-eval-record-fields` passed spill slot
  numbers (64+) directly to instruction encoding. 5-bit register
  field truncation: `64 & 31 = 0 = x0`. Fixed by loading into a
  temp register before storing to the local.

- **2-arg TCO swap clobber**: The optimized 2-arg tail-call path
  wrote param registers sequentially. For `edit-distance b a`, the
  first write overwrote `b` before the second read could use it.
  Fixed by routing through the general save-then-copy path.

- **Effect handler dispatch**: Three bugs. (1) Handler address
  treated as closure (one extra dereference). Fixed with direct
  `JALR`. (2) `resume` parameter never passed to handler function.
  Fixed by creating a `__resume_id` identity trampoline at runtime
  and passing its closure as an extra argument. (3) `push/pop-
  handler-slots` modified SP directly, corrupting all spill-slot
  offsets in the function body. Fixed by storing old handler values
  in locals instead of stack manipulation.

- **Handler name collision**: Multiple `with` blocks for the same
  effect operation (e.g., two scopes both handling `tell`) registered
  under the same `__handler_tell` name. `rv-find-func-offset` returned
  the first match. Fixed by appending instruction offset to make
  handler names unique.

## ARM64 Backend -- 62 Renode Tests Passing

Fester's parallel ARM64 push brought the pass count from ~40 to 62:

- Nested effect handler S-register save/restore
- `FunTy` return-type unification for closures
- `list-elems` local recycling to prevent register exhaustion
- Fork/await heap-wrap implementation (matching the RISC-V pattern)
- Oversaturated function call cache invalidation
- Integer-return operation for effect handlers

## All 52 Plugs Build Clean

47 transpiler plugs were broken by IR type extensions (new
`RealApproxTy`, `RealTrappingTy`, `RealSaturatingTy`, `UnitTy` types
and `IrVecPat` pattern) added during the Real number system and vector
work. All 47 fixed in a single parallel-agent batch:

- `IRBinaryOp` matches extended for 12 real-arithmetic variants
  and 4 vector comparison operators
- `IRPat` matches extended for `IrVecPat`
- `CodexType` matches extended for the 3 new real types and `UnitTy`
- Deprecated `++` operator replaced with `&`
- Undefined references resolved (`sort-by` renamed, etc.)

All 52 plugs (5 native + 47 transpiler) now compile cleanly.

## Mutable Records System

Reek's `//Codex/MutableRecords` branch merged to main (CL 5372),
bringing 15+ CLs of semantic infrastructure:

- Exhaustive match analysis for mutable record fields
- Guard clause refinements for mutable state
- Scope analysis for local mutable bindings
- Timing and system-level mutable record support
- Dynamic dispatch for mutable record methods
- Keyboard RGB, multi-monitor, and notification system modules

## 27/27 Web Apps Build Clean

Five app bugs fixed to reach full app compilation:

| App | Fix |
|-----|-----|
| tasks | Added `text-to-int` / `text-to-integer` aliases to Parse foreword; added `cites Foreword chapter Parse` |
| fitness | Type mismatch: `else 0` changed to `else ""` (Text branch) |
| notes | Added `clipboard-write` stub to WebRuntime |
| piano | Replaced `%` operator with `int-mod` (Codex syntax) |
| bridge | Updated `BridgeWebPage` to new `VoiceHierarchy` API (`vh-admirals`/`vh-captains` functions, `VoiceNode.vn-name` fields) |

## GUI OS and Application Portfolio

- **57 apps** in the repository (up from 46 at Update 25)
- **CVMM** (Codex Virtual Machine Manager) -- settings views,
  display manager, deploy/fleet/USB management, productivity
  database, calendar/notes/todo integration
- **Diffusion App** -- image generation interface for GuiOS
- **Tracker** -- issue tracking (types, store, sprint engine,
  workflow bridge)
- **Workflow Engine** -- process automation (types, engine,
  templates)
- **AssetForge** -- asset management for CodexMagic
- **Shared UI Components** -- CommandPalette, DetailPane,
  FilterableList, SearchBar, SettingsPanel, StatusBadge

## OS Kernel and Drivers

147 OS modules (up from 108 at Update 25). New additions:

- **Device management**: AppLauncher, AudioControl, BackupRestore,
  Bluetooth, DateTimeSettings, DefaultApps, GamepadManager,
  GlobalSearch, KeyboardRgb, MultiMonitor, NetworkDiag,
  Notification, PowerManager, PrintManager, PrivacySettings,
  QmkProtocol, ScreenRecorder, ScreenSaver, Taskbar,
  TouchpadSettings, VpnFirewall, Wallpaper, WindowSnap
- **Network**: ImapClient, OAuthClient, OAuthProvider
- **Security**: ExternalAuthBridge
- **Input**: UsbHid
- **Observability**: NotificationLog

## Foreword Library

360 modules (up from 322 at Update 25). Notable additions:

- `Parse` -- `text-to-int`, `text-to-integer` aliases
- `Jwt` -- JSON Web Token encoding
- Shared UI widgets (CommandPalette, DetailPane, FilterableList,
  SearchBar, SettingsPanel, StatusBadge)

## Self-Host

Seed verified: hard fixed point in one pass. All x86-64 gates green
(182/182 battery). Constants hash stable.

## By the numbers

| Metric | Update 26 | Update 27 | Delta |
|--------|----------:|----------:|------:|
| Foreword modules | 322 | 360 | +38 |
| Foreword quires | 12 | 12 | -- |
| OS/Kernel modules | 108 | 147 | +39 |
| Transpiler plugs | 134 | 134 | -- |
| Plug build health | 5/52 | 52/52 | +47 fixed |
| App count | 46 | 57 | +11 |
| App build health | -- | 27/27 | all pass |
| Seed size | 2.31 MB | 2.31 MB | -- |
| Seed digest | `355DE117` | `EF14F3A5` | -- |
| ARM64 Renode tests | ~40 | 62 | +22 |
| RISC-V Renode tests | 25 | 44 | +19 |
| Battery pass rate | 182/182 | 182/182 | -- |
| Copy-ups | 99 | 99 | -- |
| Days | 3 | 2 | -- |
| Agent streams | 4 | 4 | -- |

## What's next

Lambda captures in RISC-V (closures currently 8 bytes, no captured
variables -- ~10 tests blocked). HAMT hash-path bug (2/6 test
failures). CJK input methods. GUI OS milestone 2 (window management).
Sovereignty Phase 2 (build migration). Font hinting.