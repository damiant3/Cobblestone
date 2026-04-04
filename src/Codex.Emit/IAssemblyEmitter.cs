using Codex.IR;

namespace Codex.Emit;

public interface IAssemblyEmitter
{
    string TargetName { get; }

    byte[] EmitAssembly(IRChapter module, string assemblyName);
}
