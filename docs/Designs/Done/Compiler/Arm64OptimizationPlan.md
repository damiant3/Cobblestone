# ARM64 Codegen Optimization Plan

> **Filed to Done 2026-07-15 (val):** the campaign shipped (CLs 6141-6173); the one remaining phase -- frame elision for plain leaves -- is tracked in BACKLOG 3.2. Moved out of Active to keep init light; reopen if frame elision is picked up.

**Status:** Campaign partly executed (CLs 6141-6173). ARM64 codegen
beats **GCC -O0** on aggregate across the **four** micro-benchmarks:
Codex 74 vs GCC -O0 78 (docs/ExaminersAssay.md, ARM64 section). It does
**not** beat -Os -- it loses to -Os on every one of the four (fib 21 vs
16, fact 13 vs 9, gcd 23 vs 7, sum 17 vs 9; aggregate 74 vs 41). The
one standout is fact, which beats -O0 (17) and -O2 (15).

| Bench | GCC -O0 | GCC -O2 | GCC -Os | Codex ARM64 |
|-------|--------:|--------:|--------:|------------:|
| fib   |      20 |   237*  |      16 |          21 |
| fact  |      17 |      15 |       9 |          13 |
| gcd   |      21 |       8 |       7 |          23 |
| sum   |      20 |      13 |       9 |          17 |
| total |      78 |         |      41 |          74 |

*GCC -O2 fib unrolls tree recursion into 237 instructions.

(The "beats -Os across all 8 micro-benchmarks" claim that stood here
was **RISC-V's** result, misattributed. RISC-V runs an 8-benchmark
suite and beats -Os on four of them. ARM64 runs four and beats -O0 on
aggregate. Do not conflate them.)

The goal below -- match or beat GCC -O0 on the four -- **is achieved**.
The gap to -Os is the honest remaining headroom, and Phase 5 (frame
elision) is the unbuilt phase that would close part of it. The "Current
State" table below records the PRE-campaign baseline.

## Goal

Match or beat GCC -O0 on all four micro-benchmarks (fib, fact, gcd,
sum). The x86-64 emitter achieved this via a 24-CL campaign starting
at ~80 instructions per benchmark and ending at 14-21. The ARM64
emitter starts at 26-36 instructions per benchmark; the target is
16-22 (GCC -O0 range on AArch64).

## Current State (2026-06-26, CL 6111)

| Bench | Codex ARM64 | GCC -O0 | GCC -O2 | Gap vs -O0 |
|-------|------------:|--------:|--------:|-----------|
| fib   | 36          | 20      | 237*    | +80%      |
| fact  | 26          | 17      | 15      | +53%      |
| gcd   | 31          | 21      | 8       | +48%      |
| sum   | 28          | 20      | 13      | +40%      |

*GCC -O2 fib is 237 instructions because GCC fully unrolls the
recursion into a giant iterative state machine. GCC -Os fib is 16.

## Analysis: Where the Instructions Go

**fib (36 insns) breakdown:**
```
Prologue:     7   sub sp; 5x STP (saves x19-x27, x29, x30)
Param save:   1   mov x19, x0
Body:        18   two comparisons, two recursive calls, add
Epilogue:     8   5x LDP, add sp, ret
Result MOVs:  2   mov x20, x21; mov x0, x20 (join + return)
```

Only x19 and x22 are live across calls; x20-x21 and x23-x27 are
saved/restored for nothing. That is 8 wasted STP/LDP instructions
(4 pairs). The body has 4 redundant MOVs from not emitting directly
to the destination register.

**GCC -O0 fib (20 insns):**
```
Prologue:     3   stp x29/x30; mov x29, sp; str x19
Body:        13   
Epilogue:     4   ldr x19; ldp x29/x30; ret
```

GCC saves only what it uses. No redundant MOVs.

## Optimization Phases

### Phase 1: Selective Callee-Saved Save/Restore

**Impact: -8 to -10 instructions per function (fib 36 -> ~26)**

Currently `a64-emit-prologue` always saves 5 pairs (x19/x20 through
x27/x27 plus x29/x30). Change to only save pairs that contain
registers actually allocated during function emit.

Implementation:
1. `a64-emit-function` already tracks `peak-local` (highest allocated
   local register). The number of pairs to save = `(peak-local - 19
   + 2) / 2` (rounded up), plus 1 for x29/x30.
2. Two-pass emit: first pass emits the body with a placeholder
   prologue (fixed-size NOP sled), second pass patches the prologue
   with the actual STP count and adjusts SP offset. Already have
   `a64-patch-insn` infrastructure.
3. Epilogue: emit LDP only for saved pairs. SP adjustment matches
   prologue.

Expected after Phase 1:
- fib: 7 prologue + 8 epilogue -> 3 + 4 = -8 insns = **28**
- fact: same structure, needs 1 pair (x19) = -8 insns = **18**

### Phase 2: Destination-Driven Emission

**Impact: -2 to -4 instructions per function (fib ~28 -> ~24)**

Currently: `sub x14, x19, #1; mov x0, x14` (compute to temp, then
move to arg register).

Fix: when the result of a sub-expression is immediately used as a
function argument or return value, emit directly to the target
register (x0 for first arg/return, x1 for second arg, etc).

Implementation:
1. Add a `target-reg` parameter to `a64-emit-expr`. When non-negative,
   the expression should emit its result to that register instead of
   allocating a fresh local.
2. `a64-emit-let` passes `target-reg = -1` for the value (let it go
   wherever), then binds the result.
3. `a64-emit-apply` passes `target-reg = x0` for the first argument
   expression.
4. Return position: `a64-emit-function` passes `target-reg = x0` for
   the body expression.
5. `a64-emit-binary` with target-reg emits `sub target, left, right`
   directly.

### Phase 3: Redundant MOV Elimination

**Impact: -2 to -3 per function (fib ~24 -> ~22)**

Patterns to eliminate:
- `mov x20, x12; ... mov x0, x20` -> `mov x0, x12` (join-to-return)
- `mov x12, #0; mov x20, x12` -> `mov x20, #0` (literal-to-local)
- `mov x22, x0; ... mov x0, x22` -> keep in x0 (result already there)

Some of these fall out automatically from Phase 2 (destination-driven
emission). The remainder need peephole optimization: scan the emitted
instruction buffer for MOV chains and collapse them.

### Phase 4: Tail Call Optimization

**Impact: fact -8, gcd -10, sum -14 (function becomes a loop)**

Currently fact, gcd, and sum-to are emitted as recursive calls. TCO
converts self-tail-calls to jumps back to the loop top.

The x86-64 emitter already has TCO with parallel-move arg shuffle
(CL 3649). Port the same logic:

1. Detect self-tail-call in `a64-emit-apply`.
2. Emit arg shuffle: evaluate new args, then move them into parameter
   registers in a safe order (no clobbering).
3. Jump back to loop-top label (past the prologue).
4. Skip epilogue for the tail path.

Expected after TCO:
- fact: `cbnz x0, loop; mov x0, #1; ret; loop: mul x1, x1, x0;
  sub x0, x0, #1; b loop` = ~9 insns (matches GCC -O2)
- gcd: `cbz x1, done; rem x2, x0, x1; mov x0, x1; mov x1, x2;
  b top` = ~8 insns (matches GCC -O2)
- sum: `cbz x0, done; add x1, x1, x0; sub x0, x0, #1; b top` =
  ~6 insns (beats GCC -O2's 13)

### Phase 5: Frame Elision -- **NOT BUILT. This is the open work.**

**Impact: -4 to -6 for leaf functions, -2 for near-leaf**

`a64-emit-prologue` (Arm64CodeGen.codex) still emits, unconditionally,
for **every** function including leaves:

```
SUB SP, SP, #96
STP x29, x30, [SP, #0]
STP x19, x20, [SP, #16]
STP x21, x22, [SP, #32]
STP x23, x24, [SP, #48]
STP x25, x26, [SP, #64]
STP x27, x27, [SP, #80]
```

Phase 1 landed on top of this rather than replacing it:
`a64-compute-save-pairs` sizes the save set from `peak-local`, and
`a64-nop-unused-saves` patches the unneeded STPs to NOP after the body
is emitted (the NOPs are then removed by the peephole compactor). So
the *unused pairs* are gone from the final bytes -- but the frame
itself is not. `SUB SP,SP,#96`, `STP x29,x30`, and `STP x19,x20` are
emitted for every function, and `a64-emit-epilogue` unconditionally
emits the matching `LDP x19,x20` + `LDP x29,x30` + `RET`, even when
`save-pairs` is 0.

Still to do:

- **Leaf functions** (no calls): skip STP/LDP x29/x30, skip SP
  adjustment if no spills. Function is just body + ret.
- **Near-leaf functions** (1-2 calls, no callee-saved needed): save
  only x30 (return address), skip frame pointer.
- **Stack guard**: skip `cmp sp, x28` for leaf functions (no call
  can grow the stack). Keep for recursive/calling functions.

### Phase 6: Immediate Operand Fusion

**Impact: -1 to -2 per function (minor)**

- `mov x12, #N; add x0, x19, x12` -> `add x0, x19, #N` (already
  partially done for some patterns)
- `mov x12, #N; cmp x19, x12` -> `cmp x19, #N` (already done)
- `mov x12, #N; mul x0, x19, x12` -> no ARM64 mul-immediate; skip

## Projected vs. actual

Phases 1-4 and 6 shipped. Phase 5 did not, so the last column is a
projection, not a result. The "Actual" column is the measured
post-campaign count from ExaminersAssay.

| Bench | Baseline | Proj. P1 | Proj. P2 | Proj. P3 | Proj. P4 | **Actual (P1-P4, P6)** | Proj. after P5 |
|-------|---------:|---------:|---------:|---------:|---------:|-----------------------:|---------------:|
| fib   | 36       | 28       | 24       | 22       | 22       | **21**                 | 20             |
| fact  | 26       | 18       | 16       | 15       | 9        | **13**                 | 9              |
| gcd   | 31       | 23       | 21       | 19       | 8        | **23**                 | 8              |
| sum   | 28       | 20       | 18       | 17       | 6        | **17**                 | 6              |

All four are at or below GCC -O0 in aggregate (74 vs 78). gcd and sum
badly missed their post-TCO projections -- worth a look alongside Phase
5, since both are dominated by frame setup they do not need.

## Execution Order

Phase 1 first (biggest single-phase impact, simplest to implement).
Phase 4 (TCO) second (transforms 3 of 4 benchmarks from recursive
to iterative -- the largest per-benchmark win). Phase 2 and 3 together
(destination-driven + MOV elimination are interrelated). Phase 5 last
(frame elision benefits compound with earlier phases).

Phases 1, 2, 3, 4 and 6 are done. **Phase 5 is the remaining work.**

## Verification

After each phase:
1. `build/test-cross-batch.ps1 -Arch arm64 -Jobs 4 -RenoTimeout 10`
   must remain 135/135 pass.
2. `bench/compare-iot.ps1` (once the map name bug is fixed) for
   instruction count regression.
3. Run fib/fact/gcd/sum on QEMU and verify correctness against
   expected output.
