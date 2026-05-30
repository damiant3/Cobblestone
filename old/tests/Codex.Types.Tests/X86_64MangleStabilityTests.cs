using System.Collections.Immutable;
using Codex.Core;
using Codex.Emit.X86_64;
using Codex.IR;
using Xunit;

namespace Codex.Types.Tests;

public class X86_64MangleStabilityTests
{
    [Fact]
    public void Trampoline_name_is_stable_when_unrelated_function_is_added()
    {
        // Build two minimal IR chapters that differ only in whether a
        // completely unrelated `filler` function precedes `user`. The
        // trampoline in `user`'s body should keep the same mangled name in
        // both cases — previously its name included the global m_text.Count
        // where it was emitted, so filler's presence would shift the name.

        IRChapter withoutFiller = BuildChapter(includeFiller: false);
        IRChapter withFiller = BuildChapter(includeFiller: true);

        X86_64Emitter emitter1 = new();
        emitter1.EmitAssembly(withoutFiller, "m1");
        X86_64Emitter emitter2 = new();
        emitter2.EmitAssembly(withFiller, "m2");

        Dictionary<string, int>? offs1 = emitter1.GetFunctionOffsets();
        Dictionary<string, int>? offs2 = emitter2.GetFunctionOffsets();
        Assert.NotNull(offs1);
        Assert.NotNull(offs2);

        IEnumerable<string> tramps1 = offs1.Keys.Where(k => k.StartsWith("__tramp_", StringComparison.Ordinal));
        IEnumerable<string> tramps2 = offs2.Keys.Where(k => k.StartsWith("__tramp_", StringComparison.Ordinal));

        // Every trampoline the first compile produced must also appear under
        // the same name in the second — names must not depend on where in
        // the emit stream they land.
        foreach (string t in tramps1)
        {
            Assert.Contains(t, tramps2);
        }
    }

    static IRChapter BuildChapter(bool includeFiller)
    {
        // twoArg g = (a, b) → a + b
        //            represented as curried (a → b → a + b)
        ImmutableArray<IRParameter> gParams = [
            new IRParameter("a", IntegerType.s_instance),
            new IRParameter("b", IntegerType.s_instance),
        ];
        IRExpr gBody = new IRBinary(
            IRBinaryOp.AddInt,
            new IRName("a", IntegerType.s_instance),
            new IRName("b", IntegerType.s_instance),
            IntegerType.s_instance);
        CodexType gType = new FunctionType(
            IntegerType.s_instance,
            new FunctionType(IntegerType.s_instance, IntegerType.s_instance));
        IRDefinition g = new("g", gParams, gType, gBody);

        // user = g 10  (partial application → closure with one capture → trampoline)
        IRExpr userBody = new IRApply(
            new IRName("g", gType),
            new IRIntegerLit(10L),
            new FunctionType(IntegerType.s_instance, IntegerType.s_instance));
        IRDefinition user = new(
            "user", [], new FunctionType(IntegerType.s_instance, IntegerType.s_instance),
            userBody);

        List<IRDefinition> defs = [];
        if (includeFiller)
        {
            // An unrelated function whose body adds 1+2.
            IRDefinition filler = new(
                "filler", [], IntegerType.s_instance,
                new IRBinary(
                    IRBinaryOp.AddInt,
                    new IRIntegerLit(1L),
                    new IRIntegerLit(2L),
                    IntegerType.s_instance));
            defs.Add(filler);
        }
        defs.Add(g);
        defs.Add(user);
        // opening so the emitter has an entry point
        defs.Add(new IRDefinition(
            "opening", [], IntegerType.s_instance, new IRIntegerLit(0L)));

        return new IRChapter(
            QualifiedName.Simple("T"), [..defs], Map<string, CodexType>.s_empty);
    }
}
