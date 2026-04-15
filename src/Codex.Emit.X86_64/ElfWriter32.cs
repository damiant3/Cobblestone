namespace Codex.Emit.X86_64;

/// <summary>
/// 32-bit ELF writer for bare-metal kernels.
/// Includes PVH ELF note for QEMU direct boot and multiboot header in .text.
/// QEMU enters in 32-bit protected mode at the PVH entry address.
/// </summary>
static class ElfWriter32
{
    const uint LoadAddress = 0x100000;
    const int ElfHeaderSize = 52;
    const int PhdrSize = 32;

    public static byte[] WriteExecutable(byte[] text, byte[] rodata, uint entryOffset)
    {
        // Layout: [ELF hdr 52B][PHDR 32B x2][PVH note 20B][pad][.text][pad][.rodata]
        // PHDR 0: PT_LOAD for .text+.rodata
        // PHDR 1: PT_NOTE for PVH
        int phdrCount = 2;
        int headersEnd = ElfHeaderSize + PhdrSize * phdrCount;

        // PVH note: name="Xen\0"(4), descsz=4, type=18, desc=entry32
        byte[] note = new byte[20];
        note[0] = 4;  // namesz
        note[4] = 4;  // descsz
        note[8] = 18; // XEN_ELFNOTE_PHYS32_ENTRY
        note[12] = (byte)'X'; note[13] = (byte)'e'; note[14] = (byte)'n';
        uint pvhAddr = LoadAddress + entryOffset;
        note[16] = (byte)(pvhAddr & 0xFF);
        note[17] = (byte)((pvhAddr >> 8) & 0xFF);
        note[18] = (byte)((pvhAddr >> 16) & 0xFF);
        note[19] = (byte)((pvhAddr >> 24) & 0xFF);

        int noteOffset = Align(headersEnd, 4);
        int textStart = Align(noteOffset + note.Length, 16);
        int textEnd = textStart + text.Length;
        int rodataStart = Align(textEnd, 8);
        int fileSize = rodataStart + rodata.Length;

        byte[] elf = new byte[fileSize];

        // ── ELF header ──
        elf[0] = 0x7F; elf[1] = (byte)'E'; elf[2] = (byte)'L'; elf[3] = (byte)'F';
        elf[4] = 1;  // ELFCLASS32
        elf[5] = 1;  // ELFDATA2LSB
        elf[6] = 1;  // EV_CURRENT

        Write16(elf, 16, 2);    // ET_EXEC
        Write16(elf, 18, 3);    // EM_386
        Write32(elf, 20, 1);    // e_version
        Write32(elf, 24, LoadAddress + entryOffset); // e_entry
        Write32(elf, 28, (uint)ElfHeaderSize);       // e_phoff
        Write32(elf, 32, 0);    // e_shoff
        Write32(elf, 36, 0);    // e_flags
        Write16(elf, 40, (ushort)ElfHeaderSize);
        Write16(elf, 42, (ushort)PhdrSize);
        Write16(elf, 44, (ushort)phdrCount);

        // ── PHDR 0: PT_LOAD — maps .text at LoadAddress ──
        int ph0 = ElfHeaderSize;
        Write32(elf, ph0, 1);          // PT_LOAD
        Write32(elf, ph0 + 4, (uint)textStart);   // p_offset
        Write32(elf, ph0 + 8, LoadAddress);        // p_vaddr
        Write32(elf, ph0 + 12, LoadAddress);       // p_paddr
        Write32(elf, ph0 + 16, (uint)(fileSize - textStart)); // p_filesz
        Write32(elf, ph0 + 20, (uint)(fileSize - textStart) + 0x3FC00000); // p_memsz (~1 GB heap, upper bound of current 1 GB identity page map)
        Write32(elf, ph0 + 24, 7);    // RWX
        Write32(elf, ph0 + 28, 0x1000);

        // ── PHDR 1: PT_NOTE — PVH entry ──
        int ph1 = ElfHeaderSize + PhdrSize;
        Write32(elf, ph1, 4);          // PT_NOTE
        Write32(elf, ph1 + 4, (uint)noteOffset);  // p_offset
        Write32(elf, ph1 + 8, 0);     // p_vaddr (unused)
        Write32(elf, ph1 + 12, 0);    // p_paddr
        Write32(elf, ph1 + 16, (uint)note.Length); // p_filesz
        Write32(elf, ph1 + 20, (uint)note.Length); // p_memsz
        Write32(elf, ph1 + 24, 4);    // PF_R
        Write32(elf, ph1 + 28, 4);    // align

        // ── Note data ──
        Array.Copy(note, 0, elf, noteOffset, note.Length);

        // ── .text ──
        Array.Copy(text, 0, elf, textStart, text.Length);

        // ── .rodata ──
        if (rodata.Length > 0)
        {
            Array.Copy(rodata, 0, elf, rodataStart, rodata.Length);
        }

        return elf;
    }

    static int Align(int value, int alignment)
    {
        int r = value % alignment;
        return r == 0 ? value : value + (alignment - r);
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
