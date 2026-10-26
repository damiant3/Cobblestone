#nullable enable
using System.Collections.Immutable;
using System.Text;

namespace Uoaix.Assist.Tests;

// The one purse fill (AssistFeatures.PurseTo, AssistTesting.md T3) at its limit: the bank is asked only when the pack
// holds less than the target, one below, at and one above it.
public static class PurseTests
{
    const uint Pack = 0x42BE4F7C;
    const uint Gold = 0x45000001;
    const int Target = 300;

    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("purse: one gold under the target asks the bank, and a silent bank is the take's failure", Under),
        new TestCase("purse: the target held asks nothing", At),
        new TestCase("purse: one gold over the target asks nothing", Over));

    public static ImmutableArray<TestCase> Cases => s_cases;

    static void Kwik(FakeShard s, int gold)
    {
        s.ServerSends(Packets.LoginConfirm(1000, 1000, 0));
        s.ServerSends(Packets.Wear(Pack, 0x0E75, 0x15, Packets.Player));
        s.ServerSends(Packets.Open(Pack));
        s.ServerSends(Packets.Contained(Gold, AssistFeatures.GoldArt, gold, Pack));
        s.Clock.Sleep(1);
    }

    // The lines the player said, by the client's 0x03.
    static int Said(FakeShard s, string text)
    {
        int n = 0;
        foreach (byte[] p in s.ServerGot)
        {
            if (p[0] == 0x03 && p.Length > 8 && Encoding.ASCII.GetString(p, 8, p.Length - 8).TrimEnd('\0') == text)
                n++;
        }
        return n;
    }

    static void Under()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, Target - 1);
            string? why = f.PurseTo(Target, out string taken);
            Check.True(why is not null, "the silent bank: " + why);
            Check.Equal(1, Said(s, "bank"), "\"bank\" said once (L-FUTILITY)");
            Check.Equal("", taken, "nothing taken");
        });
    }

    static void At()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, Target);
            Check.Null(f.PurseTo(Target, out string taken), "PurseTo");
            Check.Equal(0, Said(s, "bank"), "nothing said");
            Check.Equal("", taken, "nothing asked");
        });
    }

    static void Over()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            Kwik(s, Target + 1);
            Check.Null(f.PurseTo(Target, out string taken), "PurseTo");
            Check.Equal(0, Said(s, "bank"), "nothing said");
            Check.Equal("", taken, "nothing asked");
        });
    }
}
