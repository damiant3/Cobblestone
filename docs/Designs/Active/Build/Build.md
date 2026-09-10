# Build Tooling Migration: PS1 to Codex

## Goal

The bootable IMG ships with source, compiler, editor, and shell. A developer
can compile, test, and deploy Codex programs from bare metal without any host
tooling. PowerShell remains as a thin VM orchestration layer on the host; all
computation moves into Codex.

## What exists in Codex

| Component | Location |
|---|---|
| Cite resolution (serial) | `codex/compiler/opening.codex` -- `load-cited-foreword` |
| Cite resolution (disk) | `codex/compiler/opening.codex` -- `disk-resolve-forewords`, `disk-load-cite`, `disk-extract-cites`, transitive, deduplicated by a seen-set |
| DISK compile mode | `codex/compiler/opening.codex` -- `emit-from-disk`, dispatched at `if cmd == "DISK"` |
| Quire-to-path mapping | `codex/compiler/opening.codex` -- `quire-to-dir` |
| FAT16 | `codex/foreword/core/Fat16.codex`, including subdirectories and a full VFAT long-name implementation |
| FAT32, GPT | `codex/foreword/core/Fat32.codex`, `Gpt.codex` |
| Block I/O | `codex/os/kernel/DiskFacts.codex` -- raw 512-byte sectors |
| Editor, shell | `codex/foreword/ui/Editor.codex`, `codex/os/core/ShellCore.codex` |
| CDX signing | `codex/compiler/opening.codex` -- Ed25519 via inline program |
| Container formats | `codex/plugs/{pe,elf,img}/` -- plug CDX binaries, never the compiler |

**A name the compiler cannot keep does not belong in the emitter.** `write-file`
used to print its content to the console and report success, which is silent
data loss. `file-exists`, `read-file`, `list-files` and `list-directories` were
all removed from `builtin-names` and the type environment, so a chapter that
does not cite `Fat16` now gets CDX3002 rather than a lie.

### What still exists only in PS1

| Script | Migration path |
|---|---|
| `build/concat-codex-self.ps1` | superseded on the disk path by `disk-resolve-forewords`; still used by the serial path |
| `build/compile.ps1` | keep the VM orchestration; the cite resolution is already in the compiler |
| `build/test.ps1` | the on-disk test runner, Phase B |
| `build/build.ps1` | on-disk self-compile and byte-compare, Phase C |
| `build/build-record.ps1` | Sha256 and Json forewords exist |
| `build/gpu-dispatch` bridge | the polled serial bridge should become a virtqueue device |

### What stays as PS1 forever

`build/vm-config.ps1`, `build/clean-zombies.ps1`, `build/build-gpu-dispatch.ps1`.
These launch or kill host processes. They are the boundary, not the work.

## The Windows-dependency ledger (Damian's direction 2026-08-14)

**The question is not which scripts have a generator. It is which ones have to
survive when Windows becomes a dependency we drop.** A `codex/build/*Script.codex`
authors its logic in Codex and **emits PowerShell**, so the artifact that runs is
still a `.ps1` and still needs a host. Generators are single-source-of-truth for
the host-side build. They are not a path off the host.

The work sorts into four outcomes and only the first is a port.

1. **LIFT.** Real work that survives, with no witness role. It becomes a Codex
   program that runs natively, not a generator that emits a script.
2. **KEEP FOREIGN.** Being outside Codex is load-bearing.
3. **EVAPORATES.** The script exists only to bridge something PowerShell cannot
   do and Codex OS does natively. It is deleted, not ported.
4. **DIES WITH WHAT IT DRIVES.** Perforce, Renode, USB flashing, VM launch.

**Classify each script against the four BEFORE writing any Codex**, and quote
its header if it claims independence.

### Outcome 2 is the trap, and it is the expensive one

**A script whose value is INDEPENDENCE cannot be ported into Codex without
destroying the thing it was for.** Porting it citing our own chapters turns a
control into a tautology, and the port still passes, so running it would not
catch this. The ones that say so in their own headers:

- `make-fat16-subdir.ps1`, written from the FAT16 spec rather than from
  `Fat16.codex`: a fixture built by the code under test proves only that the
  code agrees with itself.
- `oracle-cce.ps1`, `oracle-scalar.ps1`, `oracle-vector.ps1`: every answer is
  adjudicated by the HOST's tables, never by another Codex answer.
- `fat16-walk.ps1`, the independent reader for returned-flight evidence. It
  reads a FILE and never a device, because mounting a FAT volume lets Windows
  write `System Volume Information` into it, which allocates clusters, which is
  the exact evidence its third question asks about.
- `mint-factlog-fixture.ps1`, authored from the FactLog spec rather than by
  running DiskFacts.
- `brotli-tables-verify.ps1` and its five siblings, which check our tables
  against RFC 7932's PUBLISHED bytes and their CRC-32s.
- `qr-decode-test.ps1`, which turns a photograph back into bytes. Judging the
  decoder with our own encoder is the same tautology.
- `ddc-witness.ps1` one level up: the Roslyn arm IS the witness, and a Codex
  reimplementation of it witnesses nothing.

**When Windows goes, these do not become Codex programs. They need a different
foreign host.** That is open and is not answered here; naming it is the point,
because the alternative is discovering it by porting an oracle into its own
subject.

### Outcome 3, which is easy to miss

`cce-grep.ps1` exists because `Select-String` over a CCE file returns ZERO
MATCHES rather than failing, which reads exactly like "the thing is not in
there". On Codex OS, CCE is the native encoding and grep is grep. **Look for this
shape before porting anything: a script that reimplements one of our own formats
in PowerShell is usually outcome 3, not outcome 1.**

### The classifier is the artifact; the count is downstream of it

Three re-measurements of the same "pure computation" list disagreed with each
other on both the count and the membership, because each tested a different
marker set. Scripts that shell out to `compile.ps1`, launch a browser, or open
an `HttpListener` fall through it and read as pure. **Read the script before
believing the row**, and quote a number from here only with the marker set that
produced it (L-COUNT).

**Outcome 1 is empty in the pure-computation class.** What is left there is
outcome 2, outcome 3, a classifier miss, or the `plug-ports` decision:
`build/plug-ports.ps1` is the port table itself, dot-sourced by its readers, and
generating it moves the single source of truth into a `.codex`, which is a
decision rather than a port. **The next lifting work is the `codex-vm` set, and
every one of those needs Codex OS to have the capability the VM stands in for.**

**A lift is verified against the shipped script on a clean tree AND on a control
tree firing every message path**, because two arms agreeing on a PASS is two
instruments agreeing about nothing. Four of the nine lifts shipped a defect fix
that A/B surfaced and reading had not, including a `-Score` report that had
never printed its table: its two report lines are bare format expressions, so
they went to the OUTPUT stream and the caller captured them, while the exit code
still worked by accident because `-eq` on an array filters rather than compares.

## Working on a generated script

**A shipped script with a generator is edited THROUGH the generator, whatever
lane you are in.** Before submitting any CL that touches a script a generator
emits (`build/*.ps1`, `codex/plugs/common/plug-build-lib.ps1`, anything
`check-generated-scripts.ps1` lists), run
`build/check-generated-scripts.ps1 -Diff <name>` and land the generator change in
the same CL. If the checker is red, the CL is not done. The case that earned it:
a plugs CL edited `plug-build-lib.ps1` by hand, gated with the ARM64 cross bed
only, and turned `plug-build-lib newly drifted` red for every lane. The cross bed
was the right gate for what the CL did to the plug and the wrong gate for what it
did to the script.

**A generator chapter is a compiled unit.** `build.ps1` runs
`deck-headroom.ps1 -Quire codex\build -WithSelf -MinMargin 1.25`, so every
chapter in this quire is a unit the standing gate measures, seed or no seed. Two
arms proving the OUTPUT is right say nothing about whether the chapter COMPILES
within its deck.

**`-OutRoot <dir>` is how the shipped script catches up with a generator**, by
writing the emitted text to `<dir>/<Chapter>/emitted.txt` for installation, so
the two agree by construction rather than by hand-transcription. `-Diff <name>`
shows the delta; `-Update` records a drift rather than fixing it. **There is
deliberately no `-Write`**, because the shipped script is the maintained side.

**THE GENERAL TRAP: the `build/*.ps1` files are hand-maintained, and the drift
runs the OTHER way from what "generated from" advertises.** On
`lintunusedcitesScript.codex` the generator emits `[Parameter(Mandatory=$true)]`
where the shipped script has none, so the shipped copy is the hand-FIXED one and
regenerating hands back a script that prompts headless. **The instance that costs
a boot: `cdxtopeScript.codex` S06 ends at the `stack-min-rsp-addr` store and
never writes cell 4072**, which is the whole of the fix for the reboot loop under
real UEFI, and S05 still allocates the heap at the fixed `0x1000000` edk2
refuses. Regenerating that one hands back a stub that triple-faults, and no drift
number distinguishes those lines from formatting. **Diff before regenerating.**

**`compile-arm64.ps1` and `compile-riscv.ps1` are the same shape**: generator
abandoned, shipped script maintained. `build/vm-config.ps1` is the opposite and
still matches. Check `-Only <name>` before editing either half of any generated
script.

**A TEXT MATCH IS NOT VERIFICATION.** `compile-arm64` sat at 58 drift lines that
all read as convention while its generator emitted load address 0x40000000 for
0x40100000, heap reserve 0x1000000 for 0x0F000000, align 0x10000 for 0x1000, and
a CCE name decoded one byte per character. **It exits 0 and writes a broken ELF.**
Compile a real subject through the pipeline before and after installing, require
the artifact to be byte-identical, then run the PRE-fix emission as a control and
require it to diverge (L-FALSIF).

**Diff the DATA a generator carries, not just its code shape.** The `bvt`
generator's test list held 16 of the 75 tests, so adopting it unread would have
dropped 59 from the gate while the BVT went on printing PASS. Nothing in the
drift number said so: 239 lines of drift looks like any other number.

**Count phases, not lines, before judging a generator a stub.** The DSL packs a
phase into one line, so a 39-line generator against an 816-line script is not a
placeholder.

### What the drift check decides, and what the byte arm decides

**The drift comparison decides STATEMENTS, not bytes.** It trims every line and
drops the empty ones on both sides, so an added blank line and a re-indented
comment score zero: measured on its own logic, a script against itself plus one
blank line is `delta 0` and one changed statement is `delta 2` (L-GAP).

**The byte arm beside it compares the emitted text again with blanks and
indentation included, EOL normalised.** Its record is
`build/generated-scripts-bytes.txt`, one row per generator with its shipped and
emitted line counts, its indent-differing line count and a derived cause. **No
row is a content difference.** It fails on a generator not in the record, on a
recorded difference whose numbers move, and on a recorded generator that has
become byte-identical, so the residue shrinks as each is repaired.

**`-UpdateBytes` writes only that residue.** `-Update` also rewrites the drift
baseline and the hand-written inventory, and the inventory carries scripts other
lanes have just added, so a lane repairing one byte difference with `-Update`
records decisions that are not its to make.

**`check-pipe-verdicts.ps1` holds a stage's declared `PoExit` codes against the
`ScExit (SeInt N)` values its body spells**, and nothing checked them until two
generators shipped a code their scripts never exit. It reads the GENERATOR, not
the emitted script, because a text census of `exit N` over PowerShell is not
decidable: `exit` occurs in prose and strings, and a real code can reach `exit`
through a variable. An `exit` inside a `ScRaw` payload is unreadable to the
model, so such a generator is exempt from the failing arm and reported instead;
the arm grows as `check-shell-raw` shrinks the payloads. It reads text and boots
nothing.

**The parse class is closed and gated.** `check-generated-scripts.ps1` compiles
every generator, dead target or not, and hard-fails on PowerShell parse errors
with NO baseline. `build/generated-scripts-baseline.txt` is empty, so a drift of
any size in any generator fails the gate and there is no residue for a new one to
hide in.

**THE BANNER'S PRESENCE IS AN INVARIANT, NOT DECORATION.** Every generated script
opens with `generated-banner` from `ShellTypes`, ONE definition cited by all
three emitters. **A file carrying the banner is exactly a file that currently
matches its generator.** A script whose shipped copy is the maintained side must
NOT be given the banner by hand: it would be a lie on the only copy anyone edits.

### The inventory: the drift that runs the other way

Everything above reads the tree GENERATOR-FIRST, so it is structurally blind to
the commoner failure: **an agent writes a new `build/*.ps1` and never writes the
`.codex` that emits it.** `check-generated-scripts.ps1` enumerates `build/*.ps1`,
subtracts everything a generator claims, and compares the remainder against
`build/handwritten-scripts.txt`. Only a name absent from that file prints, and
the inventory NEVER changes the exit code.

**Report-only is a decision, not an omission.** Most scripts under `build/` are
meant to have no generator: probes, flight arms, interop harnesses, mint-fixture
one-offs, and `check-generated-scripts.ps1` itself, which has to run when the
generators are broken. "Every script needs a generator" is not the policy, so a
gate here would be dozens of reds whose answer is always the expected one, which
is the reader-training failure the drift baseline was built to avoid. Answer it
by writing the `.codex`, or by recording the name with `-Update`.

**A generator with no live target is compiled but never compared**, so a defect
in it survives indefinitely and the parse check cannot reach it: two adopted
generators emitted scripts that could only ever exit 2. Give a new generator its
target in the same change. The gate now REFUSES an undeclared missing target;
`$NoTargetByDesign` carries the single deliberate entry with its reason.

### What CHECK-RESOLVE is bound by, because it is not size

`checkdoccounts` at 101,042 bytes needed 52 of 64 where `vmconfigScript` at
116,795 needs 46: unit length **anti**-correlates. CHECK-RESOLVE is the resolve
tail and it tracks the resolved ENVIRONMENT, so **the count of names bound at top
level is what costs.** Measured over identical body text at four section
granularities, required scale moved six points while unit length moved less than
one per cent.

Raising `deck-scale-min` is the wrong fix: it is a whole-corpus constant and
would spend every unit's headroom to cover one author's style. **Restructuring a
generator is safe to do aggressively**, because `match / 0 drift` after the
change is proof the emitted script is unchanged.

There is a floor. Fully inlined, one chapter reached a single 25,800-character
line; keeping one binding per section **costs** two points of deck and buys the
file back. **Say which kind of chapter you have before copying either choice:** a
hand-written chapter pays for readability, a DERIVED one (the workflow is to
change the `.ps1` and re-run a transformer) does not, so line length costs less
there.

**A large derived generator is written by a transformer, not typed.**
`cdx-to-pe.ps1` is 1121 lines of hand-assembled machine code and `build-img.ps1`
527 lines of GPT and FAT structure; hand-copying either into Codex string
literals is where a wrong nibble gets in and nothing downstream would catch it.
Escaping is only `\` and `"`. **The arm that makes it safe is byte-identity of
the ARTIFACT across every flag path, with the hashes required to differ from each
other**, so the comparison distinguishes the paths rather than passing on
everything.

### The emitter traps, which are one family

**The DSL will emit an expression into a position that needs grouping, and
nothing complains until it runs.** Fix these at the CALL SITE, not in
`emit-ps-expr`: parenthesising there would drift every matching generator at
once.

- `SeProperty` emits `obj.prop` unparenthesised, right for `SeVar` and wrong for
  a raw command.
- `ScSetContent` emits its path expression unparenthesised, so a compound path
  produces `Set-Content -Path Join-Path $x '.y' -Value ...`, which does not run.
- `ScCopy` and `ScMkdir` the same: `New-Item -ItemType Directory -Force
  Split-Path $Stage0` is not a directory named `Split-Path`, it is a hard error,
  and under `$ErrorActionPreference = 'Stop'` the script dies on its second
  statement.
- **`ScForEach` over a `SeRaw` command call binds the WHOLE returned array to the
  loop variable.** A cmdlet streams; a function returning a collection does not.
  Use `SeCallArgs`, which parenthesises.
- `SeRaw` emits ONE paren pair where `SeOr` and its siblings add their own, so
  `ScIf (SeRaw ...)` gives `if (...)` and `ScIf (SeOr ...)` gives `if ((...))`.

Other node traps, each paid for once:

- **`ScForLoop` cannot express an increment.** Its step is emitted verbatim, so
  `SeAdd (SeVar "i") (SeInt 1)` produces `for (...; ...; ($i + 1))`, which
  evaluates and discards: an infinite loop, not a wrong answer, so no arm returns
  to report it. `SeRaw "$i++"` is the step that works.
- **`ScWriteError` emits `[Console]::Error.WriteLine`, where `Write-Error` raises
  a TERMINATING error under `Stop`**, so `Write-Error '...'; exit 2` never
  reaches the `exit`. The two are not interchangeable and the DSL cannot express
  `Write-Error` at all, which is why those sites are `ScRaw`.
- **`ScEcho` emits `Write-Host`, not `Write-Output`.** Different destinations,
  which matters when a script's output IS its result.
- `ScEchoStyled` carries the colours. Two traps in one node: its Boolean is
  "newline?", so `False` emits `-NoNewline`, and `ClRed` maps to `DarkRed`; the
  bright console colours are `ClBrightRed` / `ClBrightGreen`.
- **A Codex text literal cannot span lines.** A multi-line emitted block is
  written as SEPARATE `ScRaw` entries, one per emitted line.
- **A `let` binding's value must start on the SAME line.** A multi-line list
  literal is fine for a top-level definition; `let name =` followed by a newline
  raises CDX2000. Use a section-level definition.
- **Codex reads `\` as an escape introducer.** A generator writing `'\.'` emits
  `'.'`, a dot matching any character. The generator source needs `\\.`.
- **A generator's column-2 prose is NOT emitted.** A comment the shipped script
  needs must be emitted like any other line, and every `Sc*` node pads with
  `ps-indent indent`, so an INDENTED comment comes out indented only if it is
  emitted from inside the scriptblock that indents it (`ScRaw` at that level),
  not appended to the top-level phase list.
- **The emitted text is LF and the shipped scripts are CRLF.** Install without
  converting and `p4 diff` reports the whole file as changed, which buries a
  five-line change and turns the next merge into a conflict (P-EOL).
- **`-Update` writes two records and either may be read-only under Perforce.** It
  now writes only what changed and says so.
- **CHECK `ShellTypes` BEFORE ADDING A NODE.** A prior plan asserted the DSL had
  no `-ForegroundColor` node; `ScEchoStyled`, `ScEchoPartial` and `emit-ps-color`
  already existed.
- **Read the `sh-script "<name>"` line, not the filename**, before overwriting a
  generator: `build-img` is emitted by `buildimgScript.codex`, not
  `buildbootimgScript.codex`, and writing into the wrong one destroys a working
  generator.
- **A generator with no `$AltTarget` entry reads as having no target.** The
  target name in `sh-script` is not the path; before calling any generator dead,
  READ IT and look for the script it describes.
- **A PowerShell hex literal parses SIGNED**, so `0xFFFFFFFF` is int -1, a mask
  written to normalise a negative exit code is a no-op, the `[uint32]` cast
  THROWS, and in a loop the failed assignment leaves the comparison reading a
  STALE value. Compare the signed literal directly.
- **A hashtable walked in bucket order prints differently run to run**, because
  .NET randomises string hashing per process. The verdict never moves; the text
  does, which is enough to make a failure nobody can diff, and a green run never
  shows it. Sort any key loop that builds output.
- **A `List` returned from a PowerShell function is UNROLLED by the caller**, so
  `$x.Count` dies whenever the collection holds exactly one element. `@()` around
  the call is the fix.
- **`codex/test/shell-build-keep` is the test any Shell quire change touches.**
  The wider net is `check-generated-scripts.ps1` with no `-Only`.
- **The Shell quire is not in the seed unit.** `concat-codex-self.ps1` preloads
  `codex\foreword\core` only, so a `codex\foreword\shell` change moves no seed
  byte and takes no build token. Confirm it at the concat rather than reasoning
  from the directory name.
- **Losing a script's header comments is not automatically a loss, but check.**
  Grep for the doctrine's other home BEFORE installing over it.

**Do not bulk-regenerate.** Read the drift with `-Diff <name>`, judge each
shipped-only line as real behaviour or emitter convention, port only the former,
then INSTALL the emitted script and accept the style change.

## What `gen-scripts` costs, and the fix that does not pay

`check-generated-scripts.ps1` compiles the whole set in ONE batch VM boot and
then RUNS them one VM boot at a time in a serial loop, which reads like the
classic serial-harness win. It is not: measured, pre-running the loop at
`-ThrottleLimit 8` saved 9.5 s, because the serial runs are about 0.2 s each and
the batch compile of the generator chapters is the rest. That is genuine compile
work and the only way down is fewer or smaller generators. The change was
reverted; do not spend the afternoon on it again.

**Do not call `test-run.ps1` with `&` inside `ForEach-Object -Parallel`.**
Runspaces share one process, that script does `Set-Location` and sets
`[Environment]::CurrentDirectory`, and eight of them fight over one working
directory. The battery spawns `pwsh -NoProfile -File` per test and never sees it.
Spawn the process; the race disappears.

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
|  Shell ──> Compiler ──> Test Runner               |
|    |          |                                   |
|    v          v                                   |
|  Editor   FAT16 Reader                            |
|              |                                    |
|              v                                    |
|  Block I/O (ATA IDE)                              |
+--------------------------------------------------+
```

Serial-feed mode: the host resolves cites and sends everything. Disk-compile
mode, shipped: `DISK path.codex\n` over serial, the compiler reads the disk.
On-device mode, the destination: the shell compiles from disk to disk.

**Decision on record: dual-path FileSystem.** Serial-feed for VM compilation,
disk for on-device, selected by the mode header or by detecting a disk.

## Phase A: the FileSystem builtins -- DONE

**The volume start is resolved from the disk's own GPT, not hardcoded.** 2048 is
only ever right for an image our own `GptWriter` laid down; a stick partitioned
elsewhere, a vendor ESP, or a dual-boot install all put the volume somewhere
else, and every read landed on the wrong sectors and parsed garbage as a boot
record. **The partition is chosen by whether it PARSES, not by its type GUID**,
because a dual-boot disk carries a FAT32 vendor ESP and a FAT16 Codex partition
and both are "the boot partition" by some reading. 2048 survives as
`fat16-fallback-partition-start` for a disk with no readable GPT.

`gpt-read-after-hdr` refuses a header whose entry size is zero or above 512,
which used to fault the machine with `!EXC=00` through a divide by zero.

**Still hardcoded: `DevConsoleBoot`'s `uefi-partition-start`**, used by
`DriveManager` for the SOURCE volume in a dual-boot copy. That is a different
question and is not part of this phase.

## Phase B: on-disk test runner

`apps/works/DiskTestRunner.codex` mounts the boot volume, walks the root and one
level of subdirectories, and compiles each file it finds IN PROCESS, printing a
verdict per file and a summary.

**One program compiles another with no second boot.** The runner cites
`Codex chapter Opening` and `Codex chapter Diagnostic Bag` and calls
`compile-frontend-cdx` and then `compile-to-cdx`, so a chapter whose frontend is
clean and whose codegen is not is not indistinguishable from a clean one, and
each subject reports the size of the CDX it produced. **The size is checked
against the host**, which is what makes it evidence rather than a number: the
same subjects compile to the same byte counts in process and through
`build/compile.ps1`.

Every subject is compiled between `__heap-save` and its restore, because a
bare-metal heap has no collector and without the restore the run length is
bounded by memory rather than by the corpus. Only Integers cross the restore.
**The heap arm has not fired and is recorded as not fired.**

**The verdict can say no** (L-FALSIF): an error fixture reports
`errors=1 ok=no first=CDX2003`, matching what its `.failing` sidecar records. A
read that failed and a chapter that would not compile are kept apart, because a
summary that merges them cannot say which happened.

**RULED (root, 2026-09-08): the runner runs tests in-process, over a citable
compiler.** `codex/compiler/EntryPoint.codex` is the thin entry chapter;
`Chapter: Opening` declares `codex-opening` and no `opening` at all, so it is
citable by construction. The alternatives, for the record: load a compiled CDX
and jump to its entry with a fresh stack and heap, which is the hard part; or let
the host reboot the compiler once per test, which is `test.ps1` today and buys no
capability.

**RULED (root, 2026-09-09): a quire is a DIRECTORY or a MANIFEST.**
`build/quire-map.ps1` carries `$QuireManifests` beside `$QuireDirs`, with
`Codex`, `Emit` and `Semantics` all registered as `build/compiler-order.txt`, so
a cite of any chapter in a manifest quire resolves to the whole ordered unit that
manifest names, once. No renames. Four things the bundler has to get right, each
found by a failing run:

- **The unit's own entry chapter is left out**, because a consumer supplies its
  own `opening` and two in one unit collide (CDX3001).
- **The unit's cites into other quires are walked, and walked FIRST**, or the
  bundle is missing the Foreword and Math chapters the compiler cites.
- **Chapters are marked visited under EVERY manifest quire name**, because the
  compiler cites `Codex chapter Phase Allocator` while that file lives in
  `Core\`. Keyed on the directory instead, the unit is pulled ten times over.
- **No quire prefix inside the unit, and the `$present` check is honoured**, or a
  re-resolve of an already bundled source adds the unit a second time, which is
  what `compile.ps1` does to every bundle it is handed.

**The compiler's concatenation order is EXPLICIT and no longer keyed on
filenames.** `build/compiler-order.txt` carries every compiler file, one
repo-relative row each, and `concat-codex-self.ps1` refuses any disagreement with
the directory: a file with no row, or a row with no file. Both refusals were
ablated and fire, each naming the offender. The proof is the INTERMEDIATE
PRODUCT: the concatenated unit is byte-identical across the change, which is
stronger than comparing the emitted CDX, because identical input to the compiler
is what makes identical output necessary rather than observed.

**`Sort-ByDeps` in that script is defined and called by nothing** (L-UNCALLED).
It is the cite-graph walk this ordering question would otherwise want, and it
cannot serve: its seed is an ordinal name sort, and the order at issue is inside
a single chapter where the cite graph has no edges. Left in place and unused,
named here so the next reader does not mistake it for the mechanism.

**The image can carry the whole of `codex/test`.** The FAT16 root holds 509
usable entries against 646 test chapters, and 60 8.3 names are claimed by more
than one chapter, so a flat image can hold neither. `run.ps1 -Bucketed` places
sources in `SRC0`, `SRC1`, ...: a source goes in the first directory with room
whose 8.3 name it does not already claim, which separates the collisions rather
than refusing them. **A name and a size cannot say which of two colliding files
an entry holds** (L-BOTHARMS), so the arm that settles it is a verdict that
differs: two chapters folding to the same 8.3 name in two directories, one
compiling clean and one raising CDX2003.

**DISK mode honours the path it is given.** It used to read the path line and
compile `disk-default-path` regardless, which is an interface accepting what it
does not honour. `SOURCE.SRC` is now the fallback rather than the only answer and
`build/test-disk-compile.ps1` takes `-DiskPath`, so the behaviour has an arm.

**The img plug takes any number of sources.** The wire header carries a source
COUNT and a table of 8.3 name plus size per source, and the host refuses to
exceed the 509-entry root rather than silently truncating.

### What Phase B still owes

**Execution, and cite resolution for a corpus that is not cite-free.** The runner
compiles a FILE, not a UNIT: measured over twenty subjects, every entry carrying
a `cites` line reports errors and every cite-free entry reports zero, 20 for 20
with nothing on either diagonal. `bundle-app` resolves cites on the HOST before
`test.ps1` compiles anything and the runner has no such step. The runner says so
in its own output once per run, rather than leaving a reader to infer it from a
column of failures.

The two options, both priced:

- **Option A, resolution ON THE GUEST.** The image carries the test chapters and
  the cited library chapters once each; the runner indexes chapters by name,
  walks each test's cites, assembles a unit, and compiles it. Payload is about
  5.5 MB, the library paid once. Cost is a second implementation of the host's
  resolver, which must not drift from it.
- **Option B, bundling ON THE HOST.** `bundle-app`'s output becomes the image's
  entries and the runner needs no resolver at all. **Measured rather than
  extrapolated over twenty subjects: a factor of 10.0**, so the corpus is about
  15.5 MB of image. An earlier extrapolation from eight files said 6.5 and was
  low by half, which is the difference between an extrapolation and a
  measurement. All twenty compile clean on the guest, including the fourteen that
  were red when read raw, and **the control is in the same run and fires**: two
  error fixtures bundled the same way still report `errors=1 ok=no`.

**Option B works end to end today with no guest code**, and a runner over a
CHOSEN SUBSET is buildable now under either option.

**The `.expected` diff needs execution; the `.failing` half does not.** A
`.expected` sidecar holds the PROGRAM'S OUTPUT, so diffing it requires running
what was compiled. A `.failing` sidecar holds a diagnostic CODE and the runner
already has the bag, so a diagnostic-code diff is buildable now. **The pairing
stays on the HOST deliberately**: the sidecars are not on the image, and putting
them there is the same namespace question as the corpus itself.

**The CODEGEN-error branch is written and UNREACHED, and no fixture in the corpus
can reach it** (L-VACUOUS). Both candidates raise through the FRONTEND bag,
because `compile-frontend-cdx` already runs the phase that raises them. That is a
gap in the fixtures rather than a property of the compiler, and the branch stays
because a codegen error reported as `errors=0 ok=yes` would be a silent wrong
answer.

**STAGE 3, executing what was compiled, belongs to
`docs/Designs/Active/OS/DeskBuildLoop.md` (RULED by root, 2026-09-09).** Nothing
in this tree enters compiled bytes in process: `exec-run` in `ShellCore.codex`
prints a grant and runs nothing, and `OsScheduler` dispatches tasks by NAME.
That page's mechanism is a nested guest, and what is missing there is not code
but a machine: no bed has VT-x, so metal is the first machine that can execute
it. **Do not build a second execution path here.**

**An observation, not a claim:** a 5-byte file carrying no `Chapter:` line at all
compiled through `compile-frontend` with errors=0. Seen once in passing. If a
unit with no chapter is genuinely accepted, that is a compiler question and
belongs to the compiler register.

## Phase C: self-hosted pingpong -- NOT STARTED

Read the compiler source from disk, compile to `stage1.cdx`, use `stage1` to
compile the source again to `stage2.cdx`, byte-compare. Step 3 requires loading
and executing a compiled CDX, which is the hard part above. A simpler
intermediate is a TEXT round-trip twice, which proves the emitter is a fixed
point but not the binary.

## Phase D: editor and shell on the boot image -- PARTIAL

Both exist as chapters. What is missing is their integration into the booted
image as the default path: open a file, edit it, compile it, run it, without
leaving the machine.

## Priority Order

1. On-disk test runner.
2. Text pingpong on device.
3. Editor and shell as the default on-image path.

## The gate's trigger map

**A gate runs only the steps the change can affect (Damian, 2026-09-02).** An
apps-only CL must not build the seed, run the compiler BVT, or stride an app
sweep.

```
$tForeword = ^codex/foreword/
$tSeed     = ^seed/
$tKernel   = $tCompiler -or $tForeword -or $tSeed -or $tBuild
$tTest     = ^codex/test/
```

`$tKernel` is deliberately CONSERVATIVE on the foreword: `Foreword--Fat32` is
absent from the compiler unit, so a `Fat32.codex` change moves no seed and fires
for nothing. Asking the concat which chapters are in the unit would make the
trigger exact; **a trigger that is too wide costs time and one that is too narrow
ships a miscompile, and the two errors are not the same size.**

**The hazard that decides whether this is safe at all: every phase after the core
is pointed at a kernel the core BUILT.** Skipping the core leaves those paths
holding whatever the last run left on disk, which is R-GATE's name-the-kernel
trap arriving through a default. So the rule is not "skip the core" but **"skip
the core AND grade with the seed of record"**: when the core is deferred,
`$testKernel` and `$SutCdx` both become `seed/Codex.cdx`, the run PRINTS the
kernel digest it graded with, and it REFUSES if the workspace seed differs from
the DEPOT seed.

`test-bvt`, `oracles` and `check-errors` are `$tKernel -or $tTest`. **`$tTest` is
wider than the BVT's own list on purpose**: reading that list from `build.ps1`
couples the two.

**The stale-kernel arm is the one this design exists for.** Skip the core with a
deliberately wrong `build/output/stage1.cdx` on disk and the run must still grade
correctly, because it must never have read that file. An arm that only checked
the timing fall would pass with the stale kernel in place.

**A green `-Internal` no longer means the fixed point was proven.** An apps-only
gate does not run that comparison and cannot quote it. The line is not weakened;
it is absent, and an absent line quoted from habit is a false claim.

**The scope is `p4 opened` UNION `p4 diff2`, and the gate REFUSES when the stream
is behind.** `diff2` reports a file that differs in EITHER DIRECTION, so a stream
behind main reads another lane's landed files as its own change: measured with
NOTHING opened, `changed here` named three unrelated files and the whole
fixed-point core ran. Subtracting the incoming set is the wrong repair, because a
file changed on both sides would be dropped along with the lane's own change.
Merging down before a gate was already the rule with nothing enforcing it, which
is L-BODY's shape; this gives it a runner.

### The audit: a trigger must cover the files that can change a phase's ANSWER

The question per phase is not what it is ABOUT but what decides its answer.

| phase | trigger | what else decides its answer | state |
|---|---|---|---|
| `vm-differential` | `$tCompiler -or $tBuild` | host selection in `build/vm-config.ps1` | widened |
| `deck-headroom` | `$tBuild -or $tCompiler` | `used` is measured by RUNNING the compiler; the divisor is `demand-check-floor` | widened |
| `gen-scripts` | `$tBuild -or $tCompiler` | every generator is compiled by the current kernel | widened |
| `plug-binary`, `plug-smoke` | `$tPlugs -or $tCompiler` | the graded SET, not the trigger | graded set widened |
| `plug-selftest` | the changed plug ships a `test-*.ps1` | that harness, plus every `build*.ps1` the plug ships | added |
| `sem-equiv`, `text-stage1` | `$coreRuns` | was `$tSemantic`, which left every compiler chapter outside the front end ungated (L-NOGATE) | widened |
| `run-list` | `$tVm` | `tools/codex-vm.c`/`.exe`, `build/check-run-list.ps1` | added, per-file |
| `app-sweep` | `$tApps -or $tCompiler` | cite-scoped on an apps change, the 30-unit stride only when `$tCompiler` | as today |
| `jonquil`, `cross-smoke` | `$tCompiler`, `$tPlugs -or $tCompiler` | their runners under `build/` | **not widened, open** |

**The TRIGGER was never the gap for the plug phases; the GRADED SET was.** Both
fired correctly on `$tPlugs` while grading hardcoded lists that between them
omitted most of the plugs with a `build.ps1`, so changing one ran both phases,
graded plugs it had not touched, and came back green. `$changedPlugs` now names
the plug directories in the change and both lists append it, deduped. Safe to
widen because `plug-smoke` asserts only that `run.ps1` exits 0 with non-empty
output, so no target toolchain is required.

**`$tCompiler` is scoped to compiler SOURCE**, `^codex/compiler/.*\.codex$`. It
used to be the whole directory, so a docs-only CL closing a `compiler-backlog.md`
row switched on eight phases and rebuilt six plug binaries. It also VOIDED A
CONTROL, which is how it was found: proving that the plug phases defer needs a
change implicating neither, and an unrelated backlog file arriving from main ran
them. **The discriminator is not the extension**: non-source files that DO decide
an answer exist and keep their trigger, `build/app-sweep-baseline.txt` being the
standing example.

**The six phases whose runners live under `build/` are NOT widened, deliberately
and openly.** `$tBuild` is a true answer-dependency for all of them, but it fires
often and blanket-widening turns `-Internal` into the full gate for any build
change. The precise fix is a per-file trigger, which is machinery to manage a
modest risk the next full gate already catches (L-LESS). **Whoever edits one of
those harnesses runs its phase by hand and says so.**

**`run-list` is the first per-file trigger and is the shape the six could not
afford**, precisely because the phase is seconds rather than minutes. Its happy
path was already gated by `bvt.ps1`; the refusal and isolation arms ran nowhere
and could rot unseen (L-NOGATE).

### `app-sweep` cite-scoping

The closure is transitive over a table keyed by CHAPTER NAME, so the table is
only as sound as that key. **A name is not unique: 89 of 3,818 names under
`apps/` and `codex/` are carried by more than one file, and 76 of those carry
cite lists that DIFFER** (2026-09-08). Keeping the first file's list made the
table depend on directory enumeration order and dropped every path through the
losing file, so `$citeOf` takes the UNION over every file carrying the name.
**Over-inclusive is the safe direction; under-inclusive is a green from a sweep
that never looked.** Measured over all 1,152 apps chapters, the union removed
three total blind spots and twenty-five partial misses and widened the mean pick
from 4.1 entries to 4.5.

`-ChangedIs` names the changed files instead of asking Perforce, which is what
makes the scoping testable without staging an edit.

### Kernel provenance: a class, not one script

**A harness that cannot be told which compiler to use compiles with whatever
`build-output` last held.** Three instances, all fixed 2026-09-07:
`test-self-verify.ps1`, `check-generated-scripts.ps1` and `test-cross.ps1` now
take `-Kernel`, defaulting to the depot seed, print the kernel and its digest,
and refuse a `build-output` kernel whose digest differs from the seed unless
`-AllowStaleKernel` says so. Both refusals fire BEFORE any guest.

**`test-cross.ps1` was the one that destroyed a measurement rather than merely
reporting a wrong provenance:** a candidate staged into `build-output` looked
like it was in play while the run compiled with the depot seed, and an arm64
result read as a refuted fix until the compile log was opened.

**`cross-smoke` and `plug-smoke` still read `build-output\bare-metal\Codex.cdx`
implicitly**, and are correct only because `plug-binary` shares their trigger,
runs first, and stages `$SutCdx` there. **The ordering is load-bearing and
unstated**: move a phase or give `plug-smoke` its own trigger and the stale kernel
returns silently. The repair is to stage the kernel once the SUT is settled
rather than as a side effect of another phase. Unowned, latent, no live exposure.

**`build/test-boards.ps1` GRADES WHATEVER COMPILER IS ALREADY IN
`build-output`.** Verified at head 2026-09-08: it copies the seed to `$Stage0`
only `if (-not (Test-Path -PathType Leaf $Stage0))`, where `build/sweep-apps.ps1`
copies unconditionally. So a run on a workspace where any earlier build left a
binary there grades THAT binary, and nothing on the command line is wrong and the
printed board result carries no digest that would contradict it. Repairs priced:
copy unconditionally, matching `sweep-apps`; or keep the guard and PRINT the
digest of the `Stage0` actually used, so the reading is falsifiable. **Open.**

**A LANE RUNNING `test-cross.ps1` ON THIS BOX BOOTS RENODE, AND NOTHING IN THE
INVOCATION SAYS SO.** The script probes for Renode and SKIPs when it is absent,
so it reads as inert on a machine without it; `C:\Renode\renode.exe` is installed
here. Renode is out by standing rule, and the guard against running it is the
operator remembering, which is L-BODY. The repair is a refusal: decline to boot
unless told explicitly that Renode is granted. **Unowned, and it wants Damian's
word on the flag before anyone writes it.**

**A verification that is cheap by an accident of workspace state silently stops
being cheap.** The `-Kernel` guard was graded on four host-only arms, host-only
because the riscv plug was UNBUILT in that workspace; build the plug and two of
them boot. Whoever re-grades this class states the plug's build state beside the
result.

### Open, unowned

- **Release sampler identity and lifetime.** `build/box-sample.ps1` counts
  VM-host processes, including `-run-list` supervisors that create no guest
  partition (`tools/codex-vm.c`, the early supervisor dispatch). Its `guests`
  column cannot establish actual guest count or working set per guest.
  The header is written once into `build-output`, which `build.ps1` removes
  during clean; later samples can recreate a headerless file. Use an output
  outside cleaned directories and record per-process identity, role and
  working set before changing RAM admission bars. Update 58's retained
  samples are `docs/Agents/box-release-2026-09-10.csv`; the release note states
  the process-count limitation rather than inferring a guest peak.
- **The gate's unaccounted wall time.** With nothing implicated, one sample ran
  10.1 s and another of the same shape on the same box ran 225.2 s. The idle
  components sum to about 10 s and the slow sample's phase timings sum to 0.6 s,
  so about 224 s sat outside every phase. Same defect val measured at 311 s in a
  615 s gate. Cause not isolated (L-GREEN: quote the spread, not the sample).
- **`build/run-plug.ps1` given a `-IrCce` file and `rust-plug.cdx` faults the
  plug VM and exits 6.** Nothing in the gate covers it: `plug-smoke` calls each
  plug's OWN `run.ps1` with a `.codex` source instead, and passes. Whether the
  fault is the plug, the framing, or the input is NOT established.
- **`ablate-doctrine -SelfTest` and `-Setup` refuse on main, and its own guard is
  why.** `check-doc-counts` gained claims the harness's `$scoredDocs` was never
  extended to cover, so `Assert-ScoredDocsCoverChecker` throws, which is the
  guard doing its job. The fix is not one line: the scratch tree would also need
  `seed/`, `apps/` and `build/bvt.ps1` junctioned in, or every claim reports
  NOPATH and every arm scores FAIL for a reason unrelated to the candidate. It is
  a design decision about what the scratch tree carries.
- **`build-apps.ps1` is destructive on a clean tree**: every app rewrites its
  page and the rewrite is a DEGRADATION. Both arms degrade identically so a
  comparison still holds, but do not run it expecting a no-op. Separately, many
  `apps/*/web/*.html` artifacts are orphans with no Page chapter to regenerate
  them; the script warns and continues, so the shortfall is invisible unless the
  printed count is compared against the artifact count.
- **No test in the tree carries a `.stdin` sidecar**, so `test-run.ps1`'s
  `-StdinFile` branch is exercised by nothing.

## Quire tables that are copies or derivations

`build/quire-map.ps1` is the authority. Two scripts were found deriving the
mapping instead of reading it and both answered a different question; both now
source the map.

**A GATE ON THIS SHAPE WAS PROPOSED AND MEASURED DOWN.** The obvious rule, that a
script mentioning a quire must source the map, flags 77 of 95 scripts and would
be a check nobody reads. The narrow rule, two or more quire names as quoted
literals without sourcing the map, flags 7, and only three of those are tables at
all. Seven candidates needing per-file judgement is an AUDIT, not a runner: a
gate here would be four false alarms out of seven and would train its readers to
wave it through. **Re-run the audit rather than trusting this paragraph**
(L-COUNT): the census is `$QuireDirs` from the map, quoted-literal matches per
script, minus any script that sources the map.
