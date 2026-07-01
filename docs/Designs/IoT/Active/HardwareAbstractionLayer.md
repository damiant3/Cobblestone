# Hardware Abstraction Layer: Peripherals as Linear Resources

**Created**: 2026-06-12 (reek)
**Status**: Design — not yet started
**Upstream**: `docs/PM/IoT/AGENT-PROMPT.md` deliverable 2,
`docs/Designs/IoT/Active/BackendArchitecture.md` (target boards)

## The Problem

IoT firmware talks to GPIO, UART, SPI, I2C, ADC, and power-state
hardware. Every existing embedded HAL (vendor SDKs, Zephyr drivers,
Arduino cores) is a C function library where leaving a bus
half-configured, double-releasing a peripheral, or sleeping with a
transaction in flight is a runtime bug. Codex has the two language
features that turn these into compile errors — linear types and
effect rows — and a driver corpus (`codex.os.kernel`, 22 modules:
xHCI, NE2K, PCI, ATA, VGA) that proves the register-level driver
pattern works in Codex. The HAL is the disciplined generalization
of that pattern to MCU peripherals.

## Constraints

1. No vendor SDKs, no libc. Registers are programmed directly via
   memory-mapped I/O, exactly as the x86 drivers do.
2. The type system is the enforcement mechanism. If a misuse can be
   a type error, it must be: runtime checks are fallback only.
3. Foreword layering rule: foreword modules must not depend on
   codex.kernel or codex.os. Therefore the HAL's *interfaces*
   (effects, handle types) live in the foreword; the *register
   implementations* live in board chapters under codex.os.kernel
   (or a new codex.os.board sub-quire).
4. One thing at a time: GPIO+UART first (that is the LED-blink and
   serial-print demo), then SPI, I2C, ADC, power, in that order.
5. No premature abstraction: three boards is two implementations
   more than an interface needs, but the interface is written
   against the *first two* (STM32 + ESP32-C6), not speculatively.

## The Design

### Effects

One effect per peripheral class, declared in a foreword chapter
`Hal` (interfaces only — no register addresses in the foreword):

```
  effect Gpio where
    gpio-open      : PinId, PinMode -> [Gpio] linear Pin
    gpio-write     : linear Pin, Boolean -> [Gpio] linear Pin
    gpio-read      : linear Pin -> [Gpio] (linear Pin, Boolean)
    gpio-close     : linear Pin -> [Gpio] Nothing

  effect Uart where
    uart-open      : UartId, UartConfig -> [Uart] linear UartPort
    uart-send      : linear UartPort, List Integer -> [Uart] linear UartPort
    uart-recv      : linear UartPort, Integer -> [Uart] (linear UartPort, List Integer)
    uart-close     : linear UartPort -> [Uart] Nothing

  effect Spi where
    spi-open       : SpiId, SpiConfig -> [Spi] linear SpiBus
    spi-select     : linear SpiBus, ChipSelect -> [Spi] linear SpiTxn
    spi-transfer   : linear SpiTxn, List Integer -> [Spi] (linear SpiTxn, List Integer)
    spi-deselect   : linear SpiTxn -> [Spi] linear SpiBus
    spi-close      : linear SpiBus -> [Spi] Nothing

  effect I2c where
    i2c-open       : I2cId, I2cConfig -> [I2c] linear I2cBus
    i2c-write      : linear I2cBus, Address, List Integer -> [I2c] (linear I2cBus, I2cResult)
    i2c-read       : linear I2cBus, Address, Integer -> [I2c] (linear I2cBus, Result (List Integer))
    i2c-close      : linear I2cBus -> [I2c] Nothing

  effect Adc where
    adc-open       : AdcId, AdcConfig -> [Adc] linear AdcUnit
    adc-sample     : linear AdcUnit, Channel -> [Adc] (linear AdcUnit, Integer)
    adc-close      : linear AdcUnit -> [Adc] Nothing

  effect Power where
    sleep-light    : Duration -> [Power] Nothing
    sleep-deep     : linear Board, SleepConfig -> [Power] Nothing   -- see below
    wake-source    : [Power] WakeReason
```

Every operation threads its linear handle. The guarantees fall out
of the existing linearity diagnostics with zero new checker work:

- Forgetting `spi-close` → CDX2063 (linear never used — leak).
- Using a bus after close → CDX2061 (used more than once).
- A library function without `[Spi]` in its row cannot touch the
  bus at all — a compromised dependency cannot reach hardware it
  was not granted (this is the compliance story's load-bearing
  fact; see `ComplianceEvidence.md`).

The SPI design distinguishes `SpiBus` from `SpiTxn`: chip-select is
a state, and the type makes select/deselect pairing mandatory.
Leaving CS asserted is unrepresentable.

### The sleep rule

Deep sleep on MCUs powers down peripheral domains; any open handle
is silently invalidated by the hardware. Rather than invent a "no
linears live" judgment (a new whole-context check the type system
does not have), the design makes the board itself linear:

```
  board-open  : [Hal] linear Board
  gpio-open   : linear Board, PinId, PinMode -> [Gpio] (linear Board, linear Pin)
  sleep-deep  : linear Board, SleepConfig -> [Power] Nothing
```

`sleep-deep` consumes the `Board`. Because every `*-open` threads
the board but every open handle is its *own* linear value, the
programmer must close each handle to satisfy linearity on the path
that reaches `sleep-deep` — the handles cannot be implicitly
discarded (CDX2063) and the board cannot be duplicated to smuggle
one past (CDX2061). "A device that sleeps while holding a linear
SPI handle is a compile error" is then literally true, using only
the checker that exists today. Wake from deep sleep re-enters
`opening` (MCU reset semantics), which matches the linear story:
nothing survives, so nothing can dangle.

### Board chapters

Per target, a chapter in the OS layer holding what
X86_64Boot.codex holds for the PC: the address map as named
constants, and the register-level implementations of the Hal
effect operations.

| Chapter | Contents |
|---|---|
| Stm32F4Board | Flash 0x08000000, SRAM 0x20000000, peripherals 0x40000000+, RCC clock-enable sequences, GPIOA..I, USART1-6, SPI1-3, I2C1-3, ADC1-3, EXTI/NVIC tables, Stop/Standby entry |
| Esp32C6Board | SRAM 0x40800000 region, GPIO matrix, UART0-1, SPI2, I2C0, ADC1, LP-core mailbox, TWT/light-sleep hooks |
| Pi4Board | BCM2711 peripheral base, GPIO, PL011 + mini UART, SPI0, BSC I2C, no ADC (note in docs), GIC routing |

Numbers come from RM0090, the ESP32-C6 TRM, and the BCM2711
peripherals doc (catalogued in `docs/PM/IoT/Hardware/`). Every
constant is a named definition — the TRACE-ALLOC incident taught
that magic addresses baked into emit functions are a class hazard;
board constants live in exactly one chapter per board.

### MMIO primitives

The x86 drivers use port I/O and memory-mapped access already; the
HAL standardizes on two intrinsics (`__mmio-read`, `__mmio-write`,
each in 8/16/32-bit widths) that each backend emits natively
(x86: mov; ARM/RISC-V: ldr/str — with the volatile-ordering
guarantee that the emitter never reorders or elides them; Cortex-M
additionally needs a dmb on device-memory writes that gate DMA).
Bounded-integer types (`Integer between 0 and #FFFFFFFF`) type the
register values; `#`-literals (CL 3837 decision) spell the masks.

### Interrupts

Phase 1 is polled I/O — the blink/sensor/print demo needs no
interrupts, and the x86 pattern (handler-table cells + ISR stubs)
ports later alongside the scheduler. The board chapters reserve
their vector-table layout from day one (Cortex-M: SP at word 0,
reset at word 1, NVIC entries; RISC-V: CLINT mtvec) because the
flash image plug must lay the table down even when every entry is
the spin-stub. Interrupt-driven wake (`wake-source`) lands with
the Power phase.

## What This Unlocks Downstream

- `ProtocolStack.md`: UART is the first transport for bring-up;
  the ESP32-C6 radio (WiFi/802.15.4) is explicitly *not* in this
  HAL phase — it is a large driver project gated behind it.
- `OTAFirmwareUpdate.md`: needs a flash-write capability — added
  here as a `Flash` effect with the same linear shape (open bank,
  write pages, seal) once the STM32 dual-bank layout is the
  staging target.
- `HardRealtime.md`: `[HardRealtime]`-annotated sensor reads and
  motor control loops use `[Gpio]`, `[Clock]`, `[Adc]` effects —
  the effect restrictions (§Effect Integration) are designed to
  allow these HAL effects. Power-management sleep interacts with
  `[HardRealtime]` scheduling: the scheduler knows the task's
  next activation after `power-enter-stop-mode` and does not
  preempt during the stop window.
- `ComplianceEvidence.md`: each effect in the HAL is a named
  capability in the CDX manifest; "this firmware can touch SPI and
  UART but not the radio" becomes a load-time-verified, signed
  claim.

## Memory and Time-Complexity Risk

Handle records are small fixed-size allocations; on MCU heaps every
allocation is permanent until the function returns, so HAL
operations must not allocate per call in hot loops — `uart-send`
and `spi-transfer` write through the existing buffer primitives
(`__buf-write-bytes`) rather than building intermediate lists.
`List Integer` for payloads is acceptable at bring-up sizes;
the buffer-typed variants are the documented hot path. No
recursion in driver code without a fuel cap. Verdict: low risk if
the buffer discipline is stated in the chapter prose from day one.

## Open Questions

1. **Where do implementations live** — decided: `codex.os.board`.
   Keeps PC drivers and MCU boards separate. The name says what
   the quire contains (board chapters), not what it abstracts.
   The dependency order rule (foreword → codex → kernel → os) is
   satisfied.
2. **DMA.** Out of scope for every Phase-1 peripheral; SPI bulk
   transfer on H7 will eventually want it. Linear DMA descriptors
   are a clean fit but a new design — defer.
3. **Pin multiplexing conflicts.** Two opens of the same pin
   through different peripherals (PA9 as GPIO vs USART1-TX) is a
   runtime `Result` failure at open in this design. A type-level
   pin registry (board record carrying per-pin linear tokens) is
   possible but heavy; revisit after first real firmware.
4. **Clock tree configuration.** STM32 RCC setup is its own small
   language of dependencies. Phase 1 hardcodes one known-good
   configuration per board in the board chapter; a typed clock-tree
   model is explicitly deferred.
