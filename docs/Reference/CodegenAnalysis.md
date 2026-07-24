# Codegen Analysis: Codex vs C, C#, F#, Python

## Summary

Cross-compiler comparison of machine code for four micro-benchmarks:
fibonacci (tree recursion), factorial (linear recursion), GCD (tail recursion
with modulo), and sum-to-N (tail-recursive accumulator).

All compilers produce correct results on all benchmarks.

Updated: 2026-06-27 (ARM64 CLs 6141-6173, RISC-V CLs 6159-6172,
x86-64 optimization campaign CLs 6205-6243)

## Cross-Architecture Instruction Counts

| Bench | Codex x86 | Codex ARM64 | Codex RV64 | GCC ARM64 -O0 | GCC ARM64 -Os | GCC ARM64 -O2 |
|-------|----------:|------------:|-----------:|--------------:|--------------:|--------------:|
| fib   | 21        | 21          | 22         | 20            | 16            | 237*          |
| fact  | 17        | 13          | 16         | 17            | 9             | 15            |
| gcd   | 23        | 23          | 7          | 21            | 7             | 8             |
| sum   | 14        | 17          | 9          | 20            | 9             | 13            |

ARM64 total (74) beats GCC -O0 total (78). RISC-V total (54) beats
GCC -O2 total on gcd and sum. x86-64 sum (14) beats C /O2 (23).

## Instruction Counts

Function body only (no main/entry overhead). C# and F# counts are from
.NET 9 RyuJIT (tiered compilation disabled, FullOpts). Python counts are
CPython 3.11 bytecodes -- each bytecode dispatches ~20-50 native
instructions through the interpreter loop, so the real cost is 300-1000x
the bytecode count.

| Bench    | Codex | C /Od | C /O2 | C# JIT | F# JIT | Py 3.11 |
|----------|-------|-------|-------|--------|--------|---------|
| fib      | 21    | 19    | 20    | 21     | 21     | 21 bc   |
| fact     | 13    | 16    | 15    | 16     | 15     | 16 bc   |
| gcd      | 11    | 18    | 14    | 11     | 9      | 15 bc   |
| sum      | 8     | 20    | 23    | 9      | 4      | 17 bc   |
| ack      | 24    | 28    | 21    | —      | —      | —       |
| tak      | 42    | 35    | 39    | —      | —      | —       |
| collatz  | 14    | 28    | 18    | —      | —      | —       |
| locals   | 54    | 47    | 73    | —      | —      | —       |

Tier 2 (CL pending): pure-leaf arg-register allocation. A no-call
self-recursive tail loop with one or two parameters and no locals beyond
them now holds those parameters in the registers they arrive in (RDI/RSI)
instead of callee-saved RBX/R12. No prologue push/pop is owed and each
parameter bind is an identity store the emitter drops. sum 14->8 (matches
F# JIT density, beats C /O2 23), gcd 17->11 (matches C# JIT, beats C /O2
14), collatz 20->14. fact unchanged (linear recursion, not a leaf -- it
calls itself non-tail); tak unchanged (3 params, above the 2-param bound).
See Root Cause 3.

### Codex vs targets

| Bench | Codex | Best native | Gap   | Target |
|-------|-------|-------------|-------|--------|
| fib   | 21    | 20 (C /O2)  | +5%   | ~20    |
| fact  | 17    | 15 (C/F#)   | +13%  | ~18    |
| gcd   | 23    | 9 (F# JIT)  | +156% | ~14    |
| sum   | 14    | 4 (F# JIT)  | +250% | ~8     |

fib matches C# JIT (21) and is 1 instruction above C /O2 (20). The
remaining gap is the stack guard (cmp rsp, r10 + jb) that C doesn't
emit. sum is below C /Od AND C /O2 (14 vs 20 and 23;
the loop body itself is add/lea/jmp, matching the F# JIT's density --
the rest is prologue/epilogue and the entry branch).

Target = match or beat C /Od on every benchmark. Getting below C /Od
into C /O2 territory requires optimizations beyond register allocation
(constant folding, strength reduction, loop unrolling).

### Key findings

**C# and F# on .NET 9 match or beat MSVC /O2 C.** The managed runtime
tax is effectively zero for compute-bound functions. RyuJIT produces
near-optimal x86-64:

- **fib**: C# 21 vs C /O2 20 -- identical structure, one extra movsxd
- **fact**: F# 15 = C /O2 15 -- exact parity
- **gcd**: F# 9 vs C /O2 14 -- F# JIT tail-call-optimized to a tighter
  loop than MSVC's iterative while(b)
- **sum**: F# 4 vs C /O2 23 -- F# JIT emits `movsxd; add; dec; jne`;
  MSVC /O2 loop-unrolled (2x) which INCREASED instruction count

The .NET JIT achieves this by: (1) zero-cost tail call optimization for
recursive functions that the F# compiler marks `.tail`, (2) aggressive
inlining of small functions into callers, (3) register allocation that
keeps loop variables in registers without spilling.

**Python is not comparable at the instruction level.** 15-21 bytecodes
per function, but each bytecode is a C function call through the
interpreter dispatch loop. Real cost: ~400-1000 native instructions per
function call. CPython 3.11 added specializing adaptive interpreter
which helps (~25% faster than 3.10) but it is still interpreted.

## Optimization History

Starting point (CL 3091): fib 107, fact 79, gcd 79, sum 82.

| CL   | Optimization                  | fib | fact | gcd | sum | Seed delta |
|------|-------------------------------|-----|------|-----|-----|------------|
| 3284 | emit-to-local (literals/if)   | 72  | 55   | 58  | 60  | -          |
| 3307 | call-result rax, multi-arg    | 60  | 43   | 50  | 48  | -          |
| 3331 | cascading join, lea, body-rax | 42  | 35   | 42  | 42  | -          |
| 3349 | epilogue callee-saved elision | 38  | 31   | 38  | 38  | -          |
| 3378 | destination-driven IrBinary   | 36  | 29   | 36  | 40  | -          |
| 3396 | prologue NOP elision          | 34  | 29   | 34  | 38  | -          |
| 3400 | stack-guard flag              | 32  | 27   | 32  | 36  | -          |
| 3518 | imul-rri (mul-by-constant)    | 32  | 27   | 32  | 36  | -587 B     |
| 3542 | cmp-imm (standalone cmp-ri)   | 32  | 27   | 32  | 36  | -2200 B    |
| 3575 | emit-binary-reg-right         | 32  | 27   | 32  | 36  | -7644 B    |
| 3637 | eval-tail-arg-direct (merge from main, CLs 3608+) | 32 | 27 | 31 | 29 | - |
| 3649 | TCO direct arg shuffle        | 32  | 27   | 30  | 26  | -11255 B   |
| 3663 | staged binary operands (r8/r9 + stack park) | 31 | 25 | 30 | 26 | -37403 B |
| 3695 | minimal leaf emission (no frame ceremony) | 31 | 25 | 30 | 14 | +2373 B |
| 3702 | near-leaf emission (calls allowed, guard kept) | 23 | 17 | 24 | 14 | +3714 B |
| 3746 | IrRemInt + leaf inliner (math-mod inline) | 23 | 17 | 23 | 14 | +20346 B |
| 3839 | tails-all-direct TCO temp elision | 23 | 17 | 23 | 14 | +1426 B |
| 3845 | commutative both-complex shortcut (pop+op) | 21 | 17 | 23 | 14 | +408 B |
| 6205 | emit-and-push for multi-arg calls | 21 | 15 | 17 | 14 | — |
| 6206 | reg-reg comparison fusion in dest-driven if | 21 | 15 | 17 | 14 | — |
| 6207 | direct arg-reg emission for simple args | 21 | 15 | 17 | 14 | — |
| 6210 | power-of-2 div/rem strength reduction | 21 | 15 | 17 | 14 | — |
| 6212 | left-literal commute + test-mask fusion | 21 | 15 | 17 | 14 | — |
| 6216 | tco-max-complex match/act traversal | 21 | 15 | 17 | 14 | — |
| 6220 | dead jump elimination after TCO branches | 21 | 15 | 17 | 14 | — |
| 6222 | TCO direct div/rem pow2 | 21 | 15 | 17 | 14 | — |
| 6227 | dest-driven TCO fallback + relaxed direct-tail-binop | 21 | 15 | 17 | 14 | — |
| 6229 | single-complex TCO routing (no temp-locals guard) | 21 | 15 | 17 | 14 | — |
| 6233 | tco-max-complex temp reduction with safety nets | 21 | 15 | 17 | 14 | — |
| 6236 | merge adjacent unused-push NOPs into 4-byte NOP | 21 | 15 | 17 | 14 | — |
| 6243 | relax leaf-args-all-bound (name+/-literal accepted) | 21 | 15 | 17 | 14 | — |
| 6356 | Tier 1: in-place reg-left/reg-right folds + TCO spill-to-param | 21 | 13 | 17 | 14 | — |
| pend | Tier 2: pure-leaf arg-register allocation (no push/pop) | 21 | 13 | 11 | 8 | — |

Tier 1 (CL 6356) drops a redundant `mov` in three spots: commutative
local-OP-complex folds into the dead temp (`emit-binary-reg-left`), the
symmetric complex-OP-local case incl. non-commutative sub
(`emit-binary-reg-right`), and a spilled TCO arg loads straight into its
register parameter instead of via scratch (`copy-complex-temps`).
fact 15->13 (now below C /O2 15); tak 44->42; new `regright` bench 14 vs
C /Od+/O2 16. fib/gcd/sum unchanged (no matching body shape). Broad reach
across compiler source, captured in the CL 6357 seed rebuild.

The CL 6357 seed rebuild was the first since the June-21 seed (#169); it
banked the entire CL 6205-6243 campaign plus Tier 1 at once, shrinking the
seed ~2.42 MB -> 1.92 MB (1,921,384 bytes). MAP1 verification confirmed
zero functions dropped vs the prior codegen of identical source -- pure
density.

CLs 6205-6243 do not change fib/fact/gcd/sum (those already hit 1-arg
or all-reg-locals fast paths). The wins appear in the new benchmarks:
ack 42->24, tak 77->44, collatz 35->20, locals 59->54. All four new
benchmarks within 15% of C /O2 or beating it. See the extended
benchmark table above.

Seed: 2,191,873 (start) -> 2,114,168 (current) = -77,705 bytes total (-3.5%). The 3695 delta is positive: the leaf-profile pass costs more code than the ceremony it removes statically; the win is sum at 14 and 5-9 fewer executed instructions per eligible leaf call.

Note: fib/fact/gcd/sum function-body instruction counts plateaued at
CL 3400. The later CLs (mul-imm, cmp-imm, reg-right) reduce seed size
substantially by eliminating alloc-local waste across all ~2600 compiler
functions, but the four benchmarks don't exercise the specific patterns
(e.g., reg-right helps `acc + n` where both are locals -- but sum-to's
TCO path handles args specially and doesn't go through emit-binary).

## Root Causes -- What Remains

### 1. Prologue overhead: 10-14 instructions per function

Current prologue (after NOP elision and stack-guard flag):

```asm
push  rbp               ; frame pointer
mov   rbp, rsp
push  rbx               ; callee-saved (or NOP if unused)
push  r12               ; callee-saved (or NOP if unused)
push  r13               ; callee-saved (or NOP if unused)
nop                     ; r14 slot (NOP'd if unused)
cmp   rsp, r10          ; stack guard
jb    __out_of_memory
sub   rsp, <frame>      ; frame allocation
```

Plus epilogue: `lea rsp,[rbp-N]; pop r13; pop r12; pop rbx; pop rbp; ret`

C /O2 fib prologue: `push rdi; sub rsp, 32` (2 instructions).
C# JIT fib prologue: `push rsi; push rbx; sub rsp, 40` (3 instructions).

**Gap: 8-12 instructions per function.** For fib (32 total), this is
25-38% of the function body.

**What to do:**
- Omit frame pointer for leaf functions (no calls) -- saves push/pop rbp
  and mov rbp,rsp (3 insns). Already have `omit-frame-pointer` flag but
  it increases binary size due to SIB byte penalty on [rsp+offset].
- Skip stack guard for leaf functions -- saves cmp+jb (2 insns). Already
  have `stack-guard` flag but it's per-compilation, not per-function.
- NOP elision already handles unused callee-saved pushes, but the NOPs
  still occupy code bytes. True push elision requires two-pass emit.

### 2. TCO arg shuffle: RESOLVED (CLs 3637 merge + 3649)

Two stages closed this. `eval-tail-arg-direct` (merged down in 3637)
removed the stack spill-reloads by evaluating literal/lea/binop args
straight into the register temps. The direct shuffle (CL 3649) then
removed the temps themselves: `emit-tail-call` plans each site,
writes direct args (literals, local names, integer binops over
locals) straight into the parameter registers in an order that never
clobbers a parameter still needed by a pending direct arg, and only
routes complex args (calls, nested exprs) through the pre-allocated
temps. Cycles (parameter swaps) fall back to the temp path.

sum-to loop body now (3 insns after branch, F# JIT density):

```asm
add   r12, rbx               ; acc += n  (direct into param)
lea   rbx, [rbx-1]           ; n--       (direct into param)
jmp   loop_top
```

Remaining in this area:
- `math-mod` in gcd is a function call instead of inline `idiv`. Two
  attempts logged below: leaf inlining makes gcd's STATIC count worse
  even with staged operands (the substituted div/mul/sub tree plus
  the TCO temp store outweighs the 8-insn call sequence it replaces),
  and folding the inlined idiom to the int-mod builtin is UNSOUND
  (int-mod is floor mod with a sign fixup; the idiom is truncated
  remainder). The honest fix is an IrRemInt-style op that emits
  idiv's RDX directly -- blocked on plug protocol review, since plugs
  consume post-inline IR and would see the new node kind. Static gcd
  parity also needs the prologue work (Root Cause 1): prologue +
  epilogue are 16 of gcd's 30 instructions.
- When every tail site in a function is fully direct, the TCO temps
  are pre-allocated but never used: r13/r14 still get pushed/popped
  (4 insns in sum/gcd). Making pre-alloc-tco-temps conditional on a
  whole-body plan would recover them, but the decision must be made
  before emission and held consistently (next-local stability).

### 3. Register allocation: RESOLVED for binary operands (CL 3663)

emit-binary no longer consumes alloc-locals. Operands stage through
R8/R9 (the registers the allocator never assigns): simple operands
(literals, names bound to locals) are materialized directly into
their staging register after the complex sibling is evaluated, and
both-complex sites park the left result with one transient push/pop
on the machine stack. (R8, R9) feeds emit-binary-op -- the same
consumer the standard path handed its load-local scratch registers.
Anonymous saves are gone: each binary site saves 1-4 instructions,
and named bindings stay in callee-saved registers instead of
cascading to stack slots once binaries have eaten the four register
locals. Measured: -37,403 bytes (-1.8%) across the seed, fib 32->31,
fact 27->25. omit-frame-pointer mode keeps the old standard path
(a push would shift RSP-relative spill offsets).

What remains of register allocation proper: emit-sum-full-eq still
parks in alloc-locals (its operands live across nested helper calls
-- semantically required without liveness analysis), and named
let-bindings still allocate monotonically. A linear-scan pre-pass
(Option A) is now only about named bindings.

The reg-right optimization (CL 3575) proved that skipping alloc-local
for register-local right operands saves -7,644 bytes across the seed
(~2600 functions). The benchmark functions didn't directly benefit
because their hot paths already use imm or TCO paths, but the compiler's
own code (type checking, IR lowering, text emission) is full of `a + b`,
`a == b`, `a < b` where both operands are locals.

**Match-arm + scope register recycling (CL pending) -- the first real
dent in monotonic allocation.** Local registers were allocated
monotonically and never freed: every match branch's pattern bindings,
and every sequential let, permanently advanced `next-local`, so an
N-branch match held N x (bindings) registers though only one arm is ever
live at runtime. Now `alloc-local` tracks `peak-local`/`peak-spill` (the
high-water mark), the prologue/epilogue/frame are sized from the peak,
and `emit-one-match-branch` saves `next-local`/`spill-count` before each
branch and restores them after the branch result is stored -- so
mutually-exclusive arms SHARE registers. Branches are laid out
sequentially with a jump to the merge point, so a matched arm runs fully
and leaves before the next arm's code; sharing is safe by construction.
The match-heavy, let-heavy compiler shrank 38,024 bytes (1,922,654 ->
1,884,630, ~2%) with an identical 3567-function MAP1 set (pure density,
zero code dropped) and battery 182/0/13. Benchmarks are unchanged (no
match expressions, so no recycling -- byte-identical).

**let-value scratch recycling (CL pending) -- and the spill-vs-register
rule.** `emit-let` now restores `next-local` (registers ONLY, never
`spill-count`) after the value is computed into its binding, freeing the
value's evaluation scratch (call-arg saves, nested record-field locals,
nested match scrut/result locals) before the body emits. The binding
survives (it is below the recycle floor); the body emits normally, so
there is no result-liveness hazard. -2,208 bytes, MAP1-identical, battery
182/0/13. Modest because most let-values are simple (binaries stage via
R8/R9). **Hard-won rule:** recycling `spill-count` (reusing spill SLOTS)
is UNSAFE -- the same recycle applied to `emit-if-generic` corrupted a
callee-saved local in a 21KB lifted lambda (R12 garbage, crash in the
self-compiled TEXT emitter); bisecting to next-local-only fixed it. Only
register recycling is safe; spill slots must stay monotonic. (Also: `if`
branches were not worth recycling -- they bind no variables, so the win
that pattern bindings give a match is simply absent, +147 bytes net.)
The real remaining win is last-use liveness for let BINDINGS (free a
binding at its last use, not scope end) -- needs a backward liveness pass;
that is the linear-scan proper.

**Pure-leaf arg-register allocation (Tier 2, CL pending) -- the leading
edge of a per-function partition.** Register allocation is per-function:
`func-local-regs` on CodegenState names the local registers, and
`active-temp-regs`/`active-local-regs` (X86_64State.codex) derive the
two pools from it -- empty means the legacy callee-saved locals + full
caller-saved temp rotation, unchanged. A function `leaf-arg-reg-ok`
proves to be a pure leaf (no calls), self-recursive tail loop, with one
or two parameters and no locals beyond them, gets `func-local-regs` set
to its incoming argument registers (RDI/RSI). The temp rotation carves
those out so a temp can never alias a live local. RDI/RSI dodge the
RAX/RDX that idiv/imul clobber, the R8/R9/R15 binary staging slots, the
heap R10, and R11 -- so no body construct can corrupt a local without a
register-partition computation. The existing minimal-leaf mispredict
guard (errors if emission allocates past the prediction or spills) is
the compile-time backstop; the self-compile fixed point is the runtime
one. Widening to 3+ parameters, let-locals, and non-TCO leaves needs the
general partition: compute L locals and T max-simultaneous temps, assign
locals from one end of the caller-saved pool and temps from the other,
and exclude RAX/RDX whenever the body divides or multiplies.

**What to do (Option C from handoff):**
- Restructure alloc-local to separate anonymous saves (emit-binary-
  standard's temp storage) from named bindings (let/match/param).
  Anonymous saves could use R8/R9 when the right operand is simple,
  keeping next-local stable.
- Or: linear-scan pre-pass over the IR to assign registers before
  emission. This is the full solution (Option A) but the largest effort.

### 4. Two comparisons where one suffices (fib)

Codex fib tests `n == 0` then `n == 1` (two cmp+jcc pairs). C and C#
test `n <= 1` once. This is an IR-level issue: the Codex source says
`if n == 0 then ... else if n == 1 then ...` which produces two
IrIf nodes. An IR optimization pass could merge adjacent equality
tests on the same variable into a range check.

## Compiler Comparison: What They Do That We Don't (Yet)

### .NET RyuJIT (.NET 9)

1. **Tail call optimization** -- F# marks recursive calls with `.tail`
   IL prefix; RyuJIT converts to a loop. Codex has TCO but the arg
   shuffle is expensive (see Root Cause 2).
2. **Inlining** -- Small functions (< ~64 IL bytes) are inlined into
   callers. F# gcd was fully inlined into main. Codex has no inliner.
3. **Register allocation** -- Linear-scan allocator keeps loop variables
   in registers. F# sum loop uses ecx/rdx with zero spills.
4. **Dead code elimination** -- Unused computations are dropped.
5. **Strength reduction** -- `n * 20` becomes `lea + shl` chains. The
   JIT did this for fact(20) in main (`lea rcx,[rax+4*rax]; shl rcx,2`).

### MSVC /O2

1. **Loop unrolling** -- sum was 2x unrolled (two iterations per loop
   pass). This actually increased instruction count vs the JIT's tight
   loop but reduces branch overhead.
2. **Iterative lowering** -- The C source uses while/for loops directly.
   No tail-call conversion needed.
3. **Callee-saved register selection** -- Only saves registers actually
   used. fib saves one (rdi or rbx), not four.

### What Codex already does well

- **TCO** -- Structural correctness proven: backward jmp, no stack
  growth. Just needs tighter arg shuffle.
- **Comparison fusion** -- `emit-if-fused-imm` already fuses
  `cmp ri + jcc` for if-branches with literal right. Standalone
  `cmp-imm` (CL 3542) handles non-if comparisons.
- **Destination-driven emit** -- `emit-to-local` writes results
  directly to target registers, skipping intermediate temps.
- **Prologue elision** -- Unused callee-saved registers are NOP'd.
  Stack guard is flag-gated.

## Priority for Next Optimization Work

1. **DONE (CLs 3637/3649): TCO arg shuffle** -- direct
   dependency-ordered parallel move into param registers. Measured:
   sum 36->26, gcd 32->30, seed -11,255 B.

2. **Inline math-mod for integers** -- Emit `cqo; idiv` directly when
   both args are known integer type instead of calling the library
   function. Direct impact on gcd. Estimated: gcd 30->~18.

3. **Per-function stack guard** -- Skip `cmp rsp, r10; jb OOM` for
   leaf functions (no calls, no recursion). Saves 2 instructions per
   leaf. Estimated: fib no change (not leaf), but large seed impact.

4. **Minimal prologue for leaf functions** -- Skip frame pointer setup
   and callee-saved pushes entirely when a function makes no calls and
   uses only temp registers. Saves 6-10 instructions per leaf.

5. **Register allocator (Option A)** -- Linear-scan pre-pass. The full
   solution that would close most of the remaining gap to C /O2. Every
   intermediate stops round-tripping through alloc-local. Largest
   effort but largest reward.

## Benchmark Harness

Source: `bench/` directory.

- `bench/codex/` -- four Codex source files (fib, fact, gcd, sum)
- `bench/c/` -- four C source files (same algorithms)
- `bench/csharp/` -- four C# source files (.NET 9)
- `bench/fsharp/` -- four F# script files (.NET 9)
- `bench/python/` -- Python 3.11 bytecode disassembly
- `bench/compare.ps1` -- build + compare C vs Codex
- `bench/build-output/` -- compiled artifacts, disassembly, JIT dumps

To regenerate: `bench/compare.ps1` (C vs Codex). C#/F# JIT dumps
require `DOTNET_JitDisasm=*` and `DOTNET_TieredCompilation=0`.

## Attempt Log

### Comparison Folding (CL 3094, reverted)

Added `emit-if-fused` to fuse `cmp + jcc` in if-branches. Worked for
small programs but crashed on CDX self-compile (null deref at boot).
Retried successfully in later CLs with narrower scope (fused-imm only,
gated by `is-fusable-int-comparison`).

### emit-binary-simple-right (sessions 3-4, abandoned)

Attempted to skip alloc-local in emit-binary-standard when right operand
is simple (IrIntLit, IrName-is-local). Caused `next-local` counter
divergence: frame layout, epilogue, and load-local-toggle all changed.
Crash: corrupted callee-saved registers (R12=-1, R13=-1, R15=-1) at
`load-local+141` during stage1->stage2 self-compile.

Root cause fully documented. Led to the safe alternative:
emit-binary-reg-right (CL 3575), which adds a NEW path that routes
around emit-binary-standard entirely, keeping next-local stable.

### Leaf inlining at IR level (2026-06-10, shelved in CL 3656)

Implemented `inline-leaf-calls-in-chapter` (Lowering.codex): inlines
saturated calls to functions whose body is pure integer arithmetic
over their own params (math-mod, align-16 shapes), by substituting
the callee's own body -- semantics by construction, name-keyed
emitter hacks avoided (a user shadowing math-mod without citing
MathLib would have made those unsound). CDX path only; shadow-aware;
defs copied only when a scan finds a qualifying call. Self-compile
and all four bench correctness runs PASS.

**Result: gcd 30 -> 37. Reverted.** The inline `cqo; idiv` landed as
intended, but the substituted `a - (a / b) * b` in the tail-call arg
position routes through emit-binary-standard, which spilled both
intermediates to stack slots and reloaded through r8/r9 -- all four
register locals are held by params + TCO temps inside a TCO loop.
The 4 memory ops plus shuffling exceeded the eliminated call's 5
instructions at the site. (Dynamic cost per iteration likely
improved -- the callee's ~16-instruction prologue/body/epilogue per
iteration is gone -- but the harness metric is static count, and the
emitted spills are real waste.)

The pass is correct and shelved ready in CL 3656. Resurrect after
the register-pressure fix (Root Cause 3): once nested arithmetic in
tail args emits without alloc-local spills, inlining becomes a
strict win. The two pieces compose: inline exposes the arithmetic,
tight emission keeps it in registers.

**Round 2 (same day, post CL 3663 staged operands): still shelved.**
Re-measured on top of staged binary emission: gcd 30 -> 36 static
(was 37 pre-staged). The spills are gone but the substituted
div/mul/sub tree (~9 insns incl. staging) plus the TCO temp store
still exceeds the 8-insn call sequence it replaces, statically.
Seed-wide effect of the bare inliner: -6,336 bytes (0.3%), measured
as Stage1Inl (2,078,068) vs SutInl3 (2,084,404), same source, same
emission, only the pass toggled. Dynamic cost per gcd iteration
improves (~24 -> ~12 executed instructions; the callee's prologue/
body/epilogue round trip disappears) but the harness metric is
static count. Not worth the headline regression; the shelf (CL 3656,
now without the mod fold) waits for either a dynamic-cost metric in
the harness or the IrRemInt + prologue work that makes statics win
too.

**Round 3 (CL 3746, LANDED).** Resurrected with IrRemInt: substituted
bodies are scanned for X - (X / Y) * Y and folded to the new op, which
emits idiv and reads RDX -- exact truncated semantics for all inputs,
unlike the rejected int-mod fold. Two hard-won corrections: the
"-6,336 bytes seed savings" measured in round 2 was entirely the
inliner DELETING deck-record wrappers (deck-record's definition is the
identity, but the emitter intercepts the name and emits
__deck-enter/__deck-exit; inlining it un-allocated deck data and broke
stage1 on every lambda-heavy test). With deck-record excluded the
inliner fires zero times on compiler source -- the pass currently
benefits user programs only (math-mod-class call sites), at ~+20KB of
seed for the machinery. gcd 24 -> 23, pure leaf, idiv-direct loop.
Second lesson, now in KNOWN-CONDITIONS: gate batteries test stage1;
IR passes must be batteried against their own stage1 before gates.

**int-mod fold attempt (same day, REJECTED as unsound).** Tried
rewriting the inlined idiom X - (X / Y) * Y to the int-mod builtin
(idiv leaves the remainder in RDX for free). The emitted loop was
beautiful and gcd's correctness run passed -- but int-mod is FLOOR
mod: it emits a sign-fixup that adds |Y| to negative remainders.
The idiom is TRUNCATED remainder (sign of dividend). They agree on
the bench's positive inputs and diverge for negative dividends:
math-mod(-7, 3) is -1 truncated, 2 floored. Caught by reading the
emitted sign-fixup block in the disassembly before shipping. Lesson:
a green correctness run on one input set is not semantic equality --
read what the builtin actually emits. An exact rewrite needs a
truncated-remainder op (IrRemInt) that emits RDX directly; that adds
a node kind to post-inline IR, which plugs consume, so it needs a
plug protocol review first.

### Commutative Both-Complex Shortcut (CL 3845, LANDED)

When both operands of IrAddInt or IrMulInt are complex (neither is a
literal nor a local name), the staged binary path was: eval left, push,
eval right, mov r9 rax, pop r8, then emit-binary-op (alloc-temp-avoiding
+ mov rd r8 + add rd r9) -- 4 instructions after the second eval. For
commutative ops the result is independent of operand order, so: eval
left, push, eval right, pop r8, add rax r8 -- 2 instructions. The pop
target is r8 (not in the temp cycle) unless r.reg == r8 (rare:
load-local scratch from nested IrIf), in which case r9 is used.

fib 23 -> 21, matching C# JIT. fact/gcd/sum unchanged (no both-complex
binary sites). Seed +408 bytes (the when/is dispatch for IrAddInt and
IrMulInt in emit-binary-staged). The both-complex case is rare in the
compiler's own code (~2600 functions, most binary expressions have at
least one local or literal operand), so the machinery cost exceeds its
seed savings. The win is in user programs with tree recursion or nested
call arithmetic.

## External Research (ai-comp, 2026-07-14)

See `docs/Reference/AiComp/OPPORTUNITIES.md` for the full assessment.

That document is the direct sequel to this one, and it answers the question this
file has been circling for thirty CLs. This file's "Priority for Next
Optimization Work" ends at **item 5, the linear-scan register allocator —
"the full solution... largest effort but largest reward."** The ai-comp reading
says that item is mispriced, and says why:

**A linear-scan allocator assigns registers to live intervals over a linear
instruction list. We have no linear instruction list.** `IRExpr`
(`IR/IRChapter.codex:23`) is a name-based expression *tree* — `IrLet (Text)
(CodexType) (IRExpr) (IRExpr)`, `IrIf` with subexpression arms — with no basic
blocks, no SSA, and shadowing `Text` bindings. The linear sequence first exists
as machine bytes, far too late to allocate over. So Option A is not "add a pass";
it is **"add an IR level"** — the equivalent of ai-comp's `hir-to-lir` —
and that flattening is the bulk of the work, not the allocator on top of it.

This also explains the plateau recorded above (fib/fact/gcd/sum unchanged from
CL 3400 through CL 6243, while the seed kept shrinking): **a peephole cannot see
past the instruction it is rewriting.** The remaining gap to C /O2 is not a
missing peephole, and twenty CLs of evidence say so.

The cheap half is real, though: nine of ai-comp's sixteen passes run on a
*structured tree* much like our IRExpr (their HIR is also `ForLoop`/`If`, not a
CFG), and are portable to the IR we have today — simplify, CSE, DCE, load-elim,
range analysis. The sleeper is the range analysis: our bounds prover
`aexpr-proven-range` (`Types/TypeCheckerInference.codex:742`) is a degenerate
version of theirs, with no loop fixpoint, no widening, and no branch refinement.
Filed as the middle-end campaign.

## External Research (IRISA Harvest, 2026-06-23)

See `docs/Reference/IRISA_Research_Harvest.md` for full context.

### PACAP — WCET Analysis Beyond Instruction Count

The PACAP team (IRISA D3) studies Worst-Case Execution Time analysis
for real-time systems. Our instruction-count metric (this document) is
a proxy for execution time but misses pipeline effects, cache behavior,
and instruction latency variation. PACAP's techniques give
cycle-accurate bounds on specific hardware — this would let the
punctual budget (CDX6011) express "wall-clock microseconds on target X"
rather than just instruction count.

Relevant because: our codegen comparison above shows that instruction
count can be misleading (C /O2 sum uses 23 instructions but loop-
unrolled, which may be faster dynamically; F# JIT uses 4 instructions
in a tight loop). A WCET-style analysis would capture the real cost,
including the stack guard overhead (cmp rsp, r10 + jb) that our
benchmarks consistently pay but C compilers don't.

### EPICURE — Verified Security Through Codegen

The EPICURE team (IRISA D4) proves that compilation preserves security
properties (constant-time execution, information flow control). Their
approach would verify that our punctual codegen doesn't introduce
timing side-channels — e.g., that a punctual function with no
data-dependent branches at the IR level also has no data-dependent
branches in the emitted x86-64. This extends the codegen quality
analysis from "correct and compact" to "correct, compact, and
timing-invariant."
