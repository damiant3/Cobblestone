using Codex.Core;
using Codex.Emit.Codex;
using Codex.IR;
using Xunit;

namespace Codex.Types.Tests;

// Roundtrip test (section G): compile the same source twice through the
// full parse → desugar → resolve → typecheck → lower pipeline, then
// serialize both IR chapters via CodexEmitter and byte-compare. A
// difference means Lowering (or any upstream phase) depends on
// allocation order, hash iteration order, or a global counter that
// doesn't reset between runs.
public class LoweringDeterminismTests
{
    static string SerializeIR(IRChapter ir) => new CodexEmitter().Emit(ir);

    static void AssertDeterministic(string source, string label)
    {
        IRChapter? ir1 = Helpers.CompileToIR(source, label);
        IRChapter? ir2 = Helpers.CompileToIR(source, label);
        Assert.NotNull(ir1);
        Assert.NotNull(ir2);

        string s1 = SerializeIR(ir1);
        string s2 = SerializeIR(ir2);
        Assert.Equal(s1, s2);
    }

    [Fact]
    public void Simple_arithmetic_lowers_deterministically()
    {
        AssertDeterministic("opening : Integer\nopening = 1 + 2 * 3", "arith");
    }

    [Fact]
    public void Records_and_variants_lower_deterministically()
    {
        string source = """
            Shape =
             | Circle (Integer)
             | Square (Integer)

            Point = record { x : Integer, y : Integer }

            area (s) = when s
             is Circle (r) -> r * r * 3
             is Square (w) -> w * w

            origin = Point { x = 0, y = 0 }

            opening : Integer
            opening = area (Circle 5)
            """;
        AssertDeterministic(source, "shapes");
    }

    [Fact]
    public void Polymorphic_let_lowers_deterministically()
    {
        string source = """
            opening : Integer
            opening = let a = 1 in let b = 2 in let c = 3 in a + b + c
            """;
        AssertDeterministic(source, "polylet");
    }

    [Fact]
    public void Match_with_many_branches_lowers_deterministically()
    {
        string source = """
            Color =
             | Red
             | Green
             | Blue
             | Yellow
             | Purple

            name (c) = when c
             is Red -> "red"
             is Green -> "green"
             is Blue -> "blue"
             is Yellow -> "yellow"
             is Purple -> "purple"

            opening : Text
            opening = name Blue
            """;
        AssertDeterministic(source, "colors");
    }
}
