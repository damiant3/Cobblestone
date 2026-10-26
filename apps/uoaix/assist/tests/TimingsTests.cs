#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;

namespace Uoaix.Assist.Tests;

// Auto-tune on a refusal (AssistTimings.Refused, Accepted; Damian, 2026-10-22): refusals at 1 in 50 or rarer are the
// substrate's; more often, the timing rises by half and steps down 50 ms after twice the gap taken in a row; a refusal
// on the way down reverses the steps, 50 ms up each, and the stable state after that settles the timing.
public static class TimingsTests
{
    const string Key = "busy:weave";

    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("tune: the first refusal of a session measures no gap and changes nothing", FirstIsSubstrate),
        new TestCase("tune: a refusal 1 in 50 is the substrate's and changes nothing", OneInFiftyKept),
        new TestCase("tune: a refusal 1 in 49 raises the timing by half, 700 to 1050", OneInFortyNineRaises),
        new TestCase("tune: half of 650 rounds down to a 50 ms step, 950, inside the cap", RoundsDown),
        new TestCase("tune: at 1 in 10 the stable state is 20 taken in a row, then one step down", TwentyForOneInTen),
        new TestCase("tune: the steps stop 50 ms above the value refused, and settle there", StopsAboveRefused),
        new TestCase("tune: a refusal on the way down reverses to 50 ms steps up until stable, then settles",
        ReversesUp),
        new TestCase("tune: a rare refusal on the way down does not stop the steps", RareOnTheWayDown),
        new TestCase("tune: on a settled timing a frequent refusal raises by half again", SettledRaisesAgain),
        new TestCase("tune: a raise past half the setting is held at the cap and reported once", HeldAtCap),
        new TestCase("tune: with no refusal, actions taken never lower the timing", NeverRefused),
        new TestCase("tune: a value the player sets ends the steps", PlayerSetEnds),
        new TestCase("tune: with auto-tune off a frequent refusal and actions taken change nothing", AutoTuneOff),
        new TestCase("tune: the value refused and the stable count outlive a relay restart", Persisted),
        new TestCase("tune: the steps of one timing leave another alone", KeysApart),
        new TestCase("tune: a log line is stamped with the clock the timings run on, never the wall's", LogStamped),
        new TestCase("control: timing lists, answers and sets as the panel does, logged with the base reset; an "
        + "unknown key or a bad time refused, nothing set", Command));

    // The 17:46:53 case: "answer" held at 4500 from a setting of 3000 by a sample of the player's own play, set back.
    static void Command()
    {
        Rig r = new();
        r.Timings.Set("answer", 3000);
        r.Timings.Set("answer", 4500, "auto-tune: a test");
        Check.True(TimingCommand.Run(r.Timings, new[] { "timing" }).Contains(" answer=4500"), "listed");
        Check.Equal("OK answer=4500", TimingCommand.Run(r.Timings, new[] { "timing", "answer" }), "answered");
        Check.Equal("OK answer=3000", TimingCommand.Run(r.Timings, new[] { "timing", "answer", "3000" }), "set");
        Check.Equal(3000, r.Timings.Ms("answer"), "held");
        Check.True(string.Join(" | ", r.Timings.Lines()).Contains("4500 ms to 3000 ms (set by the control port)"),
            "logged: " + string.Join(" | ", r.Timings.Lines()));
        Check.Equal(3000, r.Open().Ms("answer"), "kept after a restart");
        Check.Equal("OK " + Key + "=900", TimingCommand.Run(r.Timings, new[] { "timing", Key, "900" }),
            "a busy key set");
        Check.Equal("ERROR no timing nosuch", TimingCommand.Run(r.Timings, new[] { "timing", "nosuch", "10" }),
            "an unknown key");
        Check.Equal("ERROR timing <key> <ms>: not a time in ms: -5",
            TimingCommand.Run(r.Timings, new[] { "timing", "answer", "-5" }), "a negative time");
        Check.Equal("ERROR timing <key> <ms>: not a time in ms: soon",
            TimingCommand.Run(r.Timings, new[] { "timing", "answer", "soon" }), "not a number");
        Check.Equal(3000, r.Timings.Ms("answer"), "nothing set by a refusal");
    }

    public static ImmutableArray<TestCase> Cases => s_cases;

    static readonly DateTime s_start = new(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);

    static void LogStamped()
    {
        Rig r = new();
        r.Timings.Log(Key, 700, 1050, "a test");
        string want = s_start.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + "  " + Key
            + ": 700 ms to 1050 ms (a test)";
        Check.True(r.Timings.Lines().Contains(want), "the line: " + string.Join(" | ", r.Timings.Lines()));
    }

    sealed class Rig
    {
        public readonly string Settings;
        public readonly List<string> Reports = new();
        public readonly VirtualClock Clock = new(s_start);
        public AssistTimings Timings;

        public Rig()
        {
            Settings = Path.Combine(Path.GetTempPath(),
                "assist-tests-timings-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("N") + ".txt");
            Timings = Open();
        }

        public AssistTimings Open()
        {
            string dir = Path.GetDirectoryName(Settings)!;
            return new AssistTimings(new AssistSettings(Settings), () => "",
                name => Path.Combine(dir, Path.GetFileNameWithoutExtension(Settings) + "-" + name + ".txt"), _ => { },
                Reports.Add, Clock);
        }

        public void Taken(int n)
        {
            for (int i = 0; i < n; i++)
                Timings.Accepted(Key);
        }

        public int Refused() => Timings.Refused(Key, "busy");

        // A refusal 1 in (gap + 1): taken gap times after the last refusal.
        public int RefusedAfter(int gap)
        {
            Taken(gap);
            return Refused();
        }

        public int Ms => Timings.Ms(Key);
    }

    static Rig At(int ms)
    {
        Rig r = new();
        r.Timings.Set(Key, ms);
        return r;
    }

    // At ms, raised by a refusal 1 in 10 after the session's first refusal.
    static Rig Raised(int ms)
    {
        Rig r = At(ms);
        r.Refused();
        r.RefusedAfter(9);
        return r;
    }

    static void FirstIsSubstrate()
    {
        Rig r = At(700);
        Check.Equal(700, r.Refused(), "Refused");
    }

    static void OneInFiftyKept()
    {
        Rig r = At(700);
        r.Refused();
        Check.Equal(700, r.RefusedAfter(49), "Refused");
        r.Taken(200);
        Check.Equal(700, r.Ms, "after 200 taken");
    }

    static void OneInFortyNineRaises()
    {
        Rig r = At(700);
        r.Refused();
        Check.Equal(1050, r.RefusedAfter(48), "Refused");
        r.Taken(97);
        Check.Equal(1050, r.Ms, "after 97 taken (2 x 49 is the stable state)");
        r.Taken(1);
        Check.Equal(1000, r.Ms, "after 98");
    }

    static void RoundsDown()
    {
        Rig r = At(650);
        r.Refused();
        Check.Equal(950, r.RefusedAfter(9), "Refused");
        Check.Equal(0, r.Reports.Count, "reports");
    }

    static void TwentyForOneInTen()
    {
        Rig r = Raised(700);
        Check.Equal(1050, r.Ms, "raised");
        r.Taken(19);
        Check.Equal(1050, r.Ms, "after 19");
        r.Taken(1);
        Check.Equal(1000, r.Ms, "after 20");
    }

    static void StopsAboveRefused()
    {
        Rig r = Raised(700);
        r.Taken(20 * 6);
        Check.Equal(750, r.Ms, "after 6 steps");
        r.Taken(20 * 5);
        Check.Equal(750, r.Ms, "settled");
    }

    static void ReversesUp()
    {
        Rig r = Raised(1000);
        Check.Equal(1500, r.Ms, "raised");
        r.Taken(40);
        Check.Equal(1400, r.Ms, "two steps down");
        Check.Equal(1450, r.Refused(), "refused 1 in 41 on the way down: one step up");
        Check.Equal(1500, r.RefusedAfter(5), "refused again 1 in 6: one more step up");
        r.Taken(11);
        Check.Equal(1500, r.Ms, "after 11 (2 x 6 is the stable state)");
        r.Taken(1);
        Check.Equal(1500, r.Ms, "stable after the steps up: settled, no step down");
        r.Taken(200);
        Check.Equal(1500, r.Ms, "settled after 200 more");
    }

    static void RareOnTheWayDown()
    {
        Rig r = Raised(700);
        r.Taken(69);
        Check.Equal(900, r.Ms, "three steps down, 9 into the fourth");
        Check.Equal(900, r.Refused(), "refused 1 in 70: kept");
        r.Taken(11);
        Check.Equal(850, r.Ms, "the fourth step, its count kept through the refusal");
    }

    static void SettledRaisesAgain()
    {
        Rig r = Raised(600);
        r.Taken(20 * 7);
        Check.Equal(650, r.Ms, "settled 50 above 600");
        r.Refused();
        Check.Equal(900, r.RefusedAfter(9), "refused 1 in 10 at 650, settled: by half (950), held at the cap of 900");
    }

    static void HeldAtCap()
    {
        Rig r = Raised(700);
        Check.Equal(0, r.Reports.Count, "reports at 1050");
        Check.Equal(1050, r.RefusedAfter(1), "one step up from 1050, held at the cap");
        Check.Equal(1, r.Reports.Count, "reports");
        r.RefusedAfter(1);
        Check.Equal(1, r.Reports.Count, "reports after one more");
    }

    static void NeverRefused()
    {
        Rig r = At(700);
        r.Taken(500);
        Check.Equal(700, r.Ms, "Ms");
    }

    static void PlayerSetEnds()
    {
        Rig r = Raised(700);
        r.Timings.Set(Key, 800);
        r.Taken(500);
        Check.Equal(800, r.Ms, "Ms");
    }

    static void AutoTuneOff()
    {
        Rig r = At(700);
        r.Timings.AutoTune = false;
        r.Refused();
        Check.Equal(700, r.RefusedAfter(9), "Refused");
        r.Timings.AutoTune = true;
        Check.Equal(1050, r.RefusedAfter(9), "the gap was counted while off");
        r.Timings.AutoTune = false;
        r.Taken(100);
        Check.Equal(1050, r.Ms, "no step while off");
    }

    static void Persisted()
    {
        Rig r = Raised(700);
        r.Timings = r.Open();
        r.Taken(20);
        Check.Equal(1000, r.Ms, "Ms");
    }

    static void KeysApart()
    {
        Rig r = Raised(700);
        r.Timings.Set("busy:sew", 700);
        r.Timings.Refused("busy:sew", "busy");
        r.Taken(20);
        Check.Equal(1000, r.Ms, Key);
        Check.Equal(700, r.Timings.Ms("busy:sew"), "busy:sew");
    }
}
