#!/bin/bash
# Side-by-side instruction comparison, normalized by opcode
REF=/mnt/d/Projects/NewRepository-cam/build-output/ref-small.elf
BM=/tmp/bm-small.elf

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

def disassemble(data, segments, label):
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

    # Parse instructions
    instrs = []
    for line in result.stdout.split('\n'):
        if ':\t' in line:
            parts = line.split('\t')
            if len(parts) >= 3:
                addr = parts[0].strip().rstrip(':').strip()
                hexbytes = parts[1].strip()
                mnemonic = '\t'.join(parts[2:]).strip()
                instrs.append((addr, hexbytes, mnemonic))
    return instrs, target

ref_data, ref_segs = parse_elf32('/mnt/d/Projects/NewRepository-cam/build-output/ref-small.elf')
bm_data, bm_segs = parse_elf32('/tmp/bm-small.elf')

ref_instrs, ref_entry = disassemble(ref_data, ref_segs, 'ref')
bm_instrs, bm_entry = disassemble(bm_data, bm_segs, 'bm')

print(f"Reference: {len(ref_instrs)} instructions starting at 0x{ref_entry:x}")
print(f"Self-hosted: {len(bm_instrs)} instructions starting at 0x{bm_entry:x}")

# Find CLI instruction (start of actual bare-metal code) in both
def find_cli(instrs):
    for i, (addr, hx, mn) in enumerate(instrs):
        if mn.strip() == 'cli':
            return i
    return 0

ref_cli = find_cli(ref_instrs)
bm_cli = find_cli(bm_instrs)
print(f"\nReference CLI at instruction {ref_cli} (0x{ref_instrs[ref_cli][0]})")
print(f"Self-hosted CLI at instruction {bm_cli} (0x{bm_instrs[bm_cli][0]})")
print(f"Reference has {ref_cli} instructions before CLI (prologue)")
print(f"Self-hosted has {bm_cli} instructions before CLI")

# Compare from CLI onward, instruction by instruction
print(f"\n{'='*80}")
print("SIDE-BY-SIDE from CLI onward (showing first divergence region)")
print(f"{'='*80}")

ref_from_cli = ref_instrs[ref_cli:]
bm_from_cli = bm_instrs[bm_cli:]

# Normalize an instruction: remove absolute addresses from immediates
# but keep the opcode and register names
def normalize_instr(mn):
    # Keep the instruction as-is for now
    return mn

diverge_count = 0
match_count = 0
i = 0
j = 0
max_show = 200

while i < len(ref_from_cli) and j < len(bm_from_cli) and (match_count + diverge_count) < max_show:
    r_addr, r_hex, r_mn = ref_from_cli[i]
    b_addr, b_hex, b_mn = bm_from_cli[j]

    # Normalize: strip address-dependent parts
    r_norm = r_mn.split('#')[0].strip()  # remove comments
    b_norm = b_mn.split('#')[0].strip()

    # For mov with immediates, the addresses will differ, so compare opcode+registers only
    r_op = r_norm.split()[0] if r_norm else ''
    b_op = b_norm.split()[0] if b_norm else ''

    if r_norm == b_norm:
        match_count += 1
        if diverge_count > 0 or match_count <= 5:
            print(f"  MATCH [{i:4d}] ref@{r_addr}  bm@{b_addr}  {r_mn}")
        i += 1
        j += 1
    elif r_op == b_op:
        # Same opcode, different operands (likely address differences)
        print(f"  ~ADDR [{i:4d}] ref@{r_addr}: {r_mn}")
        print(f"         [{j:4d}] bm @{b_addr}: {b_mn}")
        match_count += 1
        i += 1
        j += 1
    else:
        diverge_count += 1
        print(f"  DIFF  [{i:4d}] ref@{r_addr}: {r_mn}")
        print(f"         [{j:4d}] bm @{b_addr}: {b_mn}")
        i += 1
        j += 1

print(f"\nShowed {match_count + diverge_count} instructions. Matches: {match_count}, Divergences: {diverge_count}")
print(f"Remaining: ref has {len(ref_from_cli) - i} more, bm has {len(bm_from_cli) - j} more")

# Now let's look for CALL instructions — these define the function boundaries
print(f"\n{'='*80}")
print("CALL instructions in both")
print(f"{'='*80}")

print("\nReference CALLs:")
for idx, (addr, hx, mn) in enumerate(ref_instrs):
    if 'call' in mn.lower():
        print(f"  [{idx:4d}] {addr}: {mn}")

print("\nSelf-hosted CALLs:")
for idx, (addr, hx, mn) in enumerate(bm_instrs):
    if 'call' in mn.lower():
        print(f"  [{idx:4d}] {addr}: {mn}")

# Look for INT instructions (interrupt calls — important for bare metal)
print(f"\n{'='*80}")
print("INT/OUT/IN instructions (I/O)")
print(f"{'='*80}")

print("\nReference I/O:")
for idx, (addr, hx, mn) in enumerate(ref_instrs):
    if mn.strip().startswith(('out', 'in ', 'int')):
        print(f"  [{idx:4d}] {addr}: {mn}")

print("\nSelf-hosted I/O:")
for idx, (addr, hx, mn) in enumerate(bm_instrs):
    if mn.strip().startswith(('out', 'in ', 'int')):
        print(f"  [{idx:4d}] {addr}: {mn}")

# Look for HLT (system halt)
print(f"\n{'='*80}")
print("HLT instructions")
print(f"{'='*80}")

print("\nReference HLT:")
for idx, (addr, hx, mn) in enumerate(ref_instrs):
    if 'hlt' in mn.lower():
        print(f"  [{idx:4d}] {addr}: {mn}")

print("\nSelf-hosted HLT:")
for idx, (addr, hx, mn) in enumerate(bm_instrs):
    if 'hlt' in mn.lower():
        print(f"  [{idx:4d}] {addr}: {mn}")

PYEOF
