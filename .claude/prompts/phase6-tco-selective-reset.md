Start with tools/codex-agent/codex-agent.exe orient and clean stale intermediates per
CLAUDE.md and sync master origin.

Phase 6 TCO selective heap reset: the TCO heap reset in X86_64CodeGen.cs (EmitTailCall,
line ~233) reclaims per-iteration allocations at each tail-call loop boundary. A bandaid
at line ~291 (`hasListArg → goto skipReset`) disables reset for ALL functions with list
parameters, because in-place list-snoc can create above-mark pointers inside a below-mark
list. This bandaid blocks reclamation in emit-defs-streaming (200+ iterations of
parse/desugar/lower/emit, accumulating ~126 MB of garbage), even though emit-defs-streaming
only READS its list params and never snocs.

ROOT CAUSE (diagnosed 2026-04-01): `tokenize-loop` (Codex.Codex/Syntax/Lexer.codex) is
the function that makes the bandaid necessary. It's a TCO loop that snocs each token into
a List Token via in-place list-snoc. Removing the bandaid for ALL functions lets the reset
reclaim just-snoced tokens in tokenize-loop, corrupting the token list immediately.

THE FIX: Replace the blanket hasListArg bail-out with a selective per-param check.
For each list parameter at the TCO reset point:
  1. Check if the list pointer is below the mark (existing pointer check — keep this).
  2. If the list has heap-typed elements, also check the LAST ELEMENT value.
     If last element >= mark, snoc happened with above-mark data → skip reset (unsafe).
     If last element < mark (or list is empty, or elements are scalars), reset is safe.

This lets emit-defs-streaming reclaim (its lists are read-only, all elements below mark)
while protecting tokenize-loop (its list's last element is an above-mark Token after snoc).

PREVIOUS ATTEMPT THAT FAILED (2026-04-01): Removing hasListArg entirely and adding the
last-element check crashed the self-compile (zero QEMU output, 300s timeout). The crash
was diagnosed to tokenize-loop via @BANDAID instrumentation. The last-element check was
NOT tested in isolation on tokenize-loop — the implementation removed the bandaid for ALL
functions simultaneously. The check logic itself may be correct; the bug was applying it
to tokenize-loop which genuinely snocs.

WHAT TO DO:
  1. Read docs/Compiler/TCO-HEAP-RESET-DIAGNOSTIC.md for full analysis and test suite.
  2. Read docs/Compiler/ESCAPE-COPY-ATTEMPT2-POSTMORTEM.md for the earlier escape-copy
     attempt that also failed (different approach, same session).
  3. Keep the hasListArg bail-out for functions that snoc (tokenize-loop pattern).
  4. Add the last-element check for functions that DON'T snoc (emit-defs-streaming pattern).
  5. The distinction: if the list arg's NEW value (in tcoTempLocals) is the SAME pointer
     as the OLD value (in tcoParamLocals), the list was passed through unchanged → safe
     for last-element check. If the pointer CHANGED, snoc/concat happened → keep bail-out.
     This is a POINTER IDENTITY check, not a deep content check.

REFERENCE FILES:
  C# codegen:     src/Codex.Emit.X86_64/X86_64CodeGen.cs
    EmitTailCall:        ~line 199
    TCO heap reset:      ~line 233
    hasListArg bail-out: ~line 291
    m_tcoHeapMarkLocal:  ~line 195
    m_tcoParamLocals:    ~line 161
    m_tcoTempLocals:     ~line 192
  Lexer:          Codex.Codex/Syntax/Lexer.codex (tokenize-loop is here)
  Main pipeline:  Codex.Codex/main.codex (compile-streaming-v2, emit-defs-streaming)
  Diagnostic doc: docs/Compiler/TCO-HEAP-RESET-DIAGNOSTIC.md
  Post-mortem:    docs/Compiler/ESCAPE-COPY-ATTEMPT2-POSTMORTEM.md

CONSTRAINTS:
  - Baseline: 570KB ELF, ~60s Stage 1, pingpong green
  - Target: same ELF size (±1KB), same or better time, pingpong green, HEAP reduced
  - Build test: dotnet build + dotnet test + wsl bash tools/pingpong.sh
  - Push to a feature branch for review, not directly to master
