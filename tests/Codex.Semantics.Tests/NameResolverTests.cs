using Codex.Core;
using Codex.Syntax;
using Codex.Ast;
using Codex.Semantics;
using Xunit;

namespace Codex.Semantics.Tests;

public class NameResolverTests
{
    private static (ResolvedChapter Resolved, DiagnosticBag Diags) ResolveSource(string source)
    {
        SourceText src = new("test.codex", source);
        DiagnosticBag bag = new();
        Lexer lexer = new(src, bag);
        IReadOnlyList<Token> tokens = lexer.TokenizeAll();
        Parser parser = new(tokens, bag);
        DocumentNode doc = parser.ParseDocument();
        Desugarer desugarer = new(bag);
        Chapter chapter = desugarer.Desugar(doc, "Test");
        NameResolver resolver = new(bag);
        ResolvedChapter resolved = resolver.Resolve(chapter);
        return (resolved, bag);
    }

    [Fact]
    public void Self_referencing_definition_resolves()
    {
        (ResolvedChapter resolved, DiagnosticBag diags) = ResolveSource("x = 42");
        Assert.False(diags.HasErrors);
        Assert.Contains("x", resolved.TopLevelNames);
    }

    [Fact]
    public void Undefined_name_reports_error()
    {
        (ResolvedChapter _, DiagnosticBag diags) = ResolveSource("x = y");
        Assert.True(diags.HasErrors);
        Assert.Contains(diags.ToImmutable(), d => d.Code == CdxCodes.UndefinedName);
    }

    [Fact]
    public void Parameter_is_in_scope()
    {
        (ResolvedChapter _, DiagnosticBag diags) = ResolveSource("f (x) = x + 1");
        Assert.False(diags.HasErrors);
    }

    [Fact]
    public void Let_binding_is_in_scope()
    {
        (ResolvedChapter _, DiagnosticBag diags) = ResolveSource("f = let a = 1 in a + 2");
        Assert.False(diags.HasErrors);
    }

    [Fact]
    public void Cross_definition_reference_resolves()
    {
        string source = "a = 1\nb = a + 2";
        (ResolvedChapter _, DiagnosticBag diags) = ResolveSource(source);
        Assert.False(diags.HasErrors);
    }

    [Fact]
    public void Forward_reference_resolves()
    {
        string source = "a = b\nb = 42";
        (ResolvedChapter _, DiagnosticBag diags) = ResolveSource(source);
        Assert.False(diags.HasErrors);
    }

    [Fact]
    public void Duplicate_definition_reports_error()
    {
        string source = "x = 1\nx = 2";
        (ResolvedChapter _, DiagnosticBag diags) = ResolveSource(source);
        Assert.True(diags.HasErrors);
        Assert.Contains(diags.ToImmutable(), d => d.Code == CdxCodes.DuplicateDefinition);
    }

    [Fact]
    public void Type_name_as_expression_does_not_error()
    {
        (ResolvedChapter _, DiagnosticBag diags) = ResolveSource("x = True");
        Assert.False(diags.HasErrors);
    }

    [Fact]
    public void Match_pattern_binds_variable()
    {
        string source = "f = when True if y -> y";
        (ResolvedChapter _, DiagnosticBag diags) = ResolveSource(source);
        Assert.False(diags.HasErrors);
    }

    [Fact]
    public void Builtin_show_is_in_scope()
    {
        (ResolvedChapter _, DiagnosticBag diags) = ResolveSource("x = show 42");
        Assert.False(diags.HasErrors);
    }

    [Fact]
    public void Undefined_name_suggests_close_match()
    {
        string source = "square (x) = x * x\nmain = squre 5";
        (ResolvedChapter _, DiagnosticBag diags) = ResolveSource(source);
        Assert.True(diags.HasErrors);
        Diagnostic error = diags.ToImmutable().First(d => d.Code == CdxCodes.UndefinedName);
        Assert.Contains("Did you mean 'square'?", error.Message);
    }

    [Fact]
    public void Undefined_name_no_suggestion_when_too_distant()
    {
        string source = "square (x) = x * x\nmain = xyz 5";
        (ResolvedChapter _, DiagnosticBag diags) = ResolveSource(source);
        Assert.True(diags.HasErrors);
        Diagnostic error = diags.ToImmutable().First(d => d.Code == CdxCodes.UndefinedName);
        Assert.DoesNotContain("Did you mean", error.Message);
    }

    [Fact]
    public void Undefined_name_suggests_builtin()
    {
        string source = "x = shw 42";
        (ResolvedChapter _, DiagnosticBag diags) = ResolveSource(source);
        Assert.True(diags.HasErrors);
        Diagnostic error = diags.ToImmutable().First(d => d.Code == CdxCodes.UndefinedName);
        Assert.Contains("Did you mean 'show'?", error.Message);
    }
}
