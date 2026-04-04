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

    public ResolvedChapter? Load(string chapterName)
    {
        ResolvedChapter? cached = m_cache[chapterName];
        if (cached is not null)
            return cached;

        ContentHash? hash = m_store.LookupView(chapterName);
        if (hash is null)
            return null;

        Fact? fact = m_store.Load(hash.Value);
        if (fact is null || fact.Kind != FactKind.Definition)
            return null;

        string source = fact.Content;
        SourceText src = new($"{chapterName}.codex", source);
        DiagnosticBag compileDiag = new();

        DocumentNode document;
        if (ProseParser.IsProseDocument(source))
        {
            ProseParser proseParser = new(src, compileDiag);
            document = proseParser.ParseDocument();
        }
        else
        {
            Lexer lexer = new(src, compileDiag);
            IReadOnlyList<Token> tokens = lexer.TokenizeAll();
            Parser parser = new(tokens, compileDiag);
            document = parser.ParseDocument();
        }

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

        m_cache = m_cache.Set(chapterName, resolved);
        return resolved;
    }
}
