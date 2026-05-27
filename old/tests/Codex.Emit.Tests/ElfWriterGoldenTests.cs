using Codex.Emit.X86_64;
using Xunit;

namespace Codex.Emit.Tests;

/// <summary>
/// Golden reference tests for the C# ELF writers. These establish the byte-exact
/// reference that the Codex port (Codex.Codex/Emit/ElfWriter.codex) must match.
/// Same validation pattern as X86_64EncoderGoldenTests.
/// </summary>
public class ElfWriterGoldenTests
{
    // ═══════════════════════════════════════════════════════════
    // 32-bit bare-metal ELF (ElfWriter32) — used for QEMU PVH boot
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void BareMetal32_Magic()
    {
        byte[] elf = ElfWriter32.WriteExecutable(new byte[16], Array.Empty<byte>(), 12);
        Assert.Equal(0x7F, elf[0]);
        Assert.Equal((byte)'E', elf[1]);
        Assert.Equal((byte)'L', elf[2]);
        Assert.Equal((byte)'F', elf[3]);
    }

    [Fact]
    public void BareMetal32_ElfClass()
    {
        byte[] elf = ElfWriter32.WriteExecutable(new byte[16], Array.Empty<byte>(), 12);
        Assert.Equal(1, elf[4]);   // ELFCLASS32
        Assert.Equal(1, elf[5]);   // ELFDATA2LSB
        Assert.Equal(1, elf[6]);   // EV_CURRENT
    }

    [Fact]
    public void BareMetal32_MachineType()
    {
        byte[] elf = ElfWriter32.WriteExecutable(new byte[16], Array.Empty<byte>(), 12);
        // e_type at offset 16 (2 bytes LE)
        Assert.Equal(2, BitConverter.ToUInt16(elf, 16));  // ET_EXEC
        // e_machine at offset 18 (2 bytes LE)
        Assert.Equal(3, BitConverter.ToUInt16(elf, 18));  // EM_386
    }

    [Fact]
    public void BareMetal32_EntryPoint()
    {
        byte[] elf = ElfWriter32.WriteExecutable(new byte[16], Array.Empty<byte>(), 12);
        // e_entry at offset 24 (4 bytes LE) = LoadAddress + entryOffset = 0x100000 + 12
        uint entry = BitConverter.ToUInt32(elf, 24);
        Assert.Equal(0x10000Cu, entry);
    }

    [Fact]
    public void BareMetal32_ProgramHeaderOffset()
    {
        byte[] elf = ElfWriter32.WriteExecutable(new byte[16], Array.Empty<byte>(), 12);
        // e_phoff at offset 28 = 52 (right after ELF header)
        Assert.Equal(52u, BitConverter.ToUInt32(elf, 28));
        // e_phnum at offset 44 = 2
        Assert.Equal(2, BitConverter.ToUInt16(elf, 44));
    }

    [Fact]
    public void BareMetal32_HeaderSizes()
    {
        byte[] elf = ElfWriter32.WriteExecutable(new byte[16], Array.Empty<byte>(), 12);
        Assert.Equal(52, BitConverter.ToUInt16(elf, 40));  // e_ehsize
        Assert.Equal(32, BitConverter.ToUInt16(elf, 42));  // e_phentsize
    }

    [Fact]
    public void BareMetal32_LoadPhdr()
    {
        byte[] text = new byte[100];
        byte[] rodata = new byte[20];
        byte[] elf = ElfWriter32.WriteExecutable(text, rodata, 12);

        // PHDR 0 at offset 52: PT_LOAD
        int ph0 = 52;
        Assert.Equal(1u, BitConverter.ToUInt32(elf, ph0));      // p_type = PT_LOAD
        Assert.Equal(0x100000u, BitConverter.ToUInt32(elf, ph0 + 8));  // p_vaddr
        Assert.Equal(0x100000u, BitConverter.ToUInt32(elf, ph0 + 12)); // p_paddr
        Assert.Equal(7u, BitConverter.ToUInt32(elf, ph0 + 24)); // p_flags = RWX
    }

    [Fact]
    public void BareMetal32_LoadPhdrMemsz()
    {
        byte[] text = new byte[100];
        byte[] rodata = new byte[20];
        byte[] elf = ElfWriter32.WriteExecutable(text, rodata, 12);

        int ph0 = 52;
        uint filesz = BitConverter.ToUInt32(elf, ph0 + 16);
        uint memsz = BitConverter.ToUInt32(elf, ph0 + 20);
        // memsz = filesz + ~1 GB heap (upper bound of the 1 GB identity page map).
        // Bumped from 2 MB → 1 GB by commit a725ac7; keep this in lockstep with
        // ElfWriter32.WriteExecutable's p_memsz computation.
        Assert.Equal(filesz + 0x3FC00000u, memsz);
    }

    [Fact]
    public void BareMetal32_NotePhdr()
    {
        byte[] elf = ElfWriter32.WriteExecutable(new byte[16], Array.Empty<byte>(), 12);

        // PHDR 1 at offset 84: PT_NOTE
        int ph1 = 52 + 32;
        Assert.Equal(4u, BitConverter.ToUInt32(elf, ph1));      // p_type = PT_NOTE
        Assert.Equal(20u, BitConverter.ToUInt32(elf, ph1 + 16)); // p_filesz = 20
        Assert.Equal(4u, BitConverter.ToUInt32(elf, ph1 + 24));  // p_flags = PF_R
    }

    [Fact]
    public void BareMetal32_PvhNote()
    {
        byte[] elf = ElfWriter32.WriteExecutable(new byte[16], Array.Empty<byte>(), 12);

        // Note at offset 116 (52 + 32*2 = 116, already 4-aligned)
        int noteOff = 116;
        Assert.Equal(4u, BitConverter.ToUInt32(elf, noteOff));      // namesz
        Assert.Equal(4u, BitConverter.ToUInt32(elf, noteOff + 4));  // descsz
        Assert.Equal(18u, BitConverter.ToUInt32(elf, noteOff + 8)); // type = XEN_ELFNOTE_PHYS32_ENTRY
        Assert.Equal((byte)'X', elf[noteOff + 12]);
        Assert.Equal((byte)'e', elf[noteOff + 13]);
        Assert.Equal((byte)'n', elf[noteOff + 14]);
        Assert.Equal(0, elf[noteOff + 15]);
        // desc = LoadAddress + 12 = 0x10000C
        Assert.Equal(0x10000Cu, BitConverter.ToUInt32(elf, noteOff + 16));
    }

    [Fact]
    public void BareMetal32_TextAtExpectedOffset()
    {
        byte[] text = new byte[] { 0xCC, 0xCC, 0xCC, 0xCC }; // int3 x4
        byte[] elf = ElfWriter32.WriteExecutable(text, Array.Empty<byte>(), 0);

        // Text starts at Align(116 + 20, 16) = Align(136, 16) = 144
        Assert.Equal(0xCC, elf[144]);
        Assert.Equal(0xCC, elf[145]);
        Assert.Equal(0xCC, elf[146]);
        Assert.Equal(0xCC, elf[147]);
    }

    [Fact]
    public void BareMetal32_RodataAlignment()
    {
        byte[] text = new byte[100];
        byte[] rodata = new byte[] { 0xAA, 0xBB };
        byte[] elf = ElfWriter32.WriteExecutable(text, rodata, 0);

        // textStart = 144, textEnd = 244, rodataStart = Align(244, 8) = 248
        int rodataStart = ((144 + 100) + 7) & ~7; // 244 → 248
        Assert.Equal(0xAA, elf[rodataStart]);
        Assert.Equal(0xBB, elf[rodataStart + 1]);
    }

    [Fact]
    public void BareMetal32_EmptyRodata()
    {
        byte[] text = new byte[] { 0xF4 }; // hlt
        byte[] elf = ElfWriter32.WriteExecutable(text, Array.Empty<byte>(), 0);
        // Should not crash with empty rodata
        Assert.True(elf.Length > 0);
        // Text still at offset 144
        Assert.Equal(0xF4, elf[144]);
    }

    [Fact]
    public void BareMetal32_TotalSize()
    {
        byte[] text = new byte[100];
        byte[] rodata = new byte[20];
        byte[] elf = ElfWriter32.WriteExecutable(text, rodata, 12);

        // textStart=144, textEnd=244, rodataStart=248, fileSize=268
        Assert.Equal(268, elf.Length);
    }

    // ═══════════════════════════════════════════════════════════
    // 64-bit Linux user-mode ELF (ElfWriterX86_64)
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Linux64_Magic()
    {
        byte[] elf = ElfWriterX86_64.WriteExecutable(new byte[16], new byte[4], 0);
        Assert.Equal(0x7F, elf[0]);
        Assert.Equal((byte)'E', elf[1]);
        Assert.Equal((byte)'L', elf[2]);
        Assert.Equal((byte)'F', elf[3]);
    }

    [Fact]
    public void Linux64_ElfClass()
    {
        byte[] elf = ElfWriterX86_64.WriteExecutable(new byte[16], new byte[4], 0);
        Assert.Equal(2, elf[4]);   // ELFCLASS64
        Assert.Equal(1, elf[5]);   // ELFDATA2LSB
        Assert.Equal(1, elf[6]);   // EV_CURRENT
    }

    [Fact]
    public void Linux64_MachineType()
    {
        byte[] elf = ElfWriterX86_64.WriteExecutable(new byte[16], new byte[4], 0);
        Assert.Equal(2, BitConverter.ToUInt16(elf, 16));   // ET_EXEC
        Assert.Equal(62, BitConverter.ToUInt16(elf, 18));  // EM_X86_64
    }

    [Fact]
    public void Linux64_EntryPoint()
    {
        byte[] elf = ElfWriterX86_64.WriteExecutable(new byte[16], new byte[4], 0);
        // textFileOffset = Align(176, 16) = 176
        // textVaddr = 0x400000 + 176 = 0x4000B0
        // entryPoint = textVaddr + 0 = 0x4000B0
        ulong entry = BitConverter.ToUInt64(elf, 24);
        Assert.Equal(0x4000B0UL, entry);
    }

    [Fact]
    public void Linux64_EntryPointWithOffset()
    {
        byte[] elf = ElfWriterX86_64.WriteExecutable(new byte[256], new byte[4], 100);
        ulong entry = BitConverter.ToUInt64(elf, 24);
        // textVaddr = 0x400000 + 176 = 0x4000B0
        // entryPoint = 0x4000B0 + 100 = 0x400114
        Assert.Equal(0x400114UL, entry);
    }

    [Fact]
    public void Linux64_ProgramHeaders()
    {
        byte[] elf = ElfWriterX86_64.WriteExecutable(new byte[16], new byte[4], 0);
        // e_phoff at offset 32 (8 bytes) = 64
        Assert.Equal(64UL, BitConverter.ToUInt64(elf, 32));
        // e_phnum at offset 56 (2 bytes) = 2
        Assert.Equal(2, BitConverter.ToUInt16(elf, 56));
        // e_phentsize at offset 54 (2 bytes) = 56
        Assert.Equal(56, BitConverter.ToUInt16(elf, 54));
    }

    [Fact]
    public void Linux64_TextPhdr()
    {
        byte[] elf = ElfWriterX86_64.WriteExecutable(new byte[16], new byte[4], 0);

        // PHDR 0 at offset 64
        int ph0 = 64;
        Assert.Equal(1u, BitConverter.ToUInt32(elf, ph0));       // p_type = PT_LOAD
        Assert.Equal(7u, BitConverter.ToUInt32(elf, ph0 + 4));   // p_flags = RWX
        Assert.Equal(0UL, BitConverter.ToUInt64(elf, ph0 + 8));  // p_offset = 0
        Assert.Equal(0x400000UL, BitConverter.ToUInt64(elf, ph0 + 16));  // p_vaddr
    }

    [Fact]
    public void Linux64_RodataPhdr()
    {
        byte[] text = new byte[100];
        byte[] rodata = new byte[20];
        byte[] elf = ElfWriterX86_64.WriteExecutable(text, rodata, 0);

        // PHDR 1 at offset 120 (64 + 56)
        int ph1 = 120;
        Assert.Equal(1u, BitConverter.ToUInt32(elf, ph1));       // p_type = PT_LOAD
        Assert.Equal(6u, BitConverter.ToUInt32(elf, ph1 + 4));   // p_flags = RW (no X)
    }

    [Fact]
    public void Linux64_RodataPageAligned()
    {
        byte[] text = new byte[100];
        byte[] rodata = new byte[] { 0xDE, 0xAD };
        byte[] elf = ElfWriterX86_64.WriteExecutable(text, rodata, 0);

        // textFileOffset = 176, rodataFileOffset = Align(176+100, 4096) = 4096
        int rodataOff = 4096;
        Assert.Equal(0xDE, elf[rodataOff]);
        Assert.Equal(0xAD, elf[rodataOff + 1]);
    }

    [Fact]
    public void Linux64_TextAtExpectedOffset()
    {
        byte[] text = new byte[] { 0xCC, 0xCC };
        byte[] elf = ElfWriterX86_64.WriteExecutable(text, new byte[1], 0);
        // textFileOffset = 176
        Assert.Equal(0xCC, elf[176]);
        Assert.Equal(0xCC, elf[177]);
    }

    [Fact]
    public void Linux64_TotalSizeIncludesPagePad()
    {
        byte[] text = new byte[100];
        byte[] rodata = new byte[20];
        byte[] elf = ElfWriterX86_64.WriteExecutable(text, rodata, 0);
        // rodataFileOffset = 4096, totalSize = 4096 + 20 = 4116
        Assert.Equal(4116, elf.Length);
    }

    // ═══════════════════════════════════════════════════════════
    // Codegen helpers
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void ComputeTextFileOffset64()
    {
        // 64 + 56*2 = 176, aligned to 16 = 176
        Assert.Equal(176, ElfWriterX86_64.ComputeTextFileOffset());
    }

    [Fact]
    public void ComputeRodataVaddr64()
    {
        // textFileOffset=176, text=100, rodataFileOffset=Align(276,4096)=4096
        // rodataVaddr = 0x400000 + 4096 = 0x401000
        Assert.Equal(0x401000UL, ElfWriterX86_64.ComputeRodataVaddr(100));
    }

    [Fact]
    public void ComputeRodataVaddr64_LargeText()
    {
        // textFileOffset=176, text=8000, end=8176, rodataFileOffset=Align(8176,4096)=8192
        // rodataVaddr = 0x400000 + 8192 = 0x402000
        Assert.Equal(0x402000UL, ElfWriterX86_64.ComputeRodataVaddr(8000));
    }
}
