#!/bin/bash
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf
SOURCE=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/source.codex
STAGE1=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/stage1.elf

PIPE=/tmp/cm-p-$$; RAW=/tmp/cm-r-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & H=$!
timeout 300 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & Q=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
(printf 'BINARY\n'; cat "$SOURCE"; printf '\x04') > "$PIPE" &
while true; do sleep 1; grep -qa 'SIZE:' "$RAW" && break; kill -0 $Q 2>/dev/null || break; done
SZ=$(grep -a 'SIZE:' "$RAW" | head -1 | sed 's/.*SIZE://' | tr -dc '0-9')
SOFF=$(grep -boa 'SIZE:' "$RAW" | head -1 | cut -d: -f1)
NEEDED=$((SOFF + 5 + ${#SZ} + 1 + SZ))
while [ "$(wc -c < "$RAW")" -lt "$NEEDED" ]; do kill -0 $Q 2>/dev/null || break; sleep 1; done
kill $Q $H 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"
BSTART=$((SOFF + 5 + ${#SZ} + 1))
dd if="$RAW" bs=1 skip="$BSTART" count="$SZ" of="$STAGE1" 2>/dev/null
echo "Stage 1: $(wc -c < "$STAGE1") bytes"
rm -f "$RAW"

# Check main function
python3 -c "
import struct, subprocess
data = open('$STAGE1','rb').read()
for i in range(0x90, 0x200):
    if data[i] == 0xEA and data[i+5] == 0x08:
        entry_va = struct.unpack_from('<I', data, i+1)[0]
        break
e_phoff = struct.unpack_from('<I', data, 0x1c)[0]
p_off = struct.unpack_from('<I', data, e_phoff+4)[0]
p_va = struct.unpack_from('<I', data, e_phoff+8)[0]
p_fsz = struct.unpack_from('<I', data, e_phoff+16)[0]
entry_off = p_off + (entry_va - p_va)
code = data[entry_off:p_off+p_fsz]
tmpf = '/tmp/cm_start.bin'
open(tmpf,'wb').write(code)
r = subprocess.run(['objdump','-D','-b','binary','-m','i386:x86-64',f'--adjust-vma=0x{entry_va:x}',tmpf],capture_output=True,text=True)
for line in r.stdout.split('\n'):
    if ':\t' in line and '\tcall' in line:
        target = int(line.strip().split()[-1],16)
        toff = p_off + (target - p_va)
        tc = data[toff:toff+80]
        open('/tmp/cm_main.bin','wb').write(tc)
        r2 = subprocess.run(['objdump','-D','-b','binary','-m','i386:x86-64',f'--adjust-vma=0x{target:x}','/tmp/cm_main.bin'],capture_output=True,text=True)
        print(f'main @ 0x{target:x}:')
        for l in r2.stdout.split('\n')[:20]:
            if ':\t' in l: print(f'  {l.strip()}')
        break
"
