#nullable enable
using System.Collections.Immutable;

namespace Uoaix.Assist.Tests;

// The Bags tab's reading of a container (AssistFeatures.BagWhere): opened once, its count; never opened, "not opened
// yet". The empty bag of uo1998 2026-10-22 16:26:09 (the mining toolbag 44831E78: 24, then 3C 0000) read "not opened
// yet" however often it was opened.
public static class BagsTests
{
    const uint Pack = 0x42BE4F7C;
    const uint Bag = 0x44831E78;
    const uint Other = 0x4647DA85;
    const uint Shovel = 0x46000001;

    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("bags: an empty bag opened reads 0 items (16:26:09)", EmptyOpened),
        new TestCase("bags: a bag never opened reads \"not opened yet\"", NeverOpened),
        new TestCase("bags: a bag opened with items reads their count", FullOpened),
        new TestCase("bags: a list with items names its own bag, not the one opened before it", ListNamesItsOwn),
        new TestCase("bags: an empty list with no open before it names no bag", EmptyListAlone),
        new TestCase("bags: a shop runes bag in a bag in the pack sets, again unsets; the bank store there is refused",
        RuneBagInPack));

    public static ImmutableArray<TestCase> Cases => s_cases;

    static void Kwik(FakeShard s)
    {
        s.ServerSends(Packets.LoginConfirm(1000, 1000, 0));
        s.ServerSends(Packets.Wear(Pack, 0x0E75, 0x15, Packets.Player));
        s.ServerSends(Packets.Contained(Bag, 0x0E76, 1, Pack));
        s.ServerSends(Packets.Contained(Other, 0x0E76, 1, Pack));
        s.Clock.Sleep(1);
    }

    // The reading after the bag's name: the name is the client's tile data, which the suites do not load.
    static string Read(FakeShard s, AssistFeatures f, uint bag, string want)
    {
        Wait.Until(s.Clock, () => f.BagWhere(bag).EndsWith(want), 2000, "the bag's reading", stepMs: 10);
        return Unnamed(f, bag);
    }

    static string Unnamed(AssistFeatures f, uint bag)
    {
        string w = f.BagWhere(bag);
        return w.Substring(w.IndexOf(' ') + 1);
    }

    // 0x6C answered on an item: type 0, the cursor's id, flags, the serial, x, y, z and the graphic.
    static byte[] TargetItem(uint cursor, uint serial)
    {
        byte[] p = new byte[19];
        p[0] = 0x6C;
        p[2] = (byte)(cursor >> 24);
        p[3] = (byte)(cursor >> 16);
        p[4] = (byte)(cursor >> 8);
        p[5] = (byte)cursor;
        p[7] = (byte)(serial >> 24);
        p[8] = (byte)(serial >> 16);
        p[9] = (byte)(serial >> 8);
        p[10] = (byte)serial;
        return p;
    }

    // uo1998 2026-10-24 12:54:54: the shop runes bag 43DD0F1E in the backpack 44831E78 in the pack, targeted twice,
    // never set ("shop runes in bags wont set").
    static void RuneBagInPack()
    {
        const uint RuneBag = 0x43DD0F1E;
        const uint ShopCursor = 0x52534850, StoreCursor = 0x4253544F;
        new TestMap().Run(() => RecallTests.WithFeatures((s, f) =>
        {
            try
            {
                Kwik(s);
                s.ServerSends(Packets.Contained(RuneBag, TestMap.Bag, 1, Bag));
                s.Clock.Sleep(1);
                Check.Null(f.BankBagTarget("shoprunes"), "the cursor");
                s.ClientSends(TargetItem(ShopCursor, RuneBag));
                s.Clock.Sleep(1);
                Check.True(f.BankBags("shoprunes").Contains(RuneBag), "set: " + f.BankBagStatus);
                Check.Null(f.BankBagTarget("shoprunes"), "the cursor again");
                s.ClientSends(TargetItem(ShopCursor, RuneBag));
                s.Clock.Sleep(1);
                Check.Equal(0, f.BankBags("shoprunes").Count, "set again: removed");
                Check.Null(f.BankBagTarget("bankstore"), "the store's cursor");
                s.ClientSends(TargetItem(StoreCursor, RuneBag));
                s.Clock.Sleep(1);
                Check.Equal(0, f.BankBags("bankstore").Count, "the bank store in the pack refused");
                Check.True(f.BankBagStatus.Contains("not one in the pack"), "said: " + f.BankBagStatus);
            }
            finally
            {
                f.Cfg.Set(f.Server + "runes.shop", "");
                f.Cfg.Set(f.Server + "bank.store", "");
            }
        }));
    }

    static void EmptyOpened()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s);
            s.ServerSends(Packets.Open(Bag));
            s.ServerSends(Packets.Contents(Bag));
            Check.Equal("44831E78, in the pack, 0 items", Read(s, f, Bag, "0 items"), "BagWhere");
        });
    }

    static void NeverOpened()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s);
            Check.Equal("44831E78, in the pack, not opened yet", Read(s, f, Bag, "not opened yet"), "BagWhere");
        });
    }

    static void FullOpened()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s);
            s.ServerSends(Packets.Open(Bag));
            s.ServerSends(Packets.Contained(Shovel, 0x0F39, 1, Bag));
            s.ServerSends(Packets.Contents(Bag, Shovel));
            Check.Equal("44831E78, in the pack, 1 items", Read(s, f, Bag, "1 items"), "BagWhere");
        });
    }

    static void ListNamesItsOwn()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s);
            s.ServerSends(Packets.Open(Bag));
            s.ServerSends(Packets.Contained(Shovel, 0x0F39, 1, Other));
            s.ServerSends(Packets.Contents(Other, Shovel));
            Check.Equal("4647DA85, in the pack, 1 items", Read(s, f, Other, "1 items"), "BagWhere other");
            Check.Equal("44831E78, in the pack, not opened yet", Unnamed(f, Bag), "BagWhere bag");
        });
    }

    // The open is used once: a later empty list (no open before it) is not put on the bag opened earlier.
    static void EmptyListAlone()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s);
            s.ServerSends(Packets.Open(Bag));
            s.ServerSends(Packets.Contained(Shovel, 0x0F39, 1, Other));
            s.ServerSends(Packets.Contents(Other, Shovel));
            Read(s, f, Other, "1 items");
            s.ServerSends(Packets.Contents(0));
            s.Clock.Sleep(50);
            Check.Equal("44831E78, in the pack, not opened yet", Unnamed(f, Bag), "BagWhere bag");
        });
    }
}
