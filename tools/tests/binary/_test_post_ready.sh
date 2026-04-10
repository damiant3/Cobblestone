#!/bin/bash
# Find the main function execution and result printing
python3 << 'PYEOF'
import struct, subprocess

def parse_and_disasm(path, label):
    data = open(path, 'rb').read()
    e_phoff = struct.unpack_from('<I', data, 0x1c)[0]
    for j in range(struct.unpack_from('<H', data, 0x2c)[0]):
        ph_off = e_phoff + j * struct.unpack_from('<H', data, 0x2a)[0]
        if struct.unpack_from('<I', data, ph_off)[0] == 1:
            p_off = struct.unpack_from('<I', data, ph_off+4)[0]
            p_va = struct.unpack_from('<I', data, ph_off+8)[0]
            p_fsz = struct.unpack_from('<I', data, ph_off+16)[0]
            break
    for i in range(0x90, 0x200):
        if data[i] == 0xEA and data[i+5] == 0x08 and data[i+6] == 0x00:
            target = struct.unpack_from('<I', data, i+1)[0]
            break
    entry_off = p_off + (target - p_va)
    code = data[entry_off:p_off + p_fsz]
    tmpf = f'/tmp/postready_{label}.bin'
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
                mn = '\t'.join(parts[2:]).strip()
                instrs.append((addr, mn))
    return instrs

ref = parse_and_disasm('/mnt/d/Projects/NewRepository-cam/build-output/ref-small.elf', 'ref')
bm = parse_and_disasm('/tmp/bm-small-fixed.elf', 'bm')

# Find where READY newline is printed: mov $0xa, %rax followed by out
# Then find the next interesting code
def find_ready_end(instrs):
    """Find the instruction after READY+newline printing"""
    # Look for STI first, then find the 'Y' char (0x59) being sent
    sti_idx = None
    for i, (a, mn) in enumerate(instrs):
        if mn.strip() == 'sti':
            sti_idx = i
            break
    if sti_idx is None:
        return 0

    # After STI, look for 0xa (newline) being loaded then out
    for i in range(sti_idx, min(len(instrs), sti_idx + 200)):
        if '$0xa' in instrs[i][1] and 'mov' in instrs[i][1]:
            # Found newline load, look for the out instruction after it
            for j in range(i, min(len(instrs), i + 10)):
                if 'out' in instrs[j][1]:
                    # The instruction after the newline out is post-READY
                    return j + 1
    return sti_idx

ref_end = find_ready_end(ref)
bm_end = find_ready_end(bm)

for label, instrs, start in [('REFERENCE', ref, ref_end), ('SELF-HOSTED', bm, bm_end)]:
    print(f"\n{'='*70}")
    print(f"  {label}: Post-READY code starts at [{start}]")
    print(f"  Showing 100 instructions through main execution + result printing")
    print(f"{'='*70}")
    for i in range(start, min(len(instrs), start + 100)):
        mn = instrs[i][1]
        marker = ""
        if 'call' in mn: marker = "  <<< CALL"
        elif 'ret' in mn: marker = "  <<< RET"
        elif 'hlt' in mn: marker = "  <<< HLT"
        elif 'out' in mn and 'al' in mn: marker = "  <<< OUT"
        elif 'in' in mn and 'al' in mn: marker = "  <<< IN"
        elif '$0x2a' in mn: marker = "  <<< 42!"
        elif 'jmp' in mn:
            try:
                tgt = int(mn.split('0x')[1].strip(), 16)
                cur = int(instrs[i][0], 16)
                if tgt < cur: marker = "  <<< LOOP"
                elif tgt > cur + 0x100: marker = "  <<< FAR JMP"
            except: pass
        print(f"  [{i:4d}] {instrs[i][0]}: {mn}{marker}")

PYEOF
