// The 1.25.32 wire's two tables, read from the UOAIX sources that hold them: CLIENT.EXE's packet lengths
// (go-lengths in GameOpcode.codex, 0 for a variable packet) and the server's Huffman codebook (gh-codebook in
// GameHuffman.codex, a bit count and a code for each of 257 symbols). One reader for the relay and the tests.
#nullable enable
using System;
using System.Collections.Immutable;
using System.IO;

namespace Uoaix.Assist;

public sealed record WireTables(ImmutableArray<int> Lengths, ImmutableArray<int> Book)
{
    // Opcodes 00 to B3.
    public const int Opcodes = 180;
    public const int BookValues = 514;
    const string LengthsHead = "go-lengths : List Integer = [";
    const string BookHead = "gh-codebook : List Integer = [";

    public static WireTables Load(string uoaixDir)
    {
        return new WireTables(ParseLengths(File.ReadAllText(Path.Combine(uoaixDir, "GameOpcode.codex"))),
            ParseBook(File.ReadAllText(Path.Combine(uoaixDir, "GameHuffman.codex"))));
    }

    // Every run of decimal digits in the list, which must hold Opcodes of them.
    public static ImmutableArray<int> ParseLengths(string text)
    {
        string body = ListBody(text, LengthsHead);
        ImmutableArray<int>.Builder values = ImmutableArray.CreateBuilder<int>();
        int i = 0;
        while (i < body.Length)
        {
            if (!char.IsAsciiDigit(body[i]))
            {
                i++;
                continue;
            }
            int start = i;
            while (i < body.Length && char.IsAsciiDigit(body[i]))
                i++;
            values.Add(int.Parse(body.Substring(start, i - start)));
        }
        if (values.Count != Opcodes)
            throw new InvalidDataException("go-lengths has " + values.Count + " entries, not " + Opcodes
                + " (opcodes 00 to B3)");
        return values.ToImmutable();
    }

    // Every #-prefixed run of hex digits in the list, which must hold BookValues of them.
    public static ImmutableArray<int> ParseBook(string text)
    {
        string body = ListBody(text, BookHead);
        ImmutableArray<int>.Builder values = ImmutableArray.CreateBuilder<int>();
        int i = 0;
        while (i < body.Length)
        {
            if (body[i] != '#' || i + 1 >= body.Length || !char.IsAsciiHexDigit(body[i + 1]))
            {
                i++;
                continue;
            }
            int start = ++i;
            while (i < body.Length && char.IsAsciiHexDigit(body[i]))
                i++;
            values.Add(Convert.ToInt32(body.Substring(start, i - start), 16));
        }
        if (values.Count != BookValues)
            throw new InvalidDataException("gh-codebook has " + values.Count + " values, not " + BookValues);
        return values.ToImmutable();
    }

    static string ListBody(string text, string head)
    {
        int at = text.IndexOf(head, StringComparison.Ordinal);
        if (at < 0)
            throw new InvalidDataException("no \"" + head + "\" in the source");
        int from = at + head.Length;
        int end = text.IndexOf(']', from);
        if (end < 0)
            throw new InvalidDataException("\"" + head + "\" has no closing ]");
        return text.Substring(from, end - from);
    }
}
