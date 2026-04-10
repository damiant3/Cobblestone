#!/bin/bash
# Deep comparison of ref-small.elf vs bm-small.elf
REF=/mnt/d/Projects/NewRepository-cam/build-output/ref-small.elf
BM=/tmp/bm-small.elf

echo "=== File sizes ==="
echo "Reference:   $(wc -c < "$REF") bytes"
echo "Self-hosted: $(wc -c < "$BM") bytes"

echo ""
echo "=== ELF Headers ==="
echo "-- Reference --"
readelf -h "$REF" 2>/dev/null | grep -E "Entry|Type|Machine"
echo "-- Self-hosted --"
readelf -h "$BM" 2>/dev/null | grep -E "Entry|Type|Machine"

echo ""
echo "=== Program Headers ==="
echo "-- Reference --"
readelf -l "$REF" 2>/dev/null
echo "-- Self-hosted --"
readelf -l "$BM" 2>/dev/null

echo ""
echo "=== RET instruction count ==="
echo "Reference RETs:"
objdump -D -b binary -m i386:x86-64 "$REF" 2>/dev/null | grep -c 'ret'
echo "Self-hosted RETs:"
objdump -D -b binary -m i386:x86-64 "$BM" 2>/dev/null | grep -c 'ret'

echo ""
echo "=== Far jump target (trampoline -> 64-bit entry) ==="
python3 -c "
import struct
for label, path in [('Reference', '$REF'), ('Self-hosted', '$BM')]:
    data = open(path,'rb').read()
    # Find EA pattern (far jump) in trampoline region
    for i in range(0x80, min(0x200, len(data)-6)):
        if data[i] == 0xEA:
            target = struct.unpack_from('<I', data, i+1)[0]
            seg = struct.unpack_from('<H', data, i+5)[0]
            if seg == 8:  # code segment selector
                print(f'{label}: far jump at file offset 0x{i:x}, target=0x{target:x}, segment=0x{seg:x}')
                # Calculate file offset of 64-bit entry
                # Virtual address base for text is typically 0x100000
                # ELF header + program headers typically at file start
                break
"

echo ""
echo "=== Full disassembly of both (64-bit code only) ==="
python3 -c "
import struct, subprocess, sys

for label, path in [('Reference', '$REF'), ('Self-hosted', '$BM')]:
    data = open(path,'rb').read()

    # Find the far jump to locate 64-bit entry
    entry_file_off = None
    for i in range(0x80, min(0x200, len(data)-6)):
        if data[i] == 0xEA:
            target = struct.unpack_from('<I', data, i+1)[0]
            seg = struct.unpack_from('<H', data, i+5)[0]
            if seg == 8:
                # Need to find the LOAD segment to calculate file offset
                # Parse ELF program headers
                e_phoff = struct.unpack_from('<Q', data, 0x20)[0]
                e_phnum = struct.unpack_from('<H', data, 0x38)[0]
                e_phentsize = struct.unpack_from('<H', data, 0x36)[0]
                for j in range(e_phnum):
                    ph_off = e_phoff + j * e_phentsize
                    p_type = struct.unpack_from('<I', data, ph_off)[0]
                    if p_type == 1:  # PT_LOAD
                        p_offset = struct.unpack_from('<Q', data, ph_off + 0x08)[0]
                        p_vaddr = struct.unpack_from('<Q', data, ph_off + 0x10)[0]
                        p_filesz = struct.unpack_from('<Q', data, ph_off + 0x20)[0]
                        if p_vaddr <= target < p_vaddr + p_filesz:
                            entry_file_off = p_offset + (target - p_vaddr)
                            break
                break

    if entry_file_off is None:
        print(f'{label}: Could not find 64-bit entry point')
        continue

    print(f'\\n===== {label} 64-bit code (file offset 0x{entry_file_off:x} to end) =====')

    # Extract 64-bit code to temp file
    tmpf = f'/tmp/code64_{label.lower().replace(\"-\",\"\")}.bin'
    code = data[entry_file_off:]
    open(tmpf, 'wb').write(code)

    # Disassemble
    result = subprocess.run(
        ['objdump', '-D', '-b', 'binary', '-m', 'i386:x86-64',
         '--adjust-vma=0x' + format(entry_file_off, 'x'), tmpf],
        capture_output=True, text=True
    )
    print(result.stdout)
" 2>&1 | head -500
