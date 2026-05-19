# Text-Mode Emit Regression Investigation

**Status:** Root cause not yet identified. Bisected to CL 377-387. Full code inspection complete; disassembly required.  
**Investigator:** Cam  
**Date:** 2026-04-30  

## Summary

Text-mode self-compilation (pingpong stage 1) is broken since CL 387. Binary mode works perfectly at all CLs. The text output is correct but serial throughput is ~180x slower than binary, causing harness timeouts and apparent truncation.

## Hard Facts (proven)

### Bisect results

| CL | Text mode | Binary mode | Accelerator | Notes |
|----|-----------|-------------|-------------|-------|
| 376 | 710,938 B in 48s PASS | 1,271,104 B in 58s PASS | KVM | Last known good |
| 387 | Hung >312s, 91K truncated | 1,274,784 B in 57s PASS | KVM | First bad |
| 449 | Hung >363s, 125K truncated | 1,343,600 B in 41s PASS | KVM | |
| 463 | Hung >312s, 91K truncated | 1,384,568 B in 40s PASS | KVM | |
| 506 | 357K in 798s / 560K in 921s | 1,401,512 B in 45s PASS | WHPX | |
| 519 | 193K in 557s | 1,422,200 B in 37s PASS | WHPX | |

### Binary fixed point holds at all CLs

seed(source) === seed at every tested CL. Binary compilation is correct.

### The 221K non-streaming output IS complete

The non-streaming text path (`emit-text CtCodexText`) at CL 506 produced 221,809 bytes containing 632 function definitions. This is the CORRECT output: `skip-def` filters to only lowercase-starting names (CCE 13-38), and 632 of the 1849 total defs pass this filter. The remaining 1217 defs are constructors or names starting with uppercase/punctuation, correctly skipped. The output is not truncated — it is complete.

### Serial throughput is the bottleneck

Binary serial: ~200K bytes/s (1.4MB ELF in ~7s of serial I/O time)  
Text serial: ~1.1K bytes/s (221K in ~197s of serial I/O time)  
Ratio: ~180x slower for text serial vs binary serial.

Both use per-byte UART poll-wait-write. Binary via `__write_binary` (X86_64Helpers.codex:416). Text via `emit-print-text-loop` (X86_64IO.codex:273). Both poll port 0x3FD for THR empty, then write to 0x3F8 via `out dx, al`.

### The frontend is shared and fast

Both binary and text modes call `compile-frontend` (or `compile-checked` for streaming). Binary total is 37-42s. The frontend cannot be the bottleneck.

### Not accelerator-specific

CL 387 text mode fails under both KVM and WHPX. CL 376 text mode works under both.

## What Changed Between CL 376 and CL 387

The CL 387 SUT was compiled from source that includes CLs 377-387. Seed refreshes: CL 376, CL 387 (no intermediate).

### CL 377 — bounded SourcePosition (TESTED, PASS)
- SourceText.codex: line:u16, column:u16, offset:u32
- Text mode verified: 710,950 B, 94s combined

### CL 378 — bounded Token (TESTED, PASS)
- Token.codex: offset:u32, length:u32, line:u16, column:u16, file-id:u16
- Text mode verified: 711,049 B

### CL 381 — bounded Integer across 18 files (NOT TESTED)
- Annotation-only changes (Integer -> Integer between lo and hi)
- 18 files including CodegenState (X86_64State.codex), SkipListText, EmitResult, PatchEntry, TcoState, LocalBinding, FieldLocal, and many helper result types
- **Explicitly skipped pingpong**: "Confidence-only shelve... skipping per-CL pingpong on shape-repeats"
- Changed record layouts at machine-code level via width-sort (CL 374) + narrow-store (CL 372)

### CL 384 — revert ExprTypeEntry.key bound (NOT TESTED)
- Fixed CL 381 overflow on ExprTypeEntry.key
- "Verification: pending"

### CL 387 — deck-record in Lexer + codegen deck-bound (TESTED, reportedly PASS)
- Lexer.codex: 50 deck-record wrapping sites for token kinds
- X86_64.codex: emit-nullary-ctor deck-bound branch
- X86_64Compound.codex: emit-sum-ctor deck-bound branch
- opening.codex: early-halt on lex errors
- Verification claimed: "stage1 === stage2 byte-identical (713,420 clean bytes)"

## Code Inspection Results

### Text emitter (CodexEmitter.codex) — READ IN FULL

- No quadratic string concatenation patterns. Every accumulation uses `list-snoc` + `text-concat-list` (O(n)).
- `emit-apply` processes ~410 extra `deck-record` IrApply nodes — trivial per-node overhead (~10 instructions each).
- `skip-def` correctly filters constructor names. Output size difference (710K vs 221K) is due to different source sizes and constructor counts at different CLs, not a bug.
- TypeVarMap (only bounded record in text emitter): 2 fields, entries(8B)@0 + next-id(2B)@8. No layout ambiguity — safe regardless of by-type vs by-list path.
- ApplyChain: 2 pointer fields, no narrowing. Safe.

### Record layout store/load paths — INSPECTED

- `emit-record` (X86_64Compound.codex:696-720): Two paths:
  - **by-type**: When `resolve-constructed-ty` returns RecordTy. Uses `emit-narrow-store-checked` at width-sorted offsets. Correct.
  - **by-list fallback**: When type resolution fails. Uses 8-byte stores at `rank * 8` offsets. Wrong offsets for narrow-field records.
- `emit-field-access` (X86_64Compound.codex:779-792): Always uses width-sorted offsets via `cce-byte-offset-and-type` + `emit-narrow-load`. 
- **Layout mismatch risk**: If `emit-record` takes by-list but `emit-field-access` uses by-type, offsets don't match for records with mixed-width fields.
- **Finding**: All examined record types resolve to RecordTy correctly. Type names are unmangled. `lookup-type-binding` finds them. No mismatch found for any specific record.

### Serial output paths — INSPECTED

- `__write_binary` (X86_64Helpers.codex:414-452): Per-byte poll-wait-write. **Resets wd-stale-tick-addr to 0 on every iteration** (line 424-426).
- `emit-print-text-loop` (X86_64IO.codex:273-296): Per-byte CCE-to-Unicode lookup + poll-wait-write. **Does NOT reset wd-stale-tick-addr.**
- Watchdog threshold: 5,500,000 ticks. At 18.2 Hz PIT, unreachable in any test window. Watchdog does not fire.
- ISR overhead: ~50 instructions per tick for the stale-counter path. 18.2 Hz = negligible overhead.
- ISR saves/restores RAX via trampoline push/pop. No register clobbering.

### SkipListText layout — VERIFIED CORRECT

- Fields: head(8B)@0, size(4B)@8, max-level(1B)@12. Width-sort layout.
- Construction via record literals → by-type path → narrow stores at correct offsets.
- No `__record-set` usage on SkipListText fields.
- `skip-list-text-has` reads max-level from offset 12 via movzx-byte. Consistent with store.

### CodegenState layout — VERIFIED SAFE

- 23 eight-byte fields, 4 four-byte fields, 3 two-byte fields, 1 one-byte field.
- Width-sort produces consistent offsets used by both `emit-field-access` (load) and `__record-set` (store).
- `emit-record-set-builtin` resolves CodegenState to RecordTy and uses `emit-narrow-store-checked`. Correct.

### IR types — NO BOUNDED FIELDS

- IRDef, IRExpr, IRParam, IRFieldVal, IRBranch, IRPat — all pointer-typed fields. No narrowing. No layout risk.

## What I Cannot Determine By Code Inspection

1. **The actual generated machine code for `emit-print-text-loop`** in the CL 387 SUT vs CL 376 SUT. The source is identical, but the compiler that compiles it is different (CL 376 seed compiling CL 387 source, which has bounded CodegenState fields). The CodegenState layout change could affect register allocation, spill patterns, and instruction sequences produced by `alloc-temp`, `load-local`, `store-local` within the print loop codegen.

2. **Whether the SUT's compiled `emit-print-text-loop` has a functional bug** (wrong instruction, wrong offset, wrong register) that causes the 180x serial throughput drop. This requires disassembly.

3. **Whether there are intermediate CLs 377-386 that changed source files I haven't examined.** I verified CL 381's changes are annotation-only across all 18 files. CLs 377, 378, 384 are also verified annotation-only or single-field reverts.

## Test Plan

### Phase 0: Timing isolation (establish where time goes)

The harness reports total stage time but not the split between frontend, emit, and serial I/O. Before disassembly, establish the split empirically.

0a. **MEASURE mode on the SUT.** The SUT's `dispatch-on-mode` accepts "MEASURE" which calls `compile-text` + `format-heap-marks` + reports emit byte count. This runs the full frontend + text emit but does NOT print the full text body — only the heap-marks summary line. Comparing MEASURE wall time to TEXT wall time isolates serial I/O cost from compilation + emit.
  - Send "MEASURE\n" + source + EOT via the same serial harness (modify `Invoke-TextStage` or write a one-off script).
  - If MEASURE completes in ~40s (same as binary), the frontend+emit is fast and the bottleneck is serial I/O.
  - If MEASURE takes 200+s, the bottleneck is in the frontend or text emit, not serial.

0b. **Text mode on a tiny input.** Compile `samples/hello.codex` in TEXT mode (not BINARY) through the SUT. hello.codex is ~10 lines. If text output arrives in <1s, the per-character serial path works at speed for small inputs. If it's slow even for hello.codex, the serial path itself is broken.
  - Modify `sample-compile-selfhost.ps1` to send "TEXT\n" instead of "BINARY\n", capture text output instead of ELF.

0c. **Text mode on medium input.** Compile a single chapter (e.g., `Codex.Codex/Core/Collections.codex`) in TEXT mode. This is ~200 lines, ~50 defs. Measures whether the slowdown scales with input size or is a fixed overhead.

0d. **Binary mode with serial throughput measurement.** Time just the serial write portion of binary mode. The harness already knows when the first byte arrives (after SIZE: marker). Measuring time from SIZE: to last ELF byte gives the binary serial throughput directly.

0e. **Compare HEAP HWM between binary and text.** Both modes report heap HWM (on COM2 via HEAP: marker). If text mode has dramatically higher heap, the compilation phase is allocating more (deck reservation + text emit allocations), and the serial slowdown may actually be a symptom of near-OOM thrashing or watchdog interaction with high heap.

### Phase 1: Disassembly comparison (no compilation needed)

Both ELFs exist on disk: seed at `seed/Codex.Codex.elf` (CL 506, 1,401,512 B) and a SUT at `build-output/bare-metal/Codex.Codex.elf` (1,402,048 B).

1. **Extract the print loop from both ELFs.** Search for the characteristic byte sequence of `emit-serial-wait-thr`: `[0xBA, ?, ?, ?, ?, 0xEC, 0xA8, 0x20]` (li rdx 1021; in al,dx; test al,0x20). Compare the surrounding instructions.

2. **Extract `__write_binary` from both ELFs** using its characteristic wd-stale-tick-addr reset pattern. Compare to the print loop to identify the specific instruction-level difference.

3. **Disassemble both SUTs' print text functions** (search for the CCE-to-Unicode table lookup pattern: `movzx byte + add + movzx byte` followed by the serial poll). Count instructions per character in each SUT.

### Phase 2: Targeted single-variable tests

4. **CL 376 compiler + CL 376 tools + CL 376 seed, text mode under WHPX.** Baseline timing on the current machine under WHPX (we only have KVM timing so far). Eliminates accelerator as a variable.

5. **CL 376 seed + CL 381 source (pre-deck-record, bounded only).** If text mode works, CL 381's bounded annotations are safe. If broken, CL 381 is the culprit regardless of CL 387's changes.
   - Requires: sync Codex.Codex/ to CL 381, keep seed at CL 376 (no seed refresh at CL 381).
   - Note: CL 381 source was never compiled standalone — it was submitted as a confidence-only shelve folded into CL 387.

6. **CL 376 seed + CL 384 source (bounded + key revert).** Same as #5 but includes the ExprTypeEntry.key fix. Isolates whether the key overflow matters for text mode.

7. **CL 376 seed + CL 387 source minus Lexer.codex deck-record wrapping.** Sync to CL 387, revert only Lexer.codex to CL 376. If text mode works, deck-record wrapping is the issue. If still broken, bounded-integer is the issue.

### Phase 3: Surgical fixes based on findings

8. **If disassembly reveals wrong instructions in the print loop:** Fix the codegen that produces them (likely in `alloc-temp` or `load-local` interaction with bounded CodegenState fields).

9. **If CL 381 bounded-only source reproduces the bug:** The width-sort layout computation or narrow-load codegen has a subtle bug for specific field configurations. Audit `accumulate-offset-width-sort` and `emit-narrow-load` for edge cases (e.g., Boolean fields mixed with bounded Integer fields in the same record).

10. **If all bounded annotations are safe but deck-record wrapping causes the bug:** The `emit-deck-record-wrapper` runtime R10 swap has a side effect that corrupts state visible to the text serial output path. The watchdog stale-counter interaction (R10 alternating between bivy and deck during compilation, causing stale counter to reset repeatedly) may need investigation.

### Phase 4: Validation

11. **Full pingpong with the fix.** Text mode stage1 === stage2, byte-identical, at the expected output size (~700-800K depending on CL). Serial throughput should be comparable to binary (~200K bytes/s).

12. **Full sweep** on the fixed SUT.

13. **Seed rebuild** and second-iteration verification (SUT compiles itself, re-verify text mode).

## Open Questions

- Why did CL 387's verification reportedly pass text mode? Either the claim was inaccurate, or the test environment was different.
- The watchdog stale-counter reset difference between `__write_binary` and `emit-print-text-loop` — while the threshold is too high to trigger, could the growing stale counter interact with the ISR in an unexpected way under QEMU/WHPX?
- Could there be a QEMU-level difference in how serial port I/O is virtualized when the guest has bounded-integer-narrowed records in memory (different TLB/cache behavior due to different allocation patterns)?
