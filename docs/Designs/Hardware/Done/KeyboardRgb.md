# Keyboard RGB Lighting Control

**Date**: 2026-06-22
**Status**: Active
**Depends on**: xHCI USB (done), USB device enumeration (done),
PCI bus (done), Color.codex palettes (done)
**Unblocks**: QMK/VIA keyboard RGB control, OS-keyboard color sync,
keyboard configurator app

---

## Purpose

Support hobby mechanical keyboards (QMK/VIA firmware) with per-key
RGB lighting control from Codex OS. Read current config, set RGB
modes, apply per-key colors, and sync keyboard colors with the OS
theme/palette. Tested with X-Bows ergonomic keyboard via VIAL.

---

## Architecture

```
  App Layer              OS Layer              Kernel Layer
  ─────────              ────────              ────────────
  KeyboardManager  ──►  KeyboardRgb     ──►  QmkProtocol
  (cvmm app)            (high-level API)      (VIA raw HID)
                                               │
                                          UsbHid
                                          (HID class driver)
                                               │
                                          Xhci + Usb
                                          (transfer rings)
```

---

## Modules

### 1. UsbHid (`codex/os/kernel/UsbHid.codex`)

USB HID class driver. Scans for HID interfaces, sends SET_REPORT
control transfers, and queues interrupt OUT transfers.

- Find HID interfaces by class (3) and usage page
- GET_REPORT / SET_REPORT control transfers
- Interrupt endpoint IN polling (keyboard input over USB)
- Interrupt endpoint OUT writes (RGB commands to device)
- Raw HID report send/receive (64-byte reports for VIA protocol)

### 2. QmkProtocol (`codex/os/dev/QmkProtocol.codex`)

VIA/QMK raw HID command protocol. Sends 32-byte command packets
over the raw HID interface (usage page 0xFF60, usage 0x61).

VIA protocol commands:
- `via-get-protocol-version` (0x01)
- `via-get-keyboard-value` (0x02) -- backlight, rgblight info
- `via-set-keyboard-value` (0x03) -- set backlight/RGB state
- `via-rgblight-get-info` (0x02, 0x80) -- mode, HSV, speed
- `via-rgblight-set-hsv` (0x03, 0x80) -- set global HSV
- `via-rgblight-set-mode` (0x03, 0x81) -- set effect mode
- `via-rgblight-set-speed` (0x03, 0x82) -- set effect speed
- `via-per-key-set-color` -- QMK RGB Matrix per-key HSV

### 3. KeyboardRgb (`codex/os/dev/KeyboardRgb.codex`)

High-level RGB API that abstracts over the VIA protocol.

- `kbd-rgb-set-mode : RgbMode -> ...` -- solid, breathing, rainbow, etc.
- `kbd-rgb-set-color : Integer, Integer, Integer -> ...` -- HSV
- `kbd-rgb-set-brightness : Integer -> ...` -- 0-255
- `kbd-rgb-set-speed : Integer -> ...` -- 0-255
- `kbd-rgb-apply-palette : RainbowPalette -> ...` -- sync with lolcat palette
- `kbd-rgb-set-key-color : Integer, Integer, Integer, Integer -> ...` -- key index, H, S, V
- `kbd-rgb-sync-theme : Palette -> ...` -- map OS theme to keyboard colors

RGB modes (matching QMK/VIA):
- Solid, Breathing, Cycle All, Cycle Left/Right, Cycle Up/Down,
  Rainbow Chevron, Rainbow Pinwheel, Jellybean, Reactive, Typing
  Heatmap, Digital Rain

### 4. KeyboardManager (app in cvmm)

GUI page with:
- Detected keyboard name/firmware version
- RGB mode selector (dropdown)
- Color picker (existing ColorPicker integration)
- Brightness/speed sliders
- Palette selector (reuses RainbowPalette from Color.codex)
- "Sync with OS theme" toggle
- Per-key color map (future: visual keyboard layout)

---

## VIA Protocol Reference

Commands are 32-byte raw HID reports. First byte is the command ID.
Response comes back as a 32-byte report on the same interface.

| Byte 0 | Byte 1 | Command |
|--------|--------|---------|
| 0x01 | | Get protocol version |
| 0x02 | 0x80 | Get RGB Light info |
| 0x02 | 0x81 | Get RGB Matrix info |
| 0x03 | 0x80 | Set RGB Light HSV (bytes 2-4: H lo, H hi, S, V) |
| 0x03 | 0x81 | Set RGB Light mode (byte 2: mode) |
| 0x03 | 0x82 | Set RGB Light speed (byte 2: speed) |
| 0x04 | | Dynamic keymap get |
| 0x05 | | Dynamic keymap set |

HSV values: H = 0-360 (two bytes LE), S = 0-255, V = 0-255.

---

## Commit Plan

1. UsbHid.codex -- HID class driver
2. QmkProtocol.codex -- VIA command protocol
3. KeyboardRgb.codex -- high-level RGB API + palette sync
4. KeyboardManager in cvmm app -- GUI configurator
