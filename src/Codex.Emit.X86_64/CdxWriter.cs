namespace Codex.Emit.X86_64;

/// <summary>
/// CDX1 binary format writer per docs/Designs/Codex.OS/CodexBinary.md.
/// Produces the native Codex binary format with trust metadata.
/// Development mode: crypto fields (content_hash, author_key, signature)
/// are zero-filled until crypto primitives are available (post-MM4).
/// </summary>
static class CdxWriter
{
    const int FixedHeaderSize = 0xE0; // 224 bytes

    // Flag constants
    public const ushort FlagBareMetal = 1;
    public const ushort FlagNeedsHeap = 2;
    public const ushort FlagNeedsStackGuard = 4;
    public const ushort FlagHasProofs = 8;

    /// <summary>
    /// Build a CDX1 binary with the given sections and metadata.
    /// Crypto fields are zero-filled (development mode).
    /// </summary>
    public static byte[] Build(
        ushort flags,
        byte[] textSection,
        byte[] rodataSection,
        long entryOffset,
        uint stackSize,
        uint heapSize)
    {
        // Layout: [header 224B][text][rodata]
        // No capabilities, no proofs, no fact hashes in development mode.
        long textOffset = FixedHeaderSize;
        long textSize = textSection.Length;
        long rodataOffset = textOffset + textSize;
        long rodataSize = rodataSection.Length;

        // Capabilities and proofs: empty, offsets point to header end
        long capOffset = FixedHeaderSize;
        long capSize = 0;
        long proofOffset = FixedHeaderSize;
        long proofSize = 0;

        int totalSize = FixedHeaderSize + textSection.Length + rodataSection.Length;
        MemoryStream ms = new(totalSize);
        BinaryWriter w = new(ms);

        // ── Magic ──
        w.Write((byte)'C'); w.Write((byte)'D'); w.Write((byte)'X'); w.Write((byte)'1');

        // ── format_version ──
        w.Write((ushort)1);

        // ── flags ──
        w.Write(flags);

        // ── content_hash (32 bytes, dev mode: zeros) ──
        w.Write(new byte[32]);

        // ── author_key (32 bytes, dev mode: zeros) ──
        w.Write(new byte[32]);

        // ── signature (64 bytes, dev mode: zeros) ──
        w.Write(new byte[64]);

        // ── Section offsets and sizes ──
        w.Write(capOffset);       // capabilities_offset
        w.Write(capSize);         // capabilities_size
        w.Write(proofOffset);     // proofs_offset
        w.Write(proofSize);       // proofs_size
        w.Write(textOffset);      // text_offset
        w.Write(textSize);        // text_size
        w.Write(rodataOffset);    // rodata_offset
        w.Write(rodataSize);      // rodata_size
        w.Write(entryOffset);     // entry_point

        // ── Resource requirements ──
        w.Write(stackSize);       // stack_size
        w.Write(heapSize);        // heap_size
        w.Write((ushort)0);       // trust_threshold (dev mode: 0)
        w.Write((ushort)0);       // reserved
        w.Write((uint)0);         // fact_hash_count

        // ── Sections ──
        w.Write(textSection);
        w.Write(rodataSection);

        return ms.ToArray();
    }
}
