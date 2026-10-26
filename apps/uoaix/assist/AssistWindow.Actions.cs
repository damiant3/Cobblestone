#nullable enable
using System;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Threading;
using System.Collections.Immutable;

namespace Uoaix.Assist;

public sealed partial class AssistWindow
{
    void BuildActions()
    {
        Panel g = Group("Loops");
        Summarize(g, () => m_f.Status);
        FieldGrid lg = Grid(g);
        m_loops = lg.Line("Loop", Pick(200), null, true);
        foreach (LoopKind l in AssistFeatures.Loops)
            m_loops.Items.Add(l.Name);
        m_loops.SelectedIndex = Math.Max(0,
            AssistFeatures.Loops.FindIndex(l => l.Name == m_f.Cfg.Get("window.loop", "Mining (2 tools)")));
        m_loops.SelectedIndexChanged += (s, e) => { LoopKind k = Kind(); m_f.Cfg.Set("window.loop",
            k.Name); m_pickingLoop = true; try { m_rounds.Value = m_f.Cfg.GetInt("rounds." + k.Name,
            10); } finally { m_pickingLoop = false; } };
        // A running loop reads both boxes at every round (AssistFeatures.LiveRounds, LiveUntil).
        m_rounds = lg.Field("Times",
            Num(1, 9999, m_f.Cfg.GetInt("rounds." + Kind().Name, 10),
            v => { if (m_pickingLoop) return; m_f.LiveRounds = v; m_f.Cfg.Set("rounds." + Kind().Name,
            v.ToString()); }), "The rounds the loop runs.");
        m_untilSkill = lg.Field("Until skill",
            new NumericUpDown { Minimum = 0, Maximum = 100, DecimalPlaces = 1, Increment = 0.1m, Width = NumW },
            "Stop once the loop's skill reaches this; 0 runs the rounds.");
        m_untilSkill.ValueChanged += (s, e) => m_f.LiveUntil = (int)(m_untilSkill.Value * 10);
        // The loop's delay on this server, shown and overridden here; auto-detect turns the bisect on and off.
        // From 0, the smelting loop's floor; another loop's value under its own floor is raised to it by SetDelay.
        m_delayBox = lg.Field("Delay (ms)",
            Num(0, AssistFeatures.MaxDelayMs, m_f.Delay(Kind()),
            v => { if (!m_settingDelay && v != m_f.Delay(Kind())) m_f.SetDelay(Kind(), v); }, DelayStep),
            "The wait between one use and the next on this server.");
        m_autoPace = lg.Cell(new CheckBox { Text = "auto-detect", AutoSize = true, Checked = m_f.AutoPace },
            "Bisect the delay on the server's \"you must wait\". Off, a wait line repeats at the same delay, and 3 in "
            + "a row stop the loop.");
        m_autoPace.CheckedChanged += (s, e) => m_f.AutoPace = m_autoPace.Checked;
        // Smelting only (Damian, 2026-10-16: "add option \"whole pile\" and take the loop count from the pile size
        // after the first example run").
        CheckBox whole = lg.Cell(new CheckBox { Text = "whole pile", AutoSize = true, Checked = m_f.WholePile },
            "Smelt the whole pile: after round zero the loop runs once for each ore left in the pile, in place of the "
            + "times box.");
        whole.CheckedChanged += (s, e) => m_f.WholePile = whole.Checked;
        // Tailoring only (Damian, 2026-10-16: "the tailor loop should have a \"or all cloth is used\"").
        CheckBox allCloth = lg.Cell(new CheckBox { Text = "all cloth", AutoSize = true, Checked = m_f.AllCloth },
            "Sew until the cloth and the bolts are used up, in place of the times box.");
        // While ticked the times and until-skill boxes do not apply, and are greyed (Damian, 2026-10-19: "all cloth
        // button should disable times and skill controls when on, renable when switched off").
        Action loopBoxes = () => { lg.Show(whole, Kind().Smelt > 0); lg.Show(allCloth,
            Kind().Tailor > 0); bool all = Kind().Tailor > 0
            && allCloth.Checked; m_rounds.Enabled = m_untilSkill.Enabled = !all; };
        allCloth.CheckedChanged += (s, e) => { m_f.AllCloth = allCloth.Checked; loopBoxes(); };
        m_loops.SelectedIndexChanged += (s, e) => loopBoxes();
        loopBoxes();
        AddAction("loop", Grid(g), "Start or stop the loop", () => ToggleLoop());
        // Damian, 2026-10-20: "add a button for starting all loops using last targets".
        // Disabled, and its key idle, until the selected loop has last targets (Damian, 2026-10-20: "if the loop
        // doesn't have previous targets, the button/key should be disabled until set").
        // Each of the two starts is idle while the other's run goes (Damian, 2026-10-20: "it should be disabled when
        // the configuring one is running and vice versa"); this one stops its own run.
        AddAction("loop.last", Grid(g), "Start the loop on its last targets", () =>
        {
            if (LoopRunning())
            {
                if (m_f.LastRun)
                    m_f.Stop("the last-targets key");
                return;
            }
            if (m_f.WorkRunning || m_f.WatchSelling || !m_f.HasLastTargets(Kind()))
                return;
            string? why = m_f.Start(Kind(), (int)m_rounds.Value, (int)(m_untilSkill.Value * 10), null!, true);
            if (why != null)
                Note(why);
        });
        m_tipper.SetToolTip(m_actionControls["loop.last"]!,
            "Starts the selected loop with no round zero, on the tools, targets and menu picks of its last round zero "
            + "since the relay started.");
        m_loopGroup = g;
        m_loopLight = Add(g, Light());
        m_loopWords = Add(g, new Label { AutoSize = false, Height = 30, Font = new Font(Font, FontStyle.Bold) });
        Grid(g).Cell(TimingsTag(() => TaskTimingRegistry.ForLoop(Kind())));

        // Watch me sell (AssistFeatures.WatchSell): your hand sale, then the rest of that item to the same vendor down
        // to your price less the allowance, in gold or percent.
        g = Group("Watch me sell");
        FieldGrid wg = Grid(g);
        CheckBox watch = wg.Cell(new CheckBox { Text = "Sell the rest after I sell", AutoSize = true,
            Checked = m_f.WatchSell },
            "While ticked (and no circuit runs), sell an item to a vendor by hand once: the assist then sells the "
            + "rest of that item in your pack to the same vendor, 5 at a time, while it pays at least your price less "
            + "the allowance. A vendor pays less as it stocks up, so it stops at the first batch under that. Untick "
            + "to stop it.");
        m_watchBox = watch;
        watch.CheckedChanged += (s, e) => m_f.WatchSell = watch.Checked;
        NumericUpDown watchTol = Num(0, 1000, m_f.WatchTol, v => m_f.WatchTol = v);
        ComboBox watchUnit = Pick(45, "gp", "%");
        watchUnit.SelectedIndex = m_f.WatchPct ? 1 : 0;
        watchUnit.SelectedIndexChanged += (s, e) => m_f.WatchPct = watchUnit.SelectedIndex == 1;
        wg.Field("Down to my price less", Strip(watchTol, watchUnit),
            "The allowance under the price you sold at: gold, or a percent of the price rounded to whole gold and at "
            + "least 1.");
        m_tipper.SetToolTip(watchTol,
            "The allowance under the price you sold at: gold, or a percent of the price rounded to whole gold and at "
            + "least 1.");
        m_watchWords = Add(g, new Label { AutoSize = false, Height = 30 });
        Grid(g).Cell(TimingsTag(() => TaskTimingRegistry.Watch));
        Summarize(g, () => (watch.Checked ? "on" : "off") + (m_f.WatchStatus.Length > 0 ? ": " + m_f.WatchStatus : ""));

        g = Group("All Timings and Delays");
        // One grid for every category, each a fold, so the boxes stand in the same columns from one category to the
        // next.
        FieldGrid tg = Grid(g);
        CheckBox tune = tg.Wide(new CheckBox { Text = "auto-tune measured timings", AutoSize = true,
            Checked = m_f.Timings.AutoTune },
            "A measured timing records how long the server took each time. Auto-tune raises it at once when the "
            + "server is later, and otherwise follows 1.5 x the longest of the last " + TimingCatalog.TuneKeep
            + " plus " + TimingCatalog.TuneSlackMs + " ms, never under half its default.");
        tune.CheckedChanged += (s, e) => m_f.Timings.AutoTune = tune.Checked;
        List<string> cats = new();
        foreach (Timing t in TimingCatalog.All)
            if (!cats.Contains(t.Group))
                cats.Add(t.Group);
        foreach (string cat in cats)
        {
            string shutKey = "collapsed.timings." + cat;
            tg.Fold(cat + " (ms)", m_f.Cfg.Get(shutKey, "1") == "1", shut => m_f.Cfg.Set(shutKey, shut ? "1" : "0"));
            foreach (Timing t in TimingCatalog.All)
                if (t.Group == cat)
                    TimingField(tg, t.Key, t.Name, t.Text);
            if (cat == "Magery")
                foreach (LoopKind c in AssistFeatures.CirclePace)
                    TimingField(tg, "circle:" + c.Name, "Circle " + c.Name.Substring(c.Name.Length - 1),
                        c.Name + (": the wait between casts. Auto-detect in the Loops panel moves it on \"not yet "
                        + "recovered\"."));
        }
        // The timing changes, newest first, and the latest on the collapsed header (Damian, 2026-10-19: "a headline of
        // latest timing adjustment in collapsed expando").
        m_timingLogBox = Add(g, new ListBox { Height = 90, HorizontalScrollbar = true });
        m_tipper.SetToolTip(m_timingLogBox,
            "Every change to a timing or a loop delay, by auto-tune, auto-detect or you, newest first; all of them "
            + "are kept in timing-log-<server>.txt beside the settings.");
        // The last change only, short (Damian, 2026-10-20: "shorten the summary of that timings header to show last
        // change"): "last change 05:14: Ore drop 700 to 900 ms", the why left to the log below.
        Summarize(g, () =>
        {
            ImmutableArray<string> l = m_f.Timings.Lines();
            if (l.Length == 0)
                return "";
            string s = l[l.Length - 1];
            if (s.Length < 20)
                return s;
            string body = s.Substring(21);
            int why = body.IndexOf(" (");
            if (why > 0)
                body = body.Substring(0, why);
            return "last change " + s.Substring(11, 5) + ": " + body.Replace(" ms to ", " to ");
        });

        g = Group("Arm and disarm");
        FieldGrid ag = Grid(g);
        AddAction("arm", ag, "Disarm both / arm",
            () => ThreadPool.QueueUserWorkItem(_ => { string? why = m_f.ArmToggle("both"); if (why
            != null) Note(why); }));
        AddAction("arm.weapon", ag, "Disarm weapon / arm",
            () => ThreadPool.QueueUserWorkItem(_ => { string? why = m_f.ArmToggle("weapon"); if (why
            != null) Note(why); }));
        AddAction("arm.shield", ag, "Disarm shield / arm",
            () => ThreadPool.QueueUserWorkItem(_ => { string? why = m_f.ArmToggle("shield"); if (why
            != null) Note(why); }));
        m_hands = Add(g, new Label { AutoSize = false, Height = 18 });
        Grid(g).Cell(TimingsTag(() => TaskTimingRegistry.Arm));
        Summarize(g, () => m_hands.Text);

        // Item pickup (Damian, 2026-10-15: "for reagent pickup make it item pickup, and add a button to add, that reads
        // in the item data and lets you pick from a list"): the picked item types, kept in the "reagents" setting.
        g = Group("Item pickup");
        m_pickup = new CheckBox { Text = "Pick up these items near you", AutoSize = true, Checked = m_f.Pickup };
        m_pickup.CheckedChanged += (s, e) => m_f.Pickup = m_pickup.Checked;
        FieldGrid ig = Grid(g);
        AddAction("pickup", ig, null!, () => { m_pickup.Checked = !m_pickup.Checked; }, m_pickup);
        CheckBox harvest = ig.Wide(new CheckBox { Text = "Harvest: pluck crops and shear sheep near you, then pick up "
            + "the food and wool", AutoSize = true, Checked = m_f.Harvest });
        harvest.CheckedChanged += (s, e) => m_f.Harvest = harvest.Checked;
        Summarize(g,
            () => "pickup " + (m_pickup.Checked ? "on" : "off") + " (" + m_picks.Items.Count + " items), harvest "
            + (harvest.Checked ? "on" : "off"));
        m_reach = Add(g, new Label { AutoSize = false, Height = 16 });
        // The list with Add and Remove stacked at its right (Damian, 2026-10-19: "flow the add remove buttons ... to
        // the right of the item list box above it, which will be a little thiner. stack the add/remove vertically").
        TableLayoutPanel pickRow = Add(g, new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Height = 72 });
        pickRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pickRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        m_picks = new ListBox { Dock = DockStyle.Fill, Margin = new Padding(0) };
        Button padd = Btn("Add...");
        Button pdel = Btn("Remove");
        FlowLayoutPanel pbtn = new()
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Margin = new Padding(4, 0, 0, 0)
        };
        pbtn.Controls.Add(padd);
        pbtn.Controls.Add(pdel);
        pickRow.Controls.Add(m_picks, 0, 0);
        pickRow.Controls.Add(pbtn, 1, 0);
        padd.Click += (s, e) => { int gr = ItemPicker.Pick(this, "Add an item to pick up", m_f.SeenGraphics(true),
            m_f.SeenGraphics(false)); if (gr
            < 0) return; List<int> l = new(m_f.Reagents()); if (!l.Contains(gr)) { l.Add(gr); m_f.SetReagents(l.ToArray()); } FillPicks(); };
        pdel.Click += (s, e) => { if (m_picks.SelectedIndex
            < 0) return; List<int> l = new(m_f.Reagents()); l.RemoveAt(m_picks.SelectedIndex); m_f.SetReagents(l.ToArray()); FillPicks(); };
        FillPicks();
        Grid(g).Cell(TimingsTag(() => TaskTimingRegistry.Pickup));

        // Mill, spin and weave (Damian, 2026-10-15: "we need a miller feature", "and a tailor for the cott").
        g = Group("Mill, spin and weave");
        m_workGroup = g;
        FieldGrid mw = Grid(g);
        AddAction("work", mw, "Work the station beside me",
            () => { string? w = m_f.WorkUserToggle(); if (w != null) Note("Mill, spin and weave not started: " + w); });
        m_tipper.SetToolTip(m_actionControls["work"]!,
            "Works the pack's wheat, cotton, wool, thread or yarn at the station beside you.");
        CheckBox thenTailor = mw.Cell(new CheckBox { Text = "then tailor", AutoSize = true, Checked = m_f.ThenTailor },
            "When the weave ends by itself, start the tailoring loop that ran last, on its last targets (its kit and "
            + "menu picks, the pack's bolts and cloth), with its times box. A weave you stop does not start it.");
        thenTailor.CheckedChanged += (s, e) => m_f.ThenTailor = thenTailor.Checked;
        m_workState = Add(g, new Label { AutoSize = false, Height = 16 });
        Grid(g).Cell(TimingsTag(() => TaskTimingRegistry.Work));
        Summarize(g, () => m_workState.Text);

        // Record me (Damian, 2026-10-20: "the record me button seems unrelated to mill, spin and weave, and should be
        // moved somewhere ... its own expando with good tool tips for what to do about it, how to use it").
        g = Group("Record me");
        Summarize(g, () => m_f.Recording ? "recording" : "");
        FieldGrid rec = Grid(g);
        AddAction("record", rec, "Record me: start / stop", () => Note(m_f.RecordToggle("the window")));
        m_tipper.SetToolTip(m_actionControls["record"]!,
            "Marks a stretch of your play in the relay log for the agents to study: press to start, play normally, "
            + "press again to stop. Nothing is sent to the server.");
        Label recHow = Add(g,
            new Label { AutoSize = false, Height = 4,
            Text = "Use it when you want the assist to learn something you do by hand, or to show a problem as it "
            + "happens: start, do it, stop, then tell the agent \"look at my recording\".\nIn the client: say [rec to "
            + "start and [stop to stop; while recording, say [ and a note (\"[ this is where it got stuck\") to mark "
            + "a moment. None of these reach the server or other players." });
        m_growers = m_growers.Add(recHow);

        // Bunny hop target to my location (Damian, 2026-10-23).
        g = Group("Bunny hop");
        Summarize(g, () => m_f.HopStatus);
        FieldGrid hop = Grid(g);
        AddAction("hop", hop, "Bunny hop target to my location",
            () => { string? w = m_f.HopToMe(); Note("Bunny hop: " + (w ?? m_f.HopStatus)); });
        m_tipper.SetToolTip(m_actionControls["hop"]!,
            "Stand where the object should end up and press: your tile is kept, then target an object lying on the "
            + "ground. It is carried there by hops, lifted and put down 2 tiles ahead while you stand still, along a "
            + "route planned in full first (one that takes past 30 s to plan is refused). Press again while it runs "
            + "to stop it.");
        AddAction("hop.target", hop, "Bunny hop target to target",
            () => { string? w = m_f.HopItemToTarget(); Note("Bunny hop: " + (w ?? m_f.HopStatus)); });
        m_tipper.SetToolTip(m_actionControls["hop.target"]!,
            "Press, target the object lying on the ground, then target where it goes (a tile, or an object for its "
            + "tile). It is carried there by hops as Bunny hop target to my location does. Press again while it runs "
            + "to stop it.");
        hop.Cell(Btn("Stop", () => Note("Bunny hop: " + (m_f.HopStop() ?? "stopping"))),
            "Stop the running bunny hop: the hop under way finishes, the next does not start.");
    }

    // The delay box follows the loop's delay as the bisect moves it, except while the player is editing it.
    void ShowDelay()
    {
        if (m_delayBox == null || m_delayBox.Focused)
            return;
        int d = Math.Max((int)m_delayBox.Minimum, Math.Min((int)m_delayBox.Maximum, m_f.Delay(Kind())));
        if ((int)m_delayBox.Value != d)
        {
            m_settingDelay = true;
            m_delayBox.Value = d;
            m_settingDelay = false;
        }
    }
    // The Timings panel (Damian, 2026-10-16: "shorter names, tool tips for the full explanation ... 2 colums ... up
    // down nudge should be 100ms ... exando subgroups by functional category"): each action timing on this server,
    // editable, in a collapsible subgroup; the tooltip carries the explanation and what was seen this session. The
    // magery circles' delays are keyed "circle:<loop name>". The columns are now as many as the width holds
    // (FieldGrid). Every timing box on the window, the Timings panel's and the ones under a loop's or a circuit's run
    // status (Damian, 2026-10-19: "for each loop and circuit, under the run status, show all the relevant timings and
    // allow live adjustment during a run"), each kept to its value and its tooltip by ShowTimings. A key is
    // "pace:<loop>" (a loop's delay), "circle:<circle>" (a magery circle's), "pickup:gap" or "pickup:settle" (the
    // adaptive pickup pace), or the key of one of TimingCatalog.All. A loop's delay nudges 50 ms as the Loops panel's
    // delay box does, any other timing 100.
    sealed class TimingBox
    {
        public string Key = null!, Tip = null!; public NumericUpDown Box = null!; public FieldGrid Grid = null!;
    }
    readonly List<TimingBox> m_timingBoxes = new();
    Map<NumericUpDown, string> m_timingTips = Map<NumericUpDown, string>.Empty;
    readonly ToolTip m_tipper = new() { AutoPopDelay = 20000 };
    bool m_settingTiming;
    const int TimingStep = 100, DelayStep = 50;
    static LoopKind? Circle(string key)
    {
        return AssistFeatures.CirclePace.Find(c => c.Name == key.Substring(7));
    }
    int TimingGet(string key)
    {
        if (key.StartsWith("pace:"))
            return m_f.Delay(AssistFeatures.Find(key.Substring(5))!);
        if (key.StartsWith("circle:"))
            return m_f.Delay(Circle(key)!);
        if (key.StartsWith("pickup:"))
            return m_f.PickupPaceMs(key);
        return m_f.Timings.Ms(key);
    }
    void TimingSet(string key, int v)
    {
        if (key.StartsWith("pace:"))
            m_f.SetDelay(AssistFeatures.Find(key.Substring(5))!, v);
        else if (key.StartsWith("circle:"))
            m_f.SetDelay(Circle(key)!, v);
        else if (key.StartsWith("pickup:"))
            m_f.SetPickupPace(key, v);
        else
            m_f.Timings.Set(key, v);
    }
    void TimingField(FieldGrid grid, string key, string name, string tip, Control? beside = null)
    {
        NumericUpDown box = Num(0, TimingCatalog.MaxMs, TimingGet(key),
            v => { if (!m_settingTiming) TimingSet(key, v); }, key.Contains(":") ? DelayStep : TimingStep);
        if (beside == null)
        {
            grid.Field(name, box, tip);
        }
        else
        {
            grid.Field(name, Strip(box, beside), tip);
            m_tipper.SetToolTip(box, tip);
        }
        m_timingBoxes.Add(new TimingBox { Key = key, Tip = tip, Box = box, Grid = grid });
    }
    // A timing key's label, its panel group and its tooltip; false for a key no timing has.
    static bool TimingLabel(string key, out string? name, out string? group, out string? tip)
    {
        name = group = tip = null;
        if (key == null)
            return false;
        if (key.StartsWith("pace:"))
        {
            name = "Pace";
            group = "Pace";
            tip = "The wait between one use and the next (the loop's delay ms); too short and the server answers "
                + "\"You must wait\".";
        }
        else if (key.StartsWith("circle:"))
        {
            name = "Circle " + key.Substring(key.Length - 1);
            group = "Pace";
            tip = key.Substring(7) + ": the wait between casts.";
        }
        else if (key == "pickup:gap")
        {
            name = "Lift gap";
            group = "Pace";
            tip = "From the last pickup lift to the next. It comes down " + AssistFeatures.PickupStepMs
                + " ms after each clean pickup and backs off " + AssistFeatures.PickupBackoffMs
                + " ms on a busy refusal; a value set here is also its floor.";
        }
        else if (key == "pickup:settle")
        {
            name = "Lift settle";
            group = "Pace";
            tip = "A pickup lift counts as accepted after this long with no refusal. It comes down "
                + AssistFeatures.PickupStepMs + " ms after each clean pickup and backs off "
                + AssistFeatures.PickupBackoffMs + " ms on a refusal after it; a value set here is also its floor.";
        }
        else
        {
            Timing? t = TimingCatalog.Of(key);
            if (t == null)
                return false;
            name = t.Name;
            group = t.Measured ? "Server waits" : t.Group;
            tip = t.Text;
        }
        return true;
    }
    // An action's timings in a window of their own (Damian, 2026-10-21: "create a popup window that takes the list of
    // timings, and shows them in a popup, then tag it for every action"): its task's, its skill's and the general ones
    // (TaskTimingRegistry.Group), each with a button listing every action bound to it ("put a button beside each of
    // those timings, that pops a second dialog, that shows all the actions that are bound to that timing").
    Button TimingsTag(Func<TaskTimings> task)
    {
        Button b = Btn("Timings...", () => TimingsPopup(task()));
        m_tipper.SetToolTip(b,
            "This action's timings in a window: its own, those its skill shares, and the general ones every skill "
            + "shares.");
        return b;
    }
    void TimingsPopup(TaskTimings task)
    {
        string skill = task.Skill;
        Form form = new()
        {
            Text = task.Name + ": timings (ms)",
            FormBorderStyle = FormBorderStyle.SizableToolWindow,
            StartPosition = FormStartPosition.Manual,
            Location = Cursor.Position,
            ShowInTaskbar = false,
            AutoScroll = true,
            Width = 560,
            Height = 200
        };
        FieldGrid? grid = null;
        Action lay = () => { grid!.Width = Math.Max(200,
            form.ClientSize.Width - 16
            - SystemInformation.VerticalScrollBarWidth); grid.Height = grid.Arrange(grid.Width); };
        grid = new FieldGrid(m_tipper, () => lay()) { Location = new Point(8, 8) };
        ImmutableArray<TimingRow> rows = TaskTimingRegistry.Group(task);
        foreach (string group in new[] { "Task", "Skill", "General" })
        {
            List<TimingRow> mine = new();
            foreach (TimingRow r in rows)
                if (r.Group == group)
                    mine.Add(r);
            if (mine.Count == 0)
                continue;
            grid.Head(group == "Task" ? "This task's own"
                : group == "Skill" ? "Shared within " + skill : "Shared by every skill");
            foreach (TimingRow r in mine)
            {
                string? name, g0, tip, key = r.Key;
                if (!TimingLabel(key, out name, out g0, out tip))
                    continue;
                if (r.Others.Length > 0)
                    tip += "\nAlso used by: " + r.Others + ".";
                string? shown = name;
                Button used = Btn("Used by", () => UsedByPopup(key, shown!));
                m_tipper.SetToolTip(used, "Every action bound to this timing.");
                TimingField(grid, key, name!, tip!, used);
            }
        }
        form.Controls.Add(grid);
        lay();
        form.ClientSize = new Size(form.ClientSize.Width, Math.Min(700, grid.Height + 16));
        form.Resize += (s, e) => lay();
        form.FormClosed += (s, e) => { foreach (TimingBox t in m_timingBoxes) if (t.Grid
            == grid) m_timingTips = m_timingTips.Remove(t.Box); m_timingBoxes.RemoveAll(t => t.Grid == grid); };
        OnScreen(form);
        form.Show(this);
    }
    // A popup moved up and left until its bottom right corner is inside the working area of the screen it opens on, its
    // top left kept on it too (Damian, 2026-10-24: "the Timings... popup need to not render off the bottom or right of
    // the screen").
    static void OnScreen(Form form)
    {
        Rectangle area = Screen.FromPoint(form.Location).WorkingArea;
        int x = Math.Max(area.Left, Math.Min(form.Left, area.Right - form.Width)), y = Math.Max(area.Top,
            Math.Min(form.Top, area.Bottom - form.Height));
        if (x != form.Left || y != form.Top)
            form.Location = new Point(x, y);
    }
    void UsedByPopup(string key, string name)
    {
        ListBox list = new()
        {
            Dock = DockStyle.Fill,
            IntegralHeight = false
        };
        foreach (string u in TaskTimingRegistry.UsersOf(key))
            list.Items.Add(u);
        if (list.Items.Count == 0)
            list.Items.Add("No action lists this timing.");
        Form form = new()
        {
            Text = name + ": bound actions",
            FormBorderStyle = FormBorderStyle.SizableToolWindow,
            StartPosition = FormStartPosition.Manual,
            Location = Cursor.Position,
            ShowInTaskbar = false,
            Width = 380,
            Height = Math.Min(500, 60 + list.ItemHeight * list.Items.Count)
        };
        form.Controls.Add(list);
        OnScreen(form);
        form.Show(this);
    }

    void ShowTimings()
    {
        foreach (TimingBox r in m_timingBoxes)
        {
            if (!r.Box.Focused)
            {
                int v = Math.Max((int)r.Box.Minimum, Math.Min((int)r.Box.Maximum, TimingGet(r.Key)));
                if ((int)r.Box.Value != v)
                {
                    m_settingTiming = true;
                    r.Box.Value = v;
                    m_settingTiming = false;
                }
            }
            Timing? t = r.Key.Contains(":") ? null : TimingCatalog.Of(r.Key);
            string tip = t == null ? r.Tip
                : r.Tip + "\n" + m_f.Timings.SeenText(r.Key) + ". Default " + t.Default + " ms.";
            string? was;
            if (!m_timingTips.TryGet(r.Box, out was) || was != tip)
            {
                m_timingTips = m_timingTips.Set(r.Box, tip);
                m_tipper.SetToolTip(r.Box, tip);
                Control? cap = r.Grid.CaptionOf(r.Box);
                if (cap != null)
                    m_tipper.SetToolTip(cap, tip);
            }
        }
        if (m_timingLogBox != null)
        {
            ImmutableArray<string> lines = m_f.Timings.Lines();
            string shape = lines.Length + "|" + (lines.Length > 0 ? lines[lines.Length - 1] : "");
            if (shape != m_timingLogShape)
            {
                m_timingLogShape = shape;
                m_timingLogBox.BeginUpdate();
                m_timingLogBox.Items.Clear();
                for (int i = lines.Length - 1; i >= 0; i--)
                    m_timingLogBox.Items.Add(lines[i]);
                m_timingLogBox.EndUpdate();
            }
        }
    }
    ListBox m_timingLogBox = null!; string m_timingLogShape = "";


    LoopKind Kind()
    {
        return AssistFeatures.Loops[Math.Max(0, m_loops.SelectedIndex)];
    }
    // A loop's one-word name: its first word, lower case ("Mining (2 tools)" mining, "Magery circle 3" magery),
    // Blacksmithing as smithing.
    static string LoopWord(string name)
    {
        string w = name.Trim().Split(' ', '(', ':')[0].ToLowerInvariant();
        return w == "blacksmithing" ? "smithing" : w.Length > 0 ? w : "selected";
    }
    void ToggleLoop()
    {
        if (LoopRunning() && m_f.LastRun)
            return;
        if (m_f.Status.StartsWith("Loop ") && !m_f.Status.Contains(" done") && !m_f.Status.Contains(" stopped"))
        {
            m_f.Stop("the loop key");
            return;
        }
        // The weave is on the Loops bar, so the loop key stops it too.
        if (m_f.WorkRunning && !m_f.CircuitRunning)
        {
            m_f.WorkUserToggle();
            return;
        }
        if (m_f.WatchSelling)
        {
            m_f.StopWatchSelling();
            return;
        }
        string? why = m_f.Start(Kind(), (int)m_rounds.Value, (int)(m_untilSkill.Value * 10), null!);
        if (why != null)
            Note(why);
    }

    void FillPicks()
    {
        m_picks.Items.Clear();
        foreach (int gr in m_f.Reagents())
            m_picks.Items.Add((AssistFeatures.ReagentName(gr)
                ?? (UoArt.Folder != null ? UoTiles.Name(gr) : gr.ToString("X4"))) + "  (" + gr.ToString("X4") + ")");
    }
}
