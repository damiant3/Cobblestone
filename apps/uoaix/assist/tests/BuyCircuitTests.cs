#nullable enable
using System.Collections.Generic;
using System.Collections.Immutable;
using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace Uoaix.Assist.Tests;

// The buy circuit (AssistFeatures.CircuitBuy) and its purse: gold only from the bank box's top-level piles, gold in a
// bag being savings (Damian, 2026-10-24: "any gold pile in the top level of the bank is fine, just dont go digging in
// bags for gold ever"), and a purse the bank cannot fill ending the circuit, saying so ("the circuit should have
// reported it can't find the gold and stop").
public static class BuyCircuitTests
{
    const uint Pack = 0x42BE4F7C;
    const uint Box = 0x40006000;
    const uint Savings = 0x40006001;
    const uint SavingsGold = 0x40006002;
    const uint Pile = 0x40006003;
    const uint Rune = 0x40006004;
    const uint Vendor = 0x00290F00;
    const uint VendorBox = 0x40290F01;
    const uint VendorGold = 0x45D00001;

    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("buy: recalled into a mage shop with no gold, no keeper is asked and the circuit ends saying so",
            NoGoldAtShop),
        new TestCase("buy: a bank whose only gold is in a bag fills nothing, the savings untouched, and the circuit "
        + "ends saying so", OnlySavings),
        new TestCase("buy: the bank box's top-level pile fills the purse and the keeper is asked", TopLevelPile),
        new TestCase("trip: a purse the bank box cannot fill before a trip ends the tour saying so, the savings "
        + "untouched", TripNoGold),
        new TestCase("buy: an item the keeper's window just showed none of is not asked for; the next it holds is "
        + "bought", WindowShowedNone),
        new TestCase("buy: started in a town with no bank and no banker in hearing, \"bank\" is never said and the "
        + "circuit ends saying why", NoBankHere),
        new TestCase("buy: a shop whose known keepers' lists name nothing still wanted is not worth the trip; an "
        + "unread list or none known is", WorthTrip),
        new TestCase("buy: the shop runes bag in the bank box is carried from the purse's bank stop, its runes for "
        + "the walks", CarriesShopRunes),
        new TestCase("buy: a shop runes bag too full for the pack stays in the box and the circuit goes on to its "
        + "shops by the bank's runes", ShopRunesNoRoom),
        new TestCase("buy: a bank stop before a trip only for a purse short of what is wanted or a pack near its limit",
        BankBeforeTrip));

    public static ImmutableArray<TestCase> Cases => s_cases;

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

    static void Pack0(FakeShard s)
    {
        s.ServerSends(Packets.Wear(Pack, 0x0E75, 0x15, Packets.Player));
        s.ServerSends(Packets.Open(Pack));
        s.ServerSends(Packets.Contents(Pack));
        s.ServerSends(Packets.Contained(0x40006010, 0x0F86, 30, Pack));
        s.ServerSends(Packets.Contained(0x40006011, 0x0F7B, 30, Pack));
        s.ServerSends(Packets.Contained(0x40006012, 0x0F7A, 30, Pack));
        s.ServerSends(Packets.Status(51));
        s.Clock.Sleep(1);
    }

    // The circuit started, run until it ends or askedFor is said, then halted; its notes.
    static string Run(FakeShard s, AssistFeatures f, string? askedFor)
    {
        Check.Null(f.CircuitBuy(), "CircuitBuy");
        for (int i = 0; i < 600 && f.CircuitRunning && (askedFor is null || Asked(s, askedFor) == 0); i++)
            s.Clock.Sleep(100);
        f.CircuitHalt("the test is done");
        for (int i = 0; i < 600 && f.CircuitRunning; i++)
            s.Clock.Sleep(100);
        lock (f.Notes)
            return string.Join(" | ",
                f.Notes.FindAll(n => n.Contains("Buy:") || n.Contains("Circuit:") || n.Contains("Bank:")));
    }

    static void WithRegions(string name, string[] lines, Action body)
    {
        string path = Path.Combine(Path.GetTempPath(),
            "assist-tests-buy" + name + "-" + Environment.ProcessId + ".txt");
        File.WriteAllLines(path, lines);
        try
        {
            UoRegions.Load(path);
            body();
        }
        finally
        {
            UoRegions.Load(path + ".none");
            File.Delete(path);
        }
    }

    static void Shopping(AssistFeatures f, string city)
    {
        f.Cfg.Set("circuit.buy.list", "black pearl|0F7A|10|10");
        List<UoRegions.Shop> shops = AssistFeatures.AllShops("buy").FindAll(x => UoRegions.City(x.CX, x.CY) == city);
        Check.Equal(1, shops.Count, "the one mage shop of " + city);
        f.SetCircuitShopKeys("buy", new List<string> { shops[0].Key });
    }

    // The player out of every town at 1000,1000, a rune labelled "The Circle" (Othertown's mage shop, x 2960-2974, y
    // 614-628) in the pack, Morena in the shop; the cast lands at 2967,620 as RecallTests' shard does.
    static void NoGoldAtShop()
    {
        WithRegions("shop", new[]
        {
            "R 2900 560 200 200 -128 127 0 CITY_OTHERTOWN a b c d e Othertown",
            "R 2960 614 14 14 -128 127 0 MAGE_OTHERTOWN a b c d e The Circle",
        }, () => RecallTests.WithFeatures((s, f) =>
        {
            Shopping(f, "Othertown");
            s.ServerSends(Packets.LoginConfirm(1000, 1000, 0));
            Pack0(s);
            s.ServerSends(Packets.Contained(Rune, 0x1F14, 1, Pack));
            s.ServerSends(Packets.Speech(Rune, "", "The Circle", 6));
            VendorShard v = new(s, Vendor, VendorBox, 2968, 620, Pack, VendorGold, 0);
            s.ServerSends(Packets.Delete(VendorGold));
            v.Stock(0x40290F02, 0x0F7A, 20, 5, "Black Pearl");
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
                    return ImmutableArray.Create(Packets.Draw(2967, 620, 0, 0), Packets.Mobile(Vendor, 2968, 620));
                }
                return v.Answer(p);
            };
            string notes = Run(s, f, null);
            Check.Equal(2967, s.Link.State.X, "landed in The Circle");
            Check.Equal(0, Asked(s, VendorShard.Name + " buy"), "no keeper asked: " + notes);
            Check.True(notes.Contains("no gold in the pack to buy with at The Circle"), "the end said: " + notes);
        }));
    }

    // Testtown's bank (x and y 1000 to 1009) and mage shop (x and y 996 to 1001) both hold the player at 1000,1000, the
    // banker's tile the player's own; Morena in the shop at 1001,1000. Over TestMap's tile data, so a bag in the box
    // opens.
    static void InTown(bool pile, Action<FakeShard, AssistFeatures, BankShard, VendorShard> body)
    {
        WithRegions("bank", new[]
        {
            "R 900 900 200 200 -128 127 0 CITY_TESTTOWN a b c d e Testtown",
            "R 1000 1000 10 10 -128 127 0 BANK_TESTTOWN a b c d e Testtown bank",
            "R 996 996 6 6 -128 127 0 MAGE_TESTTOWN a b c d e The Circle",
        }, () => new TestMap().Run(() => RecallTests.WithFeatures((s, f) =>
        {
            Shopping(f, "Testtown");
            f.Cfg.Set(f.Server + "bankspot.1005,1005", "1000,1000");
            s.ServerSends(Packets.LoginConfirm(1000, 1000, 0));
            Pack0(s);
            VendorShard v = new(s, Vendor + 2, VendorBox + 2, 1001, 1000, Pack, VendorGold + 2, 0);
            s.ServerSends(Packets.Delete(VendorGold + 2));
            v.Stock(0x40290F12, 0x0F7A, 20, 5, "Black Pearl");
            BankShard b = new(s, Box);
            b.Put(Savings, TestMap.Bag, 1, Box);
            b.Put(SavingsGold, AssistFeatures.GoldArt, 5000, Savings);
            if (pile)
                b.Put(Pile, AssistFeatures.GoldArt, 1000, Box);
            s.Answer = p => b.Answer(p).AddRange(v.Answer(p));
            body(s, f, b, v);
        })));
    }

    static void OnlySavings()
    {
        InTown(false, (s, f, b, v) =>
        {
            string notes = Run(s, f, null);
            Check.Equal(5000, b.In(Savings, AssistFeatures.GoldArt), "the savings untouched");
            Check.Equal(0, Asked(s, VendorShard.Name + " buy"), "no keeper asked: " + notes);
            Check.True(notes.Contains("Circuit ended (the bank in Testtown: " + AssistFeatures.BankGoldNone + ")"),
                "the end said: " + notes);
        });
    }

    // The tour from Testtown's bank (as InTown, no shop) to Othertown's one tailor shop: TripReady before the recall
    // finds the pack with no gold and fills the purse from the box, whose only gold is a bag's.
    static void TripNoGold()
    {
        WithRegions("trip", new[]
        {
            "R 900 900 200 200 -128 127 0 CITY_TESTTOWN a b c d e Testtown",
            "R 1000 1000 10 10 -128 127 0 BANK_TESTTOWN a b c d e Testtown bank",
            "R 2900 560 200 200 -128 127 0 CITY_OTHERTOWN a b c d e Othertown",
            "R 2960 614 14 14 -128 127 0 CLOTHIER_OTHERTOWN a b c d e The Loom",
        }, () => new TestMap().Run(() => RecallTests.WithFeatures((s, f) =>
        {
            f.Cfg.Set(f.Server + "runes.shop", "");
            f.Cfg.Set(f.Server + "bankspot.1005,1005", "1000,1000");
            s.ServerSends(Packets.LoginConfirm(1000, 1000, 0));
            Pack0(s);
            // A rune for the town, so the tour's cycle holds it (BankHasRune reads the labels the run has kept).
            s.ServerSends(Packets.Contained(Rune + 1, 0x1F14, 1, Pack));
            s.ServerSends(Packets.Speech(Rune + 1, "", "Othertown", 6));
            s.Clock.Sleep(1);
            BankShard b = new(s, Box);
            b.Put(Savings, TestMap.Bag, 1, Box);
            b.Put(SavingsGold, AssistFeatures.GoldArt, 5000, Savings);
            Check.Null(f.CircuitTour(), "CircuitTour");
            for (int i = 0; i < 600 && f.CircuitRunning; i++)
                s.Clock.Sleep(100);
            f.CircuitHalt("the test is done");
            string notes;
            lock (f.Notes)
                notes = string.Join(" | ",
                    f.Notes.FindAll(n => n.Contains("Tour:") || n.Contains("Circuit:") || n.Contains("Bank:")));
            Check.Equal(5000, b.In(Savings, AssistFeatures.GoldArt), "the savings untouched");
            Check.True(notes.Contains("Circuit ended (" + AssistFeatures.BankGoldNone), "the end said: " + notes);
            Check.Equal(1000, s.Link.State.X, "no trip made");
        })));
    }

    // Morena holds black pearl only: the bloodmoss ask reads her window, so the mandrake root is not asked for, and the
    // black pearl is (2026-10-24 14:32:17, Jarrod asked three times in 3 s).
    static void WindowShowedNone()
    {
        InTown(true, (s, f, b, v) =>
        {
            f.Cfg.Set("circuit.buy.list", "bloodmoss|0F7B|10|10;mandrake root|0F86|10|10;black pearl|0F7A|10|10");
            string notes = Run(s, f, null);
            Check.True(notes.Contains("Morena, bloodmoss: the vendor has none"), "bloodmoss asked: " + notes);
            Check.True(notes.Contains("Morena, mandrake root: none in the window just read"),
                "mandrake root not asked: " + notes);
            Check.True(notes.Contains("Morena, black pearl: bought 10"), "black pearl bought: " + notes);
            Check.Equal(2, Asked(s, VendorShard.Name + " buy"), "asked twice: " + notes);
        });
    }

    // A house region by the telescope, a town of the region file with no bank (2026-10-24 14:48:27, "bank" said in the
    // house at 4700,1112); no rune carried, so no recall reaches a town with a bank.
    static void NoBankHere()
    {
        WithRegions("nobank", new[]
        {
            "R 900 900 200 200 -128 127 0 CITY_TELESCOPE a b c d e telescope in Testtown",
            "R 2960 614 14 14 -128 127 0 MAGE_OTHERTOWN a b c d e The Circle",
            "R 2900 560 200 200 -128 127 0 CITY_OTHERTOWN a b c d e Othertown",
        }, () => RecallTests.WithFeatures((s, f) =>
        {
            Shopping(f, "Othertown");
            s.ServerSends(Packets.LoginConfirm(1000, 1000, 0));
            Pack0(s);
            string notes = Run(s, f, null);
            Check.Equal(0, Asked(s, "bank"), "\"bank\" never said: " + notes);
            Check.True(notes.Contains(AssistFeatures.NoBankTown), "the end said: " + notes);
        }));
    }

    // Othertown's The Circle with Morena in it, known to the atlas by her click label; her list read as potions only,
    // then read holding black pearl (2026-10-24 14:55:33, a recall to Vesper's Circles of Magic, nobody there asked).
    static void WorthTrip()
    {
        WithRegions("worth", new[]
        {
            "R 2900 560 200 200 -128 127 0 CITY_OTHERTOWN a b c d e Othertown",
            "R 2960 614 14 14 -128 127 0 MAGE_OTHERTOWN a b c d e The Circle",
        }, () => RecallTests.WithFeatures((s, f) =>
        {
            UoRegions.Shop? shop = AssistFeatures.AllShops("buy").Find(x => x.Text == "The Circle");
            Check.True(shop is not null, "the shop");
            List<AssistFeatures.BuyItem> list = new()
            { new() { Name = "black pearl", Arts = new[] { 0x0F7A }, Count = 10, MaxPrice = 10 } };
            ValueMap<string, int> left = ValueMap<string, int>.Of(("black pearl", 10));
            Check.True(f.WorthTrip(shop!, list, left), "no keeper known there");
            s.ServerSends(Packets.LoginConfirm(2964, 620, 0));
            Pack0(s);
            VendorShard v = new(s, Vendor + 4, VendorBox + 4, 2965, 620, Pack, VendorGold + 4, 100);
            Check.True(f.Atlas!.Shops().Exists(n => n.Name == VendorShard.Name), "Morena in the atlas");
            Check.True(f.WorthTrip(shop!, list, left), "her list unread");
            v.Stock(0x40290F42, 0x0F0E, 10, 15, "Red Potion");
            Check.Equal("the vendor has none",
                f.VendorBuy(g => g == 0x0F7A, 10, out int b1, out int l1, VendorShard.Name), "her window");
            Check.False(f.WorthTrip(shop!, list, left),
                "her list names no black pearl: "
                + string.Join("|", f.Atlas.Shops().Find(n => n.Name == VendorShard.Name)!.Items));
            v.Stock(0x40290F43, 0x0F7A, 10, 5, "Black Pearl");
            f.VendorBuy(g => false, 0, out int b2, out int l2, VendorShard.Name);
            Check.True(f.WorthTrip(shop!, list, left), "her list names black pearl");
            left = left.Set("black pearl", 0);
            Check.False(f.WorthTrip(shop!, list, left), "nothing still wanted");
            left = left.Set("black pearl", 10);
            Check.Null(f.VendorBuy(g => g == 0x0F7A, 10, out int b3, out int l3, VendorShard.Name),
                "her black pearl bought out");
            // Past the atlas's joining of a list read in parts (AssistAtlas.StockJoinMs).
            s.Clock.Sleep(AssistAtlas.StockJoinMs + 1000);
            f.VendorBuy(g => false, 0, out int b4, out int l4, VendorShard.Name);
            Check.False(f.WorthTrip(shop!, list, left), "bought out: not worth the trip");
            s.Clock.Sleep((int)(KeepRules.RestockPeriodSeconds * 1000) + 1000);
            Check.True(f.WorthTrip(shop!, list, left), "a restock period on, her list counts as unread");
        }));
    }

    // The picked shop runes bag lies in the box with a rune in it (2026-10-24 15:00:47, "Moonglow Scholars Cut" left in
    // the box and a 228-tile walk taken).
    static void CarriesShopRunes()
    {
        const uint RunesBag = 0x40006020;
        const uint ShopRune = 0x40006021;
        InTown(true, (s, f, b, v) =>
        {
            b.Put(RunesBag, TestMap.Bag, 1, Box);
            b.Put(ShopRune, 0x1F14, 1, RunesBag);
            f.Cfg.Set(f.Server + "runes.shop", RunesBag.ToString("X"));
            try
            {
                string notes = Run(s, f, VendorShard.Name + " buy");
                uint inside;
                lock (s.Link.State.Gate)
                    inside = s.Link.State.Items.TryGet(RunesBag, out AssistItem? bag) ? bag.Container : 0;
                Check.Equal(Pack, inside, "the bag in the pack: " + notes);
            }
            finally
            {
                f.Cfg.Set(f.Server + "runes.shop", "");
            }
        });
    }

    // The fourth 2026-10-24 audit (2): a bag whose 130 runes would take the pack past its item limit ended the run, and
    // at every top of the tailoring circuit skipped the shops.
    static void ShopRunesNoRoom()
    {
        const uint RunesBag = 0x40006020;
        InTown(true, (s, f, b, v) =>
        {
            b.Put(RunesBag, TestMap.Bag, 1, Box);
            for (uint i = 0; i < 130; i++)
                b.Put(0x40006100 + i, 0x1F14, 1, RunesBag);
            f.Cfg.Set(f.Server + "runes.shop", RunesBag.ToString("X"));
            try
            {
                string notes = Run(s, f, VendorShard.Name + " buy");
                uint inside;
                lock (s.Link.State.Gate)
                    inside = s.Link.State.Items.TryGet(RunesBag, out AssistItem? bag) ? bag.Container : 0;
                Check.Equal(Box, inside, "the bag left in the box: " + notes);
                Check.True(notes.Contains("does not fit"), "the bag's room said: " + notes);
                Check.Equal(1, Asked(s, VendorShard.Name + " buy"), "the shop's keeper asked: " + notes);
                Check.False(notes.Contains("still does not fit"), "the first time is no repeat: " + notes);
                // Asked again with no room made: the bag is not looked for in the box again (L-FUTILITY), and the
                // circuit goes on.
                int banks = Asked(s, "bank");
                MethodInfo carry = typeof(AssistFeatures).GetMethod("CarryShopRunes",
                    BindingFlags.NonPublic | BindingFlags.Instance)!;
                Check.Null(carry.Invoke(f, new object[] { s.Link }), "goes on");
                Check.Equal(banks, Asked(s, "bank"), "no bank asked for the bag again");
                bool said;
                lock (f.Notes)
                    said = f.Notes.Exists(n => n.Contains("the shop runes bag still does not fit"));
                Check.True(said, "the repeat refused and said");
            }
            finally
            {
                f.Cfg.Set(f.Server + "runes.shop", "");
            }
        });
    }

    static void BankBeforeTrip()
    {
        Check.False(AssistFeatures.BankBeforeTrip(2000, 3000, 2000, 100, 360), "a full purse, a light pack: no bank");
        Check.True(AssistFeatures.BankBeforeTrip(1999, 3000, 2000, 100, 360),
            "a gold short of the buy purse: the bank");
        Check.False(AssistFeatures.BankBeforeTrip(500, 500, 2000, 100, 360),
            "the purse pays for all still wanted: no bank");
        Check.True(AssistFeatures.BankBeforeTrip(499, 500, 2000, 100, 360),
            "a gold short of all still wanted: the bank");
        Check.True(AssistFeatures.BankBeforeTrip(2000, 3000, 2000, 324, 360),
            "nine tenths of the carry limit: the bank");
        Check.False(AssistFeatures.BankBeforeTrip(2000, 3000, 2000, 323, 360), "just under it: no bank");
        Check.False(AssistFeatures.BankBeforeTrip(2000, 3000, 2000, 500, 0),
            "no carry limit known: weight says nothing");
    }

    static void TopLevelPile()
    {
        InTown(true, (s, f, b, v) =>
        {
            string notes = Run(s, f, VendorShard.Name + " buy");
            Check.Equal(0, b.In(Box, AssistFeatures.GoldArt), "the pile taken: " + notes);
            Check.Equal(5000, b.In(Savings, AssistFeatures.GoldArt), "the savings untouched");
            Check.True(Asked(s, VendorShard.Name + " buy") >= 1, "the keeper asked: " + notes);
        });
    }
}
