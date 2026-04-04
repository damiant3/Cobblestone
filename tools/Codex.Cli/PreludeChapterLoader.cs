using Codex.Core;
using Codex.Semantics;

namespace Codex.Cli;

sealed class PreludeChapterLoader : IChapterLoader
{
    readonly FileChapterLoader m_inner;

    PreludeChapterLoader(string preludeDir, DiagnosticBag diagnostics)
    {
        m_inner = new FileChapterLoader(preludeDir, diagnostics);
    }

    public ResolvedChapter? Load(string chapterName) => m_inner.Load(chapterName);

    public static PreludeChapterLoader? TryCreate(DiagnosticBag diagnostics)
    {
        string? preludeDir = FindPreludeDirectory();
        if (preludeDir is null) return null;
        return new PreludeChapterLoader(preludeDir, diagnostics);
    }

    static string? FindPreludeDirectory()
    {
        string? dir = Path.GetDirectoryName(typeof(PreludeChapterLoader).Assembly.Location);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir, "prelude");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }

        string cwdCandidate = Path.Combine(Directory.GetCurrentDirectory(), "prelude");
        if (Directory.Exists(cwdCandidate))
            return cwdCandidate;

        return null;
    }
}
