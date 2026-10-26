// Keep thread's decisions (docs/Designs/Active/Apps/AssistTesting.md, T3), pure so that every boundary is tested.
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Uoaix.Assist;

// A thread vendor as the purse sees it: the stock it is expected to hold and its price for one thread.
public sealed record VendorStock(int Expected, int Price);

// One visit to a thread vendor: when, the stock it showed, and the stock it kept after the visit.
public sealed record Visit(DateTime At, int Shown, int Left);

public static class KeepRules
{
    // The gold a city's visit carries so that each vendor's whole stock goes in one purchase (Damian, 2026-10-22: "this
    // npc has to know how much gold to carry around to pickup the full amount of all the threads in single vendor in a
    // single purchase"): every vendor's expected stock at its price, summed, as VendorBuy buys them one after another.
    // A price under 1 counts as 1 and a stock under 0 as none, as VendorBuy charges them.
    public static int PurseFor(ImmutableArray<VendorStock> vendors)
    {
        long sum = 0;
        foreach (VendorStock v in vendors)
            sum += (long)Math.Max(0, v.Expected) * Math.Max(1, v.Price);
        return (int)Math.Min(int.MaxValue, sum);
    }

    // When the vendor was last bought out, after a visit that saw shown and left left: a stock seen this visit, or at
    // the visit before, that is gone now was bought out now. A dry visit to a vendor already out moves nothing: the
    // visits of 2026-10-22 overwrote every real buy-out with the time of a dry ask, and a restock period learned from
    // them came out short.
    public static DateTime BoughtOut(DateTime before, int stockBefore, int shown, int left, DateTime now)
    {
        return left == 0 && (shown > 0 || stockBefore > 0) ? now : before;
    }

    // A buy visit from its attempts record line's text: "bought 64 for 192, 0 left", "none: the vendor has none",
    // "by hand: none", "by hand: saw 37", "by hand: bought 64". Any other text (a refusal, a short purse) says nothing
    // of the stock: null.
    public static Visit? ParseVisit(DateTime at, string text)
    {
        if (text == "none: the vendor has none" || text == "by hand: none")
            return new Visit(at, 0, 0);
        const string saw = "by hand: saw ";
        if (text.StartsWith(saw, StringComparison.Ordinal) && int.TryParse(text.Substring(saw.Length), out int seen)
            && seen >= 0)
            return new Visit(at, seen, seen);
        // A thread buy by hand is the vendor bought out (Damian, 2026-10-20: "didn't it see that we just bought from
        // stanley"; AssistFeatures.BuyClosed records it so).
        const string hand = "by hand: bought ";
        if (text.StartsWith(hand, StringComparison.Ordinal)
            && int.TryParse(text.Substring(hand.Length), out int handBought) && handBought > 0)
            return new Visit(at, handBought, 0);
        if (!text.StartsWith("bought ", StringComparison.Ordinal))
            return null;
        string[] words = text.Split(' ');
        // bought N for G, L left
        if (words.Length == 6 && int.TryParse(words[1], out int bought) && int.TryParse(words[4], out int left)
            && words[5] == "left" && bought >= 0 && left >= 0)
            return new Visit(at, bought + left, left);
        return null;
    }

    // A stock seen sooner than this after a buy-out is the same buy-out going on, not a restock: a buy-out in two
    // purchases (Jannelle's 10 at 3 gp, then her last 9 at 4) read as restocks 0 to 2 minutes apart in the records of
    // 2026-10-21, against periods of about 50.
    public const int MinRestockMinutes = 10;

    // A uo1998 vendor's restock period: 3280 to 3296 s over the attempts record of 2026-10-20 to 23, against OUO's 201
    // beats of 16 s, 3216 (tailoring-circuit.md, "How a 1998 vendor restocks").
    public const double RestockPeriodSeconds = 3288;

    // A refined period lies within this share of RestockPeriodSeconds; a gap that gives one outside it (a restock
    // missed between, a server restart) is not counted.
    public const double PeriodTolerance = 0.05;

    // The period the seen restocks show: each gap between two in time order divided by the whole periods it spans, the
    // median of those within PeriodTolerance; RestockPeriodSeconds with none.
    public static double RestockPeriod(ImmutableArray<DateTime> seen)
    {
        DateTime[] sorted = new DateTime[seen.Length];
        seen.CopyTo(sorted);
        Array.Sort(sorted);
        ImmutableArray<double>.Builder periods = ImmutableArray.CreateBuilder<double>();
        for (int i = 1; i < sorted.Length; i++)
        {
            double gap = (sorted[i] - sorted[i - 1]).TotalSeconds;
            double whole = Math.Round(gap / RestockPeriodSeconds);
            if (whole < 1)
                continue;
            double one = gap / whole;
            if (Math.Abs(one - RestockPeriodSeconds) <= RestockPeriodSeconds * PeriodTolerance)
                periods.Add(one);
        }
        return periods.Count == 0 ? RestockPeriodSeconds : Median(periods.ToImmutable());
    }

    // A narrow window is visited this long past its end: the phase moves (a wake from frozen adds a 16 s beat).
    public const int RestockMarginSeconds = 60;

    // A vendor restocks once a period at a phase of its own, which nobody is told (Damian, 2026-10-24: "each vendor has
    // a period which is about 55 mintues, but we don't know when any clock strikes due. so we need a way of figuring
    // that out by probing and narrowing down that strike window"). The phases still possible, as arcs of the period: a
    // visit finding the vendor restocked since it was last known empty keeps only the arc between, a dry visit drops
    // it, and a restock seen (AssistState.Restocks) keeps the beat before it. Other players buy vendors out too, so a
    // dry visit can drop the true phase: evidence that leaves no phase is answered as Keep and Drop say.
    public const int BeatSeconds = 16;
    // Evidence older than this is not used. The phase moves (one phase fit the visits of 2026-10-21 before 11:00 and
    // one after 13:00, not across); a move shows sooner as a dry visit at a narrow window's end, which leaves no phase
    // and starts the window again from that visit, so the bound is long: a narrow window's visits, each a period
    // apart, add nothing to it, and a short bound would forget it.
    public const int PhaseHours = 24;
    // A window this narrow or narrower is visited at its end, past which the restock has fallen; a wider one at its
    // median, so the visit halves it whatever it finds ("in narrowing down the actual period and respawn moment, you
    // check a few like bisecting is fine").
    public const int NarrowSeconds = 120;

    // When a vendor is next worth a visit for thread: MinValue (now) when it is not known to be bought out (stock left
    // at its last visit, or no visit); else the median of the times its next restock can still fall at, within one
    // period of the soonest it can, or that window's end plus RestockMarginSeconds once it is NarrowSeconds or under.
    // visits oldest first.
    public static DateTime ProbeAt(ImmutableArray<Visit> visits, ImmutableArray<DateTime> seen, double periodSeconds,
        DateTime now)
    {
        List<double[]> can = Window(visits, seen, periodSeconds, now, out DateTime? empty, out DateTime after);
        return empty is null ? DateTime.MinValue : Next(can, after, periodSeconds);
    }

    // When the vendor's first restock after `at` is worth a visit, by the same window, whatever its thread: a tailor
    // that has bought her fill of shirts buys again at her restock (OUO: an item sold to a vendor goes at its restock).
    // Counted from `at`, the shirts' own event: the thread visits only place the phase (one restock clock a vendor),
    // never move the hold (Damian, 2026-10-25: "we need seperate the logic of shirts and threads"; Jannelle's 03:39
    // refusal held her from 13:41 on by a thread buy-out).
    public static DateTime StrikeAfter(ImmutableArray<Visit> visits, ImmutableArray<DateTime> seen,
        double periodSeconds, DateTime at)
    {
        List<double[]> can = Window(visits, seen, periodSeconds, at, out DateTime? _, out DateTime _);
        return Next(can, at, periodSeconds);
    }

    // The phases still possible; empty: since when the vendor is known out of thread (null: stock left, or no visit);
    // after: the latest event.
    static List<double[]> Window(ImmutableArray<Visit> visits, ImmutableArray<DateTime> seen, double periodSeconds,
        DateTime now, out DateTime? emptyAt, out DateTime latest)
    {
        List<double[]> can = new() { new[] { 0.0, periodSeconds } };
        DateTime from = now.AddHours(-PhaseHours);
        List<Visit> events = new(visits);
        foreach (DateTime t in seen)
            events.Add(new Visit(t, -1, -1));
        events.Sort((a, b) => a.At.CompareTo(b.At));
        DateTime? empty = null;
        // The latest event (events are in time order): the next restock falls after it.
        DateTime after = DateTime.MinValue;
        foreach (Visit v in events)
        {
            bool used = v.At >= from;
            if (v.Shown < 0)
            {
                if (used)
                    Keep(can, v.At.AddSeconds(-BeatSeconds), v.At, periodSeconds);
                empty = null;
                after = v.At;
                continue;
            }
            if (empty is DateTime e0)
            {
                double span = (v.At - e0).TotalSeconds;
                if (v.Shown == 0 && span < periodSeconds && used)
                    Drop(can, e0, v.At, periodSeconds);
                else if (v.Shown == 0 && span >= periodSeconds)
                {
                    // Dry just past a period after a buy-out: no restock fell where one had to, so the phase moved
                    // later, and the window starts again. Dry long after: another player bought the restock.
                    if (used && span < periodSeconds + 2 * NarrowSeconds)
                    {
                        can.Clear();
                        can.Add(new[] { 0.0, periodSeconds });
                    }
                    empty = v.At;
                }
                else if (v.Shown > 0 && span >= MinRestockMinutes * 60 && span < periodSeconds && used)
                    Keep(can, e0, v.At, periodSeconds);
            }
            if (v.Shown > 0)
                empty = v.Left == 0 ? v.At : null;
            else if (empty is null)
                empty = v.At;
            after = v.At;
        }
        emptyAt = empty;
        latest = after;
        return can;
    }

    // The probe for the first restock after `after`: the median of the times left within one period, or the window's
    // end plus RestockMarginSeconds once it is NarrowSeconds or under.
    static DateTime Next(List<double[]> can, DateTime after, double periodSeconds)
    {
        List<double[]> ahead = Ahead(can, after, periodSeconds);
        // Never empty: Keep and Drop each leave a phase.
        double total = 0;
        foreach (double[] s in ahead)
            total += s[1] - s[0];
        double end = ahead[ahead.Count - 1][1];
        if (end - ahead[0][0] <= NarrowSeconds)
            return after.AddSeconds(end + RestockMarginSeconds);
        double half = total / 2;
        foreach (double[] s in ahead)
        {
            if (half <= s[1] - s[0])
                return after.AddSeconds(s[0] + half);
            half -= s[1] - s[0];
        }
        return after.AddSeconds(end);
    }

    // When the vendor has surely restocked since `at`: the last phase the window leaves within one period, plus
    // RestockMarginSeconds; a whole period on with nothing known. A trip to a tailor waits for this; a check where the
    // player already is goes at StrikeAfter's guess (Damian, 2026-10-25: "not go out of our way for the check. if we are
    // already there, its nearly free. if we have to travel, it costs at least 15gp of pain").
    public static DateTime SureAfter(ImmutableArray<Visit> visits, ImmutableArray<DateTime> seen, double periodSeconds,
        DateTime at)
    {
        List<double[]> can = Window(visits, seen, periodSeconds, at, out DateTime? _, out DateTime _);
        List<double[]> ahead = Ahead(can, at, periodSeconds);
        return at.AddSeconds(ahead[ahead.Count - 1][1] + RestockMarginSeconds);
    }

    // The window's arcs as seconds after `after`, in order: an arc holding after's own phase splits at it.
    static List<double[]> Ahead(List<double[]> can, DateTime after, double periodSeconds)
    {
        double into = Phase(after, periodSeconds);
        List<double[]> ahead = new();
        foreach (double[] s in can)
        {
            double a = s[0] - into, b = s[1] - into;
            if (b <= 0)
                ahead.Add(new[] { a + periodSeconds, b + periodSeconds });
            else if (a < 0)
            {
                ahead.Add(new[] { 0.0, b });
                ahead.Add(new[] { a + periodSeconds, periodSeconds });
            }
            else
                ahead.Add(new[] { a, b });
        }
        ahead.RemoveAll(s => s[1] <= s[0]);
        ahead.Sort((x, y) => x[0].CompareTo(y[0]));
        return ahead;
    }

    static readonly DateTime s_epoch = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Local);
    static double Phase(DateTime t, double periodSeconds)
    {
        double s = (t - s_epoch).TotalSeconds % periodSeconds;
        return s < 0 ? s + periodSeconds : s;
    }
    // The phases of the times (a, b], as arcs [start, end) of [0, period); b - a under a period.
    static List<double[]> Arc(DateTime a, DateTime b, double periodSeconds)
    {
        double span = (b - a).TotalSeconds;
        double pa = Phase(a, periodSeconds), pe = pa + span;
        return pe <= periodSeconds ? new List<double[]> { new[] { pa, pe } }
            : new List<double[]> { new[] { pa, periodSeconds }, new[] { 0.0, pe - periodSeconds } };
    }
    static List<double[]> Intersect(List<double[]> x, List<double[]> y)
    {
        List<double[]> r = new();
        foreach (double[] s in x)
            foreach (double[] t in y)
            {
                double lo = Math.Max(s[0], t[0]), hi = Math.Min(s[1], t[1]);
                if (hi > lo)
                    r.Add(new[] { lo, hi });
            }
        return r;
    }
    static List<double[]> Subtract(List<double[]> x, List<double[]> y)
    {
        List<double[]> r = new(x);
        foreach (double[] t in y)
        {
            List<double[]> next = new();
            foreach (double[] s in r)
            {
                if (t[1] <= s[0] || t[0] >= s[1])
                {
                    next.Add(s);
                    continue;
                }
                if (t[0] > s[0])
                    next.Add(new[] { s[0], t[0] });
                if (t[1] < s[1])
                    next.Add(new[] { t[1], s[1] });
            }
            r = next;
        }
        return r;
    }
    // can narrowed to (a, b] (keep) or off it (drop). A restock found that leaves no phase keeps only itself; a dry
    // visit that leaves none (no restock in a period's span: the phase moved later) starts the window again.
    static void Keep(List<double[]> can, DateTime a, DateTime b, double periodSeconds)
    {
        List<double[]> arc = Arc(a, b, periodSeconds);
        List<double[]> r = Intersect(can, arc);
        can.Clear();
        can.AddRange(r.Count > 0 ? r : arc);
    }
    static void Drop(List<double[]> can, DateTime a, DateTime b, double periodSeconds)
    {
        List<double[]> arc = Arc(a, b, periodSeconds);
        List<double[]> r = Subtract(can, arc);
        can.Clear();
        if (r.Count == 0)
            can.Add(new[] { 0.0, periodSeconds });
        else
            can.AddRange(r);
    }

    // The middle value, or the mean of the two middle values of an even count; values not empty.
    public static double Median(ImmutableArray<double> values)
    {
        double[] sorted = new double[values.Length];
        values.CopyTo(sorted);
        Array.Sort(sorted);
        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    // A step's reason that ends the whole circuit, never a fallback and never a vendor's failure: a stop, the player
    // taking over, the fail-safe, a closed or halted link, a dead player. A vendor skipped on "stopped" counted toward
    // BROKEN; a walk "stopped" fell through to a recall (2026-10-22 06:39:50).
    public static bool Ends(string? why)
    {
        if (why is null)
            return false;
        return why == "stopped" || why.StartsWith("stopped", StringComparison.Ordinal)
            || why.Contains("took over", StringComparison.Ordinal)
            || why.StartsWith("assist disabled by the fail-safe", StringComparison.Ordinal)
            || why == "the connection is closed" || why == "no game connection"
            || why.StartsWith("halted: ", StringComparison.Ordinal) || why == "the player is dead";
    }

    // The gold a bank stop leaves in the pack: the purse setting, and while Keep thread runs never less than its visit
    // needs (the recall bank put 650 of a 900 purse back at 2026-10-22 06:42:03).
    public static int PackKeeps(int purseKeep, int keepNeed, bool keeping)
    {
        return keeping ? Math.Max(purseKeep, keepNeed) : purseKeep;
    }
}
