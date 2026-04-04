using Codex.Ast;
using Codex.Core;
using Codex.Semantics;
using Codex.Syntax;

namespace Codex.Cli;

sealed class FileChapterLoader(string baseDirectory, DiagnosticBag diagnostics) : IChapterLoader
{
    readonly string m_baseDirectory = baseDirectory;
    readonly DiagnosticBag m_diagnostics = diagnostics;
    Map<string, ResolvedChapter> m_cache = Map<string, ResolvedChapter>.s_empty;

    public ResolvedChapter? Load(string chapterName)
    {
        ResolvedChapter? cached = m_cache[chapterName];
        if (cached is not null)
            return cached;

        string filePath = Path.Combine(m_baseDirectory, chapterName + ".codex");
        if (!File.Exists(filePath))
        {
            // Try lowercase
            filePath = Path.Combine(m_baseDirectory,
                chapterName.ToLowerInvariant() + ".codex");
            if (!File.Exists(filePath))
                return null;
        }

        string source = File.ReadAllText(filePath);
        SourceText src = new(filePath, source);
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

        // Use a fresh FileChapterLoader for transitive imports from the same directory
        FileChapterLoader transitiveLoader = new(
            Path.GetDirectoryName(filePath) ?? m_baseDirectory, compileDiag);
        NameResolver resolver = new(compileDiag, transitiveLoader);
        ResolvedChapter resolved = resolver.Resolve(chapter);
        if (compileDiag.HasErrors)
            return null;

        m_cache = m_cache.Set(chapterName, resolved);
        return resolved;
    }
}
