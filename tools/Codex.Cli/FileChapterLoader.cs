using Codex.Ast;
using Codex.Core;
using Codex.Semantics;
using Codex.Syntax;

namespace Codex.Cli;

/// <summary>
/// Loads chapters from a codex directory. The quire parameter is resolved to
/// a subdirectory of <paramref name="baseDirectory"/> — unless it matches
/// <paramref name="virtualQuireName"/>, in which case the root directory
/// itself is treated as the quire body (used by stdlib-style codexes like
/// the foreword, whose chapter files live at project root but are presented
/// as a single named quire).
/// </summary>
sealed class FileChapterLoader(
    string baseDirectory,
    DiagnosticBag diagnostics,
    string? virtualQuireName = null) : IChapterLoader
{
    readonly string m_baseDirectory = baseDirectory;
    readonly DiagnosticBag m_diagnostics = diagnostics;
    readonly string? m_virtualQuireName = virtualQuireName;
    Map<string, ResolvedChapter> m_cache = Map<string, ResolvedChapter>.s_empty;

    public ResolvedChapter? Load(string quire, string chapterName)
    {
        string key = $"{quire}::{chapterName}";
        ResolvedChapter? cached = m_cache[key];
        if (cached is not null)
            return cached;

        string quireDir = (m_virtualQuireName is not null && quire == m_virtualQuireName)
            ? m_baseDirectory
            : Path.Combine(m_baseDirectory, quire);
        if (!Directory.Exists(quireDir))
            return null;

        string? filePath = FindFileForChapter(quireDir, chapterName);
        if (filePath is null)
            return null;

        string source = File.ReadAllText(filePath);
        SourceText src = new(filePath, source);
        DiagnosticBag compileDiag = new();

        DocumentNode document = DocumentParser.Parse(src, compileDiag);
        if (compileDiag.HasErrors)
            return null;

        Desugarer desugarer = new(compileDiag);
        Chapter chapter = desugarer.Desugar(document, chapterName);
        if (compileDiag.HasErrors)
            return null;

        // Transitive imports resolve against the same codex root + quire layout.
        FileChapterLoader transitiveLoader = new(m_baseDirectory, compileDiag, m_virtualQuireName);
        NameResolver resolver = new(compileDiag, transitiveLoader);
        ResolvedChapter resolved = resolver.Resolve(chapter);
        if (compileDiag.HasErrors)
            return null;

        m_cache = m_cache.Set(key, resolved);
        return resolved;
    }

    static string? FindFileForChapter(string quireDir, string chapterName)
    {
        foreach (string file in Directory.GetFiles(quireDir, "*.codex"))
        {
            string? firstLine = null;
            using (StreamReader r = new(file))
                firstLine = r.ReadLine();
            if (firstLine is null) continue;
            if (!firstLine.StartsWith("Chapter:", StringComparison.Ordinal)) continue;
            string title = firstLine["Chapter:".Length..].Trim();
            if (title == chapterName)
                return file;
        }
        return null;
    }
}
