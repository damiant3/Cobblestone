using Codex.Core;
using Xunit;

namespace Codex.Types.Tests;

public class UnifierFuelTests
{
    static SourceSpan Span() => SourceSpan.Single(0, 1, 1, "test");

    static CodexType NestedFunctionType(int depth, CodexType leaf)
    {
        CodexType t = leaf;
        for (int i = 0; i < depth; i++)
        {
            t = new FunctionType(IntegerType.s_instance, t);
        }
        return t;
    }

    [Fact]
    public void Unify_moderate_depth_succeeds()
    {
        CodexType deep = NestedFunctionType(64, IntegerType.s_instance);
        DiagnosticBag bag = new();
        Unifier u = new(bag);

        // Not self-equal via record Equals short-circuit — add a fresh var at
        // the leaf on one side so Unify actually recurses down the structure.
        CodexType withVar = NestedFunctionType(64, u.FreshVar());

        Assert.True(u.Unify(deep, withVar, Span()));
        Assert.False(bag.HasErrors);
    }

    [Fact]
    public void Unify_excessive_depth_emits_CDX9001_cleanly()
    {
        // 300 > 256 fuel budget. Unify recurses on FunctionType Parameter +
        // Return pair; structurally identical types short-circuit via Equals,
        // so force real recursion by making one side's leaf a fresh TypeVar.
        DiagnosticBag bag = new();
        Unifier u = new(bag);
        CodexType lhs = NestedFunctionType(300, IntegerType.s_instance);
        CodexType rhs = NestedFunctionType(300, u.FreshVar());

        u.Unify(lhs, rhs, Span());

        Assert.Contains(bag.ToImmutable(), d => d.Code == CdxCodes.ResourceExhausted);
    }

    [Fact]
    public void OccursIn_via_Unify_trips_fuel_on_deep_type()
    {
        // Unifying a fresh var against a deep type triggers occurs-check,
        // which walks the full structure. 300-deep exceeds the 256 budget.
        DiagnosticBag bag = new();
        Unifier u = new(bag);
        TypeVariable v = u.FreshVar();
        CodexType deep = NestedFunctionType(300, IntegerType.s_instance);

        u.Unify(v, deep, Span());

        Assert.Contains(bag.ToImmutable(), d => d.Code == CdxCodes.ResourceExhausted);
    }

    [Fact]
    public void DeepResolve_deep_type_trips_fuel()
    {
        DiagnosticBag bag = new();
        Unifier u = new(bag);
        CodexType deep = NestedFunctionType(300, IntegerType.s_instance);

        u.DeepResolve(deep);

        Assert.Contains(bag.ToImmutable(), d => d.Code == CdxCodes.ResourceExhausted);
    }

    [Fact]
    public void ResourceExhausted_detail_names_the_phase()
    {
        DiagnosticBag bag = new();
        Unifier u = new(bag);
        CodexType deep = NestedFunctionType(300, IntegerType.s_instance);
        u.DeepResolve(deep);

        Diagnostic? fuelDiag = bag.ToImmutable()
            .FirstOrDefault(d => d.Code == CdxCodes.ResourceExhausted);
        Assert.NotNull(fuelDiag);
        Assert.Contains("unifier", fuelDiag.Message);
        Assert.Contains("DeepResolve", fuelDiag.Message);
        Assert.Contains("256", fuelDiag.Message);
    }

    [Fact]
    public void Resolve_cyclic_substitution_halts_via_iteration_budget()
    {
        // Occurs-check prevents Unify from ever installing a cyclic
        // substitution, so the iteration bound on Resolve's while-loop is
        // a secondary guard against compiler bugs. Inject a cycle
        // directly via the test hook and verify Resolve terminates with
        // CDX9001 instead of looping forever.
        DiagnosticBag bag = new();
        Unifier u = new(bag);
        TypeVariable v0 = u.FreshVar();
        TypeVariable v1 = u.FreshVar();
        u.InstallSubstitutionForTesting(v0.Id, v1);
        u.InstallSubstitutionForTesting(v1.Id, v0);

        CodexType result = u.Resolve(v0);

        Assert.Contains(bag.ToImmutable(), d => d.Code == CdxCodes.ResourceExhausted);
        Assert.Equal(ErrorType.s_instance, result);
    }
}
