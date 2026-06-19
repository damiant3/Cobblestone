# Tinker's Toolbox — Board Support and IoT Hardware

The board support package for Codex IoT. Nine target boards with
register-level drivers, all written in Codex, all compiled by the
seed, all tested on MMIO stubs without real hardware.

For the regulatory and compliance story, see `docs/KingsAndCourts.md`.
For the protocol stack (MQTT, CoAP, LwM2M, OTA), see the README's
IoT Platform section.

---

## The Board HAL

`codex/foreword/core/Board.codex` defines the hardware abstraction:
pin modes, UART/SPI/I2C/ADC configuration records with bounded-integer
fields, and default configs. Board-specific implementations live in
`codex/boards/` and cite `Foreword chapter Board` for the shared types.

Every driver follows the same pattern:

1. **Register constants** — base addresses and offsets from the
   official reference manual, named as `<board>-<peripheral>-<register>`.
2. **Init function** — enables clocks, configures pins (alternate
   function or GPIO), sets baud/frequency/timing registers.
3. **Transfer functions** — send/receive with fuel-bounded polling
   loops that terminate immediately on MMIO stubs.
4. **Smoke test** — exercises every driver function, returns a
   sub-test count. Expected output verified against `.expected` file.

No dynamic allocation. No interrupts (polling only). No OS
dependencies. Each board file is standalone — cite it, call its
functions, done.

---

## Board Matrix

| Board | MCU | Arch | Clock | SRAM | Sub-tests |
|-------|-----|------|------:|-----:|----------:|
| **STM32F4 Discovery** | Cortex-M4F | ARM | 168 MHz | 192 KB | 6 |
| **ESP32-C6 DevKit** | RV32IMC | RISC-V | 160 MHz | 512 KB | 6 |
| **Raspberry Pi 4** | Cortex-A72 | ARM | 1.5 GHz | 1-8 GB | 6 |
| **QEMU virt** | AArch64 + RV | Both | — | — | 6 |
| **nRF52840 DK** | Cortex-M4F | ARM | 64 MHz | 256 KB | 23 |
| **RP2040 (Pico)** | Dual M0+ | ARM | 133 MHz | 264 KB | 24 |
| **nRF9160 DK** | Cortex-M33 | ARM | 64 MHz | 256 KB | 17 |
| **STM32L4 Nucleo** | Cortex-M4F | ARM | 80 MHz | 128 KB | 13 |
| **FE310 (HiFive1)** | RV32IMAC | RISC-V | 320 MHz | 16 KB | 7 |

**108 sub-tests total.** All pass on MMIO stubs under codex-vm.

---

## Peripheral Coverage

### Standard (all 9 boards)

| Peripheral | What | Pattern |
|-----------|------|---------|
| **GPIO** | Pin mode (input/output/alternate), read, write, toggle, pull config | Read-modify-write on MODER/DIR/PIN_CNF registers |
| **UART** | Init (baud config), send byte, print text, println | Fuel-bounded TX-ready poll, write to data register |
| **SPI** | Init (clock/polarity/phase), full-duplex transfer | TX-ready wait, write TX, RX-ready wait, read RX |
| **I2C** | Init (clock speed), write-reg, read-reg | Start/address/data/stop sequence with flag polling |
| **LED** | Board-specific LED pin init, on, off | GPIO output wrappers for the dev board's LEDs |
| **Delay** | Millisecond busy-wait | Fuel-bounded decrement loop calibrated to clock speed |

### Board-Specific

| Board | Peripheral | What it does |
|-------|-----------|-------------|
| **nRF52840** | CLOCK | HFCLK (64 MHz crystal) and LFCLK (32.768 kHz) start with event wait |
| | SAADC | 14-bit successive-approximation ADC, 8 channels, DMA result buffer |
| | RADIO | 2.4 GHz radio configured for BLE 1M PHY. Access address, PCNF0/1, TX power. Channel mapping for advertising channels 37/38/39 |
| | **BLE Beacon** | Complete BLE advertising PDU builder per Bluetooth Core Spec Vol 6 Part B. ADV_NONCONN_IND with Flags (general discoverable), Complete Local Name, TX Power Level. `ble-build-beacon "Codex"` produces a well-formed advertising packet ready for RADIO.PACKETPTR |
| | **GATT Services** | Attribute table construction per Bluetooth Core Spec Vol 3 Part G. Service/characteristic/descriptor builders with UUIDs from Bluetooth SIG Assigned Numbers. Three pre-built services: Heart Rate (measurement + body sensor location + CCC), Battery (level), Temperature (measurement + CCC). `gatt-service-attr-count` verifies table structure |
| **RP2040** | Reset | Peripheral unreset with done-wait (RP2040 boots with peripherals held in reset) |
| | GPIO (atomic) | SIO SET/CLR/XOR aliases — atomic bit manipulation without read-modify-write races |
| | ADC | 12-bit SAR, 4 channels, 500 ksps. Channel select, start conversion, wait ready |
| | PIO | Programmable I/O state machine configuration: instruction memory write, clock divider, pin control, shift control, exec control, enable |
| | **WS2812 NeoPixel** | Complete WS2812B LED strip driver via PIO. 4-instruction PIO program (OUT with side-set, conditional JMP, NOP timing). GRB color packing (`ws2812-grb-pack r g b`), single-pixel FIFO write, strip send loop. Timing: T0H=400ns, T1H=800ns at 800 kHz bit rate |
| | **USB Descriptors** | USB 2.0 device descriptor construction per USB Spec Rev 2.0 §9.6. Device, Configuration, Interface, and Endpoint descriptors. CDC-ACM composite (control + data interfaces, bulk + interrupt endpoints). Class codes for CDC/HID/MSC/Vendor. Verified: descriptor lengths, type codes, endpoint direction bits, VID/PID encoding |
| **nRF9160** | CLOCK | HFCLK start with event wait (same peripheral model as nRF52840) |
| | IPC | Inter-processor communication for app↔modem. Send signal, check/clear events, read/write shared memory (GPMEM) |
| | **AT Commands** | Build: `AtCfun 1` → `"AT+CFUN=1"`, `AtCops` → `"AT+COPS?"`, `AtCesq` → `"AT+CESQ"`, `AtCgdcont 1 "internet"` → `"AT+CGDCONT=1,\"IP\",\"internet\""` |
| | **AT Parser** | Parse: `"+COPS: 0,0,\"Telia\",7"` → `AtCopsResponse 0 "Telia"`, `"+CESQ: 99,99,255,255,28,47"` → `AtCesqResponse 28 47`, `"+CGDCONT: 1,\"IP\",\"internet\""` → `AtCgdcontResponse 1 "internet"`. Round-trip: build command, parse response, extract fields |
| **STM32L4** | RCC | MSI clock range selection (100 kHz–48 MHz), MSIRGSEL bit for runtime range switching |
| | PWR | Low-power mode register config: Stop 0/1/2 (1.1 μA), Standby, Shutdown. Mode read-back |
| | LPTIM1 | Low-power timer that runs in Stop mode. Auto-reload, compare match, single-shot start |
| | **Sleep Entry** | Full sleep preparation per RM0351 §5.3: select LPMS mode, clear wakeup flags (PWR_SCR), set SCB SLEEPDEEP bit, configure LPTIM wakeup, restore clock on wake. Everything except the final WFI instruction (which would halt the VM). Sleep-restore resets SLEEPDEEP and re-selects MSI clock range |
| **FE310** | GPIO (IOF) | I/O Function select — pins 16/17 are UART0 via IOF0, pin 5 is SPI SCK. IOF_SEL + IOF_EN register pair |
| | PWM | 4-channel PWM with configurable scale. CMP0-CMP3 comparators. Always-on + zero-compare mode |
| | PLIC | Platform-Level Interrupt Controller. Per-IRQ priority (3-bit), enable bits, global threshold, claim/complete cycle |

---

## Reference Manuals

Every register address in the board files comes from the official
reference manual for that chip. No guessing, no copying from Arduino
libraries.

| Board | Document | Key sections |
|-------|----------|-------------|
| STM32F4 | RM0090 (STM32F405/407) | GPIO §8, USART §30, SPI §28, I2C §27 |
| ESP32-C6 | ESP32-C6 TRM v1.0 | GPIO §5, UART §26, SPI §25, I2C §27 |
| Raspberry Pi 4 | BCM2711 ARM Peripherals | GPIO §5, PL011 §11, SPI §10, BSC §3 |
| nRF52840 | nRF52840 PS v1.7 | GPIO §6.8, UARTE §6.34, SPIM §6.30, TWIM §6.31, SAADC §6.23, RADIO §6.20 |
| RP2040 | RP2040 Datasheet | SIO §2.3, IO Bank0 §2.19, PL011 §4.2, PL022 §4.4, DW_apb_i2c §4.3, ADC §4.9, PIO §3 |
| nRF9160 | nRF9160 PS v2.1 | GPIO §6.5, UARTE §6.10, SPIM §6.11, TWIM §6.12, IPC §6.4 |
| STM32L4 | RM0351 (STM32L4x6) | GPIO §8, USART §40, SPI §38, I2C §37, PWR §5, LPTIM §31, RCC §6 |
| FE310 | FE310-G002 Manual v1p1 | GPIO §17, UART §18, QSPI §19, PWM §20, PLIC §8 |

---

## Connectivity Coverage

| Transport | Board | Protocol |
|-----------|-------|----------|
| **BLE 5** | nRF52840 | Radio PHY + advertising PDU |
| **WiFi 6** | ESP32-C6 | (chip capability, not driver-level yet) |
| **LTE-M / NB-IoT** | nRF9160 | Modem IPC + AT command round-trip |
| **Ethernet** | Raspberry Pi 4 | (chip capability; NE2K driver in codex-vm) |
| **USB** | nRF52840, RP2040 | (chip capability, not driver-level yet) |

Combined with the protocol stack (MQTT v5, CoAP, LwM2M, OTA), this
covers the three IoT connectivity tiers: short-range (BLE), medium-range
(WiFi), and wide-area (cellular). The same Codex source compiles for
any board — swap the `cites Boards chapter` line and rebuild.

---

## What's Not Here Yet

- **Interrupt-driven I/O.** All drivers poll. Interrupt handlers need
  the bare-metal interrupt vector table wired, which depends on the
  target architecture (NVIC for ARM, PLIC for RISC-V). The FE310 PLIC
  registers are configured and tested; the missing piece is the
  compiler emitting ISR entry/exit sequences. This is a compiler
  change, not a board driver change.
- **DMA completion callbacks.** The nRF DMA peripherals (UARTE, SPIM,
  TWIM, SAADC) are configured for DMA but completion is polled via
  event registers. Real DMA callbacks need interrupt support.
- **Real hardware validation.** Every test runs on MMIO stubs (all
  reads return 0, all writes are no-ops). The register addresses are
  from the reference manuals but the electrical behavior is untested.
- **Power management — WFI instruction.** STM32L4 now has the full
  sleep preparation sequence (LPMS mode select, wakeup flag clear,
  SCB SLEEPDEEP, LPTIM wakeup, clock restore). The only missing
  step is the WFI instruction itself, which halts the CPU. On
  real hardware this is one ARM instruction; in the VM it would halt
  the guest. Waiting on compiler support for architecture-specific
  intrinsics.
- **BLE host stack.** nRF52840 has radio config, beacon PDU, and
  GATT service definitions (Heart Rate, Battery, Temperature). What's
  missing is the connection-oriented protocol: L2CAP channel framing,
  GAP connection state machine, SMP pairing. These are substantial
  protocol implementations, not driver work.
- **USB device enumeration.** RP2040 has USB descriptor construction
  (device, config, interface, endpoint) and CDC-ACM composite layout.
  The missing piece is the USB device controller driver (RP2040
  USBCTRL_REGS at 0x50110000) that responds to host Setup packets
  and delivers descriptors. The existing xHCI driver in
  codex.os.kernel is host-side only.

---

## Cross-References

- `codex/foreword/core/Board.codex` — HAL types and default configs
- `codex/boards/` — 9 board implementations
- `codex/test/*-drivers.codex` — smoke tests
- `docs/KingsAndCourts.md` — regulatory compliance story
- `docs/Designs/Features/Active/IoT-Addendum.md` — board expansion plan
- `docs/PM/IoT/` — compliance summaries, protocol references, hardware specs
