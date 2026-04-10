#!/bin/bash
python3 << 'PYEOF'
data = open('/tmp/pd-test.elf','rb').read()
# CCE→Unicode table: [0, 10, 32, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 101]
pattern = bytes([0, 10, 32, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 101])
found = False
for i in range(len(data) - len(pattern)):
    if data[i:i+len(pattern)] == pattern:
        va = 0x100000 + i - 0x90
        ctx = ' '.join(f'{b:02x}' for b in data[i:i+32])
        print(f'CCE table at file 0x{i:x} (vaddr 0x{va:x}): {ctx}')
        found = True
        break
if not found:
    print('CCE table NOT FOUND')
    # Check last 500 bytes
    print(f'Last 50 bytes of file (offset 0x{len(data)-50:x}):')
    print(' '.join(f'{b:02x}' for b in data[-50:]))
    # Check around expected rodata area
    print(f'\nAround text end (offset 0x2F40):')
    if len(data) >= 0x2F60:
        print(' '.join(f'{b:02x}' for b in data[0x2F40:0x2F60]))
PYEOF
