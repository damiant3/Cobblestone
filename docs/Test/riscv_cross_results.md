# RISC-V Cross-Compilation Test Results

**Date**: 2026-06-24 11:06
**Architecture**: RISC-V 64 (RV64GC)
**Board**: Renode codex-riscv64 (NS16550 UART, 256 MB RAM @ 0x80000000)
**Seed**: `E625476A6FF642D3`
**Plug**: `riscv-plug.cdx` (`22F65EDC5230C20C`)
**Pipeline**: source.codex -> IR (x86 seed) -> RISC-V plug -> ELF64 -> Renode

---

## Summary

| Metric | Value |
|--------|------:|
| Compile pass rate | 131 / 137 (95.6%) |
| Runtime pass rate | 1 / 130 (0.8%) |

| Status | Count |
|--------|------:|
| PASS (compile + run matched) | 1 |
| PASS_COMPILE (no .expected) | 1 |
| FAIL_COMPILE | 6 |
| FAIL_OUTPUT (run mismatch) | 129 |
| FAIL_RUNTIME (no uart) | 0 |
| SKIP | 15 |
| **Total run** | **137** |
| **Total** | **152** |

---

## Per-Test Results

| # | Test | Status | Compile | Run | Detail |
|--:|------|--------|--------:|----:|--------|
| 1 | approx-eq | FAIL_OUTPUT | 1872ms | 3276ms | output mismatch |
| 2 | arithmetic | FAIL_OUTPUT | 2204ms | 3219ms | output mismatch |
| 3 | arm64-boot-test | PASS_COMPILE | 1960ms | - | ELF 9192 bytes |
| 4 | arm64-encoder | FAIL_OUTPUT | 3605ms | 2891ms | output mismatch |
| 5 | arm64-web-server | FAIL_COMPILE | 68081ms | - | compile failed |
| 6 | atomic-smoke | FAIL_OUTPUT | 1859ms | 3060ms | output mismatch |
| 7 | audio-diffusion-test | FAIL_OUTPUT | 4775ms | 2923ms | output mismatch |
| 8 | av-codec-test | FAIL_OUTPUT | 8991ms | 2924ms | output mismatch |
| 9 | board-types | FAIL_OUTPUT | 2059ms | 3012ms | output mismatch |
| 10 | bounded-integer-ops | FAIL_OUTPUT | 2170ms | 2893ms | output mismatch |
| 11 | bounds-proof | FAIL_OUTPUT | 2343ms | 2955ms | output mismatch |
| 12 | bounds-prover | FAIL_OUTPUT | 2416ms | 2940ms | output mismatch |
| 13 | bs3-smoke | FAIL_OUTPUT | 1979ms | 2976ms | output mismatch |
| 14 | cce-tier1 | FAIL_OUTPUT | 4185ms | 2976ms | output mismatch |
| 15 | circbuf-test | FAIL_OUTPUT | 4034ms | 3060ms | output mismatch |
| 16 | class-op-no-instance | SKIP | - | - | error test (frontend only) |
| 17 | coap-encode | FAIL_OUTPUT | 2938ms | 2986ms | output mismatch |
| 18 | coap-packet | FAIL_OUTPUT | 2724ms | 3056ms | output mismatch |
| 19 | color-test | FAIL_OUTPUT | 4017ms | 3020ms | output mismatch |
| 20 | compliance-evidence | FAIL_OUTPUT | 6036ms | 3008ms | output mismatch |
| 21 | compliance-report | FAIL_OUTPUT | 4655ms | 3047ms | output mismatch |
| 22 | concurrent-test | FAIL_OUTPUT | 2625ms | 2917ms | output mismatch |
| 23 | crypto-test | FAIL_OUTPUT | 6321ms | 2931ms | output mismatch |
| 24 | db-full-test | SKIP | - | - | CDX1000 parse errors in db-full concat (~9304 lines). Token mismatch on string literal in Server.codex. |
| 25 | db-test | SKIP | - | - | Compiles clean but heap-scan overflows 2GB RAM at runtime (R10 past bare-metal-stack-top). 3 multi-column rows allocate ~2GB. Needs heap optimization or larger RAM budget. |
| 26 | edit-distance-test | FAIL_OUTPUT | 2596ms | 2968ms | output mismatch |
| 27 | effect-smoke | FAIL_OUTPUT | 1856ms | 3101ms | output mismatch |
| 28 | esp32c6-drivers | FAIL_OUTPUT | 3848ms | 3045ms | output mismatch |
| 29 | eventbus-test | FAIL_OUTPUT | 4610ms | 3042ms | output mismatch |
| 30 | exc-div-zero | SKIP | - | - | fatal |
| 31 | exc-gpf | SKIP | - | - | fatal |
| 32 | exc-null-read | SKIP | - | - | fatal |
| 33 | exc-stack-heap | SKIP | - | - | fatal |
| 34 | expr-calculator | FAIL_OUTPUT | 4451ms | 3065ms | output mismatch |
| 35 | factorial | FAIL_OUTPUT | 2249ms | 2952ms | output mismatch |
| 36 | fe310-drivers | FAIL_OUTPUT | 3497ms | 3015ms | output mismatch |
| 37 | final-batch-test | FAIL_OUTPUT | 18763ms | 2845ms | output mismatch |
| 38 | fork-nested | FAIL_OUTPUT | 1674ms | 2855ms | output mismatch |
| 39 | fork-reclaim | FAIL_OUTPUT | 1813ms | 2883ms | output mismatch |
| 40 | geometry-test | FAIL_OUTPUT | 7356ms | 2740ms | output mismatch |
| 41 | hamt-test | FAIL_OUTPUT | 4385ms | 2699ms | output mismatch |
| 42 | handler-smoke | FAIL_OUTPUT | 1717ms | 2693ms | output mismatch |
| 43 | helm-full-test | FAIL_COMPILE | 2373ms | - | compile failed |
| 44 | history-test | FAIL_OUTPUT | 3250ms | 2757ms | output mismatch |
| 45 | image-codec-test | SKIP | - | - | slow |
| 46 | implicit-convert | FAIL_OUTPUT | 1737ms | 2770ms | output mismatch |
| 47 | infra-test | FAIL_OUTPUT | 22299ms | 2793ms | output mismatch |
| 48 | iterate-test | FAIL_OUTPUT | 2178ms | 2748ms | output mismatch |
| 49 | iterate-zip-test | FAIL_OUTPUT | 1932ms | 2751ms | output mismatch |
| 50 | keyboard-layout-test | FAIL_COMPILE | 15265ms | - | compile failed |
| 51 | klondike-test | SKIP | - | - | slow |
| 52 | kvstore-test | FAIL_OUTPUT | 4841ms | 2802ms | output mismatch |
| 53 | lang-smoke | FAIL_OUTPUT | 2538ms | 2669ms | output mismatch |
| 54 | lazy-smoke | FAIL_OUTPUT | 2497ms | 2637ms | output mismatch |
| 55 | let-effectful-bug | SKIP | - | - | slow |
| 56 | linear-branch | FAIL_OUTPUT | 1687ms | 2751ms | output mismatch |
| 57 | linear-smoke | FAIL_OUTPUT | 1661ms | 2707ms | output mismatch |
| 58 | list-append-perf-N8-L7 | FAIL_OUTPUT | 1637ms | 2797ms | output mismatch |
| 59 | list-test | FAIL_OUTPUT | 3159ms | 2787ms | output mismatch |
| 60 | lwm2m-encode | FAIL_OUTPUT | 2135ms | 2791ms | output mismatch |
| 61 | mask-ops | FAIL_OUTPUT | 1928ms | 2776ms | output mismatch |
| 62 | matrix3-test | FAIL_OUTPUT | 3360ms | 2740ms | output mismatch |
| 63 | media-codec-test | FAIL_OUTPUT | 19255ms | 2697ms | output mismatch |
| 64 | mini-bootstrap | PASS | 1620ms | 2710ms | output matched |
| 65 | mqtt-encode | FAIL_OUTPUT | 2492ms | 2724ms | output mismatch |
| 66 | mqtt-packet | FAIL_OUTPUT | 2369ms | 2732ms | output mismatch |
| 67 | mutable-alias | SKIP | - | - | error test (frontend only) |
| 68 | mutable-smoke | FAIL_OUTPUT | 1984ms | 2738ms | output mismatch |
| 69 | noise-test | FAIL_OUTPUT | 2988ms | 2755ms | output mismatch |
| 70 | nrf52840-drivers | FAIL_OUTPUT | 11565ms | 2740ms | output mismatch |
| 71 | nrf9160-drivers | FAIL_OUTPUT | 6338ms | 2628ms | output mismatch |
| 72 | ota-gate-real | FAIL_OUTPUT | 2374ms | 2869ms | output mismatch |
| 73 | ota-state-machine | FAIL_OUTPUT | 24862ms | 2853ms | output mismatch |
| 74 | ota-update | FAIL_OUTPUT | 2904ms | 2793ms | output mismatch |
| 75 | par-map | FAIL_OUTPUT | 1631ms | 2744ms | output mismatch |
| 76 | par-nested | FAIL_OUTPUT | 1597ms | 2808ms | output mismatch |
| 77 | parse-test | FAIL_OUTPUT | 2640ms | 2768ms | output mismatch |
| 78 | parser-resync | SKIP | - | - | error test (frontend only) |
| 79 | pi4-drivers | FAIL_OUTPUT | 3427ms | 2758ms | output mismatch |
| 80 | pipe-unique-test | FAIL_OUTPUT | 2209ms | 3123ms | output mismatch |
| 81 | prose-consistency | FAIL_OUTPUT | 2366ms | 3084ms | output mismatch |
| 82 | prose-smoke | FAIL_OUTPUT | 1832ms | 2924ms | output mismatch |
| 83 | punctual-iot | FAIL_OUTPUT | 2360ms | 2907ms | output mismatch |
| 84 | punctual-quire | FAIL_OUTPUT | 3590ms | 2917ms | output mismatch |
| 85 | punctual-smoke | FAIL_OUTPUT | 1694ms | 2952ms | output mismatch |
| 86 | qemu-virt-board | FAIL_OUTPUT | 1979ms | 2932ms | output mismatch |
| 87 | queue-test | FAIL_OUTPUT | 1882ms | 2983ms | output mismatch |
| 88 | rasterizer-test | FAIL_OUTPUT | 5892ms | 2979ms | output mismatch |
| 89 | raytracer-test | FAIL_OUTPUT | 14300ms | 3013ms | output mismatch |
| 90 | real-approx | FAIL_OUTPUT | 1789ms | 2958ms | output mismatch |
| 91 | real-saturating | FAIL_OUTPUT | 1823ms | 2989ms | output mismatch |
| 92 | real-trapping | FAIL_OUTPUT | 1800ms | 2914ms | output mismatch |
| 93 | record-smoke | FAIL_OUTPUT | 2753ms | 2970ms | output mismatch |
| 94 | riscv-encoder | FAIL_OUTPUT | 3385ms | 2995ms | output mismatch |
| 95 | riscv32c-encoder | FAIL_OUTPUT | 3393ms | 3114ms | output mismatch |
| 96 | rp2040-drivers | FAIL_OUTPUT | 6565ms | 2913ms | output mismatch |
| 97 | sensor-data | FAIL_OUTPUT | 2715ms | 2904ms | output mismatch |
| 98 | smtp-md-test | FAIL_OUTPUT | 4980ms | 2909ms | output mismatch |
| 99 | sort-test | FAIL_OUTPUT | 2355ms | 2961ms | output mismatch |
| 100 | sound-test | FAIL_OUTPUT | 4747ms | 2757ms | output mismatch |
| 101 | sprite-test | FAIL_OUTPUT | 5985ms | 2738ms | output mismatch |
| 102 | stats-wrap-test | FAIL_OUTPUT | 3438ms | 2711ms | output mismatch |
| 103 | stm32f4-drivers | FAIL_OUTPUT | 4169ms | 2706ms | output mismatch |
| 104 | stm32l4-drivers | FAIL_OUTPUT | 4740ms | 2705ms | output mismatch |
| 105 | stringbuilder-test | FAIL_OUTPUT | 2144ms | 2757ms | output mismatch |
| 106 | stringutils-test | FAIL_OUTPUT | 2768ms | 2711ms | output mismatch |
| 107 | suggested-width | FAIL_OUTPUT | 1592ms | 2708ms | output mismatch |
| 108 | synth-test | FAIL_OUTPUT | 3974ms | 2786ms | output mismatch |
| 109 | text-fold-indexed | FAIL_OUTPUT | 1816ms | 2754ms | output mismatch |
| 110 | textscan-test | FAIL_OUTPUT | 1874ms | 2732ms | output mismatch |
| 111 | textsearch-test | FAIL_OUTPUT | 2413ms | 2680ms | output mismatch |
| 112 | thumb2-encoder | FAIL_OUTPUT | 3042ms | 2701ms | output mismatch |
| 113 | tls-test | FAIL_OUTPUT | 6597ms | 2700ms | output mismatch |
| 114 | trie-prefix-test | FAIL_OUTPUT | 4059ms | 2707ms | output mismatch |
| 115 | truetype-bridge-test | FAIL_OUTPUT | 23551ms | 2862ms | output mismatch |
| 116 | truetype-render-test | FAIL_OUTPUT | 18953ms | 2750ms | output mismatch |
| 117 | truetype-test | FAIL_OUTPUT | 3481ms | 2736ms | output mismatch |
| 118 | try-smoke | FAIL_OUTPUT | 1616ms | 2737ms | output mismatch |
| 119 | tuple-syntax | FAIL_OUTPUT | 1787ms | 2692ms | output mismatch |
| 120 | type-checker-test | FAIL_OUTPUT | 1576ms | 2683ms | output mismatch |
| 121 | type-class-no-instance-gen | SKIP | - | - | error test (frontend only) |
| 122 | type-class-no-instance | SKIP | - | - | error test (frontend only) |
| 123 | typeclass-poly | FAIL_OUTPUT | 1578ms | 2661ms | output mismatch |
| 124 | typeclass-smoke | FAIL_OUTPUT | 2842ms | 2702ms | output mismatch |
| 125 | ui-anim-test | FAIL_OUTPUT | 6974ms | 2736ms | output mismatch |
| 126 | ui-dialog-test | FAIL_OUTPUT | 11122ms | 2708ms | output mismatch |
| 127 | ui-event-test | FAIL_OUTPUT | 11318ms | 2745ms | output mismatch |
| 128 | ui-focus-test | FAIL_OUTPUT | 6807ms | 2682ms | output mismatch |
| 129 | ui-font-test | FAIL_COMPILE | 11717ms | - | compile failed |
| 130 | ui-icon-test | FAIL_COMPILE | 22481ms | - | compile failed |
| 131 | ui-layout-test | FAIL_OUTPUT | 11315ms | 2640ms | output mismatch |
| 132 | ui-orchestrator-test | FAIL_COMPILE | 601017ms | - | compile failed |
| 133 | ui-scroll-test | FAIL_OUTPUT | 4729ms | 2828ms | output mismatch |
| 134 | ui-sound-test | FAIL_OUTPUT | 7271ms | 2871ms | output mismatch |
| 135 | ui-surface-test | FAIL_OUTPUT | 15980ms | 2872ms | output mismatch |
| 136 | ui-theme-test | FAIL_OUTPUT | 77850ms | 2670ms | output mismatch |
| 137 | unit-family-mixed | FAIL_OUTPUT | 1807ms | 2677ms | output mismatch |
| 138 | unit-family | FAIL_OUTPUT | 1676ms | 2694ms | output mismatch |
| 139 | unit-smoke | FAIL_OUTPUT | 1671ms | 2701ms | output mismatch |
| 140 | units-foreword | FAIL_OUTPUT | 2116ms | 2687ms | output mismatch |
| 141 | usb-msc-test | FAIL_OUTPUT | 5294ms | 2664ms | output mismatch |
| 142 | usb-test | FAIL_OUTPUT | 3298ms | 2618ms | output mismatch |
| 143 | vec-pattern | FAIL_OUTPUT | 1569ms | 2645ms | output mismatch |
| 144 | vec-reduce-add | FAIL_OUTPUT | 1573ms | 2644ms | output mismatch |
| 145 | vec-select | FAIL_OUTPUT | 1582ms | 2611ms | output mismatch |
| 146 | vector-basic | FAIL_OUTPUT | 1638ms | 2668ms | output mismatch |
| 147 | vector-f32 | FAIL_OUTPUT | 1554ms | 2678ms | output mismatch |
| 148 | vector-int | FAIL_OUTPUT | 1584ms | 2645ms | output mismatch |
| 149 | watchdog-panic-probe | SKIP | - | - | deliberate infinite loop; not part of the standard sweep |
| 150 | wavelet-sort-aliasing | FAIL_OUTPUT | 1714ms | 2694ms | output mismatch |
| 151 | with-timeout-test | FAIL_OUTPUT | 1556ms | 2747ms | output mismatch |
| 152 | xhci-enum-test | FAIL_OUTPUT | 3493ms | 2729ms | output mismatch |

---

## Compile Failure Details

### arm64-web-server

```
  Net::MessageFraming (.\codex\os\net\MessageFraming.codex)
  Net::TcpTransport (.\codex\os\net\TcpTransport.codex)
  Net::Arm64NetIO (.\codex\os\net\Arm64NetIO.codex)
  Foreword::CCE (.\codex\foreword\core\CCE.codex)
  Works::Http (.\apps\works\Http.codex)
These chapters are cited by bundled code but missing from the app build script.
Add them to the build script's chapter list, or remove the cites.
True
[riscv-compile] Running RISC-V codegen plug...
[riscv-run] Input: 2512531 bytes from D:\Projects\NewRepository-val\codex\plugs\riscv\build-output\last-compile.ir
[riscv-run] Listening on port 9100
[riscv-run] Plug connected
[riscv-run] Sent 2512531 bytes (IR text)
[riscv-run] OK: D:\Projects\NewRepository-val\codex\plugs\riscv\build-output\last-compile.riscv.bin (0 bytes)
compile-riscv.ps1: Exception calling "ToInt32" with "2" argument(s): "Index was out of range. Must be non-negative and less than the size of the collection. (Parameter 'startIndex')"
```

### helm-full-test

```
  UI::Event (.\codex\foreword\ui\Event.codex)
  UI::Selection (.\codex\foreword\ui\Selection.codex)
  UI::TextField (.\codex\foreword\ui\TextField.codex)
  UI::Scroll (.\codex\foreword\ui\Scroll.codex)
  UI::Focus (.\codex\foreword\ui\Focus.codex)
  UI::FilterableList (.\codex\foreword\ui\FilterableList.codex)
  UI::SearchBar (.\codex\foreword\ui\SearchBar.codex)
  UI::StatusBadge (.\codex\foreword\ui\StatusBadge.codex)
  Helm::RiverPage (.\apps\helm\RiverPage.codex)
  Helm::BridgePage (.\apps\helm\BridgePage.codex)
  Foreword::Console (.\codex\foreword\core\Console.codex)
These chapters are cited by bundled code but missing from the app build script.
Add them to the build script's chapter list, or remove the cites.
True
FAIL: IR compile step exited 4; see D:\Projects\NewRepository-val\codex\plugs\riscv\build-output\compile-ir.log
```

### keyboard-layout-test

```
[riscv-compile] Compiling D:\Projects\NewRepository-val\codex\test\keyboard-layout-test.codex to IR...
kernel: build-output\bare-metal\Codex.cdx [E625476A6FF642D3]
WARNING: compile.ps1 resolved 2 chapter(s) not in bundled source:
  Foreword::KeyboardLayout (.\codex\foreword\core\KeyboardLayout.codex)
  Foreword::CCE (.\codex\foreword\core\CCE.codex)
These chapters are cited by bundled code but missing from the app build script.
Add them to the build script's chapter list, or remove the cites.
True
[riscv-compile] Running RISC-V codegen plug...
[riscv-run] Input: 351978 bytes from D:\Projects\NewRepository-val\codex\plugs\riscv\build-output\last-compile.ir
[riscv-run] Listening on port 9100
[riscv-run] Plug connected
[riscv-run] Sent 351978 bytes (IR text)
[riscv-run] OK: D:\Projects\NewRepository-val\codex\plugs\riscv\build-output\last-compile.riscv.bin (0 bytes)
compile-riscv.ps1: Exception calling "ToInt32" with "2" argument(s): "Index was out of range. Must be non-negative and less than the size of the collection. (Parameter 'startIndex')"
```

### ui-font-test

```
  Math::Geometry (.\codex\foreword\math\Geometry.codex)
  Game::Bresenham (.\codex\foreword\game\Bresenham.codex)
  Game::Rasterizer (.\codex\foreword\game\Rasterizer.codex)
  UI::Font (.\codex\foreword\ui\Font.codex)
  Game::Color (.\codex\foreword\game\Color.codex)
These chapters are cited by bundled code but missing from the app build script.
Add them to the build script's chapter list, or remove the cites.
True
[riscv-compile] Running RISC-V codegen plug...
[riscv-run] Input: 219771 bytes from D:\Projects\NewRepository-val\codex\plugs\riscv\build-output\last-compile.ir
[riscv-run] Listening on port 9100
[riscv-run] Plug connected
[riscv-run] Sent 219771 bytes (IR text)
[riscv-run] OK: D:\Projects\NewRepository-val\codex\plugs\riscv\build-output\last-compile.riscv.bin (0 bytes)
compile-riscv.ps1: Exception calling "ToInt32" with "2" argument(s): "Index was out of range. Must be non-negative and less than the size of the collection. (Parameter 'startIndex')"
```

### ui-icon-test

```
  Math::Geometry (.\codex\foreword\math\Geometry.codex)
  Game::Bresenham (.\codex\foreword\game\Bresenham.codex)
  Game::Rasterizer (.\codex\foreword\game\Rasterizer.codex)
  UI::Icon (.\codex\foreword\ui\Icon.codex)
  Game::Color (.\codex\foreword\game\Color.codex)
These chapters are cited by bundled code but missing from the app build script.
Add them to the build script's chapter list, or remove the cites.
True
[riscv-compile] Running RISC-V codegen plug...
[riscv-run] Input: 670258 bytes from D:\Projects\NewRepository-val\codex\plugs\riscv\build-output\last-compile.ir
[riscv-run] Listening on port 9100
[riscv-run] Plug connected
[riscv-run] Sent 670258 bytes (IR text)
[riscv-run] OK: D:\Projects\NewRepository-val\codex\plugs\riscv\build-output\last-compile.riscv.bin (0 bytes)
compile-riscv.ps1: Exception calling "ToInt32" with "2" argument(s): "Index was out of range. Must be non-negative and less than the size of the collection. (Parameter 'startIndex')"
```

### ui-orchestrator-test

```
  UI::Render (.\codex\foreword\ui\Render.codex)
  UI::Surface (.\codex\foreword\ui\Surface.codex)
  UI::Event (.\codex\foreword\ui\Event.codex)
  UI::Binding (.\codex\foreword\ui\Binding.codex)
  Math::Spline (.\codex\foreword\math\Spline.codex)
  Foreword::ListUtils (.\codex\foreword\core\ListUtils.codex)
  UI::Animation (.\codex\foreword\ui\Animation.codex)
  UI::Overlay (.\codex\foreword\ui\Overlay.codex)
  UI::Sound (.\codex\foreword\ui\Sound.codex)
  UI::Orchestrator (.\codex\foreword\ui\Orchestrator.codex)
  Game::Color (.\codex\foreword\game\Color.codex)
These chapters are cited by bundled code but missing from the app build script.
Add them to the build script's chapter list, or remove the cites.
False
FAIL: IR compile step exited 3; see D:\Projects\NewRepository-val\codex\plugs\riscv\build-output\compile-ir.log
```

---

## Output Mismatch Samples

Showing first 5 output mismatches for diagnosis.

### approx-eq

**Actual** (first 5 lines):
```
(empty)
```
**Expected** (first 5 lines):
```
approx-same: PASS
approx-zero: PASS
approx-far: PASS
exact-same: PASS
exact-zero: PASS
```

### arithmetic

**Actual** (first 5 lines):
```
(empty)
```
**Expected** (first 5 lines):
```
clamp: 37
absorb: 12
match: answer/one/other
clamping: 100
even: yes
```

### arm64-encoder

**Actual** (first 5 lines):
```
(empty)
```
**Expected** (first 5 lines):
```
add x0,x1,x2=8B020020
sub x3,x4,x5=CB050083
mul x6,x7,x8=9B087CE6
sdiv x9,x10,x11=9ACB0D49
and x0,x1,x2=8A020020
```

### atomic-smoke

**Actual** (first 5 lines):
```
(empty)
```
**Expected** (first 5 lines):
```
fence=ok
load-after-store=42
cas=True
xchg=77
xadd=100
```

### audio-diffusion-test

**Actual** (first 5 lines):
```
(empty)
```
**Expected** (first 5 lines):
```
peak=1000 rms=19
env=2 e0=15 e1=3
analyze: peak=1000 rms=19 centroid=3Hz bpm=120 dur=2ms
linear: steps=10 beta[0]=1 beta[-1]=20 abar[-1]=901
cosine: steps=10 beta[0]=1 beta[-1]=722 abar[-1]=27
```

