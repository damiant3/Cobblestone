#!/usr/bin/env python3
"""
pack-samsung-bootimg.py — Samsung-aware Android boot image packer.

Samsung Galaxy S7 (hero2qlte) uses the old Android boot image format
with a device-tree (DT) section that AOSP's abootimg does NOT support.

Samsung boot image layout (all sections page-aligned):
  [header 1 page][kernel pages][ramdisk pages][second pages][dt pages][SEANDROIDENFORCE]

Header format (at offset 0, 1 page = 4096 bytes typically):
  0x00: ANDROID! magic (8 bytes)
  0x08: kernel_size      (4 bytes, LE)
  0x0C: kernel_addr      (4 bytes, LE)
  0x10: ramdisk_size     (4 bytes, LE)
  0x14: ramdisk_addr     (4 bytes, LE)
  0x18: second_size      (4 bytes, LE)
  0x1C: second_addr      (4 bytes, LE)
  0x20: tags_addr        (4 bytes, LE)
  0x24: page_size        (4 bytes, LE)
  0x28: dt_size          (4 bytes, LE)  ← THIS IS WHAT abootimg MISSES
  0x2C: os_version       (4 bytes, LE)
  0x30: name             (16 bytes, null-padded)
  0x40: cmdline          (512 bytes, null-padded)
  0x240: id/sha          (32 bytes)
  0x260: extra_cmdline    (1024 bytes, null-padded)

Usage:
  python3 pack-samsung-bootimg.py \\
      --kernel Image.gz \\
      --ramdisk initrd.img \\
      --dt dtb.img \\
      --output recovery.img \\
      [--cmdline "..."] \\
      [--board ""] \\
      [--base 0x80000000] \\
      [--kernel-offset 0x00008000] \\
      [--ramdisk-offset 0x02000000] \\
      [--tags-offset 0x01e00000] \\
      [--page-size 4096] \\
      [--seandroid]
"""
import argparse
import hashlib
import os
import struct
import sys


BOOT_MAGIC = b'ANDROID!'
BOOT_MAGIC_SIZE = 8
BOOT_NAME_SIZE = 16
BOOT_ARGS_SIZE = 512
BOOT_EXTRA_ARGS_SIZE = 1024
BOOT_ID_SIZE = 32  # SHA-1 digest (20) + padding (12)


def align(size, page_size):
    """Round up to next page boundary."""
    return ((size + page_size - 1) // page_size) * page_size


def read_file(path):
    """Read a binary file, return bytes."""
    with open(path, 'rb') as f:
        return f.read()


def compute_id(kernel, ramdisk, second, dt):
    """Compute the 20-byte SHA-1 id field (matches mkbootimg behavior)."""
    sha = hashlib.sha1()
    sha.update(kernel)
    sha.update(struct.pack('<I', len(kernel)))
    sha.update(ramdisk)
    sha.update(struct.pack('<I', len(ramdisk)))
    sha.update(second)
    sha.update(struct.pack('<I', len(second)))
    sha.update(dt)
    sha.update(struct.pack('<I', len(dt)))
    return sha.digest()


def pack_boot_image(args):
    kernel = read_file(args.kernel)
    ramdisk = read_file(args.ramdisk)
    dt = read_file(args.dt) if args.dt else b''
    second = b''  # We don't use a second-stage bootloader

    page_size = args.page_size
    base = args.base

    kernel_addr = base + args.kernel_offset
    ramdisk_addr = base + args.ramdisk_offset
    second_addr = base + args.second_offset
    tags_addr = base + args.tags_offset

    # Encode board name (max 16 bytes, null-padded)
    board = args.board.encode('ascii')[:BOOT_NAME_SIZE - 1]
    board = board + b'\x00' * (BOOT_NAME_SIZE - len(board))

    # Encode cmdline (max 512 bytes, null-padded)
    cmdline = args.cmdline.encode('ascii')[:BOOT_ARGS_SIZE - 1]
    cmdline = cmdline + b'\x00' * (BOOT_ARGS_SIZE - len(cmdline))

    # Extra cmdline (max 1024 bytes, null-padded)
    extra_cmdline = b'\x00' * BOOT_EXTRA_ARGS_SIZE

    # Compute SHA-1 id
    sha_id = compute_id(kernel, ramdisk, second, dt)
    boot_id = sha_id + b'\x00' * (BOOT_ID_SIZE - len(sha_id))

    # os_version: 0 (not used for recovery)
    os_version = 0

    # ── Build header ──
    header = bytearray()
    header += BOOT_MAGIC                                          # 0x00: magic
    header += struct.pack('<I', len(kernel))                      # 0x08: kernel_size
    header += struct.pack('<I', kernel_addr)                      # 0x0C: kernel_addr
    header += struct.pack('<I', len(ramdisk))                     # 0x10: ramdisk_size
    header += struct.pack('<I', ramdisk_addr)                     # 0x14: ramdisk_addr
    header += struct.pack('<I', len(second))                      # 0x18: second_size
    header += struct.pack('<I', second_addr)                      # 0x1C: second_addr
    header += struct.pack('<I', tags_addr)                        # 0x20: tags_addr
    header += struct.pack('<I', page_size)                        # 0x24: page_size
    header += struct.pack('<I', len(dt))                          # 0x28: dt_size ← THE KEY FIELD
    header += struct.pack('<I', os_version)                       # 0x2C: os_version
    header += board                                               # 0x30: name (16 bytes)
    header += cmdline                                             # 0x40: cmdline (512 bytes)
    header += boot_id                                             # 0x240: id (32 bytes)
    header += extra_cmdline                                       # 0x260: extra_cmdline (1024 bytes)

    # Pad header to page_size
    header_padded = bytes(header) + b'\x00' * (page_size - len(header))
    assert len(header_padded) == page_size, f"Header is {len(header_padded)} bytes, expected {page_size}"

    # ── Build image ──
    image = bytearray()
    image += header_padded

    # Kernel (page-aligned)
    image += kernel
    image += b'\x00' * (align(len(kernel), page_size) - len(kernel))

    # Ramdisk (page-aligned)
    image += ramdisk
    image += b'\x00' * (align(len(ramdisk), page_size) - len(ramdisk))

    # Second bootloader (page-aligned, usually empty)
    if len(second) > 0:
        image += second
        image += b'\x00' * (align(len(second), page_size) - len(second))

    # Device tree (page-aligned) — THIS IS WHAT WAS MISSING
    if len(dt) > 0:
        image += dt
        image += b'\x00' * (align(len(dt), page_size) - len(dt))

    # SEANDROIDENFORCE trailer (Samsung-specific, not page-aligned)
    if args.seandroid:
        image += b'SEANDROIDENFORCE'

    # ── Write output ──
    with open(args.output, 'wb') as f:
        f.write(image)

    # ── Report ──
    print(f"Samsung boot image packed: {args.output}")
    print(f"  kernel_size:  {len(kernel):>12}")
    print(f"  ramdisk_size: {len(ramdisk):>12}")
    print(f"  second_size:  {len(second):>12}")
    print(f"  dt_size:      {len(dt):>12}")
    print(f"  page_size:    {page_size:>12}")
    print(f"  seandroid:    {'yes' if args.seandroid else 'no':>12}")
    print(f"  total_size:   {len(image):>12}")

    # Self-validate: re-read and check dt_size
    with open(args.output, 'rb') as f:
        hdr = f.read(page_size)
    check_dt = struct.unpack_from('<I', hdr, 0x28)[0]
    if args.dt and check_dt == 0:
        print("ERROR: dt_size is 0 in output — packing bug!", file=sys.stderr)
        return 1
    if args.dt and check_dt != len(dt):
        print(f"ERROR: dt_size mismatch: header={check_dt}, actual={len(dt)}", file=sys.stderr)
        return 1
    print(f"  VALIDATED: dt_size={check_dt} OK")
    return 0


def main():
    parser = argparse.ArgumentParser(description='Pack Samsung Android boot image with DTB support')
    parser.add_argument('--kernel', required=True, help='Path to kernel (Image.gz)')
    parser.add_argument('--ramdisk', required=True, help='Path to ramdisk (initrd.img)')
    parser.add_argument('--dt', default=None, help='Path to device tree blob (dtb.img)')
    parser.add_argument('--output', '-o', required=True, help='Output boot image path')
    parser.add_argument('--cmdline', default='', help='Kernel command line')
    parser.add_argument('--board', default='', help='Board name (max 16 chars)')
    parser.add_argument('--base', type=lambda x: int(x, 0), default=0x80000000, help='Base address')
    parser.add_argument('--kernel-offset', type=lambda x: int(x, 0), default=0x00008000)
    parser.add_argument('--ramdisk-offset', type=lambda x: int(x, 0), default=0x02000000)
    parser.add_argument('--second-offset', type=lambda x: int(x, 0), default=0x00f00000)
    parser.add_argument('--tags-offset', type=lambda x: int(x, 0), default=0x01e00000)
    parser.add_argument('--page-size', type=int, default=4096, help='Page size (default 4096)')
    parser.add_argument('--seandroid', action='store_true', default=True,
                        help='Append SEANDROIDENFORCE trailer (default: yes)')
    parser.add_argument('--no-seandroid', action='store_false', dest='seandroid')
    args = parser.parse_args()
    sys.exit(pack_boot_image(args))


if __name__ == '__main__':
    main()
