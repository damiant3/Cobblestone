using Codex.Core;
using Codex.Semantics;

namespace Codex.Cli;

sealed class ForewordChapterLoader : IChapterLoader
{
    readonly FileChapterLoader m_inner;

    ForewordChapterLoader(string forewordDir, DiagnosticBag diagnostics)
    {
        m_inner = new FileChapterLoader(forewordDir, diagnostics);
    }

    public ResolvedChapter? Load(string chapterName) => m_inner.Load(chapterName);

    public static ForewordChapterLoader? TryCreate(DiagnosticBag diagnostics)
    {
        string? forewordDir = FindForewordDirectory();
        if (forewordDir is null) return null;
        return new ForewordChapterLoader(forewordDir, diagnostics);
    }

    static string? FindForewordDirectory()
    {
        string? dir = Path.GetDirectoryName(typeof(ForewordChapterLoader).Assembly.Location);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir, "foreword");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }

        string cwdCandidate = Path.Combine(Directory.GetCurrentDirectory(), "foreword");
        if (Directory.Exists(cwdCandidate))
            return cwdCandidate;

        return null;
    }
}
