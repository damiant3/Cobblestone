# WASM Backend — Design Document

**Date**: 2026-03-21
**Author**: Copilot (VS 2022, Windows)
**Status**: Draft — for review

---

## Context

Camp II-A (IL Backend Maturity) is summited. Every Codex IR node emits to
working IL: sum types, records, pattern matching with user-defined constructor
dispatch, generic instantiation, effect handlers, tail calls, lists, and all
builtins. The agent toolkit runs as a standalone `.exe` compiled from `.codex`
source with no C# intermediate step.

The next pitch is Camp II-B: backends that emit code for targets beyond the
CLR. WASM is first because:

1. The IL emitter's **stack machine model maps almost 1:1** to WASM bytecode.
2. WASM runs everywhere — browser, WASI runtimes, edge, embedded.
3. No platform-specific codegen. One target, many hosts.
4. Small spec, well-defined, no undefined behavior.
5. It leaves a rope anchor for climbers behind us — anyone can run a `.wasm`
   file without installing .NET.

WASM alone does not summit Peak II. It still runs on someone else's runtime
(Wasmtime, V8, etc.). The native codegen push (Cranelift or direct) comes
after. But WASM proves the lowering pipeline works for non-CLR targets and
ships immediate value.

---

## Architecture

### New Project: `Codex.Emit.Wasm`

```
src/Codex.Emit.Wasm/
├── Codex.Emit.Wasm.csproj    (net8.0, refs Codex.Emit + Codex.IR + Codex.Types)
├── WasmEmitter.cs             (IAssemblyEmitter implementation)
├── WasmModuleBuilder.cs       (binary WASM module writer)
└── WasmModuleBuilder.Types.cs (sum type / record → WASM struct/tagged representation)
```

Follows the same pattern as `Codex.Emit.IL`:
- `WasmEmitter : IAssemblyEmitter` — entry point, `EmitAssembly` returns `byte[]` (`.wasm`)
- `WasmModuleBuilder` — walks `IRModule`, emits WASM binary format sections

### Dependency Flow

```
Codex.IR → Codex.Emit → Codex.Emit.Wasm
                       → Codex.Emit.IL  (existing)
                       → Codex.Emit.CSharp (existing)
                       → ...
```

No new external dependencies. The WASM binary format is simple enough to emit
directly (like we do with PE/IL via `System.Reflection.Metadata`). No need for
a WASM SDK or toolchain.

---

## WASM Binary Format — What We Emit

A `.wasm` file is a sequence of numbered sections. We need:

| Section | ID | Purpose |
|---------|----|---------|
| Type    | 1  | Function signatures (`(param i64 i64) (result i64)`) |
| Import  | 2  | WASI imports (`fd_write`, `proc_exit`, etc.) |
| Function| 3  | Maps function index → type index |
| Memory  | 5  | Linear memory declaration (for strings, heap) |
| Export  | 7  | `_start` entry point + memory |
| Code    | 10 | Function bodies (WASM bytecodes) |
| Data    | 11 | String literals, static data |

We emit raw bytes using `BinaryWriter` on a `MemoryStream`. No third-party
libraries.

---

## Type Mapping

| Codex Type | WASM Representation |
|-----------|---------------------|
| `Integer` | `i64` |
| `Number`  | `f64` |
| `Boolean` | `i32` (0 or 1) |
| `Text`    | `i32` (pointer to length-prefixed UTF-8 in linear memory) |
| `Nothing` | void (no result) |
| `List a`  | `i32` (pointer to cons-cell chain in linear memory) |
| Record    | `i32` (pointer to struct layout in linear memory) |
| Sum type  | `i32` (pointer to tagged struct: `i32` tag + fields) |

All heap-allocated values live in WASM linear memory. We manage a simple
bump allocator in the runtime preamble (a few WASM functions emitted at
the start of every module).

### Sum Type Layout (Tagged Representation)

For a sum type like:
```
Shape =
  | Circle (Integer)
  | Rect (Integer) (Integer)
```

Memory layout:
```
Circle: [tag=0 : i32] [r : i64]        → 12 bytes
Rect:   [tag=1 : i32] [w : i64] [h : i64] → 20 bytes
```

Pattern match dispatch becomes:
```wasm
(local.get $scrutinee)
(i32.load)           ;; load tag
(i32.const 0)        ;; Circle tag
(i32.eq)
(if (result i64)
  (then ...)         ;; Circle branch: load field at offset 4
  (else ...)         ;; next branch
)
```

This replaces the IL emitter's `isinst` approach. Same semantics, different
mechanism — tag comparison instead of runtime type checking.

---

## IR → WASM Translation

The IR is a tree of `IRExpr` nodes. The WASM emitter walks the tree and
emits stack-machine bytecodes, exactly like the IL emitter. Key mappings:

| IR Node | WASM Bytecodes |
|---------|---------------|
| `IRIntegerLit(n)` | `i64.const n` |
| `IRNumberLit(n)` | `f64.const n` |
| `IRBoolLit(b)` | `i32.const 0/1` |
| `IRTextLit(s)` | `i32.const <data_offset>` (string in data section) |
| `IRBinary(AddInt, l, r)` | `<emit l> <emit r> i64.add` |
| `IRBinary(Eq, l, r)` | `<emit l> <emit r> i64.eq` (or type-appropriate) |
| `IRIf(c, t, e)` | `<emit c> if <result_type> <emit t> else <emit e> end` |
| `IRLet(name, val, body)` | `<emit val> local.set $name <emit body>` |
| `IRName(n)` | `local.get $n` or `call $n` |
| `IRApply(f, arg)` | `<emit args> call $f` |
| `IRMatch` | Tag-dispatch via `if`/`else` chains or `br_table` |
| `IRRecord` | Bump-allocate + store fields |
| `IRFieldAccess` | `i32.load` at computed offset |
| `IRList` | Cons-cell chain in linear memory |

### Function Calls

All Codex functions become WASM functions. Curried multi-arg functions are
flattened (the IR lowering already collects args from nested `IRApply` chains,
same as the IL emitter does).

### Builtins

| Builtin | WASM Implementation |
|---------|-------------------|
| `print-line` | Call WASI `fd_write` on stdout (fd 1) |
| `show` | Integer/bool → format in linear memory, then return pointer |
| `read-line` | WASI `fd_read` on stdin (fd 0) |
| `text-length` | Load length prefix from string pointer |
| `++` (append) | Allocate new string, memcpy both halves |
| `read-file` | WASI `path_open` + `fd_read` |
| `write-file` | WASI `path_open` + `fd_write` |

### Memory Management

Phase 1: **Bump allocator**. A global `$heap_ptr` starts after the data
section. Every allocation bumps it forward. No free. This is sufficient for
short-lived programs (the agent toolkit, test samples, the compiler itself
during a single compilation run).

Phase 2 (Camp III): The linear allocator driven by the type system's
linearity analysis. But that's Peak III territory.

---

## Implementation Plan

### Phase 1: Minimal Viable WASM (Days)

**Goal**: `samples/hello.codex` → `.wasm` → runs under `wasmtime`.

1. Create `Codex.Emit.Wasm` project, wire into solution
2. Emit WASM module skeleton (magic bytes, version, empty sections)
3. Emit type section + function section for a single `main` function
4. Emit `i64.const` + `call $print_line` for integer output
5. Emit WASI import for `fd_write`, export `_start`
6. Emit data section for string literals
7. Test: `hello.codex` prints "Hello, World!" under `wasmtime`

### Phase 2: Core Language (Days)

8. Arithmetic (`IRBinary` → `i64.add/sub/mul/div`, `f64.*`)
9. Boolean ops and comparisons
10. `IRIf` → `if/else/end`
11. `IRLet` → `local.set`/`local.get`
12. Function calls (`IRApply` → `call`)
13. Recursion + tail calls (`return_call` if supported, else loop)
14. String operations (bump-allocate, `text-length`, `++`, `char-at`)
15. Test: `factorial.codex`, `arithmetic.codex`, `fibonacci.codex`

### Phase 3: Types (Days–Week)

16. Record construction → bump-allocate struct, store fields
17. Field access → `i32.load` at offset
18. Sum type construction → tagged struct allocation
19. Pattern match dispatch → tag comparison + field loading
20. List construction → cons-cell chain
21. `map-list`, `fold-list`, `filter-list` as WASM functions
22. Test: `shapes.codex`, `safe-divide.codex`, `expr-calculator.codex`

### Phase 4: Effects & I/O (Week)

23. `do` blocks → sequential WASM instructions
24. `print-line` / `read-line` via WASI
25. `read-file` / `write-file` via WASI
26. Effect handlers (inline, same approach as IL emitter)
27. `run-state` handler
28. Test: `effects-demo.codex`, agent toolkit subset

### Phase 5: CLI Integration

29. Add `--target wasm` flag to `Codex.Cli`
30. `codex build hello.codex --target wasm` → `hello.wasm`
31. Update THE-ASCENT.md

---

## Testing Strategy

Mirror the IL emitter test structure:

```
tests/Codex.Types.Tests/
├── WasmEmitterTests.cs          (binary format validation)
├── WasmEmitterIntegrationTests.cs (compile + run under wasmtime)
```

Integration tests use `wasmtime` (if available) via `Process.Start`, same
pattern as the IL emitter tests use `dotnet` to run compiled DLLs. Tests
skip gracefully if `wasmtime` is not on PATH.

---

## What This Doesn't Do

- **No native machine code.** WASM is a portable bytecode, not native. The
  Cranelift push (Camp II-B phase 2) handles native.
- **No garbage collection.** Bump allocator only. GC is Peak III.
- **No WASM GC proposal.** We use linear memory and manual layout. This keeps
  us independent of evolving WASM proposals.
- **No WASM component model.** We emit core WASM modules. Components can come
  later if needed.

---

## Why This Matters

Every `.wasm` file we ship is a rope anchor. Someone following behind us
doesn't need .NET installed. They don't need Visual Studio. They need
`wasmtime` (or a browser). That's the courage provision — the path up
gets easier for everyone behind us.

And for us: this is the first time the Codex compiler emits code that runs
outside the CLR. It proves the IR and lowering pipeline are truly
target-independent. Every backend after this one is easier.
