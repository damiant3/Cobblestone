#!/bin/bash
# Check what __read_line code is in Stage 1 ELF
STAGE1=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/stage1.elf

python3 << 'PYEOF'
import struct, subprocess

data = open('/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/stage1.elf', 'rb').read()

# serial-write-pos-addr = 28704 = 0x7020
# serial-read-pos-addr = 28712 = 0x7028
# serial-ring-buf-addr = 3932160 = 0x3C0000
# serial-ring-buf-mask = 262143 = 0x3FFFF

# If __read_line uses ring buffer, it'll load 0x7020 (serial-write-pos-addr)
# If it uses direct port I/O, it'll load 0x3FD (1021)

# Search for both patterns in the binary
# li rdi, 0x7020 = 48 C7 C7 20 70 00 00
ring_buf_pattern = bytes([0x48, 0xC7, 0xC7, 0x20, 0x70, 0x00, 0x00])
# li rdx, 0x3FD = 48 C7 C2 FD 03 00 00
direct_io_pattern = bytes([0x48, 0xC7, 0xC2, 0xFD, 0x03, 0x00, 0x00])

print("Searching Stage 1 ELF for I/O patterns...")
ring_count = 0
direct_count = 0
i = 0
while i < len(data) - 7:
    if data[i:i+7] == ring_buf_pattern:
        ring_count += 1
        if ring_count <= 5:
            print(f"  Ring buffer (li rdi, 0x7020) at offset 0x{i:x}")
    if data[i:i+7] == direct_io_pattern:
        direct_count += 1
        if direct_count <= 5:
            print(f"  Direct I/O (li rdx, 0x3FD) at offset 0x{i:x}")
    i += 1

print(f"\nTotal: ring_buf={ring_count}, direct_io={direct_count}")

# Also check for 0x3F8 (data port) direct reads
port_3f8 = bytes([0x48, 0xC7, 0xC2, 0xF8, 0x03, 0x00, 0x00])
port_count = 0
i = 0
while i < len(data) - 7:
    if data[i:i+7] == port_3f8:
        port_count += 1
    i += 1
print(f"Direct port 0x3F8 loads: {port_count}")

# Also check the same in Stage 0 for comparison
data0 = open('/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf', 'rb').read()
ring_count0 = 0
direct_count0 = 0
i = 0
while i < len(data0) - 7:
    if data0[i:i+7] == ring_buf_pattern:
        ring_count0 += 1
    if data0[i:i+7] == direct_io_pattern:
        direct_count0 += 1
    i += 1
print(f"\nStage 0: ring_buf={ring_count0}, direct_io={direct_count0}")

PYEOF
