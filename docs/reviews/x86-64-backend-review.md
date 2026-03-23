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

**Verdict:** One blocker (closure register conflict), one known ELF alignment issue
(same as ARM64/RISC-V). The segfault is a clean one-variable fix.

---

## Bug 1 — BLOCKER: Register conflict in closure allocation clobbers heap pointer

**File:** `X86_64CodeGen.cs`, `EmitPartialApplication` method, lines ~540–546

**Repro (minimal):**
```
apply : (Integer -> Integer) -> Integer -> Integer
apply (f) (x) = f x

double : Integer -> Integer
double (x) = x + x

main : Integer
main = apply double 21
```
→ Segfault at 0x4005f3. GDB confirms: `SIGSEGV` writing to text segment.

**Root cause:** `EmitPartialApplication` allocates the closure on the heap:

```csharp
byte ptrReg = AllocTemp();                           // line 541 — returns RAX
X86_64Encoder.MovRR(m_text, ptrReg, HeapReg);        // RAX = heap pointer
X86_64Encoder.AddRI(m_text, HeapReg, closureSize);   // bump heap

EmitLoadFunctionAddress(Reg.RAX, trampolineName);    // line 546 — CLOBBERS ptrReg!
X86_64Encoder.MovStore(m_text, ptrReg, Reg.RAX, 0);  // mov [RAX], RAX → writes to text!
```

`TempRegs = [RAX, RCX, RDX, RSI, RDI, R11]`. After the trampoline emission,
`AllocTemp()` cycles back to RAX. Then `EmitLoadFunctionAddress` hardcodes `Reg.RAX`
for the trampoline address, clobbering the heap pointer. The `MovStore` becomes
`mov [trampoline_addr], trampoline_addr` — a write to the read-only text segment.

**Disassembly proof** (text section offsets):
```asm
532: mov rax, r10              ; ptrReg(RAX) = heap pointer ✓
535: add r10, 0x8              ; bump heap ✓
539: movabs rax, 0x4005c4     ; CLOBBER — rax now = trampoline vaddr
543: mov [rax], rax           ; CRASH — writing to text segment (0x4005f3)
```

GDB: `Program received signal SIGSEGV at 0x00000000004005f3` — matches exactly.

**Fix:** Use `AllocLocal()` for `ptrReg` (same pattern as record/list allocation):

```csharp
byte ptrLocal = AllocLocal();
byte tmp = AllocTemp();
X86_64Encoder.MovRR(m_text, tmp, HeapReg);
StoreLocal(ptrLocal, tmp);
X86_64Encoder.AddRI(m_text, HeapReg, closureSize);

EmitLoadFunctionAddress(Reg.RAX, trampolineName);
X86_64Encoder.MovStore(m_text, LoadLocal(ptrLocal), Reg.RAX, 0);

for (int i = 0; i < capLocals.Count; i++)
{
    byte val = LoadLocal(capLocals[i]);
    X86_64Encoder.MovStore(m_text, LoadLocal(ptrLocal), val, 8 + i * 8);
}

return LoadLocal(ptrLocal);
```

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
| `closure-test2.codex` (apply double 21) | ✗ SIGSEGV — Bug 1 |
| Self-hosted → x86-64 (235KB) | Compiles ✓, SIGSEGV at runtime |
