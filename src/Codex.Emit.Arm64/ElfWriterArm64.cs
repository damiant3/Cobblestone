namespace Codex.Emit.Arm64;

/// Writes a minimal ELF64 executable for Linux AArch64.
/// Same structure as the RISC-V ElfWriter but with ARM64 constants.
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
        w.Write(EM_AARCH64);
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

        // ── Program Header 0: .text (r-x) ─────────────────────
        w.Write(PT_LOAD);
        w.Write(PF_R | PF_X);
        w.Write((ulong)textFileOffset);
        w.Write(textVaddr);
        w.Write(textVaddr);
        w.Write((ulong)textSection.Length);
        w.Write((ulong)textSection.Length);
        w.Write((ulong)16);

        // ── Program Header 1: .rodata (r--) ───────────────────
        w.Write(PT_LOAD);
        w.Write(PF_R);
        w.Write((ulong)rodataFileOffset);
        w.Write(rodataVaddr);
        w.Write(rodataVaddr);
        w.Write((ulong)rodataSection.Length);
        w.Write((ulong)rodataSection.Length);
        w.Write((ulong)16);

        // ── Pad to text offset ─────────────────────────────────
        while (ms.Position < textFileOffset)
            w.Write((byte)0);

        w.Write(textSection);

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
