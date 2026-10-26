#nullable enable
using System;
using System.Collections.Generic;

// The 1.25.32 wire, host side: the client keystream, the server Huffman code and packet framing.

// POL LoginCrypt::Decrypt_Old, client 1.25.32: the login and the game connection both XOR with this
// stream, seeded by the connection's first 4 bytes (the game connection's seed is the 0x8C key).

namespace Uoaix.Assist;

public sealed class UoKeystream
{
    ulong m_low, m_high;
    public UoKeystream(uint seed)
    {
        ulong s = seed, m = 0xFFFFFFFFUL;
        m_low = ((((s ^ m) ^ 0x1357UL) << 16) | ((s ^ 0xFFFFAAAAUL) & 65535UL)) & m;
        m_high = ((s ^ 0x43210000UL) >> 16) | (((s ^ m) ^ 0xABCDFFFFUL) & 4294901760UL);
    }
    public byte Apply(byte b)
    {
        byte r = (byte)(b ^ (byte)(m_low & 255));
        ulong next = (((m_low >> 1) | (m_high << 31)) ^ 0x026950C6UL) & 0xFFFFFFFFUL;
        m_high = (((m_high >> 1) | (m_low << 31)) ^ 0x389DE58CUL) & 0xFFFFFFFFUL;
        m_low = next;
        return r;
    }
}

// The server compresses each packet as one unit: every byte's code MSB first, then symbol 256, then zero
// bits to the byte boundary. A unit therefore always ends on a byte boundary.
// The book is refused unless it is a complete prefix code of at most MaxBits bits a code: every bit sequence then
// decodes within MaxBits, so the decoder cannot lose its place.
public sealed class UoHuffman
{
    public const int MaxBits = 11;
    readonly int[] m_bits = new int[257], m_codes = new int[257];
    ValueMap<long, int> m_lookup = ValueMap<long, int>.Empty;
    public UoHuffman(int[] book)
    {
        if (book.Length != 514)
            throw new ArgumentException("Huffman codebook has " + book.Length + " values, not 514");
        long kraft = 0;
        for (int i = 0; i <= 256; i++)
        {
            m_bits[i] = book[i * 2];
            m_codes[i] = book[i * 2 + 1];
            if (m_bits[i] < 1 || m_bits[i] > MaxBits)
                throw new ArgumentException("Huffman symbol " + i + " has " + m_bits[i] + " bits, not 1 to " + MaxBits);
            if (m_codes[i] < 0 || m_codes[i] >= 1 << m_bits[i])
                throw new ArgumentException("Huffman symbol " + i + " code " + m_codes[i] + " does not fit "
                    + m_bits[i] + " bits");
            if (m_lookup.ContainsKey(((long)m_bits[i] << 32) | (uint)m_codes[i]))
                throw new ArgumentException("Huffman symbol " + i + " repeats another's code");
            m_lookup = m_lookup.Set(((long)m_bits[i] << 32) | (uint)m_codes[i], i);
            kraft += 1L << (MaxBits - m_bits[i]);
        }
        for (int i = 0; i <= 256; i++)
        {
            for (int n = 1; n < m_bits[i]; n++)
                if (m_lookup.ContainsKey(((long)n << 32) | (uint)(m_codes[i] >> (m_bits[i] - n))))
                    throw new ArgumentException("Huffman symbol " + i + " begins with another's code");
        }

        if (kraft != 1L << MaxBits)
            throw new ArgumentException("Huffman codebook is not complete (Kraft sum " + kraft + "/" + (1L << MaxBits)
                + ")");
    }
    public byte[] Encode(byte[] packet)
    {
        List<byte> o = new(packet.Length * 11 / 8 + 2);
        int acc = 0, n = 0;
        for (int i = 0; i <= packet.Length; i++)
        {
            int sym = i < packet.Length ? packet[i] : 256;
            for (int b = m_bits[sym] - 1; b >= 0; b--)
            {
                acc = (acc << 1) | ((m_codes[sym] >> b) & 1);
                if (++n == 8)
                {
                    o.Add((byte)acc);
                    acc = 0;
                    n = 0;
                }
            }
        }
        if (n > 0)
            o.Add((byte)(acc << (8 - n)));
        return o.ToArray();
    }
    public Decoder NewDecoder()
    {
        return new Decoder(this);
    }
    public sealed class Decoder
    {
        readonly UoHuffman m_h; int m_nbits; long m_value; readonly List<byte> m_unit = new();
        internal Decoder(UoHuffman h)
        {
            m_h = h;
        }
        // Calls done(unit, end) for every unit completed in buf[off..off+n); end is the index just past the
        // unit's last byte.
        public void Feed(byte[] buf, int off, int n, Action<byte[], int> done)
        {
            for (int i = off; i < off + n; i++)
            {
                for (int b = 7; b >= 0; b--)
                {
                    m_value = (m_value << 1) | (uint)((buf[i] >> b) & 1);
                    m_nbits++;
                    int sym;
                    if (m_h.m_lookup.TryGet(((long)m_nbits << 32) | (uint)m_value, out sym))
                    {
                        m_nbits = 0;
                        m_value = 0;
                        if (sym == 256)
                        {
                            byte[] u = m_unit.ToArray();
                            m_unit.Clear();
                            done(u, i + 1);
                            break;
                        }
                        m_unit.Add((byte)sym);
                    }
                }
            }
        }
    }
}

// Splits a plain byte stream into whole packets by CLIENT.EXE's length table (go-lengths in
// GameOpcode.codex): 0 is a variable packet whose 3-byte header carries the length in bytes 1-2, and an
// opcode past the table's end cannot be framed.
public sealed class UoFramer
{
    readonly int[] m_lengths; byte[] m_buf = new byte[4096]; int m_used;
    public string Desync = null!;
    public UoFramer(int[] lengths)
    {
        m_lengths = lengths;
    }
    public int Buffered => m_used;
    public byte[] TakeBuffered()
    {
        byte[] r = new byte[m_used];
        Array.Copy(m_buf, r, m_used);
        m_used = 0;
        return r;
    }
    public void Feed(byte[] src, int off, int n, Action<byte[]> packet)
    {
        if (Desync != null)
            return;
        if (m_used + n > m_buf.Length)
        {
            byte[] g = new byte[Math.Max(m_buf.Length * 2, m_used + n)];
            Array.Copy(m_buf, g, m_used);
            m_buf = g;
        }
        Array.Copy(src, off, m_buf, m_used, n);
        m_used += n;
        int at = 0;
        while (at < m_used)
        {
            int op = m_buf[at];
            if (op >= m_lengths.Length)
            {
                Desync = "opcode " + op.ToString("X2") + " past the length table";
                break;
            }
            int len = m_lengths[op];
            if (len == 0)
            {
                if (m_used - at < 3)
                    break;
                len = (m_buf[at + 1] << 8) | m_buf[at + 2];
                if (len < 3)
                {
                    Desync = "opcode " + op.ToString("X2") + " variable length " + len;
                    break;
                }
            }
            if (m_used - at < len)
                break;
            byte[] p = new byte[len];
            Array.Copy(m_buf, at, p, 0, len);
            at += len;
            packet(p);
        }
        if (at > 0)
        {
            Array.Copy(m_buf, at, m_buf, 0, m_used - at);
            m_used -= at;
        }
    }
}

public static class UoHex
{
    public static string Of(byte[] b, int off, int n)
    {
        return BitConverter.ToString(b, off, n).Replace("-", "");
    }
    public static string Of(byte[] b)
    {
        return Of(b, 0, b.Length);
    }
}
