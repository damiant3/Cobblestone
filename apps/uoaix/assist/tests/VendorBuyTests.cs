#nullable enable
using System.Collections.Immutable;
using System;
using System.IO;
using System.Text;

namespace Uoaix.Assist.Tests;

// A vendor's buy (AssistFeatures.VendorBuy) against a shard that keeps one (VendorShard): the whole stock or none
// (Damian, 2026-10-20: "it should only buy all the thread or no thread"), each limit at its edge.
public static class VendorBuyTests
{
    const uint Pack = 0x42BE4F7C;
    const uint Vendor = 0x00280EC0;
    const uint Box = 0x40280EC1;
    const uint Gold = 0x45D00001;
    const uint Thread = 0x45D00002;
    const uint Kits = 0x45D00003;
    const int ThreadArt = 0x0FA0;
    const int KitArt = 0x0F9D;

    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("vendor buy: 20 thread at 3 with exactly 60 gold are bought whole, one buy sent", WholeAtEdge),
        new TestCase("vendor buy: 59 gold for a stock of 60 buys none, says so, and sends no buy", OneGoldShort),
        new TestCase("vendor buy: room for 19 of a stock of 20 buys none and says so", RoomShort),
        new TestCase("vendor buy: the list runs in the reverse of the stock's order: thread at 3 beside kits at 30",
        ListReversed),
        new TestCase("vendor buy: a stock with none of the art is \"the vendor has none\"", NoneOfIt),
        new TestCase("vendor buy: no answer to \"vendor buy\" says so, and nothing is bought", NoAnswer),
        new TestCase("vendor buy: Morena in view and in hearing asked by name sells her whole stock", ByName),
        new TestCase("vendor buy: a name nobody in view or the atlas holds is not asked", NameUnknown),
        new TestCase("vendor buy: Morena 10 tiles off is walked to within hearing, then asked once and bought from",
        WalkedTo),
        new TestCase("vendor buy: Morena asked away from her shop goes home, is waited for, and asked once more there",
        GoesHome),
        new TestCase("vendor buy: a window with no kit shows the vendor without kits for 60 s, then no longer",
        ShownNoKit),
        new TestCase("vendor buy: a window holding kits, or no window at all, is not shown without kits", ShownKit));

    public static ImmutableArray<TestCase> Cases => s_cases;

    static bool IsThread(int g) => g == ThreadArt;

    static void Shop(int gold, Action<FakeShard, AssistFeatures, VendorShard> body)
    {
        RecallTests.WithFeatures((s, f) =>
        {
            s.ServerSends(Packets.LoginConfirm(1000, 1000, 0));
            s.ServerSends(Packets.Wear(Pack, 0x0E75, 0x15, Packets.Player));
            s.ServerSends(Packets.Open(Pack));
            s.ServerSends(Packets.Contents(Pack));
            s.Clock.Sleep(1);
            body(s, f, new VendorShard(s, Vendor, Box, 1001, 1000, Pack, Gold, gold));
        });
    }

    static int PackOf(FakeShard s, int art)
    {
        int n = 0;
        lock (s.Link.State.Gate)
        {
            foreach (AssistItem it in s.Link.State.Items.Values)
            {
                if (it.Container == Pack && it.Graphic == art)
                    n += it.Amount;
            }
        }
        return n;
    }

    static void WholeAtEdge()
    {
        Shop(60, (s, f, v) =>
        {
            v.Stock(Thread, ThreadArt, 20, 3, "Spool of thread");
            Check.Null(f.VendorBuy(IsThread, 999, out int bought, out int left, "vendor", int.MaxValue, true),
                "VendorBuy");
            Check.Equal(20, bought, "bought");
            Check.Equal(0, left, "left");
            Check.Equal(1, v.Buys, "one buy");
            Check.Equal(0, v.Gold, "the purse spent");
            Check.True(PackOf(s, ThreadArt) >= 20, "the thread in the pack: " + PackOf(s, ThreadArt));
        });
    }

    static void OneGoldShort()
    {
        Shop(59, (s, f, v) =>
        {
            v.Stock(Thread, ThreadArt, 20, 3, "Spool of thread");
            string? why = f.VendorBuy(IsThread, 999, out int bought, out int left, "vendor", int.MaxValue, true);
            Check.Equal(AssistFeatures.PurseShort + "the pack's 59 gold of the 60 for all 20; none bought", why,
                "VendorBuy");
            Check.Equal(0, bought, "bought");
            Check.Equal(0, v.Buys, "no buy sent");
        });
    }

    static void RoomShort()
    {
        Shop(100, (s, f, v) =>
        {
            v.Stock(Thread, ThreadArt, 20, 3, "Spool of thread");
            string? why = f.VendorBuy(IsThread, 19, out int bought, out int left, "vendor", int.MaxValue, true);
            Check.Equal(AssistFeatures.PurseShort + "room for 19 of the 20; none bought", why, "VendorBuy");
            Check.Equal(0, v.Buys, "no buy sent");
        });
    }

    static void ListReversed()
    {
        Shop(100, (s, f, v) =>
        {
            v.Stock(Thread, ThreadArt, 20, 3, "Spool of thread");
            v.Stock(Kits, KitArt, 2, 30, "Sewing kit");
            Check.Null(f.VendorBuy(IsThread, 999, out int bought, out int left, "vendor", int.MaxValue, true),
                "VendorBuy");
            Check.Equal(20, bought, "bought");
            Check.Equal(40, v.Gold, "charged 60, the thread's own price");
        });
    }

    static void NoneOfIt()
    {
        Shop(100, (s, f, v) =>
        {
            v.Stock(Kits, KitArt, 2, 30, "Sewing kit");
            Check.Equal("the vendor has none",
                f.VendorBuy(IsThread, 999, out int bought, out int left, "vendor", int.MaxValue, true), "VendorBuy");
            Check.Equal(0, v.Buys, "no buy sent");
        });
    }

    static int Said(FakeShard s, string text)
    {
        int n = 0;
        foreach (byte[] p in s.ServerGot)
        {
            if (p[0] == 0x03 && p.Length > 8 && Encoding.ASCII.GetString(p, 8, p.Length - 8).TrimEnd('\0') == text)
                n++;
        }
        return n;
    }

    static void ByName()
    {
        Shop(60, (s, f, v) =>
        {
            v.Stock(Thread, ThreadArt, 20, 3, "Spool of thread");
            Check.Null(f.VendorBuy(IsThread, 999, out int bought, out int left, VendorShard.Name, int.MaxValue, true),
                "VendorBuy");
            Check.Equal(20, bought, "bought");
            Check.Equal(1, Said(s, VendorShard.Name + " buy"), "asked once by name");
        });
    }

    static void NameUnknown()
    {
        Shop(60, (s, f, v) =>
        {
            v.Stock(Thread, ThreadArt, 20, 3, "Spool of thread");
            string? why = f.VendorBuy(IsThread, 999, out int bought, out int left, "Brian", int.MaxValue, true);
            Check.Equal("Brian is not known here", why, "VendorBuy");
            Check.Equal(0, Said(s, "Brian buy"), "never asked");
        });
    }

    // Over TestMap's open grass (its tiles end at 255): the steps answered by StepShard, the asks by VendorShard.
    static void WalkedTo()
    {
        TestMap map = new();
        map.Run(() => RecallTests.WithFeatures((s, f) =>
        {
            StepShard walk = new(s, map, 100, 100);
            s.ServerSends(Packets.Wear(Pack, 0x0E75, 0x15, Packets.Player));
            s.ServerSends(Packets.Open(Pack));
            s.ServerSends(Packets.Contents(Pack));
            s.Clock.Sleep(1);
            VendorShard v = new(s, Vendor, Box, 110, 100, Pack, Gold, 60);
            s.Answer = p => walk.Answer(p).AddRange(v.Answer(p));
            v.Stock(Thread, ThreadArt, 20, 3, "Spool of thread");
            Check.Null(f.VendorBuy(IsThread, 999, out int bought, out int left, VendorShard.Name, int.MaxValue, true),
                "VendorBuy");
            Check.Equal(20, bought, "bought");
            Check.True(Math.Max(Math.Abs(walk.X - 110), Math.Abs(walk.Y - 100)) <= AssistFeatures.VendorHear,
                "within hearing: " + walk.X + "," + walk.Y);
            Check.Equal(1, Said(s, VendorShard.Name + " buy"), "asked once, from there");
        }));
    }

    // A town with a tailor's shop (x 1001-1003) and a bank (x 997-999) beside the player at 1000,1000. The regions are
    // every suite's: they are unloaded after.
    static void GoesHome()
    {
        string path = Path.Combine(Path.GetTempPath(), "assist-tests-vendorhome-" + Environment.ProcessId + ".txt");
        File.WriteAllLines(path, new[]
        {
            "R 900 900 200 200 -128 127 0 CITY_TESTTOWN a b c d e Testtown",
            "R 1001 1000 3 3 -128 127 0 TAILOR_TESTTOWN a b c d e The Stitch",
            "R 997 999 3 3 -128 127 0 BANK_TESTTOWN a b c d e Testtown bank",
        });
        try
        {
            UoRegions.Load(path);
            RecallTests.WithFeatures((s, f) =>
            {
                s.ServerSends(Packets.LoginConfirm(1000, 1000, 0));
                s.ServerSends(Packets.Wear(Pack, 0x0E75, 0x15, Packets.Player));
                s.ServerSends(Packets.Open(Pack));
                s.ServerSends(Packets.Contents(Pack));
                s.Clock.Sleep(1);
                VendorShard v = new(s, Vendor + 1, Box + 1, 999, 1000, Pack, Gold, 60);
                v.GoesHomeTo = new[] { 1002, 1001 };
                v.Stock(Thread, ThreadArt, 20, 3, "Spool of thread");
                Check.Null(f.VendorBuy(IsThread, 999, out int bought, out int left, VendorShard.Name, int.MaxValue,
                    true), "VendorBuy");
                Check.Equal(20, bought, "bought");
                Check.Equal(2, Said(s, VendorShard.Name + " buy"), "asked again once, the answer asking for it");
                lock (f.Notes)
                    Check.True(f.Notes.Exists(n => n.Contains(VendorShard.Name + " is away from its shop in BANK")),
                        "noted away");
            });
        }
        finally
        {
            UoRegions.Load(path + ".none");
            File.Delete(path);
        }
    }

    static bool IsKit(int g) => g == KitArt;

    static void ShownNoKit()
    {
        Shop(60, (s, f, v) =>
        {
            v.Stock(Thread, ThreadArt, 20, 3, "Spool of thread");
            Check.Null(f.VendorBuy(IsThread, 999, out int bought, out int left, "vendor", int.MaxValue, true),
                "VendorBuy");
            Check.True(f.ShownWithout(Vendor, IsKit), "no kit in the window");
            Check.False(f.ShownWithout(Vendor, IsThread), "the thread was in it");
            s.Clock.Sleep(AssistFeatures.StockSeenMs - 1000);
            Check.True(f.ShownWithout(Vendor, IsKit), "still within the window's time");
            s.Clock.Sleep(2000);
            Check.False(f.ShownWithout(Vendor, IsKit), "past it: asked again");
        });
    }

    static void ShownKit()
    {
        Shop(100, (s, f, v) =>
        {
            Check.False(f.ShownWithout(Vendor, IsKit), "no window yet");
            v.Stock(Thread, ThreadArt, 20, 3, "Spool of thread");
            v.Stock(Kits, KitArt, 2, 3, "Sewing kit");
            Check.Null(f.VendorBuy(IsThread, 999, out int bought, out int left, "vendor", int.MaxValue, true),
                "VendorBuy");
            Check.False(f.ShownWithout(Vendor, IsKit), "kits in the window");
        });
    }

    static void NoAnswer()
    {
        Shop(100, (s, f, v) =>
        {
            v.Silent = true;
            v.Stock(Thread, ThreadArt, 20, 3, "Spool of thread");
            Check.Equal("no vendor answered \"vendor buy\"",
                f.VendorBuy(IsThread, 999, out int bought, out int left, "vendor", int.MaxValue, true), "VendorBuy");
            Check.Equal(0, v.Buys, "no buy sent");
        });
    }
}
