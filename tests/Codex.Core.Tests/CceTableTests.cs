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
    public void Unmapped_characters_encode_to_zero()
    {
        // CJK character — not in Tier 0
        string cjk = "\u4E16"; // 世
        string encoded = CceTable.Encode(cjk);
        Assert.Equal(1, encoded.Length);
        Assert.Equal(0, (int)encoded[0]);
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
