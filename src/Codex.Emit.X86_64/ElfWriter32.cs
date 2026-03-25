namespace Codex.Emit.X86_64;

/// <summary>
/// Minimal 32-bit ELF writer for multiboot kernels.
/// QEMU's multiboot loader requires a 32-bit ELF (ELFCLASS32).
/// The binary contains 32-bit trampoline code that switches to 64-bit long mode.
/// </summary>
static class ElfWriter32
{
    const uint LoadAddress = 0x100000; // 1MB — standard multiboot load address

    public static byte[] WriteExecutable(byte[] text, byte[] rodata, uint entryOffset)
    {
        // ELF32 header: 52 bytes
        // Program header: 32 bytes each, 2 entries (text + rodata)
        const int ElfHeaderSize = 52;
        const int PhdrSize = 32;
        const int PhdrCount = 2;
        int headerTotal = ElfHeaderSize + PhdrSize * PhdrCount;

        // Align text start to 16 bytes
        int textStart = (headerTotal + 15) & ~15;
        int textEnd = textStart + text.Length;
        int rodataStart = (textEnd + 15) & ~15;
        int rodataEnd = rodataStart + rodata.Length;
        int fileSize = rodataEnd;

        byte[] elf = new byte[fileSize];

        // ── ELF header ──
        // e_ident
        elf[0] = 0x7F; elf[1] = (byte)'E'; elf[2] = (byte)'L'; elf[3] = (byte)'F';
        elf[4] = 1;     // ELFCLASS32
        elf[5] = 1;     // ELFDATA2LSB
        elf[6] = 1;     // EV_CURRENT
        // e_type = ET_EXEC (2)
        Write16(elf, 16, 2);
        // e_machine = EM_386 (3)
        Write16(elf, 18, 3);
        // e_version = 1
        Write32(elf, 20, 1);
        // e_entry = load address + entry offset
        Write32(elf, 24, LoadAddress + (uint)textStart - (uint)headerTotal + entryOffset);
        // Wait — entryOffset is relative to m_text[0] which starts at textStart in the file.
        // The entry point vaddr = LoadAddress + (textStart - headerTotal is wrong)
        // Actually: the LOAD segment maps file offset textStart to vaddr LoadAddress + textStart.
        // But we want the entire file loaded at LoadAddress. Simpler: single LOAD segment.
        // Let me simplify: one LOAD segment for the whole file at LoadAddress.

        // Re-think: single segment, entire file loaded at LoadAddress.
        // Entry = LoadAddress + textStart + entryOffset? No — entry relative to load.
        // If we load the entire file at LoadAddress, entry = LoadAddress + offset_in_file.
        // The multiboot header is at file offset textStart (start of .text).
        // The trampoline starts at textStart + 12 (after 12-byte multiboot header).
        // So entry = LoadAddress + textStart + 12... but we want entry in the code.

        // Actually, simplest: single LOAD at file offset 0, vaddr LoadAddress, memsz = fileSize.
        // Then entry = LoadAddress + textStart + entryOffset.

        // Redo with single LOAD segment:
        Write32(elf, 24, LoadAddress + (uint)textStart + entryOffset);
        // e_phoff = 52 (right after header)
        Write32(elf, 28, (uint)ElfHeaderSize);
        // e_shoff = 0 (no section headers)
        Write32(elf, 32, 0);
        // e_flags = 0
        Write32(elf, 36, 0);
        // e_ehsize = 52
        Write16(elf, 40, (ushort)ElfHeaderSize);
        // e_phentsize = 32
        Write16(elf, 42, (ushort)PhdrSize);
        // e_phnum = 1 (single LOAD)
        Write16(elf, 44, 1);
        // e_shentsize = 0
        Write16(elf, 46, 0);
        // e_shnum = 0
        Write16(elf, 48, 0);
        // e_shstrndx = 0
        Write16(elf, 50, 0);

        // ── Program header: single LOAD segment ──
        int ph = ElfHeaderSize;
        // p_type = PT_LOAD (1)
        Write32(elf, ph, 1);
        // p_offset = 0 (load entire file)
        Write32(elf, ph + 4, 0);
        // p_vaddr = LoadAddress
        Write32(elf, ph + 8, LoadAddress);
        // p_paddr = LoadAddress
        Write32(elf, ph + 12, LoadAddress);
        // p_filesz = fileSize
        Write32(elf, ph + 16, (uint)fileSize);
        // p_memsz = fileSize + extra (for BSS)
        Write32(elf, ph + 20, (uint)fileSize + 0x100000); // 1MB extra for heap/stack
        // p_flags = PF_X | PF_W | PF_R (7)
        Write32(elf, ph + 24, 7);
        // p_align = 0x1000
        Write32(elf, ph + 28, 0x1000);

        // ── Copy text and rodata ──
        Array.Copy(text, 0, elf, textStart, text.Length);
        Array.Copy(rodata, 0, elf, rodataStart, rodata.Length);

        return elf;
    }

    static void Write16(byte[] buf, int offset, ushort value)
    {
        buf[offset] = (byte)(value & 0xFF);
        buf[offset + 1] = (byte)((value >> 8) & 0xFF);
    }

    static void Write32(byte[] buf, int offset, uint value)
    {
        buf[offset] = (byte)(value & 0xFF);
        buf[offset + 1] = (byte)((value >> 8) & 0xFF);
        buf[offset + 2] = (byte)((value >> 16) & 0xFF);
        buf[offset + 3] = (byte)((value >> 24) & 0xFF);
    }
}
