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
            Array.Copy(rodata, 0, elf, rodataStart, rodata.Length);

        return elf;
    }

    /// <summary>
    /// Same as WriteExecutable but appends DWARF debug sections
    /// (.debug_info, .debug_abbrev) and a section header table naming them,
    /// so GDB can load symbols. PVH / PT_LOAD layout is unchanged — debug
    /// sections are non-alloc (SHF flag = 0) and sit after the loadable
    /// ranges, so kernel loaders ignore them.
    /// </summary>
    public static byte[] WriteExecutableWithDwarf(
        byte[] text, byte[] rodata, uint entryOffset,
        byte[] debugInfo, byte[] debugAbbrev)
    {
        int phdrCount = 2;
        int headersEnd = ElfHeaderSize + PhdrSize * phdrCount;

        byte[] note = new byte[20];
        note[0] = 4; note[4] = 4; note[8] = 18;
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
        int loadableEnd = rodataStart + rodata.Length;

        // Debug sections live after the loadable data. They're not in any
        // PT_LOAD so the kernel boot path never sees them.
        int debugInfoOffset = Align(loadableEnd, 8);
        int debugAbbrevOffset = debugInfoOffset + debugInfo.Length;

        // .shstrtab naming table. Entry 0 is the empty name (SHN_UNDEF).
        byte[] shstrtab = BuildShstrtab(out int textNameOff, out int rodataNameOff,
            out int debugInfoNameOff, out int debugAbbrevNameOff, out int shstrtabNameOff);
        int shstrtabOffset = debugAbbrevOffset + debugAbbrev.Length;

        // Section headers follow .shstrtab. Six entries: NULL, .text, .rodata,
        // .debug_info, .debug_abbrev, .shstrtab.
        const int ShentSize = 40;
        const int ShNum = 6;
        int shoff = Align(shstrtabOffset + shstrtab.Length, 8);
        int fileSize = shoff + ShentSize * ShNum;

        byte[] elf = new byte[fileSize];

        // ── ELF header ──
        elf[0] = 0x7F; elf[1] = (byte)'E'; elf[2] = (byte)'L'; elf[3] = (byte)'F';
        elf[4] = 1; elf[5] = 1; elf[6] = 1;

        Write16(elf, 16, 2);
        Write16(elf, 18, 3);
        Write32(elf, 20, 1);
        Write32(elf, 24, LoadAddress + entryOffset);
        Write32(elf, 28, (uint)ElfHeaderSize);
        Write32(elf, 32, (uint)shoff);
        Write32(elf, 36, 0);
        Write16(elf, 40, (ushort)ElfHeaderSize);
        Write16(elf, 42, (ushort)PhdrSize);
        Write16(elf, 44, (ushort)phdrCount);
        Write16(elf, 46, (ushort)ShentSize);
        Write16(elf, 48, (ushort)ShNum);
        Write16(elf, 50, 5); // e_shstrndx — .shstrtab is section index 5.

        // ── PHDR 0: PT_LOAD .text+.rodata ──
        int ph0 = ElfHeaderSize;
        Write32(elf, ph0, 1);
        Write32(elf, ph0 + 4, (uint)textStart);
        Write32(elf, ph0 + 8, LoadAddress);
        Write32(elf, ph0 + 12, LoadAddress);
        Write32(elf, ph0 + 16, (uint)(loadableEnd - textStart));
        Write32(elf, ph0 + 20, (uint)(loadableEnd - textStart) + 0x3FC00000);
        Write32(elf, ph0 + 24, 7);
        Write32(elf, ph0 + 28, 0x1000);

        // ── PHDR 1: PT_NOTE PVH ──
        int ph1 = ElfHeaderSize + PhdrSize;
        Write32(elf, ph1, 4);
        Write32(elf, ph1 + 4, (uint)noteOffset);
        Write32(elf, ph1 + 8, 0);
        Write32(elf, ph1 + 12, 0);
        Write32(elf, ph1 + 16, (uint)note.Length);
        Write32(elf, ph1 + 20, (uint)note.Length);
        Write32(elf, ph1 + 24, 4);
        Write32(elf, ph1 + 28, 4);

        Array.Copy(note, 0, elf, noteOffset, note.Length);
        Array.Copy(text, 0, elf, textStart, text.Length);
        if (rodata.Length > 0)
            Array.Copy(rodata, 0, elf, rodataStart, rodata.Length);
        Array.Copy(debugInfo, 0, elf, debugInfoOffset, debugInfo.Length);
        Array.Copy(debugAbbrev, 0, elf, debugAbbrevOffset, debugAbbrev.Length);
        Array.Copy(shstrtab, 0, elf, shstrtabOffset, shstrtab.Length);

        // ── Section headers ──
        // Each entry: sh_name, sh_type, sh_flags, sh_addr, sh_offset, sh_size,
        // sh_link, sh_info, sh_addralign, sh_entsize — all 4 bytes, 40 total.
        const uint SHT_PROGBITS = 1;
        const uint SHT_STRTAB = 3;
        const uint SHF_ALLOC = 2;
        const uint SHF_EXECINSTR = 4;

        // index 0: NULL — already zeroed by array init.
        // index 1: .text
        WriteSh(elf, shoff + 1 * ShentSize,
            (uint)textNameOff, SHT_PROGBITS, SHF_ALLOC | SHF_EXECINSTR,
            LoadAddress, (uint)textStart, (uint)text.Length, 0, 0, 16, 0);
        // index 2: .rodata
        WriteSh(elf, shoff + 2 * ShentSize,
            (uint)rodataNameOff, SHT_PROGBITS, SHF_ALLOC,
            LoadAddress + (uint)(rodataStart - textStart),
            (uint)rodataStart, (uint)rodata.Length, 0, 0, 8, 0);
        // index 3: .debug_info
        WriteSh(elf, shoff + 3 * ShentSize,
            (uint)debugInfoNameOff, SHT_PROGBITS, 0,
            0, (uint)debugInfoOffset, (uint)debugInfo.Length, 0, 0, 1, 0);
        // index 4: .debug_abbrev
        WriteSh(elf, shoff + 4 * ShentSize,
            (uint)debugAbbrevNameOff, SHT_PROGBITS, 0,
            0, (uint)debugAbbrevOffset, (uint)debugAbbrev.Length, 0, 0, 1, 0);
        // index 5: .shstrtab
        WriteSh(elf, shoff + 5 * ShentSize,
            (uint)shstrtabNameOff, SHT_STRTAB, 0,
            0, (uint)shstrtabOffset, (uint)shstrtab.Length, 0, 0, 1, 0);

        return elf;
    }

    static byte[] BuildShstrtab(out int textOff, out int rodataOff,
        out int debugInfoOff, out int debugAbbrevOff, out int shstrtabOff)
    {
        MemoryStream ms = new();
        ms.WriteByte(0);                                // index 0: empty name
        textOff = (int)ms.Position;         WriteCStr(ms, ".text");
        rodataOff = (int)ms.Position;       WriteCStr(ms, ".rodata");
        debugInfoOff = (int)ms.Position;    WriteCStr(ms, ".debug_info");
        debugAbbrevOff = (int)ms.Position;  WriteCStr(ms, ".debug_abbrev");
        shstrtabOff = (int)ms.Position;     WriteCStr(ms, ".shstrtab");
        return ms.ToArray();
    }

    static void WriteCStr(Stream s, string value)
    {
        foreach (byte b in System.Text.Encoding.ASCII.GetBytes(value))
            s.WriteByte(b);
        s.WriteByte(0);
    }

    static void WriteSh(byte[] buf, int offset,
        uint shName, uint shType, uint shFlags, uint shAddr,
        uint shOffset, uint shSize, uint shLink, uint shInfo,
        uint shAddralign, uint shEntsize)
    {
        Write32(buf, offset,      shName);
        Write32(buf, offset + 4,  shType);
        Write32(buf, offset + 8,  shFlags);
        Write32(buf, offset + 12, shAddr);
        Write32(buf, offset + 16, shOffset);
        Write32(buf, offset + 20, shSize);
        Write32(buf, offset + 24, shLink);
        Write32(buf, offset + 28, shInfo);
        Write32(buf, offset + 32, shAddralign);
        Write32(buf, offset + 36, shEntsize);
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
