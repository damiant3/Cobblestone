# STM32F4/H7 -- ARM Cortex-M IoT MCU Target

**Source**: STMicroelectronics (page timed out; specs from datasheet knowledge)
**Role**: Primary ARM Cortex-M target for Codex IoT

## STM32F407 (Cortex-M4, most popular variant)

- ARM Cortex-M4F @ 168 MHz (with FPU + DSP)
- 1 MB Flash, 192 KB SRAM
- Thumb-2 instruction set
- NVIC (Nested Vectored Interrupt Controller)
- SysTick 24-bit timer
- MPU (Memory Protection Unit)

### Peripherals
- GPIO: 140 pins across 9 ports
- UART: 4x USART + 2x UART
- SPI: 3x SPI
- I2C: 3x I2C
- ADC: 3x 12-bit ADC (up to 24 channels)
- DAC: 2x 12-bit DAC
- Timers: 12x 16-bit + 2x 32-bit
- USB OTG FS/HS
- Ethernet MAC (RMII/MII)
- SDIO, CAN, RNG, Hash, Crypto (on some variants)

### Power Modes
- Run, Sleep, Stop, Standby
- Standby: ~2 uA
- Stop: ~0.4 mA (all clocks off, SRAM retained)

## STM32H743 (Cortex-M7, high-performance variant)

- ARM Cortex-M7 @ 480 MHz (dual-issue superscalar)
- 2 MB Flash, 1 MB SRAM
- L1 cache: 16 KB I + 16 KB D
- Additional Cortex-M4 co-processor (dual-core variants)
- Crypto accelerator (AES, DES, SHA, RSA)
- True RNG
- All peripherals of F4 plus more ADC channels, JPEG codec

## Codex Backend Notes

- Thumb-2 instruction set (16/32-bit mixed encoding)
- Reference: ARM Architecture Reference Manual for ARMv7-M (DDI0403)
- Vector table at address 0x00000000 (or VTOR-remapped to Flash)
- NVIC: up to 240 interrupts, 8 priority bits, configurable priority
  grouping (preemption + sub-priority split)
  - Interrupt entry latency: 12 cycles (zero wait-state memory)
  - Tail-chaining (back-to-back interrupts): 6 cycles
  - Late-arriving (higher-priority during stacking): 6 cycles
  - Source: ARM Cortex-M4 TRM (DDI0439D), Section 3.4
- SysTick: 24-bit downcounter, clocked from HCLK or HCLK/8
  - Resolution at 168 MHz: 5.95 ns per tick
  - Max period: 2^24 / 168 MHz = 99.86 ms
  - Used as OS tick source and `[HardRealtime]` deadline timer
- MPU: 8 or 16 regions (configurable protection)
- No MMU -- flat address space (similar to Codex's bare-metal model)
- Flash starts at 0x08000000, SRAM at 0x20000000
- Peripheral registers at 0x40000000-0x5FFFFFFF
- Boot sequence: SP from vector[0], PC from vector[1] (reset handler)
- STM32CubeMX for pin/clock configuration (reference only)
- STM32 Reference Manual RM0090 (F407) / RM0433 (H743) for register maps
  (available from st.com/resource)

## Key Difference from x86

Cortex-M has NO MMU -- the address space is flat, which is actually
closer to Codex's bare-metal model than x86 with page tables. The
MPU provides protection regions but not virtual memory. This simplifies
the boot infrastructure significantly: no page table setup, no long
mode transition. Vector table + stack pointer + reset handler is all.
