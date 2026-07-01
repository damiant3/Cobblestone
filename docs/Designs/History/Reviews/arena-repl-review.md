# Review: Arena-Based REPL Loop for Bare Metal

**Date**: 2026-03-26
**Reviewer**: Agent Linux
**Commit**: `606c8b3`
**Verdict**: Approved with notes

---

## What It Does

Replaces the bare metal halt-after-main with an infinite REPL loop. At the top
of each iteration, the heap pointer (R10) is restored from a saved arena base
address (0x7010), discarding all allocations from the previous compilation. The
loop then calls `main`, prints the result, and repeats.

The persistent area below the arena base is empty but reserved for future use
(REPL state, interned strings, incremental compilation data).

## Verification

- Build: clean
- Tests: 844/844 passing (after test fix — see below)
- QEMU 8.2.2 bare metal boot: confirmed working
  - `main = 42` → prints "42" repeatedly in REPL loop
  - `factorial 10` → prints "3628800" repeatedly

## Bug Found and Fixed

**The REPL loop broke the existing bare metal tests.** The old kernel printed
one result and halted. The new kernel prints forever. The test helper
`CompileAndBootBareMetal` extracts output after "Booting from ROM.." and
trims it — but with the REPL, the output is `42\n42\n42\n42...` over 5
seconds. `Assert.Equal("42", output.Trim())` fails.

**Fix**: Modified the helper to extract only the first line of output after
the boot marker. This is semantically correct — we're testing the first
compilation's result, not all of them.

## Concerns for the Road

### 1. Serial input REPL interaction

The current loop calls `main` unconditionally. For a self-hosted compiler
that reads source from serial, the first iteration consumes the serial input
and compiles. The second iteration calls `main` again, but serial is empty.
What happens depends on the serial read implementation — it might block
(waiting for more input, which is the correct REPL behavior) or return
immediately with empty input (which would produce a compile error or crash).

**Status**: Not a bug in this commit — the serial REPL was already designed
for blocking reads. But worth testing explicitly with a serial-input program.

### 2. Arena overflow

The arena is fixed-size (bounded by the 2MB/64MB mapped heap). If a single
compilation exceeds the arena, the heap pointer walks past mapped memory and
faults. Current compiler output is 298K — well within limits.

**When it matters**: If the compiler grows significantly, or if user programs
allocate large data structures during compilation.

**Recommendation**: A guard page at the arena ceiling that triggers a clean
error message instead of a triple fault would be a nice safety net. Not urgent.

### 3. No REPL state persistence

The arena wipes everything between iterations. A stateful REPL (where
`let x = 42` survives to the next iteration) would need to copy surviving
bindings to persistent storage before arena reset. This is acknowledged in
the commit message as future work.

### 4. Code duplication

The `EmitCallTo("main")` + return type switch + print logic is duplicated
between the bare metal and Linux paths. Minor refactoring opportunity — extract
a `EmitCallMainAndPrint` helper. Not a correctness issue.

### 5. ArenaBaseAddr at 0x7010

The arena base is stored at a fixed memory address (0x7010), adjacent to
TickCountAddr (0x7000) and KeyBufferAddr (0x7008). This is fine for the
current layout but these magic addresses should probably live in a constants
section or enum if they keep growing. Six magic addresses and counting.
