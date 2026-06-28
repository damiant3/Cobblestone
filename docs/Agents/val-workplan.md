# val workplan -- x86-64 + RISC-V codegen optimization

**Stream**: //Codex/CodexMagic
**Date**: 2026-06-28
**Last CL**: 6287 (CodexMagic)

## Current Status

### x86-64: 12 optimizations shipped (CLs 6205-6243)

| Bench   | Start | Final | vs C /O2 |
|---------|------:|------:|---------:|
| fib     |    21 |    21 |      +5% |
| fact    |    15 |    15 |     = O2 |
| gcd     |    17 |    17 |     +21% |
| sum     |    14 |    14 | beats O2 |
| ack     |    42 |    24 |     +14% |
| tak     |    77 |    44 |     +13% |
| collatz |    35 |    20 |     +11% |
| locals  |    59 |    54 | beats O2 |

### RISC-V: 25 optimizations shipped (CLs 6159-6172, 6261-6287)

135 instructions eliminated in phase 2 (CLs 6261-6287).
Aggregate: Codex 141 vs GCC -Os 134 (+5%).

| Bench   | Start | Final | GCC -Os | vs -Os |
|---------|------:|------:|--------:|-------:|
| fib     |    41 |    20 |      22 | beats  |
| fact    |    37 |    14 |       9 | +56%   |
| gcd     |    39 |     7 |       6 | +17%   |
| sum     |    39 |     7 |       9 | beats  |
| ack     |     - |    24 |      22 | +9%    |
| tak     |     - |    39 |      34 | +15%   |
| collatz |     - |    15 |      13 | +15%   |
| locals  |     - |    15 |      19 | beats  |

### Phase 2 optimizations (17 CLs)

| CL | Optimization | Impact |
|----|-------------|--------|
| 6261 | pow2 div/rem (srai/andi) + let dest-driven | collatz -4, locals -6 |
| 6271 | NOP compaction (global, B/J-type offset fixup) | fib -2, all others -1 to -4 |
| 6273 | Direct TCO for simple 2-arg tail calls | ack -8, collatz -16, locals -6 |
| 6274 | Dead-jump elimination after TCO branches | ack -1, collatz -1 |
| 6277 | Skip save-if-needed for simple right operand | collatz -3, locals -6 |
| 6280 | Mul-by-pow2 strength reduction (slli) | locals -1 |
| 6281 | Skip ra save/restore for pure-TCO | ack -2, collatz -2, locals -2 |
| 6282 | Direct arg-reg emission for 2-arg calls | ack -1 |
| 6283 | Expanded frameless TCO + temp-only locals | collatz -6, locals -18 |
| 6284 | Frameless binop pow2 + direct-param TCO | collatz -1, locals -2, sum -2 |
| 6285 | Reordered mixed TCO (call-first, simple-after) | ack -4 |
| 6286 | Direct arg-reg emission for N-arg calls | tak -25 |
| 6287 | Last-arg skip in TCO shuffle | tak -3 |

## Remaining gaps

- **fact** (+56%): GCC transforms recursion to iteration (accumulator
  introduction). Same gap on x86 (+36% vs GCC) and ARM64 (+44%).
  Compiler-level transform, not plug-level.
- **tak** (+15%): Frame overhead (5 callee-saved + ra). GCC uses
  C-extension compressed instructions (2-byte mv). ISA difference.
- **gcd** (+17%): 1 instruction from -Os. Structural minimum.
- **ack** (+9%): 2 instructions from -Os. Frame overhead for 1 non-tail call.
- **collatz** (+15%): Loop-invariant constant hoisting (li 1/3 outside loop).

## Test commands

```powershell
pwsh codex/plugs/riscv/build.ps1                          # rebuild plug
codex/plugs/riscv/compile-riscv.ps1 -Src X -Out Y         # single compile
bench/compare-iot.ps1                                      # cross-arch bench
build/test-cross-batch.ps1 -Arch riscv64 -UseQemu -Jobs 2 # full battery
```
