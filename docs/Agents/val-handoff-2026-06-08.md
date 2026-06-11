# Val Agent Handoff — 2026-06-08

## Session Summary

**Codegen immediate-form optimizations** landed on CodexMagic and
copied to main. Two CLs:

- **CL 3518** (main CL 3529): `emit-binary-mul-imm` — 3-operand
  `imul rd, rs, imm32` for IrMulInt with IrIntLit right. Adds
  `imul-rri` encoding to X86_64Encoder.codex. Seed -587 bytes.

- **CL 3542** (main CL pending): `emit-binary-cmp-imm` — standalone
  `cmp-ri l.reg imm + setcc cc rd + movzx rd` for integer/bool/char
  comparisons with IrIntLit right. Gates on `is-fusable-int-comparison`
  to exclude text/sum equality. Seed -2200 bytes.

Combined: seed 2,127,259 -> 2,124,472 bytes (-2,787 bytes, -0.13%).

Merge-downs from main: CLs 3475, 3495, 3522, 3523, 3549.

## Root Cause: emit-binary-simple-right

Attempted and proved unsafe: skipping `alloc-local` in
`emit-binary-standard` for simple right operands (IrIntLit, IrName-local).
Passes all 201 tests and first self-compile but crashes on second
self-compile (stage1->stage2) at `load-local+141` with corrupted
callee-saved registers (R12=-1, R13=-1, R15=-1).

**Root cause:** `next-local` counter divergence. The standard path
allocates 1-2 anonymous locals (via alloc-local) to save left/right
across evaluation. Skipping these changes `next-local`, which changes:
(1) which register subsequent alloc-local calls return, (2) frame size
via `unused = 4 - min(next-local, 4)`, (3) epilogue callee-saved
restoration, (4) load-local-toggle rotation.

**The safe pattern:** add NEW paths in `emit-binary` (like mul-imm and
cmp-imm alongside add-imm/sub-imm) that follow: `emit-expr left,
alloc-temp, single instruction`. These don't touch emit-binary-standard
and keep `next-local` invariant.

## Next Task: Register Allocator

The fundamental bottleneck. Every IR expression goes through
alloc-temp/alloc-local with no liveness analysis. The emitter does a
single-pass tree walk — every intermediate spills to a temp, every
let-binding gets its own local register or stack slot.

### Current Architecture

**Files:**
- `codex/compiler/Emit/X86_64State.codex` — alloc-temp, alloc-local,
  store-local, load-local, CodegenState record
- `codex/compiler/Emit/X86_64.codex` — emit-expr, emit-binary-standard,
  emit-to-local, emit-function (prologue/epilogue)
- `docs/Designs/Compiler/Active/CodegenAnalysis.md` — analysis document

**Register sets:**
- Temps: [RAX, RCX, RDX, RSI, RDI, R11] — 6 registers, cycled mod 6
  via `next-temp`. Caller-saved.
- Locals: [RBX, R12, R13, R14] — 4 registers, allocated sequentially
  via `next-local`. Callee-saved, pushed in prologue.
- Unused: R8, R9 — currently only used as scratch in `load-local`.
  Available for a real allocator.
- Fixed: R10 (heap), R15 (closure env), RBP (frame), RSP (stack)

**Key invariant:** `next-temp` must be advanced even when an optimization
bypasses `alloc-temp` (the "codegen-temp-counter" rule). `next-local`
must not be changed without understanding its cascading effects on
frame layout and epilogue.

### What a Register Allocator Would Fix

1. **Eliminate alloc-local waste in emit-binary-standard.** Each binary
   op allocates 1-2 anonymous locals just to hold left/right results
   across right-side evaluation. A liveness-aware allocator could keep
   values in temps across simple right expressions.

2. **Reduce temp spill/reload cycles.** Currently every intermediate
   result round-trips: alloc-temp -> use -> alloc-local -> store ->
   load -> use. With liveness info, many of these can stay in temps.

3. **Use R8 and R9.** Two free registers not in either pool. A real
   allocator could assign them to hot values.

4. **Enable emit-binary-simple-right safely.** The 30KB optimization
   that's blocked by next-local divergence becomes viable if the
   allocator separates "callee-saved register allocation" from
   "temporary storage allocation."

### Design Options

**Option A: Linear-scan over the IR tree.** Before emitting a function,
walk the IR to compute live ranges for each named binding. Assign
registers greedily. Spill the least-used. This is the standard approach
(Poletto & Sarkar 1999). Fits the single-pass emit model if the
pre-scan is cheap.

**Option B: Peephole on emitted code.** After emitting all instructions,
scan for `mov [rbp-X], reg; mov reg2, [rbp-X]` pairs where the stack
slot is written once and read once. Replace with `mov reg2, reg` and
eliminate the spill. Lower complexity, lower reward.

**Option C: Restructure alloc-local.** Separate the "I need a register
to survive across a subexpression evaluation" concern from the "this
is a named let-binding that persists for the function body" concern.
Anonymous saves use a separate pool (e.g., R8/R9 as a "save pool")
that doesn't affect next-local. Named bindings use the existing local
pool. This is the minimal change that unblocks emit-binary-simple-right.

### Constraints

- **Fixed-point test is the acceptance gate.** A change that passes 201
  tests but fails stage1->stage2 byte-identity is rejected.
- **Bare-metal, no GC.** All IR tree walking happens in bounded memory.
  The liveness pre-scan must not blow the heap.
- **~29,000 lines of Codex across 54 compiler files.** The emitter is
  ~1,400 lines in X86_64.codex, ~1,500 in X86_64Compound.codex.
  Changes must be incremental and testable.
- **One thing at a time.** Don't batch. Each CL should be independently
  correct and pass all gates.

### Recommended Approach

Start with Option C (restructure alloc-local). It's the smallest change
that unblocks the biggest win (emit-binary-simple-right, ~17KB). Then
Option A (linear-scan) for the full allocator. Option B (peephole) is
the fallback if Option A is too complex for this milestone.

### Benchmark Targets

| Bench | Current | C /Od | C /O2 | Target |
|-------|---------|-------|-------|--------|
| fib   | 32      | 19    | 20    | ~22    |
| fact  | 27      | 16    | 15    | ~18    |
| gcd   | 32      | 18    | 14    | ~20    |
| sum   | 36      | 20    | 23    | ~24    |

Target: match C /Od (unoptimized) on all four benchmarks. Getting
below /Od would require optimizations beyond register allocation
(constant folding, strength reduction, etc.).

## Workspace State

- Stream: `//Codex/CodexMagic`, client: `BigWhite_Codex_val`
- Parent: `//Codex/main`
- Merged down through CL 3549
- Force-synced, p4 clean, full build green, hard fixed point one pass
- Seed: 2,124,472 bytes (CL 3543)
- Copy-up of CL 3542 (cmp-imm) still needed on main
