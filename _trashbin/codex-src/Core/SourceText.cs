using System;
using System.Collections.Generic;
using System.Linq;

public sealed record SourceSpan(SourcePosition start, SourcePosition end, string file);

public sealed record SourcePosition(long line, long column, long offset);

public static class Codex_SourceText
{
    public static SourcePosition make_position(long line, long col, long offset)
    {
        return new SourcePosition(line, col, offset);
    }

    public static SourceSpan make_span(SourcePosition s, SourcePosition e, string f)
    {
        return new SourceSpan(s, e, f);
    }

    public static long span_length(SourceSpan span)
    {
        return (span.end.offset - span.start.offset);
    }

}
