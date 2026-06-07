# Codegen Analysis: Codex vs C (MSVC x86-64)

## Summary

Comparison of x86-64 machine code emitted by the Codex self-host compiler
against MSVC 19.44 (Visual Studio 2022) at `/Od` (unoptimized) and `/O2`
(full optimization) for four micro-benchmarks: fibonacci, factorial, GCD,
and sum-to-N.

All four benchmarks produce correct results. TCO is working. Codex emits
4-5x more instructions than MSVC `/O2` and 3-5x more than MSVC `/Od`.

## Instruction Counts

| Benchmark | C /Od | C /O2 | Codex | vs /O2  | vs /Od  |
|-----------|-------|-------|-------|---------|---------|
| fib       | 19    | 20    | 107   | +435%   | +463%   |
| fact      | 16    | 15    | 79    | +427%   | +394%   |
| gcd       | 18    | 14    | 79    | +464%   | +339%   |
| sum       | 20    | 23    | 82    | +257%   | +310%   |

## Root Causes

### 1. Fixed preamble: ~36 instructions per function entry

Every function emits an identical sequence:

```asm
; -- callee-saved register saves (4 pushes: rbx, r12, r13, r14)
push  rbp / mov rbp,rsp / push rbx / push r12 / push r13 / push r14

; -- stack high-water mark tracking (6 insns)
mov   r11, 0x7040
mov   r11, [r11]          ; load current stack HWM
cmp   rsp, r11
jae   skip
mov   r11, 0x7040
mov   [r11], rsp          ; update HWM

; -- stack overflow guard (2 insns)
cmp   rsp, r10            ; r10 = stack limit
jb    __out_of_memory

; -- watchdog/scheduler preemption check (~22 insns)
mov   r11, 0x7120         ; check preempt flag
mov   r11, [r11]
cmp   r11, 0
je    skip_preempt
mov   r11, 0x7110         ; check process table
mov   r11, [r11]
cmp   r11, 0
je    skip_preempt
push  rdi / push rsi
... (clear flag, unlink from schedule queue, call scheduler) ...
pop   rsi / pop rdi
```

**Impact:** For `fib` (which does ~190M calls for fib(35)), this is ~7B
wasted cycles on a 3.5 GHz core. The preamble alone is larger than the
entire MSVC `/O2` function body.

**Mitigation:** See `InlineOptimization.md` Phase 1 (leaf inlining) for
removing the preamble from trivial functions. For non-leaf functions,
the stack guard and HWM tracking can be hoisted to loop headers or
made conditional on recursion depth.

### 2. No register allocation: stack round-trips

Every intermediate value is stored to the stack frame and immediately
reloaded. Example from `fib`, the `n == 1` comparison:

```asm
; Codex: 10 instructions for "if n == 1"
mov   [rbp-28h], rbx      ; store n to stack
mov   rdi, 1              ; load literal 1
mov   [rbp-30h], rdi      ; store 1 to stack
mov   r8, [rbp-28h]       ; reload n from stack
mov   r9, [rbp-30h]       ; reload 1 from stack
cmp   r8, r9              ; compare
sete  al
movzx rax, al
test  rax, rax
je    else_branch

; MSVC /O2: 2 instructions for "if (n <= 1)"
cmp   edi, 1
jg    else_branch
```

**Impact:** Adds ~3-4 memory ops per subexpression. In `fib`, 53 memory
ops vs 7 for MSVC `/O2`. On modern x86, each unnecessary store-load pair
costs 4-7 cycles (store forwarding latency).

**Mitigation:** Register allocation pass. The emitter currently treats
every IR node as if it needs a stack slot. A simple linear-scan allocator
over the IR tree would eliminate most of these. Even a peephole pass that
recognizes `mov [rbp-X], reg; mov reg2, [rbp-X]` and replaces with
`mov reg2, reg` would help.

### 3. Comparison materialization: 5 instructions instead of 2

Every `==` comparison compiles to:

```asm
cmp   r8, r9
sete  dl            ; materialize boolean to register
movzx rdx, dl       ; zero-extend
test  rdx, rdx      ; test the materialized boolean
je    target         ; finally branch
```

MSVC emits `cmp` + conditional jump directly. The `sete`/`movzx`/`test`
sequence is only needed when the boolean result is used as a value
(e.g., stored to a variable). When it flows directly into an `if`, the
intermediate boolean is dead and should be eliminated.

**Impact:** +3 instructions per comparison. In `fib`, there are 2
comparisons (n==0, n==1), costing 6 extra instructions. In hot loops
like `gcd` and `sum-to`, this adds up quickly.

**Mitigation:** When emitting `IrIf` whose condition is `IrEq`/`IrLt`/
etc., emit `cmp + jcc` directly instead of materializing the boolean.
This is a targeted emitter change, not a full optimization pass.

## Positive Findings

**TCO works.** Both `gcd` (tail-recursive with `math-mod`) and `sum-to`
(tail-recursive accumulator) emit a backward `jmp` instead of a
recursive `call`. The TCO'd loop body in `sum-to` is:

```asm
; sum-to (n) (acc) = if n == 0 then acc else sum-to (n-1) (acc+n)
; After the n==0 check, the else branch:
sub   rdi, r8           ; n - 1
add   r11, r15          ; acc + n
mov   rbx, r13          ; update n
mov   r12, r14          ; update acc
jmp   loop_top          ; tail call -> jump
```

This is structurally correct and comparable to what MSVC `/Od` produces
for the iterative C version. The overhead is in the surrounding
register shuffling, not the loop itself.

## Benchmark Harness

Source: `bench/` (CL 3091). Four C files, four Codex files, four scripts:

- `bench/compare.ps1` — full pipeline: compile C (/Od + /O2), compile
  Codex, extract CDX disassembly, produce comparison report
- `bench/compare.ps1 -SkipBuild` — regenerate report from existing
  build artifacts
- Per-function disassembly: `bench/build-output/codex/<name>/funcs/`

## Priority for Optimization Work

1. **Comparison folding** (Issue 3) — smallest change, biggest
   per-instruction impact. One emitter change.
2. **Preamble elision for leaf functions** (Issue 1) — pairs with
   `InlineOptimization.md` Phase 1. Skip the entire preamble when
   the function makes no calls and doesn't recurse.
3. **Register allocation** (Issue 2) — largest impact overall but
   also the largest engineering effort. Could start with a peephole
   pass to eliminate trivial store-load pairs.

## Attempt Log: Comparison Folding (CL 3094, reverted)

**Approach:** Added `emit-if-fused` to `X86_64.codex` (lines 1140-1200).
When `emit-if` sees `IrBinary(IrEq/IrLt/etc.)` on integer/bool/char
types, emit `cmp l r` + `jcc inverted-cc else` directly, skipping the
`setcc + movzx + test` materialization. Fallback to `emit-if-generic`
(original path) for text/number/sum equality and non-comparison
conditions.

**Results:**
- Text round-trip: PASS (semantic equivalence + text fixed point)
- Small CDX programs (factorial, fib, gcd, sum): PASS, correct output
- CDX self-compile: FAIL — null pointer dereference at RIP=0x3008
  after 208 VM exits. `MemAccess GPA=0x0`, `RCX=0x0`.

**Analysis:** The fused emit is correct at the source level (text mode
proves this) and works for small programs. The crash occurs only during
full compiler self-compilation to CDX — very early in boot, suggesting
a runtime library function (`__alloc`, `__str_concat`, etc.) has a
broken comparison in the generated CDX. The new compiler generates
fused comparisons for ALL integer `if` branches, including the runtime.

**Constraints discovered:**
- Source must use `|` syntax for same-body pattern arms — the text
  emitter normalizes individual arms to `|` form, causing semantic
  equivalence mismatches if the source uses separate arms.
- Mixed-arity `|` patterns work: `is IntegerTy (lo) (hi) (mode)
  | BooleanTy | CharTy -> True`.

**Next steps for a retry:**
- Start narrower: only fuse `IrEq` against `IrIntLit 0` (the `n == 0`
  pattern), which is the single most common comparison. This limits
  blast radius to one case.
- Add a compile flag (`fuse-cmp`) to enable/disable, so the optimization
  can be tested incrementally without affecting the base build.
- Debug the crash: boot the Sut.cdx in QEMU with GDB, set a watchpoint
  on the null deref at 0x3008, trace back to see which fused comparison
  produced the bad branch.
