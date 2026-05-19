using Codex.Core;

namespace Codex.Emit.X86_64;

/// <summary>
/// Minimal DWARF 4 encoder for bare-metal ELF debug info. Emits `.debug_info`
/// (compile_unit + subprogram DIEs) and `.debug_abbrev`. String forms are
/// inline (DW_FORM_string) to avoid a separate `.debug_str` section.
/// Per-instruction line info (`.debug_line`) is Phase 1 — this first cut
/// only gives GDB function-level symbols: `info functions`, `bt` names,
/// and `break funcname` work; step-level source does not.
/// </summary>
public readonly record struct FunctionDebugEntry(
    string Name,
    int LowPcOffset,
    int HighPcOffset,
    // Span is plumbed from IRDefinition.Span but not yet consumed by Encode:
    // Phase 1 (per-line .debug_line + decl_file/decl_line on subprogram DIEs)
    // will read FileName and Line from this. Kept in the struct now so the
    // codegen collection site doesn't have to change again later.
    SourceSpan Span);

public static class DwarfWriter
{
    // DWARF 4 tags / attrs / forms we actually use.
    const byte DW_TAG_compile_unit = 0x11;
    const byte DW_TAG_subprogram = 0x2e;
    const byte DW_CHILDREN_yes = 1;
    const byte DW_CHILDREN_no = 0;

    const byte DW_AT_name = 0x03;
    const byte DW_AT_low_pc = 0x11;
    const byte DW_AT_high_pc = 0x12;
    const byte DW_AT_language = 0x13;
    const byte DW_AT_producer = 0x25;

    const byte DW_FORM_addr = 0x01;
    const byte DW_FORM_data1 = 0x0b;
    const byte DW_FORM_data8 = 0x07;
    const byte DW_FORM_string = 0x08;

    const byte DW_LANG_C = 0x02;

    public static (byte[] info, byte[] abbrev) Encode(
        string unitName,
        IReadOnlyList<FunctionDebugEntry> functions,
        ulong textBaseAddress,
        int textSize)
    {
        // ── .debug_abbrev ──────────────────────────────────────
        //
        // Two abbreviations:
        //   1 = DW_TAG_compile_unit, has children
        //       producer (string), language (data1), name (string),
        //       low_pc (addr), high_pc (addr)
        //   2 = DW_TAG_subprogram, no children
        //       name (string), low_pc (addr), high_pc (addr)
        MemoryStream abbrevMs = new();

        WriteUleb(abbrevMs, 1);
        WriteUleb(abbrevMs, DW_TAG_compile_unit);
        abbrevMs.WriteByte(DW_CHILDREN_yes);
        WriteUleb(abbrevMs, DW_AT_producer);  WriteUleb(abbrevMs, DW_FORM_string);
        WriteUleb(abbrevMs, DW_AT_language);  WriteUleb(abbrevMs, DW_FORM_data1);
        WriteUleb(abbrevMs, DW_AT_name);      WriteUleb(abbrevMs, DW_FORM_string);
        WriteUleb(abbrevMs, DW_AT_low_pc);    WriteUleb(abbrevMs, DW_FORM_addr);
        WriteUleb(abbrevMs, DW_AT_high_pc);   WriteUleb(abbrevMs, DW_FORM_addr);
        abbrevMs.WriteByte(0); abbrevMs.WriteByte(0);

        WriteUleb(abbrevMs, 2);
        WriteUleb(abbrevMs, DW_TAG_subprogram);
        abbrevMs.WriteByte(DW_CHILDREN_no);
        WriteUleb(abbrevMs, DW_AT_name);      WriteUleb(abbrevMs, DW_FORM_string);
        WriteUleb(abbrevMs, DW_AT_low_pc);    WriteUleb(abbrevMs, DW_FORM_addr);
        WriteUleb(abbrevMs, DW_AT_high_pc);   WriteUleb(abbrevMs, DW_FORM_addr);
        abbrevMs.WriteByte(0); abbrevMs.WriteByte(0);

        abbrevMs.WriteByte(0);

        byte[] abbrev = abbrevMs.ToArray();

        // ── .debug_info ───────────────────────────────────────
        //
        // Header: unit_length(4) + version(2=4) + debug_abbrev_offset(4=0) +
        // address_size(1=8), patched at end once total length is known.
        MemoryStream infoMs = new();

        int unitLengthPos = (int)infoMs.Position;
        WriteUint32(infoMs, 0); // placeholder
        WriteUint16(infoMs, 4); // DWARF 4
        WriteUint32(infoMs, 0); // debug_abbrev_offset
        infoMs.WriteByte(8);    // address_size (x86-64 code, 8 bytes)

        // compile_unit DIE (abbrev 1)
        WriteUleb(infoMs, 1);
        WriteString(infoMs, "Codex");
        infoMs.WriteByte(DW_LANG_C);
        WriteString(infoMs, unitName);
        WriteUint64(infoMs, textBaseAddress);
        WriteUint64(infoMs, textBaseAddress + (ulong)textSize);

        // subprogram DIEs (abbrev 2)
        foreach (FunctionDebugEntry fn in functions)
        {
            WriteUleb(infoMs, 2);
            WriteString(infoMs, fn.Name);
            WriteUint64(infoMs, textBaseAddress + (ulong)fn.LowPcOffset);
            WriteUint64(infoMs, textBaseAddress + (ulong)fn.HighPcOffset);
        }

        // Terminate compile_unit's child list.
        infoMs.WriteByte(0);

        byte[] info = infoMs.ToArray();
        uint unitLength = (uint)(info.Length - 4);
        info[unitLengthPos]     = (byte)(unitLength & 0xFF);
        info[unitLengthPos + 1] = (byte)((unitLength >> 8) & 0xFF);
        info[unitLengthPos + 2] = (byte)((unitLength >> 16) & 0xFF);
        info[unitLengthPos + 3] = (byte)((unitLength >> 24) & 0xFF);

        return (info, abbrev);
    }

    static void WriteUleb(Stream s, ulong value)
    {
        while (true)
        {
            byte b = (byte)(value & 0x7F);
            value >>= 7;
            if (value == 0)
            {
                s.WriteByte(b);
                return;
            }
            s.WriteByte((byte)(b | 0x80));
        }
    }

    static void WriteString(Stream s, string value)
    {
        foreach (byte b in System.Text.Encoding.UTF8.GetBytes(value))
            s.WriteByte(b);
        s.WriteByte(0);
    }

    static void WriteUint16(Stream s, ushort value)
    {
        s.WriteByte((byte)(value & 0xFF));
        s.WriteByte((byte)((value >> 8) & 0xFF));
    }

    static void WriteUint32(Stream s, uint value)
    {
        s.WriteByte((byte)(value & 0xFF));
        s.WriteByte((byte)((value >> 8) & 0xFF));
        s.WriteByte((byte)((value >> 16) & 0xFF));
        s.WriteByte((byte)((value >> 24) & 0xFF));
    }

    static void WriteUint64(Stream s, ulong value)
    {
        for (int i = 0; i < 8; i++)
            s.WriteByte((byte)((value >> (i * 8)) & 0xFF));
    }
}
