# Current Plan

**Date**: 2026-03-29

---
## MM3 IS PROVEN

The self-hosted Codex compiler compiled **itself** on bare metal x86-64 under
QEMU. 180 KB source (493 definitions, ~5,000 lines) in over serial, 261,654
bytes of valid C# out over serial — byte-for-byte match with the usermode
reference. The fixed point holds on hardware.

## Open Issues (2026-03-28 Agent Linux)

### ARM64 self-compile segfault — missing builtins

**Status**: Code gap, not environmental.

The ARM64 backend is missing `list-insert-at` and `list-snoc` builtins.
The compiler warns during compilation (`ARM64 WARNING: unresolved call`).
These are used heavily by the self-hosted compiler — every sorted insertion
in the type checker (`env-bind`, `add-subst`, `scope-add`) and every
accumulator append in lowering/emission hits `list-snoc`.

**Fix**: Port `list-insert-at` and `list-snoc` from x86-64's `TryEmitBuiltin`
to `Arm64CodeGen.cs`. Pattern: allocate new list, copy elements, insert/append
at position, return pointer. The x86-64 implementation is at lines ~1725-1750
in `X86_64CodeGen.cs`.

**Impact**: ARM64 native self-compile is blocked until this is resolved.
Small programs that don't use sorted insertion work fine.


### Medium-term

| Item | Notes |
|------|-------|
| ARM64 builtins parity | 8 compiler-critical builtins missing for self-hosting |
| Codex.UI substrate | Semantic primitives, typed themes |
| Capability refinement Steps 2-8 | Scope, time-boxing, unified trust lattice |
| Perf automation | Wire `--bench-check` into CI or pre-commit hook |

### Long-term

| Item | Notes |
|------|-------|
| Codex.OS on real hardware | WHPX (Nut's box), then actual boot device |
| Ring 5+: filesystem, networking | Content-addressed FactStore as filesystem |
| Repository federation | Trust lattice, cross-repo sync, capability-gated imports |
