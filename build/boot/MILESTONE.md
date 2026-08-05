# Milestone: first Codex image to boot on real UEFI hardware

**Date:** 2026-07-08

`optiona-milestone.img` is the exact, preserved image that booted the Codex
Option A GOP menu on real hardware for the first time. Keep it byte-for-byte --
it is the known-good reference for the entire UEFI boot path.

| | |
|---|---|
| File | `build/boot/optiona-milestone.img` |
| Size | 8388608 bytes (8 MB) |
| SHA-256 | `B501A316E5F8144AB75D06899376CD7C2C042473B31265A503A73AC466653A87` |
| Filesystem | GPT + FAT16 ESP (14158 clusters -- solidly FAT16), `EFI/BOOT/BOOTX64.EFI` |
| Payload | Option A stub + `apps/works/GopBoot.codex` (the "Choose Interface" menu) |

## Boots on

- **ASUS TUF (2015, AMI Aptio V)** -- after disabling CSM + Fast Boot (Secure Boot was already off).
- **Dell Inspiron 15 5000** -- boots as-is.
- **OVMF / edk2** in QEMU (`build/boot/test-ovmf.ps1`) -- boots as-is.

The GOP menu renders (title + four items + highlight bar). Arrow-key
navigation does **not** work yet on real hardware -- GopBoot reads keys from a
codex-vm-only memory cell (28680); real hardware needs PS/2 port reads
(`0x60/0x64`). That is the next change and will produce a *new* image; this one
is frozen as the boot-proof reference.

## The two bugs this milestone resolved

1. **FAT12/16 mislabel (code bug -- the real one).** `build/build-img.ps1`
   built an 8 MB FAT at sectors-per-cluster 4 = ~3560 clusters but labeled it
   "FAT16". UEFI firmware classifies FAT by **cluster count** (<4085 = FAT12),
   not the label, so it read our 16-bit FAT as 12-bit, misfollowed every
   chain, and reported "no boot device." This broke every machine and is the
   source of years of "same stick, boots sometimes" flakiness. Fixed in
   CL 7289 (auto-pick spc to land in the FAT16 range; now spc=1, 14158
   clusters).

2. **ASUS firmware config (not our code).** CSM (legacy emulation) made the
   board attempt a legacy/MBR boot of the USB; our image is pure GPT with a
   protective MBR (type `0xEE`, no legacy boot code), so legacy boot found
   nothing. Fast Boot skipped full USB enumeration. **Disable CSM (UEFI-only)
   + Fast Boot** → clean pure-UEFI boot. (Unsigned `BOOTX64.EFI` also requires
   Secure Boot off until we sign it.)

## How it was built

```
pwsh build/boot/build-option-a.ps1        # -> build/boot/optiona.img
# = compile apps/works/GopBoot.codex -> CDX
#   build/cdx-to-pe.ps1 emits the stub from PowerShell -> PE
#   build/build-img.ps1 wraps GPT/FAT16
```

Source revisions (as of this milestone; `option_a_stub.asm` was retired from the
default boot path at B5.4 step 4, its sequence now lives in `cdx-to-pe.ps1`, and
the file and its ml64 builder were deleted 2026-08-03. It is named below because
it is what this milestone was actually built with; read it out of the depot
history):
`option_a_stub.asm`,
`build-option-a.ps1`, `build-img.ps1`
(CL 7289), `GopBoot.codex` (CL 7286 state), and the codex-vm fidelity fixes
(CLs 7278/7280/7283/7286/7288).

## Flash it

```
build/flash-usb.ps1 -Image build/boot/optiona-milestone.img -DiskNumber <N>
```
(elevated; the flasher takes the disk offline, writes, `Flush($true)`, and
verifies the whole image). Boot target in **pure UEFI** (Secure Boot off,
Fast Boot off, CSM off/UEFI-only).

## Real-firmware test (no stick)

```
pwsh build/boot/test-ovmf.ps1 -Img build/boot/optiona-milestone.img -Out out.png
```
Boots the image under edk2/OVMF and screenshots it. This is the faithful boot
test -- use it, not codex-vm (whose firmware is a lenient fake), to validate
boot images before flashing.
