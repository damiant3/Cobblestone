# ⚠️ PHONE WIPE — Flash TWRP to Samsung Galaxy S7 Edge

**THIS PROCEDURE REPLACES THE RECOVERY PARTITION ON A REAL PHONE.**
**IF SOMETHING GOES WRONG, YOU WILL NEED ODIN + STOCK FIRMWARE TO RECOVER.**
**THE PHONE IS EXPENDABLE. YOUR PATIENCE MAY NOT BE.**

---

**Phone**: SM-G935T (T-Mobile), Samsung Galaxy S7 Edge
**Image**: `phone/flash/recovery.img.tar` (25,090,048 bytes)
**Tool**: Odin v3.x (Windows)
**Date**: 2026-03-24
**Author**: Agent Windows

---

## What's Been Verified

| Check | Status |
|-------|--------|
| Boot image header format matches Samsung spec | ✅ Proven (self-test + compare-headers.py) |
| `dt_size` field set correctly (6,414,336) | ✅ Proven (the field `abootimg` gets wrong) |
| SEANDROIDENFORCE trailer present (16 bytes) | ✅ Proven |
| Page alignment correct (4096) | ✅ Proven |
| Header byte-match against known-good CHN image | ✅ Proven |
| Kernel, ramdisk, DTB are real extracted components | ✅ Real files from TWRP build |
| ARM64 Codex binaries run on Android emulator | ✅ Proven (adb push + execute) |
| **This image actually boots on an S7 Edge** | ❌ **NOT PROVEN — cannot be emulated** |
| **Odin flash to SM-G935T (T-Mobile)** | ❌ **FAILED — RQT_CLOSE after NAND write (2026-03-24)** |

The Samsung S7 Edge has a Qualcomm-specific bootloader, Samsung partition
layout, and hardware-specific DTB. No emulator exists that can tell you
"this TWRP image will boot on this phone." The format is proven correct.
Whether it boots is a physical test.

---

## Prerequisites

- Phone is in **Download Mode** (cyan screen, "Downloading... Do not turn off target!!")
  - To enter: power off, then hold **Vol Down + Home + Power**
- Odin is open and **sees the phone** (Log says `Added!!`, first box is light blue)
- USB cable is plugged in and working
- You have stock Samsung firmware somewhere in case you need to recover

---

## Pre-Flight Checks

### ☐ 1. Odin sees the phone

In the Odin **Log** pane (bottom), look for:
```
<ID:0/003> Added!!
```
One of the top boxes should be **light blue/cyan**.

If Odin doesn't see it: try a different USB cable or USB 2.0 port.
USB 3.0 ports are unreliable with Odin.

### ☐ 2. Verify the image file

Open PowerShell:
```powershell
(Get-Item "D:\Projects\NewRepository\phone\flash\recovery.img.tar").Length
```
**Expected: 25090048**

If the number is different, **STOP**.

### ☐ 3. Check Odin settings

Click the **Options** tab in Odin:

| Setting | Value | Why |
|---------|-------|-----|
| Auto Reboot | ☑ Checked | Reboots phone after flash |
| **Re-Partition** | **☐ UNCHECKED** | **Checking this wipes the partition table. Do not.** |
| F. Reset Time | Don't care | |

---

## The Flash (3 clicks)

### ☐ 4. Load the image into AP

Click the **AP** button in Odin.

Navigate to:
```
D:\Projects\NewRepository\phone\flash\recovery.img.tar
```

Select it. The Log should confirm the filename.

**⚠️ ONLY AP. Do not put anything in BL, CP, or CSC.**

- **BL** = bootloader (wrong slot → bad day)
- **CP** = modem/radio (wrong slot → no cell service)
- **CSC** = carrier config (wrong slot → factory reset)
- **AP** = Android partition = where recovery lives ✅

### ☐ 5. Click Start

Watch the Log. You should see:
```
<ID:0/003> SetupConnection...
<ID:0/003> Initialzation..
<ID:0/003> Get PIT for mapping...
<ID:0/003> recovery.img
<ID:0/003> Complete (Write) operation.
<ID:0/003> RQT_CLOSE !!
```

The top box turns **GREEN** with **PASS!**

**Expected time: 5–15 seconds.** Recovery images are small.

If the box turns **RED** with **FAIL!** — the phone is fine, it's still
in Download Mode. Read the error, do not panic.

### ☐ 6. Boot into TWRP

After PASS, the phone reboots automatically.

**Immediately** hold: **Volume Up + Home + Power** (all three)

Hold until you see the Samsung logo, then release.

This boots into **recovery mode** instead of normal Android.

---

## What Success Looks Like

TWRP loads: blue/grey touchscreen UI, "Team Win Recovery Project."

It will ask about keeping the system partition read-only.
Tap **Keep Read Only** (safe default).

From TWRP you can:
- **Backup** → back up the stock ROM (do this first!)
- **Install** → flash custom ROMs
- **ADB Sideload** → push files from PC

---

## What Failure Looks Like (and How to Recover)

| Symptom | Cause | Fix |
|---------|-------|-----|
| Odin says PASS, phone boots to normal Android | You pressed Vol Down instead of Vol Up | Power off. Hold **Vol Down + Home + Power** → Download Mode. This time remember: **Vol UP + Home + Power** after flash |
| Odin says PASS, phone bootloops (Samsung logo → restart → repeat) | Recovery image doesn't work on this device | Hold **Vol Down + Home + Power** for 10 sec → Download Mode. Flash stock recovery via Odin (same AP slot, stock `recovery.img.tar`) |
| Odin says FAIL | Wrong slot, bad USB, or corrupt file | Phone is still in Download Mode. Read the error. Retry |
| Phone is dead / black screen | Battery or USB glitch | Hold **Vol Down + Home + Power** for 15 seconds. Phone vibrates → Download Mode. You're back to start |
| Phone catches fire | You're on your own | Call 911, not an AI |

### The nuclear option

If everything goes sideways:
1. Enter Download Mode (**Vol Down + Home + Power**, hold 10+ sec)
2. Open Odin
3. Load your **full stock Samsung firmware** into AP (and BL/CP/CSC if it's a multi-file firmware)
4. Flash
5. Phone is back to factory state

Download Mode is **hardware-level**. A bad recovery image cannot break it.
The only way to truly brick this phone is to corrupt the bootloader,
which requires checking Re-Partition in Odin or flashing garbage to BL.
Don't do that.

---

## Key Combos Reference

| Combo | What it does |
|-------|-------------|
| **Vol Down + Home + Power** (hold 10 sec) | Download Mode (Odin) |
| **Vol Up + Home + Power** (hold until logo) | Recovery Mode (TWRP) |
| **Vol Down + Power** (hold 10 sec) | Force restart |

---

*Written for one specific human who has bricked a keyboard before.*
*The phone will probably be fine. Probably.*

---

## Flash Attempt Log (2026-03-24)

**Result: FAILED** — multiple approaches tried, none successful yet.
Phone boots to Android fine. No damage done.

### What Was Tried

| # | Change | Result |
|---|--------|--------|
| 1 | `recovery-fixed.img.tar` (wrong internal filename) | FAIL — Odin doesn't recognize partition name `recovery-fixed` |
| 2 | Rebuilt as `recovery.img.tar` (correct internal name) | FAIL — `RQT_CLOSE` after NAND write |
| 3 | USB port change (white → black USB 2.0 on rear panel) | FAIL — same `RQT_CLOSE` |
| 4 | Rebuilt with blank board name (was `SRPPA14B001RU` from CHN) | FAIL — same `RQT_CLOSE` |
| 5 | Full reboot cycle (Android → clean shutdown → Download Mode) | FAIL — same `RQT_CLOSE` |
| 6 | Heimdall via WSL2 (installed usbipd, attached USB to WSL) | FAIL — Heimdall 2.0.2 too old, `Failed to send handshake` |
| 7 | Official TWRP 3.7.0 for hero2qltechn (stale USB state) | FAIL — `SetupConnection` failure |
| 8 | Multiple retries after reboot cycles | FAIL — `SetupConnection` keeps failing |
| 9 | **PC reboot + official TWRP 3.7.0** (clean USB, fresh Odin) | FAIL — `RQT_CLOSE` after NAND write (clean connection, same result) |

### Key Findings

- **Heimdall 2.0.2** cannot handshake with the S7 Edge — protocol too new for the tool.
  Installed `usbipd-win` to pass USB from Windows to WSL2. Device detected by `lsusb`
  but Heimdall can't initialize the Samsung download protocol.
- **TWRP build from source** is blocked — the TWRP minimal manifest repo only has
  branches `twrp-11`, `twrp-12.1`, `twrp-14`, `twrp-14.1`. The device tree
  (`jcadduono/android_device_samsung_hero2qlte`) is on `android-6.0`. Massive
  version mismatch makes building from source a porting project, not a script run.
- **Official TWRP builds exist** for `hero2qltechn` (China variant) but NOT for
  `hero2qlte` (T-Mobile). Downloaded `twrp-3.7.0_9-0-hero2qltechn.img.tar`
  (26.7 MB, built by Jenkins) — available at `phone/flash/twrp-official-hero2qltechn.img.tar`.
- **USB driver state degrades** after repeated failed flashes. Windows accumulates
  ghost Samsung USB device entries across multiple COM ports. Multiple stale
  `PID_685D` (download mode) and `PID_6860` (normal mode) entries in Device Manager.
  **PC reboot recommended** to clear USB driver cache before next attempt.
- **Phone is fine** — boots to Android normally after all failed flash attempts.

### Files in phone/flash/

| File | Description |
|------|-------------|
| `recovery.img.tar` | Hand-packed image (blank board name). Failed `RQT_CLOSE`. |
| `twrp-official-hero2qltechn.img.tar` | Official TWRP 3.7.0 from twrp.me. **Also fails `RQT_CLOSE`.** |
| `dtb.img`, `Image.gz`, `initrd.img` | Source components of hand-packed image. |

### Diagnosis — Bootloader Signature Verification

Attempt 9 is conclusive. An official TWRP image built by TWRP's own Jenkins CI
also fails at `RQT_CLOSE`. This eliminates our hand-packed image as the cause.

**The T-Mobile SM-G935T bootloader is enforcing signature verification on the
recovery partition.** The `OEM Unlock` toggle in Developer Options was enabled,
but on Samsung devices that only *permits* unlocking — it doesn't *perform* it.
The bootloader must be explicitly unlocked before it will accept unsigned images.

On Samsung Galaxy S7 Edge (T-Mobile), unlocking requires one of:
- **`fastboot oem unlock`** — if the phone supports fastboot mode
- **Stock firmware with unlocked bootloader flag** — some T-Mobile firmware
  versions ship with bootloader restrictions even after OEM Unlock toggle
- **Combination firmware** — Samsung engineering firmware that disables
  signature verification for development purposes

### Next Steps

1. **Check if the bootloader is actually unlocked.** In Download Mode, the
   screen should say `OEM LOCK: OFF` (or `CUSTOM: YES`). If it says
   `OEM LOCK: ON`, the bootloader was never unlocked despite the toggle.
2. **If locked**: Research SM-G935T bootloader unlock procedure — may require
   `fastboot oem unlock`, a specific stock firmware version, or combination firmware.
3. **If unlocked**: The `RQT_CLOSE` failure has another cause. Try Odin 3.14.4
   (newer version) or a different Odin fork (e.g., Odin4 or Society Odin).
4. **Alternative path**: Skip TWRP entirely. Use `adb` from Android to push
   Codex ARM64 binaries to `/data/local/tmp/` and run them under Android.
   This achieves Phase 1 (Codex running on the phone) without flashing anything.

The phone is fine — still boots into Android and Download Mode normally.
