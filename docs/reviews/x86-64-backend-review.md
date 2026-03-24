# Code Review: cam/x86-64-backend

**Reviewer:** Agent Linux (Opus 4.6)  
**Date:** 2026-03-23  
**Branch:** `cam/x86-64-backend` (6 commits, tip `08eab18`)  
**Scope:** `src/Codex.Emit.X86_64/X86_64CodeGen.cs` (+182 lines), full backend ~2,500 lines

---

## Summary

x86-64 native backend: encoder, ELF writer, codegen for all IR nodes. Integer-returning
programs run correctly natively on Linux (`factorial` → exit 10! mod 256 = 0, `hello` →
exit 25). Self-hosted compiler compiles to 235KB ELF but segfaults at runtime.

**Verdict:** Closure bug fixed (verified). Self-hosted segfault is caused by 5 missing
builtins producing silent unresolved `call +0` instructions that corrupt the stack.
Implement the builtins and add an unresolved-call warning.

---

## Bug 1 — FIXED: Register conflict in closure allocation clobbers heap pointer

*Fixed by Cam in `834657e`.* Verified: `apply double 21` → exit 42. ✓

---

## Bug 2 — BLOCKER: 5 missing builtins → unresolved calls → stack corruption → segfault

**Root cause of the self-hosted compiler segfault.**

Added diagnostic `else` branch to `PatchCalls()` and found **18 unresolved calls** to
5 builtins that exist in the RISC-V backend but are missing from x86-64:

```
text-replace    (6 call sites)
char-code-at    (5 call sites)
code-to-char    (4 call sites)
char-code       (2 call sites)
is-letter       (1 call site)
```

An unresolved `call rel32` has rel32=0, meaning `call current_addr+5` — falls through
to the next instruction without a frame, pushing a garbage return address. The self-hosted
compiler hits `text-replace` in the lexer almost immediately, corrupting the stack.
Subsequent code dereferences a NULL from the corrupted stack and crashes.

**GDB confirmation:** Crash at `0x41cbdf` (text offset 117,551). First unresolved
`char-code-at` is at text offset 117,672 — 121 bytes later in the same function.
Stack backtrace shows heap addresses (`0x43b050`) where return addresses should be.

**Fix:** Implement these 5 builtins in `TryEmitBuiltin` + emit runtime helpers. The
RISC-V backend (`RiscVCodeGen.cs`) has working implementations of all five — same
algorithms, different register encoding:

| Builtin | RISC-V helper | What it does |
|---------|---------------|-------------|
| `text-replace` | `__str_replace` | Find/replace in length-prefixed string |
| `char-code-at` | inline | Load byte at offset from string data |
| `char-code` | inline | First byte of a 1-char string |
| `code-to-char` | inline | Allocate 1-byte string from integer |
| `is-letter` | inline | Check if char code is [A-Z] or [a-z] |

**Also:** `PatchCalls()` should warn on unresolved targets (the ARM64 backend does,
x86-64 doesn't). Silent `call +0` is extremely dangerous.

---

## Bug 2 — KNOWN: `p_align = 16` in ELF writer

**File:** `ElfWriterX86_64.cs` — same issue as ARM64/RISC-V (see `arm64-qemu-verification.md`).

Linux x86-64 kernel happens to be lenient about this (binaries run), but it's technically
wrong and may fail on other loaders. Fix: `(ulong)16` → `(ulong)0x1000` in both PHDR entries.

Not blocking — x86-64 binaries run natively despite the wrong alignment.

---

## What's Good

- **Trampoline design** is correct: jump-over, shift args right (backward to avoid
  clobber), load captures from closure, tail-jump via `jmp rax`. No stack frame needed.
- **Indirect call protocol** is correct: `mov r11, closure; mov rax, [r11]; call rax`.
  R11 survives into the trampoline for capture loading.
- **Call patching** (`rel32` from `patchOffset+5`) is correct for x86-64 E8 encoding.
- **Function address patching** (`MovRI64` = 2-byte prefix + 8-byte imm64) fixup offset is correct.
- **Prologue/epilogue** uses RBP frame, pushes callee-saved in correct order, restores via
  `lea rsp, [rbp-0x28]` then pops. Solid.
- **Spill infrastructure** (R8/R9 scratch, RBP-relative offsets) matches the RISC-V pattern.
- Self-hosted compiler compiles successfully to 235KB — the entire pipeline works except for
  this one register conflict at runtime.

---

## Verification

| Test | Result |
|------|--------|
| `dotnet build tools/Codex.Cli` | ✓ 0 warnings, 0 errors |
| `hello.codex` → x86-64 | ✓ exit 25 (square 5) |
| `factorial.codex` → x86-64 | ✓ exit 0 (3628800 mod 256) |
| `closure-test.codex` (add 3 4, no HOF) | ✓ exit 7 |
| `closure-test2.codex` (apply double 21) | ✓ exit 42 (Bug 1 fixed) |
| Record with text field | ✓ exit 30 |
| List length | ✓ exit 5 |
| String equality | ✓ exit 1 |
| Text-length | ✓ exit 5 |
| Self-hosted → x86-64 (239KB) | Compiles ✓, SIGSEGV at runtime — Bug 2 |
