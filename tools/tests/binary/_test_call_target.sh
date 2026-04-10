#!/bin/bash
# Find ALL call instructions in the 64-bit entry code of Stage 1
# and verify what they call
python3 << 'PYEOF'
import struct, subprocess

data = open('/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/stage1.elf', 'rb').read()

# Find 64-bit entry
for i in range(0x90, 0x200):
    if data[i] == 0xEA and data[i+5] == 0x08:
        entry_va = struct.unpack_from('<I', data, i+1)[0]
        break

e_phoff = struct.unpack_from('<I', data, 0x1c)[0]
p_off = struct.unpack_from('<I', data, e_phoff+4)[0]
p_va = struct.unpack_from('<I', data, e_phoff+8)[0]
p_fsz = struct.unpack_from('<I', data, e_phoff+16)[0]
entry_off = p_off + (entry_va - p_va)
end_off = p_off + p_fsz

# Disassemble the 64-bit entry code
code = data[entry_off:end_off]
tmpf = '/tmp/entry_code.bin'
open(tmpf, 'wb').write(code)
result = subprocess.run(
    ['objdump', '-D', '-b', 'binary', '-m', 'i386:x86-64',
     f'--adjust-vma=0x{entry_va:x}', tmpf],
    capture_output=True, text=True
)

# Find all CALL instructions
print(f"64-bit entry at 0x{entry_va:x}, code size {len(code)} bytes")
print(f"\nCALL instructions in __start:")
for line in result.stdout.split('\n'):
    if ':\t' in line and '\tcall' in line:
        print(f"  {line.strip()}")

# Also find the first CALL (to main)
# In the reference, main is called right after heap-hwm setup
# Let's show 20 instructions around each CALL
calls = []
for line in result.stdout.split('\n'):
    if ':\t' in line and '\tcall' in line:
        parts = line.strip().split(':')
        addr = int(parts[0].strip(), 16)
        # Extract target
        mn = line.split('\t')[-1].strip()
        target_str = mn.replace('call', '').strip()
        try:
            target = int(target_str, 16)
            calls.append((addr, target))
        except:
            calls.append((addr, 0))

print(f"\nResolved calls:")
for addr, target in calls:
    # What's at the target?
    target_off = p_off + (target - p_va)
    if 0 <= target_off < len(data):
        # Show first 5 bytes at target
        context = ' '.join(f'{data[target_off+j]:02x}' for j in range(min(10, len(data)-target_off)))
        print(f"  0x{addr:x} → 0x{target:x}  [{context}]")
    else:
        print(f"  0x{addr:x} → 0x{target:x}  [out of range]")

# Check the reference Stage 0 for comparison
data0 = open('/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf', 'rb').read()
for i in range(0x90, 0x200):
    if data0[i] == 0xEA and data0[i+5] == 0x08:
        entry_va0 = struct.unpack_from('<I', data0, i+1)[0]
        break
e_phoff0 = struct.unpack_from('<I', data0, 0x1c)[0]
p_off0 = struct.unpack_from('<I', data0, e_phoff0+4)[0]
p_va0 = struct.unpack_from('<I', data0, e_phoff0+8)[0]
p_fsz0 = struct.unpack_from('<I', data0, e_phoff0+16)[0]
entry_off0 = p_off0 + (entry_va0 - p_va0)
code0 = data0[entry_off0:p_off0+p_fsz0]
tmpf0 = '/tmp/entry_code0.bin'
open(tmpf0, 'wb').write(code0)
result0 = subprocess.run(
    ['objdump', '-D', '-b', 'binary', '-m', 'i386:x86-64',
     f'--adjust-vma=0x{entry_va0:x}', tmpf0],
    capture_output=True, text=True
)
print(f"\nReference (Stage 0) calls in __start:")
for line in result0.stdout.split('\n'):
    if ':\t' in line and '\tcall' in line:
        print(f"  {line.strip()}")

PYEOF
