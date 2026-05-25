using Codex.Core;
using Xunit;

namespace Codex.Core.Tests;

public class ContentHashTests
{
    [Fact]
    public void Identical_content_produces_identical_hash()
    {
        ContentHash h1 = ContentHash.Of("hello, codex");
        ContentHash h2 = ContentHash.Of("hello, codex");
        Assert.Equal(h1, h2);
    }

    [Fact]
    public void Different_content_produces_different_hash()
    {
        ContentHash h1 = ContentHash.Of("hello");
        ContentHash h2 = ContentHash.Of("world");
        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void Hash_is_32_bytes()
    {
        ContentHash h = ContentHash.Of("test");
        Assert.Equal(32, h.Bytes.Length);
    }

    [Fact]
    public void Hex_roundtrip()
    {
        ContentHash original = ContentHash.Of("roundtrip test");
        string hex = original.ToHex();
        ContentHash restored = ContentHash.FromHex(hex);
        Assert.Equal(original, restored);
    }

    [Fact]
    public void Short_hex_is_16_chars()
    {
        ContentHash h = ContentHash.Of("short");
        Assert.Equal(16, h.ToShortHex().Length);
    }
}

public class SourceTextTests
{
    [Fact]
    public void GetPosition_first_character()
    {
        SourceText source = new("test.codex", "hello\nworld");
        SourcePosition pos = source.GetPosition(0);
        Assert.Equal(1, pos.Line);
        Assert.Equal(1, pos.Column);
    }

    [Fact]
    public void GetPosition_second_line()
    {
        SourceText source = new("test.codex", "hello\nworld");
        SourcePosition pos = source.GetPosition(6);
        Assert.Equal(2, pos.Line);
        Assert.Equal(1, pos.Column);
    }

    [Fact]
    public void GetText_from_span()
    {
        SourceText source = new("test.codex", "hello world");
        SourceSpan span = new SourceSpan(
            new SourcePosition(6, 1, 7),
            new SourcePosition(11, 1, 12),
            "test.codex");
        Assert.Equal("world", source.GetText(span));
    }
}

public class NameTests
{
    [Fact]
    public void Type_name_starts_with_uppercase()
    {
        Name name = new Name("Account");
        Assert.True(name.IsTypeName);
        Assert.False(name.IsValueName);
    }

    [Fact]
    public void Value_name_starts_with_lowercase()
    {
        Name name = new Name("compute-balance");
        Assert.False(name.IsTypeName);
        Assert.True(name.IsValueName);
    }

    [Fact]
    public void Qualified_name_parse_and_display()
    {
        QualifiedName qn = QualifiedName.Parse("Sorting.Merge-Sort.merge-sort");
        Assert.Equal(3, qn.Parts.Count);
        Assert.Equal("merge-sort", qn.Leaf.Value);
        Assert.Equal("Sorting.Merge-Sort.merge-sort", qn.ToString());
    }

    [Fact]
    public void Qualified_name_append()
    {
        QualifiedName qn = QualifiedName.Simple("Sorting");
        QualifiedName extended = qn.Append(new Name("merge-sort"));
        Assert.Equal("Sorting.merge-sort", extended.ToString());
    }
}

public class DiagnosticBagTests
{
    [Fact]
    public void Initially_no_errors()
    {
        DiagnosticBag bag = new();
        Assert.False(bag.HasErrors);
        Assert.Equal(0, bag.Count);
    }

    [Fact]
    public void Adding_error_sets_has_errors()
    {
        DiagnosticBag bag = new();
        bag.Error(CdxCodes.TooManyErrors, "test error", SourceSpan.s_synthetic);
        Assert.True(bag.HasErrors);
        Assert.Equal(1, bag.Count);
    }

    [Fact]
    public void Warning_does_not_count_as_error()
    {
        DiagnosticBag bag = new();
        bag.Warning(CdxCodes.UnexpectedCharacter, "test warning", SourceSpan.s_synthetic);
        Assert.False(bag.HasErrors);
        Assert.Equal(1, bag.Count);
    }

    [Fact]
    public void ToImmutable_returns_all_diagnostics()
    {
        DiagnosticBag bag = new();
        bag.Error(CdxCodes.TooManyErrors, "error 1", SourceSpan.s_synthetic);
        bag.Warning(CdxCodes.UnexpectedCharacter, "warning 1", SourceSpan.s_synthetic);
        bag.Info(CdxCodes.IndentationMismatch, "info 1", SourceSpan.s_synthetic);
        System.Collections.Immutable.ImmutableArray<Diagnostic> all = bag.ToImmutable();
        Assert.Equal(3, all.Length);
    }

    [Fact]
    public void Levenshtein_identical_strings_is_zero()
    {
        Assert.Equal(0, StringDistance.Levenshtein("square", "square"));
    }

    [Fact]
    public void Levenshtein_single_typo()
    {
        Assert.Equal(1, StringDistance.Levenshtein("squre", "square"));
    }

    [Fact]
    public void Levenshtein_empty_strings()
    {
        Assert.Equal(0, StringDistance.Levenshtein("", ""));
        Assert.Equal(3, StringDistance.Levenshtein("abc", ""));
        Assert.Equal(3, StringDistance.Levenshtein("", "abc"));
    }

    [Fact]
    public void FindClosest_returns_best_match()
    {
        string[] candidates = ["square", "cube", "negate", "show"];
        Assert.Equal("square", StringDistance.FindClosest("squre", candidates));
        Assert.Equal("show", StringDistance.FindClosest("shw", candidates));
    }

    [Fact]
    public void FindClosest_returns_null_when_too_distant()
    {
        string[] candidates = ["square", "cube"];
        Assert.Null(StringDistance.FindClosest("xyz", candidates));
    }
}
