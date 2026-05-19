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

## Bug 2 — FIXED: 5 missing builtins → unresolved calls

Fixed by Cam in `296e359`. Verified: zero unresolved call warnings on self-hosted compile.

The 5 builtins (`text-replace`, `char-code-at`, `code-to-char`, `char-code`, `is-letter`)
were ported from the RISC-V backend and `PatchCalls()` now warns on unresolved targets.

---

## Bug 3 — BLOCKER: ConstructedType not resolved in record allocation → undersized heap object

**Root cause of the REMAINING self-hosted segfault** (after Bug 1 and Bug 2 fixes).

**Crash:** `is-at-end` (Lexer.codex:143) does `st.offset >= text-length st.source`.
Segfaults dereferencing `st.source` which is NULL.

**GDB trace:**
```
SIGSEGV at 0x41cd5c: mov rdx, [rcx]   ; rcx = 0 (NULL)

Function map (from diagnostic build):
  0x41cd38 = is-at-end
  0x41cf9a = skip-spaces  
  0x41e9dc = scan-token

Call chain: scan-token → skip-spaces → is-at-end → CRASH
```

**Heap state at crash:**
```
LexState record at 0x43b050: [0x0000000000000000, 0x0000000000000000]
Heap pointer R10 = 0x43b058 = record_addr + 8
```

`LexState` has 4 fields (`source : Text, offset : Integer, line : Integer, column : Integer`)
requiring 32 bytes. But R10 only advanced 8 bytes past the record base — the allocation
treated the record as having 1 field instead of 4.

**Root cause:** `LexState` is a `ConstructedType` at the IR level. The codegen's record
allocation computes size from the type's field count, but doesn't resolve `ConstructedType`
→ `RecordType` first. With an unresolved type, the field count is 0 or 1, producing an
8-byte allocation for a 32-byte record. The subsequent field stores write past the
allocated space into uninitialized heap, and reads return zeros.

**Where to fix:** Check every place that reads field count or field list from a type:

1. `EmitRecord` (line ~580) — computes allocation size from field count
2. `EmitFieldAccess` (line 604) — `fa.Record.Type is RecordType rt` misses ConstructedType
3. `EmitConstructor` — same pattern for sum types

All need a ConstructedType resolution step, same as exists in escape copy at line 2001:
```csharp
if (type is ConstructedType ct && m_typeDefs[ct.Constructor.Value] is CodexType resolved)
    type = resolved;
```

The RISC-V backend hit the same bug and fixed it in `LowerFieldAccess` at the IR level.
The x86-64 backend needs the same resolution, either at the IR level or in the codegen.

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
