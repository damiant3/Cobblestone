#!/bin/bash
# Compare the code after READY printing — where main is called and result printed
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

    # Find far jump target
    for i in range(0x90, 0x200):
        if data[i] == 0xEA and data[i+5] == 0x08 and data[i+6] == 0x00:
            target = struct.unpack_from('<I', data, i+1)[0]
            break

    entry_off = p_off + (target - p_va)
    code = data[entry_off:p_off + p_fsz]
    tmpf = f'/tmp/main_flow_{label}.bin'
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
    return instrs, target

ref_instrs, ref_entry = parse_and_disasm('/mnt/d/Projects/NewRepository-cam/build-output/ref-small.elf', 'ref')
bm_instrs, bm_entry = parse_and_disasm('/tmp/bm-small-fixed.elf', 'bm')

# Find the READY printing section (0x52='R' being loaded)
def find_ready_print(instrs):
    for i, (a, mn) in enumerate(instrs):
        if '$0x52' in mn and 'mov' in mn:  # loading 'R'
            return i
    return -1

ref_ready = find_ready_print(ref_instrs)
bm_ready = find_ready_print(bm_instrs)

# Find the code AFTER READY printing — look for where 'Y' (0x59) + newline (0xa) is printed
# then the next code block
def find_after_ready(instrs, ready_idx):
    # Find 0x59 ('Y') after ready_idx
    for i in range(ready_idx, min(len(instrs), ready_idx + 100)):
        if '$0x59' in instrs[i][1] and 'mov' in instrs[i][1]:
            # Found 'Y', look for the newline (0xa) after it
            for j in range(i, min(len(instrs), i + 20)):
                if '$0xa' in instrs[j][1] and 'mov' in instrs[j][1]:
                    # Look for the next non-serial-output code
                    for k in range(j, min(len(instrs), j + 20)):
                        if 'out' not in instrs[k][1] and 'in' not in instrs[k][1] and 'test' not in instrs[k][1] and 'jn' not in instrs[k][1] and 'jmp' not in instrs[k][1]:
                            return k
    return -1

# Show 50 instructions from after READY in both
for label, instrs, ready in [('REFERENCE', ref_instrs, ref_ready), ('SELF-HOSTED', bm_instrs, bm_ready)]:
    after = find_after_ready(instrs, ready)
    print(f"\n{'='*60}")
    print(f"  {label}: READY at [{ready}], post-READY code at [{after}]")
    print(f"  Showing 80 instructions from post-READY")
    print(f"{'='*60}")
    start = after if after >= 0 else ready
    for i in range(start, min(len(instrs), start + 80)):
        marker = ""
        mn = instrs[i][1]
        if 'call' in mn: marker = "  <-- CALL"
        elif 'ret' in mn: marker = "  <-- RET"
        elif 'hlt' in mn: marker = "  <-- HLT"
        elif 'out' in mn and 'al' in mn: marker = "  <-- SERIAL OUT"
        elif '$0x2a' in mn: marker = "  <-- 42!"
        elif 'jmp' in mn and '0x' in mn:
            # Check if it's a backwards jump (loop)
            try:
                tgt = int(mn.split('0x')[1].strip(), 16)
                cur = int(instrs[i][0], 16)
                if tgt < cur:
                    marker = "  <-- LOOP BACK"
            except:
                pass
        print(f"  [{i:4d}] {instrs[i][0]}: {mn}{marker}")

PYEOF
