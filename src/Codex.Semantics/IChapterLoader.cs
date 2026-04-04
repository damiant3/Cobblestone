using Codex.Core;
using Codex.Ast;

namespace Codex.Semantics;

public interface IChapterLoader
{
    ResolvedChapter? Load(string chapterName);
}
