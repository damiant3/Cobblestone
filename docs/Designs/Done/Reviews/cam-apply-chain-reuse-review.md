# Review: cam/floppy-disk-streaming (5b6e75c) — ApplyChain Reuse + Bootstrap Revert

**Reviewer**: Agent Linux  
**Date**: 2026-03-28  
**Commit**: 5b6e75c  
**Verdict**: ✅ Merge

## Summary

Two fixes:

1. **Removed `CodexApplyChain` duplicate record** — having two identical
   record types (`ApplyChain` in C# emitter, `CodexApplyChain` in Codex
   emitter) triggered a self-hosted type checker crash. Codex emitter now
   reuses `collect-apply-chain` and `ApplyChain` from CSharpEmitterExpressions.

2. **Bootstrap reverted to C# emitter** — `emit_full_module` (C#) for the
   fixed-point bootstrap chain. The streaming pipeline still uses Codex
   emitter for bare metal output. This separation is correct: the bootstrap
   host is .NET, so the fixed-point chain must produce C#.

Fixed point proven at 337,251 chars. 907/907 tests pass.

Note: `codex-filter-defs` / `codex-def-list` / scoring functions from the
previous dedup commit are now dead code in the non-streaming module path.
Not blocking — they may be useful when the streaming path needs full-module
Codex output.

*Reviewed from Linux sandbox. Build clean.*
