# Examiner's Assay

Test infrastructure, coverage, and known results for the Codex
compiler, OS, and library stack. For build process and gate
mechanics, see `OperatorsManual.md`. This document records what
is tested, what passes, and what remains.

## Test Battery Layout

```
codex/test/              Root-level tests (default battery)
codex/test/errors/       Expected-failure tests (bad programs that must reject)
codex/test/apps/         Application and integration tests (-Apps or -All)
codex/test/forewords/    Per-chapter foreword compile tests (-FW or -All)
codex/test/lib/          Library tests
codex/test/examples/     Worked examples (e.g. missile-warning)
bench/codex/             Codegen-quality shapes (instruction count and bytes,
                         not correctness -- see "The repro you kept is the test")
```

The default battery runs `codex/test/*.codex` + `codex/test/errors/*.codex`.
Use `-Apps` for the full application suite, `-All` for everything.

`bench/` is not part of any battery and is not run by the gate. It is the
only instrument that can see a change which keeps a program correct and makes
it worse, so a codegen fix pins its shape there the way a rejection pins its
shape in `codex/test/errors/`. Build and compare with `bench/compare.ps1`.

## Sidecar Files

Each test `foo.codex` may have sidecars that control its behavior:

| Sidecar | Effect |
|---------|--------|
| `foo.expected` | Compile must succeed; runtime serial output must match. **Not byte-for-byte, whatever this row said until 2026-07-30** -- see the section below the table |
| `foo.failing` | Compile must fail with the listed CDX error codes |
| `foo.diag` | Compile must succeed and emit each listed CDX code at any severity (warning/info/error). One code per line (bare number or `CDX`-prefixed). Combine with `foo.expected` to also check runtime output. This is how warnings and infos are regression-tested. |
| `foo.skip` | Skipped entirely; first line is the reason |
| `foo.slow` | Skipped unless `-Slow`; first line is the reason |
| `foo.fatal` | Skipped unless `-Fatal`; kills the VM at runtime |
| `foo.flags` | First line is appended to the test's **compile mode line**, so the test states its own compiler requirements. `prose` selects CPL; `passes=+name` adds an IR pass; `decks=N` scales every phase deck floor to N per cent, which is what a compilation unit larger than the floors were sized for needs (`codex/test/apps/foreword-all-compile` cites all 416 foreword chapters and carries `decks=150`; without it the compile is `CDX9002: Deck overflow in LOWER`, run both ways 2026-07-22). Read by `build/test-compile-batch.ps1`, so it applies to the battery and **not** to a hand-run `build/compile.ps1`, which takes the same settings as switches |
| `foo.stdin` | Pumped to VM serial after boot (runtime input) |
| `foo.keys` | Scancode timeline (`t:scancode` per line, t = ms since boot, `#` comments) handed to codex-vm as `-keys-file`. **Not interchangeable with `.stdin`** -- see below |
| `foo.disk` | Attached as IDE disk image via codex-vm `-disk` flag (primary master) |
| `foo.disk2` | A SECOND image, attached as the primary slave and reached by `block-select 1`. Every test in the tree attached at most one disk, and that is precisely why `block-select` could do nothing for as long as it did: with one image behind all four drive positions, a working drive-select and a missing one produce identical output, and `block-sector-count` returning a boot-time cache is indistinguishable from one that re-probes. `codex/test/block-select-drives` is the worked example and it demands two sizes AND two contents, so neither a stale channel nor a stale count can pass |
| `foo.disk-src` | First line names ANOTHER test; that test's freshly compiled CDX is attached as this test's disk. `.disk` names a file and is therefore frozen, which is no use to a test pinning what the CURRENT compiler emits. `manifest-pin` is the case it was built for, and it had been skipped for want of it |
| `foo.smp` | Core count. The test is booted with `-smp N`. This is how a test covers multi-core; without it every test boots single-core, which is why nothing exercised SMP for so long (`codex/test/smp-cores.codex` is the first) |
| `foo.vmargs` | Extra codex-vm flags, whitespace-separated, `#` comments and blank lines ignored. For a test whose subject is the MACHINE rather than the program: a bus topology, an absent device. Before it existed, such a test could only be a `.skip` with the command in its prose -- an unrun test, which proves less than no test because it reads as coverage. `codex/test/apps/usb-kbd-hub` is the first, passing `-xhci-no-root-kbd` to unplug the root keyboard so the hub is the only route to one |

A test with no sidecar compiles but is unverified (PASS_UNVERIFIED). Measured
2026-07-27: only **11 of 456** tests in `codex/test` are in that state, and
nearly all are deliberate crash demos. The assertions are healthy.

### An `.expected` comparison is not byte-for-byte, and 139 sidecars rely on that without anyone knowing

Found 2026-07-30 by blu, while diagnosing what looked like two failing tests
that are in fact passing. Nothing here is broken today. What is wrong is the
belief about what a green row means, and this section exists so the next
person does not spend the same hour.

**What the comparison actually is.** `build/test.ps1` decides pass and fail
with three lines:

```powershell
$expectedBytes = [System.IO.File]::ReadAllText($expectedFile) -replace "`r",''
$actualBytes   = [System.IO.File]::ReadAllText($actual)
if ($expectedBytes -eq $actualBytes) { PASS } else { FAIL }
```

Despite the variable names those are STRINGS, and `-eq` on two strings in
PowerShell is a **culture-sensitive** comparison, not an ordinal one. A
culture-sensitive comparison ignores characters that carry no collation
weight, which includes several control characters. It is a one-line
demonstration:

```powershell
"`u{0001}abc" -eq "abc"                                     # True
[string]::Equals("`u{0001}abc", "abc", 'Ordinal')           # False
```

SOH (0x01), BEL (0x07) and NUL (0x00) are ignored this way. VT (0x0B) is
not, and ordinary text differences are caught exactly as you would expect
(`"abd" -eq "abc"` is False). So the oracle is sound about content and blind
to a specific class of invisible bytes.

**Where the invisible byte comes from.** The guest's serial stream opens with
a `0x01` SOH. `build/test-run.ps1` strips it from the ACTUAL output, along
with CR, the `HEAP:`/`WD:`/`STACK:` lines and trailing newline noise, which
is why recording an `.expected` **through the harness** is the standing rule.
A sidecar recorded by copying a raw `codex-vm -output` file instead keeps the
SOH, and the file then begins with a byte the program's real output does not
have. That sidecar still passes, because the comparison above cannot see the
difference.

**Measured 2026-07-30, by reading the first byte of every file:** 1181
`.expected` sidecars under `codex/test`, of which **139 begin with the raw
SOH**. They span the tree rather than clustering in one campaign
(`codex/test`, `codex/test/apps`, `codex/test/forewords`, `codex/test/lib`,
`codex/test/ops`), so this has been happening for a long time and no run has
ever mentioned it. **Re-measure before quoting** (L-COUNT).

**What it costs, and it is two things rather than a crisis.**

1. **The oracle cannot express a difference made only of ignorable
   characters.** If a program began emitting a stray SOH or NUL where it
   previously emitted none, no `.expected` row in the battery would move.
   That is a narrow blind spot, but it is a blind spot in the instrument
   every other claim in this document rests on (L-GAP: ask what the suite
   cannot express before reading its silence as agreement).
2. **A hand-run byte compare disagrees with the battery**, and the battery
   is the one that is right about pass and fail. On 2026-07-30 a byte-exact
   comparison reported `e1000-phy` and `e1000-phy-absent` as failing; they
   pass, and the whole difference was the leading SOH in their sidecars. An
   agent who trusted the byte compare would have reported a red test to
   another lane and sent them hunting a defect that does not exist.

**So: record an `.expected` through `build/test-run.ps1`, never by copying a
raw `-output` file.** That rule already existed for the CR and the
`HEAP:` lines; the SOH is the part that survives the mistake silently,
because the CR and the extra lines would fail loudly.

**Why the comparison has not simply been changed to ordinal.** Making it
`[string]::Equals(..., 'Ordinal')` is a two-word edit that turns **139 tests
red in one run**, none of them for a real defect. The repair on the other
side is equally mechanical (strip a leading `0x01` from each sidecar), but
doing both at once is a fleet-wide event in the battery, which is Damian's
tool and Damian's call. It is written down here rather than done quietly.

### 181 of 425 foreword chapters are never asked whether they answer correctly

Measured 2026-07-27, once, by hand. For every chapter under `codex/foreword`,
the question was whether any test anywhere in `codex/test` both cites it and
computes something rather than printing a fixed string:

| | |
|---|---:|
| foreword chapters | 425 |
| cited by a test that computes | 244 |
| **cited only by a compile smoke test** | **181** |
| cited by no test at all | 0 |

**The compile tests are not the problem and are not being criticised.**
`codex/test/forewords/` exists to prove each chapter still compiles, and it
does that, and it fails when a chapter does not. The gap is that for 181
chapters nothing else exists, so a chapter can compile forever while answering
the wrong number.

That is not hypothetical. `ConsistentHash` was in this set. Its only test
printed `Foreword/ConsistentHash OK` while the ring sent 993 of 1000 keys to a
single node. `Probability` was NOT in this set, which is why its negative CDF
was at least visible behind a skip.

**The method is a first-order proxy and overstates coverage in both
directions.** A test that cites a chapter and calls `show` on something
unrelated counts as covered, and a chapter exercised indirectly through another
chapter's test counts as uncovered. Treat 181 as the right order of magnitude
and not as a roster.

**This is a one-off census, deliberately, and no instrument was built for it.**
Damian's 2026-07-27 ruling stands: the cost of discovery is lower than the cost
of continuous maintenance, so a coverage gate is declined. Re-run it by hand if
the number is ever wanted again.

### A `.skip` is a claim, and nothing re-tests it

**The skips were not healthy.** A skip reason is the one kind of claim in this
tree that no runner ever revisits: the battery reads it and believes it,
forever. Of thirty-three, four were probed by hand on 2026-07-27 and **three
were stale**:

| test | the skip said | measured |
|---|---|---|
| `let-shadow-scope` | known-failing: a shadowed `let` emits 13, want 9 | **answers 9** -- a documented MISCOMPILE, fixed and left buried behind its own skip |
| `linalg-test` | mat-mul GPFs at runtime | **passes** |
| `rp2040-drivers` | the harness does not pass `-board-mmio` | **passes** with a one-line `.vmargs`; so do `stm32l4-drivers` and `pi4-drivers` |
| `probability-test` | `normal-cdf 0` returns -253 | **still true** at that probe, and it is a negative probability. Fixed 2026-07-27; the skip is deleted and the test is in the `-Lib` battery |

All five stale skips are deleted and those tests are in the battery. The board
trio is the instructive one: the flag was read as a property of the BATTERY when
`.vmargs` makes it a property of the TEST, so three driver tests sat out for
want of one line each.

`build/audit-skips.ps1` is the instrument. It compiles and runs every skipped
test that has an `.expected`, with the battery's own sidecar semantics, and
reports STALE (passes and the expected output looks like an assertion), TRIVIAL
(passes because it asserts nothing), REAL (still differs) or UNRUNNABLE (no
output inside the wall budget, or does not compile). UNRUNNABLE is the honest
bucket for a test that genuinely needs hardware, a human or a display: the
script cannot tell that from a hang and does not pretend to.

```powershell
pwsh build/audit-skips.ps1                 # all of them; boots one VM per test
pwsh build/audit-skips.ps1 -Only rp2040    # substring match on the name
```

**It has no exclusion list on purpose.** A hand-maintained list of
tests-not-to-audit would be one more claim with no runner behind it, which is
the thing being audited.

It is on-demand and not a gate, and that is the side of Damian's 2026-07-27
ruling it belongs on. **This paragraph used to state that ruling wrongly**, as
*"coverage machinery is declined, and any item phrased as 'X runs under no
harness' needs asking first, default no"*. He corrected it the same day: the
objection is to **putting harnesses in the standard battery**, not to building
them. `-All` already runs about an hour and reports no failures almost every
time, so growing it slows the fleet for no signal -- targeted collections
invoked when pertinent are what is wanted, with the extra proofing saved for a
public push. **Build the instrument; do not gate it.**

### The full census, 2026-07-27

Twenty-eight skips at the time of the run. Nineteen had an `.expected` and could
be judged; nine did not and are named as unjudged rather than counted as
anything.

| verdict | n | what it means |
|---|---:|---|
| TRIVIAL | 7 | passes, and could not fail |
| STALE | 2 | `gpu-vecadd-e2e` (skip deleted) and `ir-probe` (skip still right) |
| REAL | 10 | the reason held |
| UNRUNNABLE | 0 | |
| no `.expected` | 9 | nothing to compare against |

**Two rows moved underneath this within the hour and the totals above are the
run, not the present.** Main 10717 landed val's `probability-test` fix, which
retires one REAL row and deletes that skip; fester's `bundled-agent-heap` adds
one skip with no `.expected`. At submit time the inventory is **27 skips, 17
judgeable**. Re-run before quoting either set.

**The seven TRIVIAL rows are the finding, and the first run of the script got
them wrong.** It filed all nine passes as STALE under the heading "delete the
`.skip` and let the battery run them". Seven of those have a body that is one
`print-line-uni` of a literal -- `first-boot-ceremony` runs no ceremony,
`fat16-read-test` never opens a disk, `gpu-bridge-test` calls `gpu-bridge-init`
and throws the result away before printing a constant. Taking the advice would
have put seven tests into the battery whose green means nothing, and **a green
test that cannot fail is worse than a skip**: a skip reads as "not covered".

Their skip reasons were the actual damage. Every one claimed an environment
limitation -- "requires GPU hardware", "requires RDRAND", "cannot run
headlessly" -- which reads as a real test the lab cannot host. There is no test.
This is the same class the Unwritten Tests section below already describes at
roughly sixty entries, wearing a more respectable reason. All seven now say
`stub:` and say what the body actually does.

`gpu-vecadd-e2e` was the one genuinely stale skip: it said "requires GPU
hardware and running gpu-dispatch process", and the body only exercises
host-side launch-config arithmetic (grid 4 from 1024/256, tid 511, grid 16 from
4096/256). It passes headless, the skip is deleted, and it is in the battery.

`ir-probe` is the case that shows why STALE is not an instruction. It passes,
its expected output is a computed `sum=31` rather than a bare "ok", so the
heuristic files it correctly as STALE -- and its skip reason, which the report
prints in full, is a precise account of why the test proves nothing about the
IR and is blocked on a build decision. The skip stays. **Read the reason before
deleting anything; the verdict is evidence, not an instruction.**

### `.stdin` and `.keys` are different machines

`.stdin` is pumped into the **serial ring**, which is where `read-line`
looks. A keyboard read -- `uefi-read-key`, and `poll-key` above it -- reads the
**PS/2 key cell** instead, and no quantity of `.stdin` has ever reached it.

That is the whole reason a dozen tests are skipped as "blocks waiting for
keyboard input": the battery had only the wrong input, and the skip reasons
recorded the resulting silence as an environment limitation. codex-vm has
accepted a scancode timeline through `-keys-file` for as long as
`build/test-gui.ps1` has driven GUI apps with one; the default harness simply
passed no such flag. It does now.

**Pick by what the code under test reads, not by what it feels like.** Reading
the source is the only reliable way, and getting it wrong is not theoretical:
`diagnostic-boot` was skipped for years as keyboard-blocked and its shell
reads `read-line` -- plain serial. It runs today on a `.stdin` sidecar, in
under a second. `vga-shell-test` still carries both a `.stdin` and a
keyboard skip reason, which cannot both be right.

`codex/test/keys-sidecar` is the worked example and the harness's own
regression: it types `A B Enter` as make codes 30, 48, 28 and must report
65, 66. It has no fuel cap on purpose -- a keyboard test that stops early
would pass while proving nothing, so not receiving a key must be a failure,
and the wall budget is what calls it.

### A sidecar names a test, and `build/check-sidecars.ps1` checks that it does

Sidecars are resolved **next to the source**: `codex/test/apps/foo.codex`
takes `codex/test/apps/foo.skip`, and a `foo.skip` one directory up is read
by nothing. Such a file is worse than absent -- it reads like a decision.
`diagnostic-boot.skip` sat a directory above its test making three claims,
none true: it skipped nothing, the test was never run anyway (no `.expected`),
and its stated reason was the wrong subsystem. This table quoted it as a
legitimate skip the whole time.

`build/check-sidecars.ps1` hard-fails `build.ps1` on any sidecar with no
`.codex` beside it. Nothing else can see this class: a sidecar that names no
test is exactly the file no compile and no battery opens.

### The diagnostic catalogue is read by the build, and `build/check-cdx-registry.ps1` reads it

`codex/compiler/Core/CdxCodes.codex` is a registry of every CDX code with its
name, severity, phase and one-line summary. **Nothing in the compiler reads
it** -- `cdx-lookup` has no caller, and whole-program dead-code elimination
prunes the whole table (measured: changing a summary leaves `Sut.cdx`
byte-identical to the seed, so a string the compiler reached would have moved
the binary). A code raised with no row, a row for a code nothing raises, or a
`Name` that has drifted from its constant is therefore invisible by
construction -- which is how CDX6011 came to call an instruction-count budget
a "byte threshold" with nothing to catch it.

`build/check-cdx-registry.ps1` is the reader. It parses the constant->code
table, the registry rows, and every `cdx-*` reference across `codex/compiler`,
and fails `build.ps1` on three disagreements: a code **raised but not
registered** (`cdx-lookup` would answer "Unregistered CDX code"), a row
**registered but raised by no site** (a summary nothing can ever show, the
CDX2043 class), and a row whose **`Name` is not the PascalCase of its
constant** (the one half of a summary a machine can check). It found **13
undocumented codes on its first run** (the `if`/`let`/`act`/record-field/
Real-equality parser and checker errors), now rowed. It does not verify
summary PROSE against behaviour -- no build-time check can, and the registry
chapter is not citable so a Codex test cannot read it either (the 7.25
blocker) -- so a wrong summary on a correctly-named, correctly-raised code
still passes. What it stops is the structural rot: a code with no
documentation, and a table that describes codes the compiler no longer emits.

## Current State (2026-07-13, seed 5A6B432B...)

Both batteries were re-run from scratch on 2026-07-13. **These are
measured numbers, and they are already going stale -- re-measure before
you quote them. Never carry a count forward.**

| Battery | Command | Total | Pass | Fail | Skip |
|---|---|---:|---:|---:|---:|
| Default | `build/test.ps1 -Jobs 4` | **342** | **328** | **0** | **14** |
| Full | `build/test.ps1 -Apps -Jobs 4` | **598** | **498** | **9** | **91** |

The default battery is green. **The catalogue of `-Apps` failures that used
to sit here is gone, because every row in it was re-measured individually
on 2026-07-20 and every one now passes**: `boot-stage-test`,
`quaternion-test`, `spark-shapes-test`, `spark-mesh-test` and `wave3-test`
compile and match their `.expected` with no change needed at all -- they
had been fixed by other work and nobody re-measured. `erp-server-test`
passed too and was `.skip`ped on a CDX2033 that no longer occurs; the skip
is deleted. `historian-test-full` had no `.expected`, so nothing ran it;
it has one now. `db-full-test` was the one row with real defects behind it
and they are fixed (CL 9850).

`nic-ping` was the ninth and was **fixed 2026-07-13** (val).
`xhci-discover-test` was the eighth and is **gone entirely**: its runtime
failure was a bounds contract, fixed, and the test then went with the kernel
xHCI driver when that duplicate stack was retired (CL 9180, fester). That is one
fewer entry, not a re-measurement.

**The header's `9` is therefore stale in the direction of pessimism, and it
stays as written because the battery itself has not been re-run.** A count
in this document is only ever the number some run actually produced; per-test
re-measurement retires the rows it covers and does not license editing a
total nobody measured. Re-run before trusting any of these figures.

`codex/test/errors/` holds **173** expected-failure tests (measured
2026-08-10; this line said 172 on 07-31 and 162 on 07-16).

## What the standing gate does not cover

`build/build.ps1` is the gate, and three whole classes of change can pass it
while broken. None is a defect in the gate; all three are things it was never
pointed at.

**Transpiler plugs: the `plug-smoke` phase is four plugs, one input, and a
binary it may not have rebuilt.** It runs typescript, python, rust and ptx
against `codex/plugs/test-input/hello.codex`, and it rebuilds a plug **only
if that plug's CDX is missing**. `codex/plugs/*/build-output/*-plug.cdx` is
not in the depot, so those binaries are as old as the last hand run of the
plug's own `build.ps1`.

Both halves of that were load-bearing on 2026-08-11. Forty-four plug
binaries in one workspace dated 08-06 against a shared
`codex/plugs/common/IRTextParser.codex` last changed 08-08; **37 of 38
runnable plugs faulted with an invalid opcode in `parse-type-record` on IR
containing a record construction, and 38 of 38 handled `hello.codex`
perfectly.** `hello.codex` has no record in it, so the phase was green
throughout. Rebuilding every plug took 150 s and made all 38 produce output.

**A green `plug-smoke` therefore means four plugs, of whatever vintage, on
one record-free program.** After touching anything in `codex/plugs/common`,
rebuild the plugs yourself; the gate will not. And do not read "produces
output" as "correct" -- of those 38 rebuilt plugs, 36 emit a field access as
a division (`codex/plugs/plugs-backlog.md` 1.2) while exiting 0.

**Devices and MMIO.** The gate exercises no device model beyond what the
compiler itself touches. After a change to a driver, a board or codex-vm,
run `build/boards-test.ps1`, the `hda-audio` / `mic-peak` / `display-ops`
tests, and the `smp-*` tests at `-Smp 4`. When adding a device to codex-vm,
give it an `mmio_decode` entry rather than a fourth register heuristic. The
three board-driver tests are in the battery via a `.vmargs` sidecar carrying
`-board-mmio`, so a plain battery covers more than it once did, but the gate
still does not.

**Applications: the gate DOES cover these now, as of main 13855.**
`build/build.ps1`'s `app-sweep` phase runs
`sweep-app-classes.ps1 -Check -Jobs 8` over every app entry chapter and
fails the gate on a regression against `build/app-sweep-baseline.txt`. It
runs LAST because it copies the seed over `build-output/bare-metal/Codex.cdx`
to compile with, which would clobber the stage0 the fixed-point phases
compare; by then SUT === seed is proven, so the seed it picks up is the
compiler that run built. Both arms were proven before it was wired in: a
sabotaged entry chapter turns the gate red naming it, and reverting turns
it green. Cost measured 2026-08-06: 191 s of a 517 s gate.

**Six app entry points do not compile DELIBERATELY** and are catalogued
with their reasons in `build/app-sweep-baseline.txt`. Read that file
before touching any of them, and before reading a non-zero dirty count as
a regression: the check compares against the baseline, not against zero.

**Backends other than x86. A BVT pin is an x86 pin.** The gate compiles the
compiler and boots x86; it never boots a plug, so it is silent about ARM64
and RISC-V however green it is. The trap is not that the coverage is missing,
it is that adding a pin FEELS like closing the question: measured 2026-07-29,
three Real-width defects were fixed in the x86 emitter and pinned in
`bvt.ps1`, and all three reproduce unchanged on the ARM64 plug -- f32
`-0 ~ +0` answers False, a moded-Real field prints nothing at all, and f32
saturating clamps to the wrong constant. **A defect fixed in the emitter can
still be sitting in a plug, because they are separate implementations of the
same decision.** After a change to width handling, a print walk or anything
else a plug re-implements, run `build/test-cross.ps1 -Arch arm64 -Test <name>`
on the tests you just pinned. It is one test and about a minute.

**A fault the runtime handles by HALTING cannot be expressed as a test, and
this is an open gap rather than a rule.** `__out_of_memory` ends in
`cli; hlt; jmp -6`, and so does `__watchdog_panic`. A test that correctly
trips one of them prints its line to serial and then never exits, so the
harness records a timeout, which is how it reports a hang. Pass and the worst
kind of fail are the same observation.

The consequence is that the heap and stack guards have no runner. `cmp rsp,
r10; jb __out_of_memory` is emitted into every non-leaf prologue and is the
only thing standing between a runaway recursion and a silently corrupted
stack, and nothing in the battery has ever watched it fire.
`act-tco-loop` comes closest and points the other way: it asserts the
constant-stack path completes, so the collision is its FAILURE mode, never its
subject. Measured 2026-08-03, the same invariant is unguarded in the other
direction entirely -- `__str_concat` bumps the frontier past the stack with no
check at all -- and no test in the tree could have said so.

Deciding what a passing halt-test looks like is the work. The shape is
probably a sidecar declaring the expected end state, so the runner can treat
"halted after emitting this line" as a pass and a bare timeout as a failure,
the way `.expected` already declares the output. Until that exists, a guard of
this class is verified by hand, in two arms, and the account goes in the CL:
see `docs/Designs/Active/Compiler/ProportionalDecks.md` for one worked
example, including the probe and the reason its result was negative.

## Standing bed facts (each cost a session; do not relearn)

Consolidated 2026-08-08 out of the retired per-agent workplans.

- **Desk rehearsals in codex-vm MUST pass `-disk`.** With no disk the
  font-mount fallback resets the controller after the HID walk and the
  mouse handle dies, bed-only. The image itself is fine.
- **`build/boot/test-ovmf.ps1` boots a TEMP COPY**
  (`$env:TEMP\ovmf-disk-<workspace>.img`), so guest disk writes do not
  persist to the input file. To chain two boots, copy that temp file back
  as the next run's input. `-Keys` takes Set-1 make codes (`88` is F12);
  `-KeyDelayMs 4000` survives TCG-speed keygen.
- **A pane F12 shot takes roughly 16 to 110 s in the bed**, because
  `med-selected` is unset under DeskVm so every sector op pays the AHCI
  and NVMe probes before falling through to IDE. **A capture deadline
  under two minutes reads as a dead key**, and the guest genuinely IS
  blocked inside `shot-take`, so it is indistinguishable from a hang.
  `build/desk.ps1 -ShotDelayMs` defaults to 6000. Give the capture two
  minutes and **verify by scanning the disk copy for the `SH*.BMP`
  directory entry, not only by the on-glass verdict.** Before calling an
  input-driven bed incapable, price the work the keystroke starts: a
  1280x800 screenshot is 3,072,054 bytes, and a negative control shorter
  than the write can only ever return what it returned.
- **`print-line` emits CCE; `print-line-uni` converts at the I/O
  boundary.** A hand-written probe using `print-line` renders as garbage
  and looks exactly like a miscompile. The control that settles it is any
  existing `codex/test` unit through the same path.
- **A hand-rolled compile-and-run harness must DELETE its artifacts
  first.** `Test-Path $cdx` finds the PREVIOUS run's binary, so a failed
  compile is indistinguishable from a pass the moment a stale artifact
  exists, and the second run of any harness is where this starts.
- **An unreferenced definition is not a reached path.** A sabotage that
  puts an undefined name in a new top-level definition nothing calls
  compiles clean. Sabotage a call site inside a body that RUNS, and treat
  an arm that does not fail as a bug in the arm.
- **`check-generated-scripts.ps1`'s RUN phase is intermittently flaky and
  retries once.** A generator can compile clean (exit 0, a full `.cdx`)
  and still leave a zero-byte `emitted.txt`. Not reproduced in 240 runs of
  the kernel that had just failed, and `test-run.ps1`'s own two
  empty-output paths were silent throughout, so neither is the cause. The
  check prints a `note:` line when the retry saves it. **If that line
  starts appearing every run, the flake has become a defect and the retry
  is hiding it.**

## Battery architecture

99 individual tests are consolidated into smoke bundles (`unit-smoke`,
`rt-smoke`, `try-smoke`, `prose-smoke`, `linear-smoke`, `linear-errors`,
`mutable-smoke`, `typeclass-smoke`, `handler-smoke`, `record-smoke`,
`lang-smoke`, `bs3-smoke`, `punctual-smoke`). Each exercises several
features in a single VM boot, cutting battery time ~60%.

**BVT mode is what `build/build.ps1` runs by default**: **73 tests**
(measured 2026-07-29; this line said "a 10-test subset, ~18 s"). The list
and the reason for each entry are in `build/bvt.ps1` itself, which is the
register -- count it there rather than quoting a number from here. That is
the standing gate. The full battery (`build/test.ps1`) is Damian's tool and
is not an agent command.

### Foreword Battery (`build/test.ps1 -FW`)

Per-chapter compile tests for all foreword modules. Not included
in the default or `-Apps` battery. Some foreword tests have large
dependency chains (~127s compile) and are marked `.slow`.

## The repro you kept is the test

**Every fix ships the smallest program that failed.** Not as ceremony
afterwards -- you built one to find the bug, and the rule is only that you
keep it.

The reason is `docs/PM/Active/Stories/Opus.md`'s, and it is about the search
space before it is about coverage: a 40-line file that reproduces the bug
gives you a 40-line search space, and the codebase gives you the whole
codebase. The ratio matters more than anything, which is why the minimal
repro is the debugging tool and not the paperwork. Having paid for it, keep
it: the thing that found the bug is the thing that proves the fix, and the
thing that stops it coming back.

**Whatever can see the failure is the instrument.** The by-construction
holes are the loudest example -- linear laundering, effect laundering,
capability laundering, bounded-signature out-of-range each shipped its
adversarial probe as a `codex/test/errors/*.failing`, which is why the error
battery is the fastest-growing part of the suite -- but a `.failing` is what
you use when the compiler must *reject*, not what the rule is. A wrong
answer wants a `.expected`. Code that is correct but wasteful wants a
**bench**: no correctness test can see a redundant instruction, because the
program computes the right answer either way. That is what `bench/` is for,
and why it is in the battery layout above despite not being a battery.

**The instrument has to be able to fail** -- that much this document says
twice already. It also has to be able to fail *at the thing that changed*,
and a suite can be blind by construction rather than by accident. CL 8600
taught the epilogue fall-through to skip blocks that emit nothing and, by
naming a block one way and the layout another, made an arm emit a five-byte
jump to the instruction underneath it. Nine benches were green. All nine are
tail-returning, so not one of them had a non-tail join exit at all -- the
shape was not under-covered, it was absent. Ask what your suite cannot
express before you read its silence as agreement.

**Pick the instrument before claiming the shape is covered.** Sometimes the
obvious one is provably blind. Sharing an epilogue against duplicating it
costs exactly the same number of instructions at two returns and a
two-instruction epilogue -- the equality is arithmetic, not luck -- and
differs only in bytes and executed jumps. A bench counting instructions
there is a test that cannot fail, so the harness reports bytes too. This is
the same sentence as "a function that always answers the same thing looks
exactly like one that works," pointed at the measurement instead of the
code.

## Cross-Architecture Battery

The cross-architecture test harness compiles tests from `codex/test/` and
`codex/test/ops/` -- those two directories only, and see the coverage
measurement below before reading that as the battery -- to ARM64 or
RISC-V ELF via the plug pipeline
(source -> IR -> plug codegen -> ELF writer), then boots the ELF on
Renode (cycle-accurate board simulation) or QEMU and compares UART
output against the `.expected` sidecar.

### Pipeline

```
source.codex -> compile.ps1 (IR mode, x86-64 seed)
             -> arm64/riscv plug CDX (codegen)
             -> compile-arm64/riscv.ps1 (ELF builder)
             -> Renode or QEMU (UART capture)
             -> compare against .expected
```

### Commands

```powershell
build/test-cross.ps1 -Arch arm64 -Test <name> -TimeoutSec 10   # single test
build/test-cross-batch.ps1 -Arch arm64 -RenoTimeout 10         # full battery (Renode)
build/test-cross-batch.ps1 -Arch arm64 -UseQemu                # full battery (QEMU)
build/test-cross-smp.ps1 -Arch riscv64 -Test smp-riscv-boot    # one multi-core test (QEMU -smp N)
build/test-cross-batch.ps1 -Arch arm64 -Filter lir-            # a subset by name
build/test-cross-batch.ps1 -Arch arm64 -CompileTimeoutSec 2    # provoke the retry (see below)
```

### What the cross battery actually covers, and the word "parity"

**Measured 2026-08-02, by enumeration, not from any doc.** Re-measure before
quoting; every number here rots (L-COUNT).

| | count |
|---|---|
| `.codex` tests under `codex/test/` recursively | 1404 |
| tests the cross battery ENUMERATES | 524 |
| of those, eligible after sidecars | 421 |
| of those, skipped | 103 |
| tests never enumerated at all | 880 |
| programs the STANDING GATE boots per architecture | 1 |

The 880 are not skipped, filtered, or reported. `test-cross-batch.ps1:67`
globs `codex/test/*.codex` and `codex/test/ops/*.codex` and nothing else,
non-recursively, while the x86 battery globs six directories
(`test.ps1:157`). So `apps/` (364), `forewords/` (316), `errors/` (173),
`lib/` (40) and `examples/` (1) are outside the cross battery's field of
view. **A test in those directories has never run on ARM64 or RISC-V and
its absence produces no line of output.** That is the difference between a
skip, which is a recorded decision with a sidecar naming a reason, and a
blind spot, which is silence.

### What the eligible tests actually score

Coverage is one number and passing is another. Read from the harness's own
`test-output-cross/<arch>_cross_results.md`, **written 2026-07-29** and
untracked build output, so re-run before quoting:

| status | ARM64 | RISC-V |
|---|---|---|
| PASS_EXPECTED | 370 | 271 |
| PASS_COMPILE_ONLY | 7 | 7 |
| PASS_REFUSED | 14 | 14 |
| **FAIL** | **20** | **119** |
| SKIPPED | 95 | 95 |
| total rows | 506 | 506 |

**RISC-V fails 119 of the tests it is actually given.** That cluster is
known and open: `docs/PM/CurrentPlan.md` carries it as roughly 90 rows, a
different run of the same thing. Anyone repeating "parity" needs to hold
this table and the coverage table at once: 421 of 1404 eligible, and of
those, one lane failing about a quarter.

The 103 skips break down as 32 `.no-cross` (each with a written reason), 30
machine-sidecar (`.vmargs`, `.disk`, `.keys`, `.disk-src` -- the fixture is
the x86 codex-vm and no cross board can mount it), 14 `.fatal`, 10
`.failing`, 7 `.smp`, 7 `.skip`, 3 `.slow`. Most of the `.no-cross` reasons
are one structural fact: **the cross lane boots a bare runtime with no
kernel and no disk**, so the process table, the capability words and FAT16
have no cross coverage by construction.

**On "parity."** The measured result is `docs/PM/Milestones.md`, 2026-06-15:
ARM64 and RISC-V meet or beat **GCC -O0 on four micro-benchmarks**. That is
a statement about generated-code SPEED on four programs. It has been
restated elsewhere as "ARM64 and RISC-V backends at parity" with the
qualifier dropped, which is not what was measured and is not true.
`docs/Designs/Done/IoT/CrossArchitectureTestStrategy.md` said so at the time
-- *"What is running today is narrower and should not be described as
parity"* -- and then went into `Done/`, which init does not read.

**So the honest sentence is: the cross backends are fast, their coverage is
421 of 1404 with one program gated, and RISC-V fails 119 of what it runs.**
Do not write "parity" without naming the four benchmarks it refers to.

### What is genuinely unimplemented on the cross lanes

Distinct from untested. Both plugs refuse rather than miscompile, and each
refusal is pinned by a `.cross-refusal` sidecar (18 of them), so a refusal
arm that silently disappears turns the row red instead of emitting wrong
code. Refusals carry an `[UNSUPPORTED]` line on the compile log.

**Two real capability gaps, both on ARM64 and RISC-V:**

| capability | refused intrinsics | note |
|---|---|---|
| builtin-List pattern matching | `__list-len`, `__list-head`, `__list-tail` | a language feature absent on both lanes |
| file read | `read-file`, `read-file-raw`, `read-file-uni`, `uefi-read-file` | `docs/Designs/Active/Compiler/CrossLaneFilesystem.md`, not started |

**Sixteen further refusals are architectural, not gaps.** Port I/O
(`port-in/out-byte/16/32`), the codex-vm GPU ports (`gpu-in`, `gpu-out`,
`gpu-mem-read`, `gpu-mem-write`), CPUID (`cpu-cpuid-eax/ebx/ecx/edx`) and
x86 control registers (`cpu-read-cr0`, `cpu-read-cr3`) describe hardware
that does not exist on ARM64 or RISC-V. Refusing them is correct and
counting them as missing features would overstate the gap.

So the count that matters is **two capabilities, not sixty**. The number
people reach for comes from adding the architectural refusals to the skip
list; those are different things and the sum is not meaningful. What is
genuinely unknown is the 879.

### The parallelism default, and the flake that was not the harness's

`-Jobs` defaults to **8**. It was 4, and the 4 was a workaround: at eight
Renode slots a passing test would come back `FAIL_RUNTIME` with no uart after
about two seconds. That was investigated twice, no cause was found inside the
harness, and the default was halved instead.

**The cause was the machine.** Its DDR5 was running an XMP profile it was not
stable at. With the memory back in spec the box has run 25+ concurrent VMs with
no fault (2026-07-22), and the default is 8.

Worth keeping because the shape recurs: two investigations that cannot find a
cause inside the software are evidence about where the cause is not. A
workaround written into a default outlives the condition that justified it and
then reads as a property of the harness. If slot-count flakes return, suspect
the hardware first.

**And it recurred here, in this document's own neighbours. RULED 2026-08-02 by
Damian: `-Jobs 8` everywhere, release proofs included.** The default above went
back to 8, but the halved number survived in every place that had copied it out
as a literal: the poison recipe in `OperatorsManual.md` said `-Jobs 4`, the
release skill said `-Jobs 3` **and told the reader not to raise it**, and
`sweep-app-classes.ps1` defaulted to 6 on a measurement taken 2026-07-20, two
days before the XMP fix that killed its cause. The cost was paid on 2026-08-02:
a release poison battery spent **977 s in its compile phase at 4 slots on a
12-core box**, because the recipe said 4 and the recipe was followed.

**A default that is corrected in one place and copied as a literal in five is
not corrected.** The paragraph above had the right answer and the right general
lesson written down, and neither reached the scripts that had already
copied the wrong number out of it.

### An elapsed reading is not a measurement unless its run count is stated

The section above is about contention producing a wrong PASS/FAIL. It
produces wrong TIMES the same way, and a time has no retry pass behind it to
catch that.

Measured 2026-07-29: three consecutive 8-slot runs of one **unchanged** test
list came back **20.4s, 22.8s and 32.6s**. `build/bvt.ps1`'s header had been
built out of four single readings taken before and after each addition
(16.2s at 59 tests, 18.7s at 66, 20.3s at 67, 22.1s at 71) and presented as
the cost of the tests added. Two of those deltas are smaller than the
12-second spread on a list that did not change, so the progression was
measuring other agents and calling it test cost.

**So: say how many runs a time came from, or do not publish the time.** A
count can be re-derived by anyone later; an elapsed number cannot, because
the conditions are gone. This is the same rule as stating the pattern beside
a survey and the seed beside a battery result.

### The BVT's own compare step can die with a PowerShell type error, once

Seen 2026-08-11 (fester) in the gate's `test-bvt` phase, in the parallel
run block, immediately after `trust-vouch-depth`:

```
InvalidOperation: 32 | $actual = $actual.TrimEnd("`n")
  Method invocation failed because [System.Object[]] does not contain a
  method named 'TrimEnd'.
```

The line is `build/bvt.ps1:283`; the "32" is the offset inside the
`ForEach-Object -Parallel` block, not a file line. It is the HARNESS
failing, not a test: `Get-Content $runOut -Raw` handed back an array
where a single string was expected, so no verdict was reached for that
test and the phase aborted.

**It did not reproduce.** BVT standalone passed 135/135 immediately
after, and a second full `build/build.ps1` was green end to end. The
mechanism is not established and this note deliberately does not guess
one. Recorded so the next person to hit it knows it has been seen once,
is not caused by whatever they just changed, and is worth a re-run before
any investigation. If it recurs, capture `$runOut` and its directory
before anything else: the answer is in what that path matched.

### The BVT has NO retry, so contention reads as a red gate

The batch retries contention-shaped failures (next section). `build/bvt.ps1`
does not, and it runs 8 compiles at a time at roughly 3 GB each.

Measured 2026-08-11 with another agent's VM live on the box: a gate run
reported `induction-assoc` and `reverse-reverse` as `FAIL (compile)` while
every other one of the 75 passed. Both are the heaviest proof units in the
list. Compiled alone against the same `build/output/Sut.cdx` immediately
afterwards, both exit 0 and emit 84,577 bytes, and the next full gate was
green with the identical script.

So a BVT compile failure confined to the heavy proof tests, on a box that
is not idle, is contention until shown otherwise. **Confirm it the cheap
way before believing it**: compile the named test alone with
`build/compile.ps1 -Kernel build/output/Sut.cdx` and read the exit code. A
red BVT with a broad spread of failures is a different thing and is not
this.

### A load flake is retried before it is believed

**The batch used to report contention as a test result, and it cost three
sessions of re-diagnosis.** A compile is given a wall-clock budget while
`-Jobs` of them run at once, so a large test can exceed it because the box is
busy; the harness recorded that as `FAIL_COMPILE`, which reads exactly like a
broken plug. `db-full-test`, `gpu-depth-tree` and `gpu-panel-border` flipped
between `FAIL_COMPILE` and `FAIL_OUTPUT` from run to run on that alone, and
the standing instruction *"always re-run a batch FAIL standalone before
believing it"* was a manual step covering for the harness.

The harness takes that step itself now. After each phase, the failures that
are **contention-shaped** are retried once, alone:

| Phase | Retried | Not retried |
|---|---|---|
| Compile | `timeout (Ns)` | a non-zero exit **with** a compiler diagnostic |
| Compile | a non-zero exit with **no** diagnostic | |
| Run | `no uart output` | `FAIL_OUTPUT` -- a deterministic wrong answer |

**The no-diagnostic class was added when `-Jobs` went to 8**, and it is the
distinction that keeps "never re-run a real failure" true rather than merely
stated. A compile that rejects a program always says so: a CDX-numbered error
or `CODEGEN-HALTED` lands in the log. A compile whose VM was killed under load
exits non-zero and says nothing. Before eight slots the second class was rare;
on the first 8-job run `chapter-pages` hit it, reported `FAIL_COMPILE`, and
passed standalone -- a phantom regression of exactly the kind the timeout retry
exists to prevent. Both directions were fired rather than assumed: an injected
one-shot no-diagnostic exit recovers (`compile 1/1 recovered`), and a
deliberately broken source exits 3 **with** diagnostics and is not retried at
all (`compile 0/0`).

**A wrong answer is never re-run.** Re-running one spends time and, worse,
would let a genuine intermittent defect be dismissed as a flake. The
distinction is the whole design: silence under load is the machine's fault,
a wrong answer is the program's.

**Every run states its retries**, including when there were none:
`**Serial retries**: compile 0/0 recovered, run 0/0 recovered`. A run that
silently absorbed a flake would read identical to one that never had a flake,
and that difference is exactly what a per-test diff against a previous run
trips over.

**Both paths were fired rather than assumed**, because an unfired guard is
worth what no guard is worth:

- **Compile retry recovers**: `-CompileTimeoutSec 2 -Jobs 4` makes four
  contended compiles time out at 2.0s; all four compile alone in 1.8-1.9s and
  the run reports 4 PASS where it would previously have reported 4
  `FAIL_COMPILE`.
- **Compile retry does not rescue a real failure**: at
  `-CompileTimeoutSec 1` the test times out alone too and stays
  `FAIL_COMPILE`, noted `still failing alone`. A deliberately broken source
  exits non-zero and is **not retried at all** (`0/0`).
- **Run retry recovers**: proven with a one-shot injected `no uart output`
  (fail first attempt, pass on retry) -- 1 of 1 recovered, the row noted
  `passed on serial retry`. Made persistent, the same injection gives 0 of 1
  and the row reads `still silent alone`. The injection was removed; it is
  recorded here because no test in the tree currently produces this class, so
  there was nothing real to fire it with.
- **A wrong answer is untouched**: a valid program with a deliberately wrong
  `.expected` stays `FAIL_OUTPUT` with `run 0/0 retried`.

`-CompileTimeoutSec` exists to make the first of those provocable on demand;
it is also why the budget is no longer a literal buried in the parallel block.

### The GATE leg learned the same lesson, two months later

**`build/check-cross-smoke.ps1` had no retry, and it is the one that runs
inside `build.ps1` every few minutes.** That is the wrong way round: the batch
above is out-of-band and careful, while the leg in the standing gate took a
single silent lane as proof that a backend had stopped executing. Renode gets
a **3 second** budget there, several agents share one box, and a gate run is
itself the heaviest thing on it.

Observed 2026-07-28: a gate reported `check-cross-smoke: FAIL (2 of 2
executed): arm64/factorial, riscv64/factorial`, both `no uart output`, on a CL
that changed one test file and four documents. Both lanes passed standalone
seconds later **at the same 3 second budget**, and the whole check passed
again at its default. The gate was red for the machine's reasons.

The leg now retries a silent lane once, alone, on exactly the batch harness's
rule: `FAIL (no uart output)` is contention and is retried; `FAIL (output
mismatch)` is a deterministic wrong answer and is **never** re-run; a compile
failure is neither and is not retried. It prints its retry count on every run,
including `0/0`, because a run that quietly absorbed a flake would otherwise
read identically to one that never had a flake -- which is the difference that
makes a *returning* flake visible.

**All three directions were fired by temporary injection**, because nothing in
the tree produces this class on demand and an unfired guard is worth what no
guard is worth:

| injection | result |
|---|---|
| every call silent | `0/2 recovered`, both `still failing alone`, exit 1 |
| first call per lane silent, then real | `2/2 recovered`, exit 0 |
| every call an output mismatch | `0/0 recovered` -- **not retried at all** -- exit 1 |

The injections were removed and the restored file verified byte-identical to
the intended version before submit. The third row is the one that matters: it
is the check that the retry cannot launder a wrong answer into a pass.

### Four traps when working the cross lanes

Measured 2026-07-28 and 2026-07-29 across the shared-lowering campaign.

**A cross row with no `.expected` reports `PASS (compile only)` and never
boots the program.** The row reads green while proving nothing. Write the
sidecar with the x86 answers before you believe any cross result; the
harness prints exp and act on a mismatch, which makes it a serviceable
bisection instrument for a lane you cannot debug directly.

**The two plug builds CONTEND. Do not run them concurrently.** Running
`codex/plugs/arm64/build.ps1` and the riscv one at the same time made riscv
fail with an empty `build.log`; both pass run serially. Test RUNS
parallelise fine -- it is the builds, not the runs.

**A builtin with no arm in a plug's dispatch falls through to a call on an
undefined symbol, and the compile still reports OK.** Both plugs detect it
and print `[WARN] unresolved call to '<name>'`, and nothing gates on that
warning. An unresolved call also EXECUTES NOTHING, so a missing store is
the larger half of the damage: before main 11864 every `Device.Mmio` store
on both cross lanes silently did not happen, which is every board driver in
`codex/boards`. The free instrument, and the first thing to reach for on
any cross-lane wrong-value row:

```powershell
Select-String -Path test-output-cross\<arch>\<test>\compile.log -Pattern 'unresolved call'
```

**Same wrong value on both arches is at least as likely to mean the same
STRUCTURAL hole as shared runtime code.** Two of the six shared-lowering
rows were read as library defects on that reasoning and were actually both
plugs reading the same stale register.

**A new call in a plug emitter must be declared to the frameless
analysis, or the program HANGS with no output.** Adding an `IrPowInt` arm
that emits `bl` without adding `IrPowInt` to `a64-binary-op-has-call` makes
a frameless leaf clobber x30 with no save, and the `ret` returns into the
loop body forever. riscv has the counterpart at `rv-arg-is-frameless-safe`.
**The tell is layout-dependence: a three-line probe passed and the
eleven-line test hung.**

### Why the unresolved-call warning is still a warning

Measured 2026-07-29, and recorded so it is not re-proposed blind. Promoting
that warning to the `[UNSUPPORTED]` tag `run.ps1` already fails on does NOT
work as written, for two independent reasons found by running a full arm64
batch (360 PASS / 30 FAIL) with the flip in place:

- **The tag is a whole-binary property and the hazard is a per-path one.**
  It fires at link time for any unpatched call, reached or not, so it
  condemns a binary for a call the program never executes. `network-effect`
  passes today precisely because its `net-send-raw` is unreached.
- **Unit TYPE CONSTRUCTORS fall through the same call-patch hole.**
  `Celsius`, `Kelvin`, `Length`, `Metre`, `Name` and `Second` turned up as
  unresolved across eight tests (`unit-smoke`, `unit-show`,
  `unit-real-arith`, `units-foreword`, `unit-family`, `unit-family-mixed`,
  `unit-pattern-lit`, `unit-real-compare`) which pass today while carrying
  unpatched calls to their own constructors. That class is unexplained and
  unowned.

### Multi-core cross tests (`.smp` sidecar)

The committed Renode board `.repl` files are single-core, so a cross test
that needs a second core runs under QEMU instead, via
`build/test-cross-smp.ps1`. A test opts in with a **`.smp` sidecar** whose
first line is the core count (the same sidecar name the x86 codex-vm
battery uses). The runner boots QEMU and compares UART output against
`.expected`, and the two backends boot differently: **RISC-V** boots the
flat `.bin` at `0x80000000` with `-bios none` (every hart auto-enters
`__start`); **ARM64** boots the ELF (QEMU honours its `0x40100880` entry)
and the secondaries are held in PSCI until core 0 starts them. Both use
`-m 1024M` (each backend's boot stack sits ~1 GB above the RAM base). The
single-core batteries (`test-cross.ps1`, `test-cross-batch.ps1`) **skip**
any test with a `.smp` sidecar, so a multi-core test is only ever run by
`test-cross-smp.ps1` -- never single-core, where it would fail by design.

Two tests exist, both `.smp` = 2, both proving a secondary core executes
guest code (closed), each reading a cell only a non-zero core
writes (the `smp-cores` discipline) -- "an ap executed guest code" at
`-smp 2`/`4`, "AP DID NOT RUN" at `-smp 1`. A test that cannot fail proves
nothing; these fail single-core.

- `codex/test/smp-riscv-boot.codex` -- hart 0 boots; a non-zero `mhartid`
  branches in `__start` to an AP path that marks `0x80090000` and parks
  (park-the-secondaries; no SBI).
- `codex/test/smp-arm64-boot.codex` -- QEMU holds the ARM64 secondaries, so
  core 0's `__start` issues PSCI `CPU_ON` (conduit HVC, id `0xC4000003`) to
  start core 1 at an AP stub that marks `0x60000000` and parks.

### ARM64 (last full measurement 2026-06-27, CL 6173)

> **Re-measured 2026-07-28 on the full current battery, and the old
> dispute is retired by replacement: the 135-test lane it argued about
> no longer exists.** The battery reorg tripled the cross-eligible set
> to 492 (60 skipped, 432 run). ARM64 under the committed Renode
> board: **358 PASS_EXPECTED, 7 PASS_COMPILE_ONLY, 17 PASS_REFUSED,
> 50 FAIL** (49 wrong-output, 1 compile). Every one of the 50 also
> fails on riscv64 -- there are ZERO arm64-only failures -- so the
> arm64 plug is clean on everything the riscv plug can also do; the
> shared set is machine-sidecar tests, heavy-compute budget
> casualties under the emulator wall clock, and suspected shared
> intrinsic gaps, all awaiting the cross-eligibility design
> (BatteryReorg step 10). riscv64 the same day (QEMU lane):
> **259 / 7 / 17 / 149**, where ~90 of the 149 are WRONG-VALUE
> answers on coverage the old 153-test lane never ran (aesgcm256
> ciphertext, bezier endpoints, bacnet lengths, acpi revision) and
> every probed one PASSES on arm64 -- a differential that localizes
> them to the riscv plug (routed to reek, red-workplan outbox
> 2026-07-28). One run each; re-measure before quoting.

The 2026-06-27 measurement, for reference:

| Category | Count |
|----------|-------|
| PASS_EXPECTED | 135 |
| PASS_UNVERIFIED | 2 |
| SKIPPED | 17 |
| FAIL | 0 |
| **Total** | **154** |
Skips: 15 pre-existing (error tests, fatal tests, hardware-dependent)
+ 2 slow (tls-test: X25519 DH exceeds Renode sim budget; ui-orchestrator-test:
17-module dependency chain exceeds IR compile budget).

Codegen quality beats GCC -O0 on aggregate across four
micro-benchmarks (static instruction count, AArch64):

| Bench | GCC -O0 | GCC -O2 | GCC -Os | Codex ARM64 |
|-------|--------:|--------:|--------:|------------:|
| fib   |      20 |   237*  |      16 |          21 |
| fact  |      17 |      15 |       9 |          13 |
| gcd   |      21 |       8 |       7 |          23 |
| sum   |      20 |      13 |       9 |          17 |

*GCC -O2 fib transforms tree recursion into a 237-instruction
unrolled loop. GCC -Os is the fair comparison for recursive codegen.

Total: Codex 74 vs GCC -O0 78 -- Codex beats GCC -O0 aggregate.
fact (13) beats GCC -O0 (17) by 4 and beats GCC -O2 (15) by 2.
sum (17) beats GCC -O0 (20) by 3. fib (21) is 1 from GCC -O0 (20).

Key optimizations (CLs 6141-6173): destination-driven emission,
direct arg emission, compact prologue with accurate local counting,
TCO skip-save for stable-reg args, CMP-immediate for comparisons,
peephole MOV eliminator with NOP compaction (branch + ADR offset
adjustment), STP-pre/LDP-post frame merge.

Renode board: Cortex-A53, GICv3, PL011 UART, 1 GB RAM at 0x40000000.
QEMU: `-M virt -cpu cortex-a53 -m 1G -kernel <elf>`.

### RISC-V (2026-06-28, CL 6287)

| Category | Count |
|----------|-------|
| PASS_EXPECTED | 135 |
| SKIPPED | ~18 |
| FAIL | 0 |
| **Total** | **~153** |

135/135 verified tests pass (100% parity with x86-64 battery).
(Superseded 2026-07-28: on the tripled battery riscv64 is 259 pass /
149 fail, ~90 of them riscv-plug wrong-value defects that pass on
arm64. See the re-measure note in the ARM64 section above.)

Codegen quality on all eight micro-benchmarks (static instruction
count, RV64). Four benchmarks beat GCC -Os:

| Bench   | GCC -O0 | GCC -O2 | GCC -Os | Codex RV64 |
|---------|--------:|--------:|--------:|-----------:|
| fib     |      34 |   241*  |      22 |         20 |
| fact    |      27 |      14 |       9 |         14 |
| gcd     |      26 |       8 |       6 |          7 |
| sum     |      27 |      11 |       9 |          7 |
| ack     |      33 |     103 |      22 |         24 |
| tak     |      36 |      33 |      34 |         39 |
| collatz |      29 |      20 |      13 |         15 |
| locals  |      52 |      25 |      19 |         15 |

*GCC -O2 fib transforms tree recursion into a 241-instruction
iterative loop. GCC -Os is the fair comparison for recursive codegen.

Two optimization campaigns (CLs 6159-6172, 6261-6287). Phase 1
(8 CLs): deferred save-reg reduction, destination-driven emission,
frameless TCO, inline builtins, compact prologue. Phase 2 (17 CLs):
pow2 strength reduction (srai/andi/slli), NOP compaction with
B/J-type offset fixup, direct TCO for 2-arg tail calls, dead-jump
elimination, skip-save for simple binary operands, ra-skip for
pure-TCO, expanded frameless TCO with temp-only locals, direct
arg-reg emission for N-arg calls, reordered mixed-TCO (call-first
simple-after), last-arg skip in TCO shuffle.

Renode board: RV64GC, PLIC/CLINT, NS16550 UART, 1 GB RAM at 0x80000000.

### Plug Build

Both ARM64 and RISC-V plugs are standalone CDX binaries built by the
x86-64 seed. Rebuild with `codex/plugs/arm64/build.ps1` (~90s) or
`codex/plugs/riscv/build.ps1`.

## The Chapter-Named Smoke Tests

`audit-skips.ps1` files a skipped test that passes and cannot fail as
TRIVIAL, and that verdict is the useful one in the whole census. The same
shape exists in the tests the battery DOES run, where nothing looks for it,
and it is much larger there.

**Measured 2026-07-29: 294 `.expected` files in `codex/test` consist
entirely of `<something> OK`, and 286 of those name a chapter the test
never calls into.** They are one shape, five lines long:

```
Chapter: FwdBrotliTest
  cites Compress chapter Brotli

Section: Entry
  opening : [Console] Nothing = act
    print-line-uni "Compress/Brotli OK"
  end
```

Re-measure rather than quoting the number:

```powershell
Get-ChildItem codex\test -Recurse -Filter *.expected |
  Where-Object { (Get-Content $_ -Raw).Trim() -match '^[A-Za-z0-9]+([/ ][A-Za-z0-9]+)* OK$' } |
  Measure-Object | Select-Object -ExpandProperty Count
```

**What they prove is real but narrow: the chapter compiles and its cite
resolves.** `codex/test/apps/foreword-all-compile` already proves exactly
that for 420 chapters in one unit, so as coverage they are near-duplicates
of a test that already exists.

**What they cost is the name.** A file called `ai-activation` reads as
coverage of `AI/Activation`, and four broken activation functions sat
behind it for as long as both existed. `math-cordic` is the same file with
a different string, and `cordic-sin` answered a saturated constant for
three quarters of the circle underneath it. `ai-embedding` is the same
again, and the sine half of every positional encoding read zero. In each
case the whole of the expected output was `<Chapter> OK`.

**The rule that falls out is mechanical: grep the `.expected` for a digit
before believing a chapter is covered.** An expected output with no numbers
in it is asserting nothing about any value the chapter computes. Deleting
these is not obviously right -- a compile check is not worthless -- but
counting them as behavioural coverage is what has repeatedly cost real
time, and renaming them to say `-compiles` would remove the whole of the
harm at no cost to what they actually do.

## Probing A Numeric Approximation

A truncated series, an iterative rotation and a range-reduced exponential
are all valid on some interval and nowhere else, and the guard around them
is usually wider than the interval. Where that happens the answer does not
merely lose precision, it changes DIRECTION, and a probe that samples near
zero cannot see it because near zero every version agrees.

Measured examples, all found this way and all fixed:

| call | answered | true |
|---|---|---|
| `attn-exp(-3000)` | 1375, and larger than `attn-exp(-2000)` | 50 |
| `samp-exp(-2000)` | -333 | 135 |
| `cordic-sin(3141)` | 986 | 0 |
| `act-sigmoid-val(5000)` | -854 | 993 |

**Find where the thing stops being valid, then probe past it.** For a
Taylor series that is the root of the truncated derivative: the sigmoid
polynomial's is zero at z=2, so it turns over and descends through zero,
and the exponential's is zero at z=-1, so it turns and rises, which makes
`exp(-2)` larger than `exp(-1)`. For CORDIC it is the sum of the angle
table, 1735 milliradians, past which the rotation cannot retire the target
at all.

**Ask which side the callers actually use.** A softmax shifts by the
maximum before exponentiating, so it asks only for arguments at or below
zero, which was the broken side in every case above. An approximation can
be wrong only where it is used and still be wrong everywhere that matters.

**Count monotonicity and sign rather than eyeballing values.** A row
reading "non-increasing over z=0..-6: 4 of 13" is an assertion; a column of
numbers a reader has to compare by eye is not. Pair it with a control the
defect cannot move, and with a row whose two candidate answers DISAGREE --
an operand pair that gives the same answer either way is decoration.

## Skip Inventory

### Unwritten Tests (the largest and least-visible skip class)

Roughly **60** `.skip` sidecars under `codex/test/apps/` -- plus five
annotation tests (`annotation-author`, `-driver`, `-migrate`, `-reader`,
`-transport`) -- carry the reason **"stub: test body not yet written"**.

This is not a defect list. It is coverage that was never written, and it
was undocumented here until 2026-07-13. It is the single largest gap in
what this document is supposed to measure, and it is tracked nowhere
but here.

### Legitimate Skips (cannot run headlessly)

| Test | Reason |
|------|--------|
| vmx-launch-test, vmx-serial-test | VMX requires CPL 0 + VT-x; no nested VMX under WHPX. They reach `vmwrite` / `vmlaunch`, which genuinely need VMX operation |
| vga-terminal-demo | Requires display + keyboard (`run-vga-demo.ps1`). This row also claimed 13 CDX2051 errors of its own; **compiled 2026-07-19 it has none**: zero errors of any code, 41 CDX4010 infos and 5 CDX3005 warnings. The skip is legitimate; the error count was stale |
| vga-shell-test | Skipped as "blocks waiting for keyboard input" -- **but it carries a `.stdin`, so somebody believed it reads serial.** Both cannot be true. Read `VgaShell`'s input path and drive it with whichever sidecar matches; it is one of the two candidates left in this table |
| firstboot-lite | Needs RDRAND, which codex-vm does not supply (the cell reads zero; the stub only fills it on real hardware). **Its skip was orphaned a directory up and applied to nothing** -- the test compiles on every `-Apps` run and has no `.expected`, so it is PASS_UNVERIFIED, not skipped. The orphan is deleted; the compile coverage is deliberately kept |
| first-boot-ceremony | Output depends on RDRAND (different pubkey each boot) |
| firstboot-identity-test | Requires RDRAND for keypair generation |
| identity-persist-test | Requires DiskFacts persistence across reboots |
| editor-notify-test | Requires interactive console editor session |
| fat16-read-test | Requires disk I/O with FAT16 image |
| gpu-bridge-test | Requires GPU hardware |
| gfx-desktop-demo | Requires GOP framebuffer display |
| run-process-full-test | Invokes dotnet which is not available on bare metal |
| watchdog-panic-probe | Deliberate infinite loop; not part of standard sweep |

### Known Defects

| Test | Issue |
|------|-------|
| ~~db-mini-test~~ `Page.codex` | CDX2000: `emit-field-access` cannot resolve the type of a chained field access (`pg.header.slot-count`) -- 21 errors in that chapter alone. **There is no `db-mini-test`**: no such `.codex` has ever existed in main, and this row described a test by the name of the orphaned `.skip` that was the defect's only record. The defect is real and is recorded only here; the sidecar is gone. Do not go looking for the test -- write one. |

`linalg-test` and `probability-test` were rows here and both are gone. `linalg-test`
was re-measured 2026-07-27 by compiling and running it: it matches its `.expected`,
`mat-mul` does not fault, and the claim was already contradicted eight sections
above in this same file. `probability-test` was a real defect and is fixed.

### Slow Tests (`.slow`, run with `-Slow`)

| Test | Reason |
|------|--------|
| image-codec-test | Large foreword dependency chain (~127s compile) |
| klondike-test | Large foreword dependency chain (~127s compile) |
| let-effectful-bug | Large foreword dependency chain (~127s compile) |
| starvation-prevent | timing-dependent scheduler test; busy-loop may not yield under WHPX |
| spark-boolean-test | also carries a `.skip`, which wins. Retained as the `.skip`; its `.slow` was deleted 2026-07-27 as dead text with a contradictory reason |

**This table listed three and there were eight** (measured 2026-07-27). Four of
the five it omitted are now deleted:

| deleted | its reason said | measured on x86 |
|---|---|---|
| `pi4-drivers` | ARM64: MMIO-heavy, exceeds Renode budget | **0.5 s**, MATCH |
| `tls-test` | redundant: arithmetic+records, covered by factorial | **0.7 s**, MATCH |
| `ui-orchestrator-test` | redundant: arithmetic+records, covered by factorial | **2.1 s**, MATCH |
| `spark-boolean-test` | CDX1023 in batch REPL, needs investigation | contradicted its own `.skip` |

Three of the four are the same mistake: **a cross-architecture reason written
into an x86 sidecar.** `.slow` suppresses a test from the default battery on
*this* machine; Renode's simulation budget and the IR compile budget are
properties of the cross-arch lane, which has its own harness and its own skip
list. `pi4-drivers` is the sharpest, because its `.skip` was deleted on
2026-07-27 as stale and the `.slow` underneath it went on suppressing the test
anyway -- **un-skipping a test does not un-suppress it if a second sidecar is
still there.** The other two carry fester's CL 2949 "redundant" wording, the
text reek reverted for 41 other tests in CL 2984; these two survived that sweep.

The three `~127s` rows and `starvation-prevent` are **deliberately not
measured**. There is no reason to think those claims are false, and running a
slow test on spec is the cost this sidecar exists to avoid.

### `.failing` is healthy, and that is the finding

Audited 2026-07-27 with the rest. **173 sidecars naming 66 distinct codes, and
every one of those codes is a real constant in `CdxCodes.codex`.** Unlike a
`.skip`, this class is not an unchecked claim: `codex/test/errors/` is in the
default battery, so every sidecar is re-evaluated on every run, and the harness
requires both that the compile fail and that each listed code appear as an
`error` in the log. A code that the compiler stopped raising turns the test
`FAIL_WRONG_DIAGNOSTIC` rather than passing quietly.

The one real gap is **position**. `build/test.ps1` accepts `CDX2031@33:5`, which
pins line and column as well as the code, and its own comment explains why that
matters: a diagnostic reported at a synthetic `0,0` span prints no `line:column`
prefix at all, so a bare code check passes against a compiler that lost the
position entirely. That is what the act-block span defect looked like.
**Two of 175 lines use the pinned form.** The fix is not a sweep -- most of these
tests exist because of a *code*, not a position -- but any test whose subject is
a span should be written in the form that can fail on one.

### `.diag` is healthy too, and its one gap is severity

26 sidecars naming 10 distinct codes, every one a real constant, and the class
lives in the default battery like `.failing`, so it is re-evaluated every run.

The gap is that **a `.diag` cannot see a severity change.** `build/test.ps1`
matches `(error|warning|info|hint|deprecated) CDX<code>`, so a code demoted from
warning to info still satisfies every sidecar naming it. Nothing else covers it
either: `CdxCodes.codex` records a `sev-*` per row and
`build/check-cdx-registry.ps1` parses that column without comparing it to
anything.

**Measured rather than left as a worry.** Of the codes raised through a
severity-bearing helper the grep can see (`st-add-info` / `-warning` / `-error`,
`bag-add-error`) -- 15 of the 102 registered -- **all 15 agree with their
registry row**. The remaining 87 are raised through shapes that pattern does not
reach. No instrument was built for them: there is no evidence of drift, and a
permanent checker for a gap with a zero hit rate is the maintenance cost that
outweighs the discovery.

### In `prose-anchor`, `prose-consistency` and `prose-smoke` the prose IS the fixture

These three are tests OF the prose system, so their column-2 prose is the
subject under test, not commentary about the code beside it. CLAUDE.md rule 12
says to delete prose in files you are already changing; **these three files are
the exception, and the failure mode is asymmetric.**

Verified against the source 2026-08-07 (blu's finding, routed to the fleet):
`prose-anchor.diag` is `1101`, and the block that raises it is the last one,
`To absent-def (n) gives an Integer.` at `prose-anchor.codex:34`. `absent-def`
has no definition in the chapter, deliberately. Cut that block and the test
goes RED, which is the safe direction because you find out.

**Cut any of its siblings instead and the test stays GREEN while pinning much
less.** The blocks at lines 4-15 and the `first-def` / `second-def` /
`third-def` templates are what pin the two defects the chapter exists for: that
the consistency check looks a named function up among ALL definitions rather
than comparing every block against index zero, and that a prose token is built
before the lexer advances past its line rather than after. `.diag` is still
satisfied by line 34 alone, so nothing reports the loss.

That is the same shape as the severity gap above -- a sidecar that keeps
passing after the thing it was protecting has gone.

### A single-line `.expected` outside the skip list is NOT the same defect

Worth recording as a negative so nobody re-runs it. The skip census found seven
tests whose whole assertion was a name-and-`ok` line and could not fail. Asking
the same question of the **live, unskipped** battery finds eight, and **none of
them is that defect**:

- `induction-assoc`, `induction-list`, `induction-param`, `induction-parse`
  each carry a **`.diag` on CDX4020**, and that is the assertion: the proof was
  recognised and erased. The runtime line is incidental.
- `normalize-eq`, `prop-arith`, `value-eq`, `occurrence-check` are tests where
  **the compile is the assertion.** A false `claim`/`proof` is CDX2001, so the
  test fails by not compiling.

And the compile-is-the-assertion form only means something if a wrong proof is
actually rejected, which is the question worth asking of it. **It is, and the
negatives are paired by name**: `normalize-eq` against `normalize-false`,
`prop-arith` against `prop-arith-false` and `prop-neg-false`, `value-eq` against
`refl-mismatch` and `term-mismatch`, the induction four against
`induction-unsound`, `induction-list-false`, `list-induction-false` and
`reverse-reverse-unsound`, plus `proof-qed-vacuous` and six `proof-launder-*`
pinning CDX4023. All are in `codex/test/errors/` and all run every battery.

### `.fatal` proved only that the test compiled, and twice not even that

Seven `.fatal` tests, and **none has an `.expected`**, so `-Fatal` has never
checked what any of them printed. Five are deliberate crash demos where that is
the honest state.

The other two are not crash demos, they are language-feature tests, and both had
**stopped compiling**: `bounded-param-trap` and `bounded-return-trap` are
rejected by `CDX2051` before any code is emitted, so the runtime guard they exist
to prove was never reached. Nothing caught it because nothing runs `-Fatal`.
Both now pass their value through `__narrow`, which is what CDX2051's own message
instructs, and both were re-measured: each compiles, each dies on `!EXC=06`
(UD2) at the guard, and neither prints the out-of-range value. See
`docs/DevelopersGuide.md`, which asserted the opposite mechanism and is
corrected.

**They still assert nothing at runtime, and that is a real limitation rather
than an oversight.** A trapping test's captured output is the `!EXC` dump, whose
`RIP` moves with every codegen change, and `test-run.ps1` strips only
`HEAP:`/`WD:`/`STACK:` lines. Pinning that dump would go red on correct work,
which is the rubber-stamp failure the LIR snapshot section describes. A `.fatal`
test is therefore a compile check plus a manual read, and should be understood
that way.

## Disk Tests

Tests that exercise block I/O use `.disk` sidecar files. The test
harness passes these to codex-vm via the `-disk` flag (IDE PIO
attachment). Two patterns:

- **Write tests** (disk-facts-init, disk-facts-multi, boot-init):
  use a blank 1 MB disk image. The test writes to it.
- **Read tests** (disk-facts-read, disk-facts-load, disk-facts-multi-load):
  use a pre-populated disk image created by running the corresponding
  write test. The `.disk` file in the depot is the frozen output.

`block-io-basic` uses a 512-byte image with value 42 in the first qword.

### The debugger's expression evaluator, and the CCE trap it caught in itself

`codex/test/apps/expr-eval` is the pure half: 25 cases, roughly half of which
require a specific error, because an evaluator that answers a number for input
it did not understand is the whole defect class.

**It caught a real bug on its first run, and the bug is the one this project
keeps shipping.** `0xff` came back as **391**. The letter-to-value step read
`10 + c - 'a'`, which is meaningless in CCE: letters are ordered by English
frequency, so `a` is 15, `e` is 13 and `f` is 28. Digits ARE contiguous (3
through 12), which is exactly why the digit arm may do arithmetic and the
letter arm may not. It is now a six-way comparison, which is the only form that
cannot be wrong about an ordering it does not assume.

The ablation reproduces it precisely: mapping `f` to 23, the value the old
arithmetic produced, brings `0xff => 391` straight back.

**One case in that test does NOT discriminate under that ablation, and it is
worth knowing why.** `0xF0 & 0x3C` answered 64 under the original bug (both `f`
and `c` were wrong) and answers 48 under the one-letter ablation (only `f`
wrong) -- which is the correct answer, by coincidence. A test that carried only
the masked case would have looked fine. It carries `0xff` too.

Two precedence cases pin the two-level rule from opposite sides: `1 + 2 * 3` is
7, and `1 | 2 + 3` is 6 rather than the 8 that C's precedence would give.

`codex/test/apps/debug-expr` is the wiring, and it goes through
`handle-console-input` rather than calling `expr-eval`. That is the only way it
can fail: the arm it exercises used to be `else state`, which swallowed any
line that was not a menu key, and a test calling the evaluator directly would
have passed against it. Five readings, and the last two are the ones that
matter -- a bad expression reports its reason, and `$` afterwards shows
`last-address` **unchanged**, so the refusal is a refusal and not a silent move
of the cursor.

### The watch table, and why the third reading is the whole test

`codex/test/apps/dev-watch` is the pure half of `Dev chapter DevState`, over
two buffers it allocates and controls. `peek-byte` carries no effect row, so
the watch logic is ordinary functions and needs no machine.

**A watch is change DETECTION, so only the transitions mean anything**, and the
one that matters is the third: unchanged, changed, unchanged **again**. The
third reading is what proves a check RE-SNAPSHOTS what it reports. Without the
re-arm a single change makes a watch report as changed for the rest of the
session, and the first two readings are identical either way. **Ablation
fired**: with the re-arm removed, `again-no-poke` reports `changed=1` forever.

**The second watch is the discriminator and the poke touches only the first.**
With one watch, a checker that reports everything as changed and a checker that
works produce the same line; the expected output requires `alpha CHANGED` and
`beta same` in the same breath.

**Both buffers are zeroed before use, and that is not ceremony.**
`alloc-bytes` does not zero what it returns -- it hands back the heap pointer
and bumps it -- so a fresh buffer holds whatever the last allocation left. The
checksums would still be self-consistent, but a case that poked a byte already
holding that value would report unchanged for a reason nobody could see.

Three refusals are asserted rather than assumed: over the length cap, a
non-positive length, and the thirty-third watch against a table of 32. Each
names the cap it hit, because silently dropping a watch leaves an operator
watching something they believe they are not.

**Two readings exist only to catch a transition mutating its argument, and
they were added because the other thirteen could not.** `DevState`'s first cut
built every new state with `__record-set st "ds-watches" ...`, which stores
into the field and returns THE SAME record -- so a caller holding `st` found
the new watch in it, while the chapter's own prose claimed the whole thing was
pure. Two CDX6020 warnings said so at the constructors.

The counts hid it precisely. `fill-watches s0` over a mutated `s0` starts at
two and adds thirty; over a clean one it starts at zero and adds thirty-two.
**Both answer `filled: 32`**, because that line measures the cap and not the
arithmetic. So the table-full assertion held under both readings and nothing
else in the test looked. `origin-untouched` and `full-after-clear` are the two
that separate them: 0 against 2, and 32 against 0.

**Ablation fired**: with `__record-set` put back at all three sites, exactly
those two lines move and the other thirteen are byte-identical. That is the
useful shape of the result -- it is the measurement proving the old test could
not have caught this, not merely a claim that it did not.

**One fragility worth knowing before this test is moved or reordered.** Its
`.expected` carries the literal addresses `6291456` and `6291520` -- the heap
base and 64 bytes past it -- because the two buffers are the first allocations
the program makes. Anything that allocates ahead of them moves both numbers.

`codex/test/apps/debug-expr` covers the console half in the same run: five
watch steps driven through `handle-console-input`, which is what proves the
console threads the returned `DevState` back into its own state rather than
dropping it on the floor.

### `vmx-init-test` was a test that could not fail, and it is in the battery now

`vmx-enable` returned `VmxOk` unconditionally -- it discarded the result of
`vmxon` -- and `VmxFailed` was declared in `DevHypervisor` and **constructed
nowhere in the tree**. The test asserted `vmx-enable:ok` against that, which is
the "function that always answers the same thing" shape with a `.expected`
recording the constant. Its skip said it needed real hardware; on real hardware
without VT-x it would have printed `ok` just the same.

Three functions had the same defect, and each was found by the previous one's
fix removing the crash that hid it:

| function | what it did |
|---|---|
| `vmx-read-revision-id` | read IA32_VMX_BASIC, a **#GP** off a VMX machine -- the test faulted before any check ran |
| `vmx-adjust-control` | read the VMX capability MSRs, same fault |
| `vmx-enable` / `vmx-create-vmcs` | ran the instructions and answered `VmxOk` regardless |

The gate is IA32_FEATURE_CONTROL: VMXON raises #GP unless the lock bit and the
VMX-outside-SMX bit are both set. **Measured under codex-vm, the MSR reads 1**
-- lock set, VMX bit clear -- which is exactly a machine whose firmware has
VT-x switched off. That is not what "an MSR outside codex-vm's list reads zero"
predicts, and measuring it rather than assuming is why the reason string is
right.

The test now prints what this machine is (`vmx-available:False`,
`vmx-enable:fail`) plus a reason naming the MSR and its value, and it runs in
`-Apps` instead of hiding behind a skip. A run on a workstation with VT-x
enabled prints `ok`; that is the gate working on a different machine, and the
test says so in its own prose. What would be wrong is `ok` here.

`vmx-launch-test` and `vmx-serial-test` still fault and stay skipped: they
reach `vmwrite` and `vmlaunch`, which need VMX operation and not merely the
capability MSRs. Their skip reasons were accurate and remain so.

### Syntax highlighting: the pure half is tested, the drawn half is a golden

Two tests, because the feature is two things and they fail differently.

`codex/test/apps/syntax-highlight` is the classifier and prints one tag
character per source character, so the `.expected` names the class of every
character and a token that grows, shrinks or changes kind moves the line. Nine
shapes, chosen for what each can get wrong: a header, a prose line whose first
word is `cites`, the same word one space further in as a real directive, a
hyphenated name held together as one token, `a-2` as one identifier, `x -> y`
where the hyphen must NOT swallow the arrow, a hex literal, a string, and a
`between ... and` bound.

**The ablation was fired**: with the one-space indent rule removed, the prose
line's `cites` reclassifies from prose to directive and `is` to keyword, so an
English sentence gets highlighted as code. That is the same confusion the
compiler raises CDX3010 for, and it is what the test would catch.

`codex/test/gui/gop-source-preview` is the drawn half: a golden frame from
`codex/test/apps/gop-source-preview`, which paints through the shipping path
(the classifier, the run walk, `syn-colour`, `gop-draw-text`) onto a real GOP
framebuffer. Measured on the recorded frame, all nine classes reach the glass
at their exact XRGB values -- header 242 px, prose 403, directive 89, keyword
55, type 230, name 319, number 44, string 80, operator 27. **A hex dump draws
in one colour; this draws in nine**, which is the reading that separates
"highlighted" from "rendered".

**Its negative control was fired and the count was predicted**: flattening the
directive colour to the plain one changes exactly **89** pixels, which is the
directive pixel count above. The count the instrument reports is the count the
diagnosis predicts.

**The `.uiscript` states its own mode**, `vmargs -gop-width 640 -gop-height
480`, and that is not decoration. Recorded at the harness default of 1024x768
the frame smears: the probe writes the framebuffer directly at a stride of 640,
so every row lands 384 pixels off. The first recording was that smear, and the
harness's own instruction -- look at what it recorded before you trust it --
is what caught it.

`codex/test/apps/gop-source-preview` carries a `.skip` because it paints once
and spins by design, so the serial battery would hang on it. The skip says
where its assertion actually lives rather than claiming an environment limit,
which is the failure the skip census found seven of.

### PARKED during active GUI development (Damian, 2026-08-03)

**Do not add new pixel goldens for GopDesk panes, and do not treat re-minting
`desk-boot` as work that has to happen.** The ruling: *"during active gui
development we should wait on the goldens, they will change for silly reasons
and it becomes ceremony not certainty."*

The case that produced it, the same day: adding one sentence to the Welcome
window's hint line moved 3752 pixels and made `desk-boot` red. The frame was
correct, the re-mint was correct, and the whole exchange established nothing
except that the text had changed -- which the diff already said. A golden over
a surface that is being actively designed reports the design changing, and a
red that is always the expected answer trains its reader to accept it, which is
worse than no test because it looks like one.

Nothing runs `codex/test/gui/` automatically, so parking costs no gate. What it
costs is honest to state: **GopDesk has no automated coverage while this holds**,
and panes are verified by capture (`build/desk.ps1 -Shot -Keys`). The recorded
frames are kept as the record rather than deleted.

Resume when the desktop's chrome stops moving -- a golden is worth minting
against a surface that is supposed to hold still, and that is exactly when it
starts catching things. `gop-source-preview` is NOT parked: it pins a probe with
a fixed geometry that nobody is redesigning.

### `desk-boot`: the desktop's chrome, on a bare `-gop` guest

`codex/test/gui/desk-boot` pins what `build/desk.ps1` puts on the glass:
the six sidebar buttons, the Welcome window, the taskbar and the clock,
from `apps/works/DeskVm.codex` with no boot image and no disk behind it.

```powershell
build/desk.ps1 -Force                                    # build build-output/desk.cdx
build/test-gui.ps1 -Kernel build-output/desk.cdx -Script codex/test/gui/desk-boot.uiscript
```

**The clock is frozen by the script's own `vmargs -rtc`, and that is the
whole reason this test can exist.** The taskbar paints the CMOS RTC, which
is host state the test cannot twist, so without the freeze the frame
differs by the digits on every single run.

**It was fired, and the count is small on purpose.** Moving the frozen
stamp from `06:00:00` to `07:11:22` changes exactly **125** pixels and the
rest of the frame holds, which says the comparison is live and that what
moved is the clock rather than the chrome. Two consecutive runs before and
after report `0 pixels differ`.

**The font row is under test as much as the chrome is.** No disk is
attached, so `desk-font` cannot mount an ESP and falls back to the CBF
bitmap face; the Welcome window prints `Font: CBF bitmap (no TTF on the
ESP)`. A frame that ever came back with the TrueType face would mean the
harness had grown a medium, and this recording would no longer be saying
what it says.

**It is NOT in `build/build.ps1`.** Nothing sweeps `codex/test/gui/`; both
goldens there are run by `build/test-gui.ps1` on request. Adding a 6.5
second guest boot to the standing gate is a gate-cost decision rather than
a test-authoring one.

### The fact store's partition, and the three tests that bound it

The store addresses its sectors relative to a partition of type
`C0DE1A11-FAC7-4C0D-9E75-C0DEC0DE5EED` rather than from LBA 0. Three tests
hold the behaviour between them, and the reason there are three is that no
two of them can agree by accident:

| test | disk | requires |
|---|---|---|
| `disk-facts-init` | raw, blank | count reaches 1 |
| `disk-facts-gpt-guard` | GPT, no fact partition | count stays 0, GPT header and entry array intact |
| `disk-facts-mbr-guard` | MBR boot signature only | count stays 0, sector 0 signature and sector 1 intact |
| `facts-partition` | GPT with a fact partition | base is the partition's LBA, limit is its sector count, count reaches 1, the fact survives a fresh `disk-load`, and the GPT header is intact **after a successful write** |

The last row is the one that separates "safe" from "useful". A guard slides
toward refusing everything, and against `disk-facts-gpt-guard` alone a store
that refused every disk would look perfect.

**`facts-partition`'s fixture puts the partition at LBA 128, not at 0**, so a
store that still addressed absolutely answers 0 -- which is also what a raw
disk legitimately answers. A fixture with the store at the bottom could not
tell those apart. The ESP at 34..127 is there for the same reason: with one
partition on the disk, a lookup by type GUID and a lookup that takes whatever
is first give the same answer.

**Both controls were fired rather than assumed** (2026-07-27, seed
`EFC7FCD0`). Retype the fixture's fact partition as generic basic-data and
the store answers `base -1 / count 0 / read MISSING` -- so the lookup is by
GUID and not by position. Move the partition from 128 to 148 and it answers
`base 148 / limit 75 / count 1` -- so the addressing follows the table rather
than being hardcoded. For the MBR guard, clearing the two signature bytes
from the same fixture makes the store write (`count 1`, and `sig GONE`
because the superblock zeroes the sector it lands on), so those two bytes are
the whole difference between refuse and write.

The fixtures are minted by `build/mint-facts-fixtures.ps1`, authored from the
GPT specification rather than by running the writer under test. The type
GUID's agreement across its three writers is a textual fact and gets a
textual runner instead: `build/check-facts-guid.ps1`, wired into
`build/build.ps1`, which fails the build when
`codex/foreword/core/Gpt.codex`, `codex/plugs/img/GptWriter.codex` and
`build/build-img.ps1` disagree. Fired by perturbing one byte: it reports the
disagreement and exits 1.

**A disk test must attach a disk and demand a specific number.**
`block-identify` needs no disk and asserts 0 sectors when none is
attached -- which is true whether the sector count works or not. It
therefore could not, and still cannot, catch a stuck counter, and
`block-sector-count` reported 0 on every disk for months behind exactly
that gap: the disks read perfectly and reported themselves empty. The
test that found it (`codex/test/block-sector-count`) attaches a 64 KB
fixture and demands **128**. A function that always answers the same
thing looks exactly like one that works.

## A Kernel On The Actual Device (`codex/test/gpu-ptx`)

Every other operation the compute bridge serves is computed by codex-vm on
the host CPU in scalar C, which is a transport exercise and not a graphics
processor. `gpu-op-launch-ptx` is the one command that reaches the card:
the guest hands over a PTX module and an entry point, `com3_launch_ptx`
JITs it through the CUDA driver API (`nvcuda.dll`, loaded at first use --
no toolkit, nothing linked against) and launches it.

**This test needs an NVIDIA GPU, and on a box without one it FAILS rather
than skipping.** That is deliberate and it is the same argument the skip
inventory makes elsewhere: a skip reads as coverage, and the claim here is
precisely about hardware. A failure naming the absent device is the honest
answer on a machine that does not have one. The dev box has an RTX 4060 Ti,
so the claim is checkable in-loop rather than against a simulator that
cannot reproduce the failure.

**Two of the three lines are controls and they are why the test means
anything.** A launch that only ever succeeds cannot tell a driver that
compiled and ran a module from a host answering something canned. So the
second line asks the same good module for an entry point that is not in it
(the driver must refuse: `named symbol not found`), and the third hands
over text that is not a module at all (the driver must refuse: `device
kernel image is invalid`). The second proves the host resolves a function;
the third proves an assembler actually read the bytes, which in turn proves
the text crossed the CCE boundary as ASCII rather than arriving as
plausible-looking code points.

**The positive line is chosen so nothing else could have produced it.** The
kernel computes ten times the input plus five over 1, 2, 3, 4, giving 15,
25, 35, 45 -- four distinct values, none an input, none a permutation of the
inputs, and an operation that appears nowhere in `com3_execute`'s scalar
table. **Sabotage run:** changing the multiplier in the PTX from 10 to 20
gives `launch: wrong 24992 44992 64960 84992` -- exactly twenty-times-plus-
five -- while both controls stay `refused`. So the answer is computed from
the module's own bytes on the device, and the failure is discriminating
rather than global.

### The two-implementation control for matmul

`codex/test/gpu-doorbell` lost its control when the serial transport was
deleted: running every case through both transports on identical operands
was what would catch an implementation that computed WRONGLY rather than
not at all, and one transport has nothing to compare against.

Matmul has that control back, and a better one, because the two paths share
no code: the scalar C loop in `com3_execute`, and a PTX kernel on the
graphics processor. `CODEX_VM_GPU_MATMUL=1` forces the device and `=0` forces
the scalar loop; UNSET auto-selects by size (below). The test's matmuls are
small, so unset takes the scalar loop there and the env var is what runs the
same case both ways. The test does not know which ran, and must not: **the
assertion is that the answers are identical.**

```powershell
$env:CODEX_VM_GPU_MATMUL = "0"   # or leave it unset
tools\codex-vm.exe -kernel build-output\gpu-doorbell.cdx -headless -output scalar.out -mem 3072
$env:CODEX_VM_GPU_MATMUL = "1"
tools\codex-vm.exe -kernel build-output\gpu-doorbell.cdx -headless -output device.out -mem 3072
Remove-Item Env:\CODEX_VM_GPU_MATMUL
```

Run 2026-07-22: both legs byte-identical to each other and to
`gpu-doorbell.expected`, with `CUDA: NVIDIA GeForce RTX 4060 Ti` present on
the device leg and **absent on the scalar leg**, which is what proves the
toggle actually moved and that the two runs were not the same run twice.
Use `Remove-Item Env:\NAME` to clear it: `$env:NAME = ""` leaves the
variable DEFINED, `getenv` returns a non-NULL pointer to an empty string,
and that is exactly how both legs of a measurement come to read as set.

**The device is auto-selected when the matmul is large enough to pay, and
the threshold is measured** (`CODEX_VM_COM3_STAT=1`, single dispatch per boot
so the device time includes its one-time ~85 ms context creation and PTX
JIT):

| N | scalar | device (incl. JIT) | |
|---|---:|---:|---|
| 256 | 23.0 ms | 95.1 ms | scalar wins |
| 384 | 80.0 ms | 72.9 ms | device wins |
| 512 | 198.1 ms | 88.5 ms | device wins 2.2x |

The scalar loop is O(N^3); the device cost is dominated by the fixed JIT. So
the device only wins once the scalar loop would cost more than ~85 ms, which
is about a 400x400x400. `cuda_matmul_wanted` picks the device above
`COM3_DEVICE_MATMUL_FLOPS` (64,000,000, ~400^3) and the scalar loop below,
so a small matmul -- every one the tests do, and every one a short program
does -- is never slowed, and a large one is accelerated automatically.
`COM3_MAX_ELEMS` (524288) caps a square at 512x512, past the break-even; a
caller needing bigger ships its own kernel through the PTX launch (op 32).
The command buffer was enlarged to 6 MB to hold the 2 MB operands of a
512x512, and the whole COM3 slab (`0xBD000000`..`0xBE000000`) is now
committed at boot -- the host writes the reply there directly, so it cannot
rely on the guest having demand-committed the page first.

## The Console Re-Mode (`build/boot/test-conout-remode.ps1`)

The boot stub asks the firmware for its display geometry AFTER it clears the
screen, and that ORDER is the whole subject of this harness. On AMI Aptio V the
first real ConOut use activates the GraphicsConsole, which sets its own graphics
mode; until 2026-08-02 `build/cdx-to-pe.ps1` read GOP `Mode->Info` and cleared
~200 bytes later, so every image it built published the splash mode's
1920x1080/2048 for a scanout the firmware had switched to 1024 px/row. Every row
the payload wrote spanned two scanlines. That is the ASUS display corruption:
glyphs stretched, alternate lines black, long-line tails overpainting the row
below.

It was cured by swapping the two calls, and **the cure then had no runner for a
day**. `codex-vm -uefi-conout-remode` models the activation and nothing in the
tree ran it, so re-ordering those two blocks -- or writing a payload that reads
the geometry before its own first ConOut call -- restored the corruption with
every gate green. The same shape as `check-cdx-registry`'s false green: an
assertion with nothing evaluating it.

```powershell
pwsh build/boot/test-conout-remode.ps1
```

`build/boot/diag/GeoTruth.codex` prints the three published numbers and paints
nothing, because the subject is two calls in a PowerShell script and a probe that
also drew could fail for unrelated reasons. One image is built and booted twice,
re-mode off then on, at 1920x1080 stride 2048:

| arm | reads | what it establishes |
|---|---|---|
| no re-mode | `1920x1080/2048` | the control. The bed really is presenting the padded panel, so the other arm is answering the question asked |
| stderr `ClearScreen re-moded GOP to 1024x768` | -- | the flag fired. A silent flag and a working fix are otherwise the same green |
| re-mode | `1024x768/1024` | the subject. Reading `1920x1080/2048` here IS the 2026-08-02 corruption |
| the pair | strides differ | a payload printing a constant satisfies exactly one arm; this says which shape a failure had |

**Fired 2026-08-03, and this is why the numbers are worth believing.** Moving the
ClearScreen block back below `GopAcquire` in `cdx-to-pe.ps1` takes the re-mode arm
to `1920x1080/2048`: the subject row and the pair row go red and **both controls
stay green**, which is what attributes the failure to the stub's ordering rather
than to the bed or the payload. `cdx-to-pe.ps1` was restored byte-identical
(SHA-256 re-checked) afterwards.

**It is NOT in `build/build.ps1`.** It compiles a payload and boots two VMs.

**`test-ovmf.ps1` cannot replace it and no QEMU geometry can.** OVMF does not
re-mode on ClearScreen, so the mechanism is absent there whatever resolution it
is given; this is the L-OPTIONAL shape inverted, a bed that is *less* eventful
than the target rather than more capable.

## The Serving Peer (`build/cdx-serve-test.ps1`)

**Until this, no test drove a Codex TCP server.** IdeaServer, WebServer and
ExplorerServer are each driven by a demo script and by nothing else, which is how
`codex/os/net/WebServer.codex` came to sit in the tree with six undeclared-effect-row
errors (CDX2031/CDX2033) that no battery could see: it did not compile, and nothing
ever asked it to. A server only ever run by hand is a server nobody is checking.

**Those errors are gone, measured 2026-07-20 (val), and this paragraph said
otherwise in the present tense until then.** `WebServer.codex` cited by a real
program compiles with **zero** errors and emits a binary, and so do
`IdeaServer`, `ExplorerServer` and `Prism`. The history above is kept because it
is why this harness exists; the claim that the chapter is broken is not true now.

**Do not re-measure this by compiling the chapter on its own, which is what makes
the stale claim so easy to believe.** A library chapter has no `opening`, so under
whole-program dead-code elimination nothing in it is reachable, lambda-lifted
locals are pruned, and emit reports a `CDX2040: Unresolved call` for each one.
`WebServer` standalone shows **15** such errors naming ordinary let-bound locals
(`sess`, `new-arp`, `hash`), none of which is a defect. The control that settles
it costs one compile: a chapter nobody claims is broken, compiled the same way,
shows exactly **one** CDX2040 for the absent `opening` and no others, so anything
above one is the artifact and not the chapter. Compile a citing program instead.

This harness boots the real ingest tool and the real server and asks the real
question:

```powershell
pwsh build/cdx-serve-test.ps1                          # against seed/Codex.cdx
pwsh build/cdx-serve-test.ps1 -Kernel build/output/Sut.cdx   # against a fresh SUT
pwsh build/cdx-serve-test.ps1 -KeepArtifacts           # keep the decoded payload
```

`build/store-source.ps1` drives `tools/cdx-store` to store one work on a blank disk
and **prints the hash it stored**; `tools/cdx-serve` boots on that same disk and
listens on guest port 9300; the script connects through `-portfwd` and asks for that
hash over the wire. **The hash is never hardcoded** -- it is read back out of
cdx-store's own output, so a change to the addressing rule moves both ends together
and the test keeps asking the right question instead of asserting a stale constant.

What it pins: a reply carries `tag-work-reply` (18); it answers the hash that was
asked; the payload is the stored work, carrying both its source and its path; and a
hash nobody stored comes back as an **empty payload, not an error**.

**Sensitivity, run rather than assumed:** the same server against a blank disk answers
109 frame bytes (an empty payload) where a seeded one answers 457. The test reads the
served work; it does not print a constant.

**THE WIRE IS CCE, AND IT DOES NOT TELL YOU.** `frame-encode-text` calls `char-code`,
which gives the CCE code point and not the ASCII one, so the hash's own hex digits go
out as CCE bytes. Sending ASCII does not error -- the server simply never finds the
work and answers "not here", which reads exactly like a working server with an empty
store. **Encode through `ConvertTo-CceBytes` (`build/vm-config.ps1`), and
decode through `ConvertFrom-CceBytes` -- never by indexing
`$script:UnicodeToCce` a byte at a time.** CCE is three tiers and
multi-byte (1 byte below 128, 2 below 2176, 3 below 67712, 4 above, with
the tier base added back on decode), so a byte-at-a-time table lookup is
correct only for tier 0 and silently corrupts everything above it rather
than refusing. The 256-slot reverse map exists for the tier-0 case and
skips the entries above 255 deliberately; a `% 256` fold over it would
land Cyrillic 1072 on 48 and silently remap the digit `0`. Thirteen
`run.ps1` scripts still encode one byte at a time, and that is safe only
because every one of them encodes the fixed literal `"IR-CCE"`.

**And the trap that cost the most: `-shl` on a `[byte]` in PowerShell keeps the left
operand's width.** `$b[1] -shl 8` shifts the bit off the end and yields 0, so every
length silently comes back as `n -band 0xFF`. That read a real 457-byte frame as 201
and a real 348-character payload as 92 -- which looks *exactly* like a server
truncating its reply, and sent this test hunting a bug in `cdx-serve` that was never
there. Cast to `[int]` before shifting. The server was right; the ruler was short.

## The Scalar Operators Against the Host (`build/oracle-scalar.ps1`)

**Real `<` `>` `<=` `>=` were emitted as a signed INTEGER compare of two
IEEE-754 bit patterns from the initial import of 2026-04-17 until 2026-07-27.**
That ordering is correct for two non-negatives and for mixed signs, because a
double's sign bit lands where the integer sign bit lands, and it REVERSES for
two negatives. Every one of the four operators answered backwards on a quarter
of the operand plane, for three months, on the original code path.

Nothing in the tree could see it, and the reasons are worth stating because
none of them is carelessness:

- **The suite was organised by feature; the hole was on an orthogonal axis.**
  Thirteen test files declared a `Real` -- negation twice, approximate,
  bitcast, saturating, trapping, cert, path. Exactly one contained any
  ordering comparison, and it was a vector test. Nobody decided to skip `<`
  on Reals; the category read as covered and *the sign of both operands* was
  never a dimension anyone enumerated.
- **The primary instrument is blind by construction.** The compiler declares
  **zero** `Real` values across its 63 files. A fixed point proves the
  compiler reproduces itself and is silent about a path it never executes.
  This is measured, not argued: the fix converged in ONE pass, meaning it
  changed no byte of the compiler's self-image.
- **The benches are blind too.** Twelve of thirteen are integer-only.

So this harness enumerates a **lattice** rather than a case list: every
operator against every ordered pair from an operand set that crosses zero.
A hole along an axis nobody thought of is what a cross product finds and a
hand-written list does not.

```powershell
pwsh build/oracle-scalar.ps1                              # against seed/Codex.cdx
pwsh build/oracle-scalar.ps1 -Kernel build/output/Sut.cdx
pwsh build/oracle-scalar.ps1 -Keep                        # keep the generated source
```

**The oracle is the HOST, never another Codex answer.** That is the whole
design and it is `BrotliBeatsOpus`'s lesson pointed at arithmetic instead of a
codec: a decoder checked only against its paired encoder is checked against
nothing, and a compiler checked only against itself is the same shape.

Each case is asked **twice**, because a comparison in condition position is
fused into the branch and one whose result is handed on as a value is
materialised. Those are different emitter paths and each carried the defect
separately; an oracle asking one shape would have declared the other correct.
It also covers truncating division against `int-rem` and `int-mod` over every
sign pair, which is the other documented silent-to-get-wrong case.

**Both directions run.** Against the fixed compiler, 376 of 376 agree. Against
`//Codex/main/seed/Codex.cdx#545`, the last seed before the fix, 328 of 376
agree and the 48 disagreements are exactly the Real comparisons between two
negatives -- four operators times the twelve unequal negative-negative ordered
pairs. The count the instrument reports is the count the diagnosis predicts.

### Widened along the operand-type axis, and it found the defect it was pointed at

The 1037-case lattice covered comparison only. Arithmetic was never
adjudicated at all: `+ - * /` reach separate emitter arms for f64 and f32 and
not one of them had ever been checked against the host. Nor had the
CONVERSIONS, and nor -- this is the one that mattered -- had the **operands**.

Widened to **2013 cases**, and the first run found a first-order defect:
**decimal Real literals were not correctly rounded.** `__text_to_double`
divided by ten once per fractional digit in a loop, so k fractional digits
cost k roundings where IEEE-754 division promises one. `2.9000001` came out
one ulp low. Measured across the tree: **106 of the 580 distinct decimal
literals are affected**, `3.141592654`, `6.283185307` and `1.570796327` among
them. The fix accumulates ten to the power k, which is exact as a double for
k up to 22, and divides once.

**The comparison lattice could not have found this, by construction, and that
is the lesson worth keeping.** A one-ulp shift in an operand leaves every
ordering in the lattice unchanged, so all 1037 comparison cases passed
against wrong operands and would have gone on passing forever. What found it
was adjudicating the operands themselves: the oracle had been checking what
the operators did with the numbers without ever checking the numbers.

An operator can only be adjudicated against an operand both sides agree on.
Any differential harness that builds its inputs in the language under test
has this hole until it measures its own inputs.

Results are compared as **bit patterns** through `real-to-bits`, not as
rendered decimals: a decimal render cannot express a one-ulp difference,
which is the entire quantity under test, and cannot express negative zero,
which is one of the operands. The single deliberate exception is that a host
answer of NaN requires *a* NaN back rather than a specific payload, because
IEEE-754 does not fix the payload of a computed NaN.

Pinned by `codex/test/real-literal-rounding`, whose expected values are the
host's, not the compiler's. Negative control run: against the pre-fix seed it
differs on 9 of its 12 lines, and the 3 control literals -- short enough that
one division was always the whole computation -- match exactly. So the pin
fails at the defect rather than everywhere.

### Widened 2026-07-27, and it found a second defect immediately

The section above used to end with a stated gap: no NaN, no infinity, no
negative zero, because "this compiler has no literal for them." That reason
was wrong. `bits-to-real` has existed since 2026-07-03 and builds any of them
exactly from its bit pattern. Three axes were added, 376 cases became **1037**,
and the first run found a **second first-order defect** that had nothing to do
with floating point.

- **`Real approximate`.** Single precision is a separate emitter arm reaching a
  different instruction, `ucomiss` rather than `ucomisd`. Nothing exercised it,
  so the f32 ordering path was being declared correct by an oracle that never
  asked it a question.
- **The IEEE special values**, built through `bits-to-real`. This closes the
  gap named above rather than restating it: `emit-fp-ord` emits `<` and `<=`
  with reversed operands so all four answer False on NaN, and that is now
  measured. It passed against the pre-fix seed too, which is the useful kind of
  negative result -- the reasoning was right and is no longer only reasoning.
- **Integer literals.** The value a literal denotes is a compiler output like
  any other, and it was the one output nothing adjudicated.

The literal axis paid for itself on its first run. **A hex literal in the band
`i64-min .. i64-min+254` compiled to the wrong constant, silently, with no
diagnostic**: `#8000000000000000` evaluated to `9223372036854775552`, which is
`0x7FFFFFFFFFFFFF00`. The front end was never at fault -- `lit-text-to-integer`
is correct and the IR dump reads `(int-lit i64-min)`. The loss was one leaf
function deep in the encoder, in `floor-div`, which biased the dividend before
dividing (`(n - d + 1) / d`) and underflowed at `i64-min`, so seven of the
eight little-endian bytes of the immediate came out inverted. The same code had
been copied into `codex/plugs/common/ByteHelpers.codex`.

### The seed was carrying the wrong `i64-min`, 54 times

The literal bug was not confined to user code. **Every seed this project has
ever shipped had a wrong constant baked into it**, and the way that surfaced is
worth repeating, because nobody went looking for it.

The fix converged in **two passes**, not one. That is the signal. `floor-div`
changes behaviour only for immediates near `i64-min`, so a two-pass
convergence is a proof by itself: the compiler must be emitting such an
immediate **into its own binary**. Diffing the pass-1 `Sut` against `stage1`
found 54 clusters, every one the same eight bytes and every one followed by an
unchanged `FF FF FF FF FF FF FF 7F`:

| built by | bytes | value |
|---|---|---|
| the old seed | `00 FF FF FF FF FF FF 7F` | 9223372036854775552 |
| the fixed compiler | `00 00 00 00 00 00 00 80` | `i64-min` |

An `i64-min` immediately followed by `i64-max` is
`int-ty-default : CodexType = IntegerTy i64-min i64-max OvError`. So the
compiler's notion of *the default Integer range* was
`9223372036854775552 .. 9223372036854775807` -- a 256-wide window at the top of
the positive range -- rather than the full 64-bit interval. Confirmed directly
on the shipped artifact: `//Codex/main/seed/Codex.cdx#549` contains the wrong
pattern **54 times and the correct one zero times**; the rebuilt seed inverts
that exactly.

Nothing visibly broke, which is the uncomfortable part and the reason this
sat for months. A lower bound above every value it is tested against makes the
`n >= lo & n <= hi` fast paths in the checker simply fail, and the compiler
falls through to the general path and gets the right answer for the wrong
reason. A bound that is absurd rather than merely wrong is invisible.

Note what the two defects have in common, because it is the useful lesson and
not the arithmetic: **both were in a leaf whose correctness was assumed from
its shape.** A comparison dispatch that reads naturally, a floor division
written the obvious way. Neither was the sort of thing a feature-organised
suite has a file for.

And note what the fixed point could and could not do here. It could not find
either defect. But its **convergence count** located the second one's blast
radius immediately, for free, once the fix existed. A two-pass convergence on a
change that touches only immediate encoding is not an inconvenience to be
rebuilt away -- it is a measurement, and it says the compiler was miscompiling
itself. Read the pass count before you clear it.

Against the pre-fix seed the widened lattice reports **991 of 1037**, and all
46 disagreements trace to that one defect: four literal cases, plus every
comparison involving negative zero, whose operand is built from the very bit
pattern that misparsed. Again the count matches the diagnosis.

**On demand. Not in `build.ps1`, not in `test.ps1`.** It boots a VM and takes a
couple of minutes. Run it when you touch an operator, an emitter comparison
path, a condition code, or an immediate encoder, and before a public push.
`codex/test/ops/int-min-literal.codex` pins the literal band as a cheap standing
check that does not need the harness.

The guard beside it is `CDX9010`. `emit-comparison` now takes the operand type
and refuses a floating one, so the omission cannot be made again silently: a
future operator that forgets to dispatch halts the build instead of emitting a
reversed compare. Fired deliberately by reverting the Real arm of
`emit-ord-comparison` and rebuilding -- 8 errors, no binary.

## The Vector Operators Against the Host (`build/oracle-vector.ps1`)

The companion to `oracle-scalar.ps1`, same design (the host adjudicates, never
another Codex answer), pointed at the SSE2 packed arm. It found a silent
miscompile on its first run.

```powershell
pwsh build/oracle-vector.ps1                              # against seed/Codex.cdx
pwsh build/oracle-vector.ps1 -Kernel build/output/Sut.cdx
pwsh build/oracle-vector.ps1 -Keep                        # keep the generated source
```

**The axis is lane distinctness, and the gap it closes is structural rather
than statistical.** Measured 2026-07-27 across every vector test in the tree --
`vector-basic`, `vector-int`, `vector-f32`, `vec-array`, `vec-arith-hazards`,
`vec-extract-hazards`, `vec-nested-binop`, `vec-nest-probe`, `vec-pattern`,
`vec-select`, `vec-reduce-add`, `mask-ops` -- **not one produces a vector whose
two lanes differ.** Every one builds its operands with `vec-splat` or
`vec4-splat`, and splat writes the same value to both lanes
(`emit-vec-splat-builtin` stores the same register at offset 0 and offset 8).

That is not a thin spot. It makes four things unobservable by construction:

| | |
|---|---|
| `vec-extract v 0` and `vec-extract v 1` | read the same bytes, so lane indexing is never exercised |
| an emitter that computed lane zero and broadcast it | `addsd` where `addpd` was meant is one prefix byte, and gives the identical answer |
| `mask-any` and `mask-all` | `movmskpd` can only yield 0 or 3, so over every mask the tree has ever built **they are the same function** |
| `mask-count` | is never asked for the answer 1 |

`vec-splat` cannot express a distinct-lane vector, which is why no test does.
The oracle builds one by writing two doubles into a sixteen-byte buffer with
`poke-byte` and loading it with `vec-load-at`.

**Second axis: temp pressure.** `alloc-temp` rotates a six-register pool, and
`X86_64State.codex` says in its own prose that a temp allocated while an
operand is still live can be handed that operand's register, that the collision
"is phase-dependent (it appears only when the surrounding expression has
consumed the right number of temps)", and that this "is how bit-xor of three
calls shipped computing x ^ x = 0". A one-shape oracle cannot see a
phase-dependent hazard, so every case is asked twice: lean, and again with
three further vector results and a mask held live across the answer.

Answers are compared as IEEE bit patterns through `real-to-bits`, so no print
formatting sits between the guest and the host.

### What it found: a mask query reading the wrong register

**117 of 130 against seed `AFFD4511`.** All thirteen disagreements are in the
comparison group; arithmetic, reduction and extraction agree on distinct lanes
throughout, which is the useful negative -- the packed arithmetic arm is
genuinely per-lane correct.

Reduced to a minimal repro, with an empty mask (so `any` and `all` are False
and `none` is True):

| expression | want | got |
|---|---:|---:|
| `mask-none m` alone | True | True |
| `4 * b2i (mask-none m)` | 4 | 4 |
| `b2i (mask-any m) + 4 * b2i (mask-none m)` | 4 | 4 |
| `2 * b2i (mask-all m) + 4 * b2i (mask-none m)` | 4 | 4 |
| `4 * b2i (mask-none m) + b2i (mask-any m)` | 4 | 4 |
| **`b2i (mask-any m) + 2 * b2i (mask-all m) + 4 * b2i (mask-none m)`** | **4** | **0** |

Two mask queries in one arithmetic expression are correct. Three drop the third
term, silently, exit 0, no diagnostic.

**And it needs the operands to arrive from a call.** The identical three-query
expression over `vec-splat` operands answers 4 correctly; it collapses to 0
only when the vectors come from a function call. So the splat-only coverage gap
above is not merely adjacent to this defect -- it is what hides it.

### The cause: movmskpd dropped its REX prefix

**Fixed. `movmskpd` was encoded with no REX byte**, so a GPR destination in
R8-R15 lost its high bit and `modrm`, which masks its operands to three bits,
encoded the low-three-bits register instead. `alloc-temp` rotates
`[RAX, RCX, RDX, RSI, RDI, R11]` and **R11 is the only extended register in
that pool**, so roughly one mask query in six wrote its bits to RBX while the
`test-rr` that follows -- which does emit REX -- read R11.

Read off the emitted bytes, three consecutive `mask-none` queries in one
expression:

```
66 0F 50 F0  movmskpd esi   48 85 F6  test rsi, rsi    agree
66 0F 50 C0  movmskpd eax   48 85 C0  test rax, rax    agree
66 0F 50 D8  movmskpd ebx   4D 85 DB  test r11, r11    DISAGREE
```

`sete` then answered from whatever R11 happened to hold, and the query
returned a wrong Boolean with no diagnostic. It also clobbered RBX, which is a
callee-saved local register. The fix is the conditional-REX form
`movd-from-xmm` four definitions above already used; `movmskpd` was the only
GPR-taking encoder in the chapter without it. The oracle goes 117/130 to
**130/130**.

**Both earlier candidates were wrong, and are recorded because the way they
were wrong is the useful part.** The first was the mask builtins' bare
`alloc-temp`; the second was `emit-binary-staged`'s push/pop arm. Each was
plausible, each was read carefully, and each was contradicted by a probe: a
three-term expression of three ordinary calls is correct, and padding a
correct expression with eight spilled locals leaves it correct. **What settled
it was dumping the bytes.** Three probes of inference cost more than one
disassembly would have, which is `docs/PM/Active/Stories/IQuit.md`'s lesson
arriving on schedule.

**Pinned by `codex/test/vec-mask-hazards`**, which is the `vec-arith-hazards`
analogue the mask builtins never had. Both directions fired: on the unfixed
seed `empty-mixed` answers 0 against 4, `empty-same` 3 against 7 and
`mixed-count` 21 against 7, while the two `vec-splat` controls answer
correctly in **both** runs -- which is the whole reason splat-only tests could
not see this.

**That test is a witness, not a guarantee, and it says so in its own prose.**
It reaches R11 through the temp rotation, so it is sensitive to how many temps
the surrounding code consumed: written against chapter constants instead of
parameters, the identical probes pass on a broken compiler, because a
top-level constant is a zero-argument function and so a call. The first draft
of the test was written that way and passed on both compilers -- a test that
could not fail, caught only by running the negative control. If it is ever
suspected of having gone quiet, re-derive it by dumping the bytes and checking
that every `movmskpd` names the same register as the `test-rr` after it.

## The CCE Text Predicates Against the Host (`build/oracle-cce.ps1`)

The third oracle, same design as the scalar and vector ones and pointed at the
last uncovered axis on that list: the character predicates.

The axis is the **CCE/ASCII boundary**. CCE numbers its code points by
frequency, not by Unicode order, so every predicate is a band test on a
numbering that shares no boundary with ASCII: digits are 3..12, lowercase
13..38, uppercase 39..64, punctuation 65..96, sixteen accented Latin letters
97..112, fifteen Cyrillic 113..127. A band written with ASCII constants is
wrong in a way that still type-checks, still runs and still returns a Boolean.
CCE 48..57, which is what an ASCII digit test accepts, is `D L C U M W F G Y P`.

That class is not hypothetical here. `text-to-upper` and `text-to-lower`
shipped subtracting 32 from a CCE code point, a no-op on every letter that
corrupted the one band it did reach, and three chapters carried the same shape
in a week.

**The harness drives by UNICODE, not by CCE.** It hands the guest a Unicode
code point; the guest converts with `from-unicode` and answers the predicate;
the host says what that character actually is. Driving by CCE would mean taking
the expected answers out of `cce-to-unicode-table`, which is guest data, and
that is a decoder checked against its paired encoder. Guest data chooses which
inputs are interesting and never supplies an answer.

Two families, 1516 cases:

- **P**, the predicates, over every Unicode code point Tier 0 carries: nine
  predicates, the digit value, and both case conversions.
- **R**, round trip and refusal, over 17 Unicode blocks. `from-unicode` must
  answer either the same character back or `-1`, never a **different**
  character. Silent replacement is the hazard `CCE.codex`'s own UTF-8 prose
  describes and is precisely what a round trip through our own halves cannot
  see.

R fires both ways: every printable ASCII character plus NUL and LF **must**
map (97 code points, exactly Tier 0's size), and every other ASCII control
character **must** be refused.

```powershell
pwsh build/oracle-cce.ps1
pwsh build/oracle-cce.ps1 -Kernel build/output/Sut.cdx
pwsh build/oracle-cce.ps1 -Keep        # keep the generated source
```

### What it found: the round trip is clean and the classification is not

Measured 2026-07-28 after G1 closed: **1485 of 1516 agree with the host, 31 in
documented gaps, 0 unexplained.** Measured 2026-07-27 against seed
`8B7A02ECE8C6E8CD` it was 1484/1516 with 32 in gaps, and the same figure
against the pre-widening seed `EFC7FCD09CCA6B03` was also 1484/1516 with 32 in
gaps -- a different set, because G2's 31 cases closed and G3's 31 remained,
which is why that total did not move. **Read the gap breakdown, not the
total.**

**The R family is a clean negative and worth keeping.** Zero silent
replacements across 1206 cases spanning Latin-1, Latin Extended, Greek,
Cyrillic, Hebrew, Arabic, Devanagari, Thai, Hangul, CJK, kana, general
punctuation and emoji. Every mandated map mapped; every mandated refusal
refused. The per-block mapped/probed counts are printed on every run, so a
`from-unicode` that quietly stopped mapping a block is visible even where each
individual case would still be "allowed".

The remaining 31 disagreements are in the P family. Three clusters were
recorded and two are now closed:

| | cases | |
|---|---:|---|
| G1 | 1 | `is-whitespace` answered True for NUL, because the band was `char-code c <= 2` and Tier 0's first three slots are NUL, LF, space. **CLOSED 2026-07-28** -- Damian ruled NUL is not whitespace, so the band is 1..2 in all four spellings, and `classify` moved with it (NUL answers Other) |
| G2 | 31 | `is-letter` and `is-lower` answered False for all 31 non-ASCII Tier 0 letters. **CLOSED 2026-07-27** |
| G3 | 31 | `to-upper` is the identity on those same 31 letters |

**G2 was a language decision and Damian took it**: any language can be used in
the source, so a predicate that admits only English letters is wrong. Both
predicates are two-band tests now and
`codex/test/ident-letters.codex` pins it.

**A letter is two bands, and that is the whole subtlety.** CCE's punctuation
sits BETWEEN the letters: 13..38 ASCII lowercase, 39..64 ASCII uppercase,
**65..96 punctuation and symbols**, 97..112 sixteen accented Latin letters,
113..127 fifteen Cyrillic. So the obvious widening -- extend the top from 64 to
127 -- calls every punctuation character a letter. The band has a hole in the
middle and the test has to have one too.

**The function that looked like corroborating evidence was the broken one.**
This section previously cited `cce-is-letter-any`'s Tier 0 arm (`13..127`) as
proof the chapter disagreed with itself about `is-letter`. It does disagree,
and `cce-is-letter-any` is the one that is wrong: it calls all 32 punctuation
code points letters. It has no caller anywhere in the tree, so it never bit. It
delegates to `is-letter` now rather than keeping a second spelling of one band.

**`to-lower` and `to-upper` had to stop borrowing the predicates.** The plus and
minus 26 is arithmetic between the two ASCII bands and is meaningless outside
them, so a predicate that legitimately grew must not gate it. Measured: leaving
`to-upper` asking a widened `is-lower` raises 89 disagreements, shifting all 32
punctuation characters into the accented and Cyrillic bands -- `!` uppercases to
a backtick. That is the historical minus-32 corruption with a different
constant, and the decoupling is what stops it.

**G3 is structural rather than a band error, which is why it did not close with
G2.** The uppercase of e-acute is a Tier 1 code point and a `Char` carries a
Tier 0 byte, so `Char -> Char` cannot express the answer at all. The identity is
the honest answer for a function of that shape; case above Tier 0 needs an entry
point that does not exist.

### Which implementation a call to `is-letter` actually reaches

Worth knowing before changing either, and it is not what the file layout
suggests. There are **two** implementations: the `CCE.codex` definition and the
compiler builtin `emit-is-letter-builtin`. Measured rather than assumed:

- The **cited definition wins**. `codex/compiler/opening.codex` carries
  `cites Foreword chapter CCE`, so `Foreword--CCE` is in the concatenated unit
  and the lexer's `is-letter-code` resolves to the chapter definition -- even
  though `Lexer.codex` itself cites nothing. Editing `CCE.codex` alone changes
  what the compiler does, which is why this change needs a seed.
- The **builtin is reached by an uncited call**, and it is genuinely reachable:
  a chapter calling `is-letter` without citing CCE compiles and, against the
  pre-change seed, answers 0 for CCE 97 (e-acute), 1 for CCE 15 (`a`) and 0 for
  CCE 70 (punctuation).

Both were changed together. Two spellings of one band that can drift apart is
the duplicate-name hazard `StringUtils` already recorded as a live defect, and
the third of those three probe values is the control that matters: the rebuilt
builtin must still answer 0 for CCE 70, or the two-band test has swallowed the
punctuation hole.

### The suppression mechanism is the part that needed proving

A documented gap is expressed as a **second expected answer**, not as an
exclusion list, so each case lands in one of three states: agrees with the
host, agrees with the gapped answer, or agrees with neither and fails. The
reason is the third column. An exclusion list is a claim with no runner behind
it and goes on suppressing a case long after the case is fixed; because the
host answer is still computed, **a gap that closes is reported as loudly as one
that opens.**

**Three controls, fired rather than assumed**, all against seed `EFC7FCD09CCA6B03`:

- **It can fail, and only where it should.** `is-punct`'s band narrowed by one
  slot gives exactly ONE failure, at U+0025, in the `pu` position, and nothing
  else moves.
- **The gap arm does not swallow its class.** Widening `is-lower` takes G2+G3
  from 31 suppressed cases to 0 and raises 89 failures. The arm matches one
  exact answer, not a category.
- **A closed gap is reported.** Marking U+0061 as gapped, where the compiler
  already agrees with the host, prints the gap-closed notice naming that case.

On demand. Not in `build.ps1`, not in `test.ps1`. Run it when you touch CCE, a
text predicate, the Unicode boundary, or a chapter that classifies characters,
and before a public push.

## The WCET Validator, Both Directions (`build/wcet-validate.ps1`)

Recorded here because this is where every other harness's both-directions
evidence lives, and because the CRA matrix publishes a row that rests on it.

Run 2026-07-27 against seed `A5758E05`:

| driver | punctual functions | verdict |
|---|---:|---|
| `codex/test/wcet-probe.codex` | 3 | PASS |
| `codex/test/examples/missile-warning.codex` | 4 | PASS |
| `punctual-iot` | 5 | PASS |
| `punctual-smoke` | 1 | PASS |

All at exit 0. **The negative was fired rather than assumed:** a deliberately
over-budget probe FAILS at exit 1 with `observed 106 > budget 1`. Without that
half, a validator that always passes is indistinguishable from one that works.

**Run it; do not gate it.** This was first recorded as "a gate nothing runs",
which was the wrong reading -- an agent running it when pertinent IS the
mechanism, and putting it in the standard battery is the thing that is
declined. Pertinent means: you touched `punctual`, the instruction counter, or
an emitter that changes instruction selection.

**One known hole, and it is not this harness's fault.** A declared `punctual`
budget is silently unenforced when the inliner consumes the function: identical
source at budget 5 against 11 instructions reports `CDX6010 220% of budget 5`
plus `CDX6011` under `-Passes fold-constants`, and **nothing at all** under the
default pipeline. The structural checks (CDX6001-6005) are safe -- they run in
CHECK, before the IR passes. Only the callee's own budget is lost, and the site
that knows a punctual definition ended up bodyless is the emit-time dead-code
prune, not the inliner.

## The IoT Protocols Against Foreign Implementations

Two harnesses, both on-demand (they boot VMs and third-party servers), both
built to the rule `BrotliBeatsOpus.md` ends with: **the oracle runs in both
directions, and the reverse direction is written first.**

```powershell
pwsh build/coap-interop-test.ps1                              # our client, aiocoap's server
pwsh build/coap-serve-test.ps1                                # our server, aiocoap's client
pwsh build/mqtt-interop-test.ps1                              # mosquitto, QoS 1 and QoS 2
pwsh build/mqtts-interop-test.ps1                             # MQTT over TLS 1.3, mosquitto
pwsh build/https-interop-test.ps1                             # fetch-tls, three OpenSSL servers
pwsh build/mqtt-interop-test.ps1 -Kernel <cdx> -KeepArtifacts
```

`https-interop-test.ps1` is the one that drives `Net chapter HttpFetch`'s own
https path. `mqtts-interop-test.ps1` reaches a foreign server but re-implements
the record framing in `tools/mqtts-client.codex`, so `tls-recv-record`,
`https-drive`, `https-request` and `https-recv-app` were still exercised only
by code that agreed with them. Three `openssl s_server -WWW` instances, three
certificate stories: an ECDSA P-256 chain on 4443 whose CertificateVerify is
`ecdsa_secp256r1_sha256`, an RSA-2048 chain on 4444 whose CertificateVerify is
`rsa_pss_rsae_sha256` over certificates signed with PKCS#1 v1.5, and on 4445 a
genuine, unexpired, correctly named, self-signed leaf that no anchor vouches
for and that must be refused. Both good fetches compare the body byte for byte
against the file the harness served, so a handshake that completes and then
delivers the wrong bytes fails here rather than passing with a footnote.
`build/mint-https-fixtures.ps1 -Patch` re-mints the three chains and writes the
CA literals into the guest; the harness re-derives them from the PEMs and
refuses to run if they have drifted.

**It found a defect on its first run**, and it is the same shape as the
`signature_algorithms` one: `tls-recv-record` pulled bytes with
`net-io-recv-raw`, which returns only when a frame carried payload and so
cannot tell a peer that has finished from a peer that is slow. `http-recv`
says exactly this about the cleartext path, twenty lines above in the same
chapter, and watches the connection state instead. The https half did not, and
no loopback could show it: a loopback never closes. The first foreign server
sent its response, sent a FIN, had the FIN acknowledged by our own stack, and
the client then spun its fifty-million-iteration budget with the whole body
already in hand. One fetch had not returned after 262 seconds. With the close
watched, all three run in about 147.

`coap-serve-test.ps1` is the one that exercises `codex-vm -portfwd udp:...`
and therefore the guest as a datagram SERVER, which is the role an IoT
device actually plays.

**`mqtts-interop-test.ps1` is the first test in the tree where OUR TLS
CLIENT talks to a foreign SERVER**, and it found a defect on its first run
that had been invisible for the life of the stack: the ClientHello carried
no `signature_algorithms` extension. RFC 8446 section 9.2 makes it
mandatory and a conforming server MUST abort without it, so **every TLS and
DTLS handshake Codex ever initiated was one no conforming server would
accept.** Nothing could see it. Our own server does not inspect the
extension, so every loopback passed, and `tls-interop-test.ps1` drives our
SERVER with openssl's client -- the client half had never met a conforming
peer. mosquitto's OpenSSL says `missing sigalgs extension` and closes
without an alert, which from the client's seat is indistinguishable from a
server that never answered.

The lesson is the general one and it is worth stating in its own right:
**an interop harness validates the half it points at.** Two harnesses that
both drive our server prove nothing about our client, however foreign the
peer.

`codex/test/apps/coap-loopback` and `codex/test/apps/mqtt-loopback` are the
paired in-battery tests. They prove the endpoint agrees with our own codec
and they cannot prove more than that -- which is the whole reason these two
exist.

**Both found real bugs on their first run, and neither bug was reachable
from a loopback.**

- **CoAP.** `coap-text-to-bytes` converted a Uri-Path segment with
  `char-code`, which answers the CCE code point and not the ASCII one, so
  every path Codex had ever built named a resource no server could match.
  aiocoap answered 4.04 for a resource that was there. The existing
  `coap-encode` test could not have caught it: it asserts
  `list-length pkt > 0` and never looks at the bytes, so it is blind at
  exactly the thing that changed.
- **CoAP, again, from the control.** With the path fixed, the second
  exchange -- a deliberate request for a resource that does not exist --
  came back 2.05 with the FIRST resource's payload. The client built a
  fresh endpoint per request, so the message id restarted at 1, and RFC
  7252 section 4.5 requires a server to treat a repeated message id as a
  duplicate and replay its cached response. aiocoap was right. Nothing in
  a loopback caches, so nothing in a loopback could have shown it.
- **MQTT.** The chapter had no parser at all, so half the protocol was
  unreachable and the interop test is the only thing that could say so.
  `mqtt-build-publish` also had no packet-identifier parameter, which is
  malformed at QoS 1 -- latent only because every caller in the tree
  publishes at QoS 0, where the field is absent and the code was
  accidentally right.

**The MQTT harness is the one to copy.** It is genuinely bidirectional and
the two halves catch different things: `mosquitto_sub`, a separate process
using mosquitto's own client library, must receive what the guest published
(our encoder), and the broker then delivers that message back to the guest's
own subscription, which the guest must report byte-exact (our decoder). Only
the second half can catch a decoder that reads only what our encoder writes,
and that is precisely the half the compression stack never had.

## A Quotation From A Peer (`build/quote-from-peer-test.ps1`)

The same quotation is now proven through three transports, and the three tests
are deliberately the same work, the same digest, the same signer and the same
signature so that the transport is the only variable:

| Test | The work travels |
|---|---|
| `codex/test/quotes-gate` | beside the source, in the `%%QUOTED-WORKS%%` blob |
| `codex/test/quote-from-store` | on an attached disk, in the fact store |
| `codex/test/quote-from-peer` | over TCP, from another booted guest |

```powershell
pwsh build/quote-from-peer-test.ps1                            # against seed/Codex.cdx
pwsh build/quote-from-peer-test.ps1 -Kernel build/output/Sut.cdx
pwsh build/quote-from-peer-test.ps1 -KeepArtifacts             # keep the disks
```

`store-quoted-work` writes the work onto a blank disk; `tools/cdx-serve` boots on
that disk and listens; `codex/test/quote-from-peer` quotes the digest with **no
blob and no store attached** and is compiled with `compile.ps1 -Peer`. The host
asks, prepends the answer to the blob, and the compiler admits it through the
same four guards. `sort-ascending` exists only in the quoted work, so
`sorted=105` means the definition crossed a socket.

**Step 5 is the test.** Steps 1-4 pass if the work reaches the compiler by *any*
route, and the entire claim is that it reached it by this one -- so the last step
compiles the same source against a peer holding nothing and requires that it
**fail**. Without it this is another function that always answers the same thing.

**Two traps, both of which cost a debugging round here:**

- **A running codex-vm's `-output` file is empty however healthy the guest is.**
  It flushes on exit. Reading a live server's output said "the server never
  booted" about a server that was serving perfectly; the control that settled it
  was booting the *known-good* disk the same way and getting the same silence.
  Ask the server a question instead -- `cdx-checkout` runs the identical
  `repo-index-from-disk disk-load` line and answers in one command.
- **A peer takes ~20 s to boot and index, and the port forward accepts the host
  connection long before the guest is behind it.** So an early ask does not fail
  fast; it blocks on a socket nobody is reading and burns the whole read timeout,
  which leaves room for about two attempts in a minute. Wait for the peer to
  answer a hash nobody stored before asking it for one that matters.

## A Peer Nobody Named (`build/registry-locate-test.ps1`)

`quote-from-peer` proves a quotation resolves from a peer the compile was TOLD
about. This proves it resolves from one the compile was never told about: the
only address it is given is a registry's.

```powershell
pwsh build/registry-locate-test.ps1
pwsh build/registry-locate-test.ps1 -KeepArtifacts     # keep the disks and logs
pwsh build/registry-probe.ps1                          # ~90 s, the registry alone
```

A work is written to a disk; `tools/cdx-registry` boots knowing nobody;
`tools/cdx-announce` tells it an address holds those digests and exits;
`codex/test/quote-from-peer` is compiled with `-Registry` naming only the
registry. `sort-ascending` exists only in the quoted work, so `sorted=105` means
the definition crossed a socket to a peer whose address came from discovery.

**Steps 7 and 8 are the point.** Steps 1-6 pass if the work arrives by ANY
route. Step 7 compiles against a registry nobody ever announced to and requires
the compile to FAIL -- without it, the work could be arriving from somewhere
else and the harness measures nothing. Step 8 announces a peer that holds
nothing: the registry must still name it (an announcement is a rumour and is not
verified) and the compile must still fail, one step later, **at the fetch**.
That is what keeps "the registry names it" from being read as "the work is
trustworthy".

**`build/registry-probe.ps1` is the instrument to reach for first.** It boots
the registry alone, asks three times, and prints the GUEST's own trace, in about
ninety seconds against this harness's ten minutes. Run the full harness once to
produce the cdx, then iterate there.

**This harness misreported the product seven times before it worked**, and every
one was in the measuring apparatus rather than the server. The last was the
sharpest: `Get-QuotedHashes` returns an array, and PowerShell unrolls a
single-element array on `return`, so `(Get-QuotedHashes ...)[0]` indexed a
STRING and asked the registry to locate a digest called **"7"**. The registry was
announced the real digest and asked for a different one, so step 5 could not pass
however well the transport worked. Wrap every work-wire call that yields a list
in `@()`. A green here is only as good as what the harness asked for.

## Manifest-Scope Pin (`codex/test/manifest-pin`, gated)

The compiler writes each program's capability manifest into the CDX header
(`manifest-cap-bytes`, `X86_64Chapter.codex`): per capability, a little-endian
cap-id, a direction, a scope length, the scope bytes (CCE), and eight zero
bytes. For a long time that emission was checked only by eye, because **nothing
inside a guest can read its own manifest** -- the running program is the
extracted code, not the CDX file -- so a regression in the manifest (the old
"readwrite for every capability" direction bug, a dropped scope, a wrong
cap-id) was caught by no test.

The pin closes that. `codex/test/manifest-subject.codex` declares a known
two-capability scoped manifest (`Network.Write "api.example.com"`,
`FileSystem.Read "/config/"`) and nothing else; `codex/test/manifest-pin.codex`
reads the header at offset 136 (manifest offset, le64) and 144 (size), walks
the entries with `block-read-sector`/`peek-byte`, and prints each cap-id,
direction, scope-length and scope, compared to `manifest-pin.expected`.

**It is a battery test as of 2026-07-27, and until then it was not.** Both
`.codex` carried a `.skip` and the pin ran only if somebody remembered to run
a shell script, which is a test nobody runs -- real but ungated, and the tree
therefore kept less than `manifest-cap-bytes` deserved. What blocked it was
narrow: the reader needs its subject built **fresh, by the compiler under
test**, and `.disk` names a file, which is frozen by definition. The
`.disk-src` sidecar names a TEST instead, and the battery attaches that test's
just-compiled CDX. One line of `test.ps1` and two deleted skips.

`build/manifest-pin-test.ps1` remains for the one thing the battery cannot do,
pointing the pin at some other compiler:

```powershell
pwsh build/manifest-pin-test.ps1                             # against seed/Codex.cdx
pwsh build/manifest-pin-test.ps1 -Kernel build/output/Sut.cdx  # against a fresh SUT
```

**Sabotage-checked against the defect it was written for.** Making
`manifest-cap-direction` answer readwrite unconditionally -- the
old bug verbatim, which handed a program declaring `[FileSystem.Read]` a
readwrite filesystem capability in its own manifest -- rebuilding the compiler
and recompiling the subject moves exactly the two entry lines, `dir=1` and
`dir=0` both becoming `dir=2`, and nothing else. The pin reports the manifest's
SIZE and CONTENTS but never its OFFSET: the offset is `224 + code size` and
moves with every codegen change, while the content is what the pin is about.

## The IR-Passes Report Pin (`build/ir-passes-test.ps1`)

The `ir-check` and `occ-report` IR passes run in IR mode and used to report
nothing, because merging their diagnostic bags into `compile-frontend-passes`
killed the compiler in `bag-add` -- the giant let-chain's register allocation
crossed a spill cliff, on a source as small as `codex/test/arithmetic`
The merge was dropped to ship, so the passes ran and printed
nothing. The fix moved the merge into a helper (`frontend-bag-with-passes`, so
the caller's binding count is unchanged) and taught `emit-ir-uni` to print
notices the way the CDX emit path already does.

```powershell
pwsh build/ir-passes-test.ps1                              # against seed/Codex.cdx
pwsh build/ir-passes-test.ps1 -Kernel build/output/Sut.cdx  # against a fresh SUT
```

It compiles `arithmetic` in IR-UNI mode twice. **Positive:** with `-Passes
occ-report`, the compile must reach `IR-END` (the merge did not crash it) and
must emit at least one `OCC ` line. **Negative:** with no passes, zero `OCC `
lines -- so the positive is the pass reporting, not a constant the emitter
always prints. Both were run: the fix gives 30 OCC lines then 0; the pre-fix
seed gives 0 then 0 and fails the positive, which is what makes the pin
load-bearing rather than a comment. On-demand (boots VMs), not gated -- but
the crash half is covered by every build regardless, because `cross-smoke`
compiles through `compile-frontend-passes` in IR mode, so a regression of the
spill cliff fails the gate there.

## The LIR Dump Pin (`build/lir-dump-test.ps1`)

`codex/test/lir-check.lir-expected` records one line per definition: block
structure, live-in sets, register assignment, and the verdicts of both the
structural and the allocation verifier. **Until 2026-07-18 no harness read
it.** Grepping the tree for the filename found three doc mentions and the
test's own prose, and no runner -- so it was a snapshot of one past run going
stale in silence.

The correctness half was never the gap. `lir-check.expected` is a real
`.expected` the battery checks, and it pins what those functions *compute*.
What had no coverage is what the LIR *is*, and that is the half that hid the
`col-hue` parallel-move miscompile through ten sessions of green benches and
byte-identical fixed points.

```powershell
pwsh build/lir-dump-test.ps1                                # against seed/Codex.cdx
pwsh build/lir-dump-test.ps1 -Kernel build/output/Sut.cdx   # against a fresh SUT
pwsh build/lir-dump-test.ps1 -Accept                        # re-record the pin
```

The dump rides the info channel as **CDX4030** under `-Passes lir-dump`, so it
lands in the compile log and the comparison is a string compare. Measured
2026-07-18 against seed `00F5F34F`: **83 lines, all verifiers ok.**

**It fails two ways, and both were run rather than assumed.** A one-register
perturbation of the pin (`v1->r4` to `v1->r9`) fails with a two-line diff.
Omitting `-Passes lir-dump` yields zero dump lines and fails a **60-line
floor** -- which exists because an empty actual compared against an
accidentally-emptied pin is the classic function that always answers the same
thing. A pin nobody can fail is a comment.

**The pin is RED on purpose as of 2026-08-05, and it is a stale pin rather
than a defect.** 113 lines pinned against 119 emitted; the six extra lines
are `math-isqrt` / `math-isqrt-loop`, both carrying `[check: ok]`. Damian
parked LIR that day, so nobody re-recorded it. **Whoever picks LIR back up
re-records it**; do not `-Accept` it as a drive-by, and do not report it as
a regression.

**`-Accept` records whatever the compiler just did, including whatever it did
wrong** -- the same hazard as `test-gui.ps1 -Accept` and the same rule: it
prints the delta it is about to accept, and you read every line before you
take it. Register assignment legitimately moves on any allocator or selector
change, so this pin is expected to need re-recording; that is what makes
reading the diff the whole discipline rather than a formality.

### What gates, and what does not (decided 2026-07-18)

**The snapshot above is deliberately NOT in `build/build.ps1`.** It records
register assignment, which legitimately moves on any allocator or selector
change, so gating it would go red on correct work and train everyone to
`-Accept` reflexively. That converts a pin into a rubber stamp, which is worse
than no pin because it reads like coverage.

**The verdicts gate instead, and they gate somewhere better than this harness
could reach.** Both verifiers stand on the *emission path* (`lir-emit-try`,
`Emit/X86_64Lir.codex`), so they run for every selector-handled function in
every compile rather than for the 26 functions in one test file, and the
allocation check is handed **the assignment the emitter is about to use, not a
re-derived one**. A violation halts the build:

| Code | Verifier | Catches |
|------|----------|---------|
| CDX9006 | prologue parallel move | a parameter that does not end in its assigned register |
| CDX9007 | structural | a use reading a vreg no path defines, a malformed block, an undefined result |
| CDX9008 | allocation | a use whose assigned location holds a different value (clobbered by a call or division) |

**All three have been observed to fire.** That matters more than it sounds: an
unfired guard is worth exactly what no guard is worth, which is what
`accum-at-capacity` proved by sitting uncalled while this document asserted it
ran. CL 8867 proved only the *absence of false positives* (battery identical to
baseline, fuzz clean). The fire tests were run separately, 2026-07-18, each by
building a deliberately broken SUT and compiling with it:

- **CDX9008** -- `lir-scan-cross` changed to place a call-crossing value in a
  caller-saved register. Compiling `bench/codex/fib.codex` halts with
  `alloc vreg 0 not in r4 at block 5 insn 2`, no binary emitted. This is a
  genuine allocator defect, and it is the same class as the `col-hue`
  miscompile.
- **CDX9007** -- entry parameters under-marked by one. Compiling
  `bench/codex/sum.codex` halts with `use before def in block 1 insn 0`, no
  binary emitted.
- **CDX9006** -- proven at the time it was built by corrupting `lir-pmv-pick`.

**One corruption that did NOT fire, recorded because the negative is
informative:** making `lcoal-param-safe` answer True unconditionally (unsafe
coalescing, the historical bug class) produced *wrong values*, not invalid
structure, and CDX9007 correctly stayed silent. Wrong answers are the
`.expected` battery's job, and `codex/test/lir-selector-smoke.codex` in the BVT
is what holds that ground: it pins five miscompile shapes by their answers,
including the `col-hue` parallel-move clash. **Structure, allocation, and
answers are three different instruments, and reaching for the wrong one reads
as a green that means nothing.**

## Expected-Failure Tests

173 tests in `codex/test/errors/` verify that the compiler rejects
invalid programs with the correct diagnostic codes. Each has a
`.failing` sidecar listing the expected CDX error codes. Examples:
`apply-non-function` (CDX2001), `duplicate-def` (CDX3002),
`infinite-type` (CDX2010), `linear-twice` (CDX2061).

## Board Tests (`build/boards-test.ps1`)

The nine IoT board drivers cannot live in the default battery: three of them
need `codex-vm -board-mmio` to back the register windows above the RAM
ceiling, and the generic harness passes no such flag. They run from
`build/boards-test.ps1` instead, all together, with the flag on.

```powershell
build/boards-test.ps1                  # the board driver batteries
build/boards-test.ps1 -Only rp2040     # one board
build/boards-test.ps1 -Renode          # plus the arm64/riscv64 boot smoke
```

Measured 2026-07-13 (seed 90137D4D):

| Boards | Pass | Fail | Sub-tests |
|---|---:|---:|---:|
| 9 | **9** | **0** | **108** |

Each test's `opening` returns the number of sub-tests that passed and the
`.expected` sidecar holds that number, so the harness re-measures the total
rather than quoting it.

A caution earned the hard way: QEMU virt's test is
`codex/test/qemu-virt-board.codex`, not `qemuvirt-drivers.codex`. The first
cut of this harness assumed the `<board>-drivers` convention, did not find
it, reported the board as having no smoke test, and that false gap reached a
recorded gap before anyone opened the directory. **Do not infer a coverage
gap from a filename.**

Since 2026-07-13 every board driver function that touches a register declares
`[Device.Mmio]`, and the `Boards` quire is no longer effect-exempt -- so these
tests also pin the effect rows. `codex/test/errors/board-mmio-pure` is the
adversarial half: a pure signature cannot launder a driver call.

What this proves: a register access lands on memory and reads back what it
wrote, so the address arithmetic, the access width, and the read-modify-write
logic are exercised. What it does not prove: peripheral behaviour. No silicon
has been in the loop. Renode is where that lives.

## GUI Tests (`build/test-gui.ps1`)

Every GOP application used to be untestable. The skip inventory says so in
a dozen places -- "requires display", "requires keyboard", "blocks waiting
for keyboard input" -- so the widget stack, the input path, and every app
built on them had no regression coverage at all. The circuits mouse bug
(a held button was legible for one poll, so no drag could ever be seen)
lived through months of green batteries for exactly that reason.

`build/test-gui.ps1` drives an app with scripted input and compares the
resulting frame against a recorded image. It runs **headless**: codex-vm
injects the pointer and keyboard directly into the guest through the same
fields the window proc writes (press latch included), so the guest cannot
tell a scripted hand from a real one. No host cursor moves and no window
takes focus -- a UI test does not fight the operator, or another agent, for
the physical mouse, and several can run at once.

```powershell
build/test-gui.ps1 -Kernel build/output/circuits.cdx -Script codex/test/gui/circuits-drag.uiscript
build/test-gui.ps1 ... -Accept        # record the current frame as expected
build/test-gui.ps1 ... -KeepArtifacts # keep the actual frame + timelines
```

### The .uiscript

One command per line, `#` comments. Coordinates are client pixels.

| Command | Meaning |
|---------|---------|
| `boot <ms>` | when the app is up and the script may start (default 8000) |
| `move <x> <y>` | move the pointer |
| `press <x> <y> [btn]` | button down (1 left, 2 right, 4 middle) |
| `release <x> <y>` | button up |
| `click <x> <y> [btn]` | hover, down, up |
| `drag <x1> <y1> <x2> <y2> [steps]` | press, move in steps with the button held, release |
| `key <scancode>` | Set-1 make code |
| `wait <ms>` | advance the clock |
| `settle <ms>` | quiet time after the last event before the frame is taken |
| `vmargs <flag> ...` | extra codex-vm flags for this test (repeatable) |
| `mask <x> <y> <w> <h>` | exclude a rect from the comparison (repeatable) |
| `expect-ink <x> <y> <w> <h> <min> <max>` | lit-pixel count in a rect must fall in range (repeatable) |

**`vmargs` exists because a frame is only reproducible on a machine that has
been told to hold still.** The standing case is the clock: an app that paints
the time cannot be compared against a recorded image while codex-vm answers the
RTC from the host, and for a long time the conclusion drawn from that was that
GuiOS could have no golden. That was a statement about the machine, not the
app. `vmargs -rtc 2026-07-21T09:41:07` fixes the clock and the frame becomes a
function of the program and the flags. Keeping it in the `.uiscript` rather
than in a harness switch means the test states its own machine, exactly as a
`.vmargs` sidecar does for the serial battery.

**Freezing the clock is not the same as making everything deterministic, and
the difference is where the honesty lives.** `apps/guios/tests/desktop-chrome`
deliberately captures the F3 hardware view rather than the prettier F1
dashboard, because the dashboard prints `Uptime: <ticks/18>s`, which at an
11-second deadline divides within one tick of a boundary and flips between 10
and 11 under host load. `-rtc` would not fix that and a tolerance would only
hide it. Uptime is legitimately not a function of the program, so it is kept
out of the frame rather than lied about. Choose the view before reaching for a
tolerance.

**When the volatile widget is in the middle of a view you want, `mask` is the
third option and it beats the other two.** `apps/guios/tests/dashboard` covers
the F1 view -- worth having because the memory gauge is one of the widgets 4.5
was about -- and masks the one 76x60 box holding the uptime figure, the heap
figure and the gauge fill edge. Three rules keep a declared hole safer than a
hidden one:

- **Derive the rect by measuring.** Capture the same frame twice with the
  volatile input different (two screenshot deadlines), diff, take the bounding
  box. `dashboard`'s box came out at x=203..268, y=87..139 over 278 differing
  pixels; guessing from a picture produces a mask that is both too big and, in
  the direction that matters, too small.
- **Run both controls.** Positive: the frame at a different deadline must
  compare CLEAN against the golden, which is what proves the mask covers the
  volatile region. Negative: the same run with the mask removed must FAIL,
  which is what proves the mask is load-bearing. A mask that has only ever been
  seen passing is indistinguishable from one covering the whole frame.
- **Keep the hole visible.** The harness prints the rect count, the masked
  pixel count and the percentage on every run, and warns above ten per cent.
  A golden whose interesting half was silently never compared is the failure
  mode this mechanism exists to avoid, not one it is allowed to cause.

**Do not get there by making the widget invisible instead** -- setting a
volatile field's colour to the background, say. It reaches the same pixel
stability by changing the product to suit the test, and it leaves a hole that
no one reviewing the `.uiscript` can see, which is strictly worse than one
written down beside its reason.

### `expect-ink`, and why it is not `expect-text`

A masked rect is excluded from the comparison, so on its own it is **wholly
unchecked**: a field that went blank, doubled in length or filled with garbage
passed the golden silently. `expect-ink` counts the lit pixels in a rect and
requires the count to fall in a declared range. It does not read the value. It
is a heuristic, it is labelled as one, and it closes the blank-field hole for
the price of six numbers.

Derive the range by measuring across the values the field actually takes.
`apps/guios/tests/dashboard` reads 509 lit pixels at an 11000 ms deadline, 572
at 20000 and 616 at 30000 (uptime gains digits, the heap gauge fills), so its
range is 350..900: wide enough for the retry span, narrow enough that a blank
rect is out of it. The harness prints the observed count every run, so the
range is widened from evidence. Negative control, run: the same frame with the
range set to 5000..6000 fails.

**Reading the value would be better and is currently impossible for GuiOS.**
The work was built and then refused by its own verification, which is the
useful part of the story. A decoder needs a glyph atlas; the atlas harvest
decodes every sample back and requires it to equal the source string exactly;
that round trip failed on all nine samples, and dumping the pixels showed why:
**booted without a disk, GuiOS falls back to a block font that renders `s`, `t`
and `b` as one identical bitmap, and `d`/`o`, `O`/`0` and `5`/`9` likewise.**
`W` is a 3x6 solid stem. The value is not in the pixels to be read, so no
decoder can recover it.

Three cheaper designs were measured and rejected on the way, recorded so nobody
re-derives them: fixed-cell slicing (the fallback font is proportional -- pitch
measures 6.95, 7.07, 7.31 and 7.61 across four known strings), blank-column
segmentation as a decoder (adjacent glyphs touch; four of nine known lines
yield fewer runs than they have characters), and cropping a glyph to its lit
rows (which makes `d` and `o` identical -- caught by the round trip, invisible
to a run-count check).

The UI library's GPU text path is a different and tractable case: `gr-emit-text`
advances `x + i * 6`, a true fixed cell, and apps rendering through it are
legible. A real `expect-text` is worth building against that font.

**The diskless-GuiOS half of that sentence is now stale and is withdrawn.** It
read "or against GuiOS once its font fallback is fixed -- which is a defect on
its own merits, since a diskless GuiOS currently draws unreadable text to a
real screen." The fallback was fixed after this section was written:
`gui-load-fallback` (`apps/guios/GuiShell.codex`, a historical reference:
that chapter was retired when the desk moved to `apps/works/GopDesk.codex`
via GopBoot) set `fonts-loaded` to
**0** rather than rasterizing `px-pixel-font` into all three roles, so a
diskless boot dispatches through `Kernel chapter BitmapFont` -- an 8x16 bitmap
of 128 glyphs compiled into the binary -- and the synthesized font this
paragraph measured is no longer on that path at all. Verified 2026-07-28 by
reading the source, not the prose beside it.

What is NOT established is that the embedded bitmap font is legible; nobody has
pointed a decoder at it. The correct statement of the remaining gap is that
`expect-text` needs a font with a known fixed cell, and the measurement above
of `px-pixel-font` no longer describes what a diskless GuiOS draws.

The harness compiles this into two timelines (`t:x,y,btn` for the pointer,
`t:scancode` for the keyboard) and hands them to codex-vm as `-mouse-file`
and `-keys-file`. The frame is captured with `-screenshot` at a fixed
wall-clock offset, so a test is only as stable as the app's own determinism.

### Sidecars

| File | Meaning |
|------|---------|
| `foo.uiscript` | the input script |
| `foo.expected.bmp` | the frame the app must produce (p4 type `binary`) |

### Adding a GUI test

1. Write `codex/test/gui/foo.uiscript`.
2. Record the frame: `build/test-gui.ps1 -Kernel <app>.cdx -Script ... -Accept`
3. **Look at the recorded image.** `-Accept` records whatever the app did,
   including whatever it did wrong. An expected image nobody looked at is a
   bug fixture, not a test.
4. Run it twice without `-Accept` to confirm it is deterministic (a clean
   test reports `0 pixels differ`).
5. `p4 add` the script and the `.expected.bmp` **as type `binary`**.

Comparison is exact by default; `-Tolerance N` allows N differing pixels for
an app with unavoidable jitter. A failing run leaves the actual frame in
`test-output/gui/` for inspection -- look at it before you re-record.

### Per-app batteries

Each application owns its GUI battery, in `apps/<app>/tests/`:

```powershell
build/test-app-gui.ps1 -App circuits             # run the battery
build/test-app-gui.ps1 -App circuits -Build      # compile the app first
build/test-app-gui.ps1 -App circuits -Only menu  # just the menu tests
build/test-app-gui.ps1 -App circuits -Accept     # (re)record every frame
```

App-BVT level: does it paint its chrome, do the menus open and have items in
them, does the status bar have content, does a click land, does a drag move
the thing it grabbed.

| App | Tests | Covers |
|-----|------:|--------|
| circuits | 15 (**15 pass**, measured 2026-07-28) | boot chrome; the four dropdowns and a menu item firing; the PCB / 3D / SIM tabs; hover properties; palette place; wire; marquee; drag; delete+undo |
| spark | 8 | boot chrome (toolbar, viewport, outliner, camera panel, status bar); add cube/sphere/plane; wireframe; grid; orbit; zoom; a multi-command scene |
| ~~guios~~ | ~~2~~ | **DELETED 2026-08-03 with their subject.** `desktop-chrome` and `dashboard` recorded the F3 and F1 views of `apps/guios/GuiShell.codex`, which is retired: GopDesk replaces it and the shell, its `build.ps1` and its `start.ps1` went in the same changelist. A golden of a program that no longer exists is not coverage, so the two `.uiscript` files and their goldens went with it rather than being left to fail. Both were good tests and their design is worth copying if the desktop ever wants one again -- `dashboard` in particular carried a measured 76x60 `mask` over the uptime and heap figures with an `expect-ink 350..900` inside it, so the masked rectangle was bounded rather than unchecked, and both fired their negative controls (drop `-rtc`, 285 pixels; drop the mask, 278). Note that GopDesk goldens are PARKED, above |

**`circuits/tab-sim` was red from 2026-07-13 and is green as of
2026-07-28. The golden was wrong, and the reading that diagnosed it was
measuring something else.**

The 2026-07-13 note recorded here said the SIM tab rendered an empty
frame, 241001 pixels differing. An empty frame is real but it is the
capture flake described below, not the failure: measured across eight
back-to-back runs at the same deadline, seven produced a bit-identical
frame differing from the golden by **1647 pixels** and one produced a
wholly empty frame differing by 311515. Retrying absorbs the blank, and
`tab-3d` hits the same one. The 241001 reading was a blank frame read as
the diagnosis, which left the real 1647-pixel disagreement undescribed
for fifteen days.

That disagreement was confined to two 3-pixel bands, y 180..182 and
y 460..462. `draw-sim-led-steps` alternates its 20 segments between
`gy + gh / 4` and `gy + gh - 20` on `i / 2 * 2 == i`; with gh 400 and gy
81 those are y 181 and y 461. The golden had LED1 as a solid line at 461
across the whole plot, which is the parity test never firing, and the
current frame alternates. `opening.codex` has not changed since the
golden was minted at CL 9766 apart from one em-dash at CL 11522, so what
moved is the compiler, and the current answer is the correct one: a
probe against the depot seed answers 1,0,1,0,1,0 for i = 0..5 and
181 / 461 / 181 for led-y. The VCC and NRST traces use no parity test
and were byte-identical throughout, which is the control. Re-minted;
the battery is now 15/15.

The battery is also **flaky by one test under parallel load**:
`menu-select-all` and `menu-file` each failed once across four full runs
and each passes 4/4 standalone. Until that is fixed, a single red test in
a batch run means "look again", not "regression" -- and the harness cannot
be a gate.

Spark (640x480, run with `-Width 640 -Height 480`) did not compile at all when
its battery was written -- see the Real-negation compiler bug below -- and once
it did, it was still invisible: it writes pixels STRAIGHT to the GOP framebuffer
instead of driving the GPU rasterizer, and codex-vm only synced its shadow
buffer for guests that announce a frame. Black window, empty screenshot,
frames=0, no error anywhere.

The first sweep of the circuits battery found three real bugs, none of which
the existing battery could see, because no GOP app was tested at all:

- `process-all` rebuilt the state with `as-tab = st.as-tab` (the OLD state),
  discarding `set-tab` every frame -- a tab click could never do anything.
- `draw-sim-legend` fed `gpu-rect`'s return value back as the next triangle
  index, writing the legend over the menubar and collapsing the frame count.
- codex-vm silently dropped every triangle past 16384. A frame is drawn back
  to front, so what vanished was whatever was drawn LAST: the dropdown menus,
  the properties panel, and the entire status bar. That one was in the VM and
  affected every GPU app.

## The QR Telemetry Channel, Decoder End (`build/qr-decode-test.ps1`)

`GopQr` paints findings as QR codes on the GOP framebuffer and
`tools/qr-read.ps1` turns a photograph back into the exact bytes. That pair
is R-1 of `TheSilentKeyboard.md` -- the output channel a hardware campaign
is not allowed to launch without. Since 2026-08-05 probes screenshot
themselves to the stick (F12), and the QR path is the fallback for boots
where the disk stack is itself under test.

Only the encoder had a test (`codex/test/qr-encode.codex`). **The decoder,
the half a sitting actually depends on, had nothing pointed at it, and it
was broken.** `Get-Otsu` kept the FIRST threshold achieving maximum
between-class variance. A screenshot is perfectly bimodal -- every sample is
0 or 255 and nothing lands between -- so every threshold in 0..254 splits it
identically and scores identically, and the answer was 0. The module test
`gray < 0` is never true, every module read light, and a pixel-perfect
capture decoded as **nothing**.

The inversion is what kept it hidden: a *blurry* photograph of the same
screen decoded fine, because noise and JPEG populate the valley and drag the
maximum somewhere sane. The only validation ever run was against simulated
hand-held photos, so the crisp direction -- which is what every screenshot
and every automated check produces -- was the one nobody could see. And on a
photograph the pre-fix decoder did not fail cleanly either: it returned a
**partial** report, five lines of nine, silently dropping the leading verdict
fields (`uk-ok`, `EPINT`, `code`) and starting mid-line.

So the test checks both directions off one fixture, and the crisp one is the
direction that regressed:

```powershell
pwsh build/qr-decode-test.ps1
```

1. `build/fixtures/qr-kbddiag.png` as rendered -- three version-5 codes
   cropped from a real `KbdDiagProbe` boot under codex-vm
2. the same fixture put through a deterministic simulated photograph
   (perspective skew, glare, defocus, sensor noise, JPEG q70, fixed seed)

Both must reassemble to the identical nine-line `KBDDIAG v8` report. With
the fix reverted, direction 1 decodes nothing and direction 2 returns five
lines: that is the control, and it fires.

## Test Lifecycle

### Adding a Test

1. Write `codex/test/foo.codex` (or `codex/test/apps/foo.codex`
   for integration tests).
2. Compile: `build/compile.ps1 -Src codex/test/foo.codex -Out foo.cdx`
3. Run: `tools/codex-vm.exe -kernel foo.cdx -headless -output foo.out`
4. Verify the output, then copy to `foo.expected`.
5. For disk tests, create `foo.disk` (blank or pre-populated).
6. `p4 add` all files and submit.

### Updating Expected Output

When a seed rebuild or codegen change shifts output (e.g., different
hash values, changed serialization lengths), update the `.expected`
file from the actual runtime output. The test harness strips `\r`
from expected files before comparing.

### Promoting a Slow Test

If a `.slow` test now compiles in under 5 seconds, delete the
`.slow` file. Fester marked 41 foreword tests "redundant" as `.slow`
in CL 2949; reek promoted them all to regular in CL 2984 -- they
run in 1-3 seconds each.

## Batch Compile Architecture

The test harness compiles tests in REPL batch mode: one VM per job
slot, compiling multiple tests sequentially over a persistent serial
connection. The compiler resets its heap between compilations
automatically (REPL loop in X86_64Chapter.codex).

This is fast for small tests (~2s each) but breaks down for tests
with large dependency chains (foreword + OS + trust modules). The
REPL VM accumulates foreword bytes across the session, and late-batch
tests may fail to compile simply because the batch budget is
exhausted. These tests compile fine with individual VMs.

Workaround: run `build/test.ps1 -Apps -Jobs 8` for more batch slots,
or compile stubborn tests individually via `build/compile.ps1`.

