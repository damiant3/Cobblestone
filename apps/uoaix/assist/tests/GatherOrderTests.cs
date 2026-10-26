#nullable enable
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Uoaix.Assist.Tests;

// GatherOrder: the oval of two foci and a range, and the pick by walk plus Band a band.
public static class GatherOrderTests
{
    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("order: one focus is a circle of the range, its edge in and one past it out, a tile with no spot "
            + "skipped", Circle),
        new TestCase("order: two foci take the line between them at band 0 and bands grow outward by Band", Bands),
        new TestCase("order: the spots come in the map's column order, each with its art and height", Columns),
        new TestCase("order: the least walk plus Band a band, the lower band on a tie, then the first; skip leaves "
        + "out; none is null", Pick));

    public static ImmutableArray<TestCase> Cases => s_cases;

    static int[]? Everywhere(int x, int y) => new[] { 0x0CCA, 7 };

    static bool Has(ImmutableArray<GatherSpot> o, int x, int y)
    {
        foreach (GatherSpot s in o)
        {
            if (s.X == x && s.Y == y)
                return true;
        }
        return false;
    }

    static void Circle()
    {
        ImmutableArray<GatherSpot> o = GatherOrder.Oval(100, 100, 100, 100, 5, Everywhere);
        Check.True(Has(o, 105, 100) && Has(o, 100, 95) && Has(o, 103, 104), "on the edge: in");
        Check.False(Has(o, 106, 100) || Has(o, 104, 104), "past it: out");
        ImmutableArray<GatherSpot> some = GatherOrder.Oval(100, 100, 100, 100, 5,
            (x, y) => x == 101 && y == 100 ? new[] { 1, 2 } : null);
        Check.Equal(1, some.Length, "only the tile with a spot");
    }

    static void Bands()
    {
        ImmutableArray<GatherSpot> o = GatherOrder.Oval(100, 100, 120, 100, 20, Everywhere);
        int Band(int x, int y)
        {
            foreach (GatherSpot s in o)
            {
                if (s.X == x && s.Y == y)
                    return s.Band;
            }
            return -1;
        }
        Check.Equal(0, Band(110, 100), "the line's middle");
        Check.Equal(0, Band(100, 100), "a focus");
        Check.True(Band(110, 109) == 0 && Band(110, 110) == 1, "level 6.9 is band 0, 8.3 band 1");
        Check.Equal(2, Band(110, 118), "level 21.2 is band 2");
    }

    static void Columns()
    {
        ImmutableArray<GatherSpot> o = GatherOrder.Oval(10, 10, 10, 10, 1, (x, y) => new[] { x * 100 + y, x - y });
        List<string> at = new();
        foreach (GatherSpot s in o)
            at.Add(s.X + "," + s.Y);
        Check.Equal("9,10 10,9 10,10 10,11 11,10", string.Join(" ", at), "by x, then y");
        Check.Equal(new GatherSpot(10, 9, 1, 1009, 0), o[1], "the art and height the tile gave");
    }

    static void Pick()
    {
        // From 0,0: A walks 10 at band 0 (10), B 3 at band 1 (11), C 11 at band 0 (11), D 2 at band 1 (10), E 10 at
        // band 0 (10).
        ImmutableArray<GatherSpot> s = ImmutableArray.Create(
            new GatherSpot(10, 0, 0, 1, 0),
            new GatherSpot(3, 0, 0, 2, 1),
            new GatherSpot(0, 11, 0, 3, 0),
            new GatherSpot(0, 2, 0, 4, 1),
            new GatherSpot(0, 10, 0, 5, 0));
        Check.Equal(1, GatherOrder.Pick(s, 0, 0, t => false)!.Graphic,
            "three at 10: band 0 before band 1, then the first");
        Check.Equal(5, GatherOrder.Pick(s, 0, 0, t => t.Graphic == 1)!.Graphic, "A left out: E, band 0 at 10");
        Check.Equal(4, GatherOrder.Pick(s, 0, 0, t => t.Graphic == 1 || t.Graphic == 5)!.Graphic,
            "A and E left out: D at 10 beats 11");
        Check.Equal(3, GatherOrder.Pick(s, 0, 0, t => t.Graphic != 2 && t.Graphic != 3)!.Graphic,
            "B and C at 11: C, band 0");
        Check.True(GatherOrder.Pick(s, 0, 0, t => true) is null, "all left out: none");
        Check.True(GatherOrder.Pick(ImmutableArray<GatherSpot>.Empty, 0, 0, t => false) is null, "no spots: none");
    }
}
