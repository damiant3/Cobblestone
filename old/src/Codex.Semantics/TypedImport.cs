using Codex.Ast;
using Codex.Core;
using Codex.Types;

namespace Codex.Semantics;

public sealed record TypedImport(
    ResolvedChapter Resolved,
    Map<string, CodexType> Types,
    Map<string, CtorInfo> ConstructorMap,
    Map<string, CodexType> TypeDefMap,
    IReadOnlyDictionary<Expr, CodexType> ExprTypes)
{
    public Chapter Chapter => Resolved.Chapter;
}

public static class TypedCitations
{
    // Type-check each direct citation once and return a cache the driver and
    // Lowering.LowerCitedDefs can consume without reconstructing a TypeChecker.
    // Each imported chapter sees every *other* direct citation via CiteChapter,
    // matching the cross-cite fan-out that LowerCitedDefs used to do inline.
    public static List<TypedImport> Check(
        IReadOnlyList<ResolvedChapter> citedChapters,
        DiagnosticBag diagnostics)
    {
        List<TypedImport> result = new(citedChapters.Count);
        foreach (ResolvedChapter imported in citedChapters)
        {
            TypeChecker checker = new(diagnostics);
            foreach (ResolvedChapter other in citedChapters)
            {
                if (!ReferenceEquals(other, imported))
                    checker.CiteChapter(other.Chapter);
            }
            Map<string, CodexType> types = checker.CheckChapter(imported.Chapter);
            result.Add(new TypedImport(
                imported,
                types,
                checker.ConstructorMap,
                checker.TypeDefMap,
                checker.ExprTypes));
        }
        return result;
    }
}
