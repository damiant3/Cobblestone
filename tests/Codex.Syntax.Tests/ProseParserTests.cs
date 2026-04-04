using Codex.Core;
using Codex.Syntax;
using Xunit;

namespace Codex.Syntax.Tests;

public class ProseParserTests
{
    private static DocumentNode ParseProse(string source)
    {
        SourceText src = new("test.codex", source);
        DiagnosticBag bag = new();
        ProseParser parser = new(src, bag);
        return parser.ParseDocument();
    }

    private static (DocumentNode Doc, DiagnosticBag Diags) ParseProseWithDiags(string source)
    {
        SourceText src = new("test.codex", source);
        DiagnosticBag bag = new();
        ProseParser parser = new(src, bag);
        return (parser.ParseDocument(), bag);
    }

    [Fact]
    public void IsProseDocument_detects_chapter_header()
    {
        Assert.True(ProseParser.IsProseDocument("Chapter: Hello\n\n Some prose.\n"));
        Assert.True(ProseParser.IsProseDocument("\nChapter: Hello\n"));
    }

    [Fact]
    public void IsProseDocument_rejects_notation_only()
    {
        Assert.False(ProseParser.IsProseDocument("square : Integer -> Integer\nsquare (x) = x * x"));
        Assert.False(ProseParser.IsProseDocument("x = 42"));
        Assert.False(ProseParser.IsProseDocument(""));
    }

    [Fact]
    public void Single_chapter_with_title()
    {
        DocumentNode doc = ParseProse("Chapter: Greeting\n");
        Assert.Single(doc.Chapters);
        Assert.Equal("Greeting", doc.Chapters[0].Title);
    }

    [Fact]
    public void Chapter_with_prose_block()
    {
        string source = "Chapter: Intro\n\n This is some prose.\n Another line.\n";
        DocumentNode doc = ParseProse(source);
        Assert.Single(doc.Chapters);

        List<DocumentMember> members = doc.Chapters[0].Members.ToList();
        ProseBlockNode prose = Assert.Single(members.OfType<ProseBlockNode>());
        Assert.Contains("This is some prose.", prose.Text);
        Assert.Contains("Another line.", prose.Text);
    }

    [Fact]
    public void Chapter_with_notation_block()
    {
        string source = "Chapter: Math\n\n  square : Integer -> Integer\n  square (x) = x * x\n";
        DocumentNode doc = ParseProse(source);
        Assert.Single(doc.Chapters);

        List<NotationBlockNode> notations = doc.Chapters[0].Members.OfType<NotationBlockNode>().ToList();
        Assert.Single(notations);
        Assert.Single(notations[0].Definitions);
        Assert.Equal("square", notations[0].Definitions[0].Name.Text);
    }

    [Fact]
    public void Chapter_with_prose_and_notation()
    {
        string source =
            "Chapter: Greeting\n" +
            "\n" +
            " This chapter greets people.\n" +
            "\n" +
            " We say:\n" +
            "\n" +
            "  greet : Text -> Text\n" +
            "  greet (name) = \"Hello, \" ++ name ++ \"!\"\n" +
            "\n" +
            " To greet the world:\n" +
            "\n" +
            "  main : Text\n" +
            "  main = greet \"World\"\n";

        DocumentNode doc = ParseProse(source);
        Assert.Single(doc.Chapters);
        Assert.Equal("Greeting", doc.Chapters[0].Title);

        // Should have definitions extracted
        Assert.Equal(2, doc.Definitions.Count);
        Assert.Equal("greet", doc.Definitions[0].Name.Text);
        Assert.Equal("main", doc.Definitions[1].Name.Text);
    }

    [Fact]
    public void Definitions_are_collected_from_notation_blocks()
    {
        string source =
            "Chapter: Test\n" +
            "\n" +
            "  f : Integer -> Integer\n" +
            "  f (x) = x + 1\n" +
            "\n" +
            "  g : Integer -> Integer\n" +
            "  g (x) = x * 2\n";

        DocumentNode doc = ParseProse(source);
        Assert.Equal(2, doc.Definitions.Count);
        Assert.Equal("f", doc.Definitions[0].Name.Text);
        Assert.Equal("g", doc.Definitions[1].Name.Text);
    }

    [Fact]
    public void Multiple_chapters()
    {
        string source =
            "Chapter: First\n" +
            "\n" +
            " Hello.\n" +
            "\n" +
            "Chapter: Second\n" +
            "\n" +
            " World.\n";

        DocumentNode doc = ParseProse(source);
        Assert.Equal(2, doc.Chapters.Count);
        Assert.Equal("First", doc.Chapters[0].Title);
        Assert.Equal("Second", doc.Chapters[1].Title);
    }

    [Fact]
    public void Prose_mode_document_round_trips_through_desugarer()
    {
        string source =
            "Chapter: Greeting\n" +
            "\n" +
            " We say:\n" +
            "\n" +
            "  greet : Text -> Text\n" +
            "  greet (name) = \"Hello, \" ++ name ++ \"!\"\n" +
            "\n" +
            "  main : Text\n" +
            "  main = greet \"World\"\n";

        DocumentNode doc = ParseProse(source);
        DiagnosticBag diagnostics = new();
        Codex.Ast.Desugarer desugarer = new(diagnostics);
        Codex.Ast.Chapter module = desugarer.Desugar(doc, "greeting");

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(2, module.Definitions.Count);
        Assert.Equal("greet", module.Definitions[0].Name.Value);
        Assert.Equal("main", module.Definitions[1].Name.Value);
    }

    [Fact]
    public void No_errors_for_well_formed_prose_document()
    {
        string source =
            "Chapter: Clean\n" +
            "\n" +
            " A clean document.\n" +
            "\n" +
            "  x : Integer\n" +
            "  x = 42\n";

        (DocumentNode doc, DiagnosticBag diags) = ParseProseWithDiags(source);
        Assert.False(diags.HasErrors);
        Assert.Single(doc.Definitions);
    }
}
