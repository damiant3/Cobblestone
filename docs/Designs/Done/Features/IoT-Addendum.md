# IoT Board Expansion -- Addendum

**Author**: Fester + Damian
**Date**: 2026-06-16
**Status**: Active
**Companions**: Board.codex (HAL), KingsAndCourts.md (compliance story)

---

## Current Coverage

| Board | MCU | Arch | Connectivity | Market |
|-------|-----|------|-------------|--------|
| STM32F4 Discovery | Cortex-M4F @ 168 MHz | ARM | USB, SPI, I2C | Industrial dev board |
| ESP32-C6 DevKit | RV32IMC @ 160 MHz | RISC-V | WiFi 6, BLE 5, 802.15.4 | WiFi IoT |
| Raspberry Pi 4 | Cortex-A72 @ 1.5 GHz | ARM | Ethernet, WiFi, BLE | Gateway / edge |
| QEMU virt | AArch64 + RISC-V | Both | PL011/16550 UART | Testing |

All four have GPIO, UART, SPI, I2C drivers with smoke tests passing
on MMIO stubs. Board HAL in `codex/foreword/core/Board.codex`.

## New Boards -- Priority Order

### 1. Nordic nRF52840 (Cortex-M4F @ 64 MHz)

**Why**: BLE is the missing connectivity story. The nRF52840 is the
dominant BLE SoC -- Adafruit Feather, Arduino Nano 33 BLE, Particle
Xenon, Raytac MDBT50Q. Every consumer IoT wearable, sensor tag, and
beacon uses one.

**Reference manual**: nRF52840 Product Specification v1.7

**Peripherals to implement**:

| Peripheral | Register base | What |
|-----------|--------------|------|
| GPIO | 0x50000000 | P0 (32 pins), P1 (16 pins), DIR/OUT/IN/PIN_CNF |
| UARTE0 | 0x40002000 | DMA-based UART, STARTTX/STARTRX events |
| SPIM0 | 0x40003000 | DMA SPI master, FREQUENCY/CONFIG/TXD/RXD |
| TWIM0 | 0x40003000 | DMA I2C master (shared with SPI via PPI) |
| SAADC | 0x40007000 | Successive-approximation ADC, 8 channels, 14-bit |
| RADIO | 0x40001000 | 2.4 GHz radio (BLE PHY). TX/RXEN, PACKETPTR, SHORTS |
| CLOCK | 0x40000000 | HFCLK (64 MHz XTAL), LFCLK (32.768 kHz) |
| TIMER0 | 0x40008000 | 32-bit timer/counter |

**BLE**: The RADIO peripheral is the physical layer. A minimal BLE
beacon (advertising-only, no connections) needs: clock init, radio
config (BLE 1M PHY, access address 0x8E89BED6), advertising PDU
construction, and timed TX. This is achievable without a full BLE
stack -- it demonstrates the radio works and the timing is correct.
Full BLE host stack (GAP, GATT, L2CAP) is a later phase.

**Test plan**: Same MMIO-stub pattern. GPIO/UART/SPI/I2C smoke tests
(6 per board). SAADC config test. RADIO config test (verify register
writes for BLE PHY setup). BLE beacon PDU construction test (verify
advertising packet format).

### 2. RP2040 (Dual Cortex-M0+ @ 133 MHz)

**Why**: The Raspberry Pi Pico is $4 and has massive adoption in
education and hobby markets. The PIO (Programmable I/O) subsystem is
architecturally unique -- a pair of small state machines that bit-bang
arbitrary protocols at full clock speed. Demonstrating PIO support
proves Codex can handle novel hardware, not just conventional
peripherals.

**Reference manual**: RP2040 Datasheet (rp2040-datasheet.pdf)

**Peripherals to implement**:

| Peripheral | Register base | What |
|-----------|--------------|------|
| SIO/GPIO | 0xD0000000 | 30 GPIO pins, SET/CLR/XOR atomic writes |
| UART0 | 0x40034000 | PL011 (same IP as Pi4, familiar) |
| SPI0 | 0x4003C000 | PL022 SSP, 4-16 bit frames |
| I2C0 | 0x40044000 | DW_apb_i2c, 100/400/1000 kHz |
| ADC | 0x4004C000 | 4-channel SAR, 12-bit, 500 ksps |
| PIO0 | 0x50200000 | Programmable I/O, 4 state machines, 32-insn memory |
| TIMER | 0x40054000 | 64-bit microsecond timer |
| ROSC/XOSC | 0x40060000/0x40024000 | Ring oscillator + crystal |

**PIO**: Each PIO block has 4 state machines with a 32-instruction
program memory. Instructions: JMP, WAIT, IN, OUT, PUSH, PULL, MOV,
IRQ, SET. A WS2812 LED driver is ~6 PIO instructions. The PIO
assembler is a stretch goal but the register-level state machine
configuration is straightforward.

**Test plan**: GPIO/UART/SPI/I2C smoke tests (6). ADC config test.
PIO state machine configuration test (load a trivial program, verify
CTRL/EXECCTRL/SHIFTCTRL register writes). Atomic GPIO test (verify
SET/CLR/XOR write behavior).

### 3. Nordic nRF9160 (Cortex-M33 @ 64 MHz)

**Why**: Cellular IoT without WiFi infrastructure. LTE-M and NB-IoT
reach field sensors, agricultural monitors, and remote infrastructure
where there is no router. The nRF9160 has a modem co-processor that
handles the LTE stack; the application core talks to it over AT
commands or the nRF modem library API.

**Reference manual**: nRF9160 Product Specification v2.1

**Peripherals to implement**:

| Peripheral | Register base | What |
|-----------|--------------|------|
| GPIO | 0x50842500 | P0 (32 pins), secure/non-secure partitioning |
| UARTE0 | 0x40008000 | DMA UART (same peripheral model as nRF52840) |
| SPIM0 | 0x40008000 | DMA SPI master |
| TWIM0 | 0x40008000 | DMA I2C master |
| SAADC | 0x4000E000 | ADC, 8 channels |
| IPC | 0x4002A000 | Inter-processor communication (app ↔ modem) |
| TIMER0 | 0x4000F000 | Timer/counter |

**Modem**: The cellular modem is a separate core. Communication is
via IPC channels with AT command strings. A minimal test: send
`AT+CFUN=1` (enable modem), `AT+COPS?` (query network), and parse
the response. This doesn't require a live cell network -- the AT
command encoding/parsing can be tested with stubs.

**Test plan**: GPIO/UART/SPI/I2C smoke tests (6). IPC register config
test. AT command builder/parser test (CFUN, COPS, CESQ signal
strength, CGDCONT APN config). No live cell network required.

### 4. STM32L4 (Cortex-M4F @ 80 MHz, ultra-low-power)

**Why**: The battery IoT chip. STM32L4 is what ships in products --
STM32F4 is for prototyping. The L4 has the same Cortex-M4F core but
adds low-power modes (Stop, Standby, Shutdown), a low-power timer
(LPTIM), and an ultra-low-power comparator. Demonstrating power-mode
control proves Codex can target battery-powered devices, not just
wall-powered dev boards.

**Reference manual**: RM0351 (STM32L4x6)

**Peripherals to implement**:

| Peripheral | Register base | What |
|-----------|--------------|------|
| GPIO | 0x48000000 | Same peripheral model as STM32F4, different base |
| USART2 | 0x40004400 | UART (same IP block) |
| SPI1 | 0x40013000 | SPI master |
| I2C1 | 0x40005400 | I2C master |
| ADC1 | 0x50040000 | 16-channel, 12-bit |
| LPTIM1 | 0x40007C00 | Low-power timer (runs in Stop mode) |
| RCC | 0x40021000 | Clock config, MSI (100 kHz - 48 MHz), HSI16, PLL |
| PWR | 0x40007000 | Power control: Stop0/1/2, Standby, Shutdown modes |

**Low-power**: The key differentiator. Functions to enter/exit Stop
mode 2 (1.1 μA), configure LPTIM wakeup, and set the MSI clock speed.

**Test plan**: GPIO/UART/SPI/I2C smoke tests (6). RCC clock
configuration test (MSI ranges). PWR mode register test (verify
LPSDSR/PDDS/LPMS bit patterns for each power mode). LPTIM wakeup
configuration test.

### 5. SiFive FE310 (RISC-V RV32IMAC @ 320 MHz)

**Why**: The reference RISC-V microcontroller (HiFive1 Rev B). We
already have RISC-V codegen; this proves the backend works on real
RISC-V silicon, not just QEMU.

**Reference manual**: FE310-G002 Manual v1p1

**Peripherals**:

| Peripheral | Register base | What |
|-----------|--------------|------|
| GPIO | 0x10012000 | 32 pins, INPUT_EN/OUTPUT_EN/PORT/IOF_SEL |
| UART0 | 0x10013000 | TXDATA/RXDATA with FIFO |
| SPI0 | 0x10014000 | QSPI controller |
| I2C | -- | Not available (bitbang via GPIO or use QSPI) |
| PWM0 | 0x10015000 | 4-channel PWM |
| PLIC | 0x0C000000 | Platform-Level Interrupt Controller |

**Test plan**: GPIO/UART/SPI smoke tests (5). PWM configuration test.
PLIC priority/enable test.

### 6. Allwinner D1 (RISC-V RV64GCV @ 1 GHz)

**Why**: First mass-market RISC-V SBC (MangoPi MQ-Pro, Nezha,
Lichee RV). Linux-capable. Validates our RV64 backend on real
silicon with a rich peripheral set.

**Defer** until RISC-V backend matures beyond "meets GCC -O0."

---

## Implementation Order

| Phase | Board | Tests | Effort |
|-------|-------|------:|--------|
| **P1** | nRF52840 | 10 | GPIO, UART, SPI, TWIM, SAADC, RADIO config, BLE beacon PDU |
| **P2** | RP2040 | 8 | GPIO (atomic), UART, SPI, I2C, ADC, PIO config |
| **P3** | nRF9160 | 8 | GPIO, UART, SPI, TWIM, IPC config, AT command builder |
| **P4** | STM32L4 | 8 | GPIO, UART, SPI, I2C, LPTIM, PWR modes, RCC config |
| **P5** | FE310 | 5 | GPIO, UART, SPI, PWM, PLIC |
| **P6** | D1 | -- | Deferred |

Each phase: one `.codex` file in `codex/boards/`, one smoke test in
`codex/test/`, one `.expected` file. Same MMIO-stub pattern as existing
boards. Each board file cites `Foreword chapter Board` for the HAL types.

---

## After Board Drivers

Once the driver matrix is wider:

1. **Board abstraction test** -- a single test that exercises the Board
   HAL effect interface against every board, proving the abstraction
   works (same code, different board backend).
2. **Punctual board drivers** -- rewrite the critical-path driver
   functions (GPIO read/write, UART send byte) as `punctual`. They
   already avoid heap and recursion; the keyword makes it explicit.
3. **Protocol-over-board integration** -- wire MQTT packet builder to
   a board's UART, proving the full sensor-to-packet-to-wire path
   compiles and runs.
