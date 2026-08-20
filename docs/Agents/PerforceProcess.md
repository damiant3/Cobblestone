# Perforce Process for Agents

**Read this before you touch Perforce. Then never mention it again.**

Hitting a trap that is written down here is not a finding. It does not go in a
status message, a CL description, a backlog entry, or a memory file. It is not
evidence of diligence, it is evidence you skipped the file. Damian does not use
Perforce and never will, so every session that ends by explaining which `p4`
behaviour surprised it spends his attention on something he already paid to
have written down once. Fix it, say nothing, move on. (`CLAUDE.md` rule 10
already covers this; this is the specific case that keeps violating it.)

**If you find a NEW trap:** add one row to the index in section 3, in the same
shape. If it needs a recipe, add it to section 4 and point the row at it. If it
is mechanically checkable, add it to `build/p4-stale-check.ps1` so the next
agent never meets it -- and read what that script actually promises first, below,
because a check added to it inherits its blind spot.

**What the preflight does and does not do.** `build/p4-stale-check.ps1` runs as
a `build/build.ps1` preflight. Stated exactly, because the previous wording
overclaimed the half that matters most:

- **It FAILS the gate** on an open file behind depot head, and on an
  unresolved open file. Those set `$bad` and exit 1 with the fixing commands
  printed.
- **It WARNS, deliberately, on a possible dropped `add`.** The on-disk-but-not-
  in-the-depot scan does not set `$bad`; the script says why in its own comment
  (scratch and lock files land in that list too, and only the operator can tell
  them apart). So the list scrolls past inside a 13-minute build and the gate
  goes green.
- **The scan runs in BOTH paths, including when nothing is opened.** Read the
  message, because two of them mean different things:
  `nothing opened, but SEE THE LIST ABOVE` means a dropped add is on disk, and
  the bare `OK (nothing opened)` now means clean. It still only warns either
  way.

  It did not always. The script used to return at its `p4 opened` check,
  ABOVE the scan, so a CL whose contents are ALL adds -- a new test plus its
  `.expected`, a new doc, a new probe -- that lost every add left nothing
  opened and got the most reassuring message the script has in exactly the
  worst case. Measured 2026-08-15 on `BigWhite_Codex_red_main`: an untracked
  file on disk, `p4 status` naming it `reconcile to add`, and the preflight
  printing `OK (nothing opened)` and exiting 0. Fixed at main 15124, which
  moved the scan above the early return; re-verified at head 2026-08-15 on a
  second workspace, `Show-Untracked` running at line 81 and the warning at 84.

**So the gate does NOT refuse to run against a workspace that does not match
the depot.** It refuses on the two revision failures and reports the third.
`p4 status` is the command that sees a dropped add; it is what the checker
itself calls, and it is worth running by hand before any submit you care about.
Do not let any of this soften P-CLOBBER's `never wave past that message`.

---

## 1. The one habit: shelve, merge down, unshelve. In that order.

Almost every merge-down difficulty in this project is self-inflicted, and this
is the command that prevents it. Do it BEFORE the copy-up, as a matter of
course, not after Perforce refuses one.

```powershell
p4 shelve -f -c <CL>                    # EVERY CL with open files; -f, or a re-shelve does nothing
p4 revert //Codex/<stream>/...          # no -c: revert them ALL, see below
p4 merge -S //Codex/<agent> -r
p4 resolve -at                # nothing of yours is in the way, so this is safe
p4 submit -d "Merge down from main."
p4 unshelve -s <CL> -c <CL>   # your edits come back on top of current main
```

**The revert takes no `-c`, and that is the point of the line.** The shelve
step says "every CL with open files" because more than one is normal; a
`revert -c <CL>` then clears exactly one of them and leaves the rest in the
workspace, which is the self-conflict this whole section exists to prevent.
Revert the stream path, not a changelist. (Section 2's gate dance is the
opposite case and keeps its `-c`: there you are isolating ONE CL to unshelve
back on top, not clearing the stream.)

**A merge-down onto a clean stream has nothing to conflict with.** That is the
whole point. Every resolve decision agents agonise over exists only because
they left edits in the way and then had to merge against themselves.

Damian, 2026-08-13: *"they seem to forget the key method to avoiding problems
is to shelve all local edits, mergedown, then unshelve. It is getting tiresome
seeing agents act all surprised and have to report their victory over it in
every session on nearly every prompt."* It is not one agent. It is all of us.

---

## 2. Quick reference

### Gate dance (workspace must match the depot before any gate)

The compiler reads source from disk, so shelved-but-not-reverted edits
contaminate the build. Order is exact: shelve, revert, sync, clean, unshelve,
sync, resolve, check, build.

```powershell
p4 shelve -f -c <CL>                  # BEFORE revert, every time (P-ORDER)
p4 revert -c <CL> //Codex/<stream>/...  # shelving alone does NOT clear the workspace
p4 sync -f
p4 clean codex/... apps/...           # sync -f leaves strays behind (P-STRAY)
p4 unshelve -s <CL> -c <CL>
p4 sync                               # THIS schedules the resolve; unshelve does not (P-UNSHELVE)
p4 resolve -am
build/p4-stale-check.ps1              # FAILS on behind/unresolved; only WARNS on a dropped add
p4 opened                             # LOOK at it
p4 status                             # the dropped add the preflight will not fail on
build/build.ps1
```

### Copy-up to main

```powershell
p4 -c BigWhite_Codex_<agent>_main sync
p4 -c BigWhite_Codex_<agent>_main copy --from <YourStream>     # ONE target path (P-COPY1)
p4 -c BigWhite_Codex_<agent>_main resolve -am
p4 -c BigWhite_Codex_<agent>_main submit -d "copy-up: <description>"
p4 files //Codex/main/<anything you claim to have added>       # existence
p4 print //Codex/main/<that file> | ...                        # CONTENT (P-COPY1)
```

Always pass `-c <main-client>`. Your `.p4config` points at the dev client, so a
bare `p4 copy` targets the wrong workspace. `--from` takes the STREAM NAME, not
a depot path (P-COPYSTREAM).

On a seed-affecting copy-up the build token covers this step too, not just the
gate: do not write `build-complete` until the submit has landed. A copy-up that
touches no seed needs no token at all (`CoordinationProtocol.md` rule 1).

### CL lifecycle

```powershell
# create the CL: section 4.7, NOT bare `p4 change`, which blocks on an editor (P-EDITOR)
p4 edit -c <CL> <file>     # -c every time, or it lands in the default CL (P-DEFAULT)
p4 shelve -f -c <CL>
p4 revert -c <CL> ...
  ... gates ...
p4 unshelve -s <CL> -c <CL>
p4 sync ; p4 resolve -am
build/p4-stale-check.ps1
p4 shelve -d -c <CL>       # submit is refused while a shelf exists (P-SHELFSUBMIT)
p4 submit -c <CL>          # read the output: the CL may be RENUMBERED (P-RENUMBER)
```

### Merge-down by hand, when you have edits of your own

```powershell
p4 merge -S //Codex/<agent> -r
p4 resolve -as    # takes theirs ONLY where your side is unchanged; skips the rest
p4 resolve -n     # what is left is the short list that earns hand treatment
p4 resolve -am <file>   # named, three-way, keeps both sides
```

`-as` cannot guess wrong and it is how you find out which files you touched:
reading the merge output does not tell you, since every file prints the same
`must resolve content from` line. Each file it resolves prints its evidence:

```
Diff chunks: 0 yours + 9 theirs + 0 both + 0 conflicting   <- yours untouched, taken
Diff chunks: 1 yours + 11 theirs + 0 both + 0 conflicting  <- skipped, yours to merge
```

**`0 yours` is not proof your content is safe -- see P-REGRESS.** On a file you
recently landed it can instead mean the merge base already swallowed your side,
and "taken" is then a wholesale copy of a revision older than your work, with no
conflict shown. The reading above holds for files you did not write.

`0 conflicting` on a skipped file means `-am` merges it without loss. Never
bare-`resolve -at` a merge containing a file you changed yourself (P-BULKAT).

### Splitting a large CL

```powershell
p4 shelve -c <big-CL>
p4 revert //Codex/<stream>/...
p4 sync -f
p4 change                              # -> small CL
p4 edit -c <small-CL> <file>           # file is at depot state; edit from there
p4 submit -c <small-CL>
p4 unshelve -s <big-CL> -c <big-CL>
p4 resolve -am
```

### The whole fleet at once

```powershell
pwsh build/merge-down-all.ps1 -DryRun             # which streams have work; no approval
pwsh build/merge-down-all.ps1 -ApprovedBy damian  # the real thing
```

`-DryRun` is free. **The real run needs Damian to have asked for it in that
session**, the same gate `build/test.ps1` carries and for the same reason: it
submits a changelist into every workspace on the box and no token serialises
it. It skips a workspace with files open, and that skip is correct. Verify with
`p4 diff2 -q`, not with the summary it prints.

---

## 3. The trap index

One row per trap, in the shape of `docs/PM/Active/Stories/LESSONS.md`. Cite the
id when one is load-bearing. A row marked with a section-4 pointer has a recipe
there; everything else is fixed by the row itself.

**An id marked `(L)` is a LYING INSTRUMENT, and that class is worse than the
rest of the table.** The other rows describe operations that destroy work: they
hurt, and you find out. An `(L)` row is a command that answers confidently and
wrongly -- clean when it is blind, "nothing to resolve" when there is,
"up-to-date" when the content differs, a hash match over the bytes that cannot
differ. You do not find out, because the instrument already told you it was
fine. When one of these is load-bearing, do not reach for a second reading from
the same command; reach for a different command (L-FALSIF).

| id | the trap, and how it looks | the fix |
|---|---|---|
| P-NOEDIT | `EPERM: operation not permitted` or a similar write error from your edit tool. Perforce marks synced files read-only. | `p4 edit -c <CL> <file>` before modifying anything. Create the CL first if you do not have one. |
| P-UNRELATED | The CL description says "fix X" and the diff also carries Y and Z, because the file was open in your CL AND modified by other work. Perforce submits whatever is on disk. | `p4 diff` the file before submitting a small CL and confirm the diff matches the intent. If it carries extra, revert and re-edit just the lines you need. |
| P-ORDER | You revert before shelving, so the shelf holds an OLDER version than disk. The gate then builds pre-fix code and everything downstream is stale. | `p4 shelve -f` BEFORE `p4 revert`, every time. Re-shelve after any mid-gate edit. |
| P-STRAY | A build bakes in a name or file that "isn't there": a stray `.codex` from an abandoned branch, or a depot-side delete `sync` left on disk. `sync -f` restores TRACKED files and deletes neither. | `p4 clean codex/... apps/...` It respects `.p4ignore`. **Those two paths, and what READS each, measured 2026-08-15:** `apps/` because `sweep-apps.ps1:36` and `sweep-app-classes.ps1:61` both glob it recursively (not `compile.ps1`, which the old justification named); `codex/` because `concat-codex-self.ps1:60` globs `-Recurse -Depth 2` from `codex\compiler`. **`seed/` does NOT belong here**: `build.ps1:16` names `seed\Codex.cdx` explicitly and no `seed/` glob exists in any `build/*.ps1`. `build/` is a DIFFERENT mechanism wanting a different fix -- a depot-deleted script still on disk and still being dot-sourced -- and `build.ps1:164` already sweeps `*.bak`/`*.tmp`/`*.snap` repo-wide at Depth 3. If you widen these paths, say what reads the one you added. |
| P-UNSHELVE (L) | After unshelve onto a moved depot your file lacks what the merge-down brought in, and `p4 resolve -n` says "No file(s) to resolve". It is wrong: unshelve opens the file at the revision it was shelved AT, and does not schedule the resolve. | `p4 sync` (this schedules it), then `p4 resolve -am`, then `p4-stale-check.ps1`. Never reach for `-ay` to get moving; it drops every revision landed while you were shelved. |
| P-CLOBBER (L) | **The worst one here.** A CL holds edits plus new files. After the gate dance the edits land and the adds do not. `p4 submit` reports success, `p4 describe` shows only edits, the new files sit on disk looking fine and are in no depot. The tell is `Can't clobber writable file` printed under a line that says "unshelved, opened for add". **The tell is a per-client option and the fleet is SPLIT** -- `noclobber` clients refuse and print it; `clobber` clients do not refuse, they OVERWRITE the writable file silently, so an agent there meets no message, sees the add open normally, and concludes this row is stale. Measured 2026-08-15: `clobber` on the reek and val DEV clients; `noclobber` on the blu, fester and red dev clients, on every `*_main` client, and on `BigWhite_Codex_main`. | `p4 client -o \| Select-String Options` tells you which you are, and neither is the safe half: **noclobber loses an ADD loudly, clobber loses an EDIT silently** -- on a clobber client `p4 sync -f` and `p4 unshelve` will replace a writable on-disk file with depot content, no message, no refusal, uncommitted work gone. Never wave past the message if you get one. Delete the on-disk copy first, or `p4 unshelve -f`, then confirm `p4 opened -c <CL>` lists every file. A dropped add is invisible to every other check: it is not a conflict, not a stale revision, and absence has no diff -- and the preflight will not fail on it (section header). `p4 status` is what sees it. Verify against the shelf before deleting (see P-PRINTEOL). |
| P-PRINTEOL (L) | You hash the on-disk file against its `p4 print -o` copy to check they match, and it reports a mismatch that is not there: `print -o` TRANSLATES line endings on a `text` file. | Normalise CRLF to LF on **BOTH** sides before comparing: `([IO.File]::ReadAllText($f) -replace "`r`n","`n")` applied to each. The `-replace` form appears in section 4.6, which normalises only the expected side because that is what the test runner does -- copying it as-is fixes one side and leaves the mismatch. **Do not follow this row into section 4.2**, which the row used to point at: 4.2 is the byte-level line-ending REPAIR and it WRITES the file, so sending P-CLOBBER's inspect-before-you-delete check through it modifies the thing you were inspecting. A BINARY file compares exact through `print -o`, so binaries matching while only text files "differ" is the tell. |
| P-INTEGRATE (L) | You merge down, `resolve -at`, edit the resolved file, verify on disk, submit -- and the depot revision comes back WITHOUT your edit. Downstream tell: `p4 copy` answers "File(s) up-to-date" while the files plainly differ. The open action is `integrate`, not `edit`, and `-at` also leaves the file read-only. | `p4 edit <file>` before writing to a file open only for integrate, then `p4 print` the revision you created and grep it for your own text. **A REBUILT BINARY is an edit** and is the flavour that gets missed: after any merge-down touching a build output, `p4 fstat -Ol` both streams before copying up. Identical digests across the merge boundary mean your build did not land. Perforce's own answer is interactive `p4 resolve` (`e` to edit the merged result, `ae` to accept it), which records the edit AS the resolve so nothing is left as a bare integrate -- but **agents in this harness cannot drive it**, since it wants a terminal and tool stdin is the null device. That is why the `p4 edit` recipe exists. State which one you used. |
| P-BULKAT | A bare `p4 resolve -at` silently reverts your own submitted work, inside a CL called "merge down". The resolve succeeds, the submit succeeds, the file goes back to what it was. | `-at` is for files untouched on YOUR side, named individually. Use `p4 resolve -as` to partition (section 2), `-am` on anything it skips. The tell that you needed `-am` is that the file appears in your own recent submits. |
| P-ATLAZY | You `p4 resolve -at` a shared doc to take main's version, hand-edit your own paragraphs back on top, and submit. The submit reports the file as integrated. The depot gets main's version WITHOUT your edits, and `p4 status` afterwards says "reconcile to edit". `-at` records the result as identical to the source revision, so the submit lazy-copies that revision and never reads your disk file. Measured twice on `GitHubUpdate43.md`, 2026-08-15. | Do not edit a `-at`-resolved file before submitting. Submit the merge-down first, THEN `p4 edit <file>` and submit the edits as their own CL. Verify with `p4 print` of the depot head, not the workspace copy. |
| P-REGRESS (L) | After a fleet merge-down, definitions you landed hours ago are GONE, the merge resolved `0 yours + N theirs + 0 conflicting`, and nothing looked wrong until a gate went red on names you know you shipped. A later merge can credit your copy-up as already integrated and offer an OLDER sibling revision as "theirs". "0 yours" can mean the merge base swallowed your side. | After any merge-down touching files you recently landed, grep ONE key definition per recent CL before submitting the merge. Repair: `p4 print` main head over the local file, `p4 sync`, `p4 resolve -ay`, resubmit. |
| P-BACKWARD | You shelve, merge down, unshelve, and a file you never touched in that CL has gone BACKWARDS. No conflict, no warning. A shelf holds file CONTENT, not a diff, so it carries the pre-merge version of everything in it. | After unshelving onto a moved stream, diff the files you did NOT expect to change. Repair: `p4 revert <f>`, `p4 sync -f <f>`, `p4 edit <f>`, redo the edit on head. |
| P-SHELFBIG | A file you reverted out of the CL stays in the shelf and returns on the next unshelve, so the shelf silently stops matching `p4 opened`. `p4 shelve -f` does not remove it. | `p4 shelve -d -c <CL>` then `p4 shelve -c <CL>`. (P-BACKWARD is a shelf too OLD, P-CLOBBER a shelf too SMALL, this one too BIG.) |
| P-DEFAULT | `p4 submit -d "msg"` with no file argument submits the WHOLE default changelist, not the thing you were working on. Ungated compiler source has shipped under a description saying "merge down docs". Same root cause: `p4 edit` with no `-c` puts the file in the default CL. | Work in a numbered CL and submit with `-c`. A numbered CL cannot pick up strays. Keep the default CL empty, and read `p4 opened` immediately before any submit. Even a docs-only change wants a numbered CL. If it happens: `p4 change -f <CL>` rewrites a submitted description; say what it actually contains, then check whether it reached main. |
| P-SPEC | You pipe a spec into `p4 change -i` to set a description and it answers `Change N updated, removing 7 file(s)`. The next submit says "No files to submit". The spec you pipe REPLACES the whole form, so a form with no `Files:` section means a CL with no files. | Write the description when you CREATE the CL and never round-trip. If you must, splice by line index (section 4.1). Re-count with `p4 opened -c <CL>` afterwards; `removing K file(s)` is the only warning you get. Recovery: the files are in the default CL, intact; `p4 reopen -c <CL>`. |
| P-DIFFC (L) | `p4 diff` takes FILE arguments, not `-c <CL>`. **p4 is not silent about it and the row used to say it was:** raw, `p4 diff -du -c <CL>` prints two lines (`Usage: diff ...` / `Invalid option: -c.`) and exits 1. What silences it is the REVIEW PIPELINE: `p4 diff -du -c <CL> 2>&1 \| Select-String '^\+' \| Measure-Object` merges the diagnostic into the filtered stream, neither error line starts with `+`, the filter eats both, and the count comes back 0 = clean for a CL carrying 164. Measured four times across four workspaces. | Diff the paths: `p4 diff -du //Codex/<stream>/...`, or per file. For a SUBMITTED CL use `p4 describe -du <CL>`, which does take a changelist (164 added lines on the same CL the broken form called empty). **Test `$LASTEXITCODE`, not the filtered count** -- it survives the pipeline and was 1 the whole time. This generalises past Perforce: any `2>&1 \| Select-String` in this tree discards the command's own error message, so a failed command reports as a clean result. |
| P-COPY1 | Naming two target paths in one `p4 copy` writes the WRONG FILE'S CONTENT into one of them, silently. A second path is read as the TARGET, not a second source. It submits ONE file. The tells read as harmless wrapping: a target integrating "from" an unrelated source path, and `Locking 1 files` when you named two. | One path per `p4 copy`, and `p4 print` each target afterwards. `p4 files` is not enough: it reports existence and a fresh revision, both true, and says nothing about content. Recovery: `p4 filelog`, find the last good revision, `p4 edit -t <its filetype>`, `p4 print -q -o`, submit. Restore the FILETYPE too. |
| P-COPYSTREAM | `p4 copy --from //Codex/<agent>/...` yields `Wildcards not allowed in '//Codex///Codex/<agent>/...'`. | `--from` takes the stream name, unadorned: `--from //Codex/<agent>`. |
| P-ASCII | `p4 submit` fails with `No Translation for parameter` and dumps a hex blob. The description contains a byte above 0x7F. The server rejects them outright. | ASCII only in every description. Em dash, en dash, curly quotes, ellipsis, accents, emoji: all banned. Every AI agent does this; your training data is full of them. Use `--`, `"`, `'`, `...`. PowerShell here-strings: single-quoted `@'...'@`, and scan before submitting. |
| P-OPENED (L) | `p4 opened` LIES before a resolve. After a merge-down it can show unresolved `branch` resolves as `delete`; submitting on that reading wipes other agents' new files. | Run `p4 resolve` first, then trust `p4 opened`. |
| P-REOPEN | `p4 reopen -c <new-CL>` moves the file REFERENCE; the on-disk content stays as it was, including edits from the original CL. | Splitting a CL: revert the file first, then `p4 edit` it fresh in the target CL and make only the intended change. |
| P-INTERCHANGE (L) | `p4 interchanges` shows phantom entries forever in a multi-stream topology, because content reaches a target through indirect paths and it tracks only direct integration records. These entries CANNOT be cleared: `copy -f`, `merge -F` and `copy -n` all answer "up-to-date" / "already integrated" and do nothing, and `integrate -f` refuses a stream view outright. | Use `p4 diff2 -q` -- it compares content. When `diff2` is clean, a lingering `interchanges` row is finished business. Do not chase it and do not report it as a loose end. |
| P-RENUMBER | `p4 submit -c 9517` answers `Change 9517 renamed change 9520 and submitted`. A note citing the pre-submit number points at nothing. | Read the submit output for the final number rather than reusing the one you created. |
| P-RESURRECT | A merge can RESURRECT a file a peer just deleted, and the copy-up carries it back. | Diff the copy's file list against the CL's and account for every difference in BOTH directions. Counting is not enough: the usual check asks what is MISSING, and here the problem is the EXTRA file, which matching totals hide. |
| P-REDELETE | You RESTORE a file main deleted long ago (p4 print -o the old revision, p4 add, submit in your dev stream), and the next merge-down deletes it again: main's old delete revision was never integrated into your stream, so the merge credits it now and esolve -at takes the delete over your add. Measured 2026-08-16 on codex/test/text-append-alias (root 15981 added, 15982 merge-down deleted). | Copy up a restored file BEFORE any merge-down, or re-add it after the merge (p4 add again, submit, copy up); once main carries the new add, later merge-downs are quiet. p4 files the two paths after any merge-down that lists a delete you did not intend. |
| P-UNBRANCHED (L) | The other direction of P-RESURRECT, and it deletes a peer's work instead of restoring it. `p4 copy --from //Codex/<agent>` lists `//Codex/main/<file> - delete from //Codex/<agent>/<file>#none` for a file you never touched, and submitting that is a real delete on main. The stream is not behind: `p4 merge -n -S //Codex/<agent> -r` answers **`All revision(s) already integrated`** while the file is in neither the workspace nor `p4 have`, so Perforce has credited the branch without ever creating the file and no ordinary merge-down will fix it. Measured 2026-08-15 on `build/jonquil.ps1`, branched to main at 15170 and absent from reek across two later merge-downs. | Revert the copy first (`p4 -c <main-client> revert //Codex/main/...`), then re-branch that one file: `p4 merge -Af --from main <path>` (a stream view refuses `p4 integrate -f`, and `merge -f` is not an option), `p4 resolve -at`, submit it alone, then copy up again and read the list. Confirm the file is on disk with a plausible size before submitting; the branch resolve says nothing about content. |
| P-SHELFSUBMIT | `p4 submit` is refused while the CL still has a shelf ("has shelved files"). Bites on every CL where a gate follows a shelve, which is every CL that uses the build token. | `p4 shelve -d -c <CL>` first. |
| P-REMERGE | Copy-up refused with `Stream //Codex/<agent> cannot 'copy' over outstanding 'merge' changes`. Another agent landed while your gate ran. | Merge down again, submit the merge, then copy up. Budget two merge-downs per token hold. Your token does not prevent this and is not meant to: what lands under you is non-seed traffic, which takes no token. |
| P-EOL | A five-line change reports as the entire file replaced, because an edit tool wrote the result back as bare LF over a CRLF file. It turns the merge-down of any contended document into a conflict and hides your real change from review. | If `p4 diff` shows far more lines than you touched, count the endings and repair at the BYTE level (section 4.2). Never through `Get-Content`/`Set-Content`, which also rewrites the encoding of a `unicode`-typed file. |
| P-SELECTSTRING | `Select-String` misreads p4 `unicode`-typed files, which is most `.codex` by typemap. | Use the Grep tool (ripgrep) for content searches over depot files. |
| P-DAMIAN | `p4 opened -a` shows a file held by `Damian@BigWhite_Codex_main`, often many revisions behind head. | That is his editor checking a file out on open. It is not a pending change, it will not clobber yours, it needs no coordination, and it is not a hazard to report (Damian, 2026-07-21). Any OTHER client holding a file is a real agent and a real merge concern. |
| P-EXPECTED | A test passes every way you check it by hand and arrives RED in the battery. The `.expected` is one byte short: `Set-Content -NoNewline` leaves the file on `...yes` where the guest emits `...yes\n`. The harness strips CR from expected and compares exactly, so a missing trailing newline is a guaranteed fail. | **Every `.expected` in the tree ends with a trailing NEWLINE: 1237 of 1237, censused independently three times on 2026-08-15.** Write the newline. Do NOT copy the CR: every dev client is `LineEnd: local`, so the CRLF you observe is produced by YOUR sync and is a client property, not a depot fact, and the runner strips CR from the expected side anyway. The trailing newline is the half the harness actually tests. Verify with the RUNNER's own rule (section 4.6), never a `.Trim()` on both sides -- that normalises away exactly the difference the harness looks for (L-SIDECAR). A `text`-typed sidecar can also gain CR bytes on sync; `p4 retype -t binary <file>` if it keeps happening. |
| P-SEEDSTALE (L) | A green gate does NOT mean the seed matches the source. `build.ps1` compares SUT against stage1 -- the compiler built from current source being a fixed point of itself. It never compares the seed against the SUT and cannot usefully, so `hard fixed point in one pass` is exactly what a stale seed looks like. | Ask not "does this change what the compiler emits" but **"does this change the compiler BINARY"**. Renames, added or removed definitions and chapter moves all qualify: a pure rename once shortened the mangled names baked into the compiler and moved the seed 672 bytes. Verify against the DEPOT every session (section 4.3). |
| P-SEEDSWAP | The same check fires when main's seed moves AHEAD of yours: a lane lands a new seed while your gate runs, and the green gate you are holding certified a compiler that no longer exists. The gate cannot see it, because the source and compiler it was handed were consistent with each other, and the build token does not prevent it and is not meant to. | Compare THREE digests, not two: your workspace seed, your `Sut.cdx`, and the depot's. Sut equal to yours but not the depot's means the depot moved under you, so merge down, unshelve and RE-GATE, never rebuild a seed. Run it after every gate including apps-only CLs that took no token (section 4.3). |
| P-SIGNED (L) | A seed installed from `build/output/NewSeed.cdx` carries zeros where its signature belongs and fails `test-self-verify.ps1` with `SIGNATURE INVALID`. `NewSeed.cdx` is a copy of the unsigned `stage1.cdx`; the sign phase patches key and signature into `Sut.cdx` in place. | Install the SIGNED `build/output/Sut.cdx` **as the final step. One exception, and it is a step you will need: section 4.4 installs the UNSIGNED `NewSeed.cdx` deliberately, as an intermediate bootstrap to converge a seed built by an older one.** That is correct and this row is not an argument against it; read 4.4 before refusing it. The content hash (bytes 8-39) deliberately excludes the signature region so the fixed-point test works on signed and unsigned alike, which is exactly why a hash match will NOT catch this: measured on a wiped-and-resynced workspace 2026-08-15, `NewSeed.cdx` and `Sut.cdx` agree on bytes 8-39 exactly while their signature regions are 64 zero bytes and a real signature. Compare WHOLE FILES (section 4.3) and run the self-verify (section 4.4). |
| P-EDITOR | A command that wants a form hangs forever with no output. `p4 change` with no arguments, `p4 client`, and interactive `p4 resolve` all open `$P4EDITOR`, which is `Notepad.exe` on this box. Measured 2026-08-15: bare `p4 change` was still blocked at 15 s. Tool stdin is the null device and no agent can close the window. **The CL lifecycle block used to open with bare `p4 change`**, so the first line of the most-copied recipe in this file was one an agent could not run. | Never invoke the bare form. Use the `-o` / `-i` spec pipe for anything that would open a form: section 4.7 creates a numbered CL with its description in one shot. `p4 resolve` has the same problem and P-INTEGRATE already names it. |
| P-WIPED | You suspect the workspace itself: strays you cannot account for, a file that will not come back, a tree of unknown provenance. Reverting and syncing per-file chases it forever. | **Delete every top-level entry except the untracked dot files (`.claude`, `.git`, `.p4config`, `.agentgrid`) and `p4 sync -f`.** It is safe and it is fast, and it is the shortest path to a workspace you can reason about. Confirm with `p4 diff -sd //Codex/<stream>/...` (unopened files missing from the client), which must be EMPTY. Done on eight clients across four agents on 2026-08-15, with the per-client restore counts reported between 9,985 and 19,398 files; `-sd` empty every time, and a restored tree then passed a full `build/build.ps1` with the hard fixed point in one pass, which is the stronger proof: complete AND buildable. Shelve anything you care about first -- this deletes unshelved work by design. |
| P-SHELFBAD | `p4 unshelve` reports `corrupted during transfer (or bad on the server)`, `p4 print` fails the same way every attempt, and `p4 verify` answers **BAD!**. It looks exactly like data loss. | "Perforce refuses to hand it back" is not "the data is gone". Usually the archive holds intact content and the recorded digest disagrees with it, which a `shelve -f` replace races on. Recover from the archive (section 4.5). `BAD!` with two digests is recoverable; `BAD! (open failed)` means the archive file is missing and nothing can be recovered. |
| P-SELFAGREE (L) | A check passes for you every time and another agent reports it failing on main. The file it reads was rewritten on disk by a tool's own `-Update` (or by a build phase) and never opened for edit, so it is modified, untracked, and invisible to `p4 opened`. Every local run then compares the tool's fresh answer against the tool's own last answer and agrees with itself, while the depot copy the fleet reads is unchanged. `seed/constants.hash` did this for four days: `check-constants` said MATCH on the workstream and MISMATCH on main, and the copy-up that should have carried it never knew the file existed. | **`p4 diff -sa <file>` cannot see this and will tell you the file is fine.** It reports only OPENED files that differ, so on an unopened one it answers `file(s) not opened on this client`, which reads as agreement and is not. Use `p4 reconcile -n <dir>/...`, which lists modified-but-unopened files, and settle any hash-file question against `p4 print` of the depot copy rather than the workspace one (section 4.3 says the same for the seed). To prove which side is stale, `p4 sync -f` the file and re-run the check: it must FAIL. A check that cannot fail is not a check (L-FALSIF). |

---

## 4. Recipes the index cannot hold

### 4.1 Rewriting a CL description without emptying it (P-SPEC)

Splice by line index. A regex is the fragile way: `p4 change -o` output contains
the word `Description:` TWICE (the spec header comment carries its own), so an
unanchored pattern eats `Change:`, `Date:`, `Client:`, `User:` and `Status:` and
Perforce answers `Error detected at line 10` naming YOUR text, which reads as
though the description is malformed when the description is fine. PowerShell
`-replace` also treats `$` in the REPLACEMENT as a capture reference, and any
description quoting the debugger's `$` cursor token loses text.

```powershell
$lines = @(p4 change -o <CL>)
$di = [Array]::IndexOf($lines, 'Description:')
$fi = [Array]::IndexOf($lines, 'Files:')
if ($di -lt 0 -or $fi -lt 0 -or $fi -le $di) { throw "spec markers not found" }
$new  = @($lines[0..$di])
$new += ($descText -split "`r?`n" | ForEach-Object { "`t" + $_ })
$new += ''
$new += $lines[$fi..($lines.Count - 1)]
($new -join "`r`n") | p4 change -i
p4 opened -c <CL>      # the count is the whole defence
```

Neither failure loses files -- both are refused before anything is written, and
`p4 opened -c <CL>` still lists the full set. That is the DIFFERENT failure from
the emptied changelist, which prints `removing K file(s)` and succeeds.

Simpler, and it avoids the whole class:
`p4 --field "Description=$text" change -o <CL> | p4 change -i`.

### 4.2 Repairing line endings at the byte level (P-EOL)

```powershell
# Count first: is this actually the trap?
$b=[IO.File]::ReadAllBytes($f); $crlf=0; $lf=0
for($i=0;$i -lt $b.Length;$i++){ if($b[$i] -eq 10){ if($i -gt 0 -and $b[$i-1] -eq 13){$crlf++} else {$lf++} } }
"$f CRLF=$crlf bareLF=$lf"

# Repair: insert 0x0D before every bare 0x0A
$src = [IO.File]::ReadAllBytes($f)
$o = New-Object System.Collections.Generic.List[byte]
for ($i = 0; $i -lt $src.Length; $i++) {
    $c = $src[$i]
    if ($c -eq 10 -and ($i -eq 0 -or $src[$i-1] -ne 13)) { $o.Add(13) }
    $o.Add($c)
}
[IO.File]::WriteAllBytes($f, $o.ToArray())
p4 diff -du $f          # must show ONLY your hunks
```

### 4.3 Verify `Sut === seed` against the DEPOT (P-SEEDSTALE)

Compare against the depot print, not the workspace file: a workspace seed can be
stale, locally overwritten, or mid-resolve, and all three read as agreement.

```powershell
p4 print -q -o build-output/depot-seed.cdx //Codex/main/seed/Codex.cdx
(Get-FileHash -Algorithm SHA256 build-output/depot-seed.cdx).Hash
(Get-FileHash -Algorithm SHA256 build/output/Sut.cdx).Hash    # must match
```

If they differ and your CL does not touch the compiler's dependency set, the lag
is someone else's. Confirm it (`p4 changes //Codex/main/codex/compiler/...`
against `p4 changes //Codex/main/seed/Codex.cdx`, looking for a compiler CL newer
than the newest seed CL), then **rebuild and submit the seed rather than
reporting it.** It is a mechanical fix and the source is the authority.

**The check fires for two OPPOSITE reasons and they need opposite actions, so
find out which before doing either.** The paragraph above is the case where
main's seed is BEHIND main's compiler source. The other case is that main's
seed is AHEAD of yours: somebody landed a new one while your gate ran, and
your green gate certified a compiler that no longer exists. Rebuilding a seed
there would be exactly wrong.

Tell them apart by which side moved. Your `Sut.cdx` digest is printed by the
gate; compare it against your workspace seed as well as the depot's:

```powershell
(Get-FileHash -Algorithm SHA256 seed/Codex.cdx).Hash              # what you gated WITH
(Get-FileHash -Algorithm SHA256 build/output/Sut.cdx).Hash        # what you gated TO
p4 print -q -o build-output/depot-seed.cdx //Codex/main/seed/Codex.cdx
(Get-FileHash -Algorithm SHA256 build-output/depot-seed.cdx).Hash # what main has now
p4 changes -m 3 //Codex/main/seed/Codex.cdx                       # and who moved it
```

Sut equal to your seed but not to the depot's means the depot moved under you:
merge it down, unshelve onto it, and **re-gate**. Sut different from your own
seed means your source produced a new compiler and the seed land in 4.3b is
yours to do.

**Run this after EVERY gate, not only when you expect a seed.** Measured
2026-08-19: an apps-only CL that took no build token gated green while a new
seed landed on main mid-run, and nothing in the gate's own output says so. It
cannot: the gate certifies the source it was handed against the compiler it
was handed, and both were internally consistent. The token does not protect
against this either, and is not meant to -- it holds SEED-AFFECTING traffic
still, and the lane that landed the seed was holding it legitimately.

### 4.3b An internal seed land is fast now (Damian, 2026-08-16)

A seed land used to pay THREE full `build/build.ps1` passes under the token:
one to gate, one to "converge" the new seed, one to re-prove on the parent.
Measured 2026-08-16 that was ~27 minutes of gating and two of the three passes
were redundant against determinism. Internal lands now cost one fast gate and
two second-long checks. **The full `build/build.ps1` is for PUBLIC/release
builds only; internal lands use `build/build.ps1 -Internal`.**

- **Gate with `-Internal`.** It always proves the seed is a byte-identical
  self-fixed-point that boots (the fixed-point core plus the BVT); it runs a
  regression phase (jonquil, the plug phases, gen-scripts, deck-headroom,
  vm-differential, app-sweep) ONLY when a file that phase depends on changed in
  your workspace, and defers the rest to the next full gate. Measured: a
  foreword-parser change gates in ~2.5 min and a codegen change still pulls the
  codegen-sensitive phases (app-sweep, jonquil, vm-differential), against ~8.6
  min for the full gate. The phase-to-dependency map is in `build/build.ps1`
  beside the `-Internal` switch.

- **The convergence rebuild is unnecessary when the gate reports one-pass.**
  If `-Internal` prints `SUT === stage1 -- hard fixed point in one pass` and
  `build/output/Sut.cdx` differs from the on-disk seed (your change moved it),
  install Sut and self-verify. Do NOT run the gate a second time to "converge":
  a one-pass fixed point IS the proof that `seed := Sut` reproduces Sut, and
  signing is deterministic, so the second full build only re-derives a hash you
  already hold.

  ```powershell
  Copy-Item -Force build/output/Sut.cdx seed/Codex.cdx
  build/test-self-verify.ps1        # THE SEED VERIFIES ITSELF -- seconds
  ```

  If the gate does NOT print one-pass (rare: the old seed compiled your source
  to a Sut that is not yet a self-fixed-point), you are in the genuine
  convergence case, not the fast one. Take the fallback in 4.4: install the
  unsigned intermediate and rebuild until `SUT === stage1` in one pass.

### 4.4 Seed verification during copy-up (P-SIGNED)

**The fast path: a copy-up needs no rebuild on the parent.** Signing is
deterministic and measured (4.3): identical source hashes identically end to
end, so a seed proven a fixed point on your stream IS the seed the parent
produces, on two conditions, both checkable in seconds:

1. **Your stream is fully merged down from main.** `p4 merge -n -S
   //Codex/<agent> -r` prints nothing outstanding. If it does, merge, resolve,
   re-gate, then copy up. **Caveat (P-UNBRANCHED): `merge -n` can answer "All
   revision(s) already integrated" while a file is genuinely absent from your
   stream.** The copy-up carries only the files you name, so a main source file
   your stream is missing is exactly what the old parent rebuild caught and this
   check does not. If you have any reason to doubt the streams share source
   (a recent unbranched-file surprise, a partial sync), take the fallback and
   gate on the parent, where the build sees the parent's actual source.
2. **No untracked source would enter the seed.** `build/check-seed-orphans.ps1`
   exits 0. This is the ONLY thing the old parent rebuild actually caught: a
   `.codex` present on your workstream but not in Perforce is baked into the
   seed you built and then not carried by the copy-up, so the parent's tracked
   source stops reproducing it and main breaks. The check is a `p4 reconcile`
   over codex/compiler and codex/foreword (both feed the seed -- a foreword
   parser moved the seed on 2026-08-16), and it does in one second what the
   9-minute rebuild did.

With both green, the copy-up carries `seed/Codex.cdx` (the same Sut your gate
signed) with the rest of your files; there is nothing to build on the parent.

```powershell
p4 merge -n -S //Codex/<agent> -r        # must be empty (fully merged down)
build/check-seed-orphans.ps1             # must exit 0
# then the ordinary per-file copy-up carries seed/Codex.cdx too
```

**The fallback, only when the fast path cannot certify:** if the orphan check
names a file you mean to keep, `p4 add` it and re-gate; if you genuinely cannot
show the streams share source, gate on the parent as before. The old procedure,
kept for that case:

```powershell
# On the PARENT workspace with the copy-up CL unshelved:
build/build.ps1

# Sut === seed?  Already the fixed point, nothing to do.
# Sut !== seed but stage1 === stage2?  The fixed point is STAGE1's -- Sut was
# built by the OLD seed. Install the unsigned intermediate and converge:
Copy-Item -Force build/output/NewSeed.cdx seed/Codex.cdx
build/build.ps1                                   # now SUT === stage1 in one pass

Copy-Item -Force build/output/Sut.cdx seed/Codex.cdx   # the SIGNED fixed point
build/test-self-verify.ps1                        # must print THE SEED VERIFIES ITSELF

p4 revert //Codex/<PARENT>/seed/Codex.cdx
p4 edit -c <CL> //Codex/<PARENT>/seed/Codex.cdx
# copy the proven seed into place
p4 shelve -r -c <CL>
```

### 4.5 Recovering a shelf `p4 verify` calls BAD (P-SHELFBAD)

```powershell
# 1. The archive lives beside the depot path. Shelved revisions: 1.<CL>.<n>.gz
$a = 'D:\PerforceRoot\Codex\<stream>\<path>,d\1.<CL>.1.gz'
Copy-Item $a "$scratch\f.gz"

# 2. Plain gzip.
$in = [IO.File]::OpenRead("$scratch\f.gz")
$gz = New-Object IO.Compression.GZipStream($in, [IO.Compression.CompressionMode]::Decompress)
$out = [IO.File]::Create("$scratch\recovered")
$gz.CopyTo($out); $out.Close(); $gz.Close(); $in.Close()

# 3. NOT OPTIONAL: identify it by content. The archive may hold an earlier
#    iteration than the one you remember shelving.
Select-String -Path "$scratch\recovered" -Pattern '<a symbol only your change adds>'
```

**The archive holds the SERVER's line-ending form, which is LF**, and this
workspace's form is CRLF. Place the bytes directly and `p4 diff` reports the
ENTIRE FILE as one hunk, which is the tell. Convert with the byte-level pass in
4.2, then **rebase**: the recovered file is at the revision it was shelved
against, not head, so diff against depot head and re-apply whatever landed in
between before submitting. A final `p4 diff` showing only your own hunks is what
proves you did not silently revert someone.

Blast radius: `p4 verify -S //...@=<CL>` for this shelf, `p4 verify -q
//Codex/...#head` for anything live.

### 4.6 Checking a `.expected` the way the runner does (P-EXPECTED)

`build/test.ps1` strips CR from expected, trims nothing, and compares exactly.
Any hand check more forgiving than that is not a check.

```powershell
$exp = [IO.File]::ReadAllText("codex\test\$n.expected") -replace "`r",''
$act = [IO.File]::ReadAllText($outFile)
$exp -eq $act
```

`test-output\_results\<name>` holds only the verdict, not the output, so there
is no diff to read after a battery run. Reproduce with `build/test-run.ps1
-Kernel <cdx> -OutFile <f> -VmArgsFile <sidecar>` and compare with that rule.

### 4.7 Creating a numbered CL, headless, with its description (P-EDITOR, P-SPEC, P-DEFAULT)

Bare `p4 change` opens Notepad and blocks (P-EDITOR). The spec pipe is the only
headless way, and it is also the RIGHT way under P-SPEC, because the description
is written when the CL is CREATED rather than round-tripped onto an existing
one. It absorbs whatever is already in the default changelist, which makes it
the P-DEFAULT recovery as well: if you have been editing with a bare
`p4 edit`, your files are in the default CL and this moves them all in one
operation.

```powershell
$spec = p4 change -o          # includes the default CL's Files: section
$desc = @'
	First line of the description.
	Continuation lines are TAB-indented, like the form itself.
'@
$out = @()
foreach ($line in $spec) {
  if ($line -match '^\s*<enter description here>\s*$') { $out += $desc -split "`r?`n" }
  else { $out += $line }
}
$out -join "`n" | p4 change -i          # -> "Change N created with K open file(s)."
```

Read the `K` in that answer and check it against `p4 opened -c <N>`: this is the
one operation where P-SPEC's `removing K file(s)` failure cannot happen, because
you are creating rather than updating, but a mismatch still means the form was
not what you thought. Use a single-quoted here-string so `$` in the description
is literal, and keep it ASCII (P-ASCII).

**And the reason this cost anything at all:** the shelf held a finished, measured
change bundled with an unfinished one, so it sat unsubmitted for days. A finished
change that stands on its own ships as its own CL the day it is measured.
**Nothing in a shelf is safe, and a shelf is not a backup.**

---

## 5. Key principle

Perforce tracks file OPENS, not file CONTENT. There is no staging area.

- Multiple CLs can have the same file open; each sees the same on-disk bytes.
- Shelving saves the current on-disk bytes, not a diff.
- Reverting restores the depot version, discarding ALL on-disk changes.
- **The on-disk state is the source of truth for compilation.**

Every trap in section 3 is a consequence of one of those four lines.

---

## 6. Streams and clients

| Client | Stream |
|---|---|
| `BigWhite_Codex_<agent>` | dev stream working client |
| `BigWhite_Codex_<agent>_main` | main, the copy-up client |

Create a missing parent client with `p4 client -S //Codex/main
BigWhite_Codex_<agent>_main`, rooted at a separate directory.

**Dev streams isolate risky work.** If an agent breaks the seed, only that
stream is affected. A branch CL description names the parent stream and exact CL
it branched from, the last proven seed hash at that point, and a one-line
purpose -- so anyone has a path back to main without archaeology.

**A dev stream off a dev stream** saves speculative work without promoting it.
Routine, commands and live traps are in `docs/Agents/RiskyBusiness.md`.

---

## 7. Landing an outside pull request

**The mirror is downstream, so a PR is never merged where it was opened.**
`//Codex/main` is the source of truth and the git remotes are a publish
(`docs/Agents/PublicPush.md`). Clicking Merge on GitHub would put a commit on
the mirror that Perforce does not have. The work is re-applied in Perforce and
the PR is closed with credit. The contributor did everything right and their
change still cannot arrive the way their tooling expects.

Precedent worth reading before landing another: GitHub PR 63, Steve Howell, the
QEMU fallback, written up in `docs/PM/Done/GitHubUpdates/GitHubUpdate41.md`.

1. **Fetch and read it whole before touching Perforce.** It was written against
   main at the last push, so it can be behind. `gh pr checkout <n>` in the
   `-main` workspace holding the `.git` (several hold a stale one).
2. **Re-apply it in your DEV stream**, not the `-main` client. Dev streams are
   Perforce-only, so this is a file copy plus `p4 edit`/`p4 add`. It is not an
   integrate; there is no branch mapping between git and the depot.
3. **Their commits do not become CLs.** One CL per coherent change. Name the
   contributor and PR number in the description -- the only place attribution
   survives inside Perforce.
4. **Gate it like your own work**, because it is yours now. "It came from a PR"
   is not a provenance that substitutes for a gate.

**Keep your changes separable from theirs and say what each is for.** The
standard is Update 41's sentence about PR 63: *"The design and the first working
version are his."* Where they are right and we were wrong, say so in the same
breath. A credit that only flows one way is marketing.

**Closing.** GitHub (`damiant3/NewRepository`) is where contributions arrive;
GitLab is a backup Damian barely looks at, so do not hunt there or wait on its
state. Prefer to keep a PR open until the push carrying the work goes public.
At release, per PR: comment with thanks, the release it landed in and the public
commit; list the tweaks made on top with reasons; link the GitHubUpdate entry;
then close referencing that commit.

**The invariant is that the contributor can check our claim, not the ordering.**
Damian, 2026-08-13: *"obviously we are doing weird things outside normal git
procedure, so we adapt and do the best we can."* If a PR has to be closed before
its work is public, say that plainly and come back with the commit when it
lands. What is never acceptable is the silent close -- to everyone outside the
fleet that reads as a rejection, and it is the one outcome none of this is
worth.
