#!/usr/bin/env python3
"""Compare boot image headers side-by-side."""
import struct, sys, os

def parse(path):
    with open(path, 'rb') as f:
        hdr = f.read(4096)
    d = {}
    d['kernel_size']  = struct.unpack_from('<I', hdr, 0x08)[0]
    d['kernel_addr']  = struct.unpack_from('<I', hdr, 0x0C)[0]
    d['ramdisk_size'] = struct.unpack_from('<I', hdr, 0x10)[0]
    d['ramdisk_addr'] = struct.unpack_from('<I', hdr, 0x14)[0]
    d['second_size']  = struct.unpack_from('<I', hdr, 0x18)[0]
    d['tags_addr']    = struct.unpack_from('<I', hdr, 0x20)[0]
    d['page_size']    = struct.unpack_from('<I', hdr, 0x24)[0]
    d['dt_size']      = struct.unpack_from('<I', hdr, 0x28)[0]
    d['board']        = hdr[0x30:0x40].split(b'\x00')[0].decode('ascii', errors='replace')

    ps = d['page_size']
    def align(v): return ((v + ps - 1) // ps) * ps
    computed = ps + align(d['kernel_size']) + align(d['ramdisk_size']) + align(d['second_size']) + align(d['dt_size'])
    d['computed_size'] = computed
    d['file_size'] = os.path.getsize(path)
    d['trailing'] = d['file_size'] - computed

    # Check SEANDROIDENFORCE
    with open(path, 'rb') as f:
        f.seek(-16, 2)
        d['trailer'] = f.read(16)
    return d

imgs = sys.argv[1:]
if len(imgs) < 2:
    print("Usage: compare-headers.py <img1> <img2> [img3]")
    sys.exit(1)

names = [os.path.basename(p) for p in imgs]
parsed = [parse(p) for p in imgs]

fields = ['kernel_size', 'kernel_addr', 'ramdisk_size', 'ramdisk_addr',
          'second_size', 'tags_addr', 'page_size', 'dt_size', 'board',
          'computed_size', 'file_size', 'trailing']

# Header
print(f"{'Field':<16}", end='')
for n in names:
    print(f"  {n:>20}", end='')
print("  Match?")
print("-" * (16 + 22 * len(names) + 8))

for field in fields:
    vals = [d[field] for d in parsed]
    print(f"{field:<16}", end='')
    for v in vals:
        if isinstance(v, int):
            print(f"  {v:>20,}", end='')
        else:
            print(f"  {str(v):>20}", end='')
    match = all(v == vals[0] for v in vals[1:])
    print(f"  {'YES' if match else '** NO **'}")

# Trailer check
print()
for i, d in enumerate(parsed):
    t = d['trailer']
    label = "SEANDROIDENFORCE" if t == b'SEANDROIDENFORCE' else repr(t)
    print(f"{names[i]}: trailer = {label}")
