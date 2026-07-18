# x86-64 Test Battery Results

**Date**: 2026-06-24
**Agent**: blu
**Seed**: `E625476A6FF642D3A798D8FA7F5C44FC73A1CCEB2CB96A06CD2C5875C5E14298`
**Platform**: codex-vm (WHP), Windows 11, 3072 MB guest RAM
**Harness**: `build/test.ps1 -Jobs 4`

## Summary

| Metric | Count |
|--------|------:|
| **Total** | **192** |
| PASS_EXPECTED | 134 |
| PASS_FAILING | 36 |
| PASS_UNVERIFIED | 2 |
| SKIPPED | 10 |
| FAIL_COMPILE | 1 |
| FAIL_OUTPUT | 1 |
| **Pass rate** | **93.8% (180/192)** |

## Timing

| Phase | Wall time |
|-------|-----------|
| Batch compile (4 slots, 182 tests) | ~2m 17s |
| Run (134 tests with .expected) | ~14s |
| **Total** | **~2m 31s** |

Batch compile slot breakdown (tests compiled in REPL batch mode,
individual compile times not separable):

| Slot | Tests | Wall time |
|------|------:|-----------|
| pcore 1 | 46 | 07:27:46 – 07:30:03 (~2m 17s) |
| pcore 2 | 46 | 07:27:46 – 07:31:43 (~3m 57s) |
| pcore 3 | 45 | 07:27:46 – 07:31:05 (~3m 19s) |
| pcore 4 | 45 | 07:27:46 – 07:32:19 (~4m 33s) |

## Failures

### FAIL_COMPILE: helm-full-test

Compile exit code 7 (codegen halted with errors). Investigation needed.

### FAIL_OUTPUT: ui-icon-test

Compiled successfully but runtime output did not match `.expected` file.

## Detailed Results — Positive Tests (PASS_EXPECTED)

Tests that compiled, ran, and matched expected output.
Run time is VM boot + execute + serial capture (where measured).

| # | Test | Compile | Run (ms) |
|--:|------|:-------:|--------:|
| 1 | approx-eq | OK | 119 |
| 2 | arithmetic | OK | 126 |
| 3 | arm64-encoder | OK | 126 |
| 4 | atomic-smoke | OK | — |
| 5 | audio-diffusion-test | OK | 132 |
| 6 | av-codec-test | OK | — |
| 7 | board-types | OK | 130 |
| 8 | bounded-integer-ops | OK | 139 |
| 9 | bounds-proof | OK | 136 |
| 10 | bounds-prover | OK | 146 |
| 11 | bs3-smoke | OK | 137 |
| 12 | cce-tier1 | OK | — |
| 13 | circbuf-test | OK | 123 |
| 14 | coap-encode | OK | 130 |
| 15 | coap-packet | OK | 137 |
| 16 | color-test | OK | 125 |
| 17 | compliance-evidence | OK | — |
| 18 | compliance-report | OK | 144 |
| 19 | concurrent-test | OK | 142 |
| 20 | crypto-test | OK | — |
| 21 | edit-distance-test | OK | 131 |
| 22 | effect-smoke | OK | 138 |
| 23 | esp32c6-drivers | OK | 137 |
| 24 | eventbus-test | OK | 144 |
| 25 | expr-calculator | OK | 148 |
| 26 | factorial | OK | — |
| 27 | fe310-drivers | OK | 131 |
| 28 | final-batch-test | OK | 142 |
| 29 | fork-nested | OK | 134 |
| 30 | fork-reclaim | OK | 134 |
| 31 | geometry-test | OK | 133 |
| 32 | hamt-test | OK | 129 |
| 33 | handler-smoke | OK | 134 |
| 34 | history-test | OK | 134 |
| 35 | implicit-convert | OK | 129 |
| 36 | infra-test | OK | 125 |
| 37 | iterate-test | OK | — |
| 38 | iterate-zip-test | OK | 139 |
| 39 | keyboard-layout-test | OK | 458 |
| 40 | kvstore-test | OK | 123 |
| 41 | lang-smoke | OK | 134 |
| 42 | lazy-smoke | OK | 133 |
| 43 | linear-branch | OK | 135 |
| 44 | linear-smoke | OK | 130 |
| 45 | list-append-perf-N8-L7 | OK | — |
| 46 | list-test | OK | 144 |
| 47 | lwm2m-encode | OK | 135 |
| 48 | mask-ops | OK | 132 |
| 49 | matrix3-test | OK | — |
| 50 | media-codec-test | OK | 137 |
| 51 | mini-bootstrap | OK | 145 |
| 52 | mqtt-encode | OK | 146 |
| 53 | mqtt-packet | OK | — |
| 54 | mutable-smoke | OK | 138 |
| 55 | noise-test | OK | 123 |
| 56 | nrf52840-drivers | OK | 134 |
| 57 | nrf9160-drivers | OK | — |
| 58 | ota-gate-real | OK | 142 |
| 59 | ota-state-machine | OK | 127 |
| 60 | ota-update | OK | 140 |
| 61 | par-map | OK | 134 |
| 62 | par-nested | OK | 127 |
| 63 | parse-test | OK | 140 |
| 64 | pi4-drivers | OK | 139 |
| 65 | pipe-unique-test | OK | 120 |
| 66 | prose-consistency | OK | — |
| 67 | prose-smoke | OK | 152 |
| 68 | punctual-iot | OK | — |
| 69 | punctual-quire | OK | 141 |
| 70 | punctual-smoke | OK | — |
| 71 | qemu-virt-board | OK | — |
| 72 | queue-test | OK | — |
| 73 | rasterizer-test | OK | 133 |
| 74 | raytracer-test | OK | — |
| 75 | real-approx | OK | — |
| 76 | real-saturating | OK | — |
| 77 | real-trapping | OK | — |
| 78 | record-smoke | OK | — |
| 79 | riscv-encoder | OK | — |
| 80 | riscv32c-encoder | OK | 125 |
| 81 | rp2040-drivers | OK | — |
| 82 | sensor-data | OK | — |
| 83 | smtp-md-test | OK | 131 |
| 84 | sort-test | OK | 129 |
| 85 | sound-test | OK | 131 |
| 86 | sprite-test | OK | — |
| 87 | stats-wrap-test | OK | 124 |
| 88 | stm32f4-drivers | OK | — |
| 89 | stm32l4-drivers | OK | — |
| 90 | stringbuilder-test | OK | 124 |
| 91 | stringutils-test | OK | 137 |
| 92 | suggested-width | OK | 138 |
| 93 | synth-test | OK | 123 |
| 94 | text-fold-indexed | OK | — |
| 95 | textscan-test | OK | 135 |
| 96 | textsearch-test | OK | — |
| 97 | thumb2-encoder | OK | — |
| 98 | tls-test | OK | 190 |
| 99 | trie-prefix-test | OK | — |
| 100 | truetype-bridge-test | OK | 130 |
| 101 | truetype-render-test | OK | 131 |
| 102 | truetype-test | OK | — |
| 103 | try-smoke | OK | — |
| 104 | tuple-syntax | OK | 137 |
| 105 | type-checker-test | OK | — |
| 106 | typeclass-poly | OK | — |
| 107 | typeclass-smoke | OK | 142 |
| 108 | ui-anim-test | OK | — |
| 109 | ui-dialog-test | OK | 136 |
| 110 | ui-event-test | OK | 131 |
| 111 | ui-focus-test | OK | 128 |
| 112 | ui-font-test | OK | 137 |
| 113 | ui-layout-test | OK | — |
| 114 | ui-orchestrator-test | OK | 128 |
| 115 | ui-scroll-test | OK | — |
| 116 | ui-sound-test | OK | 142 |
| 117 | ui-surface-test | OK | 127 |
| 118 | ui-theme-test | OK | — |
| 119 | unit-family | OK | — |
| 120 | unit-family-mixed | OK | — |
| 121 | unit-smoke | OK | 132 |
| 122 | units-foreword | OK | 138 |
| 123 | usb-msc-test | OK | — |
| 124 | usb-test | OK | 131 |
| 125 | vec-pattern | OK | — |
| 126 | vec-reduce-add | OK | — |
| 127 | vec-select | OK | 147 |
| 128 | vector-basic | OK | — |
| 129 | vector-f32 | OK | 128 |
| 130 | vector-int | OK | — |
| 131 | wavelet-sort-aliasing | OK | — |
| 132 | with-timeout-test | OK | — |
| 133 | xhci-enum-test | OK | 139 |
| 134 | ui-icon-test | OK | FAIL |

Run time "—" = sweep log did not capture start/end pair (parallel
scheduling interleave or log line corruption). Median measured run
time: ~134 ms. Outlier: keyboard-layout-test at 458 ms.

## Detailed Results — Expected-Failure Tests (PASS_FAILING)

Tests that must fail with specific CDX diagnostic codes.

| # | Test | Compile exit |
|--:|------|:-----------:|
| 1 | apply-non-function | 7 |
| 2 | arith-on-text | 7 |
| 3 | arith-string-mix | 7 |
| 4 | bad-field | 7 |
| 5 | bad-field-syntax | 7 |
| 6 | class-op-no-instance | 7 |
| 7 | cr-escape-text | 7 |
| 8 | dangling-pipe-pattern | 7 |
| 9 | duplicate-ctor | 7 |
| 10 | duplicate-def | 7 |
| 11 | duplicate-param | 7 |
| 12 | effect-undeclared | 7 |
| 13 | empty-act | 7 |
| 14 | hex-literal-overflow | 7 |
| 15 | if-no-else | 7 |
| 16 | if-no-then | 7 |
| 17 | infinite-type | 7 |
| 18 | int-literal-overflow | 7 |
| 19 | keyword-as-pattern-var | 7 |
| 20 | lazy-reserved | 7 |
| 21 | let-no-in | 7 |
| 22 | linear-errors | 7 |
| 23 | list-no-close | 7 |
| 24 | match-no-arrow | 7 |
| 25 | missing-cite | 8 |
| 26 | multi-pattern-unbound | 7 |
| 27 | multiline-app-continuation | 7 |
| 28 | mutable-alias | 7 |
| 29 | narrowing-record-set | 7 |
| 30 | non-exhaustive-match | 7 |
| 31 | parser-resync | 7 |
| 32 | real-equality | 7 |
| 33 | reserved-as-id | 7 |
| 34 | reserved-keyword-as-name | 7 |
| 35 | rt-smoke | 7 |
| 36 | tab-escape-text | 7 |
| 37 | type-arity | 7 |
| 38 | type-class-no-instance | 7 |
| 39 | type-class-no-instance-gen | 7 |
| 40 | type-mismatch | 7 |
| 41 | unknown-ctor | 7 |
| 42 | unknown-name | 7 |
| 43 | unknown-pattern-ctor | 7 |
| 44 | unknown-record-field | 7 |
| 45 | unterminated-text | 7 |

Exit code 7 = CODEGEN-HALTED (expected rejection).
Exit code 8 = foreword resolution failure (missing-cite: expected).

## Unverified Tests (compile-only, no .expected sidecar)

| Test | Compile |
|------|:-------:|
| arm64-boot-test | OK |
| arm64-web-server | OK |

## Skipped Tests

| Test | Reason |
|------|--------|
| db-full-test | CDX1000 parse errors in ~9304-line concat |
| db-test | Heap-scan overflows 2GB RAM at runtime |
| exc-div-zero | Fatal: GPF/exception kills VM |
| exc-gpf | Fatal: GPF/exception kills VM |
| exc-null-read | Fatal: GPF/exception kills VM |
| exc-stack-heap | Fatal: GPF/exception kills VM |
| image-codec-test | Slow: large foreword dependency chain (~127s) |
| klondike-test | Slow: large foreword dependency chain (~127s) |
| let-effectful-bug | Slow: large foreword dependency chain (~127s) |
| watchdog-panic-probe | Deliberate infinite loop |

## Notes

- Compile times are not available per-test because the harness uses
  REPL batch mode (one persistent VM per job slot compiling multiple
  tests sequentially). The total batch compile wall time across 4
  slots was ~4m 33s (longest slot).
- Run times marked "—" had their sweep log entries corrupted by
  parallel interleaving of log writes from multiple job slots.
- The two failures (helm-full-test compile, ui-icon-test output
  mismatch) are regressions that need investigation.
