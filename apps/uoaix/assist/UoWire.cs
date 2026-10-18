// The 1.25.32 wire, host side: the client keystream, the server Huffman code and packet framing.
// No using directives: a script appends this file to its own C# source and compiles both as one unit.

// POL LoginCrypt::Decrypt_Old, client 1.25.32: the login and the game connection both XOR with this
// stream, seeded by the connection's first 4 bytes (the game connection's seed is the 0x8C key).
public sealed class UoKeystream {
    ulong low, high;
    public UoKeystream(uint seed) {
        ulong s = seed, m = 0xFFFFFFFFUL;
        low = ((((s ^ m) ^ 0x1357UL) << 16) | ((s ^ 0xFFFFAAAAUL) & 65535UL)) & m;
        high = ((s ^ 0x43210000UL) >> 16) | (((s ^ m) ^ 0xABCDFFFFUL) & 4294901760UL);
    }
    public byte Apply(byte b) {
        byte r = (byte)(b ^ (byte)(low & 255));
        ulong next = (((low >> 1) | (high << 31)) ^ 0x026950C6UL) & 0xFFFFFFFFUL;
        high = (((high >> 1) | (low << 31)) ^ 0x389DE58CUL) & 0xFFFFFFFFUL; low = next;
        return r;
    }
}

// The server compresses each packet as one unit: every byte's code MSB first, then symbol 256, then zero
// bits to the byte boundary. A unit therefore always ends on a byte boundary.
public sealed class UoHuffman {
    readonly int[] bits = new int[257], codes = new int[257];
    readonly System.Collections.Generic.Dictionary<long, int> lookup = new System.Collections.Generic.Dictionary<long, int>();
    public UoHuffman(int[] book) {
        if (book.Length != 514) throw new System.ArgumentException("Huffman codebook has " + book.Length + " values, not 514");
        for (int i = 0; i <= 256; i++) { bits[i] = book[i * 2]; codes[i] = book[i * 2 + 1]; lookup[((long)bits[i] << 32) | (uint)codes[i]] = i; }
    }
    public byte[] Encode(byte[] packet) {
        var o = new System.Collections.Generic.List<byte>(packet.Length * 11 / 8 + 2); int acc = 0, n = 0;
        for (int i = 0; i <= packet.Length; i++) {
            int sym = i < packet.Length ? packet[i] : 256;
            for (int b = bits[sym] - 1; b >= 0; b--) { acc = (acc << 1) | ((codes[sym] >> b) & 1); if (++n == 8) { o.Add((byte)acc); acc = 0; n = 0; } }
        }
        if (n > 0) o.Add((byte)(acc << (8 - n)));
        return o.ToArray();
    }
    public Decoder NewDecoder() { return new Decoder(this); }
    public sealed class Decoder {
        readonly UoHuffman h; int nbits; long value; readonly System.Collections.Generic.List<byte> unit = new System.Collections.Generic.List<byte>();
        public bool Desync;
        internal Decoder(UoHuffman h) { this.h = h; }
        // Calls done(unit, end) for every unit completed in buf[off..off+n); end is the index just past the
        // unit's last byte. Desync is set, and the partial unit dropped, when no code matches within 11 bits.
        public void Feed(byte[] buf, int off, int n, System.Action<byte[], int> done) {
            for (int i = off; i < off + n; i++) {
                for (int b = 7; b >= 0; b--) {
                    value = (value << 1) | (uint)((buf[i] >> b) & 1); nbits++; int sym;
                    if (h.lookup.TryGetValue(((long)nbits << 32) | (uint)value, out sym)) {
                        nbits = 0; value = 0;
                        if (sym == 256) { var u = unit.ToArray(); unit.Clear(); done(u, i + 1); break; }
                        unit.Add((byte)sym);
                    } else if (nbits > 11) { Desync = true; nbits = 0; value = 0; unit.Clear(); }
                }
            }
        }
    }
}

// Splits a plain byte stream into whole packets by CLIENT.EXE's length table (go-lengths in
// GameOpcode.codex): 0 is a variable packet whose 3-byte header carries the length in bytes 1-2, and an
// opcode past the table's end cannot be framed.
public sealed class UoFramer {
    readonly int[] lengths; byte[] buf = new byte[4096]; int used;
    public string Desync;
    public UoFramer(int[] lengths) { this.lengths = lengths; }
    public int Buffered { get { return used; } }
    public byte[] TakeBuffered() { var r = new byte[used]; System.Array.Copy(buf, r, used); used = 0; return r; }
    public void Feed(byte[] src, int off, int n, System.Action<byte[]> packet) {
        if (Desync != null) return;
        if (used + n > buf.Length) { var g = new byte[System.Math.Max(buf.Length * 2, used + n)]; System.Array.Copy(buf, g, used); buf = g; }
        System.Array.Copy(src, off, buf, used, n); used += n;
        int at = 0;
        while (at < used) {
            int op = buf[at];
            if (op >= lengths.Length) { Desync = "opcode " + op.ToString("X2") + " past the length table"; break; }
            int len = lengths[op];
            if (len == 0) { if (used - at < 3) break; len = (buf[at + 1] << 8) | buf[at + 2]; if (len < 3) { Desync = "opcode " + op.ToString("X2") + " variable length " + len; break; } }
            if (used - at < len) break;
            var p = new byte[len]; System.Array.Copy(buf, at, p, 0, len); at += len; packet(p);
        }
        if (at > 0) { System.Array.Copy(buf, at, buf, 0, used - at); used -= at; }
    }
}

public static class UoHex {
    public static string Of(byte[] b, int off, int n) { return System.BitConverter.ToString(b, off, n).Replace("-", ""); }
    public static string Of(byte[] b) { return Of(b, 0, b.Length); }
}
