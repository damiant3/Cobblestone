#nullable enable
using System.Collections.Immutable;
using System;
using System.IO;

namespace Uoaix.Assist.Tests;

// The pack's runes as every picker sees them (AssistFeatures.PackRunes, AssistTesting.md T3, one implementation each),
// through PackRuneFor: each label read once and kept, Wind's runes never taken, and a rune read as unmarked read again
// once a session.
public static class PackRuneTests
{
    const uint Pack = 0x42BE4F7C;
    const uint Minoc = 0x44AE0001;
    const uint Vesper = 0x44AE0002;
    const uint Wind = 0x44AE0003;
    const uint Blank = 0x44AE0004;
    const int RuneArt = 0x1F14;

    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("pack runes: each rune's label is asked once, and a second pick asks none", LabelsOnce),
        new TestCase("pack runes: the label equal to the place beats the label holding it", EqualBeatsHolding),
        new TestCase("pack runes: a rune labelled for Wind is never taken (NeverGo)", WindNever),
        new TestCase("pack runes: a rune read as unmarked is read again once a session, then kept", BlankReadAgainOnce),
        new TestCase("pack runes: for a shop, the carried rune landing nearest it beats the city's rune landing far off",
        NearestToShop),
        new TestCase("pack runes: a reserved rune is never picked, the next one for the place is", ReservedNever));

    public static ImmutableArray<TestCase> Cases => s_cases;

    static void Kwik(FakeShard s, params uint[] runes)
    {
        s.ServerSends(Packets.LoginConfirm(1000, 1000, 0));
        s.ServerSends(Packets.Wear(Pack, 0x0E75, 0x15, Packets.Player));
        foreach (uint r in runes)
            s.ServerSends(Packets.Contained(r, RuneArt, 1, Pack));
        s.Clock.Sleep(1);
    }

    // Each rune's single-click label, by serial; the clicks counted per serial.
    static void Labels(FakeShard s, int[] looks, params (uint Serial, string Label)[] labels)
    {
        s.Answer = p =>
        {
            if (p[0] != 0x09)
                return ImmutableArray<byte[]>.Empty;
            uint serial = (uint)(p[1] << 24 | p[2] << 16 | p[3] << 8 | p[4]);
            for (int i = 0; i < labels.Length; i++)
            {
                if (labels[i].Serial == serial)
                {
                    looks[i]++;
                    return ImmutableArray.Create<byte[]>(Packets.Speech(serial, "", labels[i].Label, 6));
                }
            }
            return ImmutableArray<byte[]>.Empty;
        };
    }

    // The runes' kept labels are the features' own file, shared by every case of a run: each case uses serials of its
    // own, and every rune is new to it.
    static void LabelsOnce()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            const uint A = 0x44AE0101;
            const uint B = 0x44AE0102;
            Kwik(s, A, B);
            int[] looks = new int[2];
            Labels(s, looks, (A, "minoc bank"), (B, "vesper bank"));
            Check.Equal(A, f.PackRuneFor(s.Link, "minoc bank", (x, y) => true), "the first pick");
            Check.Equal(B, f.PackRuneFor(s.Link, "vesper bank", (x, y) => true), "the second pick");
            Check.Equal(1, looks[0], "minoc's label asks");
            Check.Equal(1, looks[1], "vesper's label asks");
        });
    }

    static void EqualBeatsHolding()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, Minoc, Vesper);
            int[] looks = new int[2];
            Labels(s, looks, (Minoc, "minoc bank west"), (Vesper, "a recall rune for minoc bank"));
            Check.Equal(Vesper, f.PackRuneFor(s.Link, "minoc bank", (x, y) => true), "the rune for minoc bank");
        });
    }

    static void WindNever()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, Wind);
            int[] looks = new int[1];
            Labels(s, looks, (Wind, "Wind"));
            Check.Equal(0u, f.PackRuneFor(s.Link, "wind", (x, y) => true), "never Wind");
            Check.Equal(1, looks[0], "its label read");
        });
    }

    // Testtown's mage shop at 1300-1303; the city's rune lands at 1000,1000, the shop's (its label in another word
    // order) at 1320,1300, 18 tiles off and past the 8 of the shop check (2026-10-24 14:33:29, Moonglow's bank rune
    // chosen, 302 tiles from Encyclopedia Magicka).
    static void NearestToShop()
    {
        string path = Path.Combine(Path.GetTempPath(), "assist-tests-runeshop-" + Environment.ProcessId + ".txt");
        File.WriteAllLines(path, new[]
        {
            "R 900 900 600 600 -128 127 0 CITY_TESTTOWN a b c d e Testtown",
            "R 1300 1300 4 4 -128 127 0 MAGE_TESTTOWN a b c d e Encyclopedia Magicka",
        });
        try
        {
            UoRegions.Load(path);
            RecallTests.WithFeatures((s, f) =>
            {
                const uint City = 0x44AE0201;
                const uint Shop = 0x44AE0202;
                File.WriteAllLines(f.ServerFile("rune-landings"),
                    new[] { City.ToString("X") + "\t1000\t1000", Shop.ToString("X") + "\t1320\t1300" });
                Kwik(s, City, Shop);
                int[] looks = new int[2];
                Labels(s, looks, (City, "Testtown"), (Shop, "Testtown Encyclopedia Magicka"));
                UoRegions.Shop? shop = UoRegions.AllShopsIn("Testtown").Find(x => x.Text == "Encyclopedia Magicka");
                Check.True(shop is not null, "the shop");
                Check.Equal(Shop, f.CarriedShopRune(s.Link, "Testtown", shop!), "the rune landing by the shop");
                Check.Equal(City, f.CarriedShopRune(s.Link, "Testtown"), "no shop asked: the city's rune");
            });
        }
        finally
        {
            UoRegions.Load(path + ".none");
            File.Delete(path);
        }
    }

    static void ReservedNever()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            const uint Roof = 0x44AE0301;
            const uint Steps = 0x44AE0302;
            Kwik(s, Roof, Steps);
            int[] looks = new int[2];
            Labels(s, looks, (Roof, "minoc bank"), (Steps, "a recall rune for minoc bank"));
            try
            {
                Check.Equal(Roof, f.PackRuneFor(s.Link, "minoc bank", (x, y) => true), "unreserved: the equal label");
                f.Cfg.Set(f.Server + "runes.reserved", "1234, " + Roof.ToString("X"));
                Check.True(f.Reserved(Roof), "reserved");
                Check.Equal(Steps, f.PackRuneFor(s.Link, "minoc bank", (x, y) => true), "reserved: the other");
            }
            finally
            {
                f.Cfg.Set(f.Server + "runes.reserved", "");
            }
        });
    }

    static void BlankReadAgainOnce()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, Blank);
            int[] looks = new int[1];
            Labels(s, looks, (Blank, "an unmarked recall rune"));
            Check.Equal(0u, f.PackRuneFor(s.Link, "britain", (x, y) => true), "the first pick");
            Check.Equal(1, looks[0], "read");
            Check.Equal(0u, f.PackRuneFor(s.Link, "britain", (x, y) => true), "the second pick");
            Check.Equal(2, looks[0], "read again once");
            Check.Equal(0u, f.PackRuneFor(s.Link, "britain", (x, y) => true), "the third pick");
            Check.Equal(2, looks[0], "kept from then on");
        });
    }
}
