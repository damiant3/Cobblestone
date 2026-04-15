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

        public ResolvedChapter? Load(string quire, string chapterName)
            => m_modules[$"{quire}::{chapterName}"];
    }

    static ResolvedChapter CompileModule(string source, string name)
    {
        SourceText src = new($"{name}.codex", source);
        DiagnosticBag bag = new();
        DocumentNode doc = DocumentParser.Parse(src, bag);
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
        DocumentNode doc = DocumentParser.Parse(src, bag);
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
            Map<string, ResolvedChapter>.s_empty.Set("Utils::Math", mathModule);
        MockModuleLoader loader = new(modules);

        string source = "cites Utils chapter Math (double)\nresult = double 21";
        (ResolvedChapter _, DiagnosticBag diags) = ResolveWithLoader(source, loader);
        Assert.False(diags.HasErrors, string.Join("; ", diags.ToImmutable()));
    }

    [Fact]
    public void Unresolved_import_reports_error()
    {
        Map<string, ResolvedChapter> modules = Map<string, ResolvedChapter>.s_empty;
        MockModuleLoader loader = new(modules);

        string source = "cites Lib chapter Missing (anything)\nresult = 42";
        (ResolvedChapter _, DiagnosticBag diags) = ResolveWithLoader(source, loader);
        Assert.True(diags.HasErrors);
        Assert.Contains(diags.ToImmutable(), d => d.Code == CdxCodes.UnresolvedCitation);
    }

    [Fact]
    public void Import_without_loader_reports_error()
    {
        SourceText src = new("test.codex", "cites Utils chapter Math (f)\nresult = 42");
        DiagnosticBag bag = new();
        DocumentNode doc = DocumentParser.Parse(src, bag);
        Desugarer desugarer = new(bag);
        Chapter chapter = desugarer.Desugar(doc, "Test");
        NameResolver resolver = new(bag);
        resolver.Resolve(chapter);
        Assert.True(bag.HasErrors);
        Assert.Contains(bag.ToImmutable(), d => d.Code == CdxCodes.UnresolvedCitation);
    }

    [Fact]
    public void Import_parses_correctly()
    {
        SourceText src = new("test.codex", "cites Utils chapter Math (f)\nx = 1");
        DiagnosticBag bag = new();
        DocumentNode doc = DocumentParser.Parse(src, bag);
        Assert.False(bag.HasErrors, string.Join("; ", bag.ToImmutable()));
        Assert.Single(doc.Citations);
        Assert.Equal("Utils", doc.Citations[0].Quire.Text);
        Assert.Equal("Math", doc.Citations[0].ChapterTitle);
        Assert.Single(doc.Definitions);
    }

    [Fact]
    public void Imported_name_used_in_body_resolves()
    {
        ResolvedChapter helperModule = CompileModule(
            "helper (x) = x + 1", "Helpers");

        Map<string, ResolvedChapter> modules =
            Map<string, ResolvedChapter>.s_empty.Set("Utils::Helpers", helperModule);
        MockModuleLoader loader = new(modules);

        string source = "cites Utils chapter Helpers (helper)\nresult = helper 5";
        (ResolvedChapter resolved, DiagnosticBag diags) =
            ResolveWithLoader(source, loader);
        Assert.False(diags.HasErrors, string.Join("; ", diags.ToImmutable()));
        Assert.Single(resolved.CitedChapters);
    }
}
