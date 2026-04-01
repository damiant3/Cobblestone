Start with tools/codex-agent/codex-agent.exe orient and clean stale intermediates per
CLAUDE.md and sync master origin. Merge feature/phase3-m32-m33 if not already merged.

We are working on docs/CurrentPlan.md Phase 3: Core Codegen — the heart of the Second
Bootstrap. Phases 1 (encoder) and 2 (ELF/CDX writers) are complete. Phase 3 milestones
M3.1 (main=42), M3.2 (let+arithmetic), and M3.3 (if/else+comparisons) are proven and
on master. The codegen is in Codex.Codex/Emit/X86_64.codex (~700 lines).

Read docs/Compiler/PHASE3-CORE-CODEGEN.md for the milestone progression. Read the
.handoff file for key learnings from the previous session (CCE encoding gotcha, no
comments, emitter parens pitfall, ++ chain / nested let performance).

The C# reference is src/Codex.Emit.X86_64/X86_64CodeGen.cs (6,075 lines). The IR types
are in Codex.Codex/IR/IRModule.codex. Tests are in tests/Codex.Bootstrap.Tests/.

Continue with milestone M3.4: function calls + recursion. This requires:
- Parameter binding in emit-function (args in RDI, RSI, RDX, RCX, R8, R9 per SysV ABI)
- cg-emit-expr case for IrApply (flatten curried calls, load args into registers, call)
- Multiple function emission (emit-all-defs already loops, call patching works)
- Test: factorial 5 prints "120"

Reference: X86_64CodeGen.cs EmitApply (line 851), EmitFunction parameter binding
(lines 428-445). Note: for M3.4 skip TCO and closures — those are M3.8 and M3.9.

After M3.4, continue through M3.5 (records), M3.6 (match), M3.7 (lists) if context
allows. Push to a feature branch for review when done. Run pingpong before pushing.

Note: function names in X86_64.codex use cg- prefix (cg-emit-expr, cg-emit-let, etc.)
to avoid collisions with CSharpEmitterExpressions.codex in the flat namespace. A
separate workstream (module-namespaces status entry) will add module scoping and
remove these prefixes. Do not rename them in this session.
