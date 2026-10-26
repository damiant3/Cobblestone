#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Uoaix.Assist.Tests;

// The walk-past pickup (AssistFeatures.PickupLoop) on the fake shard: Kwik at 1000,1000, nightshade (0x0F88) lying
// beside him. The shard answers a lift as uo1998 did (relay log 2026-10-23 17:52:49: the item's 0x1D) or refuses it
// (0x27 and its reason), and a drop into the pack with the item there.
public static class PickupTests
{
    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("pickup: a reagent beside the player goes into the pack, noted, its map mark gone", Picked),
        new TestCase("pickup: off, or an item not in the reagent list, is never lifted", NotPicked),
        new TestCase("pickup: busy is lifted again BusyRetries times after the busy time, then left alone", Busy),
        new TestCase("pickup: out of range standing still shortens the reach; any other refusal leaves the item for "
        + "good", Refused),
        new TestCase("get: an item on the ground lifted, the amount asked of it, into the pack or the container named; an "
        + "unknown serial lifts nothing", Get));

    public static ImmutableArray<TestCase> Cases => s_cases;

    const uint Pack = 0x40000001, Shade = 0x47061CCB;

    static uint U32(byte[] p, int at) => (uint)(p[at] << 24 | p[at + 1] << 16 | p[at + 2] << 8 | p[at + 3]);

    static byte[] Lying(uint serial, int graphic, int x, int y)
    {
        return new byte[] { 0x1A, 0, 14, (byte)(serial >> 24), (byte)(serial >> 16), (byte)(serial >> 8), (byte)serial,
            (byte)(graphic >> 8), (byte)graphic, (byte)(x >> 8), (byte)x, (byte)(y >> 8), (byte)y, 0 };
    }

    static byte[] Delete(uint serial) => new byte[] { 0x1D, (byte)(serial >> 24), (byte)(serial >> 16),
        (byte)(serial >> 8), (byte)serial };

    // refuse: the 0x27 reason for each lift in turn, -1 to take it; past the list every lift is taken.
    static List<uint> With(FakeShard s, AssistFeatures f, params int[] refuse)
    {
        List<uint> lifts = new();
        s.ServerSends(Packets.LoginConfirm(1000, 1000, 0));
        s.ServerSends(Packets.Wear(Pack, 0x0E75, 0x15, Packets.Player));
        s.Clock.Sleep(1);
        s.Answer = p =>
        {
            if (p[0] == 0x07)
            {
                uint item = U32(p, 1);
                int r = lifts.Count < refuse.Length ? refuse[lifts.Count] : -1;
                lifts.Add(item);
                return r >= 0 ? ImmutableArray.Create<byte[]>(new byte[] { 0x27, (byte)r })
                    : ImmutableArray.Create<byte[]>(Delete(item));
            }
            if (p[0] == 0x08)
                return ImmutableArray.Create<byte[]>(Packets.Contained(U32(p, 1), 0x0F88, 1, Pack));
            return ImmutableArray<byte[]>.Empty;
        };
        return lifts;
    }

    static void Get()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            List<uint> lifts = With(s, f);
            List<byte[]> got = new();
            Func<byte[], ImmutableArray<byte[]>>? shard = s.Answer;
            s.Answer = p =>
            {
                if (p[0] == 0x07 || p[0] == 0x08)
                    got.Add(p);
                return shard!(p);
            };
            Check.Equal("47887787 is not known", f.GetItem(0x47887787, 0, 0, out int none), "unknown");
            Check.Equal(0, lifts.Count, "nothing lifted for it");
            s.ServerSends(Lying(Shade, 0x0F88, 1001, 1000));
            s.Clock.Sleep(1);
            Check.Null(f.GetItem(Shade, 5, 0, out int moved), "got");
            Check.Equal(1, lifts.Count, "lifted once");
            byte[] lift = got.Find(p => p[0] == 0x07)!;
            Check.Equal(1, lift[5] << 8 | lift[6], "a lying item of one: one lifted, not the 5 asked");
            byte[] drop = got.Find(p => p[0] == 0x08)!;
            Check.Equal(Pack, U32(drop, 10), "into the pack");
            Check.Equal(1, moved, "moved");
            s.ServerSends(Lying(Shade + 1, 0x0F88, 1001, 1000));
            s.Clock.Sleep(1);
            Check.Null(f.GetItem(Shade + 1, 0, 0x40000099, out _), "into a container named");
            Check.Equal(0x40000099u, U32(got.FindLast(p => p[0] == 0x08)!, 10), "dropped into it");
        });
    }

    static void Await(FakeShard s, Func<bool> done, int ms = 30000)
    {
        for (int t = 0; t < ms && !done(); t += 250)
            s.Clock.Sleep(250);
    }

    static void Picked()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            f.Cfg.Set(f.Server + "res.seen", "");
            List<uint> lifts = With(s, f);
            s.ServerSends(Lying(Shade, 0x0F88, 1001, 1000));
            s.Clock.Sleep(1);
            Check.Equal(1, f.ResourceSpots().Count, "marked");
            f.Pickup = true;
            try
            {
                Await(s, () => f.Notes.Exists(n => n.Contains("Pickup: 1 of 0F88 into the backpack")));
            }
            finally
            {
                f.Pickup = false;
            }
            Check.True(f.Notes.Exists(n => n.Contains("Pickup: 1 of 0F88 into the backpack")), "noted");
            Check.Equal(1, lifts.Count, "lifted once");
            Check.Equal(0, f.ResourceSpots().Count, "its mark gone");
            f.Cfg.Set(f.Server + "res.seen", "");
        });
    }

    static void NotPicked()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            List<uint> lifts = With(s, f);
            s.ServerSends(Lying(Shade, 0x0F88, 1001, 1000));
            s.ServerSends(Lying(Shade + 1, 0x0A0F, 1000, 1001));
            s.Clock.Sleep(10000);
            Check.Equal(0, lifts.Count, "off: nothing lifted");
            string was = f.Cfg.Get("reagents", AssistFeatures.DefaultReagents);
            f.Cfg.Set("reagents", "F7A");
            f.Pickup = true;
            try
            {
                s.Clock.Sleep(10000);
            }
            finally
            {
                f.Pickup = false;
                f.Cfg.Set("reagents", was);
            }
            Check.Equal(0, lifts.Count, "on, neither in the list: nothing lifted");
        });
    }

    static void Busy()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            List<uint> lifts = With(s, f, 0xFF, 0xFF, 0xFF, 0xFF);
            s.ServerSends(Lying(Shade, 0x0F88, 1001, 1000));
            s.Clock.Sleep(1);
            f.Pickup = true;
            try
            {
                Await(s, () => f.Notes.Exists(n => n.Contains("left " + Shade.ToString("X") + " alone")));
                s.Clock.Sleep(10000);
            }
            finally
            {
                f.Pickup = false;
            }
            Check.Equal(AssistFeatures.BusyRetries + 1, lifts.Count, "the first lift and BusyRetries more, then none");
            Check.Equal(AssistFeatures.BusyRetries,
                f.Notes.FindAll(n => n.Contains("is lifted again after the busy time")).Count, "each retry noted");
        });
    }

    static void Refused()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            string reach = f.Cfg.Get(f.Server + "pickup.reach", "");
            List<uint> lifts = With(s, f, 1, 3);
            // Out of range at 2 tiles, standing still: the reach becomes 1, and the item is lifted again only from
            // nearer.
            s.ServerSends(Lying(Shade, 0x0F88, 1002, 1000));
            s.Clock.Sleep(1);
            f.Pickup = true;
            try
            {
                Await(s, () => f.Notes.Exists(n => n.Contains("reach is now 1")));
                s.Clock.Sleep(5000);
                Check.Equal(1, lifts.Count, "not lifted again from as far");
                Check.Equal(1, f.Reach, "reach 1");
                // Another, beside: refused for another reason (3), left for good.
                s.ServerSends(Lying(Shade + 1, 0x0F88, 1001, 1000));
                Await(s, () => lifts.Count >= 2);
                s.Clock.Sleep(10000);
                Check.Equal(2, lifts.Count, "the other reason: lifted once, never again");
                Check.True(f.Notes.Exists(n => n.Contains("left " + (Shade + 1).ToString("X") + " alone")),
                    "noted left alone");
            }
            finally
            {
                f.Pickup = false;
                f.Cfg.Set(f.Server + "pickup.reach", reach);
            }
        });
    }
}
