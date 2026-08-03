# ARM Architecture Specification Index

**Source**: ARM Developer documentation portal
**Note**: Full reference manuals require ARM developer account download

## Key Documents

| Document ID | Title | Architecture |
|---|---|---|
| DDI0403 | ARMv7-M Architecture Reference Manual | Cortex-M (Thumb-2) |
| DDI0553 | ARMv8-M Architecture Reference Manual | Cortex-M (v8-M) |
| DDI0487 | ARM Architecture Reference Manual ARMv8-A | Cortex-A (AArch64) |

## Cortex-M Architecture (STM32 target)

- **Instruction set**: Thumb-2 (mixed 16/32-bit encoding)
- **Exception model**: NVIC (Nested Vectored Interrupt Controller)
  - Up to 240 external interrupts
  - 8 priority bits (configurable)
  - Tail-chaining (back-to-back ISR without stack restore)
  - Late arrival (higher-priority preemption during stacking)
- **SysTick**: 24-bit decrementing counter for OS scheduling
- **Memory model**: Flat (no MMU), optional MPU (8-16 regions)
- **Registers**: R0-R12 general purpose, SP, LR, PC, xPSR
- **Power modes**: Thread/Handler mode, privileged/unprivileged
- **Boot**: Vector table at 0x0 -- SP from [0], reset handler from [4]

## Cortex-A Architecture (Raspberry Pi target)

- **Instruction set**: AArch64 (A64) -- 32-bit fixed-width encoding
- **Exception model**: GIC (Generic Interrupt Controller)
  - 4 exception levels (EL0-EL3)
  - IRQ, FIQ, SError, synchronous exceptions
- **MMU**: 4-level page tables (similar to x86-64)
  - VA sizes: 48-bit (Sv48 equivalent)
  - Granule sizes: 4KB, 16KB, 64KB
- **Registers**: X0-X30 (64-bit), W0-W30 (32-bit view), SP, PC
- **Cache**: Configurable L1/L2/L3 per implementation
- **Boot**: Vendor-specific (Pi: GPU loads kernel to 0x80000)

## Existing Codex Implementation

Reference implementation in `old/src/Codex.Emit.Arm64/`:
- Arm64Encoder.cs: AArch64 instruction encoding
- Arm64Emitter.cs: IR to machine code translation
- Arm64CodeGen.cs: Register allocation, calling convention
- ElfWriterArm64.cs: ELF binary output for ARM64

## Key Differences from x86-64

| Feature | x86-64 | Cortex-M | Cortex-A |
|---|---|---|---|
| Encoding | Variable-length (1-15 bytes) | Thumb-2 (2/4 bytes) | Fixed 4 bytes |
| Registers | 16 GP + FLAGS | 13 GP + SP/LR/PC | 31 GP + SP/PC |
| Interrupts | APIC/IOAPIC | NVIC | GIC |
| MMU | Yes (4-level) | No (MPU only) | Yes (4-level) |
| Boot | BIOS/UEFI/Multiboot | Vector table | Vendor firmware |
| Memory map | Flat + paging | Flat | Flat + paging |
