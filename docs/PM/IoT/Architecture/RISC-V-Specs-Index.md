# RISC-V Specification Index

**Source**: RISC-V International, https://riscv.org/technical/specifications/
**Spec library**: https://docs.riscv.org/

## Core ISA Specifications (Ratified)

All specifications are free and publicly available.

### Unprivileged ISA
- Volume I: RISC-V Unprivileged ISA (latest ratified version)
- Covers: RV32I, RV64I base integer instructions
- Extensions: M (multiply/divide), A (atomic), F (single-float),
  D (double-float), C (compressed 16-bit), V (vector),
  Zicsr (CSR instructions), Zifencei (instruction-fetch fence)

### Privileged ISA
- Volume II: RISC-V Privileged Architecture
- Covers: Machine mode (M), Supervisor mode (S), User mode (U)
- CLINT (Core Local Interruptor), PLIC (Platform-Level Interrupt Controller)
- Page tables (Sv32, Sv39, Sv48)

### Key Extensions for IoT

| Extension | Purpose | Codex Relevance |
|---|---|---|
| RV32IMC | Integer + Multiply + Compressed | ESP32-C6 target ISA |
| RV32E | Reduced register file (16 regs) | Ultra-low-cost MCUs |
| Zicsr | Control/Status Registers | Interrupt handling |
| Zifencei | Instruction fence | Self-modifying code (firmware update) |

## Non-ISA Specifications

- Debug specification (JTAG interface)
- Platform specifications (SiFive, etc.)
- Profiles (RVA20, RVA22 for application processors)

## Existing Codex Implementation

Reference implementation in `old/src/Codex.Emit.RiscV/`:
- RiscVEncoder.cs: Instruction encoding (R/I/S/B/U/J formats)
- RiscVEmitter.cs: IR to machine code translation
- RiscVCodeGen.cs: Register allocation, calling convention

RISC-V was the project's FIRST backend. The encoder handles RV32IM
instruction formats; compressed (C) extension encoding would be an
addition for code density on flash-constrained MCUs.

## Market Data (Verified)

- RISC-V market: $1.1-2.5B (2025), growing 24-41% CAGR
- IoT is largest application segment at ~35% of revenue
- Asia-Pacific commands 42% of the market
- ESP32-C6 is the leading RISC-V IoT SoC (WiFi 6 + BLE 5 + Thread)
