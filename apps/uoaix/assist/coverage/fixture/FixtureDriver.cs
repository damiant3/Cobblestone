// Drives Fixture (test-cover.ps1); not instrumented.
#nullable enable
using System.Collections.Generic;

namespace Uoaix.Assist.CoverFixture;

public static class FixtureDriver
{
    public static int Run()
    {
        Fixture.IfThenOnly(5);
        Fixture.AndBothTrueOnly(true, true);
        Fixture.EachEmptyOnly(new List<int>());
        Fixture.TernaryBoth(true);
        Fixture.TernaryBoth(false);
        Fixture.CoalesceNoFallback("a");
        Fixture.SwitchOneCase(1);
        Fixture.VoidBody(new List<int>());
        Fixture.OutCondition(4);
        return 0;
    }
}
