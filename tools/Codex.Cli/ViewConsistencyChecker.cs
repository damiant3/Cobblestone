using Codex.Core;
using Codex.Syntax;
using Codex.Ast;
using Codex.Semantics;
using Codex.Types;
using Codex.Proofs;
using Codex.Repository;

namespace Codex.Cli;

sealed class ViewConsistencyChecker : IViewConsistencyChecker
{
    public ViewConsistencyResult Check(IReadOnlyList<ViewDefinition> definitions)
    {
        DiagnosticBag diagnostics = new();
        Desugarer desugarer = new(diagnostics);

        List<Definition> allDefinitions = [];
        List<TypeDef> allTypeDefinitions = [];
        List<ClaimDef> allClaims = [];
        List<ProofDef> allProofs = [];
        List<ImportDecl> allImports = [];
        List<ExportDecl> allExports = [];
        List<EffectDef> allEffectDefs = [];

        foreach (ViewDefinition viewDef in definitions)
        {
            SourceText source = new(viewDef.Name + ".codex", viewDef.Source);
            DocumentNode document = ParseSource(source, viewDef.Source, diagnostics);
            Chapter chapter = desugarer.Desugar(document, viewDef.Name);

            allDefinitions.AddRange(chapter.Definitions);
            allTypeDefinitions.AddRange(chapter.TypeDefinitions);
            allClaims.AddRange(chapter.Claims);
            allProofs.AddRange(chapter.Proofs);
            allImports.AddRange(chapter.Imports);
            allExports.AddRange(chapter.Exports);
            allEffectDefs.AddRange(chapter.EffectDefs);
        }

        if (diagnostics.HasErrors)
            return ToResult(diagnostics);

        SourceSpan combinedSpan = allDefinitions.Count > 0
            ? allDefinitions[0].Span
            : SourceSpan.Single(0, 1, 1, "<view>");
        Chapter combined = new(
            QualifiedName.Simple("view"),
            allDefinitions,
            allTypeDefinitions,
            allClaims,
            allProofs,
            combinedSpan)
        {
            Imports = allImports,
            Exports = allExports,
            EffectDefs = allEffectDefs
        };

        NameResolver resolver = new(diagnostics, new CompositeChapterLoader([]));
        ResolvedChapter resolved = resolver.Resolve(combined);

        if (diagnostics.HasErrors)
            return ToResult(diagnostics);

        TypeChecker checker = new(diagnostics);

        foreach (ResolvedChapter imported in resolved.ImportedChapters)
            checker.ImportChapter(imported.Chapter, imported.ExportedNames);

        Map<string, CodexType> types = checker.CheckChapter(resolved.Chapter);

        if (diagnostics.HasErrors)
            return ToResult(diagnostics);

        LinearityChecker linearityChecker = new(diagnostics, types);
        linearityChecker.CheckChapter(resolved.Chapter);

        if (diagnostics.HasErrors)
            return ToResult(diagnostics);

        ProofChecker proofChecker = new(diagnostics);
        proofChecker.CheckChapter(resolved.Chapter, types);

        int claimCount = allClaims.Count;
        int provenCount = allProofs.Count;

        if (diagnostics.HasErrors)
            return ToResult(diagnostics, claimCount, provenCount);

        return new ViewConsistencyResult(true, [], claimCount, provenCount);
    }

    static DocumentNode ParseSource(SourceText source, string content, DiagnosticBag diagnostics)
    {
        if (ProseParser.IsProseDocument(content))
        {
            ProseParser proseParser = new(source, diagnostics);
            return proseParser.ParseDocument();
        }
        Lexer lexer = new(source, diagnostics);
        IReadOnlyList<Token> tokens = lexer.TokenizeAll();
        Parser parser = new(tokens, diagnostics);
        return parser.ParseDocument();
    }

    static ViewConsistencyResult ToResult(DiagnosticBag diagnostics, int claimCount = 0, int provenCount = 0)
    {
        List<string> errors = [];
        foreach (Diagnostic diag in diagnostics.ToImmutable())
        {
            if (diag.Severity == DiagnosticSeverity.Error)
                errors.Add($"{diag.Code}: {diag.Message} {diag.Span}");
        }
        return new ViewConsistencyResult(false, errors, claimCount, provenCount);
    }
}
