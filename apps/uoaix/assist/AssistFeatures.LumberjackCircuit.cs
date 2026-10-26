#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Threading;

namespace Uoaix.Assist;

public sealed partial class AssistFeatures
{
    // ---------- the lumberjack circuit ----------
    // Damian, 2026-10-22: "Grove is irrelevant, there are trees everywhere on this map"; "the chopper chops forever
    // until told to stop"; "logs piled wherever we target. provide a button and a target aquisition and just put them
    // there. tile, bag, pile, floor, wherever. even a packmule"; "you may use any non-magical axe as your chopping
    // tool. if one is not present, stop and inform the player"; "equip it is fine, de-equip other things is fine"; "the
    // threat avoidance should be enabled".
    // The 1998 server (rundir scripts.wombat/bladed.m, itemmanip.m Q50A): an axe chops only while equipped, only a tree
    // static of LumberTrees, and only while the 8x8 chunk egg holds 10 wood or more ("There's not enough wood here to
    // harvest."); the chop runs while the chopper stays within 2 tiles, then puts 10 wood as logs in the pack ("You put
    // some logs"), or fails its skill check or finds under 10 left ("You hack at the tree for a while, but fail to
    // produce any useable wood.").
    // A chunk found empty stays empty until the sentinel has wood (LumberProbe) or the player clears the record
    // (Damian, 2026-10-23: "the timer on the log chunks is far too slow and needs to be remembered from run to run by
    // default, only a clear should cause known empty chunks from being re-logged"): kept in lumber.empty across runs
    // and restarts.
    public const int LumberRadiusDefault = 40, LumberUnansweredMax = 3, LumberWood = 10;
    // The tree search's reach, read at every search so it can be tuned during the circuit (Damian, 2026-10-22: "if
    // there is a max range on this, make it a config i can tune during the circuit").
    public int LumberRadius
    {
        get => Math.Max(1, Math.Min(200, Cfg.GetInt(Server + "lumber.range", LumberRadiusDefault)));
        set => Cfg.Set(Server + "lumber.range", Math.Max(1, Math.Min(200, value)).ToString());
    }
    public const uint LumberAxeCursor = 0x4C415845, LumberLogsCursor = 0x4C4C4F47;  // "LAXE", "LLOG"
    // itemmanip.m Q50A, as inclusive ranges.
    static readonly ImmutableArray<int> s_lumberTrees = [ 0x0C95, 0x0C96, 0x0C9E, 0x0C9E, 0x0CCA, 0x0CE6, 0x0CE8,
        0x0CE8, 0x0CF8,
        0x0D03, 0x0D41, 0x0D53, 0x0D6E, 0x0D7F, 0x0D84, 0x0D90, 0x0D94, 0x0DAB, 0x12B6, 0x12BE, 0x12C0, 0x12C7, 0x1323,
        0x1323 ];
    public static bool IsTree(int graphic)
    {
        return InRanges(s_lumberTrees, graphic);
    }
    public static bool IsAxe(int graphic)
    {
        return LumberGather.IsAxe(graphic);
    }
    static bool IsLog(int graphic)
    {
        return LumberGather.IsLog(graphic);
    }
    // The place, At my feet, Collect at and the pile record (AssistHarvest.md stage 1); the members below of the old
    // names reach them.
    readonly LumberGather m_lumberGather;
    // A chop's answer: got logs, failed (chop again), the chunk empty, not a tree, the axe not in hand, wait, or none.
    public enum ChopAnswer
    {
        None, Logs, Failed, Empty, NotTree, NotEquipped, Wait
    }
    public static ChopAnswer ChopSaid(string line)
    {
        if (line.IndexOf("You put some logs", StringComparison.OrdinalIgnoreCase) >= 0)
            return ChopAnswer.Logs;
        if (line.IndexOf("fail to produce any useable wood", StringComparison.OrdinalIgnoreCase) >= 0)
            return ChopAnswer.Failed;
        if (line.IndexOf("not enough wood here to harvest", StringComparison.OrdinalIgnoreCase) >= 0)
            return ChopAnswer.Empty;
        if (line.IndexOf("You can't use an axe on that", StringComparison.OrdinalIgnoreCase) >= 0
            || line.IndexOf("You can't use a bladed item on that", StringComparison.OrdinalIgnoreCase) >= 0
            || line.IndexOf("too far away", StringComparison.OrdinalIgnoreCase) >= 0)
            return ChopAnswer.NotTree;
        if (line.IndexOf("must me equipped", StringComparison.OrdinalIgnoreCase) >= 0
            || line.IndexOf("must be equipped", StringComparison.OrdinalIgnoreCase) >= 0)
            return ChopAnswer.NotEquipped;
        foreach (string wl in s_waitLines)
            if (line.StartsWith(wl, StringComparison.OrdinalIgnoreCase))
                return ChopAnswer.Wait;
        return ChopAnswer.None;
    }
    volatile bool m_lumberMode;
    public string? CircuitLumber()
    {
        return StartCircuit(() => m_lumberMode = true);
    }
    // New chop (Damian, 2026-10-22: "new chop clears path and feet drops, but warns before doing, with suggestion to
    // collect first"; 2026-10-23: "what is intended whn i click new chop is a new goddamn chop. none of the old data is
    // valid except the axe. where i stand is the collection point, the first thing i chop becomes the new sentinel.
    // none of the drops or chunks from ANY PREVIOUS CHOP matter anymore.... dump it all"): once the circuit is allowed
    // to start, the piles at feet, the sentinel, the chunks found empty, the chunk grid and Chop from are forgotten,
    // the log piles in view are old drops never found as wild, the collection point is the player's tile, and the next
    // tree chopped is the sentinel (SentinelNext); then chopping as Resume does. Kept: the axe, and the settings At my
    // feet, Range and Collect at. The window warns first and clears its path.
    public const string SentinelNext = "next";
    public string? CircuitLumberNew()
    {
        return StartCircuit(() =>
        {
            m_lumberMode = true;
            int empties = LumberEmptyClear();
            LumberSentinel = SentinelNext;
            LumberFrom = "";
            GameLink k = m_link;
            int px = 0, py = 0, pz = 0, old = 0;
            bool here = k != null;
            Set<uint> olds = lumberOld;
            if (here)
            {
                lock (k!.State.Gate)
                {
                    px = k.State.X;
                    py = k.State.Y;
                    pz = k.State.Z;
                    foreach (AssistItem g in k.State.Ground.Values)
                        if (g.Container == 0 && IsLog(g.Graphic) && !olds.Contains(g.Serial))
                        {
                            if (olds.Count >= HousedMax)
                                olds = Set<uint>.Empty;
                            olds = olds.Add(g.Serial);
                            old++;
                        }
                }
            }

            if (old > 0)
                lumberOld = olds;
            int piles, logs;
            m_lumberGather.NewStart(here ? new[] { px, py, pz } : null, out piles, out logs);
            Note("Lumber: new chop" + (piles > 0 ? "; " + logs + " logs in " + piles + " piles at feet forgotten" : "")
                + (empties > 0 ? "; " + empties + " empty chunks forgotten" : "")
                + (old > 0 ? "; " + old + " log piles in view are old drops" : "")
                + (here ? "; the collection point is " + px + "," + py : "")
                + "; the first tree chopped is the sentinel");
        });
    }
    // Collect now (Damian, 2026-10-22: "add a collect now button that ignores the collect number and just go starts the
    // fetch"): during Lumberjacking the next step collects the piles at feet, Collect at or not, and chopping goes on;
    // with no circuit running, a circuit of the collection alone.
    volatile bool m_collectNow, m_collectOnly;
    public string? CollectNow()
    {
        GameLink k = m_link;
        if (k != null && !k.Closed)
            LumberDiscover(k);
        if (LumberPiles().Count == 0)
            return "no piles dropped to collect";
        if (CircuitRunning)
        {
            if (!m_lumberMode)
                return "the running circuit is not Lumberjacking";
            m_collectNow = true;
            Note("Lumber: collect now asked");
            return null;
        }
        return StartCircuit(() => { m_lumberMode = true; m_collectOnly = true; });
    }
    // Reset collection point (Damian, 2026-10-23: "when i started the chop, i goofed the collection point. i need to
    // fix that and recollect to the spot i am standing on at the time"; "it should merely change the point, not invoke
    // the job"): Logs to becomes the player's tile, a log pile in view at the old place joins the record as wild (one
    // out of view is not known to be there), and a record pile on the new tile leaves the record; the next collection
    // carries them. Null, or why not.
    public string? LumberRecollectHere()
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "not connected";
        int ox, oy, oz;
        uint onto;
        bool had = CollectPlace(k, out ox, out oy, out oz, out onto) == null;
        int px, py, pz;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
            pz = k.State.Z;
        }
        AssistItem? pile = had && (ox != px || oy != py) ? LogPileAt(k, ox, oy) : null;
        if (pile != null)
            LumberPileAdd(ox, oy, Math.Max(1, pile.Amount), true);
        LumberLogsTo = "ground|0|" + px + "|" + py + "|" + pz;
        LumberPileRemove(px, py);
        Note("Lumber: the collection point reset to " + px + "," + py + (had ? ", from " + ox + "," + oy : "")
            + (pile != null ? "; its " + Math.Max(1, pile.Amount) + " logs there join the collection" : ""));
        return null;
    }
    public volatile string LumberStatus = "";
    public uint LumberAxe
    {
        get => m_lumberGather.Tool;
        set => m_lumberGather.Tool = value;
    }
    public string LumberLogsTo
    {
        get => m_lumberGather.PlaceSpec;
        set => m_lumberGather.PlaceSpec = value;
    }
    public string LumberLogsText()
    {
        return PlaceText(LumberLogsTo, "Logs to")
            + (LumberAtFeet && !LumberLogsTo.StartsWith("feet") ? ", at my feet first" : "");
    }
    static string PlaceText(string spec, string button)
    {
        string[] t = spec.Split('|');
        if (t.Length < 2)
            return button + ": not set";
        if (t[0] == "feet")
            return button + ": at my feet";
        return button + ": "
            + (t[0] == "ground" && t.Length >= 5 ? "the ground at " + t[2] + "," + t[3] + "," + t[4]
            : t[0] == "mobile" ? "the mobile " + t[1] : "the item " + t[1]);
    }
    public string? LumberAxeTarget()
    {
        return LumberCursor(LumberAxeCursor, "Target the axe in your pack or your hand.");
    }
    public string? LumberLogsTarget()
    {
        return LumberCursor(LumberLogsCursor,
            "Target where the logs go: a tile, a bag, a pile, the floor or a pack animal.");
    }
    // The axe by an object cursor; a place by a location cursor (type 1), which takes any tile as well as anything on
    // it (Damian, 2026-10-22: "set logs to should take any tile that can be put to").
    string? LumberCursor(uint id, string ask)
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "not connected";
        byte[] p = new byte[19];
        p[0] = 0x6C;
        p[1] = (byte)(id == LumberAxeCursor ? 0 : 1);
        Put32(p, 2, id);
        string? why = k.ToClient(p, "lumber cursor");
        LumberStatus = why == null ? ask : "No cursor: " + why;
        return why;
    }
    bool HoldLumber(GameLink k, byte[] p)
    {
        if (p[0] != 0x6C || p.Length < 19)
            return false;
        uint id = U32(p, 2);
        if (id != LumberAxeCursor && id != LumberLogsCursor && id != MineStoreCursor && id != MineOreCursor
            && id != LumberFromCursor && id != HopCursor && id != HopItemCursor && id != HopPlaceCursor)
            return false;
        uint s = U32(p, 7);
        int x = (p[11] << 8) | p[12], y = (p[13] << 8) | p[14], z = (sbyte)p[16];
        if (id == HopCursor)
            return HoldHop(k, s, p[1]);
        if (id == HopItemCursor)
            return HoldHopItem(k, s, p[1]);
        if (id == HopPlaceCursor)
            return HoldHopPlace(k, s, p[1], x, y, z);
        if (id == LumberFromCursor)
        {
            if (s == 0 && p[1] == 0)
            {
                LumberStatus = "Chop from not set: the cursor was cancelled.";
                return true;
            }
            // An item or a mobile targeted stands for its tile.
            LumberFrom = x + "," + y;
            LumberStatus = LumberFromText() + ".";
            return true;
        }
        if (id == LumberAxeCursor)
        {
            AssistItem? it = s == 0 || p[1] != 0 ? null : Item(k, s);
            if (it == null || !IsAxe(it.Graphic))
            {
                LumberStatus = "Axe not set: target an axe in your pack or hand.";
                return true;
            }
            LumberAxe = s;
            LumberStatus = "Axe: " + UoTiles.Name(it.Graphic) + " (" + s.ToString("X") + ").";
            return true;
        }
        string button = id == MineStoreCursor ? "Store to" : id == MineOreCursor ? "Ore to" : "Logs to";
        if (s == 0 && p[1] == 0)
        {
            LumberStatus = button + " not set: the cursor was cancelled.";
            return true;
        }
        bool mobile;
        lock (k.State.Gate)
            mobile = s != 0 && k.State.Mobiles.ContainsKey(s);
        string spec = p[1] != 0 || s == 0 ? "ground|0|" + x + "|" + y + "|" + z
            : mobile ? "mobile|" + s.ToString("X") : "item|" + s.ToString("X") + "|" + x + "|" + y + "|" + z;
        if (id == MineStoreCursor)
        {
            MineStoreTo = spec;
            ToolbagStatus = PlaceText(spec, button) + ".";
        }
        else if (id == MineOreCursor)
        {
            MineOreTo = spec;
            ToolbagStatus = PlaceText(spec, button) + ".";
        }
        else
        {
            LumberLogsTo = spec;
        }

        LumberStatus = PlaceText(spec, button) + ".";
        return true;
    }
    AssistItem? LumberAxeNow(GameLink k)
    {
        return GatherToolNow(k, m_lumberGather);
    }
    // The gathering's tool: the one set, in the pack or in hand; null when there is none.
    AssistItem? GatherToolNow(GameLink k, Gather g)
    {
        uint s = g.Tool;
        if (s == 0)
            return null;
        lock (k.State.Gate)
        {
            AssistItem? it = Item(k, s);
            if (it == null)
                return null;
            if (it.Layer >= 0 && it.Mobile == k.State.Player)
                return it;
            return PackTree(k).Contains(it.Container) ? it : null;
        }
    }
    public string? LumberEquip(GameLink k, AssistItem axe)
    {
        return GatherEquip(k, m_lumberGather, axe);
    }
    // The tool into the hand its tile names, whatever else is held put in the pack first. Null, or why not. Where the
    // tool lies is read now, not from the item the caller found before its walk: a recall on the way put the axe in the
    // pack and the chop went out with it there (2026-10-23 22:33:45, "the server says the axe is not in hand").
    string? GatherEquip(GameLink k, Gather g, AssistItem? tool)
    {
        tool = GatherToolNow(k, g);
        if (tool is null)
            return "the " + g.ToolName + " is no longer in the pack or hand";
        if (tool.Layer >= 0)
            return null;
        uint me, pack;
        lock (k.State.Gate)
        {
            me = k.State.Player;
            pack = k.State.Backpack;
        }
        List<UoTiles.Tile> items = UoTiles.Items();
        int layer = tool.Graphic < items.Count && items[tool.Graphic].Layer > 0 ? items[tool.Graphic].Layer : 2;
        List<uint> held = new();
        lock (k.State.Gate)
            foreach (AssistItem it in k.State.Items.Values)
                if ((it.Layer == 1 || it.Layer == 2) && it.Mobile == me && it.Serial != tool.Serial)
                    held.Add(it.Serial);
        using (Act(k))
        {
            foreach (uint h in held)
            {
                string? w = MoveStack(k, h, 1, "the held item " + h.ToString("X"), "Putting the held item away",
                    () => k.Drop(h, pack)!);
                if (w != null)
                    return w;
            }
            string? why = BusyLift(k, tool.Serial, 1);
            if (why != null)
                return "lifting the " + g.ToolName + ": " + why;
            why = k.Equip(tool.Serial, layer, me);
            if (why != null)
                return Stopped(g.Key, tool.Serial, why);
            Answered(k, AnswerMs);
        }
        Wait.Until(Clock, () => { AssistItem? a = Item(k, tool.Serial); return a != null && a.Layer >= 0; }, AnswerMs,
            "the " + g.ToolName + " in hand", stepMs: 50);
        AssistItem? now = Item(k, tool.Serial);
        return now != null && now.Layer >= 0 ? null : "the " + g.ToolName + " did not go into your hand";
    }
    // One chop of the tree static at x, y, z: the axe used, its cursor aimed at the tree, the answer awaited.
    public ChopAnswer LumberChop(GameLink k, uint axe, int x, int y, int z, int graphic)
    {
        Heard mark;
        using (Act(k))
        {
            mark = new(k.State);
            if (k.Use(axe, true) != null)
                return ChopAnswer.None;
            m_lastUse = Clock.UtcNow;
            AwaitCursor(k, Clock.UtcNow.AddMilliseconds(MineCursorMs));
            if (!k.MacroCursor)
            {
                k.Settle();
                foreach (string j in mark.Answers())
                {
                    ChopAnswer a = ChopSaid(j);
                    if (a != ChopAnswer.None)
                        return a;
                }
                return ChopAnswer.None;
            }
            if (k.Target(true, 0, x, y, z, graphic) != null)
                return ChopAnswer.None;
            k.Settle();
        }
        DateTime until = Clock.UtcNow.AddMilliseconds(Ms("chop-wait"));
        while (Clock.UtcNow < until && !k.Closed && !circuitStop)
        {
            foreach (string j in mark.Answers())
            {
                ChopAnswer a = ChopSaid(j);
                if (a != ChopAnswer.None)
                    return a;
            }
            Clock.Sleep(250);
        }
        return ChopAnswer.None;
    }
    public string? LumberStore(GameLink k)
    {
        return GatherStore(k, m_lumberGather);
    }
    // Every stack of the gathering's goods in the pack onto its place, or at feet (PutStacks). Null, or why not.
    string? GatherStore(GameLink k, Gather g)
    {
        bool feet = g.AtFeet;
        m_putAt = null;
        int put;
        string? w;
        try
        {
            w = PutStacks(k, feet ? "feet|0" : g.PlaceSpec, g.Gathered, g.Goods, g.PlaceButton, out put);
            g.Stored += put;
            // Goods put on the collection's own place within reach are collected already.
            int cx, cy, cz;
            uint co;
            bool atPlace = m_putAt != null && CollectPlace(k, g, out cx, out cy, out cz, out co) == null
                && m_putAt[0] == cx && m_putAt[1] == cy;
            if (put > 0 && feet && m_putAt != null && !atPlace)
                g.AddPile(m_putAt[0], m_putAt[1], put);
        }
        finally { m_puttingAt = null; }
        // No tile beside took them: the goods carried to the place and the drops so far collected there, and the
        // gathering goes on (Damian, 2026-10-22: "the circuit should not stop for a failure like this, it should run
        // the logs back to the drop spot, then begin collecting previous drops").
        if (w != null && feet && w.Contains(PutBack))
        {
            Note(g.Title + ": no tile beside you took the " + g.Goods + " (" + w + "); carrying them to "
                + PlaceText(g.PlaceSpec, g.PlaceButton) + ", then collecting the drops");
            int carried;
            w = PutStacks(k, g.PlaceSpec, g.Gathered, g.Goods, g.PlaceButton, out carried);
            g.Stored += carried;
            put += carried;
            if (w != null)
                return w;
            if (g.Piles().Length > 0)
            {
                string? cw = GatherCollect(k, g);
                if (cw != null)
                    return "the collection: " + cw;
            }
        }
        if (w == null)
            Note(g.Title + ": " + put + " " + g.Goods + " put down");
        return w;
    }
    // The piles dropped at feet, waiting for the collection (Gather's pile record), kept across restarts; Collect at
    // the total that makes the collection due. Each pile is x, y, logs, wild (1) or ours (0).
    public int LumberCollectAt
    {
        get => m_lumberGather.CollectAt;
        set => m_lumberGather.CollectAt = value;
    }
    public List<int[]> LumberPiles()
    {
        List<int[]> l = new();
        foreach (GatherPile p in m_lumberGather.Piles())
            l.Add(new[] { p.X, p.Y, p.Amount, p.Wild ? 1 : 0 });
        return l;
    }
    // Every resource pile seen lying on the open ground, for the map (Damian, 2026-10-23: "when logs are encountered in
    // the wild, like a previously collected spot, it should be marked on the map always as if we collected it
    // ourselves, although mark it as wild logs"; "add resources on the showtypes for ores, logs, feathers, hides,
    // reagents"; "they should timeout in accord with server rules for decay and should be rechecked if we wander back
    // in range and removed if gone. we should remove them from the map if we store them. we should not look in houses
    // of anykind"). res.seen holds "x,y,kind,amount,ticks,stones" separated by ";" (kind an index of ResKinds, ticks
    // when first seen at that amount), SeenKeep at most, the newest kept. A pile inside a house is never kept. A tile
    // within SeenView of the player with no pile of the kind for SeenGoneMs leaves it (a fresh view fills over a few
    // seconds; a pile we lift goes the same way). A pile under DecayFreeStones leaves DecayMs after it was first seen
    // at its amount: OUO world.c InitDecay mode 1, 72 visits of the decay scan at about 150 s, 3 hours from the last
    // move (item.c CItem_DecayTick); heavier never decays (its weight gate).
    static readonly ImmutableArray<string> s_resKinds = [ "Logs", "Ore", "Feathers", "Hides", "Reagents" ];
    public static ImmutableArray<string> ResKinds => s_resKinds;
    public const int SeenKeep = 1024, SeenView = 14, SeenGoneMs = 10000, DecayFreeStones = 0xFF;
    public const long DecayMs = 3L * 3600 * 1000;
    public static int ResKind(int g)
    {
        if (IsLog(g))
            return 0;
        if (IsOre(g))
            return 1;
        if (g == 0x1BD1 || g == 0x1BD2)
            return 2;
        if (g == 0x1078 || g == 0x1079)
            return 3;
        if (ReagentName(g) != null)
            return 4;
        return -1;
    }
    sealed class SeenPile
    {
        public int X, Y, Kind, Amount, Weight; public long At;
    }
    readonly Lock m_seenGate = new(); Map<long, SeenPile> m_seenNow = null!;
    ValueMap<long, DateTime> m_seenMissing = ValueMap<long, DateTime>.Empty;
    static long SeenKey(int x, int y, int kind)
    {
        return TileKey(x, y) | ((long)kind << 40);
    }
    Map<long, SeenPile> SeenLoad()
    {
        if (m_seenNow != null)
            return m_seenNow;
        Map<long, SeenPile> d = Map<long, SeenPile>.Empty;
        foreach (string e in Cfg.Get(Server + "res.seen", "").Split(new[] { ';' },
            StringSplitOptions.RemoveEmptyEntries))
        {
            string[] f = e.Split(',');
            int x, y, kind, n, w;
            long at;
            if (f.Length == 6 && int.TryParse(f[0], out x) && int.TryParse(f[1], out y) && int.TryParse(f[2], out kind)
                && kind >= 0 && kind < s_resKinds.Length && int.TryParse(f[3], out n) && long.TryParse(f[4], out at)
                && int.TryParse(f[5], out w))
                d = d.Set(SeenKey(x, y, kind), new SeenPile { X = x, Y = y, Kind = kind, Amount = n, At = at,
                    Weight = w });
        }
        return m_seenNow = d;
    }
    // The seen record brought up to what lies in view now. Every caller holds seenGate.
    void SeenSweep()
    {
        Map<long, SeenPile> d = SeenLoad();
        GameLink k = m_link;
        DateTime now = Clock.UtcNow;
        bool changed = false;
        Map<long, SeenPile> here = Map<long, SeenPile>.Empty;
        if (k != null && !k.Closed)
        {
            int px, py;
            List<UoTiles.Tile> items = UoTiles.Items();
            lock (k.State.Gate)
            {
                px = k.State.X;
                py = k.State.Y;
                foreach (AssistItem g in k.State.Ground.Values)
                {
                    if (g.Container != 0)
                        continue;
                    int kind = ResKind(g.Graphic);
                    if (kind < 0)
                        continue;
                    long key = SeenKey(g.X, g.Y, kind);
                    SeenPile? p;
                    if (!here.TryGet(key, out p))
                        here = here.Set(key, p = new SeenPile { X = g.X, Y = g.Y, Kind = kind });
                    int n = Math.Max(1, g.Amount);
                    p.Amount += n;
                    p.Weight += n * (items.Count > g.Graphic ? Math.Max(1, items[g.Graphic].Weight) : 2);
                }
            }
            if ((px | py) != 0)
            {
                foreach (KeyValuePair<long, SeenPile> kv in here)
                {
                    m_seenMissing = m_seenMissing.Remove(kv.Key);
                    if (LumberHoused(kv.Value.X, kv.Value.Y))
                    {
                        if (d.ContainsKey(kv.Key))
                        {
                            d = d.Remove(kv.Key);
                            changed = true;
                        }
                        continue;
                    }
                    SeenPile? was;
                    if (d.TryGet(kv.Key, out was) && was.Amount == kv.Value.Amount)
                    {
                        if (was.Weight != kv.Value.Weight)
                        {
                            was.Weight = kv.Value.Weight;
                            changed = true;
                        }
                        continue;
                    }
                    kv.Value.At = now.Ticks;
                    d = d.Set(kv.Key, kv.Value);
                    changed = true;
                }
                foreach (KeyValuePair<long, SeenPile> kv in d)
                {
                    long key = kv.Key;
                    if (here.ContainsKey(key))
                        continue;
                    SeenPile p = kv.Value;
                    if (Tiles(px, py, p.X, p.Y) > SeenView)
                    {
                        m_seenMissing = m_seenMissing.Remove(key);
                        continue;
                    }
                    DateTime since;
                    if (!m_seenMissing.TryGet(key, out since))
                    {
                        m_seenMissing = m_seenMissing.Set(key, now);
                        continue;
                    }
                    if ((now - since).TotalMilliseconds >= SeenGoneMs)
                    {
                        d = d.Remove(key);
                        m_seenMissing = m_seenMissing.Remove(key);
                        changed = true;
                    }
                }
            }
        }
        // A pile lying in view is there whatever its age; the decay is a guess about piles out of view.
        foreach (KeyValuePair<long, SeenPile> kv in d)
        {
            SeenPile p = kv.Value;
            if (!here.ContainsKey(kv.Key) && p.Weight < DecayFreeStones
                && (now.Ticks - p.At) / TimeSpan.TicksPerMillisecond >= DecayMs)
            {
                d = d.Remove(kv.Key);
                changed = true;
            }
        }
        // Past SeenKeep the oldest sighting goes first.
        while (d.Count > SeenKeep)
        {
            long oldest = 0;
            long oldestAt = long.MaxValue;
            foreach (KeyValuePair<long, SeenPile> kv in d)
                if (kv.Value.At < oldestAt)
                {
                    oldestAt = kv.Value.At;
                    oldest = kv.Key;
                }
            d = d.Remove(oldest);
            changed = true;
        }
        m_seenNow = d;
        if (changed)
        {
            List<string> l = new();
            foreach (SeenPile p in d.Values)
                l.Add(p.X + "," + p.Y + "," + p.Kind + "," + p.Amount + "," + p.At + "," + p.Weight);
            Cfg.Set(Server + "res.seen", string.Join(";", l));
        }
    }
    // A pile the assist itself lifted (a hop, a carry, a bunny hop) is no longer on its tile: its sighting goes at
    // once, as the hop's stops, left behind past SeenView before SeenGoneMs, stayed marked wild for good (2026-10-23
    // 15:49: 30 of them, a 760-log pile at two stops).
    // A lift of a resource lying on the ground, the player's or the pickup's, takes its sighting too: walked past, its
    // tile left view before SeenGoneMs, and the reagents the pickup took stayed marked (Damian, 2026-10-23: "a lot of
    // these marked reagent spots seem like false positives"; 71 single reagents kept, the pickup's lifts of 47091893
    // and others at 17:53). A refused lift leaves the item in view, and the next sweep marks it again. Never holds the
    // packet: false always.
    bool SeenLifted(GameLink k, byte[] p)
    {
        if (p[0] != 0x07 || p.Length < 5)
            return false;
        uint s = U32(p, 1) & 0x7FFFFFFF;
        AssistItem? g;
        if (!k.State.Ground.TryGet(s, out g))
            g = null;
        if (g != null)
            SeenForget(g.X, g.Y, g.Graphic);
        return false;
    }
    // A resource the server deletes from a tile within SeenView, with none of its kind left there, is gone now, not
    // SeenGoneMs later: uo1998 lays a chop's logs on the ground about 120 ms (0x1A with no amount, 0x1D, again with the
    // amount, 0x1D) before they join the pack's stack, and a sweep inside that window drew "wild 1" (Damian,
    // 2026-10-24). A delete farther off is the server's view range, and the sighting stays. The item is the state's
    // GroundGone.
    void SeenServer(GameLink k, byte[] p)
    {
        if (p[0] != 0x1D)
            return;
        uint s = U32(p, 1);
        AssistItem gone;
        int kind, px, py;
        bool left = false;
        lock (k.State.Gate)
        {
            gone = k.State.GroundGone;
            if (gone == null || gone.Serial != s || (kind = ResKind(gone.Graphic)) < 0)
                return;
            px = k.State.X;
            py = k.State.Y;
            foreach (AssistItem g in k.State.Ground.Values)
                if (g.X == gone.X && g.Y == gone.Y && ResKind(g.Graphic) == kind)
                {
                    left = true;
                    break;
                }
        }
        if (!left && Tiles(px, py, gone.X, gone.Y) <= SeenView)
            SeenForget(gone.X, gone.Y, gone.Graphic);
    }
    void SeenForget(int x, int y, int graphic)
    {
        int kind = ResKind(graphic);
        if (kind < 0)
            return;
        lock (m_seenGate)
        {
            Map<long, SeenPile> d = SeenLoad();
            long key = SeenKey(x, y, kind);
            if (!d.ContainsKey(key))
                return;
            m_seenNow = d = d.Remove(key);
            m_seenMissing = m_seenMissing.Remove(key);
            List<string> l = new();
            foreach (SeenPile p in d.Values)
                l.Add(p.X + "," + p.Y + "," + p.Kind + "," + p.Amount + "," + p.At + "," + p.Weight);
            Cfg.Set(Server + "res.seen", string.Join(";", l));
        }
    }
    // The map's log piles: the record's (x, y, logs, wild 1 or ours 0), then every log pile seen that the record lacks,
    // wild. The pile on the collection's place is ours, not wild.
    public List<int[]> LumberMapPiles()
    {
        List<int[]> l = LumberPiles();
        int cx = -1, cy = -1, cz;
        uint co;
        GameLink k = m_link;
        if (k != null && CollectPlace(k, out cx, out cy, out cz, out co) != null)
        {
            cx = cy = -1;
        }
        int[]? putting = m_puttingAt;
        lock (m_seenGate)
        {
            SeenSweep();
            foreach (SeenPile p in SeenLoad().Values)
                if (p.Kind == 0 && !l.Exists(r => r[0] == p.X && r[1] == p.Y))
                    l.Add(new[] { p.X, p.Y, p.Amount,
                        p.X == cx && p.Y == cy || putting != null && p.X == putting[0] && p.Y == putting[1] ? 0 : 1 });
        }
        return l;
    }
    // The map's other resource piles seen: x, y, kind (1 ore to 4 reagents), amount.
    public List<int[]> ResourceSpots()
    {
        List<int[]> l = new();
        lock (m_seenGate)
        {
            SeenSweep();
            foreach (SeenPile p in SeenLoad().Values)
                if (p.Kind > 0)
                    l.Add(new[] { p.X, p.Y, p.Kind, p.Amount });
        }
        return l;
    }
    public void LumberPileAdd(int x, int y, int logs, bool wild = false)
    {
        m_lumberGather.AddPile(x, y, logs, wild);
    }
    public int LumberDropped()
    {
        return m_lumberGather.Dropped();
    }
    public int LumberWild()
    {
        return m_lumberGather.Wild();
    }
    public void LumberPilesClear()
    {
        m_lumberGather.ClearPiles();
    }
    void LumberPilesSet(List<int[]> l)
    {
        m_lumberGather.SetPiles(l.ConvertAll(e => new GatherPile(e[0], e[1], e[2], e[3] == 1)));
    }
    public void LumberPileMove(int fx, int fy, int tx, int ty, bool gone)
    {
        m_lumberGather.MovePile(fx, fy, tx, ty, gone);
    }
    // Log discovery (Damian, 2026-10-23: "as logs are encountered on the ground, add them to the collection task as
    // wild logs, probably chopped by us previously and forgotten"): every log pile lying in view on a tile the record
    // does not hold joins it as wild, but at the collection's place and inside a house (a house's logs are someone's
    // storage). Only while there is a collection to carry them. A pile found inside a house is not asked again
    // (HousedMax at most kept); a forgotten pile (New chop) is found again.
    public static bool HousedOnMap(int x, int y)
    {
        return UoMap.HouseAround(x, y) != null;
    }
    public Func<int, int, bool> LumberHoused = HousedOnMap;
    public int HousedMax = 4096;
    Set<uint> m_lumberHoused = Set<uint>.Empty;
    // The log piles in view at a New chop, the old chop's drops (lumber.old, hex serials separated by ","), kept across
    // restarts so that a Resume after a bounce still knows them; HousedMax at most.
    Set<uint> m_lumberOldSet = null!; Set<uint> lumberOld
    {
        get
        {
            if (m_lumberOldSet == null)
            {
                Set<uint> s = Set<uint>.Empty;
                foreach (string e in Cfg.Get(Server + "lumber.old", "").Split(new[] { ',' },
                    StringSplitOptions.RemoveEmptyEntries))
                {
                    uint v;
                    if (uint.TryParse(e, NumberStyles.HexNumber, null, out v))
                        s = s.Add(v);
                }
                m_lumberOldSet = s;
            }
            return m_lumberOldSet;
        }
        set
        {
            m_lumberOldSet = value;
            List<string> l = new();
            foreach (uint v in value)
                l.Add(v.ToString("X"));
            Cfg.Set(Server + "lumber.old", string.Join(",", l));
        }
    }
    public int LumberDiscover(GameLink k)
    {
        int dx, dy, dz;
        uint onto;
        if (CollectPlace(k, out dx, out dy, out dz, out onto) != null)
            return 0;
        List<AssistItem> found = new();
        lock (k.State.Gate)
            foreach (AssistItem g in k.State.Ground.Values)
                if (g.Container == 0 && IsLog(g.Graphic) && g.Serial != onto && !m_lumberHoused.Contains(g.Serial)
                    && !lumberOld.Contains(g.Serial))
                    found.Add(g);
        if (found.Count == 0)
            return 0;
        List<int[]> piles = LumberPiles();
        int added = 0, logs = 0;
        foreach (AssistItem g in found)
        {
            if ((g.X == dx && g.Y == dy) || piles.Exists(p => p[0] == g.X && p[1] == g.Y))
                continue;
            if (LumberHoused(g.X, g.Y))
            {
                if (m_lumberHoused.Count >= HousedMax)
                    m_lumberHoused = Set<uint>.Empty;
                m_lumberHoused = m_lumberHoused.Add(g.Serial);
                continue;
            }
            int n = Math.Max(1, g.Amount);
            piles.Add(new[] { g.X, g.Y, n, 1 });
            added++;
            logs += n;
        }
        if (added == 0)
            return 0;
        LumberPilesSet(piles);
        Note("Lumber: found " + logs + " wild logs in " + added + " pile" + (added == 1 ? "" : "s")
            + "; the collection carries them too");
        return added;
    }
    void LumberPileRemove(int x, int y)
    {
        m_lumberGather.RemovePile(x, y);
    }
    // At my feet (Damian, 2026-10-22: "drop at feet and bunnyhop later").
    public bool LumberAtFeet
    {
        get => m_lumberGather.AtFeet;
        set => m_lumberGather.AtFeet = value;
    }
    // A hop lifts and drops within LiftReach of where the player stands. Up to CollectPlannedMax nodes the tree weighs
    // each pair by its planned route; past that by the tiles between, so the plans stay bounded (a route per pair is
    // quadratic in the piles).
    public const int HopReach = LiftReach, CollectPlannedMax = 4;
    // A route of tiles (x, y, z each) a pile is carried along from one tile to another, the first its own, null where
    // none: the walk planner's, or a test's own.
    public Func<int, int, int, int, int[][]?>? CollectRoute = null;
    // The walk onto a tile to stand on for a hop: WalkTo's, or a test's own. Null, or why not.
    public Func<int, int, string>? CollectWalk = null!;
    int[][]? PlannedRoute(int fx, int fy, int tx, int ty)
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return null;
        int pz;
        lock (k.State.Gate)
            pz = k.State.Z;
        int fz = UoMap.StandZ(fx, fy, pz);
        if (fz == int.MinValue)
            return null;
        List<int>? dirs = PlanPath(k, fx, fy, fz, tx, ty, 0, Set<long>.Empty);
        if (dirs == null)
            return null;
        int[][] tiles = new int[dirs.Count + 1][];
        tiles[0] = new[] { fx, fy, fz };
        int x = fx, y = fy, z = fz;
        for (int i = 0; i < dirs.Count; i++)
        {
            x += s_dX[dirs[i]];
            y += s_dY[dirs[i]];
            int nz = UoMap.StandZ(x, y, z);
            if (nz != int.MinValue)
                z = nz;
            tiles[i + 1] = new[] { x, y, z };
        }
        return tiles;
    }
    string? CollectPlace(GameLink k, out int x, out int y, out int z, out uint onto)
    {
        return CollectPlace(k, m_lumberGather, out x, out y, out z, out onto);
    }
    // Where the collection carries the piles (Gather.CollectPlace), an item's tile where it lies now. Null, or why
    // none.
    string? CollectPlace(GameLink k, Gather g, out int x, out int y, out int z, out uint onto)
    {
        return g.CollectPlace(s => { AssistItem? it = ItemOrGround(k, s); return it != null && it.Container == 0
            ? new[] { it.X, it.Y } : null; }, out x, out y, out z, out onto);
    }
    static AssistItem? LogPileAt(GameLink k, int x, int y, uint except = 0)
    {
        return PileAt(k, IsLog, x, y, except);
    }
    // The pile of art lying on a tile, other than except; null when none.
    static AssistItem? PileAt(GameLink k, Func<int, bool> art, int x, int y, uint except = 0)
    {
        lock (k.State.Gate)
            foreach (AssistItem g in k.State.Ground.Values)
                if (g.Container == 0 && g.X == x && g.Y == y && g.Serial != except && art(g.Graphic))
                    return g;
        return null;
    }
    static List<AssistItem> PilesAt(GameLink k, Func<int, bool> art, int x, int y)
    {
        List<AssistItem> l = new();
        lock (k.State.Gate)
            foreach (AssistItem g in k.State.Ground.Values)
                if (g.Container == 0 && g.X == x && g.Y == y && art(g.Graphic))
                    l.Add(g);
        return l;
    }
    // A pile at x, y the stack merges into: its own art and hue, not itself.
    static AssistItem? SamePileAt(GameLink k, AssistItem stack, int x, int y)
    {
        lock (k.State.Gate)
            foreach (AssistItem g in k.State.Ground.Values)
                if (g.Container == 0 && g.X == x && g.Y == y && g.Serial != stack.Serial && g.Graphic == stack.Graphic
                    && g.Hue == stack.Hue)
                    return g;
        return null;
    }
    public volatile string CollectStatus = "";
    bool PackLogs(GameLink k)
    {
        return PackHolds(k, IsLog);
    }
    bool PackHolds(GameLink k, Func<int, bool> art)
    {
        lock (k.State.Gate)
        {
            Set<uint> inside = PackTree(k);
            foreach (AssistItem it in k.State.Items.Values)
                if (inside.Contains(it.Container) && art(it.Graphic))
                    return true;
        }
        return false;
    }
    public string? LumberCollect(GameLink k)
    {
        return GatherCollect(k, m_lumberGather);
    }
    // The piles at feet carried to the place: the minimum spanning tree over the place and the piles (PileTree), leaves
    // first, each pile hopped along its route (Bunnyhop) onto the pile it merges into. The pile record follows every
    // hop, so a collection that ends early leaves it true. Null, or why not.
    string? GatherCollect(GameLink k, Gather g)
    {
        int dx, dy, dz;
        uint onto;
        string? pw = CollectPlace(k, g, out dx, out dy, out dz, out onto);
        if (pw != null)
            return pw;
        // The pack's goods put down first, at feet, the first leaf, so that every pile carried rides alone (Damian,
        // 2026-10-23: "assuming the beginning of the collection drops the logs in the pack first").
        if (g.AtFeet && PackHolds(k, g.Gathered))
        {
            string? sw = GatherStore(k, g);
            if (sw != null)
                return "putting the pack's " + g.Goods + " down first: " + sw;
        }
        List<GatherPile> piles = new(g.Piles());
        if (piles.Count == 0)
            return null;
        Func<int, int, int, int, int[][]?> route = CollectRoute ?? PlannedRoute;
        int n = piles.Count + 1;
        int[][] at = new int[n][];
        at[0] = new[] { dx, dy };
        for (int i = 0; i < piles.Count; i++)
            at[i + 1] = new[] { piles[i].X, piles[i].Y };
        // A planned route costs 1 to 4 s in a forest (2026-10-22 02:46:37 to 02:47:39: 16 places, 120 pairs, about 5
        // minutes with nothing said), so pairs are planned only for a few places; past that each hop plans its own
        // route.
        bool planned = n <= CollectPlannedMax;
        int[,] dist = new int[n, n];
        CircuitStatus = g.Title + ": planning the collection of " + piles.Count + " piles";
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                int d;
                if (!planned)
                {
                    d = Tiles(at[i][0], at[i][1], at[j][0], at[j][1]);
                }
                else
                {
                    CollectStatus = "planning the routes between " + n + " places";
                    int[][]? r = route(at[i][0], at[i][1], at[j][0], at[j][1]);
                    d = r == null ? PileTree.Unreached : r.Length - 1;
                }
                dist[i, j] = dist[j, i] = d;
            }
        }
        // A stop during the planning leaves routes missing that were never planned: no pile is left as "no way".
        if (circuitStop || k.Closed)
            return "stopped";
        PileTree tree = PileTree.Build(dist);
        int sx, sy;
        lock (k.State.Gate)
        {
            sx = k.State.X;
            sy = k.State.Y;
        }
        int[] fromStart = new int[n];
        int[,] apart = new int[n, n];
        for (int i = 0; i < n; i++)
        {
            fromStart[i] = Tiles(sx, sy, at[i][0], at[i][1]);
            for (int j = 0; j < n; j++)
                apart[i, j] = Tiles(at[i][0], at[i][1], at[j][0], at[j][1]);
        }
        ImmutableArray<int> order = tree.NearestFirst(fromStart, apart);
        int total = g.Dropped(), moved = 0, hopLeft = 0;
        List<string> hopKept = new();
        Note(g.Title + ": collecting " + total + " " + g.Goods + " in " + piles.Count + " piles to " + dx + "," + dy
            + (planned ? "" : " (the tree by tiles: over " + CollectPlannedMax + " places)"));
        foreach (int v in order)
        {
            if (circuitStop || k.Closed)
                return "stopped";
            int p = tree.Parent[v];
            CollectStatus = "the pile at " + at[v][0] + "," + at[v][1] + " to "
                + (p == 0 ? "the place " : "the pile at ") + at[p][0] + "," + at[p][1] + " (" + (++moved) + " of "
                + order.Length + ")";
            CircuitStatus = g.Title + ": collecting, " + CollectStatus;
            string? w = HopPile(k, g, at[v][0], at[v][1], at[p][0], at[p][1], p == 0 ? onto : 0, p == 0, route);
            // Goods gone (another took them, or the player moved them) are passed, wild or ours (Damian, 2026-10-23:
            // "not finding a remembered drop is no reason to stop the collection").
            if (w != null && w.StartsWith(NoPileAt))
            {
                hopLeft += piles[v - 1].Amount;
                Note(g.Title + ": the " + (piles[v - 1].Wild ? "wild " : "") + g.Goods + " at " + at[v][0] + ","
                    + at[v][1] + " are gone; passed");
                continue;
            }
            // A hop with no way (the tree weighed by tiles plans no route first) is passed as the tree's unreached are:
            // wild goods forgotten, ours kept (2026-10-23 14:32:59, wild logs at 1652,1673 ended a collection of 951).
            if (w != null && w.StartsWith("no way"))
            {
                hopLeft += piles[v - 1].Amount;
                if (piles[v - 1].Wild)
                {
                    g.RemovePile(at[v][0], at[v][1]);
                    Note(g.Title + ": no way from the wild " + g.Goods + " at " + at[v][0] + "," + at[v][1] + " (" + w
                        + "); forgotten");
                }
                else
                {
                    hopKept.Add(at[v][0] + "," + at[v][1]);
                    Note(g.Title + ": no way from the pile at " + at[v][0] + "," + at[v][1] + " (" + w
                        + "); kept in the record");
                }
                continue;
            }
            if (w != null)
            {
                CollectStatus = "ended: " + w;
                return "the pile at " + at[v][0] + "," + at[v][1] + ": " + w;
            }
        }
        // A pile no route reaches is passed and the rest collected (Damian's finding of 2026-10-22, the pile left in
        // Moonglow when the player moved to Minoc): wild logs leave the record, ours stay in it.
        List<string> left = hopKept;
        int leftLogs = hopLeft;
        for (int i = 1; i < n; i++)
        {
            if (tree.Parent[i] >= 0)
                continue;
            string where = at[i][0] + "," + at[i][1];
            leftLogs += piles[i - 1].Amount;
            if (piles[i - 1].Wild)
            {
                g.RemovePile(at[i][0], at[i][1]);
                Note(g.Title + ": no way from the wild " + g.Goods + " at " + where + " to " + dx + "," + dy
                    + "; forgotten");
            }
            else
            {
                left.Add(where);
                Note(g.Title + ": no way from the pile at " + where + " to " + dx + "," + dy + "; kept in the record");
            }
        }
        CollectStatus = "done: " + (total - leftLogs) + " " + g.Goods + " to " + dx + "," + dy
            + (left.Count > 0 ? "; no way from " + string.Join(" ", left) : "");
        Note(g.Title + ": collected " + (total - leftLogs) + " " + g.Goods + " to " + dx + "," + dy);
        return null;
    }
    // One pile carried from fx, fy to tx, ty by hops along its route, the player standing still for each lift and drop
    // so the weight never slows a step; the last drop onto onto when set, else onto the pile at tx, ty, else the
    // ground. gone: tx, ty is the collection's place, and the pile leaves the record there. item: the one item hopped,
    // lying where the pile is looked for, in place of the log pile there; with it the record is not touched and nothing
    // merges on the way. Null, or why not.
    const string NoPileAt = "no pile lies at ";
    string? HopPile(GameLink k, Gather g, int fx, int fy, int tx, int ty, uint onto, bool gone,
        Func<int, int, int, int, int[][]?> route, uint item = 0)
    {
        int[][]? path = route(fx, fy, tx, ty);
        if (path == null)
            return "no way to " + tx + "," + ty;
        // A route through a teleporter or a gate restarts past it (Route), so its end is not the place: a pile does not
        // go through one.
        int[] end = path[path.Length - 1];
        if (end[0] != tx || end[1] != ty)
            return "the way to " + tx + "," + ty + " crosses a teleporter";
        Func<int, int, string> walk = CollectWalk ?? ((x, y) => WalkTo(x, y, 0)!);
        if (item == 0 && g.Carries && path.Length - 1 > HopReach)
        {
            string? cw;
            if (CarryPile(k, g, fx, fy, tx, ty, onto, gone, path, walk, out cw))
                return cw;
        }
        ImmutableArray<PileHop> hops = Bunnyhop.Plan(path.Length - 1, HopReach);
        if (hops.Length == 0)
        {
            if (onto == 0)
            {
                if (item == 0)
                    g.MovePile(fx, fy, tx, ty, gone);
                return null;
            }
            hops = ImmutableArray.Create(new PileHop(0, 0, 0));
        }
        foreach (PileHop h in hops)
        {
            if (circuitStop || k.Closed)
                return "stopped";
            int[] stand = path[h.Stand], lift = path[h.Lift], drop = path[h.Drop];
            int px, py;
            lock (k.State.Gate)
            {
                px = k.State.X;
                py = k.State.Y;
            }
            // No walk when the lift and the drop are both in reach already; a stand on the pile's own tile that no walk
            // reaches (the planner counts a pile there as a blocker) is taken beside it (2026-10-25 16:48:57, the ore at
            // 2439,91 one tile from Ore to: "no way ... to within 0 of 2439,91").
            bool inReach = Tiles(px, py, lift[0], lift[1]) <= HopReach && Tiles(px, py, drop[0], drop[1]) <= HopReach;
            if (!inReach && (px != stand[0] || py != stand[1]))
            {
                string ww = walk(stand[0], stand[1]);
                if (ww != null && h.Stand == h.Lift && CollectWalk == null)
                {
                    string? near = WalkTo(stand[0], stand[1], 1);
                    int qx, qy;
                    lock (k.State.Gate)
                    {
                        qx = k.State.X;
                        qy = k.State.Y;
                    }
                    if (near == null && Tiles(qx, qy, lift[0], lift[1]) <= HopReach
                        && Tiles(qx, qy, drop[0], drop[1]) <= HopReach)
                        ww = null!;
                }
                if (ww != null)
                    return "walking to " + stand[0] + "," + stand[1] + ": " + ww;
            }
            if (circuitStop || k.Closed)
                return "stopped";
            List<AssistItem> piles = new();
            if (item != 0)
            {
                // An item targeted out of view comes back into view once the walk reaches it: the server's send is
                // awaited.
                if (ItemOrGround(k, item) == null)
                    Wait.Until(Clock, () => ItemOrGround(k, item) != null, AnswerMs, "the item in view", stepMs: 50);
                AssistItem? one = ItemOrGround(k, item);
                if (one == null || one.Container != 0 || one.X != lift[0] || one.Y != lift[1])
                    return "the item " + item.ToString("X") + " is not at " + lift[0] + "," + lift[1];
                piles.Add(one);
            }
            else
            {
                // Every stack of the goods on the tile goes with the hop: ore piles of two arts lie side by side, not
                // merged, and the record moves them both.
                piles = PilesAt(k, g.Gathered, lift[0], lift[1]);
                if (piles.Count == 0)
                {
                    g.RemovePile(lift[0], lift[1]);
                    return NoPileAt + lift[0] + "," + lift[1] + " (taken from the record)";
                }
            }
            bool last = h.Drop == path.Length - 1;
            foreach (AssistItem pile in piles)
            {
                uint into = last && onto != 0 ? onto : 0;
                // Onto a pile only of its own art and hue: uo1998 put large ore (19B9) dropped onto small (19B7) back
                // where it lay (2026-10-25 16:15:53, 2440,92), as stacks of two arts do not merge.
                if (into == 0 && item == 0)
                {
                    AssistItem? under = SamePileAt(k, pile, drop[0], drop[1]);
                    if (under != null)
                        into = under.Serial;
                }
                uint serial = pile.Serial;
                int amount = Math.Max(1, pile.Amount);
                string? why;
                using (Act(k))
                    why = MoveStack(k, serial, amount, "the pile at " + lift[0] + "," + lift[1], "Hopping the pile",
                        () => (into != 0 ? k.Drop(serial, into) : k.DropGround(serial, drop[0], drop[1], drop[2]))!);
                if (why != null)
                    return why;
                Outcome put = Wait.Until(Clock,
                    () => { AssistItem? it = ItemOrGround(k, serial); return it == null ? into != 0
                    : into != 0 ? it.Container == into : it.Container == 0 && it.X == drop[0] && it.Y == drop[1]; },
                    AnswerMs, "the pile put down", stepMs: 50);
                if (!put.Done)
                    return "the pile was not put down at " + drop[0] + "," + drop[1];
                SeenForget(lift[0], lift[1], pile.Graphic);
            }
            if (item == 0)
                g.MovePile(lift[0], lift[1], drop[0], drop[1], last && gone);
            else
                HopStatus = "Hopping: " + h.Drop + " of " + (path.Length - 1) + " tiles";
        }
        return null;
    }
    // A pile whose logs fit in the pack is carried, not hopped (Damian, 2026-10-23: "there needs to be a check during
    // the collection phase that the logs fit in pack before deciding to use bunny hop technique. leaf nodes on the
    // collection will likely fit"): lifted into the pack from beside it, walked along its route to beside tx, ty, and
    // put down there as PutStacks puts a store, onto onto when set, else onto the pile there, else the ground. Fits:
    // the pack's weight with the pile's at most the carry limit; a weight not known yet hops. False when it does not
    // fit (hop it); true with why null, or why not.
    // A pile out of view is judged by the record's amount, walked to, and judged again once it lies in view (Damian,
    // 2026-10-23, of a leaf of 140 logs 43 tiles off hopped from the collection point: "we should be picking that logs
    // up directly and running it. it is a leaf").
    bool CarryPile(GameLink k, Gather g, int fx, int fy, int tx, int ty, uint onto, bool gone, int[][] path,
        Func<int, int, string> walk, out string? why)
    {
        why = null;
        AssistItem? pile = PileAt(k, g.Gathered, fx, fy);
        int amount = pile != null ? Math.Max(1, pile.Amount) : 0;
        if (pile == null)
            foreach (GatherPile e in g.Piles())
                if (e.X == fx && e.Y == fy)
                    amount = e.Amount;
        if (amount <= 0)
            return false;
        List<UoTiles.Tile> items = UoTiles.Items();
        int per = items.Count > g.WeightArt ? Math.Max(1, items[g.WeightArt].Weight) : 2;
        int w;
        uint pack;
        lock (k.State.Gate)
        {
            w = k.State.Weight;
            pack = k.State.Backpack;
        }
        if (w <= 0 || pack == 0 || w + per * amount > MineLimit(k))
            return false;
        int px, py;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
        }
        if (Tiles(px, py, fx, fy) > LiftReach)
        {
            int[] st = path[1];
            string ww = walk(st[0], st[1]);
            if (ww != null)
            {
                why = "walking to " + st[0] + "," + st[1] + ": " + ww;
                return true;
            }
        }
        if (circuitStop || k.Closed)
        {
            why = "stopped";
            return true;
        }
        if (pile == null)
        {
            Wait.Until(Clock, () => PileAt(k, g.Gathered, fx, fy) != null, AnswerMs, "the pile in view", stepMs: 50);
            pile = PileAt(k, g.Gathered, fx, fy);
            if (pile == null)
            {
                g.RemovePile(fx, fy);
                why = NoPileAt + fx + "," + fy + " (taken from the record)";
                return true;
            }
            amount = Math.Max(1, pile.Amount);
            lock (k.State.Gate)
                w = k.State.Weight;
            if (w + per * amount > MineLimit(k))
                return false;
        }
        uint serial = pile.Serial;
        string? mw;
        using (Act(k))
            mw = MoveStack(k, serial, amount, "the pile at " + fx + "," + fy, "Carrying the pile",
                () => k.Drop(serial, pack)!);
        if (mw != null)
        {
            why = mw;
            return true;
        }
        Outcome taken = Wait.Until(Clock,
            () => { lock (k.State.Gate) { AssistItem? it = Item(k, serial); return it == null
            ? PileAt(k, g.Gathered, fx, fy) == null : PackTree(k).Contains(it.Container); } }, AnswerMs,
            "the pile in the pack", stepMs: 50);
        if (!taken.Done)
        {
            why = "the pile at " + fx + "," + fy + " did not go into the pack";
            return true;
        }
        bool wild = false;
        foreach (GatherPile e in g.Piles())
            if (e.X == fx && e.Y == fy && e.Wild)
                wild = true;
        g.RemovePile(fx, fy);
        SeenForget(fx, fy, pile.Graphic);
        Note(g.Title + ": carrying the " + amount + " " + g.Goods + " at " + fx + "," + fy + " to " + tx + "," + ty
            + " in the pack (" + (path.Length - 1) + " tiles)");
        int[] near = path[path.Length - 2];
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
        }
        if (Tiles(px, py, tx, ty) > LiftReach)
        {
            string ww = walk(near[0], near[1]);
            if (ww != null)
            {
                why = "walking to " + near[0] + "," + near[1] + " with the pile in the pack: " + ww;
                return true;
            }
        }
        int[] end = path[path.Length - 1];
        string spec = onto != 0 ? "item|" + onto.ToString("X") + "|" + tx + "|" + ty + "|" + end[2]
            : "ground|0|" + tx + "|" + ty + "|" + end[2];
        int put;
        string? pw = PutStacks(k, spec, g.Gathered, g.Goods, g.PlaceButton, out put);
        if (pw != null)
        {
            why = "putting the pile down at " + tx + "," + ty + ": " + pw;
            return true;
        }
        if (!gone)
            g.AddPile(tx, ty, put, wild);
        return true;
    }
    // ---------- bunny hop to me ----------
    // Damian, 2026-10-23: "under actions, add a "bunny hop target to my location" which records current location, then
    // a target cursor for an object on the ground. then bunny hop that object to the recorded location"; and "max
    // distance should be a thing we figure out based on the pathfinder algorithm run. if its too big/slow we should
    // disallow it. if its no problem to run logs from north mines to trinsic jungles, then fine too". The player's tile
    // kept at the press, then an object cursor; the item targeted, lying on the ground, hopped there along its route as
    // the collection hops a pile (HopPile). No distance cap: the whole route is planned before anything moves, within
    // HopPlanMs. Its own thread; the button again, or any stop, stops it.
    public const uint HopCursor = 0x484F5054;  // "HOPT"
    public const int HopPlanMs = 30000;
    public volatile string HopStatus = "";
    volatile Thread m_hopThread = null!; int[] m_hopTo = null!;
    public bool HopRunning
    {
        get
        {
            Thread t = m_hopThread;
            return t != null && t.IsAlive;
        }
    }
    public string? HopToMe()
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "not connected";
        if (HopRunning)
        {
            circuitStop = true;
            HopStatus = "Stopping";
            return null;
        }
        if (CircuitRunning)
            return "a circuit is running";
        int x, y, z;
        lock (k.State.Gate)
        {
            x = k.State.X;
            y = k.State.Y;
            z = k.State.Z;
        }
        m_hopTo = new[] { x, y, z };
        byte[] p = new byte[19];
        p[0] = 0x6C;
        p[1] = 0;
        Put32(p, 2, HopCursor);
        string? why = k.ToClient(p, "bunny hop cursor");
        HopStatus = why == null ? "Target the object on the ground to hop to " + x + "," + y + "."
            : "No cursor: " + why;
        return why;
    }
    // The Bunny hop's Stop button (Damian, 2026-10-23: "it needs a stop button too"). Null, or why not.
    public string? HopStop()
    {
        if (!HopRunning)
            return "no bunny hop is running";
        circuitStop = true;
        HopStatus = "Stopping";
        return null;
    }
    bool HoldHop(GameLink k, uint s, int kind)
    {
        if (s == 0 && kind == 0)
        {
            HopStatus = "Not hopped: the cursor was cancelled.";
            return true;
        }
        AssistItem? it = s == 0 ? null : ItemOrGround(k, s);
        int[] to = m_hopTo;
        if (it == null || it.Container != 0)
        {
            HopStatus = "Not hopped: target an object lying on the ground.";
            return true;
        }
        if (to == null || CircuitRunning || HopRunning)
        {
            HopStatus = "Not hopped: " + (to == null ? "press the button first" : "something else is moving you");
            return true;
        }
        StartHop(k, s, to);
        return true;
    }
    void StartHop(GameLink k, uint s, int[] to, int[]? from = null)
    {
        ManualStart();
        m_hopThread = NewThread(() => { HopStatus = HopRun(k, s, to, from); Note("Bunny hop: " + HopStatus); });
        m_hopThread.Start();
    }
    // Bunny hop target to target (Damian, 2026-10-23: "make its inverse, target the item first, then target the
    // destination"): an object cursor for the item lying on the ground, then a location cursor for where it goes (a
    // tile, or an object standing for its tile); then hopped as Bunny hop's. The button again while it runs stops it.
    // The item's tile and art are kept at the first target: walking to see a far place takes the item out of view
    // (AssistState prunes past GroundReach), and Damian's second target was refused as "no longer on the ground"
    // (2026-10-23 17:38:26, the log 470E1027 at 1344,1750, the place 1399,1748: "bunny hop second target wont take if
    // its too far away"). An item missing while its kept tile is in view is gone.
    public const uint HopItemCursor = 0x484F5049, HopPlaceCursor = 0x484F5044;  // "HOPI", "HOPD"
    uint m_hopItem; int[]? m_hopItemAt = null;
    public string? HopItemToTarget()
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "not connected";
        if (HopRunning)
        {
            circuitStop = true;
            HopStatus = "Stopping";
            return null;
        }
        if (CircuitRunning)
            return "a circuit is running";
        m_hopItem = 0;
        byte[] p = new byte[19];
        p[0] = 0x6C;
        p[1] = 0;
        Put32(p, 2, HopItemCursor);
        string? why = k.ToClient(p, "bunny hop item cursor");
        HopStatus = why == null ? "Target the object on the ground to hop." : "No cursor: " + why;
        return why;
    }
    bool HoldHopItem(GameLink k, uint s, int kind)
    {
        if (s == 0 && kind == 0)
        {
            HopStatus = "Not hopped: the cursor was cancelled.";
            return true;
        }
        AssistItem? it = s == 0 ? null : ItemOrGround(k, s);
        if (it == null || it.Container != 0)
        {
            HopStatus = "Not hopped: target an object lying on the ground.";
            return true;
        }
        m_hopItem = s;
        m_hopItemAt = new[] { it.X, it.Y, it.Z, it.Graphic };
        byte[] p = new byte[19];
        p[0] = 0x6C;
        p[1] = 1;
        Put32(p, 2, HopPlaceCursor);
        string? why = k.ToClient(p, "bunny hop place cursor");
        HopStatus = why == null ? "Target where the " + UoTiles.Name(it.Graphic) + " goes." : "No cursor: " + why;
        return true;
    }
    bool HoldHopPlace(GameLink k, uint s, int kind, int x, int y, int z)
    {
        uint item = m_hopItem;
        m_hopItem = 0;
        int[]? at = m_hopItemAt;
        m_hopItemAt = null;
        if (s == 0 && kind == 0)
        {
            HopStatus = "Not hopped: the cursor was cancelled.";
            return true;
        }
        if (item == 0)
        {
            HopStatus = "Not hopped: target the object first";
            return true;
        }
        AssistItem? it = ItemOrGround(k, item);
        int px, py;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
        }
        bool outOfView = it == null && at != null && Tiles(px, py, at[0], at[1]) > AssistState.GroundReach;
        if (!outOfView && (it == null || it.Container != 0))
        {
            HopStatus = "Not hopped: the object is no longer on the ground";
            return true;
        }
        if (CircuitRunning || HopRunning)
        {
            HopStatus = "Not hopped: something else is moving you";
            return true;
        }
        StartHop(k, item, new[] { x, y, z }, (outOfView ? at : null)!);
        return true;
    }
    // The item hopped to to; from, its tile and art when it is out of view. The end said in full.
    string HopRun(GameLink k, uint serial, int[] to, int[]? from = null)
    {
        AssistItem? it = ItemOrGround(k, serial);
        if (it == null && from == null)
            return "Not hopped: the item " + serial.ToString("X") + " is gone";
        int fx = it != null ? it.X : from![0], fy = it != null ? it.Y : from![1], graphic = it != null ? it.Graphic
            : from![3];
        string what = UoTiles.Name(graphic) + " (" + serial.ToString("X") + ")";
        if (fx == to[0] && fy == to[1])
            return "Done: the " + what + " is already at " + to[0] + "," + to[1];
        HopStatus = "Planning the way for the " + what + " from " + fx + "," + fy + " to " + to[0] + "," + to[1];
        // The budget is the whole route's plan alone: the walks between stands plan on this thread too, after it.
        int[][]? path;
        DateTime began = Clock.UtcNow;
        s_planBy = began.AddMilliseconds(HopPlanMs);
        s_planOverBudget = false;
        try
        {
            path = (CollectRoute ?? PlannedRoute)(fx, fy, to[0], to[1]);
        }
        finally { s_planBy = DateTime.MinValue; }
        if (path == null)
            return "Not hopped: "
                + (s_planOverBudget ? "the way took past " + HopPlanMs / 1000 + " s to plan; nearer, or in parts"
                : circuitStop ? "stopped" : "no way from " + fx + "," + fy + " to " + to[0] + "," + to[1]);
        Note("Bunny hop: the " + what + ", " + (path.Length - 1) + " tiles planned in "
            + (int)(Clock.UtcNow - began).TotalMilliseconds + " ms");
        string? w = HopPile(k, m_lumberGather, fx, fy, to[0], to[1], 0, false, (a, b, c, d) => path, serial);
        return w == null ? "Done: the " + what + " is at " + to[0] + "," + to[1] : "Stopped: " + w;
    }
    // Every stack in the pack of an art art takes onto a targeted place, spec as LumberLogsTo stores it (Damian,
    // 2026-10-22: "logs piled wherever we target ... tile, bag, pile, floor, wherever. even a packmule"; "packmule for
    // mining is helpful too"): walked to within reach (a pack animal standing within reach needs no walk), then each
    // stack dropped there; a tile with a pile of that art on it takes the stacks onto the pile. what names the stacks
    // and button the setup's button in the answers. Null, or why not; put, how many went.
    const string PutBack = Hands.PutBack;
    // The tile PutStacks last put a stack on. puttingAt: the tile a store at feet is putting stacks on, from before its
    // first drop until LumberStore records the pile, so the map draws the pile there as ours, not wild (Damian,
    // 2026-10-23: "the "wild 1" that happens when you chop sometimes. it disappears").
    int[]? m_putAt = null; volatile int[]? m_puttingAt = null;
    string? PutStacks(GameLink k, string spec, Func<int, bool> art, string what, string button, out int put)
    {
        put = 0;
        string[] t = spec.Split('|');
        if (t.Length < 2)
            return "no place for the " + what + ": target one with " + button;
        uint target = t[1] == "0" ? 0 : Convert.ToUInt32(t[1], 16);
        int tx = 0, ty = 0, tz = 0;
        // At my feet (Damian, 2026-10-22: "drop at feet and bunnyhop later"): beside the player, no walk. uo1998 sends
        // a drop on the player's own tile back into the pack (logs 46EBDCDF at 4717,1127, 2026-10-22 01:35:30; the ore
        // of DumpOre, 2026-10-16), so the stacks go onto a pile of that art beside the player, else the first of the
        // eight tiles beside (Beside), the next one when the server puts a drop back.
        List<int[]> feetTiles = new();
        int feetAt = 0;
        if (t[0] == "feet")
        {
            lock (k.State.Gate)
            {
                int fx = k.State.X, fy = k.State.Y;
                tz = k.State.Z;
                // Every pile of the art within reach first, nearest first (Damian, 2026-10-23: "we should also pile up
                // logs on discovered logs at feet"), never one on the player's own tile.
                List<AssistItem> near = new();
                foreach (AssistItem g in k.State.Ground.Values)
                {
                    int d = Tiles(g.X, g.Y, fx, fy);
                    if (art(g.Graphic) && g.Container == 0 && d >= 1 && d <= LiftReach
                        && !near.Exists(n => n.X == g.X && n.Y == g.Y))
                        near.Add(g);
                }
                near.Sort((a, b) => Tiles(a.X, a.Y, fx, fy) - Tiles(b.X, b.Y, fx, fy));
                foreach (AssistItem g in near)
                    feetTiles.Add(new[] { g.X, g.Y, g.Z });
                // Each tile at its own standing height, as the client's drop names the surface it targets; the player's
                // height where the map has none (a drop at the player's z 1 on sloped forest ground was put back on all
                // eight tiles, 2026-10-22 01:51:17 to 01:51:33).
                for (int b = 0; b < s_beside.GetLength(0); b++)
                {
                    int bx = fx + s_beside[b, 0], by = fy + s_beside[b, 1], bz = UoMap.StandZ(bx, by, tz);
                    feetTiles.Add(new[] { bx, by, bz == int.MinValue ? tz : bz });
                }
            }
            tx = feetTiles[0][0];
            ty = feetTiles[0][1];
            tz = feetTiles[0][2];
        }
        else if (t[0] == "mobile")
        {
            int[]? at;
            at = k.State.Mobiles.TryGet(target, out at) ? new[] { at[0], at[1] } : null;
            if (at == null)
                return "the mobile " + target.ToString("X") + " for the " + what + " is not in view";
            tx = at[0];
            ty = at[1];
        }
        else if (t.Length >= 5)
        {
            tx = int.Parse(t[2]);
            ty = int.Parse(t[3]);
            tz = int.Parse(t[4]);
            AssistItem? it = target != 0 ? ItemOrGround(k, target) : null;
            if (it != null && it.Container == 0)
            {
                tx = it.X;
                ty = it.Y;
            }
        }
        else
        {
            return "the place for the " + what + " is not readable: target it again with " + button;
        }

        int px, py;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
        }
        if (Tiles(px, py, tx, ty) > 2)
        {
            CircuitStatus = "To the place for the " + what;
            string? w = WalkTo(tx, ty, 1);
            if (w != null)
                return "walking to the place for the " + what + " at " + tx + "," + ty + ": " + w;
        }
        for (int round = 0; round < 20 && !circuitStop; round++)
        {
            AssistItem? stack = null;
            lock (k.State.Gate)
            {
                Set<uint> inside = PackTree(k);
                foreach (AssistItem it in k.State.Items.Values)
                    if (inside.Contains(it.Container) && art(it.Graphic))
                    {
                        stack = it;
                        break;
                    }
            }
            if (stack == null)
                break;
            uint onto = target;
            // Onto a pile of the stack's own art and hue only (SamePileAt).
            if (t[0] == "ground" || t[0] == "feet")
            {
                AssistItem? under = SamePileAt(k, stack, tx, ty);
                if (under != null)
                    onto = under.Serial;
            }
            int amount = Math.Max(1, stack.Amount);
            string? why;
            if (t[0] == "feet")
                m_puttingAt = new[] { tx, ty };
            using (Act(k))
                why = MoveStack(k, stack.Serial, amount, "the " + what, "Storing the " + what,
                    () => (onto != 0 ? k.Drop(stack.Serial, onto) : k.DropGround(stack.Serial, tx, ty, tz))!);
            // A drop put back lay on the tile a moment: its sighting goes with it, or the map draws it wild for
            // SeenGoneMs.
            bool putBack = why != null && t[0] == "feet" && why.Contains(PutBack);
            if (putBack)
                SeenForget(tx, ty, stack.Graphic);
            if (putBack && feetAt + 1 < feetTiles.Count)
            {
                feetAt++;
                tx = feetTiles[feetAt][0];
                ty = feetTiles[feetAt][1];
                tz = feetTiles[feetAt][2];
                continue;
            }
            if (why != null)
                return why;
            m_putAt = new[] { tx, ty };
            bool left;
            lock (k.State.Gate)
            {
                AssistItem? it = Item(k, stack.Serial);
                left = it == null || !PackTree(k).Contains(it.Container);
            }
            if (!left)
                return "the " + what + " were not taken at " + tx + "," + ty + " (" + amount + " still in the pack)";
            put += amount;
        }
        return null;
    }
    // The chunks a "not enough wood" came from, kept until Clear empties: lumber.empty holds "zone:ticks" separated by
    // ";", the zone key and when it was found empty. A tile that is no tree is not chopped this run.
    public ValueMap<long, long> LumberEmpties()
    {
        ValueMap<long, long> d = ValueMap<long, long>.Empty;
        foreach (string e in Cfg.Get(Server + "lumber.empty", "").Split(new[] { ';' },
            StringSplitOptions.RemoveEmptyEntries))
        {
            string[] f = e.Split(':');
            long z, t;
            if (f.Length == 2 && long.TryParse(f[0], out z) && long.TryParse(f[1], out t))
                d = d.Set(z, t);
        }
        return d;
    }
    void LumberEmptyMark(long zone)
    {
        ValueMap<long, long> d = LumberEmpties().Set(zone, Clock.UtcNow.Ticks);
        List<string> parts = new();
        foreach (KeyValuePair<long, long> kv in d)
            parts.Add(kv.Key + ":" + kv.Value);
        Cfg.Set(Server + "lumber.empty", string.Join(";", parts));
    }
    // Clear empties: every chunk found empty is chopped again. How many were forgotten.
    // The chunk grid's rows go with them (Damian, 2026-10-23: "the chunk grid still has chunks in it from the last
    // grove. i think those should be cleared").
    public int LumberEmptyClear()
    {
        int n = LumberEmpties().Count;
        Cfg.Set(Server + "lumber.empty", "");
        LumberSentinel = "";
        lock (m_lumberZonesGate)
            m_lumberZones = m_lumberZones.Clear();
        if (n > 0)
            Note("Lumber: " + n + " chunks found empty forgotten; they are chopped again");
        return n;
    }
    static string EmptySince(long ticks)
    {
        return "empty since " + new DateTime(ticks, DateTimeKind.Utc).ToLocalTime().ToString("MM-dd HH:mm");
    }
    Set<long> m_lumberBad = Set<long>.Empty;
    // The chunks a tree of which no walk reached, left for the run.
    Set<long> m_lumberUnreached = Set<long>.Empty; readonly Lock m_lumberUnreachedGate = new();
    // Each 8x8 chunk chopped this session, for the Circuit tab's grid (Damian, 2026-10-22: "lets add the chunks
    // display"): chops, logs (in chops that gave them), failed chops, the last chop and the last "not enough wood".
    public sealed class LumberZone
    {
        public int Chops, Logs, Failed, Emptied; public DateTime LastChop, EmptyAt;
    }
    Map<long, LumberZone> m_lumberZones = Map<long, LumberZone>.Empty; readonly Lock m_lumberZonesGate = new();
    void LumberCount(long zone, ChopAnswer a)
    {
        lock (m_lumberZonesGate)
        {
            LumberZone? z;
            if (!m_lumberZones.TryGet(zone, out z))
                m_lumberZones = m_lumberZones.Set(zone, z = new LumberZone());
            z.Chops++;
            z.LastChop = Clock.UtcNow;
            if (a == ChopAnswer.Logs)
            {
                z.Logs++;
            }
            else if (a == ChopAnswer.Failed)
            {
                z.Failed++;
            }
            else if (a == ChopAnswer.Empty)
            {
                z.Emptied++;
                z.EmptyAt = Clock.UtcNow;
            }
        }
    }
    // The 8x8 chunks harvested, for the map's overlay: corner x, y and readiness per mille (0 just emptied, 1000
    // ready). The logger's, chopped this session or found empty (0 until Clear empties); the picked cave's, back at the
    // next respawn predicted.
    public List<int[]> HarvestChunks()
    {
        List<int[]> l = new();
        DateTime now = Clock.UtcNow;
        ValueMap<long, long> empty = LumberEmpties();
        Set<long> keys = Set<long>.Empty;
        foreach (long key in empty.Keys)
            keys = keys.Add(key);
        foreach (long key in m_lumberZones.Keys)
            keys = keys.Add(key);
        foreach (long key in keys)
            l.Add(new[] { (int)(key >> 32) << ZoneShift, (int)(uint)key << ZoneShift,
                empty.ContainsKey(key) ? 0 : 1000 });
        MineCave? c = PickedCave!.Length > 0 ? CaveNamed(PickedCave) : null;
        if (c != null)
        {
            lock (m_caves)
            {
                foreach (KeyValuePair<long, MineZone> kv in c.Zones)
                {
                    int ready = 1000;
                    if (Resting(c, kv.Value))
                    {
                        long back = NextStrike(c), span = back - kv.Value.EmptyAt;
                        ready = back <= 0 || span <= 0 ? 0
                            : (int)Math.Max(0, Math.Min(1000, 1000 * (now.Ticks - kv.Value.EmptyAt) / span));
                    }
                    l.Add(new[] { (int)(kv.Key >> 32) << ZoneShift, (int)(uint)kv.Key << ZoneShift, ready });
                }
            }
        }

        return l;
    }
    // The harvested chunk holding a tile, for the map's hover: the logger's record, the picked cave's, both, or null.
    public string? ChunkText(int x, int y)
    {
        long key = ZoneKey(x, y);
        List<string> parts = new();
        DateTime now = Clock.UtcNow;
        long at;
        bool empty = LumberEmpties().TryGet(key, out at);
        string state = empty ? EmptySince(at) + ", until Clear empties" : "has wood";
        LumberZone? lz;
        if (m_lumberZones.TryGet(key, out lz))
            parts.Add("wood " + ZoneText(key) + ": " + lz.Chops + " chops, " + lz.Logs + " with logs, " + lz.Failed
                + " failed, empty " + lz.Emptied + "x, " + state);
        else if (empty)
            parts.Add("wood " + ZoneText(key) + ": " + state);
        MineCave? c = PickedCave!.Length > 0 ? CaveNamed(PickedCave) : null;
        if (c != null)
            lock (m_caves)
            {
                MineZone? z;
                if (c.Zones.TryGet(key, out z))
                    parts.Add("ore " + ZoneText(key) + ": " + z.Ore + " ore digs, empty " + z.Emptied + "x, "
                        + BackText(c, z));
            }
        int[]? pile = LumberPiles().Find(p => p[0] == x && p[1] == y);
        if (pile != null)
            parts.Add((pile[3] == 1 ? "wild logs: " : "dropped logs: ") + pile[2] + " at " + x + "," + y);
        return parts.Count > 0 ? string.Join("; ", parts) : null;
    }
    // Chunk, chops, logs, failed, emptied, last chop, last empty, back; the chunks with wood first, then the empty ones
    // by when found empty. A chunk found empty in an earlier run shows its empty alone.
    public List<string[]> LumberZoneRows()
    {
        List<Tuple<long, string[]>> rows = new();
        ValueMap<long, long> empty = LumberEmpties();
        Map<long, LumberZone> zones = m_lumberZones;
        foreach (KeyValuePair<long, LumberZone> kv in zones)
        {
            LumberZone z = kv.Value;
            long at;
            bool e = empty.TryGet(kv.Key, out at);
            rows.Add(Tuple.Create(e ? at : long.MinValue,
                new[] { ZoneText(kv.Key), z.Chops.ToString(), z.Logs.ToString(), z.Failed.ToString(),
                z.Emptied.ToString(), z.LastChop.ToLocalTime().ToString("HH:mm:ss"),
                z.EmptyAt == DateTime.MinValue ? "" : z.EmptyAt.ToLocalTime().ToString("HH:mm:ss"),
                e ? "until Clear" : "has wood" }));
        }
        foreach (KeyValuePair<long, long> kv in empty)
            if (!zones.ContainsKey(kv.Key))
                rows.Add(Tuple.Create(kv.Value,
                    new[] { ZoneText(kv.Key), "", "", "", "", "",
                    new DateTime(kv.Value, DateTimeKind.Utc).ToLocalTime().ToString("MM-dd HH:mm"),
                    "until Clear" }));
        rows.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return rows.ConvertAll(r => r.Item2);
    }
    // The tree order (Damian, 2026-10-23: "put a second spot, a destination ... from current spot to collection, mine a
    // line between them, then concentric spiral out from the oval with foci the two spots"): the foci are the
    // collection's place and Chop from (where the circuit started, when unset; with no collection place both are Chop
    // from, so the ovals are circles). A tree's level is its distance to the two foci less theirs apart, 0 on the line
    // between them, taken in bands of LumberBand tiles, the width of an 8x8 chunk; the tree whose walk plus LumberBand
    // a band is least first (LumberTree). Range: the outermost oval's half-width across the middle of the line, so with
    // one focus a circle of Range.
    public const int LumberBand = GatherOrder.Band;
    public const uint LumberFromCursor = 0x4C46524D;  // "LFRM"
    public string LumberFrom
    {
        get => Cfg.Get(Server + "lumber.from", "");
        set => Cfg.Set(Server + "lumber.from", value ?? "");
    }
    public string LumberFromText()
    {
        string[] f = LumberFrom.Split(',');
        return f.Length == 2 ? "Chop from: " + f[0] + "," + f[1] : "Chop from: where the circuit starts";
    }
    public string? LumberFromTarget()
    {
        return LumberCursor(LumberFromCursor,
            "Target where the chopping starts from: the far end of the line to the collection.");
    }
    int[] m_lumberStart = null!;
    // The map's lumber marks (Damian, 2026-10-23: "add a dot on the map for sentinel and collection point, and both
    // loci in chop from mode"): x, y, kind (0 the sentinel, 1 the collection point, 2 Chop from), the collection point
    // the oval's other focus.
    public List<int[]> LumberSpots()
    {
        List<int[]> l = new();
        int x, y;
        int[]? s = LumberSentinelTree();
        if (s != null)
            l.Add(new[] { s[0], s[1], 0 });
        string[] t = LumberLogsTo.Split('|');
        if ((t[0] == "ground" || t[0] == "item") && t.Length >= 5 && int.TryParse(t[2], out x)
            && int.TryParse(t[3], out y))
            l.Add(new[] { x, y, 1 });
        string[] f = LumberFrom.Split(',');
        if (f.Length == 2 && int.TryParse(f[0], out x) && int.TryParse(f[1], out y))
            l.Add(new[] { x, y, 2 });
        return l;
    }
    int[] LumberFromSpot()
    {
        string[] f = LumberFrom.Split(',');
        int x, y;
        return f.Length == 2 && int.TryParse(f[0], out x) && int.TryParse(f[1], out y) ? new[] { x, y } : m_lumberStart;
    }
    // The trees of the oval (GatherOrder.Oval, the first tree static of each tile), scanned once for a pair of foci and
    // a range, the statics being fixed.
    ImmutableArray<GatherSpot> m_lumberOval; string m_lumberOvalKey = "";
    ImmutableArray<GatherSpot> LumberOval(int[] a, int[] b, int radius)
    {
        string key = a[0] + "," + a[1] + "," + b[0] + "," + b[1] + "," + radius;
        if (key == m_lumberOvalKey)
            return m_lumberOval;
        ImmutableArray<GatherSpot> l = GatherOrder.Oval(a[0], a[1], b[0], b[1], radius,
            (x, y) => { foreach (int[] s in LumberStatics(x, y)) if (IsTree(s[0])) return s; return null; });
        m_lumberOval = l;
        m_lumberOvalKey = key;
        return l;
    }
    static bool OvalHolds(ImmutableArray<GatherSpot> oval, int x, int y)
    {
        foreach (GatherSpot t in oval)
            if (t.X == x && t.Y == y)
                return true;
        return false;
    }
    // The statics of a tile, graphic and z each: the client's map, or a test's own.
    public Func<int, int, List<int[]>> LumberStatics = UoMap.StaticsAt;
    // The next tree of the oval (the order above) not in a chunk found empty, not refused, not in a chunk no walk
    // reached, and farther than FleeClear from every threat in view: x, y, z, graphic; null when none. trees: how many
    // the oval holds; empty: how many of them lie in chunks found empty.
    // The oval's trees for where the player stands now (the foci as above).
    ImmutableArray<GatherSpot> OvalNow(GameLink k, int px, int py)
    {
        int cx, cy, cz;
        uint co;
        int[] from = LumberFromSpot() ?? new[] { px, py };
        int[] near = CollectPlace(k, out cx, out cy, out cz, out co) == null ? new[] { cx, cy } : from;
        return LumberOval(near, from, LumberRadius);
    }
    public int[]? LumberTree(GameLink k, int px, int py, out int trees, out int empty)
    {
        ImmutableArray<GatherSpot> oval = OvalNow(k, px, py);
        trees = oval.Length;
        List<int[]> threats = Threats(k);
        ValueMap<long, long> empties = LumberEmpties();
        int inEmpty = 0;
        // The order is GatherOrder.Pick's; a tree is left out in a chunk found empty (counted), in a chunk no walk
        // reached, refused, or within FleeClear of a threat in view.
        GatherSpot? best = GatherOrder.Pick(oval, px, py, t =>
        {
            long zone = ZoneKey(t.X, t.Y);
            if (empties.ContainsKey(zone))
            {
                inEmpty++;
                return true;
            }
            bool unreached = m_lumberUnreached.Contains(zone);
            return unreached || m_lumberBad.Contains(TileKey(t.X, t.Y))
                || threats.Exists(th => Tiles(th[0], th[1], t.X, t.Y) <= FleeClear);
        });
        empty = inEmpty;
        return best == null ? null : new[] { best.X, best.Y, best.Z, best.Graphic };
    }
    // The sentinel (Damian, 2026-10-23: "it represents evidence of a respawn since it was last depleted"; "imagine a
    // fresh new day ... that first chunk becomes the sentinel ... at some point, we get stopped and resumed, or we
    // finish the circuit and collection is complete. then we chop again that first sentinel spot. if it is still empty,
    // we can assume ALL the spots we chopped since the first chop are also still empty. then we should create a new
    // path, one that starts from the sentinel"; logs from it: "New day: forget all"). The first chunk found empty with
    // no sentinel set is the sentinel (lumber.sentinel, its tree x, y, z, graphic). At every resume and after every
    // collection its tree is chopped: logs, a new day, every empty and the sentinel forgotten; "not enough wood", every
    // empty stands and the run's path starts from the sentinel (Chop from, when set, still wins); a failed chop is
    // chopped again, SentinelTries in all; anything else proves nothing and keeps both.
    public const int SentinelTries = 3;
    public string LumberSentinel
    {
        get => Cfg.Get(Server + "lumber.sentinel", "");
        set => Cfg.Set(Server + "lumber.sentinel", value ?? "");
    }
    public int[]? LumberSentinelTree()
    {
        string[] f = LumberSentinel.Split(',');
        int[] t = new int[4];
        if (f.Length != 4)
            return null;
        for (int i = 0; i < 4; i++)
            if (!int.TryParse(f[i], out t[i]))
                return null;
        return t;
    }
    public string LumberSentinelText()
    {
        int[]? t = LumberSentinelTree();
        return t != null ? "Sentinel: the tree at " + t[0] + "," + t[1]
            : LumberSentinel == SentinelNext ? "Sentinel: the next tree chopped (a new chop)"
            : "Sentinel: none yet (the next chunk found empty)";
    }
    // Null, or why the circuit ends.
    string? LumberProbe(GameLink k)
    {
        int[]? s = LumberSentinelTree();
        if (s == null)
            return null;
        long zone = ZoneKey(s[0], s[1]);
        // A sentinel whose chunk has not run out (a new chop's first tree, still giving wood) has nothing to tell yet.
        if (!LumberEmpties().ContainsKey(zone))
            return null;
        AssistItem? axe = LumberAxeNow(k);
        if (axe == null)
            return null;
        int px, py;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
        }
        // A sentinel outside this run's oval stands for another grove (Damian, 2026-10-23: "moved to a new grove, and
        // started a new chop. the circuit decided to go check the old sentinel"): not chopped, kept.
        if (!OvalHolds(OvalNow(k, px, py), s[0], s[1]))
        {
            Note("Lumber: the sentinel at " + s[0] + "," + s[1] + " is outside this oval; not chopped");
            return null;
        }
        // Chopped only from where the player stands, never walked to (Damian, 2026-10-23: "resume recalled to the start
        // of the chop ... never do that, always start chopping and chop towards the collection point").
        if (Tiles(px, py, s[0], s[1]) > 1)
        {
            Note("Lumber: the sentinel at " + s[0] + "," + s[1] + " is " + Tiles(px, py, s[0], s[1])
                + " tiles away; not walked to, every chunk found empty kept");
            return null;
        }
        string? ew = LumberEquip(k, axe);
        if (ew != null)
            return "the axe: " + ew;
        for (int tries = 0; tries < SentinelTries && !circuitStop && !k.Closed; tries++)
        {
            CircuitStatus = "Lumber: chopping the sentinel at " + s[0] + "," + s[1];
            BusyWait(m_lastUse);
            ChopAnswer a = LumberChop(k, axe.Serial, s[0], s[1], s[2], s[3]);
            LumberCount(zone, a);
            if (a == ChopAnswer.Logs)
            {
                int n = LumberEmpties().Count;
                LumberEmptyClear();
                Note("Lumber: the sentinel at " + s[0] + "," + s[1] + " has wood again: a new day, the " + n
                    + " chunks found empty are chopped again");
                return null;
            }
            if (a == ChopAnswer.Empty)
            {
                m_lumberStart = new[] { s[0], s[1] };
                Note("Lumber: the sentinel at " + s[0] + "," + s[1]
                    + " is still empty: every chunk chopped since is taken as empty, and the path starts from it");
                return null;
            }
            if (a == ChopAnswer.NotTree)
            {
                m_lumberBad = m_lumberBad.Add(TileKey(s[0], s[1]));
                LumberSentinel = "";
                Note("Lumber: the sentinel at " + s[0] + "," + s[1]
                    + " is not choppable; the next chunk found empty is the sentinel");
                return null;
            }
            if (a == ChopAnswer.NotEquipped)
                return "the server says the axe is not in hand";
            if (a != ChopAnswer.Failed && a != ChopAnswer.Wait)
                break;
        }
        Note("Lumber: the sentinel at " + s[0] + "," + s[1]
            + " gave no answer that shows wood or none; every chunk found empty kept");
        return null;
    }
    // The pack takes another chop's logs.
    bool LumberRoom(GameLink k)
    {
        List<UoTiles.Tile> items = UoTiles.Items();
        int per = items.Count > 0x1BDD ? Math.Max(1, items[0x1BDD].Weight) : 2;
        int w;
        lock (k.State.Gate)
            w = k.State.Weight;
        return w <= 0 || w + per * LumberWood <= MineLimit(k);
    }
    string RunLumber(GameLink k)
    {
        if (LumberLogsTo.Length == 0)
            return "no place for the logs: target one with Logs to";
        // At feet, the collection's place is checked before the first chop, not when the piles are due an hour later.
        if (LumberAtFeet)
        {
            int cx, cy, cz;
            uint co;
            string? pw = CollectPlace(k, out cx, out cy, out cz, out co);
            if (pw != null)
                return pw;
        }
        int chops = 0, logs = 0, failed = 0, unanswered = 0;
        m_lumberGather.Stored = 0;
        m_lumberBad = m_lumberBad.Clear();
        lock (m_lumberUnreachedGate)
            m_lumberUnreached = m_lumberUnreached.Clear();
        lock (k.State.Gate)
            m_lumberStart = new[] { k.State.X, k.State.Y };
        if (m_collectOnly)
        {
            m_collectOnly = false;
            m_collectNow = false;
            LumberDiscover(k);
            CircuitStatus = "Lumber: collecting " + LumberDropped() + " logs in " + LumberPiles().Count + " piles";
            string? ow = LumberCollect(k);
            return ow == null ? "collected" : "the collection: " + ow;
        }
        Note("Lumber: chopping around here, " + LumberLogsText());
        // A Resume after a flee, a bounce or a dropped link (Damian, 2026-10-23: "keep the data, figure out what to do
        // next, and continue"): a collection already due runs first, then the sentinel.
        if (m_lumberGather.CollectDue)
        {
            CircuitStatus = "Lumber: collecting " + LumberDropped() + " logs in " + LumberPiles().Count
                + " piles, due before the stop";
            string? cr = LumberCollect(k);
            if (cr != null)
                return "the collection: " + cr;
        }
        string? sp = LumberProbe(k);
        if (sp != null)
            return sp;
        while (!circuitStop && !k.Closed)
        {
            LumberDiscover(k);
            if (m_collectNow)
            {
                m_collectNow = false;
                CircuitStatus = "Lumber: collecting now, " + LumberDropped() + " logs in " + LumberPiles().Count
                    + " piles";
                string? cn = LumberCollect(k);
                if (cn != null)
                    return "the collection: " + cn;
                string? cp = LumberProbe(k);
                if (cp != null)
                    return cp;
                continue;
            }
            LumberStatus = chops + " chops, " + logs + " logs, " + failed + " failed, " + m_lumberGather.Stored
                + " stored";
            AssistItem? axe = LumberAxeNow(k);
            // Damian, 2026-10-22: "if one is not present, stop and inform the player".
            if (axe == null)
            {
                k.Tell("Lumberjacking stopped: no axe. Target one with the Axe button.", 0x0026);
                return "no axe in the pack or hand: target one with the Axe button";
            }
            List<int[]> th = Threats(k);
            if (th.Count > 0 && Nearest(th) <= FleeClear)
            {
                CircuitStatus = "Lumber: away from a threat";
                Note("Lumber: a threat within " + Nearest(th) + " tiles; running");
                string? fw = Flee();
                if (fw != null)
                    return "running from a threat: " + fw;
                continue;
            }
            if (!LumberRoom(k))
            {
                string? sw = LumberStore(k);
                if (sw != null)
                    return "storing the logs: " + sw;
                if (!LumberRoom(k))
                    return "the pack has no room after storing the logs";
                // The logs dropped since the last collection reaching Collect at start it, then chopping goes on
                // (Damian, 2026-10-22: "trigger the collection based on a configuration of the amount of total dropped
                // logs").
                if (m_lumberGather.CollectDue)
                {
                    CircuitStatus = "Lumber: collecting " + LumberDropped() + " logs in " + LumberPiles().Count
                        + " piles";
                    string? cw = LumberCollect(k);
                    if (cw != null)
                        return "the collection: " + cw;
                    string? cp = LumberProbe(k);
                    if (cp != null)
                        return cp;
                }
                continue;
            }
            int px, py;
            lock (k.State.Gate)
            {
                px = k.State.X;
                py = k.State.Y;
            }
            int inReach, inEmpty;
            int[]? tree = LumberTree(k, px, py, out inReach, out inEmpty);
            if (tree == null)
            {
                string end = inReach == 0 ? "no tree within " + LumberRadius + " tiles"
                    : "no tree left to chop in reach"
                    + (inEmpty > 0
                    ? ": " + inEmpty + " of its " + inReach
                    + (" trees are in chunks found empty; a resume chops the sentinel, and Clear empties chops them "
                    + "all again") : "");
                // The pack's logs put down and every pile collected before the end, Collect at or not (Damian,
                // 2026-10-23: "instead of just stopping, it should collect first").
                if (LumberAtFeet)
                {
                    if (PackLogs(k))
                    {
                        string? sw = LumberStore(k);
                        if (sw != null)
                            return end + "; storing the logs: " + sw;
                    }
                    if (LumberPiles().Count > 0)
                    {
                        CircuitStatus = "Lumber: collecting " + LumberDropped() + " logs in " + LumberPiles().Count
                            + " piles before the end";
                        Note("Lumber: " + end + "; collecting the piles first");
                        string? cw = LumberCollect(k);
                        if (cw != null)
                            return end + "; the collection: " + cw;
                    }
                }
                return end;
            }
            // A tree no walk reaches: its whole chunk is left this run, not tree after tree a second each (2026-10-22
            // 03:41:27 to 03:41:40, a dozen trees of one grove past water, each "no way").
            if (Tiles(px, py, tree[0], tree[1]) > 1)
            {
                CircuitStatus = "Lumber: to the tree at " + tree[0] + "," + tree[1];
                string? w = WalkTo(tree[0], tree[1], 1);
                if (w != null)
                {
                    m_lumberBad = m_lumberBad.Add(TileKey(tree[0], tree[1]));
                    if (w.StartsWith("no way"))
                        lock (m_lumberUnreachedGate)
                            m_lumberUnreached = m_lumberUnreached.Add(ZoneKey(tree[0], tree[1]));
                    Note("Lumber: the tree at " + tree[0] + "," + tree[1] + " was not reached: " + w
                        + (w.StartsWith("no way")
                        ? "; its chunk " + ZoneText(ZoneKey(tree[0], tree[1])) + " is left this run" : ""));
                    continue;
                }
            }
            string? ew = LumberEquip(k, axe);
            if (ew != null)
                return "the axe: " + ew;
            CircuitStatus = "Lumber: chopping the tree at " + tree[0] + "," + tree[1];
            BusyWait(m_lastUse);
            ChopAnswer a = LumberChop(k, axe.Serial, tree[0], tree[1], tree[2], tree[3]);
            chops++;
            LumberCount(ZoneKey(tree[0], tree[1]), a);
            if (a == ChopAnswer.None)
            {
                if (++unanswered >= LumberUnansweredMax)
                    return LumberUnansweredMax + " chops in a row had no answer from the server";
                continue;
            }
            unanswered = 0;
            if (LumberSentinel == SentinelNext
                && (a == ChopAnswer.Logs || a == ChopAnswer.Failed || a == ChopAnswer.Empty))
            {
                LumberSentinel = tree[0] + "," + tree[1] + "," + tree[2] + "," + tree[3];
                Note("Lumber: the new chop's first tree at " + tree[0] + "," + tree[1] + " is the sentinel");
            }
            if (a == ChopAnswer.Logs)
            {
                logs += LumberWood;
            }
            else if (a == ChopAnswer.Failed)
            {
                failed++;
            }
            else if (a == ChopAnswer.Empty)
            {
                LumberEmptyMark(ZoneKey(tree[0], tree[1]));
                bool first = LumberSentinelTree() == null;
                if (first)
                    LumberSentinel = tree[0] + "," + tree[1] + "," + tree[2] + "," + tree[3];
                Note("Lumber: the chunk at " + ZoneText(ZoneKey(tree[0], tree[1]))
                    + " has no wood; kept empty until the sentinel has wood or Clear empties"
                    + (first ? "; its tree at " + tree[0] + "," + tree[1] + " is the sentinel" : ""));
            }
            else if (a == ChopAnswer.NotTree)
            {
                m_lumberBad = m_lumberBad.Add(TileKey(tree[0], tree[1]));
                Note("Lumber: " + tree[0] + "," + tree[1] + " is not choppable");
            }
            else if (a == ChopAnswer.NotEquipped)
            {
                return "the server says the axe is not in hand";
            }
        }
        return circuitStop ? "stopped" : "the connection closed";
    }
}
