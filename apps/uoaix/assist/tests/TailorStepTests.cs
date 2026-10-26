#nullable enable
using System.Collections.Generic;
using System.Collections.Immutable;
using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace Uoaix.Assist.Tests;

// The home stop's choice of what to sew (AssistFeatures.SewSource; Damian, 2026-10-24: "use the most finished bits
// first... e.g. cut cloth, then bolts", each step ticked or not) and the tailoring step settings.
public static class TailorStepTests
{
    const int Cloth = 0x1766;
    const int Bolt = 0x0F95;

    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("steps: cut cloth before bolts, the largest pile of each; a pile under a shirt's cloth, set "
            + "aside or outside the house is passed", ClothFirst),
        new TestCase("steps: cloth unticked sews bolts; bolts unticked sews only cloth; both unticked sews nothing",
        Unticked),
        new TestCase("steps: every step on by default, each kept per server when unticked and ticked again", Settings),
        new TestCase("room: a thread stock past the carry limit with shirts aboard puts no shirt down; the stock is "
        + "bought whole and the thread past the limit goes down after", NoShirtShed),
        new TestCase("room: a stock past the room that the purse cannot pay for whole puts nothing down; one gold more "
        + "and the thread carried goes down for it", RoomOnlyPaid),
        new TestCase("room: a pile put down and sent back under a new serial is found on its tile; another tile's or "
        + "another art's is not", DumpedResent),
        new TestCase("pace: the next sew goes the longer of the pace and the busy after the kit's use", PaceOrBusy),
        new TestCase("pace: after a failure the busy runs from the last pick too; after a success it does not",
        AfterFailure),
        new TestCase("pace: a click after the kit's use holds the next sew its busy; one before it does not",
        AfterClick),
        new TestCase("pace: the sewing busy is 1020 ms unset, apart from the shared busy", SewBusyDefault),
        new TestCase("pace: \"You create\" is the shirt made at once, with no wait for a quality line", MadeAtOnce),
        new TestCase("reach: a shop not reached is passed over, paused 5 minutes the second time, BROKEN the third",
        Unreached),
        new TestCase("reach: a stop, a takeover or a closed link while going to a shop ends the circuit, nothing paused",
        UnreachedEnds),
        new TestCase("reach: a blocked rune is told in red to the client, noted, and recorded with its landing and shop",
        BlockedReported),
        new TestCase("home: a pack full of shirts with no thread or cloth has no task at home; thread, cloth or room "
        + "each is one", NoHomeTask),
        new TestCase("home: the bank stop before the overflow names only its own tasks; the house rune only when it "
        + "is not in the pack, none for no stop", BankErrands),
        new TestCase("home: homespun goes home with the shirts gone or after a round that did something; a round of "
        + "every shop with nothing done never goes home", HomespunGoesHome),
        new TestCase("rest: ends at the soonest held tailor's due time when sooner than the two minutes, never under the "
        + "floor, the two minutes with none due", RestEnd),
        new TestCase("bank by rune: a recall only when it saves more than RecallTiles of walking, or the town has no "
        + "bank; never with no rune landing by a bank", BankByRune),
        new TestCase("regs plan: the recalls summed up to and including the first stop by a bank; none in the plan is "
        + "no bound", RecallsToBank),
        new TestCase("mana: short of mana the order goes on to a town of two tailors only when one of its shops has "
        + "work, Jhelom and Britain only, never Vesper; none found keeps the next shop", TwoTailorPick),
        new TestCase("mana: to a tailor shop of the town the player stands in, Jhelom and Britain always the walk, "
        + "Vesper the run under two recalls' mana and the recall at two or more; anywhere else no rule", WalkInTown),
        new TestCase("stall: a pass the same as the one before counts, any change resets; two rounds' worth of 12 "
        + "shops is 24 passes", Stall),
        new TestCase("home: a vendor's home region of no low bound (z -127) is walked to on any floor; a region with "
        + "one, on its floor", HomeFloor));

    static void HomeFloor()
    {
        Check.Equal(AssistFeatures.AnyFloor, AssistFeatures.HomeFloor(new[] { 652, 2184, -127, 127 }),
            "Lynn's shop region, z -127 to 127: any floor");
        Check.Equal(20, AssistFeatures.HomeFloor(new[] { 652, 2184, 20, 40 }), "an upstairs region: its floor");
        Check.Equal(0, AssistFeatures.HomeFloor(new[] { 652, 2184, 0, 127 }), "a ground-floor region: z 0");
    }

    // The 18:38 lap: shops 11 and 12 passed with Kwik on 1470,1691 and nothing changing.
    static void Stall()
    {
        string p = "1470,1691 473 551 312 78 38";
        Check.Equal(0, AssistFeatures.StallCount(null!, p, 5), "the first pass");
        int n = 0;
        for (int i = 0; i < 24; i++)
            n = AssistFeatures.StallCount(p, p, n);
        Check.Equal(24, n, "24 the same");
        Check.True(n >= AssistFeatures.StallPasses(12), "stops at two rounds' worth");
        Check.False(23 >= AssistFeatures.StallPasses(12), "not one pass sooner");
        Check.Equal(0, AssistFeatures.StallCount(p, "1470,1691 474 551 312 77 38", n), "a sale resets");
        Check.Equal(0, AssistFeatures.StallCount(p, "1556,1656 473 551 312 78 38", n), "a move resets");
        Check.Equal(0, AssistFeatures.StallCount(p, "1470,1691 473 551 312 78 57", n), "thread bought resets");
    }

    public static ImmutableArray<TestCase> Cases => s_cases;

    static AssistItem It(uint serial, int graphic, int amount,
        int x) => new() { Serial = serial, Graphic = graphic, Amount = amount, X = x, Y = 100 };

    static AssistItem? Pick(List<AssistItem> floor, bool cloth, bool bolts, Set<uint> spent, out bool fromCloth) =>
        AssistFeatures.SewSource(floor, (x, y) => x < 200, spent, cloth, bolts, g => g == Cloth, g => g == Bolt,
        out fromCloth);

    // Kwik at 359 of a 370-stone limit with 10 shirts and no thread, a stock of 20 thread (2 stones) asked for room
    // (Damian, 2026-10-25: "buy the thread, become encumbered, but move the thread to the floor instead of the
    // product"; uo1998 09:47:20 to 09:47:28, seven shirts put down one by one for Morena's 91 thread).
    static void NoShirtShed()
    {
        const uint Pack = 0x42BE4F7C;
        RecallTests.WithFeatures((s, f) =>
        {
            s.ServerSends(Packets.LoginConfirm(1000, 1000, 0));
            s.ServerSends(Packets.Wear(Pack, 0x0E75, 0x15, Packets.Player));
            s.ServerSends(Packets.Open(Pack));
            s.ServerSends(Packets.Contents(Pack));
            for (uint i = 0; i < 10; i++)
                s.ServerSends(Packets.Contained(0x40007000 + i, 0x1EFD, 1, Pack));
            byte[] st = Packets.Status(51);
            st[64] = 359 >> 8;
            st[65] = 359 & 0xFF;
            s.ServerSends(st);
            s.Clock.Sleep(1);
            MethodInfo room = typeof(AssistFeatures).GetMethod("MakeRoom", BindingFlags.NonPublic | BindingFlags.Instance)!;
            string? w = (string?)room.Invoke(f, new object?[] { s.Link, null, "Testtown", 20 });
            int lifts = 0;
            foreach (byte[] p in s.ServerGot)
                if (p[0] == 0x07)
                    lifts++;
            Check.Equal(0, lifts, "nothing lifted to put down");
            Check.True(w is not null && w.Contains("the stock bought whole"), "short, and the stock bought all the same: "
                + w);
        });
    }

    // Kwik at 368 of 370 stones with 30 thread and Morena's 100 at 3 (uo1998 2026-10-25 10:40:00, Jhelom: 82 thread put
    // down for Ella's 100, then 276 gold of the 300 and none bought).
    static void RoomOnlyPaid()
    {
        Check.Equal(0, ThreadLifts(299), "299 gold of the 300: nothing put down");
        Check.True(ThreadLifts(300) > 0, "300 gold: the carried thread lifted to go down");
    }

    static int ThreadLifts(int gold)
    {
        const uint Pack = 0x42BE4F7C, Vendor = 0x00280EC0, Box = 0x40280EC1, Gold = 0x45D00001, Carried = 0x45D00010;
        string path = Path.Combine(Path.GetTempPath(), "assist-tests-roomonlypaid-" + Environment.ProcessId + ".txt");
        File.WriteAllLines(path, new[]
        {
            "R 900 900 200 200 -128 127 0 CITY_TESTTOWN a b c d e Testtown",
            "R 995 995 12 12 -128 127 0 CLOTHIER_TESTTOWN a b c d e The Seam",
        });
        int lifts = -1;
        try
        {
            RecallTests.WithFeatures((s, f) =>
            {
                UoRegions.Load(path);
                HomespunWorldTests.ClearRecords(f);
                f.Homespun = true;
                s.ServerSends(Packets.LoginConfirm(1000, 1000, 0));
                s.ServerSends(Packets.Wear(Pack, 0x0E75, 0x15, Packets.Player));
                s.ServerSends(Packets.Open(Pack));
                s.ServerSends(Packets.Contents(Pack));
                s.ServerSends(Packets.Contained(Carried, 0x0FA0, 30, Pack));
                byte[] st = Packets.Status(51);
                st[64] = 368 >> 8;
                st[65] = 368 & 0xFF;
                s.ServerSends(st);
                s.Clock.Sleep(1);
                VendorShard v = new(s, Vendor, Box, 1001, 1000, Pack, Gold, gold);
                v.Stock(0x45D00002, 0x0FA0, 100, 3, "Spool of thread");
                s.ServerSends(Packets.Speech(Vendor, VendorShard.Name, VendorShard.Name + " the tailor", 6));
                s.Clock.Sleep(1);
                UoRegions.Shop shop = UoRegions.AllShopsIn("Testtown")[0];
                MethodInfo buy = typeof(AssistFeatures).GetMethod("BuyShopThread",
                    BindingFlags.NonPublic | BindingFlags.Instance)!;
                buy.Invoke(f, new object?[] { s.Link, shop, "Testtown", 0, false });
                lifts = 0;
                foreach (byte[] p in s.ServerGot)
                    if (p[0] == 0x07 && p.Length >= 5 && (uint)(p[1] << 24 | p[2] << 16 | p[3] << 8 | p[4]) == Carried)
                        lifts++;
                f.Homespun = false;
            });
        }
        finally
        {
            UoRegions.Load(path + ".none");
            File.Delete(path);
        }
        return lifts;
    }

    // uo1998 2026-10-25 10:39:58: 476C218C lifted, 82 thread dropped at 1362,3779, and 476C45A0 sent there with its 82.
    static void DumpedResent()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            s.ServerSends(Packets.LoginConfirm(1360, 3779, 0));
            s.ServerSends(Packets.Ground(0x476C45A0, 0x0FA0, 1362, 3779, 0));
            s.ServerSends(Packets.Ground(0x476C45A1, 0x0FA0, 1363, 3779, 0));
            s.ServerSends(Packets.Ground(0x476C45A2, 0x1EFD, 1362, 3780, 0));
            s.Clock.Sleep(1);
            Type dumped = typeof(AssistFeatures).GetNestedType("Dumped", BindingFlags.NonPublic)!;
            MethodInfo pile = typeof(AssistFeatures).GetMethod("DumpedPile", BindingFlags.NonPublic | BindingFlags.Static)!;
            AssistItem? At(uint serial, int graphic, int x, int y)
            {
                object d = Activator.CreateInstance(dumped)!;
                dumped.GetField("Serial")!.SetValue(d, serial);
                dumped.GetField("Graphic")!.SetValue(d, graphic);
                dumped.GetField("Amount")!.SetValue(d, 82);
                dumped.GetField("X")!.SetValue(d, x);
                dumped.GetField("Y")!.SetValue(d, y);
                return (AssistItem?)pile.Invoke(null, new object?[] { s.Link, d });
            }
            Check.Equal(0x476C45A0u, At(0x476C218C, 0x0FA0, 1362, 3779)?.Serial ?? 0, "the resent pile on its tile");
            Check.Equal(0x476C45A1u, At(0x476C45A1, 0x0FA0, 1362, 3779)?.Serial ?? 0, "its own serial first");
            Check.Null(At(0x476C218C, 0x0FA0, 1364, 3779), "no thread on that tile: none");
            Check.Null(At(0x476C218C, 0x0FA0, 1362, 3780), "a shirt on that tile is not the thread");
        });
    }

    static void ClothFirst()
    {
        List<AssistItem> floor = new()
        {
            It(1, Bolt, 300, 100), It(2, Cloth, 40, 101), It(3, Cloth, 90, 102),
                It(4, Cloth, AssistFeatures.ShirtCloth - 1, 103),
            It(5, Cloth, 500, 250), It(6, Cloth, 200, 104),
        };
        Set<uint> spent = Set<uint>.Of(6);
        AssistItem? got = Pick(floor, true, true, spent, out bool fromCloth);
        Check.True(got is not null && got.Serial == 3 && fromCloth,
            "the largest cut cloth in the house, not set aside: " + got?.Serial);
        floor.RemoveAll(i => i.Serial == 2 || i.Serial == 3);
        got = Pick(floor, true, true, spent, out fromCloth);
        Check.True(got is not null && got.Serial == 1 && !fromCloth,
            "only a short pile, an aside one and one outside left: the bolts: " + got?.Serial);
        floor.Add(It(7, Cloth, AssistFeatures.ShirtCloth, 105));
        got = Pick(floor, true, true, spent, out fromCloth);
        Check.True(got is not null && got.Serial == 7 && fromCloth,
            "a pile of exactly a shirt's cloth is sewn: " + got?.Serial);
        Check.True(Pick(new List<AssistItem>(), true, true, spent, out fromCloth) is null && !fromCloth,
            "an empty floor: none");
    }

    static void Unticked()
    {
        List<AssistItem> floor = new()
        { It(1, Bolt, 300, 100), It(2, Bolt, 30, 101), It(3, Cloth, 90, 102) };
        Set<uint> none = Set<uint>.Empty;
        AssistItem? got = Pick(floor, false, true, none, out bool fromCloth);
        Check.True(got is not null && got.Serial == 1 && !fromCloth,
            "cloth unticked: the largest bolts: " + got?.Serial);
        got = Pick(floor, true, false, none, out fromCloth);
        Check.True(got is not null && got.Serial == 3 && fromCloth, "bolts unticked: the cloth: " + got?.Serial);
        floor.RemoveAll(i => i.Serial == 3);
        Check.True(Pick(floor, true, false, none, out fromCloth) is null, "bolts unticked and no cloth: none");
        Check.True(Pick(floor, false, false, none, out fromCloth) is null, "both unticked: none");
    }

    static readonly DateTime s_used = new(2026, 10, 24, 13, 0, 0, DateTimeKind.Utc);
    static readonly DateTime s_never = DateTime.MinValue;

    static DateTime At(int ms) => s_used.AddMilliseconds(ms);

    static void PaceOrBusy()
    {
        Check.Equal(At(1000), AssistFeatures.SewDue(s_used, 700, 1000, false, s_never, s_never),
            "a pace under the busy waits the busy");
        Check.Equal(At(1500), AssistFeatures.SewDue(s_used, 1500, 1000, false, s_never, s_never),
            "a pace over the busy waits the pace");
        Check.Equal(At(1000), AssistFeatures.SewDue(s_used, 1000, 1000, false, s_never, s_never), "equal: either");
    }

    static void AfterFailure()
    {
        Check.Equal(At(1400), AssistFeatures.SewDue(s_used, 700, 1000, true, At(400), s_never),
            "failed: the busy from the pick");
        Check.Equal(At(1000), AssistFeatures.SewDue(s_used, 700, 1000, false, At(400), s_never),
            "made: the busy from the use alone");
        Check.Equal(At(1500), AssistFeatures.SewDue(s_used, 1500, 1000, true, At(400), s_never),
            "failed under a longer pace: the pace");
    }

    static void AfterClick()
    {
        Check.Equal(At(1500), AssistFeatures.SewDue(s_used, 700, 1000, false, s_never, At(500)),
            "the door clicked after the use");
        Check.Equal(At(1000), AssistFeatures.SewDue(s_used, 700, 1000, false, s_never, At(-500)),
            "a click before the use");
    }

    static void SewBusyDefault()
    {
        string settings = Path.Combine(Path.GetTempPath(),
            "assist-tests-sewbusy-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("N") + ".txt");
        AssistTimings t = new(new AssistSettings(settings), () => "", name => settings + "-" + name, _ => { },
            _ => { }, new VirtualClock(s_used));
        Check.Equal(1020, t.Ms("busy:sew"), "busy:sew");
        t.Set("busy", 700);
        Check.Equal(1020, t.Ms("busy:sew"), "busy:sew with the shared busy set");
    }

    static void MadeAtOnce()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            LoopKind? l = AssistFeatures.Find("Tailoring (cloth, then bolts)");
            Check.True(l is not null, "the tailoring loop");
            // As HomeSew starts: the stop a login sets cleared.
            f.idleStop = false;
            Heard mark = new(s.Link.State);
            s.ServerSends(Packets.Speech(0xFFFFFFFF, "System",
                "You create the fancy shirt and put it in your backpack.", 0));
            DateTime from = s.Clock.UtcNow;
            Check.Equal("made", f.TailorResult(s.Link, l!, mark, 5000), "the result");
            Check.True((s.Clock.UtcNow - from).TotalMilliseconds < 300,
                "waited " + (s.Clock.UtcNow - from).TotalMilliseconds + " ms");
        });
    }

    static UoRegions.Shop Clothiers() => new() { Text = "Test Clothiers " + Guid.NewGuid().ToString("N"), X = 1000,
        Y = 1000, W = 4, H = 4 };

    static void Unreached()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            UoRegions.Shop shop = Clothiers();
            const string blocked = "the landing is blocked";
            Check.Null(f.Unreached(shop, blocked), "first: passed over, the circuit goes on");
            Check.False(f.ShopPaused(shop), "first: tried again next pass");
            Check.Null(f.Unreached(shop, blocked), "second: passed over");
            Check.True(f.ShopPaused(shop), "second: paused");
            s.Clock.Sleep(AssistFeatures.FailPauseMinutes * 60000 + 1000);
            Check.False(f.ShopPaused(shop), "the pause over");
            Check.Null(f.Unreached(shop, blocked), "third: passed over");
            s.Clock.Sleep(AssistFeatures.FailPauseMinutes * 60000 + 1000);
            Check.True(f.ShopPaused(shop), "third: BROKEN, past any pause");
            f.BrokenClear("reach " + shop.Text);
            Check.False(f.ShopPaused(shop), "cleared");
        });
    }

    static void UnreachedEnds()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            UoRegions.Shop shop = Clothiers();
            foreach (string why in new[] { "stopped", "the player took over", "the connection is closed" })
            {
                Check.Equal("to " + shop.Text + ": " + why, f.Unreached(shop, why), why);
                Check.False(f.ShopPaused(shop), why + ": not paused");
            }
        });
    }

    static void NoHomeTask()
    {
        Check.True(AssistFeatures.NoHomeTask(0, 0, 80, true), "full of shirts, nothing else: no task");
        Check.False(AssistFeatures.NoHomeTask(10, 0, 80, true), "thread to store");
        Check.False(AssistFeatures.NoHomeTask(0, 1, 80, true), "cloth to put down");
        Check.False(AssistFeatures.NoHomeTask(0, 0, 80, false), "room to sew");
        Check.False(AssistFeatures.NoHomeTask(0, 0, 0, true), "full of something else, no shirts");
    }

    static void BankErrands()
    {
        Check.Null(AssistFeatures.BankErrands(false, false, false, false),
            "the rune in the pack and nothing else: no stop");
        Check.Equal("the recall reagents", AssistFeatures.BankErrands(false, false, true, false),
            "the rune in the pack, reagents low");
        Check.Equal("the house rune", AssistFeatures.BankErrands(true, false, false, false),
            "the rune in the bank box");
        Check.Equal("the gold over the bank-over setting", AssistFeatures.BankErrands(false, true, false, false),
            "gold over");
        Check.Equal("the purse", AssistFeatures.BankErrands(false, false, false, true), "the purse short");
        Check.Equal("the house rune, the gold over the bank-over setting, the recall reagents, the purse",
            AssistFeatures.BankErrands(true, true, true, true), "every task, in order");
    }

    static void RestEnd()
    {
        DateTime now = new(2026, 10, 25, 23, 30, 5, DateTimeKind.Utc);
        int wait = AssistFeatures.TopWaitMs;
        Check.Equal(now.AddMilliseconds(wait), AssistFeatures.RestEnd(now, wait, DateTime.MaxValue), "none due");
        Check.Equal(now.AddMinutes(1), AssistFeatures.RestEnd(now, wait, now.AddMinutes(1)), "due in a minute");
        Check.Equal(now.AddMilliseconds(wait), AssistFeatures.RestEnd(now, wait, now.AddMinutes(3)),
            "due past the two minutes: the two minutes");
        Check.Equal(now.AddSeconds(AssistFeatures.RestFloorSeconds), AssistFeatures.RestEnd(now, wait, now.AddSeconds(2)),
            "due at once: the floor");
        Check.Equal(now.AddSeconds(AssistFeatures.RestFloorSeconds), AssistFeatures.RestEnd(now, wait, now.AddMinutes(-5)),
            "due in the past: the floor");
    }

    static void BankByRune()
    {
        int r = AssistFeatures.RecallTiles;
        Check.True(!AssistFeatures.BankByRune(83, 35), "Magincia, 2026-10-26: 48 saved of 83, walked");
        Check.True(!AssistFeatures.BankByRune(r + 35, 35), "exactly RecallTiles saved: walked");
        Check.True(AssistFeatures.BankByRune(r + 36, 35), "one tile over RecallTiles saved: recalled");
        Check.True(AssistFeatures.BankByRune(int.MaxValue, 35), "no bank in this town: recalled");
        Check.True(!AssistFeatures.BankByRune(int.MaxValue, int.MaxValue), "no rune lands by a bank: none");
        Check.True(!AssistFeatures.BankByRune(500, int.MaxValue), "a far bank, no rune by a bank: walked");
    }

    static void RecallsToBank()
    {
        Check.Equal(0, AssistFeatures.RecallsToBank(new[] { 0 }, new[] { true }), "home's town shop by a bank");
        Check.Equal(4, AssistFeatures.RecallsToBank(new[] { 0, 2, 2, 2 }, new[] { false, false, true, false }),
            "Brian, a stop, then Slade by the bank: 4");
        Check.Equal(2, AssistFeatures.RecallsToBank(new[] { 2, 2 }, new[] { true, true }), "the first bank counts");
        Check.Equal(int.MaxValue, AssistFeatures.RecallsToBank(new[] { 2, 2 }, new[] { false, false }),
            "no bank in the plan");
        Check.Equal(int.MaxValue, AssistFeatures.RecallsToBank(new int[0], new bool[0]), "no plan");
    }

    // The order of 12; the 16:09 loop was shirts 78, a lap of 12, idle 12.
    static void HomespunGoesHome()
    {
        Check.False(AssistFeatures.HomespunGoesHome(78, 12, 12, 12),
            "a round of every shop with nothing done: the rest, not home");
        Check.False(AssistFeatures.HomespunGoesHome(78, 12, 13, 12), "past a round with nothing done");
        Check.True(AssistFeatures.HomespunGoesHome(78, 12, 11, 12), "a round in which something was done");
        Check.True(AssistFeatures.HomespunGoesHome(78, 12, 0, 12), "a round that sold");
        Check.False(AssistFeatures.HomespunGoesHome(78, 11, 0, 12), "the round not finished");
        Check.True(AssistFeatures.HomespunGoesHome(0, 3, 0, 12), "the shirts gone");
        Check.True(AssistFeatures.HomespunGoesHome(0, 12, 12, 12), "the shirts gone, whatever the round");
    }

    // The day's order: Moonglow, Magincia, Trinsic, Skara Brae, Ocllo, Nujel'm, Jhelom twice, Vesper twice, Britain
    // twice.
    static readonly ImmutableArray<string> s_towns = [ "Moonglow", "Magincia", "Trinsic", "Skara Brae", "Ocllo",
        "Nujel'm", "Jhelom",
        "Jhelom", "Vesper", "Vesper", "Britain", "Britain" ];

    static void TwoTailorPick()
    {
        Check.Equal(6, AssistFeatures.TwoTailorPick(s_towns, 0, i => i < 8),
            "the 16:09 state: Vesper and Britain without work, Jhelom with Kimmie and Ella buying");
        Check.Equal(0, AssistFeatures.TwoTailorPick(s_towns, 0, i => i < 6),
            "Vesper, Britain and Jhelom without work: Moonglow kept");
        Check.Equal(0, AssistFeatures.TwoTailorPick(s_towns, 0, i => i == 9),
            "Vesper with work in its south shop: no jump target, Moonglow kept");
        Check.Equal(10, AssistFeatures.TwoTailorPick(s_towns, 0, i => i == 9 || i == 11),
            "Vesper and Britain with work: Britain");
        Check.Equal(6, AssistFeatures.TwoTailorPick(s_towns, 0, i => i == 10 || i == 7),
            "Jhelom with work in its north shop before Britain: Jhelom's first");
        Check.Equal(10, AssistFeatures.TwoTailorPick(s_towns, 0, i => i == 10),
            "Britain with work, Jhelom without: Britain");
        Check.Equal(9, AssistFeatures.TwoTailorPick(s_towns, 9, i => i == 8),
            "at Vesper's second shop with work in Vesper only: kept");
        Check.Equal(10, AssistFeatures.TwoTailorPick(s_towns, 9, i => i == 11),
            "at Vesper's second shop with no work in Vesper: Britain");
        Check.Equal(3, AssistFeatures.TwoTailorPick(s_towns, 3, i => false), "no work anywhere: the next shop kept");
        Check.Equal(1, AssistFeatures.TwoTailorPick(new string?[] { null, "Jhelom", "Jhelom" }!, 0, i => true),
            "a shop in no town passed, Jhelom's first taken");
        Check.Equal(0, AssistFeatures.TwoTailorPick(new[] { "Britain", "Britain", "Moonglow" }, 0, i => true),
            "a town of two at the order's first place");
        Check.Equal(1, AssistFeatures.TwoTailorPick(new[] { "Moonglow", "Vesper", "Vesper" }, 1, i => true),
            "Vesper's two shops alone: the next shop kept");
        // 18:38: The Right Fit (10) idle by its check, The Lord's Clothiers (11) found work by its check and its visit
        // did nothing; both are in the round's tried set, so the order goes on to Moonglow.
        Set<int> tried = Set<int>.Of(10, 11);
        Check.Equal(0, AssistFeatures.TwoTailorPick(s_towns, 0, AssistFeatures.RoundWork(tried, i => i == 11)),
            "the 18:38 state: Britain tried, Moonglow kept");
        Check.Equal(10,
            AssistFeatures.TwoTailorPick(s_towns, 0, AssistFeatures.RoundWork(Set<int>.Empty, i => i == 11)),
            "the same shop untried: Britain");
        Check.Equal(6,
            AssistFeatures.TwoTailorPick(s_towns, 0, AssistFeatures.RoundWork(tried, i => i == 11 || i == 9 || i == 7)),
            "Britain tried, Vesper south and Jhelom north with work: Jhelom");
    }

    // RecallMana 11 (Recall, the third circle): two recalls are 22.
    static void WalkInTown()
    {
        Check.True(AssistFeatures.WalkInTown("Vesper", true, 21, 11), "Vesper, 21 mana: the run");
        Check.False(AssistFeatures.WalkInTown("Vesper", true, 22, 11), "Vesper, 22 mana: the recall");
        Check.True(AssistFeatures.WalkInTown("Vesper", true, 0, 11), "Vesper, no mana: the run");
        Check.True(AssistFeatures.WalkInTown("Jhelom", true, 100, 11), "Jhelom, full mana: the walk");
        Check.True(AssistFeatures.WalkInTown("Britain", true, 100, 11), "Britain, full mana: the walk");
        Check.False(AssistFeatures.WalkInTown("Britain", false, 0, 11), "not a tailor shop of this town: no rule");
        Check.False(AssistFeatures.WalkInTown(null!, false, 0, 11), "in no town: no rule");
    }

    static void BlockedReported()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            UoRegions.Shop shop = Clothiers();
            const uint rune = 0x44DE6EA5;
            File.WriteAllLines(f.ServerFile("rune-landings"), new[] { rune.ToString("X") + "\t1438\t1693" });
            string record = f.ServerFile("blocked-runes");
            if (File.Exists(record))
                File.Delete(record);
            f.ReportBlockedRune(s.Link, rune, shop);
            s.Clock.Sleep(50);
            string call = "RUNE BLOCKED: the rune 44DE6EA5 for " + shop.Text + " (1438,1693); mark a better landing";
            lock (f.Notes)
                Check.True(f.Notes.Exists(n => n.Contains(call)), "noted");
            bool told = false;
            foreach (byte[] p in s.ClientGot)
            {
                if (p[0] == 0x1C && Encoding.ASCII.GetString(p).Contains(call))
                    told = true;
            }

            Check.True(told, "told to the client");
            string[] lines = File.ReadAllLines(record);
            Check.Equal(1, lines.Length, "one record line");
            Check.True(lines[0].EndsWith("\t44DE6EA5\t1438,1693\t" + shop.Text), "the record: " + lines[0]);
        });
    }

    static void Settings()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            try
            {
                Check.True(f.StepBuyThread && f.StepWeave && f.StepBolts && f.StepCloth, "all on by default");
                f.StepBuyThread = false;
                f.StepWeave = false;
                f.StepBolts = false;
                f.StepCloth = false;
                Check.True(!f.StepBuyThread && !f.StepWeave && !f.StepBolts && !f.StepCloth, "each kept unticked");
                Check.Equal("off", f.Cfg.Get(f.Server + "circuit.step.weave", ""), "kept per server");
                f.StepWeave = true;
                Check.True(f.StepWeave && !f.StepBolts, "ticked again alone");
            }
            finally
            {
                foreach (string k in new[] { "buy", "weave", "bolts", "cloth" })
                    f.Cfg.Set(f.Server + "circuit.step." + k, "on");
            }
        });
    }
}
