#!/bin/bash
ELF="/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf"
SOURCE="/mnt/d/Projects/NewRepository-cam/samples/hello.codex"
RAW=/tmp/hello-raw
PIPE=/tmp/hello-pipe
rm -f $RAW $PIPE; mkfifo $PIPE
sleep 999 > $PIPE &
HOLDER=$!
timeout 60 /usr/bin/qemu-system-x86_64 -enable-kvm -kernel $ELF -serial stdio -display none -no-reboot -m 1024 < $PIPE > $RAW 2>/dev/null &
QEMU=$!
for i in $(seq 1 100); do grep -qa READY $RAW && break; sleep 0.1; done
(printf 'BINARY\n'; cat $SOURCE; printf '\x04') > $PIPE &
sleep 25
kill $QEMU $HOLDER 2>/dev/null
wait 2>/dev/null
echo "=== raw size: $(wc -c < $RAW) ==="
echo "=== SIZE marker ==="
grep -oaP 'SIZE:[0-9]+' $RAW | head -1
SIZE_OFFSET=$(grep -boa 'SIZE:' $RAW | head -1 | cut -d: -f1)
echo "SIZE: at byte offset $SIZE_OFFSET"
echo "=== first 32 bytes AFTER the SIZE:N\\n line ==="
SIZE_LINE=$(grep -oaP 'SIZE:[0-9]+' $RAW | head -1)
AFTER=$((SIZE_OFFSET + ${#SIZE_LINE} + 1))
dd if=$RAW bs=1 skip=$AFTER count=32 2>/dev/null | od -An -tx1
