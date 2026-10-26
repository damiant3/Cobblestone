#nullable enable
using System.Collections.Immutable;

namespace Uoaix.Assist.Tests;

// RuneChoice: a rune by where it lands. The place in these cases is the box 100..199 by 100..199.
public static class RuneChoiceTests
{
    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("rune: none, none picked", None),
        new TestCase("rune: one landing in the place is picked", LandsIn),
        new TestCase("rune: a label naming the place but landing elsewhere is never picked", LabelLies),
        new TestCase("rune: a landing in the place beats a better label never recalled by", LandingBeatsLabel),
        new TestCase("rune: with no landing in the place, the best label never recalled by", BestUnknown),
        new TestCase("rune: a label of 0 never recalled by is not picked", UnknownNoLabel),
        new TestCase("rune: two landing in the place, the better label, then the lower serial", Ties),
        new TestCase("rune: the place's edges are its own test", Edges));

    public static ImmutableArray<TestCase> Cases => s_cases;

    static bool Box(int x, int y) => x >= 100 && x < 200 && y >= 100 && y < 200;

    static RuneSeen R(uint s, int x, int y, int rank) => new(s, new[] { x, y }, rank);

    static RuneSeen U(uint s, int rank) => new(s, null, rank);

    static uint Pick(params RuneSeen[] runes) => RuneChoice.Pick(ImmutableArray.Create(runes), Box);

    static void None() => Check.Equal(0u, Pick(), "Pick");

    static void LandsIn() => Check.Equal(2u, Pick(R(1, 50, 50, 0), R(2, 150, 150, 0)), "Pick");

    static void LabelLies() => Check.Equal(0u, Pick(R(1, 500, 500, 9)), "Pick");

    static void LandingBeatsLabel() => Check.Equal(2u, Pick(U(1, 9), R(2, 150, 150, 0)), "Pick");

    static void BestUnknown() => Check.Equal(3u, Pick(R(1, 500, 500, 9), U(2, 1), U(3, 5)), "Pick");

    static void UnknownNoLabel() => Check.Equal(0u, Pick(U(1, 0)), "Pick");

    static void Ties()
    {
        Check.Equal(2u, Pick(R(1, 150, 150, 1), R(2, 160, 160, 3)), "better label");
        Check.Equal(4u, Pick(R(5, 150, 150, 2), R(4, 160, 160, 2)), "lower serial");
    }

    static void Edges()
    {
        Check.Equal(1u, Pick(R(1, 100, 199, 0)), "the box's own corner");
        Check.Equal(0u, Pick(R(1, 200, 150, 0)), "one past its edge");
    }
}
