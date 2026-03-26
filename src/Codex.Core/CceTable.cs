namespace Codex.Core;

/// <summary>
/// Codex Character Encoding (CCE) Tier 0 — the single source of truth.
///
/// Every CCE lookup table in the project (compile-time encoding in the emitter,
/// runtime preamble in generated code, CLI encode command, tests) derives from
/// this array. Do not duplicate it — reference it.
///
/// Layout:
///   0-2:     Whitespace (NUL, LF, space)
///   3-12:    Digits (0-9)
///   13-38:   Lowercase letters (frequency-sorted: e,t,a,o,i,n,s,h,r,d,l,c,u,m,w,f,g,y,p,b,v,k,j,x,q,z)
///   39-64:   Uppercase letters (same frequency order)
///   65-75:   Prose punctuation (. , ! ? : ; ' " - ( ))
///   76-80:   Operators (+ = * &lt; &gt;)
///   81-93:   Syntax (/ @ # &amp; _ \ | [ ] { } ~ `)
///   94-112:  Accented Latin
///   113-127: Cyrillic
/// </summary>
public static class CceTable
{
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

    /// <summary>Convert a Unicode string to CCE-encoded string.
    /// Unmapped characters become '?' (CCE 68) instead of NUL to avoid silent corruption.</summary>
    public static string Encode(string unicode)
    {
        char[] result = new char[unicode.Length];
        for (int i = 0; i < unicode.Length; i++)
        {
            int u = unicode[i];
            result[i] = FromUnicode.TryGetValue(u, out int cce) ? (char)cce : (char)ReplacementCce;
        }
        return new string(result);
    }

    /// <summary>Convert a CCE-encoded string to Unicode.</summary>
    public static string Decode(string cce)
    {
        char[] result = new char[cce.Length];
        for (int i = 0; i < cce.Length; i++)
        {
            int b = cce[i];
            result[i] = (b >= 0 && b < 128) ? (char)ToUnicode[b] : '\uFFFD';
        }
        return new string(result);
    }

    /// <summary>Convert a single Unicode code point to CCE byte.
    /// Unmapped characters return '?' (CCE 68) instead of NUL.</summary>
    public static long UnicharToCce(long unicode)
    {
        return FromUnicode.TryGetValue((int)unicode, out int cce) ? cce : ReplacementCce;
    }

    /// <summary>Convert a single CCE byte to Unicode code point.</summary>
    public static long CceToUnichar(long cce)
    {
        return (cce >= 0 && cce < 128) ? ToUnicode[(int)cce] : 65533;
    }

    /// <summary>
    /// Generate the C# source for the _Cce runtime class (emitted into compiled programs).
    /// Single source — both reference and self-hosted emitters should call this or match it.
    /// </summary>
    public static string GenerateRuntimeSource()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("static class _Cce {");
        sb.AppendLine("    static readonly int[] _toUni = {");

        // Emit table rows matching the category layout
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
        sb.AppendLine("    static readonly Dictionary<int, int> _fromUni = new();");
        sb.AppendLine("    static _Cce() { for (int i = 0; i < 128; i++) _fromUni[_toUni[i]] = i; }");
        sb.AppendLine("    public static string FromUnicode(string s) {");
        sb.AppendLine("        var cs = new char[s.Length];");
        sb.AppendLine("        for (int i = 0; i < s.Length; i++) {");
        sb.AppendLine("            int u = s[i];");
        sb.AppendLine("            cs[i] = _fromUni.TryGetValue(u, out int c) ? (char)c : (char)68;");
        sb.AppendLine("        }");
        sb.AppendLine("        return new string(cs);");
        sb.AppendLine("    }");
        sb.AppendLine("    public static string ToUnicode(string s) {");
        sb.AppendLine("        var cs = new char[s.Length];");
        sb.AppendLine("        for (int i = 0; i < s.Length; i++) {");
        sb.AppendLine("            int b = s[i];");
        sb.AppendLine("            cs[i] = (b >= 0 && b < 128) ? (char)_toUni[b] : '\\uFFFD';");
        sb.AppendLine("        }");
        sb.AppendLine("        return new string(cs);");
        sb.AppendLine("    }");
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
