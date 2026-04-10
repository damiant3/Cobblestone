#!/bin/bash
# Debug: compile print-line "hi" and check the generated code + rodata
REPO=/mnt/d/Projects/NewRepository-cam
STAGE0="$REPO/build-output/bare-metal/Codex.Codex.elf"
PIPE=/tmp/pd-pipe-$$; RAW=/tmp/pd-raw-$$; ELF=/tmp/pd-test.elf
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & HOLDER=$!
timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & QEMU=$!
for i in $(seq 1 40); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
printf 'BINARY\nChapter: T\n\n  main : [Console] Nothing\n  main = do\n   print-line "hi"\n\x04' > "$PIPE" &
sleep 15
kill $QEMU $HOLDER 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"
python3 -c "
data=open('$RAW','rb').read();idx=data.find(b'SIZE:');nl=data.index(b'\x0a',idx);size=int(data[idx+5:nl])
open('$ELF','wb').write(data[nl+1:nl+1+size]);print(f'ELF: {size} bytes')
"
rm -f "$RAW"

python3 << 'PYEOF'
import struct, subprocess

data = open('/tmp/pd-test.elf', 'rb').read()

# Parse ELF
e_phoff = struct.unpack_from('<I', data, 0x1c)[0]
p_off = struct.unpack_from('<I', data, e_phoff+4)[0]
p_va = struct.unpack_from('<I', data, e_phoff+8)[0]
p_fsz = struct.unpack_from('<I', data, e_phoff+16)[0]
p_msz = struct.unpack_from('<I', data, e_phoff+20)[0]

text_end_va = p_va + p_fsz
rodata_va = text_end_va  # rodata follows text
print(f"Text: vaddr=0x{p_va:x} size=0x{p_fsz:x}")
print(f"MemSiz: 0x{p_msz:x}")
print(f"Rodata vaddr (text_end): 0x{rodata_va:x}")

# Check CCE→Unicode table at rodata + 16
# The table should be 256 bytes mapping CCE code → Unicode code
table_va = rodata_va + 16
table_off = p_off + p_fsz + 16  # file offset
if table_off + 256 <= len(data):
    table = data[table_off:table_off+256]
    print(f"\nCCE→Unicode table at vaddr 0x{table_va:x} (file offset 0x{table_off:x}):")
    # Show first 32 entries
    for i in range(0, min(128, len(table)), 16):
        row = ' '.join(f'{table[j]:02x}' for j in range(i, min(i+16, len(table))))
        ascii_repr = ''.join(chr(table[j]) if 32 <= table[j] < 127 else '.' for j in range(i, min(i+16, len(table))))
        print(f"  [{i:3d}] {row}  {ascii_repr}")

    # Check: what does CCE 'h' map to? 'i'?
    # In CCE, 'h' might have a different code than ASCII 104
    # Let's check what codes 'h' and 'i' have
    for ch in ['h', 'i']:
        ascii_val = ord(ch)
        # Find the CCE code for this character by checking the Unicode→CCE table
        u2c_off = p_off + p_fsz + 16 + 256  # after CCE→Unicode table
        if u2c_off + 256 <= len(data):
            u2c_table = data[u2c_off:u2c_off+256]
            cce_code = u2c_table[ascii_val]
            unicode_back = table[cce_code]
            print(f"\n  '{ch}' (ASCII {ascii_val}): CCE code={cce_code}, back to Unicode={unicode_back} ('{chr(unicode_back) if 32<=unicode_back<127 else '?'}')")
else:
    print(f"\n  ERROR: rodata region not in file (file size {len(data)}, need offset {table_off+256})")
    print(f"  File ends at offset {len(data)}, LOAD ends at file offset {p_off+p_fsz}")

# Find the "hi" string in the ELF
# It should be a length-prefixed string on the heap, but for string literals
# it might be in rodata or in the code as immediate values
# Actually, string literals are built at runtime by appending characters
# Let's just look at main's code
for i in range(0x90, 0x200):
    if data[i] == 0xEA and data[i+5] == 0x08:
        entry_va = struct.unpack_from('<I', data, i+1)[0]
        break
entry_off = p_off + (entry_va - p_va)
code = data[entry_off:p_off+p_fsz]
tmpf = '/tmp/pd_start.bin'
open(tmpf, 'wb').write(code)
r = subprocess.run(['objdump','-D','-b','binary','-m','i386:x86-64',f'--adjust-vma=0x{entry_va:x}',tmpf],capture_output=True,text=True)
# Find the call to main
for line in r.stdout.split('\n'):
    if ':\t' in line and '\tcall' in line:
        target = int(line.strip().split()[-1], 16)
        toff = p_off + (target - p_va)
        tc = data[toff:toff+200]
        open('/tmp/pd_main.bin','wb').write(tc)
        r2 = subprocess.run(['objdump','-D','-b','binary','-m','i386:x86-64',f'--adjust-vma=0x{target:x}','/tmp/pd_main.bin'],capture_output=True,text=True)
        print(f"\nmain @ 0x{target:x}:")
        for l in r2.stdout.split('\n')[:30]:
            if ':\t' in l: print(f"  {l.strip()}")
        break

PYEOF
rm -f /tmp/pd-test.elf
