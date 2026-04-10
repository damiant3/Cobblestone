#!/bin/bash
# Deep comparison - 32-bit ELF aware
REF=/mnt/d/Projects/NewRepository-cam/build-output/ref-small.elf
BM=/tmp/bm-small.elf

python3 << 'PYEOF'
import struct, subprocess, os

def parse_elf32(path):
    data = open(path, 'rb').read()
    # ELF32 header
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
        p_memsz = struct.unpack_from('<I', data, ph_off + 0x14)[0]
        segments.append((p_type, p_offset, p_vaddr, p_filesz, p_memsz))
    return data, segments

def find_far_jump(data):
    """Find EA xx xx xx xx 08 00 pattern (far jump to 64-bit code)"""
    for i in range(0x80, min(0x300, len(data)-6)):
        if data[i] == 0xEA:
            target = struct.unpack_from('<I', data, i+1)[0]
            seg = struct.unpack_from('<H', data, i+5)[0]
            if seg == 8:
                return i, target
    return None, None

def vaddr_to_fileoff(vaddr, segments):
    for p_type, p_offset, p_vaddr, p_filesz, p_memsz in segments:
        if p_type == 1:  # PT_LOAD
            if p_vaddr <= vaddr < p_vaddr + p_filesz:
                return p_offset + (vaddr - p_vaddr)
    return None

for label, path in [('REFERENCE', '/mnt/d/Projects/NewRepository-cam/build-output/ref-small.elf'),
                     ('SELF-HOSTED', '/tmp/bm-small.elf')]:
    data, segments = parse_elf32(path)
    jump_off, target = find_far_jump(data)

    load_seg = [(t,o,v,fs,ms) for t,o,v,fs,ms in segments if t == 1][0]
    text_file_start = load_seg[1]  # p_offset
    text_vaddr_start = load_seg[2]  # p_vaddr
    text_filesz = load_seg[3]

    entry_file_off = vaddr_to_fileoff(target, segments)

    print(f"\n{'='*60}")
    print(f"  {label}: {path}")
    print(f"  File size: {len(data)} bytes")
    print(f"  LOAD: file_off=0x{text_file_start:x} vaddr=0x{text_vaddr_start:x} filesz=0x{text_filesz:x}")
    print(f"  Far jump: file_off=0x{jump_off:x} -> vaddr=0x{target:x}")
    print(f"  64-bit entry file offset: 0x{entry_file_off:x}")
    print(f"  Code from 64-bit entry to end of LOAD: {text_file_start + text_filesz - entry_file_off} bytes")
    print(f"{'='*60}")

    # Extract 64-bit code region
    code_start = entry_file_off
    code_end = text_file_start + text_filesz
    code = data[code_start:code_end]

    tmpf = f'/tmp/code64_{label.lower().replace("-","")}.bin'
    open(tmpf, 'wb').write(code)

    # Disassemble with virtual addresses
    vaddr_base = target
    result = subprocess.run(
        ['objdump', '-D', '-b', 'binary', '-m', 'i386:x86-64',
         f'--adjust-vma=0x{vaddr_base:x}', tmpf],
        capture_output=True, text=True
    )

    # Write full disassembly to file
    outf = f'/tmp/disasm_{label.lower().replace("-","")}.txt'
    open(outf, 'w').write(result.stdout)
    print(f"  Full disassembly written to: {outf}")

    # Print it
    lines = result.stdout.strip().split('\n')
    for line in lines:
        print(line)

# Now do a structured comparison
print("\n" + "="*60)
print("  STRUCTURED COMPARISON")
print("="*60)

# Find RET-delimited functions in both
def extract_functions(path, vaddr_base, code):
    """Split code into functions by RET instructions"""
    tmpf = f'/tmp/funcextract_{os.path.basename(path)}.bin'
    open(tmpf, 'wb').write(code)
    result = subprocess.run(
        ['objdump', '-D', '-b', 'binary', '-m', 'i386:x86-64',
         f'--adjust-vma=0x{vaddr_base:x}', tmpf],
        capture_output=True, text=True
    )

    functions = []
    current = []
    for line in result.stdout.strip().split('\n'):
        if ':\t' in line:
            current.append(line.strip())
            if '\tret' in line and '\tret' in line.split(':\t',1)[1]:
                functions.append(current)
                current = []
    if current:
        functions.append(current)
    return functions

ref_data, ref_segs = parse_elf32('/mnt/d/Projects/NewRepository-cam/build-output/ref-small.elf')
bm_data, bm_segs = parse_elf32('/tmp/bm-small.elf')

_, ref_target = find_far_jump(ref_data)
_, bm_target = find_far_jump(bm_data)

ref_load = [(t,o,v,fs,ms) for t,o,v,fs,ms in ref_segs if t == 1][0]
bm_load = [(t,o,v,fs,ms) for t,o,v,fs,ms in bm_segs if t == 1][0]

ref_entry_off = vaddr_to_fileoff(ref_target, ref_segs)
bm_entry_off = vaddr_to_fileoff(bm_target, bm_segs)

ref_code = ref_data[ref_entry_off:ref_load[1]+ref_load[3]]
bm_code = bm_data[bm_entry_off:bm_load[1]+bm_load[3]]

ref_funcs = extract_functions('ref', ref_target, ref_code)
bm_funcs = extract_functions('bm', bm_target, bm_code)

print(f"\nReference functions (RET-delimited): {len(ref_funcs)}")
print(f"Self-hosted functions (RET-delimited): {len(bm_funcs)}")

# Show each function's size and first few instructions
for i, (label, funcs) in enumerate([("REFERENCE", ref_funcs), ("SELF-HOSTED", bm_funcs)]):
    print(f"\n--- {label} function map ---")
    for j, func in enumerate(funcs):
        first_addr = func[0].split(':')[0].strip() if func else '?'
        n_instr = len(func)
        # Get the opcodes to fingerprint the function
        opcodes = []
        for line in func[:3]:
            parts = line.split('\t')
            if len(parts) >= 3:
                opcodes.append(parts[2].strip().split()[0])
        print(f"  func[{j:2d}] @ 0x{first_addr}  {n_instr:3d} instrs  starts: {' '.join(opcodes)}")

# Compare function-by-function
print(f"\n--- Function-by-function comparison ---")
n = min(len(ref_funcs), len(bm_funcs))
for i in range(n):
    ref_f = ref_funcs[i]
    bm_f = bm_funcs[i]

    # Normalize: strip addresses, keep only opcodes
    def normalize(func):
        normed = []
        for line in func:
            parts = line.split('\t')
            if len(parts) >= 3:
                normed.append('\t'.join(parts[2:]))
        return normed

    ref_norm = normalize(ref_f)
    bm_norm = normalize(bm_f)

    if ref_norm == bm_norm:
        print(f"  func[{i:2d}]: MATCH ({len(ref_f)} instrs)")
    else:
        print(f"  func[{i:2d}]: DIFFER (ref={len(ref_f)} instrs, bm={len(bm_f)} instrs)")
        # Show first difference
        for k in range(max(len(ref_norm), len(bm_norm))):
            r = ref_norm[k] if k < len(ref_norm) else "(missing)"
            b = bm_norm[k] if k < len(bm_norm) else "(missing)"
            if r != b:
                print(f"           first diff at instr {k}:")
                print(f"             ref: {r}")
                print(f"             bm:  {b}")
                break

# If ref has more functions, show the extras
if len(ref_funcs) > len(bm_funcs):
    print(f"\n--- MISSING from self-hosted (ref functions {len(bm_funcs)}..{len(ref_funcs)-1}) ---")
    for i in range(len(bm_funcs), len(ref_funcs)):
        func = ref_funcs[i]
        first_addr = func[0].split(':')[0].strip()
        print(f"  ref func[{i}] @ 0x{first_addr} ({len(func)} instrs):")
        for line in func[:10]:
            print(f"    {line}")
        if len(func) > 10:
            print(f"    ... ({len(func)-10} more)")

PYEOF
