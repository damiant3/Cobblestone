using System.Collections.Immutable;
using Codex.Core;
using Codex.Emit.X86_64;
using Codex.IR;
using Xunit;

namespace Codex.Types.Tests;

public class X86_64EmitterFuelTests
{
    static IRExpr NestedLet(int depth)
    {
        // let v0 = 0 in let v1 = 0 in ... let vN-1 = 0 in 0
        // EmitLet recurses on Value and Body; Body is another IRLet so EmitExpr
        // re-enters on each level.
        IRExpr body = new IRIntegerLit(0L);
        for (int i = depth - 1; i >= 0; i--)
        {
            body = new IRLet(
                $"v{i}", IntegerType.s_instance,
                new IRIntegerLit(0L), body);
        }
        return body;
    }

    [Fact]
    public void Emitter_deep_nested_let_sets_fuel_flag()
    {
        IRDefinition opening = new(
            "opening", [], IntegerType.s_instance, NestedLet(400));
        IRChapter ir = new(
            QualifiedName.Simple("T"), [opening], Map<string, CodexType>.s_empty);

        X86_64Emitter emitter = new();
        emitter.EmitAssembly(ir, "fuel");

        Assert.True(emitter.EmitFuelExhausted, "deep IR should trip the 256 emit-depth budget");
        Assert.Equal(256, emitter.MaxEmitDepth);
    }

    [Fact]
    public void Emitter_moderate_depth_does_not_set_fuel_flag()
    {
        IRDefinition opening = new(
            "opening", [], IntegerType.s_instance, NestedLet(64));
        IRChapter ir = new(
            QualifiedName.Simple("T"), [opening], Map<string, CodexType>.s_empty);

        X86_64Emitter emitter = new();
        emitter.EmitAssembly(ir, "ok");

        Assert.False(emitter.EmitFuelExhausted);
    }
}
