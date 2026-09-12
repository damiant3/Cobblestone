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

### Update 59 -- the diag image hash does not reproduce across workspaces, and the public text said it did

Found at step 5, 2026-09-12: red built the stick in red-main (884FD218) and
root built it in root-main (A4EF3A62) from the same source, seed and default
cfg, ten minutes apart. `DIAG.RCP` inside each names identical
`payload-sha256`, `bundled-sha256`, `efi-sha256`, `id` and `kernel`, and
differs only in `image=`, `kernel-path=`, `cfg=` (workspace paths) and
`built=` (a timestamp), all of which sit inside the image. So the image
hash is per build, and `TechnicalDetails.md`'s "the hash carries no
timestamp, so a different hash means something moved" was false; the
sentence now points at the three payload digests. The image that ships is
the one that was REHEARSED, by hash, and only that one. Re-measure at any
release where two workspaces build the stick:

```powershell
Compare-Object (Get-Content <a>\build-output\diag-recipe.txt) (Get-Content <b>\build-output\diag-recipe.txt)   # only image=, kernel-path=, cfg=, built=, image-sha256= may differ
```

### Update 59 -- a registry row landed `fixed` with no allocation site, and only the release gate ran the checker

Found at step 0b, 2026-09-12. `vec4-select` (blu, main 25547) reads `fixed`
in `Builtins.codex` and `build/check-builtin-alloc.ps1` had no site for it,
so the full gate refused (`vec4-select reads 'fixed' and no allocation site
is recorded`). No lane gate runs that checker (CostModel.md 5.2 says so:
L-NOGATE), so the row waited two days for a release. The emitter is the one
`vec-select` already pins, `emit-bivy-alloc st9 16`, an immediate 16 bytes
at either width; the site is recorded in main 25666. Re-measure at any CL
that adds or edits a `bs-alloc = "fixed"` row:

```powershell
build/check-builtin-alloc.ps1    # must print OK; a row with no site is refused by name
```

### Update 59 -- an ir-fidelity case banked under the pipeline, and `-Grade` never runs the pipeline

Found at step 0c, 2026-09-12. `inlined-return-type` (PR 139, main 25585)
reads CARRIED only with the optimizer on, because the inlined literal exists
in `opening` only after `inline-single-caller` ran; its own comment said so.
`-Grade` invokes its children without `-Passes`, so the release grade read
UNSUPPORTED (`>>>` row). Fixed in main 25670: a case says `passes = $true`
in its `case.psd1` and is compiled through the pipeline whatever the run's
mode. Re-measure at any new case whose subject an optimizer pass creates:

```powershell
build/ir-fidelity/ir-fidelity.ps1 -Grade    # the case must read its expectation with NO -Passes on the command line
```

### Update 58 -- preserve sampler rows and name the counter accurately

The release sampler's retained file began with a sample instead of a header.
`build.ps1` cleans `build-output` after the sampler writes its one header;
the sampler then appends rows. The published CSV restores the declared
column header and preserves all 686 retained rows, from 06:39:10 to 07:49:12
PDT on 2026-09-10. No missing early samples were reconstructed.

The column named `guests` counts VM-host processes, including run-list
supervisors. The Update 58 note therefore reports the measured process
counts and working sets, and leaves actual peak guest count unclaimed.
The instrument repair belongs to `docs/Designs/Active/Build/Build.md`,
"Open, unowned". Future release sampling must write outside cleaned output
directories. Check the first line before parsing a profile:

```powershell
Get-Content docs/Agents/box-release-2026-09-10.csv -TotalCount 1
```

### Update 56 -- the reconcile hid every EXACT-PATH ignore rule, and offered the withheld specs back

Found at the pre-push reconcile, 2026-09-08, before the push rather than
after it. `PublicPush.md` 2b's recipe pipes the depot-minus-mirror
difference into `git check-ignore --stdin` from PowerShell, which sends
CRLF. Measured both directions on the same three paths: with LF input
`check-ignore` returns every ignored path; with CRLF it returns only the
paths matched by a DIRECTORY rule and silently drops every one matched by
an EXACT-PATH rule, because the trailing carriage return is part of the
name it compares. The ignored set read 417 where it should read 430, and
the survivor list carried all ten third-party specifications, which is
precisely the class red added file by file on 2026-09-07. A survivor list
is what drives `git add`, so the under-report points the expensive way.
Recipe corrected in `PublicPush.md`; the tell is a survivor you know is in
`.gitignore`, and the check is `git check-ignore -v <one path>`, which
takes an argument rather than stdin and names the rule and line.

```powershell
# LF only, and never a PowerShell pipeline
[System.IO.File]::WriteAllText($f, (($diff -join "`n") + "`n"))
cmd /c "git -C <repo> check-ignore --stdin < `"$f`""
```

### Update 56 -- the sitting configs were withheld by one sentence and nothing else

Same reconcile. Seven `build/boot/diag-sitting*.cfg` survived
`check-ignore`, every one carrying `b3 peer=192.168.6.141:7
ip=192.168.6.200`. `PublicPush.md` has said they never ship since Update
49 and `build/check-shipping-images.ps1` keeps the same addresses off the
shipping IMAGE, and nothing at all kept them off the cfg one file over
(L-BODY). `build/boot/diag-sitting*.cfg` is now in `.gitignore`, falsified
both ways in a scratch repo: the seven and a third-party spec ignored,
`diag-default.cfg`, `diag.img` and a test chapter not.

```powershell
git check-ignore -v build/boot/diag-sitting15.cfg   # must name a rule
git check-ignore -v build/boot/diag-default.cfg     # must name none
```

### Update 56 -- the seed WAS the fixed point of its source, for the first time in three releases

Update 54's and Update 55's entries both recorded a seed on main that was
not the fixed point of its own source, and both asked for this
measurement at every release. Taken 2026-09-08 at the release head:
`build/output/Sut.cdx` (built by the full gate from the release source),
`seed/Codex.cdx` on disk and `build-output/bare-metal/Codex.cdx` are
byte-identical at
`D9CF240465C3D0BC40BA854834D0A890C13757230C191E99DD54163C851DB08B`,
3,217,563 bytes. So the battery proved the exact compiler being
published, and step 1's trap did not fire. The runner those entries asked
for is still not built; what closed it this cycle was red rebuilding the
seed with `-Repl` (P-REPL) after the batteries went red on the missing
exit mode, which is a different route to the same place and not a
substitute for the guard.

```powershell
(Get-FileHash -Algorithm SHA256 build/output/Sut.cdx).Hash
(Get-FileHash -Algorithm SHA256 seed/Codex.cdx).Hash    # must be equal
```
### Update 55 -- a copy-up drops a file whose target is writable, and submits the rest

Found at the seed land, 2026-09-02 23:17. The release seed had been staged
by hand over `seed/Codex.cdx` in the copy-up workspace (for the DDC, whose
plug builder pins that path), so the file was writable. `p4 copy --from`
answered `Can't clobber writable file` for that ONE path, integrated the
other three, and `p4 submit` landed a changelist that carried the new
digests and not the seed: main read `TechnicalDetails.md` for `81F9E817`
over a seed that was still `B25B5E95`, and the token had been released on
it. P-CLOBBER names this for adds; it is the same for a copy-up. Re-measure
before every seed copy-up, and never stage an artifact by hand into a
`-main` client:

```powershell
p4 -c <main-client> opened          # every file the copy meant to carry must be listed here
p4 print -q -o $t //Codex/main/seed/Codex.cdx; (Get-FileHash $t).Hash   # after submit, whole-file
```

### Update 55 -- lane seeds are not gate seeds: the scratch path never passed -Repl

Found across the release, 2026-09-02: every seed a lane landed (04BA03DB,
6AD77CCB, A2E240BA, B25B5E95) differed from the full gate's one-pass fixed
point of the same source by about 275,000 bytes, and each was a genuine
fixed point of its own build. blu found the cause at 22:58:
`build.ps1:260` builds the seed with `-Repl` (exit mode `ExitRepl`,
`opening.codex:1599`) and the lanes' scratch fixed point never passed it, so
a lane seed was a non-REPL compiler. P-REPL is in `PerforceProcess.md` now
(blu). Re-measure at any seed land:

```powershell
# the lane's candidate must equal what build.ps1's Invoke-BuildCdx produces: -Repl -MemMB 3072
build/compile.ps1 -Src build/output/Codex.codex -Out $c -Log $l -Repl -MemMB 3072 -Kernel seed/Codex.cdx
```

### Update 55 -- the seed on main was not the fixed point of its source, second release in a row

Found at step 0b, 2026-09-02. The full gate at head 22163 compiled the source
with the depot seed `04BA03DB` (fester, 22100) and converged in one pass on
`BBB9907C`: 277,490 bytes differ outside the signature and the file is 8 bytes
shorter. No compiler or foreword source moved after 22100, so the installed
artifact was not the compiler its own CL description proved. This is Update
54's entry again, one seed later, and the runner it asks for (a verdict file
from THIS run and an install script that refuses anything else, CurrentPlan
"THE SEED INSTALL IS GUARDED BY PROSE") is still not built: fester's
`build/sign-seed.ps1` unit is the nearest owner. Re-measure at every release
and after any seed land, whole-file and computed:

```powershell
# after a full gate at head: Sut must equal the DEPOT seed whole-file
p4 print -q -o build-output/depot-seed.cdx //Codex/main/seed/Codex.cdx
(Get-FileHash -Algorithm SHA256 build-output/depot-seed.cdx).Hash
(Get-FileHash -Algorithm SHA256 build/output/Sut.cdx).Hash
```

### Update 55 -- trapping arithmetic reached tests no gate had run since it landed

Found at step 0b, 2026-09-02, in the full gate's test-run phase: three chapters
red at head with `!EXC=06`, every one a plain `Integer` op that used to wrap
and now traps under COMPILER-36 (21676, 21798): `st-product` in the SafeTensors
foreword chapter multiplied a declared shape, `spark-noise-test`'s own hash,
and `numeric-test` feeding `newton` a diverging function whose recorded answer
was wrap garbage. `-Internal` runs only the chapters that cite a changed
chapter and nothing cites the compiler, so the full gate was the first runner
to reach them (L-NOGATE, fourth instance). Fixed in 22173. The same class
surfaced the same evening in the wasm plug's build of the arcade (games-backlog,
reek). Re-measure after any CL that changes what an operator does at a
boundary, before the release finds it:

```powershell
# the chapters with an .expected that no cited set reaches: run them, one guest at a time
build/bvt.ps1 -CodexCdx seed/Codex.cdx -SubjectsFile <list of codex/test/lib and codex/test/apps chapters> -Jobs 1
```

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
