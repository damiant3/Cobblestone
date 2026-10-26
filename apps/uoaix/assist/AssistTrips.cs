#nullable enable
using System.Collections.Immutable;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace Uoaix.Assist;

// Trips by region (Damian, 2026-10-16: "a drop down of trips taken, and then some kind of meta-analysis of all the
// trips that shows me a categorization of the areas. like is good for noobs, good for mediums, or is it endgame? is
// there good resource harvest potential, or is it overrun with a spawn that has gone wild?", "use the region
// information from the server, and any other data we can cobble together to make basically a tactical and training
// analysis", "tell me where the good areas for picking up reagents, fighting for magic resists, or dropping magical
// gear", "a trip from a month ago, is not as relevant to today, but it still guideful until superceded").

// What the assist knows of a creature by name: its level (0 harmless, 1 newbie, 2 medium, 3 hard, 4 endgame), whether
// it casts spells (a fight with it trains Magic Resistance) and whether it carries magic items. The orc lord's row is
// Damian's (2026-10-16: "orc lords drop magicals without needing magic resist to survive"); every other row is the
// assist's estimate of the 1998 OSI bestiary, and a trip's measured kills, loot and resist lines stand beside it.
// Flags: c casts, m magic items, t tameable, h herdable, f feathers when carved, d hides when carved.
public static class TripLore
{
    public sealed class Lore
    {
        public int Tier; public bool Caster, Magic, Tame, Herd, Feathers, Hides;
    }
    static readonly ImmutableArray<string> s_rows = [
        "bird|0|tf", "magpie|0|tf", "crow|0|tf", "raven|0|tf", "sparrow|0|tf", "finch|0|tf", "wren|0|tf",
            "nuthatch|0|tf",
        "towhee|0|tf", "cuckoo|0|tf", "nightingale|0|tf", "lark|0|tf", "skylark|0|tf", "swallow|0|tf", "warbler|0|tf",
        "thrush|0|tf", "chickadee|0|tf", "plover|0|tf", "woodpecker|0|tf", "kingfisher|0|tf", "parrot|0|tf",
        "tern|0|tf",
        "starling|0|tf", "hawk|0|tf", "chicken|0|thf", "cat|0|t", "dog|0|t", "rabbit|0|t", "jack rabbit|0|t",
        "hare|0|t",
        "deer|0|td", "hind|0|td", "horse|0|thd", "llama|0|thd", "cow|0|thd", "sheep|0|th", "goat|0|thd",
        "mountain goat|0|td",
        "pig|0|th", "ostard|0|td", "squirrel|0|t", "ferret|0|t", "dolphin|0", "eagle|1|tf", "harpy|2|f",
        "rat|1|t", "sewer rat|1|t", "giant rat|1|t", "slime|1", "mongbat|1", "headless one|1", "headless|1",
        "zombie|1", "skeleton|1",
        "orc|1", "snake|1|t", "grey wolf|1|td", "gray wolf|1|td", "timber wolf|1|td", "black bear|1|td",
        "brown bear|1|td", "cougar|1|td", "panther|1|td",
        "gorilla|1|td", "walrus|1|td", "ratman|1", "bull|1|thd", "great hart|1|td", "boar|1|th", "imp|1|c",
        "giant toad|1", "bullfrog|1",
        "jackal|1|t", "hellcat|2", "orc captain|2|m", "orc lord|2|m", "orc mage|2|c", "lizardman|2", "ratman archer|2",
        "ratman mage|2|c",
        "ghoul|2", "spectre|2|c", "wraith|2|c", "shade|2|c", "bogle|2|c", "gazer|2|c", "earth elemental|2",
        "ettin|2|m", "ogre|2",
        "troll|2", "stone harpy|2|f", "giant spider|2", "frost spider|2", "polar bear|2|td", "grizzly bear|2|td",
        "dire wolf|2|td",
        "alligator|2|td", "giant serpent|2|t", "corpser|2", "reaper|2|c", "scorpion|2", "giant scorpion|2",
        "terathan drone|2",
        "terathan warrior|2", "ophidian warrior|2", "ophidian mage|2|c", "ophidian shaman|2|c", "water elemental|2|c",
        "air elemental|2|c", "hell hound|2", "brigand|2", "evil mage|2|c", "mummy|2", "bone knight|2", "bone magi|2|c",
        "ogre lord|3|m", "lich|3|cm", "gargoyle|3|cm", "stone gargoyle|3|m", "fire elemental|3|c", "efreet|3|cm",
        "wyvern|3|d",
        "drake|3|cmd", "cyclops|3|m", "frost troll|3", "sea serpent|3", "giant ice worm|3", "ophidian knight|3|m",
        "ophidian arch mage|3|cm", "ophidian matriarch|3|cm", "terathan avenger|3|cm", "terathan matriarch|3|cm",
        "poison elemental|3|cm", "blood elemental|3|cm", "lich lord|3|cm", "liche lord|3|cm", "deep sea serpent|3",
        "kraken|3",
        "titan|3|cm", "ice fiend|4|cm", "daemon|4|cm", "balron|4|cm", "dragon|4|cm", "white wyrm|4|cm",
        "ancient wyrm|4|cm",
        "shadow wyrm|4|cm", "wisp|3|c", "energy vortex|3", "blade spirit|3",
    ];
    // Nominal gold per unit of loot for the profit grade, the assist's estimate of 1998 NPC buy prices; gold coins are
    // exact.
    static readonly ImmutableArray<string> s_prices = [ "gold coin|1", "feather|1", "hides|2", "pile of hides|2",
        "hide|2",
        "cut of raw ribs|1", "raw ribs|1",
        "iron ore|4", "ore|4", "iron ingot|4", "log|1", "logs|1", "board|1", "wool|1", "cotton|1", "fish|1",
        "bandage|1", "arrow|1", "bolt|1",
        "black pearl|3", "bloodmoss|3", "garlic|2", "ginseng|2", "mandrake root|2", "nightshade|2", "sulfurous ash|2",
        "spiders' silk|2",
        "gem|20", "amethyst|30", "citrine|25", "diamond|90", "emerald|50", "ruby|40", "sapphire|40",
        "star sapphire|60", "tourmaline|30" ];
    public const int MagicItemGold = 150;
    static ValueMap<string, int>? prices;
    public static int Price(string item)
    {
        if (prices == null)
        {
            ValueMap<string, int> d = ValueMap<string, int>.Keyed(StringComparer.OrdinalIgnoreCase);
            foreach (string r in s_prices)
            {
                string[] p = r.Split('|');
                d = d.Set(p[0], int.Parse(p[1]));
            }
            prices = d;
        }
        if (AssistWindow.IsMagic(item))
            return MagicItemGold;
        return prices.Get(Bare(item), 0);
    }
    static Map<string, Lore>? s_table;
    static Map<string, Lore> Table()
    {
        if (s_table != null)
            return s_table;
        Map<string, Lore> t = Map<string, Lore>.Keyed(StringComparer.OrdinalIgnoreCase);
        foreach (string r in s_rows)
        {
            string[] p = r.Split('|');
            string f = p.Length > 2 ? p[2] : "";
            t = t.Set(p[0], new Lore { Tier = int.Parse(p[1]), Caster = f.Contains("c"), Magic = f.Contains("m"),
                Tame = f.Contains("t"), Herd = f.Contains("h"), Feathers = f.Contains("f"), Hides = f.Contains("d") });
        }
        return s_table = t;
    }
    // "an orc lord" is "orc lord"; an unknown name takes the longest known name it ends with ("a frost troll",
    // "troll").
    public static string Bare(string name)
    {
        string n = (name ?? "").Trim().ToLowerInvariant();
        foreach (string a in new[] { "a ", "an ", "the " })
            if (n.StartsWith(a))
            {
                n = n.Substring(a.Length);
                break;
            }
        return n;
    }
    // Cached by name: the map asks for every mark on every repaint.
    static Map<string, Lore> s_known = Map<string, Lore>.Empty; static readonly Lock s_knownGate = new();
    public static Lore? Of(string name)
    {
        string n = Bare(name);
        Lore? l;
        if (s_known.TryGet(n, out l))
            return l;
        Map<string, Lore> t = Table();
        if (!t.TryGet(n, out l))
        {
            string? best = null;
            foreach (string k in t.Keys)
                if ((n.EndsWith(" " + k)) && (best == null || k.Length > best.Length))
                    best = k;
            l = best == null ? null : t[best];
        }
        lock (s_knownGate)
        {
            if (s_known.Count > 5000)
                s_known = s_known.Clear();
            s_known = s_known.Set(n, l!);
        }
        return l;
    }
    // The server's region holding a tile, as UoRegions.Zone names it; a 256-tile map square where no region file is.
    public static string AreaAt(int x, int y, int z, out bool guarded)
    {
        guarded = false;
        string? zone = UoRegions.Loaded ? UoRegions.Zone(x, y, z, out guarded) : null;
        if (zone == null || zone == "outside every region")
            return "map square " + (x >> 8) + "," + (y >> 8);
        return zone;
    }
    // Mining land in the 1.25 TILEDATA land names: "rock" (mountainside) and "cave" (cave floor).
    static string[]? s_landNames;
    public static bool Mineable(int landId)
    {
        if (s_landNames == null)
        {
            string[] names = new string[0x4000];
            try
            {
                byte[] td = File.ReadAllBytes(Path.Combine(UoArt.Folder!, "TILEDATA.MUL"));
                for (int id = 0; id < 0x4000; id++)
                {
                    int at = (id / 32) * (4 + 32 * 26) + 4 + (id % 32) * 26 + 6;
                    if (at + 20 > td.Length)
                        break;
                    int n = 0;
                    while (n < 20 && td[at + n] != 0)
                        n++;
                    names[id] = Encoding.ASCII.GetString(td, at, n).Trim().ToLowerInvariant();
                }
            }
            catch { }
            s_landNames = names;
        }
        string? s = landId >= 0 && landId < s_landNames.Length ? s_landNames[landId] : null;
        return s == "rock" || s == "cave";
    }
    public const string ResistLine = "resisting magical energy";
    public static bool Wand(int graphic)
    {
        return graphic >= 0xDF2 && graphic <= 0xDF5;
    }
}

// One region's tallies within one trip.
public sealed class TripArea
{
    public string Name = null!;
    public int X, Y, Steps, Seconds, Cells, Trees, Rock, Water, Swarm, Deaths, Kills, Resist, ReagentPiles,
        ReagentsLooted, MagicLoot, Gold;
    public int MinHits = 101, MinStam = 101;
    public ValueMap<string, int> Gains = ValueMap<string, int>.Empty;
    public Map<string, int[]> Creatures = Map<string, int[]>.Empty;  // name: seen, hostile, chasers
    public ValueMap<string, int> Loot = ValueMap<string, int>.Empty;
}

// A running trip's regions: every confirmed step lands in the region the player stands in.
public sealed class TripRecorder
{
    public const int ChaseRange = 10, ChaseSteps = 25, FreshSeconds = 5, SampleEvery = 8, SampleRadius = 6,
        StepCapSeconds = 10;
    public Map<string, TripArea> Areas = Map<string, TripArea>.Empty;
    public string Current = ""; public int GuardedRun, Steps;
    DateTime m_lastStep = DateTime.MinValue; int m_gold = -1;
    Set<long> m_sampled = Set<long>.Empty;
    ValueMap<int, int> m_skills = ValueMap<int, int>.Empty;
    public TripArea Get(string name, int x, int y)
    {
        TripArea? a;
        if (!Areas.TryGet(name, out a))
            Areas = Areas.Set(name, a = new TripArea { Name = name, X = x, Y = y });
        return a;
    }
    public void Begin(ValueMap<int, int> now)
    {
        m_skills = now;
    }
    public static bool Hostile(int noto)
    {
        return noto >= 3 && noto <= 6;
    }
    // The caller holds the trip's lock; the mobiles are the trip's; at is the step's time on the caller's clock.
    public TripArea Step(DateTime at, int x, int y, int z, int hits, int maxHits, int stam, int maxStam, int goldNow,
        ValueMap<int, int> now, IEnumerable<AssistFeatures.TripMob> mobs, out bool guarded)
    {
        string area = TripLore.AreaAt(x, y, z, out guarded);
        Current = area;
        GuardedRun = guarded ? GuardedRun + 1 : 0;
        Steps++;
        TripArea a = Get(area, x, y);
        a.Steps++;
        if (m_gold >= 0 && goldNow > m_gold)
            a.Gold += goldNow - m_gold;
        m_gold = goldNow;
        if (m_lastStep != DateTime.MinValue)
            a.Seconds += (int)Math.Min(StepCapSeconds, (at - m_lastStep).TotalSeconds);
        m_lastStep = at;
        if (maxHits > 0)
            a.MinHits = Math.Min(a.MinHits, hits * 100 / maxHits);
        if (maxStam > 0)
            a.MinStam = Math.Min(a.MinStam, stam * 100 / maxStam);
        foreach (KeyValuePair<int, int> kv in now)
        {
            int was;
            if (!m_skills.TryGet(kv.Key, out was))
            {
                m_skills = m_skills.Set(kv.Key, kv.Value);
                continue;
            }
            if (kv.Value == was)
                continue;
            m_skills = m_skills.Set(kv.Key, kv.Value);
            int id = kv.Key - 1;
            string n = id >= 0 && id < AssistWindow.SkillNames.Length ? AssistWindow.SkillNames[id] : "skill " + id;
            a.Gains = a.Gains.Set(n, a.Gains.Get(n, 0) + kv.Value - was);
        }
        int near = 0;
        foreach (AssistFeatures.TripMob m in mobs)
        {
            if (m.Person || !Hostile(m.Noto) || (at - m.Last).TotalSeconds > FreshSeconds)
                continue;
            if (Math.Max(Math.Abs(m.X - x), Math.Abs(m.Y - y)) > ChaseRange)
                continue;
            near++;
            m.Close++;
        }
        a.Swarm = Math.Max(a.Swarm, near);
        if (Steps % SampleEvery == 1)
            Sample(a, x, y);
        return a;
    }
    // Harvest ground around the player, each tile counted once a trip: trees (lumber), rock and cave (mining), water
    // (fishing).
    void Sample(TripArea a, int x, int y)
    {
        for (int dy = -SampleRadius; dy <= SampleRadius; dy++)
        {
            for (int dx = -SampleRadius; dx <= SampleRadius; dx++)
            {
                int tx = x + dx, ty = y + dy;
                if (tx < 0 || ty < 0 || m_sampled.Contains((long)tx << 32 | (uint)ty))
                    continue;
                m_sampled = m_sampled.Add((long)tx << 32 | (uint)ty);
                UoRadar.Cells? b = UoRadar.Block(tx >> 3, ty >> 3);
                if (b == null)
                    continue;
                int cell = (ty & 7) * 8 + (tx & 7);
                a.Cells++;
                bool tree = false, water = UoTex.IsWater(b.Land[cell]);
                List<int>? st = b.ByCell[cell];
                if (st != null)
                    foreach (int v in st)
                    {
                        int k = UoFoliage.Kind(v & 0xFFFF);
                        if (k == 2)
                            tree = true;
                        else if (k == 3)
                            water = true;
                    }
                if (tree)
                    a.Trees++;
                if (water)
                    a.Water++;
                if (TripLore.Mineable(b.Land[cell]))
                    a.Rock++;
            }
        }
    }
    // The trip's data file beside its report: T (trip), A (region), C (creature), G (skill gain), L (loot) lines, tab
    // separated. kind: 0 by the button, 1 outside the guards, 2 the session.
    public void Write(string path, DateTime start, DateTime end, string server, int kind, int x0, int y0, int x1,
        int y1)
    {
        StringBuilder sb = new();
        sb.Append("T\t" + start.Ticks + "\t" + end.Ticks + "\t" + Clean(server) + "\t" + kind + "\t" + Steps + "\t"
            + x0 + "\t" + y0 + "\t" + x1 + "\t" + y1 + "\r\n");
        foreach (TripArea a in Areas.Values)
        {
            sb.Append("A\t" + Clean(a.Name) + "\t" + a.Steps + "\t" + a.Seconds + "\t" + a.Cells + "\t" + a.Trees
                + "\t" + a.Rock + "\t" + a.Water + "\t" + a.Swarm + "\t" + a.MinHits + "\t" + a.MinStam
                + "\t" + a.Deaths + "\t" + a.Kills + "\t" + a.Resist + "\t" + a.ReagentPiles + "\t" + a.ReagentsLooted
                + "\t" + a.MagicLoot + "\t" + a.X + "\t" + a.Y + "\t" + a.Gold + "\r\n");
            foreach (KeyValuePair<string, int[]> kv in a.Creatures)
                sb.Append("C\t" + Clean(a.Name) + "\t" + Clean(kv.Key) + "\t" + kv.Value[0] + "\t" + kv.Value[1] + "\t"
                    + kv.Value[2] + "\r\n");
            foreach (KeyValuePair<string, int> kv in a.Gains)
                sb.Append("G\t" + Clean(a.Name) + "\t" + Clean(kv.Key) + "\t" + kv.Value + "\r\n");
            foreach (KeyValuePair<string, int> kv in a.Loot)
                sb.Append("L\t" + Clean(a.Name) + "\t" + Clean(kv.Key) + "\t" + kv.Value + "\r\n");
        }
        File.WriteAllText(path, sb.ToString());
    }
    static string Clean(string s)
    {
        return (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }
}

// Every trip on disk, read back for the trip report and the region analysis.
public sealed class TripRecord
{
    public string Path = null!, Report = null!, Server = "";
    public DateTime Start, End; public bool Auto, Session; public int Steps;
    public readonly List<TripArea> Areas = new();
    public int Minutes => (int)(End - Start).TotalMinutes;
    public string Label()
    {
        List<string> names = new();
        List<TripArea> by = new(Areas);
        by.Sort((a, b) => b.Steps.CompareTo(a.Steps));
        foreach (TripArea a in by)
        {
            if (names.Count == 3)
                break;
            names.Add(a.Name);
        }
        return Start.ToString("yyyy-MM-dd HH:mm") + ", " + Minutes + " min"
            + (Auto ? " (auto)" : Session ? " (session)" : "")
            + (Areas.Count == 0 ? ", report only" : ": " + string.Join("; ", names));
    }
}

public static class TripBook
{
    public static string Folder => Path.Combine(Path.GetDirectoryName(AssistRelay.SettingsPath)!, "reports");
    // Newest first. A report written before trips kept data (2026-10-16) is listed with no regions.
    public static List<TripRecord> Load()
    {
        List<TripRecord> list = new();
        Set<string> withData = Set<string>.Keyed(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(Folder))
            return list;
        foreach (string p in Directory.GetFiles(Folder, "trip-*.trip"))
        {
            try
            {
                TripRecord? r = Read(p);
                if (r != null)
                {
                    list.Add(r);
                    withData = withData.Add(Path.ChangeExtension(p, ".md"));
                }
            }
            catch { }
        }
        foreach (string p in Directory.GetFiles(Folder, "trip-*.md"))
        {
            if (withData.Contains(p))
                continue;
            TripRecord r = new()
            {
                Report = p,
                Start = File.GetCreationTime(p),
                End = File.GetLastWriteTime(p)
            };
            // "# Trip 2026-10-15 21:51 to 22:23 (32 min) on login.uo1998.com:5000"
            try
            {
                string[] w = File.ReadAllLines(p)[0].Split(' ');
                DateTime s;
                int min;
                if (w.Length > 6 && w[1] == "Trip" && DateTime.TryParse(w[2] + " " + w[3], out s)
                    && int.TryParse(w[6].TrimStart('('), out min))
                {
                    r.Start = s;
                    r.End = s.AddMinutes(min);
                }
            }
            catch { }
            list.Add(r);
        }
        list.Sort((a, b) => b.Start.CompareTo(a.Start));
        return list;
    }
    static TripRecord? Read(string path)
    {
        TripRecord r = new()
        {
            Path = path,
            Report = Path.ChangeExtension(path, ".md")
        };
        Map<string, TripArea> by = Map<string, TripArea>.Empty;
        foreach (string line in File.ReadAllLines(path))
        {
            string[] t = line.Split('\t');
            if (t.Length < 2)
                continue;
            if (t[0] == "T" && t.Length >= 6)
            {
                r.Start = new DateTime(long.Parse(t[1]));
                r.End = new DateTime(long.Parse(t[2]));
                r.Server = t[3];
                r.Auto = t[4] == "1";
                r.Session = t[4] == "2";
                r.Steps = int.Parse(t[5]);
            }
            else if (t[0] == "A" && t.Length >= 19)
            {
                int[] n = new int[18];
                for (int i = 0; i < 18 && i + 2 < t.Length; i++)
                    int.TryParse(t[i + 2], out n[i]);
                TripArea a = new()
                {
                    Name = t[1],
                    Steps = n[0],
                    Seconds = n[1],
                    Cells = n[2],
                    Trees = n[3],
                    Rock = n[4],
                    Water = n[5],
                    Swarm = n[6],
                    MinHits = n[7],
                    MinStam = n[8],
                    Deaths = n[9],
                    Kills = n[10],
                    Resist = n[11],
                    ReagentPiles = n[12],
                    ReagentsLooted = n[13],
                    MagicLoot = n[14],
                    X = n[15],
                    Y = n[16],
                    Gold = n[17]
                };
                by = by.Set(a.Name, a);
                r.Areas.Add(a);
            }
            else
            {
                TripArea? a;
                if (!by.TryGet(t[1], out a))
                    continue;
                int v;
                if (t[0] == "C" && t.Length >= 6)
                    a.Creatures = a.Creatures.Set(t[2], new[] { int.Parse(t[3]), int.Parse(t[4]), int.Parse(t[5]) });
                else if (t[0] == "G" && t.Length >= 4 && int.TryParse(t[3], out v))
                    a.Gains = a.Gains.Set(t[2], v);
                else if (t[0] == "L" && t.Length >= 4 && int.TryParse(t[3], out v))
                    a.Loot = a.Loot.Set(t[2], v);
            }
        }
        return r.Start == DateTime.MinValue ? null : r;
    }

    // ---------- the region analysis ----------
    // Recency: a trip's weight halves every HalfLifeDays, and a region's grades are the weighted mean over its trips in
    // the period, so a month-old trip decides a region alone and gives way as newer trips there arrive.
    public const double HalfLifeDays = 14;
    public sealed class Grade
    {
        public string Area = null!;
        public int Trips, Minutes, Steps, Deaths; public DateTime Newest; public double Weight;
        public double Level, Density, Chasers, Swarm, ReagentRate, Resist, Casters, MagicFoes, MagicLoot, Trees, Rock,
            Water;
        public int WorstHits = 101, WorstStam = 101; public DateTime WorstAt;
        public ValueMap<string, double> GainPerHour = ValueMap<string, double>.Empty;
        public ValueMap<string, int> Foes = ValueMap<string, int>.Empty;
        public Set<string> MagicNames = Set<string>.Empty, CasterNames = Set<string>.Empty;
        public int ReagentsLooted;
        public double Tame, Herd, FeatherSources, HideSources, GoldPerHour, Hassle, GainPerHourAll;
        public int Feathers, Hides; public double Gold, Value, Hours;
        public ValueMap<string, int> Tameable = ValueMap<string, int>.Empty, Herdable = ValueMap<string, int>.Empty;
    }
    // Gold from the status packet's gold rising, plus loot at TripLore.Price; gold coins in the loot are that same
    // gold.
    public static double Value(TripArea a)
    {
        double v = a.Gold;
        foreach (KeyValuePair<string, int> kv in a.Loot)
            if (TripLore.Bare(kv.Key) != "gold coin")
                v += (double)kv.Value * TripLore.Price(kv.Key);
        return v;
    }
    static int Count(TripArea a, string word)
    {
        int n = 0;
        foreach (KeyValuePair<string, int> kv in a.Loot)
            if (kv.Key.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                n += kv.Value;
        return n;
    }
    // now: the local time the grades are for.
    public static List<Grade> Grades(List<TripRecord> trips, int days, DateTime now)
    {
        Map<string, Grade> by = Map<string, Grade>.Empty;
        Map<string, Map<string, double[]>> gainW = Map<string, Map<string, double[]>>.Empty;
        foreach (TripRecord r in trips)
        {
            if (r.Areas.Count == 0 || days > 0 && (now - r.Start).TotalDays > days)
                continue;
            double w = Math.Pow(0.5, Math.Max(0, (now - r.Start).TotalDays) / HalfLifeDays);
            foreach (TripArea a in r.Areas)
            {
                if (a.Steps == 0)
                    continue;
                Grade? g;
                if (!by.TryGet(a.Name, out g))
                    by = by.Set(a.Name, g = new Grade { Area = a.Name });
                g.Trips++;
                g.Minutes += a.Seconds / 60;
                g.Steps += a.Steps;
                g.Deaths += a.Deaths;
                g.Weight += w;
                if (r.Start > g.Newest)
                    g.Newest = r.Start;
                int[] tiers = new int[5];
                int hostile = 0, chasers = 0, casters = 0, magic = 0, tame = 0, herd = 0, feathers = 0, hides = 0;
                foreach (KeyValuePair<string, int[]> kv in a.Creatures)
                {
                    TripLore.Lore? lore = TripLore.Of(kv.Key);
                    int seenHostile = kv.Value[1];
                    chasers += kv.Value[2];
                    if (lore != null)
                    {
                        string bare = TripLore.Bare(kv.Key);
                        if (lore.Tame)
                        {
                            tame += kv.Value[0];
                            g.Tameable = g.Tameable.Set(bare, g.Tameable.Get(bare, 0) + kv.Value[0]);
                        }
                        if (lore.Herd)
                        {
                            herd += kv.Value[0];
                            g.Herdable = g.Herdable.Set(bare, g.Herdable.Get(bare, 0) + kv.Value[0]);
                        }
                        if (lore.Feathers)
                            feathers += kv.Value[0];
                        if (lore.Hides)
                            hides += kv.Value[0];
                    }
                    if (seenHostile == 0)
                        continue;
                    hostile += seenHostile;
                    g.Foes = g.Foes.Set(kv.Key, g.Foes.Get(kv.Key, 0) + seenHostile);
                    if (lore == null)
                        continue;
                    tiers[lore.Tier] += seenHostile;
                    if (lore.Caster)
                    {
                        casters += seenHostile;
                        g.CasterNames = g.CasterNames.Add(TripLore.Bare(kv.Key));
                    }
                    if (lore.Magic)
                    {
                        magic += seenHostile;
                        g.MagicNames = g.MagicNames.Add(TripLore.Bare(kv.Key));
                    }
                }
                int level = tiers[4] > 0 || tiers[3] >= 3 ? 3 : tiers[3] > 0 || tiers[2] >= 2 ? 2 : hostile > 0 ? 1 : 0;
                if (a.Deaths > 0)
                    level = Math.Min(3, level + 1);
                g.Level += w * level;
                g.Density += w * hostile * 100.0 / a.Steps;
                g.Chasers += w * chasers;
                g.Swarm += w * a.Swarm;
                g.ReagentRate += w * a.ReagentPiles * 100.0 / a.Steps;
                g.ReagentsLooted += a.ReagentsLooted;
                int resistGain = a.Gains.Get("Magic Resistance", 0);
                g.Resist += w * (a.Resist + Math.Max(0, resistGain));
                g.Casters += w * casters;
                g.MagicFoes += w * magic;
                g.MagicLoot += w * a.MagicLoot;
                if (a.Cells > 0)
                {
                    g.Trees += w * a.Trees * 100.0 / a.Cells;
                    g.Rock += w * a.Rock * 100.0 / a.Cells;
                    g.Water += w * a.Water * 100.0 / a.Cells;
                }
                if (a.MinHits < g.WorstHits)
                {
                    g.WorstHits = a.MinHits;
                    g.WorstAt = r.Start;
                }
                g.WorstStam = Math.Min(g.WorstStam, a.MinStam);
                g.Tame += w * tame * 100.0 / a.Steps;
                g.Herd += w * herd * 100.0 / a.Steps;
                g.FeatherSources += w * feathers * 100.0 / a.Steps;
                g.HideSources += w * hides * 100.0 / a.Steps;
                g.Feathers += Count(a, "feather");
                g.Hides += Count(a, "hide");
                double hours = Math.Max(60, a.Seconds) / 3600.0, value = Value(a);
                int gainAll = 0;
                foreach (KeyValuePair<string, int> kv in a.Gains)
                    gainAll += Math.Max(0, kv.Value);
                g.Gold += a.Gold;
                g.Value += value;
                g.Hours += hours;
                g.GoldPerHour += w * value / hours;
                g.GainPerHourAll += w * gainAll / 10.0 / hours;
                g.Hassle += w
                    * (a.Deaths * 3 + (a.MinHits < 25 ? 2 : a.MinHits < 50 ? 1 : 0)
                    + (chasers >= OverrunChasers ? 2 : chasers >= 2 ? 1 : 0) + (a.Swarm >= OverrunSwarm ? 1 : 0));
                Map<string, double[]>? gw;
                if (!gainW.TryGet(a.Name, out gw))
                    gw = Map<string, double[]>.Empty;
                foreach (KeyValuePair<string, int> kv in a.Gains)
                {
                    double[]? s;
                    if (!gw.TryGet(kv.Key, out s))
                        gw = gw.Set(kv.Key, s = new double[2]);
                    s[0] += w * kv.Value / 10.0;
                    s[1] += w * Math.Max(60, a.Seconds) / 3600.0;
                }
                gainW = gainW.Set(a.Name, gw);
            }
        }
        foreach (Grade g in by.Values)
        {
            double w = Math.Max(1e-9, g.Weight);
            g.Level /= w;
            g.Density /= w;
            g.Chasers /= w;
            g.Swarm /= w;
            g.ReagentRate /= w;
            g.Resist /= w;
            g.Casters /= w;
            g.MagicFoes /= w;
            g.MagicLoot /= w;
            g.Trees /= w;
            g.Rock /= w;
            g.Water /= w;
            g.Tame /= w;
            g.Herd /= w;
            g.FeatherSources /= w;
            g.HideSources /= w;
            g.GoldPerHour /= w;
            g.GainPerHourAll /= w;
            g.Hassle /= w;
            Map<string, double[]>? gw;
            if (gainW.TryGet(g.Area, out gw))
                foreach (KeyValuePair<string, double[]> kv in gw)
                    if (kv.Value[1] > 0)
                        g.GainPerHour = g.GainPerHour.Set(kv.Key, kv.Value[0] / kv.Value[1]);
        }
        List<Grade> list = new(by.Values);
        list.Sort((a, b) => b.Steps.CompareTo(a.Steps));
        return list;
    }
    static readonly ImmutableArray<string> s_levelNames = [ "Safe", "Newbie", "Medium", "Endgame" ];
    public static ImmutableArray<string> LevelNames => s_levelNames;
    public static string Level(Grade g)
    {
        return s_levelNames[Math.Max(0, Math.Min(3, (int)Math.Round(g.Level)))]
            + (g.Deaths > 0 ? " (you died here)" : "");
    }
    // Spawn gone wild: OverrunSwarm hostiles within ChaseRange at once, or OverrunChasers creatures keeping up with
    // you.
    public const double OverrunSwarm = 8, OverrunChasers = 6, BusySwarm = 4, BusyDensity = 5;
    public static string Spawn(Grade g)
    {
        if (g.Swarm >= OverrunSwarm || g.Chasers >= OverrunChasers)
            return "Overrun";
        if (g.Swarm >= BusySwarm || g.Density >= BusyDensity)
            return "Busy";
        return g.Density > 0 ? "Light" : "Empty";
    }
    // Percent of the ground seen: Good at GoodGround, Some at half.
    public const double GoodGround = 15;
    public static string Harvest(Grade g)
    {
        List<string> parts = new();
        Action<string, double> add = (n, v) => { if (v
            >= GoodGround) parts.Add(n + " good"); else if (v >= GoodGround / 2) parts.Add(n + " some"); };
        add("lumber", g.Trees);
        add("mining", g.Rock);
        add("fishing", g.Water);
        return parts.Count == 0 ? "poor" : string.Join(", ", parts);
    }
    public const double GoodReagents = 1;  // loose reagent piles per 100 steps
    public static string Reagents(Grade g)
    {
        string s = g.ReagentRate >= GoodReagents ? "Good" : g.ReagentRate > 0 ? "Some" : "None seen";
        return s + " (" + g.ReagentRate.ToString("0.0") + "/100 steps"
            + (g.ReagentsLooted > 0 ? ", " + g.ReagentsLooted + " looted" : "") + ")";
    }
    public static string Resist(Grade g)
    {
        if (g.Resist >= 3 || g.Casters >= 3)
            return "Good (" + string.Join(", ", g.CasterNames.Ordered(StringComparer.Ordinal)) + ")";
        if (g.Resist > 0 || g.Casters > 0)
            return "Some (" + string.Join(", ", g.CasterNames.Ordered(StringComparer.Ordinal)) + ")";
        return "None";
    }
    // Magic without resist is the line Damian asked for: carriers of magic items and no spell casters.
    public static string Magic(Grade g)
    {
        if (g.MagicFoes <= 0 && g.MagicLoot <= 0)
            return "None";
        string who = string.Join(", ", g.MagicNames.Ordered(StringComparer.Ordinal));
        return (g.Casters <= 0 ? "No resist needed: " : "Casters too: ") + who
            + (g.MagicLoot > 0 ? " (" + g.MagicLoot.ToString("0.#") + " magic items looted a trip)" : "");
    }
    public static string CloseCalls(Grade g)
    {
        List<string> parts = new();
        if (g.Deaths > 0)
            parts.Add(g.Deaths + " death" + (g.Deaths == 1 ? "" : "s"));
        if (g.WorstHits <= 100)
            parts.Add("hits down to " + g.WorstHits + "% on " + g.WorstAt.ToString("MM-dd"));
        if (g.WorstStam <= 100)
            parts.Add("stamina down to " + g.WorstStam + "%");
        if (g.Chasers >= 1)
            parts.Add(g.Chasers.ToString("0.#") + " chasers a trip");
        return string.Join(", ", parts);
    }
    public static string Training(Grade g)
    {
        List<KeyValuePair<string, double>> l = new(g.GainPerHour);
        l.RemoveAll(kv => kv.Value <= 0);
        l.Sort((a, b) => b.Value.CompareTo(a.Value));
        List<string> parts = new();
        foreach (KeyValuePair<string, double> kv in l)
        {
            if (parts.Count == 3)
                break;
            parts.Add(kv.Key + " +" + kv.Value.ToString("0.0") + "/h");
        }
        return parts.Count == 0 ? "" : string.Join(", ", parts);
    }
    // Worth the hassle (Damian, 2026-10-16: "grade the trip as profitable or just a waste of time for all the hassle
    // you deal with"): gold an hour against a hassle score (a death 3, hits under 25% 2 or under 50% 1, OverrunChasers
    // chasers 2 or two chasers 1, an overrun swarm 1). The gold bars are the assist's estimate.
    public const double ProfitGold = 300, WorthGold = 100, ProfitHassle = 2, WorthHassle = 3;
    public static string Profit(Grade g)
    {
        string grade = g.GoldPerHour >= ProfitGold && g.Hassle <= ProfitHassle ? "Profitable"
            : g.GoldPerHour >= WorthGold && g.Hassle <= WorthHassle ? "Worth it"
            : g.GoldPerHour >= WorthGold ? "Not worth the hassle" : "Waste of time";
        return grade + " (" + g.GoldPerHour.ToString("0") + " gp/h, hassle " + g.Hassle.ToString("0.#") + ")";
    }
    // Useful for training (Damian, 2026-10-16: "also grade as useful for training", "like what level of combat"): skill
    // points an hour across every skill, and the combat level the region offers.
    public const double GreatTraining = 1.0, GoodTraining = 0.3;
    public static string TrainingGrade(Grade g)
    {
        string grade = g.GainPerHourAll >= GreatTraining ? "Great"
            : g.GainPerHourAll >= GoodTraining ? "Good" : g.GainPerHourAll > 0 ? "Slow" : "None";
        string top = Training(g);
        return grade + " (" + g.GainPerHourAll.ToString("0.0") + " points/h, "
            + s_levelNames[Math.Max(0, Math.Min(3, (int)Math.Round(g.Level)))].ToLowerInvariant() + " combat"
            + (top.Length > 0 ? "; " + top : "") + ")";
    }
    // Taming, herding, feathers and hides (Damian, 2026-10-16: "how useful for taming/herding and resource harvest of
    // feathers, hides"): animals of each use seen per 100 steps; Good at GoodAnimals.
    public const double GoodAnimals = 3;
    static string Rate(double v)
    {
        return (v >= GoodAnimals ? "Good" : v > 0 ? "Some" : "None") + " (" + v.ToString("0.0") + "/100 steps)";
    }
    static string Top(ValueMap<string, int> d)
    {
        List<KeyValuePair<string, int>> l = new(d);
        l.Sort((a, b) => b.Value.CompareTo(a.Value));
        List<string> parts = new();
        foreach (KeyValuePair<string, int> kv in l)
        {
            if (parts.Count == 4)
                break;
            parts.Add(kv.Key + " " + kv.Value);
        }
        return string.Join(", ", parts);
    }
    public static string Taming(Grade g)
    {
        string t = Top(g.Tameable);
        return Rate(g.Tame) + (t.Length > 0 ? ": " + t : "");
    }
    public static string Herding(Grade g)
    {
        string t = Top(g.Herdable);
        return Rate(g.Herd) + (t.Length > 0 ? ": " + t : "");
    }
    public static string FeathersHides(Grade g)
    {
        return "feathers " + Rate(g.FeatherSources) + (g.Feathers > 0 ? ", " + g.Feathers + " looted" : "")
            + "; hides " + Rate(g.HideSources) + (g.Hides > 0 ? ", " + g.Hides + " looted" : "");
    }
    public static string Foes(Grade g)
    {
        List<KeyValuePair<string, int>> l = new(g.Foes);
        l.Sort((a, b) => b.Value.CompareTo(a.Value));
        List<string> parts = new();
        foreach (KeyValuePair<string, int> kv in l)
        {
            if (parts.Count == 4)
                break;
            parts.Add(TripLore.Bare(kv.Key) + " " + kv.Value);
        }
        return string.Join(", ", parts);
    }
    // One line per question, the best region for each in the period.
    public static List<string> BestFor(List<Grade> grades, DateTime now)
    {
        List<string> lines = new();
        Action<string, Func<Grade, double>, Func<Grade, bool>> best = (what, score, ok) =>
        {
            Grade? top = null;
            foreach (Grade g in grades)
                if (ok(g) && (top == null || score(g) > score(top)))
                    top = g;
            lines.Add(what + ": "
                + (top == null ? "no trip shows one yet" : top.Area + " (" + Age(top.Newest, now) + ")"));
        };
        best("Magic gear without needing resist", g => g.MagicFoes + g.MagicLoot * 5,
            g => (g.MagicFoes > 0 || g.MagicLoot > 0) && g.Casters <= 0);
        best("Magic resist training", g => g.Resist + g.Casters, g => g.Resist > 0 || g.Casters > 0);
        best("Reagent pickup", g => g.ReagentRate, g => g.ReagentRate > 0);
        best("Lumber", g => g.Trees, g => g.Trees >= GoodGround / 2);
        best("Mining", g => g.Rock, g => g.Rock >= GoodGround / 2);
        best("Most profitable", g => g.GoldPerHour - g.Hassle * 50, g => g.GoldPerHour > 0);
        best("Training", g => g.GainPerHourAll, g => g.GainPerHourAll > 0);
        best("Taming", g => g.Tame, g => g.Tame > 0);
        best("Herding", g => g.Herd, g => g.Herd > 0);
        best("Feathers", g => g.FeatherSources + g.Feathers, g => g.FeatherSources > 0 || g.Feathers > 0);
        best("Hides", g => g.HideSources + g.Hides, g => g.HideSources > 0 || g.Hides > 0);
        best("Newbie hunting", g => g.Density, g => Math.Round(g.Level) == 1 && Spawn(g) != "Overrun");
        best("Worst spawn (avoid)", g => g.Swarm + g.Chasers, g => Spawn(g) == "Overrun");
        return lines;
    }
    public static string Age(DateTime at, DateTime now)
    {
        TimeSpan d = now - at;
        return d.TotalDays >= 1 ? (int)d.TotalDays + " d ago"
            : d.TotalHours >= 1 ? (int)d.TotalHours + " h ago" : (int)d.TotalMinutes + " min ago";
    }
    // The trip report sub tab: one block per region, most walked first.
    public static List<string> Report(TripRecord r, DateTime now)
    {
        List<string> lines = new()
        { r.Label() };
        if (r.Areas.Count == 0)
        {
            lines.Add("This trip predates region data: Open the full report for its creatures and items.");
            return lines;
        }
        TripRecord whole = new()
        {
            Start = r.Start,
            End = r.End
        };
        whole.Areas.AddRange(r.Areas);
        List<Grade> all = Grades(new List<TripRecord> { whole }, 0, now);
        if (all.Count > 0)
        {
            double value = 0, hours = 0, hassle = 0, gain = 0;
            foreach (Grade x in all)
            {
                value += x.Value;
                hours += x.Hours;
                hassle += x.Hassle;
                gain += x.GainPerHourAll * x.Hours;
            }
            Grade sum = new()
            {
                GoldPerHour = hours > 0 ? value / hours : 0,
                Hassle = hassle
            };
            lines.Add("Verdict: " + Profit(sum) + "; " + value.ToString("0")
                + " gp in gold and loot (loot at estimated prices); training "
                + (hours > 0 ? (gain / hours).ToString("0.0") : "0") + " skill points/h");
        }
        List<TripArea> by = new(r.Areas);
        by.Sort((a, b) => b.Steps.CompareTo(a.Steps));
        foreach (TripArea a in by)
        {
            TripRecord copy = new()
            {
                Start = r.Start,
                End = r.End
            };
            copy.Areas.Add(a);
            List<Grade> g = Grades(new List<TripRecord> { copy }, 0, now);
            if (g.Count == 0)
                continue;
            Grade x = g[0];
            lines.Add("");
            lines.Add(a.Name + ": " + a.Steps + " steps, " + a.Seconds / 60 + " min, first at " + a.X + "," + a.Y);
            lines.Add("  Level " + Level(x) + "; spawn " + Spawn(x) + " (peak " + a.Swarm + " hostiles within "
                + TripRecorder.ChaseRange + " tiles)");
            string close = CloseCalls(x);
            if (close.Length > 0)
                lines.Add("  Close calls: " + close);
            lines.Add("  Harvest " + Harvest(x) + "; reagents " + Reagents(x));
            lines.Add("  " + Profit(x) + "; training " + TrainingGrade(x));
            lines.Add("  Resist training " + Resist(x) + "; magic drops " + Magic(x));
            lines.Add("  Taming " + Taming(x) + "; herding " + Herding(x));
            lines.Add("  Animal harvest: " + FeathersHides(x));
            string foes = Foes(x);
            if (foes.Length > 0)
                lines.Add("  Hostiles: " + foes);
            string tr = Training(x);
            if (tr.Length > 0)
                lines.Add("  Skill gains: " + tr);
            if (a.Kills > 0 || a.Loot.Count > 0)
            {
                List<string> loot = new();
                foreach (KeyValuePair<string, int> kv in a.Loot)
                    loot.Add(kv.Value + " " + kv.Key);
                lines.Add("  Kills " + a.Kills + (loot.Count > 0 ? "; loot " + string.Join(", ", loot) : ""));
            }
        }
        return lines;
    }
}
