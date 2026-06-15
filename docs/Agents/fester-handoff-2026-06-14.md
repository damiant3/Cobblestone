# Fester Handoff — 2026-06-14

## Session Summary

Massive two-day session (2026-06-13/14) covering app cleanup, compiler features, IoT platform, and publication.

## What Shipped

### Language Features
- **`revised` keyword** (CL 4215/4217) — safe multi-field record update with snapshot-then-mutate desugaring. Eliminates `__record-set` aliasing hazard. Evaluates all RHS into temporaries before any mutation.
- **CDX6020 mutation-in-constructor warning** (CL 4225/4227) — type checker walks record constructor fields for `__record-set` calls and warns.
- **Keyword-as-pattern-variable fix** (CL 4017/4018) — `is-keyword` check in `parse-pattern` emits CDX1060.
- **`=Dict` parser bug fix** (CL 4288/4289) — `class`/`instance` keywords used as record field names no longer create phantom type class definitions. Root cause: `scan-class-instance-defs` didn't check that the next token was a name.

### IoT Platform
- **Board drivers** — STM32F4 (RM0090), ESP32-C6 (TRM), Pi4 (BCM2711), QEMU virt. Full GPIO/UART/SPI/I2C. All 4 have smoke tests passing.
- **OTA state machine** (CL 4271) — two-gate verification (Gate A streaming, Gate B 5-phase), A/B boot slots, anti-rollback, 6-path test with 11 assertions.
- **IoT tests** — 9 passing: MQTT, CoAP, LwM2M, OTA, compliance evidence, board drivers x4.
- **ComplianceBuild + CRA/ETSI/IEC62443 mapping docs** — Phase 3 certification is 100% done (CL 4314 landed the automated emit hook).

### App Cleanup
- ~950 compile errors cleared across ~190 files
- `Nothing` → `None` (185 sites, 97 files)
- Tuple-style variant constructors → multi-arg (32 ctors, 18 files)
- `is []` patterns → `if list-length == 0` (65 sites, 26 files)
- Reserved keyword renames, tab escapes, multiline app joins
- 5 quire-map entries, 33 ListUtils/StringUtils cites added
- `list-map`, `list-take`, `list-drop` added to ListUtils

### Build Infrastructure
- **BVT** (`build/bvt.ps1`) — 16-test minimal gate, 18s vs 280s full battery
- **`build.ps1` uses BVT** — total gate time 400s → 140s
- **Seed rebuilt** with `revised`, foreword additions, CCE Tier 1, `=Dict` fix

### Foreword Modules
- ElasticHash, FunnelHash, ElasticBloom (open-addressing hash tables from Farach-Colton et al. 2025)
- `list-map`, `list-take`, `list-drop`, `list-filter` in ListUtils

### Reference Docs
- AMD SVM Hypervisor Patterns (from Type2-AMD-HV repo)
- Optimal Bounds for Open Addressing (Farach-Colton/Krapivin/Kuszmaul 2025)

### Publication
- Git pushed to GitHub (master) and GitLab (main), commit 6b01c5c1
- agentlanguages.dev entry is live, reply sent to Alasdair

## What's Shelved
- **CL 4219** — record-constructor safety pass (evaluate all fields into temporaries before construction). Breaks text round-trip. Investigation showed the codegen ALREADY evaluates fields into locals; the issue is `__record-set` mutating through pointer aliasing. The `revised` keyword and CDX6020 warning cover the use case without this change.

## What's Open

### IoT (Phase 1 — Backend Port)
- ARM64/RISC-V plugs need IR coverage: match, record, closures, effects
- Cross-compilation: board driver tests as ELF binaries on QEMU
- Physical hardware test bench: STM32F4 Discovery + USB-serial

### Compiler
- Heap-reduction campaign deferred: LOWER→RESOLVE scavenges, CHECK+LOWER per-def fusion
- Backlog item 5: six library bugs (decimal, toml, yaml, numeric, path, number-theory)

### Apps
- Remaining type mismatches from nested `__record-set` — use `revised` instead
- `write-export-str` in designer — WASM intrinsics not yet implemented

## Known Issues
- STM32F4 board file was overwritten by val's CL 4239 copy-up (CodexMagic branched before fester's drivers landed). Restored in CL 4263/4265. Watch for this pattern on future copy-ups.
- Full test battery shows 1 fail (stm32f4-drivers output mismatch) — trailing newline from p4 text type, not a real failure.
- `test.ps1 -All` has pre-existing failures from sha256 digest mismatches and mass FAIL_COMPILE — NOT regressions.

## Proof Build
Main workspace, force-synced and cleaned: **hard fixed point in one pass, 164.7s, all gates green.** Seed hash: `F868A9D75F91ED805C16A628E0E909D18200D7F85A186AB4DED2AD3EC3CBB1FE`.
