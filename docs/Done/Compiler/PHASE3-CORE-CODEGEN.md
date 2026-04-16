# Phase 3: Core Codegen — Expressions in Codex

**Date**: 2026-03-31
**Status**: SHIPPED. Self-host bare-metal codegen lives in `Codex.Codex/Emit/X86_64*.codex`. Binary pingpong runs the full compile pipeline end-to-end on bare metal. Archived for rationale; current work tracked in `Active/Compiler/SECOND-BOOTSTRAP.md` and `CurrentPlan.md`.
**Depends on**: Phase 1 (x86-64 encoder, done), Phase 2 (ELF/CDX writers, done)
**Acceptance**: `main = 42` compiles to bare-metal ELF, boots in QEMU, prints `42`

---

## What This Phase Does

Phase 3 ports the expression-emission core of `X86_64CodeGen.cs` (6,075 lines C#)
to Codex. Not all 6K lines — only the subset needed for core expressions. Runtime
helpers (Phase 4), builtins (Phase 5), escape copy (Phase 6), and the boot
sequence (Phase 7) come later.

Phase 3 is the heart of the Second Bootstrap. When it's done, the Codex compiler
can take IR and produce machine code via the Phase 1 encoder, wrap it in an ELF
via the Phase 2 writer, and produce a bootable binary.

---

## What We're Porting

### From C# (`src/Codex.Emit.X86_64/X86_64CodeGen.cs`)

| C# Method/Region | Lines | What it does | Codex file |
|------------------|-------|-------------|-----------|
| Class fields, register state | 8–46 | Codegen state: text/rodata buffers, patch lists, register allocator | `X86_64.codex` |
| `EmitModule` | 48–100 | Entry point: iterate IRDefs, emit functions, patch calls/rodata | `X86_64.codex` |
| `EmitFunction` | 406–522 | Prologue, parameter binding, TCO setup, epilogue, frame patching | `X86_64.codex` |
| `EmitExpr` dispatch | 524–544 | Pattern match on IR node, route to emitter | `X86_64Expr.codex` |
| `EmitIntegerLit` | 548–553 | Load immediate via `li` | `X86_64Expr.codex` |
| `EmitTextLit` | 555–580 | CCE string to rodata, load pointer | `X86_64Expr.codex` |
| `EmitName` | 581–630 | Variable lookup, nullary constructor | `X86_64Expr.codex` |
| `EmitBinary` | 631–780 | Arithmetic, comparison, logical ops | `X86_64Expr.codex` |
| `EmitIf` | 781–814 | Conditional with jump patching | `X86_64Expr.codex` |
| `EmitLet` | 815–826 | Bind value to local | `X86_64Expr.codex` |
| `EmitDo` | 827–850 | Effect sequencing (do/bind) | `X86_64Expr.codex` |
| `EmitRecord` | 1076–1116 | Heap-allocate record, store fields | `X86_64Data.codex` |
| `EmitFieldAccess` | 1118–1145 | Load field from record pointer | `X86_64Data.codex` |
| `EmitMatch` | 1146–1254 | Pattern dispatch with jump patching | `X86_64Data.codex` |
| `EmitList` | 1258–1304 | Heap-allocate list with elements | `X86_64Data.codex` |
| `EmitConstructor` | 959–1001 | Sum type constructor (tag + fields) | `X86_64Data.codex` |
| `EmitApply` | 851–957 | Function call, args in regs/stack | `X86_64Apply.codex` |
| `EmitPartialApplication` | 1002–1063 | Closure trampoline generation | `X86_64Apply.codex` |
| `EmitTailCall` | 199–405 | TCO with heap-reset heuristics | `X86_64Apply.codex` |
| Register alloc helpers | 5891–5934 | AllocTemp, AllocLocal, StoreLocal, LoadLocal | `X86_64.codex` |
| Patch helpers | 5960–6054 | PatchCalls, PatchRodataRefs, PatchJcc | `X86_64.codex` |

### NOT in Phase 3

| What | Phase | Why deferred |
|------|-------|-------------|
| Runtime helpers (__str_concat, __itoa, etc.) | 4 | 22 helpers, ~800 lines — separate concern |
| Builtins (text-length, list-at, etc.) | 5 | 50+ operations — separate concern |
| Escape copy & regions | 6 | Two-space GC with forwarding table — most complex subsystem |
| Boot sequence (multiboot, trampoline, IDT) | 7 | Constant byte sequences, ISR stubs — infrastructure |
| EmitRegion | 6 | Depends on escape copy machinery |
| EmitHandle | Post-MM4 | Algebraic effect handlers — not needed for self-compile |
| EmitFork/EmitAwait | Post-MM4 | Concurrency — not needed for self-compile |

---

## Architecture

### Codegen State

In C#, the codegen uses mutable class fields (`m_text`, `m_rodata`, etc.).
In Codex, we use a **codegen state record** threaded through all emission
functions. Every emitter takes the state, returns updated state + result.

```codex
CodegenState = record {
  text : List Integer,           -- machine code bytes
  rodata : List Integer,         -- read-only data bytes
  func-offsets : List FuncEntry, -- (name, text-offset) pairs
  call-patches : List Patch,     -- (patch-offset, target-name) pairs
  rodata-fixups : List Fixup,    -- (patch-offset, rodata-offset) pairs
  func-addr-fixups : List Fixup, -- (patch-offset, func-name) for closures
  string-offsets : List StrEntry, -- deduplicated string→rodata-offset
  type-defs : List TypeDefEntry, -- type name → field info
  next-temp : Integer,           -- temp register counter (mod 6)
  next-local : Integer,          -- local register counter
  spill-count : Integer,         -- stack spill slots used
  locals : List LocalBinding,    -- name → register/spill-slot
  in-tail-pos : Boolean,         -- TCO: currently in tail position?
  current-func : Text            -- name of function being emitted
}
```

Every emit function returns:

```codex
EmitResult = record {
  state : CodegenState,
  reg : Integer                  -- register holding the result
}
```

This is the standard functional-codegen pattern: state-passing with
a result register. The state record replaces all of C#'s `m_` fields.

### Register Allocation

Identical to C# — the scheme is already simple enough to translate directly:

| Pool | Registers | Allocation | Codex equivalent |
|------|-----------|-----------|-----------------|
| **Temps** | RAX, RCX, RDX, RSI, RDI, R11 | Round-robin via `nextTemp % 6` | `alloc-temp state` → `(state', reg)` |
| **Locals** | RBX, R12, R13, R14 | Monotonic, first 4 in regs | `alloc-local state` → `(state', slot)` |
| **Spill** | Stack `[rbp - offset]` | When locals > 4 | Slots 32+ map to `[rbp - ((slot-32+1)*8 + 32)]` |
| **Spill load** | R8, R9 (alternating) | Toggle per load | `load-local state slot` → `(state', reg)` |
| **Reserved** | RSP, RBP, R10, R15 | Frame, heap, result | Not allocatable |

### Function Prologue/Epilogue

```
Prologue:                          Epilogue:
  push rbp                           lea rsp, [rbp - 32]
  mov rbp, rsp                       pop r14; pop r13; pop r12; pop rbx
  push rbx                           pop rbp
  push r12; push r13; push r14       ret
  sub rsp, <frameSize>
```

Frame size = `spillCount * 8`, 16-byte aligned. Patched after function body
emission (the `sub rsp` immediate is overwritten once spillCount is known).

### Call Convention

**Argument registers** (System V AMD64 ABI): RDI, RSI, RDX, RCX, R8, R9.
Args 7+ pushed to stack in reverse order.

**Return**: RAX. Caller moves result to a temp immediately after call.

**Indirect call** (closures): function pointer in `[R11 + 0]`, captured
args in `[R11 + 8]`, `[R11 + 16]`, etc. Load code pointer, `call rax`.

### Tail Call Optimization

TCO applies when a function has parameters and its body contains a
self-recursive call in tail position. The C# implementation has a
sophisticated heap-reset heuristic (Phase 2a/2b) that decomposes record
arguments and checks whether all heap-typed args point below the
iteration mark. If so, it resets the heap pointer to the mark.

**For Phase 3 initial implementation**: implement basic TCO (jump to loop
top with args re-bound) WITHOUT heap-reset. This is correct but may
increase memory usage on deep recursion. Heap-reset can be added after
the basic chain works.

### Jump Patching

Codegen emits placeholder `jcc rel32 = 0` or `call rel32 = 0`, records
the patch offset, and patches after the target offset is known. In Codex:

```codex
-- Emit a conditional jump, return patch location
emit-jcc : CodegenState -> Integer -> (CodegenState, Integer)

-- Patch a previously-emitted jump
patch-jcc : CodegenState -> Integer -> Integer -> CodegenState
```

### String Literals

Strings are CCE-encoded (1 byte per character), stored in rodata as:

```
[8-byte length][CCE bytes...][padding to 8-byte align]
```

The emitter deduplicates identical strings. A text literal emits a
`mov-ri64` loading the rodata address (patched in PatchRodataRefs).

---

## File Layout

| File | Scope | Est. lines |
|------|-------|-----------|
| `Emit/X86_64.codex` | CodegenState record, module entry, function emission, register allocator, patch helpers | ~450 |
| `Emit/X86_64Expr.codex` | EmitExpr dispatch, literals, names, binary ops, if, let, do | ~600 |
| `Emit/X86_64Data.codex` | Records, lists, field access, match, sum type constructors | ~500 |
| `Emit/X86_64Apply.codex` | Function calls, closures, partial application, TCO | ~500 |
| **Total** | | **~2,050** |

---

## Milestone Strategy

Phase 3 has its own progression. Each milestone proves a larger subset works.

### M3.1: `main = 42` (integer literal, print to serial)

The minimum viable codegen. Requires:
- Module entry (emit one function, build ELF)
- Function emission (prologue/epilogue for `main`)
- `EmitIntegerLit` (load immediate into RAX)
- Serial output of integer result (temporary: inline `__itoa` + serial write
  just for `main`'s return value — full runtime helpers come in Phase 4)
- ELF writer (done, Phase 2)

This proves the chain: Codex IR → encoder → ELF → QEMU boots → serial output.

### M3.2: `let` + arithmetic

- `EmitLet` (bind to local)
- `EmitBinary` for `AddInt`, `SubInt`, `MulInt`, `DivInt`
- `EmitName` (variable lookup)

Test: `main = let x = 10 in let y = 20 in x + y` → prints `30`.

### M3.3: `if` / `else` + comparisons

- `EmitIf` with jump patching
- `EmitComparison` (Eq, Lt, etc.)
- Boolean literals

Test: `main = if 5 > 3 then 1 else 0` → prints `1`.

### M3.4: Function calls + recursion

- `EmitApply` (direct calls with args in registers)
- Call patching (PatchCalls)
- Multiple function emission

Test: `factorial 5` → prints `120`.

### M3.5: Records + field access

- `EmitRecord` (heap allocation)
- `EmitFieldAccess` (pointer + offset load)
- HeapReg (R10) initialization

Test: `main = let p = Point { x = 3, y = 4 } in p.x + p.y` → prints `7`.

### M3.6: Sum types + match

- `EmitConstructor` (tag + fields)
- `EmitMatch` with pattern dispatch
- Constructor patterns, variable patterns, wildcard

Test: pattern matching on sum types.

### M3.7: Lists

- `EmitList` (heap allocation with length header)
- List literal emission

Test: `main = list-length [1, 2, 3]` → prints `3`.

### M3.8: TCO (basic, without heap reset)

- Tail call detection (`ShouldTCO`, `HasTailCall`)
- Loop transformation (jump to loop top, re-bind args)

Test: `tco-stress 1000000` doesn't stack overflow.

### M3.9: Closures + partial application

- Trampoline generation
- Closure heap allocation
- Indirect call via R11

Test: higher-order functions (map, fold).

---

## Key Design Decisions

### 1. State-passing vs mutable state

Codex is purely functional. All codegen state is a record threaded through
functions. This is verbose but correct. The C# code's `m_text.Add(byte)` becomes
`state with text = list-snoc state.text byte` (or equivalent).

**Optimization concern**: building `List Integer` via repeated `list-snoc` is
O(n) per append amortized. A 300 KB binary means 300K appends. This may be
slow but is correct. If it becomes a bottleneck, the `Array` type with O(1)
write (safe mutation design) can replace it later.

### 2. What to stub for `main = 42`

To print `42` to serial, the codegen needs:
- `__itoa` to convert integer to text
- Serial port I/O to print

These are Phase 4 (runtime helpers) and Phase 7 (boot sequence). For M3.1,
we emit a **minimal inline serial print** that handles just the `main` return
value. This is ~50 lines of encoder calls that initialize COM1 and write
digits. It gets replaced by proper runtime helpers in Phase 4.

### 3. Rodata patching in bare-metal mode

For bare-metal, text loads at `0x100000` and rodata follows at
`0x100000 + align(textSize, 8)`. The codegen uses `compute-rodata-vaddr-bare`
from ElfWriter.codex. String literal references are patched after all code
is emitted, when the final text size is known.

### 4. Frame size patching

The function prologue emits `sub rsp, 0` as a placeholder. After the body
is emitted (spillCount known), the immediate is patched in-place. In Codex,
this means recording the patch offset and updating the text byte list at
that offset after emission completes.

This requires an `update-at` or `patch-bytes` helper that replaces 4 bytes
at a given offset in the text list. This is O(n) for a list but only happens
once per function, so acceptable.

---

## Dependencies on Encoder and ELF Writer

Phase 3 calls functions from Phase 1 and Phase 2 directly:

**From `X86_64Encoder.codex`**:
- All instruction encoders (mov-rr, add-rr, sub-ri, cmp-rr, jcc, x86-call, etc.)
- Register constants (reg-rax through reg-r15, arg-regs, callee-saved-regs)
- Condition codes (cc-e, cc-ne, cc-l, etc.)
- `li` (load immediate, smart: xor for 0, mov-ri32 for small, mov-ri64 for large)
- `write-i32`, `write-i64` for inline data

**From `ElfWriter.codex`**:
- `build-elf-32-bare` (wrap text+rodata into bootable ELF)
- `compute-rodata-vaddr-bare` (for rodata patching)
- `compute-text-start-32` (for ELF layout)

---

## Testing Strategy

Each milestone has a concrete test: compile a Codex program to bare-metal
ELF, boot in QEMU, verify serial output. This uses the same infrastructure
as the Phase 2 QEMU boot test (`ElfWriterBootTest.cs`), extended to compile
actual Codex programs through the new backend.

Additionally, byte-level golden tests can compare the Codex codegen output
with the C# codegen output for simple programs, ensuring instruction-level
equivalence.
