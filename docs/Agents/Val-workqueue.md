# Val Work Queue

Agent: val
Model: Claude Opus 4.6 (1M context)
Workspace: D:\Projects\NewRepository-val
P4Client: BigWhite_Codex_val
Stream: //Codex/CodexMagic
Main client: BigWhite_Codex_val_main

## Session: 2026-06-22 -- RISC-V Cross-Test Uplift

Took over RISC-V cross-test work from blu. Started at 43 passing,
ended at 108 (net +65, 1 regression).

### CLs Submitted (all on main)

| CL | Fix | Impact |
|---|---|---|
| 5721 | rv-rt-str-eq s5 callee-save | +5 tests |
| 5726 | rv-emit-is-char-class BGEU | Fixes is-digit/is-letter/is-whitespace |
| 5727 | __start integer-return via __itoa | +19 integer-return tests |
| 5729 | rv-rt-text-split byte copy + delimiter offset | Fixes text-split runtime |
| 5739 | Lambda captures + nested lambda unwrapping | Enables closures with captures |
| 5757 | Partial application arity-aware dispatch | Fixes partial app for 2+ arg fns |
| 5758 | Sum-eq temp register safety | Fixes variant equality |
| 5761 | Closure-from-reg state threading | Fixes non-name closure calls |
| 5770 | Multi-level lambda unwrap + TCO through lambdas | TCO for 3+ param recursive fns |
| 5782 | User functions take priority over builtins | Fixes force/freeze/fail shadowing |
| 5790 | Builtin collision diagnostic + serial capture | [WARN] on name collisions |

### Stale Stage0 Note

build-output/bare-metal/Codex.cdx was stale at session start (from
a previous incomplete build). IR-CCE compilation produced only 14
bytes. Fixed by copying seed/Codex.cdx to Stage0. Any future agent
hitting "14 bytes IR" should check this first.

### Current Test Results (108 passing / ~152 total)

Passing (108): approx-eq, arithmetic, arm64-encoder, atomic-smoke,
audio-diffusion-test, av-codec-test, board-types, bounded-integer-ops,
bounds-proof, bounds-prover, bs3-smoke, cce-tier1, circbuf-test,
coap-encode, coap-packet, color-test, compliance-evidence,
compliance-report, crypto-test, edit-distance-test, effect-smoke,
esp32c6-drivers, eventbus-test, expr-calculator, factorial,
fe310-drivers, final-batch-test, fork-nested, fork-reclaim,
geometry-test, hamt-test, handler-smoke, history-test,
implicit-convert, infra-test, iterate-test, iterate-zip-test,
kvstore-test, lang-smoke, lazy-smoke, linear-branch, linear-smoke,
list-test, lwm2m-encode, matrix3-test, media-codec-test,
mini-bootstrap, mqtt-encode, mqtt-packet, mutable-smoke,
nrf52840-drivers, ota-gate-real, ota-state-machine, ota-update,
par-map, par-nested, parse-test, pi4-drivers, pipe-unique-test,
prose-consistency, prose-smoke, punctual-iot, punctual-quire,
punctual-smoke, qemu-virt-board, queue-test, real-approx,
real-saturating, real-trapping, record-smoke, rp2040-drivers,
sensor-data, smtp-md-test, sort-test, sprite-test, stats-wrap-test,
stm32f4-drivers, stringbuilder-test, stringutils-test,
suggested-width, synth-test, text-fold-indexed, textscan-test,
textsearch-test, thumb2-encoder, trie-prefix-test, truetype-test,
tuple-syntax, type-checker-test, typeclass-poly, typeclass-smoke,
ui-anim-test, ui-dialog-test, ui-focus-test, ui-scroll-test,
ui-sound-test, ui-theme-test, unit-family, unit-family-mixed,
unit-smoke, units-foreword, usb-msc-test, usb-test,
wavelet-sort-aliasing, with-timeout-test, xhci-enum-test.

### Remaining Failures (~14)

**Regression (1):**
- concurrent-test: lambda stored in list returns closure pointer
  instead of return value. Introduced by CL 5739 (lambda captures).
  Direct lambda calls work; only list-stored lambdas fail. The
  closure call returns s1 (heap bump pointer) instead of a0.

**SIMD/Vector (5):** mask-ops, vec-pattern, vec-reduce-add,
vector-basic, vector-f32, vector-int. Need SIMD register support.

**Heap exhaustion (2):** list-append-perf-N8-L7, ui-surface-test
(surface-new 80x80+). Quadratic O(n^2) from list-push copying
the entire list each time. Needs O(1) amortized list-push runtime.

**Other crashes (~4):** nrf9160-drivers (wrong value 14 vs 17),
stm32l4-drivers (crash), tls-test (crash), rasterizer-test (crash
at line 3), sound-test (crash at line 6 eq), truetype-render-test
(crash at line 7). Likely mix of heap exhaustion and remaining
codegen issues.

**Wrong values (2):** noise-test (fbm hash computation off),
raytracer-test (FP dist precision).

**Unimplemented (1):** try-smoke (try/fallback handler semantics
stubbed to just run body).

**Off-by-one (1):** ui-event-test (len=2 vs len=3).

### Compile failures (skip)

keyboard-layout-test, truetype-bridge-test, ui-font-test,
ui-icon-test, ui-orchestrator-test, vec-select -- all exceed plug
heap during IR codegen compilation.

### Architecture Notes

The RISC-V plug (codex/plugs/riscv/) has 3 source files:
- RiscVRuntime.codex: boot sequence, runtime functions (__str_eq,
  __itoa, print-line-uni, list ops, text ops, etc.)
- RiscVCodeGen.codex: IR-to-RISC-V codegen (expression emission,
  function framing, TCO, closure dispatch, partial application,
  lambda captures, effect handlers)
- RiscVPlug.codex: plug entry (TCP networking, IR parsing, wire
  output, diagnostic printing)

Plus RiscVElf.codex (ELF writer, used by compile-riscv.ps1 in
PowerShell not Codex) and RiscVEncoder.codex (foreword module
with instruction encoders).

Key design decisions:
- User functions always take priority over builtin dispatch
  (rv-emit-direct-call checks func-names first)
- Nested lambdas are unwrapped at both def-level and lambda-level
  (rv-unwrap-lambda-params/body)
- Lambda captures: free variable scan + closure storage + t2-based
  load at lambda entry
- Partial application: arity from FunTy chain, emit closure when
  args < arity
- Collision diagnostic: [WARN] lines emitted to serial, captured
  by run.ps1 -output flag
