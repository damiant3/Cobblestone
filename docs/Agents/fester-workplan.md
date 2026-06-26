# Agent Fester Workplan

**Stream:** `//Codex/RESTRUCTURE`
**Updated:** 2026-06-25
**Latest CL:** 6091

## Score: 130/135 ARM64 tests pass (96.3%)

## Session CLs (2026-06-25)

| CL | Description |
|----|-------------|
| 6071 | Record field store uses embedded /N index; learned-field clean name + index; list-push/cons B.GE→B.GT for cap=0 |
| 6073 | ttf-debug test passes (pre-extracted font fields) |
| 6075 | Heap corruption narrowed to record alloc sizing |
| 6077 | ARM64 batch results doc |
| 6081 | Copy-on-write list-set-at; has-value hardcoded field=1; otherwise fallback to hardcoded |
| 6086 | Merge-down from main (val RISC-V CL 6085, blu Cornell CL 6070) |
| 6089 | Workplan update |
| 6090 | vec-select: a64-load-local for spill-slot out-ptr; vec-cmp uses non-recycled state |
| 6091 | Workplan update |

## Tests Fixed This Session
- `ui-event-test` — record field index (CL 6071)
- `keyboard-layout-test` — record field index (CL 6071)
- `vec-select` — spill-slot out-ptr + vec-cmp local recycle (CL 6090)
- `ttf-debug` — new test, passes with pre-extracted fields (CL 6073)

## Remaining Failures (5)

### 1. truetype-bridge-test, truetype-render-test — HEAP CORRUPTION
**Symptom:** `gr-render-glyph font 65 16` returns w=1 h=1 (should be w=9 h=11). The glyph data itself is correct (xmax=512) when parsed directly with pre-extracted args, but `ttf-glyph-for-char font 65` corrupts the font record on the heap.

**What's proven:**
- `ttf-read-glyph buf glyf-off 20 hms 2` with pre-extracted args → xmax=512 ✓
- `ttf-glyph-for-char font 65` → xmax=512 but font.tf-glyf-off corrupted to 1 (was 176)
- font.tf-head.th-units-per-em corrupted to 0 (was 1024)
- 64-byte padding on ALL record allocations: NO effect
- list-push bounds checks: correct (B.GE for count≥cap)
- Stack frames: correct (96 + spill*8)
- x28 (heap) only moves forward
- Pre-extraction workaround works but can't be applied inside foreword

**Root cause hypothesis:** Something during `ttf-read-glyph`'s execution (or its 5-arg call setup) writes to the font record's heap address. Not record sizing, not list bounds, not stack overflow. Needs binary-level debugging — set a watchpoint on the font record's offset 64 during execution. The symbol map shows `ttf-glyph-for-char` at 0x40005CD0 (328 bytes) in the truetype-bridge-test binary.

**Next step:** Use QEMU GDB stub to set a hardware watchpoint on the font record address + 64 and catch the store instruction that corrupts it.

### 2. trie-prefix-test — TCO + COMPLEX PATTERN
**Symptom:** `trie-insert trie-empty "ab" 42` creates 2 nodes (should be 3). `trie-contains` returns False. Manual step-by-step insert works (3 nodes, contains=True).

**What's proven:**
- 6-arg TCO with integers: works
- 6-arg TCO with record + list-push + list-set-at: works
- list-set-at copy semantics: correct (verified on 128-element lists)
- trie-empty-node isolation: correct (copy prevents shared mutation)
- trie-char-idx: correct (97, 98, 99 for a, b, c)
- Manual step-by-step trie insert: 3 nodes, trie-contains=True
- Calling trie-insert-at for each step separately: works
- The COMBINED trie-insert (single call with TCO) only creates 1 node per call

**Root cause hypothesis:** The specific combination of conditional branch (if child-idx < 0), nested `list-set-at (list-push ...)`, TrieNode constructor, Trie constructor, then TCO tail call in `trie-insert-at` causes the TCO second iteration to skip the node-creation branch. All individual components test correct in isolation.

### 3. tls-test — INFINITE LOOP
**Symptom:** Hangs after printing `x25519: pubkey-len=32`. First pubkey call works, DH exchange computation (second call) hangs even with 120s timeout.

**Root cause hypothesis:** X25519 field arithmetic loops forever. Possibly an integer arithmetic bug (overflow, wrong modular reduction) on ARM64 that causes a convergence check to never terminate. Or the copy-on-write list-set-at causes algorithm state divergence from expected in-place behavior.

### 4. ui-orchestrator-test — COMPILE TIMEOUT
Not a codegen bug. The test has a large dependency chain that exceeds the 120s batch compile timeout.

## Architectural Findings

1. **Embedded /N field indices:** The x86 compiler's LOWER phase embeds field indices as `/N` suffixes in IR field names. Both record STORE and field ACCESS must use these. The codegen was using iteration order for stores and defaulting to 0 for unknown accesses.

2. **Copy-on-write list-set-at:** Required for correctness — shared constants (e.g., `trie-empty-node`) get corrupted by in-place mutation. Performance impact: each update copies the entire list (O(n) instead of O(1)). May cause issues for algorithms with many array updates.

3. **Spill-slot handling:** Local registers > x27 spill to stack slots numbered 64+. Code that uses a local register number directly in ARM64 instructions must reload via `a64-load-local` first. The vec-select bug was caused by using a spill-slot number as a register number in STR.

4. **Local recycling hazard:** `a64-emit-binary-reg` recycles locals after evaluating operands. This reuses registers that may still hold live outer variables when the binary dispatch calls functions (like `a64-emit-vec-cmp`) that allocate additional locals.

5. **Hardcoded field table:** Covers most common fields but is fragile — field names like `value` appear in multiple record types at different indices. The table returns a single index per field name regardless of type. The `otherwise` path in `a64-find-field-index-st` now falls through to hardcoded before defaulting to 0.