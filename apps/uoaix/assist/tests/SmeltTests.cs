#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using System.Threading;

namespace Uoaix.Assist.Tests;

// The smelting loop on its last targets (AssistFeatures.RunSmelt) against a shard that keeps the ore pile, the bag and
// the smelts: uo1998's ore.m makes metal / 2 ingots, so the smallest ore (19B7) goes two at a time, every other one.
public static class SmeltTests
{
    const uint Pack = 0x42BE4F7C;
    const uint Bag = 0x45F00001;
    const uint Pile = 0x45F00002;
    const int PileX = 101, PileY = 100;

    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("smelt: the smallest ore (19B7) is lifted two at a time, and each pair smelts", SmallTakesTwo),
        new TestCase("smelt: a larger ore (19B9) is lifted one at a time", LargeTakesOne));

    public static ImmutableArray<TestCase> Cases => s_cases;

    static uint U32(byte[] p, int at) => (uint)(p[at] << 24 | p[at + 1] << 16 | p[at + 2] << 8 | p[at + 3]);

    static byte[] Lying(uint serial, int graphic, int amount, int x, int y)
    {
        uint s = serial | 0x80000000;
        return new byte[] { 0x1A, 0, 16, (byte)(s >> 24), (byte)(s >> 16), (byte)(s >> 8), (byte)s,
            (byte)(graphic >> 8), (byte)graphic, (byte)(amount >> 8), (byte)amount, (byte)(x >> 8), (byte)x,
            (byte)(y >> 8), (byte)y, 0 };
    }

    static void SmallTakesTwo()
    {
        List<int> lifts = Smelt(AssistFeatures.SmallOre, 6, 2, out int smelts, out string status);
        Check.Equal(2, lifts.Count, "two lifts: " + status);
        Check.True(lifts.TrueForAll(a => a == 2), "each of 2: " + string.Join(",", lifts));
        Check.Equal(2, smelts, "two smelts: " + status);
    }

    static void LargeTakesOne()
    {
        List<int> lifts = Smelt(0x19B9, 6, 2, out int smelts, out string status);
        Check.Equal(2, lifts.Count, "two lifts: " + status);
        Check.True(lifts.TrueForAll(a => a == 1), "each of 1: " + string.Join(",", lifts));
        Check.Equal(2, smelts, "two smelts: " + status);
    }

    // Kwik at 100,100, the bag in the pack, a pile of amount of art beside him, a forge east; rounds of the loop.
    static List<int> Smelt(int art, int amount, int rounds, out int smelts, out string status)
    {
        List<int> lifts = new();
        int done = 0;
        string end = "";
        RecallTests.WithFeatures((s, f) =>
        {
            Lock gate = new();
            int pileLeft = amount;
            uint next = 0x45F00100, inBag = 0;
            s.Answer = p =>
            {
                lock (gate)
                {
                    if (p[0] == 0x07 && U32(p, 1) == Pile)
                    {
                        lifts.Add(p[5] << 8 | p[6]);
                        return ImmutableArray<byte[]>.Empty;
                    }
                    if (p[0] == 0x08 && U32(p, 1) == Pile && U32(p, 10) == Bag)
                    {
                        int took = lifts.Count > 0 ? lifts[lifts.Count - 1] : 1;
                        pileLeft -= took;
                        inBag = next++;
                        return ImmutableArray.Create(Packets.Contained(inBag, art, took, Bag),
                            Lying(Pile, art, pileLeft, PileX, PileY));
                    }
                    if (p[0] == 0x06 && (U32(p, 1) & 0x7FFFFFFF) == inBag)
                        return ImmutableArray.Create<byte[]>(Packets.Cursor(0x00001234, 0x01));
                    if (p[0] == 0x6C)
                    {
                        uint ore = inBag;
                        done++;
                        s.Also(300, () =>
                        {
                            s.ServerSends(Packets.Speech(0xFFFFFFFF, "", "You smelt the ore into ingots.", 0));
                            s.ServerSends(Packets.Delete(ore));
                            s.ServerSends(Packets.Contained(0x45F00090, 0x1BF2, done, Pack));
                        });
                    }
                    return ImmutableArray<byte[]>.Empty;
                }
            };
            s.ServerSends(Packets.LoginConfirm(100, 100, 0));
            s.ServerSends(Packets.Wear(Pack, 0x0E75, 0x15, Packets.Player));
            s.ServerSends(Packets.Open(Pack));
            s.ServerSends(Packets.Contents(Pack));
            s.ServerSends(Packets.Contained(Bag, 0x0E76, 1, Pack));
            s.ServerSends(Lying(Pile, art, amount, PileX, PileY));
            s.Clock.Sleep(1);
            LoopKind smelt = AssistFeatures.Find("Smelting (one ore at a time)")!;
            BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            Type ltType = typeof(AssistFeatures).GetNestedType("LastTargets", BindingFlags.NonPublic)!;
            Type stepType = typeof(AssistFeatures).GetNestedType("Step", BindingFlags.NonPublic)!;
            object step = Activator.CreateInstance(stepType)!;
            stepType.GetField("Target")!.SetValue(step, true);
            stepType.GetField("Ground")!.SetValue(step, true);
            stepType.GetField("X")!.SetValue(step, 102);
            stepType.GetField("Y")!.SetValue(step, 100);
            stepType.GetField("Graphic")!.SetValue(step, 0x0FB1);
            stepType.GetField("Words")!.SetValue(step, "pick the forge at 102,100");
            object lt = Activator.CreateInstance(ltType)!;
            ltType.GetField("Pile")!.SetValue(lt, new AssistItem { Serial = Pile, Graphic = art, X = PileX, Y = PileY,
                Amount = amount });
            ltType.GetField("Bag")!.SetValue(lt, Bag);
            ltType.GetField("Forge")!.SetValue(lt, step);
            typeof(AssistFeatures).GetMethod("Keep", flags)!.Invoke(f, new object?[] { smelt, lt });
            bool whole = f.WholePile;
            f.WholePile = false;
            try
            {
                Check.Null(f.Start(smelt, rounds, 0, "", true), "Start on the last targets");
                for (int i = 0; i < 1200 && !f.Status.Contains(" done") && !f.Status.Contains(" stopped"); i++)
                    s.Clock.Sleep(100);
                end = f.Status;
            }
            finally
            {
                f.Stop("the test is done");
                for (int i = 0; i < 100 && !f.Status.Contains(" done") && !f.Status.Contains(" stopped"); i++)
                    s.Clock.Sleep(100);
                f.WholePile = whole;
            }
        });
        smelts = done;
        status = end;
        return lifts;
    }
}
