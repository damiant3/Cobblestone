#nullable enable
using System;
using System.Collections.Immutable;
using System.IO;
using System.Collections.Generic;

namespace Uoaix.Assist.Tests;

// The thread stash's record (Stash.cs): a stash's line and its parse, every field and every refusal; the stash file
// kept across restarts, one stash a serial; and the spot finder with no client map (the suites hold none).
public static class StashTests
{
    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("stash: a line parses back to the same stash, every field", RoundTrip),
        new TestCase("stash: a line of 7 or 9 fields, or any field unreadable, is no stash", Refusals),
        new TestCase("stashes: put, replace by serial, remove, and all of it read back by a fresh instance", Persisted),
        new TestCase("stashes: no file is no stash; unreadable lines are skipped", MissingAndBad),
        new TestCase("stashes: a file that cannot be written loses nothing in memory", Unwritable),
        new TestCase("stash spots: with no client map no tile is a spot and nothing is covered", NoMap),
        new TestCase("stash spots: a static 15 up south-east covers whole, 10 up east or south half each, at the bounds",
        CoverBounds),
        new TestCase("stash spots: a roof, a static above the floor, a passable surface and unknown art cover nothing",
        CoverIgnores),
        new TestCase("stash spots: a door, the player's tile, a refused tile, no step and another floor are no spot",
        FindRefuses),
        new TestCase("stash spots: the most hidden first, then beside furniture, then the nearest", FindOrder),
        new TestCase("stash spots: a wall, a static on another floor, a flat tile and unknown art furnish nothing; "
        + "unknown art is no door", FurnishIgnores));

    public static ImmutableArray<TestCase> Cases => s_cases;

    static readonly Stash s_one = new(0x44AE0001, 0x0FA0, 120, 1495, 1625, 20, "Britain",
        new DateTime(638650000000000000L));

    static string Scratch() => Path.Combine(Path.GetTempPath(),
        "assist-tests-stash-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("N") + ".txt");

    static void RoundTrip()
    {
        Stash? back = Stash.Parse(s_one.Line());
        Check.Equal(s_one, back, "the same stash");
        Check.Equal("44AE0001\tFA0\t120\t1495\t1625\t20\tBritain\t638650000000000000", s_one.Line(), "the line");
    }

    static void Refusals()
    {
        string[] t = s_one.Line().Split('\t');
        Check.Null(Stash.Parse(string.Join("\t", t, 0, 7)), "7 fields");
        Check.Null(Stash.Parse(s_one.Line() + "\textra"), "9 fields");
        for (int i = 0; i < t.Length; i++)
        {
            if (i == 6)
                continue;
            string[] bad = (string[])t.Clone();
            bad[i] = "x!";
            Check.Null(Stash.Parse(string.Join("\t", bad)), "field " + i + " unreadable");
        }
        string[] city = (string[])t.Clone();
        city[6] = "";
        Check.True(Stash.Parse(string.Join("\t", city)) is not null, "a city is any text, even none");
    }

    static void Persisted()
    {
        string path = Scratch();
        try
        {
            Stashes a = new(() => path);
            a.Put(s_one);
            a.Put(s_one with
            {
                Amount = 80
            });
            Stash two = s_one with
            {
                Serial = 0x44AE0002,
                X = 1496
            };
            a.Put(two);
            Check.Equal(2, a.All.Count, "one stash a serial");
            Check.Equal(80, a.All.Find(x => x.Serial == s_one.Serial)!.Amount, "replaced");
            a.Remove(two.Serial);
            a.Remove(0x44AEFFFF);
            Stashes b = new(() => path);
            Check.Equal(1, b.All.Count, "read back");
            Check.Equal(s_one with
            {
                Amount = 80
            }, b.All[0], "the stash read back");
        }
        finally
        {
            File.Delete(path);
        }
    }

    static void MissingAndBad()
    {
        string path = Scratch();
        try
        {
            Check.Equal(0, new Stashes(() => path).All.Count, "no file");
            File.WriteAllText(path, "");
            Check.Equal(0, new Stashes(() => path).All.Count, "an empty file");
            File.WriteAllLines(path, new[] { "not a stash", s_one.Line(), "", "1\t2" });
            Stashes s = new(() => path);
            Check.Equal(1, s.All.Count, "the readable line only");
            Check.Equal(s_one, s.All[0], "its stash");
        }
        finally
        {
            File.Delete(path);
        }
    }

    // A path that is a folder: the read finds no file, the write fails quietly, and the stash stays in memory.
    static void Unwritable()
    {
        string dir = Scratch();
        Directory.CreateDirectory(dir);
        try
        {
            Stashes s = new(() => dir);
            s.Put(s_one);
            Check.Equal(1, s.All.Count, "kept in memory");
            s.Remove(s_one.Serial);
            Check.Equal(0, s.All.Count, "removed in memory");
        }
        finally
        {
            Directory.Delete(dir);
        }
    }

    // Test art: 1 a tall static (20 high), 2 one 15 high, 3 one 14 high, 4 one 10 high, 5 one 9 high, 6 a roof (30
    // high), 7 a passable surface (2 high), 8 a door (20 high), 9 a wall (20 high).
    const uint Wall = 0x10, Surface = 0x200, Roof = 0x10000000, Door = 0x20000000;
    // 10 an impassable surface (20 high).
    const uint Impassable = 0x40;
    static readonly ImmutableArray<int> s_heights = [ 0, 20, 15, 14, 10, 9, 30, 2, 20, 20, 20 ];
    static readonly ImmutableArray<uint> s_flags = [ 0, 0, 0, 0, 0, 0, Roof, Surface, Door, Wall,
        Surface | Impassable ];

    static List<UoTiles.Tile> TestTiles()
    {
        List<UoTiles.Tile> l = new();
        for (int g = 0; g < s_heights.Length; g++)
            l.Add(new UoTiles.Tile(g, 1, s_heights[g], 0, s_flags[g], "art " + g));
        return l;
    }

    // A map of statics (x, y, art, z), level floor at z 0 everywhere but where stepZ says otherwise.
    static StashMap Map(int[][] statics, Func<int, int, int>? floor = null)
    {
        return new StashMap(
            (x, y) =>
            {
                List<int[]> l = new();
                foreach (int[] s in statics)
                {
                    if (s[0] == x && s[1] == y)
                        l.Add(new[] { s[2], s[3] });
                }
                return l;
            },
            TestTiles,
            (fx, fy, fz, x, y) => floor is null ? 0 : floor(x, y));
    }

    static void CoverBounds()
    {
        Check.Equal(4, StashSpots.Cover(10, 10, 0, Map(new[] { new[] { 11, 11, 2, 0 } })), "15 up south-east: whole");
        Check.Equal(0, StashSpots.Cover(10, 10, 0, Map(new[] { new[] { 11, 11, 3, 0 } })), "14 up south-east: none");
        Check.Equal(2, StashSpots.Cover(10, 10, 0, Map(new[] { new[] { 11, 10, 4, 0 } })), "10 up east: half");
        Check.Equal(0, StashSpots.Cover(10, 10, 0, Map(new[] { new[] { 11, 10, 5, 0 } })), "9 up east: none");
        Check.Equal(2, StashSpots.Cover(10, 10, 0, Map(new[] { new[] { 10, 11, 4, 0 } })), "10 up south: half");
        Check.Equal(4, StashSpots.Cover(10, 10, 0, Map(new[] { new[] { 11, 10, 4, 0 }, new[] { 10, 11, 4, 0 } })),
            "east and south: whole");
        Check.Equal(4, StashSpots.Cover(10, 10, 0, Map(new[] { new[] { 11, 11, 1, 0 }, new[] { 11, 10, 4, 0 } })),
            "never over whole");
        Check.Equal(4, StashSpots.Cover(10, 10, 5, Map(new[] { new[] { 11, 11, 1, 0 } })),
            "a rise counts from the spot's own height");
    }

    static void CoverIgnores()
    {
        Check.Equal(0, StashSpots.Cover(10, 10, 0, Map(new[] { new[] { 11, 11, 6, 0 } })), "a roof");
        Check.Equal(0, StashSpots.Cover(10, 10, 0, Map(new[] { new[] { 11, 11, 1, 11 } })),
            "a static starting 11 above the floor");
        Check.Equal(4, StashSpots.Cover(10, 10, 0, Map(new[] { new[] { 11, 11, 1, 10 } })),
            "one starting 10 above counts");
        Check.Equal(0, StashSpots.Cover(10, 10, 2, Map(new[] { new[] { 11, 11, 7, 0 } })),
            "a passable surface at the floor");
        Check.Equal(0, StashSpots.Cover(10, 10, 0, Map(new[] { new[] { 11, 11, 99, 0 }, new[] { 11, 11, -1, 0 } })),
            "unknown art");
    }

    static void FindRefuses()
    {
        StashMap map = Map(new[] { new[] { 11, 10, 8, 0 } },
            (x, y) => x == 9 && y == 9 ? int.MinValue : x == 9 && y == 11 ? 11 : x == 11 && y == 11 ? 10 : 0);
        ImmutableArray<StashSpots.Spot> spots = StashSpots.Find(9, 9, 3, 3, 10, 10, 0, (x, y) => x == 10 && y == 9,
            map);
        Set<long> at = Set<long>.Empty;
        foreach (StashSpots.Spot sp in spots)
            at = at.Add((long)sp.X << 16 | (uint)sp.Y);
        Check.False(at.Contains(11L << 16 | 10), "the door's tile");
        Check.False(at.Contains(10L << 16 | 10), "the player's tile");
        Check.False(at.Contains(10L << 16 | 9), "a refused tile");
        Check.False(at.Contains(9L << 16 | 9), "no step there");
        Check.False(at.Contains(9L << 16 | 11), "11 up is another floor");
        Check.True(at.Contains(11L << 16 | 11), "10 up is this floor");
        Check.Equal(4, spots.Length, "the four left");
    }

    // The player at 10,10 and a row of tiles 11..14 at y 10: 14 hidden behind a static at 15,11; a table at 14,9 (a
    // 2 high static, no wall) beside 13, 14 and 15; 11 and 12 plain, 11 nearer. Order: 14, 13, 11, 12.
    static void FindOrder()
    {
        StashMap map = Map(new[] { new[] { 15, 11, 1, 0 }, new[] { 14, 9, 7, 0 } });
        ImmutableArray<StashSpots.Spot> spots = StashSpots.Find(11, 10, 4, 1, 10, 10, 0, (x, y) => false, map);
        Check.Equal(4, spots.Length, "four spots");
        Check.Equal(14, spots[0].X, "the hidden one first");
        Check.Equal(4, spots[0].Cover, "hidden whole");
        Check.Equal(13, spots[1].X, "then the one beside furniture, farther than two plain ones");
        Check.True(spots[1].Furnished, "furnished");
        Check.Equal(11, spots[2].X, "then the nearest plain one");
        Check.Equal(12, spots[3].X, "the farther plain one last");
        Check.False(spots[3].Furnished, "plain");
    }

    // The player at 10,10, one spot asked at 12,10: each neighbour of it given one thing that does not furnish.
    static void FurnishIgnores()
    {
        foreach (int[] s in new[] { new[] { 13, 10, 9, 0 }, new[] { 13, 10, 1, 11 }, new[] { 13, 10, 0, 0 },
            new[] { 13, 10, 99, 0 }, new[] { 12, 10, 99, 0 } })
        {
            ImmutableArray<StashSpots.Spot> spots = StashSpots.Find(12, 10, 1, 1, 10, 10, 0, (x, y) => false,
                Map(new[] { s }));
            Check.Equal(1, spots.Length, "a spot with art " + s[2] + " at z " + s[3]);
            Check.False(spots[0].Furnished, "not furnished by art " + s[2] + " at z " + s[3]);
        }
        Check.Equal(4, StashSpots.Cover(10, 10, 2, Map(new[] { new[] { 11, 11, 10, 0 } })),
            "an impassable surface covers");
    }

    static void NoMap()
    {
        Check.Equal(0, StashSpots.Find(1490, 1620, 10, 10, 1495, 1625, 20, (x, y) => false).Length, "no spot");
        Check.Equal(0, StashSpots.Cover(1495, 1625, 20), "nothing covered");
    }
}
