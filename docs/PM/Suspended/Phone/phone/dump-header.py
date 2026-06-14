#!/usr/bin/env python3
import struct, sys

path = sys.argv[1] if len(sys.argv) > 1 else "/root/twrp-hero2qlte/download/twrp-chn.img"
with open(path, 'rb') as f:
    hdr = f.read(4096)

fields = [
    ('magic',        0x00, '8s'),
    ('kernel_size',  0x08, '<I'),
    ('kernel_addr',  0x0C, '<I'),
    ('ramdisk_size', 0x10, '<I'),
    ('ramdisk_addr', 0x14, '<I'),
    ('second_size',  0x18, '<I'),
    ('second_addr',  0x1C, '<I'),
    ('tags_addr',    0x20, '<I'),
    ('page_size',    0x24, '<I'),
    ('dt_size',      0x28, '<I'),
    ('os_version',   0x2C, '<I'),
]
for name, off, fmt in fields:
    val = struct.unpack_from(fmt, hdr, off)[0]
    if isinstance(val, bytes):
        print(f'{name}: {val}')
    else:
        print(f'{name}: {val} (0x{val:08X})')

board = hdr[0x30:0x40].split(b'\x00')[0]
print(f'board: {board}')
cmdline = hdr[0x40:0x240].split(b'\x00')[0]
print(f'cmdline: {cmdline[:120]}...')
