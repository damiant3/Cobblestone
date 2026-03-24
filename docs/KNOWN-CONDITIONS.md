# Known Conditions

Persistent record of known build/test conditions that agents should NOT re-investigate.
Read by `codex-agent doctor`. Last updated: 2026-03-21.

## Build

### CS5001 in Codex.Codex — PRE-EXISTING, IGNORE

`Codex.Codex.csproj` emits `CS5001: Program does not contain a static 'Main' method
suitable for an entry point`. This is **expected** — the project is a self-hosted Codex
compiler compiled by the Codex toolchain, not by C#. The csproj has `<NoWarn>CS5001</NoWarn>`
but CS5001 is an error, not a warning, so it isn't suppressed.

**Action**: When verifying builds, exclude `Codex.Codex` or accept 1 CS5001 error from it.
Use `dotnet build Codex.sln` and check that the ONLY error is this one.

### Codex.Bootstrap sed failure on fresh clone — PRE-EXISTING, WORKAROUND

On a fresh clone, `dotnet build Codex.sln` fails with `MSB3073` from
`Codex.Bootstrap.csproj` because the sed/PowerShell target tries to read
`Codex.Codex/out/Codex.Codex.cs`, which does not exist until the self-hosted
compiler has been run at least once.

```
error MSB3073: The command "sed 's/^Codex_Codex_Codex\.main();$//' '…/out/Codex.Codex.cs' > '…/CodexLib.g.cs'" exited with code 2.
```

**Action**: On a fresh clone, create the missing directory and placeholder:

```bash
mkdir -p Codex.Codex/out
echo "// placeholder for bootstrap" > Codex.Codex/out/Codex.Codex.cs
```

Then build individual projects (`dotnet build tools/Codex.Cli/`) or the full
solution (Bootstrap will still error on missing symbols but all other projects
compile). This is expected on any environment that hasn't run the self-hosting
pipeline.

## Tests

### Peek_non_numeric_start_does_not_crash — FIXED (2026-03-24)

`codex-agent peek` previously crashed with `ArgumentOutOfRangeException` when given
non-numeric, zero, or negative start arguments. Fixed by clamping `start` to >= 1
in both `codex-agent.codex` and `peek.codex`. `text-to-integer` returns 0 for
non-numeric input; the clamp catches that and all other sub-1 values.

**Action**: No special handling needed. All edge cases now return exit code 0.

## Agent Toolkit

### IL emitter — user-defined effectful helper functions

Calling user-defined functions with effectful return types (e.g., `[FileSystem] Nothing`)
from other effectful functions via `let _ = myHelper args` can produce
`InvalidProgramException: Common Language Runtime detected an invalid program`.

**Action**: Inline effectful logic (use `write-file` / `read-file` directly) instead of
extracting effectful helper functions. This is an IL emitter limitation, not a Codex
language issue.

### Codex.Codex.csproj in `dotnet test`

When running `dotnet test Codex.sln`, the `Codex.Codex` project will fail to build
(CS5001, see above). Test results from all other projects are valid. The overall test
command may report "build failed" but individual test project results are trustworthy.

## Editing Hazards

### edit_file `else`→`then` corruption — RECURRING, UNDER INVESTIGATION

When using `edit_file` or `create_file` on `.codex` source files, the keyword `else`
is sometimes silently replaced with `then`, producing invalid Codex syntax such as:

```
if text-contains line ": error " then line
 then first-error-line lines (idx + 1)   ← should be `else`
```

This has been observed multiple times across different sessions and different files.
The corruption appears to happen during tool-mediated writes, not in manual edits.
Root cause is unknown — may be a tokenization issue in the edit tool's diff engine
treating `else` and `then` as interchangeable Codex keywords.

Also observed: `else 0` → `then 0` on if/then/else ternary expressions (line 53 of
`codex-agent.codex` in the original doctor/session-memory session).

**Action**: After ANY edit to a `.codex` file, always verify with `snap diff` and
visually inspect for `then`→`else` swaps. Use `peek` to check if/then/else chains.
When using the `.new` file swap approach for large files, grep the output for
suspicious `then` patterns before swapping:

```powershell
Select-String -Path "file.codex.new" -Pattern "^\s+then\s" | Select-Object LineNumber, Line
```

**Status**: Under investigation as of 2026-03-21. Documenting all occurrences.
