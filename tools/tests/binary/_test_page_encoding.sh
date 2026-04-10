#!/bin/bash
# Compare the actual page table encoding between reference and self-hosted
python3 << 'PYEOF'
import struct

REF = '/mnt/d/Projects/NewRepository-cam/build-output/ref-small.elf'
BM = '/tmp/bm-small-v2.elf'

def find_page_table_region(path, label):
    data = open(path, 'rb').read()
    # Find the page table entries (0x83, 0x200083, etc.)
    # The first entry has value 0x83 stored via mov-ri32 or movabs
    entries = []
    i = 0
    while i < len(data) - 10:
        # mov rax, imm32: 48 C7 C0 83 00 00 00 (for 0x83)
        if data[i] == 0x48 and data[i+1] == 0xC7 and data[i+2] == 0xC0:
            val = struct.unpack_from('<i', data, i+3)[0]
            if val >= 0 and val & 0xFF == 0x83:
                entries.append((i, val, 'ri32'))
            i += 7; continue
        # movabs rax, imm64: 48 B8 ...
        if data[i] == 0x48 and data[i+1] == 0xB8:
            val = struct.unpack_from('<Q', data, i+2)[0]
            if val & 0xFF == 0x83 and val < 0x40000000:
                entries.append((i, val, 'ri64'))
            i += 10; continue
        i += 1

    print(f"\n{label}: {len(entries)} page table entries")
    if len(entries) < 5:
        for off, val, enc in entries:
            print(f"  offset 0x{off:x}: 0x{val:x} ({enc})")
        return

    # Show first 5 and last 5
    for off, val, enc in entries[:5]:
        print(f"  offset 0x{off:x}: 0x{val:x} ({enc})")
    print(f"  ...")
    for off, val, enc in entries[-5:]:
        print(f"  offset 0x{off:x}: 0x{val:x} ({enc})")

    # Check: what follows each entry? Should be a mov-store
    print(f"\n  Encoding pattern for first 3 entries:")
    for off, val, enc in entries[:3]:
        # Show the bytes around each entry
        start = off
        end = min(off + 20, len(data))
        hexbytes = ' '.join(f'{b:02x}' for b in data[start:end])
        print(f"    @0x{off:x}: {hexbytes}")

find_page_table_region(REF, 'Reference')
find_page_table_region(BM, 'Self-hosted')

# Now compare the mov-store encoding after each page table li instruction
# Reference uses offset addressing: mov [rdi + i*8], rax
# Self-hosted should do the same after the fix
print("\n=== Comparing mov-store patterns around page table entries ===")
for label, path in [('Reference', REF), ('Self-hosted', BM)]:
    data = open(path, 'rb').read()
    # Find the first 0x83 entry
    for i in range(len(data) - 7):
        if data[i] == 0x48 and data[i+1] == 0xC7 and data[i+2] == 0xC0:
            val = struct.unpack_from('<i', data, i+3)[0]
            if val == 0x83:
                # Show 50 bytes from here
                region = data[i:i+60]
                hexbytes = ' '.join(f'{b:02x}' for b in region)
                print(f"\n{label} first page entry region (from 0x{i:x}):")
                print(f"  {hexbytes}")
                # Also show the pattern for entry[1] (0x200083)
                for j in range(i+7, i+50):
                    if data[j] == 0x48 and data[j+1] == 0xC7 and data[j+2] == 0xC0:
                        val2 = struct.unpack_from('<i', data, j+3)[0]
                        if val2 == 0x200083:
                            region2 = data[j:j+30]
                            hex2 = ' '.join(f'{b:02x}' for b in region2)
                            print(f"  entry[1] at 0x{j:x}: {hex2}")
                            break
                    if data[j] == 0x48 and data[j+1] == 0xB8:
                        val2 = struct.unpack_from('<Q', data, j+2)[0]
                        if val2 == 0x200083:
                            region2 = data[j:j+30]
                            hex2 = ' '.join(f'{b:02x}' for b in region2)
                            print(f"  entry[1] at 0x{j:x}: {hex2}")
                            break
                break

PYEOF
