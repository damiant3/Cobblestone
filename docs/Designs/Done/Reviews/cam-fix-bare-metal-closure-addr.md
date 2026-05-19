# Review: cam/fix-bare-metal-closure-addr

**Reviewer:** Agent Linux  
**Date:** 2026-03-25  
**Status:** LGTM — ready to merge

---

## 1. Bare metal page tables 16MB → 256MB — ✅

The 32-bit loop replacing 8 hardcoded PD entries with 128 is correct:
- `edi=0x3000` (PD base), `ecx=128`, `eax=0x83` (phys=0 | present+writable+huge)
- Loop body: `mov [edi], eax; mov dword [edi+4], 0; add edi, 8; add eax, 0x200000; dec ecx; jnz`
- Short jump offset `-(count - top + 2)` is correct (2-byte jnz encoding)
- User-mode fork path also bumped 8→128

This directly addresses the heap exhaustion seen in QEMU tracing (heap at 0xB9xxxx = ~7.6MB was approaching the old 16MB ceiling).

## 2. Closure/trampoline address fix — ✅

Critical bug fix. Bare metal loads the kernel at `0x100000` (1MB), but the closure patching code was computing function addresses using the ELF formula `0x400000 + textFileOffset`. All closures and trampolines would jump to wrong addresses under bare metal.

The fix correctly branches on `m_target == X86_64Target.BareMetal` to use `0x100000` directly.

## 3. text-contains + text-starts-with (ARM64 + RISC-V) — ✅

Both are naive O(n·m) substring/prefix checks implemented as hand-rolled assembly helpers. Reviewed the branch logic, forward-patch offsets, callee-saved register save/restore, and byte-at-offset-8 text layout. All correct.

The ARM64 version adds `Beq`, `Bne`, `Bge`, `Blt` encoder helpers — condition codes are correct (0x0, 0x1, 0xA, 0xB respectively).

## Verdict

**Merge.** All three pieces are solid. Build passes, 808 existing tests green.
