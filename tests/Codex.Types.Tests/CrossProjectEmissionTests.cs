using Codex.Ast;
using Codex.Core;
using Codex.IR;
using Codex.Semantics;
using Codex.Syntax;
using Codex.Types;
using Xunit;

namespace Codex.Types.Tests;

public class CrossProjectEmissionTests
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

    static (IRChapter? Ir, DiagnosticBag Diags) CompileWithCitedDefs(
        string mainSource, IChapterLoader loader)
    {
        SourceText src = new("main.codex", mainSource);
        DiagnosticBag bag = new();
        DocumentNode doc = DocumentParser.Parse(src, bag);
        Desugarer desugarer = new(bag);
        Chapter chapter = desugarer.Desugar(doc, "Main");
        NameResolver resolver = new(bag, loader);
        ResolvedChapter resolved = resolver.Resolve(chapter);
        if (bag.HasErrors)
        {
            return (null, bag);
        }

        TypeChecker checker = new(bag);
        foreach (ResolvedChapter imported in resolved.CitedChapters)
        {
            checker.CiteChapter(imported.Chapter);
        }
        Map<string, CodexType> types = checker.CheckChapter(resolved.Chapter);
        if (bag.HasErrors)
        {
            return (null, bag);
        }

        Lowering lowering = new(types, checker.ConstructorMap, checker.TypeDefMap, bag);
        IRChapter ir = lowering.Lower(resolved.Chapter);
        ir = Lowering.LowerCitedDefs(resolved.CitedChapters, ir, bag);
        return (ir, bag);
    }

    [Fact]
    public void Cited_function_def_is_included_in_main_IR()
    {
        ResolvedChapter helperModule = CompileModule(
            "triple : Integer -> Integer\ntriple (x) = x + x + x", "Helpers");

        Map<string, ResolvedChapter> modules =
            Map<string, ResolvedChapter>.s_empty.Set("Utils::Helpers", helperModule);
        MockModuleLoader loader = new(modules);

        string mainSource = "cites Utils chapter Helpers (triple)\nresult : Integer\nresult = triple 7";
        (IRChapter? ir, DiagnosticBag diags) = CompileWithCitedDefs(mainSource, loader);

        Assert.False(diags.HasErrors, string.Join("; ", diags.ToImmutable()));
        Assert.NotNull(ir);
        Assert.Contains(ir!.Definitions, d => d.Name == "triple");
        Assert.Contains(ir.Definitions, d => d.Name == "result");
    }

    [Fact]
    public void Cited_chapter_with_no_cites_leaves_IR_unchanged()
    {
        Map<string, ResolvedChapter> modules = Map<string, ResolvedChapter>.s_empty;
        MockModuleLoader loader = new(modules);

        string mainSource = "result : Integer\nresult = 42";
        (IRChapter? ir, DiagnosticBag diags) = CompileWithCitedDefs(mainSource, loader);

        Assert.False(diags.HasErrors, string.Join("; ", diags.ToImmutable()));
        Assert.NotNull(ir);
        Assert.Single(ir!.Definitions);
        Assert.Equal("result", ir.Definitions[0].Name);
    }

    [Fact]
    public void Main_chapter_name_collision_wins()
    {
        ResolvedChapter helperModule = CompileModule(
            "shared : Integer\nshared = 100", "Helpers");

        Map<string, ResolvedChapter> modules =
            Map<string, ResolvedChapter>.s_empty.Set("Utils::Helpers", helperModule);
        MockModuleLoader loader = new(modules);

        string mainSource = "cites Utils chapter Helpers (shared)\nshared : Integer\nshared = 1\nresult : Integer\nresult = shared";
        (IRChapter? ir, DiagnosticBag diags) = CompileWithCitedDefs(mainSource, loader);

        Assert.NotNull(ir);
        int sharedCount = ir!.Definitions.Count(d => d.Name == "shared");
        Assert.Equal(1, sharedCount);
    }
}
