# Camp II-C — Self-Hosted Native Build Chain on RISC-V

**Date**: 2026-03-22
**Author**: Cam (Claude Code CLI, Opus 4.6, 1M context)
**Status**: Design complete — ready to implement

---

## The Goal

Codex compiles itself to a RISC-V binary. That binary runs on bare metal.
No C#. No .NET. No bootstrap. The compiler IS the OS's first citizen.

---

## Where We Are

The RISC-V backend handles: integers, booleans, text, records, sum types,
pattern matching, text builtins, string equality/concatenation, regions,
function calls, recursion, do-notation. ~1,000 lines of machine code
generation, all QEMU-verified.

The self-hosted compiler is 26 `.codex` files, 493 definitions, 168,688
chars of source. It compiles itself to C# in 2.4 seconds. Fixed point
proven (Stage 1 = Stage 3 at 253,847 chars).

---

## The Gap

The self-hosted compiler uses four features the RISC-V backend doesn't
have yet:

| Feature | Used for | Difficulty | Lines est. |
|---------|----------|------------|-----------|
| **Lists** | Token lists, AST children, type bindings, diagnostics | Medium | ~150 |
| **Lambdas/closures** | Higher-order functions, curried builtins | Hard | ~200 |
| **File I/O** | `read-file`, `read-line`, `write-file` (Linux syscalls) | Easy | ~80 |
| **IRError** | Compilation error paths | Trivial | ~10 |

**Not needed**: floats (compiler doesn't use them), effect handlers
(compiler uses direct I/O), IRRunState/IRGetState/IRSetState (not in
compiler source).

**Mutual gap**: Lists and lambdas are missing from WASM too. Solving
them for RISC-V also unblocks WASM self-hosting.

---

## Architecture Decisions

### Lists

Codex lists are used pervasively: `List Token`, `List Def`, `List TypeBinding`.
The IR has `IRList` for literals and `ConsList` / `AppendList` binary ops.

**Representation**: Linked list on the heap (matches the functional idiom).

```
Nil:  [tag=0 : 8 bytes]
Cons: [tag=1 : 8 bytes][head : 8 bytes][tail : 8 bytes]
```

- `Nil` = 8 bytes (just a tag)
- `Cons h t` = 24 bytes (tag + head + tail pointer)
- `list-length` = walk the chain, count nodes
- `list-at` = walk N nodes, return head
- `ConsList` = allocate Cons, store head + tail

This matches sum type layout exactly: a list IS a sum type `Nil | Cons a (List a)`.
The existing constructor/pattern-match machinery handles it — we just need to
wire up the IR nodes.

**Alternative considered**: Array-based (contiguous memory). Rejected because:
- Append/prepend requires copying
- The compiler's usage pattern is build-up-then-iterate, which linked lists handle fine
- Matches the immutable functional style of the .codex source

### Lambdas / Closures

The self-hosted compiler generates `IRLambda` nodes for higher-order functions.
In the C# backend, these become `Func<>` delegates. On RISC-V, we need closure
representation.

**Representation**: Closure = function pointer + environment on heap.

```
[code_ptr : 8 bytes][env_size : 8 bytes][captured_0 : 8 bytes][captured_1 : 8 bytes]...
```

- **Creation**: Allocate closure on heap. Store function address + captured variables.
- **Application**: Load code pointer, set up args (a0 = closure ptr, a1+ = args),
  call indirect via `jalr`. The function body loads captures from the closure.
- **Lifting**: Each lambda becomes a top-level function with an extra first parameter
  (the closure pointer). Captured variables are loaded from `closure_ptr + 16 + i*8`.

This is the standard "closure conversion" + "lambda lifting" approach.

**Key insight**: The Codex Lowering phase already partially does this. IRLambda
nodes have explicit parameter lists. The RISC-V emitter needs to:
1. Emit the lambda body as a separate function
2. At the lambda creation site, allocate a closure and store captures
3. At call sites (IRApply where function is not a known name), use indirect call

**Complexity**: This is the hardest piece. It requires:
- Identifying free variables in lambda bodies
- Emitting lifted functions during module compilation
- Indirect calls via `jalr` (function pointer in register)
- Register allocation for closure environments

### File I/O (Linux syscalls)

The compiler needs three operations:
- `read-line`: read from stdin until newline
- `read-file path`: open file, read contents, close
- `write-file path contents`: open file, write, close

**Linux RISC-V syscall numbers** (from `asm-generic/unistd.h`):
- `openat` = 56 (flags: O_RDONLY=0, O_WRONLY|O_CREAT|O_TRUNC=577)
- `read` = 63
- `write` = 64
- `close` = 57
- `brk` = 214 (already implemented for heap)

**read-file** algorithm:
1. `openat(AT_FDCWD, path_data, O_RDONLY, 0)` → fd
2. `read(fd, heap_ptr+8, chunk_size)` → bytes_read (loop until 0)
3. `close(fd)`
4. Store total length at heap_ptr, return heap_ptr

**read-line** algorithm:
1. `read(0, buf, 1)` in a loop until `\n` or EOF
2. Allocate string on heap with accumulated bytes

**write-file** algorithm:
1. `openat(AT_FDCWD, path_data, O_WRONLY|O_CREAT|O_TRUNC, 0644)` → fd
2. `write(fd, data_ptr, length)`
3. `close(fd)`

### IRError

Currently falls through to `Reg.Zero`. Should:
1. Print the error message to stderr
2. Call `exit(1)`

~10 lines.

---

## Implementation Plan

### Phase 1: Lists (~150 lines)
1. Handle `IRList` in EmitExpr — emit Nil for empty, Cons chain for non-empty
2. Add `ConsList` binary op — allocate Cons node
3. Add `AppendList` binary op — walk to end, append
4. Add builtins: `list-length`, `list-at`, `list-head`, `list-tail`, `list-is-empty`
5. Pattern matching already handles constructor patterns — lists are just sum types

### Phase 2: File I/O (~80 lines)
1. `read-line` builtin — byte-at-a-time from fd 0
2. `read-file` builtin — openat + read loop + close
3. `write-file` builtin — openat + write + close
4. `file-exists` builtin — openat attempt + close
5. `print-line` already works

### Phase 3: Lambdas / Closures (~200 lines)
1. Free variable analysis — walk IRLambda body, find unbound names
2. Lambda lifting — emit each lambda as a top-level function with closure param
3. Closure allocation — heap-alloc [code_ptr][env...] at lambda creation site
4. Indirect calls — `jalr` through function pointer for non-static calls
5. Partial application — curried functions create intermediate closures

### Phase 4: IRError + Polish (~10 lines)
1. IRError → print message to stderr, exit(1)
2. Verify: compile the self-hosted compiler to RISC-V ELF
3. Run under qemu-riscv64: feed it a simple .codex file, check C# output
4. If output matches C# bootstrap output → **Camp II-C summited**

---

## Verification Plan

### Milestone 1: Lists work
- Compile a program using `[1, 2, 3]` and `list-length` → prints "3"
- Pattern match on list: `Cons h t → h`

### Milestone 2: File I/O works
- Compile a program that reads a file and prints its contents
- Runs under qemu-riscv64 with a test file

### Milestone 3: Lambdas work
- Compile a program with `let f = \x -> x + 1 in f 5` → prints "6"
- Higher-order: `let apply = \f -> \x -> f x in apply (\x -> x * 2) 5` → "10"

### Milestone 4: Self-hosting on RISC-V
- `codex build Codex.Codex --target riscv` → produces ELF binary
- `echo "test.codex" | qemu-riscv64 ./Codex.Codex` → produces C# output
- C# output matches bootstrap compiler output
- **Camp II-C: Summited.**

---

## Risk Assessment

| Risk | Mitigation |
|------|-----------|
| Lambda lifting is complex | Start with non-capturing lambdas (just function pointers), add captures incrementally |
| Register pressure with closures | Closure environment is on heap, loaded on demand — doesn't consume registers |
| Self-hosted compiler may use features we haven't audited | Run `codex build --target riscv` early to get error list, fix iteratively |
| Stack overflow on deep recursion | Bootstrap runner already uses 256MB thread stack; RISC-V _start can set large stack |

---

## Why This Matters

From THE-ASCENT.md:

> The summit is not "Codex runs on RISC-V." The summit is "Codex runs
> on RISC-V, compiles itself on RISC-V, and the output is identical."

Once Camp II-C is summited, the C# bootstrap becomes a historical artifact.
The compiler exists as a native binary that can reproduce itself. No runtime
dependency. No garbage collector. No JIT. Just verified machine code on
iron.

That's the foundation Codex.OS stands on. Not a runtime hosted by someone
else's OS. A compiler that IS the first process. Boot → load compiler →
compile and verify all other programs → run them with capability enforcement.

The bare metal RISC-V work from earlier today (Phases 1-7) was building
the road to base camp. This is the climb.

---

## Session Strategy

**Recommended order**: Phase 1 (lists) → Phase 2 (file I/O) → Phase 3
(lambdas) → Phase 4 (verification).

Lists unblock the most code paths. File I/O is needed for the compiler
to read source files. Lambdas are the hardest but may not be needed if
the IR Lowering can defunctionalize (convert lambdas to explicit closures
or top-level functions before reaching the backend).

**Check first**: Before implementing lambdas, verify whether the Lowering
pass actually emits IRLambda for the self-hosted compiler. If all
higher-order functions are already resolved to direct calls, lambdas
may not be needed for Camp II-C.

**Estimated effort**: 2-3 sessions for Phases 1-3, 1 session for Phase 4
verification. With Cam's throughput (~1,000 lines/30 min), the coding
is ~2 hours. The debugging is the unknown.

---

## Summit Verification Procedure

The self-hosted compiler binary reads a file path from stdin, compiles
the source, and writes C# to stdout. This is the same protocol as the
bootstrap runner — it is not a test harness, it is the real compiler.

### Prerequisites

- RISC-V ELF binary: `codex build Codex.Codex --target riscv`
- QEMU user-mode: `qemu-riscv64` (Linux only, or WSL)
- A `.codex` test file (any valid Codex source with a `main` definition)
- The C# bootstrap for comparison: `dotnet run --project tools/Codex.Cli`

### Step 1: Build the binary

```bash
dotnet run --project tools/Codex.Cli -- build Codex.Codex --target riscv
# Output: Codex.Codex/out/Codex.Codex (ELF binary, ~223 KB)
```

### Step 2: Prepare a test file

```bash
cat > /tmp/summit-test.codex << 'CODEX'
main : Integer
main = 42
CODEX
```

### Step 3: Run the RISC-V binary under QEMU

The binary reads a file path from stdin (line 1), opens that file,
compiles it, and prints C# to stdout. Pipe the path:

```bash
echo "/tmp/summit-test.codex" | qemu-riscv64 ./Codex.Codex/out/Codex.Codex > /tmp/rv-output.cs
```

**Important**: The binary blocks on `read-line` (stdin) waiting for a
file path. Always pipe input. A bare `qemu-riscv64 ./Codex.Codex` will
hang indefinitely.

### Step 4: Compare with bootstrap output

```bash
echo "/tmp/summit-test.codex" | dotnet run --project tools/Codex.Cli -- compile > /tmp/bootstrap-output.cs
diff /tmp/rv-output.cs /tmp/bootstrap-output.cs
```

If the diff is empty (or differences are only whitespace/ordering),
**Camp II-C is summited**: the Codex compiler, compiled to native
RISC-V machine code, produces the same output as the C# bootstrap.

### Performance notes

Under QEMU emulation the compiler is slow — the 493-definition binary
does byte-by-byte string operations and recursive type checking. A
simple test file may take 30-120+ seconds. Use `timeout 300` to avoid
indefinite hangs. On real RISC-V hardware it would be significantly
faster.
