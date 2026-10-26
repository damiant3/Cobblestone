#nullable enable
using System.Collections.Immutable;

namespace Uoaix.Assist.Tests;

// The running lights' text (AssistWindow.LightText): a stop asked of a running circuit reads "RUNNING: Cancel
// Requested" until the circuit ends.
public static class LightTests
{
    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("light: running names what runs", Running),
        new TestCase("light: a stop asked while running reads Cancel Requested, the name kept", CancelRequested),
        new TestCase("light: a stop asked with no name reads Cancel Requested alone", CancelNoName),
        new TestCase("light: stopped reads STOPPED whatever stop was asked", Stopped),
        new TestCase("light: the detail and the attention follow Cancel Requested", CancelWithDetail));

    public static ImmutableArray<TestCase> Cases => s_cases;

    static void Running()
    {
        Check.Equal("RUNNING Lumberjacking", AssistWindow.LightText(true, null!, "Lumberjacking", null!, false),
            "running");
        Check.Equal("RUNNING", AssistWindow.LightText(true, null!, "", null!, false), "no name");
    }

    static void CancelRequested()
    {
        Check.Equal("RUNNING: Cancel Requested (Tailoring: Sell only)",
            AssistWindow.LightText(true, null!, "Tailoring: Sell only", null!, true), "cancel");
    }

    static void CancelNoName()
    {
        Check.Equal("RUNNING: Cancel Requested", AssistWindow.LightText(true, null!, null!, null!, true), "null name");
        Check.Equal("RUNNING: Cancel Requested", AssistWindow.LightText(true, null!, "", null!, true), "empty name");
    }

    static void Stopped()
    {
        Check.Equal("STOPPED", AssistWindow.LightText(false, null!, "Lumberjacking", null!, true),
            "stop flag left set");
        Check.Equal("STOPPED", AssistWindow.LightText(false, null!, "Lumberjacking", null!, false), "plain");
    }

    static void CancelWithDetail()
    {
        Check.Equal("RUNNING: Cancel Requested (Mining)  step 3:  no way",
            AssistWindow.LightText(true, "no way", "Mining", "step 3", true), "all parts");
    }
}
