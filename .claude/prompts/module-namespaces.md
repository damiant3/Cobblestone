Start with tools/codex-agent/codex-agent.exe orient and clean stale intermediates per
CLAUDE.md and sync master origin.

The self-hosted Codex compiler has a flat namespace across all .codex files. This forces
ugly prefixes (cg-emit-expr, cg-emit-let, cg-emit-binary, etc.) in X86_64.codex to
avoid collisions with identically-named functions in CSharpEmitterExpressions.codex.
The status entry is module-namespaces (codex-status list).

Design and implement module-scoped namespaces for .codex files. Each Chapter should
define a namespace scope. Functions within a Chapter are local by default. Cross-module
references use qualified names or explicit imports.

Key files to understand:
- src/Codex.Syntax/Parser.cs and ProseParser.cs — how Chapter/Section headers are parsed
- src/Codex.Semantics/NameResolver.cs — how names are resolved across files
- Codex.Codex/Semantics/NameResolver.codex — self-hosted name resolver
- docs/Designs/Language/ProseGrammarProposal.md — document grammar (Chapter/Section scope rules)

The scope rules in ProseGrammarProposal.md Part V already describe chapter-level scoping:
"A name is in scope from its introduction point to the end of its enclosing block"
and "Chapter-level types are public by default." This needs to be implemented.

After implementing namespaces, rename these functions in X86_64.codex back to their
natural names: cg-emit-expr -> emit-expr, cg-emit-name -> emit-name,
cg-emit-let -> emit-let, cg-emit-binary -> emit-binary,
cg-emit-binary-op -> emit-binary-op, cg-emit-comparison -> emit-comparison,
cg-emit-if -> emit-if, cg-emit-sub-rsp-imm32 -> emit-sub-rsp-imm32.

Both the C# reference compiler and the self-hosted compiler need the change.
Run full test suite and pingpong after. Use /plan to design the approach first.
