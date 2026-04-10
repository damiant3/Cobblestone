#!/bin/bash
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf
SRC='Chapter: T

Section: Funcs

  double : Integer -> Integer
  double (x) = x * 2

  f : Integer -> Integer
  f (c) =
    if c == 1 then let x = double c in x + 1
    else if c == 2 then let x = double c in x + 2
    else 0

Section: Main

  main : Integer
  main = f 3'

PIPE=/tmp/dl-p-$$; RAW=/tmp/dl-r-$$; ELF=/tmp/dl-$$.elf
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & H=$!
timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & Q=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
printf "BINARY\n%s\x04" "$SRC" > "$PIPE" &
while true; do sleep 1; grep -qa 'SIZE:' "$RAW" && break; kill -0 $Q 2>/dev/null || break; done
SZ=$(grep -a 'SIZE:' "$RAW" | head -1 | sed 's/.*SIZE://' | tr -dc '0-9')
SOFF=$(grep -boa 'SIZE:' "$RAW" | head -1 | cut -d: -f1)
BSTART=$((SOFF + 5 + ${#SZ} + 1))
NEEDED=$((BSTART + SZ))
while [ "$(wc -c < "$RAW")" -lt "$NEEDED" ]; do kill -0 $Q 2>/dev/null || break; sleep 1; done
kill $Q $H 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"
dd if="$RAW" bs=1 skip="$BSTART" count="$SZ" of="$ELF" 2>/dev/null
rm -f "$RAW"

# Disassemble f
python3 -c "
import struct, subprocess
data = open('$ELF','rb').read()
for i in range(0x90, 0x200):
    if data[i] == 0xEA and data[i+5] == 0x08:
        entry_va = struct.unpack_from('<I', data, i+1)[0]
        break
e_phoff = struct.unpack_from('<I', data, 0x1c)[0]
p_off = struct.unpack_from('<I', data, e_phoff+4)[0]
p_va = struct.unpack_from('<I', data, e_phoff+8)[0]
p_fsz = struct.unpack_from('<I', data, e_phoff+16)[0]

# Find all functions (push rbp patterns)
entry_off = p_off + (entry_va - p_va)
code = data[entry_off:p_off+p_fsz]
tmpf = '/tmp/dl_code.bin'
open(tmpf,'wb').write(code)
r = subprocess.run(['objdump','-D','-b','binary','-m','i386:x86-64',f'--adjust-vma=0x{entry_va:x}',tmpf],capture_output=True,text=True)

# Find the CALL in __start to get main
for line in r.stdout.split('\n'):
    if ':\t' in line and '\tcall' in line:
        main_target = int(line.strip().split()[-1],16)
        break

# Find all user functions before __start
funcs = []
for line in r.stdout.split('\n'):
    if ':\t' in line and '\tpush' in line and '%rbp' in line:
        addr = int(line.strip().split(':')[0],16)
        if addr < entry_va:
            funcs.append(addr)

print(f'Functions: {[hex(f) for f in funcs]}')
print(f'main calls: 0x{main_target:x}')

# Disassemble each function
for func_va in funcs:
    func_off = p_off + (func_va - p_va)
    # Find next function or entry
    next_va = entry_va
    for f2 in funcs:
        if f2 > func_va and f2 < next_va:
            next_va = f2
    func_code = data[func_off:p_off + (next_va - p_va)]
    tmpf2 = '/tmp/dl_func.bin'
    open(tmpf2,'wb').write(func_code)
    r2 = subprocess.run(['objdump','-D','-b','binary','-m','i386:x86-64',f'--adjust-vma=0x{func_va:x}',tmpf2],capture_output=True,text=True)
    print(f'\n=== Function at 0x{func_va:x} ===')
    for line in r2.stdout.split('\n')[:40]:
        if ':\t' in line:
            print(f'  {line.strip()}')
"
rm -f "$ELF"
