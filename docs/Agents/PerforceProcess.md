# Perforce Process for Agents

## The Golden Rule

**Your workspace files must match depot state before running gates (build, test, BS3).** The compiler reads source from disk. If you have shelved-but-not-reverted edits, the on-disk files contaminate the build. The seed doesn't know about your changes — it compiles what it reads.

## Before Running Gates

```powershell
# 1. Shelve your work (without -k: reverts on-disk files back to depot state)
p4 shelve -c <CL>

# 2. Force-sync to guarantee clean (handles stale/missing files)
p4 sync -f

# 3. Unshelve your changes back to the workspace
p4 unshelve -s <CL> -c <CL>

# 4. NOW run gates
codex.build/build.ps1
codex.build/test.ps1 -Jobs 4
```

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

```powershell
# 1. Merge down from parent (use -r for reverse = parent-to-child)
#    -Af forces the merge even if Perforce thinks it's not needed
p4 merge -c <CL> -Af -S //Codex/<CHILD_STREAM> -r

# 2. Resolve — accept ours if child has superseded parent's work
p4 resolve -ay    # accept yours (child wins)
# Or for selective merging:
p4 resolve -am    # auto-merge where possible, manual for conflicts

# 3. Submit the merge-down CL
p4 shelve -d -c <CL>   # delete shelf if shelved
p4 submit -c <CL>
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
