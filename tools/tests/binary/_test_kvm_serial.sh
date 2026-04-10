#!/bin/bash
# Test: is the KVM issue related to serial TX-ready checks?
# The self-hosted code checks TX ready before each char; the reference doesn't for READY.
# If we modify the self-hosted ELF to skip the TX checks (nop them out), does it boot?

python3 << 'PYEOF'
import struct, subprocess

BM = '/tmp/bm-small-v2.elf'
data = bytearray(open(BM, 'rb').read())
print(f"Original self-hosted ELF: {len(data)} bytes")

# The serial TX-ready check pattern is:
# mov $0x3fd,%rdx    (48 C7 C2 FD 03 00 00)
# in (%dx),%al       (EC)
# test $0x20,%al     (A8 20)
# jne +offset        (75 xx) or (0F 85 xx xx xx xx)
# jmp back           (EB xx) or (E9 xx xx xx xx)

# Find the FIRST occurrence (before READY printing) and the pattern
# Actually, let me try a different approach: just boot with KVM and add more debug
# Let me check if the issue is that the self-hosted ELF simply crashes before reaching STI

# Approach: create a tiny test ELF that just does the trampoline + cli + serial init + print 'A'
# but using the EXACT page table code from the self-hosted ELF

# Actually, simpler: let me just check the self-hosted with -d cpu (no KVM) to see
# where it's stuck vs where the reference is

# Let me try: boot the self-hosted WITHOUT any serial TX checks
# by NOPing out the in/test/jne/jmp sequences before the first out

# Find all the TX-ready check patterns
i = 0
tx_checks = []
while i < len(data) - 20:
    # Look for: mov $0x3fd,%rdx (48 C7 C2 FD 03 00 00)
    if (data[i:i+7] == bytes([0x48, 0xC7, 0xC2, 0xFD, 0x03, 0x00, 0x00]) and
        data[i+7] == 0xEC and  # in (%dx),%al
        data[i+8] == 0xA8 and data[i+9] == 0x20):  # test $0x20,%al
        tx_checks.append(i)
    i += 1

print(f"Found {len(tx_checks)} TX-ready check patterns")

# Create a patched version: NOP out the first 10 TX checks (before READY prints)
patched = bytes(data)  # keep original

# Instead, try: just NOP out ALL tx-ready checks
patched = bytearray(data)
for pos in tx_checks:
    # NOP out the 7 bytes of mov, 1 byte of in, 2 bytes of test = 10 bytes
    # Then the jne and jmp
    # mov rdx, 0x3fd: 7 bytes
    # in: 1 byte
    # test: 2 bytes
    # jne: 2 bytes (short) or 6 bytes (long)
    # jmp: 2 bytes (short) or 5 bytes (long)
    # Total: ~14 bytes

    # NOP the in/test/jne/jmp but KEEP the mov rdx (might be needed for the out)
    # Actually, NOP the whole check: mov rdx to 0x3fd + in + test + jne + jmp
    # Then the out instruction after it will just run

    # NOP from mov $0x3fd through the jmp back
    j = pos + 7  # after mov rdx
    # in: 1 byte
    j += 1  # skip in
    # test $0x20,%al: 2 bytes
    j += 2
    # jne: 75 xx (2 bytes) or 0F 85 (6 bytes)
    if patched[j] == 0x75:
        j += 2
    elif patched[j] == 0x0F and patched[j+1] == 0x85:
        j += 6
    # jmp: EB xx (2 bytes) or E9 (5 bytes)
    if patched[j] == 0xEB:
        j += 2
    elif patched[j] == 0xE9:
        j += 5

    # NOP from pos+7 (after mov rdx) to j (exclusive)
    for k in range(pos + 7, j):
        patched[k] = 0x90  # NOP

patched_path = '/tmp/bm-small-noserial.elf'
open(patched_path, 'wb').write(patched)
print(f"Patched ELF (TX checks NOPed): {len(patched)} bytes")

# Test with KVM
import os
os.system(f'timeout 5 qemu-system-x86_64 -enable-kvm -kernel {patched_path} -serial stdio -display none -no-reboot -m 512 > /tmp/kvm-noserial.txt 2>/dev/null < /dev/null')
size = os.path.getsize('/tmp/kvm-noserial.txt')
content = open('/tmp/kvm-noserial.txt', 'rb').read()[:100]
print(f"\nKVM boot (no TX checks): {size} bytes")
print(f"  Content: {content}")

# Also test original with TCG (control)
os.system(f'timeout 5 qemu-system-x86_64 -kernel {BM} -serial stdio -display none -no-reboot -m 512 > /tmp/tcg-original.txt 2>/dev/null < /dev/null')
size2 = os.path.getsize('/tmp/tcg-original.txt')
content2 = open('/tmp/tcg-original.txt', 'rb').read()[:100]
print(f"\nTCG boot (original): {size2} bytes")
print(f"  Content: {content2}")

PYEOF
