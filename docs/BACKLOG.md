# Backlog — Outstanding Work

## Compiler Correctness

| # | Item | Component | Notes | Effort |
|---|------|-----------|-------|--------|
| 1 | RISC-V self-compile (bare metal PASS, but no output in usermode| Codex.Emit.RiscV | **Partially fixed** Troubles remain with the test harness and wsl hosted QEMU.  Testing on linux | ~4 hrs |
| 2 | Result-space escape-copy disabled | Codex.Emit.RiscV, X86_64 | Crashes on cross-references. Disabled in `da67c9a`. Not blocking MM3 but leaves memory on the table. | ~4 hrs |
| 3 | Verify IL backend CCE assumptions | Codex.Emit.IL | Uses .NET `Char.IsLetter`/`Char.IsDigit`/`Char.IsWhiteSpace` (Unicode). Correct for .NET targets. If IL binaries ever process CCE-encoded data, these need CCE ranges. Document the decision. | ~15 min |
| 4 | ARM64 builtins parity | Codex.Emit.Arm64 | 8 compiler-critical builtins missing: TCO, is-digit, is-whitespace, negate, text-contains, text-starts-with, list-cons, list-append. Blocks self-hosting on ARM64. | ~1 day |
| 5 | NetworkSync test failures | Codex.Repository | 4 tests fail (need a listening peer). Either fix the tests to be self-contained or mark as integration-only. | ~1 hr |

## Features

These all have design docs in `docs/Designs/`.

| Feature | Design Doc | Depends On | Effort |
|---------|-----------|------------|--------|
| Capability refinement (Steps 2-8) | `Features/CAPABILITY-REFINEMENT.md` | Step 1 done (direction, scope, time-boxing, trust lattice) | Weeks |
| Repository federation | `Features/V3-REPOSITORY-FEDERATION.md` | V1 views done, V2 narration done. Missing: cross-repo refs, trust lattice, proposal workflow, sync protocol | Weeks |
| Structured concurrency runtime | `Features/CAMP-IIIC-STRUCTURED-CONCURRENCY.md` | `[Concurrent]` effect tracking done. Runtime (fork, await, par, race) not implemented | ~1 week |
| Standard library | `Features/STDLIB-AND-CONCURRENCY.md` | Design exists. Small core: Text, List, Map, Maybe, Either, Result, IO | ~2 weeks |
| Multi-language syntax | `CurrentPlan.md` | Parser per locale, shared AST. No design doc yet | Large |
| Codex.UI substrate | `CurrentPlan.md` | Semantic primitives, typed themes. No design doc yet | Large |
| Perf automation | `CurrentPlan.md` | Wire `--bench-check` into CI or pre-commit hook | ~2 hrs |

## Long-Term

These are summit goals, not tasks. They guide direction.

| Goal | Status |
|------|--------|
| Codex.OS on real hardware | Garage box provisioned, QEMU working. Next: WHPX, then real boot |
| Ring 5+: filesystem, networking | Content-addressed FactStore as filesystem — design exists |
| Floppy disk image (boot → compile → self-compile in 1.44MB) | 64MB achieved. Need streaming Path B optimizations to shrink further |
| Repository federation | See P3 above |

