// Two shovels kept in two 8x8 chunks through a whole cave (Damian, 2026-10-22: "digging with one shovel should only
// happen when only 1 shovel is available or there is no available adjacent spawn 8x8 chunks. the circuit should plan
// better its spots to maximally dig two 8x8 chunks at a time through the whole cycle by deciding a full cave depletion
// strategy"). A chunk key is AssistFeatures.ZoneKey's: the chunk's x in the high half, its y in the low.
#nullable enable
using System;
using System.Collections.Immutable;

namespace Uoaix.Assist;

// A standing tile and the chunks with metal its shovels reach from it.
public sealed record MineStand(int X, int Y, ImmutableArray<long> Chunks);

public static class MinePlanner
{
    // A standing tile reaching this many chunks has one chunk in reserve for whichever shovel frees first.
    public const int ReachCounted = 3;

    public static int ChunkX(long chunk) => (int)(chunk >> 32);

    public static int ChunkY(long chunk) => (int)(uint)chunk;

    public static long Chunk(int chunkX, int chunkY) => ((long)chunkX << 32) | (uint)chunkY;

    static int Apart(long a, long b) => Math.Max(Math.Abs(ChunkX(a) - ChunkX(b)), Math.Abs(ChunkY(a) - ChunkY(b)));

    // The depletion order: from the chunk nearest start, each next the unvisited neighbour (of the 8 around it) with
    // the fewest unvisited neighbours of its own, so a dead end is taken before it can be stranded; on a tie the nearer
    // to start, then the lower key. With no neighbour left, the nearest unvisited chunk.
    public static ValueMap<long, int> Order(ImmutableArray<long> chunks, long start)
    {
        ValueMap<long, int> order = ValueMap<long, int>.Empty;
        Set<long> left = Set<long>.From(ImmutableHashSet.CreateRange(chunks));
        if (left.Count == 0)
            return order;
        long at = Nearest(left, start);
        int index = 0;
        while (true)
        {
            order = order.Set(at, index++);
            left = left.Remove(at);
            if (left.Count == 0)
                return order;
            long? next = null;
            int nextFree = int.MaxValue;
            foreach (long n in Neighbours(at))
            {
                if (!left.Contains(n))
                    continue;
                int free = 0;
                foreach (long m in Neighbours(n))
                {
                    if (left.Contains(m))
                        free++;
                }
                if (next is null || free < nextFree || free == nextFree && Before(n, next.Value, start))
                {
                    next = n;
                    nextFree = free;
                }
            }
            at = next ?? Nearest(left, at);
        }
    }

    static ImmutableArray<long> Neighbours(long chunk)
    {
        ImmutableArray<long>.Builder around = ImmutableArray.CreateBuilder<long>(8);
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx != 0 || dy != 0)
                    around.Add(Chunk(ChunkX(chunk) + dx, ChunkY(chunk) + dy));
            }
        }
        return around.MoveToImmutable();
    }

    static bool Before(long a, long b, long from)
    {
        int da = Apart(a, from), db = Apart(b, from);
        return da != db ? da < db : a < b;
    }

    static long Nearest(Set<long> chunks, long from)
    {
        long? best = null;
        foreach (long c in chunks)
        {
            if (best is null || Before(c, best.Value, from))
                best = c;
        }
        return best ?? from;
    }

    // The free shovel's chunk: of the chunks with metal in reach, none a dig in flight is in, a chunk already started
    // (dug this run) before a fresh one, then the earliest in the order; a chunk the order lacks comes last, then the
    // lower key. Null when none is left for it.
    public static long? Pick(ImmutableArray<long> inReach, Set<long> inFlight, Set<long> started,
        ValueMap<long, int> order)
    {
        long? best = null;
        foreach (long c in inReach)
        {
            if (inFlight.Contains(c))
                continue;
            if (best is null || Earlier(c, best.Value, started, order))
                best = c;
        }
        return best;
    }

    static bool Earlier(long a, long b, Set<long> started, ValueMap<long, int> order)
    {
        bool sa = started.Contains(a), sb = started.Contains(b);
        if (sa != sb)
            return sa;
        int oa = order.Get(a, int.MaxValue), ob = order.Get(b, int.MaxValue);
        return oa != ob ? oa < ob : a < b;
    }

    // The standing tiles worth walking to when the free shovel has no chunk of its own in reach, best first: one
    // reaching the carried chunk (the one still being dug) and another, then one reaching two chunks other than the
    // carry; then the more chunks besides the carry (up to ReachCounted), the earliest of them in the order, the nearer
    // to px, py. A tile reaching less is not offered.
    public static ImmutableArray<MineStand> Rank(ImmutableArray<MineStand> stands, long? carry,
        ValueMap<long, int> order,
        int px, int py)
    {
        ImmutableArray<(MineStand Stand, int Tier, int Others, int First, int Second, int Walk)>.Builder worth =
            ImmutableArray.CreateBuilder<(MineStand, int, int, int, int, int)>();
        foreach (MineStand s in stands)
        {
            bool carried = false;
            int others = 0, first = int.MaxValue, second = int.MaxValue;
            foreach (long c in s.Chunks)
            {
                if (carry is long k && c == k)
                {
                    carried = true;
                    continue;
                }
                others++;
                int o = order.Get(c, int.MaxValue);
                if (o < first)
                {
                    second = first;
                    first = o;
                }
                else if (o < second)
                {
                    second = o;
                }
            }
            int tier = carried && others >= 1 ? 2 : others >= 2 ? 1 : 0;
            if (tier == 0)
                continue;
            worth.Add((s, tier, Math.Min(others, ReachCounted), first, second, Math.Max(Math.Abs(s.X - px),
                Math.Abs(s.Y - py))));
        }
        worth.Sort((a, b) =>
            a.Tier != b.Tier ? b.Tier.CompareTo(a.Tier)
            : a.Others != b.Others ? b.Others.CompareTo(a.Others)
            : a.First != b.First ? a.First.CompareTo(b.First)
            : a.Second != b.Second ? a.Second.CompareTo(b.Second)
            : a.Walk.CompareTo(b.Walk));
        ImmutableArray<MineStand>.Builder ranked = ImmutableArray.CreateBuilder<MineStand>(worth.Count);
        foreach ((MineStand Stand, int Tier, int Others, int First, int Second, int Walk) w in worth)
            ranked.Add(w.Stand);
        return ranked.MoveToImmutable();
    }
}
