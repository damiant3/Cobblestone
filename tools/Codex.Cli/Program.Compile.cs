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

    static IRCompilationResult? CompileToIR(string filePath, Set<string>? grantedCapabilities = null, PhaseDumpSink? dumpSink = null, bool liftLambdas = false)
    {
        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"File not found: {filePath}");
            return null;
        }

        string content = File.ReadAllText(filePath);
        SourceText source = new(filePath, content);
        DiagnosticBag diagnostics = new();
        string chapterName = Path.GetFileNameWithoutExtension(filePath);

        DocumentNode document = ParseSourceFile(source, content, diagnostics);
        dumpSink?.DumpParsed(chapterName, [(filePath, document)]);

        Desugarer desugarer = new(diagnostics);
        Chapter chapter = desugarer.Desugar(document, chapterName);
        dumpSink?.DumpDesugared(chapterName, chapter);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        NameResolver resolver = CreateResolver(diagnostics,
            Path.GetDirectoryName(Path.GetFullPath(filePath)));
        ResolvedChapter resolved = resolver.Resolve(chapter);
        dumpSink?.DumpResolved(chapterName, resolved);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        if (s_verifyInvariants)
        {
            InvariantVerifier.AfterResolution(resolved);
        }

        TypeChecker checker = new(diagnostics);

        // Import types from dependency modules before checking main chapter
        foreach (ResolvedChapter imported in resolved.CitedChapters)
            checker.CiteChapter(imported.Chapter);

        Map<string, CodexType> types = checker.CheckChapter(resolved.Chapter);
        dumpSink?.DumpTyped(chapterName, resolved, types, checker.ExprTypes);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        List<TypedImport> citedImports = TypedCitations.Check(resolved.CitedChapters, diagnostics);

        if (s_verifyInvariants)
        {
            TypeCheckInvariants.Verify(resolved.Chapter, types);
            TypeCheckInvariants.VerifyElaboration(resolved.Chapter, checker.ExprTypes);
        }

        LinearityChecker linearityChecker = new(diagnostics, types);
        linearityChecker.CheckChapter(resolved.Chapter);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        Codex.Proofs.ProofChecker proofChecker = new(diagnostics);
        proofChecker.CheckChapter(resolved.Chapter, types);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        Lowering lowering = new(types, checker.ConstructorMap, checker.TypeDefMap, diagnostics, checker.ExprTypes);
        IRChapter irModule = lowering.Lower(resolved);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        irModule = Lowering.LowerCitedDefs(citedImports, irModule, diagnostics, useExprTypes: true);
        if (liftLambdas)
        {
            irModule = LambdaLifting.Lift(irModule);
        }
        dumpSink?.DumpIR(chapterName, irModule);

        if (s_verifyInvariants)
        {
            LoweringInvariants.Verify(irModule);
        }

        CapabilityChecker capChecker = new(diagnostics, types);
        CapabilityReport capReport = capChecker.CheckChapter(resolved.Chapter, grantedCapabilities);

        return new IRCompilationResult(irModule, types, capReport);
    }

    static IRCompilationResult? CompileMultipleToIR(
        string[] filePaths, string chapterName, IReadOnlyList<IChapterLoader>? extraLoaders = null,
        Set<string>? grantedCapabilities = null, string? codexRoot = null, PhaseDumpSink? dumpSink = null,
        bool liftLambdas = false)
    {
        DiagnosticBag diagnostics = new();
        Desugarer desugarer = new(diagnostics);
        List<Chapter> perFileChapters = [];
        List<(string FilePath, DocumentNode Document)> parsedDocs = [];
        List<(string FilePath, string? Quire, string ChapterName, PageMarker? Page)> pageMarkers = [];

        // If no explicit root was given, infer it from the first file's directory.
        // This keeps bare `codex build <file>` behavior stable (null quire for everything).
        string? inferredRoot = codexRoot ?? (filePaths.Length > 0
            ? Path.GetDirectoryName(Path.GetFullPath(filePaths[0]))
            : null);

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
            parsedDocs.Add((filePath, document));
            SourceSpan fileSpan = SourceSpan.Single(0, 1, 1, filePath);
            if (document.Chapters.Count == 0)
            {
                diagnostics.Error(CdxCodes.FileMissingChapter,
                    $"'{Path.GetFileName(filePath)}' has no 'Chapter:' header",
                    fileSpan);
                continue;
            }
            if (document.Chapters.Count > 1)
            {
                diagnostics.Error(CdxCodes.FileMultipleChapters,
                    $"'{Path.GetFileName(filePath)}' declares {document.Chapters.Count} chapters; one per file",
                    fileSpan);
                continue;
            }
            string fileModule = document.Chapters[0].Title;
            string? quire = inferredRoot is not null
                ? QuireNameFor(filePath, inferredRoot)
                : null;
            pageMarkers.Add((filePath, quire, fileModule, document.Page));
            Chapter chapter = PhaseTimer.Time("desugar", () => desugarer.Desugar(document, fileModule) with { Quire = quire });
            perFileChapters.Add(chapter);
        }

        dumpSink?.DumpParsed(chapterName, parsedDocs);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        ValidatePageMarkers(pageMarkers, diagnostics);
        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        ChapterScoper scoper = new(diagnostics);
        Chapter combined = PhaseTimer.Time("scope", () => scoper.Scope(perFileChapters, chapterName));
        dumpSink?.DumpDesugared(chapterName, combined);

        string? baseDir = filePaths.Length > 0
            ? Path.GetDirectoryName(Path.GetFullPath(filePaths[0]))
            : null;
        NameResolver resolver = CreateResolver(diagnostics, baseDir, extraLoaders);
        ResolvedChapter resolved = PhaseTimer.Time("resolve", () => resolver.Resolve(combined));
        dumpSink?.DumpResolved(chapterName, resolved);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        if (s_verifyInvariants)
        {
            InvariantVerifier.AfterResolution(resolved);
        }

        TypeChecker checker = new(diagnostics);

        foreach (ResolvedChapter imported in resolved.CitedChapters)
            checker.CiteChapter(imported.Chapter);

        Map<string, CodexType> types = PhaseTimer.Time("check", () => checker.CheckChapter(resolved.Chapter));
        dumpSink?.DumpTyped(chapterName, resolved, types, checker.ExprTypes);

        List<TypedImport> citedImports = TypedCitations.Check(resolved.CitedChapters, diagnostics);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        if (s_verifyInvariants)
        {
            TypeCheckInvariants.Verify(resolved.Chapter, types);
            TypeCheckInvariants.VerifyElaboration(resolved.Chapter, checker.ExprTypes);
        }

        LinearityChecker linearityChecker = new(diagnostics, types);
        linearityChecker.CheckChapter(resolved.Chapter);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        Codex.Proofs.ProofChecker proofChecker = new(diagnostics);
        proofChecker.CheckChapter(resolved.Chapter, types);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        Lowering lowering = new(types, checker.ConstructorMap, checker.TypeDefMap, diagnostics, checker.ExprTypes);
        IRChapter irModule = PhaseTimer.Time("lower", () => lowering.Lower(resolved));

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        irModule = Lowering.LowerCitedDefs(citedImports, irModule, diagnostics, useExprTypes: true);
        if (liftLambdas)
        {
            irModule = LambdaLifting.Lift(irModule);
        }
        dumpSink?.DumpIR(chapterName, irModule);

        if (s_verifyInvariants)
        {
            LoweringInvariants.Verify(irModule);
        }

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
        Set<string>? grantedCapabilities = null, bool liftLambdas = false)
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
        // View-mode compile has no dump sink threading today; skip DumpTyped here.

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        List<TypedImport> citedImports = TypedCitations.Check(resolved.CitedChapters, diagnostics);

        if (s_verifyInvariants)
        {
            TypeCheckInvariants.Verify(resolved.Chapter, types);
            TypeCheckInvariants.VerifyElaboration(resolved.Chapter, checker.ExprTypes);
        }

        LinearityChecker linearityChecker = new(diagnostics, types);
        linearityChecker.CheckChapter(resolved.Chapter);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        Codex.Proofs.ProofChecker proofChecker = new(diagnostics);
        proofChecker.CheckChapter(resolved.Chapter, types);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        Lowering lowering = new(types, checker.ConstructorMap, checker.TypeDefMap, diagnostics, checker.ExprTypes);
        IRChapter irModule = lowering.Lower(resolved);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        irModule = Lowering.LowerCitedDefs(citedImports, irModule, diagnostics, useExprTypes: true);
        if (liftLambdas)
        {
            irModule = LambdaLifting.Lift(irModule);
        }

        if (s_verifyInvariants)
        {
            LoweringInvariants.Verify(irModule);
        }

        CapabilityChecker capChecker = new(diagnostics, types);
        CapabilityReport capReport = capChecker.CheckChapter(resolved.Chapter, grantedCapabilities);

        return new IRCompilationResult(irModule, types, capReport);
    }

    static void ValidatePageMarkers(List<(string FilePath, string? Quire, string ChapterName, PageMarker? Page)> markers, DiagnosticBag diagnostics)
    {
        // Group every file by (quire, chapter-name). A chapter is identified by
        // (quire, name); same name in different quires = different chapters.
        IEnumerable<IGrouping<(string? Quire, string ChapterName), (string FilePath, string? Quire, string ChapterName, PageMarker? Page)>> byChapter = markers.GroupBy(m => (m.Quire, m.ChapterName));

        foreach (IGrouping<(string? Quire, string ChapterName), (string FilePath, string? Quire, string ChapterName, PageMarker? Page)> group in byChapter)
        {
            string chapterLabel = group.Key.Quire is null
                ? group.Key.ChapterName
                : $"{group.Key.Quire}/{group.Key.ChapterName}";
            List<(string FilePath, string? Quire, string ChapterName, PageMarker? Page)> files = group.ToList();

            // Single-file chapter: no collision, no page coherence to check.
            if (files.Count == 1) continue;

            // Multi-file chapter: all files must carry 'Page N of M' markers
            // agreeing on M; any file without that marker means this is a
            // chapter-name collision, not a legitimate split.
            List<(string FilePath, string? Quire, string ChapterName, PageMarker? Page)> unpaged = files.Where(m => m.Page?.TotalPages is null).ToList();
            if (unpaged.Count > 0)
            {
                SourceSpan firstSpan = SourceSpan.Single(0, 1, 1, files[0].FilePath);
                string names = string.Join(", ", files.Select(f => $"'{Path.GetFileName(f.FilePath)}'"));
                diagnostics.Error(CdxCodes.DuplicateChapterInQuire,
                    $"Chapter '{chapterLabel}' is declared by {files.Count} files ({names}) — within a quire each chapter name is unique; to split one chapter across files add 'Page N of M' markers to every file",
                    firstSpan);
                continue;
            }

            List<int> totals = files.Select(m => m.Page!.TotalPages!.Value).Distinct().ToList();
            if (totals.Count > 1)
            {
                diagnostics.Error(CdxCodes.PageCountMismatch,
                    $"Page count mismatch in '{chapterLabel}': files disagree ({string.Join(" vs ", totals)})",
                    files[0].Page!.Span);
                continue;
            }

            int expectedTotal = totals[0];
            if (expectedTotal != files.Count)
            {
                diagnostics.Error(CdxCodes.PageCountMismatch,
                    $"Chapter '{chapterLabel}' declares 'of {expectedTotal}' but {files.Count} files carry its header",
                    files[0].Page!.Span);
                continue;
            }

            HashSet<int> seen = [];
            foreach ((string FilePath, string? Quire, string ChapterName, PageMarker? Page) m in files)
            {
                if (!seen.Add(m.Page!.PageNumber))
                {
                    diagnostics.Error(CdxCodes.DuplicatePage,
                        $"Duplicate page {m.Page.PageNumber} in '{chapterLabel}'",
                        m.Page.Span);
                }
            }

            for (int i = 1; i <= expectedTotal; i++)
            {
                if (!seen.Contains(i))
                {
                    diagnostics.Error(CdxCodes.MissingPage,
                        $"Missing page {i} of {expectedTotal} in '{chapterLabel}'",
                        files[0].Page!.Span);
                }
            }
        }

        // Also emit the existing "missing page marker" warning for every file
        // that lacks one. (Not an error — single-file chapters don't need one.)
        foreach ((string filePath, string? _, string _, PageMarker? page) in markers)
        {
            if (page is null)
            {
                diagnostics.Warning(CdxCodes.MissingPageMarker,
                    $"No page marker in '{Path.GetFileName(filePath)}' — expected 'Page N' or 'Page N of M' at end of file",
                    SourceSpan.Single(0, 1, 1, filePath));
            }
        }
    }
}
