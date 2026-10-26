#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace Uoaix.Assist;

public sealed partial class AssistFeatures
{
    // ---------- the tailoring circuit (assist-plan.md stage 6, step 4) ----------
    // No round zero (Damian, 2026-10-17: "no round 0 here"): the sewing kit is the pack's, the menus are picked by
    // their text, the cloth is the pack's largest pile and then its bolts.
    // What the circuit sells and makes are settings, changed from the control port with no code change (Damian,
    // 2026-10-17: "lets make this price and even the item a configurable so no code change", "take a list of items and
    // min prices", "sell as low as 23"). circuit.sell: items separated by ";", each "name|arts in hex,
    // comma-separated|lowest price", the name as vendors list it; circuit.item.menu: the sewing menus' picks, separated
    // by "|".
    public const string SellDefault = "fancy shirt|1EFD,1EFE|23";
    public sealed class SellItem
    {
        public string Name = null!; public int[] Arts = null!; public int Floor; public override string ToString()
        {
            return Name + "|" + string.Join(",", Array.ConvertAll(Arts, a => a.ToString("X4"))) + "|" + Floor;
        }
    }
    string m_sellRead = null!; List<SellItem> m_sellList = new();
    public List<SellItem> SellList()
    {
        string t = Cfg.Get("circuit.sell", SellDefault);
        lock (m_sellList)
        {
            if (t != m_sellRead)
            {
                List<SellItem> l = new();
                foreach (string e in t.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] f = e.Split('|');
                    int floor;
                    if (f.Length != 3 || !int.TryParse(f[2].Trim(), out floor))
                        continue;
                    List<int> arts = new();
                    foreach (string s in f[1].Split(','))
                    {
                        int v;
                        if (int.TryParse(s.Trim(), NumberStyles.HexNumber, null, out v))
                            arts.Add(v);
                    }
                    if (arts.Count > 0)
                        l.Add(new SellItem { Name = f[0].Trim(), Arts = arts.ToArray(), Floor = floor });
                }
                m_sellList = l;
                m_sellRead = t;
            }
            return new List<SellItem>(m_sellList);
        }
    }
    // Adds the item, or replaces the one of the same name; a price under 0 removes it.
    public string? SetSellItem(string name, string arts, int floor)
    {
        List<SellItem> l = SellList();
        l.RemoveAll(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));
        if (floor >= 0)
        {
            SellItem probe = new()
            {
                Name = name,
                Floor = floor,
                Arts = new int[0]
            };
            List<int> a = new();
            foreach (string s in arts.Split(','))
            {
                int v;
                if (!int.TryParse(s.Trim(), NumberStyles.HexNumber, null, out v))
                    return "not an art in hex: " + s;
                a.Add(v);
            }
            probe.Arts = a.ToArray();
            l.Add(probe);
        }
        Cfg.Set("circuit.sell", string.Join(";", l.ConvertAll(i => i.ToString())));
        return null;
    }
    public string ProductMenu
    {
        get => Cfg.Get("circuit.item.menu", "shirts|Fancy shirt");
        set => Cfg.Set("circuit.item.menu", value);
    }
    // The buy circuit's shopping list (Damian, 2026-10-17: "a shopping list of mage supply stuff (reagents, runes,
    // scrolls)"; "we should seek to acquire 250 of them @5gp each"): circuit.buy.list, items separated by ";", each
    // "name|arts in hex, comma-separated|count|highest price".
    public const string BuyDefault = "black pearl|0F7A|250|5";
    public sealed class BuyItem
    {
        public string? Name = null;
        public int[] Arts = null!; public int Count, MaxPrice; public override string ToString()
        {
            return Name + "|" + string.Join(",", Array.ConvertAll(Arts, a => a.ToString("X4"))) + "|" + Count + "|"
                + MaxPrice;
        }
    }
    public List<BuyItem> BuyList()
    {
        List<BuyItem> l = new();
        foreach (string e in Cfg.Get("circuit.buy.list", BuyDefault).Split(new[] { ';' },
            StringSplitOptions.RemoveEmptyEntries))
        {
            string[] f = e.Split('|');
            int count, max;
            if (f.Length != 4 || !int.TryParse(f[2].Trim(), out count) || !int.TryParse(f[3].Trim(), out max))
                continue;
            List<int> arts = new();
            foreach (string s in f[1].Split(','))
            {
                int v;
                if (int.TryParse(s.Trim(), NumberStyles.HexNumber, null, out v))
                    arts.Add(v);
            }
            if (arts.Count > 0)
                l.Add(new BuyItem { Name = f[0].Trim(), Arts = arts.ToArray(), Count = count, MaxPrice = max });
        }
        return l;
    }
    // Adds the item, or replaces the one of the same name; a count of 0 or less removes it.
    public string? SetBuyItem(string name, string arts, int count, int maxPrice)
    {
        List<BuyItem> l = BuyList();
        l.RemoveAll(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));
        if (count > 0)
        {
            List<int> a = new();
            foreach (string s in arts.Split(','))
            {
                int v;
                if (!int.TryParse(s.Trim(), NumberStyles.HexNumber, null, out v))
                    return "not an art in hex: " + s;
                a.Add(v);
            }
            l.Add(new BuyItem { Name = name, Arts = a.ToArray(), Count = count, MaxPrice = Math.Max(1, maxPrice) });
        }
        Cfg.Set("circuit.buy.list", string.Join(";", l.ConvertAll(i => i.ToString())));
        return null;
    }
    // The ingot-buying circuit, "Buy ingots" (circuit ingots): iron ingots bought at the smith shops (SmithyShopKinds)
    // and stored in the bank as the buy circuit stores its list; ingots.count of them at ingots.price gp at most (10
    // unset, twice ServUO's 5 at SBBlacksmith).
    public int IngotBuyCount
    {
        get => Math.Max(1, Cfg.GetInt(Server + "ingots.count", 500));
        set => Cfg.Set(Server + "ingots.count", Math.Max(1, value).ToString());
    }
    public int IngotBuyPrice
    {
        get => Math.Max(1, Cfg.GetInt(Server + "ingots.price", 10));
        set => Cfg.Set(Server + "ingots.price", Math.Max(1, value).ToString());
    }
    List<BuyItem> BuyListFor(string kind)
    {
        if (kind != "ingots")
            return BuyList();
        return new List<BuyItem> { new() { Name = "iron ingot", Arts = new[] { IngotArt }, Count = IngotBuyCount,
            MaxPrice = IngotBuyPrice } };
    }
    // The buy circuit's purse: the gold it carries from each bank, topped up from and banked onto the box's own pile.
    public int BuyPurse
    {
        get => Cfg.GetInt("circuit.buy.purse", 2000);
        set => Cfg.Set("circuit.buy.purse", Math.Max(0, value).ToString());
    }
    // What the shopping list offers by name: the reagents, a blank scroll, a recall rune.
    static readonly string[][] s_buyPresets = {
        new[] { "black pearl", "0F7A" }, new[] { "bloodmoss", "0F7B" }, new[] { "garlic", "0F84" },
            new[] { "ginseng", "0F85" },
        new[] { "mandrake root", "0F86" }, new[] { "nightshade", "0F88" }, new[] { "sulfurous ash", "0F8C" },
        new[] { "spiders' silk", "0F8D" },
        new[] { "blank scroll", "0EF3" }, new[] { "recall rune", "1F14" },
    };
    public static string[][] BuyPresets => s_buyPresets;
    // The lowest price for the art, or -1 when nothing on the list is that art.
    int FloorOf(int g)
    {
        foreach (SellItem i in SellList())
            if (Array.IndexOf(i.Arts, g) >= 0)
                return i.Floor;
        return -1;
    }
    bool IsShirtArt(int g)
    {
        return FloorOf(g) >= 0;
    }
    string FloorText()
    {
        return string.Join(", ", SellList().ConvertAll(i => i.Name + " " + i.Floor));
    }
    // A vendor whose stock lists anything on the sell list by its whole name ("Skirt: 33 gp x 10"): a shop that deals
    // in it, and so buys it. A part of a name is not the item: an armorer's "leather skirt" took Moonglow's armory for
    // a skirt buyer (2026-10-17).
    bool DealsIn(AtlasNpc n)
    {
        List<SellItem> l = SellList();
        return n.Items.Exists(s => { int c = s.IndexOf(':'); string item = (c >= 0 ? s.Substring(0, c)
            : s).Trim(); return l.Exists(i => string.Equals(item, i.Name, StringComparison.OrdinalIgnoreCase)); });
    }
    internal List<AtlasNpc> CityBuyers(string city)
    {
        List<AtlasNpc> l = new();
        if (Atlas == null)
            return l;
        // One a first name, the one seen last: a sale is asked by first name, so a stale atlas entry under another
        // serial was a second tailor with no rest or offer of her own, and "Morena sell" said for it sold to the real
        // one mid-rest (the sell order's world case under coverage).
        foreach (AtlasNpc n in Atlas.Shops())
        {
            if (UoRegions.City(n.X, n.Y) != city || !DealsIn(n) || !TailorVendor(n) || !TailorStock(n))
                continue;
            string name = n.Name.Split(' ')[0];
            int had = l.FindIndex(o => string.Equals(o.Name.Split(' ')[0], name, StringComparison.OrdinalIgnoreCase));
            if (had < 0)
                l.Add(n);
            else if (n.Last > l[had].Last)
                l[had] = n;
        }
        return l;
    }
    public const int SewWeightRoom = 10, PackItemsMax = 125, KitArt = 0x0F9D, ThreadWant = 100, CircuitReach = 150,
        VendorReach = 2;
    int RestockPollMs => Ms("restock-poll");
    static bool IsThreadArt(int g)
    {
        return g == 0x0FA0 || g == 0x0FA1;
    }
    LoopKind TailorKind
    {
        get
        {
            foreach (LoopKind l in s_loops)
                if (l.Tailor > 0)
                    return l;
            return s_loops[0];
        }
    }
    // Why the pack takes no more shirts, or null (Damian, 2026-10-19: "add pack is full cutoff for loop and circuit
    // shirt making", "both item count and load w/r/t carry capcity"). A shirt does not stack, and the pack holds
    // PackItemsMax items counting every bag's contents (ServUO Container.CheckHold: TotalItems + 1 > 125, "That
    // container cannot hold more items."; worn items do not count), so the sewing stops one short of it; an unopened
    // bag's items are unknown here (113 counted against the server's 124 at 08:43:05), so the bags are opened first.
    // Past the 1998 carry limit (strength * 4 + 30, OUO CMobile_GetMaxWeight) every step drains ten times the stamina,
    // so the sewing stops SewWeightRoom short of it: 105 unsold shirts left Kwik at 460 of 394 stones, too fatigued to
    // walk to a bank (Moonglow, 2026-10-18 04:31).
    string? PackFull(GameLink k)
    {
        int held = Inv(k).ItemCount();
        if (held >= PackItemsMax - 1)
            return "the pack holds " + held + " items, " + PackItemsMax + " at most";
        int str, weight;
        lock (k.State.Gate)
        {
            str = k.State.Str;
            weight = k.State.Weight;
        }
        if (str > 0 && weight + SewWeightRoom > str * 4 + 30)
            return "the pack weighs " + weight + " of " + (str * 4 + 30) + " stones";
        return null;
    }
    // Sews fancy shirts until the pack holds no cloth or bolts. Null, or why it stopped.
    public string? SewAll(out int made, out int failed)
    {
        made = 0;
        failed = 0;
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "no game connection";
        // TailorResult waits on the loops' stop, which Attach leaves set: a sewing outside a loop (the control port,
        // the circuit) clears it, or every shirt after the first reads as stopped.
        ManualStart();
        if (!m_loops.Running)
            idleStop = false;
        OpenPackBags(k);
        Set<uint> spent = Set<uint>.Empty;
        LoopKind l = TailorKind;
        bool failedLast = false;
        m_refusedInARow = 0;
        while (!circuitStop && !k.Closed)
        {
            string? full = PackFull(k);
            if (full != null)
                return full;
            AssistItem? kit = null;
            lock (k.State.Gate)
            {
                Set<uint> inside = PackTree(k);
                foreach (AssistItem it in k.State.Items.Values)
                    if (inside.Contains(it.Container) && it.Graphic == KitArt)
                    {
                        kit = it;
                        break;
                    }
            }
            if (kit == null)
                return "no sewing kit in the pack";
            // The bolts first, the cloth after, as the loop sews (a bolt's cloth stacks on the one pile: one scrap a
            // run).
            AssistItem? mat = null;
            foreach (AssistItem b in PackBolts(k))
                if (mat == null || b.Amount > mat.Amount)
                    mat = b;
            bool fromCloth = mat == null;
            if (mat == null)
            {
                lock (k.State.Gate)
                {
                    Set<uint> inside = PackTree(k);
                    foreach (AssistItem it in k.State.Items.Values)
                        if (inside.Contains(it.Container) && IsCloth(it.Graphic) && !spent.Contains(it.Serial)
                            && (mat == null || it.Amount > mat.Amount))
                            mat = it;
                }
            }

            if (mat == null)
                return null;
            // After a failure the busy time from the sew's last menu pick as well, as the loop waits (RunTailor).
            DateTime due = m_lastUse.AddMilliseconds(Delay(l));
            if (failedLast)
            {
                DateTime busyBy = m_sewPickedAt.AddMilliseconds(Ms("busy:sew"));
                if (busyBy > due)
                    due = busyBy;
            }
            Wait.Till(Clock, due, () => circuitStop);
            Heard mark;
            string? w;
            using (Act(k))
            {
                string[] menu = ProductMenu.Split('|');
                mark = new(k.State);
                m_lastSewn = menu[menu.Length - 1];
                m_lastSewnAt = Clock.UtcNow;
                w = Sew(k, kit.Serial, mat.Serial, menu.Length, (i, m) => MenuPick(m, menu[i])!);
            }
            string? res = w == null ? TailorResult(k, l, mark, TailorMs)
                : w == "no cursor" || w == "no menu" ? (SmeltWait(k, l, mark) ?? w) : null;
            // The server's own limit stops the sewing: the pack's count above misses bags never opened (the server
            // refused at 113 counted, 2026-10-17 08:43:05).
            string? refused = mark.Find("cannot hold more items");
            if (refused != null)
                return "the pack is full: \"" + refused + "\"";
            if (w != null && res == null)
                return w;
            failedLast = res == "failed";
            if (res == "again")
                continue;
            if (res == "made" || res == "exceptional")
            {
                made++;
                continue;
            }
            if (res == "failed")
            {
                failed++;
                continue;
            }
            if (res == "stopped")
                return "stopped";
            string? said = TailorLast(k, mark);
            if (fromCloth)
            {
                if (said != null)
                    DropScraps(k, mat.Serial);
                spent = spent.Add(mat.Serial);
                continue;
            }
            return "the bolts gave " + res;
        }
        return circuitStop ? "stopped" : "the connection closed";
    }

    // Staff, attack and death end the circuit (assist-plan.md stage 6): staff is a name begun "GM ", a title of game
    // master, GM, counselor or councilor, seer or admin, or the GM robe (0x204F) worn; players are ignored.
    static readonly ImmutableArray<string> s_staffWords = [ "game master", "counselor", "councilor", "seer", "admin" ];
    public const int GmRobeArt = 0x204F;
    static bool IsStaffName(string name)
    {
        string n = name.Trim();
        if (n.StartsWith("GM ", StringComparison.Ordinal) || n == "GM")
            return true;
        int at = n.LastIndexOf(" the ", StringComparison.OrdinalIgnoreCase);
        string title = at >= 0 ? n.Substring(at + 5).ToLowerInvariant() : "";
        if (title == "gm" || title.StartsWith("gm "))
            return true;
        foreach (string w in s_staffWords)
            if (title.Contains(w))
                return true;
        return false;
    }
    volatile string? m_circuitAlarm = null;
    void CircuitWatch(GameLink k, byte[] p)
    {
        if (m_circuitThread == null || !m_circuitThread.IsAlive)
            return;
        uint me;
        lock (k.State.Gate)
            me = k.State.Player;
        if (p[0] == 0x1C && p.Length > 44)
        {
            uint who = U32(p, 3);
            if (who == 0xFFFFFFFF || who == me)
                return;
            string name = Encoding.ASCII.GetString(p, 14, 30).TrimEnd('\0');
            if (IsStaffName(name))
                Alarm(k,
                    "staff spoke: " + name + ": \"" + Encoding.ASCII.GetString(p, 44, p.Length - 44).TrimEnd('\0')
                    + "\"");
        }
        else if (p[0] == 0x78 && p.Length >= 19)
        {
            // An incoming mobile: serial, body, x, y, z, direction, hue, flags, notoriety, then worn items (serial,
            // graphic with 0x8000 when a hue follows, layer, hue) until a zero serial.
            uint who = U32(p, 3);
            int at = 19;
            while (at + 4 <= p.Length && U32(p, at) != 0)
            {
                if (at + 7 > p.Length)
                    break;
                int g = (p[at + 4] << 8) | p[at + 5];
                at += 7;
                if ((g & 0x8000) != 0)
                    at += 2;
                if ((g & 0x7FFF) == GmRobeArt && who != me)
                {
                    Alarm(k, "someone in a GM robe is near (" + who.ToString("X") + ")");
                    break;
                }
            }
        }
        else if (p[0] == 0x2F && p.Length >= 10 && U32(p, 6) == me)
        {
            Alarm(k, "you are attacked by " + MobileName(k, U32(p, 2)));
        }
        else if (p[0] == 0x2C)
        {
            Alarm(k, "you died");
        }
    }
    void Alarm(GameLink k, string why)
    {
        if (m_circuitAlarm != null)
            return;
        m_circuitAlarm = why;
        circuitStop = true;
        m_weaveRunner.Stop();
        Note("Circuit: stopped, " + why);
        k.Tell("Circuit stopped: " + why, 0x0026);
    }

    // The circuit at the town the player is in: buy out each thread vendor whose restock is due, weave at the nearest
    // loom, sew fancy shirts from all the cloth, sell what the sell list holds at its lowest prices or more, then wait
    // for the next restock.
    Thread m_circuitThread = null!;
    volatile bool m_sellOnly;
    public string? CircuitSell()
    {
        return StartCircuit(() => m_sellOnly = true);
    }
    // Buy thread only (Damian, 2026-10-18: "we also need a tailoring buy thread only. it just cycles through all the
    // tailors one at a time and buys thread and banks it"): every city whose tailors have thread due, the town here
    // first, then nearest by the trip's order; each tailor due asked in turn (BuyCityThread), and the thread banked in
    // that city's bank (into the bank store bag, else beside the box's thread) before it is left. Ends when no tailor
    // has thread due.
    volatile bool m_threadOnly;
    public string? CircuitThread()
    {
        return StartCircuit(() => m_threadOnly = true);
    }
    string RunThreadOnly(GameLink k)
    {
        int total = 0;
        string? end = null;
        try
        {
            for (int pass = 0; pass < 50 && !circuitStop && !k.Closed && end == null; pass++)
            {
                string? here = CityOf(k);
                string? city = here != null && ThreadCandidates(here).Exists(ThreadDue) ? here : null;
                if (city == null)
                {
                    List<string> cities = TripCities(k, ThreadScore);
                    if (cities.Count == 0)
                    {
                        end = "no tailor has thread due";
                        break;
                    }
                    city = cities[0];
                }
                string? w = Travel(k, city);
                if (w != null)
                {
                    end = "to " + city + ": " + w;
                    break;
                }
                int got;
                w = BuyCityThread(k, city, out got);
                total += got;
                if (w != null && Silent(w))
                {
                    end = w;
                    break;
                }
                if (w != null)
                    Note("Thread: " + city + ": " + w);
                if (PackCount(k, IsThreadArt) > 0 && !circuitStop)
                {
                    CircuitStatus = "Thread: banking in " + city;
                    string? bw = BankHere(k, city);
                    if (bw == null)
                    {
                        string put;
                        bw = BankPut(new List<int> { 0x0FA0, 0x0FA1 }, out put);
                    }
                    if (bw != null)
                    {
                        end = "banking the thread in " + city + ": " + bw;
                        break;
                    }
                }
                // A city whose tailors gave nothing this pass is passed over (BuyCityThread skips them), so the passes
                // end.
                if (got == 0 && !ThreadCandidates(city).Exists(ThreadDue))
                    continue;
            }
            return (end ?? (circuitStop ? "stopped" : "50 cities visited")) + "; " + total + " thread bought";
        }
        finally { m_threadOnly = false; }
    }
    // Keep thread (Damian, 2026-10-21: "build a cycle that just does this. keeps these vendors bought out, never
    // assumes, always checks, runs in jhelom and brit between shops to save mana"; "set a pile on floor of house as
    // storage, use the tailor bag for the runes"; "use the bank reagent store to keep the recalls going"):
    // the thread vendors of the approved shops (the tailor runes bag, ApprovedShops), the vendor due soonest (KeepDue)
    // first and then every other vendor of its city whatever its forecast. "The vendor has none" is an answer: counted,
    // asked again next lap. Walked between shops inside KeepWalkCities, recalled between cities. The purse from the
    // bank box's top-level gold, sized for the city's stocks bought whole (KeepRules.PurseFor); the reagent loadout is
    // refilled at a bank stop when a recall reagent is low (ToBanker). Every stock bought whole (allOrNone), the thread
    // past the carry limit stashed in the shop (StashThread) and lifted again when room allows (FetchStashes); the
    // pack's thread put on the house pile (OverflowStore) once it is nine tenths full.
    volatile bool m_keepMode;
    public string? CircuitKeep()
    {
        return StartCircuit(() => m_keepMode = true);
    }
    static readonly ImmutableArray<string> s_keepWalkCities = [ "Jhelom", "Britain" ];
    // Every vendor restock seen as it happened (AssistState.Restocks), in local time: kept in restocks-<server>.txt
    // (one "VENDOR\tticks" a line) and said; the latest RestocksKeep a vendor kept in memory.
    Map<uint, List<DateTime>> m_restocksSeen = Map<uint, List<DateTime>>.Empty;
    readonly Lock m_restocksSeenGate = new();
    bool m_restocksLoaded;
    public const int RestocksKeep = 32;
    void RestocksSeen(GameLink k)
    {
        List<VendorRestock> fresh = new();
        lock (k.State.Gate)
        {
            fresh.AddRange(k.State.Restocks);
            k.State.Restocks.Clear();
        }
        if (fresh.Count == 0)
            return;
        lock (m_restocksSeenGate)
        {
            LoadRestocks();
            foreach (VendorRestock r in fresh)
            {
                DateTime at = r.At.ToLocalTime();
                AddRestock(r.Vendor, at);
                try
                {
                    File.AppendAllText(ServerFile("restocks"),
                        r.Vendor.ToString("X") + "\t" + at.Ticks + Environment.NewLine);
                }
                catch (IOException) { }
                string? name;
                k.State.Names.TryGet(r.Vendor, out name);
                Note("Keep: " + (name ?? r.Vendor.ToString("X")) + " restocked at " + at.ToString("HH:mm:ss"));
            }
        }
    }
    // The caller holds restocksSeen.
    void AddRestock(uint vendor, DateTime at)
    {
        List<DateTime>? l;
        if (!m_restocksSeen.TryGet(vendor, out l))
            m_restocksSeen = m_restocksSeen.Set(vendor, l = new List<DateTime>());
        l.Add(at);
        if (l.Count > RestocksKeep)
            l.RemoveAt(0);
    }
    // The caller holds restocksSeen.
    void LoadRestocks()
    {
        if (m_restocksLoaded)
            return;
        m_restocksLoaded = true;
        string[] lines;
        try
        {
            lines = File.Exists(ServerFile("restocks")) ? File.ReadAllLines(ServerFile("restocks")) : new string[0];
        }
        catch (IOException) { lines = new string[0]; }
        foreach (string line in lines)
        {
            string[] t = line.Split('\t');
            uint v;
            long ticks;
            if (t.Length == 2 && uint.TryParse(t[0], NumberStyles.HexNumber, null, out v)
                && long.TryParse(t[1], out ticks))
                AddRestock(v, new DateTime(ticks));
        }
    }
    bool RestockedSince(uint vendor, DateTime since)
    {
        foreach (DateTime t in RestocksOf(vendor))
            if (t >= since)
                return true;
        return false;
    }
    ImmutableArray<DateTime> RestocksOf(uint vendor)
    {
        lock (m_restocksSeenGate)
        {
            LoadRestocks();
            List<DateTime>? l;
            return m_restocksSeen.TryGet(vendor, out l) ? ImmutableArray.CreateRange(l)
                : ImmutableArray<DateTime>.Empty;
        }
    }
    // When a thread vendor is next worth a visit, every circuit alike (KeepRules.ProbeAt): its buy visits in the
    // attempts record, oldest first, and its restocks seen; a record that cannot be read counts as no visits (due now).
    // The record is parsed again only when it has grown.
    public DateTime ThreadDueAt(uint serial)
    {
        ImmutableArray<Visit> visits = VisitsOf(serial);
        ImmutableArray<DateTime> seen = RestocksOf(serial);
        return KeepRules.ProbeAt(visits, seen, KeepRules.RestockPeriod(seen), Clock.UtcNow.ToLocalTime());
    }
    // When the vendor's first restock after `at` is worth a visit (KeepRules.StrikeAfter): a tailor that bought her
    // fill of shirts at `at` buys again then.
    internal DateTime RestockAfter(uint serial, DateTime at)
    {
        ImmutableArray<DateTime> seen = RestocksOf(serial);
        return KeepRules.StrikeAfter(VisitsOf(serial), seen, KeepRules.RestockPeriod(seen), at);
    }
    // When the vendor has surely restocked since `at` (KeepRules.SureAfter): what a trip to her waits for.
    internal DateTime SureAfter(uint serial, DateTime at)
    {
        ImmutableArray<DateTime> seen = RestocksOf(serial);
        return KeepRules.SureAfter(VisitsOf(serial), seen, KeepRules.RestockPeriod(seen), at);
    }
    ImmutableArray<Visit> VisitsOf(uint serial)
    {
        ImmutableArray<Visit> visits;
        lock (m_attemptsGate)
        {
            long length;
            try
            {
                length = File.Exists(ServerFile("attempts")) ? new FileInfo(ServerFile("attempts")).Length : 0;
            }
            catch (IOException) { length = 0; }
            if (length != m_visitsLength)
            {
                string[] lines;
                try
                {
                    lines = length > 0 ? File.ReadAllLines(ServerFile("attempts")) : new string[0];
                }
                catch (IOException) { lines = new string[0]; }
                m_visits = m_visits.Clear();
                foreach (string line in lines)
                {
                    string[] t = line.Split('\t');
                    long ticks;
                    if (t.Length != 4 || t[1] != "buy" || !long.TryParse(t[2], out ticks))
                        continue;
                    Visit? v = KeepRules.ParseVisit(new DateTime(ticks), t[3]);
                    if (v is null)
                        continue;
                    if (!m_visits.TryGet(t[0], out List<Visit>? l))
                        m_visits = m_visits.Set(t[0], l = new List<Visit>());
                    l.Add(v);
                }
                m_visitsLength = length;
            }
            visits = m_visits.TryGet(serial.ToString("X"), out List<Visit>? mine)
                ? ImmutableArray.CreateRange(mine) : ImmutableArray<Visit>.Empty;
        }
        return visits;
    }
    Map<string, List<Visit>> m_visits = Map<string, List<Visit>>.Empty;
    long m_visitsLength = -1;
    string RunKeepThread(GameLink k)
    {
        int bought = 0, asks = 0, dry = 0, laps = 0;
        string? end = null;
        while (!circuitStop && !k.Closed)
        {
            string? city = null;
            AtlasNpc? pick = null;
            DateTime oldest = DateTime.MaxValue;
            // The vendor due soonest by its own restock window (ThreadDueAt), not the one asked longest ago.
            foreach (string c in TailorCities())
            {
                if (Banned(c))
                    continue;
                foreach (AtlasNpc n in ThreadCandidates(c))
                {
                    if (Skipped(n.Serial))
                        continue;
                    DateTime t = ThreadDueAt(n.Serial);
                    if (t < oldest)
                    {
                        oldest = t;
                        city = c;
                        pick = n;
                    }
                }
            }
            if (city == null)
            {
                end = "no thread vendor in the approved shops";
                break;
            }
            // Not before it is due (L-FUTILITY): with every vendor bought out, the one due soonest was asked again at
            // once, 25 times in a minute after its buy-out (KeepLapTests, 2026-10-23). The wait is where the player
            // stands, the stop tested every second.
            if (oldest > Clock.UtcNow.ToLocalTime())
            {
                string who = pick!.Name.Split(' ')[0];
                CircuitStatus = "Keep: " + who + " is due at " + oldest.ToString("HH:mm:ss");
                Note("Keep: " + who + " is due at " + oldest.ToString("HH:mm:ss") + "; waiting");
                // A restock of the vendor seen meanwhile ends the wait at once (within 18 tiles of it, AssistState).
                DateTime waitFrom = Clock.UtcNow.ToLocalTime();
                while (!circuitStop && !k.Closed && Clock.UtcNow.ToLocalTime() < oldest
                    && !RestockedSince(pick.Serial, waitFrom))
                    Clock.Sleep(1000);
                if (circuitStop || k.Closed)
                    break;
            }
            // The purse before the trip, at the bank where the player stands (2026-10-21 06:27: the cycle recalled
            // to Kimmie's island with 22 gold and then set out for a bank the island does not have), sized for every
            // vendor of the city bought whole (KeepRules.PurseFor).
            ImmutableArray<VendorStock>.Builder stocks = ImmutableArray.CreateBuilder<VendorStock>();
            foreach (AtlasNpc n in ThreadCandidates(city))
                if (!Skipped(n.Serial))
                    stocks.Add(new VendorStock(ThreadExpected(n), ThreadPrice(n)));
            m_keepPurseNeed = KeepRules.PurseFor(stocks.ToImmutable());
            if (PackCount(k, g => g == GoldArt) < m_keepPurseNeed && CityOf(k) != null)
            {
                string? pw = KeepPurse(k, CityOf(k)!);
                if (pw != null)
                    Note("Keep: the purse in " + CityOf(k) + ": " + pw);
            }
            if (PackNinety(k) && PackCount(k, IsThreadArt) > 0)
            {
                CircuitStatus = "Keep: the thread to the house pile";
                int put;
                string? sw = OverflowStore(k, out put);
                if (sw != null)
                {
                    end = "the house pile: " + sw;
                    break;
                }
                Note("Keep: " + put + " thread on the house pile");
            }
            // To the shop of the vendor due, by its own rune: the first rune for the town landed on Kimmie's island
            // when Ella's was the one due (2026-10-21 06:27:29). ToShop checks the recall reagents (TripReady) itself;
            // a second check here swept the bank bags twice a hop (2026-10-22 06:41:45). Travel, without a shop,
            // is given the check here.
            UoRegions.Shop? pickShop = TailorShopsIn(city).Find(s => s.Holds(pick!.X, pick.Y, 2));
            string? w = pickShop == null && CityOf(k) != city ? TripReady(k, false) : null;
            if (w != null)
            {
                end = w;
                break;
            }
            w = pickShop != null ? ToShop(k, pickShop) : Travel(k, city);
            if (w != null)
            {
                end = "to " + (pickShop != null ? pickShop.Text : city) + ": " + w;
                break;
            }
            List<AtlasNpc> left = ThreadCandidates(city).FindAll(n => !Skipped(n.Serial));
            bool first = true;
            while (left.Count > 0 && !circuitStop && !k.Closed)
            {
                AtlasNpc? v = first ? pick : NearestFirst(k, left)[0];
                left.RemoveAll(n => n.Serial == v!.Serial);
                first = false;
                // Each vendor's own shop, walked to or reached by its rune (Jhelom's two islands).
                UoRegions.Shop? vShop = TailorShopsIn(city).Find(s => s.Holds(v!.X, v.Y, 2));
                if (vShop != null)
                {
                    w = ToShop(k, vShop);
                    if (w != null)
                    {
                        Note("Keep: to " + vShop.Text + ": " + w);
                        if (KeepRules.Ends(w))
                        {
                            end = "to " + vShop.Text + ": " + w;
                            break;
                        }
                        SkipVendor(v!.Serial, w);
                        continue;
                    }
                }
                string name = v!.Name.Split(' ')[0];
                CircuitStatus = "Keep: asking " + name + " in " + city + " (" + bought + " thread, " + dry + " dry)";
                int g2, left2;
                w = VendorBuy(IsThreadArt, ThreadWant * 10, out g2, out left2, name, int.MaxValue, true);
                if (w != null && w.StartsWith(PurseShort) && !w.StartsWith(PurseShort + "room for "))
                {
                    // Short of the whole stock: the answer asks for gold, so the purse is filled and the vendor asked
                    // once more.
                    m_keepPurseNeed = Math.Max(m_keepPurseNeed, m_lastBuyWhole);
                    string? pw = KeepPurse(k, city, true);
                    Note("Keep: " + name + ": " + w + "; the purse: " + (pw ?? "filled"));
                    if (pw == null)
                        w = VendorBuy(IsThreadArt, ThreadWant * 10, out g2, out left2, name, int.MaxValue, true);
                }
                asks++;
                if (w == null)
                {
                    FailOk("buy vendor " + v.Serial.ToString("X"));
                    bought += g2;
                    Note("Keep: " + name + " in " + city + ": " + g2 + " thread");
                    string? ow = StashThread(k, city);
                    if (ow != null)
                    {
                        end = ow;
                        break;
                    }
                    continue;
                }
                if (w == "the vendor has none")
                {
                    dry++;
                    Note("Keep: " + name + " in " + city + ": none");
                    continue;
                }
                Note("Keep: " + name + ": " + w);
                if (Silent(w))
                {
                    end = "asking " + name + ": " + w;
                    break;
                }
                SkipVendor(v.Serial, w);
            }
            if (end != null)
                break;
            FetchStashes(k, city);
            laps++;
        }
        return (end ?? (circuitStop ? "stopped" : "the connection closed")) + "; " + asks + " asks, " + bought
            + " thread bought, " + dry + " dry, " + laps + " cities";
    }
    // The gold Keep thread's current visit needs (KeepRules.PurseFor), raised to a vendor's own whole price when its
    // answer asked for more; every bank stop leaves it in the pack (KeepRules.PackKeeps).
    volatile int m_keepPurseNeed;
    // A thread vendor's price for one thread: the highest it charged in the trades on record, else ThreadGp.
    int ThreadPrice(AtlasNpc n)
    {
        int price = ThreadGp;
        if (Atlas == null)
            return price;
        foreach (AtlasTrade t in Atlas.Trades(n.Serial))
            if (t.Kind == "buy" && t.Item.IndexOf("thread", StringComparison.OrdinalIgnoreCase) >= 0 && t.Price > price)
                price = t.Price;
        return price;
    }
    // The purse filled from the town's bank box's top-level gold up to keepPurseNeed (always, when the answer asked for
    // gold). Null, or why not; a box that filled it short says so (it said nothing when it took none, 2026-10-22
    // 06:43:25 "took gold coin 0").
    string? KeepPurse(GameLink k, string city, bool asked = false)
    {
        if (!asked && PackCount(k, g => g == GoldArt) >= m_keepPurseNeed)
            return null;
        string? w = ToCityBank(k, city, "the purse");
        if (w != null)
            return w;
        string taken;
        w = PurseTo(m_keepPurseNeed, out taken);
        if (w != null)
            return w;
        int have = PackCount(k, g => g == GoldArt);
        return have >= m_keepPurseNeed ? null
            : "the bank box's top-level gold filled the purse to " + have + " of the " + m_keepPurseNeed
            + " this visit needs (" + taken + ")";
    }
    // The thread past the carry limit (less SewWeightRoom) put down in the shop where the player stands, on its most
    // hidden spot (StashSpots), onto a stash of the same thread already there when one lies on that spot, and recorded
    // for the return (Stashes). A spot the server refuses (the stack comes back) is not tried again; the eight tiles
    // beside the player are the last resort (PutDownBeside). Null, or why the cycle stops: every spot refused and the
    // player still over the limit, unable to recall.
    string? StashThread(GameLink k, string city)
    {
        int over = WeightNow(k) - (CarryLimit(k) - SewWeightRoom);
        if (CarryLimit(k) == 0 || over <= 0)
            return null;
        AssistItem? st = LargestCarried(k, IsThreadArt);
        if (st == null)
            return null;
        int n = Math.Min(Math.Max(1, st.Amount), CountFor(st.Graphic, over));
        int px, py, pz;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
            pz = k.State.Z;
        }
        UoRegions.Shop? shop = TailorShopsIn(city).Find(s => s.Holds(px, py, 1));
        Set<long> refused = Set<long>.Empty;
        Set<long> taken = Set<long>.Empty;
        lock (k.State.Gate)
        {
            foreach (AssistItem g in k.State.Ground.Values)
                if (g.Graphic != st.Graphic)
                    taken = taken.Add(TileKey(g.X, g.Y));
            foreach (int[] m in k.State.Mobiles.Values)
                taken = taken.Add(TileKey(m[0], m[1]));
        }
        if (shop != null)
        {
            ImmutableArray<StashSpots.Spot> spots = StashSpots.Find(shop.X, shop.Y, shop.W, shop.H, px, py, pz,
                (x, y) => taken.Contains(TileKey(x, y)) || refused.Contains(TileKey(x, y)));
            for (int i = 0; i < spots.Length && i < StashTries && !circuitStop; i++)
            {
                StashSpots.Spot s = spots[i];
                if (Tiles(s.X, s.Y, px, py) > 1)
                {
                    CircuitStatus = "Keep: to the stash spot " + s.X + "," + s.Y;
                    string? ww = WalkTo(s.X, s.Y, 1);
                    if (ww != null)
                    {
                        refused = refused.Add(TileKey(s.X, s.Y));
                        continue;
                    }
                }
                AssistItem? onto = null;
                lock (k.State.Gate)
                    foreach (AssistItem g in k.State.Ground.Values)
                        if (g.Graphic == st.Graphic && g.X == s.X && g.Y == s.Y)
                            onto = g;
                int before = WeightNow(k);
                uint serial = st.Serial;
                string? w;
                using (Act(k))
                {
                    w = MoveStack(k, serial, n, "thread", "Stashing thread",
                        () => (onto != null ? k.Drop(serial, onto.Serial) : k.DropGround(serial, s.X, s.Y, s.Z))!);
                }
                if (w != null || WeightNow(k) >= before)
                {
                    refused = refused.Add(TileKey(s.X, s.Y));
                    Note("Keep: the stash spot " + s.X + "," + s.Y + " refused: " + (w ?? "the thread came back"));
                    continue;
                }
                uint kept = onto != null ? onto.Serial : FloorStack(k, st.Graphic, s.X, s.Y, serial);
                int amount = n + (onto != null ? Math.Max(1, onto.Amount) : 0);
                Leave(kept);
                Stashed.Put(new Stash(kept, st.Graphic, amount, s.X, s.Y, s.Z, city, Clock.UtcNow.ToLocalTime()));
                Note("Keep: " + n + " thread stashed at " + s.X + "," + s.Y + " in " + UoRegions.Display(shop) + " ("
                    + (s.Cover == 4 ? "hidden"
                    : s.Cover > 0 ? "half hidden" : s.Furnished ? "by the furniture" : "in view") + "; " + WeightNow(k)
                    + " of " + CarryLimit(k) + " stones)");
                return null;
            }
        }
        int[]? at;
        string? bw = PutDownBeside(k, st.Serial, n, "thread", out at);
        if (bw == null)
        {
            uint kept = FloorStack(k, st.Graphic, at![0], at[1], st.Serial);
            Leave(kept);
            Stashed.Put(new Stash(kept, st.Graphic, n, at[0], at[1], at[2], city, Clock.UtcNow.ToLocalTime()));
            Note("Keep: " + n + " thread stashed beside you at " + at[0] + "," + at[1]);
            return null;
        }
        return WeightNow(k) > CarryLimit(k) ? "over the carry limit and the thread would not go down: " + bw : null;
    }
    public const int StashTries = 4;
    // The serial of the stack of a graphic lying on a tile: a split stack lands under a new serial; the stack lifted
    // when it went down whole.
    static uint FloorStack(GameLink k, int graphic, int x, int y, uint lifted)
    {
        lock (k.State.Gate)
        {
            AssistItem? g;
            if (k.State.Ground.TryGet(lifted, out g))
                return lifted;
            foreach (AssistItem it in k.State.Ground.Values)
                if (it.Graphic == graphic && it.X == x && it.Y == y)
                    return it.Serial;
        }
        return lifted;
    }
    Stashes m_stashed = null!; Stashes Stashed => m_stashed ?? (m_stashed = new Stashes(() => ServerFile("stash")));
    // The city's stashes lifted back as far as the carry limit allows (Damian, 2026-10-21: "there will be moments of
    // opportunity to clean up"); one gone from its tile is forgotten. Each is looked for once a visit.
    void FetchStashes(GameLink k, string city)
    {
        uint pack;
        lock (k.State.Gate)
            pack = k.State.Backpack;
        foreach (Stash s in Stashed.All)
        {
            if (circuitStop || k.Closed || s.City != city)
                continue;
            int room = FitsIn(s.Graphic, CarryLimit(k) - SewWeightRoom - WeightNow(k));
            if (room <= 0)
                return;
            CircuitStatus = "Keep: back for the stash at " + s.X + "," + s.Y;
            string? w = WalkTo(s.X, s.Y, 1);
            if (w != null)
            {
                Note("Keep: walking to the stash at " + s.X + "," + s.Y + ": " + w);
                continue;
            }
            AssistItem? g;
            k.State.Ground.TryGet(s.Serial, out g);
            if (g == null)
                lock (k.State.Gate)
                    foreach (AssistItem it in k.State.Ground.Values)
                        if (it.Graphic == s.Graphic && it.X == s.X && it.Y == s.Y)
                            g = it;
            if (g == null)
            {
                Note("Keep: the stash of " + s.Amount + " thread at " + s.X + "," + s.Y + " is gone ("
                    + (int)(Clock.UtcNow.ToLocalTime() - s.At).TotalMinutes + " minutes after it was put down)");
                Stashed.Remove(s.Serial);
                continue;
            }
            int amount = Math.Max(1, g.Amount), take = Math.Min(amount, room);
            uint serial = g.Serial;
            using (Act(k))
            {
                w = MoveStack(k, serial, take, "thread", "Lifting the stash back", () => k.Drop(serial, pack)!);
            }
            if (w != null)
            {
                Note("Keep: lifting the stash at " + s.X + "," + s.Y + ": " + w);
                continue;
            }
            if (take >= amount)
                Stashed.Remove(s.Serial);
            else
                Stashed.Put(s with
                {
                    Serial = FloorStack(k, s.Graphic, s.X, s.Y, serial),
                    Amount = amount - take
                });
            Note("Keep: " + take + " thread lifted back from " + s.X + "," + s.Y
                + (take < amount ? "; " + (amount - take) + " left there" : ""));
        }
    }
    public string? CircuitGo()
    {
        return StartCircuit(null!);
    }
    // The tour (RunTour): every tailor shop once, one shirt sold to each tailor that buys and all its thread bought.
    volatile bool m_tourMode;
    public string? CircuitTour()
    {
        return StartCircuit(() => m_tourMode = true);
    }
    // The mining circuit (RunMine): shovels tinkered, ore dug through the cave the circuit starts in, smelted at the
    // forge set up, the ingots stored where the player keeps them.
    volatile bool m_mineMode;
    public string? CircuitMine()
    {
        return StartCircuit(() => m_mineMode = true);
    }
    // The buy circuit (RunBuy): the shopping list bought at the picked shops and stored in the bank.
    volatile bool m_buyMode;
    public string? CircuitBuy()
    {
        return StartCircuit(() => { m_buyMode = true; m_buyKind = "buy"; });
    }
    // The ingot-buying circuit: RunBuy over BuyListFor("ingots") at the smith shops.
    volatile string m_buyKind = "buy";
    public string? CircuitBuyIngots()
    {
        return StartCircuit(() => { m_buyMode = true; m_buyKind = "ingots"; });
    }
    // The survey (RunSurvey): every shop in a town visited and each keeper's stock read, nothing bought or sold.
    volatile bool m_surveyMode;
    public string? CircuitSurvey()
    {
        return StartCircuit(() => m_surveyMode = true);
    }
    // The refresh (RunRefresh): food for the mount and the player, both fed full, then the bank.
    volatile bool m_refreshMode;
    public string? CircuitRefresh()
    {
        return StartCircuit(() => m_refreshMode = true);
    }
    // The smithing circuit (RunSmithing): the ladder smithed at the anvil it starts at, sold, until 100.0.
    volatile bool m_smithMode;
    public string? CircuitSmith()
    {
        return StartCircuit(() => m_smithMode = true);
    }
    // The town surveyed: circuit.survey.town, empty for the town the player is in.
    public string? SurveyTown
    {
        get => Cfg.Get("circuit.survey.town", "");
        set => Cfg.Set("circuit.survey.town", value ?? "");
    }
    public string? SurveyTarget()
    {
        string? t = SurveyTown;
        if (t!.Length > 0)
            return t;
        GameLink k = m_link;
        return k == null ? null : CityOf(k);
    }
    // The town's shops to survey: the ones ticked (circuit.shops.survey) among the town's, else every shop in it.
    public List<UoRegions.Shop> SurveyShops()
    {
        List<UoRegions.Shop> all = UoRegions.AllShopsIn(SurveyTarget()!);
        List<string> keys = CircuitShopKeys("survey");
        List<UoRegions.Shop> picked = all.FindAll(s => keys.Contains(s.Key));
        return picked.Count > 0 ? picked : all;
    }
    // The mode is set only once nothing is running: a start refused mid-circuit leaves the running circuit's mode
    // alone.
    string? StartCircuit(Action mode)
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "no game connection";
        if (m_circuitThread != null && m_circuitThread.IsAlive)
            return "the circuit is running";
        if (m_loops.Running)
            return "a loop is running";
        if (WorkRunning)
            return "Mill, spin and weave is running";
        if (HopRunning)
            return "a bunny hop is running";
        m_sellOnly = m_threadOnly = m_keepMode = m_tourMode = m_mineMode = m_buyMode = m_surveyMode = m_refreshMode = m_smithMode = m_advertMode = m_spellbookMode = m_markMode = m_lumberMode = m_featherMode = m_reagentMode = m_collectOnly = m_collectNow = false;
        if (mode != null)
            mode();
        ManualStart();
        m_circuitAlarm = null;
        CircuitStart = Clock.UtcNow.ToLocalTime();
        ResetCooldowns();
        m_sweptThisCircuit = false;
        if (m_critique)
        {
            m_critique = false;
            if (Recording)
                RecordToggle("training, Go");
        }
        m_lastRun = null;
        // Every toolbag whose toolbox is within reach is topped up before the circuit sets out (AssistTools.md, "Road
        // counts").
        List<Profession> tools = CircuitProfessions();
        m_circuitThread = NewThread(() => { try { RefillInReach(k,
            tools); } catch (Exception e) { Note("Tools: the refill stopped: " + e.GetType().Name + " "
            + e.Message); } RunCircuit(k); });
        m_circuitThread.IsBackground = true;
        m_circuitThread.Start();
        return null;
    }
    // A start clears every timed avoidance, saved ones too (Damian, 2026-10-18: "we need the start to refresh the
    // cooldown timers"; sell-only passed Ella, standing beside Kwik and willing to buy, for an avoidance set an hour
    // before).
    void ResetCooldowns(bool start = true)
    {
        lock (m_underFloorGate)
        {
            m_underFloor = m_underFloor.Clear();
            m_underFloorLoaded = true;
            try
            {
                File.WriteAllLines(UnderFloorPath, new string[0]);
            }
            catch { }
        }
        lock (m_skippedGate)
            m_skipped = m_skipped.Clear();
        lock (m_bankSkipGate)
            m_bankSkip = m_bankSkip.Clear();
        lock (m_roofNoneGate)
            m_roofNone = m_roofNone.Clear();
        lock (m_shopAvoidGate)
        {
            m_shopAvoid = m_shopAvoid.Clear();
            m_shopAvoidLoaded = true;
            try
            {
                File.WriteAllLines(ShopAvoidPath, new string[0]);
            }
            catch { }
        }
        if (start)
            Note("Circuit: start; every vendor, bank and shop avoidance cleared");
    }
    // Two resets (Damian, 2026-10-18: "a global reset button for all transient waits or blocks like on vendors who dont
    // respond"; "2 buttons, one is rediscover for world state, and reinitialize for circuit state").
    // Reinitialize, the circuit's state: the avoidances a start clears, the unanswered-line guard, the second kit's
    // wait.
    public string? Reinitialize()
    {
        ResetCooldowns(false);
        m_spareKitAfter = DateTime.MinValue;
        GameLink k = m_link;
        if (k != null)
            k.ForgetSaid();
        Note("Reinitialize: every vendor, bank and shop avoidance, the unanswered-line guard and the spare kit wait "
            + "cleared");
        return null;
    }
    // Reset, our own things (Damian, 2026-10-18: "Reset should be for tools / bags / etc ... that is controllable by
    // us"): every profession's toolbox and toolbag, the bank bags picked, and the labels read off our runes, all to be
    // picked or read again.
    public string? ResetOurs()
    {
        Cfg.Set(Server + "packtoolbag", "");
        Cfg.Set(Server + "mine.toolbag", "");
        foreach (Profession p in s_professions)
        {
            Tools.SetBox(p, 0);
            Tools.SetBag(p, 0);
        }
        foreach (string slot in s_bankBagSlots)
            Cfg.Set(Server + BankBagKey(slot), "");
        lock (m_bankRunesGate)
        {
            m_bankRunes = m_bankRunes.Clear();
            m_bankRunesLoaded = true;
            try
            {
                File.Delete(BankRunesPath);
            }
            catch { }
        }
        lock (m_packRunesGate)
            m_packRunes = m_packRunes.Clear();
        Note("Reset: every toolbox and toolbag, the bank bags and the runes' labels forgotten, to be picked or read "
            + "again");
        return null;
    }
    // Rediscover, the world (Damian, 2026-10-18: "world state i meant was like npcs, zones, anvils, etc"): every NPC
    // and place in the atlas (vendors and their stock, anvils, forges, houses, guildstones), the looms seen, the bank
    // signs and the banks that answered, the vendors' homes, the teleporters marked dead and the Teleports refused, the
    // smith shops looked in, the keepers surveyed, and each tailor's stock; all seen again as the player goes. The
    // trades and the learned restocks stay, being our own records; the regions are the server's file.
    public string? Rediscover()
    {
        if (Atlas != null)
        {
            Atlas.ForgetWorld();
            Atlas.Save();
        }
        lock (m_bankSigns)
        {
            m_bankSigns.Clear();
            m_bankSignsLoaded = true;
            try
            {
                File.Delete(BankSignsPath);
            }
            catch { }
        }
        lock (m_bankHeardGate)
        {
            m_bankHeard = m_bankHeard.Clear();
            m_bankHeardLoaded = true;
            try
            {
                File.Delete(BankHeardPath);
            }
            catch { }
        }
        lock (m_vendorHomesGate)
        {
            m_vendorHomes = m_vendorHomes.Clear();
            m_vendorHomesLoaded = true;
            try
            {
                File.Delete(VendorHomesPath);
            }
            catch { }
        }
        lock (m_teleportRefusedGate)
        {
            m_teleportRefused = m_teleportRefused.Clear();
            m_teleportRefusedLoaded = true;
            try
            {
                File.Delete(TeleportRefusedPath);
            }
            catch { }
        }
        lock (m_teleportersGate)
        {
            try
            {
                File.Delete(DeadTeleportersPath);
            }
            catch { }
            m_teleporters = m_teleporters.Clear();
            m_guessedTeleporters = m_guessedTeleporters.Clear();
            m_teleportersLoaded = false;
        }
        m_smithyLooked = m_smithyLooked.Clear();
        lock (m_surveyedKeepersGate)
            m_surveyedKeepers = m_surveyedKeepers.Clear();
        lock (m_circuitVendorsGate)
        {
            LoadCircuit();
            foreach (CircuitVendor v in m_circuitVendors.Values)
            {
                v.Stock = -1;
                v.BoughtOutAt = DateTime.MinValue;
            }
            SaveCircuit();
        }
        Note("Rediscover: every NPC and place, the looms, the banks, the vendors' homes, the dead teleporters and "
            + "refused Teleports, the smith shops looked in, the keepers surveyed and the tailors' stock forgotten, "
            + "to be seen again");
        return null;
    }
    // A step clears the stop only when no circuit runs, a step run from the control port: a step inside the circuit
    // that cleared it made a stop asked mid-trip do nothing (2026-10-17 10:42:49, the circuit went on to Vesper and
    // Jhelom).
    void ManualStart()
    {
        Thread t = m_circuitThread;
        if (t == null || !t.IsAlive)
            circuitStop = false;
    }
    public bool CircuitRunning
    {
        get
        {
            Thread t = m_circuitThread;
            return t != null && t.IsAlive;
        }
    }
    public void CircuitHalt(string why)
    {
        if (m_circuitThread != null && m_circuitThread.IsAlive)
        {
            circuitStop = true;
            m_weaveRunner.Stop();
            Note("Circuit: stopping, " + why);
        }
    }
    AtlasNpc? VendorNow(uint serial)
    {
        if (Atlas == null)
            return null;
        foreach (AtlasNpc n in Atlas.Visible())
            if (n.Serial == serial)
                return n;
        return null;
    }
    // A named vendor is walked to before it is asked: it hears its name from 3 tiles, and one behind a door is reached
    // by opening the door and going into the room (Damian, 2026-10-17: "if they can't be seen, you need to open the
    // door and go in the room"; Michelle was 20 tiles from the east tailor's door). The nearest of that first name the
    // atlas knows within CircuitReach, as last seen; null when there, unknown, or "vendor".
    // A vendor out of view is where it was last seen, perhaps a day ago, so the walk is taken again from where it ends,
    // to where the vendor is seen now (Jannelle, 2026-10-17: walked to 3772,1248 from 50 tiles off, seen at 3765,1262).
    public const int VendorWalks = 3;
    public const string TookOver = "the player took over";
    // A shop asked and, unanswered, asked again after the walk is taken anew by where the vendor is seen now: a vendor
    // the atlas still had 2 tiles off did not hear "Jannelle buy" (2026-10-17 09:17), then answered the sale.
    // Every circuit's asks come through here (VendorBuy, VendorSell). A vendor going home is asked again once it is
    // home, as often as it answers "Let us go back to my shop", GoingHomeAsks asks in all (Damian, 2026-10-17: "if they
    // repeat the let us go line, wait again and observe the walking"); a positive answer records where it stands as its
    // home.
    public const int GoingHomeAsks = 3;
    byte[]? AskNear(GameLink k, string who, string word, int op)
    {
        bool askedAgain = false;
        int[]? fix = null;
        NotHome = null;
        // A vendor where it has sent the player home before is not asked there: it is watched until it is home, and
        // passed over when it stays away (Cybil on the Magincia mage shop roof).
        AtlasNpc? named = who == "vendor" ? null : VendorNamed(k, who);
        if (named != null && VendorAtHome(k, named.Serial) == false)
        {
            string where;
            VendorArea(k, named.Serial, out where);
            Note("Circuit: " + who + " is away from its shop (" + where + "); waiting for it to be home");
            if (m_watchSelling)
                WatchStatus = "waiting: " + who + " is away from its shop (" + where + ")";
            if (!WaitVendorHome(k, who))
            {
                VendorArea(k, named.Serial, out where);
                NotHome = who + " stayed away from its shop (" + where + ")";
                return null;
            }
            if (NearVendor(k, who) != null)
                return null;
        }
        for (int attempt = 0; attempt < GoingHomeAsks; attempt++)
        {
            // Asked again only when the answer says the vendor is going home, and then only at home, or says "hand it
            // to me" (AskAgain, once), after SellGapMs: every other answer ("I do not understand." from a minter or a
            // guard) is final, and one nobody knows is kicked up. Silence is final only when nothing changed (Damian,
            // 2026-10-17: "L-FUTILE means if nothing else changes: meaning take a step, re-evaluate and re-measure,
            // then try again").
            if (attempt > 0)
            {
                Reply kind = KindOf(LastReply!);
                string? changed;
                if (kind == Reply.Unknown)
                    KickUp(who + " " + word, LastReply!);
                if (kind == Reply.AskAgain && !askedAgain)
                {
                    askedAgain = true;
                    // The shopkeeper's conversation answers a "sell" or "buy" heard from past 3 tiles (rundir
                    // scripts.wombat/human.m:2027, getDistanceInTiles > 3 hands the speech to the convo): the vendor
                    // is closed in on where it stands now before the one more ask (Damian, 2026-10-25: Morena walked
                    // off after the first ask and "responded with essentially a request to move closer").
                    string? nw = NearVendor(k, who);
                    if (nw != null)
                    {
                        Note("Circuit: " + who + " answered from out of a sale's reach: " + nw);
                        return null;
                    }
                    Clock.Sleep(SellGapMs);
                }
                else if (kind == Reply.None && SayRefused == null && who != "vendor"
                    && (changed = SilenceChanged(fix!, VendorFix(k, who)!, OtherWindow)) != null)
                {
                    Note("Circuit: " + who + " did not answer \"" + word + "\": " + changed
                        + "; closing in to ask again");
                    string? nw = NearVendor(k, who);
                    if (nw != null)
                    {
                        Note("Circuit: " + nw);
                        return null;
                    }
                }
                else if (kind != Reply.GoingHome || who == "vendor")
                {
                    return null;
                }
                else
                {
                    // The region it answered in before, walked to while it goes there (Damian: "next time you visit,
                    // you could anticipate a walking if they are far from known home"); then the vendor watched until
                    // it is home, and the player follows it there (Damian: "kwik needed to follow the vendor to the
                    // shop and retry").
                    NoteVendorAway(k, who);
                    AtlasNpc? v = VendorNamed(k, who);
                    int[]? home = v == null ? null : VendorHomeRegion(k, v);
                    if (home != null)
                    {
                        string? hw = WalkToSpeakingRange(home[0], home[1], HomeFloor(home));
                        if (hw != null)
                            Note("Circuit: walking to " + who + "'s shop: " + hw);
                    }
                    if (!WaitVendorHome(k, who, true))
                    {
                        string where;
                        VendorArea(k, v!.Serial, out where);
                        NotHome = who + " stayed away from its shop (" + where + ")";
                        return null;
                    }
                    if (NearVendor(k, who) != null)
                        return null;
                }
                // The answer asked for this one more ask (assist-plan.md rule 1).
                k.Heard(who + " " + word);
            }
            byte[]? r;
            using (Act(k))
            {
                fix = VendorFix(k, who);
                r = AskShop(k, who, word, op);
            }
            if (r != null)
            {
                NoteVendorHome(k, who);
                return r;
            }
        }
        if (KindOf(LastReply!) == Reply.Unknown)
            KickUp(who + " " + word, LastReply!);
        return null;
    }
    // Where the vendor asked stood as the server last placed it, and whether it could hear from there (in view, within
    // VendorHear, in sight): { x, y, hears }, { 0, 0, 0 } out of view, null when no vendor of that name is in view.
    int[]? VendorFix(GameLink k, string who)
    {
        AtlasNpc? v = VendorNamed(k, who);
        if (v == null)
            return null;
        int px, py, pz;
        int[]? at;
        bool seen;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
            pz = k.State.Z;
            seen = k.State.Mobiles.TryGet(v.Serial, out at);
            if (seen)
                at = new[] { at![0], at[1], at[2] };
        }
        if (!seen)
            return new[] { 0, 0, 0 };
        return new[] { at![0], at[1], Hears(k, px, py, pz, at[0], at[1], at[2]) ? 1 : 0 };
    }
    // A vendor hears only on the player's floor: Edmund, 1 and 2 tiles off on the floor below (z 20, the player at 40),
    // answered none of three "Edmund buy" (Magincia, 2026-10-17 02:55). FloorApart: floors stand 20 apart; a step, dais
    // or slope stays under 16.
    public const int FloorApart = 16;
    bool Hears(GameLink k, int px, int py, int pz, int vx, int vy, int vz)
    {
        return Tiles(vx, vy, px, py) <= VendorHear && OnFloor(pz, vz) && Sight(k, px, py, pz, vx, vy);
    }
    // What changed between the ask and its silence, or null when nothing did: another vendor's window came in its place
    // (the race), the vendor was out of hearing when the line went out, or it has moved since.
    static string? SilenceChanged(int[] asked, int[] now, bool raced)
    {
        if (raced)
            return "another vendor's window came instead";
        if (asked == null || now == null)
            return null;
        if (asked[2] == 0)
            return "it was out of hearing when asked";
        if (now[0] != asked[0] || now[1] != asked[1])
            return now[2] == 0 && now[0] == 0 ? "it went out of view"
                : "it moved from " + asked[0] + "," + asked[1] + " to " + now[0] + "," + now[1];
        return null;
    }
    // The vendor is located before it is asked, not after it fails to answer (Damian, 2026-10-17: "be sure to locate
    // the npc and test its distance before asking to buy or sell, not after a failure"): its tile is the server's (the
    // mobiles in view, AssistState.Mobiles), the atlas only says where to walk until it comes into view, and the ask
    // goes out only with the vendor in view and within VendorHear (ServUO BaseVendor: 3 tiles, by range alone).
    public const int VendorHear = 3, VendorFindReach = 400;
    // The shop region (the 1998 region file's) holding the tile, its door a tile out included, or null.
    static UoRegions.Shop? ShopAt(int x, int y)
    {
        string? c = UoRegions.City(x, y);
        if (c == null)
            return null;
        foreach (UoRegions.Shop s in UoRegions.AllShopsIn(c))
            if (s.Holds(x, y, 1))
                return s;
        return null;
    }
    string? NearVendor(GameLink k, string who)
    {
        if (Atlas == null || who == "vendor")
            return null;
        string why = who + " is not known here";
        for (int leg = 0; leg <= VendorWalks; leg++)
        {
            int px, py;
            lock (k.State.Gate)
            {
                px = k.State.X;
                py = k.State.Y;
            }
            AtlasNpc? best = null;
            int bx = 0, by = 0, bz = 0;
            bool bestSeen = false;
            List<AtlasNpc> known = Atlas.Visible();
            known.AddRange(Atlas.Shops());
            foreach (AtlasNpc n in known)
            {
                if (!string.Equals(n.Name.Split(' ')[0], who, StringComparison.OrdinalIgnoreCase))
                    continue;
                int x = n.X, y = n.Y, z = 0;
                bool seen;
                lock (k.State.Gate)
                {
                    int[]? at;
                    seen = k.State.Mobiles.TryGet(n.Serial, out at);
                    if (seen)
                    {
                        x = at![0];
                        y = at[1];
                        z = at[2];
                    }
                }
                // A named vendor is looked for across the city: Jhelom's Kimmie is 224 tiles from the recall landing,
                // and a reach of CircuitReach made her "not known here".
                if (Tiles(x, y, px, py) > VendorFindReach)
                    continue;
                // One in view first, then the nearest.
                if (best == null || (seen && !bestSeen)
                    || (seen == bestSeen && Tiles(x, y, px, py) < Tiles(bx, by, px, py)))
                {
                    best = n;
                    bx = x;
                    by = y;
                    bz = z;
                    bestSeen = seen;
                }
            }
            if (best == null)
                return why;
            int pz;
            lock (k.State.Gate)
                pz = k.State.Z;
            int floor = bestSeen ? bz : AnyFloor;
            List<int[]> rivals = Interceptors(k, best.Serial, floor);
            if (bestSeen && Hears(k, px, py, pz, bx, by, bz))
            {
                int[]? rival = rivals.Find(r => Tiles(r[0], r[1], px, py) <= Tiles(bx, by, px, py));
                if (rival == null)
                    return null;
                why = who + " is " + Tiles(bx, by, px, py) + " tiles away and another vendor "
                    + Tiles(rival[0], rival[1], px, py);
                if (leg == VendorWalks)
                {
                    Note("Circuit: " + why + "; asking from here");
                    return null;
                }
                // With no nearer tile reached, the vendor is asked from here and its answer decides (Damian,
                // 2026-10-18: "if it could buy immediatly after that fail, then the fail was in error predicting the
                // fault"; the sale to Lynn was refused for a rival, and Lynn sold thread 2 s later).
                List<int[]> spots = NearerSpots(k, px, py, bx, by, bestSeen ? bz : pz, rivals);
                if (spots.Count == 0)
                {
                    Note("Circuit: " + why + ", and no tile within " + VendorHear + " of " + who
                        + " is nearer; asking from here");
                    return null;
                }
                // Inside the vendor's shop and with no Teleport, as the in-shop walk below (Jhelom 21:47:24: out of
                // Ella's door and round the building; Britain 21:12:03: a Teleport onto a display case).
                string? sw = null;
                UoRegions.Shop? vshop = ShopAt(bx, by);
                if (vshop != null)
                    s_walkWithin = new[] { vshop.X - 1, vshop.Y - 1, vshop.W + 2, vshop.H + 2 };
                try
                {
                    foreach (int[] s in spots.GetRange(0, Math.Min(3, spots.Count)))
                    {
                        sw = WalkTo(s[0], s[1], 0, false, floor, false);
                        if (sw == null)
                            break;
                    }
                }
                finally { s_walkWithin = null; }
                if (sw != null)
                {
                    Note("Circuit: stepping nearer " + who + " than the other vendor: " + sw + "; asking from here");
                    return null;
                }
                continue;
            }
            why = bestSeen ? who + " is " + Tiles(bx, by, px, py) + " tiles away"
                : who + " is not in view at " + bx + "," + by;
            if (leg == VendorWalks)
                break;
            // In the same shop as the vendor, the walk stays inside it (Damian, 2026-10-19: "this is twice now that
            // they have left the building to talk to a vendor that is inside the building ... am i in the building, is
            // the vendor in the building and why would i leave the building to talk to a vendor in the building"): no
            // route out of a door and no Teleport; with no way inside to the vendor, it is asked once from the nearest
            // tile reached. Inside the shop itself: ShopAt's door ring counted the street outside Slade's wall as his
            // shop, and the walk kept to the shop never went round to the door (2026-10-20 00:29:11, "Slade is 2 tiles
            // away", no step taken).
            UoRegions.Shop? inShop = ShopAt(px, py);
            if (inShop != null && inShop.Holds(px, py, 0) && inShop.Holds(bx, by, 1))
            {
                string? iw;
                s_walkWithin = new[] { inShop.X - 1, inShop.Y - 1, inShop.W + 2, inShop.H + 2 };
                try
                {
                    iw = WalkTo(bx, by, VendorReach, true, floor, false);
                    if (iw != null)
                        WalkTo(bx, by, VendorHear, false, floor, false);
                }
                finally { s_walkWithin = null; }
                if (iw == null)
                    continue;
                // The vendor found again where it stands now, and walked to, before an ask from here (Damian,
                // 2026-10-20: "once the wait was over and the door was cleared, the circuit should have reacquired the
                // location of the target"; Michelle walked off during a 20 s held-door wait and answered "My shop sells
                // many things."). A stop or a takeover ends the approach (three "looking again" lines followed the
                // player's takeover, 2026-10-20 15:35:22).
                if (circuitStop || k.Closed || iw == "stopped" || iw.Contains("took over"))
                    return iw;
                if (leg < VendorWalks)
                {
                    Note("Circuit: " + who + " in " + inShop.Text + ": " + iw + "; looking again from where " + who
                        + " stands now");
                    continue;
                }
                Note("Circuit: " + who + " is in this shop (" + inShop.Text
                    + ") and no walk inside it reaches them in sight: " + iw + "; asking from here");
                return null;
            }
            string? w = WalkToSpeakingRange(bx, by, floor);
            if (w != null)
                return "walking to " + who + (bestSeen ? " (z " + bz + ")" : "") + ": " + w;
        }
        return why;
    }
    // A named ask is answered by another vendor nearer the player: Edmund answered "Cybil buy" three times while Cybil
    // walked off (Magincia, 2026-10-17 02:37). The player stands nearer the vendor named than any other vendor in view
    // (Damian, 2026-10-17: "ensuring the distance to target is shorter than distance to interceptor"). VendorRivalReach
    // is ServUO VendorAI's pre-AOS hearing, 4 tiles, around the furthest tile the player can ask from.
    public const int VendorRivalReach = 4;
    // floor: the vendor's; a vendor on another floor does not hear the ask (Hears).
    List<int[]> Interceptors(GameLink k, uint target, int floor)
    {
        List<int[]> list = new();
        if (Atlas == null)
            return list;
        Set<uint> shops = Set<uint>.Empty;
        foreach (AtlasNpc n in Atlas.Shops())
            shops = shops.Add(n.Serial);
        lock (k.State.Gate)
            foreach (KeyValuePair<uint, int[]> kv in k.State.Mobiles)
                if (kv.Key != target && shops.Contains(kv.Key) && OnFloor(kv.Value[2], floor)
                    && Tiles(kv.Value[0], kv.Value[1], k.State.X, k.State.Y) <= VendorHear + VendorRivalReach)
                    list.Add(new[] { kv.Value[0], kv.Value[1] });
        return list;
    }
    // The tiles within VendorHear of the vendor at (vx, vy) nearer it than every rival: the widest margin first, then
    // the nearest the player. Only a tile a walker stands on at floor z with nobody on it: the three picked for Lynn in
    // Skara Brae were two display cases and a wall, every walk to them failed, and the sale was never asked (2026-10-18
    // 02:17:42).
    // Only a tile in sight of the vendor and, the vendor being in a shop, inside that shop; the nearest the player
    // first, any margin over the rivals doing (the widest margin first walked Kwik out of Ella's shop and round it, 19
    // s, Jhelom 2026-10-19 21:47:24).
    List<int[]> NearerSpots(GameLink k, int px, int py, int vx, int vy, int z, List<int[]> rivals)
    {
        List<int[]> spots = new();
        UoRegions.Shop? shop = ShopAt(vx, vy);
        for (int dx = -VendorHear; dx <= VendorHear; dx++)
        {
            for (int dy = -VendorHear; dy <= VendorHear; dy++)
            {
                int x = vx + dx, y = vy + dy, to = Tiles(x, y, vx, vy);
                if (to == 0)
                    continue;
                if (shop != null && !shop.Holds(x, y, 0))
                    continue;
                int sz = UoMap.StandZ(x, y, z);
                if (sz == int.MinValue || Occupied(k, x, y) || !Sight(k, x, y, sz, vx, vy))
                    continue;
                int margin = int.MaxValue;
                foreach (int[] r in rivals)
                    margin = Math.Min(margin, Tiles(x, y, r[0], r[1]) - to);
                if (margin >= 1)
                    spots.Add(new[] { x, y, margin, Tiles(x, y, px, py) });
            }
        }

        spots.Sort((a, b) => a[3] != b[3] ? a[3].CompareTo(b[3]) : b[2].CompareTo(a[2]));
        return spots;
    }
    AssistItem? NearLoom(GameLink k, int px, int py)
    {
        NoteLooms(k);
        AssistItem? loom = null;
        lock (k.State.Gate)
            foreach (AssistItem g in k.State.Ground.Values)
                if (s_looms.IndexOf(g.Graphic) >= 0 && Tiles(g.X, g.Y, px, py) <= CircuitReach
                    && (loom == null || Tiles(g.X, g.Y, px, py) < Tiles(loom.X, loom.Y, px, py)))
                    loom = g;
        return loom;
    }
    // Every loom in view, noted on each walk step, so the nearest loom known is the nearest one passed, not only the
    // one last used.
    void NoteLooms(GameLink k)
    {
        List<int[]> seen = new();
        List<int[]> signs = new();
        lock (k.State.Gate)
            foreach (AssistItem g in k.State.Ground.Values)
            {
                if (s_looms.IndexOf(g.Graphic) >= 0)
                    seen.Add(new[] { g.X, g.Y });
                else if (UoTiles.Name(g.Graphic) == "Bank")
                    signs.Add(new[] { g.X, g.Y });
            }
        if (signs.Count > 0)
        {
            LoadBankSigns();
            bool added = false;
            lock (m_bankSigns)
                foreach (int[] s in signs)
                    if (!m_bankSigns.Exists(t => t[0] == s[0] && t[1] == s[1]))
                    {
                        m_bankSigns.Add(s);
                        added = true;
                    }
            if (added)
                SaveBankSigns();
        }
    }
    // Every town has a bank (Damian, 2026-10-17: "there are many banks, and every town has one"); the 1998 region file
    // misses some (Nujel'm's, by its sign at 3768,1313), so each "Bank" sign seen is kept in banks-<server>.txt (x,
    // tab, y) and banked at in a city whose banks the region file misses.
    readonly List<int[]> m_bankSigns = new(); bool m_bankSignsLoaded;
    string BankSignsPath => ServerFile("banks");
    void LoadBankSigns()
    {
        lock (m_bankSigns)
            LoadOnce(ref m_bankSignsLoaded, BankSignsPath,
                line => { string[] t = line.Split('\t'); if (t.Length
                == 2) m_bankSigns.Add(new[] { int.Parse(t[0]), int.Parse(t[1]) }); });
    }
    void SaveBankSigns()
    {
        lock (m_bankSigns)
        {
            List<string> l = new();
            foreach (int[] s in m_bankSigns)
                l.Add(s[0] + "\t" + s[1]);
            try
            {
                File.WriteAllLines(BankSignsPath, l);
            }
            catch { }
        }
    }
    // A sign stands outside its bank's wall (Vesper's at 2896,673 is outside The Mint), so a city's banks are its BANK_
    // regions, and its signs only when the region file has none for it (Nujel'm).
    // A bank that answered "bank" before goes first, then the nearest (Nujel'm's sign at 3768,1313 went unheard twice
    // and the one at 3762,1316 answered, 2026-10-17); the answered ones are kept in banks-heard-<server>.txt (x, tab,
    // y).
    List<int[]> CityBanks(string city, int x, int y)
    {
        List<int[]> l = UoRegions.BanksIn(city, x, y);
        LoadBankSigns();
        if (l.Count == 0)
            lock (m_bankSigns)
                foreach (int[] s in m_bankSigns)
                    if (UoRegions.City(s[0], s[1]) == city)
                        l.Add(s);
        l.Sort((a, b) => { int ha = WasHeard(a) ? 0 : 1, hb = WasHeard(b) ? 0 : 1; return ha != hb ? ha.CompareTo(hb)
            : Tiles(a[0], a[1], x, y).CompareTo(Tiles(b[0], b[1], x, y)); });
        return l;
    }
    Set<string> m_bankHeard = Set<string>.Empty; bool m_bankHeardLoaded; readonly Lock m_bankHeardGate = new();
    string BankHeardPath => ServerFile("banks-heard");
    bool WasHeard(int[] bank)
    {
        lock (m_bankHeardGate)
        {
            LoadOnce(ref m_bankHeardLoaded, BankHeardPath, line => { string[] t = line.Split('\t'); if (t.Length == 2)
                m_bankHeard = m_bankHeard.Add(t[0] + "," + t[1]); });
            return m_bankHeard.Contains(bank[0] + "," + bank[1]);
        }
    }
    void BankHeard(int[] bank)
    {
        if (WasHeard(bank))
            return;
        lock (m_bankHeardGate)
        {
            m_bankHeard = m_bankHeard.Add(bank[0] + "," + bank[1]);
            List<string> l = new();
            foreach (string s in m_bankHeard.Ordered(StringComparer.Ordinal))
                l.Add(s.Replace(',', '\t'));
            try
            {
                File.WriteAllLines(BankHeardPath, l);
            }
            catch { }
        }
    }
}
