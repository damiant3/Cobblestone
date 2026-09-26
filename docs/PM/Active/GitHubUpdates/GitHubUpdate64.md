# GitHub Update 64

Pushed from main 29237 plus its release documents. The release seed is
`533C6D630D8660E6` (`TechnicalDetails.md` carries the full digests).

## The serial-ring stall under QEMU (Steve Howell's report)

Steve saw the Update 63 compiler stop reading its input twice under QEMU
`-cpu max` in software emulation, once in a codexir compile and once in an
x86emit transpile, with the guest halted and data waiting on the wire.

The cause is COMPILER-104, fixed in this update (reek, main 29170). The
serial reader waits for input with `hlt` after one direct poll of the UART,
and that poll leaves the byte it read in RAX. The timer interrupt chose which
interrupt controller to acknowledge by comparing the interrupted program's
RAX, not the vector, against the local timer's vector 48. When the byte was
`0` (48) and a PIT tick woke the `hlt`, the acknowledgement went to the
local APIC, the PIC's in-service bit for IRQ0 never cleared, and the PIC
delivered no timer or serial interrupt again. The fix acknowledges once,
where the PIT and local-timer paths meet and RAX still holds the vector.
codex-vm could not show the defect because its PIC model never holds an
in-service bit (L-ARENA).

Reproduced on the QEMU recipe `build/vm-config.ps1` uses (TCG, `-cpu max`,
`kernel-irqchip=off`), feeding the compiler 512-byte bursts of `0` every
30 ms: the Update 63 seed stalled in 3 of 3 runs, each within 7 seconds,
halted at the reader's `hlt` with RAX = `0x30`, one byte unread in the ring,
and `info pic` showing `isr=01`; the seed carrying the fix read 2.9 MB in
180 seconds with no stall. The trigger needs only an empty ring, a `0` byte
and a timer tick, so the exact 1 MB offsets Steve reported are not explained
by the guest; they are unexplained here.

The comment in `Invoke-VmCompileFallback` (`build/vm-config.ps1`) that
"sending the unit in one burst stalls intermittently on large units", and
the 8 KiB chunks and 5 ms gaps it adds, describe the same symptom; that
they share this cause is not measured. The pacing stays for this release.

## Generic equality by dictionary passing (val)

`Eq a =>` now compiles by passing an equality dictionary, so a generic
function can compare values of its type parameter. Primitive dictionaries
cover Integer, Boolean, Text and Char; a derived `Eq` on a user type supplies
its own. Acceptance is `codex/test/generic-eq` (30 rows, each beside the
direct `==`), with five refusal chapters under `codex/test/errors/generic-eq-*`.
Design: `docs/Designs/Active/Compiler/GenericEquality.md`.

## ModBuilder (val, reek, root)

`apps/modbuilder` is new: the pitch page and backlog, a page built from Codex
(`ModBuilderPage.codex`, MB-2), and the game-modding parts moved out of Prism
into the app (MB-3).

## Smaller

- WORKS-76: a VMX preemption timer bounds a guest that never exits, and a
  crashed row names the exit reason (reek 29179).
- Plugs 2.80: the arm64 data abort in `codex/test/ops/cap-grant-emit` at the
  bit-31 value is localized (reek 29186).

## Release proof

The full gate passed in one run on main 29237: a one-pass hard fixed point,
242 declared refusals, all 1,776 test chapters compiling, 3,422 test-run
checks (1,726 compiled, 1,696 run), cross-architecture smoke, plug smoke
byte-identical on codex-vm and QEMU across 8 subjects, 57 generators with no
drift, UEFI console output, deck headroom (tightest margin 2.17) and 59 hosted
wasm programs. IR fidelity graded its controls and reported no unexpected
result.

The battery (`-Tier all`) on the release seed passed 1,978 of 2,034 tests
with no failures and 56 declared skips; the three oracles agreed with the
host (2,013 scalar, 130 vector, 1,485 CCE with 31 documented gaps). The
poison battery, on a seed built from the release source with a 0xCD fill,
passed the same 1,978 of 2,034 with no failures. The app sweep compiled 301
of 302 units clean, the remaining one its known baseline failure, with no
regressions.

Roslyn built the freshly emitted C# compiler. Both DDC arms produced
3,791,766 bytes with zero differences from the release seed outside offsets
40..135. The symbol map matched all 6,352 embedded MAP1 rows by name,
address and size.

Both boot images were rebuilt on the release seed. All 58 diagnostic
rehearsal arms passed across Codex VM and QEMU/OVMF with a 180-second minimum
arm allowance, `ovmf-cad-ps2` (COMPILER-104's arm) among them, and the
shipping check confirmed the default configuration. The diagnostic image
SHA-256 is
`04736D4C0CFC61F504BF3E2E1C89175561B19BD1E8E80DD22C924006C8559844`.

The box sampler died at launch on this release (its output name collided
with the Update 63 record, which is read-only), so the gate, battery, sweep,
poison battery and DDC ran unsampled. `docs/Agents/box-release-2026-09-25-u64.csv`
covers 18:34 to 18:54, the image builds and the rehearsal: a free-memory floor
of 6.33 GiB at 18:35 and at most one guest at a time.
