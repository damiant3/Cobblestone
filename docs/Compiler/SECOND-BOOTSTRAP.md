# The Second Bootstrap — Cutting the Cord

**Date**: 2026-03-31
**Status**: Plan
**Depends on**: MM3 (proven), Codex emitter (done), pingpong (green at 55 MB HWM)

---

## The Honest Problem

The self-hosted compiler is a self-hosted **front end**. It reads Codex, runs
the full pipeline, and emits Codex. The fixed point holds. Pingpong is green.
But every binary that runs — in QEMU, on bare metal, anywhere — was built by
the C# reference compiler. The chain today:

```
.codex source
    → C# ref compiler (src/Codex.Emit.X86_64/*.cs, 7,000 lines C#)
    → bare-metal ELF
    → QEMU boots it
    → reads .codex over serial
    → emits .codex over serial
```

The reference compiler lock has been broken 20+ times. It will keep being
broken because the C# compiler IS the compiler. The .codex source proves
the front end can regenerate itself, but it cannot produce the binary that
runs it. The x86-64 instruction encoder, the ELF writer, the register
allocator, the boot trampoline, the escape-copy machinery, the 50+
builtins, the 22 runtime helpers — all C#.

**MM3 proved the front end is self-sustaining. It did not prove the compiler
is self-sustaining.** This document is the plan to finish the job.

---

## The Goal

A Codex compiler binary, compiled entirely by Codex, that compiles itself
on bare metal and achieves fixed point. No C# anywhere in the chain.

```
Stage 0:  Last C# build → bare-metal ELF (the final C# artifact)
Stage 1:  ELF (Stage 0) compiles .codex source → bare-metal ELF'
Stage 2:  ELF' (Stage 1) compiles .codex source → bare-metal ELF''
          ELF' == ELF''  →  fixed point. C# is gone.
```

After Stage 2, the C# compiler is genuinely archival. Nothing depends on it.
The reference compiler lock becomes real because there is nothing left to
unlock.

This is MM4: **self-sustaining native compiler on bare metal.**

---

## What Exists

### In Codex (.codex source, self-hosted)

| Component | File(s) | Lines | Status |
|-----------|---------|-------|--------|
| Lexer | Syntax/Lexer.codex | 485 | Done |
| Parser | Syntax/Parser*.codex | ~1,200 | Done |
| AST | Ast/AstNodes.codex, Desugarer.codex | ~310 | Done |
| Name resolver | Semantics/NameResolver.codex | 295 | Done |
| Type checker | Types/TypeChecker*.codex, Unifier.codex | ~1,000 | Done |
| IR lowering | IR/Lowering*.codex, IRModule.codex | ~680 | Done |
| C# emitter | Emit/CSharpEmitter*.codex | ~860 | Done (retiring) |
| Codex emitter | Emit/CodexEmitter.codex | 500 | Done |
| Prelude | Collections, CCE, Hamt, etc. | ~820 | Done |
| Main | main.codex | 151 | Done |

**Total self-hosted front end: ~6,300 lines of Codex.**

### In C# (reference compiler, the part that needs porting)

| Component | File(s) | Lines | What it does |
|-----------|---------|-------|-------------|
| x86-64 codegen | X86_64CodeGen.cs | 6,075 | Expression emission, builtins, escape copy, boot, ISRs, syscalls, process mgmt |
| Instruction encoder | X86_64Encoder.cs | 584 | REX/ModRM/SIB encoding, 60+ instruction types |
| ELF writer (64-bit) | ElfWriterX86_64.cs | 220 | Linux user-mode ELF generation |
| ELF writer (32-bit) | ElfWriter32.cs | 113 | Bare-metal ELF with PVH note |
| IR module | IRModule.cs | 119 | IR data structures (shared) |
| IR lowering | Lowering.cs | 763 | AST → IR (shared, already ported) |

**Total to port: ~7,000 lines of C#**, of which the codegen is 6,075.

### What We Are NOT Porting

- C# emitter (replaced by Codex emitter — already done)
- JavaScript, Python, Rust, C++, Go, Java, Ada, Fortran, COBOL emitters
- IL emitter
- RISC-V backend (abandoned)
- ARM64 backend (abandoned)
- LSP server
- Repository protocol
- WASM backend

Those stay in the C# tree as historical artifacts. The Codex compiler needs
one backend: **x86-64 bare metal**. That's the only target that matters for
self-sustainability. Everything else can be rebuilt later in Codex if needed.

---

## The Architecture

The self-hosted compiler currently has two emitters:

```
IR → CSharpEmitter   → .cs text    (legacy, being retired)
IR → CodexEmitter    → .codex text (identity backend, pingpong)
```

We add a third:

```
IR → X86_64Emitter   → bare-metal ELF binary
```

This is a new .codex file (or set of files) that takes the IR module and
produces a byte sequence — the raw binary. The byte sequence is written to
a file via `write-file`. The result is a bootable ELF.

### Output Strategy

The bare-metal compiler currently writes Codex text to serial. The new
compiler writes **a binary ELF to a file** (in user-mode) or **binary to
serial** (on bare metal, for the Stage 1→2 chain). But the simplest first
milestone is user-mode: the Codex compiler runs on .NET (via the C# emitter
of itself), reads .codex source, and writes a bare-metal ELF file. Then we
test that ELF in QEMU.

Once that works, we use the C# reference compiler one final time to build
the bare-metal ELF of the Codex compiler (with its new x86-64 backend).
That ELF is Stage 0. From there, the chain is self-sustaining.

---

## The Porting Plan

### Phase 1: Instruction Encoder (~600 lines)

Port `X86_64Encoder.cs` to `Emit/X86_64Encoder.codex`.

This is pure functions: instruction mnemonics → byte sequences. No state,
no side effects, no platform dependencies. Every function takes register
IDs and immediates, returns a `List Integer` (byte list).

```codex
mov-rr : Integer -> Integer -> List Integer
mov-rr (dst) (src) = ...  -- REX + 0x89 + ModRM

add-ri : Integer -> Integer -> List Integer
add-ri (dst) (imm) = ...  -- REX + 0x81 + ModRM + imm32
```

**Why first**: Everything else depends on it. It's self-contained. It's the
easiest to test — encode an instruction, check the bytes against the C#
output. Binary-identical output is the acceptance criterion.

**Test strategy**: Encode every instruction type, compare byte-for-byte
with C# encoder output. Can be tested in user-mode without QEMU.

### Phase 2: ELF Writer (~350 lines)

Port `ElfWriterX86_64.cs` and `ElfWriter32.cs` to `Emit/ElfWriter.codex`.

Also pure functions: takes a text section (byte list) and rodata section
(byte list), produces a complete ELF binary (byte list). Headers, program
headers, section layout, page alignment.

```codex
build-elf-bare : List Integer -> List Integer -> List Integer
build-elf-bare (text) (rodata) =
  let header = elf32-header (list-length text) (list-length rodata)
      phdr-load = program-header-load ...
      phdr-pvh  = program-header-pvh ...
  in header ++ phdr-load ++ phdr-pvh ++ text ++ rodata
```

**Test strategy**: Build an ELF from hand-assembled bytes, boot it in QEMU,
verify serial output.

### Phase 3: Core Codegen — Expressions (~2,000 lines)

Port the expression emission core of `X86_64CodeGen.cs`:

- `EmitExpr` dispatch (literals, names, binary ops, if/else, let, apply)
- `EmitRecord`, `EmitFieldAccess`, `EmitList`, `EmitMatch`
- Register allocation (temp + local registers, spill to stack)
- Function prologue/epilogue (callee-save, frame setup)
- Call convention (args in registers, return in RAX)

This is the heart. It transforms IR nodes into instruction sequences using
the Phase 1 encoder. The register allocator is simple (monotonic locals,
recycled temps) and translates directly to Codex.

Split across files for sanity:

| File | Scope | Est. lines |
|------|-------|-----------|
| `Emit/X86_64.codex` | Module entry, function emission, register state | ~400 |
| `Emit/X86_64Expr.codex` | Expression dispatch, literals, binops, if/let | ~600 |
| `Emit/X86_64Data.codex` | Records, lists, field access, match, sum types | ~500 |
| `Emit/X86_64Apply.codex` | Function calls, closures, TCO | ~500 |

**First milestone**: `main = 42` compiles to a bare-metal ELF that boots
and prints `42` to serial. This proves the full chain: Codex IR → Codex
encoder → Codex ELF writer → working binary.

### Phase 4: Runtime Helpers (~1,300 lines) — DONE (16 of 22)

16 runtime helpers ported to `Emit/X86_64Helpers.codex`:

| Category | Helpers | Status |
|----------|---------|--------|
| String | `__str_concat`, `__str_eq`, `__itoa`, `__str_replace`, `__text_contains`, `__text_starts_with`, `__text_compare` | Done |
| List | `__list_snoc`, `__list_insert_at`, `__list_contains`, `__list_cons`, `__list_append` | Done |
| Text/List | `__text_concat_list`, `__text_split` | Done |
| Math | `__ipow`, `__text_to_int` | Done |
| I/O | `__read_file`, `__read_line`, `__bare_metal_read_serial` | **Deferred** — need rodata fixups, syscalls, CCE tables |
| CCE | `__cce_to_unicode`, `__unicode_to_cce` | **Deferred** — need rodata table support |

The 5 deferred helpers require rodata fixup infrastructure (patching
absolute addresses into `mov r64, imm64` instructions at link time)
and CCE/Unicode conversion tables in the rodata section. These will
land as part of Phase 5 (builtins) or Phase 7 (boot), which already
need rodata support for string literals and the boot trampoline.

Pingpong green: 548KB ELF, 109MB Stage 1 HWM, fixed point at 213K.

### Phase 5: Builtins (~800 lines)

Port ~30 pure-CCE builtin operations. Each builtin maps a Codex
operation to an inline instruction sequence or a call to a runtime helper.

CCE boundary principle: everything inside the compiler operates on CCE
natively. Unicode conversion happens only at I/O boundaries (Phase 7).
No builtin in this phase needs CCE↔Unicode tables or rodata fixups.

Two categories:
- **Inline** (1-5 instructions): `text-length`, `list-length`, `list-at`,
  `negate`, `char-at`, `char-code-at`, `is-digit`, `is-letter`, etc.
- **Helper-calling** (move args + call): `text-replace` → `__str_replace`,
  `list-cons` → `__list_cons`, `integer-to-text` → `__itoa`, etc.

I/O builtins (`print-line`, `read-file`, `write-file`) are deferred to
Phase 7 where the CCE↔Unicode tables and serial/file I/O live.

### Phase 6: Escape Copy & Regions (~600 lines)

Port the two-space GC and forwarding hash table:

- Forwarding table alloc/zero/lookup/insert
- Per-type escape helpers (record, list, sum, text)
- Region entry/exit (save mark, copy, restore)
- Result-space base tracking

This is the most intricate code in the backend — pointer arithmetic,
hash table operations, type-dispatched deep copy. Needs careful testing.

### Phase 7: Boot + I/O Boundary (~700 lines)

Port the boot sequence and the I/O boundary (CCE↔Unicode gates):

Boot sequence:
- Multiboot header
- 32→64 bit trampoline (page tables, GDT, mode switch)
- Stack/heap setup
- IDT construction, PIC init, interrupt handlers
- Serial I/O (COM1 115200 8N1)
- Syscall setup (MSRs, handler)
- Process table, capability bits

I/O boundary (barbarians at the gates):
- CCE→Unicode and Unicode→CCE lookup tables in rodata (384 bytes)
- Rodata fixup infrastructure (patch absolute addresses at link time)
- 5 deferred runtime helpers: `__read_file`, `__read_line`,
  `__bare_metal_read_serial`, `__cce_to_unicode`, `__unicode_to_cce`
- I/O builtins: `print-line`, `read-file`, `read-line`, `write-file`,
  `file-exists`

Much of the boot sequence is constant byte sequences (the trampoline is
essentially a data blob). The I/O boundary is where CCE meets the outside
world — every byte crosses the encoding gate exactly once.

### Phase 8: Self-Compilation — The Second Fixed Point

With Phases 1–7 complete, the Codex compiler can emit x86-64 bare-metal
binaries. The test:

1. Build the Codex compiler (with x86-64 backend) using the C# ref
   compiler one last time. This produces **Stage 0 ELF**.
2. Boot Stage 0 ELF in QEMU. Feed it the Codex compiler source.
   It emits a bare-metal ELF. This is **Stage 1 ELF**.
3. Boot Stage 1 ELF in QEMU. Feed it the same source.
   It emits **Stage 2 ELF**.
4. **Stage 1 ELF == Stage 2 ELF** → fixed point. The compiler built by
   C# and the compiler built by itself produce identical binaries.

After this, Stage 0 is archived. All future builds use Stage 1 (or its
successors). The C# reference compiler is truly frozen.

---

## Milestone Markers

| Milestone | What | How you know it works |
|-----------|------|----------------------|
| ~~**M1**~~ | ~~Encoder in Codex~~ | ~~Byte-identical output vs C# encoder for all instruction types~~ |
| ~~**M2**~~ | ~~ELF writer in Codex~~ | ~~Minimal ELF boots in QEMU, prints to serial~~ |
| ~~**M3**~~ | ~~`main = 42`~~ | ~~Codex compiler (on .NET) emits bare-metal ELF, boots, prints `42`~~ |
| ~~**M4**~~ | ~~`factorial 5`~~ | ~~Non-trivial program: recursion, arithmetic, print. Also: records, match, lists, TCO, closures~~ |
| ~~**M5**~~ | ~~Runtime helpers~~ | ~~16 of 22 helpers ported. Pingpong green at 213K output, 548KB ELF, 109MB HWM~~ |
| ~~**M5b**~~ | ~~Builtins (30 pure-CCE ops)~~ | ~~Done — 30 builtins wired, pingpong green at 566KB ELF, 113MB HWM~~ |
| **M6** | Escape copy | Region-based heap reclamation working — will shrink HWM from 109MB |
| **M7** | Self-compilation | The compiler compiles itself to a bare-metal ELF |
| **M8** | Fixed point | Stage 1 ELF == Stage 2 ELF. **This is MM4.** |

---

## What Changes in the .codex Source

The self-hosted compiler gains a new backend selector. Today:

```codex
emit-module : IRModule -> EmitTarget -> Text
```

After:

```codex
emit-module : IRModule -> EmitTarget -> Text    -- for Codex/C# text output
emit-binary : IRModule -> EmitTarget -> List Integer  -- for native binary output
```

Or a unified type:

```codex
type CompilerOutput = TextOutput Text | BinaryOutput (List Integer)
```

The `main.codex` pipeline gains a `--target x86-64-bare` path that calls
`emit-binary` instead of `emit-module`, then writes the byte list to a file.

### New Files

| File | Est. lines | Phase |
|------|-----------|-------|
| `Emit/X86_64Encoder.codex` | ~600 | 1 |
| `Emit/ElfWriter.codex` | ~350 | 2 |
| `Emit/X86_64.codex` | ~400 | 3 |
| `Emit/X86_64Expr.codex` | ~600 | 3 |
| `Emit/X86_64Data.codex` | ~500 | 3 |
| `Emit/X86_64Apply.codex` | ~500 | 3 |
| `Emit/X86_64Helpers.codex` | ~800 | 4 |
| `Emit/X86_64Builtins.codex` | ~800 | 5 |
| `Emit/X86_64Escape.codex` | ~600 | 6 |
| `Emit/X86_64Boot.codex` | ~500 | 7 |
| **Total** | **~5,650** | |

The C# backend is ~7,000 lines. The Codex port is estimated at ~5,650 —
smaller because Codex is more expressive (pattern matching, algebraic types)
and because we're not porting the Linux user-mode path, only bare metal.

---

## Sequencing and Dependencies

```
Phase 1 (Encoder)          ──→ Phase 2 (ELF Writer)
                                    ↓
                               Phase 3 (Core Codegen)  ←── M3: main = 42
                                    ↓
                    ┌───────────────┼───────────────┐
                    ↓               ↓               ↓
             Phase 4 (Helpers) Phase 5 (Builtins) Phase 6 (Escape)
                    └───────────────┼───────────────┘
                                    ↓
                               Phase 7 (Boot)
                                    ↓
                               Phase 8 (Self-compile)  ←── MM4
```

Phases 4, 5, and 6 are independent after Phase 3 and can be worked in
parallel. Phase 7 (boot) is mostly constant data and can start alongside
Phase 4–6 but must complete before Phase 8.

**Critical path**: 1 → 2 → 3 → (4+5+6 parallel) → 7 → 8

---

## Risk

**The big one**: the self-hosted compiler currently runs on .NET (via C#
emitter output compiled by `dotnet`). The native backend will be tested
by running the compiler on .NET and checking that its x86-64 output boots
correctly. This means .NET is still in the development loop during Phases
1–7 — but it's in the *testing* loop, not the *output* loop. The output
is pure Codex → native binary. Once Phase 8 achieves fixed point, .NET
leaves the loop entirely.

**Performance risk**: The byte-list representation (`List Integer`) for
binary output may be slow for large binaries. The self-hosted compiler's
ELF will be ~300 KB. Building a 300 KB byte list via `list-snoc` is O(n)
per append if we're careful about linear ownership, but the constant factor
matters. If this becomes a bottleneck, we may need an `Array` type with
O(1) indexed write — which connects to the safe-mutation design
(`docs/Designs/Features/SAFE-MUTATION.md`).

**Memory risk**: At 55 MB HWM for front-end self-compilation (Codex→Codex),
adding a native backend that builds byte lists and encodes instructions will
increase pressure. The 64 MB bare-metal budget is tight. Phase 6 (escape
copy) is essential for keeping memory bounded.

---

## What This Retires

After MM4, the following are genuinely archival:

- `src/Codex.Emit.X86_64/` — replaced by `Codex.Codex/Emit/X86_64*.codex`
- `src/Codex.Emit.CSharp/` — replaced by `Codex.Codex/Emit/CodexEmitter.codex`
- `src/Codex.Emit.RiscV/` — abandoned (per CurrentPlan)
- `src/Codex.Emit.Arm64/` — abandoned (per CurrentPlan)
- `src/Codex.Emit.IL/` — no Codex replacement needed yet
- All transpilation backends (JS, Python, Rust, etc.) — barbarian land

The reference compiler lock becomes permanent. The `src/` tree becomes a
museum. The only compiler is the one written in Codex, compiling itself on
bare metal, producing bare-metal binaries.

The cord is cut.

---

## Connection to the Gap Analysis

The Second Bootstrap is prerequisite to almost every post-MM3 feature:

| Feature | Why it needs native Codex |
|---------|--------------------------|
| **Verifier** | Must be written in Codex, run on Codex.OS. Can't depend on .NET. |
| **Agent protocol** | Message handlers run on bare metal. Need native compilation. |
| **Policy compiler** | Prose → capability constraints, compiled by Codex. |
| **Forensics** | Chain construction on-device, no cloud dependency. |
| **Crypto primitives** | Must run on bare metal. No System.Security.Cryptography. |
| **Networking stack** | Written in Codex, compiled to bare metal. |
| **Shell** | The Codex.OS user interface. Written in Codex. |

Every one of these needs a compiler that produces native binaries without
C# in the chain. MM4 unblocks all of them.

---

*"The C# bootstrap was the cradle. The cradle did its job. Time to stand."*
