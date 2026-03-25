# Review: cam/native-backend-parity

**Reviewer:** Agent Linux  
**Date:** 2026-03-25  
**Status:** Merged to master — 2 bugs found by new regression tests

---

## Bug 1: ARM64 `is-digit` — CSINC condition inverted

**File:** `src/Codex.Emit.Arm64/Arm64CodeGen.cs`  
**Severity:** Functional — returns wrong results

The CSINC instruction uses condition code CC (carry clear, 0x3):

```csharp
m_instructions.Add(0x9A9F37E0u | Arm64Reg.X0); // CSINC X0, XZR, XZR, CC
```

After `CMP X11, #10`, the carry flag is **clear** when X11 < 10 (i.e., the value IS a digit). With CC condition, CSINC returns XZR = 0 when the condition is true.

So: digit → 0, non-digit → 1. **Inverted.**

**Fix:** Change CC (0x3) to CS (0x2):
```
0x9A9F37E0  →  0x9A9F27E0
```
i.e., change the condition field from `0b0011` to `0b0010`.

**Regression test:** `LinuxNativeTests.IsDigit_positive_runs_arm64` — expects "5"→1, gets 0.

---

## Bug 2: TCO segfaults at scale (all 3 backends)

**Severity:** Crash — SIGSEGV (exit 139) at N=100,000

TCO works correctly at small N (10→55 passes on x86_64). At N=100,000 all three backends segfault. The likely cause is **heap exhaustion** — each iteration of the TCO loop still allocates boxed integers via the bump allocator. At 100k iterations, the heap pointer walks past the end of mapped memory.

Evidence:
- The earlier QEMU trace showed heap pointer marching through 0xB9xxxx (~7.6MB) at ~1KB/iteration
- 100k iterations × ~1KB ≈ 100MB, exceeding the 16MB (now 256MB with the page table fix) mapped region
- The segfault is consistent across all backends — not a codegen bug, but a runtime limit

**Options:**
1. **Reduce test N** — use N=1000 (500,500) which fits in the heap. Tests TCO correctness without hitting limits.
2. **Stack-allocate TCO loop vars** — the TCO rewrite could reuse stack slots for the accumulator instead of heap-allocating. This is the real fix but changes the codegen significantly.
3. **Add a heap guard** — mmap more pages on demand (Linux user-mode only).

**Regression tests:** `LinuxNativeTests.TCO_sum_to_100k_runs_{x86_64,arm64,riscv}` — all SEGV.
