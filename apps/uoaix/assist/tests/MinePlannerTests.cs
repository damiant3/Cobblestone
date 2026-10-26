#nullable enable
using System.Collections.Immutable;
using System.Collections.Generic;

namespace Uoaix.Assist.Tests;

// The two-shovel cave plan (MinePlanner) on cave grids drawn by chunk.
public static class MinePlannerTests
{
    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("order: no chunks, no order", OrderEmpty),
        new TestCase("order: a row walked from one end runs along it", OrderRow),
        new TestCase("order: a dead end beside the start is taken before the long arm", OrderDeadEndFirst),
        new TestCase("order: a start outside the cave begins at the nearest chunk", OrderStartOutside),
        new TestCase("order: with no neighbour left, the nearest unvisited chunk next", OrderJump),
        new TestCase("order: every chunk once, in a 3x3 block", OrderBlock),
        new TestCase("order: neighbours alike in free neighbours, the nearer to the start first, then the lower key",
        OrderTie),
        new TestCase("order: of neighbours alike, the one nearer the start wins though found second", OrderTieNearer),
        new TestCase("pick: nothing in reach picks nothing", PickNone),
        new TestCase("pick: the chunk in flight is not picked", PickNotInFlight),
        new TestCase("pick: a started chunk before a fresh one, whatever the order", PickStartedFirst),
        new TestCase("pick: among fresh chunks the earliest in the order", PickEarliest),
        new TestCase("pick: a chunk the order lacks after every ordered one", PickUnordered),
        new TestCase("pick: two chunks the order lacks, the lower key", PickUnorderedTie),
        new TestCase("rank: carrying the chunk being dug beats reaching three others", RankCarryFirst),
        new TestCase("rank: a stand with the carry alone is not offered", RankCarryAlone),
        new TestCase("rank: a stand with one chunk besides no carry is not offered", RankOneFresh),
        new TestCase("rank: with no carry, a stand reaching two chunks is offered", RankTwoNoCarry),
        new TestCase("rank: more chunks first, counted up to 3", RankMoreFirst),
        new TestCase("rank: on equal reach, the earliest chunks in the order first", RankOrderFirst),
        new TestCase("rank: on equal reach and order, the nearer stand first", RankNearerFirst),
        new TestCase("rank: no stands, none offered", RankEmpty),
        new TestCase("rank: a stand reaching no chunk is not offered", RankNoChunks),
        new TestCase("rank: on an equal earliest chunk, the earlier second chunk first", RankSecondOrder));

    public static ImmutableArray<TestCase> Cases => s_cases;

    static long C(int x, int y) => MinePlanner.Chunk(x, y);

    static ImmutableArray<long> Cs(params (int X, int Y)[] at)
    {
        ImmutableArray<long>.Builder b = ImmutableArray.CreateBuilder<long>();
        foreach ((int x, int y) in at)
            b.Add(C(x, y));
        return b.ToImmutable();
    }

    static string Walk(ValueMap<long, int> order)
    {
        string[] at = new string[order.Count];
        foreach (KeyValuePair<long, int> kv in order)
            at[kv.Value] = MinePlanner.ChunkX(kv.Key) + "," + MinePlanner.ChunkY(kv.Key);
        return string.Join(" ", at);
    }

    static void OrderEmpty() => Check.Equal(0, MinePlanner.Order(ImmutableArray<long>.Empty, C(0, 0)).Count, "count");

    static void OrderRow() =>
        Check.Equal("10,10 11,10 12,10 13,10",
        Walk(MinePlanner.Order(Cs((12, 10), (10, 10), (13, 10), (11, 10)), C(10, 10))), "order");

    // From 10,10: the arm 11,10 has two unvisited neighbours (12,10 and 10,11), the spur 10,11 one (11,10).
    static void OrderDeadEndFirst() =>
        Check.Equal("10,10 10,11 11,10 12,10 13,10",
        Walk(MinePlanner.Order(Cs((10, 10), (11, 10), (12, 10), (13, 10), (10, 11)), C(10, 10))), "order");

    static void OrderStartOutside() =>
        Check.Equal("13,10 12,10 11,10", Walk(MinePlanner.Order(Cs((11, 10), (12, 10), (13, 10)), C(20, 10))), "order");

    static void OrderJump() =>
        Check.Equal("10,10 11,10 15,10 16,10",
        Walk(MinePlanner.Order(Cs((10, 10), (11, 10), (15, 10), (16, 10)), C(10, 10))), "order");

    static void OrderBlock()
    {
        ImmutableArray<long>.Builder b = ImmutableArray.CreateBuilder<long>();
        for (int x = 0; x < 3; x++)
        {
            for (int y = 0; y < 3; y++)
                b.Add(C(x, y));
        }
        ValueMap<long, int> order = MinePlanner.Order(b.ToImmutable(), C(0, 0));
        Check.Equal(9, order.Count, "count");
        Check.Equal(0, order.Get(C(0, 0), -1), "start");
        bool[] seen = new bool[9];
        foreach (KeyValuePair<long, int> kv in order)
            seen[kv.Value] = true;
        foreach (bool s in seen)
            Check.True(s, "each index once");
    }

    // From 10,10 the neighbours 11,10 and 9,10 each have 0 others free; both 1 from the start, so the lower key: 9,10.
    static void OrderTie() =>
        Check.Equal("10,10 9,10 11,10", Walk(MinePlanner.Order(Cs((10, 10), (11, 10), (9, 10)), C(10, 10))), "order");

    // From 16,9 the walk starts at 11,9 and reaches 10,10, whose free neighbours 9,11 (found first, 7 from the start)
    // and 11,11 (5 from it) each have none free: 11,11 first.
    static void OrderTieNearer() =>
        Check.Equal("11,9 10,10 11,11 9,11",
        Walk(MinePlanner.Order(Cs((11, 9), (10, 10), (9, 11), (11, 11)), C(16, 9))), "order");

    static readonly ValueMap<long, int> s_order = ValueMap<long, int>.Of((C(1, 1), 0), (C(2, 1), 1), (C(3, 1), 2),
        (C(4, 1), 3));

    static void PickNone() => Check.Null(MinePlanner.Pick(ImmutableArray<long>.Empty, Set<long>.Empty,
        Set<long>.Empty, s_order), "Pick");

    static void PickNotInFlight() => Check.Null(MinePlanner.Pick(Cs((1, 1)), Set<long>.Of(C(1, 1)), Set<long>.Empty,
        s_order), "Pick");

    static void PickStartedFirst() =>
        Check.Equal(C(4, 1), MinePlanner.Pick(Cs((1, 1), (4, 1)), Set<long>.Empty, Set<long>.Of(C(4, 1)), s_order),
        "Pick");

    static void PickEarliest() =>
        Check.Equal(C(2, 1),
        MinePlanner.Pick(Cs((3, 1), (2, 1), (4, 1)), Set<long>.Of(C(1, 1)), Set<long>.Empty, s_order), "Pick");

    static void PickUnordered() =>
        Check.Equal(C(4, 1), MinePlanner.Pick(Cs((9, 9), (4, 1)), Set<long>.Empty, Set<long>.Empty, s_order),
        "Pick");

    static void PickUnorderedTie() =>
        Check.Equal(C(8, 9), MinePlanner.Pick(Cs((9, 9), (8, 9)), Set<long>.Empty, Set<long>.Empty, s_order),
        "Pick");

    static MineStand S(int x, int y, params (int X, int Y)[] chunks) => new(x, y, Cs(chunks));

    static void RankNoChunks() =>
        Check.Equal("", Xs(MinePlanner.Rank(ImmutableArray.Create(S(1, 1)), null, s_order, 0, 0)), "Rank");

    // Both hold 1,1 first; the second chunks are 2,1 (order 1) and 4,1 (order 3).
    static void RankSecondOrder() =>
        Check.Equal("2,2 1,1",
        Xs(MinePlanner.Rank(ImmutableArray.Create(S(1, 1, (1, 1), (4, 1)), S(2, 2, (1, 1), (2, 1))), null, s_order, 0,
        0)), "Rank");

    static string Xs(ImmutableArray<MineStand> ranked)
    {
        string[] at = new string[ranked.Length];
        for (int i = 0; i < ranked.Length; i++)
            at[i] = ranked[i].X + "," + ranked[i].Y;
        return string.Join(" ", at);
    }

    static void RankCarryFirst() =>
        Check.Equal("1,1 2,2",
        Xs(MinePlanner.Rank(ImmutableArray.Create(S(2, 2, (2, 1), (3, 1), (4, 1)), S(1, 1, (1, 1), (4, 1))), C(1, 1),
        s_order, 0, 0)), "Rank");

    static void RankCarryAlone() =>
        Check.Equal("", Xs(MinePlanner.Rank(ImmutableArray.Create(S(1, 1, (1, 1))), C(1, 1), s_order, 0, 0)), "Rank");

    static void RankOneFresh() =>
        Check.Equal("", Xs(MinePlanner.Rank(ImmutableArray.Create(S(1, 1, (2, 1))), C(1, 1), s_order, 0, 0)), "Rank");

    static void RankTwoNoCarry() =>
        Check.Equal("1,1",
        Xs(MinePlanner.Rank(ImmutableArray.Create(S(1, 1, (2, 1), (3, 1)), S(2, 2, (4, 1))), null, s_order, 0, 0)),
        "Rank");

    // 3 against 2: the 3 first; 4 against 3 count alike (ReachCounted), so the order decides: 5,5 holds 1,1.
    static void RankMoreFirst()
    {
        Check.Equal("3,3 2,2",
            Xs(MinePlanner.Rank(ImmutableArray.Create(S(2, 2, (2, 1), (3, 1)), S(3, 3, (2, 1), (3, 1), (4, 1))), null,
            s_order, 0, 0)), "3 over 2");
        Check.Equal("5,5 4,4",
            Xs(MinePlanner.Rank(ImmutableArray.Create(S(4, 4, (2, 1), (3, 1), (4, 1), (9, 9)),
            S(5, 5, (1, 1), (3, 1), (4, 1))), null, s_order, 0, 0)), "4 counts as 3");
    }

    static void RankOrderFirst() =>
        Check.Equal("2,2 1,1",
        Xs(MinePlanner.Rank(ImmutableArray.Create(S(1, 1, (3, 1), (4, 1)), S(2, 2, (1, 1), (4, 1))), null, s_order, 0,
        0)), "Rank");

    static void RankNearerFirst() =>
        Check.Equal("3,0 9,0",
        Xs(MinePlanner.Rank(ImmutableArray.Create(S(9, 0, (1, 1), (2, 1)), S(3, 0, (1, 1), (2, 1))), null, s_order, 0,
        0)), "Rank");

    static void RankEmpty() => Check.Equal(0,
        MinePlanner.Rank(ImmutableArray<MineStand>.Empty, null, s_order, 0, 0).Length, "Rank");
}
