# Fester Work Queue

**Agent:** fester
**Updated:** 2026-06-21
**Pass rate:** ~71/140 testable (~51%), up from 36 baseline (28%)

## Session Summary (2026-06-21)

Started at 36/130 Renode-verified (28%). Ended at ~71/140 (~51%).

### Key Fixes

| CL | Fix | Impact |
|---|---|---|
| 5396 | P1-P6: register allocator, inline bitwise, overflow modes, match guards, __text_to_double | Foundation |
| 5401 | P3+P7: width-sorted field layout (reverted), SIMD ops, NEON encoder | +vec tests |
| 5441 | approx-eq CSINC polarity | +1 test |
| 5454 | **Revert let recycling** (register clobber in deep let chains) | +10 tests |
| 5462 | **Remove 2-arg TCO fast path** (swap clobber) | +edit-distance |
| 5470 | vec-binop register exhaustion fix | +vector-basic, vector-int |
| 5494 | **CRITICAL: field access priority** -- RecordTy field list over hardcoded table | +sensor-data, coap-packet, ctd-bugs |

### Remaining Failures (~70 tests)

**Integer-returning opening (~10 tests):** list-append-perf, qemu-virt-board, *-drivers.
Boot auto-print does CBZ X0 which skips integer 0. Needs opening
return-type dispatch in boot sequence.

**Program crash mid-execution (~15 tests):** cce-tier1, expr-calculator,
hamt-test, crypto-test, sort-test, sound-test, synth-test, noise-test.
Deep recursion exhausts stack or registers. No quick plug fix.

**Effect handler/fork/try stubs (~10 tests):** effect-smoke,
handler-smoke, fork-nested, par-map, try-smoke. Needs OS infrastructure.

**CCE raw output (~3 tests):** list-test, type-checker-test. Large
programs emit CCE bytes instead of Unicode. Unclear root cause.

**Record field access edge cases (~5 tests):** record-smoke line 8
(Box.apply closure field), mutable-smoke (crash after Thunk mutation).

**Vec comparison type dispatch (~5 tests):** mask-ops, vec-select,
vector-f32. Compiler annotates vec operands with IntegerTy instead of
VectorTy, so type-based dispatch doesn't fire.

**UI/render deep codegen (~10 tests):** ui-layout-test, ui-surface-test,
rasterizer-test, raytracer-test. Complex foreword code hits various
edge cases.

## Passing (~71 tests)

approx-eq, arithmetic, arm64-encoder, atomic-smoke, board-types,
bounded-integer-ops, bounds-proof, bounds-prover, bs3-smoke,
circbuf-test, coap-packet, color-test, compliance-report,
edit-distance-test, eventbus-test, factorial, geometry-test,
implicit-convert, inline-bug, iterate-test, iterate-zip-test,
lang-smoke, linear-branch, linear-smoke, list-bug, mini-bootstrap,
mqtt-packet, ota-gate-real, ota-update, pipe-unique-test,
prose-consistency, prose-smoke, punctual-iot, punctual-quire,
punctual-smoke, queue-test, real-approx, real-saturating, real-trapping,
reclist-bug, sensor-data, sprite-test, stringbuilder-test,
stringutils-test, suggested-width, text-fold-indexed, textscan-test,
textsearch-test, truetype-test, tuple-syntax, typeclass-poly,
typeclass-smoke, ui-anim-test, ui-dialog-test, ui-focus-test,
ui-scroll-test, ui-sound-test, unit-family, unit-family-mixed,
unit-smoke, units-foreword, usb-msc-test, usb-test, vec-pattern,
vec-reduce-add, vector-basic, vector-int, wavelet-sort-aliasing,
with-timeout-test, xhci-enum-test
