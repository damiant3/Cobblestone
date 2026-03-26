namespace Codex.Core;

/// <summary>
/// Codex Character Encoding (CCE) — the single source of truth.
///
/// Every CCE lookup table in the project (compile-time encoding in the emitter,
/// runtime preamble in generated code, CLI encode command, tests) derives from
/// these arrays. Do not duplicate them — reference them.
///
/// Tier 0 (single byte, 0x00-0x7F): 128 code points for ASCII + common accented/Cyrillic.
/// Tier 1 (two bytes, 110xxxxx 10xxxxxx): 2,048 code points for extended scripts.
///
/// Tier 0 layout:
///   0-2:     Whitespace (NUL, LF, space)
///   3-12:    Digits (0-9)
///   13-38:   Lowercase letters (frequency-sorted: e,t,a,o,i,n,s,h,r,d,l,c,u,m,w,f,g,y,p,b,v,k,j,x,q,z)
///   39-64:   Uppercase letters (same frequency order)
///   65-75:   Prose punctuation (. , ! ? : ; ' " - ( ))
///   76-80:   Operators (+ = * &lt; &gt;)
///   81-93:   Syntax (/ @ # &amp; _ \ | [ ] { } ~ `)
///   94-112:  Accented Latin
///   113-127: Cyrillic
///
/// Tier 1 block layout:
///   0x000-0x07F: Latin Extended (accented Latin + symbols not in Tier 0)
///   0x080-0x0FF: Cyrillic Extended (Russian/Ukrainian/Serbian not in Tier 0)
///   0x100-0x1FF: Greek (reserved)
///   0x200-0x3FF: Arabic + Devanagari (reserved)
///   0x400-0x5FF: CJK top-512 (reserved)
///   0x600-0x6FF: Japanese Hiragana + Katakana (reserved)
///   0x700-0x7FF: Korean + misc (reserved)
///
/// Byte framing (self-synchronizing, same as UTF-8):
///   0xxxxxxx         = Tier 0 (single byte, 0x00-0x7F)
///   110xxxxx 10xxxxxx = Tier 1 (two bytes, start 0xC0-0xDF + continuation 0x80-0xBF)
///   10xxxxxx          = continuation byte (never a start)
/// </summary>
public static class CceTable
{
    // ── Tier 0 ──────────────────────────────────────────────────────────

    /// <summary>CCE byte → Unicode code point (128 entries, indexed by CCE value).</summary>
    public static readonly int[] ToUnicode =
    {
        0, 10, 32,                                                             // 0-2: whitespace
        48, 49, 50, 51, 52, 53, 54, 55, 56, 57,                               // 3-12: digits
        101, 116, 97, 111, 105, 110, 115, 104, 114, 100,                      // 13-22: lower
        108, 99, 117, 109, 119, 102, 103, 121, 112, 98,                       // 23-32
        118, 107, 106, 120, 113, 122,                                           // 33-38
        69, 84, 65, 79, 73, 78, 83, 72, 82, 68,                               // 39-48: upper
        76, 67, 85, 77, 87, 70, 71, 89, 80, 66,                               // 49-58
        86, 75, 74, 88, 81, 90,                                                 // 59-64
        46, 44, 33, 63, 58, 59, 39, 34, 45, 40, 41,                           // 65-75: prose punct
        43, 61, 42, 60, 62,                                                     // 76-80: operators
        47, 64, 35, 38, 95, 92, 124, 91, 93, 123, 125, 126, 96,              // 81-93: syntax
        233, 232, 234, 235, 225, 224, 226, 228, 243, 242,                     // 94-103: accented
        244, 246, 250, 249, 251, 252, 241, 231, 237,                           // 104-112
        1072, 1086, 1077, 1080, 1085, 1090, 1089, 1088,                       // 113-120: cyrillic
        1074, 1083, 1082, 1084, 1076, 1087, 1091                              // 121-127
    };

    /// <summary>Unicode code point → CCE byte. Built lazily from <see cref="ToUnicode"/>.</summary>
    public static readonly Dictionary<int, int> FromUnicode = BuildFromUnicode();

    static Dictionary<int, int> BuildFromUnicode()
    {
        var d = new Dictionary<int, int>();
        for (int i = 0; i < ToUnicode.Length; i++)
            d[ToUnicode[i]] = i;
        return d;
    }

    /// <summary>CCE byte for '?' — used as the replacement character for unmapped Unicode.</summary>
    public const int ReplacementCce = 68;

    // ── Tier 1 ──────────────────────────────────────────────────────────

    /// <summary>Tier 1 code point → Unicode (2048 entries). 0 = unmapped.</summary>
    public static readonly int[] Tier1ToUnicode = BuildTier1Table();

    /// <summary>Unicode → Tier 1 code point. Only contains mapped characters.</summary>
    public static readonly Dictionary<int, int> Tier1FromUnicode = BuildTier1FromUnicode();

    /// <summary>Number of populated Tier 1 entries (for diagnostics/stats).</summary>
    public static readonly int Tier1Count = CountTier1Entries();

    static int CountTier1Entries()
    {
        int count = 0;
        for (int i = 0; i < Tier1ToUnicode.Length; i++)
            if (Tier1ToUnicode[i] != 0) count++;
        return count;
    }

    static int[] BuildTier1Table()
    {
        var t = new int[2048];

        // Block 0 (0x000-0x07F): Latin Extended
        // Lowercase not in Tier 0
        int[] latinLower =
        {
            223, 227, 229, 230, 238, 239, 240, 245, 248, 253, 254, 255,  // ß ã å æ î ï ð õ ø ý þ ÿ
            257, 259, 261, 263, 269, 271, 273, 275, 281, 283, 287, 299,   // ā ă ą ć č ď đ ē ę ě ğ ī
            305, 314, 318, 322, 324, 328, 337, 341, 345, 347, 351, 353,   // ı ĺ ľ ł ń ň ő ŕ ř ś ş š
            357, 363, 367, 369, 378, 380, 382                              // ť ū ů ű ź ż ž
        };
        // Uppercase not in Tier 0
        int[] latinUpper =
        {
            192, 193, 194, 195, 196, 197, 198, 199, 200, 201, 202, 203,  // À Á Â Ã Ä Å Æ Ç È É Ê Ë
            204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 214, 216,  // Ì Í Î Ï Ð Ñ Ò Ó Ô Õ Ö Ø
            217, 218, 219, 220, 221, 222,                                  // Ù Ú Û Ü Ý Þ
            256, 258, 260, 262, 268, 270, 272, 274, 280, 282, 286, 298,  // Ā Ă Ą Ć Č Ď Đ Ē Ę Ě Ğ Ī
            313, 317, 321, 323, 327, 336, 340, 344, 346, 350, 352, 356,  // Ĺ Ľ Ł Ń Ň Ő Ŕ Ř Ś Ş Š Ť
            362, 366, 368, 377, 379, 381                                   // Ū Ů Ű Ź Ż Ž
        };
        // Symbols from Latin-1 Supplement
        int[] latinSymbols =
        {
            161, 162, 163, 164, 165, 167, 169, 171, 174, 176,             // ¡ ¢ £ ¤ ¥ § © « ® °
            177, 178, 179, 181, 183, 185, 187, 188, 189, 190,             // ± ² ³ µ · ¹ » ¼ ½ ¾
            191, 215, 247                                                   // ¿ × ÷
        };

        int slot = 0;
        foreach (int cp in latinLower) t[slot++] = cp;
        foreach (int cp in latinUpper) t[slot++] = cp;
        foreach (int cp in latinSymbols) t[slot++] = cp;
        // Remaining slots 0x000-0x07F stay 0 (unmapped/reserved)

        // Block 1 (0x080-0x0FF): Cyrillic Extended
        // Russian lowercase not in Tier 0 (sorted by frequency)
        int[] cyrillicLower =
        {
            1073, 1075, 1078, 1079, 1081, 1092, 1093, 1094,               // б г ж з й ф х ц
            1095, 1096, 1097, 1098, 1099, 1100, 1101, 1102, 1103, 1105    // ч ш щ ъ ы ь э ю я ё
        };
        // All Russian uppercase (А-Я + Ё)
        int[] cyrillicUpper =
        {
            1040, 1041, 1042, 1043, 1044, 1045, 1046, 1047, 1048, 1049,   // А Б В Г Д Е Ж З И Й
            1050, 1051, 1052, 1053, 1054, 1055, 1056, 1057, 1058, 1059,   // К Л М Н О П Р С Т У
            1060, 1061, 1062, 1063, 1064, 1065, 1066, 1067, 1068, 1069,   // Ф Х Ц Ч Ш Щ Ъ Ы Ь Э
            1070, 1071, 1025                                                // Ю Я Ё
        };
        // Ukrainian + Serbian
        int[] cyrillicExt =
        {
            1028, 1030, 1031, 1108, 1110, 1111, 1168, 1169,               // Є І Ї є і ї Ґ ґ
            1026, 1032, 1033, 1034, 1035, 1039,                            // Ђ Ј Љ Њ Ћ Џ
            1106, 1112, 1113, 1114, 1115, 1119,                            // ђ ј љ њ ћ џ
            1038, 1118, 1027, 1036, 1107, 1116                             // Ў ў Ѓ Ќ ѓ ќ
        };

        slot = 0x080;
        foreach (int cp in cyrillicLower) t[slot++] = cp;
        foreach (int cp in cyrillicUpper) t[slot++] = cp;
        foreach (int cp in cyrillicExt) t[slot++] = cp;
        // Remaining Cyrillic slots stay 0

        return t;
    }

    static Dictionary<int, int> BuildTier1FromUnicode()
    {
        var d = new Dictionary<int, int>();
        for (int i = 0; i < Tier1ToUnicode.Length; i++)
        {
            if (Tier1ToUnicode[i] != 0)
                d[Tier1ToUnicode[i]] = i;
        }
        return d;
    }

    // ── Tier identification ─────────────────────────────────────────────

    /// <summary>Determine tier from a CCE byte: 0=Tier 0, 1=Tier 1 start, -1=continuation.</summary>
    public static int TierOf(int b) => b switch
    {
        < 0x80 => 0,     // 0xxxxxxx  — Tier 0
        < 0xC0 => -1,    // 10xxxxxx  — continuation
        < 0xE0 => 1,     // 110xxxxx  — Tier 1 start
        < 0xF0 => 2,     // 1110xxxx  — Tier 2 (future)
        _ => 3            // 11110xxx  — Tier 3 (future)
    };

    // ── Encoding (Unicode → CCE) ────────────────────────────────────────

    /// <summary>Normalize Unicode text at an I/O boundary before CCE encoding.
    /// TAB (0x09) becomes two spaces; CR (0x0D) is stripped entirely.</summary>
    public static string NormalizeUnicode(string unicode)
    {
        if (unicode.IndexOfAny(['\t', '\r']) < 0)
            return unicode;
        var sb = new System.Text.StringBuilder(unicode.Length);
        foreach (char c in unicode)
        {
            if (c == '\t') sb.Append("  ");
            else if (c == '\r') { /* stripped */ }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Convert a Unicode string to CCE-encoded string.
    /// Tier 0 characters encode as single bytes; Tier 1 as two bytes (110xxxxx 10xxxxxx).
    /// Unmapped characters become '?' (CCE 68) instead of NUL.</summary>
    public static string Encode(string unicode)
    {
        string normalized = NormalizeUnicode(unicode);
        var sb = new System.Text.StringBuilder(normalized.Length);
        foreach (char c in normalized)
        {
            int u = c;
            if (FromUnicode.TryGetValue(u, out int cce))
            {
                sb.Append((char)cce);
            }
            else if (Tier1FromUnicode.TryGetValue(u, out int t1cp))
            {
                sb.Append((char)(0xC0 | (t1cp >> 6)));
                sb.Append((char)(0x80 | (t1cp & 0x3F)));
            }
            else
            {
                sb.Append((char)ReplacementCce);
            }
        }
        return sb.ToString();
    }

    /// <summary>Convert a CCE-encoded string to Unicode. Handles Tier 0 + Tier 1.</summary>
    public static string Decode(string cce)
    {
        var sb = new System.Text.StringBuilder(cce.Length);
        int i = 0;
        while (i < cce.Length)
        {
            int b = cce[i];
            if (b < 0x80)
            {
                // Tier 0: single byte
                sb.Append((char)ToUnicode[b]);
                i++;
            }
            else if (b >= 0xC0 && b < 0xE0 && i + 1 < cce.Length)
            {
                // Tier 1: two bytes
                int b2 = cce[i + 1];
                if ((b2 & 0xC0) == 0x80)
                {
                    int cp = ((b & 0x1F) << 6) | (b2 & 0x3F);
                    int uni = (cp < Tier1ToUnicode.Length && Tier1ToUnicode[cp] != 0)
                        ? Tier1ToUnicode[cp] : 0xFFFD;
                    sb.Append((char)uni);
                    i += 2;
                }
                else
                {
                    sb.Append('\uFFFD');
                    i++;
                }
            }
            else
            {
                // Continuation without start, or future tier — replacement
                sb.Append('\uFFFD');
                i++;
            }
        }
        return sb.ToString();
    }

    /// <summary>Convert a single Unicode code point to a single CCE byte (Tier 0 only).
    /// Used by x86-64 backend for char literals. Unmapped returns '?' (68).</summary>
    public static long UnicharToCce(long unicode)
    {
        return FromUnicode.TryGetValue((int)unicode, out int cce) ? cce : ReplacementCce;
    }

    /// <summary>Convert a single CCE byte to Unicode code point (Tier 0 only).</summary>
    public static long CceToUnichar(long cce)
    {
        return (cce >= 0 && cce < 128) ? ToUnicode[(int)cce] : 65533;
    }

    // ── Runtime source generation ───────────────────────────────────────

    /// <summary>
    /// Generate the C# source for the _Cce runtime class (emitted into compiled programs).
    /// Supports both Tier 0 and Tier 1 encoding/decoding.
    /// </summary>
    public static string GenerateRuntimeSource()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("static class _Cce {");

        // Tier 0 table
        sb.AppendLine("    static readonly int[] _toUni = {");
        sb.AppendLine($"        {FormatRow(ToUnicode, 0, 3)}");     // whitespace
        sb.AppendLine($"        {FormatRow(ToUnicode, 3, 10)}");    // digits
        sb.AppendLine($"        {FormatRow(ToUnicode, 13, 10)}");   // lower 1
        sb.AppendLine($"        {FormatRow(ToUnicode, 23, 10)}");   // lower 2
        sb.AppendLine($"        {FormatRow(ToUnicode, 33, 6)}");    // lower 3
        sb.AppendLine($"        {FormatRow(ToUnicode, 39, 10)}");   // upper 1
        sb.AppendLine($"        {FormatRow(ToUnicode, 49, 10)}");   // upper 2
        sb.AppendLine($"        {FormatRow(ToUnicode, 59, 6)}");    // upper 3
        sb.AppendLine($"        {FormatRow(ToUnicode, 65, 11)}");   // prose punct
        sb.AppendLine($"        {FormatRow(ToUnicode, 76, 5)}");    // operators
        sb.AppendLine($"        {FormatRow(ToUnicode, 81, 13)}");   // syntax
        sb.AppendLine($"        {FormatRow(ToUnicode, 94, 10)}");   // accented 1
        sb.AppendLine($"        {FormatRow(ToUnicode, 104, 9)}");   // accented 2
        sb.AppendLine($"        {FormatRow(ToUnicode, 113, 8)}");   // cyrillic 1
        sb.AppendLine($"        {FormatRow(ToUnicode, 121, 7)}");   // cyrillic 2
        sb.AppendLine("    };");

        // Tier 1 table — emit only non-zero entries as a sparse dictionary
        sb.AppendLine("    static readonly Dictionary<int, int> _t1ToUni = new() {");
        for (int i = 0; i < Tier1ToUnicode.Length; i++)
        {
            if (Tier1ToUnicode[i] != 0)
                sb.AppendLine($"        [{i}] = {Tier1ToUnicode[i]},");
        }
        sb.AppendLine("    };");
        sb.AppendLine("    static readonly Dictionary<int, int> _t1FromUni = new() {");
        foreach (var kv in Tier1FromUnicode)
            sb.AppendLine($"        [{kv.Key}] = {kv.Value},");
        sb.AppendLine("    };");

        // Tier 0 reverse lookup
        sb.AppendLine("    static readonly Dictionary<int, int> _fromUni = new();");
        sb.AppendLine("    static _Cce() { for (int i = 0; i < 128; i++) _fromUni[_toUni[i]] = i; }");

        // FromUnicode — multi-byte aware
        sb.AppendLine("    public static string FromUnicode(string s) {");
        sb.AppendLine("        s = s.Replace(\"\\t\", \"  \").Replace(\"\\r\", \"\");");
        sb.AppendLine("        var sb = new System.Text.StringBuilder(s.Length);");
        sb.AppendLine("        foreach (char c in s) {");
        sb.AppendLine("            int u = c;");
        sb.AppendLine("            if (_fromUni.TryGetValue(u, out int b)) sb.Append((char)b);");
        sb.AppendLine("            else if (_t1FromUni.TryGetValue(u, out int cp)) {");
        sb.AppendLine("                sb.Append((char)(0xC0 | (cp >> 6)));");
        sb.AppendLine("                sb.Append((char)(0x80 | (cp & 0x3F)));");
        sb.AppendLine("            }");
        sb.AppendLine("            else sb.Append((char)68);");
        sb.AppendLine("        }");
        sb.AppendLine("        return sb.ToString();");
        sb.AppendLine("    }");

        // ToUnicode — multi-byte aware
        sb.AppendLine("    public static string ToUnicode(string s) {");
        sb.AppendLine("        var sb = new System.Text.StringBuilder(s.Length);");
        sb.AppendLine("        int i = 0;");
        sb.AppendLine("        while (i < s.Length) {");
        sb.AppendLine("            int b = s[i];");
        sb.AppendLine("            if (b < 0x80) { sb.Append((char)_toUni[b]); i++; }");
        sb.AppendLine("            else if (b >= 0xC0 && b < 0xE0 && i+1 < s.Length) {");
        sb.AppendLine("                int cp = ((b & 0x1F) << 6) | (s[i+1] & 0x3F);");
        sb.AppendLine("                sb.Append(_t1ToUni.TryGetValue(cp, out int u) ? (char)u : '\\uFFFD');");
        sb.AppendLine("                i += 2;");
        sb.AppendLine("            }");
        sb.AppendLine("            else { sb.Append('\\uFFFD'); i++; }");
        sb.AppendLine("        }");
        sb.AppendLine("        return sb.ToString();");
        sb.AppendLine("    }");

        // Single-char helpers (Tier 0 only — used by bare metal path)
        sb.AppendLine("    public static long UniToCce(long u) {");
        sb.AppendLine("        return _fromUni.TryGetValue((int)u, out int c) ? c : 68;");
        sb.AppendLine("    }");
        sb.AppendLine("    public static long CceToUni(long b) {");
        sb.AppendLine("        return (b >= 0 && b < 128) ? _toUni[(int)b] : 65533;");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();
        return sb.ToString();
    }

    static string FormatRow(int[] table, int start, int count)
    {
        var parts = new string[count];
        for (int i = 0; i < count; i++)
            parts[i] = table[start + i].ToString();
        bool isLast = (start + count) >= table.Length;
        return string.Join(", ", parts) + (isLast ? "" : ",");
    }
}
