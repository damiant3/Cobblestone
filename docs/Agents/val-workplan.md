<<<<<<< Updated upstream
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
=======
# val workplan -- RISC-V codegen optimization

**Stream**: //Codex/CodexMagic
**Date**: 2026-06-26
**Last CL**: 6142

## Status

134/134 cross-tests pass (100%). All pushed to main (CL 6140).
The RISC-V plug is functionally complete. Now optimize codegen
quality toward GCC -O2 parity.

## Current: instruction counts vs GCC (RISC-V RV64)

| Bench | GCC -O0 | GCC -O2 | GCC -Os | Codex RV64 | x86 Codex |
|-------|--------:|--------:|--------:|-----------:|----------:|
| fib   |      34 |   241*  |      22 |         41 |        21 |
| fact  |      27 |      14 |       9 |         37 |        15 |
| gcd   |      26 |       8 |       6 |         39 |        17 |
| sum   |      27 |      11 |       9 |         39 |        14 |

*GCC -O2 fib unrolls recursion into a 241-instruction iterative loop.

Codex RV64 is ~1.2-1.9x GCC -O0. The x86 Codex backend achieves
GCC -O2 parity on fib/fact -- the gap shows the optimization
headroom in the RISC-V plug.

## Optimization plan

### Phase 1: Remove frame overhead (target: -10 insns avg)

1. **Eliminate 2-NOP prologue reservation for small frames.**
   Currently all functions reserve 3 slots (ADDI + 2 NOPs) for the
   large-frame LUI+ADDI+SUB sequence. Functions with frame <= 2040
   waste 2 NOPs. Fix: only reserve 3 slots when the frame MIGHT be
   large (body-locals > 240), otherwise use 1 slot.

2. **Minimal frame elision for pure leaves.** Functions that make
   no calls (no JAL/JALR) don't need to save ra. Functions with
   no callee-saved register usage don't need the full 12-register
   save/restore. The x86 backend has `rv-is-pure-leaf` -- port the
   logic to skip prologue/epilogue for call-free functions.

3. **Near-leaf frame: skip callee-saved saves.** For functions that
   call other functions but use < 2 callee-saved locals, save only
   ra + the locals actually used instead of all 10 s-registers.

### Phase 2: Destination-driven emission (target: -5 insns avg)

4. **Result register targeting.** The x86 backend's `result-dest`
   mechanism tells expression emission where the result should land
   (e.g., a0 for return values). This avoids `mv a0, sN` after
   every expression. Port `rv-set-result-dest` to actually influence
   register allocation.

5. **Immediate operands in binary expressions.** `x + 1` should
   emit `addi rd, rs, 1` instead of `li t, 1; add rd, rs, t`.
   The x86 backend folds small constants into the instruction.

### Phase 3: Tail call optimization (target: -5 insns avg)

6. **Self-recursive TCO.** The mechanism exists (`rv-should-tco`,
   `rv-emit-tail-call-general`) but is conservative. Enable for
   simple self-recursive patterns (fib, fact, gcd, sum-to).

7. **Parallel-move arg shuffle.** For tail calls with reordered
   arguments, use the x86 backend's parallel-move algorithm
   instead of saving all args to temporaries.

### Phase 4: Advanced (target: GCC -O2 parity)

8. **R8/R9-staged binary operands.** The x86 backend uses extra
   staging registers so binary expressions consume zero locals.

9. **Commutative both-complex shortcut.** For tree-recursive
   patterns like `fib(n-1) + fib(n-2)`, the x86 backend uses
   `pop+op` instead of `mov+pop+mov+op`.

10. **IrRemInt with leaf inliner.** Inline `math-mod` as `div+mul+sub`
    at call sites to avoid function call overhead.

## Non-goals (for now)

- Linear-scan register allocation (the x86 backend doesn't have
  this either -- it's the next frontier for both)
- Compressed instructions (RVC) -- saves code size but doesn't
  reduce instruction count
- Self-hosting on RISC-V -- requires full compiler port, not
  just plug optimization

## Test commands

```powershell
build/test-cross-fast.ps1 -Arch riscv64 -ChunkSize 10   # full battery
bench/compare-iot.ps1                                     # codegen comparison
bench/build-codex-cross.ps1                               # build benchmarks only
```
>>>>>>> Stashed changes
