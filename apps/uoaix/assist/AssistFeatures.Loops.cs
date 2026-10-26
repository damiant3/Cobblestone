#nullable enable
using System.Collections.Immutable;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Uoaix.Assist;

public sealed partial class AssistFeatures
{
    // ---------- sounds ----------
    // The sound filter (Damian, 2026-10-16: "add a tab for supressing sounds like uoassist, include pack sounds"): a
    // server sound (0x54: flags, sound id, volume, x, y, z) in a checked group is kept from the client. Ids are the
    // 1.25 SOUNDIDX entries, named by the file names SOUND.MUL holds; 0x48 leather1 and 0x57 usecloth, the pack's, were
    // the two sounds uo1998 sent most (relay logs 2026-10-16, about 480 each).
    public sealed class SoundGroup
    {
        public string Name = null!; public int[] Ids = null!;
    }
    static int[] Ids(params int[] pairs)
    {
        List<int> l = new();
        for (int i = 0; i + 1 < pairs.Length; i += 2)
            for (int id = pairs[i]; id <= pairs[i + 1]; id++)
                l.Add(id);
        return l.ToArray();
    }
    static SoundGroup S(string name, params int[] pairs)
    {
        return new SoundGroup { Name = name, Ids = Ids(pairs) };
    }
    static readonly ImmutableArray<SoundGroup> s_soundGroups = [
        S("Pack and bags", 0x48, 0x48, 0x57, 0x59),
        S("Coins and gems", 0x32, 0x37),
        S("Chests and drawers", 0x2C, 0x2F),
        S("Doors and gates", 0xEA, 0xF4),
        S("Bard music", 0x38, 0x39, 0x3D, 0x3D, 0x43, 0x46, 0x4C, 0x4D, 0x52, 0x53),
        S("Anvil and hammer", 0x2A, 0x2B, 0x42, 0x42),
        S("Crafting tools", 0x23D, 0x23E, 0x241, 0x242, 0x248, 0x249),
        S("Spirit Speak", 0x24A, 0x24A),
        S("Spells", 0x1DF, 0x217),
        S("Footsteps", 0x11F, 0x134, 0x24B, 0x24C),
        S("Swings and hits", 0x135, 0x14A, 0x232, 0x23C),
        S("Grunts and death cries", 0x14B, 0x15D),
        S("Dogs and wolves", 0x85, 0x89, 0xE5, 0xE9),
        S("Cats", 0x69, 0x6D),
        S("Horses and llamas", 0xA8, 0xAC, 0xB7, 0xB9),
        S("Sheep, goats and pigs", 0xD6, 0xDA, 0x99, 0x9D, 0xC4, 0xC8),
        S("Cows and bulls", 0x64, 0x68, 0x78, 0x7C),
        S("Chickens and birds", 0x6E, 0x72, 0x17, 0x1F, 0x7D, 0x81, 0x8F, 0x98, 0xAD, 0xB1, 0xBF, 0xC3, 0xD1, 0xD5,
        0x270, 0x279),
        S("Dragons", 0x16A, 0x16E),
        S("Weather, water and wind", 0x00, 0x16, 0x20, 0x29),
    ];
    public static ImmutableArray<SoundGroup> SoundGroups => s_soundGroups;
    public bool SoundOff(string group)
    {
        return Cfg.Get("sound.off." + group, "0") == "1";
    }
    public void SetSoundOff(string group, bool off)
    {
        Cfg.Set("sound.off." + group, off ? "1" : "0");
        m_muted = null;
    }
    volatile Set<int>? m_muted = null;
    ValueMap<int, int> m_heard = ValueMap<int, int>.Empty; readonly Lock m_heardGate = new();
    bool HoldSound(byte[] p)
    {
        if (p[0] != 0x54 || p.Length < 4)
            return false;
        int id = (p[2] << 8) | p[3];
        lock (m_heardGate)
            m_heard = m_heard.Set(id, m_heard.Get(id, 0) + 1);
        Set<int>? m = m_muted;
        if (m == null)
        {
            m = Set<int>.Empty;
            foreach (SoundGroup g in s_soundGroups)
                if (SoundOff(g.Name))
                    foreach (int s in g.Ids)
                        m = m.Add(s);
            m_muted = m;
        }
        return m.Contains(id);
    }
    // For the Sounds tab: how often this session the server played a group's sounds.
    public int Heard(SoundGroup g)
    {
        int n = 0;
        ValueMap<int, int> heard = m_heard;
        foreach (int id in g.Ids)
            n += heard.Get(id, 0);
        return n;
    }

    // ---------- waiting, pacing ----------
    // The hands (Hands.cs) under the names the sections call until each task moves (AssistStructure.md stage 3).
    bool Answered(GameLink k, int ms)
    {
        return m_hands.Answered(k, ms);
    }
    string? Lifted(GameLink k, uint serial, int amount, int settleMs = -1)
    {
        return m_hands.Lift(k, serial, amount, settleMs).AsReason();
    }
    public const int LateRefusalMs = Hands.LateRefusalMs;
    DateTime liftSentAt => m_hands.LiftSentAt;
    byte[]? lastRefusal => m_hands.LastRefusal;
    static AssistItem? Item(GameLink k, uint s)
    {
        return Inv(k).Item(s);
    }
    static AssistItem? ItemOrGround(GameLink k, uint s)
    {
        return Inv(k).ItemOrGround(s);
    }
    static AssistItem? Hand(GameLink k, int layer)
    {
        lock (k.State.Gate)
        {
            foreach (KeyValuePair<uint, AssistItem> kv in k.State.Items)
                if (kv.Value.Layer == layer && kv.Value.Mobile == k.State.Player)
                    return kv.Value;
        }
        return null;
    }
    // The hands emptied into the pack before a cast: uo1998 refuses a spell while layer 1 or 2 holds anything but a
    // spellbook ("Your hands must be free to cast spells.", rundir spelskil.m Q49Q; the log trip's recall with the axe
    // in hand, 2026-10-22 21:54:31). The logger takes its axe back at the next chop (LumberEquip). Null, or why not.
    string? FreeHands(GameLink k)
    {
        uint me, pack;
        lock (k.State.Gate)
        {
            me = k.State.Player;
            pack = k.State.Backpack;
        }
        List<uint> held = new();
        lock (k.State.Gate)
            foreach (AssistItem it in k.State.Items.Values)
                if ((it.Layer == 1 || it.Layer == 2) && it.Mobile == me && it.Graphic != SpellbookArt)
                    held.Add(it.Serial);
        if (held.Count == 0)
            return null;
        using (Act(k))
            foreach (uint h in held)
            {
                string? w = MoveStack(k, h, 1, "the held item " + h.ToString("X"),
                    "Putting the held item away before the cast", () => k.Drop(h, pack)!);
                if (w != null)
                    return w;
            }
        foreach (uint h in held)
            if (!Wait.Until(Clock, () => { AssistItem? a = Item(k, h); return a == null || a.Layer < 0; }, AnswerMs,
                "the held item in the pack", stepMs: 50).Done)
                return "the held item " + h.ToString("X") + " stayed in your hand";
        return null;
    }

    // ---------- canned loops ----------
    public string Menu()
    {
        StringBuilder sb = new();
        foreach (LoopKind l in s_loops)
            sb.Append("Loop " + l.Name + " (" + Delay(l) + " ms; "
                + (l.Tailor > 0
                ? "round zero: double-click the sewing kit, target the bolts or the cloth and pick from the menus"
                : l.Smelt > 0
                ? ("round zero: lift one ore from a pile on the ground, drop it in a bag in your pack, double-click it "
                + "and pick the forge")
                : l.Pairs > 0 ? "round zero: double-click each tool and pick its target"
                : l.Target == 1 ? "round zero: pick the target" : "nothing to pick") + ") | ");
        return sb.ToString().TrimEnd(' ', '|');
    }
    // Per login server: pace.<loop> the delay in use, pace.lo.<loop> the longest the server refused, pace.hi.<loop> the
    // shortest that ran ProbeAfterRounds rounds clean (0: none yet).
    public int Delay(LoopKind l)
    {
        return Cfg.GetInt(Server + "pace." + l.Name, l.DelayMs);
    }
    int Bound(string which, LoopKind l)
    {
        return Cfg.GetInt(Server + "pace." + which + "." + l.Name, 0);
    }
    void SetPace(LoopKind l, int delay, int lo, int hi, string why)
    {
        int was = Delay(l);
        Cfg.Set(Server + "pace.lo." + l.Name, lo.ToString());
        Cfg.Set(Server + "pace.hi." + l.Name, hi.ToString());
        Cfg.Set(Server + "pace." + l.Name, delay.ToString());
        Timings.Log(l.Name + " delay", was, delay, why);
    }
    static int Mid(int lo, int hi)
    {
        return ((lo + hi) / 2 + 49) / 50 * 50;
    }
    // A wait line at the delay in use: that delay is refused; the next is halfway to the shortest that worked, or
    // double. Null to repeat the step at the new delay, or why the loop stops.
    // Auto-detect (Damian, 2026-10-15: "put the actual delays in there that can be shown and overridden, with a button
    // to turn on or off auto-detect by bisect feature"): off, a wait line repeats the step at the same delay, and
    // MaxRefusedInARow refusals in a row stop the loop.
    public bool AutoPace
    {
        get => Cfg.Get(Server + "pace.auto", "on") == "on";
        set => Cfg.Set(Server + "pace.auto", value ? "on" : "off");
    }
    public const int MaxRefusedInARow = 3;
    int m_refusedInARow;
    // A delay the player set: kept for this server, and the bisect's bounds are forgotten. The smelting loop's goes to
    // 0 (Damian, 2026-10-25: "allow the delay for the smelt one ingot loop to go to zero"); every other's to
    // MinDelayMs / 2.
    public static int DelayFloor(LoopKind l)
    {
        return l.Smelt > 0 ? 0 : MinDelayMs / 2;
    }
    public void SetDelay(LoopKind l, int ms)
    {
        SetPace(l, Math.Max(DelayFloor(l), Math.Min(MaxDelayMs, ms)), 0, 0, "set by you");
    }
    string? Refused(LoopKind l)
    {
        if (!AutoPace)
        {
            if (++m_refusedInARow >= MaxRefusedInARow)
                return "the server refused the loop " + m_refusedInARow + " times in a row at " + Delay(l)
                    + " ms (auto-detect is off)";
            Note("Loop " + l.Name + ": refused at " + Delay(l)
                + " ms; auto-detect is off, repeating at the same delay");
            return null;
        }
        int d = Delay(l), lo = Math.Max(Bound("lo", l), d), hi = Bound("hi", l);
        if (hi <= lo)
            hi = 0;
        // A refusal at 0 doubles from MinDelayMs / 2: doubled, 0 stays 0.
        int next = hi > 0 ? Mid(lo, hi) : (Math.Max(d, MinDelayMs / 4) * 2 + 49) / 50 * 50;
        if (next > MaxDelayMs)
            return "the server refused the loop at " + d + " ms; " + MaxDelayMs + " ms is the most the loop waits";
        SetPace(l, next, lo, hi, "auto-detect: refused at " + d + " ms");
        Note("Loop " + l.Name + ": refused at " + d + " ms; trying " + next + " ms (refused " + lo + ", worked "
            + (hi > 0 ? hi + " ms" : "none yet") + ") on " + Server.TrimEnd('.'));
        return null;
    }
    // ProbeAfterRounds clean rounds at the delay in use: it works; try halfway to the longest refused, when one is
    // known and the two are more than SettleMs apart. With none refused yet there is one point, and no halfway (Damian,
    // 2026-10-16: "bisect makes sense only when you have 2 points ... if the start is 5seconds, maybe try 4 and if that
    // works 3, if that fails 3.5"): the next try is a fifth shorter, in GuessStepMs steps, never under MinDelayMs.
    public const int GuessStepMs = 500;
    static int Guess(int d)
    {
        int step = Math.Max(GuessStepMs, (d / 5 + GuessStepMs / 2) / GuessStepMs * GuessStepMs);
        return Math.Max(MinDelayMs, d - step);
    }
    void Worked(LoopKind l)
    {
        if (!AutoPace)
            return;
        int d = Delay(l), lo = Bound("lo", l), hi = Bound("hi", l);
        if (hi == 0 || d < hi)
            hi = d;
        int next = d;
        if (lo > 0 && hi - lo > SettleMs)
            next = Mid(lo, hi);
        else if (lo == 0 && Guess(hi) < hi)
            next = Guess(hi);
        SetPace(l, next, lo, hi, "auto-detect: " + d + " ms worked");
        if (next != d)
            Note("Loop " + l.Name + ": " + d + " ms worked; trying " + next + " ms on " + Server.TrimEnd('.'));
    }
    public static LoopKind? Find(string name)
    {
        foreach (LoopKind l in s_loops)
            if (string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)
                || string.Equals("Loop " + l.Name, name, StringComparison.OrdinalIgnoreCase))
                return l;
        return null;
    }

    // A running loop's terminating settings as the window shows them now (Damian, 2026-10-18: "for all the controls in
    // the loops with an iteration count or other terminating condition, be sure to check the current ui setting, not
    // load the job at the beginning with the configs and then fly"): the window sets these when its times box or
    // until-skill box changes, and every loop reads them at each round's test; unset (0, -1), the values the loop
    // started with stand.
    public volatile int LiveRounds, LiveUntil = -1;
    int RoundsNow(int current)
    {
        int r = LiveRounds;
        return r > 0 ? r : current;
    }
    int UntilNow(int current)
    {
        int u = LiveUntil;
        return u >= 0 ? u : current;
    }
    // The last targets (Damian, 2026-10-20: "add a button for starting all loops using last targets"): each loop's
    // round zero as it ended, by loop name, for the relay's life. A start with them skips round zero and runs round 1
    // on them.
    sealed class LastTargets
    {
        public List<Step> Steps = null!;
        public uint Tool, Bag, Material;
        public List<byte[]> Picks = null!;
        public string? Item = null;
        public int Cost, Until;
        public AssistItem Pile = null!; public Step Forge = null!; public PickedCast Cast = null!;
    }
    Map<string, LastTargets> m_lastTargets = Map<string, LastTargets>.Empty; readonly Lock m_lastTargetsGate = new();
    volatile LoopKind m_lastTailorKind = null!;
    void Keep(LoopKind l, LastTargets t)
    {
        lock (m_lastTargetsGate)
            m_lastTargets = m_lastTargets.Set(l.Name, t);
        if (l.Tailor > 0)
            m_lastTailorKind = l;
    }
    // A loop with nothing to pick in round zero starts the same either way.
    static bool PicksTargets(LoopKind l)
    {
        return l.Magery == 2 || l.Smith > 0 || l.Smelt > 0 || l.Tailor > 0
            || l.Magery == 0 && (l.Pairs > 0 || l.Target == 1);
    }
    // The running loop was started on its last targets: its own button stops it, and the round-zero button is idle.
    public volatile bool LastRun;
    public bool HasLastTargets(LoopKind l)
    {
        if (!PicksTargets(l))
            return true;
        return m_lastTargets.ContainsKey(l.Name);
    }
    public string? Start(LoopKind l, int rounds, int untilSkill, string untilLine, bool last = false)
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "no game connection";
        if (m_loops.Running)
            return "a loop is running: " + Status;
        if (m_circuitThread != null && m_circuitThread.IsAlive)
            return "a circuit is running: " + CircuitName;
        if (WorkRunning && !m_weaveRunner.Runs(Thread.CurrentThread))
            return "Mill, spin and weave is running";
        LastTargets? lt = null;
        if (last && PicksTargets(l))
        {
            m_lastTargets.TryGet(l.Name, out lt);
            if (lt == null)
                return "no last targets for " + l.Name + " yet: run it once with round zero";
        }
        if (rounds <= 0)
            rounds = Cfg.GetInt("rounds." + l.Name, 10);
        Cfg.Set("rounds." + l.Name, rounds.ToString());
        idleStop = false;
        LiveRounds = 0;
        LiveUntil = -1;
        // An until-skill at or below the skill now would end the loop after round zero (Tailoring 95.2 against 70.0,
        // 2026-10-19 12:33:42): it is dropped and the times box stands.
        int trains = l.Magery > 0 ? MagerySkill
            : l.Smelt > 0 ? MiningSkill : l.Skill >= 0 ? l.Skill : l.Pairs > 0 ? MiningSkill : -1;
        if (untilSkill > 0 && trains >= 0 && SkillOf(k, trains) >= untilSkill)
        {
            Note("Loop " + l.Name + ": until " + (untilSkill / 10.0).ToString("0.0") + " is not above your "
                + SkillName(trains) + " " + (SkillOf(k, trains) / 10.0).ToString("0.0") + "; the loop runs its "
                + rounds + " times");
            untilSkill = 0;
        }
        LastRun = last;
        return m_loops.Start(new LoopTask(this, k, l, rounds, untilSkill, untilLine, lt!));
    }
    public void Stop(string why)
    {
        circuitStop = true;
        if (m_loops.Stop())
        {
            idleStop = true;
            Note("Loop stopping: " + why);
        }
    }
    // A loop as a task (AssistStructure.md stage 3): its body RunLoop, its stop the loops' runner's.
    sealed class LoopTask : ITask
    {
        readonly AssistFeatures m_f;
        readonly GameLink m_k;
        readonly LoopKind m_l;
        readonly int m_rounds, m_untilSkill; readonly string m_untilLine; readonly LastTargets m_last;
        public LoopTask(AssistFeatures f, GameLink k, LoopKind l, int rounds, int untilSkill, string untilLine,
            LastTargets last)
        {
            m_f = f;
            m_k = k;
            m_l = l;
            m_rounds = rounds;
            m_untilSkill = untilSkill;
            m_untilLine = untilLine;
            m_last = last;
        }
        public string Name => "Loop " + m_l.Name;
        public TaskTimings Timings => TaskTimingRegistry.ForLoop(m_l);
        public string Run(TaskContext context)
        {
            m_f.RunLoop(m_k, m_l, m_rounds, m_untilSkill, m_untilLine, m_last);
            return m_f.Status;
        }
    }
    // The circuit's own stop: the loop key and "stop" set it; each circuit command clears it as it starts (stop stays
    // set while no loop runs, so the circuit cannot use it).
    public volatile bool circuitStop;

    // Home: the bag a tool step's tool was in at round zero, where a replacement is looked for.
    sealed class Step
    {
        public bool Use, Skill, Target, Ground;
        public uint Serial, Home; public int X, Y, Z, Graphic, Id; public string Words = null!; public Step Copy()
        {
            return (Step)MemberwiseClone();
        }
    }
    // A broken tool (Damian, 2026-10-16: "should detect when you break the shovel or pickaxe, and then find one in the
    // same bag that isn't the other one and use it"): a tool step whose tool is gone takes another digging tool from
    // its home bag, never one another step uses. Digging tools: shovels only (0F39, 0F3A of the four rundir mining.m
    // objects; the pickaxes are 0E85, 0E86), never a pickaxe (Damian, 2026-10-22: "pickaxes are bad, waste of money,
    // heavy. should never use them").
    public static bool DigTool(int graphic)
    {
        return graphic == 0x0F39 || graphic == 0x0F3A;
    }
    // A tool that opened no cursor while still in its bag is defective (Damian, 2026-10-16: "does it still exist in the
    // bag, if so, note it as defective and not use it and pick another and try again. only after that should we stop
    // the loop"); it is never picked again this session.
    Set<uint> m_defectiveTools = Set<uint>.Empty;
    string? ReplaceTool(GameLink k, List<Step> steps, Step s, bool defective = false)
    {
        if (s.Home == 0)
            return null;
        bool gone = Item(k, s.Serial) == null;
        if (!gone && !defective)
            return null;
        if (defective && !gone)
            m_defectiveTools = m_defectiveTools.Add(s.Serial);
        Set<uint> taken = Set<uint>.Empty;
        foreach (Step o in steps)
            if (o.Use)
                taken = taken.Add(o.Serial);
        AssistItem? pick = null;
        lock (k.State.Gate)
            foreach (AssistItem it in k.State.Items.Values)
                if (it.Container == s.Home && !taken.Contains(it.Serial) && !m_defectiveTools.Contains(it.Serial)
                    && DigTool(it.Graphic) && (pick == null || it.Serial < pick.Serial))
                    pick = it;
        string what = gone ? "broke" : "opened no target cursor and is set aside as defective";
        if (pick == null)
            return "the tool " + s.Serial.ToString("X") + " " + what + ", and its bag " + s.Home.ToString("X")
                + " holds no other usable shovel";
        Note("Loop: the tool " + s.Serial.ToString("X") + " " + what + "; using the " + UoTiles.Name(pick.Graphic)
            + " " + pick.Serial.ToString("X") + " from the same bag");
        s.Serial = pick.Serial;
        s.Words = "use item " + pick.Graphic.ToString("X4") + " " + pick.Serial.ToString("X");
        return null;
    }

    // Why a loop ends before its next round or step: F2, the connection closed or the relay halted; null to go on.
    string? RoundStop(GameLink k)
    {
        return stop ? "stopped"
            : k.Closed ? "stopped: the connection closed" : k.Halted != null ? "stopped: " + k.Halted : null;
    }
    // A loop's run record from its first round, and its end: the status line with its tally, and the run kept.
    void BeginRun(GameLink k, LoopKind l, int skill)
    {
        m_currentRun = new LoopRun { Name = l.Name, Skill = skill, Start = Clock.UtcNow, From = SkillOf(k, skill) };
    }
    void EndRun(GameLink k, LoopKind l, string why, string tally = "")
    {
        s_pacing = false;
        Status = "Loop " + l.Name + " " + why + tally;
        Note(Status);
        LoopRun? run = m_currentRun;
        m_currentRun = null;
        if (run != null)
        {
            run.End = Clock.UtcNow;
            run.To = SkillOf(k, run.Skill);
            lock (m_runs)
                m_runs.Add(run);
        }
    }
    void RunLoop(GameLink k, LoopKind l, int rounds, int untilSkill, string untilLine, LastTargets last)
    {
        s_busyTask = l.Smith > 0 ? "smith"
            : l.Magery > 0 ? "magery" : l.Smelt > 0 ? "smelt" : l.Tailor > 0 ? "sew" : l.Pairs > 0 ? "mine" : null;
        if (l.Smith > 0)
        {
            RunSmith(k, l, rounds, untilSkill, last);
            return;
        }
        if (l.Magery > 0)
        {
            RunMagery(k, l, rounds, untilSkill, last);
            return;
        }
        if (l.Smelt > 0)
        {
            RunSmelt(k, l, rounds, untilSkill, last);
            return;
        }
        if (l.Tailor > 0)
        {
            RunTailor(k, l, rounds, untilSkill, last);
            return;
        }
        string why = "done: " + rounds + " rounds";
        try
        {
            List<Step> steps = new();
            // A skill loop uses the skill at once (Damian, 2026-10-15: "for skill loops, go ahead and invoke the skill.
            // only need to gather user input when there is a variable"), unless the player's own last skill use is
            // within the delay ("be sure to wait the skill-appropriate default timeout before starting"): that use is
            // the last use.
            m_lastUse = m_lastOk = l.Pairs > 0 ? Clock.UtcNow : (m_ownSkillAt > m_lastUse ? m_ownSkillAt : m_lastUse);
            Status = "Loop " + l.Name + ": round zero";
            if (last != null)
            {
                foreach (Step s in last.Steps)
                    steps.Add(s.Copy());
                if (l.Pairs > 0)
                    lock (m_pendingDigs)
                        m_pendingDigs.Clear();
            }
            else if (l.Pairs > 0)
            {
                for (int i = 1; i <= l.Pairs; i++)
                {
                    Status = "Loop " + l.Name + ": round zero: double-click tool " + i;
                    byte[]? use = Watch(k, 0x06, 120000);
                    if (use == null)
                    {
                        why = "stopped: no tool was double-clicked";
                        return;
                    }
                    uint s = U32(use, 1);
                    AssistItem? it = Item(k, s);
                    m_lastUse = m_lastOk = Clock.UtcNow;
                    steps.Add(new Step { Use = true, Serial = s,
                        Home = it != null && DigTool(it.Graphic) ? it.Container : 0,
                        Words = "use " + (it != null ? "item " + it.Graphic.ToString("X4") : "") + " "
                        + s.ToString("X") });
                    Status = "Loop " + l.Name + ": round zero: pick the target for tool " + i;
                    byte[]? t = Watch(k, 0x6C, 120000);
                    if (t == null)
                    {
                        why = "stopped: no target was picked";
                        return;
                    }
                    steps.Add(TargetStep(t));
                    m_lastUse = m_lastOk = Clock.UtcNow;
                    if (i == 1)
                        lock (m_pendingDigs)
                            m_pendingDigs.Clear();
                    if (l.Name.StartsWith("Mining"))
                        lock (m_pendingDigs)
                            m_pendingDigs.Enqueue(Clock.UtcNow);
                }
            }
            else
            {
                Step first = new()
                {
                    Skill = true,
                    Id = l.Skill,
                    Words = "use " + l.Name
                };
                steps.Add(first);
                if (l.Target == 1)
                {
                    Status = "Loop " + l.Name + ": round zero: waiting " + Delay(l) + " ms before the first use";
                    DateTime due = m_lastUse.AddMilliseconds(Delay(l));
                    Outcome waited = Wait.Till(Clock, due, () => stop, () => k.Closed);
                    if (!waited.Done)
                    {
                        why = waited.Ending == Ending.Stopped ? "stopped" : "stopped: the connection closed";
                        return;
                    }
                    Status = "Loop " + l.Name + ": round zero: pick the target";
                    string? w;
                    using (Act(k))
                    {
                        w = k.Skill(l.Skill, false);
                    }
                    if (w != null)
                    {
                        why = "stopped: " + w;
                        return;
                    }
                    byte[]? t = Watch(k, 0x6C, 120000);
                    if (t == null)
                    {
                        why = "stopped: no target was picked";
                        return;
                    }
                    steps.Add(TargetStep(t));
                    m_lastUse = Clock.UtcNow;
                }
            }
            if (last == null)
                Keep(l, new LastTargets { Steps = steps.ConvertAll(s => s.Copy()), Until = untilSkill });
            StringBuilder words = new();
            foreach (Step s in steps)
                words.Append(s.Words
                    + (s.Use || s.Skill ? ", wait for the server's answer and " + Delay(l) + " ms from the last use"
                    : ", wait for the server's answer") + "; ");
            LoopWords = "Loop " + l.Name + ": " + (last != null ? "the last targets, no round zero: " : "")
                + words.ToString().TrimEnd(' ', ';') + "; " + rounds + " times"
                + (untilSkill > 0 ? ", or until the skill reaches " + (untilSkill / 10.0).ToString("0.0") : "")
                + (untilLine != null ? ", or until \"" + untilLine + "\"" : "") + ", or F2";
            Note(LoopWords);
            s_pacing = true;
            int clean = 0;
            m_refusedInARow = 0;
            int gainSkill = l.Skill >= 0 ? l.Skill : l.Pairs > 0 ? MiningSkill : -1;
            BeginRun(k, l, gainSkill);
            for (int round = 1; round <= (rounds = RoundsNow(rounds))
                && (untilSkill = UntilNow(untilSkill)) > int.MinValue; round++)
            {
                m_refusedThisRound = false;
                for (int i = 0; i < steps.Count; i++)
                {
                    string? end = RoundStop(k);
                    if (end != null)
                    {
                        why = end;
                        return;
                    }
                    Status = "Loop " + l.Name + ": round " + round + " of " + rounds + ": " + steps[i].Words;
                    string? stopped = Do(k, l, steps, i);
                    if (stopped == "again")
                    {
                        i--;
                        continue;
                    }
                    if (stopped != null)
                    {
                        why = "stopped: " + stopped;
                        return;
                    }
                    string? until = untilLine != null ? m_markJournal?.Find(untilLine) : null;
                    if (until != null)
                    {
                        why = "done: \"" + until + "\"";
                        return;
                    }
                    if (untilSkill > 0 && l.Skill >= 0)
                    {
                        int v;
                        lock (k.State.Gate)
                            k.State.Skills.TryGet(l.Skill + 1, out v);
                        if (v >= untilSkill)
                        {
                            why = "done: the skill reached " + (v / 10.0).ToString("0.0");
                            return;
                        }
                    }
                }
                if (m_currentRun != null)
                    m_currentRun.Rounds = round;
                if (!m_refusedThisRound)
                    m_refusedInARow = 0;
                clean = m_refusedThisRound ? 0 : clean + 1;
                if (clean >= ProbeAfterRounds)
                {
                    Worked(l);
                    clean = 0;
                }
            }
        }
        catch (Exception e) { why = "stopped: " + e.Message; }
        finally { EndRun(k, l, why); }
    }

    DateTime m_lastUse = DateTime.MinValue, m_ownSkillAt = DateTime.MinValue;
    // What the server said since the loop's last use or skill; null before the first.
    Heard m_markJournal = null!;

    // One step. Null when it went as it should, "again" to repeat it after a corrected delay, or why the loop stops.
    string? Do(GameLink k, LoopKind l, List<Step> steps, int i)
    {
        Step s = steps[i];
        bool targetNext = i + 1 < steps.Count && steps[i + 1].Target;
        if (s.Use)
        {
            string? broke = ReplaceTool(k, steps, s);
            if (broke != null)
                return broke;
        }
        if (s.Use && l.Name.StartsWith("Mining") && PackNearFull(k))
        {
            // A dig still out lands its ore in the pack first: nothing is sent until every pending dig has answered,
            // then the whole pile (that dig's ore with it) goes down, and the next use follows on the loop's delay.
            while (PendingDigs() > 0)
            {
                if (stop || k.Closed)
                    return stop ? "stopped" : "the connection closed";
                Clock.Sleep(50);
            }
            if (PackNearFull(k))
                using (Act(k))
                {
                    string? full = DumpOre(k);
                    if (full != null && full != NoOre)
                        return full;
                }
        }
        if (s.Use || s.Skill)
        {
            // Rule 3: the later of the server's answer to the last action and the step's delay after the last use. The
            // wait holds no lock, so drinking, arming and pickup act in it.
            DateTime due = m_lastUse.AddMilliseconds(Delay(l));
            Outcome waited = Wait.Till(Clock, due, () => stop, () => k.Closed);
            if (!waited.Done)
                return waited.Why;
        }
        using (Act(k))
        {
            if (s.Use || s.Skill)
            {
                // Checked again right before the use: the tool can break during the wait (relay log 2026-10-16
                // 20:21:21.117 "You destroyed the shovel." and its 0x1D, then a use of it 67 ms later).
                if (s.Use)
                {
                    string? broke = ReplaceTool(k, steps, s);
                    if (broke != null)
                        return broke;
                }
                m_markJournal = new(k.State);
                string? why = s.Use ? k.Use(s.Serial, targetNext) : k.Skill(s.Id, targetNext);
                if (why != null)
                    return why;
                m_lastUse = Clock.UtcNow;
                if (targetNext)
                {
                    string? ended = null;
                    AwaitCursor(k, Clock.UtcNow.AddMilliseconds(CursorMs), null!,
                        () => (ended = Journal(k, l)) != null);
                    if (!k.MacroCursor && k.Closed)
                        return "the connection closed";
                    if (!k.MacroCursor && ended != null)
                        return ended;
                    if (k.MacroCursor)
                        Seen("cursor", (int)(Clock.UtcNow - m_lastUse).TotalMilliseconds);
                    if (!k.MacroCursor)
                    {
                        k.Settle();
                        string? said = Journal(k, l);
                        if (said != null)
                            return said;
                        // A digging tool that opened no cursor: replaced (gone) or set aside (still in the bag), then
                        // the step is tried again; the loop stops only when no other tool is left.
                        if (s.Use && s.Home != 0)
                        {
                            string? none = ReplaceTool(k, steps, s, true);
                            if (none != null)
                                return none;
                            return "again";
                        }
                        return "the server opened no target cursor";
                    }
                }
                else
                {
                    Answered(k, AnswerMs);
                    string? said = Journal(k, l);
                    if (said == null)
                        m_lastOk = m_lastUse;
                    return said;
                }
                m_lastOk = m_lastUse;
                return null;
            }
            string? t = k.Target(s.Ground, s.Serial, s.X, s.Y, s.Z, s.Graphic);
            if (t != null)
                return t;
            if (l.Name.StartsWith("Mining"))
                lock (m_pendingDigs)
                    m_pendingDigs.Enqueue(Clock.UtcNow);
            // The server's action timer runs from the target (Damian, 2026-10-15: "1250 per tool use (after first and
            // second target)"), so the next use's delay counts from here.
            m_lastUse = Clock.UtcNow;
            // With two or more tools the digs overlap: a target's answer is its own dig's result, about 2.6 s later on
            // uo1998, so waiting for it serializes the tools. Round zero starts both digs and each wait was answered by
            // the other tool's result (1.5 s a use); after a pause (the ore pile put down) the loop ran one dig at a
            // time, at half speed, for the rest of the run (relay log 2026-10-16 20:14:30, Damian: "it restarts at half
            // speed or only 1 of the two tools"). Such a loop paces on its delay alone.
            if (l.Pairs > 1)
                k.Settle();
            else
                Answered(k, AnswerMs);
            Clock.Sleep(100);
            string? heard = Journal(k, l);
            if (heard == null)
                m_lastOk = m_lastUse;
            return heard;
        }
    }

    // The journal since the step: a stop line ends the loop; a wait line moves this loop's delay for this server by the
    // bisect (Refused), keeps it, and repeats the step that long after the last use the server accepted.
    string? Journal(GameLink k, LoopKind l)
    {
        if (m_markJournal == null)
            return null;
        foreach (string j in m_markJournal.Answers())
        {
            foreach (string stopLine in s_stopLines)
            {
                if (j.IndexOf(stopLine, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // A worn-out tool in a tool loop is replaced before its next use (ReplaceTool), not a stop.
                    if (l.Pairs > 0 && stopLine == WornOut)
                        continue;
                    return "\"" + j + "\"";
                }
            }

            foreach (string full in s_fullPackLines)
            {
                if (j.IndexOf(full, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string? why = DumpOre(k);
                    if (why != null)
                        return why;
                    m_markJournal = new(k.State);
                    return null;
                }
            }

            foreach (string waitLine in s_waitLines)
            {
                if (j.StartsWith(waitLine, StringComparison.OrdinalIgnoreCase))
                {
                    string? why = Refused(l);
                    if (why != null)
                        return why;
                    // The retry waits the new delay from the refused attempt: the server's timer restarted no earlier
                    // than it.
                    m_markJournal = new(k.State);
                    m_lastUse = Clock.UtcNow;
                    m_refusedThisRound = true;
                    return "again";
                }
            }
        }
        return null;
    }
    volatile bool m_refusedThisRound;
    DateTime m_lastOk = DateTime.MinValue;
    // Settings the server corrects are kept per login server.
    public string Server = "";

    static uint U32(byte[] p, int at)
    {
        return ((uint)p[at] << 24) | ((uint)p[at + 1] << 16) | ((uint)p[at + 2] << 8) | p[at + 3];
    }
    static Step TargetStep(byte[] t)
    {
        Step s = new()
        {
            Target = true,
            Ground = t[1] == 1,
            Serial = U32(t, 7),
            X = (t[11] << 8) | t[12],
            Y = (t[13] << 8) | t[14],
            Z = (sbyte)t[16],
            Graphic = (t[17] << 8) | t[18]
        };
        s.Words = s.Ground ? "target the tile " + s.X + "," + s.Y + "," + s.Z : "target " + s.Serial.ToString("X");
        return s;
    }
    // The next client packet with this opcode, or null after ms.
    byte[]? Watch(GameLink k, int opcode, int ms)
    {
        byte[]? got = null;
        ManualResetEventSlim done = new(false);
        Action<byte[]> h = p => { if (p[0] == opcode && got == null) { got = p; done.Set(); } };
        k.ClientPacket += h;
        try
        {
            DateTime until = Clock.UtcNow.AddMilliseconds(ms);
            while (got == null && !stop && !k.Closed && Clock.UtcNow < until)
                Clock.Wait(done, 100);
        }
        finally { k.ClientPacket -= h; }
        return got;
    }

    // ---------- smithing ----------
    // uo1998 22:21:36 to 22:23:37: a double-click on the smith's hammer opens an item menu (0x7C: dialog serial, menu
    // id, title, then per item a model, a hue and a text); each pick is 0x7D (dialog serial, menu id, 1-based index,
    // model, hue), three deep for platemail legs ("Build Armor", "Build Plate Armor", "Build Platemail legs, 20
    // metal"). The item comes 3 to 10 s after the last pick (Damian: "there is a variable return length of time"), with
    // "You create the item ..." and a quality line ("Due to your exceptional skill ..." for exceptional). Round zero is
    // the player's own build; every later one replays its picks into menus kept off the client's screen.
    // A build has no fixed length (Damian, 2026-10-15: "sometimes the response is 20 seconds of hammer banging then
    // just a message that says you lost some metal. sometimes it responds immediatly"): the server plays the hammer
    // (sound 0x2A, uo1998 23:06:17 to 23:06:41, about one a second) until the result line, so the wait lasts while it
    // bangs.
    public const int HammerSound = 0x2A, IngotArt = 0x1BF2, SmithPicksMax = 6;
    int CraftMs => Ms("craft");
    int CraftQuietMs => Ms("craft-quiet");
    static readonly ImmutableArray<string> s_craftFailLines = [ "You failed", "you fail", "destroyed", "lost some" ];
    DateTime m_lastBang = DateTime.MinValue;
    static readonly ImmutableArray<string> s_craftStopLines = [ "enough metal", "enough ingot", "don't have enough",
        "do not have enough", "worn out", "broke", "must be near", "too far", "anvil", "forge" ];
    // The text of item index (1-based) in an item menu, or null.
    static string? MenuItem(byte[] m, int index)
    {
        if (m == null || m.Length < 11)
            return null;
        int at = 10 + m[9];
        if (at >= m.Length)
            return null;
        int n = m[at];
        at++;
        for (int i = 1; i <= n && at + 5 <= m.Length; i++)
        {
            int len = m[at + 4];
            if (at + 5 + len > m.Length)
                return null;
            if (i == index)
                return Encoding.ASCII.GetString(m, at + 5, len);
            at += 5 + len;
        }
        return null;
    }
    static int MetalCost(string text)
    {
        if (text == null)
            return 0;
        string[] parts = text.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i + 1 < parts.Length; i++)
        {
            int v;
            if (int.TryParse(parts[i], out v) && (parts[i + 1].StartsWith("metal") || parts[i + 1].StartsWith("ingot")))
                return v;
        }
        return 0;
    }
    // The next server packet with this opcode, or null after ms.
    byte[]? WatchServer(GameLink k, int opcode, int ms)
    {
        byte[]? got = null;
        ManualResetEventSlim done = new(false);
        Action<byte[]> h = p => { if (p[0] == opcode && got == null) { got = p; done.Set(); } };
        k.ServerPacket += h;
        try
        {
            DateTime until = Clock.UtcNow.AddMilliseconds(ms);
            while (got == null && !stop && !k.Closed && Clock.UtcNow < until)
                Clock.Wait(done, 100);
        }
        finally { k.ServerPacket -= h; }
        return got;
    }
    int PackIngots(GameLink k)
    {
        int n = 0;
        lock (k.State.Gate)
        {
            Set<uint> inside = Set<uint>.Of(k.State.Backpack);
            for (int pass = 0; pass < 8; pass++)
                foreach (KeyValuePair<uint, AssistItem> kv in k.State.Items)
                    if (inside.Contains(kv.Value.Container))
                        inside = inside.Add(kv.Key);
            foreach (KeyValuePair<uint, AssistItem> kv in k.State.Items)
                if (kv.Value.Graphic == IngotArt && inside.Contains(kv.Value.Container))
                    n += Math.Max(1, kv.Value.Amount);
        }
        return n;
    }
    // Waits for the build's result: "exceptional", "made", "failed", or the line that stops the loop (quoted).
    // circuit: a circuit's build, ended by the circuit's stop, not the loops' (a login sets the loops' stop, and the
    // smithing circuit's first build read "stopped" at once, 2026-10-18 17:36:35).
    string CraftResult(GameLink k, Heard mark, bool circuit = false)
    {
        DateTime start = Clock.UtcNow;
        DateTime cap = start.AddMilliseconds(CraftMs);
        Func<bool> halted = () => circuit ? circuitStop : stop;
        while (!halted() && !k.Closed)
        {
            DateTime now = Clock.UtcNow;
            DateTime heard = m_lastBang > start ? m_lastBang : start;
            if (now > cap || now > heard.AddMilliseconds(CraftQuietMs))
                break;
            List<string> lines = mark.Answers();
            foreach (string j in lines)
            {
                foreach (string s in s_craftStopLines)
                    if (j.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)
                        return "\"" + j + "\"";
                foreach (string s in s_craftFailLines)
                    if (j.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)
                        return "failed";
                if (j.StartsWith("You create", StringComparison.OrdinalIgnoreCase))
                {
                    Clock.Sleep(400);
                    if (mark.Has("exceptional"))
                        return "exceptional";
                    return "made";
                }
            }
            Clock.Sleep(100);
        }
        return halted() ? "stopped"
            : k.Closed ? "the connection closed"
            : "no result: the hammer went quiet " + CraftQuietMs / 1000 + " s with no line";
    }
}
