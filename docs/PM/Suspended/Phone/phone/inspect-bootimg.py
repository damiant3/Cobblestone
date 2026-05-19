#!/usr/bin/env python3
import struct, sys

def inspect(path, label):
    with open(path, 'rb') as f:
        hdr = f.read(4096)
    magic = hdr[0:8]
    kernel_size = struct.unpack_from('<I', hdr, 0x08)[0]
    ramdisk_size = struct.unpack_from('<I', hdr, 0x10)[0]
    second_size = struct.unpack_from('<I', hdr, 0x18)[0]
    page_size = struct.unpack_from('<I', hdr, 0x24)[0]
    dt_size = struct.unpack_from('<I', hdr, 0x28)[0]
    name = hdr[0x30:0x40].split(b'\x00')[0].decode('ascii', errors='replace')
    print(f"=== {label} ===")
    print(f"  magic: {magic}")
    print(f"  kernel_size: {kernel_size}")
    print(f"  ramdisk_size: {ramdisk_size}")
    print(f"  second_size: {second_size}")
    print(f"  page_size: {page_size}")
    print(f"  dt_size: {dt_size}")
    print(f"  name: {name}")
    # compute expected total
    def align(v, a):
        return ((v + a - 1) // a) * a
    total = page_size  # header
    total += align(kernel_size, page_size)
    total += align(ramdisk_size, page_size)
    total += align(second_size, page_size)
    total += align(dt_size, page_size)
    print(f"  computed_image_size: {total}")
    import os
    actual = os.path.getsize(path)
    print(f"  actual_file_size: {actual}")
    print(f"  trailing_bytes: {actual - total}")
    print()

inspect(sys.argv[1], "ORIGINAL CHN")
inspect(sys.argv[2], "OURS TMO")
