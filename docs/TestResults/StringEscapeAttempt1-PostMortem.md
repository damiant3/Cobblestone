# String Escape Bug — Investigation Post-Mortem

**Date**: 2026-04-03
**Agent**: Cam
**Status**: Root cause identified (16-byte alignment boundary in `__str_concat`
fast path). Fix not yet landed — two attempts failed.

## Summary

The bare-metal CodexEmitter corrupts the last bytes of escape sequences (`\n`,
`\"`) when the escape-text accumulator is exactly 7 or 8 output bytes long at
the moment a 2-byte escape is appended via the `__str_concat` fast path. This
is the 16-byte allocation boundary: `align8(7+8) = align8(8+8) = 16`, and the
new bytes land at positions 15-16 or 16-17, straddling or exceeding the
allocation.

## Smoke Test Results

Created `Codex.Codex/Emit/_test.codex` with 89 systematic test definitions.
Run through bare-metal pingpong, then compared stage0 (source) vs stage1
(bare-metal output) via `codex sem-equiv`.

**58 passed, 31 failed.** Key findings:

### What passes
- ALL single-`\n` tests (any position, any preceding character)
- ALL `\"` tests in isolation (1, 2, 3 quotes, embedded quotes)
- ALL `\\` tests
- ALL mixed escape tests (`\\\"\n`, `\n\"`, etc.)
- Two-`\n` with short accumulators (gap-0 through gap-4, pre-0 through pre-3)
- Two-`\n` with acc=9 (gap-7, gap-8, pre-6)

### What fails
- Two-`\n` with acc=7 before second escape: gap-5, pre-4, nl-two-end
- Two-`\n` with acc=8 before second escape: gap-6, pre-5, nl-five
- Single-`\n` with long strings: len15+ (acc=7 or 8 at the `\n` position)
- Multi-`\n` strings: corruption at FIRST `\n` where acc hits 7 or 8

### Corruption pattern

The wrong byte is deterministic per accumulator length:

| acc_len | align8(acc+8) | New bytes at | Expected | Got | CCE diff |
|---------|---------------|--------------|----------|-----|----------|
| 7 | 16 | pos 15,16 | CCE 86,18 | CCE 86,10 | -8 on byte 2 |
| 8 | 16 | pos 16,17 | CCE 86,18 | CCE 11 (1 byte) | merged/shifted |

For acc=7: the backslash (byte 1) is correct, but 'n' (byte 2) at position 16
gets value CCE 10 instead of 18. Position 16 is the first byte past the
16-byte allocation.

For acc=8: the 2-byte escape collapses to 1 wrong byte. Both bytes land past
position 16 (at 16 and 17).

## What We Tried

### Attempt 1: `char-code` instead of `char-code-at` in escape-one-char

Changed escape-one-char comparisons from `char-code-at "\n" 0` (runtime string
access) to `char-code '\n'` (compile-time constant). **No effect** — same
failures. Proves the bug is not in the comparison logic.

### Attempt 2: Bump HeapReg before copy in fast path

Moved the `HeapReg = ptr + align8(new_len+8)` update to BEFORE the byte copy
loop (instead of after). Theory: bytes at position 16 are in unclaimed heap
space. **No effect** — single-core x86 doesn't care about HeapReg ordering.

### Attempt 3: Disable fast path entirely

Changed `JNE slowPath` to `JMP slowPath` (unconditional). **Timed out** — the
O(n^2) slow-path-only compilation exceeds the 180s bare-metal timeout.

### Attempt 4: Bounded fast path (only extend when data fits in padding)

Added a second check: `len1 + len2 + 8 <= align8(len1 + 8)`. Only uses fast
path when new data fits within the existing allocation's alignment padding.
**Broke compilation** — only 317/1106 defs emitted. The additional JCC
instruction may have introduced a subtle code generation issue (wrong jump
target or register clobber). Needs investigation.

## What We Know

1. The bug is **100% correlated with the 16-byte allocation boundary** in
   `__str_concat`'s fast path. Accumulator lengths 7 and 8 trigger it;
   lengths 0-6 and 9+ do not.

2. The fast path code in `X86_64CodeGen.cs` lines 2668-2697 **looks correct
   on paper** — the byte copy loop addresses are right, the HeapReg bump
   covers the written bytes, and nothing else touches those addresses.

3. The wrong byte values are **deterministic** (same wrong CCE value every
   run for the same test), ruling out uninitialized memory or race conditions.

4. The bounded fast path (Attempt 4) proves that **changing the fast path
   behavior changes the output**, but the specific change introduced a new
   bug. This confirms the fast path is involved.

## Recommended Next Steps

1. **Debug Attempt 4**: The bounded fast path is the right fix conceptually
   but broke compilation. The issue is likely in the JCC patching or register
   state after the additional check. A careful review of the generated
   machine code (disassemble the ELF at the `__str_concat` offset) would
   find the bug quickly.

2. **Alternative fix: grow-and-copy in fast path**: Instead of checking if
   data fits in padding, always reallocate to the new aligned size when
   extending. This avoids writing past the boundary entirely while keeping
   O(n) performance.

3. **GDB on QEMU**: Attach GDB to the QEMU instance and set a hardware
   watchpoint on the corrupted byte address. This would catch the exact
   instruction that writes the wrong value.

4. **Disassemble `__str_concat`**: Extract the function from the ELF and
   verify the actual machine code matches what the C# codegen intends.
   An instruction encoding bug (wrong offset, wrong register) would show
   up immediately.

## Files

- `Codex.Codex/Emit/_test.codex` — 89 smoke test definitions
- `src/Codex.Emit.X86_64/X86_64CodeGen.cs` — `__str_concat` at line ~2642
- `tools/Codex.Cli/Program.SemEquiv.cs` — `\{`→`{` normalizer (kept)
