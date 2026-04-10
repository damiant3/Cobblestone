#!/bin/bash
# Test: does the self-hosted backend emit bodies for effectful functions?
REPO=/mnt/d/Projects/NewRepository-cam
BM_ELF="$REPO/build-output/bare-metal/Codex.Codex.elf"

compile_and_check() {
    local LABEL="$1"
    local SOURCE="$2"
    local PIPE=/tmp/ef-pipe-$$
    local RAW=/tmp/ef-raw-$$
    local ELF=/tmp/ef-elf-$$.elf
    rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
    sleep 999 > "$PIPE" & local HOLDER=$!
    timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$BM_ELF" \
        -serial stdio -display none -no-reboot -m 512 \
        < "$PIPE" > "$RAW" 2>/dev/null & local QEMU=$!
    for i in $(seq 1 40); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
    printf "BINARY\n%s\x04" "$SOURCE" > "$PIPE" &
    sleep 15
    kill $QEMU $HOLDER 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"
    if ! grep -qa 'SIZE:' "$RAW"; then
        echo "  $LABEL: FAIL (no SIZE marker)"
        rm -f "$RAW"; return
    fi
    python3 -c "
data = open('$RAW','rb').read()
idx = data.find(b'SIZE:')
nl = data.index(b'\x0a', idx)
size = int(data[idx+5:nl])
open('$ELF','wb').write(data[nl+1:nl+1+size])
print(f'  $LABEL: {size} bytes ELF')
"
    rm -f "$RAW"
    # Find main function and show first 20 instructions
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
entry_off = p_off + (entry_va - p_va)
code = data[entry_off:p_off+p_fsz]
tmpf = '/tmp/ef_start.bin'
open(tmpf,'wb').write(code)
r = subprocess.run(['objdump','-D','-b','binary','-m','i386:x86-64',f'--adjust-vma=0x{entry_va:x}',tmpf],capture_output=True,text=True)
# Find the CALL to main
for line in r.stdout.split('\n'):
    if ':\t' in line and '\tcall' in line:
        parts = line.strip().split()
        target = int(parts[-1], 16)
        toff = p_off + (target - p_va)
        tc = data[toff:toff+80]
        open('/tmp/ef_main.bin','wb').write(tc)
        r2 = subprocess.run(['objdump','-D','-b','binary','-m','i386:x86-64',f'--adjust-vma=0x{target:x}','/tmp/ef_main.bin'],capture_output=True,text=True)
        print(f'  main @ 0x{target:x}:')
        for l in r2.stdout.split('\n'):
            if ':\t' in l: print(f'    {l.strip()}')
        break
"
    rm -f "$ELF"
}

echo "=== Test 1: Pure function (no effects) ==="
compile_and_check "pure" "Chapter: T1

  main : Integer
  main = 42"

echo ""
echo "=== Test 2: Effectful function (Console) ==="
compile_and_check "effectful" 'Chapter: T2

  main : [Console] Nothing
  main = do
   print-line "hello"'

echo ""
echo "=== Test 3: Effectful with read-line ==="
compile_and_check "readline" 'Chapter: T3

  main : [Console] Nothing
  main = do
   x <- read-line
   print-line x'
