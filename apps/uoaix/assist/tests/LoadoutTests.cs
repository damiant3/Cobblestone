#nullable enable
using System.Collections.Immutable;

namespace Uoaix.Assist.Tests;

// The reagent loadout (AssistFeatures.LoadoutGo, LoadoutRun; Damian, 2026-10-22: "Reagent loadout status should turn
// the screen green like a loop and go back to gray when done, and the status text should be explicit and say Done,
// Success: Stored 0, ...."): LoadoutRunning holds while it runs (the window's green border), and its end line says how
// it ended.
public static class LoadoutTests
{
    const uint Pack = 0x42BE4F7C;
    const uint Bag = 0x43DD0F1A;
    const uint Storage = 0x43DD0F1B;
    const uint BagPearl = 0x46000001;
    const uint StorePearl = 0x46000002;
    const int Pearl = 0x0F7A;

    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("loadout: a refill runs with LoadoutRunning set and ends \"Done, Success\" with it clear",
            Success),
        new TestCase("loadout: an excess stored says \"Done, Success: Stored 5, Took 0\"", Stored),
        new TestCase("loadout: an excess into a storage bag holding none of it says \"Done, Success: Stored 5\"",
        StoredFresh),
        new TestCase("loadout: a refused lift while storing says \"Done, Failed\"", StoreRefused),
        new TestCase("loadout: storage short of the want says \"Done, Short\" and what it is short of", Short),
        new TestCase("loadout: a refused lift says \"Done, Failed\" with the counts so far, LoadoutRunning clear",
        Failed),
        new TestCase("loadout: a take the server leaves where it lay says \"Done, Failed\", dropped once", TakePutBack),
        new TestCase("loadout: a store the server leaves where it lay says \"Done, Failed\", dropped once",
        StorePutBack),
        new TestCase("loadout: a whole stack stored onto the storage pile is counted moved", WholeStack),
        new TestCase("loadout: the bag's count is -1 unset, -1 for a bag not seen, else its pearls", Have),
        new TestCase("loadout: a second start while a refill runs is refused, and the one refill ends", Twice));

    public static ImmutableArray<TestCase> Cases => s_cases;

    // The pack holding the loadout bag (inBag pearls) and the storage bag (inStore pearls); every want 0 but pearl's.
    // A drop moves the lifted pearls as uo1998 does: the stack lifted from shrinks (or goes, 0x1D), the pile dropped on
    // grows, a drop on a bag makes a new pile there; ignoreDrop answers a drop with nothing, the pearls left where they
    // lay.
    static void Setup(FakeShard s, AssistFeatures f, int inBag, int inStore, int want, bool refuseLift,
        bool ignoreDrop = false)
    {
        s.ServerSends(Packets.LoginConfirm(1000, 1000, 0));
        s.ServerSends(Packets.Wear(Pack, 0x0E75, 0x15, Packets.Player));
        s.ServerSends(Packets.Contained(Bag, 0x0E76, 1, Pack));
        s.ServerSends(Packets.Contained(Storage, 0x0E76, 1, Pack));
        if (inBag > 0)
            s.ServerSends(Packets.Contained(BagPearl, Pearl, inBag, Bag));
        if (inStore > 0)
            s.ServerSends(Packets.Contained(StorePearl, Pearl, inStore, Storage));
        s.Clock.Sleep(1);
        foreach (int g in AssistFeatures.LoadoutReagents())
            f.SetLoadoutWant(g, g == Pearl ? want : 0);
        f.Cfg.Set("loadout.bag", Bag.ToString("X"));
        f.Cfg.Set("loadout.storage", Storage.ToString("X"));
        ValueMap<uint, (int Amount, uint Container)> piles = ValueMap<uint, (int Amount, uint Container)>.Empty;
        if (inBag > 0)
            piles = piles.Set(BagPearl, (inBag, Bag));
        if (inStore > 0)
            piles = piles.Set(StorePearl, (inStore, Storage));
        uint lifted = 0;
        int liftedAmount = 0;
        uint next = 0x46000100;
        // A bag's double-click opens it (0x24); a lift is taken silently or refused (27 00).
        s.Answer = p =>
        {
            if (p[0] == 0x06)
                return ImmutableArray.Create<byte[]>(Packets.Open(U32(p, 1)));
            if (p[0] == 0x07 && refuseLift)
                return ImmutableArray.Create<byte[]>(new byte[] { 0x27, 0x00 });
            if (p[0] == 0x07)
            {
                lifted = U32(p, 1);
                liftedAmount = p[5] << 8 | p[6];
                return ImmutableArray<byte[]>.Empty;
            }
            if (p[0] != 0x08 || ignoreDrop || !piles.TryGet(lifted, out (int Amount, uint Container) from))
                return ImmutableArray<byte[]>.Empty;
            uint onto = U32(p, 10);
            ImmutableArray<byte[]>.Builder sent = ImmutableArray.CreateBuilder<byte[]>();
            int left = from.Amount - liftedAmount;
            piles = left > 0 ? piles.Set(lifted, (left, from.Container)) : piles.Remove(lifted);
            sent.Add(left > 0 ? Packets.Contained(lifted, Pearl, left, from.Container) : Delete(lifted));
            if (piles.TryGet(onto, out (int Amount, uint Container) pile))
            {
                piles = piles.Set(onto, (pile.Amount + liftedAmount, pile.Container));
                sent.Add(Packets.Contained(onto, Pearl, pile.Amount + liftedAmount, pile.Container));
            }
            else
            {
                uint made = left > 0 ? next++ : lifted;
                piles = piles.Set(made, (liftedAmount, onto));
                sent.Add(Packets.Contained(made, Pearl, liftedAmount, onto));
            }
            return sent.ToImmutable();
        };
    }

    static uint U32(byte[] p, int at) => (uint)(p[at] << 24 | p[at + 1] << 16 | p[at + 2] << 8 | p[at + 3]);

    static byte[] Delete(uint serial) => new byte[] { 0x1D, (byte)(serial >> 24), (byte)(serial >> 16),
        (byte)(serial >> 8), (byte)serial };

    // The run started and waited out on the virtual clock: whether LoadoutRunning was seen set, and the end line.
    static string Run(FakeShard s, AssistFeatures f, out bool seenRunning)
    {
        Check.Null(f.LoadoutGo(), "LoadoutGo");
        seenRunning = false;
        for (int i = 0; i < 2000 && !f.LoadoutStatus.StartsWith("Done"); i++)
        {
            seenRunning |= f.LoadoutRunning;
            s.Clock.Sleep(50);
        }
        for (int i = 0; i < 100 && f.LoadoutRunning; i++)
            s.Clock.Sleep(50);
        return f.LoadoutStatus;
    }

    static void Success()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Setup(s, f, 2, 30, 10, false);
            string end = Run(s, f, out bool seen);
            Check.True(seen, "LoadoutRunning while it ran");
            Check.False(f.LoadoutRunning, "LoadoutRunning after");
            Check.True(end.StartsWith("Done, Success: Stored 0, Took 8; loaded 10 "), "end: " + end);
        });
    }

    static void Twice()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Setup(s, f, 2, 30, 10, false);
            Check.Null(f.LoadoutGo(), "LoadoutGo");
            Check.Equal("the reagent loadout is running", f.LoadoutGo(), "a second refused");
            for (int i = 0; i < 2000 && !f.LoadoutStatus.StartsWith("Done"); i++)
                s.Clock.Sleep(50);
            Check.True(f.LoadoutStatus.StartsWith("Done, Success: Stored 0, Took 8; loaded 10 "),
                "one refill: " + f.LoadoutStatus);
        });
    }

    static void Stored()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Setup(s, f, 15, 5, 10, false);
            string end = Run(s, f, out bool _);
            Check.True(end.StartsWith("Done, Success: Stored 5, Took 0; loaded 10 "), "end: " + end);
        });
    }

    static void StoredFresh()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Setup(s, f, 15, 0, 10, false);
            string end = Run(s, f, out bool _);
            Check.True(end.StartsWith("Done, Success: Stored 5, Took 0; loaded 10 "), "end: " + end);
        });
    }

    static void StoreRefused()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Setup(s, f, 15, 0, 10, true);
            string end = Run(s, f, out bool _);
            Check.False(f.LoadoutRunning, "LoadoutRunning after");
            Check.True(end.StartsWith("Done, Failed: ") && end.EndsWith("(stored 0, took 0)"), "end: " + end);
        });
    }

    static void Short()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Setup(s, f, 0, 4, 10, false);
            string end = Run(s, f, out bool _);
            Check.True(end.StartsWith("Done, Short: Stored 0, Took 4; loaded 4 ") && end.EndsWith(" (4 of 10)."),
                "end: " + end);
        });
    }

    static void Failed()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Setup(s, f, 0, 30, 10, true);
            string end = Run(s, f, out bool _);
            Check.False(f.LoadoutRunning, "LoadoutRunning after");
            Check.True(end.StartsWith("Done, Failed: ") && end.EndsWith("(stored 0, took 0)"), "end: " + end);
        });
    }

    static int Drops(FakeShard s)
    {
        int n = 0;
        foreach (byte[] p in s.ServerGot)
            n += p[0] == 0x08 ? 1 : 0;
        return n;
    }

    static void TakePutBack()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Setup(s, f, 2, 30, 10, false, ignoreDrop: true);
            string end = Run(s, f, out bool _);
            Check.Equal("Done, Failed: the server put 8 black pearl back where they lay (stored 0, took 0)", end,
                "end");
            Check.Equal(1, Drops(s), "drops sent");
        });
    }

    static void StorePutBack()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Setup(s, f, 15, 5, 10, false, ignoreDrop: true);
            string end = Run(s, f, out bool _);
            Check.Equal("Done, Failed: the server put 5 black pearl back where they lay (stored 0, took 0)", end,
                "end");
            Check.Equal(1, Drops(s), "drops sent");
        });
    }

    static void WholeStack()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Setup(s, f, 5, 5, 0, false);
            string end = Run(s, f, out bool _);
            Check.True(end.StartsWith("Done, Success: Stored 5, Took 0"), "end: " + end);
        });
    }

    static void Have()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Check.Equal(-1, f.LoadoutHave(Pearl), "no bag set");
            f.Cfg.Set("loadout.bag", "44AC9999");
            Check.Equal(-1, f.LoadoutHave(Pearl), "a bag not seen");
            Setup(s, f, 7, 0, 10, false);
            Check.Equal(7, f.LoadoutHave(Pearl), "the bag's pearls");
        });
    }
}
