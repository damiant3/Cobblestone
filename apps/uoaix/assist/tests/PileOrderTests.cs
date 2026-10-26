#nullable enable
using System;
using System.Collections.Immutable;

namespace Uoaix.Assist.Tests;

// The collection's order from where the player stands (PileTree.NearestFirst): the ready pile nearest the player
// next, every pile after the piles that merge into it.
public static class PileOrderTests
{
    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("order: the pile at the player's feet moves first, not the first one dropped", FeetFirst),
        new TestCase("order: a pile nearest the player still waits for the piles that merge into it", ChildrenFirst),
        new TestCase("order: after a move the player stands at its parent and takes the ready pile nearest there",
        FromParent),
        new TestCase("order: a pile no way reaches is in no order", Unreached),
        new TestCase("order: two ready piles as near take the first recorded", Tie),
        new TestCase("order: no piles is no order", Empty),
        new TestCase("order: a tie goes to the lower index though it was readied after", TieLowerIndexLater),
        new TestCase("order: a parent with two children waits for the second", TwoChildren));

    public static ImmutableArray<TestCase> Cases => s_cases;

    // Tiles apart (the larger of the two axes), between every pair of points and from the player.
    static int[,] Apart(int[][] at)
    {
        int n = at.Length;
        int[,] d = new int[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
                d[i, j] = Math.Max(Math.Abs(at[i][0] - at[j][0]), Math.Abs(at[i][1] - at[j][1]));
        }
        return d;
    }

    static int[] From(int[][] at, int x, int y)
    {
        int[] d = new int[at.Length];
        for (int i = 0; i < at.Length; i++)
            d[i] = Math.Max(Math.Abs(at[i][0] - x), Math.Abs(at[i][1] - y));
        return d;
    }

    // The place at 0,0; the first pile dropped 10 east, the last 10 west where the player stands: the tree sends each
    // to the place, and the walk east and back again is the one the record's order took.
    static void FeetFirst()
    {
        int[][] at = { new[] { 0, 0 }, new[] { 10, 0 }, new[] { -10, 0 } };
        PileTree t = PileTree.Build(Apart(at));
        Check.Equal(0, t.Parent[1], "east to the place");
        Check.Equal(0, t.Parent[2], "west to the place");
        Check.Equal(1, t.Order[0], "the record's order: the first dropped first");
        ImmutableArray<int> o = t.NearestFirst(From(at, -10, 0), Apart(at));
        Check.Equal(2, o.Length, "both move");
        Check.Equal(2, o[0], "the pile at the feet first");
        Check.Equal(1, o[1], "then the far one");
    }

    // A line from the place: 10 and 20; the player beside 10. 20 goes onto 10, so 10 moves only after it.
    static void ChildrenFirst()
    {
        int[][] at = { new[] { 0, 0 }, new[] { 10, 0 }, new[] { 20, 0 } };
        PileTree t = PileTree.Build(Apart(at));
        ImmutableArray<int> o = t.NearestFirst(From(at, 10, 0), Apart(at));
        Check.Equal(2, o[0], "the far pile first: it merges into the near one");
        Check.Equal(1, o[1], "then the near one, carrying both");
    }

    // The place at 0,0; piles at 10,0, -10,0 and 0,-40, each to the place. After the west pile, the player stands at
    // the place: 10 from the east pile, 40 from the north one.
    static void FromParent()
    {
        int[][] at = { new[] { 0, 0 }, new[] { 10, 0 }, new[] { -10, 0 }, new[] { 0, -40 } };
        PileTree t = PileTree.Build(Apart(at));
        Check.Equal(0, t.Parent[3], "north to the place");
        ImmutableArray<int> o = t.NearestFirst(From(at, -10, 0), Apart(at));
        Check.Equal(2, o[0], "west, at the feet");
        Check.Equal(1, o[1], "then east, 10 from the place");
        Check.Equal(3, o[2], "north last, 40 from the place");
    }

    static void Unreached()
    {
        int[][] at = { new[] { 0, 0 }, new[] { 5, 0 }, new[] { 9, 0 } };
        int[,] d = Apart(at);
        d[2, 0] = d[0, 2] = d[2, 1] = d[1, 2] = PileTree.Unreached;
        PileTree t = PileTree.Build(d);
        ImmutableArray<int> o = t.NearestFirst(From(at, 9, 0), Apart(at));
        Check.Equal(1, o.Length, "only the reached pile");
        Check.Equal(1, o[0], "the reached pile");
    }

    static void Tie()
    {
        int[][] at = { new[] { 0, 0 }, new[] { 10, 0 }, new[] { -10, 0 } };
        PileTree t = PileTree.Build(Apart(at));
        ImmutableArray<int> o = t.NearestFirst(From(at, 0, 0), Apart(at));
        Check.Equal(1, o[0], "both 10 away: the first recorded");
    }

    // The place at 0,0; 1 at 10,0 with 3 at 20,0 onto it; 2 at -20,0 to the place. The ready piles are 3 then 2, both
    // 20 from the player at the place: 2, the lower index.
    static void TieLowerIndexLater()
    {
        int[][] at = { new[] { 0, 0 }, new[] { 10, 0 }, new[] { -20, 0 }, new[] { 20, 0 } };
        PileTree t = PileTree.Build(Apart(at));
        Check.Equal(1, t.Parent[3], "20 onto 10");
        Check.Equal(0, t.Parent[2], "-20 to the place");
        ImmutableArray<int> o = t.NearestFirst(From(at, 0, 0), Apart(at));
        Check.Equal(2, o[0], "the lower index of the two ready");
    }

    // The place at 0,0; 1 at 10,0; 2 at 20,0 and 3 at 10,5 both onto 1. After 2, 1 still waits for 3.
    static void TwoChildren()
    {
        int[][] at = { new[] { 0, 0 }, new[] { 10, 0 }, new[] { 20, 0 }, new[] { 10, 5 } };
        PileTree t = PileTree.Build(Apart(at));
        Check.Equal(1, t.Parent[2], "20 onto 10");
        Check.Equal(1, t.Parent[3], "10,5 onto 10");
        ImmutableArray<int> o = t.NearestFirst(From(at, 20, 0), Apart(at));
        Check.Equal(2, o[0], "the feet pile");
        Check.Equal(3, o[1], "the other child before the parent");
        Check.Equal(1, o[2], "the parent last");
    }

    static void Empty()
    {
        Check.Equal(0, PileTree.Build(new int[1, 1]).NearestFirst(new int[1], new int[1, 1]).Length, "the place alone");
        Check.Equal(0, PileTree.Build(new int[0, 0]).NearestFirst(new int[0], new int[0, 0]).Length, "no nodes");
    }
}
