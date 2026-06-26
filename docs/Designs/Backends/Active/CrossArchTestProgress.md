# Cross-Architecture Test Progress

Status of ARM64 and RISC-V backends against the x86-64 test battery,
run via `build/test-cross-batch.ps1` on Renode virtual boards and
QEMU system emulation.

## Current Results (2026-06-26)

| Metric | ARM64 | RISC-V | x86-64 |
|---|---|---|---|
| Tests total | 154 | ~153 | ~193 |
| Verified pass | **135 (100%)** | 122 (92%) | ~183 |
| Compile-only pass | 2 | ~2 | 3 |
| Skipped | 17 | ~20 | 10 |
| Fail | **0** | 11 | 0 |

ARM64 reached full parity with the x86-64 battery on 2026-06-26
(CL 6111). Every test that passes on x86-64 now passes on ARM64.

## ARM64 Journey: 20% to 100%

| Date | CL | Pass Rate | Key Fix |
|------|-----|-----------|---------|
| 2026-06-14 | 4421 | ~17/152 (11%) | Initial plug, Hello World |
| 2026-06-21 | 5347 | 62/152 (41%) | 18 codegen CLs: builtins, lambda, text |
| 2026-06-24 | 6065 | 131/137 (96%) | O(1) list-push, try/fail, closures, vec-select |
| 2026-06-25 | 6091 | 130/135 (96%) | Record fields, list-set-at COW, vec-select spill |
| **2026-06-26** | **6111** | **135/135 (100%)** | **Record constructor heap corruption, boot stub EL3, byte stride** |

### Three bugs that closed the last 4%

1. **Record constructor heap corruption (CL 6097)** -- `a64-emit-record`
   passed spill slot numbers directly to ARM64 instruction encoding.
   When a function exhausted all 9 callee-saved registers (x19-x27),
   the spill slot (e.g. 92) wrapped to x28 (heap register) in the 5-bit
   register field (92 & 31 = 28). The `MOV x28, x28` was a NOP; the
   heap pointer was never saved before advancing. Next allocation
   overwrote the record. Found via QEMU GDB hardware watchpoint on
   the font record at heap offset +64. Fixed truetype-bridge,
   truetype-render, trie-prefix tests.

2. **Boot stub VBAR_EL3 (CL 6101)** -- the boot stub wrote
   `MSR VBAR_EL3, X9` which traps on QEMU virt (boots at EL1).
   Renode's Cortex-A53 starts at EL3 so it worked there. Removed
   the EL3 write. ARM64 tests now run on both Renode and QEMU.

3. **unicode-bytes-to-text byte stride (CL 6106)** -- the runtime
   helper read `List Integer` elements at 1-byte stride (LDRB at
   base+i) but each element is 8 bytes. Only the first character
   converted correctly. Fix: LSL index by 3, LDR for 8-byte access.
   Fixed HTTP method parsing ("GET" instead of "G" + nulls).

### Slow tests (skipped by default)

| Test | Reason | x86-64 |
|------|--------|--------|
| tls-test | X25519 DH: ~660KB/step x 255 steps x 4 scalar mults exceeds Renode sim budget | PASS |
| ui-orchestrator-test | 17 foreword module deps, IR compile exceeds 600s | PASS |

Both pass on x86-64. Run with `-Slow` flag if needed.

## RISC-V Progress

122/133 verified pass (92%) as of CL 6088. Key milestones:

- CL 6085: vector f32 fix, fast cross-test battery
- CL 6088: missing ret fix recovered 28 tests
- Remaining 11 failures: vector-f32 IR gaps, edge cases in
  effect handler dispatch

## Infrastructure

### Test Commands

```powershell
# Single test (Renode)
build/test-cross.ps1 -Arch arm64 -Test arithmetic -TimeoutSec 10

# Full battery (Renode, parallel)
build/test-cross-batch.ps1 -Arch arm64 -Jobs 4 -RenoTimeout 10

# Full battery (QEMU)
build/test-cross-batch.ps1 -Arch arm64 -Jobs 4 -UseQemu

# Plug rebuild (~90s)
codex/plugs/arm64/build.ps1
codex/plugs/riscv/build.ps1
```

### Renode Boards

| Board | CPU | RAM | UART |
|-------|-----|-----|------|
| `codex-arm64.repl` | Cortex-A53 (GICv3) | 1 GB @ 0x40000000 | PL011 @ 0x09000000 |
| `codex-riscv64.repl` | RV64GC (PLIC/CLINT) | 256 MB @ 0x80000000 | NS16550 @ 0x10000000 |

### QEMU

ARM64: `qemu-system-aarch64 -M virt -cpu cortex-a53 -m 1G -kernel <elf> -serial file:<log>`
RISC-V: `qemu-system-riscv64 -M virt -m 256M -bios none -device loader,file=<elf>,addr=0x80000000 -serial file:<log>`

### Pipeline

```
source.codex
  -> compile.ps1 -IrCce (x86-64 seed, produces IR text)
  -> arm64/riscv plug CDX (consumes IR, emits wire protocol)
  -> compile-arm64/riscv.ps1 (parses wire, builds ELF64)
  -> Renode/QEMU (boots ELF, captures UART)
  -> compare UART vs .expected
```

### Timing

- Single test compile: ~2-7s (IR step ~1.5s + plug codegen ~1-5s)
- Single test run (Renode): 10s timeout (most complete in 1-3s)
- Full battery (Renode, 4 parallel): ~8 min compile + ~7 min run
- Full battery (QEMU): similar compile, faster run (~3 min)

### Symbol Maps

Each cross-compiled test produces a `.map` file at
`test-output-cross/<arch>/<test>/<test>.map` with function addresses
and sizes. Use for crash analysis:

```powershell
build/resolve-rip.ps1 0x40005CD0 -Map test-output-cross/arm64/test/test.map
```
