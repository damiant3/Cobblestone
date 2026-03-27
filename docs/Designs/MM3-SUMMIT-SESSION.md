# MM3 Summit Session — Findings

**Date**: 2026-03-26 evening
**Agent**: Cam
**Branch**: `cam/mm3-easy-builtins`

---

## What We Proved

1. **The compiler works on bare metal.** 30 of 32 feature tests pass —
   every builtin the self-hosted compiler uses (text ops, list ops, char
   ops, closures, records, higher-order functions) produces valid C# output.

2. **Compilation is fast.** Scaling test with 2, 4, 8, 16, 32 functions
   in a single module: all complete in 0.11-0.17 seconds. Not N². Not
   even measurably N. Compilation time is not the bottleneck.

3. **Serial receive works.** Interrupt-driven serial (IRQ4 handler + ring
   buffer) and polling both successfully receive data. The paced 32KB test
   delivered all 64 chunks (512B each, 500ms apart) without data loss.

4. **The gap analysis was wrong.** Higher-order functions already work
   (closure/trampoline system). Multi-file protocol isn't needed (bootstrap
   concatenates). Arena memory is sufficient (254MB mapped). The "hard gaps"
   were already solved.

## What Blocks MM3

**Stack overflow during compilation of large modules.**

- 489 functions (22KB source) compiles successfully.
- 712 functions (32KB source) crashes (QEMU exits — triple fault).
- The crash occurs AFTER receiving all data (verified with paced sending).
- The crash occurs during compilation, not during serial I/O.
- The kernel stack is 448KB (0x10000-0x7FFFF, growing down from 0x80000).

The self-hosted compiler is deeply recursive: the type checker, unifier,
and emitter all recurse through AST nodes. Each stack frame saves 7
callee-saved registers (56 bytes) plus local variables. With 700+
definitions, the recursion depth during type checking or emitting
exhausts the stack.

## The Fix (Not Yet Implemented)

Two options:

**A: Increase the stack.** Move the stack base higher — e.g., from 0x80000
to 0x180000 (1.5MB of stack). This requires updating the stack pointer
initialization in `EmitStart` and ensuring the memory layout doesn't
conflict with the ring buffer or other structures.

**B: Reduce stack usage.** The self-hosted compiler's recursive functions
could be converted to iterative loops with explicit stacks (on the heap).
This is the TCO approach — but many of the recursive calls aren't in tail
position (the type checker pattern-matches and then recurses on children).

Option A is the pragmatic fix. Option B is the correct long-term fix.

## Changes on This Branch

| Commit | What |
|--------|------|
| `25dd27b` | Gap analysis revision — hard gap eliminated |
| `9fe1aa8` | Gap analysis collapses — may already work |
| `19b0b43` | UART init + busy-wait poll (pause instead of hlt) |
| `7ccae0f` | MM3 test suite (32 tests) + --dump-source flag |
| `3fdda43` | Sum type REPL print diagnosis |

### Uncommitted Changes

- **IRQ4 interrupt-driven serial receive**: Ring buffer at 0x180000 (256KB),
  interrupt handler drains COM1 FIFO into buffer, `__bare_metal_read_serial`
  reads from buffer instead of polling. PIC mask changed from 0xFC to 0xEC
  to unmask IRQ4.

- **UART initialization**: 115200 baud, 8N1, FIFO enabled with 14-byte
  trigger, receive interrupt enabled (IER bit 0).

- **Test scripts**: `mm3-scaling.py` (proves compilation is not N²),
  `mm3-slow32.py` (proves data arrives intact but compilation crashes),
  `mm3-scaling-systematic.py` (4 series × 5 sizes, identifies 22-32KB wall).

## Test Results Summary

| Test | Result |
|------|--------|
| main = 42 (24B) | PASS — compiles on bare metal |
| 153B (3 functions) | PASS |
| 525B (10 functions) | PASS |
| 966B (20 functions) | PASS |
| 2KB (44 functions) | PASS |
| 11KB (250 functions) | PASS |
| 22KB (489 functions) | PASS |
| 32KB (712 functions) | FAIL — stack overflow |
| 180KB (full compiler) | FAIL — stack overflow |

## Next Steps

1. Increase kernel stack to 1.5MB (change 0x80000 to 0x180000)
2. Verify 32KB compiles with larger stack
3. Attempt full 180KB self-compilation
4. Compare output with Stage 1 — if it matches, MM3 is proven
