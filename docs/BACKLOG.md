# Backlog — Outstanding Work

**Method**: Cross-referenced all planning docs, commit history, and codebase state.
Items marked ~~strikethrough~~ in older docs have been verified as complete and are
excluded. Everything here is confirmed still open.

## P1 — Compiler Correctness

| # | Item | Component | Notes | Effort |
|---|------|-----------|-------|--------|
| 1 | RISC-V self-compile crash | Codex.Emit.RiscV | **Partially fixed** (cam/fix-riscv-s10-alloc): S10 allocator overwrite + LoadLocal toggle clobber in print loop both fixed. Third bug remains: heap corruption overwrites Token.kind with raw tag (0x10=InKeyword). Crashes in parser's `is-paren-type-param`. Only triggers on full 211KB source. Leading theory: premature heap reclamation via unresolved TypeVariable in IR region. Next: GDB watchpoint to catch the corruptor. Tools: WSL + QEMU + GDB available on Cam worktree. | ~4 hrs |
| 2 | Result-space escape-copy disabled | Codex.Emit.RiscV, X86_64 | Crashes on cross-references. Disabled in `da67c9a`. Not blocking MM3 but leaves memory on the table. | ~4 hrs |
| 3 | ~~Bootstrap Stage 1 crash~~ | Codex.Bootstrap | **Verified resolved** (2026-03-29). Full self-compile + mini-compile both succeed. Likely fixed by parser/scan_token improvements. | Done |
| 4 | Regenerate `_all-source.codex` | Codex.Codex | Concatenated compiler source predates CCE fixes, CRLF fix, AEffectType handling. Should be regenerated from current 26 source files. Consider a script. | ~15 min |
| 5 | Verify IL backend CCE assumptions | Codex.Emit.IL | Uses .NET `Char.IsLetter`/`Char.IsDigit`/`Char.IsWhiteSpace` (Unicode). Correct for .NET targets. If IL binaries ever process CCE-encoded data, these need CCE ranges. Document the decision. | ~15 min |
| 6 | ARM64 builtins parity | Codex.Emit.Arm64 | 8 compiler-critical builtins missing: TCO, is-digit, is-whitespace, negate, text-contains, text-starts-with, list-cons, list-append. Blocks self-hosting on ARM64. | ~1 day |
| 7 | NetworkSync test failures | Codex.Repository | 4 tests fail (need a listening peer). Either fix the tests to be self-contained or mark as integration-only. | ~1 hr |


## P3 — Features Designed But Not Built

These all have design docs in `docs/Designs/`. No code exists yet.

| Feature | Design Doc | Depends On | Effort |
|---------|-----------|------------|--------|
| Capability refinement (Steps 2-8) | `Features/CAPABILITY-REFINEMENT.md` | Step 1 done (direction, scope, time-boxing, trust lattice) | Weeks |
| Repository federation | `Features/V3-REPOSITORY-FEDERATION.md` | V1 views done, V2 narration done. Missing: cross-repo refs, trust lattice, proposal workflow, sync protocol | Weeks |
| Structured concurrency runtime | `Features/CAMP-IIIC-STRUCTURED-CONCURRENCY.md` | `[Concurrent]` effect tracking done. Runtime (fork, await, par, race) not implemented | ~1 week |
| Standard library | `Features/STDLIB-AND-CONCURRENCY.md` | Design exists. Small core: Text, List, Map, Maybe, Either, Result, IO | ~2 weeks |
| Multi-language syntax | `CurrentPlan.md` | Parser per locale, shared AST. No design doc yet | Large |
| Codex.UI substrate | `CurrentPlan.md` | Semantic primitives, typed themes. No design doc yet | Large |
| Perf automation | `CurrentPlan.md` | Wire `--bench-check` into CI or pre-commit hook | ~2 hrs |

## Long-Term Vision

These are summit goals, not tasks. They guide direction.

| Goal | Status |
|------|--------|
| MM3: Self-hosted compiler compiling itself on bare metal | **PROVEN** (x86-64, 64MB, ping-pong fixed point) |
| Codex.OS on real hardware | Garage box provisioned, QEMU working. Next: WHPX, then real boot |
| Ring 5+: filesystem, networking | Content-addressed FactStore as filesystem — design exists |
| Floppy disk image (boot → compile → self-compile in 1.44MB) | 64MB achieved. Need streaming Path B optimizations to shrink further |
| Repository federation | See P3 above |

## Stale Docs To Update

These docs list items as open that are actually complete. They create confusion
and should be updated to reflect reality:
