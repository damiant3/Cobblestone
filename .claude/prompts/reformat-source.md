You are Agent Linux. Pull the repo at github.com/damiant3/NewRepository, install .NET 8,
run `codex-agent orient` then `codex-agent orient vision`.

## Task: Run codex format on all self-hosted source files

The `codex format` command was just merged to master. It normalizes indentation in
.codex prose-format files. Your job: run it on every file, verify bootstrap still
passes, and push.

See docs/Compiler/FORMAT-WORKFLOW.md for the full process.

### Steps

1. Build the compiler:
   ```
   dotnet build tools/Codex.Cli/Codex.Cli.csproj -c Release
   ```

2. Create branch:
   ```
   git checkout -b linux/reformat-source
   ```

3. Format all source files:
   ```
   for f in $(find Codex.Codex -name '*.codex' | sort); do
       dotnet run --no-build --project tools/Codex.Cli -- format "$f" --write
   done
   ```

4. Review the diffs — formatter only changes whitespace. If you see content
   changes, stop and report:
   ```
   git diff --stat Codex.Codex/
   ```

5. Build and test (6 pre-existing failures expected, 0 new):
   ```
   dotnet build Codex.sln
   dotnet test Codex.sln
   ```

6. Commit:
   ```
   git add Codex.Codex/
   git commit -m "style: normalize source formatting across all .codex files"
   ```

7. Push branch for review. Upload PAT from _claude.json, push, scrub PAT from
   remote URL immediately after.

### Key context

- 33 .codex source files in Codex.Codex/
- Fixed point was PROVEN today: Stage 1 = Stage 3, 673,061 chars identical
- Bare metal pingpong is byte-identical
- The formatter is line-level only — no parsing, no semantic changes
- If bootstrap breaks after formatting, the formatter misclassified a line
- Test baseline: 6 known failures (pre-existing), 3 skipped

### Branch cleanup note

Do NOT delete remote feature branches after merging. Cam reuses branch names
for incremental pushes.

### Cam may push other branches during your session

If Damian relays a branch from Cam for review: fetch, review (build + test),
merge to master if approved, push. Do not delete the branch. Resolve conflicts
by taking Cam's version for compiler code, yours for docs/tooling.

### PAT workflow

Upload _claude.json only when pushing. Set remote URL with PAT, push, immediately
reset remote URL to clean HTTPS to scrub the PAT.
