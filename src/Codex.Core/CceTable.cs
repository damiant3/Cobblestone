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
///   0x100-0x1FF: Greek + typographic symbols
///   0x200-0x3FF: Arabic + Devanagari
///   0x400-0x5FF: CJK top-512 (frequency-sorted Chinese)
///   0x600-0x6FF: Japanese (Hiragana + Katakana + punctuation)
///   0x700-0x7FF: Korean (Jamo + frequent syllables)
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

        // Block 2 (0x100-0x1FF): Greek
        int[] greekLower =
        {
            945, 946, 947, 948, 949, 950, 951, 952, 953, 954,             // α β γ δ ε ζ η θ ι κ
            955, 956, 957, 958, 959, 960, 961, 963, 964, 965,             // λ μ ν ξ ο π ρ σ τ υ
            966, 967, 968, 969, 962                                        // φ χ ψ ω ς (final sigma)
        };
        int[] greekUpper =
        {
            913, 914, 915, 916, 917, 918, 919, 920, 921, 922,             // Α Β Γ Δ Ε Ζ Η Θ Ι Κ
            923, 924, 925, 926, 927, 928, 929, 931, 932, 933,             // Λ Μ Ν Ξ Ο Π Ρ Σ Τ Υ
            934, 935, 936, 937                                             // Φ Χ Ψ Ω
        };
        int[] greekSymbols =
        {
            8364, 8482, 8240, 8230, 8211, 8212, 8216, 8217,               // € ™ ‰ … – — ' '
            8220, 8221, 8224, 8225, 8226, 8249, 8250                      // " " † ‡ • ‹ ›
        };

        slot = 0x100;
        foreach (int cp in greekLower) t[slot++] = cp;
        foreach (int cp in greekUpper) t[slot++] = cp;
        foreach (int cp in greekSymbols) t[slot++] = cp;

        // Block 3 (0x200-0x3FF): Arabic + Devanagari
        // Arabic: most frequent letters
        int[] arabic =
        {
            1575, 1576, 1578, 1579, 1580, 1581, 1582, 1583,               // ا ب ت ث ج ح خ د
            1584, 1585, 1586, 1587, 1588, 1589, 1590, 1591,               // ذ ر ز س ش ص ض ط
            1592, 1593, 1594, 1601, 1602, 1603, 1604, 1605,               // ظ ع غ ف ق ك ل م
            1606, 1607, 1608, 1610, 1569, 1570, 1571, 1572,               // ن ه و ي ء آ أ ؤ
            1573, 1574, 1577, 1609, 1611, 1612, 1613, 1614,               // إ ئ ة ى ً ٌ ٍ َ
            1615, 1616, 1617, 1618                                         // ُ ِ ّ ْ
        };
        // Devanagari: vowels + most frequent consonants
        int[] devanagari =
        {
            2309, 2310, 2311, 2312, 2313, 2314, 2319, 2320,               // अ आ इ ई उ ऊ ए ऐ
            2323, 2324, 2325, 2326, 2327, 2328, 2330, 2331,               // ओ औ क ख ग घ च छ
            2332, 2333, 2335, 2336, 2337, 2338, 2339, 2340,               // ज झ ट ठ ड ढ ण त
            2341, 2342, 2343, 2344, 2346, 2347, 2348, 2349,               // थ द ध न प फ ब भ
            2350, 2351, 2352, 2354, 2357, 2358, 2359, 2360,               // म य र ल व श ष स
            2361, 2366, 2367, 2368, 2369, 2370, 2375, 2376,               // ह ा ि ी ु ू े ै
            2379, 2380, 2381, 2306, 2307                                   // ो ौ ् ं ः
        };

        slot = 0x200;
        foreach (int cp in arabic) t[slot++] = cp;
        foreach (int cp in devanagari) t[slot++] = cp;

        // Block 4-5 (0x400-0x5FF): CJK (most frequent Chinese characters, deduplicated)
        int[] cjkFrequent =
        {
            30340, 19968, 26159, 19981, 20102, 20154, 25105, 22312,       // 的 一 是 不 了 人 我 在
            26377, 20182, 36825, 22823, 26469, 20197, 22269, 20013,       // 有 他 这 大 来 以 国 中
            21040, 20250, 23601, 23398, 35828, 22320, 19978, 37324,       // 到 会 就 学 说 地 上 里
            23545, 29983, 26102, 21487, 21457, 22810, 32463, 34892,       // 对 生 时 可 发 多 经 行
            24037, 35201, 22905, 27861, 32780, 20316, 29992, 37117,       // 工 要 女 没 给 作 用 都
            21035, 20027, 21407, 25991, 21270, 36824, 24403, 24180,       // 别 主 原 文 化 进 当 年
            20160, 21147, 22914, 24515, 25919, 24773, 21516, 25104,       // 事 力 如 已 政 情 同 成
            27599, 26041, 21069, 20986, 20840, 21482, 31038, 38271,       // 比 方 前 出 全 只 社 问
            23450, 31181, 20851, 26412, 30475, 28857, 26032, 20844,       // 定 种 关 本 看 点 新 公
            24320, 20294, 35748, 21518, 35770, 26524, 33258, 22240,       // 开 但 论 后 认 果 自 因
            22825, 20854, 27492, 28982, 27665, 38388, 36947, 20004,       // 天 其 每 然 步 间 道 两
            30334, 24605, 26376, 34987, 21592, 24819, 29305, 30524,       // 真 想 月 着 又 很 特 目
            20449, 25163, 26126, 24213, 35774, 37096, 31561, 30693,       // 信 手 明 建 设 部 等 理
            28216, 20998, 23383, 22238, 20307, 22909, 26356, 23478,       // 清 分 字 回 体 好 最 实
            21518, 36824, 24180, 20160, 22810, 21040, 22823, 25104,       // 后 进 年 事 多 到 大 成
            23478, 27861, 22269, 20316, 29992, 20182, 24403, 22312,       // 实 没 国 作 用 他 当 在
            25105, 35201, 19981, 20102, 20154, 26159, 19968, 30340,       // 我 要 不 了 人 是 一 的
            20197, 26377, 22320, 23398, 26032, 21147, 22914, 20844,       // 以 有 地 学 新 力 如 公
            36335, 20043, 22763, 21326, 36164, 20301, 22797, 24847,       // 还 之 少 北 过 位 女 得
            33021, 24050, 23478, 24515, 26412, 23450, 22320, 25104,       // 能 已 实 已 本 定 地 成
            38271, 31181, 20851, 27861, 21035, 20027, 25919, 22905,       // 问 种 关 没 别 主 政 女
            24773, 21516, 32463, 34892, 24037, 35828, 37117, 21270,       // 情 同 经 行 工 说 都 化
            29983, 26102, 23601, 21457, 23545, 37324, 19978, 20250,       // 生 时 就 发 对 里 上 会
            26469, 20013, 30475, 28857, 24320, 20294, 35748, 35770,       // 来 中 看 点 开 但 论 认
            22240, 26524, 33258, 22825, 28982, 36947, 24605, 27665        // 因 果 自 天 然 道 想 步
        };

        // Deduplicate: only insert first occurrence of each code point
        var cjkSeen = new HashSet<int>();
        slot = 0x400;
        foreach (int cp in cjkFrequent)
        {
            if (slot >= 0x600) break;
            if (cp != 0 && cjkSeen.Add(cp)) t[slot++] = cp;
        }

        // Block 6 (0x600-0x6FF): Japanese Hiragana + Katakana
        // Hiragana: U+3041-U+3093 (83 chars)
        slot = 0x600;
        for (int cp = 0x3041; cp <= 0x3093; cp++) t[slot++] = cp;
        // Katakana: U+30A1-U+30F6 (86 chars)
        for (int cp = 0x30A1; cp <= 0x30F6; cp++) t[slot++] = cp;
        // Japanese punctuation
        int[] japanesePunct = { 0x3001, 0x3002, 0x300C, 0x300D, 0x3005, 0x30FC, 0x30FB };  // 、。「」々ー・
        foreach (int cp in japanesePunct) t[slot++] = cp;

        // Block 7 (0x700-0x7FF): Korean Jamo + frequent syllables
        // Hangul Jamo consonants: U+1100-U+1112 (19)
        slot = 0x700;
        for (int cp = 0x1100; cp <= 0x1112; cp++) t[slot++] = cp;
        // Hangul Jamo vowels: U+1161-U+1175 (21)
        for (int cp = 0x1161; cp <= 0x1175; cp++) t[slot++] = cp;
        // Hangul Jamo final consonants: U+11A8-U+11C2 (27)
        for (int cp = 0x11A8; cp <= 0x11C2; cp++) t[slot++] = cp;
        // Most frequent Hangul syllables (deduplicated)
        int[] koreanFrequent =
        {
            44032, 45208, 45796, 46972, 47560, 48148, 49324, 50500,       // 가 나 다 라 마 바 사 아
            51088, 52264, 52852, 53440, 54028, 54616, 45768, 46020,       // 자 차 카 타 파 하 는 다(dup)
            47484, 50640, 51060, 51032, 51012, 51008, 44163, 48320,       // 를 에 이 의 을 은 각 보
            49373, 51068, 51204, 51201, 45908, 47196, 46108               // 서 있 저 적 되 로 된
        };
        var korSeen = new HashSet<int>();
        foreach (int cp in koreanFrequent)
        {
            if (cp != 0 && korSeen.Add(cp)) t[slot++] = cp;
        }

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

    /// <summary>Determine tier from a CCE byte: 0=Tier 0, 1=Tier 1 start, 2=Tier 2, 3=Tier 3, -1=continuation.</summary>
    public static int TierOf(int b) => b switch
    {
        < 0x80 => 0,     // 0xxxxxxx  — Tier 0 (1 byte, table lookup)
        < 0xC0 => -1,    // 10xxxxxx  — continuation
        < 0xE0 => 1,     // 110xxxxx  — Tier 1 start (2 bytes, table lookup)
        < 0xF0 => 2,     // 1110xxxx  — Tier 2 start (3 bytes, direct BMP)
        _ => 3            // 11110xxx  — Tier 3 start (4 bytes, supplementary)
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
    /// Tier 0: single byte (table lookup). Tier 1: 2 bytes (table lookup).
    /// Tier 2: 3 bytes (direct BMP code point). Tier 3: 4 bytes (supplementary).
    /// No character is ever lost — full Unicode coverage.</summary>
    public static string Encode(string unicode)
    {
        string normalized = NormalizeUnicode(unicode);
        var sb = new System.Text.StringBuilder(normalized.Length);
        for (int idx = 0; idx < normalized.Length; idx++)
        {
            int u = normalized[idx];

            // Handle surrogate pairs for supplementary characters (U+10000+)
            if (char.IsHighSurrogate((char)u) && idx + 1 < normalized.Length
                && char.IsLowSurrogate(normalized[idx + 1]))
            {
                int full = char.ConvertToUtf32((char)u, normalized[idx + 1]);
                idx++; // consume low surrogate
                // Tier 3: 11110xxx 10xxxxxx 10xxxxxx 10xxxxxx
                sb.Append((char)(0xF0 | (full >> 18)));
                sb.Append((char)(0x80 | ((full >> 12) & 0x3F)));
                sb.Append((char)(0x80 | ((full >> 6) & 0x3F)));
                sb.Append((char)(0x80 | (full & 0x3F)));
                continue;
            }

            if (FromUnicode.TryGetValue(u, out int cce))
            {
                sb.Append((char)cce);                              // Tier 0: 1 byte
            }
            else if (Tier1FromUnicode.TryGetValue(u, out int t1cp))
            {
                sb.Append((char)(0xC0 | (t1cp >> 6)));             // Tier 1: 2 bytes
                sb.Append((char)(0x80 | (t1cp & 0x3F)));
            }
            else
            {
                // Tier 2: 1110xxxx 10xxxxxx 10xxxxxx (direct Unicode code point)
                sb.Append((char)(0xE0 | (u >> 12)));
                sb.Append((char)(0x80 | ((u >> 6) & 0x3F)));
                sb.Append((char)(0x80 | (u & 0x3F)));
            }
        }
        return sb.ToString();
    }

    /// <summary>Convert a CCE-encoded string to Unicode. Full coverage: Tier 0-3.</summary>
    public static string Decode(string cce)
    {
        var sb = new System.Text.StringBuilder(cce.Length);
        int i = 0;
        while (i < cce.Length)
        {
            int b = cce[i];
            if (b < 0x80)
            {
                // Tier 0: single byte → table lookup
                sb.Append((char)ToUnicode[b]);
                i++;
            }
            else if (b >= 0xC0 && b < 0xE0 && i + 1 < cce.Length && (cce[i + 1] & 0xC0) == 0x80)
            {
                // Tier 1: 2 bytes → table lookup
                int cp = ((b & 0x1F) << 6) | (cce[i + 1] & 0x3F);
                int uni = (cp < Tier1ToUnicode.Length && Tier1ToUnicode[cp] != 0)
                    ? Tier1ToUnicode[cp] : 0xFFFD;
                sb.Append((char)uni);
                i += 2;
            }
            else if (b >= 0xE0 && b < 0xF0 && i + 2 < cce.Length
                     && (cce[i + 1] & 0xC0) == 0x80 && (cce[i + 2] & 0xC0) == 0x80)
            {
                // Tier 2: 3 bytes → direct Unicode code point (BMP)
                int uni = ((b & 0x0F) << 12) | ((cce[i + 1] & 0x3F) << 6) | (cce[i + 2] & 0x3F);
                sb.Append((char)uni);
                i += 3;
            }
            else if (b >= 0xF0 && b < 0xF8 && i + 3 < cce.Length
                     && (cce[i + 1] & 0xC0) == 0x80 && (cce[i + 2] & 0xC0) == 0x80
                     && (cce[i + 3] & 0xC0) == 0x80)
            {
                // Tier 3: 4 bytes → supplementary Unicode (surrogate pair in C#)
                int full = ((b & 0x07) << 18) | ((cce[i + 1] & 0x3F) << 12)
                         | ((cce[i + 2] & 0x3F) << 6) | (cce[i + 3] & 0x3F);
                if (full >= 0x10000 && full <= 0x10FFFF)
                    sb.Append(char.ConvertFromUtf32(full));
                else
                    sb.Append('\uFFFD');
                i += 4;
            }
            else
            {
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

        // FromUnicode — full Tier 0-3
        sb.AppendLine("    public static string FromUnicode(string s) {");
        sb.AppendLine("        s = s.Replace(\"\\t\", \"  \").Replace(\"\\r\", \"\");");
        sb.AppendLine("        var sb = new System.Text.StringBuilder(s.Length);");
        sb.AppendLine("        for (int i = 0; i < s.Length; i++) {");
        sb.AppendLine("            int u = s[i];");
        sb.AppendLine("            if (char.IsHighSurrogate((char)u) && i+1 < s.Length && char.IsLowSurrogate(s[i+1])) {");
        sb.AppendLine("                int full = char.ConvertToUtf32((char)u, s[++i]);");
        sb.AppendLine("                sb.Append((char)(0xF0|(full>>18))); sb.Append((char)(0x80|((full>>12)&0x3F)));");
        sb.AppendLine("                sb.Append((char)(0x80|((full>>6)&0x3F))); sb.Append((char)(0x80|(full&0x3F)));");
        sb.AppendLine("            }");
        sb.AppendLine("            else if (_fromUni.TryGetValue(u, out int b)) sb.Append((char)b);");
        sb.AppendLine("            else if (_t1FromUni.TryGetValue(u, out int cp)) {");
        sb.AppendLine("                sb.Append((char)(0xC0 | (cp >> 6)));");
        sb.AppendLine("                sb.Append((char)(0x80 | (cp & 0x3F)));");
        sb.AppendLine("            }");
        sb.AppendLine("            else { sb.Append((char)(0xE0|(u>>12))); sb.Append((char)(0x80|((u>>6)&0x3F))); sb.Append((char)(0x80|(u&0x3F))); }");
        sb.AppendLine("        }");
        sb.AppendLine("        return sb.ToString();");
        sb.AppendLine("    }");

        // ToUnicode — full Tier 0-3
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
        sb.AppendLine("            else if (b >= 0xE0 && b < 0xF0 && i+2 < s.Length) {");
        sb.AppendLine("                sb.Append((char)(((b&0x0F)<<12)|((s[i+1]&0x3F)<<6)|(s[i+2]&0x3F))); i += 3;");
        sb.AppendLine("            }");
        sb.AppendLine("            else if (b >= 0xF0 && i+3 < s.Length) {");
        sb.AppendLine("                int full = ((b&0x07)<<18)|((s[i+1]&0x3F)<<12)|((s[i+2]&0x3F)<<6)|(s[i+3]&0x3F);");
        sb.AppendLine("                sb.Append(full >= 0x10000 ? char.ConvertFromUtf32(full) : \"\\uFFFD\"); i += 4;");
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
