#nullable enable
using System.Collections.Immutable;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;

namespace Uoaix.Assist;

public sealed partial class AssistFeatures
{
    // ---------- the refresh circuit ----------
    // (Damian, 2026-10-17: "new circuit: refresh loadout and feed horse and self, dump gold in bank when done. use
    // provisioner for apples or pears or a farmers market, or a stable if necessary. never bails of hay or wheat, for
    // some reason they don't work. eat for self as well. better food is usually better, but not required. get full
    // basically"): food from the city's food shops, the player eats until the server says full, the mount is handed its
    // fruit, and at the bank the gold over the purse goes in and the reagent loadout is refilled from its storage bag.
    // Satiety: each 1998 food's (rundir/scripts.wombat/<item>.m); the server refuses food past 24 ("You are simply too
    // full to eat any more!", eat.m), so FullSatiety from empty fills the player. No hay or wheat is on any list.
    static readonly ValueMap<int, int> s_satiety = ValueMap<int, int>.Of(
        (0x09BB, 25), (0x09BC, 25), (0x097E, 16), (0x09B7, 12), (0x09B8, 12), (0x09C0, 12), (0x09C1, 12),
            (0x097D, 12),
        (0x09E9, 10), (0x09C9, 10), (0x09D3, 10), (0x09F2, 9), (0x0970, 9), (0x0971, 9), (0x0972, 9),
        (0x0973, 9),
        (0x0C6A, 8), (0x0C6B, 8), (0x0C6C, 8), (0x09B6, 6), (0x1040, 6), (0x1041, 6), (0x1721, 6),
        (0x1722, 6), (0x0993, 6),
        (0x0C5C, 5), (0x0C5D, 5), (0x0C74, 5), (0x0C75, 5), (0x103B, 5), (0x0C79, 4), (0x0C7A, 4),
        (0x097B, 4), (0x103C, 4),
        (0x0C7B, 3), (0x0C7C, 3), (0x0C70, 3), (0x0C71, 3), (0x160A, 3), (0x1608, 3), (0x0976, 3),
        (0x0977, 3), (0x097C, 3),
        (0x098C, 3), (0x0C72, 2), (0x0C73, 2), (0x09D0, 2), (0x09EA, 2), (0x09EB, 2), (0x171F, 2),
        (0x1720, 2), (0x1723, 2),
        (0x0994, 1), (0x09D1, 1), (0x09D2, 1));
    // The mount's: apples and pears, HorseFeed handed over in one stack (the feeding Damian does by hand).
    static readonly ImmutableArray<int> s_horseFood = [ 0x09D0, 0x0994 ];
    public const int HorseFeed = 10, FullSatiety = 25, EatMax = 40, EatAnswerMs = 2500, MountWaitMs = 3000;
    static readonly ImmutableArray<string> s_eatLines = [ "too full", "stuffed", "You eat the food",
        "After eating the food",
        "quite full after", "can't eat that" ];
    public volatile string RefreshStatus = "";
    bool Riding(GameLink k)
    {
        lock (k.State.Gate)
        {
            foreach (AssistItem it in k.State.Items.Values)
                if (it.Layer == MountLayer && it.Mobile == k.State.Player)
                    return true;
        }
        return false;
    }
    uint MountSerial()
    {
        lock (m_pets)
        {
            foreach (Pet p in m_pets)
                if (p.Mount)
                    return p.Serial;
        }
        return 0;
    }
    // The satiety the pack's food holds for the player, the mount's fruit left out while a mount is fed.
    int SelfFood(GameLink k, bool mount)
    {
        int n = 0;
        lock (k.State.Gate)
        {
            Set<uint> inside = PackTree(k);
            foreach (AssistItem it in k.State.Items.Values)
            {
                int s;
                if (inside.Contains(it.Container) && s_satiety.TryGet(it.Graphic, out s)
                    && !(mount && s_horseFood.IndexOf(it.Graphic) >= 0))
                    n += s * Math.Max(1, it.Amount);
            }
        }
        return n;
    }
    // The stocked food of the most satiety, by the art in the vendor's stock boxes (the buy window just read): the
    // stock list's names are the server's ("Loaf of bread", "Cooked ribs") and not the client's ("bread loa", "cut of
    // ribs"). 0 when it stocks none.
    int BestFood(GameLink k, uint vendor)
    {
        Set<uint> boxes = Set<uint>.Empty;
        foreach (KeyValuePair<uint, uint> kv in m_wornBy)
            if (kv.Value == vendor)
                boxes = boxes.Add(kv.Key);
        int best = 0;
        lock (k.State.Gate)
            foreach (AssistItem it in k.State.Items.Values)
            {
                int s;
                if (boxes.Contains(it.Container) && s_satiety.TryGet(it.Graphic, out s)
                    && (best == 0 || s > s_satiety.Get(best, 0)))
                    best = it.Graphic;
            }
        return best;
    }
    // ---------- the mining circuit (Damian, 2026-10-18: "tinker shovels, dig and smelt, store the ingots. the target
    // will be any cave, but the circuit needs to be configured for a player's prefered storage"; "the configure needs
    // to see the forge") ----------
    // Set up per server with mine-setup or the Circuit tab's mining rows (Damian, 2026-10-18: "need a drop down for 1
    // or 2 shovel digging, a tool bag selection, automatic selection and walking to targets, using the 8x8 resource
    // zone"): the forge in view, the shovel mode, the tool bag, the storage and the cave, picked from the cave list.
    // The 1998 server's mining (uo-reference rundir/scripts.wombat/mining.m): a dig reaches 4 tiles (past 4, "That is
    // too far away."), takes the metal of the 8x8 map chunk it lands in (getChunkEgg), and answers "There is no metal
    // here to mine." or "Someone has gotten to the metal before you." when the chunk has none; a shovel used while its
    // own dig runs cancels that dig (removeCallback), so no shovel is used while its dig is in flight.
    public const int PlanEveryMs = 2000;
    public const int MineReach = 4, ZoneShift = 3, MineRadius = 24, ShovelPrice = 14, MineCursorMs = 3000,
        MineCarryDefault = 10, MineUnansweredMax = 3, MineWalkTries = 3;
    // People and creatures near the miner are recorded in the cave this often, within this reach.
    public const int CaveLookSeconds = 30, CaveLookReach = 18;
    // itemmanip.m Q4ZJ (land ids) and Q4ZI (static ids): what a shovel can dig, as inclusive ranges.
    static readonly ImmutableArray<int> s_mineLand = [ 0xDC, 0xE7, 0xEC, 0xF7, 0xFC, 0x107, 0x10C, 0x117, 0x11E, 0x129,
        0x141, 0x144,
        0x1D3, 0x1DA, 0x21F, 0x243, 0x245, 0x26D, 0x6CD, 0x6DD, 0x6EB, 0x6FE, 0x709, 0x720, 0x727, 0x73E, 0x745, 0x75C,
        0x7BD, 0x7D4 ];
    static readonly ImmutableArray<int> s_mineStatic = [ 0x25C, 0x276, 0x27D, 0x280, 0x53B, 0x54F, 0x551, 0x553, 0x56A,
        0x56A,
        0x16E4, 0x16F3 ];
    static bool InRanges(IReadOnlyList<int> r, int v)
    {
        for (int i = 0; i + 1 < r.Count; i += 2)
            if (v >= r[i] && v <= r[i + 1])
                return true;
        return false;
    }
    // A dig's answer (mining.m): ore, rocks loosened, the chunk empty, the spot refused, or the dig broken off.
    static readonly ImmutableArray<string> s_digOre = [ "You dig some ore" ],
        s_digLoosened = [ "You loosen some rocks" ];
    static readonly ImmutableArray<string> s_digEmpty = [ "There is no metal here to mine",
        "There is no metal to mine here",
        "Someone has gotten to the metal before you" ];
    static readonly ImmutableArray<string> s_digRefused = [ "You can't mine there", "You can't mine that",
        "You cannot mine",
        "That is too far away", "too far away to mine" ];
    static readonly ImmutableArray<string> s_digBroken = [ "You have moved too far away to continue mining" ];
    static readonly ImmutableArray<string> s_digEnds = [ "You can't mine while riding",
        "You can't mine while polymorphed" ];
    static bool Says(string line, IReadOnlyList<string> any)
    {
        foreach (string s in any)
            if (line.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
    }
    public static bool IsForge(int graphic)
    {
        return IsSmithForge(graphic);
    }
    // 1EBC "tinker's tools"; the shop and guild signs (0BA7, 0BA8, 0C01, 0C02) and 201D also say tinker.
    static bool IsTinkerTool(int graphic)
    {
        string n = UoTiles.Name(graphic).ToLowerInvariant();
        return n.Contains("tinker") && n.Contains("tool");
    }
    string MineCfg(string key, string def = "")
    {
        return Cfg.Get(Server + "mine." + key, def);
    }
    int MineCfgInt(string key, int def)
    {
        return Cfg.GetInt(Server + "mine." + key, def);
    }
    // 1 or 2 shovels digging at once.
    public int MineMode
    {
        get => MineCfgInt("mode", 2) == 1 ? 1 : 2;
        set => Cfg.Set(Server + "mine.mode", value == 1 ? "1" : "2");
    }
    // The shovels carried after a purchase or a tinkering.
    public int MineCarry
    {
        get => Math.Max(1, MineCfgInt("shovels", MineCarryDefault));
        set => Cfg.Set(Server + "mine.shovels", Math.Max(1, value).ToString());
    }
    // backpack (local, while mining), vendor (small and secure, for sale) or stash (the ingot stash's target: big and
    // unsecured in the house, small and secure in the bank box). Damian, 2026-10-22: "the bank can be targted as
    // storage through stack targeting: either ingot pile or container in bank should be targetable as the store, so no
    // specific "bank first" step is needed"; "remove floor where i stand".
    // target: wherever Store to targeted, a tile, a bag, a pile or a pack animal (PutStacks; Damian, 2026-10-22:
    // "packmule for mining is helpful too").
    public string MineStorage
    {
        get
        {
            string s = MineCfg("store", "backpack");
            return s == "vendor" || s == "stash" || s == "target" ? s : "backpack";
        }
    }
    public string MineStoreTo
    {
        get => MineCfg("store-to");
        set => Cfg.Set(Server + "mine.store-to", value ?? "");
    }
    public const uint MineStoreCursor = 0x4D53544F;  // "MSTO"
    public string? MineStoreToTarget()
    {
        return LumberCursor(MineStoreCursor,
            "Target where the ingots go: a tile, a bag, a pile, the floor or a pack animal.");
    }
    public string MineStoreToText()
    {
        return PlaceText(MineStoreTo, "Store to");
    }
    // The pack rune's label that recalls to the bank (Damian, 2026-10-18: "if you pick bank, it needs to have a recall
    // rune. no running to the bank").
    public string MineBankRune
    {
        get => RunePlace(MineCfg("bank-rune", MineCfg("store-rune"))); set
        {
            Cfg.Set(Server + "mine.bank-rune", RunePlace(value));
            MineBankRuneSerial = 0;
        }
    }
    // The bank rune targeted (AssistIdentity.md stage 1: a rune picked by target is its serial; the label only orders
    // the runes never recalled by); 0 when the label was set by name.
    public uint MineBankRuneSerial
    {
        get
        {
            uint v;
            return uint.TryParse(MineCfg("bank-rune-serial"), NumberStyles.HexNumber, null, out v) ? v : 0;
        }

        set => Cfg.Set(Server + "mine.bank-rune-serial", value == 0 ? "" : value.ToString("X"));
    }
    public volatile string MineStatus = "";

    // ---------- caves (Damian, 2026-10-18: "i want a list of caves to choose from that can be configed on the fly
    // "mark cave here" and then we save details about the cave like depth of resource seams and business, npcs and
    // monsters nearby")
    // ----------
    // One line a record in caves-<server>.txt beside the settings, tab separated: C name x y z radius rune marked
    // people-max; Z name chunk ore loosened emptied emptied-at (UTC ticks); S name kind (1 a person, 0 a creature)
    // times-seen who. Each chunk's evidence (Damian, 2026-10-18: "keep track of each harvest region in a grid in the ui
    // and show last dig, last empty message and likely respawn based on evidence"): EmptyAt is the first "no metal"
    // since its last ore; RegrowMin the shortest seen from that to ore again (an upper bound on the regrowth),
    // StillEmptyMax the longest seen from it to another "no metal" (a lower bound). Times are UTC ticks; the two spans
    // are tick counts.
    public sealed class MineZone
    {
        public int Ore, Loosened, Emptied;
        public long EmptyAt, LastDig, LastOreAt, RegrowMin, StillEmptyMax; public string EmptyLine = "";
    }
    public sealed class MineCave
    {
        public string Name = "", Rune = ""; public int X, Y, Z, Radius = MineRadius, PeopleMax; public long Marked;
        // The rune back targeted (the cave file's 13th column, hex); 0 when its label was set by name.
        public uint RuneSerial;
        // The cave respawns as a whole (Damian, 2026-10-21: "individual chunks don't respawn individually. they respawn
        // as a group, like all the items at a vendor shop"). RestCleared: a chunk emptied after it rests until the next
        // strike (the last strike, or a circuit's start, which trusts nothing). LastStrike and Cycle (the shortest time
        // between two strikes) are kept in the cave file. Sentinel: the doorway chunk (DoorZone), set when a run
        // starts.
        public long RestCleared, Sentinel = long.MinValue, LastStrike, Cycle;
        // The respawn of the last strike came after StrikeFrom, the last "no metal" heard before it, and no later than
        // LastStrike, when ore was found (Damian, 2026-10-22: "you just don't know when in the timer cycle you
        // started"): predictions count from StrikeFrom. LastEmpty: the latest "no metal" heard in the cave.
        public long StrikeFrom, LastEmpty;
        public Map<long, MineZone> Zones = Map<long, MineZone>.Empty;
        public Map<string, int[]> Seen = Map<string, int[]>.Empty;
    }
    readonly List<MineCave> m_caves = new();
    bool m_cavesLoaded;
    string CavesPath => ServerFile("caves");
    static long ZoneKey(int x, int y)
    {
        return ((long)(x >> ZoneShift) << 32) | (uint)(y >> ZoneShift);
    }
    static string ZoneText(long z)
    {
        return ((z >> 32) << ZoneShift) + "," + (((int)(uint)z) << ZoneShift);
    }
    static string Clean(string s)
    {
        return (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
    }
    void LoadCaves()
    {
        lock (m_caves)
        {
            if (m_cavesLoaded)
                return;
            m_cavesLoaded = true;
            try
            {
                if (!File.Exists(CavesPath))
                    return;
                foreach (string line in File.ReadAllLines(CavesPath))
                {
                    string[] t = line.Split('\t');
                    if (t[0] == "C" && t.Length >= 9)
                    {
                        MineCave nc = new()
                        {
                            Name = t[1],
                            X = int.Parse(t[2]),
                            Y = int.Parse(t[3]),
                            Z = int.Parse(t[4]),
                            Radius = int.Parse(t[5]),
                            Rune = t[6],
                            Marked = long.Parse(t[7]),
                            PeopleMax = int.Parse(t[8])
                        };
                        if (t.Length >= 11)
                        {
                            nc.LastStrike = long.Parse(t[9]);
                            nc.Cycle = long.Parse(t[10]);
                            nc.RestCleared = nc.LastStrike;
                        }
                        if (t.Length >= 12)
                            nc.StrikeFrom = long.Parse(t[11]);
                        uint rs;
                        if (t.Length >= 13 && uint.TryParse(t[12], NumberStyles.HexNumber, null, out rs))
                            nc.RuneSerial = rs;
                        m_caves.Add(nc);
                    }
                    MineCave? c = t.Length > 1 ? m_caves.Find(v => v.Name == t[1]) : null;
                    if (c == null)
                        continue;
                    if (t[0] == "Z" && t.Length >= 7)
                    {
                        MineZone z = new()
                        {
                            Ore = int.Parse(t[3]),
                            Loosened = int.Parse(t[4]),
                            Emptied = int.Parse(t[5]),
                            EmptyAt = long.Parse(t[6])
                        };
                        if (t.Length >= 12)
                        {
                            z.LastDig = long.Parse(t[7]);
                            z.LastOreAt = long.Parse(t[8]);
                            z.RegrowMin = long.Parse(t[9]);
                            z.StillEmptyMax = long.Parse(t[10]);
                            z.EmptyLine = t[11];
                        }
                        c.Zones = c.Zones.Set(long.Parse(t[2]), z);
                    }
                    if (t[0] == "S" && t.Length >= 5)
                        c.Seen = c.Seen.Set(t[4], new[] { int.Parse(t[2]), int.Parse(t[3]) });
                }
            }
            catch (Exception e) { Note("Mine: the cave list did not load: " + e.Message); }
        }
    }
    void SaveCaves()
    {
        lock (m_caves)
        {
            List<string> lines = new();
            foreach (MineCave c in m_caves)
            {
                lines.Add(string.Join("\t", "C", c.Name, c.X.ToString(), c.Y.ToString(), c.Z.ToString(),
                    c.Radius.ToString(), c.Rune, c.Marked.ToString(), c.PeopleMax.ToString(), c.LastStrike.ToString(),
                    c.Cycle.ToString(), c.StrikeFrom.ToString(), c.RuneSerial.ToString("X")));
                foreach (KeyValuePair<long, MineZone> z in c.Zones)
                {
                    lines.Add(string.Join("\t", "Z", c.Name, z.Key.ToString(), z.Value.Ore.ToString(),
                        z.Value.Loosened.ToString(), z.Value.Emptied.ToString(), z.Value.EmptyAt.ToString(),
                    z.Value.LastDig.ToString(), z.Value.LastOreAt.ToString(), z.Value.RegrowMin.ToString(),
                    z.Value.StillEmptyMax.ToString(), Clean(z.Value.EmptyLine)));
                }

                foreach (KeyValuePair<string, int[]> s in c.Seen)
                    lines.Add(string.Join("\t", "S", c.Name, s.Value[0].ToString(), s.Value[1].ToString(), s.Key));
            }
            try
            {
                File.WriteAllLines(CavesPath, lines);
            }
            catch (Exception e) { Note("Mine: the cave list did not save: " + e.Message); }
        }
    }
    public List<string> CaveNames()
    {
        LoadCaves();
        List<string> l = new();
        lock (m_caves)
            foreach (MineCave c in m_caves)
                l.Add(c.Name);
        return l;
    }
    MineCave? CaveNamed(string name)
    {
        LoadCaves();
        lock (m_caves)
            return m_caves.Find(c => c.Name == name);
    }
    // The cave the circuit mines: mine.cave, empty for where the circuit starts.
    public string? PickedCave
    {
        get => MineCfg("cave");
        set => Cfg.Set(Server + "mine.cave", Clean(value!));
    }
    public string MarkCave(string name, int radius)
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "not connected";
        name = Clean(name);
        if (name.Length == 0)
            return "REFUSED a cave needs a name";
        int x, y, z;
        lock (k.State.Gate)
        {
            x = k.State.X;
            y = k.State.Y;
            z = k.State.Z;
        }
        LoadCaves();
        MineCave? c;
        lock (m_caves)
        {
            c = m_caves.Find(v => v.Name == name);
            if (c == null)
            {
                c = new MineCave { Name = name, Marked = Clock.UtcNow.Ticks };
                m_caves.Add(c);
            }
            c.X = x;
            c.Y = y;
            c.Z = z;
            if (radius > 0)
                c.Radius = radius;
        }
        PickedCave = name;
        CaveLook(k, c);
        SaveCaves();
        return "OK cave " + name + " marked at " + x + "," + y + "," + z + ", radius " + c.Radius;
    }
    // serial: the rune targeted, 0 for a label set by name.
    public string SetCaveRune(string name, string rune, uint serial = 0)
    {
        MineCave? c = CaveNamed(name);
        if (c == null)
            return "REFUSED no cave " + name;
        lock (m_caves)
        {
            c.Rune = Clean(RunePlace(rune));
            c.RuneSerial = serial;
        }
        SaveCaves();
        return "OK cave " + name + " rune " + (c.Rune.Length > 0 ? c.Rune : "none")
            + (serial != 0 ? " (" + serial.ToString("X") + ")" : "");
    }
    public string CaveRuneOf(string name)
    {
        MineCave? c = CaveNamed(name);
        return c == null ? "" : c.Rune;
    }
    public uint CaveRuneSerialOf(string name)
    {
        MineCave? c = CaveNamed(name);
        return c == null ? 0 : c.RuneSerial;
    }
    public string ForgetCave(string name)
    {
        LoadCaves();
        int n;
        lock (m_caves)
            n = m_caves.RemoveAll(c => c.Name == name);
        if (n == 0)
            return "REFUSED no cave " + name;
        if (PickedCave == name)
            PickedCave = "";
        SaveCaves();
        return "OK forgot cave " + name;
    }
    public string CaveText(string name)
    {
        MineCave? c = CaveNamed(name);
        if (c == null)
            return "No cave picked: the circuit mines within " + MineRadius
                + " tiles of where it starts and records nothing.";
        int ore = 0, loosened = 0, emptied = 0, emptiedOre = 0, emptyNow = 0;
        List<string> people = new();
        List<string> beasts = new();
        List<string> npcs = new();
        lock (m_caves)
        {
            foreach (MineZone z in c.Zones.Values)
            {
                ore += z.Ore;
                loosened += z.Loosened;
                emptied += z.Emptied;
                if (z.Emptied > 0)
                    emptiedOre += z.Ore;
                if (Resting(c, z))
                    emptyNow++;
            }
            List<KeyValuePair<string, int[]>> seen = new(c.Seen);
            seen.Sort((a, b) => b.Value[1].CompareTo(a.Value[1]));
            foreach (KeyValuePair<string, int[]> s in seen)
                (s.Value[0] == 1 ? people : s.Value[0] == 2 ? npcs : beasts).Add(s.Key + " x" + s.Value[1]);
        }
        string depth = emptied == 0 ? "no seam dug out yet"
            : "a seam gave about " + (emptiedOre * 10 / emptied / 10.0).ToString("0.0")
            + " ore digs before it emptied (" + emptied + " emptied)";
        return "Cave " + c.Name + " at " + c.X + "," + c.Y + "," + c.Z + ", radius " + c.Radius + ", rune "
            + (c.Rune.Length > 0 ? c.Rune : "none") + ".\n"
            + "Seams: " + c.Zones.Count + " chunks dug, " + ore + " ore digs, " + loosened + " loosened; " + depth
            + "; " + emptyNow + " empty now; " + CycleText(c) + ".\n"
            + "Busy: up to " + c.PeopleMax + " people at once"
            + (people.Count > 0 ? " (" + string.Join(", ", people.GetRange(0, Math.Min(6, people.Count))) + ")" : "")
            + ".\n"
            + "Vendors and NPCs: "
            + (npcs.Count > 0 ? string.Join(", ", npcs.GetRange(0, Math.Min(6, npcs.Count))) : "none seen") + ".\n"
            + "Creatures: "
            + (beasts.Count > 0 ? string.Join(", ", beasts.GetRange(0, Math.Min(8, beasts.Count))) : "none seen") + ".";
    }
    // Who is near: a human body (0x190 to 0x193) is a person unless its label called it invulnerable, a vendor or NPC
    // (kind 2; Damian, 2026-10-18: "rex is my vendor, it misidentified that as a player"); any other body a creature;
    // each by its name when known. A human body is single-clicked once for its label (caveClicked), never again.
    Set<uint> m_caveClicked = Set<uint>.Empty; readonly Lock m_caveClickedGate = new();
    void CaveLook(GameLink k, MineCave c)
    {
        if (c == null || c.Name.Length == 0)
            return;
        NameUnnamed(CaveLookReach);
        List<AssistItem> unlabelled = new();
        lock (k.State.Gate)
            lock (m_caveClickedGate)
            {
                foreach (KeyValuePair<uint, int[]> kv in k.State.Mobiles)
                {
                    int body;
                    k.State.Bodies.TryGet(kv.Key, out body);
                    if (body < 0x190 || body > 0x193 || k.State.Labelled.Contains(kv.Key)
                        || m_caveClicked.Contains(kv.Key))
                        continue;
                    m_caveClicked = m_caveClicked.Add(kv.Key);
                    if (Tiles(kv.Value[0], kv.Value[1], k.State.X, k.State.Y) <= CaveLookReach)
                        unlabelled.Add(new AssistItem { Serial = kv.Key, X = kv.Value[0], Y = kv.Value[1] });
                }
            }

        if (unlabelled.Count > 0)
            LabelsOf(k, unlabelled);
        List<string[]> who = new();
        int people = 0;
        lock (k.State.Gate)
        {
            foreach (KeyValuePair<uint, int[]> kv in k.State.Mobiles)
            {
                if (Tiles(kv.Value[0], kv.Value[1], k.State.X, k.State.Y) > CaveLookReach)
                    continue;
                int body;
                k.State.Bodies.TryGet(kv.Key, out body);
                int kind = body >= 0x190 && body <= 0x193 ? (k.State.Invulnerable.Contains(kv.Key) ? 2 : 1) : 0;
                if (kind == 1)
                    people++;
                string? name;
                if (!k.State.Names.TryGet(kv.Key, out name) || name.Length == 0)
                    name = "body " + body.ToString("X");
                who.Add(new[] { Clean(name), kind.ToString() });
            }
        }

        lock (m_caves)
        {
            c.PeopleMax = Math.Max(c.PeopleMax, people);
            foreach (string[] w in who)
            {
                int[]? s;
                if (!c.Seen.TryGet(w[0], out s))
                    c.Seen = c.Seen.Set(w[0], s = new[] { 0, 0 });
                s[0] = int.Parse(w[1]);
                s[1]++;
            }
        }
    }
}
