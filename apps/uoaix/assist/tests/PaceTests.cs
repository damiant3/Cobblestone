#nullable enable
using System.Collections.Immutable;
using System.Reflection;

namespace Uoaix.Assist.Tests;

// A loop's delay set by the player (AssistFeatures.SetDelay) and the bisect's answer to a refusal (Refused), each at its
// floor.
public static class PaceTests
{
    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("pace: the smelting loop's delay goes to 0; another loop's stops at MinDelayMs / 2, and under 0 "
        + "is 0", Floors),
        new TestCase("pace: a refusal at 0 ms with auto-detect tries MinDelayMs / 2, not 0 again", RefusedAtZero));

    public static ImmutableArray<TestCase> Cases => s_cases;

    static void Kept(AssistFeatures f, LoopKind l, System.Action body)
    {
        string key = f.Server + "pace." + l.Name;
        string was = f.Cfg.Get(key, ""), auto = f.Cfg.Get(f.Server + "pace.auto", "");
        try
        {
            body();
        }
        finally
        {
            f.Cfg.Set(key, was);
            f.Cfg.Set(f.Server + "pace.lo." + l.Name, "");
            f.Cfg.Set(f.Server + "pace.hi." + l.Name, "");
            f.Cfg.Set(f.Server + "pace.auto", auto);
        }
    }

    static void Floors()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            LoopKind smelt = AssistFeatures.Find("Smelting (one ore at a time)")!;
            LoopKind hiding = AssistFeatures.Find("Hiding")!;
            Kept(f, smelt, () =>
            {
                f.SetDelay(smelt, 0);
                Check.Equal(0, f.Delay(smelt), "smelting at 0");
                f.SetDelay(smelt, -5);
                Check.Equal(0, f.Delay(smelt), "smelting under 0 is 0");
            });
            Kept(f, hiding, () =>
            {
                f.SetDelay(hiding, 0);
                Check.Equal(AssistFeatures.MinDelayMs / 2, f.Delay(hiding), "hiding at its floor");
                f.SetDelay(hiding, AssistFeatures.MinDelayMs / 2 + 1);
                Check.Equal(AssistFeatures.MinDelayMs / 2 + 1, f.Delay(hiding), "hiding one past its floor");
            });
        });
    }

    static void RefusedAtZero()
    {
        RecallTests.WithFeatures((s, f) =>
        {
            LoopKind smelt = AssistFeatures.Find("Smelting (one ore at a time)")!;
            Kept(f, smelt, () =>
            {
                f.AutoPace = true;
                f.SetDelay(smelt, 0);
                MethodInfo refused = typeof(AssistFeatures).GetMethod("Refused",
                    BindingFlags.NonPublic | BindingFlags.Instance)!;
                Check.Null((string?)refused.Invoke(f, new object?[] { smelt }), "Refused goes on");
                Check.Equal(AssistFeatures.MinDelayMs / 2, f.Delay(smelt), "0 refused tries MinDelayMs / 2");
            });
        });
    }
}
