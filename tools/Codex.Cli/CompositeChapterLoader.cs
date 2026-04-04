using Codex.Semantics;

namespace Codex.Cli;

sealed class CompositeChapterLoader(params IChapterLoader[] loaders) : IChapterLoader
{
    readonly IChapterLoader[] m_loaders = loaders;

    public ResolvedChapter? Load(string chapterName)
    {
        foreach (IChapterLoader loader in m_loaders)
        {
            ResolvedChapter? result = loader.Load(chapterName);
            if (result is not null)
                return result;
        }
        return null;
    }
}
