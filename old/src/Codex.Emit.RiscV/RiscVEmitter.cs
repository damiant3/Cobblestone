using Codex.IR;

namespace Codex.Emit.RiscV;

public enum RiscVTarget
{
    LinuxUser,
    BareMetal
}

public sealed class RiscVEmitter(RiscVTarget target = RiscVTarget.LinuxUser) : IAssemblyEmitter
{
    readonly RiscVTarget m_target = target;

    public string TargetName => m_target == RiscVTarget.BareMetal ? "RiscV-BareMetal" : "RiscV";

    public byte[] EmitAssembly(IRChapter module, string assemblyName)
    {
        RiscVCodeGen codeGen = new(m_target);
        codeGen.EmitModule(module);
        return codeGen.BuildElf();
    }
}
