using Codex.Core;
using Codex.Emit.X86_64;
using Codex.IR;
using Xunit;

namespace Codex.Types.Tests;

// Locks in the ELF32 + DWARF layout produced by bare-metal emit. If the
// section header offsets, names, sizes, or order drift, these asserts
// fire before the bytes reach GDB — catches offset-calc mistakes that
// readelf/gdb would silently accept on a bad-but-parseable ELF.
public class X86_64DwarfShapeTests
{
    static byte[] EmitBareMetalHelloElf()
    {
        IRDefinition opening = new(
            "opening", [], IntegerType.s_instance,
            new IRIntegerLit(0L));
        IRChapter ir = new(
            QualifiedName.Simple("Hello"), [opening], Map<string, CodexType>.s_empty);

        X86_64Emitter emitter = new(X86_64Target.BareMetal);
        return emitter.EmitAssembly(ir, "hello");
    }

    [Fact]
    public void BareMetal_ELF_has_section_header_table_at_nonzero_offset()
    {
        byte[] elf = EmitBareMetalHelloElf();

        // e_shoff at byte 32 (ELF32).
        uint shoff = ReadU32(elf, 32);
        Assert.True(shoff > 0, "section header table offset must be set");
    }

    [Fact]
    public void BareMetal_ELF_has_six_section_headers()
    {
        byte[] elf = EmitBareMetalHelloElf();

        // e_shnum at byte 48, e_shentsize at byte 46.
        ushort shnum = ReadU16(elf, 48);
        ushort shentsize = ReadU16(elf, 46);

        Assert.Equal(6, shnum);
        Assert.Equal(40, shentsize);
    }

    [Fact]
    public void BareMetal_ELF_section_names_are_in_expected_order()
    {
        byte[] elf = EmitBareMetalHelloElf();

        string[] names = ReadSectionNames(elf);

        Assert.Equal(6, names.Length);
        Assert.Equal("", names[0]);
        Assert.Equal(".text", names[1]);
        Assert.Equal(".rodata", names[2]);
        Assert.Equal(".debug_info", names[3]);
        Assert.Equal(".debug_abbrev", names[4]);
        Assert.Equal(".shstrtab", names[5]);
    }

    [Fact]
    public void BareMetal_ELF_debug_info_begins_with_DWARF4_header()
    {
        byte[] elf = EmitBareMetalHelloElf();

        (uint offset, uint size) = FindSection(elf, ".debug_info");
        Assert.True(size >= 11, "debug_info must fit at least a 32-bit compile-unit header");

        // unit_length (4) + version (2) + debug_abbrev_offset (4) + address_size (1)
        uint unitLength = ReadU32(elf, (int)offset);
        ushort version = ReadU16(elf, (int)offset + 4);
        byte addressSize = elf[offset + 10];

        Assert.Equal(size - 4u, unitLength);
        Assert.Equal(4, version);
        Assert.Equal(8, addressSize);
    }

    [Fact]
    public void BareMetal_ELF_debug_info_address_range_matches_text_section()
    {
        byte[] elf = EmitBareMetalHelloElf();

        (uint textOff, uint textSize) = FindSection(elf, ".text");
        (uint infoOff, _) = FindSection(elf, ".debug_info");

        // Skip compile_unit header (11 bytes) then abbrev code (1 byte = 1).
        // compile_unit DIE attrs come after: producer (string), language
        // (data1), name (string), low_pc (addr 8), high_pc (addr 8).
        int p = (int)infoOff + 11;
        Assert.Equal(1, elf[p]); // abbrev code 1
        p++;
        p = SkipCString(elf, p);          // producer
        p++;                              // language (data1)
        p = SkipCString(elf, p);          // name
        ulong lowPc = ReadU64(elf, p);
        ulong highPc = ReadU64(elf, p + 8);

        // .text maps at 0x100000 regardless of its file offset.
        Assert.Equal(0x100000ul, lowPc);
        Assert.Equal(0x100000ul + textSize, highPc);
    }

    // ── helpers ──────────────────────────────────────────────

    static ushort ReadU16(byte[] b, int o) =>
        (ushort)(b[o] | (b[o + 1] << 8));

    static uint ReadU32(byte[] b, int o) =>
        (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));

    static ulong ReadU64(byte[] b, int o)
    {
        ulong v = 0;
        for (int i = 0; i < 8; i++)
        {
            v |= (ulong)b[o + i] << (8 * i);
        }
        return v;
    }

    static int SkipCString(byte[] b, int o)
    {
        while (b[o] != 0) { o++; }
        return o + 1;
    }

    static string[] ReadSectionNames(byte[] elf)
    {
        uint shoff = ReadU32(elf, 32);
        ushort shnum = ReadU16(elf, 48);
        ushort shstrndx = ReadU16(elf, 50);
        // Section table entry is 40 bytes; sh_offset at offset 16.
        uint shstrtabHdrPos = shoff + (uint)(shstrndx * 40);
        uint strtabOff = ReadU32(elf, (int)shstrtabHdrPos + 16);

        string[] result = new string[shnum];
        for (int i = 0; i < shnum; i++)
        {
            uint entry = shoff + (uint)(i * 40);
            uint nameOff = ReadU32(elf, (int)entry);
            int pos = (int)(strtabOff + nameOff);
            int end = pos;
            while (elf[end] != 0) { end++; }
            result[i] = System.Text.Encoding.ASCII.GetString(elf, pos, end - pos);
        }
        return result;
    }

    static (uint offset, uint size) FindSection(byte[] elf, string name)
    {
        string[] names = ReadSectionNames(elf);
        uint shoff = ReadU32(elf, 32);
        for (int i = 0; i < names.Length; i++)
        {
            if (names[i] == name)
            {
                uint entry = shoff + (uint)(i * 40);
                return (ReadU32(elf, (int)entry + 16), ReadU32(elf, (int)entry + 20));
            }
        }
        throw new Xunit.Sdk.XunitException($"section {name} not found");
    }
}
