# Build Tooling Migration: PS1 to Codex

## Goal

The bootable IMG ships with source, compiler, editor, and shell. A
developer can compile, test, and deploy Codex programs from bare metal
without any host tooling. PowerShell remains as a thin VM orchestration
layer on the host; all computation moves into Codex.

## Current State (verified against the tree, 2026-07-13)

The migration is roughly two-thirds done. Disk-compile mode works; the
on-device test runner and pingpong do not exist yet; and two FileSystem
builtins are still stubs that quietly lie.

### What exists in Codex

| Component | Location | Status |
|---|---|---|
| Cite resolution (serial) | `codex/compiler/opening.codex` — `load-cited-foreword` | **Live.** Called on the serial path. (An older revision of this doc called it dead code. It is not.) |
| Cite resolution (disk) | `codex/compiler/opening.codex` — `disk-resolve-forewords`, `disk-load-cite`, `disk-extract-cites` | **Live.** Resolves `cites` transitively from the FAT16 volume, deduplicating by a seen-set. |
| DISK compile mode | `codex/compiler/opening.codex` — `emit-from-disk`, dispatched at `if cmd == "DISK"` | **Shipped.** Reads a path from stdin, mounts FAT16 at sector 2048, reads the source, resolves cites from disk, compiles. |
| Quire-to-path mapping | `codex/compiler/opening.codex` — `quire-to-dir` | Works. |
| FAT16 reader | `codex/foreword/core/Fat16.codex` | `fat16-init`, `fat16-read-file`, `fat16-file-exists`, `fat16-list-dir`, `fat16-read-text`. |
| FAT32 | `codex/foreword/core/Fat32.codex` | Exists. |
| GPT | `codex/foreword/core/Gpt.codex` | Exists. |
| Block I/O | `codex/os/kernel/DiskFacts.codex` | `block-read-sector`, `block-write-sector` — raw 512-byte sectors. |
| Editor | `codex/foreword/ui/Editor.codex` | Exists. |
| Shell | `codex/os/core/ShellCore.codex`, driven by `codex/test/apps/codex-shell.codex` | Exists. |
| CDX signing | `codex/compiler/opening.codex` | Ed25519 sign via inline program. |
| Container formats | `codex/plugs/{pe,elf,img}/` | PE, ELF, and GPT/FAT disk images are produced by **plug CDX binaries**, not by the compiler. The old `codex/Emit/PeWriter.codex` and `Fat32Writer.codex` no longer exist. |

### The two stubs that still lie

These are the sharpest edges in this design, and they are easy to miss
because the disk path works *around* them rather than through them.
`emit-from-disk` calls `fat16-read-text` directly; it never goes through
the generic FileSystem builtins. So DISK mode works while these remain
broken:

| Builtin | Emitter | What it actually does |
|---|---|---|
| `read-file` | `emit-read-serial-cce-builtin` (`codex/compiler/Emit/X86_64Builtins.codex`) | **Reads from serial, not from disk.** A program calling `read-file` on bare metal does not touch the filesystem. |
| `file-exists` | `emit-file-exists-builtin` (same file, ~line 562) | **Emits `li rd, 1`. It unconditionally returns `True`** — for every path, existing or not. |

A `[FileSystem]`-effected program therefore compiles, type-checks, and
runs while its two most basic operations do something other than what
they say. Wiring these to FAT16 is the highest-value item left in this
design, and it is a correctness issue, not just a migration chore.

### What still exists only in PS1

| Script | What it does | Migration path |
|---|---|---|
| `build/concat-codex-self.ps1` | Concatenate compiler source with quire prefixes | Superseded on the disk path by `disk-resolve-forewords`; still used by the serial path. |
| `build/compile.ps1` | Boot VM, resolve cites, feed source over serial | Keep the VM orchestration; the cite resolution is already in the compiler. |
| `build/test.ps1` | Parallel test runner | On-disk test runner (Phase 4 below — not started). |
| `build/build.ps1` | Fixed-point verification (text + CDX pingpong) | On-disk self-compile + byte-compare (Phase 5 below — not started). |
| `build/build-record.ps1` | Hash + JSON provenance | Sha256 + Json forewords exist. |
| `build/gpu-dispatch` bridge | Serial-to-GPU dispatch | See `docs/PM/BACKLOG.md` §4.6 — the polled serial bridge should become a virtqueue device. |

### What stays as PS1 forever

- `build/vm-config.ps1` — VM process management, port allocation.
- `build/clean-zombies.ps1` — kill orphaned VM processes.
- `build/build-gpu-dispatch.ps1` — CUDA/nvcc invocation.

These launch or kill host processes. They are the boundary, not the
work.

## Architecture

```
+--------------------------------------------------+
|  Host (Windows)                                   |
|  PS1: VM launch, port management, serial I/O      |
+--------------------------------------------------+
        |  serial / disk image
        v
+--------------------------------------------------+
|  Bare Metal (codex-vm or real hardware)           |
|                                                   |
|  Shell ──> Compiler ──> Test Runner (not built)   |
|    |          |                                   |
|    v          v                                   |
|  Editor   FAT16 Reader                            |
|              |                                    |
|              v                                    |
|  Block I/O (ATA IDE)                              |
+--------------------------------------------------+
```

### Compilation modes

Serial-feed mode (the host resolves cites and sends everything):

```
PS1: concat source + forewords -> serial -> compiler -> binary -> serial -> PS1
```

Disk-compile mode — **shipped**:

```
PS1: send "DISK path.codex\n" -> serial -> compiler reads disk -> binary -> serial
```

On-device mode — the destination, not yet reached:

```
Shell: compile path.codex -> compiler reads disk -> binary -> disk
```

## Remaining Plan

### Phase A: Wire the FileSystem builtins to FAT16 — NOT STARTED

Replace the two stubs above so `read-file` and `file-exists` mean what
they say. The compiler needs the partition offset; `emit-from-disk`
hardcodes `disk-partition-start = 2048`, which is the value to
generalize.

Decision on record: **dual-path FileSystem.** Serial-feed for VM
compilation, disk for on-device, selected by the mode header or by
detecting a disk. That decision stands; it is the implementation that is
missing.

### Phase B: On-disk test runner — NOT STARTED

A Codex app that lists `codex/test/*.codex` from FAT16, compiles and
runs each, reads the `.expected` / `.failing` / `.skip` sidecars, diffs,
and prints a summary.

The open question is how one program runs another. Recommendation on
record: in-process, with `heap-save` / `heap-restore` between tests —
the compiler already uses exactly that pattern at phase boundaries.

### Phase C: Self-hosted pingpong — NOT STARTED

Read the compiler source from disk, compile to `stage1.cdx`, use
`stage1` to compile the source again to `stage2.cdx`, byte-compare.

Step 3 requires loading and executing a compiled CDX — a bare-metal
binary — which means jumping to its entry point with a fresh stack and
heap. That is essentially a reboot into the new binary, and it is the
hard part.

Simpler intermediate: TEXT round-trip twice (string comparison, no
binary loading). That proves the emitter is a fixed point but not the
binary.

### Phase D: Editor and shell on the boot image — PARTIAL

Both exist as chapters (`codex/foreword/ui/Editor.codex`,
`codex/os/core/ShellCore.codex`). What is missing is their integration
into the booted image as the default path: open a file, edit it, compile
it, run it, without leaving the machine. See `docs/PM/CurrentPlan.md`
gap 7.

## Priority Order

1. **Wire the FileSystem builtins** — a correctness bug, not just
   migration debt. Everything on-device depends on reading files
   honestly.
2. **On-disk test runner.**
3. **Text pingpong on device.**
4. **Editor and shell as the default on-image path.**
