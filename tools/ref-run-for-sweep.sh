#!/bin/bash
# Helper invoked by ref-sweep.sh from the WSL side. Boots $1 (an ELF path,
# resolved from the git-bash cwd — which is the same path WSL sees when
# launched via `wsl bash tools/ref-run-for-sweep.sh ...`), captures serial
# output between READY and HEAP, writes to $2.
set -u
elf=$1
outfile=$2
raw=/tmp/ref-sweep-raw-$$
pipe=/tmp/ref-sweep-pipe-$$
rm -f "$raw" "$pipe"
mkfifo "$pipe"
sleep 30 > "$pipe" &
holder=$!
timeout 10 /usr/bin/qemu-system-x86_64 -enable-kvm -kernel "$elf" -serial stdio \
    -device isa-debug-exit,iobase=0xf4,iosize=0x04 \
    -display none -no-reboot -m 1024 < "$pipe" > "$raw" 2>/dev/null &
qemu=$!
for _ in $(seq 1 60); do
    grep -qa 'HEAP' "$raw" 2>/dev/null && break
    sleep 0.2
    kill -0 $qemu 2>/dev/null || break
done
sleep 1
kill $qemu $holder 2>/dev/null || true
wait 2>/dev/null || true
awk 'found && /^HEAP:/ { exit } found { print } /^READY/ { found=1 }' "$raw" > "$outfile"
rm -f "$raw" "$pipe"
