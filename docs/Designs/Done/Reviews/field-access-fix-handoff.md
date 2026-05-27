# Field Access Fix — Review Handoff

**Date**: 2026-03-23
**From**: Cam (Claude Code CLI)
**For**: Agent Linux (reviewer)
**Branch**: `cam/fix-riscv-null-deref`
**Commit**: `fe17dc3` — `fix: resolve ConstructedType to RecordType in LowerFieldAccess`

---

## What Changed

One file: `src/Codex.IR/Lowering.cs`, method `LowerFieldAccess` (+14 lines).

When a record expression's type was `ConstructedType` (a named type reference
like `Token` or `Name`) instead of `RecordType`, the lowering pass left it
unresolved. Downstream, the RISC-V and WASM emitters defaulted every such
field access to **index 0** — silently returning the wrong field.

The fix:
1. If `record.Type` is `ConstructedType`, look it up in `m_typeDefMap`
2. If it resolves to `RecordType`, use that for field index computation
3. Rewrite `record.Type` on the IR node so emitters see `RecordType`

Same pattern already used for `SumType` resolution in `LowerMatch` (line 390).

---

## Impact

- **41 field accesses** in the self-hosted compiler were hitting the fallback
- All were `.text` or `.value` fields on `ConstructedType` records
- `token.text` (field 1) was returning `token.kind` (field 0) in the RISC-V binary
- This is the root cause of the corrupted RISC-V stage3 output

---

## Verification Done (Windows)

- `dotnet build Codex.sln` — 0 warnings, 0 errors
- `dotnet test Codex.sln` — 390 pass, 0 fail (2 skipped HAMT, 1 pre-existing AgentToolkit failure)
- RISC-V build: 0 field-access warnings (was 41 before fix), 227,600 byte ELF
- C# stage1 == stage3 identity preserved (258,472 bytes, same MD5)

---

## What to Test (Linux)

This is the **Camp II-C summit push**. Please:

1. Pull `cam/fix-riscv-null-deref`
2. Build the RISC-V binary: `dotnet run --project tools/Codex.Cli -- build Codex.Codex --target riscv`
3. Run under QEMU with a test `.codex` file piped to stdin:
   ```
   qemu-riscv64 ./Codex.Codex/out/Codex.Codex < samples/hello.codex
   ```
4. Compare output with bootstrap C# compiler output
5. If they match → **Camp II-C summited**

The binary should now produce correct field accesses for all record types.
If it still crashes or produces wrong output, the issue is elsewhere — but the
41 wrong-index field accesses are definitively fixed.

---

## Files NOT Committed

The working tree has unstaged changes to stage1/stage3 output files,
generated-output/, and CodexLib.g.cs — these are from prior work and
not part of this fix. Only `Lowering.cs` was committed.
