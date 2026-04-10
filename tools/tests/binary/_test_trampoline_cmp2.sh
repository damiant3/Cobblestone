#!/bin/bash
# Compare trampolines and helper functions between reference and self-hosted
python3 << 'PYEOF'
import struct, subprocess

REF = '/mnt/d/Projects/NewRepository-cam/build-output/ref-small.elf'
BM = '/tmp/bm-small-fixed.elf'

def parse_elf32(path):
    data = open(path, 'rb').read()
    e_phoff = struct.unpack_from('<I', data, 0x1c)[0]
    e_phnum = struct.unpack_from('<H', data, 0x2c)[0]
    e_phentsize = struct.unpack_from('<H', data, 0x2a)[0]
    for j in range(e_phnum):
        ph_off = e_phoff + j * e_phentsize
        p_type = struct.unpack_from('<I', data, ph_off)[0]
        p_offset = struct.unpack_from('<I', data, ph_off + 0x04)[0]
        p_vaddr = struct.unpack_from('<I', data, ph_off + 0x08)[0]
        p_filesz = struct.unpack_from('<I', data, ph_off + 0x10)[0]
        if p_type == 1:
            return data, p_offset, p_vaddr, p_filesz
    return data, 0, 0, 0

def find_far_jump(data):
    for i in range(0x80, min(0x300, len(data)-6)):
        if data[i] == 0xEA:
            target = struct.unpack_from('<I', data, i+1)[0]
            seg = struct.unpack_from('<H', data, i+5)[0]
            if seg == 8: return i, target
    return None, None

ref_data, ref_poff, ref_vaddr, ref_fsz = parse_elf32(REF)
bm_data, bm_poff, bm_vaddr, bm_fsz = parse_elf32(BM)

_, ref_entry = find_far_jump(ref_data)
_, bm_entry = find_far_jump(bm_data)

ref_entry_off = ref_poff + (ref_entry - ref_vaddr)
bm_entry_off = bm_poff + (bm_entry - bm_vaddr)

# Compare trampoline (first 0xA4 bytes should be identical except for far jump address)
TRAMP_SIZE = 0xA4  # up to the far jump
ref_tramp = ref_data[ref_poff:ref_poff + TRAMP_SIZE]
bm_tramp = bm_data[bm_poff:bm_poff + TRAMP_SIZE]

print("=== Trampoline comparison (first 0xA4 bytes of LOAD) ===")
if ref_tramp == bm_tramp:
    print("  IDENTICAL")
else:
    diffs = 0
    for i in range(TRAMP_SIZE):
        if ref_tramp[i] != bm_tramp[i]:
            diffs += 1
            if diffs <= 10:
                print(f"  Diff at offset 0x{ref_poff+i:x}: ref=0x{ref_tramp[i]:02x} bm=0x{bm_tramp[i]:02x}")
    print(f"  Total diffs: {diffs}")

# Disassemble the helper functions (between trampoline and 64-bit entry)
# Reference: helpers from trampoline end to 0x1f1c
# Self-hosted: helpers from trampoline end to 0x1723
print(f"\n=== Helper function region ===")
print(f"  Reference: 0x{ref_poff+TRAMP_SIZE:x} to 0x{ref_entry_off:x} ({ref_entry_off - ref_poff - TRAMP_SIZE} bytes)")
print(f"  Self-hosted: 0x{bm_poff+TRAMP_SIZE:x} to 0x{bm_entry_off:x} ({bm_entry_off - bm_poff - TRAMP_SIZE} bytes)")

# Disassemble both helper regions
for label, data, start, end, vaddr_base in [
    ('ref', ref_data, ref_poff, ref_entry_off, ref_vaddr),
    ('bm', bm_data, bm_poff, bm_entry_off, bm_vaddr)
]:
    code = data[start:end]
    tmpf = f'/tmp/helpers_{label}.bin'
    open(tmpf, 'wb').write(code)
    adj = vaddr_base
    result = subprocess.run(
        ['objdump', '-D', '-b', 'binary', '-m', 'i386:x86-64',
         f'--adjust-vma=0x{adj:x}', tmpf],
        capture_output=True, text=True
    )
    # Write to file
    outf = f'/tmp/helpers_{label}.txt'
    open(outf, 'w').write(result.stdout)

    # Find RET-delimited functions
    instrs = []
    for line in result.stdout.split('\n'):
        if ':\t' in line:
            parts = line.split('\t')
            if len(parts) >= 3:
                addr = parts[0].strip().rstrip(':').strip()
                mn = '\t'.join(parts[2:]).strip()
                instrs.append((addr, mn))

    # Find functions by looking for common prologs (push rbp, push rbx) and rets
    print(f"\n--- {label} helpers: {len(instrs)} instructions ---")
    # Look for patterns: call targets from the 64-bit code
    # Reference calls: 0x1013a5, 0x100204
    # Self-hosted calls: 0x100d08

    # Find all RET instructions
    rets = [(i, a) for i, (a, mn) in enumerate(instrs) if mn.strip() == 'ret' or mn.strip() == 'retq']
    print(f"  RET positions: {[(i, a) for i, a in rets]}")

    # Show 20 instructions before each RET to see function bodies
    for ri, (ret_idx, ret_addr) in enumerate(rets):
        func_start = rets[ri-1][0]+1 if ri > 0 else 0
        print(f"\n  Function ending at [{ret_idx}] {ret_addr}:")
        show_from = max(func_start, ret_idx - 30)
        for j in range(show_from, ret_idx + 1):
            print(f"    [{j}] {instrs[j][0]}: {instrs[j][1]}")

# Also check: what does the self-hosted call at 0x100d08?
print(f"\n=== Self-hosted call target 0x100d08 ===")
# This is vaddr 0x100d08. File offset = bm_poff + (0x100d08 - bm_vaddr)
target_off = bm_poff + (0x100d08 - bm_vaddr)
print(f"  File offset: 0x{target_off:x}")
# Show 30 instructions from there
code = bm_data[target_off:target_off+300]
tmpf = '/tmp/call_target.bin'
open(tmpf, 'wb').write(code)
result = subprocess.run(
    ['objdump', '-D', '-b', 'binary', '-m', 'i386:x86-64',
     f'--adjust-vma=0x100d08', tmpf],
    capture_output=True, text=True
)
for line in result.stdout.split('\n')[:40]:
    if ':\t' in line:
        print(f"  {line.strip()}")

# And reference call targets
for target_vaddr in [0x1013a5, 0x100204]:
    print(f"\n=== Reference call target 0x{target_vaddr:x} ===")
    target_off = ref_poff + (target_vaddr - ref_vaddr)
    print(f"  File offset: 0x{target_off:x}")
    code = ref_data[target_off:target_off+300]
    tmpf = '/tmp/ref_call_target.bin'
    open(tmpf, 'wb').write(code)
    result = subprocess.run(
        ['objdump', '-D', '-b', 'binary', '-m', 'i386:x86-64',
         f'--adjust-vma=0x{target_vaddr:x}', tmpf],
        capture_output=True, text=True
    )
    for line in result.stdout.split('\n')[:40]:
        if ':\t' in line:
            print(f"  {line.strip()}")

PYEOF
