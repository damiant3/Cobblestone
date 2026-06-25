# ARM64 Cross-Compilation Test Results

**Date**: 2026-06-24 12:51
**Seed**: `seed/Codex.cdx`
**Plug**: `codex/plugs/arm64/build-output/arm64-plug.cdx`
**Emulator**: Renode (`codex-arm64.repl`, Cortex-A53 + PL011)
**Parallel slots**: 4 (Renode timeout: 1s)
**Total time**: compile 524s + run 133s = ~11 min (down from 27.7 min)

## Summary

| Status | Count |
|--------|------:|
| PASS_EXPECTED | 124 |
| PASS_COMPILE_ONLY | 3 |
| FAIL | 10 |
| SKIPPED | 15 |
| **Total** | **152** |

## Detailed Results

| Test | Status | Notes |
|------|--------|-------|
| approx-eq | PASS_EXPECTED |  |
| arithmetic | PASS_EXPECTED |  |
| arm64-boot-test | PASS_COMPILE_ONLY |  |
| arm64-encoder | PASS_EXPECTED |  |
| arm64-web-server | PASS_COMPILE_ONLY |  |
| atomic-smoke | PASS_EXPECTED |  |
| audio-diffusion-test | PASS_EXPECTED |  |
| av-codec-test | PASS_EXPECTED |  |
| board-types | PASS_EXPECTED |  |
| bounded-integer-ops | PASS_EXPECTED |  |
| bounds-proof | PASS_EXPECTED |  |
| bounds-prover | PASS_EXPECTED |  |
| bs3-smoke | PASS_EXPECTED |  |
| cce-tier1 | PASS_EXPECTED |  |
| circbuf-test | PASS_EXPECTED |  |
| class-op-no-instance | SKIPPED | error test (frontend only) |
| coap-encode | PASS_EXPECTED |  |
| coap-packet | PASS_EXPECTED |  |
| color-test | PASS_EXPECTED |  |
| compliance-evidence | PASS_EXPECTED |  |
| compliance-report | PASS_EXPECTED |  |
| concurrent-test | PASS_EXPECTED |  |
| crypto-test | PASS_EXPECTED |  |
| db-full-test | SKIPPED | CDX1000 parse errors in db-full concat (~9304 lines). Token mismatch on string literal in Server.... |
| db-test | SKIPPED | Compiles clean but heap-scan overflows 2GB RAM at runtime (R10 past bare-metal-stack-top). 3 mult... |
| edit-distance-test | PASS_EXPECTED |  |
| effect-smoke | PASS_EXPECTED |  |
| esp32c6-drivers | PASS_EXPECTED |  |
| eventbus-test | PASS_EXPECTED |  |
| exc-div-zero | SKIPPED | fatal |
| exc-gpf | SKIPPED | fatal |
| exc-null-read | SKIPPED | fatal |
| exc-stack-heap | SKIPPED | fatal |
| expr-calculator | PASS_EXPECTED |  |
| factorial | PASS_EXPECTED |  |
| fe310-drivers | PASS_EXPECTED |  |
| final-batch-test | PASS_EXPECTED |  |
| fork-nested | PASS_EXPECTED |  |
| fork-reclaim | PASS_EXPECTED |  |
| geometry-test | PASS_EXPECTED |  |
| hamt-test | PASS_EXPECTED |  |
| handler-smoke | PASS_EXPECTED |  |
| helm-full-test | PASS_COMPILE_ONLY |  |
| history-test | PASS_EXPECTED |  |
| image-codec-test | SKIPPED | slow |
| implicit-convert | PASS_EXPECTED |  |
| infra-test | PASS_EXPECTED |  |
| iterate-test | PASS_EXPECTED |  |
| iterate-zip-test | PASS_EXPECTED |  |
| keyboard-layout-test | FAIL_OUTPUT |  |
| klondike-test | SKIPPED | slow |
| kvstore-test | PASS_EXPECTED |  |
| lang-smoke | PASS_EXPECTED |  |
| lazy-smoke | PASS_EXPECTED |  |
| let-effectful-bug | SKIPPED | slow |
| linear-branch | PASS_EXPECTED |  |
| linear-smoke | PASS_EXPECTED |  |
| list-append-perf-N8-L7 | PASS_EXPECTED |  |
| list-test | PASS_EXPECTED |  |
| lwm2m-encode | PASS_EXPECTED |  |
| mask-ops | PASS_EXPECTED |  |
| matrix3-test | PASS_EXPECTED |  |
| media-codec-test | PASS_EXPECTED |  |
| mini-bootstrap | PASS_EXPECTED |  |
| mqtt-encode | PASS_EXPECTED |  |
| mqtt-packet | PASS_EXPECTED |  |
| mutable-alias | SKIPPED | error test (frontend only) |
| mutable-smoke | PASS_EXPECTED |  |
| noise-test | PASS_EXPECTED |  |
| nrf52840-drivers | PASS_EXPECTED |  |
| nrf9160-drivers | PASS_EXPECTED |  |
| ota-gate-real | PASS_EXPECTED |  |
| ota-state-machine | PASS_EXPECTED |  |
| ota-update | PASS_EXPECTED |  |
| par-map | PASS_EXPECTED |  |
| par-nested | PASS_EXPECTED |  |
| parse-test | PASS_EXPECTED |  |
| parser-resync | SKIPPED | error test (frontend only) |
| pi4-drivers | PASS_EXPECTED |  |
| pipe-unique-test | PASS_EXPECTED |  |
| prose-consistency | PASS_EXPECTED |  |
| prose-smoke | PASS_EXPECTED |  |
| punctual-iot | PASS_EXPECTED |  |
| punctual-quire | PASS_EXPECTED |  |
| punctual-smoke | PASS_EXPECTED |  |
| qemu-virt-board | PASS_EXPECTED |  |
| queue-test | PASS_EXPECTED |  |
| rasterizer-test | PASS_EXPECTED |  |
| raytracer-test | PASS_EXPECTED |  |
| real-approx | PASS_EXPECTED |  |
| real-saturating | FAIL_OUTPUT | line 1: exp=[sum: 7.0] act=[sum: 0.0] |
| real-trapping | PASS_EXPECTED |  |
| record-smoke | PASS_EXPECTED |  |
| riscv-encoder | PASS_EXPECTED |  |
| riscv32c-encoder | PASS_EXPECTED |  |
| rp2040-drivers | PASS_EXPECTED |  |
| sensor-data | PASS_EXPECTED |  |
| smtp-md-test | PASS_EXPECTED |  |
| sort-test | PASS_EXPECTED |  |
| sound-test | PASS_EXPECTED |  |
| sprite-test | PASS_EXPECTED |  |
| stats-wrap-test | PASS_EXPECTED |  |
| stm32f4-drivers | PASS_EXPECTED |  |
| stm32l4-drivers | PASS_EXPECTED |  |
| stringbuilder-test | PASS_EXPECTED |  |
| stringutils-test | PASS_EXPECTED |  |
| suggested-width | PASS_EXPECTED |  |
| synth-test | PASS_EXPECTED |  |
| text-fold-indexed | PASS_EXPECTED |  |
| textscan-test | PASS_EXPECTED |  |
| textsearch-test | PASS_EXPECTED |  |
| thumb2-encoder | PASS_EXPECTED |  |
| tls-test | PASS_EXPECTED |  |
| trie-prefix-test | FAIL_OUTPUT | line 1: exp=[count:3] act=[count:2] |
| truetype-bridge-test | FAIL_OUTPUT | line 2: exp=[A: w=9 h=11 adv=8] act=[A: w=1 h=1 adv=0] |
| truetype-render-test | FAIL_OUTPUT |  |
| truetype-test | PASS_EXPECTED |  |
| try-smoke | FAIL_OUTPUT |  |
| tuple-syntax | PASS_EXPECTED |  |
| type-checker-test | PASS_EXPECTED |  |
| type-class-no-instance-gen | SKIPPED | error test (frontend only) |
| type-class-no-instance | SKIPPED | error test (frontend only) |
| typeclass-poly | PASS_EXPECTED |  |
| typeclass-smoke | PASS_EXPECTED |  |
| ui-anim-test | PASS_EXPECTED |  |
| ui-dialog-test | PASS_EXPECTED |  |
| ui-event-test | FAIL_OUTPUT |  |
| ui-focus-test | PASS_EXPECTED |  |
| ui-font-test | PASS_EXPECTED |  |
| ui-icon-test | PASS_EXPECTED |  |
| ui-layout-test | PASS_EXPECTED |  |
| ui-orchestrator-test | FAIL_COMPILE | compile failed or not attempted |
| ui-scroll-test | PASS_EXPECTED |  |
| ui-sound-test | PASS_EXPECTED |  |
| ui-surface-test | PASS_EXPECTED |  |
| ui-theme-test | PASS_EXPECTED |  |
| unit-family-mixed | PASS_EXPECTED |  |
| unit-family | PASS_EXPECTED |  |
| unit-smoke | PASS_EXPECTED |  |
| units-foreword | PASS_EXPECTED |  |
| usb-msc-test | PASS_EXPECTED |  |
| usb-test | PASS_EXPECTED |  |
| vec-pattern | PASS_EXPECTED |  |
| vec-reduce-add | PASS_EXPECTED |  |
| vec-select | FAIL_OUTPUT | line 1: exp=[lt-true: 3.0] act=[lt-true: 0.0] |
| vector-basic | PASS_EXPECTED |  |
| vector-f32 | FAIL_OUTPUT | line 1: exp=[add0: 7.0] act=[add0: '0.0] |
| vector-int | PASS_EXPECTED |  |
| watchdog-panic-probe | SKIPPED | deliberate infinite loop; not part of the standard sweep |
| wavelet-sort-aliasing | PASS_EXPECTED |  |
| with-timeout-test | PASS_EXPECTED |  |
| xhci-enum-test | PASS_EXPECTED |  |
