#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading;

namespace Uoaix.Assist.Tests;

// The bunnyhop collection (Collection.cs, AssistFeatures.LumberCollect; Damian, 2026-10-22): the hops along a path,
// the tree the piles merge on, and the piles at feet carried to Logs to against the fake shard, a route and a walk of
// the test's own (the suites hold no client map).
public static class CollectionTests
{
    const uint Pack = 0x42BE4F7C;
    const uint Axe = 0x44AB0001;
    const uint Logs = 0x44AB0002;
    const uint Logs2 = 0x44AB0003;
    const uint Bag = 0x44AB0004;
    const uint PileA = 0x44AB0010;
    const uint PileB = 0x44AB0011;
    const int AxeArt = 0x0F43;
    const int LogArt = 0x1BDD;
    const int TreeArt = 0x0CCA;

    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("hops: a path of 0 steps needs no hop", HopsNone),
        new TestCase("hops: every lift and drop within reach of the stand, the pile always forward, the last on the end",
        HopsBounds),
        new TestCase("hops: a path of 9 steps at reach 2 is three hops, 4 tiles a hop", HopsNine),
        new TestCase("hops: a reach under 1 is refused", HopsReachZero),
        new TestCase("tree: piles in a line merge outward-in, the far one onto the near one", TreeLine),
        new TestCase("tree: a pile nearer another pile than the place goes onto that pile", TreeMerge),
        new TestCase("tree: a pile no way reaches has no parent and does not move", TreeUnreached),
        new TestCase("tree: the place alone moves nothing", TreeAlone),
        new TestCase("collect: two piles in a line, the far merged onto the near and both carried to the place",
        TwoPiles),
        new TestCase("collect: the pile at the player's feet first, not the first one dropped across the place",
        FeetPileFirst),
        new TestCase("collect: the last drop onto a bag targeted with Logs to", IntoBag),
        new TestCase("collect: a pile of ours the record names and no pile lies at is passed, the record corrected, "
        + "the rest collected", PileGone),
        new TestCase("carry: a leaf out of view is judged by the record, walked to and carried; never there, it is "
        + "passed", CarryUnseen),
        new TestCase("collect: a route that ends past a teleporter lifts nothing", Teleporter),
        new TestCase("collect: a pile with no route moves nothing and is named", NoRoute),
        new TestCase("collect: a pack animal or no Logs to is no place for the collection", NoPlace),
        new TestCase("collect: a drop the server ignores ends it, the record left where the pile was", DropIgnored),
        new TestCase("lumber: at feet, the total reaching Collect at collects, then chopping goes on", CircuitCollects),
        new TestCase("lumber: at feet, no tree left below Collect at collects the piles and the pack's logs, then ends",
        NoTreeCollects),
        new TestCase("lumber: at feet, no tree left and no piles ends with no collection", NoTreeNoPiles),
        new TestCase("lumber: at feet, no tree left and the pack's logs not taken ends naming both, no collection",
        NoTreeStoreFails),
        new TestCase("lumber: at feet, no tree left and the collection's walk refused ends naming both",
        NoTreeCollectFails),
        new TestCase("lumber: at feet with no place for the collection, the circuit refuses before the first chop",
        CircuitNoPlace),
        new TestCase("lumber: new chop forgets the piles at feet, then starts", NewChopClears),
        new TestCase("lumber: new chop while the circuit runs is refused and keeps the piles", NewChopRunning),
        new TestCase("lumber: new chop takes the log piles in view as old drops, never found as wild, kept across "
        + "restarts", NewChopOldDrops),
        new TestCase("lumber: resume with the piles at feet past Collect at collects them before the first chop",
        ResumeCollectsDue),
        new TestCase("lumber: the old drops saved before a restart are read back and never found as wild",
        OldDropsRead),
        new TestCase("map: the sentinel, the collection point and Chop from are marked; unset, a pack animal or "
        + "unreadable, none", SpotsMarked),
        new TestCase("map: every log pile seen is marked wild beside the record's, kept out of view and across "
        + "restarts, gone at the delete of its tile's last stack in view, else 10 s after its tile is seen empty",
        SeenMarked),
        new TestCase("map: a chop's logs laid on the ground a moment (0x1A of 1, 0x1D, 0x1A of 10, 0x1D) leave no "
        + "wild mark", ChopFlicker),
        new TestCase("map: a delete takes only its own mark: none before any ground item, a non-resource's, a pack "
        + "item's, and a log's beside ore", DeleteEdges),
        new TestCase("map: ore, feathers, hides and reagents seen are marked, none in a house; under 255 stones they "
        + "decay after 3 hours", ResourcesDecay),
        new TestCase("map: a reagent lifted by hand or by the harvest leaves its mark at once; a lift the server "
        + "refuses leaves the mark", LiftedUnmarked),
        new TestCase("map: a pile the collection hops leaves no wild mark where it lay, and the pile on the place is "
        + "ours", HopLeavesNoMark),
        new TestCase("map: logs put at feet read ours while the store still runs, and a drop put back leaves no wild "
        + "mark", FeetStoreOurs),
        new TestCase("lumber: a drop on Logs to put back ends the store, records nothing and leaves the tile's "
        + "sighting to the view", LogsToPutBack),
        new TestCase("lumber: at feet a refused lift ends the store, records nothing and the sighting beside stays wild",
        FeetLiftRefused),
        new TestCase("decor: a multi seen is catalogued and saved, read back on a new start (the map's multi data is "
        + "not in the suites); a lifted item is not; seen gone, it leaves", DecorKept),
        new TestCase("decor: the game view's fill is the box's fixed items, multis and items outside it left out; the "
        + "sequence moves on a change only", DecorInBox),
        new TestCase("lumber: new chop with no piles starts and names none forgotten", NewChopNoPiles),
        new TestCase("collect: no piles in the record is nothing to do", NothingToCollect),
        new TestCase("collect: with no client map the planner finds no way and nothing moves", NoMapNoWay),
        new TestCase("collect: wild logs no route reaches are forgotten and ours still collected", WildNoWayForgotten),
        new TestCase("collect: past 4 places a hop with no way passes its pile, wild forgotten and ours kept, and the "
        + "rest are collected", HopNoWayPassed),
        new TestCase("carry: a pile that fits in the pack is lifted into it, walked to the place and put down there, "
        + "no hop", CarryFits),
        new TestCase("carry: a pile past the pack's limit is hopped", CarryTooHeavy),
        new TestCase("carry: the pack's logs are put down at feet first and collected with the piles", CarryPackFirst),
        new TestCase("carry: a walk refused before or after the lift, a lift not taken, or a stop ends it with the "
        + "record true; into a bag", CarryEdges),
        new TestCase("collect: past 24 places the tree is weighed by tiles, and every pile still reaches the place",
        ManyByTiles),
        new TestCase("collect: a stop between hops ends it, the record where the pile lies", StopBetweenHops),
        new TestCase("collect: a walk to a stand that fails ends it and names the stand", WalkFails),
        new TestCase("collect: a pile already on the place leaves the record with no lift", AlreadyThere),
        new TestCase("collect: a pile on the bag's own tile is lifted into the bag where it lies", OnTheBagsTile),
        new TestCase("collect: a route found for the tree and gone for the hop is no way", RouteGone),
        new TestCase("collect: a pile the server puts down on another tile was not put down where asked", PutElsewhere),
        new TestCase("collect: a bag out of view is reached at its targeted tile", BagOutOfView),
        new TestCase("collect: the record's moves: a merge sums, the place removes, an unknown pile changes nothing",
        RecordMoves),
        new TestCase("lumber: At my feet is apart from Logs to, and off clears the old feet setting", AtFeetSetting),
        new TestCase("collect: a Logs to with no tile is not readable", NotReadable),
        new TestCase("collect: a bag inside a container is reached at its targeted tile", BagInContainer),
        new TestCase("collect: a stop after one pile's last hop ends it before the next pile", StopBetweenPiles),
        new TestCase("collect: a route ending at another x is past a teleporter too", TeleporterX),
        new TestCase("collect: with no walk of the test's own the real walk is taken, and its refusal ends it",
        RealWalk),
        new TestCase("collect: the link closed between hops ends it", ClosedBetweenHops),
        new TestCase("map: a dropped pile's tile reads its logs on hover, the tile beside it nothing", DropHover),
        new TestCase("lumber: no tile beside taking the logs, they go to Logs to and the earlier drops are collected "
        + "there", BesideRefusedCarries),
        new TestCase("collect now: with no circuit, the piles collected under Collect at and the circuit ends",
        CollectNowAlone),
        new TestCase("collect now: during Lumberjacking the next step collects under Collect at, then chopping goes on",
        CollectNowDuring),
        new TestCase("collect now: no piles is refused", CollectNowNothing),
        new TestCase("reset collection point: Logs to the player's tile and the old tile's pile in the record, "
        + "nothing started; Collect now carries both there", ResetCollects),
        new TestCase("reset collection point: an old tile out of view adds no pile and starts nothing",
        ResetOldOutOfView),
        new TestCase("reset collection point: the same tile, an old bag out of view or no Logs to adds no pile; a "
        + "pile on the player's tile leaves the record", ResetAddsNothing),
        new TestCase("reset collection point: with the link closed nothing changes", ResetClosed),
        new TestCase("discover: log piles in view join the record as wild, but the place's, a recorded tile's and a "
        + "house's", Discover),
        new TestCase("discover: with no place for the collection nothing is found", DiscoverNoPlace),
        new TestCase("discover: a pile in a house is asked once; a forgotten pile is found again", DiscoverAgain),
        new TestCase("collect: wild logs gone are passed and the next pile still collected", WildGone),
        new TestCase("collect: the record's wild flag: kept, ours on a wild pile makes it ours, a merge is wild only "
        + "of two wild", WildRecord),
        new TestCase("collect now: wild logs in view are found and collected with no pile dropped", CollectNowWild),
        new TestCase("hop to me: the object targeted is hopped to the tile kept at the press, no drop on the stand's "
        + "tile", HopToMe),
        new TestCase("hop target to target: a place targeted with the object out of view hops it from its kept tile; "
        + "never back in view, the hop ends naming it", HopTargetOutOfView),
        new TestCase("hop to me: a cancel, an item in the pack or no press first hops nothing", HopRefused),
        new TestCase("hop to me: an object already there, no way, or gone on the way says so", HopEnds),
        new TestCase("hop to me: the button again stops it, and no circuit starts while it runs", HopStops),
        new TestCase("hop to me: the Stop button stops a running hop; with none running it says so", HopStopButton),
        new TestCase("hop target to target: the object's cursor, then a location cursor, and the object hopped to the "
        + "tile targeted", HopTargetToTarget),
        new TestCase("hop target to target: either cursor cancelled, a tile for the object, no object asked, or the "
        + "object gone hops nothing", HopTargetRefused),
        new TestCase("hop to me: a circuit running, a ground target, a second target or the client map's no way hops "
        + "nothing", HopRefusedMore));

    public static ImmutableArray<TestCase> Cases => s_cases;

    static void HopsNone() => Check.Equal(0, Bunnyhop.Plan(0, 2).Length, "hops");

    static void HopsBounds()
    {
        for (int reach = 1; reach <= 3; reach++)
        {
            for (int length = 1; length <= 20; length++)
            {
                ImmutableArray<PileHop> hops = Bunnyhop.Plan(length, reach);
                int pile = 0;
                foreach (PileHop h in hops)
                {
                    Check.Equal(pile, h.Lift, "lifted where the last hop dropped it (" + length + ", " + reach + ")");
                    Check.True(h.Stand - h.Lift <= reach && h.Stand >= h.Lift, "the lift within reach");
                    Check.True(h.Drop - h.Stand <= reach && h.Drop > h.Stand,
                        "the drop within reach, never on the stand's own tile");
                    Check.True(h.Drop > h.Lift, "the pile forward");
                    pile = h.Drop;
                }
                Check.Equal(length, pile, "on the end (" + length + ", " + reach + ")");
                Check.Equal((length + 2 * reach - 1) / (2 * reach), hops.Length,
                    "the fewest hops (" + length + ", " + reach + ")");
            }
        }
    }

    static void HopsNine()
    {
        ImmutableArray<PileHop> hops = Bunnyhop.Plan(9, 2);
        Check.Equal(3, hops.Length, "hops");
        Check.Equal(new PileHop(2, 0, 4), hops[0], "first");
        Check.Equal(new PileHop(6, 4, 8), hops[1], "second");
        Check.Equal(new PileHop(8, 8, 9), hops[2], "last, standing short of the end");
    }

    static void HopsReachZero() => Check.Throws<ArgumentOutOfRangeException>(() => Bunnyhop.Plan(4, 0), "reach 0");

    static int[,] Line(params int[] at)
    {
        int n = at.Length;
        int[,] d = new int[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
                d[i, j] = Math.Abs(at[i] - at[j]);
        }
        return d;
    }

    static void TreeLine()
    {
        PileTree t = PileTree.Build(Line(0, 10, 20));
        Check.Equal(-1, t.Parent[0], "the place");
        Check.Equal(0, t.Parent[1], "the near pile to the place");
        Check.Equal(1, t.Parent[2], "the far pile onto the near");
        Check.Equal(2, t.Order.Length, "both move");
        Check.Equal(2, t.Order[0], "the far first");
        Check.Equal(1, t.Order[1], "then the near, carrying it");
    }

    // The place at 0, a pile at 10 and one at 12: 12 goes onto 10 (2), never to the place (12).
    static void TreeMerge()
    {
        PileTree t = PileTree.Build(Line(0, 12, 10));
        Check.Equal(2, t.Parent[1], "12 onto 10");
        Check.Equal(0, t.Parent[2], "10 to the place");
    }

    static void TreeUnreached()
    {
        int[,] d = Line(0, 5, 9);
        d[2, 0] = d[0, 2] = d[2, 1] = d[1, 2] = PileTree.Unreached;
        PileTree t = PileTree.Build(d);
        Check.Equal(-1, t.Parent[2], "no parent");
        Check.Equal(1, t.Order.Length, "only the reached pile moves");
        Check.Equal(1, t.Order[0], "the reached pile");
    }

    static void TreeAlone()
    {
        PileTree t = PileTree.Build(new int[1, 1]);
        Check.Equal(0, t.Order.Length, "nothing moves");
        PileTree none = PileTree.Build(new int[0, 0]);
        Check.Equal(0, none.Parent.Length, "no nodes");
        Check.Equal(0, none.Order.Length, "no order");
    }

    static byte[] Delete(uint serial) => new byte[] { 0x1D, (byte)(serial >> 24), (byte)(serial >> 16),
        (byte)(serial >> 8), (byte)serial };

    // 0x1A: length, serial with the top bit (an amount follows), graphic, amount, x, y, z.
    static byte[] Lying(uint serial, int graphic, int amount, int x, int y)
    {
        uint s = serial | 0x80000000;
        return new byte[] { 0x1A, 0, 16, (byte)(s >> 24), (byte)(s >> 16), (byte)(s >> 8), (byte)s,
            (byte)(graphic >> 8), (byte)graphic, (byte)(amount >> 8), (byte)amount, (byte)(x >> 8), (byte)x,
            (byte)(y >> 8), (byte)y, 0 };
    }

    static uint U32(byte[] p, int at) => (uint)(p[at] << 24 | p[at + 1] << 16 | p[at + 2] << 8 | p[at + 3]);

    static int U16(byte[] p, int at) => p[at] << 8 | p[at + 1];

    static int Sent(FakeShard s, byte op)
    {
        int n = 0;
        foreach (byte[] p in s.ServerGot)
            n += p[0] == op ? 1 : 0;
        return n;
    }

    // The tiles from one to another a step at a time, straight or diagonal, as the test's route.
    static int[][] Straight(int fx, int fy, int tx, int ty)
    {
        List<int[]> l = new() { new[] { fx, fy, 0 } };
        int x = fx;
        int y = fy;
        while (x != tx || y != ty)
        {
            x += Math.Sign(tx - x);
            y += Math.Sign(ty - y);
            l.Add(new[] { x, y, 0 });
        }
        return l.ToArray();
    }

    // What the shard saw of the hops: each lift and drop, with where the player stood and where the item lay.
    sealed record Move(byte Op, uint Item, int PlayerX, int PlayerY, int X, int Y, uint Onto);

    // The ground piles the shard keeps (serial to x, y, amount): a lift taken, a drop on the ground moves the pile
    // there, a drop onto a pile merges into it, a drop into the bag puts it there, unless ignore holds; pack stacks
    // dropped leave the pack for the ground, a status of weight after.
    static List<Move> Shard(FakeShard s, Ground ground, Func<bool>? ignore = null,
        int weightAfterDrop = 0, Func<int, string?>? chop = null)
    {
        List<Move> moves = new();
        ValueMap<uint, int> pack = ValueMap<uint, int>.Of((Logs, 30), (Logs2, 20));
        s.Answer = p =>
        {
            int px = s.Link.State.X;
            int py = s.Link.State.Y;
            if (p[0] == 0x07)
            {
                uint item = U32(p, 1);
                int[]? at = ground.Piles[item];
                lock (moves)
                    moves.Add(new Move(0x07, item, px, py, at?[0] ?? -1, at?[1] ?? -1, 0));
                return ImmutableArray<byte[]>.Empty;
            }
            if (p[0] == 0x08)
            {
                uint item = U32(p, 1);
                uint onto = U32(p, 10);
                int x = U16(p, 5);
                int y = U16(p, 7);
                // A drop onto a pile names no tile: the pile's own.
                if (ground.Piles.TryGet(onto, out int[]? target))
                {
                    x = target[0];
                    y = target[1];
                }
                lock (moves)
                    moves.Add(new Move(0x08, item, px, py, x, y, onto));
                if (ignore is not null && ignore())
                    return ImmutableArray<byte[]>.Empty;
                int amount = ground.Piles.TryGet(item, out int[]? g) ? g[2] : pack.TryGet(item, out int n) ? n : 1;
                pack = pack.Remove(item);
                ImmutableArray<byte[]>.Builder replies = ImmutableArray.CreateBuilder<byte[]>();
                if (onto == Pack)
                {
                    // A pile carried: into the pack, a pack stack of its own.
                    ground.Piles = ground.Piles.Remove(item);
                    pack = pack.Set(item, amount);
                    replies.Add(Packets.Contained(item, LogArt, amount, Pack));
                }
                else if (onto == Bag)
                {
                    ground.Piles = ground.Piles.Remove(item);
                    replies.Add(Packets.Contained(item, LogArt, amount, Bag));
                }
                else if (ground.Piles.TryGet(onto, out int[]? under))
                {
                    ground.Piles = ground.Piles.Remove(item);
                    under[2] += amount;
                    replies.Add(Delete(item));
                    replies.Add(Lying(onto, LogArt, under[2], under[0], under[1]));
                }
                else
                {
                    ground.Piles = ground.Piles.Set(item, new[] { x, y, amount });
                    replies.Add(Lying(item, LogArt, amount, x, y));
                }
                if (weightAfterDrop > 0)
                {
                    byte[] st = Packets.Status(51);
                    st[64] = (byte)(weightAfterDrop >> 8);
                    st[65] = (byte)weightAfterDrop;
                    replies.Add(st);
                }
                return replies.ToImmutable();
            }
            if (p[0] == 0x13)
                return ImmutableArray.Create<byte[]>(Packets.Wear(U32(p, 1), AxeArt, p[5], Packets.Player));
            if (p[0] == 0x06)
                return ImmutableArray.Create<byte[]>(Packets.Cursor(0x00001234, 0x01));
            if (p[0] == 0x6C && chop is not null)
            {
                string? said = chop(U16(p, 11));
                if (said is not null)
                    s.Also(400, () => s.ServerSends(Packets.Speech(0xFFFFFFFF, "", said, 0)));
            }
            return ImmutableArray<byte[]>.Empty;
        };
        return moves;
    }

    static void Kwik(FakeShard s)
    {
        s.ServerSends(Packets.LoginConfirm(1000, 1000, 0));
        s.ServerSends(Packets.Wear(Pack, 0x0E75, 0x15, Packets.Player));
        s.Clock.Sleep(1);
    }

    // The piles lying and in the record, and the test's route and walk: the walk draws the player on the tile.
    static Ground Piles(FakeShard s, AssistFeatures f, List<int[]> walks, params int[][] piles)
    {
        Ground ground = new();
        f.LumberPilesClear();
        for (int i = 0; i < piles.Length; i++)
        {
            uint serial = PileA + (uint)i;
            ground.Piles = ground.Piles.Set(serial, new[] { piles[i][0], piles[i][1], piles[i][2] });
            s.ServerSends(Lying(serial, LogArt, piles[i][2], piles[i][0], piles[i][1]));
            f.LumberPileAdd(piles[i][0], piles[i][1], piles[i][2]);
        }
        s.Clock.Sleep(1);
        f.CollectRoute = Straight;
        f.CollectWalk = (x, y) =>
        {
            walks.Add(new[] { x, y });
            s.ServerSends(Packets.Draw(x, y, 0, 0));
            s.Clock.Sleep(1);
            return null!;
        };
        return ground;
    }

    static void Within(List<Move> moves)
    {
        foreach (Move m in moves)
        {
            // The bag's reach is its own case; a pack stack lifted lies on no tile; a carried pile goes into the pack.
            if (m.Onto == Bag || m.Onto == Pack || m.X < 0)
                continue;
            Check.True(Math.Max(Math.Abs(m.X - m.PlayerX), Math.Abs(m.Y - m.PlayerY)) <= AssistFeatures.HopReach,
                (m.Op == 7 ? "lift" : "drop") + " at " + m.X + "," + m.Y + " within reach of " + m.PlayerX + ","
                + m.PlayerY);
        }
    }

    // The player at 1000,1000 beside the last pile dropped (1000,1001); the first one dropped at 1000,1020, past the
    // place at 1000,1010. Each goes to the place; the record's order walked to 1020 first and back.
    static void FeetPileFirst()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s);
            List<int[]> walks = new();
            Ground ground = Piles(s, f, walks, new[] { 1000, 1020, 40 }, new[] { 1000, 1001, 30 });
            List<Move> moves = Shard(s, ground);
            f.LumberLogsTo = "ground|0|1000|1010|0";
            try
            {
                Check.Null(f.LumberCollect(s.Link), "LumberCollect");
            }
            finally
            {
                f.LumberLogsTo = "";
            }
            Check.Equal(0, f.LumberPiles().Count, "the record empty");
            Within(moves);
            Move first = moves.Find(m => m.Op == 7)!;
            Check.Equal(PileB, first.Item, "the pile at the feet lifted first");
            Move last = moves.FindLast(m => m.Op == 7)!;
            Check.Equal(PileA, last.Item, "the far pile last");
        });
    }

    static void TwoPiles()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s);
            List<int[]> walks = new();
            Ground ground = Piles(s, f, walks, new[] { 1000, 1004, 40 }, new[] { 1000, 1008, 30 });
            List<Move> moves = Shard(s, ground);
            f.LumberLogsTo = "ground|0|1000|1010|0";
            try
            {
                Check.Null(f.LumberCollect(s.Link), "LumberCollect");
            }
            finally
            {
                f.LumberLogsTo = "";
            }
            Check.Equal(0, f.LumberPiles().Count, "the record empty");
            Within(moves);
            // 1004 is nearer 1008 (4) than the place (6): hopped onto 1008's pile, then the 70 carried 2 tiles.
            Move first = moves.Find(m => m.Op == 8)!;
            Check.Equal(PileA, first.Item, "the 1004 pile first");
            Check.Equal(1008, first.Y, "dropped at 1008");
            Check.Equal(PileB, first.Onto, "onto the 1008 pile");
            Move last = moves.FindLast(m => m.Op == 8)!;
            Check.Equal(PileB, last.Item, "the merged pile last");
            Check.Equal(1010, last.Y, "on the place");
            int[] b = ground.Piles[PileB]!;
            Check.Equal(70, b[2], "all 70 logs there");
            Check.Equal(1000 * 65536 + 1010, b[0] * 65536 + b[1], "at 1000,1010");
            Check.Equal(2, moves.FindAll(m => m.Op == 7).Count, "two lifts");
            Check.True(f.CollectStatus.StartsWith("done: 70 logs"), "status: " + f.CollectStatus);
        });
    }

    static void IntoBag()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s);
            List<int[]> walks = new();
            Ground ground = Piles(s, f, walks, new[] { 1000, 1003, 25 });
            s.ServerSends(Lying(Bag, 0x0E76, 1, 1000, 1007));
            s.Clock.Sleep(1);
            List<Move> moves = Shard(s, ground);
            f.LumberLogsTo = "item|" + Bag.ToString("X") + "|1000|1007|0";
            try
            {
                Check.Null(f.LumberCollect(s.Link), "LumberCollect");
            }
            finally
            {
                f.LumberLogsTo = "";
            }
            Move last = moves.FindLast(m => m.Op == 8)!;
            Check.Equal(Bag, last.Onto, "into the bag");
            Check.Equal(0, f.LumberPiles().Count, "the record empty");
            Check.True(Math.Max(Math.Abs(1000 - last.PlayerX), Math.Abs(1007 - last.PlayerY))
                <= AssistFeatures.HopReach, "the bag within reach");
        });
    }

    // A pile of ours the record names and none lies at (the player moved it by hand, 2026-10-23 23:56:31): passed, and
    // the 30 at 1000,1006 still collected.
    static void PileGone()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s);
            List<int[]> walks = new();
            Ground ground = Piles(s, f, walks, new[] { 1000, 1006, 30 });
            f.LumberPileAdd(1000, 1004, 40);
            List<Move> moves = Shard(s, ground);
            f.LumberLogsTo = "ground|0|1000|1010|0";
            try
            {
                Check.Null(f.LumberCollect(s.Link), "LumberCollect");
            }
            finally
            {
                f.LumberLogsTo = "";
            }
            Check.False(moves.Exists(m => m.Op == 7 && m.X == 1000 && m.Y == 1004), "nothing lifted at the empty tile");
            Check.Equal(0, f.LumberPiles().Count, "the record corrected");
            Check.Equal(30, LogsAt(ground, 1000, 1010), "the 30 at the place");
            Check.True(f.Notes.Exists(n => n.Contains("the logs at 1000,1004 are gone; passed")), "noted");
            Check.True(f.Notes.Exists(n => n.Contains("collected 30 logs")), "the gone ones not counted as collected");
        });
    }

    // A leaf of 40 logs at 1000,1040, out of view from 1000,1000 and known by the record alone: walked to, seen there,
    // and carried (the live leaf of 140 at 1337,1881, 2026-10-23 23:55:36, was hopped). never: it is not there when the
    // walk reaches it, and is passed.
    static void CarryOutOfView(bool never, out List<Move> moves, out Ground ground,
        out List<string> notes, int shownAmount = 40)
    {
        List<Move> got = new();
        Ground g = new();
        List<string> said = new();
        RecallTests.WithFeatures((s, f) =>
        {
            s.ServerSends(Packets.Contents(Pack));
            Kwik(s);
            byte[] st = Packets.Status(51);
            st[64] = 0;
            st[65] = 110;
            s.ServerSends(st);
            s.Clock.Sleep(1);
            f.LumberPilesClear();
            f.LumberPileAdd(1000, 1040, 40);
            if (!never)
                g.Piles = g.Piles.Set(PileA, new[] { 1000, 1040, shownAmount });
            got = Shard(s, g);
            f.CollectRoute = Straight;
            bool shown = false;
            f.CollectWalk = (x, y) =>
            {
                s.ServerSends(Packets.Draw(x, y, 0, 0));
                if (!never && !shown && Math.Abs(y - 1040) <= 18)
                {
                    shown = true;
                    s.ServerSends(Lying(PileA, LogArt, shownAmount, 1000, 1040));
                }
                s.Clock.Sleep(1);
                return null!;
            };
            f.LumberLogsTo = "ground|0|1000|1010|0";
            try
            {
                Check.Null(f.LumberCollect(s.Link), "LumberCollect");
                Check.Equal(0, f.LumberPiles().Count, "the record empty");
            }
            finally
            {
                f.LumberLogsTo = "";
                f.LumberPilesClear();
            }
            lock (f.Notes)
                said = new List<string>(f.Notes);
        });
        moves = got;
        ground = g;
        notes = said;
    }

    static void CarryUnseen()
    {
        CarryOutOfView(false, out List<Move> moves, out Ground ground, out List<string> notes);
        Check.Equal(1, moves.FindAll(m => m.Op == 8 && m.Onto == Pack).Count, "into the pack");
        Check.True(notes.Exists(n => n.Contains("carrying the 40 logs at 1000,1040 to 1000,1010 in the pack")),
            "noted");
        Check.Equal(40, LogsAt(ground, 1000, 1010), "the 40 on the place");
        CarryOutOfView(true, out moves, out _, out notes);
        Check.Equal(0, moves.Count, "nothing lifted");
        Check.True(notes.Exists(n => n.Contains("the logs at 1000,1040 are gone; passed")), "the missing pile passed");
        // The record says 40, the pile seen there holds 400 (800 stones past the 418 limit): hopped, not carried.
        CarryOutOfView(false, out moves, out ground, out notes, shownAmount: 400);
        Check.Equal(0, moves.FindAll(m => m.Op == 8 && m.Onto == Pack).Count, "nothing into the pack");
        Check.Equal(400, LogsAt(ground, 1000, 1010), "the 400 hopped to the place");
    }

    static void Teleporter()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s);
            List<int[]> walks = new();
            Ground ground = Piles(s, f, walks, new[] { 1000, 1004, 40 });
            List<Move> moves = Shard(s, ground);
            // The planner's route restarts past a teleporter: its end is not the place.
            f.CollectRoute = (fx, fy, tx, ty) => Straight(fx, fy, tx, ty - 3);
            f.LumberLogsTo = "ground|0|1000|1010|0";
            string? why;
            try
            {
                why = f.LumberCollect(s.Link);
            }
            finally
            {
                f.LumberLogsTo = "";
                f.LumberPilesClear();
            }
            Check.True(why is not null && why.Contains("crosses a teleporter"), "why: " + why);
            Check.Equal(0, moves.Count, "nothing lifted");
        });
    }

    static void NoRoute()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s);
            List<int[]> walks = new();
            Ground ground = Piles(s, f, walks, new[] { 1000, 1004, 40 });
            List<Move> moves = Shard(s, ground);
            f.CollectRoute = (fx, fy, tx, ty) => null!;
            // No route either way: the tree leaves the pile out, so nothing is hopped.
            f.LumberLogsTo = "ground|0|1000|1010|0";
            string? why;
            try
            {
                why = f.LumberCollect(s.Link);
            }
            finally
            {
                f.LumberLogsTo = "";
            }
            Check.Null(why, "the collection goes on");
            Check.True(f.Notes.Exists(n =>
                n.Contains("no way from the pile at 1000,1004 to 1000,1010; kept in the record")), "named");
            Check.True(f.CollectStatus.EndsWith("; no way from 1000,1004"), "status: " + f.CollectStatus);
            Check.Equal(0, moves.Count, "nothing lifted");
            Check.Equal(1, f.LumberPiles().Count, "the pile still in the record");
            f.LumberPilesClear();
        });
    }

    static void NoPlace()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s);
            f.LumberPilesClear();
            f.LumberPileAdd(1000, 1004, 40);
            try
            {
                f.LumberLogsTo = "mobile|BEEF";
                string? mule = f.LumberCollect(s.Link);
                Check.True(mule is not null && mule.Contains("a pack animal moves"), "mule: " + mule);
                f.LumberLogsTo = "feet|0";
                string? feet = f.LumberCollect(s.Link);
                Check.True(feet is not null && feet.Contains("no place for the collection"), "feet: " + feet);
                f.LumberLogsTo = "";
                string? none = f.LumberCollect(s.Link);
                Check.True(none is not null && none.Contains("no place for the collection"), "unset: " + none);
            }
            finally
            {
                f.LumberLogsTo = "";
                f.LumberPilesClear();
            }
        });
    }

    static void DropIgnored()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s);
            List<int[]> walks = new();
            Ground ground = Piles(s, f, walks, new[] { 1000, 1004, 40 });
            List<Move> moves = Shard(s, ground, () => true);
            f.LumberLogsTo = "ground|0|1000|1010|0";
            string? why;
            try
            {
                why = f.LumberCollect(s.Link);
            }
            finally
            {
                f.LumberLogsTo = "";
            }
            Check.True(why is not null && why.Contains("the pile at 1000,1004: ")
                && why.Contains("the server put it back where it lay"), "why: " + why);
            Check.Equal(1, moves.FindAll(m => m.Op == 8).Count, "one drop, not repeated");
            List<int[]> rec = f.LumberPiles();
            Check.Equal(1, rec.Count, "one pile in the record");
            Check.Equal(1004, rec[0][1], "where it was");
            f.LumberPilesClear();
        });
    }

    // The pack full of logs (360 stones, 50 logs): put down at feet, the 50 reaching Collect at 40 carried 6 tiles to
    // the place, then the tree beside the place chopped.
    static void CircuitCollects()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            s.ServerSends(Packets.Contents(Pack));
            Kwik(s);
            s.ServerSends(Packets.Contained(Axe, AxeArt, 1, Pack));
            s.ServerSends(Packets.Contained(Logs, LogArt, 30, Pack));
            s.ServerSends(Packets.Contained(Logs2, LogArt, 20, Pack));
            byte[] st = Packets.Status(51);
            st[64] = 360 >> 8;
            st[65] = 360 & 0xFF;
            s.ServerSends(st);
            s.Clock.Sleep(1);
            List<int[]> walks = new();
            Ground ground = Piles(s, f, walks);
            List<Move> moves = Shard(s, ground, weightAfterDrop: 110,
                chop: x => "You put some logs into your backpack.");
            f.LumberAxe = Axe;
            f.LumberLogsTo = "ground|0|1000|1006|0";
            f.LumberAtFeet = true;
            f.LumberCollectAt = 40;
            f.LumberStatics = (x, y) => x == 1001 && y == 1006 ? new List<int[]> { new[] { TreeArt, 0 } }
                : new List<int[]>();
            Sentinel(f);
            int chops() => Sent(s, 0x6C);
            try
            {
                Check.Null(f.CircuitLumber(), "CircuitLumber");
                for (int i = 0; i < 1000 && f.CircuitRunning && chops() == 0; i++)
                    s.Clock.Sleep(200);
                f.CircuitHalt("test done");
                for (int i = 0; i < 2000 && f.CircuitRunning; i++)
                    s.Clock.Sleep(50);
                s.Clock.Sleep(600);
            }
            finally
            {
                f.LumberAtFeet = false;
                f.LumberLogsTo = "";
                f.LumberCollectAt = 1000;
                f.LumberPilesClear();
                f.LumberEmptyClear();
            }
            Check.Equal(2, SentinelProbes(f), "the sentinel probed at the start and after the collection");
            Check.True(chops() >= 1, "chopping went on after the collection: " + f.CircuitStatus);
            Check.Equal(0, f.LumberPiles().Count, "the record empty");
            Within(moves);
            KeyValuePair<uint, int[]> there = default;
            foreach (KeyValuePair<uint, int[]> kv in ground.Piles)
            {
                if (kv.Value[0] == 1000 && kv.Value[1] == 1006)
                    there = kv;
            }
            Check.True(there.Value is not null && there.Value[2] == 50, "the 50 logs on the place");
        });
    }

    // A pack of 50 logs under the weight limit (110 stones), Collect at 1000 and no tree: the logs put down at feet,
    // then carried to the place, then the end; no chop.
    static void NoTreeCollects()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            List<Move> moves = FullPack(s, f, out Ground ground);
            byte[] st = Packets.Status(51);
            st[64] = 0;
            st[65] = 110;
            s.ServerSends(st);
            s.Clock.Sleep(1);
            f.LumberCollectAt = 1000;
            f.LumberStatics = (x, y) => new List<int[]>();
            Sentinel(f);
            try
            {
                Check.Null(f.CircuitLumber(), "CircuitLumber");
                for (int i = 0; i < 2000 && f.CircuitRunning; i++)
                    s.Clock.Sleep(50);
            }
            finally
            {
                f.LumberAtFeet = false;
                f.LumberLogsTo = "";
                f.LumberPilesClear();
                f.LumberEmptyClear();
            }
            Check.True(f.CircuitStatus.Contains("no tree within"), "end: " + f.CircuitStatus);
            Check.Equal(0, Sent(s, 0x6C), "no chop");
            Check.Equal(0, f.LumberPiles().Count, "the record empty");
            Within(moves);
            KeyValuePair<uint, int[]> there = default;
            foreach (KeyValuePair<uint, int[]> kv in ground.Piles)
            {
                if (kv.Value[0] == 1000 && kv.Value[1] == 1006)
                    there = kv;
            }
            Check.True(there.Value is not null && there.Value[2] == 50, "the 50 logs on the place");
        });
    }

    // No logs in the pack and no piles: the end with nothing lifted or dropped.
    static void NoTreeNoPiles()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            s.ServerSends(Packets.Contents(Pack));
            Kwik(s);
            s.ServerSends(Packets.Contained(Axe, AxeArt, 1, Pack));
            s.Clock.Sleep(1);
            Ground ground = Piles(s, f, new List<int[]>());
            List<Move> moves = Shard(s, ground);
            f.LumberAxe = Axe;
            f.LumberLogsTo = "ground|0|1000|1006|0";
            f.LumberAtFeet = true;
            f.LumberStatics = (x, y) => new List<int[]>();
            Sentinel(f);
            try
            {
                Check.Null(f.CircuitLumber(), "CircuitLumber");
                for (int i = 0; i < 2000 && f.CircuitRunning; i++)
                    s.Clock.Sleep(50);
            }
            finally
            {
                f.LumberAtFeet = false;
                f.LumberLogsTo = "";
                f.LumberPilesClear();
                f.LumberEmptyClear();
            }
            Check.True(f.CircuitStatus.Contains("no tree within"), "end: " + f.CircuitStatus);
            Check.False(f.CircuitStatus.Contains("collection"), "no collection: " + f.CircuitStatus);
            Check.Equal(0, moves.Count, "nothing lifted or dropped");
        });
    }

    // NoTreeCollects with every drop ignored by the server: the pack's logs stay, and the end names the store's
    // failure.
    static void NoTreeStoreFails()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            FullPack(s, f, out Ground ground);
            List<Move> moves = Shard(s, ground, ignore: () => true);
            byte[] st = Packets.Status(51);
            st[64] = 0;
            st[65] = 110;
            s.ServerSends(st);
            s.Clock.Sleep(1);
            f.LumberCollectAt = 1000;
            f.LumberStatics = (x, y) => new List<int[]>();
            Sentinel(f);
            try
            {
                Check.Null(f.CircuitLumber(), "CircuitLumber");
                for (int i = 0; i < 2000 && f.CircuitRunning; i++)
                    s.Clock.Sleep(50);
            }
            finally
            {
                f.LumberAtFeet = false;
                f.LumberLogsTo = "";
                f.LumberCollectAt = 1000;
                f.LumberPilesClear();
                f.LumberEmptyClear();
            }
            Check.True(f.CircuitStatus.Contains("no tree within") && f.CircuitStatus.Contains("storing the logs: "),
                "end: " + f.CircuitStatus);
            Check.Equal(0, moves.FindAll(m => m.Op == 7 && m.X >= 0).Count, "no pile lifted");
        });
    }

    // An empty pack, one pile at 1000,1004 and the collection's walk refused: the end names the collection's failure.
    static void NoTreeCollectFails()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            s.ServerSends(Packets.Contents(Pack));
            Kwik(s);
            s.ServerSends(Packets.Contained(Axe, AxeArt, 1, Pack));
            s.Clock.Sleep(1);
            Ground ground = Piles(s, f, new List<int[]>(), new[] { 1000, 1004, 40 });
            List<Move> moves = Shard(s, ground);
            f.CollectWalk = (x, y) => "blocked";
            f.LumberAxe = Axe;
            f.LumberLogsTo = "ground|0|1000|1010|0";
            f.LumberAtFeet = true;
            f.LumberStatics = (x, y) => new List<int[]>();
            Sentinel(f);
            int left;
            try
            {
                Check.Null(f.CircuitLumber(), "CircuitLumber");
                for (int i = 0; i < 2000 && f.CircuitRunning; i++)
                    s.Clock.Sleep(50);
                left = f.LumberPiles().Count;
            }
            finally
            {
                f.LumberAtFeet = false;
                f.LumberLogsTo = "";
                f.LumberPilesClear();
                f.LumberEmptyClear();
            }
            Check.True(f.CircuitStatus.Contains("no tree within") && f.CircuitStatus.Contains("the collection: ")
                && f.CircuitStatus.Contains("blocked"), "end: " + f.CircuitStatus);
            Check.Equal(1, left, "the pile kept in the record");
            Check.Equal(0, moves.Count, "nothing lifted");
        });
    }

    // A collection to 1000,1010 of the piles given, its moves and its answer; tune sets the features up after the
    // piles.
    static string? Collect(Action<FakeShard, AssistFeatures, Ground, List<int[]>>? tune,
        out List<Move> moves, params int[][] piles)
    {
        string? why = null;
        List<Move> got = new();
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s);
            List<int[]> walks = new();
            Ground ground = Piles(s, f, walks, piles);
            got = Shard(s, ground);
            f.LumberLogsTo = "ground|0|1000|1010|0";
            tune?.Invoke(s, f, ground, walks);
            try
            {
                why = f.LumberCollect(s.Link);
            }
            finally
            {
                f.LumberLogsTo = "";
                f.LumberPilesClear();
            }
        });
        moves = got;
        return why;
    }

    static void NothingToCollect()
    {
        Check.Null(Collect(null, out List<Move> moves), "LumberCollect");
        Check.Equal(0, moves.Count, "nothing moved");
    }

    static void NoMapNoWay()
    {
        string? why = Collect((s, f, g, w) => f.CollectRoute = null, out List<Move> moves, new[] { 1000, 1004, 40 });
        Check.Null(why, "passed, the collection goes on");
        Check.Equal(0, moves.Count, "nothing moved");
    }

    // A pile of 40 at 1000,1004 to the place at 1000,1010; the player at 1000,1000 carrying 110 stones under a limit of
    // limit; the moves and the notes.
    static List<Move> Carry(string limit, out List<string> notes, out Ground ground,
        bool packLogs = false, Func<FakeShard, AssistFeatures, Ground, string?>? edge = null)
    {
        List<Move> got = new();
        List<string> said = new();
        Ground g = new();
        RecallTests.WithFeatures((s, f) =>
        {
            if (edge is not null)
            {
                s.ServerSends(Packets.Contents(Pack));
                Kwik(s);
                byte[] st0 = Packets.Status(51);
                st0[64] = 0;
                st0[65] = 110;
                s.ServerSends(st0);
                s.Clock.Sleep(1);
                g = Piles(s, f, new List<int[]>(), new[] { 1000, 1004, 40 });
                got = Shard(s, g);
                f.LumberLogsTo = "ground|0|1000|1010|0";
                try
                {
                    said.Add(edge(s, f, g) ?? "");
                }
                finally
                {
                    f.LumberLogsTo = "";
                    f.LumberPilesClear();
                }
                lock (f.Notes)
                    said.AddRange(f.Notes);
                return;
            }
            s.ServerSends(Packets.Contents(Pack));
            Kwik(s);
            if (packLogs)
            {
                s.ServerSends(Packets.Contained(Logs, LogArt, 30, Pack));
                s.ServerSends(Packets.Contained(Logs2, LogArt, 20, Pack));
            }
            byte[] st = Packets.Status(51);
            st[64] = 0;
            st[65] = 110;
            s.ServerSends(st);
            s.Clock.Sleep(1);
            g = Piles(s, f, new List<int[]>(), new[] { 1000, 1004, 40 });
            got = Shard(s, g);
            f.LumberLogsTo = "ground|0|1000|1010|0";
            f.LumberAtFeet = packLogs;
            f.Cfg.Set(f.Server + "pack.limit", limit);
            try
            {
                Check.Null(f.LumberCollect(s.Link), "LumberCollect");
                Check.Equal(0, f.LumberPiles().Count, "the record empty");
            }
            finally
            {
                f.LumberLogsTo = "";
                f.LumberAtFeet = false;
                f.LumberPilesClear();
                f.Cfg.Set(f.Server + "pack.limit", "");
            }
            lock (f.Notes)
                said = new List<string>(f.Notes);
        });
        notes = said;
        ground = g;
        return got;
    }

    static int LogsAt(Ground ground, int x, int y)
    {
        int n = 0;
        foreach (int[] g in ground.Piles.Values)
        {
            if (g[0] == x && g[1] == y)
                n += g[2];
        }
        return n;
    }

    static void CarryFits()
    {
        List<Move> moves = Carry("", out List<string> notes, out Ground ground);
        Check.Equal(1, moves.FindAll(m => m.Op == 7 && m.X >= 0).Count, "one lift off the ground");
        Check.Equal(1, moves.FindAll(m => m.Op == 7 && m.X < 0).Count, "one out of the pack");
        Check.Equal(1, moves.FindAll(m => m.Op == 8 && m.Onto == Pack).Count, "into the pack");
        Check.Equal(2, moves.FindAll(m => m.Op == 8).Count, "then one drop at the place");
        Check.True(notes.Exists(n =>
            n.Contains("carrying the 40 logs at 1000,1004 to 1000,1010 in the pack (6 tiles)")), "noted");
        Check.Equal(40, LogsAt(ground, 1000, 1010), "the 40 on the place");
        Within(moves);
    }

    static void CarryTooHeavy()
    {
        // 110 stones and 40 logs of 2 stones: 190 past a limit of 150.
        List<Move> moves = Carry("150", out List<string> notes, out Ground ground);
        Check.Equal(0, moves.FindAll(m => m.Op == 8 && m.Onto == Pack).Count, "nothing into the pack");
        Check.True(moves.FindAll(m => m.Op == 7).Count >= 2, "hopped");
        Check.False(notes.Exists(n => n.Contains("carrying")), "not carried");
        Check.Equal(40, LogsAt(ground, 1000, 1010), "the 40 on the place");
        Within(moves);
    }

    static void CarryEdges()
    {
        // The walk to the pile refused: nothing lifted, the pile in the record.
        Carry("", out List<string> a, out _, edge: (s, f, g) =>
        {
            f.CollectWalk = (x, y) => "blocked";
            string? w = f.LumberCollect(s.Link);
            Check.Equal(1, f.LumberPiles().Count, "refused before: the pile kept");
            return w;
        });
        Check.True(a[0].Contains("walking to 1000,1005: blocked"), "refused before: " + a[0]);
        // The walk with the pile in the pack refused: the pile out of the record, its logs in the pack.
        Carry("", out List<string> b, out _, edge: (s, f, g) =>
        {
            int walks = 0;
            f.CollectWalk = (x, y) => { if (++walks
                == 2) return "blocked"; s.ServerSends(Packets.Draw(x, y, 0, 0)); s.Clock.Sleep(1); return null!; };
            s.ServerSends(Packets.Draw(1000, 1001, 0, 0));
            s.ServerSends(Packets.Draw(1000, 1000, 0, 0));
            s.Clock.Sleep(1);
            string? w = f.LumberCollect(s.Link);
            Check.Equal(0, f.LumberPiles().Count, "refused after: the pile in the pack, not on the record");
            return w;
        });
        Check.True(b[0].Contains("with the pile in the pack: blocked"), "refused after: " + b[0]);
        // The drop into the pack ignored by the server: not taken, the pile in the record.
        Carry("", out List<string> c, out _, edge: (s, f, g) =>
        {
            s.Answer = null;
            List<Move> moves = Shard(s, g, ignore: () => true);
            string? w = f.LumberCollect(s.Link);
            Check.Equal(1, f.LumberPiles().Count, "not taken: the pile kept");
            return w;
        });
        Check.True(c[0].Contains("Carrying the pile stopped: the server put it back"), "not taken: " + c[0]);
        // The link closed during the walk to the pile: stopped before the lift.
        Carry("", out List<string> d, out _, edge: (s, f, g) =>
        {
            f.CollectWalk = (x, y) => { s.CloseServer(); for (int i = 0; i < 200
                && !s.Link.Closed; i++) s.Clock.Sleep(50); return null!; };
            string? w = f.LumberCollect(s.Link);
            Check.Equal(1, f.LumberPiles().Count, "stopped: the pile kept");
            return w;
        });
        Check.True(d[0].Contains("stopped"), "stopped: " + d[0]);
        // Into a bag lying on the place.
        Carry("", out List<string> e, out Ground ge, edge: (s, f, g) =>
        {
            s.ServerSends(Lying(Bag, 0x0E76, 1, 1000, 1010));
            s.Clock.Sleep(1);
            f.LumberLogsTo = "item|" + Bag.ToString("X") + "|1000|1010|0";
            string? w = f.LumberCollect(s.Link);
            Check.Equal(0, f.LumberPiles().Count, "into the bag: the record empty");
            return w;
        });
        Check.Equal("", e[0], "into the bag");
        Check.True(e.Exists(n => n.Contains("carrying the 40 logs")), "carried into the bag");
    }

    static void CarryPackFirst()
    {
        List<Move> moves = Carry("", out List<string> notes, out Ground ground, packLogs: true);
        Move first = moves.Find(m => m.Op == 8)!;
        Check.True(first.Item == Logs || first.Item == Logs2, "the pack's logs put down first");
        Check.True(notes.Exists(n => n.Contains("50 logs put down")), "at feet");
        Check.Equal(90, LogsAt(ground, 1000, 1010), "the 50 and the 40 on the place");
    }

    // Five places, so the tree is weighed by tiles and plans no route: ours at 1000,1004 and 1000,1006 reached; wild
    // logs at 1000,1020 and ours at 1000,1022 that no route reaches: the reached collected, the wild forgotten, ours at
    // 1022 kept.
    static void HopNoWayPassed()
    {
        List<int[]> left = new();
        bool wild = false, kept = false;
        string status = "";
        string? why = Collect((s, f, g, w) =>
        {
            f.CollectRoute = (fx, fy, tx, ty) => fy >= 1020 || ty >= 1020 ? null! : Straight(fx, fy, tx, ty);
            f.LumberPileAdd(1000, 1020, 15, true);
            f.LumberPileAdd(1000, 1022, 25);
            Check.Null(f.LumberCollect(s.Link), "LumberCollect");
            left = f.LumberPiles();
            status = f.CollectStatus;
            wild = f.Notes.Exists(n => n.Contains("no way from the wild logs at 1000,1020 (no way to")
                && n.EndsWith("forgotten"));
            kept = f.Notes.Exists(n => n.Contains("no way from the pile at 1000,1022 (no way to")
                && n.EndsWith("kept in the record"));
        }, out List<Move> moves, new[] { 1000, 1004, 40 }, new[] { 1000, 1006, 30 });
        Check.Null(why, "nothing left to do");
        Check.Equal(1, left.Count, "ours at 1022 kept");
        Check.Equal(1022, left[0][1], "at 1022");
        Check.True(wild, "the wild named");
        Check.True(kept, "ours named");
        Check.True(status.StartsWith("done: 70 logs") && status.EndsWith("no way from 1000,1022"), "status: " + status);
    }

    // Ours at 1000,1004 reached; wild logs at 1000,1020 that no route reaches: ours collected, the wild forgotten.
    static void WildNoWayForgotten()
    {
        int left = -1;
        bool noted = false;
        string? why = Collect((s, f, g, w) =>
        {
            f.CollectRoute = (fx, fy, tx, ty) => fy == 1020 || ty == 1020 ? null! : Straight(fx, fy, tx, ty);
            f.LumberPileAdd(1000, 1020, 15, true);
            Check.Null(f.LumberCollect(s.Link), "LumberCollect");
            left = f.LumberPiles().Count;
            noted = f.Notes.Exists(n => n.Contains("no way from the wild logs at 1000,1020 to 1000,1010; forgotten"));
        }, out List<Move> moves, new[] { 1000, 1004, 40 });
        Check.Null(why, "nothing left");
        Check.Equal(0, left, "ours collected and the wild forgotten");
        Check.True(noted, "the wild named");
        Check.True(moves.Exists(m => m.Op == 7), "ours lifted");
    }

    static void ManyByTiles()
    {
        int[][] piles = new int[AssistFeatures.CollectPlannedMax][];
        for (int i = 0; i < piles.Length; i++)
            piles[i] = new[] { 988 + i, 1009, 5 };
        int left = -1;
        string status = "";
        int routes = 0;
        string? why = Collect((s, f, g, w) =>
        {
            f.CollectRoute = (fx, fy, tx, ty) =>
            {
                routes++;
                return Straight(fx, fy, tx, ty);
            };
            Check.Null(f.LumberCollect(s.Link), "LumberCollect");
            left = f.LumberPiles().Count;
            status = f.CollectStatus;
        }, out List<Move> moves, piles);
        Check.Null(why, "nothing left");
        Check.Equal(0, left, "every pile reached the place");
        Check.Equal(AssistFeatures.CollectPlannedMax, routes, "one route a pile, none for the tree");
        Check.True(status.StartsWith("done: " + 5 * AssistFeatures.CollectPlannedMax + " logs"), "status: " + status);
    }

    // The circuit's collection stopped during its first walk: that hop finishes, the next does not start.
    static void StopBetweenHops()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            List<Move> moves = FullPack(s, f, out Ground ground);
            // A pack limit of 150 stones: room for a chop's logs over the 110 left, none for the pile, so it hops.
            f.Cfg.Set(f.Server + "pack.limit", "150");
            // The stop comes with the second walk, after the first hop's drop.
            int walked = 0;
            f.CollectWalk = (x, y) =>
            {
                s.ServerSends(Packets.Draw(x, y, 0, 0));
                s.Clock.Sleep(1);
                if (++walked == 2)
                    f.CircuitHalt("test");
                return null!;
            };
            int[][] rec;
            try
            {
                Check.Null(f.CircuitLumber(), "CircuitLumber");
                for (int i = 0; i < 2000 && f.CircuitRunning; i++)
                    s.Clock.Sleep(50);
                rec = f.LumberPiles().ToArray();
            }
            finally
            {
                f.LumberAtFeet = false;
                f.LumberLogsTo = "";
                f.LumberCollectAt = 1000;
                f.LumberPilesClear();
                f.Cfg.Set(f.Server + "pack.limit", "");
            }
            Check.True(f.CircuitStatus.Contains("stopped"), "end: " + f.CircuitStatus);
            Check.Equal(1, rec.Length, "one pile in the record");
            // The feet pile lies south of the player (1000,1001): the first hop stands at 1003 and drops at 1005.
            Check.Equal(1005, rec[0][1], "where the first hop put it");
            Check.Equal(0, Sent(s, 0x6C), "no chop");
        });
    }

    // The circuit's start of CircuitCollects: a full pack of 50 logs, At my feet, Logs to 1000,1006, Collect at 40.
    static List<Move> FullPack(FakeShard s, AssistFeatures f, out Ground ground)
    {
        s.ServerSends(Packets.Contents(Pack));
        Kwik(s);
        s.ServerSends(Packets.Contained(Axe, AxeArt, 1, Pack));
        s.ServerSends(Packets.Contained(Logs, LogArt, 30, Pack));
        s.ServerSends(Packets.Contained(Logs2, LogArt, 20, Pack));
        byte[] st = Packets.Status(51);
        st[64] = 360 >> 8;
        st[65] = 360 & 0xFF;
        s.ServerSends(st);
        s.Clock.Sleep(1);
        ground = Piles(s, f, new List<int[]>());
        List<Move> moves = Shard(s, ground, weightAfterDrop: 110, chop: x => "You put some logs into your backpack.");
        f.LumberAxe = Axe;
        f.LumberLogsTo = "ground|0|1000|1006|0";
        f.LumberAtFeet = true;
        f.LumberCollectAt = 40;
        f.LumberStatics = (x, y) => x == 1001 && y == 1006 ? new List<int[]> { new[] { TreeArt, 0 } }
            : new List<int[]>();
        return moves;
    }

    static void WalkFails()
    {
        string? why = Collect((s, f, g, w) => f.CollectWalk = (x, y) => "blocked", out List<Move> moves,
            new[] { 1000, 1004, 40 });
        Check.True(why is not null && why.Contains("walking to 1000,1006: blocked"), "why: " + why);
        Check.Equal(0, moves.Count, "nothing lifted");
    }

    static void AlreadyThere()
    {
        int left = -1;
        string? why = Collect((s, f, g, w) =>
        {
            f.LumberCollect(s.Link);
            left = f.LumberPiles().Count;
        }, out List<Move> moves, new[] { 1000, 1010, 40 });
        Check.Equal(0, left, "the record empty");
        Check.Equal(0, moves.Count, "no lift");
    }

    static void OnTheBagsTile()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s);
            List<int[]> walks = new();
            Ground ground = Piles(s, f, walks, new[] { 1000, 1007, 25 });
            s.ServerSends(Lying(Bag, 0x0E76, 1, 1000, 1007));
            s.Clock.Sleep(1);
            List<Move> moves = Shard(s, ground);
            f.LumberLogsTo = "item|" + Bag.ToString("X") + "|1000|1007|0";
            try
            {
                Check.Null(f.LumberCollect(s.Link), "LumberCollect");
            }
            finally
            {
                f.LumberLogsTo = "";
            }
            Check.Equal(1, moves.FindAll(m => m.Op == 8).Count, "one drop");
            Check.Equal(Bag, moves.Find(m => m.Op == 8)!.Onto, "into the bag");
            Check.Equal(0, f.LumberPiles().Count, "the record empty");
        });
    }

    static void RouteGone()
    {
        string? why = Collect((s, f, g, w) =>
        {
            int n = 0;
            f.CollectRoute = (fx, fy, tx, ty) => n++ == 0 ? Straight(fx, fy, tx, ty) : null!;
        }, out List<Move> moves, new[] { 1000, 1004, 40 });
        Check.Null(why, "passed, the collection goes on");
        Check.Equal(0, moves.Count, "nothing lifted");
    }

    static void PutElsewhere()
    {
        string? why = Collect((s, f, g, w) =>
        {
            s.Answer = p =>
            {
                if (p[0] != 0x08)
                    return ImmutableArray<byte[]>.Empty;
                uint item = U32(p, 1);
                return ImmutableArray.Create<byte[]>(Lying(item, LogArt, 40, U16(p, 5) + 1, U16(p, 7)));
            };
        }, out List<Move> moves, new[] { 1000, 1004, 40 });
        Check.True(why is not null && why.Contains("the pile was not put down at 1000,1008"), "why: " + why);
    }

    static void BagOutOfView()
    {
        List<int[]> asked = new();
        string? why = Collect((s, f, g, w) =>
        {
            f.LumberLogsTo = "item|" + Bag.ToString("X") + "|1000|1007|0";
            f.CollectRoute = (fx, fy, tx, ty) =>
            {
                asked.Add(new[] { tx, ty });
                return Straight(fx, fy, tx, ty);
            };
        }, out List<Move> moves, new[] { 1000, 1003, 25 });
        Check.Null(why, "LumberCollect");
        Check.True(asked.Count > 0 && asked[asked.Count - 1][0] == 1000 && asked[asked.Count - 1][1] == 1007,
            "the hop's route to the targeted tile");
        Check.Equal(Bag, moves.FindLast(m => m.Op == 8)!.Onto, "into the bag");
    }

    static void RecordMoves()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            f.LumberPilesClear();
            f.LumberPileAdd(1, 1, 10);
            f.LumberPileAdd(2, 2, 20);
            f.LumberPileMove(9, 9, 1, 1, false);
            Check.Equal(30, f.LumberDropped(), "an unknown pile changes nothing");
            f.LumberPileMove(1, 1, 2, 2, false);
            Check.Equal(1, f.LumberPiles().Count, "merged");
            Check.Equal(30, f.LumberPiles()[0][2], "summed");
            f.LumberPileMove(2, 2, 3, 3, false);
            Check.Equal(3, f.LumberPiles()[0][0], "moved");
            f.LumberPileMove(3, 3, 4, 4, true);
            Check.Equal(0, f.LumberPiles().Count, "at the place, gone");
        });
    }

    static void AtFeetSetting()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            try
            {
                f.LumberLogsTo = "ground|0|1000|1010|0";
                f.LumberAtFeet = true;
                Check.Equal("Logs to: the ground at 1000,1010,0, at my feet first", f.LumberLogsText(), "both");
                f.LumberAtFeet = false;
                Check.Equal("ground|0|1000|1010|0", f.LumberLogsTo, "off keeps Logs to");
                f.LumberLogsTo = "feet|0";
                Check.True(f.LumberAtFeet, "the old feet setting is at feet");
                Check.Equal("Logs to: at my feet", f.LumberLogsText(), "the old text");
                f.LumberAtFeet = false;
                Check.Equal("", f.LumberLogsTo, "off clears it");
                Check.False(f.LumberAtFeet, "off");
            }
            finally
            {
                f.LumberAtFeet = false;
                f.LumberLogsTo = "";
            }
        });
    }

    static void NotReadable()
    {
        string? why = Collect((s, f, g, w) => f.LumberLogsTo = "ground|0", out List<Move> moves,
            new[] { 1000, 1004, 40 });
        Check.True(why is not null && why.Contains("not readable"), "why: " + why);
    }

    static void BagInContainer()
    {
        List<int[]> asked = new();
        string? why = Collect((s, f, g, w) =>
        {
            s.ServerSends(Packets.Contained(Bag, 0x0E76, 1, 0x44ABFFFF));
            s.Clock.Sleep(1);
            f.LumberLogsTo = "item|" + Bag.ToString("X") + "|1000|1007|0";
            f.CollectRoute = (fx, fy, tx, ty) =>
            {
                asked.Add(new[] { tx, ty });
                return Straight(fx, fy, tx, ty);
            };
        }, out List<Move> moves, new[] { 1000, 1003, 25 });
        Check.Null(why, "LumberCollect");
        Check.Equal(1007, asked[asked.Count - 1][1], "the targeted tile, not the bag's place in its container");
    }

    // Two piles, the feet pile (1000,1000) and one at 1000,1003 on the way: the feet pile hopped onto it in one hop,
    // the circuit stopped during that hop's walk, and the merged pile not moved.
    static void StopBetweenPiles()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            List<Move> moves = FullPack(s, f, out Ground ground);
            ground.Piles = ground.Piles.Set(PileB, new[] { 1000, 1003, 30 });
            s.ServerSends(Lying(PileB, LogArt, 30, 1000, 1003));
            s.Clock.Sleep(1);
            f.LumberPileAdd(1000, 1003, 30);
            // A pack limit of 150 stones: room for a chop's logs over the 110 left, none for a pile, so the piles hop.
            f.Cfg.Set(f.Server + "pack.limit", "150");
            // The stop comes with the second walk, the first pile merged.
            int walked = 0;
            f.CollectWalk = (x, y) =>
            {
                s.ServerSends(Packets.Draw(x, y, 0, 0));
                s.Clock.Sleep(1);
                if (++walked == 2)
                    f.CircuitHalt("test");
                return null!;
            };
            int[][] rec;
            try
            {
                Check.Null(f.CircuitLumber(), "CircuitLumber");
                for (int i = 0; i < 2000 && f.CircuitRunning; i++)
                    s.Clock.Sleep(50);
                rec = f.LumberPiles().ToArray();
            }
            finally
            {
                f.LumberAtFeet = false;
                f.LumberLogsTo = "";
                f.LumberCollectAt = 1000;
                f.LumberPilesClear();
                f.Cfg.Set(f.Server + "pack.limit", "");
            }
            Check.True(f.CircuitStatus.Contains("stopped"), "end: " + f.CircuitStatus);
            Check.Equal(1, rec.Length, "one pile in the record");
            Check.Equal(1003, rec[0][1], "the merged pile where it was");
            Check.Equal(80, rec[0][2], "holding both");
        });
    }

    static void TeleporterX()
    {
        string? why = Collect((s, f, g, w) => f.CollectRoute = (fx, fy, tx, ty) => Straight(fx, fy, tx - 2, ty),
            out List<Move> moves, new[] { 1000, 1004, 40 });
        Check.True(why is not null && why.Contains("crosses a teleporter"), "why: " + why);
    }

    static void RealWalk()
    {
        string? why = Collect((s, f, g, w) => f.CollectWalk = null, out List<Move> moves, new[] { 1000, 1004, 40 });
        Check.True(why is not null && why.Contains("walking to 1000,1006: "), "why: " + why);
        Check.Equal(0, moves.Count, "nothing lifted");
    }

    static void ClosedBetweenHops()
    {
        string? why = Collect((s, f, g, w) =>
        {
            Func<byte[], ImmutableArray<byte[]>> shard = s.Answer!;
            bool closing = false;
            s.Answer = p =>
            {
                ImmutableArray<byte[]> replies = shard(p);
                if (p[0] == 0x08 && !closing)
                {
                    closing = true;
                    s.Also(DropCloseMs, () => s.CloseServer());
                }
                return replies;
            };
            // The link's reader notices the close on its own thread, so the walk after it may run first: it sends
            // nothing into the closed pipe.
            f.CollectWalk = (x, y) =>
            {
                if (closing)
                    return "the link closed";
                s.ServerSends(Packets.Draw(x, y, 0, 0));
                s.Clock.Sleep(1);
                return null!;
            };
        }, out List<Move> moves, new[] { 1000, 1000, 40 });
        Check.True(why is not null, "ended");
        Check.Equal(1, moves.FindAll(m => m.Op == 8).Count, "one hop");
    }

    // Kwik at 1000,1000 with 50 logs; Logs to at 1002,1000 (2 tiles, no walk); an earlier pile of 40 at 1002,1003. The
    // shard puts back every drop from the pack beside Kwik, takes drops on 1002,1000, merges drops onto a pile.
    static void BesideRefusedCarries()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s);
            s.ServerSends(Packets.Contained(Logs, LogArt, 30, Pack));
            s.ServerSends(Packets.Contained(Logs2, LogArt, 20, Pack));
            s.Clock.Sleep(1);
            List<int[]> walks = new();
            Ground ground = Piles(s, f, walks, new[] { 1002, 1003, 40 });
            ValueMap<uint, int> pack = ValueMap<uint, int>.Of((Logs, 30), (Logs2, 20));
            int putBack = 0;
            s.Answer = p =>
            {
                if (p[0] != 0x08)
                    return ImmutableArray<byte[]>.Empty;
                uint item = U32(p, 1);
                uint onto = U32(p, 10);
                int x = U16(p, 5), y = U16(p, 7);
                int amount = ground.Piles.TryGet(item, out int[]? g) ? g[2] : pack.TryGet(item, out int n) ? n : 1;
                if (ground.Piles.TryGet(onto, out int[]? under))
                {
                    ground.Piles = ground.Piles.Remove(item);
                    pack = pack.Remove(item);
                    under[2] += amount;
                    return ImmutableArray.Create(Delete(item), Lying(onto, LogArt, under[2], under[0], under[1]));
                }
                if (x == 1002 && y == 1000)
                {
                    pack = pack.Remove(item);
                    ground.Piles = ground.Piles.Set(item, new[] { x, y, amount });
                    return ImmutableArray.Create<byte[]>(Lying(item, LogArt, amount, x, y));
                }
                putBack++;
                return ImmutableArray.Create<byte[]>(Packets.Contained(item, LogArt, amount, Pack));
            };
            f.LumberLogsTo = "ground|0|1002|1000|0";
            f.LumberAtFeet = true;
            // Eight put-back drops each wait out the answer: a short one keeps the case inside the hang limit.
            int answer = f.Timings.Ms("answer");
            f.Timings.Set("answer", 300);
            try
            {
                Check.Null(f.LumberStore(s.Link), "LumberStore");
            }
            finally
            {
                f.Timings.Set("answer", answer);
                f.LumberAtFeet = false;
                f.LumberLogsTo = "";
            }
            Check.Equal(8, putBack, "every tile beside tried once");
            Check.Equal(0, f.LumberPiles().Count, "the earlier drop collected");
            int there = 0;
            foreach (int[] g in ground.Piles.Values)
            {
                if (g[0] == 1002 && g[1] == 1000)
                    there += g[2];
            }
            Check.Equal(90, there, "the 50 carried and the 40 collected on Logs to");
        });
    }

    static void CollectNowAlone()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s);
            List<int[]> walks = new();
            Ground ground = Piles(s, f, walks, new[] { 1000, 1004, 40 });
            List<Move> moves = Shard(s, ground);
            f.LumberLogsTo = "ground|0|1000|1010|0";
            f.LumberAtFeet = true;
            try
            {
                Check.Null(f.CollectNow(), "CollectNow");
                for (int i = 0; i < 2000 && f.CircuitRunning; i++)
                    s.Clock.Sleep(50);
            }
            finally
            {
                f.LumberAtFeet = false;
                f.LumberLogsTo = "";
            }
            Check.False(f.CircuitRunning, "ended");
            Check.True(f.CircuitStatus.Contains("collected"), "end: " + f.CircuitStatus);
            Check.Equal(0, f.LumberPiles().Count, "the record empty");
            Check.Equal(0, Sent(s, 0x6C), "no chop");
        });
    }

    // Logs to 1000,1010 holding 50 logs (in view, in no record), a pile of 40 at 1000,1004, the player at 1000,1000.
    static void ResetCollects()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s);
            List<int[]> walks = new();
            Ground ground = Piles(s, f, walks, new[] { 1000, 1004, 40 });
            ground.Piles = ground.Piles.Set(PileB + 8, new[] { 1000, 1010, 50 });
            s.ServerSends(Lying(PileB + 8, LogArt, 50, 1000, 1010));
            s.Clock.Sleep(1);
            List<Move> moves = Shard(s, ground);
            f.LumberLogsTo = "ground|0|1000|1010|0";
            f.LumberAtFeet = true;
            string logsTo;
            try
            {
                Check.Null(f.LumberRecollectHere(), "LumberRecollectHere");
                logsTo = f.LumberLogsTo;
                Check.False(f.CircuitRunning, "nothing started");
                Check.Equal(0, moves.Count, "nothing moved at the press");
                Check.Equal(90, f.LumberDropped(), "the old place's 50 and the 40 in the record");
                Check.Null(f.CollectNow(), "CollectNow");
                for (int i = 0; i < 2000 && f.CircuitRunning; i++)
                    s.Clock.Sleep(50);
            }
            finally
            {
                f.LumberAtFeet = false;
                f.LumberLogsTo = "";
                f.LumberPilesClear();
            }
            Check.Equal("ground|0|1000|1000|0", logsTo, "Logs to the player's tile");
            Check.True(f.CircuitStatus.Contains("collected"), "end: " + f.CircuitStatus);
            Check.Equal(0, f.LumberPiles().Count, "the record empty");
            int here = 0;
            foreach (int[] g in ground.Piles.Values)
            {
                if (g[0] == 1000 && g[1] == 1000)
                    here += g[2];
            }
            Check.Equal(90, here, "the 50 and the 40 on the player's tile");
            Within(moves);
        });
    }

    static void ResetOldOutOfView()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s);
            Ground ground = Piles(s, f, new List<int[]>());
            List<Move> moves = Shard(s, ground);
            f.LumberLogsTo = "ground|0|1000|1010|0";
            int piles;
            try
            {
                Check.Null(f.LumberRecollectHere(), "LumberRecollectHere");
                piles = f.LumberPiles().Count;
            }
            finally
            {
                f.LumberLogsTo = "";
                f.LumberPilesClear();
            }
            Check.Equal(0, piles, "an old tile out of view adds no pile");
            Check.False(f.CircuitRunning, "nothing started");
            Check.Equal(0, moves.Count, "nothing lifted");
        });
    }

    static void ResetAddsNothing()
    {
        foreach (string old in new[] { "ground|0|1000|1000|0", "item|" + Bag.ToString("X") + "|1000|1010|0", "" })
        {
            RecallTests.WithFeatures((s, f) =>
            {
                Kwik(s);
                Ground ground = Piles(s, f, new List<int[]>(), new[] { 1000, 1000, 30 });
                List<Move> moves = Shard(s, ground);
                f.LumberLogsTo = old;
                string? why;
                string logsTo;
                try
                {
                    why = f.LumberRecollectHere();
                    logsTo = f.LumberLogsTo;
                }
                finally
                {
                    f.LumberLogsTo = "";
                    f.LumberPilesClear();
                }
                Check.Null(why, "old " + old);
                Check.False(f.CircuitRunning, "nothing started, old " + old);
                Check.Equal("ground|0|1000|1000|0", logsTo, "Logs to the player's tile, old " + old);
                Check.True(f.Notes.Exists(n =>
                    n.Contains("the collection point reset to 1000,1000" + (old.Length == 0 ? "" : ", from "))),
                    "the note, old " + old);
                Check.Equal(0, moves.Count, "nothing moved, old " + old);
            });
        }
    }

    static void ResetClosed()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s);
            f.LumberLogsTo = "ground|0|1000|1010|0";
            try
            {
                s.CloseServer();
                for (int i = 0; i < 200 && !s.Link.Closed; i++)
                    s.Clock.Sleep(50);
                Check.Equal("not connected", f.LumberRecollectHere(), "LumberRecollectHere");
                Check.Equal("ground|0|1000|1010|0", f.LumberLogsTo, "Logs to unchanged");
            }
            finally
            {
                f.LumberLogsTo = "";
            }
        });
    }

    static void CollectNowDuring()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s);
            s.ServerSends(Packets.Contained(Axe, AxeArt, 1, Pack));
            s.ServerSends(Packets.Status(51));
            s.Clock.Sleep(1);
            List<int[]> walks = new();
            Ground ground = Piles(s, f, walks, new[] { 1000, 1004, 40 });
            List<Move> moves = Shard(s, ground, chop: x => "You put some logs into your backpack.");
            f.LumberAxe = Axe;
            f.LumberLogsTo = "ground|0|1000|1010|0";
            f.LumberAtFeet = true;
            f.LumberStatics = (x, y) => x == 1001 && y == 1010 ? new List<int[]> { new[] { TreeArt, 0 } }
                : new List<int[]>();
            Sentinel(f);
            try
            {
                Check.Null(f.CircuitLumber(), "CircuitLumber");
                Check.Null(f.CollectNow(), "CollectNow");
                for (int i = 0; i < 1000 && f.CircuitRunning && Sent(s, 0x6C) == 0; i++)
                    s.Clock.Sleep(200);
                f.CircuitHalt("test done");
                for (int i = 0; i < 2000 && f.CircuitRunning; i++)
                    s.Clock.Sleep(50);
                s.Clock.Sleep(600);
            }
            finally
            {
                f.LumberAtFeet = false;
                f.LumberLogsTo = "";
                f.LumberEmptyClear();
            }
            Check.Equal(2, SentinelProbes(f), "the sentinel probed at the start and after the collection");
            Check.Equal(0, f.LumberPiles().Count, "collected under Collect at's 1000");
            Check.True(Sent(s, 0x6C) >= 1, "then chopped: " + f.CircuitStatus);
        });
    }

    // A sentinel outside the oval (an earlier grove's), so each probe is a note and no chop.
    static void Sentinel(AssistFeatures f)
    {
        f.Cfg.Set(f.Server + "lumber.empty", (((long)(1100 >> 3) << 32) | (uint)(1100 >> 3)) + ":100");
        f.LumberSentinel = "1100,1100,0," + TreeArt;
    }

    static int SentinelProbes(AssistFeatures f) => f.Notes.FindAll(n =>
        n.Contains("the sentinel at 1100,1100 is outside this oval")).Count;

    const uint Crate = 0x44AB0030;

    // A target cursor's answer from the client: kind 0 (an object), the cursor, the serial, its tile.
    static byte[] Targeted(uint cursor, uint serial, int x, int y)
    {
        byte[] p = new byte[19];
        p[0] = 0x6C;
        p[2] = (byte)(cursor >> 24);
        p[3] = (byte)(cursor >> 16);
        p[4] = (byte)(cursor >> 8);
        p[5] = (byte)cursor;
        p[7] = (byte)(serial >> 24);
        p[8] = (byte)(serial >> 16);
        p[9] = (byte)(serial >> 8);
        p[10] = (byte)serial;
        p[11] = (byte)(x >> 8);
        p[12] = (byte)x;
        p[13] = (byte)(y >> 8);
        p[14] = (byte)y;
        return p;
    }

    // The player at 1000,1000; the crate at 1000,1009 on the shard's ground; the test's route and walk.
    static Ground CrateGround(FakeShard s, AssistFeatures f, List<int[]> walks)
    {
        Kwik(s);
        Ground ground = Piles(s, f, walks);
        ground.Piles = ground.Piles.Set(Crate, new[] { 1000, 1009, 1 });
        s.ServerSends(Lying(Crate, 0x0E7E, 1, 1000, 1009));
        s.Clock.Sleep(1);
        return ground;
    }

    static string HopWait(FakeShard s, AssistFeatures f)
    {
        for (int i = 0; i < 400
            && (f.HopRunning
            || !f.HopStatus.StartsWith("Done") && !f.HopStatus.StartsWith("Stopped")
            && !f.HopStatus.StartsWith("Not")); i++)
            s.Clock.Sleep(50);
        return f.HopStatus;
    }

    static void HopToMe()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            List<int[]> walks = new();
            Ground ground = CrateGround(s, f, walks);
            List<Move> moves = Shard(s, ground);
            Check.Null(f.HopToMe(), "HopToMe");
            Check.True(f.HopStatus.StartsWith("Target the object on the ground to hop to 1000,1000"),
                "asks: " + f.HopStatus);
            s.ClientSends(Targeted(AssistFeatures.HopCursor, Crate, 1000, 1009));
            string end = HopWait(s, f);
            Check.True(end.StartsWith("Done: the ") && end.EndsWith(" is at 1000,1000"), "end: " + end);
            Check.Equal(1000 * 65536 + 1000, ground.Piles[Crate]![0] * 65536 + ground.Piles[Crate]![1], "the crate at the kept tile");
            Within(moves);
            foreach (Move m in moves.FindAll(m => m.Op == 8))
                Check.False(m.X == m.PlayerX && m.Y == m.PlayerY, "no drop on the tile stood on: " + m.X + "," + m.Y);
            Check.Equal(0, f.LumberPiles().Count, "the pile record untouched");
            Check.True(f.Notes.Exists(n => n.Contains("9 tiles planned in")), "the plan noted");
        });
    }

    // A location cursor's answer: kind 1, the cursor, no object, the tile.
    static byte[] TargetedTile(uint cursor, int x, int y)
    {
        byte[] p = Targeted(cursor, 0, x, y);
        p[1] = 1;
        return p;
    }

    static void HopTargetToTarget()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            List<int[]> walks = new();
            Ground ground = CrateGround(s, f, walks);
            List<Move> moves = Shard(s, ground);
            Check.Null(f.HopItemToTarget(), "HopItemToTarget");
            Check.Equal("Target the object on the ground to hop.", f.HopStatus, "asks the object");
            s.ClientSends(Targeted(AssistFeatures.HopItemCursor, Crate, 1000, 1009));
            s.Clock.Sleep(1);
            Check.True(f.HopStatus.StartsWith("Target where the "), "asks the place: " + f.HopStatus);
            byte[]? place = null;
            foreach (byte[] p in s.ClientGot)
            {
                if (p[0] == 0x6C)
                    place = p;
            }
            Check.True(place is not null && place[1] == 1 && U32(place, 2) == AssistFeatures.HopPlaceCursor,
                "a location cursor");
            s.ClientSends(TargetedTile(AssistFeatures.HopPlaceCursor, 1000, 1003));
            string end = HopWait(s, f);
            Check.True(end.StartsWith("Done: the ") && end.EndsWith(" is at 1000,1003"), "end: " + end);
            Check.Equal(1003, ground.Piles[Crate]![1], "the crate at the place targeted");
            Within(moves);
        });
    }

    // The place targeted after walking out of the crate's view (Damian, 2026-10-23: "bunny hop second target wont take
    // if its too far away"): hopped from the crate's kept tile, the server sending the crate again once the walk is
    // back in view; a crate that never comes back ends the hop naming it.
    static void HopTargetOutOfView()
    {
        foreach (bool back in new[] { true, false })
        {
            RecallTests.WithFeatures((s, f) =>
            {
                List<int[]> walks = new();
                Ground ground = CrateGround(s, f, walks);
                List<Move> moves = Shard(s, ground);
                Func<int, int, string>? walk = f.CollectWalk;
                f.CollectWalk = (x, y) =>
                {
                    string w = walk!(x, y);
                    if (back
                        && Math.Max(Math.Abs(x - ground.Piles[Crate]![0]), Math.Abs(y - ground.Piles[Crate]![1]))
                        <= AssistState.GroundReach)
                        s.ServerSends(Lying(Crate, 0x0E7E, 1, ground.Piles[Crate]![0], ground.Piles[Crate]![1]));
                    s.Clock.Sleep(1);
                    return w;
                };
                f.HopItemToTarget();
                s.ClientSends(Targeted(AssistFeatures.HopItemCursor, Crate, 1000, 1009));
                s.Clock.Sleep(1);
                s.ServerSends(Packets.Draw(1000, 1049, 0, 0));
                s.Clock.Sleep(1);
                bool seen;
                lock (s.Link.State.Gate)
                    seen = s.Link.State.Ground.ContainsKey(Crate);
                Check.False(seen, "40 tiles off, the crate is out of view");
                s.ClientSends(TargetedTile(AssistFeatures.HopPlaceCursor, 1000, 1003));
                string end = HopWait(s, f);
                if (back)
                {
                    Check.True(end.StartsWith("Done: the ") && end.EndsWith(" is at 1000,1003"), "end: " + end);
                    Check.Equal(1003, ground.Piles[Crate]![1], "the crate at the place targeted");
                }
                else
                {
                    Check.True(end.StartsWith("Stopped: the item ") && end.Contains(" is not at 1000,1009"),
                        "end: " + end);
                    Check.Equal(0, moves.Count, "nothing lifted");
                }
            });
        }
    }

    static void HopTargetRefused()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            List<int[]> walks = new();
            Ground ground = CrateGround(s, f, walks);
            List<Move> moves = Shard(s, ground);
            // The object cursor cancelled, then a ground tile in place of an object.
            f.HopItemToTarget();
            s.ClientSends(Targeted(AssistFeatures.HopItemCursor, 0, 0, 0));
            s.Clock.Sleep(1);
            Check.Equal("Not hopped: the cursor was cancelled.", f.HopStatus, "cancelled");
            f.HopItemToTarget();
            s.ClientSends(TargetedTile(AssistFeatures.HopItemCursor, 1000, 1009));
            s.Clock.Sleep(1);
            Check.Equal("Not hopped: target an object lying on the ground.", f.HopStatus, "a tile for the object");
            // The place cursor cancelled; a place answered with no object asked first.
            f.HopItemToTarget();
            s.ClientSends(Targeted(AssistFeatures.HopItemCursor, Crate, 1000, 1009));
            s.ClientSends(Targeted(AssistFeatures.HopPlaceCursor, 0, 0, 0));
            s.Clock.Sleep(1);
            Check.Equal("Not hopped: the cursor was cancelled.", f.HopStatus, "the place cancelled");
            s.ClientSends(TargetedTile(AssistFeatures.HopPlaceCursor, 1000, 1003));
            s.Clock.Sleep(1);
            Check.Equal("Not hopped: target the object first", f.HopStatus, "no object asked");
            // The object gone between the two cursors (the object's answer handled first: the delete raced it under the
            // coverage instrument, 2026-10-23).
            f.HopItemToTarget();
            s.ClientSends(Targeted(AssistFeatures.HopItemCursor, Crate, 1000, 1009));
            s.Clock.Sleep(1);
            s.ServerSends(Delete(Crate));
            s.Clock.Sleep(1);
            s.ClientSends(TargetedTile(AssistFeatures.HopPlaceCursor, 1000, 1003));
            s.Clock.Sleep(1);
            Check.Equal("Not hopped: the object is no longer on the ground", f.HopStatus, "gone");
            Check.Equal(0, moves.Count, "nothing lifted");
            Check.Equal("a circuit is running", f.CircuitLumber() == null ? f.HopItemToTarget() : "no circuit",
                "refused beside a circuit");
            f.CircuitHalt("test done");
            for (int i = 0; i < 2000 && f.CircuitRunning; i++)
                s.Clock.Sleep(50);
        });
    }

    static void HopRefused()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            List<int[]> walks = new();
            Ground ground = CrateGround(s, f, walks);
            List<Move> moves = Shard(s, ground);
            s.ClientSends(Targeted(AssistFeatures.HopCursor, Crate, 1000, 1009));
            s.Clock.Sleep(1);
            Check.Equal("Not hopped: press the button first", f.HopStatus, "no press first");
            f.HopToMe();
            s.ClientSends(Targeted(AssistFeatures.HopCursor, 0, 0, 0));
            s.Clock.Sleep(1);
            Check.Equal("Not hopped: the cursor was cancelled.", f.HopStatus, "a cancel");
            s.ServerSends(Packets.Contained(Bag, 0x0E76, 1, Pack));
            s.Clock.Sleep(1);
            f.HopToMe();
            s.ClientSends(Targeted(AssistFeatures.HopCursor, Bag, 0, 0));
            s.Clock.Sleep(1);
            Check.Equal("Not hopped: target an object lying on the ground.", f.HopStatus, "an item in the pack");
            Check.Equal(0, moves.Count, "nothing lifted");
        });
    }

    static void HopEnds()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            List<int[]> walks = new();
            Ground ground = CrateGround(s, f, walks);
            List<Move> moves = Shard(s, ground);
            // Already there: the player moved onto the crate's tile.
            s.ServerSends(Packets.Draw(1000, 1009, 0, 0));
            s.Clock.Sleep(1);
            f.HopToMe();
            s.ClientSends(Targeted(AssistFeatures.HopCursor, Crate, 1000, 1009));
            Check.True(HopWait(s, f).Contains("is already at 1000,1009"), "already: " + f.HopStatus);
            // No way: the route finds none.
            s.ServerSends(Packets.Draw(1000, 1000, 0, 0));
            s.Clock.Sleep(1);
            f.CollectRoute = (a, b, c, d) => null!;
            f.HopToMe();
            s.ClientSends(Targeted(AssistFeatures.HopCursor, Crate, 1000, 1009));
            Check.Equal("Not hopped: no way from 1000,1009 to 1000,1000", HopWait(s, f), "no way");
            // Gone on the way: taken from the ground after the plan, before the first lift.
            f.CollectRoute = (a, b, c,
                d) => { s.ServerSends(Delete(Crate)); s.Clock.Sleep(1); return Straight(a, b, c, d); };
            f.HopToMe();
            s.ClientSends(Targeted(AssistFeatures.HopCursor, Crate, 1000, 1009));
            Check.Equal("Stopped: the item " + Crate.ToString("X") + " is not at 1000,1009", HopWait(s, f), "gone");
            f.CollectRoute = Straight;
            Check.Equal(0, moves.Count, "nothing lifted in any of them");
        });
    }

    static void HopStops()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            List<int[]> walks = new();
            Ground ground = CrateGround(s, f, walks);
            List<Move> moves = Shard(s, ground);
            ManualResetEventSlim walking = new(false);
            ManualResetEventSlim release = new(false);
            f.CollectWalk = (x, y) => { walking.Set(); release.Wait(5000); return null!; };
            f.HopToMe();
            s.ClientSends(Targeted(AssistFeatures.HopCursor, Crate, 1000, 1009));
            Check.True(walking.Wait(5000), "walking to the first stand");
            Check.True(f.HopRunning, "running");
            Check.Equal("a bunny hop is running", f.CircuitLumber(), "no circuit beside it");
            Check.Equal("a bunny hop is running", f.HopToMe() == null ? "a bunny hop is running" : "not stopped",
                "the button again");
            Check.Equal("Stopping", f.HopStatus, "stopping");
            release.Set();
            Check.True(HopWait(s, f).StartsWith("Stopped: stopped"), "end: " + f.HopStatus);
            Check.Equal(0, moves.FindAll(m => m.Op == 7).Count, "nothing lifted after the stop");
        });
    }

    static void HopStopButton()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            List<int[]> walks = new();
            Ground ground = CrateGround(s, f, walks);
            List<Move> moves = Shard(s, ground);
            Check.Equal("no bunny hop is running", f.HopStop(), "none running");
            ManualResetEventSlim walking = new(false);
            ManualResetEventSlim release = new(false);
            f.CollectWalk = (x, y) => { walking.Set(); release.Wait(5000); return null!; };
            f.HopToMe();
            s.ClientSends(Targeted(AssistFeatures.HopCursor, Crate, 1000, 1009));
            Check.True(walking.Wait(5000), "walking to the first stand");
            Check.Null(f.HopStop(), "HopStop");
            Check.Equal("Stopping", f.HopStatus, "stopping");
            release.Set();
            Check.True(HopWait(s, f).StartsWith("Stopped: stopped"), "end: " + f.HopStatus);
            Check.False(f.HopRunning, "ended");
            Check.Equal(0, moves.FindAll(m => m.Op == 7).Count, "nothing lifted after the stop");
        });
    }

    static void HopRefusedMore()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            List<int[]> walks = new();
            Ground ground = CrateGround(s, f, walks);
            List<Move> moves = Shard(s, ground);
            // A ground target (kind 1, no object).
            f.HopToMe();
            byte[] tile = Targeted(AssistFeatures.HopCursor, 0, 1000, 1005);
            tile[1] = 1;
            s.ClientSends(tile);
            s.Clock.Sleep(1);
            Check.Equal("Not hopped: target an object lying on the ground.", f.HopStatus, "a ground target");
            // The client map's own route: with no map in the suites, no way.
            f.CollectRoute = null;
            f.HopToMe();
            s.ClientSends(Targeted(AssistFeatures.HopCursor, Crate, 1000, 1009));
            Check.Equal("Not hopped: no way from 1000,1009 to 1000,1000", HopWait(s, f), "the real planner, no map");
            f.CollectRoute = Straight;
            // A second target while a hop runs.
            ManualResetEventSlim walking = new(false);
            ManualResetEventSlim release = new(false);
            f.CollectWalk = (x, y) => { walking.Set(); release.Wait(); return "test walk refused"; };
            f.HopToMe();
            s.ClientSends(Targeted(AssistFeatures.HopCursor, Crate, 1000, 1009));
            Check.True(walking.Wait(5000), "walking");
            s.ClientSends(Targeted(AssistFeatures.HopCursor, Crate, 1000, 1009));
            // Read in real time while the walk is held: the virtual clock's sleep would wait on the held thread.
            for (int i = 0; i < 200 && f.HopStatus != "Not hopped: something else is moving you"; i++)
                Thread.Sleep(10);
            string second = f.HopStatus;
            release.Set();
            Check.Equal("Not hopped: something else is moving you", second, "a second target");
            HopWait(s, f);
            Check.True(f.HopStatus.StartsWith("Stopped: walking to "), "the walk's refusal ends it: " + f.HopStatus);
            Check.Equal(0, moves.Count, "nothing lifted");
        });
        // A circuit running refuses the press.
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s);
            s.ServerSends(Packets.Contained(Axe, AxeArt, 1, Pack));
            s.ServerSends(Packets.Status(51));
            s.Clock.Sleep(1);
            List<int[]> walks = new();
            Ground ground = Piles(s, f, walks);
            Shard(s, ground, chop: x => "You put some logs into your backpack.");
            f.LumberAxe = Axe;
            f.LumberLogsTo = "ground|0|1000|1010|0";
            f.LumberStatics = (x, y) => x == 1001 && y == 1000 ? new List<int[]> { new[] { TreeArt, 0 } }
                : new List<int[]>();
            try
            {
                Check.Null(f.CircuitLumber(), "CircuitLumber");
                for (int i = 0; i < 400 && Sent(s, 0x6C) == 0; i++)
                    s.Clock.Sleep(50);
                Check.True(f.CircuitRunning, "chopping");
                Check.Equal("a circuit is running", f.HopToMe(), "refused");
            }
            finally
            {
                f.CircuitHalt("test done");
                for (int i = 0; i < 200 && f.CircuitRunning; i++)
                    s.Clock.Sleep(50);
                s.Clock.Sleep(600);
                f.LumberLogsTo = "";
                f.LumberEmptyClear();
            }
        });
    }

    static void NewChopClears()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            s.ServerSends(Packets.Contents(Pack));
            Kwik(s);
            s.ServerSends(Packets.Contained(Axe, AxeArt, 1, Pack));
            s.Clock.Sleep(1);
            f.LumberAxe = Axe;
            f.LumberLogsTo = "feet|0";
            f.LumberPilesClear();
            f.LumberPileAdd(1000, 1004, 40);
            f.LumberPileAdd(1003, 1004, 20);
            f.LumberSentinel = "1100,1100,0," + TreeArt;
            f.Cfg.Set(f.Server + "lumber.empty", (((long)(1100 >> 3) << 32) | (uint)(1100 >> 3)) + ":100");
            f.LumberFrom = "1200,1200";
            f.LumberRadius = 25;
            f.LumberCollectAt = 300;
            try
            {
                Check.Null(f.CircuitLumberNew(), "CircuitLumberNew");
                Check.Equal(0, f.LumberPiles().Count, "the piles forgotten");
                Check.Equal("Sentinel: the next tree chopped (a new chop)", f.LumberSentinelText(),
                    "the old sentinel forgotten, the next tree the new one");
                Check.Equal(0, f.LumberEmpties().Count, "the chunks found empty forgotten");
                Check.Equal("", f.LumberFrom, "Chop from forgotten");
                Check.Equal("ground|0|1000|1000|0", f.LumberLogsTo, "the collection point the player's tile");
                Check.Equal(Axe, f.LumberAxe, "the axe kept");
                Check.True(f.LumberAtFeet, "At my feet kept");
                Check.Equal(25, f.LumberRadius, "Range kept");
                Check.Equal(300, f.LumberCollectAt, "Collect at kept");
                Check.True(f.Notes.Exists(n =>
                    n.Contains("; 1 empty chunks forgotten; the collection point is 1000,1000; the first tree chopped "
                    + "is the sentinel")), "noted");
                f.CircuitHalt("test done");
                for (int i = 0; i < 2000 && f.CircuitRunning; i++)
                    s.Clock.Sleep(50);
                Check.True(!f.CircuitRunning, "ended: " + f.CircuitStatus);
            }
            finally
            {
                f.LumberLogsTo = "";
                f.LumberAtFeet = false;
                f.LumberRadius = 40;
                f.LumberCollectAt = 1000;
                f.LumberPilesClear();
                f.LumberEmptyClear();
            }
            Check.Equal("Lumberjacking", f.CircuitName, "the lumber circuit ran");
        });
    }

    static void NewChopOldDrops()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            s.ServerSends(Packets.Contents(Pack));
            Kwik(s);
            s.ServerSends(Packets.Contained(Axe, AxeArt, 1, Pack));
            s.ServerSends(Lying(PileB + 8, LogArt, 70, 1000, 1003));
            s.Clock.Sleep(1);
            f.LumberAxe = Axe;
            f.LumberAtFeet = true;
            int found;
            string kept;
            try
            {
                Check.Null(f.CircuitLumberNew(), "CircuitLumberNew");
                f.CircuitHalt("test done");
                for (int i = 0; i < 2000 && f.CircuitRunning; i++)
                    s.Clock.Sleep(50);
                found = f.LumberDiscover(s.Link);
                kept = f.Cfg.Get(f.Server + "lumber.old", "");
            }
            finally
            {
                f.LumberAtFeet = false;
                f.LumberLogsTo = "";
                f.LumberSentinel = "";
                f.LumberPilesClear();
                f.Cfg.Set(f.Server + "lumber.old", "");
            }
            Check.Equal(0, found, "the old drop not found as wild");
            Check.True(kept.Contains((PileB + 8).ToString("X")), "kept in the settings: " + kept);
            Check.True(f.Notes.Exists(n => n.Contains("1 log piles in view are old drops")), "noted");
        });
    }

    static void SeenMarked()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            string Map() => string.Join(" ",
                f.LumberMapPiles().ConvertAll(t => t[0] + "," + t[1] + ":" + t[2] + (t[3] == 1 ? "w" : "")));
            string Sorted()
            {
                List<string> l = f.LumberMapPiles().ConvertAll(t => t[0] + "," + t[1] + ":" + t[2]
                    + (t[3] == 1 ? "w" : ""));
                l.Sort(string.CompareOrdinal);
                return string.Join(" ", l);
            }
            f.Cfg.Set(f.Server + "res.seen",
                "1500,1500,0,9," + s.Clock.UtcNow.Ticks + ",18;bad;1501,1500,9,1,0,2;1002,1002,0,3,"
                + s.Clock.UtcNow.Ticks + ",6");
            try
            {
                Kwik(s);
                f.LumberPilesClear();
                f.LumberPileAdd(1000, 1004, 40);
                s.ServerSends(Lying(PileA, LogArt, 40, 1000, 1004));
                s.ServerSends(Lying(PileB, LogArt, 25, 1003, 1000));
                s.ServerSends(Lying(PileB + 1, LogArt, 5, 1003, 1000));
                s.ServerSends(Lying(PileB + 2, LogArt, 7, 1000, 1020));
                s.Clock.Sleep(1);
                Check.Equal("1000,1004:40 1000,1020:7w 1002,1002:3w 1003,1000:30w 1500,1500:9w", Sorted(),
                    "the record's, the saved ones, the two stacks on one tile as one, the one past view");
                Check.True(f.Cfg.Get(f.Server + "res.seen", "").Contains("1003,1000,0,30,"), "saved");
                s.ServerSends(Delete(PileB));
                s.Clock.Sleep(1);
                Check.Equal("1000,1004:40 1000,1020:7w 1002,1002:3w 1003,1000:5w 1500,1500:9w", Sorted(),
                    "one stack of two deleted: the tile keeps the other");
                s.ServerSends(Delete(PileB + 1));
                s.ServerSends(Delete(PileB + 2));
                s.Clock.Sleep(1);
                Check.Equal("1000,1004:40 1000,1020:7w 1002,1002:3w 1500,1500:9w", Sorted(),
                    "the last stack deleted in view: gone at once; deleted past view: kept");
                s.Clock.Sleep(AssistFeatures.SeenGoneMs / 2);
                Check.True(Map().Contains("1002,1002"), "a saved one seen empty, still within the 10 s");
                s.Clock.Sleep(AssistFeatures.SeenGoneMs);
                Check.Equal("1000,1004:40 1000,1020:7w 1500,1500:9w", Sorted(),
                    "gone after 10 s seen empty; the far ones kept");
            }
            finally
            {
                f.Cfg.Set(f.Server + "res.seen", "");
                f.LumberPilesClear();
            }
        });
    }

    // 0x1A with no amount (the top bit of the serial clear): length, serial, graphic, x, y, z.
    static byte[] LyingOne(uint serial, int graphic, int x, int y) =>
        new byte[] { 0x1A, 0, 14, (byte)(serial >> 24), (byte)(serial >> 16), (byte)(serial >> 8), (byte)serial,
        (byte)(graphic >> 8), (byte)graphic, (byte)(x >> 8), (byte)x, (byte)(y >> 8), (byte)y, 0 };

    // uo1998 2026-10-24 11:28:39: a chop's logs lie on the ground 120 ms, sent once with no amount and once with 10,
    // each deleted; a sweep in each window sees them, and each delete takes the mark.
    static void ChopFlicker()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            string Map() => string.Join(" ",
                f.LumberMapPiles().ConvertAll(t => t[0] + "," + t[1] + ":" + t[2] + (t[3] == 1 ? "w" : "")));
            f.Cfg.Set(f.Server + "res.seen", "");
            try
            {
                Kwik(s);
                f.LumberPilesClear();
                s.ServerSends(LyingOne(PileA, LogArt, 1001, 1000));
                s.Clock.Sleep(1);
                Check.Equal("1001,1000:1w", Map(), "the sweep inside the first window");
                s.ServerSends(Delete(PileA));
                s.Clock.Sleep(1);
                Check.Equal("", Map(), "the delete takes it");
                s.ServerSends(Lying(PileA, LogArt, 10, 1001, 1000));
                s.Clock.Sleep(1);
                Check.Equal("1001,1000:10w", Map(), "the sweep inside the second");
                s.ServerSends(Delete(PileA));
                s.Clock.Sleep(1);
                Check.Equal("", Map(), "no wild mark left");
            }
            finally
            {
                f.Cfg.Set(f.Server + "res.seen", "");
                f.LumberPilesClear();
            }
        });
    }

    // Kwik at 1000,1000. A delete with no ground item gone, a shirt's on a log's tile, and the pack's after a ground
    // delete each leave the log marks; a log's beside ore takes the log's mark at once and leaves the ore's.
    static void DeleteEdges()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            string Map() => string.Join(" ",
                f.LumberMapPiles().ConvertAll(t => t[0] + "," + t[1] + ":" + t[2] + (t[3] == 1 ? "w" : "")));
            string Ore() => string.Join(" ",
                f.ResourceSpots().FindAll(t => t[2] == 1).ConvertAll(t => t[0] + "," + t[1] + ":" + t[3]));
            f.Cfg.Set(f.Server + "res.seen", "");
            try
            {
                Kwik(s);
                f.LumberPilesClear();
                s.ServerSends(Delete(PileB + 3));
                s.ServerSends(Lying(PileA, LogArt, 5, 1001, 1000));
                s.ServerSends(Lying(PileB, 0x1517, 1, 1001, 1000));
                s.Clock.Sleep(1);
                Check.Equal("1001,1000:5w", Map(), "a delete before any ground item: nothing");
                s.ServerSends(Delete(PileB));
                s.Clock.Sleep(1);
                Check.Equal("1001,1000:5w", Map(), "the shirt deleted: the log mark stays");
                s.ServerSends(Delete(Pack));
                s.Clock.Sleep(1);
                Check.Equal("1001,1000:5w", Map(), "the pack deleted after a ground delete: the log mark stays");
                s.ServerSends(Lying(PileB + 1, LogArt, 4, 1002, 1002));
                s.ServerSends(Lying(PileB + 2, 0x19B9, 3, 1002, 1002));
                s.Clock.Sleep(1);
                Check.Equal("1001,1000:5w 1002,1002:4w", Map(), "the log beside ore marked");
                Check.Equal("1002,1002:3", Ore(), "the ore marked");
                s.ServerSends(Delete(PileB + 1));
                s.Clock.Sleep(1);
                Check.Equal("1001,1000:5w", Map(), "the log deleted beside ore: its mark gone at once");
                Check.Equal("1002,1002:3", Ore(), "the ore's mark kept");
            }
            finally
            {
                f.Cfg.Set(f.Server + "res.seen", "");
                f.LumberPilesClear();
            }
        });
    }

    // Kwik at 1000,1000 with 30 and 20 logs, at feet; a wild sighting saved on 1000,1001, the first tile beside. The
    // shard puts back the drop there, takes one on 1001,1000, and merges the second stack onto it, where the map is
    // read.
    static void FeetStoreOurs()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            string Map() => string.Join(" ",
                f.LumberMapPiles().ConvertAll(t => t[0] + "," + t[1] + ":" + t[2] + (t[3] == 1 ? "w" : "")));
            // Beside it: logs on the store's column and on its row, and ore on its column.
            long at = s.Clock.UtcNow.Ticks;
            f.Cfg.Set(f.Server + "res.seen",
                "1000,1001,0,1," + at + ",2;1001,1005,0,1," + at + ",2;1003,1000,0,1," + at + ",2;1001,1002,1,1," + at
                + ",12");
            string mid = "";
            try
            {
                Kwik(s);
                f.LumberPilesClear();
                s.ServerSends(Packets.Contained(Logs, LogArt, 30, Pack));
                s.ServerSends(Packets.Contained(Logs2, LogArt, 20, Pack));
                s.Clock.Sleep(1);
                Check.Equal("1000,1001:1w 1001,1005:1w 1003,1000:1w", Map(), "the old sightings before the store");
                s.Answer = p =>
                {
                    if (p[0] != 0x08)
                        return ImmutableArray<byte[]>.Empty;
                    uint item = U32(p, 1);
                    if (U32(p, 10) == Logs)
                    {
                        mid = Map();
                        return ImmutableArray.Create(Delete(item), Lying(Logs, LogArt, 50, 1001, 1000));
                    }
                    if (U16(p, 5) == 1001 && U16(p, 7) == 1000)
                        return ImmutableArray.Create<byte[]>(Lying(item, LogArt, 30, 1001, 1000));
                    return ImmutableArray<byte[]>.Empty;
                };
                f.LumberAtFeet = true;
                int answer = f.Timings.Ms("answer");
                f.Timings.Set("answer", 300);
                try
                {
                    Check.Null(f.LumberStore(s.Link), "LumberStore");
                }
                finally
                {
                    f.Timings.Set("answer", answer);
                    f.LumberAtFeet = false;
                }
                Check.Equal("1001,1000:30 1001,1005:1w 1003,1000:1w", mid,
                    "during the store: the first stack's pile ours, the put-back tile's sighting gone, the others wild");
                Check.Equal("1001,1000:50 1001,1005:1w 1003,1000:1w", Map(),
                    "after it: the record's pile, the others wild");
            }
            finally
            {
                f.Cfg.Set(f.Server + "res.seen", "");
                f.LumberPilesClear();
            }
        });
    }

    // At feet, the lift refused: the store ends naming it, records nothing, and the sighting beside stays wild.
    static void FeetLiftRefused()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            string Map() => string.Join(" ",
                f.LumberMapPiles().ConvertAll(t => t[0] + "," + t[1] + ":" + t[2] + (t[3] == 1 ? "w" : "")));
            f.Cfg.Set(f.Server + "res.seen", "1000,1001,0,1," + s.Clock.UtcNow.Ticks + ",2");
            try
            {
                Kwik(s);
                f.LumberPilesClear();
                s.ServerSends(Packets.Contained(Logs, LogArt, 30, Pack));
                s.Clock.Sleep(1);
                s.Answer = p => p[0] == 0x07 ? ImmutableArray.Create<byte[]>(new byte[] { 0x27, 0x00 })
                    : ImmutableArray<byte[]>.Empty;
                f.LumberAtFeet = true;
                string? why;
                try
                {
                    why = f.LumberStore(s.Link);
                }
                finally
                {
                    f.LumberAtFeet = false;
                }
                Check.True(why is not null && !why.Contains(Hands.PutBack), "refused, not put back: " + why);
                Check.Equal(0, f.LumberPiles().Count, "nothing recorded");
                Check.Equal("1000,1001:1w", Map(), "the sighting beside still wild");
            }
            finally
            {
                f.Cfg.Set(f.Server + "res.seen", "");
                f.LumberPilesClear();
            }
        });
    }

    // Not at feet: a drop on Logs to put back ends the store, and a sighting on that tile is left to the view.
    static void LogsToPutBack()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            f.Cfg.Set(f.Server + "res.seen", "1002,1000,0,1," + s.Clock.UtcNow.Ticks + ",2");
            try
            {
                Kwik(s);
                f.LumberPilesClear();
                s.ServerSends(Packets.Contained(Logs, LogArt, 30, Pack));
                s.Clock.Sleep(1);
                s.Answer = p => ImmutableArray<byte[]>.Empty;
                f.LumberLogsTo = "ground|0|1002|1000|0";
                int answer = f.Timings.Ms("answer");
                f.Timings.Set("answer", 300);
                string? why;
                try
                {
                    why = f.LumberStore(s.Link);
                }
                finally
                {
                    f.Timings.Set("answer", answer);
                    f.LumberLogsTo = "";
                }
                Check.True(why is not null && why.Contains(Hands.PutBack), "put back: " + why);
                Check.True(f.Cfg.Get(f.Server + "res.seen", "").StartsWith("1002,1000,0,1,"), "the sighting kept");
                Check.Equal(0, f.LumberPiles().Count, "nothing recorded");
            }
            finally
            {
                f.Cfg.Set(f.Server + "res.seen", "");
                f.LumberPilesClear();
            }
        });
    }

    static void HopLeavesNoMark()
    {
        string marks = "";
        Collect((s, f, g, w) =>
        {
            f.Cfg.Set(f.Server + "res.seen", "");
            // Seen where it lies, then hopped 6 tiles to the place (no weight known: hopped, not carried).
            f.LumberMapPiles();
            Check.Null(f.LumberCollect(s.Link), "LumberCollect");
            marks = string.Join(" ",
                f.LumberMapPiles().ConvertAll(t => t[0] + "," + t[1] + ":" + t[2] + (t[3] == 1 ? "w" : "")));
            f.Cfg.Set(f.Server + "res.seen", "");
        }, out List<Move> moves, new[] { 1000, 1004, 40 });
        Check.False(marks.Contains("1000,1004"), "no mark where it lay: " + marks);
        Check.False(marks.Contains("w"), "nothing wild: " + marks);
    }

    static void DecorKept()
    {
        string file = Path.Combine(Path.GetTempPath(), "assist-decor-" + Environment.ProcessId + ".txt");
        File.Delete(file);
        const uint House = 0x40000077;
        RecallTests.WithFeatures((s, f) =>
        {
            f.DecorPath = file;
            Kwik(s);
            // A small house multi (0x4064) at 1004,1004, and a log pile anyone can lift.
            s.ServerSends(Lying(House, 0x4064, 1, 1004, 1004));
            s.ServerSends(Lying(PileA, LogArt, 10, 1002, 1002));
            s.Clock.Sleep(1);
            f.DecorSweep(s.Link);
            List<int[]> items = f.DecorItems();
            Check.Equal(1, items.Count, "the multi only");
            Check.Equal(0x4064, items[0][1], "its graphic");
            s.Clock.Sleep(AssistFeatures.DecorSaveMs + 1000);
            f.DecorSweep(s.Link);
            Check.True(File.Exists(file) && File.ReadAllText(file).Contains("4064,1004,1004"), "saved");
        });
        RecallTests.WithFeatures((s, f) =>
        {
            f.DecorPath = file;
            Check.Equal(1, f.DecorItems().Count, "read back");
            Kwik(s);
            f.DecorSweep(s.Link);
            s.Clock.Sleep(AssistFeatures.SeenGoneMs + 1000);
            f.DecorSweep(s.Link);
            Check.Equal(0, f.DecorItems().Count, "its tile seen without it for 10 s: gone");
        });
        File.Delete(file);
    }

    // The test map's wall (TestMap.Wall) weighs 255, so a wall item lying on the ground is fixed decoration.
    static void DecorInBox()
    {
        string file = Path.Combine(Path.GetTempPath(), "assist-decor-box-" + Environment.ProcessId + ".txt");
        File.Delete(file);
        new TestMap().Run(() => RecallTests.WithFeatures((s, f) =>
        {
            f.DecorPath = file;
            Check.Equal(0, f.DecorIn(0, 0, 4000, 4000).Count, "an empty catalogue fills nothing");
            Kwik(s);
            s.ServerSends(Lying(0x40000071, TestMap.Wall, 1, 1002, 1003));
            s.ServerSends(Lying(0x40000072, TestMap.Wall, 1, 1030, 1003));
            s.ServerSends(Lying(0x40000073, 0x4064, 1, 1004, 1004));
            s.ServerSends(Lying(PileA, LogArt, 10, 1001, 1001));
            s.Clock.Sleep(1);
            int was = f.DecorSeq;
            f.DecorSweep(s.Link);
            Check.Equal(3, f.DecorItems().Count, "both walls and the multi catalogued");
            Check.True(f.DecorSeq != was, "the sequence moved");
            List<int[]> box = f.DecorIn(990, 990, 1010, 1010);
            Check.Equal(1, box.Count, "the wall in the box only");
            Check.Equal(TestMap.Wall + ",1002,1003,0", string.Join(",", box[0]), "graphic, x, y, z");
            Check.Equal(0, f.DecorIn(1003, 990, 1010, 1010).Count, "a box past its x");
            Check.Equal(0, f.DecorIn(990, 1004, 1010, 1010).Count, "a box past its y");
            Check.Equal(0, f.DecorIn(990, 990, 1001, 1010).Count, "a box short of its x");
            Check.Equal(0, f.DecorIn(990, 990, 1010, 1002).Count, "a box short of its y");
            Check.Equal(1, f.DecorIn(1002, 1003, 1002, 1003).Count, "a box of its own tile");
            was = f.DecorSeq;
            f.DecorSweep(s.Link);
            Check.Equal(was, f.DecorSeq, "nothing changed, the sequence stands");
            // Its tile found inside a house: it leaves the catalogue and the view; seen again there, nothing moves. A
            // door in the same house is kept (Damian, 2026-10-23: "you can put the door on their house though").
            f.LumberHoused = (x, y) => y == 1003 && (x == 1002 || x == 1005);
            s.ServerSends(Lying(0x40000074, TestMap.Door, 1, 1005, 1003));
            s.Clock.Sleep(1);
            f.DecorSweep(s.Link);
            Check.True(f.DecorSeq != was, "the sequence moved");
            box = f.DecorIn(990, 990, 1010, 1010);
            Check.Equal(1, box.Count, "housed, the wall is not drawn and the door is");
            Check.Equal(TestMap.Door + ",1005,1003,0", string.Join(",", box[0]), "the door");
            was = f.DecorSeq;
            f.DecorSweep(s.Link);
            Check.Equal(was, f.DecorSeq, "housed again, the sequence stands");
            f.LumberHoused = AssistFeatures.HousedOnMap;
        }));
        File.Delete(file);
    }

    // Nightshade (0x0F88) lying at 1002,1000 and 1003,1000; the shard answers a lift as uo1998 did (relay log
    // 2026-10-23 17:52:49: the item's 0x1D) and a drop into the pack with the item there, but refuses the lift of the
    // second the second time (0x27).
    static void LiftedUnmarked()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            const uint Shade = 0x47061CCB, Shade2 = 0x47061CCC;
            string Res() => string.Join(" ", f.ResourceSpots().ConvertAll(t => t[0] + "," + t[1]));
            f.Cfg.Set(f.Server + "res.seen", "");
            try
            {
                Kwik(s);
                bool refuse = false;
                s.Answer = p =>
                {
                    if (p[0] == 0x07)
                        return refuse ? ImmutableArray.Create<byte[]>(new byte[] { 0x27, 0x00 })
                            : ImmutableArray.Create<byte[]>(Delete(U32(p, 1)));
                    if (p[0] == 0x08)
                        return ImmutableArray.Create<byte[]>(Packets.Contained(U32(p, 1), 0x0F88, 1, Pack));
                    return ImmutableArray<byte[]>.Empty;
                };
                s.ServerSends(Lying(Shade, 0x0F88, 1, 1002, 1000));
                s.ServerSends(Lying(Shade2, 0x0F88, 1, 1003, 1000));
                s.Clock.Sleep(1);
                Check.Equal("1002,1000 1003,1000", Res(), "both marked");
                s.ClientSends(Packets.Lift(Shade, 1));
                s.Clock.Sleep(100);
                Check.Equal("1003,1000", Res(), "lifted by hand: its mark gone at once");
                s.ClientSends(Packets.Lift(0x40000099, 1));
                s.Clock.Sleep(100);
                Check.Equal("1003,1000", Res(), "a lift of what lies on no ground: nothing forgotten");
                refuse = true;
                s.ClientSends(Packets.Lift(Shade2, 1));
                s.Clock.Sleep(100);
                Check.Equal("1003,1000", Res(), "a refused lift: still there, still marked");
                refuse = false;
                AssistItem it;
                lock (s.Link.State.Gate)
                    it = s.Link.State.Ground[Shade2]!;
                f.Gather(s.Link, it, Pack, "nightshade");
                Check.Equal("", Res(), "the harvest's lift: gone");
            }
            finally
            {
                f.Cfg.Set(f.Server + "res.seen", "");
            }
        });
    }

    // Ore, feathers, hides and a reagent in view; a pile in a house; then 3 hours: the light piles decay, a pile of 255
    // stones or more does not.
    static void ResourcesDecay()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            string Res() => string.Join(" ",
                f.ResourceSpots().ConvertAll(t => t[0] + "," + t[1] + ":" + AssistFeatures.ResKinds[t[2]] + t[3]));
            string Logs() => string.Join(" ", f.LumberMapPiles().ConvertAll(t => t[0] + "," + t[1] + ":" + t[2]));
            f.Cfg.Set(f.Server + "res.seen", "");
            f.LumberHoused = (x, y) => x == 1006;
            try
            {
                Kwik(s);
                f.LumberPilesClear();
                s.ServerSends(Lying(PileA, 0x19B9, 3, 1001, 1001));
                s.ServerSends(Lying(PileA + 1, 0x1BD1, 20, 1002, 1002));
                s.ServerSends(Lying(PileA + 2, 0x1078, 4, 1003, 1003));
                s.ServerSends(Lying(PileA + 3, 0x0F7A, 7, 1004, 1004));
                s.ServerSends(Lying(PileA + 4, LogArt, 200, 1005, 1005));
                s.ServerSends(Lying(PileA + 5, LogArt, 10, 1005, 1006));
                s.ServerSends(Lying(PileA + 6, 0x19B9, 5, 1006, 1001));
                s.ServerSends(Lying(PileA + 7, 0x0E21, 5, 1007, 1001));
                s.Clock.Sleep(1);
                Check.Equal("1001,1001:Ore3 1002,1002:Feathers20 1003,1003:Hides4 1004,1004:Reagents7", Res(),
                    "each kind, not the housed ore, not bandages");
                Check.Equal("1005,1005:200 1005,1006:10", Logs(), "the logs");
                s.Clock.Sleep((int)(AssistFeatures.DecayMs / 2));
                s.ServerSends(Lying(PileA + 5, LogArt, 12, 1005, 1006));
                s.Clock.Sleep(1);
                Logs();
                // Away, out of view: the client forgets the piles.
                s.ServerSends(Packets.Draw(1100, 1100, 0, 0));
                s.Clock.Sleep(1);
                Check.Equal("1001,1001:Ore3 1002,1002:Feathers20 1003,1003:Hides4 1004,1004:Reagents7", Res(),
                    "out of view: kept");
                s.Clock.Sleep((int)(AssistFeatures.DecayMs / 2) + 1000);
                Check.Equal("1005,1005:200 1005,1006:12", Logs(),
                    "400 stones never decays; the 12 seen again at a new amount counts from then");
                Check.Equal("", Res(), "the light piles decayed after 3 hours");
                s.Clock.Sleep((int)(AssistFeatures.DecayMs / 2));
                Check.Equal("1005,1005:200", Logs(), "the 12 too, 3 hours after its new amount");
            }
            finally
            {
                f.Cfg.Set(f.Server + "res.seen", "");
                f.LumberHoused = AssistFeatures.HousedOnMap;
            }
        });
    }

    static void SpotsMarked()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            string Spots() => string.Join(" ", f.LumberSpots().ConvertAll(t => t[0] + "," + t[1] + ":" + t[2]));
            try
            {
                f.LumberSentinel = "";
                f.LumberLogsTo = "";
                f.LumberFrom = "";
                Check.Equal("", Spots(), "nothing set");
                f.LumberSentinel = "1100,1101,0," + TreeArt;
                f.LumberLogsTo = "ground|0|1000|1010|0";
                f.LumberFrom = "1200,1201";
                Check.Equal("1100,1101:0 1000,1010:1 1200,1201:2", Spots(), "all three");
                f.LumberLogsTo = "item|" + Bag.ToString("X") + "|990|991|0";
                Check.Equal("1100,1101:0 990,991:1 1200,1201:2", Spots(), "a bag's tile");
                f.LumberLogsTo = "mobile|00001234";
                f.LumberFrom = "a,b";
                f.LumberSentinel = AssistFeatures.SentinelNext;
                Check.Equal("", Spots(), "a pack animal, an unreadable Chop from, the next tree");
                f.LumberLogsTo = "ground|0|x|1|0";
                Check.Equal("", Spots(), "an unreadable tile");
            }
            finally
            {
                f.LumberSentinel = "";
                f.LumberLogsTo = "";
                f.LumberFrom = "";
            }
        });
    }

    static void OldDropsRead()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s);
            s.ServerSends(Lying(PileB + 8, LogArt, 70, 1000, 1003));
            s.ServerSends(Lying(PileB + 9, LogArt, 30, 1000, 1005));
            s.Clock.Sleep(1);
            f.Cfg.Set(f.Server + "lumber.old", (PileB + 8).ToString("X") + ",zz");
            f.LumberLogsTo = "ground|0|1000|1010|0";
            int found;
            List<int[]> rec;
            try
            {
                found = f.LumberDiscover(s.Link);
                rec = f.LumberPiles();
            }
            finally
            {
                f.LumberLogsTo = "";
                f.LumberPilesClear();
                f.Cfg.Set(f.Server + "lumber.old", "");
            }
            Check.Equal(1, found, "only the pile not saved as old, an unreadable entry passed");
            Check.Equal(1005, rec[0][1], "the pile at 1000,1005");
        });
    }

    // Collect at 40 reached by the pile of 40 at 1000,1004 before the stop: Resume carries it to 1000,1010 first.
    static void ResumeCollectsDue()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            s.ServerSends(Packets.Contents(Pack));
            Kwik(s);
            s.ServerSends(Packets.Contained(Axe, AxeArt, 1, Pack));
            s.ServerSends(Packets.Status(51));
            s.Clock.Sleep(1);
            Ground ground = Piles(s, f, new List<int[]>(), new[] { 1000, 1004, 40 });
            List<Move> moves = Shard(s, ground, chop: x => "You put some logs into your backpack.");
            f.LumberAxe = Axe;
            f.LumberLogsTo = "ground|0|1000|1010|0";
            f.LumberAtFeet = true;
            f.LumberCollectAt = 40;
            f.LumberStatics = (x, y) => new List<int[]>();
            int left;
            try
            {
                Check.Null(f.CircuitLumber(), "CircuitLumber");
                for (int i = 0; i < 2000 && f.CircuitRunning; i++)
                    s.Clock.Sleep(50);
                left = f.LumberPiles().Count;
            }
            finally
            {
                f.LumberAtFeet = false;
                f.LumberLogsTo = "";
                f.LumberCollectAt = 1000;
                f.LumberPilesClear();
            }
            Check.Equal(0, left, "collected");
            Check.True(f.Notes.Exists(n => n.Contains("collected 40 logs to 1000,1010")), "the collection ran");
            Check.True(f.CircuitStatus.Contains("no tree within"),
                "then the circuit looked for a tree: " + f.CircuitStatus);
        });
    }

    static void NewChopNoPiles()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            s.ServerSends(Packets.Contents(Pack));
            Kwik(s);
            s.ServerSends(Packets.Contained(Axe, AxeArt, 1, Pack));
            s.Clock.Sleep(1);
            f.LumberAxe = Axe;
            f.LumberLogsTo = "feet|0";
            f.LumberPilesClear();
            try
            {
                Check.Null(f.CircuitLumberNew(), "CircuitLumberNew");
                f.CircuitHalt("test done");
                for (int i = 0; i < 2000 && f.CircuitRunning; i++)
                    s.Clock.Sleep(50);
                Check.True(!f.CircuitRunning, "ended: " + f.CircuitStatus);
            }
            finally
            {
                f.LumberLogsTo = "";
            }
            bool noted;
            lock (f.Notes)
                noted = f.Notes.Exists(n => n.Contains("Lumber: new chop") && !n.Contains("piles at feet"));
            Check.True(noted, "the note names no piles");
        });
    }

    static void NewChopRunning()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            s.ServerSends(Packets.Contents(Pack));
            Kwik(s);
            s.ServerSends(Packets.Contained(Axe, AxeArt, 1, Pack));
            s.ServerSends(Packets.Status(51));
            s.Clock.Sleep(1);
            List<int[]> walks = new();
            Ground ground = Piles(s, f, walks, new[] { 1000, 1004, 40 });
            List<Move> moves = Shard(s, ground, chop: x => "You put some logs into your backpack.");
            f.LumberAxe = Axe;
            f.LumberLogsTo = "ground|0|1000|1010|0";
            f.LumberAtFeet = true;
            f.LumberStatics = (x, y) => x == 1001 && y == 1010 ? new List<int[]> { new[] { TreeArt, 0 } }
                : new List<int[]>();
            int before = f.LumberPiles().Count, after;
            string? refused;
            try
            {
                Check.Null(f.CircuitLumber(), "CircuitLumber");
                refused = f.CircuitLumberNew();
                after = f.LumberPiles().Count;
                f.CircuitHalt("test done");
                for (int i = 0; i < 2000 && f.CircuitRunning; i++)
                    s.Clock.Sleep(50);
            }
            finally
            {
                f.LumberAtFeet = false;
                f.LumberLogsTo = "";
            }
            Check.Equal("the circuit is running", refused, "refused");
            Check.True(before > 0, "piles to keep: " + before);
            Check.Equal(before, after, "the piles kept");
            f.LumberPilesClear();
        });
    }

    static void CollectNowNothing()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            f.LumberPilesClear();
            Check.Equal("no piles dropped to collect", f.CollectNow(), "refused");
        });
    }

    static void DropHover()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            f.LumberPilesClear();
            f.LumberPileAdd(4685, 1166, 140);
            try
            {
                Check.Equal("dropped logs: 140 at 4685,1166", f.ChunkText(4685, 1166), "the pile");
                Check.Null(f.ChunkText(4686, 1166), "beside it");
                f.LumberPilesClear();
                Check.Null(f.ChunkText(4685, 1166), "cleared");
            }
            finally
            {
                f.LumberPilesClear();
            }
        });
    }

    // After the drop's answer and settle, before the next hop.
    const int DropCloseMs = 2000;

    static void CircuitNoPlace()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            s.ServerSends(Packets.Contents(Pack));
            Kwik(s);
            s.ServerSends(Packets.Contained(Axe, AxeArt, 1, Pack));
            s.Clock.Sleep(1);
            f.LumberAxe = Axe;
            f.LumberLogsTo = "feet|0";
            try
            {
                Check.Null(f.CircuitLumber(), "CircuitLumber");
                for (int i = 0; i < 200 && f.CircuitRunning; i++)
                    s.Clock.Sleep(50);
            }
            finally
            {
                f.LumberLogsTo = "";
            }
            Check.True(f.CircuitStatus.Contains("no place for the collection"), "end: " + f.CircuitStatus);
            Check.Equal(0, Sent(s, 0x6C), "no chop");
        });
    }

    const uint Wild1 = 0x44AB0020;
    const uint Wild2 = 0x44AB0021;
    const uint Housed = 0x44AB0022;
    const uint AtPlace = 0x44AB0023;

    // The player at 1000,1000, the place at 1000,1010; wild logs at 1003,1000 (12) and 1000,1005 (7), a pile of ours at
    // 1002,1002 in the record, logs in a house at 1005,1005 and logs on the place itself.
    static void WildGround(FakeShard s, AssistFeatures f)
    {
        Kwik(s);
        f.LumberPilesClear();
        f.LumberPileAdd(1002, 1002, 30);
        s.ServerSends(Lying(Wild1, LogArt, 12, 1003, 1000));
        s.ServerSends(Lying(Wild2, LogArt, 7, 1000, 1005));
        s.ServerSends(Lying(PileA, LogArt, 30, 1002, 1002));
        s.ServerSends(Lying(Housed, LogArt, 50, 1005, 1005));
        s.ServerSends(Lying(AtPlace, LogArt, 90, 1000, 1010));
        s.ServerSends(Lying(Bag, 0x0E76, 1, 1001, 1001));
        s.Clock.Sleep(1);
        f.LumberHoused = (x, y) => x == 1005 && y == 1005;
        f.LumberLogsTo = "ground|0|1000|1010|0";
    }

    static void WildDone(AssistFeatures f)
    {
        f.LumberLogsTo = "";
        f.LumberPilesClear();
        f.LumberHoused = AssistFeatures.HousedOnMap;
        f.HousedMax = 4096;
    }

    static void Discover()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            WildGround(s, f);
            try
            {
                Check.Equal(2, f.LumberDiscover(s.Link), "piles found");
                List<int[]> rec = f.LumberPiles();
                Check.Equal(3, rec.Count, "ours and the two wild");
                int[]? w1 = rec.Find(p => p[0] == 1003 && p[1] == 1000);
                Check.True(w1 is not null && w1[2] == 12 && w1[3] == 1, "the 12 at 1003,1000 wild");
                int[]? ours = rec.Find(p => p[0] == 1002 && p[1] == 1002);
                Check.True(ours is not null && ours[2] == 30 && ours[3] == 0, "ours kept ours, its count unchanged");
                Check.Equal(19, f.LumberWild(), "wild logs");
                Check.Equal(49, f.LumberDropped(), "all logs in the record");
                Check.Equal("wild logs: 7 at 1000,1005", f.ChunkText(1000, 1005), "the hover names them wild");
                Check.Equal(0, f.LumberDiscover(s.Link), "nothing new the second time");
                Check.True(f.Notes.Exists(n => n.Contains("found 19 wild logs in 2 piles")), "noted");
            }
            finally
            {
                WildDone(f);
            }
        });
    }

    static void DiscoverNoPlace()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            WildGround(s, f);
            try
            {
                f.LumberLogsTo = "feet|0";
                Check.Equal(0, f.LumberDiscover(s.Link), "no collection, no discovery");
                Check.Equal(1, f.LumberPiles().Count, "the record unchanged");
            }
            finally
            {
                WildDone(f);
            }
        });
    }

    static void DiscoverAgain()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            WildGround(s, f);
            try
            {
                int asked = 0;
                f.LumberHoused = (x, y) => { if (x == 1005 && y == 1005) asked++; return x == 1005 && y == 1005; };
                f.LumberDiscover(s.Link);
                f.LumberPilesClear();
                Check.Equal(3, f.LumberDiscover(s.Link),
                    "after the record is forgotten, the two wild and ours found again");
                Check.Equal(1, asked, "the house's pile asked once");
                Check.Equal(3, f.LumberPiles().FindAll(p => p[3] == 1).Count, "all three wild now");
                // Two more housed piles with the cap at 2: the third kept starts the set afresh, so the first is asked
                // again.
                f.HousedMax = 2;
                f.LumberHoused = (x, y) => { if (x == 1005 && y == 1005) asked++; return y == 1005 || y == 1006
                    || y == 1007; };
                s.ServerSends(Lying(Housed + 0x10, LogArt, 5, 1006, 1006));
                s.ServerSends(Lying(Housed + 0x11, LogArt, 5, 1007, 1007));
                s.Clock.Sleep(1);
                f.LumberDiscover(s.Link);
                f.LumberDiscover(s.Link);
                Check.Equal(2, asked, "past the cap the first house pile is asked again");
            }
            finally
            {
                WildDone(f);
            }
        });
    }

    static void WildGone()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s);
            List<int[]> walks = new();
            Ground ground = Piles(s, f, walks, new[] { 1000, 1006, 30 });
            f.LumberPileAdd(1000, 1004, 40, wild: true);
            List<Move> moves = Shard(s, ground);
            f.LumberLogsTo = "ground|0|1000|1010|0";
            try
            {
                Check.Null(f.LumberCollect(s.Link), "LumberCollect");
                Check.Equal(0, f.LumberPiles().Count, "the record empty");
                int[] a = ground.Piles[PileA]!;
                Check.Equal(30, a[2], "the 30 at the place");
                Check.Equal(1000 * 65536 + 1010, a[0] * 65536 + a[1], "at 1000,1010");
                Check.True(f.Notes.Exists(n => n.Contains("the wild logs at 1000,1004 are gone; passed")), "noted");
            }
            finally
            {
                f.LumberLogsTo = "";
                f.LumberPilesClear();
            }
        });
    }

    static void WildRecord()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            f.LumberPilesClear();
            try
            {
                f.LumberPileAdd(1, 1, 10, wild: true);
                f.LumberPileAdd(2, 2, 20, wild: true);
                f.LumberPileAdd(3, 3, 5);
                Check.Equal(1, f.LumberPiles().Find(p => p[0] == 1)![3], "wild kept across the record's text");
                f.LumberPileAdd(2, 2, 4, wild: true);
                Check.Equal(1, f.LumberPiles().Find(p => p[0] == 2)![3], "wild on wild stays wild");
                f.LumberPileMove(1, 1, 2, 2, false);
                int[] two = f.LumberPiles().Find(p => p[0] == 2)!;
                Check.True(two[2] == 34 && two[3] == 1, "two wild merged: wild, summed");
                f.LumberPileMove(2, 2, 3, 3, false);
                int[] three = f.LumberPiles().Find(p => p[0] == 3)!;
                Check.True(three[2] == 39 && three[3] == 0, "wild onto ours: ours");
                f.LumberPileMove(3, 3, 4, 4, false);
                f.LumberPileAdd(5, 5, 1, wild: true);
                f.LumberPileMove(5, 5, 6, 6, false);
                Check.Equal(1, f.LumberPiles().Find(p => p[0] == 6)![3], "wild moved to an empty tile: wild");
                f.LumberPileAdd(6, 6, 2);
                Check.Equal(0, f.LumberPiles().Find(p => p[0] == 6)![3], "ours put on a wild pile: ours");
                Check.Equal(0, f.LumberWild(), "no wild left");
            }
            finally
            {
                f.LumberPilesClear();
            }
        });
    }

    static void CollectNowWild()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s);
            List<int[]> walks = new();
            Ground ground = Piles(s, f, walks);
            f.LumberPilesClear();
            ground.Piles = ground.Piles.Set(Wild1, new[] { 1000, 1004, 12 });
            s.ServerSends(Lying(Wild1, LogArt, 12, 1000, 1004));
            s.Clock.Sleep(1);
            List<Move> moves = Shard(s, ground);
            f.LumberLogsTo = "ground|0|1000|1010|0";
            try
            {
                Check.Null(f.CollectNow(), "CollectNow");
                for (int i = 0; i < 400 && (f.CircuitRunning || f.LumberPiles().Count > 0); i++)
                    s.Clock.Sleep(50);
                Check.Equal(0, f.LumberPiles().Count, "collected");
                Check.Equal(1000 * 65536 + 1010, ground.Piles[Wild1]![0] * 65536 + ground.Piles[Wild1]![1],
                    "the wild logs at the place");
            }
            finally
            {
                f.CircuitHalt("test done");
                for (int i = 0; i < 200 && f.CircuitRunning; i++)
                    s.Clock.Sleep(50);
                f.LumberLogsTo = "";
                f.LumberPilesClear();
            }
        });
    }
}
