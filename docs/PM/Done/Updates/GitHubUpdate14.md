# GitHub Update 14 — CL 1527 to CL 1811 (2026-05-18)

Previous update: CL 1526 (GitHubUpdate13).
This update: CL 1811.

Two days, ~285 changes across main and four dev streams (RESTRUCTURE,
MutableRecords, CodexMagic, Spark). The headlines: the repository was
restructured from 31 top-level directories down to 8, codex-vm
replaced QEMU as the default VM, mutable records landed in the
compiler, and Codex.Spark (a 3D modeling framework) was born.

## Repository Restructure (Friend, CL 1630)

31 top-level directories consolidated to 8:

| Old | New |
|---|---|
| `codex/` (compiler) | `codex/compiler/` |
| `codex.foreword.*` (9 quires) | `codex/foreword/{core,ai,compress,...}` |
| `codex.os.*` + `codex.kernel` | `codex/os/{core,dev,kernel,net,...}` |
| `codex.test` | `codex/test/` |
| `codex.works` | `apps/works/` |
| `codex.games` + `codex.magic` | `apps/games/{classic,magic}` |
| `codex.build` | `build/` |

Build scripts, concat paths, and test paths all updated. Full build
green (CDX fixed point confirmed). Integrated to main across CLs
1654-1696-1710-1714-1724-1778-1794.

## codex-vm Replaces QEMU (Gollum CLs 1592-1606, Reek CL 1632)

codex-vm.exe is now the default VM for all build and test scripts.
Key changes since the initial WHP host (CL 1154):

- **Shadow register file** (CL 1606): workaround for WHP corrupting
  GPRs across VM exits. All 16 GPRs snapshotted after every exit,
  force-written before every run.
- **Serial IRQ fix** (CL 1632): nested serial interrupts during large
  transfers (~1.1M bytes) corrupted the guest stack. All three IRQ
  injection sites now check `serial_irq_pending` guard.
- **NE2000 NIC** (CL 1696): full NE2000 ISA emulation with NAT
  gateway (ARP, IP, UDP, TCP socket proxy). Guest network stack
  works end-to-end.
- **VGA display + PS/2 keyboard/mouse** (CL 1696): 80x25 text-mode
  VGA window with 16-color palette. PS/2 scancode queue for keyboard
  input. Mouse packet buffer at addr 28684.
- **UEFI emulation** (CL 1696): fake SystemTable, ConIn/ConOut,
  BootServices (AllocatePages, GetMemoryMap, ExitBootServices). Trap
  page at 0xF1000 with HLT-based dispatch.
- **GOP (Graphics Output Protocol)** (CL 1788): framebuffer for
  graphical UEFI apps.

QEMU remains available via `$env:USE_QEMU=1`.

## Mutable Records (Reek, CLs 1636 + 1740)

New `mutable` keyword with in-place field assignment:

```
  mutable Counter = record { value : Integer }
  c = Counter { value = 0 }
  c.value = c.value + 1
```

**Phase 1 (CL 1636)**: full pipeline — lexer, parser, AST, desugarer,
type checker, IR, lowering, x86 codegen, emitters. Codegen reuses
`__record-set` path. `IrFieldStore` IR node.

**Phase 2 (CL 1740)**: Boolean on `ARecordTypeDef` tracks mutability
through AST. Type checker registers `__mutable-TypeName` markers and
rejects field assignment on immutable records (CDX2060).

Phase 3 (linearity tracking: CDX2061 use-after-consume, CDX2062
aliasing, `freeze` builtin) is pending.

## REPL Exit Bug Fix (Reek, CL 1802)

CL 1768 replaced the compiler's REPL exit logic (variant match on
`exit-mode`) with a runtime flag at address 28936. That address
collided with `cap-expiry-addr`, and the flag was never initialized —
the REPL broke in any CDX compiled from the current source. Every
other test in the batch battery crashed ("connection aborted").

Fix: reverted to the original variant-match exit code (`when
st.exit-mode is Exit -> halt | is otherwise -> jmp repl-loop`).
The variant match codegen bug that CL 1768 was working around does
not affect this particular pattern. Seed rebuilt. 103/105 pass.

## Codex.Spark — 3D Modeling Framework (Db, CLs 1715-1811)

New application framework in `apps/spark/`. Phases 1-9 in a single
day:

- Meshes, vertices, edges, faces, normals, UVs
- Textures, color picker, gradient editor
- Armatures, IK solver, weight painting
- Particle systems, node transforms
- Material editor, render modes, render passes
- Asset browser, scene outliner, property inspector
- Clipboard, undo/redo, measure tool, grid snap
- Interactive app shell with UEFI console
- Real-time animated demo, ASCII font rendering

Compiles and runs on bare metal via codex-vm.

## CodexMagic — Game Engine (Gollum, CLs 1642-1780)

Continued development of the Magic: The Gathering engine. Themed
HTML pages for the web dashboard, copy-ups to main across multiple
CLs. 99/100 tests passing.

## Web Dashboard (Gollum, CL 1601 + CL 1762)

`tools/web/server.ps1` — PowerShell HTTP server with game lobby,
status page, and themed HTML pages for each game. Serves assets
from `assets/games/`. Game catalog with 30+ playable games.

`tools/status-server.ps1` — lightweight status endpoint.

## Compiler Limits Expanded (CL 1635)

Line number tracking and diagnostic capacity increased to handle
larger source inputs from the restructured repo layout.

## Seed

Rebuilt at CL 1802. Hard fixed point confirmed. All gates green.

| Property | Value |
|---|---|
| Size | 2,134,336 bytes |
| MD5 | `D0421F0A4D2BFF2B18B18B1ED0829168` |
| SHA-256 | `72E0EEE9416EBB143F2C0459E31D71B393356FA572A00752DD2CCD1AA1AC466F` |
| Tests | 156 total, 103 pass, 2 fail (pre-existing), 51 skipped |

## Numbers

- Compiler: 52 files, ~21,000 lines of Codex.
- Foreword library: 88+ chapters across 20 quires.
- Applications: Spark (30+ modules), CodexMagic, 30+ classic games.
- Test battery: ~5 minutes at `-Jobs 4` on 12th gen i7.
- Agents active: Reek, Gollum, Friend, Db.
- Repository: 8 top-level directories (was 31).
