// The Assist's timings (docs/Designs/Active/Apps/AssistStructure.md, stage 1): what each timing is (TimingCatalog), its
// value per login server with its change log and auto-tune (ITimings), and which timings each task waits on
// (TaskTimings, TaskTimingRegistry).
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Text;

namespace Uoaix.Assist;

// Action timings (Damian, 2026-10-16: "make all the delays configurable so we don't have to redo this if its too slow",
// "then add active monitoring and tuning of these shared configurations"): each kept per login server as ms.<key> and
// shown in the window's Timings panel with what was seen. A measured timing waits for something the server sends;
// each arrival is a sample of how long it took. With auto-tune on (ms.auto, per server) a sample past the timing
// raises it at once, and from TuneAfter samples it follows the longest of the last TuneKeep, times 1.5 plus 100 ms,
// never under half its default: an action timeout cut too short reads as a server refusal. No one change moves it more
// than TuneCapPct, and a larger one asked for is reported (Tune).
// Name is the panel's short label and Text its tooltip; Group is the panel's subgroup.
public sealed record Timing(string Key, string Group, string Name, int Default, bool Measured, string Text);

public static class TimingCatalog
{
    public const int TuneKeep = 20;
    public const int TuneAfter = 5;
    public const int MaxMs = 600000;
    public const int TuneSlackMs = 100;
    public const int TuneCapPct = 50;
    public const int StepMs = 50;
    public const int QosOneIn = 50;

    static readonly ImmutableArray<Timing> s_all = ImmutableArray.Create(
        new Timing("answer", "Server", "Answer", 3000, true, "Longest wait for the server to answer an action."),
        new Timing("cursor", "Server", "Cursor", 3000, true, "Longest wait for a target cursor after a use."),
        new Timing("lift-settle", "Server", "Lift settle", 600, true,
            "A lift counts as accepted after this long with no refusal."),
        new Timing("pending-dig", "Mining and smelting", "Dig result", 6000, true,
            "A dig with no result this long counts as answered."),
        new Timing("chop-wait", "Lumberjacking", "Chop result", 20000, false,
            "A chop with no answer this long counts as unanswered; three in a row end the circuit."),
        // A chicken died 2.1 s after the attack on uo1998 (2026-10-23); an eagle fights back for longer.
        new Timing("kill-wait", "Feathers", "Kill", 30000, false,
            "A bird attacked and not dead this long is left, and the next one hunted."),
        // The carve's lines came 200 ms after its target on uo1998 (2026-10-23 22:31:36).
        new Timing("carve-wait", "Feathers", "Carve result", 3000, false,
            "A carve with no answer this long counts as unanswered."),
        // Every put-down waits it, not ore alone (Damian, 2026-10-20: "ore drop timing details in the watch me sell is
        // werid").
        new Timing("drop-settle", "Server", "Drop settle", 700, false,
            "After something is put down or handed over (ore on the ground, items sold to a vendor, a bolt stacked), "
            + "before checking the server took it."),
        new Timing("smelt", "Mining and smelting", "Smelt result", 4000, true, "Longest wait for a smelt's result."),
        new Timing("smelt-settle", "Mining and smelting", "Ingot count", 300, false,
            "After the smelted ore is gone, before the ingots are counted."),
        new Timing("tailor", "Tailoring", "Sew result", 4000, true,
            "Longest wait for a sewing's result after the last menu pick."),
        new Timing("craft", "Smithing", "Build max", 120000, false, "Longest a smith build may take."),
        new Timing("craft-quiet", "Smithing", "Hammer quiet", 6000, true,
            "A smith build with no hammer sound this long has ended."),
        new Timing("quality-wait", "Smithing", "Quality line", 600, false,
            "After a build, before its quality line is read."),
        new Timing("label-wait", "Smithing", "Item label", 1500, true, "Longest wait for a built item's name label."),
        new Timing("busy", "Harvest and stations", "Busy", 1250, false,
            "How long a double-click or a lift keeps you busy, for every task without a busy of its own (Busy by "
            + "task). Auto-tune leaves a refusal of a lift as busy alone at 1 in " + QosOneIn + " or "
            + "rarer; more often, it raises the busy of the task that was refused by half, then after twice the lifts "
            + "seen between the refusals, taken in a row, lowers it " + StepMs + " ms at a time, down to "
            + StepMs + " ms above the value refused."),
        // Each task's own busy (Damian, 2026-10-21: "i think every task might need its own busy wait"): a default
        // under 0 is the shared busy until the task's own is set or tuned (AssistTimings.Ms).
        // 1020: a double-click sets the 1998 server's actionState to 4 (OUO DispatchDoubleClick), one off a 250 ms tick
        // (CPlayer_RegenTick), so the busy ends 750 to 1000 ms after the kit's use; uo1998's tick runs about 2% late
        // (the restock period, 3288 s against 3216), so 4 ticks reach 1020.
        new Timing("busy:sew", "Busy by task", "Sewing", 1020, false,
            "Busy for sewing, from the kit's double-click to the next: the tailoring loop and the circuit's sewing."),
        new Timing("busy:weave", "Busy by task", "Mill, spin and weave", -1, false,
            "Busy for the mill, wheel and loom. Unset, the shared Busy."),
        new Timing("busy:smelt", "Busy by task", "Smelting", -1, false,
            "Busy for the smelting loop's double-click and lift. Unset, the shared Busy."),
        new Timing("busy:mine", "Busy by task", "Mining", -1, false,
            "Busy for digging and putting ore down. Unset, the shared Busy."),
        new Timing("busy:smith", "Busy by task", "Smithing", -1, false, "Busy for smithing. Unset, the shared Busy."),
        new Timing("busy:lumber", "Busy by task", "Lumberjacking", -1, false,
            "Busy for chopping and putting the logs down. Unset, the shared Busy."),
        new Timing("busy:feathers", "Busy by task", "Feathers", -1, false,
            "Busy for carving and putting the feathers down. Unset, the shared Busy."),
        new Timing("busy:magery", "Busy by task", "Magery", -1, false,
            "Busy for the magery loop. Unset, the shared Busy."),
        new Timing("busy:inscribe", "Busy by task", "Inscription", -1, false,
            "Busy for writing spellbooks. Unset, the shared Busy."),
        new Timing("busy:harvest", "Busy by task", "Harvest", -1, false,
            "Busy between harvest actions. Unset, the shared Busy."),
        new Timing("busy:pickup", "Busy by task", "Item pickup", -1, false,
            "Busy for item pickup after another action. Unset, the shared Busy."),
        new Timing("busy:recall", "Busy by task", "Recall and teleport", -1, false,
            "Busy after a recall, teleport or mark is refused. Unset, the shared Busy."),
        new Timing("busy:arm", "Busy by task", "Arm and disarm", -1, false,
            "Busy for arming and disarming. Unset, the shared Busy."),
        new Timing("busy:eat", "Busy by task", "Eating and feeding", -1, false,
            "Busy between bites and feeding the mount. Unset, the shared Busy."),
        new Timing("busy:loadout", "Busy by task", "Reagent loadout", -1, false,
            "Busy between the reagent loadout's bag opens and lifts. Unset, the shared Busy."),
        new Timing("busy:circuit", "Busy by task", "Circuits", -1, false,
            "Busy for a circuit's lifts and double-clicks (thread, cloth, the bank). Unset, the shared Busy."),
        new Timing("pluck", "Harvest and stations", "Pluck", 1500, true, "Longest wait for what a pluck gives."),
        new Timing("spin", "Harvest and stations", "Spin", 6500, false,
            "A spinning wheel is busy this long after it takes a bale."),
        new Timing("work-wait", "Harvest and stations", "Station", 12000, true,
            "Longest wait for a mill, wheel or loom to take or give."),
        new Timing("harvest-scan", "Harvest and stations", "Harvest scan", 300, false,
            "How often harvest looks for a crop, wool or a sheep within reach."),
        new Timing("pluck-follow", "Harvest and stations", "Pluck follow", 300, false,
            "After a plant changes on a pluck, how long to wait for a new item on its tile before taking the plant "
            + "itself."),
        new Timing("reshear", "Harvest and stations", "Reshear", 600000, false,
            "A shorn sheep is left alone this long before it is sheared again."),
        new Timing("pickup-scan", "Item pickup", "Ground scan", 25, false,
            "How often pickup looks at the ground (Damian, 2026-10-17: \"down to 25ms chunks\")."),
        new Timing("pickup-walking", "Item pickup", "Walking", 600, false,
            "A step within this long counts as walking, and a walking pickup reaches only 1 tile: a mounted run "
            + "steps every 100 to 200 ms, a walk every 400."),
        new Timing("pickup-hold", "Item pickup", "Refusal hold", 60000, false,
            "After any pickup refusal, the lift gap and settle are not lowered for this long."),
        new Timing("mana-wait", "Magery", "Mana wait", 300000, false, "Longest the magery loop waits for mana."),
        new Timing("walk-step", "Walking", "Walk step", 400, false, "Between steps walking on foot."),
        new Timing("door-held", "Walking", "Held door wait", 5000, false,
            "How long a walk waits for someone standing beside the only door to step away, before taking the door "
            + "anyway or giving the target up (Damian, 2026-10-20: the 20 s wait let Michelle walk off)."),
        new Timing("walk-run", "Walking", "Run step", 200, false, "Between steps running on foot."),
        new Timing("walk-mount", "Walking", "Ride step", 200, false, "Between steps walking mounted."),
        new Timing("run-mount", "Walking", "Gallop step", 100, false,
            "Between steps running mounted (ServUO's mounted run pace)."),
        new Timing("step-settle", "Walking", "Refused step", 300, false,
            "After a refused step, before planning again."),
        new Timing("warn-rest", "Walking", "Horse rest", 5000, false,
            "The rest when the horse is very fatigued, then walking until it is rested."),
        new Timing("recall-walk", "Walking", "Recall over", 15000, false,
            "A recall to a rune replaces a walk only when the walk would run longer than this (the straight line at "
            + "the step pace), and the rune lands nearer: two shops a short run apart are walked."),
        new Timing("fatigue-rest", "Walking", "Fatigue rest", 10000, false,
            "The first rest when too fatigued to move, doubled for each more in a walk, at most 60 s: a mount regains "
            + "a point about every 2.8 s."),
        new Timing("vendor-still", "Vendors", "Vendor still", 5000, false,
            "A vendor asked by name and unanswered is asked again once it has stood still this long (on its way "
            + "home)."),
        new Timing("vendor-land", "Vendors", "Vendor landed", 1000, false,
            "The same, after the vendor teleported home."),
        new Timing("restock-poll", "Vendors", "Restock check", 5000, false,
            "Between checks while waiting on a thread vendor's restock."),
        new Timing("speech-wait", "Vendors", "Speech answer", 4000, false,
            "Longest wait for a vendor's spoken answer or window after asking it (at least the answer wait): Simon "
            + "answered 1.55 s after the ask (Trinsic, 2026-10-17)."),
        new Timing("reply-grace", "Vendors", "Reply grace", 700, false,
            "After a vendor speaks, how long a window may still follow before the speech counts as its answer (a "
            + "greeting before the window)."),
        new Timing("vendor-home", "Vendors", "Vendor home", 60000, false,
            "Longest wait for a vendor that sent you home to walk back to its shop before it is passed over."),
        new Timing("sale-confirm", "Vendors", "Sale confirm", 5000, false,
            "A sold item the server deletes within this long counts as sold."),
        new Timing("label-gap", "Vendors", "Label clicks", 150, false, "Between single clicks that read rune labels."),
        new Timing("sell-gap", "Vendors", "Shop ask gap", 2500, false,
            "Between a shop window and the next buy or sell ask: asked 0.7 s after a sale, Nana answered \"I am a "
            + "tailor. I could purchase those things that I supply.\" with no window (Magincia, 2026-10-17)."),
        new Timing("pricer-open", "Other", "Bag open", 700, false, "After the pricer opens a bag, before the next."),
        new Timing("loadout-cursor-gap", "Other", "Cursor gap", 300, false,
            "Between the reagent loadout's two target cursors."),
        new Timing("look", "Other", "Name click", 1500, false, "Between single clicks that name people nearby."));

    static readonly Map<string, Timing> s_byKey = ByKey();

    public static ImmutableArray<Timing> All => s_all;

    public static Timing? Of(string key)
    {
        return s_byKey[key];
    }

    static Map<string, Timing> ByKey()
    {
        Map<string, Timing> map = Map<string, Timing>.Empty;
        foreach (Timing t in s_all)
            map = map.Set(t.Key, t);
        return map;
    }
}

// The control port's timing command (Damian, 2026-10-24: "add the control port commands", after "answer" held at 4500
// by a sample of his own play): "timing" lists every timing as key=ms, "timing <key>" answers one, "timing <key> <ms>"
// sets it as the Timings panel does, logged and the auto-tune base reset. A key is the catalog's or a busy:<task> one.
public static class TimingCommand
{
    public static string Run(ITimings t, string[] a)
    {
        if (a.Length < 2)
        {
            StringBuilder all = new("OK");
            foreach (Timing c in TimingCatalog.All)
                all.Append(' ').Append(c.Key).Append('=').Append(t.Ms(c.Key));
            return all.ToString();
        }
        string key = a[1];
        if (TimingCatalog.Of(key) is null && !key.StartsWith("busy:", StringComparison.Ordinal))
            return "ERROR no timing " + key;
        if (a.Length == 2)
            return "OK " + key + "=" + t.Ms(key);
        if (!int.TryParse(a[2], out int ms) || ms < 0)
            return "ERROR timing <key> <ms>: not a time in ms: " + a[2];
        t.Set(key, ms, "set by the control port");
        return "OK " + key + "=" + t.Ms(key);
    }
}

// A timing's value for the login server in play, set by the player or by auto-tune, each change logged.
public interface ITimings
{
    int Ms(string key);
    void Set(string key, int ms, string why = "set by you");
    bool AutoTune
    {
        get; set;
    }
    // A measured timing's arrival after ms; with auto-tune on it may raise or lower the timing.
    void Seen(string key, int ms);
    // An auto-tune change toward want, capped and reported (AssistTimings.Capped); the value set comes back.
    int Tune(string key, int want, string why);
    // The server refused an action the timing paced; with auto-tune on and refusals more often than 1 in QosOneIn, the
    // timing rises. The value set comes back.
    int Refused(string key, string why);
    // An action the timing paced that the server took; with auto-tune on, the stable count of them in a row steps it
    // down by StepMs toward the value last refused.
    void Accepted(string key);
    string SeenText(string key);
    // The change log: every change to a timing or a loop delay, newest last.
    ImmutableArray<string> Lines();
    void Log(string name, int from, int to, string why);
}

public sealed class AssistTimings : ITimings
{
    // Every change to a timing or a loop delay, by auto-tune, auto-detect or the player (Damian, 2026-10-19: "timings
    // should keep a log of all changes to times, auto or user driven"): timing-log-<server>.txt, the last LogKeep
    // lines held for the Timings panel.
    public const int LogKeep = 200;

    readonly AssistSettings m_cfg;
    readonly Func<string> m_server;
    readonly Func<string, string> m_serverFile;
    readonly Action<string> m_note;
    readonly Action<string> m_report;
    readonly Lock m_gate = new();
    ImmutableQueue<string> m_log = ImmutableQueue<string>.Empty;
    int m_logCount;
    string? m_logFor;
    Map<string, ImmutableQueue<int>> m_seen = Map<string, ImmutableQueue<int>>.Empty;
    Set<string> m_reported = Set<string>.Empty;
    ValueMap<string, int> m_gap = ValueMap<string, int>.Empty;
    ValueMap<string, int> m_run = ValueMap<string, int>.Empty;

    readonly IClock m_clock;

    // report: a change auto-tune held back, said where the player sees it. clock: the log's stamps.
    public AssistTimings(AssistSettings cfg, Func<string> server, Func<string, string> serverFile, Action<string> note,
        Action<string> report, IClock clock)
    {
        m_clock = clock;
        m_cfg = cfg;
        m_server = server;
        m_serverFile = serverFile;
        m_note = note;
        m_report = report;
    }

    public int Ms(string key)
    {
        Timing? t = TimingCatalog.Of(key);
        int v = m_cfg.GetInt(m_server() + "ms." + key, int.MinValue);
        if (v != int.MinValue)
            return v;
        if (t is null)
            return 0;
        return t.Default < 0 ? Ms("busy") : t.Default;
    }

    public void Set(string key, int ms, string why = "set by you")
    {
        int was = Ms(key);
        int now = Math.Max(0, Math.Min(TimingCatalog.MaxMs, ms));
        m_cfg.Set(m_server() + "ms." + key, now.ToString());
        // A value not set by auto-tune is the base auto-tune moves within (Capped).
        if (!why.StartsWith("auto-tune"))
        {
            m_cfg.Set(m_server() + "ms." + key + ".base", now.ToString());
            m_cfg.Set(m_server() + "ms." + key + ".refused", "");
            m_cfg.Set(m_server() + "ms." + key + ".need", "");
            m_cfg.Set(m_server() + "ms." + key + ".dir", "");
            lock (m_gate)
                m_reported = m_reported.Remove(key);
        }
        Timing? t = TimingCatalog.Of(key);
        Log(t is null ? key : t.Name, was, now, why);
    }

    // The value auto-tune moves within TuneCapPct of: the player's last setting, else the default.
    int Base(string key)
    {
        int v = m_cfg.GetInt(m_server() + "ms." + key + ".base", int.MinValue);
        if (v != int.MinValue)
            return v;
        Timing? t = TimingCatalog.Of(key);
        return t is null ? Ms(key) : t.Default < 0 ? Ms("busy") : t.Default;
    }

    public bool AutoTune
    {
        get => m_cfg.Get(m_server() + "ms.auto", "on") == "on";
        set => m_cfg.Set(m_server() + "ms.auto", value ? "on" : "off");
    }

    public ImmutableArray<string> Lines()
    {
        lock (m_gate)
        {
            LoadLog();
            return ImmutableArray.CreateRange(m_log);
        }
    }

    public void Log(string name, int from, int to, string why)
    {
        if (from == to)
            return;
        string line = m_clock.UtcNow.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + "  " + name + ": " + from
            + " ms to " + to
            + " ms (" + why + ")";
        lock (m_gate)
        {
            LoadLog();
            Append(line);
        }
        try
        {
            File.AppendAllText(m_serverFile("timing-log"), line + Environment.NewLine);
        }
        catch (Exception)
        {
        }
    }

    // The log of the server in play, read from its file once each time the server changes.
    void LoadLog()
    {
        string server = m_server();
        if (m_logFor == server)
            return;
        m_log = ImmutableQueue<string>.Empty;
        m_logCount = 0;
        m_logFor = server;
        string path = m_serverFile("timing-log");
        if (!File.Exists(path))
            return;
        try
        {
            string[] all = File.ReadAllLines(path);
            for (int i = Math.Max(0, all.Length - LogKeep); i < all.Length; i++)
                Append(all[i]);
        }
        catch (Exception)
        {
        }
    }

    void Append(string line)
    {
        m_log = m_log.Enqueue(line);
        if (++m_logCount > LogKeep)
        {
            m_log = m_log.Dequeue();
            m_logCount--;
        }
    }

    public void Seen(string key, int ms)
    {
        Timing? t = TimingCatalog.Of(key);
        if (t is null || ms < 0)
            return;
        int n = 0;
        int longest = 0;
        lock (m_gate)
        {
            ImmutableQueue<int> samples = m_seen.Get(key, ImmutableQueue<int>.Empty).Enqueue(ms);
            foreach (int v in samples)
                n++;
            if (n > TimingCatalog.TuneKeep)
            {
                samples = samples.Dequeue();
                n--;
            }
            m_seen = m_seen.Set(key, samples);
            foreach (int v in samples)
                longest = Math.Max(longest, v);
        }
        if (!AutoTune || !t.Measured)
            return;
        int now = Ms(key);
        int want = now;
        if (ms > now)
            want = (ms * 3 / 2 + TimingCatalog.TuneSlackMs + 49) / 50 * 50;
        else if (n >= TimingCatalog.TuneAfter)
            want = Math.Max(t.Default / 2, (longest * 3 / 2 + TimingCatalog.TuneSlackMs + 49) / 50 * 50);
        // Lowered only by more than the slack, so one sample's jitter does not rewrite the setting.
        if (want > now || want < now - TimingCatalog.TuneSlackMs)
        {
            string why = ms > now
                ? "the server took " + ms + " ms"
                : "longest of the last " + n + ": " + longest + " ms";
            Tune(key, want, why);
        }
    }

    // An auto-tune change, kept within TuneCapPct of the timing's base, a change past it asked for reported once until
    // the player sets the timing (Capped). The value it was moved to comes back.
    public int Tune(string key, int want, string why)
    {
        int now = Ms(key);
        int b = Base(key);
        int step = Capped(b, want);
        if (step != want)
        {
            bool first;
            lock (m_gate)
            {
                first = !m_reported.Contains(key);
                m_reported = m_reported.Add(key);
            }
            if (first)
            {
                m_report("Timing " + key + " wanted " + now + " to " + want + " ms (" + why + "); held at " + step
                    + " ms, " + TimingCatalog.TuneCapPct + "% from its setting of " + b
                    + " ms. Check the timing if the sample was not this task's.");
            }
        }
        if (step == now)
            return now;
        string capped = step != want ? ", held within " + TimingCatalog.TuneCapPct + "% of " + b + " ms" : "";
        Set(key, step, "auto-tune: " + why + capped);
        m_note("Timing " + key + ": " + now + " ms to " + step + " ms (" + why + ")");
        return step;
    }

    // Damian, 2026-10-22: "auto tune should jump 50% of current on a refusal, and when reaching a stable state, then
    // incrementally test at each 50ms step between the faulty setting and the new one"; "if 999 calls were good and 1
    // got refused, no action need be taken at all ... 1/10 is a problem. so then we want to see 2x trials without
    // refusal"; "accept a mildly misbehaving substrate at say 1 in 50". The gap is the actions taken since the timing's
    // last refusal this session: a refusal after a gap of QosOneIn - 1 or more, or the first of a session (no gap
    // measured), is the substrate's (router, server, client) and changes nothing. A shorter gap on a settled timing
    // raises it by half and the steps go down; 2 x (gap + 1) actions taken in a row are the stable state, each one step
    // down, stopping one StepMs above the value refused. A shorter gap during the steps reverses them ("refusal on the
    // way down should reverse direction and increment by 50ms"): one step up, and each such refusal after it one more,
    // until the stable state settles it. ms.<key>.refused, .need and .dir (down, up, or empty when settled) are kept
    // until the player sets the timing.
    public int Refused(string key, string why)
    {
        int now = Ms(key);
        bool measured;
        int gap;
        lock (m_gate)
        {
            measured = m_gap.TryGet(key, out gap);
            m_gap = m_gap.Set(key, 0);
        }
        if (!AutoTune)
            return now;
        if (!measured || gap + 1 >= TimingCatalog.QosOneIn)
        {
            m_note("Timing " + key + ": refused at " + now + " ms, "
                + (measured ? "1 in " + (gap + 1) : "the first this session")
                + ", within 1 in " + TimingCatalog.QosOneIn + ": kept (" + why + ")");
            return now;
        }
        string dir = m_cfg.Get(m_server() + "ms." + key + ".dir", "");
        m_cfg.Set(m_server() + "ms." + key + ".refused", now.ToString());
        m_cfg.Set(m_server() + "ms." + key + ".need", (2 * (gap + 1)).ToString());
        lock (m_gate)
            m_run = m_run.Set(key, 0);
        why += " at " + now + " ms, 1 in " + (gap + 1);
        if (dir.Length > 0)
        {
            m_cfg.Set(m_server() + "ms." + key + ".dir", "up");
            return Tune(key, now + TimingCatalog.StepMs,
                why + (dir == "down" ? ", during the steps down: reversed" : ", stepping up"));
        }
        m_cfg.Set(m_server() + "ms." + key + ".dir", "down");
        // Down to a StepMs step: rounded up, half of a base off the step (650 to 1000) passes the TuneCapPct cap (975).
        int want = Math.Max(now + TimingCatalog.StepMs, now * 3 / 2 / TimingCatalog.StepMs * TimingCatalog.StepMs);
        return Tune(key, want, why);
    }

    public void Accepted(string key)
    {
        int run;
        lock (m_gate)
        {
            if (m_gap.TryGet(key, out int gap))
                m_gap = m_gap.Set(key, gap + 1);
            run = m_run.Get(key, 0) + 1;
            m_run = m_run.Set(key, run);
        }
        int refused = m_cfg.GetInt(m_server() + "ms." + key + ".refused", int.MinValue);
        int need = m_cfg.GetInt(m_server() + "ms." + key + ".need", 2 * TimingCatalog.QosOneIn);
        string dir = m_cfg.Get(m_server() + "ms." + key + ".dir", "");
        if (!AutoTune || refused == int.MinValue || dir.Length == 0 || run < need)
            return;
        lock (m_gate)
            m_run = m_run.Set(key, 0);
        int now = Ms(key);
        if (dir == "up" || now - TimingCatalog.StepMs <= refused)
        {
            m_cfg.Set(m_server() + "ms." + key + ".dir", "");
            m_note("Timing " + key + ": settled at " + now + " ms, " + run + " taken in a row; " + refused
                + " ms was refused");
            return;
        }
        Tune(key, now - TimingCatalog.StepMs,
            run + " taken in a row at " + now + " ms; " + refused + " ms was refused");
    }

    // Auto-tune keeps a timing within TuneCapPct of its base, the value the player set (Damian, 2026-10-21:
    // "autotune shouldn't change the value by more than 50% without a report"): lift-settle went 450 to 5350 ms on one
    // sample that was the player's own refused lift, and capping each step alone let one sample walk tailor 2000 to
    // 3000 to 4450 ms in two steps two seconds apart (05:13:42, 05:13:44).
    static int Capped(int b, int want)
    {
        if (b <= 0)
            return want;
        int most = b + b * TimingCatalog.TuneCapPct / 100;
        int least = b - b * TimingCatalog.TuneCapPct / 100;
        return Math.Max(least, Math.Min(most, want));
    }

    // For the Timings panel: what was seen this session.
    public string SeenText(string key)
    {
        ImmutableQueue<int>? samples;
        lock (m_gate)
            samples = m_seen[key];
        Timing? t = TimingCatalog.Of(key);
        if (samples is null || samples.IsEmpty)
            return t is not null && t.Measured ? "not seen yet" : "set by hand";
        int count = 0;
        int longest = 0;
        int last = 0;
        foreach (int v in samples)
        {
            count++;
            longest = Math.Max(longest, v);
            last = v;
        }
        return "seen " + count + ", longest " + longest + " ms, last " + last + " ms";
    }
}

// One task's timings: its name and skill, the keys its section shows, and its own busy.
public sealed record TaskTimings(string Name, string Skill, ImmutableArray<string> Keys)
{
    public string Busy
    {
        get
        {
            foreach (string k in Keys)
            {
                if (k.StartsWith("busy:"))
                    return k;
            }
            return "busy";
        }
    }
}

// One row of a task's timings popup: Group is "Task" (this task's alone), "Skill" (shared only with tasks of its skill)
// or "General"; Others names the other tasks that wait on Key.
public sealed record TimingRow(string Group, string Key, string Others);

// Every task's timings in one place (Damian, 2026-10-21: "a popup window that takes the list of timings ... tag it for
// every action"; "two primary groups: task, and shared"; "shared comes in two flavors, those for a particular skill
// like tailoring might share, but then some are general purpose shared").
// The pauses come first (Damian, 2026-10-19: "i was expecting to see, for example, the delay we take to not get
// refusals"): "pace:<loop>" is a loop's delay between uses, "circle:<name>" a magery circle's; then the timings that
// are not measured (a pause the assist takes), then the measured waits for the server.
public static class TaskTimingRegistry
{
    static readonly ImmutableArray<string> s_walk = ImmutableArray.Create(
        "walk-step", "walk-run", "walk-mount", "run-mount", "step-settle", "warn-rest", "fatigue-rest", "recall-walk");
    static readonly ImmutableArray<string> s_circuits = ImmutableArray.Create(
        "tailor", "mine", "smith", "refresh", "spellbook", "mark", "buy");
    // Watch me sell's (its sells: VendorSell, AskNear, SayFor, SaleDeleted).
    static readonly TaskTimings s_watch = new("Watch me sell", "Vendors", ImmutableArray.Create(
        "sell-gap", "drop-settle", "vendor-still", "vendor-land", "vendor-home", "speech-wait", "reply-grace",
        "sale-confirm", "answer"));
    static readonly TaskTimings s_arm = new("Arm and disarm", "Combat", ImmutableArray.Create(
        "busy:arm", "lift-settle", "answer"));
    // Mill, spin and weave (Work, MillStep; Damian, 2026-10-19: "put the timings affecting mill spin and weave in the
    // section for that action").
    static readonly TaskTimings s_work = new("Mill, spin and weave", "Tailoring", ImmutableArray.Create(
        "busy:weave", "spin", "drop-settle", "work-wait", "cursor", "answer"));
    // "pickup:gap" and "pickup:settle" are the adaptive pickup pace.
    static readonly TaskTimings s_pickup = new("Item pickup", "Gathering", ImmutableArray.Create(
        "pickup:gap", "pickup:settle", "pickup-scan", "pickup-walking", "pickup-hold", "busy:pickup", "busy:harvest",
        "harvest-scan", "pluck-follow", "reshear", "pluck", "lift-settle", "cursor", "answer"));
    static readonly TaskTimings s_loadout = new("Reagent loadout", "Supplies", ImmutableArray.Create(
        "busy:loadout", "pricer-open", "lift-settle", "answer"));

    public static TaskTimings Watch => s_watch;
    public static TaskTimings Arm => s_arm;
    public static TaskTimings Work => s_work;
    public static TaskTimings Pickup => s_pickup;
    public static TaskTimings Loadout => s_loadout;

    public static string LoopSkill(LoopKind l)
    {
        if (l.Tailor > 0)
            return "Tailoring";
        if (l.Smelt > 0 || l.Pairs > 0)
            return "Mining";
        if (l.Smith > 0)
            return "Smithing";
        return l.Magery > 0 ? "Magery" : l.Name;
    }

    public static string CircuitSkill(string circuit)
    {
        switch (circuit)
        {
            case "tailor":
                return "Tailoring";
            case "mine":
                return "Mining";
            case "lumber":
                return "Lumberjacking";
            case "smith":
                return "Smithing";
            case "spellbook":
            case "mark":
                return "Magery";
            default:
                return "Supplies";
        }
    }

    public static TaskTimings ForLoop(LoopKind l)
    {
        ImmutableArray<string>.Builder keys = ImmutableArray.CreateBuilder<string>();
        if (l.Magery > 0)
        {
            foreach (LoopKind c in AssistFeatures.CirclePace)
                keys.Add("circle:" + c.Name);
        }
        else if (l.Smith == 0)
        {
            keys.Add("pace:" + l.Name);
        }

        if (l.Tailor > 0)
            keys.AddRange("busy:sew", "tailor", "cursor", "answer", "work-wait");
        else if (l.Smelt > 0)
            keys.AddRange("busy:smelt", "drop-settle", "smelt-settle", "smelt", "lift-settle", "answer");
        else if (l.Magery > 0)
            keys.AddRange("busy:magery", "mana-wait", "cursor", "answer");
        else if (l.Smith > 0)
            keys.AddRange("busy:smith", "quality-wait", "craft", "craft-quiet", "label-wait", "answer");
        else if (l.Pairs > 0)
            keys.AddRange("busy:mine", "drop-settle", "pending-dig", "cursor", "answer");
        else
            keys.AddRange("cursor", "answer");
        return new("Loop " + l.Name, LoopSkill(l), keys.ToImmutable());
    }

    public static TaskTimings ForCircuit(string circuit)
    {
        ImmutableArray<string>.Builder keys = ImmutableArray.CreateBuilder<string>();
        switch (circuit)
        {
            case "tailor":
                foreach (LoopKind l in AssistFeatures.Loops)
                {
                    if (l.Tailor > 0)
                    {
                        keys.Add("pace:" + l.Name);
                        break;
                    }
                }
                keys.AddRange("sell-gap", "vendor-still", "vendor-land", "restock-poll", "label-gap", "busy:circuit",
                    "busy:sew", "busy:recall", "spin");
                break;
            case "mine":
                keys.AddRange("busy:mine", "busy:recall", "drop-settle", "smelt-settle", "label-gap");
                break;
            case "lumber":
                keys.AddRange("busy:lumber", "chop-wait", "drop-settle");
                break;
            case "feathers":
                keys.AddRange("busy:feathers", "kill-wait", "carve-wait", "label-wait", "drop-settle");
                break;
            case "reagents":
                keys.AddRange("pickup-scan", "pickup-walking");
                break;
            case "smith":
                keys.AddRange("sell-gap", "vendor-still", "quality-wait");
                break;
            case "refresh":
                keys.AddRange("sell-gap", "vendor-still", "loadout-cursor-gap");
                break;
            default:
                keys.AddRange("sell-gap", "vendor-still", "label-gap");
                break;
        }
        keys.AddRange(s_walk);
        switch (circuit)
        {
            case "tailor":
                keys.AddRange("tailor", "work-wait");
                break;
            case "mine":
                keys.AddRange("pending-dig", "smelt");
                break;
            case "smith":
                keys.AddRange("craft", "craft-quiet", "label-wait");
                break;
            case "spellbook":
            case "mark":
                keys.Add("mana-wait");
                break;
        }
        keys.AddRange("cursor", "answer", "lift-settle");
        return new("Circuit " + circuit, CircuitSkill(circuit), keys.ToImmutable());
    }

    public static ImmutableArray<TaskTimings> All()
    {
        ImmutableArray<TaskTimings>.Builder all = ImmutableArray.CreateBuilder<TaskTimings>();
        foreach (LoopKind l in AssistFeatures.Loops)
            all.Add(ForLoop(l));
        foreach (string c in s_circuits)
            all.Add(ForCircuit(c));
        all.AddRange(s_work, s_watch, s_arm, s_pickup, s_loadout);
        return all.ToImmutable();
    }

    // The task's keys as popup rows. A busy:<task> unset is the shared busy, so the shared busy is listed as general
    // wherever a task's own is.
    public static ImmutableArray<TimingRow> Group(TaskTimings task)
    {
        ImmutableArray<TaskTimings> all = All();
        ImmutableArray<TimingRow>.Builder rows = ImmutableArray.CreateBuilder<TimingRow>();
        Set<string> done = Set<string>.Empty;
        ImmutableArray<string> keys = task.Keys;
        if (task.Busy != "busy" && !keys.Contains("busy"))
            keys = keys.Add("busy");
        foreach (string key in keys)
        {
            if (done.Contains(key))
                continue;
            done = done.Add(key);
            List<string> others = new();
            bool otherSkill = key == "busy";
            foreach (TaskTimings s in all)
            {
                if (s.Name == task.Name || !s.Keys.Contains(key))
                    continue;
                others.Add(s.Name);
                if (s.Skill != task.Skill)
                    otherSkill = true;
            }
            string group = others.Count == 0 && key != "busy" ? "Task" : otherSkill ? "General" : "Skill";
            rows.Add(new(group, key, string.Join(", ", others)));
        }
        return rows.ToImmutable();
    }

    // Every task that waits on key; for the shared busy, also every task whose own busy falls back to it.
    public static ImmutableArray<string> UsersOf(string key)
    {
        ImmutableArray<string>.Builder users = ImmutableArray.CreateBuilder<string>();
        foreach (TaskTimings s in All())
        {
            bool own = s.Keys.Contains(key);
            bool fallback = key == "busy" && !own && s.Busy != "busy";
            if (own || fallback)
                users.Add(s.Name + " (" + s.Skill + ")" + (fallback ? ": while its own busy is unset" : ""));
        }
        return users.ToImmutable();
    }
}
