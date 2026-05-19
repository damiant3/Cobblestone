using Codex.IR;

namespace Codex.Emit.Wasm;

public sealed class WasmEmitter : IAssemblyEmitter
{
    public string TargetName => "Wasm";

    public byte[] EmitAssembly(IRChapter module, string assemblyName)
    {
        WasmModuleBuilder builder = new();
        builder.EmitModule(module);
        return builder.Build();
    }
}
