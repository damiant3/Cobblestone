#!/bin/bash
ELF="/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf"
SOURCE="/mnt/d/Projects/NewRepository-cam/samples/hello.codex"
RAW=/tmp/hello-raw
PIPE=/tmp/hello-pipe
rm -f $RAW $PIPE /tmp/hello-sh.elf; mkfifo $PIPE
sleep 999 > $PIPE &
HOLDER=$!
timeout 120 /usr/bin/qemu-system-x86_64 -enable-kvm -kernel $ELF -serial stdio -display none -no-reboot -m 1024 < $PIPE > $RAW 2>/dev/null &
QEMU=$!
for i in $(seq 1 100); do grep -qa READY $RAW && break; sleep 0.1; done
(printf 'BINARY\n'; cat $SOURCE; printf '\x04') > $PIPE &
# wait for SIZE + N bytes + HEAP
target_size=0
for i in $(seq 1 120); do
    sleep 1
    size_line=$(grep -oaP 'SIZE:[0-9]+' $RAW | head -1)
    if [ -n "$size_line" ]; then
        target=${size_line#SIZE:}
        have=$(wc -c < $RAW)
        if [ "$have" -gt "$((target + 200))" ]; then break; fi
    fi
done
kill $QEMU $HOLDER 2>/dev/null
wait 2>/dev/null
SIZE_LINE=$(grep -oaP 'SIZE:[0-9]+' $RAW | head -1)
SIZE=${SIZE_LINE#SIZE:}
SIZE_OFFSET=$(grep -boa 'SIZE:' $RAW | head -1 | cut -d: -f1)
AFTER=$((SIZE_OFFSET + ${#SIZE_LINE} + 1))
dd if=$RAW bs=1 skip=$AFTER count=$SIZE of=/tmp/hello-sh.elf 2>/dev/null
echo "Claimed SIZE: $SIZE  Extracted: $(wc -c < /tmp/hello-sh.elf)"
echo "First 64 bytes of extracted:"
head -c 64 /tmp/hello-sh.elf | od -An -tx1
echo "Ref compare:"
head -c 64 /mnt/d/Projects/NewRepository-cam/samples/hello.elf | od -An -tx1
