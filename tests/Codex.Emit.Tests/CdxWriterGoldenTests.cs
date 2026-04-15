using Codex.Emit.X86_64;
using Xunit;

namespace Codex.Emit.Tests;

/// <summary>
/// Golden reference tests for the C# CDX writer. These establish the byte-exact
/// reference that the Codex port (Codex.Codex/Emit/CdxWriter.codex) must match.
/// Validates the CDX1 format per docs/Designs/Codex.OS/CodexBinary.md.
/// </summary>
public class CdxWriterGoldenTests
{
    // ═══════════════════════════════════════════════════════════
    // Header structure
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Magic()
    {
        byte[] cdx = CdxWriter.Build(0, new byte[4], Array.Empty<byte>(), 0, 4096, 0);
        Assert.Equal((byte)'C', cdx[0]);
        Assert.Equal((byte)'D', cdx[1]);
        Assert.Equal((byte)'X', cdx[2]);
        Assert.Equal((byte)'1', cdx[3]);
    }

    [Fact]
    public void FormatVersion()
    {
        byte[] cdx = CdxWriter.Build(0, new byte[4], Array.Empty<byte>(), 0, 4096, 0);
        Assert.Equal(1, BitConverter.ToUInt16(cdx, 0x04));
    }

    [Fact]
    public void Flags_BareMetal()
    {
        byte[] cdx = CdxWriter.Build(CdxWriter.FlagBareMetal, new byte[4], Array.Empty<byte>(), 0, 4096, 0);
        Assert.Equal(1, BitConverter.ToUInt16(cdx, 0x06));
    }

    [Fact]
    public void Flags_BareMetalPlusHeap()
    {
        ushort flags = CdxWriter.FlagBareMetal | CdxWriter.FlagNeedsHeap;
        byte[] cdx = CdxWriter.Build(flags, new byte[4], Array.Empty<byte>(), 0, 4096, 1048576);
        Assert.Equal(3, BitConverter.ToUInt16(cdx, 0x06));
    }

    [Fact]
    public void ContentHash_DevModeZeros()
    {
        byte[] cdx = CdxWriter.Build(0, new byte[4], Array.Empty<byte>(), 0, 4096, 0);
        // content_hash at 0x08, 32 bytes, all zeros
        for (int i = 0x08; i < 0x28; i++)
        {
            Assert.Equal(0, cdx[i]);
        }
    }

    [Fact]
    public void AuthorKey_DevModeZeros()
    {
        byte[] cdx = CdxWriter.Build(0, new byte[4], Array.Empty<byte>(), 0, 4096, 0);
        // author_key at 0x28, 32 bytes, all zeros
        for (int i = 0x28; i < 0x48; i++)
        {
            Assert.Equal(0, cdx[i]);
        }
    }

    [Fact]
    public void Signature_DevModeZeros()
    {
        byte[] cdx = CdxWriter.Build(0, new byte[4], Array.Empty<byte>(), 0, 4096, 0);
        // signature at 0x48, 64 bytes, all zeros
        for (int i = 0x48; i < 0x88; i++)
        {
            Assert.Equal(0, cdx[i]);
        }
    }

    // ═══════════════════════════════════════════════════════════
    // Section offsets and sizes
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void CapabilitiesOffset()
    {
        byte[] cdx = CdxWriter.Build(0, new byte[100], new byte[20], 0, 4096, 0);
        // capabilities_offset at 0x88 = 224 (header end, empty table)
        Assert.Equal(224L, BitConverter.ToInt64(cdx, 0x88));
        // capabilities_size at 0x90 = 0
        Assert.Equal(0L, BitConverter.ToInt64(cdx, 0x90));
    }

    [Fact]
    public void ProofsOffset()
    {
        byte[] cdx = CdxWriter.Build(0, new byte[100], new byte[20], 0, 4096, 0);
        // proofs_offset at 0x98 = 224
        Assert.Equal(224L, BitConverter.ToInt64(cdx, 0x98));
        // proofs_size at 0xA0 = 0
        Assert.Equal(0L, BitConverter.ToInt64(cdx, 0xA0));
    }

    [Fact]
    public void TextOffset()
    {
        byte[] cdx = CdxWriter.Build(0, new byte[100], new byte[20], 0, 4096, 0);
        // text_offset at 0xA8 = 224
        Assert.Equal(224L, BitConverter.ToInt64(cdx, 0xA8));
        // text_size at 0xB0 = 100
        Assert.Equal(100L, BitConverter.ToInt64(cdx, 0xB0));
    }

    [Fact]
    public void RodataOffset()
    {
        byte[] cdx = CdxWriter.Build(0, new byte[100], new byte[20], 0, 4096, 0);
        // rodata_offset at 0xB8 = 224 + 100 = 324
        Assert.Equal(324L, BitConverter.ToInt64(cdx, 0xB8));
        // rodata_size at 0xC0 = 20
        Assert.Equal(20L, BitConverter.ToInt64(cdx, 0xC0));
    }

    [Fact]
    public void EntryPoint()
    {
        byte[] cdx = CdxWriter.Build(0, new byte[100], new byte[20], 42, 4096, 0);
        // entry_point at 0xC8 = 42
        Assert.Equal(42L, BitConverter.ToInt64(cdx, 0xC8));
    }

    // ═══════════════════════════════════════════════════════════
    // Resource requirements
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void StackSize()
    {
        byte[] cdx = CdxWriter.Build(0, new byte[4], Array.Empty<byte>(), 0, 32768, 0);
        // stack_size at 0xD0 = 32768
        Assert.Equal(32768u, BitConverter.ToUInt32(cdx, 0xD0));
    }

    [Fact]
    public void HeapSize()
    {
        byte[] cdx = CdxWriter.Build(0, new byte[4], Array.Empty<byte>(), 0, 4096, 1048576);
        // heap_size at 0xD4 = 1048576
        Assert.Equal(1048576u, BitConverter.ToUInt32(cdx, 0xD4));
    }

    [Fact]
    public void TrustThreshold_DevModeZero()
    {
        byte[] cdx = CdxWriter.Build(0, new byte[4], Array.Empty<byte>(), 0, 4096, 0);
        // trust_threshold at 0xD8 = 0
        Assert.Equal(0, BitConverter.ToUInt16(cdx, 0xD8));
    }

    [Fact]
    public void Reserved_Zero()
    {
        byte[] cdx = CdxWriter.Build(0, new byte[4], Array.Empty<byte>(), 0, 4096, 0);
        Assert.Equal(0, BitConverter.ToUInt16(cdx, 0xDA));
    }

    [Fact]
    public void FactHashCount_Zero()
    {
        byte[] cdx = CdxWriter.Build(0, new byte[4], Array.Empty<byte>(), 0, 4096, 0);
        Assert.Equal(0u, BitConverter.ToUInt32(cdx, 0xDC));
    }

    // ═══════════════════════════════════════════════════════════
    // Section content
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void TextContent()
    {
        byte[] text = new byte[] { 0xCC, 0xCC, 0xCC, 0xCC };
        byte[] cdx = CdxWriter.Build(0, text, Array.Empty<byte>(), 0, 4096, 0);
        // text starts at offset 224
        Assert.Equal(0xCC, cdx[224]);
        Assert.Equal(0xCC, cdx[225]);
        Assert.Equal(0xCC, cdx[226]);
        Assert.Equal(0xCC, cdx[227]);
    }

    [Fact]
    public void RodataContent()
    {
        byte[] text = new byte[100];
        byte[] rodata = new byte[] { 0xDE, 0xAD };
        byte[] cdx = CdxWriter.Build(0, text, rodata, 0, 4096, 0);
        // rodata starts at 224 + 100 = 324
        Assert.Equal(0xDE, cdx[324]);
        Assert.Equal(0xAD, cdx[325]);
    }

    [Fact]
    public void TotalSize()
    {
        byte[] text = new byte[100];
        byte[] rodata = new byte[20];
        byte[] cdx = CdxWriter.Build(0, text, rodata, 0, 4096, 0);
        // 224 + 100 + 20 = 344
        Assert.Equal(344, cdx.Length);
    }

    [Fact]
    public void TotalSize_EmptyRodata()
    {
        byte[] text = new byte[50];
        byte[] cdx = CdxWriter.Build(0, text, Array.Empty<byte>(), 0, 4096, 0);
        Assert.Equal(274, cdx.Length);
    }

    [Fact]
    public void HeaderSize_Exactly224()
    {
        byte[] cdx = CdxWriter.Build(0, Array.Empty<byte>(), Array.Empty<byte>(), 0, 0, 0);
        // With empty text and rodata, total = 224
        Assert.Equal(224, cdx.Length);
    }
}
