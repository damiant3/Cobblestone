#!/bin/bash
# Compare ELF headers and try GDB debugging
REF=/mnt/d/Projects/NewRepository-cam/build-output/ref-small.elf
BM=/tmp/bm-small-v2.elf

echo "=== ELF Header comparison ==="
echo "-- Reference --"
readelf -h "$REF" 2>/dev/null
echo ""
echo "-- Self-hosted --"
readelf -h "$BM" 2>/dev/null

echo ""
echo "=== Program Header comparison ==="
echo "-- Reference --"
readelf -l "$REF" 2>/dev/null
echo ""
echo "-- Self-hosted --"
readelf -l "$BM" 2>/dev/null

echo ""
echo "=== Hex dump of first 128 bytes ==="
echo "-- Reference --"
xxd -l 128 "$REF"
echo ""
echo "-- Self-hosted --"
xxd -l 128 "$BM"

echo ""
echo "=== Multiboot header comparison ==="
# Multiboot header is near offset 0 of the LOAD segment
python3 -c "
import struct
for label, path in [('Reference', '$REF'), ('Self-hosted', '$BM')]:
    data = open(path, 'rb').read()
    # Multiboot header: magic (0x1BADB002) at LOAD start
    for i in range(min(0x200, len(data))):
        if i + 4 <= len(data):
            val = struct.unpack_from('<I', data, i)[0]
            if val == 0x1BADB002:
                magic = val
                flags = struct.unpack_from('<I', data, i+4)[0]
                checksum = struct.unpack_from('<I', data, i+8)[0]
                print(f'{label} Multiboot at offset 0x{i:x}: magic=0x{magic:x} flags=0x{flags:x} checksum=0x{checksum:x}')
                # Check if checksum is valid
                if (magic + flags + checksum) & 0xFFFFFFFF == 0:
                    print(f'  Checksum VALID')
                else:
                    print(f'  Checksum INVALID! Sum=0x{(magic+flags+checksum)&0xFFFFFFFF:x}')
                break
"

echo ""
echo "=== NOTE section comparison ==="
echo "-- Reference --"
readelf -n "$REF" 2>/dev/null
echo ""
echo "-- Self-hosted --"
readelf -n "$BM" 2>/dev/null
