#nullable enable
using System.Collections.Immutable;
using System;

namespace Uoaix.Assist.Tests;

// The purse filled from a bank box (AssistFeatures.PurseTo over BankTake) against a shard that keeps one (BankShard):
// from the box's own piles only, gold in a bag being savings (Damian, 2026-10-20: "any gold in bags in the bank are
// savings and are not to be touched by any agent").
public static class PurseFillTests
{
    const uint Pack = 0x42BE4F7C;
    const uint Box = 0x40005000;
    const uint Pile = 0x40005001;
    const uint Savings = 0x40005002;
    const uint SavingsGold = 0x40005003;
    const int Target = 300;

    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("purse fill: the box's pile brings the pack to the target, the rest left in the box", FromPile),
        new TestCase("purse fill: a pile of exactly what is short is taken whole", PileExact),
        new TestCase("purse fill: a pile short of it is taken whole, and a short box is no failure", PileShort),
        new TestCase("purse fill: gold in a bag in the box is savings, never lifted", SavingsKept));

    public static ImmutableArray<TestCase> Cases => s_cases;

    // Over TestMap's tile data, so a bag in the box is a container the bank's walk opens (IsContainer): with no tile
    // data no bag opens, and the savings case passed with the savings rule broken.
    static void Bank(Action<FakeShard, AssistFeatures, BankShard> body)
    {
        new TestMap().Run(() => RecallTests.WithFeatures((s, f) =>
        {
            s.ServerSends(Packets.LoginConfirm(1000, 1000, 0));
            s.ServerSends(Packets.Wear(Pack, 0x0E75, 0x15, Packets.Player));
            s.ServerSends(Packets.Open(Pack));
            s.ServerSends(Packets.Contents(Pack));
            s.Clock.Sleep(1);
            body(s, f, new BankShard(s, Box));
        }));
    }

    static int PackGold(FakeShard s)
    {
        int n = 0;
        lock (s.Link.State.Gate)
        {
            foreach (AssistItem it in s.Link.State.Items.Values)
            {
                if (it.Container == Pack && it.Graphic == AssistFeatures.GoldArt)
                    n += it.Amount;
            }
        }
        return n;
    }

    static void FromPile()
    {
        Bank((s, f, b) =>
        {
            b.Put(Pile, AssistFeatures.GoldArt, 1000, Box);
            Check.Null(f.PurseTo(Target, out string taken), "PurseTo");
            Check.Equal(Target, PackGold(s), "the pack's gold");
            Check.Equal(1000 - Target, b.In(Box, AssistFeatures.GoldArt), "the box's gold");
            Check.Equal(1, b.Lifts, "one lift");
        });
    }

    static void PileExact()
    {
        Bank((s, f, b) =>
        {
            b.Put(Pile, AssistFeatures.GoldArt, Target, Box);
            Check.Null(f.PurseTo(Target, out string taken), "PurseTo");
            Check.Equal(Target, PackGold(s), "the pack's gold");
            Check.Equal(0, b.In(Box, AssistFeatures.GoldArt), "the box empty of gold");
        });
    }

    static void PileShort()
    {
        Bank((s, f, b) =>
        {
            b.Put(Pile, AssistFeatures.GoldArt, Target - 1, Box);
            Check.Null(f.PurseTo(Target, out string taken), "a short box is no failure");
            Check.Equal(Target - 1, PackGold(s), "the pack's gold");
            Check.Equal(1, b.Lifts, "the pile lifted once (L-FUTILITY)");
        });
    }

    static void SavingsKept()
    {
        Bank((s, f, b) =>
        {
            b.Put(Savings, TestMap.Bag, 1, Box);
            b.Put(SavingsGold, AssistFeatures.GoldArt, 5000, Savings);
            Check.Null(f.PurseTo(Target, out string taken), "PurseTo");
            Check.Equal(0, PackGold(s), "nothing in the pack");
            Check.Equal(5000, b.In(Savings, AssistFeatures.GoldArt), "the savings untouched");
            Check.Equal(0, b.Lifts, "nothing lifted");
            Check.True(b.Opened(Savings), "the bag opened, its gold in view");
        });
    }
}
