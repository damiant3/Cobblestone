# ESP32-C6 -- RISC-V IoT Target

**Source**: Espressif official product page
**Role**: Primary RISC-V development target for Codex IoT

## CPU

- High-performance 32-bit RISC-V core, up to 160 MHz
- Low-power 32-bit RISC-V core, up to 20 MHz (deep sleep sentinel)
- Dual-core design: HP core for application, LP core for always-on

## Memory

- 320 KB ROM
- 512 KB SRAM
- External flash support (typically 4-16 MB)

## Wireless

- WiFi 6 (802.11ax) 2.4 GHz -- OFDMA, MU-MIMO, TWT (target wake time)
- Also 802.11b/g/n backward compatible
- Bluetooth 5 LE -- advertising extensions, coded PHY (long range)
- 802.15.4 -- Thread and Zigbee support (Matter-compatible)

## Peripherals

- 30 GPIOs (QFN40) or 22 GPIOs (QFN32)
- SPI, UART, I2C, I2S, RMT, TWAI (CAN), PWM, SDIO
- Motor Control PWM
- 12-bit ADC
- Temperature sensor

## Security

- RSA-3072 secure boot
- AES-128/256-XTS flash encryption
- Digital signature peripheral
- HMAC peripheral
- Trusted Execution Environment (TEE)

## Power Modes

Dual-core design enables the LP core to monitor sensors and
peripherals while the HP core sleeps. WiFi 6 TWT (Target Wake Time)
reduces radio power by scheduling wake windows with the AP.

## Development

- ESP-IDF framework (C/C++, FreeRTOS-based)
- ESP-AT firmware for co-processor mode
- Widely available dev boards (~$5-10 USD)

## Codex Backend Notes

- RISC-V RV32IMC instruction set (Integer, Multiply, Compressed)
- Reference encoder exists in old/src/Codex.Emit.RiscV/
- Vector table at address 0x0 (RISC-V CLINT model)
- Interrupt controller: PLIC (Platform-Level Interrupt Controller)
  with vectored mode
  - HP core: up to 64 external interrupt sources, 15 priority levels
  - LP core: limited interrupt set (GPIO wakeup, timer, UART)
  - Interrupt entry latency: 4 cycles minimum (vectored mode)
  - Source: ESP32-C6 Technical Reference Manual, Chapter 8
- Timers: 2x 54-bit general-purpose timers, 1x SysTick-equivalent
  (RISC-V machine timer via CLINT mtime/mtimecmp)
  - Resolution at 160 MHz: 6.25 ns per tick
  - Used for `[HardRealtime]` deadline monitoring on HP core
- Flash-mapped execution (XIP) -- different from RAM-loaded x86
- Memory-mapped peripherals (GPIO, UART, etc. at fixed addresses)
- ESP32-C6 Technical Reference Manual needed for register maps
  (available from Espressif: https://www.espressif.com/en/support/documents/technical-documents)
