# Hardware Abstraction Layer: Peripherals as Linear Resources

**Created**: 2026-06-12 (reek)
**Status**: Structure shipped; the safety surface is real for the HAL
itself, for `Flash`, and (2026-08-18) for UART and GPIO on all nine board
drivers. `codex/foreword/core/Board.codex` plus nine board chapters in
`codex/boards/` are in the tree with passing smoke tests. Since
2026-07-13 every board function that touches a register declares
`[Device.Mmio]`, and `Board.codex` carries linear handles for UART, SPI,
`Flash` (2026-07-16) and now a linear GPIO `Pin` (2026-08-18). **As of
2026-08-18 all nine board chapters thread the shipped linear `UartPort`
and `Pin` handles**: each board has `<b>-uart-open/write/close` and
`<b>-gpio-open` + `<b>-pin-write/close` wrapping its own register-correct
functions, so the board's UART and GPIO carry the open-once/close-once
lifecycle (a dropped handle is CDX2063, a reused one CDX2061). QemuVirt is
UART-only. **The per-peripheral capabilities now exist (2026-08-18, main
17063, seed 7590CCA1):** `[Gpio]` (cap id 18), `[Uart]` (19) and `[Spi]`
(20) are rows in `Capability.codex` mirroring `Flash`, and the foreword
`gpio`/`uart`/`spi` handle ops carry `[Gpio, Device.Mmio]` /
`[Uart, Device.Mmio]` / `[Spi, Device.Mmio]`, so a driver holding only
`Device.Mmio` is refused (CDX2031, `errors/hal-launder-mmio-{gpio,uart,
spi}`). **The nine board wrappers carry them too (main 17084):** each
board's `<b>-uart-open/write/close` is `[Uart, Device.Mmio]` and its
`<b>-gpio-open` + `<b>-pin-write/close` `[Gpio, Device.Mmio]`, so
board-level access is gated the way the foreword handles are. **The read
side exists (2026-08-18, root):** `gpio-read : linear Pin -> (linear Pin,
Boolean)`, `uart-recv : linear UartPort, Integer -> (linear UartPort, List
Integer)`, and the `SpiTxn` trio `spi-select`/`spi-transfer`/`spi-deselect`,
after the type checker learned to mint owners for the linear components of
a returned tuple (see "The read side" below), **and threaded into the
boards (main 17146/17148/17156: pin-read on 8, uart-recv on 3, the SPI
transaction on 6; 143 sub-tests).** Still open: Power (rulings 15) and ADC on
the boards that gain an ADC driver. See "What Is Actually Built" below.
**Upstream**: `docs/Reference/IoT/AGENT-PROMPT.md` deliverable 2 (target boards)

## What Is Actually Built

The HAL exists structurally. `codex/foreword/core/Board.codex` is the
interface chapter, and nine boards implement it:

| Board chapter | |
|---|---|
| `codex/boards/Stm32F4Board.codex` | `codex/boards/Stm32L4Board.codex` |
| `codex/boards/Esp32C6Board.codex` | `codex/boards/Rp2040Board.codex` |
| `codex/boards/Nrf52840Board.codex` | `codex/boards/Nrf9160Board.codex` |
| `codex/boards/Fe310Board.codex` | `codex/boards/QemuVirtBoard.codex` |
| `codex/boards/Pi4Board.codex` | |

Address maps, register constants, and the peripheral entry points are
there, and the smoke tests pass. That is the easy half.

**The half that matters is missing from the board chapters.** This
paragraph used to say the guarantees had no enforcement anywhere, and
that has not been true since the HAL grew real handles: a forgotten
`uart-close` is CDX2063 and a bus used after close is CDX2061, pinned by
`codex/test/hal-peripheral-linear` and its three probes, and `Flash`
(2026-07-16) adds the third leg -- a library without `[Flash]` in its row
cannot reach the bank, pinned by `errors/flash-launder-mmio`.

**Update 2026-08-18: the nine board chapters now thread the shipped
linear handles for UART and GPIO.** Each board grew `<b>-uart-open/write/
close` returning the foreword `UartPort` and `<b>-gpio-open` +
`<b>-pin-write/close` returning the foreword `Pin`, each wrapping the
board's own register-correct functions. So a program driving a board's
UART or GPIO now gets the open-once/close-once lifecycle the type system
enforces (CDX2063 on a dropped handle, CDX2061 on a reused one), verified
by each board's `*-drivers` smoke test under `build/boards-test.ps1` (all
nine green, 126 sub-tests). The linear GPIO `Pin` handle itself was added
to `Board.codex` the same day, mirroring the shipped UART trio.

**The per-peripheral capabilities now exist (2026-08-18, main 17063):**
`[Gpio]`, `[Uart]` and `[Spi]` are rows in `Capability.codex` (cap ids
18/19/20, kernel bits 27/28/29) mirroring `Flash`, and the foreword
`gpio`/`uart`/`spi` handle ops carry them, so a driver holding only
`Device.Mmio` is refused (CDX2031, three `hal-launder-mmio-*` tests). That
is the mechanism the load-bearing "a dependency without `[Spi]` cannot
reach the bus" claim in `ComplianceEvidence.md` needs. What the board
chapters still do **not** do: their own `<b>-uart/gpio/spi` wrappers still
carry `[Device.Mmio]` and need promoting to the new capabilities (a
program calling a board wrapper is not yet gated). SPI/I2C/ADC/Power handle
threading is also open: SPI and the read side need the tuple-returning ops
(`SpiTxn`, `uart-recv`, `gpio-read`) the shipped foreword deferred.

The remaining work is therefore: (1) promote the nine board wrappers to
the `[Gpio]`/`[Uart]`/`[Spi]` capabilities the foreword now carries
(**DONE, main 17084**), and (2) the tuple-returning and
remaining-peripheral handles, building on the UART/GPIO threading now
demonstrated end to end on every board.

**The read side, built 2026-08-18 (root).** The tuple ops were deferred
because the checker did not track them: a call returning `(linear Pin,
Boolean)` is not a `LinearTy` at the top, so `let (p2, v) = gpio-read p`
minted nothing and a dropped `p2` was silent, and the parser read
`linear` inside a tuple type as a type variable. Both closed in the same
CL: `TypeChecker.codex` "Linear Tuple Components" mints an owner per
linear component of a returned tuple (let-pattern, `when` on the call,
and act-bind then `when` on the name; a `_` at a linear position is
CDX2063; a bare return of the tuple owner from a def declared to return
that tuple type is sanctioned), and `parse-tuple-type-elem` gained the
`linear` arm. `Board.codex` "The Read Side" then carries `gpio-read`
(IDR +0x10), `uart-recv` (n polled bytes off the data register) and
`SpiTxn` with `spi-select`/`spi-transfer`/`spi-deselect` (CS word at
CR2 +0x04, DR +0x0C), all `[Gpio|Uart|Spi, Device.Mmio]`. Guards:
`codex/test/hal-tuple-linear` (all three, both binding forms, run under
codex-vm's RAM-backed windows) and `errors/hal-tuple-{leak,dup,wild,
owner-leak}` plus `errors/hal-spi-cs-leak` ("leaving CS asserted is
unrepresentable", now literally CDX2063). Design detail settled by the
checker: a wrapper hands a component back by destructure-and-rebuild
(`(Pin base pin, level)`), never by stashing the owner in a new tuple
(CDX2065). **Threaded into the boards the same evening (main 17146/17148/17156):**
`<b>-pin-read` on the eight GPIO boards; `<b>-uart-recv` on the three with a
receive primitive (Esp32C6, Pi4, Stm32F4 first; Fe310, Rp2040, Stm32L4 gained a
polled receive in main 17164 and both nRF an EasyDMA receive in 17194, so all eight UART boards); the linear SPI transaction
`<b>-spi-open/select/txn-transfer/deselect/close` on the six SPI boards
(Esp32C6, Fe310, Pi4, Rp2040, Stm32F4, Stm32L4; CS is a manual GPIO on each,
GPIOA on the STM32s). `build/boards-test.ps1` 9 green, 143 sub-tests. Open:
polled receive on the two nRF boards (UARTE is EasyDMA: RXD.PTR/MAXCNT +
STARTRX/ENDRX, a buffer-based read, not a FIFO poll; Fe310/Rp2040/Stm32L4
landed main 17164); I2C/ADC/Power handles. **Blocker for I2C/ADC/Power, found
2026-08-18:** they need capability bits 30, 31, 32, and the boot grant is
emitted as a sign-extended imm32 (`compiler-backlog` COMPILER-17): bit 31 would
grant every bit from 31 up and bit 32 would never be granted. **Sidestepped
2026-08-18 (root): the table's own unassigned bits 1, 2 and 13 carry `I2c`
(cs-id 21), `Adc` (22) and `Power` (23), so no emitter changed and COMPILER-17
stays latent for whatever capability comes next.** `Board.codex` "Linear I2C
Bus and ADC Unit" carries `I2cBus` with `i2c-open/write/read/close` (write and
read return `(linear I2cBus, ...)`) and `AdcUnit` with `adc-open/sample/close`
(`(linear AdcUnit, Integer)`), STM32 register shape (I2C CR1/CR2/DR, ADC
CR2/SQR3/DR), rows `[I2c, Device.Mmio]` / `[Adc, Device.Mmio]`. Guards:
`hal-i2c-adc-linear` (x86, RAM read-back), `errors/hal-launder-mmio-{i2c,adc}`
(CDX2031), `errors/hal-{i2c,adc}-leak` (CDX2063). `Power` gained its ops
2026-08-20 (main 17831): `sleep-deep` consumes the linear `Board`, which
now exists and is threaded through every open (see "The sleep rule",
BUILT). **Board threading of
I2C/ADC landed main 17183:** `<b>-i2c-open/close` and `<b>-i2c-bus-write-reg/
read-reg` on Esp32C6, Pi4, Rp2040, Stm32F4, Stm32L4 (register-level ops through
the handle, the shape the boards already had), `rp-adc-open/unit-sample/close`
on RP2040 (the only board with an ADC driver); boards-test 152 sub-tests.
**Board threading of ADC landed main 17777 (2026-08-20):** ADC1 drivers
plus `<b>-adc-open/unit-sample/close` on Stm32F4 (0x40012000, the RM0090
shape the generic HAL already carries) and Stm32L4 (0x50040000, RM0351
shape: DEEPPWD/ADVREGEN wake, ADSTART, close by ADDIS), and SAADC-backed
wrappers on both nRF boards (nRF9160 gains its SAADC section at
0x4000E000, non-secure id 14; the EasyDMA result word lives at sram-top
minus 320, below the SPIM rx buffer at minus 256). Five of nine boards
now carry `AdcUnit`. Esp32C6 is the one remaining candidate: its SAR ADC
register map is not verifiable from anything in-tree, so it was left
alone rather than guessed; Fe310, Pi4 and QemuVirt have no on-chip ADC.
boards-test 9 green, 161 sub-tests.

**`Flash` shipped 2026-07-16 (blu)** -- it was the highest-value single
piece of this design, and it is the first peripheral to get the full
surface this document specifies rather than only half of it.

`Foreword chapter Board` carries a linear `FlashBank` with the lifecycle
the hardware demands -- `flash-open-bank` → `flash-write-page`* →
`flash-seal-bank` -- typed `[Flash, Device.Mmio]`. Both halves of that row
are load-bearing: `Flash` is the authority and `Device.Mmio` is the
mechanism, and declaring only the former would launder the register
access the Mmio row exists to make visible. `Flash` is a capability in
its own right (cap id 15, kernel bit 24) across all seven tables, so
"this firmware can rewrite its own boot image" is a signed line in the
manifest rather than a comment. It is deliberately not `Device.Flash`:
a dotted effect answers to its base, so that spelling would have filed
the authority in the same slot the UART driver already occupies, and the
distinction is the entire point of the capability.

The guarantees this document sells are real *for flash specifically*:
a dropped bank is CDX2063, a double seal is CDX2061, a write after seal
is CDX2061, and a driver holding `Device.Mmio` but not `Flash` is
refused CDX2031 (`codex/test/errors/flash-launder-mmio`, whose body is
identical to the passing `codex/test/hal-flash-linear` but for the
effect row). What flash does **not** yet have is a board chapter that
implements it against a real controller -- the HAL carries the STM32F4/L4
register shape on a generic base, exactly the reach the UART and SPI
handles beside it have. That is the same remaining work the rest of this
document describes.

## The Problem

IoT firmware talks to GPIO, UART, SPI, I2C, ADC, and power-state
hardware. Every existing embedded HAL (vendor SDKs, Zephyr drivers,
Arduino cores) is a C function library where leaving a bus
half-configured, double-releasing a peripheral, or sleeping with a
transaction in flight is a runtime bug. Codex has the two language
features that turn these into compile errors -- linear types and
effect rows -- and a driver corpus (`codex.os.kernel`, 22 modules:
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
`Hal` (interfaces only -- no register addresses in the foreword):

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

- Forgetting `spi-close` → CDX2063 (linear never used -- leak).
- Using a bus after close → CDX2061 (used more than once).
- A library function without `[Spi]` in its row cannot touch the
  bus at all -- a compromised dependency cannot reach hardware it
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
that reaches `sleep-deep` -- the handles cannot be implicitly
discarded (CDX2063) and the board cannot be duplicated to smuggle
one past (CDX2061). "A device that sleeps while holding a linear
SPI handle is a compile error" is then literally true, using only
the checker that exists today. Wake from deep sleep re-enters
`opening` (MCU reset semantics), which matches the linear story:
nothing survives, so nothing can dangle.

**BUILT 2026-08-20 (root, main 17831; rulings queue 15 ruled (a) by
Damian).** `Board.codex` carries `Board (pwr-base) (scb-base)` (the two
register blocks sleep entry programs), `board-open`/`board-close` (pure;
close is the mundane disposal a program that never sleeps needs), and a
Deep Sleep section: `sleep-light`, `sleep-deep` (consumes the Board, sets
LPMS from `SleepConfig.mode` and SCB SLEEPDEEP, RM0351 5.3 shape on the
Board's own bases, stopping one instruction short of WFI as the L4
chapter does), `wake-source` (PWR SR1: WUF1-5 -> WakePin, WUFI ->
WakeTimer, else WakeReset). The five foreword opens and all 33 board
wrapper opens re-signed to `linear Board, ... -> (linear Board, linear
H)`. Guards: `errors/hal-sleep-open-handle` (open board, open pin, sleep
without closing: CDX2063 -- the arm this ruling exists for),
`errors/hal-board-dup` (CDX2061), `errors/hal-board-leak` (CDX2063),
`hal-board-sleep` (positive: lpms/SLEEPDEEP readback, wake
discrimination, sleep-light clears). The 5 hal tests and 16 existing
refusal arms re-threaded with outputs and codes unchanged; boards-test 9
green, 161 sub-tests. Gate green, hard fixed point in one pass; the Sut
came out byte-identical to the depot seed (whole-program DCE never
reaches the HAL), so no seed moved. **The flash hole is CLOSED (main 17839, 2026-08-20, Damian-directed):
`flash-open-bank` threads the Board too.** OtaBoot's whole selector
family (`boot-store`/`boot-mark`/`boot-give-up-store`/
`boot-run-candidate`/`boot-run`/`boot-commit`) and Lwm2mFirmware's
download chain (`fw-write`/`fw-stage-block`/`fw-stage-last`/
`fw-feed-response`) re-signed to take and return it; outputs of
`ota-boot-rollback` and `ota-lwm2m-loopback` byte-identical.
`errors/hal-sleep-open-bank` pins the guarantee: deep sleep with an
unsealed bank is CDX2063. One reusable piece fell out: `board-pair :
linear Board, a -> (linear Board, a)` in `Board.codex`, the canonical
destructure-and-rebuild -- a Board returned by a call cannot be placed
directly into a result tuple (CDX2065), and every threading site that
returns `(board, value)` goes through it. Every peripheral this design
names, flash included, now rides the sleep rule.

### Board chapters -- BUILT (as plain functions, not effect ops)

Per target, a chapter holding what X86_64Boot.codex holds for the PC:
the address map as named constants, and the register-level
implementations. This part shipped -- nine chapters, listed above,
living in `codex/boards/` against the `Board.codex` interface.

What they do *not* yet do is implement the Hal effect operations,
because those effects do not exist. The register pokes are there; the
typed shell around them is what remains.

| Chapter | Contents |
|---|---|
| Stm32F4Board | Flash 0x08000000, SRAM 0x20000000, peripherals 0x40000000+, RCC clock-enable sequences, GPIOA..I, USART1-6, SPI1-3, I2C1-3, ADC1-3, EXTI/NVIC tables, Stop/Standby entry |
| Esp32C6Board | SRAM 0x40800000 region, GPIO matrix, UART0-1, SPI2, I2C0, ADC1, LP-core mailbox, TWT/light-sleep hooks |
| Pi4Board | BCM2711 peripheral base, GPIO, PL011 + mini UART, SPI0, BSC I2C, no ADC (note in docs), GIC routing |

(Stm32L4, Rp2040, Nrf52840, Nrf9160, Fe310, and QemuVirt followed the
same shape.)

Numbers come from RM0090, the ESP32-C6 TRM, and the BCM2711
peripherals doc (catalogued in `docs/Reference/IoT/Hardware/`). Every
constant is a named definition -- the TRACE-ALLOC incident taught
that magic addresses baked into emit functions are a class hazard;
board constants live in exactly one chapter per board.

### MMIO primitives

The x86 drivers use port I/O and memory-mapped access already; the
HAL standardizes on two intrinsics (`__mmio-read`, `__mmio-write`,
each in 8/16/32-bit widths) that each backend emits natively
(x86: mov; ARM/RISC-V: ldr/str -- with the volatile-ordering
guarantee that the emitter never reorders or elides them; Cortex-M
additionally needs a dmb on device-memory writes that gate DMA).
Bounded-integer types (`Integer between 0 and #FFFFFFFF`) type the
register values; `#`-literals (CL 3837 decision) spell the masks.

### Interrupts

Phase 1 is polled I/O -- the blink/sensor/print demo needs no
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
  HAL phase -- it is a large driver project gated behind it.
- `OTAFirmwareUpdate.md`: needed a flash-write capability -- a `Flash`
  effect with the same linear shape (open bank, write pages, seal).
  **Shipped 2026-07-16; OTA is no longer blocked.** Its gates,
  anti-rollback, and manifest verification were already built, and the
  staging write now has a typed path to the bank. The wiring landed the
  same day: `ota-step` signals `ActionWriteBlock data offset` and
  `codex/os/net/Lwm2mFirmware.codex` calls `flash-write-page` with it,
  proven by `codex/test/apps/ota-lwm2m-loopback`.
- `[HardRealtime]`-annotated sensor reads and
  motor control loops use `[Gpio]`, `[Clock]`, `[Adc]` effects --
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
operations must not allocate per call in hot loops -- `uart-send`
and `spi-transfer` write through the existing buffer primitives
(`__buf-write-bytes`) rather than building intermediate lists.
`List Integer` for payloads is acceptable at bring-up sizes;
the buffer-typed variants are the documented hot path. No
recursion in driver code without a fuel cap. Verdict: low risk if
the buffer discipline is stated in the chapter prose from day one.

## Open Questions

1. **Where do implementations live** -- settled by the code: the
   interface is `codex/foreword/core/Board.codex` and the
   implementations are a top-level `codex/boards/` quire. PC drivers
   and MCU boards stay separate, and the dependency order rule
   (foreword → codex → kernel → os) is satisfied.
2. **DMA.** Out of scope for every Phase-1 peripheral; SPI bulk
   transfer on H7 will eventually want it. Linear DMA descriptors
   are a clean fit but a new design -- defer.
3. **Pin multiplexing conflicts.** Two opens of the same pin
   through different peripherals (PA9 as GPIO vs USART1-TX) is a
   runtime `Result` failure at open in this design. A type-level
   pin registry (board record carrying per-pin linear tokens) is
   possible but heavy; revisit after first real firmware.
4. **Clock tree configuration.** STM32 RCC setup is its own small
   language of dependencies. Phase 1 hardcodes one known-good
   configuration per board in the board chapter; a typed clock-tree
   model is explicitly deferred.
5. **Hardware crypto dispatch. DESIGNED 2026-08-21 (root, red's draw);
   the row is "Hardware crypto dispatch" under The Design below.** Moved
   here from `ThreatModel.md` recommendation 2 (fester 2026-08-21) and
   shaped by ruling 19 (red, 2026-08-20): the foreword defines the API,
   board chapters provide the implementation, and the capability manifest
   says whether hardware crypto is in use. What the row adds is the
   measurement that decides what can be BUILT, which is one board, and
   the two rules that keep the dispatch honest: the software path is the
   control, and absence refuses.

### Hardware crypto dispatch (root, 2026-08-21)

**The measurement first, because it decides the shape.** Nine board
chapters, and what each cites for its register map, what of that the
tree holds, and what the HAL threads today:

| board | cites | held in-tree | crypto block per the cited document | HAL threads |
|---|---|---|---|---|
| `Stm32F4Board` (F407) | RM0090 | no; `STM32-Reference.md` (2.8 KB) names "RNG, Hash, Crypto (on some variants)" and "True RNG" | RNG on the F405/F407; CRYP/HASH on the F415/F417 variants only | GPIO, UART, SPI, I2C, ADC, Power, Flash |
| `Stm32L4Board` (L4x6) | RM0351 | no; same summary | RNG; AES on some L4 parts | the same set |
| `Nrf52840Board` | nRF52840 PS v1.7 | no | RNG, ECB (AES), CCM, CryptoCell-310 | GPIO, UART, SPI, I2C, ADC, Power, Flash |
| `Nrf9160Board` | nRF9160 PS v2.1 | no | CryptoCell-310 | the same set |
| `Rp2040Board` | rp2040-datasheet.pdf | no | none (a ring-oscillator random bit; no TRNG, no AES, no SHA) | GPIO, UART, SPI, I2C, ADC, Power, Flash |
| `Esp32C6Board` | ESP32-C6 TRM | no; `ESP32-C6-Reference.md` (2.5 KB) names RSA-3072 secure boot, AES-XTS flash encryption, an HMAC peripheral | AES, SHA, RSA, HMAC, ECC, TRNG | GPIO, UART, SPI, I2C (no ADC either: the map was unverifiable in-tree, the ADC ruling) |
| `Fe310Board` (G002) | FE310-G002 Manual v1p1 | no | none | GPIO, UART, SPI, Power |
| `Pi4Board` (BCM2711) | BCM2711 ARM Peripherals | no; `RaspberryPi-Reference.md` (1.3 KB) names nothing of it | an RNG block; AES/SHA as ARMv8 CPU instructions, not a peripheral | GPIO, UART, SPI, I2C, Power |
| `QemuVirtBoard` | the QEMU virt machine model | the model is the bed | virtio-rng, a DEVICE the bed attaches; AES/SHA only as CPU instructions under `-cpu cortex-a72` | UART |

Two things the table says that the question did not. **No board's crypto
register map can be cited from a document the tree holds**: the nine
chapters cite reference manuals by name and section, none of which is in
`docs/Reference`, and the three in-tree summaries name blocks without a
single register. That is the wall the Esp32C6 ADC hit ("do not add it from
memory"), and it stands here for every block in the fourth column, which is
written from memory and says so. **One board's block is a device the bed
can attach and the spec of which the tree already implements**: QEMU virt's
virtio-rng is VIRTIO device id 4 on the same virtio-mmio transport
`codex/foreword/core/VirtioBlk.codex` drives for id 2, through the same
`vb-find` slot probe, one queue, and requests that are one device-written
buffer with no header. Nothing else here has silicon.

**The shape, in this design's own convention** (board chapters are plain
functions threading linear handles, not effect ops; see "Board chapters"):

```
  effect Rng where
    rng-open   : linear Board, Integer -> [Rng, Device.Mmio] (linear Board, Result (linear RngUnit))
    rng-read   : linear RngUnit, Integer -> [Rng, Device.Mmio] (linear RngUnit, Result (List Integer))
    rng-close  : linear RngUnit -> [Rng, Device.Mmio] Integer

  effect Crypto where
    aes-unit-open      : linear Board, Integer -> [Crypto, Device.Mmio] (linear Board, Result (linear AesUnit))
    aes-unit-encrypt   : linear AesUnit, List Integer, List Integer -> [Crypto, Device.Mmio] (linear AesUnit, List Integer)
    aes-unit-close     : linear AesUnit -> [Crypto, Device.Mmio] Integer
    sha-unit-open / sha-unit-digest / sha-unit-close, the same shape
```

`[Rng]` and `[Crypto]` are capability rows beside `[Adc]` and `[Power]`
(`cs-id` 24 and 25; the bit table's free bits are chosen at build time, not
here, because the table moved twice this week). A program that touches a
unit declares the row on `opening`, and the grant is what the manifest
shows: hardware crypto in use means the bit is granted, and nothing else.

**Rule 1: the software path is the control, and the arm is the same
answer.** `aes-encrypt-block`, `aes-cbc-encrypt`, `sha256` and the rest of
`codex/foreword/core/` stay exactly as they are and stay the thing every
caller reaches for by default. A board's unit is a SECOND implementation
of the same function, and the arm that admits it is the one `ddc` and the
rechecker already taught this project: the same input through the unit and
through the software path must give one answer, on the board, on every
vector the software path's own known-answer tests carry. A unit that has
no such arm is not wired. For an RNG there is no same answer; its controls
are the refusal below and a reading (two reads differ, a 64-byte read has
at least sixteen distinct bytes), which is weak and is said to be weak.

**Rule 2: absence REFUSES, it never falls back.** There is no dispatcher
that picks the unit when present and the software otherwise; that is the
silent shape, and a caller who wanted the hardware would learn from a
timing that they did not get it. `rng-open` on a board whose chapter has no
unit does not exist, which is a compile-time refusal (CDX3002), the
strongest kind; `rng-open` on a board whose unit is declared and absent at
run time (the bed without the device attached, silicon fused off) answers
a `Result` failure and the caller decides. A caller that wants "hardware if
you have it" writes that choice at the call site where a reader can see
it. The manifest therefore says exactly one thing, and it is true: `[Rng]`
granted means a unit was opened.

**BUILT, step (1), 2026-08-21 (root), and one deviation from the shape
above is deliberate.** `Result` at open is spelled as a unit whose device
base is zero: `rng-open` always hands back a linear `RngUnit` (the checker
tracks a linear inside a tuple, not yet inside a `Result` constructor), a
zero-base unit is one that was asked for and not there, `rng-present` says
which, and every read through it answers zero bytes. The refusal is the
same, the handle discipline is unchanged, and the caller still cannot be
handed a software generator by mistake. `VirtioRng.codex` reuses
`VirtioBlk`'s transport by name, `QemuVirtBoard` threads the linear Board
through `qemu-rng-open/read/close`, `[Rng]` is `cs-id` 24 at bit 30 (bits
0 to 29 were all taken; COMPILER-17's imm32 hazard sits at 31 and is still
latent), and the two arms are `codex/test/qemu-rng` and `qemu-rng-absent`,
the same program routed to the arm64 bed by a `.qemudev` sidecar, one
attaching `virtio-rng-device` and the other attaching nothing. The x86
battery skips both by `.skip`, which names the runner that does run them.
Measured on the bed before any of the rest existed: with the device
`rng found=y n1=64 n2=64 distinct=ok differ=y`, without it `rng absent`.

**What gets built, in order, and what waits.** (1) `VirtioRng` beside
`VirtioBlk` in the foreword: id 4, `vb-find` reused, one queue, the device
fills the buffer; `QemuVirtBoard` gains `qemu-rng-open/read/close`
threading the linear Board; the `[Rng]` row; the arms are
`codex/test/qemu-rng` (positive, `-device virtio-rng-device` attached the
way `test-cross-disk.ps1` attaches the block device) and `qemu-rng-absent`
(same binary, no device, `rng-open` answers the failure and the program
says so), both on the arm64 bed, which is the only bed that can. That is
one CL, seed-affecting for the capability row, and it is the whole of what
this question can PROVE today. (2) Every other board's unit waits for its
register map to be citable, which means the manual in `docs/Reference` or
the silicon on the bench; the first one with either is the Esp32C6's
TRNG, because its summary is the only in-tree document that names a block,
and the C6 is also the board the ADC ruling refused for the same reason,
so the two wait together. (3) AES/SHA units wait for a board with a
citable map AND a bed or bench to run the same-answer arm on; none
qualifies today, and the design says so rather than scheduling one.

**Steps (2) and (3) are BLOCKED, 2026-08-21 (root, red's ruling), and the
block is a document to acquire, not a mystery: there is no board crypto
manual in `docs/Reference`.** What the tree holds for the nine boards is
three summaries, `STM32-Reference.md` (2.8 KB), `ESP32-C6-Reference.md`
(2.5 KB) and `RaspberryPi-Reference.md` (1.3 KB), each naming blocks and
none naming a register; the PDFs there are the Intel NICs, USB, xHCI, HID
and AMI, none of them a board. The first of RM0090, RM0351, the nRF52840 or
nRF9160 PS, the ESP32-C6 TRM, the FE310-G002 manual or the BCM2711
peripherals document to land in `docs/Reference` unblocks that board's
unit, the C6 TRM first. Until then the row gains only what needs no map:
**the compile-time half of rule 2 has its arm**, `errors/hal-rng-no-unit`,
which calls `rp-rng-open` on the Rp2040, the one board the table says has
no TRNG at all, and pins `CDX3002` at the call site. Its control is the
same program on `QemuVirtBoard` through `qemu-rng-open`, which compiles
clean, so the refusal is the name and nothing else. The arm goes red the
day somebody backs `rp-rng-open` with the ring oscillator, which is the
silent shape rule 2 exists to refuse.
