using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Codex.IR;
using Codex.Types;

namespace Codex.Emit.IL;

public sealed class ILEmitter : IAssemblyEmitter
{
    public string TargetName => "IL";

    public byte[] EmitAssembly(IRChapter module, string assemblyName)
    {
        ILAssemblyBuilder builder = new(assemblyName);
        builder.EmitModule(module);
        return builder.Build();
    }
}
