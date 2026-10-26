#nullable enable
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System;

namespace Uoaix.Assist.Tests;

// The walk planner (AssistFeatures.PlanPath) over a client map of the test's own (TestMap): the route's tiles never a
// wall or water, its end within reach, its cost the cheapest (a straight step 100, a diagonal 101, a turn 100).
public static class PlannerTests
{
    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("plan: open grass is a straight line, one step a tile", Straight),
        new TestCase("plan: a diagonal goal is all diagonals", Diagonal),
        new TestCase("plan: reach 2 stops 2 tiles short", Reach),
        new TestCase("plan: a wall is walked round, never through, at the cheapest cost", AroundWall),
        new TestCase("plan: a wall wider than the box is walked round once the box widens", WidenedBox),
        new TestCase("plan: a target sealed by walls has no way", Sealed),
        new TestCase("plan: water across the box is no way", WaterAcross),
        new TestCase("plan: a teleporter across water is taken, the route ending on it", Teleporter),
        new TestCase("plan: a stop asked ends the plan with no route and says it stopped", Stopped),
        new TestCase("plan: a stop test that never fires is polled, and the plan ends as no way, not stopped",
        NotStopped),
        new TestCase("walk: a stop during the plan ends the walk as stopped, no step sent", WalkStopped),
        new TestCase("walk: the same walk with no stop asked is no way", WalkNoWay),
        new TestCase("plan: a house's wall is walked round, and gone with the house the way is straight", HouseWall),
        new TestCase("plan: a house put before the client's folder is known stands, walled, once it is",
        HouseBeforeFolder),
        new TestCase("plan: a teleporter learned on a raised pad is linked from its tile's lower floors within 16, "
        + "never above", TeleporterFloors),
        new TestCase("plan tables: a tile's floors are states of their own, a state outside the box is found, the "
        + "tables grow past their first size, refused tiles are closed", GridStates),
        new TestCase("plan tables: arrivals keep their first order and their latest cost, a route stops on a link's "
        + "tile", GridArrivals),
        new TestCase("plan: a start outside the box walks in; walks back into a goal cost what the walks out cost",
        WalksOutsideAndBack));

    public static ImmutableArray<TestCase> Cases => s_cases;

    static readonly ImmutableArray<int> s_dx = [ 0, 1, 1, 1, 0, -1, -1, -1 ];
    static readonly ImmutableArray<int> s_dy = [ -1, -1, 0, 1, 1, 1, 0, -1 ];

    static List<int>? Plan(FakeShard s, AssistFeatures f, int sx, int sy, int tx, int ty, int reach)
    {
        return f.PlanPath(s.Link, sx, sy, 0, tx, ty, reach, Set<long>.Empty);
    }

    // The tile the route ends on, every tile on the way checked against the map.
    static int[] Follow(TestMap map, List<int> dirs, int sx, int sy)
    {
        int x = sx;
        int y = sy;
        foreach (int d in dirs)
        {
            x += s_dx[d];
            y += s_dy[d];
            Check.True(!map.IsWall(x, y) && !map.IsWater(x, y), "the route steps on " + x + "," + y);
        }
        return new[] { x, y };
    }

    static int Cost(List<int> dirs)
    {
        int cost = 0;
        for (int i = 0; i < dirs.Count; i++)
        {
            cost += s_dx[dirs[i]] != 0 && s_dy[dirs[i]] != 0 ? 101 : 100;
            if (i > 0 && dirs[i] != dirs[i - 1])
                cost += 100;
        }
        return cost;
    }

    static void OnMap(TestMap map, Action<FakeShard, AssistFeatures> body)
    {
        map.Run(() => RecallTests.WithFeatures(body));
    }

    // Moonglow's 4540,898 as the client's map gives it (2026-10-24): stood on from 21 to 26 the step at 27, from 27 up
    // the pad at 32 where the teleporter was learned, nothing below 21.
    static void TeleporterFloors()
    {
        Func<int, int> stand = from => from < 21 ? int.MinValue : from < 27 ? 27 : 32;
        List<int> floors = AssistFeatures.TeleporterFloors(32, stand);
        Check.Equal("27", string.Join(",", floors), "the step under the pad");
        Check.Equal("", string.Join(",", AssistFeatures.TeleporterFloors(32, from => 32)), "one floor: nothing more");
        Check.Equal("", string.Join(",", AssistFeatures.TeleporterFloors(32, from => from < 25 ? 10 : 32)),
            "a floor 22 below: past the reach");
        Check.Equal("16", string.Join(",", AssistFeatures.TeleporterFloors(32, from => from < 20 ? 16 : 32)),
            "a floor exactly 16 below: in reach");
    }

    static void GridStates()
    {
        AssistFeatures.PlanGrid g = new(100, 100, 10, 10, Set<long>.Of(((long)101 << 16) | 102u));
        int a = g.Index(100, 100, 0), b = g.Index(100, 100, 20), c = g.Index(99, 100, 0);
        Check.True(a != b && g.Find(100, 100, 20) == b && g.Index(100, 100, 0) == a, "two floors, two states");
        Check.Equal(c, g.Find(99, 100, 0), "a state outside the box");
        Check.Equal(-1, g.Find(99, 100, 5), "outside, another height: none");
        Check.Equal(-1, g.Find(105, 105, 0), "inside, never met: none");
        Check.Equal(AssistFeatures.State(99, 100, 0), g.State(c), "the state's own key");
        Check.False(g.Walkable(101, 102), "a refused tile is closed");
        Check.True(g.Walkable(101, 103), "the tile beside it is open");
        Check.False(g.Walkable(99, 100) || g.Walkable(110, 105), "past the box is closed");
        Check.True(g.InBox(109, 109) && !g.InBox(110, 109) && !g.InBox(100, 99), "the box's edges");
        // Past the first 1024 states: 3000 on 100 tiles, 30 floors each, every one found again.
        List<int> made = new();
        for (int i = 0; i < 3000; i++)
            made.Add(g.Index(100 + i % 10, 100 + i / 10 % 10, i / 100 - 15));
        bool all = true;
        for (int i = 0; i < 3000; i++)
            all &= g.Find(100 + i % 10, 100 + i / 10 % 10, i / 100 - 15) == made[i];
        Check.True(all, "3000 states found again after the tables grew");
        Check.Equal(3002, g.Count, "3000 new beside the two floors of 100,100 and the outside one (one floor shared)");
        Check.True(g.Find(100, 100, 20) == b && g.Find(99, 100, 0) == c, "the first states kept through the growth");
        Check.Equal(AssistFeatures.PlanGrid.Unknown, g.Step[made[2999] * 8 + 7], "a new state's steps unknown");
        Check.Equal(AssistFeatures.PlanGrid.Unknown, g.NodeCost[made[2999] * AssistFeatures.PlanGrid.Headings + 8],
            "a new state's nodes unknown");
    }

    static void GridArrivals()
    {
        AssistFeatures.PlanGrid g = new(0, 0, 10, 10, Set<long>.Empty);
        int s0 = g.Index(1, 1, 0), s1 = g.Index(2, 1, 0), s2 = g.Index(5, 5, 0), s3 = g.Index(5, 6, 0);
        int never = g.Index(7, 7, 0);
        g.Arrive(s0, 0, -1);
        g.Arrive(s1, 100, s0 * 8 + 2);
        g.Arrive(s2, 200, -2 - s1);
        g.Arrive(s3, 300, s2 * 8 + 4);
        g.Arrive(s1, 90, s0 * 8 + 2);
        List<long> states = g.States();
        Check.Equal(4, states.Count, "four arrivals, the rewritten one once");
        Check.True(states[0] == g.State(s0) && states[1] == g.State(s1) && states[3] == g.State(s3), "first order");
        Check.Equal(90, g.Costs()[1].Value, "the rewritten cost in its first place");
        Check.Equal(90, g.CostOf(g.State(s1)), "its cost by key");
        Check.True(g.Reached(g.State(s2)) && !g.Reached(g.State(never)), "met but not arrived is not reached");
        Check.False(g.Reached(AssistFeatures.State(9, 9, 0)), "never met is not reached");
        Check.Equal("2", string.Join(",", g.Route(g.State(s3))), "the route ends on the link's tile, east of the start");
        Check.Equal("", string.Join(",", g.Route(g.State(s0))), "a start's route is empty");
    }

    // On open grass with a wall between: a walk from 59,65, west of the box 60..79, enters it; a walk back into 70,70
    // costs each tile what the walk out of 70,70 to it costs (the steps are the same both ways on flat land).
    static void WalksOutsideAndBack()
    {
        TestMap map = new TestMap().WallLine(72, 60, 72, 75);
        OnMap(map, (s, f) =>
        {
            long goal = f.Walks(s.Link, new[] { AssistFeatures.State(59, 65, 0) }, 60, 60, 20, 20, Set<long>.Empty,
                false, (x, y, z) => x == 65 && y == 65, out AssistFeatures.PlanGrid inn);
            Check.Equal(AssistFeatures.State(65, 65, 0), goal, "the goal reached from outside the box");
            List<int> r = inn.Route(goal);
            Check.True(r.Count == 6 && r.TrueForAll(d => d == 2), "six steps east: " + string.Join(",", r));
            f.Walks(s.Link, new[] { AssistFeatures.State(70, 70, 0) }, 60, 60, 20, 20, Set<long>.Empty, false, null!,
                out AssistFeatures.PlanGrid outward);
            f.Walks(s.Link, new[] { AssistFeatures.State(70, 70, 0) }, 60, 60, 20, 20, Set<long>.Empty, false, null!,
                out AssistFeatures.PlanGrid backward, true);
            int checkedN = 0, differ = 0;
            foreach (KeyValuePair<long, int> kv in outward.Costs())
            {
                checkedN++;
                if (!backward.Reached(kv.Key) || backward.CostOf(kv.Key) != kv.Value)
                    differ++;
            }
            Check.Equal(0, differ, "every tile's cost both ways, of " + checkedN);
            Check.Equal(outward.States().Count, backward.States().Count, "the same tiles reached both ways");
            Check.True(checkedN > 300 && !outward.Reached(AssistFeatures.State(72, 65, 0)), "the box less the wall");
        });
    }

    static void Straight()
    {
        TestMap map = new();
        OnMap(map, (s, f) =>
        {
            List<int>? r = Plan(s, f, 100, 100, 110, 100, 0);
            Check.True(r is not null, "a route");
            Check.Equal(10, r!.Count, "steps");
            Check.True(r.TrueForAll(d => d == 2), "every step east");
        });
    }

    static void Diagonal()
    {
        TestMap map = new();
        OnMap(map, (s, f) =>
        {
            List<int>? r = Plan(s, f, 100, 100, 105, 105, 0);
            Check.True(r is not null, "a route");
            Check.Equal(5, r!.Count, "steps");
            Check.True(r.TrueForAll(d => d == 3), "every step south-east");
        });
    }

    static void Reach()
    {
        TestMap map = new();
        OnMap(map, (s, f) =>
        {
            List<int>? r = Plan(s, f, 100, 100, 110, 100, 2);
            Check.True(r is not null, "a route");
            Check.Equal(8, r!.Count, "steps");
        });
    }

    static void AroundWall()
    {
        TestMap map = new TestMap().WallLine(105, 90, 105, 110);
        OnMap(map, (s, f) =>
        {
            List<int>? r = Plan(s, f, 100, 100, 110, 100, 0);
            Check.True(r is not null, "a route");
            int[] end = Follow(map, r!, 100, 100);
            Check.Equal(110, end[0], "end x");
            Check.Equal(100, end[1], "end y");
            Check.Equal(AroundWallCost, Cost(r!), "the cheapest cost");
        });
    }

    // The cost the planner found round the wall of AroundWall before the planner's search changed (measured
    // 2026-10-22).
    const int AroundWallCost = 2710;

    static void WidenedBox()
    {
        // From y 70 to 130: 30 tiles past the first box's margin of 24 either side.
        TestMap map = new TestMap().WallLine(105, 70, 105, 130);
        OnMap(map, (s, f) =>
        {
            List<int>? r = Plan(s, f, 100, 100, 110, 100, 0);
            Check.True(r is not null, "a route past the first box");
            int[] end = Follow(map, r!, 100, 100);
            Check.Equal(110, end[0], "end x");
            Check.Equal(100, end[1], "end y");
        });
    }

    static void Sealed()
    {
        OnMap(Ring(), (s, f) =>
        {
            Check.True(Plan(s, f, 130, 150, 150, 150, 0) is null, "no way into the ring");
        });
    }

    static void WaterAcross()
    {
        TestMap map = new();
        for (int y = 0; y < TestMap.Size; y++)
            map.WaterAt(120, y);
        OnMap(map, (s, f) =>
        {
            Check.True(Plan(s, f, 110, 100, 130, 100, 0) is null, "no way across");
        });
    }

    static bool Noted(AssistFeatures f, string text)
    {
        lock (f.Notes)
            return f.Notes.Exists(n => n.Contains(text));
    }

    // The sealed ring of Sealed: a plan into it searches every box (every plan asking its stop), finding no way.
    static TestMap Ring() => new TestMap().WallLine(148, 148, 152, 148).WallLine(148, 152, 152, 152).WallLine(148, 148,
        148, 152).WallLine(152, 148, 152, 152);

    static void Stopped()
    {
        OnMap(Ring(), (s, f) =>
        {
            f.PlanStopped = () => true;
            Check.True(Plan(s, f, 130, 150, 150, 150, 0) is null, "no route");
            Check.True(Noted(f, "Walk: planning 130,150 to 150,150 stopped after"), "the note says stopped");
        });
    }

    static void NotStopped()
    {
        OnMap(Ring(), (s, f) =>
        {
            int asked = 0;
            f.PlanStopped = () =>
            {
                asked++;
                return false;
            };
            Check.True(Plan(s, f, 130, 150, 150, 150, 0) is null, "no way in");
            Check.True(asked > 0, "the stop was asked: " + asked);
            Check.False(Noted(f, "stopped after"), "not noted as stopped");
        });
    }

    static void WalkStopped()
    {
        OnMap(Ring(), (s, f) =>
        {
            s.ServerSends(Packets.LoginConfirm(130, 150, 0));
            s.Clock.Sleep(1);
            f.PlanStopped = () => true;
            string? w = f.WalkTo(150, 150, 0, false, AssistFeatures.AnyFloor, false);
            Check.Equal("stopped", w, "the walk");
            foreach (byte[] p in s.ServerGot)
                Check.True(p[0] != 0x02, "no step sent");
        });
    }

    static void WalkNoWay()
    {
        OnMap(Ring(), (s, f) =>
        {
            s.ServerSends(Packets.LoginConfirm(130, 150, 0));
            s.Clock.Sleep(1);
            string? w = f.WalkTo(150, 150, 0, false, AssistFeatures.AnyFloor, false);
            Check.True(w is not null && w.StartsWith("no way from 130,150"), "the walk: " + w);
        });
    }

    // A multi of id 0x40 whose wall runs 10 tiles either side of its centre, placed at 105,100 across the way east.
    static void HouseWall()
    {
        int[][] parts = new int[21][];
        for (int i = 0; i < parts.Length; i++)
            parts[i] = new[] { 0, i - 10 };
        TestMap map = new TestMap().MultiWalls(0x40, parts);
        const uint House = 0x40000001;
        OnMap(map, (s, f) =>
        {
            try
            {
                UoMap.PutMulti(House, 0x40, 105, 100, 0);
                List<int>? r = Plan(s, f, 100, 100, 110, 100, 0);
                Check.True(r is not null, "a route round the house");
                int x = 100;
                int y = 100;
                foreach (int d in r!)
                {
                    x += s_dx[d];
                    y += s_dy[d];
                    Check.True(x != 105 || y < 90 || y > 110, "the route steps on the house's wall at " + x + "," + y);
                }
                Check.Equal(110 * 65536 + 100, x * 65536 + y, "at the target");
                Check.True(r.Count > 10, "longer than the straight line: " + r.Count);
                UoMap.DropMulti(House);
                List<int>? straight = Plan(s, f, 100, 100, 110, 100, 0);
                Check.Equal(10, straight!.Count, "straight once the house is gone");
            }
            finally
            {
                UoMap.DropMulti(House);
            }
        });
    }

    // A house the decor catalogue puts at start, read before the client's folder is known (a map painting before the
    // client connects), stands with its walls once the folder is: nothing unread is kept as unreadable (2026-10-24).
    static void HouseBeforeFolder()
    {
        int[][] parts = new int[21][];
        for (int i = 0; i < parts.Length; i++)
            parts[i] = new[] { 0, i - 10 };
        TestMap map = new TestMap().MultiWalls(0x40, parts);
        const uint House = 0x40000002;
        OnMap(map, (s, f) =>
        {
            string? dir = UoArt.Folder;
            try
            {
                UoArt.Folder = null;
                UoMap.PutMulti(House, 0x40, 105, 100, 0);
                Check.True(UoMap.MultiBox(0x40) is null, "no box without the folder");
                Check.Equal(0, UoMap.HousesIn(90, 90, 120, 110).Count, "no house without the folder");
                Check.Equal(0, UoMap.MultiAt(105, 100).Count, "no parts without the folder");
                UoArt.Folder = dir;
                Check.True(UoMap.MultiBox(0x40) is not null, "the box once the folder is known");
                Check.Equal(1, UoMap.HousesIn(90, 90, 120, 110).Count, "the house once the folder is known");
                Check.True(UoMap.MultiAt(105, 100).Count > 0, "its wall once the folder is known");
                List<int>? r = Plan(s, f, 100, 100, 110, 100, 0);
                Check.True(r is not null && r.Count > 10, "the walk goes round it: " + (r?.Count ?? -1));
            }
            finally
            {
                UoArt.Folder = dir;
                UoMap.DropMulti(House);
            }
        });
    }

    static void Teleporter()
    {
        TestMap map = new();
        for (int y = 0; y < TestMap.Size; y++)
            map.WaterAt(120, y);
        OnMap(map, (s, f) =>
        {
            string dir = Path.GetDirectoryName(AssistRelay.SettingsPath)!;
            try
            {
                lock (s.Link.State.Gate)
                    s.Link.State.Jumps.Add(new[] { 118, 100, 0, 122, 100, 0 });
                List<int>? r = Plan(s, f, 110, 100, 130, 100, 0);
                Check.True(r is not null, "a route by the teleporter");
                int[] end = Follow(map, r!, 110, 100);
                Check.Equal(118, end[0], "ends on the teleporter, x");
                Check.Equal(100, end[1], "ends on the teleporter, y");
            }
            finally
            {
                foreach (string t in Directory.GetFiles(dir, "teleporters-*.txt"))
                    File.Delete(t);
            }
        });
    }
}
