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

*Nothing open. The Update 43 entries were worked in the run-up to that release;
the Update 44 entry was found and worked during the release run at head 15686.*

## Done

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
