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

PIPE=/tmp/d2-p-$$; RAW=/tmp/d2-r-$$; ELF=/tmp/d2-$$.elf
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

python3 -c "
import struct, subprocess
data = open('$ELF','rb').read()
e_phoff = struct.unpack_from('<I', data, 0x1c)[0]
p_off = struct.unpack_from('<I', data, e_phoff+4)[0]
p_va = struct.unpack_from('<I', data, e_phoff+8)[0]
p_fsz = struct.unpack_from('<I', data, e_phoff+16)[0]

# Disassemble the whole LOAD segment
code = data[p_off:p_off+p_fsz]
tmpf = '/tmp/d2_all.bin'
open(tmpf,'wb').write(code)
r = subprocess.run(['objdump','-D','-b','binary','-m','i386:x86-64',f'--adjust-vma=0x{p_va:x}',tmpf],capture_output=True,text=True)

# Find all push rbp (function starts)
funcs = []
for line in r.stdout.split('\n'):
    if ':\t' in line and 'push' in line and '%rbp' in line:
        addr = int(line.strip().split(':')[0],16)
        funcs.append(addr)

# Find all CALL targets from __start
calls = []
for line in r.stdout.split('\n'):
    if ':\t' in line and '\tcall' in line:
        parts = line.strip().split()
        try:
            target = int(parts[-1], 16)
            calls.append((int(parts[0].rstrip(':'),16), target))
        except: pass

# Print last 3 functions (double, f, main)
print(f'Push-rbp functions: {len(funcs)}')
for fv in funcs[-3:]:
    foff = p_off + (fv - p_va)
    # Find next function
    nxt = p_va + p_fsz
    for f2 in funcs:
        if f2 > fv and f2 < nxt: nxt = f2
    fc = data[foff:p_off + (nxt - p_va)]
    tmpf2 = '/tmp/d2_f.bin'
    open(tmpf2,'wb').write(fc)
    r2 = subprocess.run(['objdump','-D','-b','binary','-m','i386:x86-64',f'--adjust-vma=0x{fv:x}',tmpf2],capture_output=True,text=True)
    print(f'\n=== 0x{fv:x} ({len(fc)} bytes) ===')
    for l in r2.stdout.split('\n'):
        if ':\t' in l: print(f'  {l.strip()}')
"
rm -f "$ELF"
