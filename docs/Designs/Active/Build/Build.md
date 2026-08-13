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
it, run it, without leaving the machine.

## The Shell DSL generators (the `build/*.ps1` half of the migration)

**The parse class is closed and gated.** `check-generated-scripts.ps1`
compiles every generator, dead target or not, and hard-fails on PowerShell
parse errors with NO baseline. **Re-measure the counts, never copy them**
(L-COUNT): measured 2026-08-11 with the campaign finished, 45
generators, 4 drifted, 0 broken, 1 dead target. **The four that remain
are exactly the backlogged stubs** (`test`, `cdx-to-pe`, `build`,
`build-img`); every ordinary generator in the tree now matches the
script it ships beside and carries the banner.

`plug-build` and `plug-run` were adopted 2026-08-10 and ship as
`build/plug-build.ps1` and `build/plug-run.ps1`. Both carried defects
that survived because a generator with no live target is compiled but
never compared against anything: `plug-build` resolved
`$PSScriptRoot/../plugs`, which is `<repo>/plugs` and does not exist,
and then tested that path with `-PathType Leaf`, which is false for a
directory, so the emitted script could only ever exit 2. **Neither is
reachable by the parse check** -- both emit text PowerShell parses
happily. A live target is what turns a generator from compiled into
checked, so give a new one its target in the same change. Only
`testrunBashScript` sits in that blind spot now.

`test-run` was closed 2026-08-10 (fester) and is the worked example of the
method below: its whole 63-line drift was comments and formatting, so
nothing was ported and the emitted script was installed as-is. The two
arms were the five sidecar branches (plain, `.disk`, `.keys`, `.smp`,
`.vmargs`) byte-compared before and after the install, with a sabotaged
harness as the control to show the comparison could fail at all. **No
test in the tree carries a `.stdin` sidecar, so the `-StdinFile` branch
is unexercised by anything and no arm could cover it.**

`compile` was closed 2026-08-11 (fester) and is the worked example of the
OTHER shape: 433 drift lines of which about twenty were real behaviour
the generator had never had. `-Kernel` and its seed-drift NOTE, `-Text`,
`-Measure`, `-Pet`, `-MemNoCap`, `-TimeoutSec`, `-Decks`, `-Passes`,
`-RawFlags`, `-DiskFile`, `-Peer` / `-Registry`, the `map` flag, the
`$implicitNames` guard on the unbundled warning, `Resolve-Name` against
the kernel's embedded MAP1 instead of the drifting `.map` sidecar,
`Remap-Diag`, `Format-CrashReport`, and the retry line that reports the
memory it actually set. The old generator still emitted "retrying with
3584MB" while setting 8192, which is the bug the shipped script had
already fixed. Two arms of eleven cases (CDX with and without `-Kernel`,
`-Text`, `-IrCce`, `-Measure`, `-Decks 100`, a failing compile, `-Break`
found and not-found, a missing `-DiskFile`, a missing `-Kernel`)
compared exit code, log text, stderr, and the content hash of every
artifact: identical. Dropping the `map` flag was the control and it
diverged. The NOTE branch needed its own run from a working directory
whose `build-output` kernel differed from the seed, because on this box
they were equal and the branch was silently dead in both arms.

- **`ValidateRange` had no DSL node, and that alone would have blocked
  the install** -- the emitted script would have accepted `-Decks 99999`.
  `ShellParam` gained `sp-attrs : List Text` and `ShellBuild` a `sh-attrs`
  wrapper alongside `sh-doc`; `emit-ps-params` emits each as its own
  attribute line. One field, because the alternative was leaving a real
  validation behind.
`compare-codex-semantic` was closed 2026-08-11 (fester) and is the third
shape: a drift where the GENERATOR was partly right and the shipped
script was partly wrong, so neither side could just be installed over the
other. Ported INTO the generator: `Canonicalize-TypeName` (without it a
bare-style `HamtEntry a` reports as a dropped definition, an emitter
information-loss claim where nothing was lost) and the `#HEX` to decimal
normalization. Deleted FROM the generator: a `++` operator at precedence
4 and a rewrite of every binary `&` into it. **There is no `PlusPlus`
token** -- `codex/compiler/Syntax/Token.codex` does not declare one, so
that was a fiction the shipped script had never had.

- **The precedence table was wrong on BOTH sides, and the shipped side
  was wrong in the dangerous direction.** The compiler
  (`ParserCore.codex:522`) ranks Pipe 2, Ampersand 3, comparisons 4,
  ColonColon 5. Shipped gave `&` and `|` the same rank, which makes
  `Strip-RedundantParens` strip the load-bearing parens from
  `(a | b) & c` and compare it EQUAL to `a | b & c`. A false PASS: the
  leg exists to catch emitter information loss and would have waved
  through a reassociation. The generator's `++` put `&` ABOVE
  comparisons, wrong the other way. The table now carries the compiler's
  own numbers. `AppPrec = 10` still sits above `^` (now 8) and below `.`
  (20), so raising the scale by one does not disturb application.
- **The VERDICT is unchanged on the real corpus. The NORMALIZATION is
  not, and checking only the verdict said the wrong thing.** All arms
  report the same 5237 matched, 0 dropped, 0 body mismatches, 331 extra,
  41 sig mismatches over `build/output/Codex.codex` against
  `stage1.codex`, and on that basis this entry first claimed the
  precedence fix changed nothing. Dumping every normalized body and
  diffing old table against new shows it changes **4 definitions of
  5237**: `join-title-parts` and `scan-class-instance-defs`
  (`Syntax/Parser.codex:1245`, `:1522`), `normalize-list-insert-at` and
  `normalize-list-prim` (`Types/TypeChecker.codex:309`, `:250`). Each
  mixes boolean `|` and `&` and is correctly parenthesized in the
  source:

  ```
  if (list-prim-nil a | list-prim-cons a) & is-int-literal-text it
  ```

  The old table stripped those parens, leaving
  `list-prim-nil a | list-prim-cons a & is-int-literal-text it`, which
  under the real precedence means `a | (b & c)` and is a different
  expression. **It stripped them from BOTH sides equally, so the two
  still compared equal and the gate stayed green.** Nothing was ever
  reported wrongly. What it means is narrower and worth stating exactly:
  for those four definitions the leg was comparing a form whose grouping
  had already been destroyed, so a reassociation of `|` against `&`
  there could not have been detected. A blind spot, not a false answer.
- **Compare the intermediate product, not just the answer.** A pass/fail
  verdict is a lossy summary of the thing you changed. The verdict was
  identical under all three tables and stayed identical; only dumping
  the 10,474 normalized bodies showed the correction did anything at all
  on real code. **Had the four definitions been the only evidence, the
  verdict-level check would have reported "inert" about a fix that was
  not.**
- The three changes were ALSO proven on hand-built pairs, each with the
  behaviour removed as its own control, because four accidental
  instances in one corpus is not a test anyone chose.
- **The script prints NOTHING on PASS, so a PASS/PASS comparison proves
  nothing.** Compare instrumented copies that always print matched /
  dropped / extra / body / sig counts, not exit codes.
- **Two crashes fixed, one defect class: a `List` returned from a
  PowerShell function is UNROLLED by the caller.** `$s1Defs.Count` died
  whenever stage1 held exactly one definition, and the FAIL path's
  `$s0Chapters.Values | ForEach-Object { $_.Count }` died whenever the
  source held one chapter. Both are pre-existing and present in the
  shipped script; the gate never meets them because its corpus has
  thousands of defs, but anyone running the tool by hand on a small pair
  meets them immediately and gets a bare type error instead of a report.
  `@()` around the call is the fix. This is the same root cause as the
  `foreach` trap below.
- **`ScForEach` over a `SeRaw` command call is a trap.** The DSL emitted
  `foreach ($l in Format-CiteChapters -Ordered $ordered)` with no
  parentheses, and PowerShell bound the WHOLE returned array to `$l`. The
  assembled compiler input came to 114 bytes instead of 3830 and every
  compile failed. `Get-ChildItem` and `Get-Content` in the same shape are
  fine, which is why the tree's other bare `foreach ... in <cmd>` lines
  are not bugs: a cmdlet streams, a function returning a collection does
  not. Use `SeCallArgs`, which parenthesises.

`bvt` was closed 2026-08-11 (fester) and is the one that shows what this
campaign is actually FOR. **The generator's test list held 16 of the 75
tests.** Adopting it unread would have dropped 59 from the gate: every
ECDSA, X.509, TLS and DTLS test, every induction and normalizer proof,
every scope test, the whole repository-protocol set, and most error
tripwires. The BVT would have gone on printing PASS while testing a
quarter of what it claims. Nothing in the drift number says this: 239
lines of drift looks like the same kind of number `clean-zombies` has.
**Diff the DATA a generator carries, not just its code shape.**

- The list was rebuilt from the shipped script MECHANICALLY, not by
  hand, and the round trip is asserted: same 75 paths, same order, and
  all 75 trailing notes byte-identical. Those notes are what each test
  GUARDS and several say what breaks when the test is weakened; they are
  the only record of why most of these exist, so losing them silently
  was the second risk here. They live in the generator as
  `bvt-entry "<path>" "<note>"` pairs with the group headers preserved
  as `bvt-head`.
- **The `.disk` sidecar handling was missing entirely.** The generator
  called `test-run.ps1 -Kernel .. -OutFile ..` with no `-DiskFile`, so
  every repository test would have run against no block device. Control:
  removing the ported line makes 8 tests fail on output mismatch
  (`repo-tombstone`, `repo-checkout`, `disk-facts-multi-load`,
  `colophon-dogfood` and the rest), which is what the two-arm PASS/PASS
  comparison needed to be worth anything.
- `-Jobs` defaulted to 4 in the generator against 8 in the shipped
  script. Damian's 2026-08-02 ruling is 8 for every parallel harness and
  `CLAUDE.md` says not to copy a lower number out of an older doc; the
  generator WAS the older doc. The gate passes `-Jobs 8` explicitly so
  the default never bit, but a hand run would have taken it.
- The failure paths lost their colour (`ScEcho` where the shipped script
  uses `-ForegroundColor Red`). `ScEchoStyled` expresses it. Two traps
  in one node: its Boolean is "newline?" so `False` emits `-NoNewline`,
  and `ClRed` maps to `DarkRed` -- the bright console colours are
  `ClBrightRed` / `ClBrightGreen`.

`run-plug` was closed 2026-08-11 (fester). One real behaviour ported and
it is the kind worth naming: the generator emitted a BARE `catch` where
the shipped script catches `[System.IO.IOException]` only. A bare catch
swallows everything, so a genuine fault inside the receive loop is
treated as a normal end of stream and the script then reports
`FAIL: plug produced no output` -- a diagnostic that points at the plug
when the fault is in the harness. Measured by injecting a
non-IO exception into the loop: typed catch exits 1 and the real error
surfaces; bare catch exits 6 and says "no output". `ShellTypes` gained
`ScTryCatch (body) (exc-type) (catch) (finally)` for it, because the
existing `ScTry` has no exception type and the alternative was hand
editing the output, which the banner now forbids. The `[byte]$Tag`
parameter became `ValidateRange(0, 255)` on an `SptInt`, which refuses
the same inputs at binding; there is no `SptByte`.

`test-compile-batch` was closed 2026-08-11 (fester). 201 drift lines and
**every one of them was real**, which makes it the counterexample to
`test-run`, where the whole drift was convention. Four separate things,
each of which had already been fixed once in the shipped script and would
have been reintroduced by installing the generator's output:

- **Both of the parser's documented performance disasters were back.**
  `NextLine` walked bytes in a PowerShell loop instead of using the
  Latin-1 shadow string, which is 17 to 23 minutes on a single crashed
  batch; the per-test scan was a per-line walk instead of the memoized
  marker search, which is 865 seconds of host CPU per crash. Both
  regressions come with the measurement written beside them in the
  shipped script, so the generator was not merely older, it predated the
  fix and carried the slow version verbatim. `$rawStr`, `$markerList` /
  `$markerMemo`, `Add-LogSpan` with its 2000-line cap, the anchored-at-
  `$pos` marker check, `$vmDead` and the 64 KB `!EXC` dump capture are
  all ported.
- **`Resolve-Source` returned text only, so there were no diagnostic
  regions and no `Convert-DiagLine` remap.** Every diagnostic in a
  battery `build.log` would have carried its position in the ASSEMBLED
  unit rather than in the source file, which is exactly what a
  `.failing` position pin reads. This is the control, and it fires
  loudly: dropping the remap changes 45 of 46 `build.log` files, turning
  `.\codex\foreword\core\CCE.codex:24:3` into `704:3`.
- **The mode line said `CDX repl`.** The shipped script says plain `CDX`
  and explains why in the line above it: a per-request `repl` flag
  embeds a REPL loop in the TEST binary and hangs every test that
  consumes stdin. The generator would have put the flag back under its
  own explanation of why it must not be there.
- The `.src-bytes` census file and the three stopwatches (`resolvems`,
  `vmms`, `parsems` in the sweep log) were absent, so the batch would
  have gone silent on where its time goes.

Two arms over all 46 generators in `codex/build/`, comparing every
artifact and not the exit code: 184 files (`.exitcode`, `build.log`,
`.src-bytes`, `<name>.cdx` for each) byte-identical, 46 of 46 at exit 0,
317 log lines matched. **This generator compiles the generators**, so
`check-generated-scripts.ps1` is both the consumer and a self-check of
the install; the battery is the other caller.

- **`ScSetContent` emits its path expression UNPARENTHESISED**, so
  `ScSetContent (SeRaw "Join-Path $testOut '.exitcode'") ...` produces
  `Set-Content -Path Join-Path $testOut '.exitcode' -Value ...`, which
  does not run. This generator held the only compound-path use of the
  node in the tree; the other seven all pass a bare `$var` and are
  fine. Fixed at the call site, not in `emit-ps-expr`, for the reason
  below. Same shape as the `SeProperty` and `ScForEach` traps: the DSL
  will happily emit an expression into a position that needs grouping.

`build-apps` was closed 2026-08-11 (fester). The 74 drift lines were
convention with one exception: `ScWriteError` emits
`[Console]::Error.WriteLine`, where the shipped script says `Write-Error`.
Those are not the same thing under `$ErrorActionPreference = 'Stop'` --
`Write-Error` raises a TERMINATING error, so `Write-Error '...'; exit 2`
never reaches the `exit` and the process ends at 1. **The shipped
script's documented exit codes 2 and 3 have therefore never occurred.**
Left as shipped and recorded here rather than corrected: nothing reads
those codes, and swapping the node would have smuggled an error-semantics
change into a drift closure. The DSL cannot currently express
`Write-Error` at all, which is why the two sites are `ScRaw`.

- Two arms over every app: 56 artifacts (28 bundles + 28 renders)
  byte-identical, 301 console lines identical, both at exit 0. Control:
  removing the CRLF normalization changes the written page. The other
  half of that control -- skipping the bundle step -- was a NO-OP and is
  worth saying so, because the compiler resolves cites itself, which is
  what `TheShimmeringPortal.md` already says the bundle step is not
  required for.
- **Running the tool is destructive right now, and that is how two
  pre-existing defects surfaced.** Every one of the 28 apps rewrote its
  page, and the rewrite is a DEGRADATION: `apps/weather/web/weather.html`
  went from 303 lines to 26, the plug reporting `OK ... (1060 chars)`.
  Reverted, never submitted. Nothing in the script is at fault and both
  arms degrade identically, so the comparison still holds; but do not run
  `build-apps.ps1` on a clean tree expecting it to be a no-op.
- **46 of the 74 `apps/*/web/*.html` artifacts are orphans** with no Page
  chapter to regenerate them, `apps/gpushow/` being 40 of them with no
  `.codex` in the directory at all. The script warns and continues, so
  the shortfall is invisible unless the printed count is compared against
  the artifact count. Recorded in `docs/TheShimmeringPortal.md`, which
  claimed 74 until this run.

`test-disk-compile` was closed 2026-08-11 (fester), and the drift closure
is the smallest part of what was wrong with it. Three real fixes:

- **`Start-VmRun` sat INSIDE the try whose finally calls
  `Close-Vm -Conn $run.Conn`.** So every failure above the assignment --
  a missing `Codex.cdx`, a VM that would not start -- ran a finally that
  dereferences an unassigned `$run` under `Set-StrictMode -Version
  Latest`, replacing the real diagnostic with a strict-mode error.
  `exit` inside a try still runs the finally, so the exit paths did not
  escape it either. The try now opens after `$run` holds a VM, which is
  where the shipped script put it. New section `S05b` exists only to mark
  that boundary.
- Five `Write-Host` colours were lost (the Cyan banner, three Red
  failures, the Green PASS). `ScEchoStyled` restores them; the other
  seven `Write-Host` lines are uncoloured in the shipped script too and
  stay `ScEcho`.
- **The default `-SampleSrc` pointed at
  `codex\test\absorb-outer-lambda.codex`, deleted in change 1568.** The
  script could not pass with no arguments and nothing in the tree calls
  it, so nothing noticed. Repointed at `codex\test\field-range-proven.codex`,
  which is cite-free and expects `12`, so the `-Expected` default is
  unchanged. **The sample MUST be cite-free**: the image plug embeds it
  raw as `SOURCE.SRC` and the compiler reads it off the disk inside the
  VM, so nothing resolves a `cites` line on the way in.

- **This one is NOT verified end to end, and the reason is a defect
  outside it.** Steps 1a to 1c pass, then the IMG plug returns 1400 bytes
  where a 16384-sector image is 8 MB. Its VM faults: a register and stack
  dump on stderr and `VM exited (code=-1, exits=172327)`. Reproduced on
  BOTH `-Fat16` and the default FAT32, so it is not the FAT16 branch.
  **`codex/plugs/img/run.ps1` prints `OK: <file> (N bytes)` anyway** --
  its receive loop is `while (read) {...} catch {}` with no completeness
  check, so a crash mid-stream is indistinguishable from a clean EOF.
  That is the same defect class as the bare `catch` fixed in `run-plug`
  above, and it is why a crashing plug has been reporting success. The
  generator adoption rests on the emitted-versus-shipped diff, a clean
  `ParseFile`, and inspection of the three fixes; say so rather than
  calling it green.

The last five ordinary generators were closed together 2026-08-11
(fester), leaving only the four backlogged stubs. Each is worth one line
except where it is not:

- **`test-boards` emitted `New-Item -ItemType Directory -Force Split-Path
  $Stage0`**, missing the parentheses shipped code has. That is not a
  directory named `Split-Path`, it is a HARD ERROR -- PowerShell reports
  `A positional parameter cannot be found`, and with
  `$ErrorActionPreference = 'Stop'` the script dies on its second
  statement. Fixed at the call site (`ScMkdir (SeRaw "(Split-Path
  $Stage0)")`). **This is the third node in this family**, after
  `SeProperty` and `ScSetContent`: the DSL will emit an expression into a
  position that needs grouping and nothing complains until it runs.
- **`clean-zombies` would have gained `Set-StrictMode -Version Latest`
  and `$ErrorActionPreference = 'Stop'`, which the shipped script omits
  on purpose.** This is the tool reached for when the machine is already
  wedged, and it has to run to the end on a box where things are failing.
  Under `Stop`, `& wsl.exe --shutdown` becomes a terminating error on any
  shell with `PSNativeCommandUseErrorActionPreference` enabled (7.6.3 on
  this box reports it False, so the risk was latent rather than live).
  `s01` now omits both, which is the only generator here that does, and
  the reason is in the prose above it. Its `Write-Output` was also being
  emitted as `Write-Host` by `ScEcho` -- different destinations, and this
  script's two lines are its entire result, so they belong on the
  pipeline.
- `resolve-trace`: three `Write-Error` sites were becoming
  `[Console]::Error.WriteLine`, the same node mismatch as `build-apps`
  above, left as shipped for the same reason.
- `run-plug-chain`: convention only. Its header's `-InFile`-not-`-Input`
  trap already lives in `OperatorsManual.md`, so dropping it lost
  nothing.
- **`concat-codex-self` had ZERO code differences**, which is the good
  news for the generator that assembles the compiler's own 2.77 MB unit.

- **`Sort-ByDeps` in `concat-codex-self.ps1` is DEAD CODE, and the header
  comment asserting what it does was false.** It is defined at line 97,
  40 lines of topological sort, and nothing calls it -- emission at the
  bottom of the script uses `[Array]::Sort($subFiles, $nameCmp)` with
  `State|Encoder` files hoisted. So chapters are NOT "topologically
  sorted so cited chapters appear before consumers", as the shipped
  header claimed; they are alphabetical with a two-name exception. The
  emitted script drops that claim, which is why installing it is an
  improvement, but the dead function is still there and was left alone
  rather than deleted inside a drift closure.
- **This was found by a control that FAILED TO FIRE, and that is the
  whole lesson.** Reversing `$sorted` in `Sort-ByDeps` produced a
  byte-identical 2.77 MB unit, which should have been impossible if the
  sort mattered. Reported as "verified" it would have been a lie by
  accident. The real control -- removing the `State|Encoder` hoist --
  changes the hash while keeping the byte count, and only then does
  arm A equals arm B mean anything. Same shape as the `resolve-trace`
  arm below.
- **A control aimed at a branch the test data never reaches is not a
  control.** The first `resolve-trace` arm fed a synthetic trace with the
  fields reversed -- the format is `T:<size>:<rip>`, not `<rip>:<size>`
  -- so every address fell outside the symbol map, `Resolve-Addr`
  returned its hex fallback every time, and the `-le` to `-lt` sabotage
  of its range check could not fire. With the fields the right way round
  and one address placed EXACTLY on a segment start, the same sabotage
  turns `beta-fn+0x0` into `0x100100`. Both arms then match over both
  trace formats, text and binary, 12 lines each.

Arms for the five: `concat-codex-self` 2,769,366 bytes byte-identical;
`test-boards` 2 pass 0 fail with identical console; `resolve-trace` 12
identical lines over both formats; `run-plug-chain` identical exit and
output on the missing-input path and on a real plug run.
**`clean-zombies` was NOT run and must not be**, by the doctrine now in
`OperatorsManual.md`: it kills any VM on the box regardless of which
agent started it, and another agent's gate was live. Verified by parse
plus a comment-stripped comparison showing the executable lines
identical modulo `@()` array literals and redundant parentheses.

- **An unrelated observation, unresolved and not diagnosed.**
  `build/run-plug.ps1` given a `-IrCce` file and `rust-plug.cdx` -- the
  path `plug-oracle-test.ps1` uses -- faults the plug VM
  (`VM exited (code=-1)`) and exits 6. Nothing in the gate covers it:
  `plug-smoke` calls each plug's OWN `run.ps1` with a `.codex` source
  instead, and passes. Whether the fault is the plug, the framing, or the
  input is NOT established here. A separate earlier attempt through the
  PE plug is not evidence either way -- `run-plug.ps1` sends tag 1 and
  the PE plug wants tag 4 with a mode byte, so that one was invalid
  input, not a defect.

- **THE BANNER'S PRESENCE IS AN INVARIANT, NOT DECORATION.** Every
  generated script opens with `generated-banner` from `ShellTypes`,
  which says a hand edit here must not be submitted: change the
  generator, regenerate, and submit both together. It is ONE definition
  cited by all three emitters, because three copies of a warning are
  three things that drift apart. The invariant is that **a file carrying
  the banner is exactly a file that currently matches its generator.**
  The four still in the baseline do NOT carry it and must not be
  given it by hand: their shipped script is the maintained side, so
  "do not edit by hand" would be a lie on the only copy anyone edits.
  They get the banner when their drift closes, as part of the install.
- **The emitted text is LF and the shipped scripts are CRLF.** Install
  without converting and every line reads as changed. Nothing warns.
- **Losing a script's header comments is not automatically a loss, but
  check.** `test-run.ps1`'s header carried the StdinFile-versus-KeysFile
  doctrine, which the emitter cannot express; it was safe to drop only
  because `ExaminersAssay.md` and `OperatorsManual.md` both already own
  it. Grep for the doctrine's other home BEFORE installing over it.

The four backlogged stubs (`test`, `cdx-to-pe`, `build`, `build-img`)
remain Damian's decision, recorded in `docs/PM/CurrentPlan.md`.

Method, for whoever resumes it:

- Read the drift with `check-generated-scripts.ps1 -Diff <name>`, judge
  each shipped-only line as real behaviour or emitter convention, port
  only the former, then INSTALL the emitted script and accept the style
  change. **Do not bulk-regenerate; there is deliberately no `-Write`
  flag.**
- **A TEXT MATCH IS NOT VERIFICATION.** `compile-arm64` sat at 58 drift
  lines that all read as convention while its generator emitted load
  address 0x40000000 for 0x40100000, heap reserve 0x1000000 for
  0x0F000000, align 0x10000 for 0x1000, and a CCE name decoded one byte
  per character. **It exits 0 and writes a broken ELF.** The two-arm run
  is what caught it: compile a real test through the plug pipeline before
  and after installing and require the artifact to be byte-identical,
  then run the PRE-fix emission as a control and require it to diverge
  (L-FALSIF).
- **A GENERATOR WITH NO `$AltTarget` ENTRY READS AS HAVING NO TARGET, AND
  NOTHING REPORTS THAT.** `cvmm-build` emits `apps\cvmm\build.ps1` and
  sat in that blind spot since it was written: counted among the "no live
  target" set, never compared, drift unknown. **Before calling any
  generator dead, READ IT and look for the script it describes** -- the
  target name in `sh-script` is not the path.
- **The emitter is usually not the wrong side.** `SeProperty` emits
  `obj.prop` unparenthesised, which is right for `SeVar` and wrong for a
  raw command; the fix went in the three generator sites, not in
  `emit-ps-expr`, because parenthesising there would drift every matching
  generator at once.
- **CHECK `ShellTypes` BEFORE ADDING A NODE.** A prior plan asserted the
  DSL had no `-ForegroundColor` node and needed a new one. Wrong:
  `ScEchoStyled` and `ScEchoPartial` already existed and `emit-ps-color`
  already maps the sixteen `ShellColor` constructors onto PowerShell's
  console names. No node was added.
- **A `let` binding's value must start on the SAME line.** A multi-line
  list literal is fine for a top-level definition but `let name =`
  followed by a newline raises CDX2000 at the column after the `=`. The
  fix is a section-level definition rather than a `let`.
- `codex/test/shell-build-keep` cites `ShellTypes` and `ShellBuild`, so it
  is the test any Shell quire change touches. Compile and run it through
  the harness. The wider net for a Shell quire change is
  `check-generated-scripts.ps1` with no `-Only`: it recompiles all 45 and
  reports how many still match, so a field added to `ShellParam` shows up
  as a generator that stopped matching. Adding `sp-attrs` left all 29
  matches intact.
- **The Shell quire is not in the seed unit.** `concat-codex-self.ps1`
  preloads `codex\foreword\core` only, so a `codex\foreword\shell` change
  cannot move a seed byte and takes no build token. Confirm it at the
  concat rather than reasoning from the directory name.
- `testrunBashScript` is the only `emit-bash` generator and is NOT one to
  delete: until `test-run.sh` has a live target, "no unhandled nodes" is a
  statement about PowerShell only. `PowerShellEmit` handles every
  constructor `ShellTypes` declares; `BashEmit` and `KshEmit` are each
  missing most of them. The count is recorded at the scan site in
  `check-generated-scripts.ps1`; re-derive it, never copy it.
- `build/vm-config.ps1` is generated and still matches: edit the generator
  and regenerate. `compile-arm64.ps1` / `compile-riscv.ps1` are the
  opposite -- generator abandoned, shipped script is the maintained side.
  **Check `-Only <name>` before editing either half of any generated
  script.**

**The general trap, which is not limited to this lane: the `build/*.ps1`
files are hand-maintained, and the drift runs the OTHER way from what
"generated from" advertises.** Measured on `lintunusedcitesScript.codex`:
the generator emits `[Parameter(Mandatory=$true)]` where the shipped
script has none, so the shipped copy is the hand-FIXED one and the Codex
is stale. Regenerating would hand back a script that prompts headless.
Diff before regenerating one of these to fix a bug in it.

## Priority Order

1. ~~Wire the FileSystem builtins~~ -- done 2026-07-19, Phase A above.
2. **On-disk test runner.**
3. **Text pingpong on device.**
4. **Editor and shell as the default on-image path.**
