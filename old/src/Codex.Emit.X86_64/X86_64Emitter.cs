using Codex.IR;

namespace Codex.Emit.X86_64;

public enum X86_64Target
{
    LinuxUser,
    BareMetal
}

public enum X86_64ExitMode
{
    // Bare-metal default: REPL loop that re-runs `opening` forever. Preserves existing behavior.
    Repl,
    // After `opening` returns, `out 0xf4, al=0` triggers QEMU's isa-debug-exit device
    // (requires QEMU invocation with `-device isa-debug-exit,iobase=0xf4,iosize=0x04`).
    // Falls through to a halt loop if the device isn't present.
    QemuExit,
    // ACPI S5 soft-off — real-hardware shutdown. Not yet implemented; compile aborts in Program.Build.
    AcpiS5
}

public enum X86_64WatchdogMode
{
    // Default: progress-based watchdog fires at 550 ticks (~30s @ 18.2 Hz PIT) when neither R10
    // (heap pointer) nor RIP has moved. Loose; a tight in-function loop that doesn't allocate
    // can hang unnoticed for the full window.
    Progress,
    // Debug: every function prologue calls __pet_watchdog (resets the stale-tick counter).
    // Threshold tightens to 20 ticks (~1.1s), so a hang inside a single function that
    // makes no calls fires quickly. Adds ~5 bytes per function prologue; only for debug
    // runs. No effect on Linux-user target.
    Pet
}

public sealed class X86_64Emitter(X86_64Target target = X86_64Target.LinuxUser, bool diagnostic = false, X86_64ExitMode exitMode = X86_64ExitMode.Repl, X86_64WatchdogMode watchdogMode = X86_64WatchdogMode.Progress) : IAssemblyEmitter
{
    readonly X86_64Target m_target = target;
    readonly bool m_diagnostic = diagnostic;
    readonly X86_64ExitMode m_exitMode = exitMode;
    readonly X86_64WatchdogMode m_watchdogMode = watchdogMode;

    public string TargetName => m_target == X86_64Target.BareMetal ? "X86_64-BareMetal" : "X86_64";

    X86_64CodeGen? m_lastCodeGen;

    public byte[] EmitAssembly(IRChapter module, string assemblyName)
    {
        X86_64CodeGen codeGen = new(m_target, m_diagnostic, m_exitMode, m_watchdogMode);
        codeGen.EmitModule(module);
        m_lastCodeGen = codeGen;
        return codeGen.BuildElf();
    }

    public Dictionary<string, int>? GetFunctionOffsets() => m_lastCodeGen?.GetFunctionOffsets();
    public Dictionary<string, int>? GetFunctionFrameSizes() => m_lastCodeGen?.GetFunctionFrameSizes();

    public IReadOnlyList<string> GetUnresolvedCallTargets() =>
        m_lastCodeGen?.GetUnresolvedCallTargets() ?? [];
    public IReadOnlyList<string> GetUnresolvedFuncAddrFixups() =>
        m_lastCodeGen?.GetUnresolvedFuncAddrFixups() ?? [];

    internal void InjectUnresolvedFuncAddrFixupForTesting(string name) =>
        m_lastCodeGen?.InjectUnresolvedFuncAddrFixupForTesting(name);

    public bool EmitFuelExhausted => m_lastCodeGen?.EmitFuelExhausted ?? false;
    public int MaxEmitDepth => m_lastCodeGen?.MaxEmitDepthLimit ?? 256;
}
