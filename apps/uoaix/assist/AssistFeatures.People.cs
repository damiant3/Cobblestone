#nullable enable
using System.Collections.Immutable;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace Uoaix.Assist;

public sealed partial class AssistFeatures
{
    // ---------- people seen ----------
    // Damian, 2026-10-15: "keep a report of all players seen, any activities they were performing, where they were,
    // noteriety and criminal details". Every human-bodied mobile (0x190 to 0x193, ghosts included) but the player; the
    // 1.25 wire does not tell players from human NPCs. Activity from 0x2F (fight), 0x6E (animation), 0x1C (speech) and
    // 0xAF (death); notoriety from 0x77/0x78, every change kept. Written as Markdown every ReportMs.
    public sealed class Person
    {
        public uint Serial;
        public string Name = "";
        public int Body, Noto, X, Y, Z;
        public bool NotoKnown;
        public DateTime First, Last;
        public readonly List<string> Acts = new(); public string LastAct = null!; public int Repeats;
    }
    public const int PeopleKeep = 2000, ActsKeep = 100, ReportMs = 30000;
    static readonly ImmutableArray<string> s_notorieties = [ "none", "innocent", "friend", "attackable", "criminal",
        "enemy",
        "murderer", "invulnerable" ];
    Map<uint, Person> m_people = Map<uint, Person>.Empty; readonly Lock m_peopleGate = new();
    public string PeopleReportPath = "";
    bool m_peopleChanged; Timer m_reportTimer = null!;
    // Human action numbers (the 1.25 client's body animations): swings 9-13, casting 16-17, shooting 18-19, bow 32,
    // salute 33, eat 34.
    static string? Action(int a)
    {
        if (a >= 9 && a <= 13)
            return "swinging (fighting or harvesting)";
        if (a == 16 || a == 17)
            return "casting";
        if (a == 18 || a == 19)
            return "shooting";
        if (a == 32)
            return "bowing";
        if (a == 33)
            return "saluting";
        if (a == 34)
            return "eating";
        return null;
    }
    // Creatures (every mobile that is not human-bodied) for the report's second part (Damian, 2026-10-15: "also for the
    // report creatures seen, and where"): one row per serial, first and last place and time, and whether it died.
    public sealed class Creature
    {
        public uint Serial; public int Body, X0, Y0, X, Y; public DateTime First, Last; public bool Died;
    }
    public const int CreaturesKeep = 5000, PlacesShown = 12;
    Map<uint, Creature> m_creatures = Map<uint, Creature>.Empty; readonly Lock m_creaturesGate = new();
    void SeenCreature(uint s, int body, int x, int y)
    {
        lock (m_creaturesGate)
        {
            Creature? c;
            DateTime now = Clock.UtcNow.ToLocalTime();
            if (!m_creatures.TryGet(s, out c))
            {
                if (m_creatures.Count >= CreaturesKeep)
                    return;
                m_creatures = m_creatures.Set(s, c = new Creature { Serial = s, X0 = x, Y0 = y, First = now });
            }
            c.Body = body;
            c.X = x;
            c.Y = y;
            c.Last = now;
            m_peopleChanged = true;
        }
    }
    public AssistAtlas Atlas = null!;
    // 1.25 art (the client's TILEDATA.MUL): 0x0ED4 the gravestone a 1998 guildstone is drawn with, 0x0BCF-0x0BD2 the
    // wooden and brass signs a house carries. Each such item is single-clicked once for its label.
    public const int GuildstoneArt = 0x0ED4, HouseSignArt0 = 0x0BCF, HouseSignArt1 = 0x0BD2;
    static readonly ImmutableArray<string> s_houseWords = [ "decay", "condition", "repair", "worn", "ruin", "collaps",
        "refresh",
        "owner", "house" ];
    Map<uint, int[]> m_lookItems = Map<uint, int[]>.Empty; readonly Lock m_lookItemsGate = new();  // x, y, graphic
    ValueMap<uint, uint> m_wornBy = ValueMap<uint, uint>.Empty; readonly Lock m_wornByGate = new();  // item, mobile
    Map<uint, List<uint>> m_contents = Map<uint, List<uint>>.Empty; readonly Lock m_contentsGate = new();
    Map<uint, List<int>> m_contentAmounts = Map<uint, List<int>>.Empty;  // box, amounts in contents order
    // The container the last open (0x24) named: the contents list after it (0x3C) names its container only through its
    // items, so an empty one names none (uo1998 2026-10-22 16:26:09, the mining toolbag 44831E78: 24, then 3C 0000).
    uint m_lastOpened;
    Map<uint, object[]> m_offers = Map<uint, object[]>.Empty; readonly Lock m_offersGate = new();  // name, price, vendor
    DateTime m_offersAt;
    // A buy (client 0x3B: vendor, flag, then layer, serial, amount per item) or a sell (client 0x9F: vendor, count,
    // then serial, amount per item), priced from the offer the server made, recorded per item.
    // byHand: the client sent it (the player's own trade), not a circuit.
    void Traded(GameLink k, byte[] p, bool byHand = false)
    {
        if (Atlas == null || p.Length < 8)
            return;
        bool buy = p[0] == 0x3B;
        uint vendor = U32(p, 3);
        int at = buy ? 8 : 9, step = buy ? 7 : 6;
        int vx = 0, vy = 0;
        string vname = "";
        foreach (AtlasNpc n in Atlas.Visible())
            if (n.Serial == vendor)
            {
                vx = n.X;
                vy = n.Y;
                vname = n.Name;
            }
        int handThread = 0;
        for (; at + step <= p.Length; at += step)
        {
            uint s = buy ? U32(p, at + 1) : U32(p, at);
            int amount = buy ? (p[at + 5] << 8) | p[at + 6] : (p[at + 4] << 8) | p[at + 5];
            object[]? o;
            m_offers.TryGet(s, out o);
            if (buy && byHand)
            {
                AssistItem? ti = Item(k, s);
                if (ti != null && IsThreadArt(ti.Graphic))
                    handThread += amount;
            }
            AtlasTrade t = new()
            {
                At = Clock.UtcNow.ToLocalTime(),
                Kind = buy ? "buy" : "sell",
                Vendor = vname,
                VendorSerial = vendor,
                X = vx,
                Y = vy,
                Item = o != null ? (string)o[0] : "item " + s.ToString("X"),
                Amount = amount,
                Price = o != null ? (int)o[1] : 0
            };
            // A sale counts when the server deletes the item (uo1998 23:07:32: Clement answered a six-item sell with
            // speech and took nothing; the five-item sell at 23:07:36 deleted each one).
            if (buy && byHand)
            {
                lock (m_pendingBuysGate)
                {
                    List<AtlasTrade>? l;
                    if (!m_pendingBuys.TryGet(vendor, out l)
                        || (Clock.UtcNow.ToLocalTime() - l[0].At).TotalMilliseconds > SaleConfirmMs)
                        m_pendingBuys = m_pendingBuys.Set(vendor, l = new List<AtlasTrade>());
                    l.Add(t);
                }
            }
            else if (buy)
            {
                Atlas.Trade(t);
            }
            else
            {
                lock (m_pendingSalesGate)
                    m_pendingSales = m_pendingSales.Set(s, t);
                if (byHand)
                {
                    AssistItem? it = Item(k, s);
                    int art = it != null ? it.Graphic : o != null && o.Length > 3 ? (int)o[3] : -1;
                    lock (m_handSalesGate)
                    {
                        if (m_handSales.Count > 2000)
                            m_handSales = m_handSales.Clear();
                        m_handSales = m_handSales.Set(s, art);
                    }
                }
            }
            // The art from the sell list when the pack is unseen: right after a login, Watch me sell found no art for
            // Brian's shirts and did nothing (2026-10-20 12:09:42).
        }
        if (buy && byHand)
            lock (m_pendingBuysGate)
                m_pendingThread = m_pendingThread.Set(vendor, new object[] { vname, handThread });
    }
    // A buy by hand counts once the server closes the vendor's buy window (0x3B: length 8, the vendor's serial), as a
    // sale counts once its item is deleted. A buy the server leaves unanswered bought nothing: Kimmie's 109 thread at
    // 2026-10-21 20:46:45 and Brian's 91 at 19:51:33 were each recorded twice, the first buy unanswered and the same
    // stock offered again (28 of 30 hand buys that night were answered within 3 s).
    Map<uint, List<AtlasTrade>> m_pendingBuys = Map<uint, List<AtlasTrade>>.Empty;
    Map<uint, object[]> m_pendingThread = Map<uint, object[]>.Empty; readonly Lock m_pendingBuysGate = new();
    void BuyClosed(byte[] p)
    {
        if (p[0] != 0x3B || p.Length < 8 || Atlas == null)
            return;
        uint vendor = U32(p, 3);
        List<AtlasTrade>? l;
        object[]? th;
        lock (m_pendingBuysGate)
        {
            if (!m_pendingBuys.TryGet(vendor, out l))
                return;
            m_pendingBuys = m_pendingBuys.Remove(vendor);
            if (m_pendingThread.TryGet(vendor, out th))
                m_pendingThread = m_pendingThread.Remove(vendor);
        }
        if ((Clock.UtcNow.ToLocalTime() - l[0].At).TotalMilliseconds > SaleConfirmMs)
            return;
        foreach (AtlasTrade t in l)
            Atlas.Trade(t);
        // A thread buy by hand is the vendor bought out in the circuit's record, as the circuit's own buy-out (Damian,
        // 2026-10-20: "didn't it see that we just bought from stanley"; the circuit asked Stanley a minute after his
        // 64).
        int handThread = th != null ? (int)th[1] : 0;
        if (handThread > 0)
        {
            CircuitStock(vendor, (string)th![0], handThread, 0);
            Attempt(vendor, "buy", "by hand: bought " + handThread);
        }
    }
    int SaleConfirmMs => Ms("sale-confirm");
    Map<uint, AtlasTrade> m_pendingSales = Map<uint, AtlasTrade>.Empty; readonly Lock m_pendingSalesGate = new();
    void SaleDeleted(uint s)
    {
        AtlasTrade? t;
        lock (m_pendingSalesGate)
        {
            if (!m_pendingSales.TryGet(s, out t))
                return;
            m_pendingSales = m_pendingSales.Remove(s);
        }
        if ((Clock.UtcNow.ToLocalTime() - t.At).TotalMilliseconds > SaleConfirmMs || Atlas == null)
            return;
        Atlas.Trade(t);
        int art = -1;
        bool byHand;
        lock (m_handSalesGate)
        {
            byHand = m_handSales.TryGet(s, out art);
            m_handSales = m_handSales.Remove(s);
        }
        if (byHand)
            WatchSaw(t, art);
        if (Smith != null && !t.Item.StartsWith("item "))
        {
            string? q = Smith.QualityOf(s);
            if (q != null || Smith.CostOf(t.Item) > 0)
                Smith.Sold(t.Vendor, t.Item, q ?? "unknown", t.Amount, t.Price);
        }
        if (Tailor != null && !t.Item.StartsWith("item "))
        {
            string? q = Tailor.QualityOf(s);
            if (q != null || Tailor.CostOf(t.Item) > 0)
                Tailor.Sold(t.Vendor, t.Item, q ?? "unknown", t.Amount, t.Price);
        }
        if (!t.Item.StartsWith("item "))
            foreach (CraftKind c in Crafts)
                if (c.Book != null && c.Book.CostOf(t.Item) > 0)
                    c.Book.Sold(t.Vendor, t.Item, "unknown", t.Amount, t.Price);
    }
    // Names for the atlas: a single click (0x09) on each new unnamed human within LookReach, at most one per LookMs, as
    // a player clicking would; the server answers with the name label. Off with the "look" setting.
    public const int LookReach = 14;
    int LookMs => Ms("look");
    Set<uint> m_looked = Set<uint>.Empty;
    Map<uint, int[]> m_lookCandidates = Map<uint, int[]>.Empty; readonly Lock m_lookCandidatesGate = new();
    void LookLoop()
    {
        // Ends on shutdown as the other loops do: a test's features left it sleeping on a virtual clock no actor held,
        // every sleep returned at once, and each test left a core spinning (2026-10-22: 17 of 20 by the collect cases).
        while (!m_shutdown)
        {
            Clock.Sleep(LookMs);
            GameLink k = m_link;
            if (k == null || k.Closed || k.Halted != null || Atlas == null)
                continue;
            if (AssistGuard.Current != null && AssistGuard.Current.Disabled != null)
                continue;
            // What lies in view is kept whether or not the map is drawn: the resource piles and the server's
            // decoration.
            lock (m_seenGate)
                SeenSweep();
            DecorSweep(k);
            int px, py;
            lock (k.State.Gate)
            {
                px = k.State.X;
                py = k.State.Y;
            }
            uint pick = 0;
            lock (m_lookCandidatesGate)
            {
                foreach (KeyValuePair<uint, int[]> kv in m_lookCandidates)
                    if (!m_looked.Contains(kv.Key)
                        && Math.Max(Math.Abs(kv.Value[0] - px), Math.Abs(kv.Value[1] - py)) <= LookReach
                        && Atlas.NeedsName(kv.Key))
                    {
                        pick = kv.Key;
                        break;
                    }
                if (pick != 0)
                {
                    m_looked = m_looked.Add(pick);
                    m_lookCandidates = m_lookCandidates.Remove(pick);
                    if (m_looked.Count > 10000)
                        m_looked = m_looked.Clear();
                }
            }
            if (pick == 0)
                lock (m_lookItemsGate)
                    foreach (KeyValuePair<uint, int[]> kv in m_lookItems)
                        if (!m_looked.Contains(kv.Key)
                            && Math.Max(Math.Abs(kv.Value[0] - px), Math.Abs(kv.Value[1] - py)) <= LookReach)
                        {
                            pick = kv.Key;
                            m_looked = m_looked.Add(pick);
                            break;
                        }
            // On a trip, every creature in reach is clicked once for its name.
            if (pick == 0 && m_trip != null)
            {
                lock (m_trip)
                {
                    foreach (TripMob m in m_trip.Mobs.Values)
                    {
                        string? n;
                        lock (k.State.Gate)
                            k.State.Names.TryGet(m.Serial, out n);
                        if (m.Person || !string.IsNullOrEmpty(n) || m_looked.Contains(m.Serial)
                            || Math.Max(Math.Abs(m.X - px), Math.Abs(m.Y - py)) > LookReach)
                            continue;
                        pick = m.Serial;
                        m_looked = m_looked.Add(pick);
                        break;
                    }
                }
            }

            if (pick == 0)
                continue;
            using (ActionScope? scope = TryAct(k))
                if (scope != null)
                    k.Look(pick);
        }
    }
    void SeenPerson(GameLink k, uint s, int body, int x, int y, int z, int noto)
    {
        uint me;
        string? known;
        lock (k.State.Gate)
        {
            me = k.State.Player;
            if (!k.State.Names.TryGet(s, out known))
                known = "";
        }
        if (s == me)
            return;
        if (Atlas != null)
        {
            Atlas.Seen(s, body, x, y, known, Criminal(noto));
            if (Atlas.NeedsName(s))
                lock (m_lookCandidatesGate)
                {
                    if (m_lookCandidates.Count > 2000)
                        m_lookCandidates = m_lookCandidates.Clear();
                    m_lookCandidates = m_lookCandidates.Set(s, new[] { x, y });
                }
        }
        if (body < 0x190 || body > 0x193)
        {
            SeenCreature(s, body, x, y);
            return;
        }
        lock (m_peopleGate)
        {
            Person? p;
            DateTime now = Clock.UtcNow.ToLocalTime();
            if (!m_people.TryGet(s, out p))
            {
                if (m_people.Count >= PeopleKeep)
                    return;
                m_people = m_people.Set(s, p = new Person { Serial = s, First = now });
            }
            p.Body = body;
            p.X = x;
            p.Y = y;
            p.Z = z;
            p.Last = now;
            if (!p.NotoKnown)
            {
                p.Noto = noto;
                p.NotoKnown = true;
            }
            else if (noto != p.Noto)
            {
                Act(p, "notoriety " + Notoriety(p.Noto) + " to " + Notoriety(noto));
                p.Noto = noto;
            }
            m_peopleChanged = true;
        }
    }
    // uo1998 sends the 1998 server's raw notoriety, a signed karma-like value -127 to 127 (OUO mobile.c
    // CMobile_SetNotoriety clamps it there; NotoValueToLevel groups it by 24): Kwik 0x01, an orc 0x81 (-127). The UOAIX
    // shard sends the 1.25 highlight value 1 to 7.
    bool RawNotoriety => Server.IndexOf("uo1998", StringComparison.OrdinalIgnoreCase) >= 0;
    string Notoriety(int n)
    {
        if (RawNotoriety)
            return n.ToString("+0;-0;0") + " (level " + (n < 0 ? "-" : "") + (Math.Abs(n) / 24)
                + (n < 0 ? ", negative: the criminal side" : "") + ")";
        return n >= 0 && n < s_notorieties.Length ? s_notorieties[n] : "notoriety " + n;
    }
    bool Criminal(int n)
    {
        return RawNotoriety ? n < 0 : n == 4 || n == 6;
    }
    // The notoriety byte as a 0x77 or 0x78 carries it, said for the Session card: uo1998's read signed (Kwik's +4 read
    // "criminal (grey)" by the 1.25 values while he showed blue, 2026-10-24).
    public string NotorietyByte(int b)
    {
        return Notoriety(RawNotoriety ? (sbyte)b : b);
    }
    // Called with people locked. The same activity again within a minute counts as a repeat, not a new line.
    void Act(Person p, string what)
    {
        DateTime now = Clock.UtcNow.ToLocalTime();
        if (what == p.LastAct && p.Acts.Count > 0 && (now - p.Last).TotalSeconds < 60)
        {
            p.Repeats++;
            p.Acts[p.Acts.Count - 1] = p.Acts[p.Acts.Count - 1].Split(new[] { " (x" }, StringSplitOptions.None)[0]
                + " (x" + (p.Repeats + 1) + ")";
        }
        else
        {
            p.Acts.Add(now.ToString("HH:mm:ss") + " at " + p.X + "," + p.Y + ": " + what);
            p.LastAct = what;
            p.Repeats = 0;
            if (p.Acts.Count > ActsKeep)
                p.Acts.RemoveAt(0);
        }
        p.Last = now;
        m_peopleChanged = true;
    }
    void ActOn(uint s, string what)
    {
        lock (m_peopleGate)
        {
            Person? p;
            if (m_people.TryGet(s, out p))
                Act(p, what);
        }
    }
    void People(GameLink k, byte[] p)
    {
        switch (p[0])
        {
            case 0x78:
                if (p.Length >= 19)
                {
                    uint m = U32(p, 3) & 0x7FFFFFFF;
                    SeenPerson(k, m, (p[7] << 8) | p[8], (p[9] << 8) | p[10], (p[11] << 8) | p[12], (sbyte)p[13],
                        (sbyte)p[18]);
                    // Worn items: serial, graphic (hue follows when 0x8000), layer, to a zero serial; a vendor's stock
                    // pack (layer 0x1A) leads a buy list back to its vendor (uo1998: Marlene 00280EC0 wears 40280EC1).
                    // uo1998's click label is the bare name ("Marlene", no title), so a worn shop pack (layers
                    // 0x1A-0x1C, which no player wears) is what types a vendor there.
                    bool shop = false;
                    lock (m_wornByGate)
                    {
                        if (m_wornBy.Count > 20000)
                            m_wornBy = m_wornBy.Clear();
                        for (int at = 19; at + 4 <= p.Length;)
                        {
                            uint s = U32(p, at);
                            if (s == 0 || at + 7 > p.Length)
                                break;
                            int g = (p[at + 4] << 8) | p[at + 5];
                            if (p[at + 6] >= 0x1A && p[at + 6] <= 0x1C)
                                shop = true;
                            m_wornBy = m_wornBy.Set(s & 0x7FFFFFFF, m);
                            at += 7 + ((g & 0x8000) != 0 ? 2 : 0);
                        }
                    }
                    if (shop && Atlas != null)
                        Atlas.ShopPack(m);
                }
                break;
            case 0x1D:
                if (p.Length >= 5)
                    UoMap.DropMulti(U32(p, 1));
                break;
            case 0x77:
                if (p.Length >= 17)
                    SeenPerson(k, U32(p, 1), (p[5] << 8) | p[6], (p[7] << 8) | p[8], (p[9] << 8) | p[10], (sbyte)p[11],
                        (sbyte)p[16]);
                break;
            case 0x2F:
                if (p.Length >= 10)
                {
                    uint a = U32(p, 2), d = U32(p, 6);
                    ActOn(a, "fighting " + MobileName(k, d));
                    ActOn(d, "fought by " + MobileName(k, a));
                }
                break;
            case 0x6E:
                if (p.Length >= 7)
                {
                    string? w = Action((p[5] << 8) | p[6]);
                    if (w != null)
                        ActOn(U32(p, 1), w);
                }
                break;
            case 0xAF:
                if (p.Length >= 5)
                {
                    ActOn(U32(p, 1), "died");
                    lock (m_creaturesGate)
                    {
                        Creature? c;
                        if (m_creatures.TryGet(U32(p, 1), out c))
                        {
                            c.Died = true;
                            m_peopleChanged = true;
                        }
                    }
                }
                break;
            case 0x11:
                if (Atlas != null && p.Length >= 37)
                    Atlas.Named(U32(p, 3), Encoding.ASCII.GetString(p, 7, 30).TrimEnd('\0').Trim());
                break;
            // The paperdoll's line: serial, 60 bytes of name and title. uo1998's labels and status name a banker bare
            // ("Danyelle"); its paperdoll says "Danyelle the banker" (Damian, 2026-10-22, Magincia, 793BE).
            case 0x88:
                if (Atlas != null && p.Length >= 65)
                    Atlas.Named(U32(p, 1) & 0x7FFFFFFF, Encoding.ASCII.GetString(p, 5, 60).TrimEnd('\0').Trim());
                break;
            // A world item: serial (amount follows when its top bit is set), graphic (stack byte follows when 0x8000),
            // x, y.
            case 0x1A:
                if (Atlas != null && p.Length >= 13)
                {
                    uint s = U32(p, 3);
                    int at = 7;
                    if ((s & 0x80000000) != 0)
                        at += 2;
                    s &= 0x7FFFFFFF;
                    int g = (p[at] << 8) | p[at + 1];
                    at += 2;
                    if ((g & 0x8000) != 0)
                    {
                        g = ((g & 0x7FFF) + (at < p.Length ? p[at] : 0)) & 0x7FFF;
                        at++;
                    }
                    g &= 0x7FFF;
                    if (at + 4 > p.Length)
                        break;
                    int x = ((p[at] << 8) | p[at + 1]) & 0x7FFF, y = ((p[at + 2] << 8) | p[at + 3]) & 0x3FFF;
                    // A house's walls and floors stand on the map from here (UoMap.PutMulti): the z follows x and y,
                    // after the facing byte when x's 0x8000 is set.
                    int zAt = at + 4 + ((p[at] & 0x80) != 0 ? 1 : 0);
                    // Multis under 0x40 are boats (the 1998 multi table's ships), the rest houses and tents.
                    if ((g & 0x4000) != 0)
                    {
                        Atlas.Place(s, (g & 0x3FFF) < 0x40 ? "Boat" : "House", g, x, y);
                        if (zAt < p.Length)
                            UoMap.PutMulti(s, g & 0x3FFF, x, y, (sbyte)p[zAt]);
                    }
                    else if (IsAnvil(g) || IsSmithForge(g))
                    {
                        Atlas.Place(s, IsAnvil(g) ? "Anvil" : "Forge", g, x, y);
                    }
                    else if ((g == GuildstoneArt || (g >= HouseSignArt0 && g <= HouseSignArt1)) && !Atlas.HasPlace(s))
                    {
                        lock (m_lookItemsGate)
                        {
                            if (m_lookItems.Count > 500)
                                m_lookItems = m_lookItems.Clear();
                            m_lookItems = m_lookItems.Set(s, new[] { x, y, g });
                        }
                    }
                }
                break;
            case 0x24:
                if (p.Length >= 5)
                    m_lastOpened = U32(p, 1);
                break;
            // A container's contents in order (serial, graphic, a byte, amount, x, y, container, hue: 19 bytes each);
            // the buy list names the same contents in the same order (OUO MakePacket_SHOP_DATA walks
            // container->contents).
            case 0x3C:
                if (p.Length >= 5)
                {
                    int n = (p[3] << 8) | p[4];
                    List<uint> order = new();
                    List<int> amounts = new();
                    uint box = 0;
                    for (int i = 0; i < n && 5 + i * 19 + 19 <= p.Length; i++)
                    {
                        int at = 5 + i * 19;
                        order.Add(U32(p, at));
                        amounts.Add((p[at + 7] << 8) | p[at + 8]);
                        box = U32(p, at + 13);
                    }
                    if (n == 0)
                        box = m_lastOpened;
                    m_lastOpened = 0;
                    if (box != 0)
                        lock (m_contentsGate)
                        {
                            if (m_contents.Count > 200)
                            {
                                m_contents = m_contents.Clear();
                                m_contentAmounts = m_contentAmounts.Clear();
                            }
                            m_contents = m_contents.Set(box, order);
                            m_contentAmounts = m_contentAmounts.Set(box, amounts);
                        }
                }
                break;
            // The sell list: vendor, count, then serial, graphic, hue, amount, price (u16 each after the serial), name
            // length (u16) and name (OUO MakePacket_SHOP_SELL).
            case 0x9E:
                if (p.Length >= 9)
                {
                    uint vendor = U32(p, 3);
                    int n = (p[7] << 8) | p[8], at = 9;
                    lock (m_offersGate)
                    {
                        m_offers = m_offers.Clear();
                        for (int i = 0; i < n && at + 14 <= p.Length; i++)
                        {
                            uint s = U32(p, at);
                            int price = (p[at + 10] << 8) | p[at + 11], len = (p[at + 12] << 8) | p[at + 13];
                            if (at + 14 + len > p.Length)
                                break;
                            m_offers = m_offers.Set(s, new object[] {
                                Encoding.ASCII.GetString(p, at + 14, len).TrimEnd('\0'), price, vendor,
                                (p[at + 4] << 8) | p[at + 5] });
                            at += 14 + len;
                        }
                    }
                }
                break;
            // The buy list (OUO packet_manager.c MakePacket_SHOP_DATA): stock container, count, then price (u32), name
            // length (u8, with its NUL) and name per item. The container is worn by the vendor (layer 0x1A).
            case 0x74:
                if (Atlas != null && p.Length >= 8 && p[7] > 0)
                {
                    uint box = U32(p, 3);
                    int count = p[7], at = 8;
                    List<string> items = new();
                    // The quantity is the stock container's amount for the item (0x3C, sent just before), whose order
                    // the list reverses (StockIndex).
                    List<int>? amounts;
                    m_contentAmounts.TryGet(box, out amounts);
                    for (int i = 0; i < count && at + 5 <= p.Length; i++)
                    {
                        uint price = U32(p, at);
                        int len = p[at + 4];
                        if (at + 5 + len > p.Length)
                            break;
                        int si = StockIndex(i, count, amounts != null ? amounts.Count : 0);
                        items.Add(Encoding.ASCII.GetString(p, at + 5, len).TrimEnd('\0') + ": " + price + " gp"
                            + (amounts != null && si >= 0 && si < amounts.Count ? " x " + amounts[si] : ""));
                        at += 5 + len;
                    }
                    uint vendor = 0;
                    m_wornBy.TryGet(box, out vendor);
                    if (vendor == 0)
                        lock (k.State.Gate)
                        {
                            AssistItem? it;
                            if (k.State.Items.TryGet(box, out it))
                                vendor = it.Mobile != 0 ? it.Mobile : it.Container;
                        }
                    if (vendor != 0)
                        Atlas.Stock(vendor, items);
                    // Prices by serial for the buy that may follow.
                    List<uint>? order;
                    m_contents.TryGet(box, out order);
                    lock (m_offersGate)
                    {
                        DateTime now = Clock.UtcNow.ToLocalTime();
                        if ((now - m_offersAt).TotalMilliseconds >= AssistAtlas.StockJoinMs)
                            m_offers = m_offers.Clear();
                        m_offersAt = now;
                        at = 8;
                        for (int i = 0; i < count && at + 5 <= p.Length; i++)
                        {
                            uint price = U32(p, at);
                            int len = p[at + 4];
                            if (at + 5 + len > p.Length)
                                break;
                            int si = StockIndex(i, count, order != null ? order.Count : 0);
                            if (order != null && si >= 0 && si < order.Count)
                                m_offers = m_offers.Set(order[si], new object[] { Encoding.ASCII.GetString(p,
                                    at + 5, len).TrimEnd('\0'), (int)price, vendor });
                            at += 5 + len;
                        }
                    }
                    // A thread vendor asked by hand goes in the record whatever it showed (Damian, 2026-10-21: "i do
                    // say vendor buy every time", "you should be logging all those"): a visit that found it empty
                    // bounds its restock. A vendor sends a list per stock box, so a "none" can be followed by its
                    // "saw".
                    if (vendor != 0 && Clock.UtcNow > m_shopClaimUntil
                        && CircuitVendors().Exists(c => c.Serial == vendor))
                    {
                        int thread = 0;
                        if (order != null)
                        {
                            foreach (uint s in order)
                            {
                                AssistItem? it = Item(k, s);
                                if (it != null && IsThreadArt(it.Graphic))
                                    thread += Math.Max(1, it.Amount);
                            }
                        }

                        Attempt(vendor, "buy", thread > 0 ? "by hand: saw " + thread : "by hand: none");
                    }
                }
                break;
            case 0x1C:
                // A label (type 6, the answer to a single click) carries the name as its text; speech names its
                // speaker.
                if (Atlas != null && p.Length > 44 && U32(p, 3) != 0xFFFFFFFF)
                {
                    uint s = U32(p, 3);
                    string label = (p[9] == 6 ? Encoding.ASCII.GetString(p, 44, p.Length - 44)
                        : Encoding.ASCII.GetString(p, 14, 30)).TrimEnd('\0').Trim();
                    // Speech names its speaker without the title ("Marlene" for "Marlene the armourer"): it never
                    // replaces a name.
                    if (p[9] == 6)
                        Atlas.Named(s, label);
                    else
                        Atlas.NamedIfNone(s, label);
                    int[]? item;
                    lock (m_lookItemsGate)
                    {
                        if (m_lookItems.TryGet(s, out item))
                            m_lookItems = m_lookItems.Remove(s);
                    }
                    if (item != null && p[9] == 6)
                    {
                        if (item[2] == GuildstoneArt && label.IndexOf("guild", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            Atlas.Place(s, "Guildstone", item[2], item[0], item[1]);
                            Atlas.PlaceName(s, label);
                        }
                        else if (item[2] != GuildstoneArt)
                        {
                            uint house = Atlas.HouseNear(item[0], item[1], 16);
                            if (house != 0)
                                Atlas.PlaceName(house, label);
                        }
                    }
                }
                // A system line about a house's state while the player stands by one is kept as that house's details.
                if (Atlas != null && p.Length > 44 && U32(p, 3) == 0xFFFFFFFF)
                {
                    string text = Encoding.ASCII.GetString(p, 44, p.Length - 44).TrimEnd('\0');
                    bool about = false;
                    foreach (string w in s_houseWords)
                        if (text.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0)
                            about = true;
                    if (about)
                    {
                        int px, py;
                        lock (k.State.Gate)
                        {
                            px = k.State.X;
                            py = k.State.Y;
                        }
                        uint house = Atlas.HouseNear(px, py, 16);
                        if (house != 0)
                            Atlas.PlaceDetail(house, text);
                    }
                }
                if (p.Length > 44 && (p[9] == 0 || p[9] == 2 || p[9] == 8 || p[9] == 9))
                {
                    string text = Encoding.ASCII.GetString(p, 44, p.Length - 44).TrimEnd('\0');
                    ActOn(U32(p, 3),
                        (p[9] == 2 ? "emoted " : p[9] == 8 ? "whispered " : p[9] == 9 ? "yelled " : "said ") + "\""
                        + text + "\"");
                }
                break;
        }
    }
    // The player's corpses (Damian, 2026-10-15: "and if you die, a marker for your corpse in the map"): the server's
    // 0x2C with flag 0 (OUO packet_manager.c MakePacket_DEATH, the death screen) marks the player's tile at that
    // moment.
    public sealed class Corpse
    {
        public int X, Y; public DateTime At;
    }
    public const int CorpsesKeep = 5;
    readonly List<Corpse> m_corpses = new();
    void PlayerDied(GameLink k)
    {
        int x, y;
        lock (k.State.Gate)
        {
            x = k.State.X;
            y = k.State.Y;
        }
        lock (m_corpses)
        {
            m_corpses.Add(new Corpse { X = x, Y = y, At = Clock.UtcNow.ToLocalTime() });
            if (m_corpses.Count > CorpsesKeep)
                m_corpses.RemoveAt(0);
        }
        k.Tell("Your corpse lies at " + x + "," + y + " (marked on the assist's map).", 0x0026);
        Note("Died at " + x + "," + y);
        // Nothing automatic runs while dead (Damian, 2026-10-15: "be sure none of these auto actions or loops run while
        // dead"): the running loop and the mill, spin and weave run stop; GameLink refuses the assist's own packets.
        idleStop = true;
        m_loops.Stop();
        m_weaveRunner.Stop();
        PlotHealers(k, x, y);
    }
    // At death, the way to a healer (Damian, 2026-10-15: "mark the corpse and then plot a course for the nearest known
    // healer and last seen wandering healer"): the atlas's nearest healer who is not a wanderer and its most recently
    // seen wandering healer, told in the journal and drawn on the map from the player while the player is dead.
    // Straight lines; the atlas holds no paths.
    public sealed class Waypoint
    {
        public int X, Y; public string Label = null!;
    }
    readonly List<Waypoint> m_course = new();
    public volatile bool ShowMapNow;
    static readonly ImmutableArray<string> s_compass = [ "north", "northeast", "east", "southeast", "south",
        "southwest", "west",
        "northwest" ];
    public static string Heading(int dx, int dy)
    {
        if (dx == 0 && dy == 0)
            return "here";
        double a = Math.Atan2(dx, -dy) * 180 / Math.PI;
        if (a < 0)
            a += 360;
        return s_compass[(int)((a + 22.5) / 45) % 8];
    }
    void PlotHealers(GameLink k, int x, int y)
    {
        AtlasNpc? near = null, wander = null;
        int nd = int.MaxValue;
        if (Atlas != null)
        {
            foreach (AtlasNpc n in Atlas.Visible())
            {
                if (n.Type != "Healer")
                    continue;
                if (n.Sub.Contains("wandering"))
                {
                    if (wander == null || n.Last > wander.Last)
                        wander = n;
                    continue;
                }
                int d = Math.Max(Math.Abs(n.X - x), Math.Abs(n.Y - y));
                if (d < nd)
                {
                    nd = d;
                    near = n;
                }
            }
        }

        lock (m_course)
        {
            m_course.Clear();
            if (near != null)
                m_course.Add(new Waypoint { X = near.X, Y = near.Y, Label = "healer " + near.Name });
            if (wander != null)
                m_course.Add(new Waypoint { X = wander.X, Y = wander.Y,
                    Label = "wandering healer " + wander.Name + " (seen " + wander.Last.ToString("HH:mm") + ")" });
        }
        if (near == null && wander == null)
        {
            k.Tell("No healer is known yet: the assist learns healers as you see them.", 0x002B);
            return;
        }
        foreach (AtlasNpc? n in new[] { near, wander })
        {
            if (n == null)
                continue;
            int d = Math.Max(Math.Abs(n.X - x), Math.Abs(n.Y - y));
            k.Tell((n == wander ? "Last seen wandering healer " : "Nearest healer ") + n.Name + ": " + d + " tiles "
                + Heading(n.X - x, n.Y - y) + ", at " + n.X + "," + n.Y
                + (n == wander ? " (seen " + n.Last.ToString("HH:mm") + ")" : "") + ".", 0x0044);
        }
        ShowMapNow = true;
    }
    // The map's search (Damian, 2026-10-18: "in map add search box for name and type of shop or feature, that shows
    // shortest path, and a walk button there, and a run button too"): every shop of the region file by its name and
    // kind, every bank, every place the atlas keeps (anvils, forges, looms, ...), and every named shopkeeper, banker
    // and healer the atlas knows, matched by any word of the search in the text, nearest first.
    public sealed class MapPlace
    {
        public string Name = null!, Kind = null!; public int X, Y; public override string ToString()
        {
            return Name + " (" + Kind + ")";
        }
    }
    public const int MapSearchMax = 60;
    public List<MapPlace> MapSearch(string q)
    {
        List<MapPlace> l = new();
        GameLink k = m_link;
        q = (q ?? "").Trim().ToLowerInvariant();
        if (q.Length == 0)
            return l;
        string[] words = q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        Action<string, string, int, int> add = (name, kind, x,
            y) => { string t = (name + " " + kind).ToLowerInvariant(); if (Array.TrueForAll(words, w => t.Contains(w))
            && !l.Exists(p => p.X == x && p.Y == y && p.Name == name)) l.Add(new MapPlace { Name = name, Kind = kind,
            X = x, Y = y }); };
        foreach (string c in UoRegions.Cities())
        {
            foreach (UoRegions.Shop s in UoRegions.AllShopsIn(c))
                add(s.Text, (UoRegions.ShopKind(s.CX, s.CY) ?? "shop") + " in " + c, s.CX, s.CY);
            foreach (int[] b in UoRegions.BanksIn(c, 0, 0))
                add("Bank of " + c, "bank", b[0], b[1]);
        }
        if (Atlas != null)
        {
            foreach (AtlasPlace p in Atlas.Places())
                add(p.Name.Length > 0 ? p.Name : p.Kind,
                    p.Kind.ToLowerInvariant()
                    + (UoRegions.City(p.X, p.Y) != null ? " in " + UoRegions.City(p.X, p.Y) : ""), p.X, p.Y);
            foreach (AtlasNpc n in Atlas.Shops())
                if (n.Name.Length > 0)
                    add(n.Name,
                        (n.Type.Length > 0 ? n.Type.ToLowerInvariant() : "npc")
                        + (UoRegions.City(n.X, n.Y) != null ? " in " + UoRegions.City(n.X, n.Y) : ""), n.X, n.Y);
        }
        int px = 0, py = 0;
        if (k != null)
            lock (k.State.Gate)
            {
                px = k.State.X;
                py = k.State.Y;
            }
        l.Sort((a, b) => Tiles(a.X, a.Y, px, py).CompareTo(Tiles(b.X, b.Y, px, py)));
        if (l.Count > MapSearchMax)
            l.RemoveRange(MapSearchMax, l.Count - MapSearchMax);
        return l;
    }
    // The route shown on the map: the tiles of the shortest walk from the player to the place picked, to the first
    // teleporter on it when there is one (the walk plans again from the landing).
    volatile int[][]? m_routeShown = null; volatile MapPlace? m_routeTo = null;
    public int[][] RouteShown()
    {
        return m_routeShown ?? new int[0][];
    }
    public MapPlace? RouteTarget => m_routeTo;
    public string? ShowRoute(MapPlace p)
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "not connected";
        if (p == null)
        {
            m_routeShown = null;
            m_routeTo = null;
            return null;
        }
        if (m_mapWalk == null || !m_mapWalk.IsAlive)
            MapWalkStatus = "";
        int x, y, z;
        lock (k.State.Gate)
        {
            x = k.State.X;
            y = k.State.Y;
            z = k.State.Z;
        }
        List<int>? dirs = PlanPath(k, x, y, z, p.X, p.Y, 1, Set<long>.Empty);
        m_routeTo = p;
        if (dirs == null)
        {
            m_routeShown = new[] { new[] { x, y }, new[] { p.X, p.Y } };
            return "no way found from " + x + "," + y + " to " + p.Name + " (" + Tiles(x, y, p.X, p.Y)
                + " tiles); the line is straight";
        }
        List<int[]> tiles = new()
        { new[] { x, y } };
        int cx = x, cy = y;
        foreach (int d in dirs)
        {
            cx += s_dX[d];
            cy += s_dY[d];
            tiles.Add(new[] { cx, cy });
        }
        bool tele = Tiles(cx, cy, p.X, p.Y) > 1;
        if (tele)
            tiles.Add(new[] { p.X, p.Y });
        m_routeShown = tiles.ToArray();
        return dirs.Count + " steps to " + p.Name
            + (tele ? ", to the teleporter at " + cx + "," + cy + " and on from its landing" : "");
    }
    // The walk to the place picked, walking or running, off the window's thread; a step of the player's own stops it.
    volatile Thread m_mapWalk = null!; public volatile string MapWalkStatus = "";
    public string? MapGo(bool run)
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "not connected";
        MapPlace? p = m_routeTo;
        if (p == null)
            return "pick a place first";
        if (CircuitRunning)
            return "a circuit is running";
        if (m_mapWalk != null && m_mapWalk.IsAlive)
            return "a walk is running";
        ManualStart();
        m_mapWalk = NewThread(() =>
        {
            string was = Pace;
            Pace = run ? "run" : "walk";
            MapWalkStatus = (run ? "Running" : "Walking") + " to " + p.Name;
            try
            {
                string? w = WalkTo(p.X, p.Y, 1);
                MapWalkStatus = w == null ? "At " + p.Name : p.Name + ": " + w;
            }
            finally { Pace = was; ShowRoute(null!); }
        });
        m_mapWalk.Start();
        return null;
    }
    public void MapStop()
    {
        Thread t = m_mapWalk;
        if (t != null && t.IsAlive)
        {
            circuitStop = true;
            MapWalkStatus = "Stopped";
        }
    }
    // The course while the player is dead; nothing once alive again.
    public Waypoint[] Course()
    {
        GameLink k = m_link;
        if (k == null || !k.State.Dead)
        {
            lock (m_course)
                m_course.Clear();
            return new Waypoint[0];
        }
        lock (m_course)
            return m_course.ToArray();
    }
    public Corpse[] Corpses()
    {
        lock (m_corpses)
            return m_corpses.ToArray();
    }
    public void ClearCorpses()
    {
        lock (m_corpses)
            m_corpses.Clear();
    }
    // For the map: x, y and 1 when on the criminal side, of every person seen in the last PeopleOnMapMs.
    public const int PeopleOnMapMs = 60000;
    public int[][] PeoplePositions()
    {
        List<int[]> list = new();
        DateTime now = Clock.UtcNow.ToLocalTime();
        lock (m_peopleGate)
            foreach (Person p in m_people.Values)
                if ((now - p.Last).TotalMilliseconds < PeopleOnMapMs)
                    list.Add(new[] { p.X, p.Y, Criminal(p.Noto) ? 1 : 0 });
        return list.ToArray();
    }
    void WritePeopleReport()
    {
        GameLink k = m_link;
        if (k == null || !m_peopleChanged || PeopleReportPath.Length == 0)
            return;
        StringBuilder sb = new();
        sb.AppendLine("# People seen, " + Server.TrimEnd('.') + ", session from "
            + Clock.UtcNow.ToLocalTime().ToString("yyyy-MM-dd"));
        sb.AppendLine();
        sb.AppendLine("Human-bodied mobiles the server showed this client (players and human NPCs alike; the 1.25 "
            + "wire does not tell them apart).");
        lock (m_peopleGate)
        {
            m_peopleChanged = false;
            List<Person> list = new(m_people.Values);
            list.Sort((a, b) => b.Last.CompareTo(a.Last));
            foreach (Person p in list)
            {
                string? name;
                lock (k.State.Gate)
                {
                    if (!k.State.Names.TryGet(p.Serial, out name))
                        name = "";
                }
                if (name.Length > 0)
                    p.Name = name;
                sb.AppendLine();
                sb.AppendLine("## " + (p.Name.Length > 0 ? p.Name : "unnamed") + " (" + p.Serial.ToString("X")
                    + (p.Body >= 0x192 ? ", ghost" : "") + ")");
                sb.AppendLine("- seen " + p.First.ToString("HH:mm:ss") + " to " + p.Last.ToString("HH:mm:ss")
                    + ", last at " + p.X + "," + p.Y + "," + p.Z);
                sb.AppendLine("- notoriety: " + Notoriety(p.Noto) + (Criminal(p.Noto) ? " (CRIMINAL)" : ""));
                foreach (string a in p.Acts)
                    sb.AppendLine("- " + a);
            }
        }
        // Creatures by kind, most seen first; each one's place where last seen (and first, when it moved), newest
        // first.
        Map<string, List<Creature>> kinds = Map<string, List<Creature>>.Empty;
        lock (m_creaturesGate)
        {
            foreach (Creature c in m_creatures.Values)
            {
                string name = MobileName(k, c.Serial);
                List<Creature>? l;
                if (!kinds.TryGet(name, out l))
                    kinds = kinds.Set(name, l = new List<Creature>());
                l.Add(c);
            }
        }

        if (kinds.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("# Creatures seen");
            List<string> order = new(kinds.Keys);
            order.Sort((a, b) => kinds[b]!.Count != kinds[a]!.Count ? kinds[b]!.Count.CompareTo(kinds[a]!.Count)
                : string.CompareOrdinal(a, b));
            foreach (string name in order)
            {
                List<Creature> l = kinds[name]!;
                l.Sort((a, b) => b.Last.CompareTo(a.Last));
                int died = 0;
                foreach (Creature c in l)
                    if (c.Died)
                        died++;
                sb.AppendLine();
                sb.AppendLine("## " + name + ": " + l.Count + " seen" + (died > 0 ? ", " + died + " died" : ""));
                for (int i = 0; i < l.Count && i < PlacesShown; i++)
                {
                    Creature c = l[i];
                    sb.AppendLine("- " + c.Last.ToString("HH:mm") + " at " + c.X + "," + c.Y
                        + (c.X != c.X0 || c.Y != c.Y0
                        ? " (first at " + c.X0 + "," + c.Y0 + " " + c.First.ToString("HH:mm") + ")" : "")
                        + (c.Died ? ", died" : ""));
                }
                if (l.Count > PlacesShown)
                    sb.AppendLine("- and " + (l.Count - PlacesShown) + " more");
            }
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PeopleReportPath)!);
            File.WriteAllText(PeopleReportPath, sb.ToString());
        }
        catch (Exception e) { Note("People report not written: " + e.Message); }
    }

    // ---------- resource counts ----------
    public string Counts()
    {
        GameLink k = m_link;
        if (k == null)
            return "no game connection";
        ValueMap<string, int> sums = ValueMap<string, int>.Empty;
        int[] reagents = Reagents();
        lock (k.State.Gate)
        {
            Set<uint> inside = Set<uint>.Empty;
            if (k.State.Backpack != 0)
                inside = inside.Add(k.State.Backpack);
            for (int pass = 0; pass < 8; pass++)
                foreach (KeyValuePair<uint, AssistItem> kv in k.State.Items)
                    if (inside.Contains(kv.Value.Container))
                        inside = inside.Add(kv.Key);
            foreach (KeyValuePair<uint, AssistItem> kv in k.State.Items)
            {
                AssistItem it = kv.Value;
                if (it.Layer >= 0 || !inside.Contains(it.Container))
                    continue;
                foreach (string[] g in s_countGroups)
                    foreach (string h in g[1].Split(','))
                        if (Convert.ToInt32(h, 16) == it.Graphic)
                            sums = sums.Set(g[0], sums.Get(g[0], 0) + it.Amount);
                if (Array.IndexOf(reagents, it.Graphic) >= 0)
                    sums = sums.Set("reagents", sums.Get("reagents", 0) + it.Amount);
            }
        }
        StringBuilder sb = new();
        foreach (string[] g in s_countGroups)
            sb.Append(g[0] + " " + sums.Get(g[0], 0) + " ");
        sb.Append("reagents " + sums.Get("reagents", 0));
        return sb.ToString();
    }

    // RunUO Scripts/Items/Resources/Reagents, by graphic.
    static readonly Map<int, string> s_reagentNames = Map<int, string>.Of(
        (0xF7A, "black pearl"), (0xF7B, "bloodmoss"), (0xF84, "garlic"), (0xF85, "ginseng"),
        (0xF86, "mandrake root"), (0xF88, "nightshade"), (0xF8C, "sulfurous ash"), (0xF8D, "spiders' silk"));
    public static string? ReagentName(int g)
    {
        return s_reagentNames[g];
    }
    public sealed class CountRow
    {
        public string? Name = null, Group = null; public int Graphic, Count; public int[] Graphics = null!;
    }
    // The counted list (Damian, 2026-10-15: "add the same for the resources, a picker and a set list"): settings key
    // "resources", entries name=hex[,hex][@group] joined by |, "-" for an empty list; unset, the count groups and the
    // reagents. An entry with no @group takes GroupOf its first graphic.
    public List<CountRow> ResourceList()
    {
        List<CountRow> list = new();
        string s = Cfg.Get("resources", "");
        if (s.Length == 0)
        {
            foreach (string[] g in s_countGroups)
                list.Add(new CountRow { Name = g[0], Group = g[2],
                    Graphics = Array.ConvertAll(g[1].Split(','), h => Convert.ToInt32(h, 16)) });
            foreach (int r in Reagents())
                list.Add(new CountRow { Name = ReagentName(r) ?? r.ToString("X4"), Group = "reagents",
                    Graphics = new[] { r } });
        }
        else if (s != "-")
        {
            foreach (string part in s.Split('|'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0)
                    continue;
                string? value = part.Substring(eq + 1), group = null;
                int at = value.IndexOf('@');
                if (at >= 0)
                {
                    group = value.Substring(at + 1);
                    value = value.Substring(0, at);
                    if (s_resourceGroups.IndexOf(group) < 0)
                        group = null;
                }
                try
                {
                    list.Add(new CountRow { Name = part.Substring(0, eq), Group = group,
                        Graphics = Array.ConvertAll(value.Split(','), h => Convert.ToInt32(h, 16)) });
                }
                catch (FormatException) { }
            }
        }
        // Ore shows the solid pile 0x19B8 (Damian, 2026-10-17: "the art with the more solid pile would be better").
        foreach (CountRow r in list)
        {
            r.Graphic = Array.IndexOf(r.Graphics, 0x19B8) >= 0 ? 0x19B8 : r.Graphics[0];
            if (r.Group == null)
                r.Group = GroupOf(r.Graphics[0]);
        }
        return list;
    }
    public void SetResources(List<CountRow> list)
    {
        if (list.Count == 0)
        {
            Cfg.Set("resources", "-");
            return;
        }
        Cfg.Set("resources",
            string.Join("|",
            list.ConvertAll(r =>
            r.Name!.Replace("|", " ").Replace("=", " ").Replace("@", " ") + "="
            + string.Join(",", Array.ConvertAll(r.Graphics, g => g.ToString("X"))) + "@"
            + (r.Group ?? GroupOf(r.Graphics[0])))));
    }
    // One row per counted entry, summed over the backpack and its containers.
    public List<CountRow> CountRows()
    {
        List<CountRow> rows = ResourceList();
        GameLink k = m_link;
        if (k == null)
            return rows;
        lock (k.State.Gate)
        {
            Set<uint> inside = Set<uint>.Empty;
            if (k.State.Backpack != 0)
                inside = inside.Add(k.State.Backpack);
            for (int pass = 0; pass < 8; pass++)
                foreach (KeyValuePair<uint, AssistItem> kv in k.State.Items)
                    if (inside.Contains(kv.Value.Container))
                        inside = inside.Add(kv.Key);
            foreach (KeyValuePair<uint, AssistItem> kv in k.State.Items)
            {
                AssistItem it = kv.Value;
                if (it.Layer >= 0 || !inside.Contains(it.Container))
                    continue;
                foreach (CountRow r in rows)
                    if (Array.IndexOf(r.Graphics, it.Graphic) >= 0)
                        r.Count += it.Amount;
            }
        }
        return rows;
    }
    // Graphics the player can see now: carried (in the backpack or worn) first, then on the ground nearby.
    public Set<int> SeenGraphics(bool carried)
    {
        Set<int> seen = Set<int>.Empty;
        GameLink k = m_link;
        if (k == null)
            return seen;
        lock (k.State.Gate)
        {
            if (carried)
                foreach (KeyValuePair<uint, AssistItem> kv in k.State.Items)
                    seen = seen.Add(kv.Value.Graphic);
            else
                foreach (KeyValuePair<uint, AssistItem> kv in k.State.Ground)
                    seen = seen.Add(kv.Value.Graphic);
        }
        return seen;
    }

    // ---------- skills and stats advice ----------
    int Value(GameLink k, AdviceRow a)
    {
        lock (k.State.Gate)
        {
            if (a.Strength)
                return k.State.Str;
            return k.State.Skills.Get(a.Skill + 1, 0);
        }
    }
    int Threshold(AdviceRow a)
    {
        return Cfg.GetInt(Server + "advice." + a.Key, a.At);
    }
    bool Known(GameLink k, AdviceRow a)
    {
        lock (k.State.Gate)
            return a.Strength ? k.State.Str > 0 : k.State.Skills.ContainsKey(a.Skill + 1);
    }
    void CheckAdvice(GameLink k)
    {
        foreach (AdviceRow a in s_advice)
        {
            if (!Known(k, a))
                continue;
            int v = Value(k, a);
            int before;
            lock (m_seenSkillsGate)
            {
                if (!m_seen.TryGet(a.Key, out before))
                {
                    m_seen = m_seen.Set(a.Key, v);
                    continue;
                }
                m_seen = m_seen.Set(a.Key, v);
            }
            int at = Threshold(a);
            if (before < at && v >= at)
                Note("Advice: " + a.Text + " [" + a.Source + "]");
        }
    }
    // RunUO cliloc 500213 after an equip: the threshold for that item's row is wrong for this server.
    void CheckStrengthRefusal(GameLink k)
    {
        List<string> j = k.State.JournalSince(k.State.JournalSeq - 1);
        if (j.Count == 0 || j[0].IndexOf("not strong enough to equip", StringComparison.OrdinalIgnoreCase) < 0)
            return;
        if ((Clock.UtcNow - m_lastEquipAt).TotalSeconds > 5)
            return;
        AssistItem? it = Item(k, m_lastEquip);
        if (it == null)
            return;
        int str;
        lock (k.State.Gate)
            str = k.State.Str;
        foreach (AdviceRow a in s_advice)
        {
            if (a.Strength && Array.IndexOf(a.Graphics, it.Graphic) >= 0 && Threshold(a) <= str)
            {
                Cfg.Set(Server + "advice." + a.Key, (str + 1).ToString());
                Note("Advice corrected: this server refused " + it.Graphic.ToString("X4") + " at strength " + str
                    + "; " + a.Key + " is now " + (str + 1));
            }
        }
    }
    public string AdviceNow()
    {
        GameLink k = m_link;
        if (k == null)
            return "no game connection";
        StringBuilder sb = new();
        foreach (AdviceRow a in s_advice)
        {
            int v = Value(k, a), at = Threshold(a);
            sb.Append((v >= at ? "yes " : "not yet ") + a.Key + " ("
                + (a.Strength ? v.ToString() : (v / 10.0).ToString("0.0")) + "/"
                + (a.Strength ? at.ToString() : (at / 10.0).ToString("0.0")) + ") | ");
        }
        return sb.ToString().TrimEnd(' ', '|');
    }

    // ---------- display options ----------
    public bool Shown(string name)
    {
        return Cfg.Get("title." + name, s_titleDefaults.IndexOf(name) >= 0 ? "on" : "off") == "on";
    }
    public void Show(string name, bool on)
    {
        Cfg.Set("title." + name, on ? "on" : "off");
    }
}
