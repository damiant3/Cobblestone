# USB / xHCI Boot Diagnostics

Two standalone Option A GOP payloads for diagnosing USB bring-up on
real hardware, where there is no serial log and the ceremony cannot
advance if the keyboard does not work. Each paints its findings to the
framebuffer and sits (it never returns), so it can be photographed off
the glass. They read the same diagnostic cell block (36200) that the
real xHCI bring-up fills (`apps/works/GopXhci.codex`), so the numbers
are exactly what the boot path saw.

Use these when a machine boots the Codex payload but finds no USB
keyboard, no boot stick, or nothing on the USB bus at all.

## XhciTruthProbe.codex

Runs the runtime spine, enumerates the USB bus (the full xHCI bring-up:
ownership handoff, halt/reset, port power, Intel EHCI->xHCI routing,
device enumeration), then paints the xHCI reading and halts:

- controller vendor:device, caplen
- HCCPARAMS1 with CSZ (32- vs 64-byte contexts), PPC, xECP
- ownership handoff: legsup present, before/after words, BIOS released
- slots / ports / reset / cnr / run / connected count
- Intel USB2 routing applied + masks
- raw PORTSC of every root port (CCS/PED/PR/speed)
- ENUMERATED: whether the keyboard, mouse, and disk were configured

Reading it: `found=n` means no xHCI on the PCI bus (an EHCI-only
board). `connected=0` with devices plugged in points below enumeration
-- port power or chipset routing (check the Intel routing line and
PPC). Live connect bits with no enumerated device point above it.

## KbdDiagProbe.codex (v8)

For the case where the keyboard enumerates (`uk-ok=y`) but delivers no
keystrokes. Paints the xHCI summary and the keyboard endpoint
parameters, then runs three timed phases (~90s, ~45s, then forever;
tick-driven with a paint-count fallback). Hold a key in EACH phase:

- **Phase 1** -- the endpoint-attributed USB pump (below), with
  findings rewritten to KBDDIAG.TXT.
- **Phase 2** -- the OWNERSHIP HANDBACK experiment, the feasibility
  test for a permanent "no USB keyboard, fall back to PS/2" boot
  feature: halt the controller, restore the firmware's own SMI
  enables (recorded at diag index 39 by the handoff), clear OS-owned,
  then count PS/2 arrivals on both routes (IRQ1 mailbox + a
  floating-bus-guarded port 0x60 poll -- SMM emulation on some boards
  only answers the polled port). `PS2` climbing here = the firmware
  revived its legacy keyboard emulation = the fallback is real.
  `reclaim=y` = the BIOS re-took ownership. No file writes in this
  phase (the controller is the firmware's).
- **Phase 3** -- REACQUIRE: the full bring-up runs again and the
  phase-2 verdict is written to the file. `reacq kbd/disk/mount` all
  `y` proves ownership can be juggled per phase -- the strongest form
  of the fallback feature.

What the pump counters mean:

1. **Events are attributed to their endpoint.** The transfer event
   TRB's control dword carries the endpoint id in bits 20:16; earlier
   probes counted ANY transfer event, so one leftover EP0 control
   completion read as "the interrupt endpoint fired once." `EPINT`
   counts only completions whose endpoint id equals the keyboard's
   dci; `EP0` and `OTH` count the impostors; `LATCH` counts codes
   taken from the per-slot latch (endpoint id already lost there).
2. **Findings are written to KBDDIAG.TXT** on the boot stick's own
   ESP whenever the counters change (capped at 250 rewrites). After a
   real-hardware run, mount the stick and read the file -- no
   photographing the glass. Only the disk usb-attach itself published
   is written (the selection cells are pinned to the USB medium);
   internal AHCI/NVMe drives are never touched. No USB disk -> no
   file, screen only.

**Findings also render as QR codes** (R-1 of TheSilentKeyboard.md):
the same body that goes to KBDDIAG.TXT is drawn as three version-5
codes below the text, re-rendered whenever the counters change and
fully independent of the disk. On real hardware, PHOTOGRAPH THE
CODES with any phone camera — decode the photo on the dev box with
`python <scratchpad>/qrshot.py photo.jpg` (cv2; chunks carry `i/n;`
prefixes and reassemble automatically). Validated end to end: OVMF
screenshot decodes 6/6 under a simulated hand-held photo
(perspective, uneven light, blur, noise, phone JPEG).

Press and HOLD a key:

- `EPINT` climbs when the interrupt-IN endpoint completes a transfer
  (this is the verdict number)
- `code` is the completion code (01 = success, 0d = short, other =
  error); `resid` is the event's residual byte count
- `ctl` is the raw event control dword; `trb` is the completed TRB's
  address, `ring` the keyboard transfer ring's base -- matching
  prefixes prove the completion points at our interrupt ring
- `SCANS` climbs when a scancode decodes from the report
- `REPORT` is the raw 8 bytes the controller DMAs into the boot-report
  buffer: `[modifiers, reserved, key1..key6]`. Hold a key and byte 2
  should show the key's HID usage.

Reading it:

| Observation | Meaning |
|---|---|
| `EPINT` stuck at 0, `EP0`/`LATCH` nonzero | The "one event then silence" was enumeration residue -- the interrupt endpoint has NEVER delivered; look at scheduling (interval, root-hub FS servicing) |
| `EPINT` stuck at 0, everything 0 | Controller never completes the transfer -- endpoint not polled (interval / doorbell / ring on real silicon) |
| `EPINT` climbs, `code` != 01 | Transfers complete with an error -- report-buffer or stall problem |
| `EPINT` climbs, `code`=01, `REPORT` all zero while held | Transfer completes but delivers no data |
| `REPORT` byte 2 nonzero while held, `SCANS`=0 | Data delivered; the HID decode is the bug |
| `EPINT` and `SCANS` both climb | The pump works; the bug is in the consumer wiring, not the driver |

The counters are zeroed at start and the status line repaints on a
fixed iteration count (not the PIT tick, which is unreliable on some
firmware), so nothing here depends on the timer, and the per-iteration
pump path allocates nothing.

## Build and run

```powershell
# Build a bootable image (menu-only, no seed/font needed):
build/boot/build-option-a.ps1 -Src build/boot/diag/XhciTruthProbe.codex `
    -Out build/boot/xhci-probe.img -Seed '' -Font ''

# Verify under real UEFI (OVMF) with USB devices on qemu-xhci:
build/boot/test-ovmf.ps1 -Img build/boot/xhci-probe.img `
    -Out probe.png -UsbDisk -UsbKbd -UsbMouse -NoPs2
# -NecXhci swaps in a different controller model as a second opinion.

# On real hardware: flash and boot (re-flash before EVERY boot -- a
# stick that re-entered Windows has a corrupted GPT):
build/flash-usb.ps1 -Image build/boot/xhci-probe.img -DiskNumber N   # elevated
```

The keyboard probe is the same build/flash flow with
`-Src build/boot/diag/KbdDiagProbe.codex`. On the keyboard probe no
keypress is needed to read the screen; press keys only to exercise the
pump.
