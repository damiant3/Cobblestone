namespace Codex.Emit.RiscV;

sealed class ElfWriter
{
    // ELF constants
    const byte ELFCLASS64 = 2;
    const byte ELFDATA2LSB = 1;   // little-endian
    const ushort EM_RISCV = 243;
    const ushort ET_EXEC = 2;
    const uint PT_LOAD = 1;
    const uint PF_X = 1;
    const uint PF_W = 2;
    const uint PF_R = 4;

    // Layout constants
    const ulong LinuxBaseAddress = 0x10000;    // standard Linux user-space base
    const ulong BareMetalBaseAddress = 0x80000000; // QEMU virt machine -kernel load address

    const int ElfHeaderSize = 64;
    const int ProgramHeaderSize = 56;

    public static byte[] WriteExecutable(byte[] textSection, byte[] rodataSection, ulong entryOffset,
        RiscVTarget target = RiscVTarget.LinuxUser)
    {
        ulong baseAddr = target == RiscVTarget.BareMetal ? BareMetalBaseAddress : LinuxBaseAddress;

        int phdrCount = 2;
        int headersTotalSize = ElfHeaderSize + ProgramHeaderSize * phdrCount;

        // Align text to 16 bytes after headers
        int textFileOffset = Align(headersTotalSize, 16);
        ulong textVaddr = baseAddr + (ulong)textFileOffset;
        ulong entryPoint = textVaddr + entryOffset;

        // Page-align rodata to avoid sharing a page with text (different permissions)
        int rodataFileOffset = Align(textFileOffset + textSection.Length, 4096);
        ulong rodataVaddr = baseAddr + (ulong)rodataFileOffset;

        int totalSize = rodataFileOffset + rodataSection.Length;

        MemoryStream ms = new(totalSize);
        BinaryWriter w = new(ms);

        // ── ELF Header (64 bytes) ──────────────────────────────
        // e_ident[0..3]: magic
        w.Write((byte)0x7F); w.Write((byte)'E'); w.Write((byte)'L'); w.Write((byte)'F');
        // e_ident[4]: class
        w.Write(ELFCLASS64);
        // e_ident[5]: data encoding
        w.Write(ELFDATA2LSB);
        // e_ident[6]: version
        w.Write((byte)1);
        // e_ident[7]: OS/ABI (Linux)
        w.Write((byte)0); // OS/ABI: NONE (works for both Linux and bare metal)
        // e_ident[8..15]: padding
        w.Write(new byte[8]);

        // e_type: executable
        w.Write(ET_EXEC);
        // e_machine: RISC-V
        w.Write(EM_RISCV);
        // e_version
        w.Write((uint)1);
        // e_entry: entry point virtual address
        w.Write(entryPoint);
        // e_phoff: program header offset
        w.Write((ulong)ElfHeaderSize);
        // e_shoff: section header offset (0 = none)
        w.Write((ulong)0);
        // e_flags: RISC-V flags (RVC + double-float ABI = 0x0005 for RV64GC, 0x0004 for double-float)
        w.Write((uint)0x0004);
        // e_ehsize
        w.Write((ushort)ElfHeaderSize);
        // e_phentsize
        w.Write((ushort)ProgramHeaderSize);
        // e_phnum
        w.Write((ushort)phdrCount);
        // e_shentsize
        w.Write((ushort)0);
        // e_shnum
        w.Write((ushort)0);
        // e_shstrndx
        w.Write((ushort)0);

        // ── Program Header 0: .text (r-x) ─────────────────────
        w.Write(PT_LOAD);                     // p_type
        w.Write(PF_R | PF_X);                 // p_flags
        w.Write((ulong)textFileOffset);        // p_offset
        w.Write(textVaddr);                    // p_vaddr
        w.Write(textVaddr);                    // p_paddr
        w.Write((ulong)textSection.Length);     // p_filesz
        w.Write((ulong)textSection.Length);     // p_memsz
        w.Write((ulong)0x1000);                // p_align

        // ── Program Header 1: .rodata (r--) ───────────────────
        w.Write(PT_LOAD);                      // p_type
        w.Write(PF_R);                         // p_flags
        w.Write((ulong)rodataFileOffset);       // p_offset
        w.Write(rodataVaddr);                   // p_vaddr
        w.Write(rodataVaddr);                   // p_paddr
        w.Write((ulong)rodataSection.Length);    // p_filesz
        w.Write((ulong)rodataSection.Length);    // p_memsz
        w.Write((ulong)16);                     // p_align

        // ── Pad to text offset ─────────────────────────────────
        while (ms.Position < textFileOffset)
        {
            w.Write((byte)0);
        }

        // ── .text ──────────────────────────────────────────────
        w.Write(textSection);

        // ── Pad to rodata offset ───────────────────────────────
        while (ms.Position < rodataFileOffset)
        {
            w.Write((byte)0);
        }

        // ── .rodata ────────────────────────────────────────────
        w.Write(rodataSection);

        return ms.ToArray();
    }

    // Writes a flat binary for bare metal: just code + rodata, no headers.
    // QEMU -kernel loads this at the base address and jumps to byte 0.
    public static byte[] WriteFlatBinary(byte[] textSection, byte[] rodataSection)
    {
        int rodataOffset = Align(textSection.Length, 16);
        int totalSize = rodataOffset + rodataSection.Length;

        byte[] result = new byte[totalSize];
        Array.Copy(textSection, 0, result, 0, textSection.Length);
        Array.Copy(rodataSection, 0, result, rodataOffset, rodataSection.Length);
        return result;
    }

    // Returns the virtual address where .rodata starts.
    public static ulong ComputeRodataVaddr(int textSize, RiscVTarget target = RiscVTarget.LinuxUser)
    {
        if (target == RiscVTarget.BareMetal)
        {
            int rodataOffset = Align(textSize, 16);
            return BareMetalBaseAddress + (ulong)rodataOffset;
        }
        ulong baseAddr = LinuxBaseAddress;
        int headersTotalSize = ElfHeaderSize + ProgramHeaderSize * 2;
        int textFileOffset = Align(headersTotalSize, 16);
        int rodataFileOffset = Align(textFileOffset + textSize, 4096);
        return baseAddr + (ulong)rodataFileOffset;
    }

    public static int ComputeTextFileOffset(RiscVTarget target = RiscVTarget.LinuxUser)
    {
        if (target == RiscVTarget.BareMetal)
        {
            return 0;
        }

        int headersTotalSize = ElfHeaderSize + ProgramHeaderSize * 2;
        return Align(headersTotalSize, 16);
    }

    static int Align(int value, int alignment)
    {
        int remainder = value % alignment;
        return remainder == 0 ? value : value + (alignment - remainder);
    }
}
