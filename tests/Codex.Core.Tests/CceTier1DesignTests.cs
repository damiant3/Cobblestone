using Xunit;

namespace Codex.Core.Tests;

/// <summary>
/// Design spike: validates the CCE Tier 1 multi-byte encoding format
/// before it's implemented in production code.
///
/// Tier 1 uses the same self-synchronizing framing as UTF-8:
///   110xxxxx 10xxxxxx  →  11 data bits  →  2,048 code points (0-2047)
///
/// These tests prove the byte format works: encode, decode, roundtrip,
/// self-synchronization, and script block identification.
/// </summary>
public class CceTier1DesignTests
{
    // Encode a Tier 1 code point (0-2047) into a 2-byte sequence.
    static byte[] EncodeTier1(int codePoint)
    {
        if (codePoint < 0 || codePoint > 2047)
        {
            throw new System.ArgumentOutOfRangeException(nameof(codePoint));
        }

        byte b1 = (byte)(0xC0 | (codePoint >> 6));   // 110xxxxx
        byte b2 = (byte)(0x80 | (codePoint & 0x3F));  // 10xxxxxx
        return [b1, b2];
    }

    // Decode a 2-byte Tier 1 sequence back to a code point.
    static int DecodeTier1(byte b1, byte b2)
    {
        if ((b1 & 0xE0) != 0xC0)
        {
            throw new System.FormatException("Not a Tier 1 start byte");
        }

        if ((b2 & 0xC0) != 0x80)
        {
            throw new System.FormatException("Not a continuation byte");
        }

        return ((b1 & 0x1F) << 6) | (b2 & 0x3F);
    }

    // Check if a byte is a character-start byte (not a continuation).
    static bool IsStartByte(byte b) => (b & 0xC0) != 0x80;

    // Determine tier from the first byte.
    static int TierOf(byte b) => b switch
    {
        < 0x80 => 0,                           // 0xxxxxxx
        < 0xC0 => -1,                          // 10xxxxxx (continuation)
        < 0xE0 => 1,                           // 110xxxxx
        < 0xF0 => 2,                           // 1110xxxx
        _ => 3                                 // 11110xxx
    };

    [Fact]
    public void Roundtrip_all_tier1_code_points()
    {
        for (int cp = 0; cp < 2048; cp++)
        {
            byte[] encoded = EncodeTier1(cp);
            Assert.Equal(2, encoded.Length);
            int decoded = DecodeTier1(encoded[0], encoded[1]);
            Assert.Equal(cp, decoded);
        }
    }

    [Fact]
    public void Start_byte_has_110_prefix()
    {
        for (int cp = 0; cp < 2048; cp++)
        {
            byte[] encoded = EncodeTier1(cp);
            Assert.Equal(0xC0, encoded[0] & 0xE0); // top 3 bits = 110
        }
    }

    [Fact]
    public void Continuation_byte_has_10_prefix()
    {
        for (int cp = 0; cp < 2048; cp++)
        {
            byte[] encoded = EncodeTier1(cp);
            Assert.Equal(0x80, encoded[1] & 0xC0); // top 2 bits = 10
        }
    }

    [Fact]
    public void Self_synchronization_from_any_position()
    {
        // Encode three Tier 1 characters, then verify we can find the next
        // character boundary from any byte position in the stream.
        int[] cps = [100, 500, 1500];
        byte[] stream = [
            ..EncodeTier1(cps[0]),
            ..EncodeTier1(cps[1]),
            ..EncodeTier1(cps[2])
        ];

        // From every position, scan forward to next start byte
        for (int pos = 0; pos < stream.Length; pos++)
        {
            int next = pos;
            while (next < stream.Length && !IsStartByte(stream[next]))
            {
                next++;
            }

            // next should be at a valid character boundary (0, 2, or 4)
            Assert.True(next % 2 == 0 || next == stream.Length,
                $"From position {pos}, found start at {next} which is not a character boundary");
        }
    }

    [Fact]
    public void Tier_identification_from_first_byte()
    {
        // Tier 0: any byte 0x00-0x7F
        Assert.Equal(0, TierOf(0x00));
        Assert.Equal(0, TierOf(0x7F));

        // Tier 1: start byte 0xC0-0xDF
        byte[] t1 = EncodeTier1(0);
        Assert.Equal(1, TierOf(t1[0]));
        byte[] t1max = EncodeTier1(2047);
        Assert.Equal(1, TierOf(t1max[0]));

        // Continuation bytes are not starts
        Assert.Equal(-1, TierOf(t1[1]));
    }

    [Fact]
    public void No_overlap_with_tier0()
    {
        // Every Tier 1 encoded byte must be >= 0x80 (no collision with Tier 0 single bytes)
        for (int cp = 0; cp < 2048; cp++)
        {
            byte[] encoded = EncodeTier1(cp);
            Assert.True(encoded[0] >= 0x80, $"Tier 1 start byte {encoded[0]:X2} overlaps Tier 0 range");
            Assert.True(encoded[1] >= 0x80, $"Tier 1 continuation byte {encoded[1]:X2} overlaps Tier 0 range");
        }
    }

    [Fact]
    public void Script_block_identification_via_bit_mask()
    {
        // Design: Tier 1 code points are organized in power-of-2 blocks.
        // Script identification is a range check on the code point, not a table lookup.

        // Latin Extended block: 0x000-0x07F (128 entries)
        int latin = 0x050;
        Assert.True(latin >= 0x000 && latin < 0x080);

        // Cyrillic block: 0x080-0x0FF
        int cyrillic = 0x0A0;
        Assert.True(cyrillic >= 0x080 && cyrillic < 0x100);

        // CJK top-512 block: 0x400-0x5FF
        int cjk = 0x450;
        Assert.True(cjk >= 0x400 && cjk < 0x600);

        // Japanese block: 0x600-0x6FF
        int japanese = 0x650;
        Assert.True(japanese >= 0x600 && japanese < 0x700);

        // Script ID from high bits: (codepoint >> 7) gives block index
        Assert.Equal(0, 0x050 >> 7);  // Latin extended
        Assert.Equal(1, 0x0A0 >> 7);  // Cyrillic
        Assert.Equal(8, 0x450 >> 7);  // CJK
        Assert.Equal(12, 0x650 >> 7); // Japanese
    }

    [Fact]
    public void Mixed_tier0_tier1_stream()
    {
        // A realistic stream: Tier 0 bytes + Tier 1 sequences interleaved
        byte[] stream = [
            0x15, // Tier 0: 'a' (CCE)
            ..EncodeTier1(0x450), // Tier 1: a CJK character
            0x16, // Tier 0: 'o' (CCE)
            ..EncodeTier1(0x0A0), // Tier 1: a Cyrillic character
        ];

        // Walk the stream, decode each character
        int pos = 0;
        int charCount = 0;
        while (pos < stream.Length)
        {
            int tier = TierOf(stream[pos]);
            if (tier == 0) { pos += 1; charCount++; }
            else if (tier == 1) { pos += 2; charCount++; }
            else
            {
                Assert.Fail($"Unexpected tier {tier} at position {pos}");
            }
        }
        Assert.Equal(4, charCount);
        Assert.Equal(stream.Length, pos); // consumed exactly
    }
}
