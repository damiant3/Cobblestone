using Codex.Core;
using Codex.Syntax;
using Codex.Ast;
using Codex.Semantics;
using Codex.Types;
using Codex.IR;
using Codex.Emit.CSharp;

namespace Codex.Cli;

public static partial class Program
{
    sealed record CompilationResult(
        string CSharpSource,
        Map<string, CodexType> Types);

    sealed record IRCompilationResult(
        IRChapter Chapter,
        Map<string, CodexType> Types,
        CapabilityReport? Capabilities = null);

    static CompilationResult? CompileFile(string filePath)
    {
        IRCompilationResult? irResult = CompileToIR(filePath);
        if (irResult is null) return null;

        CSharpEmitter emitter = new();
        string csharpSource = emitter.Emit(irResult.Chapter);
        return new CompilationResult(csharpSource, irResult.Types);
    }

    static IRCompilationResult? CompileToIR(string filePath, Set<string>? grantedCapabilities = null)
    {
        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"File not found: {filePath}");
            return null;
        }

        string content = File.ReadAllText(filePath);
        SourceText source = new(filePath, content);
        DiagnosticBag diagnostics = new();

        DocumentNode document = ParseSourceFile(source, content, diagnostics);

        Desugarer desugarer = new(diagnostics);
        string chapterName = Path.GetFileNameWithoutExtension(filePath);
        Chapter chapter = desugarer.Desugar(document, chapterName);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        NameResolver resolver = CreateResolver(diagnostics,
            Path.GetDirectoryName(Path.GetFullPath(filePath)));
        ResolvedChapter resolved = resolver.Resolve(chapter);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        TypeChecker checker = new(diagnostics);

        // Import types from dependency modules before checking main chapter
        foreach (ResolvedChapter imported in resolved.CitedChapters)
            checker.CiteChapter(imported.Chapter);

        Map<string, CodexType> types = checker.CheckChapter(resolved.Chapter);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        LinearityChecker linearityChecker = new(diagnostics, types);
        linearityChecker.CheckChapter(resolved.Chapter);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        Codex.Proofs.ProofChecker proofChecker = new(diagnostics);
        proofChecker.CheckChapter(resolved.Chapter, types);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        Lowering lowering = new(types, checker.ConstructorMap, checker.TypeDefMap, diagnostics);
        IRChapter irModule = lowering.Lower(resolved.Chapter);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        CapabilityChecker capChecker = new(diagnostics, types);
        CapabilityReport capReport = capChecker.CheckChapter(resolved.Chapter, grantedCapabilities);

        return new IRCompilationResult(irModule, types, capReport);
    }

    static IRCompilationResult? CompileMultipleToIR(
        string[] filePaths, string chapterName, IReadOnlyList<IChapterLoader>? extraLoaders = null,
        Set<string>? grantedCapabilities = null)
    {
        DiagnosticBag diagnostics = new();
        Desugarer desugarer = new(diagnostics);
        List<Chapter> perFileChapters = [];
        List<(string FilePath, PageMarker? Page)> pageMarkers = [];

        foreach (string filePath in filePaths)
        {
            if (!File.Exists(filePath))
            {
                Console.Error.WriteLine($"File not found: {filePath}");
                return null;
            }

            string content = File.ReadAllText(filePath);
            SourceText source = new(filePath, content);
            DocumentNode document = ParseSourceFile(source, content, diagnostics);
            pageMarkers.Add((filePath, document.Page));
            string fileModule = Path.GetFileNameWithoutExtension(filePath);
            Chapter chapter = desugarer.Desugar(document, fileModule);
            perFileChapters.Add(chapter);
        }

        ValidatePageMarkers(pageMarkers, diagnostics);
        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        ChapterScoper scoper = new(diagnostics);
        Chapter combined = scoper.Scope(perFileChapters, chapterName);

        string? baseDir = filePaths.Length > 0
            ? Path.GetDirectoryName(Path.GetFullPath(filePaths[0]))
            : null;
        NameResolver resolver = CreateResolver(diagnostics, baseDir, extraLoaders);
        ResolvedChapter resolved = resolver.Resolve(combined);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        TypeChecker checker = new(diagnostics);

        foreach (ResolvedChapter imported in resolved.CitedChapters)
            checker.CiteChapter(imported.Chapter);

        Map<string, CodexType> types = checker.CheckChapter(resolved.Chapter);

        foreach (ResolvedChapter imported in resolved.CitedChapters)
        {
            TypeChecker importChecker = new(diagnostics);
            importChecker.CheckChapter(imported.Chapter);
        }

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        LinearityChecker linearityChecker = new(diagnostics, types);
        linearityChecker.CheckChapter(resolved.Chapter);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        Codex.Proofs.ProofChecker proofChecker = new(diagnostics);
        proofChecker.CheckChapter(resolved.Chapter, types);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        Lowering lowering = new(types, checker.ConstructorMap, checker.TypeDefMap, diagnostics);
        IRChapter irModule = lowering.Lower(resolved.Chapter);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        CapabilityChecker capChecker = new(diagnostics, types);
        CapabilityReport capReport = capChecker.CheckChapter(resolved.Chapter, grantedCapabilities);

        return new IRCompilationResult(irModule, types, capReport);
    }

    static void PrintDiagnostics(DiagnosticBag diagnostics)
    {
        foreach (Diagnostic diag in diagnostics.ToImmutable())
        {
            string severity = diag.Severity switch
            {
                DiagnosticSeverity.Error => "error",
                DiagnosticSeverity.Warning => "warning",
                DiagnosticSeverity.Info => "info",
                DiagnosticSeverity.Hint => "hint",
                _ => "?"
            };
            Console.Error.WriteLine($"{severity} {diag.Code}: {diag.Message} {diag.Span}");
            foreach (SourceSpan related in diag.RelatedSpans)
            {
                Console.Error.WriteLine($"  note: see {related}");
            }
        }
    }

    static NameResolver CreateResolver(
        DiagnosticBag diagnostics,
        string? baseDirectory = null,
        IReadOnlyList<IChapterLoader>? extraLoaders = null)
    {
        string dir = baseDirectory ?? Directory.GetCurrentDirectory();
        List<IChapterLoader> loaders = [new FileChapterLoader(dir, diagnostics)];

        if (extraLoaders is not null)
        {
            foreach (IChapterLoader loader in extraLoaders)
                loaders.Add(loader);
        }

        ForewordChapterLoader? foreword = ForewordChapterLoader.TryCreate(diagnostics);
        if (foreword is not null)
            loaders.Add(foreword);

        Codex.Repository.FactStore? store =
            Codex.Repository.FactStore.Open(Directory.GetCurrentDirectory());
        if (store is not null)
            loaders.Add(new RepositoryChapterLoader(store, diagnostics));

        return new NameResolver(diagnostics, new CompositeChapterLoader([.. loaders]));
    }

    static IRCompilationResult? CompileViewToIR(
        Codex.Repository.FactStore store, string viewName, string chapterName,
        Set<string>? grantedCapabilities = null)
    {
        ValueMap<string, ContentHash> view = store.GetNamedView(viewName);
        if (view.Count == 0)
        {
            Console.Error.WriteLine($"View '{viewName}' is empty — nothing to compile.");
            return null;
        }

        DiagnosticBag diagnostics = new();
        Desugarer desugarer = new(diagnostics);
        List<Definition> allDefinitions = [];
        List<TypeDef> allTypeDefinitions = [];
        List<ClaimDef> allClaims = [];
        List<ProofDef> allProofs = [];
        List<CitesDecl> allCitations = [];
        List<EffectDef> allEffectDefs = [];

        foreach (KeyValuePair<string, ContentHash> kv in view)
        {
            Codex.Repository.Fact? fact = store.Load(kv.Value);
            if (fact is null)
            {
                Console.Error.WriteLine(
                    $"error: definition '{kv.Key}' references missing fact {kv.Value.ToHex()}");
                return null;
            }
            if (fact.Kind != Codex.Repository.FactKind.Definition)
            {
                Console.Error.WriteLine(
                    $"error: view entry '{kv.Key}' is a {fact.Kind}, expected Definition");
                return null;
            }

            SourceText source = new(kv.Key + ".codex", fact.Content);
            DocumentNode document = ParseSourceFile(source, fact.Content, diagnostics);
            Chapter chapter = desugarer.Desugar(document, kv.Key);

            allDefinitions.AddRange(chapter.Definitions);
            allTypeDefinitions.AddRange(chapter.TypeDefinitions);
            allClaims.AddRange(chapter.Claims);
            allProofs.AddRange(chapter.Proofs);
            allCitations.AddRange(chapter.Citations);
            allEffectDefs.AddRange(chapter.EffectDefs);
        }

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        SourceSpan combinedSpan = allDefinitions.Count > 0
            ? allDefinitions[0].Span
            : SourceSpan.Single(0, 1, 1, "<view>");
        Chapter combined = new(
            QualifiedName.Simple(chapterName),
            allDefinitions,
            allTypeDefinitions,
            allClaims,
            allProofs,
            combinedSpan)
        {
            Citations = allCitations,
            EffectDefs = allEffectDefs
        };

        NameResolver resolver = CreateResolver(diagnostics);
        ResolvedChapter resolved = resolver.Resolve(combined);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        TypeChecker checker = new(diagnostics);

        foreach (ResolvedChapter imported in resolved.CitedChapters)
            checker.CiteChapter(imported.Chapter);

        Map<string, CodexType> types = checker.CheckChapter(resolved.Chapter);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        LinearityChecker linearityChecker = new(diagnostics, types);
        linearityChecker.CheckChapter(resolved.Chapter);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        Codex.Proofs.ProofChecker proofChecker = new(diagnostics);
        proofChecker.CheckChapter(resolved.Chapter, types);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        Lowering lowering = new(types, checker.ConstructorMap, checker.TypeDefMap, diagnostics);
        IRChapter irModule = lowering.Lower(resolved.Chapter);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        CapabilityChecker capChecker = new(diagnostics, types);
        CapabilityReport capReport = capChecker.CheckChapter(resolved.Chapter, grantedCapabilities);

        return new IRCompilationResult(irModule, types, capReport);
    }

    static void ValidatePageMarkers(List<(string FilePath, PageMarker? Page)> markers, DiagnosticBag diagnostics)
    {
        // Check: every file must have a page marker
        foreach (var (filePath, page) in markers)
        {
            if (page is null)
            {
                diagnostics.Warning("CDX0010",
                    $"No page marker in '{Path.GetFileName(filePath)}' — expected 'Page N' or 'Page N of M' at end of file",
                    SourceSpan.Single(0, 1, 1, filePath));
            }
        }

        // Collect files that declare a total (Page N of M)
        var withTotal = markers.Where(m => m.Page?.TotalPages is not null).ToList();
        if (withTotal.Count == 0) return;

        // Group by total to detect count mismatches
        var totals = withTotal.Select(m => m.Page!.TotalPages!.Value).Distinct().ToList();
        if (totals.Count > 1)
        {
            diagnostics.Error("CDX1072",
                $"Page count mismatch: files disagree on total pages ({string.Join(" vs ", totals)})",
                withTotal[0].Page!.Span);
            return;
        }

        int expectedTotal = totals[0];

        // Check for gaps and duplicates
        var pageNumbers = markers
            .Where(m => m.Page is not null)
            .Select(m => (m.FilePath, m.Page!.PageNumber))
            .OrderBy(m => m.PageNumber)
            .ToList();

        HashSet<int> seen = [];
        foreach (var (filePath, pageNum) in pageNumbers)
        {
            if (!seen.Add(pageNum))
            {
                diagnostics.Error("CDX1073",
                    $"Duplicate page number {pageNum} in '{Path.GetFileName(filePath)}'",
                    SourceSpan.Single(0, 1, 1, filePath));
            }
        }

        for (int i = 1; i <= expectedTotal; i++)
        {
            if (!seen.Contains(i))
            {
                diagnostics.Error("CDX1074",
                    $"Missing page {i} of {expectedTotal}",
                    withTotal[0].Page!.Span);
            }
        }
    }
}
