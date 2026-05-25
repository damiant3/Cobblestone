# TWRP Build Handoff — Session 2026-03-23

## Status: IMAGE NOT SAFE TO FLASH — DTB missing

## What We Have
- Official TWRP 3.3.1 for hero2qltechn (China S7 Edge) downloaded: `~/twrp-hero2qlte/download/twrp-chn.img`
- Ramdisk extracted: `~/twrp-hero2qlte/unpack/initrd.img` (8.78MB, good)
- TMO prebuilt kernel: `device/samsung/hero2qlte/Image.gz` (good)
- TMO prebuilt DTB: `device/samsung/hero2qlte/dtb.img` (good)
- Inspect script: `phone/inspect-bootimg.py`

## The Bug
`abootimg --create` does NOT write Samsung's appended DTB. Header field `dt_size = 0` in our image vs `6414336` in the original. Without DTB, kernel can't find hardware → boot fails.

## Fix Needed
Use Samsung-aware mkbootimg that supports `--dt` flag (NOT the AOSP v2 `--dtb`). Options:
1. Build the old Samsung mkbootimg from source (it's a single C file)
2. Use `split_bootimg.py` + manual binary concatenation with correct header
3. Write a Python script that packs the image correctly (header + kernel + ramdisk + dt + SEANDROIDENFORCE)

## Original vs Ours (from inspect-bootimg.py)
| Field | CHN (good) | TMO (broken) |
|-------|-----------|-------------|
| kernel_size | 9,455,372 | 9,456,342 |
| ramdisk_size | 9,208,464 | 9,208,464 |
| dt_size | **6,414,336** | **0** |
| trailing_bytes | 16 (SEANDROIDENFORCE) | 6,414,368 (junk) |

## Samsung Boot Image Format
```
[page-aligned header][kernel][ramdisk][second][dt][SEANDROIDENFORCE]
```
All sections page-aligned (4096). Header at offset 0x28 has dt_size.

## Files on Disk
- WSL: `~/twrp-hero2qlte/` (sync'd TWRP source, device trees, downloads, unpack dir)
- Windows: `phone/flash/` — **DO NOT FLASH** these, DTB is missing
- Windows: `phone/inspect-bootimg.py` — boot image inspector
- Windows: `phone/hero2qlte-manifest.xml` — repo local manifest

## AOSP Build System — Dead End
Tried twrp-11, twrp-12.1 manifests. Both have soong module version mismatches (GKI, rustlibs, license module type). Old twrp-6.0 branch deleted from GitHub. Building from source is not viable without a matching-era manifest.

## Emulation
Android SDK at `C:\Program Files (x86)\Android\android-sdk\`, emulator 35.2.10, AVD `pixel_7_-_api_35`. Could test boot image under emulation but need to fix DTB first.
