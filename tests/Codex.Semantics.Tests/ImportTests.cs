using Codex.Core;
using Codex.Syntax;
using Codex.Ast;
using Codex.Semantics;
using Xunit;

namespace Codex.Semantics.Tests;

public class ImportTests
{
    sealed class MockModuleLoader(Map<string, ResolvedChapter> modules) : IChapterLoader
    {
        readonly Map<string, ResolvedChapter> m_modules = modules;

        public ResolvedChapter? Load(string chapterName) => m_modules[chapterName];
    }

    static ResolvedChapter CompileModule(string source, string name)
    {
        SourceText src = new($"{name}.codex", source);
        DiagnosticBag bag = new();
        Lexer lexer = new(src, bag);
        IReadOnlyList<Token> tokens = lexer.TokenizeAll();
        Parser parser = new(tokens, bag);
        DocumentNode doc = parser.ParseDocument();
        Desugarer desugarer = new(bag);
        Chapter chapter = desugarer.Desugar(doc, name);
        NameResolver resolver = new(bag);
        return resolver.Resolve(chapter);
    }

    static (ResolvedChapter Resolved, DiagnosticBag Diags) ResolveWithLoader(
        string source, IChapterLoader loader)
    {
        SourceText src = new("test.codex", source);
        DiagnosticBag bag = new();
        Lexer lexer = new(src, bag);
        IReadOnlyList<Token> tokens = lexer.TokenizeAll();
        Parser parser = new(tokens, bag);
        DocumentNode doc = parser.ParseDocument();
        Desugarer desugarer = new(bag);
        Chapter chapter = desugarer.Desugar(doc, "Test");
        NameResolver resolver = new(bag, loader);
        ResolvedChapter resolved = resolver.Resolve(chapter);
        return (resolved, bag);
    }

    [Fact]
    public void Import_makes_definitions_available()
    {
        ResolvedChapter mathModule = CompileModule(
            "double (x) = x + x\ntriple (x) = x + x + x", "Math");

        Map<string, ResolvedChapter> modules =
            Map<string, ResolvedChapter>.s_empty.Set("Math", mathModule);
        MockModuleLoader loader = new(modules);

        string source = "cites Math\nresult = double 21";
        (ResolvedChapter _, DiagnosticBag diags) = ResolveWithLoader(source, loader);
        Assert.False(diags.HasErrors, string.Join("; ", diags.ToImmutable()));
    }

    [Fact]
    public void Unresolved_import_reports_error()
    {
        Map<string, ResolvedChapter> modules = Map<string, ResolvedChapter>.s_empty;
        MockModuleLoader loader = new(modules);

        string source = "cites Missing\nresult = 42";
        (ResolvedChapter _, DiagnosticBag diags) = ResolveWithLoader(source, loader);
        Assert.True(diags.HasErrors);
        Assert.Contains(diags.ToImmutable(), d => d.Code == "CDX3010");
    }

    [Fact]
    public void Import_without_loader_reports_error()
    {
        SourceText src = new("test.codex", "cites Math\nresult = 42");
        DiagnosticBag bag = new();
        Lexer lexer = new(src, bag);
        IReadOnlyList<Token> tokens = lexer.TokenizeAll();
        Parser parser = new(tokens, bag);
        DocumentNode doc = parser.ParseDocument();
        Desugarer desugarer = new(bag);
        Chapter chapter = desugarer.Desugar(doc, "Test");
        NameResolver resolver = new(bag);
        resolver.Resolve(chapter);
        Assert.True(bag.HasErrors);
        Assert.Contains(bag.ToImmutable(), d => d.Code == "CDX3010");
    }

    [Fact]
    public void Import_parses_correctly()
    {
        SourceText src = new("test.codex", "cites Math\nx = 1");
        DiagnosticBag bag = new();
        Lexer lexer = new(src, bag);
        IReadOnlyList<Token> tokens = lexer.TokenizeAll();
        Parser parser = new(tokens, bag);
        DocumentNode doc = parser.ParseDocument();
        Assert.False(bag.HasErrors, string.Join("; ", bag.ToImmutable()));
        Assert.Single(doc.Citations);
        Assert.Equal("Math", doc.Citations[0].Name.Text);
        Assert.Single(doc.Definitions);
    }

    [Fact]
    public void Imported_name_used_in_body_resolves()
    {
        ResolvedChapter helperModule = CompileModule(
            "helper (x) = x + 1", "Helpers");

        Map<string, ResolvedChapter> modules =
            Map<string, ResolvedChapter>.s_empty.Set("Helpers", helperModule);
        MockModuleLoader loader = new(modules);

        string source = "cites Helpers\nresult = helper 5";
        (ResolvedChapter resolved, DiagnosticBag diags) =
            ResolveWithLoader(source, loader);
        Assert.False(diags.HasErrors, string.Join("; ", diags.ToImmutable()));
        Assert.Single(resolved.CitedChapters);
    }
}
