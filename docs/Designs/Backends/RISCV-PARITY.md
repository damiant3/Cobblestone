# RISC-V Feature Parity — Design & Handoff

**Date**: 2026-03-22
**Author**: Cam (Claude Code CLI, Opus 4.6, 1M context)
**Status**: Planned — ready for next session

---

## Handoff Prompt

> Yeah we need a home bro. First lets give NASA a gift and give them full
> fidelity on RISC-V. Your context is way huger. I didn't know you were
> different than the other Opus. We have to readjust the entire plan here.
> You're blasting stuff out that would take me hours and hours of difficulty.
> So yeah, I want you to review it all. Maybe plan a full audit and then
> begin. Maybe a new session for that audit to optimize. I am thinking how
> to best use your ability.

---

## The Gap

The WASM backend is feature-rich. The RISC-V backend is a proof of concept.
Both target bare metal. Only one can actually compile real programs.

| Feature | WASM | RISC-V |
|---------|------|--------|
| Primitives (int, bool, text literals) | ✅ | ✅ |
| Arithmetic, comparison, logical ops | ✅ | ✅ |
| If/else | ✅ | ✅ |
| Let bindings | ✅ | ✅ |
| Function calls + recursion | ✅ | ✅ |
| Do expressions | ✅ | ✅ |
| Negate | ✅ | ✅ |
| **Records (creation + field access)** | ✅ | ❌ |
| **Sum types (tagged unions)** | ✅ | ❌ |
| **Pattern matching (all forms)** | ✅ | ❌ |
| **Text builtins (length, char-at, etc.)** | ✅ (10+) | ❌ |
| **Text concatenation** | ✅ | ❌ |
| **String equality** | ✅ | ❌ |
| **Lists** | ❌ | ❌ |
| **Regions (linear allocator)** | ✅ | Passthrough |
| Print-line (int/bool/text) | ✅ | ✅ |
| Other builtins | 22 total | 1 total |

---

## The Mission

Bring RISC-V to feature parity with WASM. The WASM emitter is the
blueprint — it already solved the representation problems. The port is
mechanical: same concepts, different instruction set.

---

## Architecture Notes

### RISC-V Emitter (1,042 lines, 4 files)

```
RiscVEmitter.cs (23 lines)     — entry point, dual target (Linux/BareMetal)
RiscVCodeGen.cs (612 lines)    — main compiler, IR → machine code
RiscVEncoder.cs (249 lines)    — instruction encoding (R/I/S/B/U/J formats)
ElfWriter.cs (158 lines)       — ELF64 generation, flat binary for bare metal
```

**Register allocation**: Round-robin across temporaries (t0-t6) and callee-saved
(s2-s11). Locals use callee-saved regs. No spill handling — assumes ≤20 live
values. This will need attention for records/sum types.

**Stack frame**: 112 bytes. ra at sp+104, fp at sp+96, s1-s11 below. All callee-
saved regs preserved on entry/exit.

**I/O**: Linux uses `write(2)` syscall via ecall. Bare metal writes to UART at
0x10000000 (QEMU virt NS16550A THR).

**Text format**: Length-prefixed (8-byte i64 length + UTF-8 bytes). Same as WASM.

### WASM Emitter (2,357 lines, 5 files) — The Blueprint

```
WasmModuleBuilder.cs (183 lines)          — module state, type/function management
WasmModuleBuilder.Emit.cs (860 lines)     — IR → WASM bytecode
WasmModuleBuilder.Builtins.cs (522 lines) — builtin function emission
WasmModuleBuilder.Runtime.cs (777 lines)  — runtime helpers, string ops, memory
```

**Memory model**: Bump allocator with region stack. Heap starts at 1024 +
data size. Region enter pushes heap ptr, exit restores it (bulk free).

**Records**: Heap-allocated structs. Constructor bumps heap ptr by total field
size, stores fields sequentially. Field access is `i32.load` at base + offset.

**Sum types**: Tagged unions. Tag (i32) at offset 0, fields after. Pattern match
dispatches on tag value via if-chain. Constructor stores tag + fields.

**Pattern matching**: Recursive. Literals → equality check. Wildcards → always
match. Variables → bind to local. Constructors → check tag, extract fields.

---

## Implementation Plan

### Phase 1: Heap Allocator

RISC-V needs a heap. The WASM approach works: bump allocator with a global
heap pointer. On RISC-V, use a register (s1 or a dedicated global) as the
heap pointer. `malloc(n)` = current ptr, then advance ptr by n.

For bare metal: heap starts after rodata. For Linux: use `brk(2)` syscall
to grow the data segment, or just start at a fixed high address.

**Key decisions**:
- Heap pointer in a callee-saved register (s1) — always available
- Alignment: 8-byte aligned (natural for i64 fields)
- No free (bump only, matches WASM model)

### Phase 2: Records

Port from WASM. A record is a heap-allocated block:
```
[field0: 8 bytes][field1: 8 bytes][field2: 8 bytes]...
```

- **Constructor**: Bump heap ptr by (field_count * 8). Store each field
  value at base + (index * 8). Return base pointer.
- **Field access**: Load from base + (field_index * 8).
- **Type tracking**: Need a map from record type name → field list with
  offsets. Mirror what WASM does with `m_typeFields`.

Estimated: ~80 lines of new code in RiscVCodeGen.

### Phase 3: Sum Types (Tagged Unions)

Port from WASM. A sum type value is:
```
[tag: 8 bytes][field0: 8 bytes][field1: 8 bytes]...
```

- **Constructor**: Bump heap ptr by (1 + field_count) * 8. Store tag at
  offset 0, fields after. Tag is an integer identifying the constructor.
- **Tag check**: Load 8 bytes at base, compare to expected tag.

Estimated: ~60 lines.

### Phase 4: Pattern Matching

Port from WASM. The IR has `IRMatch` with branches. Each branch has a
pattern (literal, wildcard, variable, constructor) and a body.

- **Literal match**: Compare value to literal, branch if not equal.
- **Wildcard**: Always matches, skip check.
- **Variable**: Bind value to local (callee-saved reg), continue.
- **Constructor match**: Load tag, compare, branch. Extract fields to locals.

This is the most complex phase — needs careful register allocation since
pattern bodies may need many live values.

Estimated: ~120 lines.

### Phase 5: Text Builtins

Port the essential text builtins from WASM:
- `text-length`: Load 8 bytes at ptr (the length prefix). Trivial.
- `char-at`: Load length-prefix, bounds check, load byte at ptr + 8 + index.
- `substring`: Allocate new string, copy bytes.
- `text-to-integer`: Parse decimal from bytes.
- `integer-to-text`: Already exists (itoa in print-line).
- `text-contains`, `text-starts-with`: Byte-by-byte comparison loops.
- String equality: Compare lengths, then byte-by-byte.
- Text concatenation: Allocate new string, copy both halves.

Estimated: ~200 lines (most complexity is in the byte loops).

### Phase 6: Additional Builtins

- `get-args`: Linux only — read from stack at _start (argc, argv).
- `read-file`, `write-file`, `file-exists`: Linux syscalls (open, read, write, close, stat).
- `run-process`: Linux fork+exec.
- `current-dir`, `get-env`: Linux syscalls.

These are Linux-only — bare metal doesn't have a filesystem.

Estimated: ~150 lines.

### Phase 7: Region-Based Allocation

Currently passthrough. Port the WASM region model:
- Region enter: Push heap ptr to region stack.
- Region exit: Pop heap ptr (bulk free).
- Text escape: Copy text to parent region before exit.

Estimated: ~40 lines.

---

## Test Plan

Mirror the WASM test suite structure:
- Record creation and field access
- Sum type construction and tag dispatch
- Pattern matching (all forms)
- Text builtins (each one individually)
- String equality and concatenation
- Combined: programs using records + pattern matching + text
- Execution tests under `qemu-riscv64` (Linux) and `qemu-system-riscv64` (bare metal)

---

## Why This Matters

From THE-ASCENT.md:

> A Codex program can already run with zero OS, zero runtime, zero libc on
> RISC-V hardware. The path from here to Codex.OS is not "build an OS from
> scratch" — it's "extend what we already have until it manages resources
> for multiple programs."

But right now, "what we already have" on RISC-V can only print integers and
booleans. You can't define a record, match on a sum type, or concatenate
strings. The WASM backend can. Parity means the bare metal path is real —
not just a demo, but a foundation you can actually build an OS on.

NASA doesn't want a demo. They want software that can't segfault with a
knife in its hand — or with a spacecraft under its control.

---

## Session Strategy

**Cam's advantage**: 1M context window. Can hold the entire WASM emitter
(2,357 lines), the entire RISC-V emitter (1,042 lines), the IR definitions,
and the test suites simultaneously. The other agents (60K context) had to
work one file at a time — that's why RISC-V stayed minimal.

**Recommended approach**: Load both emitters, port feature by feature from
WASM to RISC-V, test each phase before moving to the next. One session
should cover Phases 1-4 (heap, records, sum types, pattern matching).
A second session for Phases 5-7 (text builtins, system builtins, regions).

**Reference compiler lock**: All changes are in `src/Codex.Emit.RiscV/`
(new backend code) and `tests/` (new tests). No changes to parser, type
checker, IR, or any other emitter. Lock override justified: extending an
existing backend, all additive.

---

## Files to Modify

- `src/Codex.Emit.RiscV/RiscVCodeGen.cs` — main target, all new emission code
- `src/Codex.Emit.RiscV/RiscVEncoder.cs` — may need new helpers (memory ops)
- `tests/Codex.Types.Tests/RiscVEmitterTests.cs` — new test cases
- Possibly: `src/Codex.Emit.RiscV/RiscVCodeGen.Records.cs` (partial class split if it gets large)

---

## The View From Here

Day 10. 337+ commits. The prose layer is done — all 6 CPL forms parse.
The IL backend compiles the agent toolkit. The WASM backend handles
structured data. The RISC-V backend proved bare metal works.

Now we make it real. Records on registers. Pattern matching on branch
instructions. Tagged unions in memory-mapped I/O. The same Codex source,
the same type system, the same proofs — but running on iron.

We're going up.
