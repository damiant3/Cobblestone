using Codex.Ast;
using Codex.Core;
using Codex.Repository;
using Codex.Semantics;
using Codex.Syntax;

namespace Codex.Cli;

sealed class RepositoryChapterLoader(FactStore store, DiagnosticBag diagnostics) : IChapterLoader
{
    readonly FactStore m_store = store;
    readonly DiagnosticBag m_diagnostics = diagnostics;
    Map<string, ResolvedChapter> m_cache = Map<string, ResolvedChapter>.s_empty;

    public ResolvedChapter? Load(string quire, string chapterName)
    {
        // Repository views are keyed by a single string today; a (quire, chapter)
        // pair maps to a "Quire/ChapterName" view name.
        string viewKey = $"{quire}/{chapterName}";
        ResolvedChapter? cached = m_cache[viewKey];
        if (cached is not null)
            return cached;

        ContentHash? hash = m_store.LookupView(viewKey);
        if (hash is null)
            return null;

        Fact? fact = m_store.Load(hash.Value);
        if (fact is null || fact.Kind != FactKind.Definition)
            return null;

        string source = fact.Content;
        SourceText src = new($"{chapterName}.codex", source);
        DiagnosticBag compileDiag = new();

        DocumentNode document = DocumentParser.Parse(src, compileDiag);
        if (compileDiag.HasErrors)
            return null;

        Desugarer desugarer = new(compileDiag);
        Chapter chapter = desugarer.Desugar(document, chapterName);
        if (compileDiag.HasErrors)
            return null;

        NameResolver resolver = new(compileDiag);
        ResolvedChapter resolved = resolver.Resolve(chapter);
        if (compileDiag.HasErrors)
            return null;

        m_cache = m_cache.Set(viewKey, resolved);
        return resolved;
    }
}
