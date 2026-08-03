# Build Tooling Migration: PS1 to Codex

## Goal

The bootable IMG ships with source, compiler, editor, and shell. A
developer can compile, test, and deploy Codex programs from bare metal
without any host tooling. PowerShell remains as a thin VM orchestration
layer on the host; all computation moves into Codex.

## Current State (verified against the tree, 2026-07-19)

The migration is roughly two-thirds done. Disk-compile mode works and now
finds its volume from the disk's GPT rather than assuming LBA 2048. The
FileSystem builtins are real. The on-device test runner and pingpong do
not exist yet.

### What exists in Codex

| Component | Location | Status |
|---|---|---|
| Cite resolution (serial) | `codex/compiler/opening.codex` -- `load-cited-foreword` | **Live.** Called on the serial path. (An older revision of this doc called it dead code. It is not.) |
| Cite resolution (disk) | `codex/compiler/opening.codex` -- `disk-resolve-forewords`, `disk-load-cite`, `disk-extract-cites` | **Live.** Resolves `cites` transitively from the FAT16 volume, deduplicating by a seen-set. |
| DISK compile mode | `codex/compiler/opening.codex` -- `emit-from-disk`, dispatched at `if cmd == "DISK"` | **Shipped.** Reads a path from stdin, mounts the FAT16 volume found from the disk's GPT (`fat16-boot-volume`), reads the source, resolves cites from disk, compiles. |
| Quire-to-path mapping | `codex/compiler/opening.codex` -- `quire-to-dir` | Works. |
| FAT16 reader | `codex/foreword/core/Fat16.codex` | `fat16-init`, `fat16-read-file`, `fat16-file-exists`, `fat16-list-dir`, `fat16-read-text`. |
| FAT32 | `codex/foreword/core/Fat32.codex` | Exists. |
| GPT | `codex/foreword/core/Gpt.codex` | Exists. |
| Block I/O | `codex/os/kernel/DiskFacts.codex` | `block-read-sector`, `block-write-sector` -- raw 512-byte sectors. |
| Editor | `codex/foreword/ui/Editor.codex` | Exists. |
| Shell | `codex/os/core/ShellCore.codex`, driven by `codex/test/apps/codex-shell.codex` | Exists. |
| CDX signing | `codex/compiler/opening.codex` | Ed25519 sign via inline program. |
| Container formats | `codex/plugs/{pe,elf,img}/` | PE, ELF, and GPT/FAT disk images are produced by **plug CDX binaries**, not by the compiler. The old `codex/Emit/PeWriter.codex` and `Fat32Writer.codex` no longer exist. |

### The two stubs that lied: both gone (verified 2026-07-19)

An earlier revision of this doc named `read-file` and `file-exists` as
stubs that lie: `read-file` reading serial instead of disk, and
`file-exists` emitting `li rd, 1` so it answered `True` for every path.
Both are fixed, and the fix went further than this section asked for.

| Name | Where it is now |
|---|---|
| `file-exists` | A real FAT16 lookup in Foreword chapter `Fat16`. The emitter is deleted; `X86_64Builtins.codex` records why. |
| `read-file` | The builtin is **deleted** (blu, CL 9092). It read serial and discarded its path argument. 25 transpiler emitters were retargeted to `read-text`. |
| `list-files`, `list-directories` | Real, in `Fat16`. Gone from `builtin-names` and the type environment, so a chapter that does not cite `Fat16` gets CDX3002 rather than a lie. |
| `write-file`, `write-binary-file` | Real, in `Fat16`. FAT16 is no longer read-only: it allocates a cluster, chains it, writes the bytes and commits a directory entry, in Codex over `block-write-sector`. |

The general rule those changes settled: a name the compiler cannot keep
does not belong in the emitter. `write-file` used to print its content to
the console and report success, which is silent data loss.

### What still exists only in PS1

| Script | What it does | Migration path |
|---|---|---|
| `build/concat-codex-self.ps1` | Concatenate compiler source with quire prefixes | Superseded on the disk path by `disk-resolve-forewords`; still used by the serial path. |
| `build/compile.ps1` | Boot VM, resolve cites, feed source over serial | Keep the VM orchestration; the cite resolution is already in the compiler. |
| `build/test.ps1` | Parallel test runner | On-disk test runner (Phase 4 below -- not started). |
| `build/build.ps1` | Fixed-point verification (text + CDX pingpong) | On-disk self-compile + byte-compare (Phase 5 below -- not started). |
| `build/build-record.ps1` | Hash + JSON provenance | Sha256 + Json forewords exist. |
| `build/gpu-dispatch` bridge | Serial-to-GPU dispatch | The polled serial bridge should become a virtqueue device. |

### What stays as PS1 forever

- `build/vm-config.ps1` -- VM process management, port allocation.
- `build/clean-zombies.ps1` -- kill orphaned VM processes.
- `build/build-gpu-dispatch.ps1` -- CUDA/nvcc invocation.

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

Disk-compile mode -- **shipped**:

```
PS1: send "DISK path.codex\n" -> serial -> compiler reads disk -> binary -> serial
```

On-device mode -- the destination, not yet reached:

```
Shell: compile path.codex -> compiler reads disk -> binary -> disk
```

## Remaining Plan

### Phase A: Wire the FileSystem builtins to FAT16 -- DONE (2026-07-19)

The builtins are real (table above). The last piece was the partition
offset, and it is closed.

**The volume start was hardcoded 2048 in three places** -- the compiler's
`disk-partition-start`, `Fat16`'s `fat16-boot-partition-start`, and
`DevConsoleBoot`'s `uefi-partition-start`. That constant is only ever
right for an image our own `GptWriter` laid down: it puts its single ESP
at LBA 2048 and nothing else does. A stick partitioned elsewhere, a
vendor ESP, or our own dual-boot install (`DriveManager` places its Codex
partition after the last existing one, at a computed offset) all put the
volume somewhere else, and every read landed on the wrong sectors and
parsed garbage as a boot record.

`fat16-boot-volume` now resolves it from the disk's own GPT. **The
partition is chosen by whether it parses, not by its type GUID** -- a
dual-boot disk carries a FAT32 vendor ESP and a FAT16 Codex partition and
both are "the boot partition" by some reading, so the volume is asked
rather than guessed. `fat16-init` is total and `fat16-vol-is-usable` is
the existing judgement, so the walk takes the first partition that answers
yes. It cannot pick a partition it then fails to read.

2048 survives as `fat16-fallback-partition-start`, for a disk with no
readable GPT at all. Measured: with no disk, `gpt-read` answers `None`
and the fallback gives exactly the old behaviour; with a disk, the real
LBA. Our own images are unchanged, because their one ESP is at 2048 and
it parses.

**A divide-by-zero in `Gpt.codex` was found by wiring this up.**
`gpt-read-entries` computes entries-per-sector as `512 / entry-size` with
`entry-size` read straight off the disk. A header claiming zero faults
the machine with `!EXC=00`; one claiming more than 512 makes that
quotient zero and the next division faults the same way. `gpt-read-after-hdr`
now refuses the header instead, which is the rule `fat16-parse-bpb`
already applied to bytes-per-sector.

Still hardcoded: `DevConsoleBoot`'s `uefi-partition-start`, used by
`DriveManager` for the **source** volume in a dual-boot copy. That is a
different question (which disk you are copying *from*) and is not part of
this phase.

Decision on record: **dual-path FileSystem.** Serial-feed for VM
compilation, disk for on-device, selected by the mode header or by
detecting a disk. That decision stands and is now implemented on the disk
side.

### Phase B: On-disk test runner -- NOT STARTED

A Codex app that lists `codex/test/*.codex` from FAT16, compiles and
runs each, reads the `.expected` / `.failing` / `.skip` sidecars, diffs,
and prints a summary.

The open question is how one program runs another. Recommendation on
record: in-process, with `heap-save` / `heap-restore` between tests --
the compiler already uses exactly that pattern at phase boundaries.

### Phase C: Self-hosted pingpong -- NOT STARTED

Read the compiler source from disk, compile to `stage1.cdx`, use
`stage1` to compile the source again to `stage2.cdx`, byte-compare.

Step 3 requires loading and executing a compiled CDX -- a bare-metal
binary -- which means jumping to its entry point with a fresh stack and
heap. That is essentially a reboot into the new binary, and it is the
hard part.

Simpler intermediate: TEXT round-trip twice (string comparison, no
binary loading). That proves the emitter is a fixed point but not the
binary.

### Phase D: Editor and shell on the boot image -- PARTIAL

Both exist as chapters (`codex/foreword/ui/Editor.codex`,
`codex/os/core/ShellCore.codex`). What is missing is their integration
into the booted image as the default path: open a file, edit it, compile
it, run it, without leaving the machine. See `docs/PM/CurrentPlan.md`
gap 7.

## Priority Order

1. ~~Wire the FileSystem builtins~~ -- done 2026-07-19, Phase A above.
2. **On-disk test runner.**
3. **Text pingpong on device.**
4. **Editor and shell as the default on-image path.**
