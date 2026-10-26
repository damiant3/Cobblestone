#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using System.Threading;

namespace Uoaix.Assist.Tests;

// The cave miner's At my feet (MineGather; Damian, 2026-10-25: "the same drop at feet and gather to collection spot
// stuff we do with lumberjack"), run whole over a test map: a seam of rock, a shard that digs it, takes the lifts and
// drops, and answers the steps.
public static class MineFeetTests
{
    const uint Pack = 0x42BE4F7C;
    const uint Shovel = 0x45E00001;
    const int OreArt = 0x19B7;
    // Kwik on the grass beside a seam of 3 tiles in one 8x8 chunk; the place 6 tiles south; the forge far off.
    const int StartX = 100, StartY = 100, PlaceX = 100, PlaceY = 106;
    // 10 stones an ore over 300 of a 370-stone limit: 2 ore a dig, so the third dig fills the pack (MineFull).
    const int BaseWeight = 300, StonesPerOre = 10, OrePerDig = 2;

    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("mine at feet: each full pack's ore goes down beside the miner and the digging goes on; the "
        + "piles are carried to Ore to at Collect at and when the chunk is empty; nothing smelted", AtFeetRun),
        new TestCase("mine at feet off: a full pack walks to the forge, and no ore goes down at feet", OffSmelts),
        new TestCase("mine at feet with no Ore to refuses before the first dig, and names Ore to", NoPlaceRefuses),
        new TestCase("mine setup: Ore to, At my feet and Collect at keep their own settings, apart from the "
        + "logger's", OwnSettings),
        new TestCase("mine setup: feet on|off and collect-at <n> are set and shown, a bad word refused; ore-to's "
        + "cursor sets Ore to alone, a cancel names it", SetupWords),
        new TestCase("mine at feet resumed with piles past Collect at: they are collected before the first dig",
        ResumeCollects),
        new TestCase("cave back: within the cave's radius and the rune slack it is walked to with no cast, even with "
        + "no rune; one tile past it, with no rune, refused and not walked", CaveWalked),
        new TestCase("toolbox: a mining toolbox within CircuitReach of the cave (the house beside it) is fetched from; "
        + "one tile past it is not", BoxNearCave),
        new TestCase("toolbox: the box seen once is found again out of view and after a restart (its spot in the "
        + "settings); another box's spot is not taken for it", BoxRemembered),
        new TestCase("toolbox: the server's draw of the box keeps its spot with no circuit running (uo1998's 14-byte "
        + "0x1A of 16:12:13); another item's draw does not", BoxDrawn),
        new TestCase("toolbox: the shovel trip takes from the box at its last known spot before any vendor or rune; a "
        + "box past CircuitReach is not walked to", TripTakesFromBox),
        new TestCase("collect now: no piles says so; with no circuit the piles are collected and nothing dug; asked "
        + "mid-run, Collect at unreached, the piles are collected before the seam runs out", CollectNowMine),
        new TestCase("collect: a pile one tile from Ore to, both in reach, is hopped with no walk (uo1998: the stand on "
        + "the pile's own tile no walk reaches)", HopInReach));

    public static ImmutableArray<TestCase> Cases => s_cases;

    static uint U32(byte[] p, int at) => (uint)(p[at] << 24 | p[at + 1] << 16 | p[at + 2] << 8 | p[at + 3]);

    static int U16(byte[] p, int at) => p[at] << 8 | p[at + 1];

    // The large ore pile: uo1998's digs give both, and the two arts do not merge (2026-10-25 16:15:53).
    const int LargeOre = 0x19B9;

    static byte[] Lying(uint serial, int amount, int x, int y, int art = OreArt)
    {
        uint s = serial | 0x80000000;
        return new byte[] { 0x1A, 0, 16, (byte)(s >> 24), (byte)(s >> 16), (byte)(s >> 8), (byte)s,
            (byte)(art >> 8), (byte)art, (byte)(amount >> 8), (byte)amount, (byte)(x >> 8), (byte)x,
            (byte)(y >> 8), (byte)y, 0 };
    }

    // The shard's side: the pack's ore stacks and the ground's piles (serial to x, y, amount), each dig answered with
    // ore into a new pack stack until the seam has given SeamDigs, then "no metal"; every lift and drop kept.
    sealed class Seam
    {
        public readonly Lock Gate = new();
        public ValueMap<uint, int> PackOre = ValueMap<uint, int>.Empty;
        public Map<uint, int[]> Ground = Map<uint, int[]>.Empty;
        // Each stack's art, the small ore unless set.
        public ValueMap<uint, int> Arts = ValueMap<uint, int>.Empty;
        public int Refused;

        public int ArtOf(uint serial) => Arts.Get(serial, OreArt);
        public int Digs, Drops, GroundDrops, PackDrops;
        // The digs made when the first ore reached the place.
        public int DigsAtPlace = -1;
        public int SeamDigs = 8;
        uint m_next = 0x45E00100;

        public uint Next() => m_next++;

        public int Weight()
        {
            int n = 0;
            foreach (int a in PackOre.Values)
                n += a;
            return BaseWeight + n * StonesPerOre;
        }

        public int OreAt(int x, int y)
        {
            int n = 0;
            foreach (int[] g in Ground.Values)
            {
                if (g[0] == x && g[1] == y)
                    n += g[2];
            }
            return n;
        }
    }

    static byte[] Weighed(int weight)
    {
        byte[] st = Packets.Status(51);
        st[64] = (byte)(weight >> 8);
        st[65] = (byte)weight;
        return st;
    }

    static ImmutableArray<byte[]> Answer(FakeShard s, StepShard step, Seam seam, byte[] p)
    {
        lock (seam.Gate)
        {
            if (p[0] == 0x06 && (U32(p, 1) & 0x7FFFFFFF) == Shovel)
                return ImmutableArray.Create<byte[]>(Packets.Cursor(0x00001234, 0x01));
            if (p[0] == 0x6C)
            {
                seam.Digs++;
                bool ore = seam.Digs <= seam.SeamDigs;
                s.Also(300, () =>
                {
                    lock (seam.Gate)
                    {
                        if (!ore)
                        {
                            s.ServerSends(Packets.Speech(0xFFFFFFFF, "", "There is no metal here to mine.", 0));
                            return;
                        }
                        uint stack = seam.Next();
                        seam.PackOre = seam.PackOre.Set(stack, OrePerDig);
                        // Every other dig the large ore, as uo1998's.
                        int art = seam.Digs % 2 == 0 ? LargeOre : OreArt;
                        seam.Arts = seam.Arts.Set(stack, art);
                        s.ServerSends(Packets.Speech(0xFFFFFFFF, "", "You dig some ore and put it in your backpack.",
                            0));
                        s.ServerSends(Packets.Contained(stack, art, OrePerDig, Pack));
                        s.ServerSends(Weighed(seam.Weight()));
                    }
                });
                return ImmutableArray<byte[]>.Empty;
            }
            if (p[0] == 0x07)
                return ImmutableArray<byte[]>.Empty;
            if (p[0] == 0x08)
            {
                seam.Drops++;
                uint item = U32(p, 1);
                uint onto = U32(p, 10);
                int x = U16(p, 5);
                int y = U16(p, 7);
                int amount = seam.Ground.TryGet(item, out int[]? g) ? g[2] : seam.PackOre.Get(item, 0);
                int art = seam.ArtOf(item);
                ImmutableArray<byte[]>.Builder replies = ImmutableArray.CreateBuilder<byte[]>();
                // A drop onto a pile of the other art: put back where it was, as uo1998 does.
                if (seam.Ground.TryGet(onto, out int[]? other) && seam.ArtOf(onto) != art)
                {
                    seam.Refused++;
                    replies.Add(g is not null ? Lying(item, g[2], g[0], g[1], art)
                        : Packets.Contained(item, art, amount, Pack));
                    replies.Add(Weighed(seam.Weight()));
                    return replies.ToImmutable();
                }
                seam.PackOre = seam.PackOre.Remove(item);
                seam.Ground = seam.Ground.Remove(item);
                if (onto != Pack && seam.DigsAtPlace < 0 && (x == PlaceX && y == PlaceY
                    || seam.Ground.TryGet(onto, out int[]? on) && on[0] == PlaceX && on[1] == PlaceY))
                    seam.DigsAtPlace = seam.Digs;
                if (onto == Pack)
                {
                    seam.PackDrops++;
                    seam.PackOre = seam.PackOre.Set(item, amount);
                    replies.Add(Packets.Contained(item, art, amount, Pack));
                }
                else if (seam.Ground.TryGet(onto, out int[]? under))
                {
                    under[2] += amount;
                    replies.Add(Packets.Delete(item));
                    replies.Add(Lying(onto, under[2], under[0], under[1], art));
                }
                else
                {
                    seam.GroundDrops++;
                    seam.Ground = seam.Ground.Set(item, new[] { x, y, amount });
                    replies.Add(Lying(item, amount, x, y, art));
                }
                replies.Add(Weighed(seam.Weight()));
                return replies.ToImmutable();
            }
        }
        return step.Answer(p);
    }

    // Kwik beside the seam with one shovel and an empty pack, the forge set far off, one shovel digging, no cave.
    static void Run(bool feet, string place, Action<FakeShard, AssistFeatures, Seam> body)
    {
        TestMap map = new TestMap().RockAt(StartX + 1, StartY).RockAt(StartX + 1, StartY + 1)
            .RockAt(StartX + 1, StartY + 2);
        map.Run(() => RecallTests.WithFeatures((s, f) =>
        {
            StepShard step = new(s, map, StartX, StartY);
            Seam seam = new();
            s.Answer = p => Answer(s, step, seam, p);
            s.ServerSends(Packets.Wear(Pack, 0x0E75, 0x15, Packets.Player));
            s.ServerSends(Packets.Open(Pack));
            s.ServerSends(Packets.Contents(Pack));
            s.ServerSends(Packets.Contained(Shovel, 0x0F39, 1, Pack));
            s.ServerSends(Weighed(BaseWeight));
            s.Clock.Sleep(1);
            string mode = f.Cfg.Get(f.Server + "mine.mode", "");
            string forge = f.Cfg.Get(f.Server + "mine.forge", "");
            string cave = f.PickedCave ?? "";
            f.MineMode = 1;
            f.PickedCave = "";
            f.Cfg.Set(f.Server + "mine.forge", "1|0|100|200|0|4012");
            f.MinePilesClear();
            f.MineAtFeet = feet;
            f.MineOreTo = place;
            f.MineCollectAt = 10;
            f.CollectRoute = (fx, fy, tx, ty) =>
            {
                List<int[]> l = new() { new[] { fx, fy, 0 } };
                int x = fx, y = fy;
                while (x != tx || y != ty)
                {
                    x += Math.Sign(tx - x);
                    y += Math.Sign(ty - y);
                    l.Add(new[] { x, y, 0 });
                }
                return l.ToArray();
            };
            f.CollectWalk = (x, y) =>
            {
                step.MoveTo(x, y);
                s.ServerSends(Packets.Draw(x, y, 0, 0));
                s.Clock.Sleep(1);
                return null!;
            };
            try
            {
                body(s, f, seam);
            }
            finally
            {
                if (f.CircuitRunning)
                    f.CircuitHalt("the test is done");
                for (int i = 0; i < 600 && f.CircuitRunning; i++)
                    s.Clock.Sleep(100);
                f.CollectRoute = null;
                f.CollectWalk = null!;
                f.MineAtFeet = false;
                f.MineOreTo = "";
                f.MineCollectAt = 200;
                f.MinePilesClear();
                f.MineMode = mode == "1" ? 1 : 2;
                f.PickedCave = cave;
                f.Cfg.Set(f.Server + "mine.forge", forge);
            }
        }));
    }

    static void AtFeetRun()
    {
        Run(true, "ground|0|" + PlaceX + "|" + PlaceY + "|0", (s, f, seam) =>
        {
            Check.Null(f.CircuitMine(), "CircuitMine");
            for (int i = 0; i < 3000 && f.CircuitRunning; i++)
                s.Clock.Sleep(100);
            string notes;
            lock (f.Notes)
                notes = string.Join(" | ", f.Notes.FindAll(n => n.Contains("Mine:") || n.Contains("Lumber:")));
            Check.False(f.CircuitRunning, "the circuit ended: " + f.CircuitStatus);
            Check.True(f.CircuitStatus.Contains(AssistFeatures.NoMetalInReach), "ended for no metal: "
                + f.CircuitStatus);
            Check.False(f.CircuitStatus.Contains("smelting"), "nothing smelted: " + f.CircuitStatus);
            Check.Equal(9, seam.Digs, "8 ore digs and the empty one: " + notes);
            // Full at 6 ore (3 digs) and again at 12: the 12 dropped reach Collect at 10, the collection runs, and the
            // digging goes on for the seam's last 2.
            Check.Equal(6, seam.DigsAtPlace, "the collection at Collect at, before the seam ran out: " + notes);
            lock (seam.Gate)
            {
                Check.Equal(seam.SeamDigs * OrePerDig, seam.OreAt(PlaceX, PlaceY) + PackOreCount(seam),
                    "every ore at Ore to or in the pack: " + seam.OreAt(PlaceX, PlaceY) + " there, "
                    + PackOreCount(seam) + " in the pack: " + notes);
                Check.Equal(0, PackOreCount(seam), "none left in the pack: " + notes);
            }
            Check.Equal(0, f.MinePileCount(), "the pile record empty: " + notes);
            Check.True(notes.Contains("Mine: no metal in reach") && notes.Contains("collecting the piles first"),
                "the end collected: " + notes);
            Check.True(seam.GroundDrops >= 3, "the ore put down on the ground: " + seam.GroundDrops);
            // Ore is hopped, never carried in the pack (MineGather.Carries).
            Check.Equal(0, seam.PackDrops, "no pile carried in the pack: " + notes);
            // Both arts dug, each stack onto its own art only: no drop the server put back.
            Check.Equal(0, seam.Refused, "no stack dropped onto the other art: " + notes);
            lock (seam.Gate)
            {
                bool small = false, large = false;
                foreach (KeyValuePair<uint, int[]> kv in seam.Ground)
                {
                    if (kv.Value[0] == PlaceX && kv.Value[1] == PlaceY)
                    {
                        small |= seam.ArtOf(kv.Key) == OreArt;
                        large |= seam.ArtOf(kv.Key) == LargeOre;
                    }
                }
                Check.True(small && large, "both arts at Ore to: small " + small + ", large " + large);
            }
        });
    }

    static int PackOreCount(Seam seam)
    {
        int n = 0;
        foreach (int a in seam.PackOre.Values)
            n += a;
        return n;
    }

    static void OffSmelts()
    {
        Run(false, "", (s, f, seam) =>
        {
            Check.Null(f.CircuitMine(), "CircuitMine");
            bool forge = false;
            for (int i = 0; i < 3000 && f.CircuitRunning && !forge; i++)
            {
                s.Clock.Sleep(100);
                forge = f.CircuitStatus.Contains("forge");
            }
            Check.True(forge, "a full pack walks to the forge: " + f.CircuitStatus);
            Check.Equal(0, seam.GroundDrops, "no ore put down at feet");
            Check.Equal(0, f.MinePileCount(), "no pile recorded");
        });
    }

    static void NoPlaceRefuses()
    {
        Run(true, "", (s, f, seam) =>
        {
            Check.Null(f.CircuitMine(), "CircuitMine");
            for (int i = 0; i < 600 && f.CircuitRunning; i++)
                s.Clock.Sleep(100);
            Check.True(f.CircuitStatus.Contains("Ore to"), "names Ore to: " + f.CircuitStatus);
            Check.Equal(0, seam.Digs, "no dig");
        });
    }

    // A target cursor's answer: kind 1 a location, serial 0 with kind 0 a cancel.
    static byte[] Targeted(uint cursor, int kind, uint serial, int x, int y)
    {
        byte[] p = new byte[19];
        p[0] = 0x6C;
        p[1] = (byte)kind;
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

    static void SetupWords()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            s.ServerSends(Packets.LoginConfirm(1000, 1000, 0));
            s.ServerSends(Packets.Wear(Pack, 0x0E75, 0x15, Packets.Player));
            s.Clock.Sleep(1);
            string logs = f.LumberLogsTo, store = f.MineStoreTo;
            try
            {
                Check.Equal("OK Ore to: not set, at my feet first", f.MineSetup(new[] { "mine-setup", "feet", "on" }),
                    "feet on");
                Check.True(f.MineAtFeet, "At my feet on");
                Check.True(f.MineSetup(new[] { "mine-setup", "feet", "maybe" }).StartsWith("ERROR"), "feet maybe");
                Check.True(f.MineSetup(new[] { "mine-setup", "feet" }).StartsWith("ERROR"), "feet alone");
                Check.True(f.MineAtFeet, "still on after the refusals");
                Check.Equal("OK collect-at 50", f.MineSetup(new[] { "mine-setup", "collect-at", "50" }), "collect-at");
                Check.True(f.MineSetup(new[] { "mine-setup", "collect-at", "lots" }).StartsWith("ERROR"),
                    "collect-at lots");
                Check.Equal(50, f.MineCollectAt, "still 50");
                string shown = f.MineSetup(new[] { "mine-setup" });
                Check.True(shown.Contains("Ore to: not set, at my feet first ; at feet 0 ore in 0 piles, the "
                    + "collection at 50"), "shown: " + shown);
                Check.Equal("OK target where the ore piles are collected",
                    f.MineSetup(new[] { "mine-setup", "ore-to" }), "ore-to");
                s.ClientSends(Targeted(AssistFeatures.MineOreCursor, 1, 0, 7, 8));
                s.Clock.Sleep(1);
                Check.Equal("ground|0|7|8|0", f.MineOreTo, "Ore to set");
                Check.Equal(logs, f.LumberLogsTo, "Logs to untouched");
                Check.Equal(store, f.MineStoreTo, "Store to untouched");
                s.ClientSends(Targeted(AssistFeatures.MineOreCursor, 0, 0, 0, 0));
                s.Clock.Sleep(1);
                Check.True(f.LumberStatus.StartsWith("Ore to not set"), "a cancel names Ore to: " + f.LumberStatus);
                Check.Equal("OK Ore to: the ground at 7,8,0", f.MineSetup(new[] { "mine-setup", "feet", "off" }),
                    "feet off");
                Check.False(f.MineAtFeet, "At my feet off");
            }
            finally
            {
                f.MineOreTo = "";
                f.MineAtFeet = false;
                f.MineCollectAt = 200;
            }
        });
    }

    // 12 ore left in a pile at 102,103 by the last run, Collect at 10.
    static void ResumeCollects()
    {
        Run(true, "ground|0|" + PlaceX + "|" + PlaceY + "|0", (s, f, seam) =>
        {
            const uint Left = 0x45E00050;
            lock (seam.Gate)
                seam.Ground = seam.Ground.Set(Left, new[] { 102, 103, 12 });
            s.ServerSends(Lying(Left, 12, 102, 103));
            s.Clock.Sleep(1);
            f.MinePileAdd(102, 103, 12);
            Check.Null(f.CircuitMine(), "CircuitMine");
            for (int i = 0; i < 3000 && f.CircuitRunning; i++)
                s.Clock.Sleep(100);
            Check.Equal(0, seam.DigsAtPlace, "collected before the first dig: " + f.CircuitStatus);
            lock (seam.Gate)
                Check.Equal(12 + seam.SeamDigs * OrePerDig, seam.OreAt(PlaceX, PlaceY), "the old pile and the run's");
            Check.Equal(0, f.MinePileCount(), "the record empty");
        });
    }

    // A cave of radius 5 at 100,100 with no rune: 21 tiles south (5 + CaveRuneSlack 16) is walked; 22 is not.
    static void CaveWalked()
    {
        Check.Equal(null, CaveBackFrom(100 + 21), "21 tiles: walked in");
        Check.Equal("no rune back to the cave", CaveBackFrom(100 + 22), "22 tiles, no rune: refused");
        string? walled = CaveBackFrom(100 + 21, true);
        Check.True(walled is not null && walled.StartsWith("walking into the cave from 100,121: "),
            "walled off, no rune: the walk's failure, no recall: " + walled);
    }

    // walled: a wall ring 6 tiles round the mark, so no tile within MineReach of it is reached.
    static string? CaveBackFrom(int y, bool walled = false)
    {
        string? why = "not run";
        TestMap map = new();
        if (walled)
        {
            map.WallLine(94, 94, 106, 94).WallLine(94, 106, 106, 106).WallLine(94, 94, 94, 106)
                .WallLine(106, 94, 106, 106);
        }
        map.Run(() => RecallTests.WithFeatures((s, f) =>
        {
            StepShard step = new(s, map, 100, y);
            s.ServerSends(Packets.Wear(Pack, 0x0E75, 0x15, Packets.Player));
            s.Clock.Sleep(1);
            AssistFeatures.MineCave cave = new() { Name = "Test", X = 100, Y = 100, Radius = 5 };
            System.Reflection.MethodInfo back = typeof(AssistFeatures).GetMethod("CaveBack",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            why = (string?)back.Invoke(f, new object?[] { s.Link, cave });
            int casts = 0, steps = 0;
            foreach (byte[] p in s.ServerGot)
            {
                casts += p[0] == 0x12 ? 1 : 0;
                steps += p[0] == 0x02 ? 1 : 0;
            }
            Check.Equal(0, casts, "no cast from " + y);
            if (why is null)
            {
                Check.True(Math.Max(Math.Abs(step.X - 100), Math.Abs(step.Y - 100)) <= AssistFeatures.MineReach,
                    "within reach of the mark: " + step.X + "," + step.Y);
            }
            else if (!walled)
            {
                Check.Equal(0, steps, "no step from " + y);
            }
        }));
        return why;
    }

    // The cave at 1000,1000 of radius 24; the box CircuitReach (150) and 151 tiles east (uo1998: the box at 2481,73,
    // 43 tiles from Home2's mark).
    static void BoxNearCave()
    {
        Check.True(BoxCounts(AssistFeatures.CircuitReach), "the box at CircuitReach");
        Check.True(BoxCounts(43), "the box 43 tiles off, as Home2's");
        Check.False(BoxCounts(AssistFeatures.CircuitReach + 1), "one tile past it");
    }

    static bool BoxCounts(int east)
    {
        const uint Box = 0x45E00090;
        bool counts = false;
        RecallTests.WithFeatures((s, f) =>
        {
            string key = f.Server + "tools.mining.box";
            string was = f.Cfg.Get(key, "");
            try
            {
                f.Cfg.Set(key, Box.ToString("X"));
                s.ServerSends(Packets.LoginConfirm(1000, 1000, 0));
                s.ServerSends(Packets.Ground(Box, 0x0E7D, 1000 + east, 1000, 0));
                s.Clock.Sleep(1);
                AssistFeatures.MineCave cave = new() { Name = "Test", X = 1000, Y = 1000, Radius = 24 };
                System.Reflection.MethodInfo near = typeof(AssistFeatures).GetMethod("BoxInCave",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                counts = (bool)near.Invoke(f, new object?[] { s.Link, cave })!;
            }
            finally
            {
                f.Cfg.Set(key, was);
            }
        });
        return counts;
    }

    // uo1998 2026-10-25 16:00:22: the relay restarted, the box at 2481,73 out of view from the cave, the shovels gone.
    static void BoxRemembered()
    {
        const uint Box = 0x45E00091;
        RecallTests.WithFeatures((s, f) =>
        {
            string key = f.Server + "tools.mining.box", at = f.Server + "tools.mining.box-at";
            string was = f.Cfg.Get(key, ""), wasAt = f.Cfg.Get(at, "");
            try
            {
                f.Cfg.Set(key, Box.ToString("X"));
                f.Cfg.Set(at, "");
                s.ServerSends(Packets.LoginConfirm(1000, 1000, 0));
                s.ServerSends(Packets.Ground(Box, 0x0E7D, 1043, 1000, 0));
                s.Clock.Sleep(1);
                AssistFeatures.MineCave cave = new() { Name = "Test", X = 1000, Y = 1000, Radius = 24 };
                BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
                MethodInfo near = typeof(AssistFeatures).GetMethod("BoxInCave", flags)!;
                Check.True((bool)near.Invoke(f, new object?[] { s.Link, cave })!, "seen in view");
                s.ServerSends(Packets.Delete(Box));
                s.Clock.Sleep(1);
                // A restart: the relay's memory of the box gone, the settings kept.
                typeof(AssistFeatures).GetField("m_mineBoxSeen", flags)!.SetValue(f, 0u);
                Check.True((bool)near.Invoke(f, new object?[] { s.Link, cave })!, "out of view, after a restart");
                f.Cfg.Set(at, (Box + 1).ToString("X") + "|1043|1000|0");
                Check.False((bool)near.Invoke(f, new object?[] { s.Link, cave })!, "another box's spot");
            }
            finally
            {
                f.Cfg.Set(key, was);
                f.Cfg.Set(at, wasAt);
            }
        });
    }

    static byte[] Hex(string h)
    {
        byte[] b = new byte[h.Length / 2];
        for (int i = 0; i < b.Length; i++)
            b[i] = Convert.ToByte(h.Substring(i * 2, 2), 16);
        return b;
    }

    // The relay log's line, 2026-10-25 16:12:13: "S>C 1A000E4647DA850E7609B1004907", the box at 2481,73,7.
    static void BoxDrawn()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            string key = f.Server + "tools.mining.box", at = f.Server + "tools.mining.box-at";
            string was = f.Cfg.Get(key, ""), wasAt = f.Cfg.Get(at, "");
            try
            {
                f.Cfg.Set(key, "4647DA85");
                f.Cfg.Set(at, "");
                s.ServerSends(Packets.LoginConfirm(1000, 1000, 0));
                s.ServerSends(Hex("1A000E4647DA860E7609B1004907"));
                s.Clock.Sleep(1);
                Check.Equal("", f.Cfg.Get(at, ""), "another item's draw: nothing kept");
                s.ServerSends(Hex("1A000E4647DA850E7609B1004907"));
                s.Clock.Sleep(1);
                Check.Equal("4647DA85|2481|73|7", f.Cfg.Get(at, ""), "the box's draw kept");
            }
            finally
            {
                f.Cfg.Set(key, was);
                f.Cfg.Set(at, wasAt);
            }
        });
    }

    // uo1998 2026-10-25 16:24:53: the trip at the house with the box drawn there went to the vendor and the bank rune.
    static void TripTakesFromBox()
    {
        string? near = TripFrom(1, out int nearLifts, out int nearCasts, out int nearShovels);
        Check.Equal(null, near, "the box beside: the trip's answer");
        Check.Equal(2, nearShovels, "both shovels from the box in the pack");
        Check.True(nearLifts >= 2, "lifted from the box: " + nearLifts);
        Check.Equal(0, nearCasts, "no recall");
        string? far = TripFrom(AssistFeatures.CircuitReach + 1, out int farLifts, out int farCasts, out int _);
        Check.Equal("no shovel left, and no bank rune to buy shovels by", far, "a box past CircuitReach");
        Check.Equal(0, farLifts, "nothing lifted");
        Check.Equal(0, farCasts, "no recall");
    }

    // Kwik at 100,100 with no shovel, the box (2 shovels inside) east by east, no bag, no vendor, no bank rune.
    static string? TripFrom(int east, out int lifts, out int casts, out int shovels)
    {
        const uint Box = 0x45E000A0, S1 = 0x45E000A1, S2 = 0x45E000A2;
        string? why = "not run";
        int l = 0, c = 0, n = 0;
        TestMap map = new();
        map.Run(() => RecallTests.WithFeatures((s, f) =>
        {
            string[] keys = { "tools.mining.box", "tools.mining.bag", "tools.mining.box-at", "mine.bank-rune" };
            string[] was = new string[keys.Length];
            for (int i = 0; i < keys.Length; i++)
                was[i] = f.Cfg.Get(f.Server + keys[i], "");
            try
            {
                f.Cfg.Set(f.Server + "tools.mining.box", Box.ToString("X"));
                f.Cfg.Set(f.Server + "tools.mining.bag", "");
                f.Cfg.Set(f.Server + "tools.mining.box-at", "");
                f.Cfg.Set(f.Server + "mine.bank-rune", "");
                StepShard step = new(s, map, 100, 100);
                s.Answer = p =>
                {
                    if (p[0] == 0x08 && U32(p, 10) == Pack)
                        return ImmutableArray.Create<byte[]>(Packets.Contained(U32(p, 1), 0x0F39, 1, Pack));
                    return p[0] == 0x07 ? ImmutableArray<byte[]>.Empty : step.Answer(p);
                };
                s.ServerSends(Packets.Wear(Pack, 0x0E75, 0x15, Packets.Player));
                s.ServerSends(Packets.Open(Pack));
                s.ServerSends(Packets.Contents(Pack));
                s.ServerSends(Packets.Ground(Box, 0x0E7D, 100 + east, 100, 0));
                s.ServerSends(Packets.Contained(S1, 0x0F39, 1, Box));
                s.ServerSends(Packets.Contained(S2, 0x0F39, 1, Box));
                s.ServerSends(Weighed(BaseWeight));
                s.Clock.Sleep(1);
                AssistFeatures.MineCave cave = new() { Name = "", X = 100, Y = 100, Radius = 24 };
                MethodInfo trip = typeof(AssistFeatures).GetMethod("MineResupply",
                    BindingFlags.NonPublic | BindingFlags.Instance)!;
                why = (string?)trip.Invoke(f, new object?[] { s.Link, cave, new[] { "1", "0", "0", "0", "0", "0" },
                    "backpack" });
                foreach (byte[] p in s.ServerGot)
                {
                    l += p[0] == 0x07 ? 1 : 0;
                    c += p[0] == 0x12 ? 1 : 0;
                }
                lock (s.Link.State.Gate)
                    foreach (AssistItem it in s.Link.State.Items.Values)
                        n += it.Container == Pack && it.Graphic == 0x0F39 ? 1 : 0;
            }
            finally
            {
                for (int i = 0; i < keys.Length; i++)
                    f.Cfg.Set(f.Server + keys[i], was[i]);
            }
        }));
        lifts = l;
        casts = c;
        shovels = n;
        return why;
    }

    static void CollectNowMine()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            f.MinePilesClear();
            Check.Equal("no ore piles dropped to collect", f.MineCollectNow(), "no piles");
        });
        // With no circuit: a pile of 12 at 102,103 carried to Ore to, no dig.
        Run(true, "ground|0|" + PlaceX + "|" + PlaceY + "|0", (s, f, seam) =>
        {
            const uint Left = 0x45E00051;
            lock (seam.Gate)
                seam.Ground = seam.Ground.Set(Left, new[] { 102, 103, 12 });
            s.ServerSends(Lying(Left, 12, 102, 103));
            s.Clock.Sleep(1);
            f.MinePileAdd(102, 103, 12);
            Check.Null(f.MineCollectNow(), "MineCollectNow with no circuit");
            for (int i = 0; i < 3000 && f.CircuitRunning; i++)
                s.Clock.Sleep(100);
            Check.True(f.CircuitStatus.Contains("collected"), "the collection alone: " + f.CircuitStatus);
            Check.Equal(0, seam.Digs, "nothing dug");
            lock (seam.Gate)
                Check.Equal(12, seam.OreAt(PlaceX, PlaceY), "the 12 at Ore to");
            Check.Equal(0, f.MinePileCount(), "the record empty");
        });
        // Mid-run with Collect at 1000: asked after the first put-down, the collection runs before the seam is dug
        // out (at the end, dig 9, without the ask).
        Run(true, "ground|0|" + PlaceX + "|" + PlaceY + "|0", (s, f, seam) =>
        {
            f.MineCollectAt = 1000;
            Check.Null(f.CircuitMine(), "CircuitMine");
            for (int i = 0; i < 3000 && f.CircuitRunning && f.MinePileCount() == 0; i++)
                s.Clock.Sleep(100);
            Check.Null(f.MineCollectNow(), "MineCollectNow while mining");
            for (int i = 0; i < 3000 && f.CircuitRunning; i++)
                s.Clock.Sleep(100);
            Check.True(seam.DigsAtPlace >= 3 && seam.DigsAtPlace < seam.SeamDigs,
                "collected before the seam ran out: at dig " + seam.DigsAtPlace);
        });
    }

    // 2026-10-25 16:48:57: the ore at 2439,91 beside Ore to at 2440,92, the stand its own tile, "no way".
    static void HopInReach()
    {
        Run(true, "ground|0|" + PlaceX + "|" + PlaceY + "|0", (s, f, seam) =>
        {
            const uint Near = 0x45E00052;
            int walks = 0;
            f.CollectWalk = (x, y) => { walks++; return "no way to " + x + "," + y; };
            s.ServerSends(Packets.Draw(PlaceX, PlaceY - 2, 0, 0));
            lock (seam.Gate)
                seam.Ground = seam.Ground.Set(Near, new[] { PlaceX, PlaceY - 1, 10 });
            s.ServerSends(Lying(Near, 10, PlaceX, PlaceY - 1));
            s.Clock.Sleep(1);
            f.MinePileAdd(PlaceX, PlaceY - 1, 10);
            Check.Null(f.MineCollectNow(), "MineCollectNow");
            for (int i = 0; i < 600 && f.CircuitRunning; i++)
                s.Clock.Sleep(100);
            Check.True(f.CircuitStatus.Contains("collected"), "collected: " + f.CircuitStatus);
            Check.Equal(0, walks, "no walk");
            lock (seam.Gate)
                Check.Equal(10, seam.OreAt(PlaceX, PlaceY), "the 10 at Ore to");
        });
    }

    static void OwnSettings()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            try
            {
                f.MineOreTo = "ground|0|1|2|3";
                f.MineAtFeet = true;
                f.MineCollectAt = 77;
                Check.Equal("ground|0|1|2|3", f.Cfg.Get(f.Server + "mine.ore-to", ""), "mine.ore-to");
                Check.Equal("on", f.Cfg.Get(f.Server + "mine.feet", ""), "mine.feet");
                Check.Equal(77, f.Cfg.GetInt(f.Server + "mine.collect-at", 0), "mine.collect-at");
                Check.False(f.LumberAtFeet, "the logger's At my feet untouched");
                Check.False(f.LumberLogsTo == "ground|0|1|2|3", "the logger's place untouched");
                Check.True(f.MineOreText().Contains("1,2,3") && f.MineOreText().Contains("at my feet first"),
                    "the words: " + f.MineOreText());
                Check.True(MineGather.IsOre(0x19B7) && MineGather.IsOre(0x19BA) && !MineGather.IsOre(0x19BB)
                    && !MineGather.IsOre(0x19B6), "the ore arts, at both edges");
            }
            finally
            {
                f.MineOreTo = "";
                f.MineAtFeet = false;
                f.MineCollectAt = 200;
            }
        });
    }
}
