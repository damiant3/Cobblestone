#!/bin/bash
# Compare reference vs fixed self-hosted ELF — focus on structural differences
REF=/mnt/d/Projects/NewRepository-cam/build-output/ref-small.elf
BM=/tmp/bm-small-fixed.elf

python3 << 'PYEOF'
import struct, subprocess

def parse_elf32(path):
    data = open(path, 'rb').read()
    e_phoff = struct.unpack_from('<I', data, 0x1c)[0]
    e_phnum = struct.unpack_from('<H', data, 0x2c)[0]
    e_phentsize = struct.unpack_from('<H', data, 0x2a)[0]
    segments = []
    for j in range(e_phnum):
        ph_off = e_phoff + j * e_phentsize
        p_type = struct.unpack_from('<I', data, ph_off)[0]
        p_offset = struct.unpack_from('<I', data, ph_off + 0x04)[0]
        p_vaddr = struct.unpack_from('<I', data, ph_off + 0x08)[0]
        p_filesz = struct.unpack_from('<I', data, ph_off + 0x10)[0]
        segments.append((p_type, p_offset, p_vaddr, p_filesz))
    return data, segments

def find_far_jump(data):
    for i in range(0x80, min(0x300, len(data)-6)):
        if data[i] == 0xEA:
            target = struct.unpack_from('<I', data, i+1)[0]
            seg = struct.unpack_from('<H', data, i+5)[0]
            if seg == 8:
                return i, target
    return None, None

def vaddr_to_fileoff(vaddr, segments):
    for p_type, p_offset, p_vaddr, p_filesz in segments:
        if p_type == 1 and p_vaddr <= vaddr < p_vaddr + p_filesz:
            return p_offset + (vaddr - p_vaddr)
    return None

def disasm(data, segments, label):
    _, target = find_far_jump(data)
    entry_off = vaddr_to_fileoff(target, segments)
    load = [s for s in segments if s[0] == 1][0]
    code_end = load[1] + load[3]
    code = data[entry_off:code_end]
    tmpf = f'/tmp/code64_{label}.bin'
    open(tmpf, 'wb').write(code)
    result = subprocess.run(
        ['objdump', '-D', '-b', 'binary', '-m', 'i386:x86-64',
         f'--adjust-vma=0x{target:x}', tmpf],
        capture_output=True, text=True
    )
    instrs = []
    for line in result.stdout.split('\n'):
        if ':\t' in line:
            parts = line.split('\t')
            if len(parts) >= 3:
                addr = parts[0].strip().rstrip(':').strip()
                mnemonic = '\t'.join(parts[2:]).strip()
                instrs.append((addr, mnemonic))
    return instrs, target, entry_off, code_end

ref_data, ref_segs = parse_elf32('/mnt/d/Projects/NewRepository-cam/build-output/ref-small.elf')
bm_data, bm_segs = parse_elf32('/tmp/bm-small-fixed.elf')

ref_instrs, ref_entry, ref_off, ref_end = disasm(ref_data, ref_segs, 'ref')
bm_instrs, bm_entry, bm_off, bm_end = disasm(bm_data, bm_segs, 'bm')

print(f"Reference: {len(ref_instrs)} instructions, 64-bit entry @ 0x{ref_entry:x}")
print(f"Self-hosted: {len(bm_instrs)} instructions, 64-bit entry @ 0x{bm_entry:x}")

# Find CLI in both
def find_instr(instrs, mnemonic):
    for i, (addr, mn) in enumerate(instrs):
        if mn.strip() == mnemonic:
            return i
    return -1

ref_cli = find_instr(ref_instrs, 'cli')
bm_cli = find_instr(bm_instrs, 'cli')

# Find key markers in both
markers = ['cli', 'sti', 'hlt', 'wrmsr', 'rdmsr', 'lidt']
for m in markers:
    ref_idxs = [i for i, (a, mn) in enumerate(ref_instrs) if mn.strip() == m]
    bm_idxs = [i for i, (a, mn) in enumerate(bm_instrs) if mn.strip() == m]
    print(f"  {m:8s}: ref={ref_idxs}  bm={bm_idxs}")

# Find all OUT instructions (serial I/O)
ref_outs = [(i, a, mn) for i, (a, mn) in enumerate(ref_instrs) if mn.strip().startswith('out')]
bm_outs = [(i, a, mn) for i, (a, mn) in enumerate(bm_instrs) if mn.strip().startswith('out')]
print(f"\n  OUT instructions: ref={len(ref_outs)}  bm={len(bm_outs)}")

# Find all CALL instructions
ref_calls = [(i, a, mn) for i, (a, mn) in enumerate(ref_instrs) if 'call' in mn]
bm_calls = [(i, a, mn) for i, (a, mn) in enumerate(bm_instrs) if 'call' in mn]
print(f"  CALL instructions: ref={len(ref_calls)}  bm={len(bm_calls)}")
for i, a, mn in ref_calls:
    print(f"    ref [{i}] @ {a}: {mn}")
for i, a, mn in bm_calls:
    print(f"    bm  [{i}] @ {a}: {mn}")

# Find all RET instructions
ref_rets = [i for i, (a, mn) in enumerate(ref_instrs) if mn.strip() == 'ret']
bm_rets = [i for i, (a, mn) in enumerate(bm_instrs) if mn.strip() == 'ret']
print(f"  RET instructions: ref={len(ref_rets)}  bm={len(bm_rets)}")

# Find instruction after last page table entry (where code diverges after fix)
# Page table entries end with "mov %rax,0x7f8(%rdi)" (last entry = index 255, offset 0x7f8)
# After that, let's find where both ELFs do their CR3 write
for label, instrs in [('ref', ref_instrs), ('bm', bm_instrs)]:
    cr3_idx = -1
    for i, (a, mn) in enumerate(instrs):
        if 'cr3' in mn:
            cr3_idx = i
            break
    if cr3_idx >= 0:
        print(f"\n  {label} CR3 write at instruction {cr3_idx}: {instrs[cr3_idx]}")
        print(f"    Context (5 before, 10 after):")
        for j in range(max(0, cr3_idx-5), min(len(instrs), cr3_idx+11)):
            mark = " >>>" if j == cr3_idx else "    "
            print(f"    {mark} [{j}] {instrs[j][0]}: {instrs[j][1]}")

# Compare from STI onward (after all setup, before user code)
ref_sti = find_instr(ref_instrs, 'sti')
bm_sti = find_instr(bm_instrs, 'sti')
if ref_sti >= 0 and bm_sti >= 0:
    print(f"\n=== Code from STI onward (serial init done, printing starts) ===")
    print(f"  ref STI at [{ref_sti}], bm STI at [{bm_sti}]")
    print(f"\n--- Reference post-STI (20 instructions) ---")
    for j in range(ref_sti, min(len(ref_instrs), ref_sti + 20)):
        print(f"  [{j}] {ref_instrs[j][0]}: {ref_instrs[j][1]}")
    print(f"\n--- Self-hosted post-STI (20 instructions) ---")
    for j in range(bm_sti, min(len(bm_instrs), bm_sti + 20)):
        print(f"  [{j}] {bm_instrs[j][0]}: {bm_instrs[j][1]}")

# After READY printing, look for where the ELFs differ in behavior
# Find HLT instruction
ref_hlt = find_instr(ref_instrs, 'hlt')
bm_hlt = find_instr(bm_instrs, 'hlt')
if ref_hlt >= 0 and bm_hlt >= 0:
    print(f"\n=== Code near HLT ===")
    print(f"  ref HLT at [{ref_hlt}], bm HLT at [{bm_hlt}]")
    print(f"\n--- Reference pre-HLT (10 instructions) ---")
    for j in range(max(0, ref_hlt-10), ref_hlt+1):
        print(f"  [{j}] {ref_instrs[j][0]}: {ref_instrs[j][1]}")
    print(f"\n--- Self-hosted pre-HLT (10 instructions) ---")
    for j in range(max(0, bm_hlt-10), bm_hlt+1):
        print(f"  [{j}] {bm_instrs[j][0]}: {bm_instrs[j][1]}")

# Full disassembly of code BEFORE the 64-bit entry (trampoline + helper functions)
print(f"\n=== Helper functions (before 64-bit entry) ===")
for label, data, segs, entry_off in [('ref', ref_data, ref_segs, ref_off), ('bm', bm_data, bm_segs, bm_off)]:
    load = [s for s in segs if s[0] == 1][0]
    # Code from trampoline end to 64-bit entry
    # The trampoline starts at the LOAD offset; the far jump target is the 64-bit entry
    # Helper functions are between the trampoline and the 64-bit entry, but also could be after
    tramp_start = load[1]  # file offset of LOAD
    tramp_end = entry_off  # file offset of 64-bit entry
    helpers_size = tramp_end - tramp_start
    code_after_entry = load[1] + load[3] - entry_off
    print(f"\n  {label}: trampoline+helpers = {helpers_size} bytes (0x{tramp_start:x}-0x{entry_off:x})")
    print(f"  {label}: 64-bit code = {code_after_entry} bytes")

PYEOF
