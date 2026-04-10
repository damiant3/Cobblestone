#!/bin/bash
# Check what the trampoline's initial page tables map
# The trampoline is identical for both ELFs, so we only need to check one
python3 << 'PYEOF'
import struct

data = open('/mnt/d/Projects/NewRepository-cam/build-output/ref-small.elf', 'rb').read()

# Disassemble the trampoline's 32-bit code to find page table setup
# The trampoline sets up page tables at some address, then enables paging + long mode
# Look for writes to page table addresses in the trampoline

# The LOAD segment starts at file offset 0x90, vaddr 0x100000
# Entry point is 0x10000c (offset 0x9c in file)
# Trampoline runs in 32-bit real mode / protected mode

# Let's look at the raw trampoline bytes and find CR0/CR3/CR4 writes
# These are done with special MOV instructions in 32-bit mode

print("=== Trampoline analysis ===")
# The trampoline at file offset 0x90 is real-mode code
# It transitions: real mode -> protected mode -> enables paging -> long mode -> far jump to 64-bit

# In 32-bit mode, CR3 write is: 0F 22 D8 (mov cr3, eax)
# CR0 write: 0F 22 C0 (mov cr0, eax)
# CR4 write: 0F 22 E0 (mov cr4, eax)

tramp_start = 0x90
tramp_end = 0x134 + 7  # far jump at 0x133, 7 bytes long

for i in range(tramp_start, tramp_end - 2):
    if data[i] == 0x0F and data[i+1] == 0x22:
        reg = data[i+2]
        if reg == 0xD8:
            print(f"  mov CR3, eax at file offset 0x{i:x}")
            # Find the value loaded into eax before this
            # Look backwards for mov eax, imm32 (B8 xx xx xx xx)
            for j in range(i-1, max(tramp_start, i-20), -1):
                if data[j] == 0xB8:
                    val = struct.unpack_from('<I', data, j+1)[0]
                    print(f"    eax loaded with 0x{val:x} at offset 0x{j:x}")
                    print(f"    → Initial page table root at physical address 0x{val:x}")
                    break
        elif reg == 0xC0:
            print(f"  mov CR0, eax at file offset 0x{i:x}")
        elif reg == 0xE0:
            print(f"  mov CR4, eax at file offset 0x{i:x}")

# Now find the initial page table setup in the trampoline
# The trampoline writes page table entries before enabling paging
# Look for sequences of mov [addr], value in 32-bit mode

# What does the trampoline do with the page tables?
# It typically:
# 1. Clears page table area (rep stosb/stosd)
# 2. Sets PML4[0] -> PDPT
# 3. Sets PDPT[0] -> PD
# 4. Sets PD entries for identity mapping
# 5. Enables PAE in CR4
# 6. Sets CR3 to PML4
# 7. Enables long mode via MSR
# 8. Enables paging in CR0

# The initial page tables might only map a small area (just enough to reach the far jump)
# Then the 64-bit code sets up proper page tables and switches CR3

# Let's check: the initial PML4 is at what address? From above CR3 write.
# Then the 64-bit code sets CR3 to 0x8000 (the process page tables).
# If the initial page tables map enough for the 64-bit boot code to run
# until the CR3 switch, we're fine.

# Let's look at what physical addresses the 64-bit code accesses BEFORE CR3 switch:
# - RSP at 0x20000000 (stack)
# - Code at ~0x100000+ (the ELF text segment)
# - Data at various low addresses (0x5000, 0x7000, 0x8000, 0xa000, etc.)

# The initial trampoline page tables need to map:
# - 0x100000+ (code) — definitely needed
# - 0x20000000 (stack) — needed before CR3 switch
# But does the 64-bit code access the stack before CR3 switch?

# Let's check: the 64-bit code starts with CLI, sets RSP=0x20000000, then does setup.
# Does it PUSH or CALL before the CR3 switch?
# From the disassembly: the first stack access was at instruction 154 (sub rsp, lidt).

# The IDT load uses (rsp) as a temp:
# sub $0x10,%rsp
# mov ...,(%rsp)   <-- stack access!
# lidt (%rdi)      <-- or is it lidt (%rsp)?
# This is BEFORE CR3 switch!

# So the initial page tables need to map 0x20000000 (stack region).
# If they don't, it crashes even before reaching the CR3 switch.

# Let's verify what the initial trampoline page tables actually map.
# We need to find where the trampoline sets up the initial PD entries.

# From the CR3 value, find the PML4, follow the chain
print("\n=== Checking trampoline page tables in ELF data ===")
# Actually, the page tables are set up by the trampoline CODE, not pre-existing in the ELF data.
# The trampoline zeroes memory and writes entries before enabling paging.

# But the BIG QUESTION: do the trampoline's initial page tables map 0x20000000?
# The trampoline is shared between ref and self-hosted (IDENTICAL), and the ref works.
# So the trampoline's page tables DO work for the reference ELF.
# BUT — the reference sets RSP to 0x20000000 too... and it works.

# Wait, the trampoline is IDENTICAL but the 64-bit entry point is at different addresses.
# Ref: 0x101e8c, Self-hosted: 0x101693.
# The far jump target in the trampoline is PATCHED to point to the right address.

# Since the trampoline is identical and BOTH ELFs put the stack at 0x20000000,
# the initial page tables should work the same for both.
# The ref works, so the initial page tables DO map 0x20000000.

# Then WHY does the self-hosted ELF not produce any output?
# The trampoline is identical, the initial page tables are the same...
# Maybe the issue is in the 64-bit code BEFORE the CR3 switch?

# Let me check: does the 64-bit code access any address outside the initial mapping
# BEFORE the CR3 switch?

# Actually — let me check if the far jump target is actually correct.
# The trampoline's far jump: EA xx xx xx xx 08 00
# The target should be the vaddr of the 64-bit entry code.

for i in range(0x90, 0x200):
    if data[i] == 0xEA and i+6 < len(data) and data[i+5] == 0x08 and data[i+6] == 0x00:
        target = struct.unpack_from('<I', data, i+1)[0]
        print(f"  Far jump at file offset 0x{i:x}: target=0x{target:x}")
        # Check: is this address within the LOAD segment?
        # LOAD: vaddr=0x100000, filesz from header
        load_end_vaddr = 0x100000 + struct.unpack_from('<I', data, 0x90 - 0x90 + 0x34 + 0x10)[0]
        print(f"  (LOAD segment ends at vaddr 0x{load_end_vaddr:x})")
        if target >= 0x100000 and target < load_end_vaddr:
            print(f"  Target is within LOAD segment ✓")
        else:
            print(f"  Target is OUTSIDE LOAD segment ✗")
        break

bm_data2 = open('/tmp/bm-small-fixed.elf', 'rb').read()
for i in range(0x90, 0x200):
    if bm_data2[i] == 0xEA and i+6 < len(bm_data2) and bm_data2[i+5] == 0x08 and bm_data2[i+6] == 0x00:
        target = struct.unpack_from('<I', bm_data2, i+1)[0]
        ph_off2 = struct.unpack_from('<I', bm_data2, 0x1c)[0]
        p_filesz2 = struct.unpack_from('<I', bm_data2, ph_off2 + 0x10)[0]
        load_end = 0x100000 + p_filesz2
        print(f"\n  Self-hosted far jump target: 0x{target:x}")
        print(f"  (LOAD segment ends at vaddr 0x{load_end:x})")
        if target >= 0x100000 and target < load_end:
            print(f"  Target is within LOAD segment ✓")
        else:
            print(f"  Target is OUTSIDE LOAD segment ✗")
        break

# Check what the trampoline's initial page tables setup is
# by examining the trampoline code more carefully
print("\n=== Trampoline page table setup code ===")
# Disassemble trampoline in 16/32-bit mode to find page table operations
import subprocess
tramp_code = data[0x90:0x134+7]
tmpf = '/tmp/trampoline.bin'
open(tmpf, 'wb').write(tramp_code)

# 16-bit mode first few instructions, then switches to 32-bit
# Let's try 32-bit disassembly (after mode switch)
result = subprocess.run(
    ['objdump', '-D', '-b', 'binary', '-m', 'i386',
     f'--adjust-vma=0x100000', tmpf],
    capture_output=True, text=True
)
print(result.stdout[:3000])

PYEOF
