#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Uoaix.Assist.Tests;

// The reagent harvest (AssistFeatures.RunReagents) on the fake shard: Kwik at 1000,1000; the shard takes a lift as
// uo1998 did (the item's 0x1D) and a drop into the pack with the item there, as PickupTests' shard does.
public static class ReagentTests
{
    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("reagents: a pile beside Kwik is lifted by the pickup the circuit turns on, then the circuit "
            + "ends with every pile visited", Harvest),
        new TestCase("reagents: a pile the pickup leaves is left once; none known, or none in range, ends at once; "
        + "one no walk reaches is left", Edges),
        new TestCase("reagents: an orc within FleeClear is run from, and the harvest goes on once clear", Threat),
        new TestCase("reagents: a pack one item under its limit ends the harvest; two under goes on", FullPack),
        new TestCase("reagents: a pack within SewWeightRoom of the carry limit ends the harvest; at its edge goes on",
        Heavy),
        new TestCase("reagents: a stop during the walk or the pickup's wait ends the harvest stopped", StopMid));

    public static ImmutableArray<TestCase> Cases => s_cases;

    const uint Pack = 0x40000001, Shade = 0x47061CCB;

    static uint U32(byte[] p, int at) => (uint)(p[at] << 24 | p[at + 1] << 16 | p[at + 2] << 8 | p[at + 3]);

    static byte[] Lying(uint serial, int graphic, int x, int y)
    {
        return new byte[] { 0x1A, 0, 14, (byte)(serial >> 24), (byte)(serial >> 16), (byte)(serial >> 8), (byte)serial,
            (byte)(graphic >> 8), (byte)graphic, (byte)(x >> 8), (byte)x, (byte)(y >> 8), (byte)y, 0 };
    }

    static List<uint> Kwik(FakeShard s, AssistFeatures f)
    {
        List<uint> lifts = new();
        f.Cfg.Set(f.Server + "res.seen", "");
        s.ServerSends(Packets.LoginConfirm(1000, 1000, 0));
        s.ServerSends(Packets.Wear(Pack, 0x0E75, 0x15, Packets.Player));
        s.ServerSends(Packets.Contents(Pack));
        s.ServerSends(Packets.Status(51));
        s.Clock.Sleep(1);
        s.Answer = p =>
        {
            if (p[0] == 0x07)
            {
                uint item = U32(p, 1);
                lifts.Add(item);
                return ImmutableArray.Create<byte[]>(new byte[] { 0x1D, (byte)(item >> 24), (byte)(item >> 16),
                    (byte)(item >> 8), (byte)item });
            }
            if (p[0] == 0x08)
                return ImmutableArray.Create<byte[]>(Packets.Contained(U32(p, 1), 0x0F88, 1, Pack));
            return ImmutableArray<byte[]>.Empty;
        };
        return lifts;
    }

    static void Run(FakeShard s, AssistFeatures f)
    {
        Check.Null(f.CircuitReagents(), "CircuitReagents");
        for (int i = 0; i < 600 && f.CircuitRunning; i++)
            s.Clock.Sleep(200);
        f.CircuitHalt("test done");
        for (int i = 0; i < 2000 && f.CircuitRunning; i++)
            s.Clock.Sleep(50);
        Check.False(f.CircuitRunning, "the circuit ended");
    }

    static void Done(AssistFeatures f)
    {
        f.Pickup = false;
        f.SetReagents(Array.ConvertAll(AssistFeatures.DefaultReagents.Split(','), h => Convert.ToInt32(h, 16)));
        f.ReagentRange = AssistFeatures.ReagentRangeDefault;
        f.Cfg.Set(f.Server + "res.seen", "");
    }

    static void Harvest()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            List<uint> lifts = Kwik(s, f);
            f.Pickup = false;
            s.ServerSends(Lying(Shade, 0x0F88, 1001, 1000));
            s.Clock.Sleep(1);
            try
            {
                Run(s, f);
                Check.Equal(1, lifts.Count, "lifted once");
                Check.True(f.Pickup, "the pickup turned on");
                Check.True(f.Notes.Exists(n => n.Contains("Reagents: Item pickup turned on")), "noted");
                Check.Equal("Circuit ended (every reagent pile known within " + AssistFeatures.ReagentRangeDefault
                    + " tiles visited).", f.CircuitStatus, "the end");
                Check.Equal("1 piles visited, 1 picked up", f.ReagentStatus, "the status");
            }
            finally
            {
                Done(f);
            }
        });
    }

    const uint Orc = 0x00004F01;

    // An orc (body 0x11) 5 tiles east; the shard answers the flee's first step and the orc goes out of view.
    static void Threat()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, f);
            byte[] orc = Packets.Mobile(Orc, 1005, 1000);
            orc[5] = 0x00;
            orc[6] = 0x11;
            s.ServerSends(orc);
            s.Clock.Sleep(1);
            s.Answer = p => p[0] == 0x02
                ? ImmutableArray.Create(Packets.StepAck(p[2]), Packets.Delete(Orc))
                : ImmutableArray<byte[]>.Empty;
            try
            {
                Run(s, f);
                Check.True(f.Notes.Exists(n => n.Contains("Reagents: a threat within 5 tiles; running")), "ran");
                Check.True(f.Notes.Exists(n => n.Contains("Flee: clear after")), "clear");
                Check.Equal("Circuit ended (no reagent pile known within " + AssistFeatures.ReagentRangeDefault
                    + " tiles).", f.CircuitStatus, "went on to the end");
            }
            finally
            {
                Done(f);
            }
        });
    }

    static void FullPack()
    {
        foreach (int items in new[] { Inventory.ItemsMax - 1, Inventory.ItemsMax - 2 })
        {
            RecallTests.WithFeatures((s, f) =>
            {
                Kwik(s, f);
                for (uint i = 0; i < items; i++)
                    s.ServerSends(Packets.Contained(0x41100000 + i, 0x1EFD, 1, Pack));
                s.Clock.Sleep(1);
                try
                {
                    Run(s, f);
                    string want = items == Inventory.ItemsMax - 1
                        ? "Circuit ended (the pack holds " + items + " items, " + Inventory.ItemsMax + " at most)."
                        : "Circuit ended (no reagent pile known within " + AssistFeatures.ReagentRangeDefault
                        + " tiles).";
                    Check.Equal(want, f.CircuitStatus, items + " items");
                }
                finally
                {
                    Done(f);
                }
            });
        }
    }

    // Packets.Status's strength 85: a carry limit of 370.
    static void Heavy()
    {
        foreach (int weight in new[] { 370 - AssistFeatures.SewWeightRoom + 1, 370 - AssistFeatures.SewWeightRoom })
        {
            RecallTests.WithFeatures((s, f) =>
            {
                Kwik(s, f);
                byte[] st = Packets.Status(51);
                st[64] = (byte)(weight >> 8);
                st[65] = (byte)weight;
                s.ServerSends(st);
                s.Clock.Sleep(1);
                try
                {
                    Run(s, f);
                    string want = weight > 370 - AssistFeatures.SewWeightRoom
                        ? "Circuit ended (the pack weighs " + weight + " of 370 stones)."
                        : "Circuit ended (no reagent pile known within " + AssistFeatures.ReagentRangeDefault
                        + " tiles).";
                    Check.Equal(want, f.CircuitStatus, weight + " stones");
                }
                finally
                {
                    Done(f);
                }
            });
        }
    }

    // Over the test map: a pile 6 tiles east the walk's steps answered 500 ms late, stopped after the first step; then
    // a pile beside Kwik off the pickup's list, stopped during the pickup's wait.
    static void StopMid()
    {
        TestMap map = new();
        map.Run(() => RecallTests.WithFeatures((s, f) =>
        {
            f.Cfg.Set(f.Server + "res.seen", "106,100,4,3," + s.Clock.UtcNow.Ticks + ",3");
            StepShard step = new(s, map, 100, 100)
            {
                AnswerMs = 500
            };
            s.ServerSends(Packets.Wear(Pack, 0x0E75, 0x15, Packets.Player));
            s.ServerSends(Packets.Contents(Pack));
            s.ServerSends(Packets.Status(51));
            s.Clock.Sleep(1);
            try
            {
                Check.Null(f.CircuitReagents(), "CircuitReagents");
                for (int i = 0; i < 600 && step.Entered(101, 100) == 0 && f.CircuitRunning; i++)
                    s.Clock.Sleep(50);
                f.CircuitHalt("the test");
                for (int i = 0; i < 600 && f.CircuitRunning; i++)
                    s.Clock.Sleep(100);
                Check.Equal("Circuit ended (stopped).", f.CircuitStatus, "the walk stopped");
                Check.True(step.X < 106, "short of the pile: " + step.X);
                Check.False(f.Notes.Exists(n => n.Contains("was not reached")), "the stop is no unreached pile");
            }
            finally
            {
                Done(f);
            }
        }));
        map.Run(() => RecallTests.WithFeatures((s, f) =>
        {
            f.SetReagents(new[] { 0x0F7A });
            f.Cfg.Set(f.Server + "res.seen", "101,100,4,3," + s.Clock.UtcNow.Ticks + ",3");
            StepShard step = new(s, map, 100, 100);
            s.ServerSends(Packets.Wear(Pack, 0x0E75, 0x15, Packets.Player));
            s.ServerSends(Packets.Contents(Pack));
            s.ServerSends(Packets.Status(51));
            s.ServerSends(Lying(Shade, 0x0F88, 101, 100));
            s.Clock.Sleep(1);
            try
            {
                Check.Null(f.CircuitReagents(), "CircuitReagents");
                for (int i = 0; i < 600 && !f.CircuitStatus.StartsWith("Reagents: the pickup at")
                    && f.CircuitRunning; i++)
                    s.Clock.Sleep(50);
                f.CircuitHalt("the test");
                for (int i = 0; i < 600 && f.CircuitRunning; i++)
                    s.Clock.Sleep(100);
                Check.Equal("Circuit ended (stopped).", f.CircuitStatus, "the wait stopped");
                Check.False(f.Notes.Exists(n => n.Contains("was not picked up in")), "not left as unpicked");
            }
            finally
            {
                Done(f);
            }
        }));
    }

    static void Edges()
    {
        // Nightshade off the pickup's list: the pickup never lifts it, the pile is left once, and the circuit ends.
        RecallTests.WithFeatures((s, f) =>
        {
            List<uint> lifts = Kwik(s, f);
            f.SetReagents(new[] { 0x0F7A });
            s.ServerSends(Lying(Shade, 0x0F88, 1001, 1000));
            s.Clock.Sleep(1);
            try
            {
                Run(s, f);
                Check.Equal(0, lifts.Count, "never lifted");
                Check.Equal(1, f.Notes.FindAll(n => n.Contains("was not picked up in")).Count, "left once");
                Check.True(f.CircuitStatus.Contains("every reagent pile known"), "the end: " + f.CircuitStatus);
            }
            finally
            {
                Done(f);
            }
        });
        // None known.
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, f);
            try
            {
                Run(s, f);
                Check.Equal("Circuit ended (no reagent pile known within " + AssistFeatures.ReagentRangeDefault
                    + " tiles).", f.CircuitStatus, "the end");
            }
            finally
            {
                Done(f);
            }
        });
        // One past the range: never walked to.
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, f);
            f.ReagentRange = 5;
            f.Cfg.Set(f.Server + "res.seen", "1010,1000,4,3," + s.Clock.UtcNow.Ticks + ",3");
            try
            {
                Run(s, f);
                Check.Equal("Circuit ended (no reagent pile known within 5 tiles).", f.CircuitStatus, "the end");
                Check.False(f.Notes.Exists(n => n.Contains("1010,1000")), "never walked to");
            }
            finally
            {
                Done(f);
            }
        });
        // One three tiles off with no map to walk on: left, then the end.
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, f);
            f.Cfg.Set(f.Server + "res.seen", "1003,1000,4,3," + s.Clock.UtcNow.Ticks + ",3");
            try
            {
                Run(s, f);
                Check.True(f.Notes.Exists(n => n.Contains("Reagents: the pile at 1003,1000 was not reached")), "left");
                Check.True(f.CircuitStatus.Contains("no reagent pile known within"), "the end: " + f.CircuitStatus);
            }
            finally
            {
                Done(f);
            }
        });
    }
}
