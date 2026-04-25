using System.Security.Cryptography;
using System.Text;

namespace Codex.Core;

public readonly record struct ContentHash : IComparable<ContentHash>, IEquatable<ContentHash>
{
    readonly byte[] m_bytes;

    ContentHash(byte[] bytes)
    {
        m_bytes = bytes;
    }

    public ReadOnlySpan<byte> Bytes => m_bytes;

    public static ContentHash Of(ReadOnlySpan<byte> data)
    {
        byte[] hash = SHA256.HashData(data);
        return new ContentHash(hash);
    }

    public static ContentHash Of(string text)
    {
        return Of(Encoding.UTF8.GetBytes(text));
    }

    public string ToHex()
    {
        return Convert.ToHexString(m_bytes).ToLowerInvariant();
    }

    public static ContentHash FromHex(string hex)
    {
        return new ContentHash(Convert.FromHexString(hex));
    }

    public string ToShortHex() => ToHex()[..16];

    public int CompareTo(ContentHash other)
    {
        for (int i = 0; i < m_bytes.Length && i < other.m_bytes.Length; i++)
        {
            int cmp = m_bytes[i].CompareTo(other.m_bytes[i]);
            if (cmp != 0)
                return cmp;
        }
        return m_bytes.Length.CompareTo(other.m_bytes.Length);
    }

    public bool Equals(ContentHash other)
    {
        return m_bytes.AsSpan().SequenceEqual(other.m_bytes);
    }

    public override int GetHashCode()
    {
        if (m_bytes is null || m_bytes.Length < 4)
            return 0;
        return BitConverter.ToInt32(m_bytes, 0);
    }

    public override string ToString() => ToShortHex();
}
