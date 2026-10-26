#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Uoaix.Assist.Tests;

public static class KeepRulesTests
{
    static readonly DateTime s_before = new(2026, 10, 22, 6, 0, 0, DateTimeKind.Local);
    static readonly DateTime s_now = new(2026, 10, 22, 6, 41, 2, DateTimeKind.Local);

    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("purse: no vendor needs no gold", PurseNone),
        new TestCase("purse: one vendor's 19 thread at 3 need 57", PurseOne),
        new TestCase("purse: two vendors' stocks add up", PurseTwo),
        new TestCase("purse: a price of 0 counts as 1, of 1 as 1", PursePriceFloor),
        new TestCase("purse: a stock of -1 counts as none, of 0 as none", PurseStockFloor),
        new TestCase("purse: a sum past the largest int stops there", PurseClamp),
        new TestCase("buy-out: a stock seen and bought out now is out now", BoughtOutNow),
        new TestCase("buy-out: a dry visit to a vendor already out moves nothing", DryVisitKeeps),
        new TestCase("buy-out: a dry first visit (stock unknown) moves nothing", DryUnknownKeeps),
        new TestCase("buy-out: a stock seen at the last visit and gone now is out now", OthersBoughtOut),
        new TestCase("buy-out: 1 thread left is not out", OneLeft),
        new TestCase("bank: outside Keep thread the pack keeps the purse setting", KeepsSetting),
        new TestCase("bank: in Keep thread the pack keeps the visit's need when larger", KeepsNeed),
        new TestCase("bank: in Keep thread a need under the setting keeps the setting", KeepsSettingOverNeed),
        new TestCase("bank: a need equal to the setting keeps it", KeepsEqual),
        new TestCase("visit: a whole buy-out reads as stock shown and 0 left", ParseBought),
        new TestCase("visit: a part buy reads its stock left", ParseBoughtPart),
        new TestCase("visit: a dry answer and a dry hand check read as none", ParseDry),
        new TestCase("visit: a hand check that saw stock reads it", ParseSaw),
        new TestCase("visit: a hand buy is the vendor bought out; a hand buy of 0 says nothing", ParseHandBought),
        new TestCase("visit: a refusal or a short purse says nothing", ParseNothing),
        new TestCase("visit: a malformed bought line says nothing", ParseMalformed),
        new TestCase("probe: no visit, stock left, or a dry first visit's own stock unknown: due now only while not "
            + "known out", ProbeNotOut),
        new TestCase("probe: a buy-out with nothing known is probed at half a period", ProbeHalf),
        new TestCase("probe: a dry visit at the half probes the half after it; a restock found there, the half before "
        + "it a period on", ProbeHalves),
        new TestCase("probe: a stock 9 minutes after a buy-out is the same buy-out, at 10 a restock", ProbeSplitBuy),
        new TestCase("probe: a restock seen is visited a period on, at its end and the margin", ProbeSeen),
        new TestCase("probe: evidence older than PhaseHours is not used", ProbeOld),
        new TestCase("probe: a dry visit a period or more after the buy-out bounds nothing and starts again",
        ProbeLongDry),
        new TestCase("probe: evidence that leaves no phase keeps only itself", ProbeContradiction),
        new TestCase("strike after: nothing known is half a period on; a seen restock, a period after it; counted from "
        + "the refusal, a later thread visit or buy-out never moving it; stock left does not make it now",
        StrikeAfterCases),
        new TestCase("sure after: a whole period on with nothing known; with a restock seen, a period after it; never "
        + "before the strike guess", SureAfterCases),
        new TestCase("probe: dry just past a period where the restock had to fall: the phase moved, the window starts "
        + "again", ProbeMovedLater),
        new TestCase("probe: a vendor of hidden phase visited at every probe is found within the narrow window in six "
        + "probes and visited past its strike", ProbeConverges),
        new TestCase("period: none or one restock seen is the default; a gap of two periods gives one", PeriodFromGaps),
        new TestCase("period: a gap under half a period or giving one past PeriodTolerance is not counted; the median "
        + "of the rest", PeriodSkips),
        new TestCase("ends: a stop, a takeover, the fail-safe, a closed or halted link and death end the circuit",
        EndsTerminal),
        new TestCase("ends: a failed recall, a walk refused, a vendor's silence and no reason do not", EndsNot));

    public static ImmutableArray<TestCase> Cases => s_cases;

    static void PurseNone() => Check.Equal(0, KeepRules.PurseFor(ImmutableArray<VendorStock>.Empty), "PurseFor");

    static void PurseOne() => Check.Equal(57, KeepRules.PurseFor(ImmutableArray.Create(new VendorStock(19, 3))),
        "PurseFor");

    static void PurseTwo() => Check.Equal(57 + 109 * 3,
        KeepRules.PurseFor(ImmutableArray.Create(new VendorStock(19, 3), new VendorStock(109, 3))), "PurseFor");

    static void PursePriceFloor()
    {
        Check.Equal(10, KeepRules.PurseFor(ImmutableArray.Create(new VendorStock(10, 0))), "price 0");
        Check.Equal(10, KeepRules.PurseFor(ImmutableArray.Create(new VendorStock(10, 1))), "price 1");
        Check.Equal(10, KeepRules.PurseFor(ImmutableArray.Create(new VendorStock(10, -4))), "price -4");
    }

    static void PurseStockFloor()
    {
        Check.Equal(0, KeepRules.PurseFor(ImmutableArray.Create(new VendorStock(-1, 3))), "stock -1");
        Check.Equal(0, KeepRules.PurseFor(ImmutableArray.Create(new VendorStock(0, 3))), "stock 0");
    }

    static void PurseClamp()
    {
        Check.Equal(int.MaxValue, KeepRules.PurseFor(ImmutableArray.Create(new VendorStock(int.MaxValue, 2))),
            "PurseFor");
    }

    static void BoughtOutNow() => Check.Equal(s_now, KeepRules.BoughtOut(s_before, 0, 19, 0, s_now), "BoughtOut");

    static void DryVisitKeeps() => Check.Equal(s_before, KeepRules.BoughtOut(s_before, 0, 0, 0, s_now), "BoughtOut");

    static void DryUnknownKeeps() => Check.Equal(DateTime.MinValue,
        KeepRules.BoughtOut(DateTime.MinValue, -1, 0, 0, s_now), "BoughtOut");

    static void OthersBoughtOut() => Check.Equal(s_now, KeepRules.BoughtOut(s_before, 5, 0, 0, s_now), "BoughtOut");

    static void OneLeft() => Check.Equal(s_before, KeepRules.BoughtOut(s_before, 0, 19, 1, s_now), "BoughtOut");

    static void KeepsSetting() => Check.Equal(600, KeepRules.PackKeeps(600, 2000, false), "PackKeeps");

    static void KeepsNeed() => Check.Equal(2000, KeepRules.PackKeeps(600, 2000, true), "PackKeeps");

    static void KeepsSettingOverNeed() => Check.Equal(600, KeepRules.PackKeeps(600, 57, true), "PackKeeps");

    static void KeepsEqual() => Check.Equal(600, KeepRules.PackKeeps(600, 600, true), "PackKeeps");

    static void ParseBought() => Check.Equal(new Visit(s_now, 64, 0),
        KeepRules.ParseVisit(s_now, "bought 64 for 192, 0 left"), "ParseVisit");

    static void ParseBoughtPart() => Check.Equal(new Visit(s_now, 73, 62),
        KeepRules.ParseVisit(s_now, "bought 11 for 35, 62 left"), "ParseVisit");

    static void ParseDry()
    {
        Check.Equal(new Visit(s_now, 0, 0), KeepRules.ParseVisit(s_now, "none: the vendor has none"), "the answer");
        Check.Equal(new Visit(s_now, 0, 0), KeepRules.ParseVisit(s_now, "by hand: none"), "the hand check");
    }

    static void ParseSaw() => Check.Equal(new Visit(s_now, 37, 37), KeepRules.ParseVisit(s_now, "by hand: saw 37"),
        "ParseVisit");

    static void ParseHandBought()
    {
        Check.Equal(new Visit(s_now, 64, 0), KeepRules.ParseVisit(s_now, "by hand: bought 64"), "64");
        Check.Null(KeepRules.ParseVisit(s_now, "by hand: bought 0"), "0");
    }

    static void ParseNothing()
    {
        Check.Null(KeepRules.ParseVisit(s_now, "failed: Morena gave no answer"), "a refusal");
        Check.Null(KeepRules.ParseVisit(s_now, "none: the pack's 22 gold buys none of it"), "a short purse");
        Check.Null(KeepRules.ParseVisit(s_now, "by hand: saw many"), "a hand check without a count");
        Check.Null(KeepRules.ParseVisit(s_now, "by hand: saw -1"), "a hand check of a negative count");
        Check.Null(KeepRules.ParseVisit(s_now, "by hand: bought many"), "a hand buy without a count");
        Check.Null(KeepRules.ParseVisit(s_now, "bought 64 for 192, x left"), "no count left");
        Check.Null(KeepRules.ParseVisit(s_now, "bought 64 for 192, -1 left"), "a negative count left");
    }

    static void ParseMalformed()
    {
        Check.Null(KeepRules.ParseVisit(s_now, "bought 64 for 192"), "too short");
        Check.Null(KeepRules.ParseVisit(s_now, "bought x for 192, 0 left"), "no count");
        Check.Null(KeepRules.ParseVisit(s_now, "bought 64 for 192, 0 kept"), "no left");
        Check.Null(KeepRules.ParseVisit(s_now, "bought -1 for 192, 0 left"), "a negative count");
    }

    const double P = KeepRules.RestockPeriodSeconds;
    static readonly ImmutableArray<DateTime> s_none = ImmutableArray<DateTime>.Empty;

    static Visit At(double minute, int shown, int left) => new(s_before.AddMinutes(minute), shown, left);

    static double MinutesAfter(DateTime t, double minute) => (t - s_before).TotalMinutes - minute;

    static DateTime Probe(DateTime now, params Visit[] visits) => KeepRules.ProbeAt(ImmutableArray.Create(visits),
        s_none, P, now);

    static void Between(double lo, double hi, double got, string what) => Check.True(got >= lo && got <= hi,
        what + ": " + got.ToString("0.00") + " not in " + lo + ".." + hi);

    static void ProbeNotOut()
    {
        Check.Equal(DateTime.MinValue, Probe(s_now), "no visit");
        Check.Equal(DateTime.MinValue, Probe(s_now, At(0, 19, 5)), "stock left");
        Check.Equal(DateTime.MinValue, Probe(s_now, At(0, 19, 0), At(70, 28, 9)), "restocked and some left");
        Check.True(Probe(s_now, At(0, 0, 0)) > s_before, "a dry first visit: out from it");
    }

    // Half of 3288 s is 27.4 minutes; the median beat's end lies within one 16 s beat of it.
    static void ProbeHalf()
    {
        Between(27.0, 28.0, MinutesAfter(Probe(s_now, At(0, 19, 0)), 0), "half a period on");
        DateTime early = new(2025, 6, 1, 12, 0, 0, DateTimeKind.Local);
        DateTime p = KeepRules.ProbeAt(ImmutableArray.Create(new Visit(early, 19, 0)), s_none, P, early);
        Between(27.0, 28.0, (p - early).TotalMinutes, "a buy-out before the phase epoch, the same");
    }

    static void ProbeHalves()
    {
        double half = MinutesAfter(Probe(s_now, At(0, 19, 0)), 0);
        // Dry there: the restock is in the second half, probed at its middle.
        Between(40.5, 41.7, MinutesAfter(Probe(s_now, At(0, 19, 0), At(half, 0, 0)), 0), "the half after");
        // Restocked there and bought out: the restock is in the first half; a period on, its middle.
        Between(P / 60 + 13.2, P / 60 + 14.4,
            MinutesAfter(Probe(s_now, At(0, 19, 0), At(half, 28, 0)), 0), "the half before, a period on");
    }

    static void ProbeSplitBuy()
    {
        // Bought out at 0, stock again at 9 (the same buy-out in two purchases): no phase kept, out from 9.
        Between(27.0, 28.0, MinutesAfter(Probe(s_now, At(0, 10, 0), At(9, 9, 0)), 9), "9: out from 9, the half on");
        // At 10: a restock in (0, 10]; a period on, the middle of those ten minutes.
        Between(P / 60 - 10 + 4.4, P / 60 - 10 + 5.6, MinutesAfter(Probe(s_now, At(0, 10, 0), At(10, 9, 0)), 10),
            "10: the restock window a period on");
    }

    static void ProbeSeen()
    {
        DateTime seen = s_before.AddMinutes(5);
        DateTime p = KeepRules.ProbeAt(ImmutableArray.Create(At(15, 19, 0)), ImmutableArray.Create(seen), P,
            s_before.AddMinutes(16));
        Between(P, P + 2 * KeepRules.BeatSeconds + KeepRules.RestockMarginSeconds, (p - seen).TotalSeconds,
            "seconds after the seen restock");
    }

    static void ProbeOld()
    {
        DateTime seen = s_before.AddMinutes(5);
        DateTime now = s_before.AddHours(KeepRules.PhaseHours + 1);
        double outAt = (now - s_before).TotalMinutes - 1;
        DateTime p = KeepRules.ProbeAt(ImmutableArray.Create(At(outAt, 19, 0)), ImmutableArray.Create(seen), P, now);
        Between(27.0, 28.0, MinutesAfter(p, outAt), "the seen restock too old: half a period on");
        // Old visits too: out at 0, dry at 20, restocked and out at 30, dry just past a period after that.
        DateTime q = KeepRules.ProbeAt(ImmutableArray.Create(At(0, 19, 0), At(20, 0, 0), At(30, 28, 0),
            At(30 + P / 60 + 1, 0, 0), At(outAt, 19, 0)), s_none, P, now);
        Between(27.0, 28.0, MinutesAfter(q, outAt), "old dry and restocked visits: half a period on");
    }

    static void ProbeLongDry() => Between(27.0, 28.0,
        MinutesAfter(Probe(s_now, At(0, 19, 0), At(60, 0, 0)), 60), "out from the dry visit at 60, the half on");

    static void StrikeAfterCases()
    {
        DateTime at = s_before.AddMinutes(10);
        Between(27.0, 28.0, (KeepRules.StrikeAfter(ImmutableArray<Visit>.Empty, s_none, P, at) - at).TotalMinutes,
            "nothing known: half a period after the refusal");
        DateTime seen = s_before.AddMinutes(5);
        DateTime p = KeepRules.StrikeAfter(ImmutableArray<Visit>.Empty, ImmutableArray.Create(seen), P, at);
        Between(P, P + KeepRules.BeatSeconds + KeepRules.RestockMarginSeconds, (p - seen).TotalSeconds,
            "seconds after the seen restock, refused 5 minutes after it");
        DateTime q = KeepRules.StrikeAfter(ImmutableArray.Create(At(20, 19, 5)), s_none, P, at);
        Between(27.0, 28.0, MinutesAfter(q, 10), "refused at 10, a thread visit at 20: from the refusal");
        Check.True(q != DateTime.MinValue, "stock left is not now");
        // Refused at 10, the thread bought out ten hours on: the hold is long over, not renewed by the thread.
        DateTime r = KeepRules.StrikeAfter(ImmutableArray.Create(At(610, 10, 0)), s_none, P, at);
        Check.True(r < s_before.AddMinutes(10 + P / 60), "an old refusal not held by a later thread buy-out: "
            + MinutesAfter(r, 10).ToString("0.0") + " min after it");
    }

    static void SureAfterCases()
    {
        DateTime at = s_before.AddMinutes(10);
        double none = (KeepRules.SureAfter(ImmutableArray<Visit>.Empty, s_none, P, at) - at).TotalSeconds;
        Between(P, P + KeepRules.RestockMarginSeconds + 1, none, "nothing known: a whole period on");
        DateTime seen = s_before.AddMinutes(5);
        DateTime s = KeepRules.SureAfter(ImmutableArray<Visit>.Empty, ImmutableArray.Create(seen), P, at);
        Between(P, P + KeepRules.BeatSeconds + KeepRules.RestockMarginSeconds + 1, (s - seen).TotalSeconds,
            "a restock seen 5 minutes before the refusal: a period after it");
        ImmutableArray<Visit> v = ImmutableArray.Create(At(0, 19, 0), At(20, 0, 0));
        Check.True(KeepRules.SureAfter(v, s_none, P, at) >= KeepRules.StrikeAfter(v, s_none, P, at),
            "never before the strike guess");
    }

    static void ProbeMovedLater()
    {
        // A restock seen at 5, out at 6; dry a period and a minute after the buy-out, past where the restock had to
        // be: the phase moved later, so the window starts again from the dry visit, half a period on.
        double dry = 6 + P / 60 + 1;
        DateTime p = KeepRules.ProbeAt(ImmutableArray.Create(At(6, 19, 0), At(dry, 0, 0)),
            ImmutableArray.Create(s_before.AddMinutes(5)), P, s_before.AddMinutes(dry));
        Between(27.0, 28.0, MinutesAfter(p, dry), "half a period after the dry visit");
        // The same dry visit with the seen restock's window kept would be probed a period after it.
        DateTime kept = KeepRules.ProbeAt(ImmutableArray.Create(At(6, 19, 0)),
            ImmutableArray.Create(s_before.AddMinutes(5)), P, s_before.AddMinutes(7));
        Between(P / 60, P / 60 + 2, MinutesAfter(kept, 5), "without the dry visit: a period after the seen one");
    }

    static void ProbeContradiction()
    {
        // Restocks seen at 5 and at 25 minutes, which no one phase fits (the phase moved): the later one alone, so a
        // buy-out at 30 is probed a period after 25.
        ImmutableArray<DateTime> seen = ImmutableArray.Create(s_before.AddMinutes(5), s_before.AddMinutes(25));
        DateTime p = KeepRules.ProbeAt(ImmutableArray.Create(At(30, 19, 0)), seen, P, s_before.AddMinutes(31));
        Between(P / 60, P / 60 + 2, MinutesAfter(p, 25), "a period after the later restock");
    }

    static void ProbeConverges()
    {
        foreach (double phase in new[] { 1.0, 13.7, 27.4, 40.0, 54.0 })
        {
            List<Visit> visits = new() { At(0, 19, 0) };
            DateTime empty = s_before;
            // The first strike strictly after `after`.
            DateTime Strike(DateTime after)
            {
                double k = Math.Floor(((after - s_before).TotalSeconds - phase * 60) / P) + 1;
                return s_before.AddSeconds(phase * 60 + k * P);
            }
            // Six probes span at most six periods, inside PhaseHours.
            for (int i = 0; i < 6; i++)
            {
                DateTime p = KeepRules.ProbeAt(visits.ToImmutableArray(), s_none, P, empty);
                bool restocked = Strike(empty) <= p;
                visits.Add(new Visit(p, restocked ? 28 : 0, 0));
                empty = p;
            }
            DateTime last = KeepRules.ProbeAt(visits.ToImmutableArray(), s_none, P, empty);
            double past = (last - Strike(empty)).TotalSeconds;
            Between(0, KeepRules.NarrowSeconds + KeepRules.RestockMarginSeconds + KeepRules.BeatSeconds, past,
                "phase " + phase + ": seconds past the true strike after 6 probes");
        }
    }

    static void EndsTerminal()
    {
        foreach (string why in new[] { "stopped", "stopped; by rune", "the player took over",
            "assist disabled by the fail-safe: the server refused assist actions (4 in 60 s)",
            "the connection is closed", "no game connection", "halted: client packet unframed", "the player is dead" })
            Check.True(KeepRules.Ends(why), why);
    }

    static void EndsNot()
    {
        foreach (string why in new[] { "the recall did not move you (\"You are already casting a spell.\")",
            "no route to 1471,1695", "Morena gave no answer", "the vendor has none", "unstopped" })
            Check.False(KeepRules.Ends(why), why);
        Check.False(KeepRules.Ends(null), "null");
    }

    static readonly DateTime s_tick = new(2026, 10, 24, 5, 52, 31, DateTimeKind.Local);

    static ImmutableArray<DateTime> Seen(params double[] seconds)
    {
        ImmutableArray<DateTime>.Builder b = ImmutableArray.CreateBuilder<DateTime>();
        foreach (double s in seconds)
            b.Add(s_tick.AddSeconds(s));
        return b.ToImmutable();
    }

    static void PeriodFromGaps()
    {
        Check.Equal(KeepRules.RestockPeriodSeconds, KeepRules.RestockPeriod(ImmutableArray<DateTime>.Empty), "none");
        Check.Equal(KeepRules.RestockPeriodSeconds, KeepRules.RestockPeriod(Seen(0)), "one");
        Check.Equal(3290.0, KeepRules.RestockPeriod(Seen(2 * 3290, 0)), "two periods apart, out of order");
    }

    static void PeriodSkips()
    {
        Check.Equal(KeepRules.RestockPeriodSeconds, KeepRules.RestockPeriod(Seen(0, 1600)), "under half a period");
        // PeriodTolerance of 3288 is 164.4 s.
        Check.Equal(3288.0 + 164, KeepRules.RestockPeriod(Seen(0, 3288 + 164)), "within the tolerance counted");
        Check.Equal(KeepRules.RestockPeriodSeconds, KeepRules.RestockPeriod(Seen(0, 3288 + 165)), "past it not");
        Check.Equal(3288.0 - 164, KeepRules.RestockPeriod(Seen(0, 3288 - 164)), "within it below counted");
        Check.Equal(KeepRules.RestockPeriodSeconds, KeepRules.RestockPeriod(Seen(0, 3288 - 165)), "past it below not");
        Check.Equal(3284.0, KeepRules.RestockPeriod(Seen(0, 3280, 3280 + 3284, 3280 + 3284 + 3290)),
            "the median of 3280, 3284, 3290");
    }

}
