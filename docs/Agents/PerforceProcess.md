# Perforce Process for Agents

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
client — a bare `p4 copy` targets the wrong workspace.

## The Golden Rule

**Your workspace files must match depot state before running gates (build, test, BS3).** The compiler reads source from disk. If you have shelved-but-not-reverted edits, the on-disk files contaminate the build. The seed doesn't know about your changes — it compiles what it reads.

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

# 4. NOW run gates
codex.build/build.ps1
codex.build/test.ps1 -Jobs 4
```

`p4 clean` is also the fix when a build mysteriously bakes in a name or
file that "isn't there" — a stray .codex from a reverted/abandoned branch
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

### 2. Submitting a file with unrelated changes
**Symptom:** CL description says "fix X" but the diff also includes Y and Z.
**Cause:** The file was open for edit in your CL AND modified by other work (rename, idiom replacement). Perforce submits whatever is on disk.
**Fix:** Before submitting a small CL, `p4 diff` the file and verify the diff matches your intent. If it has extra changes, revert and re-edit just the lines you need.

### 3. Moving files between CLs without checking content
**Symptom:** Files from CL A end up in CL B with A's modifications baked in.
**Cause:** `p4 reopen -c <new-CL>` moves the file reference but the on-disk content stays as-is — including all edits from the original CL.
**Fix:** If splitting a CL, revert the file first, then `p4 edit` it fresh in the target CL and make only the intended changes.

## CL Lifecycle

```
p4 change              # Create numbered CL
p4 edit -c <CL> file   # Open file in specific CL
  ... make changes ...
p4 shelve -c <CL>      # Save to Perforce (preserves work)
p4 revert -c <CL> ...  # Restore depot state on disk
  ... run gates ...
p4 unshelve -s <CL>    # Restore changes after gates
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
# (the file is now at depot state — edit from there)

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

## Key Principle

Perforce tracks file OPENS, not file CONTENT. When you `p4 edit` a file, Perforce marks it as open. Whatever bytes are on disk at submit time get submitted. There is no staging area like git. This means:

- Multiple CLs can have the same file open — each sees the same on-disk bytes
- Shelving saves the current on-disk bytes, not a diff
- Reverting restores the depot version, discarding ALL on-disk changes
- The on-disk state is the source of truth for compilation

## Dev Streams

### Why We Use Them

Dev streams isolate risky work from mainline. If an agent breaks the seed, only
the dev stream is affected — main stays at its last proven state. The stream
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
   `-at` and silently kept (discarding theirs) by `-ay` — both are wrong
   when the file has changes on both sides.
4. If in doubt, `p4 diff2` the two versions before resolving.

**The failure mode is silent.** A bulk `-at` overwrites your `-Trace` flag.
A bulk `-ay` drops main's bug fix. Neither produces an error. The only
signal is a test failure or a crash hours later.

```powershell
# 1. Merge down from parent (use -r for reverse = parent-to-child)
p4 merge -S //Codex/<CHILD_STREAM> -r

# 2. Review each file — DO NOT BULK-RESOLVE
p4 resolve -n   # preview what needs resolving

# For files you haven't touched:
p4 resolve -at <file>    # accept theirs

# For files with changes on both sides:
p4 diff2 //Codex/main/<file> //Codex/<CHILD>/<file>   # inspect
p4 resolve -am <file>    # auto-merge, or manual if conflicts

# For files where you want to keep your version:
p4 resolve -ay <file>    # accept yours — but ONLY if you've verified
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
# 1. Copy up — specify the PARENT client, use --from with the child stream
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
- `BigWhite_Codex_<agent>_main` — main stream (copy-up client)
- `BigWhite_Codex_<agent>` — dev stream working client

### Checking Stream Sync Status

**Do not use `p4 interchanges` to check if a stream is ahead of another.**
In a multi-stream topology, content often reaches a target through indirect
paths (e.g. Mountain → RESTRUCTURE → main). `interchanges` only tracks
direct integration records and will permanently show CLs whose content
arrived via a sibling stream — there is no supported way to clear these
entries without touching every file from the original CL.

Use `p4 diff2` instead — it compares actual content:

```powershell
# Are there real content differences between two streams?
p4 diff2 //Codex/<STREAM_A>/... //Codex/<STREAM_B>/...

# Check a specific file
p4 diff2 //Codex/<STREAM_A>/path/to/file //Codex/<STREAM_B>/path/to/file
```

If `diff2` reports all files identical, the streams are in sync
regardless of what `interchanges` says.

### Seed Verification During Copy-Up

**The seed in a copy-up CL must be a proven fixed point on the TARGET stream.**
The seed built on the child stream may not match what the parent produces,
because the source concat can differ between workspaces.

```powershell
# On the PARENT workspace with the copy-up CL unshelved:

# 1. Run full build — this rebuilds from the shelved seed
codex.build/build.ps1

# 2. Check Sut content hash against seed content hash (bytes 8-39)
#    If Sut === seed, the seed is already the fixed point.
#    If Sut !== seed but stage1 === stage2, replace the seed:
Copy-Item -Force codex.build/output/NewSeed.cdx seed/Codex.cdx

# 3. Revert the integrate on the seed, re-edit, and re-shelve
p4 revert //Codex/<PARENT>/seed/Codex.cdx
p4 edit -c <CL> //Codex/<PARENT>/seed/Codex.cdx
# (copy the proven seed)
p4 shelve -r -c <CL>

# 4. Only submit after the seed content hash matches Sut === stage1 === stage2
```

**Why this matters:** The compiler is a fixed point of itself. A seed from a
different compilation environment (different workspace, different source concat
order, different stream) may produce correct output but not be self-consistent
on the target. The byte-identity check catches everything — including cosmic
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
