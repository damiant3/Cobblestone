# Build Tooling Migration: PS1 to Codex

## Goal

The bootable IMG ships with source, compiler, editor, and shell. A developer
can compile, test, and deploy Codex programs from bare metal without any
host tooling. PowerShell remains as a thin QEMU orchestration layer on the
host; all computation moves into Codex.

## Current State (CL 1123)

### What exists in Codex

| Component | Location | Status |
|-----------|----------|--------|
| Cite resolution | `codex/opening.codex` `load-cited-foreword` | **Dead code** — defined, never called |
| Quire-to-path mapping | `codex/opening.codex` `quire-to-dir` | Works (19 quires mapped, incl. UI — CL 1121) |
| FAT16 reader | `codex.foreword/Fat16.codex` | Read-only: init, read-file, file-exists, list-dir, read-text (CL 1117) |
| FileSystem effect | `codex.foreword/FileSystem.codex` | Interface only — `read-file`, `write-file`, `file-exists`, `open-file` |
| `read-file` builtin | `codex/Emit/X86_64Helpers.codex` | **Stub** — reads from serial, not disk |
| `file-exists` builtin | `codex/Emit/X86_64Builtins.codex` | **Stub** — always returns True |
| Block I/O | `codex.kernel/DiskFacts.codex` | `block-read-sector`, `block-write-sector` — raw 512-byte sectors |
| FAT32 writer | `codex/Emit/Fat32Writer.codex` | Write-only, used for IMG generation during compilation |
| PE writer | `codex/Emit/PeWriter.codex` | Full PE32+ with .reloc, kernel stub (page tables, ExitBS, trampoline) |
| CDX signing | `codex/opening.codex` | Ed25519 sign via inline program |

### What exists only in PS1

| Script | Lines | What it does | Migration path |
|--------|-------|-------------|---------------|
| `concat-codex-self.ps1` | 82 | Concatenate compiler source with quire prefixes | Compiler reads from disk via `load-cited-foreword` |
| `sample-compile-selfhost.ps1` | 175 | Boot QEMU, resolve cites, feed source via serial | Keep QEMU orchestration; cite resolution moves to compiler |
| `make-efi.ps1` | 789 | CDX to PE32+ with UEFI boot menu | PE construction done (CL 1111-1113). Boot menu becomes a Codex UEFI app |
| `make-usb-image.ps1` | ~200 | GPT + FAT16 disk image | Write Codex FAT16/GPT forewords |
| `sweep.ps1` | 244 | Parallel test runner | On-disk test runner Codex app |
| `pingpong-self.ps1` | 330 | Fixed-point verification | On-disk self-compile + byte-compare |
| `validate-prose.ps1` | 95 | CPL compliance scan | **Stripped to compiler-only** (CL 1121) — just invokes compiler prose pass |
| `build-boot-img.ps1` | 92 | Assemble EFI + disk image | **Uses compiler IMG mode** (CL 1123) — 1 VM instead of 3 tools |
| `build-record.ps1` | ~150 | Hash + JSON provenance | Sha256 + Json forewords exist |
| ~~`annotate.ps1`~~ | ~100 | ~~Annotation management~~ — deleted at CL 1141. **Lift target**: Codex-native `codex.works/AnnotationsCli.codex`. See addendum at the bottom of `docs/Active/Compiler/Annotations.md` |
| `gpu-serial-proxy.ps1` | ~200 | Serial to GPU dispatch bridge | Binary protocol in Codex |

### What stays as PS1 forever

- `qemu-config.ps1` — QEMU process management, port allocation
- `run-for-sweep.ps1` — Boot kernel in QEMU, capture serial
- `clean-zombies.ps1` — Kill orphaned QEMU/WSL processes
- `build-gpu-dispatch.ps1` — CUDA/nvcc invocation
- `stress-sweep.ps1` — Loop driver (just calls sweep.ps1)

## Architecture

### Layer Diagram

```
+--------------------------------------------------+
|  Host (Windows)                                   |
|  PS1: QEMU launch, port management, serial I/O   |
+--------------------------------------------------+
        |  serial / disk image
        v
+--------------------------------------------------+
|  Bare Metal (QEMU or real hardware)               |
|                                                   |
|  Shell ──> Compiler ──> Test Runner               |
|    |          |              |                    |
|    v          v              v                    |
|  Editor   FAT16 Reader   Byte Compare             |
|              |                                    |
|              v                                    |
|  Block I/O (ATA IDE)                              |
+--------------------------------------------------+
```

### Compilation Modes

Current serial-feed mode (PS1 resolves cites, sends everything):
```
PS1: concat source + forewords -> serial -> compiler -> binary -> serial -> PS1
```

New disk-compile mode (compiler resolves cites from FAT16):
```
PS1: send "DISK path.codex\n" -> serial -> compiler reads disk -> binary -> serial/disk
```

On-device mode (no PS1, no serial):
```
Shell: compile path.codex -> compiler reads disk -> binary -> disk
```

## Implementation Plan

### Phase 1: FAT16 Reader Foreword — DONE (CL 1117)

`codex.foreword/Fat16.codex` — read files by path from FAT16 partition.

Implemented:
- `fat16-init : Integer -> Fat16Volume` — read BPB from partition start sector
- `fat16-read-file : Fat16Volume -> Text -> Maybe (List Integer)` — read file by path
- `fat16-file-exists : Fat16Volume -> Text -> Boolean` — check if path exists
- `fat16-list-dir : Fat16Volume -> Text -> List Text` — list directory entries
- `fat16-read-text : Fat16Volume -> Text -> Maybe Text` — read file as text (CCE)

Test: `codex.test/apps/fat16-read-test.codex` (compiles, runtime blocked by ATA WHPX crash)

### Phase 2: Wire FileSystem Builtins to FAT16

Replace the stubs in the compiler's codegen:
- `__read_file` — call FAT16 reader instead of serial read
- `file-exists` — call FAT16 file-exists instead of returning True
- `write-file` — call FAT16 write (needs FAT16 writer, Phase 5)

The compiler needs to know the partition offset. On UEFI boot, the
SystemTable at 0x8000 provides access to block I/O protocols. On
multiboot (QEMU), the IDE disk is at known I/O ports.

Decision: **dual-path FileSystem.** Serial-feed mode for QEMU compilation
(current behavior). Disk mode for on-device compilation. Mode selected by
a flag in the compilation mode header or by detecting whether a disk is
present.

### Phase 3: Disk-Compile Mode

Add `DISK` command to `dispatch-on-mode`:
```
else if cmd == "DISK" then emit-from-disk clean flags
```

`emit-from-disk`:
1. Read file path from serial (the "source" is just a path)
2. Read the file from FAT16 disk
3. Call `load-cited-foreword` to resolve cites from disk
4. Concatenate foreword prefix + source
5. Compile via existing pipeline
6. Output binary via serial (or write to disk)

This eliminates `concat-codex-self.ps1` and the cite resolution in
`sample-compile-selfhost.ps1`. The PS1 just sends a path instead of
the entire source.

### Phase 4: On-Disk Test Runner

New Codex app `codex.works/TestRunner.codex`:
1. List `codex.test/*.codex` from FAT16
2. For each: read source, compile (call compiler functions directly or via IPC), run, capture output
3. Read `.expected` / `.failing` / `.skip` sidecars from disk
4. Diff actual vs expected
5. Print pass/fail summary

Challenge: running a compiled program from within another program.
Options:
- **In-process:** compile to memory, jump to entry point, return. Requires resetting heap between tests.
- **Subprocess:** OS scheduler runs compiled program as a separate task. Requires IPC for output capture.
- **Self-invoke:** compiler compiles + runs in one step (like current sweep but without QEMU restart).

Recommendation: in-process with `heap-save`/`heap-restore` between tests.
The compiler already uses this pattern for phase boundaries.

### Phase 5: Self-Hosted Pingpong

Codex app `codex.works/Pingpong.codex`:
1. Read compiler source from disk (`codex/*.codex`)
2. Compile source in CDX mode -> stage1.cdx (write to disk)
3. Load stage1.cdx, use it to compile source -> stage2.cdx
4. Byte-compare stage1 vs stage2
5. Report fixed-point status

Challenge: step 3 requires loading and executing a compiled CDX.
The CDX is a bare-metal binary. Running it means jumping to its
entry point with a fresh stack/heap. This is essentially a reboot
into the new binary.

Alternative: compile source in TEXT mode twice (text round-trip).
This is simpler — no binary loading, just string comparison.
TEXT round-trip proves the emitter is a fixed point. CDX fixed
point proves the binary is a fixed point but requires binary
execution.

### Phase 6: Editor

New Codex app `codex.works/Editor.codex`:
- VGA text-mode display (existing VGA terminal support)
- Keyboard input (existing keyboard driver)
- File open/save via FAT16
- Syntax highlighting for .codex files
- Line numbers, cursor movement, search

Depends on: codex.foreword.ui (Widget, Cursor, Scroll, etc.)

### Phase 7: Shell

New Codex app `codex.works/Shell.codex`:
- Command prompt with history
- Built-in commands: `compile`, `run`, `test`, `edit`, `ls`, `cat`, `pingpong`
- Tab completion (TabComplete foreword exists)
- Pipes output to VGA terminal

## Priority Order

1. **FAT16 reader** — everything depends on reading files from disk
2. **FileSystem builtins** — wire `read-file` and `file-exists` to FAT16
3. **DISK compile mode** — compile from disk path, eliminate concat PS1
4. **Test runner** — on-device sweep
5. **Text pingpong** — self-compile verification on device
6. **Editor** — edit source on device
7. **Shell** — interactive command interface

## Recent Progress

| CL | What |
|----|------|
| 1113 | `build-pe-from-cdx` .reloc section, `build-boot-img.ps1` uses compiler EFI mode |
| 1117 | FAT16 reader foreword + fat16-read-test |
| 1121 | UI quire support (19 quires), validate-prose stripped, 12 UI tests enabled, seed 1886680 |
| 1122 | run-with-disk.ps1 rewritten to use Start-QemuRun + ExtraArgs |
| 1123 | build-boot-img.ps1 uses compiler IMG mode (1 VM, eliminates make-efi/make-usb-image) |
