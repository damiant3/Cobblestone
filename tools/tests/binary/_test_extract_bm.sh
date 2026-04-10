#!/bin/bash
REF_ELF=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf
BM_OUT=/tmp/bm-small.elf

PIPE=/tmp/ext-pipe-$$
RAW=/tmp/ext-raw-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & HOLDER=$!
timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$REF_ELF" \
    -serial stdio -display none -no-reboot -m 512 < "$PIPE" > "$RAW" 2>/dev/null & QEMU=$!
for i in $(seq 1 40); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
(printf 'BINARY\nChapter: SmallTest\n\n  main : Integer\n  main = 42\n\x04') > "$PIPE" &
sleep 15
kill $QEMU 2>/dev/null; kill $HOLDER 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"
python3 -c "
data = open('$RAW','rb').read()
idx = data.find(b'SIZE:')
if idx < 0: print('FAIL'); exit(1)
nl = data.index(b'\x0a', idx)
size = int(data[idx+5:nl])
elf = data[nl+1:nl+1+size]
open('$BM_OUT','wb').write(elf)
print(f'Saved {len(elf)} bytes to $BM_OUT')
"
rm -f "$RAW"
