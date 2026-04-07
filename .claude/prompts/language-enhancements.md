Start with tools/codex-agent/codex-agent.exe orient and clean stale intermediates per
CLAUDE.md. Sync with origin master.

## Context

Both bootstraps are green (C# fixed point + bare-metal pingpong). All 1151
tests pass. See docs/Designs/Language/DESIGN-PROPOSALS.md for the full design doc.

## P10 — Self-Hosted TCO: DONE

TCO is fully implemented in all three backends:
- Reference C# emitter: CSharpEmitter.TailCall.cs (while loops)
- Self-hosted C# emitter: CSharpEmitter.codex (while loops, 182 functions)
- X86-64 bare-metal: X86_64.codex (backward jmp, added in commit e627912)

All three stages produce identical TCO output (182 while(true) loops in C#).
The bare-metal stack HWM is ~2MB steady state. Remaining stack pressure is
from deep non-tail call chains and [x] ++ recursive(...) patterns in
ChapterScoper — those need accumulator rewrites, not TCO.

## Priority: P1 — Multi-Pattern Matching

Parser change: recognize `|` between patterns in `when` branches.
Desugarer: expand `P1 | P2 -> body` into separate AMatchArms.
No emitter changes needed. See DESIGN-PROPOSALS.md for details.

## After P1: P8 — Exhaustiveness Checking (no stack impact — compile-time only)

Type checker verifies that `when` covers all constructors. Missing constructors
produce compile-time diagnostics. This is the correctness guarantee — "no patch
is possible at that distance."

## Quick Win: Sorted Keywords

Sort the cs-keywords list in CSharpEmitterExpressions.codex and use binary
search (bsearch-text-pos) instead of the 50-element if-else chain. No language
change needed. Also applies to is-cs-member-name and similar functions.

## How to Test

After any change:
1. dotnet build Codex.sln && dotnet test Codex.sln (1151 tests, all must pass)
2. dotnet run --project tools/Codex.Cli -c Release -- bootstrap Codex.Codex
3. wsl bash tools/pingpong.sh (bare-metal fixed point)
