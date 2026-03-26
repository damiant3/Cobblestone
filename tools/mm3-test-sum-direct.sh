#!/bin/bash
# Test the sum type kernel directly - no serial input needed,
# the program is baked into the kernel
KERNEL="/mnt/d/Projects/NewRepository-cam/tools/mm3-minimal-sum.elf"
timeout 10 qemu-system-x86_64 \
  -kernel "$KERNEL" \
  -serial stdio \
  -display none \
  -no-reboot \
  -m 512 \
  2>/dev/null
echo "exit: $?"
