#nullable enable
using System.Collections.Immutable;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

// The NPC atlas (Damian, 2026-10-15: "map should update to show shop keeps, bankers, other npcs. add filters by type
// and subtype. show regions on the map, and drill down for specific npc item lists when the vendor buy list comes up",
// "add a shopkeeps and merchants as a main tab"). Every mobile seen, typed by the title in its name ("Bob the banker"):
// bankers, shopkeepers, healers, guards and other titled NPCs are kept per server in atlas-<server>.txt; people, ghosts
// and creatures only while seen. A vendor's buy list (0x74) is kept on the vendor.

namespace Uoaix.Assist;

public sealed class AtlasNpc
{
    public uint Serial;
    public string Name = "", Type = "", Sub = ""; public int Body, X, Y; public bool Criminal, ShopPack;
    // A player vendor's wares as its catalogue read them (VendorWares.Describe); empty until one is read. Vended: it
    // said the player vendor's line (AssistFeatures.VendedSays), so it is a Player vendor whatever its name.
    public string Wares = ""; public bool Vended;
    public DateTime Last, ItemsAt; public List<string> Items = new();
    public bool Kept => Type == "Banker" || Type == "Shopkeeper" || Type == "Player vendor" || Type == "Healer"
        || Type == "Guard" || Type == "NPC";
}

// Houses, guildstones and trades (Damian, 2026-10-15: "record all transactions, buy and sell with each npc vendor and
// also keep track of player vendors and houses, showing house decay level and any other details. if you see
// guildstones, record them and show them on the map").
public sealed class AtlasPlace
{
    public uint Serial;
    public string Kind = "", Name = "", Details = ""; public int Graphic, X, Y; public DateTime Last;
}
public sealed class AtlasTrade
{
    public DateTime At;
    public string Kind = "", Vendor = "", Item = ""; public uint VendorSerial; public int X, Y, Amount, Price;
}
// One buy list or catalogue a vendor showed: when it began and its rows, as AtlasNpc.Items holds them.
public sealed class AtlasSurvey
{
    public DateTime At; public List<string> Items = new();
}

// Smithing value (Damian, 2026-10-15: "calculate the best ingot efficient item to make and sell the npcs based on
// smithy skill ... remember sales and how many and what prices. note that prices change based on quality"): ingot costs
// from the smith's menus, each build with its quality and the skill then, each NPC offer and sale with the item's
// quality, kept in smithing-<server>.txt beside the atlas (tab-separated, one record a line, first field the kind).
// The same book serves tailoring (Damian, 2026-10-16: "a \"best items to make\" for tailor like smithy, that is context
// aware of char skill and prices they sell at ... i sometimes sell the vendor down and i can't get all the sales i need
// there"), its cost in cloth from the tailor's menus ("Fancy shirt - takes 8 cloth."), kept in tailoring-<server>.txt.
// Demand: uo1998 kept fancy shirts at 27 gp at every vendor (trades 2026-10-16: Nana 60, Morena 36, Ella 24, Lynn 9
// sold), so selling a vendor down is its sell list leaving items out, not a lower price. Each sell list records, per
// item the player carries, how many the vendor offered to buy against how many were carried; the latest within
// DemandHours per vendor is that vendor's demand.
public sealed class SmithRow
{
    public string Item = null!;
    public int Cost, Builds, Exceptional, Failed, PriceNormal, PriceExceptional, Sold, SoldGold, Wanted, Full;
    public double PerIngot; public string Basis = null!, Demand = "";
}
public sealed class SmithBook
{
    readonly string m_path; readonly Lock m_gate = new();
    public const int DemandHours = 24;
    Map<string, string[]> m_wants = Map<string, string[]>.Empty;  // vendor|key: ticks, vendor, key, offered, carried
    ValueMap<string, int> m_costs = ValueMap<string, int>.Empty;
    Map<string, string> m_names = Map<string, string>.Empty;
    readonly List<string[]> m_crafts = new();  // ticks, serial, key, quality, skill
    readonly List<string[]> m_sales = new();   // ticks, vendor, key, quality, amount, price
    ValueMap<string, int> m_offers = ValueMap<string, int>.Empty;  // key|quality, latest price
    Map<uint, string> m_quality = Map<uint, string>.Empty;
    public const int NearSkill = 50, NearBuildsMin = 3;
    // The features' clock, its local time the book's stamps (AssistTesting.md T2).
    readonly IClock m_clock;
    DateTime Now => m_clock.UtcNow.ToLocalTime();
    public SmithBook(string path, IClock clock)
    {
        m_path = path;
        m_clock = clock;
        try
        {
            if (File.Exists(path))
                foreach (string line in File.ReadAllLines(path))
                    Apply(line.Split('\t'));
        }
        catch { }
    }
    // "Build Platemail legs, 20 metal." and "platemail legs" name the same item, as "Fancy shirt - takes 8 cloth." and
    // "fancy shirt" do.
    static string Bare(string name)
    {
        string s = name.Trim().TrimEnd('.');
        if (s.StartsWith("Build ", StringComparison.OrdinalIgnoreCase))
            s = s.Substring(6);
        int c = s.IndexOf(',');
        if (c >= 0)
            s = s.Substring(0, c);
        c = s.IndexOf(" - ");
        if (c >= 0)
            s = s.Substring(0, c);
        return s.Trim();
    }
    public static string Key(string name)
    {
        string s = Bare(name);
        s = string.Join(" ", s.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        if (s.StartsWith("a "))
            s = s.Substring(2);
        else if (s.StartsWith("an "))
            s = s.Substring(3);
        if (s.EndsWith("s") && s.Length > 3)
            s = s.Substring(0, s.Length - 1);
        return s;
    }
    public static string Display(string name)
    {
        return Bare(name);
    }
    void Apply(string[] t)
    {
        if (t.Length < 2)
            return;
        int n;
        uint u;
        switch (t[0])
        {
            case "cost":
                if (t.Length >= 4 && int.TryParse(t[3], out n))
                {
                    m_costs = m_costs.Set(t[1], n);
                    m_names = m_names.Set(t[1], t[2]);
                }
                break;
            case "craft":
                if (t.Length >= 6)
                {
                    m_crafts.Add(new[] { t[1], t[2], t[3], t[4], t[5] });
                    if (uint.TryParse(t[2], NumberStyles.HexNumber, null, out u))
                        m_quality = m_quality.Set(u, t[4]);
                }
                break;
            case "sale":
                if (t.Length >= 7)
                    m_sales.Add(new[] { t[1], t[2], t[3], t[4], t[5], t[6] });
                break;
            case "offer":
                if (t.Length >= 5 && int.TryParse(t[4], out n))
                    m_offers = m_offers.Set(t[2] + "|" + t[3], n);
                break;
            case "want":
                if (t.Length >= 6)
                    m_wants = m_wants.Set(t[2] + "|" + t[3], new[] { t[1], t[2], t[3], t[4], t[5] });
                break;
        }
    }
    // A vendor's latest demand for an item, as the Tailoring value shows it ("15 of 20", "4+"); empty when none within
    // DemandHours.
    public string WantAt(string vendor, string item)
    {
        string[]? w;
        lock (m_gate)
        {
            if (!m_wants.TryGet(vendor + "|" + Key(item), out w))
                return "";
        }
        long ticks;
        int off, car;
        if (!long.TryParse(w[0], out ticks) || !int.TryParse(w[3], out off) || !int.TryParse(w[4], out car))
            return "";
        if ((Now - new DateTime(ticks)).TotalHours > DemandHours)
            return "";
        return off < car ? off + " of " + car : off + "+";
    }
    // A vendor's sell list offered to buy this many of an item the player carried this many of.
    public void Want(string vendor, string item, int offered, int carried)
    {
        string key = Key(item);
        string[]? had;
        lock (m_gate)
        {
            if (m_wants.TryGet(vendor + "|" + key, out had) && had[3] == offered.ToString()
                && had[4] == carried.ToString() && (Now - new DateTime(long.Parse(had[0]))).TotalHours < 1)
                return;
        }
        Add("want", Now.Ticks.ToString(), vendor, key, offered.ToString(), carried.ToString());
    }
    void Add(params string[] t)
    {
        for (int i = 0; i < t.Length; i++)
            t[i] = t[i].Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        lock (m_gate)
        {
            Apply(t);
            try
            {
                File.AppendAllText(m_path, string.Join("\t", t) + "\r\n");
            }
            catch { }
        }
    }
    public void Cost(string menuText, int metal)
    {
        string key = Key(menuText);
        int had;
        lock (m_gate)
        {
            if (m_costs.TryGet(key, out had) && had == metal)
                return;
        }
        Add("cost", key, Display(menuText), metal.ToString());
    }
    public int CostOf(string name)
    {
        int n;
        lock (m_gate)
            return m_costs.TryGet(Key(name), out n) ? n : 0;
    }
    public void Crafted(uint serial, string item, string q, int skill)
    {
        Add("craft", Now.Ticks.ToString(), serial.ToString("X"), Key(item), q, skill.ToString());
    }
    public string? QualityOf(uint serial)
    {
        string? q;
        lock (m_gate)
            return m_quality.TryGet(serial, out q) ? q : null;
    }
    public void Offer(string item, string q, int price)
    {
        int had;
        lock (m_gate)
        {
            if (m_offers.TryGet(Key(item) + "|" + q, out had) && had == price)
                return;
        }
        Add("offer", Now.Ticks.ToString(), Key(item), q, price.ToString());
    }
    public void Sold(string vendor, string item, string q, int amount, int price)
    {
        Add("sale", Now.Ticks.ToString(), vendor, Key(item), q, amount.ToString(), price.ToString());
    }
    // Exceptional against everything else (average and below sell together here); a failure is a build that gave
    // nothing.
    static bool Exc(string q)
    {
        return q == "exceptional";
    }
    public List<SmithRow> Rows(int skill)
    {
        List<SmithRow> rows = new();
        lock (m_gate)
        {
            foreach (KeyValuePair<string, int> kv in m_costs)
            {
                SmithRow r = new()
                {
                    Item = m_names.Get(kv.Key, kv.Key),
                    Cost = kv.Value
                };
                List<string[]> mine = m_crafts.FindAll(c => c[2] == kv.Key);
                List<string[]> near = mine.FindAll(c => { int s; return int.TryParse(c[4], out s)
                    && Math.Abs(s - skill) <= NearSkill; });
                List<string[]> used = near.Count >= NearBuildsMin ? near : mine;
                r.Builds = used.Count;
                foreach (string[] c in used)
                {
                    if (c[3] == "failed")
                        r.Failed++;
                    else if (Exc(c[3]))
                        r.Exceptional++;
                }
                r.Basis = used.Count == 0 ? "no builds recorded"
                    : used.Count + " builds " + (used == near ? "within 5.0 of your skill" : "at any skill");
                long sumN = 0, sumE = 0;
                int cntN = 0, cntE = 0;
                foreach (string[] s in m_sales)
                    if (s[2] == kv.Key)
                    {
                        int a, p;
                        if (!int.TryParse(s[4], out a) || !int.TryParse(s[5], out p))
                            continue;
                        r.Sold += a;
                        r.SoldGold += a * p;
                        if (Exc(s[3]))
                        {
                            sumE += (long)a * p;
                            cntE += a;
                        }
                        else
                        {
                            sumN += (long)a * p;
                            cntN += a;
                        }
                    }
                int o;
                r.PriceNormal = cntN > 0 ? (int)(sumN / cntN)
                    : m_offers.TryGet(kv.Key + "|average", out o) ? o
                    : m_offers.TryGet(kv.Key + "|below average", out o) ? o
                    : m_offers.TryGet(kv.Key + "|unknown", out o) ? o : 0;
                r.PriceExceptional = cntE > 0 ? (int)(sumE / cntE)
                    : m_offers.TryGet(kv.Key + "|exceptional", out o) ? o : 0;
                int pn = r.PriceNormal > 0 ? r.PriceNormal : r.PriceExceptional, pe = r.PriceExceptional > 0
                    ? r.PriceExceptional : r.PriceNormal;
                double perBuild = r.Builds == 0 ? pn
                    : ((double)r.Exceptional * pe + (double)(r.Builds - r.Exceptional - r.Failed) * pn) / r.Builds;
                r.PerIngot = r.Cost > 0 ? perBuild / r.Cost : 0;
                // Demand: each vendor's latest sell list within DemandHours. A vendor that offered fewer than were
                // carried is full at that count ("Nana 15 of 20"); one that took all it was shown wants at least that
                // many ("Nana 15+").
                List<string> parts = new();
                foreach (string[] w in m_wants.Values)
                {
                    if (w[2] != kv.Key)
                        continue;
                    long ticks;
                    int off, car;
                    if (!long.TryParse(w[0], out ticks) || !int.TryParse(w[3], out off) || !int.TryParse(w[4], out car))
                        continue;
                    if ((Now - new DateTime(ticks)).TotalHours > DemandHours)
                        continue;
                    r.Wanted += off;
                    if (off < car)
                        r.Full++;
                    parts.Add(w[1] + " " + off + (off < car ? " of " + car : "+"));
                }
                parts.Sort(StringComparer.Ordinal);
                r.Demand = string.Join(", ", parts);
                rows.Add(r);
            }
        }

        rows.Sort((a, b) => b.PerIngot.CompareTo(a.PerIngot));
        return rows;
    }
}

public sealed class AssistAtlas
{
    static readonly ImmutableArray<string> s_types = [ "Banker", "Shopkeeper", "Player vendor", "Healer", "Guard",
        "NPC",
        "Person", "Ghost", "Creature", "House", "Guildstone", "Anvil", "Forge" ];
    public static ImmutableArray<string> Types => s_types;
    // 1998 shop titles (OUO templates and the shard's vendor roles); a title holding one of these is a shopkeeper.
    static readonly ImmutableArray<string> s_shopTitles = [
        "provisioner", "blacksmith", "armourer", "armorer", "weaponsmith", "weapon", "tailor", "weaver", "baker",
            "butcher", "cook",
        "tavern", "innkeeper", "barkeep", "bartender", "bowyer", "fletcher", "carpenter", "mage", "alchemist",
        "herbalist",
        "jewel", "scribe", "tinker", "mapmaker", "shipwright", "stablemaster", "animal trainer", "fur", "fisherman",
        "farmer",
        "beekeeper", "gypsy", "veterinarian", "waiter", "architect", "realtor", "estate", "leather", "tanner",
        "glassblower",
        "merchant", "vendor", "shopkeeper", "potter", "cobbler", "jeweler", "smith", "trader", "seller", "dealer",
        "minter",
    ];
    public const int PeopleShownMs = 60000, KeepMax = 20000, StockJoinMs = 5000;
    // The vendor whose buy list came in last, and a count of buy lists, for the Shops tab to follow.
    public volatile uint Stocked; public long StockedSeq;
    // Counts every change the map draws (a mark moved, named, reclassed, a place kept or forgotten), so an idle map
    // repaints only when it moves.
    public long ChangeSeq;
    void Changed()
    {
        Interlocked.Increment(ref ChangeSeq);
    }
    readonly string m_path; Map<uint, AtlasNpc> m_npcs = Map<uint, AtlasNpc>.Empty; readonly Lock m_npcsGate = new();
    bool m_dirty;

    // The features' clock (AssistTesting.md T2: no wall clock under the suites), its local time the atlas's stamps.
    readonly IClock m_clock;
    DateTime Now => m_clock.UtcNow.ToLocalTime();
    public AssistAtlas(string path, IClock clock)
    {
        m_path = path;
        m_clock = clock;
        try
        {
            if (File.Exists(path))
            {
                foreach (string line in File.ReadAllLines(path))
                {
                    string[] t = line.Split('\t');
                    if (t.Length < 9)
                        continue;
                    AtlasNpc n = new()
                    {
                        Serial = Convert.ToUInt32(t[0], 16),
                        Type = t[1],
                        Sub = t[2],
                        Name = t[3],
                        Body = int.Parse(t[4]),
                        X = int.Parse(t[5]),
                        Y = int.Parse(t[6]),
                        Last = new DateTime(long.Parse(t[7]))
                    };
                    if (t[8].Length > 0)
                        n.Items.AddRange(t[8].Split('|'));
                    if (t.Length > 9)
                        n.ItemsAt = new DateTime(long.Parse(t[9]));
                    if (t.Length > 10)
                        n.Wares = t[10];
                    n.ShopPack = n.Type == "Shopkeeper";
                    n.Vended = n.Type == "Player vendor";
                    m_npcs = m_npcs.Set(n.Serial, n);
                }
            }
        }
        catch { }
    }

    public static void Classify(string name, int body, out string type, out string sub)
    {
        if (body < 0x190 || body > 0x193)
        {
            type = "Creature";
            sub = name.Length > 0 ? name.ToLowerInvariant() : "body 0x" + body.ToString("X3");
            return;
        }
        if (body >= 0x192)
        {
            type = "Ghost";
            sub = "ghost";
            return;
        }
        int at = name.LastIndexOf(" the ", StringComparison.OrdinalIgnoreCase);
        if (at < 0)
        {
            type = "Person";
            sub = name.Length > 0 ? "untitled" : "unnamed";
            return;
        }
        string title = name.Substring(at + 5).Trim().ToLowerInvariant();
        sub = title;
        if (title == "vendor")
        {
            type = "Player vendor";  // a player's hired vendor is titled "the vendor" (RunUO PlayerVendor)
        }
        else if (title.Contains("banker"))
        {
            type = "Banker";
        }
        else if (title.Contains("healer"))
        {
            type = "Healer";
        }
        else if (title.Contains("guard"))
        {
            type = "Guard";
        }
        else
        {
            type = "NPC";
            foreach (string s in s_shopTitles)
                if (title.Contains(s))
                {
                    type = "Shopkeeper";
                    break;
                }
        }
    }

    public void Seen(uint s, int body, int x, int y, string name, bool criminal)
    {
        lock (m_npcsGate)
        {
            AtlasNpc? n;
            if (!m_npcs.TryGet(s, out n))
            {
                if (m_npcs.Count >= KeepMax)
                    return;
                m_npcs = m_npcs.Set(s, n = new AtlasNpc { Serial = s });
            }
            if (n.Body != body || n.X != x || n.Y != y || n.Criminal != criminal)
                Changed();
            n.Body = body;
            n.X = x;
            n.Y = y;
            n.Last = Now;
            n.Criminal = criminal;
            if (name.Length > 0 && n.Name.IndexOf(" the ", StringComparison.OrdinalIgnoreCase) < 0)
                n.Name = name;
            string was = n.Type + n.Sub;
            Reclass(n);
            if (n.Kept || was != n.Type + n.Sub)
            {
                m_dirty = true;
                Changed();
            }
        }
    }
    public void Named(uint s, string name)
    {
        lock (m_npcsGate)
        {
            AtlasNpc? n;
            if (!m_npcs.TryGet(s, out n) || name.Length == 0 || n.Name == name)
                return;
            // A bare name never strips the title the paperdoll gave ("Danyelle" after "Danyelle the banker").
            if (n.Name.StartsWith(name + " the ", StringComparison.OrdinalIgnoreCase))
                return;
            n.Name = name;
            Reclass(n);
            m_dirty = true;
            Changed();
        }
    }
    public void NamedIfNone(uint s, string name)
    {
        lock (m_npcsGate)
        {
            AtlasNpc? n;
            if (m_npcs.TryGet(s, out n) && n.Name.Length > 0)
                return;
        }
        Named(s, name);
    }
    // A human with no name, or a name without a title (speech gives the bare name), is clicked once for its label.
    public bool NeedsName(uint s)
    {
        lock (m_npcsGate)
        {
            AtlasNpc? n;
            return m_npcs.TryGet(s, out n) && n.Body >= 0x190 && n.Body <= 0x191
                && n.Name.IndexOf(" the ", StringComparison.OrdinalIgnoreCase) < 0;
        }
    }
    public void ShopPack(uint s)
    {
        lock (m_npcsGate)
        {
            AtlasNpc? n;
            if (!m_npcs.TryGet(s, out n) || n.ShopPack)
                return;
            n.ShopPack = true;
            Reclass(n);
            m_dirty = true;
            Changed();
        }
    }
    // A mobile that showed a buy list or wears a shop pack is a shopkeeper whatever its name says.
    static void Reclass(AtlasNpc n)
    {
        string type, sub;
        Classify(n.Name, n.Body, out type, out sub);
        if (n.Vended)
        {
            type = "Player vendor";
            sub = "vendor";
        }
        else if ((n.Items.Count > 0 || n.ShopPack) && (type == "Person" || type == "NPC"))
        {
            type = "Shopkeeper";
            if (sub == "untitled" || sub == "unnamed")
                sub = "vendor";
        }
        n.Type = type;
        n.Sub = sub;
    }
    // A mobile that said the player vendor's line is a Player vendor from now on.
    public void PlayerVendor(uint s)
    {
        lock (m_npcsGate)
        {
            AtlasNpc? n;
            if (!m_npcs.TryGet(s, out n) || n.Vended)
                return;
            n.Vended = true;
            Reclass(n);
            m_dirty = true;
            Changed();
        }
    }
    public void Stock(uint vendor, List<string> items)
    {
        // One "vendor buy" sends a list per shop pack (uo1998: the 0x1A stock, then the 0x1B resale, often empty): an
        // empty list changes nothing, and a list within StockJoinMs of the last one joins it.
        if (items.Count == 0)
            return;
        DateTime start;
        List<string> rows;
        lock (m_npcsGate)
        {
            AtlasNpc? n;
            if (!m_npcs.TryGet(vendor, out n))
                return;
            DateTime now = Now;
            bool join = (now - n.ItemsAt).TotalMilliseconds < StockJoinMs;
            if (join)
                n.Items.AddRange(items);
            else
                n.Items = items;
            // A list joined just after a restart has no start kept: it begins a survey of its own.
            if (!join || !m_stockStart.TryGet(vendor, out start))
                m_stockStart = m_stockStart.Set(vendor, start = now);
            n.ItemsAt = now;
            Reclass(n);
            m_dirty = true;
            Changed();
            Stocked = vendor;
            StockedSeq++;
            rows = new List<string>(n.Items);
        }
        Survey(vendor, start, rows);
    }
    // ---- surveys (Damian, 2026-10-24: "for items, it should show every survey as a drop down list by date/time for
    // each vendor when we pull a buy list, so we can see the changes over time"): every buy list and catalogue appended
    // to stocks-<server>.txt, one line each: the vendor's serial, the list's start ticks and its rows joined by "|". A
    // later line of the same vendor and start is the same list joined (StockJoinMs) and replaces it. Read per vendor on
    // demand, so nothing is held in memory but each vendor's current list's start.
    ValueMap<uint, DateTime> m_stockStart = ValueMap<uint, DateTime>.Empty;
    readonly Lock m_surveyGate = new();
    string SurveysPath => Path.Combine(Path.GetDirectoryName(m_path)!,
        Path.GetFileName(m_path).Replace("atlas-", "stocks-"));
    void Survey(uint vendor, DateTime start, List<string> rows)
    {
        string line = vendor.ToString("X") + "\t" + start.Ticks + "\t" + string.Join("|", rows.ConvertAll(Clean));
        lock (m_surveyGate)
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(m_path)!);
                File.AppendAllText(SurveysPath, line + "\r\n");
            }
            catch (IOException) { }
    }
    // A vendor's surveys, newest first.
    public List<AtlasSurvey> Surveys(uint vendor)
    {
        Map<long, AtlasSurvey> byStart = Map<long, AtlasSurvey>.Empty;
        string key = vendor.ToString("X") + "\t";
        string[] lines;
        lock (m_surveyGate)
        {
            try
            {
                lines = File.Exists(SurveysPath) ? File.ReadAllLines(SurveysPath) : new string[0];
            }
            catch (IOException) { lines = new string[0]; }
        }
        foreach (string line in lines)
        {
            if (!line.StartsWith(key))
                continue;
            string[] t = line.Split('\t');
            long ticks;
            if (t.Length != 3 || !long.TryParse(t[1], out ticks))
                continue;
            byStart = byStart.Set(ticks, new AtlasSurvey { At = new DateTime(ticks),
                Items = t[2].Length == 0 ? new List<string>() : new List<string>(t[2].Split('|')) });
        }
        List<AtlasSurvey> l = new(byStart.Values);
        l.Sort((a, b) => b.At.CompareTo(a.At));
        return l;
    }
    // The type the atlas gives a mobile, empty when it has never been seen.
    public string TypeOf(uint s)
    {
        lock (m_npcsGate)
        {
            AtlasNpc? n;
            return m_npcs.TryGet(s, out n) ? n.Type : "";
        }
    }
    // A player vendor's catalogue (AssistFeatures.VendorCatalog): its priced items, as a buy list's rows, in place of
    // the last, and its wares; an empty list is a vendor with nothing for sale.
    public void Catalogued(uint vendor, List<string> items, string wares)
    {
        DateTime at;
        lock (m_npcsGate)
        {
            AtlasNpc? n;
            if (!m_npcs.TryGet(vendor, out n))
                return;
            n.Items = items;
            n.ItemsAt = at = Now;
            n.Wares = wares;
            m_dirty = true;
            Changed();
            Stocked = vendor;
            StockedSeq++;
        }
        Survey(vendor, at, new List<string>(items));
    }
    // Every kept NPC, and everyone else seen in the last PeopleShownMs; copies.
    public List<AtlasNpc> Visible()
    {
        List<AtlasNpc> list = new();
        DateTime now = Now;
        lock (m_npcsGate)
            foreach (AtlasNpc n in m_npcs.Values)
                if (n.Kept || (now - n.Last).TotalMilliseconds < PeopleShownMs)
                    list.Add(Copy(n));
        return list;
    }
    public List<AtlasNpc> Shops()
    {
        List<AtlasNpc> list = new();
        lock (m_npcsGate)
            foreach (AtlasNpc n in m_npcs.Values)
                if (n.Type == "Shopkeeper" || n.Type == "Player vendor" || n.Items.Count > 0)
                    list.Add(Copy(n));
        return list;
    }
    // Every type:subtype seen, for the map's filter.
    public SortedSet<string> Kinds()
    {
        SortedSet<string> set = new();
        lock (m_npcsGate)
            foreach (AtlasNpc n in m_npcs.Values)
                set.Add(n.Type + ":" + n.Sub);
        lock (m_placesGate)
        {
            LoadPlaces();
            foreach (AtlasPlace p in m_places.Values)
                set.Add(p.Kind + ":" + PlaceSub(p));
        }
        return set;
    }
    static AtlasNpc Copy(AtlasNpc n)
    {
        return new AtlasNpc { Serial = n.Serial, Name = n.Name, Type = n.Type, Sub = n.Sub, Body = n.Body, X = n.X,
            Y = n.Y, Criminal = n.Criminal, Last = n.Last, ItemsAt = n.ItemsAt, Items = new List<string>(n.Items),
            Wares = n.Wares, Vended = n.Vended };
    }

    // ---- houses and guildstones: a multi (0x1A graphic with 0x4000) is a house; a gravestone-art item (0x0ED4, the
    // 1998 guildstone) whose label names a guild is a guildstone. Kept in places-<server>.txt beside the atlas.
    Map<uint, AtlasPlace> m_places = Map<uint, AtlasPlace>.Empty; readonly Lock m_placesGate = new();
    bool m_placesLoaded;
    string PlacesPath => Path.Combine(Path.GetDirectoryName(m_path)!,
        Path.GetFileName(m_path).Replace("atlas-", "places-"));
    void LoadPlaces()
    {
        if (m_placesLoaded)
            return;
        m_placesLoaded = true;
        try
        {
            if (File.Exists(PlacesPath))
            {
                foreach (string line in File.ReadAllLines(PlacesPath))
                {
                    string[] t = line.Split('\t');
                    if (t.Length < 8)
                        continue;
                    AtlasPlace p = new()
                    {
                        Serial = Convert.ToUInt32(t[0], 16),
                        Kind = t[1],
                        Name = t[2],
                        Details = t[3],
                        Graphic = int.Parse(t[4]),
                        X = int.Parse(t[5]),
                        Y = int.Parse(t[6]),
                        Last = new DateTime(long.Parse(t[7]))
                    };
                    m_places = m_places.Set(p.Serial, p);
                }
            }
        }
        catch { }
    }
    public static string PlaceSub(AtlasPlace p)
    {
        return p.Kind == "House" ? "multi 0x" + (p.Graphic & 0x3FFF).ToString("X") : p.Kind.ToLowerInvariant();
    }
    public void Place(uint s, string kind, int graphic, int x, int y)
    {
        lock (m_placesGate)
        {
            LoadPlaces();
            AtlasPlace? p;
            if (!m_places.TryGet(s, out p))
                m_places = m_places.Set(s, p = new AtlasPlace { Serial = s, Kind = kind });
            p.Graphic = graphic;
            p.X = x;
            p.Y = y;
            p.Last = Now;
            if (kind.Length > 0)
                p.Kind = kind;
            m_dirty = true;
            Changed();
        }
    }
    public bool HasPlace(uint s)
    {
        lock (m_placesGate)
        {
            LoadPlaces();
            return m_places.ContainsKey(s);
        }
    }
    public void PlaceName(uint s, string name)
    {
        lock (m_placesGate)
        {
            AtlasPlace? p;
            if (m_places.TryGet(s, out p) && name.Length > 0)
            {
                p.Name = name;
                m_dirty = true;
                Changed();
            }
        }
    }
    public void PlaceDetail(uint s, string detail)
    {
        lock (m_placesGate)
        {
            AtlasPlace? p;
            if (m_places.TryGet(s, out p))
            {
                p.Details = Now.ToString("MM-dd HH:mm") + " " + detail;
                m_dirty = true;
                Changed();
            }
        }
    }
    public void Forget(uint s)
    {
        lock (m_placesGate)
        {
            if (m_places.ContainsKey(s))
            {
                m_places = m_places.Remove(s);
                m_dirty = true;
            }
            Changed();
        }
    }
    // Every NPC and place forgotten, to be seen again (the Rediscover button); the trades stay.
    public void ForgetWorld()
    {
        lock (m_npcsGate)
        {
            m_npcs = m_npcs.Clear();
            m_dirty = true;
            Changed();
        }
        lock (m_placesGate)
        {
            LoadPlaces();
            m_places = m_places.Clear();
            m_dirty = true;
            Changed();
        }
    }
    // The nearest house within reach of a tile, 0 when none.
    public uint HouseNear(int x, int y, int reach)
    {
        lock (m_placesGate)
        {
            LoadPlaces();
            uint best = 0;
            int bd = reach + 1;
            foreach (AtlasPlace p in m_places.Values)
                if (p.Kind == "House")
                {
                    int d = Math.Max(Math.Abs(p.X - x), Math.Abs(p.Y - y));
                    if (d < bd)
                    {
                        bd = d;
                        best = p.Serial;
                    }
                }
            return best;
        }
    }
    public List<AtlasPlace> Places()
    {
        List<AtlasPlace> l = new();
        lock (m_placesGate)
        {
            LoadPlaces();
            foreach (AtlasPlace p in m_places.Values)
                l.Add(new AtlasPlace { Serial = p.Serial, Kind = p.Kind, Name = p.Name, Details = p.Details,
                    Graphic = p.Graphic, X = p.X, Y = p.Y, Last = p.Last });
        }
        return l;
    }

    // ---- trades: one row per item bought or sold, appended to trades-<server>.csv and kept in memory for the Shops
    // tab.
    readonly List<AtlasTrade> m_trades = new();
    string TradesPath => Path.Combine(Path.GetDirectoryName(m_path)!,
        Path.GetFileName(m_path).Replace("atlas-", "trades-").Replace(".txt", ".csv"));
    public void Trade(AtlasTrade t)
    {
        LoadTrades();
        lock (m_trades)
        {
            m_trades.Add(t);
            if (m_trades.Count > 5000)
                m_trades.RemoveAt(0);
        }
        Interlocked.Increment(ref TradesSeq);
        try
        {
            bool fresh = !File.Exists(TradesPath);
            File.AppendAllText(TradesPath,
                (fresh ? "time,kind,vendor,vendor serial,x,y,item,amount,unit price,total\r\n" : "")
                + t.At.ToString("yyyy-MM-dd HH:mm:ss") + "," + t.Kind + "," + Csv(t.Vendor) + ","
                + t.VendorSerial.ToString("X") + "," + t.X + "," + t.Y + "," + Csv(t.Item) + "," + t.Amount + ","
                + t.Price + "," + (t.Amount * t.Price) + "\r\n");
        }
        catch { }
    }
    static string Csv(string s)
    {
        return "\"" + s.Replace("\"", "'") + "\"";
    }
    public List<AtlasTrade> Trades(uint vendor)
    {
        LoadTrades();
        lock (m_trades)
            return m_trades.FindAll(t => vendor == 0 || t.VendorSerial == vendor);
    }
    // The file's rows back to memory once, the newest 5000, so the Circuit tab's totals outlive a relay restart.
    bool m_tradesLoaded; public long TradesSeq;
    void LoadTrades()
    {
        lock (m_trades)
        {
            if (m_tradesLoaded)
                return;
            m_tradesLoaded = true;
            try
            {
                if (!File.Exists(TradesPath))
                    return;
                string[] lines = File.ReadAllLines(TradesPath);
                List<AtlasTrade> old = new();
                for (int i = Math.Max(1, lines.Length - 5000); i < lines.Length; i++)
                {
                    AtlasTrade? t = ParseTrade(lines[i]);
                    if (t != null)
                        old.Add(t);
                }
                m_trades.InsertRange(0, old);
                if (m_trades.Count > 5000)
                    m_trades.RemoveRange(0, m_trades.Count - 5000);
                TradesSeq++;
            }
            catch { }
        }
    }
    // time,kind,"vendor",serial,x,y,"item",amount,unit price,total; a quoted field holds no quote (Csv). Null for the
    // header or a row of another shape.
    static AtlasTrade? ParseTrade(string line)
    {
        List<string> f = new();
        StringBuilder cur = new();
        bool quoted = false;
        foreach (char ch in line)
        {
            if (ch == '"')
            {
                quoted = !quoted;
            }
            else if (ch == ',' && !quoted)
            {
                f.Add(cur.ToString());
                cur.Clear();
            }
            else
            {
                cur.Append(ch);
            }
        }
        f.Add(cur.ToString());
        DateTime at;
        uint s;
        int x, y, a, p;
        if (f.Count < 9 || !DateTime.TryParse(f[0], out at)
            || !uint.TryParse(f[3], NumberStyles.HexNumber, null, out s) || !int.TryParse(f[4], out x)
            || !int.TryParse(f[5], out y) || !int.TryParse(f[7], out a) || !int.TryParse(f[8], out p))
            return null;
        return new AtlasTrade { At = at, Kind = f[1], Vendor = f[2], VendorSerial = s, X = x, Y = y, Item = f[6],
            Amount = a, Price = p };
    }
    // Every trade with one vendor in the record, oldest first: the whole file, past the newest 5000 Trades holds
    // (Damian, 2026-10-24: "shopkeepers and merchants should also show the history of sales to those vendors, and
    // purchases"). Read on demand.
    public List<AtlasTrade> VendorTrades(uint vendor)
    {
        List<AtlasTrade> l = new();
        string[] lines;
        try
        {
            lines = File.Exists(TradesPath) ? File.ReadAllLines(TradesPath) : new string[0];
        }
        catch (IOException) { lines = new string[0]; }
        foreach (string line in lines)
        {
            AtlasTrade? t = ParseTrade(line);
            if (t != null && t.VendorSerial == vendor)
                l.Add(t);
        }
        return l;
    }

    public void Save()
    {
        List<string> lines = new();
        lock (m_npcsGate)
        {
            if (!m_dirty)
                return;
            m_dirty = false;
            foreach (AtlasNpc n in m_npcs.Values)
                if (n.Kept)
                    lines.Add(n.Serial.ToString("X") + "\t" + n.Type + "\t" + Clean(n.Sub) + "\t" + Clean(n.Name)
                        + "\t" + n.Body + "\t" + n.X + "\t" + n.Y + "\t" + n.Last.Ticks + "\t"
                        + string.Join("|", n.Items.ConvertAll(Clean)) + "\t" + n.ItemsAt.Ticks + "\t" + Clean(n.Wares));
        }
        List<string> placeLines = new();
        lock (m_placesGate)
            foreach (AtlasPlace p in m_places.Values)
                placeLines.Add(p.Serial.ToString("X") + "\t" + p.Kind + "\t" + Clean(p.Name) + "\t" + Clean(p.Details)
                    + "\t" + p.Graphic + "\t" + p.X + "\t" + p.Y + "\t" + p.Last.Ticks);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(m_path)!);
            File.WriteAllLines(m_path, lines);
            if (m_placesLoaded)
                File.WriteAllLines(PlacesPath, placeLines);
        }
        catch { }
    }
    static string Clean(string s)
    {
        return s.Replace('\t', ' ').Replace('|', '/');
    }
}
