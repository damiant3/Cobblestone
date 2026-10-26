#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;

namespace Uoaix.Assist.Tests;

public static class WireTests
{
    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("wire: the real tables load, 180 lengths and 514 book values", RealTablesLoad),
        new TestCase("wire: the table fixes a step at 3, its answer at 3, its refusal at 8 and a drawing at 19",
        FixedLengthsGameLinkRelies),
        new TestCase("wire: 179 lengths are refused", Lengths179),
        new TestCase("wire: 181 lengths are refused", Lengths181),
        new TestCase("wire: 513 book values are refused", Book513),
        new TestCase("wire: 515 book values are refused", Book515),
        new TestCase("wire: a list with no head is refused", NoHead),
        new TestCase("wire: a list with no closing bracket is refused", NoClose),
        new TestCase("wire: a # at the list's end and a # before a non-hex are not values", HashEdges),
        new TestCase("wire: a hex run ending the list is read whole", HexAtEnd),
        new TestCase("huffman: a book of 513 values is refused", Huffman513),
        new TestCase("huffman: a code of 0 bits is refused", HuffmanZeroBits),
        new TestCase("huffman: a code of 12 bits is refused", HuffmanTwelveBits),
        new TestCase("huffman: a code too wide for its bits is refused", HuffmanTooWide),
        new TestCase("huffman: a negative code is refused", HuffmanNegative),
        new TestCase("huffman: a repeated code is refused", HuffmanRepeat),
        new TestCase("huffman: a code beginning with another is refused", HuffmanPrefix),
        new TestCase("huffman: an incomplete code is refused", HuffmanIncomplete),
        new TestCase("huffman: every byte round-trips, one unit each", EveryByteRoundTrips),
        new TestCase("huffman: an empty packet round-trips", EmptyRoundTrips),
        new TestCase("huffman: an encoding ending on a byte boundary has no pad byte", ExactBoundary),
        new TestCase("huffman: a unit split byte by byte decodes once, at its last byte", SplitUnit),
        new TestCase("huffman: two units in one read decode in order with their ends", TwoUnits),
        new TestCase("framer: a whole fixed packet comes out", FramerFixed),
        new TestCase("framer: a fixed packet split at every byte comes out once", FramerFixedSplit),
        new TestCase("framer: a variable header split at every byte comes out once", FramerVariableSplit),
        new TestCase("framer: a variable length of 2 is a desync", FramerVariableTooShort),
        new TestCase("framer: a variable length of 3 is a packet", FramerVariableThree),
        new TestCase("framer: the last opcode in the table frames", FramerLastOpcode),
        new TestCase("framer: the first opcode past the table is a desync, and the framer stays stopped",
        FramerPastTable),
        new TestCase("framer: more than 4096 bytes at once are framed", FramerGrows),
        new TestCase("framer: the buffered bytes are handed back and cleared", FramerTakeBuffered),
        new TestCase("hex: bytes print as upper-case pairs", HexPrints));

    public static ImmutableArray<TestCase> Cases => s_cases;

    static int[] Copy(ImmutableArray<int> values)
    {
        int[] copy = new int[values.Length];
        values.CopyTo(copy);
        return copy;
    }

    static int[] Book => Copy(FakeShard.Wire.Book);

    static int[] Lengths => Copy(FakeShard.Wire.Lengths);

    static string LengthsSource(int count)
    {
        List<string> values = new();
        for (int i = 0; i < count; i++)
            values.Add((i % 7).ToString());
        return "  go-lengths : List Integer = [" + string.Join(", ", values) + "]\n";
    }

    static string BookSource(int count)
    {
        List<string> values = new();
        for (int i = 0; i < count; i++)
            values.Add("#" + (i % 16).ToString("X"));
        return "gh-codebook : List Integer = [" + string.Join(" ", values) + "]";
    }

    static void RealTablesLoad()
    {
        Check.Equal(180, FakeShard.Wire.Lengths.Length, "lengths");
        Check.Equal(514, FakeShard.Wire.Book.Length, "book values");
    }

    // GameLink reads these packets at fixed offsets without a length check of its own.
    static void FixedLengthsGameLinkRelies()
    {
        Check.Equal(3, FakeShard.Wire.Lengths[0x02], "0x02");
        Check.Equal(3, FakeShard.Wire.Lengths[0x22], "0x22");
        Check.Equal(8, FakeShard.Wire.Lengths[0x21], "0x21");
        Check.Equal(19, FakeShard.Wire.Lengths[0x20], "0x20");
    }

    static void Lengths179()
    {
        InvalidDataException e = Check.Throws<InvalidDataException>(() => WireTables.ParseLengths(LengthsSource(179)),
            "ParseLengths");
        Check.Equal("go-lengths has 179 entries, not 180 (opcodes 00 to B3)", e.Message, "message");
    }

    static void Lengths181()
    {
        Check.Throws<InvalidDataException>(() => WireTables.ParseLengths(LengthsSource(181)), "ParseLengths");
    }

    static void Book513()
    {
        InvalidDataException e = Check.Throws<InvalidDataException>(() => WireTables.ParseBook(BookSource(513)),
            "ParseBook");
        Check.Equal("gh-codebook has 513 values, not 514", e.Message, "message");
    }

    static void Book515()
    {
        Check.Throws<InvalidDataException>(() => WireTables.ParseBook(BookSource(515)), "ParseBook");
    }

    static void NoHead()
    {
        Check.Throws<InvalidDataException>(() => WireTables.ParseLengths("go-lengths = [1, 2]"), "ParseLengths");
    }

    static void NoClose()
    {
        Check.Throws<InvalidDataException>(() => WireTables.ParseLengths("go-lengths : List Integer = [1, 2"),
            "ParseLengths");
    }

    static void HashEdges()
    {
        // 514 values, then a "#" before a space and a "#" as the list's last character: neither is a value.
        string source = BookSource(514);
        source = source.Substring(0, source.Length - 1) + " # #]";
        Check.Equal(514, WireTables.ParseBook(source).Length, "values");
    }

    static void HexAtEnd()
    {
        string source = BookSource(513);
        source = source.Substring(0, source.Length - 1) + " #ABC]";
        ImmutableArray<int> values = WireTables.ParseBook(source);
        Check.Equal(0xABC, values[513], "the last value");
    }

    static void Huffman513()
    {
        Check.Throws<ArgumentException>(() => new UoHuffman(new int[513]), "UoHuffman");
    }

    static void HuffmanZeroBits()
    {
        int[] book = Book;
        book[2 * 5] = 0;
        ArgumentException e = Check.Throws<ArgumentException>(() => new UoHuffman(book), "UoHuffman");
        Check.Equal("Huffman symbol 5 has 0 bits, not 1 to 11", e.Message, "message");
    }

    static void HuffmanTwelveBits()
    {
        int[] book = Book;
        book[2 * 5] = 12;
        ArgumentException e = Check.Throws<ArgumentException>(() => new UoHuffman(book), "UoHuffman");
        Check.Equal("Huffman symbol 5 has 12 bits, not 1 to 11", e.Message, "message");
    }

    static void HuffmanTooWide()
    {
        int[] book = Book;
        book[2 * 5 + 1] = 1 << book[2 * 5];
        ArgumentException e = Check.Throws<ArgumentException>(() => new UoHuffman(book), "UoHuffman");
        Check.True(e.Message.Contains("does not fit"), "message: " + e.Message);
    }

    static void HuffmanRepeat()
    {
        int[] book = Book;
        book[2 * 6] = book[2 * 5];
        book[2 * 6 + 1] = book[2 * 5 + 1];
        ArgumentException e = Check.Throws<ArgumentException>(() => new UoHuffman(book), "UoHuffman");
        Check.Equal("Huffman symbol 6 repeats another's code", e.Message, "message");
    }

    static int ShortSymbol(int[] book)
    {
        for (int i = 0; i <= 256; i++)
        {
            if (book[2 * i] <= 10)
                return i;
        }
        throw new CheckFailed("no symbol of 10 bits or fewer");
    }

    static void HuffmanPrefix()
    {
        int[] book = Book;
        int s = ShortSymbol(book);
        int other = s == 0 ? 1 : 0;
        book[2 * other] = book[2 * s] + 1;
        book[2 * other + 1] = book[2 * s + 1] << 1;
        ArgumentException e = Check.Throws<ArgumentException>(() => new UoHuffman(book), "UoHuffman");
        Check.True(e.Message.EndsWith("begins with another's code"), "message: " + e.Message);
    }

    static void HuffmanNegative()
    {
        int[] book = Book;
        book[2 * 5 + 1] = -1;
        ArgumentException e = Check.Throws<ArgumentException>(() => new UoHuffman(book), "UoHuffman");
        Check.True(e.Message.Contains("does not fit"), "message: " + e.Message);
    }

    static void HuffmanIncomplete()
    {
        int[] book = Book;
        int s = ShortSymbol(book);
        book[2 * s] += 1;
        book[2 * s + 1] <<= 1;
        ArgumentException e = Check.Throws<ArgumentException>(() => new UoHuffman(book), "UoHuffman");
        Check.True(e.Message.StartsWith("Huffman codebook is not complete"), "message: " + e.Message);
    }

    static List<(byte[] Unit, int End)> Decode(UoHuffman huff, byte[] coded, int chunk)
    {
        List<(byte[] Unit, int End)> units = new();
        UoHuffman.Decoder decoder = huff.NewDecoder();
        for (int at = 0; at < coded.Length; at += chunk)
        {
            int n = Math.Min(chunk, coded.Length - at);
            decoder.Feed(coded, at, n, (u, end) => units.Add((u, end)));
        }
        return units;
    }

    static void EveryByteRoundTrips()
    {
        UoHuffman huff = new(Book);
        for (int b = 0; b < 256; b++)
        {
            byte[] packet = { (byte)b, (byte)(255 - b) };
            List<(byte[] Unit, int End)> units = Decode(huff, huff.Encode(packet), 1 << 16);
            Check.Equal(1, units.Count, "units for byte " + b);
            Check.Bytes(packet, units[0].Unit, "byte " + b);
        }
    }

    static void EmptyRoundTrips()
    {
        UoHuffman huff = new(Book);
        List<(byte[] Unit, int End)> units = Decode(huff, huff.Encode(new byte[0]), 1 << 16);
        Check.Equal(1, units.Count, "units");
        Check.Equal(0, units[0].Unit.Length, "unit length");
    }

    static void ExactBoundary()
    {
        int[] book = Book;
        UoHuffman huff = new(book);
        for (int a = 0; a < 256; a++)
        {
            for (int b = 0; b < 256; b++)
            {
                int bits = book[2 * a] + book[2 * b] + book[2 * 256];
                if (bits % 8 != 0)
                    continue;
                byte[] coded = huff.Encode(new[] { (byte)a, (byte)b });
                Check.Equal(bits / 8, coded.Length, "bytes for " + a + "," + b);
                Check.Bytes(new[] { (byte)a, (byte)b }, Decode(huff, coded, 1 << 16)[0].Unit, "round trip");
                return;
            }
        }
        throw new CheckFailed("no two-byte packet ends on a byte boundary");
    }

    static void SplitUnit()
    {
        UoHuffman huff = new(Book);
        byte[] packet = { 0x1C, 0x00, 0x2D, 0xFF, 0xFF, 0xFF, 0xFF, 0x41, 0x42 };
        byte[] coded = huff.Encode(packet);
        List<(byte[] Unit, int End)> units = Decode(huff, coded, 1);
        Check.Equal(1, units.Count, "units");
        Check.Bytes(packet, units[0].Unit, "unit");
        Check.Equal(coded.Length, units[0].End, "end, as an index into the last chunk's buffer");
    }

    static void TwoUnits()
    {
        UoHuffman huff = new(Book);
        byte[] first = huff.Encode(new byte[] { 1, 2, 3 });
        byte[] second = huff.Encode(new byte[] { 4, 5 });
        byte[] both = new byte[first.Length + second.Length];
        first.CopyTo(both, 0);
        second.CopyTo(both, first.Length);
        List<(byte[] Unit, int End)> units = Decode(huff, both, both.Length);
        Check.Equal(2, units.Count, "units");
        Check.Bytes(new byte[] { 1, 2, 3 }, units[0].Unit, "the first");
        Check.Equal(first.Length, units[0].End, "the first's end");
        Check.Bytes(new byte[] { 4, 5 }, units[1].Unit, "the second");
        Check.Equal(both.Length, units[1].End, "the second's end");
    }

    static List<byte[]> Frame(UoFramer framer, byte[] bytes, int chunk)
    {
        List<byte[]> packets = new();
        for (int at = 0; at < bytes.Length; at += chunk)
            framer.Feed(bytes, at, Math.Min(chunk, bytes.Length - at), p => packets.Add(p));
        return packets;
    }

    static void FramerFixed()
    {
        List<byte[]> packets = Frame(new UoFramer(Lengths), new byte[] { 0x06, 1, 2, 3, 4 }, 64);
        Check.Equal(1, packets.Count, "packets");
        Check.Bytes(new byte[] { 0x06, 1, 2, 3, 4 }, packets[0], "packet");
    }

    static void FramerFixedSplit()
    {
        UoFramer framer = new(Lengths);
        List<byte[]> packets = Frame(framer, new byte[] { 0x06, 1, 2, 3, 4 }, 1);
        Check.Equal(1, packets.Count, "packets");
        Check.Equal(0, framer.Buffered, "left buffered");
    }

    static void FramerVariableSplit()
    {
        UoFramer framer = new(Lengths);
        byte[] say = { 0x03, 0x00, 0x0A, 0, 0, 0x5A, 0, 3, 0x41, 0 };
        List<byte[]> packets = Frame(framer, say, 1);
        Check.Equal(1, packets.Count, "packets");
        Check.Bytes(say, packets[0], "packet");
    }

    static void FramerVariableTooShort()
    {
        UoFramer framer = new(Lengths);
        Frame(framer, new byte[] { 0x03, 0x00, 0x02 }, 64);
        Check.Equal("opcode 03 variable length 2", framer.Desync, "Desync");
    }

    static void FramerVariableThree()
    {
        List<byte[]> packets = Frame(new UoFramer(Lengths), new byte[] { 0x03, 0x00, 0x03 }, 64);
        Check.Equal(1, packets.Count, "packets");
    }

    static void FramerLastOpcode()
    {
        int[] lengths = Lengths;
        int last = lengths.Length - 1;
        int len = lengths[last] == 0 ? 3 : lengths[last];
        byte[] packet = new byte[len];
        packet[0] = (byte)last;
        if (lengths[last] == 0)
            packet[2] = 3;
        UoFramer framer = new(lengths);
        Check.Equal(1, Frame(framer, packet, 64).Count, "packets");
        Check.Null(framer.Desync, "Desync");
    }

    static void FramerPastTable()
    {
        int[] lengths = Lengths;
        UoFramer framer = new(lengths);
        Frame(framer, new byte[] { (byte)lengths.Length, 0, 0 }, 64);
        Check.Equal("opcode B4 past the length table", framer.Desync, "Desync");
        Check.Equal(0, Frame(framer, new byte[] { 0x06, 1, 2, 3, 4 }, 64).Count, "packets after the desync");
    }

    static void FramerGrows()
    {
        byte[] bytes = new byte[5 * 1000];
        for (int i = 0; i < 1000; i++)
            bytes[i * 5] = 0x06;
        UoFramer framer = new(Lengths);
        Check.Equal(1000, Frame(framer, bytes, bytes.Length).Count, "packets");
        Check.Equal(0, framer.Buffered, "left buffered");
    }

    static void FramerTakeBuffered()
    {
        UoFramer framer = new(Lengths);
        Frame(framer, new byte[] { 0x06, 1, 2 }, 64);
        Check.Equal(3, framer.Buffered, "buffered");
        Check.Bytes(new byte[] { 0x06, 1, 2 }, framer.TakeBuffered(), "taken");
        Check.Equal(0, framer.Buffered, "buffered after the take");
    }

    static void HexPrints()
    {
        Check.Equal("00AB0F", UoHex.Of(new byte[] { 0x00, 0xAB, 0x0F }), "hex");
        Check.Equal("AB", UoHex.Of(new byte[] { 0x00, 0xAB, 0x0F }, 1, 1), "hex of a range");
    }
}
