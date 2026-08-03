# Perforce Process for Agents

## Read this file BEFORE you touch Perforce. Then never mention it again.

This file exists so the traps below do not cost you anything. It is not a
place to discover things afterwards, and it is **not something to report on**.

**The rule, and it is not negotiable: hitting a trap that is written down here
is not a finding.** It does not go in a status message, a CL description, a
backlog entry, or a memory file. It is not evidence of diligence, it is
evidence you skipped the file. Every session that ends with an agent explaining
which Perforce behaviour surprised it is a session that spent Damian's
attention on something he already paid to have written down once. Fix it, say
nothing, move on. (`CLAUDE.md` rule 10 already covers this: "the steps of a
standard process that went as documented" and "a mistake you made and fixed
yourself with nothing left behind" are both on the do-not-report list. This is
the specific case that keeps violating it.)

**You should not need to remember any of it anyway.** The two failures that
actually destroy work are now enforced mechanically, not by memory:

| Enforced by | Catches |
|---|---|
| `build/p4-stale-check.ps1`, run automatically as a `build/build.ps1` preflight | An open file behind depot head, an unresolved file, and an `add` silently dropped by "Can't clobber writable file" |

The gate refuses to run against a workspace that does not match the depot, so a
skipped step surfaces as a red gate with the two fixing commands printed, not
as a wrong binary or a lost test. **You do not have to remember to run it.**

**If you find a NEW trap:** add it to this file in the same shape as the
entries below (symptom, cause, fix), and if it is mechanically checkable, add
it to `p4-stale-check.ps1` so the next agent never meets it. That is the whole
contribution. Do not also narrate it.

## Quick Reference: Copy-Up to Main

```powershell
# 1. Sync the main client
p4 -c BigWhite_Codex_<agent>_main sync

# 2. Copy from your dev stream to main
p4 -c BigWhite_Codex_<agent>_main copy --from <YourStream>

# 3. Resolve (usually no-op for clean copy-up)
p4 -c BigWhite_Codex_<agent>_main resolve -am

# 4. Submit on the main client
p4 -c BigWhite_Codex_<agent>_main submit -d "copy-up: <description>"
```

Always use `-c <main-client>`. Your `.p4config` points at the dev
client -- a bare `p4 copy` targets the wrong workspace.

**On a seed-affecting copy-up the token covers this step too, not just the
gate. Do not write `build-complete` until step 4 has landed.** The gate
result you are carrying up is only valid against the seed you gated
against; release the token before the copy lands and a seed-affecting CL
can arrive in the gap, which puts you back at the start with someone else
holding the token. **A copy-up that touches no seed needs no token at
all** -- see rule 1 of `CoordinationProtocol.md`. (You will also meet
`p4 copy`'s *"cannot copy over outstanding merge changes"* if main moved
after your merge-down. That is Perforce refusing a stale copy, which is
mechanics, not the reason the token exists.)

**ONE FILE PER `p4 copy`. Naming two target paths in a single copy
writes the WRONG FILE'S CONTENT into one of them, silently.** On
2026-07-28 this copy:

```powershell
p4 -c ..._main copy --from //Codex/reek `
   //Codex/main/.claude/skills/handoff/SKILL.md `
   //Codex/main/docs/Agents/reek-workplan.md      # DO NOT DO THIS
```

submitted ONE file, not two: `reek-workplan.md` on main was replaced
with the CONTENT OF `SKILL.md`, and `SKILL.md` never moved at all
(CL 11401). The tell was in the output and reads as harmless line
wrapping:

```
//Codex/main/docs/Agents/reek-workplan.md#102 - sync/integrate from
//Codex/main/.claude/skills/handoff/SKILL.md#1,#3
```

A target integrating "from" an unrelated source path is the whole
warning, and `Locking 1 files` when you named two is the other one.
Nothing errors. The next merge-down then pulls the corruption back
DOWN into your own stream and overwrites your good copy, so the
window to notice using `p4 print` is short.

Copy up one path at a time, and **`p4 print` each target afterwards**
rather than trusting the submit line -- the standing rule two sections
below ("after any copy-up, `p4 files` the artifacts you claim to have
added") is the same lesson and it is not enough on its own: `p4 files`
said the workplan existed and was at a fresh revision, which was true
and told you nothing about what was inside it. Check CONTENT, not
existence.

Recovery is cheap if caught: `p4 filelog` the file, find the last good
revision, `p4 edit -t <its filetype>`, `p4 print -q -o <path>
<file>#<rev>`, submit, then copy up again singly. Restore the FILETYPE
too -- the corrupt revision came back as `unicode` where the workplan
had been `unicode+C`.

## The Golden Rule

**Your workspace files must match depot state before running gates (build, test, BS3).** The compiler reads source from disk. If you have shelved-but-not-reverted edits, the on-disk files contaminate the build. The seed doesn't know about your changes -- it compiles what it reads.

## Before Running Gates

```powershell
# 1. Shelve your work (without -k: reverts on-disk files back to depot state)
p4 shelve -c <CL>

# 2. Force-sync to guarantee clean (handles stale/missing files)
p4 sync -f

# 2b. Remove strays that force-sync leaves behind. `p4 sync -f` restores
#     TRACKED files, but it does NOT delete untracked files, nor files
#     that were deleted on the depot yet still exist on disk. Our
#     "gather the files and build" loose system is sensitive to these:
#     concat-codex-self globs codex/**/*.codex and compile.ps1 pulls the
#     apps/ quires, so an orphaned or deleted-on-repo-still-local .codex
#     gets silently baked into the seed/build. `p4 clean` deletes strays
#     and restores the trees to exact depot state. It respects .p4ignore
#     (build-output/ and friends are left alone).
p4 clean codex/... apps/...

# 3. Unshelve your changes back to the workspace
p4 unshelve -s <CL> -c <CL>

# 3a. If the depot moved while you were shelved -- which is exactly what a
#     merge-down does -- unshelve leaves your files BEHIND head, and it does
#     not schedule the resolve. `p4 resolve -n` will tell you there is
#     nothing to resolve. It is wrong. These two commands are what fix it:
p4 sync                      # THIS is what schedules the resolve
p4 resolve -am               # three-way merge: your shelf + the new head
build/p4-stale-check.ps1     # refuses to continue if anything is still behind

# 4. NOW run gates
build/build.ps1
build/test.ps1 -Jobs 8
```

`p4 clean` is also the fix when a build mysteriously bakes in a name or
file that "isn't there" -- a stray .codex from a reverted/abandoned branch
or a depot-side delete that sync left on disk. When in doubt before a seed
rebuild or copy-up verification, `p4 clean codex/... apps/...` first.

## Common Mistakes

### 0. Editing a file without `p4 edit` first
**Symptom:** `EPERM: operation not permitted` or similar write error.
**Cause:** Perforce marks synced files read-only. You must `p4 edit -c <CL> <file>` before modifying any file. Without it, the file is locked on disk and your edit tool will fail.
**Fix:** Always `p4 edit -c <CL> <file>` before writing to a file. If you don't have a CL yet, create one with `p4 change` first.

### 1. Running gates with open edits
**Symptom:** Build fails with "Undefined name" errors for names you renamed, or "Duplicate definition" for names you added.
**Cause:** The seed compiles on-disk source. Your unsaved rename (`list-snoc` -> `list-push`) is on disk but the seed doesn't know the new name.
**Fix:** Shelve + revert before running gates.

### 1b. `p4 submit -d "..."` with no file argument submits EVERYTHING open
**Symptom:** A changelist whose description names one small change but whose
`p4 describe` lists files you never meant to send. Worst case, ungated
compiler source ships under a description that says "merge down docs".
**Cause:** `p4 submit -d "msg"` submits the **whole default changelist**, not
"the thing I was just working on". Every file left open from earlier work
rides along. It happened on 2026-07-18: a BACKLOG merge-down submit carried
three ungated compiler files with it (CL 9114), and the only reason it was
not worse is that the copy-up to main failed for an unrelated reason.
**Fix, in order of reliability:**
1. **Work in a numbered CL and submit with `-c`.** `p4 change -i` up front,
   `p4 reopen -c <CL> <files>`, then `p4 submit -c <CL>`. A numbered CL
   cannot pick up strays. This is the habit worth building.
2. Keep the default changelist EMPTY. If `p4 opened -c default` prints
   anything you are not about to submit, move it into a numbered CL first.
3. Run `p4 opened` immediately before any `p4 submit`, and read it. Naming
   files on the submit line (`p4 submit -d "msg" path/to/file`) does limit it
   to those files, but it is the weakest of the three because it only helps
   when you remember.

**If it happens:** the CL is the record, so fix the record. `p4 change -f
<CL>` rewrites a submitted changelist's description -- say what it actually
contains and that it was ungated. Then check whether it reached main
(`p4 print //Codex/main/...` or `p4 files`), because a dev-stream mistake
that never copied up is contained and a copy-up is not.

### 1c. `p4 change -i` with a hand-built spec EMPTIES the changelist

**Symptom:** you pipe a spec into `p4 change -i` to set a description, and it
answers `Change <N> updated, removing 7 file(s)`. The very next `p4 submit -c
<N>` says **"No files to submit"**, at the exact moment you were expecting the
work to land.

**Cause:** the spec you pipe in **replaces the whole changelist form**, not the
part you wrote. A form with no `Files:` section means a changelist with no
files, so every one of them is moved to the default changelist. Nothing is
lost and nothing warns you, because as far as Perforce is concerned you asked
for that.

**Fix:** never hand-build the form. Round-trip it, so the `Files:` section
survives untouched:

```powershell
$spec = p4 change -o <CL> | Out-String
$spec = $spec -replace '(?sm)^Description:.*?\r?\n\r?\n^Files:', "Description:`r`n`t<new text>`r`n`r`nFiles:"
$spec | p4 change -i
```

**The `^` anchors and the `m` flag are load-bearing.** Without them the
pattern matches the SPEC HEADER COMMENT, which carries its own
`#  Description: Comments about the changelist.  Required.` line about
eighty characters before the real field. The lazy `.*?` then runs from
inside the comment block to the first `Files:`, the whole header is
replaced, and `p4 change -i` answers:

```
Error in change specification.
Error detected at line 10.
Syntax error in 'The'.
```

which names your description text and points at the header, so it reads
like the description is malformed when the description is fine.

A regex is the fragile way to do this. Splicing on the two marker lines
cannot mismatch, and is worth the three extra lines when the description
is long:

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
```

Either way, **re-count with `p4 opened -c <CL>` afterwards.** The count
is the whole defence, and `Change N updated, removing K file(s)` is the
only warning you get.

Better still, **write the description when you create the CL** and never
round-trip at all. A description you have to go back and fix is a round-trip
you did not need to take.

**If it happens:** the files are in the default changelist, intact.
`p4 reopen -c <CL> //Codex/<stream>/...` puts them back, then `p4 opened -c
<CL>` to confirm the set is complete before submitting. Observed 2026-07-21
against CL 10069 (seven files, seed included); recovered with no loss.

**The general rule this belongs to:** do not run an exploratory or
spec-rewriting command with files outstanding. An open changelist is live
state, and a command that behaves slightly differently from your assumption
rearranges it silently -- which you discover mid-submit, when stopping to read
is most expensive. Shelve first, or experiment on a clean tree.

### Two ways the round-trip above corrupts the spec, both silent until submit

The round-trip is the right shape and both of these bit it on 2026-07-28.

**1. `p4 change -o` output contains the word `Description:` TWICE.** The spec
begins with a comment block, and one of its lines is
`#  Description: Comments about the changelist.  Required.` A regex written as
`(?s)Description:.*?Files:` matches from THAT line, so the replacement eats
`Change:`, `Date:`, `Client:`, `User:` and `Status:` and leaves your prose
where the fields belong. Perforce answers
`Error detected at line 10. Unknown field name '<first word of your text>'`.
Anchor to line start (`(?ms)^Description:.*?^Files:`), or do it by line index
rather than by regex.

**2. PowerShell `-replace` treats `$` in the REPLACEMENT as a capture
reference.** A description containing `$` -- and in this tree that is any
description quoting the debugger's `$` cursor token, or a shell snippet --
loses text or produces
`Too many entries for field 'Change'`. `-replace` is the wrong tool for
inserting arbitrary text; splice by line index:

```powershell
$spec = @(p4 change -o <CL>)
$di = [Array]::IndexOf($spec, ($spec | Where-Object { $_ -match '^Description:' } | Select-Object -First 1))
$fi = [Array]::IndexOf($spec, ($spec | Where-Object { $_ -match '^Files:' } | Select-Object -First 1))
$new  = @($spec[0..$di]) + @($body | ForEach-Object { "`t" + $_ }) + @('') + @($spec[$fi..($spec.Count-1)])
($new -join "`r`n") | Set-Content $tmp -NoNewline
Get-Content $tmp -Raw | p4 change -i
```

**Neither failure loses the files** -- both are refused before anything is
written, and `p4 opened -c <CL>` still lists the full set. Check it after any
failed `p4 change -i` rather than assuming the CL was emptied, which is the
DIFFERENT failure documented directly above.

### 2. Submitting a file with unrelated changes
**Symptom:** CL description says "fix X" but the diff also includes Y and Z.
**Cause:** The file was open for edit in your CL AND modified by other work (rename, idiom replacement). Perforce submits whatever is on disk.
**Fix:** Before submitting a small CL, `p4 diff` the file and verify the diff matches your intent. If it has extra changes, revert and re-edit just the lines you need.

### 3. Unicode in submit descriptions -- NEVER USE EM DASHES
**Symptom:** `No Translation for parameter` error on `p4 submit`.
**Cause:** The submit description contains non-ASCII characters.
The Perforce server rejects them outright. The submit fails, you
waste a round-trip, and the human has to watch you figure out why.

**EVERY AI agent does this.** Your training data is full of em
dashes and curly quotes. Fight the instinct. There is no place in
a CL description for any byte above 0x7F. None. Ever.

**Banned characters (non-exhaustive):**
- Em dash `--` (U+2014) -- use hyphen `-` (0x2D)
- En dash (U+2013) -- use hyphen `-`
- Curly quotes (U+201C, U+201D, U+2018, U+2019) -- use `"` and `'`
- Ellipsis (U+2026) -- use `...`
- Any accented character, any emoji, any non-ASCII symbol

**Fix:** ASCII only. Hyphen-minus, straight quotes, three dots.
If your description fails `p4 submit`, the first thing to check
is whether you snuck in an em dash. You almost certainly did.

### 4. Moving files between CLs without checking content
**Symptom:** Files from CL A end up in CL B with A's modifications baked in.
**Cause:** `p4 reopen -c <new-CL>` moves the file reference but the on-disk content stays as-is -- including all edits from the original CL.
**Fix:** If splitting a CL, revert the file first, then `p4 edit` it fresh in the target CL and make only the intended changes.

### 5. Reverting before shelving -- silently losing a fresh edit
**Symptom:** You made a fix, ran the gate dance, and the built SUT
does NOT contain your fix. The battery fails on a case you already
verified passing; the gate `Sut.cdx` hash differs from a SUT you
compiled by hand moments earlier.
**Cause:** You ran `p4 revert` (or `p4 revert -w`) on a file that had a
**newer on-disk edit than the shelf**, then `p4 unshelve`. The revert
discarded your fresh edit, and the unshelve restored the OLDER shelved
version. Shelving saves on-disk bytes at shelve time; a later edit that
was never re-shelved is invisible to unshelve. The gate then builds the
pre-fix code and everything downstream is stale.
**Fix:** **Always `p4 shelve` (or `p4 shelve -f`) BEFORE `p4 revert`,
every time**, so the shelf captures your latest on-disk bytes. The
correct gate dance order is exactly: shelve → revert → sync -f → clean
→ unshelve → build. If you edit a file AFTER unshelving (e.g. a fix
mid-gate), you must re-`shelve -f` before the next revert or you lose
it again.
### `p4 unshelve` silently drops an `add` -- "Can't clobber writable file"

**This is the worst one on this page. It cost every test added on
2026-07-13, and nothing noticed for hours.**

**Symptom:** A CL contains edits and one or more new files. You do the gate
dance (shelve, revert, unshelve), submit, copy up -- and **the edits land and
the new files do not**. `p4 submit` reports success. `p4 describe` shows only
the edits. The new files sit on disk looking perfectly fine, and are not in
the depot at all. Docs you wrote in the same CL now name tests that do not
exist.

**Cause:** `p4 revert` on a file opened for **add** removes it from the CL
but **leaves the file on disk** (correctly -- it was never in the depot to
restore). When you then `p4 unshelve`, Perforce finds a writable file already
sitting there and prints:

```
//Codex/blu/codex/test/smp-cores.codex#none - unshelved, opened for add
Can't clobber writable file D:/.../codex/test/smp-cores.codex
```

The first line makes it look fine. **It is not fine.** The second line means
the add was **not opened**. The file is untracked, the CL now holds only the
edits, and the submit is a success that leaves your new files behind.

**A dropped add is invisible to every check you would normally run.** It is
not a conflict. It is not a stale revision. It is not an unresolved file.
`p4 opened` does not list it, so nothing is out of date. It is simply
*absent*, and absence has no diff.

**Fix:** Never wave past `Can't clobber writable file`. Either delete the
on-disk copy before unshelving, or `p4 unshelve -f` to force the clobber, and
then **check `p4 opened -c <CL>` actually lists every file you expect** before
you submit.

**Detect (do this every time):**

```powershell
build/p4-stale-check.ps1     # lists on-disk files that are not in the depot
p4 files //Codex/main/<the-file-you-added>   # after copy-up: is it really there?
```

The rule that would have caught it in seconds: **after any copy-up, `p4 files`
the artifacts you claim to have added.** A doc that names a test is not
evidence the test exists.

### An edit on top of an `integrate`-only open is DROPPED at submit

**Symptom:** you merge down, `p4 resolve -at`, then edit the resolved file,
verify your text is on disk, submit -- and the depot revision comes back
WITHOUT your edit while the workspace file still has it. No conflict, no
warning, and the submit line says the file was submitted. Downstream, the
tell is `p4 copy` answering **"File(s) up-to-date"** while the two files
plainly differ, because the integration record is satisfied even though the
content is not.

**Cause:** the file's open action is `integrate`, not `edit`. A resolve that
took theirs (`-at`) is a "copy from", and it also leaves the file READ-ONLY
on disk while still open, so the first thing an editor hits is EPERM;
clearing the read-only bit by hand gets the write through to disk but does
not change the open action, and the submit carries the resolved content.

Measured 2026-07-29: a whole handoff resting-state section was written,
verified on disk, submitted, and absent from the depot revision. It took two
recoveries -- the first because the same session had already lost the section
once to a bulk `-at` over its own file.

**Fix, and it is two commands:** `p4 edit <file>` before writing to a file
that is open only for integrate, so the action includes an edit. Then
**`p4 print` the depot revision you just created and grep it for your own
text.** The standing rule two sections above -- check CONTENT, not the
submit line -- is written about copy-up to main, and this is the same rule
applied to your own dev-stream submit, which is where it had not been.

**Perforce has a first-class way to do this and the section above spent two
recoveries not using it.** Editing on disk after a resolve is working around
the resolve rather than through it. `p4 resolve` interactively offers, from
`p4 help resolve`:

```
Accept:   ae   Keep merged and edited file.
Edit:     e    Edit merged file (read/write).
```

So the sanctioned hand-merge is one step: `p4 resolve`, `e` to edit the
merged result, `ae` to accept what you edited. The edit is recorded AS the
resolve, so the open action is never left as a bare `integrate` and there is
nothing to drop at submit. Use it whenever you are merging a file by hand --
which for an agent means **any file you have also edited yourself, above all
your own workplan**, since `-at` over that is the other half of this trap.

**Agents in this harness cannot drive it**, because interactive `p4 resolve`
wants a terminal and an editor and the tool stdin is the null device. That is
the whole reason the `p4 edit` recipe above exists, and it is verified: it was
used on 2026-07-30 to recover a workplan section the submit had dropped, and
the depot revision was checked by content afterwards. **State which one you
used.** A human hand-merging should reach for `e`/`ae`; an agent uses
`p4 edit` plus the `p4 print` check, and neither should be mistaken for the
other being unavailable.

**And never bulk-`resolve -at` a merge that includes a file you changed
yourself.** `-at` is for files untouched on your side; on your own file it is
a silent revert. Resolve those by hand.

**`p4 resolve -as` is how you find out which files those are, and it cannot
guess wrong.** The rule above requires knowing which side of a merge each file
changed on, and reading the merge output does not tell you: every file prints
the same `must resolve content from` line whether you touched it or not.
`-as` is safe-automatic -- it takes theirs only where **your** side has no
changes, and SKIPS every file with edits on both sides -- so one command
partitions the merge for you. Each file it resolves prints its own evidence:

```
Diff chunks: 0 yours + 9 theirs + 0 both + 0 conflicting   <- yours untouched, taken
Diff chunks: 1 yours + 11 theirs + 0 both + 0 conflicting  <- skipped, yours to merge
```

Then `p4 resolve -n` lists exactly what is left, which is the short list that
earns the hand treatment. Used 2026-07-31 on a ten-file merge-down where every
file came back `0 yours` (no hand-merge was needed at all, and that was
established rather than assumed), and again the same session on a five-file one
where it correctly skipped the two workplans holding edits from both sides.
**`0 conflicting` on a skipped file means `-am` will merge it without loss**;
reach for the `p4 edit` recipe above only when a file actually conflicts.

### An unshelve restores the SHELVED revision over a newer file, silently

**Symptom:** you shelve, merge down, unshelve, and a file you never touched in
that CL has gone BACKWARDS. Not a conflict, not an unresolved file, no
warning. Whatever landed on that file between the shelve and the unshelve is
simply gone.

**Cause:** a shelf holds file CONTENT, not a diff. A shelf made before a
merge-down carries the pre-merge version of every file in it, so unshelving it
after the merge writes that version back over head. This is the
ValPostMortem "accept ours -- already incorporated" accident wearing a shelf
instead of a resolve: the same silent revert, arriving through a mechanism
nobody thinks of as a resolve at all.

Measured 2026-07-27: a workplan lost an entire cross-lane section this way and
nothing said a word.

**Fix:** after unshelving onto a stream that has moved, **diff the files you
did NOT expect to change**. If one went backwards:

```powershell
p4 revert <file>
p4 sync -f <file>
p4 edit <file>
# redo the edit on top of head
```

### `p4 revert` of an `add` leaves the file writable, and `p4 print -o` lies about whether it matches

The companion to the dropped-add trap above. `p4 revert` on an `add` leaves the
file on disk and writable, which is what makes the next `unshelve` fail with
`Can't clobber writable file`. Deleting it first is correct -- **but verify it
matches the shelf before you delete it**, because if it does not you are
throwing away the newer of the two.

**The verification itself has a trap.** `p4 print -o` TRANSLATES line endings
on a text file, so a raw hash compare of the on-disk file against its shelved
copy reports a mismatch that is not there. Normalize before comparing:

```powershell
$a = (Get-Content $onDisk -Raw) -replace "`r`n","`n"
$b = (Get-Content $printed -Raw) -replace "`r`n","`n"
$a -eq $b
```

A **binary** file compares exact through `p4 print -o`, which is the tell: if
the binaries match and only the text files "differ", it is the translation and
not your content.

### Unshelving onto a moved depot: `p4 resolve -n` lies to you

**Symptom:** You shelve work, merge down from main, unshelve -- and your
file no longer contains what the merge-down just brought in. `p4 resolve
-n` says **"No file(s) to resolve"**, and `p4 fstat` shows no `unresolved`
flag. Everything looks clean. It is not.

**Cause:** `p4 unshelve` restores the shelved bytes and opens the file at
the revision it was shelved **at**, not at head. A merge-down moves head.
So the file is now the shelved content, and every revision submitted in
between is missing from your copy. **Perforce does not schedule the
resolve at unshelve time** -- only `p4 sync` does that. Until you sync, the
one command you would reach for to check reports that you are clean.

Measured 2026-07-13 by reproducing it deliberately: after a merge-down, an
unshelved `BACKLOG.md` sat at `haveRev 29` against `headRev 31`, with
`p4 resolve -n` reporting nothing to do.

**The depot is not at risk from doing nothing** -- `p4 submit` does block on
an out-of-date file. The risk is the *recovery*: an agent who has just been
told there is nothing to resolve reaches for `p4 resolve -ay` (accept
yours) to get moving, and that silently drops every revision another agent
submitted while the work was shelved. This is the same shape as the
move-trap below: the damage does not appear where the mistake was made.

**Fix -- two commands, and they are not optional:**

```powershell
p4 unshelve -s <CL> -c <CL>
p4 sync                      # schedules the resolve unshelve did not
p4 resolve -am               # three-way merge; keeps your shelf AND the new head
build/p4-stale-check.ps1     # asserts nothing is still behind
```

Verified: the auto-merge keeps both sides (1 chunk yours, 6 theirs, 0
conflicting), and the guard fails before the fix and passes after.

**Detect:** `build/p4-stale-check.ps1` compares every opened file's
`haveRev` against `headRev` and refuses to pass if any is behind or
unresolved. Run it after every unshelve and before every submit. It exists
because the built-in check answers the wrong question.

**Detect:** After a gate build, compare the gate `build/output/Sut.cdx`
hash against a SUT you compiled by hand from the same source. A
mismatch means the gate built different bytes than you think -- usually
a lost edit. `Get-FileHash build/output/Sut.cdx` is two seconds; a lost
edit is an hour.

### A shelf `p4 verify` calls BAD is usually still recoverable

**"Perforce refuses to hand it back" is not "the data is gone."** These are
different claims and only the first one is what a failed unshelve proves.

**Symptom:** `p4 unshelve` reports `corrupted during transfer (or bad on the
server)` for one or more files and does not open them. `p4 print` fails the same
way, with the same two digests, every attempt. `p4 verify -S //Codex/<stream>/
<path>@=<CL>` answers **BAD!**. It looks exactly like data loss.

**What it usually is:** the archive holds intact content and the *recorded*
digest disagrees with it. Verified on shelf 9824 (2026-07-20): the archive
gunzipped cleanly to the complete file, and its MD5 equalled the digest
`p4 verify` prints as the **actual** value. Metadata and archive simply described
different versions of the file, which is what a `shelve -f` replace races on.

**Recovery, and it works:**

```powershell
# 1. The archive lives beside the depot path, one file per revision.
#    Shelved revisions are named 1.<CL>.<n>.gz
$a = 'D:\PerforceRoot\Codex\<stream>\<path>,d\1.<CL>.1.gz'
Copy-Item $a "$scratch\f.gz"

# 2. Gunzip it. The archive is plain gzip.
$in = [System.IO.File]::OpenRead("$scratch\f.gz")
$gz = New-Object System.IO.Compression.GZipStream($in, [System.IO.Compression.CompressionMode]::Decompress)
$out = [System.IO.File]::Create("$scratch\recovered")
$gz.CopyTo($out); $out.Close(); $gz.Close(); $in.Close()

# 3. Confirm it is the version you wanted, by content, before trusting it.
Select-String -Path "$scratch\recovered" -Pattern '<a symbol only your change adds>'
```

**Step 3 is not optional.** The archive may hold an earlier iteration than the
one you remember shelving. Grep for something only the final version contains
(here: the last feature added). A recovered file you have not identified is a
guess.

**The step that is easy to get wrong: the archive holds the SERVER's line-ending
form, which is LF.** This workspace's form is CRLF with no BOM. Place the LF
bytes directly and the file is byte-wrong everywhere: `p4 diff` reports the
**entire file as a single hunk** (`@@ -1,9848 +1,9997 @@`), which is the tell.
Convert at the byte level rather than through text APIs, so encoding is never in
question:

```powershell
$src = [System.IO.File]::ReadAllBytes("$scratch\recovered")
$o = New-Object System.Collections.Generic.List[byte]
for ($i = 0; $i -lt $src.Length; $i++) {
    $c = $src[$i]
    if ($c -eq 10 -and ($i -eq 0 -or $src[$i-1] -ne 13)) { $o.Add(13) }
    $o.Add($c)
}
p4 edit -c <CL> <path>
[System.IO.File]::WriteAllBytes('<workspace path>', $o.ToArray())
p4 diff -du <path>      # must show ONLY your hunks
```

**Then rebase.** The recovered file is at the revision it was shelved against,
not head. Diff it against depot head and re-apply whatever landed in between,
by hand, before you submit. If you skip this you silently revert another agent's
work inside your own CL. The final `p4 diff` showing only your own hunks is what
proves you did not.

**Check the blast radius before assuming your shelf is special:**

```powershell
p4 verify -S //...@=<CL>        # which files in THIS shelf are bad
p4 verify -q //Codex/...#head   # any live file in the depot failing (quiet: only problems)
```

Two distinct faults exist and they are not the same. **`BAD!`** with two digests
is a mismatch against intact content, recoverable as above. **`BAD! (open
failed)`** means the archive file itself is missing and there is nothing to
recover; the 2026-07-20 sweep found 17 of those under `//Codex/main/samples/`,
all historical revisions of files deleted at head when they moved to
`codex/test/`.

**And the reason this cost anything at all:** the shelf held a finished, measured
change bundled with an unfinished one, so it sat unsubmitted for days. A finished
change that stands on its own ships as its own CL the day it is measured. Nothing
in a shelf is safe, and a shelf is not a backup.

## CL Lifecycle

```
p4 change              # Create numbered CL
p4 edit -c <CL> file   # Open file in specific CL
  ... make changes ...
p4 shelve -c <CL>      # Save to Perforce (preserves work)
p4 revert -c <CL> ...  # Restore depot state on disk
  ... run gates ...
p4 unshelve -s <CL>    # Restore changes after gates
p4 sync                # schedules the resolve unshelve did NOT
p4 resolve -am         # merge shelf with any head that moved under you
build/p4-stale-check.ps1   # assert nothing is behind; resolve -n will not tell you
p4 shelve -d -c <CL>   # Delete shelf before submit
p4 submit -c <CL>      # Submit
```

## Splitting a Large CL

When you need to submit part of a CL (e.g., a critical fix) without the rest:

```powershell
# 1. Shelve the big CL
p4 shelve -c <big-CL>

# 2. Revert everything
p4 revert //Codex/main/...

# 3. Force-sync to clean state
p4 sync -f

# 4. Create the small CL
p4 change  # -> new CL number

# 5. Edit ONLY the file(s) for the small fix
p4 edit -c <small-CL> path/to/file.codex

# 6. Make ONLY the targeted change
# (the file is now at depot state -- edit from there)

# 7. Submit the small CL
p4 submit -c <small-CL>

# 8. Unshelve the big CL and resolve
p4 unshelve -s <big-CL> -c <big-CL>
p4 resolve -am  # auto-merge
```

### 4. Em-dash and non-ASCII in `p4 submit -d`
**Symptom:** `p4 submit -d "..."` fails with `No Translation for parameter`
and dumps a hex blob instead of your description.
**Cause:** The Perforce client's charset cannot encode characters outside
ASCII. Em-dashes (U+2014), curly quotes, and other Unicode punctuation
silently creep in from LLM output, copy-paste, and PowerShell here-strings
that interpolate smart punctuation.
**Fix:** Keep submit descriptions pure ASCII. Replace em-dashes with `--`,
curly quotes with straight quotes. When using PowerShell here-strings for
multi-line descriptions, use the single-quoted form `@'...'@` (literal,
no interpolation) and visually scan for non-ASCII before submitting. If
in doubt, pipe through `[System.Text.Encoding]::ASCII.GetString()` to
catch offenders.

### 5. Test sidecars with CRLF or trailing newline corruption
**Symptom:** A test that passes on first run fails after `p4 sync`. The output matches visually but differs by one byte.
**Cause:** Perforce `text` type files get CRLF translation on Windows and an appended trailing newline. `.expected` sidecars are compared byte-for-byte against serial output (which is LF-only). A `text`-typed `.expected` file gains `\r` bytes and/or an extra `\n` that the runtime never emits.
**Fix:** The test harness strips `\r` from expected files, but cannot detect a spurious trailing newline. If a test fails with an off-by-one length mismatch, hex-dump both files and check for a trailing `0x0A 0x0A` in expected vs `0x0A` in actual. Fix by removing the trailing blank line from the `.expected` file, or retype it as `binary` with `p4 retype -t binary <file>`.

### 6. Traps consolidated from agent memories (each rediscovered more than once)

- **`p4 change -i` with no `Files:` section EMPTIES the changelist.** Creating a
  numbered CL by piping a spec whose Files section you stripped (or string-joined
  wrong) moves your opened files out of the CL. Create the CL empty, then
  `p4 edit -c <cl> <files>` (or `p4 reopen -c <cl>`) to populate it. Reek
  rediscovered this at least four times.
- **`p4 edit <file>` with no `-c <cl>` goes to the DEFAULT changelist** and gets
  left out of a numbered submit. Always pass `-c` once the CL exists.
- **ONE target path per `p4 copy --from`.** A second path is read as the TARGET, not
  a second source: `copy --from fester //main/A //main/B` opens B for integrate FROM
  A. Copy each file in its own command and READ the `... - sync/integrate from ...`
  line it prints -- it names the real source, so a wrong one is visible before submit.
- **`p4 opened` LIES before a resolve.** After a merge-down it can show unresolved
  `branch` resolves as `delete`; submitting on that reading wipes other agents' new
  files. Run `p4 resolve` first, then trust `p4 opened`.
- **Use `p4 diff2 -q` for true content parity, not `p4 interchanges`** -- the latter
  is unreliable in streams and shows phantom entries.
- **A file opened by `Damian@BigWhite_Codex_main` is Damian READING it, not an edit
  in flight.** His editor checks a file out on open, so `p4 opened -a` shows it held
  at whatever revision he last looked at, often many revisions behind head. It is
  not a pending change, it will not clobber yours, and it needs no coordination.
  Do not report it as a hazard (Damian, 2026-07-21). Any OTHER client holding a file
  is a real agent and a real merge concern.
- **`Select-String` misreads p4 `unicode`-typed files** (most `.codex` are unicode by
  typemap). Use the Grep tool (ripgrep) for content searches over depot files.
- **A docs-only change still wants a NUMBERED CL** even though it needs no token:
  `p4 edit` with no `-c` has no CL number, and AgentGrid's build-request needs an
  integer if you later decide the change is not docs-only after all.
- **`p4 copy --from` takes the STREAM NAME, not a depot path.**
  `p4 -c BigWhite_Codex_<agent>_main copy --from //Codex/<agent>` is right; appending
  `/...` yields `Wildcards not allowed in '//Codex///Codex/<agent>/...'`.
- **Revert your open files BEFORE a merge-down, not after.** The shelf holds them, the
  merge then takes main's contended files (`BACKLOG.md` above all) wholesale with zero
  conflicts, and you reapply your delta on top. Doing it in the other order is what
  makes `BACKLOG.md` conflict every time. Delete the loose copies of any reverted `add`
  first -- revert-on-add leaves them on disk writable and the later unshelve silently
  drops them ("Can't clobber writable file").
- **Merge-down is not a one-time precondition; it is a precondition of the SUBMIT.**
  Another agent can land while your gate runs (the standing gate is ~160s and main
  moves nightly), and the copy-up is then refused with
  `Stream //Codex/<agent> cannot 'copy' over outstanding 'merge' changes`. Merge down
  again, submit the merge, then copy up. Budget for two merge-downs per token hold.
  Your token does not prevent this and is not meant to: what lands under you is
  non-seed traffic, which takes no token, and re-merging it costs a merge rather
  than a gate. A SEED-affecting CL is the one thing that cannot arrive while you
  hold the token, and that is the whole point of holding it.
- **`p4 submit` is refused while the CL still has a shelf** ("has shelved files").
  `p4 shelve -d -c <CL>` first. This bites on every CL where a gate follows a shelve,
  which is every CL that uses the build token.
- **A CL can be RENUMBERED on submit.** `p4 submit -c 9517` reported
  `Change 9517 renamed change 9520 and submitted`. Read the submit output for the final
  number rather than reusing the one you created; a workplan row citing the pre-submit
  number points at nothing.

## Key Principle

Perforce tracks file OPENS, not file CONTENT. When you `p4 edit` a file, Perforce marks it as open. Whatever bytes are on disk at submit time get submitted. There is no staging area like git. This means:

- Multiple CLs can have the same file open -- each sees the same on-disk bytes
- Shelving saves the current on-disk bytes, not a diff
- Reverting restores the depot version, discarding ALL on-disk changes
- The on-disk state is the source of truth for compilation

## Dev Streams

### Experimental Sub-Branches (a dev stream off a dev stream)

To save speculative work in the repo **without promoting it** -- a campaign
that must prove itself before it earns main -- branch a sub-stream off your
own dev stream and submit there. Routine, commands, and the live traps are in
`docs/Agents/RiskyBusiness.md`. First cut: the LIR selector (`//Codex/reek-lir`).

### Why We Use Them

Dev streams isolate risky work from mainline. If an agent breaks the seed, only
the dev stream is affected -- main stays at its last proven state. The stream
gives a clear, easy-to-remember baseline: "DEV_2GB_SYNTAX branched from main at
CL 1334, proven seed at CL 1380."

**When creating a dev stream branch, always include in the branch CL description:**
- The parent stream and the exact CL it branched from
- The last proven seed hash on the parent at that point
- A one-line purpose ("syntax conversion + 2GB seed + memory abstraction")

This gives anyone a clear path back to main without archaeology.

### Merge-Down (Parent → Child)

Bring parent changes into the child stream before copying up. This ensures
the child is a superset of the parent.

**You are responsible for semantic merge of every file.** Do not bulk-accept
with `-ay` or `-at`. For each file in the merge:

1. Check if YOU have pending edits or recently submitted changes to that
   file. Run `p4 opened` and review your shelved CLs.
2. If the file is untouched on your side, accept theirs (`-at`).
3. If BOTH sides changed the file, diff the incoming version against yours
   and merge manually. Your WIP changes will be silently overwritten by
   `-at` and silently kept (discarding theirs) by `-ay` -- both are wrong
   when the file has changes on both sides.
4. If in doubt, `p4 diff2` the two versions before resolving.

**The failure mode is silent.** A bulk `-at` overwrites your `-Trace` flag.
A bulk `-ay` drops main's bug fix. Neither produces an error. The only
signal is a test failure or a crash hours later.

```powershell
# 1. Merge down from parent (use -r for reverse = parent-to-child)
p4 merge -S //Codex/<CHILD_STREAM> -r

# 2. Review each file -- DO NOT BULK-RESOLVE
p4 resolve -n   # preview what needs resolving

# For files you haven't touched:
p4 resolve -at <file>    # accept theirs

# For files with changes on both sides:
p4 diff2 //Codex/main/<file> //Codex/<CHILD>/<file>   # inspect
p4 resolve -am <file>    # auto-merge, or manual if conflicts

# For files where you want to keep your version:
p4 resolve -ay <file>    # accept yours -- but ONLY if you've verified
                         # that main's changes are already incorporated
                         # or intentionally excluded

# 3. Submit the merge-down CL
p4 submit -d "merge down from main (CLs ...)"
```

### Copy-Up (Child → Parent)

After merge-down is complete, copy the child's state up to the parent.

**You must specify the parent client with `-c`.** If your `.p4config` points
at the child client, a bare `p4 copy` targets the wrong workspace. Always
use `-c <parent-client>` to ensure files open on the parent.

```powershell
# 1. Copy up -- specify the PARENT client, use --from with the child stream
#    Example: child is CodexMagic, parent client targets main
p4 -c BigWhite_Codex_gollum_main copy --from CodexMagic

# 2. Resolve if needed (accept theirs for clean copy-up)
p4 -c BigWhite_Codex_gollum_main resolve -at

# 3. Submit on the parent client
p4 -c BigWhite_Codex_gollum_main submit -d "Copy up from CodexMagic: <description>"
```

If you don't have a parent client, create one:
```powershell
p4 client -S //Codex/main BigWhite_Codex_<agent>_main
# Set Root to a separate directory (e.g. D:\Projects\NewRepository-<agent>-main)
```

Common agent client names:
- `BigWhite_Codex_<agent>_main` -- main stream (copy-up client)
- `BigWhite_Codex_<agent>` -- dev stream working client

### Checking Stream Sync Status

**Do not use `p4 interchanges` to check if a stream is ahead of another.**
In a multi-stream topology, content often reaches a target through indirect
paths (e.g. Mountain → RESTRUCTURE → main). `interchanges` only tracks
direct integration records and will permanently show CLs whose content
arrived via a sibling stream -- there is no supported way to clear these
entries without touching every file from the original CL.

Use `p4 diff2` instead -- it compares actual content:

```powershell
# Are there real content differences between two streams?
p4 diff2 //Codex/<STREAM_A>/... //Codex/<STREAM_B>/...

# Check a specific file
p4 diff2 //Codex/<STREAM_A>/path/to/file //Codex/<STREAM_B>/path/to/file
```

If `diff2` reports all files identical, the streams are in sync
regardless of what `interchanges` says.

### A green gate does NOT mean the seed matches the source

**Any change under `codex/compiler/` changes the compiled compiler, so the
seed is stale the moment you submit without rebuilding it.** Do not reason
from "behaviour is identical" or "the emitted output is unchanged": the seed
is the compilation OF THE SOURCE, so if the source moved, the seed no longer
corresponds to it whatever the binary does. A rename is the most convincing
possible case for "this cannot matter" and it is still a source change.
(Blu shipped exactly that in CL 8994 on 2026-07-18; val hit it. Repaired by
the seed in the CL that closed BACKLOG 2.18.)

**`build/build.ps1` cannot catch this, and it is worth knowing why before you
trust it for something it does not check.** Its fixed-point phase compares
**SUT** (source compiled by the seed) against **stage1** (source compiled by
SUT). That proves the compiler built from the current source is a fixed point
of itself. It never compares the seed against the SUT, and it cannot usefully:
a correct old seed compiling new source still yields a correct new compiler, so
`hard fixed point in one pass` is exactly what a stale seed looks like. The only
mention of the seed in that script is the constants hash.

The check that does catch it is the one below, and it runs on the **target**
workspace, not on your dev stream.

### A rename can change the seed, even when codegen does not

**"Not a codegen change" is not the same as "not a seed change", and the
difference has already shipped a seed that did not reproduce from its source.**
CL 8994 gave the text emitter a `codex-emit-*` prefix, removing 16 cross-chapter
name collisions. The emitted output was genuinely byte-identical -- that was
verified -- and the CL correctly said so. But the compiler mangles a colliding
name with its chapter (`emit--codex-emitter_foo`), so removing the collisions
**shortened the names baked into the compiler's own binary**: 672 bytes smaller,
and `seed/Codex.cdx` no longer reproduced from `codex/compiler/`. It sat that way
across several CLs until a routine `Sut === seed` check caught it (val 9003).

Ask not "does this change what the compiler emits" but **"does this change the
compiler binary"**. Renames, added or removed definitions, chapter moves and
anything touching a name the compiler bakes into itself all qualify.

### Verify `Sut === seed` against the DEPOT, every session

**`build/build.ps1` does not test this.** A green gate proves the SUT is a fixed
point of *itself* (`SUT === stage1`); it proves nothing about whether the binary
sitting in the depot is that same SUT. Only copy-up depends on it, so a seed can
lag its source indefinitely while every gate stays green.

```powershell
p4 print -q -o build-output/depot-seed.cdx //Codex/main/seed/Codex.cdx
(Get-FileHash -Algorithm SHA256 build-output/depot-seed.cdx).Hash
(Get-FileHash -Algorithm SHA256 build/output/Sut.cdx).Hash    # must match
```

Compare against the **depot** print, not the workspace file: a workspace seed can
be stale, locally overwritten, or mid-resolve, and all three read as agreement.
If they differ and your CL does not touch the compiler's dependency set, the lag
is someone else's -- confirm it (`p4 changes //Codex/main/codex/compiler/...`
against `p4 changes //Codex/main/seed/Codex.cdx`, looking for a compiler CL newer
than the newest seed CL), then **rebuild and submit the seed rather than reporting
it**. It is a mechanical fix and the source is the authority.

### Seed Verification During Copy-Up

**The seed in a copy-up CL must be a proven fixed point on the TARGET stream.**
The seed built on the child stream may not match what the parent produces,
because the source concat can differ between workspaces.

**The installed seed must be `build/output/Sut.cdx` -- the signed one.**
`build/output/NewSeed.cdx` is a copy of the unsigned `stage1.cdx`
(`build.ps1`:401). The sign phase patches the public key and signature
into `Sut.cdx` **in place** (`build.ps1`:272-274) and touches nothing
else, so a seed installed from `NewSeed.cdx` carries zeros where its
signature belongs and fails `build/test-self-verify.ps1` with
`SIGNATURE INVALID`. The content hash (bytes 8-39) deliberately excludes
the signature region so the fixed-point test works on signed and unsigned
alike -- which is exactly why a hash match will *not* catch this for you.
Run the self-verify.

```powershell
# On the PARENT workspace with the copy-up CL unshelved:

# 1. Run full build -- this rebuilds from the shelved seed
build/build.ps1

# 2. Check Sut content hash against seed content hash (bytes 8-39).
#    If Sut === seed, the seed is already the fixed point -- nothing to do.
#    If Sut !== seed but stage1 === stage2, the fixed-point content is
#    STAGE1's, not Sut's (Sut was built by the OLD seed). Install the
#    unsigned NewSeed.cdx as an intermediate bootstrap and converge:
Copy-Item -Force build/output/NewSeed.cdx seed/Codex.cdx
build/build.ps1            # now converges: SUT === stage1 in one pass

# 3. Install the SIGNED fixed point as the seed
Copy-Item -Force build/output/Sut.cdx seed/Codex.cdx

# 4. Prove it -- this is the step that catches an unsigned seed
build/test-self-verify.ps1   # must print THE SEED VERIFIES ITSELF

# 5. Revert the integrate on the seed, re-edit, and re-shelve
p4 revert //Codex/<PARENT>/seed/Codex.cdx
p4 edit -c <CL> //Codex/<PARENT>/seed/Codex.cdx
# (copy the proven seed)
p4 shelve -r -c <CL>

# 6. Only submit after the seed content hash matches Sut === stage1 === stage2
#    AND test-self-verify.ps1 is green
```

**Why this matters:** The compiler is a fixed point of itself. A seed from a
different compilation environment (different workspace, different source concat
order, different stream) may produce correct output but not be self-consistent
on the target. The byte-identity check catches everything -- including cosmic
rays, stale files, and source concat differences between workspaces.

### Stream Lifecycle Example (DEV_NEXT, 2026-05-14 → 2026-05-15)

```
1. main broken (suspect seed at CL 1348)
2. DEV_2GB_SYNTAX branched from main@1334, proven seed at CL 1380
3. DEV_NEXT branched from DEV_2GB_SYNTAX@1389 to isolate seed fix
4. Work done on DEV_NEXT: seed fix, exception handler, renames, tests
5. Merge-down DEV_2GB_SYNTAX → DEV_NEXT (resolve accept-yours)
6. Copy-up DEV_NEXT → DEV_2GB_SYNTAX (verify seed on target)
7. Next: merge-down main → DEV_2GB_SYNTAX, then copy-up to main
```
