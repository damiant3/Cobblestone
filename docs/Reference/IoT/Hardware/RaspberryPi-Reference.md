# Raspberry Pi 4/5 -- ARM Cortex-A IoT Gateway Target

**Source**: Raspberry Pi official documentation

## Raspberry Pi 4 (BCM2711)

- Quad-core ARM Cortex-A72 (ARMv8-A) @ 1.5 GHz
- L1: 32 KB data + 48 KB instruction per core
- L2: 1 MB shared
- Up to 8 GB LPDDR4-2400
- VideoCore VI GPU @ 500 MHz (OpenGL ES 3.0)
- PCIe, Gigabit Ethernet, dual HDMI, USB 3.0
- Multiple UART/I2C/SPI interfaces

## Raspberry Pi 5 (BCM2712)

- Quad-core ARM Cortex-A76 (ARMv8-A) @ 2.4 GHz
- 60% faster than Pi 4
- L1: 64 KB I+D per core
- L2: 512 KB per core
- L3: 2 MB shared
- LPDDR4X, 17 GB/s bandwidth
- VideoCore V3D VII @ 960 MHz (Vulkan 1.3)
- 2-3x performance uplift over Pi 4

## Codex Backend Notes

- AArch64 instruction set (ARMv8-A)
- Reference encoder exists in old/src/Codex.Emit.Arm64/
- ELF writer for ARM64 exists (ElfWriterArm64.cs)
- Boot: GPU firmware loads kernel to 0x80000 (AArch64 mode)
- GIC (Generic Interrupt Controller) for interrupt handling
- MMU with 4-level page tables (same concept as x86-64)
- GPIO memory-mapped at BCM peripheral base address
- UART at fixed memory-mapped addresses (mini UART and PL011)
- Community documentation for bare-metal extensive
  (https://github.com/isometimes/rpi4-osdev as reference)
