using Codex.IR;

namespace Codex.Emit.X86_64;

public enum X86_64Target
{
    LinuxUser,
    BareMetal
}

public sealed class X86_64Emitter(X86_64Target target = X86_64Target.LinuxUser) : IAssemblyEmitter
{
    readonly X86_64Target m_target = target;

    public string TargetName => m_target == X86_64Target.BareMetal ? "X86_64-BareMetal" : "X86_64";

    X86_64CodeGen? m_lastCodeGen;

    public byte[] EmitAssembly(IRModule module, string assemblyName)
    {
        X86_64CodeGen codeGen = new(m_target);
        codeGen.EmitModule(module);
        m_lastCodeGen = codeGen;
        return codeGen.BuildElf();
    }

    public Dictionary<string, int>? GetFunctionOffsets() => m_lastCodeGen?.GetFunctionOffsets();
    public Dictionary<string, int>? GetFunctionFrameSizes() => m_lastCodeGen?.GetFunctionFrameSizes();
}
