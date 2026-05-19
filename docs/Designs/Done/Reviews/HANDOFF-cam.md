# Handoff: Agent Linux → Cam

**Date:** 2026-03-25  
**Session:** Linux sandbox review + flat-iron parallel testing

---

## What I did

1. **Ran `linux-session-init.sh`** — installed .NET 8.0.419, QEMU 8.2.2, cross-compilers. Build and all 808 tests green.

2. **Reviewed and merged your branches** (with your fixes):
   - `cam/native-backend-parity` — TCO + ARM64/RISC-V builtins + Slt→Sltu fix ✅
   - `cam/ring4-self-hosting` — x86_64 builtins + CC_BE fixes ✅
   - Both pushed to `origin/master`.

3. **Reviewed `cam/fix-bare-metal-closure-addr`** (includes `more-heap-pages` + `more-builtin-gaps`):
   - 256MB page tables ✅
   - Closure address fix ✅
   - text-contains/text-starts-with ✅
   - **LGTM — ready to merge.** See `docs/reviews/cam-fix-bare-metal-closure-addr.md`.

4. **Wrote 21+ new regression tests** in `LinuxNativeTests.cs` covering `is-digit`, `is-whitespace`, `negate`, and TCO across all three native backends (x86_64 native, ARM64/RISC-V via qemu-user). Committed to master.

5. **Ran flat-iron parallel testing.** Performance baseline:
   - x86_64 native: 26 tests in 205ms (~8ms/test)
   - ARM64 qemu-user: 34 tests in 349ms (~10ms/test)
   - RISC-V qemu-user: 41 tests in 14s (dominated by 2 bare-metal qemu-system tests at 5s each; user-mode tests are ~100-200ms)

---

## 3 bugs found — need your fixes

### Bug 1: ARM64 `is-digit` — CSINC condition inverted

**File:** `Arm64CodeGen.cs`, `is-digit` case  
**Symptom:** `is-digit "5"` → 0 (should be 1), `is-digit " "` → 1 (should be 0)

The CSINC uses CC (carry clear = 0x3). After `CMP X11, #10`, carry is clear when X11 < 10 (IS a digit), so CSINC returns 0 for digits. Inverted.

**Fix:** Change condition from CC (0x3) to CS (0x2):
```
0x9A9F37E0u  →  0x9A9F27E0u
```

**Tests that will flip green:** `IsDigit_positive_runs_arm64`, `IsDigit_space_rejected_arm64`

### Bug 2: x86_64 `is-whitespace` — false positive for non-whitespace

**File:** `X86_64CodeGen.cs`, `is-whitespace` case  
**Symptom:** `is-whitespace "a"` → 1 (should be 0)

Suspected `AllocTemp()` register aliasing — `t2` may get the same register as `rd`, so `Setcc` into `t2` clobbers the character byte before the tab/newline/CR comparisons run. Fix: use `AllocLocal()` for `rd` to pin it in a callee-saved register, or save it before the comparison chain.

**Test that will flip green:** `IsWhitespace_letter_rejected_x86_64`

### Bug 3: TCO segfaults at N=100,000 (all backends)

**Symptom:** `sum-to 100000 0` → SIGSEGV (exit 139) on x86_64, ARM64, RISC-V  
**Works at:** N=10 (x86_64 confirmed, produces 55)

The TCO loop is functionally correct but each iteration still heap-allocates boxed integers via the bump allocator. At 100k iterations the heap pointer walks past mapped memory. The earlier QEMU trace showed ~1KB/iteration at 0xB9xxxx.

**Options (your call):**
- Quick: reduce test to N=1000 (500,500) which fits in heap. Tests TCO correctness without hitting limits.
- Medium: bump heap mmap to 256MB (your page table fix helps bare-metal; user-mode may need a larger mmap too).
- Real fix: have TCO loop vars reuse stack slots instead of heap-allocating.

---

## What's on master now

```
f48bef3  (origin/master) — your toolkit cleanup + copilot instructions
28977bd  (local master)  — my regression tests (21 new tests)
```

My test commit needs a push once you've merged `fix-bare-metal-closure-addr`. Or I can push now and you rebase — your call.

## Branches still open

| Branch | Status | Action |
|--------|--------|--------|
| `cam/fix-bare-metal-closure-addr` | LGTM | Merge to master |
| `cam/ring4-self-hosting` | has is-letter fix (merged) + needs is-whitespace fix | Fix Bug 2, push |
| `cam/native-backend-parity` | has Sltu fix (merged) + needs is-digit + TCO fixes | Fix Bugs 1 & 3, push |
| `cam/more-builtin-gaps` | rolled into fix-bare-metal-closure-addr | Can delete |
| `cam/more-heap-pages` | rolled into fix-bare-metal-closure-addr | Can delete |

## Review docs

Detailed analysis for each branch: `docs/reviews/cam-*.md`
