# RISC-V Plug Codegen -- Work Queue

Agent: blu
Stream: //Codex/Mountain
Updated: 2026-06-22

## Current State

Full test sweep complete: 43 pass, ~20 fail, rest skip/no-expected.

Seed: E625476A (installed 2026-06-21, includes emit-ir-cce fix +
field-index + CCE 81).

## Bugs Fixed (Sessions 2-3, CLs 5516-5622)

| CL | Fix |
|---|---|
| 5516 | emit-ir-cce: revert streaming to single-pass CCE output |
| 5516 | rv-parse-field-index: CCE 78 -> 81 (slash) |
| 5516 | Seed rebuild with both fixes |
| 5522 | rv-count-ir-locals: IrRecord returns field count |
| 5523 | rv-collect-field-types: reversed order (list-push -> cons) |
| 5523 | rv-eval-record-fields: save to local before clamp |
| 5528 | ConstructedTy: resolve ctor tags for generic sum types |
| 5528 | rv-emit-name: ConstructedTy nullary ctor emission |
| 5543 | rv-emit-function-framed: TCO extra locals for save-args |
| 5550 | rv-emit-epilogue: use save-regs from prologue |
| 5551 | rv-emit-function-framed: always save all 10 callee-saved |
| 5571 | Memoize zero-param defs via s0 memo table |
| 5571 | rv-li-hi20: floor division for negative adjusted values |
| 5571 | __start: init s0 as memo base, s1 past memo table |
| 5573 | rv-is-memoizable: 64-slot bounds check |
| 5582 | rv-maybe-clamp: skip for full-range int-ty-default |
| 5591 | Spill safety: rv-store-record-fields, rv-store-to-dest, function return, rv-emit-record ptr |
| 5596 | Revert branch reset in rv-emit-if (not root cause) |
| 5612 | rv-emit-two-arg-call: fix a0 clobber when arg1 in a0 |
| 5617 | rv-eval-record-fields: load spilled local before clamp |
| 5647 | rv-emit-fork/rv-emit-await: wrap/unwrap Task on heap |
| 5647 | rv-bind-ctor-fields: spill-safe field load via temp |
| 5661 | rv-emit-tail-call: fix 2-arg swap clobber in simple TCO |
| 5665 | rv-emit-effect-op-call: direct JALR to handler, not closure dispatch |
| 5668 | rv-emit-effect-op-call: pass resume identity closure as extra arg |
| 5668 | rv-push/pop-handler-slots: use locals not SP (fixes SP corruption) |
| 5668 | __resume_id runtime function (identity trampoline) |
| 5686 | rv-emit-fork: call non-lambda thunks with dummy arg |

## Test Results (full sweep, 43 pass)

Passing: approx-eq, arithmetic, atomic-smoke, board-types,
bounded-integer-ops, bounds-proof, bounds-prover, bs3-smoke,
cce-tier1, circbuf-test, color-test, concurrent-test, crypto-test,
edit-distance-test, factorial, fork-reclaim, geometry-test,
handler-smoke, iterate-test, iterate-zip-test, linear-branch,
linear-smoke, mini-bootstrap, parse-test, pipe-unique-test,
queue-test, real-approx, real-saturating, real-trapping,
stringbuilder-test, stringutils-test, suggested-width,
text-fold-indexed, textscan-test, textsearch-test,
trie-prefix-test, tuple-syntax, typeclass-poly, typeclass-smoke,
unit-family, unit-family-mixed, unit-smoke, units-foreword.

Partial: effect-smoke (3/4), hamt-test (4/6), noise-test (4/5),
sort-test (16/18), stats-wrap-test (6/10).

Failing (crash/empty): fork-nested, lazy-smoke, list-append-perf,
mask-ops, matrix3-test, mutable-smoke, par-map, par-nested,
record-smoke (line 8+), type-checker-test, vec-pattern.

Failing (wrong values): kvstore-test, lang-smoke (line 12+),
try-smoke.

Skip: db-full-test, db-test.
Compile fail: keyboard-layout-test.

## Remaining Known Issues

### 1. Lambda captures not implemented

RISC-V lambdas are 8-byte closures (function pointer only, no captured
variables). This breaks: record-smoke (closure in record field),
lazy-smoke/mutable-smoke (lazy/force use closures), par-map/par-nested
(closures capturing loop vars). ~10 tests affected.

### 2. Nested handler scope

effect-smoke nested handler returns 170 instead of 115. The inner
`with Writer` (no clauses) may interact with handler slot state.

### 3. Spill-as-register (systemic)

The plug's register allocator uses spill slot numbers (64+) as
register references. Any instruction-emitting code that receives
a spill number and passes it to `rv-sd`/`rv-mv`/`rv-blt`/etc.
produces garbage (5-bit truncation: 64 & 31 = 0 = x0).

Fixed sites: rv-store-record-fields, rv-store-to-dest,
rv-emit-function-framed return, rv-eval-record-fields clamp,
rv-bind-ctor-fields.

Unfixed sites: binary expression operands (rv-emit-binary uses
rv-save-if-needed which checks rv-is-stable-reg, but spill slots
pass that check: 64 >= 18 is true!). This means spilled binary
operands appear "stable" but are actually spill slot numbers.

**rv-is-stable-reg bug**: `if r >= 18 then r < 28 else False`.
For spill slot 64: `64 >= 18` is True, `64 < 28` is False, so
returns False. Actually this IS correct -- spill slots are NOT
stable. The `rv-save-if-needed` would save them. But `rv-load-local`
for a spill returns a temp. So `rv-save-if-needed` on a temp would
re-save... actually this might work. Need to verify.

### 3. Closures in records + lazy/force (~5 tests)

Not yet investigated in sessions 2-3.

## Architecture Notes

### Memo Table (CL 5571)

s0 register holds memo table base (0x80100200). 64 slots x 8 bytes
= 512 bytes. Heap starts at s0 + 512 = 0x80100400. Zero-param
non-lambda non-act defs are memoized: first call computes and
caches, subsequent calls return cached pointer. Solves heap
exhaustion from global list reconstruction.

### Register Convention

s0 = memo table base (global, never modified after __start)
s1 = heap bump pointer (grows up)
s2-s11 = callee-saved locals (all saved in every prologue)
t3-t6 = temp registers (cycle via rv-alloc-temp)
a0-a7 = argument/return registers

### Spill Mechanism

When locals exceed s2-s11 (10 registers), rv-alloc-local returns
spill slot numbers (64, 65, ...). rv-store-local/rv-load-local
handle these via stack: SD/LD at [SP + 112 + (slot-64)*8].
Frame size is patched after emission to include spill space.
