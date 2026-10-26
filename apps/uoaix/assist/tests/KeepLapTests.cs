#nullable enable
using System.Collections.Generic;
using System.Collections.Immutable;
using System;
using System.IO;
using System.Text;

namespace Uoaix.Assist.Tests;

// Keep thread's lap (AssistFeatures.CircuitKeep, AssistTesting.md T3), driven through its public start against the fake
// shard's pieces.
public static class KeepLapTests
{
    const uint Pack = 0x42BE4F7C;

    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("keep: with no thread vendor in an approved shop the circuit ends at once, says so, and asks "
            + "nobody", NoVendor),
        new TestCase("keep: the tailor in the town's one shop is asked by name and her whole stock bought on the "
        + "first lap", FirstLap),
        new TestCase("keep: the tailor bought out is not asked again before she is due; the circuit waits and says so",
        NotBeforeDue),
        new TestCase("keep: with the tailor runes bag set, a tailor in a shop it holds no rune for is never asked",
        BagApproves),
        new TestCase("keep: a tailor in another town is reached by the rune labelled for her shop, cast once, and "
        + "bought out", OtherTown),
        new TestCase("keep: a stone past the carry limit after the buy puts 10 thread down beside the player, and "
        + "none comes back", StashPastLimit),
        new TestCase("keep: at the carry limit after the buy nothing is put down", StashAtLimit),
        new TestCase("keep: over a map the stash goes on the shop's most hidden spot, walked to", StashOnSpot),
        new TestCase("keep: a spot whose drop comes back is refused and the next spot taken", StashSpotRefused),
        new TestCase("keep: room after the stash lifts it back into the pack at the lap's end", StashLiftedBack),
        new TestCase("keep: a restock seen during the wait ends it, is said and kept, and the tailor is asked at once",
        RestockEndsWait),
        new TestCase("keep: a keeper titled the blacksmith in the tailor shop is never asked for thread; the circuit "
        + "ends at once and says so", () => Titled("blacksmith", false, 40)),
        new TestCase("keep: a keeper titled the weaver in the tailor shop is never asked for thread; the circuit ends "
        + "at once and says so", () => Titled("weaver", false, 42)),
        new TestCase("keep: a keeper titled the tailor is asked by name and bought out on the first lap",
        () => Titled("tailor", true, 44)));

    public static ImmutableArray<TestCase> Cases => s_cases;

    static int Said(FakeShard s)
    {
        int n = 0;
        foreach (byte[] p in s.ServerGot)
            n += p[0] == 0x03 ? 1 : 0;
        return n;
    }

    const uint Vendor = 0x00280F00;
    const uint Box = 0x40280F01;
    const uint Gold = 0x45C00001;
    const uint Thread = 0x45C00002;

    // A town whose one tailor's shop (x 998-1004, y 998-1004) holds the player and Morena; no runes bag, so every
    // tailor shop is approved. The regions are every suite's: unloaded after.
    static void FirstLap()
    {
        string path = Path.Combine(Path.GetTempPath(), "assist-tests-keeplap-" + Environment.ProcessId + ".txt");
        File.WriteAllLines(path, new[]
        {
            "R 900 900 200 200 -128 127 0 CITY_TESTTOWN a b c d e Testtown",
            "R 998 998 7 7 -128 127 0 CLOTHIER_TESTTOWN a b c d e The Stitch",
        });
        try
        {
            RecallTests.WithFeatures((s, f) =>
            {
                UoRegions.Load(path);
                f.Cfg.Set(f.Server + "runes.shop", "");
                s.ServerSends(Packets.LoginConfirm(1000, 1000, 0));
                s.ServerSends(Packets.Wear(Pack, 0x0E75, 0x15, Packets.Player));
                s.ServerSends(Packets.Open(Pack));
                s.ServerSends(Packets.Contents(Pack));
                s.Clock.Sleep(1);
                VendorShard v = new(s, Vendor, Box, 1001, 1000, Pack, Gold, 500);
                v.Stock(Thread, 0x0FA0, 20, 3, "Spool of thread");
                Check.Null(f.CircuitKeep(), "CircuitKeep");
                bool bought = false;
                for (int i = 0; i < 600 && f.CircuitRunning && !bought; i++)
                {
                    s.Clock.Sleep(100);
                    lock (f.Notes)
                        bought = f.Notes.Exists(n =>
                            n.Contains("Keep: " + VendorShard.Name + " in Testtown: 20 thread"));
                }
                f.CircuitHalt("the test's lap is done");
                for (int i = 0; i < 600 && f.CircuitRunning; i++)
                    s.Clock.Sleep(100);
                string last;
                lock (f.Notes)
                    last = string.Join(" | ", f.Notes.FindAll(n => n.Contains("Keep:") || n.Contains("Circuit:")));
                Check.True(bought, "the stock bought on the first lap: " + last);
                Check.True(v.Buys >= 1, "a buy sent: " + last);
            });
        }
        finally
        {
            UoRegions.Load(path + ".none");
            File.Delete(path);
        }
    }

    // FirstLap's town with Morena's label titled "Morena the <title>" after the bare one (Damian, 2026-10-24: "only
    // tailors have thread. weavers never do."). FirstLap is the untitled control.
    static void Titled(string title, bool asked, uint offset)
    {
        string path = Path.Combine(Path.GetTempPath(), "assist-tests-keeptitle-" + Environment.ProcessId + ".txt");
        File.WriteAllLines(path, new[]
        {
            "R 900 900 200 200 -128 127 0 CITY_TESTTOWN a b c d e Testtown",
            "R 998 998 7 7 -128 127 0 CLOTHIER_TESTTOWN a b c d e The Stitch",
        });
        try
        {
            RecallTests.WithFeatures((s, f) =>
            {
                UoRegions.Load(path);
                f.Cfg.Set(f.Server + "runes.shop", "");
                s.ServerSends(Packets.LoginConfirm(1000, 1000, 0));
                s.ServerSends(Packets.Wear(Pack, 0x0E75, 0x15, Packets.Player));
                s.ServerSends(Packets.Open(Pack));
                s.ServerSends(Packets.Contents(Pack));
                s.Clock.Sleep(1);
                VendorShard v = new(s, Vendor + offset, Box + offset, 1001, 1000, Pack, Gold + offset, 500);
                v.Stock(Thread + offset, 0x0FA0, 20, 3, "Spool of thread");
                s.ServerSends(Packets.Speech(Vendor + offset, VendorShard.Name, VendorShard.Name + " the " + title, 6));
                s.Clock.Sleep(1);
                Check.Null(f.CircuitKeep(), "CircuitKeep");
                bool bought = false;
                for (int i = 0; i < 600 && f.CircuitRunning && !bought; i++)
                {
                    s.Clock.Sleep(100);
                    lock (f.Notes)
                        bought = f.Notes.Exists(n =>
                            n.Contains("Keep: " + VendorShard.Name + " in Testtown: 20 thread"));
                }
                bool ended = !f.CircuitRunning;
                f.CircuitHalt("the test is done");
                for (int i = 0; i < 600 && f.CircuitRunning; i++)
                    s.Clock.Sleep(100);
                string last;
                lock (f.Notes)
                    last = string.Join(" | ", f.Notes.FindAll(n => n.Contains("Keep:") || n.Contains("Circuit:")));
                Check.Equal(asked, bought, "bought out: " + last);
                Check.Equal(asked ? 1 : 0, Asked(s, VendorShard.Name + " buy"), "asked: " + last);
                if (!asked)
                {
                    Check.True(ended, "the circuit ended at once: " + last);
                    lock (f.Notes)
                        Check.True(f.Notes.Exists(n => n.Contains("no thread vendor in the approved shops")),
                            "the end said: " + last);
                }
            });
        }
        finally
        {
            UoRegions.Load(path + ".none");
            File.Delete(path);
        }
    }

    static int Asked(FakeShard s, string text)
    {
        int n = 0;
        foreach (byte[] p in s.ServerGot)
        {
            if (p[0] == 0x03 && p.Length > 8 && Encoding.ASCII.GetString(p, 8, p.Length - 8).TrimEnd('\0') == text)
                n++;
        }
        return n;
    }

    // The first lap's town, a minute of virtual time after the buy-out: Morena is due 55 minutes after it
    // (KeepRules.RestockDefaultMinutes), so she is asked once.
    static void NotBeforeDue()
    {
        string path = Path.Combine(Path.GetTempPath(), "assist-tests-keepdue-" + Environment.ProcessId + ".txt");
        File.WriteAllLines(path, new[]
        {
            "R 900 900 200 200 -128 127 0 CITY_TESTTOWN a b c d e Testtown",
            "R 998 998 7 7 -128 127 0 CLOTHIER_TESTTOWN a b c d e The Stitch",
        });
        try
        {
            RecallTests.WithFeatures((s, f) =>
            {
                UoRegions.Load(path);
                f.Cfg.Set(f.Server + "runes.shop", "");
                s.ServerSends(Packets.LoginConfirm(1000, 1000, 0));
                s.ServerSends(Packets.Wear(Pack, 0x0E75, 0x15, Packets.Player));
                s.ServerSends(Packets.Open(Pack));
                s.ServerSends(Packets.Contents(Pack));
                s.Clock.Sleep(1);
                VendorShard v = new(s, Vendor + 2, Box + 2, 1001, 1000, Pack, Gold + 2, 500);
                v.Stock(Thread + 2, 0x0FA0, 20, 3, "Spool of thread");
                Check.Null(f.CircuitKeep(), "CircuitKeep");
                bool bought = false;
                for (int i = 0; i < 600 && f.CircuitRunning && !bought; i++)
                {
                    s.Clock.Sleep(100);
                    lock (f.Notes)
                        bought = f.Notes.Exists(n =>
                            n.Contains("Keep: " + VendorShard.Name + " in Testtown: 20 thread"));
                }
                s.Clock.Sleep(60000);
                int asks = Asked(s, VendorShard.Name + " buy");
                bool waits;
                lock (f.Notes)
                    waits = f.Notes.Exists(n => n.Contains("Keep: " + VendorShard.Name + " is due at "));
                f.CircuitHalt("the test is done");
                for (int i = 0; i < 600 && f.CircuitRunning; i++)
                    s.Clock.Sleep(100);
                Check.True(bought, "bought out on the first lap");
                Check.Equal(1, asks, "asked once in the minute after (L-FUTILITY)");
                Check.True(waits, "the wait said");
            });
        }
        finally
        {
            UoRegions.Load(path + ".none");
            File.Delete(path);
        }
    }

    // Two tailor shops: the bag's one rune is labelled for The Stitch, Morena stands in The Needle (Damian, 2026-10-20:
    // "lets just use the runes in the bag as the list of player approved shops"). FirstLap is the control: no bag, and
    // she is asked.
    static void BagApproves()
    {
        const uint Bag = 0x40280F10;
        const uint Rune = 0x40280F11;
        string path = Path.Combine(Path.GetTempPath(), "assist-tests-keepbag-" + Environment.ProcessId + ".txt");
        File.WriteAllLines(path, new[]
        {
            "R 900 900 200 200 -128 127 0 CITY_TESTTOWN a b c d e Testtown",
            "R 1020 1020 7 7 -128 127 0 CLOTHIER_TESTTOWN a b c d e The Stitch",
            "R 998 998 7 7 -128 127 0 WEAVER_TESTTOWN a b c d e The Needle",
        });
        try
        {
            RecallTests.WithFeatures((s, f) =>
            {
                UoRegions.Load(path);
                f.Cfg.Set(f.Server + "runes.shop", Bag.ToString("X"));
                s.ServerSends(Packets.LoginConfirm(1000, 1000, 0));
                s.ServerSends(Packets.Wear(Pack, 0x0E75, 0x15, Packets.Player));
                s.ServerSends(Packets.Open(Pack));
                s.ServerSends(Packets.Contents(Pack));
                s.ServerSends(Packets.Contained(Bag, 0x0E76, 1, Pack));
                s.ServerSends(Packets.Contained(Rune, 0x1F14, 1, Bag));
                s.ServerSends(Packets.Speech(Rune, "", "The Stitch", 6));
                s.Clock.Sleep(1);
                VendorShard v = new(s, Vendor + 4, Box + 4, 1001, 1000, Pack, Gold + 4, 500);
                v.Stock(Thread + 4, 0x0FA0, 20, 3, "Spool of thread");
                Check.Null(f.CircuitKeep(), "CircuitKeep");
                for (int i = 0; i < 600 && f.CircuitRunning; i++)
                    s.Clock.Sleep(100);
                f.CircuitHalt("the test is done");
                for (int i = 0; i < 600 && f.CircuitRunning; i++)
                    s.Clock.Sleep(100);
                Check.Equal(0, Asked(s, VendorShard.Name + " buy"), "never asked");
                lock (f.Notes)
                    Check.True(f.Notes.Exists(n => n.Contains("no thread vendor in the approved shops")),
                        "the end said");
            });
        }
        finally
        {
            UoRegions.Load(path + ".none");
            File.Delete(path);
            RecallTests.WithFeatures((s, f) => f.Cfg.Set(f.Server + "runes.shop", ""));
        }
    }

    static int Casts(FakeShard s)
    {
        int n = 0;
        foreach (byte[] p in s.ServerGot)
            n += p[0] == 0x12 && p[3] == 0x56 ? 1 : 0;
        return n;
    }

    // The player in Testtown, which has no tailor; Morena in The Loom, Othertown's one tailor shop (x 2960-2974, y
    // 614-628). The pack holds a rune labelled "The Loom", never recalled by, and the recall reagents. uo1998's cast as
    // RecallTests' shard: the cursor 1500 ms after the words, the target landing the player at 2967,620, where Morena
    // is drawn again beside the player.
    static void OtherTown()
    {
        const uint Rune = 0x40280F21;
        string path = Path.Combine(Path.GetTempPath(), "assist-tests-keeptown-" + Environment.ProcessId + ".txt");
        File.WriteAllLines(path, new[]
        {
            "R 900 900 200 200 -128 127 0 CITY_TESTTOWN a b c d e Testtown",
            "R 2900 560 200 200 -128 127 0 CITY_OTHERTOWN a b c d e Othertown",
            "R 2960 614 14 14 -128 127 0 CLOTHIER_OTHERTOWN a b c d e The Loom",
        });
        try
        {
            RecallTests.WithFeatures((s, f) =>
            {
                UoRegions.Load(path);
                f.Cfg.Set(f.Server + "runes.shop", "");
                s.ServerSends(Packets.LoginConfirm(1000, 1000, 0));
                s.ServerSends(Packets.Wear(Pack, 0x0E75, 0x15, Packets.Player));
                s.ServerSends(Packets.Open(Pack));
                s.ServerSends(Packets.Contents(Pack));
                s.ServerSends(Packets.Contained(Rune, 0x1F14, 1, Pack));
                s.ServerSends(Packets.Speech(Rune, "", "The Loom", 6));
                s.ServerSends(Packets.Contained(0x40280F22, 0x0F86, 30, Pack));
                s.ServerSends(Packets.Contained(0x40280F23, 0x0F7B, 30, Pack));
                s.ServerSends(Packets.Contained(0x40280F24, 0x0F7A, 30, Pack));
                s.ServerSends(Packets.Status(51));
                s.Clock.Sleep(1);
                VendorShard v = new(s, Vendor + 6, Box + 6, 2968, 620, Pack, Gold + 6, 500);
                v.Stock(Thread + 6, 0x0FA0, 20, 3, "Spool of thread");
                bool casting = false;
                s.Answer = p =>
                {
                    if (p[0] == 0x12 && p[3] == 0x56 && !casting)
                    {
                        casting = true;
                        s.Also(1500, () => s.ServerSends(Packets.Cursor(0x0007237A, 0x00)));
                        return ImmutableArray.Create<byte[]>(Packets.Speech(Packets.Player, "Kwik", "Kal Ort Por", 0));
                    }
                    if (p[0] == 0x6C && casting)
                    {
                        casting = false;
                        return ImmutableArray.Create(Packets.Draw(2967, 620, 0, 0),
                            Packets.Mobile(Vendor + 6, 2968, 620));
                    }
                    return v.Answer(p);
                };
                Check.Null(f.CircuitKeep(), "CircuitKeep");
                bool bought = false;
                for (int i = 0; i < 600 && f.CircuitRunning && !bought; i++)
                {
                    s.Clock.Sleep(100);
                    lock (f.Notes)
                        bought = f.Notes.Exists(n =>
                            n.Contains("Keep: " + VendorShard.Name + " in Othertown: 20 thread"));
                }
                f.CircuitHalt("the test's lap is done");
                for (int i = 0; i < 600 && f.CircuitRunning; i++)
                    s.Clock.Sleep(100);
                string last;
                lock (f.Notes)
                    last = string.Join(" | ", f.Notes.FindAll(n => n.Contains("Keep:") || n.Contains("Circuit:")));
                Check.True(bought, "bought in Othertown: " + last);
                Check.Equal(1, Casts(s), "cast once");
                Check.Equal(2967, s.Link.State.X, "landed in The Loom");
                Check.Equal(1, Asked(s, VendorShard.Name + " buy"), "asked once");
            });
        }
        finally
        {
            UoRegions.Load(path + ".none");
            File.Delete(path);
        }
    }

    // FirstLap's town; Morena bought out, then 5 minutes into the wait for her due restock, her stock box's ware of
    // thread re-created a unit at a time as uo1998 restocks (AssistState.Restocks). The restock record holds lines no
    // reader takes and another vendor's restock.
    static void RestockEndsWait()
    {
        const uint offset = 30;
        string path = Path.Combine(Path.GetTempPath(), "assist-tests-keeprestock-" + Environment.ProcessId + ".txt");
        File.WriteAllLines(path, new[]
        {
            "R 900 900 200 200 -128 127 0 CITY_TESTTOWN a b c d e Testtown",
            "R 998 998 7 7 -128 127 0 CLOTHIER_TESTTOWN a b c d e The Stitch",
        });
        try
        {
            RecallTests.WithFeatures((s, f) =>
            {
                UoRegions.Load(path);
                f.Cfg.Set(f.Server + "runes.shop", "");
                File.WriteAllLines(f.ServerFile("restocks"),
                    new[] { "garbage", "ZZ\t1", "ABC\tsoon", "1234\t" + s.Clock.UtcNow.Ticks });
                s.ServerSends(Packets.LoginConfirm(1000, 1000, 0));
                s.ServerSends(Packets.Wear(Pack, 0x0E75, 0x15, Packets.Player));
                s.ServerSends(Packets.Open(Pack));
                s.ServerSends(Packets.Contents(Pack));
                s.Clock.Sleep(1);
                VendorShard v = new(s, Vendor + offset, Box + offset, 1001, 1000, Pack, Gold + offset, 500);
                v.Stock(Thread + offset, 0x0FA0, 20, 3, "Spool of thread");
                Check.Null(f.CircuitKeep(), "CircuitKeep");
                bool waits = false;
                for (int i = 0; i < 600 && f.CircuitRunning && !waits; i++)
                {
                    s.Clock.Sleep(100);
                    lock (f.Notes)
                        waits = f.Notes.Exists(n => n.Contains("Keep: " + VendorShard.Name + " is due at "));
                }
                s.Clock.Sleep(5 * 60000);
                int before = Asked(s, VendorShard.Name + " buy");
                uint ware = Thread + offset;
                s.ServerSends(Packets.Delete(ware));
                s.ServerSends(Packets.Contained(ware, 0x0FA0, 1, Box + offset));
                s.ServerSends(Packets.Delete(ware));
                s.ServerSends(Packets.Contained(ware, 0x0FA0, 2, Box + offset));
                s.Clock.Sleep(10000);
                int after = Asked(s, VendorShard.Name + " buy");
                f.CircuitHalt("the test is done");
                for (int i = 0; i < 600 && f.CircuitRunning; i++)
                    s.Clock.Sleep(100);
                string said;
                lock (f.Notes)
                    said = string.Join(" | ", f.Notes.FindAll(n => n.Contains("Keep:") || n.Contains("Circuit:")));
                Check.True(waits, "the wait said: " + said);
                Check.Equal(1, before, "asked once before the restock: " + said);
                Check.True(said.Contains("Keep: " + VendorShard.Name + " restocked at "), "the restock said: " + said);
                Check.Equal(2, after, "asked again at once: " + said);
                Check.True(Array.Exists(File.ReadAllLines(f.ServerFile("restocks")),
                    l => l.StartsWith((Vendor + offset).ToString("X") + "\t")), "kept in the record");
            });
        }
        finally
        {
            UoRegions.Load(path + ".none");
            File.Delete(path);
        }
    }

    static byte[] Weight(int stones)
    {
        byte[] st = Packets.Status(51);
        st[64] = (byte)(stones >> 8);
        st[65] = (byte)stones;
        return st;
    }

    // Packets.Status's strength 85: the 1998 carry limit, strength * 4 + 30.
    const int CarryLimit = 85 * 4 + 30;

    static void StashPastLimit() => Stash(CarryLimit - AssistFeatures.SewWeightRoom + 1, 10);

    static void StashAtLimit() => Stash(CarryLimit - AssistFeatures.SewWeightRoom, 0);

    // FirstLap's town, the player's strength 85 (a carry limit of 370), the suites' tile data none (thread a stone for
    // each 10): the buy answered with a weight of afterBuy; a lift answered by nothing, a drop on the ground by the
    // thread lying there, the rest left in the pack and the weight at the limit less SewWeightRoom, so nothing fits to
    // lift back.
    static void Stash(int afterBuy, int put)
    {
        string path = Path.Combine(Path.GetTempPath(), "assist-tests-keepstash-" + Environment.ProcessId + ".txt");
        File.WriteAllLines(path, new[]
        {
            "R 900 900 200 200 -128 127 0 CITY_TESTTOWN a b c d e Testtown",
            "R 998 998 7 7 -128 127 0 CLOTHIER_TESTTOWN a b c d e The Stitch",
        });
        try
        {
            RecallTests.WithFeatures((s, f) =>
            {
                UoRegions.Load(path);
                f.Cfg.Set(f.Server + "runes.shop", "");
                s.ServerSends(Packets.LoginConfirm(1000, 1000, 0));
                s.ServerSends(Packets.Wear(Pack, 0x0E75, 0x15, Packets.Player));
                s.ServerSends(Packets.Open(Pack));
                s.ServerSends(Packets.Contents(Pack));
                s.ServerSends(Weight(300));
                s.Clock.Sleep(1);
                VendorShard v = new(s, Vendor + 8 + (uint)put, Box + 8 + (uint)put, 1001, 1000, Pack,
                    Gold + 8 + (uint)put, 500);
                v.Stock(Thread + 8 + (uint)put, 0x0FA0, 20, 3, "Spool of thread");
                List<int> lifts = new();
                List<int[]> drops = new();
                s.Answer = p =>
                {
                    if (p[0] == 0x07)
                    {
                        lock (lifts)
                            lifts.Add(p[5] << 8 | p[6]);
                        return ImmutableArray<byte[]>.Empty;
                    }
                    if (p[0] == 0x08)
                    {
                        uint item = (uint)(p[1] << 24 | p[2] << 16 | p[3] << 8 | p[4]);
                        int x = p[5] << 8 | p[6], y = p[7] << 8 | p[8];
                        int n;
                        lock (lifts)
                            n = lifts[lifts.Count - 1];
                        lock (drops)
                            drops.Add(new[] { x, y, n });
                        return ImmutableArray.Create(Lying(item, 0x0FA0, n, x, y),
                            Packets.Contained(0x45E10000, 0x0FA0, 20 - n, Pack),
                            Weight(CarryLimit - AssistFeatures.SewWeightRoom));
                    }
                    ImmutableArray<byte[]> r = v.Answer(p);
                    return p[0] == 0x3B ? r.Add(Weight(afterBuy)) : r;
                };
                Check.Null(f.CircuitKeep(), "CircuitKeep");
                bool waits = false;
                for (int i = 0; i < 600 && f.CircuitRunning && !waits; i++)
                {
                    s.Clock.Sleep(100);
                    lock (f.Notes)
                        waits = f.Notes.Exists(n => n.Contains("Keep: " + VendorShard.Name + " is due at "));
                }
                f.CircuitHalt("the test is done");
                for (int i = 0; i < 600 && f.CircuitRunning; i++)
                    s.Clock.Sleep(100);
                string last;
                lock (f.Notes)
                    last = string.Join(" | ", f.Notes.FindAll(n => n.Contains("Keep:") || n.Contains("Circuit:")));
                Check.True(waits, "the lap done and the next wait said: " + last);
                lock (drops)
                {
                    Check.Equal(put > 0 ? 1 : 0, drops.Count, "drops: " + last);
                    if (put > 0)
                    {
                        Check.Equal(put, drops[0][2], "thread put down");
                        Check.True(Math.Max(Math.Abs(drops[0][0] - 1000), Math.Abs(drops[0][1] - 1000)) == 1,
                            "a tile beside the player: " + drops[0][0] + "," + drops[0][1]);
                    }
                }
                lock (f.Notes)
                    Check.Equal(put > 0,
                        f.Notes.Exists(n => n.Contains("Keep: " + put + " thread stashed beside you at ")),
                        "the stash said: " + last);
                lock (lifts)
                    Check.Equal(put > 0 ? 1 : 0, lifts.Count, "nothing lifted back");
            });
        }
        finally
        {
            UoRegions.Load(path + ".none");
            File.Delete(path);
        }
    }

    static void StashOnSpot()
    {
        List<int[]> drops = StashOnMap(20, false, CarryLimit - AssistFeatures.SewWeightRoom, out string notes);
        Check.Equal(1, drops.Count, "one drop: " + notes);
        Check.Equal("102,102", drops[0][0] + "," + drops[0][1], "the spot the wall at 103,103 hides");
        Check.True(notes.Contains("Keep: 10 thread stashed at 102,102 in The Stitch (hidden;"),
            "the stash said: " + notes);
    }

    static void StashSpotRefused()
    {
        List<int[]> drops = StashOnMap(22, true, CarryLimit - AssistFeatures.SewWeightRoom, out string notes);
        Check.Equal(2, drops.Count, "two drops: " + notes);
        Check.Equal("102,102", drops[0][0] + "," + drops[0][1], "the hidden spot first");
        Check.True(drops[1][0] != 102 || drops[1][1] != 102, "then another spot");
        Check.True(drops[1][0] >= 98 && drops[1][0] <= 104 && drops[1][1] >= 98 && drops[1][1] <= 104,
            "in the shop: " + drops[1][0] + "," + drops[1][1]);
        Check.True(notes.Contains("Keep: the stash spot 102,102 refused: "), "the refusal said: " + notes);
        Check.True(notes.Contains("Keep: 10 thread stashed at " + drops[1][0] + "," + drops[1][1]),
            "the stash said: " + notes);
    }

    static void StashLiftedBack()
    {
        List<int[]> drops = StashOnMap(24, false, CarryLimit - AssistFeatures.SewWeightRoom - 5, out string notes);
        Check.Equal(2, drops.Count, "the stash and the lift back: " + notes);
        Check.Equal(1, drops[1][3], "the second into the pack");
        Check.True(notes.Contains("Keep: 10 thread lifted back from 102,102"), "the lift back said: " + notes);
    }

    // A town over the test map: The Stitch (x and y 98 to 104) holding the player at 100,100 and Morena at 101,100, a
    // wall at 103,103 hiding 102,102 (StashSpots.CoverRise). The buy answered with a weight a stone past the limit; a
    // drop on the ground answered by the thread lying there and a weight of afterStash, or, refuseFirst, the first one
    // by the thread back in the pack; a drop into the pack by the stack there. The drops: x, y, amount, 1 for into the
    // pack. Each arm's serials are its own (offset): the attempts record is the run's, and a vendor bought out in one
    // arm is not due in the next.
    static List<int[]> StashOnMap(uint offset, bool refuseFirst, int afterStash, out string notes)
    {
        string path = Path.Combine(Path.GetTempPath(), "assist-tests-keepspot-" + Environment.ProcessId + ".txt");
        File.WriteAllLines(path, new[]
        {
            "R 0 0 256 256 -128 127 0 CITY_TESTTOWN a b c d e Testtown",
            "R 98 98 7 7 -128 127 0 CLOTHIER_TESTTOWN a b c d e The Stitch",
        });
        List<int[]> drops = new();
        string said = "";
        try
        {
            TestMap map = new TestMap().WallAt(103, 103);
            map.Run(() => RecallTests.WithFeatures((s, f) =>
            {
                UoRegions.Load(path);
                f.Cfg.Set(f.Server + "runes.shop", "");
                StepShard step = new(s, map, 100, 100);
                s.ServerSends(Packets.Wear(Pack, 0x0E75, 0x15, Packets.Player));
                s.ServerSends(Packets.Open(Pack));
                s.ServerSends(Packets.Contents(Pack));
                s.ServerSends(Weight(300));
                s.Clock.Sleep(1);
                VendorShard v = new(s, Vendor + offset, Box + offset, 101, 100, Pack, Gold + offset, 500);
                v.Stock(Thread + offset, 0x0FA0, 20, 3, "Spool of thread");
                int lifted = 0;
                bool refused = false;
                s.Answer = p =>
                {
                    if (p[0] == 0x07)
                    {
                        lifted = p[5] << 8 | p[6];
                        return ImmutableArray<byte[]>.Empty;
                    }
                    if (p[0] == 0x08)
                    {
                        uint item = (uint)(p[1] << 24 | p[2] << 16 | p[3] << 8 | p[4]);
                        uint into = (uint)(p[10] << 24 | p[11] << 16 | p[12] << 8 | p[13]);
                        int x = p[5] << 8 | p[6], y = p[7] << 8 | p[8];
                        lock (drops)
                            drops.Add(new[] { x, y, lifted, into == Pack ? 1 : 0 });
                        if (into == Pack)
                            return ImmutableArray.Create(Packets.Contained(item, 0x0FA0, lifted, Pack),
                                Weight(CarryLimit - AssistFeatures.SewWeightRoom));
                        if (refuseFirst && !refused)
                        {
                            refused = true;
                            return ImmutableArray.Create(Packets.Contained(item, 0x0FA0, 20, Pack),
                                Weight(CarryLimit - AssistFeatures.SewWeightRoom + 1));
                        }
                        return ImmutableArray.Create(Lying(item, 0x0FA0, lifted, x, y),
                            Packets.Contained(0x45E10000, 0x0FA0, 20 - lifted, Pack), Weight(afterStash));
                    }
                    ImmutableArray<byte[]> r = step.Answer(p).AddRange(v.Answer(p));
                    return p[0] == 0x3B ? r.Add(Weight(CarryLimit - AssistFeatures.SewWeightRoom + 1)) : r;
                };
                Check.Null(f.CircuitKeep(), "CircuitKeep");
                bool waits = false;
                for (int i = 0; i < 600 && f.CircuitRunning && !waits; i++)
                {
                    s.Clock.Sleep(100);
                    lock (f.Notes)
                        waits = f.Notes.Exists(n => n.Contains("Keep: " + VendorShard.Name + " is due at "));
                }
                f.CircuitHalt("the test is done");
                for (int i = 0; i < 600 && f.CircuitRunning; i++)
                    s.Clock.Sleep(100);
                lock (f.Notes)
                    said = string.Join(" | ", f.Notes.FindAll(n => n.Contains("Keep:") || n.Contains("Circuit:")));
                Check.True(waits, "the lap done and the next wait said: " + said);
            }));
        }
        finally
        {
            UoRegions.Load(path + ".none");
            File.Delete(path);
        }
        notes = said;
        return drops;
    }

    // 0x1A: an item lying on the ground (amount follows the top bit), 16 bytes.
    static byte[] Lying(uint serial, int graphic, int amount, int x, int y)
    {
        uint t = serial | 0x80000000;
        return new byte[] { 0x1A, 0, 16, (byte)(t >> 24), (byte)(t >> 16), (byte)(t >> 8), (byte)t,
            (byte)(graphic >> 8), (byte)graphic, (byte)(amount >> 8), (byte)amount, (byte)(x >> 8), (byte)x,
            (byte)(y >> 8), (byte)y, 0 };
    }

    static void NoVendor()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            s.ServerSends(Packets.LoginConfirm(1000, 1000, 0));
            s.ServerSends(Packets.Wear(Pack, 0x0E75, 0x15, Packets.Player));
            s.ServerSends(Packets.Open(Pack));
            s.ServerSends(Packets.Contents(Pack));
            s.Clock.Sleep(1);
            Check.Null(f.CircuitKeep(), "CircuitKeep");
            for (int i = 0; i < 600 && f.CircuitRunning; i++)
                s.Clock.Sleep(100);
            Check.False(f.CircuitRunning, "the circuit ended");
            lock (f.Notes)
                Check.True(f.Notes.Exists(n => n.Contains("no thread vendor in the approved shops")),
                    "the end said: " + (f.Notes.Count > 0 ? f.Notes[f.Notes.Count - 1] : "no note"));
            Check.Equal(0, Said(s), "nothing said");
        });
    }
}
