namespace Codex.Core;

public readonly record struct SourcePosition(int Offset, int Line, int Column)
{
    public override string ToString() => $"({Line}:{Column})";
}

public readonly record struct SourceSpan(SourcePosition Start, SourcePosition End, string FileName)
{
    public int Length => End.Offset - Start.Offset;

    public static readonly SourceSpan s_synthetic = new(
        new SourcePosition(0, 0, 0),
        new SourcePosition(0, 0, 0),
        "");

    public static SourceSpan Single(int offset, int line, int column, string fileName) =>
        new(new SourcePosition(offset, line, column),
            new SourcePosition(offset + 1, line, column + 1),
            fileName);

    public SourceSpan Through(SourceSpan other) =>
        new(Start.Offset <= other.Start.Offset ? Start : other.Start,
            End.Offset >= other.End.Offset ? End : other.End,
            FileName.Length > 0 ? FileName : other.FileName);

    public override string ToString() =>
        FileName.Length > 0
            ? $"{FileName} {Start}-{End}"
            : $"{Start}-{End}";
}

public sealed class SourceText(string fileName, string content)
{
    int[]? m_lineStarts;

    public string FileName { get; } = fileName;
    public string Content { get; } = NormalizeLineEndings(content);

    /// <summary>Boundary normalization: strip \r, convert \t to two spaces.
    /// Same convention as CceTable.NormalizeUnicode — legacy line-ending and
    /// tab concerns are pushed to the input boundary so the lexer only ever
    /// sees clean LF-terminated, space-indented text.</summary>
    static string NormalizeLineEndings(string raw)
    {
        if (raw.IndexOfAny(['\t', '\r']) < 0)
            return raw;
        var sb = new System.Text.StringBuilder(raw.Length);
        foreach (char c in raw)
        {
            if (c == '\t') sb.Append("  ");
            else if (c == '\r') { /* stripped */ }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    public string GetText(SourceSpan span)
    {
        return Content.Substring(span.Start.Offset, span.Length);
    }

    public ReadOnlySpan<int> LineStarts
    {
        get
        {
            m_lineStarts ??= ComputeLineStarts();
            return m_lineStarts;
        }
    }

    public SourcePosition GetPosition(int offset)
    {
        ReadOnlySpan<int> starts = LineStarts;
        int line = BinarySearchLineStarts(starts, offset);
        int column = offset - starts[line] + 1;
        return new SourcePosition(offset, line + 1, column);
    }

    static int BinarySearchLineStarts(ReadOnlySpan<int> starts, int offset)
    {
        int lo = 0, hi = starts.Length - 1;
        while (lo <= hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (starts[mid] <= offset)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return hi;
    }

    int[] ComputeLineStarts()
    {
        // Content is already normalized (no \r), so only \n marks line breaks.
        List<int> starts = [0];
        for (int i = 0; i < Content.Length; i++)
        {
            if (Content[i] == '\n')
            {
                starts.Add(i + 1);
            }
        }
        return starts.ToArray();
    }
}
