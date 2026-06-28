# ARM64 Cross-Compilation Test Results

**Date**: 2026-06-27 22:04
**Seed**: `seed/Codex.cdx`
**Plug**: `codex/plugs/arm64/build-output/arm64-plug.cdx`
**Emulator**: Renode (`codex-arm64.repl, Cortex-A53 + PL011`)
**Parallel slots**: 4
**Total time**: 13 min (781.9s)

## Summary

| Status | Count |
|--------|------:|
| PASS_EXPECTED | 132 |
| PASS_COMPILE_ONLY | 2 |
| FAIL | 2 |
| SKIPPED | 18 |
| **Total** | **154** |

## Detailed Results

| Test | Status | Compile (s) | Run (s) | Notes |
|------|--------|------------:|--------:|-------|
| approx-eq | PASS_EXPECTED | 1.5 | 12.9 |  |
| arithmetic | PASS_EXPECTED | 1.4 | 12.9 |  |
| arm64-boot-test | PASS_EXPECTED | 1.4 | 12.9 |  |
| arm64-encoder | PASS_EXPECTED | 2.1 | 12.9 |  |
| arm64-http-test | PASS_EXPECTED | 1.8 | 12.8 |  |
| arm64-web-server | PASS_COMPILE_ONLY | 22.3 | --- |  |
| atomic-smoke | PASS_EXPECTED | 1.7 | 12.9 |  |
| audio-diffusion-test | PASS_EXPECTED | 2.1 | 12.8 |  |
| av-codec-test | FAIL_OUTPUT | 2.9 | 12.9 | line 1: exp=[flac: size=67 magic=102] act=[flac: size=45 magic=102] |
| board-types | PASS_EXPECTED | 1.4 | 12.9 |  |
| bounded-integer-ops | PASS_EXPECTED | 1.4 | 12.8 |  |
| bounds-proof | PASS_EXPECTED | 1.5 | 12.9 |  |
| bounds-prover | PASS_EXPECTED | 1.5 | 12.9 |  |
| bs3-smoke | PASS_EXPECTED | 1.4 | 12.9 |  |
| cce-tier1 | PASS_EXPECTED | 1.8 | 13.8 |  |
| circbuf-test | PASS_EXPECTED | 1.9 | 12.8 |  |
| coap-encode | PASS_EXPECTED | 1.6 | 12.9 |  |
| coap-packet | PASS_EXPECTED | 1.6 | 12.8 |  |
| color-test | PASS_EXPECTED | 2 | 12.9 |  |
| compliance-evidence | PASS_EXPECTED | 2.4 | 12.8 |  |
| compliance-report | PASS_EXPECTED | 2 | 12.9 |  |
| concurrent-test | PASS_EXPECTED | 1.6 | 12.9 |  |
| crypto-test | PASS_EXPECTED | 2.1 | 12.9 |  |
| edit-distance-test | PASS_EXPECTED | 1.6 | 12.8 |  |
| effect-smoke | PASS_EXPECTED | 1.4 | 12.9 |  |
| esp32c6-drivers | PASS_EXPECTED | 1.9 | 12.9 |  |
| eventbus-test | PASS_EXPECTED | 2.2 | 12.9 |  |
| expr-calculator | PASS_EXPECTED | 2.1 | 12.9 |  |
| factorial | PASS_EXPECTED | 1.5 | 12.8 |  |
| fe310-drivers | PASS_EXPECTED | 1.8 | 12.9 |  |
| final-batch-test | PASS_EXPECTED | 6 | 12.9 |  |
| fork-nested | PASS_EXPECTED | 1.3 | 12.9 |  |
| fork-reclaim | PASS_EXPECTED | 1.4 | 12.9 |  |
| geometry-test | PASS_EXPECTED | 3 | 12.8 |  |
| hamt-test | PASS_EXPECTED | 2 | 12.9 |  |
| handler-smoke | FAIL_OUTPUT | 1.4 | 12.8 |  |
| helm-full-test | PASS_COMPILE_ONLY | 2.7 | --- |  |
| history-test | PASS_EXPECTED | 1.9 | 12.9 |  |
| implicit-convert | PASS_EXPECTED | 1.4 | 12.9 |  |
| infra-test | PASS_EXPECTED | 7.7 | 12.9 |  |
| iterate-test | PASS_EXPECTED | 1.5 | 13 |  |
| iterate-zip-test | PASS_EXPECTED | 1.4 | 12.9 |  |
| keyboard-layout-test | PASS_EXPECTED | 4.4 | 12.9 |  |
| kvstore-test | PASS_EXPECTED | 2.2 | 12.8 |  |
| lang-smoke | PASS_EXPECTED | 1.6 | 12.9 |  |
| lazy-smoke | PASS_EXPECTED | 1.6 | 12.9 |  |
| linear-branch | PASS_EXPECTED | 1.4 | 12.9 |  |
| linear-smoke | PASS_EXPECTED | 1.4 | 12.9 |  |
| list-append-perf-N8-L7 | PASS_EXPECTED | 1.5 | 12.8 |  |
| list-test | PASS_EXPECTED | 1.9 | 12.9 |  |
| lwm2m-encode | PASS_EXPECTED | 1.6 | 12.8 |  |
| mask-ops | PASS_EXPECTED | 1.4 | 12.9 |  |
| matrix3-test | PASS_EXPECTED | 1.9 | 12.9 |  |
| media-codec-test | PASS_EXPECTED | 4.1 | 12.9 |  |
| mini-bootstrap | PASS_EXPECTED | 1.5 | 12.8 |  |
| mqtt-encode | PASS_EXPECTED | 1.7 | 12.9 |  |
| mqtt-packet | PASS_EXPECTED | 1.8 | 12.9 |  |
| mutable-smoke | PASS_EXPECTED | 1.5 | 12.8 |  |
| noise-test | PASS_EXPECTED | 1.7 | 12.8 |  |
| nrf52840-drivers | PASS_EXPECTED | 2.5 | 12.9 |  |
| nrf9160-drivers | PASS_EXPECTED | 2.3 | 12.9 |  |
| ota-gate-real | PASS_EXPECTED | 1.6 | 12.9 |  |
| ota-state-machine | PASS_EXPECTED | 9.1 | 12.9 |  |
| ota-update | PASS_EXPECTED | 1.8 | 12.9 |  |
| par-map | PASS_EXPECTED | 1.3 | 12.9 |  |
| par-nested | PASS_EXPECTED | 1.4 | 12.8 |  |
| parse-test | PASS_EXPECTED | 1.5 | 12.9 |  |
| pipe-unique-test | PASS_EXPECTED | 1.6 | 12.9 |  |
| prose-consistency | PASS_EXPECTED | 1.4 | 12.9 |  |
| prose-smoke | PASS_EXPECTED | 1.4 | 12.8 |  |
| punctual-iot | PASS_EXPECTED | 1.6 | 12.9 |  |
| punctual-quire | PASS_EXPECTED | 2 | 12.9 |  |
| punctual-smoke | PASS_EXPECTED | 1.3 | 12.9 |  |
| qemu-virt-board | PASS_EXPECTED | 1.5 | 12.9 |  |
| queue-test | PASS_EXPECTED | 1.5 | 12.8 |  |
| rasterizer-test | PASS_EXPECTED | 2.7 | 12.9 |  |
| raytracer-test | PASS_EXPECTED | 5.1 | 12.8 |  |
| real-approx | PASS_EXPECTED | 1.4 | 12.9 |  |
| real-saturating | PASS_EXPECTED | 1.4 | 12.9 |  |
| real-trapping | PASS_EXPECTED | 1.3 | 12.9 |  |
| record-smoke | PASS_EXPECTED | 1.6 | 12.9 |  |
| riscv-encoder | PASS_EXPECTED | 2 | 12.9 |  |
| riscv32c-encoder | PASS_EXPECTED | 1.8 | 12.9 |  |
| rp2040-drivers | PASS_EXPECTED | 2.3 | 12.9 |  |
| sensor-data | PASS_EXPECTED | 1.6 | 12.9 |  |
| smtp-md-test | PASS_EXPECTED | 2.4 | 12.9 |  |
| sort-test | PASS_EXPECTED | 1.5 | 12.9 |  |
| sound-test | PASS_EXPECTED | 2.1 | 12.9 |  |
| sprite-test | PASS_EXPECTED | 2.7 | 12.8 |  |
| stats-wrap-test | PASS_EXPECTED | 1.9 | 12.9 |  |
| stm32f4-drivers | PASS_EXPECTED | 2 | 12.9 |  |
| stm32l4-drivers | PASS_EXPECTED | 1.9 | 12.9 |  |
| stringbuilder-test | PASS_EXPECTED | 1.7 | 12.9 |  |
| stringutils-test | PASS_EXPECTED | 1.9 | 12.8 |  |
| suggested-width | PASS_EXPECTED | 1.4 | 12.9 |  |
| synth-test | PASS_EXPECTED | 2.1 | 12.8 |  |
| text-fold-indexed | PASS_EXPECTED | 1.5 | 12.9 |  |
| textscan-test | PASS_EXPECTED | 1.4 | 13.3 |  |
| textsearch-test | PASS_EXPECTED | 1.6 | 12.9 |  |
| thumb2-encoder | PASS_EXPECTED | 1.7 | 12.9 |  |
| trie-prefix-test | PASS_EXPECTED | 2.2 | 12.8 |  |
| truetype-bridge-test | PASS_EXPECTED | 7.1 | 12.9 |  |
| truetype-render-test | PASS_EXPECTED | 5.8 | 12.9 |  |
| truetype-test | PASS_EXPECTED | 2.1 | 12.9 |  |
| try-smoke | PASS_EXPECTED | 1.3 | 12.8 |  |
| ttf-debug | PASS_EXPECTED | 2.9 | 13 |  |
| tuple-syntax | PASS_EXPECTED | 1.5 | 12.8 |  |
| type-checker-test | PASS_EXPECTED | 1.4 | 12.9 |  |
| typeclass-poly | PASS_EXPECTED | 1.3 | 12.9 |  |
| typeclass-smoke | PASS_EXPECTED | 1.6 | 12.8 |  |
| ui-anim-test | PASS_EXPECTED | 3.2 | 12.8 |  |
| ui-dialog-test | PASS_EXPECTED | 4.6 | 12.9 |  |
| ui-event-test | PASS_EXPECTED | 4.5 | 12.9 |  |
| ui-focus-test | PASS_EXPECTED | 3.1 | 12.9 |  |
| ui-font-test | PASS_EXPECTED | 3.4 | 12.9 |  |
| ui-icon-test | PASS_EXPECTED | 7.7 | 12.9 |  |
| ui-layout-test | PASS_EXPECTED | 4.7 | 12.9 |  |
| ui-scroll-test | PASS_EXPECTED | 2.3 | 12.9 |  |
| ui-sound-test | PASS_EXPECTED | 3.1 | 14.4 |  |
| ui-surface-test | PASS_EXPECTED | 5.8 | 12.9 |  |
| ui-theme-test | PASS_EXPECTED | 26.2 | 12.9 |  |
| unit-family | PASS_EXPECTED | 1.5 | 12.9 |  |
| unit-family-mixed | PASS_EXPECTED | 1.5 | 12.9 |  |
| unit-smoke | PASS_EXPECTED | 1.5 | 12.8 |  |
| units-foreword | PASS_EXPECTED | 1.6 | 12.9 |  |
| usb-msc-test | PASS_EXPECTED | 2.4 | 12.9 |  |
| usb-test | PASS_EXPECTED | 1.8 | 12.9 |  |
| vec-pattern | PASS_EXPECTED | 1.3 | 12.9 |  |
| vec-reduce-add | PASS_EXPECTED | 1.3 | 12.9 |  |
| vec-select | PASS_EXPECTED | 1.3 | 12.9 |  |
| vector-basic | PASS_EXPECTED | 1.4 | 12.8 |  |
| vector-f32 | PASS_EXPECTED | 1.3 | 12.9 |  |
| vector-int | PASS_EXPECTED | 1.3 | 12.9 |  |
| wavelet-sort-aliasing | PASS_EXPECTED | 1.5 | 12.9 |  |
| with-timeout-test | PASS_EXPECTED | 1.3 | 12.9 |  |
| xhci-enum-test | PASS_EXPECTED | 1.8 | 12.9 |  |
