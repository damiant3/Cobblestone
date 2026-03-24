namespace Codex.Emit.Arm64;

sealed class ElfWriterArm64
{
    const byte ELFCLASS64 = 2;
    const byte ELFDATA2LSB = 1;
    const ushort EM_AARCH64 = 183;
    const ushort ET_EXEC = 2;
    const uint PT_LOAD = 1;
    const uint PF_X = 1;
    const uint PF_W = 2;
    const uint PF_R = 4;

    const ulong LinuxBaseAddress = 0x400000;  // standard AArch64 Linux base
    const int ElfHeaderSize = 64;
    const int ProgramHeaderSize = 56;
    const int SectionHeaderSize = 64;

    // Minimal .shstrtab: "\0.shstrtab\0" = 11 bytes
    static readonly byte[] ShstrtabData = { 0, 0x2E, 0x73, 0x68, 0x73, 0x74, 0x72, 0x74, 0x61, 0x62, 0 };

    public static byte[] WriteExecutable(byte[] textSection, byte[] rodataSection, ulong entryOffset)
    {
        int phdrCount = 2;
        int headersTotalSize = ElfHeaderSize + ProgramHeaderSize * phdrCount;

        int textFileOffset = Align(headersTotalSize, 16);
        ulong textVaddr = LinuxBaseAddress + (ulong)textFileOffset;
        ulong entryPoint = textVaddr + entryOffset;

        int rodataFileOffset = Align(textFileOffset + textSection.Length, 4096);
        ulong rodataVaddr = LinuxBaseAddress + (ulong)rodataFileOffset;

        // .shstrtab section data follows rodata
        int shstrtabOffset = Align(rodataFileOffset + rodataSection.Length, 8);
        int shstrtabSize = ShstrtabData.Length;

        // Section header table follows .shstrtab (2 entries: SHT_NULL + .shstrtab)
        int shOffset = Align(shstrtabOffset + shstrtabSize, 8);
        int totalSize = shOffset + SectionHeaderSize * 2;

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
        w.Write(EM_AARCH64);
        w.Write((uint)1);   // EV_CURRENT
        w.Write(entryPoint);
        w.Write((ulong)ElfHeaderSize);
        w.Write((ulong)shOffset);        // e_shoff
        w.Write((uint)0);                // e_flags
        w.Write((ushort)ElfHeaderSize);
        w.Write((ushort)ProgramHeaderSize);
        w.Write((ushort)phdrCount);
        w.Write((ushort)SectionHeaderSize); // e_shentsize
        w.Write((ushort)2);              // e_shnum: SHT_NULL + .shstrtab
        w.Write((ushort)1);              // e_shstrndx: index 1 = .shstrtab

        // ── Program Header 0: .text (r-x) ─────────────────────
        w.Write(PT_LOAD);
        w.Write(PF_R | PF_X);
        w.Write((ulong)textFileOffset);
        w.Write(textVaddr);
        w.Write(textVaddr);
        w.Write((ulong)textSection.Length);
        w.Write((ulong)textSection.Length);
        w.Write((ulong)0x1000);

        // ── Program Header 1: .rodata (r--) ───────────────────
        w.Write(PT_LOAD);
        w.Write(PF_R);
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

        // ── .shstrtab data ─────────────────────────────────────
        while (ms.Position < shstrtabOffset)
            w.Write((byte)0);
        w.Write(ShstrtabData);

        // ── Section Header Table ───────────────────────────────
        while (ms.Position < shOffset)
            w.Write((byte)0);

        // Entry 0: SHT_NULL (64 bytes of zeros)
        w.Write(new byte[SectionHeaderSize]);

        // Entry 1: .shstrtab (SHT_STRTAB)
        w.Write((uint)1);                  // sh_name: offset 1 in shstrtab (".shstrtab")
        w.Write((uint)3);                  // sh_type: SHT_STRTAB
        w.Write((ulong)0);                 // sh_flags
        w.Write((ulong)0);                 // sh_addr
        w.Write((ulong)shstrtabOffset);    // sh_offset
        w.Write((ulong)shstrtabSize);      // sh_size
        w.Write((uint)0);                  // sh_link
        w.Write((uint)0);                  // sh_info
        w.Write((ulong)1);                 // sh_addralign
        w.Write((ulong)0);                 // sh_entsize

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
