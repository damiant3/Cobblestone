// The bunnyhop collection of log piles (CurrentPlan blu row, Damian 2026-10-22): "put the item down in the direction
// you want to go, move to a place farther down the path to the destination where you can still reach the item, pick it,
// put it down in new location further down the path, then bunny hop over it"; and "collect big piles and consolidate
// them in a network that calculates the minimum movements for the whole distribution of drops you left ... like a
// network graph". A hop costs the same whatever the pile weighs and piles dropped on each other travel as one, so the
// network is a Steiner tree rooted at the destination; its minimum spanning tree is within 2x of the optimum.
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Uoaix.Assist;

// One hop along a path of tiles (index 0 the pile's tile, the last the destination): the player stands at Stand, lifts
// the pile from Lift and puts it down at Drop, each within the reach of Stand.
public sealed record PileHop(int Stand, int Lift, int Drop);

public static class Bunnyhop
{
    // The hops that carry a pile along a path of length steps: each stands reach past the pile and drops it reach past
    // the stand, so the pile moves 2 * reach tiles a hop and the last hop drops it on the destination. No hop stands on
    // the tile it drops on: uo1998 sends a drop on the player's own tile back into the pack (logs 46EBDCDF at
    // 4717,1127, 2026-10-22 01:35:30), so the last stands short of the destination.
    public static ImmutableArray<PileHop> Plan(int length, int reach)
    {
        if (reach < 1)
            throw new ArgumentOutOfRangeException(nameof(reach), "a hop reaches at least 1 tile");
        ImmutableArray<PileHop>.Builder hops = ImmutableArray.CreateBuilder<PileHop>();
        int pile = 0;
        while (pile < length)
        {
            int stand = Math.Min(pile + reach, Math.Max(pile, length - 1));
            int drop = Math.Min(stand + reach, length);
            hops.Add(new PileHop(stand, pile, drop));
            pile = drop;
        }
        return hops.ToImmutable();
    }
}

// The minimum spanning tree over the destination (node 0) and the piles (1 on), by the distances given, and the order
// the piles move in: every pile after all the piles that merge into it, each onto its parent.
public sealed record PileTree(ImmutableArray<int> Parent, ImmutableArray<int> Order)
{
    public const int Unreached = int.MaxValue;

    // dist[i, j]: the cost of carrying a pile from node i to node j, Unreached where no way exists. A node no way
    // reaches has parent -1 and is in no order.
    public static PileTree Build(int[,] dist)
    {
        int n = dist.GetLength(0);
        int[] parent = new int[n];
        int[] best = new int[n];
        bool[] inTree = new bool[n];
        for (int i = 0; i < n; i++)
        {
            parent[i] = -1;
            best[i] = Unreached;
        }
        if (n == 0)
            return new PileTree(ImmutableArray<int>.Empty, ImmutableArray<int>.Empty);
        best[0] = 0;
        for (int round = 0; round < n; round++)
        {
            int next = -1;
            for (int i = 0; i < n; i++)
            {
                if (!inTree[i] && best[i] != Unreached && (next < 0 || best[i] < best[next]))
                    next = i;
            }
            if (next < 0)
                break;
            inTree[next] = true;
            for (int j = 0; j < n; j++)
            {
                // A pile moves toward the destination, so the edge from j into the tree is j's cost to next.
                if (!inTree[j] && dist[j, next] < best[j])
                {
                    best[j] = dist[j, next];
                    parent[j] = next;
                }
            }
        }
        List<int>[] children = new List<int>[n];
        for (int i = 0; i < n; i++)
            children[i] = new();
        for (int i = 1; i < n; i++)
        {
            if (inTree[i])
                children[parent[i]].Add(i);
        }
        // Post-order from the root, children first; the root itself does not move.
        ImmutableArray<int>.Builder order = ImmutableArray.CreateBuilder<int>();
        Stack<(int Node, int Next)> walk = new();
        walk.Push((0, 0));
        while (walk.Count > 0)
        {
            (int node, int next) = walk.Pop();
            if (next < children[node].Count)
            {
                walk.Push((node, next + 1));
                walk.Push((children[node][next], 0));
            }
            else if (node != 0)
            {
                order.Add(node);
            }
        }
        int[] reached = new int[n];
        for (int i = 0; i < n; i++)
            reached[i] = inTree[i] ? parent[i] : -1;
        return new PileTree(ImmutableArray.Create(reached), order.ToImmutable());
    }

    // The order the player moves the piles in, starting where the player stands (Damian, 2026-10-22: "it seems almost
    // inconceivable to me that the optimal collection strategy would not begin with the logs at your feet"): each pile
    // still after every pile that merges into it, and of the piles ready the one nearest the player, who stands at a
    // pile's parent once it is moved there. fromStart[i]: the walk from the player to node i; walk[i, j]: from node i
    // to node j. The tree's hops are the same in any such order; only the walks between them change.
    public ImmutableArray<int> NearestFirst(int[] fromStart, int[,] walk)
    {
        int n = Parent.Length;
        int[] waiting = new int[n];
        foreach (int v in Order)
            waiting[Parent[v]]++;
        List<int> ready = new();
        foreach (int v in Order)
        {
            if (waiting[v] == 0)
                ready.Add(v);
        }
        ImmutableArray<int>.Builder order = ImmutableArray.CreateBuilder<int>(Order.Length);
        int at = -1;
        while (ready.Count > 0)
        {
            int pick = 0;
            for (int i = 1; i < ready.Count; i++)
            {
                int d = at < 0 ? fromStart[ready[i]] : walk[at, ready[i]];
                int best = at < 0 ? fromStart[ready[pick]] : walk[at, ready[pick]];
                if (d < best || (d == best && ready[i] < ready[pick]))
                    pick = i;
            }
            int v = ready[pick];
            ready.RemoveAt(pick);
            order.Add(v);
            int p = Parent[v];
            waiting[p]--;
            if (p != 0 && waiting[p] == 0)
                ready.Add(p);
            at = p;
        }
        return order.MoveToImmutable();
    }
}
