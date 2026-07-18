# RISC-V 64 Cross-Compilation Test Results

**Date**: 2026-06-26
**Seed**: `seed/Codex.cdx`
**Plug**: `codex/plugs/riscv/build-output/riscv-plug.cdx`
**Emulator**: QEMU virt (RV64GC, NS16550 UART, 3 GB RAM)
**Agent**: val
**Last CL**: 6137

## Summary

**134/134 PASS (100%)**

| Status | Count |
|--------|------:|
| PASS_EXPECTED | 134 |
| COMPILE_ONLY | 2 |
| SKIPPED | 17 |
| FAIL | 0 |
| **Total eligible** | **153** |

Compile-only tests (no `.expected` sidecar): `arm64-web-server`,
`helm-full-test`. Both are server tests that block on network I/O.

## Battery Details

```
IR compile:    146.8s (136/136 blocks, 14 chunks of 10)
Plug codegen:   29.6s (136 wire blocks)
ELF assembly:    1.4s (136 files)
QEMU run:       53.2s (134 tests, 4 parallel slots)
Total:         231.1s
```

## Codegen Fixes (CLs 5997-6137)

### Phase 1: Basic codegen (CLs 5997-6087)

| CL | Fix | Impact |
|----|-----|--------|
| 5997 | Stack address (0x8FFF0000 for 256 MB board) | 1 → 108 pass |
| 6012 | `__start` return-value echo (beq skip for Nothing) | Spurious output eliminated |
| 6013 | Comparison register reuse in if-chains | +12 tests |
| 6074 | Vector f32 codegen (3-way dispatch, register clobbering) | +1 test |
| 6087 | Missing `ret` in 28 runtime helpers + `IrPowInt` handler | +28 tests |

### Phase 2: Map and debugging (CLs 6096-6102)

| CL | Fix | Impact |
|----|-----|--------|
| 6096 | Wire protocol func-offsets × 4 (byte offsets, matching ARM64) | GDB breakpoints work |
| 6098 | Compile-time opening result print (ported from ARM64) | +1 test |
| 6102 | Map addresses: remove ELF text-section offset from .bin maps | Exact GDB resolution |

### Phase 3: 12-bit immediate overflow (CLs 6113-6126)

RISC-V ADDI/SD/LD instructions use 12-bit signed immediates
(range [-2048, +2047]). Values outside this range silently wrap,
flipping the sign. ARM64 uses unsigned 12-bit SUB (range 0-4095)
so it never hits this.

| CL | Fix | Impact |
|----|-----|--------|
| 6113 | Mutable code buffer (ported from ARM64) + large-frame prologue (LUI+ADDI+SUB for frames > 2040 bytes) | Structural fix |
| 6126 | Spill slot offset overflow: LI+ADD+SD/LD for offsets > 2040 | +2 tests (ui-icon, ui-font) |

### Phase 4: Missing runtime helpers (CLs 6132-6134)

| CL | Fix | Impact |
|----|-----|--------|
| 6132 | `alloc-bytes` runtime helper (was missing — heap pointer never bumped, buffers aliased) | German umlauts fixed |
| 6133 | `__cce_to_unicode` / `__unicode_to_cce` table lookup helpers | CCE round-trip correct |
| 6134 | `unicode-bytes-to-text` list stride fix (8-byte elements, was reading at stride 1) | +1 test (arm64-http) |

### Phase 5: Memoization (CL 6137)

Zero-argument functions whose body is a list literal (like
`cce-to-unicode-table` with 128 elements) were re-allocated on
every reference — ~1 KB per call, 128 calls per `from-unicode`,
30+ `from-unicode` per layout builder = ~7 MB per keyboard layout.

The memo mechanism caches return values of pure zero-arg functions
in a 64-slot table (zeroed at boot, stored in s0). First call
executes normally; subsequent calls return the cached value.

| CL | Fix | Impact |
|----|-----|--------|
| 6137 | Memoize zero-arg list-literal functions | +1 test (keyboard-layout), heap usage ~100x reduction |

## Architecture Notes

### Register Convention

| Register | Role |
|----------|------|
| s0 (x8) | Memo table base pointer |
| s1 (x9) | Heap bump pointer |
| sp (x2) | Stack pointer (top of RAM) |
| t2 (x7) | Reserved for large-frame/spill address computation |
| t3-t6, t0, t1 | Temp rotation (6 registers) |
| s2-s11 | Callee-saved locals (10 registers, spill to stack beyond) |
| a0-a7 | Arguments and return value |

### Memory Layout (QEMU virt, 3 GB)

```
0x80000000          Code (loaded by QEMU device loader)
0x80080000          CCE-to-Unicode table (256 bytes)
0x80080100          Unicode-to-CCE table (256 bytes)
0x80100200          Memo table (512 bytes, 64 slots × 8)
0x80100400          Heap base (s1 init)
  ↓ grows up
0x13FFF0000         Stack top (sp init, grows down)
```
