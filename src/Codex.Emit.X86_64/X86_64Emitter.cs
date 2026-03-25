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

    public byte[] EmitAssembly(IRModule module, string assemblyName)
    {
        X86_64CodeGen codeGen = new(m_target);
        codeGen.EmitModule(module);
        // Bare metal: use ELF with multiboot header (QEMU needs ELF format)
        // The multiboot header is embedded in .text, entry point is byte 12
        // (right after the 12-byte multiboot header)
        return codeGen.BuildElf();
    }
}
