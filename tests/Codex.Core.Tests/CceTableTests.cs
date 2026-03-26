using System.Text.RegularExpressions;
using Codex.Core;
using Xunit;

namespace Codex.Core.Tests;

public class CceTableTests
{
    [Fact]
    public void Table_has_128_entries()
    {
        Assert.Equal(128, CceTable.ToUnicode.Length);
    }

    [Fact]
    public void Table_is_bijective()
    {
        // Every CCE byte maps to a unique Unicode code point
        var seen = new HashSet<int>();
        for (int i = 0; i < CceTable.ToUnicode.Length; i++)
        {
            int u = CceTable.ToUnicode[i];
            Assert.True(seen.Add(u), $"Duplicate Unicode code point {u} at CCE positions");
        }
    }

    [Fact]
    public void FromUnicode_is_inverse_of_ToUnicode()
    {
        for (int cce = 0; cce < 128; cce++)
        {
            int unicode = CceTable.ToUnicode[cce];
            Assert.True(CceTable.FromUnicode.ContainsKey(unicode),
                $"Unicode {unicode} (CCE {cce}) not in FromUnicode dictionary");
            Assert.Equal(cce, CceTable.FromUnicode[unicode]);
        }
    }

    [Fact]
    public void Encode_decode_roundtrip_ascii()
    {
        string original = "Hello, World! 42 + x = y";
        string encoded = CceTable.Encode(original);
        string decoded = CceTable.Decode(encoded);
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void Encode_decode_roundtrip_all_mapped_ascii()
    {
        // Only characters that are in the CCE table should roundtrip
        var sb = new System.Text.StringBuilder();
        for (char c = ' '; c <= '~'; c++)
        {
            if (CceTable.FromUnicode.ContainsKey(c))
                sb.Append(c);
        }
        string original = sb.ToString();
        Assert.True(original.Length > 80, $"Expected 80+ mapped ASCII chars, got {original.Length}");
        string decoded = CceTable.Decode(CceTable.Encode(original));
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void Encode_decode_roundtrip_accented()
    {
        // Only use accented chars that are in Tier 0
        string original = "café résumé";
        string decoded = CceTable.Decode(CceTable.Encode(original));
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void Unmapped_characters_encode_to_replacement()
    {
        // CJK character — not in Tier 0, should become '?' (CCE 68) not NUL
        string cjk = "\u4E16"; // 世
        string encoded = CceTable.Encode(cjk);
        Assert.Equal(1, encoded.Length);
        Assert.Equal(CceTable.ReplacementCce, (int)encoded[0]);
    }

    [Fact]
    public void Classification_ranges_are_contiguous()
    {
        // Whitespace: 0-2
        for (int b = 0; b <= 2; b++)
            Assert.True(CceTable.ToUnicode[b] == 0 || char.IsWhiteSpace((char)CceTable.ToUnicode[b]) || CceTable.ToUnicode[b] == 0,
                $"CCE {b} should be whitespace");

        // Digits: 3-12
        for (int b = 3; b <= 12; b++)
            Assert.True(char.IsDigit((char)CceTable.ToUnicode[b]),
                $"CCE {b} (U+{CceTable.ToUnicode[b]}) should be a digit");

        // Lowercase: 13-38
        for (int b = 13; b <= 38; b++)
            Assert.True(char.IsLower((char)CceTable.ToUnicode[b]),
                $"CCE {b} (U+{CceTable.ToUnicode[b]}) should be lowercase");

        // Uppercase: 39-64
        for (int b = 39; b <= 64; b++)
            Assert.True(char.IsUpper((char)CceTable.ToUnicode[b]),
                $"CCE {b} (U+{CceTable.ToUnicode[b]}) should be uppercase");
    }

    [Fact]
    public void UnicharToCce_and_CceToUnichar_roundtrip()
    {
        for (int cce = 0; cce < 128; cce++)
        {
            long unicode = CceTable.CceToUnichar(cce);
            long back = CceTable.UnicharToCce(unicode);
            Assert.Equal(cce, (int)back);
        }
    }

    [Fact]
    public void GenerateRuntimeSource_contains_all_table_values()
    {
        // Verify the generated _Cce runtime class contains the same values
        string source = CceTable.GenerateRuntimeSource();
        var numbers = Regex.Matches(source, @"(?<=[\s,{])(\d+)(?=[,\s}])")
            .Select(m => int.Parse(m.Value))
            .ToList();

        // The table should appear as a contiguous run of 128 values
        // Find the start by looking for the NUL entry (0) followed by LF (10) and space (32)
        int start = -1;
        for (int i = 0; i < numbers.Count - 2; i++)
        {
            if (numbers[i] == 0 && numbers[i + 1] == 10 && numbers[i + 2] == 32)
            {
                start = i;
                break;
            }
        }
        Assert.True(start >= 0, "Could not find CCE table header (0, 10, 32) in generated runtime source");

        for (int cce = 0; cce < 128; cce++)
        {
            Assert.Equal(CceTable.ToUnicode[cce], numbers[start + cce]);
        }
    }

    [Fact]
    public void SelfHosted_emitter_table_matches_CceTable()
    {
        // Parse the self-hosted emitter's string-concatenated table and verify
        // it matches CceTable.ToUnicode. This catches the triple-copy sync bug.
        string emitterPath = Path.Combine(
            FindRepoRoot(), "Codex.Codex", "Emit", "CSharpEmitter.codex");

        if (!File.Exists(emitterPath))
        {
            // Skip if running outside repo context
            return;
        }

        string source = File.ReadAllText(emitterPath);

        // Find the emit-cce-runtime function and extract all numbers from
        // the _toUni array literal between the first "{" and "};"
        int funcStart = source.IndexOf("emit-cce-runtime");
        Assert.True(funcStart >= 0, "Could not find emit-cce-runtime in CSharpEmitter.codex");

        int arrayStart = source.IndexOf("_toUni = {", funcStart);
        Assert.True(arrayStart >= 0, "Could not find _toUni array in emit-cce-runtime");

        int arrayEnd = source.IndexOf("};", arrayStart);
        Assert.True(arrayEnd >= 0, "Could not find end of _toUni array");

        string arrayText = source.Substring(arrayStart, arrayEnd - arrayStart);
        var numbers = Regex.Matches(arrayText, @"\b(\d+)\b")
            .Select(m => int.Parse(m.Value))
            .ToList();

        // Filter out noise: we expect exactly 128 values that match the table.
        // The numbers include "128" from the loop, so find the table values specifically.
        // The table starts with 0, 10, 32 and we know its length.
        int start = -1;
        for (int i = 0; i < numbers.Count - 2; i++)
        {
            if (numbers[i] == 0 && numbers[i + 1] == 10 && numbers[i + 2] == 32)
            {
                start = i;
                break;
            }
        }
        Assert.True(start >= 0,
            "Could not find CCE table header (0, 10, 32) in self-hosted emitter");
        Assert.True(start + 128 <= numbers.Count,
            $"Self-hosted emitter table too short: found {numbers.Count - start} values starting at index {start}, expected 128");

        for (int cce = 0; cce < 128; cce++)
        {
            Assert.Equal(CceTable.ToUnicode[cce], numbers[start + cce]);
        }
    }

    [Fact]
    public void Encode_normalizes_tab_to_spaces()
    {
        string input = "hello\tworld";
        string encoded = CceTable.Encode(input);
        string decoded = CceTable.Decode(encoded);
        Assert.Equal("hello  world", decoded);
    }

    [Fact]
    public void Encode_strips_carriage_return()
    {
        string input = "line1\r\nline2";
        string encoded = CceTable.Encode(input);
        string decoded = CceTable.Decode(encoded);
        Assert.Equal("line1\nline2", decoded);
    }

    [Fact]
    public void NormalizeUnicode_mixed_input()
    {
        // TAB→2 spaces, CR stripped, everything else unchanged
        string input = "a\tb\r\nc\td";
        string result = CceTable.NormalizeUnicode(input);
        Assert.Equal("a  b\nc  d", result);
    }

    [Fact]
    public void NormalizeUnicode_fast_path_no_alloc()
    {
        // No tabs or CRs — should return the same string instance
        string input = "hello world";
        string result = CceTable.NormalizeUnicode(input);
        Assert.Same(input, result);
    }

    [Fact]
    public void GenerateRuntimeSource_includes_normalization()
    {
        string source = CceTable.GenerateRuntimeSource();
        Assert.Contains("Replace(\"\\t\"", source);
        Assert.Contains("Replace(\"\\r\"", source);
    }

    // ── Tier 1 production tests ──────────────────────────────────────

    [Fact]
    public void Tier1_table_has_2048_entries()
    {
        Assert.Equal(2048, CceTable.Tier1ToUnicode.Length);
    }

    [Fact]
    public void Tier1_table_is_bijective()
    {
        var seen = new HashSet<int>();
        for (int i = 0; i < CceTable.Tier1ToUnicode.Length; i++)
        {
            int u = CceTable.Tier1ToUnicode[i];
            if (u == 0) continue; // unmapped slot
            Assert.True(seen.Add(u), $"Duplicate Unicode {u} at Tier 1 position {i}");
        }
    }

    [Fact]
    public void Tier1_does_not_overlap_tier0()
    {
        for (int i = 0; i < CceTable.Tier1ToUnicode.Length; i++)
        {
            int u = CceTable.Tier1ToUnicode[i];
            if (u == 0) continue;
            Assert.False(CceTable.FromUnicode.ContainsKey(u),
                $"Unicode {u} at Tier 1 position {i} also exists in Tier 0");
        }
    }

    [Fact]
    public void Tier1_from_unicode_is_inverse()
    {
        foreach (var kv in CceTable.Tier1FromUnicode)
        {
            Assert.Equal(kv.Key, CceTable.Tier1ToUnicode[kv.Value]);
        }
    }

    [Fact]
    public void Tier1_count_is_positive()
    {
        Assert.True(CceTable.Tier1Count > 100, $"Expected 100+ Tier 1 entries, got {CceTable.Tier1Count}");
    }

    [Fact]
    public void Tier1_latin_extended_block_populated()
    {
        // Block 0 (0x000-0x07F) should have Latin Extended characters
        int count = 0;
        for (int i = 0; i < 0x80; i++)
            if (CceTable.Tier1ToUnicode[i] != 0) count++;
        Assert.True(count >= 80, $"Latin Extended block should have 80+ entries, got {count}");
    }

    [Fact]
    public void Tier1_cyrillic_block_populated()
    {
        // Block 1 (0x080-0x0FF) should have Cyrillic characters
        int count = 0;
        for (int i = 0x80; i < 0x100; i++)
            if (CceTable.Tier1ToUnicode[i] != 0) count++;
        Assert.True(count >= 50, $"Cyrillic block should have 50+ entries, got {count}");
    }

    [Fact]
    public void Encode_decode_roundtrip_tier1_latin()
    {
        // ß is not in Tier 0, should roundtrip via Tier 1
        string original = "Straße";
        string encoded = CceTable.Encode(original);
        // ß should produce 2 CCE bytes, so encoded is longer than original
        Assert.True(encoded.Length > original.Length,
            $"Tier 1 encoding should be longer: input {original.Length}, encoded {encoded.Length}");
        string decoded = CceTable.Decode(encoded);
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void Encode_decode_roundtrip_tier1_cyrillic()
    {
        // Mix Tier 0 Cyrillic (а,о,е...) with Tier 1 (б,г,ж...)
        string original = "абвгд";  // а(T0), б(T1), в(T0), г(T1), д(T0)
        string encoded = CceTable.Encode(original);
        string decoded = CceTable.Decode(encoded);
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void Encode_mixed_tier0_tier1_stream()
    {
        // ASCII (Tier 0) + accented (Tier 0) + ß (Tier 1) + Ø (Tier 1)
        string original = "café ß Ø";
        string encoded = CceTable.Encode(original);
        string decoded = CceTable.Decode(encoded);
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void Tier1_encoded_bytes_are_valid_framing()
    {
        // Every Tier 1 character should encode as exactly 2 bytes:
        // start byte (0xC0-0xDF) + continuation (0x80-0xBF)
        foreach (var kv in CceTable.Tier1FromUnicode)
        {
            string s = new string((char)kv.Key, 1);
            string encoded = CceTable.Encode(s);
            Assert.Equal(2, encoded.Length);
            int b1 = encoded[0];
            int b2 = encoded[1];
            Assert.True(b1 >= 0xC0 && b1 < 0xE0, $"Start byte {b1:X2} not in 0xC0-0xDF for U+{kv.Key:X4}");
            Assert.True(b2 >= 0x80 && b2 < 0xC0, $"Continuation byte {b2:X2} not in 0x80-0xBF for U+{kv.Key:X4}");
        }
    }

    [Fact]
    public void Tier1_self_synchronization()
    {
        // Encode a mixed stream, verify valid byte sequence structure:
        // Tier 0 bytes stand alone, Tier 1 start bytes are followed by exactly one continuation
        string input = "aßbгc";  // T0 ASCII, T1 Latin, T0, T1 Cyrillic, T0
        string encoded = CceTable.Encode(input);
        int i = 0;
        int charCount = 0;
        while (i < encoded.Length)
        {
            int b = encoded[i];
            int tier = CceTable.TierOf(b);
            if (tier == 0) { i++; charCount++; }
            else if (tier == 1)
            {
                Assert.True(i + 1 < encoded.Length, $"Tier 1 start at position {i} without continuation");
                Assert.Equal(-1, CceTable.TierOf(encoded[i + 1]));
                i += 2; charCount++;
            }
            else Assert.Fail($"Unexpected byte {b:X2} (tier {tier}) at position {i}");
        }
        Assert.Equal(5, charCount); // a, ß, b, г, c
    }

    [Fact]
    public void TierOf_classifies_correctly()
    {
        Assert.Equal(0, CceTable.TierOf(0x00));    // Tier 0 min
        Assert.Equal(0, CceTable.TierOf(0x7F));    // Tier 0 max
        Assert.Equal(-1, CceTable.TierOf(0x80));   // Continuation min
        Assert.Equal(-1, CceTable.TierOf(0xBF));   // Continuation max
        Assert.Equal(1, CceTable.TierOf(0xC0));    // Tier 1 start min
        Assert.Equal(1, CceTable.TierOf(0xDF));    // Tier 1 start max
        Assert.Equal(2, CceTable.TierOf(0xE0));    // Future Tier 2
    }

    [Fact]
    public void Decode_handles_orphan_continuation_bytes()
    {
        // A continuation byte without a start should produce replacement
        string bad = new string(new[] { (char)0x80 });
        string decoded = CceTable.Decode(bad);
        Assert.Equal("\uFFFD", decoded);
    }

    [Fact]
    public void Decode_handles_truncated_tier1()
    {
        // A Tier 1 start byte at end of string (no continuation) → replacement
        string bad = new string(new[] { (char)0xC0 });
        string decoded = CceTable.Decode(bad);
        Assert.Equal("\uFFFD", decoded);
    }

    [Fact]
    public void Tier1_greek_block_populated()
    {
        int count = 0;
        for (int i = 0x100; i < 0x200; i++)
            if (CceTable.Tier1ToUnicode[i] != 0) count++;
        Assert.True(count >= 49, $"Greek block should have 49+ entries, got {count}");
    }

    [Fact]
    public void Tier1_japanese_block_populated()
    {
        int count = 0;
        for (int i = 0x600; i < 0x700; i++)
            if (CceTable.Tier1ToUnicode[i] != 0) count++;
        Assert.True(count >= 170, $"Japanese block should have 170+ entries, got {count}");
    }

    [Fact]
    public void Tier1_cjk_block_populated()
    {
        int count = 0;
        for (int i = 0x400; i < 0x600; i++)
            if (CceTable.Tier1ToUnicode[i] != 0) count++;
        Assert.True(count >= 100, $"CJK block should have 100+ entries, got {count}");
    }

    [Fact]
    public void Encode_decode_roundtrip_greek()
    {
        string original = "\u03B1\u03B2\u03B3"; // αβγ
        string encoded = CceTable.Encode(original);
        Assert.True(encoded.Length > original.Length);
        Assert.Equal(original, CceTable.Decode(encoded));
    }

    [Fact]
    public void Encode_decode_roundtrip_japanese()
    {
        string original = "\u3053\u3093\u306B\u3061\u306F"; // こんにちは
        string encoded = CceTable.Encode(original);
        Assert.True(encoded.Length > original.Length);
        Assert.Equal(original, CceTable.Decode(encoded));
    }

    [Fact]
    public void Encode_decode_roundtrip_arabic()
    {
        string original = "\u0627\u0644\u0633\u0644\u0627\u0645"; // السلام
        string encoded = CceTable.Encode(original);
        Assert.Equal(original, CceTable.Decode(encoded));
    }

    [Fact]
    public void Encode_decode_roundtrip_devanagari()
    {
        string original = "\u0928\u092E\u0938\u094D\u0924\u0947"; // नमस्ते
        string encoded = CceTable.Encode(original);
        Assert.Equal(original, CceTable.Decode(encoded));
    }

    [Fact]
    public void Tier1_total_coverage()
    {
        Assert.True(CceTable.Tier1Count >= 500,
            $"Expected 500+ total Tier 1 entries, got {CceTable.Tier1Count}");
    }

    [Fact]
    public void GenerateRuntimeSource_includes_tier1_tables()
    {
        string source = CceTable.GenerateRuntimeSource();
        Assert.Contains("_t1ToUni", source);
        Assert.Contains("_t1FromUni", source);
        Assert.Contains("0xC0", source);  // Tier 1 encoding in FromUnicode
        Assert.Contains("0x1F", source);  // Tier 1 decoding mask in ToUnicode
    }

    static string FindRepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, "Codex.Codex")))
                return dir;
            dir = Path.GetDirectoryName(dir)!;
        }
        // Fallback: walk up from current directory
        dir = Directory.GetCurrentDirectory();
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, "Codex.Codex")))
                return dir;
            dir = Path.GetDirectoryName(dir)!;
        }
        return Directory.GetCurrentDirectory();
    }
}
