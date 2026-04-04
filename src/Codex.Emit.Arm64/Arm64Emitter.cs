using Codex.IR;

namespace Codex.Emit.Arm64;

public sealed class Arm64Emitter : IAssemblyEmitter
{
    public string TargetName => "Arm64";

    public byte[] EmitAssembly(IRChapter module, string assemblyName)
    {
        Arm64CodeGen codeGen = new();
        codeGen.EmitModule(module);
        return codeGen.BuildElf();
    }
}
