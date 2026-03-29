namespace Codex.Emit.X86_64;

sealed class ElfWriterX86_64
{
    const byte ELFCLASS64 = 2;
    const byte ELFDATA2LSB = 1;
    const ushort EM_X86_64 = 62;
    const ushort ET_EXEC = 2;
    const uint PT_LOAD = 1;
    const uint PF_X = 1;
    const uint PF_W = 2;
    const uint PF_R = 4;

    const ulong LinuxBaseAddress = 0x400000;
    const int ElfHeaderSize = 64;
    const int ProgramHeaderSize = 56;

    public static byte[] WriteExecutable(byte[] textSection, byte[] rodataSection, ulong entryOffset)
    {
        int phdrCount = 2;
        int headersTotalSize = ElfHeaderSize + ProgramHeaderSize * phdrCount;

        int textFileOffset = Align(headersTotalSize, 16);
        ulong textVaddr = LinuxBaseAddress + (ulong)textFileOffset;
        ulong entryPoint = textVaddr + entryOffset;

        int rodataFileOffset = Align(textFileOffset + textSection.Length, 4096);
        ulong rodataVaddr = LinuxBaseAddress + (ulong)rodataFileOffset;

        int totalSize = rodataFileOffset + rodataSection.Length;

        MemoryStream ms = new(totalSize);
        BinaryWriter w = new(ms);

        // ── ELF Header (64 bytes) ──────────────────────────────
        w.Write((byte)0x7F); w.Write((byte)'E'); w.Write((byte)'L'); w.Write((byte)'F');
        w.Write(ELFCLASS64);
        w.Write(ELFDATA2LSB);
        w.Write((byte)1);   // EV_CURRENT
        w.Write((byte)0);   // ELFOSABI_NONE
        w.Write(new byte[8]);

        w.Write(ET_EXEC);
        w.Write(EM_X86_64);
        w.Write((uint)1);   // EV_CURRENT
        w.Write(entryPoint);
        w.Write((ulong)ElfHeaderSize);
        w.Write((ulong)0);  // no section headers
        w.Write((uint)0);   // e_flags
        w.Write((ushort)ElfHeaderSize);
        w.Write((ushort)ProgramHeaderSize);
        w.Write((ushort)phdrCount);
        w.Write((ushort)0);
        w.Write((ushort)0);
        w.Write((ushort)0);

        // ── Program Header 0: .text (rwx) — includes ELF headers for page-aligned p_offset.
        // QEMU usermode requires p_offset to be page-aligned; native Linux is lenient.
        // Text code starts at file offset textFileOffset (= vaddr LinuxBaseAddress + textFileOffset).
        w.Write(PT_LOAD);
        w.Write(PF_R | PF_W | PF_X);
        w.Write((ulong)0);                // p_offset: page-aligned (includes ELF + PHDR headers)
        w.Write(LinuxBaseAddress);         // p_vaddr
        w.Write(LinuxBaseAddress);         // p_paddr
        w.Write((ulong)(textFileOffset + textSection.Length)); // p_filesz
        w.Write((ulong)(textFileOffset + textSection.Length)); // p_memsz
        w.Write((ulong)0x1000);

        // ── Program Header 1: .rodata (rw-) — writable for result_space_base global
        w.Write(PT_LOAD);
        w.Write(PF_R | PF_W);
        w.Write((ulong)rodataFileOffset);
        w.Write(rodataVaddr);
        w.Write(rodataVaddr);
        w.Write((ulong)rodataSection.Length);
        w.Write((ulong)rodataSection.Length);
        w.Write((ulong)0x1000);

        // ── Pad to text offset ─────────────────────────────────
        while (ms.Position < textFileOffset)
            w.Write((byte)0);

        w.Write(textSection);

        while (ms.Position < rodataFileOffset)
            w.Write((byte)0);

        w.Write(rodataSection);

        return ms.ToArray();
    }

    /// <summary>
    /// Write a bare-metal ELF with PVH note for QEMU direct boot.
    /// QEMU enters the 32-bit trampoline which sets up long mode.
    /// </summary>
    public static byte[] WriteBareMetal(byte[] textSection, byte[] rodataSection, uint pvhEntry32)
    {
        // Layout: ELF64 header + 3 PHDRs (text LOAD + rodata LOAD + PVH NOTE) + note + text + rodata
        const ulong bareMetalBase = 0x100000; // 1MB load address
        int phdrCount = 3;
        int headersTotalSize = ElfHeaderSize + ProgramHeaderSize * phdrCount;

        // PVH note: name="Xen\0" (4 bytes), desc=entry32 (4 bytes), type=18
        // Note header: namesz(4) + descsz(4) + type(4) + name(4, padded) + desc(4, padded)
        byte[] noteData = new byte[20];
        noteData[0] = 4; // namesz (including null)
        noteData[4] = 4; // descsz
        noteData[8] = 18; // type = XEN_ELFNOTE_PHYS32_ENTRY
        noteData[12] = (byte)'X'; noteData[13] = (byte)'e'; noteData[14] = (byte)'n'; noteData[15] = 0;
        uint pvhAddr = (uint)(bareMetalBase + pvhEntry32);
        noteData[16] = (byte)(pvhAddr & 0xFF);
        noteData[17] = (byte)((pvhAddr >> 8) & 0xFF);
        noteData[18] = (byte)((pvhAddr >> 16) & 0xFF);
        noteData[19] = (byte)((pvhAddr >> 24) & 0xFF);

        int noteFileOffset = Align(headersTotalSize, 4);
        int textFileOffset = Align(noteFileOffset + noteData.Length, 16);
        ulong textVaddr = bareMetalBase;
        // Map text at bareMetalBase: p_offset=textFileOffset, p_vaddr=bareMetalBase
        // So m_text[0] → 0x100000

        int rodataFileOffset = Align(textFileOffset + textSection.Length, 16);
        ulong rodataVaddr = bareMetalBase + (ulong)(rodataFileOffset - textFileOffset);

        int totalSize = rodataFileOffset + rodataSection.Length;

        MemoryStream ms = new(totalSize);
        BinaryWriter w = new(ms);

        // ── ELF Header ──
        w.Write((byte)0x7F); w.Write((byte)'E'); w.Write((byte)'L'); w.Write((byte)'F');
        w.Write(ELFCLASS64);
        w.Write(ELFDATA2LSB);
        w.Write((byte)1); // EV_CURRENT
        w.Write((byte)0); // ELFOSABI_NONE
        w.Write(new byte[8]);
        w.Write(ET_EXEC);
        w.Write(EM_X86_64);
        w.Write((uint)1);
        w.Write(textVaddr + pvhEntry32); // e_entry (64-bit, points to trampoline)
        w.Write((ulong)ElfHeaderSize);   // e_phoff
        w.Write((ulong)0);               // e_shoff
        w.Write((uint)0);                // e_flags
        w.Write((ushort)ElfHeaderSize);
        w.Write((ushort)ProgramHeaderSize);
        w.Write((ushort)phdrCount);
        w.Write((ushort)0);
        w.Write((ushort)0);
        w.Write((ushort)0);

        // ── PHDR 0: .text LOAD (rwx) ──
        w.Write(PT_LOAD);
        w.Write(PF_R | PF_W | PF_X);
        w.Write((ulong)textFileOffset);
        w.Write(textVaddr);
        w.Write(textVaddr);
        w.Write((ulong)textSection.Length);
        w.Write((ulong)textSection.Length + 0x400000); // 4MB heap (2MB working + 2MB result)
        w.Write((ulong)0x1000);

        // ── PHDR 1: .rodata LOAD (r) ──
        w.Write(PT_LOAD);
        w.Write(PF_R);
        w.Write((ulong)rodataFileOffset);
        w.Write(rodataVaddr);
        w.Write(rodataVaddr);
        w.Write((ulong)rodataSection.Length);
        w.Write((ulong)rodataSection.Length);
        w.Write((ulong)0x1000);

        // ── PHDR 2: PVH NOTE ──
        const uint PT_NOTE = 4;
        w.Write(PT_NOTE);
        w.Write(PF_R);
        w.Write((ulong)noteFileOffset);
        w.Write((ulong)0); // vaddr (unused for notes)
        w.Write((ulong)0); // paddr
        w.Write((ulong)noteData.Length);
        w.Write((ulong)noteData.Length);
        w.Write((ulong)4); // align

        // ── Note data ──
        while (ms.Position < noteFileOffset)
            w.Write((byte)0);
        w.Write(noteData);

        // ── .text ──
        while (ms.Position < textFileOffset)
            w.Write((byte)0);
        w.Write(textSection);

        // ── .rodata ──
        while (ms.Position < rodataFileOffset)
            w.Write((byte)0);
        w.Write(rodataSection);

        return ms.ToArray();
    }

    public static ulong ComputeRodataVaddr(int textSize)
    {
        int headersTotalSize = ElfHeaderSize + ProgramHeaderSize * 2;
        int textFileOffset = Align(headersTotalSize, 16);
        int rodataFileOffset = Align(textFileOffset + textSize, 4096);
        return LinuxBaseAddress + (ulong)rodataFileOffset;
    }

    public static int ComputeTextFileOffset()
    {
        int headersTotalSize = ElfHeaderSize + ProgramHeaderSize * 2;
        return Align(headersTotalSize, 16);
    }

    static int Align(int value, int alignment)
    {
        int remainder = value % alignment;
        return remainder == 0 ? value : value + (alignment - remainder);
    }
}
