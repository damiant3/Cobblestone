namespace Codex.Emit;

// Per-target register-convention descriptor. Each register backend
// (X86_64 / Arm64 / RiscV) provides a subclass that supplies its
// named reserved registers — the ones with load-bearing semantics
// (heap pointer, result-space pointer, etc.). Adding a new named
// register here forces every target to state what it uses for that
// role (or opt out via `=> null` on a nullable slot).
//
// Pool conventions (temp/local register ranges) are intentionally NOT
// modeled here: the three targets allocate from those pools with
// different strategies (X86_64 indexes into static byte[] arrays;
// Arm64 and RiscV walk uint counters through contiguous ranges).
// Unifying the pools would require redesigning each target's
// allocator, which is out of scope for a convention descriptor.
public abstract class RegisterAllocatorConfig
{
    public abstract string TargetName { get; }

    // Working-space heap pointer. Every target reserves one — X86 uses R10,
    // Arm64 uses X28, RiscV uses S1. Stored as uint for cross-target
    // uniformity; X86 narrows to byte at use sites.
    public abstract uint HeapReg { get; }
}
