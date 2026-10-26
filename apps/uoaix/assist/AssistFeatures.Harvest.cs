#nullable enable
using System.Collections.Immutable;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Uoaix.Assist;

public sealed partial class AssistFeatures
{
    // ---------- harvest ----------
    // Damian, 2026-10-15: "a food harvest item pickup. it will like when running past carrots, it needs to pluck then
    // pickup", "and sheep for wool", "wheat and cotton need to harvest, as weell cabbage which requires no digging,
    // just pluck it". A crop is plucked by a double-click on the plant, and its product is lifted into the pack: the
    // carrots plant turns into the carrot itself (uo1998 21:59:17: 44B6C930, 0x0C76, became 0x0C78), wheat and cotton
    // drop a new item on the plant's tile (uo1998 01:40:30: wheat 0x0C58 dropped a sheaf 0x1EBD and became the stalk
    // 0x0DAE; 01:40:42: cotton 0x0C54 dropped a bale 0x0DF9 and was removed). A head of cabbage lies loose and is only
    // lifted: a double-click eats it (01:38:53). The plants are the 1.25 TILEDATA's, a fixed list: a learned one took
    // iron ore and ribs. A sheep (body 0xCF; 0xDF once shorn) is shorn with a blade from the pack targeted on it, and
    // its wool (0x0DF8) is picked up; the shearing is 1998 practice, not yet seen on uo1998's wire.
    public bool Harvest
    {
        get => Cfg.Get("harvest", "off") == "on";
        set => Cfg.Set("harvest", value ? "on" : "off");
    }
    static readonly ImmutableArray<int> s_cropPlants = [ 0x0C76, 0x0C4F, 0x0C50, 0x0C51, 0x0C52, 0x0C53, 0x0C54,
        0x0C55, 0x0C56,
        0x0C57, 0x0C58, 0x0C59, 0x0C5A, 0x0C5B ];
    static readonly ImmutableArray<int> s_looseCrops = [ 0x0C7B, 0x0C7C ];
    public const int SheepBody = 0xCF, WoolArt = 0x0DF8;
    int PluckMs => Ms("pluck");
    // A double-click or a lift leaves the player busy for 4 ticks of 250 ms (OUO packet_handler.c DispatchDoubleClick
    // and REQ_GETOBJ set actionState 4; player.c RegenTick counts it down). A lift while busy is refused with reason
    // 0xFF and a double-click is dropped unanswered (uo1998 01:29:26: a carrot lifted 250 ms after its pluck, "You must
    // wait to perform another action."), so each harvest action waits BusyMs, one tick of margin, after the last.
    int BusyMs => Ms(BusyKey);
    // The task the running thread does, set where each task's thread starts (a loop, the circuit, the weave, pickup,
    // harvest): its busy is busy:<task>.
    [ThreadStatic] static string? s_busyTask;
    string BusyKey => s_busyTask != null ? "busy:" + s_busyTask : "busy";
    DateTime m_harvestActAt;
    void HarvestGap()
    {
        int wait = BusyMs
            - (int)(Clock.UtcNow - (liftSentAt > m_harvestActAt ? liftSentAt : m_harvestActAt)).TotalMilliseconds;
        if (wait > 0)
            Clock.Sleep(wait);
        m_harvestActAt = Clock.UtcNow;
    }
    // Dagger, butcher knife, cleaver, skinning knife (1.25 TILEDATA).
    static readonly ImmutableArray<int> s_blades = [ 0x0F51, 0x0F52, 0x13F6, 0x13F7, 0x0EC2, 0x0EC3, 0x0EC4, 0x0EC5 ];
    public int[] Crops()
    {
        List<int> l = new(s_cropPlants);
        l.AddRange(s_looseCrops);
        return l.ToArray();
    }
    // What a pluck gave: a new item on the plant's tile (wheat, cotton), else the plant itself turned into the food
    // (carrots), else null.
    AssistItem? Plucked(GameLink k, AssistItem plant, Set<uint> before)
    {
        DateTime start = Clock.UtcNow;
        DateTime until = start.AddMilliseconds(PluckMs);
        AssistItem? turned = null;
        DateTime turnedAt = DateTime.MaxValue;
        while (Clock.UtcNow < until)
        {
            AssistItem? fresh = null;
            lock (k.State.Gate)
            {
                foreach (KeyValuePair<uint, AssistItem> kv in k.State.Ground)
                    if (kv.Value.X == plant.X && kv.Value.Y == plant.Y && !before.Contains(kv.Key))
                    {
                        fresh = kv.Value;
                        break;
                    }
                AssistItem? now;
                if (fresh == null && turned == null && k.State.Ground.TryGet(plant.Serial, out now)
                    && now.Graphic != plant.Graphic)
                {
                    turned = now;
                    turnedAt = Clock.UtcNow;
                }
            }
            if (fresh != null)
            {
                Seen("pluck", (int)(Clock.UtcNow - start).TotalMilliseconds);
                return fresh;
            }
            // A new item can follow the plant's own change on the wire: wait a little for one before taking the plant.
            if (turned != null && (Clock.UtcNow - turnedAt).TotalMilliseconds >= Ms("pluck-follow"))
            {
                Seen("pluck", (int)(turnedAt - start).TotalMilliseconds);
                return turned;
            }
            Clock.Sleep(50);
        }
        return turned;
    }
    ValueMap<uint, DateTime> m_shorn = ValueMap<uint, DateTime>.Empty; readonly Lock m_shornGate = new();
    void HarvestLoop()
    {
        s_busyTask = "harvest";
        while (!m_shutdown)
        {
            Clock.Sleep(Math.Max(1, Ms("harvest-scan")));
            GameLink k = m_link;
            if (k == null || k.Closed || k.Halted != null || !Harvest || k.State.Dead)
                continue;
            if (AssistGuard.Current != null && AssistGuard.Current.Disabled != null)
                continue;
            int reach = Reach;
            AssistItem? plant = null, wool = null;
            uint pack, blade = 0;
            int px, py, plantD = int.MaxValue, str, weight;
            lock (k.State.Gate)
            {
                pack = k.State.Backpack;
                px = k.State.X;
                py = k.State.Y;
                str = k.State.Str;
                weight = k.State.Weight;
                foreach (KeyValuePair<uint, AssistItem> kv in k.State.Ground)
                {
                    AssistItem g = kv.Value;
                    if (Left(g.Serial))
                        continue;
                    int d = Math.Max(Math.Abs(g.X - px), Math.Abs(g.Y - py));
                    if (d > reach)
                        continue;
                    if (g.Graphic == WoolArt || s_looseCrops.IndexOf(g.Graphic) >= 0)
                    {
                        wool = g;
                    }
                    else if (s_cropPlants.IndexOf(g.Graphic) >= 0 && d < plantD)
                    {
                        plant = g;
                        plantD = d;
                    }
                }
                foreach (KeyValuePair<uint, AssistItem> kv in k.State.Items)
                    if (s_blades.IndexOf(kv.Value.Graphic) >= 0
                        && (kv.Value.Container == pack || kv.Value.Mobile == k.State.Player))
                    {
                        blade = kv.Key;
                        break;
                    }
            }
            if (pack == 0 || (str > 0 && weight + 1 > str * 4 + 30))
                continue;
            AtlasNpc? sheep = null;
            if (plant == null && wool == null && blade != 0 && Atlas != null)
            {
                DateTime now = Clock.UtcNow.ToLocalTime();
                int reshear = Ms("reshear");
                foreach (AtlasNpc n in Atlas.Visible())
                {
                    if (n.Body != SheepBody || (now - n.Last).TotalMilliseconds > 3000
                        || Math.Max(Math.Abs(n.X - px), Math.Abs(n.Y - py)) > reach)
                        continue;
                    DateTime at;
                    if (m_shorn.TryGet(n.Serial, out at) && (Clock.UtcNow - at).TotalMilliseconds < reshear)
                        continue;
                    sheep = n;
                    break;
                }
            }
            if (plant == null && wool == null && sheep == null)
                continue;
            using (ActionScope? scope = TryAct(k))
            {
                if (scope == null)
                    continue;
                if (wool != null)
                {
                    Gather(k, wool, pack, UoTiles.Name(wool.Graphic));
                    continue;
                }
                if (plant != null)
                {
                    Set<uint> before = Set<uint>.Empty;
                    lock (k.State.Gate)
                        foreach (KeyValuePair<uint, AssistItem> kv in k.State.Ground)
                            if (kv.Value.X == plant.X && kv.Value.Y == plant.Y)
                                before = before.Add(kv.Key);
                    HarvestGap();
                    string? why = k.Use(plant.Serial, false);
                    if (why != null)
                    {
                        Leave(plant.Serial);
                        continue;
                    }
                    AssistItem? got = Plucked(k, plant, before);
                    k.Settle();
                    Leave(plant.Serial);
                    if (got == null)
                    {
                        Note("Harvest: " + UoTiles.Name(plant.Graphic) + " " + plant.Serial.ToString("X")
                            + " gave nothing; left alone");
                        continue;
                    }
                    Gather(k, got, pack, UoTiles.Name(got.Graphic));
                    continue;
                }
                lock (m_shornGate)
                {
                    if (m_shorn.Count > 2000)
                        m_shorn = m_shorn.Clear();
                    m_shorn = m_shorn.Set(sheep!.Serial, Clock.UtcNow);
                }
                HarvestGap();
                string? w = k.Use(blade, true);
                if (w != null)
                {
                    Note("Harvest: the blade " + blade.ToString("X") + " did not open: " + w);
                    continue;
                }
                DateTime cursorBy = Clock.UtcNow.AddMilliseconds(CursorMs);
                AwaitCursor(k, cursorBy);
                if (!k.MacroCursor)
                {
                    Note("Harvest: the blade opened no target cursor; sheep " + sheep.Serial.ToString("X") + " left");
                    continue;
                }
                w = k.Target(false, sheep.Serial, sheep.X, sheep.Y, 0, SheepBody);
                Note(w == null ? "Harvest: shearing sheep " + sheep.Serial.ToString("X")
                    : "Harvest: shearing failed: " + w);
                Answered(k, AnswerMs);
            }
        }
    }
    // ---------- mill, spin and weave ----------
    // Damian, 2026-10-15: "we need a miller feature", "and a tailor for the cott". The 1998 rule (UOX3
    // js/jse_objectassociations.scp, skill/cooking/wheat.js and flourmill.js; ServUO Cooking.cs WheatSheaf): a material
    // in the pack is double-clicked and its cursor targeted on a station: wheat sheaves into a flour mill, cotton or
    // wool on a spinning wheel, thread or yarn on a loom. uo1998 places these stations as items (a spinning wheel
    // 0x1019 at 648,2176, a loom 652,2177, three mills at Yew 550-554,820); how each answers on uo1998 is noted below.
    // The art is the 1.25 TILEDATA's.
    static readonly ImmutableArray<int> s_mills = [ 0x1920, 0x1921, 0x1922, 0x1923, 0x1925, 0x1926, 0x1927, 0x192C,
        0x192D, 0x192E,
        0x192F, 0x1931, 0x1932, 0x1933 ];
    static readonly ImmutableArray<int> s_wheels = [ 0x1015, 0x1016, 0x1017, 0x1019, 0x101A, 0x101B, 0x101C, 0x101D,
        0x101E, 0x10A4,
        0x10A5, 0x10A6 ];
    // A loom that weaves is an addon of two of these (ServUO LoomEastAddon 0x1060 and 0x105F, LoomSouthAddon 0x1061 and
    // 0x1062); 0x1063-0x1066, the upright loom, is decoration, and thread used on it got no answer (Moonglow,
    // 2026-10-17).
    static readonly ImmutableArray<int> s_looms = [ 0x105F, 0x1060, 0x1061, 0x1062 ];
    static readonly ImmutableArray<int> s_sheaves = [ 0x1EBD ], s_fibres = [ 0x0DF9, 0x0DF8, 0x101F ],
        s_strands = [ 0x0FA0, 0x0FA1, 0x0E1D, 0x0E1E, 0x0E1F ];
    static readonly ImmutableArray<int> s_products = [ 0x1039, 0x1045, 0x0FA0, 0x0FA1, 0x0E1D, 0x0E1E, 0x0E1F, 0x0F95,
        0x0F96,
        0x0F97, 0x0F98, 0x0F99, 0x0F9A, 0x0F9B, 0x0F9C, 0x175D, 0x175E, 0x175F, 0x1760, 0x1761, 0x1762, 0x1763, 0x1764,
        0x1765, 0x1767 ];
    // uo1998's loom takes one spool a use and builds the bolt across uses ("The bolt of cloth has just been started.",
    // "... needs quite a bit more.", "... needs a little more.", "... is almost finished.", then "You create some
    // cloth"; relay log 2026-10-15 02:09-02:10), not UOX3's five at once: every spool in the pack is used.
    // uo1998's mill (Yew, relay log 2026-10-15 02:17-02:19): a sheaf targeted on the hopper goes into it and the hopper
    // shows loaded (0x192E empty, 0x192F loaded); a double-click on the hopper grinds, "You need more wheat to make a
    // sack of flour." under MillSheaves, else the hopper shows 0x1932 and about 5 s later is empty again with a sack of
    // flour (0x1045) dropped on the ground beside it, which is picked up. The east mill's art (0x1922, 0x1923, 0x1926)
    // is taken to run the same way. Every mill in reach works at once (Damian, 2026-10-15: "there are three at a time
    // here").
    static readonly ImmutableArray<int> s_hopperEmpty = [ 0x1922, 0x192E ], s_hopperLoaded = [ 0x1923, 0x192F ],
        s_hopperGrinding = [ 0x1926, 0x1927, 0x1932, 0x1933 ];
    static readonly ImmutableArray<int> s_flourSacks = [ 0x1039, 0x1045 ];
    public const int MillSheaves = 4;  // a hopper with 1 sheaf before plus 3 ground; 1 before plus 2 needed more (02:18)
    // Sheaves this run put in each hopper; -1 for a hopper loaded before the run, whose count is unknown.
    ValueMap<uint, int> m_hopperLoad = ValueMap<uint, int>.Empty;
    Set<uint> m_oldSacks = Set<uint>.Empty, m_ground = Set<uint>.Empty, m_everGround = Set<uint>.Empty;
    bool m_millsTouched;
    // UOX3 InRange 3 for a mill, a wheel and a loom; Yew's three mills stand 2 tiles apart.
    public const int StationReach = 3, LoomTakes = 1, CollectWaitMs = 3000, WorkMisses = 3, StackResendMs = 500,
        WorkCursorMs = 20000;
    // Bolts off the loom (Damian, 2026-10-16: "after using the loom, to stack the bolts. inline is best"): the loom's
    // bolt comes as an item of its own ("You create some cloth and put it in your backpack.", a new 0x0F95 of amount 1,
    // uo1998 04:38:43.051), so right after the turn it goes on the pack's largest bolt stack of the same hue: any bolt
    // art first, then its own art when the server leaves it apart.
    List<AssistItem> PackBolts(GameLink k)
    {
        List<AssistItem> l = new();
        lock (k.State.Gate)
        {
            Set<uint> inside = PackTree(k);
            foreach (AssistItem it in k.State.Items.Values)
                if (inside.Contains(it.Container) && IsBolt(it.Graphic))
                    l.Add(it);
        }
        return l;
    }
    // Weaving inside a safe house (Damian, 2026-10-21: "for the weaving we need to be "in safe, private house" aware
    // and allow using thread from the floor directly on the loom, and making the stack of bolts also on the floor in
    // the house"): the player inside a registered private house. FloorReach: the 1998 use range for an item on the
    // ground.
    public const int FloorReach = 2;
    Home? SafeHouse(GameLink k)
    {
        if (!HousesKnown())
            return null;
        int px, py;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
        }
        Home? h = LockedHomeAt(k, px, py);
        return h != null && InHome(h, px, py) ? h : null;
    }
    // Ground items of these graphics inside the house and within FloorReach of the player: the total and the largest.
    static int OnFloor(GameLink k, Home h, IReadOnlyList<int> graphics, out AssistItem? biggest)
    {
        int n = 0;
        biggest = null;
        lock (k.State.Gate)
        {
            foreach (AssistItem g in k.State.Ground.Values)
            {
                if (!graphics.Contains(g.Graphic) || g.X < h.X0 || g.X > h.X1 || g.Y < h.Y0 || g.Y > h.Y1)
                    continue;
                if (Math.Max(Math.Abs(g.X - k.State.X), Math.Abs(g.Y - k.State.Y)) > FloorReach)
                    continue;
                n += Math.Max(1, g.Amount);
                if (biggest == null || g.Amount > biggest.Amount)
                    biggest = g;
            }
        }

        return n;
    }
    // The new bolt onto the house floor's largest bolt stack of its hue in reach, its own art first (a stack of another
    // art can leave it lying apart), else onto a house floor tile beside the player as the first stack (Damian,
    // 2026-10-21: "this keeps our pack clear and the number of items in the world to just the one big stacks not a
    // billion bolts at our feet"). Never the player's own tile: uo1998 sends a drop there back to the pack (1D, then 25
    // into the pack, 2026-10-21 06:06:35). A drop sent back ends floor bolts for the run (L-FUTILITY): false, and the
    // bolt is stacked in the pack instead.
    bool m_floorBoltRefused;
    bool FloorBolt(GameLink k, Home h, AssistItem bolt)
    {
        if (m_floorBoltRefused)
            return false;
        AssistItem? stack = null;
        foreach (bool sameArt in new[] { true, false })
        {
            if (stack != null)
                break;
            lock (k.State.Gate)
                foreach (AssistItem g in k.State.Ground.Values)
                    if (IsBolt(g.Graphic) && g.Hue == bolt.Hue && (!sameArt || g.Graphic == bolt.Graphic)
                        && g.X >= h.X0 && g.X <= h.X1 && g.Y >= h.Y0 && g.Y <= h.Y1
                        && Math.Max(Math.Abs(g.X - k.State.X), Math.Abs(g.Y - k.State.Y)) <= FloorReach
                        && (stack == null || g.Amount > stack.Amount))
                        stack = g;
        }
        int px, py;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
        }
        int[]? tile = null;
        if (stack == null)
        {
            ValueMap<long, int> seen = ValueMap<long, int>.Empty;
            for (int i = 0; i < 8 && tile == null; i++)
            {
                int x = px + s_dX[i], y = py + s_dY[i];
                if (!InHome(h, x, y) || x == h.DoorX && y == h.DoorY)
                    continue;
                int z = Open(k, x, y, h.Floor, ref seen);
                if (z != int.MinValue && Math.Abs(z - h.Floor) <= 1)
                    tile = new[] { x, y, z };
            }
            if (tile == null)
            {
                m_floorBoltRefused = true;
                Note("Work: no house floor beside " + px + "," + py
                    + " for the bolts; stacking them in the pack this run");
                return false;
            }
        }
        using (Act(k))
        {
            HarvestGap();
            string? why = BusyLift(k, bolt.Serial, Math.Max(1, bolt.Amount));
            m_harvestActAt = liftSentAt;
            if (why != null)
            {
                Note("Work: lifting the new bolt " + bolt.Serial.ToString("X") + ": " + why);
                return true;
            }
            why = stack != null ? k.Drop(bolt.Serial, stack.Serial)
                : k.DropGround(bolt.Serial, tile![0], tile[1], tile[2]);
            if (why != null)
            {
                Stopped("Putting the bolt on the house floor", bolt.Serial, why);
                return true;
            }
            Answered(k, AnswerMs);
            BoltLeft(k, bolt.Serial);
        }
        if (Item(k, bolt.Serial) != null)
        {
            m_floorBoltRefused = true;
            Note("Work: the bolt " + bolt.Serial.ToString("X")
                + " came back to the pack from the house floor; stacking bolts in the pack this run");
            return false;
        }
        bool apart;
        lock (k.State.Gate)
            apart = stack != null && k.State.Ground.ContainsKey(bolt.Serial);
        if (apart)
            Note("Work: the bolt " + bolt.Serial.ToString("X") + " did not stack onto " + stack!.Serial.ToString("X")
                + " on the house floor");
        return true;
    }
    // A bolt put down: waited for until it leaves the pack (the server's 0x1A on the floor, or 0x1D as it joins a
    // stack), DropSettleMs at most; a bolt sent back stays in the pack for the caller to see. The next use's busy time
    // runs from the lift's sending (harvestActAt = liftSentAt), not from here: waiting drop-settle whole and then
    // busy:weave after it held each bolt about 1.8 s past its drop (2026-10-21 04:50:30.581 to 04:50:32.416), and
    // counted from after lift-settle it still held 0.9 s (04:53).
    void BoltLeft(GameLink k, uint bolt)
    {
        Wait.Until(Clock,
            () => { AssistItem? it = Item(k, bolt); return it == null || !PackTree(k).Contains(it.Container); },
            DropSettleMs, "the bolt's drop", stepMs: 20);
    }
    void StackBolt(GameLink k, AssistItem bolt)
    {
        foreach (bool sameArt in new[] { false, true })
        {
            AssistItem? stack = null;
            foreach (AssistItem b in PackBolts(k))
                if (b.Serial != bolt.Serial && b.Hue == bolt.Hue && (!sameArt || b.Graphic == bolt.Graphic)
                    && (stack == null || b.Amount > stack.Amount))
                    stack = b;
            if (stack == null)
                return;
            using (Act(k))
            {
                HarvestGap();
                string? why = BusyLift(k, bolt.Serial, Math.Max(1, bolt.Amount));
                m_harvestActAt = liftSentAt;
                if (why != null)
                {
                    Note("Work: lifting the new bolt " + bolt.Serial.ToString("X") + ": " + why);
                    return;
                }
                why = k.Drop(bolt.Serial, stack.Serial);
                if (why != null)
                {
                    Stopped("Stacking the bolt", bolt.Serial, why);
                    return;
                }
                Answered(k, AnswerMs);
                BoltLeft(k, bolt.Serial);
            }
            if (Item(k, bolt.Serial) == null)
                return;
            if (stack.Graphic == bolt.Graphic)
            {
                Note("Work: the bolt " + bolt.Serial.ToString("X") + " did not stack onto "
                    + stack.Serial.ToString("X"));
                return;
            }
        }
    }
    int WorkWaitMs => Ms("work-wait");
    public volatile string WorkState = "", WorkEnd = "";
    // The weave's runner (AssistStructure.md stage 3): its stop is the weave's.
    readonly TaskRunner m_weaveRunner;
    bool workStop => m_weaveRunner.Stopped;
    // Mill, spin and weave as a task: its body Work.
    sealed class WorkTask : ITask
    {
        readonly AssistFeatures m_f; public WorkTask(AssistFeatures f)
        {
            m_f = f;
        }
        public string Name => "Mill, spin and weave";
        public TaskTimings Timings => TaskTimingRegistry.Work;
        public string Run(TaskContext context)
        {
            m_f.Work();
            return m_f.WorkEnd;
        }
    }
    static readonly ImmutableArray<string> s_workKinds = [ "wheel", "loom" ];
    // One mill action: pick up a new sack of flour, grind a full hopper, or load a sheaf. 1 acted, 2 nothing to do now
    // but a mill is grinding, 0 no mill work.
    int MillStep(GameLink k, uint pack, int px, int py, ref string end)
    {
        AssistItem? sack = null;
        List<AssistItem> hoppers = new();
        List<AssistItem> sacks = new();
        lock (k.State.Gate)
        {
            foreach (KeyValuePair<uint, AssistItem> kv in k.State.Ground)
            {
                AssistItem g = kv.Value;
                int d = Math.Max(Math.Abs(g.X - px), Math.Abs(g.Y - py));
                if (d > StationReach)
                    continue;
                if (s_flourSacks.IndexOf(g.Graphic) >= 0 && !m_oldSacks.Contains(g.Serial) && !Left(g.Serial))
                    sacks.Add(g);
                if (s_hopperEmpty.IndexOf(g.Graphic) >= 0 || s_hopperLoaded.IndexOf(g.Graphic) >= 0
                    || s_hopperGrinding.IndexOf(g.Graphic) >= 0)
                    hoppers.Add(g);
            }
        }
        // Only a sack beside a mill this run ground: another player's flour is left alone.
        foreach (AssistItem s in sacks)
            foreach (AssistItem h in hoppers)
                if (m_everGround.Contains(h.Serial) && Math.Abs(s.X - h.X) <= 1 && Math.Abs(s.Y - h.Y) <= 1)
                    sack = s;
        if (hoppers.Count == 0 && sack == null)
            return 0;
        if (sack != null)
        {
            WorkState = "picking up a sack of flour";
            Gather(k, sack, pack, "sack of flour");
            return 1;
        }
        bool grinding = false;
        foreach (AssistItem h in hoppers)
        {
            int n;
            bool known = m_hopperLoad.TryGet(h.Serial, out n);
            if (s_hopperGrinding.IndexOf(h.Graphic) >= 0)
            {
                if (m_ground.Contains(h.Serial))
                    grinding = true;
                m_hopperLoad = m_hopperLoad.Set(h.Serial, 0);
                continue;
            }
            m_ground = m_ground.Remove(h.Serial);
            if (s_hopperEmpty.IndexOf(h.Graphic) >= 0)
            {
                m_hopperLoad = m_hopperLoad.Set(h.Serial, 0);
                continue;
            }
            if (!known)
                m_hopperLoad = m_hopperLoad.Set(h.Serial, -1);
        }
        // Grind a hopper at MillSheaves, or one of unknown contents after each sheaf this run added (marked -2).
        foreach (AssistItem h in hoppers)
        {
            int n = m_hopperLoad.Get(h.Serial, 0);
            if (s_hopperLoaded.IndexOf(h.Graphic) < 0 || !(n >= MillSheaves || n == -2))
                continue;
            WorkState = "grinding the mill at " + h.X + "," + h.Y;
            Heard seq = new(k.State);
            using (Act(k))
            {
                HarvestGap();
                string? why = k.Use(h.Serial, true);
                if (why != null)
                {
                    end = "grinding failed: " + why;
                    return -1;
                }
            }
            DateTime by = Clock.UtcNow.AddMilliseconds(1500);
            bool more = false, started = false;
            while (Clock.UtcNow < by && !more && !started)
            {
                if (seq.Has("need more wheat"))
                    more = true;
                AssistItem? now;
                k.State.Ground.TryGet(h.Serial, out now);
                if (now != null && s_hopperGrinding.IndexOf(now.Graphic) >= 0)
                    started = true;
                Clock.Sleep(50);
            }
            k.Settle();
            m_millsTouched = true;
            if (started)
            {
                m_hopperLoad = m_hopperLoad.Set(h.Serial, 0);
                m_ground = m_ground.Add(h.Serial);
                m_everGround = m_everGround.Add(h.Serial);
                Note("Work: the mill at " + h.X + "," + h.Y + " is grinding");
                return 1;
            }
            m_hopperLoad = m_hopperLoad.Set(h.Serial, n >= MillSheaves ? MillSheaves - 1
                : -1);  // needs more: one more sheaf, then try again
            return 1;
        }
        // Load a sheaf: a hopper this run has started first, then an empty one, then one of unknown contents.
        AssistItem? sheaf;
        int have = InPack(k, pack, s_sheaves, out sheaf);
        if (sheaf == null)
            return grinding ? 2 : 0;
        AssistItem? target = null;
        int rank = 9;
        foreach (AssistItem h in hoppers)
        {
            if (s_hopperGrinding.IndexOf(h.Graphic) >= 0)
                continue;
            int n = m_hopperLoad.Get(h.Serial, 0), r = n > 0 ? 0 : n == 0 ? 1 : 2;
            if (r < rank)
            {
                rank = r;
                target = h;
            }
        }
        if (target == null)
            return grinding ? 2 : 0;
        WorkState = "wheat into the mill at " + target.X + "," + target.Y + ": " + have + " sheaves left";
        using (Act(k))
        {
            HarvestGap();
            string? why = k.Use(sheaf.Serial, true);
            if (why != null)
            {
                end = "the wheat sheaf did not open: " + why;
                return -1;
            }
            DateTime cursorBy = Clock.UtcNow.AddMilliseconds(CursorMs);
            AwaitCursor(k, cursorBy);
            if (!k.MacroCursor)
            {
                k.Settle();
                end = "the wheat sheaf opened no target cursor";
                return -1;
            }
            why = k.Target(false, target.Serial, target.X, target.Y, target.Z, target.Graphic);
            if (why != null)
            {
                end = "targeting the mill failed: " + why;
                return -1;
            }
            m_harvestActAt = Clock.UtcNow;
        }
        AssistItem? none;
        bool took = Wait.Until(Clock, () => InPack(k, pack, s_sheaves, out none) < have, 3000, "the sheaves",
            stepMs: 100).Done;
        k.Settle();
        m_millsTouched = true;
        if (!took)
        {
            end = "the mill at " + target.X + "," + target.Y + " took no wheat (see the journal)";
            return -1;
        }
        int was = m_hopperLoad.Get(target.Serial, 0);
        m_hopperLoad = m_hopperLoad.Set(target.Serial, was < 0 ? -2 : was + 1);
        return 1;
    }
    string? m_lastKind = ""; DateTime m_lastWorkAt;
    // What the weave step is doing, for the loop button: spin at a wheel, weave at a loom, mill at a mill.
    public string WorkVerb => m_lastKind == "wheel" ? "spin"
        : m_lastKind == "loom" ? "weave" : m_millsTouched ? "mill" : "weave";
    ValueMap<uint, DateTime> m_wheelBusy = ValueMap<uint, DateTime>.Empty;
    int SpinMs => Ms("spin");   // uo1998 spun a bale in 5.1 s and 5.4 s (relay log 02:06:35, 02:06:50), with margin
    public bool WorkRunning => m_weaveRunner.Running;
    // The weave button and the control port's work: one of a loop, the weave and a circuit at a time (Damian,
    // 2026-10-20: "it should mutually exclude the other loop calls, as well as circuits and loops should not be
    // invokable at the same time"). A circuit's or a loop's own weave goes through WorkToggle.
    public string? WorkUserToggle()
    {
        if (!WorkRunning)
        {
            if (m_circuitThread != null && m_circuitThread.IsAlive)
                return "a circuit is running: " + CircuitName;
            if (m_loops.Running)
                return "a loop is running: " + Status;
        }
        WorkToggle();
        return null;
    }
    public void WorkToggle()
    {
        if (m_weaveRunner.Stop())
        {
            WorkState = "stopping...";
            return;
        }
        m_weaveRunner.Start(new WorkTask(this));
    }
    // The pack's own items of these graphics: the total and the largest stack.
    static int InPack(GameLink k, uint pack, IReadOnlyList<int> graphics, out AssistItem? biggest)
    {
        int n = 0;
        biggest = null;
        lock (k.State.Gate)
        {
            foreach (KeyValuePair<uint, AssistItem> kv in k.State.Items)
            {
                AssistItem it = kv.Value;
                if (it.Container != pack || !graphics.Contains(it.Graphic))
                    continue;
                // uo1998 sends a single item's amount as 0 (relay log 02:06:35: the bale 4570393A, 0x25 amount 0000).
                n += Math.Max(1, it.Amount);
                if (biggest == null || it.Amount > biggest.Amount)
                    biggest = it;
            }
        }

        return n;
    }
    void Work()
    {
        s_busyTask = "weave";
        int done = 0, misses = 0, busyWaits = 0;
        string end = "nothing to work";
        GameLink k = m_link;
        m_lastKind = "";
        m_wheelBusy = m_wheelBusy.Clear();
        m_hopperLoad = m_hopperLoad.Clear();
        m_ground = m_ground.Clear();
        m_everGround = m_everGround.Clear();
        m_oldSacks = m_oldSacks.Clear();
        m_millsTouched = false;
        m_floorBoltRefused = false;
        if (k != null)
            lock (k.State.Gate)
                foreach (KeyValuePair<uint, AssistItem> kv in k.State.Ground)
                    if (s_flourSacks.IndexOf(kv.Value.Graphic) >= 0)
                        m_oldSacks = m_oldSacks.Add(kv.Key);
        try
        {
            while (!workStop)
            {
                k = m_link;
                if (k == null || k.Closed)
                {
                    end = "no connection";
                    break;
                }
                if (k.State.Dead)
                {
                    end = "you are dead";
                    break;
                }
                uint pack;
                int px, py;
                lock (k.State.Gate)
                {
                    pack = k.State.Backpack;
                    px = k.State.X;
                    py = k.State.Y;
                }
                // The nearest station of each kind in reach. With more than one kind holding material in the pack, the
                // kinds take turns (Damian, 2026-10-15: "make it spin and weave at the same time if both are there",
                // "like after you start"): a wheel takes its bale at the target, so the loom weaves while the wheel
                // turns.
                int mill = MillStep(k, pack, px, py, ref end);
                if (mill < 0)
                    break;
                if (mill == 1)
                {
                    done++;
                    misses = 0;
                    continue;
                }
                bool grinding = mill == 2;
                // Every spinning wheel in reach, but only the nearest loom (Damian, 2026-10-16: "for spinning, use all
                // available spinning wheels, but only 1 loom").
                Map<string, AssistItem> near = Map<string, AssistItem>.Empty;
                ValueMap<string, int> nearD = ValueMap<string, int>.Empty;
                List<AssistItem> wheels = new();
                lock (k.State.Gate)
                {
                    foreach (KeyValuePair<uint, AssistItem> kv in k.State.Ground)
                    {
                        AssistItem g = kv.Value;
                        int d = Math.Max(Math.Abs(g.X - px), Math.Abs(g.Y - py));
                        if (d > StationReach)
                            continue;
                        if (s_wheels.IndexOf(g.Graphic) >= 0)
                        {
                            wheels.Add(g);
                            continue;
                        }
                        int was;
                        if (s_looms.IndexOf(g.Graphic) >= 0 && (!nearD.TryGet("loom", out was) || d < was))
                        {
                            near = near.Set("loom", g);
                            nearD = nearD.Set("loom", d);
                        }
                    }
                }

                if (near.Count == 0 && wheels.Count == 0 && !grinding)
                {
                    end = m_millsTouched ? "no wheat left in the backpack and no mill grinding"
                        : "no flour mill, spinning wheel or loom within " + StationReach + " tiles";
                    break;
                }
                AssistItem? station = null, use = null;
                string? kind = null;
                IReadOnlyList<int>? takes = null;
                int have = 0;
                Home? safe = SafeHouse(k);
                bool fromFloor = false;
                int from = s_workKinds.IndexOf(m_lastKind!) + 1;
                // A wheel spins one bale at a time (uo1998 02:06:35 to 02:06:40: the bale taken at the target, the
                // spool 5 s later), so a wheel that took a bale is passed over for SpinMs; another free wheel takes the
                // next bale.
                AssistItem? none;
                int madeNow = InPack(k, pack, s_products, out none);
                DateTime nowUtc = Clock.UtcNow;
                bool spinning = false;
                AssistItem? freeWheel = null;
                foreach (AssistItem w in wheels)
                {
                    DateTime busy;
                    if (m_wheelBusy.TryGet(w.Serial, out busy) && busy > nowUtc)
                        spinning = true;
                    else if (freeWheel == null)
                        freeWheel = w;
                }
                if (freeWheel != null)
                    near = near.Set("wheel", freeWheel);
                for (int i = 0; i < s_workKinds.Length && use == null; i++)
                {
                    string kd = s_workKinds[(from + i) % s_workKinds.Length];
                    if (!near.ContainsKey(kd))
                        continue;
                    if (kd == "loom")
                    {
                        // A loom counts one material across every stack in the pack (UOX3 ResourceCount): the first
                        // with LoomTakes.
                        foreach (int sg in s_strands)
                        {
                            AssistItem? big;
                            int n = InPack(k, pack, new[] { sg }, out big);
                            if (n >= LoomTakes)
                            {
                                takes = new[] { sg };
                                use = big;
                                have = n;
                                break;
                            }
                        }
                        // The pack empty of strands inside a safe house: the thread on its floor.
                        if (use == null && safe != null)
                            foreach (int sg in s_strands)
                            {
                                AssistItem? big;
                                int n = OnFloor(k, safe, new[] { sg }, out big);
                                if (n >= LoomTakes)
                                {
                                    takes = new[] { sg };
                                    use = big;
                                    have = n;
                                    fromFloor = true;
                                    break;
                                }
                            }
                    }
                    else
                    {
                        takes = s_fibres;
                        have = InPack(k, pack, takes, out use);
                    }
                    if (use != null)
                    {
                        kind = kd;
                        station = near[kd]!;
                    }
                }
                if (use == null)
                {
                    // The wheel is still spinning: its spool is the next material.
                    if (spinning || grinding)
                    {
                        Clock.Sleep(200);
                        continue;
                    }
                    end = "no wheat, cotton, wool, thread or yarn left in the backpack for the stations here";
                    break;
                }
                m_lastKind = kind;
                m_lastWorkAt = Clock.UtcNow;
                int made = madeNow;
                Heard workMark = new(k.State);
                List<AssistItem>? boltsBefore = kind == "loom" ? PackBolts(k) : null;
                WorkState = UoTiles.Name(use.Graphic) + " on the " + UoTiles.Name(station!.Graphic) + ": " + have
                    + " left, " + done + " done";
                using (Act(k))
                {
                    HarvestGap();
                    string? why = k.Use(use.Serial, true);
                    if (why != null)
                    {
                        end = "the " + UoTiles.Name(use.Graphic) + " did not open: " + why;
                        break;
                    }
                    // A slow server is waited out, not given up on (2026-10-21 07:37:55: the spool's answer came 9.7 s
                    // late, the cursor after the wait had ended, and it fell to the client): up to WorkCursorMs, ended
                    // early by a busy line.
                    DateTime cursorBy = Clock.UtcNow.AddMilliseconds(WorkCursorMs);
                    AwaitCursor(k, cursorBy, null!,
                        () => workMark.Has("You must wait") || workMark.Has("in use") || workStop);
                    // "You must wait" is an answer that asks for the use again (assist-plan.md rule 1): the player's
                    // own double-click on a bag 0.87 s before left him busy, and the weave stopped (2026-10-20
                    // 03:07:14).
                    if (!k.MacroCursor && workMark.Has("You must wait") && ++busyWaits <= WorkMisses)
                    {
                        k.Settle();
                        Clock.Sleep(BusyMs);
                        continue;
                    }
                    if (!k.MacroCursor)
                    {
                        k.Settle();
                        end = "the " + UoTiles.Name(use.Graphic) + " opened no target cursor";
                        break;
                    }
                    busyWaits = 0;
                    why = k.Target(false, station.Serial, station.X, station.Y, station.Z, station.Graphic);
                    if (why != null)
                    {
                        end = "targeting the " + UoTiles.Name(station.Graphic) + " failed: " + why;
                        break;
                    }
                    m_harvestActAt = Clock.UtcNow;
                }
                // The material leaves the pack or a product arrives; a loaded mill is emptied when its hopper shows
                // ready. The same slow server on the answer to the target: waited out up to WorkCursorMs.
                DateTime workFrom = Clock.UtcNow;
                DateTime until = workFrom.AddMilliseconds(Math.Max(WorkWaitMs, WorkCursorMs));
                bool took = false, gave = false;
                while (Clock.UtcNow < until && !workStop)
                {
                    took = (fromFloor ? OnFloor(k, safe!, takes!, out none) : InPack(k, pack, takes!, out none)) < have;
                    gave = InPack(k, pack, s_products, out none) > made;
                    // A spool from another wheel is not this wheel's answer: a wheel answers by taking its bale.
                    if (took || gave && kind != "wheel")
                    {
                        Seen("work-wait", (int)(Clock.UtcNow - workFrom).TotalMilliseconds);
                        break;
                    }
                    if (workMark.Has("in use") || workMark.Has("You must wait"))
                        break;
                    Clock.Sleep(100);
                }
                k.Settle();
                // A stack the station took one from is deleted and sent again one fewer (uo1998 04:23:53.311 to .439, a
                // vendor stack of 10 "shuttle of thread", art 0x0FA0, on the loom); counted in between, the pack read
                // empty and weaving stopped after one. The next turn waits up to StackResendMs for the stack to come
                // back. The loom is deleted and sent again after each turn too (1D then 1A for 40001FAA); counted in
                // between, no loom was in reach and weaving stopped with 21 spools in the pack (uo1998 2026-10-20
                // 10:32:53.897 to .898).
                if (took)
                {
                    DateTime back = Clock.UtcNow.AddMilliseconds(StackResendMs);
                    while (Clock.UtcNow < back)
                    {
                        bool stationBack, useBack;
                        lock (k.State.Gate)
                        {
                            stationBack = k.State.Ground.ContainsKey(station.Serial);
                            useBack = fromFloor && k.State.Ground.ContainsKey(use.Serial);
                        }
                        if (stationBack && (useBack || !fromFloor && Item(k, use.Serial) != null))
                            break;
                        Clock.Sleep(20);
                    }
                }
                if (boltsBefore != null)
                    foreach (AssistItem b in PackBolts(k))
                        if (!boltsBefore.Exists(o => o.Serial == b.Serial))
                        {
                            if (safe == null || !FloorBolt(k, safe, b))
                                StackBolt(k, b);
                            break;
                        }
                // A station can be in use for a moment (UOX3: "That is already in use." while a wheel turns), or the
                // server asks for a wait: tried again. Silence, or any other answer, stops the work (assist-plan.md
                // rule 1).
                if (!took && (!gave || kind == "wheel"))
                {
                    if (kind == "wheel")
                        m_wheelBusy = m_wheelBusy.Set(station.Serial, Clock.UtcNow.AddMilliseconds(SpinMs));
                    List<string> said = workMark.Answers();
                    bool again = said.Exists(j => j.IndexOf("in use", StringComparison.OrdinalIgnoreCase) >= 0
                        || j.IndexOf("You must wait", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (!again)
                    {
                        end = "the " + UoTiles.Name(station.Graphic) + " took nothing"
                            + (said.Count > 0 ? "; the server said \"" + said[said.Count - 1] + "\""
                            : " and the server said nothing");
                        break;
                    }
                    if (++misses >= WorkMisses)
                    {
                        end = "the " + UoTiles.Name(station.Graphic) + " was busy " + WorkMisses + " times in a row";
                        break;
                    }
                    Clock.Sleep(3000);
                    continue;
                }
                if (kind == "wheel")
                    m_wheelBusy = m_wheelBusy.Set(station.Serial, Clock.UtcNow.AddMilliseconds(SpinMs));
                misses = 0;
                done++;
                Note("Work: " + UoTiles.Name(use.Graphic) + " on the " + UoTiles.Name(station.Graphic)
                    + (gave ? "" : ", nothing new in the pack yet"));
            }
            if (workStop)
                end = "stopped";
        }
        finally
        {
            WorkState = "";
            WorkEnd = end;
            Note("Work: " + done + " done; " + end);
            if (k != null && !k.Closed)
                k.Tell("Mill, spin and weave: " + done + " done; " + end + ".", done > 0 ? 0x0044 : 0x002B);
            if (ThenTailor && done > 0 && workStop == false && k != null && !k.Closed && !k.State.Dead
                && !(m_circuitThread != null && m_circuitThread.IsAlive))
                ThenTailorStart();
        }
    }
    // Then tailor (Damian, 2026-10-20: "make weave have an option to auto tailor when done", "using last tailor
    // settings"): a weave the player started, ended by itself, starts the tailoring loop that ran last on its last
    // targets.
    public bool ThenTailor
    {
        get => Cfg.Get("work.tailor", "off") == "on";
        set => Cfg.Set("work.tailor", value ? "on" : "off");
    }
    void ThenTailorStart()
    {
        LoopKind tk = m_lastTailorKind;
        if (tk == null)
        {
            Note("Mill, spin and weave: then tailor: no tailoring loop has run since the relay started, so there are "
                + "no last targets");
            return;
        }
        LastTargets? lt;
        m_lastTargets.TryGet(tk.Name, out lt);
        string? w = Start(tk, Cfg.GetInt("rounds." + tk.Name, 10), lt != null ? lt.Until : 0, null!, true);
        Note("Mill, spin and weave: then tailor: "
            + (w == null ? "started " + tk.Name + " on the last targets" : "not started: " + w));
    }

    // Lift a ground item into the pack, as the pickup does.
    public void Gather(GameLink k, AssistItem it, uint pack, string what)
    {
        Spot? from = Where(k, it.Serial);
        HarvestGap();
        string? why = Lifted(k, it.Serial, it.Amount);
        if (why != null)
        {
            Leave(it.Serial);
            Note("Harvest: " + what + " not lifted: " + why);
            return;
        }
        why = k.Drop(it.Serial, pack);
        if (why != null)
        {
            Stopped("harvest", it.Serial, why);
            Leave(it.Serial);
            return;
        }
        Answered(k, AnswerMs);
        // A pickup the server put back is left, never lifted again (L-FUTILITY).
        if (Landed(k, it.Serial, from!, "harvest") != null)
        {
            Leave(it.Serial);
            return;
        }
        lock (m_pickedGate)
            m_picked = m_picked.Set(it.Graphic, m_picked.Get(it.Graphic, 0) + it.Amount);
        SeenForget(it.X, it.Y, it.Graphic);
        Note("Harvest: " + it.Amount + " " + what + " into the backpack");
    }
}
