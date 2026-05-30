using Codex.IR;

namespace Codex.Emit;

public interface ICodeEmitter
{
    string TargetName { get; }

    string FileExtension { get; }

    string Emit(IRChapter module);

    // True iff the most recent Emit call exceeded its recursion budget.
    // Text backends (C#, Codex) default to false; emit may still produce a
    // string whose content is a partial tree with sentinel tokens where the
    // recursion was cut. CLI wrappers promote this flag to CDX9001.
    bool EmitFuelExhausted => false;
    int MaxEmitDepth => 256;
}
