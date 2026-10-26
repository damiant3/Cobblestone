#nullable enable
using System.Collections.Generic;
using System.Collections.Immutable;
using System;
using System.IO;

namespace Uoaix.Assist.Tests;

// Only a tailor sells thread or buys shirts (Damian, 2026-10-24: "only tailors have thread. weavers never do. only
// tailors buy shirts, weavers never do."): AssistFeatures.TailorStock and CityBuyers.
public static class TailorTitleTests
{
    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("tailor title: a titled keeper is a tailor by its title alone, whatever its stock; a bare name "
            + "by its stock, an unread one counting", ByTitle),
        new TestCase("tailor title: the shirt buyers of a town are its tailors; a blacksmith or a weaver listing the "
        + "shirt is none, a bare name listing it is one", Buyers),
        new TestCase("tailor title: a shop's keepers asked with no tailor known are its tailors only; a weaver by its "
        + "read stock and a blacksmith by its title never", Keepers),
        new TestCase("tailor title: Watch me sell asks the vendor of a hand sale by its first name, titled or bare; "
        + "\"vendor\" for none", WatchVendor),
        new TestCase("tailor title: the Shops tab types a tailor shop's keepers tailor or weaver, never clothier; a "
        + "title by its title, another shop by its kind", ShopTrade));

    // Testtown's clothier (x and y 998 to 1004), a Spinning Wheel (WEAVER_, 1010 to 1016) and a smithy (1020 to 1026).
    static void ShopTrade()
    {
        string path = Path.Combine(Path.GetTempPath(), "assist-tests-shoptrade-" + Environment.ProcessId + ".txt");
        File.WriteAllLines(path, new[]
        {
            "R 900 900 200 200 -128 127 0 CITY_TESTTOWN a b c d e Testtown",
            "R 998 998 7 7 -128 127 0 CLOTHIER_TESTTOWN a b c d e The Stitch",
            "R 1010 1010 7 7 -128 127 0 WEAVER_TESTTOWN a b c d e The Wheel",
            "R 1020 1020 7 7 -128 127 0 BLACKSMITH_TESTTOWN a b c d e The Anvil",
        });
        try
        {
            UoRegions.Load(path);
            AtlasNpc At(string name, string sub, int x, params string[] items) => new()
            { Name = name, Sub = sub, Type = "Shopkeeper", X = x, Y = x, Items = new List<string>(items) };
            Check.Equal("tailor", AssistWindow.Kind(At("Kimmie", "untitled", 1000, "Spool of thread: 3 gp x 19")),
                "a bare tailor in the clothier");
            Check.Equal("weaver", AssistWindow.Kind(At("Judd", "untitled", 1000, "Dyes: 8 gp x 10")),
                "a bare weaver in the clothier");
            Check.Equal("tailor", AssistWindow.Kind(At("Lynn", "untitled", 1000)), "a bare keeper, its stock unread");
            Check.Equal("tailor", AssistWindow.Kind(At("Morena", "untitled", 1012, "Sewing kit: 3 gp x 10")),
                "a bare tailor in the Spinning Wheel");
            Check.Equal("weaver", AssistWindow.Kind(At("Bertram", "untitled", 1012, "Ball of yarn: 15 gp x 10")),
                "a bare weaver in the Spinning Wheel");
            Check.Equal("tailor", AssistWindow.Kind(At("Stanley the tailor", "tailor", 1000)), "a titled tailor");
            Check.Equal("blacksmith", AssistWindow.Kind(At("William", "untitled", 1022, "Spool of thread: 3 gp x 19")),
                "another shop by its kind, whatever its stock");
            Check.Equal("untitled", AssistWindow.Kind(At("Nobody", "untitled", 950)), "outside every shop");
            Check.Equal("weaver", AssistWindow.Kind(At("Neale", "unnamed", 1000, "Dyes: 8 gp x 10")), "unnamed");
            Check.Equal("weaver", AssistWindow.Kind(At("Erica", "", 1000, "Dyes: 8 gp x 10")), "no title kept");
            AtlasNpc hired = At("Bob the vendor", "vendor", 1000, "Dyes: 8 gp x 10");
            Check.Equal("weaver", AssistWindow.Kind(hired), "titled the vendor, not a player's");
            hired.Type = "Player vendor";
            Check.Equal("weaver", AssistWindow.Kind(hired), "a player's vendor, its wares unknown");
            hired.Wares = "reagents";
            Check.Equal("reagents", AssistWindow.Kind(hired), "a player's vendor by its wares");
        }
        finally
        {
            UoRegions.Load(path + ".none");
            File.Delete(path);
        }
    }

    static void WatchVendor()
    {
        Check.Equal("Stanley", AssistFeatures.WatchVendor("Stanley the tailor"), "titled");
        Check.Equal("Simon", AssistFeatures.WatchVendor("Simon"), "bare");
        Check.Equal("vendor", AssistFeatures.WatchVendor(""), "none");
        Check.Equal("Stanley", AssistFeatures.Untitled("Stanley the tailor"), "the activity's name, titled");
        Check.Equal("Simon", AssistFeatures.Untitled("Simon"), "the activity's name, bare");
        Check.Equal("Anna", AssistFeatures.Untitled("Anna The Tailor"), "the title in any case");
        Check.Equal("75EB84", AssistFeatures.Untitled("75EB84"), "a serial for a name");
    }

    public static ImmutableArray<TestCase> Cases => s_cases;

    static AtlasNpc Npc(string name, params string[] items) => new() { Name = name, Items = new List<string>(items) };

    static void ByTitle()
    {
        Check.True(AssistFeatures.TailorStock(Npc("Stanley the tailor")), "the tailor");
        Check.True(AssistFeatures.TailorStock(Npc("Anna The Tailor")), "the title in any case");
        Check.True(AssistFeatures.TailorStock(Npc("Stanley the tailor", "Folded cloth: 4 gp x 50")),
            "the tailor whatever the stock");
        Check.False(AssistFeatures.TailorStock(Npc("William the blacksmith")), "the blacksmith");
        Check.False(AssistFeatures.TailorStock(Npc("Bertram the weaver")), "the weaver");
        Check.False(AssistFeatures.TailorStock(Npc("Bertram the weaver", "Spool of thread: 3 gp x 20",
            "Fancy shirt: 33 gp x 10")), "the weaver whatever the stock");
        Check.True(AssistFeatures.TailorStock(Npc("Lynn")), "a bare name, its stock unread");
        Check.True(AssistFeatures.TailorStock(Npc("Lynn", "Spool of thread: 3 gp x 20")), "a bare name selling thread");
        Check.False(AssistFeatures.TailorStock(Npc("Lynn", "Folded cloth: 4 gp x 50")),
            "a bare name selling none of a tailor's goods");
    }

    const uint Smith = 0x00290001, Weaver = 0x00290002, Tailor = 0x00290003, Bare = 0x00290004;

    // Testtown's one tailor shop (x and y 998 to 1004), four keepers in it, every one listing the fancy shirt.
    static void Buyers()
    {
        string path = Path.Combine(Path.GetTempPath(), "assist-tests-tailortitle-" + Environment.ProcessId + ".txt");
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
                List<string> shirt = new()
                { "Fancy shirt: 33 gp x 10" };
                (uint serial, string name)[] keepers = { (Smith, "William the blacksmith"),
                    (Weaver, "Bertram the weaver"), (Tailor, "Stanley the tailor"), (Bare, "Lynn") };
                foreach ((uint serial, string name) in keepers)
                {
                    f.Atlas!.Seen(serial, 0x190, 1000, 1001, name, false);
                    f.Atlas.Stock(serial, new List<string>(shirt));
                }
                List<uint> buyers = f.CityBuyers("Testtown").ConvertAll(n => n.Serial);
                buyers.Sort();
                Check.Equal(Tailor.ToString("X") + "," + Bare.ToString("X"),
                    string.Join(",", buyers.ConvertAll(b => b.ToString("X"))), "the tailor and the bare name");
            });
        }
        finally
        {
            UoRegions.Load(path + ".none");
            File.Delete(path);
        }
    }

    const uint Judd = 0x00290011, Tate = 0x00290012, Unread = 0x00290013, Smith2 = 0x00290014, Gone = 0x00290015;

    // Vesper south at 17:12:08 in small: Judd the weaver's stock read, Tate's read, Nell the tailor unread, a titled
    // blacksmith, and Gone, a tailor last seen past PeopleShownMs. A bare name with no stock read is a Person to the
    // atlas, never in its shops.
    static void Keepers()
    {
        string path = Path.Combine(Path.GetTempPath(), "assist-tests-shoptailors-" + Environment.ProcessId + ".txt");
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
                f.Atlas!.Seen(Gone, 0x190, 1000, 1001, "Ollie the tailor", false);
                s.Clock.Sleep(AssistAtlas.PeopleShownMs + 1000);
                f.Atlas.Seen(Judd, 0x190, 1000, 1001, "Judd", false);
                f.Atlas.Stock(Judd,
                    new List<string> { "Dyes: 8 gp x 10", "Ball of yarn: 15 gp x 10", "Scissors: 13 gp x 10" });
                f.Atlas.Seen(Tate, 0x190, 1001, 1001, "Tate", false);
                f.Atlas.Stock(Tate, new List<string> { "Sewing kit: 3 gp x 10", "Fancy shirt: 52 gp x 10" });
                f.Atlas.Seen(Unread, 0x191, 1002, 1001, "Nell the tailor", false);
                f.Atlas.Seen(Smith2, 0x190, 1003, 1001, "William the blacksmith", false);
                UoRegions.Shop shop = UoRegions.AllShopsIn("Testtown").Find(x => x.Text == "The Stitch")!;
                List<uint> got = f.ShopTailors(shop).ConvertAll(n => n.Serial);
                got.Sort();
                Check.Equal(Tate.ToString("X") + "," + Unread.ToString("X"),
                    string.Join(",", got.ConvertAll(b => b.ToString("X"))),
                    "Tate and Nell, not Judd, William or the long-gone Ollie");
            });
        }
        finally
        {
            UoRegions.Load(path + ".none");
            File.Delete(path);
        }
    }
}
