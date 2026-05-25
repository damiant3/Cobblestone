# TWRP Build Handoff — Agent Linux

**Date**: 2026-03-23 (updated 2026-03-24)
**From**: Agent Windows
**To**: Agent Linux, Cam, or **Nut** (garage box has WSL + Ubuntu ready)
**Priority**: Blocking — hand-packed image failed Odin flash (RQT_CLOSE). Need a real build.

**2026-03-24 UPDATE**: The hand-packed recovery image (built with `pack-samsung-bootimg.py`
from CHN variant components) was rejected by the T-Mobile bootloader at the `RQT_CLOSE`
step. Eight attempts with different configurations all failed. The official TWRP for
`hero2qltechn` was downloaded but not yet tested with clean USB state. The TWRP manifest
repo only has branches `twrp-11` and up — the `twrp-6.0` branch referenced below no
longer exists. Building from source requires porting the `android-6.0` device tree to
a newer TWRP base. See `docs/Active/Projects/PHONE-WIPE.md` flash attempt log for full details.

---

## What We Need

A TWRP `recovery.img` for **Samsung Galaxy S7 Edge (T-Mobile)** — codename **hero2qlte**, model **SM-G935T**.

There are **no pre-built TWRP images** for this device. TWRP's official site never published builds for the Qualcomm S7 Edge. LineageOS also doesn't support it. The device tree source exists and is maintained.

## Source Repositories

- **Device tree**: https://github.com/jcadduono/android_device_samsung_hero2qlte (branch: `android-6.0`)
- **Kernel**: https://github.com/jcadduono/android_kernel_samsung_msm8996 (branch: `twrp-6.0`)
- **TWRP minimal manifest**: https://github.com/minimal-manifest-twrp/platform_manifest_twrp_aosp (use appropriate branch)

## Build Steps

```bash
# 1. Install repo tool if not present
mkdir -p ~/bin
curl https://storage.googleapis.com/git-repo-downloads/repo > ~/bin/repo
chmod a+x ~/bin/repo
export PATH=~/bin:$PATH

# 2. Create build directory
mkdir -p ~/twrp-hero2qlte && cd ~/twrp-hero2qlte

# 3. Init with TWRP minimal manifest (android-6.0 to match device tree)
repo init -u https://github.com/minimal-manifest-twrp/platform_manifest_twrp_aosp.git -b twrp-6.0
# If twrp-6.0 branch doesn't exist, try twrp-7.1 or twrp-9.0 and adjust device tree branch

# 4. Add device tree to local manifest
mkdir -p .repo/local_manifests
cat > .repo/local_manifests/hero2qlte.xml << 'EOF'
<?xml version="1.0" encoding="UTF-8"?>
<manifest>
  <project path="device/samsung/hero2qlte"
           name="jcadduono/android_device_samsung_hero2qlte"
           remote="github"
           revision="android-6.0" />
  <project path="kernel/samsung/msm8996"
           name="jcadduono/android_kernel_samsung_msm8996"
           remote="github"
           revision="twrp-6.0" />
</manifest>
EOF

# 5. Sync (minimal manifest is ~10-15GB, much less than full AOSP)
repo sync -j8

# 6. Build
. build/envsetup.sh
lunch omni_hero2qlte-eng
make -j$(nproc) recoveryimage
```

## Output

The build produces: `out/target/product/hero2qlte/recovery.img`

This file needs to get to the Windows machine. Options:
- Push to a GitHub release on our repo
- `scp` / transfer via shared filesystem
- Base64 encode and paste if it's small enough (~40MB typical)

## What Happens Next

1. Agent Windows takes `recovery.img`, wraps in `.tar` if needed: `tar -cf recovery.img.tar recovery.img`
2. Flash via Odin: AP slot → `recovery.img.tar` → Start
3. Boot into TWRP (Vol Up + Home + Power)
4. Wipe Android
5. Flash postmarketOS or minimal Linux
6. `adb push` Codex ARM64 binary
7. Run it

## Phone Status

- Phone: Samsung SM-G935T, serial RF8H30790RF
- Current state: In Download Mode, Odin connected on COM7
- OEM Unlock: Enabled
- SIM card: Removed
- SD card: None
- Backup: Complete (on PC disk)
- Speaker: Dead (minor casualty during SIM removal)

## Notes

- Do NOT use the hero2lte (Exynos) or hero2qltechn (China Qualcomm) builds — wrong device
- The `omni.dependencies` file in the device tree may reference additional repos — check and add to local manifest if needed
- jcadduono's tree is based on android-6.0; newer TWRP manifest branches may need adjustments
- If build issues arise, jcadduono also maintains the China variant (hero2qltechn) which is more recently updated — compare for fixes
````````

This is the code block that represents the suggested code change:

````````markdown
# TWRP Build Handoff — Agent Linux

**Date**: 2026-03-23 (updated 2026-03-24)
**From**: Agent Windows
**To**: Agent Linux, Cam, or **Nut** (garage box has WSL + Ubuntu ready)
**Priority**: Blocking — hand-packed image failed Odin flash (RQT_CLOSE). Need a real build.

**2026-03-24 UPDATE**: The hand-packed recovery image (built with `pack-samsung-bootimg.py`
from CHN variant components) was rejected by the T-Mobile bootloader at the `RQT_CLOSE`
step. Eight attempts with different configurations all failed. The official TWRP for
`hero2qltechn` was downloaded but not yet tested with clean USB state. The TWRP manifest
repo only has branches `twrp-11` and up — the `twrp-6.0` branch referenced below no
longer exists. Building from source requires porting the `android-6.0` device tree to
a newer TWRP base. See `docs/Active/Projects/PHONE-WIPE.md` flash attempt log for full details.

---

## What We Need

A TWRP `recovery.img` for **Samsung Galaxy S7 Edge (T-Mobile)** — codename **hero2qlte**, model **SM-G935T**.

There are **no pre-built TWRP images** for this device. TWRP's official site never published builds for the Qualcomm S7 Edge. LineageOS also doesn't support it. The device tree source exists and is maintained.

## Source Repositories

- **Device tree**: https://github.com/jcadduono/android_device_samsung_hero2qlte (branch: `android-6.0`)
- **Kernel**: https://github.com/jcadduono/android_kernel_samsung_msm8996 (branch: `twrp-6.0`)
- **TWRP minimal manifest**: https://github.com/minimal-manifest-twrp/platform_manifest_twrp_aosp (use appropriate branch)

## Build Steps

```bash
# 1. Install repo tool if not present
mkdir -p ~/bin
curl https://storage.googleapis.com/git-repo-downloads/repo > ~/bin/repo
chmod a+x ~/bin/repo
export PATH=~/bin:$PATH

# 2. Create build directory
mkdir -p ~/twrp-hero2qlte && cd ~/twrp-hero2qlte

# 3. Init with TWRP minimal manifest (android-6.0 to match device tree)
repo init -u https://github.com/minimal-manifest-twrp/platform_manifest_twrp_aosp.git -b twrp-6.0
# If twrp-6.0 branch doesn't exist, try twrp-7.1 or twrp-9.0 and adjust device tree branch

# 4. Add device tree to local manifest
mkdir -p .repo/local_manifests
cat > .repo/local_manifests/hero2qlte.xml << 'EOF'
<?xml version="1.0" encoding="UTF-8"?>
<manifest>
  <project path="device/samsung/hero2qlte"
           name="jcadduono/android_device_samsung_hero2qlte"
           remote="github"
           revision="android-6.0" />
  <project path="kernel/samsung/msm8996"
           name="jcadduono/android_kernel_samsung_msm8996"
           remote="github"
           revision="twrp-6.0" />
</manifest>
EOF

# 5. Sync (minimal manifest is ~10-15GB, much less than full AOSP)
repo sync -j8

# 6. Build
. build/envsetup.sh
lunch omni_hero2qlte-eng
make -j$(nproc) recoveryimage
```

## Output

The build produces: `out/target/product/hero2qlte/recovery.img`

This file needs to get to the Windows machine. Options:
- Push to a GitHub release on our repo
- `scp` / transfer via shared filesystem
- Base64 encode and paste if it's small enough (~40MB typical)

## What Happens Next

1. Agent Windows takes `recovery.img`, wraps in `.tar` if needed: `tar -cf recovery.img.tar recovery.img`
2. Flash via Odin: AP slot → `recovery.img.tar` → Start
3. Boot into TWRP (Vol Up + Home + Power)
4. Wipe Android
5. Flash postmarketOS or minimal Linux
6. `adb push` Codex ARM64 binary
7. Run it

## Phone Status

- Phone: Samsung SM-G935T, serial RF8H30790RF
- Current state: In Download Mode, Odin connected on COM7
- OEM Unlock: Enabled
- SIM card: Removed
- SD card: None
- Backup: Complete (on PC disk)
- Speaker: Dead (minor casualty during SIM removal)

## Notes

- Do NOT use the hero2lte (Exynos) or hero2qltechn (China Qualcomm) builds — wrong device
- The `omni.dependencies` file in the device tree may reference additional repos — check and add to local manifest if needed
- jcadduono's tree is based on android-6.0; newer TWRP manifest branches may need adjustments
- If build issues arise, jcadduono also maintains the China variant (hero2qltechn) which is more recently updated — compare for fixes
