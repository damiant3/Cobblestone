#!/bin/bash
# Binary patch test: replace self-hosted 64-bit entry with reference 64-bit entry
# If this boots under KVM, the issue is in the 64-bit code, not the trampoline
python3 << 'PYEOF'
import struct

REF = '/mnt/d/Projects/NewRepository-cam/build-output/ref-small.elf'
BM = '/tmp/bm-small-v2.elf'

def parse_elf32(path):
    data = open(path, 'rb').read()
    e_phoff = struct.unpack_from('<I', data, 0x1c)[0]
    for j in range(struct.unpack_from('<H', data, 0x2c)[0]):
        ph_off = e_phoff + j * struct.unpack_from('<H', data, 0x2a)[0]
        if struct.unpack_from('<I', data, ph_off)[0] == 1:
            return (data,
                    struct.unpack_from('<I', data, ph_off+4)[0],  # p_offset
                    struct.unpack_from('<I', data, ph_off+8)[0],  # p_vaddr
                    struct.unpack_from('<I', data, ph_off+16)[0]) # p_filesz
    return data, 0, 0, 0

def find_far_jump(data):
    for i in range(0x90, 0x200):
        if data[i] == 0xEA and data[i+5] == 0x08 and data[i+6] == 0x00:
            return i, struct.unpack_from('<I', data, i+1)[0]
    return None, None

ref_data, ref_poff, ref_va, ref_fsz = parse_elf32(REF)
bm_data, bm_poff, bm_va, bm_fsz = parse_elf32(BM)

_, ref_entry_va = find_far_jump(ref_data)
_, bm_entry_va = find_far_jump(bm_data)

ref_entry_off = ref_poff + (ref_entry_va - ref_va)
bm_entry_off = bm_poff + (bm_entry_va - bm_va)

ref_64bit = ref_data[ref_entry_off:ref_poff + ref_fsz]
bm_64bit = bm_data[bm_entry_off:bm_poff + bm_fsz]

# Check: are the trampolines+helpers TRULY identical (byte-for-byte)?
ref_pre64 = ref_data[ref_poff:ref_entry_off]
bm_pre64 = bm_data[bm_poff:bm_entry_off]

print(f"Reference trampoline+helpers: {len(ref_pre64)} bytes")
print(f"Self-hosted trampoline+helpers: {len(bm_pre64)} bytes")

# Compare byte-by-byte
diffs = []
min_len = min(len(ref_pre64), len(bm_pre64))
for i in range(min_len):
    if ref_pre64[i] != bm_pre64[i]:
        diffs.append(i)

if len(diffs) == 0 and len(ref_pre64) == len(bm_pre64):
    print("  IDENTICAL (byte-for-byte)")
else:
    print(f"  {len(diffs)} byte differences in first {min_len} bytes")
    for d in diffs[:20]:
        print(f"    offset 0x{ref_poff+d:x}: ref=0x{ref_pre64[d]:02x} bm=0x{bm_pre64[d]:02x}")
    if len(ref_pre64) != len(bm_pre64):
        print(f"  Size differs: ref={len(ref_pre64)} bm={len(bm_pre64)}")
        # Show what's extra in the longer one
        if len(bm_pre64) > len(ref_pre64):
            extra = bm_pre64[len(ref_pre64):]
            print(f"  Self-hosted has {len(extra)} extra bytes in helpers")
        else:
            extra = ref_pre64[len(bm_pre64):]
            print(f"  Reference has {len(extra)} extra bytes in helpers")

PYEOF
