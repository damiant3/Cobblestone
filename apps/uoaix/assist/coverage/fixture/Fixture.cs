// The coverage instrument's control (test-cover.ps1): every probe kind, with arms FixtureDriver leaves untaken on
// purpose. expected.txt lists exactly those; a line moved here moves it there.
#nullable enable
using System.Collections.Generic;

namespace Uoaix.Assist.CoverFixture;

public static class Fixture
{
    public static int IfThenOnly(int x)
    {
        if (x > 0)
            return 1;
        return 0;
    }

    public static bool AndBothTrueOnly(bool a, bool b)
    {
        return a && b;
    }

    public static int EachEmptyOnly(List<int> items)
    {
        int n = 0;
        foreach (int i in items)
            n += i;
        return n;
    }

    public static string TernaryBoth(bool c) => c ? "yes" : "no";

    public static string CoalesceNoFallback(string? s) => s ?? "none";

    public static int SwitchOneCase(int k)
    {
        switch (k)
        {
            case 1:
                return 10;
            case 2:
                return 20;
        }
        return 0;
    }

    public static void VoidBody(List<int> sink) => sink.Add(1);

    public static bool OutCondition(int x)
    {
        if (TryHalf(x, out int half) && half > 0)
            return true;
        return false;
    }

    static bool TryHalf(int x, out int half)
    {
        half = x / 2;
        return x % 2 == 0;
    }

    public static int NeverCalled()
    {
        return 7;
    }
}
