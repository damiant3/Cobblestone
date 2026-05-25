using Codex.Core;
using Codex.Ast;

namespace Codex.Semantics;

public interface IChapterLoader
{
    /// <summary>
    /// Load a chapter identified by (<paramref name="quire"/>, <paramref name="chapterName"/>).
    /// The quire name is the top-level subdirectory basename of the containing codex;
    /// for stdlib-style codexes presented as a single quire the quire name is the
    /// project's short name.
    /// </summary>
    ResolvedChapter? Load(string quire, string chapterName);
}
