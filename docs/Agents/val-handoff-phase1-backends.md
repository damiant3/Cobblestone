# Val Handoff: Phase 1 — ARM64 + RISC-V IR Coverage

**Date**: 2026-06-14
**Task**: Close the IR-to-native coverage gap on both backends
**Priority**: Closures first, then guards, then TCO

## Current State

Both backends (ARM64 in `codex/plugs/arm64/`, RISC-V in `codex/plugs/riscv/`)
handle arithmetic, control flow, function calls, records, field access, basic
match expressions, and lists. Hello World runs on QEMU for both.

## What's Implemented vs Missing

| IR Node | x86-64 Reference | ARM64 | RISC-V |
|---------|------------------|-------|--------|
| IrIntLit, IrBoolLit, IrTextLit | emit-expr | Done | Done |
| IrAddInt, IrSubInt, IrMulInt, IrDivInt | emit-binary-* | Done | Done |
| IrEq, IrNotEq, IrLt, IrGt, etc. | emit-comparison | Done | Done |
| IrIf | emit-if | Done | Done |
| IrLet | emit-let | Done | Done |
| IrName (local/global) | emit-name | Done | Done |
| IrCall | emit-call | Done | Done |
| IrRecord | emit-store-record | Done | Done |
| IrFieldAccess | emit-field-access | Done | Done |
| IrMatch (basic) | emit-match | Done | Done |
| IrWildPat, IrVarPat, IrCtorPat | emit-match-arm | Done | Done |
| IrLitPat | emit-match-arm | Stub | Stub |
| IrList, IrConsList, IrAppendList | emit-list | Done | Done |
| IrAct | emit-act | Done | Done |
| **IrClosure** | X86_64Compound:561 | **MISSING** | **MISSING** |
| **Guard expressions** | X86_64Compound:1477 | **MISSING** | **MISSING** |
| **TCO** | X86_64:110-138 | **STUB** (state exists) | **STUB** (state exists) |
| **Type class dict dispatch** | via closures | Blocked by closures | Blocked by closures |

## Implementation Order

### 1. Closures (Critical — blocks type classes, higher-order functions)

**What it is**: Partial application creates a closure object on the heap
containing the function pointer and captured values. Calling a closure
loads the captures and jumps to the function.

**x86-64 reference**: `X86_64Compound.codex` lines 561-579 (`emit-partial-application`)

**What each backend needs**:
1. Heap-allocate closure: `[func-ptr, capture1, capture2, ...]`
   - ARM64: `x28` is heap pointer, bump-allocate (add x28, x28, size)
   - RISC-V: `s1` is heap pointer, bump-allocate (add s1, s1, size)
2. Emit trampoline code inline (jump over initialization)
3. Store function address and captures
4. Return closure pointer in `x0`/`a0`

**ARM64 stub location**: `a64-emit-closure-call` at line 557 (call side exists, allocation missing)
**RISC-V stub location**: `rv-emit-closure-call` at line 505 (same)

### 2. Guard Expressions (Medium — needed for filtered match arms)

**What it is**: A `when` branch with an extra condition: `is Foo (x) if x > 0 -> ...`

**x86-64 reference**: `X86_64Compound.codex` line 1477 (`emit-match-guard`)

**What each backend needs**:
- After binding the pattern variables, emit the guard condition
- If guard fails, jump to the next arm (same as pattern mismatch)

### 3. TCO (Low priority — optimization, not correctness)

**What it is**: Self-recursive tail calls become jumps to loop-top.

**x86-64 reference**: `X86_64.codex` lines 110-138

**Both backends already have**:
- `A64TcoState` / `RvTcoState` records with loop-top, param-locals, etc.
- Just need to detect tail-position self-calls and emit jump instead of call

## Architecture Notes

### ARM64 Register Convention
```
x0-x7:   args + return
x9-x15:  temps (x12-x15 cycled by alloc-temp)
x19-x27: callee-saved locals
x28:     heap pointer (fixed)
x29:     frame pointer
x30:     link register
```

### RISC-V Register Convention
```
a0-a7:   args + return
t0-t6:   temps (t3-t6 cycled)
s0:      frame pointer
s1:      heap pointer (fixed)
s2-s11:  callee-saved locals
ra:      return address
```

### Plug Communication
- **ARM64**: File-mapped I/O via `Arm64Plug.codex` — reads IR from file, emits binary
- **RISC-V**: TCP on port 9100 via `RiscVPlug.codex` — receives IR as S-expressions

### Wire Output Format (both identical)
```
[u32] code-length
[u32] data-length
[u32] function-count
[code bytes]
[data bytes]
[function table: name-len, name, code-offset per function]
```

## Key Files

| File | Purpose | Lines |
|------|---------|-------|
| `codex/plugs/arm64/Arm64CodeGen.codex` | ARM64 code generator | ~750 |
| `codex/plugs/arm64/Arm64Plug.codex` | ARM64 plug harness | ~100 |
| `codex/plugs/arm64/Arm64Runtime.codex` | 29 runtime helpers | ~470 |
| `codex/plugs/arm64/Arm64Elf.codex` | ELF64 writer | ~200 |
| `codex/plugs/riscv/RiscVCodeGen.codex` | RISC-V code generator | ~700 |
| `codex/plugs/riscv/RiscVPlug.codex` | RISC-V plug harness | ~100 |
| `codex/plugs/riscv/RiscVRuntime.codex` | Runtime helpers | ~470 |
| `codex/compiler/Emit/X86_64Compound.codex` | Reference impl (closures, match, records) | ~1600 |
| `codex/compiler/Emit/X86_64.codex` | Reference impl (TCO, binary ops) | ~900 |

## Test Infrastructure

ARM64: `codex/plugs/arm64/compile-arm64.ps1` + `run.ps1`
RISC-V: `codex/plugs/riscv/compile-riscv.ps1` + `run-riscv.ps1`
Board tests: `codex/test/esp32c6-drivers.codex`, `pi4-drivers.codex`, `stm32f4-drivers.codex`

## What "Done" Looks Like

The backends can compile the compiler's own foreword chapters (which use
closures, records, pattern matching, and type classes extensively). The
acid test: compile a non-trivial program like `codex/test/final-batch-test.codex`
(which exercises Schedule, Pattern, RateLimiter, CountMinSketch, Geodesic)
through the ARM64 plug and get correct output on QEMU.
