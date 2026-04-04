using Codex.IR;

namespace Codex.Emit;

public interface ICodeEmitter
{
    string TargetName { get; }

    string FileExtension { get; }

    string Emit(IRChapter module);
}
