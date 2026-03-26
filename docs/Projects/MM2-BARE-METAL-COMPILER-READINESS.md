# MM2 Readiness Review: Compile .codex on Bare Metal

**Date**: 2026-03-26  
**Author**: Linux agent  
**Purpose**: Gap analysis for the CurrentPlan near-term item:
> *QEMU test: send .codex over serial, verify compilation — First real MM2 validation*

---

## What MM2 Means

The self-hosting Codex compiler (26 files, ~5,100 lines) runs on bare metal
x86-64 hardware. Source code arrives over serial, the compiler processes it
(tokenize → parse → desugar → resolve → typecheck → lower → emit), and
generated output goes back over serial. No OS. No runtime. Just the compiler,
an arena allocator, and the UART.

This is qualitatively different from what we test today. Current bare metal
tests compile trivial programs *on the host* into bare metal ELFs, boot them
under QEMU, and check serial output. MM2 means the compiler *itself* is the
bare metal kernel.

---

## What's In Place

### Environment (verified on Linux agent)

| Component | Version | Status |
|-----------|---------|--------|
| .NET 8 SDK | 8.0.419 | Installed, builds succeed |
| qemu-system-x86_64 | 8.2.2 | Installed, boots bare metal ELFs |
| qemu-system-riscv64 | 8.2.2 | Installed |
| qemu-aarch64 (user-mode) | 8.2.2 | Installed |
| aarch64-linux-gnu-gcc | present | Cross-compilation |
| riscv64-linux-gnu-gcc | present | Cross-compilation |

### Bare Metal Infrastructure (proven)

- **x86-64 bare metal backend**: compiles Codex → multiboot ELF, boots under
  QEMU. CCE-native as of today's merge (`cam/ring4-cleanup`).
- **Codex.OS Rings 0–3**: boot trampoline, IDT, PIC, timer, keyboard, process
  table, preemptive scheduling, capability-enforced syscalls. 7 KB kernel.
- **Ring 4 REPL loop**: arena-based — compile, print, reset heap, repeat.
  Verified under QEMU.
- **Serial I/O with CCE conversion**: `__read_line` and `__bare_metal_read_serial`
  convert Unicode→CCE at input boundary. Print paths convert CCE→Unicode at
  output boundary. Both directions use 128/256-byte lookup tables in `.rodata`.
- **Existing QEMU boot tests**: 4 tests pass (2 x86-64, 2 RISC-V). The x86-64
  tests boot a kernel, capture serial output via `-nographic`, extract the
  first line after `"Booting from ROM.."`. Test harness is in
  `LinuxNativeTests.CompileAndBootBareMetal`.

### x86-64 Backend Capabilities (what it can compile today)

- Integer/boolean arithmetic, comparisons, if/else
- Let bindings, function calls, tail-call optimization
- Pattern matching (`when`/`if` on sum types)
- Records (field access, construction)
- Closures, partial application, indirect calls
- Lists: `list-cons`, `list-append`, `list-at`, `list-length`
- Text: `text-length`, `text-contains`, `text-replace`, `text-starts-with`,
  `show` (integer-to-text), `text-to-int`, `char-code-at`, `char-to-text`,
  `code-to-char`, `is-letter`, `is-digit`, `is-whitespace`
- I/O: `print-line`, `read-line`, `read-file`, `write-file`
- Concurrency intrinsics: `fork`, `await`
- String escape helper
- Heap allocation (bump allocator), arena reset for REPL

---

## The Gaps

### 1. Missing Builtins (blocking)

The self-hosted compiler depends on builtins that the x86-64 backend does not
yet implement. These are used pervasively — the compiler cannot run without them.

| Builtin | Used For | Difficulty |
|---------|----------|------------|
| `text-compare` | Binary search in TypeEnv, Scope, UnificationState — the P2-alt 9.2x speedup depends on this | Medium (strcmp-style loop over CCE bytes) |
| `list-snoc` | O(1) amortized append used across 30+ sites | Medium (append single element to list tail) |
| `list-insert-at` | Sorted insertion for binary search structures | Medium |
| `list-contains` | Membership checks in name resolution | Easy (linear scan with equality) |
| `text-split` | Tokenizer, various string processing | Hard (loop + allocation of list of strings) |
| `text-concat-list` | Batch string joins in emitter output | Medium (loop + concatenation) |

**Note**: The compiler's performance optimization (P2-alt sorted binary search)
makes `text-compare`, `list-snoc`, and `list-insert-at` load-bearing. Without
them the compiler literally cannot type-check. These aren't optional.

### 2. read-file on Bare Metal (design decision needed)

The compiler entry point is:

```
main = do
  path <- read-line
  source <- read-file path
  print-line (compile source "Program")
```

On bare metal there is no filesystem. Two options:

**Option A — Serial bulk read**: `read-file` on bare metal reads from serial
until a sentinel (e.g., `\x04` EOT). The REPL loop already does something
similar with `__bare_metal_read_serial`. The host sends the .codex source
directly, the kernel treats it as the "file contents."

**Option B — Modify the entry point**: A bare-metal-specific `main` that reads
source directly from serial without the path indirection. This is simpler but
means the compiler source needs conditional compilation or a separate entry
point for bare metal.

Option A is cleaner — it preserves the compiler source unchanged and pushes
the adaptation to the runtime layer where it belongs.

### 3. Output Target (design decision needed)

When the bare metal compiler finishes compilation, what does `print-line` emit?

The self-hosted compiler currently emits C# source code (the `emit-full-module`
function generates C#). On bare metal, the serial output would be... C# code.
Which is correct for MM2 validation — the goal is to prove the compiler *runs*,
not that the output is directly executable on bare metal.

Future options:
- Emit C# over serial → validate on host (MM2, simplest)
- Emit x86-64 machine code → execute in arena (MM3, the ultimate fixed point)
- Emit to a different backend target selected at compile time

### 4. Memory Pressure (unknown risk)

The compiler self-compiles its 5,100 lines in ~279ms on the host with managed
GC. On bare metal with arena allocation:

- The arena is a linear bump allocator — no GC, no free. Every allocation
  (every cons cell, every string, every intermediate AST node) permanently
  consumes arena space until reset.
- The compiler creates extensive intermediate structures: token lists, AST
  nodes, type environments (sorted lists), IR nodes, emitted output strings.
- For a *trivial* test program (few lines), arena usage should be modest.
- For self-compilation (5,100 lines), arena usage could be substantial.

**Recommendation**: Start with trivial programs (hello world, factorial) to
validate the pipeline works, then progressively increase complexity. Arena
size can be tuned in the boot trampoline.

### 5. Stack Depth (low risk)

The compiler uses recursion extensively (recursive descent parser, recursive
type checker, recursive emitter). TCO handles some of this, but not all
recursive patterns are tail calls. The bare metal kernel sets up a fixed-size
stack. Deep nesting in source files could overflow it.

For trivial test programs this is unlikely to be an issue.

---

## Proposed Path to MM2

### Phase 1: Missing Builtins (Cam)

Implement the six missing builtins in `X86_64CodeGen.cs`. Priority order:

1. `text-compare` — unblocks binary search, most critical
2. `list-snoc` — unblocks list accumulation
3. `list-insert-at` — unblocks sorted insertion
4. `list-contains` — straightforward
5. `text-concat-list` — needed for emitter output
6. `text-split` — needed for tokenizer

Each can be tested independently with small .codex programs under the existing
QEMU boot test harness.

### Phase 2: read-file Serial Adapter

Implement `__read_file` on bare metal as "read from serial until EOT sentinel."
This requires minimal code — the `__bare_metal_read_serial` helper already
does most of the work. Add EOT detection and return the buffer as a
length-prefixed CCE string.

### Phase 3: Trivial Compilation Test

Write a QEMU test that:
1. Compiles the self-hosting compiler into a bare metal ELF (using the C#
   bootstrap to compile all 26 .codex files into one bare metal kernel)
2. Boots the kernel under `qemu-system-x86_64`
3. Sends a trivial program over serial: `main : Integer\nmain = 42`
4. Captures the C# output from serial
5. Verifies the output contains the expected generated code

This is the MM2 validation test.

### Phase 4: Progressive Complexity

- Compile programs with pattern matching, records, lists
- Compile a prelude module
- Eventually: compile the compiler itself on bare metal (MM3)

---

## What the Linux Agent Can Do

The environment is ready. I can:

- Run the existing QEMU boot tests (and they pass — verified today)
- Build any branch Cam pushes
- Test new builtins as they land (compile + QEMU boot + serial capture)
- Write the Phase 3 integration test once the builtins are in place
- Profile arena usage if we add instrumentation

---

## Files Referenced

| File | What |
|------|------|
| `src/Codex.Emit.X86_64/X86_64CodeGen.cs` | x86-64 backend (4,468 lines) |
| `tests/Codex.Types.Tests/LinuxNativeTests.cs` | QEMU boot test harness |
| `tests/Codex.Types.Tests/Helpers.cs` | `CompileToX86_64BareMetal` helper |
| `Codex.Codex/main.codex` | Compiler entry point |
| `Codex.Codex/` | Self-hosted compiler (26 files, ~5,100 lines) |
| `prelude/` | Prelude modules (23 files, ~1,250 lines) |
