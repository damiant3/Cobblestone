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
| `foo.expected` | Compile must succeed; runtime serial output must match. **Not byte-for-byte** -- see the section below the table |
| `foo.failing` | Compile must fail with the listed CDX error codes |
| `foo.diag` | Compile must succeed and emit each listed CDX code at any severity (warning/info/error). One code per line (bare number or `CDX`-prefixed). Combine with `foo.expected` to also check runtime output. This is how warnings and infos are regression-tested. |
| `foo.skip` | Skipped entirely; first line is the reason |
| `foo.slow` | Skipped unless `-Slow`; first line is the reason |
| `foo.fatal` | Skipped unless `-Fatal`; kills the VM at runtime |
| `foo.flags` | First line is appended to the test's **compile mode line**, so the test states its own compiler requirements. `prose` selects CPL; `passes=+name` adds an IR pass; `decks=N` scales every phase deck floor to N per cent, which is what a compilation unit larger than the floors were sized for needs (`codex/test/apps/foreword-all-compile` cites all 416 foreword chapters and carries `decks=200`, re-measured 2026-09-02 off the file and not carried forward; without it the compile is `CDX9002: Deck overflow in LOWER`, run both ways 2026-07-22). Read by `build/test-compile-batch.ps1` and, since 2026-09-02, by `build/bvt.ps1`, so it applies to the battery and the BVT but **not** to a hand-run `build/compile.ps1`, which takes the same settings as switches. **Until then the BVT could not have run a `.flags` subject at all**: it compiled every subject with bare switches, so a chapter needing `decks=200` failed COMPILE there while compiling fine in the battery, and the runner was measuring its own invocation rather than the test (L-SIDECAR). Nothing had noticed because no BVT subject carried a `.flags`; it surfaced the moment the gate's cited-test run phase pointed the same runner at `foreword-all-compile` |
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

### Reproducing one failure out of a battery run

**`test-output/_results/<name>` holds the VERDICT ONLY** -- a single line like
`FAIL_OUTPUT<tab><name>`. The actual output is not kept, so there is no diff
to read after the fact and no amount of staring at that directory will produce
one. Re-run the test to see what it said.

**Re-run it with the sidecar, not with the flags copied out of it:**

```powershell
pwsh build/test-run.ps1 -Kernel <compiled>.cdx -OutFile x.out -VmArgsFile codex/test/<name>.vmargs
```

`-VmArgsFile` applies the same parsing the battery does, comments and blank
lines included. Reading a `.vmargs` by hand and passing its contents to
`-VmArgs` feeds the `#` comment lines to codex-vm as arguments and produces
output unrelated to the test, which reads like a much more interesting failure
than the one being chased. Measured 2026-08-13, twice in one evening, by two
different agents.

**Compare the way the harness compares.** `test.ps1` reads the whole
`.expected`, strips CR, and tests `-eq` against the whole actual: nothing is
trimmed at either end. A hand check that joins lines and `.Trim()`s both sides
normalises away exactly the difference the runner is looking for, so it can
call a genuine failure green -- and, on the same evening, wrongly flag a
passing test as broken. **A check more forgiving than the runner is not a
check.** The trailing-newline case this comes from is in
`docs/Agents/PerforceProcess.md` trap 5b.

### Two sidecars are one byte short of what their subject prints, and three arms agree against them

Found 2026-09-01 by reek, grading the wasm plug against these sidecars. Not
fixed here: which side is wrong needs the bare-metal battery, which is not an
agent command.

**The measurement.** `apps/annotation-query-test` and `apps/diagnostic-boot`
each produce their sidecar's content plus exactly one trailing newline
(`startsWith` is true, 168 against 167 and 427 against 426) on THREE
independent arms: the wasm plug under wasmtime, the hosted x86-64 lift on
linux, and the same lift on windows. Both sidecars grade green on bare metal.

**The source agrees with the three arms, not with the sidecars.**
`annq-render` (`apps/works/AnnotationsQuery.codex:58`) appends `"\n"` per
entry and the subject's last statement is `print-line-uni (annq-render
warns)`, which adds its own. `diag-repl-loop`
(`codex/os/kernel/DiagnosticShell.codex:210`) is `if diag-is-exit cmd then
print-line-uni ""`, an empty line after `halting.`. Two newlines is what both
programs say.

**So either bare metal drops the final newline of a program's output, or the
two sidecars were captured or trimmed short.** Both are worth knowing and the
second is the cheaper to check. It is not the culture-sensitive comparison
documented in the next section: measured, `"abc\n\n" -eq "abc\n"` is False
both culture-sensitively and ordinally, so a trailing newline is not a
character the comparison can ignore.

**Why only two subjects show it.** A sidecar one byte short is invisible
unless the rest of the output matches exactly, so this is a floor on how many
sidecars carry it, never a count. Do not turn "two" into a census.

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

**The CR strip is not a courtesy to a few odd files: measured 2026-08-27, all
1,429 `.expected` under `codex/test` are CRLF and not one is LF.** So a
comparison done BY HAND rather than through the harness reports a phantom
mismatch on every test in the tree, not on a handful of older ones. Strip CR
from the sidecar side the way `test.ps1` does before reading any verdict off a
hand diff. This note replaced a private belief that four named desk sidecars
were CRLF exceptions; the control that killed it was reading three unrelated
`.expected` files, which are CRLF too (L-COUNT).

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

**The same rule broken the other way: a MISSING trailing newline** (fester,
2026-08-16). `test-run.ps1` writes `($lines -join "\n") + "\n"`, so the
actual always ends in exactly one LF; the comparison strips CR from the
expected side only. LF is NOT one of the ignorable characters above: with a
literal newline in the right operand, `"abc" -eq "abc<LF>"` is False, where
the SOH form on line 106 is True. So a sidecar with no final LF can never
match, whatever else is right about it.

**And that is the interesting part, because this failure is LOUD.** Three
sidecars shipped in CL 15313 anyway (`gpt-hdr-crc-guard`,
`gpt-array-crc-guard`, `gpt-array-geom-guard`) and sat unpassable until
root's release battery found them on 2026-08-16. A loud failure still needs
something to hear it, and the full battery is Damian's tool, so a test can
land and never be run at all. Do not read "it would have failed" as "it
would have been caught".

Measured across every `.expected` under `codex/test` and `apps`: **1,273
files, exactly those 3 without a final LF** (L-COUNT: re-measure, do not
quote this). That ratio is why it is worth a runner rather than a rule --
1,270 files already get it right, so a check fires on real defects and
never spuriously. Unlike the ordinal-comparison repair above, this one is
not a fleet-wide event: it is three files.

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
ruling it belongs on: the objection is to **putting harnesses in the standard
battery**, not to building
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

**It scanned column-2 PROSE as though it were code, and that is the false
green referred to elsewhere in this document.** The raise-site scan was a
`cdx-[a-z0-9-]+` regex over every line of every `.codex` under
`codex/compiler`, and it did not distinguish code from prose. `opening.codex`
carried a sentence reading *"held the compiler's ONLY raise of
`cdx-missing-cite`, so the code read as"* -- so **the sentence reporting that
the raise site had been deleted was counted as the raise site**, and it was the
only mention of that constant outside the catalogue. The row stayed green for
13 days over a diagnostic nothing could raise. Fixed main 12435: skip column-2
prose, and also scan host scripts for `error <N>:`, since `compile.ps1` and
`test-compile-batch.ps1` raise CDX3010 as a bare literal with no `CDX` prefix.

**The consequence for anyone deleting prose under CLAUDE.md rule 12: deleting
prose can turn a gate RED.** Not because prose reaches the binary -- it does
not -- but because a text-scanning HOST checker may be counting it. Expect that
class, and read the red as the check finally telling the truth rather than as
your deletion breaking something. The near-miss is worth the same warning: the
first instinct on seeing the row go dead is to delete the registry row, and
that would have deleted a live diagnostic. A survey is a claim bounded by the
pattern you gave it; three spellings were searched and the code used a fourth.

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

`codex/test/errors/` holds **206** expected-failure tests (measured
2026-09-02).

## What the standing gate does not cover

`build/build.ps1` is the gate, and five whole classes of change can pass it
while broken. None is a defect in the gate; all five are things it was never
pointed at.

**The ai foreword chapters and the tests behind them: no gate phase compiles
`codex/foreword/ai/`, and `codex/test/forewords/ai-*` run only in the battery.**
Measured at the Release 46 battery (2026-08-17): `FluxPipeline.codex` had
never cited `DiffusionPipeline` for `PipelineOutput`, CDX3008 (main 16241)
refused it, `ai-flux-pipeline` and `ai-exp-approximations` went red, and four
green gates including the release gate had run over that source. Fixed at
main 16550 (one cite line). The class is any foreword no compiler chapter
reaches: the gate proves the compiler unit and whatever the BVT and the
sweeps name, and a foreword outside those is proven by the battery alone.
After touching a foreword the compiler does not cite, compile and run its
tests yourself; the gate will not.

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
it green. Cost measured 2026-08-06: 191 s of a 517 s gate; re-measured
2026-08-20 at head 18157, 151.7 s of a 644.1 s gate.

**Since 2026-08-20 the standing gate sweeps a SAMPLE and the release gate
sweeps all of them** (Damian's call, with `-Internal` published as the gate
every agent runs). `build/build.ps1 -Internal` passes
`sweep-app-classes.ps1 -Sample 30`, which takes every 9th unit of the sorted
270 rather than the first 30, so the subset spreads across apps and is
deterministic: the same tree sweeps the same units. Measured 36.7 s against
135.9 s for the full sweep on the same seed.

All three arms were run before it was relied on. A sabotaged unit INSIDE the
sample turns the sampled sweep red and names it; the same sabotage OUTSIDE
the sample leaves the sampled sweep green at `29 clean, 1 known-dirty, 0
regressions`; and the full sweep catches that second one, red, naming
`apps\calendar\CalendarPage.codex`. **The middle arm is the gap and it is
real, not theoretical** -- a regression confined to units the stride skipped
reaches main and waits for the release gate. What makes the trade worth
taking is that a compiler regression usually moves a CLASS of construct and
so lands in many units at once; a single-unit regression is what this gives
up. `-Check` will not report a baseline unit as fixed on a sampled run, for
the same reason it will not on a filtered one.

**The sweep compiles ENTRY chapters, and that is the shape of its gap.** A
ported chapter with no `opening` and no citer is compiled by NOTHING in the
gate: not by the sweep, which never reaches it, and not by the fixed-point
phases, which only see what the compiler cites. It is not a silent failure, it
is a silent absence, so the app going green says nothing about it. After
porting a chapter, confirm something cites it or give it an entry point.

**The tests themselves. No gate phase compiles anything under `codex/test`,** so a test
chapter can stop compiling outright and every gate afterwards is green. Only
the battery and the BVT's own 75 reach any of it, and a test in neither is
compiled by nothing.

Measured 2026-08-20: `codex/test/lib/widget-tone` called `comp-draw-node` with
the arity it had before a clip parameter was added at main 17846, so it had not
compiled for four days, across a release. What makes this worse than a red test
is that a red test at least reports; this one is UNRUNNABLE and the usual
instruments are all silent about it. `check-sidecars` passes, because the
`.expected` is still there and still well formed. `check-doc-counts` passes,
because it is still a file. The battery would have said so and had not run.

**A SECOND INSTANCE, 2026-08-20, and its cause is not a signature change**
(root found it, blu's lane and blu's fix). `codex/test/cost/accumulator-corpus`
had not compiled since 08-16: `:84:9: error CDX3002: Undefined name: it`,
against the depot seed, exit 4. The chapter is one of five under
`codex/test/cost/**`, and `build/cost-corpus.ps1` is **invoked by nothing**, so
the whole directory sits outside every instrument exactly as this section
describes. Nothing changed underneath it; a prose continuation line began with
`and`, which is a reserved word (`Lexer.codex:315`), so an English sentence was
parsed as code. The trap is written up in `DevelopersGuide.md` beside the
`above` case.

Census taken at the fix, all five compiled against `seed/Codex.cdx`:
`accumulator-corpus` (was the only break, now exit 0), `builtin-alloc`,
`giveup-beats-fuel`, `literal-alloc`, `str-concat-inplace`. **Re-measure rather
than quoting that** (L-COUNT); a directory no runner touches is one edit away
from drifting again, and this entry is the second time this section has been
written from a live example rather than a worry.

So: **after changing the signature of anything a test can call, grep the test
tree for the name and compile every hit.** That is the whole of the discipline,
it costs one grep, and it is the check the caller of a changed signature is best
placed to run. Compiling only the tests you can think of is what produced this
one: the same change updated `gop-composite-stride` for the same arity and
missed the chapter one directory over.

### The discipline did not hold, and here is the census (root, 2026-08-20)

**That paragraph was written, published, and then the same file was missed
again by the next signature change.** `comp-draw-node` gained a `GopFont`
parameter when GopFont was threaded at main 18118; every production caller was
updated, `codex/test/lib/widget-tone` was not, and it was red on main until
18248. It is the SECOND time this exact chapter has been missed this way. A
discipline that has now failed twice at the same site is not a discipline, it
is an unenforced assertion (`LESSONS.md` preamble), which is what CurrentPlan
18222 exists to fix.

So the whole tree was swept once, to get the cost of a phase and the sites
rather than a total. **Re-measure before quoting any of this (L-COUNT).**

| | |
|---|---|
| chapters under `codex/test`, excluding `errors/` | **1,413** |
| `errors/`, already compiled by the gate's `check-errors` | 200 |
| sequential, one VM boot (`test-compile-batch.ps1`) | **1.00 s/unit** |
| 4-way parallel, whole tree | **1,202 s wall, 0.85 s/unit effective** |

**Do not extrapolate this from a sample, which is the mistake made on the way
to it.** The first 400 units in sorted order ran at 0.42 s/unit and predicted
about 10 minutes; the real figure is 20. The sorted head is `codex/test/apps`,
whose chapters are small, so the sample was not the population (L-DILUTE).

**The result: 1,399 clean, 14 dirty, and only ONE of the fourteen is a red.**

- **11 carry a `.failing` sidecar** and are supposed to be refused.
- **2 are the instrument measuring its own invocation** (L-SIDECAR):
  `quote-from-peer` and `quote-from-store` answer CDX3020 `Quoted work 'Sorted'
  was not supplied` to a bare compile, and that is CORRECT, because
  `build/quote-from-peer-test.ps1` stands a peer up and compiles them with
  `-Peer 127.0.0.1:<port>`. A compile-every-chapter phase that does not know
  this reports two permanent reds, and a red whose answer is always the
  expected one trains its reader to accept reds. **The phase needs a baseline
  naming units whose harness supplies an input**, the way
  `build/app-sweep-baseline.txt` already does for the app sweep.
- **1 is a genuine red**: `codex/test/cost/accumulator-corpus.codex:84:9`,
  CDX3002 `Undefined name: it`, on a line of column-2 prose. Reproduced
  outside the batch harness with a plain `compile.ps1`, exit 4. Its runner
  `build/cost-corpus.ps1` is **invoked by nothing** in the tree, and the
  chapter last moved 2026-08-16, so nothing could have noticed. That is a
  third mechanism for the same class: not a missed caller, but a corpus whose
  runner nobody calls.

**Two traps for whoever builds the phase.**

- **`test-compile-batch.ps1` keys its output directory by the filename STEM,
  so two chapters with the same basename in different directories collide and
  one is silently never measured.** `engine-culling` and `engine-texture` exist
  in both `codex/test/` and `codex/test/forewords/`; the sweep produced 1,412
  directories for 1,413 chapters and reported nothing wrong. Key by path. The
  four colliding files were compiled individually afterwards and all four are
  clean, which is how the census reconciles to 1,413. **The battery has the
  same collision and it went red on 2026-08-22**: `test.ps1` keys the output
  directory and the verdict by stem too, the pair had passed by the luck of
  which one compiled last, and size-balanced dealing flipped it (`engine-culling`
  `FAIL_OUTPUT`, 222 chars against an expected 18: the root test's output under
  the forewords test's name). The root pair, the calibrated ones red added
  2026-08-06 into already-taken names, are renamed `engine-culling-cost` and
  `engine-texture-cost`; no stem is duplicated under `codex/test` now, and
  keying by path in the harness is still the real fix.
- The cheap alternative to sweeping everything is to **compile only the tests
  that CITE what changed**, which is what the grep discipline above was
  reaching for. Measured over the same 1,413: 756 distinct chapters are cited,
  the fan-out is **median 2, mean 5.1, max 382** (`Foreword::Console`).
  `Works::GopComposite`, the chapter both incidents came from, has **11**
  citers costing **26.3 s**, and those 11 contain `widget-tone` AND exactly the
  six `gop-composite-*` chapters val fixed at 18220. So a cite-scoped phase
  would have caught both misses for 26 s.

### The phase, and what each of its exclusions promises

**RULED by red on those numbers, 2026-08-20: cite-scoped in `-Internal`, the
full sweep in the full gate only.** `build/check-test-compile.ps1` is the
runner and `test-compile` is the gate phase. It is never SKIPPED, only SCOPED,
which is why it is not in `build/build.ps1`'s `-Internal` phase map: with
`-Internal` it compiles the chapters that cite a chapter changed in that
workspace, and the full gate passes `-Full`.

It matches a changed chapter by its `Chapter:` NAME, read out of the file,
rather than by path, because that is how a test names it. The quire is
deliberately not matched, so citing the name alone is over-inclusive, which is
the safe direction for a check.

Three exclusions, and **each one is a promise not to cry wolf** (red: a check
that cries wolf gets ignored, and is then worse than nothing):

| excluded | why, and who already covers it |
|---|---|
| `codex/test/errors/**`, 200 | the gate's `check-errors` compiles all of them and requires a REFUSAL |
| a chapter with a `.failing` sidecar, 11 | the chapter declares it is expected to fail |
| `build/test-compile-baseline.txt`, 2 | the chapter's own runner supplies an input the compiler needs, so no bare compile can ever succeed |

The baseline holds `quote-from-peer` and `quote-from-store` and nothing else.
Both answer CDX3020 to a bare compile, correctly, because
`build/quote-from-peer-test.ps1` stands a peer up and compiles with
`-Peer 127.0.0.1:<port>`. **Add a line there only for a chapter that compiles
under its own runner and cannot compile without it, and name the runner.** A
chapter that is merely broken is a red and gets fixed.

**Four arms, run before it landed.** A `GopComposite` change selects exactly 11
chapters and passes in 12.4 s. Sabotaging one of those 11 turns it red and
names the chapter and the CDX code. A workspace with no `.codex` open selects
nothing and returns in under a second. And `-Only codex/test/quote-from-peer`
fails, which is what makes the baseline entry load-bearing rather than
decorative: the unit really cannot compile standalone.

**A GATE RUN AFTER YOU HAVE SUBMITTED SCOPES TO NOTHING, AND IT COMES BACK
GREEN.** The third arm above is stated as a virtue, and it is the same
mechanism as a trap nobody had written down. `-Internal` derives its whole
change set from `p4 opened` (`build/build.ps1:85`); a submit empties that, so
every trigger is false, every optional phase is skipped, and `test-compile`
selects zero citing chapters. The run prints `changed here: nothing opened` and
`core + BVT always, plus: (nothing implicated)`, proves the seed is a
byte-identical fixed point that boots, and **re-tests none of your work**. It
is a true answer to a different question. This bites whenever a merge brings
work in after your submit, and NOT only when that work is a seed. Measured the
same day: a merge-down submitted before the gate carried fourteen new plug
chapters from another lane, `p4 opened` was therefore empty, `tPlugs` was
false, and `plug-binary`, `cross-smoke` and `plug-smoke` were all skipped. The
run went green in 91 seconds having compiled no plug at all; the same tree
gated with the files still open took 337.8 s and ran all three. A CodexType
variant that made five plug emitters non-exhaustive reached main through that
gap and was caught by another lane. Whatever class the merge brought, re-run
the affected tests BY HAND -- the gate cannot see them any more. Read the
THREE `[internal gate]` lines on every run rather than the exit code
(`build/build.ps1:109-111`, three unconditional prints in the `if ($Internal)`
block). The third is the one to read: it names the deferred phases outright,
`deferred to the next full gate: <phases>`, so what is missing does not have
to be inferred from the second (val, 2026-08-27; widened by fester the same
day; corrected from "two" by reek and blu, verified at source, 2026-08-27).

**It refuses a silent absence rather than reading it as a pass.** If the number
of results does not equal the number of chapters submitted, the phase fails
saying so. That is not hypothetical: `test-compile-batch.ps1` keys its output
directory by filename STEM, so the census produced 1,412 directories for 1,413
chapters and reported nothing wrong. The runner splits colliding basenames into
separate batches so every chapter gets its own directory, and then asserts the
count.

**And when you write that loop, do not pipe `compile.ps1` into
`Select-Object -First N`.** `-First` terminates the upstream pipeline once it
has what it asked for, which ABORTS the compile partway. Measured 2026-08-20:
a five-test loop written that way reported `COMPILE-FAIL` on all five, with no
`.cdx` and no `.log` written for any of them, an hour after the fleet had
converged a seed. Every one of the five passes; the loop killed its own
subject. The tell is that a single explicit re-run of any one of them succeeds
immediately, and that the failure is uniform across unrelated tests.

Use `*> $null`, or `| Out-Null`, or `Select-Object -Last N`, none of which
terminate the producer. This is the same family as P-DIFFC in
`PerforceProcess.md`, which records the other direction: a `2>&1 |
Select-String` filter eating a command's own error so a failed command reports
as a clean result. **A PowerShell pipeline is not a passive observer of the
command in front of it**, and in this tree both directions of that have now
produced a wrong published result.

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
see `docs/Designs/Done/Compiler/ProportionalDecks.md` for one worked
example, including the probe and the reason its result was negative.

## Standing bed facts (each cost a session; do not relearn)

- **A `-RedirectStandardError` file on D: costs ~7.5 ms per stderr line; keep
  harness captures on the system temp** (red, 2026-08-22). The same eight
  `codex-vm -run-list` supervisors over the BVT's 60 run tests took 2.6 s with
  the redirect file under `%TEMP%` on C: and 12.3 to 12.7 s with it anywhere
  on D:, inside the repo or not; the children's own per-kernel ms were
  identical in both, so nothing in the guest or the hypervisor is involved.
  PowerShell's redirect is one file write per line and D: pays for each.
  `test-run.ps1` and `test-compile-batch.ps1` never saw it because they
  redirect to `GetTempFileName()`, which lands on C:; the first `-run-list`
  wiring of `bvt.ps1` put the file in `test-output\_bvt-runs` and its run
  phase went 7.5 s to 12.4 s, worse than the `pwsh`-per-test path it
  replaced. Before blaming a harness change for a slowdown, move its capture
  file to C: and re-time. Both batch harnesses now capture on the system temp
  and `Move-Item` the file into `_runs` for the record.

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
- **`print-line-raw` emits CCE; `print-line` (and `print-line-uni`) convert
  at the I/O boundary** (main 14809, 2026-08-13; before it `print-line` was
  the raw alias, and this bullet said so). A hand-written probe using
  `print-line-raw` renders as garbage and looks exactly like a miscompile. The control that settles it is any
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

- **A null container pointer READS AS ZERO in the bed, so a row over a
  container builtin can pass on a representation that is not a container at
  all.** Address 0 is mapped and holds zero in codex-vm, so a load from a null
  pointer does not fault, it answers 0. Measured 2026-08-25 calibrating the
  `ec-empty` guard: with the empty vector emitted as a literal 0 instead of a
  counted heap block, both `vec-length` rows still answered 0 and PASSED,
  because a length read is a load from offset 0. Only a row that DEREFERENCES
  the container failed, at `CR2=fffffffffffffff8`, the count cell one word
  below null. A row that reads a field whose correct value happens to be zero
  cannot fail, and it looks exactly like a row that passed for the right
  reason. Make the row CONSUME the container, or it discriminates nothing.

## Battery architecture

99 individual tests are consolidated into smoke bundles (`unit-smoke`,
`rt-smoke`, `try-smoke`, `prose-smoke`, `linear-smoke`, `linear-errors`,
`mutable-smoke`, `typeclass-smoke`, `handler-smoke`, `record-smoke`,
`lang-smoke`, `bs3-smoke`, `punctual-smoke`). Each exercises several
features in a single VM boot, cutting battery time ~60%.

**BVT mode is what `build/build.ps1` runs by default**: **73 tests**
(measured 2026-07-29). The list
and the reason for each entry are in `build/bvt.ps1` itself, which is the
register -- count it there rather than quoting a number from here. That is
the standing gate. The full battery (`build/test.ps1`) is Damian's tool and
is not an agent command.

**Do not add tests to `build/build.ps1` or `build/test.ps1`.** Damian's
2026-07-27 ruling in its general form: *"the standard tests are plenty
enough... the cost of discovery and fixup is lower than the cost of
continuous maintenance."* Gate time is the scarcest shared resource. A
standing test is paid by every agent on every run forever; a rotted one costs
a single repair on the day somebody needs it, and that trade is why the
coverage gate above was declined too.

Three consequences worth stating, because each has been argued the wrong way:

- **An orphan test that nothing runs is a deliberate tradeoff, not a
  defect.** Do not adopt one into a harness to "fix" it.
- **Anything phrased as "X runs under no harness" is a question for Damian,
  not a work item**, and the default answer is no.
- **A CDX verifier is not coverage machinery** -- always-on, microseconds,
  halts the build on a wrong byte -- and stays in scope. The rule is about
  tests that cost gate time, not about structural checks on an artifact.

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

### `test-cross.ps1` beside itself: the collision, the measurement, and the fix

**It is safe to run beside itself now, and it was not before 2026-09-01.** The
single-test script invoked `compile-<arch>.ps1` with no `-WorkDir`, so every
concurrent instance shared one `build-output/last-compile.ir`, one
`last-compile.<arch>.bin` and one `compile-ir.log`, and the slots overwrote
each other's IR: **the ELF you graded could be built from another test's
program.** `test-cross-batch.ps1` was parallel-safe only because it hands each
slot its own `-WorkDir` (`test-cross-batch.ps1:167`), which is the mechanism
and not a style difference. The single-test script now computes `$testOutDir`
before it compiles and passes it the same way, which is the whole fix.

**Measured before the fix (fester, 2026-09-01) over 77 named subjects, the same
plug and the same harness on both arms, `-Jobs 4` against `-Jobs 1` as the only
variable: 3 of 77 verdicts moved.** `engine-shadow` failed with `compile-ir.log`
"being used by another process", and `ttt-perfect` and `vec-nested-binop`
reported `[UNSUPPORTED] char-encode` and `raw-bytes-to-text` refusals belonging
to OTHER subjects' programs. All three read as `FAIL (compile)` under four slots
and `FAIL (output mismatch)` run alone.

**Do not read that 3 as a small blast radius, because of what the corpus could
not express.** All 77 subjects were already red, so the arm can show a failure
changing CLASS and is structurally incapable of showing a green turning red or a
red turning green -- the two outcomes that would actually mislead somebody
(L-GAP). What it does establish is that the corruption is QUIET: two of the three
surfaced as a plausible builtin refusal, which on a cross lane is an ordinary
result nobody would chase. A run that looks clean is not evidence the collision
did not happen.

**The hazard was LATENT rather than active while it stood**, because the only
caller in the tree, `check-cross-smoke.ps1`, runs its arches and tests strictly
serially. What made it worth fixing rather than noting is that the two scripts
are listed as peers in the block above, and reaching for parallelism on the
single-test one is the obvious move for anyone wanting a named subset the
`-Filter` substring cannot express -- which is exactly what plugs 1.3's two-arm
subset needed.

`test-cross.ps1` is GENERATED, so the change is in `codex/build/testcrossScript.codex`
and the shipped script matched to it by hand; `check-generated-scripts.ps1 -Only
test-cross` reports 0 drift. **There is no `-Write` flag on that checker and the
omission is load-bearing** -- measured 2026-08-03, 39 of 40 generators had
drifted and the SHIPPED script was the maintained side every time, so a bulk
regenerate would destroy working scripts.

**The cross phase was NOT run against this change, and the rerun is owed.** What
was run is `test-cross.ps1 -Arch riscv64 -Test rv-frameless-temp` through the
changed path (compile OK, run PASS, exit 0) and `check-generated-scripts -Only
test-cross` at 0 drift, which is the only gate check that reads this script.
`build/build.ps1 -Internal` was not run: Renode is banned on this box from
2026-09-01, the box being one DIMM down and a riscv arm peaking 2.0 GB per boot.
Rerun the cross phase when the ban lifts.

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

### The cross bed compiles with the SEED, and did not until 2026-08-20

`compile-arm64.ps1` and `compile-riscv.ps1` pass `-Kernel seed/Codex.cdx` to
`compile.ps1` now. Before that they passed nothing, so Phase 1 used
`compile.ps1`'s default of `build-output/bare-metal/Codex.cdx`, which holds
whatever the last `build.ps1` left there. **A cross-lane result was therefore a
property of the workspace, not of the depot**, and two agents measuring the
same source could disagree without either being wrong: it is the same
uncontrolled default that made every plug binary a workspace property until
main 17516.

It has already cost two investigations. It voided val's typescript red on
2026-08-19 (a three-day-old kernel), and it is the whole of `plugs` 1.49, where
a 2.1 s baseline and a 92.8 s failure were compared across workspaces holding
different compilers and read as a regression. Nothing in a `history/` file
records which kernel produced it, and `test-output-cross/` is p4-ignored, so
the baseline never leaves the workspace that wrote it -- **do not compare a
cross run against a history file another workspace produced.**

Pass `-Kernel <path>` to measure a compiler that is not the seed, and say so in
the CL. Both directions are measured: with a stale kernel still in
`build-output` the default reports the seed's digest, and an explicit
`-Kernel` reports the one you named.

### What is genuinely unimplemented on the cross lanes

Distinct from untested. Both plugs refuse rather than miscompile, and each
refusal is pinned by a `.cross-refusal` sidecar (23 of them), so a refusal
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

**And there is a THIRD case, one test wide, that neither bucket describes:
a PORTABLE subject refused because the lane has no instrument for it.**
`heap-bracket-shape` asks where `__heap-save` belongs relative to an
effectful bind, which is a property of the allocator and has nothing to do
with x86. It reaches `port-in-byte` only because the CMOS RTC is a
convenient effectful Integer no device has to be present for and the
compiler cannot fold. Neither cross lane has any substitute -- no clock, no
tick, no counter, no random (measured 2026-08-20) -- so it carries a
`.cross-refusal` like the architectural three and the allocator-bracket
question goes unasked on ARM64 and RISC-V. Counting it as architectural
would be the mistake this section exists to prevent, one bucket over: it is
a gap, it is just a gap in the BED rather than in the compiler. The sidecar
says so in full and names its own deletion condition.

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

**RE-RULED 2026-08-27 by Damian: `-Jobs 4`, superseding the 8.** Not the old
workaround returning: a different condition, measured, and NAMED here so this
default dies with its condition rather than outliving it, which is this
section's own lesson. The box holds 15.8 GiB; `check-test-compile` at 4 ways
times `-mem 3072` commits ~12 GB and free host RAM fell 8.5 GB to 1.9 GB with
four VMs live, so 8 slots of 3072 MB guests cannot fit. The tell that it is
RAM and not codegen: the named casualty MOVES between runs (three runs, three
different chapters, 326/1076/340 casualties, one workspace, one kernel, each
chapter clean alone). The account with the control matrix is
`OperatorsManual.md` "The compile batch asks for 12 GB of guest RAM, and a
short box reports it as a CODEGEN failure" (blu, main 20370). When the box
grows RAM, re-measure and re-raise.

### A harness that can throw is a harness that can take itself down, and the fix was already in the file

`test-cross-batch.ps1` read its `uart.log` with a bare
`[System.IO.File]::ReadAllText`, no share mode, no retry, behind nothing but
a 50 ms sleep. QEMU owns that file through `-serial file:` and `Kill()`
returns before Windows releases the handle, so the read can land on a locked
file. With `$ErrorActionPreference = 'Stop'` at the top of the script, that
throw does not fail one test.

Measured twice on 2026-08-18, on both cross lanes. The riscv64 run died on
its **first** test with a `MethodInvocationException` on `range-let-carry`'s
`uart.log`: 457 tests lost, exit 1, no results file. The arm64 run died the
same way at `unit-show`, 453 tests lost.

Then the second half of the cost, which is the one to remember. **QEMU does
not exit when its guest finishes**, and the ceiling that kills it lives in
the harness that just died. Both outages stranded their whole slot width.
The riscv64 one left six guests, two at 5,300 CPU-seconds when they were
found. The arm64 one was worse only because nobody looked for six hours: its
five guests were still running at **23,100 CPU-seconds each, 115,597 in
total**, and they had been saturating five cores of a shared build box for
the whole afternoon. Every agent's build on this machine was paying for it.

Which is the part that generalises past this bug: **an orphaned guest is
silent**. Nothing reports it, no gate observes it, and the harness whose job
was to kill it is the thing that died. If a cross run ends badly, look at
the process list before doing anything else.

**The fix was already in the file, one scope over.** `$compileBlock` has a
`Read-LogShared` helper -- share-tolerant open, five retries, empty on
failure -- with a comment explaining that `WaitForExit` returns before the
redirected handle is released. `$runBlock` was written later, hit the
identical hazard, and got the naive read. A helper in a sibling scope is not
reachable and, more to the point, was not looked for.

Fixed by giving `$runBlock` the same helper, waiting on the process after
`Kill()` so the handle is normally closed before the read, and routing an
unreadable log to `FAIL_RUNTIME 'no uart output'`, which the serial retry
pass already re-runs. It fails toward a re-run rather than toward a content
verdict.

Verified as an A/B against a deliberately locked file rather than by hoping
the race recurred, since the instrument has to be able to show the opposite:

| holder's share mode | old read | new read |
|---|---|---|
| `Read` (what a `-serial file:` writer allows) | **threw** | returned the content |
| `None` | **threw** | empty, so the retry pass takes it |

The general shape, and it is not about file handles: **any uncaught throw
inside a `ForEach-Object -Parallel` block is a battery-wide outage, not a
test failure**, and the blast radius is bigger than the run because the
harness is also the thing enforcing every guest's ceiling. Every I/O call in
a run block belongs inside a `try`, and a hazard fixed in one of two sites
is not fixed.

**The class is CLOSED across all three harnesses, 2026-08-20 (fester).** Seven
scripts under `build/` drive a `ForEach-Object -Parallel` block. `bvt.ps1` and
`check-errors.ps1` read their redirected logs with `-ErrorAction
SilentlyContinue` and were already safe by that. The two that were not now
carry the same `Read-LogShared` helper as `test-cross-batch.ps1`:
`sweep-app-classes.ps1` on its redirected stdout and stderr, and `test.ps1` on
`runtime.actual` twice and on the `.expected` beside it.

**`test.ps1` is GENERATED, so the fix went into `codex/build/testScript.codex`
and the script was regenerated from it.** A hand edit there is discarded by the
next regeneration and reads as drift until then. The drift was measured before
anything was touched, because the on-demand contract warns it can run the other
way and a regeneration can hand back a script that has lost hand edits: `test`
matched its generator at 0 drift over 1033 lines beforehand, so the generator
was the source of truth and regenerating was safe. Afterwards it matches again
at 1051 lines, 0 drift, which is the proof that the shipped script is what the
generator says.

The battery itself was NOT run to verify this and must not be, being Damian's
to run. What stands behind the change instead: the emitted script parses, the
helper is defined at line 726 and first used at 756 so the ordering is right,
and the helper was A/B'd against a deliberately locked file exactly as the
original fix was, reproducing the same table. A bare `ReadAllText` threw under
both share modes; `Read-LogShared` returned the content under `Read` and empty
under `None`.

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

**Contention corrupts a DURATION as readily as a verdict, and a duration is
quoted forward.** The section above is about a wrong pass or fail. This is
the same cause producing a number that everyone then plans around. Measured
2026-08-16: a release-day gate reported `cross-smoke` at **11,601 s**, and a
build-token request was made warning of a 3.5 hour hold on the strength of
it. It did not reproduce. The next gate ran 606 s end to end with
`cross-smoke` at **16.8 s**, which is a factor of 690, so the sample was
shared-box contention and not a regression.

**Never quote a timing taken on a contended box, and never budget a token
hold from one sample.** A phase that suddenly costs hundreds of times its
usual is contention until a second run on an idle box says otherwise, the
same rule the paragraph above applies to a verdict. The cost of getting it
wrong is not a wrong number; it is a build token held out of the fleet's
queue for an afternoon that the work never needed.

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
  and the row reads `still silent alone`. The injection was removed.
- **A wrong answer is untouched**: a valid program with a deliberately wrong
  `.expected` stays `FAIL_OUTPUT` with `run 0/0 retried`.

`-CompileTimeoutSec` exists to make the first of those provocable on demand;
it is also why the budget is no longer a literal buried in the parallel block.

### `check-errors` has none of that, and reports the silence as a code mismatch

**The refusal harness does not retry, and its summary line names a cause it
has not measured.** Measured 2026-08-21, twice in one session on one box, with
no change to the tree between the red and the green:

| run | failing tests | shape |
|---|---|---|
| first | 6, `pow-on-real` through `proof-launder-lemma-mutual` | contiguous, alphabetical |
| second | 5, `list-literal-too-large` through `list-prim2-false` | contiguous, alphabetical, disjoint from the first |

Both cleared on an immediate re-run: `refusals ok (200 refused as declared)`.
The per-test logs say what actually happened -- two of the six and two of the
five are **5 bytes**, and the rest open with `FAIL: no output` -- and one was
itself truncated mid-line. A contiguous alphabetical block is one parallel
slot's batch, so the failure is the slot dying, which is the same
silence-under-load this section already calls the machine's fault everywhere
else.

**The summary line is what makes it expensive**, because it prints the
declared code in the actual's place: `pow-on-real -- refused, but not with
9011 (declared: 9011)`. Read literally that says a diagnostic moved to a
different code, which is a compiler regression and is the reddest thing this
gate can report. What it means is "did not produce the declared code", and the
reason is one level down in a log the line does not mention. **A run that
captured nothing and a diagnostic that genuinely moved are the same glyph
there** (L-SHORT, and the same shape as the `FAIL_COMPILE` misattribution that
cost three sessions above).

Two separate repairs, and neither is done: the retry this section describes
does not exist for `check-errors`, and the message needs the actual code or
`no output` rather than an echo of the declared one. Reek has the message half
(`check-errors.ps1` is generated from `codex/build/`, so it is a generator
change). Until then: **check the per-test log's SIZE before believing a
refusal red**, and re-run once.

**`still silent alone` does NOT mean broken. The retry re-runs at the SAME
ceiling, so a test that merely needs longer than the ceiling cannot pass it
however many times it is retried.** The row is therefore two different
findings wearing one sentence, and the doc used to say no test in the tree
produced the class at all. Four do. Measured 2026-08-18 (blu), arm64:

| test | at `-RenoTimeout 10` | at `-RenoTimeout 120` |
|---|---|---|
| `ecdsa-p384` | FAIL_RUNTIME, `no uart output, still silent alone` | PASS, 103.4s |
| `ttt-perfect` | FAIL_RUNTIME, `no uart output, still silent alone` | PASS, 15.5s |
| `ecdsa-cert` | FAIL_STARVED, incomplete at 90s | not re-measured |
| `tls-cv-schemes` | FAIL_STARVED, incomplete at 90s | not re-measured |

The same `ecdsa-p384` ran in **12.9s** on 2026-08-17 and **103.4s** the next
day on the same source: identical kernel digest `12B07296419847B2`, foreword
and test source unchanged, and all three arm64 plug CLs in the window ruled
out by configurations that still failed. The variable is concurrent fleet
load, not the tree.

**So raise the ceiling before you bisect.** These four read exactly like a
regression against a previous run's per-test diff, and a bisect over them
costs a plug rebuild per candidate and finds nothing, because nothing in the
depot turned them. One `-RenoTimeout 120` run separates the classes in the
time a single bisect step would take. `FAIL_STARVED` already says "incomplete
at ceiling" and is honest; `FAIL_RUNTIME` with `no uart output` is the one
that lies, because a run cut off before its first line looks the same as one
that never had a first line.

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

This is not a defect list. It is coverage that was never written. It is the
single largest gap in what this document is supposed to measure, and it is
tracked nowhere but here.

### Legitimate Skips (cannot run headlessly)

| Test | Reason |
|------|--------|
| vmx-launch-test, vmx-serial-test | VMX requires CPL 0 + VT-x; no nested VMX under WHPX. They reach `vmwrite` / `vmlaunch`, which genuinely need VMX operation |
| vga-terminal-demo | Requires display + keyboard (`run-vga-demo.ps1`). Compiled 2026-07-19: zero errors of any code, 41 CDX4010 infos and 5 CDX3005 warnings |
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

`linalg-test` and `probability-test` are deliberately not in this table.
`linalg-test` matches its `.expected` and `mat-mul` does not fault, measured
2026-07-27 by compiling and running it; `probability-test` is fixed.

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

### The self-compile warning register

**Ruling 14 (Damian, 2026-08-18): warnings are warnings. They do NOT gate the
build; they are AUDITED AT THE RELEASE GATE.** This section is that audit's
subject: one entry per warning code the compiler's own self-compile emits, with
its ruling recorded as each is made. **No ruling is owed on any of them today.**

**The log is retained as of this change and was not before.** `Invoke-BuildCdx`
wrote the self-compile diagnostics to a temp file, kept them ONLY on failure,
and deleted them on success, so on every green build the single copy was
destroyed. It now persists at `build/output/<artifact>.diag.log`, one per
artifact (`Sut`, `stage1`, and `stage2` when a second pass runs), so the two
can be diffed. Both the generator `codex/build/BuildScript.codex` and the
shipped `build/build.ps1` carry the change: the shipped script is the
maintained side (`check-generated-scripts.ps1` has no `-Write` flag and that
omission is load-bearing), so a change to one without the other is drift.

**What it cost to have no log.** `CDX2064` fired correctly on
`X86_64Boot.codex:3216` on every build for as long as that defect existed, and
reached nobody: the warning was right, the analysis was right, and the only
copy was deleted seconds later. That is GitHub issue 70, and it took real
hardware and an outside contributor to find what our own compiler had been
saying all along.

**Measured 2026-08-18 from `build/output/Sut.diag.log`, seed `CAE56FBC`.
Re-measure rather than quoting this table (L-COUNT): it moved twice in one day
as fixes landed.**

| code | severity | count | what it says | ruling |
|---|---|---|---|---|
| `CDX6020` | warning | 13 | `mutation in constructor`: a constructor field contains a `__record-set`, so other fields of the same constructor reading that record see the mutated value | none owed |
| `CDX3005` | warning | 7 | `shadows builtin`: a definition has the same name as a builtin, and which one a call site gets depends on resolution order | none owed |
| `CDX4010` | info | 980 | `bounds proven`: a runtime check was elided | none owed |
| `CDX2053` | info | 6 | narrowing proven | none owed |
| `CDX4030` | info | 1 | the pass pipeline in force | none owed |
| `CDX2064` | warning | **0** | stale read after in-place update. **It was 1** until the issue-70 fix; its absence here is the evidence the log reports what it should | none owed |

**The register is not a to-do list and the counts are not defects.** Several of
the `CDX6020` sites are benign because the field read is an argument evaluated
BEFORE the mutation applies. At least one is worth a look and has not had one:
`state = __record-set st1 "load-local-toggle" (st.load-local-toggle + 1)` reads
`st` while mutating `st1`. **None of the 13 has been judged**, and judging them
is per-site work, not a sweep.

**INFO IS NOT WARNING OR ERROR, and the log is split on that line** (Damian,
2026-08-18). Retention alone would not have been enough: 987 of the 1009 lines
are infos, so a reader hunting the one warning that mattered was hunting it in
97 per cent noise, which is the second half of why `CDX2064` reached nobody.

| file | holds | lines here |
|---|---|---|
| `<artifact>.diag.log` | warnings, errors, and any non-diagnostic context | **20 warnings** (22 lines, 2 blank) |
| `<artifact>.info.log` | `info`, `hint`, `deprecated` | 987 |

The two are LOSSLESS between them: the split routes lines, it does not suppress
them, and `CDX4010` is the compiler reporting a SUCCESS (a bounds check it
proved unnecessary and elided) rather than anything to act on. The audit surface
is now twenty lines that can be read at a glance, and an EMPTY `diag.log` is a
meaningful green.

**Two traps in the filter, both met and both worth not re-paying.** The pattern
must not require a leading space: `info CDX4030: PIPELINE ...` starts at column
0 and leaked into the diag log on the first attempt, found by reading the
emitted file rather than by trusting the split. And the pattern must avoid the
backslash-s class entirely, because a Codex string literal escapes a backslash
as two, so a generator written that way emits a bare `s` and the generated
script silently disagrees with the shipped one. It is `(^| )` for that reason.

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

### The cross runners cut the actual output down to the `.expected` line count

`build/test-cross.ps1` and `build/test-cross-disk.ps1` both truncate the
captured UART to as many lines as the `.expected` holds before comparing
(`test-cross-disk.ps1:157`). A run that prints everything the sidecar asks for
and then hangs therefore compares EQUAL: the lines that would have shown it
still running are discarded, so a hang and a completed run read identically.

**When you are probing for a hang, write the `.expected` LONGER than the output
you expect.** The surplus lines make the truncation inert, so a run that stops
early is a mismatch instead of a pass. Measured during plugs 1.38, where a
spawned child faulted forever and the arm read green.

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

**Those errors are gone, measured 2026-07-20 (val).** `WebServer.codex` cited
by a real program compiles with **zero** errors and emits a binary, and so do
`IdeaServer`, `ExplorerServer` and `Prism`. The account above is kept because
it is why this harness exists.

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

**The same conversation over the Intel model, by name (root, 2026-08-17,
CurrentPlan B4 step 2).** `-Card e1000` boots the same server with `-e1000-nat`
(the NAT wire moved to the e1000 model, `OperatorsManual.md` "The NAT is one
wire"); `-Card ne2k` is the default and every historical run. Measured on
seed `D354208C631FDDA7`: all eight checks pass on both cards, ne2k in 24 s
and e1000 in 19 s of wall clock, so the repository protocol is served over
both branches of the bed by one script rather than by an ad-hoc `-VmArgs`
remembered from 2026-07-30 (`DeviceEmulationCatalog.md`, "wired to the
NAT"). `registry-locate-test.ps1` takes the same switch for its three guests,
and passes 9/9 on both (ne2k 162 s, e1000 145 s) since the same day. It did
not at first, and the account is worth one paragraph: `cdx-registry` and
`cdx-announce` never called `net-driver-bring-up`, so under `-e1000-nat` they
stayed on the silenced NE2000; and once they did bring the driver up, the
registry on ne2k accepted every connection and read no message, because its
`recv-give-up` was the POLL COUNT 40,000,000 handed to `net-io-recv-loop` as a
starting point, tuned to NetIO's uncalibrated cap of 50,000,000 polls. The
calibrated interval on the NE2000 is 6,000 polls per tick (the guest prints
it now), the cap `interval * 500` is 3,000,000, and 40,000,000 was past it
before the first poll. It is the poll-count-as-duration class `LESSONS.md`
names for `e1000-aneg-fuel`, from the other side of the same interval, and
the e1000 never showed it because there the interval is 2,281,000. The
give-up is 100 ticks now, the same ten seconds either way. Read the green as `DeviceEmulationCatalog.md` says to: evidence about the
stack over a descriptor ring, not about the card; the card's own answer is
B3's flight and Damian's sitting.

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

NaN, infinity and negative zero are covered, and no literal is needed for
them: `bits-to-real` builds any of them exactly from its bit pattern.
Three axes were added, 376 cases became **1037**,
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

## The Receive Checksum (`codex/test/tcp-checksum-refuse`)

Until 2026-08-15 nothing in `codex/os/net` verified a checksum on the way IN.
`tcp-with-checksum` and `ip-checksum` were build-only, so a corrupted segment
reached the parser unchallenged, and that is why the PR 64 DMA truncation --
one byte of the previous frame substituted into every odd-length receive --
was silent rather than diagnosed. Two independent defects, and the missing
checker is why the first was invisible.

**The arm exists because a checker that accepts everything and a checker that
works are indistinguishable on well-formed traffic.** It flips one bit of one
payload byte AFTER `tcp-with-checksum` has run, so the stored field is correct
for the bytes the sender had and wrong for the bytes the receiver gets, and
requires the segment refused. The control is the identical frame with the flip
omitted and requires it delivered. A third arm corrupts the IP source address
instead, a fourth truncates the frame, and two more call `tcp-checksum-valid`
directly on a properly built segment and on a zero-checksum one.

**The truncation arm found a crash, and it is the half a checksum cannot
cover.** The checksum is computed over the bytes that ARRIVED, and on a
DMA-truncated frame the IP header is one of them: it validates, while
`total-length` still claims bytes that are not there, and `ip-payload` walks
past the end of the list. Measured 2026-08-15 on a frame cut by six bytes,
before `ip-length-valid` existed:

```
cut-ip-header-valid=True
cut-claimed-total=58
cut-actual-ip-bytes=52
about-to-process
!EXC=06 RIP=000000000010b739 ... R12=0000000000000034 R13=000000000000003a
```

`R12` is 52 and `R13` is 58, the two numbers that disagree. It never reached
`survived`. That is a remotely reachable guest crash on the receive path, it is
exactly the PR 64 truncation shape, and it was reachable before this work as
well -- the checksum check did not introduce it and could not have caught it.
`ip-length-valid` is a separate predicate from `ip-header-valid` so the two
report as `bad-ip-length` and `bad-ip-checksum` rather than as one verdict:
truncation and corruption send an operator to different places. Claimed length
UNDER actual is normal and passes, because Ethernet pads every frame to 60
bytes, which is why the live tests are unaffected.

**The ablation is the evidence, not the green run.** Measured 2026-08-15 with
the guard in `net-process-ip` replaced by `if False`:

| line | with the check | ablated |
|---|---|---|
| `dirty-verdict` | `bad-tcp-checksum` | `received 18 bytes` |
| `dirty-bytes` | 0 | 18 |
| `dirty-emitted` | 0 | 1 |

The ablated run DELIVERS the corrupted 18 bytes to the caller, which is the PR
64 path itself rather than an analogue of it.

Every control line is unmoved across the ablation, which is what says the three
that move are measuring the check and not the harness.

**Two traps this arm was written around, both of which cost a revision.** The
outbox count is taken from a session whose handshake SYN-ACK has been cleared
(`set-outbox established []`); against an uncleared session both arms read 1
and the line discriminates nothing. And the refusal is a silent drop rather
than a dup-ACK, per RFC 1122 4.2.3.2, so `dirty-emitted=0` is the assertion
that no ACK quotes untrusted header fields back to the peer.

**Eight existing test files had to be repaired, and that is the finding.**
`tcp-reliability`, `tcp-seqwrap`, `net-io-clock`, `web-server-test` and the
four `web-mux-*` tests all built inbound frames with `tcp-build-segment`, which
leaves bytes 16..19 zero, and never called `tcp-with-checksum`. A real peer
always computes the field and no fixture in the tree did. Each now computes it,
and **every one of the eight reproduces its existing `.expected` byte for
byte**, so the repair is faithful rather than an accommodation.

The live path is covered separately and was not disturbed: `cdx-serve-test`
and `nat-conn-churn-test` both pass with the check active, 80 of 80
connections answered, because `codex-vm`'s NAT computes a correct
pseudo-header TCP checksum (`tools/codex-vm.c`, `nat_build_tcp_frame`).

## The UDP Frame Guard (`codex/test/udp-frame-guard`)

The same truncation class as the TCP path above, found by asking the same
question of UDP, and **worse in two ways that make it the more serious of the
two**. `udp-io-mine` runs on EVERY frame the poll loop takes off the wire
rather than only on frames already matched to a session, so any short frame of
any protocol reaches it. And DHCP rides this path at boot through
`dhcp-io-await` into `udp-io-recv`, so it sits on the first thing the machine
does with a NIC.

It read `ip-proto` (byte 9) and then walked `ip-payload` to the header's
claimed total-length with no guard on either. Measured 2026-08-15 on a frame
cut by six bytes: `!EXC=06` inside `udp-io-mine`, 30 actual IP bytes in R12
against 36 claimed in R13.

`ip-length-valid` and `ip-header-valid` now guard it, in ONE place:
`udp-io-payload` and `udp-io-src-port` are reached only after `udp-io-mine`
answers True, and `DhcpIO` parses no frame of its own.

**The same chapter also gained a UDP checksum, in both directions.**
`udp-build` wrote `[0, 0]` where the checksum belongs and nothing ever filled
it, so we neither computed one nor checked one. RFC 768's two zero rules are
not symmetric and the arms pin both: a COMPUTED zero goes out as 0xFFFF so it
cannot be read as "not computed", and a RECEIVED zero is ACCEPTED without
checking, because refusing it would refuse every peer that declines to compute
one.

**Eight arms. Two of them exist to catch a checker that has become strict
rather than correct.**

| arm | expects | why it is there |
|---|---|---|
| `good-frame` | True | a guard that refuses everything fails here |
| `other-port-frame` | False | well formed, refused for a reason that is NOT the guard |
| `cut-frame` | False | the truncation that used to kill the guest |
| `runt-frame` | False | shorter than an IP header, so byte 9 does not exist |
| `bad-hdr-frame` | False | a flipped byte inside the twenty the IP checksum covers |
| `bad-cksum-frame` | False | a flipped PAYLOAD byte, checksum left in place |
| `bad-len-frame` | False | a UDP length field that disagrees with the bytes present |
| `no-cksum-frame` | **True** | the same corruption with the checksum ZEROED |

`bad-cksum-frame` and `no-cksum-frame` are the discriminating pair and are
worth reading together: identical corrupted payload, and the only difference
is whether the checksum field is populated. One is refused and one is
accepted. That is what says the acceptance is the RFC 768 rule being applied
rather than the corruption going unnoticed. `good-cksum-nonzero` asserts we
actually populate the field on send, which is the line that would have failed
before this work at `0,0`.

**Two ablations, each moving only its own arms.** Deleting the two header
guards: the run prints `good-len`, `good-mine`, `good-payload` and
`other-port-mine` correctly and then dies `!EXC=06` at `cut-mine`, never
reaching `survived`. Deleting the `udp-checksum-valid` line instead:
`bad-cksum-mine` and `bad-len-mine` flip to True and **every one of the other
ten lines is unmoved**, `no-cksum-mine` included, which is what separates the
checksum arm from the length arm from the acceptance control.

**Run it with its sidecar, and this is a trap worth naming.** The DHCP tests
beside it carry `.vmargs` (`-dhcp-lease 4`, `-e1000-nat`), and a hand-rolled
runner that ignores those reports `dhcp-renew` and `dhcp-acquire-e1000` as
FAILED against a healthy tree: the renewal cannot be observed inside a run at
the default hour lease, and without `-e1000-nat` the Intel arm is its own
NE2000 sibling. Both looked exactly like a regression from the guard and
neither was.

## The HTTP Response Guard (`codex/test/apps/http-response-guard`)

Track D census item 4 (`VerifiedFormatParsing.md` 10.1): `http-parse-response`
is what the browser's `PageFetcher` and `HttpFetch` hand a server's bytes
to. Measured 2026-08-15 with fifteen arms BEFORE any change: the parser is
crash-safe by construction (every walk is bounded by `list-length`, no
declared length is trusted, high bytes and bare CRs pass through), and the
two defects it had were both the same shape, an `acc * 10` accumulator on a
plain `Integer`, which WRAPS rather than traps. A twenty-digit status token
parsed to `code=7766279631452241919 valid=True`; a twenty-digit
`Content-Length` parsed to the same number, and `Content-Length: -5` parsed
to `5`. None crashes; each is a wire-supplied number believed.

Now `parse-status-code` answers `-1` unless the token is exactly three
digits (RFC 7230 says it always is) and `valid` follows it; `http-cl-value`
answers `-1` on a leading minus and once the accumulator passes
`http-cl-ceiling` (10^15), which is above anything `http-max-response` will
ever read and below the wrap. Arms: `good` and `not-found` are the positive
controls, `good-cl` the content-length control; `empty`, `runt`, `one-cr`
are the pre-existing "fewer than two tokens" refusal; `status-only`,
`no-blank`, `cr-only`, `high-body` must still be ACCEPTED (a guard that
refuses everything fails here); `big-status`, `neg-status`, `big-cl`,
`neg-cl` are the refusals. The pre-change run is the ablation record: those
four arms read `7766279631452241919`, `0 valid=True`, `7766279631452241919`
and `5` respectively, and every other line was unmoved by the change.
`http-client-test`, `http-test` and `network-effect` still pass and the
browser bundle compiles.

**The server side treats the same header differently, on purpose (blu,
2026-08-15).** `WebServer`'s `wb-parse-num` feeds `wb-request-total`; an
oversized `Content-Length` makes the total exceed the bytes present and it
answers `-1`, "keep buffering", probed sound at 3, 18 and 25 digits. So the
server waits on an absurd length and the client refuses one. That is a
decision, not an inconsistency: a server that refuses a long request drops a
legitimate slow upload, while a client that believes a 20-digit
`Content-Length` has already been lied to, and the client is the half where a
wrapped or negative length reaches a slice. Do not "fix" either to match the
other.

## The TrueType Plausibility Guard (`codex/test/apps/ttf-plausible-guard`)

Track D census item 5. The font the desk loads off the ESP (`CMUNSS.TTF`,
`GopFont` `gfont-load`) is parsed by `apps/guios/FontLoad.codex`
`fb-parse-ttf`, NOT by `codex/foreword/encode/TrueType.codex` `ttf-parse`
(that one is reached only by `FontExtract`, which is red under FONTAI-1, and
by `TrueTypeFont`, which nothing calls). `fb-parse-ttf` is a `peek-byte`
parser: every 32-bit table offset in the directory, every loca entry and
every cmap subtable offset became a raw address relative to the buffer,
so a font from a stick could make the guest READ anywhere. Measured 2026-08-15
with the guard ablated: an EMPTY buffer parsed to `glyphs=8295` off heap
junk, and an 8-byte runt killed the guest, `!EXC=06` with `RDI=-1` (a
`list-at` on an empty hmetrics list, `ttf-get-hmetric` reaching for index
`length - 1`).

`fb-ttf-plausible buf size` now runs before the parse and `gfont-load`
answers `gfont-none` (the CBF fallback) when it says no. It reads only bytes
already proven inside the buffer, in dependency order by nested `if` (the
`&` operator is not a short circuit): the directory fits (`12 + tables*16`),
all seven required tables are present with `offset + length <= size` and a
per-table minimum (`head` 54, `maxp` 6, `hhea` 36), `numGlyphs >= 1`,
`numHMetrics >= 1` and `*4` inside `hmtx`, `indexToLoc` is 0 or 1 and
`(numGlyphs+1) * (2|4)` inside `loca`, and every cmap subtable offset lies
inside `cmap` with the format-4 table's own length and segment count inside
it. `fb-glyph-for-char` then clamps at glyph time: an index off the end of
`loca`, a glyph shorter than its 10-byte header, or one whose end lies past
the file all answer the empty glyph instead of reading.

Arms, on the 356-byte hand-built font from `truetype-bridge-test`, one byte
patched per arm: `good` (control: `glyphs=3 A-idx=2 A-contours=1`); `empty`,
`runt`, `trunc-200`, `many-tables`, `head-far`, `glyphs-huge`,
`hmetrics-zero`, `cmap-sub-far` all refused; `loca-flag-short` ACCEPTED
(short loca still fits, so a guard that refuses every changed byte fails
here); `glyph-past-end` accepted at the file level and answered by the
glyph clamp, `A-contours=0` where the control has 1. Every value predicted
before the run except the control's contour count, which is measured.

**Residual, named:** `fb-read-simple-glyph` still reads end points,
instructions, flags and coordinates relative to a glyph offset that is
inside the file, but the counts it reads are wire-supplied 16-bit values,
so a malformed glyph can read up to ~64 KB PAST its own end. That stays
inside the `size + 1024` allocation's heap neighbourhood rather than the
file, and produces a wrong glyph rather than a fault; bounding it against
`next-off` is the next arm.

## The GOP Mode Arms (`build/gop-mode-arm.ps1`)

The UEFI stub (`build/cdx-to-pe.ps1`, generated from
`codex/build/cdxtopeScript.codex`) now picks the largest GOP mode the
firmware enumerates before it reads `Mode->Info`: the 1024 the ASUS painted
at was the GraphicsConsole mode its own ClearScreen activated, and nothing
had ever called `SetMode`. `GopSetLargestMode` runs only when
`LocateProtocol(GOP)` succeeded and only on payloads without `-EntryStart`
(the flown A5 stub is byte-identical, pinned by `.efi` hash on both
`-EntryStart` arms). It reads `MaxMode`, skips everything when that is 1,
`QueryMode`s each index keeping the largest `HRes * VRes`, and `SetMode`s
the winner only if it differs from `Mode->Mode`. Every failure falls through:
a `QueryMode` error skips that index, a `SetMode` error changes nothing, and
the body reads `Mode->Info` from the firmware either way (L-OPTIONAL: the
bed's GOP is friendlier than AMI, so the fallback is the safety).

**The bed had to be made faithful first, and it found a host crash.**
codex-vm's GOP advertised three fixed modes regardless of `-gop-width`, so
the stub would have shrunk every 1600x900 desk run to 1024x768; the mode
table now carries the CLI mode as mode 3 (`OperatorsManual`,
`-gop-max-mode`). `QueryMode` also used to write the queried geometry over
the CURRENT mode's info block, so an enumerate-then-read without a `SetMode`
saw the last queried mode; it answers into a scratch block now. And the
`SetMode` trap never committed the framebuffer for a runtime mode set:
the default bed and `-gop-width 800` both crashed the HOST (`0xC0000005`
inside the trap) at the stub's `SetMode 2` while a bed already at the target
mode did not, the same class as the VBE mode set fixed at main 14494.

Six arms, one PE, expected values written in the script before they run,
read from codex-vm's own `GOP: SetMode` line and the BMP geometry at
`-screenshot-delay 2000` (blu measured 5000+ writing no BMP on a UEFI probe,
which reads exactly like a killed GOP):

| arm | bed | SetMode | BMP |
|---|---|---|---|
| `default` | 640x480, MaxMode 3 | 2 | 1024x768 |
| `desk1600` | `-gop-width 1600 -gop-height 900` | none (already largest) | 1600x900 |
| `maxmode1` | `-gop-max-mode 1` | none (nothing to enumerate) | no framebuffer |
| `remode` | `-uefi-conout-remode` at 1600x900: the ASUS shape | 3 | 1600x900 |
| `gop800` | 800x600 | 2 | 1024x768 |
| `gop` | `-gop` | 2 | 1024x768 |

**Ablation:** the main 15393 stub under the `remode` arm stays at 1024x768
with no `SetMode` line, which is what the ASUS photographs looked like. The
metal half is a stick rebuild and a photograph and rides a sitting under
the standing ruling; the bed cannot answer it (the ASUS's largest mode and
whether AMI's `SetMode` honours it are the L-FREEDOM questions).

**The guest can now answer it too, and that is the half a flight needs
(2026-08-20).** Every arm above is a HOST observation: codex-vm's own stderr
and a screenshot the guest never sees. A stranger booting a stick has neither.
So the stub banks its selection into the handoff block as version 3 -- maxmode
and the mode before the choice at +0xC8, the mode chosen and flags at +0xD0,
the `EFI_STATUS` `SetMode` answered at +0xD8 -- and `DiagGop.codex` reports it
as stage 6 of the diagnostic ladder. **Geometry alone could never have answered
this**: 1024x768 reads the same whether it was the largest mode offered or a
refusal, which is the pass/fail shape L-STATES forbids. The six states are
`honoured`, `kept`, `single`, `refused`, and two that indict the instrument
rather than the box, `noloop` (the bank contradicts itself) and `nostub` (a
v2 block, so an older stub booted this image).

The two instruments agree where they overlap, which is the point of keeping
both. codex-vm: host says `GOP: SetMode 2` and 1024x768, guest says
`max=3 before=0 chose=2 flags=3 status=00000000`. OVMF, a completely different
firmware: `max=30 before=0 chose=27`, 2048x2048.

**Both beds read `honoured`, so the row needed a falsifier before it could be
believed.** `gop-kept` in `diag-arm.ps1` runs the ladder at
`-gop-width 1600 -gop-height 900`, where the firmware already boots in the
largest mode it enumerates: `max=4 before=3 chose=3 flags=1 status=00000000`
and `SetMode` is never called. Every value there was written into the script
before the arm ran and all five matched. `refused` cannot be produced in
either bed -- codex-vm's `SetMode` always succeeds -- and that is precisely
the state the metal sitting exists to find.

**Two things about the emitter that reading it did not reveal.** The plan of
record said the six patched jump displacements sit below offset 112, so stores
appended between `SetMode` and `restore:` move nothing hand-computed. True of
an append, and the change is not one: the one-mode case has to be banked
BEFORE the `cmp ecx, 1` that branches away from everything else, so all six
literal indices moved. They are captured variables now. And the `jle skip` at
the top had to become a rel32: the stores put `skip:` 134 bytes away, a rel8
tops out at 127, and **nothing in the emitter range-checks a displacement**,
so the byte would have been written truncated and the stub would have jumped
into the middle of its own loop on any single-mode firmware. The function is
169 bytes and still asserts its own length.
## The CDX Input Guard (`build/cdx-guard-test.ps1`)

Blu's audit handed round the fleet 2026-08-15: find the paths in your lane that
parse bytes you did not produce, for a length trusted out of the data and for
an integrity field computed on the way OUT and never checked on the way IN.
`build/cdx-to-pe.ps1` had both, and it is the tool that turns a CDX into the
bootable UEFI image.

**Every header field it used was read out of the file and trusted.** `textOff`,
`textSz`, `rodataOff`, `rodataSz` at offsets 168..199, and `mapCount` in the
debug map, all reached `[Array]::Copy` and a `for` bound with no test against
the file length. **And nothing checked the content hash at bytes 8..39**, which
the compiler writes on every build (`cdx-build-header`, `X86_64Chapter.codex`)
and which `build.ps1` already computes for its own fixed-point comparison. The
field existed, was correct, and was never consulted on the way in.

**Why the consequence is worse than a crash.** A header that survives while the
content is short is the truncation shape, and the diagnostic would have been a
raw `ArgumentException` naming no number. Worse, a header UNDERSTATING a
section produces a PE that builds clean and dies on the metal with a black
screen, which is the most expensive failure this project has.

**Seven arms, and three of them must be ACCEPTED.** A checker that accepts
everything and a checker that works are indistinguishable on a good CDX, and a
checker that refuses everything passes a bare refusal count, so each refusal
arm also names the reason it must give:

| arm | want | reason asserted |
|---|---|---|
| good | ACCEPT | (none) |
| unsigned, bytes 40..135 zeroed | ACCEPT | (none) |
| six bytes off the tail | ACCEPT | (none) |
| truncated into the content | REFUSE | `claims bytes up to` |
| one bit flipped in the content | REFUSE | `content hash mismatch` |
| magic clobbered | REFUSE | `magic is not CDX1` |
| text size overstated by 4 KB | REFUSE | `do not tile` |

**The unsigned arm is the RFC-768 analogue and it is the point.** A CDX built
without the signing key carries 96 zero bytes where the signature goes, and
that is legal, so the guard checks the HASH and not the signature. Refusing
unsigned artifacts would have been a guard that looked strict and broke every
ordinary build.

**The tail-cut arm documents a LIMIT rather than hiding it.** Six bytes off the
end lands in the debug map's string table; the string search is already bounded
by the file length and a symbol it then fails to find refuses through the
pre-existing `__syscall_handler` throw. Asserting a refusal there would claim a
property the tool does not have.

**Two checks, because neither subsumes the other.** The overstated-text arm is
not truncated and its content hash is untouched -- the hash covers the region,
not the header fields -- so only the tiling invariant catches it:
`rodata-offset = text-offset + align-up code-len 8`, taken from the emitter,
which this script already ASSUMED one screen down where `$textAligned` becomes
`$dataVaddr`. Conversely the bit-flip arm has every length correct and only the
hash disagrees. Length and integrity are different questions.

**The ablation is the evidence, not the green run.** Measured 2026-08-15 with
the guard block deleted and `Deny` stubbed to a no-op:

| arm | with the guard | ablated |
|---|---|---|
| good | ACCEPT | ACCEPT |
| unsigned | ACCEPT | ACCEPT |
| tailcut | ACCEPT | ACCEPT |
| truncated | REFUSE, `claims bytes up to` | refused by the WRONG check |
| bitflip | REFUSE, `content hash mismatch` | **ACCEPT, 2,647,552-byte .efi** |
| badmagic | REFUSE, `magic is not CDX1` | **ACCEPT, 2,647,552-byte .efi** |
| bigtext | REFUSE, `do not tile` | **ACCEPT** |

All three acceptance controls are unmoved, which is what says the four that
move are measuring the guard and not the harness. The ablated bit-flip and
bad-magic arms each emitted a bootable image from corrupted and from non-CDX
input. The ablated truncation arm refuses only by accident, through the
`__syscall_handler not found` throw, so it reports a missing symbol when the
file is short and sends the operator to the wrong place.

**Two traps in the harness itself, both of which cost a revision.**
PowerShell's error formatting ECHOES THE SOURCE LINE before the message, so the
first `REFUSED:` in captured output is the uninterpolated `$why` from the
`throw` statement; scoring against it failed four arms that were working. And
the message is hard-wrapped with `|` gutters, so the reason must be matched
against the unwrapped text -- clamping the string for display before scoring it
failed the one arm whose phrase sits past column 74.

**Good input is unchanged.** All six `cdx-to-pe` flag arms (plain,
`-ExitBootServices`, `-EntryStart`, `-Stdin`, `-HeapAt`, `-HeapPages 131072`)
still produce `.efi` files byte-identical to the pre-guard script.

**The SCOPE of the hash, stated because "hash verified" reads as more than it
is.** The content hash covers `[224, contentEnd)` and therefore **does not
cover the 224-byte header**. Outside the magic and the four fields the guard
validates, a header field is neither hashed nor checked. Measured 2026-08-15 by
corrupting byte 140, inside `cap-off`: **accepted, and the emitted `.efi` is
byte-identical to a clean build.** That is the right outcome here -- this tool
never reads `cap-off`, so the corruption cannot reach its output, and the
identical artifact is the proof rather than the excuse. But a reader who takes
this section as whole-file integrity would be wrong, and a later tool that DOES
read those fields would need its own check. The prompt for measuring it was
reek's note that a guard can pass for the wrong reason; the answer here is that
it passes for a reason worth writing down.

## The GPT Integrity Guard (`codex/test/apps/gpt-hdr-crc-guard`, `gpt-array-geom-guard`, `gpt-array-crc-guard`)

The GPT half of the untrusted-volume audit, on top of val's FAT geometry guard
below. `gpt-esp-start` checked the `EFI PART` signature and **neither CRC32**,
so a tampered or half-written partition table parsed exactly like a good one,
and the ESP start LBA it hands out is the base every later read is relative to.
A garbage LBA was already caught downstream, because the volume it lands on
will not parse as FAT. **An LBA moved to a DIFFERENT well-formed FAT was not**,
and that is the case the array CRC exists to stop.

**Three checks, in this order, and the order is the finding.**
`NumberOfPartitionEntries` and `SizeOfPartitionEntry` are the array CRC's OWN
EXTENT, so they are geometry and must be refused BEFORE the CRC that depends on
them: a corrupt count moves the window and a reader then computes a perfectly
valid CRC over the wrong bytes. So header CRC (stage 9), then array geometry
(stage 10), then array CRC (stage 11).

**Each arm changes ONE thing and leaves the volume otherwise perfect**, which is
what makes them integrity arms rather than parsing arms:

| arm | the single change | refuses at |
|---|---|---|
| `gpt-hdr-crc-guard` | one byte of the DiskGUID at header+56, which nothing in the chapter reads | 9 |
| `gpt-array-geom-guard` | `NumberOfPartitionEntries` = 100000, header CRC RECOMPUTED so it still verifies | 10 |
| `gpt-array-crc-guard` | one byte inside partition entry 5, far past the two populated entries | 11 |

The array-CRC arm is the one worth reading twice. The ESP entry is untouched,
the geometry is untouched, the header CRC is untouched, and the volume mounts
and reads correctly in every other respect. **The array CRC is the only thing
in the system that notices.** The geometry arm recomputes the header CRC on
purpose, so its refusal cannot be stage 9 wearing stage 10's name.

**Every expected output was PREDICTED before the arm was run** -- stage 9, 10
and 11 written into the `.expected` files first -- so these are tests rather
than recordings of whatever happened.

**The ablation.** All three call sites replaced by `False`:

| arm | with the guard | ablated |
|---|---|---|
| `gop-fat16` (positive) | `esp-start=64`, full read | UNMOVED |
| `gop-fat16-geom-guard` (val's) | refuses at stage 8 | UNMOVED |
| `gpt-hdr-crc-guard` | `esp-start=0 stage=9` | **`esp-start=64`** |
| `gpt-array-geom-guard` | `esp-start=0 stage=10` | **`esp-start=64`** |
| `gpt-array-crc-guard` | `esp-start=0 stage=11` | **`esp-start=64`** |

Both controls unmoved, including val's, which is what says this guard runs
ahead of the geometry one without disturbing what it measures. Ablated, all
three arms hand out the ESP start LBA from a table that fails its own
integrity fields.

**FOUR EXISTING FIXTURES HAD TO BE REPAIRED, AND THAT IS THE FINDING.**
`gop-fat16.disk`, `gop-fat16-geom-guard.disk`, `fat-write.disk` and
`fat-write-guard.disk` are hand-built 64 KB images carrying the signature and
the entry array with `HeaderSize`, `FirstUsableLBA` and **both CRC32 fields
zero**. No fixture in the tree had a valid GPT CRC on the paths that now check
one, because nothing had ever looked. Measured across the whole corpus: of 26
GPT-bearing fixtures, **the 21 produced by `build/build-img.ps1` verify both
CRCs** and only these four hand-built ones did not. Each now gets
`HeaderSize=92`, `FirstUsableLBA=34`, and both CRCs computed in that order --
the array CRC first, because the header CRC covers the field the array CRC
lives in -- **and every one of the four reproduces its existing `.expected`
byte for byte**, so the repair is faithful rather than an accommodation.

`disk-facts-gpt-guard.disk` is a stub too and was deliberately LEFT ALONE: it
belongs to `DiskFacts`, not this chapter, and its comment says it carries a
sentinel through sector 2 on purpose.

**Cost, because this runs at every mount.** The array CRC streams
`NumberOfPartitionEntries * SizeOfPartitionEntry` bytes, 16 KB at our own
128 x 128, a sector at a time with only the running CRC retained -- no buffer
and no list, on a path with no GC. The 256-entry `crc32-table` is bound once
and threaded as a parameter for the reason this chapter's own GUID comment
gives: a list constant is rebuilt at every mention, so naming it inside the
byte loop would rematerialise the whole table per byte. A read failure answers
-1, which no CRC can be, so a short medium refuses rather than comparing
against a fabricated zero.

**One trap that did not bite and is worth recording anyway.** blu's warning
that a hand-rolled runner ignoring a `.vmargs` sidecar reports healthy tests as
failed was checked rather than assumed: all seven tests in this set carry a
`.disk` and nothing else. `fat-write-big` does carry `.vmargs`, and it is
skipped, so it cannot break here -- but whoever unskips it needs a fixture with
a valid GPT.

## The Foreword GPT Geometry Guard (`codex/test/apps/gpt-core-read`, `gpt-core-size-256`, `gpt-core-size-guard`, `gpt-core-count-guard`)

Track D 10.1 item 14 (red, 2026-08-16). The three arms above guard the WORKS
GPT reader, `GopFat16`'s `gpt-esp-start`. The FOREWORD reader,
`codex/foreword/core/Gpt.codex` `gpt-read`, is a different function on a
different path: `DiskFacts.codex:501` and `Fat16.codex:1258` call it, which is
the compiler's own boot-volume walk and inside the seed's reachable set. It
checked the signature and refused `SizeOfPartitionEntry` of 0 and above 512,
and its own prose said that was the whole hazard. It was not: any entry size
from 1 to 127 was admitted, `entries-per-sector = 512 / entry-size` and
`off-in-sector` then run to `(512/es - 1) * es`, and `gpt-parse-entry` reads
128 bytes from there (name at +56, 72 bytes long), which for every such size
is past byte 511 of a 512-byte sector buffer. `peek-byte` on a raw buffer does
not trap, so that is adjacent heap read as a partition entry, silently. The
u32 count at header+80 was walked to the first zero type GUID with no
ceiling, and the entry LBA at +72 was any u64.

**The guard is the same test the Works layer already runs**, `gpt-array-geom-ok`
transcribed as `gpt-header-geom-ok`: entry LBA at least 2, count 1..1024, size
128..512 and a multiple of 128, and the array ending at or before
`FirstUsableLBA`. Refuse, not clamp: every one of these decides WHERE a read
lands (blu's split). Two layers now agree, so an image the desk mounts is an
image the compiler's store reads, and a fixture that fails one fails both.

**Fixtures.** `build/mint-gpt-core-fixtures.ps1` derives all four from
`gop-fat16.disk` (val's, both CRCs valid): protective-MBR signature set at
510/511, because the foreword reader requires it and the Works one does not,
then ONE header field changed and the header CRC recomputed, so the refusal
cannot come from anything but geometry. The minted control reproduces
`gop-fat16.disk`'s header CRC `526e8cfd` and array CRC `db47cd74` exactly, which
is what says the minter's CRC is the same function as val's.

**Arms, every value written into `.expected` before the run:**

| arm | the single change | expected | ablated (guard call replaced by the old `entry-size <= 0` test) |
|---|---|---|---|
| `gpt-core-read` (positive) | none | `gpt parts=1 esp=64` | unmoved |
| `gpt-core-size-256` (accepted, not refused) | size 256, count 64 | `gpt parts=1 esp=64` | unmoved |
| `gpt-core-size-guard` | size 64 | `gpt none` | **`gpt parts=1 esp=64`** |
| `gpt-core-count-guard` | count 100000 | `gpt none` | **`gpt parts=1 esp=64`** |

All four answered as predicted, and both refusal arms move under ablation.
Read the size arm's ablated answer for what it is: entry 0 at offset 0 is the
real ESP, entry 1 at offset 64 lands in entry 0's zero name bytes and stops the
walk, so the ablated reader believes the size and answers a plausible table
from a misaligned stride. The over-read itself (offset 448 + 128 on the eighth
entry) is by inspection; this fixture has one populated entry and does not
reach it, and an arm that did would be reading whatever the allocator put
after the sector, which is not a value a `.expected` can pin.

**What it does NOT do.** No CRC in the foreword reader, either one; the Works
layer checks both and this item was the bounds. The 16384-byte array floor the
spec requires is deliberately not enforced: 20 of the 33 GPT-bearing fixtures
in `codex/test/` (every one `build/build-img.ps1` mints, and so every boot
stick) carry `NumberOfPartitionEntries = 1`, and every one of the 33 was
re-measured against the new test before it landed: all pass except the two
100000-count arms and the size-64 arm, which are the ones meant to, and
`disk-facts-gpt-guard.disk`, an all-zero header stub that was refused before
(entry size 0) and is refused now.

**Cost.** Four u32/u64 reads and one comparison chain per `gpt-read`, no
allocation; the count ceiling makes the entry walk at most 1024 entries
(256 sectors at 128 bytes) where before it was unbounded.

## The FAT Geometry Guard (`codex/test/apps/gop-fat16-geom-guard`)

Blu's same audit, one leg on from reek's Config-Descriptor Clamp (handoff
blu -> fester -> reek -> val, 2026-08-15): `GopFat16` mounts a GPT+FAT volume the
desk did not produce, and the ASUS reads the compiler's own source off that
volume (A5). The BPB fields are volume GEOMETRY, not a read length:
`sectors-per-cluster`, `reserved-sectors`, `num-fats` and `fat-sectors` decide
WHERE every later read lands. Before this, `gfat-mount-at` refused only
`bytes-per-sector /= 512`; a bogus `reserved` sent `fat-start` off the medium and
the mount served the wrong sectors, silently.

**Clamp is wrong here; refuse is right** (blu's split). A read-length field longer
than the buffer clamps and you lose nothing you had. A geometry field does not
bound a read, it relocates every read, so a clamped-but-wrong value reads the
wrong place and hands the caller confident garbage: A5 compiling wrong source and
never knowing is the exact failure the whole cascade started from. So
`gfat-geometry-ok` REFUSES a volume whose sectors-per-cluster is not a power of
two through 128, whose reserved count is zero, whose FAT count is not one or two,
whose FAT is empty, or whose fat-start / data-start fall outside the declared
volume. Every bound is a FAT-spec invariant, so no valid volume is refused, which
the positive arm proves rather than asserts.

**Two arms, and the negative one names its stage.** `gop-fat16` is the positive
control: the well-formed image mounts, `mount=ok`, unchanged by the guard.
`gop-fat16-geom-guard` is the negative: the same image with the BPB reserved count
set to 0xFFFF. It prints `esp-start=64` -- GPT parsing still succeeds and bps is
still 512, so the refusal is post-GPT geometry and nothing earlier -- and
`geom-mount-ok=0 stage=8`, where 8 is `gfat-stage-geom` on xdiag cell 80. Ablate
`gfat-geometry-ok` to `True` and the negative arm flips to `geom-mount-ok=1
stage=7`: the mount accepts a volume that reads off the medium. Measured
2026-08-15 (val), both directions on codex-vm, so the discrimination is asserted
on every run and cannot rot into a tautology.

## The FAT Cluster Range Guard (`codex/test/fat32-parse`, `range32` row)

The leg after the geometry guard, same audit and same volume. Geometry decides
where a mount lands; this is the cluster number that arrives AFTER a successful
mount, out of a directory entry's `de-cluster` or out of a FAT slot, both of
which are volume data. `fat16-cluster-sector` turns one into an LBA by
arithmetic alone, so a number above the volume's own limit addresses sectors
past the medium, and `gfat-fat-next` indexes the loaded FAT buffer at
`cluster * 4` with no bound of its own.

**The sentinel test could not catch it and must not be taught to.** Seven
walkers guarded only `cluster < 2` plus `gfat-is-end`. On FAT16 that hides the
gap, because every value over 65528 already reads as end-of-chain; on FAT32 the
whole band from the cluster count to 268435447 is neither a sentinel nor
addressable. Folding a range test into `gfat-is-end` would have covered every
walker in one line and blinded the `is-end32 big=no` arm, which exists to
observe WHICH WIDTH'S sentinel ran (L-INSTRUMENT). So the range question is a
separate predicate, `gfat-cluster-ok`, and `gfat-is-end` still answers only its
own.

**The bound already existed.** `gfat-max-cluster` is the volume limit capped by
the FAT's own capacity, and the write side used it while the read side did not.
It is EXCLUSIVE -- `fat16-last-cluster vol + 1` -- which `gfat-mark-chain` and
`gfat-chain-hits` already encoded as `cluster >= limit`, and which is why the
predicate is `<` and not `<=`. Nine sites now take it: `gfat-find-in-dir`,
`gfat-list-walk`, `gfat-list-dir`, `gfat-read-loop`, `gfat-read-runs`,
`gfat-dir-slot32`, `gfat-write-runs`, `gfat-chain-release`, and
`gfat-run-length`. The last is the one that is easy to miss: guarding the
STARTING cluster still leaves the run walking upward under a cap taken from the
file's own size, so a valid start could extend a contiguous run past the medium
(L-CAPABILITY -- the entry check alone leaves the capability absent).

**The arm is a census, not a threshold.** `range32` builds a FAT32 BPB
declaring 40000 sectors, which puts the highest valid cluster at 38937, and
prints the bound alongside the verdict at six points: `clusters=38936 max=38938
c0=no c1=no c2=yes last=yes past=no big=no`. The earlier fixtures all leave
total sectors at zero, so their cluster count is zero and every cluster is out
of range, which cannot tell a correct bound from one that is off by one.
**Ablated to `<=`, exactly one cell moves** -- `past=no` becomes `past=yes`,
with `last` and `big` unchanged -- so the arm fails when and only when the bound
is wrong at the boundary, and cannot be satisfied by one that is merely roughly
right. Measured 2026-08-15 (fester), both directions on codex-vm.

## A Count Is Not A Length (`codex/test/arp-cache-bound`, `codex/test/dns-answer-count`)

Two crashes on the same shape, found a day apart in different chapters, and
the pair is worth reading together because the shape is what generalises
rather than either instance.

**A count and the list it describes drift apart, and nothing is visible from a
lookup that happens to succeed.** A walk bounded by the count indexes past the
list; a walk bounded short of it loses entries. Neither shows up in ordinary
traffic, because in ordinary traffic the two agree.

| | the count | the list | what went wrong |
|---|---|---|---|
| ARP cache | `cache.count`, ours | `entries`, ours | both ours, and they had to be kept in step across an update-in-place |
| DNS answers | `ancount`, **the peer's** | `answers`, ours | the peer's number bounded a walk over a list the peer did not build |

**The ARP half was a bounded-integer refusal.** `arp-cache-add` appended into a
`count` banded 0..255 with no duplicate check, so the 256th ARP frame wrote 256
into that band: `!EXC=06`, `RSI=0x100` against a ceiling of `0xFF`. It never
needed 256 distinct hosts, because re-sending the same ten senders took the
count from 10 to 20. Fixed by updating in place and refusing past
`arp-cache-max`; `gw-survived-flood` is the arm an eviction policy would fail,
since evicting lets a flood push out the gateway.

**THE FLOOD CHANGED SHAPE 2026-08-20 AND THE TEST WOULD OTHERWISE HAVE STOPPED
ASKING ITS QUESTION.** `net-process-arp` now learns only from a REPLY addressed
to us (Damian's ruling, CurrentPlan Decisions 2: learning from any frame on the
wire is cache poisoning by construction). The flood arm was three hundred
REQUESTS, which is precisely the shape the narrowing refuses, so left alone it
would have filled nothing and every capacity assertion above it would have
passed vacuously against an empty cache. It is three hundred replies now, and
the capacity, dedup and count-in-step questions are asked exactly as before.

That is the general trap and it is worth more than this instance: **when a
guard is narrowed, an arm that fed it through the newly-refused path does not
fail, it goes quiet.** `count-after-300=64` and `count-after-300=0` are both
"no crash"; only the first is the question. Anything asserting a CAP has to be
re-aimed at whatever can still reach the cap, or it is asserting that nothing
happens.

The narrowing brought its own arms into the same file rather than a new one,
because they are the same subject. `poison-count` floods three hundred requests
aimed at a THIRD party -- the old shape, kept as evidence rather than deleted --
and requires the cache to hold exactly the one entry learned before it, with
nothing queued. `asked-us-*` sends a request aimed at US and requires both
halves: `arp-replied` with one frame queued, and a cache still empty. The reply
is built from the sender MAC in the frame just parsed, so answering never needed
the cache, and RFC 826's licence to record the sender there is the licence a
forged request would have used. The pre-learned gateway entry is the control in
both arms: if the cache were broken rather than narrowed, it would be gone too.

Every value in `codex/test/arp-cache-bound.expected` was predicted before the
run and all sixteen matched.

**The DNS half was twelve bytes.** `dns-parse-response` stored `ancount`
verbatim while `dns-parse-answers` stops the moment `off + 10` passes the end
of the datagram, so the list is routinely shorter than the header's number. A
header-only response claiming 100 answers died `!EXC=06` in
`dns-find-a-in-answers` with the real length 0 in R12 and the claimed 100 in
R13. `HttpFetch.codex:167` parses a wire datagram and line 168 calls straight
into it, and DNS answers are unauthenticated UDP. Fixed at both levels:
`answer-count` is now what was parsed, and both walkers take their bound from
`list-length` regardless of the field.

**Both arms carry a positive control on the same path** (`honest` differing
from `lying` only in the claimed count; the gateway learned before the flood),
because a parser that answers "nothing here" to everything passes the crash arm
on its own. Both reach a final `survived` line that the pre-fix runs never
printed, which is what distinguishes a refusal from a trap.

**The lesson generalises past these two.** Wherever a length, count or offset
arrives beside the data it describes, ask which side produced each. If the
count is the peer's, it is an assertion and not a fact. If both are ours, they
are an invariant somebody has to maintain, and the maintenance is exactly where
an edit breaks it.

## The Staging Bank Bound (`codex/test/apps/ota-lwm2m-loopback`)

Four arms over the LwM2M firmware download, added 2026-08-20 (reek) on red's
ruling. `fw-write` wrote each block at `fw-stage + fw-written` with nothing
bounding the total, so a server sending in-order blocks drove an unbounded
flash write; `flash-write-page` takes an absolute address, so nothing under it
refused. The bank size now enforces and CoAP `Size2` refuses early only.

The arms and what each is for: **`size2 honest`** is the positive control, an
image that fits, staged whole with Gate A passing, and it must never be
refused; **`bank no-size2`** fires the bound with no hint present at all;
**`size2 over`** is the early refusal, zero bytes written; **`size2 lying
low`** declares a small total and sends more, which is the arm that proves the
hint has not become the bound.

**Each ablation moves only its own arms.** Removing the bank bound takes
`bank no-size2` and `size2 lying low` from 128 to 254 -- both then overrun a
128-byte bank -- and leaves `size2 over` at 0. Removing the `Size2` check
takes `size2 over` from 0 to 254 and leaves both bank arms where they were.

**Two things about the fixture that cost time and would cost it again.** The
arms deliberately leave PARTIAL images in the one staging bank the fixture
shares, and Gate A reads that bank, so the positive control is ordered FIRST:
run last it reports `GateAFailMagic` and the reading is about the fixture
rather than the bound. And the arm loop stops on the first failure, because
`fw-stage-block`'s block-order check fires on the next block and would
overwrite the result code with `conn-lost`, hiding the cause under a symptom.

## The Transport Length Guards (`codex/test/apps/tcp-transport-guard`)

Track D item 1, and the widest-reach parser in the census: `TcpTransport` carries
the 4-byte length prefix on EVERY TCP transport we have. `NetIO`, `Arm64NetIO`,
`TrustTransport` and all 51 plugs reach it through `plug-source`, and the only
test on it fed one well-formed frame.

Three separate defects, all in the recv path, all fed from the wire.
`transport-feed-raw` wrote `data` at `recv-len` into `recv-base` with no check
against `recv-cap`: `__buf-write-bytes` takes an offset and no capacity, so a
peer sending more than 32 MB before a message completed wrote past the
allocation. `transport-try-recv` then read a 4-byte `msg-len` off that buffer and
used it two ways it could not survive: `msg-len = 0` gave
`__buf-read-bytes base 5 (msg-len - 1)`, a read of length **-1**, and a `msg-len`
above `recv-cap - 4` named a message that can never fit, so the connection waited
for bytes it could never accumulate and never made progress again.

**Refuse, not clamp** (blu's split, and geometry is not the reason here -- these
ARE read lengths). A clamped over-cap write would still be a message we
half-received and could not reassemble, and a clamped `msg-len` would hand the
caller a body of somebody else's bytes. So the over-cap feed drops the frame and
leaves the buffer untouched, and both malformed lengths reset `recv-len` to zero
through `transport-drop-recv` and answer `has-message = False`.

**Four arms, one positive, and every ablation was run.** The state is built by
hand with a 16-byte `recv-cap` instead of `transport-new`'s 32 MB, because a test
cannot feed 32 MB; row 1 feeds a well-formed frame through the same tiny state
and still gets `has=True tag=2 body=2`, so a refusal below is the guard and not a
dead path. Measured 2026-08-15 (val), each ablation moves exactly one row and no
other: drop the `recv-cap` check in `transport-feed-raw` and row 2 reads
`len=20 cap=16`, four bytes past the allocation; drop the `msg-len < 1` check and
row 3 reads `has=True len=1`, the malformed frame accepted with the -1 read
length having gone into `__buf-read-bytes` without faulting; drop the
`recv-cap - 4` check and row 4 reads `len=4`, the stalled connection. The
ablations are what make the four expected lines an assertion rather than a
transcript.

**No legitimate message is refused by the new ceiling**, and that is arithmetic
rather than a hope: `recv-len` can never exceed `recv-cap`, so a message needing
`4 + msg-len > recv-cap` bytes could never have completed under the old code
either. The guard converts a permanent stall into a drop.

## The Identity Wrap Known Answer (`codex/test/apps/first-boot-ceremony`)

Identity reconciliation stage 1 (red, 2026-08-18). The shipped first-boot
wizard wrapped the Ed25519 seed under a MALFORMED key: `hkdf` already answers
bytes, and `GopWizard` re-expanded those 32 bytes with `wz-words-to-bytes` into
128 elements of `[0,0,0,b]` and handed AES-256 that list as its key
(`GopWizard.codex:348`, `:493` before the fix). The AES-256 schedule then read
words 8..31 out of the padded list instead of expanding them. It round-tripped,
so this test could not see it: **a round trip through your own wrap and unwrap
agrees with itself under any key schedule** (L-FALSIF, the same shape as the
`ddc` self-agreement). `IdentityManager.codex:385-393` had found and fixed the
identical defect on a copy nothing calls, and nobody ported it.

The arm that cannot be dodged is a known answer computed OUTSIDE Codex: .NET
`HMACSHA256` for HKDF-SHA256 (salt 16 x 7, ikm `test`, info `codex-identity-v2`,
L 32) and `System.Security.Cryptography.Aes` for AES-256-CBC/PKCS7 (iv 16 x 9,
plaintext bytes 0..31), pasted into the test as `fbc-kat-dk` and
`fbc-kat-wrapped`. The row `kat dk-len=32 dk-eq=Y wrapped-eq=Y expanded=240`
says the wizard's key IS HKDF's 32 bytes, the wrap IS AES-256-CBC, and the
expanded schedule is 240 bytes (a 128-element key expands to 336; that number is
the fingerprint of the old defect). Under the pre-fix wizard the same row reads
`dk-len=128 dk-eq=N wrapped-eq=N expanded=336`, which is the ablation.

`IDENTITY.DAT` is version 2 now and **version 1 is refused** (`version1=rejected`):
a v1 reader and an unlock-time rewrap were built and then removed the same day
at Damian's direction, because no v1 record exists outside this environment
and a reader for the malformed wrap is only a hole going forward. The stage
did not change the entropy path, the storage, or what happens to the seed
after unlock; those are stages 2-4 in `CurrentPlan.md`.

**Stage 2 (red, 2026-08-18): the seed is kept and everything else is scrubbed.**
`pinned load=0 status=1 seed-zeroed=Y after-zero=0` is the pinned-region arm:
`wz-load-seed` copies the seed through `key-load` into the kernel's pinned
32 bytes (`X86_64Boot.codex` `identity-key-addr`), zeroes its own copy, and
`key-status` reads 1 until `key-zero` (three failed attempts, restart, power
off) reads 0. `wz-bytes-eq` folds every byte into one difference word and
decides at the end (constant time in the position of the first difference).

The zeroing itself is NOT per value, and the reason is the arm
`codex/test/heap-scrub`. `Text` is `[len][bytes]` on the bump heap, so a
`text-zero` would be a three-instruction builtin -- and it would be the wrong
instrument: the passphrase field builds its text one character at a time
(`GopText.codex:67`, `buf & char-to-text ...`), so every PREFIX of the
passphrase is its own allocation, and zeroing the final `Text` leaves
"hunter2", "hunter", "hunte" ... readable one byte short. The arm types
`hunter2!` the way the field does and counts the first character across the
heap region: `prefixes-before=9` (eight prefixes plus the one-character
text), then `heap-scrub-to mark` (`codex/foreword/core/HeapScrub.codex`)
zero-fills `[mark, top)` and restores the heap: `prefixes-after=0
nonzero-after=0 heap-back=Y`. The control is the same typing under a plain
`__heap-restore`: `prefixes-still-there=9`. Locals are registers and stack
spills (`X86_64State.codex` `alloc-local`), never heap, so the scrub cannot
reach the frame that is running it. The wizard brackets the whole secret half
of the ceremony (passphrase, entropy, keygen, save, `key-load`) and the whole
unlock prompt under one mark and scrubs on every exit; the completion screen
re-reads the identity from the stick because the record was above the mark.
That is why the ceremony order changed to upstream, timezone, then identity:
everything after the mark is discarded, so the non-secret answers the last
screen still shows come first. Not scrubbed: the USB HID report buffer the
keystrokes arrived through, which is the keyboard driver's and outside the
mark.

**Stage 3 (red, 2026-08-18, Damian's ruling): the bench auto-unlock is bed-only.**
`wz-auto-pass` (`abc123`) is still in the source and still worthless as a
secret; what changed is that `wz-auto-try` is given `wz-in-bed`, CPUID leaf 1
ecx bit 31, the bit every hypervisor sets and no bare board does, and answers
`None` without deriving a key when it is clear. So a returning stick on metal
always asks, and the bench (codex-vm, QEMU, any VM) still boots to the desk
without typing. The row `bed=Y auto-on-bed=Y auto-off-bed=N` wraps a fresh
identity under the bench passphrase and asks `wz-auto-try` both ways; the
first field is the test host's own bit, so an `.expected` of `bed=Y` also
pins the test to running under a hypervisor, which every battery run does.
The image itself is the same bytes on metal and in the bed; there is no build
flag, and a flag would have been the weaker instrument since the desk image the
bed boots IS the stick image.

**Stage 4 (red, 2026-08-20): the trust root is in the file, and the passphrase
changes.** `IDENTITY.DAT` is version 3: a 64-byte Ed25519 self-vouch over
`"codex-trust-root-v1" ++ public key` follows the wrapped seed, signed at
keygen while the seed is in hand and verified at parse, so a record that does
not certify itself is refused before any unlock is offered. The vouch binds
only the public key, which is why a passphrase change (fresh salt, iv and
wrap) leaves it byte-identical: `rewrap new=Y old-rejected=Y pub-eq=Y
vouch-eq=Y` is the row, `machine-wrap=Y` is the same claim through the
machine-derived salt path `wz-change-pass` actually calls, and
`vouchflip=rejected` is the sabotage arm that keeps the verify from being an
instrument that cannot fail (L-FALSIF): one flipped signature byte refuses the
parse, and the arm restores the byte so the version and magic arms that follow
mutate an otherwise intact record. `version2=rejected` beside
`version1=rejected`, by the same ruling: no v2 record exists outside the
bench, and a reader for a superseded format is only a hole going forward.

**WHICH VERSIONS EXIST, MEASURED 2026-08-21 (root), because both refusal
paragraphs above rest on a premise that is false.** `wz-id-version` is 3 and
`GopWizard.codex:483` accepts that and nothing else. Stage 1 justified removing
the v1 reader with "no v1 record exists outside this environment" and stage 4
justified `version2=rejected` with "no v2 record exists outside the bench".
**Both are wrong.** A scan of all 31 hardware-returned images in the stick
archive for the `CIDN` magic found THREE distinct records a board actually
wrote: v1 in `stick-before-20260811.img` (`AB5C7D83058DC876`), a DIFFERENT v1
in `vmxprobe-returned-20260813.img` (`D20EFE7F1EF9C0E0`), and a v2 in
`a8v2-returned-20260819.img` (`D2BF3CB4D86C6878`). All three are ingested at
`build/boot/archive/` with the images that carry them (rulings queue 4, red
2026-08-21).

**This does not reopen either ruling**, and the distinction matters: the other
half of each justification -- a reader for a superseded format is a hole going
forward -- is untouched by how many records exist, and it is the half that
decides. What changes is that `version1=rejected` and `version2=rejected` can
now be armed against a record a BOARD wrote rather than one the bench
synthesised, which is the only evidence we have about what the parser refuses.
Nothing in the tree reads those files yet.

**`bed-identity` as first landed (main 17691) was not deterministic, and the
green that landed it certified one boot.** `wz-keygen` derives salt and iv
from the device seed cell and the tick count, so the test pinned one run's
random bytes and every re-run went red; the `.expected` and `BEDIDENT.DAT`
agreed only because both came from the same boot. `wz-keygen-with` takes salt
and iv explicitly, the bed identity passes fixed ones (it wraps a public seed
with a public passphrase and protects nothing), and the pin now holds across
consecutive runs. `BEDIDENT.DAT` is 188 bytes and carries the vouch.

## The Handshake Prove Guards (`codex/test/apps/handshake-prove-guard`)

Track D item 2, and it turned out not to be a parsing bound at all. **The
handshake did not authenticate anybody.** `hs-receive-prove` bound
`expected-nonce`, never read it, never took the peer's signature as a parameter
in the first place, and returned `HsCompleted` unconditionally with the trust
score of whatever public key the peer had claimed in its hello. Its one caller,
`trust-complete-as-responder`, received the prove message, discarded `rr.body`,
and set `authenticated = True` on every path. Proof-of-work needs no private
key, so anyone who could hash could claim any identity in the lattice and be
believed.

That survived because **nothing calls it** (L-UNCALLED). The four handshake entry
points in `TrustTransport` have no caller anywhere in the tree, tests included,
which also corrects the census's reachability note for this row: item 2 is
LATENT, not reached, the same finding that demoted `WebSocket` and
`PeerDiscovery`. It was fixed rather than demoted because a bounds bug in an
uncalled parser is a bounds bug, while an authentication step that authenticates
nothing is a trap for whoever wires it up.

`hs-receive-prove` now takes the signature and refuses three ways: a public key
that is not 32 bytes, a signature that is not 64, and a signature that
`ed25519-verify` does not accept over the challenge nonce. **The two length
checks are not defensive politeness.** `ge-from-bytes` reads index 31
unconditionally and `ed-list-drop sig 32` computes `len - 32` as a slice length,
so a short key or signature is an out-of-bounds read and a negative length inside
the crypto. `hs-receive-hello` refuses a non-32-byte key too, which is the
earlier refusal and also bounds `bytes-to-hex-hs`, whose accumulator is quadratic
in a peer-supplied length.

**Six arms, two of them positive, every guard ablated and run.** Row 1 is a real
RFC 8032 vector-1 key signing the real challenge nonce, and it asserts
`score=7000` rather than merely completing, so a disagreement between
`bytes-to-hex-hs` and Ed25519's own `bytes-to-hex` would read as `score=0`
instead of passing silently. Row 5 is a well-formed hello. Measured 2026-08-15
(val): removing the `ed25519-verify` call flips row 2 to `failed=False`, a
signature over a different message accepted with the impersonated key's score,
which is the pre-fix behaviour reproduced exactly. **Removing either length check
does not soften a refusal, it kills the guest**: both ablations die `!EXC=06`
before printing a line, so those two guards stand between a peer and a remote
crash of the node. Removing the hello key check flips row 6 alone.

The residual is named rather than fixed: `decode-hello-body` still ignores the
`valid` flag `frame-decode-bytes` now returns, so a truncated hello body decodes
to a garbage `HelloMsg`. The key-length guard refuses the result, but the
refusal reason will be the key rather than the truncation. That is Track D item
3's change.

## The Chain Cycle Guard (`codex/test/fat16-cycle-guard`)

The other half of the two cluster guards, and the half they explicitly did not
cover: those bound the cluster ADDRESS, this bounds the WALK. A FAT whose
chain cycles -- 2 to 3 and back to 2 -- is well formed at every single step,
`fat16-cluster-ok` answers yes to both numbers, and the walk never ends.
`Fat16.codex` had said so in prose for some time without closing it.

**Eight walkers, measured rather than taken from that prose, which is wrong in
one direction.** `fat16-free-chain`, `fat16-find-free-slot-in-dir`,
`fat16-find-name-slot-in-dir`, `fat16-find-in-cluster-dir`,
`fat16-cluster-entries`; `fat32-find-chain-end`, `fat32-find-in-dir`,
`fat32-list-dir-cluster`. **`fat16-read-cluster-bytes` is NOT among them**
though the prose names it: it decrements a byte budget by at least one per
step and stops at zero, so a cycle makes it re-read clusters and still
terminate. Same for `fat32-read-cluster-bytes`, `fat32-alloc-loop` and
`fat32-write-data-to-chain`, each bounded by a quantity that advances.

**A COUNTING BOUND WAS TRIED FIRST AND MEASURED UNFIT. That measurement is
the reason the shipped code is Brent's algorithm.** Fuel of cluster-count + 2
is spec-derived -- a chain visits each cluster once -- and it does terminate.
On the fixture (30,414 clusters, fuel 30,416) the cyclic walk **did not finish
inside the harness window: 60.4 s, no output**. Dropping the fuel to 10
returned in **0.6 s**, which is what proved the mechanism reached the walker
rather than the bound going unread. **A finite bound is not a survivable
one**, and on FAT32 the clamped ceiling is 268435447, far worse.

**Brent, not Floyd, and the difference is the read count.** Floyd's hare
takes two steps per iteration and every step here is a FAT sector read, so it
costs about half again as many reads on healthy volumes. Brent advances one
cursor exactly as the unguarded walk did, keeping a saved cluster, a power
and a step count, and re-saving on a doubling schedule. A well-formed chain
therefore pays one comparison per step and nothing else. Measured on the full
30,414-cluster volume: **0.6 s**, against the counting bound's 60.4 s timeout
on the same fixture.

**blu's underlying point stands and is why the count is not the bound.**
Cluster count is computed from BPB fields the image supplies
(`Fat16.codex:88-90`, `Fat32.codex:43-44`), so a declared total-sector count
would have been setting it: **a bound derived from untrusted bytes is not a
bound**. The parse-time clamp is kept anyway -- 65524 on FAT16, 268435445 on
FAT32 -- because it gives `fat32-alloc-loop`'s limit a real ceiling and stops
Fat32's `data-sectors` going NEGATIVE, which is how the first Fat32 arm got a
cluster count of -7454427426.

**The arm's ablation is a HANG, not a moved row**, and that is a weaker
signal worth naming. With the cycle check: **0.6 s**, `walk-terminated none`.
With it bypassed: **61 s and no output**, reported as a timeout. The fixture
is `fat16-alloc.disk` with both FAT copies patched to `2 -> 3 -> 2`, at full
geometry -- because Brent is O(cycle) the arm does not need a shrunken volume
to run fast, so it exercises the realistic 30,414-cluster case rather than an
easier one.

## The Foreword Fat32 Guards (`codex/test/fat32-cluster-guard`)

Track D 10.1 item 17, and unlike item 12 it is **not seed-affecting**:
`Fat32.codex:3` cites `Fat16`, not the reverse, so `Fat16` is pulled into the
compiler unit and `Fat32` hangs off it without being pulled in. Measured two
ways -- no `cites Foreword chapter Fat32` under `codex/compiler/`, and blu
grepped the whole subtree for the bare word in case of a citation under
another name. The citers are `DriveManager`, `DevConsoleBoot`, `GopFat16`,
`FontLoad` and tests.

**Two guards, both of red's item-14 shape: refuse where the field decides
WHERE a read lands.**

**The divisor.** `fat32-parse-bpb32` computed `total-clusters = data-sectors /
spc` where `spc` is `peek-byte buf 13`, unchecked. `Fat16` has answered a
zeroed sector with `fat16-zero-volume` since the same fault was found on its
side, and its prose says exactly why: with no disk attached the block device
hands back a sector of zeroes and dividing there takes the machine out with
`!EXC=00`. This chapter had no such escape at all -- no zero-volume, no
usability predicate -- and divided regardless. **Ablated, the arm reproduces
that verbatim**: `!EXC=00 RIP=000000000010b1fb`, the guest dead before any
caller could notice a bad volume.

**The range.** `fat32-next-cluster` turned its argument into a FAT sector
address by arithmetic alone. Same class as item 12 one format over; the
refusal answers 268435455, end-of-chain in this format's own 28-bit width.
**Ablated, `next-past` becomes 109791427 and `next-big` becomes 0** -- the
unguarded read landed on arbitrary sectors and returned a cluster number that
would send the walk anywhere.

**The volume is built in the arm, not taken off a fixture, and that mattered.**
The first version handed `fat16-alloc.disk` to a FAT32 parser: offset 32 reads
zero and offset 36 reads noise, so the cluster count came out **negative** and
every `ok` row read `no` -- the right answer for the wrong reason, and an arm
that would have passed with no guard at all. A BPB written at the spec's own
offsets (40000 sectors, spc 1, 32 reserved, two 516-sector FATs) puts the
highest valid cluster at 38937 and makes the boundary rows mean something.

**It needs no `.disk` sidecar**, checked rather than assumed: the BPB is
synthetic and the guard refuses before `block-read-sector`, so the arm passes
with no disk attached and a 16 MB fixture was kept out of the depot.

**Regression.** `foreword-fat32` and `install-to-drive` both pass. The latter
first read as a failure and was NOT one: it carries `.disk` AND `.disk2`, and
a hand-run passing only `-DiskFile` measures the invocation rather than the
code (L-SIDECAR).

**NOT COVERED, and it is the same gap named against item 12.**
`fat32-find-chain-end` (`:175`) recurses through `fat32-next-cluster` with no
fuel and no cycle guard, reached from `fat32-alloc-chain` on the WRITE path. A
chain that cycles within valid range still spins forever. These guards bound
the cluster ADDRESS, not the walk; the walk is a different mechanism and is
unmeasured.

## The Foreword Cluster Guard (`codex/test/fat16-cluster-guard`)

WORKS-29's class one layer down, in the chapter the COMPILER reads its own
source through. `fat16-next-cluster` turned a cluster number read off the
volume into a FAT sector address by arithmetic alone, with no bound; the
bound it needed, `fat16-last-cluster`, was already in the chapter and used
only by the allocator. The guard answers 65535 -- end-of-chain in this
format's own width -- so a bad cluster truncates a file instead of reading a
foreign sector, which is what the callers already handle.

**The guard is inside `fat16-next-cluster`, not at the walkers, and that is
the point.** There are about twenty call sites in the chapter and four
outside it: `opening.codex` (:1849), `AgentBundle` (:348), `SinkLadderProbe`
(:137, :153) and `GopFiles`. One guard covers every one of them without
touching a caller.

**THREE ARMS THAT PROVED NOTHING, recorded because each looked like
evidence.** This is the useful half of the entry.

1. **`build/sink-arm.ps1 -Only pass` is blind to this chapter.** It walks
   5,364 clusters back through `fat16-next-cluster` and is the best-shaped
   test in the tree for this change -- but it runs the PREBUILT
   `build/boot/sinkladder.img` and never compiles. Its staleness guard
   (lines 58-59) checks `SinkLadderProbe.codex` and `MetalLadder.codex` and
   nothing in the foreword, so a `Fat16.codex` change passes it silently.
   Sabotaging the bound to 100 against a 5,364-cluster chain left it
   reporting `verified`. **It also reported `verified` after the build that
   was meant to feed it had failed outright**, which is the exact failure its
   own comment at lines 54-57 says it exists to prevent, one dependency level
   up.
2. **The eight disk-backed `fat16-*` arms do not reach the chain walk.**
   `fat16-alloc`, `list`, `write`, `subdir`, `overwrite`, `dirgrow`, `mkdir`
   and `source-cr` all pass with the guard REFUSING EVERY CLUSTER. The FAT16
   root directory is a fixed sector region rather than a chain, and every file
   on those fixtures fits in one cluster, so the walk is never entered
   (L-NAMED).
3. **The first sabotage was too gentle to move a row.** Making the guard
   refuse everything makes `fat16-next-cluster` answer end-of-chain, and on a
   one-cluster chain that is indistinguishable from the true answer. A
   sabotage moving fewer rows than predicted is telling you the code is
   shaped differently than you wrote down (L-SABOTAGE).

**So the arm calls the function directly.** On the `fat16-alloc` fixture
(30,414 clusters, highest valid 30,415) it prints the bound beside the
verdict and then asks `fat16-next-cluster` for three clusters outside it:

```
clusters 30414 last 30415
ok c0=no c1=no c2=yes last=yes past=no big=no
next-c2-is-a-number True
next-past 65535
next-big 65535
next-low 65535
```

**Ablated** (the guard bypassed in `fat16-next-cluster`), `next-past` and
`next-big` both become **0**: the unguarded read went outside the FAT and
came back with zeros, which on a populated volume is an arbitrary cluster
number and a walk that continues into foreign sectors.

**`next-low` does not move and is therefore not evidence.** Cluster 1's FAT
slot legitimately holds 0xFFFF (the media descriptor), so that row reads the
same guarded or not. It is kept because it pins the low side of the range,
not because it discriminates; anyone reading this arm should know which of
its rows can fail.

## The Stub Panic That Did Not Halt (`build/cdx-to-pe.ps1`, `AllocPanic`)

Found 2026-08-15 (fester) by firing the negative control for the A8 allocation
question, and it is the whole argument for rule 6 of the sitting sheet: an arm
that has only ever been seen agreeing has not measured anything.

**The defect.** `AllocPanic` emitted `mov al,c` / `out 0x3F8` / `out 0x2F8` /
`hlt`, with no `cli` and no halt loop. Every one of its five sites -- `C` code
pages, `H` heap pages, `G` GDT page, `B` a null heap base, `V` the framebuffer
straddling the record arena -- runs BEFORE ExitBootServices, where the firmware
timer is live. A bare `hlt` therefore RESUMES on the next tick and execution
falls through the panic into code that assumes the allocation succeeded.

**What that looked like on the wire.** Forcing a refusal (a 2 GB heap in a
640 MB bed) printed `s v c H V h g` and then an X64 `#GP` dump: the `H` panic
fell through, the `V` panic fired on the garbage heap pointer and fell through
too, the progress marks for the heap and GDT printed anyway, and the machine
died in the firmware's exception handler. **A board refusing the allocation
would have shown a crash, not a refusal**, and `CurrentPlan` told the reader
in as many words that "the stub raises `H` if refused". That is L-MISROUTE:
a sheet that pre-assigns the meaning of a failure the tool does not actually
produce.

**Why it had never been seen.** reek's `V` halt earlier the same day looked
like a clean stop because that one fires AFTER ExitBootServices, where `IF=0`
and the `hlt` sticks. The same instruction is terminal on one side of EBS and
transparent on the other, which is exactly the kind of state a pass/fail arm
cannot distinguish (L-STATES).

**The fix and its cost.** `cli`, `hlt`, `jmp` back to the `hlt`. The panic
block grows 13 bytes to 16, so the four displacements that skip it move with
it: `jz`/`jnz`/`jae` +13 become +16 and the outer `jae +31` becomes +34. It
cannot be made size-neutral without deleting the second UART write, which is a
deliberate fallback (the operator has whatever port they happened to attach).
**So the layout of every path shifts, including `-EntryStart`, whose bytes red
had hash-pinned for A5.** Measured: all three arms change hash. That is a real
cost and it is the L-DECODE class -- a code-size change has moved a store onto
a page boundary in this tree before -- so the next A5 flight carries stub bytes
that have not flown.

**The arms, both directions, on the exact bytes.** Positive: `-AllocPages
131072` in the default bed prints `s v c h g x o` before and after, so a
successful boot is unchanged. Negative: the same build at `-AllocPages 500000`
in a 640 MB bed printed `s v c H V h g` + `#GP` before and prints `s v c H`
and stops after. **A first attempt at the control was invalid and is worth
recording**: 512 MB requested in a 384 MB bed killed OVMF itself before the
payload ran, and at 640 MB the firmware granted the 512 MB anyway, so two
runs said "green" while proving nothing about refusal.

## The Agent Message Guards (`codex/test/apps/agent-msg-truncated`)

Track D item 3, and unlike item 2 this one is genuinely reached: `trust-recv` is
called from `TrustNode`'s `node-recv-loop`, and what it decodes goes straight to
`node-eval-msg`, which builds a `PolicyContext` out of the message and hands it
to `eval-policy`. These are peer bytes deciding a policy question.

**Two defects, and the first one is a remote kill.** Every `decode-*-body` in
`TrustTransport` chains `next-offset` off a peer-supplied length, and eight of
them then read a tag, direction or flag byte with a bare `list-at bs off` rather
than `frame-byte-at`. A body that ends before that offset is an out-of-bounds
index. Measured 2026-08-15: `decode-agent-msg tag-propose []` dies `!EXC=06`
against the code as it stood. The repair is reek's drop-in from `RepoProtocol`,
applied to all eight sites.

**The second defect is what the crash was hiding.** With the reads made safe, a
truncated body no longer faults; it decodes to a message with empty fields, and
`eval-policy` is asked about it anyway. `MessageFraming`'s own prose already says
this in the general case: handing back a truncated field is not safe, it is only
better than the crash. So `decode-agent-msg-checked` answers a `valid` flag and
`trust-recv` reports `has-message = False` when it is clear, which
`node-recv-loop` already handles by moving to the next peer.

**Validity is decided by a round trip against our own encoder**, not by a second
walk of the format. `amsg-bytes-equal (encode-agent-msg-body msg) body` cannot
drift from the decoder the way a hand-written length model would, it needs no new
record on any of the seventeen body decoders, and it refuses a body that is not
the canonical encoding of what it decoded to, which is the non-malleability
property this design names in section 4 and gets here for free. The one question
it raises and answers: it does NOT refuse the "trailing bytes are ignored"
behaviour `work-serve` documents, because the transport's length prefix bounds
`rr.body` before any of this runs, so trailing STREAM bytes never enter the body.

The tag check is separate and needed on its own. `decode-agent-msg`'s final
`else` decodes **any** unrecognised tag as a `WorkReply`, so a handshake tag
carrying a well-formed work-reply body round-trips perfectly and would be
accepted. `agent-tag-known` enumerates all seventeen.

**Twelve arms, three of them positive, four ablations each moving only its own
rows.** `workreply-tagged` and `unknown-tag-same-body` are the same bytes under
two tags, which is what isolates `agent-tag-known` from the round trip. Reverting
`frame-byte-at` kills the guest `!EXC=06`; removing the round-trip check flips
all six truncation rows to `valid=True` and leaves the unknown-tag row refused;
removing the tag check flips only that row; removing the hello checks flips only
the two hello rows.

`decode-hello-body-checked` closes the residual the handshake guards left open:
`trust-respond-hello` now refuses a hello whose key or nonce field does not fit,
or which has no room for the difficulty word, instead of feeding a garbage
`HelloMsg` to `hs-receive-hello`.

## The Fact Store Length Guards (`source-def-wire-guard`, `factdisk-hostile-head`)

Track D item 15, and it is reached: `opening.codex:2101` calls
`store-read-bundle` whenever the mode carries `store`, so these are bytes off a
disk deciding what the compiler admits as a quoted work. Two lengths, two
different failures, one test each.

**The wire length is a guest kill, and the runtime is why.** `sdw-decode` ends
with `substring line (p9 + 1) clen` where `clen` is the record's own ninth
field. Two primitive behaviours make that fatal rather than merely wrong, and
both were measured on 2026-08-16 rather than assumed: **`substring` faults
`!EXC=06` on a length past the end of the text AND on a negative one**, and
**`text-to-integer` answers a NUMBER for text that is not one** -- `"abc"` is
1511 -- so a corrupt field arrives as a plausible large length and never as a
zero. Against the code as it stood, `source-def-wire-guard` reached arm 4 and
died with the hostile 99 sitting in R15.

The bound is `clen < 0` or `clen > text-length line - (p9 + 1)`, and a length
SHORTER than what remains is still admitted. That is deliberate: the encoder
always writes the exact length, so only a longer one is provably malformed, and
refusing the short case would be a new refusal rather than a bound. Arm 2 pins
it (`clen` 4 against 12 characters still answers `[demo]`), which is what stops
a later tightening from being mistaken for a fix.

**It SUBTRACTS, and it shipped adding.** For one day the bound was
`p9 + 1 + clen > text-length line`, which a nineteen-digit length field walks
around: the sum wraps negative, the comparison reads false, and the record is
admitted. "A Bounds Guard That ADDS Can Be Overflowed" below is the whole class
and carries the measurement. **Arm 9 is the arm for it here, and why arms 4 to
7 could not have caught it is the part worth keeping**: a length past the end
and a negative length are both hostile and both kill the guest unguarded, but
neither WRAPS, so a bound that fails only on overflow passes every one of them.
An arm that probes a guard with plausible hostile values does not test the
guard's own arithmetic. Ablated, restoring the additive form lets arms 1-8 pass
and kills the guest at arm 9.

**The log head is an allocation, and the two hostile fields are ONE defect.**
`fd-log-head` reads a u64 off the superblock and it was the only ceiling on two
things: `fd-fold` visits every sector up to it, and `fd-fold-entry` admits an
entry's span on `sector + nsec > end-sec` alone -- where `end-sec` IS the head.
So with an honest head of 5 an entry claiming 4,000,000,000 bytes is already
refused by the code as it stood; only a head that is itself a lie admits it.
That span is 7,812,501 sectors ending at 7,812,504, which clears a head of
8,000,000, and `fd-entry-content` then asks `alloc-bytes` for 4,000,000,512
bytes. **Ablated, that arm prints `OUT OF MEMORY` with the heap frontier at
0xeecb3850 and the VM exits -1.**

The ceiling is `block-sector-count`, because the medium knows better than the
superblock does: a store cannot extend past the disk holding it. A count of
zero or less is DriveManager's no-drive answer and stays a pass, on a path that
cannot reach the defect anyway -- with no disk the superblock has no magic,
`fd-gen` answers -1 twice, and the walk ends before its first sector.

**An incredible head refuses the WHOLE store rather than the prefix that fits,
and that posture is val's** from the parallel work on this item: a superblock
that cannot be true is not one to trust part of. It also keeps the per-entry
span check honest, since that check is measured against the same head. **The
positive control moves with it**: it is no longer "the good entry still
arrives" but "the bundle the caller handed in is given back intact", because a
refusal that also dropped the caller's offered works would pass a count check
and quietly cost the compiler its quotations. `factdisk-read` is the other half
of the control and is deliberately untouched -- same reader, honest superblock,
all three entries still walked.

**A derived bound beats a constant here, and that is not a style preference.**
val's parallel change ceilinged the head at `fd-max-log-sectors = 262144`. A
constant cannot know how big the disk is: a head of 200,000 sits under it, so
on a 128-sector fixture the store is accepted and the walk runs ~200,000 reads
past the end of the medium. `block-sector-count` moves with the fixture and
cannot be tuned past.

**Still open, and it is val's to land:** a per-record cap. A large medium still
permits one record to allocate most of it, which the head bound does not touch.
val's `fd-max-content-len` (4 MB, shipped without an arm because reaching it
needs a store image over 4 MB) is the answer to that and is not folded in here.
This section previously carried a paragraph of mine arguing the cap could not
be chosen because the format has never stated a maximum; that was
rationalisation and it is deleted rather than defended.

**The codegen citation belongs to blu and val**, who both found
`emit-substring-bounds` at `X86_64Builtins.codex:666-681` while this lane was
still measuring the crater from the outside.

**Both fixtures are minted from the specification, not written by the writer
under test** (`build/mint-factlog-fixture.ps1`, `-Hostile` for the second),
which is the `BrotliBeatsOpus` discipline the original fixture was built under.
The hostile one moves exactly two fields and leaves entry 2's record text
well-formed, so the refusal under test is the sector-span bound and not the
wire decoder's. Two controls guard the mint itself: the minter with the
`-Hostile` code added still reproduces the good fixture byte-for-byte
(`21AF003D625E3938`), and the hostile image it emits is byte-identical to the
same two fields patched into the good one by hand. **Entry 3 is the positive
control** -- untouched, and it must still arrive, which is what makes this an
instrument rather than a crash that stopped crashing. Entry 2 must NOT arrive,
because its own header is the thing that is lying.

**The residue, and it ships without an arm (val, 2026-08-16).**
`fd-head-credible` bounds the walk and every entry's allocation, but it bounds
them by the SIZE OF THE ATTACHED MEDIUM, which is still a number this code did
not produce. On the store images this reader is pointed at, a megabyte or a
few, that is already tight; on a large medium it is not, and a superblock with
a credible head plus one entry declaring a gigabyte asks `alloc-bytes` for a
gigabyte on a guest that has three. `fd-max-content-len` caps one entry at
4 MB independently of the medium, against a widest legitimate value of one
chapter (the largest in the tree measured 735,952 bytes), and an over-long
entry strides on exactly as an entry that will not fit the log already does.

**Ablating it moves nothing any test can see, and that is stated rather than
hidden.** Reaching it needs a store image over 4 MB, because on anything
smaller the medium ceiling refuses the entry first, so the arm would cost a
multi-megabyte fixture to exercise a guard whose whole purpose is the case the
fixtures cannot reach. It is kept on the campaign's rule -- we do not size an
allocation on a number we did not produce -- and not on a measurement. What IS
measured is that it refuses nothing legitimate: `factdisk-read`,
`factdisk-hostile-head`, `foreword-source-def-wire` and
`build/test-quote-from-store.ps1` (`sorted=105`) all pass unchanged with it in.

## The GGUF Bounds Guards (`codex/test/apps/gguf-hostile`)

Track D item 16. `codex/foreword/ai/Gguf.codex` reads every field with a bare
`list-at`, which traps out of range, so a length or count taken off a model
file decides where a read LANDS and a wrong one halts the machine instead of
answering badly. Not seed-affecting: only `apps/works/AgentBundle.codex` cites
the chapter, and no compiler chapter does.

**`gguf-fits` was itself the additive class, found by item 20 and fixed
2026-08-18 (reek).** The guard every other guard in this chapter is built on
was `(off + n) <= list-length data`, with `off < 0` and `n < 0` refused in
front of it. Both operands come off the file as u64s, so neither is negative
and the SUM is: measured on the seed by asking the predicate directly, `off`
9223372036854775800 with `n` 8, against a ten-byte list, answered **True**, and
so did `off` 16 with `n` 9223372036854775800. The read that follows lands at
9.2e18 and traps. It is now `n <= list-length data - off`, which cannot wrap
because both sides are bounded by a length that exists.

The probe is six rows and its discriminator is `far-off`: `off` 1,000,000 with
`n` 8 answers False both before and after, so the two True answers were the
arithmetic wrapping and not a guard that refuses nothing. After the change only
those two rows move; `sane-in`, `sane-out`, `neg-off` and `far-off` are
unchanged. `gguf-hostile` and `gguf-test` are byte-identical, and
`build/gguf-foreign-test.ps1` still parses four real llama.cpp models up to
3,184 MB with the answers re-derived on the host.

**The guard has no standing arm and that is deliberate**, not an oversight:
the standing instruction is not to add tests to the gate or the battery, and
`gguf-hostile` is in the battery. The six rows above reproduce it in a
scratch chapter citing `AI chapter Gguf` and calling `gguf-fits` directly,
which is the same shape as the `repo-has` arm. Whoever is next told to grow
this arm should add them.

**The path is reached and the shape of the caller makes it worse.**
`ab-window-step` (`AgentBundle.codex:297`) calls `gguf-tensor-info-offset` on a
DELIBERATE prefix of the file, growing the window until the metadata block
fits. Handing the parser a short buffer is not the pathological case here, it
is the normal one, and the parser answering `-1` is what the loop is built on.

**Two guards existed and both checked the wrong thing.** `gguf-parse-header`
refused a file under 20 bytes and then read bytes 16..23 of a header that
`gguf-header-bytes` says is 24, so a 20-byte file passed the guard and trapped
inside it. `gguf-skip-metadata` checked the START offset against the data
length without checking that the read AT that offset fits, so a file four bytes
longer than a header walked straight off the end. And the two walkers over the
same block disagreed: measured against the unfixed chapter, the file that
`gguf-tensor-info-offset` correctly refuses with `-1` is the same file that
kills the guest through `gguf-metadata-text`, because `gguf-md-scan` carried no
offset check at all.

`gguf-fits data off n` is now asked before every read at a file-supplied
offset, and each caller refuses on the channel it already had: `-1` for a byte
count, `""` for a metadata lookup, `gh-valid` or the new `gti-valid` for a
record. `gti-valid` is additive in the sense CurrentPlan describes for
`MessageFraming`: a caller that ignores it reads exactly the fields it read
before, and a refused entry answers the empty name and zero dimensions it would
have answered for a file of zeroes. What it no longer does is trap.

**The string-array walk needs no fuel cap and the chapter says why.** Every
element it accepts consumes at least its own eight-byte length word out of a
finite file, so the fits test ends the walk after one iteration per eight bytes
however large the declared count is. That is a different answer from the fuel
`Fat16` and `Fat32` need, and it is different because a FAT chain can revisit a
cluster while this walk only moves forward.

**Sixteen rows, three positive controls, ELEVEN guards each ablated
separately, and every one killed the guest at exactly its own row:**

| ablation | row it moved | | ablation | row it moved |
|---|---|---|---|---|
| header 24 back to 20 | `hdr-20` | | array header 12 | `kv-arrhdr` |
| `md-scan` offset 8 | `md-none-t` | | array-string 8 | `kv-array` |
| `md-scan` vtype 4 | `kv-keyln-t` | | tensor name 8 | `ti-short` |
| `kv-bytes` offset 8 | `kv-short` | | tensor ndim 4 | `ti-name` |
| `kv-bytes` vtype 4 | `kv-keylen` | | tensor shape span | `ti-ndim` |
| string length 8 | `kv-strlen` | | | |

Two arm-design notes worth keeping. The rows compute their values INSIDE the
print statements through helpers, because the first version bound them in
`let`s above the `act` block, every binding was evaluated before the first line
printed, and the run died with no output at all and named its row only through
`RSI=0x14`. And `md-none` and `md-none-t` are the same file through the two
different walkers, which is what caught the disagreement between them.

**Positive controls.** `gguf-test`, `ai-gguf`, `bundled-agent`,
`foreword-all-compile` pass unchanged, and `build/gguf-foreign-test.ps1` parses
four real llama.cpp models off this box, up to 3,184 MB, checking version,
tensor count, kv count, architecture, tensor-table offset and first tensor name
against an independent host parse. That harness is the one that matters: a
tokenizer vocabulary is an array of tens of thousands of strings, which is
exactly the shape the string-array guard sits in and exactly the shape a
generated fixture never has.

**The dequant path was outside the first pass and was still a guest kill.
Found by reek, verified here against main's bytes before anything was
changed.** The metadata guards do not reach it because neither loop takes a
length off the file: the element count arrives from the caller, and the caller
gets it from the tensor shape, which IS off the file. Measured on the landed
chapter: 32 elements from a one-block buffer answers 32 values, and 64 from the
same buffer dies `!EXC=06`. `gguf-dq8-loop` and `gguf-dq4-loop` now stop on the
first block that does not fit, which also covers `gguf-dq8-block`'s values and
`gguf-dq4-block`'s nibbles, since the whole 34 or 18 byte block is checked
before either is entered.

That one is a CLAMP where the rest of the chapter refuses, and the split is
blu's: the count decides how many values come back, not where a read lands, so
a short buffer answers the values it actually holds. Rows `dq8-fit` and
`dq4-fit` are the positive controls that keep the well-formed case honest, and
ablating either guard kills the guest at its own row and nowhere else. The arm
is now twenty rows and **thirteen guards, each with a row that isolates it.**

`AgentBundle.ab-parse-model` also now checks `gti-valid` rather than reporting
a tensor name nothing validated. The refusal channel landed in the first pass
with no caller asking for it, which is L-UNCALLED sitting in our own work.

## A Bounds Guard That ADDS Can Be Overflowed (the whole class)

Found by blu 2026-08-16 in reek's item-15 `sdw-decode` bound, one day after it
landed. It is worth its own section because it is not one defect: **71 sites in
the tree compare `a + b` against a length, and 34 of those have a non-constant
second operand.**

**The shape.** A guard reads a length off the wire, checks it is not negative,
then asks whether the field fits:

```
    else if p9 + 1 + clen > text-length line then None
```

That is wrong whenever `clen` can be large enough to wrap the sum. Measured
2026-08-16 on the seed: `text-to-integer` of a 19-digit field answers **i64
max**, and `46 + 9223372036854775807` is **-9223372036854775763**. A negative
sum is under any length, so the guard answers False and admits exactly the
record it exists to refuse. blu ran it against main 15576 and the guest died
`!EXC=06`.

**The fix is to subtract, not to add a second check.** `clen > text-length line
- (p9 + 1)` cannot wrap, because both operands are already bounded by the
length of a thing that exists. A range check whose own arithmetic can overflow
is not a range check.

**`clen < 0` is not enough and that is the trap.** The original guard DID
refuse a negative length, and a reader checking it sees a bounds test with a
sign test in front of it and moves on. The value was bounded; the arithmetic
was not.

**Two boundary arms are still missing from `source-def-wire-guard`**, found
2026-08-16 while clearing a superseded standalone draft of this test off disk.
Arm 4 `over` is a FAR-over length and arm 9 `overflow` is the wrapping one, so
nothing covers **over-by-one**, which is the value a fencepost error produces
and the only one an off-by-one in `avail` would admit. Nothing covers content
that CONTAINS the field delimiter either. Both were written and neither landed;
whoever next touches the chapter should add them.

**`sdw-decode` itself was made subtractive at main 15641** (this said 15614
until 2026-08-16, a transposition that resolves to no changelist at all), and
this is worth
dating because for a day this section prescribed a fix the originating record
did not yet have: the class was written up, `repo-has` was changed, and the
site the class was found in still shipped the additive form. Its arm is arm 9
of `source-def-wire-guard`. With it landed the untouched remainder below is 32
rather than 33.

`repo-has` in `RepoProtocol` had the same shape and was made subtractive in the
same pass. **No live path was demonstrated there and the section says so**: all
five call sites pass a constant `n` of 1 or 8, so nothing reachable overflows
it. It was changed anyway, because the additive form is only safe while nobody
adds a call site with a length off the wire, and re-proving that at every
future call site is a worse deal than never needing to. Its arm is in
`codex/test/apps/repo-frame-truncated`, four rows asking the predicate
directly: a huge offset, a huge length, both huge, and a sane case that must
still pass. **Ablated to the additive form, `off` and `both` flip to admitted
while `sane` stays green**, which is the discrimination -- the guard starts
saying yes to a read of eight bytes at offset i64 max.

**The remainder IS swept now (reek, 2026-08-20), every one is safe, and the
reason is structural rather than site-by-site luck. WIDTH decides it, not
shape.** `a + b > len` can only wrap when an operand can approach i64 max.
Every remaining addend in the tree is one of three things and none of them
can:

- a literal constant (`off + 4`, `i + 1`, `12 + `, `35 + `);
- a FIXED-WIDTH field read off the wire -- u8, u16, u24 or u32 -- which
  ceilings at 2^32-1, nine orders of magnitude below the wrap;
- `text-length` or `list-length` of an object that EXISTS, which is bounded
  by memory.

So the rule for new code is one line, and it is cheaper than re-auditing:
**the additive guard is unsafe exactly when an addend is UNBOUNDED, which in
this tree means a decimal parse (`text-to-integer`) or a value already
i64-wide.** That is precisely what `sdw-decode` did, and it is why that site
and no other was exploitable.

Judged individually, with the width named: `Asn1` long-len (`n > 4` refused
above it) and `asn1-mk` (`asn1-be` over at most 4 bytes); `DtlsMessage`
(`dtls-rd24`), `Dtls` plain-decode and `DtlsHello` ext-scan and cookie
(16-bit), `DtlsHello` session-id (a single byte at index 34); `TlsEndpoint`
(24-bit, built as `*65536 + *256 +`); `TlsCert` (`ctx-len` is
`list-at body 0`, one byte); `Tls` record-decrypt (16-bit); `HttpFetch:406`
(16-bit). `CCE`'s `utf8-assemble` takes `n` from three call sites that pass
2, 3 and 4 literally. `Pattern`, `TextScan` and `TextSearch` add a real
`text-length`, and their callers pass 0, a difference of two real lengths, or
a loop index already bounded by the haystack -- checked, because a caller
handing one of those an attacker-controlled offset is the one way this
conclusion could have been wrong.

**What is NOT claimed.** This is the census's population, not a proof over an
enumeration matching the 71/34 above; those counts were never reproduced here
and are not the thing that matters. The rule above is, and it does not drift
the way a count does.

## The Heap Guard Page (`build/guard-page-test.ps1`)

Two arms against the 2 MB unmapped page below the boot stack's reserve: FIRE
parks the frontier just under it and expects `OUT OF MEMORY`, CONTROL parks
well clear and expects the program to survive. It is not in the standing gate;
run it when you touch the guard, the allocator, or `__out_of_memory`.

**The message now names which side ran away.** Measured 2026-08-15 on the SUT,
the FIRE arm prints

```
OUT OF MEMORY
SP=00000000bdfffec8 HEAP=00000000b9e00028
```

`HEAP` is 0x28 past the guard page address the harness computes independently
(3118465024 = 0xB9E00000), so the reading is heap-ran-away with the stack 67 MB
clear. Before this the line was bare `OUT OF MEMORY` and `OperatorsManual`
records that it "has repeatedly been read as heap exhaustion" when it fires
equally for a stack descending into the heap.

**A third arm, LEAP, was RETIRED 2026-08-15, and what it covered is now
uncovered.** It ran the whole-compiler `-IrCce` emit on the premise that the
emit overruns the guard. That was true on 2026-08-04 and is not true now:
measured 2026-08-15, the emit COMPLETES, writing a 15.7 MB `leap.ir` at a peak
frontier of 1,305,881,760 bytes against a guard at 2,974 MB, 1.6 GB of
headroom. The arm expected `OUT OF MEMORY`, got neither that nor a crash, and
took its third branch, so **a healthy tree reported FAILED**.

The script's own header already carried the refutation -- "the compiler's own
peak frontier is ~1245 MB against a guard page at ~2974 MB" -- and that 1245 MB
IS the retired arm's peak, stated four lines above the claim it contradicts.
The arm had lost its subject rather than caught a regression (L-INSTRUMENT).

**It was retired rather than kept red, because the signal was inverted.** The
arm passed ONLY when the emit overran, so red was the healthy state and green
would have meant the compiler got more expensive. A permanently red arm also
trains the reader to discount reds, which is the argument `Build.md` makes for
keeping the handwritten-scripts inventory report-only rather than a gate.

**Re-aiming was tried and rejected on measurement.** Lowering `-MemMB` brings
the guard down to the workload -- at 1280 MB it sits at 1,272,971,264, below
the peak -- and the emit does then answer `OUT OF MEMORY`. But at that size the
compiler legitimately needs more memory than exists, so the trip is correct
behaviour rather than a runaway being caught, and the arm cannot distinguish
them. That is the defect that got the `-Decks 450` arm rejected on 2026-08-04:
at `-Decks 450` R10 lands above RSP immediately and the pre-existing prologue
`cmp rsp, r10` catches it, so it prints `OUT OF MEMORY` against a compiler with
no guard page at all. The 1280 MB run also printed no `SP=`/`HEAP=` line, so it
was never even shown to have come through `__out_of_memory`.

**The hole is closed: the WALK arm (root, 2026-08-20) is the genuine
allocation walk.** What LEAP got wrong is what shapes it: instead of a real
workload whose cost can drift back under the guard, `build/guard-page-walk.codex`
is a runaway by construction -- one ~256 KB `__str_concat` per step (far under
the 2 MB page, so it cannot step over the hole) from the heap base until the
page catches it, under a ~6 GB fuel cap whose exhaustion prints
`WALK COMPLETED` and fails the arm. Its subject cannot drift: the walk always
overruns at normal memory because overrunning is what it is. The arm also
checks WHERE the trip landed -- the `HEAP=` address must fall inside
`[guard, guard + 4 MB)`, so an `OUT OF MEMORY` from a ceiling firing early is
a failure, which is the discrimination the 1280 MB re-aim could not make.
First flight on the seed: caught at `HEAP=3118654960`, 185 KB past the guard
at 3,118,465,024, after a ~2.9 GB march that exercised the allocation path
and the demand mapping under it together. The probe lives in `build/` beside
`guard-page-probe.codex` for a sibling reason: it never exits on a healthy
tree, so the battery's run-and-compare model cannot hold it.

**The harness could not run at all until 2026-08-15, on any box where
`QEMU_BIN` was unset.** `build/vm-config.ps1` used `$root` as a `foreach`
variable in its QEMU side-load discovery, and a dot-source runs in the caller's
scope, so it overwrote the caller's `$root` with `C:\` and the script died on
`Cannot find path 'C:\build\guard-page-probe.codex'` before its first arm. Of
the 19 build scripts that dot-source `vm-config.ps1`, this was the only one
affected, because it is the only one that assigns `$root` BEFORE the dot-source
and reads it after; `test-disk-compile.ps1` assigns after and overwrote the
leaked value. Renamed to `$qroot`. The general shape is worth keeping: **a
dot-sourced script shares the caller's scope, so every loop variable in one is
part of its interface.**

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
structural and the allocation verifier. **A harness reads it, and that is the
whole point of the pin**: without a runner it is a snapshot of one past run
going stale in silence.

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
take it. **The discriminator is not "did the number change" and not "is the
code current": it is whether the new values are verified by something OTHER
than the run that produced them.** A documented true value, a hand-computable
check, a stated intent -- any one will do. Two calls the same day went opposite
ways on it: `neural-test` re-minted correctly, because the old goldens had been
minted while the activations were wrong in SHAPE and the new ones were
checkable by hand (`sigmoid(1) = 0.7311 -> 731`); `files-parse` would have
baked a defect in, because the new panel geometry had never been confirmed as
intended. Absent an independent check, confirm the new behaviour is INTENDED
before re-deriving anything from it, and bisect per file rather than assuming
one cause covers every golden that moved. Register assignment legitimately moves on any allocator or selector
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
unfired guard is worth exactly what no guard is worth, and `accum-at-capacity`
sat uncalled for months while being counted as protection. CL 8867 proved only
the *absence of false positives* (battery identical to
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

206 tests in `codex/test/errors/` verify that the compiler rejects
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

## The Config-Descriptor Clamp (`codex/test/apps/usb-cfg-total-guard`)

The USB half of the audit blu started on the net stack. wTotalLength is two
bytes at offsets 2 and 3 of a configuration descriptor, sixteen bits wide, and
the DEVICE chooses them. Three identical unclamped readers walked to it over a
256-byte buffer that one control transfer had filled with at most 255 bytes, so
a device claiming 0xFFFF sent every walk in `GopUsb`, `GopUsbKbd`, `GopUsbMsc`
and `CamCapture` through 64 KB of adjacent heap, acting on anything shaped like
an interface descriptor. It is the enumeration path, so it runs on every device
present at boot and on anything plugged in later. One `usb-cfg-total cfg cap`
in `GopXhci` replaces all three.

**A clamp and not a refusal, because a config larger than one transfer is
legal.** RFC-768's zero-checksum rule has the same shape here: the guard must
not turn ordinary hardware away. What the clamp says is only that the walk
covers the bytes we actually fetched, and `cap` is what was REQUESTED of
`usb-get-descriptor` rather than the allocation size, because the tail of the
allocation was never written by anything.

**The descriptors are built by hand.** codex-vm's emulated devices answer
honestly, and a harness that can only produce well-formed input cannot tell a
working guard from an absent one.

**`poison` is the arm that carries the finding.** 512 bytes, a legal config
with one mass-storage interface in the first 32, and a second interface
descriptor -- boot HID, which the walk is looking for -- planted at offset 300,
past any byte a fetch could have delivered. The gap between them is filled with
two-byte class-specific descriptors rather than left zero: a zero length byte
terminates every walk in the tree, so a zeroed gap would stop the unclamped
walk before it reached the plant and the arm would pass for the wrong reason.
Live heap under a running driver is not zeroed, and that is the case being
modelled.

| line | clamped | ablated (`if False`) |
|---|---|---|
| `liar total` (claims 65535) | 255 | 65535 |
| `poison total` (claims 400) | 255 | 400 |
| `poison hid-clamped` | **no** | **yes** |
| `big total` (claims 300) | 255 | 300 |
| `cap-honoured` | yes | no |
| `honest`, `runt`, `zerolen` | unmoved | unmoved |

`hid-clamped` is the line that matters: ablated, the walk reports a boot
keyboard interface the device never sent, read out of memory past the fetch.
`hid-unclamped` sits beside it in BOTH runs and is always `yes`, so the arm
carries its own positive control and cannot rot into a tautology.

Three acceptance controls are unmoved by the ablation, which is what says the
five lines that move are measuring the clamp and not the harness. `honest` is
the one that fails first if a future clamp refuses ordinary descriptors; `big`
pins the legal case of a device whose config exceeds our fetch, and it must
still find the interface that IS present.

**The live path is the other control.** With the clamp in, the desk enumerates
the emulated stick and the Files pane lists the ESP (`EFI`, `SOURCE.SRC`,
`CODEX.CDX`, `SRC`), so the real xHCI-to-descriptor-walk-to-MSC-to-FAT chain is
unaffected. The Monitor pane still reads `keyboard USB HID   mouse yes`.

**The other defect class was already closed on this path and is worth saying
so.** A Command Status Wrapper is checked on the way in: `msc-finish` requires
the `USBS` signature, requires the tag to echo the one `msc-stamp-cbw` sent,
and treats a phase error as a reset-and-fail. An MSC reply for the wrong
command therefore cannot be read as the answer to this one.

## The Truncated Repository Frame (`codex/test/apps/repo-frame-truncated`)

The caller half of blu's framing work, handed over after the clamp landed
(main 15345). `frame-next-offset` clamps a decoder's next offset to
`list-length bs`, which makes it a valid SLICE BOUND and **not a valid index**.
Two sites in `apps/works/RepoProtocol.codex` then indexed at exactly that
offset with a raw `list-at`: `decode-annotation:323` and
`decode-verdict-kind:399`. On a truncated frame both are out of range by
exactly one. Both are `frame-byte-at` now, which answers 0 past the end.

**The boundary is constructed, not approximated.** With one-character fields
the annotation encoding opens with three length-prefixed texts at five bytes
each, so cutting the buffer at 15 leaves `tr.next-offset` equal to
`list-length`. The arm asserts that (`boundary cut=15 len=15 tr-next=15`), so
an arm that stops being the boundary case says so rather than passing quietly.

**The ablated call is NOT carried inside the arm, and that is a difference from
the config-descriptor clamp above.** There the unguarded read stayed inside a
larger allocation and could sit beside the guarded one on every run. Here it
faults, so it kills the run instead of printing a wrong answer. Run as a source
edit at site 323, restoring the raw `list-at`:

```
!EXC=06 RIP=0000000000112c37 ... RDI=000000000000000f RSI=000000000000000f
```

`0x0f` is 15, the same index the guarded arm asserts, and the guest dies before
printing any of the seven lines. That is the whole finding: the fault lands on
the boundary the test names.

| line | guarded | ablated |
|---|---|---|
| all eight | printed | nothing printed, `!EXC=06` at index 15 |

**The depth is swept, not fixed, and that is fester's point on the handoff:**
no-crash at ONE length passes just as well on a build where the guard was never
reached. Every cut from the empty list to the whole 34-byte frame is decoded and
its kind recorded as one letter:

```
sweep 0..34 = rrrrrrrrrrrrrrrrDDDDDDDDDDDDDDDDDDD
```

Thirty-five depths, one discontinuity, and it sits exactly where the boundary
arm says it should: `r` while the kind byte is absent and `frame-byte-at`
answers 0, `D` for `Discovery` from cut 16 once the byte is present. A bound
wrong by one moves that transition. A depth that faults takes the run with it
and prints nothing at all, which is what the ablated build does at the very
first depth.

**What the guarded arms pin is safety, not correctness, and the doc should not
overclaim it.** `cut hash=h chapter=c kind=Rationale body=[] ts=0` is a
truncated message being handed on as though it were whole: byte 0 reads as
`Rationale` because that is what `frame-byte-at` answers past the end. Refusing
instead needs somewhere for the refusal to travel, and `FrameTextResult` carries
only a value and an offset. That channel is open work and blu's;
`MessageFraming.codex:29-37` states it. Two sites in the same file that step
past a clamped offset, `decode-annotation:324` and `:336`, are deliberately left
alone for the same reason: they are memory-safe and they yield empty fields.

Not in the class and checked: `:230` and `:464` index internal lists rather
than wire bytes, and `frame-read-le64` is built from `frame-read-le32`, so it
inherited the fix.

**The refusal channel is wired, and the two sweeps together are the claim.**
`MessageFraming` gained `valid` on its result records (blu, main 15375), and
every decode result in `RepoProtocol` now folds it. Two reads had no predicate
to fold, because `frame-fits` answers for a length-prefixed field and these are
raw spans: the annotation's single kind byte, and the eight bytes of a
timestamp. `repo-has` is the predicate for those and lives in `RepoProtocol`,
because the shape is that chapter's rather than the framing's.

```
sweep 0..34 = rrrrrrrrrrrrrrrrDDDDDDDDDDDDDDDDDDD
valid 0..34 = xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxv
```

**They must not switch at the same byte, and they do not.** The kind sweep
turns at 16, where the kind byte arrives; validity turns at 34, where the
annotation is whole. A `valid` that merely tracked the last field read would
switch with the kind, and this pair is what says it does not.

The worst default in that chapter is now readable rather than silent:
`decode-verdict-kind` past the end reads 0 through `frame-byte-at`, and 0 is
`Accept`, so a truncated verdict decoded as an ACCEPTED one. The value is
unchanged, because refusing is the caller's decision, but `verdict at-end
valid=x` says the byte was never there.

`decode-verdict` and `decode-proposal-payload` keep their signatures and are
one-line readers of `decode-verdict-checked` and
`decode-proposal-payload-checked`, so the walk happens once. Two walks that had
to agree about validity would be two sources of truth about the same bytes,
which is what this channel exists to remove.

## The App Sweep Passed While An App Was Broken (2026-08-16)

**A language change broke an app, the gate's app-sweep ran over it, and the
gate was green.** Recorded because the sweep is the fleet's main evidence
that 265 diverse programs still compile, and this is a case where that
evidence was not being taken.

`bounded` became a keyword at blu 16020, which collides with a bare `bounded`
local. `apps/radio/RadioStation.codex:574` had one. Measured against the
seed that CL shipped:

```
apps/radio/RadioStation.codex:574:14: error CDX1023: Expected 'in' after let bindings
apps/radio/RadioStation.codex:574:22: error CDX1022: Expected 'else' in if expression (got '=')
```

The gate that landed it ran `app-sweep` for 153.5 s over **270 units and
reported 265 clean, 5 known-dirty, 0 regressions.** `RadioStationMain` is not
one of the five baseline entries, so it was not masked as known-dirty. root
found the breakage from their own gate under the next token.

**The unit is covered, which is what closes it.** The first reading of this
was "perhaps the sweep does not compile that app". It does:
`test-output\clssweep\_per-unit.csv` carries

```
"apps\radio\RadioStationMain.codex","0","0","False",""
"apps\radio\tests\TestRadio.codex","0","0","False",""
```

and it carried the same clean row in the run where the app provably did not
compile. **The aggregate is identical before and after the fix** -- 270 units,
265 clean, 5 diagnostics, 0 regressions, both times -- so the sweep's answer
does not depend on whether this app compiles. A covered unit reported clean
while broken means the sweep compiled it with something that accepted
`let bounded =`, and nothing that knows the keyword does.

**Where that points, stated as a candidate rather than a conclusion.**
`sweep-app-classes.ps1:48` takes its kernel from
`build-output\bare-metal\Codex.cdx` rather than `build/output/Sut.cdx`, the
path `CLAUDE.md`'s `-Kernel` trap already warns "holds whichever kernel ran
LAST". Measured after the fact that path held a kernel which DOES know the
keyword, so if staleness is the mechanism it depends on phase ordering during
the run and is not visible afterwards -- which is exactly why the fix is to
have the sweep NAME and PIN its kernel the way `compile.ps1 -Kernel` does,
rather than to reason about ordering. root took that fix.

**FIXED 2026-08-16 (root), and the mechanism was simpler than staleness.**
`sweep-app-classes.ps1:48-50` COPIED `seed\Codex.cdx` over
`build-output\bare-metal\Codex.cdx` and then let every `compile.ps1` default
to that path, so the sweep always ran with the SEED. Inside a gate the seed
on disk is the OLD compiler until the seed is converged and installed, which
is every gate whose change moves the seed, and 16020's did: the sweep
compiled 270 units with the pre-keyword compiler and was right that all of
them compiled under it. Not phase ordering, not a race: the script chose the
seed by construction. The fix: the sweep takes `-Kernel` (default
`seed\Codex.cdx` for a hand run) and prints `kernel: <path> [digest]` first;
`build.ps1`'s `app-sweep` phase passes `-Kernel $SutCdx`, through
`BuildScript.codex` so `check-generated-scripts` stays at 0 drift; the
stage0 copy is gone. Ablation, measured: with `let bounded` put back at
`RadioStation.codex:574`, `sweep-app-classes.ps1 -Filter radio -Check
-Kernel seed\Codex.cdx` (seed `0D239110`, post-keyword) answers `CHECK
FAILED: 2 unit(s) regressed` (`RadioStationMain`, `TestRadio`, CDX1022
CDX1023), and the same sweep with the pre-keyword seed `04D14348` answers
`CHECK OK: 2 clean` -- the two answers the old script could not tell apart.

**The reader's lesson is the cheaper one.** A green sweep is evidence that the
sweep ran, not that the apps compile under the compiler you just built, until
the sweep says which kernel it used.

## The Bounded Declaration and Its Corpus (`bounded-exceeded`, `bounded-accepted`)

Cost Model 3.3, blu 2026-08-16. `bounded <class>` declares a ceiling in
`none < fixed < linear < growing`; the compiler infers the class and refuses
when it is exceeded. Three arms, and the pairing is the point.

**`codex/test/errors/bounded-exceeded` proves the refusal is TRANSITIVE, not
local.** `walks` declares `bounded linear` and its body is a single call, so a
check that only read the declared function's own body would pass it. It is
refused because `grow-loop` underneath copies its accumulator:

```
bounded-exceeded.codex:24:3: error CDX6101: 'walks' declares bounded linear
but is inferred growing: an accumulator is copied by & inside a self call,
here or in something it calls
```

That is the `unpack-text` shape the feature exists for -- linear where you
read it, quadratic below -- and an arm whose declared function did its own
appending would not have tested it.

**`codex/test/apps/bounded-accepted` is the control, and a refusal arm is
worthless without it.** A check that refuses every `bounded` declaration
scores a perfect refusal rate. The control declares `bounded linear` over a
`list-push` accumulator and compiles and runs (`walks-ok 8 = 8`), and declares
`bounded growing` over the very shape the other chapter is refused for
(`grows-declared 4 = xxxx`). The two chapters differ in ONE thing: the
callee's accumulator operator.

**The kill rate is measured, not asserted**, against
`codex/test/cost/accumulator-corpus`: 5 of 5 quadratic entries inferred
`growing`, 4 of 5 linear entries left alone, with `n-fixed-appends` the single
over-refusal. Both that number and the rule-1-alone number (8 of 10) are taken
by ablation. `CostModel.md` sections 7 and 8 carry the tables; the corpus
carries five linear entries written to look like the quadratic ones, which is
what makes the score mean anything.

**These arms run under the battery and not the standing gate**, which is the
convention for `codex/test/errors` and `codex/test/apps` rather than an
omission: `test.ps1` sweeps both tiers, `build.ps1`'s `app-sweep` covers the
`apps/` entry chapters instead, and the BVT runs a named list neither is on.
They were compiled and run by hand in both directions when written, and the
`TechnicalDetails.md` `bounded linear unpack-text` example was compiled too rather than
written from memory -- it refuses with the exact CDX6101 text that doc
quotes.

## Two Guest Truncation Report Shapes, And One Pattern Cannot Match Both

val 2026-08-18, lifted out of `plugs-backlog` 1.30 when that row closed; the
row is gone and this is now the only record. A harness that greps a plug
guest's console for a truncation is matching one of two incompatible shapes,
and the pattern everybody copies matches only the first:

- `TRUNCATED sent=N of M ...` -- `javascript`, `wpf`, `rust`, `recheck`.
- `OK ... sent=N TRUNCATED ...` -- `pe`, `elf` and `img`, which append the
  token CONDITIONALLY inside the OK line. Here `TRUNCATED` comes AFTER
  `sent=`, so the pattern `TRUNCATED sent=` cannot match it, **and the line
  begins with `OK`.** A harness reading these three sees success on a
  refused send however carefully it was written.

So the work is never "add the grep to N harnesses". Normalise the guest
report to one shape first, then add the check only where the guest can
actually report a refusal at all: a plug that streams to the codex-vm output
ring rather than over a checked TCP send has no refusal to report, and a
check there is decoration.

**Three failure channels are distinct and a harness needs all three.** The
guest can REFUSE a send and say so; the guest can DIE mid-emission, which is
never a refusal, so it prints no truncation line and closes cleanly enough
that an abort flag stays false; and codex-vm can DROP the serial bytes the
console is made of, which it reports on stderr rather than on the console it
is dropping from. `build/plug-run.ps1` carries one arm for each, and the
serial-drop arm runs FIRST because a short console makes every check below
it read clean for the wrong reason.

**Sabotage the PATTERN, not the plug.** A check that now passes is
indistinguishable from one that never ran. Point the grep at a line the
guest or the VM demonstrably DOES print and confirm the harness fails; keep
the broken arm (L-FALSIF, L-ORACLE).

## A Send That Cannot Finish Says So (`net-send-capped`)

blu 2026-08-16, from the plugs lane's measurement in `plugs-backlog` 1.16.
`net-io-send-chunk` returns the transport whether or not the bytes went, and
`net-io-send-text-loop` then advances past a chunk that never left, so a plug
whose send was refused printed `OK` anyway. The arm covers the refusal channel
added to `NetIO`.

**The cap was NOT the send path and it is not every plug, which is what a day
of believing the headline would have cost.** Measured after the channel
landed: `python` over the shared `build/plug-run.ps1` harness delivered
**68,049 bytes** intact, exit 0, no truncation. 38 of the 55 plugs use that
harness. What separates the capped ones is the RECEIVER: `wpf` has its own
listener that half-closes its send side (`codex/plugs/wpf/run.ps1:93`,
`Shutdown(SocketShutdown::Send)`) before reading the reply, and
`plug-run.ps1` never does. Suppressing that one line took `wpf` from 11,200
bytes and three files to 11,697 bytes and all five.

**The root cause is the CLOSE_WAIT state handler, not the queue.** A peer's
FIN moves the connection to `TcpCloseWait` (`Tcp.codex:284`), and
`tcp-step-close-wait` answered `EvSend` with `ActError "peer closed"`.
CLOSE_WAIT means the PEER will send no more; the local side may still send,
and a plug answering a request over a half-closed socket is precisely that
case. With that one arm made to build a segment like ESTABLISHED does, `wpf`
delivers all 11,697 bytes WITH the half-close left in place.

**Two hypotheses died on the way and both are worth recording, because each
was plausible and each was wrong.** First: that the FIN made
`net-io-conn-closed` true. It does not -- that predicate answers only on
`TcpClosed`, and CLOSE_WAIT is a different state. Second: that CLOSE_WAIT's
`ActNone` for `EvSegment` stopped acks draining the retransmit queue. It does
not -- ack processing lives in `net-process-frame`
(`NetworkStack.codex:408-413`) and runs regardless of TCP state, and
ESTABLISHED answers `ActNone` to a bare ack too. The 11,200 is real but it is
a CONSEQUENCE: it is how far the sender ran ahead before the drain first
processed the FIN, and the sender runs ahead exactly to the queue capacity.
Matching arithmetic is not a mechanism.

**And the channel shipped hours earlier did not catch this case.** It tested
`net-rexmit-full` and `net-io-conn-closed`, and in CLOSE_WAIT neither is true,
so a send into a half-closed connection passed both guards, dropped its bytes,
and reported complete. The predicate that holds for every refusal is the
outbox: `net-send` produces no frame whenever the state machine declines, so a
non-empty chunk that produced no frame did not go. Enumerating states was the
error; asking whether anything was actually queued is state-independent.

**The early stop is not the defect, and a fix that removed it would have
passed a careless review.** `arm64-send-refusal` already pins the stop:
walking the rest of the buffer after a refusal cost 593,152 bytes of slices
against 16,400 for stopping, measured 2026-08-10. What was missing was that
the stop was silent. Reading that arm BEFORE writing the fix is what kept
the change additive.

**Two instruments agree on the ceiling and neither was told the other's
answer.** The plugs lane arrived at 11,200 bytes from the far end, by
counting what a wpf emit delivered over four runs on one pinned IR. This arm
computes `net-rexmit-capacity * net-mss` inside the guest and prints 11,200.
A single number reproduced four times is one instrument repeated; two
instruments that start from different ends are the evidence.

```
queue-full=True cap-bytes=11200
stalled complete=False sent=0 of 50000
stalled-text complete=False sent=0
empty complete=True sent=0
healthy complete=True sent=3
close-wait complete=True sent=3
syn-sent complete=False sent=0
```

**The last two rows are the pair that has to disagree**, and they are the
whole reason the outbox predicate exists. Both states pass
`net-rexmit-full` and `net-io-conn-closed`: neither is full and neither is
`TcpClosed`. CLOSE_WAIT must SEND (the peer is done, we are not) and SYN_SENT
must refuse (there is no connection yet). Ablated independently: with the
`Tcp.codex` arm put back to `ActError`, `close-wait` alone flips to
`complete=False sent=0`; with the outbox predicate removed, `syn-sent` alone
flips to `complete=True sent=3`, reporting three bytes it never sent. Neither
ablation moves the other row, so the two guards are not standing in for each
other.

**The controls are the reason the refusal rows count** (L-CONTROL). `empty`
and `healthy` are the two ways `ns-complete` can legitimately be True, and
`healthy` is the one that matters: it puts three bytes through an
ESTABLISHED session and gets `sent=3`, so True is reachable on a real send
and not only on the trivial zero-length path. Ablated -- the `net-rexmit-full`
arm returning `ns-complete = True`, which is the pre-fix behaviour -- the two
refusal rows flip to True and both controls stay True. An arm that only read
True everywhere would look identical to a passing run without them.

**No arm needs a NIC**, which is why this is an ordinary `codex/test/apps`
entry rather than a harness script like `cdx-serve-test.ps1`.
`net-io-send-drain` returns at its own closed check before it polls, so a
session that is both queue-full and closed never reaches
`net-driver-recv-frame`. `net-rexmit-full` is tested before
`net-io-conn-closed` in the sender, so the full-queue arm is the one that
answers.

**Its runner is the battery, not the standing gate, and that is the
convention for this directory rather than an omission.** `test.ps1` sweeps
`codex\test\apps` wholesale through `$allDirs` (`:166`); `build.ps1`'s
`app-sweep` phase is a different thing entirely -- it runs
`sweep-app-classes.ps1` over the `apps/` entry chapters -- and the BVT runs a
NAMED list (`bvt.ps1:103` onward) that this arm is not on. Neither is
`agent-msg-truncated`, which is `TrustTransport`'s own refusal arm and
predates all of this, nor `crf-truncated` or `fact-sync-truncated`. A
refusal arm added here is run by `build/test.ps1 -Tier apps` and by nothing
else, so it is compiled and run by hand when it is written and the
`.expected` is recorded from that run.

**A LIBRARY CHAPTER THAT NOTHING CITES IS COMPILED BY NOTHING, and it
accumulates errors at the rate somebody edits it.** Measured 2026-08-19 across
`codex/os/net`: **5 of its 39 chapters did not compile at all**, and four of
the five were found only because a citer was written for each one by hand.
`EdgeRouter` named a type (`RateLimiterState`) that has never existed in the
tree, contradicted by its own constructor twenty lines below; `LoadBalancer`
and `MessageQueue` each passed a `Text` key to
`chr-hash-key : Integer -> Integer`; `DistributedConfig` was missing a cite
for `text-take` AND carried rename drift in three places
(`cs-entries`/`cs-count` on a record that has `cs-meta`/`cs-meta-count`);
`ServiceProxy` was collateral, blocked only by `LoadBalancer`. All fixed the
same day.

**The defects were not hidden by subtlety, they were hidden by never being
compiled**, and they came out one sweep at a time because a compiler stops at
the first error: five failures became two, then one more behind that, then two
more behind that. **The count is only trustworthy when a full sweep returns
clean**, never when you run out of errors you happened to trip over.

The instrument is cheap and worth rerunning against any quire nobody builds:
for each chapter, write a citer that cites it and does nothing else, compile
it, and read the LOG rather than the exit chatter. `sweep-app-classes.ps1`
does the equivalent for `apps/` entry chapters, and no such sweep exists for
library quires; the `codex/test/**` suites only reach a chapter something in
them already cites. `codex/os/net` is green as of 2026-08-19 and there is
still nothing standing that would catch the sixth.

**A test living under `apps/` is COMPILED by every gate and RUN by none, and
that is not the same as untested-looking.** `sweep-app-classes.ps1` selects
every chapter with an `opening` and hands each to `compile.ps1`; nothing in
that phase executes the result. So a suite under `apps/<app>/tests/` goes
green in `app-sweep` while trapping at its first runtime fault, and the trap
is invisible to `build.ps1`, to the BVT and to the battery alike. Measured
2026-08-19: `apps/nettool/tests/TestGroupMembership.codex` passed fifteen arms
and took a bounds trap (EXC=06) on the sixteenth, from a service count that
disagreed with the list it counted; the suite had been swept clean by
app-sweep the whole time, and the defect was found only because fester ran the
file by hand. **The lesson is where a test lives, not whether one exists**:
`codex/test/**` is executed by `test.ps1`, `apps/**` is compiled by
`build.ps1`, and a suite in the second place is an assertion with no runner in
the sense `LESSONS.md` means. The regression arm for that defect was put at
`codex/test/apps/group-service-count` for exactly this reason.

**The over-cap case on a LIVE connection is deliberately absent, and the
reason is a finding.** A 12,600-byte send (nine chunks, so the queue fills on
the ninth) over an established session produced NO output inside
`test-run.ps1`'s 60-second wall budget (`build/test-run.ps1:28`); the run took
61 seconds and the outfile was empty. The send pays `net-io-max-polls`, which
is `net-driver-poll-interval * 500`, for each remaining chunk. This is the
poll-count-as-duration shape a third time, after `e1000-await-aneg` and
`e1000-link-wait`, now in the send path. It is recorded as an observation and
not as a measurement: what is known is that it exceeds 60 seconds, not what it
costs.

**And the reading that came out of the source rather than the report.** The
plugs entry describes a shortfall. `net-io-send-chunk` drains before every
chunk, so a queue that frees again resumes sending while the unchecked text
loop is still walking, and the far end is handed the bytes either side of the
refusal with the refused ones missing from the middle. That is corruption
rather than truncation. It is reasoned from the send path and is NOT
reproduced here: the bed cannot free a retransmit queue mid-send without a
peer, and every arm above uses a closed connection precisely to stay off the
driver.

## The Framing Refusal Reaches Its Consumers (`crf-truncated`, `fact-sync-truncated`)

Track B item 7, blu 2026-08-16. `MessageFraming` set a `valid` flag on every
decoded field at main 15375 and nothing downstream read it. Not seed-affecting:
the compiler unit is `codex/compiler` plus cites resolved only within the
`Foreword` and `Math` quires (`concat-codex-self.ps1` `$libQuireNames`), and
Trust, Net and Replay are not in it. Confirmed against the depot seed after the
gate rather than predicted.

**One of the three named consumers never needed the change, and the plan said
otherwise.** `CurrentPlan` listed `TrustTransport`, `FactSync` and `ReplayCrf`
as chaining decodes blind. `TrustTransport` does not and never did:
`decode-agent-msg-checked` RE-ENCODES the message it decoded and compares bytes,
so it refuses a truncated body and a non-canonical encoding in one test, and
three call sites act on the answer. Reading the source before writing against
the brief is what caught it.

**Two live raw indexes were sitting behind the missing flag**, both the
`!EXC=06` class rather than the wrong-answer class: `list-at bs (off + 8)` in
`decode-schedule-event` and `list-at bs off` in `sync-decode-fact`, each reached
with an offset from a peer-driven loop.

**The count-driven loops are the same defect and the more serious half.** Both
chapters ran a loop whose trip count is a peer-supplied le32 with no relation to
the buffer. Refusing on `valid` is what bounds them, because the loop can no
longer run more times than the buffer has bytes. Measured with the refusal
removed from `crf-decode-events`, a header declaring 4294967295 events over a
27-byte log:

```
OUT OF MEMORY
SP=00000000bdfffbc8 HEAP=00000000b9e00010
```

after 6 seconds. It kills the run rather than answering, so that arm cannot sit
ablated beside the guarded one and the control was run as a source edit.

**The disclosure finding is the one worth another lane's eye.**
`fact-sync-answer-offer` replies with every fact the peer's offer did not list.
A truncated offer decoded to a SHORT hash list, fewer hashes means fewer facts
excluded, so **a peer that cut its own offer short was handed the responder's
entire store.** The leak row sweeps every prefix of a 16-byte offer naming both
of the responder's two facts and counts what came back:

```
leak 0..16 = 00000000000000000        guarded
leak 0..16 = 22222222221111110        ablated
```

**All-zeros proves nothing on its own and the control is what makes it
evidence** (L-CONTROL). A whole offer that legitimately names both facts also
draws zero, so the row cannot distinguish refusing from excluding. The control
is `empty-offer reply-facts=2`, which shows disclosure is reachable at all;
without it the guarded row is a green that a permanently broken responder would
also produce.

`event-count` in a decoded session now reports the events actually decoded
rather than the peer's claim, so a consumer iterating it cannot index past the
list it was handed.

**The gap was not in any of the three, and it was the live one.**
`tools/cdx-serve.codex:117` called the RAW `decode-agent-msg`, bypassing the
checked variant every other caller uses, so the single place in the tree that
skipped the refusal was a SERVER taking frames off a socket. Closed in the same
CL, with two arms in `build/cdx-serve-test.ps1` driving the real guest server
over TCP.

**The refusal is silence, so the arm asks twice.** `handle-conn` answers
"malformed" to its own caller and sends nothing, and a test that only checked
for no-reply would pass against a server that had simply died. The second
question is the one that carries: a good request after the malformed one must
still be served. Ablated to the raw decode, the first arm reports
`got: True, want: False` -- the unfixed server decoded the declared-64/present-8
hash to a short field and answered it as though it were whole.

## The Wire Payload Refusal (`codex/test/apps/wire-payload-refuse`)

The five consumers refuse now, and each takes the road it already had for a
message not worth acting on: `accepted = False` in `FactSync`, `drop-inbound`
in `AnnotationTransport`, the unchanged driver in `AnnotationDriver`. No new
policy was invented; the only new fact is that a truncated payload is
distinguishable from an empty one.

**The message in this arm is neither corrupt nor forged, and that is the
point.** It is a correctly signed envelope around a SHORT payload -- what an
attacker who can cut a stream gets for free, and what a half-written frame
produces by accident. The envelope is rebuilt over the truncated bytes rather
than copied from the whole message, so the id and the signature are right FOR
WHAT ARRIVED; reusing the original envelope would only have tested the id
check, which already worked.

```
good  envelope=yes payload-valid=yes id=v1
bad   envelope=yes payload-valid=no  id=v1
bad kind-would-have-been=Accept
consumer good accepted=yes bad accepted=no
consumer good recv=1 bad recv=0
```

`envelope=yes` on both arms is the line that matters: authentication passed and
told nobody anything about completeness. And the verdict the truncated message
would have been persisted as is **Accept**, because `frame-byte-at` answers 0
past the end and 0 is that tag.

**Ablated** (the `FactSync` guard replaced by `if False`): `bad accepted=yes`,
`bad recv=1`. The corrupted verdict is accepted and counted, which is what the
tree did until 2026-08-15.

`good` is the acceptance control and a guard that refuses everything fails it.
`FactSync` is the consumer the arm drives because its verdict path checks the
envelope and then decodes with no keyring in the way, so it is reachable
without standing up a trust store; the other four are the same one-line shape.
The two existing consumer tests, `fact-sync-wire-test` and
`annotation-driver-test`, still match their expected output byte for byte,
which is what says well-formed traffic is undisturbed.

## The Kernel Descriptor Family (`codex/test/usb-desc-guard`)

The kernel-side half of what `usb-cfg-total-guard` covers at the Works layer,
and the third of the three USB rows in the Track D census. Two different
outcomes in one arm, which is the point of running it.

**`hid-scan-loop` was a real defect.** It bounded its offset against the
caller's `total` and never against `list-length desc`, and that total is a
device's own `wTotalLength`. Measured before the fix, a 25-byte configuration
claiming 200:

```
scan honest total=25 ifaces=1
!EXC=06 RIP=000000000010bdb6 ... R12=0000000000000019 R13=00000000000000c8
```

`0x19` is 25 and `0xc8` is 200 -- the buffer and the claim, the two numbers that
disagree, in the registers. The honest arm above it had already answered, so the
fault is the lying total and nothing else. Clamped at `hid-scan-interfaces`
rather than refused, for the same reason the Works-side walk clamps: a
configuration larger than one control transfer is ordinary hardware.

**The three `Usb.codex` parsers were already correct, and that is worth an arm
too.** `usb-parse-device-desc`, `usb-parse-endpoint`, `usb-parse-interface` and
`usb-le16` all check their upper bound and degrade to zeroed records. Nothing
asserted that, which the census recorded as KAT-ONLY: one hand-built
well-formed array. A guard with no arm is one edit away from not being a guard,
so `device-short`, `endpoint-past`, `interface-past` and `le16 past` pin the
degrade.

| arm | expects | why it is there |
|---|---|---|
| `device`, `endpoint`, `interface` | the real values | a clamp that refuses everything fails here |
| `scan honest total=25` | 1 interface | the acceptance control for the scanner |
| `scan lying total=200` | **1 interface** | the same answer as honest: clamped, not refused, and no invented interface |
| `scan huge total=65535` | 1 interface | the device's field is 16 bits wide and this is all of it |
| `scan empty` | 0 | a zero-length buffer with a non-zero total |
| `device-short`, `endpoint-past`, `interface-past` | zeroed records | the degrade the parsers already did |

`honest` and `lying` are the discriminating pair and are worth reading
together: the buffer is identical and only the device's claim differs, so one
answer for both is the clamp working rather than the walk getting lucky.

**The negative offset is recorded and NOT guarded.** Every bound in
`Usb.codex` is of the form `offset + n > list-length`, which a negative offset
passes before indexing backwards. No caller can currently produce one --
`hid-scan-loop` advances by a descriptor length and the Works-side walks start
at zero -- so it is a hole with no way in, and closing it would be a guard
against a caller that does not exist.

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

### `test-compile` grades with whatever kernel `build-output` was left holding (val, 2026-09-02)

`test-compile-batch.ps1:100` takes its compiler from
`build-output/bare-metal/Codex.cdx` and refuses only if the file is ABSENT.
Nothing checks that it is the seed of record, and on an `-Internal` run whose
core is SKIPPED nothing writes it: the `clean` phase is skipped too, so the
file is whichever kernel some earlier run happened to leave there.

Measured: a workspace at seed `E0042890` (red's multiply unit, main 21676)
had a `BE8B04B5` kernel sitting in `build-output` from three seeds earlier.
The exact i64 `wrapping` band that 21676 accepts, that older compiler
REFUSES, so `test-compile` reported `CDX1073` in
`codex/foreword/core/Wrap64.codex` and `Hamt.codex` for every chapter whose
subset reaches `Random` -- and those files are untouched at head, so it reads
as another lane's regression rather than as a stale instrument. It was
published as a red at head before the control was run. Copying the seed over
that path made all of it compile clean with nothing else changed.

**The gate prints a `kernel:` line, and it is NOT this phase's kernel.** That
line comes from `compile.ps1`; `test-compile-batch` launches its own VM with
`$Stage0`. Reading the printed digest and concluding the phase used it is the
L-SAMEVER trap one level along, and it is what made the wrong reading
plausible. Hash `build-output/bare-metal/Codex.cdx` before believing a
`test-compile` red that names a file your change never touched.

### The parser was quadratic, and the VM was a tenth of phase 1 (red, 2026-08-22)

Measured on the 2026-08-20 release battery's own `test.log`: each batch of
193 tests spent 20 to 62 s in the VM and **417 to 456 s in the host parser**.
Isolated in a scratchpad with the same script: 24 tests 10 s, 48 tests 31 s,
96 tests 130 s, all of it in `Add-LogSpan` and `NextLine`. The cause was one
assignment, `$raw = if (Test-Path $outputFile) { ReadAllBytes } else { ... }`:
a statement's result goes through the pipeline, which unrolls a `byte[]`
into an `Object[]` of boxed bytes (`$raw.GetType()` said so), and every
`GetString($raw, ...)` and `[Array]::Copy($raw, ...)` then re-converted the
whole buffer, 183 ms per call on a 2.9 MB array. Nothing else in the loop
was wrong, and the loop's own comments record two earlier campaigns against
the same symptom that each sped up a different line.

Fixed in the generator (`codex/build/testcompilebatchScript.codex`, a direct
assignment) and installed at drift 0. Proven on the 96-test list: parse
132,007 ms to 831 ms, the 93 emitted binaries byte-identical to the pre-fix
run, all 96 `build.log`s identical, the three expected-error tests at exit 7
in both arms. `check-test-compile.ps1` compiles through this parser, so the
standing gate's `test-compile` phase was paying it too. The phase 2 side of
the same measurement (a `pwsh` child per test against 75 ms for `codex-vm`
alone) is `CurrentPlan.md` "The battery choreography", with the items.

### The batch stream can lose bytes, and the loss lands on the wrong names

Measured 2026-08-16, release battery at main 15671, kernel `D230B11D`, `-Jobs 8`.
batch-0 (182 tests) delivered 180 `SIZE:` blocks. Everything through test 177
was correct; at test 178 the stream lost bytes and every block after it was
filed under the wrong name. `verifier-phase5-test.cdx` carried its own header
(bytes 0..223 identical to a standalone compile, same 164,350-byte length) and
a body that differed in 157,882 of 164,350 bytes; `repo-reclaim.cdx` WAS
`ota-lwm2m-loopback` (SHA `A4103863`, test 181); `colophon-dogfood.cdx` WAS
`web-server-test` (SHA `AB8F20CE`, test 182); tests 181 and 182 got exit 99 and
were rebatched clean. All three shifted tests, compiled standalone with
`compile.ps1 -Kernel seed\Codex.cdx` and run through `test-run.ps1` with their
sidecars, matched `.expected` exactly. Ten VMs were live on the box at the time:
the eight batch slots plus an `emit-compiler.ps1` IR run and a `dotnet build`.

The parser in `test-compile-batch.ps1` assigns blocks to names by sequence and
has no way to notice a lost block: a shortfall shows up only as `FAIL: VM died
before this test` on the tail names and as byte-identical binaries under
foreign names further up. Two things tell it apart from a compiler regression
in a minute: hash the failing test's `.cdx` against a standalone compile
(a shifted block matches ANOTHER test's standalone output exactly), and check
whether the batch that produced it lost tail members to exit 99. Neither the
lossy layer (guest serial, host `-output` writer, or the parser's marker walk)
nor the role of host load is established; ten VMs is the only measurement.

**Second instance and containment (red, 2026-08-28, red 20450).** The Update 52
release battery (2026-08-27, hot box) put 26 false `FAIL_OUTPUT` reds on the
rollup from two death-batches: the shifted members held exit 0 and their
binaries ran under foreign names, while only the tail 99s were re-batched --
so the re-batch machinery cleared the honest failures and KEPT the misfiled
ones, and the one line naming the cause went to a hidden console. Three
repairs, all in the generators:

- `test-compile-batch.ps1` invalidates the WHOLE batch on either loss signal
  (a `DROPPED` report on codex-vm's stderr, or a stream that ends short of
  the test count with no `!EXC`): every member's `.exitcode` becomes 99 with
  a `BATCH INVALIDATED` build.log line, so phase 1a re-batches all of it and
  a clean session either reproduces each result or clears it. A crash
  (`!EXC`) without a drop keeps the old attribution -- framing is intact up
  to the dump and the crasher's standalone confirm needs its exit 4. Proven
  by killing the VM mid-batch: new script 20/20 at exit 99; old script kept
  3 members at exit 0.
- `test.ps1` redirects each batch child's stderr (main and rebatch launches)
  to a temp file and moves it into `test-output\_batches\*.err`, echoing any
  `DROPPED` / `BATCH INVALIDATED` content to the console. The next loss is
  therefore attributable to a layer: a `.err` naming dropped bytes convicts
  the serial path; a short stream with a silent `.err` points at the writer
  or an early VM exit.
- `test.ps1`'s `Get-FailHint` checks, before its length arithmetic, whether
  a `FAIL_OUTPUT` actual is byte-for-byte ANOTHER test's expected and says
  `HOLDS ... batch misattribution` instead of presenting a first-differing
  line that reads as codegen. The owner map is built once, on the first
  FAIL_OUTPUT hint; green runs never pay for it.

**The lossy layer of the two historical events is unknowable now, and does not
need to be: the NEXT one names itself.** Since main 21584 codex-vm's output
writer checks its own write and reports a shortfall in the canonical
`N guest serial byte(s) DROPPED ... is SHORT` wording with the CAUSE in
parentheses, and there are five of them -- the output file could not be opened,
the output write went short at N of M, buffer growth failed, a blit was out of
range, blit growth failed. So the attribution rule above is now wrong in one
direction and it matters: **a short stream with a SILENT `.err` no longer points
at the writer, because the writer can no longer be silent.** It points at an
early VM exit or at a guest that never produced the bytes.

**Reproducible on demand, so the chain is proven rather than assumed** (fester,
2026-09-02). `CODEX_VM_SHORT_WRITE_AT=N` truncates every `-output` write to N
bytes. Over a three-subject batch at N=500 the writer reported
`SERIAL: 297074 guest serial byte(s) DROPPED (output write short at 500 of
297574 bytes)`, `test-compile-batch.ps1` matched the wording and warned that
every block after the loss is filed under the wrong name, and the containment
engaged end to end: 3 of 3 members at exit 99 with `BATCH INVALIDATED` in their
build logs. That is one arm per layer away from a full census -- the guest-serial
and blit causes have no injection knob yet -- but the half that carries the
host writer is measured rather than argued.

**The layer is decidable from the SHAPE, and it did not need a recurrence**
(fester, 2026-09-02). Both writers `fopen` the output `"wb"` and rewrite the
whole buffer from byte 0, so the file on disk is always a PREFIX of the
capture and a writer fault can only truncate the TAIL. The parser assigns
blocks by sequence and loses nothing; it misattributes, which the injection
above already proved. So a MIDDLE hole with later blocks present -- the
2026-08-16 shape -- can only be `output_buf_write` or `blit_guest_output`
failing to grow the buffer, which is the host out of memory, and both have
counted and reported it since blu's fix. **A capture that ends EARLY is the
writer or an early exit; a capture missing a block in the MIDDLE is host RAM.**

That left the first of those two unattributable, and it was silent by
construction. Neither writer checked `fwrite`'s return or `fclose`'s,
`dump_output_file` printed `output_len` whatever had happened, and
`runlist_scan_output` propagated that same self-made number as `output=N`:
nothing in the loop compared what codex-vm SAID it wrote against what the file
HOLDS, so a short or failed write read as a healthy run. It reported the
intent rather than the outcome, which is L-REQUEST's shape sitting inside the
instrument this row was relying on. Both writes are checked now; `Output: N
bytes` reports the bytes that reached the file; and a shortfall prints an
`OUTPUT:` line naming the writer as the layer followed by the canonical
`guest serial byte(s) DROPPED ... is SHORT`, so `test-run.ps1`,
`test-compile-batch.ps1` and `runlist_scan_dropped` all refuse with no harness
change (L-UNHEARD's repair: route the signal into a state something already
acts on). The `OUTPUT:` line matters because the rule above would otherwise
convict the serial path for a loss that never touched it.

The arm is `check-run-list.ps1` arm 6, with `CODEX_VM_SHORT_WRITE_AT` as the
injection. Its cap is the CONTROL run's own file length minus one, so exactly
one byte is lost and the arm asserts `dropped=1` and `output=<len-1>` rather
than "nonzero"; a fixed cap would pass vacuously the day the canary's output
fell below it (L-VACUOUS).

**The two loss signals had one control between them, and it is now two**
(fester, 2026-08-28). Killing the VM mid-batch fires the SHORT-STREAM arm; it
cannot fire the `DROPPED` arm, because a killed VM never reports a drop. The
`DROPPED` branch was therefore live, correct and unfired, which is the state
L-FALSIF is about. `CODEX_VM_DROP_SERIAL_AT=N` with `CODEX_VM_DROP_SERIAL_LEN=K`
(`OperatorsManual.md`) makes codex-vm discard K bytes of capture once N have
been taken, on whichever output path is carrying them, counted and reported
exactly as a real loss is. Both arms measured over the same eight-test list:

| arm | result |
|---|---|
| no injection | 8 of 8 at exit 0, eight DISTINCT correct binaries |
| injected, pre-containment script | tests 4, 5, 6 and 7 each holding the NEXT test's binary byte-identically, test 8 at exit 99, **and 4 through 7 all at exit 0** |
| injected, shipped script | `BATCH INVALIDATED: codex-vm dropped serial bytes`, 8 of 8 at exit 99 |

The middle row is the defect reproduced on demand rather than waited for, and
it is what makes the third row mean something: the same input that used to
ship four foreign binaries at exit 0 now invalidates the batch. It also
reproduces the 2026-08-16 batch-0 shape exactly -- shifted members above,
exit 99 on the tail -- from a deliberate byte loss, which is the evidence that
the sequence-assigning parser is the thing that misattributes, whatever loses
the bytes.

Invalidation sets the exit code and leaves the misattributed `.cdx` on disk.
That is inert to the harness and deliberate to leave alone: phase 1b gates
every downstream decision on `$compileOk = ($exitCode -eq '0')`, and exit 0 is
written only in the branch that has just written a fresh binary, so a stale
binary under a 99 can never reach a verdict. It can still mislead a human
reading `test-output\<name>\` by hand after an invalidated batch, and the
swap census one screen up is the way to tell.


## The E1000 Bring-Up Budgets (`e1000-aneg-budget`, `e1000-link-budget`, `e1000-link-deadline`)

Two loops in `e1000-init` were bounded by a count and neither could be. A count
is not a duration, and on this part the two failures were serial: fixing the
first exposed the second, which is the whole reason all three arms are here
rather than one.

**The measurement that reframed both.** NIC-3 (ASUS, 2026-08-15) recorded
`e1000-await-aneg` running its full million at 92.89 us per MDIO read and
**returning 0**. Aneg-done is never set on this part, while `STATUS.LU` comes up
anyway and it negotiates 1000 Mb/s. So those 92.9 seconds were never
auto-negotiation succeeding. They were dead time during which the link came up
behind them, and they MASKED the fact that `e1000-await-link` had no deadline at
all, only a bare count of four million `STATUS` reads. Budgeting aneg to 3 s
removed the dead time and the count then ran against a link still settling:
NIC-4, 2026-08-16, over ten minutes with no return, which reads as a wedge.

**Two of the three arms are arithmetic only, and say so.** `e1000-aneg-budget`
and `e1000-link-budget` assert the same wall time at both real HPET rates this
project has measured (23999999 from the ASUS, 14318179 from the bed) and, for
the link, that `batch * batch-fuel` is 409,600,000 so nothing got LESS sensitive
than the count it replaced. The timeout branch is genuinely unreachable for
aneg, because the model reports aneg-done on the first read.

**`e1000-link-deadline` is the one that reproduces the metal symptom on the
desk, and it exists because a claim of unreachability was wrong.** The budget
arm's prose first asserted the link timeout was unreachable in the bed too, by
analogy with aneg. `-e1000-no-link` had existed for some time and
`e1000-asde-nolink` was already running under it; the analogy was the whole of
the reasoning and nothing checked it. Under that flag `e1000-init` returns in
**8,037,305 us** with the budget and **21,862,178 us** with the count it
replaced, and the arm asserts the BAND rather than the figure, because a
wall-clock reading on a loaded host is a flake.

**The lower bound is the half that discriminates**, and the ceiling is the
weaker assertion: a count that happens to be slow can land under a ceiling, but
only a clock lands in a band. On a faster host the old count would finish sooner
and could slip under 15 s; the budget lands near 8 s on any host because the
clock decides, not the host. On metal the same count did not finish in ten
minutes. Ablated, exactly one row moves.

**The clock is read once per 4096-poll batch, not once per poll**, following
`e1000-await-tx-clocked` in the same chapter. A `STATUS` read is the cheap thing
the loop is made of and an HPET read is not, so a per-pass check would measure
the clock rather than the link. The same defect one level out bit the NIC-4
probe's own listen loop during rehearsal, at a fuel of four million.

## The Invisible Receive (`codex/test/e1000-rx-invisible`, `-off`)

DiagNicRing reads three rows off two counters and the ring: `gprc=0 rnbc=0` is
nothing arrived (:122), `gprc>0 ddset=0` is frames arrived and we cannot see
them (:124), and `rnbc>0` is turned away for want of a descriptor (:127). A
driver acts on the three differently, which is why the counters are read
together and never the ring alone.

**The bed could only ever answer the first of them.** codex-vm incremented
GPRC inside the delivery loop just after DD was written, and all three ways of
stopping a receive -- `e1000_ring_poisoned`, `i219_mac_stalled`,
`e1000_dma_blocked` -- return above that point, so every fault the model had
read `gprc=0` and landed on the wrong row. Sitting 9 read `gp=1 rnbc=0
ddset=0` off the ASUS on 2026-08-21 with the aim row saying RDBA is ours, and
that reading is what the arms are written against. **A row the bed cannot make
say "invisible" is a row whose instrument cannot fail** (L-FALSIF), and it is
the row a flight gets read against.

GPRC now counts where the MAC accepts the frame, which is what 82583V stats
and `DiagNicRing.codex:119` describe. RNBC did not move: it stays at the
ring-full test, because it is a different row.

**The before-and-after is one line and it is the evidence.** The same arm,
same kernel, same sidecar, against the depot emulator and against the fixed
one:

| | pre-fix | post-fix |
|---|---|---|
| `recv happened` | no | no |
| `gprc over 0` | **no** | **yes** |

Pre-fix it reads "nothing arrived" and post-fix it reads the row the board
read. The arm fails against the old emulator, which is what makes it evidence
rather than a green.

**The pair uses `-nic-bme-clear`, not the K1 stall, and the choice is forced
rather than preferred.** K1 is the cause sitting 9 read, but the canned
injector empties its whole `-e1000-inject` budget at the `RCTL.EN` write and
`e1000-init` has performed the shipped K1 step by then, so nothing on that
path can be stalled at the moment frames arrive. Re-entering the stall
afterwards does not help either: the frames are already in the ring, and the
second poll reads a descriptor rather than provoking a delivery. Bus mastering
is clear from before bring-up, so it reaches the same row without the
sequencing. **The K1 half rides the NAT path**, where frames arrive over time,
and belongs to the diag image's nicring stage.

`e1000-rx-invisible-off` is the control and it must read `gprc over 0 : yes`
as well. That is the point of printing it: **gprc>0 alone is not the
discriminator**, and an arm that showed only the flagged side would read as
though it were.

`e1000_nat_rx` had no stall gate at all until this change, so a MAC that
`e1000_deliver_rx` correctly refused to deliver through still received
everything the moment the NAT was the source -- and `-e1000-nat -i219` is
exactly the combination the sitting-9 arm is written on, so the divergence sat
under the arm being built.

## The Lz4 Decompressor Guards (`codex/test/apps/lz4-hostile`)

Track D item 19, the `Lz4` half. Every read in the decompressor was a bare
`list-at`, so a literal count, a match offset or a match length taken off the
stream decided where a read landed, and three separate hostile streams halted
the machine. Not seed-affecting: `Lz4` is absent from the compiler's unit,
measured with `build/concat-codex-self.ps1` rather than assumed.

**The reason nothing had ever caught this is the shape of the only test.**
`codex/test/lib/lz4-test` is a round trip through `lz4-compress`, so the
decoder had only ever been handed bytes we wrote ourselves, and our encoder
cannot emit a malformed stream. A round-trip suite cannot express the hostile
half of a codec no matter how many vectors it carries (L-GAP).

**Clamp, not refuse, and the caller is why.** `archive-expand`
(`apps/works/FactArchive.codex:177`) decompresses and then compares
`fa-hash-bytes` against the base record's digest, answering `None` on
mismatch. The caller already holds a refusal channel, so the primitive only
has to become total; adding a `valid` flag would have been machinery paying
for a decision already made one frame up (L-LESS).

| guard | the stream | unguarded |
|---|---|---|
| `lz4-fits` in `lz4-copy-literals` | a 35-byte literal run over a 2-byte stream | `!EXC=06`, `RSI=2` |
| `lz4-fits input after-lit 2` | the offset's second byte one past the end | `!EXC=06`, `RSI=3` |
| `offset <= 0` in `lz4-copy-match` | a back-reference of distance 0 | `!EXC=06`, `RSI=0` |
| `src < 0` in `lz4-copy-match` | a back-reference 5 behind a 1-byte output | **no crash, `len=5`** |

**The old two-byte offset guard tested one of its two bytes.** `after-lit >= len`
admits `after-lit == len - 1`, and the very next line reads `after-lit + 1`.
A guard that checks the start of a multi-byte field and not its end is the
same defect `gguf-parse-header` had at a different width, and it is worth
naming as a shape rather than as two incidents.

**The fourth row is the one that pays, and it never crashed.** `safe-src`
clamped a negative source index to 0 and carried on, so a back-reference
pointing before the start of the output FABRICATED bytes the stream never
encoded: five bytes out of a one-byte decode, all of them plausible, none of
them refused. A crash is loud; this answered confidently. Ablated, it is the
only row that moves without an exception, which is exactly why the arm reports
a decoded LENGTH and first byte rather than pass or fail.

**The bound is subtractive on purpose** (census row 20): `off <= list-length
input - n`, never `off + n <= list-length input`, since a stream-supplied `n`
is what makes the additive form wrap and admit what it exists to refuse.

**There is no decompression bomb here and a cap for one was written and then
deleted.** `lz4-read-extra-loop` consumes one input byte per 255 it adds and
stops at the end of the input, so total output is bounded at about 256 times
the input by the format's own structure. A ceiling at 255x could never fire,
and an unfired guard is worth what no guard is worth. The unbounded-count
shape the census names for `compress/` is real in `Rle` and `Deflate`, where
a stream integer sizes an allocation directly; it is not real in `Lz4`.

## The Lz77 Decompressor Guards (`codex/test/apps/lz77-hostile`)

Track D item 19, the `Lz77` half, and the same three shapes one chapter over:
`lz77-decompress` reads a back-reference distance and a match length out of
the token list and hands the distance to `lz77-copy-match` as an index.

**Guarded because something runs it** (red's ruling, 2026-08-16: guard a
compress chapter with any caller, production or a harness that runs it;
census-row the rest under L-UNCALLED). `lz77-test` round trips through it and
`Deflate.codex` uses the chapter's table helpers throughout.

| guard | the tokens | unguarded |
|---|---|---|
| `start < 0` | distance 5 against an empty output | `!EXC=06`, `RSI=-5` |
| `off <= 0` | distance 0 after one literal | `!EXC=06`, `RSI=1` |
| `mlen > lz77-max-match` | one token asking for 100000 | **no crash, `len=100001`** |

**This chapter has the unbounded-count shape that `Lz4` turned out not to
have, and the difference is worth stating because it is what decides whether
a cap is worth writing.** In `Lz4` a long match is paid for one input byte
per 255, so output is bounded at about 256 times the input by the format
itself and a cap could never fire. Here ONE token carries the entire count
as a raw integer, so a 5-element token list expands to 100001 bytes and
nothing structural stops it. Same family, opposite answer.

**The bound is the paired encoder's own ceiling, not a number picked for
being round.** `lz77-compress` finds every match through
`lz77-find-lazy ... lz77-max-match`, so 258 is precisely what it can emit and
the guard cannot refuse a stream we produced. `Brotli` does not call
`lz77-decompress` at all (it uses `deflate-copy` and the table helpers), so
the larger Brotli match ceiling the constants prose describes does not reach
this path. Checked before choosing the constant, because bounding a decoder
below what its encoder emits breaks the round trip silently.

**Rle is the counter-case and got no guard.** `codex/foreword/compress/Rle.codex`
has no caller anywhere: `compress-rle` cites it and only prints a string,
`foreword-all-compile` is compile-only, and the `rle-encode` and
`rle-decompress` hits elsewhere in the tree belong to `apps/data/ColumnStore`
and `encode/VideoCodec`, which define their own. Grepping the NAME rather
than resolving the cite would have manufactured three callers it does not
have.

## The Deflate Decoder Guards (`codex/test/apps/deflate-hostile`)

Track D item 19, the `Deflate` half, and the two hazards fail differently
from each other: one traps, the other runs the machine out of memory.

**The back-reference.** `deflate-fixed-match:827` and `deflate-dyn-loop:900`
both hand `list-length acc - dist` to `deflate-copy`, and `dist` comes off the
stream through `dist-base`, which answers 24577 for code 29 and 32769 for
code 30 against any output a short stream has produced. `deflate-copy` now
refuses a negative source. The arm calls it directly rather than through a
crafted stream, and says so: hand-assembling bit-exact hostile Deflate is a
separate job, and the row's value is that it pins the guard where all three
call sites land, Brotli's `:2408` included.

**The truncated dynamic block, which is the one that matters.** `br-bit:727`
answers 0 past the end of the input, so a dynamic block whose bytes run out
mid-symbol keeps decoding zeros forever, never reaches end-of-block, and
pushes a literal per pass. `deflate-blocks:788` already had the exhaustion
check and it is in the wrong place: OUTSIDE the block loop, so nothing
observes it once a dynamic block has begun decoding. `br-exhausted` in
`deflate-dyn-loop` is the same test moved to where the loop can see it.
Ablated, the row does not print a wrong answer, it prints `OUT OF MEMORY`
after six seconds.

**Row 7 asserts a BOUND, not a length, and the reason is worth keeping.** The
exact output of a truncated stream depends on where the cut lands in the
table, so an exact number would be an oracle copied from a run of the code
under test rather than derived from anything. What the guard promises is that
the walk stops when the bits do.

**A red interop result here was NOT this change, and the elimination is the
record.** `build/brotli-interop-test.ps1` failed once with four probe rows
missing and a `HOST CRASH: codex-vm faulted` at a guest write to
`0x70a00000`. Measured across five runs: control green, `deflate-copy` guard
alone green, `br-exhausted` alone green, and **the identical source that
failed green on rerun**. One failure in five, not reproducible on the failing
configuration, and the run names codex-vm itself as the faulting party. It is
not in `test.ps1`, `bvt.ps1` or the release recipe, so it reds nobody's gate;
it is recorded in `CurrentPlan.md` as an unowned intermittent rather than
attributed to a change that cannot reproduce it.

## The Brotli Termination Arm, and what it does NOT prove (`codex/test/apps/brotli-hostile`)

Track D item 19, the `Brotli` half. **This arm adds no guards, and the useful
result is a negative one: it does not exercise the guards the census credits
this chapter with, and it says so in its own prose.**

The census called `Brotli` "the best-defended by inspection" and that survived
contact. What did not survive is the assumption that a hostile arm would
therefore have something to catch.

**Two ablations, neither of which moved a single row.** Deleting the
`brotli-valid` gate at `:1834`: no change. Widening `:2405`'s `dist < 1`
refusal to an unreachable bound: no change. Hostile HEADERS and truncated
streams do not decode far enough to reach a distance command at all. Reaching
those guards needs a crafted bitstream that clears the meta-block header and
the Huffman tables first, which is a fuzz-corpus job and is NOT done. An arm
shipped without those ablations would have read as coverage of the whole
chapter and been believed.

**`brotli-valid` is not load-bearing for any input constructible here, and
half of it cannot fire at all.** `brotli-read-wbits:313` can only answer 16,
17, 9..15 or 18..24, so the `w <= 24` test in `:2507` is unreachable by
construction; the `w < 10` half fires only for `wbits = 9`, and for that input
the decoder independently answers the empty list. Deleting the whole gate
changes nothing measurable. It is an unfired guard in a chapter everyone
believes is defended, which is the shape L-FALSIF is about.

**What the arm does establish is TERMINATION on bytes we did not write**, and
nothing else in the tree asserts it: `brotli-test` is a round trip through our
own encoder, which cannot produce a malformed stream, and
`brotli-interop-test.ps1` carries a single corrupted-stream control. Rows 6
and 7 run the decoder end to end on garbage and on a halved stream and require
it to stop, under a heap mark per row so the rows do not pay for each other.

**Rows 4 and 5 report an exact length because both are derivable from the
source; rows 6 and 7 report a bound because a mutated stream's output is not.**
Row 5's single byte is derived, not guessed: bit 0 set skips the `wbits = 16`
shorthand, bits 1..3 zero make `n` zero, bits 4..6 equal to one make `m` one,
so `wbits` is 9 and the stream is refused before a symbol is decoded. That
byte is 17.

## The TLS Record Header Guard (`codex/test/apps/tls-record-guard`)

Track D item 19, the `Tls` leg, under red's ruling extended past `compress/`
on 2026-08-16. `tls-decode-record:96` read five header bytes with five bare
`list-at` calls and no length check of any kind, so a record shorter than its
own header killed the guest before a single field was interpreted. Ablated,
an empty record dies `!EXC=06` at row 3.

**The payload was never the hazard and the header always was.** `tls-slice:205`
already refuses to read past the end and clamps, which is why the obvious
worry -- a 16-bit length field driving a slice -- was already safe. The five
unguarded bytes in front of it were not. Reading the length field's guard and
concluding the function was defended is the mistake available here.

**A clamp with no channel is a confident lie, which is the second guard.**
With the header check alone, a record declaring 100 payload bytes with none
behind it returns a well-formed `TlsRecord` with an empty payload and no way
for a caller to tell it from a legitimately empty one. `tls-rec-valid` now
answers whether the payload that arrived is the payload the record claimed.
Ablated, exactly row 5 flips to `valid=True` and nothing else moves.

**Row 6 is the discriminator and it is why the check is `payload == len`
rather than a minimum size.** A record declaring zero payload bytes is
legitimate TLS and must stay valid; a guard that refused every short record
would pass rows 3, 4 and 5 and fail here.

The existing `tls-test` decodes only what `tls-encode-record` just wrote, so
the decoder had never been shown a record our own encoder did not produce --
and a truncated read off a socket is precisely the record it cannot produce
(L-GAP). The chapter is cited in production by `DtlsHello`, `DtlsMessage`,
`TlsCert`, `TlsEndpoint` and `os/net/DtlsEndpoint`, none of which call this
function today.

Not seed-affecting: `Tls` is absent from the compiler unit, measured against
a `Foreword--Fat16` positive control. `tls-test`, `tls-cert`, `tls13-record`,
`tls13-schedule`, `tls-cv-schemes` and `dtls-fragmented-flight` are all still
green after the record grew a field.

## The Pbkdf Stored-Record Guards (`codex/test/apps/pbkdf-stored-guard`)

Track D item 19, the `Pbkdf` leg, same ruling. `pbkdf-verify:157` walked both
hashes to `list-length (result.pbr-hash)`, so the STORED record decided how far
the read went while the rehash it was compared against was whatever length the
parameters produced. A stored hash longer than the rehash indexes the rehash
past its end. Ablated, row 3 dies `!EXC=06` with `R13=0x20` and `R14=0x40`
against a 32-byte rehash, which is the walk arriving at index 32 of 32 bytes.

**The fault is the smaller half of what that guard is for.** A stored hash
SHORTER than the rehash never faults; it compares a prefix and agrees. Ablated
and with row 3 removed so the kill does not mask it, `4-truncated-hash` reports
`verify=True`: a stored record cut to any prefix of a real hash verifies
against the right password, and eight bytes is a forgery cost of 2^64 down
from 2^256. The length equality is checked before the constant-time walk, which
is the same shape `AesGcm:196` and `EcdsaP256:511` already use.

**Row 7 is the discriminator.** A 16-byte tag is a legitimate `pb-hash-length`,
so a guard that demanded 32 bytes would pass rows 3 and 4 and fail only here.
The check is rehash-length against stored-length, not either against a constant.

The second guard is `pbkdf-final-block:133`. `count` is `list-length blocks /
32`, and a `pb-block-count` of zero or less leaves the array empty, so `last`
is `(0 - 1) * 32` and `pb-slice` reads at -32. Ablated, row 5 dies with
`R12 = RSI = 0xffffffffffffffe0`. Rows 1 through 4 do not move under it, and
rows 5 and 6 do not move under the first guard.

**What this does NOT prove, because the census has been wrong in this exact
way before.** Nothing in production hands `pbkdf-verify` a record it did not
just compute. `crypto-vectors:81` and `:84` call it, which is what qualifies
the chapter under the ruling; `apps/secrets` reaches `pbkdf-hash` at
`VaultCrypto:77` but builds `PbkdfParams` from literals and never rebuilds a
`PbkdfResult` from a file. The hostile stored record is reachable the moment a
vault format persists one, and not before. The existing test hashes and
verifies in the same breath, so the only records the function had ever seen
were ones it had just produced (L-GAP).

Not seed-affecting: `Pbkdf` is absent from the compiler unit against the same
`Foreword--Fat16` control, and `crypto-vectors` is byte-identical to its
sidecar after the change.

Left alone deliberately: `pb-time-cost` and `pb-block-count` are an unbounded
cost rather than an unbounded read, and clamping either would silently change
the key derived for any caller already above the clamp. `apps/secrets` is one
(`pbkdf-iterations` is 100000 against a chapter default of 3), so a clamp there
is a stored-data break and belongs to whoever owns that format, not to a bounds
pass. It is recorded in `apps/secrets/secrets-backlog.md`.

## The ChaCha20Poly1305 Size Guards (`codex/test/apps/chachapoly-size-guard`)

Track D item 19, same leg. RFC 8439 fixes the key at 32 bytes and the nonce at
12, and nothing below this chapter checked either. **The interesting part is
that ChaCha20 does not fail on a wrong size. It reinterprets the state.**

`chacha-init-state:38` builds the state by CONCATENATION -- four constants, the
key words, the counter, the nonce words -- and `chacha-words-from-bytes:88`
takes `list-length bytes / 4`. So a wrong-sized input shifts which words of the
state are which, and the rounds carry on over whatever lands at indices 0..15.

Three consequences, all measured on the unguarded chapter BEFORE the guard was
written, and all visible in one ablation run:

| input | what happens | row |
|---|---|---|
| 16-byte nonce | four nonce words, 17-word state; the rounds and `chacha-add-states` both stop at 16, so the fourth is dropped and the keystream is **exactly** that of the 12-byte prefix | 5 |
| 64-byte key | sixteen key words, so indices 0..15 are the constants and the first twelve key words and **the nonce never enters the state at all**; two different nonces give byte-identical ciphertext | 6, 7 |
| 11-byte nonce | two nonce words, 15-word state, `chacha-qr state 3 7 11 15` reads index 15 and the guest dies (`RDI=0x0f`) | 8, 9 |

Only the third one crashes. The first two are silent, and they are the worse
pair: a repeated keystream under Poly1305 is exactly what this chapter's own
opening says destroys it, and the 64-byte-key case repeats the keystream for
EVERY message under that key regardless of nonce.

**Rows 1 and 2 are the instrument's control and the arm is worthless without
them.** They are two legitimate nonces, and they must produce different
ciphertext -- otherwise rows 6 and 7 agreeing would prove nothing about the
nonce being ignored, only that the probe cannot tell nonces apart. Ablated,
rows 1 and 2 still differ while 6 and 7 agree, which is the finding.

The guard is one predicate, `cp-params-ok`, refused at both entry points:
`chacha20poly1305-decrypt` answers `None`, which the chapter already treats as
the security answer rather than an error path, and
`chacha20poly1305-encrypt` gains `cp-valid` so a caller can tell a refusal from
a short message (the same reasoning as `tls-rec-valid`; a refusal with no
channel is a confident lie). Ablating the encrypt guard moves rows 5 through 8
and nothing else; ablating the decrypt guard moves only row 9.

Not guarded, and it did not need to be: `poly-tags-equal:221` already checks
`list-length a /= list-length b` before the constant-time walk, so the tag
comparison never had the defect `pbkdf-verify` had. Checked rather than assumed.

`Pbkdf` and this chapter are the same shape from opposite ends: there the
untrusted length decided how far a read went, here the untrusted length decided
what the bytes MEANT.

Not seed-affecting: absent from the compiler unit against a `Foreword--Fat16`
control. `codex/test/chacha20poly1305`, the RFC 8439 section 2.8.2 vector, is
byte-identical after the change, which is what says the guard did not move the
cipher. No production caller; that vector test is what qualifies the chapter.

## The Decimal Scale Guards (`codex/test/apps/decimal-scale-guard`)

Track D item 19, same leg, and the first fault in this campaign that is not
`!EXC=06`. **10^18 is the largest power of ten a signed 64-bit integer holds.
10^19 wraps negative. 10^64 is EXACTLY ZERO**, because 10^n is 2^n times 5^n
and the low 64 bits have lost every factor of two by n = 64. `dec-pow10` is a
DIVISOR in `dec-to-text`, `dec-whole-part`, `dec-frac-part`, `dec-div`,
`dec-round` and `dec-truncate`, so a scale of 64 kills the guest with
`!EXC=00`, a divide error.

`dec-from-text:147` set `scale = text-length frac-str` with no bound, so the
scale is the length of whatever text arrived. Sixty-four fraction digits is the
whole exploit.

**The scale has three producers and guarding the parser alone would have closed
one door.** `dec-mul:48` ADDS the two scales, so two scale-32 values reach 64 by
arithmetic with no text involved, and `dec-new` takes a scale from its caller
directly. That is the shape blu caught on row 19 itself (L-CAPABILITY), one
level down. The saturation therefore sits in `dec-pow10`, where every consumer
must pass, and the two producers that take a scale from input clamp as well so
the scale they report is the scale they actually carry.

Three guards, three ablations, each moving its own rows and nothing else:

| ablation | effect |
|---|---|
| `dec-pow10` saturation removed | row 6 dies `!EXC=00`; rows 1-5 unmoved |
| `dec-from-text` clamp removed | rows 4 and 5 do NOT fault (the saturation catches them) but read `-8.0000000000000000000000000000000000000000000000597994509810618382` and `-5.00101065172474983726` for inputs beginning `1.1234` |
| `dec-mul` clamp removed | row 7 reports scale 64; row 3, ordinary multiplication, is unmoved |

**The middle row is the one to read.** Without the parser clamp there is no
crash at all once `dec-pow10` is safe, and a legitimate-looking decimal string
becomes a NEGATIVE number. A campaign that hunts faults would have fixed
`dec-pow10`, seen the guest stop dying, and shipped that.

Row 8 is the discriminator: eighteen digits is fully representable and must
survive unclamped, so a guard that saturated at fifteen or ten would pass every
hostile row and fail only there. Row 3 does the same job for `dec-mul`.

**One thing measured and deliberately NOT changed.** `dec-find-dot:156`
compares `char-code (char-at s i) == 65`, which reads as ASCII `A` and looks
exactly like a typo for `.`. It is correct: in CCE the full stop IS 65 and `A`
is 41, measured against the depot seed. Left alone, and recorded here because
the next reader will reach for it (R-CCE).

Not guarded: `dec-mul`'s `raw = a.dec-mantissa * b.dec-mantissa` can still
overflow, and `dec-new` still takes any scale a caller names. Neither is a
divisor or a read offset, and neither takes its value from parsed input.

Not seed-affecting: absent from the compiler unit against a `Foreword--Fat16`
control, `Sut.cdx` byte-identical to the depot seed after the gate.
`codex/test/lib/decimal-test` is unchanged, and it is worth saying what that
test does NOT do: it never calls `dec-from-text`, so the parser had no runner
of any kind before this arm (L-GAP).

## The Schedule Parser Guards (`codex/test/apps/schedule-parse-guard`)

Track D item 19, same leg, and **the only chapter in this campaign with no
memory-safety defect at all.** Eighteen malformed inputs were run against it
before a line was written: empty text, one-word and two-word fragments, a bare
`between`, a lone `:`, an `am` with no time, a twenty-digit interval, spaces
only. Every one answered `None` or returned a `Sched`. Nothing faulted. Its
index checks are genuinely complete, and that is worth recording as a result
rather than a non-event.

**What it did instead was ACCEPT.** Four shapes of malformed text produced a
well-formed schedule that was not the one written, which is the same class as
`Pbkdf`'s truncated hash and `Decimal`'s negative parse: no crash, wrong answer.

| input | before | after |
|---|---|---|
| `weekly on wendsday at 9am` | `weekly Sun 9am` | `None` |
| `every 5 minutes between 9am` | `every 300s`, window silently dropped | `None` |
| `every 5 minutes between 9am and` | `every 300s between 9am and 12pm`, actually running to midnight | `None` |
| `daily at 99999:99999` | `daily at 99987:99999pm` | `None` |

The first is `sched-parse-day-name`'s final `else 0`: every unrecognised word
was Sunday, so a misspelled day scheduled rather than failing. The second is
`n >= 6` guarding the whole `between` clause instead of its arguments, so a
clause too short to parse was dropped and the caller got an UNRESTRICTED
schedule where a restricted one was asked for. The third defaults the stop hour
to 24 and then renders it through `sched-format-hour`, which maps 24 to `12pm`
-- a printed window that reads as noon and runs to midnight.

Three guards, three ablations, each moving exactly its own rows and nothing
else: the day-name sentinel (rows 7, 8), the `between` completeness check
(rows 9, 10), the hour/minute/day-of-month ranges (rows 11 to 14).

**Rows 1 to 6 and 15 are the controls and they carry the whole weight of the
range checks.** A guard that answered `None` to everything would pass all eight
hostile rows; only the legitimate forms catch it. `12:00am` (row 5) and
`monthly on 31` (row 6) are the boundary pair, since a range check written as
`hour > 0` or `day < 31` fails there and nowhere else.

Not seed-affecting: absent from the compiler unit against a `Foreword--Fat16`
control, `Sut.cdx` byte-identical to the depot seed after the gate.
`codex/test/final-batch-test`, which is what qualifies this chapter, is
byte-identical -- and it only ever passes well-formed strings, which is why
none of the four was visible to it.

## The Pattern Progress Guards (`codex/test/apps/pattern-progress-guard`)

Track D item 19, same leg. Two defects, both measured before a line was written,
and the first is the only heap exhaustion this campaign has produced from
ordinary text rather than from a crafted binary.

**`pat-match-many:181` recursed on whatever position its inner pattern left it
at, without requiring that the position MOVE.** A pattern matching zero
characters repeats forever, accumulating `acc & r.mr-value` on a heap with no
collector. `many (lit "")` reaches `OUT OF MEMORY` in about seven seconds.

**It is reachable from two words of English.** `pattern "many optional"`:
`many` takes `pat-parse-many-of`, `optional` sits at the end of the token list
so `pat-parse-optional` hits its `i >= n` case and answers `PatLiteral ""`, and
the result is `PatMany (PatLiteral "")`. Measured: `OUT OF MEMORY`. Nothing in
that input is malformed in any way a reader would notice.

**`pat-match-repeat:199` answered `mr-consumed = pos`, the ABSOLUTE position,
where every other matcher in the chapter answers a relative count.** At
position 0 the two coincide, which is exactly why nothing caught it: every
existing exercise of `exactly` starts at 0. At any other position the caller
adds position-to-position and overshoots, so
`and-then (lit "ab") (and-then (exactly 2 digit) (lit "cd"))` did NOT match
`"ab12cd"` -- a plain false negative on a pattern that is obviously correct.

| ablation | effect |
|---|---|
| zero-progress stop removed | row 8 dies `OUT OF MEMORY`; rows 1-7 unmoved |
| `mr-consumed = pos` restored | row 5 reads `matched(4)` for a two-character match and row 6 reads `no match`; row 4 unmoved |

**Row 4 is the discriminator and it is the whole reason the fix is a new
accumulator rather than a subtraction.** The position-0 case was already
correct; a "fix" that changed it would be trading one wrong answer for another,
and only that row would say so.

Not guarded: `PatRepeat p n` with a large `n` is still a long loop, but `n`
comes from `exactly` at a call site and no path in the English parser
constructs a `PatRepeat` at all, so there is no text that reaches it.

Not seed-affecting: absent from the compiler unit against a `Foreword--Fat16`
control, `Sut.cdx` byte-identical to the depot seed after the gate.
`codex/test/final-batch-test` is byte-identical; it exercises `pat-match` only
at position 0 and only with patterns that consume, which is precisely the
blind spot both defects lived in (L-GAP).

## The Glyph unitsPerEm Guards (`codex/test/apps/glyph-upem-guard`)

Track D item 19, the `ui/` leg. **`unitsPerEm` is a raw 16-bit field of the
head table and `gr-render-glyph:203` divides every outline coordinate by it.
Two bytes of a font file decide whether this chapter runs at all.**
`TrueType.codex ttf-read-head:89` reads it with `ttf-u16` and validates nothing.

Both defects measured on the unguarded chapter, using
`codex/test/truetype-bridge-test`'s own font byte for byte with only offsets
246 and 247 rewritten:

| unitsPerEm | result |
|---|---|
| 0 | `!EXC=00`, divide error, on the first glyph rendered (`R12=0x20`, the space) |
| 1 | `!EXC=08`, **DOUBLE FAULT**: with no divisor to bring the coordinates back, the width came out `0x2bc1` (11,201 px) and `w * h` sized the pixel buffer |

That is a third fault class for this campaign, after `!EXC=06` and `!EXC=00`.
The `upem = 1` case is the more interesting: it is not a crash in the parser,
it is an allocation whose size the file chose.

`gr-upem-ok` refuses a non-positive `unitsPerEm` and answers a 1x1 blank glyph;
`gr-clamp-dim` bounds each dimension at four times the requested ppem, floored
at 16 and capped at 1024. Both `gr-render-glyph` and `gr-render-glyph-aa` take
both guards, and the anti-aliased path is what sets the absolute cap, since it
supersamples 4x4 and allocates sixteen times the pixels.

Ablating the `upem` refusal kills row 3 with `!EXC=00` and leaves rows 1 and 2
untouched. Ablating the dimension clamp kills row 4 with `!EXC=08` and leaves
rows 1 to 3 untouched, row 3 still being refused by the other guard.

**Rows 1, 2 and 7 are the controls and they carry the arm.** Rows 1 and 2 are
the SAME font at its real 1024 and must still render 96 glyphs at 9x11, which
a guard that refused everything would fail. Row 7 is a legitimately different
`unitsPerEm` of 2048 rendering at 5x6: it is what says the clamp bounds the
hostile case without touching ordinary scaling.

**A finding in a chapter this lane does not own, verified and recorded here
rather than fixed.** `ttf-u16:12` reads `list-at buf off` and
`list-at buf (off + 1)` with no bounds check at all, so a TRUNCATED font kills
the guest inside `ttf-parse` before `GlyphRasterizer` is reached. `encode/` was
the first sweep's territory and `TrueType` is not in row 19; the census carries
it as an open item.

Not seed-affecting: `GlyphRasterizer` and `TrueTypeFont` both absent from the
compiler unit against a `Foreword--Fat16` control, `Sut.cdx` byte-identical to
the depot seed after the gate. `truetype-bridge-test`, `truetype-render-test`
and `truetype-test` are all byte-identical.

## The SafeTensors Read Bounds (`codex/test/apps/safetensors-bounds-guard`)

Track D item 19, the `ai/` leg. **The header parser was already defended and the
loaders were not**, which is the whole shape of this one. `st-parse-file`
answers `valid=False` for a short file and for a garbage header, and
`codex/test/forewords/foreword-safetensors.codex` already covered both. Nothing
covered what happens once a WELL-FORMED header describes a tensor that is not
there.

`st-read-u32:48` reads four bytes with four bare `list-at` calls, and
`st-read-f16-loop:209` and `st-read-bf16-loop:239` read theirs the same way. The
offset is `st-data-offset + stm-offset-start` and the count is
`st-element-count`, the PRODUCT of the declared shape. All of it comes out of
the file's own JSON.

Measured on the unguarded chapter: a header reading
`"shape":[1000000],"data_offsets":[0,4000000]` in front of twenty bytes of
payload dies `!EXC=06` with `R13=0xf4240` (1,000,000, the declared count)
against `RSI=0x5e` (94, the bytes that arrived).

`st-can-read` is written **subtractively** -- `count <= (list-length data - off)
/ width` -- so a shape whose product overflows a 64-bit integer cannot pass by
wrapping to a small number. Row 8 is that case, a shape of
`[4294967296, 4294967296]`. This is the same construction as `lz4-fits` and for
the same reason.

Three call sites, three ablations, each killing exactly its own row (4, 5, 6)
with `R13=0xf4240` and leaving the others untouched.

**Row 9 is the discriminator.** A tensor whose declared bytes end exactly at the
last byte of the file is legitimate and must still load; an off-by-one in the
bound shows up there and in no hostile row.

Recorded as cosmetic rather than fixed: row 8 reports `rows=4294967296` because
`st-rows` returns the declared first dimension whatever it is, while
`values=0` says nothing was read. No data is touched, so it is a label on a
refused tensor and not a second defect.

Not seed-affecting: absent from the compiler unit against a `Foreword--Fat16`
control, `Sut.cdx` byte-identical to the depot seed after the gate.
`foreword-safetensors` is byte-identical, including the two rows it labels
`PINS A DEFECT`, which are about `I8` dispatch and are untouched by this.

## The GPU Result Bounds (`codex/test/apps/gpu-result-guard`)

Track D item 19, the last chapter of the row. **`GpuProxy` builds command
buffers and parses result buffers, and only the second half belongs in this
census**: the buffer it parses is what a device wrote.

`gpu-read-u32:115` reads four bytes with four bare `list-at` calls.
`gpu-parse-result:194` calls it at offsets 0, 32 and 36 -- so it needs forty
bytes and checked for none. `gpu-is-complete` and `gpu-is-error` need four and
checked for none. `gpu-f32-bytes-to-tensor:153` reads `rows * cols` elements
with the count from its caller and no comparison to the buffer at all.

Measured on the unguarded chapter: `gpu-parse-result []` dies `!EXC=06`. **An
empty result buffer is not an exotic input. It is what a device that answered
nothing returns.**

Three guards, three ablations, each killing exactly its own row (3, 6, 7) and
leaving every other row standing. Ablation C reports `R13=0x2710` (10,000
elements) against `RSI=0x10` (16 bytes), which is the shape of the whole row 19
campaign in one register pair.

**Rows 5 and 9 are the discriminators.** Forty bytes is exactly enough for
`gpu-parse-result` and must parse rather than refuse; a tensor whose bytes end
exactly at the last byte must still load. An off-by-one in either bound shows
up there and in no hostile row.

`gpu-status-error` was already in this chapter's vocabulary, so a truncated
result reports it rather than inventing a channel. `gpu-is-complete` and
`gpu-is-error` both answer `False` on a short buffer, which is deliberate: a
buffer too short to hold a status does not say the work completed and does not
say it failed, and a caller polling on it waits rather than dying.

Not seed-affecting: absent from the compiler unit against a `Foreword--Fat16`
control, `Sut.cdx` byte-identical to the depot seed after the gate.
`codex/test/apps/gpu-proxy-test` is byte-identical, and it is the runner that
qualifies this chapter: every buffer it passes is one it built itself two lines
earlier, which is why none of this was visible to it (L-GAP).

## The Truncated Font Guard (`codex/test/apps/truetype-truncated-guard`)

Row 19's residue: found while guarding `GlyphRasterizer`, recorded as unowned
because `encode/` belongs to the first sweep, then assigned back to this lane.

`ttf-u8:10`, `ttf-u16:14`, `ttf-u32:25` and `ttf-read-tag:45` all read with bare
`list-at`, and **every field of every table is read at a fixed offset from a
table offset the FILE supplied**. A file that stops early is read past its end
rather than refused. Measured: `ttf-parse []` dies `!EXC=06`.

**The point worth keeping is where this sat relative to the rest of the
campaign.** The `ui/` leg's `upem` guards, landed hours earlier, are downstream
of `ttf-parse`. A truncated font never reached them, because the call that was
supposed to return the font killed the guest first. Guarding a consumer does
nothing for an input its producer cannot survive.

`ttf-byte-at` answers zero outside the buffer, and **zero was chosen because the
downstream guard already catches it**: a truncated head table yields
`unitsPerEm 0`, `gr-upem-ok` refuses that, and the font renders blank. The arm
shows the whole chain -- eight truncation points, every one reporting
`upem=0 render=1x1` rather than dying. `TtfFont` carries no validity field, so
there is nowhere to report a refusal to; zeros plus a downstream refusal is the
honest arrangement, and it is why this is not written as a `Maybe`.

`ttf-get-hmetric:273` took the same treatment for a different reason: it answered
`list-at hmetrics (list-length hmetrics - 1)` for an out-of-range index, which is
`list-at hmetrics -1` when the list is empty, and a truncated `hmtx` makes it
empty.

Ablated, row 2 dies `!EXC=06` with `RSI=4` against a zero-length buffer, and
**row 1 does not move** -- the intact 356-byte font still parses to 96 glyphs at
9x11. That control is what says the guard did not simply start answering zero
everywhere; row 10 re-reads `unitsPerEm` from the intact font and still gets
1024.

Not seed-affecting: `TrueType` absent from the compiler unit against a
`Foreword--Fat16` control, `Sut.cdx` byte-identical to the depot seed after the
gate. `truetype-bridge-test`, `truetype-render-test`, `truetype-test` and
`glyph-upem-guard` are all byte-identical.

## The Plug Self-Check Tier (`build/plug-selfcheck.ps1`)

`build/plug-oracle-test.ps1` is the only thing in the tree that runs a plug's
OUTPUT, and it does it by EXECUTING the emitted source, so it can only reach a
plug whose target toolchain is on this box: python, javascript, csharp, zig,
wasm. **Every binary and image emitter is outside it by construction.** There
is no SPIR-V runtime here, and installing one to grade a plug is not the trade;
the honest consequence was that those plugs had no runner at all.

This tier is the other half: **checks whose assertion is the emitted ARTIFACT
rather than its execution.** A SPIR-V word stream that validates and packs to a
module with the right magic; a GPT image of the declared length carrying a
protective MBR and an `EFI PART` header.

**An exit code is not one of those assertions, and 1.25 is why the tier says
so.** The `img` plug page-faulted before it sent anything, for as long as it
had been broken, and the host wrote 1,400 bytes of a 16,777,216-byte image
under an `OK` line. Every exit code in that chain was 0, because `img/run.ps1`
reads until the socket closes inside a bare `catch {}` and a guest that dies
mid-stream is indistinguishable from one that finished.

Entries as of 2026-08-16, 4 checks in about 370 seconds including plug builds:

| check | what it asserts |
|---|---|
| `spirv-text` | disassembly types consistent, constants at module scope, no dangling ids |
| `spirv-binary` | three hand-built bad modules are REFUSED (duplicate id, id at or above bound, dangling reference) |
| `spirv-emit` | the end-to-end word stream validates and the packed `.spv` carries magic `0x07230203` |
| `img-image` | both filesystem paths deliver `TotalSectors * 512` bytes with `55AA` at 510 and `EFI PART` at 512 |

**It is not in `build/build.ps1` and that is deliberate.** Every entry boots a
VM, several twice, for plugs the seed does not depend on. Run it by hand, before
a release, or when a plug changes.

**Both directions were fired before this was called done.** Pointed at the
pre-1.25 `img` binary the tier reports `IMG-FAT32: FAIL -- length 1,400,
expected 16,777,216`, both paths, exit 1; pointed at the fixed one, 4 passed 0
failed, exit 0. An unknown `-Only` name exits 2 rather than reporting a vacuous
pass over an empty set, which is the failure mode a filtered harness has.

**Every check that can take one is given `-Kernel`.** `test-spirv.ps1` did not
have the parameter and its `run.ps1` passed none, so the probe compiled against
whatever `build.ps1` last left in `build-output` -- measured here at
`D230B11D910D437D` against a seed of `1A33FB0E5C703CBD`. Both now thread it and
the tier defaults to `seed/Codex.cdx`.

**What belongs here next.** `elf` and `pe` emit containers with checkable magic
and section extents and have no arm; `t3isa` has `gate.ps1`, which is
machine-only (`D:\Toolchain-Ternary`) and would be a permanent skip. The tier
takes a plug the moment somebody writes an assertion over its artifact.

## The gpushow WGSL Sweep (`apps/gpushow/tools/validate-all.mjs`)

**A census of `kernels/*.wgsl` measures about half the shaders gpushow ships,
and reports full coverage while doing it.** Each demo page creates TWO shader
modules: the compute kernel, which the page FETCHES from `/kernels/<Name>.wgsl`
(`cube.html:35`), and a render shader written as a template literal inside the
page itself (`cube.html:50`). Only the first is a file. Measured 2026-09-01:
the 42 kernel files against **83 modules across the 40 pages**. A file-glob run
is not wrong so much as silently partial, which is L-DENOM with the population
wearing the costume of the corpus.

So the sweep drives the PAGES and records every module the page actually
creates, by wrapping `createShaderModule` through
`Page.addScriptToEvaluateOnNewDocument` before any page script runs. What it
grades is what a visitor compiles (L-ARTIFACT). Kernels no page fetches are
compiled standalone in the same browser so a file cannot hide by being
unreferenced; at the time of writing all 42 are fetched and none are orphaned.

```powershell
node apps\gpushow\tools\validate-all.mjs                 # all 40 pages
node apps\gpushow\tools\validate-all.mjs --only cube     # substring filter
```

Exit 0 every module compiled clean, 1 at least one WGSL error, **2 could not
run at all** -- no Chrome, no adapter, or no module created anywhere. The third
state is the point: a run that compiled nothing must not read as a pass.

**Calibrated in both directions before its first green was believed**, because
the two halves fail differently and a file-only instrument can only see one.
An undefined identifier appended to `cube.html`'s INLINE render shader was
caught at 4:18, and one appended to the FETCHED `CubeKernel.wgsl` at 91:34,
each exit 1; the clean tree is 83 modules and 0 errors, exit 0. Note the broken
kernel produced ONE module rather than two, because the page stops before
building its render shader, so a module COUNT is not a coverage check.

**It is an instrument, not a gate**, for the same reason as
`build/check-app-pages.ps1`: it needs an installed browser, so it cannot live
in `build/build.ps1`. Nothing invokes it automatically. Run it when you touch a
kernel, the WGSL plug, or a demo page.

Both tools ask the OS for a free CDP port rather than pinning one. The
single-file `validate.mjs` pinned 9223, which on a shared box lets a peer's
browser answer your query (L-SHARED); the sweep additionally uses ONE Chrome
for every shader instead of one launch per file.
