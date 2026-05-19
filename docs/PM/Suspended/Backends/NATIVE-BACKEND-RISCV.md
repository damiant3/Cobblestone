# Native Backend — RISC-V Design

**Date**: 2026-03-21
**Author**: Copilot (VS 2022, Windows)
**Status**: Accepted

---

## The Decision

Codex needs a native code backend. Not transpilation to C. Not WASM running on
someone else's runtime. Real machine code that runs on real hardware with nothing
between it and the metal.

We choose **RISC-V (RV64I)** as the first native target.

x86-64 is the eventual practical target. But x86-64 is a variable-length,
CISC nightmare with implicit flags, segment registers, and an encoding scheme
that requires a 2,000-page manual. Building a native codegen against x86-64
first means debugging the *architecture of the codegen* and the *madness of the
ISA* simultaneously. That's two problems at once. We don't do that.

RISC-V lets us build the codegen architecture — register allocation, instruction
selection, stack frames, ELF emission — against a clean, fixed-width, load/store
ISA with 32 general-purpose registers and an instruction encoding you can fit
on one page. Once the architecture works, retargeting to x86-64 (or ARM64, or
anything else) is a swap of the instruction selection layer. The rest stays.

This is the same strategy we used throughout the project: prove the shape on
something simple, then scale to something hard. Lexer before parser. C# emitter
before IL emitter. IL emitter before native codegen. RISC-V before x86-64.

---

## Why Not C

The shortest path to native code is `IR → C → gcc → binary`. We rejected this.

1. **C is not freedom.** It's trading one dependency (the .NET runtime) for
   another (a C toolchain). The whole point is *nothing between us and the metal*.
2. **Segfaults.** Codex has a sound type system. The compiler proves things.
   We are not going to pipe our verified IR into a language famous for undefined
   behavior and silent memory corruption. That's not a step forward.
3. **We already have 12 transpiler backends.** Adding a 13th transpiler target
   doesn't change what Codex is. Emitting machine code does.

## Why Not WASM

We built a WASM backend (Phase 1 shipping on this branch). It's a good target
and it stays in the portfolio. But WASM is a virtual ISA that runs inside a
runtime (Wasmtime, V8, etc.). It's portable and sandboxed, which is valuable.
It is not bare metal freedom. A `.wasm` file cannot boot. It cannot run without
asking permission from a host.

WASM is a deployment target. RISC-V native is a freedom target. Both are worth
having. They serve different purposes.

## Why Not ARM64 or x86-64 First

ARM64 (AArch64) is also a clean RISC-ish ISA and runs on hardware we can touch
(Apple Silicon, Raspberry Pi). It would be a reasonable first target. But:

- ARM has licensing complexity and a more nuanced encoding than RISC-V.
- RISC-V is fully open. The ISA spec is public domain. No IP entanglements.
- RISC-V hardware is cheap and available (SiFive, Milk-V, ~$8 dev boards).
- The simplicity delta between RV64I and AArch64 is real — RISC-V is simpler.

x86-64 is saved for later because it's genuinely harder to emit correctly, and
fighting encoding bugs while also debugging register allocation is a recipe for
misery. We'll get there. CISC madness comes after we have a proven codegen.

---

## Architecture

### New Project: `Codex.Emit.RiscV`

```
src/Codex.Emit.RiscV/
├── Codex.Emit.RiscV.csproj     (net8.0, refs Codex.Emit + Codex.IR + Codex.Types)
├── RiscVEmitter.cs              (IAssemblyEmitter — entry point, returns ELF bytes)
├── RiscVCodeGen.cs              (IR → RISC-V instruction selection)
├── RegisterAllocator.cs         (Linear scan over virtual registers)
├── StackFrame.cs                (Calling convention, locals, spills)
├── RiscVEncoder.cs              (Instruction → 32-bit word encoding)
├── ElfWriter.cs                 (ELF64 binary emission)
└── RiscVRuntime.cs              (Minimal runtime: _start, syscall wrappers)
```

Follows the same pattern as every other emitter. `IAssemblyEmitter.EmitAssembly`
takes an `IRModule`, returns `byte[]`. The bytes are a complete ELF64 binary.

### Dependency Flow

```
Codex.IR → Codex.Emit → Codex.Emit.RiscV
                       → Codex.Emit.IL    (existing)
                       → Codex.Emit.Wasm  (existing)
                       → Codex.Emit.CSharp (existing)
                       → ...
```

No external dependencies. We emit ELF bytes the same way we emit WASM bytes
and PE/IL bytes — `BinaryWriter` on a `MemoryStream`. No linker. No assembler.
No toolchain. The output is a ready-to-run ELF64 binary for Linux/RISC-V.

---

## RISC-V RV64I — What We Need

RV64I is the base 64-bit integer instruction set. It has:

- **32 registers**: `x0` (hardwired zero) through `x31`
- **Fixed 32-bit instructions** (no variable-length encoding pain)
- **Load/store architecture**: only `ld`/`sd` touch memory
- **No flags register**: comparisons produce values, not side effects
- **Simple calling convention**: `a0`-`a7` for args, `a0`-`a1` for returns

We also need the **M extension** (multiply/divide) for integer arithmetic.
RV64IM is our target. Every RISC-V implementation supports this.

### Register Map

| RISC-V | ABI Name | Codex Usage |
|--------|----------|-------------|
| `x0` | `zero` | Hardwired zero |
| `x1` | `ra` | Return address |
| `x2` | `sp` | Stack pointer |
| `x3` | `gp` | Global pointer (heap base) |
| `x8` | `s0`/`fp` | Frame pointer |
| `x10`-`x17` | `a0`-`a7` | Arguments and return values |
| `x9`, `x18`-`x27` | `s1`-`s11` | Callee-saved (for locals) |
| `x5`-`x7`, `x28`-`x31` | `t0`-`t6` | Temporaries (caller-saved) |

For floating point (`Number` type), we use the **D extension** (double-precision
float): registers `f0`-`f31`, instructions `fadd.d`, `fmul.d`, etc. Target
becomes RV64IMD.

### Instruction Encoding

All instructions are 32 bits. Six formats:

| Format | Used For | Layout |
|--------|----------|--------|
| R-type | Register ops (`add`, `sub`, `mul`) | `[funct7][rs2][rs1][funct3][rd][opcode]` |
| I-type | Immediates, loads (`addi`, `ld`) | `[imm12][rs1][funct3][rd][opcode]` |
| S-type | Stores (`sd`) | `[imm7][rs2][rs1][funct3][imm5][opcode]` |
| B-type | Branches (`beq`, `bne`) | `[imm][rs2][rs1][funct3][imm][opcode]` |
| U-type | Upper immediate (`lui`) | `[imm20][rd][opcode]` |
| J-type | Jumps (`jal`) | `[imm20][rd][opcode]` |

That's the entire encoding. One page. Compare to x86-64 where a single `mov`
instruction has ~15 different encodings depending on operand types.

---

## Type Mapping

| Codex Type | RISC-V Representation |
|-----------|----------------------|
| `Integer` | 64-bit value in GPR (`x` register) |
| `Number` | 64-bit double in FPR (`f` register) |
| `Boolean` | 64-bit value in GPR (0 or 1) |
| `Text` | GPR holding pointer to length-prefixed UTF-8 on heap |
| `Nothing` | No register (void) |
| `List a` | GPR holding pointer to cons-cell chain on heap |
| Record | GPR holding pointer to struct on heap |
| Sum type | GPR holding pointer to tagged struct (tag word + fields) |

Everything is either a 64-bit register value or a pointer to heap memory.
Same model as the WASM backend but with real registers instead of a stack.

---

## IR → RISC-V Translation

### Phase 1: Virtual Registers

The code generator first translates IR into instructions using **virtual
registers** (unlimited supply). This separates instruction selection from
register allocation — two hard problems solved independently.

```
IRIntegerLit(42)     →  li   v0, 42
IRBinary(Add, l, r)  →  add  v2, v0, v1
IRIf(cond, t, e)     →  beqz v0, .Lelse; <then>; j .Lend; .Lelse: <else>; .Lend:
IRLet(x, val, body)  →  <emit val into vN>; <emit body with x→vN>
IRApply(f, arg)      →  mv a0, vN; call f; mv vR, a0
IRName(x)            →  mv vR, <lookup x>
```

### Phase 2: Register Allocation

Linear scan allocator over the virtual register set. Maps virtual registers
to physical registers (`a0`-`a7`, `s1`-`s11`, `t0`-`t6`). Spills to stack
when registers run out.

Linear scan is O(n), simple to implement correctly, and produces good-enough
code. We can upgrade to graph coloring later if profiling shows register
pressure is a problem. It won't be for the programs we're compiling now.

### Phase 3: Encoding + ELF Emission

Virtual registers are resolved to physical register numbers. Instructions are
encoded as 32-bit words. The words are laid into an ELF64 `.text` section.
Static data (string literals) goes into `.rodata`. Heap starts after BSS.

---

## Calling Convention (Standard RISC-V LP64D)

```
Arguments:      a0-a7  (integer), fa0-fa7 (float)
Return:         a0-a1  (integer), fa0-fa1 (float)
Callee-saved:   s0-s11, fs0-fs11
Caller-saved:   t0-t6, ft0-ft11, a0-a7, fa0-fa7
Stack:          grows downward, 16-byte aligned
Frame pointer:  s0 (optional but we use it for simplicity)
```

Codex functions with ≤8 parameters pass all args in registers (covers nearly
everything — Codex functions are curried and typically take 1-3 args after
flattening). Overflow args go on the stack.

---

## System Calls (Linux/RISC-V)

No libc. Direct Linux syscalls via `ecall`:

| Codex Operation | Syscall | Number |
|----------------|---------|--------|
| `print-line` | `write(1, buf, len)` | 64 |
| `read-line` | `read(0, buf, len)` | 63 |
| `read-file` | `openat` + `read` + `close` | 56, 63, 57 |
| `write-file` | `openat` + `write` + `close` | 56, 64, 57 |
| exit | `exit(code)` | 93 |
| heap (brk) | `brk(addr)` | 214 |

That's the entire runtime: ~6 syscall wrappers. No libc, no dynamic linker,
no shared libraries. A statically linked ELF binary with nothing but our code
and the kernel interface.

---

## ELF64 Binary Format

The output is a minimal ELF64 executable:

```
ELF Header (64 bytes)
Program Header: PT_LOAD for .text + .rodata (r-x)
Program Header: PT_LOAD for .data + .bss (rw-)
.text:   RISC-V machine code
.rodata: String literals, constant data
.data:   Mutable globals (heap pointer)
.bss:    Zero-initialized space
```

No section headers needed for execution (only for debugging). No symbol table
needed. No relocations — we resolve everything at emit time. The resulting
binary is as small as physically possible: headers + code + data.

A hello-world should be well under 1KB.

---

## Memory Management

Same as WASM: **bump allocator**. A global pointer starts after `.bss` (or we
`brk` to get heap space). Every allocation moves the pointer forward. No free.

This is honest. Short-lived programs (compiler invocations, scripts, tools)
never need to free. Long-running programs that need memory management will get
it when the type system's linearity analysis (already implemented) drives a
region-based allocator. That's Peak III.

---

## Implementation Phases

### Phase 1: Hello RISC-V (Days)

1. Create `Codex.Emit.RiscV` project, wire into solution
2. `RiscVEncoder`: encode R/I/S/B/U/J format instructions
3. `ElfWriter`: emit minimal ELF64 header + single PT_LOAD
4. Hardcoded test: emit `_start` that calls `write(1, "Hello\n", 6)` + `exit(0)`
5. Verify: `qemu-riscv64 ./hello` prints "Hello"
6. IR integer literals + `print-line` via `write` syscall
7. Test: `main = 42` compiles and prints "42"

### Phase 2: Arithmetic + Control Flow (Days)

8. `add`, `sub`, `mul`, `div` (R-type + M extension)
9. Comparisons → `slt`, `beq`, `bne`
10. `if/then/else` → branch sequences
11. `let` bindings → virtual register mapping
12. Function calls → `jal`/`jalr` + calling convention
13. Recursion
14. Test: `factorial 5` → 120

### Phase 3: Register Allocation (Days)

15. Linear scan allocator
16. Spill/reload to stack
17. Callee-save/restore in function prologues
18. Test: programs with >32 live variables

### Phase 4: Heap Types (Week)

19. Bump allocator via `brk` syscall
20. String literals in `.rodata`, runtime strings on heap
21. Records → heap-allocated structs
22. Sum types → tagged structs
23. Pattern matching → tag loads + branches
24. Lists → cons cells
25. Test: all sample programs that work on IL

### Phase 5: Float + Remaining Builtins (Days)

26. D extension: `fadd.d`, `fmul.d`, `fdiv.d`, `fsub.d`
27. Float-to-int, int-to-float conversions
28. `show`, `text-length`, `++`, string builtins
29. File I/O syscalls

### Phase 6: CLI Integration

30. `--target riscv` flag in `Codex.Cli`
31. `codex build hello.codex --target riscv` → `hello` (ELF64)

### Future: x86-64 Retarget

- New `RiscVEncoder` equivalent for x86-64 encoding (variable-length, REX prefixes)
- New instruction selection rules (same IR, different instructions)
- Same register allocator, same stack frame logic, same ELF writer (different e_machine)
- ARM64 retarget follows the same pattern

---

## Testing Strategy

```
tests/Codex.Types.Tests/
├── RiscVEncoderTests.cs        (instruction encoding unit tests)
├── ElfWriterTests.cs           (binary format validation)
├── RiscVEmitterTests.cs        (compile + run under qemu-riscv64)
```

Integration tests use `qemu-riscv64` (user-mode emulation). Tests skip
gracefully if QEMU is not on PATH, same pattern as the WASM tests with
wasmtime. On CI, we install QEMU — it's a single apt package.

---

## What This Gives Us

When Phase 2 is done, Codex will be able to do this:

```
codex build factorial.codex --target riscv
qemu-riscv64 ./factorial     # prints 120
```

No runtime. No VM. No garbage collector. No framework. No package manager.
No shared libraries. Just an ELF binary, a kernel, and a CPU.

That's freedom.

When we retarget to x86-64 and ARM64, the same binary runs without QEMU.
On a Raspberry Pi. On a laptop. On a server. On anything with a CPU and
a Linux kernel. And eventually, without the kernel too — but that's Peak IV.

---

## Relationship to Other Backends

| Property | Source Backends | IL Backend | WASM Backend | RISC-V Backend |
|----------|----------------|------------|-------------|---------------|
| Output | Source text | .NET PE | `.wasm` | ELF64 native |
| Runtime needed | Target compiler | .NET | WASM runtime | **None** |
| Sandboxed | No | No | Yes | No (full access) |
| Portable | Per-language | .NET platforms | Everywhere WASM runs | RISC-V Linux |
| Bare metal | No | No | No | **Yes** |
| Binary size | N/A | ~5KB+ | ~1KB+ | **<1KB possible** |
