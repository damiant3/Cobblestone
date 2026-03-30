# Current Plan

**Date**: 2026-03-29

---
## MM3 IS PROVEN

The self-hosted Codex compiler compiled **itself** on bare metal x86-64 under
QEMU. 180 KB source (493 definitions, ~5,000 lines) in over serial, 261,654
bytes of valid C# out over serial — byte-for-byte match with the usermode
reference. The fixed point holds on hardware.

## ⛔ ARM64 & RISC-V — ABANDONED (2026-03-29)

**All work on ARM64 and RISC-V backends is forbidden.**

No new features, bug fixes, builtin ports, or bare-metal work on these
targets. Existing ARM64/RISC-V code remains in the tree for reference
but must not receive active development. Agent time is x86-64 only.

Previously open ARM64/RISC-V issues (missing builtins, self-compile segfault,
bare-metal UART, RISC-V stack pressure) are all **closed — will not fix**.

The `origin/linux/fix-riscv-bare-uart-full-init` branch has been deleted.

---

### Medium-term

| Item | Notes |
|------|-------|
| Codex.UI substrate | Semantic primitives, typed themes |
| Capability refinement Steps 2-8 | Scope, time-boxing, unified trust lattice |
| Perf automation | Wire `--bench-check` into CI or pre-commit hook |

### Long-term

| Item | Notes |
|------|-------|
| Codex.OS on real hardware | WHPX (Nut's box), then actual boot device |
| Ring 5+: filesystem, networking | Content-addressed FactStore as filesystem |
| Repository federation | Trust lattice, cross-repo sync, capability-gated imports |
