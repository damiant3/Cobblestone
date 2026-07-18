# Emulator Testing Plan — Codex on Phone

**Date**: 2026-03-23
**Branch**: `windows/codex-phone`
**Goal**: Validate everything in emulation before touching the real S7 Edge

---

## Two Separate Problems

### Problem 1: ARM64 Codex Binaries
Can we compile `.codex` → ARM64 ELF and run it on Android?

**Test path** (no risk to phone):
1. Build ARM64 ELF via `codex build hello.codex --target arm64`
2. Run in WSL via `qemu-aarch64` (already installed at `/usr/bin/qemu-aarch64`)
3. Boot x86_64 Android emulator, `adb push` an x86_64 binary and run it
4. Once ARM64 binary works under QEMU, push it to the phone via `adb`

**Status**: ARM64 backend complete, QEMU verification pending.

### Problem 2: TWRP Recovery Image (Samsung DTB Bug)
Can we pack a correct Samsung boot image with the DTB section?

**The bug**: `abootimg --create` does NOT write Samsung's `dt_size` header field.
Our images have `dt_size=0` vs the original's `dt_size=6414336`. Without the DTB
the kernel can't find hardware → phone won't boot → **soft brick** (recoverable
via Odin, but still bad).

**Test path** (no risk to phone):
1. Write a Python script (`phone/pack-samsung-bootimg.py`) that creates a proper
   Samsung boot image with header, kernel, ramdisk, DTB, and SEANDROIDENFORCE
2. Validate the output with `phone/inspect-bootimg.py` — `dt_size` must be non-zero
3. Byte-compare header fields against the known-good CHN image
4. **Only after all validations pass** → flash to phone

---

## Available Tools

| Tool | Location | Purpose |
|------|----------|---------|
| Android emulator | `C:\Program Files (x86)\Android\android-sdk\emulator\` | x86_64 AVD (pixel_7_-_api_35) |
| adb | `C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe` | Push/execute on emulator or phone |
| qemu-aarch64 | WSL `/usr/bin/qemu-aarch64` | Run ARM64 ELFs on x86_64 |
| inspect-bootimg.py | `phone/inspect-bootimg.py` | Validate boot image header fields |
| Codex ARM64 backend | `src/Codex.Emit.Arm64/` | Compile .codex → ARM64 ELF |

## System Images Installed
- `android-35/google_apis_playstore/x86_64` (Pixel 7 AVD)
- Platforms: android-34, android-35

---

## Step-by-Step Plan

### Phase A: Validate ARM64 binaries (WSL + QEMU)

```bash
# In WSL:
cd /mnt/d/Projects/NewRepository
# Build hello.codex → ARM64 ELF (using dotnet CLI)
dotnet run --project tools/Codex.Cli -- build samples/hello.codex --target arm64 -o /tmp/hello-arm64
# Run under QEMU
qemu-aarch64 /tmp/hello-arm64
```

If this works, ARM64 codegen is verified. Skip to Phase C.

### Phase B: Android Emulator — adb workflow test

```powershell
# Start emulator (headless for speed)
& "C:\Program Files (x86)\Android\android-sdk\emulator\emulator.exe" -avd pixel_7_-_api_35 -no-window -no-audio -no-snapshot &

# Wait for boot
adb wait-for-device
adb shell getprop sys.boot_completed  # wait for "1"

# Push and run (x86_64 binary for now)
adb push generated-output/hello-x86_64 /data/local/tmp/
adb shell chmod +x /data/local/tmp/hello-x86_64
adb shell /data/local/tmp/hello-x86_64

# Kill emulator
adb emu kill
```

This validates the adb push + execute workflow. Same steps will work on the real phone.

### Phase C: Fix Samsung boot image packer

Write `phone/pack-samsung-bootimg.py`:
- Reads: kernel (Image.gz), ramdisk (initrd.img), DTB (dtb.img)
- Writes: Samsung-format boot image with correct header
- Appends SEANDROIDENFORCE magic

Validation:
```bash
python3 phone/inspect-bootimg.py known-good-chn.img our-new-image.img
# dt_size must match, trailing_bytes must be 16 (SEANDROIDENFORCE)
```

### Phase D: Flash to phone (ONLY after C passes)

```
1. Boot phone into Download Mode (Vol Down + Home + Power)
2. Connect USB
3. Flash via Odin: recovery.img.tar → AP slot
4. Reboot into recovery (Vol Up + Home + Power)
5. If TWRP loads → success
6. If bootloop → Odin flash stock recovery (already have stock firmware)
```

---

## Risk Assessment

| Action | Risk | Mitigation |
|--------|------|------------|
| QEMU ARM64 test | None | Software only |
| Android emulator test | None | Virtual device |
| adb push to emulator | None | Virtual device |
| Build boot image | None | Just creates a file |
| inspect-bootimg.py | None | Read-only |
| Flash to phone | **Medium** | Only after all validations. Stock firmware available for recovery. |

---

## Next Steps (This Session)

1. ✅ Feature branch created: `windows/codex-phone`
2. ✅ ARM64 binary under QEMU in WSL — hello=25, factorial=3628800, greeting=Hello World
3. ✅ Fixed ARM64 str_concat bug (byte copy + alloc sizing)
4. ✅ Fixed ARM64 ELF section headers for Android (bionic linker requires .shstrtab)
5. ✅ All 5 samples running on Android 15 emulator via adb push
6. ✅ `pack-samsung-bootimg.py` with proper DTB support — self-test passes
7. ✅ Packed real recovery image — dt_size=6414336, matches CHN reference exactly
8. ✅ compare-headers.py confirms structural match to known-good image
9. [ ] **Phase D: Flash to phone** — recovery-fixed.img.tar ready for Odin

## Bugs Found and Fixed Along the Way

| Bug | Where | Impact |
|-----|-------|--------|
| str_concat second loop: 8-byte Ldr/Str + stride 8 | Arm64CodeGen.cs | Segfault on string concat |
| str_concat allocation: (total+1)*8 | Arm64CodeGen.cs | Massive over-allocation |
| ELF missing section headers | ElfWriterArm64.cs | Android refuses to execute |
| Samsung DTB not in header | abootimg (upstream tool) | Phone won't boot |
