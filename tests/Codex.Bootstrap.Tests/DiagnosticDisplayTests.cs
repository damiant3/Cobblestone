using Xunit;

namespace Codex.Bootstrap.Tests;

// Tests that exercise the self-host diagnostic machinery end-to-end.
// The self-host compiler operates on CCE-encoded text internally; I/O
// boundaries convert to/from Unicode. These tests route Unicode inputs
// through FromUnicode on the way in and ToUnicode on the way out so the
// assertions read naturally against the user-visible diagnostic form:
// "file:line:col: severity CDXnnnn: message".

public class DiagnosticDisplayTests
{
    static string Cce(string unicode) => _Cce.FromUnicode(unicode);
    static string Uni(string cce) => _Cce.ToUnicode(cce);

    static FileTable TableWith(string name)
    {
        return Codex_Codex_Codex.file_table_add(
            Codex_Codex_Codex.empty_file_table(),
            Cce(name));
    }

    static SourceSpan SpanAt(long line, long col, long offset, long length, long fileId)
    {
        return Codex_Codex_Codex.span_at(line, col, offset, length, fileId);
    }

    static string Render(Diagnostic d, FileTable table)
    {
        return Uni(Codex_Codex_Codex.diagnostic_display(d, table));
    }

    [Fact]
    public void Error_with_real_span_renders_file_line_col()
    {
        FileTable table = TableWith("demo.codex");
        SourceSpan span = SpanAt(7, 12, 0, 3, 1);
        Diagnostic d = Codex_Codex_Codex.make_error(
            Codex_Codex_Codex.cdx_undefined_name(),
            Cce("Undefined name: foo"),
            span);

        Assert.Equal("demo.codex:7:12: error CDX3002: Undefined name: foo", Render(d, table));
    }

    [Fact]
    public void Warning_renders_with_warning_label()
    {
        FileTable table = TableWith("demo.codex");
        SourceSpan span = SpanAt(3, 1, 0, 4, 1);
        Diagnostic d = Codex_Codex_Codex.make_warning(
            9100L,
            Cce("Unused binding: temp"),
            span);

        Assert.Equal("demo.codex:3:1: warning CDX9100: Unused binding: temp", Render(d, table));
    }

    [Fact]
    public void Info_renders_with_info_label()
    {
        FileTable table = TableWith("demo.codex");
        SourceSpan span = SpanAt(1, 1, 0, 0, 1);
        Diagnostic d = Codex_Codex_Codex.make_info(
            9200L,
            Cce("Stage 0 complete"),
            span);

        Assert.Equal("demo.codex:1:1: info CDX9200: Stage 0 complete", Render(d, table));
    }

    [Fact]
    public void Hint_renders_with_hint_label()
    {
        FileTable table = TableWith("demo.codex");
        SourceSpan span = SpanAt(42, 5, 0, 8, 1);
        Diagnostic d = Codex_Codex_Codex.make_hint(
            9300L,
            Cce("Consider extracting this expression"),
            span);

        Assert.Equal("demo.codex:42:5: hint CDX9300: Consider extracting this expression", Render(d, table));
    }

    [Fact]
    public void Synthetic_span_omits_location_prefix()
    {
        FileTable table = Codex_Codex_Codex.empty_file_table();
        Diagnostic d = Codex_Codex_Codex.make_error(
            Codex_Codex_Codex.cdx_ir_error(),
            Cce("Unexpected IR error node"),
            Codex_Codex_Codex.synthetic_span());

        Assert.Equal("error CDX2000: Unexpected IR error node", Render(d, table));
    }

    [Fact]
    public void Span_without_file_entry_renders_line_col_only()
    {
        // file-id 1 but no entry in the table: the location prefix falls back
        // to just "line:col" with no filename.
        FileTable table = Codex_Codex_Codex.empty_file_table();
        SourceSpan span = SpanAt(5, 9, 0, 1, 1);
        Diagnostic d = Codex_Codex_Codex.make_warning(
            9100L,
            Cce("No file registered"),
            span);

        Assert.Equal("5:9: warning CDX9100: No file registered", Render(d, table));
    }

    // End-to-end: feed source through the self-host pipeline and confirm
    // the resolver's diagnostic carries a real span tracing back to the
    // offending token in the source.
    static ResolveResult ResolveSource(string source, out FileTable table)
    {
        table = TableWith("test.codex");
        var tokens = Codex_Codex_Codex.tokenize(Cce(source), 1L);
        var parseState = Codex_Codex_Codex.make_parse_state(tokens);
        var doc = Codex_Codex_Codex.parse_document(parseState);
        var chapter = Codex_Codex_Codex.desugar_document(doc, Cce("Test"));
        return Codex_Codex_Codex.resolve_chapter_with_citations(
            chapter, new List<ResolveResult>());
    }

    [Fact]
    public void Undefined_name_end_to_end_carries_real_span()
    {
        ResolveResult result = ResolveSource("x = y\n", out FileTable table);

        Assert.NotEmpty(result.bag.diagnostics);
        Diagnostic undef = result.bag.diagnostics
            .Single(d => d.code == Codex_Codex_Codex.cdx_undefined_name());

        // 'y' sits at column 5 of line 1. file-id 1 maps to "test.codex".
        Assert.Equal(1L, undef.span.start.line);
        Assert.Equal(5L, undef.span.start.column);
        Assert.Equal(1L, undef.span.file_id);

        string rendered = Render(undef, table);
        Assert.StartsWith("test.codex:1:5: error CDX3002:", rendered);
    }

    [Fact]
    public void Duplicate_definition_end_to_end_carries_real_span()
    {
        // Two defs with the same name. The resolver reports duplicate-def
        // with the span of the second def (where duplication was detected),
        // not synthetic.
        ResolveResult result = ResolveSource("x = 1\nx = 2\n", out FileTable table);

        Diagnostic dup = result.bag.diagnostics
            .Single(d => d.code == Codex_Codex_Codex.cdx_duplicate_definition());

        Assert.Equal(2L, dup.span.start.line);
        Assert.Equal(1L, dup.span.file_id);

        string rendered = Render(dup, table);
        Assert.StartsWith("test.codex:2:", rendered);
        Assert.Contains("error CDX3001:", rendered);
    }

    [Fact]
    public void Bag_caps_errors_at_twenty_and_emits_overflow_sentinel()
    {
        // Add 25 errors; bag keeps only 20 plus a single CDX0001 sentinel.
        DiagnosticBag bag = Codex_Codex_Codex.empty_bag();
        SourceSpan span = SpanAt(1, 1, 0, 1, 1);
        for (int i = 0; i < 25; i++)
        {
            Diagnostic d = Codex_Codex_Codex.make_error(9999L, Cce($"err {i}"), span);
            bag = Codex_Codex_Codex.bag_add(bag, d);
        }

        Assert.True(Codex_Codex_Codex.bag_is_truncated(bag));
        // 20 real errors + 1 sentinel = 21 total diagnostics.
        Assert.Equal(21L, Codex_Codex_Codex.bag_count(bag));

        Diagnostic last = bag.diagnostics.Last();
        Assert.Equal(Codex_Codex_Codex.cdx_too_many_errors(), last.code);
    }

    [Fact]
    public void Bag_counts_only_errors_toward_cap()
    {
        // Warnings and hints never trigger the cap.
        DiagnosticBag bag = Codex_Codex_Codex.empty_bag();
        SourceSpan span = SpanAt(1, 1, 0, 1, 1);
        for (int i = 0; i < 100; i++)
        {
            bag = Codex_Codex_Codex.bag_add(bag,
                Codex_Codex_Codex.make_warning(9100L, Cce("w"), span));
        }

        Assert.False(Codex_Codex_Codex.bag_is_truncated(bag));
        Assert.False(Codex_Codex_Codex.bag_has_errors(bag));
        Assert.Equal(100L, Codex_Codex_Codex.bag_count(bag));
    }
}
