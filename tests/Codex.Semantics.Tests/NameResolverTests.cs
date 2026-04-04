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
        Assert.Contains(diags.ToImmutable(), d => d.Code == "CDX3002");
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
        Assert.Contains(diags.ToImmutable(), d => d.Code == "CDX3001");
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
    public void Module_without_exports_exports_everything()
    {
        (ResolvedChapter resolved, DiagnosticBag diags) = ResolveSource("x = 1\ny = 2");
        Assert.False(diags.HasErrors);
        Assert.Contains("x", resolved.ExportedNames);
        Assert.Contains("y", resolved.ExportedNames);
    }

    [Fact]
    public void Module_with_exports_restricts_visibility()
    {
        string source = "export square\n\nsquare (x) = x * x\nhelper (x) = x + 1";
        (ResolvedChapter resolved, DiagnosticBag diags) = ResolveSource(source);
        Assert.False(diags.HasErrors);
        Assert.Contains("square", resolved.ExportedNames);
        Assert.DoesNotContain("helper", resolved.ExportedNames);
    }

    [Fact]
    public void Export_of_undefined_name_reports_error()
    {
        string source = "export missing\n\nx = 1";
        (ResolvedChapter _, DiagnosticBag diags) = ResolveSource(source);
        Assert.True(diags.HasErrors);
        Assert.Contains(diags.ToImmutable(), d => d.Code == "CDX3020");
    }

    [Fact]
    public void Multiple_export_declarations_accumulate()
    {
        string source = "export a\nexport b\n\na = 1\nb = 2\nc = 3";
        (ResolvedChapter resolved, DiagnosticBag diags) = ResolveSource(source);
        Assert.False(diags.HasErrors);
        Assert.Contains("a", resolved.ExportedNames);
        Assert.Contains("b", resolved.ExportedNames);
        Assert.DoesNotContain("c", resolved.ExportedNames);
    }

    [Fact]
    public void Comma_separated_exports()
    {
        string source = "export a, b\n\na = 1\nb = 2\nc = 3";
        (ResolvedChapter resolved, DiagnosticBag diags) = ResolveSource(source);
        Assert.False(diags.HasErrors);
        Assert.Contains("a", resolved.ExportedNames);
        Assert.Contains("b", resolved.ExportedNames);
        Assert.DoesNotContain("c", resolved.ExportedNames);
    }

    [Fact]
    public void Undefined_name_suggests_close_match()
    {
        string source = "square (x) = x * x\nmain = squre 5";
        (ResolvedChapter _, DiagnosticBag diags) = ResolveSource(source);
        Assert.True(diags.HasErrors);
        Diagnostic error = diags.ToImmutable().First(d => d.Code == "CDX3002");
        Assert.Contains("Did you mean 'square'?", error.Message);
    }

    [Fact]
    public void Undefined_name_no_suggestion_when_too_distant()
    {
        string source = "square (x) = x * x\nmain = xyz 5";
        (ResolvedChapter _, DiagnosticBag diags) = ResolveSource(source);
        Assert.True(diags.HasErrors);
        Diagnostic error = diags.ToImmutable().First(d => d.Code == "CDX3002");
        Assert.DoesNotContain("Did you mean", error.Message);
    }

    [Fact]
    public void Undefined_name_suggests_builtin()
    {
        string source = "x = shw 42";
        (ResolvedChapter _, DiagnosticBag diags) = ResolveSource(source);
        Assert.True(diags.HasErrors);
        Diagnostic error = diags.ToImmutable().First(d => d.Code == "CDX3002");
        Assert.Contains("Did you mean 'show'?", error.Message);
    }
}
