# val workplan

**Stream**: //Codex/CodexMagic
**Date**: 2026-06-24

## Completed (2026-06-24)

RISC-V cross-compilation backend: 0.8% -> ~95% pass rate.

12 CLs submitted (5997, 6012, 6013, 6029, 6034, 6039, 6041, 6042,
6046, 6047, 6050, 6054, 6056):

- Stack address, FPU enable, beq echo, comparison register reuse
- __start echo beq-pos off-by-one
- Memoization disabled (stack corruption)
- list-push grow condition (exponential reallocation)
- Real negation (fneg.d vs integer sub)
- Plug rewrite: TCP -> serial I/O, O(n^2) -> O(1) code emission
- f32 register moves (fmv.w.x/fmv.x.w), vec4-extract dispatch

Also: parameterized test-cross-batch.ps1 for arm64/riscv64, installed
Renode v1.16.1.

## Open

### Priority 1: Vector IR gap (shared ARM64/RISC-V)

vector-f32 test fails on both backends. Vector +/* on heap-allocated
arrays compile as scalar ops on pointers. Needs IR-level fix: the
compiler's LOWER phase should expand vector arithmetic into
element-wise loops before the plug sees them. One fix for all plugs.

### Priority 2: Full battery run

Run test-cross-batch.ps1 -Arch riscv64 with all fixes to get final
pass count. Estimated 130+/137 pass. Submit updated
docs/Test/riscv64_cross_results.md.

### Priority 3: Copy-up to main

Merge down from main, resolve, run gates, copy up. 12 CLs of RISC-V
codegen improvements.
