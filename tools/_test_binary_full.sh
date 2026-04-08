#!/bin/bash
set -u
ELF=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf
SOURCE=${1:-/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/source.codex}
OUTELF=/tmp/binary-stage1.elf
QEMU=/usr/bin/qemu-system-x86_64
RAW=/tmp/binary-full-raw
PIPE=/tmp/binary-full-pipe
PARSE=/tmp/binary-full-parse
rm -f "$PIPE" "$RAW" "$PARSE" "$OUTELF"
mkfifo "$PIPE"
sleep 999 > "$PIPE" &
HOLDER=$!
echo "Starting QEMU..."
timeout 360 "$QEMU" -enable-kvm -kernel "$ELF" -serial stdio -display none -no-reboot -m 512 < "$PIPE" > "$RAW" 2>/dev/null &
PID=$!
for i in $(seq 1 50); do
  grep -qa READY "$RAW" 2>/dev/null && break
  sleep 0.2
done
echo "Sending BINARY + source ($(wc -c < "$SOURCE") bytes)..."
(printf 'BINARY\n'; cat "$SOURCE"; printf '\x04') > "$PIPE" &
PREV=0
STABLE=0
while true; do
    sleep 5
    CUR=$(wc -c < "$RAW" 2>/dev/null || echo 0)
    echo "  output=$CUR bytes"
    if [ "$CUR" -gt 100 ] && [ "$CUR" -eq "$PREV" ]; then
        STABLE=$((STABLE + 1))
        [ "$STABLE" -ge 2 ] && break
    else
        STABLE=0
    fi
    PREV=$CUR
    kill -0 $PID 2>/dev/null || { echo "  QEMU exited"; break; }
done
echo "Killing QEMU..."
kill $PID 2>/dev/null || true
kill $HOLDER 2>/dev/null || true
wait 2>/dev/null || true
rm -f "$PIPE"
echo "Parsing output..."
python3 -c "
import sys
data = open('$RAW', 'rb').read()
print(f'Total raw: {len(data)} bytes')
idx = data.find(b'SIZE:')
if idx < 0:
    print('SIZE: marker not found')
    print('First 200 bytes:', repr(data[:200]))
    sys.exit(1)
nl = data.index(b'\x0a', idx)
size = int(data[idx+5:nl])
binary_start = nl + 1
binary = data[binary_start:binary_start+size]
print(f'SIZE:{size}, got {len(binary)} bytes')
if len(binary) != size:
    print('INCOMPLETE')
    sys.exit(1)
open('$OUTELF', 'wb').write(binary)
print(f'Wrote $OUTELF')
rest = data[binary_start+size:]
for line in rest.split(b'\x0a'):
    line = line.strip()
    if line: print(line.decode('ascii', errors='replace'))
" 2>&1
echo "Parse exit: $?"
rm -f "$RAW" "$PARSE"
