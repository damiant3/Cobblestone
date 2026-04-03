Start with tools/codex-agent/codex-agent.exe orient and clean stale intermediates per
CLAUDE.md and sync master origin.

We are working on the semantic equivalence checker: 98% body match, 18 mismatches
remain. The feature branch `feat/sem-equiv-checker` is under review (or merged).
Check your memories and handoff docs for full context.

The tool: `dotnet run --project tools/Codex.Cli -- sem-equiv docs/TestResults/stage0.codex docs/TestResults/stage1.codex`
Per-def drill: add `--show <name>` to see normalized S0/S1 bodies side by side.

The remaining 18 body mismatches are dominated by CCE string content bugs (11 defs).
The bare-metal text emitter (`escape-one-char` in CodexEmitter.codex) produces wrong
characters inside multi-character string literals:

1. `\n` (CCE 1) sometimes emits as the wrong character (e.g. `s`, `\C`). See
   emit-tco-def, emit-cce-runtime. Use `--show emit-tco-def` to see the exact
   divergence point.

2. `\"` (CCE 73, the double-quote character) emits as `\I\n)` instead of `\"\"`.
   See emit-builtin, emit-expr. The escape check `c == char-code-at "\"" 0` may
   be returning the wrong CCE code on bare metal.

3. Multi-line string constants split at `\n` boundaries differently between
   stage0 and stage1. See emit-full-module, emit-module.

Start by verifying the CCE codes: `char-code-at "\n" 0` should be 1,
`char-code-at "\"" 0` should be 73, `char-code-at "\\" 0` should be 85.
If these are wrong on bare metal, the escape comparisons in `escape-one-char`
(CodexEmitter.codex line 505-509) will fail silently — the character passes
through unescaped, producing garbled output.

After CCE fixes, 5 remaining diffs are redundant-paren normalizer issues
(application parens before binary ops, e.g. `(f x) * y` vs `f x * y`)
plus 2 cosmetic diffs (list spacing, main token count). These can be
handled in the sem-equiv normalizer.

Key files:
- Codex.Codex/Emit/CodexEmitter.codex (escape-one-char ~line 504, escape-text-loop ~498)
- Codex.Codex/Syntax/Lexer.codex (process-escapes ~line 300, CCE constants ~105)
- tools/Codex.Cli/Program.SemEquiv.cs (the checker)
- docs/TestResults/stage0.codex, stage1.codex (test data)
