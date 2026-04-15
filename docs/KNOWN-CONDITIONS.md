# Known Conditions

Persistent record of known build/test conditions that agents should NOT re-investigate.
Read by `codex-agent doctor`. Last updated: 2026-04-15.

Tool failures are tracked separately in `docs/Agents/TOOL-ERROR-REGISTRY.md`.

## Build

### CS2001 in Codex.Codex — PRE-EXISTING, IGNORE

`Codex.Codex.csproj` references `../build-output/bootstrap/Codex.Codex.cs`, which is generated
by the self-hosting pipeline. On a fresh clone (or any environment that hasn't run the
Codex compiler), this file does not exist and the build emits:

```
CSC : error CS2001: Source file '…/build-output/bootstrap/Codex.Codex.cs' could not be found.
```

This is **expected**. The project is a self-hosted Codex compiler compiled by the Codex
toolchain, not by C#.

**Action**: When verifying builds, exclude `Codex.Codex` or accept this single CS2001.
Build individual projects (`dotnet build tools/Codex.Cli/`) or the full solution and
confirm this is the ONLY error.

### Codex.Codex.csproj in `dotnet test`

When running `dotnet test Codex.sln`, the `Codex.Codex` project will fail to build
(CS2001, see above). Test results from all other projects are valid. The overall test
command may report "build failed" but individual test project results are trustworthy.

## Codegen

### Sum type `==` is tag-only — KNOWN LIMITATION

The x86-64 bare-metal backend compares sum type values by loading and
comparing their tags (offset 0). This is correct for nullary constructors
(`EndOfFile == EndOfFile`, `True == False`, etc.) but NOT for constructors
with fields — `Circle 5 == Circle 5` would compare tags (both 0) and
return true even if the field values differed.

Current code only uses `==` on nullary sum types (TokenKind comparisons,
Boolean comparisons). If sum type `==` is ever used on constructors with
fields, it needs recursive structural comparison.

**Action**: Do not use `==` on sum type values with fields. Use pattern
matching instead. When linear types land, revisit with proper structural
equality.

### `record-set` is in-place mutation — CONTROLLED CONCESSION

The `record-set` builtin mutates records in place on bare metal (`MovStore`
at field offset). Safe only because CodegenState is linearly threaded —
single owner, no aliasing. If this invariant breaks (concurrent compilation,
shared state), `record-set` becomes a bug factory.

**Action**: Only use `record-set` on linearly-owned state. When linear types
land, replace with type-system-enforced ownership.
