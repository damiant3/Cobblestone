# Something Seen During Release

A parking lot for things that are true all the time and only MATTER at
publication. An observation lands here instead of interrupting a dev cycle,
and the release skill reads the file at the step where it bites.

Damian, 2026-08-15, on why the split is real and not just convenient:
**publication and public are the same word.** A number that is wrong in a
document nobody outside the fleet reads is a number that is wrong in private.
It becomes a claim the moment we publish, and not before.

So the test for an entry here is not "is this true" but "does this only cost
us at the moment we make it public". A red gate does not go here. A stale
count does.

## How to use it

- **Adding:** append an entry with the date, what was seen, and the exact
  command that re-measures or fixes it. An entry with no command is a
  complaint, not an entry.
- **At release:** the release skill routes you here from Step 6. Work the open
  entries, then mark them done with the release cycle number rather than
  deleting them, so the next cycle can see what recurs.
- **Not a backlog.** If an item turns out to bite during dev too, it belongs in
  `CurrentPlan.md` or the owning app or quire register, and it should leave
  this file.

## Open

## Done

### Update 54 -- the seed on main was a pre-convergence Sut, and only the release's 4.3 check saw it

Found at step 0b, 2026-09-01. The full gate at head 21221 built `FCBABF07` in
one pass while main carried `18995A1A` (21215): same size, different content
hash, identical source. The lane's `-Internal` gate had printed the two-pass
P-STAGE2 refusal and the pre-convergence Sut was installed against it (the
wrong log grepped). Nothing between the land and the release compares a
landed seed to its own source: `-Internal` on the next lane merges the seed
down and compiles WITH it, which is consistent by construction. The chain's
seed check (`proofs.ps1`, `PerforceProcess.md` 4.3) is what caught it, on its
first release run. Re-measure at every release, and after any seed land:

```powershell
# whole-file, computed; never the header field (bytes 8..39 are STORED, not derived)
p4 print -q -o build-output/depot-seed.cdx //Codex/main/seed/Codex.cdx
(Get-FileHash -Algorithm SHA256 build-output/depot-seed.cdx).Hash
(Get-FileHash -Algorithm SHA256 build/output/Sut.cdx).Hash          # after a gate at head; must match
```

### Update 54 -- a new compiler builtin reached the wire and only the DDC could see it (third instance)

Found at step 4, 2026-09-01: Roslyn refused the emitted compiler with
`CS0103: The name 'print_uni' does not exist`. Fester's RESOLVE-mode frame
(main 20783, four days before the release) made the compiler call `print-uni`,
and `codex/plugs/csharp/CSharpEmitterExpressions.codex` had no emitter, so the
gate, the battery, the sweep and the poison battery were all green while the
witness could not build. Fixed at 21228 (one entry beside `print-text`). This
is Update 47's and Update 50's entry again. **A static diff of
`Builtins.codex` names against the plug's table is NOT the instrument**:
measured at this head it lists 103 of 264 names, nearly all bare-metal
devices (ports, MMIO, VMX, UEFI, processes) the compiler's own source never
calls, so it cannot tell the one that matters from the hundred that do not.
What decides is whether the COMPILER'S IR reaches a name the plug cannot
emit, and only the arm's build answers that. Re-measure at any CL that makes
the compiler call a builtin it did not call before (the DDC's steps 1 and 2,
about five minutes, no seed change needed):

```powershell
codex/plugs/csharp/emit-compiler.ps1 -Kernel seed/Codex.cdx -Out build-output/Codex.cs
dotnet build build-output/ddc-arm/CodexCs.csproj -c Release    # CS0103 names the missing emitter
```

### Update 54 -- an ir-fidelity row moved DROPPED to CARRIED, which is a fix, not a fault

Found at step 0c, 2026-09-01: `empty-list-element-type` reported CARRIED
against a banked DROPPED (`>>>` row, `unexpected 0`). COMPILER-30's witness
(PR 101, main 20944) had fixed the compiler five days earlier and nothing
re-baselines a case when the fix lands. Re-baselined at 21224. The skill
already says a DROPPED-to-CARRIED move is a re-baseline; what recurs is that
the lane landing a fix does not run `-Grade`. Re-measure at any CL that
touches lowering's type carriage:

```powershell
build/ir-fidelity/ir-fidelity.ps1 -Grade    # a >>> row naming your case means re-baseline its case.psd1 in the same CL
```

### Update 52 -- a battery batch can hand every test in it ANOTHER test's output, whole

Found at the Update 52 step 1, 2026-08-27. The first battery run went red
26 tests, all `FAIL_OUTPUT`, all confined to two of eight batches, and the
actuals were not wrong so much as SWAPPED: `repo-tombstone`'s actual held
`erp-posting-test`'s output byte for byte. Not the Update 48 truncation
class -- actuals were frequently LONGER than expected and none was a strict
prefix, so the `TRUNCATED`/`LENGTHS DIFFER` guards correctly stayed quiet.
The run had "re-batching 153 tests from death-batches" in phase 1 (the box
was still hot from the full gate); the clean re-run had none and cleared
all 26 with the identical compiler, which convicted the instrument. The
corrective run that evening (three full `-Tier all` batteries) had no
re-batching and nothing for the swap check to examine; its one red was
`smp-preempt` `FAIL_RUNTIME` (wall budget, poison battery), a different
class, cleared solo.

**Mechanism established and contained 2026-08-28 (red, red 20450).** It is
the byte-loss class `ExaminersAssay.md` "The batch stream can lose bytes"
had already measured on 2026-08-16: `test-compile-batch.ps1` assigns
blocks to names by SEQUENCE, so a lost block files every later block under
the wrong name; the tail lands exit 99, which triggers exactly the
death-batch re-batching this run showed, while the shifted exit-0 members
were KEPT and ran their neighbours' binaries. Three repairs, generators
and scripts together, each proven with a control arm:

- `test-compile-batch.ps1` invalidates the WHOLE batch (every member exit
  99, build.log noting `BATCH INVALIDATED`) when codex-vm reports dropped
  bytes or the stream ends short of the test count. Proven by killing the
  VM mid-batch: new script 20 of 20 members at 99; old script kept 3 at
  exit 0, which is the defect.
- `test.ps1` captures each batch child's stderr into `_batches/*.err`
  (main and rebatch launches both). The DROPPED report used to be written
  to a hidden console; that discard is why this entry's mechanism could
  not be established from the 08-27 run.
- `Get-FailHint` answers a `FAIL_OUTPUT` whose actual is byte-for-byte
  another test's expected with a `HOLDS ... batch misattribution` claim,
  before the length arithmetic, so the swap census below is now built into
  the rollup.

WHICH layer loses the bytes (guest serial, host writer, or parser) is
still unknown; the `.err` capture is the instrument that answers it on the
next occurrence. Hand census, for a run predating the fix:

```powershell
# after any red battery, before believing FAIL_OUTPUT: is the actual some
# OTHER test's expected? One line answers it for the whole run:
Get-ChildItem test-output -Directory | ForEach-Object { $a = Join-Path $_.FullName 'runtime.actual'; if (Test-Path $a) { $h = (Get-FileHash $a).Hash; foreach ($e in Get-ChildItem codex\test -Recurse -Filter *.expected) { if ((Get-FileHash $e.FullName).Hash -eq $h -and $e.BaseName -ne $_.Name) { "$($_.Name) HOLDS $($e.BaseName)'s output" } } } }
```

### Update 51 -- compile.ps1's binary write follows the PROCESS working directory, not the shell's

Found at the poison-build step 2026-08-26. `compile.ps1` writes its `-Out`
binary through .NET (`WriteAllBytes`, `compile.ps1:326`), and .NET's
current directory does not follow PowerShell's `Set-Location` inside a
harness-driven shell -- so a RELATIVE `-Out` landed under the session's
START directory (the red workspace) while the `-Log`, written through
PowerShell providers, landed where `Set-Location` pointed (red-main). The
compile exits 0; the consumer then refuses a missing file one step later,
which reads as a compile failure and is not one. Never bit a human at a
terminal, where the two directories agree. Re-measure / avoid:

```powershell
# absolute -Out and -Log always; or launch a child with the cwd set at spawn
Start-Process pwsh -WorkingDirectory $R -ArgumentList '-File','build\...'
```

### Update 50 -- a compiler change that reshapes the IR wire is a DDC change, and only the DDC sees it

Update 47's first entry recorded the forward direction: a csharp-plug
change is a DDC change, because the oracle harness does not compile the
compiler. This release paid for the converse. The 19558 lambda-lift fix
(plugs 1.70) put lifted `__lam_N` defs with unresolved type variables on
every plug's IR wire; the standing gate, the battery, the sweep and the
poison build all stayed green, and the csharp arm had been un-buildable
for days when the release reached step 4 (484 Roslyn errors). Fixed at
main 19775/19777 (dynamic lam params, `_Buf.dmap` for the CS1977 sites).
The general shape: the DDC is the only proof that consumes the IR wire
through a second implementation, so a wire-shape change's breakage waits
silently until a release runs it. Re-measure on any cycle that touched
the lift, lower-lambda, or `codex/plugs/csharp/`:

```powershell
codex/plugs/csharp/emit-compiler.ps1 -Kernel seed/Codex.cdx
dotnet build build-output/ddc-arm/CodexCs.csproj -c Release
```

### Update 49 -- two runners contradicted each other and no shipping image could exist

Found at publication 2026-08-21. `build/check-shipping-images.ps1` (red,
18237) refused ANY `DIAG.CFG` on the shipping `diag.img`; the same day
`build-diag.ps1` (root, 18645) started refusing to BUILD an image whose cfg
leaves a non-passive stage unnamed, and bakes `diag-default.cfg` when none
is given. Every buildable image therefore carried a cfg and the check
refused every one of them; nothing noticed until the release tried to build
the image that ships. Fixed by making the check accept a cfg byte-identical
to the checked-in default and refuse any other, naming the first differing
line; falsified both ways (sitting 11's image REFUSED, the default OK).
**The general shape: two guards landed the same day by two lanes, each
correct alone, jointly impossible, and the only runner that exercises them
together is the release.** Re-measure:

```powershell
build/boot/build-diag.ps1          # no -Cfg
build/check-shipping-images.ps1    # must print OK
```

### Update 49 -- the sitting configs were untracked, new, and name the box

Found at the pre-push scan 2026-08-21: five `build/boot/diag-sitting*.cfg`
files sat in `git status` as new paths, every one carrying
`b3 peer=192.168.6.141:7 ip=192.168.6.200`. The image check kept the box's
addresses off the mirror inside the image and nothing kept them off one
file over. Rule added to `PublicPush.md`: they never ship. Re-measure:

```powershell
git -C D:\Projects\NewRepository-<agent>-main status --porcelain | Select-String 'diag-sitting'
```

### Update 48 -- the README states the compiler's size twice and only one copy is checked

Found at publication 2026-08-20. `README.md` carried **63 chapters, 53,881
lines** in the headline "Verified" section and **64 / 55,645** further down.
`check-doc-counts.ps1` matched only the second, so the first had gone stale
unobserved since 2026-08-10 -- in the paragraph a first-time reader reaches
first. Fixed by making the headline agree with the checked claim.

**The general shape, and it is the NOMATCH hazard one step earlier:** a claim
the checker does not match is not merely unchecked, it is invisible, and a
document can hold two contradictory numbers while the runner reports 61 of 61
green. Before trusting a green count run, ask what the checker does NOT match.

```powershell
# every compiler-size claim in the public doc, checked and unchecked alike
Select-String README.md -Pattern '\d+ chapters|\d{2},\d{3} lines'
```

Also this run, and the reason the DDC paragraph is not on that list: it names
its own measurement date and seed, so it ages honestly rather than silently.
It was refreshed to the shipped seed anyway (`930FF7F1`, 2,872,563 bytes).

### Update 47 -- nothing was open, and two things bit at publication anyway

1. **A csharp-plug change is a DDC change.** reek's 16981 fixed four
   oracle-red lambda arms by casting every lambda to its delegate type; the
   oracle harness went 6/6 and nothing else looked, but the compiler source has
   106 `map_list` lambdas whose parameter type is a free type variable, and
   the cast spelled them `Func<T72, ..>`. Roslyn refused the C# arm and the
   DDC was INCONCLUSIVE at 21:22 with the operator waiting. Re-measure with
   `codex/plugs/csharp/emit-compiler.ps1 -Kernel seed/Codex.cdx` followed by
   `dotnet build build-output/ddc-arm/CodexCs.csproj -c Release` on any cycle
   that touched `codex/plugs/csharp/`; the oracle harness does not compile the
   compiler and cannot see this.
2. **A "preview" battery on a shelved change proves nothing the release can
   use unless it runs on the exact bytes that land.** The battery on blu's
   ATA fix from red's workspace was green and was on a pre-convergence stage
   (compile(FAD4F1E2) = A, compile(A) = B, the seed is B). Re-measure: hash
   `build/output/Sut.cdx` against the depot seed BEFORE launching a battery.


### Update 45 -- the release skill's own step 1 and step 4 were wrong

Nothing was open when this release started, and the release found two defects
in the procedure rather than in the tree. Step 1 said "run the FULL battery"
where a bare `build/test.ps1` runs the `lang` tier only (756 of 1,526 tests),
and step 4 said `ddc-witness.ps1` runs steps 3 AND 4 when that script has no
poison phase at all. Both are corrected in the skill. Recorded here because
the class recurs: **a procedure step that names an outcome instead of a
command is a step every reader executes differently.** Re-measure with
`build/test.ps1 -Tier all` and by grepping the runner for the phase it is
credited with.

### Update 44 -- README's DDC paragraph restated a boundary the owning doc had already corrected

`README.md` said the measured DDC boundary was "self-reproducing versus not"
and that a self-reproducing quine was the one thing that would survive.
`OperatorsManual.md` "The witness has a negative control" corrected both on
2026-08-11 (a frontend-IR-emission hook survives WITHOUT self-reproduction, and
the neutralisation is that a survivor is readable text in the IR and the C#).
Update 43 shipped the stale paragraph. Reworded 2026-08-16 to the corrected
statement. `check-doc-counts.ps1` cannot see this: it is a claim, not a count.

```powershell
# the two must agree on the boundary; the README paragraph is the copy
Select-String README.md, docs/OperatorsManual.md -Pattern 'self-reproducing|readable intermediate' | ForEach-Object { "$($_.Filename):$($_.LineNumber): $($_.Line.Trim())" }
```

Also this run: the seed triple, the img SHA-256 and the test-file count
(1,466 to 1,499) drifted as they do every cycle; `check-doc-counts.ps1` is 61
claims, 0 drifted after the edit. All 14 repo-relative README links resolve.

### Update 43 -- README's only broken link, and a published seed that had gone stale

Found by the release run at head 15253, both in `README.md`, which is the
document the public inherits first and the one nothing checks for either
defect.

**A broken link.** `docs/Designs/Active/Tools/HardwareBringUpPlaybook.md` moved
to `Done/` and the README kept pointing at `Active/`. `check-doc-counts.ps1`
counts numbers and cannot see a path. Audited all 14 repo-relative links in the
README this run; that was the only one broken.

```powershell
$readme = Get-Content README.md
$links = [regex]::Matches(($readme -join "`n"), '\]\((docs/[^)]+|build/[^)]+|codex/[^)]+|apps/[^)]+)\)') |
         ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
$links | Where-Object { -not (Test-Path ($_ -replace '/','\')) }
```

**A published seed digest inside another artifact.** The deskboot.img section
said "The seed inside it (`D9A6A7A2`) is the number that has to match".
`D9A6A7A2` is two seeds old, and the sentence is the kind that goes stale on
every release by construction. Rewritten so it cannot: the number that has to
match is whatever `seed/Codex.cdx` holds in the tree you build from.

**Worth knowing about that same section:** `build/boot/deskboot.img` is neither
on disk nor in the depot. The README documents its size and SHA-256 as a build
you perform yourself with `build/boot/build-option-a.ps1`, which does exist, so
the section is honest -- but a reader who takes the digest as something to
verify against a file we ship will not find the file. Left as it is because the
text already says the digest is "this build of the image, not a target to
reproduce"; noted here because it reads as a shipped artifact at a glance and
somebody will eventually go looking.
### Update 43 -- the doc counts drift, and every drifting one is in README.md

`build/check-doc-counts.ps1` is the L-COUNT runner. It is OPT-IN and off by
default, enabled per run with `$env:CODEX_CHECK_DOC_COUNTS = '1'` or per
workspace with a `.doc-counts` file in the repo root (`build/build.ps1`, the
block at "Counts in the docs go stale"). Off is the right default: this drift
is noise during a dev cycle, because nothing downstream reads the numbers and
correcting them churns a contended file for no signal.

It is not noise at release. Measured on a clean tree synced to main 15101,
**5 of 61 claims drifted and all five are in `README.md`**, which is the
document the public inherits first:

| claim | said | measured |
|---|---|---|
| apps (README) | 66 | 67 |
| app modules (README) | 1014 | 1019 |
| apps (README tree) | 66 | 67 |
| app modules (README tree) | 1014 | 1019 |
| test files (README) | 1457 | 1462 |

Do not carry these five numbers forward either: they are what one run produced
on one day, and the point of the entry is the command, not the table. Worked
2026-08-15 at main 15194: **13 of 61 claims drifted**, not five, and the set
was different again (foreword modules, the errors-test count, test files, the
seed triple). Corrected at main 15207 except the seed triple, which is
measured at the release head by definition and is the release run's Step 6.
**This entry recurs every cycle.** It is kept in Done rather than deleted
because the useful fact is that the drift is never the same drift.

```powershell
pwsh build/check-doc-counts.ps1        # per-claim table, exit 1 on any drift
```

A `NOMATCH` result is a worse failure than a `DRIFT` and is easy to skim past:
it means the doc changed shape, the claim's pattern stopped matching, and that
number has quietly not been checked since. Fix the pattern or the doc, but do
not leave it unmatched.

### Update 43 -- check-doc-counts.ps1's own header says it is not wired in, and that is now false

The generated header reads "Not wired into build.ps1. Wiring it in is a
decision about everyone's gate, not this script's to make." It IS wired in,
opt-in, at `build/build.ps1` in the `$countsOn` block. The prose is stale in
the direction that matters: it tells a reader the switch does not exist, so
nobody turns it on at release.

It is a GENERATED script and must not be hand-edited. The fix is in the
generator under `codex/build/`, regenerated and submitted together with the
script, or `build/check-generated-scripts.ps1` reports it as drifted and the
next regeneration discards the edit.

Fixed 2026-08-15 in `codex/build/checkdoccountsScript.codex` and
`build/check-doc-counts.ps1` together; the header now names the env var and
the `.doc-counts` file. `check-generated-scripts.ps1 -Only check-doc-counts`
reports match / 0 drift, which is the proof the two halves are byte-identical.
There is no `-Write` flag, so both sides are edited by hand and that check is
the only thing that says they agree.
