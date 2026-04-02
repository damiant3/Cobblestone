Start with tools/codex-agent/codex-agent.exe orient and clean stale intermediates per
CLAUDE.md and sync master origin.

Phase 6 Hat Tip: linear record mutation optimization. The Codex self-compiler's
Stage 1 Heap HWM is 126 MB. The dominant cause is copy-on-update: every state
transition allocates a NEW record, copies unchanged fields, and the old record
is dead garbage on the bump-allocated heap (never reclaimed on bare metal).

READ FIRST: docs/Compiler/HatTip.md — full analysis, inventory of all 23
instances, waste estimates, and the chosen approach (Option A).

THE FIX: Add a linearity-based optimization to the C# x86-64 codegen that
detects when a record construction reuses all fields from a single-use binding
of the same type, and emits in-place field writes instead of a full allocation.

HOW IT WORKS:
  1. For each IRLet where the value is a record construction of type T:
  2. Find any referenced binding B of the same type T in the construction.
  3. Count all references to B in the entire let body.
  4. Count all references to B inside the record construction.
  5. If counts match AND every reference is a field access → B is linear.
  6. Emit MOV [B + field_offset], new_value for changed fields only.
     Return B's pointer. Skip heap allocation entirely.

WHAT CHANGES:
  - X86_64CodeGen.cs only. No .codex changes. No new syntax or builtins.
  - New method: CountBindingRefs(IRExpr, string) → int (recursive walk)
  - New path in record emission: detect linear source, emit field writes
  - All 23 copy-on-update instances optimize automatically

WHAT TO VERIFY:
  - The IR representation of let bindings and record constructions. Read
    src/Codex.IR/IRModule.cs to understand IRLet, IRRecord, IRFieldAccess
    (or equivalent) structure before implementing.
  - How record construction is currently emitted in X86_64CodeGen.cs. Search
    for EmitRecord or RecordType emission to find the allocation + field
    write pattern that needs the in-place alternative.
  - The ref counting must handle: field accesses (st.field), nested
    expressions (st.field + 1), and must NOT count references inside
    lambda bodies (closures capture, breaking linearity).

EXPECTED RESULT:
  - 18-25 MB HWM reduction (from ~126 MB toward ~100-108 MB)
  - ELF size decrease (fewer alloc + copy instructions)
  - Pingpong green, fixed point holds
  - Zero .codex changes — pure codegen optimization

CONSTRAINTS:
  - Baseline: 680KB ELF, ~60s Stage 1, pingpong green
  - Build test: dotnet build + dotnet test + wsl bash tools/pingpong.sh
  - Push to a feature branch for review, not directly to master

CONTEXT FROM PREVIOUS SESSION (2026-04-01):
  - TCO selective reset (feature/phase6-tco-selective-reset) was implemented
    and works correctly but had ZERO effect on HWM — the peak is from
    copy-on-update dead records, not per-iteration TCO garbage.
  - Escape copy bail-out removal still crashes the self-compile.
  - The 126 MB and 62 MB (Stage 1 / Stage 2) HWMs are identical with and
    without TCO selective reset — confirmed via baseline comparison on master.
