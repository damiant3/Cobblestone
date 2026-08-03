# QEMU IDE PIO: same-session write-then-read returns stale data -- 2026-05-02

**Reported:** 2026-05-02
**Reporter:** Cam (during DiskFacts V0 bring-up, CL 665)
**Bug class:** QEMU IDE controller emulation (PIO path)

## Trigger

ATA PIO write to sector N, then ATA PIO read of sector N in the same
QEMU boot. Config: QEMU 11.0, `-drive file=test.img,format=raw,if=ide,index=0`,
WHPX accel, bare-metal kernel.

## Symptom

The read returns all zeros (the sector's pre-write content) instead of
the just-written data. Reading a *different* sector after the write
works correctly. The disk image file on the host IS modified -- `xxd`
confirms the written bytes are at the correct offset. A fresh QEMU
boot reads the written data correctly.

## Reproduction

1. Create a zeroed 1 MB disk image.
2. Boot a bare-metal ELF that does:
   ```
   alloc-bytes 512
   poke-byte buf 0 42
   block-write-sector 1 buf
   block-read-sector 1
   peek-byte rbuf 0
   ```
3. Result: 0 (expected: 42).
4. Reboot QEMU with the same disk image, do only `block-read 1`.
5. Result: 42 (correct).

## What we tried

- ATA FLUSH CACHE (0xE7) after `outsw` + wait-ready: no effect.
- Soft reset (SRST via control register 0x3F6) before read: reads of
  *other* sectors work, but the just-written sector still returns
  stale.
- 400 ns delays (4× read of alternate status) between write and read.
- Explicit status register read after `outsw` to clear pending state.
- `CLD` before `rep insw`/`outsw` (direction flag).

## Theory

QEMU's IDE controller emulation has an internal read cache per sector
that is not invalidated when the same sector is written via PIO. The
write goes to the backing file (confirmed) and to the controller's
write path, but the read path serves from a stale cache. This may be
specific to WHPX acceleration or to the PIO (non-DMA) path.

## Workaround

After writing a sector, retain the buffer in memory and serve
subsequent reads from the in-memory copy. Do not re-read a
just-written sector from disk in the same boot. This is also faster.

## Related

See `qemu-ide-cross-session-reads-unreliable.md` for the adjacent
cross-boot variant of the IDE PIO bug.

## Upstream

Not yet filed. Need to test with TCG accel (`-accel tcg`) and with DMA
(`-drive if=virtio`) to narrow down whether it's WHPX-specific or a
general QEMU IDE PIO issue.
