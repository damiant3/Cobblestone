using Codex.IR;

namespace Codex.Emit.X86_64;

public sealed class X86_64Emitter : IAssemblyEmitter
{
    public string TargetName => "X86_64";

    public byte[] EmitAssembly(IRModule module, string assemblyName)
    {
        X86_64CodeGen codeGen = new();
        codeGen.EmitModule(module);
        return codeGen.BuildElf();
    }
}
