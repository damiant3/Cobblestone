# Risky Business: Experimental Sub-Branches

When work is worth keeping but might not pan out -- a speculative campaign,
a rewrite that may lose to the incumbent, a codegen experiment that has to
prove it beats the old numbers before it earns main -- do not leave it in
shelvesets forever and do not push it up the normal ladder. Branch a
**sub-stream off your own dev stream**, submit the work there, and let it
live in the repo, quarantined, until it either wins (promote) or is
abandoned (it stays, solidly saved, for whoever revisits it).

This is a child of a development stream -- a grandchild of `//Codex/main`.
Every existing dev stream (`reek`, `blu`, `val`, ...) branches directly off
`main`; a sub-branch of one of those is new ground, which is why this routine
exists. It was first cut on 2026-07-15 for the LIR selector experiment
(`//Codex/reek-lir`, change 8237).

**That first cut got it wrong, and the corrected routine is below.** Read the
next section before you copy anything from an older version of this document.

---

## Populate first. Everything else follows from that.

The 2026-07-15 routine created the sub-stream and then `p4 add`ed the changed
files straight into it. It looked like it worked: the files landed, the
submits were real, the work was durably saved. It was still wrong, and the
damage was invisible for a day.

`p4 add` creates a file with **no integration record**. The sub-stream's
copy of `X86_64State.codex` and the parent's copy are, as far as the server is
concerned, two unrelated files that happen to share a name. There is **no
common ancestor**, so every later integration between them is a two-way diff
with no base. Diagnose it with `p4 filelog`:

```
... #1 change 8237 add on 2026/07/15      <- broken: no lineage
... #1 change 8410 branch from //Codex/reek/...   <- correct
```

What that costs, measured on `//Codex/reek-lir` 2026-07-16:

- **Merge-down is unusable.** `p4 resolve -am` reported
  `0 yours + 0 theirs + N both + N conflicting` on every file --
  `IR/Lir.codex` 77 chunks, 76 conflicting; `X86_64Chapter.codex` 26 of 26.
  Those are not real conflicts. With no base, the merge cannot tell which
  side changed a line, so it calls everything a conflict. The true divergence
  on eight of those files was **one line**.
- **Copy-up would be just as bad**, in the direction that matters most.
- **`p4 sync` serves only the added files**, never the parent's tree, so the
  sub-workspace cannot build -- which is what the old "untracked overlay"
  workaround existed to paper over. It is not needed. It was treating a
  symptom.

The fix is one command, in the right order: **`p4 populate` the sub-stream
from its parent before you put any work in it.** Populate branches the
parent's files into the child with real lineage. It creates **lazy copies** --
the depot does not duplicate content, so branching the parent's whole tree
costs metadata, not storage. The old fear of "copying 11k files into the
sub-stream" was a fear of a number in a status message.

The old trap 1 claimed `p4 populate` refuses a sub-stream outright. It does
not. It refuses a target that **already has files** -- which is exactly what
step 3 had just done by adding them. Add-then-populate fails; populate-then-
edit works. Verified 2026-07-16 on a throwaway task stream off `//Codex/reek`:
`5946 files branched`, and `p4 sync` then served the full tree.

---

## The routine

You are agent `<a>`, on dev stream `//Codex/<a>`, with the experimental work
sitting in a pending CL (open + shelved) in your workspace. Pick a
descriptive sub-branch name, e.g. `//Codex/<a>-<topic>`.

### 1. Create the sub-stream as a task stream

```powershell
p4 stream -o -t task -P //Codex/<a> //Codex/<a>-<topic> > spec.txt
# edit Description in spec.txt (ASCII only), then:
Get-Content spec.txt | p4 stream -i
p4 streams //Codex/<a>-<topic>        # verify: '... task //Codex/<a> ...'
```

A **task stream** is the right shape for this and is what the type exists for:
lightweight, branches lazily from its parent, stores only the files you
actually modify, and is disposable -- deleting the stream removes its
lightweight files with it (`p4 obliterate` on a task-stream path needs `-T`).
You get the "only my diffs are in the depot" property that the old routine was
reaching for, **and** you keep the lineage it threw away.

Use `-t development` instead only if the sub-branch must be long-lived or
shared, or needs children of its own -- task streams are meant to be transient
and can be unloaded when idle. **Either way, populate it.** The lineage rule
below is not about the stream type.

The Description is load-bearing (same rule as any dev branch): name the
parent, the CL/seed it branched from, the one-line purpose, and -- for an
experiment -- the bar it has to clear to be promoted. Anyone who finds this
stream later should learn from the description alone why it exists and
whether it's alive.

### 2. Create a client for it (its own root)

A client tracks one stream and one root. Your `.p4config` points at your dev
client, so **every** command against the sub-stream needs `-c <subclient>`.

```powershell
New-Item -ItemType Directory -Force D:\Projects\NewRepository-<a>-<topic> | Out-Null
$c = p4 client -S //Codex/<a>-<topic> -o BigWhite_Codex_<a>_<topic>
$c = $c -replace '(?m)^Root:.*$', "Root:`tD:\Projects\NewRepository-<a>-<topic>"
$c | p4 client -i
"P4PORT=localhost:1666`nP4USER=<user>`nP4CLIENT=BigWhite_Codex_<a>_<topic>" |
  Set-Content D:\Projects\NewRepository-<a>-<topic>\.p4config -NoNewline
```

### 3. Populate it from the parent -- BEFORE any work goes in

This is the step the first cut skipped, and the only one that matters for
everything downstream.

```powershell
p4 populate -n -S //Codex/<a>-<topic> -r -d "populate from <a>"   # preview
p4 populate    -S //Codex/<a>-<topic> -r -d "populate from <a>"   # '<N> files branched'
p4 -c BigWhite_Codex_<a>_<topic> sync                             # the full tree lands
```

The sub-workspace is now a complete, buildable tree, and every file in it has
a branch record pointing at the parent. No overlay, no copying, no `/XF`
exclusion lists.

### 4. Put the work in -- as edits, not adds

```powershell
$rl = 'BigWhite_Codex_<a>_<topic>'
p4 -c $rl edit -c <CL> <existing files you changed...>
p4 -c $rl add  -c <CL> <genuinely new files only>
# apply your changes on disk, then:
p4 -c $rl opened -c <CL>                 # verify every file is listed
p4 -c $rl submit -c <CL>
```

`p4 edit` on a populated file keeps the lineage; `p4 add` on a file the parent
already has destroys it. Only a file that genuinely does not exist in the
parent (a new chapter) should ever be `add`ed. The depot stores real revisions
of exactly the files you touched -- the rest stay lazy.

Build and measure **with the current directory set to the sub-workspace**:
`compile.ps1` resolves cited forewords with `-Repo '.'`, so a wrong CWD looks
for `.\codex\foreword\...` in the wrong tree and dies `CDX3010 ... not found`.

### 5. Clean your dev stream back to baseline

The work is safe in the sub-stream now; your dev workspace should return to
clean so it does not contaminate a gate.

```powershell
p4 revert -c <devCL> //Codex/<a>/...
# TRAP 2: reverting an 'add' leaves the file ON DISK as an untracked stray.
# concat-codex-self globs **/*.codex and bakes strays into the seed. DELETE it:
Remove-Item <the-added-file> -Force
p4 shelve -d -c <devCL>                # drop the now-redundant shelf
p4 change  -d <devCL>                  # delete the empty CL
p4 opened ; p4 changes -s shelved -c BigWhite_Codex_<a>   # both empty == clean
```

### 6. Merge down from the parent every session

Standing direction (Damian, 2026-07-16): **build against the latest main you
can, so the promotion merge never becomes a campaign of its own.** With the
stream populated this is the ordinary command and it actually works:

```powershell
p4 -c BigWhite_Codex_<a>_<topic> merge -S //Codex/<a>-<topic> -r
p4 -c BigWhite_Codex_<a>_<topic> resolve      # semantically, per file
p4 -c BigWhite_Codex_<a>_<topic> submit -d "merge down from <a>"
```

A healthy merge here is mostly `theirs` chunks. If you see
`0 yours + 0 theirs + N both + N conflicting`, stop: the lineage is broken and
you are merging without a base. That is a stream defect, not a code conflict,
and no amount of resolving will fix it.

### 7. Promote later -- only if it wins

```powershell
p4 -c BigWhite_Codex_<a> copy --from //Codex/<a>-<topic>   # child -> parent is COPY
p4 -c BigWhite_Codex_<a> resolve -am
p4 -c BigWhite_Codex_<a> submit -d "promote <a>-<topic>: <why it won>"
```

If it does not win, do nothing. It stays in the repo, attributed and
recoverable, for whoever picks the thread back up.

---

## Repairing a sub-stream whose files were added, not branched

`//Codex/reek-lir` is in this state as of 2026-07-16. There is no way to
retrofit lineage onto an added file; the stream has to be rebuilt. The work
itself is never at risk -- it is immutable in history.

1. Snapshot the tracked files: `p4 print -o <dst> //Codex/<a>-<topic>/<path>@<CL>`
   for each. (History alone is sufficient, but snapshot anyway.)
2. **Delete the untracked overlay from the sub-workspace first** (`codex/`,
   `build/`, `bench/`, `tools/`, `seed/`; keep `.p4config`), or the sync in
   step 5 dies on "can't clobber writable file".
3. `p4 -c <subclient> delete` the tracked files and submit -- the stream must
   be empty for populate to accept it, and for the spec to be deletable.
4. `p4 stream -d //Codex/<a>-<topic>`, then recreate it per step 1 above
   (`-t task`).
5. `p4 populate -S ... -r`, then `p4 -c <subclient> sync`.
6. Re-apply the snapshot as **edits** (step 4 of the routine). Re-apply, do not
   blind-copy: the parent has moved, and overwriting its files with your older
   snapshot silently drops everything it gained in the meantime.
7. Re-validate from scratch. The base moved, so every measured number moved
   with it -- re-baseline, do not compare against figures taken on the old base.

---

## Traps (hit live, 2026-07-15 and 2026-07-16)

1. **`p4 populate` refuses a target that already has files, not the stream
   itself.** The 2026-07-15 note recorded this as "populate refuses inherit
   streams" and moved on; that conclusion was wrong and cost the lineage. If
   populate says "Can't populate target path when files already exist", the
   answer is that something was added too early -- not that populate is
   unavailable. Verified working 2026-07-16: `5946 files branched`.

2. **Reverting an `add` leaves a stray on disk.** `p4 revert` on an
   opened-for-add file removes it from the CL but does not delete the file --
   it was never in the depot to restore. Our build globs every `*.codex` under
   `codex/`, so a leftover experimental file gets silently compiled into the
   next seed. Always delete the on-disk file after reverting its add.

3. **Parent to child is `merge`, not `copy`.** `p4 copy --from //Codex/<a>`
   into the child fails: "Stream needs 'merge' not 'copy' in this direction."
   `copy` is the child->parent (promotion) direction; `merge` is parent->child
   (branch/merge-down). The server's own hint points the wrong way -- trust
   the direction, not the hint.

4. **`-c <subclient>` on every sub-stream command.** Your `.p4config` binds
   the shell to your dev client. A bare `p4 add`/`submit`/`sync` targets the
   wrong workspace. Pass `-c BigWhite_Codex_<a>_<topic>` explicitly, every
   time, exactly as copy-up already requires `-c ..._main`.

5. **ASCII-only in stream and CL descriptions.** Same rule as everywhere:
   no em dashes, no curly quotes, no ellipsis. The server rejects non-ASCII
   outright.

6. **`p4 change -i` with a spec that has no `Files:` section empties the
   changelist.** It reports "Change N updated, removing 2 file(s)", the files
   fall back to the default changelist, and the submit then says "No files to
   submit". The description survives; recover with
   `p4 -c <client> reopen -c <CL> <files>`. Set the description when you create
   the CL, or use `p4 submit -d`.

---

## When to reach for this

- A speculative campaign whose payoff is unproven and might lose to the
  incumbent (the LIR selector: correct, but must beat the tree emitter's
  instruction counts before it earns main).
- A rewrite big enough that you want it in the repo -- reviewable, diffable,
  attributed -- long before it is ready to land.
- Anything you would otherwise leave in a shelf for weeks. A shelf is not a
  home; a sub-branch is.

The point is that experimental work should be **saved without being
promoted**. The repo remembers everything; that is the whole doctrine. A
sub-branch is how a risky idea gets remembered without being trusted yet --
and a sub-branch without lineage remembers the bytes while forgetting where
they came from, which is half a memory and the expensive half to rebuild.
