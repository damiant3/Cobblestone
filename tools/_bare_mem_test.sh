#!/bin/bash
KERNEL=/mnt/d/Projects/NewRepository-cam/Codex.Codex/out/Codex.Codex.elf
SOURCE=/mnt/d/Projects/NewRepository-cam/tools/_all-source.codex
for MEM in 512 256 128 64; do
  BYTES=$(echo "$SOURCE" | timeout 120 qemu-system-x86_64 -kernel "$KERNEL" -serial stdio -display none -no-reboot -m $MEM 2>/dev/null | wc -c)
  echo "  -m $MEM: $BYTES bytes"
done
