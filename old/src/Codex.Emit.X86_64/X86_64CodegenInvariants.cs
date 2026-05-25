using Codex.Core;

namespace Codex.Emit.X86_64;

// Post-codegen invariant pass for the x86-64 backend. The doc's section B
// invariants in concrete form here:
//   (1) every direct CALL (m_callPatches) resolves to an emitted function
//   (2) every FuncAddrFixup resolves to an emitted function
// A violation is a dangling patch: the byte slot was emitted but never
// filled in. X86_64CodeGen currently logs a warning for (1) and silently
// skips (2); this verifier promotes both to hard failures under opt-in.
//
// Both classes are reported in one exception if both fire, so a bad run
// surfaces everything in a single pass rather than forcing a recompile
// per class.
//
// Other bare-metal backends (RISC-V, ARM64) have the same pattern and get
// their own verifier class in follow-up CLs. Text backends (C#, Codex)
// delegate validation to the downstream compiler.
public static class X86_64CodegenInvariants
{
    public static void Verify(X86_64Emitter emitter)
    {
        IReadOnlyList<string> unresolvedCalls = emitter.GetUnresolvedCallTargets();
        IReadOnlyList<string> unresolvedFixups = emitter.GetUnresolvedFuncAddrFixups();

        if (unresolvedCalls.Count == 0 && unresolvedFixups.Count == 0)
            return;

        List<string> parts = [];
        if (unresolvedCalls.Count > 0)
        {
            parts.Add($"{unresolvedCalls.Count} unresolved direct call target(s): [{string.Join(",", SortedDistinct(unresolvedCalls))}]");
        }
        if (unresolvedFixups.Count > 0)
        {
            parts.Add($"{unresolvedFixups.Count} unresolved function-address fixup(s): [{string.Join(",", SortedDistinct(unresolvedFixups))}]");
        }

        throw new InvariantViolationException(
            phase: "codegen",
            invariant: "every call target and every FuncAddrFixup resolves to an emitted function",
            detail: "x86-64: " + string.Join("; ", parts));
    }

    static IEnumerable<string> SortedDistinct(IReadOnlyList<string> names)
    {
        HashSet<string> set = [];
        foreach (string n in names)
            set.Add(n);
        List<string> sorted = [.. set];
        sorted.Sort(StringComparer.Ordinal);
        return sorted;
    }
}
