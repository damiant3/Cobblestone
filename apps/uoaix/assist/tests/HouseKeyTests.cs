#nullable enable
using System.Collections.Immutable;
using System.IO;
using System;

namespace Uoaix.Assist.Tests;

// The house key found in the pack before a recall (AssistFeatures.HomeKeyBeforeTravel; Damian, 2026-10-22: "the
// circuit can never travel with the house key ... if that is delayed by movement, or refused or not possible, then it
// should be put in the bank. the circuit can continue at that point."): away from the house it goes into the bank box
// and the recall goes on; a bank that cannot take it refuses the recall.
public static class HouseKeyTests
{
    const uint Pack = 0x42BE4F7C;
    const uint Rune = 0x44DE6EA5;
    const uint Key = 0x468656EB;
    const uint Box = 0x40001234;
    const uint Vendor = 0x00ABCDEF;
    const int KeyArt = 0x1769;

    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("house key: no key in the pack, the recall casts and \"bank\" is not said", NoKey),
        new TestCase("house key: a key with no house set up is no house key, the recall casts with it", NoHouse),
        new TestCase("house key: with the house unlocked by setting, a key is no house key", HouseUnlocked),
        new TestCase("house key: away from the house it goes into the bank box, then the recall casts", Banked),
        new TestCase("house key: outside every town it stays in the pack and the recall is refused", NoTown),
        new TestCase("house key: a refused lift at the bank leaves it in the pack and refuses the recall", LiftRefused),
        new TestCase("house key: a drop the server ignores leaves it in the pack and refuses the recall", DropIgnored),
        new TestCase("house key: with no bank in walking reach the key's one recall is to a bank, then the recall casts",
        BankByRecall),
        new TestCase("house key: two lifts refused as busy in a row raise busy by half, and the lift after them banks "
        + "the key", BusyRefused));

    public static ImmutableArray<TestCase> Cases => s_cases;

    // A town around the player at 1000,1000 with a bank 5 tiles off (inside BankWalkTiles: no recall to it).
    static void Town(bool withCity)
    {
        string path = Path.Combine(Path.GetTempPath(),
            "assist-tests-keyregions-" + (withCity ? "town" : "wild") + "-" + Environment.ProcessId + ".txt");
        File.WriteAllLines(path, withCity
            ? new[]
            {
                "R 900 900 200 200 -128 127 0 CITY_TESTTOWN a b c d e Testtown",
                "R 1000 1000 10 10 -128 127 0 BANK_TESTTOWN a b c d e Testtown bank",
            }
            : new[] { "R 2000 2000 10 10 -128 127 0 CITY_ELSEWHERE a b c d e Elsewhere" });
        UoRegions.Load(path);
    }

    static void Kwik(FakeShard s, bool key)
    {
        s.ServerSends(Packets.LoginConfirm(1000, 1000, 0));
        s.ServerSends(Packets.Wear(Pack, 0x0E75, 0x15, Packets.Player));
        s.ServerSends(Packets.Contained(Rune, 0x1F14, 1, Pack));
        s.ServerSends(Packets.Contained(0x464FAC6C, 0x0F86, 30, Pack));
        s.ServerSends(Packets.Contained(0x464FABFC, 0x0F7B, 30, Pack));
        s.ServerSends(Packets.Contained(0x46B09470, 0x0F7A, 30, Pack));
        if (key)
            s.ServerSends(Packets.Contained(Key, KeyArt, 1, Pack));
        s.ServerSends(Packets.Status(51));
        s.Clock.Sleep(1);
    }

    // The house set up (its vendor known, locked by default) and the banker's tile the one the player stands on. Every
    // case of a run shares one settings file (RecallTests.WithFeatures), so a case without a house clears it.
    static void House(AssistFeatures f, bool locked, bool vendor = true)
    {
        f.Cfg.Set(f.Server + "house.vendor", vendor ? Vendor.ToString("X") : "");
        f.Cfg.Set(f.Server + "house.locked", locked ? "on" : "off");
        f.Cfg.Set(f.Server + "bankspot.1005,1005", "1000,1000");
    }

    // uo1998: "bank" opens the box; a lift is accepted silently, or refused (27) when refuseLift; a drop into the box
    // puts the key there; a cast, its cursor and target land the player at 2967,620, and any later cast at 1500,1500.
    // busyTwice: the first two lifts refused as busy (27 FF), the next taken.
    static void Shard(FakeShard s, bool refuseLift, bool ignoreDrop = false, bool busyTwice = false)
    {
        int landings = 0;
        int lifts = 0;
        s.Answer = p =>
        {
            if (p[0] == 0x03)
                return ImmutableArray.Create(Packets.Open(Box), Packets.Contents(Box));
            if (p[0] == 0x07 && busyTwice && ++lifts <= 2)
                return ImmutableArray.Create<byte[]>(new byte[] { 0x27, 0xFF });
            if (p[0] == 0x07 && refuseLift)
                return ImmutableArray.Create<byte[]>(new byte[] { 0x27, 0x00 });
            if (p[0] == 0x08 && !ignoreDrop)
                return ImmutableArray.Create<byte[]>(Packets.Contained(Key, KeyArt, 1, Box));
            if (p[0] == 0x12 && p[3] == 0x56)
            {
                s.Also(1500, () => s.ServerSends(Packets.Cursor(0x0007237A, 0x00)));
                return ImmutableArray.Create<byte[]>(Packets.Speech(Packets.Player, "Kwik", "Kal Ort Por", 0));
            }
            if (p[0] == 0x6C)
                return ImmutableArray.Create<byte[]>(++landings == 1 ? Packets.Draw(2967, 620, 0, 0)
                    : Packets.Draw(1500, 1500, 0, 0));
            return ImmutableArray<byte[]>.Empty;
        };
    }

    static int Sent(FakeShard s, byte op)
    {
        int n = 0;
        foreach (byte[] p in s.ServerGot)
            n += p[0] == op ? 1 : 0;
        return n;
    }

    static int Casts(FakeShard s)
    {
        int n = 0;
        foreach (byte[] p in s.ServerGot)
            n += p[0] == 0x12 && p[3] == 0x56 ? 1 : 0;
        return n;
    }

    // The landing's draw clears the items seen, so after a recall the key is read from what was sent: the container of
    // the first drop, and whether that drop came before the first cast.
    static uint DroppedInto(FakeShard s, out bool beforeCast)
    {
        bool cast = false;
        beforeCast = false;
        foreach (byte[] p in s.ServerGot)
        {
            if (p[0] == 0x12 && p[3] == 0x56)
                cast = true;
            if (p[0] == 0x08)
            {
                beforeCast = !cast;
                return (uint)(p[10] << 24 | p[11] << 16 | p[12] << 8 | p[13]);
            }
        }
        return 0;
    }

    static uint KeyContainer(FakeShard s)
    {
        lock (s.Link.State.Gate)
            return s.Link.State.Items.TryGet(Key, out AssistItem? it) ? it.Container : 0;
    }

    static void NoKey()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Town(true);
            Kwik(s, false);
            House(f, true);
            Shard(s, false);
            Check.Null(f.Recall(Rune), "Recall");
            Check.Equal(0, Sent(s, 0x03), "speech");
            Check.Equal(1, Casts(s), "casts");
        });
    }

    static void NoHouse()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Town(true);
            Kwik(s, true);
            House(f, true, vendor: false);
            Shard(s, false);
            Check.Null(f.Recall(Rune), "Recall");
            Check.Equal(0, Sent(s, 0x03), "speech");
            Check.Equal(0, Sent(s, 0x08), "drops");
            Check.Equal(1, Casts(s), "casts");
        });
    }

    static void HouseUnlocked()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Town(true);
            Kwik(s, true);
            House(f, false);
            Shard(s, false);
            Check.Null(f.Recall(Rune), "Recall");
            Check.Equal(Pack, KeyContainer(s), "the key's container");
            Check.Equal(1, Casts(s), "casts");
        });
    }

    static void Banked()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Town(true);
            Kwik(s, true);
            House(f, true);
            Shard(s, false);
            Check.Null(f.Recall(Rune), "Recall");
            Check.Equal(Box, DroppedInto(s, out bool beforeCast), "the drop's container");
            Check.True(beforeCast, "the drop before the cast");
            Check.Equal(1, Casts(s), "casts");
            Check.Equal(2967, s.Link.State.X, "X");
        });
    }

    static void NoTown()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Town(false);
            Kwik(s, true);
            House(f, true);
            Shard(s, false);
            string? why = f.Recall(Rune);
            Check.True(why is not null
                && why.EndsWith("not into the bank: not in a town, and no rune in the pack lands by a bank"),
                "Recall: " + why);
            Check.Equal(Pack, KeyContainer(s), "the key's container");
            Check.Equal(0, Casts(s), "casts");
        });
    }

    static void LiftRefused()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Town(true);
            Kwik(s, true);
            House(f, true);
            Shard(s, true);
            string? why = f.Recall(Rune);
            Check.True(why is not null && why.Contains("not into the bank: lifting the house key"), "Recall: " + why);
            Check.Equal(Pack, KeyContainer(s), "the key's container");
            Check.Equal(0, Sent(s, 0x08), "drops");
            Check.Equal(0, Casts(s), "casts");
        });
    }

    static void DropIgnored()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Town(true);
            Kwik(s, true);
            House(f, true);
            Shard(s, false, ignoreDrop: true);
            string? why = f.Recall(Rune);
            Check.True(why is not null && why.Contains("not into the bank: ")
                && why.Contains("the server put it back where it lay"), "Recall: " + why);
            Check.Equal(Pack, KeyContainer(s), "the key's container");
            Check.Equal(0, Casts(s), "casts");
        });
    }

    // The player at 1000,1000 in no town; the rune lands at 2967,620, 3 tiles from Faraway's bank. Without the guard
    // the bank's recall asks for the key again, and again.
    static void BankByRecall()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            string regions = Path.Combine(Path.GetTempPath(),
                "assist-tests-keyregions-far-" + Environment.ProcessId + ".txt");
            File.WriteAllLines(regions, new[]
            {
                "R 2900 500 200 200 -128 127 0 CITY_FARAWAY a b c d e Faraway",
                "R 2965 615 10 10 -128 127 0 BANK_FARAWAY a b c d e Faraway bank",
            });
            UoRegions.Load(regions);
            string data = Path.GetDirectoryName(AssistRelay.SettingsPath)!;
            string safe = f.Server.TrimEnd('.').Replace(':', '-').Replace(',', '-');
            File.WriteAllLines(Path.Combine(data, "rune-landings-" + safe + ".txt"),
                new[] { Rune.ToString("X") + "\t2967\t620" });
            Kwik(s, true);
            House(f, true);
            f.Cfg.Set(f.Server + "bankspot.2970,620", "2967,620");
            Shard(s, false);
            Check.Null(f.Recall(Rune), "Recall");
            Check.Equal(Box, DroppedInto(s, out bool beforeCast), "the drop's container");
            Check.True(!beforeCast, "the drop after the bank's recall");
            Check.Equal(2, Casts(s), "casts");
        });
    }

    static void BusyRefused()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Town(true);
            Kwik(s, true);
            House(f, true);
            f.Timings.Set("busy", 700);
            Shard(s, false, busyTwice: true);
            try
            {
                Check.Null(f.Recall(Rune), "Recall");
                Check.Equal(3, Sent(s, 0x07), "lifts");
                Check.Equal(Box, DroppedInto(s, out bool beforeCast), "the drop's container");
                Check.Equal(1050, f.Timings.Ms("busy"), "busy");
            }
            finally
            {
                f.Timings.Set("busy", 700);
            }
        });
    }
}
