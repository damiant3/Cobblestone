#nullable enable
using System.Collections.Immutable;
using System.IO;
using System;
using System.Collections.Generic;

namespace Uoaix.Assist.Tests;

// A shop's display name (UoRegions.Display) over a region file drawn for it: Vesper's two Spinning Wheels, one above
// the other; Jhelom's two Adventurer's Needles, side by side; one shop of its title.
public static class ShopDisplayTests
{
    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("shop name: a title the city holds once is the title", Unique),
        new TestCase("shop name: two of a title one above the other read north and south", NorthSouth),
        new TestCase("shop name: two of a title side by side read west and east", WestEast),
        new TestCase("shop name: the same shop asked twice reads the same", Stable),
        new TestCase("shops: the buy circuit's default names a title, and both shops of that title are in it",
        DefaultBothShops),
        new TestCase("vendors: thread-off by serial takes one of two tailors of a name out of the round, by name both",
        ThreadOffBySerial));

    public static ImmutableArray<TestCase> Cases => s_cases;

    // A region line: a tag, x, y, w, h, z0, z1, a number, the name, five fields, the text.
    static string Line(int x, int y, int w, int h, string name,
        string text) => "R " + x + " " + y + " " + w + " " + h + " -128 127 0 " + name + " a b c d e " + text;

    static void Load()
    {
        string path = Path.Combine(Path.GetTempPath(), "assist-tests-regions-" + Environment.ProcessId + ".txt");
        File.WriteAllLines(path, new[]
        {
            Line(2800, 500, 300, 500, "CITY_VESPER", "Vesper"),
            Line(2900, 600, 10, 10, "WEAVER_VESPER_1", "Spinning Wheel"),
            Line(2900, 900, 10, 10, "WEAVER_VESPER_2", "Spinning Wheel"),
            Line(2950, 700, 10, 10, "TAILOR_VESPER_1", "Tailor Shop"),
            Line(1300, 3700, 400, 100, "CITY_JHELOM", "Jhelom"),
            Line(1350, 3740, 10, 10, "TAILOR_JHELOM_1", "Adventurer's Needle"),
            Line(1600, 3740, 10, 10, "TAILOR_JHELOM_2", "Adventurer's Needle"),
        });
        UoRegions.Load(path);
    }

    static UoRegions.Shop Find(string city, string text, int x)
    {
        foreach (UoRegions.Shop s in UoRegions.AllShopsIn(city))
        {
            if (s.Text == text && s.X == x)
                return s;
        }
        throw new InvalidOperationException("no shop " + text + " at x " + x + " in " + city);
    }

    static void Unique()
    {
        Load();
        Check.Equal("Tailor Shop", UoRegions.Display(Find("Vesper", "Tailor Shop", 2950)), "Display");
    }

    static void NorthSouth()
    {
        Load();
        Check.Equal("Spinning Wheel, Vesper north", UoRegions.Display(Find("Vesper", "Spinning Wheel", 2900)),
            "the one at y 600");
        UoRegions.Shop south = UoRegions.AllShopsIn("Vesper").Find(s => s.Text == "Spinning Wheel" && s.Y == 900)!;
        Check.Equal("Spinning Wheel, Vesper south", UoRegions.Display(south), "the one at y 900");
    }

    static void WestEast()
    {
        Load();
        Check.Equal("Adventurer's Needle, Jhelom west", UoRegions.Display(Find("Jhelom", "Adventurer's Needle", 1350)),
            "x 1350");
        Check.Equal("Adventurer's Needle, Jhelom east", UoRegions.Display(Find("Jhelom", "Adventurer's Needle", 1600)),
            "x 1600");
    }

    static void DefaultBothShops()
    {
        string path = Path.Combine(Path.GetTempPath(), "assist-tests-mage-regions-" + Environment.ProcessId + ".txt");
        File.WriteAllLines(path, new[]
        {
            Line(2800, 500, 300, 500, "CITY_VESPER", "Vesper"),
            Line(2900, 600, 10, 10, "MAGE_VESPER_1", "The Circles of Magic in Vesper"),
            Line(2900, 900, 10, 10, "MAGE_VESPER_2", "The Circles of Magic in Vesper"),
            Line(2950, 700, 10, 10, "MAGE_VESPER_3", "The Arcane Arts"),
        });
        UoRegions.Load(path);
        RecallTests.WithFeatures((s, f) =>
        {
            List<string> keys = f.CircuitShopKeys("buy");
            Check.Equal(2, keys.Count, "both of the title, none other: " + string.Join("; ", keys));
            Check.True(keys[0] != keys[1], "two keys");
        });
    }

    static void ThreadOffBySerial()
    {
        Load();
        RecallTests.WithFeatures((s, f) =>
        {
            const uint TateA = 0x00011111, TateB = 0x00022222;
            // Inside the north Spinning Wheel (WEAVER_VESPER_1, a tailor shop to the circuit: TailorShopKinds).
            f.Atlas.Seen(TateA, 0x190, 2902, 602, "Tate", false);
            f.Atlas.ShopPack(TateA);
            f.Atlas.Seen(TateB, 0x190, 2904, 604, "Tate", false);
            f.Atlas.ShopPack(TateB);
            try
            {
                string one = f.ThreadOffSet(TateB.ToString("X8"), true);
                Check.True(one.StartsWith("OK off: "), "by serial: " + one);
                Check.True(f.IsThreadOff(TateB) && !f.IsThreadOff(TateA), "only that one");
                string both = f.ThreadOffSet("tate", true);
                Check.True(f.IsThreadOff(TateA) && f.IsThreadOff(TateB), "by name both: " + both);
                f.ThreadOffSet(TateA.ToString("X8"), false);
                Check.True(!f.IsThreadOff(TateA) && f.IsThreadOff(TateB), "back by serial");
                Check.True(f.ThreadOffSet("0000FFFF", true).StartsWith("ERROR no tailor"), "an unknown serial");
            }
            finally
            {
                f.ThreadOffSet("tate", false);
            }
        });
    }

    static void Stable()
    {
        Load();
        UoRegions.Shop s = Find("Vesper", "Spinning Wheel", 2900);
        Check.Equal(UoRegions.Display(s), UoRegions.Display(s), "twice");
    }
}
